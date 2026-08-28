// ============================================================================
// DailyReportPersistence.cs - 日报存档管线（P0 步骤 2）
// ============================================================================
// 硬约束（形态照 PetNest/PetNestPersistence.cs，语义按单 key 收敛）：
//   - `SavesSystem.Save<string>` **JSON 整存**，不用 typed `Save<T>`：ES3 会把
//     assembly-qualified 类型名写进存档，mod 程序集改名/重构就会让老档读不回来；
//     整存字符串把这层耦合切断。
//   - `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` **幂等订阅**且成对退订。
//   - 写屏障：未知/更高 schemaVersion、payload 不可读时只读不写，**绝不覆盖该 key**。
//   - 战斗中不写盘：Store 只入队 pending，物理落盘统一由 DailyReportSaveCoordinator 触发。
//   - 全程 no-throw：存档路径异常不得拖崩宿主。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>日报单 key 存档门面。</summary>
    internal static class DailyReportPersistence
    {
        #region 状态

        private static readonly object _lock = new object();

        private static DailyReportData _cache;
        private static string _pendingJson;
        private static bool _pendingActive;
        private static bool _writeBarrier;
        private static bool _storeFaulted;
        private static string _lastError;

        private static readonly object _subscriptionLock = new object();
        private static bool _subscribed;

        #endregion

        #region 只读查询

        /// <summary>是否已订阅官方存档事件。</summary>
        internal static bool IsSubscribed { get { return _subscribed; } }

        /// <summary>单向故障：写入路径出过异常之后不再尝试写。</summary>
        internal static bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>写屏障：未知版本 / 不可读 payload，只读不写。</summary>
        internal static bool HasWriteBarrier { get { lock (_lock) { return _writeBarrier; } } }

        /// <summary>是否有待落盘批次。</summary>
        internal static bool HasPendingWrite
        {
            get { lock (_lock) { return _pendingActive && _pendingJson != null; } }
        }

        /// <summary>最后一次失败原因（诊断用）。</summary>
        internal static string LastError { get { return _lastError; } }

        #endregion

        #region 订阅（幂等）

        /// <summary>幂等订阅官方存档事件。模块 bootstrap 调一次。</summary>
        internal static void EnsureSubscribed()
        {
            lock (_subscriptionLock)
            {
                if (_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData += HandleCollectSaveData;
                    SavesSystem.OnSetFile += HandleSetFile;
                    SavesSystem.OnSaveDeleted += HandleSaveDeleted;
                    _subscribed = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 存档订阅失败: " + e.Message);
                }
            }
        }

        /// <summary>幂等退订。宿主销毁 / 开关关闭时调用。</summary>
        internal static void ShutdownSubscription()
        {
            lock (_subscriptionLock)
            {
                if (!_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData -= HandleCollectSaveData;
                    SavesSystem.OnSetFile -= HandleSetFile;
                    SavesSystem.OnSaveDeleted -= HandleSaveDeleted;
                }
                catch (Exception)
                {
                    // 退订失败也要把 _subscribed 置回 false，避免重复订阅越滚越多
                }
                _subscribed = false;
            }
        }

        /// <summary>
        /// 官方存盘前的采集点。这里做两件事：
        ///   1) 把运行时的当天余数同步进 DTO（借官方存盘顺带写，零额外 IO）；
        ///   2) 把 pending 合并进 ES3 缓存，但**不单独** SaveFile（那是协调器的职责）。
        /// </summary>
        private static void HandleCollectSaveData()
        {
            try
            {
                DailyReportService.SyncCarrySecondsToPersistence();
                FlushPending();
            }
            catch (Exception)
            {
                // no-throw：存档收集路径不得抛
            }
        }

        private static void HandleSetFile()
        {
            try
            {
                ResetForSlotChange();
                DailyReportSaveCoordinator.NotifySlotChanged();
                DailyReportService.NotifySlotChanged();
            }
            catch (Exception)
            {
                // no-throw：切档回调不得抛，异常时保持"已重置"状态即可
            }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                ResetForSlotChange();
                DailyReportSaveCoordinator.NotifySlotChanged();
                DailyReportService.NotifySlotChanged();
            }
            catch (Exception)
            {
                // no-throw：删档回调不得抛
            }
        }

        #endregion

        #region 加载 / 入队

        /// <summary>加载或初始化。幂等：已有缓存直接返回。</summary>
        internal static DailyReportData LoadOrInit()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;

                bool keyExists;
                try
                {
                    keyExists = SavesSystem.KeyExisits(DailyReportTuning.StorageKey);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "key_classification_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 存档 key 分类失败，进入写屏障");
                    _cache = DailyReportCodec.CreateDefault();
                    return _cache;
                }

                if (!keyExists)
                {
                    _cache = DailyReportCodec.CreateDefault();
                    return _cache;
                }

                string raw;
                try
                {
                    raw = SavesSystem.Load<string>(DailyReportTuning.StorageKey);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "payload_load_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 存档读取失败，进入写屏障");
                    _cache = DailyReportCodec.CreateDefault();
                    return _cache;
                }

                int version = DailyReportCodec.ReadSchemaVersion(raw);
                if (version != DailyReportTuning.CurrentSchemaVersion)
                {
                    // 高版本 fail-closed 只读；低版本目前没有迁移路径，同样只读不覆盖。
                    _writeBarrier = true;
                    _lastError = "schema_mismatch:" + version;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 存档 schemaVersion="
                        + version + " 与当前 " + DailyReportTuning.CurrentSchemaVersion
                        + " 不符，只读不覆盖");
                    _cache = DailyReportCodec.CreateDefault();
                    return _cache;
                }

                DailyReportData decoded = DailyReportCodec.Decode(raw);
                if (decoded == null)
                {
                    _writeBarrier = true;
                    _lastError = "decode_failed";
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 存档解码失败，进入写屏障");
                    _cache = DailyReportCodec.CreateDefault();
                    return _cache;
                }

                _cache = decoded;
                return _cache;
            }
        }

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal static DailyReportData Current { get { return LoadOrInit(); } }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 DailyReportSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(DailyReportData value)
        {
            if (value == null) return false;
            if (_storeFaulted) return false;
            if (HasWriteBarrier) return false;

            try
            {
                value.LastUpdatedTicks = DateTime.UtcNow.Ticks;
                string json = DailyReportCodec.Encode(value);
                if (json == null) return false;

                lock (_lock)
                {
                    _cache = value;
                    // 至多一个 pending：合并覆盖，不叠加
                    _pendingJson = json;
                    _pendingActive = true;
                }
                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                _lastError = "encode_failed:" + e.GetType().Name;
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                    + "[ERROR] 存档编码异常，进入 StoreFaulted: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 把 pending 写进 ES3 缓存。IsSaving 时返回 false 并保留 pending（由协调器重试）。
        /// 不在这里调 SaveFile：那是协调器的唯一职责。
        /// </summary>
        internal static bool FlushPending()
        {
            lock (_lock)
            {
                if (!_pendingActive || _pendingJson == null) return true;
                if (_writeBarrier)
                {
                    _pendingActive = false;
                    _pendingJson = null;
                    return true;
                }

                try
                {
                    if (SavesSystem.IsSaving)
                    {
                        _lastError = "flush_deferred_is_saving";
                        return false;
                    }

                    SavesSystem.Save<string>(DailyReportTuning.StorageKey, _pendingJson);

                    // 回读核对：写进去的字符串必须能原样读回来
                    string readback = SavesSystem.Load<string>(DailyReportTuning.StorageKey);
                    if (!string.Equals(readback, _pendingJson, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("daily report save readback mismatch");
                    }

                    _pendingJson = null;
                    _pendingActive = false;
                    _lastError = null;
                    return true;
                }
                catch (Exception e)
                {
                    _storeFaulted = true;
                    _lastError = "flush_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[ERROR] 存档 flush 异常，进入 StoreFaulted: " + e.Message);
                    return false;
                }
            }
        }

        #endregion

        #region 清理

        /// <summary>切档 / 删档：丢弃内存状态，从新档重新加载。故障标记不清（跨槽保守）。</summary>
        private static void ResetForSlotChange()
        {
            lock (_lock)
            {
                _cache = null;
                _pendingJson = null;
                _pendingActive = false;
                _writeBarrier = false;
                _lastError = null;
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
            lock (_lock)
            {
                _cache = null;
                _pendingJson = null;
                _pendingActive = false;
                _writeBarrier = false;
                _storeFaulted = false;
                _lastError = null;
            }
        }

        #endregion
    }
}

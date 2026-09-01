// ============================================================================
// DailyReportPersistence.cs - 日报存档管线（P0 步骤 2）
// ============================================================================
// 硬约束（形态照 PetNest/PetNestPersistence.cs，语义按单 key 收敛）：
//   - `SavesSystem.Save<string>` **JSON 整存**，不用 typed `Save<T>`：ES3 会把
//     assembly-qualified 类型名写进存档，mod 程序集改名/重构就会让老档读不回来；
//     整存字符串把这层耦合切断。
//   - `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` **幂等订阅**且成对退订。
//   - 缓存带槽位烙印：`LoadOrInit` 命中缓存也要比对 `SavesSystem.CurrentSlot`，
//     不一致就自失效重载并复位运行时。退订之后（开关关闭）换档没有任何回调，
//     只靠 `OnSetFile` 会让上一个槽的数据被写进新档，因此校验必须在读取侧。
//   - 写屏障：未知/更高 schemaVersion、payload 不可读时只读不写，**绝不覆盖该 key**。
//   - 战斗中不写盘：Store 只入队 pending，物理落盘统一由 DailyReportSaveCoordinator 触发，
//     且协调器只在基地场景真正 SaveFile（宿主销毁与关停例外，见其头注释）。
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

        /// <summary>
        /// `_cache` 归属的存档槽位。`SlotUnknown` = 未加载 / 槽位读不到。
        /// 存在的唯一理由：`ShutdownSubscription` 会退订 OnSetFile（开关关闭时），
        /// 此后玩家在主菜单换档没有任何回调会重置缓存，再打开开关就会把上一个槽
        /// 的日报 JSON 写进新槽。加了这个字段之后，槽位校验发生在读取侧，
        /// **无论订阅是否还在**都安全。
        /// </summary>
        private static int _cacheSlot = SlotUnknown;

        /// <summary>槽位不可知的哨兵值（读 CurrentSlot 抛异常时用）。</summary>
        private const int SlotUnknown = int.MinValue;

        private static string _pendingJson;
        private static bool _pendingActive;
        private static bool _writeBarrier;
        private static bool _storeFaulted;
        private static string _lastError;
        private static bool _validationRejectStore;

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
            bool becameSubscribed = false;
            lock (_subscriptionLock)
            {
                if (_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData += HandleCollectSaveData;
                    SavesSystem.OnSetFile += HandleSetFile;
                    SavesSystem.OnSaveDeleted += HandleSaveDeleted;
                    _subscribed = true;
                    becameSubscribed = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 存档订阅失败: " + e.Message);
                }
            }

            // 复位通知放在订阅锁外，避免与运行时侧的锁产生嵌套顺序。
            if (becameSubscribed) DiscardCacheOnResubscribe();
        }

        /// <summary>
        /// 重新挂上监听时丢弃上一段「无人监听」期间可能已经过期的缓存。
        ///
        /// 槽位烙印（见 LoadOrInit）挡得住换档，但挡不住**同槽删档重开**：
        /// 官方 `DeleteCurrentSave` 删的就是当前槽，`CurrentSlot` 不变，
        /// 槽号比对看不出区别。而 dormant 期间没有任何回调能告诉我们发生过什么，
        /// 重新订阅这一刻是唯一确定的重新对齐点。
        ///
        /// 代价是一次重读；首次订阅（无缓存）直接早返，正常开局零成本。
        /// 关停路径已经先做过一次 bypassSceneGate 的最后落盘，此处不再有可救的进度。
        /// </summary>
        private static void DiscardCacheOnResubscribe()
        {
            bool hadCache;
            lock (_lock)
            {
                hadCache = _cache != null;
            }
            if (!hadCache) return;

            ResetForSlotChange();
            ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                + "重新订阅存档事件，dormant 期间的缓存已丢弃并将从当前槽重读");
            NotifySlotDrift();
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

        /// <summary>
        /// 加载或初始化。幂等：缓存命中且**槽位一致**时直接返回。
        /// 槽位不一致说明中途换过档（典型是开关关闭期间退订了 OnSetFile），
        /// 此时缓存与 pending 全部作废并从新槽重读，避免把上一个槽的数据写进新档。
        /// </summary>
        internal static DailyReportData LoadOrInit()
        {
            bool slotDrifted = false;
            DailyReportData loaded = LoadOrInitCore(ref slotDrifted);

            // 槽位漂移的复位通知放在锁外：运行时侧（协调器 / Service）各有自己的锁，
            // 在持久层锁内回调会引入跨锁顺序。
            if (slotDrifted)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                    + "[WARNING] 检测到存档槽位已变更但未收到切档回调，日报缓存已自失效并从新槽重载");
                NotifySlotDrift();
            }
            return loaded;
        }

        /// <summary>LoadOrInit 的锁内主体。slotDrifted 回报是否命中了槽位漂移。</summary>
        private static DailyReportData LoadOrInitCore(ref bool slotDrifted)
        {
            lock (_lock)
            {
                int slot = ReadCurrentSlotSafe();

                if (_cache != null)
                {
                    // 绝大多数调用走这条：一次 int 比较，无 IO 无分配。
                    if (_cacheSlot == slot) return _cache;
                    slotDrifted = true;
                    ResetForSlotChange();
                }

                _cacheSlot = slot;

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
        /// no-throw 读当前槽位。官方 `SavesSystem.CurrentSlot` 只是一个静态可空 int 的
        /// 读取（首次访问才读 PlayerPrefs），因此放在读取热路径上没有代价。
        /// 读不到时返回哨兵值：此后哨兵与哨兵自比一致，不会反复自失效。
        /// </summary>
        private static int ReadCurrentSlotSafe()
        {
            try
            {
                return SavesSystem.CurrentSlot;
            }
            catch (Exception)
            {
                return SlotUnknown;
            }
        }

        /// <summary>
        /// 槽位漂移后的运行时复位。与 HandleSetFile 同一组下游，
        /// 保证「数据换了、计时也换」——否则新槽会继承上一个槽的当日余数。
        /// </summary>
        private static void NotifySlotDrift()
        {
            try
            {
                DailyReportSaveCoordinator.NotifySlotChanged();
                DailyReportService.NotifySlotChanged();
            }
            catch (Exception)
            {
                // no-throw：复位失败也不能拖崩读取路径
            }
        }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 DailyReportSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(DailyReportData value)
        {
            if (value == null) return false;
            if (_validationRejectStore && ModBehaviour.DevModeEnabled) return false;
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

        internal static void SetValidationRejectStore(bool reject)
        {
            if (!ModBehaviour.DevModeEnabled) return;
            _validationRejectStore = reject;
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
                _cacheSlot = SlotUnknown;
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
                _cacheSlot = SlotUnknown;
                _pendingJson = null;
                _pendingActive = false;
                _writeBarrier = false;
                _storeFaulted = false;
                _validationRejectStore = false;
                _lastError = null;
            }
        }

        #endregion
    }
}

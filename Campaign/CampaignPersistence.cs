// ============================================================================
// CampaignPersistence.cs - 鸭王征程存档管线
// ============================================================================
// 形态照 Integration/Codex/CodexPersistence.cs 与 ModeG/ModeGProfilePersistence.cs，
// 四条纪律一条不少：
//   - `SavesSystem.Save<string>` **JSON 整存**，不用 typed `Save<T>`：ES3 会把
//     assembly-qualified 类型名写进存档，mod 程序集改名就让老档读不回来。
//   - `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` **幂等订阅**且成对退订，
//     一律用命名方法（AGENTS.md 4.6，lambda 退订不掉）。
//   - **缓存带槽位烙印**：命中缓存也要比对 CurrentSlot。开关关闭会退订 OnSetFile，
//     此后玩家换档没有任何回调，只靠事件会把 A 档的章节进度写进 B 档。
//   - **写屏障**：未知 schemaVersion、payload 不可读时只读不写，绝不覆盖该 key。
//     战役进度是几十小时的剧情推进，覆盖比丢一次记录严重得多。
//
// DTO 遵循 ModeG 纪律：`[Serializable]` 且**禁字段初始化器**（ES3/JsonUtility 反序列化
// 时字段初始化器与反序列化赋值的先后顺序不可靠，写了会掩盖「存档里真的没这个字段」）。
// ============================================================================

using System;
using System.Collections.Generic;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>单章存档记录。禁字段初始化器。</summary>
    [Serializable]
    internal class CampaignChapterRecord
    {
        /// <summary>章节 ID。</summary>
        public string chapterId;

        /// <summary>CampaignChapterState 的整数值。</summary>
        public int state;
    }

    /// <summary>战役存档根对象。禁字段初始化器。</summary>
    [Serializable]
    internal class CampaignSaveData
    {
        public int schemaVersion;
        public CampaignChapterRecord[] chapters;
        public string[] grantedTokens;
        public string[] unlockedClues;
        public long lastUpdatedTicks;
    }

    /// <summary>战役单 key 存档门面。</summary>
    internal static class CampaignPersistence
    {
        #region 常量

        /// <summary>当前 schema 版本。改动 DTO 结构必须同步递增并想清楚老档怎么办。</summary>
        internal const int CurrentSchemaVersion = 1;

        private const int SlotUnknown = int.MinValue;

        #endregion

        #region 状态

        private static readonly object _lock = new object();
        private static readonly object _subscriptionLock = new object();

        private static CampaignSaveData _cache;
        private static int _cacheSlot = SlotUnknown;
        private static string _pendingJson;
        private static bool _pendingActive;
        private static bool _writeBarrier;
        private static bool _storeFaulted;
        private static string _lastError;
        private static bool _subscribed;

        #endregion

        #region 只读查询

        internal static bool IsSubscribed { get { return _subscribed; } }

        internal static bool IsStoreFaulted { get { return _storeFaulted; } }

        internal static bool HasWriteBarrier { get { lock (_lock) { return _writeBarrier; } } }

        internal static bool HasPendingWrite
        {
            get { lock (_lock) { return _pendingActive && _pendingJson != null; } }
        }

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
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 存档订阅失败: " + e.Message);
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
                    // 退订失败也要把标记置回，避免重复订阅越滚越多
                }
                _subscribed = false;
            }
        }

        private static void HandleCollectSaveData()
        {
            try
            {
                FlushPending();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 存档收集回调失败: " + e.Message);
            }
        }

        private static void HandleSetFile()
        {
            try
            {
                ResetForSlotChange();
                NotifySlotChangedDownstream();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 换槽回调失败: " + e.Message);
            }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                ResetForSlotChange();
                NotifySlotChangedDownstream();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 删档回调失败: " + e.Message);
            }
        }

        /// <summary>
        /// 换槽/删档后的下游复位。**解锁契约必须一起复位**，
        /// 否则 A 档已解锁的后山设施会在 B 档继续可见。
        /// </summary>
        private static void NotifySlotChangedDownstream()
        {
            try
            {
                CampaignSaveCoordinator.NotifySlotChanged();
                CampaignFacilityUnlocks.ResetForSlotReload();
                CampaignProgressService.NotifySlotChanged();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 换槽下游通知失败: " + e.Message);
            }
        }

        #endregion

        #region 加载

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal static CampaignSaveData Current { get { return LoadOrInit(); } }

        /// <summary>
        /// 加载或初始化。幂等：缓存命中且**槽位一致**时直接返回。
        /// 槽位不一致说明中途换过档（典型是开关关闭期间退订了 OnSetFile），
        /// 缓存与 pending 全部作废并从新槽重读。
        /// </summary>
        internal static CampaignSaveData LoadOrInit()
        {
            bool slotDrifted = false;
            CampaignSaveData loaded = LoadOrInitCore(ref slotDrifted);

            if (slotDrifted)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix
                    + "[WARNING] 检测到存档槽位已变更但未收到切档回调，战役缓存已自失效并从新槽重载");
                NotifySlotChangedDownstream();
            }
            return loaded;
        }

        private static CampaignSaveData LoadOrInitCore(ref bool slotDrifted)
        {
            lock (_lock)
            {
                int slot = ReadCurrentSlotSafe();

                if (_cache != null)
                {
                    if (_cacheSlot == slot) return _cache;
                    slotDrifted = true;
                    ResetForSlotChangeUnlocked();
                }

                _cacheSlot = slot;

                bool keyExists;
                try
                {
                    // 官方拼写就是 KeyExisits（少一个 t），不是笔误
                    keyExists = SavesSystem.KeyExisits(CampaignTuning.ProgressSaveKey);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "key_classification_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 存档 key 分类失败，进入写屏障");
                    _cache = CreateDefault();
                    return _cache;
                }

                if (!keyExists)
                {
                    _cache = CreateDefault();
                    return _cache;
                }

                string raw;
                try
                {
                    raw = SavesSystem.Load<string>(CampaignTuning.ProgressSaveKey);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "payload_load_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 存档读取失败，进入写屏障");
                    _cache = CreateDefault();
                    return _cache;
                }

                CampaignSaveData decoded = Decode(raw);
                if (decoded == null)
                {
                    _writeBarrier = true;
                    _lastError = "decode_failed";
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 存档解码失败，进入写屏障");
                    _cache = CreateDefault();
                    return _cache;
                }

                if (decoded.schemaVersion != CurrentSchemaVersion)
                {
                    // 高版本 fail-closed 只读；低版本暂无迁移路径，同样只读不覆盖
                    _writeBarrier = true;
                    _lastError = "schema_mismatch:" + decoded.schemaVersion;
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 存档 schemaVersion="
                        + decoded.schemaVersion + " 与当前 " + CurrentSchemaVersion + " 不符，只读不覆盖");
                    _cache = CreateDefault();
                    return _cache;
                }

                _cache = decoded;
                return _cache;
            }
        }

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

        #endregion

        #region 写入

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 CampaignSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(CampaignSaveData value)
        {
            if (value == null) return false;
            if (_storeFaulted) return false;
            if (HasWriteBarrier) return false;

            try
            {
                value.schemaVersion = CurrentSchemaVersion;
                value.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                string json = Encode(value);
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
                ModBehaviour.DevLog(CampaignTuning.LogPrefix
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

                    SavesSystem.Save<string>(CampaignTuning.ProgressSaveKey, _pendingJson);

                    // 回读核对：写进去的字符串必须能原样读回来
                    string readback = SavesSystem.Load<string>(CampaignTuning.ProgressSaveKey);
                    if (!string.Equals(readback, _pendingJson, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("campaign save readback mismatch");
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
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix
                        + "[ERROR] 存档 flush 异常，进入 StoreFaulted: " + e.Message);
                    return false;
                }
            }
        }

        #endregion

        #region 编解码

        /// <summary>默认存档：全章未解锁，由 ProgressService 负责把第 1 章开出来。</summary>
        internal static CampaignSaveData CreateDefault()
        {
            CampaignSaveData data = new CampaignSaveData();
            data.schemaVersion = CurrentSchemaVersion;
            data.chapters = new CampaignChapterRecord[0];
            data.grantedTokens = new string[0];
            data.unlockedClues = new string[0];
            data.lastUpdatedTicks = 0L;
            return data;
        }

        private static string Encode(CampaignSaveData value)
        {
            try
            {
                return JsonUtility.ToJson(value);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static CampaignSaveData Decode(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw)) return null;
                CampaignSaveData decoded = JsonUtility.FromJson<CampaignSaveData>(raw);
                if (decoded == null) return null;

                // JsonUtility 对缺失数组给 null，下游一律按非 null 消费
                if (decoded.chapters == null) decoded.chapters = new CampaignChapterRecord[0];
                if (decoded.grantedTokens == null) decoded.grantedTokens = new string[0];
                if (decoded.unlockedClues == null) decoded.unlockedClues = new string[0];
                return decoded;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>把已授予 token 灌进跨系统契约。读档路径专用，不触发授予事件。</summary>
        internal static void PublishTokensToUnlockContract(CampaignSaveData data)
        {
            try
            {
                List<string> tokens = new List<string>();
                if (data != null && data.grantedTokens != null)
                {
                    for (int i = 0; i < data.grantedTokens.Length; i++)
                    {
                        string token = data.grantedTokens[i];
                        if (string.IsNullOrEmpty(token)) continue;
                        tokens.Add(token);
                    }
                }
                CampaignFacilityUnlocks.LoadGrantedTokens(tokens);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 发布 token 到解锁契约失败: " + e.Message);
            }
        }

        #endregion

        #region 清理

        /// <summary>切档 / 删档：丢弃内存状态，从新档重新加载。故障标记不清（跨槽保守）。</summary>
        private static void ResetForSlotChange()
        {
            lock (_lock)
            {
                ResetForSlotChangeUnlocked();
            }
        }

        private static void ResetForSlotChangeUnlocked()
        {
            _cache = null;
            _cacheSlot = SlotUnknown;
            _pendingJson = null;
            _pendingActive = false;
            _writeBarrier = false;
            _lastError = null;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
            lock (_lock)
            {
                ResetForSlotChangeUnlocked();
                _storeFaulted = false;
            }
        }

        #endregion
    }
}

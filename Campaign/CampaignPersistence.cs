// ============================================================================
// CampaignPersistence.cs - 鸭王征程存档管线（门面）
// ============================================================================
// 单 key JSON 整存的状态机（幂等订阅 / 槽位烙印 / 写屏障 / 回读核对 / pending 入队）
// 自 2026-09-06 起只有一份实现：Common/Lifecycle/BossRushSlotJsonStore.cs。
// 本文件只保留战役自己的绑定：DTO、存档 key、schema、共享 JSON 编解码、盖章、
// 官方采集前置步骤（采集现金快照）、下游复位通知、token 发布，
// 以及 CampaignPersistence.* 不变的调用面。
//
// 战役特有的约束：
//   - 战役进度是几十小时的剧情推进，覆盖比丢一次记录严重得多：未知 schemaVersion、
//     payload 不可读时只读不写（共享实现保证）；
//   - 换槽 / 删档后的下游复位**必须包含解锁契约**（CampaignFacilityUnlocks），
//     否则 A 档已解锁的后山设施会在 B 档继续可见。
//
// DTO 遵循 ModeG 纪律：`[Serializable]` 且**禁字段初始化器**（ES3/JsonUtility 反序列化
// 时字段初始化器与反序列化赋值的先后顺序不可靠，写了会掩盖「存档里真的没这个字段」）。
// ============================================================================

using System;
using System.Collections.Generic;
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
        /// <summary>一次性新内容引导已完成的稳定 ID（SCHEMA+，旧档缺失时按空集）。</summary>
        public string[] completedGuides;
        public string[] acceptedGuides;
        public string[] experiencedGuides;
        public long lastUpdatedTicks;
    }

    /// <summary>战役单 key 存档门面。</summary>
    internal static class CampaignPersistence
    {
        #region 常量

        /// <summary>
        /// 当前 schema 版本。completedGuides 是可选扩展字段，保持 v1 以便旧战役档继续可读；
        /// 显式解码兼容旧版缺失引导数组；缺失章节数组另按已有交付 token 恢复。
        /// </summary>
        internal const int CurrentSchemaVersion = 1;

        #endregion

        private static BossRushSlotJsonStore<CampaignSaveData> _store = CreateStore();
        private static float _nextRecoveryAt;
        private static int _loadedSlot = int.MinValue;

        private static BossRushSlotJsonStore<CampaignSaveData> CreateStore()
        {
            return new BossRushSlotJsonStore<CampaignSaveData>(new BossRushSlotJsonStoreSpec<CampaignSaveData>
            {
                StorageKey = CampaignTuning.ProgressSaveKey,
                SchemaVersion = CurrentSchemaVersion,
                LogPrefix = CampaignTuning.LogPrefix,
                DisplayName = "战役",
                Encode = Encode,
                ReadSchemaVersion = ReadSchemaVersion,
                Decode = Decode,
                CreateDefault = CreateDefault,
                BeforeStore = StampBeforeStore,
                NotifySlotChanged = NotifySlotChangedDownstream,
                BeforeCollectSaveData = BeforeCollectSaveData,
                AfterPendingWriteFlushed = CampaignSaveCoordinator.NotifyPendingWriteFlushed,
            });
        }

        #region 只读查询

        internal static bool IsSubscribed { get { return _store.IsSubscribed; } }

        internal static bool IsStoreFaulted { get { return _store.IsStoreFaulted; } }

        internal static bool HasWriteBarrier { get { return _store.HasWriteBarrier; } }

        internal static bool HasPendingWrite { get { return _store.HasPendingWrite; } }

        internal static string LastError { get { return _store.LastError; } }

        #endregion

        #region 订阅（幂等）

        /// <summary>幂等订阅官方存档事件。模块 bootstrap 调一次。</summary>
        internal static void EnsureSubscribed()
        {
            _store.EnsureSubscribed();
        }

        /// <summary>幂等退订。宿主销毁 / 开关关闭时调用。</summary>
        internal static void ShutdownSubscription()
        {
            _store.ShutdownSubscription();
        }

        #endregion

        #region 加载 / 写入

        /// <summary>
        /// 与天空岛序章同一时机读取槽位事实：主菜单和关卡加载期间不把尚未就绪的空缓存当新档。
        /// CurrentSlot 在主菜单就有值，它本身不能证明官方加载流程已经完成。
        /// </summary>
        internal static bool IsCurrentSlotReady
        {
            get
            {
                try
                {
                    return !SceneLoader.IsSceneLoading && !LevelManager.LevelInitializing
                        && LevelManager.LevelInited && Saves.SavesSystem.CurrentSlot >= 0;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>首次读取等关卡就绪；已经装载的同槽快照在过图和退出期间继续有效。</summary>
        internal static CampaignSaveData Current { get { return LoadOrInit(); } }

        /// <summary>加载或初始化。幂等：缓存命中且槽位一致时直接返回。</summary>
        internal static CampaignSaveData LoadOrInit()
        {
            int slot;
            try { slot = Saves.SavesSystem.CurrentSlot; }
            catch (Exception) { return null; }
            if (_loadedSlot != slot && !IsCurrentSlotReady) return null;
            bool firstLoad = _loadedSlot != slot;
            CampaignSaveData data = _store.LoadOrInit();
            if (data != null)
            {
                _loadedSlot = slot;
                if (firstLoad)
                    Debug.Log(CampaignTuning.LogPrefix + "CAMPAIGN_LOAD slot=" + slot + " chapters=" + data.chapters.Length
                        + " guides=" + data.acceptedGuides.Length + "/" + data.experiencedGuides.Length + "/" + data.completedGuides.Length
                        + " barrier=" + _store.HasWriteBarrier);
            }
            return data;
        }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 CampaignSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(CampaignSaveData value)
        {
            return IsCurrentSlotReady && _store.Store(value);
        }

        /// <summary>把 pending 写进 ES3 缓存。IsSaving 时返回 false 并保留 pending（由协调器重试）。</summary>
        internal static bool FlushPending()
        {
            return _store.FlushPending();
        }

        /// <summary>键写/回读的短暂失败后，校验原 key 并由新 store 接回已接受快照；不解除旧 owner 的单向故障。</summary>
        internal static bool TryRecoverFaultedStore()
        {
            if (!_store.IsStoreFaulted) return true;
            if (Saves.SavesSystem.IsSaving || Time.unscaledTime < _nextRecoveryAt) return false;
            _nextRecoveryAt = Time.unscaledTime + 1f;
            BossRushSlotJsonStore<CampaignSaveData> replacement = null;
            bool adopted = false;
            try
            {
                int slot = Saves.SavesSystem.CurrentSlot;
                CampaignSaveData accepted = CampaignProgressService.CloneSaveData(_store.Current);
                // Current 会自失效漂移的旧槽；不能把失效前的快照转给新槽。
                if (!_store.IsStoreFaulted) return true;
                if (slot < 0 || accepted == null || Decode(Encode(accepted)) == null) return false;
                replacement = CreateStore();
                // 首次订阅会丢弃既有缓存并通知下游复位，必须在接回 accepted 之前完成。
                // 否则恢复键写失败时，会把刚接回的章节和 pending 再清空，重启又回到第一章。
                if (_store.IsSubscribed) replacement.EnsureSubscribed();
                replacement.LoadOrInit();
                if (Saves.SavesSystem.CurrentSlot != slot || replacement.HasWriteBarrier || replacement.IsStoreFaulted)
                    return false;
                if (!replacement.Store(accepted)) return false;
                _store.ShutdownSubscription();
                _store = replacement;
                adopted = true;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 待保存快照恢复失败: " + e.Message);
                return false;
            }
            finally
            {
                if (!adopted && replacement != null) replacement.ShutdownSubscription();
            }
        }

        /// <summary>引导三态独立持久化：接取、体验目标、回基地交付。</summary>
        internal static bool IsGuideCompleted(string id) { return ContainsGuide(Current != null ? Current.completedGuides : null, id); }
        internal static bool IsGuideAccepted(string id) { return ContainsGuide(Current != null ? Current.acceptedGuides : null, id); }
        internal static bool IsGuideExperienced(string id) { return ContainsGuide(Current != null ? Current.experiencedGuides : null, id); }

        private static bool ContainsGuide(string[] values, string id)
        {
            if (values == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], id, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static bool TryAdvanceGuide(string id, int stage)
        {
            if (CampaignGuideTable.Find(id) == null || HasWriteBarrier || IsStoreFaulted) return false;
            CampaignSaveData current = Current;
            if (current == null || stage < 1 || stage > 3) return false;
            if (stage > 1 && !IsGuideAccepted(id)) return false;
            if (stage == 3 && !IsGuideExperienced(id)) return false;
            string[] previous = stage == 1 ? current.acceptedGuides
                : (stage == 2 ? current.experiencedGuides : current.completedGuides);
            if (ContainsGuide(previous, id)) return true;
            // 不在 Current 上先改后写：写屏障/序列化失败也不能把内存事实提前变成已完成。
            CampaignSaveData copy = CampaignProgressService.CloneSaveData(current);
            List<string> values = previous != null ? new List<string>(previous) : new List<string>();
            values.Add(id);
            if (stage == 1) copy.acceptedGuides = values.ToArray();
            else if (stage == 2) copy.experiencedGuides = values.ToArray();
            else copy.completedGuides = values.ToArray();
            if (!Store(copy)) return false;
            CampaignSaveCoordinator.RequestFlush();
            return true;
        }

        #endregion

        #region 编解码与绑定钩子

        /// <summary>默认存档：全章未解锁，由 ProgressService 负责把第 1 章开出来。</summary>
        internal static CampaignSaveData CreateDefault()
        {
            CampaignSaveData data = new CampaignSaveData();
            data.schemaVersion = CurrentSchemaVersion;
            data.chapters = new CampaignChapterRecord[0];
            data.grantedTokens = new string[0];
            data.unlockedClues = new string[0];
            data.completedGuides = new string[0];
            data.acceptedGuides = new string[0];
            data.experiencedGuides = new string[0];
            data.lastUpdatedTicks = 0L;
            return data;
        }

        /// <summary>Store 前盖 schemaVersion 与时间戳。</summary>
        private static void StampBeforeStore(CampaignSaveData value)
        {
            value.schemaVersion = CurrentSchemaVersion;
            value.lastUpdatedTicks = DateTime.UtcNow.Ticks;
        }

        // 与天空岛序章相同：显式编码字段与章节对象，不依赖 Unity 的托管 DTO 序列化。
        private static string Encode(CampaignSaveData value)
        {
            if (value == null) return null;
            var writer = new BossRushJsonWriter();
            writer.BeginObject().Int("schemaVersion", value.schemaVersion).BeginArray("chapters");
            foreach (CampaignChapterRecord record in value.chapters ?? new CampaignChapterRecord[0])
            {
                if (record == null) continue;
                writer.BeginObject().Str("chapterId", record.chapterId).Int("state", record.state).EndObject();
            }
            writer.EndArray();
            WriteStrings(writer, "grantedTokens", value.grantedTokens);
            WriteStrings(writer, "unlockedClues", value.unlockedClues);
            WriteStrings(writer, "completedGuides", value.completedGuides);
            WriteStrings(writer, "acceptedGuides", value.acceptedGuides);
            WriteStrings(writer, "experiencedGuides", value.experiencedGuides);
            string raw = writer.Long("lastUpdatedTicks", value.lastUpdatedTicks).EndObject().ToString();
            if (Decode(raw) == null) throw new InvalidOperationException("campaign_payload_invalid");
            return raw;
        }

        private static void WriteStrings(BossRushJsonWriter writer, string key, string[] values)
        {
            writer.BeginArray(key);
            foreach (string value in values ?? new string[0]) writer.ItemStr(value);
            writer.EndArray();
        }

        private static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            return root != null ? root.GetInt("schemaVersion", -1) : -1;
        }

        private static CampaignSaveData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            if (root == null || root.GetInt("schemaVersion", -1) != CurrentSchemaVersion) return null;
            CampaignSaveData decoded = CreateDefault();
            List<BossRushJsonValue> chapters;
            BossRushJsonValue chapterValue = root.GetProperty("chapters");
            if (chapterValue != null && chapterValue.Kind != BossRushJsonKind.Null)
            {
                if (!root.TryGetArray("chapters", out chapters)) return null;
                var records = new List<CampaignChapterRecord>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (BossRushJsonValue entry in chapters)
                {
                    string id;
                    int state;
                    if (entry == null || !entry.TryGetString("chapterId", out id) || string.IsNullOrEmpty(id)
                        || !entry.TryGetInt("state", out state) || !ids.Add(id)) return null;
                    records.Add(new CampaignChapterRecord { chapterId = id, state = state });
                }
                decoded.chapters = records.ToArray();
            }
            if (!ReadStrings(root, "grantedTokens", out decoded.grantedTokens)
                || !ReadStrings(root, "unlockedClues", out decoded.unlockedClues)
                || !ReadStrings(root, "completedGuides", out decoded.completedGuides)
                || !ReadStrings(root, "acceptedGuides", out decoded.acceptedGuides)
                || !ReadStrings(root, "experiencedGuides", out decoded.experiencedGuides)) return null;
            // 旧 Unity 编码在实档中漏掉 chapters，但交付 token 仍在；只恢复有已交付凭据的章节。
            // 有章节数组的新旧正常档不走这条迁移，不凭线索、日志或下一章开放状态猜测未交付进度。
            if (root.GetProperty("chapters") == null)
            {
                var recovered = new List<CampaignChapterRecord>();
                for (int order = 1; order <= 6; order++)
                    if (Array.IndexOf(decoded.grantedTokens, CampaignTuning.FacilityTokenPrefix + order) >= 0)
                        recovered.Add(new CampaignChapterRecord { chapterId = "ch" + order, state = (int)CampaignChapterState.Completed });
                decoded.chapters = recovered.ToArray();
            }
            if (root.GetProperty("lastUpdatedTicks") != null && !root.TryGetLong("lastUpdatedTicks", out decoded.lastUpdatedTicks)) return null;
            return decoded;
        }

        private static bool ReadStrings(BossRushJsonValue root, string key, out string[] values)
        {
            values = new string[0];
            BossRushJsonValue field = root.GetProperty(key);
            if (field == null || field.Kind == BossRushJsonKind.Null) return true; // 兼容旧 v1 缺字段或 null 数组。
            List<string> parsed;
            if (!root.TryGetStringList(key, out parsed)) return false;
            values = parsed.ToArray();
            return true;
        }

        /// <summary>官方存盘前的采集点：现金未就绪时本次不把 pending 交给 SavesSystem。</summary>
        private static bool BeforeCollectSaveData()
        {
            if (!CampaignSaveCoordinator.CollectPendingCash()) return false;
            return true;
        }

        /// <summary>
        /// 换槽 / 删档 / 槽位漂移后的下游复位。**解锁契约必须一起复位**，
        /// 否则 A 档已解锁的后山设施会在 B 档继续可见。
        /// </summary>
        private static void NotifySlotChangedDownstream()
        {
            _nextRecoveryAt = 0f;
            _loadedSlot = int.MinValue;
            CampaignSaveCoordinator.NotifySlotChanged();
            CampaignFacilityUnlocks.ResetForSlotReload();
            CampaignProgressService.NotifySlotChanged();
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

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            _nextRecoveryAt = 0f;
            _loadedSlot = int.MinValue;
            _store.ResetAll();
        }

        #endregion
    }
}

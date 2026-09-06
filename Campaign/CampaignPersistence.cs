// ============================================================================
// CampaignPersistence.cs - 鸭王征程存档管线（门面）
// ============================================================================
// 单 key JSON 整存的状态机（幂等订阅 / 槽位烙印 / 写屏障 / 回读核对 / pending 入队）
// 自 2026-09-06 起只有一份实现：Common/Lifecycle/BossRushSlotJsonStore.cs。
// 本文件只保留战役自己的绑定：DTO、存档 key、schema、JsonUtility 编解码、盖章、
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
        public long lastUpdatedTicks;
    }

    /// <summary>战役单 key 存档门面。</summary>
    internal static class CampaignPersistence
    {
        #region 常量

        /// <summary>当前 schema 版本。改动 DTO 结构必须同步递增并想清楚老档怎么办。</summary>
        internal const int CurrentSchemaVersion = 1;

        #endregion

        private static readonly BossRushSlotJsonStore<CampaignSaveData> _store =
            new BossRushSlotJsonStore<CampaignSaveData>(new BossRushSlotJsonStoreSpec<CampaignSaveData>
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
            });

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

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal static CampaignSaveData Current { get { return _store.Current; } }

        /// <summary>加载或初始化。幂等：缓存命中且槽位一致时直接返回。</summary>
        internal static CampaignSaveData LoadOrInit()
        {
            return _store.LoadOrInit();
        }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 CampaignSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(CampaignSaveData value)
        {
            return _store.Store(value);
        }

        /// <summary>把 pending 写进 ES3 缓存。IsSaving 时返回 false 并保留 pending（由协调器重试）。</summary>
        internal static bool FlushPending()
        {
            return _store.FlushPending();
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
            data.lastUpdatedTicks = 0L;
            return data;
        }

        /// <summary>Store 前盖 schemaVersion 与时间戳。</summary>
        private static void StampBeforeStore(CampaignSaveData value)
        {
            value.schemaVersion = CurrentSchemaVersion;
            value.lastUpdatedTicks = DateTime.UtcNow.Ticks;
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

        /// <summary>顶层 schemaVersion 由共享节点解析器读；缺失或读不动返回 -1（进写屏障）。</summary>
        private static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            return root != null ? root.GetInt("schemaVersion", -1) : -1;
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
            _store.ResetAll();
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode G 支持地图注册表（规格 §19.1 重写版）。
    ///
    /// 硬约束：
    /// - 只允许 exact scene pair（runtime sceneName + 加载用 sceneID），不支持任意地图；
    /// - 首发唯一候选：Level_DemoChallenge_1 + Level_DemoChallenge_Main；
    /// - preview 冻结 exact sceneName/sceneId，运行中离开 verified pair 即 End(SceneChanged)；
    /// - 注册表只读（首版不开放运行时 Register，防脏地图进入）。
    /// </summary>
    public static class ModeGMapSupportRegistry
    {
        #region Supported Map Entry

        /// <summary>
        /// 支持地图条目（不可变）。
        /// </summary>
        public struct SupportedMap
        {
            /// <summary>运行时场景名（SceneManager.GetActiveScene().name）</summary>
            public readonly string baseSceneName;
            /// <summary>加载用场景 ID（SceneLoader）</summary>
            public readonly string combatSceneName;
            /// <summary>双语展示名（中文原文，L10n 消费）</summary>
            public readonly string displayName;
            /// <summary>验证 revision 快照</summary>
            public readonly string verificationRevision;
            /// <summary>官方死亡行为摘要 key（双语文本由 L10n 解析）</summary>
            public readonly string deathBehavior;
            /// <summary>死亡风险双语文案 key</summary>
            public readonly string riskTextKey;
            /// <summary>玩家/Boss/环境安全三元组验证摘要</summary>
            public readonly string safetyTriad;

            public SupportedMap(string baseScene, string combatScene, string displayName, string revision,
                string deathBehavior, string riskTextKey, string safetyTriad)
            {
                this.baseSceneName = baseScene;
                this.combatSceneName = combatScene;
                this.displayName = displayName;
                this.verificationRevision = revision;
                this.deathBehavior = deathBehavior;
                this.riskTextKey = riskTextKey;
                this.safetyTriad = safetyTriad;
            }
        }

        #endregion

        #region Registry（首版只读，唯一候选）

        /// <summary>
        /// 首发唯一候选地图（§19.1）。
        /// </summary>
        private static readonly SupportedMap[] _supportedMaps =
        {
            new SupportedMap(
                "Level_DemoChallenge_1",
                "Level_DemoChallenge_Main",
                "宿命回响竞技场",
                ModeGAvailability.CurrentVerificationRevision,
                "BossRush_ModeG_OfficialDeathBehavior",
                "BossRush_ModeG_DeathRisk",
                "player-distance|boss-distance|water-zone-navmesh"),
        };

        /// <summary>
        /// 所有支持地图（只读快照）。
        /// </summary>
        public static IReadOnlyList<SupportedMap> SupportedMaps { get { return _supportedMaps; } }

        /// <summary>
        /// 当前是否有任何支持地图。
        /// </summary>
        public static bool HasAnySupportedMap { get { return _supportedMaps.Length > 0; } }

        #endregion

        #region Queries

        /// <summary>
        /// 检查指定 scene pair 是否支持（exact 匹配）。
        /// </summary>
        public static bool IsSupported(string baseScene, string combatScene)
        {
            for (int i = 0; i < _supportedMaps.Length; i++)
            {
                if (string.Equals(_supportedMaps[i].baseSceneName, baseScene, StringComparison.Ordinal)
                    && string.Equals(_supportedMaps[i].combatSceneName, combatScene, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查运行时场景名是否属于 verified pair（OnSceneLoaded 离开判定用）。
        /// </summary>
        public static bool IsVerifiedSceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            for (int i = 0; i < _supportedMaps.Length; i++)
            {
                if (string.Equals(_supportedMaps[i].baseSceneName, sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查加载用场景 ID 是否支持。
        /// </summary>
        public static bool IsCombatSceneSupported(string combatScene)
        {
            if (string.IsNullOrEmpty(combatScene)) return false;
            for (int i = 0; i < _supportedMaps.Length; i++)
            {
                if (string.Equals(_supportedMaps[i].combatSceneName, combatScene, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 首选 verified pair（preview 冻结用）。首发唯一候选；无候选返回 false。
        /// </summary>
        public static bool TryGetPrimaryVerifiedPair(out string sceneName, out string sceneId)
        {
            if (_supportedMaps.Length > 0)
            {
                sceneName = _supportedMaps[0].baseSceneName;
                sceneId = _supportedMaps[0].combatSceneName;
                return !string.IsNullOrEmpty(sceneName) && !string.IsNullOrEmpty(sceneId);
            }
            sceneName = string.Empty;
            sceneId = string.Empty;
            return false;
        }

        /// <summary>
        /// 首选地图双语展示名。
        /// </summary>
        public static string GetPrimaryDisplayName()
        {
            if (_supportedMaps.Length == 0) return string.Empty;
            return L10n.T(_supportedMaps[0].displayName, "Fate Echo Arena");
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;

namespace BossRush
{
    public enum ModeGMapSupportStatus
    {
        NotVerified = 0,
        Verified = 1
    }

    /// <summary>
    /// Mode G 支持地图注册表。
    ///
    /// 硬约束：
    /// - 只允许 exact scene pair（runtime sceneName + 加载用 sceneID），不支持任意地图；
    /// - Owner 已确认地图选择 UI 暴露的全部 BossRushMapConfig 均可用于 Mode G；
    /// - preview 冻结 exact sceneName/sceneId，运行中离开 verified pair 即 End(SceneChanged)；
    /// - 直接复用 GetAllMapConfigs()，不维护第二份地图清单。
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
            /// <summary>实机矩阵验证状态</summary>
            public readonly ModeGMapSupportStatus status;
            /// <summary>官方死亡行为摘要 key（双语文本由 L10n 解析）</summary>
            public readonly string deathBehavior;
            /// <summary>死亡风险双语文案 key</summary>
            public readonly string riskTextKey;
            /// <summary>玩家/Boss/环境安全三元组验证摘要</summary>
            public readonly string safetyTriad;

            public SupportedMap(string baseScene, string combatScene, string displayName, string revision,
                ModeGMapSupportStatus status, string deathBehavior, string riskTextKey, string safetyTriad)
            {
                this.baseSceneName = baseScene;
                this.combatSceneName = combatScene;
                this.displayName = displayName;
                this.verificationRevision = revision;
                this.status = status;
                this.deathBehavior = deathBehavior;
                this.riskTextKey = riskTextKey;
                this.safetyTriad = safetyTriad;
            }
        }

        #endregion

        #region Registry（复用地图选择 UI 配置）

        private static readonly SupportedMap[] EmptyMaps = new SupportedMap[0];
        private static SupportedMap[] _configuredMaps;

        internal static void ResetStaticCaches()
        {
            _configuredMaps = null;
        }

        /// <summary>
        /// 从地图选择 UI 的同一配置源构建只读快照。首次有效读取后缓存，
        /// 避免 IsInteractable 高频查询重复分配；无效或重复 pair 不进入 Mode G。
        /// </summary>
        private static SupportedMap[] GetConfiguredMaps()
        {
            if (_configuredMaps != null) return _configuredMaps;

            BossRushMapConfig[] configs;
            try
            {
                configs = ModBehaviour.GetAllMapConfigs();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] 读取地图选择 UI 配置失败: " + e.Message);
                return EmptyMaps;
            }
            // 初始化前不缓存空结果，允许 OnAwake 完成 MapSpawnPointRegistry 后重试。
            if (configs == null || configs.Length == 0) return EmptyMaps;

            List<SupportedMap> maps = new List<SupportedMap>(configs.Length);
            HashSet<string> seenPairs = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < configs.Length; i++)
            {
                BossRushMapConfig config = configs[i];
                if (config == null
                    || string.IsNullOrEmpty(config.sceneName)
                    || string.IsNullOrEmpty(config.sceneID)
                    || config.spawnPoints == null
                    || config.spawnPoints.Length == 0) continue;

                string pairKey = config.sceneName + "\n" + config.sceneID;
                if (!seenPairs.Add(pairKey)) continue;
                maps.Add(new SupportedMap(
                    config.sceneName,
                    config.sceneID,
                    config.displayName,
                    ModeGAvailability.CurrentVerificationRevision,
                    ModeGMapSupportStatus.Verified,
                    "BossRush_ModeG_OfficialDeathBehavior",
                    "BossRush_ModeG_DeathRisk",
                    "owner-approved-map-selection-ui|configured-spawn-points|exact-scene-pair"));
            }
            _configuredMaps = maps.ToArray();
            return _configuredMaps;
        }

        /// <summary>
        /// 所有支持地图（只读快照）。
        /// </summary>
        public static IReadOnlyList<SupportedMap> SupportedMaps { get { return GetConfiguredMaps(); } }

        /// <summary>
        /// 当前是否有任何支持地图。
        /// </summary>
        public static bool HasAnySupportedMap { get { return EligibleMapCount > 0; } }

        public static int EligibleMapCount
        {
            get
            {
                int count = 0;
                SupportedMap[] maps = GetConfiguredMaps();
                for (int i = 0; i < maps.Length; i++)
                {
                    if (IsRecordVerified(maps[i])) count++;
                }
                return count;
            }
        }

        #endregion

        #region Queries

        /// <summary>
        /// 检查指定 scene pair 是否支持（exact 匹配）。
        /// </summary>
        public static bool IsSupported(string baseScene, string combatScene)
        {
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!IsRecordVerified(maps[i])) continue;
                if (string.Equals(maps[i].baseSceneName, baseScene, StringComparison.Ordinal)
                    && string.Equals(maps[i].combatSceneName, combatScene, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查 preview 冻结的完整支持记录（scene pair + verification revision）。
        /// </summary>
        public static bool IsSupported(string baseScene, string combatScene, string verificationRevision)
        {
            if (string.IsNullOrEmpty(verificationRevision)) return false;
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!IsRecordVerified(maps[i])) continue;
                if (string.Equals(maps[i].baseSceneName, baseScene, StringComparison.Ordinal)
                    && string.Equals(maps[i].combatSceneName, combatScene, StringComparison.Ordinal)
                    && string.Equals(maps[i].verificationRevision, verificationRevision,
                        StringComparison.Ordinal))
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
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!IsRecordVerified(maps[i])) continue;
                if (string.Equals(maps[i].baseSceneName, sceneName, StringComparison.Ordinal))
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
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!IsRecordVerified(maps[i])) continue;
                if (string.Equals(maps[i].combatSceneName, combatScene, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 首选 verified pair（兼容旧调用）；无候选返回 false。
        /// </summary>
        public static bool TryGetPrimaryVerifiedPair(out string sceneName, out string sceneId)
        {
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!IsRecordVerified(maps[i])) continue;
                sceneName = maps[i].baseSceneName;
                sceneId = maps[i].combatSceneName;
                return true;
            }
            sceneName = string.Empty;
            sceneId = string.Empty;
            return false;
        }

        /// <summary>
        /// 获取玩家实际选择并已加载场景的 exact scene pair。
        /// </summary>
        public static bool TryGetVerifiedPairForScene(string sceneName, out string sceneId)
        {
            sceneId = string.Empty;
            if (string.IsNullOrEmpty(sceneName)) return false;
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (!IsRecordVerified(maps[i])) continue;
                if (!string.Equals(maps[i].baseSceneName, sceneName, StringComparison.Ordinal)) continue;
                sceneId = maps[i].combatSceneName;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 首选地图双语展示名。
        /// </summary>
        public static string GetPrimaryDisplayName()
        {
            SupportedMap[] maps = GetConfiguredMaps();
            for (int i = 0; i < maps.Length; i++)
            {
                if (IsRecordVerified(maps[i])) return maps[i].displayName;
            }
            return string.Empty;
        }

        private static bool IsRecordVerified(SupportedMap map)
        {
            return map.status == ModeGMapSupportStatus.Verified
                && string.Equals(map.verificationRevision,
                    ModeGAvailability.CurrentVerificationRevision, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(map.baseSceneName)
                && !string.IsNullOrEmpty(map.combatSceneName)
                && !string.IsNullOrEmpty(map.deathBehavior)
                && !string.IsNullOrEmpty(map.riskTextKey)
                && !string.IsNullOrEmpty(map.safetyTriad);
        }

        #endregion
    }
}

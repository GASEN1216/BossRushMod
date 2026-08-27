using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一张通过审计的 Mode H 地图记录（不可变）。
    /// </summary>
    public sealed class ModeHSupportedMap
    {
        /// <summary>运行时场景名。</summary>
        public string SceneName;
        /// <summary>加载用场景 ID。</summary>
        public string SceneId;
        /// <summary>显示名。</summary>
        public string DisplayName;
        /// <summary>擂台刷怪点。</summary>
        public Vector3[] ArenaSpawnPoints;
        /// <summary>隔离生成点。</summary>
        public Vector3 StagingPos;
        /// <summary>看台点。</summary>
        public Vector3 SpectatorPos;
        /// <summary>玩家落点。</summary>
        public Vector3 PlayerSpawnPos;
        /// <summary>安全离场点。</summary>
        public Vector3 ExitPos;
        /// <summary>擂台中心（由刷怪点求均值，供 center 口令点火使用）。</summary>
        public Vector3 ArenaCenter;
    }

    /// <summary>
    /// Mode H 地图支持注册表（设计提案 §19.1、§25.1）。
    ///
    /// 与 Mode G 的地图注册表同形：不维护第二份地图清单，只从
    /// ModBehaviour.GetAllMapConfigs() 构建，并要求地图 JSON 提供 Mode H 可选点位：
    /// modeHSpawnPoints / modeHStagingPos / modeHSpectatorPos / modeHPlayerSpawnPos / modeHExitPos。
    ///
    /// 没有有效擂台、staging 或看台点位就拒绝进入，绝不把普通 BossRush 刷新点猜作替代。
    /// </summary>
    public static class ModeHMapSupportRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static Dictionary<string, ModeHSupportedMap> _mapsByScene;
        private static string _lastError;

        /// <summary>staging 点与擂台/看台的最小隔离距离（米），低于该值判定点位无效。</summary>
        public const float MinStagingIsolationDistance = 30f;

        #endregion

        #region 只读

        /// <summary>最后一次审计失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        #endregion

        #region 构建

        /// <summary>清空缓存（地图配置变化或 Mod 重载时调用）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _mapsByScene = null;
                _lastError = null;
            }
        }

        private static Dictionary<string, ModeHSupportedMap> GetOrBuild()
        {
            lock (_lock)
            {
                if (_mapsByScene != null) return _mapsByScene;
            }

            Dictionary<string, ModeHSupportedMap> maps =
                new Dictionary<string, ModeHSupportedMap>(StringComparer.Ordinal);
            try
            {
                BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
                if (configs == null || configs.Length == 0)
                {
                    _lastError = "map_configs_empty";
                    return maps; // 空结果不缓存，允许 OnAwake 之后重试
                }

                for (int i = 0; i < configs.Length; i++)
                {
                    BossRushMapConfig config = configs[i];
                    ModeHSupportedMap map = TryBuildMap(config);
                    if (map == null) continue;
                    maps[map.SceneName] = map;
                }
            }
            catch (Exception e)
            {
                _lastError = "map_scan_exception:" + e.GetType().Name;
                return maps;
            }

            if (maps.Count == 0)
            {
                _lastError = "map_no_modeh_points";
                return maps; // 同样不缓存空结果
            }

            lock (_lock)
            {
                _mapsByScene = maps;
            }
            return maps;
        }

        private static ModeHSupportedMap TryBuildMap(BossRushMapConfig config)
        {
            if (config == null) return null;
            if (string.IsNullOrEmpty(config.sceneName) || string.IsNullOrEmpty(config.sceneID)) return null;
            if (config.modeHSpawnPoints == null || config.modeHSpawnPoints.Length == 0) return null;
            if (!config.modeHStagingPos.HasValue) return null;
            if (!config.modeHSpectatorPos.HasValue) return null;
            if (!config.modeHPlayerSpawnPos.HasValue) return null;
            if (!config.modeHExitPos.HasValue) return null;

            Vector3 staging = config.modeHStagingPos.Value;
            Vector3 spectator = config.modeHSpectatorPos.Value;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < config.modeHSpawnPoints.Length; i++)
            {
                center += config.modeHSpawnPoints[i];
            }
            center /= config.modeHSpawnPoints.Length;

            // staging 必须与擂台和看台保持实机审计后的隔离距离（§19.1）
            if (Vector3.Distance(staging, center) < MinStagingIsolationDistance) return null;
            if (Vector3.Distance(staging, spectator) < MinStagingIsolationDistance) return null;

            ModeHSupportedMap map = new ModeHSupportedMap();
            map.SceneName = config.sceneName;
            map.SceneId = config.sceneID;
            map.DisplayName = config.displayName;
            map.ArenaSpawnPoints = config.modeHSpawnPoints;
            map.StagingPos = staging;
            map.SpectatorPos = spectator;
            map.PlayerSpawnPos = config.modeHPlayerSpawnPos.Value;
            map.ExitPos = config.modeHExitPos.Value;
            map.ArenaCenter = center;
            return map;
        }

        #endregion

        #region 查询

        /// <summary>该场景是否支持 Mode H。</summary>
        public static bool IsSupportedScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return GetOrBuild().ContainsKey(sceneName);
        }

        /// <summary>该 scene pair 是否支持 Mode H。</summary>
        public static bool IsSupportedPair(string sceneName, string sceneId)
        {
            ModeHSupportedMap map;
            if (!TryGetMap(sceneName, out map)) return false;
            if (string.IsNullOrEmpty(sceneId)) return true;
            return string.Equals(map.SceneId, sceneId, StringComparison.Ordinal);
        }

        /// <summary>取地图记录。</summary>
        public static bool TryGetMap(string sceneName, out ModeHSupportedMap map)
        {
            map = null;
            if (string.IsNullOrEmpty(sceneName)) return false;
            Dictionary<string, ModeHSupportedMap> maps = GetOrBuild();
            return maps.TryGetValue(sceneName, out map);
        }

        /// <summary>取任意一张受支持地图（入口页默认目标）。</summary>
        public static bool TryGetPrimaryMap(out ModeHSupportedMap map)
        {
            map = null;
            Dictionary<string, ModeHSupportedMap> maps = GetOrBuild();
            if (maps.Count == 0) return false;

            List<string> names = new List<string>(maps.Keys);
            names.Sort(StringComparer.Ordinal);
            map = maps[names[0]];
            return true;
        }

        /// <summary>受支持地图数量。</summary>
        public static int SupportedMapCount
        {
            get { return GetOrBuild().Count; }
        }

        /// <summary>全部受支持场景名（ordinal 升序）。</summary>
        public static List<string> GetSupportedSceneNames()
        {
            List<string> names = new List<string>(GetOrBuild().Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        #endregion
    }
}

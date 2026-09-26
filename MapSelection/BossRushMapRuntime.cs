using System.Linq;
using UnityEngine;

namespace BossRush
{
    internal sealed class BossRushMapRuntime
    {
        private static readonly MapSpawnPointRegistry _mapSpawnRegistry = new MapSpawnPointRegistry();
        private Vector3[] currentMapSpawnPoints = null;

        internal static Vector3 ResolveArenaSignPosition(BossRushMapConfig currentMapConfig, Vector3 playerPosition)
        {
            Vector3 signPosition;
            if (currentMapConfig != null && currentMapConfig.defaultSignPos.HasValue)
            {
                signPosition = currentMapConfig.defaultSignPos.Value;
                ModBehaviour.DevLog("[BossRush] SetupBossRushInGroundZero: 使用配置的交互点位置: " + signPosition);
            }
            else
            {
                signPosition = playerPosition + new Vector3(-2f, 0f, 1f);
                ModBehaviour.DevLog("[BossRush] SetupBossRushInGroundZero: 使用玩家位置偏移: " + signPosition);
            }
            return signPosition;
        }

        internal static void Initialize(string modPath) { _mapSpawnRegistry.Initialize(modPath); }
        internal void SetCurrentMapSpawnPoints(Vector3[] points) { currentMapSpawnPoints = points; }

        internal static BossRushMapConfig GetMapConfigBySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            return _mapSpawnRegistry.TryGet(sceneName);
        }

        internal static BossRushMapConfig GetMapConfigBySceneID(string sceneID)
        {
            if (string.IsNullOrEmpty(sceneID)) return null;

            // 优先从注册表遍历查找（按 sceneID 匹配）
            foreach (var config in _mapSpawnRegistry.All())
            {
                if (config.sceneID == sceneID)
                {
                    return config;
                }
            }

            return null;
        }

        internal static BossRushMapConfig GetCurrentMapConfig()
        {
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return GetMapConfigBySceneName(currentScene);
        }

        internal static BossRushMapConfig[] GetAllMapConfigs()
        {
            return _mapSpawnRegistry.All().ToArray();
        }

        internal bool IsValidBossRushArenaScene(string sceneName)
        {
            return GetMapConfigBySceneName(sceneName) != null;
        }

        internal bool IsCurrentSceneValidBossRushArena()
        {
            return GetCurrentMapConfig() != null;
        }

        internal static Vector3[] GetSpawnPointsForScene(string sceneName)
        {
            BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
            return mapConfig != null ? mapConfig.spawnPoints : null;
        }

        internal Vector3[] GetCurrentSceneSpawnPoints()
        {
            // 优先使用动态设置的刷新点
            if (currentMapSpawnPoints != null && currentMapSpawnPoints.Length > 0)
            {
                return currentMapSpawnPoints;
            }

            BossRushMapConfig mapConfig = GetCurrentMapConfig();
            return mapConfig != null ? mapConfig.spawnPoints : null;
        }

        internal static Vector3 GetCurrentSceneDefaultPosition()
        {
            BossRushMapConfig mapConfig = GetCurrentMapConfig();
            if (mapConfig != null)
            {
                // 优先使用自定义传送位置
                if (mapConfig.customSpawnPos.HasValue)
                {
                    return mapConfig.customSpawnPos.Value;
                }
                // 其次使用默认路牌位置
                if (mapConfig.defaultSignPos.HasValue)
                {
                    return mapConfig.defaultSignPos.Value;
                }
            }
            // 兜底：DEMO 竞技场默认位置
            return new Vector3(235.48f, -7.99f, 202.41f);
        }

        internal static Vector3 GetDefaultPositionForScene(string sceneName)
        {
            BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
            if (mapConfig != null)
            {
                if (mapConfig.customSpawnPos.HasValue)
                {
                    return mapConfig.customSpawnPos.Value;
                }
                if (mapConfig.defaultSignPos.HasValue)
                {
                    return mapConfig.defaultSignPos.Value;
                }
            }
            // 兜底：DEMO 竞技场默认位置
            return new Vector3(235.48f, -7.99f, 202.41f);
        }
    }
}

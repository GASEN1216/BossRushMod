using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class CommonNpcRuntimeModule
    {
        private Func<bool> usesArenaSupportNpcPlacement;
        private Func<bool> isActive;
        private Func<bool> isModeDActive;
        private Func<bool> isBossRushArenaActive;

        internal void BindSpawnPointQueries(Func<bool> useArenaSupportPlacement, Func<bool> active,
            Func<bool> modeDActive, Func<bool> arenaActive)
        {
            usesArenaSupportNpcPlacement = useArenaSupportPlacement;
            isActive = active;
            isModeDActive = modeDActive;
            isBossRushArenaActive = arenaActive;
        }

        internal static Vector3[] GetSharedCommonNPCSpawnPointsForScene(string sceneName, CommonNpcRuntimeModule mod)
        {
            if (mod != null && mod.ShouldUseBossRushCommonNPCSpawnPoints(sceneName))
            {
                return BossRushMapRuntime.GetSpawnPointsForScene(sceneName);
            }

            Vector3[] normalModePoints = NPCSpawnConfig.GetCourierNormalModeSpawnPoints(sceneName);
            if (normalModePoints != null && normalModePoints.Length > 0)
            {
                return normalModePoints;
            }

            return BossRushMapRuntime.GetSpawnPointsForScene(sceneName);
        }

        internal bool ShouldUseBossRushCommonNPCSpawnPoints(string sceneName = null)
        {
            if (usesArenaSupportNpcPlacement())
            {
                return false;
            }

            if (!isActive() && !isModeDActive() && !isBossRushArenaActive())
            {
                return false;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }

            return BossRushMapRuntime.GetMapConfigBySceneName(sceneName) != null;
        }
    }
}

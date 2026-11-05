using System;
using Duckov.Scenes;
using Duckov.Utilities;

namespace BossRush
{
    internal static class SceneRuntimeGate
    {
        private static int _staticCanRunFrame = -1;
        private static bool _staticCanRunResult;

        internal static bool CanRunGameplayRuntimeCached()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != _staticCanRunFrame)
            {
                _staticCanRunFrame = frame;
                _staticCanRunResult = SceneRuntimeGate.CanRunGameplayRuntimeNow(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            return _staticCanRunResult;
        }

        internal const string StoneOutpostResourceScenePath = "Assets/StoneOutpost/StoneOutpost.unity";
        private const string BaseRootSceneName = "Base";
        private const string BaseSceneName = "Base_SceneV2";
        /// <summary>基地地堡下水道（官方船点所在子场景，sceneID `Base_SceneV2_2`）。</summary>
        private const string BaseSceneSubName = "Base_SceneV2_Sub_01";
        /// <summary>
        /// 仓库区出击图 `Level_HiddenWarehouse_Main` 的地下室子场景，**不是基地**。它曾被当成「基地下水道」写进基地名单，
        /// 玩家在仓库区出击时走进地下室，Mod 就以为回到了基地（2026-11-05 读官方 level18 / level3 的 MultiSceneCore.subScenes 核实：
        /// 基地的两个子场景是 Base_SceneV2 与 Base_SceneV2_Sub_01）。留着常量只为让守卫钉住「它不在基地名单里」。
        /// </summary>
        internal const string HiddenWarehouseCellarSceneName = "Level_HiddenWarehouse_CellarUnderGround";

        /// <summary>
        /// 只承载附加环境资源、不代表角色/关卡服务切换的 Scene；按精确路径识别。
        /// 天空岛不在此列：它是完整的独立 Raid 关卡（`SkyIslandRaid.unity`），本来就该走正常切图保护。
        /// </summary>
        internal static bool IsModResourceScene(UnityEngine.SceneManagement.Scene scene)
        {
            return string.Equals(scene.path, StoneOutpostResourceScenePath, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsBaseHubSceneName(string sceneName)
        {
            return sceneName == BaseRootSceneName ||
                   sceneName == BaseSceneName ||
                   sceneName == BaseSceneSubName;
        }

        internal static bool IsGameplaySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            try
            {
                if (SceneNameEquals(sceneName, GameplayDataSettings.SceneManagement.MainMenuScene.Name) ||
                    SceneNameEquals(sceneName, GameplayDataSettings.SceneManagement.FailLoadingScreenScene.Name) ||
                    SceneNameEquals(sceneName, GameplayDataSettings.SceneManagement.EvacuateScreenScene.Name))
                {
                    return false;
                }
            }
            catch { }

            try
            {
                if (SceneLoader.Instance != null &&
                    SceneLoader.Instance.defaultCurtainScene != null &&
                    SceneNameEquals(sceneName, SceneLoader.Instance.defaultCurtainScene.Name))
                {
                    return false;
                }
            }
            catch { }

            if (SceneNameEquals(sceneName, "MainMenu") ||
                SceneNameEquals(sceneName, "LoadingScreen") ||
                SceneNameEquals(sceneName, "LoadingScreen_Black") ||
                SceneNameEquals(sceneName, "FailLoadingScreen") ||
                SceneNameEquals(sceneName, "EvacuateScreen"))
            {
                return false;
            }

            return true;
        }

        internal static bool CanRunGameplayRuntimeNow(string sceneName)
        {
            if (!IsGameplaySceneName(sceneName))
            {
                return false;
            }

            try
            {
                if (SceneLoader.IsSceneLoading)
                {
                    return false;
                }
            }
            catch { }

            try
            {
                MultiSceneCore multiSceneCore = MultiSceneCore.Instance;
                if (multiSceneCore != null && multiSceneCore.IsLoading)
                {
                    return false;
                }
            }
            catch { }

            return true;
        }

        internal static bool ShouldRunGameplaySceneRuntimeHooks(string sceneName)
        {
            return CanRunGameplayRuntimeNow(sceneName);
        }

        private static bool SceneNameEquals(string sceneName, string expectedSceneName)
        {
            return !string.IsNullOrEmpty(sceneName) &&
                   !string.IsNullOrEmpty(expectedSceneName) &&
                   string.Equals(sceneName, expectedSceneName, StringComparison.OrdinalIgnoreCase);
        }
    }
}

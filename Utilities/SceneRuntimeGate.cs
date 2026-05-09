using System;
using Duckov.Scenes;
using Duckov.Utilities;

namespace BossRush
{
    internal static class SceneRuntimeGate
    {
        private const string BaseRootSceneName = "Base";
        private const string BaseSceneName = "Base_SceneV2";
        private const string BaseSceneSubName = "Base_SceneV2_Sub_01";
        private const string BaseSewerSceneName = "Level_HiddenWarehouse_CellarUnderGround";

        internal static bool IsBaseHubSceneName(string sceneName)
        {
            return sceneName == BaseRootSceneName ||
                   sceneName == BaseSceneName ||
                   sceneName == BaseSceneSubName ||
                   sceneName == BaseSewerSceneName;
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

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private bool integrationContentBootstrapStarted = false;
        private bool integrationEssentialContentFinished = false;
        private bool integrationContentBootstrapFinished = false;
        private Coroutine integrationContentBootstrapCoroutine = null;
        private Coroutine deferredSceneSetupCoroutine = null;
        private Coroutine deferredBaseSceneSetupCoroutine = null;
        private int deferredSceneSetupHandle = int.MinValue;
        private int deferredBaseSceneSetupHandle = int.MinValue;
        private int appliedDeferredBaseSceneSetupHandle = int.MinValue;

        private void EnsureIntegrationContentBootstrapScheduled(string source)
        {
            if (integrationContentBootstrapFinished || integrationContentBootstrapStarted)
            {
                return;
            }

            integrationContentBootstrapStarted = true;
            integrationContentBootstrapCoroutine =
                StartCoroutine(RunIntegrationContentBootstrapWhenReady(source));
        }

        private IEnumerator RunIntegrationContentBootstrapWhenReady(string source)
        {
            while (!CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))
            {
                yield return null;
            }

            yield return null;

            yield return RunDeferredStep_Integration("InitializeAlwaysOnDeferredContent", () => InitializeAlwaysOnDeferredContent());
            yield return RunDeferredStep_Integration("InitializeDynamicItems", () => InitializeDynamicItems());
            yield return RunDeferredStep_Integration("InjectBossRushTicketLocalization", () => InjectBossRushTicketLocalization());
            yield return RunDeferredStep_Integration("InitializeBirthdayCakeItem", () => InitializeBirthdayCakeItem());
            yield return RunDeferredStep_Integration("InjectBirthdayCakeLocalization", () => InjectBirthdayCakeLocalization());
            yield return RunDeferredStep_Integration("InitializeWikiBookItem", () => InitializeWikiBookItem());
            yield return RunDeferredStep_Integration("InjectWikiBookLocalization", () => InjectWikiBookLocalization());
            yield return RunDeferredStep_Integration("InjectAchievementMedalLocalization", () => InjectAchievementMedalLocalization());

            integrationEssentialContentFinished = true;
            Scene essentialScene = SceneManager.GetActiveScene();
            if (essentialScene.IsValid())
            {
                ScheduleRestoreFollowingSpouse(essentialScene.name, "EssentialContentReady");
            }
            ScheduleDeferredSceneSetupForActiveScene("EssentialContentReady:" + source);

            yield return RunDeferredStep_Integration("LoadEquipmentContent", () => LoadEquipmentContent());
            yield return RunDeferredStep_Integration("InitializeEarlyEquipmentAbilitySystems", () => InitializeEarlyEquipmentAbilitySystems());
            yield return RunDeferredStep_Integration("InitializeLateEquipmentAbilitySystems", () => InitializeLateEquipmentAbilitySystems());

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                string activeSceneName = activeScene.name;
                int activeSceneHandle = activeScene.handle;

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupFlightTotemForScene", () => SetupFlightTotemForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupReverseScaleForScene", () => SetupReverseScaleForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupFenHuangHalberdForScene", () => SetupFenHuangHalberdForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupFrostmourneForScene", () => SetupFrostmourneForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupPhantomWitchScytheForScene", () => SetupPhantomWitchScytheForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupNewWeaponsForScene", () => SetupNewWeaponsForScene(activeScene));
                }
            }

            integrationContentBootstrapFinished = true;
            integrationContentBootstrapCoroutine = null;
            ScheduleDeferredSceneSetupForActiveScene("ContentBootstrapComplete:" + source);
        }

        private IEnumerator RunDeferredStep_Integration(string label, Action action)
        {
            SafeRuntime.Run(label, action);
            yield return null;
        }

        private void OnAfterSceneInitialize_Integration(SceneLoadingContext context)
        {
            EnsureIntegrationContentBootstrapScheduled("AfterSceneInitialize:" + context.sceneName);
            ScheduleDeferredSceneSetupForActiveScene("AfterSceneInitialize:" + context.sceneName);
        }

        private void ScheduleDeferredSceneSetupForActiveScene(string reason)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return;
            }

            if (deferredSceneSetupCoroutine != null && deferredSceneSetupHandle == activeScene.handle)
            {
                return;
            }

            if (deferredSceneSetupCoroutine != null)
            {
                StopCoroutine(deferredSceneSetupCoroutine);
                deferredSceneSetupCoroutine = null;
            }

            deferredSceneSetupHandle = activeScene.handle;
            deferredSceneSetupCoroutine = StartCoroutine(
                RunDeferredSceneSetupForActiveScene(activeScene.handle, activeScene.name, reason));
        }

        private IEnumerator RunDeferredSceneSetupForActiveScene(
            int sceneHandle,
            string sceneName,
            string reason)
        {
            yield return null;
            yield return null;

            while (!integrationEssentialContentFinished)
            {
                EnsureIntegrationContentBootstrapScheduled(reason);
                if (SceneManager.GetActiveScene().handle != sceneHandle)
                {
                    deferredSceneSetupCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            if (SceneManager.GetActiveScene().handle != sceneHandle)
            {
                deferredSceneSetupCoroutine = null;
                yield break;
            }

            ApplyDeferredSceneSetup_Integration(sceneName);
            deferredSceneSetupHandle = int.MinValue;
            deferredSceneSetupCoroutine = null;
        }

        private void ApplyDeferredSceneSetup_Integration(string sceneName)
        {
            if (sceneName != BaseSceneName)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return;
            }

            int sceneHandle = activeScene.handle;
            if (appliedDeferredBaseSceneSetupHandle == sceneHandle)
            {
                return;
            }

            if (deferredBaseSceneSetupCoroutine != null && deferredBaseSceneSetupHandle == sceneHandle)
            {
                return;
            }

            if (deferredBaseSceneSetupCoroutine != null)
            {
                StopCoroutine(deferredBaseSceneSetupCoroutine);
                deferredBaseSceneSetupCoroutine = null;
            }

            deferredBaseSceneSetupHandle = sceneHandle;
            deferredBaseSceneSetupCoroutine =
                StartCoroutine(RunDeferredBaseSceneSetup_Integration(sceneName, sceneHandle));
        }

        private IEnumerator RunDeferredBaseSceneSetup_Integration(string sceneName, int sceneHandle)
        {
            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("InjectBossRushTicketIntoShops_Integration", () => InjectBossRushTicketIntoShops_Integration(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("InjectAdventureJournalIntoShops_Integration", () => InjectAdventureJournalIntoShops_Integration(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("InjectAchievementMedalIntoShops", () => InjectAchievementMedalIntoShops(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("AwenCourierTokenConfig.InjectIntoShops", () => AwenCourierTokenConfig.InjectIntoShops(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("InjectBrickStoneIntoShops", () => InjectBrickStoneIntoShops(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("ZombieTideInvitationConfig.InjectIntoShops", () => ZombieTideInvitationConfig.InjectIntoShops(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("FactionFlagConfig.InjectIntoShops", () => FactionFlagConfig.InjectIntoShops(sceneName));

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("BloodhuntTransponderConfig.InjectIntoShops", () => BloodhuntTransponderConfig.InjectIntoShops(sceneName));

            StartCoroutine(DelayedBirthdayCakeGift());
            yield return null;

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("InitWeddingBuilding", () => InitWeddingBuilding());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("RestoreWeddingBuildingNPC", () => RestoreWeddingBuildingNPC());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("InitWishFountainBuilding", () => InitWishFountainBuilding());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("RestoreWishFountainBuildings", () => RestoreWishFountainBuildings());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            ScheduleWishRewardPoolWarmup();
            appliedDeferredBaseSceneSetupHandle = sceneHandle;
            ClearDeferredBaseSceneSetup_Integration(sceneHandle);
        }

        private bool IsDeferredSceneStillActive_Integration(string sceneName, int sceneHandle)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.handle != sceneHandle)
            {
                return false;
            }

            return activeScene.name == sceneName;
        }

        private bool ShouldContinueDeferredBaseSceneSetup_Integration(string sceneName, int sceneHandle)
        {
            if (IsDeferredSceneStillActive_Integration(sceneName, sceneHandle))
            {
                return true;
            }

            ClearDeferredBaseSceneSetup_Integration(sceneHandle);
            return false;
        }

        private void ClearDeferredBaseSceneSetup_Integration(int sceneHandle)
        {
            if (deferredBaseSceneSetupHandle != sceneHandle)
            {
                return;
            }

            deferredBaseSceneSetupHandle = int.MinValue;
            deferredBaseSceneSetupCoroutine = null;
        }

        private void CleanupDeferredIntegrationBootstrap_Integration()
        {
            if (integrationContentBootstrapCoroutine != null)
            {
                StopCoroutine(integrationContentBootstrapCoroutine);
                integrationContentBootstrapCoroutine = null;
            }

            if (deferredSceneSetupCoroutine != null)
            {
                StopCoroutine(deferredSceneSetupCoroutine);
                deferredSceneSetupCoroutine = null;
            }

            if (deferredBaseSceneSetupCoroutine != null)
            {
                StopCoroutine(deferredBaseSceneSetupCoroutine);
                deferredBaseSceneSetupCoroutine = null;
            }

            deferredSceneSetupHandle = int.MinValue;
            deferredBaseSceneSetupHandle = int.MinValue;
            appliedDeferredBaseSceneSetupHandle = int.MinValue;
            integrationContentBootstrapStarted = false;
            integrationEssentialContentFinished = false;
            integrationContentBootstrapFinished = false;
        }
    }
}

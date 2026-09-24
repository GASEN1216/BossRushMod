using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.Scenes;

namespace BossRush
{
    /// <summary>Deferred Integration bootstrap state and scene scheduling owner.</summary>
    internal sealed partial class IntegrationRuntimeModule
    {
        private IntegrationDeferredBootstrapActions _deferredBootstrapActions;
        private bool integrationContentBootstrapStarted = false;
        private bool integrationEssentialContentFinished = false;
        private bool integrationContentBootstrapFinished = false;
        private Coroutine integrationContentBootstrapCoroutine = null;
        private Coroutine deferredSceneSetupCoroutine = null;
        private Coroutine deferredBaseSceneSetupCoroutine = null;
        private int deferredSceneSetupHandle = int.MinValue;
        private int deferredBaseSceneSetupHandle = int.MinValue;
        private int appliedDeferredBaseSceneSetupHandle = int.MinValue;

        internal void EnsureIntegrationContentBootstrapScheduled(string source)
        {
            if (integrationContentBootstrapFinished || integrationContentBootstrapStarted)
            {
                return;
            }

            integrationContentBootstrapStarted = true;
            integrationContentBootstrapCoroutine =
                _owner.StartCoroutine(RunIntegrationContentBootstrapWhenReady(source));
        }

        private IEnumerator RunIntegrationContentBootstrapWhenReady(string source)
        {
            while (!ModBehaviour.CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))
            {
                yield return null;
            }

            yield return null;

            yield return RunDeferredStep_Integration("InitializeAlwaysOnDeferredContent", () => _owner.InitializeAlwaysOnDeferredContent());
            yield return FactoryResourceLoading.InitializeItems(_owner, _deferredBootstrapActions.InitializeDynamicItems);
            yield return RunDeferredStep_Integration("InjectBossRushTicketLocalization", () => _deferredBootstrapActions.InjectBossRushTicketLocalization());
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/birthday_cake", InitializeBirthdayCakeItem);
            yield return RunDeferredStep_Integration("InjectBirthdayCakeLocalization", () => InjectBirthdayCakeLocalization());
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/ui/bossrush_wiki", InitializeWikiBookItem);
            yield return RunDeferredStep_Integration("InjectWikiBookLocalization", () => InjectWikiBookLocalization());
            yield return RunDeferredStep_Integration("InjectAchievementMedalLocalization", () => _deferredBootstrapActions.InjectAchievementMedalLocalization());

            integrationEssentialContentFinished = true;
            Scene essentialScene = SceneManager.GetActiveScene();
            if (essentialScene.IsValid())
            {
                _owner.ScheduleRestoreFollowingSpouse(essentialScene.name, "EssentialContentReady");
            }
            ScheduleDeferredSceneSetupForActiveScene("EssentialContentReady:" + source);

            yield return EquipmentFactory.LoadAllEquipmentAsync(_owner);
            yield return RunDeferredStep_Integration("LoadEquipmentContent", () => _deferredBootstrapActions.LoadEquipmentContent());
            yield return RunDeferredStep_Integration("InitializeEarlyEquipmentAbilitySystems", () => _deferredBootstrapActions.InitializeEarlyEquipmentAbilitySystems());
            yield return RunDeferredStep_Integration("InitializeLateEquipmentAbilitySystems", () => _deferredBootstrapActions.InitializeLateEquipmentAbilitySystems());
            // Mod 战利品名录（幂等；lazy 注册的物品由基地装配再捡漏；不补标签：官方枪架 / 假人按装备自身槽位标签准入）
            yield return RunDeferredStep_Integration("ShowcaseTrophyCatalog.Refresh", () => ShowcaseTrophyCatalog.Refresh());

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                string activeSceneName = activeScene.name;
                int activeSceneHandle = activeScene.handle;

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupFlightTotemForScene", () => _deferredBootstrapActions.SetupFlightTotemForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupReverseScaleForScene", () => _deferredBootstrapActions.SetupReverseScaleForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupFenHuangHalberdForScene", () => _deferredBootstrapActions.SetupFenHuangHalberdForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupFrostmourneForScene", () => _deferredBootstrapActions.SetupFrostmourneForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupPhantomWitchScytheForScene", () => _deferredBootstrapActions.SetupPhantomWitchScytheForScene(activeScene));
                }

                if (IsDeferredSceneStillActive_Integration(activeSceneName, activeSceneHandle))
                {
                    yield return RunDeferredStep_Integration("SetupNewWeaponsForScene", () => _deferredBootstrapActions.SetupNewWeaponsForScene(activeScene));
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

        internal void OnAfterSceneInitialize_Integration(SceneLoadingContext context)
        {
            EnsureIntegrationContentBootstrapScheduled("AfterSceneInitialize:" + context.sceneName);
            ScheduleDeferredSceneSetupForActiveScene("AfterSceneInitialize:" + context.sceneName);
        }

        internal void ScheduleDeferredSceneSetupForActiveScene(string reason)
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
                _owner.StopCoroutine(deferredSceneSetupCoroutine);
                deferredSceneSetupCoroutine = null;
            }

            deferredSceneSetupHandle = activeScene.handle;
            deferredSceneSetupCoroutine = _owner.StartCoroutine(
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
            if (sceneName != _owner.IntegrationBaseSceneName)
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
                _owner.StopCoroutine(deferredBaseSceneSetupCoroutine);
                deferredBaseSceneSetupCoroutine = null;
            }

            deferredBaseSceneSetupHandle = sceneHandle;
            deferredBaseSceneSetupCoroutine =
                _owner.StartCoroutine(RunDeferredBaseSceneSetup_Integration(sceneName, sceneHandle));
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
            yield return RunDeferredStep_Integration("InjectAchievementMedalIntoShops", () => _deferredBootstrapActions.InjectAchievementMedalIntoShops(sceneName));

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
            yield return RunDeferredStep_Integration("FateEchoRelicConfig.InjectIntoShops", () => FateEchoRelicConfig.InjectIntoShops(sceneName));
            // 图鉴：Harmony 的 BaseHubShopAwakePatch 只覆盖「商店 Awake 晚于 Mod」的情况，
            // 场景已加载完再进基地时要靠这一步补注入（与上面几个 InjectIntoShops 同理）。
            yield return RunDeferredStep_Integration("InjectCodexBookIntoShops", () => _owner.InjectCodexBookIntoShops(sceneName));

            _owner.StartCoroutine(DelayedBirthdayCakeGift());
            yield return null;

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/weddingchapel", _owner.InitWeddingBuilding,
                () => _owner.WeddingRuntime != null && _owner.WeddingRuntime.HasAssetBundle);

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("RestoreWeddingBuildingNPC", () => _owner.RestoreWeddingBuildingNPC());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/starwish_fountain", _owner.InitWishFountainBuilding,
                () => _owner.WishFountainRuntime != null && _owner.WishFountainRuntime.HasAssetBundle);

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("RestoreWishFountainBuildings", () => _owner.RestoreWishFountainBuildings());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/petnest_relic_nest", _owner.InitPetNestBuilding, () => PetNestBuilder.IsBundleLoaded);

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("RestorePetNestBuildings", () => _owner.RestorePetNestBuildings());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/bossrush_daily_mailbox", _owner.InitDailyReportMailbox, () => DailyReportMailboxBuilder.IsBundleLoaded);

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return RunDeferredStep_Integration("RestoreDailyReportMailboxes", () => _owner.RestoreDailyReportMailboxes());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/bossrush_campaign_board", _owner.InitCampaignBoardBuilding);
            yield return FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/bossrush_backmountain_showcase", _owner.InitBackMountainShowcase);

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            // 线索注册要在建筑之后：注册会读章节表并取海报图，
            // 而海报与建筑图标共用同一套资源路径，放一起省一次冷启动。
            yield return RunDeferredStep_Integration("RegisterCampaignNotes", () => _owner.RegisterCampaignNotesForScene());

            if (!ShouldContinueDeferredBaseSceneSetup_Integration(sceneName, sceneHandle))
            {
                yield break;
            }
            _deferredBootstrapActions.ScheduleWishRewardPoolWarmup();
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

        internal void CleanupDeferredIntegrationBootstrap()
        {
            if (integrationContentBootstrapCoroutine != null)
            {
                _owner.StopCoroutine(integrationContentBootstrapCoroutine);
                integrationContentBootstrapCoroutine = null;
            }

            if (deferredSceneSetupCoroutine != null)
            {
                _owner.StopCoroutine(deferredSceneSetupCoroutine);
                deferredSceneSetupCoroutine = null;
            }

            if (deferredBaseSceneSetupCoroutine != null)
            {
                _owner.StopCoroutine(deferredBaseSceneSetupCoroutine);
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

    /// <summary>Private ModBehaviour entrypoints captured for the Integration runtime owner.</summary>
    internal sealed class IntegrationDeferredBootstrapActions
    {
        internal Action InitializeDynamicItems;
        internal Action InjectBossRushTicketLocalization;
        internal Action InjectAchievementMedalLocalization;
        internal Action LoadEquipmentContent;
        internal Action InitializeEarlyEquipmentAbilitySystems;
        internal Action InitializeLateEquipmentAbilitySystems;
        internal Action<Scene> SetupFlightTotemForScene;
        internal Action<Scene> SetupReverseScaleForScene;
        internal Action<Scene> SetupFenHuangHalberdForScene;
        internal Action<Scene> SetupFrostmourneForScene;
        internal Action<Scene> SetupPhantomWitchScytheForScene;
        internal Action<Scene> SetupNewWeaponsForScene;
        internal Action<string> InjectAchievementMedalIntoShops;
        internal Action ScheduleWishRewardPoolWarmup;
    }
}

using System;
using System.Collections;
using UnityEngine.SceneManagement;
using Duckov.Scenes;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void EnsureIntegrationContentBootstrapScheduled(string source)
        {
            bossRushIntegrationRuntime.EnsureIntegrationContentBootstrapScheduled(source);
        }

        private void OnAfterSceneInitialize_Integration(SceneLoadingContext context)
        {
            bossRushIntegrationRuntime.OnAfterSceneInitialize_Integration(context);
        }

        private void ScheduleDeferredSceneSetupForActiveScene(string reason)
        {
            bossRushIntegrationRuntime.ScheduleDeferredSceneSetupForActiveScene(reason);
        }

        private void CleanupDeferredIntegrationBootstrap_Integration()
        {
            bossRushIntegrationRuntime.CleanupDeferredIntegrationBootstrap();
        }

        internal IntegrationDeferredBootstrapActions CreateIntegrationDeferredBootstrapActions()
        {
            return new IntegrationDeferredBootstrapActions
            {
                InitializeDynamicItems = InitializeDynamicItems,
                InjectBossRushTicketLocalization = InjectBossRushTicketLocalization,
                InjectAchievementMedalLocalization = InjectAchievementMedalLocalization,
                LoadEquipmentContent = LoadEquipmentContent,
                InitializeEarlyEquipmentAbilitySystems = InitializeEarlyEquipmentAbilitySystems,
                InitializeLateEquipmentAbilitySystems = InitializeLateEquipmentAbilitySystems,
                SetupFlightTotemForScene = SetupFlightTotemForScene,
                SetupReverseScaleForScene = SetupReverseScaleForScene,
                SetupFenHuangHalberdForScene = SetupFenHuangHalberdForScene,
                SetupFrostmourneForScene = SetupFrostmourneForScene,
                SetupPhantomWitchScytheForScene = SetupPhantomWitchScytheForScene,
                SetupNewWeaponsForScene = SetupNewWeaponsForScene,
                InjectAchievementMedalIntoShops = InjectAchievementMedalIntoShops,
                ScheduleWishRewardPoolWarmup = ScheduleWishRewardPoolWarmup,
            };
        }
    }
}

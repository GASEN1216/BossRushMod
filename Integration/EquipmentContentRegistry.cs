using System;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void LoadEquipmentContent()
        {
            bossRushIntegrationRuntime.LoadEquipmentContent();
        }

        private void InitializeEarlyEquipmentAbilitySystems()
        {
            InitializeFlightTotemSystem();
        }

        private void InitializeLateEquipmentAbilitySystems()
        {
            InitializeReverseScaleSystem();
            InitializeFenHuangHalberdSystem();
            InitializeFrostmourneSystem();
            InitializePhantomWitchScytheSystem();
            InitializeNewWeaponSystems();
        }

        private void CleanupEquipmentAbilitySystems()
        {
            CleanupReverseScaleSystem();
            CleanupFenHuangHalberdSystem();
            CleanupFrostmourneSystem();
            CleanupPhantomWitchScytheSystem();
            CleanupNewWeaponSystemsOnDestroy();
            DragonKingBossGunRuntime.ResetStaticCaches();
            UnsubscribeDragonBreathEffectEvent();
            DragonBreathBuffHandler.Cleanup();
            CleanupFlightTotemSystem();
        }
    }
}

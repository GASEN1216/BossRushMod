using System;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void LoadEquipmentContent()
        {
            int equipCount = EquipmentFactory.LoadAllEquipment();
            DevLog("[BossRush] 自动加载装备完成，共 " + equipCount + " 个");

            DragonKingBossGunRuntime.InitializeRuntime();
            DragonKingBossGunRuntime.WarmupProjectileCache();

            try
            {
                Item fenHuangHalberd = ItemFactory.GetLoadedItem(FenHuangHalberdIds.WeaponTypeId);
                if (fenHuangHalberd != null)
                {
                    FenHuangHalberdWeaponConfig.TryConfigure(fenHuangHalberd, "FenHuangHalberd");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 绑定焚皇断界戟模型失败: " + e.Message);
            }

            try
            {
                Item frostmourne = ItemFactory.GetLoadedItem(FrostmourneIds.WeaponTypeId);
                if (frostmourne != null)
                {
                    FrostmourneWeaponConfig.TryConfigure(frostmourne, "Frostmourne");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 绑定霜之哀伤模型失败: " + e.Message);
            }
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
        }

        private void CleanupEquipmentAbilitySystems()
        {
            CleanupReverseScaleSystem();
            CleanupFenHuangHalberdSystem();
            CleanupFrostmourneSystem();
            CleanupPhantomWitchScytheSystem();
            DragonKingBossGunRuntime.ResetStaticCaches();
            UnsubscribeDragonBreathEffectEvent();
            DragonBreathBuffHandler.Cleanup();
            CleanupFlightTotemSystem();
        }
    }
}

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

            // P0 新武器、P1 套装占位符注册：必须在 EquipmentFactory.LoadAllEquipment 之后执行，
            // 因为它们要把飞行图腾(500010) / 逆鳞(500013) / 龙裔头盔(500003) 等真实装备
            // 作为克隆源；这些装备此时已通过 LoadAllEquipment 进入 ItemAssetsCollection。
            // NewWeaponPlaceholderRegistry 内部会在创建占位符后直接调用对应 WeaponConfig.TryConfigure，
            // 因此 AssetBundle 缺失时占位符也能拿到完整 Stats / 标签 / Buff 配置。
            // ConfigureNewWeaponsAfterLoad 只针对真正从 AssetBundle 加载到 ItemFactory.loadedItems 的物品生效，
            // 占位符不在那条路径上，因此两路互不冲突。
            try
            {
                NewWeaponPlaceholderRegistry.EnsureAllRegistered();
                SetBonusPlaceholderRegistry.EnsureAllRegistered();
            }
            catch (Exception placeholderEx)
            {
                DevLog("[BossRush] 装备占位符注册失败: " + placeholderEx.Message);
            }

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

            // P0 新武器扩展：配置五把新武器
            ConfigureNewWeaponsAfterLoad();
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

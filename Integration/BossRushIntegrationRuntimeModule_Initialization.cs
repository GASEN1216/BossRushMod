using System;
using BossRush.Utils;
using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    internal sealed partial class IntegrationRuntimeModule
    {
        internal void InitializeDynamicItems_Integration()
        {
            if (_owner.IntegrationDynamicItemsInitialized)
            {
                return;
            }
            _owner.IntegrationDynamicItemsInitialized = true;

            try
            {
                if (!_owner.EnsureBossRushTicketItemRegisteredForDynamicRegistry())
                {
                    ModBehaviour.DevLog("[BossRush] BossRush 船票按需注册失败，继续加载其他动态物品");
                }

                ModBehaviour.DevLog("[BossRush] BossRush 船票注册检查完成，BossRushTicketTypeId=" + _owner.IntegrationBossRushTicketTypeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] InitializeDynamicItems 出错: " + e.Message);
            }

            // 初始化 ItemFactory（加载消耗品等非装备类物品）
            try
            {
                // 注册物品配置器（必须在 LoadAllItems 之前）
                _owner.EnsureItemContentConfiguratorsRegisteredForDynamicRegistry();

                int itemCount = ItemFactory.LoadedItemCount;
                if (itemCount > 0)
                {
                    ModBehaviour.DevLog("[BossRush] ItemFactory loaded " + itemCount + " items");
                }

                AwenLootSweepTokenConfig.EnsureRuntimeRegistration();
                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                ZombieTideBeaconConfig.EnsureRuntimeFallbackRegistrationShell();
                PortableSafeZoneDeviceConfig.EnsureRuntimeFallbackRegistrationShell();

                PeaceCharmRuntime.InitializeRuntime();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ItemFactory 初始化失败: " + e.Message);
            }
        }

        internal static void InjectBossRushTicketLocalization_Integration(int ticketTypeId)
        {
            LocalizationInjector.InjectTicketLocalization(ticketTypeId);
            ModBehaviour.DevLog("[BossRush] 船票本地化注入完成");
        }

        internal static void InjectModeFItemLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                // 血猎收发器
                InjectModeFItemLoc(BloodhuntTransponderConfig.LOC_KEY_DISPLAY, BloodhuntTransponderConfig.TYPE_ID,
                    BloodhuntTransponderConfig.DISPLAY_NAME_CN, BloodhuntTransponderConfig.DISPLAY_NAME_EN,
                    BloodhuntTransponderConfig.DESCRIPTION_CN, BloodhuntTransponderConfig.DESCRIPTION_EN, isChinese);

                // 折叠掩体包
                InjectModeFItemLoc(FoldableCoverPackConfig.LOC_KEY_DISPLAY, FoldableCoverPackConfig.TYPE_ID,
                    FoldableCoverPackConfig.DISPLAY_NAME_CN, FoldableCoverPackConfig.DISPLAY_NAME_EN,
                    FoldableCoverPackConfig.DESCRIPTION_CN, FoldableCoverPackConfig.DESCRIPTION_EN, isChinese);

                // 加固路障包
                InjectModeFItemLoc(ReinforcedRoadblockPackConfig.LOC_KEY_DISPLAY, ReinforcedRoadblockPackConfig.TYPE_ID,
                    ReinforcedRoadblockPackConfig.DISPLAY_NAME_CN, ReinforcedRoadblockPackConfig.DISPLAY_NAME_EN,
                    ReinforcedRoadblockPackConfig.DESCRIPTION_CN, ReinforcedRoadblockPackConfig.DESCRIPTION_EN, isChinese);

                // 阻滞铁丝网包
                InjectModeFItemLoc(BarbedWirePackConfig.LOC_KEY_DISPLAY, BarbedWirePackConfig.TYPE_ID,
                    BarbedWirePackConfig.DISPLAY_NAME_CN, BarbedWirePackConfig.DISPLAY_NAME_EN,
                    BarbedWirePackConfig.DESCRIPTION_CN, BarbedWirePackConfig.DESCRIPTION_EN, isChinese);

                // 应急维修喷剂
                InjectModeFItemLoc(EmergencyRepairSprayConfig.LOC_KEY_DISPLAY, EmergencyRepairSprayConfig.TYPE_ID,
                    EmergencyRepairSprayConfig.DISPLAY_NAME_CN, EmergencyRepairSprayConfig.DISPLAY_NAME_EN,
                    EmergencyRepairSprayConfig.DESCRIPTION_CN, EmergencyRepairSprayConfig.DESCRIPTION_EN, isChinese);

                // 宿命回响信物（Mode G 入场物品）
                InjectModeFItemLoc(FateEchoRelicConfig.LOC_KEY_DISPLAY, FateEchoRelicConfig.TYPE_ID,
                    FateEchoRelicConfig.DISPLAY_NAME_CN, FateEchoRelicConfig.DISPLAY_NAME_EN,
                    FateEchoRelicConfig.DESCRIPTION_CN, FateEchoRelicConfig.DESCRIPTION_EN, isChinese);

                ModBehaviour.DevLog("[BossRush] Mode F 物品本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] Mode F 物品本地化注入失败: " + e.Message);
            }
        }

        internal static void InjectModeFItemLoc(string locKey, int typeId, string nameCN, string nameEN, string descCN, string descEN, bool isChinese)
        {
            string displayName = isChinese ? nameCN : nameEN;
            string description = isChinese ? descCN : descEN;

            LocalizationHelper.InjectLocalization(locKey, displayName);
            LocalizationHelper.InjectLocalization(locKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + typeId, displayName);
            LocalizationHelper.InjectLocalization("Item_" + typeId + "_Desc", description);
            LocalizationHelper.InjectLocalization(nameCN, displayName);
            LocalizationHelper.InjectLocalization(nameEN, displayName);
        }

        internal void RegisterCustomWeaponRuntimeConfigs()
        {
            NewWeaponRuntime.RegisterRuntimeConfigs();
            CustomItemRuntimeStateHelper.RegisterGunRuntimeConfiguredItem(
                DragonBreathWeaponConfig.WEAPON_TYPE_ID,
                DragonBreathWeaponConfig.ConfigureWeapon,
                "龙息",
                null,
                true);

            CustomItemRuntimeStateHelper.RegisterGunRuntimeConfiguredItem(
                DragonKingBossGunConfig.WeaponTypeId,
                DragonKingBossGunConfig.ConfigureWeapon,
                "焚皇铳",
                DragonKingBossGunRuntime.RefreshAmmoProfileAfterRuntimeRestore);

            CustomItemRuntimeStateHelper.RegisterMeleeRuntimeConfiguredItem(
                FenHuangHalberdIds.WeaponTypeId,
                delegate(Item item)
                {
                    FenHuangHalberdWeaponConfig.TryConfigure(item, "FenHuangHalberd");
                },
                "焚皇断界戟",
                null,
                FenHuangHalberdWeaponConfig.PrepareRuntimeHoldAgentVisual);

            CustomItemRuntimeStateHelper.RegisterMeleeRuntimeConfiguredItem(
                FrostmourneIds.WeaponTypeId,
                delegate(Item item)
                {
                    FrostmourneWeaponConfig.TryConfigure(item, "Frostmourne");
                },
                "霜之哀伤",
                null,
                FrostmourneWeaponConfig.PrepareRuntimeHoldAgentVisual);

            CustomItemRuntimeStateHelper.RegisterMeleeRuntimeConfiguredItem(
                PhantomWitchConfig.ReservedScytheTypeId,
                delegate(Item item)
                {
                    PhantomWitchScytheWeaponConfig.TryConfigure(item);
                },
                "噬魂挽歌",
                null,
                PhantomWitchScytheWeaponConfig.PrepareRuntimeHoldAgentVisual);
        }

        internal void OnFenHuangHalberdLoaded(Item itemPrefab)
        {
            FenHuangHalberdWeaponConfig.TryConfigure(itemPrefab, "FenHuangHalberd");
        }

        internal void OnFrostmourneLoaded(Item itemPrefab)
        {
            FrostmourneWeaponConfig.TryConfigure(itemPrefab, "Frostmourne");
        }

        internal void OnPhantomWitchScytheLoaded(Item itemPrefab)
        {
            PhantomWitchScytheWeaponConfig.TryConfigure(itemPrefab);
        }

        internal void OnAdventureJournalLoaded(Item itemPrefab)
        {
            if (itemPrefab == null) return;
            EquipmentHelper.AddTagToItem(itemPrefab, "Special");
            ModBehaviour.DevLog("[BossRush] 冒险家日志已添加 Special 标签");
        }

        internal int TryInjectAllBossRushItemsIntoShop(StockShop shop)
        {
            int injectedCount = 0;
            if (TryInjectBossRushTicketIntoShop(shop)) injectedCount++;
            if (TryInjectAdventureJournalIntoShop(shop)) injectedCount++;
            if (_owner.TryInjectAchievementMedalIntoShop(shop)) injectedCount++;
            if (AwenCourierTokenConfig.TryInjectIntoShop(shop, _owner)) injectedCount++;
            if (TryInjectBrickStoneIntoShop(shop)) injectedCount++;
            if (ZombieTideInvitationConfig.TryInjectIntoShop(shop, _owner)) injectedCount++;
            injectedCount += FactionFlagConfig.TryInjectIntoShop(shop);
            if (BloodhuntTransponderConfig.TryInjectIntoShop(shop, _owner)) injectedCount++;
            if (FateEchoRelicConfig.TryInjectIntoShop(shop, _owner)) injectedCount++;
            // 鸭皇图鉴：面板的唯一入口就是这本书，漏了这行整个图鉴系统买不到也打不开。
            // 内部自带基地普通商人判定与开关 dormant 早返。
            if (_owner.TryInjectCodexBookIntoShop(shop)) injectedCount++;
            // 后山种子（菜地开放后才挂，内部判开关与解锁）：Boss 掉落之外的稳定来源。
            injectedCount += BackMountainItems.TryInjectSeedsIntoShop(shop, _owner);
            return injectedCount;
        }
    }
}

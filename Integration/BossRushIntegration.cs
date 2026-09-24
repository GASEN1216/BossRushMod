// ============================================================================
// Integration.cs - 游戏系统集成
// ============================================================================
// 模块说明：
//   管理 BossRush 模组与游戏系统的集成，包括：
//   - 动态物品初始化（BossRush 船票）
//   - 本地化注入（中文显示名称和描述）
//   - 商店注入（将船票添加到商店）
//   - 场景加载事件处理
//
// 主要功能：
//   - InitializeDynamicItems: 从 AssetBundle 加载船票物品
//   - InjectBossRushTicketLocalization: 注入船票的本地化文本
//   - InjectBossRushTicketIntoShops: 将船票添加到游戏商店
//   - OnSceneLoaded: 场景加载后的初始化处理
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 游戏系统集成模块
    /// </summary>
    public partial class ModBehaviour
    {
        internal int IntegrationBossRushTicketTypeId { get { return bossRushTicketTypeId; } }
        internal string IntegrationBaseSceneName { get { return BaseSceneName; } }
        // ModBehaviour 的旧重置点仍通过此兼容属性写入模块状态。
        private int item105PurchaseCount
        {
            get { return bossRushIntegrationRuntime.Item105PurchaseCount; }
            set { bossRushIntegrationRuntime.Item105PurchaseCount = value; }
        }

        internal bool IsIntegrationAmmoShop(StockShop shop) { return ammoShop != null && shop == ammoShop; }

        internal WaitForSeconds IntegrationSharedWait05s { get { return sharedWait05s; } }

        internal WaitForSeconds IntegrationSharedWait1s { get { return sharedWait1s; } }

        private void LogIntegrationWarningLimited(string key, string message, Exception e = null) { bossRushIntegrationRuntime.LogIntegrationWarningLimited(key, message, e); }

        private bool ReadMainExistsWithWarning(string context) { return bossRushIntegrationRuntime.ReadMainExistsWithWarning(context); }

        private bool ReadLevelInitedWithWarning(string context) { return bossRushIntegrationRuntime.ReadLevelInitedWithWarning(context); }

        private bool ReadSceneLoaderDoneWithWarning(string context) { return bossRushIntegrationRuntime.ReadSceneLoaderDoneWithWarning(context); }

        private bool ReadCameraExistsWithWarning(string context) { return bossRushIntegrationRuntime.ReadCameraExistsWithWarning(context); }

        private string ReadActiveSceneNameWithWarning(string context) { return bossRushIntegrationRuntime.ReadActiveSceneNameWithWarning(context); }

        private void InvalidateIntegrationStockShopCache() { bossRushIntegrationRuntime.InvalidateIntegrationStockShopCache(); }

        internal bool IsBaseHubNormalMerchantShop(StockShop shop) { return bossRushIntegrationRuntime.IsBaseHubNormalMerchantShop(shop); }

        internal bool TryInjectBossRushTicketIntoShop(StockShop shop) { return bossRushIntegrationRuntime.TryInjectBossRushTicketIntoShop(shop); }

        internal bool TryInjectAdventureJournalIntoShop(StockShop shop) { return bossRushIntegrationRuntime.TryInjectAdventureJournalIntoShop(shop); }

        internal bool TryInjectBrickStoneIntoShop(StockShop shop) { return bossRushIntegrationRuntime.TryInjectBrickStoneIntoShop(shop); }

        internal int TryInjectAllBossRushItemsIntoShop(StockShop shop)
        {
            int injectedCount = 0;
            if (TryInjectBossRushTicketIntoShop(shop)) injectedCount++;
            if (TryInjectAdventureJournalIntoShop(shop)) injectedCount++;
            if (TryInjectAchievementMedalIntoShop(shop)) injectedCount++;
            if (AwenCourierTokenConfig.TryInjectIntoShop(shop, this)) injectedCount++;
            if (TryInjectBrickStoneIntoShop(shop)) injectedCount++;
            if (ZombieTideInvitationConfig.TryInjectIntoShop(shop, this)) injectedCount++;
            injectedCount += FactionFlagConfig.TryInjectIntoShop(shop);
            if (BloodhuntTransponderConfig.TryInjectIntoShop(shop, this)) injectedCount++;
            if (FateEchoRelicConfig.TryInjectIntoShop(shop, this)) injectedCount++;
            // 鸭皇图鉴：面板的唯一入口就是这本书，漏了这行整个图鉴系统买不到也打不开。
            // 内部自带基地普通商人判定与开关 dormant 早返。
            if (TryInjectCodexBookIntoShop(shop)) injectedCount++;
            // 后山种子（菜地开放后才挂，内部判开关与解锁）：Boss 掉落之外的稳定来源。
            injectedCount += BackMountainItems.TryInjectSeedsIntoShop(shop, this);
            return injectedCount;
        }

        /// <summary>
        /// 初始化动态物品（从 AssetBundle 加载 BossRush 船票）
        /// </summary>
        private void InitializeDynamicItems_Integration()
        {
            if (dynamicItemsInitialized)
            {
                return;
            }
            dynamicItemsInitialized = true;

            try
            {
                if (!EnsureBossRushTicketItemRegisteredForDynamicRegistry())
                {
                    DevLog("[BossRush] BossRush 船票按需注册失败，继续加载其他动态物品");
                }

                DevLog("[BossRush] BossRush 船票注册检查完成，BossRushTicketTypeId=" + bossRushTicketTypeId);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] InitializeDynamicItems 出错: " + e.Message);
            }

            // 初始化 ItemFactory（加载消耗品等非装备类物品）
            try
            {
                // 注册物品配置器（必须在 LoadAllItems 之前）
                RegisterItemContentConfigurators();

                int itemCount = ItemFactory.LoadedItemCount;
                if (itemCount > 0)
                {
                    DevLog("[BossRush] ItemFactory loaded " + itemCount + " items");
                }

                AwenLootSweepTokenConfig.EnsureRuntimeRegistration();
                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                ZombieTideBeaconConfig.EnsureRuntimeFallbackRegistrationShell();
                PortableSafeZoneDeviceConfig.EnsureRuntimeFallbackRegistrationShell();

                PeaceCharmRuntime.InitializeRuntime();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ItemFactory 初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注入 BossRush 船票本地化（委托给 LocalizationInjector）
        /// </summary>
        private static void InjectBossRushTicketLocalization_Integration()
        {
            LocalizationInjector.InjectTicketLocalization(bossRushTicketTypeId);
            DevLog("[BossRush] 船票本地化注入完成");
        }

        /// <summary>
        /// 注入 Mode F 物品本地化
        /// </summary>
        private static void InjectModeFItemLocalization()
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

                DevLog("[BossRush] Mode F 物品本地化注入完成");
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] Mode F 物品本地化注入失败: " + e.Message);
            }
        }

        private static void InjectModeFItemLoc(string locKey, int typeId, string nameCN, string nameEN, string descCN, string descEN, bool isChinese)
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

        /// <summary>
        /// 注册“需要运行时补配基础配置”的自定义武器。
        /// 以后新增同类武器时，只需要在这里追加一条注册，
        /// 不需要再去改快递/仓库/切图/重铸恢复链路。
        /// </summary>
        private void RegisterCustomWeaponRuntimeConfigs()
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

        private void OnFenHuangHalberdLoaded(Item itemPrefab)
        {
            FenHuangHalberdWeaponConfig.TryConfigure(itemPrefab, "FenHuangHalberd");
        }

        private void OnFrostmourneLoaded(Item itemPrefab)
        {
            FrostmourneWeaponConfig.TryConfigure(itemPrefab, "Frostmourne");
        }

        private void OnPhantomWitchScytheLoaded(Item itemPrefab)
        {
            PhantomWitchScytheWeaponConfig.TryConfigure(itemPrefab);
        }

        private void OnAdventureJournalLoaded(Item itemPrefab)
        {
            if (itemPrefab == null) return;
            EquipmentHelper.AddTagToItem(itemPrefab, "Special");
            DevLog("[BossRush] 冒险家日志已添加 Special 标签");
        }

        /// <summary>
        /// 将船票注入到商店
        /// </summary>
        /// <param name="targetSceneName">目标场景名称，如果为null则使用GetActiveScene</param>
        private void InjectBossRushTicketIntoShops_Integration(string targetSceneName = null) { bossRushIntegrationRuntime.InjectBossRushTicketIntoShops_Integration(targetSceneName); }

        /// <summary>
        /// 将冒险家日志注入到商店（与船票逻辑相同）
        /// </summary>
        /// <param name="targetSceneName">目标场景名称，如果为null则使用GetActiveScene</param>
        private void InjectAdventureJournalIntoShops_Integration(string targetSceneName = null) { bossRushIntegrationRuntime.InjectAdventureJournalIntoShops_Integration(targetSceneName); }

        // ============================================================================
        // 砖石商店注入
        // ============================================================================

        /// <summary>
        /// 将砖石注入到商店
        /// </summary>
        private void InjectBrickStoneIntoShops(string targetSceneName = null) { bossRushIntegrationRuntime.InjectBrickStoneIntoShops(targetSceneName); }

    }
}

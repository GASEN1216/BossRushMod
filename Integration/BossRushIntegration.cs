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
        internal bool EnsureItemContentConfiguratorsRegisteredForDynamicRegistry() { return bossRushIntegrationRuntime.EnsureItemContentConfiguratorsRegisteredForDynamicRegistry(); }
        internal bool EnsureBossRushTicketItemRegisteredForDynamicRegistry() { return bossRushIntegrationRuntime.EnsureBossRushTicketItemRegisteredForDynamicRegistry(); }
        internal bool EnsureBirthdayCakeItemRegisteredForDynamicRegistry() { return bossRushIntegrationRuntime.EnsureBirthdayCakeItemRegisteredForDynamicRegistry(); }
        internal bool EnsureAdventureJournalItemRegisteredForDynamicRegistry() { return bossRushIntegrationRuntime.EnsureAdventureJournalItemRegisteredForDynamicRegistry(); }

        internal int IntegrationBossRushTicketTypeId { get { return IntegrationRuntimeModule.BossRushTicketTypeId; } }
        internal string IntegrationBaseSceneName { get { return BaseSceneName; } }
        internal bool IntegrationDynamicItemsInitialized
        {
            get { return IntegrationRuntimeModule.DynamicItemsInitialized; }
            set { IntegrationRuntimeModule.DynamicItemsInitialized = value; }
        }
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
            return bossRushIntegrationRuntime.TryInjectAllBossRushItemsIntoShop(shop);
        }

        /// <summary>
        /// 初始化动态物品（从 AssetBundle 加载 BossRush 船票）
        /// </summary>
        private void InitializeDynamicItems_Integration()
        {
            bossRushIntegrationRuntime.InitializeDynamicItems_Integration();
        }

        /// <summary>
        /// 注入 BossRush 船票本地化（委托给 LocalizationInjector）
        /// </summary>
        private static void InjectBossRushTicketLocalization_Integration()
        {
            IntegrationRuntimeModule.InjectBossRushTicketLocalization_Integration(bossRushTicketTypeId);
        }

        /// <summary>
        /// 注入 Mode F 物品本地化
        /// </summary>
        private static void InjectModeFItemLocalization()
        {
            IntegrationRuntimeModule.InjectModeFItemLocalization();
        }

        private static void InjectModeFItemLoc(string locKey, int typeId, string nameCN, string nameEN, string descCN, string descEN, bool isChinese)
        {
            IntegrationRuntimeModule.InjectModeFItemLoc(locKey, typeId, nameCN, nameEN, descCN, descEN, isChinese);
        }

        /// <summary>
        /// 注册“需要运行时补配基础配置”的自定义武器。
        /// 以后新增同类武器时，只需要在这里追加一条注册，
        /// 不需要再去改快递/仓库/切图/重铸恢复链路。
        /// </summary>
        private void RegisterCustomWeaponRuntimeConfigs()
        {
            bossRushIntegrationRuntime.RegisterCustomWeaponRuntimeConfigs();
        }

        private void OnFenHuangHalberdLoaded(Item itemPrefab)
        {
            bossRushIntegrationRuntime.OnFenHuangHalberdLoaded(itemPrefab);
        }

        private void OnFrostmourneLoaded(Item itemPrefab)
        {
            bossRushIntegrationRuntime.OnFrostmourneLoaded(itemPrefab);
        }

        private void OnPhantomWitchScytheLoaded(Item itemPrefab)
        {
            bossRushIntegrationRuntime.OnPhantomWitchScytheLoaded(itemPrefab);
        }

        private void OnAdventureJournalLoaded(Item itemPrefab)
        {
            bossRushIntegrationRuntime.OnAdventureJournalLoaded(itemPrefab);
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

// Integration 装配与内容注册的兼容入口；实际状态与算法由既有 runtime/service 持有。
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
using System.Collections;

namespace BossRush
{
    public partial class ModBehaviour
    {
        #region BossRushIntegration
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


        #endregion

        #region BirthdayCakeItem
// ============================================================================
// BirthdayCakeItem.cs - 生日蛋糕物品
// ============================================================================
// 模块说明：
//   管理生日蛋糕物品的加载、配置和注册，包括：
//   - 从 AssetBundle 加载生日蛋糕预制体
//   - 动态添加食物功能（FoodDrink）
//   - 动态添加 Buff 效果（高兴）
//   - 动态添加 Tag（食物、收藏品）
//   - 本地化注入
//   - 商店注入
// ============================================================================

        private void InitializeBirthdayCakeItem()
        {
            bossRushIntegrationRuntime.InitializeBirthdayCakeItem();
        }

        private void InjectBirthdayCakeLocalization()
        {
            bossRushIntegrationRuntime.InjectBirthdayCakeLocalization();
        }

        private void AddTagsToItem(Item itemPrefab, string[] tagNames)
        {
            bossRushIntegrationRuntime.AddTagsToItem(itemPrefab, tagNames);
        }

        public void DebugGiveBirthdayCake()
        {
            bossRushIntegrationRuntime.DebugGiveBirthdayCake();
        }

        #endregion

        #region WikiBookItem
// ============================================================================
// WikiBookItem.cs - Wiki 百科全书物品
// ============================================================================
// 模块说明：
//   管理 Wiki Book 物品的加载、配置和注册，包括：
//   - 从 AssetBundle 加载 Wiki UI 和书物品预制体
//   - 动态添加使用行为（打开 Wiki UI，不消耗物品）
//   - 本地化注入
// ============================================================================

        private void InitializeWikiBookItem()
        {
            bossRushIntegrationRuntime.InitializeWikiBookItem();
        }

        private void InjectWikiBookLocalization()
        {
            bossRushIntegrationRuntime.InjectWikiBookLocalization();
        }

        #endregion

        #region CodexBookItem
// ============================================================================
// CodexBookItem.cs - 鸭皇图鉴（可用物品）+ 基地商店注入
// ============================================================================
// TypeID 500061（台账见 docs/reference/Bossrush使用物品ID表.md，AGENTS.md 4.3 严格递增不复用）。
//
// 设计要点：
//   - **零新增 bundle**：没有专属模型，走 BossRushDynamicItemRegistry 的
//     FallbackLoader 从既有物品克隆一份 prefab 顶上（形态照 RelicEggConfig），
//     图标由 EquipmentHelperIcon 从 Assets/Items/codex_book.png 注入。
//   - **使用后不消耗**：耐久模式 MaxDurability/Durability = 999f，
//     UsageBehavior 只打开面板，不 Consume（形态照 AchievementMedalConfig）。
//   - **DisplayNameRaw 必须配本地化注入**（AGENTS.md 4.4）：本物品的全部 key
//     由 Localization/CodexLocalization.cs 单点注入，本文件的 InjectLocalization()
//     只是转发门面，不再各写一份，避免两处文案漂移。
//   - 商店注入与库存持久化形态照 Achievement/AchievementMedalItem.cs：
//     只进基地普通商人（IsBaseHubNormalMerchantShop），库存跟存档走。
//   - **dormant 纪律**：codexEnabled 关闭时不注入商店条目——开关关掉之后连
//     买入口都不该出现。
// ============================================================================

        internal bool TryInjectCodexBookIntoShop(StockShop shop)
        {
            return bossRushIntegrationRuntime.TryInjectCodexBookIntoShop(shop);
        }

        internal void InjectCodexBookIntoShops(string targetSceneName = null)
        {
            bossRushIntegrationRuntime.InjectCodexBookIntoShops(targetSceneName);
        }


        #endregion

        #region BossRushIntegration_MapObjectsAndDragonBreath

        // 原场景初始化调用点保留，地图对象业务由唯一 IntegrationRuntimeModule 执行。
        private void SpawnBossRushMapObjects()
        {
            bossRushIntegrationRuntime.SpawnBossRushMapObjects();
        }

        // ModBehaviour 的既有公开流程继续取得原方法签名和协程时序。
        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)
        {
            return bossRushIntegrationRuntime.WaitForLevelInitializedThenSetup_Integration(scene);
        }

        // 模块等待完成后，在宿主上启动原有场景 setup 协程。
        internal void StartBossRushDemoChallengeSetupForScene(Scene scene)
        {
            StartCoroutine(SetupBossRushInDemoChallenge(scene));
        }

        // 外部装备内容注册器使用的兼容入口；订阅 owner 与 delegate 都在 IntegrationRuntimeModule。
        private void UnsubscribeDragonBreathEffectEvent()
        {
            bossRushIntegrationRuntime.UnsubscribeDragonBreathEffectEvent();
        }

        #endregion

        #region IntegrationDeferredBootstrap

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
                ScheduleWishRewardPoolWarmup = bossRushIntegrationRuntime.ScheduleWishRewardPoolWarmup,
            };
        }

        #endregion

        #region IntegrationRuntimeHooks

        internal void StartIntegrationRuntime()
        {
            Start_Integration();
        }

        internal void OnSceneLoadedIntegrationRuntime(Scene scene, LoadSceneMode mode)
        {
            // 与遗种掉落同样按场景释放 per-character 订阅；不能等下一只 Boss 生成才裁剪死引用。
            AffixForgeStoneDropService.ClearAllTracking();
            OnSceneLoaded_Integration(scene, mode);
        }

        internal void CleanupIntegrationRuntimeOnDestroy()
        {
            SafeRuntime.Run("WikiUIManager.Shutdown", WikiUIManager.Shutdown);
            SafeRuntime.Run("ImageViewerUI.ResetStaticCaches", () => ImageViewerUI.ResetStaticCaches());
            OnDestroy_Integration();
        }

        #endregion

        #region EquipmentContentRegistry

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

        #endregion
    }
}

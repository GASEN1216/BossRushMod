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
        // 物品 ID 105 购买计数器（用于检测进货行为）
        private int item105PurchaseCount = 0;

        // BossRush 船票库存持久化相关
        private const string TICKET_STOCK_SAVE_KEY = "BossRush_TicketStock";
        private const int TICKET_DEFAULT_MAX_STOCK = 10;
        private static int cachedTicketStock = -1;  // -1 表示未初始化，需要从存档读取
        private static StockShop.Entry injectedTicketEntry = null;  // 缓存注入的船票条目引用

        // 冒险家日志库存持久化相关
        private const int ADVENTURE_JOURNAL_TYPE_ID = BossRushItemIds.AdventureJournal;  // 冒险家日志物品 ID
        private const string JOURNAL_STOCK_SAVE_KEY = "BossRush_JournalStock";
        private const int JOURNAL_DEFAULT_MAX_STOCK = 1;  // 最大库存为1
        private static int cachedJournalStock = -1;  // -1 表示未初始化，需要从存档读取
        private static StockShop.Entry injectedJournalEntry = null;  // 缓存注入的冒险家日志条目引用

        // 砖石库存持久化相关
        private static int cachedBrickStoneStock = -1;  // -1 表示未初始化，需要从存档读取
        private static StockShop.Entry injectedBrickStoneEntry = null;  // 缓存注入的砖石条目引用
        private Coroutine runtimeStateMonitorCoroutine;
        private const float INTEGRATION_WARNING_LOG_INTERVAL = 5f;
        private readonly Dictionary<string, float> integrationNextWarningLogTimes = new Dictionary<string, float>();
        private StockShop[] cachedIntegrationStockShops = null;
        private string cachedIntegrationStockShopsSceneName = null;

        private void LogIntegrationWarningLimited(string key, string message, Exception e = null)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message))
            {
                return;
            }

            float now = Time.unscaledTime;
            float nextLogTime;
            if (integrationNextWarningLogTimes.TryGetValue(key, out nextLogTime) && now < nextLogTime)
            {
                return;
            }

            integrationNextWarningLogTimes[key] = now + INTEGRATION_WARNING_LOG_INTERVAL;
            DevLog("[BossRush] [WARNING] " + message + (e != null ? ": " + e.Message : string.Empty));
        }

        private bool ReadMainExistsWithWarning(string context)
        {
            try
            {
                return CharacterMainControl.Main != null;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(context + "_main", context + " 读取 CharacterMainControl.Main 失败", e);
                return false;
            }
        }

        private bool ReadLevelInitedWithWarning(string context)
        {
            try
            {
                return LevelManager.LevelInited;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(context + "_level", context + " 读取 LevelManager.LevelInited 失败", e);
                return false;
            }
        }

        private bool ReadSceneLoaderDoneWithWarning(string context)
        {
            try
            {
                return !SceneLoader.IsSceneLoading;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(context + "_loader", context + " 读取 SceneLoader 状态失败", e);
                return true;
            }
        }

        private bool ReadCameraExistsWithWarning(string context)
        {
            try
            {
                return GameCamera.Instance != null;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(context + "_camera", context + " 读取 GameCamera.Instance 失败", e);
                return false;
            }
        }

        private string ReadActiveSceneNameWithWarning(string context)
        {
            try
            {
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(context + "_scene", context + " 读取当前场景名失败", e);
                return string.Empty;
            }
        }

        private void InvalidateIntegrationStockShopCache()
        {
            cachedIntegrationStockShops = null;
            cachedIntegrationStockShopsSceneName = null;
        }

        private bool HasValidCachedIntegrationStockShops(string sceneName)
        {
            if (cachedIntegrationStockShops == null || cachedIntegrationStockShopsSceneName != sceneName)
            {
                return false;
            }

            if (cachedIntegrationStockShops.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < cachedIntegrationStockShops.Length; i++)
            {
                StockShop shop = cachedIntegrationStockShops[i];
                if (shop == null || shop.gameObject == null)
                {
                    return false;
                }
            }

            return true;
        }

        private StockShop[] GetIntegrationStockShops(string sceneName, string context)
        {
            if (sceneName != BaseSceneName)
            {
                return null;
            }

            if (HasValidCachedIntegrationStockShops(sceneName))
            {
                return cachedIntegrationStockShops;
            }

            try
            {
                cachedIntegrationStockShops = UnityEngine.Object.FindObjectsOfType<StockShop>();
                cachedIntegrationStockShopsSceneName = sceneName;
                return cachedIntegrationStockShops;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    context + "_shop_scan",
                    context + " 扫描商店失败",
                    e);
                InvalidateIntegrationStockShopCache();
                return null;
            }
        }

        internal bool IsBaseHubNormalMerchantShop(StockShop shop)
        {
            if (shop == null || shop.entries == null || shop.gameObject == null)
            {
                return false;
            }

            bool isNpcShop = false;
            try
            {
                isNpcShop = shop.GetComponentInParent<CharacterMainControl>() != null;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "IsBaseHubNormalMerchantShop_npc",
                    "IsBaseHubNormalMerchantShop 检查 NPC 商店失败",
                    e);
            }

            if (isNpcShop)
            {
                return false;
            }

            string sceneName = string.Empty;
            string merchantId = string.Empty;
            try
            {
                sceneName = shop.gameObject.scene.name;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "IsBaseHubNormalMerchantShop_scene",
                    "IsBaseHubNormalMerchantShop 读取商店场景名失败",
                    e);
            }

            try
            {
                merchantId = shop.MerchantID;
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "IsBaseHubNormalMerchantShop_merchant",
                    "IsBaseHubNormalMerchantShop 读取商人 ID 失败",
                    e);
            }

            return merchantId == "Merchant_Normal" && sceneName == BaseSceneName;
        }

        internal bool TryInjectBossRushTicketIntoShop(StockShop shop)
        {
            if (bossRushTicketTypeId <= 0 || !IsBaseHubNormalMerchantShop(shop))
            {
                return false;
            }

            bool alreadyExists = false;
            foreach (StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == bossRushTicketTypeId)
                {
                    alreadyExists = true;
                    injectedTicketEntry = entry;
                    break;
                }
            }

            if (alreadyExists)
            {
                return false;
            }

            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
            itemEntry.typeID = bossRushTicketTypeId;
            itemEntry.maxStock = TICKET_DEFAULT_MAX_STOCK;
            itemEntry.forceUnlock = true;
            itemEntry.priceFactor = 1f;
            itemEntry.possibility = 1f;
            itemEntry.lockInDemo = false;

            StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
            int stockToSet = LoadTicketStockFromSave();
            wrapped.CurrentStock = stockToSet;
            wrapped.Show = true;

            injectedTicketEntry = wrapped;
            shop.entries.Add(wrapped);
            DevLog("[BossRush] 船票注入成功，库存设置为: " + stockToSet);
            return true;
        }

        internal bool TryInjectAdventureJournalIntoShop(StockShop shop)
        {
            if (!IsBaseHubNormalMerchantShop(shop))
            {
                return false;
            }

            bool alreadyExists = false;
            foreach (StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == ADVENTURE_JOURNAL_TYPE_ID)
                {
                    alreadyExists = true;
                    injectedJournalEntry = entry;
                    break;
                }
            }

            if (alreadyExists)
            {
                return false;
            }

            float priceFactor = 1f;
            try
            {
                Item itemPrefab = ItemAssetsCollection.GetPrefab(ADVENTURE_JOURNAL_TYPE_ID);
                if (itemPrefab != null)
                {
                    int rawValue = itemPrefab.GetTotalRawValue();
                    if (rawValue > 0)
                    {
                        priceFactor = 1f / rawValue;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 计算冒险家日志价格系数失败，已回退默认价格: " + e.Message);
            }

            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
            itemEntry.typeID = ADVENTURE_JOURNAL_TYPE_ID;
            itemEntry.maxStock = JOURNAL_DEFAULT_MAX_STOCK;
            itemEntry.forceUnlock = true;
            itemEntry.priceFactor = priceFactor;
            itemEntry.possibility = 1f;
            itemEntry.lockInDemo = false;

            StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
            int stockToSet = LoadJournalStockFromSave();
            wrapped.CurrentStock = stockToSet;
            wrapped.Show = true;

            injectedJournalEntry = wrapped;
            shop.entries.Insert(0, wrapped);
            DevLog("[BossRush] 冒险家日志注入成功，库存设置为: " + stockToSet + ", priceFactor=" + priceFactor);
            return true;
        }

        internal bool TryInjectBrickStoneIntoShop(StockShop shop)
        {
            if (!IsBaseHubNormalMerchantShop(shop))
            {
                return false;
            }

            bool alreadyExists = false;
            foreach (StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == BrickStoneConfig.TYPE_ID)
                {
                    alreadyExists = true;
                    injectedBrickStoneEntry = entry;
                    break;
                }
            }

            if (alreadyExists)
            {
                return false;
            }

            float priceFactor = 1f;
            try
            {
                Item itemPrefab = ItemAssetsCollection.GetPrefab(BrickStoneConfig.TYPE_ID);
                if (itemPrefab != null)
                {
                    int rawValue = itemPrefab.GetTotalRawValue();
                    if (rawValue > 0)
                    {
                        priceFactor = 1f / rawValue;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BrickStone] [WARNING] 计算砖石价格系数失败，已回退默认价格: " + e.Message);
            }

            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
            itemEntry.typeID = BrickStoneConfig.TYPE_ID;
            itemEntry.maxStock = BrickStoneConfig.DEFAULT_MAX_STOCK;
            itemEntry.forceUnlock = true;
            itemEntry.priceFactor = priceFactor;
            itemEntry.possibility = 1f;
            itemEntry.lockInDemo = false;

            StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
            int stockToSet = LoadBrickStoneStockFromSave();
            wrapped.CurrentStock = stockToSet;
            wrapped.Show = true;

            injectedBrickStoneEntry = wrapped;
            shop.entries.Add(wrapped);
            DevLog("[BrickStone] 砖石注入成功，库存设置为: " + stockToSet + ", priceFactor=" + priceFactor);
            return true;
        }

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

                int itemCount = ItemFactory.LoadAllItems();
                if (itemCount > 0)
                {
                    DevLog("[BossRush] ItemFactory loaded " + itemCount + " items");
                }

                AwenLootSweepTokenConfig.EnsureRuntimeRegistration();
                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                ZombieTideBeaconConfig.EnsureRuntimeFallbackRegistrationShell();

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
            CustomItemRuntimeStateHelper.RegisterGunRuntimeConfiguredItem(
                DragonBreathWeaponConfig.WEAPON_TYPE_ID,
                DragonBreathWeaponConfig.ConfigureWeapon,
                "龙息",
                null,
                true);

            CustomItemRuntimeStateHelper.RegisterGunRuntimeConfiguredItem(
                DragonKingBossGunConfig.WeaponTypeId,
                DragonKingBossGunConfig.ConfigureWeapon,
                "焚皇铳");

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
        private void InjectBossRushTicketIntoShops_Integration(string targetSceneName = null)
        {
            if (bossRushTicketTypeId <= 0)
            {
                DevLog("[BossRush] BossRush 船票 TypeID 未初始化，跳过商店注入");
                return;
            }

            // [性能优化] 只在基地相关场景扫描商店，船票只在基地售货机出售
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                currentScene = ReadActiveSceneNameWithWarning("InjectBossRushTicketIntoShops");
            }
            if (currentScene != BaseSceneName)
            {
                return;
            }

            try
            {
                StockShop[] shops = GetIntegrationStockShops(currentScene, "InjectBossRushTicketIntoShops");
                if (shops == null || shops.Length == 0)
                {
                    DevLog("[BossRush] 未找到任何 StockShop，跳过商店扫描");
                    return;
                }

                int totalCount = 0;
                int npcShopCount = 0;
                int nonNpcShopCount = 0;
                int targetShopCount = 0;
                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null)
                    {
                        continue;
                    }
                    totalCount++;

                    bool isNpcShop = false;
                    try
                    {
                        if (shop.GetComponentInParent<CharacterMainControl>() != null)
                        {
                            isNpcShop = true;
                        }
                    }
                    catch (Exception e)
                    {
                        LogIntegrationWarningLimited(
                            "InjectBossRushTicketIntoShops_npc_scan",
                            "商店扫描时判断 NPC 商店失败",
                            e);
                    }

                    if (isNpcShop)
                    {
                        npcShopCount++;
                    }
                    else
                    {
                        nonNpcShopCount++;
                    }

                    string sceneName = "";
                    string merchantId = "";
                    string goName = "";
                    string displayName = "";

                    try
                    {
                        sceneName = shop.gameObject != null ? shop.gameObject.scene.name : "<no-go>";
                    }
                    catch (Exception e)
                    {
                        LogIntegrationWarningLimited(
                            "InjectBossRushTicketIntoShops_scene_scan",
                            "商店扫描时读取场景名失败",
                            e);
                    }

                    try
                    {
                        goName = shop.gameObject != null ? shop.gameObject.name : "<no-go>";
                    }
                    catch (Exception e)
                    {
                        LogIntegrationWarningLimited(
                            "InjectBossRushTicketIntoShops_name_scan",
                            "商店扫描时读取对象名失败",
                            e);
                    }

                    try
                    {
                        merchantId = shop.MerchantID;
                    }
                    catch (Exception e)
                    {
                        LogIntegrationWarningLimited(
                            "InjectBossRushTicketIntoShops_merchant_scan",
                            "商店扫描时读取商人 ID 失败",
                            e);
                    }

                    try
                    {
                        displayName = shop.DisplayName;
                    }
                    catch (Exception e)
                    {
                        LogIntegrationWarningLimited(
                            "InjectBossRushTicketIntoShops_display_scan",
                            "商店扫描时读取商店显示名失败",
                            e);
                    }

                    bool isTargetShop = IsBaseHubNormalMerchantShop(shop);

                    if (isTargetShop)
                    {
                        targetShopCount++;

                        if (shop.entries != null)
                        {
                            if (TryInjectBossRushTicketIntoShop(shop))
                            {
                                addedCount++;
                            }
                        }
                    }

                    DevLog("[BossRush] ShopScan: scene=" + sceneName + ", isNpcShop=" + isNpcShop + ", merchantID=" + merchantId + ", goName=" + goName + ", displayName=" + displayName + ", isTargetShop=" + isTargetShop);
                }

                DevLog("[BossRush] ShopScan summary: total=" + totalCount + ", npcShops=" + npcShopCount + ", nonNpcShops=" + nonNpcShopCount + ", targetShops=" + targetShopCount + ", added=" + addedCount + ", TypeID=" + bossRushTicketTypeId);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] InjectBossRushTicketIntoShops 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 将冒险家日志注入到商店（与船票逻辑相同）
        /// </summary>
        /// <param name="targetSceneName">目标场景名称，如果为null则使用GetActiveScene</param>
        private void InjectAdventureJournalIntoShops_Integration(string targetSceneName = null)
        {
            // 如果不在基地场景，跳过扫描
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                currentScene = ReadActiveSceneNameWithWarning("InjectAdventureJournalIntoShops");
            }
            if (currentScene != BaseSceneName)
            {
                return;
            }

            // [Bug修复] 每次场景加载都重新扫描商店，因为场景切换后商店对象会被重建
            // StockShop.Entry 不是 Unity 对象，无法通过 null 检查判断是否有效

            try
            {
                StockShop[] shops = GetIntegrationStockShops(currentScene, "InjectAdventureJournalIntoShops");
                if (shops == null || shops.Length == 0)
                {
                    return;
                }

                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null) continue;

                    if (TryInjectAdventureJournalIntoShop(shop))
                    {
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    DevLog("[BossRush] 冒险家日志商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] InjectAdventureJournalIntoShops 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 从存档读取冒险家日志库存
        /// </summary>
        private int LoadJournalStockFromSave()
        {
            try
            {
                if (cachedJournalStock >= 0)
                {
                    return cachedJournalStock;
                }

                if (SavesSystem.KeyExisits(JOURNAL_STOCK_SAVE_KEY))
                {
                    cachedJournalStock = SavesSystem.Load<int>(JOURNAL_STOCK_SAVE_KEY);
                    DevLog("[BossRush] 从存档读取冒险家日志库存: " + cachedJournalStock);
                    return cachedJournalStock;
                }

                cachedJournalStock = JOURNAL_DEFAULT_MAX_STOCK;
                return cachedJournalStock;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 读取冒险家日志库存失败: " + e.Message);
                cachedJournalStock = JOURNAL_DEFAULT_MAX_STOCK;
                return cachedJournalStock;
            }
        }

        /// <summary>
        /// 存档时保存冒险家日志库存
        /// </summary>
        private void OnCollectSaveData_JournalStock()
        {
            try
            {
                int stockToSave = 0;
                if (injectedJournalEntry != null)
                {
                    stockToSave = injectedJournalEntry.CurrentStock;
                }
                else if (cachedJournalStock >= 0)
                {
                    stockToSave = cachedJournalStock;
                }
                else
                {
                    stockToSave = JOURNAL_DEFAULT_MAX_STOCK;
                }

                SavesSystem.Save<int>(JOURNAL_STOCK_SAVE_KEY, stockToSave);
                cachedJournalStock = stockToSave;
                DevLog("[BossRush] 保存冒险家日志库存: " + stockToSave);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 保存冒险家日志库存失败: " + e.Message);
            }
        }

        /// <summary>
        /// 读档时重置冒险家日志缓存
        /// </summary>
        private void OnSetFile_JournalStock()
        {
            cachedJournalStock = -1;
            injectedJournalEntry = null;
            DevLog("[BossRush] 检测到读档，重置冒险家日志库存缓存");
        }

        // ============================================================================
        // 砖石商店注入
        // ============================================================================

        /// <summary>
        /// 将砖石注入到商店
        /// </summary>
        private void InjectBrickStoneIntoShops(string targetSceneName = null)
        {
            // 如果不在基地场景，跳过扫描
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                currentScene = ReadActiveSceneNameWithWarning("InjectBrickStoneIntoShops");
            }
            if (currentScene != BaseSceneName)
            {
                return;
            }

            try
            {
                StockShop[] shops = GetIntegrationStockShops(currentScene, "InjectBrickStoneIntoShops");
                if (shops == null || shops.Length == 0)
                {
                    return;
                }

                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null) continue;

                    if (TryInjectBrickStoneIntoShop(shop))
                    {
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    DevLog("[BrickStone] 砖石商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                DevLog("[BrickStone] InjectBrickStoneIntoShops 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 从存档读取砖石库存
        /// </summary>
        private int LoadBrickStoneStockFromSave()
        {
            try
            {
                if (cachedBrickStoneStock >= 0)
                {
                    return cachedBrickStoneStock;
                }

                if (SavesSystem.KeyExisits(BrickStoneConfig.STOCK_SAVE_KEY))
                {
                    cachedBrickStoneStock = SavesSystem.Load<int>(BrickStoneConfig.STOCK_SAVE_KEY);
                    DevLog("[BrickStone] 从存档读取砖石库存: " + cachedBrickStoneStock);
                    return cachedBrickStoneStock;
                }

                cachedBrickStoneStock = BrickStoneConfig.DEFAULT_MAX_STOCK;
                return cachedBrickStoneStock;
            }
            catch (Exception e)
            {
                DevLog("[BrickStone] 读取砖石库存失败: " + e.Message);
                cachedBrickStoneStock = BrickStoneConfig.DEFAULT_MAX_STOCK;
                return cachedBrickStoneStock;
            }
        }

        /// <summary>
        /// 存档时保存砖石库存
        /// </summary>
        private void OnCollectSaveData_BrickStoneStock()
        {
            try
            {
                int stockToSave = BrickStoneConfig.DEFAULT_MAX_STOCK;

                if (injectedBrickStoneEntry != null)
                {
                    stockToSave = injectedBrickStoneEntry.CurrentStock;
                }

                SavesSystem.Save<int>(BrickStoneConfig.STOCK_SAVE_KEY, stockToSave);
                cachedBrickStoneStock = stockToSave;
                DevLog("[BrickStone] 保存砖石库存: " + stockToSave);
            }
            catch (Exception e)
            {
                DevLog("[BrickStone] 保存砖石库存失败: " + e.Message);
            }
        }

        /// <summary>
        /// 读档时重置砖石缓存
        /// </summary>
        private void OnSetFile_BrickStoneStock()
        {
            cachedBrickStoneStock = -1;
            injectedBrickStoneEntry = null;
            DevLog("[BrickStone] 检测到读档，重置砖石库存缓存");
        }

        /// <summary>
        /// 从存档读取船票库存
        /// </summary>
        private int LoadTicketStockFromSave()
        {
            try
            {
                // 如果已有缓存值，直接返回
                if (cachedTicketStock >= 0)
                {
                    return cachedTicketStock;
                }

                // 尝试从存档读取
                if (SavesSystem.KeyExisits(TICKET_STOCK_SAVE_KEY))
                {
                    cachedTicketStock = SavesSystem.Load<int>(TICKET_STOCK_SAVE_KEY);
                    DevLog("[BossRush] 从存档读取船票库存: " + cachedTicketStock);
                    return cachedTicketStock;
                }

                // 没有存档，返回默认最大库存
                cachedTicketStock = TICKET_DEFAULT_MAX_STOCK;
                DevLog("[BossRush] 无存档，使用默认船票库存: " + cachedTicketStock);
                return cachedTicketStock;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 读取船票库存失败: " + e.Message);
                cachedTicketStock = TICKET_DEFAULT_MAX_STOCK;
                return cachedTicketStock;
            }
        }

        /// <summary>
        /// 存档时保存船票库存
        /// </summary>
        private void OnCollectSaveData_TicketStock()
        {
            try
            {
                // 优先从注入的条目获取当前库存
                int stockToSave = 0;
                if (injectedTicketEntry != null)
                {
                    stockToSave = injectedTicketEntry.CurrentStock;
                }
                else if (cachedTicketStock >= 0)
                {
                    stockToSave = cachedTicketStock;
                }
                else
                {
                    stockToSave = TICKET_DEFAULT_MAX_STOCK;
                }

                SavesSystem.Save<int>(TICKET_STOCK_SAVE_KEY, stockToSave);
                cachedTicketStock = stockToSave;
                DevLog("[BossRush] 保存船票库存: " + stockToSave);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 保存船票库存失败: " + e.Message);
            }
        }

        /// <summary>
        /// 读档时重置缓存，强制从存档重新读取
        /// </summary>
        private void OnSetFile_TicketStock()
        {
            cachedTicketStock = -1;  // 重置缓存，下次注入时会从存档读取
            injectedTicketEntry = null;  // 清除旧引用
            DevLog("[BossRush] 检测到读档，重置船票库存缓存");
        }


    }
}

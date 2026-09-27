using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    /// <summary>商店注入与库存存档状态的唯一运行时 owner。</summary>
    internal sealed partial class IntegrationRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private bool _ticketStockEventsSubscribed;
        private bool _journalStockEventsSubscribed;
        private bool _brickStoneStockEventsSubscribed;

        private const string TICKET_STOCK_SAVE_KEY = "BossRush_TicketStock";
        private const int TICKET_DEFAULT_MAX_STOCK = 10;
        private static int cachedTicketStock = -1;
        private static StockShop.Entry injectedTicketEntry = null;

        private const int ADVENTURE_JOURNAL_TYPE_ID = BossRushItemIds.AdventureJournal;
        private const string JOURNAL_STOCK_SAVE_KEY = "BossRush_JournalStock";
        private const int JOURNAL_DEFAULT_MAX_STOCK = 1;
        private static int cachedJournalStock = -1;
        private static StockShop.Entry injectedJournalEntry = null;

        private static int cachedBrickStoneStock = -1;
        private static StockShop.Entry injectedBrickStoneEntry = null;

        private const float INTEGRATION_WARNING_LOG_INTERVAL = 5f;
        private readonly Dictionary<string, float> integrationNextWarningLogTimes = new Dictionary<string, float>();
        private StockShop[] cachedIntegrationStockShops = null;
        private string cachedIntegrationStockShopsSceneName = null;

        public override string ModuleName { get { return "Integration"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
            _deferredBootstrapActions = owner.CreateIntegrationDeferredBootstrapActions();
        }

        public override void OnDestroy()
        {
            StopRuntimeStateMonitor();
            CleanupDeferredIntegrationBootstrap();
            _deferredBootstrapActions = null;
            CleanupRuntimeEvents();
            UnsubscribePurchaseEvents();
            UnsubscribeDragonBreathEffectEvent();
            _owner = null;
        }



































        internal void CleanupRuntimeEvents()
        {
            UnsubscribeTicketStockEvents();
            UnsubscribeJournalStockEvents();
            UnsubscribeBrickStoneStockEvents();
        }

        internal void SubscribeTicketStockEvents()
        {
            if (_ticketStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile += OnSetFile_TicketStock;
            _ticketStockEventsSubscribed = true;
        }

        internal void UnsubscribeTicketStockEvents()
        {
            if (!_ticketStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile -= OnSetFile_TicketStock;
            _ticketStockEventsSubscribed = false;
        }

        internal void SubscribeJournalStockEvents()
        {
            if (_journalStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile += OnSetFile_JournalStock;
            _journalStockEventsSubscribed = true;
        }

        internal void UnsubscribeJournalStockEvents()
        {
            if (!_journalStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile -= OnSetFile_JournalStock;
            _journalStockEventsSubscribed = false;
        }

        internal void SubscribeBrickStoneStockEvents()
        {
            if (_brickStoneStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_BrickStoneStock;
            SavesSystem.OnSetFile += OnSetFile_BrickStoneStock;
            _brickStoneStockEventsSubscribed = true;
        }

        internal void UnsubscribeBrickStoneStockEvents()
        {
            if (!_brickStoneStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_BrickStoneStock;
            SavesSystem.OnSetFile -= OnSetFile_BrickStoneStock;
            _brickStoneStockEventsSubscribed = false;
        }

        internal void LogIntegrationWarningLimited(string key, string message, Exception e = null)
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
            ModBehaviour.DevLog("[BossRush] [WARNING] " + message + (e != null ? ": " + e.Message : string.Empty));
        }

        internal bool ReadMainExistsWithWarning(string context)
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

        internal bool ReadLevelInitedWithWarning(string context)
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

        internal bool ReadSceneLoaderDoneWithWarning(string context)
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

        internal bool ReadCameraExistsWithWarning(string context)
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

        internal string ReadActiveSceneNameWithWarning(string context)
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

        internal void InvalidateIntegrationStockShopCache()
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
            if (sceneName != _owner.IntegrationBaseSceneName)
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

            return merchantId == "Merchant_Normal" && sceneName == _owner.IntegrationBaseSceneName;
        }

        internal bool TryInjectBossRushTicketIntoShop(StockShop shop)
        {
            if (BossRushTicketTypeId <= 0 || !IsBaseHubNormalMerchantShop(shop))
            {
                return false;
            }

            bool alreadyExists = false;
            foreach (StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == BossRushTicketTypeId)
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
            itemEntry.typeID = BossRushTicketTypeId;
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
            ModBehaviour.DevLog("[BossRush] 船票注入成功，库存设置为: " + stockToSet);
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
                ModBehaviour.DevLog("[BossRush] [WARNING] 计算冒险家日志价格系数失败，已回退默认价格: " + e.Message);
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
            ModBehaviour.DevLog("[BossRush] 冒险家日志注入成功，库存设置为: " + stockToSet + ", priceFactor=" + priceFactor);
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
                ModBehaviour.DevLog("[BrickStone] [WARNING] 计算砖石价格系数失败，已回退默认价格: " + e.Message);
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
            ModBehaviour.DevLog("[BrickStone] 砖石注入成功，库存设置为: " + stockToSet + ", priceFactor=" + priceFactor);
            return true;
        }

        internal void InjectBossRushTicketIntoShops_Integration(string targetSceneName = null)
        {
            if (BossRushTicketTypeId <= 0)
            {
                ModBehaviour.DevLog("[BossRush] BossRush 船票 TypeID 未初始化，跳过商店注入");
                return;
            }

            // [性能优化] 只在基地相关场景扫描商店，船票只在基地售货机出售
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                currentScene = ReadActiveSceneNameWithWarning("InjectBossRushTicketIntoShops");
            }
            if (currentScene != _owner.IntegrationBaseSceneName)
            {
                return;
            }

            try
            {
                StockShop[] shops = GetIntegrationStockShops(currentScene, "InjectBossRushTicketIntoShops");
                if (shops == null || shops.Length == 0)
                {
                    ModBehaviour.DevLog("[BossRush] 未找到任何 StockShop，跳过商店扫描");
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

                    ModBehaviour.DevLog("[BossRush] ShopScan: scene=" + sceneName + ", isNpcShop=" + isNpcShop + ", merchantID=" + merchantId + ", goName=" + goName + ", displayName=" + displayName + ", isTargetShop=" + isTargetShop);
                }

                ModBehaviour.DevLog("[BossRush] ShopScan summary: total=" + totalCount + ", npcShops=" + npcShopCount + ", nonNpcShops=" + nonNpcShopCount + ", targetShops=" + targetShopCount + ", added=" + addedCount + ", TypeID=" + BossRushTicketTypeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] InjectBossRushTicketIntoShops 出错: " + e.Message);
            }
        }

        internal void InjectAdventureJournalIntoShops_Integration(string targetSceneName = null)
        {
            // 如果不在基地场景，跳过扫描
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                currentScene = ReadActiveSceneNameWithWarning("InjectAdventureJournalIntoShops");
            }
            if (currentScene != _owner.IntegrationBaseSceneName)
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
                    ModBehaviour.DevLog("[BossRush] 冒险家日志商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] InjectAdventureJournalIntoShops 出错: " + e.Message);
            }
        }

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
                    ModBehaviour.DevLog("[BossRush] 从存档读取冒险家日志库存: " + cachedJournalStock);
                    return cachedJournalStock;
                }

                cachedJournalStock = JOURNAL_DEFAULT_MAX_STOCK;
                return cachedJournalStock;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 读取冒险家日志库存失败: " + e.Message);
                cachedJournalStock = JOURNAL_DEFAULT_MAX_STOCK;
                return cachedJournalStock;
            }
        }

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
                ModBehaviour.DevLog("[BossRush] 保存冒险家日志库存: " + stockToSave);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 保存冒险家日志库存失败: " + e.Message);
            }
        }

        private void OnSetFile_JournalStock()
        {
            cachedJournalStock = -1;
            injectedJournalEntry = null;
            ModBehaviour.DevLog("[BossRush] 检测到读档，重置冒险家日志库存缓存");
        }

        internal void InjectBrickStoneIntoShops(string targetSceneName = null)
        {
            // 如果不在基地场景，跳过扫描
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                currentScene = ReadActiveSceneNameWithWarning("InjectBrickStoneIntoShops");
            }
            if (currentScene != _owner.IntegrationBaseSceneName)
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
                    ModBehaviour.DevLog("[BrickStone] 砖石商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BrickStone] InjectBrickStoneIntoShops 出错: " + e.Message);
            }
        }

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
                    ModBehaviour.DevLog("[BrickStone] 从存档读取砖石库存: " + cachedBrickStoneStock);
                    return cachedBrickStoneStock;
                }

                cachedBrickStoneStock = BrickStoneConfig.DEFAULT_MAX_STOCK;
                return cachedBrickStoneStock;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BrickStone] 读取砖石库存失败: " + e.Message);
                cachedBrickStoneStock = BrickStoneConfig.DEFAULT_MAX_STOCK;
                return cachedBrickStoneStock;
            }
        }

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
                ModBehaviour.DevLog("[BrickStone] 保存砖石库存: " + stockToSave);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BrickStone] 保存砖石库存失败: " + e.Message);
            }
        }

        private void OnSetFile_BrickStoneStock()
        {
            cachedBrickStoneStock = -1;
            injectedBrickStoneEntry = null;
            ModBehaviour.DevLog("[BrickStone] 检测到读档，重置砖石库存缓存");
        }

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
                    ModBehaviour.DevLog("[BossRush] 从存档读取船票库存: " + cachedTicketStock);
                    return cachedTicketStock;
                }

                // 没有存档，返回默认最大库存
                cachedTicketStock = TICKET_DEFAULT_MAX_STOCK;
                ModBehaviour.DevLog("[BossRush] 无存档，使用默认船票库存: " + cachedTicketStock);
                return cachedTicketStock;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 读取船票库存失败: " + e.Message);
                cachedTicketStock = TICKET_DEFAULT_MAX_STOCK;
                return cachedTicketStock;
            }
        }

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
                ModBehaviour.DevLog("[BossRush] 保存船票库存: " + stockToSave);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 保存船票库存失败: " + e.Message);
            }
        }

        private void OnSetFile_TicketStock()
        {
            cachedTicketStock = -1;  // 重置缓存，下次注入时会从存档读取
            injectedTicketEntry = null;  // 清除旧引用
            ModBehaviour.DevLog("[BossRush] 检测到读档，重置船票库存缓存");
        }
    }
}

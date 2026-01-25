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
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
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
        private const int ADVENTURE_JOURNAL_TYPE_ID = 500007;  // 冒险家日志物品 ID
        private const string JOURNAL_STOCK_SAVE_KEY = "BossRush_JournalStock";
        private const int JOURNAL_DEFAULT_MAX_STOCK = 1;  // 最大库存为1
        private static int cachedJournalStock = -1;  // -1 表示未初始化，需要从存档读取
        private static StockShop.Entry injectedJournalEntry = null;  // 缓存注入的冒险家日志条目引用
        
        // [性能优化] 商店注入完成标记，避免每次场景加载都重复扫描
        private static bool _shopInjectionCompleted = false;
        
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
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "bossrush_ticket");

                if (!File.Exists(bundlePath))
                {
                    DevLog("[BossRush] 未找到 bossrush_ticket AssetBundle: " + bundlePath);
                    return;
                }

                // 通过反射加载 AssetBundle，避免编译期依赖 UnityEngine.AssetBundleModule
                Type assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
                if (assetBundleType == null)
                {
                    assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine");
                }
                if (assetBundleType == null)
                {
                    DevLog("[BossRush] 无法找到 UnityEngine.AssetBundle 类型，跳过动态物品加载");
                    return;
                }

                MethodInfo loadFromFile = assetBundleType.GetMethod("LoadFromFile", new Type[] { typeof(string) });
                if (loadFromFile == null)
                {
                    DevLog("[BossRush] 未找到 AssetBundle.LoadFromFile 方法，跳过动态物品加载");
                    return;
                }

                object bundle = loadFromFile.Invoke(null, new object[] { bundlePath });
                if (bundle == null)
                {
                    DevLog("[BossRush] 反射调用 LoadFromFile 失败: " + bundlePath);
                    return;
                }

                MethodInfo loadAllAssets = assetBundleType.GetMethod("LoadAllAssets", new Type[] { typeof(Type) });
                if (loadAllAssets == null)
                {
                    DevLog("[BossRush] 未找到 AssetBundle.LoadAllAssets(Type) 方法，跳过动态物品加载");
                    return;
                }

                // 加载所有资源，然后从 GameObject 中查找挂载的 Item 组件
                UnityEngine.Object[] assets = loadAllAssets.Invoke(bundle, new object[] { typeof(UnityEngine.Object) }) as UnityEngine.Object[];
                if (assets == null || assets.Length == 0)
                {
                    DevLog("[BossRush] bossrush_ticket AssetBundle 中未找到任何资源");
                    return;
                }

                int itemCount = 0;
                foreach (UnityEngine.Object obj in assets)
                {
                    GameObject go = obj as GameObject;
                    if (go == null)
                    {
                        continue;
                    }

                    Item itemPrefab = go.GetComponent<Item>();
                    if (itemPrefab == null)
                    {
                        continue;
                    }

                    // 为船票添加 Tag（Key = 钥匙，SpecialKey = 钥匙（无法录入））
                    AddTagsToItem(itemPrefab, new string[] { "Key", "SpecialKey" });
                    
                    ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                    itemCount++;

                    if (bossRushTicketTypeId <= 0 && itemPrefab.TypeID > 0)
                    {
                        bossRushTicketTypeId = itemPrefab.TypeID;
                    }
                }

                DevLog("[BossRush] 动态物品初始化完成：从 bossrush_ticket 加载 " + itemCount + " 个 Item，BossRushTicketTypeId=" + bossRushTicketTypeId);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] InitializeDynamicItems 出错: " + e.Message);
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
            
            // [性能优化] 只在基地场景扫描商店，船票只在基地售货机出售
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch {}
            }
            if (currentScene != "Base_SceneV2")
            {
                return;
            }
            
            try
            {
                StockShop[] shops = UnityEngine.Object.FindObjectsOfType<StockShop>();
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
                    catch { }

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
                    catch { }

                    try
                    {
                        goName = shop.gameObject != null ? shop.gameObject.name : "<no-go>";
                    }
                    catch { }

                    try
                    {
                        merchantId = shop.MerchantID;
                    }
                    catch { }

                    try
                    {
                        displayName = shop.DisplayName;
                    }
                    catch { }

                    bool isTargetShop = (!isNpcShop && merchantId == "Merchant_Normal" && sceneName == "Base_SceneV2");

                    if (isTargetShop)
                    {
                        targetShopCount++;

                        if (shop.entries != null)
                        {
                            bool alreadyExists = false;
                            foreach (StockShop.Entry entry in shop.entries)
                            {
                                if (entry != null && entry.ItemTypeID == bossRushTicketTypeId)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }

                            if (!alreadyExists)
                            {
                                StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                                itemEntry.typeID = bossRushTicketTypeId;
                                itemEntry.maxStock = TICKET_DEFAULT_MAX_STOCK;
                                itemEntry.forceUnlock = true;
                                itemEntry.priceFactor = 1f;
                                itemEntry.possibility = 1f;
                                itemEntry.lockInDemo = false;

                                StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                                
                                // 从存档读取库存，如果没有存档则使用默认最大库存
                                int stockToSet = LoadTicketStockFromSave();
                                wrapped.CurrentStock = stockToSet;
                                wrapped.Show = true;
                                
                                // 缓存引用，用于后续存档
                                injectedTicketEntry = wrapped;
                                
                                shop.entries.Add(wrapped);
                                addedCount++;
                                
                                DevLog("[BossRush] 船票注入成功，库存设置为: " + stockToSet);
                            }
                            else
                            {
                                // 已存在的条目，更新缓存引用
                                foreach (StockShop.Entry entry in shop.entries)
                                {
                                    if (entry != null && entry.ItemTypeID == bossRushTicketTypeId)
                                    {
                                        injectedTicketEntry = entry;
                                        break;
                                    }
                                }
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
                try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch {}
            }
            if (currentScene != "Base_SceneV2")
            {
                return;
            }
            
            // [Bug修复] 每次场景加载都重新扫描商店，因为场景切换后商店对象会被重建
            // StockShop.Entry 不是 Unity 对象，无法通过 null 检查判断是否有效
            
            try
            {
                StockShop[] shops = UnityEngine.Object.FindObjectsOfType<StockShop>();
                if (shops == null || shops.Length == 0)
                {
                    return;
                }

                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null) continue;

                    bool isNpcShop = false;
                    try
                    {
                        if (shop.GetComponentInParent<CharacterMainControl>() != null)
                        {
                            isNpcShop = true;
                        }
                    }
                    catch { }

                    string sceneName = "";
                    string merchantId = "";
                    try { sceneName = shop.gameObject != null ? shop.gameObject.scene.name : ""; } catch { }
                    try { merchantId = shop.MerchantID; } catch { }

                    // 只注入到基地的普通售货机
                    bool isTargetShop = (!isNpcShop && merchantId == "Merchant_Normal" && sceneName == "Base_SceneV2");

                    if (isTargetShop && shop.entries != null)
                    {
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

                        if (!alreadyExists)
                        {
                            // 计算 priceFactor 使价格为1块钱
                            // 价格 = rawValue * priceFactor，所以 priceFactor = 1 / rawValue
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
                            catch { }
                            
                            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                            itemEntry.typeID = ADVENTURE_JOURNAL_TYPE_ID;
                            itemEntry.maxStock = JOURNAL_DEFAULT_MAX_STOCK;
                            itemEntry.forceUnlock = true;
                            itemEntry.priceFactor = priceFactor;  // 设置价格因子使价格为1块钱
                            itemEntry.possibility = 1f;
                            itemEntry.lockInDemo = false;

                            StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                            
                            // 从存档读取库存
                            int stockToSet = LoadJournalStockFromSave();
                            wrapped.CurrentStock = stockToSet;
                            wrapped.Show = true;
                            
                            injectedJournalEntry = wrapped;
                            // 插入到列表开头，排在船票前面
                            shop.entries.Insert(0, wrapped);
                            addedCount++;
                            
                            DevLog("[BossRush] 冒险家日志注入成功，库存设置为: " + stockToSet + ", priceFactor=" + priceFactor);
                        }
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
            _shopInjectionCompleted = false;  // [性能优化] 重置商店注入标记，读档后需要重新注入
            DevLog("[BossRush] 检测到读档，重置船票库存缓存");
        }

        /// <summary>
        /// 注入基础本地化（委托给 LocalizationInjector）
        /// </summary>
        private void InjectLocalization_Integration()
        {
            // 已迁移到 LocalizationInjector.InjectUILocalization()
            // 此方法保留以兼容调用，实际注入在 InjectLocalization_Extra_Integration 中统一执行
        }

        /// <summary>
        /// 注入扩展本地化（委托给 LocalizationInjector）
        /// </summary>
        private void InjectLocalization_Extra_Integration()
        {
            // 统一调用 LocalizationInjector 注入所有本地化
            LocalizationInjector.InjectUILocalization();
            LocalizationInjector.InjectMapNameLocalizations();
            LocalizationInjector.InjectCourierNPCLocalization();  // 快递员NPC本地化
            EquipmentLocalization.InjectAllEquipmentLocalizations();
            InjectReverseScaleLocalization();  // 逆鳞图腾本地化
            DevLog("[BossRush] 扩展本地化注入完成");
        }

        void Start_Integration()
        {
            LoadConfigFromFile();
            Type modConfigType = FindModConfigType("ModConfig.ModBehaviour");
            if (modConfigType != null)
            {
                SetupModConfig();
                LoadConfigFromModConfig();
                SaveConfigToFile();
            }

            // 尝试注入本地化字典
            InjectLocalization();

            InitializeDynamicItems();
            InjectBossRushTicketLocalization();
            InjectBossRushTicketIntoShops();
            
            // 初始化生日蛋糕物品
            InitializeBirthdayCakeItem();
            InjectBirthdayCakeLocalization();
            // 不再注入商店，生日蛋糕仅通过12月自动赠送获得
            
            // 初始化 Wiki Book 物品（冒险家日志）
            InitializeWikiBookItem();
            InjectWikiBookLocalization();
            InjectAdventureJournalIntoShops_Integration();  // 将冒险家日志注入到售货机
            
            // 初始化自定义装备（自动扫描加载 Assets/Equipment/ 目录）
            int equipCount = EquipmentFactory.LoadAllEquipment();
            DevLog("[BossRush] 自动加载装备完成，共 " + equipCount + " 个");
            
            // 初始化飞行图腾系统
            InitializeFlightTotemSystem();
            
            // 注意：龙息武器Buff处理器现在是按需订阅
            // 只在玩家装备龙息武器时才订阅Health.OnHurt事件，卸下时取消订阅
            // 这样可以避免在所有伤害事件中进行武器ID检查，提升性能

            DevLog("[BossRush] ========================================");
            DevLog("[BossRush] Boss Rush Mod v1.0 已加载");
            DevLog("[BossRush] ========================================");
            DevLog("[BossRush] 使用方法:");
            DevLog("[BossRush]   1. 购买'Boss Rush船票'（在商店中）");
            // 延迟初始化，等待游戏系统完全加载
            
            // 注册场景加载事件，在进入新场景时注入交互
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // 注册商店购买事件，用于检测进货行为
            StockShop.OnItemPurchased += OnItemPurchased_Integration;
            
            // 注册存档系统事件，用于持久化船票库存
            SavesSystem.OnCollectSaveData += OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile += OnSetFile_TicketStock;
            
            // 注册存档系统事件，用于持久化冒险家日志库存
            SavesSystem.OnCollectSaveData += OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile += OnSetFile_JournalStock;
            
            // 注册龙套装装备槽变化事件
            RegisterDragonSetEvents();

            // 初始化逆鳞图腾系统（新架构）
            InitializeReverseScaleSystem();
            
            // 如果当前已经在场景中，立即执行一次
            if (SceneManager.GetActiveScene().name != "MainMenu" && SceneManager.GetActiveScene().name != "LoadingScreen_Black")
            {
                StartCoroutine(FindInteractionTargets(5)); // 立即扫描5次
            }
        }
        
        void OnDestroy_Integration()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StockShop.OnItemPurchased -= OnItemPurchased_Integration;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_TicketStock;
            SavesSystem.OnSetFile -= OnSetFile_TicketStock;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_JournalStock;
            SavesSystem.OnSetFile -= OnSetFile_JournalStock;
            Health.OnDead -= OnPlayerDeathInBossRush;
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            
            // 取消注册龙套装事件
            UnregisterDragonSetEvents();

            // 清理逆鳞图腾系统（新架构）
            CleanupReverseScaleSystem();
            
            // 取消订阅龙息武器火焰特效事件
            UnsubscribeDragonBreathEffectEvent();
            
            // 清理龙息武器Buff处理器
            DragonBreathBuffHandler.Cleanup();
            
            // 清理飞行图腾系统
            CleanupFlightTotemSystem();
            
            // [性能优化] 重置商店注入标记
            _shopInjectionCompleted = false;
            
            try
            {
                Type modBehaviourType = FindModConfigType("ModConfig.ModBehaviour");
                if (modBehaviourType != null)
                {
                    MethodInfo removeDelegateMethod = modBehaviourType.GetMethod("RemoveOnOptionsChangedDelegate", BindingFlags.Public | BindingFlags.Static);
                    if (removeDelegateMethod != null)
                    {
                        Action<string> handler = new Action<string>(OnModConfigOptionsChanged);
                        removeDelegateMethod.Invoke(null, new object[] { handler });
                        DevLog("[BossRush] 已移除配置变更事件监听");
                    }
                }
            }
            catch
            {
            }
            
            if (arenaStartPoint != null) UnityEngine.Object.Destroy(arenaStartPoint);
            
            // 清理 Boss 池 UI
            DestroyBossPoolUI();
            
            // [性能优化] 重置敌人预设初始化标记，下次加载Mod时重新扫描
            _enemyPresetsInitialized = false;
            
            DevLog("[BossRush] Boss Rush Mod已卸载");
        }

        /// <summary>
        /// 商店购买事件处理：检测玩家是否大量购买 ID 105 物品（进货行为）
        /// 仅在 BossRush 加油站（ammoShop）中生效
        /// </summary>
        private void OnItemPurchased_Integration(StockShop shop, Item item)
        {
            try
            {
                if (shop == null || item == null) return;
                
                // 仅在 BossRush 加油站中检测
                if (ammoShop == null || shop != ammoShop) return;
                
                // 检测是否购买了 ID 105 的物品
                if (item.TypeID == 105)
                {
                    item105PurchaseCount++;
                    
                    // 达到 10 个时显示横幅提示
                    if (item105PurchaseCount == 10)
                    {
                        ShowBigBanner(L10n.T("喂喂，你这家伙来这进货了是吗(*´･д･)?", "Hey, are you here to stock up? (*´･д･)?"));
                    }
                }
            }
            catch {}
        }

        private void OnSceneLoaded_Integration(Scene scene, LoadSceneMode mode)
        {
            DevLog("[BossRush] 场景加载: " + scene.name);
            
            // [内存优化] 场景切换时清理龙裔遗族相关的静态缓存，防止持有已销毁对象引用
            ClearDragonDescendantStaticCache();
            
            // [内存优化] 场景切换时清理龙王相关的静态缓存
            ClearDragonKingStaticCache();
            
            // 在任何场景加载后都尝试订阅龙息武器事件
            // 使用延迟调用确保玩家角色已初始化
            StartCoroutine(DelayedSubscribeDragonBreathEvents());
            
            // 设置飞行图腾系统（注入商店、Hook Dash等）
            SetupFlightTotemForScene(scene);

            try
            {
                InjectBossRushTicketIntoShops_Integration(scene.name);
                InjectAdventureJournalIntoShops_Integration(scene.name);
                // 不再注入商店，生日蛋糕仅通过12月自动赠送获得
                
                // 在基地场景检查并赠送12月份生日蛋糕
                if (scene.name == "Base_SceneV2")
                {
                    StartCoroutine(DelayedBirthdayCakeGift());
                }

                // 使用配置系统检查是否是有效的 BossRush 竞技场场景
                BossRushMapConfig loadedMapConfig = GetMapConfigBySceneName(scene.name);
                if (loadedMapConfig != null && !loadedMapConfig.customSpawnPos.HasValue)
                {
                    // 只有在通过 BossRush 启动且是默认传送位置的地图时才执行竞技场逻辑
                    if (bossRushArenaPlanned)
                    {
                        InitializeEnemyPresets();
                        InitializeItemValueCacheAsync(); // 异步初始化物品价值缓存
                        InitializeBossPoolFilter();
                        bossRushArenaActive = true;
                        bossRushArenaPlanned = false;
                        
                        // [Bug修复] 确保订阅龙息Buff处理器，使龙裔遗族Boss的龙息能触发龙焰灼烧
                        DragonBreathBuffHandler.Subscribe();
                        
                        // 设置当前地图的刷新点
                        SetCurrentMapSpawnPoints(scene.name);
                        
                        // [性能优化] 立即设置竞技场中心，确保后续清理有范围限制
                        SetArenaCenterFromMapConfig(scene.name);
                        spawnersDisabled = false; // 重置标志确保能重新禁用
                        
                        // [修复] 立即禁用 spawner，防止敌人生成
                        // 场景加载时 spawner 已经存在，必须立即禁用
                        DisableAllSpawners();
                        DevLog("[BossRush] 场景加载后立即禁用 spawner");
                        
                        // [修复] 立即启动持续清理协程，清理已生成的敌人
                        StartCoroutine(ContinuousClearEnemiesUntilWaveStart());
                        DevLog("[BossRush] 场景加载后立即启动持续清理协程");

                        // 设置BossRush竞技场
                        demoChallengeStartPosition = Vector3.zero;
                        // 延迟到地图完全加载后再执行传送、禁用spawner和创建交互点
                        StartCoroutine(WaitForLevelInitializedThenSetup(scene));
                    }
                }
                // 处理 BossRush 自定义传送位置（如零号区等其他地图）
                // 只在目标子场景加载后才执行传送，避免在 LoadingScreen 场景就触发
                else if (bossRushArenaPlanned)
                {
                    // 检查当前场景是否是待处理地图的目标子场景
                    string targetSubScene = BossRushMapSelectionHelper.GetPendingTargetSubSceneName();
                    string targetMainScene = BossRushMapSelectionHelper.GetPendingMainSceneName();
                    Vector3? customPos = BossRushMapSelectionHelper.GetPendingCustomPosition();
                    
                    // 只有当前场景匹配目标子场景时才执行传送
                    if (targetSubScene != null && scene.name == targetSubScene && customPos.HasValue)
                    {
                        DevLog("[BossRush] 检测到目标子场景加载: " + scene.name + ", 执行自定义传送到: " + customPos.Value);
                        bossRushArenaPlanned = false;
                        
                        // 延迟传送玩家到自定义位置
                        StartCoroutine(TeleportPlayerToCustomPosition(customPos.Value));
                        
                        // 清除待处理的地图条目
                        BossRushMapSelectionHelper.ClearPendingMapEntry();
                    }
                    // 如果是加载屏幕、菜单场景或主场景（_Main），保持标记等待子场景加载
                    else if (scene.name.Contains("Loading") || scene.name.Contains("Menu") || 
                             (targetMainScene != null && scene.name == targetMainScene))
                    {
                        DevLog("[BossRush] 检测到中间场景: " + scene.name + ", 保持传送标记等待目标子场景");
                        // 不重置 bossRushArenaPlanned，等待目标场景加载
                    }
                    // 特殊处理：风暴区地下场景 - 如果加载了错误的子场景（地上），强制传送到地下
                    else if (targetSubScene == "Level_StormZone_B0" && scene.name == "Level_StormZone_1" && customPos.HasValue)
                    {
                        DevLog("[BossRush] 检测到加载了错误的子场景: " + scene.name + " (期望: " + targetSubScene + "), 强制传送到地下场景");
                        // 使用 MultiSceneCore.LoadAndTeleport 强制传送到地下场景
                        StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                    }
                    else
                    {
                        // 其他场景，重置标记
                        DevLog("[BossRush] 非目标场景: " + scene.name + ", 重置传送标记");
                        bossRushArenaPlanned = false;
                        BossRushMapSelectionHelper.ClearPendingMapEntry();
                    }
                }
                else
                {
                    // Bug #1 修复：从竞技场撤离到其他场景时重置状态
                    if (IsActive || bossRushArenaActive)
                    {
                        DevLog("[BossRush] 检测到场景切换（离开竞技场），重置 BossRush 状态");
                        // 优先清空 BossRush 内部的下一波倒计时/提示状态和 NotificationText 队列，防止在基地继续播报 "下一波将在 X 秒后开始"
                        waitingForNextWave = false;
                        waveCountdown = 0f;
                        lastWaveCountdownSeconds = -1;
                        statusMessage = string.Empty;
                        messageTimer = 0f;
                        
                        // [多次进入优化] 清理波次相关状态
                        currentWaveBosses.Clear();
                        bossesInCurrentWaveTotal = 0;
                        bossesInCurrentWaveRemaining = 0;
                        currentEnemyIndex = 0;
                        defeatedEnemies = 0;
                        totalEnemies = 0;
                        
                        // [多次进入优化] 清理大兴兴追踪集合，防止持有已销毁对象引用
                        bossRushOwnedDaXingXing.Clear();

                        // 清理 NotificationText.pendingTexts 队列
                        try
                        {
                            System.Type notifType = typeof(NotificationText);
                            System.Reflection.FieldInfo pendingField = notifType.GetField("pendingTexts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                            if (pendingField != null)
                            {
                                System.Collections.Generic.Queue<string> q = pendingField.GetValue(null) as System.Collections.Generic.Queue<string>;
                                if (q != null)
                                {
                                    q.Clear();
                                }
                            }
                        }
                        catch { }

                        // 最后再重置 BossRush 状态标志，避免因为 IsActive 过早被置为 false 导致某些清理逻辑被跳过
                        SetBossRushRuntimeActive(false);
                        bossRushArenaActive = false;
                        bossRushArenaPlanned = false;
                        currentBoss = null;
                        
                        // 销毁快递员 NPC
                        DestroyCourierNPC();
                        
                        // 重置 spawner 禁用标志，以便下次进入竞技场时能重新禁用
                        spawnersDisabled = false;
                        try
                        {
                            if (ammoShop != null)
                            {
                                try
                                {
                                    if (ammoShop.gameObject != null)
                                    {
                                        UnityEngine.Object.Destroy(ammoShop.gameObject);
                                    }
                                }
                                catch {}
                                ammoShop = null;
                            }
                        }
                        catch {}
                        
                        // 取消敌人死亡监听
                        Health.OnDead -= OnEnemyDiedWithDamageInfo;

                        // 如果是 Mode D 模式，结束 Mode D
                        if (modeDActive)
                        {
                            EndModeD();
                        }
                    }
                    
                    // 普通模式下：检查是否需要在当前场景生成快递员NPC
                    // 使用 NPCSpawnConfig 配置系统判断
                    if (NPCSpawnConfig.HasCourierNormalModeConfig(scene.name))
                    {
                        DevLog("[CourierNPC] 普通模式检测到配置场景: " + scene.name + ", 延迟生成快递员");
                        StartCoroutine(DelayedSpawnCourierInNormalMode(scene.name));
                    }
                    
                    // 其他场景：注入传送到竞技场的交互选项
                    StartCoroutine(FindInteractionTargets(10));
                }
            }
            catch { }
        }

        /// <summary>
        /// 普通模式下延迟生成快递员NPC
        /// 等待场景完全初始化后再生成，确保地面碰撞体等已加载
        /// </summary>
        private System.Collections.IEnumerator DelayedSpawnCourierInNormalMode(string sceneName)
        {
            // 等待场景完全加载
            const float maxWait = 10f;
            const float interval = 0.2f;
            float elapsed = 0f;
            
            while (elapsed < maxWait)
            {
                bool mainExists = false;
                bool levelInited = false;
                
                try { mainExists = CharacterMainControl.Main != null; } catch { }
                try { levelInited = LevelManager.LevelInited; } catch { }
                
                if (mainExists && levelInited)
                {
                    break;
                }
                
                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }
            
            // 额外等待确保场景物理碰撞体已加载
            yield return new WaitForSeconds(0.5f);
            
            // 再次检查是否仍在目标场景（玩家可能已切换场景）
            string currentScene = "";
            try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            
            if (currentScene != sceneName)
            {
                DevLog("[CourierNPC] 场景已切换，取消普通模式快递员生成");
                yield break;
            }
            
            // 检查是否已进入 BossRush 模式（玩家可能在等待期间启动了 BossRush）
            if (IsActive || IsModeDActive || IsBossRushArenaActive)
            {
                DevLog("[CourierNPC] 已进入 BossRush 模式，跳过普通模式快递员生成");
                yield break;
            }
            
            // 检查快递员是否已存在
            if (courierNPCInstance != null)
            {
                DevLog("[CourierNPC] 快递员已存在，跳过生成");
                yield break;
            }
            
            // 生成快递员
            DevLog("[CourierNPC] 普通模式场景初始化完成，开始生成快递员");
            SpawnCourierNPC();
        }

        /// <summary>
        /// 将玩家传送到自定义位置（用于 BossRush 地图选择中的非默认地图）
        /// [修复] 使用 RaycastAll 找到最接近配置 Y 坐标的地面点，避免在室内场景传送到房顶
        /// </summary>
        private System.Collections.IEnumerator TeleportPlayerToCustomPosition(Vector3 targetPosition)
        {
            DevLog("[BossRush] TeleportPlayerToCustomPosition: 开始等待场景初始化，目标位置: " + targetPosition);
            
            // 等待场景完全加载
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            
            while (elapsed < maxWait)
            {
                bool mainExists = false;
                bool levelInited = false;
                
                try { mainExists = CharacterMainControl.Main != null; } catch { }
                try { levelInited = LevelManager.LevelInited; } catch { }
                
                if (mainExists && levelInited)
                {
                    break;
                }
                
                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }
            
            // 额外等待一小段时间，确保游戏自身的出生点逻辑已执行完毕
            yield return new WaitForSeconds(0.5f);
            
            // 传送玩家到目标位置
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    // [修复] 使用 RaycastAll 找到最接近配置 Y 坐标的地面点
                    Vector3 finalPosition = targetPosition;
                    Vector3 rayStart = targetPosition + Vector3.up * 50f;
                    RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 100f);
                    
                    if (hits != null && hits.Length > 0)
                    {
                        float configY = targetPosition.y;
                        float bestY = targetPosition.y;
                        float lowestY = float.MaxValue;
                        
                        foreach (var h in hits)
                        {
                            // 优先选择接近配置 Y 坐标的点（允许 1 米误差）
                            if (Mathf.Abs(h.point.y - configY) < 1f)
                            {
                                bestY = h.point.y + 0.1f;
                                break;
                            }
                            // 否则选择最低的点
                            if (h.point.y < lowestY)
                            {
                                lowestY = h.point.y;
                                bestY = h.point.y + 0.1f;
                            }
                        }
                        
                        finalPosition = new Vector3(targetPosition.x, bestY, targetPosition.z);
                        DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 RaycastAll 修正落点: " + finalPosition + " (配置Y=" + configY + ")");
                    }
                    else
                    {
                        // 如果没有碰撞，使用单次射线检测
                        RaycastHit hit;
                        if (Physics.Raycast(rayStart, Vector3.down, out hit, 100f))
                        {
                            finalPosition = hit.point + new Vector3(0f, 0.1f, 0f);
                            DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用单次 Raycast 修正落点: " + finalPosition);
                        }
                    }
                    
                    // 保存相机偏移
                    GameCamera camera = GameCamera.Instance;
                    Vector3 cameraOffset = Vector3.zero;
                    if (camera != null)
                    {
                        cameraOffset = camera.transform.position - main.transform.position;
                    }
                    
                    // 传送玩家
                    try
                    {
                        main.SetPosition(finalPosition);
                        DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 SetPosition 传送玩家到 " + finalPosition);
                    }
                    catch (System.Exception e)
                    {
                        DevLog("[BossRush] SetPosition 失败: " + e.Message + "，改用 transform.position");
                        main.transform.position = finalPosition;
                    }
                    
                    // 恢复相机位置
                    if (camera != null)
                    {
                        camera.transform.position = main.transform.position + cameraOffset;
                    }
                    
                    DevLog("[BossRush] TeleportPlayerToCustomPosition: 传送完成");
                    
                    // 在有效的 BossRush 竞技场场景执行初始化
                    string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    BossRushMapConfig mapConfig = GetMapConfigBySceneName(currentScene);
                    if (mapConfig != null && mapConfig.customSpawnPos.HasValue)
                    {
                        // 启动该地图的 BossRush 初始化协程
                        StartCoroutine(SetupBossRushInGroundZero(finalPosition));
                    }
                }
                else
                {
                    DevLog("[BossRush] TeleportPlayerToCustomPosition: CharacterMainControl.Main 为 null");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] TeleportPlayerToCustomPosition 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 强制传送到指定的子场景（用于风暴区地下等需要特定子场景的情况）
        /// 当游戏随机选择了错误的子场景时，查找并触发场景中的传送器
        /// </summary>
        private System.Collections.IEnumerator ForceTeleportToSubScene(string targetSubSceneID, Vector3 targetPosition)
        {
            DevLog("[BossRush] ForceTeleportToSubScene: 开始强制传送到子场景 " + targetSubSceneID);
            
            // 等待场景完全加载（使用较短的间隔提高响应速度）
            const float maxWait = 10f;
            const float interval = 0.1f;
            float elapsed = 0f;
            
            while (elapsed < maxWait)
            {
                bool mainExists = false;
                bool levelInited = false;
                
                try { mainExists = CharacterMainControl.Main != null; } catch { }
                try { levelInited = LevelManager.LevelInited; } catch { }
                
                if (mainExists && levelInited) break;
                
                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }
            
            // 额外等待确保场景稳定
            yield return new WaitForSeconds(1.0f);
            
            // 方案1：查找场景中通往目标子场景的传送器并触发
            try
            {
                MultiSceneTeleporter[] teleporters = UnityEngine.Object.FindObjectsOfType<MultiSceneTeleporter>(true);
                MultiSceneTeleporter targetTeleporter = null;
                
                // [性能优化] 只在找到传送器时输出日志
                foreach (MultiSceneTeleporter t in teleporters)
                {
                    if (t == null) continue;
                    
                    try
                    {
                        MultiSceneLocation target = t.Target;
                        string targetSceneID = target.SceneID;
                        
                        // 精确匹配
                        if (targetSceneID == targetSubSceneID)
                        {
                            targetTeleporter = t;
                            break;
                        }
                        
                        // 风暴区特殊处理：模糊匹配
                        if (targetSubSceneID == "Level_StormZone_B0")
                        {
                            string interactName = t.InteractName ?? "";
                            if (interactName.Contains("下去") || interactName.Contains("地下") ||
                                t.name.Contains("Down") || t.name.Contains("B0") ||
                                (targetSceneID != null && targetSceneID.Contains("B0")))
                            {
                                targetTeleporter = t;
                                break;
                            }
                        }
                    }
                    catch { }
                }
                
                if (targetTeleporter != null)
                {
                    DevLog("[BossRush] ForceTeleportToSubScene: 触发传送器 " + targetTeleporter.name);
                    targetTeleporter.DoTeleport();
                    yield break;
                }
            }
            catch { }
            
            // 方案2：使用 MultiSceneCore.LoadAndTeleport（备用方案）
            try
            {
                Duckov.Scenes.MultiSceneCore multiSceneCore = Duckov.Scenes.MultiSceneCore.Instance;
                if (multiSceneCore != null)
                {
                    DevLog("[BossRush] ForceTeleportToSubScene: 使用 LoadAndTeleport 备用方案");
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(multiSceneCore.LoadAndTeleport(targetSubSceneID, targetPosition, true));
                }
                else
                {
                    // 回退：直接传送玩家到目标位置
                    bossRushArenaPlanned = false;
                    StartCoroutine(TeleportPlayerToCustomPosition(targetPosition));
                    BossRushMapSelectionHelper.ClearPendingMapEntry();
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] ForceTeleportToSubScene 失败: " + e.Message);
                bossRushArenaPlanned = false;
                BossRushMapSelectionHelper.ClearPendingMapEntry();
            }
        }
        
        /// <summary>
        /// 在零号区设置 BossRush 模式（类似 SetupBossRushInDemoChallenge）
        /// </summary>
        private System.Collections.IEnumerator SetupBossRushInGroundZero(Vector3 playerPosition)
        {
            DevLog("[BossRush] SetupBossRushInGroundZero: 开始初始化零号区 BossRush 模式");
            
            // 0. 重置 spawner 禁用标志（确保能重新禁用新场景的 spawner）
            spawnersDisabled = false;
            
            // [性能优化] 先根据地图配置设置竞技场中心，确保后续清理和禁用操作有范围限制
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            SetArenaCenterFromMapConfig(currentSceneName);
            
            // 1. [重要] 先生成地图阻挡物和撤离点（必须在销毁 spawner 之前执行）
            // 因为撤离点模板 ExitNoSmoke Variant 位于 EnemySpawner_TestBossZone 下
            SpawnBossRushMapObjects();
            
            // 2. 禁用场景中的 spawner，阻止敌怪生成（会销毁 EnemySpawner_TestBossZone）（现在有 50m 范围限制）
            DisableAllSpawners();
            DevLog("[BossRush] SetupBossRushInGroundZero: 已禁用竞技场范围内的敌怪生成器");
            
            // 3. 启动持续清理敌人协程（直到波次开始）
            StartCoroutine(ContinuousClearEnemiesUntilWaveStart());
            
            // 4. 等待场景稳定
            yield return new UnityEngine.WaitForSeconds(0.5f);
            
            // 5. 清理场景中现有的敌人（现在有 50m 范围限制）
            ClearEnemiesForBossRush();
            DevLog("[BossRush] SetupBossRushInGroundZero: 已清理竞技场范围内的敌人");
            
            // 6. 创建 BossRush 交互点（优先使用配置的位置，否则使用玩家位置偏移）
            BossRushMapConfig currentMapConfig = GetMapConfigBySceneName(currentSceneName);
            Vector3 signPosition;
            if (currentMapConfig != null && currentMapConfig.defaultSignPos.HasValue)
            {
                signPosition = currentMapConfig.defaultSignPos.Value;
                DevLog("[BossRush] SetupBossRushInGroundZero: 使用配置的交互点位置: " + signPosition);
            }
            else
            {
                signPosition = playerPosition + new Vector3(-2f, 0f, 1f);
                DevLog("[BossRush] SetupBossRushInGroundZero: 使用玩家位置偏移: " + signPosition);
            }
            TryCreateArenaDifficultyEntryPoint(signPosition);
            DevLog("[BossRush] SetupBossRushInGroundZero: 已创建 BossRush 交互点，位置=" + signPosition);
            
            // 7. 设置当前地图的刷新点（使用当前场景名）
            SetCurrentMapSpawnPoints(currentSceneName);
            
            // 8. 标记 BossRush 竞技场已激活
            bossRushArenaActive = true;
            InitializeEnemyPresets();
            InitializeItemValueCacheAsync(); // 异步初始化物品价值缓存
            InitializeBossPoolFilter();
            DevLog("[BossRush] SetupBossRushInGroundZero: 零号区 BossRush 模式初始化完成");
            
            // 9. 检测 Mode D 条件：玩家裸体入场
            bool shouldStartModeD = false;
            try
            {
                shouldStartModeD = IsPlayerNaked();
                if (shouldStartModeD)
                {
                    DevLog("[BossRush] SetupBossRushInGroundZero: 检测到玩家裸体入场，将启动 Mode D");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] SetupBossRushInGroundZero: 检测 Mode D 条件失败: " + e.Message);
            }
            
            // 10. 如果满足 Mode D 条件，延迟启动 Mode D
            if (shouldStartModeD)
            {
                yield return new UnityEngine.WaitForSeconds(0.5f);
                TryStartModeD();
            }
            
            // 11. 生成快递员 NPC
            SpawnCourierNPC();
        }
        
        /// <summary>
        /// 根据场景名称设置当前地图的刷新点（使用 BossRushMapConfig 配置系统）
        /// </summary>
        private void SetCurrentMapSpawnPoints(string sceneName)
        {
            // 使用配置系统获取刷新点（使用 mapConfig 避免与实例字段 config 混淆）
            BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
            if (mapConfig != null && mapConfig.spawnPoints != null)
            {
                currentMapSpawnPoints = mapConfig.spawnPoints;
                DevLog("[BossRush] SetCurrentMapSpawnPoints: 使用 " + mapConfig.displayName + " 刷新点，共 " + mapConfig.spawnPoints.Length + " 个");
            }
            else
            {
                // 默认使用 DEMO 竞技场刷新点
                currentMapSpawnPoints = DemoChallengeSpawnPoints;
                DevLog("[BossRush] SetCurrentMapSpawnPoints: 未知场景 " + sceneName + "，使用默认刷新点");
            }
        }
        
        /// <summary>
        /// BossRush 地图物品复制配置
        /// </summary>
        private class MapObjectCloneConfig
        {
            public string templateName;      // 模板对象名称
            public string parentNamePrefix;  // 父对象名称前缀（用于查找）
            public Vector3 targetPosition;   // 目标位置
            public string cloneName;         // 克隆后的名称
            public float? rotationY;         // Y轴旋转角度（可选，null表示使用模板旋转）
            
            public MapObjectCloneConfig(string template, string parentPrefix, Vector3 pos, string name, float? rotation = null)
            {
                templateName = template;
                parentNamePrefix = parentPrefix;
                targetPosition = pos;
                cloneName = name;
                rotationY = rotation;
            }
        }
        
        /// <summary>
        /// 获取指定地图的物品复制配置列表
        /// </summary>
        private List<MapObjectCloneConfig> GetMapCloneConfigs(string sceneName)
        {
            List<MapObjectCloneConfig> configs = new List<MapObjectCloneConfig>();
            
            if (sceneName == "Level_GroundZero_1")
            {
                // 零号区地图的复制配置
                
                // 1. 路障 - 封堵出口
                configs.Add(new MapObjectCloneConfig(
                    "Prfb_Roadblock_1",
                    "Group_",
                    new Vector3(425.35f, 0.02f, 254.49f),
                    "BossRush_Roadblock"
                ));
                
                // 2. 火焰烟雾特效 - 复制到出口位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_GroundZero_1",
                    new Vector3(447.50f, 0.01f, 288.27f),
                    "BossRush_Exit_FireSmoke"
                ));
                
                // 3. 铁丝网 - 封堵地形缺口
                configs.Add(new MapObjectCloneConfig(
                    "Prfb_BarbedWire_01_03_20",
                    "Group_",
                    new Vector3(455.80f, 0.02f, 306.78f),
                    "BossRush_BarbedWire"
                ));
            }
            else if (sceneName == "Level_ChallengeSnow")
            {
                // 零度挑战地图的复制配置
                
                // 1. 集装箱 - 作为竞技场边界
                configs.Add(new MapObjectCloneConfig(
                    "Pfb_Container_01_B_Season_64",
                    "Env",
                    new Vector3(242.54f, -0.01f, 259.44f),
                    "BossRush_Container_Clone",
                    270f  // Y轴旋转270度
                ));
                
                // 2. 篝火 - 作为交互点载体
                configs.Add(new MapObjectCloneConfig(
                    "Pfb_Campingfire",
                    "Env",
                    new Vector3(225.32f, 0.01f, 285.64f),
                    "BossRush_Campfire_Interact",
                    0f
                ));
            }
            else if (sceneName == "Level_HiddenWarehouse")
            {
                // 仓库区地图的围栏配置（使用 Prfb_Roadblock_33）
                // 围栏数据从 Player.log 提取
                
                // 南侧围栏（旋转0°和180°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(125.90f, 0.00f, 162.66f), "BossRush_Barrier_1", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(123.65f, 0.00f, 162.67f), "BossRush_Barrier_2", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(121.36f, 0.00f, 162.67f), "BossRush_Barrier_3", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(119.09f, 0.00f, 162.65f), "BossRush_Barrier_4", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(116.81f, 0.00f, 162.64f), "BossRush_Barrier_5", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(114.54f, 0.00f, 162.64f), "BossRush_Barrier_6", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(112.27f, 0.00f, 162.63f), "BossRush_Barrier_7", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.02f, 0.00f, 162.64f), "BossRush_Barrier_8", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(107.76f, 0.00f, 162.65f), "BossRush_Barrier_9", 0f));
                
                // 西侧围栏（旋转270°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.72f, 0.00f, 163.89f), "BossRush_Barrier_10", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 166.16f), "BossRush_Barrier_11", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 168.44f), "BossRush_Barrier_12", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.75f, 0.00f, 170.70f), "BossRush_Barrier_13", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 172.90f), "BossRush_Barrier_14", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 175.11f), "BossRush_Barrier_15", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.76f, 0.00f, 177.37f), "BossRush_Barrier_16", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.76f, 0.00f, 179.62f), "BossRush_Barrier_17", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.77f, 0.00f, 181.81f), "BossRush_Barrier_18", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.79f, 0.00f, 184.01f), "BossRush_Barrier_19", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.80f, 0.00f, 186.30f), "BossRush_Barrier_20", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.80f, 0.00f, 188.55f), "BossRush_Barrier_21", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.79f, 0.00f, 190.81f), "BossRush_Barrier_22", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.82f, 0.00f, 193.07f), "BossRush_Barrier_23", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.83f, 0.00f, 195.29f), "BossRush_Barrier_24", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.84f, 0.00f, 197.51f), "BossRush_Barrier_25", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.87f, 0.00f, 199.79f), "BossRush_Barrier_26", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.88f, 0.00f, 202.05f), "BossRush_Barrier_27", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.88f, 0.00f, 204.25f), "BossRush_Barrier_28", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.89f, 0.00f, 206.50f), "BossRush_Barrier_29", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.90f, 0.00f, 208.77f), "BossRush_Barrier_30", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.91f, 0.00f, 210.97f), "BossRush_Barrier_31", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.92f, 0.00f, 213.22f), "BossRush_Barrier_32", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.92f, 0.00f, 214.60f), "BossRush_Barrier_33", 270f));
                
                // 北侧围栏（旋转180°和特殊角度）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(107.93f, 0.00f, 215.85f), "BossRush_Barrier_34", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.17f, 0.00f, 215.86f), "BossRush_Barrier_35", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.43f, 0.00f, 216.92f), "BossRush_Barrier_36", 300f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(111.13f, 0.00f, 218.23f), "BossRush_Barrier_37", 150f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(118.65f, 0.00f, 218.73f), "BossRush_Barrier_38", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(120.92f, 0.00f, 218.72f), "BossRush_Barrier_39", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(124.35f, 0.00f, 218.83f), "BossRush_Barrier_40", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(127.81f, 0.00f, 218.77f), "BossRush_Barrier_41", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(130.08f, 0.00f, 218.77f), "BossRush_Barrier_42", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(132.32f, 0.00f, 218.76f), "BossRush_Barrier_43", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(134.58f, 0.00f, 218.78f), "BossRush_Barrier_44", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(136.80f, 0.00f, 218.79f), "BossRush_Barrier_45", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(139.07f, 0.00f, 218.78f), "BossRush_Barrier_46", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(141.35f, 0.00f, 218.79f), "BossRush_Barrier_47", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(143.62f, 0.00f, 218.79f), "BossRush_Barrier_48", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(145.88f, 0.00f, 218.81f), "BossRush_Barrier_49", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(148.10f, 0.00f, 218.79f), "BossRush_Barrier_50", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.38f, 0.00f, 218.69f), "BossRush_Barrier_51", 15f));
                
                // 东侧围栏（旋转270°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.16f, 0.00f, 214.34f), "BossRush_Barrier_52", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 212.05f), "BossRush_Barrier_53", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 209.79f), "BossRush_Barrier_54", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 207.58f), "BossRush_Barrier_55", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 205.34f), "BossRush_Barrier_56", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 203.05f), "BossRush_Barrier_57", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.16f, 0.00f, 200.83f), "BossRush_Barrier_58", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.15f, 0.00f, 198.57f), "BossRush_Barrier_59", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.13f, 0.00f, 196.52f), "BossRush_Barrier_60", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 194.30f), "BossRush_Barrier_61", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 192.01f), "BossRush_Barrier_62", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.06f, 0.00f, 189.82f), "BossRush_Barrier_63", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 187.61f), "BossRush_Barrier_64", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 185.38f), "BossRush_Barrier_65", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.10f, 0.00f, 183.12f), "BossRush_Barrier_66", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 180.88f), "BossRush_Barrier_67", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 178.68f), "BossRush_Barrier_68", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.12f, 0.00f, 176.52f), "BossRush_Barrier_69", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.12f, 0.00f, 174.28f), "BossRush_Barrier_70", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.13f, 0.00f, 172.10f), "BossRush_Barrier_71", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.15f, 0.00f, 169.86f), "BossRush_Barrier_72", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 167.65f), "BossRush_Barrier_73", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 165.50f), "BossRush_Barrier_74", 270f));
                
                // 南侧围栏补充（旋转180°和特殊角度）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(130.64f, 0.00f, 162.87f), "BossRush_Barrier_75", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(132.84f, 0.00f, 162.84f), "BossRush_Barrier_76", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(135.07f, 0.00f, 162.82f), "BossRush_Barrier_77", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(137.24f, 0.00f, 162.80f), "BossRush_Barrier_78", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(139.52f, 0.00f, 162.80f), "BossRush_Barrier_79", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(141.76f, 0.00f, 162.78f), "BossRush_Barrier_80", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(144.03f, 0.00f, 162.80f), "BossRush_Barrier_81", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(146.31f, 0.00f, 162.81f), "BossRush_Barrier_82", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(148.57f, 0.00f, 162.78f), "BossRush_Barrier_83", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(149.91f, 0.00f, 163.34f), "BossRush_Barrier_84", 285f));
                
                // 撤离点 - 复制场景中的 Exit(Clone) 到指定位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_HiddenWarehouse",
                    new Vector3(108.64f, 0.02f, 213.95f),
                    "BossRush_Exit_FireSmoke"
                ));
            }
            else if (sceneName == "Level_Farm_01")
            {
                // 农场镇地图的围栏配置（使用 Prfb_Shop_Shelf_01_53 商店货架）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Zone_D1/Pfb_Store_01/Indoor/Prfb_Shop_Shelf_01_53
                // 直接父对象是 Indoor
                
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(368.33f, 0.02f, 600.91f), "BossRush_Barrier_1", 2f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(365.53f, 0.02f, 597.44f), "BossRush_Barrier_2", 272f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(384.55f, 0.02f, 600.93f), "BossRush_Barrier_3", 2f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(420.01f, 0.02f, 589.50f), "BossRush_Barrier_4", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(419.89f, 0.02f, 582.90f), "BossRush_Barrier_5", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(420.07f, 0.02f, 576.10f), "BossRush_Barrier_6", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(400.42f, 0.02f, 557.33f), "BossRush_Barrier_7", 182f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(368.64f, 0.02f, 557.40f), "BossRush_Barrier_8", 182f));
                
                // 撤离点 - 复制场景中的 Exit(Clone) 到指定位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_Farm_01",
                    new Vector3(355.37f, 0.02f, 589.19f),
                    "BossRush_Exit_FireSmoke"
                ));
            }
            else if (sceneName == "Level_JLab_1")
            {
                // J-Lab 实验室地图的围栏配置（使用 Pfb_JLABContainer_13 集装箱）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Center_01/Group/Pfb_JLABContainer_13
                
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-94.99f, 0.00f, -56.06f), "BossRush_Barrier_1", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-80.70f, 0.00f, -44.28f), "BossRush_Barrier_2", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-65.28f, 1.01f, -17.18f), "BossRush_Barrier_3", 90f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-11.35f, 0.00f, -16.58f), "BossRush_Barrier_4", 90f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(3.02f, 0.00f, -49.92f), "BossRush_Barrier_5", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(3.06f, 0.00f, -54.86f), "BossRush_Barrier_6", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(5.85f, 0.00f, -56.93f), "BossRush_Barrier_7", 330f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-24.20f, 0.00f, -73.11f), "BossRush_Barrier_8", 270f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-54.28f, 0.00f, -63.48f), "BossRush_Barrier_9", 270f));
                
                // 撤离点 - 复制场景中的 ExitNoSmoke_1 到指定位置（无烟雾版本，使用 CapsuleCollider）
                configs.Add(new MapObjectCloneConfig(
                    "ExitNoSmoke_1",
                    "Exits",
                    new Vector3(-90.76f, 0.02f, -56.25f),
                    "BossRush_Exit_JLab"
                ));
            }
            else if (sceneName == "Level_StormZone_B0")
            {
                // 风暴区地下地图的围栏配置（使用 Pfb_BarbedWire_01_03 铁丝网）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Boss/BarbedWire_Line/Pfb_BarbedWire_01_03
                
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(102.76f, 0.09f, 454.12f), "BossRush_Barrier_1", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(105.05f, 0.00f, 454.16f), "BossRush_Barrier_2", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(107.33f, 0.00f, 454.09f), "BossRush_Barrier_3", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(109.64f, 0.00f, 454.07f), "BossRush_Barrier_4", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(111.90f, 0.00f, 454.12f), "BossRush_Barrier_5", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(114.18f, 0.00f, 454.09f), "BossRush_Barrier_6", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(116.46f, 0.00f, 454.04f), "BossRush_Barrier_7", 345f));
                
                // 撤离点使用 CreateBossRushExit 方法创建（不再复制模板）
            }
            // 后续可以添加其他地图的配置
            
            return configs;
        }
        
        /// <summary>
        /// 使用游戏原生 exitPrefab 创建 BossRush 撤离点
        /// </summary>
        private void CreateBossRushExit(Vector3 position, string exitName)
        {
            try
            {
                // 方案1：使用 LevelManager.ExitCreator.exitPrefab（最优方案，直接使用游戏原生预制体）
                if (LevelManager.Instance != null && LevelManager.Instance.ExitCreator != null)
                {
                    GameObject exitPrefab = LevelManager.Instance.ExitCreator.exitPrefab;
                    if (exitPrefab != null)
                    {
                        GameObject exit = UnityEngine.Object.Instantiate(exitPrefab, position, Quaternion.identity);
                        exit.name = exitName;
                        exit.SetActive(true);
                        
                        // 确保 CountDownArea 启用
                        CountDownArea countDown = exit.GetComponent<CountDownArea>();
                        if (countDown != null)
                        {
                            countDown.enabled = true;
                        }
                        
                        // 禁用烟雾/粒子效果（室内场景不需要）
                        DisableExitSmokeEffects(exit);
                        
                        DevLog("[BossRush] 使用 exitPrefab 创建撤离点: " + exitName + " 位置: " + position);
                        return;
                    }
                }
                
                // 方案2：从头创建一个简单的撤离点（无烟雾效果）
                // 跳过 FindObjectsOfType 遍历，直接创建简单撤离点，性能更优
                CreateSimpleExit(position, exitName);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CreateBossRushExit 失败: " + e.Message);
                // 回退到简单撤离点
                CreateSimpleExit(position, exitName);
            }
        }
        
        /// <summary>
        /// 禁用撤离点的烟雾/粒子效果（用于室内场景）
        /// [性能优化] 使用字符串缓存避免重复 ToLower 调用
        /// </summary>
        private void DisableExitSmokeEffects(GameObject exit)
        {
            try
            {
                int disabledCount = 0;
                
                // 查找并禁用名称包含烟雾/粒子相关关键词的子对象
                foreach (Transform child in exit.GetComponentsInChildren<Transform>(true))
                {
                    if (child == exit.transform) continue;  // 跳过根对象
                    
                    // [性能优化] 使用 IndexOf 替代 Contains + ToLower，减少字符串分配
                    string name = child.name;
                    if (name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("fog", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("particle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("effect", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("vfx", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        child.gameObject.SetActive(false);
                        disabledCount++;
                    }
                }
                
                if (disabledCount > 0)
                {
                    DevLog("[BossRush] 已禁用撤离点烟雾效果，禁用子对象数: " + disabledCount);
                }
            }
            catch { }  // 静默失败，不影响主流程
        }
        
        /// <summary>
        /// 从头创建一个简单的撤离点（当无法获取预制体时使用）
        /// </summary>
        private void CreateSimpleExit(Vector3 position, string exitName)
        {
            try
            {
                // 创建撤离点 GameObject
                GameObject exit = new GameObject(exitName);
                exit.transform.position = position;
                
                // 添加触发器 Collider
                BoxCollider collider = exit.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(3f, 2f, 3f);  // 3x2x3 的触发区域
                collider.center = new Vector3(0f, 1f, 0f);  // 中心稍微抬高
                
                // 添加 CountDownArea 组件
                CountDownArea countDown = exit.AddComponent<CountDownArea>();
                
                // 通过反射设置 requiredExtrationTime（私有字段）
                try
                {
                    System.Reflection.FieldInfo timeField = typeof(CountDownArea).GetField("requiredExtrationTime", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (timeField != null)
                    {
                        timeField.SetValue(countDown, 5f);  // 5秒撤离时间
                    }
                }
                catch { }
                
                // 订阅撤离成功事件
                countDown.onCountDownSucceed = new UnityEngine.Events.UnityEvent();
                countDown.onCountDownSucceed.AddListener(() => {
                    DevLog("[BossRush] 撤离成功！");
                    // 触发游戏的撤离逻辑
                    try
                    {
                        if (LevelManager.Instance != null)
                        {
                            // 创建撤离信息并通知
                            EvacuationInfo info = new EvacuationInfo(
                                Duckov.Scenes.MultiSceneCore.ActiveSubSceneID,
                                position
                            );
                            LevelManager.Instance.NotifyEvacuated(info);
                        }
                    }
                    catch (System.Exception e)
                    {
                        DevLog("[BossRush] 调用 NotifyEvacuated 失败: " + e.Message);
                    }
                });
                
                // 订阅倒计时开始/停止事件（显示UI）
                countDown.onCountDownStarted = new UnityEngine.Events.UnityEvent<CountDownArea>();
                countDown.onCountDownStarted.AddListener((area) => {
                    EvacuationCountdownUI.Request(area);
                });
                
                countDown.onCountDownStopped = new UnityEngine.Events.UnityEvent<CountDownArea>();
                countDown.onCountDownStopped.AddListener((area) => {
                    EvacuationCountdownUI.Release(area);
                });
                
                // 室内场景不创建视觉指示器（绿色光柱）
                
                DevLog("[BossRush] 创建简单撤离点: " + exitName + " 位置: " + position);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CreateSimpleExit 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 在 BossRush 模式下生成地图阻挡物（通用函数）
        /// [性能优化] 使用模板缓存避免重复查找，减少 FindObjectsOfType 调用
        /// </summary>
        private void SpawnBossRushMapObjects()
        {
            try
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                List<MapObjectCloneConfig> configs = GetMapCloneConfigs(currentScene);
                
                if (configs.Count == 0)
                {
                    DevLog("[BossRush] SpawnBossRushMapObjects: 当前地图 " + currentScene + " 没有配置复制物品");
                    return;
                }
                
                DevLog("[BossRush] SpawnBossRushMapObjects: 开始在 " + currentScene + " 生成 " + configs.Count + " 个阻挡物");
                
                // 如果配置数量较多（如仓库区84个围栏），使用协程分帧生成避免卡顿
                if (configs.Count > 20)
                {
                    StartCoroutine(SpawnMapObjectsAsync(configs));
                }
                else
                {
                    // [性能优化] 少量配置也使用模板缓存，避免重复遍历
                    SpawnMapObjectsWithCache(configs);
                }
                
                // 为特定地图创建撤离点（使用游戏原生方式）
                CreateBossRushExitForScene(currentScene);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] SpawnBossRushMapObjects 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 为指定场景创建 BossRush 撤离点
        /// </summary>
        private void CreateBossRushExitForScene(string sceneName)
        {
            // 风暴区地下场景
            if (sceneName == "Level_StormZone_B0")
            {
                CreateBossRushExit(new Vector3(109.92f, 0.02f, 503.95f), "BossRush_Exit_StormZone");
            }
            // 其他需要自定义撤离点的场景可以在这里添加
        }
        
        /// <summary>
        /// 使用模板缓存同步生成地图阻挡物（适用于少量配置）
        /// [性能优化] 只遍历一次场景对象，缓存所有需要的模板
        /// </summary>
        private void SpawnMapObjectsWithCache(List<MapObjectCloneConfig> configs)
        {
            // 收集所有需要查找的模板名称
            HashSet<string> templateNames = new HashSet<string>();
            foreach (var config in configs)
            {
                templateNames.Add(config.templateName);
            }
            
            // 一次性查找所有模板（只遍历一次场景）
            Dictionary<string, GameObject> templateCache = new Dictionary<string, GameObject>();
            Dictionary<string, Transform> parentCache = new Dictionary<string, Transform>();
            
            // [性能优化] 使用 FindObjectsOfType<Transform> 比 FindObjectsOfType<GameObject> 更快
            Transform[] allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>();
            
            foreach (Transform t in allTransforms)
            {
                if (templateNames.Contains(t.name))
                {
                    // 为每个配置检查父对象前缀
                    foreach (var config in configs)
                    {
                        if (t.name == config.templateName)
                        {
                            string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                            if (!templateCache.ContainsKey(cacheKey))
                            {
                                if (t.parent != null && t.parent.name.StartsWith(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                                else if (string.IsNullOrEmpty(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                            }
                        }
                    }
                }
            }
            
            // 使用缓存的模板生成所有阻挡物
            int totalCreated = 0;
            foreach (var config in configs)
            {
                string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                if (templateCache.TryGetValue(cacheKey, out GameObject template))
                {
                    Transform parentTransform = parentCache.ContainsKey(cacheKey) ? parentCache[cacheKey] : null;
                    CloneMapObjectFast(template, parentTransform, config);
                    totalCreated++;
                }
                else
                {
                    DevLog("[BossRush] CloneMapObject: 未找到模板 " + config.templateName + " (父对象前缀: " + config.parentNamePrefix + ")");
                }
            }
            
            DevLog("[BossRush] SpawnBossRushMapObjects: 完成，共创建 " + totalCreated + " 个阻挡物");
        }
        
        /// <summary>
        /// 异步分帧生成地图阻挡物（平滑生成，在2秒内完成，避免卡顿）
        /// [性能优化] 使用 Transform 查找替代 GameObject，减少内存分配
        /// </summary>
        private System.Collections.IEnumerator SpawnMapObjectsAsync(List<MapObjectCloneConfig> configs)
        {
            // 等待一小段时间让场景完全稳定后再开始生成
            yield return new WaitForSeconds(0.3f);
            
            // [性能优化] 收集所有需要查找的模板名称
            HashSet<string> templateNames = new HashSet<string>();
            foreach (var config in configs)
            {
                templateNames.Add(config.templateName);
            }
            
            // [性能优化] 使用 FindObjectsOfType<Transform> 比 FindObjectsOfType<GameObject> 更快
            Transform[] allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>();
            
            // 预先查找并缓存模板对象（只遍历一次）
            Dictionary<string, GameObject> templateCache = new Dictionary<string, GameObject>();
            Dictionary<string, Transform> parentCache = new Dictionary<string, Transform>();
            
            foreach (Transform t in allTransforms)
            {
                if (templateNames.Contains(t.name))
                {
                    foreach (var config in configs)
                    {
                        if (t.name == config.templateName)
                        {
                            string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                            if (!templateCache.ContainsKey(cacheKey))
                            {
                                if (t.parent != null && t.parent.name.StartsWith(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                                else if (string.IsNullOrEmpty(config.parentNamePrefix))
                                {
                                    templateCache[cacheKey] = t.gameObject;
                                    parentCache[cacheKey] = t.parent;
                                }
                            }
                        }
                    }
                }
            }
            
            // 平滑生成：每帧只生成少量对象，在约2秒内完成
            // 84个围栏，2秒 = 120帧（60fps），每帧约0.7个 -> 每帧1个，间隔约0.02秒
            const int batchSize = 3;  // [性能优化] 每批生成3个，加快生成速度
            const float batchInterval = 0.016f;  // 约60fps的帧间隔
            int count = 0;
            int totalCreated = 0;
            
            foreach (MapObjectCloneConfig config in configs)
            {
                string cacheKey = config.templateName + "|" + config.parentNamePrefix;
                GameObject template = templateCache.ContainsKey(cacheKey) ? templateCache[cacheKey] : null;
                Transform parentTransform = parentCache.ContainsKey(cacheKey) ? parentCache[cacheKey] : null;
                
                if (template != null)
                {
                    // 快速克隆（不输出单个日志）
                    CloneMapObjectFast(template, parentTransform, config);
                    totalCreated++;
                }
                
                count++;
                if (count >= batchSize)
                {
                    count = 0;
                    yield return new WaitForSeconds(batchInterval);  // 等待一小段时间
                }
            }
            
            DevLog("[BossRush] SpawnBossRushMapObjects: 异步生成完成，共创建 " + totalCreated + " 个阻挡物");
        }
        
        /// <summary>
        /// 快速克隆地图物品（不输出单个日志，用于批量生成）
        /// [修复] 撤离点复制后自动激活，确保玩家可以使用
        /// </summary>
        private void CloneMapObjectFast(GameObject template, Transform parentTransform, MapObjectCloneConfig config)
        {
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(template);
                clone.name = config.cloneName;
                clone.transform.position = config.targetPosition;
                
                if (config.rotationY.HasValue)
                {
                    Vector3 templateEuler = template.transform.rotation.eulerAngles;
                    clone.transform.rotation = Quaternion.Euler(templateEuler.x, config.rotationY.Value, templateEuler.z);
                }
                else
                {
                    clone.transform.rotation = template.transform.rotation;
                }
                clone.transform.localScale = template.transform.localScale;
                
                if (parentTransform != null)
                {
                    clone.transform.SetParent(parentTransform);
                }
                
                // [修复] 如果是撤离点（包含 Exit 或 CountDownArea），确保完全激活
                if (config.cloneName.Contains("Exit") || clone.GetComponent<CountDownArea>() != null)
                {
                    // 递归激活所有子对象（确保视觉效果可见）
                    ActivateAllChildren(clone);
                    
                    // 确保 CountDownArea 组件启用
                    CountDownArea countDown = clone.GetComponent<CountDownArea>();
                    if (countDown != null)
                    {
                        countDown.enabled = true;
                    }
                    
                    // 确保 Collider 启用（用于触发进入检测）
                    Collider col = clone.GetComponent<Collider>();
                    if (col != null)
                    {
                        col.enabled = true;
                    }
                    
                    // 启用所有 Renderer（确保可见）
                    Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        r.enabled = true;
                    }
                    
                    DevLog("[BossRush] 撤离点已激活: " + config.cloneName + " 位置: " + config.targetPosition + ", 子对象数: " + clone.transform.childCount);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CloneMapObjectFast 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 递归激活 GameObject 及其所有子对象
        /// </summary>
        private void ActivateAllChildren(GameObject obj)
        {
            if (obj == null) return;
            obj.SetActive(true);
            foreach (Transform child in obj.transform)
            {
                ActivateAllChildren(child.gameObject);
            }
        }
        
        /// <summary>
        /// 根据配置复制单个地图物品
        /// </summary>
        private void CloneMapObject(GameObject[] allObjects, MapObjectCloneConfig config)
        {
            try
            {
                GameObject template = null;
                Transform parentTransform = null;
                
                // 查找模板对象
                foreach (GameObject go in allObjects)
                {
                    if (go.name == config.templateName)
                    {
                        // 检查父对象名称前缀
                        if (go.transform.parent != null && 
                            go.transform.parent.name.StartsWith(config.parentNamePrefix))
                        {
                            template = go;
                            parentTransform = go.transform.parent;
                            break;
                        }
                        // 如果没有指定父对象前缀，直接使用找到的第一个
                        else if (string.IsNullOrEmpty(config.parentNamePrefix))
                        {
                            template = go;
                            parentTransform = go.transform.parent;
                            break;
                        }
                    }
                }
                
                if (template == null)
                {
                    DevLog("[BossRush] CloneMapObject: 未找到模板 " + config.templateName + " (父对象前缀: " + config.parentNamePrefix + ")");
                    return;
                }
                
                // 复制对象
                GameObject clone = UnityEngine.Object.Instantiate(template);
                clone.name = config.cloneName;
                clone.transform.position = config.targetPosition;
                
                // 设置旋转：如果配置了自定义Y轴旋转，则使用；否则使用模板旋转
                if (config.rotationY.HasValue)
                {
                    Vector3 templateEuler = template.transform.rotation.eulerAngles;
                    clone.transform.rotation = Quaternion.Euler(templateEuler.x, config.rotationY.Value, templateEuler.z);
                }
                else
                {
                    clone.transform.rotation = template.transform.rotation;
                }
                clone.transform.localScale = template.transform.localScale;
                
                // 设置父对象
                if (parentTransform != null)
                {
                    clone.transform.SetParent(parentTransform);
                }
                
                DevLog("[BossRush] CloneMapObject: 已复制 " + config.templateName + " 到 " + config.targetPosition + " (名称: " + config.cloneName + ")");
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CloneMapObject 失败 (" + config.templateName + "): " + e.Message);
            }
        }
        
        /// <summary>
        /// 在零号区生成路障（BossRush 模式专用）- 保留旧函数名以兼容
        /// </summary>
        private void SpawnRoadblockInGroundZero()
        {
            SpawnBossRushMapObjects();
        }
        
        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)
        {
            DevLog("[BossRush] WaitForLevelInitializedThenSetup: 开始等待地图完全初始化...");

            // 等待条件：场景已加载、SceneLoader 不在加载中、CharacterMainControl.Main 和 GameCamera.Instance 均已存在
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxWait)
            {
                attempt++;
                bool sceneLoaded = scene.isLoaded;
                bool sceneLoaderDone = true;
                bool mainExists = false;
                bool cameraExists = false;
                bool levelInited = false;

                try
                {
                    sceneLoaderDone = !SceneLoader.IsSceneLoading;
                }
                catch { }

                try
                {
                    mainExists = CharacterMainControl.Main != null;
                }
                catch { }

                try
                {
                    cameraExists = GameCamera.Instance != null;
                }
                catch { }

                try
                {
                    levelInited = LevelManager.LevelInited;
                }
                catch { }

                if (sceneLoaded && sceneLoaderDone && mainExists && cameraExists && levelInited)
                {
                    DevLog("[BossRush] WaitForLevelInitializedThenSetup: 地图初始化完成，第 " + attempt + " 次检查，elapsed=" + elapsed + "s");
                    break;
                }

                if (attempt % 10 == 0)
                {
                    DevLog("[BossRush] WaitForLevelInitializedThenSetup: 第 " + attempt + " 次检查, sceneLoaded=" + sceneLoaded + ", sceneLoaderDone=" + sceneLoaderDone + ", mainExists=" + mainExists + ", cameraExists=" + cameraExists + ", levelInited=" + levelInited + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            DevLog("[BossRush] WaitForLevelInitializedThenSetup: 结束等待, elapsed=" + elapsed + "s");

            // 执行原来的设置逻辑
            StartCoroutine(SetupBossRushInDemoChallenge(scene));
        }
        
        // ========== 龙息武器火焰特效（仅视觉效果）==========
        
        // 是否已订阅手持物品变更事件
        private bool dragonBreathEffectEventSubscribed = false;
        // 缓存的玩家角色引用
        private CharacterMainControl cachedMainCharForEffect = null;
        
        /// <summary>
        /// 延迟订阅龙息武器火焰特效事件（等待玩家角色初始化）
        /// </summary>
        private System.Collections.IEnumerator DelayedSubscribeDragonBreathEvents()
        {
            // 等待0.5秒确保玩家角色已初始化
            yield return new WaitForSeconds(0.5f);
            SubscribeDragonBreathEffectEvent();
        }
        
        /// <summary>
        /// 订阅手持物品变更事件（用于添加火焰特效）
        /// </summary>
        private void SubscribeDragonBreathEffectEvent()
        {
            try
            {
                if (LevelManager.Instance == null) return;
                var mainChar = LevelManager.Instance.MainCharacter;
                if (mainChar == null) return;
                
                // 如果已订阅同一个角色，跳过
                if (dragonBreathEffectEventSubscribed && cachedMainCharForEffect == mainChar) return;
                
                // 先取消之前的订阅
                UnsubscribeDragonBreathEffectEvent();
                
                // 订阅手持物品变更事件
                mainChar.OnHoldAgentChanged += OnPlayerHoldAgentChanged;
                
                cachedMainCharForEffect = mainChar;
                dragonBreathEffectEventSubscribed = true;
                
                DevLog("[DragonBreath] 已订阅手持物品变更事件（火焰特效）");
                
                // 始终订阅Buff事件（龙裔遗族Boss也会发射龙息子弹，需要触发龙焰灼烧Buff）
                DragonBreathBuffHandler.Subscribe();
                
                // 检查当前手持的武器（处理玩家进入存档时已装备龙息武器的情况）
                var currentGun = mainChar.GetGun();
                if (currentGun != null)
                {
                    // 添加火焰特效
                    DragonBreathWeaponConfig.TryAddFireEffectsToAgent(currentGun);
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonBreath] 订阅火焰特效事件失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消订阅火焰特效事件
        /// </summary>
        private void UnsubscribeDragonBreathEffectEvent()
        {
            try
            {
                if (!dragonBreathEffectEventSubscribed || cachedMainCharForEffect == null) return;
                
                cachedMainCharForEffect.OnHoldAgentChanged -= OnPlayerHoldAgentChanged;
                
                cachedMainCharForEffect = null;
                dragonBreathEffectEventSubscribed = false;
            }
            catch { }
        }
        
        /// <summary>
        /// 玩家手持物品变更回调（添加火焰特效）
        /// 注意：Buff事件订阅不再与玩家手持武器绑定，因为龙裔遗族Boss也会发射龙息子弹
        /// </summary>
        private void OnPlayerHoldAgentChanged(DuckovItemAgent newAgent)
        {
            var gunAgent = newAgent as ItemAgent_Gun;
            
            // 检查是否为龙息武器
            bool isDragonBreath = gunAgent != null && 
                                  gunAgent.Item != null && 
                                  gunAgent.Item.TypeID == DragonBreathConfig.WEAPON_TYPE_ID;
            
            if (isDragonBreath)
            {
                // 装备龙息武器：添加火焰特效
                DragonBreathWeaponConfig.TryAddFireEffectsToAgent(gunAgent);
            }
            // 不再在此处取消订阅Buff事件，因为Boss的龙息子弹也需要触发龙焰灼烧Buff
        }
    }
}

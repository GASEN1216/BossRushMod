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

        private void InjectBossRushTicketIntoShops_Integration()
        {
            if (bossRushTicketTypeId <= 0)
            {
                DevLog("[BossRush] BossRush 船票 TypeID 未初始化，跳过商店注入");
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
            
            // 初始化自定义装备（自动扫描加载 Assets/Equipment/ 目录）
            int equipCount = EquipmentFactory.LoadAllEquipment();
            DevLog("[BossRush] 自动加载装备完成，共 " + equipCount + " 个");
            
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
            
            // 注册龙套装装备槽变化事件
            RegisterDragonSetEvents();
            
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
            Health.OnDead -= OnPlayerDeathInBossRush;
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            
            // 取消注册龙套装事件
            UnregisterDragonSetEvents();
            
            // 取消订阅龙息武器火焰特效事件
            UnsubscribeDragonBreathEffectEvent();
            
            // 清理龙息武器Buff处理器
            DragonBreathBuffHandler.Cleanup();
            
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
            
            // 在任何场景加载后都尝试订阅龙息武器事件
            // 使用延迟调用确保玩家角色已初始化
            StartCoroutine(DelayedSubscribeDragonBreathEvents());

            try
            {
                InjectBossRushTicketIntoShops();
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
                        
                        // 设置当前地图的刷新点
                        SetCurrentMapSpawnPoints(scene.name);

                        // 设置BossRush竞技场
                        demoChallengeStartPosition = Vector3.zero;
                        // 延迟到地图完全加载后再执行传送和创建交互点，避免被游戏自身的出生点逻辑覆盖
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
                    
                    // 其他场景：注入传送到竞技场的交互选项
                    StartCoroutine(FindInteractionTargets(10));
                }
            }
            catch { }
        }

        /// <summary>
        /// 将玩家传送到自定义位置（用于 BossRush 地图选择中的非默认地图）
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
                    // 使用 Raycast 修正落点到地面
                    Vector3 finalPosition = targetPosition;
                    RaycastHit hit;
                    if (Physics.Raycast(targetPosition + Vector3.up * 10f, Vector3.down, out hit, 50f))
                    {
                        finalPosition = hit.point + new Vector3(0f, 0.1f, 0f);
                        DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 Raycast 修正落点: " + finalPosition);
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
        /// 在零号区设置 BossRush 模式（类似 SetupBossRushInDemoChallenge）
        /// </summary>
        private System.Collections.IEnumerator SetupBossRushInGroundZero(Vector3 playerPosition)
        {
            DevLog("[BossRush] SetupBossRushInGroundZero: 开始初始化零号区 BossRush 模式");
            
            // 0. 重置 spawner 禁用标志（确保能重新禁用新场景的 spawner）
            spawnersDisabled = false;
            
            // 1. 禁用场景中的 spawner，阻止敌怪生成
            DisableAllSpawners();
            DevLog("[BossRush] SetupBossRushInGroundZero: 已禁用所有敌怪生成器");
            
            // 2. 启动持续清理敌人协程（直到波次开始）
            StartCoroutine(ContinuousClearEnemiesUntilWaveStart());
            
            // 3. 等待场景稳定
            yield return new UnityEngine.WaitForSeconds(0.5f);
            
            // 4. 清理场景中现有的敌人
            ClearEnemiesForBossRush();
            DevLog("[BossRush] SetupBossRushInGroundZero: 已清理现有敌人");
            
            // 5. 生成地图阻挡物（路障、铁丝网等）
            SpawnBossRushMapObjects();
            
            // 6. 创建 BossRush 交互点（优先使用配置的位置，否则使用玩家位置偏移）
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
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
            // 后续可以添加其他地图的配置
            
            return configs;
        }
        
        /// <summary>
        /// 在 BossRush 模式下生成地图阻挡物（通用函数）
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
                
                // 缓存所有 GameObject，避免多次查找
                GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                
                foreach (MapObjectCloneConfig config in configs)
                {
                    CloneMapObject(allObjects, config);
                }
                
                DevLog("[BossRush] SpawnBossRushMapObjects: 完成");
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] SpawnBossRushMapObjects 失败: " + e.Message);
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
                
                // 检查当前手持的武器（处理玩家进入存档时已装备龙息武器的情况）
                var currentGun = mainChar.GetGun();
                if (currentGun != null)
                {
                    // 添加火焰特效
                    DragonBreathWeaponConfig.TryAddFireEffectsToAgent(currentGun);
                    
                    // 如果是龙息武器，订阅Buff事件
                    if (currentGun.Item != null && currentGun.Item.TypeID == DragonBreathConfig.WEAPON_TYPE_ID)
                    {
                        DragonBreathBuffHandler.Subscribe();
                    }
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
        /// 玩家手持物品变更回调（添加火焰特效 + 管理Buff事件订阅）
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
                // 装备龙息武器：添加火焰特效 + 订阅Buff事件
                DragonBreathWeaponConfig.TryAddFireEffectsToAgent(gunAgent);
                DragonBreathBuffHandler.Subscribe();
            }
            else
            {
                // 卸下龙息武器：取消订阅Buff事件（节省性能）
                DragonBreathBuffHandler.Unsubscribe();
            }
        }
    }
}

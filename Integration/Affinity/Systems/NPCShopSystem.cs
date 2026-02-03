// ============================================================================
// NPCShopSystem.cs - 通用NPC商店系统
// ============================================================================
// 模块说明：
//   通用的NPC商店系统，支持任意实现 INPCShopConfig 的NPC。
//   管理商店创建、商品配置、折扣应用等功能。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 通用NPC商店系统
    /// </summary>
    public static class NPCShopSystem
    {
        // ============================================================================
        // 私有字段
        // ============================================================================
        
        // 当前活动的商店
        private static string currentNpcId = null;
        private static GameObject shopObject = null;
        private static StockShop currentShop = null;
        private static Transform currentNpcTransform = null;
        private static GoblinNPCController currentController = null;
        private static bool isServiceActive = false;
        
        // 反射缓存
        private static FieldInfo textSellField = null;
        private static bool reflectionInitialized = false;
        private static string originalTextSell = null;
        
        // 常量
        private const float BUBBLE_Y_OFFSET = 1.2f;
        private const float BUBBLE_DURATION = 3f;
        
        // ============================================================================
        // 公共属性
        // ============================================================================
        
        /// <summary>
        /// 检查服务是否激活
        /// </summary>
        public static bool IsServiceActive => isServiceActive;
        
        /// <summary>
        /// 当前活动的NPC ID
        /// </summary>
        public static string CurrentNpcId => currentNpcId;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 检查指定NPC的商店是否已解锁
        /// </summary>
        public static bool IsShopUnlocked(string npcId)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var shopConfig = config as INPCShopConfig;
            
            if (shopConfig == null || !shopConfig.ShopEnabled)
            {
                return false;
            }
            
            int level = AffinityManager.GetLevel(npcId);
            return level >= shopConfig.ShopUnlockLevel;
        }
        
        /// <summary>
        /// 打开指定NPC的商店
        /// </summary>
        public static void OpenShop(string npcId, Transform npcTransform, GoblinNPCController controller = null)
        {
            if (isServiceActive)
            {
                ModBehaviour.DevLog("[NPCShop] 商店已在运行中，忽略重复调用");
                return;
            }
            
            if (!IsShopUnlocked(npcId))
            {
                ModBehaviour.DevLog("[NPCShop] 商店未解锁: " + npcId);
                return;
            }
            
            // 检查 StockShopView 是否存在于当前场景
            // StockShopView 是场景中预配置的 UI 预制体，某些场景可能没有
            var shopView = StockShopView.Instance;
            if (shopView == null)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 当前场景没有 StockShopView，无法打开商店UI");
                
                // 显示提示气泡
                if (npcTransform != null)
                {
                    string hint = L10n.T("这里不方便做生意...", "Not a good place for business...");
                    try
                    {
                        Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                            Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                                hint,
                                npcTransform,
                                BUBBLE_Y_OFFSET,
                                false,
                                false,
                                -1f,
                                BUBBLE_DURATION
                            )
                        );
                    }
                    catch { }
                }
                return;
            }
            
            ModBehaviour.DevLog("[NPCShop] 开始打开商店: " + npcId);
            
            // 初始化反射缓存
            InitializeReflection();
            
            // 保存状态
            currentNpcId = npcId;
            currentNpcTransform = npcTransform;
            currentController = controller;
            
            // 让NPC进入对话状态
            if (currentController != null)
            {
                currentController.StartDialogue();
            }
            
            // 创建商店
            CreateShop(npcId);
            
            // 注册事件
            RegisterEvents();
            
            // 打开商店UI
            if (currentShop != null)
            {
                try
                {
                    currentShop.ShowUI();
                    isServiceActive = true;
                    
                    // 修改UI文字
                    ModifyShopUIText();
                    
                    // 显示购物对话
                    ShowShoppingDialogue(npcId);
                    
                    ModBehaviour.DevLog("[NPCShop] 商店已打开: " + npcId);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCShop] [ERROR] 打开商店失败: " + e.Message);
                    Cleanup();
                }
            }
        }
        
        /// <summary>
        /// 关闭当前商店
        /// </summary>
        public static void CloseShop()
        {
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[NPCShop] 关闭商店: " + currentNpcId);
            
            isServiceActive = false;
            
            // 取消注册事件
            UnregisterEvents();
            
            // 恢复UI文字
            RestoreShopUIText();
            
            // 保存引用
            var savedController = currentController;
            
            // 停止对话状态，显示告别对话
            if (savedController != null)
            {
                savedController.EndDialogueWithStay(10f, true);  // 商店关闭时显示告别对话
            }
            
            // 清理资源
            Cleanup();
            
            ModBehaviour.DevLog("[NPCShop] 商店已关闭");
        }
        
        /// <summary>
        /// 获取指定NPC的当前折扣率
        /// </summary>
        public static float GetDiscount(string npcId)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var shopConfig = config as INPCShopConfig;
            
            if (shopConfig == null)
            {
                return AffinityManager.GetDiscount(npcId);
            }
            
            int level = AffinityManager.GetLevel(npcId);
            return shopConfig.GetDiscountForLevel(level);
        }
        
        // ============================================================================
        // 私有方法 - 初始化
        // ============================================================================
        
        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            
            try
            {
                BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                textSellField = typeof(StockShopView).GetField("textSell", privateInstance);
                reflectionInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 反射初始化失败: " + e.Message);
            }
        }
        
        private static void CreateShop(string npcId)
        {
            // 清理旧商店
            if (shopObject != null)
            {
                UnityEngine.Object.Destroy(shopObject);
            }
            
            // 创建商店对象
            shopObject = new GameObject("NPCShop_" + npcId);
            currentShop = shopObject.AddComponent<StockShop>();
            
            // 配置商品
            ConfigureShopEntries(npcId);
            
            // 关键：手动缓存物品实例到 itemInstances 字典
            // 游戏原生商店在 Start() 中异步调用 CacheItemInstances()
            // 但我们是动态创建的商店，需要在 ShowUI() 之前手动填充
            CacheItemInstancesManually(npcId);
            
            ModBehaviour.DevLog("[NPCShop] 商店创建成功: " + npcId);
        }
        
        /// <summary>
        /// 手动缓存物品实例（解决动态创建商店的空引用问题）
        /// </summary>
        private static void CacheItemInstancesManually(string npcId)
        {
            if (currentShop == null || currentShop.entries == null)
            {
                ModBehaviour.DevLog("[NPCShop] CacheItemInstances: 商店或条目为空");
                return;
            }
            
            try
            {
                // 使用 ReflectionCache 获取 itemInstances 字段
                var fItems = ReflectionCache.StockShop_ItemInstances;
                if (fItems == null)
                {
                    ModBehaviour.DevLog("[NPCShop] [ERROR] ReflectionCache.StockShop_ItemInstances 为空");
                    return;
                }
                
                var dict = fItems.GetValue(currentShop) as Dictionary<int, Item>;
                if (dict == null)
                {
                    dict = new Dictionary<int, Item>();
                    fItems.SetValue(currentShop, dict);
                }
                
                // 为每个商品条目创建物品实例
                foreach (var entry in currentShop.entries)
                {
                    if (entry == null) continue;
                    
                    int typeId = entry.ItemTypeID;
                    if (dict.ContainsKey(typeId)) continue;
                    
                    try
                    {
                        Item item = ItemAssetsCollection.InstantiateSync(typeId);
                        if (item != null)
                        {
                            dict[typeId] = item;
                            ModBehaviour.DevLog("[NPCShop] 缓存物品实例: TypeID=" + typeId);
                        }
                        else
                        {
                            ModBehaviour.DevLog("[NPCShop] [WARNING] 无法创建物品实例: TypeID=" + typeId);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[NPCShop] [ERROR] 创建物品实例失败: TypeID=" + typeId + ", " + e.Message);
                    }
                }
                
                ModBehaviour.DevLog("[NPCShop] 物品实例缓存完成，共 " + dict.Count + " 个");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [ERROR] CacheItemInstances 失败: " + e.Message);
            }
        }
        
        private static void ConfigureShopEntries(string npcId)
        {
            if (currentShop == null) return;
            
            currentShop.entries.Clear();
            
            var config = AffinityManager.GetNPCConfig(npcId);
            var shopConfig = config as INPCShopConfig;
            
            if (shopConfig == null)
            {
                ModBehaviour.DevLog("[NPCShop] NPC未实现商店配置接口: " + npcId);
                return;
            }
            
            int level = AffinityManager.GetLevel(npcId);
            float discount = shopConfig.GetDiscountForLevel(level);
            
            ModBehaviour.DevLog("[NPCShop] 配置商品，等级: " + level + ", 折扣: " + (discount * 100) + "%");
            
            // 获取商品列表
            List<ShopItemEntry> items = shopConfig.GetShopItems();
            if (items == null || items.Count == 0)
            {
                ModBehaviour.DevLog("[NPCShop] 商店暂无商品");
                return;
            }
            
            // 添加商品
            foreach (var item in items)
            {
                // 检查解锁等级
                if (level < item.RequiredLevel)
                {
                    continue;
                }
                
                try
                {
                    StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                    itemEntry.typeID = item.TypeID;
                    itemEntry.maxStock = item.MaxStock;
                    itemEntry.priceFactor = item.BasePriceFactor * (1.0f - discount);
                    itemEntry.forceUnlock = true;
                    itemEntry.lockInDemo = false;
                    itemEntry.possibility = item.Possibility;
                    
                    StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                    wrapped.CurrentStock = item.MaxStock;
                    wrapped.Show = true;
                    
                    currentShop.entries.Add(wrapped);
                    
                    ModBehaviour.DevLog("[NPCShop] 添加商品 TypeID: " + item.TypeID);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCShop] [ERROR] 添加商品失败: " + e.Message);
                }
            }
        }
        
        // ============================================================================
        // 私有方法 - 事件处理
        // ============================================================================
        
        private static void RegisterEvents()
        {
            ManagedUIElement.onClose += OnManagedUIElementClose;
        }
        
        private static void UnregisterEvents()
        {
            ManagedUIElement.onClose -= OnManagedUIElementClose;
        }
        
        private static void OnManagedUIElementClose(ManagedUIElement element)
        {
            if (!isServiceActive) return;
            
            if (element is StockShopView)
            {
                ModBehaviour.DevLog("[NPCShop] 检测到商店UI关闭");
                CloseShop();
            }
        }
        
        // ============================================================================
        // 私有方法 - UI修改
        // ============================================================================
        
        private static void ModifyShopUIText()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null) return;
                
                if (textSellField != null)
                {
                    var textSell = textSellField.GetValue(shopView) as TMPro.TextMeshProUGUI;
                    if (textSell != null)
                    {
                        originalTextSell = textSell.text;
                        textSell.text = L10n.T("购买", "Buy");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 修改UI文字失败: " + e.Message);
            }
        }
        
        private static void RestoreShopUIText()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null) return;
                
                if (textSellField != null && originalTextSell != null)
                {
                    var textSell = textSellField.GetValue(shopView) as TMPro.TextMeshProUGUI;
                    if (textSell != null)
                    {
                        textSell.text = originalTextSell;
                    }
                }
                
                originalTextSell = null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 恢复UI文字失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 对话
        // ============================================================================
        
        private static void ShowShoppingDialogue(string npcId)
        {
            if (currentNpcTransform == null) return;
            
            try
            {
                string dialogue = NPCDialogueSystem.GetDialogue(npcId, DialogueCategory.Shopping);
                
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                        dialogue,
                        currentNpcTransform,
                        BUBBLE_Y_OFFSET,
                        false,
                        false,
                        -1f,
                        BUBBLE_DURATION
                    )
                );
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 显示对话失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 清理
        // ============================================================================
        
        private static void Cleanup()
        {
            if (shopObject != null)
            {
                UnityEngine.Object.Destroy(shopObject);
                shopObject = null;
                currentShop = null;
            }
            
            currentNpcId = null;
            currentNpcTransform = null;
            currentController = null;
        }
    }
}

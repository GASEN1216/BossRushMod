// ============================================================================
// StorageDepositService.cs - 阿稳寄存服务核心逻辑
// ============================================================================
// 模块说明：
//   管理"阿稳寄存"服务功能，包括：
//   - 创建和配置寄存商店（使用 StockShop 组件）
//   - 处理物品存入（双击背包物品）
//   - 处理物品取回（购买商品）
//   - 动态计算寄存费用
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// TMP 链接点击处理器（处理"全部取出"和"全部丢弃"的点击）
    /// 使用 TMP 的 <link> 标签实现可点击文本区域
    /// </summary>
    public class DepositLinkClickHandler : MonoBehaviour, IPointerClickHandler
    {
        private TextMeshProUGUI textComponent;
        private Camera uiCamera;
        
        void Awake()
        {
            textComponent = GetComponent<TextMeshProUGUI>();
        }
        
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!StorageDepositService.IsServiceActive) return;
            if (textComponent == null) return;
            
            // 获取 UI 相机（用于坐标转换）
            if (uiCamera == null)
            {
                Canvas canvas = textComponent.canvas;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    uiCamera = canvas.worldCamera;
                }
            }
            
            // 检测点击的链接
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, eventData.position, uiCamera);
            if (linkIndex < 0) return;
            
            // 获取链接信息
            TMP_LinkInfo linkInfo = textComponent.textInfo.linkInfo[linkIndex];
            string linkID = linkInfo.GetLinkID();
            
            ModBehaviour.DevLog("[DepositLinkClickHandler] 点击链接: " + linkID);
            
            // 根据链接 ID 执行对应操作
            if (linkID == "retrieve")
            {
                StorageDepositService.OnRetrieveAllClickedPublic();
            }
            else if (linkID == "discard")
            {
                StorageDepositService.OnDiscardAllClickedPublic();
            }
        }
    }
    
    /// <summary>
    /// 阿稳寄存服务核心逻辑（静态类）
    /// </summary>
    public static class StorageDepositService
    {
        // ============================================================================
        // 私有字段
        // ============================================================================
        
        // 商店相关
        private static GameObject shopObject;
        private static StockShop depositShop;
        
        // NPC 引用
        private static Transform courierNPCTransform;
        private static CourierNPCController courierController;
        private static CourierMovement courierMovement;
        
        // 服务状态
        private static bool isServiceActive = false;
        
        // 商品索引映射（Entry -> DepositedItemData 索引）
        private static Dictionary<StockShop.Entry, int> entryIndexMapping = new Dictionary<StockShop.Entry, int>();
        
        // 常量
        private const float GOODBYE_BUBBLE_DURATION = 4f;
        private const float BUBBLE_Y_OFFSET = 1.5f;
        
        // 反射缓存
        private static FieldInfo textSellField = null;
        private static FieldInfo priceTextField = null;
        private static FieldInfo itemInstancesField = null;
        private static FieldInfo accountAvaliableField = null;
        private static MethodInfo cacheItemInstancesMethod = null;
        private static FieldInfo refreshCountDownField = null;
        private static bool reflectionInitialized = false;
        
        // "全部取出"按钮相关
        private static GameObject retrieveAllButtonObj = null;
        #pragma warning disable CS0414
        private static UnityEngine.UI.Button retrieveAllButton = null;
        #pragma warning restore CS0414
        private static TMPro.TextMeshProUGUI retrieveAllText = null;
        private static TMPro.TextMeshProUGUI originalRefreshCountDown = null;
        private static GameObject hiddenRefreshLabel = null;  // 被隐藏的"下次刷新"标签
        private static Color colorEnough = new Color(0.2f, 0.8f, 0.2f);  // 绿色
        private static Color colorNotEnough = new Color(0.9f, 0.2f, 0.2f);  // 红色
        private static Color colorDiscard = new Color(0.7f, 0.3f, 0.3f);  // 丢弃按钮颜色（暗红色）
        
        // "全部丢弃"按钮相关
        private static GameObject discardAllButtonObj = null;
        #pragma warning disable CS0414
        private static UnityEngine.UI.Button discardAllButton = null;
        private static TMPro.TextMeshProUGUI discardAllText = null;
        #pragma warning restore CS0414
        
        // 物品实例缓存（使用唯一索引作为 key，避免同类型物品冲突）
        private static Dictionary<int, Item> depositItemInstances = new Dictionary<int, Item>();
        
        // 原始售出文字（用于恢复）
        private static string originalTextSell = null;
        
        // 待寄存物品（用于拦截售出逻辑）
        #pragma warning disable CS0414
        private static Item pendingDepositItem = null;
        
        // 右键删除相关
        private static bool rightClickDeleteEnabled = true;
        #pragma warning restore CS0414
        
        // ============================================================================
        // 公共属性
        // ============================================================================
        
        /// <summary>
        /// 检查服务是否激活
        /// </summary>
        public static bool IsServiceActive { get { return isServiceActive; } }
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 打开寄存服务（由 CourierStorageInteractable 调用）
        /// </summary>
        public static void OpenService(Transform npcTransform)
        {
            // 调用异步版本
            OpenServiceAsync(npcTransform).Forget();
        }
        
        /// <summary>
        /// 异步打开寄存服务（内部实现）
        /// </summary>
        private static async UniTaskVoid OpenServiceAsync(Transform npcTransform)
        {
            if (isServiceActive)
            {
                ModBehaviour.DevLog("[StorageDepositService] 服务已在运行中，忽略重复调用");
                return;
            }
            
            // 检查快递服务是否正在运行
            if (CourierService.IsServiceActive)
            {
                ModBehaviour.DevLog("[StorageDepositService] 快递服务正在运行，无法打开寄存服务");
                return;
            }
            
            ModBehaviour.DevLog("[StorageDepositService] 开始打开寄存服务...");
            
            // 初始化反射缓存
            InitializeReflection();
            
            // 保存 NPC 引用
            courierNPCTransform = npcTransform;
            
            // 获取 NPC 控制器
            if (npcTransform != null)
            {
                courierController = npcTransform.GetComponent<CourierNPCController>();
                courierMovement = npcTransform.GetComponent<CourierMovement>();
                
                // 停止移动并播放对话动画
                if (courierMovement != null)
                {
                    courierMovement.SetInService(true);
                }
                
                if (courierController != null)
                {
                    courierController.StartTalking();
                }
            }
            
            // 加载寄存数据
            DepositDataManager.Load();
            
            // 异步创建寄存商店（等待物品实例初始化完成）
            await CreateDepositShopAsync();
            
            // 【关键】在打开 UI 之前，将所有寄存物品解锁
            // 这样原版 StockShopItemEntry.Refresh() 中的 EconomyManager.IsUnlocked() 会返回 true
            // 从根源阻止物品被隐藏，而不是事后补救
            UnlockAllDepositedItems();
            
            // 注册事件
            RegisterEvents();
            
            // 打开商店 UI
            bool uiOpened = false;
            if (depositShop != null)
            {
                try
                {
                    depositShop.ShowUI();
                    uiOpened = true;
                    ModBehaviour.DevLog("[StorageDepositService] ShowUI 调用成功");
                    
                    // 等待多帧让 UI 完成初始化
                    await UniTask.DelayFrame(3);
                    
                    // 更新每个商品条目的 ItemDisplay（使用正确的物品实例）
                    // 注意：由于已经在 ShowUI 之前解锁了所有物品，这里不需要强制激活
                    UpdateAllItemDisplays();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [ERROR] ShowUI 失败: " + e.Message + "\n" + e.StackTrace);
                }
            }
            
            if (!uiOpened)
            {
                // UI 打开失败，清理并退出
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] UI 打开失败，清理资源");
                UnregisterEvents();
                Cleanup();
                
                // 恢复 NPC 状态
                if (courierController != null) courierController.StopTalking();
                if (courierMovement != null) courierMovement.SetInService(false);
                return;
            }
            
            isServiceActive = true;
            
            // 修改 UI 文字（一次性）
            ModifyShopUITextOnce();
            
            // 播放鸭子叫声（打开寄存服务时喊一下）
            PlayQuackSound();
            
            ModBehaviour.DevLog("[StorageDepositService] 寄存服务已打开");
        }
        
        /// <summary>
        /// 关闭寄存服务
        /// </summary>
        public static void CloseService()
        {
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[StorageDepositService] 关闭寄存服务...");
            
            isServiceActive = false;
            
            // 取消注册事件
            UnregisterEvents();
            
            // 恢复原始 UI 文字
            RestoreShopUIText();
            
            // 保存引用（Cleanup 会清空）
            var savedController = courierController;
            var savedMovement = courierMovement;
            var savedNPCTransform = courierNPCTransform;
            
            // 停止对话动画
            if (savedController != null)
            {
                savedController.StopTalking();
            }
            
            // 恢复移动
            if (savedMovement != null)
            {
                savedMovement.SetInService(false);
            }
            
            // 显示告别气泡
            ShowGoodbyeBubble(savedNPCTransform);
            
            // 清理资源
            Cleanup();
            
            ModBehaviour.DevLog("[StorageDepositService] 寄存服务已关闭");
        }
        
        /// <summary>
        /// 计算寄存费（外部调用）
        /// </summary>
        public static int CalculateDepositFee(DepositedItemData item)
        {
            if (item == null) return 0;
            return item.GetCurrentFee();
        }
        
        // ============================================================================
        // 私有方法 - 事件注册
        // ============================================================================
        
        private static void RegisterEvents()
        {
            // 注册售出事件（拦截原版售出，改为寄存）
            StockShop.OnItemSoldByPlayer += OnItemSoldByPlayer;
            
            // 注册购买事件（物品取回）
            StockShop.OnItemPurchased += OnItemPurchased;
            
            // 注册 UI 关闭事件
            ManagedUIElement.onClose += OnManagedUIElementClose;
            
            // 注册选择变化事件（用于修改价格显示并阻止隐藏）
            ItemUIUtilities.OnSelectionChanged += OnSelectionChanged;
            
            // 注册物品解锁状态变化事件（阻止原版 Refresh() 隐藏物品）
            // 原版 StockShopItemEntry.OnItemUnlockStateChanged 会调用 Refresh()，可能导致物品被隐藏
            EconomyManager.OnItemUnlockStateChanged += OnItemUnlockStateChanged;
        }
        
        private static void UnregisterEvents()
        {
            StockShop.OnItemSoldByPlayer -= OnItemSoldByPlayer;
            StockShop.OnItemPurchased -= OnItemPurchased;
            ManagedUIElement.onClose -= OnManagedUIElementClose;
            ItemUIUtilities.OnSelectionChanged -= OnSelectionChanged;
            EconomyManager.OnItemUnlockStateChanged -= OnItemUnlockStateChanged;
        }
        
        // ============================================================================
        // 私有方法 - 初始化
        // ============================================================================
        
        /// <summary>
        /// 将所有寄存物品解锁（从根源阻止原版隐藏机制）
        /// 原版 StockShopItemEntry.Refresh() 会根据 EconomyManager.IsUnlocked() 隐藏未解锁的物品
        /// 通过在 ShowUI 之前解锁所有物品，可以从根源阻止隐藏行为
        /// </summary>
        private static void UnlockAllDepositedItems()
        {
            var depositedItems = DepositDataManager.GetAllItems();
            int unlockedCount = 0;
            
            foreach (var item in depositedItems)
            {
                if (item == null || item.itemData == null) continue;
                
                int typeID = item.itemData.RootTypeID;
                
                // 检查是否已解锁
                if (!EconomyManager.IsUnlocked(typeID))
                {
                    // 解锁物品（needConfirm=false 直接解锁，showUI=false 不显示通知）
                    EconomyManager.Unlock(typeID, false, false);
                    unlockedCount++;
                    ModBehaviour.DevLog("[StorageDepositService] 解锁物品: TypeID=" + typeID);
                }
            }
            
            if (unlockedCount > 0)
            {
                ModBehaviour.DevLog("[StorageDepositService] 已解锁 " + unlockedCount + " 个物品，从根源阻止隐藏");
            }
        }
        
        /// <summary>
        /// 初始化反射缓存
        /// </summary>
        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            
            try
            {
                // textSell 字段（用于修改"售出"文字）
                textSellField = typeof(StockShopView).GetField("textSell",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // priceText 字段（用于显示寄存费）
                priceTextField = typeof(StockShopView).GetField("priceText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // itemInstances 字典（用于缓存物品实例）
                itemInstancesField = typeof(StockShop).GetField("itemInstances",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // accountAvaliable 字段（用于设置是否允许使用账户余额）
                accountAvaliableField = typeof(StockShop).GetField("accountAvaliable",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // CacheItemInstances 方法
                cacheItemInstancesMethod = typeof(StockShop).GetMethod("CacheItemInstances",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // refreshCountDown 字段（用于替换为"全部取出"按钮）
                refreshCountDownField = typeof(StockShopView).GetField("refreshCountDown",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                reflectionInitialized = true;
                ModBehaviour.DevLog("[StorageDepositService] 反射缓存初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 反射初始化失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 商店创建
        // ============================================================================
        
        /// <summary>
        /// 创建寄存商店（异步，等待物品实例初始化完成）
        /// </summary>
        private static async UniTask CreateDepositShopAsync()
        {
            // 清理旧商店
            if (shopObject != null)
            {
                UnityEngine.Object.Destroy(shopObject);
                shopObject = null;
                depositShop = null;
            }
            
            // 创建商店对象
            shopObject = new GameObject("StorageDepositShop");
            UnityEngine.Object.DontDestroyOnLoad(shopObject);
            
            // 添加 StockShop 组件
            depositShop = shopObject.AddComponent<StockShop>();
            
            // 设置商店名称（使用本地化键，而不是本地化后的文本）
            string shopNameKey = "BossRush_StorageDeposit_ShopName";
            
            // 通过反射设置商店属性
            try
            {
                // 设置 DisplayNameKey（本地化键）
                var displayNameKeyField = typeof(StockShop).GetField("DisplayNameKey",
                    BindingFlags.Public | BindingFlags.Instance);
                if (displayNameKeyField != null)
                {
                    displayNameKeyField.SetValue(depositShop, shopNameKey);
                }
                
                // 设置 sellFactor = 0（寄存免费）
                var sellFactorField = typeof(StockShop).GetField("sellFactor",
                    BindingFlags.Public | BindingFlags.Instance);
                if (sellFactorField != null)
                {
                    sellFactorField.SetValue(depositShop, 0f);
                }
                
                // 设置 accountAvaliable = true（允许使用账户余额，而不仅仅是现金物品）
                if (accountAvaliableField != null)
                {
                    accountAvaliableField.SetValue(depositShop, true);
                    ModBehaviour.DevLog("[StorageDepositService] 已设置 accountAvaliable = true");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 设置商店属性失败: " + e.Message);
            }
            
            // 异步初始化物品实例缓存（等待完成）
            await InitializeItemInstancesAsync();
            
            // 刷新商品列表
            RefreshShopEntries();
            
            ModBehaviour.DevLog("[StorageDepositService] 寄存商店创建成功: " + shopNameKey);
        }
        
        /// <summary>
        /// 异步初始化物品实例缓存（从 ItemTreeData 恢复完整状态）
        /// </summary>
        private static async UniTask InitializeItemInstancesAsync()
        {
            if (depositShop == null || itemInstancesField == null) return;
            
            // 清空旧的缓存
            depositItemInstances.Clear();
            
            try
            {
                // 获取 itemInstances 字典（原版机制，仅用于兼容）
                var itemInstances = itemInstancesField.GetValue(depositShop) as Dictionary<int, Item>;
                if (itemInstances == null)
                {
                    itemInstances = new Dictionary<int, Item>();
                    itemInstancesField.SetValue(depositShop, itemInstances);
                }
                
                // 清空原有缓存
                itemInstances.Clear();
                
                // 为每个寄存物品恢复完整实例
                var depositedItems = DepositDataManager.GetAllItems();
                
                for (int i = 0; i < depositedItems.Count; i++)
                {
                    var depositedItem = depositedItems[i];
                    if (depositedItem == null || depositedItem.itemData == null) continue;
                    
                    int typeID = depositedItem.itemData.RootTypeID;
                    
                    try
                    {
                        // 使用 ItemTreeData.InstantiateAsync 恢复完整物品（包含配件、耐久度等状态）
                        Item restoredItem = await ItemTreeData.InstantiateAsync(depositedItem.itemData);
                        
                        if (restoredItem != null)
                        {
                            // 使用索引作为 key 存储到我们的缓存（每个物品独立）
                            depositItemInstances[i] = restoredItem;
                            
                            // 同时更新商店的 itemInstances（用于原版兼容，但会被覆盖）
                            itemInstances[typeID] = restoredItem;
                            
                            ModBehaviour.DevLog("[StorageDepositService] 恢复物品实例: index=" + i + ", TypeID=" + typeID + ", Name=" + restoredItem.DisplayName);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 恢复物品实例失败: index=" + i + ", error=" + e.Message);
                        
                        // 失败时使用空白物品作为后备
                        Item fallbackItem = ItemAssetsCollection.InstantiateSync(typeID);
                        if (fallbackItem != null)
                        {
                            depositItemInstances[i] = fallbackItem;
                            itemInstances[typeID] = fallbackItem;
                        }
                    }
                }
                
                ModBehaviour.DevLog("[StorageDepositService] 物品实例初始化完成，共 " + depositItemInstances.Count + " 个");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 初始化物品实例缓存失败: " + e.Message);
            }
        }

        /// <summary>
        /// 刷新商店商品列表（根据寄存数据生成）
        /// </summary>
        private static void RefreshShopEntries()
        {
            if (depositShop == null) return;
            
            // 清空现有商品
            depositShop.entries.Clear();
            entryIndexMapping.Clear();
            
            // 获取所有寄存物品
            var depositedItems = DepositDataManager.GetAllItems();
            
            for (int i = 0; i < depositedItems.Count; i++)
            {
                var item = depositedItems[i];
                if (item == null || item.itemData == null) continue;
                
                try
                {
                    int fee = item.GetCurrentFee();
                    
                    // 创建商品条目
                    StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                    itemEntry.typeID = item.itemData.RootTypeID;
                    int stackCount = item.itemData.RootData != null ? item.itemData.RootData.StackCount : 1;
                    itemEntry.maxStock = stackCount;
                    itemEntry.forceUnlock = true;
                    
                    // 动态价格因子：寄存费 / 物品原始价值
                    if (item.itemValue > 0)
                    {
                        itemEntry.priceFactor = (float)fee / item.itemValue;
                    }
                    else
                    {
                        itemEntry.priceFactor = 0.01f;  // 最小价格因子
                    }
                    
                    itemEntry.possibility = 1f;
                    itemEntry.lockInDemo = false;
                    
                    // 包装为 Entry
                    StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                    wrapped.CurrentStock = stackCount;
                    wrapped.Show = true;
                    
                    depositShop.entries.Add(wrapped);
                    
                    // 记录索引映射
                    entryIndexMapping[wrapped] = i;
                    
                    ModBehaviour.DevLog("[StorageDepositService] 添加商品: TypeID=" + item.itemData.RootTypeID + 
                        ", 费用=" + fee + ", 原价值=" + item.itemValue);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 创建商品条目失败: " + e.Message);
                }
            }
            
            ModBehaviour.DevLog("[StorageDepositService] 商品列表刷新完成，共 " + depositShop.entries.Count + " 件");
        }

        // ============================================================================
        // 私有方法 - 事件处理
        // ============================================================================
        
        /// <summary>
        /// UI 关闭事件处理
        /// </summary>
        private static void OnManagedUIElementClose(ManagedUIElement element)
        {
            if (!isServiceActive) return;
            
            // 检查是否是 StockShopView 关闭
            if (element is StockShopView)
            {
                ModBehaviour.DevLog("[StorageDepositService] 检测到商店 UI 关闭");
                CloseService();
            }
        }
        
        /// <summary>
        /// 物品解锁状态变化事件处理
        /// 当有新物品被解锁时，确保我们的寄存物品仍然保持解锁状态
        /// </summary>
        private static void OnItemUnlockStateChanged(int itemTypeID)
        {
            // 由于我们已经在 ShowUI 之前解锁了所有寄存物品，这里不需要额外处理
            // 此事件处理器主要用于防止其他系统意外锁定我们的物品
        }
        
        /// <summary>
        /// 选择变化事件处理（用于修改价格显示）
        /// 注意：由于已经在 ShowUI 之前解锁了所有物品，不需要事后补救
        /// </summary>
        private static void OnSelectionChanged()
        {
            if (!isServiceActive) return;
            
            // 修改价格显示为 0（寄存免费）
            var selectedDisplay = ItemUIUtilities.SelectedItemDisplay;
            if (selectedDisplay != null && selectedDisplay.Target != null)
            {
                UpdatePriceDisplay();
            }
        }
        
        /// <summary>
        /// 物品售出事件处理（拦截原版售出，改为寄存）
        /// </summary>
        private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)
        {
            // 检查是否是我们的商店
            if (shop != depositShop) return;
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[StorageDepositService] 拦截到售出事件: " + 
                (soldItem != null ? soldItem.DisplayName : "null") + ", 价格: " + price);
            
            if (soldItem == null)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 无法获取售出物品信息");
                return;
            }
            
            // 异步处理寄存逻辑
            HandleDepositAsync(soldItem, price).Forget();
        }
        
        /// <summary>
        /// 异步处理物品寄存（优化：只更新新增的物品，不刷新全部）
        /// </summary>
        private static async UniTaskVoid HandleDepositAsync(Item soldItem, int price)
        {
            try
            {
                string itemName = soldItem.DisplayName;
                
                // 扣除玩家获得的钱（寄存是免费的）
                if (price > 0)
                {
                    var cost = new Cost(price);
                    cost.Pay();
                    ModBehaviour.DevLog("[StorageDepositService] 已扣除售出所得: " + price);
                }
                
                // 保存物品数据到寄存系统
                DepositDataManager.AddItem(soldItem);
                int newIndex = DepositDataManager.GetItemCount() - 1;
                ModBehaviour.DevLog("[StorageDepositService] 物品已存入寄存数据: " + itemName + ", index=" + newIndex);
                
                // 获取刚保存的 ItemTreeData
                var depositedItems = DepositDataManager.GetAllItems();
                var newDepositedItem = depositedItems[newIndex];
                
                if (newDepositedItem != null && newDepositedItem.itemData != null)
                {
                    // 异步恢复物品实例（等待完成）
                    Item restoredItem = await ItemTreeData.InstantiateAsync(newDepositedItem.itemData);
                    
                    if (restoredItem != null)
                    {
                        // 存入缓存
                        depositItemInstances[newIndex] = restoredItem;
                        
                        // 同时更新原版 itemInstances（兼容）
                        if (itemInstancesField != null && depositShop != null)
                        {
                            var itemInstances = itemInstancesField.GetValue(depositShop) as Dictionary<int, Item>;
                            if (itemInstances != null)
                            {
                                itemInstances[newDepositedItem.itemData.RootTypeID] = restoredItem;
                            }
                        }
                        
                        ModBehaviour.DevLog("[StorageDepositService] 恢复新寄存物品实例: index=" + newIndex + ", Name=" + restoredItem.DisplayName);
                    }
                }
                
                // 刷新商品列表（添加新条目）
                RefreshShopEntries();
                
                // 刷新 UI 显示（只更新新增的条目）
                RefreshShopUIAndUpdateNewEntry(newIndex);
                
                // 更新"全部取出"按钮
                UpdateRetrieveAllButton();
                
                // 显示通知（使用本地化键）
                string msg = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_Deposited");
                NotificationText.Push(msg);
                
                ModBehaviour.DevLog("[StorageDepositService] 寄存完成: " + itemName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 处理寄存失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 刷新商店 UI 显示
        /// </summary>
        private static void RefreshShopUI()
        {
            // 调用异步版本
            RefreshShopUIAsync().Forget();
        }
        
        /// <summary>
        /// 异步刷新商店 UI 显示（等待 UI 创建完成后再更新）
        /// </summary>
        private static async UniTaskVoid RefreshShopUIAsync()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView != null && depositShop != null)
                {
                    // 使用反射调用 internal 方法 SetupAndShow
                    var setupAndShowMethod = typeof(StockShopView).GetMethod("SetupAndShow",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (setupAndShowMethod != null)
                    {
                        setupAndShowMethod.Invoke(shopView, new object[] { depositShop });
                        ModBehaviour.DevLog("[StorageDepositService] 商店 UI 已刷新");
                        
                        // 等待几帧让 UI 完成创建
                        await UniTask.DelayFrame(2);
                        
                        // 【关键】先隐藏无效条目（已购买的物品），再更新有效条目的显示
                        // 不再调用 ForceActivateAllEntries()，因为它会错误地激活已购买的旧条目
                        HideInvalidUIEntries();
                        
                        // 刷新后更新所有商品条目的 ItemDisplay 和价格
                        UpdateAllItemDisplays();
                    }
                    else
                    {
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 SetupAndShow 方法");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 刷新商店 UI 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 更新所有商品条目的 ItemDisplay（使用正确的物品实例）
        /// 解决原版 itemInstances 使用 TypeID 作为 key 导致同类型物品图标显示错误的问题
        /// 关键：通过 StockShopItemEntry.Target 获取 Entry，再通过 entryIndexMapping 找到正确的 depositIndex
        /// 注意：不再强制激活所有条目，由 HideInvalidUIEntries() 负责隐藏无效条目
        /// 支持同类型物品独立显示：使用 Entry 集合而不是 TypeID 集合判断有效性
        /// </summary>
        private static void UpdateAllItemDisplays()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || depositShop == null) return;
                
                // 调试：输出 entries 数量
                ModBehaviour.DevLog("[StorageDepositService] depositShop.entries.Count = " + depositShop.entries.Count);
                ModBehaviour.DevLog("[StorageDepositService] entryIndexMapping.Count = " + entryIndexMapping.Count);
                ModBehaviour.DevLog("[StorageDepositService] depositItemInstances.Count = " + depositItemInstances.Count);
                
                // 获取商店条目的父容器（通过反射获取 entryTemplate 的 parent）
                var entryTemplateField = typeof(StockShopView).GetField("entryTemplate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (entryTemplateField == null) return;
                
                var entryTemplate = entryTemplateField.GetValue(shopView) as StockShopItemEntry;
                if (entryTemplate == null) return;
                
                Transform entriesParent = entryTemplate.transform.parent;
                if (entriesParent == null) return;
                
                // 获取 StockShopItemEntry 的 itemDisplay 字段
                var itemDisplayField = typeof(StockShopItemEntry).GetField("itemDisplay",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemDisplayField == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 itemDisplay 字段");
                    return;
                }
                
                // 获取 StockShopItemEntry 的 priceText 字段（用于更新价格显示）
                var priceTextFieldEntry = typeof(StockShopItemEntry).GetField("priceText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // 调试：输出父容器中的子对象数量
                int totalChildren = entriesParent.childCount;
                int activeChildren = 0;
                foreach (Transform child in entriesParent)
                {
                    if (child.gameObject.activeSelf) activeChildren++;
                }
                ModBehaviour.DevLog("[StorageDepositService] UI 父容器子对象: 总数=" + totalChildren + ", 激活=" + activeChildren);
                
                // 构建当前有效的 Entry 集合（支持同类型物品独立显示）
                HashSet<StockShop.Entry> validEntries = new HashSet<StockShop.Entry>();
                foreach (var entry in depositShop.entries)
                {
                    if (entry != null)
                    {
                        validEntries.Add(entry);
                    }
                }
                
                // 只激活在有效 Entry 集合中但被隐藏的条目
                int activatedCount = 0;
                foreach (Transform child in entriesParent)
                {
                    if (child == entryTemplate.transform) continue;
                    
                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;
                    
                    var stockEntry = uiEntry.Target;
                    if (stockEntry == null) continue;
                    
                    // 只激活在有效 Entry 集合中的条目（精确匹配 Entry 对象）
                    if (!child.gameObject.activeSelf && validEntries.Contains(stockEntry))
                    {
                        child.gameObject.SetActive(true);
                        activatedCount++;
                        ModBehaviour.DevLog("[StorageDepositService] 激活有效条目: TypeID=" + stockEntry.ItemTypeID);
                    }
                }
                
                if (activatedCount > 0)
                {
                    ModBehaviour.DevLog("[StorageDepositService] 激活了 " + activatedCount + " 个有效条目");
                }
                
                // 更新所有激活条目的 ItemDisplay 和价格显示
                int updatedCount = 0;
                int skippedInactive = 0;
                int skippedNoEntry = 0;
                int skippedNoMapping = 0;
                
                foreach (Transform child in entriesParent)
                {
                    // 跳过模板对象
                    if (child == entryTemplate.transform) continue;
                    
                    // 检查是否激活
                    if (!child.gameObject.activeSelf)
                    {
                        skippedInactive++;
                        continue;
                    }
                    
                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;
                    
                    // 获取 UI 条目对应的 StockShop.Entry
                    var stockEntry = uiEntry.Target;
                    if (stockEntry == null)
                    {
                        skippedNoEntry++;
                        continue;
                    }
                    
                    // 通过 entryIndexMapping 找到正确的 depositIndex
                    int depositIndex;
                    if (!entryIndexMapping.TryGetValue(stockEntry, out depositIndex))
                    {
                        skippedNoMapping++;
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 Entry 对应的 depositIndex, TypeID=" + stockEntry.ItemTypeID);
                        continue;
                    }
                    
                    // 从我们的缓存获取正确的物品实例
                    Item correctItem;
                    if (depositItemInstances.TryGetValue(depositIndex, out correctItem) && correctItem != null)
                    {
                        // 获取 ItemDisplay 并更新
                        var itemDisplay = itemDisplayField.GetValue(uiEntry) as ItemDisplay;
                        if (itemDisplay != null)
                        {
                            itemDisplay.Setup(correctItem);
                            ModBehaviour.DevLog("[StorageDepositService] 更新商品条目 ItemDisplay: index=" + depositIndex + ", Name=" + correctItem.DisplayName);
                            updatedCount++;
                        }
                        
                        // 更新价格显示（使用正确的寄存费用）
                        if (priceTextFieldEntry != null)
                        {
                            var priceTextComp = priceTextFieldEntry.GetValue(uiEntry) as TMPro.TextMeshProUGUI;
                            if (priceTextComp != null)
                            {
                                var depositedItems = DepositDataManager.GetAllItems();
                                if (depositIndex < depositedItems.Count)
                                {
                                    int fee = depositedItems[depositIndex].GetCurrentFee();
                                    priceTextComp.text = fee.ToString("n0");
                                    ModBehaviour.DevLog("[StorageDepositService] 更新价格显示: index=" + depositIndex + ", fee=" + fee);
                                }
                            }
                        }
                        
                    }
                }
                
                ModBehaviour.DevLog("[StorageDepositService] 已更新 " + updatedCount + " 个商品条目的 ItemDisplay");
                ModBehaviour.DevLog("[StorageDepositService] 跳过: 未激活=" + skippedInactive + ", 无Entry=" + skippedNoEntry + ", 无映射=" + skippedNoMapping);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 更新 ItemDisplay 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 强制激活所有寄存商品条目（解决原版 Refresh() 隐藏未解锁物品的问题）
        /// 同时更新价格显示，确保显示正确的寄存费用
        /// 关键改进：不依赖 entryIndexMapping 判断，直接激活所有非模板条目
        /// </summary>
        private static void ForceActivateAllEntries()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || depositShop == null) return;
                
                // 获取商店条目的父容器
                var entryTemplateField = typeof(StockShopView).GetField("entryTemplate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (entryTemplateField == null) return;
                
                var entryTemplate = entryTemplateField.GetValue(shopView) as StockShopItemEntry;
                if (entryTemplate == null) return;
                Transform entriesParent = entryTemplate.transform.parent;
                if (entriesParent == null) return;
                
                // 获取 priceText 字段（用于更新价格显示）
                var priceTextFieldEntry = typeof(StockShopItemEntry).GetField("priceText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                int activatedCount = 0;
                int priceUpdatedCount = 0;
                int totalEntries = 0;
                
                foreach (Transform child in entriesParent)
                {
                    // 跳过模板对象
                    if (child == entryTemplate.transform) continue;
                    
                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;
                    
                    totalEntries++;
                    
                    // 获取 Entry
                    var stockEntry = uiEntry.Target;
                    if (stockEntry == null) continue;
                    
                    // 强制激活（无论原版 Refresh() 是否隐藏了它）
                    // 关键：不检查 entryIndexMapping，直接激活所有条目
                    if (!child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(true);
                        activatedCount++;
                        ModBehaviour.DevLog("[StorageDepositService] 强制激活条目: TypeID=" + stockEntry.ItemTypeID);
                    }
                    
                    // 更新价格显示（使用正确的寄存费用）
                    if (priceTextFieldEntry != null && entryIndexMapping.ContainsKey(stockEntry))
                    {
                        int depositIndex;
                        if (entryIndexMapping.TryGetValue(stockEntry, out depositIndex))
                        {
                            var priceTextComp = priceTextFieldEntry.GetValue(uiEntry) as TMPro.TextMeshProUGUI;
                            if (priceTextComp != null)
                            {
                                var depositedItems = DepositDataManager.GetAllItems();
                                if (depositIndex < depositedItems.Count)
                                {
                                    int fee = depositedItems[depositIndex].GetCurrentFee();
                                    priceTextComp.text = fee.ToString("n0");
                                    priceUpdatedCount++;
                                }
                            }
                        }
                    }
                }
                
                ModBehaviour.DevLog("[StorageDepositService] ForceActivateAllEntries: 总条目=" + totalEntries + 
                    ", 激活=" + activatedCount + ", 价格更新=" + priceUpdatedCount);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] ForceActivateAllEntries 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 刷新商店 UI 并更新所有条目（修复：同类型物品价格显示问题）
        /// 原版 Setup() 使用 GetItemInstanceDirect(TypeID) 获取物品实例，
        /// 同一 TypeID 的所有物品会获取到同一个实例，导致价格显示相同。
        /// 因此必须在 SetupAndShow 后调用 UpdateAllItemDisplays() 更新所有条目的价格。
        /// </summary>
        private static void RefreshShopUIAndUpdateNewEntry(int newIndex)
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || depositShop == null) return;
                
                // 使用反射调用 internal 方法 SetupAndShow
                var setupAndShowMethod = typeof(StockShopView).GetMethod("SetupAndShow",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (setupAndShowMethod != null)
                {
                    setupAndShowMethod.Invoke(shopView, new object[] { depositShop });
                    ModBehaviour.DevLog("[StorageDepositService] 商店 UI 已刷新（新增物品 index=" + newIndex + "）");
                    
                    // 【修复】更新所有条目的 ItemDisplay 和价格，而不是只更新新增的
                    // 原因：SetupAndShow 会重新调用每个条目的 Setup()，
                    // 而 Setup() 使用 GetItemInstanceDirect(TypeID) 获取物品实例，
                    // 同类型物品会获取到同一个实例，导致价格显示为第一个物品的价格
                    UpdateAllItemDisplays();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 刷新商店 UI 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 只更新指定索引的商品条目的 ItemDisplay 和价格显示
        /// </summary>
        private static void UpdateSingleItemDisplay(int targetIndex)
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || depositShop == null) return;
                
                // 获取商店条目的父容器
                var entryTemplateField = typeof(StockShopView).GetField("entryTemplate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (entryTemplateField == null) return;
                
                var entryTemplate = entryTemplateField.GetValue(shopView) as StockShopItemEntry;
                if (entryTemplate == null) return;
                
                Transform entriesParent = entryTemplate.transform.parent;
                if (entriesParent == null) return;
                
                // 获取 StockShopItemEntry 的 itemDisplay 字段
                var itemDisplayField = typeof(StockShopItemEntry).GetField("itemDisplay",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemDisplayField == null) return;
                
                // 获取 priceText 字段（用于更新价格显示）
                var priceTextFieldEntry = typeof(StockShopItemEntry).GetField("priceText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                // 查找对应的条目并更新（通过 entryIndexMapping 匹配）
                foreach (Transform child in entriesParent)
                {
                    // 跳过模板对象
                    if (child == entryTemplate.transform) continue;
                    
                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;
                    
                    var stockEntry = uiEntry.Target;
                    if (stockEntry == null) continue;
                    
                    // 通过 entryIndexMapping 检查是否是目标索引
                    int depositIndex;
                    if (entryIndexMapping.TryGetValue(stockEntry, out depositIndex) && depositIndex == targetIndex)
                    {
                        // 强制激活（如果被隐藏）
                        if (!child.gameObject.activeSelf)
                        {
                            child.gameObject.SetActive(true);
                            ModBehaviour.DevLog("[StorageDepositService] 强制激活新增条目: index=" + depositIndex);
                        }
                        
                        // 从缓存获取正确的物品实例
                        Item correctItem;
                        if (depositItemInstances.TryGetValue(depositIndex, out correctItem) && correctItem != null)
                        {
                            var itemDisplay = itemDisplayField.GetValue(uiEntry) as ItemDisplay;
                            if (itemDisplay != null)
                            {
                                itemDisplay.Setup(correctItem);
                                ModBehaviour.DevLog("[StorageDepositService] 更新新增条目 ItemDisplay: index=" + depositIndex + ", Name=" + correctItem.DisplayName);
                            }
                            
                            // 更新价格显示（使用正确的寄存费用）
                            if (priceTextFieldEntry != null)
                            {
                                var priceTextComp = priceTextFieldEntry.GetValue(uiEntry) as TMPro.TextMeshProUGUI;
                                if (priceTextComp != null)
                                {
                                    var depositedItems = DepositDataManager.GetAllItems();
                                    if (depositIndex < depositedItems.Count)
                                    {
                                        int fee = depositedItems[depositIndex].GetCurrentFee();
                                        priceTextComp.text = fee.ToString("n0");
                                        ModBehaviour.DevLog("[StorageDepositService] 更新新增条目价格: index=" + depositIndex + ", fee=" + fee);
                                    }
                                }
                            }
                        }
                        break; // 找到目标后退出
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 更新单个 ItemDisplay 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 商品购买事件处理（物品取回）
        /// 关键：商店给的是空白物品，需要替换为保存的完整物品（包含配件和容器内容）
        /// 修复：使用 entryIndexMapping 获取正确的索引，而不是通过 TypeID 查找（避免同类型物品冲突）
        /// </summary>
        private static void OnItemPurchased(StockShop shop, Item purchasedItem)
        {
            // 检查是否是我们的商店
            if (shop != depositShop) return;
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[StorageDepositService] 检测到购买事件，物品: " + 
                (purchasedItem != null ? purchasedItem.DisplayName : "null"));
            
            if (purchasedItem == null) return;
            
            try
            {
                // 通过 StockShopView.GetSelection() 获取当前选中的条目，然后通过 entryIndexMapping 获取正确的索引
                // 这样可以正确处理同类型物品（如多张 Boss Rush 船票）
                int depositIndex = -1;
                var shopView = StockShopView.Instance;
                if (shopView != null)
                {
                    var selectedEntry = shopView.GetSelection();
                    if (selectedEntry != null && selectedEntry.Target != null)
                    {
                        // 通过 entryIndexMapping 获取正确的索引
                        if (entryIndexMapping.TryGetValue(selectedEntry.Target, out depositIndex))
                        {
                            ModBehaviour.DevLog("[StorageDepositService] 通过 entryIndexMapping 找到索引: " + depositIndex);
                        }
                    }
                }
                
                // 如果无法通过选中条目获取索引，回退到 TypeID 查找（兼容性）
                if (depositIndex < 0)
                {
                    depositIndex = FindDepositedItemIndex(purchasedItem.TypeID);
                    ModBehaviour.DevLog("[StorageDepositService] 回退到 TypeID 查找，索引: " + depositIndex);
                }
                
                if (depositIndex >= 0)
                {
                    // 获取保存的完整物品数据
                    var depositedItems = DepositDataManager.GetAllItems();
                    var depositedItemData = depositedItems[depositIndex];
                    
                    if (depositedItemData != null && depositedItemData.itemData != null)
                    {
                        // 异步恢复完整物品（包含配件和容器内容）
                        RestoreAndReplaceItemAsync(purchasedItem, depositedItemData.itemData, depositIndex).Forget();
                    }
                    else
                    {
                        // 数据异常，直接移除记录
                        DepositDataManager.RemoveItem(depositIndex);
                        RefreshShopEntries();
                        
                        string msg = L10n.T("物品已取回", "Item retrieved");
                        NotificationText.Push(msg);
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到对应的寄存记录");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 处理购买事件失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 异步恢复完整物品并替换商店给的空白物品
        /// </summary>
        private static async UniTaskVoid RestoreAndReplaceItemAsync(Item emptyItem, ItemTreeData savedData, int depositIndex)
        {
            try
            {
                ModBehaviour.DevLog("[StorageDepositService] 开始恢复完整物品数据...");
                
                // 使用 ItemTreeData.InstantiateAsync 恢复完整物品（包含配件、容器内容等）
                Item restoredItem = await ItemTreeData.InstantiateAsync(savedData);
                
                if (restoredItem == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [ERROR] 恢复物品失败，保留空白物品");
                    DepositDataManager.RemoveItem(depositIndex);
                    RebuildItemInstancesIndex(depositIndex);
                    RefreshShopEntries();
                    // 【关键修复】恢复失败时也需要刷新 UI
                    RefreshShopUI();
                    UpdateRetrieveAllButton();
                    return;
                }
                
                ModBehaviour.DevLog("[StorageDepositService] 物品恢复成功: " + restoredItem.DisplayName);
                
                // 记录空白物品的位置（用于掉落）
                Vector3 emptyItemPosition = emptyItem != null ? emptyItem.transform.position : Vector3.zero;
                
                // 销毁空白物品
                if (emptyItem != null)
                {
                    // 先从背包移除
                    emptyItem.Detach();
                    UnityEngine.Object.Destroy(emptyItem.gameObject);
                    ModBehaviour.DevLog("[StorageDepositService] 已销毁空白物品");
                }
                
                // 将恢复的物品发送给玩家（优先背包，满了放仓库或掉落）
                // 使用 dontMerge=true 避免物品被合并
                ItemUtilities.SendToPlayer(restoredItem, true, true);
                ModBehaviour.DevLog("[StorageDepositService] 已将恢复的物品发送给玩家");
                
                // 从寄存数据移除
                DepositDataManager.RemoveItem(depositIndex);
                
                // 重建物品实例缓存索引（移除后索引会变化）
                RebuildItemInstancesIndex(depositIndex);
                
                // 刷新商店列表
                RefreshShopEntries();
                
                // 【关键修复】购买后必须刷新整个 UI，而不是仅隐藏无效条目
                // 原因：RefreshShopEntries() 会重建 entryIndexMapping，创建新的 Entry 对象
                // 但 UI 中的 StockShopItemEntry.Target 仍然指向旧的 Entry 对象
                // 导致 HideInvalidUIEntries() 中 validEntries.Contains(stockEntry) 全部返回 false
                // 所以必须调用 RefreshShopUI() 让 UI 重新绑定到新的 Entry 对象
                RefreshShopUI();
                
                // 更新"全部取出"按钮
                UpdateRetrieveAllButton();
                
                // 显示通知（使用本地化键）
                string msg = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_Retrieved");
                NotificationText.Push(msg);
                
                ModBehaviour.DevLog("[StorageDepositService] 物品取回完成: " + restoredItem.DisplayName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 恢复物品失败: " + e.Message + "\n" + e.StackTrace);
                
                // 出错时也要移除记录，避免数据不一致
                DepositDataManager.RemoveItem(depositIndex);
                RebuildItemInstancesIndex(depositIndex);
                RefreshShopEntries();
                // 【关键修复】出错时也需要刷新 UI
                RefreshShopUI();
                UpdateRetrieveAllButton();
            }
        }
        
        /// <summary>
        /// 重建物品实例缓存索引（移除物品后调用）
        /// 当移除索引 N 的物品后，所有索引 > N 的物品需要向前移动
        /// </summary>
        private static void RebuildItemInstancesIndex(int removedIndex)
        {
            // 创建新的字典
            var newInstances = new Dictionary<int, Item>();
            
            foreach (var kvp in depositItemInstances)
            {
                int oldIndex = kvp.Key;
                Item item = kvp.Value;
                
                if (oldIndex == removedIndex)
                {
                    // 被移除的物品，销毁实例
                    if (item != null && item.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(item.gameObject);
                    }
                    continue;
                }
                
                // 索引大于被移除的索引，需要减 1
                int newIndex = oldIndex > removedIndex ? oldIndex - 1 : oldIndex;
                newInstances[newIndex] = item;
            }
            
            depositItemInstances = newInstances;
            ModBehaviour.DevLog("[StorageDepositService] 重建物品实例索引完成，剩余 " + depositItemInstances.Count + " 个");
        }
        
        /// <summary>
        /// 查找寄存物品索引（根据 TypeID）
        /// </summary>
        private static int FindDepositedItemIndex(int typeID)
        {
            var items = DepositDataManager.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].itemData != null && items[i].itemData.RootTypeID == typeID)
                {
                    return i;
                }
            }
            return -1;
        }
        
        // ============================================================================
        // 私有方法 - UI 修改
        // ============================================================================
        
        /// <summary>
        /// 一次性修改商店 UI 文字
        /// </summary>
        private static void ModifyShopUITextOnce()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null) return;
                
                // 修改 textSell 字段（设置为本地化键，而不是本地化后的文本）
                // 游戏内部会通过 .ToPlainText() 将键转换为实际文本
                if (textSellField != null)
                {
                    string currentText = textSellField.GetValue(shopView) as string;
                    if (originalTextSell == null)
                    {
                        originalTextSell = currentText;
                    }
                    
                    // 设置为本地化键，游戏会自动调用 ToPlainText() 获取实际文本
                    string depositTextKey = "BossRush_StorageDeposit_Button";
                    textSellField.SetValue(shopView, depositTextKey);
                    ModBehaviour.DevLog("[StorageDepositService] 已修改 textSell 字段为本地化键: " + depositTextKey);
                }
                
                // 创建"全部取出"按钮（替换刷新时间显示）
                CreateRetrieveAllButton(shopView);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 修改 UI 文字失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建操作按钮区域（复用原有的刷新时间显示组件）
        /// 显示格式：全部取出 ￥xxx | 全部丢弃
        /// </summary>
        private static void CreateRetrieveAllButton(StockShopView shopView)
        {
            try
            {
                if (refreshCountDownField == null) return;
                
                // 获取原有的 refreshCountDown 文本组件
                originalRefreshCountDown = refreshCountDownField.GetValue(shopView) as TMPro.TextMeshProUGUI;
                if (originalRefreshCountDown == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 refreshCountDown 组件");
                    return;
                }
                
                // 【关键】隐藏原版的 refreshCountDown 组件
                // 原版 StockShopView.FixedUpdate() 会不断调用 RefreshCountDown() 更新倒计时
                // 我们需要隐藏它，创建新的文本组件来显示我们的内容
                originalRefreshCountDown.gameObject.SetActive(false);
                ModBehaviour.DevLog("[StorageDepositService] 已隐藏原版 refreshCountDown 组件");
                
                // 隐藏"下次刷新"标签（通常是父对象中的兄弟节点）
                Transform parent = originalRefreshCountDown.transform.parent;
                if (parent != null)
                {
                    // 遍历父对象的所有子对象，隐藏包含"刷新"文字的标签
                    foreach (Transform child in parent)
                    {
                        if (child == originalRefreshCountDown.transform) continue;
                        
                        var textComp = child.GetComponent<TMPro.TextMeshProUGUI>();
                        if (textComp != null)
                        {
                            string text = textComp.text;
                            if (text != null && (text.Contains("刷新") || text.Contains("Refresh")))
                            {
                                // 保存被隐藏的标签引用（用于恢复）
                                hiddenRefreshLabel = child.gameObject;
                                child.gameObject.SetActive(false);
                                ModBehaviour.DevLog("[StorageDepositService] 已隐藏'下次刷新'标签: " + text);
                                break;
                            }
                        }
                    }
                    
                    // 创建新的文本组件（克隆原版的样式）
                    GameObject newTextObj = UnityEngine.Object.Instantiate(originalRefreshCountDown.gameObject, parent);
                    newTextObj.name = "DepositOperationButtons";
                    newTextObj.SetActive(true);
                    
                    retrieveAllText = newTextObj.GetComponent<TMPro.TextMeshProUGUI>();
                    if (retrieveAllText != null)
                    {
                        retrieveAllText.richText = true;
                        retrieveAllText.raycastTarget = true;
                        
                        // 添加链接点击处理器（用于处理 TMP 的 <link> 标签点击）
                        var linkHandler = newTextObj.GetComponent<DepositLinkClickHandler>();
                        if (linkHandler == null)
                        {
                            linkHandler = newTextObj.AddComponent<DepositLinkClickHandler>();
                        }
                        
                        // 保存新创建的对象引用（用于清理）
                        retrieveAllButtonObj = newTextObj;
                        
                        // 更新按钮显示
                        UpdateRetrieveAllButton();
                        
                        ModBehaviour.DevLog("[StorageDepositService] 已创建操作按钮区域");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 创建按钮失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 更新操作按钮显示
        /// 格式：全部取出 ￥xxx | 全部丢弃（有物品时）
        /// 格式：空（无物品时）
        /// </summary>
        private static void UpdateRetrieveAllButton()
        {
            if (retrieveAllText == null) return;
            
            try
            {
                int itemCount = DepositDataManager.GetItemCount();
                
                if (itemCount == 0)
                {
                    // 没有物品时显示"空"
                    string emptyText = LocalizationHelper.GetLocalizedText("BossRush_StorageService_Empty");
                    if (string.IsNullOrEmpty(emptyText) || emptyText.StartsWith("BossRush_"))
                    {
                        emptyText = "空";
                    }
                    retrieveAllText.text = emptyText;
                    retrieveAllText.color = Color.gray;
                }
                else
                {
                    // 计算总费用
                    int totalFee = CalculateTotalRetrieveFee();
                    long playerMoney = EconomyManager.Money;
                    bool canAfford = playerMoney >= totalFee;
                    
                    // 获取本地化文本
                    string retrieveAllLabel = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_RetrieveAll");
                    if (string.IsNullOrEmpty(retrieveAllLabel) || retrieveAllLabel.StartsWith("BossRush_"))
                    {
                        retrieveAllLabel = "全部取出";
                    }
                    string discardAllLabel = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_DiscardAll");
                    if (string.IsNullOrEmpty(discardAllLabel) || discardAllLabel.StartsWith("BossRush_"))
                    {
                        discardAllLabel = "全部丢弃";
                    }
                    
                    // 构建显示文本：<link=retrieve>全部取出 ￥xxx</link> | <link=discard>全部丢弃</link>
                    string retrieveColor = canAfford ? "#33CC33" : "#CC3333";  // 绿色或红色
                    string discardColor = "#AA5555";  // 暗红色
                    
                    string displayText = string.Format(
                        "<link=retrieve><color={0}><u>{1} ￥{2}</u></color></link> | <link=discard><color={3}><u>{4}</u></color></link>",
                        retrieveColor,
                        retrieveAllLabel,
                        totalFee.ToString("N0"),
                        discardColor,
                        discardAllLabel
                    );
                    
                    retrieveAllText.text = displayText;
                    retrieveAllText.color = Color.white;  // 基础颜色为白色，实际颜色由富文本控制
                    
                    ModBehaviour.DevLog("[StorageDepositService] 更新操作按钮: 总费用=" + totalFee + ", 物品数=" + itemCount + ", 可支付=" + canAfford);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 更新操作按钮失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 更新"全部丢弃"按钮显示（已合并到 UpdateRetrieveAllButton）
        /// </summary>
        private static void UpdateDiscardAllButton()
        {
            // 已合并到 UpdateRetrieveAllButton，此方法保留为空以兼容现有调用
        }
        
        /// <summary>
        /// "全部丢弃"按钮点击事件
        /// </summary>
        private static void OnDiscardAllClicked()
        {
            if (!isServiceActive) return;
            
            int itemCount = DepositDataManager.GetItemCount();
            if (itemCount == 0)
            {
                ModBehaviour.DevLog("[StorageDepositService] 没有物品可丢弃");
                return;
            }
            
            // 执行全部丢弃
            DiscardAllItems();
        }
        
        /// <summary>
        /// 公共方法：全部丢弃（供 DepositLinkClickHandler 调用）
        /// </summary>
        public static void OnDiscardAllClickedPublic()
        {
            OnDiscardAllClicked();
        }
        
        /// <summary>
        /// 丢弃所有寄存物品（不恢复物品，直接删除数据）
        /// </summary>
        private static void DiscardAllItems()
        {
            try
            {
                int itemCount = DepositDataManager.GetItemCount();
                ModBehaviour.DevLog("[StorageDepositService] 开始全部丢弃，共 " + itemCount + " 件物品");
                
                // 清空寄存数据
                DepositDataManager.ClearAll();
                
                // 清空物品实例缓存
                foreach (var kvp in depositItemInstances)
                {
                    if (kvp.Value != null && kvp.Value.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(kvp.Value.gameObject);
                    }
                }
                depositItemInstances.Clear();
                
                // 刷新商店
                RefreshShopEntries();
                
                // 【关键】隐藏所有缓存的 UI 条目
                // 原版 StockShopView 使用 PrefabPool 缓存 UI 条目，丢弃后这些条目仍然存在
                // 需要手动隐藏它们，而不是依赖 RefreshShopUI（它会错误地激活这些条目）
                HideAllCachedUIEntries();
                
                // 更新按钮显示
                UpdateRetrieveAllButton();
                UpdateDiscardAllButton();
                
                // 显示通知
                string notification = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_Discarded");
                if (string.IsNullOrEmpty(notification) || notification.StartsWith("BossRush_"))
                {
                    notification = "已丢弃所有寄存物品";
                }
                NotificationText.Push(notification);
                
                ModBehaviour.DevLog("[StorageDepositService] 全部丢弃完成，共 " + itemCount + " 件物品");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 全部丢弃失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 隐藏所有缓存的 UI 条目（解决 PrefabPool 缓存问题）
        /// </summary>
        private static void HideAllCachedUIEntries()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null) return;
                
                // 获取商店条目的父容器
                var entryTemplateField = typeof(StockShopView).GetField("entryTemplate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (entryTemplateField == null) return;
                
                var entryTemplate = entryTemplateField.GetValue(shopView) as StockShopItemEntry;
                if (entryTemplate == null) return;
                
                Transform entriesParent = entryTemplate.transform.parent;
                if (entriesParent == null) return;
                
                int hiddenCount = 0;
                foreach (Transform child in entriesParent)
                {
                    // 跳过模板对象
                    if (child == entryTemplate.transform) continue;
                    
                    // 隐藏所有非模板条目
                    if (child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(false);
                        hiddenCount++;
                    }
                }
                
                ModBehaviour.DevLog("[StorageDepositService] 已隐藏 " + hiddenCount + " 个缓存的 UI 条目");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 隐藏缓存 UI 条目失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 隐藏无效的 UI 条目（超出当前寄存物品数量的条目）
        /// 用于购买物品后隐藏已购买的条目，而不是让它变灰
        /// 关键改进：使用 Entry 数量计数，支持同类型物品独立显示
        /// 原理：depositShop.entries 中每个物品都是独立的 Entry，即使 TypeID 相同
        /// </summary>
        private static void HideInvalidUIEntries()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null) return;
                
                // 获取商店条目的父容器
                var entryTemplateField = typeof(StockShopView).GetField("entryTemplate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (entryTemplateField == null) return;
                
                var entryTemplate = entryTemplateField.GetValue(shopView) as StockShopItemEntry;
                if (entryTemplate == null) return;
                
                Transform entriesParent = entryTemplate.transform.parent;
                if (entriesParent == null) return;
                
                // 当前有效的 Entry 数量（每个寄存物品对应一个 Entry）
                int validEntryCount = depositShop != null ? depositShop.entries.Count : 0;
                
                // 构建当前有效的 Entry 集合（用于精确匹配）
                HashSet<StockShop.Entry> validEntries = new HashSet<StockShop.Entry>();
                if (depositShop != null)
                {
                    foreach (var entry in depositShop.entries)
                    {
                        if (entry != null)
                        {
                            validEntries.Add(entry);
                        }
                    }
                }
                
                ModBehaviour.DevLog("[StorageDepositService] HideInvalidUIEntries: 有效 Entry 数量=" + validEntryCount);
                
                int hiddenCount = 0;
                int keptCount = 0;
                
                foreach (Transform child in entriesParent)
                {
                    // 跳过模板对象
                    if (child == entryTemplate.transform) continue;
                    
                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;
                    
                    var stockEntry = uiEntry.Target;
                    
                    // 检查这个 UI 条目的 Entry 是否在当前有效集合中
                    bool isValid = stockEntry != null && validEntries.Contains(stockEntry);
                    
                    if (!isValid && child.gameObject.activeSelf)
                    {
                        // Entry 不在有效集合中，隐藏它
                        child.gameObject.SetActive(false);
                        hiddenCount++;
                        int typeID = stockEntry != null ? stockEntry.ItemTypeID : -1;
                        ModBehaviour.DevLog("[StorageDepositService] 隐藏无效条目: TypeID=" + typeID);
                    }
                    else if (isValid)
                    {
                        keptCount++;
                    }
                }
                
                ModBehaviour.DevLog("[StorageDepositService] HideInvalidUIEntries: 隐藏=" + hiddenCount + ", 保留=" + keptCount);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 隐藏无效 UI 条目失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 丢弃单个寄存物品（已废弃，功能已移除）
        /// </summary>
        public static void DiscardSingleItem(int depositIndex)
        {
            // 功能已移除，保留方法签名以兼容
            ModBehaviour.DevLog("[StorageDepositService] DiscardSingleItem 功能已移除");
        }
        
        /// <summary>
        /// 计算所有寄存物品的总取回费用
        /// </summary>
        private static int CalculateTotalRetrieveFee()
        {
            int totalFee = 0;
            var items = DepositDataManager.GetAllItems();
            
            foreach (var item in items)
            {
                if (item != null)
                {
                    totalFee += item.GetCurrentFee();
                }
            }
            
            return totalFee;
        }
        
        /// <summary>
        /// "全部取出"按钮点击事件
        /// </summary>
        private static void OnRetrieveAllClicked()
        {
            if (!isServiceActive) return;
            
            int itemCount = DepositDataManager.GetItemCount();
            if (itemCount == 0)
            {
                ModBehaviour.DevLog("[StorageDepositService] 没有物品可取出");
                return;
            }
            
            int totalFee = CalculateTotalRetrieveFee();
            long playerMoney = EconomyManager.Money;
            
            if (playerMoney < totalFee)
            {
                // 金钱不足，显示提示
                string msg = LocalizationHelper.GetLocalizedText("BossRush_StorageService_InsufficientFunds");
                NotificationText.Push(msg);
                ModBehaviour.DevLog("[StorageDepositService] 金钱不足，无法全部取出");
                return;
            }
            
            // 执行全部取出
            RetrieveAllItemsAsync(totalFee).Forget();
        }
        
        /// <summary>
        /// 公共方法：全部取出（供 DepositLinkClickHandler 调用）
        /// </summary>
        public static void OnRetrieveAllClickedPublic()
        {
            OnRetrieveAllClicked();
        }
        
        /// <summary>
        /// 异步取出所有物品
        /// </summary>
        private static async UniTaskVoid RetrieveAllItemsAsync(int totalFee)
        {
            try
            {
                ModBehaviour.DevLog("[StorageDepositService] 开始全部取出，总费用: " + totalFee);
                
                // 扣除费用
                if (totalFee > 0)
                {
                    var cost = new Cost(totalFee);
                    if (!cost.Enough)
                    {
                        string msg = LocalizationHelper.GetLocalizedText("BossRush_StorageService_InsufficientFunds");
                        NotificationText.Push(msg);
                        return;
                    }
                    cost.Pay();
                    ModBehaviour.DevLog("[StorageDepositService] 已扣除费用: " + totalFee);
                }
                
                // 获取所有寄存物品（从后往前取，避免索引问题）
                var depositedItems = DepositDataManager.GetAllItems();
                int successCount = 0;
                
                for (int i = depositedItems.Count - 1; i >= 0; i--)
                {
                    var depositedItem = depositedItems[i];
                    if (depositedItem == null || depositedItem.itemData == null) continue;
                    
                    try
                    {
                        // 恢复物品实例
                        Item restoredItem = await ItemTreeData.InstantiateAsync(depositedItem.itemData);
                        
                        if (restoredItem != null)
                        {
                            // 发送给玩家
                            ItemUtilities.SendToPlayer(restoredItem, true, true);
                            successCount++;
                            ModBehaviour.DevLog("[StorageDepositService] 取出物品: " + restoredItem.DisplayName);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 取出物品失败: index=" + i + ", error=" + e.Message);
                    }
                }
                
                // 清空寄存数据
                DepositDataManager.ClearAll();
                
                // 清空物品实例缓存
                depositItemInstances.Clear();
                
                // 刷新商店
                RefreshShopEntries();
                RefreshShopUI();
                
                // 更新按钮显示
                UpdateRetrieveAllButton();
                
                // 显示通知
                string notification = LocalizationHelper.GetLocalizedText("BossRush_StorageService_Retrieved");
                NotificationText.Push(notification);
                
                ModBehaviour.DevLog("[StorageDepositService] 全部取出完成，共 " + successCount + " 件物品");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 全部取出失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 清理"全部取出"和"全部丢弃"按钮（恢复原有组件状态）
        /// </summary>
        private static void CleanupRetrieveAllButton()
        {
            // 销毁"全部取出"按钮
            if (retrieveAllButtonObj != null)
            {
                UnityEngine.Object.Destroy(retrieveAllButtonObj);
                retrieveAllButtonObj = null;
            }
            
            // 销毁"全部丢弃"按钮
            if (discardAllButtonObj != null)
            {
                UnityEngine.Object.Destroy(discardAllButtonObj);
                discardAllButtonObj = null;
            }
            
            // 恢复被隐藏的"下次刷新"标签
            if (hiddenRefreshLabel != null)
            {
                hiddenRefreshLabel.SetActive(true);
                ModBehaviour.DevLog("[StorageDepositService] 已恢复'下次刷新'标签");
                hiddenRefreshLabel = null;
            }
            
            // 恢复原有的 refreshCountDown 组件
            if (originalRefreshCountDown != null)
            {
                originalRefreshCountDown.gameObject.SetActive(true);
                originalRefreshCountDown = null;
            }
            
            retrieveAllButton = null;
            retrieveAllText = null;
            discardAllButton = null;
            discardAllText = null;
        }
        
        /// <summary>
        /// 更新价格显示为 0
        /// </summary>
        private static void UpdatePriceDisplay()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || priceTextField == null) return;
                
                var priceText = priceTextField.GetValue(shopView) as TMPro.TextMeshProUGUI;
                if (priceText != null)
                {
                    priceText.text = "0";
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 恢复原始 UI 文字
        /// </summary>
        private static void RestoreShopUIText()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView != null && textSellField != null && originalTextSell != null)
                {
                    textSellField.SetValue(shopView, originalTextSell);
                    ModBehaviour.DevLog("[StorageDepositService] 已恢复售出按钮文字");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 恢复 UI 文字失败: " + e.Message);
            }
            
            originalTextSell = null;
        }
        
        // ============================================================================
        // 私有方法 - 清理和告别
        // ============================================================================
        
        /// <summary>
        /// 显示告别气泡
        /// </summary>
        private static void ShowGoodbyeBubble(Transform npcTransform)
        {
            if (npcTransform == null) return;
            
            try
            {
                string farewell = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_Farewell");
                
                UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                        farewell, npcTransform, BUBBLE_Y_OFFSET, false, false, -1f, GOODBYE_BUBBLE_DURATION)
                );
                
                ModBehaviour.DevLog("[StorageDepositService] 显示告别气泡: " + farewell);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 显示告别气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        private static void Cleanup()
        {
            // 清理"全部取出"按钮
            CleanupRetrieveAllButton();
            
            // 销毁缓存的物品实例
            foreach (var kvp in depositItemInstances)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value.gameObject);
                }
            }
            depositItemInstances.Clear();
            
            // 销毁商店对象
            if (shopObject != null)
            {
                UnityEngine.Object.Destroy(shopObject);
                shopObject = null;
            }
            
            depositShop = null;
            courierNPCTransform = null;
            courierController = null;
            courierMovement = null;
            entryIndexMapping.Clear();
            pendingDepositItem = null;
            
            ModBehaviour.DevLog("[StorageDepositService] 资源清理完成");
        }
        
        /// <summary>
        /// 播放鸭子叫声（使用反射调用原版 AudioManager）
        /// </summary>
        private static void PlayQuackSound()
        {
            try
            {
                // 获取玩家角色
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    // 使用反射调用 AudioManager.Post（避免 FMOD 程序集依赖）
                    System.Type audioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                    if (audioManagerType != null)
                    {
                        var postMethod = audioManagerType.GetMethod("Post", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                            null,
                            new System.Type[] { typeof(string), typeof(GameObject) },
                            null);
                        if (postMethod != null)
                        {
                            postMethod.Invoke(null, new object[] { "Char/Voice/PlayerQuak", player.gameObject });
                            ModBehaviour.DevLog("[StorageDepositService] 播放鸭子叫声");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 播放叫声失败: " + e.Message);
            }
        }
    }
}

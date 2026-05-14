// ============================================================================
// StorageDepositLifecycle.cs - 寄存服务生命周期与商店初始化
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Cysharp.Threading.Tasks;
using BossRush.Utils;

namespace BossRush
{
    public static partial class StorageDepositService
    {
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

            BindPlayerInventory();

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

                    // 【关键】在 ShowUI 之后注册商店条目选择变化事件
                    // 此时 StockShopView.Instance 已经可用
                    RegisterShopSelectionEvent();

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

            // 在玩家背包侧创建"一键寄存"按钮
            CreateQuickDepositButton();

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

            // 保存引用（Cleanup 会清空）
            var savedController = courierController;
            var savedMovement = courierMovement;
            var savedNPCTransform = courierNPCTransform;

            try
            {
                UnregisterEvents();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 关闭时解绑事件失败: " + e.Message);
            }

            try
            {
                RestoreShopUIText();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 关闭时恢复 UI 文本失败: " + e.Message);
            }

            try
            {
                if (savedController != null)
                {
                    savedController.StopTalking();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 关闭时停止对话失败: " + e.Message);
            }

            try
            {
                if (savedMovement != null)
                {
                    savedMovement.SetInService(false);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 关闭时恢复移动失败: " + e.Message);
            }

            try
            {
                ShowGoodbyeBubble(savedNPCTransform);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 关闭时显示告别气泡失败: " + e.Message);
            }

            try
            {
                Cleanup();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 关闭时清理资源失败: " + e.Message);
            }

            ModBehaviour.DevLog("[StorageDepositService] 寄存服务已关闭");
        }

        public static void CloseServiceIfOwnedBy(Transform npcTransform)
        {
            if (!isServiceActive || !IsServiceOwnedBy(npcTransform))
            {
                return;
            }

            CloseService();
        }

        private static bool IsServiceOwnedBy(Transform npcTransform)
        {
            if (npcTransform == null || courierNPCTransform == null)
            {
                return false;
            }

            return ReferenceEquals(courierNPCTransform, npcTransform) ||
                   courierNPCTransform.IsChildOf(npcTransform) ||
                   npcTransform.IsChildOf(courierNPCTransform);
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

            // 注册背包物品选择变化事件（用于修改价格显示）
            // 注意：此事件仅在玩家选择背包物品时触发，不会在点击商店条目时触发
            ItemUIUtilities.OnSelectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// 注册商店条目选择变化事件（必须在 ShowUI 之后调用，因为 StockShopView.Instance 需要存在）
        /// 【关键修复】原版 StockShopItemEntry 点击时走的是 master.SetSelection(this) → StockShopView.OnSelectionChanged()
        /// 这会触发 StockShopView.onSelectionChanged Action，而不是 ItemUIUtilities.OnSelectionChanged
        /// 所以必须注册到 StockShopView.onSelectionChanged 上才能拦截商店条目的选择变化
        /// </summary>
        private static void RegisterShopSelectionEvent()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] RegisterShopSelectionEvent: StockShopView.Instance 为 null");
                    return;
                }

                // 直接注册到 onSelectionChanged（public Action 字段）
                shopView.onSelectionChanged = (Action)Delegate.Combine(shopView.onSelectionChanged, new Action(OnShopEntrySelectionChanged));
                ModBehaviour.DevLog("[StorageDepositService] 已注册 StockShopView.onSelectionChanged 事件");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 注册商店选择事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消注册商店条目选择变化事件
        /// </summary>
        private static void UnregisterShopSelectionEvent()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView != null)
                {
                    shopView.onSelectionChanged = (Action)Delegate.Remove(shopView.onSelectionChanged, new Action(OnShopEntrySelectionChanged));
                    ModBehaviour.DevLog("[StorageDepositService] 已取消注册 StockShopView.onSelectionChanged 事件");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 取消注册商店选择事件失败: " + e.Message);
            }
        }

        private static void UnregisterEvents()
        {
            // 先取消商店选择事件
            UnregisterShopSelectionEvent();

            StockShop.OnItemSoldByPlayer -= OnItemSoldByPlayer;
            StockShop.OnItemPurchased -= OnItemPurchased;
            ManagedUIElement.onClose -= OnManagedUIElementClose;
            ItemUIUtilities.OnSelectionChanged -= OnSelectionChanged;
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

                // 玩家背包区域和整理按钮（用于创建一键寄存按钮）
                playerInventoryDisplayField = typeof(StockShopView).GetField("playerInventoryDisplay",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                characterInventoryDisplayField = typeof(StockShopView).GetField("characterInventoryDisplay",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                sortButtonField = typeof(Duckov.UI.InventoryDisplay).GetField("sortButton",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                interactionButtonField = typeof(StockShopView).GetField("interactionButton",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                interactionTextField = typeof(StockShopView).GetField("interactionText",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // StockShop.Entry 内部的 entry 字段（StockShopDatabase.ItemEntry 类型）
                stockEntryInnerField = typeof(StockShop.Entry).GetField("entry",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // StockShopDatabase.ItemEntry 的 priceFactor 字段
                if (stockEntryInnerField != null)
                {
                    Type innerEntryType = stockEntryInnerField.FieldType;
                    innerEntryPriceFactorField = innerEntryType.GetField("priceFactor",
                        BindingFlags.Public | BindingFlags.Instance);
                }

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
                            CustomItemRuntimeStateHelper.RestoreRuntimeState(restoredItem, "StorageDeposit.InitializeItemInstances");

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
                            CustomItemRuntimeStateHelper.RestoreRuntimeState(fallbackItem, "StorageDeposit.InitializeFallbackItem");
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

                    // 动态价格因子：寄存费 / 物品实际价值
                    // 【修复】优先使用恢复后物品实例的 GetTotalRawValue()，而不是存入时的 itemValue
                    // 原因：物品有配件等加成时，恢复后的实际价值可能与存入时不同
                    // 而原版 ConvertPrice 用的是 item.GetTotalRawValue() * priceFactor
                    // 所以 priceFactor 必须基于恢复后的实际价值计算，才能让 ConvertPrice 结果 = 正确的寄存费
                    int valueForFactor = item.itemValue;
                    Item cachedItem;
                    if (depositItemInstances.TryGetValue(i, out cachedItem) && cachedItem != null)
                    {
                        int actualValue = cachedItem.GetTotalRawValue();
                        if (actualValue > 0)
                        {
                            valueForFactor = actualValue;
                        }
                    }

                    if (valueForFactor > 0)
                    {
                        itemEntry.priceFactor = IsZombieModeTemporaryCourierPurificationService() ? 0f : (float)fee / valueForFactor;
                    }
                    else
                    {
                        itemEntry.priceFactor = IsZombieModeTemporaryCourierPurificationService() ? 0f : 0.01f;  // 最小价格因子
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
                        ", 费用=" + fee + ", 存入价值=" + item.itemValue + ", 实际价值=" + valueForFactor + ", priceFactor=" + itemEntry.priceFactor);
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
        /// 选择变化事件处理（用于修改价格显示，并修正同类型物品的详情面板显示）
        /// 注意：此方法由 ItemUIUtilities.OnSelectionChanged 触发，仅在玩家选择背包物品时调用
        /// 商店条目的选择变化由 OnShopEntrySelectionChanged 处理
        /// </summary>
        private static void OnSelectionChanged()
        {
            if (!isServiceActive) return;

            ModBehaviour.DevLog("[StorageDepositService] OnSelectionChanged 触发（背包物品选择变化）");

            // 修改价格显示为寄存费（免费存入）
            var selectedDisplay = ItemUIUtilities.SelectedItemDisplay;
            if (selectedDisplay != null && selectedDisplay.Target != null)
            {
                UpdatePriceDisplay();
            }
        }

        /// <summary>
        /// 商店条目选择变化事件处理（由 StockShopView.onSelectionChanged 触发）
        /// 【关键修复】这是点击商店条目时真正触发的事件
        /// 原版流程：StockShopItemEntry.OnPointerClick → master.SetSelection(this) → StockShopView.OnSelectionChanged()
        ///   → 触发 onSelectionChanged Action → 然后调用 details.Setup(item) 和 RefreshInteractionButton()
        ///
        /// 问题：原版 OnSelectionChanged 在触发 onSelectionChanged Action 之后才调用 details.Setup 和 RefreshInteractionButton
        /// 所以我们在这里修正数据后，原版还会继续执行，用修正后的数据来设置详情面板和价格
        ///
        /// 但实际上原版的执行顺序是：
        ///   1. 触发 onSelectionChanged Action（我们的回调在这里执行）
        ///   2. 获取 item = stockShopItemEntry.GetItem()（使用 GetItemInstanceDirect(typeID)）
        ///   3. details.Setup(item)
        ///   4. RefreshInteractionButton()（内部调用 ConvertPrice）
        ///
        /// 所以我们在步骤1中修正 itemInstances[typeID] 和 entries 顺序，
        /// 步骤2-4就会自动使用正确的数据。
        /// </summary>
        private static void OnShopEntrySelectionChanged()
        {
            if (!isServiceActive) return;

            ModBehaviour.DevLog("[StorageDepositService] OnShopEntrySelectionChanged 触发（商店条目选择变化）");

            // 【修复】同类型物品详情面板显示不正确的问题
            SyncSelectedItemInstance();
            UpdateSingleRetrieveUiDeferred();
        }

        /// <summary>
        /// 将当前选中条目对应的正确物品实例同步到原版 itemInstances 字典，
        /// 并将选中的 Entry 移到 entries 列表最前面，确保 ConvertPrice 的 entries.Find 能找到正确的条目。
        ///
        /// 根因分析：
        ///   原版 StockShop 假设每个 TypeID 只有一个 Entry。
        ///   - GetItemInstanceDirect(typeID) 从 itemInstances 字典取物品，同类型只有一个
        ///   - ConvertPrice 中 entries.Find(e => e.ItemTypeID == typeID) 总是返回第一个匹配的 Entry
        ///   当存入多个同类型物品时，详情面板和实际扣费都会用错误的 Entry/物品实例。
        ///
        /// 修复策略（利用原版执行顺序）：
        ///   原版 StockShopView.OnSelectionChanged() 的执行顺序：
        ///     1. 触发 onSelectionChanged Action（我们的回调在这里执行）
        ///     2. item = entry.GetItem() → GetItemInstanceDirect(typeID)
        ///     3. details.Setup(item)
        ///     4. RefreshInteractionButton() → ConvertPrice()
        ///
        ///   我们在步骤1中修正数据，步骤2-4自动使用修正后的数据：
        ///     a. 将选中的 Entry 移到 entries 列表最前面（让 ConvertPrice.Find 找到正确的）
        ///     b. 将正确的物品实例写入 itemInstances[typeID]（让 GetItemInstanceDirect 返回正确的）
        ///   不需要手动调用 RefreshInteractionButton 或 details.Setup，原版会自动执行。
        /// </summary>
        private static void SyncSelectedItemInstance()
        {
            try
            {
                ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance 开始执行");

                if (depositShop == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: depositShop 为 null，退出");
                    return;
                }
                if (itemInstancesField == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: itemInstancesField 为 null，退出");
                    return;
                }

                var shopView = StockShopView.Instance;
                if (shopView == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: shopView 为 null，退出");
                    return;
                }

                // 获取当前选中的条目
                var selectedEntry = shopView.GetSelection();
                if (selectedEntry == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: 无选中条目（取消选择），退出");
                    return;
                }
                if (selectedEntry.Target == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: selectedEntry.Target 为 null，退出");
                    return;
                }

                int typeID = selectedEntry.Target.ItemTypeID;
                ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: 选中条目 TypeID=" + typeID);

                // 通过 entryIndexMapping 获取正确的 depositIndex
                int depositIndex;
                if (!entryIndexMapping.TryGetValue(selectedEntry.Target, out depositIndex))
                {
                    ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: 未找到 Entry 对应的 depositIndex，TypeID=" + typeID);
                    return;
                }

                ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance: depositIndex=" + depositIndex + ", TypeID=" + typeID);

                // 【关键修复1】将选中的 Entry 移到 entries 列表最前面
                // 这样 ConvertPrice 的 entries.Find(e => e.ItemTypeID == typeID) 会优先找到它
                int currentIndex = depositShop.entries.IndexOf(selectedEntry.Target);
                if (currentIndex > 0)
                {
                    depositShop.entries.RemoveAt(currentIndex);
                    depositShop.entries.Insert(0, selectedEntry.Target);
                    ModBehaviour.DevLog("[StorageDepositService] 将选中 Entry 从位置 " + currentIndex + " 移到最前: TypeID=" + typeID);
                }
                else
                {
                    ModBehaviour.DevLog("[StorageDepositService] Entry 已在最前或未找到: currentIndex=" + currentIndex);
                }

                // 【关键修复2】将正确的物品实例写入原版 itemInstances[typeID]
                Item correctItem;
                if (depositItemInstances.TryGetValue(depositIndex, out correctItem) && correctItem != null)
                {
                    var itemInstances = itemInstancesField.GetValue(depositShop) as Dictionary<int, Item>;
                    if (itemInstances != null)
                    {
                        itemInstances[typeID] = correctItem;
                        ModBehaviour.DevLog("[StorageDepositService] 已写入正确物品实例: TypeID=" + typeID +
                            ", Name=" + correctItem.DisplayName + ", Value=" + correctItem.GetTotalRawValue());
                    }

                    // 【关键修复3】动态修正 Entry 的 priceFactor
                    // 根因：RefreshShopEntries 中 priceFactor = fee / itemValue（存入时的价值）
                    // 但 ConvertPrice 计算 = item.GetTotalRawValue() * priceFactor（恢复后的实际价值）
                    // 如果物品有配件等加成，恢复后的 GetTotalRawValue() 可能与存入时的 itemValue 不同
                    // 导致 ConvertPrice 计算出的价格 ≠ 实际寄存费
                    // 修复：用恢复后物品的实际价值重新计算 priceFactor，确保 ConvertPrice 结果 = 正确的寄存费
                    int actualValue = correctItem.GetTotalRawValue();
                    if (actualValue > 0)
                    {
                        var depositedItems = DepositDataManager.GetAllItems();
                        if (depositIndex >= 0 && depositIndex < depositedItems.Count)
                        {
                            int correctFee = depositedItems[depositIndex].GetCurrentFee();
                            float newPriceFactor = IsZombieModeTemporaryCourierPurificationService() ? 0f : (float)correctFee / actualValue;

                            // 通过缓存的反射字段修改 Entry 的 priceFactor
                            if (stockEntryInnerField != null)
                            {
                                var innerEntry = stockEntryInnerField.GetValue(selectedEntry.Target);
                                if (innerEntry != null && innerEntryPriceFactorField != null)
                                {
                                    float oldFactor = (float)innerEntryPriceFactorField.GetValue(innerEntry);
                                    innerEntryPriceFactorField.SetValue(innerEntry, newPriceFactor);
                                    ModBehaviour.DevLog("[StorageDepositService] 修正 priceFactor: " + oldFactor + " -> " + newPriceFactor +
                                        " (fee=" + correctFee + ", actualValue=" + actualValue + ")");
                                }
                            }
                        }
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 depositIndex=" + depositIndex + " 对应的物品实例");
                }

                // 原版 OnSelectionChanged 会在我们之后继续执行：
                //   entry.GetItem() → GetItemInstanceDirect(typeID) → 返回我们刚写入的正确实例
                //   details.Setup(item) → 使用正确的物品
                //   RefreshInteractionButton() → ConvertPrice() → entries.Find 找到我们移到最前的 Entry
                // 所以不需要手动调用 RefreshInteractionButton 或 details.Setup

                ModBehaviour.DevLog("[StorageDepositService] SyncSelectedItemInstance 完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] SyncSelectedItemInstance 失败: " + e.Message + "\n" + e.StackTrace);
            }
        }


        /// <summary>
        /// 物品售出事件处理（拦截原版售出，改为寄存）
        /// </summary>
    }
}

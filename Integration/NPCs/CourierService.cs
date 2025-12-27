// ============================================================================
// CourierService.cs - 快递服务核心逻辑
// ============================================================================
// 模块说明：
//   管理快递员NPC的快递服务功能，包括：
//   - 打开快递容器UI（使用 InteractableLootbox.OnStartLoot 事件）
//   - 计算快递费用（物品总价值的90%）
//   - 发送物品到玩家仓库（复用 PlayerStorage.Push）
//   - 显示告别气泡（复用 DialogueBubblesManager）
// 
// 实现说明：
//   - 使用 InteractableLootbox 组件触发官方 LootView
//   - 通过 OnStartLoot/OnStopLoot 事件驱动，无每帧循环检测
//   - 通过 Inventory.onContentChanged 事件更新按钮状态
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ItemStatsSystem;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// 快递服务核心逻辑（静态类）
    /// </summary>
    public static class CourierService
    {
        // ============================================================================
        // 私有字段
        // ============================================================================
        
        // 快递容器相关
        private static GameObject containerObject;
        private static Inventory courierInventory;
        private static InteractableLootbox courierLootbox;
        
        // NPC引用（用于显示气泡和控制动画）
        private static Transform courierNPCTransform;
        private static CourierNPCController courierController;
        
        // 发送按钮UI
        private static GameObject sendButtonObject;
        private static Button sendButton;
        private static TextMeshProUGUI buttonText;
        
        // 服务状态
        private static bool isServiceActive = false;
        
        /// <summary>
        /// 检查服务是否激活（公共属性）
        /// </summary>
        public static bool IsServiceActive { get { return isServiceActive; } }
        
        // 发送结果（用于显示告别气泡）
        private static int lastSentItemCount = 0;
        private static int lastDeliveryFee = 0;
        
        // 常量
        private const int CONTAINER_CAPACITY = 35;  // 35个格子
        private const float DELIVERY_FEE_RATE = 0.1f;  // 10%快递费
        private const float GOODBYE_BUBBLE_DURATION = 4f;
        private const float BUBBLE_Y_OFFSET = 1.5f;
        
        // 反射缓存
        private static FieldInfo storeAllButtonField = null;
        private static FieldInfo pickAllButtonField = null;
        private static FieldInfo inventoryReferenceField = null;
        private static FieldInfo lootTargetFadeGroupField = null;
        private static FieldInfo lootTargetInventoryDisplayField = null;  // 容器区域的 InventoryDisplay
        private static FieldInfo sortButtonField = null;  // InventoryDisplay 中的整理按钮
        private static FieldInfo lootboxDisplayNameKeyField = null;  // InteractableLootbox 的 displayNameKey 字段
        private static bool reflectionInitialized = false;
        
        // CourierMovement 引用（用于控制移动）
        private static CourierMovement courierMovement;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 打开快递服务（由 CourierInteractable 调用）
        /// </summary>
        public static void OpenService(Transform npcTransform)
        {
            if (isServiceActive)
            {
                ModBehaviour.DevLog("[CourierService] 服务已在运行中，忽略重复调用");
                return;
            }
            
            ModBehaviour.DevLog("[CourierService] 开始打开快递服务...");
            
            // 初始化反射缓存（仅用于按钮）
            InitializeReflection();
            
            // 保存NPC引用
            courierNPCTransform = npcTransform;
            
            // 获取 CourierNPCController 和 CourierMovement 引用
            if (npcTransform != null)
            {
                courierController = npcTransform.GetComponent<CourierNPCController>();
                courierMovement = npcTransform.GetComponent<CourierMovement>();
                
                ModBehaviour.DevLog("[CourierService] courierController: " + (courierController != null ? "找到" : "未找到"));
                ModBehaviour.DevLog("[CourierService] courierMovement: " + (courierMovement != null ? "找到" : "未找到"));
                
                // 开始对话时停止移动并播放对话动画
                // 注意：必须先停止移动，再播放动画
                if (courierMovement != null)
                {
                    courierMovement.SetInService(true);  // 设置服务状态，阻止移动
                    ModBehaviour.DevLog("[CourierService] 已调用 SetInService(true)");
                }
                else
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] courierMovement 为空，无法停止移动！");
                }
                
                if (courierController != null)
                {
                    courierController.StartTalking();  // 播放对话动画
                    ModBehaviour.DevLog("[CourierService] 已调用 StartTalking()");
                }
            }
            else
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] npcTransform 为空！");
            }
            
            // 创建快递容器
            CreateCourierContainer();
            
            // 使用官方方式打开 LootView
            OpenLootViewOfficial();
            
            isServiceActive = true;
            
            // 播放鸭子叫声（打开快递服务时喊一下）
            PlayQuackSound();
            
            ModBehaviour.DevLog("[CourierService] 快递服务已打开");
        }
        
        /// <summary>
        /// 计算快递费用（物品总价值的90%，向上取整）
        /// </summary>
        public static int CalculateDeliveryFee(Inventory container)
        {
            if (container == null) return 0;
            
            int totalValue = 0;
            foreach (Item item in container)
            {
                if (item != null)
                {
                    // 使用 Item.Value 获取物品单价，乘以堆叠数量
                    totalValue += item.Value * item.StackCount;
                }
            }
            
            // 90%快递费，向上取整
            return (int)Math.Ceiling(totalValue * DELIVERY_FEE_RATE);
        }
        
        /// <summary>
        /// 检查玩家是否能支付快递费
        /// </summary>
        public static bool CanAffordDelivery(int fee)
        {
            if (fee <= 0) return false;
            return CanAffordDeliveryInternal(fee);
        }
        
        /// <summary>
        /// 内部资金检查（不检查fee是否>0）
        /// </summary>
        private static bool CanAffordDeliveryInternal(int fee)
        {
            try
            {
                long playerFunds = Duckov.Economy.EconomyManager.Money;
                return playerFunds >= fee;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 获取玩家资金失败: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 执行发送操作
        /// </summary>
        public static void ExecuteDelivery()
        {
            if (courierInventory == null || courierInventory.GetItemCount() == 0)
            {
                ModBehaviour.DevLog("[CourierService] 容器为空，无法发送");
                return;
            }
            
            int fee = CalculateDeliveryFee(courierInventory);
            if (!CanAffordDelivery(fee))
            {
                ModBehaviour.DevLog("[CourierService] 资金不足，无法发送");
                return;
            }
            
            try
            {
                // 使用 EconomyManager 扣除快递费（通过 Cost.Pay 方法）
                var cost = new Duckov.Economy.Cost((long)fee);
                if (!cost.Pay(true, true))
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 扣费失败");
                    return;
                }
                ModBehaviour.DevLog("[CourierService] 已扣除快递费: " + fee);
                
                // 收集所有物品（避免遍历时修改集合）
                List<Item> itemsToSend = new List<Item>();
                foreach (Item item in courierInventory)
                {
                    if (item != null)
                    {
                        itemsToSend.Add(item);
                    }
                }
                
                // 直接操作 PlayerStorageBuffer.Buffer 避免显示单个物品通知
                // 注意：PlayerStorage.IncomingItemBuffer 实际上就是 PlayerStorageBuffer.Buffer
                // 但其他 mod 可能对 PlayerStorage 进行了 patch，所以我们直接使用 PlayerStorageBuffer
                foreach (Item item in itemsToSend)
                {
                    item.Detach();  // 先从容器分离
                    
                    // 直接添加到缓冲区（不显示通知）
                    var itemData = ItemStatsSystem.Data.ItemTreeData.FromItem(item);
                    PlayerStorageBuffer.Buffer.Add(itemData);
                    item.DestroyTree();  // 销毁物品树
                    
                    ModBehaviour.DevLog("[CourierService] 已发送物品: " + item.DisplayName);
                }
                
                // 记录发送结果（用于告别气泡）
                lastSentItemCount = itemsToSend.Count;
                lastDeliveryFee = fee;
                
                ModBehaviour.DevLog("[CourierService] 发送完成，共 " + itemsToSend.Count + " 件物品");
                
                // 显示一次总的大横幅通知（绿色文字）
                ShowDeliveryCompleteBanner();
                
                // 关闭服务（会显示告别气泡）
                CloseService();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 发送失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 显示快递完成的大横幅通知（绿色背景，2秒）
        /// </summary>
        private static void ShowDeliveryCompleteBanner()
        {
            try
            {
                // 获取本地化文本（绿色文字）
                string bannerText = L10n.T(
                    "<color=#00FF00>快递已送达！</color>",
                    "<color=#00FF00>Delivery Complete!</color>"
                );
                
                // 使用游戏的通知系统显示横幅（与 BossRush 其他横幅一致）
                NotificationText.Push(bannerText);
                ModBehaviour.DevLog("[CourierService] 显示快递完成横幅");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 显示横幅失败: " + e.Message);
            }
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
                            ModBehaviour.DevLog("[CourierService] 播放鸭子叫声");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 播放叫声失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 关闭服务并显示告别气泡
        /// </summary>
        public static void CloseService()
        {
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[CourierService] 关闭快递服务...");
            
            // 标记服务已关闭（必须在最前面，防止重复调用）
            isServiceActive = false;
            
            // 保存 NPC 引用（因为 Cleanup 会清空它们）
            var savedController = courierController;
            var savedMovement = courierMovement;
            var savedNPCTransform = courierNPCTransform;
            
            // 停止对话动画（设置 isTalking = false）
            if (savedController != null)
            {
                savedController.StopTalking();
                ModBehaviour.DevLog("[CourierService] 已调用 StopTalking()");
            }
            else
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] courierController 为空，无法停止对话动画");
            }
            
            // 恢复移动（设置 isInService = false）
            if (savedMovement != null)
            {
                savedMovement.SetInService(false);
                ModBehaviour.DevLog("[CourierService] 已调用 SetInService(false)，恢复移动");
            }
            else
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] courierMovement 为空，无法恢复移动");
            }
            
            // 关闭 LootView
            if (LootView.Instance != null && LootView.Instance.open)
            {
                LootView.Instance.Close();
            }
            
            // 返还容器中的物品到玩家背包（如果有关闭时未发送的物品）
            // 注意：如果是从 ExecuteDelivery() 调用的，容器应该已经空了
            ReturnItemsToPlayerInventory();
            
            // 显示告别气泡（使用保存的 NPC Transform）
            ShowGoodbyeBubbleInternal(savedNPCTransform);
            
            // 清理资源
            Cleanup();
            
            ModBehaviour.DevLog("[CourierService] 快递服务已关闭");
        }
        
        // ============================================================================
        // 私有方法 - 反射初始化（仅用于按钮）
        // ============================================================================
        
        /// <summary>
        /// 初始化反射字段缓存（只执行一次）
        /// </summary>
        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            
            try
            {
                BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                
                // LootView 按钮字段（用于复制按钮样式）
                storeAllButtonField = typeof(LootView).GetField("storeAllButton", privateInstance);
                pickAllButtonField = typeof(LootView).GetField("pickAllButton", privateInstance);
                
                // LootView 容器区域字段（用于放置发送按钮）
                lootTargetFadeGroupField = typeof(LootView).GetField("lootTargetFadeGroup", privateInstance);
                
                // LootView 的 lootTargetInventoryDisplay 字段（容器区域的 InventoryDisplay）
                lootTargetInventoryDisplayField = typeof(LootView).GetField("lootTargetInventoryDisplay", privateInstance);
                
                // InventoryDisplay 的 sortButton 字段（整理按钮，用于定位发送按钮）
                sortButtonField = typeof(Duckov.UI.InventoryDisplay).GetField("sortButton", privateInstance);
                
                // InteractableLootbox 的 inventoryReference 字段（用于设置容器引用）
                inventoryReferenceField = typeof(InteractableLootbox).GetField("inventoryReference", privateInstance);
                
                // InteractableLootbox 的 displayNameKey 字段（用于设置容器显示名称）
                lootboxDisplayNameKeyField = typeof(InteractableLootbox).GetField("displayNameKey", privateInstance);
                
                reflectionInitialized = true;
                ModBehaviour.DevLog("[CourierService] 反射字段缓存初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 反射初始化失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 容器创建
        // ============================================================================
        
        /// <summary>
        /// 创建快递容器
        /// </summary>
        private static void CreateCourierContainer()
        {
            // 清理旧容器
            if (containerObject != null)
            {
                UnityEngine.Object.Destroy(containerObject);
            }
            
            // 创建容器对象
            containerObject = new GameObject("CourierServiceContainer");
            
            // 添加 Inventory 组件
            courierInventory = containerObject.AddComponent<Inventory>();
            courierInventory.SetCapacity(CONTAINER_CAPACITY);
            
            // 添加 InteractableLootbox 组件（用于触发官方 LootView）
            courierLootbox = containerObject.AddComponent<InteractableLootbox>();
            
            // 通过反射设置 inventoryReference，使 Lootbox.Inventory 返回我们的容器
            if (inventoryReferenceField != null)
            {
                inventoryReferenceField.SetValue(courierLootbox, courierInventory);
                ModBehaviour.DevLog("[CourierService] 已设置 InteractableLootbox.inventoryReference");
            }
            else
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] inventoryReferenceField 为空，无法设置容器引用");
            }
            
            // 设置容器显示名称（必须在 InteractableLootbox 上设置，因为它会覆盖 Inventory 的 DisplayNameKey）
            SetLootboxDisplayNameKey(courierLootbox, "BossRush_CourierService_ContainerTitle");
            
            // 注册容器内容变化事件（事件驱动，无需每帧检测）
            courierInventory.onContentChanged += OnContainerContentChanged;
            
            // 注册 LootView 关闭事件（通过 InteractableLootbox.OnStopLoot）
            InteractableLootbox.OnStopLoot += OnLootboxStopLoot;
            
            ModBehaviour.DevLog("[CourierService] 快递容器创建成功，容量: " + CONTAINER_CAPACITY);
        }
        
        /// <summary>
        /// 设置 InteractableLootbox 的 displayNameKey 字段（通过反射）
        /// </summary>
        private static void SetLootboxDisplayNameKey(InteractableLootbox lootbox, string key)
        {
            if (lootbox == null || lootboxDisplayNameKeyField == null)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 无法设置 displayNameKey: lootbox=" + (lootbox != null) + ", field=" + (lootboxDisplayNameKeyField != null));
                return;
            }
            
            try
            {
                lootboxDisplayNameKeyField.SetValue(lootbox, key);
                ModBehaviour.DevLog("[CourierService] 已设置 InteractableLootbox.displayNameKey: " + key);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 设置 displayNameKey 失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 打开 LootView（通过触发官方 OnStartLoot 事件）
        // ============================================================================

        // 反射字段缓存
        private static FieldInfo onStartLootField = null;
        
        /// <summary>
        /// 通过触发官方 InteractableLootbox.OnStartLoot 事件打开容器UI
        /// 这是最符合官方规范的方式，确保所有监听者（如 HardwareSyncingManager）都能收到通知
        /// </summary>
        private static void OpenLootViewOfficial()
        {
            try
            {
                if (LootView.Instance == null)
                {
                    ModBehaviour.DevLog("[CourierService] [ERROR] LootView.Instance 为空");
                    return;
                }
                
                if (courierLootbox == null)
                {
                    ModBehaviour.DevLog("[CourierService] [ERROR] courierLootbox 为空");
                    return;
                }
                
                // 验证 Inventory 已正确关联
                if (courierLootbox.Inventory == null)
                {
                    ModBehaviour.DevLog("[CourierService] [ERROR] courierLootbox.Inventory 为空，容器关联失败");
                    return;
                }
                
                // 触发官方 OnStartLoot 事件（让 LootView 自然响应）
                // 这样 HardwareSyncingManager 等其他监听者也能收到通知
                TriggerOnStartLootEvent(courierLootbox);
                
                ModBehaviour.DevLog("[CourierService] 快递容器UI已打开（通过 OnStartLoot 事件）");
                
                // 延迟创建发送按钮（等待 LootView 完全打开）
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.StartCoroutine(CreateSendButtonDelayed());
                    
                    // 启动 LootView 关闭监控（备用方案，防止 OnStopLoot 事件不触发）
                    ModBehaviour.Instance.StartCoroutine(MonitorLootViewClose());
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 打开 LootView 失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 触发 InteractableLootbox.OnStartLoot 静态事件
        /// 官方 StartLoot() 方法内部就是这样触发的：
        ///   Action<InteractableLootbox> onStartLoot = InteractableLootbox.OnStartLoot;
        ///   if (onStartLoot != null) { onStartLoot(this); }
        /// </summary>
        private static void TriggerOnStartLootEvent(InteractableLootbox lootbox)
        {
            try
            {
                // 缓存反射字段（只获取一次）
                // 注意：C# 事件的底层字段是 private static，需要使用 NonPublic | Static
                if (onStartLootField == null)
                {
                    // OnStartLoot 是 public static event，但底层字段是 private
                    onStartLootField = typeof(InteractableLootbox).GetField("OnStartLoot", 
                        BindingFlags.NonPublic | BindingFlags.Static);
                }
                
                if (onStartLootField != null)
                {
                    // 获取事件的委托
                    var onStartLootDelegate = onStartLootField.GetValue(null) as Action<InteractableLootbox>;
                    if (onStartLootDelegate != null)
                    {
                        // 触发事件（调用所有订阅者，包括 LootView 和 HardwareSyncingManager）
                        onStartLootDelegate.Invoke(lootbox);
                        ModBehaviour.DevLog("[CourierService] OnStartLoot 事件已触发");
                        return;
                    }
                    else
                    {
                        ModBehaviour.DevLog("[CourierService] [WARNING] OnStartLoot 事件委托为空（可能没有订阅者）");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 无法获取 OnStartLoot 字段");
                }
                
                // 如果反射获取失败，回退到直接设置 targetLootBox 的方式
                ModBehaviour.DevLog("[CourierService] [WARNING] 无法通过反射触发 OnStartLoot 事件，使用备用方案");
                FallbackOpenLootView(lootbox);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 触发 OnStartLoot 事件失败: " + e.Message + "，使用备用方案");
                FallbackOpenLootView(lootbox);
            }
        }
        
        /// <summary>
        /// 备用方案：直接设置 targetLootBox 字段并打开 LootView
        /// </summary>
        private static void FallbackOpenLootView(InteractableLootbox lootbox)
        {
            try
            {
                var targetLootBoxField = typeof(LootView).GetField("targetLootBox", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (targetLootBoxField != null)
                {
                    targetLootBoxField.SetValue(LootView.Instance, lootbox);
                    LootView.Instance.Open(null);
                    ModBehaviour.DevLog("[CourierService] 使用备用方案打开 LootView");
                }
                else
                {
                    ModBehaviour.DevLog("[CourierService] [ERROR] 备用方案也失败：无法获取 targetLootBox 字段");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 备用方案失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 发送按钮
        // ============================================================================
        
        /// <summary>
        /// 延迟创建发送按钮
        /// </summary>
        private static IEnumerator CreateSendButtonDelayed()
        {
            // 等待 LootView 完全打开
            yield return new WaitForSeconds(0.15f);
            
            // 确保 LootView 已打开
            if (LootView.Instance == null || !LootView.Instance.open)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] LootView 未打开，无法创建发送按钮");
                yield break;
            }
            
            CreateSendButton();
        }
        
        /// <summary>
        /// 创建发送按钮
        /// 定位策略：获取玩家背包整理按钮的相对位置，将发送按钮放到快递容器的相同相对位置
        /// 这样可以确保在不同分辨率下都能正确定位
        /// </summary>
        private static void CreateSendButton()
        {
            if (LootView.Instance == null) return;
            
            try
            {
                // 获取快递容器区域的 InventoryDisplay
                Duckov.UI.InventoryDisplay courierInventoryDisplay = null;
                if (lootTargetInventoryDisplayField != null)
                {
                    courierInventoryDisplay = lootTargetInventoryDisplayField.GetValue(LootView.Instance) as Duckov.UI.InventoryDisplay;
                }
                
                if (courierInventoryDisplay == null)
                {
                    ModBehaviour.DevLog("[CourierService] [ERROR] 无法获取快递容器的 InventoryDisplay");
                    return;
                }
                
                // 获取整理按钮作为模板（从快递容器的 InventoryDisplay 中获取）
                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(courierInventoryDisplay) as Button;
                }
                
                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[CourierService] [ERROR] 无法获取整理按钮");
                    return;
                }
                
                ModBehaviour.DevLog("[CourierService] 找到整理按钮: " + sortButton.name + ", 父级: " + sortButton.transform.parent.name);
                
                // 复制整理按钮作为发送按钮（放在同一个父级下）
                sendButtonObject = UnityEngine.Object.Instantiate(sortButton.gameObject, sortButton.transform.parent);
                sendButtonObject.name = "CourierSendButton";
                sendButtonObject.SetActive(true);
                
                // 使用与整理按钮完全相同的位置
                RectTransform rt = sendButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    // 完全复制整理按钮的位置设置
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition;
                    
                    // 增加宽度以容纳费用文本
                    rt.sizeDelta = new Vector2(sortRt.sizeDelta.x + 100f, sortRt.sizeDelta.y);
                    
                    ModBehaviour.DevLog("[CourierService] 发送按钮 sizeDelta: " + rt.sizeDelta);
                }
                
                // 处理 LayoutElement（如果存在，需要修改它的 preferredWidth）
                var layoutElement = sendButtonObject.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    // 增加 preferredWidth
                    if (layoutElement.preferredWidth > 0)
                    {
                        layoutElement.preferredWidth += 100f;
                        ModBehaviour.DevLog("[CourierService] 修改 LayoutElement.preferredWidth: " + layoutElement.preferredWidth);
                    }
                    if (layoutElement.minWidth > 0)
                    {
                        layoutElement.minWidth += 100f;
                    }
                }
                
                // 禁用 ContentSizeFitter（如果存在，它会覆盖我们的宽度设置）
                var contentSizeFitter = sendButtonObject.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    ModBehaviour.DevLog("[CourierService] 禁用 ContentSizeFitter 水平适配");
                }
                
                // 配置按钮组件
                sendButton = sendButtonObject.GetComponent<Button>();
                if (sendButton != null)
                {
                    sendButton.onClick.RemoveAllListeners();
                    sendButton.onClick.AddListener(OnSendButtonClicked);
                }
                
                // 获取文本组件
                buttonText = sendButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                
                // 更新按钮状态
                UpdateButtonState();
                
                ModBehaviour.DevLog("[CourierService] 发送按钮创建成功（基于整理按钮位置）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 创建发送按钮失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 更新按钮状态（文本、颜色、可交互性）
        /// 通过事件驱动调用，无需每帧检测
        /// </summary>
        private static void UpdateButtonState()
        {
            if (sendButton == null) return;
            
            bool hasItems = courierInventory != null && courierInventory.GetItemCount() > 0;
            int fee = hasItems ? CalculateDeliveryFee(courierInventory) : 0;
            bool canAfford = hasItems && fee > 0 && CanAffordDeliveryInternal(fee);
            
            // 获取本地化文本（从本地化系统获取）
            string sendText = LocalizationHelper.GetLocalizedText("BossRush_CourierService_Send");
            string emptyText = LocalizationHelper.GetLocalizedText("BossRush_CourierService_Empty");
            
            // 更新按钮文本和状态
            string displayText;
            bool interactable;
            Color buttonColor;
            
            if (!hasItems)
            {
                displayText = emptyText;
                interactable = false;
                buttonColor = Color.gray;
            }
            else if (!canAfford)
            {
                // 资金不足时显示红色金额
                displayText = sendText + " (<color=#FF0000>￥" + fee + "</color>)";
                interactable = false;
                buttonColor = Color.gray;
            }
            else
            {
                // 可以支付时显示绿色金额
                displayText = sendText + " (￥" + fee + ")";
                interactable = true;
                buttonColor = new Color(0.2f, 0.8f, 0.2f);
            }
            
            // 应用文本
            if (buttonText != null)
            {
                buttonText.text = displayText;
                buttonText.richText = true;  // 启用富文本支持
            }
            else if (sendButtonObject != null)
            {
                var legacyText = sendButtonObject.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = displayText;
                    legacyText.supportRichText = true;
                }
            }
            
            // 应用可交互性和颜色
            sendButton.interactable = interactable;
            var colors = sendButton.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.1f;
            colors.pressedColor = buttonColor * 0.9f;
            colors.disabledColor = Color.gray;
            sendButton.colors = colors;
        }
        
        /// <summary>
        /// 发送按钮点击事件
        /// </summary>
        private static void OnSendButtonClicked()
        {
            ModBehaviour.DevLog("[CourierService] 发送按钮被点击");
            ExecuteDelivery();
        }
        
        /// <summary>
        /// 容器内容变化事件（事件驱动，无需每帧检测）
        /// </summary>
        private static void OnContainerContentChanged(Inventory inventory, int index)
        {
            // 立即更新按钮状态
            UpdateButtonState();
        }
        
        /// <summary>
        /// InteractableLootbox.OnStopLoot 事件处理（官方事件驱动）
        /// </summary>
        private static void OnLootboxStopLoot(InteractableLootbox lootbox)
        {
            // 只处理我们自己的 lootbox
            if (lootbox != courierLootbox) return;
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[CourierService] 收到 OnStopLoot 事件");
            OnLootViewClosed();
        }
        
        /// <summary>
        /// 监控 LootView 关闭状态（备用方案）
        /// 因为我们没有通过正常交互流程打开 lootbox，OnStopLoot 可能不会触发
        /// 所以需要一个备用的关闭检测机制
        /// </summary>
        private static IEnumerator MonitorLootViewClose()
        {
            // 等待 LootView 打开
            yield return new WaitForSeconds(0.2f);
            
            // 持续检测 LootView 是否关闭（每0.5秒检测一次，非每帧）
            while (isServiceActive)
            {
                // 如果 LootView 已关闭，触发关闭处理
                if (LootView.Instance == null || !LootView.Instance.open)
                {
                    ModBehaviour.DevLog("[CourierService] 检测到 LootView 已关闭（通过监控协程）");
                    OnLootViewClosed();
                    yield break;
                }
                
                // 每0.5秒检测一次，节省资源
                yield return new WaitForSeconds(0.5f);
            }
        }

        
        // ============================================================================
        // 私有方法 - LootView 关闭处理
        // ============================================================================
        
        /// <summary>
        /// 尝试自动发送物品到玩家仓库（关闭UI时调用）
        /// 当玩家关闭UI但未点击发送按钮时，如果资金充足则自动扣费发送
        /// </summary>
        /// <returns>true 如果成功自动发送，false 如果需要返还到背包</returns>
        private static bool TryAutoDelivery()
        {
            // 检查容器是否有物品
            if (courierInventory == null || courierInventory.GetItemCount() == 0)
            {
                ModBehaviour.DevLog("[CourierService] TryAutoDelivery: 容器为空，无需自动发送");
                return false;
            }
            
            // 计算快递费用
            int fee = CalculateDeliveryFee(courierInventory);
            if (fee <= 0)
            {
                ModBehaviour.DevLog("[CourierService] TryAutoDelivery: 快递费用为0，无需自动发送");
                return false;
            }
            
            // 检查玩家资金是否充足
            if (!CanAffordDeliveryInternal(fee))
            {
                ModBehaviour.DevLog("[CourierService] TryAutoDelivery: 资金不足，无法自动发送");
                return false;
            }
            
            // 执行自动发送
            return ExecuteAutoDelivery(fee);
        }
        
        /// <summary>
        /// 执行自动发送操作（复用 ExecuteDelivery 的核心逻辑）
        /// </summary>
        /// <param name="fee">已计算好的快递费用</param>
        /// <returns>true 如果发送成功</returns>
        private static bool ExecuteAutoDelivery(int fee)
        {
            try
            {
                // 使用 EconomyManager 扣除快递费（通过 Cost.Pay 方法）
                var cost = new Duckov.Economy.Cost((long)fee);
                if (!cost.Pay(true, true))
                {
                    ModBehaviour.DevLog("[CourierService] ExecuteAutoDelivery: 扣费失败");
                    return false;
                }
                ModBehaviour.DevLog("[CourierService] ExecuteAutoDelivery: 已扣除快递费: " + fee);
                
                // 收集所有物品（避免遍历时修改集合）
                List<Item> itemsToSend = new List<Item>();
                foreach (Item item in courierInventory)
                {
                    if (item != null)
                    {
                        itemsToSend.Add(item);
                    }
                }
                
                // 发送物品到玩家仓库
                foreach (Item item in itemsToSend)
                {
                    item.Detach();  // 先从容器分离
                    
                    // 直接添加到缓冲区（不显示通知）
                    var itemData = ItemStatsSystem.Data.ItemTreeData.FromItem(item);
                    PlayerStorageBuffer.Buffer.Add(itemData);
                    item.DestroyTree();  // 销毁物品树
                    
                    ModBehaviour.DevLog("[CourierService] ExecuteAutoDelivery: 已发送物品: " + item.DisplayName);
                }
                
                // 记录发送结果（用于告别气泡）
                lastSentItemCount = itemsToSend.Count;
                lastDeliveryFee = fee;
                
                ModBehaviour.DevLog("[CourierService] ExecuteAutoDelivery: 自动发送完成，共 " + itemsToSend.Count + " 件物品");
                
                // 显示快递完成横幅
                ShowDeliveryCompleteBanner();
                
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] ExecuteAutoDelivery 失败: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// 显示自动发送的告别气泡
        /// </summary>
        private static void ShowAutoDeliveryBubble(Transform npcTransform)
        {
            if (npcTransform == null)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] NPC Transform 为空，无法显示自动发送气泡");
                return;
            }
            
            try
            {
                // 显示新的告别气泡："货我已经送出去了，钱我就收下了"
                string bubbleText = L10n.T(
                    "货我已经送出去了，钱我就收下了",
                    "I've sent the goods, I'll take the money"
                );
                
                // 使用原版气泡系统显示对话
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                        bubbleText,
                        npcTransform,
                        BUBBLE_Y_OFFSET,
                        false,
                        false,
                        -1f,
                        GOODBYE_BUBBLE_DURATION
                    )
                );
                
                ModBehaviour.DevLog("[CourierService] 显示自动发送告别气泡: " + bubbleText);
                
                // 重置发送记录（防止下次打开时显示旧数据）
                lastSentItemCount = 0;
                lastDeliveryFee = 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 显示自动发送气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// LootView 关闭事件处理
        /// </summary>
        private static void OnLootViewClosed()
        {
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[CourierService] LootView 被关闭");
            
            // 标记服务已关闭（必须在最前面，防止重复调用）
            isServiceActive = false;
            
            // 保存 NPC 引用（因为 Cleanup 会清空它们）
            var savedController = courierController;
            var savedMovement = courierMovement;
            var savedNPCTransform = courierNPCTransform;
            
            // 停止对话动画（设置 isTalking = false）
            if (savedController != null)
            {
                savedController.StopTalking();
                ModBehaviour.DevLog("[CourierService] OnLootViewClosed: 已调用 StopTalking()");
            }
            
            // 恢复移动（设置 isInService = false）
            if (savedMovement != null)
            {
                savedMovement.SetInService(false);
                ModBehaviour.DevLog("[CourierService] OnLootViewClosed: 已调用 SetInService(false)，恢复移动");
            }
            
            // 新增：尝试自动发送（如果容器有物品且资金充足）
            bool autoDelivered = TryAutoDelivery();
            
            if (autoDelivered)
            {
                // 自动发送成功，显示新的告别气泡
                ShowAutoDeliveryBubble(savedNPCTransform);
            }
            else
            {
                // 自动发送失败（资金不足或容器为空），返还物品到玩家背包
                ReturnItemsToPlayerInventory();
                // 显示原有告别气泡
                ShowGoodbyeBubbleInternal(savedNPCTransform);
            }
            
            // 清理资源
            Cleanup();
        }
        
        /// <summary>
        /// 返还容器中的物品到玩家背包（当玩家关闭UI但未发送物品时）
        /// </summary>
        private static void ReturnItemsToPlayerInventory()
        {
            if (courierInventory == null)
            {
                ModBehaviour.DevLog("[CourierService] 容器为空，无需返还物品");
                return;
            }
            
            // 检查容器中是否还有物品
            int itemCount = courierInventory.GetItemCount();
            if (itemCount == 0)
            {
                ModBehaviour.DevLog("[CourierService] 容器中没有物品，无需返还");
                return;
            }
            
            ModBehaviour.DevLog("[CourierService] 检测到容器中有 " + itemCount + " 件物品未发送，开始返还到玩家背包...");
            
            try
            {
                // 获取玩家角色和背包
                CharacterMainControl player = null;
                try
                {
                    player = CharacterMainControl.Main;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 获取玩家角色失败: " + e.Message);
                }
                
                if (player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null)
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 无法获取玩家背包，尝试丢到地上");
                    // 如果无法获取玩家背包，尝试丢到地上
                    DropItemsToGround(player);
                    return;
                }
                
                Inventory playerInventory = player.CharacterItem.Inventory;
                
                // 收集所有需要返还的物品（避免遍历时修改集合）
                List<Item> itemsToReturn = new List<Item>();
                foreach (Item item in courierInventory)
                {
                    if (item != null)
                    {
                        itemsToReturn.Add(item);
                    }
                }
                
                int returnedCount = 0;
                int droppedCount = 0;
                
                // 尝试将物品添加到玩家背包
                foreach (Item item in itemsToReturn)
                {
                    if (item == null) continue;
                    
                    // 先从容器分离
                    item.Detach();
                    
                    // 尝试添加到玩家背包
                    bool added = playerInventory.AddAndMerge(item, 0);
                    if (added)
                    {
                        returnedCount++;
                        ModBehaviour.DevLog("[CourierService] 已返还物品到背包: " + item.DisplayName);
                    }
                    else
                    {
                        // 背包满了，丢到地上（在玩家位置）
                        try
                        {
                            item.Drop(player, true);
                            droppedCount++;
                            ModBehaviour.DevLog("[CourierService] 背包已满，已丢到地上: " + item.DisplayName);
                        }
                        catch (Exception e)
                        {
                            ModBehaviour.DevLog("[CourierService] [WARNING] 丢物品到地上失败: " + e.Message);
                            // 如果丢到地上失败，销毁物品（这种情况应该很少见）
                            item.DestroyTree();
                        }
                    }
                }
                
                ModBehaviour.DevLog("[CourierService] 物品返还完成 - 背包: " + returnedCount + " 件, 地上: " + droppedCount + " 件");
                
                // 如果有物品被返还，显示通知
                if (returnedCount > 0 || droppedCount > 0)
                {
                    string message;
                    if (droppedCount > 0)
                    {
                        message = L10n.T(
                            "已返还 " + returnedCount + " 件物品到背包，" + droppedCount + " 件物品已丢到地上",
                            "Returned " + returnedCount + " items to inventory, " + droppedCount + " items dropped on ground"
                        );
                    }
                    else
                    {
                        message = L10n.T(
                            "已返还 " + returnedCount + " 件物品到背包",
                            "Returned " + returnedCount + " items to inventory"
                        );
                    }
                    NotificationText.Push(message);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 返还物品失败: " + e.Message + "\n" + e.StackTrace);
                // 如果返还失败，尝试丢到地上作为备用方案
                CharacterMainControl player = null;
                try
                {
                    player = CharacterMainControl.Main;
                }
                catch { }
                DropItemsToGround(player);
            }
        }
        
        /// <summary>
        /// 将容器中的所有物品丢到地上（备用方案，当无法获取玩家背包时）
        /// </summary>
        private static void DropItemsToGround(CharacterMainControl player)
        {
            if (courierInventory == null) return;
            
            try
            {
                List<Item> itemsToDrop = new List<Item>();
                foreach (Item item in courierInventory)
                {
                    if (item != null)
                    {
                        itemsToDrop.Add(item);
                    }
                }
                
                if (itemsToDrop.Count == 0) return;
                
                int droppedCount = 0;
                
                foreach (Item item in itemsToDrop)
                {
                    if (item == null) continue;
                    
                    // 先从容器分离
                    item.Detach();
                    
                    // 尝试丢到地上
                    try
                    {
                        if (player != null)
                        {
                            // 在玩家位置丢下
                            item.Drop(player, true);
                        }
                        else
                        {
                            // 如果无法获取玩家，尝试获取玩家位置
                            CharacterMainControl mainPlayer = null;
                            try
                            {
                                mainPlayer = CharacterMainControl.Main;
                            }
                            catch { }
                            
                            if (mainPlayer != null)
                            {
                                item.Drop(mainPlayer, true);
                            }
                            else
                            {
                                // 如果还是无法获取玩家，销毁物品
                                item.DestroyTree();
                                ModBehaviour.DevLog("[CourierService] [WARNING] 无法获取玩家位置，销毁物品: " + item.DisplayName);
                                continue;
                            }
                        }
                        
                        droppedCount++;
                        ModBehaviour.DevLog("[CourierService] 已丢到地上: " + item.DisplayName);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[CourierService] [WARNING] 丢物品到地上失败: " + e.Message);
                        // 如果丢到地上失败，销毁物品
                        item.DestroyTree();
                    }
                }
                
                if (droppedCount > 0)
                {
                    ModBehaviour.DevLog("[CourierService] 已丢 " + droppedCount + " 件物品到地上");
                    string message = L10n.T(
                        "已丢 " + droppedCount + " 件物品到地上",
                        "Dropped " + droppedCount + " items on ground"
                    );
                    NotificationText.Push(message);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 丢物品到地上失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 气泡显示
        // ============================================================================
        
        /// <summary>
        /// 显示告别气泡
        /// </summary>
        private static void ShowGoodbyeBubble()
        {
            ShowGoodbyeBubbleInternal(courierNPCTransform);
        }
        
        /// <summary>
        /// 显示告别气泡（内部方法，接受 Transform 参数）
        /// </summary>
        private static void ShowGoodbyeBubbleInternal(Transform npcTransform)
        {
            if (npcTransform == null)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] NPC Transform 为空，无法显示气泡");
                return;
            }
            
            try
            {
                // 如果有发送记录，显示气泡
                if (lastSentItemCount > 0)
                {
                    // 格式：已送达x件物品，共花费x元，欢迎下次光临~
                    // x 用红色显示
                    string goodbyeText = L10n.T(
                        "已送达<color=#FF0000>" + lastSentItemCount + "</color>件物品，共花费<color=#FF0000>￥" + lastDeliveryFee + "</color>，欢迎下次光临~",
                        "Delivered <color=#FF0000>" + lastSentItemCount + "</color> items, cost <color=#FF0000>￥" + lastDeliveryFee + "</color>, come again~"
                    );
                    
                    // 使用原版气泡系统显示对话
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                        Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                            goodbyeText,
                            npcTransform,
                            BUBBLE_Y_OFFSET,
                            false,
                            false,
                            -1f,
                            GOODBYE_BUBBLE_DURATION
                        )
                    );
                    
                    ModBehaviour.DevLog("[CourierService] 显示告别气泡: " + goodbyeText);
                    
                    // 重置发送记录
                    lastSentItemCount = 0;
                    lastDeliveryFee = 0;
                }
                else
                {
                    // 没有发送物品时，使用气泡显示"穷小子，没钱还来浪费爷的时间"
                    string bubbleText = L10n.T(
                        "穷小子，没钱还来浪费爷的时间",
                        "Poor kid, wasting my time without money"
                    );
                    
                    // 使用原版气泡系统显示对话
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                        Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                            bubbleText,
                            npcTransform,
                            BUBBLE_Y_OFFSET,
                            false,
                            false,
                            -1f,
                            GOODBYE_BUBBLE_DURATION
                        )
                    );
                    
                    ModBehaviour.DevLog("[CourierService] 显示告别气泡: " + bubbleText);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 显示告别消息失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 资源清理
        // ============================================================================
        
        /// <summary>
        /// 清理资源
        /// </summary>
        private static void Cleanup()
        {
            ModBehaviour.DevLog("[CourierService] 开始清理资源...");
            
            // 取消事件订阅
            if (courierInventory != null)
            {
                courierInventory.onContentChanged -= OnContainerContentChanged;
            }
            
            // 取消 OnStopLoot 事件订阅
            InteractableLootbox.OnStopLoot -= OnLootboxStopLoot;
            
            // 销毁发送按钮
            if (sendButtonObject != null)
            {
                UnityEngine.Object.Destroy(sendButtonObject);
                sendButtonObject = null;
                sendButton = null;
                buttonText = null;
            }
            
            // 销毁容器对象
            if (containerObject != null)
            {
                UnityEngine.Object.Destroy(containerObject);
                containerObject = null;
                courierInventory = null;
                courierLootbox = null;
            }
            
            // 清空 NPC 引用
            courierNPCTransform = null;
            courierController = null;
            courierMovement = null;
            
            ModBehaviour.DevLog("[CourierService] 资源清理完成");
        }
    }
}

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
using Duckov.Utilities;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 快递服务核心逻辑（静态类）
    /// </summary>
    public static partial class CourierService
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

        // 玩家背包侧的"一键存入"按钮
        private static GameObject quickStoreButtonObject;
        private static Button quickStoreButton;
        private static TextMeshProUGUI quickStoreButtonText;
        private static Inventory playerInventory;

        // 服务状态
        private static bool isServiceActive = false;

        /// <summary>
        /// 检查服务是否激活（公共属性）
        /// </summary>
        public static bool IsServiceActive { get { return isServiceActive; } }

        /// <summary>
        /// 不打开快递 UI，直接把物品快递回家。
        /// 用于一键清包类快捷功能。
        /// </summary>
        public static int QuickDeliverItems(IEnumerable<Item> items, string bannerText = null, bool showBanner = true)
        {
            if (items == null)
            {
                return 0;
            }

            int sentCount = 0;

            try
            {
                foreach (Item item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    string itemName = item.DisplayName;

                    try
                    {
                        item.Detach();
                        ReforgeDataPersistence.SyncCurrentReforgeState(item);
                        PlayerStorage.Push(item, true);
                        sentCount++;
                        ModBehaviour.DevLog("[CourierService] QuickDeliverItems: 已发送物品到快递站: " + itemName);
                    }
                    catch (Exception pushEx)
                    {
                        ModBehaviour.DevLog("[CourierService] QuickDeliverItems: 发送失败: " + itemName + ", " + pushEx.Message);

                        try
                        {
                            ItemUtilities.SendToPlayer(item, true, true);
                        }
                        catch (Exception restoreEx)
                        {
                            ModBehaviour.DevLog("[CourierService] QuickDeliverItems: 回退物品失败: " + itemName + ", " + restoreEx.Message);
                        }
                    }
                }

                if (sentCount <= 0)
                {
                    return 0;
                }

                ClearNotificationQueue();
                lastSentItemCount = sentCount;
                lastDeliveryFee = 0;
                PlayerStorageBuffer.SaveBuffer();
                if (showBanner)
                {
                    ShowDeliveryCompleteBanner(bannerText);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] QuickDeliverItems 失败: " + e.Message + "\n" + e.StackTrace);
            }

            return sentCount;
        }

        public static bool TryCreateTransientLootbox(
            string objectName,
            int capacity,
            string displayNameKey,
            out GameObject transientObject,
            out Inventory transientInventory,
            out InteractableLootbox transientLootbox)
        {
            transientObject = null;
            transientInventory = null;
            transientLootbox = null;

            try
            {
                InitializeReflection();

                transientObject = new GameObject(string.IsNullOrEmpty(objectName) ? "TransientLootboxContainer" : objectName);
                transientObject.SetActive(false);

                transientInventory = transientObject.AddComponent<Inventory>();
                transientInventory.SetCapacity(Mathf.Max(1, capacity));

                BoxCollider boxCollider = transientObject.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = new Vector3(0.1f, 0.1f, 0.1f);
                boxCollider.enabled = false;

                transientLootbox = transientObject.AddComponent<InteractableLootbox>();
                NPCInteractionGroupHelper.GetOrCreateGroupList(transientLootbox, "[CourierServiceTransient]");
                transientLootbox.enabled = false;

                if (inventoryReferenceField != null)
                {
                    inventoryReferenceField.SetValue(transientLootbox, transientInventory);
                }

                SetLootboxDisplayNameKey(transientLootbox, displayNameKey);
                transientObject.SetActive(true);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 创建临时 Lootbox 失败: " + e.Message);

                if (transientObject != null)
                {
                    UnityEngine.Object.Destroy(transientObject);
                }

                transientObject = null;
                transientInventory = null;
                transientLootbox = null;
                return false;
            }
        }

        public static bool TryOpenTransientLootbox(InteractableLootbox lootbox)
        {
            try
            {
                InitializeReflection();
                if (LootView.Instance == null || lootbox == null || lootbox.Inventory == null)
                {
                    return false;
                }

                TriggerOnStartLootEvent(lootbox);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [ERROR] 打开临时 Lootbox 失败: " + e.Message);
                return false;
            }
        }

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
        private static FieldInfo playerInventoryDisplayField = null;      // 玩家背包区域的 InventoryDisplay
        private static FieldInfo characterInventoryDisplayField = null;   // 旧版本/其他布局的玩家背包区域
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

            BindPlayerInventory();

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
        /// 计算快递费用（物品总价值的10%，向上取整）
        /// </summary>
        public static int CalculateDeliveryFee(Inventory container)
        {
            if (container == null) return 0;

            return CalculateDeliveryFee((IEnumerable<Item>)container);
        }

        /// <summary>
        /// 计算快递费用（物品总价值的10%，向上取整）。
        /// 用于快捷快递等不经过快递 UI 的场景。
        /// </summary>
        public static int CalculateDeliveryFee(IEnumerable<Item> items)
        {
            if (items == null) return 0;

            int totalValue = 0;
            foreach (Item item in items)
            {
                if (item == null)
                {
                    continue;
                }

                totalValue += item.Value * item.StackCount;
            }

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
        /// 扣除快递费。fee 小于等于 0 时视为无需扣费。
        /// </summary>
        public static bool TryPayDeliveryFee(int fee)
        {
            if (fee <= 0)
            {
                return true;
            }

            try
            {
                if (courierNPCTransform != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
                {
                    return ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                        courierNPCTransform,
                        fee,
                        "ZombieModeTempCourierDelivery");
                }

                var cost = new Duckov.Economy.Cost((long)fee);
                if (!cost.Pay(true, true))
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 扣费失败: " + fee);
                    return false;
                }

                ModBehaviour.DevLog("[CourierService] 已扣除快递费: " + fee);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 扣费异常: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 内部资金检查（不检查fee是否>0）
        /// </summary>
        private static bool CanAffordDeliveryInternal(int fee)
        {
            try
            {
                if (IsZombieModeTemporaryCourierPurificationService(courierNPCTransform))
                {
                    return ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(courierNPCTransform, fee);
                }

                // 与实际扣费保持同一口径：同时检查账户余额与身上现金。
                return Duckov.Economy.EconomyManager.IsEnough(
                    new Duckov.Economy.Cost((long)fee),
                    true,
                    true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 获取玩家资金失败: " + e.Message);
                return false;
            }
        }

        private static bool IsZombieModeTemporaryCourierPurificationService(Transform npcTransform)
        {
            return npcTransform != null &&
                   ModBehaviour.Instance != null &&
                   ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(npcTransform);
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
                if (!TryPayDeliveryFee(fee))
                {
                    return;
                }

                // 收集所有物品（避免遍历时修改集合）
                List<Item> itemsToSend = new List<Item>();
                foreach (Item item in courierInventory)
                {
                    if (item != null)
                    {
                        itemsToSend.Add(item);
                    }
                }

                // 使用原版 PlayerStorage.Push() 方法发送物品到快递站（Buffer）
                foreach (Item item in itemsToSend)
                {
                    string itemName = item.DisplayName;  // 先保存名称，因为 Push 后 item 会被销毁
                    item.Detach();  // 先从容器分离
                    ReforgeDataPersistence.SyncCurrentReforgeState(item);

                    // 使用原版 PlayerStorage.Push() 方法
                    // 参数 toBufferDirectly=true 表示直接放入快递站（Buffer），不尝试放入仓库
                    PlayerStorage.Push(item, true);

                    ModBehaviour.DevLog("[CourierService] 已发送物品到快递站: " + itemName);
                }

                // 清空原版 PlayerStorage.Push 产生的通知队列，避免每个物品都播报一次
                // 我们会在后面显示一次汇总通知
                ClearNotificationQueue();

                // 记录发送结果（用于告别气泡）
                lastSentItemCount = itemsToSend.Count;
                lastDeliveryFee = fee;

                ModBehaviour.DevLog("[CourierService] 发送完成，共 " + itemsToSend.Count + " 件物品");

                // 立即保存快递数据
                PlayerStorageBuffer.SaveBuffer();
                ModBehaviour.DevLog("[CourierService] 已保存快递数据到存档");

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
        private static void ShowDeliveryCompleteBanner(string customBannerText = null)
        {
            try
            {
                // 获取本地化文本（绿色文字），显示发送的物品数量
                string bannerText = customBannerText;
                if (string.IsNullOrEmpty(bannerText))
                {
                    bannerText = L10n.T(
                        "<color=#00FF00>" + lastSentItemCount + "件快递已送达！</color>",
                        "<color=#00FF00>" + lastSentItemCount + " items delivered!</color>"
                    );
                }

                // 使用游戏的通知系统显示横幅
                NotificationText.Push(bannerText);
                ModBehaviour.DevLog("[CourierService] 显示快递完成横幅: " + lastSentItemCount + " 件物品");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 显示横幅失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清空通知队列（通过反射访问 NotificationText.pendingTexts 静态字段）
        /// 用于禁止 PlayerStorage.Push 产生的逐个物品通知
        /// </summary>
        private static void ClearNotificationQueue()
        {
            try
            {
                // 获取 NotificationText 类的 pendingTexts 静态字段
                var pendingTextsField = typeof(NotificationText).GetField("pendingTexts",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (pendingTextsField != null)
                {
                    var queue = pendingTextsField.GetValue(null) as System.Collections.Generic.Queue<string>;
                    if (queue != null)
                    {
                        int clearedCount = queue.Count;
                        queue.Clear();
                        ModBehaviour.DevLog("[CourierService] 已清空通知队列，清除了 " + clearedCount + " 条通知");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 无法获取 pendingTexts 字段");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 清空通知队列失败: " + e.Message);
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

                // LootView 的玩家背包区域字段
                playerInventoryDisplayField = typeof(LootView).GetField("playerInventoryDisplay", privateInstance);
                characterInventoryDisplayField = typeof(LootView).GetField("characterInventoryDisplay", privateInstance);

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
                containerObject = null;
                courierInventory = null;
                courierLootbox = null;
            }

            // 创建容器对象
            containerObject = new GameObject("CourierServiceContainer");
            containerObject.SetActive(false);

            // 添加 Inventory 组件
            courierInventory = containerObject.AddComponent<Inventory>();
            courierInventory.SetCapacity(CONTAINER_CAPACITY);
            BoxCollider boxCollider = containerObject.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = new Vector3(0.1f, 0.1f, 0.1f);
            boxCollider.enabled = false;

            // 添加 InteractableLootbox 组件（用于触发官方 LootView）
            courierLootbox = containerObject.AddComponent<InteractableLootbox>();
            NPCInteractionGroupHelper.GetOrCreateGroupList(courierLootbox, "[CourierService]");
            courierLootbox.enabled = false;

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
            containerObject.SetActive(true);

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

    }
}

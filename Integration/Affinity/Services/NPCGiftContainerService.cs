// ============================================================================
// NPCGiftContainerService.cs - NPC礼物容器服务
// ============================================================================
// 核心服务类，管理礼物容器的生命周期。
// 使用 InteractableLootbox + LootView 实现单格容器UI，
// 支持玩家将物品放入容器格子中进行赠送。
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ItemStatsSystem;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// NPC礼物容器服务（静态类）
    /// 管理礼物容器的创建、打开、关闭和资源清理
    /// </summary>
    public static class NPCGiftContainerService
    {
        // ============================================================================
        // 私有字段 - 容器相关
        // ============================================================================
        
        /// <summary>容器游戏对象</summary>
        private static GameObject containerObject;
        
        /// <summary>礼物容器 Inventory 组件</summary>
        private static Inventory giftInventory;
        
        /// <summary>礼物容器 InteractableLootbox 组件</summary>
        private static InteractableLootbox giftLootbox;
        
        // ============================================================================
        // 私有字段 - NPC引用
        // ============================================================================
        
        /// <summary>当前NPC的唯一标识符</summary>
        private static string currentNpcId;
        
        /// <summary>当前NPC的Transform（用于显示气泡和控制动画）</summary>
        private static Transform npcTransform;
        
        /// <summary>当前NPC的礼物容器配置</summary>
        private static INPCGiftContainerConfig currentConfig;
        
        /// <summary>当前NPC的控制器（用于播放动画）</summary>
        private static GoblinNPCController currentNpcController;

        // ============================================================================
        // 私有字段 - UI组件
        // ============================================================================
        
        /// <summary>赠送按钮游戏对象</summary>
        private static GameObject giftButtonObject;
        
        /// <summary>赠送按钮组件</summary>
        private static Button giftButton;
        
        // ============================================================================
        // 私有字段 - 本地化文本缓存
        // ============================================================================
        
        /// <summary>缓存的赠送按钮文本</summary>
        private static string cachedGiftButtonText;
        
        /// <summary>缓存的空槽位提示文本</summary>
        private static string cachedEmptySlotText;
        
        // ============================================================================
        // 私有字段 - 服务状态
        // ============================================================================
        
        /// <summary>服务是否激活</summary>
        private static bool isServiceActive = false;
        
        // ============================================================================
        // 常量
        // ============================================================================
        
        /// <summary>容器容量（单格容器）</summary>
        private const int CONTAINER_CAPACITY = 1;
        
        // ============================================================================
        // 公共属性
        // ============================================================================
        
        /// <summary>
        /// 检查服务是否激活
        /// </summary>
        public static bool IsServiceActive
        {
            get { return isServiceActive; }
        }
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 打开礼物容器服务（由 NPCGiftInteractable 调用）
        /// 创建单格容器并打开 LootView，允许玩家放入礼物
        /// </summary>
        /// <param name="npcId">NPC的唯一标识符</param>
        /// <param name="npcTransformParam">NPC的Transform</param>
        /// <param name="config">NPC礼物容器配置</param>
        /// <param name="npcController">NPC控制器（可选）</param>
        public static void OpenService(string npcId, Transform npcTransformParam, INPCGiftContainerConfig config, GoblinNPCController npcController = null)
        {
            // 防止重复调用
            if (isServiceActive)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] 服务已在运行中，忽略重复调用");
                return;
            }
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 开始打开礼物容器服务...");
            ModBehaviour.DevLog("[NPCGiftContainerService] NPC ID: " + npcId);
            
            // 保存NPC引用
            currentNpcId = npcId;
            npcTransform = npcTransformParam;
            currentConfig = config;
            currentNpcController = npcController;
            
            // 如果没有传入控制器，尝试从 Transform 获取
            if (currentNpcController == null && npcTransform != null)
            {
                currentNpcController = npcTransform.GetComponent<GoblinNPCController>();
            }
            
            // 开始对话时播放对话动画
            if (currentNpcController != null)
            {
                currentNpcController.StartDialogue();
            }
            
            // 初始化反射字段缓存
            InitializeReflection();
            
            // 缓存本地化文本
            CacheLocalizedTexts();
            
            // 创建礼物容器
            CreateGiftContainer();
            
            // 检查 LootView.Instance 是否存在
            if (LootView.Instance == null)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] LootView.Instance 为空，无法打开UI");
                Cleanup();
                return;
            }
            
            // 打开 LootView
            OpenLootViewOfficial();
            
            // 设置服务状态为激活
            isServiceActive = true;
            
            // 延迟创建赠送按钮（等待 LootView 完全打开）
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StartCoroutine(CreateGiftButtonDelayed());
            }
            else
            {
                CreateGiftButton();
            }
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 礼物容器服务已打开");
        }

        /// <summary>
        /// 延迟创建赠送按钮（等待 LootView 完全打开）
        /// </summary>
        private static System.Collections.IEnumerator CreateGiftButtonDelayed()
        {
            yield return new WaitForSeconds(0.15f);
            
            // 检查服务是否仍然激活
            if (!isServiceActive)
            {
                yield break;
            }
            
            // 确保 LootView 已打开
            if (LootView.Instance == null || !LootView.Instance.open)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] LootView 未打开，无法创建赠送按钮");
                yield break;
            }
            
            CreateGiftButton();
        }
        
        /// <summary>
        /// 关闭礼物容器服务
        /// </summary>
        public static void CloseService()
        {
            if (!isServiceActive)
            {
                return;
            }
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 关闭礼物容器服务...");
            
            CloseLootView();
            Cleanup();
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 礼物容器服务已关闭");
        }
        
        // ============================================================================
        // 反射字段缓存
        // ============================================================================
        
        private static FieldInfo inventoryReferenceField = null;
        private static FieldInfo lootboxDisplayNameKeyField = null;
        private static bool reflectionInitialized = false;
        
        // ============================================================================
        // 私有方法 - 反射初始化
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
                
                inventoryReferenceField = typeof(InteractableLootbox).GetField("inventoryReference", privateInstance);
                lootboxDisplayNameKeyField = typeof(InteractableLootbox).GetField("displayNameKey", privateInstance);
                
                reflectionInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 反射初始化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 缓存本地化文本
        /// </summary>
        private static void CacheLocalizedTexts()
        {
            string giftButtonTextKey = NPCGiftContainerConfigDefaults.GetGiftButtonTextKey(currentConfig);
            string emptySlotTextKey = NPCGiftContainerConfigDefaults.GetEmptySlotTextKey(currentConfig);
            
            cachedGiftButtonText = LocalizationHelper.GetLocalizedText(giftButtonTextKey);
            cachedEmptySlotText = LocalizationHelper.GetLocalizedText(emptySlotTextKey);
        }
        
        // ============================================================================
        // 私有方法 - 容器创建
        // ============================================================================
        
        /// <summary>
        /// 创建礼物容器
        /// </summary>
        private static void CreateGiftContainer()
        {
            // 清理旧容器
            if (containerObject != null)
            {
                UnityEngine.Object.Destroy(containerObject);
                containerObject = null;
                giftInventory = null;
                giftLootbox = null;
            }
            
            // 创建容器游戏对象
            containerObject = new GameObject("NPCGiftContainer");
            
            // 添加 Inventory 组件
            giftInventory = containerObject.AddComponent<Inventory>();
            giftInventory.SetCapacity(CONTAINER_CAPACITY);
            
            // 添加 InteractableLootbox 组件
            giftLootbox = containerObject.AddComponent<InteractableLootbox>();
            
            // 设置 inventoryReference
            if (inventoryReferenceField != null)
            {
                inventoryReferenceField.SetValue(giftLootbox, giftInventory);
            }
            
            // 设置容器显示名称
            string displayNameKey = NPCGiftContainerConfigDefaults.GetContainerTitleKey(currentConfig);
            SetLootboxDisplayNameKey(giftLootbox, displayNameKey);
            
            // 订阅容器内容变化事件
            giftInventory.onContentChanged += OnContainerContentChanged;
            
            // 订阅 LootView 关闭事件
            InteractableLootbox.OnStopLoot += OnLootboxStopLoot;
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 礼物容器创建成功");
        }

        /// <summary>
        /// 设置 InteractableLootbox 的 displayNameKey 字段
        /// </summary>
        private static void SetLootboxDisplayNameKey(InteractableLootbox lootbox, string key)
        {
            if (lootbox == null || lootboxDisplayNameKeyField == null) return;
            
            try
            {
                lootboxDisplayNameKeyField.SetValue(lootbox, key);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 设置 displayNameKey 失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 反射字段缓存 - OnStartLoot 事件
        // ============================================================================
        
        private static FieldInfo onStartLootField = null;
        private static FieldInfo targetLootBoxField = null;
        
        // ============================================================================
        // 私有方法 - 打开 LootView
        // ============================================================================
        
        /// <summary>
        /// 通过触发官方 OnStartLoot 事件打开容器UI
        /// </summary>
        private static void OpenLootViewOfficial()
        {
            try
            {
                if (LootView.Instance == null || giftLootbox == null || giftLootbox.Inventory == null)
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 无法打开 LootView");
                    return;
                }
                
                TriggerOnStartLootEvent(giftLootbox);
                ModBehaviour.DevLog("[NPCGiftContainerService] 礼物容器UI已打开");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 打开 LootView 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 触发 InteractableLootbox.OnStartLoot 静态事件
        /// </summary>
        private static void TriggerOnStartLootEvent(InteractableLootbox lootbox)
        {
            try
            {
                if (onStartLootField == null)
                {
                    onStartLootField = typeof(InteractableLootbox).GetField("OnStartLoot", 
                        BindingFlags.NonPublic | BindingFlags.Static);
                }
                
                if (onStartLootField != null)
                {
                    var onStartLootDelegate = onStartLootField.GetValue(null) as Action<InteractableLootbox>;
                    if (onStartLootDelegate != null)
                    {
                        onStartLootDelegate.Invoke(lootbox);
                        return;
                    }
                }
                
                // 备用方案
                FallbackOpenLootView(lootbox);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] 触发 OnStartLoot 失败: " + e.Message);
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
                if (targetLootBoxField == null)
                {
                    targetLootBoxField = typeof(LootView).GetField("targetLootBox", 
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }
                
                if (targetLootBoxField != null)
                {
                    targetLootBoxField.SetValue(LootView.Instance, lootbox);
                    LootView.Instance.Open(null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 备用方案失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 反射字段缓存 - 按钮创建
        // ============================================================================
        
        private static FieldInfo lootTargetInventoryDisplayField = null;
        private static FieldInfo sortButtonField = null;

        // ============================================================================
        // 私有方法 - 赠送按钮创建
        // ============================================================================
        
        /// <summary>
        /// 创建赠送按钮
        /// </summary>
        private static void CreateGiftButton()
        {
            if (LootView.Instance == null)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] LootView.Instance 为空");
                return;
            }
            
            try
            {
                InitializeButtonReflection();
                
                // 获取礼物容器区域的 InventoryDisplay
                Duckov.UI.InventoryDisplay giftInventoryDisplay = null;
                if (lootTargetInventoryDisplayField != null)
                {
                    giftInventoryDisplay = lootTargetInventoryDisplayField.GetValue(LootView.Instance) as Duckov.UI.InventoryDisplay;
                }
                
                if (giftInventoryDisplay == null)
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 无法获取 InventoryDisplay");
                    return;
                }
                
                // 获取整理按钮作为模板
                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(giftInventoryDisplay) as Button;
                }
                
                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 无法获取整理按钮");
                    return;
                }
                
                // 复制整理按钮作为赠送按钮
                giftButtonObject = UnityEngine.Object.Instantiate(sortButton.gameObject, sortButton.transform.parent);
                giftButtonObject.name = "NPCGiftButton";
                giftButtonObject.SetActive(true);
                
                // 复制位置设置
                RectTransform rt = giftButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition;
                    rt.sizeDelta = sortRt.sizeDelta;
                }
                
                // 配置按钮组件
                giftButton = giftButtonObject.GetComponent<Button>();
                if (giftButton != null)
                {
                    giftButton.onClick.RemoveAllListeners();
                    giftButton.onClick.AddListener(OnGiftButtonClicked);
                }
                else
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 无法获取按钮组件");
                    return;
                }
                
                SetGiftButtonText();
                UpdateButtonState();
                
                ModBehaviour.DevLog("[NPCGiftContainerService] 赠送按钮创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 创建赠送按钮失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 初始化按钮相关的反射字段缓存
        /// </summary>
        private static void InitializeButtonReflection()
        {
            try
            {
                BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                
                if (lootTargetInventoryDisplayField == null)
                {
                    lootTargetInventoryDisplayField = typeof(LootView).GetField("lootTargetInventoryDisplay", privateInstance);
                }
                
                if (sortButtonField == null)
                {
                    sortButtonField = typeof(Duckov.UI.InventoryDisplay).GetField("sortButton", privateInstance);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 按钮反射初始化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置赠送按钮的文本
        /// </summary>
        private static void SetGiftButtonText()
        {
            if (giftButtonObject == null) return;
            
            string buttonTextKey = NPCGiftContainerConfigDefaults.GetGiftButtonTextKey(currentConfig);
            string buttonTextValue = LocalizationHelper.GetLocalizedText(buttonTextKey);
            
            var tmpText = giftButtonObject.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = buttonTextValue;
                return;
            }
            
            var legacyText = giftButtonObject.GetComponentInChildren<Text>();
            if (legacyText != null)
            {
                legacyText.text = buttonTextValue;
            }
        }
        
        /// <summary>
        /// 赠送按钮点击事件处理
        /// </summary>
        private static void OnGiftButtonClicked()
        {
            ModBehaviour.DevLog("[NPCGiftContainerService] 赠送按钮被点击");
            ExecuteGift();
        }

        // ============================================================================
        // 私有方法 - 按钮状态更新
        // ============================================================================
        
        /// <summary>
        /// 更新按钮状态（文本、颜色、可交互性）
        /// </summary>
        private static void UpdateButtonState()
        {
            if (giftButton == null) return;
            
            bool hasItems = giftInventory != null && giftInventory.GetItemCount() > 0;
            
            string displayText;
            bool interactable;
            Color buttonColor;
            
            if (hasItems)
            {
                displayText = cachedGiftButtonText;
                interactable = true;
                buttonColor = new Color(0.2f, 0.8f, 0.2f);
            }
            else
            {
                displayText = cachedEmptySlotText;
                interactable = false;
                buttonColor = Color.gray;
            }
            
            // 应用文本
            if (giftButtonObject != null)
            {
                var tmpText = giftButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                if (tmpText != null)
                {
                    tmpText.text = displayText;
                }
                else
                {
                    var legacyText = giftButtonObject.GetComponentInChildren<Text>();
                    if (legacyText != null)
                    {
                        legacyText.text = displayText;
                    }
                }
            }
            
            // 应用可交互性和颜色
            giftButton.interactable = interactable;
            var colors = giftButton.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.1f;
            colors.pressedColor = buttonColor * 0.9f;
            colors.disabledColor = Color.gray;
            giftButton.colors = colors;
        }
        
        /// <summary>
        /// 容器内容变化事件处理
        /// </summary>
        private static void OnContainerContentChanged(Inventory inventory, int index)
        {
            UpdateButtonState();
        }
        
        // ============================================================================
        // 私有方法 - 礼物赠送
        // ============================================================================
        
        /// <summary>
        /// 执行礼物赠送
        /// </summary>
        private static void ExecuteGift()
        {
            ModBehaviour.DevLog("[NPCGiftContainerService] ExecuteGift() 开始执行");
            
            // 标记服务为非激活状态，防止重复处理
            isServiceActive = false;
            
            // 取消 OnStopLoot 事件订阅
            try
            {
                InteractableLootbox.OnStopLoot -= OnLootboxStopLoot;
            }
            catch { }
            
            try
            {
                // 检查容器是否有物品
                if (giftInventory == null || giftInventory.GetItemCount() <= 0)
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] 容器为空");
                    isServiceActive = true;
                    CloseService();
                    return;
                }
                
                // 获取物品
                Item giftItem = null;
                foreach (Item item in giftInventory)
                {
                    if (item != null)
                    {
                        giftItem = item;
                        break;
                    }
                }
                
                if (giftItem == null)
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] 无法获取物品");
                    isServiceActive = true;
                    CloseService();
                    return;
                }
                
                string itemName = giftItem.DisplayName ?? "Unknown";
                ModBehaviour.DevLog("[NPCGiftContainerService] 准备赠送物品: " + itemName);
                
                // 从容器中移除物品
                giftInventory.RemoveItem(giftItem);
                
                GameObject itemGameObject = giftItem.gameObject;
                
                // 调用 NPCGiftSystem 处理礼物赠送
                bool success = NPCGiftSystem.GiveGift(currentNpcId, giftItem, npcTransform, currentNpcController);
                
                if (success)
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] 礼物赠送成功");
                    if (itemGameObject != null)
                    {
                        UnityEngine.Object.Destroy(itemGameObject);
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] 礼物赠送失败，返还物品");
                    ReturnItemToPlayer(giftItem);
                }
                
                CloseLootView();
                CleanupResources();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] ExecuteGift() 异常: " + e.Message);
                try { CloseLootView(); } catch { }
                try { CleanupResources(); } catch { }
            }
        }

        /// <summary>
        /// 关闭 LootView UI
        /// </summary>
        private static void CloseLootView()
        {
            try
            {
                if (LootView.Instance != null && LootView.Instance.open)
                {
                    LootView.Instance.Close();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] 关闭 LootView 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 返还物品到玩家背包
        /// </summary>
        private static void ReturnItemToPlayer(Item item)
        {
            if (item == null) return;
            
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    Item charItem = player.CharacterItem;
                    if (charItem != null)
                    {
                        Inventory playerInventory = charItem.Inventory;
                        if (playerInventory != null)
                        {
                            bool added = playerInventory.AddAndMerge(item, 0);
                            if (added)
                            {
                                ModBehaviour.DevLog("[NPCGiftContainerService] 物品已返还到玩家背包");
                                return;
                            }
                        }
                    }
                }
                
                // 背包满，丢到地上
                DropItemOnGround(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [WARNING] 返还物品失败: " + e.Message);
                DropItemOnGround(item);
            }
        }
        
        /// <summary>
        /// 将物品丢到玩家脚下
        /// </summary>
        private static void DropItemOnGround(Item item)
        {
            if (item == null) return;
            
            try
            {
                Vector3 dropPosition = Vector3.zero;
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    dropPosition = player.transform.position + new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        0.5f,
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    );
                }
                
                if (item.gameObject != null)
                {
                    item.transform.position = dropPosition;
                    item.gameObject.SetActive(true);
                    ModBehaviour.DevLog("[NPCGiftContainerService] 物品已丢到地上");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 丢弃物品失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 私有方法 - 容器关闭处理
        // ============================================================================
        
        /// <summary>
        /// OnStopLoot 事件处理
        /// </summary>
        private static void OnLootboxStopLoot(InteractableLootbox lootbox)
        {
            if (!isServiceActive) return;
            if (lootbox != giftLootbox) return;
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 收到 OnStopLoot 事件");
            OnLootViewClosed();
        }
        
        /// <summary>
        /// LootView 关闭事件处理（玩家关闭UI但未点击赠送按钮）
        /// </summary>
        private static void OnLootViewClosed()
        {
            if (!isServiceActive) return;
            
            ModBehaviour.DevLog("[NPCGiftContainerService] LootView 被关闭（未点击赠送按钮）");
            
            isServiceActive = false;
            ReturnContainerItemsToPlayer();
            Cleanup();
        }
        
        /// <summary>
        /// 返还容器中的物品到玩家背包
        /// </summary>
        private static void ReturnContainerItemsToPlayer()
        {
            if (giftInventory == null) return;
            
            int itemCount = giftInventory.GetItemCount();
            if (itemCount == 0) return;
            
            ModBehaviour.DevLog("[NPCGiftContainerService] 返还 " + itemCount + " 件物品到玩家背包");
            
            try
            {
                Item item = null;
                foreach (Item i in giftInventory)
                {
                    if (i != null)
                    {
                        item = i;
                        break;
                    }
                }
                
                if (item == null) return;
                
                item.Detach();
                ReturnItemToPlayer(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] 返还物品失败: " + e.Message);
            }
        }

        // ============================================================================
        // 私有方法 - 资源清理
        // ============================================================================
        
        /// <summary>
        /// 清理资源（完整清理，包含事件取消订阅）
        /// </summary>
        private static void Cleanup()
        {
            ModBehaviour.DevLog("[NPCGiftContainerService] Cleanup() 开始清理资源");
            
            try
            {
                // 取消容器内容变化事件订阅
                if (giftInventory != null)
                {
                    try { giftInventory.onContentChanged -= OnContainerContentChanged; }
                    catch { }
                }
                
                // 取消 OnStopLoot 事件订阅
                try { InteractableLootbox.OnStopLoot -= OnLootboxStopLoot; }
                catch { }
                
                CleanupResources();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] Cleanup() 异常: " + e.Message);
                isServiceActive = false;
            }
        }
        
        /// <summary>
        /// 清理资源（不包含事件取消订阅，由 Cleanup() 统一处理）
        /// </summary>
        private static void CleanupResources()
        {
            ModBehaviour.DevLog("[NPCGiftContainerService] CleanupResources() 开始清理");
            
            try
            {
                // 注意：事件取消订阅已在 Cleanup() 中统一处理，此处不再重复
                
                // 销毁赠送按钮
                if (giftButtonObject != null)
                {
                    UnityEngine.Object.Destroy(giftButtonObject);
                    giftButtonObject = null;
                    giftButton = null;
                }
                
                // 销毁容器对象
                if (containerObject != null)
                {
                    UnityEngine.Object.Destroy(containerObject);
                    containerObject = null;
                    giftInventory = null;
                    giftLootbox = null;
                }
                
                // 恢复NPC状态
                if (currentNpcController != null)
                {
                    try { currentNpcController.EndDialogueWithStay(5f); }
                    catch { }
                }
                
                // 清空引用
                currentNpcId = null;
                npcTransform = null;
                currentConfig = null;
                currentNpcController = null;
                cachedGiftButtonText = null;
                cachedEmptySlotText = null;
                
                isServiceActive = false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCGiftContainerService] [ERROR] CleanupResources() 异常: " + e.Message);
                isServiceActive = false;
            }
        }
    }
}

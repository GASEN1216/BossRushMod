// ============================================================================
// NPCShopSystem.cs - 通用NPC商店系统
// ============================================================================
// 模块说明：
//   通用的NPC商店系统，支持任意实现 INPCShopConfig 的NPC。
//   管理商店创建、商品配置、折扣应用等功能。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using BossRush.Utils;
using TMPro;

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
        private static INPCController currentController = null;
        private static bool isServiceActive = false;

        // 反射缓存
        private static FieldInfo textSellField = null;
        private static FieldInfo priceTextField = null;
        private static FieldInfo interactionButtonField = null;
        private static FieldInfo interactionTextField = null;
        private static bool reflectionInitialized = false;
        private static string originalTextSell = null;
        private static readonly Dictionary<int, float> temporaryPurificationFactors = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> temporaryPurificationPrices = new Dictionary<int, int>();

        // 常量
        private const float BUBBLE_Y_OFFSET = 1.2f;
        private const float BUBBLE_DURATION = 3f;
        private const string TemporaryPurificationPriceUnavailable = "TemporaryPurificationPriceUnavailable";

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
        public static void OpenShop(string npcId, Transform npcTransform, INPCController controller = null)
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
                    catch (Exception e)
                    {
                        NPCExceptionHandler.LogAndIgnore(e, "NPCShopSystem.OpenShop.ShowHintBubble");
                    }
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
                    RegisterShopSelectionEvent();
                    UpdateTemporaryShopCurrencyUiDeferred();

                    // 显示购物对话
                    ShowShoppingDialogue(npcId);

                    ModBehaviour.DevLog("[NPCShop] 商店已打开: " + npcId);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCShop] [ERROR] 打开商店失败: " + e.Message);
                    UnregisterEvents();
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

        public static void CloseShopIfOwnedBy(Transform npcTransform)
        {
            if (!isServiceActive || !IsCurrentShopOwnedBy(npcTransform))
            {
                return;
            }

            CloseShop();
        }

        public static void ResetStaticCaches()
        {
            isServiceActive = false;
            UnregisterEvents();
            RestoreShopUIText();
            Cleanup();

            textSellField = null;
            priceTextField = null;
            interactionButtonField = null;
            interactionTextField = null;
            reflectionInitialized = false;
            originalTextSell = null;
        }

        private static bool IsCurrentShopOwnedBy(Transform npcTransform)
        {
            if (npcTransform == null || currentNpcTransform == null)
            {
                return false;
            }

            return ReferenceEquals(currentNpcTransform, npcTransform) ||
                   currentNpcTransform.IsChildOf(npcTransform) ||
                   npcTransform.IsChildOf(currentNpcTransform);
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

        /// <summary>
        /// 获取指定NPC的卖出加成率
        /// 返回实际的 sellFactor（原版0.5，有折扣时更高）
        /// </summary>
        public static float GetSellFactor(string npcId)
        {
            float discount = GetDiscount(npcId);
            // 卖出加成 = 基础0.5 + 折扣的一半
            return 0.5f + (discount / 2f);
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
                priceTextField = typeof(StockShopView).GetField("priceText", privateInstance);
                interactionButtonField = typeof(StockShopView).GetField("interactionButton", privateInstance);
                interactionTextField = typeof(StockShopView).GetField("interactionText", privateInstance);
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

            temporaryPurificationFactors.Clear();
            temporaryPurificationPrices.Clear();

            // 创建商店对象
            shopObject = new GameObject("NPCShop_" + npcId);
            currentShop = shopObject.AddComponent<StockShop>();

            // 设置 accountAvaliable = true，允许使用银行账户付款（而不仅仅是现金物品）
            try
            {
                var fAccount = ReflectionCache.StockShop_AccountAvaliable;
                if (fAccount != null)
                {
                    fAccount.SetValue(currentShop, true);
                    ModBehaviour.DevLog("[NPCShop] 已设置 accountAvaliable = true，允许使用银行账户付款");
                }
            }
            catch (System.Exception ex)
            {
                ModBehaviour.DevLog("[NPCShop] [ERROR] 设置 accountAvaliable 失败: " + ex.Message);
            }

            // 配置商品
            ConfigureShopEntries(npcId);

            // 配置卖出加成（好感度折扣也影响卖出价格）
            ConfigureSellBonus(npcId);

            // 关键：手动缓存物品实例到 itemInstances 字典
            // 游戏原生商店在 Start() 中异步调用 CacheItemInstances()
            // 但我们是动态创建的商店，需要在 ShowUI() 之前手动填充
            CacheItemInstancesManually(npcId);

            ModBehaviour.DevLog("[NPCShop] 商店创建成功: " + npcId);
        }

        /// <summary>
        /// 配置卖出加成 - 好感度折扣也影响卖出价格
        /// 原版 sellFactor = 0.5（卖出获得50%价值）
        /// 有折扣时：sellFactor = 0.5 + discount/2（折扣的一半作为卖出加成）
        /// 例如：20%折扣 → sellFactor = 0.6（卖出获得60%价值）
        /// </summary>
        private static void ConfigureSellBonus(string npcId)
        {
            if (currentShop == null) return;

            var config = AffinityManager.GetNPCConfig(npcId);
            var shopConfig = config as INPCShopConfig;

            if (shopConfig == null) return;

            if (IsZombieModeTemporaryPurificationShop())
            {
                currentShop.sellFactor = 0f;
                ModBehaviour.DevLog("[NPCShop] 临时净化点商店已禁用现金出售: sellFactor=0");
                return;
            }

            int level = AffinityManager.GetLevel(npcId);
            float discount = shopConfig.GetDiscountForLevel(level);

            // 计算卖出加成：基础0.5 + 折扣的一半
            // 这样20%折扣 = 卖出获得60%价值（比原版50%多10%）
            float sellBonus = discount / 2f;
            float newSellFactor = 0.5f + sellBonus;

            // 通过反射设置 sellFactor（公共字段）
            currentShop.sellFactor = newSellFactor;

            if (discount > 0)
            {
                ModBehaviour.DevLog("[NPCShop] 卖出加成已配置: 折扣=" + (discount * 100) + "%, sellFactor=" + newSellFactor);
            }
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
                            if (IsZombieModeTemporaryPurificationShop())
                            {
                                float factor;
                                if (temporaryPurificationFactors.TryGetValue(typeId, out factor))
                                {
                                    temporaryPurificationPrices[typeId] = CalculatePurificationPrice(item, factor);
                                }
                            }

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

            List<ShopItemEntry> items = shopConfig.GetShopItems();
            if (items == null || items.Count == 0)
            {
                ModBehaviour.DevLog("[NPCShop] 商店暂无商品");
                return;
            }

            foreach (var item in items)
            {
                if (level < item.RequiredLevel)
                {
                    continue;
                }

                try
                {
                    StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                    itemEntry.typeID = item.TypeID;
                    itemEntry.maxStock = item.MaxStock;
                    float adjustedPriceFactor = item.BasePriceFactor * (1.0f - discount);
                    itemEntry.priceFactor = adjustedPriceFactor;
                    itemEntry.forceUnlock = true;
                    itemEntry.lockInDemo = false;
                    itemEntry.possibility = item.Possibility;

                    if (IsZombieModeTemporaryPurificationShop())
                    {
                        temporaryPurificationFactors[item.TypeID] = adjustedPriceFactor;
                        itemEntry.priceFactor = 0f;
                    }

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
            StockShop.OnItemPurchased += OnItemPurchased;
            StockShop.OnItemSoldByPlayer += OnItemSoldByPlayer;
            ManagedUIElement.onClose += OnManagedUIElementClose;
        }

        private static void UnregisterEvents()
        {
            StockShop.OnItemPurchased -= OnItemPurchased;
            StockShop.OnItemSoldByPlayer -= OnItemSoldByPlayer;
            ManagedUIElement.onClose -= OnManagedUIElementClose;
            UnregisterShopSelectionEvent();
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

        private static void RegisterShopSelectionEvent()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null)
                {
                    return;
                }

                shopView.onSelectionChanged = (Action)Delegate.Combine(shopView.onSelectionChanged, new Action(OnShopSelectionChanged));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 注册商店选择事件失败: " + e.Message);
            }
        }

        private static void UnregisterShopSelectionEvent()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null)
                {
                    return;
                }

                shopView.onSelectionChanged = (Action)Delegate.Remove(shopView.onSelectionChanged, new Action(OnShopSelectionChanged));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 取消注册商店选择事件失败: " + e.Message);
            }
        }

        private static void OnShopSelectionChanged()
        {
            if (!isServiceActive)
            {
                return;
            }

            UpdateTemporaryShopCurrencyUiDeferred();
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
                    string currentText = textSellField.GetValue(shopView) as string;
                    if (originalTextSell == null)
                    {
                        originalTextSell = currentText;
                    }

                    textSellField.SetValue(
                        shopView,
                        IsZombieModeTemporaryPurificationShop()
                            ? L10n.T("购买（净化点）", "Buy (Purification)")
                            : L10n.T("购买", "Buy"));
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
                    textSellField.SetValue(shopView, originalTextSell);
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
            temporaryPurificationFactors.Clear();
            temporaryPurificationPrices.Clear();
        }

        private static bool IsZombieModeTemporaryPurificationShop()
        {
            return currentNpcTransform != null &&
                   ModBehaviour.Instance != null &&
                   ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentNpcTransform);
        }

        private static int CalculatePurificationPrice(Item item, float factor)
        {
            if (item == null || factor <= 0f)
            {
                return 0;
            }

            return Math.Max(1, Mathf.CeilToInt(item.GetTotalRawValue() * factor));
        }

        private static bool TryGetPurificationPriceForType(int typeId, out int price)
        {
            return temporaryPurificationPrices.TryGetValue(typeId, out price) && price > 0;
        }

        private static bool CanPurchaseTemporaryPurificationShopSelection(StockShop.Entry entry, out int price, out bool priceAvailable)
        {
            price = 0;
            priceAvailable = false;
            if (entry == null)
            {
                return false;
            }

            priceAvailable = TryGetPurificationPriceForType(entry.ItemTypeID, out price);
            if (!priceAvailable)
            {
                return false;
            }

            return ModBehaviour.Instance != null &&
                   ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(currentNpcTransform, price);
        }

        private static IEnumerator UpdateTemporaryShopCurrencyUiNextFrame()
        {
            yield return null;
            UpdateTemporaryShopCurrencyUi();
        }

        private static void UpdateTemporaryShopCurrencyUiDeferred()
        {
            if (!IsZombieModeTemporaryPurificationShop() || ModBehaviour.Instance == null)
            {
                return;
            }

            ModBehaviour.Instance.StartCoroutine(UpdateTemporaryShopCurrencyUiNextFrame());
        }

        private static void UpdateTemporaryShopCurrencyUi()
        {
            if (!IsZombieModeTemporaryPurificationShop())
            {
                return;
            }

            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null)
                {
                    return;
                }

                int price = 0;
                var selected = shopView.GetSelection();
                bool hasSelectedShopEntry = selected != null &&
                                            selected.Target != null &&
                                            currentShop != null &&
                                            currentShop.entries != null &&
                                            currentShop.entries.Contains(selected.Target);

                if (!hasSelectedShopEntry)
                {
                    string disabledText = selected != null && selected.Target != null
                        ? L10n.T("不可出售", "Selling disabled")
                        : L10n.T("选择商品", "Select item");

                    if (priceTextField != null)
                    {
                        var priceText = priceTextField.GetValue(shopView) as TextMeshProUGUI;
                        if (priceText != null)
                        {
                            priceText.text = disabledText;
                            priceText.color = new Color(1f, 0.4f, 0.4f);
                        }
                    }

                    if (interactionButtonField != null)
                    {
                        var interactionButton = interactionButtonField.GetValue(shopView) as Button;
                        if (interactionButton != null)
                        {
                            interactionButton.interactable = false;
                        }
                    }

                    if (interactionTextField != null)
                    {
                        var interactionText = interactionTextField.GetValue(shopView) as TextMeshProUGUI;
                        if (interactionText != null)
                        {
                            interactionText.text = disabledText;
                        }
                    }

                    return;
                }

                bool priceAvailable;
                bool canAfford = CanPurchaseTemporaryPurificationShopSelection(selected.Target, out price, out priceAvailable);

                if (priceTextField != null)
                {
                    var priceText = priceTextField.GetValue(shopView) as TextMeshProUGUI;
                    if (priceText != null)
                    {
                        priceText.text = priceAvailable
                            ? "净化点 " + price.ToString("N0")
                            : L10n.T("价格不可用", "Price unavailable");
                        priceText.color = canAfford ? Color.white : new Color(1f, 0.4f, 0.4f);
                    }
                }

                if (interactionButtonField != null)
                {
                    var interactionButton = interactionButtonField.GetValue(shopView) as Button;
                    if (interactionButton != null)
                    {
                        interactionButton.interactable = canAfford;
                    }
                }

                if (interactionTextField != null)
                {
                    var interactionText = interactionTextField.GetValue(shopView) as TextMeshProUGUI;
                    if (interactionText != null)
                    {
                        interactionText.text = !priceAvailable
                            ? L10n.T("价格不可用", "Price unavailable")
                            : canAfford
                            ? L10n.T("购买（净化点）", "Buy (Purification)")
                            : L10n.T("净化点不足", "Not enough Purification");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 更新净化点商店 UI 失败: " + e.Message);
            }
        }

        private static void OnItemPurchased(StockShop shop, Item purchasedItem)
        {
            if (!isServiceActive || shop != currentShop || purchasedItem == null || !IsZombieModeTemporaryPurificationShop())
            {
                return;
            }

            int price;
            bool priceAvailable = TryGetPurificationPriceForType(purchasedItem.TypeID, out price);
            if (!priceAvailable)
            {
                RollbackTemporaryPurificationShopPurchase(purchasedItem, TemporaryPurificationPriceUnavailable);
                NotificationText.Push(L10n.T("价格不可用，请重新打开商店。", "Price unavailable. Please reopen the shop."));
                UpdateTemporaryShopCurrencyUiDeferred();
                return;
            }

            if (ModBehaviour.Instance == null ||
                !ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(currentNpcTransform, price, "ZombieModeTempGoblinShopBuy"))
            {
                RollbackTemporaryPurificationShopPurchase(purchasedItem, "NotEnoughPurification");
                NotificationText.Push(L10n.T("净化点不足。", "Not enough purification."));
            }

            UpdateTemporaryShopCurrencyUiDeferred();
        }

        private static void RollbackTemporaryPurificationShopPurchase(Item purchasedItem, string reason)
        {
            if (purchasedItem == null)
            {
                return;
            }

            int typeId = purchasedItem.TypeID;
            int stockRollbackCount = 1;
            try
            {
                purchasedItem.Detach();
                if (purchasedItem.gameObject != null)
                {
                    UnityEngine.Object.Destroy(purchasedItem.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 回滚净化点商店购买物品失败: " + e.Message);
            }

            if (currentShop != null && currentShop.entries != null)
            {
                foreach (var entry in currentShop.entries)
                {
                    if (entry != null && entry.ItemTypeID == typeId)
                    {
                        entry.CurrentStock += stockRollbackCount;
                        entry.Show = true;
                        break;
                    }
                }
            }

            try
            {
                var shopView = StockShopView.Instance;
                if (shopView != null)
                {
                    var setupAndShow = typeof(StockShopView).GetMethod("SetupAndShow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (setupAndShow != null)
                    {
                        setupAndShow.Invoke(shopView, new object[] { currentShop });
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 刷新净化点商店 UI 失败: " + e.Message);
            }

            ModBehaviour.DevLog("[NPCShop] 临时净化点商店购买已回滚: " + reason);
        }

        private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)
        {
            if (!isServiceActive || shop != currentShop || soldItem == null || !IsZombieModeTemporaryPurificationShop())
            {
                return;
            }

            RejectTemporaryPurificationShopSell(soldItem, price);
            UpdateTemporaryShopCurrencyUiDeferred();
        }

        private static void RejectTemporaryPurificationShopSell(Item soldItem, int price)
        {
            if (soldItem == null)
            {
                return;
            }

            if (price > 0)
            {
                try
                {
                    Cost cost = new Cost(price);
                    if (cost.Enough)
                    {
                        cost.Pay(true, true);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[NPCShop] [WARNING] 回收临时净化点商店现金出售收益失败: " + e.Message);
                }
            }

            try
            {
                ItemUtilities.SendToPlayer(soldItem, true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 返还临时净化点商店售出物品失败: " + e.Message);
            }

            NotificationText.Push(L10n.T(
                "临时哥布林只接受净化点购买，不能出售物品。",
                "Temporary goblin only accepts Purification purchases; selling is disabled."));
        }
    }
}

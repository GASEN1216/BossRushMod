// ============================================================================
// ModeEMerchantSupportClasses.cs - Mode E merchant interactable, UI, and pet helpers
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;

namespace BossRush
{
    public class ModeEShopInteractable : InteractableBase
    {
        /// <summary>关联的 StockShop 实例</summary>
        private StockShop _shop;

        /// <summary>显示名称（如"枪械"、"护甲"等）</summary>
        private string _displayName;

        /// <summary>
        /// 初始化交互选项
        /// </summary>
        public void Setup(StockShop shop, string displayName)
        {
            _shop = shop;
            _displayName = displayName;
            this.overrideInteractName = true;
            this._overrideInteractNameKey = displayName;
        }

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                // 禁用碰撞体（作为子交互选项不需要独立碰撞检测）
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                    this.interactCollider.enabled = false;
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                // Start 后重新设置名称（防止被 base.Start 覆盖）
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            return _shop != null;
        }

        /// <summary>
        /// 玩家选择此交互选项时，打开对应分类的商店 UI
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                if (_shop == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] ModeEShopInteractable: _shop 为 null");
                    return;
                }
                _shop.ShowUI();
                ModeEMerchantSellAllUI.Attach(_shop);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ModeEShopInteractable.OnTimeOut 失败: " + e.Message);
            }
        }
    }

    internal static class ModeEMerchantSellAllUI
    {
        private static FieldInfo playerInventoryDisplayField;
        private static FieldInfo characterInventoryDisplayField;
        private static FieldInfo sortButtonField;
        private static MethodInfo sellMethod;
        private static bool reflectionInitialized;

        private static StockShop currentShop;
        private static Inventory currentPlayerInventory;
        private static GameObject sellAllButtonObject;
        private static Button sellAllButton;
        private static TextMeshProUGUI sellAllButtonText;
        private static bool isSelling;

        internal static void Attach(StockShop shop)
        {
            Cleanup();

            if (shop == null || string.IsNullOrEmpty(shop.MerchantID) || !shop.MerchantID.StartsWith("ModeE_", StringComparison.Ordinal))
            {
                return;
            }

            InitializeReflection();
            currentShop = shop;
            BindPlayerInventory();
            RegisterEvents();
            CreateSellAllButton();
            UpdateButtonState();
        }

        private static void InitializeReflection()
        {
            if (reflectionInitialized)
            {
                return;
            }

            BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
            playerInventoryDisplayField = typeof(StockShopView).GetField("playerInventoryDisplay", privateInstance);
            characterInventoryDisplayField = typeof(StockShopView).GetField("characterInventoryDisplay", privateInstance);
            sortButtonField = typeof(Duckov.UI.InventoryDisplay).GetField("sortButton", privateInstance);
            sellMethod = typeof(StockShop).GetMethod("Sell", privateInstance, null, new Type[] { typeof(Item) }, null);
            reflectionInitialized = true;
        }

        private static void RegisterEvents()
        {
            StockShop.OnAfterItemSold += OnAfterItemSold;
            ManagedUIElement.onClose += OnManagedUIElementClose;
        }

        private static void UnregisterEvents()
        {
            StockShop.OnAfterItemSold -= OnAfterItemSold;
            ManagedUIElement.onClose -= OnManagedUIElementClose;
        }

        private static void BindPlayerInventory()
        {
            if (currentPlayerInventory != null)
            {
                currentPlayerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                currentPlayerInventory = null;
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.CharacterItem != null)
                {
                    currentPlayerInventory = player.CharacterItem.Inventory;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 绑定玩家背包失败: " + e.Message);
            }

            if (currentPlayerInventory != null)
            {
                currentPlayerInventory.onContentChanged += OnPlayerInventoryContentChanged;
            }
        }

        private static void CreateSellAllButton()
        {
            StockShopView shopView = StockShopView.Instance;
            if (shopView == null || shopView.Target != currentShop)
            {
                return;
            }

            try
            {
                Duckov.UI.InventoryDisplay playerInventoryDisplay = null;
                if (playerInventoryDisplayField != null)
                {
                    playerInventoryDisplay = playerInventoryDisplayField.GetValue(shopView) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null && characterInventoryDisplayField != null)
                {
                    playerInventoryDisplay = characterInventoryDisplayField.GetValue(shopView) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取玩家背包 InventoryDisplay，跳过创建一键卖出按钮");
                    return;
                }

                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(playerInventoryDisplay) as Button;
                }

                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取整理按钮，跳过创建一键卖出按钮");
                    return;
                }

                sellAllButtonObject = UnityEngine.Object.Instantiate(sortButton.gameObject, sortButton.transform.parent);
                sellAllButtonObject.name = "ModeEMerchantSellAllButton";
                sellAllButtonObject.SetActive(true);

                RectTransform rt = sellAllButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition + new Vector2(0f, sortRt.sizeDelta.y + 8f);
                    rt.sizeDelta = new Vector2(sortRt.sizeDelta.x + 80f, sortRt.sizeDelta.y);
                }

                LayoutElement layoutElement = sellAllButtonObject.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    if (layoutElement.preferredWidth > 0f)
                    {
                        layoutElement.preferredWidth += 80f;
                    }

                    if (layoutElement.minWidth > 0f)
                    {
                        layoutElement.minWidth += 80f;
                    }
                }

                ContentSizeFitter contentSizeFitter = sellAllButtonObject.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                sellAllButton = sellAllButtonObject.GetComponent<Button>();
                if (sellAllButton != null)
                {
                    sellAllButton.onClick.RemoveAllListeners();
                    sellAllButton.onClick.AddListener(OnSellAllButtonClicked);
                }

                sellAllButtonText = sellAllButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                UpdateButtonState();

                ModBehaviour.DevLog("[ModeE] 一键卖出按钮创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 创建一键卖出按钮失败: " + e.Message);
            }
        }

        private static void UpdateButtonState()
        {
            if (sellAllButton == null)
            {
                return;
            }

            string sellAllLabel = L10n.T("一键卖出", "Sell All");
            string displayText;
            bool interactable;
            Color buttonColor;

            if (isSelling)
            {
                displayText = L10n.T("卖出中...", "Selling...");
                interactable = false;
                buttonColor = Color.gray;
            }
            else
            {
                int itemCount = CountSellableInventoryItems();
                bool canSell = currentShop != null && itemCount > 0;
                displayText = canSell ? sellAllLabel + " (" + itemCount + ")" : sellAllLabel;
                interactable = canSell;
                buttonColor = canSell ? new Color(0.2f, 0.8f, 0.2f) : Color.gray;
            }

            ApplyButtonState(sellAllButton, sellAllButtonObject, sellAllButtonText, displayText, interactable, buttonColor);
        }

        private static int CountSellableInventoryItems()
        {
            if (currentPlayerInventory == null)
            {
                BindPlayerInventory();
            }

            if (currentPlayerInventory == null || currentPlayerInventory.Content == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < currentPlayerInventory.Content.Count; i++)
            {
                if (IsInventoryIndexLocked(currentPlayerInventory, i))
                {
                    continue;
                }

                Item item = currentPlayerInventory.Content[i];
                if (item != null && item.CanBeSold && !IsItemWishlisted(item))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<Item> CollectSellableInventoryItems()
        {
            List<Item> items = new List<Item>();

            if (currentPlayerInventory == null)
            {
                BindPlayerInventory();
            }

            if (currentPlayerInventory == null || currentPlayerInventory.Content == null)
            {
                return items;
            }

            for (int i = 0; i < currentPlayerInventory.Content.Count; i++)
            {
                if (IsInventoryIndexLocked(currentPlayerInventory, i))
                {
                    continue;
                }

                Item item = currentPlayerInventory.Content[i];
                if (item != null && item.CanBeSold && !IsItemWishlisted(item))
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static void OnSellAllButtonClicked()
        {
            if (currentShop == null || isSelling)
            {
                return;
            }

            SellAllInventoryItemsAsync().Forget();
        }

        private static async UniTaskVoid SellAllInventoryItemsAsync()
        {
            StockShop targetShop = currentShop;
            if (targetShop == null)
            {
                UpdateButtonState();
                return;
            }

            List<Item> itemsToSell = CollectSellableInventoryItems();
            if (itemsToSell.Count <= 0)
            {
                UpdateButtonState();
                return;
            }

            isSelling = true;
            UpdateButtonState();

            int soldCount = 0;
            int failedCount = 0;

            try
            {
                for (int i = 0; i < itemsToSell.Count; i++)
                {
                    Item item = itemsToSell[i];
                    if (item == null)
                    {
                        continue;
                    }

                    try
                    {
                        await SellItemAsync(targetShop, item);
                        soldCount++;
                    }
                    catch (Exception e)
                    {
                        failedCount++;
                        ModBehaviour.DevLog("[ModeE] [WARNING] 一键卖出失败: " + item.DisplayName + ", " + e.Message);
                    }
                }

                if (soldCount > 0 && failedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已卖出 " + soldCount + " 件物品，" + failedCount + " 件未能卖出",
                        "Sold " + soldCount + " items, " + failedCount + " could not be sold"));
                }
                else if (soldCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已卖出 " + soldCount + " 件物品",
                        "Sold " + soldCount + " items"));
                }
                else
                {
                    NotificationText.Push(L10n.T(
                        "没有物品被卖出",
                        "No items were sold"));
                }
            }
            finally
            {
                isSelling = false;
                UpdateButtonState();
            }
        }

        private static async UniTask SellItemAsync(StockShop shop, Item item)
        {
            if (shop == null)
            {
                throw new InvalidOperationException("shop is null");
            }

            if (item == null)
            {
                throw new InvalidOperationException("item is null");
            }

            if (sellMethod == null)
            {
                throw new MissingMethodException(typeof(StockShop).FullName, "Sell");
            }

            object taskObject = sellMethod.Invoke(shop, new object[] { item });
            if (!(taskObject is UniTask))
            {
                throw new InvalidOperationException("StockShop.Sell did not return UniTask");
            }

            await (UniTask)taskObject;
        }

        private static void OnPlayerInventoryContentChanged(Inventory inventory, int index)
        {
            UpdateButtonState();
        }

        private static void OnAfterItemSold(StockShop shop)
        {
            if (shop != currentShop)
            {
                return;
            }

            UpdateButtonState();
        }

        private static void OnManagedUIElementClose(ManagedUIElement element)
        {
            StockShopView shopView = element as StockShopView;
            if (shopView == null || shopView.Target != currentShop)
            {
                return;
            }

            Cleanup();
        }

        private static void Cleanup()
        {
            UnregisterEvents();

            if (currentPlayerInventory != null)
            {
                currentPlayerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                currentPlayerInventory = null;
            }

            if (sellAllButtonObject != null)
            {
                UnityEngine.Object.Destroy(sellAllButtonObject);
                sellAllButtonObject = null;
            }

            sellAllButton = null;
            sellAllButtonText = null;
            currentShop = null;
            isSelling = false;
        }

        /// <summary>
        /// 静态缓存兜底清理 — 由 ResetModeEMerchantStaticCaches 统一调用。
        /// 作为 Cleanup 的上位兜底，确保反射缓存等静态字段被完整释放。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            Cleanup();
            playerInventoryDisplayField = null;
            characterInventoryDisplayField = null;
            sortButtonField = null;
            sellMethod = null;
            reflectionInitialized = false;
        }

        private static bool IsInventoryIndexLocked(Inventory inventory, int index)
        {
            try
            {
                return inventory != null && inventory.IsIndexLocked(index);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsItemWishlisted(Item item)
        {
            try
            {
                return item != null
                    && ItemWishlist.Instance != null
                    && ItemWishlist.Instance.IsManuallyWishlisted(item.TypeID);
            }
            catch (Exception e)
            {
                // 异常时返回 false（允许卖出），但记录日志便于排障：
                // 若 ItemWishlist.Instance 状态异常或 IsManuallyWishlisted 抛异常，
                // 玩家的愿望清单物品可能被误卖，需要定位原因。
                ModBehaviour.DevLog("[ModeEMerchant] IsItemWishlisted 检查异常，默认允许卖出: " + e.Message);
                return false;
            }
        }

        private static void ApplyButtonState(
            Button targetButton,
            GameObject targetObject,
            TextMeshProUGUI targetText,
            string displayText,
            bool interactable,
            Color buttonColor)
        {
            if (targetButton == null)
            {
                return;
            }

            if (targetText != null)
            {
                targetText.text = displayText;
                targetText.richText = false;
            }
            else if (targetObject != null)
            {
                Text legacyText = targetObject.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = displayText;
                    legacyText.supportRichText = false;
                }
            }

            targetButton.interactable = interactable;
            ColorBlock colors = targetButton.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.1f;
            colors.pressedColor = buttonColor * 0.9f;
            colors.disabledColor = Color.gray;
            targetButton.colors = colors;
        }
    }

    // ========================================================================
    // ModeEPetSpawner — 召唤煤球辅助类
    // ========================================================================

    /// <summary>
    /// Mode E 召唤煤球辅助类。
    /// 提供静态方法用于生成煤球宠物NPC。
    /// </summary>
    public static class ModeEPetSpawner
    {
        /// <summary>缓存的煤球预设（避免重复查找）</summary>
        private static CharacterRandomPreset cachedCoalballPreset = null;

        /// <summary>
        /// 异步生成煤球宠物（供 Harmony patch 调用）
        /// </summary>
        public static void SpawnPet()
        {
            var inst = ModBehaviour.Instance;
            int modeFSessionToken = inst != null ? inst.CurrentModeFSessionToken : 0;
            int modeESessionToken = inst != null ? inst.CurrentModeESessionToken : 0;
            int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            SpawnPetAsync(modeFSessionToken, relatedScene, modeESessionToken, relatedScene).Forget();
        }

        /// <summary>
        /// 清理缓存（Mode E 结束时调用）
        /// </summary>
        public static void ClearCache()
        {
            cachedCoalballPreset = null;
        }

        /// <summary>
        /// 重置宠物NPC的雇佣交互点状态，防止位置哈希导致的状态复用
        /// 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
        /// 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
        /// </summary>
        private static void ResetPetHireInteractable(GameObject petGo)
        {
            try
            {
                if (petGo == null) return;

                // 查找宠物NPC上的所有 InteractableBase 组件
                var interactables = petGo.GetComponentsInChildren<InteractableBase>(true);
                if (interactables == null || interactables.Length == 0)
                {
                    ModBehaviour.DevLog("[ModeE] 煤球NPC上未找到 InteractableBase");
                    return;
                }

                foreach (var interact in interactables)
                {
                    if (interact == null) continue;

                    // 通过反射重置 requireItem 和 requireItemUsed 状态
                    try
                    {
                        // 获取 requireItem 字段
                        var requireItemField = typeof(InteractableBase).GetField("requireItem",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemUsed 字段
                        var requireItemUsedField = typeof(InteractableBase).GetField("requireItemUsed",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemId 字段（用于判断是否是雇佣交互）
                        var requireItemIdField = typeof(InteractableBase).GetField("requireItemId",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (requireItemField != null && requireItemIdField != null)
                        {
                            int itemId = (int)requireItemIdField.GetValue(interact);
                            // 只重置需要 ID=388 物品的交互点（雇佣交互）
                            if (itemId == 388)
                            {
                                requireItemField.SetValue(interact, true);
                                if (requireItemUsedField != null)
                                {
                                    requireItemUsedField.SetValue(interact, false);
                                }
                                ModBehaviour.DevLog("[ModeE] 已重置煤球雇佣交互点状态 (requireItemId=388)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModBehaviour.DevLog("[ModeE] [WARNING] 重置交互点状态失败: " + ex.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ResetPetHireInteractable 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 异步生成煤球NPC
        /// </summary>
        private static async UniTaskVoid SpawnPetAsync(
            int modeFSessionToken,
            int modeFRelatedScene,
            int modeESessionToken,
            int modeESessionRelatedScene)
        {
            try
            {
                // 获取玩家位置
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 召唤煤球：玩家为空");
                    return;
                }

                // 查找煤球预设（优先使用缓存）
                CharacterRandomPreset coalballPreset = cachedCoalballPreset;

                if (coalballPreset == null)
                {
                    // 优先从 ModBehaviour 的缓存字典查找
                    var inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        // 通过反射获取 cachedCharacterPresets（如果可访问）
                        // 回退到 FindObjectsOfTypeAll
                        try
                        {
                            var allPresets = ObjectCache.GetCharacterPresets();
                            foreach (var preset in allPresets)
                            {
                                if (preset == null) continue;
                                try
                                {
                                    string nameKey = preset.nameKey;
                                    if (!string.IsNullOrEmpty(nameKey) && nameKey.Contains("SnowPMC"))
                                    {
                                        coalballPreset = preset;
                                        cachedCoalballPreset = preset; // 缓存以供后续使用
                                        ModBehaviour.DevLog("[ModeE] 找到煤球预设: " + nameKey);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                if (coalballPreset == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 未找到煤球预设 (Character_SnowPMC)，无法召唤");
                    NotificationText.Push(L10n.T("未找到煤球预设", "Coalball preset not found"));
                    return;
                }

                // 在玩家前方生成煤球
                Vector3 spawnPos = player.transform.position + player.transform.forward * 1.5f;
                Vector3 dir = -player.transform.forward;
                var coalballCharacter = await coalballPreset.CreateCharacterAsync(spawnPos, dir, modeFSessionToken > 0 ? modeFRelatedScene : modeESessionRelatedScene, null, false);
                if (coalballCharacter == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 煤球生成失败");
                    return;
                }

                // 设置煤球为玩家阵营
                var inst2 = ModBehaviour.Instance;
                if (inst2 == null ||
                    !inst2.IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene))
                {
                    try
                    {
                        if (coalballCharacter.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(coalballCharacter.gameObject);
                        }
                    }
                    catch { }

                    ModBehaviour.DevLog("[ModeE] 煤球生成完成时模式已结束或场景已切换，已放弃该实例");
                    return;
                }

                if (inst2 != null)
                {
                    coalballCharacter.SetTeam(inst2.ModeEPlayerFaction);
                }
                else
                {
                    coalballCharacter.SetTeam(Teams.player);
                }

                // [修复] 重置煤球NPC的雇佣交互点状态，防止位置哈希导致的状态复用
                // 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
                // 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
                ResetPetHireInteractable(coalballCharacter.gameObject);

                ModBehaviour.DevLog("[ModeE] 煤球召唤成功");
                NotificationText.Push(L10n.T("煤球已召唤！", "Coalball summoned!"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] SpawnPetAsync 失败: " + e.Message);
            }
        }
    }
}

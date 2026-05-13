// ============================================================================
// StorageDepositInventoryQuickDeposit.cs - 背包绑定与一键寄存
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
        /// 将物品写入寄存数据，返回新条目索引。
        /// </summary>
        private static bool TryStoreItemInDepositData(Item item, out int newIndex)
        {
            newIndex = -1;

            if (item == null)
            {
                return false;
            }

            int oldCount = DepositDataManager.GetItemCount();
            DepositDataManager.AddItem(item);

            int currentCount = DepositDataManager.GetItemCount();
            if (currentCount <= oldCount)
            {
                return false;
            }

            newIndex = currentCount - 1;
            return true;
        }

        /// <summary>
        /// 为指定寄存索引恢复并缓存物品实例，供商店 UI 正确显示。
        /// </summary>
        private static async UniTask<bool> CacheDepositedItemInstanceAsync(int depositIndex, string runtimeContext)
        {
            var depositedItems = DepositDataManager.GetAllItems();
            if (depositIndex < 0 || depositIndex >= depositedItems.Count)
            {
                return false;
            }

            var depositedItem = depositedItems[depositIndex];
            if (depositedItem == null || depositedItem.itemData == null)
            {
                return false;
            }

            int typeID = depositedItem.itemData.RootTypeID;
            Item restoredItem = null;

            try
            {
                restoredItem = await ItemTreeData.InstantiateAsync(depositedItem.itemData);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 恢复寄存物品实例失败: index=" + depositIndex + ", error=" + e.Message);
            }

            if (restoredItem == null)
            {
                try
                {
                    restoredItem = ItemAssetsCollection.InstantiateSync(typeID);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 创建寄存物品后备实例失败: index=" + depositIndex + ", error=" + e.Message);
                }
            }

            if (restoredItem == null)
            {
                return false;
            }

            CustomItemRuntimeStateHelper.RestoreRuntimeState(restoredItem, runtimeContext);

            if (!isServiceActive || depositShop == null)
            {
                if (restoredItem.gameObject != null)
                {
                    UnityEngine.Object.Destroy(restoredItem.gameObject);
                }
                return false;
            }

            RegisterDepositItemInstance(depositIndex, typeID, restoredItem);
            return true;
        }

        /// <summary>
        /// 注册寄存物品实例到本地缓存和原版商店缓存。
        /// </summary>
        private static void RegisterDepositItemInstance(int depositIndex, int typeID, Item itemInstance)
        {
            if (itemInstance == null)
            {
                return;
            }

            Item oldInstance;
            if (depositItemInstances.TryGetValue(depositIndex, out oldInstance) &&
                oldInstance != null &&
                oldInstance != itemInstance &&
                oldInstance.gameObject != null)
            {
                UnityEngine.Object.Destroy(oldInstance.gameObject);
            }

            depositItemInstances[depositIndex] = itemInstance;

            if (itemInstancesField != null && depositShop != null)
            {
                var itemInstances = itemInstancesField.GetValue(depositShop) as Dictionary<int, Item>;
                if (itemInstances != null)
                {
                    itemInstances[typeID] = itemInstance;
                }
            }
        }

        /// <summary>
        /// 绑定当前玩家背包，便于实时刷新一键寄存按钮状态。
        /// </summary>
        private static void BindPlayerInventory()
        {
            if (playerInventory != null)
            {
                playerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                playerInventory = null;
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.CharacterItem != null)
                {
                    playerInventory = player.CharacterItem.Inventory;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 绑定玩家背包失败: " + e.Message);
            }

            if (playerInventory != null)
            {
                playerInventory.onContentChanged += OnPlayerInventoryContentChanged;
            }
        }

        /// <summary>
        /// 玩家背包内容变化时更新一键寄存按钮状态。
        /// </summary>
        private static void OnPlayerInventoryContentChanged(Inventory inventory, int index)
        {
            UpdateQuickDepositButtonState();
        }

        /// <summary>
        /// 检查物品是否允许寄存，保持与单件寄存规则一致。
        /// </summary>
        private static bool CanDepositItem(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                return item.CanBeSold;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 检查寄存资格失败，按可寄存处理: " + item.DisplayName + ", " + e.Message);
                return true;
            }
        }

        /// <summary>
        /// 检查背包中某索引处的格子是否被锁定。
        /// </summary>
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

        /// <summary>
        /// 统计玩家背包中的根物品数量。
        /// </summary>
        private static int CountPlayerInventoryItems()
        {
            if (playerInventory == null)
            {
                BindPlayerInventory();
            }

            if (playerInventory == null || playerInventory.Content == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < playerInventory.Content.Count; i++)
            {
                if (IsInventoryIndexLocked(playerInventory, i))
                {
                    continue;
                }
                Item item = playerInventory.Content[i];
                if (item != null && CanDepositItem(item))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 收集玩家背包中的根物品，用于批量寄存。
        /// </summary>
        private static List<Item> CollectPlayerInventoryRootItems(out int blockedCount)
        {
            List<Item> items = new List<Item>();
            blockedCount = 0;

            if (playerInventory == null)
            {
                BindPlayerInventory();
            }

            if (playerInventory == null || playerInventory.Content == null)
            {
                return items;
            }

            for (int i = 0; i < playerInventory.Content.Count; i++)
            {
                if (IsInventoryIndexLocked(playerInventory, i))
                {
                    continue;
                }
                Item item = playerInventory.Content[i];
                if (item != null)
                {
                    if (CanDepositItem(item))
                    {
                        items.Add(item);
                    }
                    else
                    {
                        blockedCount++;
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// 创建玩家背包侧的"一键寄存"按钮。
        /// </summary>
        private static void CreateQuickDepositButton()
        {
            var shopView = StockShopView.Instance;
            if (shopView == null)
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
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 无法获取玩家背包 InventoryDisplay，跳过创建一键寄存按钮");
                    return;
                }

                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(playerInventoryDisplay) as Button;
                }

                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 无法获取玩家背包整理按钮，跳过创建一键寄存按钮");
                    return;
                }

                if (quickDepositButtonObj != null)
                {
                    UnityEngine.Object.Destroy(quickDepositButtonObj);
                    quickDepositButtonObj = null;
                    quickDepositButton = null;
                    quickDepositButtonText = null;
                }

                quickDepositButtonObj = UnityEngine.Object.Instantiate(sortButton.gameObject, sortButton.transform.parent);
                quickDepositButtonObj.name = "StorageQuickDepositButton";
                quickDepositButtonObj.SetActive(true);

                RectTransform rt = quickDepositButtonObj.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition + new Vector2(0f, sortRt.sizeDelta.y + 8f);
                    rt.sizeDelta = new Vector2(sortRt.sizeDelta.x + 80f, sortRt.sizeDelta.y);
                }

                LayoutElement layoutElement = quickDepositButtonObj.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    if (layoutElement.preferredWidth > 0)
                    {
                        layoutElement.preferredWidth += 80f;
                    }

                    if (layoutElement.minWidth > 0)
                    {
                        layoutElement.minWidth += 80f;
                    }
                }

                ContentSizeFitter contentSizeFitter = quickDepositButtonObj.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                quickDepositButton = quickDepositButtonObj.GetComponent<Button>();
                if (quickDepositButton != null)
                {
                    quickDepositButton.onClick.RemoveAllListeners();
                    quickDepositButton.onClick.AddListener(OnQuickDepositButtonClicked);
                }

                quickDepositButtonText = quickDepositButtonObj.GetComponentInChildren<TextMeshProUGUI>();
                UpdateQuickDepositButtonState();

                ModBehaviour.DevLog("[StorageDepositService] 一键寄存按钮创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 创建一键寄存按钮失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 更新一键寄存按钮状态。
        /// </summary>
        private static void UpdateQuickDepositButtonState()
        {
            if (quickDepositButton == null)
            {
                return;
            }

            string quickDepositLabel = L10n.T("一键寄存", "Deposit All");
            string displayText;
            bool interactable;
            Color buttonColor;

            if (isQuickDepositInProgress)
            {
                displayText = L10n.T("寄存中...", "Depositing...");
                interactable = false;
                buttonColor = Color.gray;
            }
            else
            {
                int itemCount = CountPlayerInventoryItems();
                bool canDeposit = isServiceActive && itemCount > 0;

                displayText = canDeposit
                    ? quickDepositLabel + " (" + itemCount + ")"
                    : quickDepositLabel;
                interactable = canDeposit;
                buttonColor = canDeposit ? new Color(0.2f, 0.8f, 0.2f) : Color.gray;
            }

            ApplyButtonState(quickDepositButton, quickDepositButtonObj, quickDepositButtonText, displayText, interactable, buttonColor);
        }

        /// <summary>
        /// 统一应用按钮文字、颜色和可交互状态。
        /// </summary>
        private static void ApplyButtonState(Button targetButton, GameObject targetObject, TextMeshProUGUI targetText, string displayText, bool interactable, Color buttonColor)
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

        /// <summary>
        /// 一键寄存按钮点击事件。
        /// </summary>
        private static void OnQuickDepositButtonClicked()
        {
            if (!isServiceActive || isQuickDepositInProgress)
            {
                return;
            }

            ModBehaviour.DevLog("[StorageDepositService] 一键寄存按钮被点击");
            QuickDepositPlayerInventoryAsync().Forget();
        }

        /// <summary>
        /// 批量将玩家背包中的根物品寄存到阿稳寄存点。
        /// </summary>
        private static async UniTaskVoid QuickDepositPlayerInventoryAsync()
        {
            int blockedCount;
            List<Item> itemsToDeposit = CollectPlayerInventoryRootItems(out blockedCount);
            if (itemsToDeposit.Count == 0)
            {
                if (blockedCount > 0)
                {
                    string blockedMessage = L10n.T("背包里的物品都不符合寄存规则", "No items in inventory are eligible for deposit");
                    NotificationText.Push(blockedMessage);
                }
                else
                {
                    NotificationText.Push(L10n.T("背包里没有可寄存的物品", "No items in inventory to deposit"));
                }
                UpdateQuickDepositButtonState();
                return;
            }

            isQuickDepositInProgress = true;
            UpdateQuickDepositButtonState();

            int depositedCount = 0;
            int failedCount = 0;
            List<int> newIndices = new List<int>();

            try
            {
                for (int i = 0; i < itemsToDeposit.Count; i++)
                {
                    Item item = itemsToDeposit[i];
                    if (item == null)
                    {
                        continue;
                    }

                    string itemName = item.DisplayName;

                    try
                    {
                        int price = GetDepositSellPrice(item);
                        item.Detach();

                        int newIndex = await StoreItemInDepositDataAsync(item, price, "StorageDeposit.QuickDeposit");
                        if (newIndex < 0)
                        {
                            throw new InvalidOperationException("写入寄存数据失败");
                        }

                        newIndices.Add(newIndex);
                        depositedCount++;
                        DestroyOriginalDepositedItem(item, itemName);

                        ModBehaviour.DevLog("[StorageDepositService] 一键寄存成功: " + itemName + ", index=" + newIndex);
                    }
                    catch (Exception e)
                    {
                        failedCount++;
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 一键寄存失败: " + itemName + ", " + e.Message);
                        RestoreItemToPlayerInventory(item, itemName);
                    }
                }

                if (newIndices.Count > 0 && isServiceActive && depositShop != null)
                {
                    for (int i = 0; i < newIndices.Count; i++)
                    {
                        if (!isServiceActive || depositShop == null)
                        {
                            break;
                        }

                        await CacheDepositedItemInstanceAsync(newIndices[i], "StorageDeposit.QuickDeposit");
                    }

                    if (isServiceActive && depositShop != null)
                    {
                        RefreshShopEntries();
                        RefreshShopUI();
                        UpdateRetrieveAllButton();
                        UpdatePriceDisplay();
                    }
                }

                if (depositedCount > 0 && failedCount > 0 && blockedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已寄存 " + depositedCount + " 件物品，" + failedCount + " 件失败，" + blockedCount + " 件不符合寄存规则",
                        "Deposited " + depositedCount + " items, " + failedCount + " failed, " + blockedCount + " were not eligible"));
                }
                else if (depositedCount > 0 && failedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已寄存 " + depositedCount + " 件物品，" + failedCount + " 件未能寄存",
                        "Deposited " + depositedCount + " items, " + failedCount + " could not be deposited"));
                }
                else if (depositedCount > 0 && blockedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已寄存 " + depositedCount + " 件物品，" + blockedCount + " 件不符合寄存规则",
                        "Deposited " + depositedCount + " items, " + blockedCount + " were not eligible"));
                }
                else if (depositedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已寄存 " + depositedCount + " 件物品",
                        "Deposited " + depositedCount + " items"));
                }
                else if (failedCount > 0 && blockedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "没有物品被寄存，" + failedCount + " 件失败，" + blockedCount + " 件不符合寄存规则",
                        "No items were deposited, " + failedCount + " failed, " + blockedCount + " were not eligible"));
                }
                else if (failedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "没有物品被寄存，" + failedCount + " 件未能寄存",
                        "No items were deposited, " + failedCount + " could not be deposited"));
                }
                else if (blockedCount > 0)
                {
                    string blockedMessage = L10n.T("背包里的物品都不符合寄存规则", "No items in inventory are eligible for deposit");
                    NotificationText.Push(blockedMessage);
                }
                else
                {
                    NotificationText.Push(L10n.T(
                        "没有物品被寄存",
                        "No items were deposited"));
                }
            }
            finally
            {
                isQuickDepositInProgress = false;
                UpdateQuickDepositButtonState();
            }
        }

        /// <summary>
        /// 一键寄存成功后销毁原始物品对象，避免残留在场景中。
        /// </summary>
        private static void DestroyOriginalDepositedItem(Item item, string itemName)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                item.DestroyTree();
                return;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] DestroyTree 失败，尝试销毁物体: " + itemName + ", " + e.Message);
            }

            try
            {
                if (item.gameObject != null)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 销毁寄存物品对象失败: " + itemName + ", " + e.Message);
            }
        }

        /// <summary>
        /// 批量寄存失败时，尽量把物品恢复回玩家身上。
        /// </summary>
        private static void RestoreItemToPlayerInventory(Item item, string itemName)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                if (playerInventory == null)
                {
                    BindPlayerInventory();
                }

                if (playerInventory != null && playerInventory.AddAndMerge(item, 0))
                {
                    ModBehaviour.DevLog("[StorageDepositService] 已恢复物品到玩家背包: " + itemName);
                    return;
                }
            }
            catch (Exception restoreEx)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 恢复物品到背包失败: " + itemName + ", " + restoreEx.Message);
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    item.Drop(player, true);
                    ModBehaviour.DevLog("[StorageDepositService] 已将未恢复物品丢到玩家脚下: " + itemName);
                    return;
                }
            }
            catch (Exception dropEx)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 丢弃未恢复物品失败: " + itemName + ", " + dropEx.Message);
            }

            DestroyOriginalDepositedItem(item, itemName);
        }

        /// <summary>
        /// 创建操作按钮区域（复用原有的刷新时间显示组件）
        /// 显示格式：全部取出 ￥xxx | 全部丢弃
        /// </summary>
    }
}

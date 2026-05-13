// ============================================================================
// CourierService_Buttons.cs - send and quick-store button handling
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
    public static partial class CourierService
    {
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
            CreateQuickStoreButton();
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
            bool hasItems = courierInventory != null && courierInventory.GetItemCount() > 0;
            int fee = hasItems ? CalculateDeliveryFee(courierInventory) : 0;
            bool canAfford = hasItems && fee > 0 && CanAffordDeliveryInternal(fee);

            if (sendButton != null)
            {
                string sendText = LocalizationHelper.GetLocalizedText("BossRush_CourierService_Send");
                string emptyText = LocalizationHelper.GetLocalizedText("BossRush_CourierService_Empty");

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
                    bool usePurification = IsZombieModeTemporaryCourierPurificationService(courierNPCTransform);
                    displayText = sendText + " (<color=#FF0000>" + (usePurification ? "净化点 " : "￥") + fee + "</color>)";
                    interactable = false;
                    buttonColor = Color.gray;
                }
                else
                {
                    bool usePurification = IsZombieModeTemporaryCourierPurificationService(courierNPCTransform);
                    displayText = sendText + " (" + (usePurification ? "净化点 " : "￥") + fee + ")";
                    interactable = true;
                    buttonColor = new Color(0.2f, 0.8f, 0.2f);
                }

                ApplyButtonState(sendButton, sendButtonObject, buttonText, displayText, interactable, buttonColor, true);
            }

            if (quickStoreButton != null)
            {
                int transferableCount = CountPlayerInventoryItems();
                bool canQuickStore = courierInventory != null && transferableCount > 0;
                string quickStoreLabel = L10n.T("一键存入", "Store All");
                string quickStoreText = canQuickStore
                    ? quickStoreLabel + " (" + transferableCount + ")"
                    : quickStoreLabel;
                Color quickStoreColor = canQuickStore ? new Color(0.2f, 0.8f, 0.2f) : Color.gray;

                ApplyButtonState(
                    quickStoreButton,
                    quickStoreButtonObject,
                    quickStoreButtonText,
                    quickStoreText,
                    canQuickStore,
                    quickStoreColor,
                    false);
            }
        }

        /// <summary>
        /// 统一应用按钮文案、颜色和交互状态。
        /// </summary>
        private static void ApplyButtonState(
            Button targetButton,
            GameObject targetObject,
            TextMeshProUGUI targetText,
            string displayText,
            bool interactable,
            Color buttonColor,
            bool useRichText)
        {
            if (targetButton == null)
            {
                return;
            }

            if (targetText != null)
            {
                targetText.text = displayText;
                targetText.richText = useRichText;
            }
            else if (targetObject != null)
            {
                Text legacyText = targetObject.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = displayText;
                    legacyText.supportRichText = useRichText;
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
        /// 发送按钮点击事件
        /// </summary>
        private static void OnSendButtonClicked()
        {
            ModBehaviour.DevLog("[CourierService] 发送按钮被点击");
            ExecuteDelivery();
        }

        /// <summary>
        /// 玩家背包侧"一键存入"按钮点击事件。
        /// </summary>
        private static void OnQuickStoreButtonClicked()
        {
            ModBehaviour.DevLog("[CourierService] 一键存入按钮被点击");
            MovePlayerInventoryItemsToCourier();
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
        /// 玩家背包内容变化事件。
        /// </summary>
        private static void OnPlayerInventoryContentChanged(Inventory inventory, int index)
        {
            UpdateButtonState();
        }

        /// <summary>
        /// 绑定当前玩家背包，便于实时刷新一键存入按钮状态。
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
                ModBehaviour.DevLog("[CourierService] [WARNING] 绑定玩家背包失败: " + e.Message);
            }

            if (playerInventory != null)
            {
                playerInventory.onContentChanged += OnPlayerInventoryContentChanged;
            }
        }

        /// <summary>
        /// 统计当前玩家背包里的根物品数量。
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
                if (playerInventory.IsIndexLocked(i))
                {
                    continue;
                }
                if (playerInventory.Content[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 收集玩家背包中的根物品，用于一键搬运到快递箱。
        /// </summary>
        private static List<Item> CollectPlayerInventoryRootItems()
        {
            List<Item> items = new List<Item>();

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
                if (playerInventory.IsIndexLocked(i))
                {
                    continue;
                }
                Item item = playerInventory.Content[i];
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        /// <summary>
        /// 将玩家背包里的根物品批量搬运到快递容器。
        /// </summary>
        private static void MovePlayerInventoryItemsToCourier()
        {
            if (courierInventory == null)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 快递容器不存在，无法执行一键存入");
                return;
            }

            List<Item> itemsToMove = CollectPlayerInventoryRootItems();
            if (itemsToMove.Count == 0)
            {
                NotificationText.Push(L10n.T("背包里没有可存入的物品", "No items in inventory to store"));
                UpdateButtonState();
                return;
            }

            int movedCount = 0;
            int failedCount = 0;

            for (int i = 0; i < itemsToMove.Count; i++)
            {
                Item item = itemsToMove[i];
                if (item == null)
                {
                    continue;
                }

                string itemName = item.DisplayName;

                try
                {
                    item.Detach();

                    bool added = courierInventory.AddAndMerge(item, 0);
                    if (added)
                    {
                        movedCount++;
                        ModBehaviour.DevLog("[CourierService] 一键存入成功: " + itemName);
                        continue;
                    }

                    failedCount++;
                    RestoreItemToPlayerInventory(item, itemName);
                }
                catch (Exception e)
                {
                    failedCount++;
                    ModBehaviour.DevLog("[CourierService] [WARNING] 一键存入失败: " + itemName + ", " + e.Message);
                    RestoreItemToPlayerInventory(item, itemName);
                }
            }

            if (movedCount > 0 && failedCount > 0)
            {
                NotificationText.Push(L10n.T(
                    "已存入 " + movedCount + " 件物品，" + failedCount + " 件未能存入",
                    "Stored " + movedCount + " items, " + failedCount + " could not be stored"));
            }
            else if (movedCount > 0)
            {
                NotificationText.Push(L10n.T(
                    "已存入 " + movedCount + " 件物品到快递箱",
                    "Stored " + movedCount + " items into the courier box"));
            }
            else
            {
                NotificationText.Push(L10n.T(
                    "没有物品存入快递箱，可能已经满了",
                    "No items were stored, the courier box may be full"));
            }

            UpdateButtonState();
        }

        /// <summary>
        /// 一键存入失败时，尽量把物品恢复回玩家身上。
        /// </summary>
        private static void RestoreItemToPlayerInventory(Item item, string itemName)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                if (playerInventory != null && playerInventory.AddAndMerge(item, 0))
                {
                    ModBehaviour.DevLog("[CourierService] 已恢复物品到玩家背包: " + itemName);
                    return;
                }
            }
            catch (Exception restoreEx)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 恢复物品到背包失败: " + itemName + ", " + restoreEx.Message);
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    item.Drop(player, true);
                    ModBehaviour.DevLog("[CourierService] 已将未恢复物品丢到玩家脚下: " + itemName);
                    return;
                }
            }
            catch (Exception dropEx)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 丢弃未恢复物品失败: " + itemName + ", " + dropEx.Message);
            }

            item.DestroyTree();
            ModBehaviour.DevLog("[CourierService] [WARNING] 未恢复物品已销毁: " + itemName);
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


    }
}

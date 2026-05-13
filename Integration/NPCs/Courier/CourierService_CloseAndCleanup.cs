// ============================================================================
// CourierService_CloseAndCleanup.cs - auto delivery, close handling, and cleanup
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
                if (!TryPayDeliveryFee(fee))
                {
                    return false;
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

                // 发送物品到玩家仓库
                foreach (Item item in itemsToSend)
                {
                    item.Detach();  // 先从容器分离
                    ReforgeDataPersistence.SyncCurrentReforgeState(item);

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

                // 立即保存快递数据到存档
                PlayerStorageBuffer.SaveBuffer();
                ModBehaviour.DevLog("[CourierService] ExecuteAutoDelivery: 已保存快递数据到存档");

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
                bool usePurification = IsZombieModeTemporaryCourierPurificationService(npcTransform);
                string bubbleText = usePurification
                    ? L10n.T(
                        "货我已经送出去了，净化点我就收下了",
                        "I've sent the goods, I'll take the Purification Points"
                    )
                    : L10n.T(
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
                catch (Exception ex)
                {
                    NPCExceptionHandler.LogAndIgnore(ex, "CourierService.ReturnItemsToPlayer.GetMainCharacter");
                }
                DropItemsToGround(player);
            }
        }

        /// <summary>
        /// 创建玩家背包侧的"一键存入"按钮。
        /// 为避免覆盖原有整理按钮，按钮会放在整理按钮上方。
        /// </summary>
        private static void CreateQuickStoreButton()
        {
            if (LootView.Instance == null) return;

            try
            {
                Duckov.UI.InventoryDisplay playerInventoryDisplay = null;
                if (playerInventoryDisplayField != null)
                {
                    playerInventoryDisplay = playerInventoryDisplayField.GetValue(LootView.Instance) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null && characterInventoryDisplayField != null)
                {
                    playerInventoryDisplay = characterInventoryDisplayField.GetValue(LootView.Instance) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null)
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 无法获取玩家背包的 InventoryDisplay，跳过创建一键存入按钮");
                    return;
                }

                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(playerInventoryDisplay) as Button;
                }

                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[CourierService] [WARNING] 无法获取玩家背包整理按钮，跳过创建一键存入按钮");
                    return;
                }

                quickStoreButtonObject = UnityEngine.Object.Instantiate(sortButton.gameObject, sortButton.transform.parent);
                quickStoreButtonObject.name = "CourierQuickStoreButton";
                quickStoreButtonObject.SetActive(true);

                RectTransform rt = quickStoreButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition + new Vector2(0f, sortRt.sizeDelta.y + 8f);
                    rt.sizeDelta = new Vector2(sortRt.sizeDelta.x + 80f, sortRt.sizeDelta.y);
                }

                LayoutElement layoutElement = quickStoreButtonObject.GetComponent<LayoutElement>();
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

                ContentSizeFitter contentSizeFitter = quickStoreButtonObject.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                quickStoreButton = quickStoreButtonObject.GetComponent<Button>();
                if (quickStoreButton != null)
                {
                    quickStoreButton.onClick.RemoveAllListeners();
                    quickStoreButton.onClick.AddListener(OnQuickStoreButtonClicked);
                }

                quickStoreButtonText = quickStoreButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                UpdateButtonState();

                ModBehaviour.DevLog("[CourierService] 一键存入按钮创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierService] [WARNING] 创建一键存入按钮失败: " + e.Message + "\n" + e.StackTrace);
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
                            catch (Exception ex)
                            {
                                NPCExceptionHandler.LogAndIgnore(ex, "CourierService.DropItemsToGround.GetMainCharacter");
                            }

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
                    bool usePurification = IsZombieModeTemporaryCourierPurificationService(npcTransform);
                    string currencyTextCn = usePurification ? "净化点 " : "￥";
                    string currencyTextEn = usePurification ? "Purification " : "￥";

                    // 格式：已送达x件物品，共花费x，欢迎下次光临~
                    // x 用红色显示
                    string goodbyeText = L10n.T(
                        "已送达<color=#FF0000>" + lastSentItemCount + "</color>件物品，共花费<color=#FF0000>" + currencyTextCn + lastDeliveryFee + "</color>，欢迎下次光临~",
                        "Delivered <color=#FF0000>" + lastSentItemCount + "</color> items, cost <color=#FF0000>" + currencyTextEn + lastDeliveryFee + "</color>, come again~"
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
                    bool usePurification = IsZombieModeTemporaryCourierPurificationService(npcTransform);
                    string bubbleText = usePurification
                        ? L10n.T(
                            "净化点不够还来浪费爷的时间",
                            "Wasting my time without enough Purification")
                        : L10n.T(
                            "穷小子，没钱还来浪费爷的时间",
                            "Poor kid, wasting my time without money");

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

            if (playerInventory != null)
            {
                playerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                playerInventory = null;
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

            if (quickStoreButtonObject != null)
            {
                UnityEngine.Object.Destroy(quickStoreButtonObject);
                quickStoreButtonObject = null;
                quickStoreButton = null;
                quickStoreButtonText = null;
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

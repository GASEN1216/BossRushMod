// ============================================================================
// StorageDepositBulkActions.cs - 全部取出丢弃与清理
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
                    bool canAfford = CanAffordRetrieveFee(totalFee);

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
                        "<link=retrieve><color={0}><u>{1} {5}{2}</u></color></link> | <link=discard><color={3}><u>{4}</u></color></link>",
                        retrieveColor,
                        retrieveAllLabel,
                        totalFee.ToString("N0"),
                        discardColor,
                        discardAllLabel,
                        GetRetrieveFeePrefix()
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
        /// 检查玩家是否能支付全部取回费用。
        /// 与实际扣费保持同一口径：同时计算账户余额与身上现金。
        /// </summary>
        private static bool CanAffordRetrieveFee(int totalFee)
        {
            if (totalFee <= 0)
            {
                return true;
            }

            try
            {
                if (courierNPCTransform != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
                {
                    return ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(courierNPCTransform, totalFee);
                }

                return new Cost(totalFee).Enough;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 检查取回费用失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// "全部取出"按钮点击事件
        /// </summary>
        private static void OnRetrieveAllClicked()
        {
            if (!isServiceActive) return;
            if (isRetrieveAllInProgress)
            {
                ModBehaviour.DevLog("[StorageDepositService] 全部取出正在进行中，忽略重复点击");
                return;
            }

            int itemCount = DepositDataManager.GetItemCount();
            if (itemCount == 0)
            {
                ModBehaviour.DevLog("[StorageDepositService] 没有物品可取出");
                return;
            }

            int totalFee = CalculateTotalRetrieveFee();
            if (!CanAffordRetrieveFee(totalFee))
            {
                string msg = IsZombieModeTemporaryCourierPurificationService()
                    ? GetTemporaryCourierPurificationInsufficientText()
                    : LocalizationHelper.GetLocalizedText("BossRush_StorageService_InsufficientFunds");
                NotificationText.Push(msg);
                ModBehaviour.DevLog("[StorageDepositService] 全部取出余额不足，已跳过物品恢复");
                return;
            }

            // 执行全部取出
            isRetrieveAllInProgress = true;
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

                var depositedItems = DepositDataManager.GetAllItems();
                List<int> failedRestoreIndices = new List<int>();
                List<RetrieveAllDepositItem> restoredItems =
                    await TryRestoreAllDepositItemsForRetrieveAll(depositedItems, failedRestoreIndices);
                if (restoredItems.Count <= 0)
                {
                    CleanupRestoredRetrieveAllItems(restoredItems);
                    NotificationText.Push(L10n.T("取出失败，寄存物品已保留。", "Retrieve failed. Deposited items were kept."));
                    ModBehaviour.DevLog("[StorageDepositService] 全部取出失败：没有物品恢复成功，失败数=" + failedRestoreIndices.Count);
                    RefreshShopEntries();
                    RefreshShopUI();
                    UpdateRetrieveAllButton();
                    return;
                }

                int payableFee = CalculateRetrieveAllRestoredFee(restoredItems);
                if (!TryPayRetrieveFee(payableFee, "ZombieModeTempCourierDepositRetrieveAll"))
                {
                    CleanupRestoredRetrieveAllItems(restoredItems);
                    string msg = IsZombieModeTemporaryCourierPurificationService()
                        ? GetTemporaryCourierPurificationInsufficientText()
                        : LocalizationHelper.GetLocalizedText("BossRush_StorageService_InsufficientFunds");
                    NotificationText.Push(msg);
                    ModBehaviour.DevLog("[StorageDepositService] 全部取出扣费失败，已保留寄存数据");
                    return;
                }

                List<int> deliveredIndices = new List<int>();
                int failedDeliveryFee = 0;
                int successCount = 0;
                for (int i = 0; i < restoredItems.Count; i++)
                {
                    RetrieveAllDepositItem restored = restoredItems[i];
                    if (restored == null || restored.RestoredItem == null)
                    {
                        continue;
                    }

                    try
                    {
                        ItemUtilities.SendToPlayer(restored.RestoredItem, true, true);
                        deliveredIndices.Add(restored.DepositIndex);
                        successCount++;
                        ModBehaviour.DevLog("[StorageDepositService] 取出物品: " + restored.RestoredItem.DisplayName);
                        restored.RestoredItem = null;
                    }
                    catch (Exception e)
                    {
                        failedDeliveryFee += Mathf.Max(0, restored.Fee);
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 发送取出物品失败: index=" + restored.DepositIndex + ", error=" + e.Message);
                    }
                }

                RefundRetrieveFee(failedDeliveryFee, failedDeliveryFee > 0);
                CleanupRestoredRetrieveAllItems(restoredItems);
                RemoveRetrievedDepositItems(deliveredIndices);
                depositItemInstances.Clear();

                // 刷新商店
                RefreshShopEntries();
                RefreshShopUI();

                // 更新按钮显示
                UpdateRetrieveAllButton();

                // 显示通知
                string notification = failedRestoreIndices.Count > 0 || failedDeliveryFee > 0
                    ? L10n.T("部分物品取出失败，失败物品已保留。", "Some items could not be retrieved and were kept in storage.")
                    : LocalizationHelper.GetLocalizedText("BossRush_StorageService_Retrieved");
                NotificationText.Push(notification);

                ModBehaviour.DevLog("[StorageDepositService] 全部取出完成，共 " + successCount + " 件物品，恢复失败=" + failedRestoreIndices.Count + "，发送失败退款=" + failedDeliveryFee);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 全部取出失败: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                isRetrieveAllInProgress = false;
            }
        }

        private static async UniTask<List<RetrieveAllDepositItem>> TryRestoreAllDepositItemsForRetrieveAll(
            List<DepositedItemData> depositedItems,
            List<int> failedRestoreIndices)
        {
            List<RetrieveAllDepositItem> restoredItems = new List<RetrieveAllDepositItem>();
            if (depositedItems == null)
            {
                return restoredItems;
            }

            for (int i = depositedItems.Count - 1; i >= 0; i--)
            {
                DepositedItemData depositedItem = depositedItems[i];
                if (depositedItem == null || depositedItem.itemData == null)
                {
                    failedRestoreIndices.Add(i);
                    continue;
                }

                try
                {
                    Item restoredItem = await ItemTreeData.InstantiateAsync(depositedItem.itemData);
                    if (restoredItem == null)
                    {
                        failedRestoreIndices.Add(i);
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 取出物品恢复为空: index=" + i);
                        continue;
                    }

                    CustomItemRuntimeStateHelper.RestoreRuntimeState(restoredItem, "StorageDeposit.RetrieveAll");
                    RetrieveAllDepositItem restored = new RetrieveAllDepositItem();
                    restored.DepositIndex = i;
                    restored.DepositData = depositedItem;
                    restored.RestoredItem = restoredItem;
                    restored.Fee = depositedItem.GetCurrentFee();
                    restoredItems.Add(restored);
                }
                catch (Exception e)
                {
                    failedRestoreIndices.Add(i);
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 取出物品失败: index=" + i + ", error=" + e.Message);
                }
            }

            return restoredItems;
        }

        private static int CalculateRetrieveAllRestoredFee(List<RetrieveAllDepositItem> restoredItems)
        {
            int total = 0;
            if (restoredItems == null)
            {
                return total;
            }

            for (int i = 0; i < restoredItems.Count; i++)
            {
                RetrieveAllDepositItem restored = restoredItems[i];
                if (restored != null)
                {
                    total += Mathf.Max(0, restored.Fee);
                }
            }

            return total;
        }

        private static bool TryPayRetrieveFee(int totalFee, string reason)
        {
            if (totalFee <= 0)
            {
                return true;
            }

            if (courierNPCTransform != null &&
                ModBehaviour.Instance != null &&
                ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
            {
                return ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                    courierNPCTransform,
                    totalFee,
                    reason);
            }

            Cost cost = new Cost(totalFee);
            if (!cost.Enough)
            {
                return false;
            }

            cost.Pay();
            return true;
        }

        private static void RefundRetrieveFee(int totalFee, bool shouldRefund)
        {
            if (!shouldRefund || totalFee <= 0)
            {
                return;
            }

            try
            {
                if (courierNPCTransform != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
                {
                    ModBehaviour.Instance.RefundZombieModePurificationPointsForRealNpc(courierNPCTransform, totalFee, true);
                    return;
                }

                EconomyManager.Add(totalFee);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 全部取出退款失败: " + e.Message);
            }
        }

        private static void RemoveRetrievedDepositItems(List<int> depositIndices)
        {
            if (depositIndices == null || depositIndices.Count <= 0)
            {
                return;
            }

            depositIndices.Sort();
            for (int i = depositIndices.Count - 1; i >= 0; i--)
            {
                DepositDataManager.RemoveItem(depositIndices[i]);
            }
        }

        private static void CleanupRestoredRetrieveAllItems(List<RetrieveAllDepositItem> restoredItems)
        {
            if (restoredItems == null)
            {
                return;
            }

            for (int i = 0; i < restoredItems.Count; i++)
            {
                RetrieveAllDepositItem restored = restoredItems[i];
                if (restored == null || restored.RestoredItem == null)
                {
                    continue;
                }

                try
                {
                    restored.RestoredItem.DestroyTree();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 清理未发放取出物品失败: " + e.Message);
                    try
                    {
                        if (restored.RestoredItem.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(restored.RestoredItem.gameObject);
                        }
                    }
                    catch (Exception destroyEx)
                    {
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 销毁未发放取出物品失败: " + destroyEx.Message);
                    }
                }

                restored.RestoredItem = null;
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

            if (quickDepositButtonObj != null)
            {
                UnityEngine.Object.Destroy(quickDepositButtonObj);
                quickDepositButtonObj = null;
            }

            quickDepositButton = null;
            quickDepositButtonText = null;
        }

        /// <summary>
        /// 更新详情面板的价格显示
        /// 寄存时显示 0（免费），取回时显示正确的寄存费
        /// 【修复】不再硬编码为 "0"，而是根据当前选中的条目显示正确的取回费用
        /// </summary>
        private static void UpdatePriceDisplay()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || priceTextField == null) return;

                var priceText = priceTextField.GetValue(shopView) as TMPro.TextMeshProUGUI;
                if (priceText == null) return;

                // 获取当前选中条目对应的正确寄存费
                int fee = 0;
                var selectedEntry = shopView.GetSelection();
                if (selectedEntry != null && selectedEntry.Target != null)
                {
                    int depositIndex;
                    if (entryIndexMapping.TryGetValue(selectedEntry.Target, out depositIndex))
                    {
                        var depositedItems = DepositDataManager.GetAllItems();
                        if (depositIndex >= 0 && depositIndex < depositedItems.Count)
                        {
                            fee = depositedItems[depositIndex].GetCurrentFee();
                        }
                    }
                }

                priceText.text = FormatRetrieveFeeText(fee);
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "StorageDepositService.UpdatePriceDisplay");
            }
        }

        private static string FormatRetrieveFeeText(int fee)
        {
            return IsZombieModeTemporaryCourierPurificationService()
                ? GetRetrieveFeePrefix() + fee.ToString("N0")
                : fee.ToString("N0");
        }

        private static string GetRetrieveFeePrefix()
        {
            return IsZombieModeTemporaryCourierPurificationService()
                ? L10n.T("净化点 ", "Purification ")
                : "￥";
        }

        private static bool IsZombieModeTemporaryCourierPurificationService()
        {
            return courierNPCTransform != null &&
                   ModBehaviour.Instance != null &&
                   ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform);
        }

        private static string GetTemporaryCourierPurificationInsufficientText()
        {
            return L10n.T("净化点不足。", "Not enough Purification.");
        }

        private static IEnumerator UpdateSingleRetrieveUiNextFrame()
        {
            yield return null;
            UpdateSingleRetrieveUi();
        }

        private static void UpdateSingleRetrieveUiDeferred()
        {
            if (!IsZombieModeTemporaryCourierPurificationService() || ModBehaviour.Instance == null)
            {
                return;
            }

            ModBehaviour.Instance.StartCoroutine(UpdateSingleRetrieveUiNextFrame());
        }

        private static void UpdateSingleRetrieveUi()
        {
            if (!IsZombieModeTemporaryCourierPurificationService())
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

                int fee = 0;
                var selectedEntry = shopView.GetSelection();
                if (selectedEntry != null && selectedEntry.Target != null)
                {
                    int depositIndex;
                    if (entryIndexMapping.TryGetValue(selectedEntry.Target, out depositIndex))
                    {
                        var depositedItems = DepositDataManager.GetAllItems();
                        if (depositIndex >= 0 && depositIndex < depositedItems.Count)
                        {
                            fee = depositedItems[depositIndex].GetCurrentFee();
                        }
                    }
                }

                bool canAfford = fee <= 0 || ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(courierNPCTransform, fee);

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
                        interactionText.text = canAfford
                            ? L10n.T("取回（净化点）", "Retrieve (Purification)")
                            : L10n.T("净化点不足", "Not enough Purification");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 更新单件取回净化点 UI 失败: " + e.Message);
            }
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

            if (playerInventory != null)
            {
                playerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                playerInventory = null;
            }

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
            isQuickDepositInProgress = false;
            isRetrieveAllInProgress = false;

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

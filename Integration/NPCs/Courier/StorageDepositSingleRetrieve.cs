// ============================================================================
// StorageDepositSingleRetrieve.cs - 单件取回事务
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
        private static void OnItemPurchased(StockShop shop, Item purchasedItem)
        {
            // 检查是否是我们的商店
            if (shop != depositShop) return;
            if (!isServiceActive) return;

            ModBehaviour.DevLog("[StorageDepositService] 检测到购买事件，物品: " +
                (purchasedItem != null ? purchasedItem.DisplayName : "null"));

            if (purchasedItem == null) return;

            try
            {
                // 通过 StockShopView.GetSelection() 获取当前选中的条目，然后通过 entryIndexMapping 获取正确的索引
                // 这样可以正确处理同类型物品（如多张 Boss Rush 船票）
                int depositIndex = -1;
                var shopView = StockShopView.Instance;
                if (shopView != null)
                {
                    var selectedEntry = shopView.GetSelection();
                    if (selectedEntry != null && selectedEntry.Target != null)
                    {
                        // 通过 entryIndexMapping 获取正确的索引
                        if (entryIndexMapping.TryGetValue(selectedEntry.Target, out depositIndex))
                        {
                            ModBehaviour.DevLog("[StorageDepositService] 通过 entryIndexMapping 找到索引: " + depositIndex);
                        }
                    }
                }

                // 如果无法通过选中条目获取索引，回退到 TypeID 查找（兼容性）
                if (depositIndex < 0)
                {
                    depositIndex = FindDepositedItemIndex(purchasedItem.TypeID);
                    ModBehaviour.DevLog("[StorageDepositService] 回退到 TypeID 查找，索引: " + depositIndex);
                }

                if (depositIndex >= 0)
                {
                    // 获取保存的完整物品数据
                    var depositedItems = DepositDataManager.GetAllItems();
                    var depositedItemData = depositedItems[depositIndex];

                    if (depositedItemData != null && depositedItemData.itemData != null)
                    {
                        int fee = depositedItemData.GetCurrentFee();
                        bool usePurification = IsZombieModeTemporaryCourierPurificationService();
                        if (usePurification &&
                            fee > 0 &&
                            (ModBehaviour.Instance == null ||
                             !ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(courierNPCTransform, fee)))
                        {
                            RollbackTemporarySingleRetrievePlaceholder(purchasedItem);
                            NotificationText.Push(L10n.T("净化点不足。", "Not enough purification."));
                            return;
                        }

                        // 异步恢复完整物品（包含配件和容器内容）
                        RestoreAndReplaceItemAsync(
                            purchasedItem,
                            depositedItemData.itemData,
                            depositIndex,
                            usePurification ? fee : 0).Forget();
                    }
                    else
                    {
                        // 数据异常，直接移除记录
                        DepositDataManager.RemoveItem(depositIndex);
                        RefreshShopEntries();

                        string msg = L10n.T("物品已取回", "Item retrieved");
                        NotificationText.Push(msg);
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到对应的寄存记录");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 处理购买事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 异步恢复完整物品并替换商店给的空白物品
        /// </summary>
        private static async UniTaskVoid RestoreAndReplaceItemAsync(Item emptyItem, ItemTreeData savedData, int depositIndex, int purificationFee = 0)
        {
            Item restoredItem = null;
            bool useTemporaryPurificationRetrieve = IsZombieModeTemporaryCourierPurificationService();
            bool usePurificationPayment = purificationFee > 0 && useTemporaryPurificationRetrieve;
            bool purificationPaymentDeducted = false;
            bool deliveryCompleted = false;

            try
            {
                ModBehaviour.DevLog("[StorageDepositService] 开始恢复完整物品数据...");

                // 使用 ItemTreeData.InstantiateAsync 恢复完整物品（包含配件、容器内容等）
                restoredItem = await ItemTreeData.InstantiateAsync(savedData);

                if (restoredItem == null)
                {
                    if (useTemporaryPurificationRetrieve)
                    {
                        ModBehaviour.DevLog("[StorageDepositService] 临时阿稳单件取回恢复失败，已保留寄存数据");
                        RollbackTemporarySingleRetrievePlaceholder(emptyItem);
                        NotificationText.Push(L10n.T("取回失败，寄存物品已保留。", "Retrieve failed. Deposited item was kept."));
                        return;
                    }

                    ModBehaviour.DevLog("[StorageDepositService] [ERROR] 恢复物品失败，保留空白物品");
                    DepositDataManager.RemoveItem(depositIndex);
                    RebuildItemInstancesIndex(depositIndex);
                    RefreshShopEntries();
                    // 【关键修复】恢复失败时也需要刷新 UI
                    RefreshShopUI();
                    UpdateRetrieveAllButton();
                    return;
                }

                CustomItemRuntimeStateHelper.RestoreRuntimeState(restoredItem, "StorageDeposit.RestoreAndReplace");
                string restoredItemName = restoredItem.DisplayName;
                ModBehaviour.DevLog("[StorageDepositService] 物品恢复成功: " + restoredItemName);

                if (usePurificationPayment)
                {
                    if (ModBehaviour.Instance == null ||
                        !ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                            courierNPCTransform,
                            purificationFee,
                            "ZombieModeTempCourierDepositRetrieveSingle"))
                    {
                        CleanupSingleRetrievedItem(restoredItem);
                        restoredItem = null;
                        RollbackTemporarySingleRetrievePlaceholder(emptyItem);
                        NotificationText.Push(L10n.T("净化点不足。", "Not enough purification."));
                        return;
                    }

                    purificationPaymentDeducted = true;
                }

                // 销毁空白物品
                if (emptyItem != null)
                {
                    // 先从背包移除
                    emptyItem.Detach();
                    UnityEngine.Object.Destroy(emptyItem.gameObject);
                    ModBehaviour.DevLog("[StorageDepositService] 已销毁空白物品");
                }

                // 将恢复的物品发送给玩家（优先背包，满了放仓库或掉落）
                // 使用 dontMerge=true 避免物品被合并
                ItemUtilities.SendToPlayer(restoredItem, true, true);
                deliveryCompleted = true;
                ModBehaviour.DevLog("[StorageDepositService] 已将恢复的物品发送给玩家");
                restoredItem = null;

                // 从寄存数据移除
                DepositDataManager.RemoveItem(depositIndex);

                // 重建物品实例缓存索引（移除后索引会变化）
                RebuildItemInstancesIndex(depositIndex);

                // 刷新商店列表
                RefreshShopEntries();

                // 【关键修复】购买后必须刷新整个 UI，而不是仅隐藏无效条目
                // 原因：RefreshShopEntries() 会重建 entryIndexMapping，创建新的 Entry 对象
                // 但 UI 中的 StockShopItemEntry.Target 仍然指向旧的 Entry 对象
                // 导致 HideInvalidUIEntries() 中 validEntries.Contains(stockEntry) 全部返回 false
                // 所以必须调用 RefreshShopUI() 让 UI 重新绑定到新的 Entry 对象
                RefreshShopUI();

                // 更新"全部取出"按钮
                UpdateRetrieveAllButton();

                // 显示通知（使用本地化键）
                string msg = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_Retrieved");
                NotificationText.Push(msg);

                ModBehaviour.DevLog("[StorageDepositService] 物品取回完成: " + restoredItemName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 恢复物品失败: " + e.Message + "\n" + e.StackTrace);

                if (useTemporaryPurificationRetrieve)
                {
                    bool shouldRefund = purificationPaymentDeducted && !deliveryCompleted;
                    if (ModBehaviour.Instance != null)
                    {
                        ModBehaviour.Instance.RefundZombieModePurificationPointsForRealNpc(courierNPCTransform, purificationFee, shouldRefund);
                    }

                    CleanupSingleRetrievedItem(restoredItem);
                    RollbackTemporarySingleRetrievePlaceholder(emptyItem);
                    NotificationText.Push(L10n.T("取回失败，寄存物品已保留。", "Retrieve failed. Deposited item was kept."));
                    return;
                }

                // 出错时也要移除记录，避免数据不一致
                DepositDataManager.RemoveItem(depositIndex);
                RebuildItemInstancesIndex(depositIndex);
                RefreshShopEntries();
                // 【关键修复】出错时也需要刷新 UI
                RefreshShopUI();
                UpdateRetrieveAllButton();
            }
        }

        private static void RollbackTemporarySingleRetrievePlaceholder(Item placeholderItem)
        {
            try
            {
                if (placeholderItem != null)
                {
                    placeholderItem.Detach();
                    if (placeholderItem.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(placeholderItem.gameObject);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 回滚单件取回占位物失败: " + e.Message);
            }

            RefreshShopEntries();
            RefreshShopUI();
            UpdateRetrieveAllButton();
            UpdatePriceDisplay();
            UpdateSingleRetrieveUiDeferred();
        }

        private static void CleanupSingleRetrievedItem(Item restoredItem)
        {
            if (restoredItem == null)
            {
                return;
            }

            try
            {
                restoredItem.DestroyTree();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 清理单件取回恢复物失败: " + e.Message);
                try
                {
                    if (restoredItem.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(restoredItem.gameObject);
                    }
                }
                catch (Exception destroyEx)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 销毁单件取回恢复物失败: " + destroyEx.Message);
                }
            }
        }

        /// <summary>
        /// 重建物品实例缓存索引（移除物品后调用）
        /// 当移除索引 N 的物品后，所有索引 > N 的物品需要向前移动
        /// </summary>
        private static void RebuildItemInstancesIndex(int removedIndex)
        {
            // 创建新的字典
            var newInstances = new Dictionary<int, Item>();

            foreach (var kvp in depositItemInstances)
            {
                int oldIndex = kvp.Key;
                Item item = kvp.Value;

                if (oldIndex == removedIndex)
                {
                    // 被移除的物品，销毁实例
                    if (item != null && item.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(item.gameObject);
                    }
                    continue;
                }

                // 索引大于被移除的索引，需要减 1
                int newIndex = oldIndex > removedIndex ? oldIndex - 1 : oldIndex;
                newInstances[newIndex] = item;
            }

            depositItemInstances = newInstances;
            ModBehaviour.DevLog("[StorageDepositService] 重建物品实例索引完成，剩余 " + depositItemInstances.Count + " 个");
        }

        /// <summary>
        /// 查找寄存物品索引（根据 TypeID）
        /// </summary>
        private static int FindDepositedItemIndex(int typeID)
        {
            var items = DepositDataManager.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].itemData != null && items[i].itemData.RootTypeID == typeID)
                {
                    return i;
                }
            }
            return -1;
        }

        // ============================================================================
        // 私有方法 - UI 修改
        // ============================================================================

        /// <summary>
        /// 一次性修改商店 UI 文字
        /// </summary>
    }
}

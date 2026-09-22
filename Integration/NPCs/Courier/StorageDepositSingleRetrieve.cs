// 自有寄存商店在官方 Buy 入口接管交易，完整物品恢复成功后才收费和交付。
using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    [HarmonyLib.HarmonyPatch(typeof(StockShop), "Buy")]
    internal static class StorageDepositBuyPatch
    {
        [HarmonyLib.HarmonyPrefix]
        private static bool Prefix(StockShop __instance, int itemTypeID, int amount, ref UniTask<bool> __result)
        {
            if (!StorageDepositService.OwnsShop(__instance)) return true;
            __result = StorageDepositService.RetrieveSingleAsync(itemTypeID, amount);
            return false;
        }
    }

    public static partial class StorageDepositService
    {
        internal static async UniTask<bool> RetrieveSingleAsync(int itemTypeID, int amount)
        {
            var selection = StockShopView.Instance != null ? StockShopView.Instance.GetSelection() : null;
            int index;
            if (amount != 1 || selection == null || selection.Target == null
                || !entryIndexMapping.TryGetValue(selection.Target, out index)) return false;
            var records = DepositDataManager.GetAllItems();
            if (index < 0 || index >= records.Count) return false;
            DepositedItemData record = records[index];
            if (record == null || record.itemData == null || record.itemData.RootTypeID != itemTypeID) return false;
            DepositTransaction transaction = TryBeginTransaction();
            if (transaction == null) return false;
            Item restoredItem = null;
            bool paid = false;
            bool delivered = false;
            int fee = Mathf.Max(0, record.GetCurrentFee());
            try
            {
                restoredItem = await ItemTreeData.InstantiateAsync(record.itemData);
                if (!IsCurrentTransaction(transaction) || DepositDataManager.IndexOf(record) < 0) return false;
                if (restoredItem == null) return false;
                CustomItemRuntimeStateHelper.RestoreRuntimeState(restoredItem, "StorageDeposit.RetrieveSingle");
                if (!TryPayRetrieveFee(fee, "ZombieModeTempCourierDepositRetrieveSingle", transaction)) return false;
                paid = true;
                delivered = TryDeliverRetrievedItem(restoredItem);
                if (!delivered) return false;
                restoredItem = null;
                int removedIndex = DepositDataManager.IndexOf(record);
                DepositDataManager.RemoveItem(record);
                RebuildItemInstancesIndex(removedIndex);
                RefreshShopEntries();
                RefreshShopUI();
                UpdateRetrieveAllButton();
                NotificationText.Push(L10n.T("物品已取回", "Item retrieved"));
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] 单件取回失败，未交付记录保留: " + e.Message);
                return delivered;
            }
            finally
            {
                if (paid && !delivered) RefundRetrieveFee(fee, true, transaction);
                CleanupSingleRetrievedItem(restoredItem);
                if (IsCurrentTransaction(transaction) && !delivered)
                    NotificationText.Push(L10n.T("取回失败，寄存物品已保留。", "Retrieve failed. Deposited item was kept."));
                EndTransaction(transaction);
            }
        }

        private static bool TryDeliverRetrievedItem(Item item)
        {
            try
            {
                ItemUtilities.SendToPlayer(item, true, true);
                return true;
            }
            catch (Exception e)
            {
                // 官方通知可能在实际入包/入仓后抛错；已交付的实例不得退款或销毁。
                bool delivered = item == null || item.InInventory != null || item.PluggedIntoSlot != null;
                ModBehaviour.DevLog("[StorageDepositService] 交付通知异常，delivered=" + delivered + ": " + e.Message);
                return delivered;
            }
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

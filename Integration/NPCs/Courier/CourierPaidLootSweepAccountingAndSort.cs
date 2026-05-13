using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public static partial class CourierPaidLootSweepService
    {
        private static void SortSweepResultInventory(Inventory resultInventory)
        {
            if (resultInventory == null || resultInventory.Content == null)
            {
                return;
            }

            List<Item> items = new List<Item>();
            for (int i = 0; i < resultInventory.Content.Count; i++)
            {
                Item item = resultInventory.Content[i];
                if (item != null)
                {
                    items.Add(item);
                }
            }

            if (items.Count <= 1)
            {
                return;
            }

            items.Sort(CompareSweepResultItems);

            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    items[i].Detach();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 排序前 Detach 失败: " + e.Message);
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null)
                {
                    continue;
                }

                try
                {
                    if (resultInventory.AddAndMerge(item, 0))
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 排序后回填失败: " + e.Message);
                }

                try
                {
                    ItemUtilities.SendToPlayer(item, true, true);
                }
                catch (Exception sendEx)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 排序失败物品回退给玩家失败: " + sendEx.Message);
                }
            }
        }

        private static int CompareSweepResultItems(Item left, Item right)
        {
            if (object.ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int leftQuality = GetSweepItemQuality(left);
            int rightQuality = GetSweepItemQuality(right);
            int qualityCompare = rightQuality.CompareTo(leftQuality);
            if (qualityCompare != 0)
            {
                return qualityCompare;
            }

            int leftValue = GetSweepItemSortValue(left);
            int rightValue = GetSweepItemSortValue(right);
            int valueCompare = rightValue.CompareTo(leftValue);
            if (valueCompare != 0)
            {
                return valueCompare;
            }

            string leftName = string.Empty;
            string rightName = string.Empty;
            try { leftName = left.DisplayName ?? string.Empty; } catch {}
            try { rightName = right.DisplayName ?? string.Empty; } catch {}
            return string.Compare(leftName, rightName, StringComparison.Ordinal);
        }

        private static int GetSweepItemQuality(Item item)
        {
            if (item == null)
            {
                return -1;
            }

            try
            {
                return item.Quality;
            }
            catch
            {
                return -1;
            }
        }

        private static int GetSweepItemSortValue(Item item)
        {
            if (item == null)
            {
                return 0;
            }

            try
            {
                int rawValue = item.GetTotalRawValue();
                if (rawValue > 0)
                {
                    return rawValue;
                }
            }
            catch {}

            try
            {
                return item.Value * Math.Max(1, item.StackCount);
            }
            catch
            {
                return 0;
            }
        }

        private static bool CanAfford(int cost)
        {
            if (cost <= 0)
            {
                return true;
            }

            try
            {
                if (activeServiceController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController))
                {
                    return ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(activeServiceController, cost);
                }

                return EconomyManager.IsEnough(new Cost((long)cost), true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 检查资金失败: " + e.Message);
                return false;
            }
        }

        private static bool TryPay(int cost)
        {
            if (cost <= 0)
            {
                return true;
            }

            try
            {
                if (activeServiceController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController))
                {
                    return ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                        activeServiceController,
                        cost,
                        "ZombieModeTempCourierSweep");
                }

                Cost payment = new Cost((long)cost);
                return EconomyManager.IsEnough(payment, true, true) && EconomyManager.Pay(payment, true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 扣费失败: " + e.Message);
                return false;
            }
        }

        private static void TryRefund(int cost, bool shouldRefund)
        {
            if (!shouldRefund || cost <= 0)
            {
                return;
            }

            try
            {
                if (activeServiceController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController))
                {
                    ModBehaviour.Instance.RefundZombieModePurificationPointsForRealNpc(activeServiceController, cost, shouldRefund);
                    return;
                }

                EconomyManager.Add(cost);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 退款失败: " + e.Message);
            }
        }

        private static void ShowBubbleOrMessage(Transform npcTransform, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                if (npcTransform != null)
                {
                    UniTaskExtensions.Forget(
                        DialogueBubblesManager.Show(
                            message,
                            npcTransform,
                            BubbleYOffset,
                            false,
                            false,
                            -1f,
                            BubbleDuration)
                    );
                    return;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 显示气泡失败: " + e.Message);
            }

            ModBehaviour mod = ModBehaviour.Instance;
            if (mod != null)
            {
                mod.ShowMessage(message);
            }
        }
    }
}

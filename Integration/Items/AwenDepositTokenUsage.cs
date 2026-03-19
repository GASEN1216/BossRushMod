using System;
using System.Collections;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 阿稳快递牌使用行为。
    /// 复用快递服务链路，把玩家当前穿戴和背包物品一键快递回家。
    /// </summary>
    public class AwenCourierTokenUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = AwenCourierTokenConfig.GetUseDescription()
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
            List<Item> rootItems = CollectCandidateRootItems(player, item, false);
            if (rootItems.Count <= 0)
            {
                return false;
            }

            int fee = CalculateRequiredFee(rootItems);
            if (fee <= 0)
            {
                return true;
            }

            return CourierService.CanAffordDelivery(fee);
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
                ModBehaviour mod = ModBehaviour.Instance;
                if (player == null || mod == null)
                {
                    ModBehaviour.DevLog("[AwenCourierToken] Player or mod instance missing, abort");
                    return;
                }

                mod.StartCoroutine(StoreCarriedItemsCoroutine(player, item));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierToken] OnUse failed: " + e.Message);
            }
        }

        private static IEnumerator StoreCarriedItemsCoroutine(CharacterMainControl player, Item usedItem)
        {
            yield return null;
            yield return null;

            ModBehaviour mod = ModBehaviour.Instance;
            if (player == null || player.CharacterItem == null || mod == null)
            {
                yield break;
            }

            List<Item> rootItems = CollectCandidateRootItems(player, usedItem, true);
            if (rootItems.Count == 0)
            {
                mod.ShowMessage(AwenCourierTokenConfig.GetNoItemsMessageText());
                yield break;
            }

            int fee = CalculateRequiredFee(rootItems);
            if (fee > 0 && !CourierService.TryPayDeliveryFee(fee))
            {
                mod.ShowMessage(AwenCourierTokenConfig.GetInsufficientFundsMessageText(fee));
                yield break;
            }

            int shippedCount = CourierService.QuickDeliverItems(rootItems, null, false);
            if (shippedCount > 0)
            {
                mod.ShowBigBanner(AwenCourierTokenConfig.GetSuccessBannerText(shippedCount, fee));
                mod.ShowMessage(AwenCourierTokenConfig.GetSuccessMessageText(shippedCount, fee));
            }
            else
            {
                mod.ShowMessage(AwenCourierTokenConfig.GetNoItemsMessageText());
            }
        }

        private static int CalculateRequiredFee(List<Item> rootItems)
        {
            if (rootItems == null || rootItems.Count == 0)
            {
                return 0;
            }

            if (IsInBaseScene())
            {
                return 0;
            }

            return CourierService.CalculateDeliveryFee(rootItems);
        }

        private static bool IsInBaseScene()
        {
            try
            {
                return SceneManager.GetActiveScene().name == "Base_SceneV2";
            }
            catch
            {
                return false;
            }
        }

        private static int CountCandidateRootItems(CharacterMainControl player, Item usedItem)
        {
            if (player == null || player.CharacterItem == null)
            {
                return 0;
            }

            return CollectCandidateRootItems(player, usedItem, false).Count;
        }

        private static List<Item> CollectCandidateRootItems(CharacterMainControl player, Item usedItem, bool excludeRootsContainingUsedItem)
        {
            List<Item> result = new List<Item>();
            if (player == null || player.CharacterItem == null)
            {
                return result;
            }

            Item characterItem = player.CharacterItem;
            HashSet<Item> seen = new HashSet<Item>();

            try
            {
                if (characterItem.Slots != null)
                {
                    for (int i = 0; i < characterItem.Slots.Count; i++)
                    {
                        Slot slot = characterItem.Slots.GetSlotByIndex(i);
                        Item rootItem = slot != null ? slot.Content : null;
                        TryAddCandidate(result, seen, rootItem, usedItem, excludeRootsContainingUsedItem);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierToken] Collect slots failed: " + e.Message);
            }

            try
            {
                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        TryAddCandidate(result, seen, inventory.Content[i], usedItem, excludeRootsContainingUsedItem);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierToken] Collect inventory failed: " + e.Message);
            }

            try
            {
                Inventory petInventory = PetProxy.PetInventory;
                if (petInventory != null && petInventory.Content != null)
                {
                    for (int i = 0; i < petInventory.Content.Count; i++)
                    {
                        TryAddCandidate(result, seen, petInventory.Content[i], usedItem, excludeRootsContainingUsedItem);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierToken] Collect pet inventory failed: " + e.Message);
            }

            return result;
        }

        private static void TryAddCandidate(
            List<Item> result,
            HashSet<Item> seen,
            Item rootItem,
            Item usedItem,
            bool excludeRootsContainingUsedItem)
        {
            if (rootItem == null || seen.Contains(rootItem))
            {
                return;
            }

            if (ReferenceEquals(rootItem, usedItem))
            {
                return;
            }

            if (excludeRootsContainingUsedItem && usedItem != null && ContainsItemInTree(rootItem, usedItem))
            {
                ModBehaviour.DevLog("[AwenCourierToken] Skip root containing used item: " + SafeItemName(rootItem));
                return;
            }

            seen.Add(rootItem);
            result.Add(rootItem);
        }

        private static bool ContainsItemInTree(Item rootItem, Item targetItem)
        {
            if (rootItem == null || targetItem == null)
            {
                return false;
            }

            if (ReferenceEquals(rootItem, targetItem))
            {
                return true;
            }

            try
            {
                Inventory inventory = rootItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        if (ContainsItemInTree(inventory.Content[i], targetItem))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (rootItem.Slots != null)
                {
                    for (int i = 0; i < rootItem.Slots.Count; i++)
                    {
                        Slot slot = rootItem.Slots.GetSlotByIndex(i);
                        if (slot != null && ContainsItemInTree(slot.Content, targetItem))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static string SafeItemName(Item item)
        {
            if (item == null)
            {
                return "<null>";
            }

            try
            {
                return item.DisplayName;
            }
            catch
            {
                try
                {
                    return item.name;
                }
                catch
                {
                    return "<unknown>";
                }
            }
        }
    }
}

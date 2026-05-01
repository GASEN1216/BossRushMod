using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool PrepareZombieModeInventoryTransferShell(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            if (zombieModeEntryTransaction.InventoryTransferStarted)
            {
                return true;
            }

            List<Item> items = CollectZombieModeTopLevelPlayerItems();
            Inventory storage = PlayerStorage.Inventory;
            if (items.Count > 0 && storage == null)
            {
                DevLog("[ZombieMode] 裸装转移失败: 玩家仓库不可用");
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null || item.IsBeingDestroyed)
                {
                    continue;
                }

                try
                {
                    item.Detach();
                    if (!storage.AddItem(item))
                    {
                        throw new System.InvalidOperationException("PlayerStorage is full");
                    }

                    zombieModeEntryTransaction.InventoryTransferredItems.Add(item);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] 裸装转移失败: " + e.Message);
                    try
                    {
                        if (item != null && !item.IsBeingDestroyed)
                        {
                            ItemUtilities.SendToPlayer(item, false, false);
                        }
                    }
                    catch (System.Exception ex2)
                    {
                        DevLog("[ZombieMode] 裸装回退 SendToPlayer 失败: " + ex2.Message);
                    }
                    RollbackZombieModeInventoryTransferShell();
                    return false;
                }
            }

            zombieModeEntryTransaction.InventoryTransferStarted = true;
            return true;
        }

        private void RollbackZombieModeInventoryTransferShell()
        {
            for (int i = zombieModeEntryTransaction.InventoryTransferredItems.Count - 1; i >= 0; i--)
            {
                Item item = zombieModeEntryTransaction.InventoryTransferredItems[i];
                if (item == null || item.IsBeingDestroyed)
                {
                    continue;
                }

                try
                {
                    ItemUtilities.SendToPlayer(item, false, false);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] 裸装转移回滚失败: " + e.Message);
                }
            }

            zombieModeEntryTransaction.InventoryTransferredItems.Clear();
            zombieModeEntryTransaction.InventoryTransferStarted = false;
        }

        private List<Item> CollectZombieModeTopLevelPlayerItems()
        {
            List<Item> result = new List<Item>();
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null)
            {
                return result;
            }

            Item characterItem = player.CharacterItem;
            Inventory inventory = characterItem.Inventory;
            if (inventory != null && inventory.Content != null)
            {
                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    AddZombieModeTransferCandidate(result, inventory.Content[i]);
                }
            }

            SlotCollection slots = characterItem.Slots;
            if (slots != null)
            {
                foreach (Slot slot in slots)
                {
                    if (slot != null)
                    {
                        AddZombieModeTransferCandidate(result, slot.Content);
                    }
                }
            }

            return result;
        }

        private void AddZombieModeTransferCandidate(List<Item> result, Item item)
        {
            if (result == null || item == null || item.IsBeingDestroyed)
            {
                return;
            }

            if (item.TypeID == BossRushItemIds.ZombieTideInvitation || item.TypeID == BossRushItemIds.ZombieTideBeacon)
            {
                return;
            }

            if (!result.Contains(item))
            {
                result.Add(item);
            }
        }
    }
}

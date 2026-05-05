using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Data;
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
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null || item.IsBeingDestroyed)
                {
                    continue;
                }

                try
                {
                    if (!TryMoveZombieModeEntryItemToStorageOrInbox(item))
                    {
                        throw new System.InvalidOperationException("Storage transfer returned false");
                    }
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

        private bool TryMoveZombieModeEntryItemToStorageOrInbox(Item item)
        {
            if (item == null || item.IsBeingDestroyed)
            {
                return true;
            }

            item.Detach();
            ReforgeDataPersistence.SyncCurrentReforgeState(item);
            Inventory storage = PlayerStorage.Inventory;
            if (storage != null)
            {
                int firstEmptyPosition = storage.GetFirstEmptyPosition(0);
                if (firstEmptyPosition >= 0)
                {
                    bool added = storage.AddAt(item, firstEmptyPosition);
                    if (added)
                    {
                        zombieModeEntryTransaction.InventoryTransferredItems.Add(item);
                        return true;
                    }
                }
            }

            ItemTreeData itemData = ItemTreeData.FromItem(item);
            if (itemData == null)
            {
                throw new System.InvalidOperationException("ItemTreeData.FromItem returned null");
            }

            PlayerStorageBuffer.Buffer.Add(itemData);
            zombieModeEntryTransaction.InventoryTransferredInboxItems.Add(itemData);
            PlayerStorageBuffer.SaveBuffer();
            item.DestroyTree();
            return true;
        }

        private void RollbackZombieModeInventoryTransferShell()
        {
            // 反向迭代清理走 RunScopedRegistry.ForEachReverse（审查 §1.3）。
            RunScopedRegistry.ForEachReverse(
                zombieModeEntryTransaction.InventoryTransferredItems,
                item =>
                {
                    if (item != null && !item.IsBeingDestroyed)
                    {
                        ItemUtilities.SendToPlayer(item, false, false);
                    }
                },
                (e, item) => DevLog("[ZombieMode] 裸装转移回滚失败: " + e.Message));

            RunScopedRegistry.ForEachReverse(
                zombieModeEntryTransaction.InventoryTransferredInboxItems,
                itemData =>
                {
                    if (itemData != null)
                    {
                        PlayerStorageBuffer.Buffer.Remove(itemData);
                    }
                },
                (e, itemData) => DevLog("[ZombieMode] 裸装 inbox 回滚失败: " + e.Message));

            try { PlayerStorageBuffer.SaveBuffer(); } catch (System.Exception e) { DevLog("[ZombieMode] 裸装 inbox SaveBuffer 回滚失败: " + e.Message); }
            zombieModeEntryTransaction.InventoryTransferredItems.Clear();
            zombieModeEntryTransaction.InventoryTransferredInboxItems.Clear();
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

            if (!result.Contains(item))
            {
                result.Add(item);
            }
        }
    }
}

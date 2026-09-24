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
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.PrepareZombieModeInventoryTransfer(runId);
        }

        private bool TryMoveZombieModeEntryItemToStorageOrInbox(Item item)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryMoveZombieModeEntryItemToStorageOrInbox(item);
        }

        private void RollbackZombieModeInventoryTransferShell()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RollbackZombieModeInventoryTransfer();
        }

        private List<Item> CollectZombieModeTopLevelPlayerItems()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.CollectZombieModeTopLevelPlayerItems() : new List<Item>();
        }

        private void AddZombieModeTransferCandidate(List<Item> result, Item item)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.AddZombieModeTransferCandidate(result, item);
        }
    }
}

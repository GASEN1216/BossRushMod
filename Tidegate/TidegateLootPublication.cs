using System;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>搜刮库存核验与原生界面收起；状态仍归 TidegateLoot 会话 owner。</summary>
    internal sealed partial class TidegateLoot
    {
        private bool RequiresStock(Point point)
        {
            return IsFixedContainer(point.Id) || point.Id == shiftKeyBox || point.Id == ForemanCabinet
                || underwaterPrizes.TryGetValue(point.Id, out int prize) && prize > 0;
        }

        private static void RetryBuild(Point point, string error)
        {
            Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮点 " + point.Id + " 待重试：" + error);
            point.Built = false;
            point.RetryAt = Time.time + 2f;
        }

        private static bool IsCurrentLootView(InteractableLootbox box)
        {
            LootView view = LootView.Instance;
            return view != null && view.open && box.Inventory != null && view.TargetInventory == box.Inventory;
        }

        private static void HideLootBox(InteractableLootbox box, bool resetState = false)
        {
            try
            {
                if (IsCurrentLootView(box)) LootView.Instance.Close();
                else if (resetState || box.Interacting) box.StopInteract();
            }
            finally
            {
                if (box != null) box.gameObject.SetActive(false);
            }
        }

        private static bool ContainsStock(InteractableLootbox box, int typeId, int minimumQuality = 0, int maximumQuality = int.MaxValue)
        {
            Inventory inventory = box.Inventory;
            if (inventory == null || inventory.gameObject != box.gameObject) return false;
            foreach (Item item in inventory.Content)
                if (item != null && item.InInventory == inventory && !item.IsBeingDestroyed && item.TypeID == typeId
                    && item.Quality >= minimumQuality && item.Quality <= maximumQuality) return true;
            return false;
        }

    }
}

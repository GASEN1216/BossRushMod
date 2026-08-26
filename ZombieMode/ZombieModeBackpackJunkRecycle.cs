using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool HasZombieModeRecyclableBackpackJunk()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null || player.CharacterItem.Inventory.Content == null)
            {
                return false;
            }

            System.Collections.Generic.List<Item> content = player.CharacterItem.Inventory.Content;
            for (int i = 0; i < content.Count; i++)
            {
                if (IsZombieModeRecyclableBackpackJunk(content[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private bool RecycleZombieModeBackpackJunkForPurification()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null || player.CharacterItem.Inventory.Content == null)
            {
                return false;
            }

            Inventory inventory = player.CharacterItem.Inventory;
            int recycledItems = 0;
            long recycledValue = 0L;
            for (int i = inventory.Content.Count - 1; i >= 0; i--)
            {
                Item item = inventory.Content[i];
                if (!IsZombieModeRecyclableBackpackJunk(item))
                {
                    continue;
                }

                try
                {
                    recycledValue += System.Math.Max(0, item.GetTotalRawValue());
                    recycledItems += item.Stackable ? Mathf.Max(1, item.StackCount) : 1;
                    inventory.RemoveItem(item);
                    item.DestroyTree();
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] 背包废品回收失败: " + e.Message);
                }
            }

            if (recycledItems <= 0)
            {
                return false;
            }

            int gainedPoints = Mathf.Max(recycledItems, Mathf.CeilToInt(recycledValue / (float)ZombieModeTuning.BackpackJunkValuePerPurificationPoint));
            zombieModeRunState.PurificationPoints += gainedPoints;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_RecycledBackpackJunk"),
                recycledItems,
                gainedPoints));
            return true;
        }

        private bool IsZombieModeRecyclableBackpackJunk(Item item)
        {
            if (item == null || item.TypeID <= 0 || item.Quality > ZombieModeTuning.BackpackJunkMaximumQuality)
            {
                return false;
            }

            // 只回收叶子物品。容器、带附件的武器或带插槽内容的物品不能
            // 通过 DestroyTree 整棵销毁，避免连带删除受保护物品。
            if (ZombieModeItemHasNestedContent(item))
            {
                return false;
            }

            if (item.TypeID == BossRushItemIds.ZombieTideInvitation ||
                item.TypeID == BossRushItemIds.ZombieTideBeacon ||
                item.TypeID == BossRushItemIds.PortableSafeZoneDevice)
            {
                return false;
            }

            string[] protectedTags =
            {
                "Weapon", "Gun", "MeleeWeapon", "Armor", "BodyArmor", "Helmet", "Backpack", "Headset",
                "Ammo", "Bullet", "Medical", "Medic", "Healing", "Food", "Drink",
                "Key", "SpecialKey", "Special", "RunOnly", "Quest", "Task"
            };
            for (int i = 0; i < protectedTags.Length; i++)
            {
                if (ItemHasZombieModeTag(item, protectedTags[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ZombieModeItemHasNestedContent(Item item)
        {
            try
            {
                if (item.Inventory != null && item.Inventory.Content != null && item.Inventory.Content.Count > 0)
                {
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 检查废品内置库存失败，按受保护物品处理: " + e.Message);
                return true;
            }

            try
            {
                if (item.Slots != null)
                {
                    for (int i = 0; i < item.Slots.Count; i++)
                    {
                        Slot slot = item.Slots.GetSlotByIndex(i);
                        if (slot != null && slot.Content != null)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 检查废品插槽内容失败，按受保护物品处理: " + e.Message);
                return true;
            }

            return false;
        }
    }
}

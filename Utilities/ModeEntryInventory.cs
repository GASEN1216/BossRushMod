using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    internal static class ModeEntryInventory
    {
        private static readonly string[] NakedEquipmentSlotNames = new string[]
        {
            "Armor",
            "Helmat",
            "FaceMask",
            "Backpack",
            "Headset",
            "Totem1",
            "Totem2"
        };

        private static readonly string[] NakedWeaponSlotNames = new string[]
        {
            "PrimaryWeapon",
            "SecondaryWeapon",
            "MeleeWeapon"
        };

        internal static bool TryGetMainCharacterItem(out CharacterMainControl main, out Item characterItem)
        {
            main = CharacterMainControl.Main;
            if (main == null)
            {
                characterItem = null;
                return false;
            }

            characterItem = main.CharacterItem;
            return characterItem != null;
        }

        internal static Item FindFirstPlayerInventoryItemByTypeId(int typeId, string logTag = null, string itemLabel = null)
        {
            if (typeId <= 0)
            {
                return null;
            }

            try
            {
                CharacterMainControl main;
                Item characterItem;
                if (!TryGetMainCharacterItem(out main, out characterItem))
                {
                    return null;
                }

                Inventory inventory = characterItem.Inventory;
                if (inventory == null || inventory.Content == null)
                {
                    return null;
                }

                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    int itemTypeId = -1;
                    try { itemTypeId = item.TypeID; } catch { }
                    if (itemTypeId != typeId)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(logTag) && !string.IsNullOrEmpty(itemLabel))
                    {
                        ModBehaviour.DevLog("[" + logTag + "] 检测到" + itemLabel + " (TypeID=" + itemTypeId + ")");
                    }

                    return item;
                }
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(logTag))
                {
                    string label = string.IsNullOrEmpty(itemLabel) ? "入场物品" : itemLabel;
                    ModBehaviour.DevLog("[" + logTag + "] [ERROR] 查找" + label + "失败: " + e.Message);
                }
            }

            return null;
        }

        internal static bool IsAllowedNakedEntryInventoryItem(int typeId, int allowedTypeIdA, int allowedTypeIdB, bool allowFactionFlags)
        {
            if (typeId <= 0)
            {
                return false;
            }

            if (typeId == allowedTypeIdA || typeId == allowedTypeIdB)
            {
                return true;
            }

            return allowFactionFlags && IsFactionFlagTypeId(typeId);
        }

        internal static bool IsPlayerNakedWithAllowedItems(string logTag, int allowedTypeIdA, int allowedTypeIdB, bool allowFactionFlags)
        {
            try
            {
                CharacterMainControl main;
                Item characterItem;
                if (!TryGetMainCharacterItem(out main, out characterItem))
                {
                    return false;
                }

                for (int i = 0; i < NakedEquipmentSlotNames.Length; i++)
                {
                    string slotName = NakedEquipmentSlotNames[i];
                    try
                    {
                        Slot slot = characterItem.Slots.GetSlot(slotName);
                        if (slot != null && slot.Content != null)
                        {
                            ModBehaviour.DevLog("[" + logTag + "] 装备槽不为空: " + slotName);
                            return false;
                        }
                    }
                    catch { }
                }

                for (int i = 0; i < NakedWeaponSlotNames.Length; i++)
                {
                    string slotName = NakedWeaponSlotNames[i];
                    try
                    {
                        Slot slot = characterItem.Slots.GetSlot(slotName);
                        if (slot != null && slot.Content != null)
                        {
                            ModBehaviour.DevLog("[" + logTag + "] 武器槽不为空: " + slotName);
                            return false;
                        }
                    }
                    catch { }
                }

                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        Item item = inventory.Content[i];
                        if (item == null)
                        {
                            continue;
                        }

                        int typeId = -1;
                        try { typeId = item.TypeID; } catch { }
                        if (IsAllowedNakedEntryInventoryItem(typeId, allowedTypeIdA, allowedTypeIdB, allowFactionFlags))
                        {
                            continue;
                        }

                        string displayName = null;
                        try { displayName = item.DisplayName; } catch { }
                        string itemLabel = string.IsNullOrEmpty(displayName)
                            ? ("TypeID=" + typeId)
                            : (displayName + " (TypeID=" + typeId + ")");
                        ModBehaviour.DevLog("[" + logTag + "] 背包中存在未允许物品: " + itemLabel);
                        return false;
                    }
                }

                try
                {
                    Inventory petInventory = PetProxy.PetInventory;
                    if (petInventory != null && petInventory.Content != null)
                    {
                        for (int i = 0; i < petInventory.Content.Count; i++)
                        {
                            Item petItem = petInventory.Content[i];
                            if (petItem == null)
                            {
                                continue;
                            }

                            string displayName = null;
                            try { displayName = petItem.DisplayName; } catch { }
                            ModBehaviour.DevLog("[" + logTag + "] 狗子背包中存在物品: " + (string.IsNullOrEmpty(displayName) ? "未知物品" : displayName));
                            return false;
                        }
                    }
                }
                catch (Exception petEx)
                {
                    ModBehaviour.DevLog("[" + logTag + "] 无法检查狗子背包: " + petEx.Message);
                }

                ModBehaviour.DevLog("[" + logTag + "] 玩家满足裸装条件");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[" + logTag + "] [ERROR] 裸装检测失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 判断指定 TypeID 是否为 Mode E 营旗物品（裸装检测时豁免用）
        /// </summary>
        internal static bool IsFactionFlagTypeId(int typeId)
        {
            if (typeId <= 0) return false;
            int[] flagIds = FactionFlagConfig.ALL_FLAG_TYPE_IDS;
            for (int i = 0; i < flagIds.Length; i++)
            {
                if (flagIds[i] == typeId) return true;
            }
            return false;
        }

    }
}

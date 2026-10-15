using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>裸装入场被拦的位置。玩家看到的提示按它说清是哪个容器挡住了。</summary>
    internal enum NakedEntryBlockKind
    {
        None,
        NotReady,
        EquipmentSlot,
        WeaponSlot,
        AccessorySlot,
        Backpack,
        DogBackpack,
        PetNestBackpack
    }

    internal struct NakedEntryBlock
    {
        internal NakedEntryBlockKind Kind;
        internal string ItemName;

        /// <summary>
        /// 护甲、头盔、背包槽与武器槽都空，只剩背包类容器或饰品挡路：玩家显然想裸装入场，值得明说原因。
        /// 饰品（面罩、耳机、图腾）在武器槽之后才判，所以命中它时武器槽一定是空的。
        /// </summary>
        internal bool OnlyContainersBlock
        {
            get
            {
                return Kind == NakedEntryBlockKind.NotReady || Kind == NakedEntryBlockKind.AccessorySlot || Kind == NakedEntryBlockKind.Backpack
                    || Kind == NakedEntryBlockKind.DogBackpack || Kind == NakedEntryBlockKind.PetNestBackpack;
            }
        }
    }

    internal static class ModeEntryInventory
    {
        private static readonly string[] NakedEquipmentSlotNames = new string[]
        {
            "Armor",
            "Helmat",
            "Backpack"
        };

        // 小件饰品最容易忘了摘；排在武器槽之后判，命中时可以放心提示「这次不会进白手起家」。
        private static readonly string[] NakedAccessorySlotNames = new string[]
        {
            "FaceMask",
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
            NakedEntryBlock ignored;
            return IsPlayerNakedWithAllowedItems(logTag, allowedTypeIdA, allowedTypeIdB, allowFactionFlags, out ignored);
        }

        /// <summary>
        /// 同一次遍历里带出第一个拦截项。正式构建没有 DevLog，玩家只能靠它知道为什么没进裸装模式。
        /// </summary>
        internal static bool IsPlayerNakedWithAllowedItems(string logTag, int allowedTypeIdA, int allowedTypeIdB, bool allowFactionFlags,
            out NakedEntryBlock block)
        {
            block = new NakedEntryBlock { Kind = NakedEntryBlockKind.NotReady };
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
                            block = new NakedEntryBlock { Kind = NakedEntryBlockKind.EquipmentSlot, ItemName = SafeDisplayName(slot.Content) };
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
                            block = new NakedEntryBlock { Kind = NakedEntryBlockKind.WeaponSlot, ItemName = SafeDisplayName(slot.Content) };
                            return false;
                        }
                    }
                    catch { }
                }

                for (int i = 0; i < NakedAccessorySlotNames.Length; i++)
                {
                    string slotName = NakedAccessorySlotNames[i];
                    try
                    {
                        Slot slot = characterItem.Slots.GetSlot(slotName);
                        if (slot != null && slot.Content != null)
                        {
                            ModBehaviour.DevLog("[" + logTag + "] 饰品槽不为空: " + slotName);
                            block = new NakedEntryBlock { Kind = NakedEntryBlockKind.AccessorySlot, ItemName = SafeDisplayName(slot.Content) };
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
                        block = new NakedEntryBlock { Kind = NakedEntryBlockKind.Backpack, ItemName = displayName };
                        return false;
                    }
                }

                try
                {
                    // 官方狗子背包就是存档里的 Inventory_Safe，跨局保留；它异步加载，加载中不能当成「空」。
                    Inventory petInventory = PetProxy.PetInventory;
                    if (petInventory != null && petInventory.Loading)
                    {
                        ModBehaviour.DevLog("[" + logTag + "] 狗子背包尚在加载，暂不判定裸装");
                        block = new NakedEntryBlock { Kind = NakedEntryBlockKind.NotReady };
                        return false;
                    }
                    if (petInventory != null && petInventory.Content != null)
                    {
                        for (int i = 0; i < petInventory.Content.Count; i++)
                        {
                            Item petItem = petInventory.Content[i];
                            if (petItem == null)
                            {
                                continue;
                            }

                            int petTypeId = -1;
                            try { petTypeId = petItem.TypeID; } catch { /* 读不到 TypeID 按不放行处理 */ }
                            if (IsAllowedNakedEntryInventoryItem(petTypeId, allowedTypeIdA, allowedTypeIdB, allowFactionFlags))
                            {
                                continue;
                            }

                            string displayName = null;
                            try { displayName = petItem.DisplayName; } catch { }
                            ModBehaviour.DevLog("[" + logTag + "] 狗子背包中存在物品: " + (string.IsNullOrEmpty(displayName) ? "未知物品" : displayName));
                            block = new NakedEntryBlock { Kind = NakedEntryBlockKind.DogBackpack, ItemName = displayName };
                            return false;
                        }
                    }
                }
                catch (Exception petEx)
                {
                    ModBehaviour.DevLog("[" + logTag + "] 无法检查狗子背包: " + petEx.Message);
                }

                // 遗种巢出战崽有自己的独立背包，会跟着进图，同样不能借它把物资带进裸装模式。
                try
                {
                    if (PetNestService.HasBackpackItems(PetNestService.DeployedPet))
                    {
                        ModBehaviour.DevLog("[" + logTag + "] 出战遗种崽的背包里有物品");
                        block = new NakedEntryBlock { Kind = NakedEntryBlockKind.PetNestBackpack };
                        return false;
                    }
                }
                catch (Exception petNestEx)
                {
                    ModBehaviour.DevLog("[" + logTag + "] 无法检查遗种崽背包: " + petNestEx.Message);
                }

                ModBehaviour.DevLog("[" + logTag + "] 玩家满足裸装条件");
                block = new NakedEntryBlock { Kind = NakedEntryBlockKind.None };
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[" + logTag + "] [ERROR] 裸装检测失败: " + e.Message);
                return false;
            }
        }

        private static string SafeDisplayName(Item item)
        {
            if (item == null) return null;
            try { return item.DisplayName; } catch { return null; }
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

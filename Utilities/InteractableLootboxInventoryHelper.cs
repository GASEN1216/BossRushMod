using System;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal static class InteractableLootboxInventoryHelper
    {
        private static readonly BindingFlags InstanceBindingFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo createLocalInventoryMethod;
        private static bool createLocalInventoryMethodCached;
        private static FieldInfo inventoryReferenceField;
        private static bool inventoryReferenceFieldCached;

        internal static bool EnsureLocalInventory(InteractableLootbox lootbox, int fallbackCapacity = 24)
        {
            if (lootbox == null)
            {
                return false;
            }

            try
            {
                MethodInfo createMethod = GetCreateLocalInventoryMethod();
                if (createMethod != null)
                {
                    createMethod.Invoke(lootbox, null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LootboxInventoryHelper] CreateLocalInventory failed: " + e.Message);
            }

            if (lootbox.Inventory != null)
            {
                return true;
            }

            try
            {
                Inventory inventory = lootbox.gameObject.GetComponent<Inventory>();
                if (inventory == null)
                {
                    inventory = lootbox.gameObject.AddComponent<Inventory>();
                    inventory.SetCapacity(fallbackCapacity);
                }

                FieldInfo referenceField = GetInventoryReferenceField();
                if (referenceField != null)
                {
                    referenceField.SetValue(lootbox, inventory);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LootboxInventoryHelper] inventoryReference fallback failed: " + e.Message);
            }

            return lootbox.Inventory != null;
        }

        private static MethodInfo GetCreateLocalInventoryMethod()
        {
            if (!createLocalInventoryMethodCached)
            {
                createLocalInventoryMethod = typeof(InteractableLootbox).GetMethod("CreateLocalInventory", InstanceBindingFlags);
                createLocalInventoryMethodCached = true;
            }

            return createLocalInventoryMethod;
        }

        private static FieldInfo GetInventoryReferenceField()
        {
            if (!inventoryReferenceFieldCached)
            {
                inventoryReferenceField = typeof(InteractableLootbox).GetField("inventoryReference", InstanceBindingFlags);
                inventoryReferenceFieldCached = true;
            }

            return inventoryReferenceField;
        }
    }
}

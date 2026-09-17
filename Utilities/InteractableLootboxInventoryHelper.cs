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

        /// <summary>
        /// 交付一件新建的额外战利品：满箱只扩一格，不替换已有物品。
        /// 接管未挂载实例的所有权；失败时清掉临时实例，已入箱的物品不销毁。
        /// </summary>
        internal static bool TryAddExtraItem(Inventory inventory, Item item)
        {
            if (item == null || item.ParentObject != null) return false;
            try
            {
                if (inventory == null) return false;
                if (inventory.GetFirstEmptyPosition(0) < 0)
                {
                    int contentCount = inventory.Content != null ? inventory.Content.Count : 0;
                    inventory.SetCapacity(Math.Max(inventory.Capacity, contentCount) + 1);
                }
                return inventory.AddItem(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LootboxInventoryHelper] Extra reward delivery failed: " + e.Message);
                // 官方 AddAt 在挂载后广播事件；监听者抛错不等于物品没有交付。
                return inventory != null && item != null && item.InInventory == inventory;
            }
            finally
            {
                try
                {
                    if (item != null && item.ParentObject == null
                        && (inventory == null || inventory.Content == null || !inventory.Content.Contains(item)))
                        item.DestroyTree();
                }
                catch (Exception) { /* 清理失败不能中断其余专属奖励。 */ }
            }
        }

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

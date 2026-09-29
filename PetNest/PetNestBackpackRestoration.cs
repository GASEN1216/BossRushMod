// 背包恢复的短命 owner。官方 ItemTreeData.InstantiateAsync 不暴露半途生成的节点，
// 缺槽 / AddAt 失败时会留下孤儿；这里逐节点使用同一组官方物品 API 并负责全部失败回收。
using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush
{
    internal sealed class PetNestBackpackRestoration : IDisposable
    {
        // 官方仅公开事件 add/remove。已对照当前 DLL：私有同名字段 Action<Item>，
        // 每个节点写完 Variables 后、整树连接前通知。保留其它 Mod 的加载兼容性。
        private static readonly FieldInfo LoadedEvent = typeof(ItemTreeData).GetField("OnItemLoaded",
            BindingFlags.NonPublic | BindingFlags.Static);
        private readonly Dictionary<int, Item> _owned = new Dictionary<int, Item>();
        private bool _transferred;

        internal void CreateNode(ItemTreeData.DataEntry entry)
        {
            if (LoadedEvent == null || LoadedEvent.FieldType != typeof(Action<Item>))
                throw new InvalidOperationException("backpack_item_loaded_contract_changed");
            BossRushDynamicItemRegistry.EnsureRegistered(entry.typeID);
            if (ItemAssetsCollection.GetPrefab(entry.typeID) == null)
                throw new InvalidOperationException("backpack_prefab_missing:" + entry.typeID);
            Item item = ItemAssetsCollection.InstantiateSync(entry.typeID);
            if (item == null) throw new InvalidOperationException("backpack_restore_failed");
            // 后续任何变量 / 插槽操作抛错时，这一节点已归 owner，可完整回收。
            _owned.Add(entry.instanceID, item);
            foreach (CustomData value in entry.variables)
                item.Variables.SetRaw(value.Key, value.DataType, value.GetRawCopied(), true, value.Display);
            Action<Item> onLoaded = (Action<Item>)LoadedEvent.GetValue(null);
            if (onLoaded != null) onLoaded(item);
        }

        internal Item Connect(ItemTreeData tree)
        {
            PetNestBackpackSnapshot.ValidateTree(tree);
            if (_owned.Count != tree.entries.Count) throw new InvalidOperationException("backpack_restore_incomplete");
            foreach (ItemTreeData.DataEntry entry in tree.entries)
            {
                Item item = _owned[entry.instanceID];
                foreach (ItemTreeData.SlotInstanceIDPair link in entry.slotContents)
                {
                    // 与官方恢复一致：存档可能包含运行时新增的插槽 / 容器。
                    if (item.Slots == null) item.CreateSlotsComponent();
                    var slot = item.Slots.GetSlot(link.slot);
                    if (slot == null)
                    {
                        slot = new ItemStatsSystem.Items.Slot(link.slot);
                        item.Slots.Add(slot);
                    }
                    // 同 TypeID 可堆叠时，官方 Plug 会直接和默认内容 Combine。
                    // 默认内容来自本次 prefab 克隆，先清掉才能恢复存档中的确切数量。
                    Item defaultContent = slot.Content;
                    if (defaultContent != null)
                    {
                        try { slot.Unplug(); }
                        finally { defaultContent.DestroyTree(); }
                    }
                    Item displaced;
                    if (!slot.Plug(_owned[link.instanceID], out displaced))
                        throw new InvalidOperationException("backpack_slot_restore_failed");
                    // 预制体的默认物品也属于本次新实例，卸下后不能留成孤儿。
                    if (displaced != null) displaced.DestroyTree();
                }
                if (entry.inventory.Count > 0 && item.Inventory == null)
                    item.CreateInventoryComponent();
                foreach (ItemTreeData.InventoryDataEntry link in entry.inventory)
                {
                    Item replaced = item.Inventory.GetItemAt(link.position);
                    if (replaced != null) { replaced.Detach(); replaced.DestroyTree(); }
                    item.Inventory.SetCapacity(Math.Max(item.Inventory.Capacity, checked(link.position + 1)));
                    if (!item.Inventory.AddAt(_owned[link.instanceID], link.position))
                        throw new InvalidOperationException("backpack_inventory_restore_failed");
                }
                if (item.Inventory != null)
                {
                    item.Inventory.lockedIndexes.Clear();
                    item.Inventory.lockedIndexes.AddRange(entry.inventorySortLocks);
                }
            }
            return _owned[tree.rootInstanceID];
        }

        // 调用方已把根放进它自己的容器，全部节点的唯一 owner 才一起移交。
        internal void Transfer() { _transferred = true; _owned.Clear(); }

        public void Dispose()
        {
            if (!_transferred)
                foreach (Item item in _owned.Values)
                    if (item != null) item.DestroyTree();
            _owned.Clear();
        }
    }
}

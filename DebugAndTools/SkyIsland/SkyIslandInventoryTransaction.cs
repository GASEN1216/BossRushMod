using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 合成台的一次同步事务。整堆预留只取出原件，部分预留留在原槽并记录原数量；
    /// 发出完整成品才销毁足额原件，失败时归还原件/原数量。
    /// 官方库存事件可能在改变状态以后抛出，所以每一步以实际归属和数量确认结果。
    /// 不保存玩家数据、不跨帧持有物品，循环上界是本次背包槽位数。
    /// </summary>
    internal sealed class SkyIslandInventoryTransaction : IDisposable
    {
        private sealed class Reserved
        {
            internal Item Item;
            internal int Slot, Count, Take;
            internal bool Touched;
        }

        private readonly CharacterMainControl player;
        private readonly Inventory inventory;
        private readonly List<Reserved> reserved = new List<Reserved>();
        private bool committed, disposed;

        private SkyIslandInventoryTransaction(CharacterMainControl owner)
        {
            player = owner;
            inventory = owner.CharacterItem.Inventory;
        }

        internal static bool TryReserve(CharacterMainControl player, SkyIslandIngredient[] inputs,
            out SkyIslandInventoryTransaction transaction)
        {
            transaction = null;
            if (player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null || inputs == null) return false;
            var candidate = new SkyIslandInventoryTransaction(player);
            try
            {
                // 先把整份配方选完，任何一种不够都不修改背包。同一物品只预留一次。
                for (int ingredient = 0; ingredient < inputs.Length; ingredient++)
                {
                    int left = inputs[ingredient].Count;
                    if (left <= 0) return false;
                    for (int slot = candidate.inventory.Content.Count - 1; slot >= 0 && left > 0; slot--)
                    {
                        Item item = candidate.inventory.Content[slot];
                        if (item == null || item.TypeID != inputs[ingredient].TypeId) continue;
                        bool selected = false;
                        for (int i = 0; i < candidate.reserved.Count; i++) if (candidate.reserved[i].Item == item) selected = true;
                        if (selected) continue;
                        int count = item.Stackable ? item.StackCount : 1;
                        int take = Math.Min(count, left);
                        if (take <= 0) continue;
                        candidate.reserved.Add(new Reserved { Item = item, Slot = slot, Count = count, Take = take });
                        left -= take;
                    }
                    if (left > 0) return false;
                }
                for (int i = 0; i < candidate.reserved.Count; i++)
                {
                    Reserved entry = candidate.reserved[i];
                    if (entry.Item == null || entry.Item.IsBeingDestroyed || entry.Item.InInventory != candidate.inventory
                        || candidate.inventory.GetItemAt(entry.Slot) != entry.Item || entry.Item.StackCount != entry.Count)
                        throw new InvalidOperationException("预留期间材料已变化");
                    entry.Touched = true;
                    if (entry.Take < entry.Count)
                    {
                        entry.Item.StackCount = entry.Count - entry.Take;
                        if (entry.Item.StackCount != entry.Count - entry.Take) throw new InvalidOperationException("材料数量未扣除");
                    }
                    else
                    {
                        candidate.inventory.RemoveItem(entry.Item);
                        if (candidate.inventory.Content.Contains(entry.Item) || HasOwner(entry.Item))
                            throw new InvalidOperationException("材料未被独占预留");
                    }
                }
                transaction = candidate;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandCraft] 预留材料失败：" + e.Message);
                return false;
            }
            finally
            {
                if (transaction == null) candidate.Dispose();
            }
        }

        internal void Commit()
        {
            if (disposed || committed) return;
            committed = true;
            for (int i = 0; i < reserved.Count; i++)
            {
                Reserved entry = reserved[i];
                if (entry.Take == entry.Count) DestroyUnowned(entry.Item);
            }
        }

        private void Restore(Reserved entry)
        {
            Item item = entry.Item;
            if (item == null || item.IsBeingDestroyed || inventory.Content.Contains(item) || HasOwner(item)) return;
            try
            {
                if (entry.Slot < inventory.Capacity && inventory.GetItemAt(entry.Slot) == null)
                    inventory.AddAt(item, entry.Slot);
                else if (!inventory.AddItem(item)) item.Drop(player, true);
            }
            catch (Exception e)
            {
                // AddAt / Drop 可以已经成功，只是末尾通知抛异常。此时绝不再投递一次。
                if (!inventory.Content.Contains(item) && !HasOwner(item))
                {
                    try { item.Drop(player, true); }
                    catch (Exception drop) { Debug.LogWarning("[SkyIslandCraft] 原材料待拾取失败：" + drop.Message); }
                }
                Debug.LogWarning("[SkyIslandCraft] 原材料归还通知失败：" + e.Message);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!committed)
                for (int i = reserved.Count - 1; i >= 0; i--)
                {
                    Reserved entry = reserved[i];
                    if (!entry.Touched) continue;
                    try
                    {
                        if (entry.Take < entry.Count && entry.Item != null && !entry.Item.IsBeingDestroyed)
                        {
                            // 只撤销自己那次扣减，保留外部回调另外作出的增减。
                            int current = entry.Item.StackCount;
                            if (current != entry.Count)
                                SetCount(entry.Item, current + entry.Take);
                        }
                        Restore(entry);
                    }
                    catch (Exception e) { Debug.LogWarning("[SkyIslandCraft] 原材料恢复失败：" + e.Message); }
                }
            reserved.Clear();
        }

        internal static bool HasOwner(Item item)
        {
            return item != null && (item.InInventory != null || item.PluggedIntoSlot != null
                || (item.AgentUtilities != null && item.AgentUtilities.ActiveAgent != null));
        }

        internal static bool HasBufferReceipt(int instanceId, int typeId)
        {
            if (instanceId == 0) return false;
            try
            {
                List<ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
                if (buffer == null) return false;
                for (int i = 0; i < buffer.Count; i++)
                {
                    ItemTreeData entry = buffer[i];
                    if (entry != null && entry.rootInstanceID == instanceId && entry.RootTypeID == typeId) return true;
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandCraft] 待领取缓冲核对失败：" + e.Message); }
            return false;
        }

        internal static void DestroyUnowned(Item item)
        {
            if (item == null || item.IsBeingDestroyed || HasOwner(item)) return;
            try { item.DestroyTree(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandCraft] 未交付物品清理失败：" + e.Message); }
        }

        private static void SetCount(Item item, int count)
        {
            try { item.StackCount = count; }
            catch (Exception e)
            {
                if (item.StackCount != count)
                {
                    try { item.SetInt("Count", count, true); }
                    catch (Exception fallback) { Debug.LogWarning("[SkyIslandCraft] 数量恢复失败：" + fallback.Message); }
                }
                Debug.LogWarning("[SkyIslandCraft] 数量变更通知失败：" + e.Message);
            }
        }

        /// <summary>
        /// 保留自动合堆。官方 Combine 先加目标、后减来源，每次回读目标实际增加量修复
        /// 被中断的来源扣减；剩余物品再走官方入包/落地。全部交付之前可还原合堆。
        /// </summary>
        internal static bool TryDeliver(Item item, CharacterMainControl player)
        {
            if (item == null || player == null || player.CharacterItem == null) return false;
            Inventory pack = player.CharacterItem.Inventory;
            if (pack == null) return false;
            int original = item.StackCount;
            var targets = new List<Item>();
            var before = new List<int>();
            var movedCounts = new List<int>();
            try
            {
                if (item.Stackable)
                {
                    // 回调可改变 Content 长度，所以先定住本次候选表，不在外部回调后继续索引活列表。
                    for (int i = 0; i < pack.Content.Count; i++)
                    {
                        Item target = pack.Content[i];
                        if (target != null && target != item && target.TypeID == item.TypeID
                            && target.StackCount < target.MaxStackCount) targets.Add(target);
                    }
                    for (int i = 0; i < targets.Count; i++)
                    {
                        Item target = targets[i];
                        if (target == null || target.IsBeingDestroyed || target.InInventory != pack) break;
                        int sourceCount = item.StackCount, targetCount = target.StackCount;
                        before.Add(targetCount);
                        try { target.Combine(item); }
                        catch (Exception e)
                        {
                            int moved = Math.Max(0, Math.Min(sourceCount, target.StackCount - targetCount));
                            if (item != null && !item.IsBeingDestroyed) SetCount(item, sourceCount - moved);
                            Debug.LogWarning("[SkyIslandCraft] 合堆通知失败：" + e.Message);
                        }
                        movedCounts.Add(Math.Max(0, Math.Min(sourceCount, target.StackCount - targetCount)));
                        if (item == null || item.IsBeingDestroyed || item.StackCount <= 0)
                        {
                            DestroyUnowned(item);
                            return true;
                        }
                    }
                }
                // 已逐堆合并过；这里禁止再次进入 Combine，才能根据这个原件的归属确认交付。
                try { ItemUtilities.SendToPlayer(item, true, false); }
                catch (Exception e)
                {
                    if (HasOwner(item)) return true;
                    Debug.LogWarning("[SkyIslandCraft] 交付失败：" + e.Message);
                }
                if (HasOwner(item)) return true;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandCraft] 交付准备失败：" + e.Message); }

            // 未交付的成品保留在调用者手里。只还原本事务触碰的堆，不造第二份退款物。
            for (int i = before.Count - 1; i >= 0; i--)
                if (i < movedCounts.Count && targets[i] != null && !targets[i].IsBeingDestroyed)
                {
                    int count = targets[i].StackCount;
                    if (count >= movedCounts[i]) SetCount(targets[i], count - movedCounts[i]);
                    else Debug.LogWarning("[SkyIslandCraft] 合堆后物品被外部再次消费，无法完整撤销");
                }
            if (item != null && !item.IsBeingDestroyed) SetCount(item, original);
            return false;
        }
    }
}

using System;
using System.Collections.Generic;

namespace UnityEngine { public static class Debug { public static void LogWarning(string value) { } } }
namespace ItemStatsSystem
{
    public sealed class Item
    {
        private static int nextId = 10;
        public int TypeID;
        public int Id = nextId++;
        public bool IsBeingDestroyed;
        public Inventory InInventory;
        public object PluggedIntoSlot;
        public AgentUtilities AgentUtilities;
        public int GetInstanceID() { return Id; }
        public void DestroyTree() { IsBeingDestroyed = true; }
    }
    public sealed class AgentUtilities { public object ActiveAgent; }
    public sealed class Inventory { public readonly List<Item> Items = new List<Item>(); }
    public sealed class ItemTreeData { public int rootInstanceID; public int RootTypeID; }
    public static class ItemAssetsCollection
    {
        public static bool MissingPrefab;
        public static Item GetPrefab(int typeId) { return MissingPrefab ? null : new Item { TypeID = typeId }; }
        public static Item InstantiateSync(int typeId) { return new Item { TypeID = typeId }; }
    }
}
namespace BossRush
{
    using ItemStatsSystem;
    public static class ModBehaviour { public static void DevLog(string value) { } }
    // Keep the fixture aligned with the production ID table. 500083 is the
    // batch-four cloudmoss veil; using it here made this delivery regression
    // silently exercise the wrong item identity.
    public static class BossRushItemIds { public const int SkyIslandHomecomingBadge = 500068; }
    public static class SkyIslandInventoryTransaction
    {
        public static readonly List<ItemTreeData> Buffer = new List<ItemTreeData>();
        public static bool HasOwner(Item item) { return item != null && (item.InInventory != null || item.PluggedIntoSlot != null || (item.AgentUtilities != null && item.AgentUtilities.ActiveAgent != null)); }
        public static bool HasBufferReceipt(int instanceId, int typeId) { foreach (var e in Buffer) if (e.rootInstanceID == instanceId && e.RootTypeID == typeId) return true; return false; }
        public static void DestroyUnowned(Item item) { if (item != null && !item.IsBeingDestroyed && !HasOwner(item)) item.DestroyTree(); }
    }
    public static class ItemUtilities
    {
        public static bool ThrowBefore;
        public static bool ThrowAfterOwnership;
        public static bool ThrowAfterBuffer;
        public static void SendToPlayer(Item item) { if (ThrowBefore) throw new InvalidOperationException("transfer unavailable"); item.InInventory = new Inventory(); item.InInventory.Items.Add(item); if (ThrowAfterOwnership) throw new InvalidOperationException("notification failed"); }
        public static void SendToPlayerStorage(Item item) { SkyIslandInventoryTransaction.Buffer.Add(new ItemTreeData { rootInstanceID = item.GetInstanceID(), RootTypeID = item.TypeID }); if (ThrowAfterBuffer) throw new InvalidOperationException("buffer notification failed"); }
    }
}

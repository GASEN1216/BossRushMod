using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Duckov.Economy;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            BossRush.Program.Events.Add("destroy:" + go.Name);
            go.Destroyed = true;
            foreach (var component in go.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        public string Name;
        public readonly List<Object> Components = new List<Object>();
    }
}
namespace ItemStatsSystem
{
    public class Item { }
    public static class ItemAssetsCollection
    {
        public static readonly List<int> Created = new List<int>();
        public static Item InstantiateSync(int id) { Created.Add(id); return new Item(); }
    }
}
namespace Duckov.Economy
{
    public class StockShop : UnityEngine.Object
    {
        public class Entry { public int ItemTypeID; }
        public UnityEngine.GameObject gameObject;
        public List<Entry> entries = new List<Entry>();
        public Dictionary<int, Item> Items;
        public StockShop(string name, params int[] ids)
        {
            gameObject = new UnityEngine.GameObject { Name = name };
            gameObject.Components.Add(this);
            foreach (var id in ids) entries.Add(new Entry { ItemTypeID = id });
        }
    }
}
namespace BossRush
{
    public class CharacterMainControl : UnityEngine.Object
    {
        public UnityEngine.GameObject gameObject;
        public CharacterMainControl(string name)
        { gameObject = new UnityEngine.GameObject { Name = name }; gameObject.Components.Add(this); }
    }
    public class InteractableBase { }
    public static class ModBehaviour { public static void DevLog(string text) { } }
    public static class BossRushEagerReflectionCache
    { public static FieldInfo StockShop_ItemInstances = typeof(StockShop).GetField("Items"); }
    internal sealed partial class ModeEFMerchantRuntime
    {
        private sealed class CleanupPolicy
        {
            public Action ClearPetCache;
            public Func<bool> HasShellSessionState;
            public Action<string> InvalidateShellMerchantGeneration;
        }
        private readonly CleanupPolicy policy;
        private readonly List<StockShop> modeEMerchantShops;
        private CharacterMainControl modeEMerchantNPC;
        private InteractableBase modeEMerchantMainInteract;
        public ModeEFMerchantRuntime(List<StockShop> sharedShops, CharacterMainControl npc, bool shell)
        {
            modeEMerchantShops = sharedShops;
            modeEMerchantNPC = npc;
            modeEMerchantMainInteract = new InteractableBase();
            policy = new CleanupPolicy
            {
                ClearPetCache = () => Program.Events.Add("pet"),
                HasShellSessionState = () => { Program.Events.Add("scope"); return shell; },
                InvalidateShellMerchantGeneration = reason =>
                {
                    Program.Events.Add("invalidate:" + reason);
                    foreach (var shop in sharedShops) UnityEngine.Object.Destroy(shop.gameObject);
                    sharedShops.Clear();
                }
            };
        }
        public IEnumerator Warmup() { return CacheAllModeFShopItemInstancesAsync(); }
        public bool Empty { get { return modeEMerchantShops.Count == 0 && modeEMerchantNPC == null && modeEMerchantMainInteract == null; } }
    }
    public static class Program
    {
        public static readonly List<string> Events = new List<string>();
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
        public static void Main()
        {
            StockShop first = new StockShop("first"), second = new StockShop("second"), dead = new StockShop("dead");
            var shared = new List<StockShop> { first, dead, second };
            UnityEngine.Object.Destroy(dead.gameObject);
            Events.Clear();
            var runtime = new ModeEFMerchantRuntime(shared, new CharacterMainControl("npc"), false);
            runtime.CleanupModeEMerchant();
            Check(Events.SequenceEqual(new[] { "pet", "scope", "destroy:second", "destroy:first", "destroy:npc" }),
                "shared cleanup invalidates scope before reverse shops and merchant");
            Check(runtime.Empty && shared.Count == 0, "cleanup clears the exact shared shop list and entity references");
            Events.Clear(); runtime.CleanupModeEMerchant();
            Check(Events.SequenceEqual(new[] { "pet", "scope" }), "repeated cleanup does not destroy retired entities again");
            shared.Add(new StockShop("shell"));
            runtime = new ModeEFMerchantRuntime(shared, new CharacterMainControl("shellNpc"), true);
            Events.Clear(); runtime.CleanupModeEMerchant();
            Check(Events.SequenceEqual(new[] { "pet", "scope", "invalidate:CleanupModeEMerchant", "destroy:shell", "destroy:shellNpc" }),
                "shell policy can retire shared shops before generic cleanup");

            first = new StockShop("warmFirst", 1, 2, 3, 4, 5, 6, 7, 8, 9);
            second = new StockShop("warmSecond", 10, 11);
            shared.Add(first); shared.Add(second);
            runtime = new ModeEFMerchantRuntime(shared, null, false);
            var routine = runtime.Warmup();
            Check(routine.MoveNext() && routine.Current == null && ItemAssetsCollection.Created.Count == 8,
                "shop warmup yields after eight attempts");
            UnityEngine.Object.Destroy(first.gameObject);
            shared.Clear(); shared.Add(new StockShop("successor", 90));
            Check(!routine.MoveNext(), "remaining snapshot entries finish without an extra frame");
            Check(ItemAssetsCollection.Created.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 })
                && second.Items.Count == 2, "warmup owns snapshot, skips destroyed shop and leaves successor alone");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Duckov.MiniMaps
{
    public sealed class SimplePointOfInterest : Component
    {
        public Color Color; public bool IsArea; public float AreaRadius;
        public string Key;
        public void Setup(object icon, string key) { Key = key; }
    }
}
namespace Duckov.UI
{
    internal sealed class LootView
    {
        internal static LootView Instance;
        internal bool open;
        internal ItemStatsSystem.Inventory TargetInventory;
        internal Action OnClose;
        internal void Close()
        {
            if (!open) return;
            open = false;
            OnClose?.Invoke();
            TargetInventory = null;
        }
    }
}
namespace ItemStatsSystem
{
    internal sealed class Inventory : Component
    {
        internal readonly List<Item> Content = new List<Item>();
    }
}
#if !TIDEGATE_REAL_CATALOG
namespace ItemStatsSystem
{
    internal enum DisplayQuality { None, White, Green, Blue, Purple, Orange, Red, Q7, Q8 }
    internal sealed class Item : Component
    {
        internal int TypeID, Quality, Value;
        internal Inventory InInventory;
        internal bool IsBeingDestroyed { get { return dead; } }
        internal DisplayQuality DisplayQuality;
        internal bool CanBeSold = true;
        internal readonly HashSet<string> Tags = new HashSet<string>();
    }
    internal static class ItemAssetsCollection
    {
        internal static bool NoCargoPrefabs;
        internal static int ThrowCategory = -1;
        internal static int MissingPrefab;
        internal static Item GetPrefab(int typeId)
        {
            if (typeId == MissingPrefab) return null;
            if (typeId / 10000 == ThrowCategory) throw new InvalidOperationException("catalog lookup unavailable");
            if (NoCargoPrefabs && typeId >= 10000) return null;
            int q = typeId >= 10000 ? (typeId / 100) % 100 : 2;
            int value = q >= 7 ? 18000 : q == 6 ? 9000 : q == 5 ? 3500 : q == 4 ? 1500 : 300;
            return new Item { TypeID = typeId, Quality = q, Value = value, DisplayQuality = (DisplayQuality)q };
        }
    }
}
#endif
namespace BossRush
{
    internal sealed class CharacterMainControl : Component { }
    internal sealed class TidegateWorld
    {
        public readonly GameObject Root = new GameObject("World");
        public readonly List<Transform> LootMarkers = new List<Transform>(), Pois = new List<Transform>();
        public bool TryGroundAt(Vector3 p, out float y) { y = p.y; return true; }
        public bool InLagoon(Vector3 p) { return false; }
    }
    internal sealed class TidegateTide { public float Level = -10, LagoonLevel = -10; }
    internal static class TidegateTideRules { public static bool IsDeep(float level, float ground) { return level-ground > .5f; } }
    internal static class TidegateStory { public const int PageCount=4; }
    internal static class TidegateRaidScene { public const string LogTag="[Tidegate]"; }
    internal static class TidegateInventory { public static bool Carries(CharacterMainControl player,int id) { return false; } }
    internal static class L10n { public static string T(string zh,string en) { return en; } }
    internal static class LocalizationHelper
    {
        public static void InjectLocalization(string k,string v) { }
        public static void InjectLocalizations(Dictionary<string,string> values) { }
    }
    internal sealed class InteractableLootbox : Component
    {
        internal static event Action<InteractableLootbox> OnStartLoot;
        internal static int Subscribers { get { return OnStartLoot == null ? 0 : OnStartLoot.GetInvocationList().Length; } }
        internal bool Interacting;
        internal bool NativeLooting;
        internal void Open() { Interacting = true; OnStartLoot?.Invoke(this); NativeLooting = true; }
        internal void StopInteract() { Interacting = false; NativeLooting = false; }
        internal ItemStatsSystem.Inventory Inventory;
        public readonly List<Stack> Stocks = new List<Stack>();
    }
    internal sealed class Stack { public int Id, Amount; public string Source; }
    internal static class SkyIslandRewardCrate
    {
        public static readonly Dictionary<string, InteractableLootbox> Boxes = new Dictionary<string, InteractableLootbox>();
        public static bool RejectCargo;
        public static string FailBuild;
        internal static Func<InteractableLootbox, int, bool> Push;
        internal static int InvalidStockType;
        public static InteractableLootbox Build(Transform parent,Vector3 position,float yaw,string name,out string error,bool pending)
        {
            if (name == "TidegateLoot_" + FailBuild) { error = "simulated missing container"; return null; }
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = position;
            var box = go.AddComponent<InteractableLootbox>();
            box.Inventory = go.AddComponent<ItemStatsSystem.Inventory>();
            Boxes[name.Substring("TidegateLoot_".Length)] = box;
            error = null; return box;
        }
        public static bool AddOfficialStack(InteractableLootbox box,int id,int amount,StringBuilder log,string tag)
        {
            if (id <= 0 || (tag == "TidegateCargo" && RejectCargo)) return false;
            ItemStatsSystem.Item prefab;
            try { prefab = ItemStatsSystem.ItemAssetsCollection.GetPrefab(id); if (prefab == null) return false; }
            catch (InvalidOperationException) { return false; }
            if (Push != null && !Push(box, id)) return false;
            if (box == null) return false;
            var item = new GameObject("Loot item").AddComponent<ItemStatsSystem.Item>();
            item.TypeID = id == InvalidStockType ? 1 : id;
            item.Quality = prefab.Quality;
            item.InInventory = box.Inventory;
            item.transform.SetParent(box.transform, false);
            box.Inventory.Content.Add(item);
            box.Stocks.Add(new Stack { Id = id, Amount = amount, Source = tag }); return true;
        }
    }
#if !TIDEGATE_REAL_CATALOG
    internal static class SkyIslandLootPools
    {
        public static readonly HashSet<SkyIslandLootCategory> Missing = new HashSet<SkyIslandLootCategory>();
        public static int PickCategory(SkyIslandLootCategory cat,SkyIslandLootTier tier,Random random)
        { return Missing.Contains(cat) ? 0 : 10000 * ((int)cat + 1) + (int)tier*100 + random.Next(1,4); }
        public static int Pick(SkyIslandLootTier tier,bool prefer,Random random) { return 500 + (int)tier; }
        public static bool IsCategoryCached(SkyIslandLootCategory cat,int min,int max) { return true; }
        public static int[] GetCategoryBand(SkyIslandLootCategory cat,int min,int max)
        {
            if (Missing.Contains(cat)) return new int[0];
            return Enumerable.Range(min,max-min+1).Select(q => 10000*((int)cat+1) + q*100 + 1).ToArray();
        }
    }
    internal static class SkyIslandEnemyArmoryRules
    {
        internal static bool IsOwnModItem(int id) { return id >= 500000 && id < 510000; }
    }
#endif
}

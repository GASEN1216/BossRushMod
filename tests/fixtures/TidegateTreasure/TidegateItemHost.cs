using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ItemStatsSystem;
using UnityEngine;
using BossRush;

// The clone/registration/configuration code is the real RelicEggConfig + TidegateItems.
// This host replaces Unity object cloning, the dynamic-entry store, icons and usage plumbing only.
internal static class TidegateItemHost
{
    private static readonly Dictionary<int,Item> custom = new Dictionary<int,Item>();
    private static readonly HashSet<int> registering = new HashSet<int>();
    internal static void LoadTemplate()
    {
        var d = JsonDocument.Parse(File.ReadAllText("tests/fixtures/TidegateTreasure/FallbackTemplate.json")).RootElement;
        var source = new GameObject("PortableSafeZoneTemplate").AddComponent<Item>();
        source.TypeID=d.GetProperty("typeID").GetInt32(); source.Quality=d.GetProperty("quality").GetInt32();
        source.DisplayQuality=(DisplayQuality)d.GetProperty("displayQuality").GetInt32();
        source.Value=d.GetProperty("value").GetInt32(); source.MaxStackCount=1; source.StackCount=1;
        source.Tags=new HashSet<string>{"Special"}; custom[source.TypeID]=source;
    }
    internal static void Register(Item item) { custom[item.TypeID]=item; }
    internal static Item Get(int id)
    {
        Item result;
        if (custom.TryGetValue(id,out result)) return result;
        if (!registering.Add(id)) return null;
        try
        {
            if (id==BossRushItemIds.RelicEgg) RelicEggConfig.EnsureRuntimeRegistration();
            else if (TidegateItems.IsTidegateItem(id)) TidegateItems.EnsureRuntimeRegistration(id);
            return custom.TryGetValue(id,out result) ? result : null;
        }
        finally { registering.Remove(id); }
    }
}

namespace ItemStatsSystem
{
    public sealed class ItemVariables
    {
        public void Set(string key,string value,bool overwrite) { }
        public void SetDisplay(string key,bool display) { }
        public string GetString(string key) { return null; }
    }
}
namespace UnityEngine
{
    public enum HideFlags { HideAndDontSave }
    public partial class GameObject { public HideFlags hideFlags; }
    public partial class Component
    {
        public T GetComponent<T>() where T : Component
        { return gameObject == null ? null : gameObject.components.OfType<T>().FirstOrDefault(); }
    }
    public partial class Object
    {
        public static void DontDestroyOnLoad(Object value) { }
        public static Item Instantiate(Item source)
        {
            var clone=new GameObject("clone").AddComponent<Item>();
            clone.TypeID=source.TypeID; clone.Quality=source.Quality; clone.DisplayQuality=source.DisplayQuality;
            clone.Value=source.Value; clone.MaxStackCount=source.MaxStackCount; clone.StackCount=source.StackCount;
            clone.Tags=new HashSet<string>(source.Tags);
            return clone;
        }
    }
}
namespace Duckov.ItemUsage
{
    public class UsageBehavior : Component { }
    public sealed class UsageUtilities : Component { public List<UsageBehavior> behaviors; }
}
namespace BossRush
{
    // Constants live in ConfigPetNest / Awen's unrelated integration owner, excluded from this fixture.
    public static partial class BossRushItemIds { public const int RelicEgg=500059; }
    internal static class AwenLootSweepTokenConfig { internal const int TYPE_ID=500042; }
    internal static class ItemFactory
    {
        internal static void RegisterConfigurator(int id,Action<Item> configure) { }
        internal static Item GetLoadedItem(int id) { return null; }
    }
    internal static class ModeFItemConfigHelper
    {
        internal static void ClearInheritedUsage(Item item) { }
        internal static void SetHiddenMember(Item item,string name,object value) { }
        internal static void BindUsageUtilitiesToItem(Item item,Duckov.ItemUsage.UsageUtilities usage,float time) { }
    }
    internal static class EquipmentHelper { internal static void AddTagToItem(Item item,string tag) { item.Tags.Add(tag); } }
    internal static class EquipmentHelperIcon { internal static void TryInjectIcon(Item item,object bundle,string name) { } }
    internal sealed class TidegateLeadUsage : Duckov.ItemUsage.UsageBehavior { }
    internal sealed class TidegateLetterUsage : Duckov.ItemUsage.UsageBehavior { }
}

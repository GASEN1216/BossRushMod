using System;
using System.Collections.Generic;
using BossRush;
using Duckov.Buildings;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a,Object b) {bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}
        public static bool operator !=(Object a,Object b) {return !(a==b);}
        public override bool Equals(object other) {return ReferenceEquals(this,other);}
        public override int GetHashCode() {return base.GetHashCode();}
        internal static BuildingArea Area;
        internal static bool ThrowArea;
        internal static int ShowcaseScans;
        internal static Showcase[] Showcases=new Showcase[0];
        public static T FindObjectOfType<T>(bool inactive) where T:class {if(ThrowArea)throw new Exception("area unavailable");return Area as T;}
        public static T[] FindObjectsOfType<T>(bool inactive) where T:class {ShowcaseScans++;return Showcases as T[];}
    }
}
namespace Duckov.Buildings
{
    public sealed class BuildingArea:UnityEngine.Object {}
    public sealed class Showcase:UnityEngine.Object {public Item Item;}
    public static class BuildingManager {public static event Action<int> OnBuildingBuilt;}
}
namespace Duckov.Utilities {}
namespace ItemStatsSystem
{
    public sealed class Item:UnityEngine.Object {public int TypeID;public SlotCollection Slots=new SlotCollection();public event Action<Item,ItemStatsSystem.Items.Slot> onSlotContentChanged;}
    public sealed class SlotCollection:List<ItemStatsSystem.Items.Slot> {}
}
namespace ItemStatsSystem.Items {public sealed class Slot {public Item Content;}}
namespace BossRush
{
    internal static class ModBehaviour {internal static void DevLog(string message) {}}
    internal static class BackMountainConfig {internal const string LogPrefix="fixture";}
    internal static class ShowcaseTrophyCatalog {internal static bool IsShowcaseTrophy(int id) {return id>0;}}
    // Existing main lifecycle executable links the real persisted service; this boundary records the
    // scanner's authoritative snapshots so missing BuildingArea cannot erase previously read displays.
    internal static class ShowcaseService
    {internal static int Writes;internal static int[] Snapshot=new int[0];internal static void ApplyDisplaySnapshot(int[] ids,bool any) {Writes++;Snapshot=ids;}}
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string reason) {checks++;if(!value)throw new Exception(reason);}
    private static void Main()
    {
        UnityEngine.Object.Area=new BuildingArea();
        var rack=new Showcase{Item=new Item()};rack.Item.Slots.Add(new ItemStatsSystem.Items.Slot{Content=new Item{TypeID=500050}});
        UnityEngine.Object.Showcases=new[]{rack};ShowcaseDisplayScanner.RefreshForScene(true,true);
        Check(ShowcaseService.Writes==1&&ShowcaseService.Snapshot.Length==1&&ShowcaseDisplayScanner.SubscriptionCount==1,"loaded official area publishes actual rack slots");
        int scans=UnityEngine.Object.ShowcaseScans;
        UnityEngine.Object.Area=null;UnityEngine.Object.Showcases=new Showcase[0];ShowcaseDisplayScanner.RefreshForScene(true,true);ShowcaseDisplayScanner.FlushIfDirty();
        Check(ShowcaseService.Writes==1&&ShowcaseService.Snapshot.Length==1&&UnityEngine.Object.ShowcaseScans==scans,"base subscene without BuildingArea cannot erase real display snapshot");
        Check(ShowcaseDisplayScanner.SubscriptionCount==0,"subscene still retires stale slot subscriptions");
        UnityEngine.Object.ThrowArea=true;ShowcaseDisplayScanner.RefreshForScene(true,true);
        Check(ShowcaseService.Writes==1,"unavailable building lookup preserves prior authoritative snapshot");
        UnityEngine.Object.ThrowArea=false;UnityEngine.Object.Area=new BuildingArea();ShowcaseDisplayScanner.RefreshForScene(true,true);
        Check(ShowcaseService.Writes==2&&ShowcaseService.Snapshot.Length==0,"loaded empty building area still clears genuinely removed displays");
        ShowcaseDisplayScanner.ResetStaticCaches();Console.WriteLine("ShowcaseScanner: "+checks+" PASS");
    }
}

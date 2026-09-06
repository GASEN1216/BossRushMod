using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;

namespace UnityEngine
{
    public class GameObject {}
    public class Component {}
    public class Transform : Component
    {
        public GameObject gameObject = new GameObject();
        public Component child;
        public Component GetComponent(Type type) { return child != null && type.IsInstanceOfType(child) ? child : null; }
    }
}
namespace Duckov.Buildings
{
    class Building : UnityEngine.Component { public string ID { get { return "fixture_building"; } } }
    static class BuildingManager
    {
        public static bool Any(string id, bool includeDestroyed) { return id == "fixture_building" && !includeDestroyed; }
        public static bool Any(string id) { throw new Exception("wrong overload"); }
        private static string GetBuildingData(int id) { return "data_" + id; }
    }
}
namespace BossRush
{
    public partial class ModBehaviour
    {
        private static UnityEngine.GameObject starwishModelPrefab = new UnityEngine.GameObject();
    }
    class BuilderSpy
    {
        internal static readonly List<BuilderSpy> Created = new List<BuilderSpy>();
        internal readonly ModBehaviour Owner;
        internal readonly List<string> Calls = new List<string>();
        internal BuilderSpy(ModBehaviour owner) { Owner = owner; Created.Add(this); }
    }
    class DailyReportMailboxBuilder : BuilderSpy
    {
        internal DailyReportMailboxBuilder(ModBehaviour owner) : base(owner) {}
        public void InitDailyReportMailbox() { Calls.Add("init"); }
        internal void TryInitializeDailyReportMailboxEarly() { Calls.Add("early"); }
        public void RestoreDailyReportMailboxes() { Calls.Add("restore"); }
        public void CleanupDailyReportMailbox() { Calls.Add("cleanup"); }
    }
    class CampaignBoardBuilder : BuilderSpy
    {
        internal CampaignBoardBuilder(ModBehaviour owner) : base(owner) {}
        public void InitCampaignBoardBuilding() { Calls.Add("init"); }
        internal void TryInitializeCampaignBoardEarly() { Calls.Add("early"); }
        internal void RegisterCampaignNotesForScene() { Calls.Add("notes"); }
        public void CleanupCampaignBoardBuilding() { Calls.Add("cleanup"); }
    }
    class ShowcaseBuildingBuilder : BuilderSpy
    {
        internal ShowcaseBuildingBuilder(ModBehaviour owner) : base(owner) {}
        public void InitBackMountainShowcase() { Calls.Add("init"); }
        internal void TryInitializeBackMountainShowcaseEarly() { Calls.Add("early"); }
        internal void NotifyShowcaseSlotChanged() { Calls.Add("slot"); }
        public void CleanupBackMountainShowcase() { Calls.Add("cleanup"); }
    }
    class PetNestBuilder : BuilderSpy
    {
        internal PetNestBuilder(ModBehaviour owner) : base(owner) {}
        public void InitPetNestBuilding() { Calls.Add("init"); }
        internal void TryInitializePetNestEarly() { Calls.Add("early"); }
        public void RestorePetNestBuildings() { Calls.Add("restore"); }
        public void CleanupPetNestBuilding() { Calls.Add("cleanup"); }
    }
}
class FixtureComponent : UnityEngine.Component {}
class Containers : UnityEngine.Component
{
    public UnityEngine.Transform transformContainer;
    public UnityEngine.GameObject objectContainer;
    public FixtureComponent componentContainer;
    public object genericContainer;
}
class Program
{
    static int checks;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS " + description);
    }
    static BuilderSpy Spy<T>(ModBehaviour owner) where T : BuilderSpy
    {
        var found = BuilderSpy.Created.FindAll(spy => spy is T && ReferenceEquals(spy.Owner, owner));
        if (found.Count != 1) throw new Exception("expected one owner instance for " + typeof(T).Name);
        return found[0];
    }
    static void Main()
    {
        Check(BuildingInjectionHelper.FindGameType("Duckov.Buildings.Building") == typeof(Duckov.Buildings.Building), "shared reflection resolves loaded game type");
        Check(BuildingInjectionHelper.FindGameType("No.Such.GameType") == null, "unknown reflection type remains a safe miss");
        Check(BuildingInjectionHelper.GetBuildingType() == typeof(Duckov.Buildings.Building), "building type cache resolves expected type");
        Check(BuildingInjectionHelper.GetBuildingManagerType() == typeof(Duckov.Buildings.BuildingManager), "manager cache resolves expected type");
        var any = BuildingInjectionHelper.GetBuildingManagerAnyMethod();
        Check((bool)any.Invoke(null, new object[] { "fixture_building", false })
            && ReferenceEquals(any, BuildingInjectionHelper.GetBuildingManagerAnyMethod()), "Any lookup keeps exact two-argument overload and cached identity");
        Check((string)BuildingInjectionHelper.GetBuildingDataMethod().Invoke(null, new object[] { 42 }) == "data_42", "private static GetBuildingData reflection remains callable");
        Check((string)BuildingInjectionHelper.GetBuildingIdProperty().GetValue(new Duckov.Buildings.Building()) == "fixture_building", "building ID property binding remains readable");
        var target = new Containers();
        var container = new UnityEngine.Transform { child = new FixtureComponent() };
        foreach (string field in new[] { "transformContainer", "objectContainer", "componentContainer", "genericContainer" })
            BuildingInjectionHelper.AssignBuildingContainerField(typeof(Containers).GetField(field), target, container);
        Check(ReferenceEquals(target.transformContainer, container), "Transform container receives Transform");
        Check(ReferenceEquals(target.objectContainer, container.gameObject), "GameObject container receives GameObject");
        Check(ReferenceEquals(target.componentContainer, container.child), "component container resolves requested component");
        Check(ReferenceEquals(target.genericContainer, container), "fallback container preserves original assignment");
        BuildingInjectionHelper.AssignBuildingContainerField(typeof(Containers).GetField("transformContainer"), target, null);
        Check(target.transformContainer == null, "null container clears field");
        BuildingInjectionHelper.AssignBuildingContainerField(null, target, container);
        BuildingInjectionHelper.AssignBuildingContainerField(typeof(Containers).GetField("objectContainer"), null, container);
        Check(ReferenceEquals(target.objectContainer, container.gameObject), "missing field or component is a no-op");

        var owner = new ModBehaviour();
        owner.TryInitializeDailyReportMailboxEarly(); owner.InitDailyReportMailbox(); owner.RestoreDailyReportMailboxes(); owner.CleanupDailyReportMailbox();
        Check(string.Join(",", Spy<DailyReportMailboxBuilder>(owner).Calls) == "early,init,restore,cleanup", "mailbox bridge preserves ordered calls on one owner instance");
        owner.TryInitializeCampaignBoardEarly(); owner.InitCampaignBoardBuilding(); owner.RegisterCampaignNotesForScene(); owner.CleanupCampaignBoardBuilding();
        Check(string.Join(",", Spy<CampaignBoardBuilder>(owner).Calls) == "early,init,notes,cleanup", "campaign bridge preserves early init notes and cleanup");
        owner.NotifyShowcaseSlotChanged(); owner.TryInitializeBackMountainShowcaseEarly(); owner.InitBackMountainShowcase(); owner.CleanupBackMountainShowcase();
        Check(string.Join(",", Spy<ShowcaseBuildingBuilder>(owner).Calls) == "slot,early,init,cleanup", "showcase slot callback is forwarded even before initialization");
        typeof(ModBehaviour).GetMethod("TryInitializePetNestEarly", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, null);
        owner.InitPetNestBuilding(); owner.RestorePetNestBuildings(); owner.CleanupPetNestBuilding();
        Check(string.Join(",", Spy<PetNestBuilder>(owner).Calls) == "early,init,restore,cleanup", "pet nest keeps private early entry and ordered lifetime calls");
        owner.InitDailyReportMailbox();
        Check(Spy<DailyReportMailboxBuilder>(owner).Calls.Count == 5, "cleanup does not replace module state during same host lifetime");
        var other = new ModBehaviour(); other.InitDailyReportMailbox();
        Check(!ReferenceEquals(Spy<DailyReportMailboxBuilder>(owner), Spy<DailyReportMailboxBuilder>(other)), "new host owns an independent building module");
        Check(ReferenceEquals(owner.StarwishBuildingModelPrefab, other.StarwishBuildingModelPrefab), "borrowed model remains the existing shared resource without another loader");
        var cleanupOnly = new ModBehaviour();
        cleanupOnly.CleanupDailyReportMailbox(); cleanupOnly.CleanupCampaignBoardBuilding(); cleanupOnly.CleanupBackMountainShowcase(); cleanupOnly.CleanupPetNestBuilding();
        Check(Spy<DailyReportMailboxBuilder>(cleanupOnly).Calls[0] == "cleanup"
            && Spy<CampaignBoardBuilder>(cleanupOnly).Calls[0] == "cleanup"
            && Spy<ShowcaseBuildingBuilder>(cleanupOnly).Calls[0] == "cleanup"
            && Spy<PetNestBuilder>(cleanupOnly).Calls[0] == "cleanup", "original cleanup obligations still run if initialization never completed");
        Console.WriteLine("Building ownership regression checks=" + checks);
    }
}

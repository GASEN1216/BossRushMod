using System;
using System.Linq;
using BossRush;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static FlightTotemRuntimeModule Create(out ModBehaviour host)
    {
        host = new ModBehaviour();
        var module = new FlightTotemRuntimeModule();
        module.OnAwake(host);
        return module;
    }

    private static void Ordered(params string[] events)
    {
        int previous = -1;
        foreach (string entry in events)
        {
            int index = TestTrace.Events.IndexOf(entry);
            Assert(index > previous, "Missing or out-of-order event: " + entry);
            previous = index;
        }
    }

    private static void CheckConfigured(Item item)
    {
        Assert(item.Variables.Values.Count == 7, "The loaded item must receive all seven flight variables");
        Assert(item.Variables.Values[FlightConfig.VAR_MAX_UPWARD_SPEED] == 10f, "Upward speed changed");
        Assert(item.Variables.Values[FlightConfig.VAR_ACCELERATION_TIME] == 0.3f, "Acceleration time changed");
        Assert(item.Variables.Values[FlightConfig.VAR_GLIDING_MULTIPLIER] == 0.8f, "Gliding multiplier changed");
        Assert(item.Variables.Values[FlightConfig.VAR_DESCENT_SPEED] == 0.8f, "Displayed descent speed must remain positive");
        Assert(item.Variables.Values[FlightConfig.VAR_STARTUP_STAMINA] == 5f, "Startup stamina changed");
        Assert(item.Variables.Values[FlightConfig.VAR_FLIGHT_STAMINA_DRAIN] == 50f, "Flight drain changed");
        Assert(item.Variables.Values[FlightConfig.VAR_GLIDING_STAMINA_DRAIN] == 30f, "Gliding drain changed");
        Assert(item.Variables.Displays.Count == 7 && item.Variables.Displays.Values.All(value => value),
            "Configured flight variables must remain visible");
    }

    private static void TestInitializationOwnershipAndReuse()
    {
        TestTrace.Reset();
        var itemA = new Item(FlightConfig.TotemTypeIdBase);
        ItemAssetsCollection.Responses.Enqueue(() => itemA);
        ModBehaviour hostA;
        var ownerA = Create(out hostA);
        ownerA.InitializeFlightTotemSystem();
        Ordered("ability.ensure", "effect.ensure", "bundle:flight_totem", "item:500010",
            "display:" + FlightConfig.VAR_GLIDING_STAMINA_DRAIN, "localize:BossRush_FlightTotem");
        CheckConfigured(itemA);
        Assert(LocalizationHelper.Values["BossRush_FlightTotem"] == FlightConfig.Instance.DisplayNameCN,
            "First initialization must inject the current language");
        Assert(LocalizationHelper.Values["Item_500010_Desc"] == FlightConfig.Instance.DescriptionCN,
            "The item ID description key must remain available");

        L10n.English = true;
        ownerA.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 1 && ItemAssetsCollection.InstantiateCalls == 1,
            "One owner must not repeat bundle loading or item creation");
        Assert(LocalizationHelper.Values["BossRush_FlightTotem"] == FlightConfig.Instance.DisplayNameEN,
            "Repeated initialization must still refresh localization in the current language");

        EquipmentFactory.LoadResult = 0;
        var itemB = new Item(FlightConfig.TotemTypeIdBase);
        ItemAssetsCollection.Responses.Enqueue(() => itemB);
        ModBehaviour hostB;
        var ownerB = Create(out hostB);
        ownerB.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 2 && ItemAssetsCollection.InstantiateCalls == 2,
            "A second owner must initialize independently even when its bundle was automatically loaded");
        CheckConfigured(itemB);
        Assert(!ReferenceEquals(itemA, itemB), "Owner-specific loaded instances must remain separate");
        ownerA.OnDestroy();
        ownerB.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 2, "Destroying another owner must not reset this owner's initialization");
    }

    private static void TestFailureLatch()
    {
        TestTrace.Reset();
        EquipmentFactory.FailLoad = true;
        ModBehaviour host;
        var owner = Create(out host);
        owner.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 1 && ItemAssetsCollection.InstantiateCalls == 0,
            "A failed bundle load must stop item creation");
        Assert(LocalizationHelper.Values.ContainsKey("BossRush_FlightTotem"),
            "Factory failure must retain the following localization phase");
        EquipmentFactory.FailLoad = false;
        owner.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 1, "The original initialization latch must remain set after failure");

        ItemAssetsCollection.Responses.Enqueue(() => { throw new InvalidOperationException("missing prefab"); });
        var second = Create(out host);
        second.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 2 && ItemAssetsCollection.InstantiateCalls == 1,
            "An item failure must be isolated to its owner");
        second.InitializeFlightTotemSystem();
        Assert(ItemAssetsCollection.InstantiateCalls == 1, "Item failure must keep the original non-retry behavior");

        ItemAssetsCollection.Responses.Enqueue(() => null);
        var third = Create(out host);
        third.InitializeFlightTotemSystem();
        Assert(!TestTrace.Events.Any(entry => entry.StartsWith("set:")),
            "Missing item results must not be configured");
    }

    private static void TestSceneWaitAndCleanup()
    {
        TestTrace.Reset();
        ItemAssetsCollection.Responses.Enqueue(() => new Item(FlightConfig.TotemTypeIdBase));
        ModBehaviour host;
        var owner = Create(out host);
        owner.InitializeFlightTotemSystem();
        TestTrace.Events.Clear();
        owner.SetupFlightTotemForScene(new Scene("Gameplay"));
        Ordered("ability.scene", "coroutine.start");
        Assert(host.Coroutines.Count == 1, "Gameplay scene setup must schedule exactly one equipment check");
        var routine = host.Coroutines[0];
        Assert(routine.MoveNext() && ((WaitForSeconds)routine.Current).Seconds == 0.5f,
            "Shared helper must wait its original half second");
        Assert(!TestTrace.Events.Contains("equipment.check"), "Equipment check ran before the helper wait");
        Assert(routine.MoveNext() && ReferenceEquals(routine.Current, ModBehaviour.FlightTotemSharedWait05sForRuntime),
            "Module must preserve the second shared half-second wait object");
        Assert(!TestTrace.Events.Contains("equipment.check"), "Equipment check ran before the module wait");
        Assert(!routine.MoveNext() && TestTrace.Events.Count(entry => entry == "equipment.check") == 1,
            "Equipment must be checked once after both original waits");

        TestTrace.Events.Clear();
        owner.SetupFlightTotemForScene(new Scene("Menu"));
        Assert(TestTrace.Events.SequenceEqual(new[] { "ability.scene" }) && host.Coroutines.Count == 1,
            "A non-gameplay scene must rebind without adding a delayed check");

        var effect = FlightTotemEffectManager.Instance;
        TestTrace.Events.Clear();
        owner.CleanupFlightTotemSystem();
        Ordered("ability.cleanup", "destroy:FlightTotemEffectManager");
        Assert(effect == null && FlightAbilityManager.Instance == null,
            "Cleanup must destroy both managers, including the effect component");
        owner.InitializeFlightTotemSystem();
        Assert(EquipmentFactory.LoadCalls == 1,
            "Existing system cleanup must not reset the item initialization latch");
        owner.OnDestroy();
    }

    public static int Main()
    {
        TestInitializationOwnershipAndReuse();
        TestFailureLatch();
        TestSceneWaitAndCleanup();
        Console.WriteLine("FlightTotemRuntimeModule PASS: ownership, initialization, reuse, failure, localization, waits, cleanup");
        return 0;
    }
}

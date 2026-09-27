using System;
using System.Linq;
using BossRush;

internal static class Program
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void DestroyHost(ModBehaviour host, string scenario)
    {
        try { host.InvokeDestroy(); }
        catch (Exception error) { throw new InvalidOperationException(scenario + " destruction must complete without exceptions", error); }
    }

    public static int Main()
    {
        ModBehaviour.SetInstance(null);
        var unawakened = new ModBehaviour();
        DestroyHost(unawakened, "Unawakened");
        Check(Probe.Trace.Count == 0, "An unawakened instance must not touch shared resources");

        var live = new ModBehaviour();
        live.InvokeAwake();
        Check(ReferenceEquals(ModBehaviour.Instance, live), "Real Awake must establish the owning instance");
        Probe.Trace.Clear();
        var duplicate = new ModBehaviour();
        duplicate.InvokeAwake();
        Check(duplicate == null && duplicate.gameObject == null, "Duplicate Awake must destroy the object and its component");
        Check(Probe.Trace.Count == 0, "Duplicate Awake must return before registering modules");
        DestroyHost(duplicate, "Duplicate");
        Check(Probe.Trace.Count == 0, "Duplicate destruction must preserve the live owner's subscriptions, modules and caches");
        Check(ReferenceEquals(ModBehaviour.Instance, live), "Duplicate destruction must preserve the active singleton");

        UnityEngine.Object.Destroy(live.gameObject);
        Check(live == null, "The normal owning instance must exercise Unity fake-null at destruction");
        DestroyHost(live, "Active fake-null owner");
        string[] expected = {
            "modeg.prepare", "language.cleanup", "debug.cleanup", "player.cleanup", "achievement.cleanup",
            "BossRushAchievementManager.ResetStaticCaches", "AchievementIconLoader.ResetStaticCaches",
            "BossRushUI.ResetStaticCaches", "BossRushUISkinLoader.Cleanup", "BossBgmCoordinator.ResetStaticCaches",
            "MutatorUI.ResetStaticCaches", "affix.cleanup", "always.cleanup", "integration.cleanup", "modes.cleanup",
            "module.tail.destroy", "module.achievement.destroy", "BossRushSaveFileThrottle.ResetStaticCaches", "harmony.clear"
        };
        Check(Probe.Trace.SequenceEqual(expected), "Owner destruction must retain the full cleanup order, reversed module dispatch and final save barrier");
        Check(ReferenceEquals(ModBehaviour.Instance, null), "Owner destruction must clear the singleton even when Unity considers it destroyed");
        Probe.Trace.Clear();
        DestroyHost(live, "Repeated owner");
        Check(Probe.Trace.Count == 0, "Repeated owner destruction must not repeat global teardown");

        var replacement = new ModBehaviour();
        replacement.InvokeAwake();
        Probe.Trace.Clear();
        DestroyHost(live, "Late previous owner");
        Check(Probe.Trace.Count == 0 && ReferenceEquals(ModBehaviour.Instance, replacement),
            "A late callback from the old instance must not tear down its replacement");
        DestroyHost(replacement, "Replacement owner");
        Console.WriteLine("HostDestroyOwnership: PASS (unawakened, duplicate, fake-null owner, repeated destroy, replacement, cleanup order)");
        return 0;
    }
}

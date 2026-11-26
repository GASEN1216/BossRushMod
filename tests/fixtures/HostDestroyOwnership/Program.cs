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
        Check(Probe.Trace.SequenceEqual(new[] {
            "startup.begin", "modules.register", "init.bootstrap", "init.always", "init.player", "init.debug",
            "init.achievement", "startup.complete" }),
            "Awake must start the startup guard before any module or Harmony work and finish with the self-check");
        Check(ReferenceEquals(ModBehaviour.Instance, live), "Real Awake must establish the owning instance");
        Probe.Trace.Clear();
        live.InvokeStart();
        Check(Probe.Trace.SequenceEqual(new[] { "init.integration", "module.achievement.start", "module.tail.start", "startup.selfcheck" }),
            "Start must load the integration runtime, dispatch module OnStart and print the self-check last");
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
            "modeg.prepare", "language.cleanup", "startup.shutdown", "debug.cleanup", "player.cleanup", "achievement.cleanup",
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

        ModBehaviour.ThrowStage = "register";
        var halfRegistered = new ModBehaviour();
        Probe.Trace.Clear();
        halfRegistered.InvokeAwake();
        Check(Probe.Trace.SequenceEqual(new[] {
            "startup.begin", "modules.register", "startup.fail:register-modules", "init.bootstrap", "init.always",
            "init.player", "init.debug", "init.achievement", "startup.complete" }),
            "A throwing module registration must be reported and must not stop item registration or the later stages");
        ModBehaviour.ThrowStage = null;
        DestroyHost(halfRegistered, "Half-registered owner");

        ModBehaviour.ThrowStage = "integration";
        var startFailure = new ModBehaviour();
        startFailure.InvokeAwake();
        Probe.Trace.Clear();
        startFailure.InvokeStart();
        Check(Probe.Trace.SequenceEqual(new[] {
            "init.integration", "startup.fail:integration-start", "module.achievement.start", "module.tail.start", "startup.selfcheck" }),
            "A throwing integration start must be reported, every module must still get OnStart, and the self-check must come after");
        ModBehaviour.ThrowStage = null;
        DestroyHost(startFailure, "Start-failure owner");

        foreach (string failing in new[] { "bootstrap", "always", "achievement" })
        {
            ModBehaviour.ThrowStage = failing;
            var isolated = new ModBehaviour();
            Probe.Trace.Clear();
            isolated.InvokeAwake();
            string reported = failing == "bootstrap" ? "startup.fail:harmony-bootstrap"
                : failing == "always" ? "startup.fail:always-on" : "startup.fail:achievement";
            int at = Probe.Trace.IndexOf("init." + failing);
            Check(at >= 0 && Probe.Trace.Count > at + 1 && Probe.Trace[at + 1] == reported,
                "A throwing startup stage must be reported right away: " + failing);
            Check(Probe.Trace.Contains("init.achievement") && Probe.Trace.Last() == "startup.complete",
                "Later startup stages and the self-check must still run after a failure: " + failing);
            ModBehaviour.ThrowStage = null;
            DestroyHost(isolated, "Isolated owner");
        }
        Console.WriteLine("HostDestroyOwnership: PASS (unawakened, duplicate, fake-null owner, repeated destroy, replacement, cleanup order, startup stage isolation, module registration and integration start failures)");
        return 0;
    }
}

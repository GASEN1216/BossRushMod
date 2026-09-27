using System;
using System.Linq;
using UnityEngine;

namespace BossRush
{
    internal static class Program
    {
        private static int assertions;
        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message + " | " + string.Join(";", Probe.Events)); Console.WriteLine("PASS " + message); }
        private static ModBehaviour Host()
        { Probe.Events.Clear(); CharacterMainControl.Main = new CharacterMainControl(); return new ModBehaviour(); }
        private static void Main()
        {
            foreach (string mode in new[] { "D", "E", "F" })
            {
                var dormantHost = Host(); var unrelated = new CharacterMainControl();
                dormantHost.Recovery.Add(unrelated); dormantHost.Mutators.Add("another mode");
                CharacterMainControl.Main.SetTeam(Teams.middle);
                BossRushRuntimeModuleBase dormant = mode == "D" ? (BossRushRuntimeModuleBase)new ModeDRuntimeModule()
                    : mode == "E" ? (BossRushRuntimeModuleBase)new ModeERuntimeModule() : new ModeFRuntimeModule();
                dormant.OnAwake(dormantHost); dormant.OnDestroy();
                Check(dormantHost.Recovery.Contains(unrelated) && dormantHost.Mutators.Contains("another mode")
                    && dormantHost.CourierCleanup == 0 && CharacterMainControl.Main.Team == Teams.middle
                    && !Probe.Events.Contains("scheduler-clear") && !Probe.Events.Contains("extraction-restore"),
                    mode + " never-started destroy leaves another owner's state intact");
                Check(!Probe.Events.Any(x => x.Contains("ERROR") || x.Contains("WARNING")), mode + " dormant destroy performs no unbound cleanup");
            }
            var host = Host(); var d = new ModeDRuntimeModule(); d.OnAwake(host);
            var enemy = new CharacterMainControl(); var task = host.OwnCoroutine();
            d.modeDActive = true; d.modeDWaveIndex = 9; d.modeDWaveCompletePending = true;
            d.modeDCurrentWaveEnemies.Add(enemy); d.modeDAutoNextWaveCoroutine = task;
            host.Recovery.Add(enemy); host.Mutators.Add("ModeD");
            var valid = ModeDRuntimeModule.CaptureValidity(host, false);
            Check(valid(), "D valid before destroy");
            d.OnDestroy();
            Check(!valid() && !d.modeDActive && d.modeDWaveIndex == 0 && !d.modeDWaveCompletePending, "D destroy invalidates run and queued wave state");
            Check(enemy == null && enemy.Health == null && !enemy.dropBoxOnDead && d.modeDCurrentWaveEnemies.Count == 0, "D destroy retires actual registered character without loot");
            Check(task.Stopped && d.modeDAutoNextWaveCoroutine == null && host.Recovery.Count == 0 && host.Mutators.Count == 0 && host.Messages == 0, "D destroy releases coroutine/recovery/mutators without settlement");
            d.OnDestroy();

            foreach (bool destroyedHost in new[] { false, true }) foreach (bool active in new[] { true, false })
            {
                host = Host(); var registry = new ModeEFEnemyRegistry(); var e = new ModeERuntimeModule(); e.Bind(registry); e.OnAwake(host);
                enemy = new CharacterMainControl(); e.Seed(enemy); e.modeEActive = active;
                host.Mutators.Add("ModeE"); host.Recovery.Add(enemy);
                if (destroyedHost) UnityEngine.Object.Destroy(host.gameObject);
                e.OnDestroy();
                Check(!e.modeEActive && e.modeESessionToken == 0 && !e.IsModeESessionStillValid(17, 1), "E destroy invalidates active/partial run " + active);
                Check(!e.IsModeEOrModeFSpawnSessionStillValid(17, 1, 0, -1) && !e.IsModeEOrModeFSpawnSessionStillValid(0, -1, 0, -1), "E disposed gate rejects F and legacy requests without dereferencing host");
                Check(enemy == null && enemy.Health == null && enemy.Health.OnDeadEvent.Count == 0 && enemy.LootHandlerCount == 0 && !enemy.dropBoxOnDead, "E destroy detaches production registry callbacks before character destruction");
                Check(e.PlayerModifier.Removed && e.EnemyModifier.Removed && CharacterMainControl.Main.Team == Teams.player && registry.LegacyAliveEnemies.Count == 0, "E destroy restores player/faction and removes player/enemy modifiers");
                Check(e.Warmup.Stopped && host.Mutators.Count == 0 && host.Recovery.Count == 0 && host.Messages == 0 && e.MerchantCleanup == 1, "E destroy stops warmup and cleans merchant/recovery without end message");
                enemy.Health.OnDeadEvent.Invoke(new DamageInfo()); enemy.FireLoot();
                Check(e.DeathCallbacks == 0 && e.ShellDisposes == 1, "E no late gameplay callback after teardown");
                e.OnDestroy(); Check(e.ShellDisposes == 1 && e.MerchantCleanup == 1, "E repeated destroy is idempotent");
                Check(!Probe.Events.Any(x => x.Contains("ERROR") || x.Contains("WARNING")), "E cleanup has no swallowed exception");
            }

            foreach (bool destroyedHost in new[] { false, true }) foreach (bool active in new[] { true, false })
            {
                host = Host(); var registry = new ModeEFEnemyRegistry(); var e = new ModeERuntimeModule(); e.Bind(registry); e.OnAwake(host);
                e.ScheduleModeEStartupWarmup("Mode F shared startup"); var sharedWarmup = e.CurrentWarmup;
                var f = new ModeFRuntimeModule(); f.Bind(e, registry); f.OnAwake(host); host.ModeF = f;
                enemy = new CharacterMainControl(); var fort = new ModeFFortificationMarker(); f.Seed(enemy, fort);
                var extraction = f.modeFState.ActiveExtractionArea;
                f.modeFActive = active; host.Mutators.Add("ModeF"); host.Recovery.Add(enemy);
                int startsBeforeDestroy = host.StartedCoroutines;
                if (destroyedHost) UnityEngine.Object.Destroy(host.gameObject);
                f.OnDestroy();
                Check(!f.modeFActive && !f.modeFState.IsActive && f.modeFState.RuntimeSessionToken == 0 && !f.IsModeFSessionStillValid(17, 1), "F destroy invalidates active/partial run " + active);
                Check(enemy == null && enemy.Health == null && enemy.Health.OnDeadEvent.Count == 0 && enemy.LootHandlerCount == 0 && !enemy.dropBoxOnDead, "F destroy detaches F and shared E events before disposing Boss");
                Check(f.PlayerModifier.Removed && f.BossModifier.Removed && CharacterMainControl.Main.Health.CurrentHealth == 100, "F destroy removes growth and clamps only granted max-health growth");
                Check(fort == null && f.Preview == null && extraction == null && f.modeFState.ActiveBosses.Count == 0 && registry.LegacyAliveEnemies.Count == 0, "F destroy removes fortification/preview/extraction and clears shared registry");
                Check(f.Awards == 0 && f.Refunds == 2 && f.PendingCount == 0 && host.Messages == 0 && host.StartedCoroutines == startsBeforeDestroy, "F destroy refunds consumed placement/repair once, grants no awards, starts no dying-host coroutine");
                Check(host.Recovery.Count == 0 && host.Mutators.Count == 0, "F destroy clears recovery and mutators");
                Check(sharedWarmup.Stopped && e.CurrentWarmup == null, "F shared reset stops E warmup or releases native-stopped handle with fake-null host");
                enemy.Health.OnDeadEvent.Invoke(new DamageInfo()); enemy.FireLoot(); Check(f.Callbacks == 0, "F no late death/loot callback after teardown");
                int cleanupCalls = host.CourierCleanup; f.OnDestroy(); Check(cleanupCalls == host.CourierCleanup && f.Refunds == 2, "F repeated destroy is idempotent and cannot duplicate refunds");
                Check(!Probe.Events.Any(x => x.Contains("ERROR") || x.Contains("WARNING")), "F cleanup has no swallowed exception");
            }

            host = Host(); var endedRegistry = new ModeEFEnemyRegistry(); var endedE = new ModeERuntimeModule(); endedE.Bind(endedRegistry); endedE.OnAwake(host);
            endedE.Seed(new CharacterMainControl()); endedE.EndModeE(false);
            var sentinel = new CharacterMainControl(); host.Recovery.Add(sentinel); host.Mutators.Add("next mode"); CharacterMainControl.Main.SetTeam(Teams.middle);
            int priorCourier = host.CourierCleanup; endedE.OnDestroy();
            Check(host.Recovery.Contains(sentinel) && host.Mutators.Contains("next mode") && CharacterMainControl.Main.Team == Teams.middle
                && host.CourierCleanup == priorCourier, "E already-ended destroy does not repeat shared/player cleanup");

            host = Host(); var normalRegistry = new ModeEFEnemyRegistry(); var normalE = new ModeERuntimeModule(); normalE.Bind(normalRegistry); normalE.OnAwake(host);
            var normalF = new ModeFRuntimeModule(); normalF.Bind(normalE, normalRegistry); normalF.OnAwake(host);
            enemy = new CharacterMainControl(); normalF.Seed(enemy, new ModeFFortificationMarker()); normalF.ExitModeF(false);
            Check(normalF.Refunds == 2 && normalF.Awards == 3 && Probe.Events.IndexOf("refund:1") < Probe.Events.IndexOf("awards"), "normal F exit preserves placement refund before pending awards and repair refund");
            Check(enemy != null && !enemy.gameObject.activeSelf && host.StartedCoroutines == 1, "normal F exit still defers character destruction");
            var deferred = host.Deferred.Single(); Check(deferred.MoveNext() && ((WaitForSeconds)deferred.Current).Seconds == 0.25f, "normal F exit retains 0.25-second wait");
            deferred.MoveNext(); Check(enemy == null && normalF.DeferredExitCount == 0, "normal F deferred cleanup destroys character and releases its owner record");
            sentinel = new CharacterMainControl(); host.Recovery.Add(sentinel); host.Mutators.Add("next mode");
            priorCourier = host.CourierCleanup; int previousAwards = normalF.Awards; normalF.OnDestroy();
            Check(host.Recovery.Contains(sentinel) && host.Mutators.Contains("next mode") && host.CourierCleanup == priorCourier
                && normalF.Refunds == 2 && normalF.Awards == previousAwards, "F already-ended destroy does not repeat cleanup/refund/award");
            Check(!Probe.Events.Any(x => x.Contains("ERROR") || x.Contains("WARNING")), "production lifecycle completed without swallowed cleanup exception");

            host = Host(); var committedRegistry = new ModeEFEnemyRegistry(); var committedE = new ModeERuntimeModule(); committedE.Bind(committedRegistry); committedE.OnAwake(host);
            var committedF = new ModeFRuntimeModule(); committedF.Bind(committedE, committedRegistry); committedF.OnAwake(host);
            committedF.Seed(new CharacterMainControl(), new ModeFFortificationMarker()); committedF.MarkUtilityActionsCommitted(); committedF.OnDestroy();
            Check(committedF.Refunds == 0 && committedF.Awards == 0, "destroy does not refund already-committed utility items or convert rewards into refunds");

            foreach (bool destroyedHost in new[] { false, true })
            {
            host = Host(); var delayedRegistry = new ModeEFEnemyRegistry(); var delayedE = new ModeERuntimeModule(); delayedE.Bind(delayedRegistry); delayedE.OnAwake(host);
            var delayedF = new ModeFRuntimeModule(); delayedF.Bind(delayedE, delayedRegistry); delayedF.OnAwake(host);
            enemy = new CharacterMainControl(); delayedF.Seed(enemy, new ModeFFortificationMarker()); delayedF.ExitModeF(false);
            Check(enemy != null && host.Deferred.Count == 1, "normal exit still owns object during deferred wait");
            if (destroyedHost) UnityEngine.Object.Destroy(host.gameObject); delayedF.OnDestroy();
            Check(enemy == null && host.Deferred.Count == 0 && delayedF.DeferredExitCount == 0 && delayedF.Refunds == 2 && delayedF.Awards == 3,
                "host destroyed during normal exit wait reclaims outstanding Boss without repeating rewards/refunds");
            }

            host = Host(); d = new ModeDRuntimeModule(); d.OnAwake(host); enemy = new CharacterMainControl();
            d.modeDActive = true; d.modeDCurrentWaveEnemies.Add(enemy); d.modeDAutoNextWaveCoroutine = host.OwnCoroutine();
            UnityEngine.Object.Destroy(host.gameObject); d.OnDestroy();
            Check(enemy == null && !d.modeDActive && d.modeDAutoNextWaveCoroutine == null,
                "D fake-null host still runs C# cleanup without calling native coroutine API");

            foreach (bool destroyedHost in new[] { false, true })
            {
                host = Host(); var dormantE = new ModeERuntimeModule(); dormantE.OnAwake(host);
                dormantE.ScheduleModeEStartupWarmup("before entry"); var warmup = dormantE.CurrentWarmup;
                Check(warmup != null && !dormantE.modeEActive, "real warmup scheduler starts before E session");
                sentinel = new CharacterMainControl(); host.Recovery.Add(sentinel); CharacterMainControl.Main.SetTeam(Teams.middle);
                if (destroyedHost) UnityEngine.Object.Destroy(host.gameObject);
                dormantE.OnDestroy();
                Check(warmup.Stopped && dormantE.CurrentWarmup == null && host.Recovery.Contains(sentinel)
                    && CharacterMainControl.Main.Team == Teams.middle && host.CourierCleanup == 0,
                    "dormant E destroys only its pre-entry warmup, including fake-null host");
                int starts = host.StartedCoroutines; dormantE.ScheduleModeEStartupWarmup("late callback");
                Check(host.StartedCoroutines == starts && !Probe.Events.Any(x => x.Contains("ERROR") || x.Contains("WARNING")),
                    "disposed E ignores late warmup request before native host access or swallowed exception");
            }

            host = Host(); var nestedRegistry = new ModeEFEnemyRegistry(); var nestedE = new ModeERuntimeModule(); nestedE.Bind(nestedRegistry); nestedE.OnAwake(host);
            nestedE.ScheduleModeEStartupWarmup("nested merchant prewarm"); var parentWarmup = nestedE.CurrentWarmup;
            var warmupRoutine = host.Deferred.Single(); int warmupSteps = 0;
            while (warmupRoutine.MoveNext() && !(warmupRoutine.Current is Coroutine)) { if (++warmupSteps > 10) throw new Exception("merchant warmup was not reached"); }
            var childWarmup = nestedE.MerchantWarmup;
            Check(childWarmup != null && host.Deferred.Count == 2, "production startup routine registers its independently scheduled merchant child");
            nestedE.OnDestroy();
            Check(parentWarmup.Stopped && childWarmup.Stopped && host.Deferred.Count == 0 && nestedE.MerchantWarmup == null,
                "direct E module destroy cancels both outer startup and merchant child coroutine");
            Console.WriteLine("ModeDestroyLifecycle: PASS " + assertions + " assertions");
        }
    }
}

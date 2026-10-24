using System;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int assertions, scenarios;
    private static readonly ZombieModeCombatPhase[] PreparationPhases = {
        ZombieModeCombatPhase.InitialPreparation, ZombieModeCombatPhase.Preparation,
        ZombieModeCombatPhase.ExtractionOpportunity
    };

    private sealed class World
    {
        internal ModBehaviour Host;
        internal ZombieModeRunState State;
        internal ZombieModeRuntimeModule Runtime;
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    private static World New(ZombieModeCombatPhase phase = ZombieModeCombatPhase.Combat, int wave = 23, bool inside = false)
    {
        scenarios++;
        Health.Reset();
        Debug.Warnings.Clear();
        Duckov.UI.NotificationText.Messages.Clear();
        SceneManager.ActiveBuildIndex = 3;
        Time.unscaledTime = 100f;
        Time.unscaledDeltaTime = 1f;
        CharacterMainControl.Main = new CharacterMainControl("player", true);
        CharacterMainControl.Main.transform.position = new Vector3(inside ? 0f : 30f, 0f, 0f);
        var world = new World {
            Host = new ModBehaviour(),
            State = new ZombieModeRunState {
                RunId = 7, SceneBuildIndex = 3, LifecyclePhase = ZombieModeLifecyclePhase.Active,
                CombatPhase = phase, CurrentWave = wave, CurrentWaveKillTarget = 10,
                CurrentWaveKills = 9, ActiveSafeZoneActive = true, ActiveSafeZoneRadius = 8f,
                PreparationTimer = 2f, BeaconChannelDuration = 1f
            }
        };
        world.Runtime = new ZombieModeRuntimeModule(world.Host, world.State);
        world.Runtime.RegisterZombieModeEventListeners(world.State.RunId);
        return world;
    }

    private static void FullProgressTick()
    {
        foreach (bool inside in new[] { false, true })
        foreach (int wave in new[] { 1, 23, 24, 25, 26 })
        {
            World world = New(wave: wave, inside: inside);
            bool boss = wave % 5 == 0;
            world.State.CurrentWaveKillTarget = boss ? 0 : 10;
            world.State.CurrentWaveKills = boss ? 0 : 10;
            world.State.CurrentWaveBossesRemaining = 0;
            // A stale/failing safe-zone presentation must not precede completed combat.
            world.Host.Faults.Add("safe-zone-tick");
            world.Runtime.TickZombieModeWaveController(0.1f);
            Check(world.State.CombatPhase == ZombieModeCombatPhase.RewardSelection, "full combat goal must settle inside and outside circle, wave " + wave);
            Check(world.Host.Count("reward-selection") == 1, "one reward transition for full goal");
            Check(world.Host.Count("safe-zone-tick") == 0, "goal fallback runs before safe-zone work");
            Check(world.State.PollutionFromNatural == (boss ? 1 : 0), "boss pollution credited once");
            world.Host.Faults.Clear();
            world.Runtime.TickZombieModeWaveController(5f);
            world.Runtime.StartZombieModeWave(world.State.RunId);
            Check(world.Host.Count("reward-selection") == 1 && world.State.CurrentWave == wave, "reward phase cannot resettle or start wave");
        }

        World incomplete = New();
        incomplete.Runtime.TickZombieModeWaveController(1f);
        Check(incomplete.State.CombatPhase == ZombieModeCombatPhase.Combat && incomplete.Host.Count("ambient-pressure") == 1, "incomplete ordinary goal stays combat");
        World bossAlive = New(wave: 25);
        bossAlive.State.CurrentWaveKillTarget = 0;
        bossAlive.State.CurrentWaveBossesRemaining = 1;
        bossAlive.Runtime.TickZombieModeWaveController(1f);
        Check(bossAlive.State.CombatPhase == ZombieModeCombatPhase.Combat, "live boss prevents fallback settlement");
    }

    private static void PreparationAndPhaseGates()
    {
        foreach (ZombieModeCombatPhase phase in PreparationPhases)
        foreach (bool inside in new[] { false, true })
        {
            World world = New(phase, inside: inside);
            world.Runtime.TickZombieModeWaveController(1f);
            Check(world.State.PreparationTimer == 1f, "preparation timer advances regardless of circle position: " + phase);
            world.Runtime.TickZombieModeWaveController(1f);
            Check(world.State.CombatPhase == ZombieModeCombatPhase.Combat && world.State.CurrentWave == 24, "preparation timeout starts next wave");
            Check(world.State.CurrentWaveKills == 0 && world.State.CurrentWaveKillTarget == 10, "start initializes ordinary wave goal");
            world.Runtime.StartZombieModeWave(world.State.RunId);
            Check(world.State.CurrentWave == 24 && world.Host.Count("cleanup-preparation") == 1, "start wave is idempotent after combat transition");
        }

        foreach (ZombieModeCombatPhase phase in new[] { ZombieModeCombatPhase.None, ZombieModeCombatPhase.Combat,
            ZombieModeCombatPhase.Settling, ZombieModeCombatPhase.RewardSelection, ZombieModeCombatPhase.SuccessExit,
            ZombieModeCombatPhase.FailedExit })
        {
            World world = New(phase);
            world.Runtime.StartZombieModeWave(world.State.RunId);
            Check(world.State.CurrentWave == 23 && world.Host.Count("cleanup-preparation") == 0, "direct start rejects phase " + phase);
        }
        foreach (bool extraction in new[] { false, true })
        {
            World world = New(ZombieModeCombatPhase.Preparation);
            world.State.BeaconChanneling = !extraction;
            world.State.ExtractionChanneling = extraction;
            world.Runtime.TickZombieModeWaveController(50f);
            Check(world.State.PreparationTimer == 2f && world.State.CurrentWave == 23, "active channel holds preparation countdown");
        }
        foreach (string fault in new[] { "spawn-elite-squad", "banner" })
        {
            World world = New(ZombieModeCombatPhase.Preparation, wave: 24);
            world.Host.Faults.Add(fault);
            world.Runtime.StartZombieModeWave(world.State.RunId);
            Check(world.State.CurrentWave == 25 && world.State.CurrentWaveBossesRemaining == 1, "optional start failure preserves initialized boss goal");
            Check(world.Host.Count("spawn-boss-wave") == 1, "optional start failure does not skip boss spawning");
        }
    }

    private static void DeathFailuresAndReentry()
    {
        foreach (string fault in new[] { "safe-zone-attack", "death-stars", "option-death", "enemy-drop", "elite-effect", "special-effect", "prune-records" })
        foreach (bool inside in new[] { false, true })
        {
            World world = New(inside: inside);
            var enemy = new CharacterMainControl("zombie");
            ZombieModeEnemyKind kind = fault == "elite-effect" ? ZombieModeEnemyKind.Elite :
                fault == "special-effect" ? ZombieModeEnemyKind.Special : ZombieModeEnemyKind.Normal;
            ZombieModeEnemyRuntimeMarker marker = world.Runtime.Track(enemy, kind);
            world.Host.Faults.Add(fault);
            enemy.Health.DeliverDeath(CharacterMainControl.Main);
            Check(!enemy.gameObject.activeSelf, "death exception must not skip official post-OnDead deactivation: " + fault);
            Check(marker.DeathSettled && world.State.CurrentWaveKills == 10 && world.State.LivingZombieCount == 0 && world.State.LivingNormalZombieCount == 0,
                "death core count survives optional failure: " + fault);
            Check(world.State.CombatPhase == ZombieModeCombatPhase.RewardSelection && world.Host.Count("reward-selection") == 1,
                "death failure does not block settlement: " + fault);
            Check(Debug.Warnings.Count > 0, "isolated failure leaves diagnostic: " + fault);
            enemy.Health.DeliverDeath(CharacterMainControl.Main);
            Check(world.State.CurrentWaveKills == 10 && world.Host.Count("death-stars") == 1 && world.Host.Count("reward-selection") == 1,
                "duplicate death cannot count or reward twice");
        }
        foreach (string fault in new[] { "option-death", "death-stars", "boss-effect", "boss-drop", "prune-records" })
        {
            World world = New(wave: 25);
            world.State.CurrentWaveKillTarget = 0;
            world.State.CurrentWaveBossesRemaining = 1;
            var enemy = new CharacterMainControl("boss");
            world.Runtime.Track(enemy, boss: true);
            world.Host.Faults.Add(fault);
            enemy.Health.DeliverDeath(CharacterMainControl.Main);
            Check(!enemy.gameObject.activeSelf && world.State.CurrentWaveBossesRemaining == 0, "boss death count reaches official disable despite " + fault);
            Check(world.Host.Count("reward-selection") == 1 && world.State.PollutionFromNatural == 1 && world.State.CurrentWaveKills == 9, "boss settlement exactly once despite " + fault);
        }

        World reentrant = New();
        var sameEnemy = new CharacterMainControl("reentrant-zombie");
        ZombieModeEnemyRuntimeMarker sameMarker = reentrant.Runtime.Track(sameEnemy);
        bool reentered = false;
        reentrant.Host.OnStage = delegate(string stage) {
            if (stage != "option-death" || reentered) return;
            reentered = true;
            Check(sameMarker.DeathSettled && reentrant.State.CurrentWaveKills == 10 && reentrant.Host.Count("death-stars") == 1,
                "death count and own stars are committed before optional death callback");
            sameEnemy.Health.DeliverDeath(CharacterMainControl.Main);
        };
        sameEnemy.Health.DeliverDeath(CharacterMainControl.Main);
        Check(reentered && reentrant.State.CurrentWaveKills == 10 && reentrant.Host.Count("reward-selection") == 1 && reentrant.Host.Count("option-death") == 1,
            "reentrant optional callback cannot double settle same health");
    }

    private static void CompletionFailuresAndRetries()
    {
        foreach (string fault in new[] { "cleanup-enemies", "recycle-npcs", "recycle-real-npcs", "clear-healing", "clear-banner" })
        {
            World world = New();
            world.State.CurrentWaveKills = 10;
            world.Host.Faults.Add(fault);
            world.Runtime.TickZombieModeWaveController(1f);
            Check(world.State.CombatPhase == ZombieModeCombatPhase.RewardSelection && world.Host.Count("reward-selection") == 1, "optional completion failure cannot strand Settling: " + fault);
            Check(world.Host.Count("start-coroutine") == 1 && world.Host.Count("clear-healing") == 1, "completion makes one settlement attempt despite " + fault);
        }
        foreach (bool returnNull in new[] { false, true })
        foreach (bool boss in new[] { false, true })
        {
            World world = New(wave: boss ? 25 : 23);
            world.State.CurrentWaveKills = boss ? 0 : 10;
            world.State.CurrentWaveKillTarget = boss ? 0 : 10;
            world.Host.ReturnNullCoroutine = returnNull;
            if (!returnNull) world.Host.Faults.Add("start-coroutine");
            world.Runtime.TickZombieModeWaveController(1f);
            Check(world.State.CombatPhase == ZombieModeCombatPhase.Settling, "launch failure retains retryable phase");
            world.Runtime.TickZombieModeWaveController(1f);
            Check(world.Host.Count("start-coroutine") == 1, "settlement retry is throttled against repeated failed launch");
            world.Host.ReturnNullCoroutine = false;
            world.Host.Faults.Clear();
            Time.unscaledTime += 1f;
            world.Runtime.TickZombieModeWaveController(1f);
            Check(world.Host.Count("start-coroutine") == 2 && world.Host.Count("reward-selection") == 1, "settlement retries after launch failure");
            Check(world.Host.Count("clear-healing") == 1 && world.Host.Count("cleanup-enemies") == 1, "retry does not duplicate completion side effects");
            Check(world.State.PollutionFromNatural == (boss ? 1 : 0), "launch retries do not duplicate boss pollution");
        }

        World delayed = New();
        delayed.State.CurrentWaveKills = 10;
        delayed.Host.PendingStars = true;
        delayed.Runtime.TickZombieModeWaveController(1f);
        delayed.Runtime.TickZombieModeWaveController(1f);
        Check(delayed.Host.Count("start-coroutine") == 1 && delayed.State.CombatPhase == ZombieModeCombatPhase.Settling, "active settlement retains single iterator owner");
        delayed.Host.Faults.Add("collect-stars");
        delayed.Host.Frame(4f);
        Check(delayed.Host.CoroutineErrors.Count == 1 && delayed.State.CombatPhase == ZombieModeCombatPhase.Settling, "host observes iterator fault while phase remains recoverable");
        delayed.Host.Faults.Clear();
        delayed.Host.PendingStars = false;
        delayed.Runtime.TickZombieModeWaveController(1f);
        Check(delayed.Host.Count("reward-selection") == 1 && delayed.Host.Count("start-coroutine") == 2, "iterator finally releases owner for tick retry");
        Check(delayed.Host.Count("clear-healing") == 1, "iterator retry does not repeat healing");

        World settlingPause = New();
        settlingPause.State.CurrentWaveKills = 10;
        settlingPause.Host.PendingStars = true;
        settlingPause.Host.Paused = true;
        settlingPause.Runtime.TickZombieModeWaveController(1f);
        settlingPause.Host.Frame(20f);
        Check(settlingPause.State.CombatPhase == ZombieModeCombatPhase.Settling && settlingPause.Host.Count("collect-stars") == 0,
            "paused settlement does not consume star collection deadline");
        settlingPause.Host.Paused = false;
        settlingPause.Host.Frame(ZombieModeTuning.SettlementMaxWaitSeconds + 0.1f);
        Check(settlingPause.State.CombatPhase == ZombieModeCombatPhase.RewardSelection && settlingPause.Host.Count("reward-selection") == 1,
            "active settlement timeout force collects and reaches reward once");

        World rewardFault = New();
        rewardFault.State.CurrentWaveKills = 10;
        rewardFault.Host.Faults.Add("reward-selection");
        rewardFault.Runtime.TickZombieModeWaveController(1f);
        Check(rewardFault.State.CombatPhase == ZombieModeCombatPhase.Settling, "synchronous iterator reward failure is caught at launch");
        rewardFault.Host.Faults.Clear();
        Time.unscaledTime += 1f;
        rewardFault.Runtime.TickZombieModeWaveController(1f);
        Check(rewardFault.State.CombatPhase == ZombieModeCombatPhase.RewardSelection && rewardFault.Host.Count("clear-healing") == 1, "reward UI startup may retry without reapplying completion grants");
    }

    private static void BeaconGatesAndOwnership()
    {
        foreach (bool inside in new[] { false, true })
        {
            World combat = New(inside: inside);
            combat.State.ActiveSafeZoneActive = false;
            combat.State.PortableSafeZoneActive = true;
            Check(!combat.Runtime.CanUseZombieModeBeacon() && !combat.Runtime.TryUseZombieModeBeacon(), "combat portable safe-zone does not unlock beacon");
            Check(combat.Host.Count("start-coroutine") == 0 && !combat.State.BeaconChanneling, "rejected beacon starts no channel");
        }
        foreach (ZombieModeCombatPhase phase in PreparationPhases)
        foreach (bool inside in new[] { false, true })
        {
            World world = New(phase, inside: inside);
            Check(world.Runtime.CanUseZombieModeBeacon() && world.Runtime.TryUseZombieModeBeacon(), "preparation beacon works inside and outside safe-zone: " + phase);
            Coroutine channel = world.Host.Coroutines[0];
            Check(!world.Runtime.TryUseZombieModeBeacon(), "duplicate use cannot start a second channel");
            world.Runtime.TickZombieModeWaveController(50f);
            Check(world.State.PreparationTimer == 2f && world.State.CurrentWave == 23, "last-frame channel prevents timer auto-start");
            world.Host.Step(channel);
            Check(world.State.CurrentWave == 24 && world.State.CombatPhase == ZombieModeCombatPhase.Combat && !world.State.BeaconChanneling,
                "one completed beacon starts exactly one wave");
            world.Host.Step(channel);
            Check(world.State.CurrentWave == 24 && world.Host.Count("cleanup-preparation") == 1, "completed coroutine cannot restart wave");
        }

        foreach (string stale in new[] { "phase", "extraction", "flag", "wave", "run", "scene", "cleanup" })
        {
            World world = New(ZombieModeCombatPhase.Preparation);
            Check(world.Runtime.TryUseZombieModeBeacon(), "stale case starts valid beacon");
            int expectedWave = 23;
            switch (stale)
            {
                case "phase": world.State.CombatPhase = ZombieModeCombatPhase.Combat; break;
                case "extraction": world.State.ExtractionChanneling = true; break;
                case "flag": world.State.BeaconChanneling = false; break;
                case "wave": expectedWave = ++world.State.CurrentWave; break;
                case "run": world.State.RunId++; break;
                case "scene": SceneManager.ActiveBuildIndex++; break;
                case "cleanup": world.State.IsCleaningUp = true; break;
            }
            world.Host.Step(world.Host.Coroutines[0]);
            Check(world.State.CurrentWave == expectedWave && world.Host.Count("cleanup-preparation") == 0, "last yield revalidates beacon " + stale);
            if (stale == "phase" || stale == "extraction") Check(!world.State.BeaconChanneling, "cancelled current operation releases owned flag");
        }

        World generations = New(ZombieModeCombatPhase.Preparation);
        Check(generations.Runtime.TryUseZombieModeBeacon(), "old operation starts");
        Coroutine old = generations.Host.Coroutines[0];
        // Simulate external cancellation followed by a fresh same-run/same-wave use.
        // This needs an operation owner, since run/wave alone are identical.
        generations.State.BeaconChanneling = false;
        generations.State.BeaconChannelDuration = 10f;
        Time.unscaledTime += 2f;
        Check(generations.Runtime.TryUseZombieModeBeacon(), "new operation starts in same run and wave");
        float newStart = generations.State.BeaconChannelStartTime;
        generations.Host.Step(old);
        Check(generations.State.BeaconChanneling && generations.State.BeaconChannelStartTime == newStart && generations.State.CurrentWave == 23,
            "old generation cannot clear or complete newer same-wave beacon");
        Coroutine current = generations.Host.Coroutines[1];
        ((IDisposable)current.Iterator).Dispose();
        current.Destroyed = true;
        Check(!generations.State.BeaconChanneling && generations.State.BeaconChannelStartTime == 0f, "disposing current operation releases channel flag");

        World timed = New(ZombieModeCombatPhase.Preparation);
        timed.State.BeaconChannelDuration = 3f;
        Check(timed.Runtime.TryUseZombieModeBeacon(), "three second beacon starts");
        timed.Host.Frame(1f);
        Check(timed.State.CurrentWave == 23, "two active seconds cannot complete three second beacon");
        timed.Host.Frame(1f);
        Check(timed.State.CurrentWave == 23, "completion waits for final yield boundary");
        timed.Host.Frame(0f);
        Check(timed.State.CurrentWave == 24, "three active seconds complete beacon after final yield");

        World paused = New(ZombieModeCombatPhase.Preparation);
        paused.State.BeaconChannelDuration = 3f;
        paused.Host.Paused = true;
        Check(paused.Runtime.TryUseZombieModeBeacon(), "paused beacon is created");
        paused.Host.Frame(20f);
        Check(paused.State.CurrentWave == 23 && paused.State.BeaconChannelStartTime == 121f, "paused beacon waits and advances display origin");
        paused.Host.Paused = false;
        paused.Host.Frame(1f);
        paused.Host.Frame(1f);
        Check(paused.State.CurrentWave == 23, "paused time gives no credit to three second channel");
        paused.Host.Frame(1f);
        paused.Host.Frame(0f);
        Check(paused.State.CurrentWave == 24, "unpaused beacon completes after active time");

        World rejectedLaunch = New(ZombieModeCombatPhase.Preparation);
        rejectedLaunch.Host.ReturnNullCoroutine = true;
        Check(!rejectedLaunch.Runtime.TryUseZombieModeBeacon() && !rejectedLaunch.State.BeaconChanneling && rejectedLaunch.State.BeaconChannelStartTime == 0f,
            "null coroutine launch rolls channel state back and reports use failure");
    }

    private static void DestroyAndCleanupBoundary()
    {
        World world = New();
        var enemy = new CharacterMainControl("destroyed-on-map-exit");
        ZombieModeEnemyRuntimeMarker marker = world.Runtime.Track(enemy);
        UnityEngine.Object.Destroy(enemy.gameObject);
        Check(enemy == null && enemy.Health == null && marker == null, "destroying root fake-nulls all components");
        enemy.Health.DeliverDeath(CharacterMainControl.Main);
        Check(world.State.CurrentWaveKills == 9 && !marker.DeathSettled, "destroyed enemy death cannot settle after map exit");
        foreach (Action cleanup in world.Host.Cleanup) cleanup();
        world.State.LifecyclePhase = ZombieModeLifecyclePhase.None;
        var afterCleanup = new CharacterMainControl("unsubscribed");
        world.Runtime.Track(afterCleanup);
        afterCleanup.Health.DeliverDeath(CharacterMainControl.Main);
        world.Runtime.TickZombieModeWaveController(50f);
        Check(world.State.CurrentWaveKills == 9 && world.Host.Count("reward-selection") == 0, "cleanup unsubscribes global death owner and inactive tick does no work");
    }

    private static void Main()
    {
        FullProgressTick();
        PreparationAndPhaseGates();
        DeathFailuresAndReentry();
        CompletionFailuresAndRetries();
        BeaconGatesAndOwnership();
        DestroyAndCleanupBoundary();
        Console.WriteLine("ZombieModeWaveRuntime PASS: " + scenarios + " scenarios, " + assertions + " assertions (production-linked wave/death/beacon)");
    }
}

using System;
using System.Collections;
using System.Linq;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class TargetSceneEntryRegression
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL " + message); }
    private sealed class Entry
    {
        internal Scene Target;
        internal ModBehaviour Host;
        internal ZombieModeRuntimeModule Module;
    }
    private static void Ready(bool loader, bool level, bool after)
    {
        SceneLoader.IsSceneLoading = !loader; LevelManager.LevelInited = level; LevelManager.AfterInit = after;
    }
    private static void QueueEntry(Entry entry, long cash)
    {
        Check(entry.Module.TryBeginMapSelectionShell(), "production entry shell opens");
        // 预检/支付发生在本夹具边界外：真实等待从已扣款的 LoadingMap 阶段开始。
        entry.Module.State.LifecyclePhase = ZombieModeLifecyclePhase.LoadingMap;
        entry.Module.State.PendingCashInvestment = cash;
        entry.Module.State.ConfirmedCashInvested = cash;
        entry.Module.Transaction.InvitationTemporarilyHeld = true;
        entry.Module.Transaction.CashTemporarilyHeld = true;
        entry.Module.Transaction.CashWithheldAmount = cash;
        ZombieModeMapSelectionHelper.HasPendingZombieEntry = true;
        ZombieModeMapSelectionHelper.TargetSceneName = entry.Target.name;
    }
    private static Entry Create()
    {
        SceneManager.LoadedHandles.Clear();
        SceneLoader.Instance = new SceneLoader(); SceneLoader.BaseLoads = 0;
        LevelManager.Instance = new LevelManager(); Ready(false, false, false);
        ZombieModeMapSelectionHelper.Clears = 0;
        ZombieModeEntryDebt.Invitations = ZombieModeEntryDebt.CashCalls = 0; ZombieModeEntryDebt.Cash = 0;
        var entry = new Entry
        {
            Target = new Scene { name = "Level_DemoChallenge_1", buildIndex = 36, handle = 17 },
            Host = new ModBehaviour(), Module = new ZombieModeRuntimeModule()
        };
        SceneManager.LoadedHandles.Add(17);
        SceneManager.Current = new Scene { name = "Level_DemoChallenge_Main", buildIndex = 37, handle = 18 };
        SceneManager.LoadedHandles.Add(18);
        entry.Module.BindForTest(entry.Host);
        QueueEntry(entry, 1200);
        return entry;
    }
    private static int Drain(IEnumerator routine, Action<int, float> afterWait = null)
    {
        int waits = 0; float elapsed = 0;
        while (routine.MoveNext())
        {
            var wait = routine.Current as WaitForSeconds;
            Check(wait != null && wait.Seconds > 0, "real wait yields its Unity wait instruction");
            waits++; elapsed += wait.Seconds;
            Check(waits <= 400, "production wait remains bounded");
            if (afterWait != null) afterWait(waits, elapsed);
        }
        return waits;
    }
    private static void CheckNoNewRun(Entry entry, string label)
    {
        Check(entry.Module.State.Resets == 0 && entry.Module.InitializeCalls.Count == 0,
            label + ": no Begin/initialize effects");
    }
    private static void CheckFailedOwnEntry(Entry entry, string label)
    {
        CheckNoNewRun(entry, label);
        Check(entry.Host.CleanupCalls == 1 && ZombieModeMapSelectionHelper.Clears == 1,
            label + ": original failure cleanup clears this pending entry once");
        Check(ZombieModeEntryDebt.Invitations == 1 && ZombieModeEntryDebt.CashCalls == 1 && ZombieModeEntryDebt.Cash == 1200,
            label + ": real refund gates refund held invitation/cash once");
        Check(entry.Host.CoroutineStarts == 0 && SceneLoader.BaseLoads == 0,
            label + ": LoadingMap failure keeps original no-return-to-base protocol");
    }
    private static void CheckStarted(Entry entry, string label)
    {
        Check(entry.Module.State.Resets == 1 && entry.Module.State.LifecyclePhase == ZombieModeLifecyclePhase.WaitingStarterChoice,
            label + ": real Begin and Initialize reach starter choice once");
        Check(entry.Module.InitializeCalls.SequenceEqual(new[] { "cache", "inventory", "spawn", "isolation", "preset", "containers", "beacon", "events", "hud", "choice" }),
            label + ": real initialization invokes dependency boundaries in order");
        Check(entry.Module.State.PurificationPoints == 12 && entry.Module.IsZombieModeRunValid(entry.Module.State.RunId),
            label + ": cash conversion and real run validity survive initialization");
        Check(entry.Host.CleanupCalls == 0 && ZombieModeEntryDebt.CashCalls == 0 && ZombieModeEntryDebt.Invitations == 0 && ZombieModeMapSelectionHelper.Clears == 1,
            label + ": success clears pending without refunds");
    }
    private static void TimeoutGates()
    {
        foreach (var flags in new[] { new[] { false, false, false }, new[] { false, true, true }, new[] { true, false, true }, new[] { true, true, false } })
        {
            var entry = Create(); SceneManager.Current = entry.Target; Ready(flags[0], flags[1], flags[2]);
            int waits = Drain(entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null));
            Check(waits >= 300, "unready case reaches the 30 second timeout");
            CheckFailedOwnEntry(entry, "unready " + string.Join(",", flags));
        }
    }
    private static void NormalAndDeadlineReady()
    {
        var entry = Create(); Ready(true, true, true);
        var routine = entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null);
        Check(routine.MoveNext() && routine.Current is WaitForSeconds, "sceneLoaded while Main is active waits normally");
        CheckNoNewRun(entry, "Main-to-target transition");
        SceneManager.Current = entry.Target;
        Check(Drain(routine) == 0, "target handle becoming active completes without another wait");
        CheckStarted(entry, "normal ready");

        entry = Create();
        routine = entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null);
        int waits = Drain(routine, (count, elapsed) =>
        {
            if (elapsed >= 30f) { SceneManager.Current = entry.Target; Ready(true, true, true); }
        });
        Check(waits >= 300 && waits <= 301, "deadline case becomes ready on the final timeout yield");
        CheckStarted(entry, "ready at deadline");
    }
    private static void SceneBoundaries()
    {
        var entry = Create(); Ready(true, true, true);
        SceneManager.Current = new Scene { name = entry.Target.name, buildIndex = entry.Target.buildIndex, handle = 99 };
        SceneManager.LoadedHandles.Add(99);
        Drain(entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null));
        CheckFailedOwnEntry(entry, "same-name replacement scene handle");

        entry = Create(); var routine = entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null);
        Check(routine.MoveNext(), "unload case starts waiting");
        SceneManager.LoadedHandles.Remove(entry.Target.handle);
        SceneManager.Current = new Scene { name = "Base", buildIndex = 1, handle = 2 }; Ready(true, true, true);
        Drain(routine);
        CheckFailedOwnEntry(entry, "target unloaded and scene changed");
    }
    private static void OldWaitCannotTouchNewEntry()
    {
        var entry = Create(); var old = entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null);
        Check(old.MoveNext(), "old attempt yields");
        int oldAttempt = entry.Module.Attempt, oldRun = entry.Module.State.RunId;
        entry.Host.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.SceneSwitched); QueueEntry(entry, 9900);
        Check(entry.Module.Attempt != oldAttempt && entry.Module.State.RunId == oldRun,
            "new attempt has independent identity even before a new RunId exists");
        SceneManager.Current = entry.Target; Ready(true, true, true);
        Check(!old.MoveNext(), "old waiter ends after cancellation and same-map reentry");
        CheckNoNewRun(entry, "stale waiter");
        Check(entry.Module.Pending && ZombieModeMapSelectionHelper.HasPendingZombieEntry && ZombieModeMapSelectionHelper.Clears == 0 &&
            entry.Module.Transaction.CashTemporarilyHeld && entry.Module.Transaction.CashWithheldAmount == 9900 && entry.Host.CleanupCalls == 1 &&
            ZombieModeEntryDebt.Invitations == 0 && ZombieModeEntryDebt.CashCalls == 0,
            "stale waiter preserves new entry pending markers, held resources and cleanup");
        Drain(entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null));
        Check(entry.Module.State.Resets == 1 && entry.Module.State.PurificationPoints == 99 && entry.Host.CleanupCalls == 1,
            "new entry still initializes independently");

        entry = Create(); old = entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null);
        Check(old.MoveNext(), "reentrant-probe case yields");
        SceneManager.Current = entry.Target; Ready(true, true, true);
        entry.Host.OnReadLoader = () =>
        {
            entry.Host.OnReadLoader = null;
            entry.Host.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.SceneSwitched); QueueEntry(entry, 7700);
        };
        Check(!old.MoveNext(), "identity is rechecked after a ready loop break");
        CheckNoNewRun(entry, "reentrant ready probe");
        Check(entry.Module.Transaction.CashWithheldAmount == 7700 && ZombieModeMapSelectionHelper.Clears == 0 && entry.Host.CleanupCalls == 1,
            "probe reentry cannot refund or clear the replacement entry");
    }
    private static void FinalReadinessIsFresh()
    {
        var entry = Create(); SceneManager.Current = entry.Target; Ready(true, true, true);
        int reads = 0;
        entry.Host.OnReadLoader = () => { if (++reads == 2) Ready(false, true, true); };
        Drain(entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null));
        Check(reads == 2, "loop readiness is followed by an independent final readiness probe");
        CheckFailedOwnEntry(entry, "loader becomes unready after loop success");
    }
    private static void StaleTimeoutCannotRefundNewEntry()
    {
        var entry = Create(); SceneManager.Current = entry.Target;
        int waits = Drain(entry.Module.WaitForZombieModeTargetSceneActiveThenInitialize(entry.Target, null), (count, elapsed) =>
        {
            if (elapsed >= 30f)
            {
                entry.Host.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.SceneSwitched);
                QueueEntry(entry, 8800);
            }
        });
        Check(waits >= 300, "replacement occurs on the old waiter's final timeout yield");
        CheckNoNewRun(entry, "stale timeout");
        Check(entry.Host.CleanupCalls == 1 && ZombieModeMapSelectionHelper.Clears == 0 &&
            ZombieModeMapSelectionHelper.HasPendingZombieEntry && entry.Module.Pending &&
            entry.Module.Transaction.InvitationTemporarilyHeld && entry.Module.Transaction.CashTemporarilyHeld &&
            entry.Module.Transaction.CashWithheldAmount == 8800 && ZombieModeEntryDebt.CashCalls == 0 && ZombieModeEntryDebt.Invitations == 0,
            "old timeout cannot refund or clear the new entry");
    }
    public static void Main()
    {
        TimeoutGates(); NormalAndDeadlineReady(); SceneBoundaries(); OldWaitCannotTouchNewEntry(); FinalReadinessIsFresh(); StaleTimeoutCannotRefundNewEntry();
        Console.WriteLine("ZombieModeHostOwners target scene readiness: " + checks + " PASS / 0 FAIL");
        Console.WriteLine("L2: real wait, entry identity, Begin, run validity, Initialize and refund/failure gates; official scene/loader and gameplay effects remain recording boundaries.");
    }
}

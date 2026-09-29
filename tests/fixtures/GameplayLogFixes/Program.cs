using System;
using System.Collections;
using System.Collections.Generic;
using BossRush;

internal static class Program
{
    private static int passed, failed;
    internal static void Check(string label, Action assertion)
    {
        try { assertion(); passed++; Console.WriteLine("PASS " + label); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + label + ": " + e.Message); }
    }
    internal static void Require(bool value, string reason)
    { if (!value) throw new Exception(reason); }
    private static void Reset(bool ready, ModeHStakeJournalDto journal = null, bool unreadable = false)
    {
        ModeHInventoryPersistenceBridge.Ready = ready;
        ModeHInventoryPersistenceBridge.Occurrences = 1;
        ModeHStakeJournalPersistence.Stored = journal;
        ModeHStakeJournalPersistence.IsWriteBarrier = unreadable;
        ModeHStakeJournalPersistence.Loads = 0;
        ModeHWarehouseStakeJournal.Reset();
        ModeHRuntimeGates.ResetForSlotChange();
    }
    private static int Main()
    {
        Check("late module attachment hydrates the selected slot before season restore", () => {
            Reset(true);
            var runtime = new ModeHRuntimeModule(); runtime.OnAwake(new ModBehaviour());
            Require(runtime.ConsistentAtRestore && ModeHWarehouseStakeJournal.IsSlotConsistent,
                "selected slot stayed uninitialized after the OnSetFile event was already missed");
        });
        Check("attachment before storage initialization defers and later becomes ready", () => {
            Reset(false); new ModeHRuntimeModule().OnAwake(new ModBehaviour());
            Require(ModeHWarehouseStakeJournal.Deferred && !ModeHRuntimeGates.Blocked,
                "storage readiness must defer without marking an asset debt");
            ModeHInventoryPersistenceBridge.Ready = true;
            Require(ModeHWarehouseStakeJournal.TryRecomputeDeferredSlotConsistency()
                && ModeHWarehouseStakeJournal.IsSlotConsistent, "level-ready retry did not unlock the clean slot");
        });
        Check("late attachment retains an active escrow journal", () => {
            var journal = new ModeHStakeJournalDto { phase = (int)ModeHStakePhase.MatchLocked };
            Reset(true, journal); new ModeHRuntimeModule().OnAwake(new ModBehaviour());
            Require(ReferenceEquals(ModeHWarehouseStakeJournal.Active, journal)
                && !ModeHWarehouseStakeJournal.IsSlotConsistent && ModeHRuntimeGates.Blocked,
                "an outstanding asset journal was skipped");
        });
        Check("unreadable journal remains blocked even when storage is ready", () => {
            Reset(true, null, true); new ModeHRuntimeModule().OnAwake(new ModBehaviour());
            Require(!ModeHWarehouseStakeJournal.IsSlotConsistent && ModeHRuntimeGates.Blocked
                && ModeHWarehouseStakeJournal.Reason == "slot_journal_unreadable", "unreadable journal was treated as absent");
        });
        Check("terminal journal permits a new clean match", () => {
            Reset(true, new ModeHStakeJournalDto { phase = (int)ModeHStakePhase.RefundedTerminal });
            new ModeHRuntimeModule().OnAwake(new ModBehaviour());
            Require(ModeHWarehouseStakeJournal.IsSlotConsistent && !ModeHRuntimeGates.Blocked,
                "completed refund still blocked the next match");
        });
        Check("unremoved stake waits for storage and cancels after it becomes ready", () => {
            foreach (ModeHStakePhase phase in new[] { ModeHStakePhase.Prepared, ModeHStakePhase.EscrowSnapshotDurable })
            {
                var journal = new ModeHStakeJournalDto { phase = (int)phase };
                journal.escrowItems.Add(new ModeHItemTreeSnapshotDto { semanticTreeDigest = "original", preCount = 1 });
                Reset(false, journal); new ModeHRuntimeModule().OnAwake(new ModBehaviour());
                string reason;
                Require(!ModeHWarehouseStakeJournal.TryCancelWithoutRemoval(out reason)
                    && journal.phase == (int)phase && ModeHWarehouseStakeJournal.ManualInterventions == 0,
                    "unavailable storage permanently changed the recoverable journal phase");
                ModeHInventoryPersistenceBridge.Ready = true;
                Require(ModeHWarehouseStakeJournal.TryCancelWithoutRemoval(out reason)
                    && journal.phase == (int)ModeHStakePhase.CancelledTerminal,
                    "unchanged original stake could not be cancelled after storage initialization");
            }
        });
        Check("actual missing stake still enters manual intervention", () => {
            var journal = new ModeHStakeJournalDto { phase = (int)ModeHStakePhase.EscrowSnapshotDurable };
            journal.escrowItems.Add(new ModeHItemTreeSnapshotDto { semanticTreeDigest = "original", preCount = 1 });
            Reset(true, journal); new ModeHRuntimeModule().OnAwake(new ModBehaviour());
            ModeHInventoryPersistenceBridge.Occurrences = 0;
            string reason;
            Require(!ModeHWarehouseStakeJournal.TryCancelWithoutRemoval(out reason)
                && journal.phase == (int)ModeHStakePhase.ManualIntervention
                && ModeHWarehouseStakeJournal.ManualInterventions == 1,
                "missing original item was incorrectly accepted as a clean cancellation");
        });
        Check("inactive dragon ignores a queued hurt callback", () => {
            var dragon = new DragonKingAbilityController { isActiveAndEnabled = false };
            dragon.Hurt(); Require(dragon.Starts == 0 && !dragon.Triggered, "inactive dragon started child protection");
        });
        Check("dead dragon cannot restart child protection", () => {
            var dragon = new DragonKingAbilityController(); dragon.bossHealth.IsDead = true;
            dragon.Hurt(); Require(dragon.Starts == 0 && !dragon.Triggered, "dead dragon restarted a skill");
        });
        Check("death phase wins over queued protection healing", () => {
            var dragon = new DragonKingAbilityController { CurrentPhase = DragonKingPhase.Dead, isInChildProtection = true };
            dragon.Hurt(); Require(dragon.bossHealth.Heals == 0, "dead phase healed from a queued hurt callback");
        });
        Check("live dragon still starts child protection exactly once", () => {
            var dragon = new DragonKingAbilityController(); dragon.Hurt(); dragon.Hurt();
            Require(dragon.Starts == 1 && dragon.Triggered, "normal child protection changed");
        });
        Check("live transition still restores protected damage", () => {
            var dragon = new DragonKingAbilityController { CurrentPhase = DragonKingPhase.Transitioning };
            dragon.Hurt(); Require(dragon.Starts == 0 && dragon.bossHealth.Heals == 1, "transition protection changed");
        });
        GameCallbackChecks.Run();
        Console.WriteLine("GameplayLogFixes: " + passed + " PASS / " + failed + " FAIL");
        return failed == 0 ? 0 : 1;
    }
}

namespace BossRush
{
    internal partial class ModBehaviour { }
    internal class BossRushRuntimeModuleBase { public virtual void OnAwake(ModBehaviour owner) { } }
    internal partial class ModeHRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private bool _shutdownCompleted;
        internal bool ConsistentAtRestore;
        private void EnsureLevelReadySubscription() { }
        private void RestoreFromSaveIfPresent() { ConsistentAtRestore = ModeHWarehouseStakeJournal.IsSlotConsistent; }
        private void ReconcileCashBetOnRestore() { }
        private void LogFailure(string step, Exception error) { throw new Exception(step, error); }
    }
    internal static class ModeHSaveFlushCoordinator { internal static void EnsureSubscribed() { } }
    // 本场总结（2026-09-29）：读档对账不进总结，启动时清一次记账
    internal static class ModeHSessionSummary { internal static void Discard() { } }
    internal static class ModeHRuntimeGates
    {
        internal static int SlotGeneration;
        internal static bool Blocked;
        internal static void ResetForSlotChange() { Blocked = false; }
        internal static void InitializeRiskForSlot(int generation) { }
        internal static void SetExternalAssetRiskBlocked(bool blocked, string reason) { Blocked = blocked; }
    }
    internal enum ModeHStakePhase { Unknown, None, MatchLocked, Terminal, CancelledTerminal, RefundedTerminal, ManualIntervention, Prepared, EscrowSnapshotDurable }
    internal enum ModeHSettlementKind { None }
    internal sealed class ModeHItemTreeSnapshotDto { internal string semanticTreeDigest; internal int preCount; }
    internal sealed class ModeHStakeJournalDto { internal int phase; internal List<ModeHItemTreeSnapshotDto> escrowItems = new List<ModeHItemTreeSnapshotDto>(); }
    internal static class ModeHStakeJournalPersistence
    {
        internal static bool IsWriteBarrier;
        internal static ModeHStakeJournalDto Stored;
        internal static int Loads;
        internal static ModeHStakeJournalDto LoadCurrent() { Loads++; return IsWriteBarrier ? null : Stored; }
    }
    internal static class ModeHInventoryPersistenceBridge
    {
        internal static bool Ready;
        internal static int Occurrences;
        internal static bool IsStorageReady(out string reason) { reason = Ready ? null : "storage_not_initialized"; return Ready; }
        internal static int CountOccurrences(ModeHItemTreeSnapshotDto snapshot) { return Occurrences; }
    }
    internal static partial class ModeHWarehouseStakeJournal
    {
        private static ModeHStakeJournalDto _active;
        private static bool _slotConsistent, _slotConsistencyDeferred;
        private static string _slotInconsistentReasonId;
        private static readonly List<object> _escrowItems = new List<object>();
        internal static int ManualInterventions;
        private static void EnterManualIntervention(string reason) { ManualInterventions++; _active.phase = (int)ModeHStakePhase.ManualIntervention; }
        private static bool TryAdvancePhase(ModeHStakePhase next, ModeHSettlementKind kind, out string reason)
        { reason = null; _active.phase = (int)next; return true; }
        internal static bool IsSlotConsistent { get { return _slotConsistent; } }
        internal static ModeHStakeJournalDto Active { get { return _active; } }
        internal static bool Deferred { get { return _slotConsistencyDeferred; } }
        internal static string Reason { get { return _slotInconsistentReasonId; } }
        private static void DrainEscrowToStorageBuffer(string reason, bool sameSlot) { }
        internal static void Reset() { _active = null; _slotConsistent = false; _slotConsistencyDeferred = false; _slotInconsistentReasonId = null; ManualInterventions = 0; }
    }
    internal enum DragonKingPhase { Fighting, Transitioning, Dead }
    internal static class DragonKingConfig { internal const float ChildProtectionHealthThreshold = 1f; }
    internal struct DamageInfo { internal float finalDamage; }
    internal sealed class FakeHealth
    {
        internal float CurrentHealth = 1f;
        internal bool IsDead;
        internal int Heals;
        internal void SetHealth(float value) { CurrentHealth = value; Heals++; }
    }
    internal partial class DragonKingAbilityController
    {
        internal bool isActiveAndEnabled = true, isInChildProtection;
        internal FakeHealth bossHealth = new FakeHealth();
        internal object bossCharacter = new object();
        internal DragonKingPhase CurrentPhase;
        private bool childProtectionTriggered;
        private object childProtectionCoroutine;
        internal int Starts;
        internal bool Triggered { get { return childProtectionTriggered; } }
        internal void Hurt() { OnBossHurt(new DamageInfo { finalDamage = 5f }); }
        private void CheckPhaseTransition() { }
        private IEnumerator ChildProtectionSequence() { yield break; }
        private object StartCoroutine(IEnumerator routine)
        { if (!isActiveAndEnabled) throw new InvalidOperationException("inactive coroutine owner"); Starts++; return routine; }
    }
}

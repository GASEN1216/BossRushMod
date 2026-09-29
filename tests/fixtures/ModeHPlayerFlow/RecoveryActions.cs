// Production action composition, journal return dispatch and cancel binding;
// storage mutation, Unity widgets and key events remain host boundaries.
using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class ModeHActionData
    {
        internal string Label;
        internal Action OnClick;
        internal bool IsPrimary, BypassReadOnly, IsDanger, IsCancel;
        internal bool Interactable = true;
    }
    internal static class ModeHRuntimeGates
    {
        internal static bool IsModeHRiskScanFaulted;
        internal static int ScanCalls;
        internal static void TryRetryRiskScan(float unused) { ScanCalls++; }
    }
    internal static class ModeHProfilePersistence
    {
        internal static ModeHSeasonDto Current;
        internal static ModeHSeasonDto LoadCurrent() { return Current; }
    }
    internal static class ModeHWarehouseStakeJournal
    {
        internal static ModeHStakeJournalDto Active;
        internal static int EscrowCount;
        internal static bool IsSlotConsistent;
        internal static ModeHStakeJournalDto Export() { return Active; }
        internal static bool IsTerminalPhase(ModeHStakePhase phase)
        { return phase == ModeHStakePhase.Terminal || phase == ModeHStakePhase.CancelledTerminal
            || phase == ModeHStakePhase.RefundedTerminal; }
    }
    internal static class ModeHRealStakeService
    {
        internal static bool Reject;
        internal static int Calls, LastMatch;
        internal static long LastSeed;
        internal static bool TryAbortReturn(long seed, int match, out string reason)
        {
            Calls++; LastSeed = seed; LastMatch = match;
            reason = Reject ? "fixture return failed" : null;
            if (Reject) return false;
            if (ModeHWarehouseStakeJournal.Active != null)
                ModeHWarehouseStakeJournal.Active.phase = (int)ModeHStakePhase.RefundedTerminal;
            ModeHWarehouseStakeJournal.EscrowCount = 0;
            ModeHWarehouseStakeJournal.IsSlotConsistent = true;
            return true;
        }
    }
    public partial class ModBehaviour { public static void CriticalLog(string text) { } }
    internal static class BossRushConfirmDialog { internal static bool IsOpen; }
    internal sealed class PetNestCancelKey
    {
        internal Action Callback;
        internal Func<bool> Covered;
        internal static PetNestCancelKey Attach(UnityEngine.GameObject root, Action action, Func<bool> covered)
        { return new PetNestCancelKey { Callback = action, Covered = covered }; }
        internal void Detach() { Callback = null; }
        internal void Press() { if (!Covered() && Callback != null) Callback(); }
    }
    internal partial class ModeHRecoveryPanel
    {
        private UnityEngine.GameObject _root;
        private PetNestCancelKey _cancelKey;
        internal IList<ModeHActionData> Actions;
        internal bool AllowActions;
        internal bool Visible { get { return _root != null; } }
        internal static List<string> BuildLines(ModeHSeasonDto season, ModeHStakeJournalDto journal, string reason)
        { return new List<string>(); }
        internal void Show(string title, IList<string> lines, IList<ModeHActionData> actions, bool allow, Action stop)
        {
            stop();
            _root = new UnityEngine.GameObject();
            AllowActions = allow;
            Actions = EnsureCloseAction(actions);
            SyncCancelKey(Actions, allow);
        }
        internal void Hide() { DetachCancelKey(); _root = null; }
        internal void PressCancel() { if (_cancelKey != null) _cancelKey.Press(); }
        internal bool Click(string suffix)
        {
            foreach (ModeHActionData action in Actions)
            {
                if (action.Label != ModeHConfig.LocalizationKeyPrefix + suffix) continue;
                if ((!AllowActions && !action.BypassReadOnly) || !action.Interactable || action.OnClick == null)
                    return false;
                action.OnClick();
                return true;
            }
            return false;
        }
    }
    internal partial class RecoveryActions
    {
        private ModeHSeasonDto _season;
        private RoutingState _runState;
        private ModeHRecoveryPanel _recoveryPanel;
        private bool _restoredSeasonPending, _resumeScenePending, _seasonDirty;
        private string _recoveryResultText, _lastExitReasonId;
        private int _resumes, _confirmations;
        private void HideRecoveryShell() { _recoveryPanel.Hide(); }
        private void ResumeFromSuspended() { _resumes++; }
        private void ConfirmAbandonSeasonFromRecovery() { _confirmations++; }
        private bool TryPersistSeason(string reason) { return true; }
        private void StopOwnRewardReveal() { }
        private void LogFailure(string reason, Exception error) { throw new Exception(reason, error); }

        private static void Reset()
        {
            ModeHProfilePersistence.Current = null;
            ModeHRuntimeGates.IsModeHRiskScanFaulted = false;
            ModeHRuntimeGates.ScanCalls = 0;
            ModeHWarehouseStakeJournal.Active = null;
            ModeHWarehouseStakeJournal.EscrowCount = 0;
            ModeHWarehouseStakeJournal.IsSlotConsistent = true;
            ModeHRealStakeService.Calls = 0;
            ModeHRealStakeService.Reject = false;
            BossRushConfirmDialog.IsOpen = false;
        }
        internal static void Run()
        {
            foreach (bool haveSeason in new[] { false, true })
            foreach (bool haveOwner in new[] { false, true })
            {
                Reset();
                var runtime = new RecoveryActions
                {
                    _season = haveSeason ? new ModeHSeasonDto
                    { runState = new ModeHRunStateDto { lifecycle = (int)ModeHLifecycle.Suspended, runSeed = 99 } } : null,
                    _runState = haveOwner ? new RoutingState { Lifecycle = ModeHLifecycle.Suspended } : null,
                };
                ModeHWarehouseStakeJournal.Active = new ModeHStakeJournalDto
                { phase = (int)ModeHStakePhase.EscrowRemovedDurable, matchIndex = 3 };
                ModeHWarehouseStakeJournal.IsSlotConsistent = false;
                ModeHRealStakeService.Reject = true;
                runtime.OpenRecoveryShell("journal-only");
                Program.Check(runtime._recoveryPanel.Click("Recovery_ReturnEscrow") && ModeHRealStakeService.Calls == 1,
                    "journal return remains reachable without season or owner: " + haveSeason + "/" + haveOwner);
                Program.Check(ModeHRealStakeService.LastMatch == 3 && ModeHRealStakeService.LastSeed == (haveSeason ? 99 : 0),
                    "journal return uses persisted match identity");
                Program.Check(runtime._recoveryPanel.Visible && runtime._recoveryPanel.Click("Recovery_ReturnEscrow")
                    && ModeHRealStakeService.Calls == 2, "failed journal return retains retry action");
                ModeHRealStakeService.Reject = false;
                Program.Check(runtime._recoveryPanel.Click("Recovery_ReturnEscrow") && ModeHRealStakeService.Calls == 3,
                    "journal return can complete on retry");
                Program.Check(!runtime._recoveryPanel.Click("Recovery_ReturnEscrow"), "terminal journal removes return action");
                bool canResume = runtime._recoveryPanel.Click("Recovery_SameMatchRestart");
                Program.Check(canResume == (haveSeason && haveOwner) && runtime._resumes == (canResume ? 1 : 0),
                    "resume requires season and owner");
                bool canAbandon = runtime._recoveryPanel.Click("Recovery_AbandonSeason");
                Program.Check(canAbandon == haveSeason && runtime._confirmations == (canAbandon ? 1 : 0),
                    "abandon always dispatches through confirmation");
                BossRushConfirmDialog.IsOpen = true;
                runtime._recoveryPanel.PressCancel();
                Program.Check(runtime._recoveryPanel.Visible, "cancel yields to abandon confirmation");
                BossRushConfirmDialog.IsOpen = false;
                runtime._recoveryPanel.PressCancel();
                Program.Check(!runtime._recoveryPanel.Visible, "cancel closes recovery after return without owner");
            }

            Reset();
            ModeHRuntimeGates.IsModeHRiskScanFaulted = true;
            var scan = new RecoveryActions();
            scan.OpenRecoveryShell("scan-failure");
            Program.Check(scan._recoveryPanel.Click("Recovery_RetryScan") && ModeHRuntimeGates.ScanCalls == 1,
                "risk scan retry remains reachable before season is loaded");
            scan._recoveryPanel.PressCancel();
            Program.Check(!scan._recoveryPanel.Visible, "scan-only shell remains dismissible");

            var empty = new ModeHRecoveryPanel();
            empty.Show("empty", null, new List<ModeHActionData>(), false, delegate { });
            empty.PressCancel();
            Program.Check(!empty.Visible, "production panel supplies cancel even for empty read-only action list");
            Reset();
        }
    }
}

// Execute the production dispatcher and page-commit boundary with a reentrant content builder.
using System;

namespace BossRush
{
    internal enum ModeHPage { None, Entry, Brief, Odds, Settlement, Transfer, HallOfFame, ItemBet }
    internal sealed class ModeHPageContent
    {
        internal string Title, FailureText;
        internal bool ReplayCardEntrance;
    }
    internal sealed class RoutingState
    {
        internal ModeHLifecycle Lifecycle;
        internal int StateSequence;
        internal string RunId = "routing";
    }
    internal sealed class RoutingUi
    {
        internal int Opens, Closes;
        internal ModeHPage Page;
        internal ModeHPageContent Content;
        internal void OpenPage(ModeHPage page, ModeHLifecycle phase, string runId, ModeHPageContent content)
        { Opens++; Page = page; Content = content; }
        internal void ClosePage() { Closes++; Page = ModeHPage.None; }
        internal void EnsureHud(Action bell, Action surrender, Action exit) { }
        internal void SetBellCommand(string name, string meaning) { }
    }
    internal partial class UiRouting
    {
        private bool _commandsClosed, _deferPageRoutes, _showItemBetPicker, _replayCardEntrance;
        private string _pageFailureText;
        private int _sceneGeneration = 1;
        private RoutingState _runState;
        private RoutingUi _ui = new RoutingUi();
        private Action _onBuild;
        private int _builds, _recoveries;

        private void EnsureUi() { if (_ui == null) _ui = new RoutingUi(); }
        private void HideRecoveryShell() { }
        private void ObserveLifecycleForSummary(ModeHLifecycle lifecycle) { }
        private bool IsItemBetPickerHost(ModeHLifecycle lifecycle) { return IsPageLifecycle(lifecycle); }
        private bool TryDeferPreparedFighterPage(ModeHLifecycle lifecycle) { return false; }
        private void ApplySettlementDefaults() { }
        private void RouteRecoveryLifecycle(ModeHLifecycle lifecycle) { _recoveries++; _ui.ClosePage(); }
        private void OnBellPressed() { }
        private void OnSurrenderPressed() { }
        private void OnSpectatorExitPressed() { }
        private string ResolveLockedCommandId() { return "fixture"; }
        private string ResolveCommandDisplayName(string id) { return id; }
        private string ResolveLockedCommandPlain() { return "fixture"; }
        private ModeHPageContent Build()
        {
            _builds++;
            Action hook = _onBuild;
            _onBuild = null;
            if (hook != null) hook();
            return new ModeHPageContent { Title = "outer page" };
        }
        private ModeHPageContent BuildDraftPageContent() { return Build(); }
        private ModeHPageContent BuildBriefPageContent() { return Build(); }
        private ModeHPageContent BuildOddsPageContent() { return Build(); }
        private ModeHPageContent BuildSettlementPageContent() { return Build(); }
        private ModeHPageContent BuildTransferPageContent() { return Build(); }
        private ModeHPageContent BuildHallOfFamePageContent() { return Build(); }
        private ModeHPageContent BuildItemBetPickerPage() { return Build(); }

        internal static void Run()
        {
            ModeHLifecycle[] phases = { ModeHLifecycle.Drafting, ModeHLifecycle.RosterLocked,
                ModeHLifecycle.MatchBrief, ModeHLifecycle.LoadoutEditing, ModeHLifecycle.OddsPreview,
                ModeHLifecycle.MatchSettling, ModeHLifecycle.Intermission, ModeHLifecycle.TransferWindow,
                ModeHLifecycle.HallOfFame };
            foreach (ModeHLifecycle phase in phases)
            {
                var normal = new UiRouting { _runState = new RoutingState { Lifecycle = phase } };
                normal.RouteUiForLifecycle(phase);
                Program.Check(normal._builds == 1 && normal._ui.Opens == 1,
                    "normal page still opens: " + phase);
                foreach (bool itemPicker in new[] { false, true })
                {
                    var runtime = new UiRouting { _runState = new RoutingState { Lifecycle = phase },
                        _showItemBetPicker = itemPicker };
                    runtime._onBuild = delegate
                    {
                        runtime._runState.Lifecycle = ModeHLifecycle.Recovering;
                        runtime._runState.StateSequence++;
                        runtime.RouteUiForLifecycle(ModeHLifecycle.Recovering);
                    };
                    runtime.RouteUiForLifecycle(phase);
                    Program.Check(runtime._builds == 1 && runtime._recoveries == 1 && runtime._ui.Opens == 0
                        && runtime._ui.Page == ModeHPage.None,
                        "reentrant recovery cannot be overwritten by stale modal: " + phase + "/picker=" + itemPicker);
                }
            }

            foreach (string invalidation in new[] { "owner", "sequence", "scene", "ui", "shutdown", "null" })
            {
                var runtime = new UiRouting { _runState = new RoutingState { Lifecycle = ModeHLifecycle.MatchBrief } };
                RoutingUi firstUi = runtime._ui;
                runtime._pageFailureText = "current failure";
                runtime._onBuild = delegate
                {
                    switch (invalidation)
                    {
                        case "owner": runtime._runState = new RoutingState { Lifecycle = ModeHLifecycle.MatchBrief }; break;
                        case "sequence": runtime._runState.StateSequence++; break;
                        case "scene": runtime._sceneGeneration++; break;
                        case "ui": runtime._ui = new RoutingUi(); break;
                        case "shutdown": runtime._commandsClosed = true; break;
                        case "null": runtime._runState = null; break;
                    }
                };
                runtime.RouteUiForLifecycle(ModeHLifecycle.MatchBrief);
                Program.Check(runtime._ui.Opens == 0 && firstUi.Opens == 0
                    && runtime._pageFailureText == "current failure", "invalidated build is discarded: " + invalidation);
            }
            var stale = new UiRouting { _runState = new RoutingState { Lifecycle = ModeHLifecycle.Suspended } };
            stale.RouteUiForLifecycle(ModeHLifecycle.MatchBrief);
            Program.Check(stale._builds == 0 && stale._ui.Opens == 0, "stale dispatch cannot build a page for another phase");
        }
    }
}

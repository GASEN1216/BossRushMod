using System;
using System.Collections.Generic;
using Duckov.UI.DialogueBubbles;
using UnityEngine;

namespace BossRush
{
    public enum BossRushTrackedLootboxMode
    {
        None = 0,
        ModeE = 1,
        ModeF = 2
    }

    internal sealed class AwenLootSweepTarget
    {
        public InteractableLootbox Lootbox;
        public Vector3 VisitPosition;
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private readonly AwenLootSweepRuntime awenLootSweepRuntime = new AwenLootSweepRuntime();

        private void BindAwenLootSweepRuntime()
        {
            awenLootSweepRuntime.BindServices(TryGetActiveModeEFLootboxContext, CanUseAwenLootSweepInCurrentMode,
                IsAwenLootSweepSessionStillValid, () => courierNPCInstance, () => courierController,
                (typeId, name, bubble, drop) => modeFRuntime.TryGiveItemToPlayerOrDrop(typeId, name, bubble, drop),
                ShowBigBanner, ShowMessage);
        }

        private bool TryGetActiveModeEFLootboxContext(out BossRushTrackedLootboxMode mode, out int sessionToken)
        {
            mode = BossRushTrackedLootboxMode.None;
            sessionToken = 0;

            if (modeFActive && modeFState.IsActive && CurrentModeFSessionToken > 0)
            {
                mode = BossRushTrackedLootboxMode.ModeF;
                sessionToken = CurrentModeFSessionToken;
                return true;
            }

            if (modeEActive && CurrentModeESessionToken > 0)
            {
                mode = BossRushTrackedLootboxMode.ModeE;
                sessionToken = CurrentModeESessionToken;
                return true;
            }

            return false;
        }

        internal void ResetModeEFLootboxTrackerState()
        { awenLootSweepRuntime.ResetModeEFLootboxTrackerState(); }

        internal void CaptureModeEFLootboxBaseline()
        { awenLootSweepRuntime.CaptureModeEFLootboxBaseline(); }

        internal bool CanUseAwenLootSweepInCurrentMode()
        {
            return (IsActive && !IsModeDActive) || (IsBossRushArenaActive && !IsModeDActive) || modeEActive || modeFActive;
        }

        internal void InvalidateAwenLootSweepTargetCache()
        { awenLootSweepRuntime.InvalidateAwenLootSweepTargetCache(); }

        internal int GetCurrentAwenLootSweepTargetCount()
        { return awenLootSweepRuntime.GetCurrentAwenLootSweepTargetCount(); }

        internal int CopyCurrentAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        { return awenLootSweepRuntime.CopyCurrentAwenLootSweepTargets(output); }

        internal int CopyFreshAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        { return awenLootSweepRuntime.CopyFreshAwenLootSweepTargets(output); }

        internal void NotifyAwenLootSweepRunnerDestroyed(AwenLootSweepRunner runner)
        { awenLootSweepRuntime.NotifyAwenLootSweepRunnerDestroyed(runner); }

        internal bool IsAwenLootSweepSessionStillValid(BossRushTrackedLootboxMode mode, int sessionToken, int relatedScene)
        {
            switch (mode)
            {
                case BossRushTrackedLootboxMode.ModeE:
                    return IsModeESessionStillValid(sessionToken, relatedScene);
                case BossRushTrackedLootboxMode.ModeF:
                    return IsModeFSessionStillValid(sessionToken, relatedScene);
                default:
                    return false;
            }
        }

        internal void TryRegisterModeEFLootbox(InteractableLootbox lootbox)
        { awenLootSweepRuntime.TryRegisterModeEFLootbox(lootbox); }

        internal void RegisterModeEFBossDeathForSweepToken()
        { awenLootSweepRuntime.RegisterModeEFBossDeathForSweepToken(); }

        internal bool CanUseAwenLootSweepToken()
        { return awenLootSweepRuntime.CanUseAwenLootSweepToken(); }

        internal bool CanUseAwenLootSweepToken(CharacterMainControl player, bool showFailureFeedback)
        { return awenLootSweepRuntime.CanUseAwenLootSweepToken(player, showFailureFeedback); }

        internal bool TryActivateAwenLootSweepToken(CharacterMainControl player)
        { return awenLootSweepRuntime.TryActivateAwenLootSweepToken(player); }

        internal bool TryRefundAwenLootSweepToken()
        { return awenLootSweepRuntime.TryRefundAwenLootSweepToken(); }

    }
}

using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void RegisterZombieModeRunOnlyObject(int runId, ZombieModeRunOnlyObjectKind kind, GameObject gameObject, UnityEngine.Object target, Action cleanupAction)
        {
            if (runId <= 0 || runId != zombieModeRunState.RunId)
            {
                return;
            }

            ZombieModeRunOnlyRecord record = new ZombieModeRunOnlyRecord();
            record.RunId = runId;
            record.Kind = kind;
            record.GameObject = gameObject;
            record.Target = target;
            record.CleanupAction = cleanupAction;
            zombieModeRunState.RunOnlyObjects.Add(record);
        }

        private Coroutine StartZombieModeCoroutine(IEnumerator routine, int runId)
        {
            if (!IsZombieModeRunValid(runId) || routine == null)
            {
                return null;
            }

            Coroutine coroutine = StartCoroutine(routine);
            if (coroutine != null)
            {
                RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Coroutine, null, null, delegate
                {
                    try { StopCoroutine(coroutine); } catch (System.Exception e) { DevLog("[ZombieMode] StopCoroutine 失败: " + e.Message); }
                });
            }

            return coroutine;
        }

        private void InvalidateZombieModeRun()
        {
            nextZombieModeRunId++;
            if (zombieModeRunState.RunId > 0)
            {
                zombieModeRunState.RunId = -zombieModeRunState.RunId;
            }
            zombieModeRunState.IsCleaningUp = true;
        }

        private void CleanupZombieModeRunOnlyState(ZombieModeFailureReason reason, bool destroyGameObjects)
        {
            if (ShouldSettleZombieModeFailureInsurance(reason))
            {
                SettleZombieModeFailureInsuranceShell(zombieModeRunState.RunId);
            }

            RemoveZombieModeAttributeModifiers();
            InvalidateZombieModeRun();

            for (int i = zombieModeRunState.RunOnlyObjects.Count - 1; i >= 0; i--)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null)
                {
                    continue;
                }

                try
                {
                    record.Cleanup(destroyGameObjects);
                }
                catch (Exception e)
                {
                    DevLog("[ZombieMode] Run-only cleanup failed: " + reason.ToString() + " - " + e.Message);
                }
            }

            zombieModeRunState.RunOnlyObjects.Clear();
            ClearZombieModeRewardShell();
            RestoreZombieModeMapIsolationShell();
        }

        private bool ShouldSettleZombieModeFailureInsurance(ZombieModeFailureReason reason)
        {
            return reason != ZombieModeFailureReason.SuccessfulExtraction &&
                   (reason == ZombieModeFailureReason.PlayerDeath ||
                    reason == ZombieModeFailureReason.ManualExit ||
                    reason == ZombieModeFailureReason.SceneSwitched ||
                    reason == ZombieModeFailureReason.UnexpectedSceneUnload ||
                    reason == ZombieModeFailureReason.Unknown);
        }

        private void CleanupZombieModeForSceneChange(ZombieModeFailureReason reason)
        {
            if (!IsZombieModeActive && !IsZombieModeStartupInProgress() && zombieModeRunState.RunOnlyObjects.Count <= 0)
            {
                return;
            }

            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.Exiting;
            if (ShouldRollbackZombieModeEntryResources())
            {
                RollbackZombieModeInventoryTransferShell();
                RefundZombieModeInvitationIfNeeded();
                RefundZombieModeCashIfNeeded();
            }

            CleanupZombieModeRunOnlyState(reason, true);
            zombieModeRunState.ClearRuntime();
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.None;
            pendingZombieModeEntry = false;
            zombieModeEntryTransaction.Reset();
        }

        private void CleanupZombieModeOnDestroy()
        {
            if (ShouldRollbackZombieModeEntryResources())
            {
                RollbackZombieModeInventoryTransferShell();
                RefundZombieModeInvitationIfNeeded();
                RefundZombieModeCashIfNeeded();
            }

            CleanupZombieModeRunOnlyState(ZombieModeFailureReason.Unknown, true);
            zombieModeRunState.ClearRuntime();
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.None;
            pendingZombieModeEntry = false;
            zombieModeEntryTransaction.Reset();
        }
    }
}

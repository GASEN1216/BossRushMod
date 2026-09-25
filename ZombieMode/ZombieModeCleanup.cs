using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void RegisterZombieModeRunOnlyObject(int runId, ZombieModeRunOnlyObjectKind kind, GameObject gameObject, UnityEngine.Object target, Action cleanupAction)
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.RegisterZombieModeRunOnlyObject(runId, kind, gameObject, target, cleanupAction);
        }

        private void PruneZombieModeRunOnlyEnemyRecords(int runId)
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.PruneZombieModeRunOnlyEnemyRecords(runId);
        }

        private void RemoveZombieModeRunOnlyObjectRecord(UnityEngine.Object target)
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.RemoveZombieModeRunOnlyObjectRecord(target);
        }

        private void PruneZombieModeUnknownRunOnlyRecords()
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.PruneZombieModeUnknownRunOnlyRecords();
        }

        private void CleanupZombieModeEnemiesNearPlayerSafeZone(int runId, string reason)
        {
            zombieModeRuntimeModule.CleanupZombieModeEnemiesNearPlayerSafeZone(runId, reason);
        }

        /// <summary>
        /// 安全区启用时清空区内敌人。普通丧尸直接移除，Boss 只推出边界。
        /// 被移除的普通丧尸有意不计入击杀、也不下调击杀目标：安全区是站位工具，
        /// 不是清怪手段，缺口由环境压力补刷回填。
        /// </summary>
        private void ClearZombieModeEnemiesInsideActiveSafeZone(int runId, string reason)
        {
            zombieModeRuntimeModule.ClearZombieModeEnemiesInsideActiveSafeZone(runId, reason);
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

        private async UniTask<bool> WaitForZombieModeRuntimeResumeAsync(int runId)
        {
            while (IsZombieModeRunValid(runId) && IsZombieModeRuntimePaused())
            {
                await UniTask.Yield();
            }

            return IsZombieModeRunValid(runId);
        }

        private void InvalidateZombieModeRun()
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.InvalidateZombieModeRun();
        }

        private void CleanupZombieModeRunOnlyState(ZombieModeFailureReason reason, bool destroyGameObjects)
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.CleanupZombieModeRunOnlyState(reason, destroyGameObjects);
        }

        private bool ShouldSettleZombieModeFailureInsurance(ZombieModeFailureReason reason)
        {
            return zombieModeRuntimeModule != null && zombieModeRuntimeModule.ShouldSettleZombieModeFailureInsurance(reason);
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

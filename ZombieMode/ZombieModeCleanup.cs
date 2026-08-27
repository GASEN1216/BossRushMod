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
            if (runId <= 0 || runId != zombieModeRunState.RunId)
            {
                return;
            }

            if (kind == ZombieModeRunOnlyObjectKind.RewardUi)
            {
                for (int i = zombieModeRunState.RunOnlyObjects.Count; i-- > 0;)
                {
                    ZombieModeRunOnlyRecord existing = zombieModeRunState.RunOnlyObjects[i];
                    if (existing != null &&
                        existing.RunId == runId &&
                        existing.Kind == ZombieModeRunOnlyObjectKind.RewardUi &&
                        existing.GameObject == null &&
                        existing.CleanupAction == null)
                    {
                        zombieModeRunState.RunOnlyObjects.RemoveAt(i);
                    }
                }
            }

            ZombieModeRunOnlyRecord record = new ZombieModeRunOnlyRecord();
            record.RunId = runId;
            record.Kind = kind;
            record.GameObject = gameObject;
            record.Target = target;
            record.CleanupAction = cleanupAction;
            zombieModeRunState.RunOnlyObjects.Add(record);
        }

        private void PruneZombieModeRunOnlyEnemyRecords(int runId)
        {
            if (runId <= 0 || zombieModeRunState == null || zombieModeRunState.RunOnlyObjects.Count <= 0)
            {
                return;
            }

            for (int i = zombieModeRunState.RunOnlyObjects.Count; i-- > 0;)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null)
                {
                    zombieModeRunState.RunOnlyObjects.RemoveAt(i);
                    continue;
                }

                if (record.RunId != runId ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss))
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null && record.GameObject != null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (marker != null)
                    {
                        record.Target = marker;
                    }
                }

                bool shouldPrune = record.GameObject == null ||
                                   marker == null ||
                                   marker.RunId != runId ||
                                   marker.DeathSettled ||
                                   marker.RemovedFromRuntime;
                if (!shouldPrune)
                {
                    continue;
                }

                try
                {
                    record.Cleanup(false);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] Run-only enemy prune failed: " + e.Message);
                }

                zombieModeRunState.RunOnlyObjects.RemoveAt(i);
            }
        }

        private void RemoveZombieModeRunOnlyObjectRecord(UnityEngine.Object target)
        {
            if (target == null || zombieModeRunState == null || zombieModeRunState.RunOnlyObjects == null)
            {
                return;
            }

            for (int i = zombieModeRunState.RunOnlyObjects.Count; i-- > 0;)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null || (record.GameObject != target && record.Target != target))
                {
                    continue;
                }

                try
                {
                    record.Cleanup(false);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] Run-only object record cleanup failed: " + e.Message);
                }
                zombieModeRunState.RunOnlyObjects.RemoveAt(i);
            }
        }

        private void PruneZombieModeUnknownRunOnlyRecords()
        {
            if (zombieModeRunState == null || zombieModeRunState.RunOnlyObjects == null)
            {
                return;
            }

            for (int i = zombieModeRunState.RunOnlyObjects.Count; i-- > 0;)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null)
                {
                    zombieModeRunState.RunOnlyObjects.RemoveAt(i);
                    continue;
                }

                if (record.Kind != ZombieModeRunOnlyObjectKind.Unknown ||
                    record.GameObject != null ||
                    record.Target != null)
                {
                    continue;
                }

                try
                {
                    record.Cleanup(false);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] prune run-only record cleanup 失败: " + e.Message);
                }
                zombieModeRunState.RunOnlyObjects.RemoveAt(i);
            }
        }

        private void CleanupZombieModeEnemiesNearPlayerSafeZone(int runId, string reason)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            Vector3 center = player.transform.position;
            float radius = ZombieModeTuning.SafeZoneRadius;
            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            if (count <= 0)
            {
                return;
            }

            int cleaned = 0;
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId || marker.IsBoss || marker.DeathSettled || marker.RemovedFromRuntime)
                {
                    continue;
                }

                Vector3 delta = marker.transform.position - center;
                delta.y = 0f;
                if (delta.sqrMagnitude > radius * radius)
                {
                    continue;
                }

                try
                {
                    marker.RemovedFromRuntime = true;
                    CharacterMainControl owner = marker.Owner;
                    if (owner == null)
                    {
                        owner = marker.GetComponent<CharacterMainControl>();
                    }
                    UnregisterZombieModeEnemyInstanceId(owner);
                    zombieModeRunState.LivingZombieCount = Mathf.Max(0, zombieModeRunState.LivingZombieCount - 1);
                    zombieModeRunState.LivingNormalZombieCount = Mathf.Max(0, zombieModeRunState.LivingNormalZombieCount - 1);

                    GameObject enemyObject = marker.gameObject;
                    if (enemyObject != null)
                    {
                        // Destroy 会延迟到帧尾；先停用 AI，避免其在同帧继续执行并访问已失效状态。
                        enemyObject.SetActive(false);
                        Destroy(enemyObject);
                    }
                    cleaned++;
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] safe-zone enemy cleanup failed: " + reason + " - " + e.Message);
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
            PruneZombieModeRunOnlyEnemyRecords(runId);
            if (cleaned > 0)
            {
                DevLog("[ZombieMode] safe-zone cleanup " + reason + ": removed " + cleaned + " nearby enemies");
            }
        }

        /// <summary>
        /// 安全区启用时清空区内敌人。普通丧尸直接移除，Boss 只推出边界。
        /// 被移除的普通丧尸有意不计入击杀、也不下调击杀目标：安全区是站位工具，
        /// 不是清怪手段，缺口由环境压力补刷回填。
        /// </summary>
        private void ClearZombieModeEnemiesInsideActiveSafeZone(int runId, string reason)
        {
            if (!IsZombieModeRunValid(runId) || !AnyZombieModeSafeZoneActive)
            {
                return;
            }

            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, true);
            if (count <= 0)
            {
                return;
            }

            int removed = 0;
            int ejectedBosses = 0;
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null ||
                    marker.RunId != runId ||
                    marker.DeathSettled ||
                    marker.RemovedFromRuntime ||
                    !IsZombieModePositionInsideActiveSafeZone(marker.transform.position))
                {
                    continue;
                }

                if (marker.IsBoss)
                {
                    if (TryMoveZombieModeEnemyOutsideSafeZone(marker.gameObject, marker, true))
                    {
                        ejectedBosses++;
                    }
                    continue;
                }

                try
                {
                    marker.RemovedFromRuntime = true;
                    CharacterMainControl owner = marker.Owner;
                    if (owner == null)
                    {
                        owner = marker.GetComponent<CharacterMainControl>();
                    }

                    UnregisterZombieModeEnemyInstanceId(owner);
                    UnregisterEnemyRecovery(owner);
                    zombieModeRunState.LivingZombieCount = Mathf.Max(0, zombieModeRunState.LivingZombieCount - 1);
                    zombieModeRunState.LivingNormalZombieCount = Mathf.Max(0, zombieModeRunState.LivingNormalZombieCount - 1);
                    if (marker.gameObject != null)
                    {
                        marker.gameObject.SetActive(false);
                        Destroy(marker.gameObject);
                    }
                    removed++;
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] safe-zone deployment cleanup failed: " + reason + " - " + e.Message);
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
            PruneZombieModeRunOnlyEnemyRecords(runId);
            if (removed > 0 || ejectedBosses > 0)
            {
                DevLog(
                    "[ZombieMode] safe-zone deployment " + reason +
                    ": removed=" + removed +
                    ", ejectedBosses=" + ejectedBosses);
            }
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
            RemoveZombieModeOptionRuntimeEffects();
            CleanupZombieModeFortificationInteractionState();
            InvalidateZombieModeRun();
            ClearZombieModeSupportSpawnQueue();

            RunScopedRegistry.ForEachReverse(
                zombieModeRunState.RunOnlyObjects,
                record => record.Cleanup(destroyGameObjects),
                (e, record) => DevLog("[ZombieMode] Run-only cleanup failed: " + reason.ToString() + " - " + e.Message));

            zombieModeRunState.RunOnlyObjects.Clear();
            // OnHurt/OnDead hot path 集合也在局结束时清掉（审查 §3.1）。
            ClearZombieModeEnemyInstanceIds();
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

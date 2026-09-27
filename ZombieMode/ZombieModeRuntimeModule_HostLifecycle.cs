using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    // 构造期备用状态和宿主收尾协议属于模式；活动状态仍由已注册 RuntimeModule 持有。
    // 未 Awake 或重复宿主沿用同一批备用对象，Attach 迁交引用，Detach 按原身份退回。
    internal sealed class ZombieModeHostLifecycle
    {
        private ZombieModeRuntimeModule zombieModeRuntimeModule;
        private ZombieModeRunState zombieModeUnattachedRunState = new ZombieModeRunState();
        private ZombieModeEntryTransaction zombieModeUnattachedEntryTransaction = new ZombieModeEntryTransaction();
        private Dictionary<string, int[]> zombieModeUnattachedRewardCandidateCache = new Dictionary<string, int[]>();
        private List<int> zombieModeUnattachedRewardCandidateScratch = new List<int>();
        private HashSet<int> zombieModeUnattachedOpaqueFilterLogIds = new HashSet<int>();
        private bool zombieModeUnattachedPendingEntry;

        internal void AttachZombieModeRuntimeModule(ZombieModeRuntimeModule module)
        {
            if (module == null) return;
            module.AdoptHostState(
                zombieModeUnattachedRunState,
                zombieModeUnattachedEntryTransaction,
                zombieModeUnattachedRewardCandidateCache,
                zombieModeUnattachedRewardCandidateScratch,
                zombieModeUnattachedOpaqueFilterLogIds,
                zombieModeUnattachedPendingEntry);
            zombieModeRuntimeModule = module;
            zombieModeUnattachedRunState = null;
            zombieModeUnattachedEntryTransaction = null;
            zombieModeUnattachedRewardCandidateCache = null;
            zombieModeUnattachedRewardCandidateScratch = null;
            zombieModeUnattachedOpaqueFilterLogIds = null;
        }

        internal void DetachZombieModeRuntimeModule(ZombieModeRuntimeModule module)
        {
            if (!ReferenceEquals(zombieModeRuntimeModule, module)) return;
            zombieModeUnattachedRunState = module.RunState;
            zombieModeUnattachedEntryTransaction = module.EntryTransaction;
            zombieModeUnattachedRewardCandidateCache = module.RewardCandidateCache;
            zombieModeUnattachedRewardCandidateScratch = module.RewardCandidateScratch;
            zombieModeUnattachedOpaqueFilterLogIds = module.OpaqueFilterLogIds;
            zombieModeUnattachedPendingEntry = module.PendingEntry;
            zombieModeRuntimeModule = null;
        }

        internal ZombieModeRunState zombieModeRunState
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.RunState : zombieModeUnattachedRunState; }
        }

        internal ZombieModeEntryTransaction zombieModeEntryTransaction
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.EntryTransaction : zombieModeUnattachedEntryTransaction; }
        }

        internal Dictionary<string, int[]> zombieModeRewardCandidateCache
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.RewardCandidateCache : zombieModeUnattachedRewardCandidateCache; }
        }

        internal List<int> zombieModeRewardSafeCandidateScratch
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.RewardCandidateScratch : zombieModeUnattachedRewardCandidateScratch; }
        }

        internal HashSet<int> zombieModeOpaqueFilterLogIds
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.OpaqueFilterLogIds : zombieModeUnattachedOpaqueFilterLogIds; }
        }

        internal bool pendingZombieModeEntry
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.PendingEntry : zombieModeUnattachedPendingEntry; }
            set
            {
                if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.PendingEntry = value;
                else zombieModeUnattachedPendingEntry = value;
            }
        }

        internal int nextZombieModeRunId
        {
            get { return ZombieModeRuntimeModule.NextRunId; }
            set { ZombieModeRuntimeModule.NextRunId = value; }
        }

        internal ZombieModeRuntimeModule Runtime { get { return zombieModeRuntimeModule; } }
        internal void CleanupZombieModeForSceneChange(ZombieModeFailureReason reason)
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

        internal void CleanupZombieModeOnDestroy()
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

        public bool IsZombieModeActive
        {
            get
            {
                return ZombieModePhaseGuards.IsRunActive(zombieModeRunState.LifecyclePhase);
            }
        }

        private bool IsZombieModeStartupInProgress()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.IsZombieModeStartupInProgress();
            ZombieModeLifecyclePhase phase = zombieModeRunState.LifecyclePhase;
            return pendingZombieModeEntry ||
                   (ZombieModePhaseGuards.IsBeforeActive(phase) &&
                    phase != ZombieModeLifecyclePhase.WaitingStarterChoice);
        }

        private bool ShouldRollbackZombieModeEntryResources()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.ShouldRollbackZombieModeEntryResources()
                : !zombieModeRunState.EntryResourcesFinalized && !zombieModeEntryTransaction.EntryResourcesFinalized;
        }

        private void RefundZombieModeInvitationIfNeeded()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModeInvitationIfNeeded();
        }

        private void RefundZombieModeCashIfNeeded()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModeCashIfNeeded();
        }

        private void RollbackZombieModeInventoryTransferShell()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RollbackZombieModeInventoryTransfer();
        }

        private void CleanupZombieModeRunOnlyState(ZombieModeFailureReason reason, bool destroyGameObjects)
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.CleanupZombieModeRunOnlyState(reason, destroyGameObjects);
        }
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        internal Coroutine StartZombieModeCoroutine(IEnumerator routine, int runId)
        {
            if (!IsZombieModeRunValid(runId) || routine == null)
            {
                return null;
            }

            ModBehaviour coroutineOwner = owner;
            Coroutine coroutine = coroutineOwner.StartCoroutine(routine);
            if (coroutine != null)
            {
                RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Coroutine, null, null, delegate
                {
                    try { coroutineOwner.StopCoroutine(coroutine); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] StopCoroutine 失败: " + e.Message); }
                });
            }

            return coroutine;
        }
    }
}

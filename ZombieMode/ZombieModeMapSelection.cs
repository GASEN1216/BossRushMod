using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool TryBeginZombieModeMapSelectionShell()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryBeginMapSelectionShell();
        }

        private void CancelZombieModeMapSelectionShell()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CancelMapSelectionShell();
        }

        /// <summary>
        /// 丧尸模式入口的两类拒绝：Mode H 真实资产风险门，与「已有其他模式在跑」。
        ///
        /// 两者成因完全不同，文案必须分开：混用同一句会让玩家去关一个根本没在跑的模式。
        /// Mode H 侧被拒时先给一次自愈重试（读档 I/O 异常可恢复），再取对应文案。
        /// 放在本文件而不是 ZombieModeEntry.cs：后者已经顶到
        /// tests/large_file_existing_allowlist.txt 的行数上限，不允许再涨。
        /// </summary>
        private bool IsZombieModeStartBlocked(out string failureReason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.IsZombieModeStartBlocked(out failureReason);

            failureReason = null;
            if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
            {
                failureReason = L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey());
                return true;
            }
            if (IsAnyBossRushLikeModeActive() || IsZombieModeStartupInProgress())
            {
                failureReason = L10n.T("BossRush_ZombieMode_OtherModeActive");
                return true;
            }
            return false;
        }
    }


    /// <summary>
    /// ZombieMode 旧 partial 的窄兼容面。活动状态对象在 Awake 接线后由唯一 RuntimeModule 持有；
    /// 未注册的重复宿主仍用构造期备用对象完成旧有销毁路径。
    /// </summary>
    public partial class ModBehaviour
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

        private ZombieModeRunState zombieModeRunState
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.RunState : zombieModeUnattachedRunState; }
        }

        private ZombieModeEntryTransaction zombieModeEntryTransaction
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.EntryTransaction : zombieModeUnattachedEntryTransaction; }
        }

        private Dictionary<string, int[]> zombieModeRewardCandidateCache
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.RewardCandidateCache : zombieModeUnattachedRewardCandidateCache; }
        }

        private List<int> zombieModeRewardSafeCandidateScratch
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.RewardCandidateScratch : zombieModeUnattachedRewardCandidateScratch; }
        }

        private HashSet<int> zombieModeOpaqueFilterLogIds
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.OpaqueFilterLogIds : zombieModeUnattachedOpaqueFilterLogIds; }
        }

        private bool pendingZombieModeEntry
        {
            get { return zombieModeRuntimeModule != null ? zombieModeRuntimeModule.PendingEntry : zombieModeUnattachedPendingEntry; }
            set
            {
                if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.PendingEntry = value;
                else zombieModeUnattachedPendingEntry = value;
            }
        }

        private int nextZombieModeRunId
        {
            get { return ZombieModeRuntimeModule.NextRunId; }
            set { ZombieModeRuntimeModule.NextRunId = value; }
        }

        internal bool IsZombieModeStartBlockedForRuntimeModule(out string failureReason)
        {
            return IsZombieModeStartBlocked(out failureReason);
        }

        internal System.Collections.IEnumerator ForceTeleportToSubSceneForRuntimeModule(string targetSubScene, Vector3 targetPosition)
        {
            return ForceTeleportToSubScene(targetSubScene, targetPosition);
        }

        internal bool ReadSceneLoaderDoneWithWarningForRuntimeModule(string context)
        {
            return ReadSceneLoaderDoneWithWarning(context);
        }

        internal bool ReadLevelInitedWithWarningForRuntimeModule(string context)
        {
            return ReadLevelInitedWithWarning(context);
        }

        internal void PrepareSoulCubePrefabCacheForRuntimeModule()
        {
            PrepareSoulCubePrefabCacheForZombieRun();
        }

        internal bool PrepareZombieModeInventoryTransferForRuntimeModule(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.PrepareZombieModeInventoryTransfer(runId);
        }

        internal bool CollectZombieModeSpawnPointsForRuntimeModule(int runId)
        {
            return CollectZombieModeSpawnPoints(runId);
        }

        internal bool ApplyZombieModeMapIsolationForRuntimeModule(int runId)
        {
            return ApplyZombieModeMapIsolationShell(runId);
        }

        internal bool EnsureZombieModeCharacterPresetForRuntimeModule()
        {
            EnsureCharacterPresetsCacheReady();
            return cachedCharacterPresets != null && cachedCharacterPresets.ContainsKey("Cname_Zombie");
        }

        internal bool InitializeZombieModeContainersForRuntimeModule(int runId)
        {
            return InitializeZombieModeContainersShell(runId);
        }

        internal void RegisterZombieModeEventListenersForRuntimeModule(int runId)
        {
            RegisterZombieModeEventListeners(runId);
        }

        internal void CreateZombieModeHudForRuntimeModule(int runId)
        {
            CreateZombieModeHud(runId);
        }

        internal void ShowZombieModeStarterChoiceForRuntimeModule(int runId)
        {
            ShowZombieModeStarterChoice(runId);
        }

        internal void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason)
        {
            CleanupZombieModeForSceneChange(reason);
        }

        internal void TickZombieModeWaveControllerForRuntimeModule(float deltaTime)
        {
            TickZombieModeWaveController(deltaTime);
        }

        internal void TickZombieModeDropsAndPerformanceForRuntimeModule(float deltaTime)
        {
            TickZombieModeDropsAndPerformance(deltaTime);
        }

        internal void TickZombieModeBossControllerForRuntimeModule(float deltaTime)
        {
            TickZombieModeBossController(deltaTime);
        }

        internal void TickZombieModeTemporaryNpcProtectionForRuntimeModule()
        {
            TickZombieModeTemporaryNpcProtection();
        }

        internal void UpdateModeFFortificationHighlightsForRuntimeModule()
        {
            UpdateModeFFortificationHighlights();
        }

        internal void ShowZombieModeRewardSelectionForRuntimeModule(int runId, bool bossNode, bool restEditorExpanded)
        {
            ShowZombieModeRewardSelection(runId, bossNode, restEditorExpanded);
        }

        internal void SettleZombieModeFailureInsuranceForRuntimeModule(int runId)
        {
            SettleZombieModeFailureInsuranceShell(runId);
        }

        internal void RemoveZombieModeAttributeModifiersForRuntimeModule()
        {
            RemoveZombieModeAttributeModifiers();
        }

        internal void RemoveZombieModeOptionRuntimeEffectsForRuntimeModule()
        {
            RemoveZombieModeOptionRuntimeEffects();
        }

        internal void CleanupZombieModeFortificationInteractionStateForRuntimeModule()
        {
            CleanupZombieModeFortificationInteractionState();
        }

        internal void ClearZombieModeSupportSpawnQueueForRuntimeModule()
        {
            ClearZombieModeSupportSpawnQueue();
        }

        internal void ClearZombieModeEnemyInstanceIdsForRuntimeModule()
        {
            ClearZombieModeEnemyInstanceIds();
        }

        internal void ClearZombieModeRewardShellForRuntimeModule()
        {
            ClearZombieModeRewardShell();
        }

        internal void RestoreZombieModeMapIsolationShellForRuntimeModule()
        {
            RestoreZombieModeMapIsolationShell();
        }

    }
}

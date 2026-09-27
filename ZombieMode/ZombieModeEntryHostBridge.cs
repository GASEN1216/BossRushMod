using System.Collections;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Duckov.MiniMaps;
using Duckov.Scenes;
using Duckov.Economy;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Duckov.Buffs;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // ZombieModeCleanup.cs
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
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.StartZombieModeCoroutine(routine, runId) : null;
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
            zombieModeHostLifecycle.CleanupZombieModeForSceneChange(reason);
        }

        private void CleanupZombieModeOnDestroy()
        {
            zombieModeHostLifecycle.CleanupZombieModeOnDestroy();
        }

        // ZombieModeDebug.cs
        private void DebugResetZombieModeShell()
        {
            CleanupZombieModeForSceneChange(ZombieModeFailureReason.ManualExit);
        }

        // ZombieModeEntry.cs
        private const int ZOMBIE_MODE_INITIAL_PURIFICATION_POINTS = 0;


        public bool IsZombieModeActive
        {
            get
            {
                return ZombieModePhaseGuards.IsRunActive(zombieModeRunState.LifecyclePhase);
            }
        }

        public int ZombieModeCurrentRunId
        {
            get { return zombieModeRunState.RunId; }
        }

        public bool IsAnyBossRushLikeModeActive()
        {
            // Mode G 门控（加法分支）：只纳入 IsModeGRunInProgress（lifecycle），
            // 绝不纳入 late sink quarantine，避免永不返回的 late task 长期抑制普通地图公共 NPC。
            return IsActive || IsModeDActive || IsBossRushArenaActive || IsModeEActive || IsModeFActive || IsZombieModeActive || IsModeGRunInProgressSafe();
        }

        public bool UsesArenaSupportNpcPlacement()
        {
            return IsModeEActive || IsModeFActive || IsZombieModeActive;
        }

        public bool ShouldSuppressBaseNpcSpawnForCurrentMode()
        {
            return IsAnyBossRushLikeModeActive();
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

        public bool CanStartZombieModeMapSelectionPhase1(out string failureReason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.CanStartZombieModeMapSelectionPhase1(out failureReason);
            failureReason = null;
            try
            {
                if (ModeGRuntimeGates.IsModeGEntryBlocked)
                {
                    failureReason = L10n.T("BossRush_ZombieMode_OtherModeActive");
                    return false;
                }
            }
            catch { }
            if (IsZombieModeStartBlocked(out failureReason)) return false;
            return true;
        }

        public bool TryBeginZombieModeMapSelectionPhase1(out string failureReason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.TryBeginZombieModeMapSelectionPhase1(out failureReason);
            failureReason = null;
            return false;
        }

        public void MarkZombieModeMapConfirmedPhase1()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.MarkZombieModeMapConfirmedPhase1();
        }

        public bool IsZombieModeMapLoadReadyPhase1()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeMapLoadReadyPhase1();
        }

        public void AbortZombieModeMapLoadPhase1(ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.AbortZombieModeMapLoadPhase1(reason);
        }

        // 入场预检查（邀请函/其他模式互斥）。随身物品不阻止入场，入图后统一转入仓库。
        private bool TryRunZombieModePrechecks(out ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.TryRunZombieModePrechecks(out reason);
            reason = ZombieModeFailureReason.InitializationFailed;
            return false;
        }

        // 资源暂扣：丧尸模式自行提交邀请函与现金，地图选择条目只借用原版 UI 外观。
        private bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.CommitZombieModeEntryResourcesShell(out reason);
            reason = ZombieModeFailureReason.InitializationFailed;
            return false;
        }

        /// <summary>
        /// 入场回滚时退还已扣的入场现金。真正的退款与欠账落在 <see cref="ZombieModeEntryDebt.RefundCash"/>；
        /// 它返回 false 表示钱既没退出去、账也没记下，此时**保留事务状态**等下一次清理路径重试
        /// （修复前无条件 `finally` 清状态，玩家已扣的入场费会永久蒸发，CR-2026-09-11-019）。
        /// </summary>
        private void RefundZombieModeCashIfNeeded()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModeCashIfNeeded();
        }

        public void CancelZombieModeMapSelectionPhase1()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CancelZombieModeMapSelectionPhase1();
        }

        private bool ShouldPreserveZombieModeStartupForSceneLoad(Scene scene)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.ShouldPreserveZombieModeStartupForSceneLoad(scene);
        }

        private bool TryHandleZombieModePendingMapSceneLoaded(Scene scene, BossRushMapConfig loadedMapConfig)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig);
        }

        private System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(Scene scene, Vector3? customPos)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null) yield break;
            System.Collections.IEnumerator routine = module.WaitForZombieModeTargetSceneActiveThenInitialize(scene, customPos);
            while (routine.MoveNext()) yield return routine.Current;
        }

        private int BeginZombieModeRunShell(int sceneBuildIndex, string sceneName)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.BeginZombieModeRunShell(sceneBuildIndex, sceneName) : 0;
        }

        private ZombieModeMapProfile BuildZombieModeMapProfile(int sceneBuildIndex, string sceneName)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.BuildZombieModeMapProfile(sceneBuildIndex, sceneName) : new ZombieModeMapProfile();
        }

        private bool IsZombieModeRunValid(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeRunValid(runId);
        }

        private bool ShouldRollbackZombieModeEntryResources()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.ShouldRollbackZombieModeEntryResources()
                : !zombieModeRunState.EntryResourcesFinalized && !zombieModeEntryTransaction.EntryResourcesFinalized;
        }

        private void TickZombieMode(float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieMode(deltaTime);
        }

        internal bool IsZombieModeGamePaused()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.IsZombieModeGamePaused();
            try
            {
                return PauseMenu.Instance != null && PauseMenu.Instance.Shown;
            }
            catch
            {
                return false;
            }
        }

        internal bool IsZombieModeRuntimePaused()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.IsZombieModeRuntimePaused()
                : ZombieModeUIHelper.IsModalInputPaused || IsZombieModeGamePaused() || CameraMode.Active;
        }

        private void RefreshZombieModeRuntimePauseClock()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefreshZombieModeRuntimePauseClock();
        }

        private void ResetZombieModeRuntimePauseClock()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ResetZombieModeRuntimePauseClock();
        }

        internal float GetZombieModeRuntimeNow()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }

        private bool InitializeZombieModeRunAfterMapLoaded(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.InitializeZombieModeRunAfterMapLoaded(runId);
        }

        private bool GrantZombieModeBeacon(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.GrantZombieModeBeacon(runId);
        }

        private void FinalizeZombieModeEntryResources()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.FinalizeZombieModeEntryResources();
        }

        /// <summary>
        /// 入场回滚时返还已扣的尸潮邀请函。实例化与欠账落在 <see cref="ZombieModeEntryDebt.RefundInvitation"/>；
        /// 它返回 false 表示实例造不出来、账也没记下，此时保留事务状态等下一次清理路径重试。
        /// </summary>
        private void RefundZombieModeInvitationIfNeeded()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModeInvitationIfNeeded();
        }

        private void FailZombieModeBeforeActive(ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.FailZombieModeBeforeActive(reason);
        }

        private bool ShouldReturnToBaseAfterZombieModePreActiveFailure(ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.ShouldReturnToBaseAfterZombieModePreActiveFailure(reason);
        }

        private void ShowZombieModeStarterChoice(int runId)
        {
            zombieModeRuntimeModule.ShowZombieModeStarterChoice(runId);
        }

        public void SelectZombieModeStarterLoadout(int runId, ZombieModeStarterLoadout loadout)
        {
            zombieModeRuntimeModule.SelectZombieModeStarterLoadout(runId, loadout);
        }

        internal Sprite GetZombieModeStarterIcon(ZombieModeStarterLoadout loadout)
        {
            return zombieModeRuntimeModule.GetZombieModeStarterIcon(loadout);
        }

        internal Sprite GetZombieModeRewardIcon(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.GetZombieModeRewardIcon(rewardType);
        }

        internal Sprite GetZombieModeMerchantIcon(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            return zombieModeRuntimeModule.GetZombieModeMerchantIcon(entry);
        }

        private bool ApplyZombieModeMapIsolationShell(int runId)
        {
            return zombieModeRuntimeModule.ApplyZombieModeMapIsolationShell(runId);
        }

        private void RestoreZombieModeMapIsolationShell()
        {
            zombieModeRuntimeModule.RestoreZombieModeMapIsolationShell();
        }

        private void PrepareSoulCubePrefabCacheForZombieRun()
        {
            zombieModeRuntimeModule.PrepareSoulCubePrefabCacheForZombieRun();
        }

        private bool CreateZombieModePurificationPoint(int runId, Vector3 position, int value)
        {
            return zombieModeRuntimeModule.CreateZombieModePurificationPoint(runId, position, value);
        }

        private bool HasZombieModePendingPurificationStars()
        {
            return zombieModeRuntimeModule.HasZombieModePendingPurificationStars();
        }

        private void ForceCollectZombieModePendingPurificationStars(int runId)
        {
            zombieModeRuntimeModule.ForceCollectZombieModePendingPurificationStars(runId);
        }

        public void CollectZombieModePurificationPoint(int runId, int value, GameObject pointObject, ZombiePurificationStar starRecord)
        {
            zombieModeRuntimeModule.CollectZombieModePurificationPoint(runId, value, pointObject, starRecord);
        }

        public bool ConfigureZombieModePendingCashInvestment(long requestedAmount, out string failureReasonKey)
        {
            return zombieModeRuntimeModule.ConfigureZombieModePendingCashInvestment(requestedAmount, out failureReasonKey);
        }

        public long GetZombieModePendingCashInvestment()
        {
            return zombieModeRuntimeModule.GetZombieModePendingCashInvestment();
        }

        public int PreviewZombieModeInitialPurificationPoints()
        {
            return zombieModeRuntimeModule.PreviewZombieModeInitialPurificationPoints();
        }

        public void ShowZombieModeCashInvestmentPrompt(System.Action onConfirmed, System.Action onCancelled = null)
        {
            zombieModeRuntimeModule.ShowZombieModeCashInvestmentPrompt(onConfirmed, onCancelled);
        }






        // ZombieModeExtractionController.cs
        public bool CanUseZombieModeBeacon()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.CanUseZombieModeBeacon();
        }

        public bool CanUseZombieModePortableSafeZoneDevice()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.CanUseZombieModePortableSafeZoneDevice();
        }

        public string GetZombieModePortableSafeZoneUnavailableReasonKey()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModePortableSafeZoneUnavailableReasonKey() : "BossRush_ZombieMode_Notify_PortableSafeZoneUnavailable";
        }

        public bool TryUseZombieModePortableSafeZoneDevice()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryUseZombieModePortableSafeZoneDevice();
        }

        public string GetZombieModeBeaconUnavailableReasonKey()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeBeaconUnavailableReasonKey() : "BossRush_ZombieMode_Notify_BeaconNotZombieMode";
        }

        public bool TryUseZombieModeBeacon()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryUseZombieModeBeacon();
        }

        public void ContinueZombieModeAfterExtractionOpportunity(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ContinueZombieModeAfterExtractionOpportunity(runId);
        }

        public string StartZombieModeExtractionFromUi(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.StartZombieModeExtractionFromUi(runId) : null;
        }

        private void ShowZombieModeExtractionOpportunityUi(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ShowZombieModeExtractionOpportunityUi(runId);
        }

        private void EnsureZombieModeExtractionArea(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.EnsureZombieModeExtractionArea(runId);
        }

        private void CleanupZombieModePreparationObjects(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CleanupZombieModePreparationObjects(runId);
        }

        private void CancelZombieModeSafeZone(int runId, string reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CancelZombieModeSafeZone(runId, reason);
        }

        private void CreateZombieModeSafeZone(
            int runId,
            bool includeMerchantTerminal = true,
            bool clearBoundServices = true,
            bool portable = false,
            bool useSecondarySlot = false)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CreateZombieModeSafeZone(runId, includeMerchantTerminal, clearBoundServices, portable, useSecondarySlot);
        }

        public void UpdateZombieModeSafeZoneVisual()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.UpdateZombieModeSafeZoneVisual();
        }

        internal Coroutine StartZombieModeCoroutineForRuntimeModule(IEnumerator routine, int runId)
        {
            return StartZombieModeCoroutine(routine, runId);
        }

        internal void StartZombieModeWaveForRuntimeModule(int runId)
        {
            StartZombieModeWave(runId);
        }

        internal void TickZombieModeSafeZoneForRuntimeModule()
        {
            zombieModeRuntimeModule.TickZombieModeSafeZone();
        }

        internal void ReleaseZombieModeSafeZoneThreatSuppressionForRuntimeModule()
        {
            zombieModeRuntimeModule.ReleaseZombieModeSafeZoneThreatSuppression();
        }

        internal void RecycleZombieModeSafeZoneBoundTemporaryNpcsForRuntimeModule(int runId)
        {
            RecycleZombieModeSafeZoneBoundTemporaryNpcs(runId);
        }

        internal void RecycleZombieModeSafeZoneBoundTemporaryRealNpcsForRuntimeModule(int runId)
        {
            RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);
        }

        internal void ClearZombieModeEnemiesInsideActiveSafeZoneForRuntimeModule(int runId, string reason)
        {
            ClearZombieModeEnemiesInsideActiveSafeZone(runId, reason);
        }

        internal void TryRegisterZombieModeShootStealthBreakerForRuntimeModule(int runId)
        {
            zombieModeRuntimeModule.TryRegisterZombieModeShootStealthBreaker(runId);
        }

        internal ZombieModeTemporaryNpc FindZombieModeTemporaryNpcForRuntimeModule(string serviceType)
        {
            return FindZombieModeTemporaryNpc(serviceType);
        }

        internal void SpawnZombieModeTemporaryNpcForRuntimeModule(int runId, string serviceType, bool bossNodeStock)
        {
            SpawnZombieModeTemporaryNpc(runId, serviceType, bossNodeStock);
        }

        // ZombieModeInventoryTransfer.cs
        private bool PrepareZombieModeInventoryTransferShell(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.PrepareZombieModeInventoryTransfer(runId);
        }

        private bool TryMoveZombieModeEntryItemToStorageOrInbox(Item item)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryMoveZombieModeEntryItemToStorageOrInbox(item);
        }

        private void RollbackZombieModeInventoryTransferShell()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RollbackZombieModeInventoryTransfer();
        }

        private List<Item> CollectZombieModeTopLevelPlayerItems()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.CollectZombieModeTopLevelPlayerItems() : new List<Item>();
        }

        private void AddZombieModeTransferCandidate(List<Item> result, Item item)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.AddZombieModeTransferCandidate(result, item);
        }

        // ZombieModeMapSelection.cs
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

        // ZombieModeMapSelection.cs
        // 备用对象仍在宿主构造期创建；状态与 Attach/Detach 协议归模式自己的 owner。
        private readonly ZombieModeHostLifecycle zombieModeHostLifecycle = new ZombieModeHostLifecycle();
        private ZombieModeRuntimeModule zombieModeRuntimeModule { get { return zombieModeHostLifecycle.Runtime; } }
        internal void AttachZombieModeRuntimeModule(ZombieModeRuntimeModule module) { zombieModeHostLifecycle.AttachZombieModeRuntimeModule(module); }
        internal void DetachZombieModeRuntimeModule(ZombieModeRuntimeModule module) { zombieModeHostLifecycle.DetachZombieModeRuntimeModule(module); }
        private ZombieModeRunState zombieModeRunState { get { return zombieModeHostLifecycle.zombieModeRunState; } }
        private ZombieModeEntryTransaction zombieModeEntryTransaction { get { return zombieModeHostLifecycle.zombieModeEntryTransaction; } }
        private Dictionary<string, int[]> zombieModeRewardCandidateCache { get { return zombieModeHostLifecycle.zombieModeRewardCandidateCache; } }
        private List<int> zombieModeRewardSafeCandidateScratch { get { return zombieModeHostLifecycle.zombieModeRewardSafeCandidateScratch; } }
        private HashSet<int> zombieModeOpaqueFilterLogIds { get { return zombieModeHostLifecycle.zombieModeOpaqueFilterLogIds; } }
        private bool pendingZombieModeEntry { get { return zombieModeHostLifecycle.pendingZombieModeEntry; } set { zombieModeHostLifecycle.pendingZombieModeEntry = value; } }
        private int nextZombieModeRunId { get { return zombieModeHostLifecycle.nextZombieModeRunId; } set { zombieModeHostLifecycle.nextZombieModeRunId = value; } }

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


        internal Vector3[] GetZombieModeCachedSpawnerPositionsForRuntimeModule() { return modeECachedSpawnerPositions; }
        internal string GetZombieModeCachedSpawnerSceneNameForRuntimeModule() { return modeECachedSpawnerSceneName; }
        internal void ResetZombieModeOriginalSpawnerStateForRuntimeModule() { spawnersDisabled = false; }
        internal void DisableZombieModeOriginalSpawnersForRuntimeModule() { DisableAllSpawners(); }
        internal void RegisterZombieModeEnemyRecoveryAnchorForRuntimeModule(CharacterMainControl enemy, Vector3 anchor) { RegisterEnemyRecoveryAnchor(enemy, anchor); }

        // ZombieModeRuntimeHooks.cs
        internal void TickZombieModeRuntime(float unscaledDeltaTime)
        {
            TickZombieMode(unscaledDeltaTime);
        }

        internal void LateUpdateZombieModeRuntime()
        {
            ZombieModeUIHelper.EnforceModalInputPause();
        }

        internal void CleanupZombieModeForSceneLoad(Scene scene)
        {
            if (!ShouldPreserveZombieModeStartupForSceneLoad(scene))
            {
                CleanupZombieModeForSceneChange(ZombieModeFailureReason.SceneSwitched);
            }
        }

        internal void CleanupZombieModeOnDestroyRuntime()
        {
            CleanupZombieModeOnDestroy();
            ZombieModeFootMarkerPool.Clear();
            ZombieModeZoneVisuals.ResetStaticCaches();
        }

    }
}

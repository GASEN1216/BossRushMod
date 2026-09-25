using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
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





    }

}

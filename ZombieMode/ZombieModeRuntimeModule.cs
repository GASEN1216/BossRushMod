using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private ZombieModeRunState runState;
        private ZombieModeEntryTransaction entryTransaction;
        private Dictionary<string, int[]> rewardCandidateCache;
        private List<int> rewardCandidateScratch;
        private HashSet<int> opaqueFilterLogIds;
        private bool pendingEntry;
        private float runtimePausedDuration;
        private float runtimePauseStartTime = -1f;
        private int runtimePauseRunId;
        private static int nextRunId;
        private const int InitialPurificationPoints = 0;
        private const int ZombieModePreparationDurationMinimumSeconds = 15;
        private const int ZombieModePreparationDurationMaximumSeconds = 300;
        private static readonly int[] ZombieModePreparationDurationOptions =
        {
            15, 30, 45, 60, 75,
            90, 105, 120, 135, 150,
            165, 180, 195, 210, 225,
            240, 255, 270, 285, 300
        };

        public override string ModuleName
        {
            get { return "ZombieMode"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            // 入场回滚的欠账账本：订阅官方「经济加载完成」，经济一回来就把欠玩家的现金与
            // 邀请函补上（CR-2026-09-11-019）。订阅幂等，OnDestroy 成对退订。
            ZombieModeEntryDebt.Attach();
            if (owner != null) owner.AttachZombieModeRuntimeModule(this);
        }

        internal ZombieModeRunState RunState { get { return runState; } }
        internal ZombieModeEntryTransaction EntryTransaction { get { return entryTransaction; } }
        internal Dictionary<string, int[]> RewardCandidateCache { get { return rewardCandidateCache; } }
        internal List<int> RewardCandidateScratch { get { return rewardCandidateScratch; } }
        internal HashSet<int> OpaqueFilterLogIds { get { return opaqueFilterLogIds; } }
        internal bool PendingEntry { get { return pendingEntry; } set { pendingEntry = value; } }
        internal static int NextRunId { get { return nextRunId; } set { nextRunId = value; } }

        internal void AdoptHostState(
            ZombieModeRunState runState,
            ZombieModeEntryTransaction entryTransaction,
            Dictionary<string, int[]> rewardCandidateCache,
            List<int> rewardCandidateScratch,
            HashSet<int> opaqueFilterLogIds,
            bool pendingEntry)
        {
            this.runState = runState;
            this.entryTransaction = entryTransaction;
            this.rewardCandidateCache = rewardCandidateCache;
            this.rewardCandidateScratch = rewardCandidateScratch;
            this.opaqueFilterLogIds = opaqueFilterLogIds;
            this.pendingEntry = pendingEntry;
        }

        internal void TickZombieMode(float deltaTime)
        {
            if (!ZombieModePhaseGuards.IsRunActive(runState.LifecyclePhase))
            {
                ResetZombieModeRuntimePauseClock();
                return;
            }

            RefreshZombieModeRuntimePauseClock();
            if (IsZombieModeRuntimePaused())
            {
                return;
            }

            owner.TickZombieModeWaveControllerForRuntimeModule(deltaTime);
            TickZombieModeDropsAndPerformance(deltaTime);
            owner.TickZombieModeBossControllerForRuntimeModule(deltaTime);
            TickZombieModeTemporaryNpcProtection();
            owner.UpdateModeFFortificationHighlightsForRuntimeModule();
            owner.UpdateFortPlacementMode();
            owner.UpdateModeFRepairSelection();
        }

        internal bool IsZombieModeGamePaused()
        {
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
            return ZombieModeUIHelper.IsModalInputPaused || IsZombieModeGamePaused() || CameraMode.Active;
        }

        internal void RefreshZombieModeRuntimePauseClock()
        {
            int runId = runState.RunId;
            if (runId <= 0)
            {
                ResetZombieModeRuntimePauseClock();
                return;
            }

            if (runtimePauseRunId != runId)
            {
                runtimePauseRunId = runId;
                runtimePausedDuration = 0f;
                runtimePauseStartTime = -1f;
            }

            if (IsZombieModeRuntimePaused())
            {
                if (runtimePauseStartTime < 0f)
                {
                    runtimePauseStartTime = Time.unscaledTime;
                }
                return;
            }

            if (runtimePauseStartTime >= 0f)
            {
                runtimePausedDuration += Mathf.Max(0f, Time.unscaledTime - runtimePauseStartTime);
                runtimePauseStartTime = -1f;
            }
        }

        internal void ResetZombieModeRuntimePauseClock()
        {
            runtimePauseRunId = 0;
            runtimePausedDuration = 0f;
            runtimePauseStartTime = -1f;
        }

        internal float GetZombieModeRuntimeNow()
        {
            float pausedDuration = runtimePausedDuration;
            if (runtimePauseRunId == runState.RunId && runtimePauseStartTime >= 0f)
            {
                pausedDuration += Mathf.Max(0f, Time.unscaledTime - runtimePauseStartTime);
            }

            return Time.unscaledTime - pausedDuration;
        }

        internal bool TryBeginMapSelectionShell()
        {
            // Keep the Mode H risk gate and the existing public feedback before mutating the entry state.
            try
            {
                if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
                {
                    owner.ShowMessage(L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey()));
                    ModBehaviour.DevLog("[ZombieMode] 地图选择被 Mode H 真实资产风险门拒绝");
                    return false;
                }
            }
            catch
            {
                // Gate lookup failures retain the legacy behavior and allow the entry attempt.
            }

            if (owner.IsAnyBossRushLikeModeActive() || IsZombieModeStartupInProgress())
            {
                return false;
            }

            pendingEntry = true;
            entryTransaction.Reset();
            runState.PendingCashInvestment = 0L;
            runState.ConfirmedCashInvested = 0L;
            runState.LifecyclePhase = ZombieModeLifecyclePhase.SelectingMap;
            return true;
        }

        internal void CancelMapSelectionShell()
        {
            if (runState.LifecyclePhase == ZombieModeLifecyclePhase.SelectingMap)
            {
                runState.LifecyclePhase = ZombieModeLifecyclePhase.None;
            }

            pendingEntry = false;
            runState.PendingCashInvestment = 0L;
            runState.ConfirmedCashInvested = 0L;
            entryTransaction.Reset();
        }

        internal bool IsZombieModeStartupInProgress()
        {
            ZombieModeLifecyclePhase phase = runState.LifecyclePhase;
            return pendingEntry ||
                   (ZombieModePhaseGuards.IsBeforeActive(phase) &&
                    phase != ZombieModeLifecyclePhase.WaitingStarterChoice);
        }

        internal bool CanStartZombieModeMapSelectionPhase1(out string failureReason)
        {
            failureReason = null;
            // Mode G 门控（加法分支）：丧尸模式最终入口额外拒绝 IsModeGEntryBlocked
            // （= RunInProgress || late sink quarantine）；no-throw，未运行时条件恒 false。
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

        internal bool IsZombieModeStartBlocked(out string failureReason)
        {
            failureReason = null;
            if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
            {
                failureReason = L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey());
                return true;
            }
            if (owner.IsAnyBossRushLikeModeActive() || IsZombieModeStartupInProgress())
            {
                failureReason = L10n.T("BossRush_ZombieMode_OtherModeActive");
                return true;
            }
            return false;
        }

        internal bool TryBeginZombieModeMapSelectionPhase1(out string failureReason)
        {
            if (!CanStartZombieModeMapSelectionPhase1(out failureReason))
            {
                return false;
            }

            return TryBeginMapSelectionShell();
        }

        internal void MarkZombieModeMapConfirmedPhase1()
        {
            // 状态机：SelectingMap → Prechecking → CommittingResources → LoadingMap
            if (!pendingEntry)
            {
                return;
            }

            if (runState.LifecyclePhase != ZombieModeLifecyclePhase.SelectingMap)
            {
                return;
            }

            runState.LifecyclePhase = ZombieModeLifecyclePhase.Prechecking;
            ZombieModeFailureReason precheckReason;
            if (!TryRunZombieModePrechecks(out precheckReason))
            {
                FailZombieModeBeforeActive(precheckReason);
                return;
            }

            runState.LifecyclePhase = ZombieModeLifecyclePhase.CommittingResources;
            ZombieModeFailureReason commitReason;
            if (!CommitZombieModeEntryResourcesShell(out commitReason))
            {
                FailZombieModeBeforeActive(commitReason);
                return;
            }

            runState.LifecyclePhase = ZombieModeLifecyclePhase.LoadingMap;
        }

        internal bool IsZombieModeMapLoadReadyPhase1()
        {
            return pendingEntry &&
                   runState.LifecyclePhase == ZombieModeLifecyclePhase.LoadingMap &&
                   ZombieModeMapSelectionHelper.HasPendingZombieEntry;
        }

        internal void AbortZombieModeMapLoadPhase1(ZombieModeFailureReason reason)
        {
            FailZombieModeBeforeActive(reason);
        }

        internal bool TryRunZombieModePrechecks(out ZombieModeFailureReason reason)
        {
            reason = ZombieModeFailureReason.None;
            entryTransaction.BlockingMessages.Clear();

            if (owner.IsActive || owner.IsModeDActive || owner.IsBossRushArenaActive || owner.IsModeEActive || owner.IsModeFActive)
            {
                reason = ZombieModeFailureReason.AnotherBossRushLikeModeActive;
                return false;
            }

            // 邀请函预检（Cost.Enough 在 ZombieModeMapSelectionHelper 已校验，这里再保险一次）
            try
            {
                Duckov.Economy.Cost cost = ZombieModeMapSelectionHelper.CreateZombieModeCost();
                if (!cost.Enough)
                {
                    reason = ZombieModeFailureReason.InvitationMissing;
                    return false;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] Precheck Cost 检查失败: " + e.Message);
                reason = ZombieModeFailureReason.InvitationMissing;
                return false;
            }

            return true;
        }

        internal bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason)
        {
            reason = ZombieModeFailureReason.None;

            // 再扣之前先把上一次入场失败欠下的现金/邀请函还清：这里经济一定可用（正准备扣款），
            // 是除官方「经济加载完成」之外最自然的一个结账点（CR-2026-09-11-019）。
            ZombieModeEntryDebt.TrySettleAll();

            try
            {
                Duckov.Economy.Cost invitationCost = ZombieModeMapSelectionHelper.CreateZombieModeCost();
                if (!Duckov.Economy.EconomyManager.IsEnough(invitationCost, true, true))
                {
                    reason = ZombieModeFailureReason.InvitationMissing;
                    return false;
                }

                if (!Duckov.Economy.EconomyManager.Pay(invitationCost, true, true))
                {
                    reason = ZombieModeFailureReason.InvitationConsumeFailed;
                    return false;
                }

                entryTransaction.InvitationTemporarilyHeld = true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 邀请函扣除失败: " + e.Message);
                reason = ZombieModeFailureReason.InvitationConsumeFailed;
                return false;
            }

            long pendingCash = runState.PendingCashInvestment;
            if (pendingCash > 0L)
            {
                try
                {
                    Duckov.Economy.Cost cashCost = new Duckov.Economy.Cost();
                    cashCost.money = pendingCash;
                    if (!Duckov.Economy.EconomyManager.IsEnough(cashCost, true, true))
                    {
                        reason = ZombieModeFailureReason.NotEnoughCash;
                        return false;
                    }

                    if (!Duckov.Economy.EconomyManager.Pay(cashCost, true, true))
                    {
                        reason = ZombieModeFailureReason.CashWithdrawFailed;
                        return false;
                    }

                    entryTransaction.CashTemporarilyHeld = true;
                    entryTransaction.CashWithheldAmount = pendingCash;
                    runState.ConfirmedCashInvested = pendingCash;
                }
                catch (System.Exception e)
                {
                    ModBehaviour.DevLog("[ZombieMode] 现金扣款失败: " + e.Message);
                    reason = ZombieModeFailureReason.CashWithdrawFailed;
                    return false;
                }
            }

            return true;
        }

        internal void RefundZombieModeCashIfNeeded()
        {
            if (!ShouldRollbackZombieModeEntryResources() || !entryTransaction.CashTemporarilyHeld) return;
            if (!ZombieModeEntryDebt.RefundCash(entryTransaction.CashWithheldAmount)) return;
            entryTransaction.CashTemporarilyHeld = false;
            entryTransaction.CashWithheldAmount = 0L;
            runState.ConfirmedCashInvested = 0L;
        }

        internal void CancelZombieModeMapSelectionPhase1()
        {
            CancelMapSelectionShell();
        }

        internal bool ShouldPreserveZombieModeStartupForSceneLoad(Scene scene)
        {
            if (!IsZombieModeStartupInProgress() || !ZombieModeMapSelectionHelper.HasPendingZombieEntry)
            {
                return false;
            }

            string targetSubScene = ZombieModeMapSelectionHelper.GetPendingTargetSubSceneName();
            string targetMainScene = ZombieModeMapSelectionHelper.GetPendingMainSceneName();
            if (scene.name.Contains("Loading") || scene.name.Contains("Menu"))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(targetSubScene) && scene.name == targetSubScene)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(targetMainScene) && scene.name == targetMainScene)
            {
                return true;
            }

            if (targetSubScene == "Level_StormZone_B0" && scene.name == "Level_StormZone_1")
            {
                return true;
            }

            if (targetSubScene == "Level_SnowMilitaryBase_ColdStorage" && scene.name == "Level_SnowMilitaryBase")
            {
                return true;
            }

            return false;
        }

        internal bool TryHandleZombieModePendingMapSceneLoaded(Scene scene, BossRushMapConfig loadedMapConfig)
        {
            if (!ZombieModeMapSelectionHelper.HasPendingZombieEntry)
            {
                return false;
            }

            string targetSubScene = ZombieModeMapSelectionHelper.GetPendingTargetSubSceneName();
            string targetMainScene = ZombieModeMapSelectionHelper.GetPendingMainSceneName();
            Vector3? customPos = ZombieModeMapSelectionHelper.GetPendingCustomPosition();

            if (scene.name.Contains("Loading") || scene.name.Contains("Menu") ||
                (!string.IsNullOrEmpty(targetMainScene) && scene.name == targetMainScene))
            {
                ModBehaviour.DevLog("[ZombieMode] 检测到中间场景: " + scene.name + ", 保持 Phase 1 地图进入状态");
                return true;
            }

            if (!string.IsNullOrEmpty(targetSubScene) && scene.name == targetSubScene)
            {
                ZombieModeMapSelectionHelper.MarkTargetSceneLoadStarted();
                owner.StartCoroutine(WaitForZombieModeTargetSceneActiveThenInitialize(scene, customPos));
                return true;
            }

            if (targetSubScene == "Level_StormZone_B0" && scene.name == "Level_StormZone_1" && customPos.HasValue)
            {
                ModBehaviour.DevLog("[ZombieMode] 检测到风暴区地上场景，转入目标地下子场景");
                owner.StartCoroutine(owner.ForceTeleportToSubSceneForRuntimeModule(targetSubScene, customPos.Value));
                return true;
            }

            if (targetSubScene == "Level_SnowMilitaryBase_ColdStorage" && scene.name == "Level_SnowMilitaryBase" && customPos.HasValue)
            {
                ModBehaviour.DevLog("[ZombieMode] 检测到雪地军事基地主场景，转入冷藏区子场景");
                owner.StartCoroutine(owner.ForceTeleportToSubSceneForRuntimeModule(targetSubScene, customPos.Value));
                return true;
            }

            ModBehaviour.DevLog("[ZombieMode] 非目标场景: " + scene.name + "，取消 Phase 1 待处理进入状态");
            RefundZombieModeInvitationIfNeeded();
            RefundZombieModeCashIfNeeded();
            CancelZombieModeMapSelectionPhase1();
            ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
            return false;
        }

        internal System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(Scene scene, Vector3? customPos)
        {
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxWait)
            {
                attempt++;
                Scene activeScene = SceneManager.GetActiveScene();
                bool sceneLoaded = scene.isLoaded;
                bool activeMatches = activeScene.name == scene.name;
                bool sceneLoaderDone = owner.ReadSceneLoaderDoneWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");
                bool levelInited = owner.ReadLevelInitedWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");
                // 官方 InitLevel 置 levelInited 后还会等 0.25 秒再 SetPosition(startPos)、置 afterInit；
                // 在这之前初始化会拿到未定位的玩家，失败回基地还会让官方 SetPosition 打到已销毁的角色。
                bool levelAfterInit = IsOfficialLevelAfterInitForZombieMode();

                if (sceneLoaded && activeMatches && sceneLoaderDone && levelInited && levelAfterInit)
                {
                    break;
                }

                if (attempt % 10 == 0)
                {
                    ModBehaviour.DevLog("[ZombieMode] 等待目标地图激活: target=" + scene.name
                        + ", active=" + activeScene.name
                        + ", sceneLoaded=" + sceneLoaded
                        + ", sceneLoaderDone=" + sceneLoaderDone
                        + ", levelInited=" + levelInited
                        + ", afterInit=" + levelAfterInit
                        + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            Scene finalActiveScene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || finalActiveScene.name != scene.name)
            {
                ModBehaviour.DevLog("[ZombieMode] 初始化失败：目标地图未成为 ActiveScene, target=" + scene.name + ", active=" + finalActiveScene.name);
                ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
                FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                yield break;
            }

            int runId = BeginZombieModeRunShell(scene.buildIndex, scene.name);
            // 状态机推进：LoadingMap → InitializingRun（WaitingStarterChoice 由 InitializeZombieModeRunAfterMapLoaded 末尾设置）
            runState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            runState.CombatPhase = ZombieModeCombatPhase.None;
            runState.PurificationPoints = InitialPurificationPoints;
            if (runState.ConfirmedCashInvested > 0L)
            {
                // 100 现金 = 1 净化点数（向下取整）
                runState.PurificationPoints = (int)System.Math.Min(
                    int.MaxValue,
                    runState.ConfirmedCashInvested / ZombieModeTuning.CashToPurificationRatio);
            }

            ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
            if (!InitializeZombieModeRunAfterMapLoaded(runId))
            {
                FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                yield break;
            }

            ModBehaviour.DevLog("[ZombieMode] 已进入目标地图: " + scene.name + "，等待初始流派选择");
        }

        internal int BeginZombieModeRunShell(int sceneBuildIndex, string sceneName)
        {
            int runId = ++nextRunId;
            long pendingCashInvestment = runState.PendingCashInvestment;
            long confirmedCashInvested = runState.ConfirmedCashInvested;
            if (confirmedCashInvested <= 0L && entryTransaction.CashTemporarilyHeld)
            {
                confirmedCashInvested = entryTransaction.CashWithheldAmount;
            }
            runState.ResetForNewRun(runId, sceneBuildIndex, sceneName);
            runState.PendingCashInvestment = pendingCashInvestment;
            runState.ConfirmedCashInvested = confirmedCashInvested;
            runState.MapProfile = BuildZombieModeMapProfile(sceneBuildIndex, sceneName);
            pendingEntry = false;
            return runId;
        }

        internal ZombieModeMapProfile BuildZombieModeMapProfile(int sceneBuildIndex, string sceneName)
        {
            ZombieModeMapProfile profile = new ZombieModeMapProfile();
            profile.SceneId = sceneBuildIndex;
            profile.SceneName = sceneName ?? string.Empty;

            BossRushMapConfig mapConfig = ModBehaviour.GetCurrentMapConfig();
            if (mapConfig != null)
            {
                int parsedSceneId;
                if (int.TryParse(mapConfig.sceneID, out parsedSceneId))
                {
                    profile.SceneId = parsedSceneId;
                }

                profile.SceneName = mapConfig.sceneName ?? profile.SceneName;
                profile.MainSceneName = mapConfig.sceneID == mapConfig.sceneName ? string.Empty : (mapConfig.sceneID ?? string.Empty);
                profile.DisplayName = mapConfig.displayName ?? string.Empty;
                profile.StaticSpawnPoints = mapConfig.modeESpawnPoints != null && mapConfig.modeESpawnPoints.Length > 0
                    ? mapConfig.modeESpawnPoints
                    : (mapConfig.spawnPoints ?? new Vector3[0]);
                profile.CustomSpawnPos = mapConfig.customSpawnPos;
            }

            return profile;
        }

        internal bool IsZombieModeRunValid(int runId)
        {
            if (runId <= 0 || runState.RunId != runId || runState.IsCleaningUp)
            {
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (runState.SceneBuildIndex >= 0 && scene.buildIndex != runState.SceneBuildIndex)
            {
                return false;
            }

            ZombieModeLifecyclePhase phase = runState.LifecyclePhase;
            return phase == ZombieModeLifecyclePhase.InitializingRun ||
                   phase == ZombieModeLifecyclePhase.WaitingStarterChoice ||
                   ZombieModePhaseGuards.IsActive(phase);
        }

        internal int GetZombieModeSelectedPreparationDuration(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return Mathf.RoundToInt(ZombieModeTuning.PreparationCountdownSeconds);
            }

            if (runState.SelectedPreparationDurationSeconds > 0)
            {
                return Mathf.Clamp(
                    runState.SelectedPreparationDurationSeconds,
                    ZombieModePreparationDurationMinimumSeconds,
                    ZombieModePreparationDurationMaximumSeconds);
            }

            return Mathf.RoundToInt(ZombieModeTuning.PreparationCountdownSeconds);
        }

        internal void OpenZombieModePreparationDurationEditor(int runId)
        {
            if (!IsZombieModeRunValid(runId) ||
                runState.CombatPhase != ZombieModeCombatPhase.RewardSelection ||
                runState.CurrentRewardNode == null)
            {
                return;
            }

            owner.ShowZombieModeRewardSelectionForRuntimeModule(runId, runState.CurrentRewardNode.BossNode, true);
        }

        internal void SetZombieModePreparationDuration(int runId, int seconds)
        {
            if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.RewardSelection)
            {
                return;
            }

            for (int i = 0; i < ZombieModePreparationDurationOptions.Length; i++)
            {
                if (ZombieModePreparationDurationOptions[i] != seconds)
                {
                    continue;
                }

                runState.SelectedPreparationDurationSeconds = seconds;
                bool bossNode = runState.CurrentRewardNode != null && runState.CurrentRewardNode.BossNode;
                owner.ShowZombieModeRewardSelectionForRuntimeModule(runId, bossNode, false);
                return;
            }
        }

        internal void RegisterZombieModeRunOnlyObject(int runId, ZombieModeRunOnlyObjectKind kind, GameObject gameObject, UnityEngine.Object target, Action cleanupAction)
        {
            if (runId <= 0 || runId != runState.RunId)
            {
                return;
            }

            if (kind == ZombieModeRunOnlyObjectKind.RewardUi)
            {
                for (int i = runState.RunOnlyObjects.Count; i-- > 0;)
                {
                    ZombieModeRunOnlyRecord existing = runState.RunOnlyObjects[i];
                    if (existing != null &&
                        existing.RunId == runId &&
                        existing.Kind == ZombieModeRunOnlyObjectKind.RewardUi &&
                        existing.GameObject == null &&
                        existing.CleanupAction == null)
                    {
                        runState.RunOnlyObjects.RemoveAt(i);
                    }
                }
            }
            else if (kind == ZombieModeRunOnlyObjectKind.Projectile)
            {
                // 毒径每秒两段、远程骚扰每次出手一条：局内只清敌人类记录时，自毁完的地面区 / 弹道
                // 记录会整局累积，被击杀清理、恢复监控和光环逐条遍历。登记新一条时顺手摘掉已销毁的。
                for (int i = runState.RunOnlyObjects.Count; i-- > 0;)
                {
                    ZombieModeRunOnlyRecord existing = runState.RunOnlyObjects[i];
                    if (existing == null ||
                        (existing.Kind == ZombieModeRunOnlyObjectKind.Projectile &&
                         existing.GameObject == null &&
                         existing.Target == null))
                    {
                        if (existing != null)
                        {
                            try
                            {
                                existing.Cleanup(false);
                            }
                            catch (System.Exception e)
                            {
                                ModBehaviour.DevLog("[ZombieMode] Run-only projectile prune failed: " + e.Message);
                            }
                        }
                        runState.RunOnlyObjects.RemoveAt(i);
                    }
                }
            }

            ZombieModeRunOnlyRecord record = new ZombieModeRunOnlyRecord();
            record.RunId = runId;
            record.Kind = kind;
            record.GameObject = gameObject;
            record.Target = target;
            record.CleanupAction = cleanupAction;
            runState.RunOnlyObjects.Add(record);
        }

        internal void PruneZombieModeRunOnlyEnemyRecords(int runId)
        {
            if (runId <= 0 || runState == null || runState.RunOnlyObjects.Count <= 0)
            {
                return;
            }

            for (int i = runState.RunOnlyObjects.Count; i-- > 0;)
            {
                ZombieModeRunOnlyRecord record = runState.RunOnlyObjects[i];
                if (record == null)
                {
                    runState.RunOnlyObjects.RemoveAt(i);
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
                    ModBehaviour.DevLog("[ZombieMode] Run-only enemy prune failed: " + e.Message);
                }

                runState.RunOnlyObjects.RemoveAt(i);
            }
        }

        internal void RemoveZombieModeRunOnlyObjectRecord(UnityEngine.Object target)
        {
            if (target == null || runState == null || runState.RunOnlyObjects == null)
            {
                return;
            }

            for (int i = runState.RunOnlyObjects.Count; i-- > 0;)
            {
                ZombieModeRunOnlyRecord record = runState.RunOnlyObjects[i];
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
                    ModBehaviour.DevLog("[ZombieMode] Run-only object record cleanup failed: " + e.Message);
                }
                runState.RunOnlyObjects.RemoveAt(i);
            }
        }

        internal void PruneZombieModeUnknownRunOnlyRecords()
        {
            if (runState == null || runState.RunOnlyObjects == null)
            {
                return;
            }

            for (int i = runState.RunOnlyObjects.Count; i-- > 0;)
            {
                ZombieModeRunOnlyRecord record = runState.RunOnlyObjects[i];
                if (record == null)
                {
                    runState.RunOnlyObjects.RemoveAt(i);
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
                    ModBehaviour.DevLog("[ZombieMode] prune run-only record cleanup 失败: " + e.Message);
                }
                runState.RunOnlyObjects.RemoveAt(i);
            }
        }

        internal void InvalidateZombieModeRun()
        {
            nextRunId++;
            if (runState.RunId > 0)
            {
                runState.RunId = -runState.RunId;
            }
            runState.IsCleaningUp = true;
        }

        internal bool ShouldSettleZombieModeFailureInsurance(ZombieModeFailureReason reason)
        {
            return reason != ZombieModeFailureReason.SuccessfulExtraction &&
                   (reason == ZombieModeFailureReason.PlayerDeath ||
                    reason == ZombieModeFailureReason.ManualExit ||
                    reason == ZombieModeFailureReason.SceneSwitched ||
                    reason == ZombieModeFailureReason.UnexpectedSceneUnload ||
                    reason == ZombieModeFailureReason.Unknown);
        }

        internal void CleanupZombieModeRunOnlyState(ZombieModeFailureReason reason, bool destroyGameObjects)
        {
            if (ShouldSettleZombieModeFailureInsurance(reason))
            {
                SettleZombieModeFailureInsuranceShell(runState.RunId);
            }

            RemoveZombieModeAttributeModifiers();
            RemoveZombieModeOptionRuntimeEffects();
            owner.CleanupZombieModeFortificationInteractionStateForRuntimeModule();
            InvalidateZombieModeRun();
            owner.ClearZombieModeSupportSpawnQueueForRuntimeModule();

            // 整表 Destroy 前先补一次拾取所有权扫描：常规扫描每秒一次，撤离那一刻
            // 1 秒内刚捡进背包的掉落还登记在 RunOnlyObjects 里，会被下面一并销毁。
            if (destroyGameObjects)
            {
                ReleaseZombieModeOwnedDropCandidates();
            }

            RunScopedRegistry.ForEachReverse(
                runState.RunOnlyObjects,
                record => record.Cleanup(destroyGameObjects),
                (e, record) => ModBehaviour.DevLog("[ZombieMode] Run-only cleanup failed: " + reason.ToString() + " - " + e.Message));

            runState.RunOnlyObjects.Clear();
            // OnHurt/OnDead hot path 集合也在局结束时清掉（审查 §3.1）。
            owner.ClearZombieModeEnemyInstanceIdsForRuntimeModule();
            ClearZombieModeRewardShell();
            RestoreZombieModeMapIsolationShell();
        }

        internal bool ShouldRollbackZombieModeEntryResources()
        {
            return !runState.EntryResourcesFinalized && !entryTransaction.EntryResourcesFinalized;
        }

        internal bool InitializeZombieModeRunAfterMapLoaded(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            runState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            PrepareSoulCubePrefabCacheForZombieRun();
            if (!PrepareZombieModeInventoryTransfer(runId))
            {
                return false;
            }

            if (!CollectZombieModeSpawnPoints(runId))
            {
                ModBehaviour.DevLog("[ZombieMode] 初始化失败：未收集到有效刷怪点");
                return false;
            }

            if (!ApplyZombieModeMapIsolationShell(runId))
            {
                return false;
            }

            if (!owner.EnsureZombieModeCharacterPresetForRuntimeModule())
            {
                ModBehaviour.DevLog("[ZombieMode] 初始化失败：缺少 Cname_Zombie 预设");
                return false;
            }

            if (!InitializeZombieModeContainersShell(runId))
            {
                return false;
            }

            if (!GrantZombieModeBeacon(runId))
            {
                return false;
            }

            owner.RegisterZombieModeEventListenersForRuntimeModule(runId);
            owner.CreateZombieModeHudForRuntimeModule(runId);
            runState.LifecyclePhase = ZombieModeLifecyclePhase.WaitingStarterChoice;
            ShowZombieModeStarterChoice(runId);
            return true;
        }

        internal bool GrantZombieModeBeacon(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            try
            {
                ZombieTideBeaconConfig.EnsureRuntimeFallbackRegistrationShell();
                Item beacon = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ZombieTideBeacon);
                if (beacon == null)
                {
                    return false;
                }

                ItemUtilities.SendToPlayer(beacon, true, false);
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 发放尸潮信标失败: " + e.Message);
                return false;
            }
        }

        internal void FinalizeZombieModeEntryResources()
        {
            runState.ZombieModeResourcesCommitted = true;
            runState.EntryResourcesFinalized = true;
            entryTransaction.EntryResourcesFinalized = true;
            entryTransaction.InvitationTemporarilyHeld = false;
        }

        internal void RefundZombieModeInvitationIfNeeded()
        {
            if (!ShouldRollbackZombieModeEntryResources() || !entryTransaction.InvitationTemporarilyHeld) return;
            if (!ZombieModeEntryDebt.RefundInvitation()) return;
            entryTransaction.InvitationTemporarilyHeld = false;
        }

        internal void FailZombieModeBeforeActive(ZombieModeFailureReason reason)
        {
            ModBehaviour.DevLog("[ZombieMode] Fail before Active: " + reason.ToString());
            bool shouldReturnToBase = ShouldReturnToBaseAfterZombieModePreActiveFailure(reason);
            RefundZombieModeInvitationIfNeeded();
            RefundZombieModeCashIfNeeded();
            owner.CleanupZombieModeForRuntimeModule(reason);
            if (!shouldReturnToBase)
            {
                return;
            }

            // 官方关卡初始化（InitLevel）还在跑时切场景会让它在 SetPosition 上 NRE、DOTween 报目标丢失；
            // 等 afterInit 或关卡已不存在后再 LoadBaseScene。
            try
            {
                owner.StartCoroutine(LoadBaseSceneAfterOfficialLevelInitialized());
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] [WARNING] Entry 失败回主场景调度失败: " + e.Message);
            }
        }

        internal static bool IsOfficialLevelAfterInitForZombieMode()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.AfterInit;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsOfficialLevelSafeToLeaveForZombieMode()
        {
            try
            {
                if (SceneLoader.IsSceneLoading)
                {
                    return false;
                }

                return LevelManager.Instance == null || LevelManager.AfterInit;
            }
            catch
            {
                return true;
            }
        }

        private System.Collections.IEnumerator LoadBaseSceneAfterOfficialLevelInitialized()
        {
            const float maxWaitSeconds = 30f;
            float waited = 0f;
            while (waited < maxWaitSeconds && !IsOfficialLevelSafeToLeaveForZombieMode())
            {
                yield return new WaitForSecondsRealtime(0.1f);
                waited += 0.1f;
            }

            try
            {
                if (SceneLoader.Instance != null)
                {
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] [WARNING] Entry 失败回主场景失败: " + e.Message);
            }
        }

        internal bool ShouldReturnToBaseAfterZombieModePreActiveFailure(ZombieModeFailureReason reason)
        {
            ZombieModeLifecyclePhase phase = runState.LifecyclePhase;
            if (phase == ZombieModeLifecyclePhase.Prechecking ||
                phase == ZombieModeLifecyclePhase.CommittingResources ||
                phase == ZombieModeLifecyclePhase.LoadingMap)
            {
                return false;
            }

            if (phase == ZombieModeLifecyclePhase.InitializingRun ||
                phase == ZombieModeLifecyclePhase.WaitingStarterChoice)
            {
                return true;
            }

            return false;
        }

        // 源码的爆炸复用同一份碰撞与去重缓冲；Hurt / Dead 回调只安排下一帧。
        // 每次请求使用已有 RunOnlyObjects，结束立刻移除，不让高频暴击留下整局协程记录。
        internal static void DeferExplosion(ModBehaviour owner, ZombieModeRunState run, int runId,
            Func<bool> valid, Action apply)
        {
            if (owner == null || run == null || valid == null || !valid()) return;
            CharacterMainControl player = CharacterMainControl.Main;
            LevelManager level = LevelManager.Instance;
            var record = new ZombieModeRunOnlyRecord { RunId = runId, Kind = ZombieModeRunOnlyObjectKind.Coroutine };
            Coroutine pending = null;
            record.CleanupAction = () =>
            {
                if (owner != null && pending != null) owner.StopCoroutine(pending);
                record.CleanupAction = null;
                run.RunOnlyObjects.Remove(record);
            };
            run.RunOnlyObjects.Add(record);
            try
            {
                pending = owner.StartCoroutine(ExplosionNextFrame(owner, run, record, player, level, valid, apply));
            }
            catch (Exception e)
            {
                record.CleanupAction = null;
                run.RunOnlyObjects.Remove(record);
                ModBehaviour.DevLog("[ZombieMode] explosion scheduling failed: " + e.Message);
            }
        }

        private static IEnumerator ExplosionNextFrame(ModBehaviour owner, ZombieModeRunState run,
            ZombieModeRunOnlyRecord record, CharacterMainControl player, LevelManager level,
            Func<bool> valid, Action apply)
        {
            yield return null;
            try
            {
                while (owner != null && record.CleanupAction != null && valid()
                    && player != null && ReferenceEquals(player, CharacterMainControl.Main)
                    && player.Health != null && !player.Health.IsDead
                    && ReferenceEquals(level, LevelManager.Instance))
                {
                    if (!owner.IsZombieModeRuntimePaused())
                    {
                        apply();
                        yield break;
                    }
                    yield return null;
                }
            }
            finally
            {
                record.CleanupAction = null;
                run.RunOnlyObjects.Remove(record);
            }
        }

        internal static void TriggerDoomPulse(CharacterMainControl player, int stacks,
            Action<Vector3, float, float> explode)
        {
            Vector3 center = player.transform.position;
            Vector3 forward = player.transform.forward;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            float radius = 2.75f;
            float damage = 30f + 10f * (stacks - 1);
            float offsetDistance = 1.5f + 0.25f * (stacks - 1);
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 120f * i, 0f) * forward * offsetDistance;
                explode(center + offset, radius, damage);
            }
        }

        public override void OnDestroy()
        {
            ZombieModeEntryDebt.ResetStaticCaches();
            ZombieModeBossVisuals.ResetStaticCaches();
            ModBehaviour currentOwner = owner;
            owner = null;
            if (currentOwner != null) currentOwner.DetachZombieModeRuntimeModule(this);
        }
    }


}

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

        private readonly ZombieModeRunState zombieModeRunState = new ZombieModeRunState();
        private readonly ZombieModeEntryTransaction zombieModeEntryTransaction = new ZombieModeEntryTransaction();
        private readonly Dictionary<string, int[]> zombieModeRewardCandidateCache = new Dictionary<string, int[]>();
        private readonly List<int> zombieModeRewardSafeCandidateScratch = new List<int>();
        private static int nextZombieModeRunId = 0;
        private bool pendingZombieModeEntry = false;

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
            return IsActive || IsModeDActive || IsBossRushArenaActive || IsModeEActive || IsModeFActive || IsZombieModeActive;
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
            ZombieModeLifecyclePhase phase = zombieModeRunState.LifecyclePhase;
            return pendingZombieModeEntry ||
                   (ZombieModePhaseGuards.IsBeforeActive(phase) &&
                    phase != ZombieModeLifecyclePhase.WaitingStarterChoice);
        }

        public bool CanStartZombieModeMapSelectionPhase1(out string failureReason)
        {
            failureReason = null;
            if (IsAnyBossRushLikeModeActive() || IsZombieModeStartupInProgress())
            {
                failureReason = L10n.T("BossRush_ZombieMode_OtherModeActive");
                return false;
            }

            return true;
        }

        public bool TryBeginZombieModeMapSelectionPhase1(out string failureReason)
        {
            if (!CanStartZombieModeMapSelectionPhase1(out failureReason))
            {
                return false;
            }

            return TryBeginZombieModeMapSelectionShell();
        }

        public void MarkZombieModeMapConfirmedPhase1()
        {
            // 状态机：SelectingMap → Prechecking → CommittingResources → LoadingMap
            if (!pendingZombieModeEntry)
            {
                return;
            }

            if (zombieModeRunState.LifecyclePhase != ZombieModeLifecyclePhase.SelectingMap)
            {
                return;
            }

            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.Prechecking;
            ZombieModeFailureReason precheckReason;
            if (!TryRunZombieModePrechecks(out precheckReason))
            {
                FailZombieModeBeforeActive(precheckReason);
                return;
            }

            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.CommittingResources;
            ZombieModeFailureReason commitReason;
            if (!CommitZombieModeEntryResourcesShell(out commitReason))
            {
                FailZombieModeBeforeActive(commitReason);
                return;
            }

            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.LoadingMap;
        }

        public bool IsZombieModeMapLoadReadyPhase1()
        {
            return pendingZombieModeEntry &&
                   zombieModeRunState.LifecyclePhase == ZombieModeLifecyclePhase.LoadingMap &&
                   ZombieModeMapSelectionHelper.HasPendingZombieEntry;
        }

        public void AbortZombieModeMapLoadPhase1(ZombieModeFailureReason reason)
        {
            FailZombieModeBeforeActive(reason);
        }

        // 入场预检查（邀请函/其他模式互斥）。随身物品不阻止入场，入图后统一转入仓库。
        private bool TryRunZombieModePrechecks(out ZombieModeFailureReason reason)
        {
            reason = ZombieModeFailureReason.None;
            zombieModeEntryTransaction.BlockingMessages.Clear();

            if (IsActive || IsModeDActive || IsBossRushArenaActive || IsModeEActive || IsModeFActive)
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
                DevLog("[ZombieMode] Precheck Cost 检查失败: " + e.Message);
                reason = ZombieModeFailureReason.InvitationMissing;
                return false;
            }

            return true;
        }

        // 资源暂扣：丧尸模式自行提交邀请函与现金，地图选择条目只借用原版 UI 外观。
        private bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason)
        {
            reason = ZombieModeFailureReason.None;

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

                zombieModeEntryTransaction.InvitationTemporarilyHeld = true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 邀请函扣除失败: " + e.Message);
                reason = ZombieModeFailureReason.InvitationConsumeFailed;
                return false;
            }

            long pendingCash = zombieModeRunState.PendingCashInvestment;
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

                    zombieModeEntryTransaction.CashTemporarilyHeld = true;
                    zombieModeEntryTransaction.CashWithheldAmount = pendingCash;
                    zombieModeRunState.ConfirmedCashInvested = pendingCash;
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] 现金扣款失败: " + e.Message);
                    reason = ZombieModeFailureReason.CashWithdrawFailed;
                    return false;
                }
            }

            return true;
        }

        private void RefundZombieModeCashIfNeeded()
        {
            if (!ShouldRollbackZombieModeEntryResources() || !zombieModeEntryTransaction.CashTemporarilyHeld)
            {
                return;
            }

            try
            {
                long amount = zombieModeEntryTransaction.CashWithheldAmount;
                if (amount > 0L)
                {
                    Duckov.Economy.EconomyManager.Add(amount);
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefundedCash"));
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 退还现金失败: " + e.Message);
            }
            finally
            {
                zombieModeEntryTransaction.CashTemporarilyHeld = false;
                zombieModeEntryTransaction.CashWithheldAmount = 0L;
                zombieModeRunState.ConfirmedCashInvested = 0L;
            }
        }

        public void CancelZombieModeMapSelectionPhase1()
        {
            CancelZombieModeMapSelectionShell();
        }

        private bool ShouldPreserveZombieModeStartupForSceneLoad(Scene scene)
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

        private bool TryHandleZombieModePendingMapSceneLoaded(Scene scene, BossRushMapConfig loadedMapConfig)
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
                DevLog("[ZombieMode] 检测到中间场景: " + scene.name + ", 保持 Phase 1 地图进入状态");
                return true;
            }

            if (!string.IsNullOrEmpty(targetSubScene) && scene.name == targetSubScene)
            {
                ZombieModeMapSelectionHelper.MarkTargetSceneLoadStarted();
                StartCoroutine(WaitForZombieModeTargetSceneActiveThenInitialize(scene, customPos));
                return true;
            }

            if (targetSubScene == "Level_StormZone_B0" && scene.name == "Level_StormZone_1" && customPos.HasValue)
            {
                DevLog("[ZombieMode] 检测到风暴区地上场景，转入目标地下子场景");
                StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                return true;
            }

            if (targetSubScene == "Level_SnowMilitaryBase_ColdStorage" && scene.name == "Level_SnowMilitaryBase" && customPos.HasValue)
            {
                DevLog("[ZombieMode] 检测到雪地军事基地主场景，转入冷藏区子场景");
                StartCoroutine(ForceTeleportToSubScene(targetSubScene, customPos.Value));
                return true;
            }

            DevLog("[ZombieMode] 非目标场景: " + scene.name + "，取消 Phase 1 待处理进入状态");
            RefundZombieModeInvitationIfNeeded();
            RefundZombieModeCashIfNeeded();
            CancelZombieModeMapSelectionPhase1();
            ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
            return false;
        }

        private System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(Scene scene, Vector3? customPos)
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
                bool sceneLoaderDone = ReadSceneLoaderDoneWithWarning("ZombieModeTargetSceneInitialize");
                bool levelInited = ReadLevelInitedWithWarning("ZombieModeTargetSceneInitialize");

                if (sceneLoaded && activeMatches && sceneLoaderDone && levelInited)
                {
                    break;
                }

                if (attempt % 10 == 0)
                {
                    DevLog("[ZombieMode] 等待目标地图激活: target=" + scene.name
                        + ", active=" + activeScene.name
                        + ", sceneLoaded=" + sceneLoaded
                        + ", sceneLoaderDone=" + sceneLoaderDone
                        + ", levelInited=" + levelInited
                        + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            Scene finalActiveScene = SceneManager.GetActiveScene();
            if (!scene.isLoaded || finalActiveScene.name != scene.name)
            {
                DevLog("[ZombieMode] 初始化失败：目标地图未成为 ActiveScene, target=" + scene.name + ", active=" + finalActiveScene.name);
                ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
                FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                yield break;
            }

            int runId = BeginZombieModeRunShell(scene.buildIndex, scene.name);
            // 状态机推进：LoadingMap → InitializingRun（WaitingStarterChoice 由 InitializeZombieModeRunAfterMapLoaded 末尾设置）
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.None;
            zombieModeRunState.PurificationPoints = ZOMBIE_MODE_INITIAL_PURIFICATION_POINTS;
            if (zombieModeRunState.ConfirmedCashInvested > 0L)
            {
                // 100 现金 = 1 净化点数（向下取整）
                zombieModeRunState.PurificationPoints = (int)System.Math.Min(
                    int.MaxValue,
                    zombieModeRunState.ConfirmedCashInvested / ZombieModeTuning.CashToPurificationRatio);
            }

            ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
            if (!InitializeZombieModeRunAfterMapLoaded(runId))
            {
                FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                yield break;
            }

            DevLog("[ZombieMode] 已进入目标地图: " + scene.name + "，等待初始流派选择");
        }

        private int BeginZombieModeRunShell(int sceneBuildIndex, string sceneName)
        {
            int runId = ++nextZombieModeRunId;
            long pendingCashInvestment = zombieModeRunState.PendingCashInvestment;
            long confirmedCashInvested = zombieModeRunState.ConfirmedCashInvested;
            if (confirmedCashInvested <= 0L && zombieModeEntryTransaction.CashTemporarilyHeld)
            {
                confirmedCashInvested = zombieModeEntryTransaction.CashWithheldAmount;
            }
            zombieModeRunState.ResetForNewRun(runId, sceneBuildIndex, sceneName);
            zombieModeRunState.PendingCashInvestment = pendingCashInvestment;
            zombieModeRunState.ConfirmedCashInvested = confirmedCashInvested;
            zombieModeRunState.MapProfile = BuildZombieModeMapProfile(sceneBuildIndex, sceneName);
            pendingZombieModeEntry = false;
            return runId;
        }

        private ZombieModeMapProfile BuildZombieModeMapProfile(int sceneBuildIndex, string sceneName)
        {
            ZombieModeMapProfile profile = new ZombieModeMapProfile();
            profile.SceneId = sceneBuildIndex;
            profile.SceneName = sceneName ?? string.Empty;

            BossRushMapConfig mapConfig = GetCurrentMapConfig();
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

        private bool IsZombieModeRunValid(int runId)
        {
            if (runId <= 0 || zombieModeRunState.RunId != runId || zombieModeRunState.IsCleaningUp)
            {
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (zombieModeRunState.SceneBuildIndex >= 0 && scene.buildIndex != zombieModeRunState.SceneBuildIndex)
            {
                return false;
            }

            ZombieModeLifecyclePhase phase = zombieModeRunState.LifecyclePhase;
            return phase == ZombieModeLifecyclePhase.InitializingRun ||
                   phase == ZombieModeLifecyclePhase.WaitingStarterChoice ||
                   ZombieModePhaseGuards.IsActive(phase);
        }

        private bool ShouldRollbackZombieModeEntryResources()
        {
            return !zombieModeRunState.EntryResourcesFinalized && !zombieModeEntryTransaction.EntryResourcesFinalized;
        }

        private void TickZombieMode(float deltaTime)
        {
            if (!IsZombieModeActive)
            {
                ResetZombieModeRuntimePauseClock();
                return;
            }

            RefreshZombieModeRuntimePauseClock();
            if (IsZombieModeRuntimePaused())
            {
                return;
            }

            TickZombieModeWaveController(deltaTime);
            TickZombieModeDropsAndPerformance(deltaTime);
            TickZombieModeBossController(deltaTime);
            TickZombieModeTemporaryNpcProtection();
            UpdateModeFFortificationHighlights();
            UpdateFortPlacementMode();
            UpdateModeFRepairSelection();
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
            return ZombieModeUIHelper.IsModalInputPaused || IsZombieModeGamePaused();
        }

        private float zombieModeRuntimePausedDuration;
        private float zombieModeRuntimePauseStartTime = -1f;
        private int zombieModeRuntimePauseRunId;

        private void RefreshZombieModeRuntimePauseClock()
        {
            int runId = zombieModeRunState.RunId;
            if (runId <= 0)
            {
                ResetZombieModeRuntimePauseClock();
                return;
            }

            if (zombieModeRuntimePauseRunId != runId)
            {
                zombieModeRuntimePauseRunId = runId;
                zombieModeRuntimePausedDuration = 0f;
                zombieModeRuntimePauseStartTime = -1f;
            }

            if (IsZombieModeRuntimePaused())
            {
                if (zombieModeRuntimePauseStartTime < 0f)
                {
                    zombieModeRuntimePauseStartTime = Time.unscaledTime;
                }
                return;
            }

            if (zombieModeRuntimePauseStartTime >= 0f)
            {
                zombieModeRuntimePausedDuration += Mathf.Max(0f, Time.unscaledTime - zombieModeRuntimePauseStartTime);
                zombieModeRuntimePauseStartTime = -1f;
            }
        }

        private void ResetZombieModeRuntimePauseClock()
        {
            zombieModeRuntimePauseRunId = 0;
            zombieModeRuntimePausedDuration = 0f;
            zombieModeRuntimePauseStartTime = -1f;
        }

        internal float GetZombieModeRuntimeNow()
        {
            float pausedDuration = zombieModeRuntimePausedDuration;
            if (zombieModeRuntimePauseRunId == zombieModeRunState.RunId &&
                zombieModeRuntimePauseStartTime >= 0f)
            {
                pausedDuration += Mathf.Max(0f, Time.unscaledTime - zombieModeRuntimePauseStartTime);
            }

            return Time.unscaledTime - pausedDuration;
        }

        private bool InitializeZombieModeRunAfterMapLoaded(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            PrepareSoulCubePrefabCacheForZombieRun();
            if (!PrepareZombieModeInventoryTransferShell(runId))
            {
                return false;
            }

            if (!CollectZombieModeSpawnPoints(runId))
            {
                DevLog("[ZombieMode] 初始化失败：未收集到有效刷怪点");
                return false;
            }

            if (!ApplyZombieModeMapIsolationShell(runId))
            {
                return false;
            }

            EnsureCharacterPresetsCacheReady();
            if (cachedCharacterPresets == null || !cachedCharacterPresets.ContainsKey("Cname_Zombie"))
            {
                DevLog("[ZombieMode] 初始化失败：缺少 Cname_Zombie 预设");
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

            RegisterZombieModeEventListeners(runId);
            CreateZombieModeHud(runId);
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.WaitingStarterChoice;
            ShowZombieModeStarterChoice(runId);
            return true;
        }

        private bool GrantZombieModeBeacon(int runId)
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
                DevLog("[ZombieMode] 发放尸潮信标失败: " + e.Message);
                return false;
            }
        }

        private void FinalizeZombieModeEntryResources()
        {
            zombieModeRunState.ZombieModeResourcesCommitted = true;
            zombieModeRunState.EntryResourcesFinalized = true;
            zombieModeEntryTransaction.EntryResourcesFinalized = true;
            zombieModeEntryTransaction.InvitationTemporarilyHeld = false;
        }

        private void RefundZombieModeInvitationIfNeeded()
        {
            if (!ShouldRollbackZombieModeEntryResources() || !zombieModeEntryTransaction.InvitationTemporarilyHeld)
            {
                return;
            }

            try
            {
                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                Item refund = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ZombieTideInvitation);
                if (refund != null)
                {
                    ItemUtilities.SendToPlayer(refund, true, PlayerStorage.Inventory != null);
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefundedInvitation"));
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 返还尸潮邀请函失败: " + e.Message);
            }
            finally
            {
                zombieModeEntryTransaction.InvitationTemporarilyHeld = false;
            }
        }

        private void FailZombieModeBeforeActive(ZombieModeFailureReason reason)
        {
            DevLog("[ZombieMode] Fail before Active: " + reason.ToString());
            bool shouldReturnToBase = ShouldReturnToBaseAfterZombieModePreActiveFailure(reason);
            RefundZombieModeInvitationIfNeeded();
            RefundZombieModeCashIfNeeded();
            CleanupZombieModeForSceneChange(reason);
            if (!shouldReturnToBase)
            {
                return;
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
                DevLog("[ZombieMode] [WARNING] Entry 失败回主场景失败: " + e.Message);
            }
        }

        private bool ShouldReturnToBaseAfterZombieModePreActiveFailure(ZombieModeFailureReason reason)
        {
            ZombieModeLifecyclePhase phase = zombieModeRunState.LifecyclePhase;
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

        private void ShowZombieModeStarterChoice(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject root = new GameObject("ZombieMode_StarterChoice");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, root, root, null);
            ZombieModeStarterChoiceView view = root.AddComponent<ZombieModeStarterChoiceView>();
            view.Initialize(runId, this);
            DevLog("[ZombieMode] 初始流派选择 UI 已创建: runId=" + runId);
        }

        public void SelectZombieModeStarterLoadout(int runId, ZombieModeStarterLoadout loadout)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.LifecyclePhase != ZombieModeLifecyclePhase.WaitingStarterChoice)
            {
                return;
            }

            if (!GrantZombieModeStarterLoadout(loadout))
            {
                FailZombieModeBeforeActive(ZombieModeFailureReason.StarterLoadoutFailed);
                return;
            }

            zombieModeRunState.StarterLoadout = loadout;
            FinalizeZombieModeEntryResources();
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.Active;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.InitialPreparation;
            UnlockZombieModeContainersForActiveRun(runId);
            BeginZombieModePreparation(runId, true, false);
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_Started"));
        }

        // Melee = 近战×1（品质≤5）+ 医疗品×5 + 食物×3 + 饮料×2
        // Gunner = 枪械×1 + 匹配口径×1000 + 医疗×3 + 食物×2 + 饮料×1
        private bool GrantZombieModeStarterLoadout(ZombieModeStarterLoadout loadout)
        {
            try
            {
                if (loadout != ZombieModeStarterLoadout.Melee && loadout != ZombieModeStarterLoadout.Gunner)
                {
                    return false;
                }

                bool grantedAny = false;
                if (loadout == ZombieModeStarterLoadout.Melee)
                {
                    bool coreGranted = TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 1, ZombieModeTuning.StarterMaxQuality);
                    if (!coreGranted)
                    {
                        DevLog("[ZombieMode] 近战开局失败：缺少可发放近战武器");
                        return false;
                    }

                    grantedAny = true;
                    int guaranteedHealing = TryGiveZombieModeStarterGuaranteedHealingItems();
                    if (guaranteedHealing < 2)
                    {
                        DevLog("[ZombieMode] 近战开局失败：保底回血道具不足");
                        return false;
                    }

                    int medical = guaranteedHealing + TryGiveRandomItemByTagsTimes(new string[] { "Medic", "Medical", "Healing" }, 1, 3, 3);
                    grantedAny |= medical > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Food" }, 1, 3, 3) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Drink" }, 1, 3, 2) > 0;
                    zombieModeRunState.StarterAmmoCaliber = string.Empty;
                }
                else if (loadout == ZombieModeStarterLoadout.Gunner)
                {
                    int gunTypeId = FindRandomItemTypeByTags(new string[] { "Gun" }, 1, ZombieModeTuning.StarterMaxQuality);
                    bool gunGranted = false;
                    if (gunTypeId > 0)
                    {
                        Item gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
                        if (gun != null)
                        {
                            string caliber = TryReadZombieModeItemCaliber(gun);
                            if (!string.IsNullOrEmpty(caliber))
                            {
                                zombieModeRunState.StarterAmmoCaliber = caliber;
                            }
                            ItemUtilities.SendToPlayer(gun, false, false);
                            gunGranted = true;
                            grantedAny = true;
                        }
                    }

                    if (!gunGranted)
                    {
                        DevLog("[ZombieMode] 枪械开局失败：缺少可发放枪械");
                        return false;
                    }

                    int ammoCount = ZombieModeTuning.StarterGunnerExtraAmmoCount;
                    bool ammoGranted = false;
                    if (ammoCount > 0)
                    {
                        ammoGranted = TryGiveZombieModeStarterAmmo(zombieModeRunState.StarterAmmoCaliber, ammoCount);
                        grantedAny |= ammoGranted;
                    }

                    if (!ammoGranted)
                    {
                        DevLog("[ZombieMode] 枪械开局失败：缺少匹配或通用弹药");
                        return false;
                    }

                    int guaranteedHealing = TryGiveZombieModeStarterGuaranteedHealingItems();
                    if (guaranteedHealing < 2)
                    {
                        DevLog("[ZombieMode] 枪械开局失败：保底回血道具不足");
                        return false;
                    }

                    grantedAny |= (guaranteedHealing + TryGiveRandomItemByTagsTimes(new string[] { "Medic", "Medical", "Healing" }, 1, 3, 1)) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Food" }, 1, 3, 2) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Drink" }, 1, 3, 1) > 0;
                }

                if (!GrantZombieModeStarterProtectionSet())
                {
                    DevLog("[ZombieMode] 开局防具发放失败：缺少护甲/头盔/耳机候选物品");
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 发放初始流派失败: " + e.Message);
                return false;
            }
        }

        private bool GrantZombieModeStarterProtectionSet()
        {
            bool armorGranted = TryGiveRandomItemByTags(new string[] { "BodyArmor" }, 1, ZombieModeTuning.StarterMaxQuality);
            bool helmetGranted = TryGiveRandomItemByTags(new string[] { "Helmet" }, 1, ZombieModeTuning.StarterMaxQuality);
            bool headsetGranted = TryGiveRandomItemByTags(new string[] { "Headset" }, 1, ZombieModeTuning.StarterMaxQuality);

            if (!armorGranted)
            {
                DevLog("[ZombieMode] 开局护甲发放失败");
            }
            if (!helmetGranted)
            {
                DevLog("[ZombieMode] 开局头盔发放失败");
            }
            if (!headsetGranted)
            {
                DevLog("[ZombieMode] 开局耳机发放失败");
            }

            return armorGranted && helmetGranted && headsetGranted;
        }

        private int TryGiveZombieModeStarterGuaranteedHealingItems()
        {
            int success = 0;
            for (int i = 0; i < 2; i++)
            {
                if (TryGiveZombieModeStarterGuaranteedHealingItem())
                {
                    success++;
                }
            }
            return success;
        }

        private bool TryGiveZombieModeStarterGuaranteedHealingItem()
        {
            return TryGiveRandomItemByTags(new string[] { "Healing" }, 1, ZombieModeTuning.StarterMaxQuality);
        }

        private bool TryGiveZombieModeWaveClearHealingItem()
        {
            return TryGiveRandomItemByTags(new string[] { "Healing" }, 1, ZombieModeTuning.StarterMaxQuality);
        }

        private int TryGiveRandomItemByTagsTimes(string[] requiredTags, int minQuality, int maxQuality, int times)
        {
            int success = 0;
            for (int i = 0; i < times; i++)
            {
                if (TryGiveRandomItemByTags(requiredTags, minQuality, maxQuality))
                {
                    success++;
                }
            }
            return success;
        }

        private string TryReadZombieModeItemCaliber(Item item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            try
            {
                if (item.Constants == null)
                {
                    return string.Empty;
                }
                string caliber = item.Constants.GetString("Caliber", null);
                return caliber ?? string.Empty;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 弹药 caliber 读取失败: " + e.Message);
            }
            return string.Empty;
        }

        private bool TryGiveZombieModeStarterAmmo(string caliber, int totalCount)
        {
            return TryGiveZombieModeAmmo(caliber, totalCount, 1, 2);
        }

        private bool TryGiveZombieModeAmmo(string caliber, int totalCount, int minQuality, int maxQuality)
        {
            try
            {
                ItemFilter filter = new ItemFilter();
                Tag[] ammoTags = ResolveZombieModeTags(new string[] { "Ammo" });
                if (ammoTags == null || ammoTags.Length <= 0)
                {
                    ammoTags = ResolveZombieModeTags(new string[] { "Bullet" });
                }
                filter.requireTags = ammoTags;
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = caliber ?? string.Empty;

                int[] candidates = ItemAssetsCollection.Search(filter);
                if (candidates == null || candidates.Length <= 0)
                {
                    return false;
                }

                int chosenTypeId = PickZombieModeStrictQualityCandidate(candidates, minQuality, maxQuality);
                if (chosenTypeId <= 0)
                {
                    return false;
                }

                Item ammoItem = ItemAssetsCollection.InstantiateSync(chosenTypeId);
                if (ammoItem == null)
                {
                    return false;
                }

                try { ammoItem.StackCount = totalCount; } catch (System.Exception e) { DevLog("[ZombieMode] ammoItem.StackCount 设置失败: " + e.Message); }
                ItemUtilities.SendToPlayer(ammoItem, true, true);
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 发放起始弹药失败: " + e.Message);
                return false;
            }
        }

        private bool TryGiveRandomItemByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            int typeId = FindRandomItemTypeByTags(requiredTags, minQuality, maxQuality);
            if (typeId <= 0)
            {
                return false;
            }

            Item item = ItemAssetsCollection.InstantiateSync(typeId);
            if (item == null)
            {
                return false;
            }

            ItemUtilities.SendToPlayer(item, false, false);
            return true;
        }

        private int FindRandomItemTypeByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            try
            {
                int[] candidates = GetZombieModeRewardCandidateIds(requiredTags, minQuality, maxQuality);
                if (candidates == null || candidates.Length <= 0)
                {
                    return -1;
                }

                return candidates[UnityEngine.Random.Range(0, candidates.Length)];
            }
            catch
            {
                return -1;
            }
        }

        private int PickZombieModeStrictQualityCandidate(int[] candidates, int minQuality, int maxQuality)
        {
            if (candidates == null || candidates.Length <= 0)
            {
                return -1;
            }

            minQuality = Mathf.Max(0, minQuality);
            maxQuality = Mathf.Max(minQuality, maxQuality);
            int eligibleCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsZombieModeItemQualityInRange(candidates[i], minQuality, maxQuality))
                {
                    eligibleCount++;
                }
            }

            if (eligibleCount <= 0)
            {
                return -1;
            }

            int target = UnityEngine.Random.Range(0, eligibleCount);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!IsZombieModeItemQualityInRange(candidates[i], minQuality, maxQuality))
                {
                    continue;
                }

                if (target <= 0)
                {
                    return candidates[i];
                }

                target--;
            }

            return -1;
        }

        private bool IsZombieModeItemQualityInRange(int typeId, int minQuality, int maxQuality)
        {
            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                return metaData.id > 0 && metaData.quality >= minQuality && metaData.quality <= maxQuality;
            }
            catch
            {
                return false;
            }
        }

        private int[] GetZombieModeRewardCandidateIds(string[] requiredTags, int minQuality, int maxQuality)
        {
            if (ItemAssetsCollection.Instance == null)
            {
                return new int[0];
            }

            string cacheKey = BuildZombieModeRewardCandidateCacheKey(requiredTags, minQuality, maxQuality);
            int[] cached;
            if (zombieModeRewardCandidateCache.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            zombieModeRewardSafeCandidateScratch.Clear();

            if (requiredTags == null || requiredTags.Length <= 0)
            {
                ItemFilter filter = new ItemFilter();
                filter.requireTags = null;
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = string.Empty;
                int[] candidates = ItemAssetsCollection.Search(filter);
                AddZombieModeRewardCandidates(candidates);
            }
            else
            {
                bool searchedAnyTag = false;
                for (int i = 0; i < requiredTags.Length; i++)
                {
                    Tag[] tags = ResolveZombieModeTags(new string[] { requiredTags[i] });
                    if (tags == null || tags.Length <= 0)
                    {
                        continue;
                    }

                    for (int tagIndex = 0; tagIndex < tags.Length; tagIndex++)
                    {
                        Tag tag = tags[tagIndex];
                        if (tag == null)
                        {
                            continue;
                        }

                        searchedAnyTag = true;
                        ItemFilter filter = new ItemFilter();
                        filter.requireTags = new Tag[] { tag };
                        filter.minQuality = minQuality;
                        filter.maxQuality = maxQuality;
                        filter.caliber = string.Empty;
                        int[] candidates = ItemAssetsCollection.Search(filter);
                        AddZombieModeRewardCandidates(candidates);
                    }
                }

                if (!searchedAnyTag)
                {
                    cached = new int[0];
                    zombieModeRewardCandidateCache[cacheKey] = cached;
                    return cached;
                }
            }

            cached = zombieModeRewardSafeCandidateScratch.ToArray();
            zombieModeRewardSafeCandidateScratch.Clear();
            zombieModeRewardCandidateCache[cacheKey] = cached;
            return cached;
        }

        private void AddZombieModeRewardCandidates(int[] candidates)
        {
            if (candidates == null || candidates.Length <= 0)
            {
                return;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsZombieModeRewardCandidateAllowed(candidates[i]) &&
                    !zombieModeRewardSafeCandidateScratch.Contains(candidates[i]))
                {
                    zombieModeRewardSafeCandidateScratch.Add(candidates[i]);
                }
            }
        }

        private string BuildZombieModeRewardCandidateCacheKey(string[] requiredTags, int minQuality, int maxQuality)
        {
            string tagsKey = "*";
            if (requiredTags != null && requiredTags.Length > 0)
            {
                tagsKey = string.Join("|", requiredTags);
            }

            return tagsKey + "#" + minQuality.ToString() + "#" + maxQuality.ToString();
        }

        private Tag[] ResolveZombieModeTags(string[] tagNames)
        {
            if (tagNames == null || tagNames.Length == 0)
            {
                return null;
            }

            List<Tag> tags = new List<Tag>();
            for (int i = 0; i < tagNames.Length; i++)
            {
                AddZombieModeTagsByName(tags, tagNames[i]);
            }

            return tags.ToArray();
        }

        private void AddZombieModeTagsByName(List<Tag> tags, string tagName)
        {
            try
            {
                if (tags == null)
                {
                    return;
                }

                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null)
                {
                    return;
                }

                string[] aliases = GetZombieModeTagAliases(tagName);
                for (int i = 0; i < aliases.Length; i++)
                {
                    string alias = aliases[i];
                    foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                    {
                        if (tag != null && tag.name == alias)
                        {
                            if (!tags.Contains(tag))
                            {
                                tags.Add(tag);
                            }
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] Tag.AllTags 扫描失败: " + e.Message);
            }

        }

        private Tag FindZombieModeTagByName(string tagName)
        {
            try
            {
                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null)
                {
                    return null;
                }

                string[] aliases = GetZombieModeTagAliases(tagName);
                for (int i = 0; i < aliases.Length; i++)
                {
                    string alias = aliases[i];
                    foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                    {
                        if (tag != null && tag.name == alias)
                        {
                            return tag;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] Tag.AllTags 扫描失败: " + e.Message);
            }

            return null;
        }

        private static string[] GetZombieModeTagAliases(string tagName)
        {
            if (string.Equals(tagName, "BodyArmor", System.StringComparison.Ordinal))
            {
                return new string[] { "Armor" };
            }

            if (string.Equals(tagName, "Armor", System.StringComparison.Ordinal))
            {
                return new string[] { "Armor", "Helmat", "Helmet" };
            }

            if (string.Equals(tagName, "Helmet", System.StringComparison.Ordinal))
            {
                return new string[] { "Helmat", "Helmet" };
            }

            return new string[] { tagName };
        }

        private bool IsZombieModeRewardCandidateAllowed(int typeId)
        {
            return typeId > 0 &&
                   typeId != BossRushItemIds.ZombieTideInvitation &&
                   typeId != BossRushItemIds.ZombieTideBeacon;
        }
    }

    public sealed class ZombieModeStarterChoiceView : MonoBehaviour
    {
        // ==================== 配色方案（与 CashInvestmentView 统一） ====================
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelOuterColor = new Color(0.12f, 0.16f, 0.24f, 0.98f);
        private static readonly Color PanelBorderColor = new Color(0.22f, 0.30f, 0.44f, 0.45f);
        private static readonly Color PanelInnerColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.14f, 0.20f, 0.32f, 1.00f);
        private static readonly Color AccentLineColor = new Color(0.35f, 0.55f, 0.85f, 0.70f);
        private static readonly Color SubtitleColor = new Color(0.62f, 0.70f, 0.82f, 0.90f);

        // 近战卡片：暗红-铜色调
        private static readonly Color MeleeCardColor = new Color(0.14f, 0.10f, 0.10f, 0.98f);
        private static readonly Color MeleeAccentColor = new Color(0.85f, 0.50f, 0.25f, 0.95f);
        private static readonly Color MeleeBtnColor = new Color(0.52f, 0.30f, 0.14f, 1.00f);
        private static readonly Color MeleeBtnHoverColor = new Color(0.68f, 0.40f, 0.20f, 1.00f);

        // 枪手卡片：暗蓝-钢色调
        private static readonly Color GunnerCardColor = new Color(0.10f, 0.10f, 0.14f, 0.98f);
        private static readonly Color GunnerAccentColor = new Color(0.35f, 0.65f, 0.92f, 0.95f);
        private static readonly Color GunnerBtnColor = new Color(0.16f, 0.36f, 0.56f, 1.00f);
        private static readonly Color GunnerBtnHoverColor = new Color(0.22f, 0.48f, 0.72f, 1.00f);

        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
            ClaimInputAndPause();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            // ── 全屏遮罩 ──
            GameObject backdrop = ZombieModeUIHelper.CreateRect(
                "Backdrop", transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = BackdropColor;
            backdropImage.raycastTarget = true;

            // ── 外框（深靛色） ──
            GameObject outer = ZombieModeUIHelper.CreateRect("PanelOuter", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(700f, 420f), new Vector2(0.5f, 0.5f));
            Image outerImage = outer.AddComponent<Image>();
            outerImage.color = PanelOuterColor;

            // ── 亮边层 ──
            GameObject borderGlow = ZombieModeUIHelper.CreateRect("BorderGlow", outer.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image borderImg = borderGlow.AddComponent<Image>();
            borderImg.color = PanelBorderColor;

            // ── 主面板 ──
            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", borderGlow.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = PanelInnerColor;

            // ── 标题栏 ──
            float yPos = 0f;
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = HeaderColor;

            ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T("BossRush_ZombieMode_Starter_Title"), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, Color.white);
            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f, AccentLineColor);
            yPos += 6f;

            // ── 副标题 ──
            float subtitleH = 36f;
            ZombieModeUIHelper.CreateText("Subtitle", panel.transform,
                L10n.T("BossRush_ZombieMode_Starter_Subtitle"), 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + subtitleH * 0.5f)), new Vector2(-40f, subtitleH),
                TextAlignmentOptions.Center, SubtitleColor);
            yPos += subtitleH + 10f;

            // ── 卡片区域 ──
            float cardW = 290f;
            float cardH = 230f;
            float cardGap = 24f;
            float cardsStartX = -(cardW + cardGap * 0.5f) * 0.5f;

            // 近战卡片
            CreateLoadoutCard(panel.transform, "MeleeCard",
                new Vector2(-(cardW * 0.5f + cardGap * 0.5f), -(yPos + cardH * 0.5f)),
                new Vector2(cardW, cardH),
                L10n.T("BossRush_ZombieMode_Starter_Melee"),
                L10n.T("BossRush_ZombieMode_Starter_Melee_Desc"),
                "刀",
                MeleeCardColor, MeleeAccentColor, MeleeBtnColor, MeleeBtnHoverColor,
                ZombieModeStarterLoadout.Melee);

            // 枪手卡片
            CreateLoadoutCard(panel.transform, "GunnerCard",
                new Vector2(cardW * 0.5f + cardGap * 0.5f, -(yPos + cardH * 0.5f)),
                new Vector2(cardW, cardH),
                L10n.T("BossRush_ZombieMode_Starter_Gunner"),
                L10n.T("BossRush_ZombieMode_Starter_Gunner_Desc"),
                "枪",
                GunnerCardColor, GunnerAccentColor, GunnerBtnColor, GunnerBtnHoverColor,
                ZombieModeStarterLoadout.Gunner);
        }

        private void CreateLoadoutCard(
            Transform parent, string name, Vector2 position, Vector2 size,
            string title, string description, string iconText,
            Color cardBg, Color accentColor, Color btnColor, Color btnHoverColor,
            ZombieModeStarterLoadout loadout)
        {
            // ── 卡片底板 ──
            GameObject card = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                position, size, new Vector2(0.5f, 0.5f));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = cardBg;

            // ── 顶部高亮条 ──
            GameObject topAccent = ZombieModeUIHelper.CreateRect("TopAccent", card.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -2f), new Vector2(0f, 4f), new Vector2(0.5f, 1f));
            Image topAccentImg = topAccent.AddComponent<Image>();
            topAccentImg.color = accentColor;
            topAccentImg.raycastTarget = false;

            // ── 图标区域（使用文字模拟） ──
            float iconAreaH = 52f;
            ZombieModeUIHelper.CreateText("Icon", card.transform, iconText, 32,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(14f + iconAreaH * 0.5f)), new Vector2(0f, iconAreaH),
                TextAlignmentOptions.Center, accentColor);

            // ── 名称 ──
            float titleY = 14f + iconAreaH + 4f;
            float titleH = 34f;
            ZombieModeUIHelper.CreateText("Title", card.transform, title, 22,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(titleY + titleH * 0.5f)), new Vector2(-16f, titleH),
                TextAlignmentOptions.Center, Color.white);

            // ── 分隔线 ──
            float sepY = titleY + titleH + 6f;
            ZombieModeUIHelper.CreateSeparator("Sep", card.transform,
                new Vector2(0.15f, 1f), new Vector2(0.85f, 1f),
                new Vector2(0f, -sepY), 1f, new Color(accentColor.r, accentColor.g, accentColor.b, 0.35f));

            // ── 说明文字 ──
            float descY = sepY + 8f;
            float descH = 56f;
            ZombieModeUIHelper.CreateText("Desc", card.transform, description, 13,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(descY + descH * 0.5f)), new Vector2(-24f, descH),
                TextAlignmentOptions.Center, new Color(0.70f, 0.74f, 0.80f, 0.92f));

            // ── 选择按钮 ──
            float btnW = 200f;
            float btnH = 42f;
            float btnY = size.y - 18f - btnH * 0.5f;
            ZombieModeStarterLoadout capturedLoadout = loadout;

            Button button = ZombieModeUIHelper.CreateButton(
                "SelectBtn", card.transform,
                L10n.T("BossRush_ZombieMode_Starter_Select"),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -btnY),
                new Vector2(btnW, btnH),
                btnColor, 17,
                new Vector2(btnW - 12f, btnH - 6f),
                delegate
                {
                    RestoreInputState();
                    if (owner != null)
                    {
                        owner.SelectZombieModeStarterLoadout(runId, capturedLoadout);
                    }
                    Destroy(gameObject);
                },
                true);

            // 设置按钮悬停色
            Image btnImage = button.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = btnColor;
            colors.highlightedColor = btnHoverColor;
            colors.pressedColor = btnColor * 0.85f;
            colors.selectedColor = btnHoverColor;
            colors.disabledColor = btnColor * 0.6f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = btnImage;
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "StarterChoice");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }
    }
}

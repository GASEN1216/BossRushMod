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
        private static readonly string[] ZombieModeRewardTagAmmo = { "Ammo" };
        private static readonly string[] ZombieModeRewardTagArmor = { "Armor" };
        private static readonly string[] ZombieModeRewardTagBodyArmor = { "BodyArmor" };
        private static readonly string[] ZombieModeRewardTagBullet = { "Bullet" };
        private static readonly string[] ZombieModeRewardTagDrink = { "Drink" };
        private static readonly string[] ZombieModeRewardTagFood = { "Food" };
        private static readonly string[] ZombieModeRewardTagGun = { "Gun" };
        private static readonly string[] ZombieModeRewardTagHeadset = { "Headset" };
        private static readonly string[] ZombieModeRewardTagHealing = { "Healing" };
        private static readonly string[] ZombieModeRewardTagHelmet = { "Helmet" };
        private static readonly string[] ZombieModeRewardTagMedical = { "Medical" };
        private static readonly string[] ZombieModeRewardTagMedic = { "Medic" };
        private static readonly string[] ZombieModeRewardTagMeleeWeapon = { "MeleeWeapon" };
        private static readonly string[] ZombieModeRewardTagWeapon = { "Weapon" };
        private static readonly string[] ZombieModeRewardTagsMedicMedicalHealing = { "Medic", "Medical", "Healing" };
        private static readonly string[] ZombieModeTagAliasesBodyArmor = { "Armor" };
        private static readonly string[] ZombieModeTagAliasesArmor = { "Armor", "Helmat", "Helmet" };
        private static readonly string[] ZombieModeTagAliasesHelmet = { "Helmat", "Helmet" };
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
                    Tag[] tags = ResolveZombieModeTags(requiredTags[i]);
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

        private Tag[] ResolveZombieModeTags(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            List<Tag> tags = new List<Tag>();
            AddZombieModeTagsByName(tags, tagName);
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
                return ZombieModeTagAliasesBodyArmor;
            }

            if (string.Equals(tagName, "Armor", System.StringComparison.Ordinal))
            {
                return ZombieModeTagAliasesArmor;
            }

            if (string.Equals(tagName, "Helmet", System.StringComparison.Ordinal))
            {
                return ZombieModeTagAliasesHelmet;
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
}

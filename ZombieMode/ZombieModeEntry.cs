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
        private static int nextZombieModeRunId = 0;
        private bool pendingZombieModeEntry = false;

        public bool IsZombieModeActive
        {
            get
            {
                ZombieModeLifecyclePhase phase = zombieModeRunState.LifecyclePhase;
                return phase == ZombieModeLifecyclePhase.WaitingStarterChoice ||
                       ZombieModePhaseGuards.IsActive(phase) ||
                       phase == ZombieModeLifecyclePhase.Exiting;
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

        // 入场预检查（邀请函/仓储/任务物品/其他模式互斥）
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

                if (customPos.HasValue)
                {
                    StartCoroutine(TeleportPlayerToCustomPosition(customPos.Value));
                }

                ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
                if (!InitializeZombieModeRunAfterMapLoaded(runId))
                {
                    FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                    return true;
                }

                DevLog("[ZombieMode] 已进入目标地图: " + scene.name + "，等待初始流派选择");
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
                return;
            }

            TickZombieModeWaveController(deltaTime);
            TickZombieModeDropsAndPerformance(deltaTime);
            TickZombieModeBossController(deltaTime);
            TickZombieModeTemporaryNpcProtection();
        }

        private bool InitializeZombieModeRunAfterMapLoaded(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            if (!PrepareZombieModeInventoryTransferShell(runId))
            {
                return false;
            }

            if (!ApplyZombieModeMapIsolationShell(runId))
            {
                return false;
            }

            if (!CollectZombieModeSpawnPoints(runId))
            {
                return false;
            }

            if (FindZombieModeNormalZombiePreset() == null)
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
                RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Beacon, null, beacon, delegate
                {
                    CleanupZombieModeBeaconItem(beacon);
                });
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 发放尸潮信标失败: " + e.Message);
                return false;
            }
        }

        private void CleanupZombieModeBeaconItem(Item issuedBeacon)
        {
            DestroyZombieModeRunOnlyBeaconItem(issuedBeacon);
            try
            {
                List<Item> beacons = ItemUtilities.FindAllBelongsToPlayer(delegate(Item item)
                {
                    return item != null &&
                           !item.IsBeingDestroyed &&
                           item.TypeID == BossRushItemIds.ZombieTideBeacon;
                });
                for (int i = 0; i < beacons.Count; i++)
                {
                    DestroyZombieModeRunOnlyBeaconItem(beacons[i]);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 清理尸潮信标失败: " + e.Message);
            }
        }

        private void DestroyZombieModeRunOnlyBeaconItem(Item item)
        {
            if (item == null || item.IsBeingDestroyed)
            {
                return;
            }

            try
            {
                item.Detach();
                item.DestroyTree();
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 销毁尸潮信标失败: " + e.Message);
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
            RefundZombieModeInvitationIfNeeded();
            RefundZombieModeCashIfNeeded();
            CleanupZombieModeForSceneChange(reason);
            try
            {
                if (SceneLoader.Instance != null)
                {
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
                }
            }
            catch { }
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
                bool grantedAny = false;
                if (loadout == ZombieModeStarterLoadout.Melee)
                {
                    grantedAny |= TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 1, ZombieModeTuning.StarterMaxQuality);
                    int medical = TryGiveRandomItemByTagsTimes(new string[] { "Medic", "Medical", "Healing" }, 1, 3, 5);
                    grantedAny |= medical > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Food" }, 1, 3, 3) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Drink" }, 1, 3, 2) > 0;
                    zombieModeRunState.StarterAmmoCaliber = string.Empty;
                }
                else if (loadout == ZombieModeStarterLoadout.Gunner)
                {
                    int gunTypeId = FindRandomItemTypeByTags(new string[] { "Gun" }, 1, ZombieModeTuning.StarterMaxQuality);
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
                            grantedAny = true;
                        }
                    }

                    int ammoCount = ZombieModeTuning.StarterGunnerExtraAmmoCount;
                    if (ammoCount > 0)
                    {
                        grantedAny |= TryGiveZombieModeStarterAmmo(zombieModeRunState.StarterAmmoCaliber, ammoCount);
                    }

                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Medic", "Medical", "Healing" }, 1, 3, 3) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Food" }, 1, 3, 2) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(new string[] { "Drink" }, 1, 3, 1) > 0;
                }

                return grantedAny || loadout == ZombieModeStarterLoadout.Melee || loadout == ZombieModeStarterLoadout.Gunner;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 发放初始流派失败: " + e.Message);
                return false;
            }
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
            catch { }
            return string.Empty;
        }

        private bool TryGiveZombieModeStarterAmmo(string caliber, int totalCount)
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
                filter.minQuality = 1;
                filter.maxQuality = 2;
                filter.caliber = caliber ?? string.Empty;

                int[] candidates = ItemAssetsCollection.Search(filter);
                if (candidates == null || candidates.Length <= 0)
                {
                    return false;
                }

                int chosenTypeId = candidates[UnityEngine.Random.Range(0, candidates.Length)];
                Item ammoItem = ItemAssetsCollection.InstantiateSync(chosenTypeId);
                if (ammoItem == null)
                {
                    return false;
                }

                try { ammoItem.StackCount = totalCount; } catch { }
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
                if (ItemAssetsCollection.Instance == null)
                {
                    return -1;
                }

                Tag[] tags = ResolveZombieModeTags(requiredTags);
                if (requiredTags != null && requiredTags.Length > 0 && (tags == null || tags.Length <= 0))
                {
                    return -1;
                }

                ItemFilter filter = new ItemFilter();
                filter.requireTags = tags;
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = string.Empty;
                int[] candidates = ItemAssetsCollection.Search(filter);
                if (candidates == null || candidates.Length <= 0)
                {
                    return -1;
                }

                List<int> safeCandidates = new List<int>();
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (IsZombieModeRewardCandidateAllowed(candidates[i]))
                    {
                        safeCandidates.Add(candidates[i]);
                    }
                }

                if (safeCandidates.Count <= 0)
                {
                    return -1;
                }

                return safeCandidates[UnityEngine.Random.Range(0, safeCandidates.Count)];
            }
            catch
            {
                return -1;
            }
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
                Tag tag = FindZombieModeTagByName(tagNames[i]);
                if (tag != null)
                {
                    tags.Add(tag);
                }
            }

            return tags.ToArray();
        }

        private Tag FindZombieModeTagByName(string tagName)
        {
            try
            {
                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null)
                {
                    return null;
                }

                foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                {
                    if (tag != null && tag.name == tagName)
                    {
                        return tag;
                    }
                }
            }
            catch { }

            return null;
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
        private int runId;
        private ModBehaviour owner;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreateRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(520f, 260f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.85f);

            CreateText("Title", panel.transform, L10n.T("BossRush_ZombieMode_Starter_Title"), 28, new Vector2(0f, 80f), new Vector2(480f, 60f));
            CreateButton("Melee", panel.transform, L10n.T("BossRush_ZombieMode_Starter_Melee"), new Vector2(-130f, -30f), ZombieModeStarterLoadout.Melee);
            CreateButton("Gunner", panel.transform, L10n.T("BossRush_ZombieMode_Starter_Gunner"), new Vector2(130f, -30f), ZombieModeStarterLoadout.Gunner);
        }

        private GameObject CreateRect(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return obj;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, int fontSize, Vector2 position, Vector2 size)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            return ZombieModeUIHelper.CreateTMPText(
                obj,
                text,
                fontSize,
                TextAlignmentOptions.Center,
                Color.white);
        }

        private void CreateButton(string name, Transform parent, string text, Vector2 position, ZombieModeStarterLoadout loadout)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(210f, 78f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.color = new Color(0.18f, 0.32f, 0.22f, 0.95f);
            Button button = obj.AddComponent<Button>();
            CreateText("Text", obj.transform, text, 22, Vector2.zero, new Vector2(200f, 68f));
            button.onClick.AddListener(delegate
            {
                if (owner != null)
                {
                    owner.SelectZombieModeStarterLoadout(runId, loadout);
                }
                Destroy(gameObject);
            });
        }
    }
}

using System.Collections;
using Duckov.MiniMaps;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule : BossRushRuntimeModuleBase
    {
        private GameObject zombieModeExtractionOpportunityUiRoot;

        private bool IsZombieModeActive
        {
            get { return ZombieModePhaseGuards.IsRunActive(runState.LifecyclePhase); }
        }

        /// <summary>
        /// 主槽（正常安全区，带商人）与副槽（准备期便携安全区，不带商人）任一激活。
        /// 两槽只在准备期并存，下一波开始时一起清除。
        /// </summary>
        internal bool AnyZombieModeSafeZoneActive
        {
            get { return runState.ActiveSafeZoneActive || runState.PortableSafeZoneActive; }
        }


        internal bool CanUseZombieModeBeacon()
        {
            if (!IsZombieModeActive)
            {
                return false;
            }

            return !runState.BeaconChanneling &&
                   !runState.ExtractionChanneling &&
                   ZombieModePhaseGuards.AllowsBeacon(runState.CombatPhase);
        }

        internal bool CanUseZombieModePortableSafeZoneDevice()
        {
            return IsZombieModeActive &&
                   !runState.BeaconChanneling &&
                   !runState.ExtractionChanneling &&
                   ZombieModePhaseGuards.AllowsPortableSafeZoneDeployment(runState.CombatPhase);
        }

        internal string GetZombieModePortableSafeZoneUnavailableReasonKey()
        {
            // 「不在丧尸模式」原先由 PortableSafeZoneDeviceUsage.CanBeUsed 自己弹；判断函数不再弹窗后归口到这里，
            // 与 GetZombieModeBeaconUnavailableReasonKey 同口径。
            if (!IsZombieModeActive)
            {
                return "BossRush_ZombieMode_Notify_PortableSafeZoneNotZombieMode";
            }

            return "BossRush_ZombieMode_Notify_PortableSafeZoneUnavailable";
        }

        /// <summary>
        /// 部署便携安全区。战斗中部署替换主槽，波次结束时随准备期清理消失，
        /// 之后正常流程会重新生成带商人的安全区；准备期部署则写入独立副槽，
        /// 与正常安全区（及其商人）并存，下一波开始时一起清除。
        /// </summary>
        internal bool TryUseZombieModePortableSafeZoneDevice()
        {
            if (!CanUseZombieModePortableSafeZoneDevice())
            {
                NotificationText.Push(L10n.T(GetZombieModePortableSafeZoneUnavailableReasonKey()));
                return false;
            }

            if (runState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                CreateZombieModeSafeZone(runState.RunId, false, true, true);
            }
            else
            {
                CreateZombieModeSafeZone(runState.RunId, false, false, true, true);
            }
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_PortableSafeZoneDeployed"));
            return true;
        }

        internal string GetZombieModeBeaconUnavailableReasonKey()
        {
            if (!IsZombieModeActive)
            {
                return "BossRush_ZombieMode_Notify_BeaconNotZombieMode";
            }

            if (runState.ExtractionChanneling)
            {
                return "BossRush_ZombieMode_Notify_BeaconExtractionLocked";
            }

            return "BossRush_ZombieMode_Notify_BeaconNotPreparation";
        }

        internal bool TryUseZombieModeBeacon()
        {
            if (!CanUseZombieModeBeacon())
            {
                NotificationText.Push(L10n.T(GetZombieModeBeaconUnavailableReasonKey()));
                return false;
            }

            runState.BeaconChanneling = true;
            runState.BeaconChannelStartTime = Time.unscaledTime;
            owner.StartZombieModeCoroutineForRuntimeModule(ZombieModeBeaconChannelCoroutine(runState.RunId), runState.RunId);
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconChannelStarted"));
            return true;
        }

        private IEnumerator ZombieModeBeaconChannelCoroutine(int runId)
        {
            float remaining = runState.BeaconChannelDuration;
            while (remaining > 0f)
            {
                if (!IsZombieModeRunValid(runId))
                {
                    yield break;
                }

                if (runState.ExtractionChanneling || !ZombieModePhaseGuards.AllowsBeacon(runState.CombatPhase))
                {
                    runState.BeaconChanneling = false;
                    runState.BeaconChannelStartTime = 0f;
                    yield break;
                }

                if (!IsZombieModeRuntimePaused())
                {
                    remaining -= Time.unscaledDeltaTime;
                }
                else
                {
                    // 暂停时起点跟着顺延：HUD 信标读条按「当前时刻 - 起点」算已引导时长（审美审查 UC-17）。
                    runState.BeaconChannelStartTime += Time.unscaledDeltaTime;
                }

                yield return null;
            }

            if (!IsZombieModeRunValid(runId) || !runState.BeaconChanneling)
            {
                yield break;
            }

            runState.BeaconChanneling = false;
            runState.BeaconChannelStartTime = 0f;
            owner.StartZombieModeWaveForRuntimeModule(runId);
        }

        internal void ShowZombieModeExtractionOpportunityUi(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ClearZombieModeExtractionOpportunityUi();
            zombieModeExtractionOpportunityUiRoot = new GameObject("ZombieMode_ExtractionOpportunity");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, zombieModeExtractionOpportunityUiRoot, zombieModeExtractionOpportunityUiRoot, null);
            ZombieModeExtractionOpportunityView view = zombieModeExtractionOpportunityUiRoot.AddComponent<ZombieModeExtractionOpportunityView>();
            view.Initialize(runId, owner);
            owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_ExtractionOpen"));
        }

        internal void ContinueZombieModeAfterExtractionOpportunity(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity)
            {
                return;
            }

            CloseZombieModeExtractionOpportunityAndContinue(runId);
        }

        private void CloseZombieModeExtractionOpportunityAndContinue(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity)
            {
                return;
            }

            ClearZombieModeExtractionOpportunityUi();
            runState.ExtractionChanneling = false;
            DestroyZombieModeActiveExtractionArea();
            runState.CombatPhase = ZombieModeCombatPhase.Preparation;
            owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_PreparationNextWave"));
        }

        /// <summary>
        /// 撤离页「立即撤离」。返回 null = 已受理（页面由宿主收掉）或页面已失效；否则是拒绝原因的本地化 key，
        /// 页面保留模态租约并就地提示（UI 共识对照审查 B-01：旧版先还租约再调这里，被拒时面板盖着、时间却恢复了）。
        /// </summary>
        internal string StartZombieModeExtractionFromUi(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return null;
            }

            return StartZombieModeExtraction(runId);
        }

        private string StartZombieModeExtraction(int runId)
        {
            if (!IsZombieModeRunValid(runId) ||
                runState.ExtractionChanneling ||
                runState.ExtractionSuccessHandled ||
                !ZombieModePhaseGuards.AllowsExtraction(runState.CombatPhase))
            {
                return null;
            }

            if (runState.BeaconChanneling)
            {
                return "BossRush_ZombieMode_Notify_ExtractionBeaconLocked";
            }

            EnsureZombieModeExtractionArea(runId);
            if (runState.ActiveExtractionArea == null)
            {
                return "BossRush_ZombieMode_Notify_ExtractionAreaFailed";
            }

            ClearZombieModeExtractionOpportunityUi();
            runState.ExtractionChanneling = true;
            runState.CombatPhase = ZombieModeCombatPhase.ExtractionOpportunity;
            try { EvacuationCountdownUI.Request(runState.ActiveExtractionArea); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] EvacuationCountdownUI.Request 失败: " + e.Message); }
            owner.StartZombieModeCoroutineForRuntimeModule(ZombieModeExtractionCountdownCoroutine(runId), runId);
            owner.ShowBigBanner(string.Format(
                L10n.T("BossRush_ZombieMode_Banner_ExtractionCountdown"),
                Mathf.CeilToInt(ZombieModeTuning.ExtractionCountdownSeconds)));
            return null;
        }

        private void CompleteZombieModeExtractionSuccess(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.ExtractionSuccessHandled)
            {
                return;
            }

            if (!SettleZombieModeExtractionCashShell())
            {
                runState.ExtractionChanneling = false;
                runState.BeaconChanneling = false;
                runState.CombatPhase = ZombieModeCombatPhase.ExtractionOpportunity;
                TryReleaseZombieModeExtractionCountdownUi();
                NotificationText.Push(L10n.T(
                    "净化点结算失败，奖励已保留；请稍后重新撤离。",
                    "Purification payout failed; your reward was preserved. Try extracting again later."));
                ShowZombieModeExtractionOpportunityUi(runId);
                return;
            }

            runState.ExtractionSuccessHandled = true;
            runState.CombatPhase = ZombieModeCombatPhase.SuccessExit;
            runState.ExtractionChanneling = false;
            runState.BeaconChanneling = false;
            owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Settle_SuccessTitle"));

            // 通知战役契约（未启用时零成本早返）。必须早于场景切换：
            // 回基地后 LifecyclePhase 会复位，武装判定就认不出这是丧尸模式了。
            owner.NotifyCampaignZombieExtracted();

            bool dispatched = TryDispatchZombieModeExtractionSuccess(runState.ActiveExtractionArea);
            if (!dispatched)
            {
                TryReleaseZombieModeExtractionCountdownUi();
                TryNotifyZombieModeExtraction();
                TryLoadBaseSceneAfterZombieModeExtraction();
            }

            owner.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.SuccessfulExtraction);
        }

        private bool SettleZombieModeExtractionCashShell()
        {
            if (runState.PurificationPoints <= 0)
            {
                return true;
            }

            long cashGain = runState.PurificationPoints;
            try
            {
                if (EconomyManager.Add(cashGain))
                {
                    NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_ExtractionCash"), cashGain.ToString("N0")));
                    runState.PurificationPoints = 0;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 净化点现金结算异常，保留欠账: " + e.Message);
                return false;
            }

            ModBehaviour.DevLog("[ZombieMode] 净化点现金结算失败，保留欠账: " + cashGain);
            return false;
        }

        private IEnumerator ZombieModeExtractionCountdownCoroutine(int runId)
        {
            CountDownArea area = runState.ActiveExtractionArea;
            float remaining = ZombieModeTuning.ExtractionCountdownSeconds;
            while (remaining > 0f)
            {
                if (!IsZombieModeRunValid(runId) ||
                    runState.ActiveExtractionArea != area ||
                    !runState.ExtractionChanneling ||
                    runState.ExtractionSuccessHandled)
                {
                    yield break;
                }

                if (!IsPlayerInsideZombieModeExtractionArea(area))
                {
                    runState.ExtractionChanneling = false;
                    TryReleaseZombieModeExtractionCountdownUi();
                    if (runState.PreparationTimer <= 0f)
                    {
                        owner.StartZombieModeWaveForRuntimeModule(runState.RunId);
                    }
                    yield break;
                }

                if (!IsZombieModeRuntimePaused())
                {
                    remaining -= Time.unscaledDeltaTime;
                }

                yield return null;
            }

            if (IsZombieModeRunValid(runId) &&
                runState.ActiveExtractionArea == area &&
                runState.ExtractionChanneling &&
                !runState.ExtractionSuccessHandled)
            {
                CompleteZombieModeExtractionSuccess(runId);
            }
        }

        internal void EnsureZombieModeExtractionArea(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.ActiveExtractionArea != null)
            {
                return;
            }

            CharacterMainControl mainPlayer = CharacterMainControl.Main;
            Vector3 position = runState.ActiveSafeZoneActive
                ? runState.ActiveSafeZoneCenter
                : (mainPlayer != null ? mainPlayer.transform.position : Vector3.zero);

            ModeExtractionPointRequest request = new ModeExtractionPointRequest();
            request.ObjectName = "ZombieMode_ExtractionPoint";
            request.Position = position + Vector3.up * 0.05f;
            request.CountdownSeconds = ZombieModeTuning.ExtractionCountdownSeconds;
            request.FallbackTriggerRadius = ZombieModeTuning.ExtractionAreaTriggerRadius;
            request.LogPrefix = "[ZombieMode]";
            request.IsCurrentArea = delegate(CountDownArea area)
            {
                return IsZombieModeRunValid(runId) && runState.ActiveExtractionArea == area;
            };
            request.OnSucceed = delegate
            {
                if (IsZombieModeRunValid(runId) &&
                    runState.ActiveExtractionArea != null &&
                    !runState.ExtractionSuccessHandled)
                {
                    CompleteZombieModeExtractionSuccess(runId);
                }
            };
            request.OnFallbackNotify = TryNotifyZombieModeExtractionFromFactory;
            request.OnFallbackLoadBase = TryLoadBaseSceneAfterZombieModeExtraction;

            ModeExtractionPointResult result = ModeExtractionPointFactory.CreateExtractionPoint(request);
            if (result == null || result.GameObject == null || result.CountDownArea == null)
            {
                return;
            }

            ZombieModeExtractionController controller = result.GameObject.GetComponent<ZombieModeExtractionController>();
            if (controller == null)
            {
                controller = result.GameObject.AddComponent<ZombieModeExtractionController>();
            }
            controller.Initialize(runId);

            runState.ActiveExtractionArea = result.CountDownArea;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.ExtractionPoint, result.GameObject, controller, null);
        }

        private bool IsPlayerInsideZombieModeExtractionArea(CountDownArea area)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (area == null || player == null)
            {
                return false;
            }

            Vector3 delta = player.transform.position - area.transform.position;
            delta.y = 0f;
            float r = ZombieModeTuning.ExtractionAreaLeaveRadius;
            return delta.sqrMagnitude <= r * r;
        }

        private void TryReleaseZombieModeExtractionCountdownUi()
        {
            try
            {
                if (runState.ActiveExtractionArea != null)
                {
                    EvacuationCountdownUI.Release(runState.ActiveExtractionArea);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] EvacuationCountdownUI.Release 失败: " + e.Message);
            }
        }

        private void TryNotifyZombieModeExtraction()
        {
            CharacterMainControl mainPlayer = CharacterMainControl.Main;
            Vector3 position = runState.ActiveExtractionArea != null
                ? runState.ActiveExtractionArea.transform.position
                : (mainPlayer != null ? mainPlayer.transform.position : Vector3.zero);
            TryNotifyZombieModeExtraction(position);
        }

        private void TryNotifyZombieModeExtractionFromFactory(Vector3 position)
        {
            TryNotifyZombieModeExtraction(position);
        }

        private void TryNotifyZombieModeExtraction(Vector3 position)
        {
            try
            {
                if (LevelManager.Instance == null)
                {
                    return;
                }

                EvacuationInfo info = new EvacuationInfo(MultiSceneCore.ActiveSubSceneID, position);
                LevelManager.Instance.NotifyEvacuated(info);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] [WARNING] NotifyEvacuated 失败: " + e.Message);
            }
        }

        private void TryLoadBaseSceneAfterZombieModeExtraction()
        {
            try
            {
                if (SceneLoader.Instance == null)
                {
                    ModBehaviour.DevLog("[ZombieMode] [WARNING] TryLoadBaseSceneAfterZombieModeExtraction: SceneLoader.Instance 为 null");
                    return;
                }

                UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] [WARNING] 撤离后回主场景失败: " + e.Message);
            }
        }

        private bool TryDispatchZombieModeExtractionSuccess(CountDownArea area)
        {
            if (area == null)
            {
                return false;
            }

            try
            {
                if (area.onCountDownStopped != null)
                {
                    area.onCountDownStopped.Invoke(area);
                }

                if (area.onCountDownSucceed != null)
                {
                    area.onCountDownSucceed.Invoke();
                    return true;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] [WARNING] TryDispatchZombieModeExtractionSuccess 失败: " + e.Message);
            }

            return false;
        }

        private void ClearZombieModeExtractionOpportunityUi()
        {
            if (zombieModeExtractionOpportunityUiRoot != null)
            {
                // 先还输入再淡出（审美审查 UC-07）：输入与时间流速在这一帧就恢复，淡出只是表现。
                ZombieModeExtractionOpportunityView view = zombieModeExtractionOpportunityUiRoot.GetComponent<ZombieModeExtractionOpportunityView>();
                if (view != null) view.ReleaseInput();
                BossRushUIKit.PlayCloseAndDestroy(zombieModeExtractionOpportunityUiRoot);
                zombieModeExtractionOpportunityUiRoot = null;
            }
        }

        /// <summary>
        /// 清理准备期对象。两个安全区槽都在此清除：战斗期部署的便携区在波次结束时消失，
        /// 之后由 BeginZombieModePreparation 重新生成带商人的正常安全区；准备期部署的
        /// 便携副槽则随下一波开始一起清除。
        /// </summary>
        internal void CleanupZombieModePreparationObjects(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ClearZombieModeExtractionOpportunityUi();
            TryReleaseZombieModeExtractionCountdownUi();
            ReleaseZombieModeSafeZoneThreatSuppression();
            RecycleZombieModeSafeZoneBoundTemporaryNpcs(runId);
            RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);

            DestroyZombieModeActiveExtractionArea();

            if (runState.ActiveSafeZoneVisual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(runState.ActiveSafeZoneVisual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(runState.ActiveSafeZoneVisual, null); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy ActiveSafeZoneVisual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();
            ClearZombieModePortableSafeZoneSlot();

            runState.ActiveSafeZoneCenter = Vector3.zero;
            runState.ActiveSafeZoneRadius = 0f;
            runState.ActiveSafeZoneActive = false;
            runState.ActiveSafeZonePortable = false;
            runState.PlayerInsideSafeZone = false;
            runState.SafeZoneThreatSuppressed = false;
            runState.LastSafeZoneTickTime = 0f;
            runState.ActiveSafeZoneVisual = null;
            runState.ActiveSafeZoneMapPoi = null;
        }

        private void DestroyZombieModeActiveExtractionArea()
        {
            if (runState.ActiveExtractionArea == null)
            {
                return;
            }

            CountDownArea area = runState.ActiveExtractionArea;
            runState.ActiveExtractionArea = null;
            try { EvacuationCountdownUI.Release(area); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] EvacuationCountdownUI.Release(area) 失败: " + e.Message); }
            try { UnityEngine.Object.Destroy(area.gameObject); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy ExtractionArea 失败: " + e.Message); }
        }

        /// <summary>
        /// 玩家在区内主动攻击丧尸时取消安全区。主槽与便携副槽一并取消，
        /// 不留下“攻击后仍有半个保护区”的暧昧状态。
        /// </summary>
        internal void CancelZombieModeSafeZone(int runId, string reason)
        {
            if (!IsZombieModeRunValid(runId) || !AnyZombieModeSafeZoneActive)
            {
                return;
            }

            ReleaseZombieModeSafeZoneThreatSuppression();
            RecycleZombieModeSafeZoneBoundTemporaryNpcs(runId);
            RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);

            if (runState.ActiveSafeZoneVisual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(runState.ActiveSafeZoneVisual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(runState.ActiveSafeZoneVisual, null); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy cancelled safe-zone visual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();
            ClearZombieModePortableSafeZoneSlot();

            runState.ActiveSafeZoneCenter = Vector3.zero;
            runState.ActiveSafeZoneRadius = 0f;
            runState.ActiveSafeZoneActive = false;
            runState.ActiveSafeZonePortable = false;
            runState.PlayerInsideSafeZone = false;
            runState.SafeZoneThreatSuppressed = false;
            runState.LastSafeZoneTickTime = 0f;
            runState.ActiveSafeZoneVisual = null;
            runState.ActiveSafeZoneMapPoi = null;
            ModBehaviour.DevLog("[ZombieMode] safe-zone cancelled: " + reason);
            owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_SafeZoneCancelled"));
        }

        /// <summary>
        /// 创建安全区。useSecondarySlot=true 时写入便携副槽：与主槽并存、不带商人、
        /// 不回收主槽绑定的服务 NPC，仅供准备期部署便携装置使用。
        /// </summary>
        internal void CreateZombieModeSafeZone(
            int runId,
            bool includeMerchantTerminal = true,
            bool clearBoundServices = true,
            bool portable = false,
            bool useSecondarySlot = false)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            if (useSecondarySlot)
            {
                ClearZombieModePortableSafeZoneSlot();
            }
            else
            {
                ResetZombieModeSafeZoneForReplacement(runId, clearBoundServices);
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 position = player != null ? player.transform.position : Vector3.zero;
            position = ValidateZombieModeSafeZonePosition(position);
            // 共享 disk mesh 替代 CreatePrimitive(Cylinder)（审查 §3.3）。
            // 安全区也复用同一个 disk mesh / material，避免每个准备期都构造 Cylinder primitive。
            GameObject safeZone = CreateZombieModeFlatZoneVisual(
                "ZombieMode_SafeZone",
                position + Vector3.up * 0.03f,
                ZombieModeTuning.SafeZoneRadius,
                0.05f,
                ZombieModeZoneVisuals.SafeZoneColor);
            AttachZombieModeSafeZoneBoundaryVisual(safeZone, position, ZombieModeTuning.SafeZoneRadius);

            ZombieModeSafeZoneController controller = safeZone.AddComponent<ZombieModeSafeZoneController>();
            controller.Initialize(runId);
            if (useSecondarySlot)
            {
                runState.PortableSafeZoneCenter = position;
                runState.PortableSafeZoneRadius = ZombieModeTuning.SafeZoneRadius;
                runState.PortableSafeZoneActive = true;
                runState.PortableSafeZoneVisual = safeZone;
            }
            else
            {
                runState.ActiveSafeZoneCenter = position;
                runState.ActiveSafeZoneRadius = ZombieModeTuning.SafeZoneRadius;
                runState.ActiveSafeZoneActive = true;
                runState.ActiveSafeZonePortable = portable;
                runState.ActiveSafeZoneVisual = safeZone;
            }
            runState.LastSafeZoneTickTime = 0f;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.SafeZone, safeZone, controller, null);
            CreateZombieModeSafeZoneMapPoi(runId, position, useSecondarySlot);
            if (includeMerchantTerminal)
            {
                EnsureZombieModeSafeZoneMerchantTerminal(runId);
            }
            TryRegisterZombieModeShootStealthBreaker(runId);
            // 安全区启用必须立即建立干净边界：普通丧尸直接清除，Boss 只移出边界，
            // 后续由安全区 tick 持续执行物理禁入。
            ClearZombieModeEnemiesInsideActiveSafeZone(runId, "CreateSafeZone");
            TickZombieModeSafeZone();
        }

        private void ResetZombieModeSafeZoneForReplacement(int runId, bool clearBoundServices)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ReleaseZombieModeSafeZoneThreatSuppression();
            if (clearBoundServices)
            {
                RecycleZombieModeSafeZoneBoundTemporaryNpcs(runId);
                RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);
            }

            GameObject oldVisual = runState.ActiveSafeZoneVisual;
            if (oldVisual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(oldVisual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(oldVisual, null); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy replaced safe-zone visual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();

            runState.ActiveSafeZoneCenter = Vector3.zero;
            runState.ActiveSafeZoneRadius = 0f;
            runState.ActiveSafeZoneActive = false;
            runState.ActiveSafeZonePortable = false;
            runState.PlayerInsideSafeZone = false;
            runState.SafeZoneThreatSuppressed = false;
            runState.LastSafeZoneTickTime = 0f;
            runState.ActiveSafeZoneVisual = null;
            runState.ActiveSafeZoneMapPoi = null;
        }

        private void RemoveZombieModeSafeZoneRunOnlyRecord(UnityEngine.Object target)
        {
            if (target == null || runState.RunOnlyObjects == null)
            {
                return;
            }

            for (int i = runState.RunOnlyObjects.Count - 1; i >= 0; i--)
            {
                ZombieModeRunOnlyRecord record = runState.RunOnlyObjects[i];
                if (record == null || record.Kind != ZombieModeRunOnlyObjectKind.SafeZone)
                {
                    continue;
                }

                if (record.GameObject == target || record.Target == target)
                {
                    runState.RunOnlyObjects.RemoveAt(i);
                }
            }
        }

        private void AttachZombieModeSafeZoneBoundaryVisual(GameObject safeZone, Vector3 center, float radius)
        {
            if (safeZone == null || radius <= 0f)
            {
                return;
            }

            GameObject ring = new GameObject("ZombieMode_SafeZone_BoundaryRing");
            ring.transform.position = center + Vector3.up * 0.14f;

            LineRenderer line = ring.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 97;
            line.widthMultiplier = 0.10f;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            // 共享材质（柔边带贴图 + 白色，颜色只走顶点色），不随环销毁，所以不再按环 new Material（审美审查 VA-28）。
            Material lineMaterial = ZombieModeZoneVisuals.GetRingMaterial();
            if (lineMaterial != null)
            {
                line.sharedMaterial = lineMaterial;
            }

            const int segments = 96;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            ring.transform.SetParent(safeZone.transform, true);
            // 压饱和的静态边线，跟着圆盘一起淡入淡出；边线本身不做任何脉冲。挂到父物体之后再交给表现组件（它要按父物体当前的出场缩放校正）。
            ZombieModeZoneVisuals.AttachRing(safeZone, line, ZombieModeZoneVisuals.SafeZoneRingColor);
        }

        private void EnsureZombieModeSafeZoneMerchantTerminal(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            if (FindZombieModeTemporaryNpc("Merchant") == null)
            {
                SpawnZombieModeTemporaryNpc(runId, "Merchant", false);
            }
        }

        private void CreateZombieModeSafeZoneMapPoi(int runId, Vector3 position, bool secondarySlot = false)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            DestroyZombieModeSafeZoneMapPoi(secondarySlot);

            string sceneId = ResolveZombieModeSafeZoneMapSceneId();
            string displayName = L10n.T("BossRush_ZombieMode_Map_SafeZone");
            SimplePointOfInterest poi = null;
            try
            {
                poi = SimplePointOfInterest.Create(
                    position,
                    sceneId,
                    displayName,
                    null,
                    false);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 创建安全区地图标记失败: " + e.Message);
            }

            if (poi == null)
            {
                return;
            }

            poi.Color = ZombieModeZoneVisuals.SafeZoneMapColor;
            poi.ShadowColor = new Color(0.02f, 0.18f, 0.06f, 0.75f);
            poi.ShadowDistance = 1.5f;
            poi.IsArea = true;
            poi.AreaRadius = ZombieModeTuning.SafeZoneRadius;
            poi.Setup(null, displayName, false, sceneId);
            if (secondarySlot)
            {
                runState.PortableSafeZoneMapPoi = poi;
            }
            else
            {
                runState.ActiveSafeZoneMapPoi = poi;
            }
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.SafeZone, poi.gameObject, poi, null);
            ModBehaviour.DevLog("[ZombieMode] 安全区地图标记已创建: sceneId=" + sceneId
                + ", activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                + ", position=" + position
                + ", radius=" + ZombieModeTuning.SafeZoneRadius);
        }

        /// <summary>
        /// 安全区地图标记要写的官方场景 ID。解析口径与 Mode F 撤离点、天空岛序章目标共用
        /// （<see cref="MapPointSceneResolver"/>：先按 buildIndex 反查官方 ID，再退 ActiveSubSceneID，最后才退场景名）；
        /// 本模式额外保留一条「用本局记录的场景名」兜底。
        /// </summary>
        private string ResolveZombieModeSafeZoneMapSceneId()
        {
            try
            {
                string sceneId = MapPointSceneResolver.Resolve();
                if (!string.IsNullOrEmpty(sceneId))
                {
                    return sceneId;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] Resolve safe-zone map scene id failed: " + e.Message);
            }

            return runState.SceneName;
        }

        private void DestroyZombieModeSafeZoneMapPoi(bool secondarySlot = false)
        {
            if (secondarySlot)
            {
                if (runState.PortableSafeZoneMapPoi == null)
                {
                    return;
                }

                RemoveZombieModeSafeZoneRunOnlyRecord(runState.PortableSafeZoneMapPoi);
                try { UnityEngine.Object.Destroy(runState.PortableSafeZoneMapPoi.gameObject); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy PortableSafeZoneMapPoi 失败: " + e.Message); }
                runState.PortableSafeZoneMapPoi = null;
                return;
            }

            if (runState.ActiveSafeZoneMapPoi == null)
            {
                return;
            }

            RemoveZombieModeSafeZoneRunOnlyRecord(runState.ActiveSafeZoneMapPoi);
            try { UnityEngine.Object.Destroy(runState.ActiveSafeZoneMapPoi.gameObject); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy ActiveSafeZoneMapPoi 失败: " + e.Message); }
            runState.ActiveSafeZoneMapPoi = null;
        }

        /// <summary>
        /// 清除便携安全区副槽。副槽不带商人、不绑定服务 NPC，因此不回收 SafeZoneBound NPC，
        /// 也不触碰主槽的任何状态。
        /// </summary>
        private void ClearZombieModePortableSafeZoneSlot()
        {
            GameObject visual = runState.PortableSafeZoneVisual;
            if (visual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(visual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(visual, null); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Destroy portable safe-zone visual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi(true);

            runState.PortableSafeZoneCenter = Vector3.zero;
            runState.PortableSafeZoneRadius = 0f;
            runState.PortableSafeZoneActive = false;
            runState.PortableSafeZoneVisual = null;
            runState.PortableSafeZoneMapPoi = null;
        }

        private Vector3 ValidateZombieModeSafeZonePosition(Vector3 candidate)
        {
            NavMeshHit hit;
            for (int i = 0; i < 8; i++)
            {
                Vector3 probe = i == 0 ? candidate : candidate + Random.insideUnitSphere * ZombieModeTuning.NavMeshSafeZoneRadius;
                probe.y = candidate.y;
                if (NavMesh.SamplePosition(probe, out hit, ZombieModeTuning.NavMeshSafeZoneRadius, NavMesh.AllAreas))
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        Vector3 delta = hit.position - player.transform.position;
                        delta.y = 0f;
                        if (delta.sqrMagnitude > ZombieModeTuning.SafeZoneCenterPlayerRange * ZombieModeTuning.SafeZoneCenterPlayerRange)
                        {
                            continue;
                        }
                    }
                    return hit.position;
                }
            }

            return candidate;
        }

        internal void UpdateZombieModeSafeZoneVisual()
        {
            if (!IsZombieModeActive)
            {
                return;
            }

            UpdateZombieModeSafeZoneSlotVisual(false);
            UpdateZombieModeSafeZoneSlotVisual(true);
        }

        private void UpdateZombieModeSafeZoneSlotVisual(bool secondarySlot)
        {
            GameObject visual = secondarySlot
                ? runState.PortableSafeZoneVisual
                : runState.ActiveSafeZoneVisual;
            if (visual == null)
            {
                return;
            }

            // 最后几秒的警示：圆盘在 ZombieModeZoneVisualFx 里按帧做平滑的暖色呼吸（旧写法是这里 0.2 s 一跳的线性三角波），
            // 边线不动（ZombieModeSafeZoneVisualGuard）。这里只切开关与地图标记色。
            bool warning = runState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds &&
                runState.PreparationTimer > 0f;
            ZombieModeZoneVisuals.SetSafeZoneWarning(visual, warning);
            UpdateZombieModeSafeZoneMapPoiColor(
                warning ? ZombieModeZoneVisuals.SafeZoneWarningMapColor : ZombieModeZoneVisuals.SafeZoneMapColor,
                secondarySlot);
        }

        private void UpdateZombieModeSafeZoneMapPoiColor(Color color, bool secondarySlot = false)
        {
            SimplePointOfInterest poi = secondarySlot
                ? runState.PortableSafeZoneMapPoi
                : runState.ActiveSafeZoneMapPoi;
            if (poi == null)
            {
                return;
            }

            poi.Color = color;
        }

    }
}

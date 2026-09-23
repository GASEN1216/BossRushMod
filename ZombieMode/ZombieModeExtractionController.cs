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
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private GameObject zombieModeExtractionOpportunityUiRoot;

        public bool CanUseZombieModeBeacon()
        {
            if (!IsZombieModeActive)
            {
                return false;
            }

            return !zombieModeRunState.BeaconChanneling &&
                   !zombieModeRunState.ExtractionChanneling &&
                   ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase);
        }

        public bool CanUseZombieModePortableSafeZoneDevice()
        {
            return IsZombieModeActive &&
                   !zombieModeRunState.BeaconChanneling &&
                   !zombieModeRunState.ExtractionChanneling &&
                   ZombieModePhaseGuards.AllowsPortableSafeZoneDeployment(zombieModeRunState.CombatPhase);
        }

        public string GetZombieModePortableSafeZoneUnavailableReasonKey()
        {
            return "BossRush_ZombieMode_Notify_PortableSafeZoneUnavailable";
        }

        /// <summary>
        /// 部署便携安全区。战斗中部署替换主槽，波次结束时随准备期清理消失，
        /// 之后正常流程会重新生成带商人的安全区；准备期部署则写入独立副槽，
        /// 与正常安全区（及其商人）并存，下一波开始时一起清除。
        /// </summary>
        public bool TryUseZombieModePortableSafeZoneDevice()
        {
            if (!CanUseZombieModePortableSafeZoneDevice())
            {
                NotificationText.Push(L10n.T(GetZombieModePortableSafeZoneUnavailableReasonKey()));
                return false;
            }

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                CreateZombieModeSafeZone(zombieModeRunState.RunId, false, true, true);
            }
            else
            {
                CreateZombieModeSafeZone(zombieModeRunState.RunId, false, false, true, true);
            }
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_PortableSafeZoneDeployed"));
            return true;
        }

        public string GetZombieModeBeaconUnavailableReasonKey()
        {
            if (!IsZombieModeActive)
            {
                return "BossRush_ZombieMode_Notify_BeaconNotZombieMode";
            }

            if (zombieModeRunState.ExtractionChanneling)
            {
                return "BossRush_ZombieMode_Notify_BeaconExtractionLocked";
            }

            return "BossRush_ZombieMode_Notify_BeaconNotPreparation";
        }

        public bool TryUseZombieModeBeacon()
        {
            if (!CanUseZombieModeBeacon())
            {
                NotificationText.Push(L10n.T(GetZombieModeBeaconUnavailableReasonKey()));
                return false;
            }

            zombieModeRunState.BeaconChanneling = true;
            zombieModeRunState.BeaconChannelStartTime = Time.unscaledTime;
            StartZombieModeCoroutine(ZombieModeBeaconChannelCoroutine(zombieModeRunState.RunId), zombieModeRunState.RunId);
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconChannelStarted"));
            return true;
        }

        private IEnumerator ZombieModeBeaconChannelCoroutine(int runId)
        {
            float remaining = zombieModeRunState.BeaconChannelDuration;
            while (remaining > 0f)
            {
                if (!IsZombieModeRunValid(runId))
                {
                    yield break;
                }

                if (zombieModeRunState.ExtractionChanneling || !ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase))
                {
                    zombieModeRunState.BeaconChanneling = false;
                    zombieModeRunState.BeaconChannelStartTime = 0f;
                    yield break;
                }

                if (!IsZombieModeRuntimePaused())
                {
                    remaining -= Time.unscaledDeltaTime;
                }
                else
                {
                    // 暂停时起点跟着顺延：HUD 信标读条按「当前时刻 - 起点」算已引导时长（审美审查 UC-17）。
                    zombieModeRunState.BeaconChannelStartTime += Time.unscaledDeltaTime;
                }

                yield return null;
            }

            if (!IsZombieModeRunValid(runId) || !zombieModeRunState.BeaconChanneling)
            {
                yield break;
            }

            zombieModeRunState.BeaconChanneling = false;
            zombieModeRunState.BeaconChannelStartTime = 0f;
            StartZombieModeWave(runId);
        }

        private void ShowZombieModeExtractionOpportunityUi(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ClearZombieModeExtractionOpportunityUi();
            zombieModeExtractionOpportunityUiRoot = new GameObject("ZombieMode_ExtractionOpportunity");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, zombieModeExtractionOpportunityUiRoot, zombieModeExtractionOpportunityUiRoot, null);
            ZombieModeExtractionOpportunityView view = zombieModeExtractionOpportunityUiRoot.AddComponent<ZombieModeExtractionOpportunityView>();
            view.Initialize(runId, this);
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_ExtractionOpen"));
        }

        public void ContinueZombieModeAfterExtractionOpportunity(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity)
            {
                return;
            }

            CloseZombieModeExtractionOpportunityAndContinue(runId);
        }

        private void CloseZombieModeExtractionOpportunityAndContinue(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity)
            {
                return;
            }

            ClearZombieModeExtractionOpportunityUi();
            zombieModeRunState.ExtractionChanneling = false;
            DestroyZombieModeActiveExtractionArea();
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.Preparation;
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_PreparationNextWave"));
        }

        public void StartZombieModeExtractionFromUi(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            StartZombieModeExtraction(runId);
        }

        private void StartZombieModeExtraction(int runId)
        {
            if (!IsZombieModeRunValid(runId) ||
                zombieModeRunState.ExtractionChanneling ||
                zombieModeRunState.ExtractionSuccessHandled ||
                !ZombieModePhaseGuards.AllowsExtraction(zombieModeRunState.CombatPhase))
            {
                return;
            }

            if (zombieModeRunState.BeaconChanneling)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_ExtractionBeaconLocked"));
                return;
            }

            EnsureZombieModeExtractionArea(runId);
            if (zombieModeRunState.ActiveExtractionArea == null)
            {
                return;
            }

            ClearZombieModeExtractionOpportunityUi();
            zombieModeRunState.ExtractionChanneling = true;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.ExtractionOpportunity;
            try { EvacuationCountdownUI.Request(zombieModeRunState.ActiveExtractionArea); } catch (System.Exception e) { DevLog("[ZombieMode] EvacuationCountdownUI.Request 失败: " + e.Message); }
            StartZombieModeCoroutine(ZombieModeExtractionCountdownCoroutine(runId), runId);
            ShowBigBanner(string.Format(
                L10n.T("BossRush_ZombieMode_Banner_ExtractionCountdown"),
                Mathf.CeilToInt(ZombieModeTuning.ExtractionCountdownSeconds)));
        }

        private void CompleteZombieModeExtractionSuccess(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.ExtractionSuccessHandled)
            {
                return;
            }

            if (!SettleZombieModeExtractionCashShell())
            {
                zombieModeRunState.ExtractionChanneling = false;
                zombieModeRunState.BeaconChanneling = false;
                zombieModeRunState.CombatPhase = ZombieModeCombatPhase.ExtractionOpportunity;
                TryReleaseZombieModeExtractionCountdownUi();
                NotificationText.Push(L10n.T(
                    "净化点结算失败，奖励已保留；请稍后重新撤离。",
                    "Purification payout failed; your reward was preserved. Try extracting again later."));
                ShowZombieModeExtractionOpportunityUi(runId);
                return;
            }

            zombieModeRunState.ExtractionSuccessHandled = true;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.SuccessExit;
            zombieModeRunState.ExtractionChanneling = false;
            zombieModeRunState.BeaconChanneling = false;
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Settle_SuccessTitle"));

            // 通知战役契约（未启用时零成本早返）。必须早于场景切换：
            // 回基地后 LifecyclePhase 会复位，武装判定就认不出这是丧尸模式了。
            NotifyCampaignZombieExtracted();

            bool dispatched = TryDispatchZombieModeExtractionSuccess(zombieModeRunState.ActiveExtractionArea);
            if (!dispatched)
            {
                TryReleaseZombieModeExtractionCountdownUi();
                TryNotifyZombieModeExtraction();
                TryLoadBaseSceneAfterZombieModeExtraction();
            }

            CleanupZombieModeForSceneChange(ZombieModeFailureReason.SuccessfulExtraction);
        }

        private bool SettleZombieModeExtractionCashShell()
        {
            if (zombieModeRunState.PurificationPoints <= 0)
            {
                return true;
            }

            long cashGain = zombieModeRunState.PurificationPoints;
            try
            {
                if (EconomyManager.Add(cashGain))
                {
                    NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_ExtractionCash"), cashGain.ToString("N0")));
                    zombieModeRunState.PurificationPoints = 0;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 净化点现金结算异常，保留欠账: " + e.Message);
                return false;
            }

            DevLog("[ZombieMode] 净化点现金结算失败，保留欠账: " + cashGain);
            return false;
        }

        private IEnumerator ZombieModeExtractionCountdownCoroutine(int runId)
        {
            CountDownArea area = zombieModeRunState.ActiveExtractionArea;
            float remaining = ZombieModeTuning.ExtractionCountdownSeconds;
            while (remaining > 0f)
            {
                if (!IsZombieModeRunValid(runId) ||
                    zombieModeRunState.ActiveExtractionArea != area ||
                    !zombieModeRunState.ExtractionChanneling ||
                    zombieModeRunState.ExtractionSuccessHandled)
                {
                    yield break;
                }

                if (!IsPlayerInsideZombieModeExtractionArea(area))
                {
                    zombieModeRunState.ExtractionChanneling = false;
                    TryReleaseZombieModeExtractionCountdownUi();
                    if (zombieModeRunState.PreparationTimer <= 0f)
                    {
                        StartZombieModeWave(zombieModeRunState.RunId);
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
                zombieModeRunState.ActiveExtractionArea == area &&
                zombieModeRunState.ExtractionChanneling &&
                !zombieModeRunState.ExtractionSuccessHandled)
            {
                CompleteZombieModeExtractionSuccess(runId);
            }
        }

        private void EnsureZombieModeExtractionArea(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.ActiveExtractionArea != null)
            {
                return;
            }

            CharacterMainControl mainPlayer = CharacterMainControl.Main;
            Vector3 position = zombieModeRunState.ActiveSafeZoneActive
                ? zombieModeRunState.ActiveSafeZoneCenter
                : (mainPlayer != null ? mainPlayer.transform.position : Vector3.zero);

            ModeExtractionPointRequest request = new ModeExtractionPointRequest();
            request.ObjectName = "ZombieMode_ExtractionPoint";
            request.Position = position + Vector3.up * 0.05f;
            request.CountdownSeconds = ZombieModeTuning.ExtractionCountdownSeconds;
            request.FallbackTriggerRadius = ZombieModeTuning.ExtractionAreaTriggerRadius;
            request.LogPrefix = "[ZombieMode]";
            request.IsCurrentArea = delegate(CountDownArea area)
            {
                return IsZombieModeRunValid(runId) && zombieModeRunState.ActiveExtractionArea == area;
            };
            request.OnSucceed = delegate
            {
                if (IsZombieModeRunValid(runId) &&
                    zombieModeRunState.ActiveExtractionArea != null &&
                    !zombieModeRunState.ExtractionSuccessHandled)
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

            zombieModeRunState.ActiveExtractionArea = result.CountDownArea;
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
                if (zombieModeRunState.ActiveExtractionArea != null)
                {
                    EvacuationCountdownUI.Release(zombieModeRunState.ActiveExtractionArea);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] EvacuationCountdownUI.Release 失败: " + e.Message);
            }
        }

        private void TryNotifyZombieModeExtraction()
        {
            CharacterMainControl mainPlayer = CharacterMainControl.Main;
            Vector3 position = zombieModeRunState.ActiveExtractionArea != null
                ? zombieModeRunState.ActiveExtractionArea.transform.position
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
                DevLog("[ZombieMode] [WARNING] NotifyEvacuated 失败: " + e.Message);
            }
        }

        private void TryLoadBaseSceneAfterZombieModeExtraction()
        {
            try
            {
                if (SceneLoader.Instance == null)
                {
                    DevLog("[ZombieMode] [WARNING] TryLoadBaseSceneAfterZombieModeExtraction: SceneLoader.Instance 为 null");
                    return;
                }

                UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] 撤离后回主场景失败: " + e.Message);
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
                DevLog("[ZombieMode] [WARNING] TryDispatchZombieModeExtractionSuccess 失败: " + e.Message);
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
        private void CleanupZombieModePreparationObjects(int runId)
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

            if (zombieModeRunState.ActiveSafeZoneVisual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(zombieModeRunState.ActiveSafeZoneVisual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(zombieModeRunState.ActiveSafeZoneVisual, null); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ActiveSafeZoneVisual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();
            ClearZombieModePortableSafeZoneSlot();

            zombieModeRunState.ActiveSafeZoneCenter = Vector3.zero;
            zombieModeRunState.ActiveSafeZoneRadius = 0f;
            zombieModeRunState.ActiveSafeZoneActive = false;
            zombieModeRunState.ActiveSafeZonePortable = false;
            zombieModeRunState.PlayerInsideSafeZone = false;
            zombieModeRunState.SafeZoneThreatSuppressed = false;
            zombieModeRunState.LastSafeZoneTickTime = 0f;
            zombieModeRunState.ActiveSafeZoneVisual = null;
            zombieModeRunState.ActiveSafeZoneMapPoi = null;
        }

        private void DestroyZombieModeActiveExtractionArea()
        {
            if (zombieModeRunState.ActiveExtractionArea == null)
            {
                return;
            }

            CountDownArea area = zombieModeRunState.ActiveExtractionArea;
            zombieModeRunState.ActiveExtractionArea = null;
            try { EvacuationCountdownUI.Release(area); } catch (System.Exception e) { DevLog("[ZombieMode] EvacuationCountdownUI.Release(area) 失败: " + e.Message); }
            try { Destroy(area.gameObject); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ExtractionArea 失败: " + e.Message); }
        }

        /// <summary>
        /// 玩家在区内主动攻击丧尸时取消安全区。主槽与便携副槽一并取消，
        /// 不留下“攻击后仍有半个保护区”的暧昧状态。
        /// </summary>
        private void CancelZombieModeSafeZone(int runId, string reason)
        {
            if (!IsZombieModeRunValid(runId) || !AnyZombieModeSafeZoneActive)
            {
                return;
            }

            ReleaseZombieModeSafeZoneThreatSuppression();
            RecycleZombieModeSafeZoneBoundTemporaryNpcs(runId);
            RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);

            if (zombieModeRunState.ActiveSafeZoneVisual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(zombieModeRunState.ActiveSafeZoneVisual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(zombieModeRunState.ActiveSafeZoneVisual, null); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy cancelled safe-zone visual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();
            ClearZombieModePortableSafeZoneSlot();

            zombieModeRunState.ActiveSafeZoneCenter = Vector3.zero;
            zombieModeRunState.ActiveSafeZoneRadius = 0f;
            zombieModeRunState.ActiveSafeZoneActive = false;
            zombieModeRunState.ActiveSafeZonePortable = false;
            zombieModeRunState.PlayerInsideSafeZone = false;
            zombieModeRunState.SafeZoneThreatSuppressed = false;
            zombieModeRunState.LastSafeZoneTickTime = 0f;
            zombieModeRunState.ActiveSafeZoneVisual = null;
            zombieModeRunState.ActiveSafeZoneMapPoi = null;
            DevLog("[ZombieMode] safe-zone cancelled: " + reason);
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_SafeZoneCancelled"));
        }

        /// <summary>
        /// 创建安全区。useSecondarySlot=true 时写入便携副槽：与主槽并存、不带商人、
        /// 不回收主槽绑定的服务 NPC，仅供准备期部署便携装置使用。
        /// </summary>
        private void CreateZombieModeSafeZone(
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
                zombieModeRunState.PortableSafeZoneCenter = position;
                zombieModeRunState.PortableSafeZoneRadius = ZombieModeTuning.SafeZoneRadius;
                zombieModeRunState.PortableSafeZoneActive = true;
                zombieModeRunState.PortableSafeZoneVisual = safeZone;
            }
            else
            {
                zombieModeRunState.ActiveSafeZoneCenter = position;
                zombieModeRunState.ActiveSafeZoneRadius = ZombieModeTuning.SafeZoneRadius;
                zombieModeRunState.ActiveSafeZoneActive = true;
                zombieModeRunState.ActiveSafeZonePortable = portable;
                zombieModeRunState.ActiveSafeZoneVisual = safeZone;
            }
            zombieModeRunState.LastSafeZoneTickTime = 0f;
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

            GameObject oldVisual = zombieModeRunState.ActiveSafeZoneVisual;
            if (oldVisual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(oldVisual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(oldVisual, null); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy replaced safe-zone visual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();

            zombieModeRunState.ActiveSafeZoneCenter = Vector3.zero;
            zombieModeRunState.ActiveSafeZoneRadius = 0f;
            zombieModeRunState.ActiveSafeZoneActive = false;
            zombieModeRunState.ActiveSafeZonePortable = false;
            zombieModeRunState.PlayerInsideSafeZone = false;
            zombieModeRunState.SafeZoneThreatSuppressed = false;
            zombieModeRunState.LastSafeZoneTickTime = 0f;
            zombieModeRunState.ActiveSafeZoneVisual = null;
            zombieModeRunState.ActiveSafeZoneMapPoi = null;
        }

        private void RemoveZombieModeSafeZoneRunOnlyRecord(UnityEngine.Object target)
        {
            if (target == null || zombieModeRunState.RunOnlyObjects == null)
            {
                return;
            }

            for (int i = zombieModeRunState.RunOnlyObjects.Count - 1; i >= 0; i--)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null || record.Kind != ZombieModeRunOnlyObjectKind.SafeZone)
                {
                    continue;
                }

                if (record.GameObject == target || record.Target == target)
                {
                    zombieModeRunState.RunOnlyObjects.RemoveAt(i);
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
                DevLog("[ZombieMode] 创建安全区地图标记失败: " + e.Message);
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
                zombieModeRunState.PortableSafeZoneMapPoi = poi;
            }
            else
            {
                zombieModeRunState.ActiveSafeZoneMapPoi = poi;
            }
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.SafeZone, poi.gameObject, poi, null);
            DevLog("[ZombieMode] 安全区地图标记已创建: sceneId=" + sceneId
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
                DevLog("[ZombieMode] Resolve safe-zone map scene id failed: " + e.Message);
            }

            return zombieModeRunState.SceneName;
        }

        private void DestroyZombieModeSafeZoneMapPoi(bool secondarySlot = false)
        {
            if (secondarySlot)
            {
                if (zombieModeRunState.PortableSafeZoneMapPoi == null)
                {
                    return;
                }

                RemoveZombieModeSafeZoneRunOnlyRecord(zombieModeRunState.PortableSafeZoneMapPoi);
                try { Destroy(zombieModeRunState.PortableSafeZoneMapPoi.gameObject); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy PortableSafeZoneMapPoi 失败: " + e.Message); }
                zombieModeRunState.PortableSafeZoneMapPoi = null;
                return;
            }

            if (zombieModeRunState.ActiveSafeZoneMapPoi == null)
            {
                return;
            }

            RemoveZombieModeSafeZoneRunOnlyRecord(zombieModeRunState.ActiveSafeZoneMapPoi);
            try { Destroy(zombieModeRunState.ActiveSafeZoneMapPoi.gameObject); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ActiveSafeZoneMapPoi 失败: " + e.Message); }
            zombieModeRunState.ActiveSafeZoneMapPoi = null;
        }

        /// <summary>
        /// 清除便携安全区副槽。副槽不带商人、不绑定服务 NPC，因此不回收 SafeZoneBound NPC，
        /// 也不触碰主槽的任何状态。
        /// </summary>
        private void ClearZombieModePortableSafeZoneSlot()
        {
            GameObject visual = zombieModeRunState.PortableSafeZoneVisual;
            if (visual != null)
            {
                RemoveZombieModeSafeZoneRunOnlyRecord(visual);
                try { ZombieModeZoneVisuals.FadeOutAndDestroy(visual, null); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy portable safe-zone visual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi(true);

            zombieModeRunState.PortableSafeZoneCenter = Vector3.zero;
            zombieModeRunState.PortableSafeZoneRadius = 0f;
            zombieModeRunState.PortableSafeZoneActive = false;
            zombieModeRunState.PortableSafeZoneVisual = null;
            zombieModeRunState.PortableSafeZoneMapPoi = null;
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

        public void UpdateZombieModeSafeZoneVisual()
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
                ? zombieModeRunState.PortableSafeZoneVisual
                : zombieModeRunState.ActiveSafeZoneVisual;
            if (visual == null)
            {
                return;
            }

            // 最后几秒的警示：圆盘在 ZombieModeZoneVisualFx 里按帧做平滑的暖色呼吸（旧写法是这里 0.2 s 一跳的线性三角波），
            // 边线不动（ZombieModeSafeZoneVisualGuard）。这里只切开关与地图标记色。
            bool warning = zombieModeRunState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds &&
                zombieModeRunState.PreparationTimer > 0f;
            ZombieModeZoneVisuals.SetSafeZoneWarning(visual, warning);
            UpdateZombieModeSafeZoneMapPoiColor(
                warning ? ZombieModeZoneVisuals.SafeZoneWarningMapColor : ZombieModeZoneVisuals.SafeZoneMapColor,
                secondarySlot);
        }

        private void UpdateZombieModeSafeZoneMapPoiColor(Color color, bool secondarySlot = false)
        {
            SimplePointOfInterest poi = secondarySlot
                ? zombieModeRunState.PortableSafeZoneMapPoi
                : zombieModeRunState.ActiveSafeZoneMapPoi;
            if (poi == null)
            {
                return;
            }

            poi.Color = color;
        }
    }

    public sealed class ZombieModeExtractionController : MonoBehaviour
    {
        public int RunId;
        public bool IsChanneling;

        public void Initialize(int runId)
        {
            RunId = runId;
            IsChanneling = false;
        }
    }

    public sealed class ZombieModeExtractionOpportunityView : MonoBehaviour
    {
        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;
        private bool decided;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
            ClaimInputAndPause();
        }

        /// <summary>
        /// 两张后果卡 + 一个主按钮（审美审查 UC-06）：旧版是两块 256×108 的半透明绿 / 橙平涂大按钮，没有任何说明。
        /// 撤离是本屏唯一的主操作（AccentFill），继续战斗走次级按钮；卡片左侧强调条区分两条路。
        /// </summary>
        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ZombieModal;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(680f, 340f),
                ZombieModeUIHelper.WarningColor);

            // 标题行不再垫底色条：直角色条会在 14px 圆角外露出方角（UC-21），分区交给下面的分隔线。
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText(
                "Title", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_Title"), 28,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -34f), new Vector2(-48f, 48f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            ZombieModeUIHelper.CreateSeparator(
                "HeaderDivider",
                panel.transform,
                Vector2.up,
                Vector2.one,
                new Vector2(0f, -68f),
                2f,
                ZombieModeUIHelper.WarningColor);

            int points = owner != null ? owner.GetZombieModePurificationPoints(runId) : 0;
            CreateChoiceCard(panel.transform, "Extract", new Vector2(-152f, -38f), true,
                L10n.T("BossRush_ZombieMode_Extraction_ExtractNow"),
                string.Format(L10n.T("BossRush_ZombieMode_Extraction_ExtractDesc"),
                    Mathf.CeilToInt(ZombieModeTuning.ExtractionCountdownSeconds), points.ToString("N0")));
            CreateChoiceCard(panel.transform, "Continue", new Vector2(152f, -38f), false,
                L10n.T("BossRush_ZombieMode_Extraction_Continue"),
                L10n.T("BossRush_ZombieMode_Extraction_ContinueDesc"));
            BossRushUI.PlayOpenAnimation(panel);
        }

        private void CreateChoiceCard(Transform parent, string name, Vector2 position, bool extract, string title, string description)
        {
            GameObject card = BossRushUI.CreateCard(name + "Card", parent, position, new Vector2(288f, 204f),
                BossRushUIColors.SurfaceRaised, extract ? BossRushUIColors.Accent : BossRushUIColors.Warning);
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", card.transform, title, 20,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -26f), new Vector2(-40f, 30f),
                TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            ZombieModeUIHelper.CreateText("Desc", card.transform, description, 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -82f), new Vector2(-40f, 72f),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);

            Button button = ZombieModeUIHelper.CreateButton(
                name,
                card.transform,
                title,
                new Vector2(0.5f, 0f),
                new Vector2(0f, 34f),
                new Vector2(236f, 42f),
                extract ? BossRushUIColors.AccentFill : BossRushUIColors.SurfaceRaised,
                17,
                new Vector2(220f, 36f),
                delegate
                {
                    if (owner == null || decided)
                    {
                        return;
                    }

                    decided = true;
                    if (extract)
                    {
                        RestoreInputState();
                        owner.StartZombieModeExtractionFromUi(runId);
                    }
                    else
                    {
                        RestoreInputState();
                        owner.ContinueZombieModeAfterExtractionOpportunity(runId);
                    }
                    // 被宿主拒绝（例如信标引导中）时页面没关：放开防连点，允许再选。
                    decided = GetComponent<BossRushUICloseAnimation>() != null;
                },
                true);
            if (!extract)
            {
                BossRushUIKit.StyleSecondaryButton(button);
            }
            BossRushUIEntranceAnimation.Play(card, extract ? 0.06f : 0.11f, 0.22f, 12f);
        }

        /// <summary>关闭前由宿主先还输入（UC-07：输入与时间流速当帧恢复，淡出只是表现）。</summary>
        internal void ReleaseInput()
        {
            RestoreInputState();
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "ExtractionOpportunity");
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

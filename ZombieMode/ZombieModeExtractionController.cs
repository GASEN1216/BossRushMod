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
            if (!IsZombieModeActive || zombieModeRunState.LifecyclePhase != ZombieModeLifecyclePhase.Active)
            {
                return false;
            }

            return !zombieModeRunState.BeaconChanneling &&
                   !zombieModeRunState.ExtractionChanneling &&
                   ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase);
        }

        public bool TryUseZombieModeBeacon()
        {
            if (!CanUseZombieModeBeacon())
            {
                if (zombieModeRunState.ExtractionChanneling)
                {
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconExtractionLocked"));
                }
                else
                {
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconNotPreparation"));
                }
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

            zombieModeRunState.ExtractionSuccessHandled = true;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.SuccessExit;
            zombieModeRunState.ExtractionChanneling = false;
            zombieModeRunState.BeaconChanneling = false;
            SettleZombieModeExtractionCashShell();
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Settle_SuccessTitle"));

            bool dispatched = TryDispatchZombieModeExtractionSuccess(zombieModeRunState.ActiveExtractionArea);
            if (!dispatched)
            {
                TryReleaseZombieModeExtractionCountdownUi();
                TryNotifyZombieModeExtraction();
                TryLoadBaseSceneAfterZombieModeExtraction();
            }

            CleanupZombieModeForSceneChange(ZombieModeFailureReason.SuccessfulExtraction);
        }

        private void SettleZombieModeExtractionCashShell()
        {
            if (zombieModeRunState.PurificationPoints <= 0)
            {
                return;
            }

            long cashGain = zombieModeRunState.PurificationPoints;
            if (EconomyManager.Add(cashGain))
            {
                NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_ExtractionCash"), cashGain.ToString("N0")));
            }
            zombieModeRunState.PurificationPoints = 0;
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

            Vector3 position = zombieModeRunState.ActiveSafeZoneActive
                ? zombieModeRunState.ActiveSafeZoneCenter
                : (CharacterMainControl.Main != null ? CharacterMainControl.Main.transform.position : Vector3.zero);

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
            Vector3 position = zombieModeRunState.ActiveExtractionArea != null
                ? zombieModeRunState.ActiveExtractionArea.transform.position
                : (CharacterMainControl.Main != null ? CharacterMainControl.Main.transform.position : Vector3.zero);
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
                Destroy(zombieModeExtractionOpportunityUiRoot);
                zombieModeExtractionOpportunityUiRoot = null;
            }
        }

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

            DestroyZombieModeActiveExtractionArea();

            if (zombieModeRunState.ActiveSafeZoneVisual != null)
            {
                try { Destroy(zombieModeRunState.ActiveSafeZoneVisual); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ActiveSafeZoneVisual 失败: " + e.Message); }
            }
            DestroyZombieModeSafeZoneMapPoi();

            zombieModeRunState.ActiveSafeZoneCenter = Vector3.zero;
            zombieModeRunState.ActiveSafeZoneRadius = 0f;
            zombieModeRunState.ActiveSafeZoneActive = false;
            zombieModeRunState.SafeZoneStealthBroken = false;
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

        private void BreakZombieModeSafeZoneStealth(int runId)
        {
            if (!IsZombieModeRunValid(runId) || !zombieModeRunState.ActiveSafeZoneActive || zombieModeRunState.SafeZoneStealthBroken)
            {
                return;
            }

            zombieModeRunState.SafeZoneStealthBroken = true;
            UpdateZombieModeSafeZoneVisual();
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_StealthBroken"));
        }

        private void CreateZombieModeSafeZone(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
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
                new Color(0.18f, 0.78f, 0.32f, 0.45f));

            ZombieModeSafeZoneController controller = safeZone.AddComponent<ZombieModeSafeZoneController>();
            controller.Initialize(runId);
            zombieModeRunState.ActiveSafeZoneCenter = position;
            zombieModeRunState.ActiveSafeZoneRadius = ZombieModeTuning.SafeZoneRadius;
            zombieModeRunState.ActiveSafeZoneActive = true;
            zombieModeRunState.LastSafeZoneTickTime = 0f;
            zombieModeRunState.ActiveSafeZoneVisual = safeZone;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.SafeZone, safeZone, controller, null);
            CreateZombieModeSafeZoneMapPoi(runId, position);
            EnsureZombieModeSafeZoneMerchantTerminal(runId);
            TryRegisterZombieModeShootStealthBreaker(runId);
            TickZombieModeSafeZone();
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

        private void CreateZombieModeSafeZoneMapPoi(int runId, Vector3 position)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            DestroyZombieModeSafeZoneMapPoi();

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

            poi.Color = new Color(0.18f, 0.78f, 0.32f, 0.75f);
            poi.ShadowColor = new Color(0.02f, 0.18f, 0.06f, 0.75f);
            poi.ShadowDistance = 1.5f;
            poi.IsArea = true;
            poi.AreaRadius = ZombieModeTuning.SafeZoneRadius;
            poi.Setup(null, displayName, false, sceneId);
            zombieModeRunState.ActiveSafeZoneMapPoi = poi;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.SafeZone, poi.gameObject, poi, null);
            DevLog("[ZombieMode] 安全区地图标记已创建: sceneId=" + sceneId
                + ", activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                + ", position=" + position
                + ", radius=" + ZombieModeTuning.SafeZoneRadius);
        }

        private string ResolveZombieModeSafeZoneMapSceneId()
        {
            try
            {
                UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                string sceneId = SceneInfoCollection.GetSceneID(activeScene.buildIndex);
                if (!string.IsNullOrEmpty(sceneId))
                {
                    return sceneId;
                }

                if (!string.IsNullOrEmpty(activeScene.name))
                {
                    return activeScene.name;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] Resolve safe-zone map scene id failed: " + e.Message);
            }

            if (!string.IsNullOrEmpty(MultiSceneCore.ActiveSubSceneID))
            {
                return MultiSceneCore.ActiveSubSceneID;
            }

            return zombieModeRunState.SceneName;
        }

        private void DestroyZombieModeSafeZoneMapPoi()
        {
            if (zombieModeRunState.ActiveSafeZoneMapPoi == null)
            {
                return;
            }

            try { Destroy(zombieModeRunState.ActiveSafeZoneMapPoi.gameObject); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ActiveSafeZoneMapPoi 失败: " + e.Message); }
            zombieModeRunState.ActiveSafeZoneMapPoi = null;
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
            if (!IsZombieModeActive || zombieModeRunState.ActiveSafeZoneVisual == null)
            {
                return;
            }

            Renderer renderer = zombieModeRunState.ActiveSafeZoneVisual.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (zombieModeRunState.SafeZoneStealthBroken)
            {
                SetZombieModeRendererColor(renderer, new Color(0.85f, 0.42f, 0.20f, 0.40f));
                UpdateZombieModeSafeZoneMapPoiColor(new Color(0.85f, 0.42f, 0.20f, 0.75f));
                return;
            }

            if (zombieModeRunState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds &&
                zombieModeRunState.PreparationTimer > 0f)
            {
                float flash = Mathf.PingPong(Time.unscaledTime / ZombieModeTuning.SafeZoneFlashCycleSeconds, 1f);
                float alpha = Mathf.Lerp(0.15f, 0.55f, flash);
                SetZombieModeRendererColor(renderer, new Color(0.92f, 0.72f, 0.18f, alpha));
                UpdateZombieModeSafeZoneMapPoiColor(new Color(0.92f, 0.72f, 0.18f, 0.75f));
                return;
            }

            SetZombieModeRendererColor(renderer, new Color(0.18f, 0.78f, 0.32f, 0.45f));
            UpdateZombieModeSafeZoneMapPoiColor(new Color(0.18f, 0.78f, 0.32f, 0.75f));
        }

        private void UpdateZombieModeSafeZoneMapPoiColor(Color color)
        {
            if (zombieModeRunState.ActiveSafeZoneMapPoi == null)
            {
                return;
            }

            zombieModeRunState.ActiveSafeZoneMapPoi.Color = color;
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

            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(540f, 260f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.86f);
            ZombieModeUIHelper.CreateText("Title", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_Title"), 28, new Vector2(0f, 80f), new Vector2(500f, 60f), TextAlignmentOptions.Center, Color.white);
            CreateButton("Extract", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_ExtractNow"), new Vector2(-130f, -30f), true);
            CreateButton("Continue", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_Continue"), new Vector2(130f, -30f), false);
        }

        private void CreateButton(string name, Transform parent, string text, Vector2 position, bool extract)
        {
            ZombieModeUIHelper.CreateButton(
                name,
                parent,
                text,
                new Vector2(0.5f, 0.5f),
                position,
                new Vector2(210f, 78f),
                extract ? new Color(0.18f, 0.36f, 0.22f, 0.95f) : new Color(0.36f, 0.24f, 0.18f, 0.95f),
                22,
                new Vector2(200f, 68f),
                delegate
                {
                    if (owner == null)
                    {
                        return;
                    }

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
                },
                true);
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

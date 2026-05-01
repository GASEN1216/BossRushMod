using System.Collections;
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
            float deadline = Time.unscaledTime + zombieModeRunState.BeaconChannelDuration;
            while (Time.unscaledTime < deadline)
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

            ClearZombieModeExtractionOpportunityUi();
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
            TryReleaseZombieModeExtractionCountdownUi();
            TryNotifyZombieModeExtraction();
            SettleZombieModeExtractionCashShell();
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Settle_SuccessTitle"));
            CleanupZombieModeForSceneChange(ZombieModeFailureReason.SuccessfulExtraction);
            try
            {
                if (SceneLoader.Instance != null)
                {
                    UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] 撤离后回主场景失败: " + e.Message);
            }
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
            float deadline = Time.unscaledTime + ZombieModeTuning.ExtractionCountdownSeconds;
            while (Time.unscaledTime < deadline)
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
            try
            {
                if (LevelManager.Instance == null)
                {
                    return;
                }

                Vector3 position = zombieModeRunState.ActiveExtractionArea != null
                    ? zombieModeRunState.ActiveExtractionArea.transform.position
                    : (CharacterMainControl.Main != null ? CharacterMainControl.Main.transform.position : Vector3.zero);
                EvacuationInfo info = new EvacuationInfo(MultiSceneCore.ActiveSubSceneID, position);
                LevelManager.Instance.NotifyEvacuated(info);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] NotifyEvacuated 失败: " + e.Message);
            }
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

            if (zombieModeRunState.ActiveExtractionArea != null)
            {
                CountDownArea area = zombieModeRunState.ActiveExtractionArea;
                zombieModeRunState.ActiveExtractionArea = null;
                try { EvacuationCountdownUI.Release(area); } catch (System.Exception e) { DevLog("[ZombieMode] EvacuationCountdownUI.Release(area) 失败: " + e.Message); }
                try { Destroy(area.gameObject); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ExtractionArea 失败: " + e.Message); }
            }

            if (zombieModeRunState.ActiveSafeZoneVisual != null)
            {
                try { Destroy(zombieModeRunState.ActiveSafeZoneVisual); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy ActiveSafeZoneVisual 失败: " + e.Message); }
            }

            zombieModeRunState.ActiveSafeZoneCenter = Vector3.zero;
            zombieModeRunState.ActiveSafeZoneRadius = 0f;
            zombieModeRunState.ActiveSafeZoneActive = false;
            zombieModeRunState.SafeZoneStealthBroken = false;
            zombieModeRunState.PlayerInsideSafeZone = false;
            zombieModeRunState.SafeZoneThreatSuppressed = false;
            zombieModeRunState.LastSafeZoneTickTime = 0f;
            zombieModeRunState.ActiveSafeZoneVisual = null;
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
            GameObject safeZone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            safeZone.name = "ZombieMode_SafeZone";
            safeZone.transform.position = position + Vector3.up * 0.03f;
            safeZone.transform.localScale = new Vector3(ZombieModeTuning.SafeZoneRadius * 2f, 0.05f, ZombieModeTuning.SafeZoneRadius * 2f);
            Collider collider = safeZone.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = safeZone.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.18f, 0.78f, 0.32f, 0.45f);
            }

            ZombieModeSafeZoneController controller = safeZone.AddComponent<ZombieModeSafeZoneController>();
            controller.Initialize(runId);
            zombieModeRunState.ActiveSafeZoneCenter = position;
            zombieModeRunState.ActiveSafeZoneRadius = ZombieModeTuning.SafeZoneRadius;
            zombieModeRunState.ActiveSafeZoneActive = true;
            zombieModeRunState.LastSafeZoneTickTime = 0f;
            zombieModeRunState.ActiveSafeZoneVisual = safeZone;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.SafeZone, safeZone, controller, null);
            TryRegisterZombieModeShootStealthBreaker(runId);
            TickZombieModeSafeZone();
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
                renderer.material.color = new Color(0.85f, 0.42f, 0.20f, 0.40f);
                return;
            }

            if (zombieModeRunState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds &&
                zombieModeRunState.PreparationTimer > 0f)
            {
                float flash = Mathf.PingPong(Time.unscaledTime / ZombieModeTuning.SafeZoneFlashCycleSeconds, 1f);
                float alpha = Mathf.Lerp(0.15f, 0.55f, flash);
                renderer.material.color = new Color(0.92f, 0.72f, 0.18f, alpha);
                return;
            }

            renderer.material.color = new Color(0.18f, 0.78f, 0.32f, 0.45f);
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

            GameObject panel = CreateRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(540f, 260f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.86f);
            CreateText("Title", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_Title"), 28, new Vector2(0f, 80f), new Vector2(500f, 60f));
            CreateButton("Extract", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_ExtractNow"), new Vector2(-130f, -30f), true);
            CreateButton("Continue", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_Continue"), new Vector2(130f, -30f), false);
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

        private void CreateButton(string name, Transform parent, string text, Vector2 position, bool extract)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(210f, 78f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.color = extract ? new Color(0.18f, 0.36f, 0.22f, 0.95f) : new Color(0.36f, 0.24f, 0.18f, 0.95f);
            Button button = obj.AddComponent<Button>();
            CreateText("Text", obj.transform, text, 22, Vector2.zero, new Vector2(200f, 68f));
            button.onClick.AddListener(delegate
            {
                if (owner == null)
                {
                    return;
                }

                if (extract)
                {
                    owner.StartZombieModeExtractionFromUi(runId);
                }
                else
                {
                    owner.ContinueZombieModeAfterExtractionOpportunity(runId);
                }
            });
        }
    }
}

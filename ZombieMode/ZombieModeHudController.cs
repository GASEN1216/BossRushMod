// ============================================================================
// ZombieModeHudController.cs - 丧尸模式 HUD 控制器
// ============================================================================
// 模块说明：
//   管理丧尸模式运行期间的抬头显示（HUD），包括：
//   - 主面板：波次/污染度/净化点/击杀进度/Boss距离
//   - 安全区面板：安全区状态/隐匿/倒计时
//   - 阶段面板：当前阶段/信标/撤离提示
//
// 性能说明：
//   文本刷新按 0.1s 间隔节流，避免每帧字符串拼接。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Zombie Mode HUD
        /// <summary>
        /// 创建丧尸模式 HUD 并注册为 Run-only 对象
        /// </summary>
        private void CreateZombieModeHud(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject root = new GameObject("ZombieMode_Hud");
            ZombieModeHudController controller = root.AddComponent<ZombieModeHudController>();
            controller.Initialize(runId);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Hud, root, controller, null);
        }

        public string GetZombieModeHudMainText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            string wave = string.Format(L10n.T("BossRush_ZombieMode_Hud_Wave"), zombieModeRunState.CurrentWave);
            string pollution = string.Format(L10n.T("BossRush_ZombieMode_Hud_Pollution"), zombieModeRunState.TotalPollution, GetZombieModePollutionTierText());
            string purification = string.Format(L10n.T("BossRush_ZombieMode_Hud_PurificationPoints"), zombieModeRunState.PurificationPoints);
            string kills = string.Empty;
            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat && zombieModeRunState.CurrentWaveKillTarget > 0)
            {
                kills = string.Format(L10n.T("BossRush_ZombieMode_Hud_KillProgress"), zombieModeRunState.CurrentWaveKills, zombieModeRunState.CurrentWaveKillTarget);
            }
            else if (zombieModeRunState.CurrentWaveBossesRemaining > 0)
            {
                kills = GetZombieModeBossProgressText();
            }

            string result = L10n.T("BossRush_ZombieMode_EntryName") + "\n" + wave + "\n" + pollution + "\n" + purification;
            if (!string.IsNullOrEmpty(kills))
            {
                result += "\n" + kills;
            }
            result += "\n" + GetZombieModeNextBossText();
            return result;
        }

        public string GetZombieModeHudSafeZoneText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            if (!zombieModeRunState.ActiveSafeZoneActive)
            {
                return string.Empty;
            }

            string inside = zombieModeRunState.PlayerInsideSafeZone
                ? L10n.T("BossRush_ZombieMode_Hud_SafeZone_Inside")
                : L10n.T("BossRush_ZombieMode_Hud_SafeZone_Outside");
            string stealth = zombieModeRunState.SafeZoneStealthBroken
                ? L10n.T("BossRush_ZombieMode_Hud_SafeZone_StealthBroken")
                : L10n.T("BossRush_ZombieMode_Hud_SafeZone_StealthOk");
            string timer = string.Format(
                L10n.T("BossRush_ZombieMode_Hud_PreparationTimer"),
                Mathf.Max(0, Mathf.CeilToInt(zombieModeRunState.PreparationTimer)));
            return inside + "\n" + stealth + "\n" + timer;
        }

        public string GetZombieModeHudStageText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            string stage = GetZombieModeStageText();
            string beacon = GetZombieModeBeaconHudText();
            string extraction = GetZombieModeExtractionHudText();
            string result = stage;
            if (!string.IsNullOrEmpty(beacon))
            {
                result += "  |  " + beacon;
            }
            if (!string.IsNullOrEmpty(extraction))
            {
                result += "  |  " + extraction;
            }
            return result;
        }

        private static readonly Color ZombieModeHudSafeZoneInactiveColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
        private static readonly Color ZombieModeHudSafeZoneStealthBrokenColor = new Color(0.85f, 0.42f, 0.20f, 0.95f);
        private static readonly Color ZombieModeHudSafeZoneInsideColor = new Color(0.18f, 0.78f, 0.32f, 0.95f);
        private static readonly Color ZombieModeHudSafeZoneFlashTargetColor = new Color(0.92f, 0.72f, 0.18f, 0.95f);
        private static readonly Color ZombieModeHudSafeZoneOutsideColor = new Color(0.92f, 0.72f, 0.18f, 0.85f);

        public Color GetZombieModeHudSafeZoneColor(int runId)
        {
            if (!IsZombieModeRunValid(runId) || !zombieModeRunState.ActiveSafeZoneActive)
            {
                return ZombieModeHudSafeZoneInactiveColor;
            }

            if (zombieModeRunState.SafeZoneStealthBroken)
            {
                return ZombieModeHudSafeZoneStealthBrokenColor;
            }

            if (zombieModeRunState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds)
            {
                float flash = Mathf.PingPong(Time.unscaledTime / ZombieModeTuning.SafeZoneFlashCycleSeconds, 1f);
                return Color.Lerp(
                    ZombieModeHudSafeZoneInsideColor,
                    ZombieModeHudSafeZoneFlashTargetColor,
                    flash);
            }

            return zombieModeRunState.PlayerInsideSafeZone
                ? ZombieModeHudSafeZoneInsideColor
                : ZombieModeHudSafeZoneOutsideColor;
        }

        private string GetZombieModeBossProgressText()
        {
            if (zombieModeRunState.CurrentWaveBossInstances.Count <= 0)
            {
                return string.Format(L10n.T("BossRush_ZombieMode_Hud_BossProgress"), 0, 0);
            }

            int total = zombieModeRunState.CurrentWaveBossInstances.Count;
            int defeated = Mathf.Max(0, total - zombieModeRunState.CurrentWaveBossesRemaining);
            return string.Format(L10n.T("BossRush_ZombieMode_Hud_BossProgress"), defeated, total);
        }

        private string GetZombieModeNextBossText()
        {
            int currentWave = Mathf.Max(0, zombieModeRunState.CurrentWave);
            int wavesToBoss = 5 - (currentWave % 5);
            if (wavesToBoss <= 1)
            {
                return L10n.T("BossRush_ZombieMode_Hud_NextBossNow");
            }

            return string.Format(L10n.T("BossRush_ZombieMode_Hud_NextBoss"), wavesToBoss);
        }

        private string GetZombieModeBeaconHudText()
        {
            return CanUseZombieModeBeacon()
                ? L10n.T("BossRush_ZombieMode_Hud_BeaconReady")
                : string.Empty;
        }

        private string GetZombieModeExtractionHudText()
        {
            if (zombieModeRunState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity || zombieModeRunState.ActiveExtractionArea == null)
            {
                return string.Empty;
            }

            return L10n.T("BossRush_ZombieMode_Hud_ExtractionOpenHint");
        }

        private string GetZombieModePollutionTierText()
        {
            switch (zombieModeRunState.PollutionTier)
            {
                case 0:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_Base");
                case 1:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_I");
                case 2:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_II");
                case 3:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_III");
                case 4:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_IV");
                default:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_Critical");
            }
        }

        private string GetZombieModeStageText()
        {
            switch (zombieModeRunState.CombatPhase)
            {
                case ZombieModeCombatPhase.Settling:
                    return L10n.T("BossRush_ZombieMode_Hud_StageSettling");
                case ZombieModeCombatPhase.RewardSelection:
                    return L10n.T("BossRush_ZombieMode_Hud_StageRewardSelection");
                case ZombieModeCombatPhase.Preparation:
                case ZombieModeCombatPhase.InitialPreparation:
                    return L10n.T("BossRush_ZombieMode_Hud_StagePreparation");
                case ZombieModeCombatPhase.ExtractionOpportunity:
                    return L10n.T("BossRush_ZombieMode_Hud_StageExtractionOpportunity");
                default:
                    return L10n.T("BossRush_ZombieMode_Hud_StageBattle");
            }
        }

        #endregion
    }

    /// <summary>
    /// 丧尸模式 HUD MonoBehaviour，按 0.1s 间隔刷新 TMP 文本。
    /// </summary>
    public sealed class ZombieModeHudController : MonoBehaviour
    {
        public int RunId;
        private Canvas canvas;
        private TextMeshProUGUI mainText;
        private TextMeshProUGUI safeZoneText;
        private TextMeshProUGUI stageText;
        private string lastMainText;
        private string lastSafeZoneText;
        private string lastStageText;
        private Color lastSafeZoneColor;
        private bool hasLastSafeZoneColor;
        private float nextRefreshTime;
        private bool pauseMenuHidden;
        private ModBehaviour owner;
        private const float REFRESH_INTERVAL = 0.1f;

        public void Initialize(int runId)
        {
            RunId = runId;
            owner = ModBehaviour.Instance;
            Build();
        }

        private void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 28000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            mainText = CreatePanel(
                "MainPanel",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -294f),
                new Vector2(420f, 200f),
                22f,
                TextAlignmentOptions.TopLeft,
                new Color(0.75f, 1f, 0.75f, 0.95f));

            safeZoneText = CreatePanel(
                "SafeZonePanel",
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-408f, -24f),
                new Vector2(320f, 120f),
                24f,
                TextAlignmentOptions.TopRight,
                new Color(0.18f, 0.78f, 0.32f, 0.95f));

            stageText = CreatePanel(
                "StagePanel",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 156f),
                new Vector2(600f, 48f),
                28f,
                TextAlignmentOptions.Center,
                new Color(1f, 0.92f, 0.55f, 0.95f));
        }

        private TextMeshProUGUI CreatePanel(
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            TextMeshProUGUI tmp = ZombieModeUIHelper.CreateTMPText(obj, string.Empty, fontSize, alignment, color);
            return tmp;
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null)
            {
                return;
            }

            bool hidden = inst.IsZombieModeGamePaused();
            SetPauseMenuHidden(hidden);
            if (hidden)
            {
                return;
            }

            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }
            nextRefreshTime = Time.unscaledTime + REFRESH_INTERVAL;

            if (mainText != null)
            {
                SetTextIfChanged(mainText, inst.GetZombieModeHudMainText(RunId), ref lastMainText);
            }

            if (safeZoneText != null)
            {
                SetTextIfChanged(safeZoneText, inst.GetZombieModeHudSafeZoneText(RunId), ref lastSafeZoneText);
                SetSafeZoneColorIfChanged(inst.GetZombieModeHudSafeZoneColor(RunId));
            }

            if (stageText != null)
            {
                SetTextIfChanged(stageText, inst.GetZombieModeHudStageText(RunId), ref lastStageText);
            }
        }

        private static void SetTextIfChanged(TextMeshProUGUI target, string value, ref string lastValue)
        {
            string nextValue = value ?? string.Empty;
            if (string.Equals(lastValue, nextValue, System.StringComparison.Ordinal))
            {
                return;
            }

            lastValue = nextValue;
            target.text = nextValue;
        }

        private void SetSafeZoneColorIfChanged(Color value)
        {
            if (safeZoneText == null)
            {
                return;
            }

            if (hasLastSafeZoneColor && lastSafeZoneColor == value)
            {
                return;
            }

            lastSafeZoneColor = value;
            hasLastSafeZoneColor = true;
            safeZoneText.color = value;
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
        }

        private void SetPauseMenuHidden(bool hidden)
        {
            if (pauseMenuHidden == hidden)
            {
                return;
            }
            pauseMenuHidden = hidden;
            if (canvas != null)
            {
                canvas.enabled = !hidden;
            }
        }
    }
}

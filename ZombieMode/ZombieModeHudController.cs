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

        // HUD 富文本配色（审美审查 UC-04）：正文底色是 TextSecondary（标签），数值包成 TextPrimary，
        // 标题行 20 号、模式名用 Accent；阶位 / 警示用 token 预先转好的 hex，不每帧拼。
        private static readonly string ZombieModeHudValueOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextPrimary) + ">";
        private static readonly string ZombieModeHudAccentOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.Accent) + ">";
        private static readonly string ZombieModeHudWarningOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText) + ">";
        private static readonly string ZombieModeHudDangerOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";
        private static readonly string ZombieModeHudSuccessOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText) + ">";

        private static string ZombieModeHudValue(int value)
        {
            return ZombieModeHudValueOpen + value + "</color>";
        }

        public string GetZombieModeHudMainText(int runId)
        {
            return GetZombieModeHudMainText(runId, -1);
        }

        /// <summary>主面板文本。<paramref name="shownPurification"/> 是 HUD 滚动中的显示值（&lt;0 时用真实值）。</summary>
        public string GetZombieModeHudMainText(int runId, int shownPurification)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            string wave = string.Format(L10n.T("BossRush_ZombieMode_Hud_Wave"), zombieModeRunState.CurrentWave);
            string pollution = string.Format(L10n.T("BossRush_ZombieMode_Hud_Pollution"), ZombieModeHudValue(zombieModeRunState.TotalPollution), GetZombieModePollutionTierText());
            int purificationValue = shownPurification >= 0 ? shownPurification : zombieModeRunState.PurificationPoints;
            // 数字等宽：滚动计数与跳变时整行不左右抖。
            string purification = string.Format(L10n.T("BossRush_ZombieMode_Hud_PurificationPoints"), ZombieModeHudValueOpen + "<mspace=0.56em>" + purificationValue + "</mspace></color>");
            string pressure = GetZombieModeHudPressureText();
            string kills = string.Empty;
            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat && zombieModeRunState.CurrentWaveKillTarget > 0)
            {
                kills = string.Format(L10n.T("BossRush_ZombieMode_Hud_KillProgress"), ZombieModeHudValue(zombieModeRunState.CurrentWaveKills), ZombieModeHudValue(zombieModeRunState.CurrentWaveKillTarget));
            }
            else if (zombieModeRunState.CurrentWaveBossesRemaining > 0)
            {
                kills = GetZombieModeBossProgressText();
            }

            string result = "<size=20><b>" + ZombieModeHudAccentOpen + L10n.T("BossRush_ZombieMode_EntryName") + "</color>  " + ZombieModeHudValueOpen + wave + "</color></b></size>\n" + pollution + "\n" + purification;
            if (!string.IsNullOrEmpty(pressure))
            {
                result += "\n" + pressure;
            }
            if (!string.IsNullOrEmpty(kills))
            {
                result += "\n" + kills;
            }
            result += "\n" + GetZombieModeNextBossText();
            return result;
        }

        public string GetZombieModeNextWavePreviewText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            int nextWave = Mathf.Max(1, zombieModeRunState.CurrentWave + 1);
            if (IsZombieModeBossWave(nextWave))
            {
                return string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_NextBossPreview"),
                    nextWave,
                    GetZombieModeWaveCycleIndex(nextWave) + 1,
                    GetZombieModeBossCountForWave(nextWave),
                    Mathf.RoundToInt(GetZombieModeBossHealthScale(nextWave) * 100f),
                    Mathf.RoundToInt(GetZombieModeBossDamageScale(nextWave) * 100f),
                    GetZombieModeWavePressureTarget(nextWave),
                    Mathf.RoundToInt(GetZombieModeBossRewardScale(nextWave) * 100f));
            }

            return string.Format(
                L10n.T("BossRush_ZombieMode_Reward_NextWavePreview"),
                nextWave,
                GetZombieModeWavePressureTarget(nextWave),
                Mathf.RoundToInt(GetZombieModeWaveSpeedMultiplier(nextWave) * 100f),
                GetZombieModeTideStageText(nextWave, false));
        }

        private string GetZombieModeHudPressureText()
        {
            if (!IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase))
            {
                return string.Empty;
            }

            bool preparation = zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat;
            int wave = GetZombieModePacingWave();
            return string.Format(
                L10n.T("BossRush_ZombieMode_Hud_Pressure"),
                ZombieModeHudValue(zombieModeRunState.LivingNormalZombieCount),
                ZombieModeHudValue(GetZombieModeAmbientPressureTarget()),
                GetZombieModeTideStageText(wave, preparation));
        }

        private string GetZombieModeTideStageText(int wave, bool preparation)
        {
            if (preparation)
            {
                return L10n.T("BossRush_ZombieMode_Tide_Low");
            }

            if (IsZombieModeBossWave(wave))
            {
                return L10n.T("BossRush_ZombieMode_Tide_Boss");
            }

            switch (GetZombieModeNormalWaveStageIndex(wave))
            {
                case 0:
                    return L10n.T("BossRush_ZombieMode_Tide_Low");
                case 1:
                    return L10n.T("BossRush_ZombieMode_Tide_Rising");
                case 2:
                    return L10n.T("BossRush_ZombieMode_Tide_High");
                default:
                    return L10n.T("BossRush_ZombieMode_Tide_Peak");
            }
        }

        public string GetZombieModeHudSafeZoneText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            if (!AnyZombieModeSafeZoneActive)
            {
                return string.Empty;
            }

            string inside = zombieModeRunState.PlayerInsideSafeZone
                ? L10n.T("BossRush_ZombieMode_Hud_SafeZone_Inside")
                : L10n.T("BossRush_ZombieMode_Hud_SafeZone_Outside");
            // 旧版第二行恒为「安全区：有效」，面板显示本身就说明了这一点，删掉（审美审查 UC-23）。
            // 战斗期部署的便携安全区没有准备倒计时，不显示恒为 0 的那一行。
            if (zombieModeRunState.PreparationTimer <= 0f)
            {
                return inside;
            }

            string timer = string.Format(
                L10n.T("BossRush_ZombieMode_Hud_PreparationTimer"),
                Mathf.Max(0, Mathf.CeilToInt(zombieModeRunState.PreparationTimer)));
            return inside + "\n" + timer;
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
                result += "  ·  " + beacon;
            }
            if (!string.IsNullOrEmpty(extraction))
            {
                result += "  ·  " + ZombieModeHudWarningOpen + extraction + "</color>";
            }
            return result;
        }

        /// <summary>
        /// HUD 读条取数（插值与绘制在 ZombieModeHudController）。纯读，不改状态。
        /// beaconFill &lt; 0 表示没在引导；引导起点在暂停时会顺延（见 ZombieModeBeaconChannelCoroutine），所以按墙钟算就是已引导时长。
        /// </summary>
        internal void GetZombieModeHudBarState(int runId, out int kills, out int killTarget, out float beaconFill, out float preparationTimer)
        {
            kills = 0;
            killTarget = 0;
            beaconFill = -1f;
            preparationTimer = 0f;
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            if (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                kills = zombieModeRunState.CurrentWaveKills;
                killTarget = zombieModeRunState.CurrentWaveKillTarget;
            }
            if (zombieModeRunState.BeaconChanneling && zombieModeRunState.BeaconChannelDuration > 0f)
            {
                beaconFill = Mathf.Clamp01((Time.unscaledTime - zombieModeRunState.BeaconChannelStartTime) / zombieModeRunState.BeaconChannelDuration);
            }
            preparationTimer = zombieModeRunState.PreparationTimer;
        }

        /// <summary>Boss 奖励节点的净化收益倍率（百分比）。奖励卡按它分描边档位（≥150% 用传说色）。</summary>
        public int GetZombieModeBossRewardPercent(int runId)
        {
            return IsZombieModeRunValid(runId)
                ? Mathf.RoundToInt(GetZombieModeBossRewardScale(zombieModeRunState.CurrentWave) * 100f)
                : 100;
        }

        // 安全区面板字色走 token（审美审查 UC-16）：区内 SuccessText、区外 WarningText；
        // 最后几秒在 WarningText 与 TextPrimary 之间平滑呼吸（SmoothStep），由 HUD 每帧取色，不再 0.1 s 一跳的线性三角波。
        private static readonly Color ZombieModeHudSafeZoneInactiveColor = BossRushUIColors.TextSecondary;
        private static readonly Color ZombieModeHudSafeZoneInsideColor = BossRushUIColors.SuccessText;
        private static readonly Color ZombieModeHudSafeZoneFlashTargetColor = BossRushUIColors.TextPrimary;
        private static readonly Color ZombieModeHudSafeZoneOutsideColor = BossRushUIColors.WarningText;

        internal bool IsZombieModeHudSafeZoneWarning(int runId)
        {
            return IsZombieModeRunValid(runId) && AnyZombieModeSafeZoneActive &&
                   zombieModeRunState.PreparationTimer > 0f &&
                   zombieModeRunState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds;
        }

        public Color GetZombieModeHudSafeZoneColor(int runId)
        {
            if (!IsZombieModeRunValid(runId) || !AnyZombieModeSafeZoneActive)
            {
                return ZombieModeHudSafeZoneInactiveColor;
            }

            if (IsZombieModeHudSafeZoneWarning(runId))
            {
                float flash = Mathf.PingPong(Time.unscaledTime / ZombieModeTuning.SafeZoneFlashCycleSeconds, 1f);
                return Color.Lerp(
                    ZombieModeHudSafeZoneOutsideColor,
                    ZombieModeHudSafeZoneFlashTargetColor,
                    BossRushUI.SmoothStep(flash));
            }

            return zombieModeRunState.PlayerInsideSafeZone
                ? ZombieModeHudSafeZoneInsideColor
                : ZombieModeHudSafeZoneOutsideColor;
        }

        private string GetZombieModeBossProgressText()
        {
            int total = GetZombieModeBossCountForWave(zombieModeRunState.CurrentWave);
            int defeated = Mathf.Max(0, total - zombieModeRunState.CurrentWaveBossesRemaining);
            return string.Format(L10n.T("BossRush_ZombieMode_Hud_BossProgress"), ZombieModeHudValue(defeated), ZombieModeHudValue(total));
        }

        private string GetZombieModeNextBossText()
        {
            int currentWave = Mathf.Max(0, zombieModeRunState.CurrentWave);
            int wavesToBoss = 5 - (currentWave % 5);
            if (wavesToBoss <= 1)
            {
                return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_NextBossNow") + "</color>";
            }

            return string.Format(L10n.T("BossRush_ZombieMode_Hud_NextBoss"), ZombieModeHudValue(wavesToBoss));
        }

        private string GetZombieModeBeaconHudText()
        {
            if (zombieModeRunState.BeaconChanneling)
            {
                // 引导中不再让「信标可用」直接消失：显示剩余秒数，读条在阶段栏下沿（审美审查 UC-17）。
                float remaining = Mathf.Max(0f, zombieModeRunState.BeaconChannelDuration - (Time.unscaledTime - zombieModeRunState.BeaconChannelStartTime));
                return ZombieModeHudAccentOpen + string.Format(L10n.T("BossRush_ZombieMode_Hud_BeaconChanneling"), remaining) + "</color>";
            }

            return CanUseZombieModeBeacon()
                ? ZombieModeHudSuccessOpen + L10n.T("BossRush_ZombieMode_Hud_BeaconReady") + "</color>"
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
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_I") + "</color>";
                case 2:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_II") + "</color>";
                case 3:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_III") + "</color>";
                case 4:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_IV") + "</color>";
                default:
                    return ZombieModeHudDangerOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_Critical") + "</color>";
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
    /// 2026-09-23 审美审查（UC-04 / UC-16 / UC-17 / UC-18 / UC-22）：主面板改成「标题行 + 标签 / 数值两级配色」、
    /// 高度随行数收放；击杀进度与信标引导 / 准备期倒计时各一条 4px 读条（按帧 MoveTowards，O(1)）；
    /// 净化点数滚动计数 + 右上角聚合的「净化点 +N」；字描边走共享 TMP 材质（UI.Shadow 对 TMP 不生效）。
    /// 文字仍按 0.1 s 节流，只在净化点滚动期间逐帧刷新主面板。
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
        private const float GainHoldSeconds = 1.0f;
        private const float GainFadeSeconds = 0.3f;

        private ZombieModeHudBar killBar;
        private ZombieModeHudBar stageBar;
        private TextMeshProUGUI gainText;
        private CanvasGroup gainGroup;
        private int lastActualPurification = -1;
        private float shownPurificationValue;
        private int shownPurification = -1;
        private int pendingGain;
        private float gainAge = 99f;
        private float lastPreparationTimer;
        private float preparationTotal;

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
            canvas.sortingOrder = BossRushUILayers.ZombieHud;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            // 主面板：正文 16 号 TextSecondary（标签），数值与标题行在富文本里提成 TextPrimary / 20 号。
            mainText = CreatePanel(
                "MainPanel",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -294f),
                new Vector2(392f, 184f),
                16f,
                TextAlignmentOptions.TopLeft,
                BossRushUIColors.TextSecondary);
            mainText.rectTransform.offsetMin = new Vector2(16f, 16f);
            mainText.rectTransform.offsetMax = new Vector2(-12f, -8f);
            Transform mainPanel = mainText.transform.parent;
            GameObject rail = ZombieModeUIHelper.CreateRect("AccentRail", mainPanel, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(3f, 0f), new Vector2(3f, -18f), new Vector2(0f, 0.5f));
            Image railImage = rail.AddComponent<Image>();
            railImage.color = BossRushUIColors.Accent;
            railImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
            killBar = new ZombieModeHudBar(mainPanel, "KillBar", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(2f, 8f), new Vector2(-32f, 4f));

            GameObject gainObject = ZombieModeUIHelper.CreateRect("PurificationGain", mainPanel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-12f, -8f), new Vector2(170f, 28f), new Vector2(1f, 1f));
            gainGroup = gainObject.AddComponent<CanvasGroup>();
            gainGroup.alpha = 0f;
            gainGroup.blocksRaycasts = false;
            gainText = ZombieModeUIHelper.CreateTMPText(gainObject, string.Empty, 16f, TextAlignmentOptions.TopRight, BossRushUIColors.SuccessText);
            gainText.enableAutoSizing = false;
            gainText.overflowMode = TextOverflowModes.Overflow;
            gainText.fontStyle = FontStyles.Bold;
            BossRushUIKit.ApplyWorldTextOutline(gainText);

            safeZoneText = CreatePanel(
                "SafeZonePanel",
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-408f, -24f),
                new Vector2(300f, 76f),
                17f,
                TextAlignmentOptions.TopRight,
                BossRushUIColors.SuccessText);
            safeZoneText.rectTransform.offsetMin = new Vector2(10f, 8f);
            safeZoneText.rectTransform.offsetMax = new Vector2(-12f, -8f);

            stageText = CreatePanel(
                "StagePanel",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 156f),
                new Vector2(560f, 42f),
                18f,
                TextAlignmentOptions.Center,
                BossRushUIColors.TextPrimary);
            stageBar = new ZombieModeHudBar(stageText.transform.parent, "StageBar", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 4f), new Vector2(-28f, 4f));
        }

        /// <summary>面板高度跟着行数收放（关了自动缩字，不会整块字号跳变）。只在文字变化时调。</summary>
        private static void FitPanelHeight(TextMeshProUGUI text, float minimum, float padding)
        {
            RectTransform panel = text != null ? text.transform.parent as RectTransform : null;
            if (panel == null)
            {
                return;
            }
            float height = Mathf.Max(minimum, Mathf.Ceil(text.preferredHeight) + padding);
            if (!Mathf.Approximately(panel.sizeDelta.y, height))
            {
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
            }
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
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name,
                transform,
                anchorMin,
                anchorMax,
                position,
                size,
                pivot);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            bool stagePanel = name == "StagePanel";
            // HUD 此前是三块裸文本，只靠投影和背景区分；补一层半透底板，
            // 亮场景下才读得清。raycastTarget 必须关掉，HUD 不能挡住游戏内点击。
            Image panelBackground = obj.AddComponent<Image>();
            panelBackground.color = stagePanel
                ? new Color(0.02f, 0.025f, 0.03f, 0.42f)
                : new Color(0.02f, 0.025f, 0.03f, 0.55f);
            BossRushUI.ApplyPanelSkin(panelBackground, 8, BossRushUISkinPart.Card);
            panelBackground.raycastTarget = false;

            GameObject textObject = ZombieModeUIHelper.CreateRect(
                "Text",
                obj.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.offsetMin = stagePanel ? new Vector2(8f, 4f) : new Vector2(2f, 4f);
            textRect.offsetMax = stagePanel ? new Vector2(-8f, -4f) : new Vector2(-2f, -8f);
            TextMeshProUGUI tmp = ZombieModeUIHelper.CreateTMPText(textObject, string.Empty, fontSize, alignment, color);
            tmp.lineSpacing = stagePanel ? 0f : 2f;
            // 固定字号：行数随阶段变化时整块字号不跳；面板高度由 FitPanelHeight 跟着收放（UC-04）。
            tmp.enableAutoSizing = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            // 压在游戏世界上的字用共享 TMP 描边 + 底影（UC-22：UI.Shadow 是 BaseMeshEffect，TMP 不走它，挂了等于没挂）。
            BossRushUIKit.ApplyWorldTextOutline(tmp);
            return tmp;
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null)
            {
                return;
            }

            // 暂停菜单之外，官方界面（背包 / 地图 / 对话 / 捏脸 / 拍照模式）开着、切图黑幕与加载界面（9000 / 10000）盖着时也收起：
            // 本画布在 ZombieHud（28000），压在这些之上（常驻 HUD 口径，2026-09-14）。
            bool hidden = inst.IsZombieModeGamePaused() || BossRushUI.IsOfficialHudHidden() || BossRushUI.IsGamePaused() || global::SceneLoader.IsSceneLoading;
            SetPauseMenuHidden(hidden);
            if (hidden)
            {
                return;
            }

            // 逐帧的只有 O(1) 的插值：读条、净化点滚动、安全区末段的呼吸取色；文字仍按 0.1 s 节流。
            float deltaTime = Time.unscaledDeltaTime;
            bool rolling = TickPurification(inst, deltaTime);
            TickBars(inst, deltaTime);
            if (safeZoneText != null && inst.IsZombieModeHudSafeZoneWarning(RunId))
            {
                SetSafeZoneColorIfChanged(inst.GetZombieModeHudSafeZoneColor(RunId));
            }

            if (!rolling && Time.unscaledTime < nextRefreshTime)
            {
                return;
            }
            nextRefreshTime = Time.unscaledTime + REFRESH_INTERVAL;

            if (mainText != null)
            {
                string previousMain = lastMainText;
                SetTextIfChanged(mainText, inst.GetZombieModeHudMainText(RunId, shownPurification), ref lastMainText);
                if (!ReferenceEquals(previousMain, lastMainText))
                {
                    FitPanelHeight(mainText, 96f, 26f);
                }
            }

            if (safeZoneText != null)
            {
                string previousSafeZone = lastSafeZoneText;
                SetTextIfChanged(safeZoneText, inst.GetZombieModeHudSafeZoneText(RunId), ref lastSafeZoneText);
                SetPanelVisible(safeZoneText, !string.IsNullOrEmpty(lastSafeZoneText));
                SetSafeZoneColorIfChanged(inst.GetZombieModeHudSafeZoneColor(RunId));
                if (!ReferenceEquals(previousSafeZone, lastSafeZoneText))
                {
                    FitPanelHeight(safeZoneText, 44f, 18f);
                }
            }

            if (stageText != null)
            {
                SetTextIfChanged(stageText, inst.GetZombieModeHudStageText(RunId), ref lastStageText);
            }
        }

        /// <summary>
        /// 净化点滚动计数（UC-04 / UC-18）：显示值按 unscaled 时间 MoveTowards 真实值，速度 max(20, |差|×6)/秒；
        /// 增加时右上角浮「净化点 +N」，1 秒内的拾取合并成一个数，之后 0.3 秒淡出。返回是否仍在滚动。
        /// </summary>
        private bool TickPurification(ModBehaviour inst, float deltaTime)
        {
            int actual = inst.GetZombieModePurificationPoints(RunId);
            if (lastActualPurification < 0)
            {
                lastActualPurification = actual;
                shownPurificationValue = actual;
                shownPurification = actual;
            }
            else if (actual != lastActualPurification)
            {
                if (actual > lastActualPurification && gainText != null)
                {
                    pendingGain = (gainAge < GainHoldSeconds + GainFadeSeconds ? pendingGain : 0) + (actual - lastActualPurification);
                    gainAge = 0f;
                    gainText.text = string.Format(L10n.T("BossRush_ZombieMode_Reward_PurificationPoints"), pendingGain);
                }
                lastActualPurification = actual;
            }

            if (gainGroup != null && gainAge < GainHoldSeconds + GainFadeSeconds)
            {
                gainAge += deltaTime;
                gainGroup.alpha = 1f - BossRushUI.SmoothStep((gainAge - GainHoldSeconds) / GainFadeSeconds);
            }

            if (shownPurification == actual)
            {
                return false;
            }
            float speed = Mathf.Max(20f, Mathf.Abs(actual - shownPurificationValue) * 6f);
            shownPurificationValue = Mathf.MoveTowards(shownPurificationValue, actual, speed * deltaTime);
            shownPurification = Mathf.RoundToInt(shownPurificationValue);
            return true;
        }

        /// <summary>击杀进度读条 + 阶段栏下沿的信标引导 / 准备期倒计时读条（UC-04 / UC-17）。</summary>
        private void TickBars(ModBehaviour inst, float deltaTime)
        {
            int kills;
            int killTarget;
            float beaconFill;
            float preparationTimer;
            inst.GetZombieModeHudBarState(RunId, out kills, out killTarget, out beaconFill, out preparationTimer);
            if (killBar != null)
            {
                bool showKills = killTarget > 0;
                killBar.SetTarget(showKills, showKills ? (float)kills / killTarget : 0f, BossRushUIColors.Accent);
                killBar.Tick(deltaTime);
            }

            // 准备期总时长没有存在状态里：倒计时往上跳（新一轮准备期）时记下起点。
            if (preparationTimer > lastPreparationTimer + 0.5f)
            {
                preparationTotal = preparationTimer;
            }
            lastPreparationTimer = preparationTimer;
            if (stageBar == null)
            {
                return;
            }
            if (beaconFill >= 0f)
            {
                stageBar.SetTarget(true, beaconFill, BossRushUIColors.Accent);
            }
            else if (preparationTimer > 0f && preparationTotal > 0f)
            {
                bool closing = preparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds;
                stageBar.SetTarget(true, preparationTimer / preparationTotal, closing ? BossRushUIColors.WarningText : BossRushUIColors.Accent);
            }
            else
            {
                stageBar.SetTarget(false, 0f, BossRushUIColors.Accent);
            }
            stageBar.Tick(deltaTime);
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

        private static void SetPanelVisible(TextMeshProUGUI target, bool visible)
        {
            if (target != null && target.transform.parent != null && target.transform.parent.gameObject.activeSelf != visible)
            {
                target.transform.parent.gameObject.SetActive(visible);
            }
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

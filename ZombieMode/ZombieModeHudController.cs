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
        private void CreateZombieModeHud(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CreateZombieModeHud(runId);
        }

        public string GetZombieModeHudMainText(int runId)
        {
            return GetZombieModeHudMainText(runId, -1);
        }

        public string GetZombieModeHudMainText(int runId, int shownPurification)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeHudMainText(runId, shownPurification) : string.Empty;
        }

        public string GetZombieModeNextWavePreviewText(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeNextWavePreviewText(runId) : string.Empty;
        }

        public string GetZombieModeHudSafeZoneText(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeHudSafeZoneText(runId) : string.Empty;
        }

        public string GetZombieModeHudStageText(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeHudStageText(runId) : string.Empty;
        }

        internal void GetZombieModeHudBarState(int runId, out int kills, out int killTarget, out float beaconFill, out float preparationTimer)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null)
            {
                module.GetZombieModeHudBarState(runId, out kills, out killTarget, out beaconFill, out preparationTimer);
                return;
            }
            kills = 0;
            killTarget = 0;
            beaconFill = -1f;
            preparationTimer = 0f;
        }

        public int GetZombieModeBossRewardPercent(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeBossRewardPercent(runId) : 100;
        }

        internal bool IsZombieModeHudSafeZoneWarning(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeHudSafeZoneWarning(runId);
        }

        public Color GetZombieModeHudSafeZoneColor(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeHudSafeZoneColor(runId) : BossRushUIColors.TextSecondary;
        }

        internal bool SetZombieModeHudVisibilityForRuntimeModule(ZombieModeHudController controller, bool hidden)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.SetZombieModeHudVisibility(controller, hidden);
        }

        internal void TickZombieModeHudForRuntimeModule(ZombieModeHudController controller, float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeHud(controller, deltaTime);
        }

        internal void CleanupZombieModeHudForRuntimeModule(ZombieModeHudController controller)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CleanupZombieModeHud(controller);
        }

        internal bool IsZombieModeAmbientZombieSpawnPhaseForHud(ZombieModeCombatPhase phase)
        {
            return IsZombieModeAmbientZombieSpawnPhase(phase);
        }

        internal bool IsZombieModeBossWaveForHud(int wave)
        {
            return IsZombieModeBossWave(wave);
        }

        internal int GetZombieModeWaveCycleIndexForHud(int wave)
        {
            return GetZombieModeWaveCycleIndex(wave);
        }

        internal int GetZombieModeBossCountForWaveForHud(int wave)
        {
            return GetZombieModeBossCountForWave(wave);
        }

        internal float GetZombieModeBossHealthScaleForHud(int wave)
        {
            return GetZombieModeBossHealthScale(wave);
        }

        internal float GetZombieModeBossDamageScaleForHud(int wave)
        {
            return GetZombieModeBossDamageScale(wave);
        }

        internal float GetZombieModeBossRewardScaleForHud(int wave)
        {
            return GetZombieModeBossRewardScale(wave);
        }

        internal int GetZombieModeWavePressureTargetForHud(int wave)
        {
            return GetZombieModeWavePressureTarget(wave);
        }

        internal float GetZombieModeWaveSpeedMultiplierForHud(int wave)
        {
            return GetZombieModeWaveSpeedMultiplier(wave);
        }

        internal int GetZombieModeNormalWaveStageIndexForHud(int wave)
        {
            return GetZombieModeNormalWaveStageIndex(wave);
        }

        internal int GetZombieModePacingWaveForHud()
        {
            return GetZombieModePacingWave();
        }

        internal int GetZombieModeAmbientPressureTargetForHud()
        {
            return GetZombieModeAmbientPressureTarget();
        }

        internal bool IsAnyZombieModeSafeZoneActiveForHud()
        {
            return zombieModeRuntimeModule.AnyZombieModeSafeZoneActive;
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
        private ModBehaviour owner;

        private ZombieModeHudBar killBar;
        private ZombieModeHudBar stageBar;
        private TextMeshProUGUI gainText;
        private CanvasGroup gainGroup;

        internal bool HasMainText { get { return mainText != null; } }
        internal bool HasSafeZoneText { get { return safeZoneText != null; } }
        internal bool HasStageText { get { return stageText != null; } }
        internal bool HasGainText { get { return gainText != null; } }
        internal bool HasGainGroup { get { return gainGroup != null; } }

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
            // 底色走 Surface token，只调透明度（UI 共识对照审查 B-30，与血猎追击状态卡同口径）。
            Image panelBackground = obj.AddComponent<Image>();
            Color panelColor = BossRushUIColors.Surface;
            panelColor.a = stagePanel ? 0.42f : 0.55f;
            panelBackground.color = panelColor;
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

            inst.TickZombieModeHudForRuntimeModule(this, Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst != null)
            {
                inst.CleanupZombieModeHudForRuntimeModule(this);
            }
        }

        internal void SetMainText(string value, bool fitPanel)
        {
            if (mainText != null)
            {
                mainText.text = value ?? string.Empty;
                if (fitPanel)
                {
                    FitPanelHeight(mainText, 96f, 26f);
                }
            }
        }

        internal void SetSafeZoneText(string value, bool fitPanel)
        {
            if (safeZoneText != null)
            {
                safeZoneText.text = value ?? string.Empty;
                if (fitPanel)
                {
                    FitPanelHeight(safeZoneText, 44f, 18f);
                }
            }
        }

        internal void SetSafeZonePanelVisible(bool visible)
        {
            SetPanelVisible(safeZoneText, visible);
        }

        internal void SetSafeZoneColor(Color value)
        {
            if (safeZoneText != null)
            {
                safeZoneText.color = value;
            }
        }

        internal void SetStageText(string value)
        {
            if (stageText != null)
            {
                stageText.text = value ?? string.Empty;
            }
        }

        internal void SetPurificationGainText(string value)
        {
            if (gainText != null)
            {
                gainText.text = value ?? string.Empty;
            }
        }

        internal void SetPurificationGainAlpha(float alpha)
        {
            if (gainGroup != null)
            {
                gainGroup.alpha = alpha;
            }
        }

        internal void TickBars(int kills, int killTarget, float beaconFill, float preparationTimer, float preparationTotal, float deltaTime)
        {
            if (killBar != null)
            {
                bool showKills = killTarget > 0;
                killBar.SetTarget(showKills, showKills ? (float)kills / killTarget : 0f, BossRushUIColors.Accent);
                killBar.Tick(deltaTime);
            }

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
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || !inst.SetZombieModeHudVisibilityForRuntimeModule(this, hidden))
            {
                return;
            }
            if (canvas != null)
            {
                canvas.enabled = !hidden;
            }
        }
    }
}

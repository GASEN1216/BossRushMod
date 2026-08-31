using System;
using System.Collections.Generic;
using Duckov.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>Mode H 的九类页面（§23.1）。</summary>
    internal enum ModeHPage
    {
        /// <summary>无页面</summary>
        None = 0,
        /// <summary>入口与五席试棚</summary>
        Entry = 1,
        /// <summary>赛前看盘</summary>
        Brief = 2,
        /// <summary>赔率与虚拟下注</summary>
        Odds = 3,
        /// <summary>结算战报</summary>
        Settlement = 4,
        /// <summary>转会窗口</summary>
        Transfer = 5,
        /// <summary>名人堂</summary>
        HallOfFame = 6
    }

    /// <summary>
    /// Mode H 界面根（设计提案 §23.1、§25.1）。
    ///
    /// 冻结契约：
    /// - 全部运行时创建，不制作 Unity prefab；
    /// - Canvas 一律走 `BossRushUI.CreateCanvasRoot`，`sortingOrder` 只引用
    ///   `BossRushUILayers` 常量，本文件不得出现裸层级数字；
    /// - 遮罩只用 `BossRushUI.CreateBackdrop`，皮肤走 `BossRushUI.ApplyPanelSkin`，
    ///   颜色只用 `BossRushUIColors` token，文本一律 TMP；
    /// - 官方 prefab 优先：按钮先取 `GameplayDataSettings.UIPrefabs.Button`，
    ///   为 null 时回退共享库手搓；
    /// - HUD 与诊断层挂 `GraphicRaycaster` 但**不** `ClaimModalInput`、不暂停时间；
    ///   六个非战斗模态页共用**唯一**一个 `ModalInputLease`，
    ///   owner label 为 `ModeH:&lt;lifecycle&gt;:&lt;runId&gt;`，页面切换不重复 claim；
    /// - HUD 只在值变化或最多 4 Hz 时刷新；不自绘头顶血条与伤害数字。
    /// </summary>
    internal sealed class ModeHUI
    {
        #region 状态

        private Canvas _hudCanvas;
        private Canvas _diagnosticsCanvas;
        private Canvas _modalCanvas;

        private GameObject _hudRoot;
        private GameObject _diagnosticsRoot;
        private GameObject _modalRoot;

        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private GameObject _modalInputToken;
        private ModeHPage _currentPage;
        private string _modalOwnerLabel;

        private TextMeshProUGUI _hudTimer;
        private TextMeshProUGUI _hudStarter;
        private TextMeshProUGUI _hudRelay;
        private TextMeshProUGUI _hudEnemies;
        private Button _bellButton;
        private TextMeshProUGUI _bellLabel;
        private Image _bellWindowBar;

        private float _hudRefreshAccumulator;
        private int _lastTimerSeconds = -1;
        private int _lastEnemyCount = -1;
        private bool _lastBellAvailable;

        #endregion

        #region 只读

        /// <summary>当前打开的模态页面。</summary>
        public ModeHPage CurrentPage { get { return _currentPage; } }

        /// <summary>HUD 是否已创建。</summary>
        public bool HasHud { get { return _hudRoot != null; } }

        /// <summary>是否持有唯一模态输入租约。</summary>
        public bool HasModalLease { get { return _modalLease != null; } }

        #endregion

        #region HUD

        /// <summary>
        /// 创建观战 HUD。挂 `GraphicRaycaster` 让拍铃按钮可点，
        /// 但**不**调用会暂停时间的 `ClaimModalInput`——角色输入由 spectator lease 阻断。
        /// </summary>
        public void EnsureHud(Action onRingBell)
        {
            if (_hudRoot != null) return;

            _hudCanvas = BossRushUI.CreateCanvasRoot(
                "ModeH_HUD", BossRushUILayers.ModeHHud, true);
            _hudRoot = _hudCanvas.gameObject;
            UnityEngine.Object.DontDestroyOnLoad(_hudRoot);

            // 状态区固定左上 560x220、边距 24
            GameObject status = ZombieModeUIHelper.CreateRect(
                "ModeH_Status", _hudRoot.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(StatusMargin + StatusSize.x * 0.5f, -(StatusMargin + StatusSize.y * 0.5f)),
                StatusSize, new Vector2(0.5f, 0.5f));
            Image statusBackground = status.AddComponent<Image>();
            statusBackground.color = BossRushUIColors.Surface;
            statusBackground.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(statusBackground, 10);

            _hudStarter = CreateHudLine(status.transform, "Starter", 0f);
            _hudRelay = CreateHudLine(status.transform, "Relay", -52f);
            _hudEnemies = CreateHudLine(status.transform, "Enemies", -104f);

            // 计时区固定顶部居中 320x96
            GameObject timer = ZombieModeUIHelper.CreateRect(
                "ModeH_Timer", _hudRoot.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(StatusMargin + TimerSize.y * 0.5f)),
                TimerSize, new Vector2(0.5f, 0.5f));
            Image timerBackground = timer.AddComponent<Image>();
            timerBackground.color = BossRushUIColors.Surface;
            timerBackground.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(timerBackground, 10);
            _hudTimer = CreateHudLine(timer.transform, "TimerText", 0f);

            CreateBellButton(onRingBell);
            BossRushUI.PlayOpenAnimation(_hudRoot);
        }

        private TextMeshProUGUI CreateHudLine(Transform parent, string name, float offsetY)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, offsetY), new Vector2(StatusSize.x - 32f, 44f),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, string.Empty, 26f, TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        /// <summary>
        /// 拍铃按钮：全场唯一主动操作，HUD 底部中央大按钮，三态呈现。
        /// 官方 prefab 优先，为 null 时回退共享库手搓。
        /// </summary>
        private void CreateBellButton(Action onRingBell)
        {
            Button official = TryInstantiateOfficialButton(_hudRoot.transform);
            if (official != null)
            {
                RectTransform rect = official.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = BellButtonSize;
                rect.anchoredPosition = new Vector2(0f, BellButtonBottomOffset);
                _bellButton = official;
                _bellLabel = official.GetComponentInChildren<TextMeshProUGUI>();
            }
            else
            {
                _bellButton = ZombieModeUIHelper.CreateButton(
                    "ModeH_Bell", _hudRoot.transform,
                    L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_RingBell"),
                    new Vector2(0.5f, 0f), new Vector2(0f, BellButtonBottomOffset),
                    BellButtonSize, BossRushUIColors.Accent, 30f,
                    new Vector2(BellButtonSize.x - 16f, BellButtonSize.y - 16f), null, true);
                _bellLabel = _bellButton.GetComponentInChildren<TextMeshProUGUI>();
            }

            if (_bellLabel != null) BossRushUI.ApplyGameFont(_bellLabel);
            if (onRingBell != null)
            {
                _bellButton.onClick.RemoveAllListeners();
                _bellButton.onClick.AddListener(delegate { onRingBell(); });
            }

            // 口令窗口倒计时条
            GameObject bar = ZombieModeUIHelper.CreateRect(
                "ModeH_BellWindow", _bellButton.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 4f), new Vector2(0f, 6f), new Vector2(0.5f, 0f));
            _bellWindowBar = bar.AddComponent<Image>();
            _bellWindowBar.color = BossRushUIColors.Success;
            _bellWindowBar.raycastTarget = false;
            _bellWindowBar.type = Image.Type.Filled;
            _bellWindowBar.fillMethod = Image.FillMethod.Horizontal;
            _bellWindowBar.fillAmount = 0f;
        }

        /// <summary>
        /// 刷新 HUD。只在值变化或最多 `HudRefreshIntervalSeconds` 一次时写文本，
        /// 避免每帧字符串分配。
        /// </summary>
        public void TickHud(
            float deltaTime,
            float remainingSeconds,
            string starterName,
            string relayName,
            int liveEnemyCount,
            bool bellAvailable,
            bool bellConsumed,
            string lockedCommandName,
            float commandWindowRemaining)
        {
            if (_hudRoot == null) return;

            _hudRefreshAccumulator += deltaTime;
            int timerSeconds = Mathf.CeilToInt(remainingSeconds);
            bool valueChanged = timerSeconds != _lastTimerSeconds
                || liveEnemyCount != _lastEnemyCount
                || bellAvailable != _lastBellAvailable;
            if (!valueChanged && _hudRefreshAccumulator < ModeHConfig.HudRefreshIntervalSeconds)
            {
                UpdateBellWindowBar(commandWindowRemaining);
                return;
            }
            _hudRefreshAccumulator = 0f;
            _lastTimerSeconds = timerSeconds;
            _lastEnemyCount = liveEnemyCount;
            _lastBellAvailable = bellAvailable;

            if (_hudTimer != null)
            {
                _hudTimer.text = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_TimeRemaining")
                    + "  " + timerSeconds;
            }
            if (_hudStarter != null)
            {
                _hudStarter.text = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_MatchStarter")
                    + "  " + (starterName != null ? starterName : "-");
            }
            if (_hudRelay != null)
            {
                _hudRelay.text = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_MatchRelay")
                    + "  " + (relayName != null ? relayName : "-");
            }
            if (_hudEnemies != null)
            {
                _hudEnemies.text = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Summary_EnemyCount")
                    + "  " + liveEnemyCount;
            }

            UpdateBellState(bellAvailable, bellConsumed, lockedCommandName, commandWindowRemaining);
        }

        /// <summary>三态：可用 + 口令名 / 窗口进行中 / 已消耗置灰。</summary>
        private void UpdateBellState(
            bool bellAvailable, bool bellConsumed, string lockedCommandName, float windowRemaining)
        {
            if (_bellButton == null) return;

            if (windowRemaining > 0f)
            {
                _bellButton.interactable = false;
                SetBellLabel(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Command_WindowActive"));
                SetBellTint(BossRushUIColors.Success);
            }
            else if (bellConsumed || !bellAvailable)
            {
                _bellButton.interactable = false;
                SetBellLabel(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Command_BellConsumed"));
                SetBellTint(BossRushUIColors.Disabled);
            }
            else
            {
                _bellButton.interactable = true;
                string label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_RingBell");
                if (!string.IsNullOrEmpty(lockedCommandName)) label += "  ·  " + lockedCommandName;
                SetBellLabel(label);
                SetBellTint(BossRushUIColors.Accent);
            }
            UpdateBellWindowBar(windowRemaining);
        }

        private void UpdateBellWindowBar(float windowRemaining)
        {
            if (_bellWindowBar == null) return;
            float fill = ModeHConfig.CommandWindowSeconds > 0f
                ? windowRemaining / ModeHConfig.CommandWindowSeconds
                : 0f;
            _bellWindowBar.fillAmount = Mathf.Clamp01(fill);
        }

        private void SetBellLabel(string text)
        {
            if (_bellLabel != null) _bellLabel.text = text;
        }

        private void SetBellTint(Color color)
        {
            if (_bellButton == null) return;
            Image image = _bellButton.GetComponent<Image>();
            if (image != null) image.color = color;
        }

        #endregion

        /// <summary>只销毁观战 HUD；结算页仍由同一 UI owner 继续使用。</summary>
        public void DestroyHud()
        {
            if (_hudRoot != null)
            {
                UnityEngine.Object.Destroy(_hudRoot);
                _hudRoot = null;
                _hudCanvas = null;
            }
            _hudTimer = null;
            _hudStarter = null;
            _hudRelay = null;
            _hudEnemies = null;
            _bellButton = null;
            _bellLabel = null;
            _bellWindowBar = null;
            _lastTimerSeconds = -1;
            _lastEnemyCount = -1;
        }

        #region 诊断覆盖层

        /// <summary>
        /// 生产兼容性诊断：独立实时覆盖层，挂 raycaster 但不 claim 模态输入、
        /// 不暂停 `Time.timeScale`。唯一可交互控件是 owner-checked“取消并退款”。
        /// </summary>
        public void EnsureDiagnostics(Action onCancelAndRefund)
        {
            if (_diagnosticsRoot != null) return;

            _diagnosticsCanvas = BossRushUI.CreateCanvasRoot(
                "ModeH_Diagnostics", BossRushUILayers.ModeHDiagnostics, true);
            _diagnosticsRoot = _diagnosticsCanvas.gameObject;
            UnityEngine.Object.DontDestroyOnLoad(_diagnosticsRoot);

            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeH_DiagnosticsSurface", _diagnosticsRoot.transform,
                DiagnosticsSize, BossRushUIColors.Accent);

            CreateTitle(surface.transform,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Diagnostics"), DiagnosticsSize);
            CreateBody(surface.transform,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Diag_ReadOnlyNotice"),
                DiagnosticsSize, 0f);

            ZombieModeUIHelper.CreateButton(
                "ModeH_DiagCancel", surface.transform,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_CancelAndRefund"),
                new Vector2(0.5f, 0f), new Vector2(0f, SafeMargin + 28f),
                new Vector2(320f, 56f), BossRushUIColors.Danger, 24f,
                new Vector2(300f, 44f),
                onCancelAndRefund != null ? new UnityEngine.Events.UnityAction(onCancelAndRefund) : null,
                onCancelAndRefund != null);

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>只在状态变化时刷新诊断列表。</summary>
        public void UpdateDiagnostics(string progressText)
        {
            if (_diagnosticsRoot == null) return;
            Transform body = _diagnosticsRoot.transform.Find(
                "ModeH_DiagnosticsSurface/ModeH_Body");
            if (body == null) return;
            TextMeshProUGUI text = body.GetComponent<TextMeshProUGUI>();
            if (text != null && !string.Equals(text.text, progressText, StringComparison.Ordinal))
            {
                text.text = progressText;
            }
        }

        /// <summary>幂等销毁诊断覆盖层。</summary>
        public void DestroyDiagnostics()
        {
            if (_diagnosticsRoot == null) return;
            UnityEngine.Object.Destroy(_diagnosticsRoot);
            _diagnosticsRoot = null;
            _diagnosticsCanvas = null;
        }

        #endregion

        #region 模态页面

        /// <summary>
        /// 打开一个非战斗模态页。六个页面共用**唯一**一个 modal lease：
        /// 页面切换只换内容，不重复 claim。
        /// </summary>
        public void OpenPage(ModeHPage page, ModeHLifecycle lifecycle, string runId, ModeHPageContent content)
        {
            if (page == ModeHPage.None)
            {
                ClosePage();
                return;
            }

            EnsureModalRoot(lifecycle, runId);
            ClearModalContent();
            _currentPage = page;

            Vector2 size = page == ModeHPage.Settlement ? ReportPanelSize : MainPanelSize;
            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeH_PageSurface", _modalRoot.transform, size, BossRushUIColors.Accent);

            ModeHUIPages.Build(page, surface.transform, size, content);
            BossRushUI.PlayOpenAnimation(surface);
        }

        private void EnsureModalRoot(ModeHLifecycle lifecycle, string runId)
        {
            string ownerLabel = "ModeH:" + lifecycle + ":" + (runId != null ? runId : string.Empty);
            if (_modalRoot != null)
            {
                _modalOwnerLabel = ownerLabel;
                return;
            }

            _modalCanvas = BossRushUI.CreateCanvasRoot(
                "ModeH_Modal", BossRushUILayers.ModeHModal, true);
            _modalRoot = _modalCanvas.gameObject;
            UnityEngine.Object.DontDestroyOnLoad(_modalRoot);
            BossRushUI.CreateBackdrop(_modalRoot.transform);

            _modalOwnerLabel = ownerLabel;
            _modalInputToken = new GameObject("ModeH_ModalInputToken");
            UnityEngine.Object.DontDestroyOnLoad(_modalInputToken);
            _modalLease = ZombieModeUIHelper.ClaimModalInput(_modalInputToken, _modalOwnerLabel);
        }

        private void ClearModalContent()
        {
            if (_modalRoot == null) return;
            for (int i = _modalRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _modalRoot.transform.GetChild(i);
                if (child == null) continue;
                if (string.Equals(child.name, "Backdrop", StringComparison.Ordinal)) continue;
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        /// <summary>关闭模态页并释放唯一租约。幂等：重复调用安全。</summary>
        public void ClosePage()
        {
            _currentPage = ModeHPage.None;
            if (_modalLease != null)
            {
                _modalLease.Release();
                _modalLease = null;
            }
            if (_modalInputToken != null)
            {
                UnityEngine.Object.Destroy(_modalInputToken);
                _modalInputToken = null;
            }
            if (_modalRoot != null)
            {
                UnityEngine.Object.Destroy(_modalRoot);
                _modalRoot = null;
                _modalCanvas = null;
            }
        }

        #endregion

        #region 共享构件

        /// <summary>官方按钮 prefab 优先；不可用时返回 null 由调用方回退共享库。</summary>
        internal static Button TryInstantiateOfficialButton(Transform parent)
        {
            try
            {
                if (GameplayDataSettings.UIPrefabs == null) return null;
                Button prefab = GameplayDataSettings.UIPrefabs.Button;
                if (prefab == null) return null;
                Button instance = UnityEngine.Object.Instantiate(prefab, parent, false);
                return instance;
            }
            catch (Exception)
            {
                // 官方 prefab 不可用（版本差异或尚未加载）：回退共享库手搓
                return null;
            }
        }

        internal static TextMeshProUGUI CreateTitle(Transform parent, string title, Vector2 panelSize)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                "ModeH_Title", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(SafeMargin + 24f)),
                new Vector2(panelSize.x - SafeMargin * 2f, 56f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, title, 36f, TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        internal static TextMeshProUGUI CreateBody(
            Transform parent, string body, Vector2 panelSize, float offsetY)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                "ModeH_Body", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, offsetY),
                new Vector2(panelSize.x - SafeMargin * 2f, panelSize.y - SafeMargin * 4f),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, body, 24f, TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        /// <summary>品质描边：按 Q1-Q2 / Q3 / Q4 / Q5 / Q6+ 映射五档稀有度 token。</summary>
        internal static Color ResolveRarityColor(int gameQuality)
        {
            if (gameQuality <= 2) return BossRushUIColors.RarityCommon;
            if (gameQuality == 3) return BossRushUIColors.RarityUncommon;
            if (gameQuality == 4) return BossRushUIColors.RarityRare;
            if (gameQuality == 5) return BossRushUIColors.RarityEpic;
            return BossRushUIColors.RarityLegendary;
        }

        #endregion

        #region 尺寸常量（§23.1 冻结）

        /// <summary>状态区固定尺寸。</summary>
        internal static readonly Vector2 StatusSize = new Vector2(560f, 220f);
        /// <summary>计时区固定尺寸。</summary>
        internal static readonly Vector2 TimerSize = new Vector2(320f, 96f);
        /// <summary>拍铃按钮稳定点击区。</summary>
        internal static readonly Vector2 BellButtonSize = new Vector2(160f, 72f);
        /// <summary>主页面面板尺寸。</summary>
        internal static readonly Vector2 MainPanelSize = new Vector2(1480f, 860f);
        /// <summary>战报面板尺寸。</summary>
        internal static readonly Vector2 ReportPanelSize = new Vector2(1180f, 760f);
        /// <summary>诊断覆盖层尺寸。</summary>
        internal static readonly Vector2 DiagnosticsSize = new Vector2(1280f, 760f);
        /// <summary>恢复壳尺寸。</summary>
        internal static readonly Vector2 RecoverySize = new Vector2(1280f, 780f);
        /// <summary>HUD 状态区边距。</summary>
        internal const float StatusMargin = 24f;
        /// <summary>模态页四周安全边距。</summary>
        internal const float SafeMargin = 48f;
        /// <summary>拍铃按钮距底部距离。</summary>
        internal const float BellButtonBottomOffset = 96f;

        #endregion

        #region 生命周期

        /// <summary>幂等销毁全部 Mode H UI。</summary>
        public void DestroyAll()
        {
            ClosePage();
            DestroyDiagnostics();
            DestroyHud();
        }

        #endregion
    }
}

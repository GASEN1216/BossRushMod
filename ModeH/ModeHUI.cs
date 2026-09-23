using System;
using System.Collections.Generic;
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
    /// - 页面按钮走共享库 `ZombieModeUIHelper.CreateButton`；拍铃是一整张可点的卡片
    ///   （2026-09-23 owner 实测：旧的官方 prefab 大按钮压在底部快捷栏上、样子也丑），
    ///   卡片挂在左上状态卡正下方，三态靠描边、徽章与文字区分；
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
        private Image _bellStroke;
        private Image _bellBadge;
        private TextMeshProUGUI _bellBadgeText;
        private TextMeshProUGUI _bellTitle;
        private TextMeshProUGUI _bellHint;
        private TextMeshProUGUI _bellSubtitle;
        private Image _bellWindowBar;
        /// <summary>本场锁定口令的白话说明（开打时由模块写入一次）。</summary>
        private string _bellCommandPlain;
        /// <summary>拍铃卡当前画的是哪一态（-1 = 未画）；只在态或口令名变化时重写文字，避免每次刷新都拼串。</summary>
        private int _bellShownState = -1;
        private string _bellShownName;

        private TextMeshProUGUI _diagProgressText;
        private Image _diagProgressFill;

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
            BossRushUI.ApplyFramedPanelSkin(statusBackground, 10, BossRushUISkinPart.Card);

            _hudStarter = CreateHudLine(status.transform, "Starter", 64f, StatusSize.x - 32f);
            _hudRelay = CreateHudLine(status.transform, "Relay", 0f, StatusSize.x - 32f);
            _hudEnemies = CreateHudLine(status.transform, "Enemies", -64f, StatusSize.x - 32f);

            // 计时区固定顶部居中 320x96
            GameObject timer = ZombieModeUIHelper.CreateRect(
                "ModeH_Timer", _hudRoot.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(StatusMargin + TimerSize.y * 0.5f)),
                TimerSize, new Vector2(0.5f, 0.5f));
            Image timerBackground = timer.AddComponent<Image>();
            timerBackground.color = BossRushUIColors.Surface;
            timerBackground.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(timerBackground, 10, BossRushUISkinPart.Card);
            _hudTimer = CreateHudLine(timer.transform, "TimerText", 0f, TimerSize.x - 32f);
            _hudTimer.alignment = TextAlignmentOptions.Center;

            CreateBellButton(onRingBell);
            BossRushUI.PlayOpenAnimation(_hudRoot);
        }

        private TextMeshProUGUI CreateHudLine(Transform parent, string name, float offsetY, float width)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, offsetY), new Vector2(width, 44f),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, string.Empty, 26f, TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        /// <summary>
        /// 拍铃卡：全场唯一主动操作。整张卡就是按钮，挂在左上状态卡正下方（旧版在底部正中，
        /// 正好压住官方快捷栏）。左边一个圆徽章，右边「拍铃：口令名」+ 一句白话说明，
        /// 底边一条细线是口令生效的 6 秒倒计时。三态：可拍（青色描边）/ 生效中（绿色）/ 已用完（灰）。
        /// </summary>
        private void CreateBellButton(Action onRingBell)
        {
            GameObject card = ZombieModeUIHelper.CreateRect(
                "ModeH_Bell", _hudRoot.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(StatusMargin + BellCardSize.x * 0.5f,
                    -(StatusMargin + StatusSize.y + BellCardGap + BellCardSize.y * 0.5f)),
                BellCardSize, new Vector2(0.5f, 0.5f));
            Image background = card.AddComponent<Image>();
            background.color = Color.white;
            BossRushUI.ApplyPanelSkin(background, 10, BossRushUISkinPart.Card);
            _bellStroke = BossRushUI.ApplyPanelStroke(background, 10, BossRushUISkinPart.Card, BossRushUIColors.Accent);
            _bellButton = card.AddComponent<Button>();
            _bellButton.targetGraphic = background;

            GameObject badge = ZombieModeUIHelper.CreateRect(
                "Badge", card.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(BellInnerPadding + BellBadgeSize * 0.5f, 2f),
                new Vector2(BellBadgeSize, BellBadgeSize), new Vector2(0.5f, 0.5f));
            _bellBadge = badge.AddComponent<Image>();
            _bellBadge.sprite = BossRushUI.GetRoundedSprite((int)(BellBadgeSize * 0.5f));
            _bellBadge.type = Image.Type.Sliced;
            _bellBadge.raycastTarget = false;
            // 「铃」字本身就是图标：官方中文字体一定有字形，不依赖图片资源。
            _bellBadgeText = CreateBellText(badge.transform, "Glyph", L10n.T("铃", "!"), 40f,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(BellBadgeSize, BellBadgeSize));

            float textLeft = BellInnerPadding + BellBadgeSize + 14f;
            float textWidth = BellCardSize.x - textLeft - BellInnerPadding;
            _bellTitle = CreateBellText(card.transform, "Title", string.Empty, 26f, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(textLeft, 20f),
                new Vector2(textWidth - BellHintWidth, BellTitleHeight));
            _bellHint = CreateBellText(card.transform, "Hint", string.Empty, 17f, TextAlignmentOptions.Right,
                new Vector2(1f, 0.5f), new Vector2(-BellInnerPadding, 20f), new Vector2(BellHintWidth, 30f));
            _bellSubtitle = CreateBellText(card.transform, "Subtitle", string.Empty, 19f, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(textLeft, -20f), new Vector2(textWidth, BellSubtitleHeight));

            if (onRingBell != null)
            {
                _bellButton.onClick.RemoveAllListeners();
                _bellButton.onClick.AddListener(delegate { onRingBell(); });
            }

            // 口令窗口倒计时：卡片底边一条细线
            GameObject bar = ZombieModeUIHelper.CreateRect(
                "ModeH_BellWindow", card.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 7f), new Vector2(-BellInnerPadding * 2f, 4f), new Vector2(0.5f, 0f));
            _bellWindowBar = bar.AddComponent<Image>();
            _bellWindowBar.color = BossRushUIColors.SuccessText;
            _bellWindowBar.raycastTarget = false;
            // Filled 必须有 sprite：sprite 为 null 时 Image.OnPopulateMesh 直接退回整块矩形，
            // fillAmount 被完全忽略——口令倒计时条会一直满格、fillAmount=0 时也不消失。
            _bellWindowBar.sprite = BossRushUI.GetSolidSprite();
            _bellWindowBar.type = Image.Type.Filled;
            _bellWindowBar.fillMethod = Image.FillMethod.Horizontal;
            _bellWindowBar.fillAmount = 0f;

            _bellShownState = -1;
            UpdateBellState(true, false, null, 0f);
        }

        /// <summary>
        /// 拍铃卡上的一行字。单行框高至少 1.45×字号 + 4：TMP 的 Ellipsis 在框比一行还矮时会把整串清空。
        /// </summary>
        private static TextMeshProUGUI CreateBellText(Transform parent, string name, string value, float fontSize,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, anchor, anchor, position, size,
                new Vector2(anchor.x, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, value, fontSize, alignment, BossRushUIColors.TextPrimary);
            text.enableWordWrapping = false;
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        /// <summary>
        /// 入场时写入本场锁定口令（名字 + 白话说明）。拍铃卡还在「可拍」态时立刻重画，
        /// 入场那一两秒就能看到口令；生效中 / 已用完时只标脏，下一次 HUD 刷新再画，不闪回「可拍」。
        /// </summary>
        public void SetBellCommand(string commandName, string plainMeaning)
        {
            bool plainChanged = !string.Equals(plainMeaning, _bellCommandPlain, StringComparison.Ordinal);
            _bellCommandPlain = plainMeaning;
            if (_bellShownState == -1 || _bellShownState == BellStateReady)
            {
                _bellShownState = -1;
                UpdateBellState(true, false, commandName, 0f);
            }
            else if (plainChanged)
            {
                _bellShownState = -1;
            }
        }

        /// <summary>
        /// 观战 HUD 跟随官方界面与暂停菜单收起（常驻 HUD 口径，2026-09-14）。由模块每帧驱动，**不放进 TickHud**：
        /// TickHud 只在交战期被调，放在里面的话刷怪那几秒 HUD 仍压在 sortingOrder 100 的背包与地图上面。
        /// 只开关画布：画布关着时拍铃按钮一起点不到，关掉背包后立刻回来。
        /// </summary>
        public void ApplyHudVisibility()
        {
            if (_hudCanvas == null && _diagnosticsCanvas == null) return;
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (_hudCanvas != null && _hudCanvas.enabled != visible) _hudCanvas.enabled = visible;
            // 认证期的诊断页同一口径（2026-09-14 拍板，原待拍板 #10）：它自动挂出、不占模态输入、不停时间，
            // 「取消并退款」按钮是可选的——性质是常驻进度页，不是玩家打开的模态；不跟随的话 970 层会压在背包与地图上面。
            if (_diagnosticsCanvas != null && _diagnosticsCanvas.enabled != visible) _diagnosticsCanvas.enabled = visible;
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
                // 这一行是**场上还活着的敌人数**。旧版借用了看盘页「人数区间」的标签，
                // 玩家看到「人数区间 2」不知道在说什么（2026-09-23 owner 实测截图）。
                _hudEnemies.text = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Hud_EnemiesLeft")
                    + "  " + liveEnemyCount;
            }

            UpdateBellState(bellAvailable, bellConsumed, lockedCommandName, commandWindowRemaining);
        }

        private const int BellStateReady = 0;
        private const int BellStateActive = 1;
        private const int BellStateUsed = 2;

        /// <summary>
        /// 三态：可拍（青色描边、可点）/ 口令生效中（绿色、倒计时线）/ 本场已用完（灰）。
        /// 态和口令名都没变时只推进倒计时线，不重写文字。
        /// </summary>
        private void UpdateBellState(
            bool bellAvailable, bool bellConsumed, string lockedCommandName, float windowRemaining)
        {
            if (_bellButton == null) return;

            int state = windowRemaining > 0f ? BellStateActive
                : (bellConsumed || !bellAvailable ? BellStateUsed : BellStateReady);
            if (state == _bellShownState && string.Equals(lockedCommandName, _bellShownName, StringComparison.Ordinal))
            {
                UpdateBellWindowBar(windowRemaining);
                return;
            }
            _bellShownState = state;
            _bellShownName = lockedCommandName;

            string prefix = ModeHConfig.LocalizationKeyPrefix;
            string name = string.IsNullOrEmpty(lockedCommandName) ? string.Empty : L10n.T("：", ": ") + lockedCommandName;
            string plain = _bellCommandPlain ?? string.Empty;
            if (state == BellStateActive)
            {
                _bellButton.interactable = false;
                SetBellCard(L10n.T(prefix + "Command_WindowActive") + name, string.Empty, plain,
                    BossRushUIColors.SuccessText, BossRushUIColors.Success, BossRushUIColors.SuccessText);
            }
            else if (state == BellStateUsed)
            {
                _bellButton.interactable = false;
                SetBellCard(L10n.T(prefix + "Command_BellConsumed"), string.Empty,
                    L10n.T(prefix + "Hud_BellUsedHint"),
                    BossRushUIColors.Stroke, BossRushUIColors.Disabled, BossRushUIColors.TextSecondary);
            }
            else
            {
                _bellButton.interactable = true;
                SetBellCard(L10n.T(prefix + "Button_RingBell") + name, L10n.T(prefix + "Hud_BellOncePerMatch"),
                    plain, BossRushUIColors.Accent, BossRushUIColors.Accent, BossRushUIColors.TextPrimary);
            }
            UpdateBellWindowBar(windowRemaining);
        }

        private void SetBellCard(string title, string hint, string subtitle,
            Color stroke, Color badge, Color titleColor)
        {
            SetBellTint(BossRushUIColors.SurfaceRaised);
            if (_bellStroke != null) _bellStroke.color = stroke;
            if (_bellBadge != null) _bellBadge.color = badge;
            if (_bellBadgeText != null) _bellBadgeText.color = BossRushUI.GetButtonTextColor(badge);
            if (_bellTitle != null) { _bellTitle.text = title; _bellTitle.color = titleColor; }
            if (_bellHint != null) { _bellHint.text = hint; _bellHint.color = BossRushUIColors.TextSecondary; }
            if (_bellSubtitle != null) { _bellSubtitle.text = subtitle; _bellSubtitle.color = BossRushUIColors.TextSecondary; }
        }

        private void UpdateBellWindowBar(float windowRemaining)
        {
            if (_bellWindowBar == null) return;
            float fill = ModeHConfig.CommandWindowSeconds > 0f
                ? windowRemaining / ModeHConfig.CommandWindowSeconds
                : 0f;
            _bellWindowBar.fillAmount = Mathf.Clamp01(fill);
        }

        private void SetBellTint(Color color)
        {
            if (_bellButton == null) return;
            // 底色走 ColorBlock：直接写 Image.color 会和 ColorTint 相乘，三态越切越暗。
            // 不可点的两态由 ColorBlock 的 disabledColor 自动压暗，状态差别主要靠描边与徽章颜色。
            ZombieModeUIHelper.SetButtonBaseColor(_bellButton, color);
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
            _bellStroke = null;
            _bellBadge = null;
            _bellBadgeText = null;
            _bellTitle = null;
            _bellHint = null;
            _bellSubtitle = null;
            _bellWindowBar = null;
            _bellShownState = -1;
            _bellShownName = null;
            _lastTimerSeconds = -1;
            _lastEnemyCount = -1;
        }

        #region 诊断覆盖层

        /// <summary>
        /// 生产认证期间的加载页（玩家看到的是「擂台准备中」）：独立实时覆盖层，挂 raycaster 但不 claim
        /// 模态输入、不暂停 `Time.timeScale`。一句白话说明为什么要等、一行「正在请选手上台热身（3/12）」、
        /// 一条进度条；唯一可交互控件是 owner-checked 的「取消并退票」。
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
            float innerWidth = DiagnosticsSize.x - SafeMargin * 2f;
            GameObject notice = ZombieModeUIHelper.CreateRect(
                "ModeH_Body", surface.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 50f), new Vector2(innerWidth, 84f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI noticeText = ZombieModeUIHelper.CreateTMPText(notice,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Diag_ReadOnlyNotice"), 20f,
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(noticeText);

            GameObject progress = ZombieModeUIHelper.CreateRect(
                "ModeH_Progress", surface.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -16f), new Vector2(innerWidth, 40f), new Vector2(0.5f, 0.5f));
            _diagProgressText = ZombieModeUIHelper.CreateTMPText(progress, string.Empty, 24f,
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_diagProgressText);

            GameObject track = ZombieModeUIHelper.CreateRect(
                "ModeH_ProgressTrack", surface.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -58f), new Vector2(innerWidth, 8f), new Vector2(0.5f, 0.5f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Header;
            trackImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(trackImage, 3, BossRushUISkinPart.Hairline);
            GameObject fill = ZombieModeUIHelper.CreateRect(
                "Fill", track.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Vector2(0.5f, 0.5f));
            _diagProgressFill = fill.AddComponent<Image>();
            _diagProgressFill.color = BossRushUIColors.Accent;
            _diagProgressFill.raycastTarget = false;
            _diagProgressFill.sprite = BossRushUI.GetSolidSprite();   // Filled 没有 sprite 时 fillAmount 不生效
            _diagProgressFill.type = Image.Type.Filled;
            _diagProgressFill.fillMethod = Image.FillMethod.Horizontal;
            _diagProgressFill.fillAmount = 0f;

            ZombieModeUIHelper.CreateButton(
                "ModeH_DiagCancel", surface.transform,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_CancelAndRefund"),
                new Vector2(0.5f, 0f), new Vector2(0f, SafeMargin + 28f),
                new Vector2(280f, 52f), BossRushUIColors.Danger, 22f,
                new Vector2(260f, 40f),
                onCancelAndRefund != null ? new UnityEngine.Events.UnityAction(onCancelAndRefund) : null,
                onCancelAndRefund != null);

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>刷新加载页的进度行与进度条。调用方只在「又热完一位」时调。</summary>
        public void UpdateDiagnostics(string progressText, float progress01)
        {
            if (_diagnosticsRoot == null) return;
            if (_diagProgressText != null
                && !string.Equals(_diagProgressText.text, progressText, StringComparison.Ordinal))
            {
                _diagProgressText.text = progressText;
            }
            if (_diagProgressFill != null) _diagProgressFill.fillAmount = Mathf.Clamp01(progress01);
        }

        /// <summary>幂等销毁诊断覆盖层。</summary>
        public void DestroyDiagnostics()
        {
            _diagProgressText = null;
            _diagProgressFill = null;
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
                "ModeH_PageSurface", _modalRoot.transform, size, BossRushUIColors.Accent, createBackdrop: false);

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
            float top = panelSize.y * 0.5f - SafeMargin - 76f;
            float bottom = -panelSize.y * 0.5f + SafeMargin + 96f;
            GameObject obj = ZombieModeUIHelper.CreateRect(
                "ModeH_Body", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, (top + bottom) * 0.5f + offsetY),
                new Vector2(panelSize.x - SafeMargin * 2f, top - bottom),
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
        /// <summary>拍铃卡尺寸：与状态卡同宽，挂在它正下方（整张卡都是点击区）。</summary>
        internal static readonly Vector2 BellCardSize = new Vector2(560f, 112f);
        /// <summary>拍铃卡与状态卡之间的间隙。</summary>
        internal const float BellCardGap = 12f;
        /// <summary>拍铃卡内边距。</summary>
        internal const float BellInnerPadding = 16f;
        /// <summary>拍铃卡左侧圆徽章直径。</summary>
        internal const float BellBadgeSize = 76f;
        /// <summary>拍铃卡右上「每场一次」提示宽度。</summary>
        internal const float BellHintWidth = 120f;
        /// <summary>标题行高：26 号字单行至少 1.45×26+4≈42。</summary>
        internal const float BellTitleHeight = 44f;
        /// <summary>白话说明行高：19 号字单行至少 1.45×19+4≈32。</summary>
        internal const float BellSubtitleHeight = 34f;
        /// <summary>主页面面板尺寸。</summary>
        internal static readonly Vector2 MainPanelSize = new Vector2(1480f, 860f);
        /// <summary>战报面板尺寸。</summary>
        internal static readonly Vector2 ReportPanelSize = new Vector2(1180f, 760f);
        /// <summary>加载页（生产认证）尺寸：一句说明 + 进度行 + 进度条 + 取消键。</summary>
        internal static readonly Vector2 DiagnosticsSize = new Vector2(960f, 400f);
        /// <summary>恢复壳尺寸。</summary>
        internal static readonly Vector2 RecoverySize = new Vector2(1280f, 780f);
        /// <summary>HUD 状态区边距。</summary>
        internal const float StatusMargin = 24f;
        /// <summary>模态页四周安全边距。</summary>
        internal const float SafeMargin = 48f;

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

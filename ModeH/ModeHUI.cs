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
        HallOfFame = 6,
        /// <summary>押背包物品的选择页（2026-09-24，从押注行进入，「完成」回原页）</summary>
        ItemBet = 7
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
    ///
    /// 2026-09-23 审美打磨（审查 UB-01/02/08/14/15/32）：
    /// - 观战 HUD 收小、分层：标签小字次级色、值正文主色；计时 m:ss 等宽数字，最后 10 / 5 秒变色；
    ///   整组从左上官方时间显示下面开始排（与随机事件徽章同一让位口径）；
    /// - 拍铃徽章画模式徽记（不再是圆里写「铃」字），三态颜色 0.2 秒过渡，拍下去徽章弹一下，
    ///   底边倒计时条是圆头细条；
    /// - 同一页刷新（点整备选项、押品格、翻页）只换内容，不重播打开动画、保住滚动位置；
    /// - 关页、收 HUD、收加载页都淡出（BossRushUIKit.PlayCloseAndDestroy），输入租约在淡出**之前**就释放；
    ///   整体销毁（关停 / 卸载）仍是立即销毁：展示 bundle 紧接着要卸载，不能让淡出中的界面还握着徽记贴图。
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
        /// <summary>本页的 ESC / 手柄取消（只在页上有「返回」动作时挂，见 SyncCancelKey）。</summary>
        private PetNestCancelKey _cancelKey;
        private GameObject _modalInputToken;
        private ModeHPage _currentPage;
        private string _currentPageTitle;
        private GameObject _modalSurface;
        private string _modalOwnerLabel;

        private TextMeshProUGUI _hudTimer;
        private TextMeshProUGUI _hudTimerCaption;
        private TextMeshProUGUI _hudStarter;
        private TextMeshProUGUI _hudRelay;
        private TextMeshProUGUI _hudEnemies;
        private Button _bellButton;
        private Button _surrenderButton;
        internal GameObject HudAnchor { get { return _hudRoot; } }
        private Button _exitButton;
        private Image _bellStroke;
        private Image _bellBadge;
        private Image _bellBadgeRing;
        private Image _bellEmblem;
        private RectTransform _bellBadgeRect;
        private TextMeshProUGUI _bellTitle;
        private TextMeshProUGUI _bellHint;
        private TextMeshProUGUI _bellSubtitle;
        private RectTransform _bellWindowFill;
        private Image _bellWindowFillImage;
        private float _bellWindowShown = -1f;
        /// <summary>本场锁定口令的白话说明（开打时由模块写入一次）。</summary>
        private string _bellCommandPlain;
        /// <summary>拍铃卡当前画的是哪一态（-1 = 未画）；只在态或口令名变化时重写文字，避免每次刷新都拼串。</summary>
        private int _bellShownState = -1;
        private string _bellShownName;

        // 拍铃卡三态的颜色过渡与拍下去那一下的徽章回弹。由 TickHud 每帧推进（宿主驱动，走比赛时间：
        // 暂停时不推进），不另挂 Update。
        private Color _bellStrokeFrom, _bellStrokeTo, _bellRingFrom, _bellRingTo;
        private float _bellTweenElapsed = -1f;
        private float _bellPunchElapsed = -1f;

        /// <summary>计时读数的色调：0 常态 / 1 最后 10 秒 / 2 最后 5 秒。只在跨阈值时写颜色。</summary>
        private int _timerTone = -1;

        private TextMeshProUGUI _diagProgressText;
        private RectTransform _diagProgressFill;
        private Image _diagProgressFillImage;
        private float _diagProgressShown;
        private float _diagProgressTarget;

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

        /// <summary>HUD 行首标签的次级色（富文本用，按 token 预先转好，不每次刷新都拼）。</summary>
        private static readonly string HudLabelOpen =
            "<size=" + HudLabelFontSize + "><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">";
        private const string HudLabelClose = "</color></size>  ";

        /// <summary>
        /// 创建观战 HUD。挂 `GraphicRaycaster` 让拍铃按钮可点，
        /// 但**不**调用会暂停时间的 `ClaimModalInput`——角色输入由 spectator lease 阻断。
        /// </summary>
        public void EnsureHud(Action onRingBell, Action onSurrender, Action onExit)
        {
            if (_hudRoot != null) return;

            _hudCanvas = BossRushUI.CreateCanvasRoot(
                "ModeH_HUD", BossRushUILayers.ModeHHud, true);
            _hudRoot = _hudCanvas.gameObject;
            UnityEngine.Object.DontDestroyOnLoad(_hudRoot);

            // 状态区：左上，从官方时间显示下沿开始（HudTop），三行「小字标签  正文值」
            GameObject status = ZombieModeUIHelper.CreateRect(
                "ModeH_Status", _hudRoot.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(StatusMargin + StatusSize.x * 0.5f, -(HudTop + StatusSize.y * 0.5f)),
                StatusSize, new Vector2(0.5f, 0.5f));
            Image statusBackground = status.AddComponent<Image>();
            statusBackground.color = BossRushUIColors.Surface;
            statusBackground.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(statusBackground, 10, BossRushUISkinPart.Card);

            _hudStarter = CreateHudLine(status.transform, "Starter", 38f, StatusSize.x - 32f);
            _hudRelay = CreateHudLine(status.transform, "Relay", 0f, StatusSize.x - 32f);
            _hudEnemies = CreateHudLine(status.transform, "Enemies", -38f, StatusSize.x - 32f);

            // 计时区：顶部居中，上一行小字「剩余时间」，下一行 m:ss 等宽大数字
            GameObject timer = ZombieModeUIHelper.CreateRect(
                "ModeH_Timer", _hudRoot.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(StatusMargin + TimerSize.y * 0.5f)),
                TimerSize, new Vector2(0.5f, 0.5f));
            Image timerBackground = timer.AddComponent<Image>();
            timerBackground.color = BossRushUIColors.Surface;
            timerBackground.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(timerBackground, 10, BossRushUISkinPart.Card);
            _hudTimerCaption = CreateHudLine(timer.transform, "TimerCaption", 22f, TimerSize.x - 32f,
                HudLabelFontSize, TimerCaptionHeight);
            _hudTimerCaption.alignment = TextAlignmentOptions.Center;
            _hudTimerCaption.color = BossRushUIColors.TextSecondary;
            _hudTimer = CreateHudLine(timer.transform, "TimerText", -8f, TimerSize.x - 32f,
                TimerFontSize, TimerDigitsHeight);
            _hudTimer.alignment = TextAlignmentOptions.Center;
            _timerTone = -1;

            CreateBellButton(onRingBell);
            CreateSpectatorActions(onSurrender, onExit);
            BossRushUI.PlayOpenAnimation(_hudRoot);
        }

        /// <summary>观战期右侧的投降与退出：小型次级按钮，不遮挡中央战场。</summary>
        private void CreateSpectatorActions(Action onSurrender, Action onExit)
        {
            float x = -SpectatorActionMargin - SpectatorActionSize.x * 0.5f;
            _surrenderButton = ZombieModeUIHelper.CreateButton(
                "ModeH_Surrender", _hudRoot.transform,
                L10n.T("投降", "Surrender"),
                new Vector2(1f, 0.5f), new Vector2(x, SpectatorActionGap * 0.5f),
                SpectatorActionSize, BossRushUIColors.SurfaceRaised, 17f,
                new Vector2(SpectatorActionSize.x - 16f, SpectatorActionSize.y - 8f),
                onSurrender != null ? new UnityEngine.Events.UnityAction(onSurrender) : null,
                onSurrender != null);
            BossRushUIKit.StyleSecondaryButton(_surrenderButton);
            Image surrenderImage = _surrenderButton != null ? _surrenderButton.targetGraphic as Image : null;
            if (surrenderImage != null)
                BossRushUI.ApplyPanelStroke(surrenderImage, 8, BossRushUISkinPart.Button, BossRushUIColors.DangerText);
            TextMeshProUGUI surrenderLabel = _surrenderButton != null ? _surrenderButton.GetComponentInChildren<TextMeshProUGUI>() : null;
            if (surrenderLabel != null) surrenderLabel.color = BossRushUIColors.DangerText;

            _exitButton = ZombieModeUIHelper.CreateButton(
                "ModeH_SpectatorExit", _hudRoot.transform,
                L10n.T("退出", "Exit"),
                new Vector2(1f, 0.5f), new Vector2(x, -SpectatorActionGap * 0.5f),
                SpectatorActionSize, BossRushUIColors.SurfaceRaised, 17f,
                new Vector2(SpectatorActionSize.x - 16f, SpectatorActionSize.y - 8f),
                onExit != null ? new UnityEngine.Events.UnityAction(onExit) : null,
                onExit != null);
            BossRushUIKit.StyleSecondaryButton(_exitButton);
        }

        private TextMeshProUGUI CreateHudLine(Transform parent, string name, float offsetY, float width)
        {
            return CreateHudLine(parent, name, offsetY, width, HudValueFontSize, HudLineHeight);
        }

        /// <summary>
        /// HUD 的一行字：关掉自动缩字（内容一变字号就跳），单行、放不下按省略号收。
        /// 框高至少 1.45×字号 + 4：TMP 的 Ellipsis 在框比一行还矮时会把整串清空。
        /// </summary>
        private TextMeshProUGUI CreateHudLine(Transform parent, string name, float offsetY, float width,
            float fontSize, float height)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, offsetY), new Vector2(width, height),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, string.Empty, fontSize, TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(text);
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        /// <summary>
        /// 拍铃卡：全场唯一主动操作。整张卡就是按钮，挂在左上状态卡正下方（旧版在底部正中，
        /// 正好压住官方快捷栏）。左边一个圆徽章（模式徽记 + 状态色细环），右边「拍铃：口令名」+ 一句白话说明，
        /// 底边一条圆头细条是口令生效的 6 秒倒计时。三态：可拍（青色描边）/ 照做中（绿色）/ 已用完（灰）。
        /// 徽记取不到时不画徽章、文字左移（不退回汉字当图标）。
        /// </summary>
        private void CreateBellButton(Action onRingBell)
        {
            GameObject card = ZombieModeUIHelper.CreateRect(
                "ModeH_Bell", _hudRoot.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(StatusMargin + BellCardSize.x * 0.5f,
                    -(HudTop + StatusSize.y + BellCardGap + BellCardSize.y * 0.5f)),
                BellCardSize, new Vector2(0.5f, 0.5f));
            Image background = card.AddComponent<Image>();
            BossRushUI.ApplyPanelSkin(background, 10, BossRushUISkinPart.Card);
            // 描边先于按钮配色挂上：投影与斜面按卡片档（圆角与卡片底图一致）生成，按钮配色那一步见到已有的就跳过
            _bellStroke = BossRushUI.ApplyPanelStroke(background, 10, BossRushUISkinPart.Card, BossRushUIColors.Accent);
            _bellButton = card.AddComponent<Button>();
            _bellButton.targetGraphic = background;
            _bellButton.navigation = new Navigation { mode = Navigation.Mode.None };
            // 底色走 ColorBlock（SetButtonBaseColor 把 Graphic 置成乘法底）：直接写 Image.color 会和 ColorTint 相乘，
            // 三态越切越暗。不可点的两态由 ColorBlock 的 disabledColor 自动压暗，状态差别主要靠描边与徽章。
            ZombieModeUIHelper.SetButtonBaseColor(_bellButton, BellCardBase);

            float textLeft = BellInnerPadding;
            Sprite emblem = ModeHPresentationAssetCache.GetEmblemSprite();
            if (emblem != null)
            {
                GameObject badge = ZombieModeUIHelper.CreateRect(
                    "Badge", card.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(BellInnerPadding + BellBadgeSize * 0.5f, 0f),
                    new Vector2(BellBadgeSize, BellBadgeSize), new Vector2(0.5f, 0.5f));
                _bellBadgeRect = badge.GetComponent<RectTransform>();
                _bellBadge = badge.AddComponent<Image>();
                _bellBadge.color = BossRushUIColors.Header;
                _bellBadge.raycastTarget = false;
                _bellBadge.sprite = BossRushUI.GetRoundedSprite((int)(BellBadgeSize * 0.5f));
                _bellBadge.type = Image.Type.Sliced;
                // 细环走细条档：它按调用方半径取程序化圆环，与圆底严丝合缝（卡片档注入图集后弧半径会变成 14，圆会变圆角方）
                _bellBadgeRing = BossRushUI.ApplyPanelStroke(_bellBadge, (int)(BellBadgeSize * 0.5f),
                    BossRushUISkinPart.Hairline, BossRushUIColors.Accent);
                RectTransform emblemRect = BossRushUIHero.CreateEmblem(badge.transform, "Emblem", emblem, BellEmblemSize);
                _bellEmblem = emblemRect != null ? emblemRect.GetComponent<Image>() : null;
                textLeft = BellInnerPadding + BellBadgeSize + 12f;
            }

            float textWidth = BellCardSize.x - textLeft - BellInnerPadding;
            _bellTitle = CreateBellText(card.transform, "Title", string.Empty, 20f, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(textLeft, 14f),
                new Vector2(textWidth - BellHintWidth, BellTitleHeight));
            _bellHint = CreateBellText(card.transform, "Hint", string.Empty, 13f, TextAlignmentOptions.Right,
                new Vector2(1f, 0.5f), new Vector2(-BellInnerPadding, 14f), new Vector2(BellHintWidth, BellHintHeight));
            _bellSubtitle = CreateBellText(card.transform, "Subtitle", string.Empty, 15f, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(textLeft, -15f), new Vector2(textWidth, BellSubtitleHeight));

            if (onRingBell != null)
            {
                _bellButton.onClick.RemoveAllListeners();
                _bellButton.onClick.AddListener(delegate { onRingBell(); });
            }

            // 口令窗口倒计时：卡片底边一条圆头细条，按剩余比例从右往左收（改锚点宽度，圆角两端不被压扁）
            GameObject track = ZombieModeUIHelper.CreateRect(
                "ModeH_BellWindow", card.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 6f), new Vector2(-BellInnerPadding * 2f, BellWindowHeight), new Vector2(0.5f, 0f));
            GameObject fill = ZombieModeUIHelper.CreateRect(
                "Fill", track.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero,
                new Vector2(0f, 0.5f));
            _bellWindowFill = fill.GetComponent<RectTransform>();
            _bellWindowFillImage = fill.AddComponent<Image>();
            _bellWindowFillImage.color = BossRushUIColors.SuccessText;
            _bellWindowFillImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(_bellWindowFillImage, 2, BossRushUISkinPart.Hairline);
            _bellWindowFillImage.enabled = false;
            _bellWindowShown = -1f;

            _bellShownState = -1;
            _bellTweenElapsed = -1f;
            _bellPunchElapsed = -1f;
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
            text.enableAutoSizing = false;
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
                // 置 -1 重画：同一态只换口令名，按「首次绘制」直接落色，不播颜色过渡
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
        /// 加载页进度条的显示值也在这里按真实时间追目标值（MoveTowards，与帧率无关）：
        /// 它同样是宿主每帧驱动的常驻页，让位或暂停时不推进。
        /// </summary>
        public void ApplyHudVisibility()
        {
            if (_hudCanvas == null && _diagnosticsCanvas == null) return;
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (_hudCanvas != null && _hudCanvas.enabled != visible) _hudCanvas.enabled = visible;
            // 认证期的诊断页同一口径（2026-09-14 拍板，原待拍板 #10）：它自动挂出、不占模态输入、不停时间，
            // 「取消并退款」按钮是可选的——性质是常驻进度页，不是玩家打开的模态；不跟随的话 970 层会压在背包与地图上面。
            if (_diagnosticsCanvas != null && _diagnosticsCanvas.enabled != visible) _diagnosticsCanvas.enabled = visible;
            if (visible && _diagProgressFill != null && _diagProgressShown != _diagProgressTarget)
            {
                _diagProgressShown = Mathf.MoveTowards(_diagProgressShown, _diagProgressTarget,
                    Time.unscaledDeltaTime * DiagProgressSpeed);
                ApplyDiagnosticsFill(_diagProgressShown);
            }
        }

        /// <summary>
        /// 刷新 HUD。只在值变化或最多 `HudRefreshIntervalSeconds` 一次时写文本，
        /// 避免每帧字符串分配。拍铃卡的颜色过渡、徽章回弹与倒计时条每帧推进（纯数值写入，无分配）。
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

            AdvanceBellMotion(deltaTime);
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

            string prefix = ModeHConfig.LocalizationKeyPrefix;
            if (_hudTimer != null)
            {
                _hudTimerCaption.text = L10n.T(prefix + "Label_TimeRemaining");
                _hudTimer.text = FormatClock(timerSeconds);
                int tone = timerSeconds <= TimerDangerSeconds ? 2 : (timerSeconds <= TimerWarningSeconds ? 1 : 0);
                if (tone != _timerTone)
                {
                    _timerTone = tone;
                    _hudTimer.color = tone == 2 ? BossRushUIColors.DangerText
                        : (tone == 1 ? BossRushUIColors.WarningText : BossRushUIColors.TextPrimary);
                }
            }
            if (_hudStarter != null)
            {
                _hudStarter.text = HudLabelOpen + L10n.T(prefix + "Label_MatchStarter") + HudLabelClose
                    + (starterName != null ? starterName : "-");
            }
            if (_hudRelay != null)
            {
                _hudRelay.text = HudLabelOpen + L10n.T(prefix + "Label_MatchRelay") + HudLabelClose
                    + (relayName != null ? relayName : "-");
            }
            if (_hudEnemies != null)
            {
                // 这一行是**场上还活着的敌人数**。旧版借用了看盘页「人数区间」的标签，
                // 玩家看到「人数区间 2」不知道在说什么（2026-09-23 owner 实测截图）。
                _hudEnemies.text = HudLabelOpen + L10n.T(prefix + "Hud_EnemiesLeft") + HudLabelClose
                    + liveEnemyCount;
            }

            UpdateBellState(bellAvailable, bellConsumed, lockedCommandName, commandWindowRemaining);
        }

        /// <summary>剩余秒数 → 「m:ss」等宽数字（秒数每秒才变一次，这里的拼串在节流之内）。</summary>
        private static string FormatClock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int minutes = seconds / 60;
            int rest = seconds % 60;
            return "<mspace=0.56em>" + minutes + ":" + (rest < 10 ? "0" : string.Empty) + rest + "</mspace>";
        }

        private const int BellStateReady = 0;
        private const int BellStateActive = 1;
        private const int BellStateUsed = 2;

        /// <summary>
        /// 三态：可拍（青色描边、可点）/ 照做中（绿色、倒计时线）/ 本场已用完（灰）。
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
            int previous = _bellShownState;
            _bellShownState = state;
            _bellShownName = lockedCommandName;

            string prefix = ModeHConfig.LocalizationKeyPrefix;
            string name = string.IsNullOrEmpty(lockedCommandName) ? string.Empty : L10n.T("：", ": ") + lockedCommandName;
            string plain = _bellCommandPlain ?? string.Empty;
            if (state == BellStateActive)
            {
                _bellButton.interactable = false;
                SetBellCard(L10n.T(prefix + "Command_WindowActive") + name, string.Empty, plain,
                    BossRushUIColors.SuccessText, BossRushUIColors.SuccessText, BossRushUIHero.ArtTint);
            }
            else if (state == BellStateUsed)
            {
                _bellButton.interactable = false;
                SetBellCard(L10n.T(prefix + "Command_BellConsumed"), string.Empty,
                    L10n.T(prefix + "Hud_BellUsedHint"),
                    BossRushUIColors.Stroke, BossRushUIColors.TextSecondary, BellEmblemDimmed);
            }
            else
            {
                _bellButton.interactable = true;
                SetBellCard(L10n.T(prefix + "Button_RingBell") + name, L10n.T(prefix + "Hud_BellOncePerMatch"),
                    plain, BossRushUIColors.Accent, BossRushUI.GetButtonTextColor(BellCardBase), BossRushUIHero.ArtTint);
            }

            if (previous == -1)
            {
                SnapBellColors();
            }
            else
            {
                StartBellTween();
                // 拍下去那一下的回执：徽章弹一下（只在「可拍 → 照做中」这条边上）
                if (previous == BellStateReady && state == BellStateActive) _bellPunchElapsed = 0f;
            }
            UpdateBellWindowBar(windowRemaining);
        }

        /// <summary>写文字与目标色。描边 / 徽章细环按 <see cref="StartBellTween"/> 过渡，标题色直接换（字色硬切不刺眼）。</summary>
        private void SetBellCard(string title, string hint, string subtitle, Color stateColor, Color titleColor,
            Color emblemTint)
        {
            _bellStrokeTo = stateColor;
            _bellRingTo = stateColor;
            if (_bellEmblem != null) _bellEmblem.color = emblemTint;
            if (_bellTitle != null) { _bellTitle.text = title; _bellTitle.color = titleColor; }
            if (_bellHint != null) { _bellHint.text = hint; _bellHint.color = BossRushUIColors.TextSecondary; }
            if (_bellSubtitle != null) { _bellSubtitle.text = subtitle; _bellSubtitle.color = BossRushUIColors.TextSecondary; }
        }

        private void StartBellTween()
        {
            _bellStrokeFrom = _bellStroke != null ? _bellStroke.color : _bellStrokeTo;
            _bellRingFrom = _bellBadgeRing != null ? _bellBadgeRing.color : _bellRingTo;
            _bellTweenElapsed = 0f;
        }

        private void SnapBellColors()
        {
            _bellTweenElapsed = -1f;
            if (_bellStroke != null) _bellStroke.color = _bellStrokeTo;
            if (_bellBadgeRing != null) _bellBadgeRing.color = _bellRingTo;
        }

        /// <summary>颜色过渡（0.2 秒 SmoothStep，原地变色）与徽章回弹（0.25 秒，1 → 1.12 → 1）。O(1)、零分配。</summary>
        private void AdvanceBellMotion(float deltaTime)
        {
            if (_bellTweenElapsed >= 0f)
            {
                _bellTweenElapsed += deltaTime;
                float t = BossRushUI.SmoothStep(_bellTweenElapsed / BellTweenSeconds);
                if (_bellStroke != null) _bellStroke.color = Color.Lerp(_bellStrokeFrom, _bellStrokeTo, t);
                if (_bellBadgeRing != null) _bellBadgeRing.color = Color.Lerp(_bellRingFrom, _bellRingTo, t);
                if (_bellTweenElapsed >= BellTweenSeconds) _bellTweenElapsed = -1f;
            }
            if (_bellPunchElapsed >= 0f && _bellBadgeRect != null)
            {
                _bellPunchElapsed += deltaTime;
                float t = Mathf.Clamp01(_bellPunchElapsed / BellPunchSeconds);
                // 前 40% 用 EaseOut 冲到峰值，后 60% 用 SmoothStep 落回：像按下去的东西弹一下再停稳
                float scale = t < 0.4f
                    ? Mathf.Lerp(1f, BellPunchScale, BossRushUI.EaseOut(t / 0.4f))
                    : Mathf.Lerp(BellPunchScale, 1f, BossRushUI.SmoothStep((t - 0.4f) / 0.6f));
                _bellBadgeRect.localScale = new Vector3(scale, scale, 1f);
                if (t >= 1f)
                {
                    _bellPunchElapsed = -1f;
                    _bellBadgeRect.localScale = Vector3.one;
                }
            }
        }

        private void UpdateBellWindowBar(float windowRemaining)
        {
            if (_bellWindowFill == null) return;
            float fill = ModeHConfig.CommandWindowSeconds > 0f
                ? Mathf.Clamp01(windowRemaining / ModeHConfig.CommandWindowSeconds)
                : 0f;
            if (Mathf.Abs(fill - _bellWindowShown) < 0.001f) return;
            _bellWindowShown = fill;
            _bellWindowFill.anchorMax = new Vector2(fill, 1f);
            // 比两端圆头还短时整条藏掉：九宫格在宽度小于两个圆角时会被压成一个扁点
            if (_bellWindowFillImage != null) _bellWindowFillImage.enabled = fill > 0.02f;
        }

        #endregion

        /// <summary>收起观战 HUD（淡出）；结算页仍由同一 UI owner 继续使用。</summary>
        public void DestroyHud()
        {
            DestroyHud(false);
        }

        private void DestroyHud(bool immediate)
        {
            if (_hudRoot != null)
            {
                GameObject root = _hudRoot;
                _hudRoot = null;
                _hudCanvas = null;
                DisposeRoot(root, immediate);
            }
            _hudTimer = null;
            _hudTimerCaption = null;
            _hudStarter = null;
            _hudRelay = null;
            _hudEnemies = null;
            _bellButton = null;
            _surrenderButton = null;
            _exitButton = null;
            _bellStroke = null;
            _bellBadge = null;
            _bellBadgeRing = null;
            _bellEmblem = null;
            _bellBadgeRect = null;
            _bellTitle = null;
            _bellHint = null;
            _bellSubtitle = null;
            _bellWindowFill = null;
            _bellWindowFillImage = null;
            _bellWindowShown = -1f;
            _bellShownState = -1;
            _bellShownName = null;
            _bellTweenElapsed = -1f;
            _bellPunchElapsed = -1f;
            _timerTone = -1;
            _lastTimerSeconds = -1;
            _lastEnemyCount = -1;
        }

        /// <summary>
        /// 界面根的收尾：平常淡出（先改名，免得淡出中的旧根被按名字找到，例如 F3 验收的 GameObject.Find）；
        /// 整体销毁时立即销毁（展示 bundle 紧接着要卸载）。
        /// </summary>
        private static void DisposeRoot(GameObject root, bool immediate)
        {
            if (root == null) return;
            if (immediate)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            root.name = root.name + "_Closing";
            BossRushUIKit.PlayCloseAndDestroy(root);
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
                new Vector2(0f, 46f), new Vector2(innerWidth, 84f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI noticeText = ZombieModeUIHelper.CreateTMPText(notice,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Diag_ReadOnlyNotice"), 17f,
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(noticeText);

            GameObject progress = ZombieModeUIHelper.CreateRect(
                "ModeH_Progress", surface.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -18f), new Vector2(innerWidth, 34f), new Vector2(0.5f, 0.5f));
            _diagProgressText = ZombieModeUIHelper.CreateTMPText(progress, string.Empty, 20f,
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_diagProgressText);

            GameObject track = ZombieModeUIHelper.CreateRect(
                "ModeH_ProgressTrack", surface.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -56f), new Vector2(innerWidth, 8f), new Vector2(0.5f, 0.5f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Header;
            trackImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(trackImage, 3, BossRushUISkinPart.Hairline);
            // 填充与轨道同一圆头细条，按进度改锚点宽度（旧写法是直角 Filled 条，满格时四角戳出圆角轨道）
            GameObject fill = ZombieModeUIHelper.CreateRect(
                "Fill", track.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero,
                new Vector2(0f, 0.5f));
            _diagProgressFill = fill.GetComponent<RectTransform>();
            _diagProgressFillImage = fill.AddComponent<Image>();
            _diagProgressFillImage.color = BossRushUIColors.Accent;
            _diagProgressFillImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(_diagProgressFillImage, 3, BossRushUISkinPart.Hairline);
            _diagProgressShown = 0f;
            _diagProgressTarget = 0f;
            ApplyDiagnosticsFill(0f);

            // 「取消并退票」是退出，不是破坏性操作：次级样式（旧版整块危险红，一张加载页上最显眼的就是它）
            Button cancel = ZombieModeUIHelper.CreateButton(
                "ModeH_DiagCancel", surface.transform,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_CancelAndRefund"),
                new Vector2(0.5f, 0f), new Vector2(0f, SafeMargin + 26f),
                new Vector2(260f, 52f), BossRushUIColors.SurfaceRaised, 20f,
                new Vector2(240f, 40f),
                onCancelAndRefund != null ? new UnityEngine.Events.UnityAction(onCancelAndRefund) : null,
                onCancelAndRefund != null);
            BossRushUIKit.StyleSecondaryButton(cancel);

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>刷新加载页的进度行与进度条目标值。调用方只在「又热完一位」时调；条的显示值由 ApplyHudVisibility 平滑追上。</summary>
        public void UpdateDiagnostics(string progressText, float progress01)
        {
            if (_diagnosticsRoot == null) return;
            if (_diagProgressText != null
                && !string.Equals(_diagProgressText.text, progressText, StringComparison.Ordinal))
            {
                _diagProgressText.text = progressText;
            }
            _diagProgressTarget = Mathf.Clamp01(progress01);
            // 往回退（重试从头再来）不做倒放动画，直接落位
            if (_diagProgressTarget < _diagProgressShown)
            {
                _diagProgressShown = _diagProgressTarget;
                ApplyDiagnosticsFill(_diagProgressShown);
            }
        }

        private void ApplyDiagnosticsFill(float value)
        {
            if (_diagProgressFill == null) return;
            _diagProgressFill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            if (_diagProgressFillImage != null) _diagProgressFillImage.enabled = value > 0.01f;
        }

        /// <summary>幂等收起诊断覆盖层（淡出）。</summary>
        public void DestroyDiagnostics()
        {
            DestroyDiagnostics(false);
        }

        private void DestroyDiagnostics(bool immediate)
        {
            _diagProgressText = null;
            _diagProgressFill = null;
            _diagProgressFillImage = null;
            _diagProgressShown = 0f;
            _diagProgressTarget = 0f;
            if (_diagnosticsRoot == null) return;
            GameObject root = _diagnosticsRoot;
            _diagnosticsRoot = null;
            _diagnosticsCanvas = null;
            DisposeRoot(root, immediate);
        }

        #endregion

        #region 模态页面

        /// <summary>
        /// 打开一个非战斗模态页。六个页面共用**唯一**一个 modal lease：
        /// 页面切换只换内容，不重复 claim。
        ///
        /// 同一页刷新（同页、同标题：点整备选项、押品格、下注档、翻页）只换内容——不重播打开动画、
        /// 不重播卡片入场、滚动位置原样保住（审查 UB-08：旧版每点一次整块面板闪一下，列表跳回顶部）。
        /// </summary>
        public void OpenPage(ModeHPage page, ModeHLifecycle lifecycle, string runId, ModeHPageContent content)
        {
            if (page == ModeHPage.None)
            {
                ClosePage();
                return;
            }

            bool refresh = _modalRoot != null && _modalSurface != null && _currentPage == page
                && content != null && string.Equals(_currentPageTitle, content.Title, StringComparison.Ordinal);
            List<float> scroll = refresh ? CaptureScroll(_modalSurface) : null;

            EnsureModalRoot(lifecycle, runId);
            ClearModalContent();
            _currentPage = page;
            _currentPageTitle = content != null ? content.Title : null;

            Vector2 size = page == ModeHPage.Settlement ? ReportPanelSize : MainPanelSize;
            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeH_PageSurface", _modalRoot.transform, size, BossRushUIColors.Accent, createBackdrop: false);
            _modalSurface = surface;

            if (content != null) content.Refresh = refresh;
            ModeHUIPages.Build(page, surface.transform, size, content);
            SyncCancelKey(content);
            if (refresh) RestoreScroll(surface, scroll);
            else BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>
        /// ESC / 手柄取消（2026-09-24）：本页有标了 IsCancel 的动作（整备页、押物品选择页的「完成」）就等于点它。
        /// 没有返回语义的页（选人、看盘、赔率、结算、转会、名人堂）不接：那些页没有可退回的地方，
        /// 吞掉 ESC 会让玩家在这里打不开官方暂停菜单；暂停菜单由官方 TimeScaleManager 压时间，盖在本页上无害。
        /// 共享确认框或恢复壳盖在上面时让给它们。
        /// </summary>
        private void SyncCancelKey(ModeHPageContent content)
        {
            Action cancel = null;
            if (content != null)
            {
                for (int i = 0; i < content.Actions.Count; i++)
                {
                    ModeHActionData action = content.Actions[i];
                    if (action == null || !action.IsCancel || !action.Interactable || action.OnClick == null) continue;
                    cancel = action.OnClick;
                    break;
                }
            }
            if (cancel == null || _modalRoot == null)
            {
                DetachCancelKey();
                return;
            }
            _cancelKey = PetNestCancelKey.Attach(_modalRoot, cancel, IsCancelCovered);
        }

        private static bool IsCancelCovered()
        {
            return BossRushConfirmDialog.IsOpen || ModeHRecoveryPanel.AnyVisible;
        }

        private void DetachCancelKey()
        {
            if (_cancelKey == null) return;
            try { _cancelKey.Detach(); }
            catch (Exception) { /* 画布已销毁：订阅随组件 OnDestroy 退掉 */ }
            _cancelKey = null;
        }

        /// <summary>记下旧页面里每个滚动区的位置（按层级顺序）。只在玩家点击时调用。</summary>
        private static List<float> CaptureScroll(GameObject surface)
        {
            List<float> positions = new List<float>();
            if (surface == null) return positions;
            ScrollRect[] scrolls = surface.GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < scrolls.Length; i++) positions.Add(scrolls[i].verticalNormalizedPosition);
            return positions;
        }

        /// <summary>新页面的滚动区数量与旧页面一致时逐个写回（setter 会先补一次布局再算边界）。</summary>
        private static void RestoreScroll(GameObject surface, List<float> positions)
        {
            if (surface == null || positions == null || positions.Count == 0) return;
            ScrollRect[] scrolls = surface.GetComponentsInChildren<ScrollRect>(true);
            if (scrolls.Length != positions.Count) return;
            for (int i = 0; i < scrolls.Length; i++)
            {
                try { scrolls[i].verticalNormalizedPosition = Mathf.Clamp01(positions[i]); }
                catch (Exception) { /* 布局还没就绪：留在顶部即可，不影响操作 */ }
            }
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
            _modalSurface = null;
            if (_modalRoot == null) return;
            for (int i = _modalRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _modalRoot.transform.GetChild(i);
                if (child == null) continue;
                if (string.Equals(child.name, "Backdrop", StringComparison.Ordinal)) continue;
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        /// <summary>关闭模态页并释放唯一租约（界面淡出）。幂等：重复调用安全。</summary>
        public void ClosePage()
        {
            ClosePage(false);
        }

        private void ClosePage(bool immediate)
        {
            DetachCancelKey();
            _currentPage = ModeHPage.None;
            _currentPageTitle = null;
            _modalSurface = null;
            // 输入不等动画：租约在淡出开始前就还回去
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
                GameObject root = _modalRoot;
                _modalRoot = null;
                _modalCanvas = null;
                DisposeRoot(root, immediate);
            }
        }

        #endregion

        #region 共享构件

        /// <summary>
        /// 页面标题：32 号主色，左边一枚模式徽记，按标题实际宽度整组居中（徽记取不到时标题单独居中）。
        /// </summary>
        internal static TextMeshProUGUI CreateTitle(Transform parent, string title, Vector2 panelSize)
        {
            float centerY = panelSize.y * 0.5f - (SafeMargin + 24f);
            float maxWidth = panelSize.x - SafeMargin * 2f;
            GameObject obj = ZombieModeUIHelper.CreateRect(
                "ModeH_Title", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, centerY), new Vector2(maxWidth, TitleHeight), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, title ?? string.Empty, TitleFontSize, TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(text);
            RectTransform emblem = BossRushUIHero.CreateEmblem(parent, "ModeH_TitleEmblem",
                ModeHPresentationAssetCache.GetEmblemSprite(), TitleEmblemSize);
            BossRushUIHero.PlaceTitleRow(text, emblem, TitleEmblemGap, maxWidth, new Vector2(0f, centerY));
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
                obj, body, BodyFontSize, TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
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

        /// <summary>
        /// HUD 顶边距屏幕顶边的距离：左上角是官方时间 / 天气显示，状态卡与拍铃卡从它下面开始排
        /// （与随机事件徽章 RandomEventsTuning.HudBadgeMarginY 同一让位口径；Mode H 期间随机事件整局禁用，两者不会撞）。
        /// </summary>
        internal const float HudTop = 140f;
        /// <summary>状态区固定尺寸（2026-09-23 从 560×220 收小：三行 18 号正文 + 14 号标签）。</summary>
        internal static readonly Vector2 StatusSize = new Vector2(400f, 140f);
        /// <summary>状态行值的字号；行首标签小一号走次级色。</summary>
        internal const float HudValueFontSize = 18f;
        internal const float HudLabelFontSize = 14f;
        /// <summary>状态行框高：18 号字单行至少 1.45×18+4≈30。</summary>
        internal const float HudLineHeight = 34f;
        /// <summary>计时区固定尺寸：上一行小字、下一行大数字。</summary>
        internal static readonly Vector2 TimerSize = new Vector2(200f, 76f);
        internal const float TimerFontSize = 30f;
        /// <summary>计时数字框高：30 号字单行至少 1.45×30+4≈48。</summary>
        internal const float TimerDigitsHeight = 48f;
        /// <summary>计时小字框高：14 号字单行至少 1.45×14+4≈25。</summary>
        internal const float TimerCaptionHeight = 24f;
        /// <summary>最后几秒变色：先警示色、再危险色。</summary>
        internal const int TimerWarningSeconds = 10;
        internal const int TimerDangerSeconds = 5;
        /// <summary>拍铃卡尺寸：与状态卡同宽，挂在它正下方（整张卡都是点击区）。</summary>
        internal static readonly Vector2 BellCardSize = new Vector2(400f, 88f);
        /// <summary>拍铃卡与状态卡之间的间隙。</summary>
        internal const float BellCardGap = 10f;
        /// <summary>拍铃卡内边距。</summary>
        internal const float BellInnerPadding = 14f;
        /// <summary>拍铃卡左侧圆徽章直径与里面的徽记尺寸。</summary>
        internal const float BellBadgeSize = 56f;
        internal const float BellEmblemSize = 42f;
        /// <summary>拍铃卡右上「每场一次」提示宽度与框高（13 号字至少 1.45×13+4≈23）。</summary>
        internal const float BellHintWidth = 96f;
        internal const float BellHintHeight = 24f;
        /// <summary>标题行高：20 号字单行至少 1.45×20+4≈33。</summary>
        internal const float BellTitleHeight = 34f;
        /// <summary>白话说明行高：15 号字单行至少 1.45×15+4≈26。</summary>
        internal const float BellSubtitleHeight = 26f;
        /// <summary>口令倒计时细条高度。</summary>
        internal const float BellWindowHeight = 3f;
        /// <summary>拍铃卡底色：与卡片同一档深色（按钮三态由 ColorBlock 派生）。</summary>
        internal static readonly Color BellCardBase = BossRushUIColors.SurfaceRaised;
        /// <summary>「已用完」态的徽记压暗（插图乘色）。</summary>
        internal static readonly Color BellEmblemDimmed = new Color(1f, 1f, 1f, 0.35f);
        internal const float BellTweenSeconds = 0.2f;
        internal const float BellPunchSeconds = 0.25f;
        internal const float BellPunchScale = 1.12f;
        /// <summary>加载页进度条追目标值的速度（每秒走满条的 1.5 倍）。</summary>
        internal const float DiagProgressSpeed = 1.5f;
        /// <summary>页面标题：32 号，框高至少 1.45×32+4≈51。</summary>
        internal const float TitleFontSize = 32f;
        internal const float TitleHeight = 56f;
        internal const float TitleEmblemSize = 40f;
        internal const float TitleEmblemGap = 12f;
        /// <summary>整段正文（恢复壳、兜底正文）的字号：正文 16–18 档。</summary>
        internal const float BodyFontSize = 17f;
        /// <summary>主页面面板尺寸。</summary>
        internal static readonly Vector2 MainPanelSize = new Vector2(1480f, 860f);
        /// <summary>战报面板尺寸。</summary>
        internal static readonly Vector2 ReportPanelSize = new Vector2(1180f, 760f);
        /// <summary>加载页（生产认证）尺寸：一句说明 + 进度行 + 进度条 + 取消键。</summary>
        internal static readonly Vector2 DiagnosticsSize = new Vector2(960f, 400f);
        /// <summary>恢复壳尺寸。</summary>
        internal static readonly Vector2 RecoverySize = new Vector2(1280f, 780f);
        /// <summary>HUD 左边距（计时区的顶边距）。</summary>
        internal const float StatusMargin = 24f;
        /// <summary>观战操作按钮尺寸与右侧间距。</summary>
        internal static readonly Vector2 SpectatorActionSize = new Vector2(148f, 48f);
        internal const float SpectatorActionGap = 64f;
        internal const float SpectatorActionMargin = 28f;
        /// <summary>模态页四周安全边距。</summary>
        internal const float SafeMargin = 48f;

        #endregion

        #region 生命周期

        /// <summary>
        /// 幂等销毁全部 Mode H UI。关停 / 卸载路径用：**立即**销毁，不淡出——
        /// 展示 bundle 紧接着可能被卸载，淡出中的界面不能还握着徽记与横幅贴图。
        /// </summary>
        public void DestroyAll()
        {
            ClosePage(true);
            DestroyDiagnostics(true);
            DestroyHud(true);
        }

        #endregion
    }

    /// <summary>
    /// 擂台规则区地面圈的表现层（审查 UB-17）：颜色走 token、开圈 0.35 秒展开并轻呼吸
    /// （复用撤离环的 SkyIslandGroundRingPulse，同一套开环回执），收圈 0.3 秒淡出、带宽收一点再销毁。
    /// **只改表现**：半径与判定仍由 ModeHMatchRules 决定，一帧都不动。
    ///
    /// 旧版掩体圈是冷霓虹蓝 (0.35,0.8,1)、危险圈是纯橙，压在原版暖琥珀的地面上很跳；开打瞬间凭空出现、结束瞬间消失。
    /// 放在这里而不是 ModeHMatchRules.cs：规则类在隔离回归里用桩跑，表现层依赖的 Unity 组件不进桩。
    /// 淡出用挂在环自己身上的协程（不另挂 Update）：环随场景销毁时协程一起停。
    /// </summary>
    internal static class ModeHRuleRingPresenter
    {
        /// <summary>圈带宽（米）：0.22，与旧版一致。</summary>
        internal const float RingWidth = 0.22f;
        internal const float FadeSeconds = 0.3f;
        /// <summary>掩体圈：本 Mod 主色青，压一点透明（细带，不是大面积平涂）。</summary>
        internal static readonly Color CoverColor = WithAlpha(BossRushUIColors.Accent, 0.85f);
        /// <summary>危险边界：警示暖金，落在原版暖琥珀画风的色相带里。</summary>
        internal static readonly Color HazardColor = WithAlpha(BossRushUIColors.WarningText, 0.9f);

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>开圈：挂展开 + 呼吸（只动带宽与透明度）。</summary>
        internal static void Show(LineRenderer ring, Color color)
        {
            if (ring == null) return;
            SkyIslandGroundRingPulse.Attach(ring, RingWidth, color);
        }

        /// <summary>收圈：没画出来（无材质 / 未激活 / 没挂呼吸）的直接销毁；否则停呼吸、淡出后销毁。</summary>
        internal static void FadeOutAndDestroy(GameObject root)
        {
            if (root == null) return;
            LineRenderer line = root.GetComponentInChildren<LineRenderer>();
            SkyIslandGroundRingPulse pulse = line != null ? line.GetComponent<SkyIslandGroundRingPulse>() : null;
            if (line == null || pulse == null || line.sharedMaterial == null || !root.activeInHierarchy)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            pulse.StartCoroutine(FadeOut(root, line));
            pulse.enabled = false;   // 停呼吸：颜色交给淡出协程（组件停用不影响已经起的协程）
        }

        private static System.Collections.IEnumerator FadeOut(GameObject root, LineRenderer line)
        {
            Color from = line.startColor;
            float width = line.widthMultiplier;
            float elapsed = 0f;
            while (root != null && line != null && elapsed < FadeSeconds)
            {
                // 表现层走 unscaled 时间（结算页会把 timeScale 置 0），暂停菜单开着时停推进
                if (!BossRushUI.IsGamePaused()) elapsed += Time.unscaledDeltaTime;
                float t = BossRushUI.SmoothStep(elapsed / FadeSeconds);
                Color color = from;
                color.a = from.a * (1f - t);
                line.startColor = color;
                line.endColor = color;
                line.widthMultiplier = width * (1f - 0.35f * t);
                yield return null;
            }
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}

// ============================================================================
// PetNestUI.cs - 遗种巢主面板（实施计划 步骤 10）
// ============================================================================
// 唯一一个会创建 canvas 的遗种巢界面文件（PetNestUIPages.cs / PetNestUINestPage.cs 只组装数据，
// PetNestUILayout.cs 只在既有 surface 内摆内容，都不碰 sortingOrder）。
//
// 共享 UI 库纪律（AGENTS.md 4.14）：
//   - sortingOrder 一律引用 BossRushUILayers 常量，禁裸数字；
//   - 颜色走 BossRushUIColors token，遮罩必须是 Backdrop；
//   - 底图走 BossRushUI.ApplyPanelSkin，字体走 ApplyGameFont / GetGameFont；
//   - Canvas 走 BossRushUI.CreateCanvasRoot（内部已调 ConfigureCanvasScaler）；
//   - 模态输入走 ZombieModeUIHelper.ClaimModalInput 的唯一 lease。
//
// 惰性构建：面板只在玩家第一次交互时装配，关闭即销毁 canvas，不常驻。
//
// 2026-09-23 审美审查（UA-10 / 14–18 / 21–23）：立绘、博物馆网格、分区标题、可悬停的卡片、真勾选框、
// ESC 关闭与关闭淡出、远征倒计时走表。
// 2026-09-24 交互重排（owner：「一股脑把所有功能都做成按钮丢出来」）：巢页改成「列表 + 详情」两栏，
// 其余页改成分区、按钮跟着它作用的那一行走；页签带待办数字；页眉加「说明」。两栏与分区的画法在 PetNestUILayout.cs。
// 本文件底部另有两个各窗口共用的静态小件：立绘框（CreateIconFrame）、关闭淡出（FadeOutAndDestroy）；
// ESC 组件 PetNestCancelKey 与异色流光 PetNestShinyTextShimmer 在 PetNestUIWidgets.cs。
// 放生 / 亡命出发的确认走共享 BossRushConfirmDialog（本文件「确认弹窗」一节组装内容、挂 Anchor），不再自绘。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>遗种巢主面板。四页共用一个 canvas，切页只重画内容区。</summary>
    internal sealed partial class PetNestUI : MonoBehaviour
    {
        #region 常量与状态

        private const string RootName = "BossRush_PetNestPanel";
        private static readonly Vector2 PanelSize = new Vector2(1180f, 760f);

        // 正文区（内容 + 动作条）的垂直预算。BodyTop 是面板局部坐标里正文顶边的 y。
        // 动作条按条数分配高度，两个区共享同一份预算。
        private const float BodyTop = 220f;
        private const float TotalBodyHeight = 572f;
        private const float ActionRowHeight = 56f;
        private const float ActionPadding = 16f;
        private const float ActionGap = 12f;
        private const float MinActionAreaHeight = 72f;
        private const float MaxActionAreaHeight = 200f;

        /// <summary>分页内容区里一行的宽度（滚动内容的内宽）。</summary>
        private const float ContentWidth = 1080f;

        // 博物馆网格（UA-17）：4 列 × 262 + 3 × 12 = 1084，正好是滚动内容的内宽。
        private const int GridColumns = 4;
        private static readonly Vector2 GridCellSize = new Vector2(262f, 190f);
        private const float GridSpacing = 12f;
        private const float GridPortraitSize = 96f;

        // 动作条（UA-23）：不超过 4 条时横排、右对齐，危险操作排最左并与其余隔开 24；超过 4 条退回纵向列表。
        private const int MaxInlineActions = 4;
        private const float InlineActionHeight = 46f;
        private const float InlineActionMinWidth = 200f;
        private const float InlineActionMaxWidth = 360f;
        private const float InlineActionSpacing = 12f;

        /// <summary>切页时卡片错峰入场：最多给前几张播，后面的已经在视口外。</summary>
        private const int MaxEntranceCards = 10;

        private static PetNestUI _instance;

        private Canvas _canvas;
        private Transform _contentRoot;
        private Transform _actionRoot;
        private GameObject _actionDivider;
        private readonly Dictionary<PetNestUIPage, Button> _tabs = new Dictionary<PetNestUIPage, Button>();
        private Button _helpButton;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private PetNestUIPage _page;
        private bool _showHelp;
        private string _selectedPetId;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<GameObject> _entranceTargets = new List<GameObject>();

        // 批量放生：勾选集合只活在面板里，关面板或切页即丢弃（owner 2026-09-22 要批量放生）。
        private bool _batchMode;
        private readonly HashSet<string> _batchSelection = new HashSet<string>();

        // 同一页重绘时保住滚动位置：点选中 / 勾选之后列表跳回顶部，看起来就是「闪一下」。
        private bool _hasRendered;
        private PetNestUIPage _lastRenderedPage;
        private bool _lastRenderedHelp;

        // 倒计时走表（UA-22）：只改在途行 / 详情底栏的字，一秒一次，不整页重建。
        private readonly List<TextMeshProUGUI> _liveBodies = new List<TextMeshProUGUI>();
        private readonly List<Func<string>> _liveBodySources = new List<Func<string>>();
        private float _nextLiveTick;

        #endregion

        #region 打开 / 关闭

        /// <summary>主面板是否开着（ESC 优先级判定用）。</summary>
        internal static bool IsOpen
        {
            get { return _instance != null; }
        }

        /// <summary>由运行时模块在 bootstrap 时把打开器注册进桥。</summary>
        internal static void RegisterOpener()
        {
            PetNestUIBridge.RegisterPageOpener(Open);
        }

        /// <summary>打开指定页。惰性构建：第一次调用才装配 canvas。</summary>
        internal static void Open(PetNestUIPage page)
        {
            try
            {
                if (_instance == null)
                {
                    GameObject host = new GameObject(RootName + "_Host");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    _instance = host.AddComponent<PetNestUI>();
                    _instance.Build();
                }
                _instance._page = page;
                _instance._showHelp = false;
                _instance.Refresh();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板打开失败: " + e.Message);
                Close();
            }
        }

        /// <summary>关闭并销毁面板。幂等。异常清理、切图、卸载走这里（立即销毁）。</summary>
        internal static void Close()
        {
            CloseInternal(false);
        }

        /// <summary>
        /// 关闭。<paramref name="animated"/> 只在玩家点「关闭」/ 按 ESC 时为 true：画布 0.12 秒淡出，
        /// 与打开时的淡入对称（UA-21）。输入租约与静态引用都在淡出**之前**放掉，淡出中的画布不再吃点击。
        /// </summary>
        private static void CloseInternal(bool animated)
        {
            try
            {
                if (_instance == null) return;
                PetNestUI closing = _instance;
                closing.ReleaseLease();
                if (closing._cancelKey != null) closing._cancelKey.Detach();
                if (closing.gameObject != null)
                {
                    if (animated) FadeOutAndDestroy(closing.gameObject, closing._canvas);
                    else UnityEngine.Object.Destroy(closing.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板关闭失败: " + e.Message);
            }
            finally
            {
                _instance = null;
                // 失败提示是进程级静态串，不清会把上一次（甚至上一个存档槽）的
                // 失败文案带到下次开面板时重新弹一遍。
                PetNestUIPages.ClearLastFailureText();
            }
        }

        private void ReleaseLease()
        {
            try
            {
                if (_modalLease != null)
                {
                    _modalLease.Release();
                    _modalLease = null;
                }
            }
            catch (Exception)
            {
                // 释放失败也要把引用丢掉，避免二次 Release
            }
        }

        private void OnDestroy()
        {
            // 子弹窗不能比宿主活得久，否则会留一个抢着 modal lease 的孤儿。
            // 放生 / 亡命出发的共享确认框挂了 Anchor = 本宿主，宿主一没它就按「取消」收场（见 ShowConfirm）。
            PetNestRenameModal.Close();
            ReleaseLease();
            if (_instance == this) _instance = null;
        }

        /// <summary>模块关停时注销打开器，与 RegisterOpener 成对。</summary>
        internal static void UnregisterOpener()
        {
            Close();
            PetNestUIBridge.UnregisterPageOpener();
        }

        #endregion

        #region 构建

        private void Build()
        {
            _canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.PetNestPanel, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), PanelSize);
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(surfaceImage, 14, BossRushUISkinPart.Panel);
            BossRushUI.ApplyPanelStroke(surfaceImage, 14, BossRushUISkinPart.Panel, BossRushUIColors.Stroke);

            BuildHeader(surface.transform);
            BuildTabs(surface.transform);

            // 分页内容区必须能滚动：远征的在途与派遣、孵化的蛋与遗魂账本、博物馆的血脉网格 + 碑文都远超一屏。
            // 早先按固定 y 预算铺元素会静默截断——第三个远征目的地、整段纪念碑都会在 UI 上凭空消失。
            _contentRoot = CreateScrollList(
                surface.transform, "Content", new Vector2(0f, 14f), new Vector2(1120f, 412f));
            _actionRoot = CreateScrollList(
                surface.transform, "Actions", new Vector2(0f, -288f), new Vector2(1120f, 128f));
            _actionDivider = ZombieModeUIHelper.CreateSeparator("ActionDivider", surface.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -208f), 1f, BossRushUIColors.Divider);

            // 巢页的两栏（PetNestUILayout.cs）：建好先藏着，巢页时才显示
            BuildNestBody(surface.transform);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestPanel");
            // ESC / 手柄取消 = 关闭（UA-21）；上面压着改名 / 放生 / 出发确认 / 演出时让给它们
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject,
                delegate { CloseAndPlayPendingReveal(); }, IsCoveredByChildWindow);
            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>主面板上面还压着自家的弹窗或演出：ESC 该给它们。</summary>
        private static bool IsCoveredByChildWindow()
        {
            return PetNestRenameModal.IsOpen || BossRushConfirmDialog.IsOpen
                || PetNestHatchRevealView.IsOpen || PetNestExpeditionRevealView.IsOpen;
        }

        private void BuildHeader(Transform parent)
        {
            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", parent,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "SystemName"),
                34f, new Vector2(-130f, 330f), new Vector2(860f, 52f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            title.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(title);

            // 「说明」：系统介绍、出战、扩建、保底、远征风险、放生，原来散在巢页 24 张卡的下面（交互重排 #5）
            _helpButton = ZombieModeUIHelper.CreateButton(
                "Help", parent, L10n.T("说明", "Guide"),
                new Vector2(0.5f, 0.5f), new Vector2(400f, 330f), new Vector2(110f, 44f),
                BossRushUIColors.SurfaceRaised, 20f, new Vector2(100f, 40f),
                delegate { _showHelp = !_showHelp; _batchMode = false; _batchSelection.Clear(); PetNestUIPages.ClearFailure(); Refresh(); }, true);
            BossRushUIKit.StyleSecondaryButton(_helpButton);

            Button close = ZombieModeUIHelper.CreateButton(
                "Close", parent, L10n.T("关闭", "Close"),
                new Vector2(0.5f, 0.5f), new Vector2(520f, 330f), new Vector2(110f, 44f),
                BossRushUIColors.SurfaceRaised, 20f, new Vector2(100f, 40f),
                delegate { CloseAndPlayPendingReveal(); }, true);
            BossRushUIKit.StyleSecondaryButton(close);
        }

        private void BuildTabs(Transform parent)
        {
            PetNestUIPage[] pages =
            {
                PetNestUIPage.Nest, PetNestUIPage.Hatch,
                PetNestUIPage.Expedition, PetNestUIPage.Museum,
            };

            for (int i = 0; i < pages.Length; i++)
            {
                PetNestUIPage page = pages[i];
                Button tab = ZombieModeUIHelper.CreateButton(
                    "Tab_" + page, parent, TabLabel(page),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-420f + i * 200f, 268f), new Vector2(190f, 44f),
                    BossRushUIColors.SurfaceRaised, 20f, new Vector2(180f, 40f),
                    delegate { SwitchPage(page); }, true);
                // 页签描一圈边：未选中的深底页签对面板底只有约 1.03:1，不描边就是几行浮着的字
                BossRushUIKit.StyleSecondaryButton(tab);
                _tabs[page] = tab;
            }
        }

        private static string TabLabel(PetNestUIPage page)
        {
            string key;
            switch (page)
            {
                case PetNestUIPage.Hatch: key = "Page_Hatch"; break;
                case PetNestUIPage.Expedition: key = "Page_Expedition"; break;
                case PetNestUIPage.Museum: key = "Page_Museum"; break;
                default: key = "Page_Nest"; break;
            }
            return LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + key);
        }

        private void SwitchPage(PetNestUIPage page)
        {
            _page = page;
            _showHelp = false;
            _batchMode = false;
            _batchSelection.Clear();
            PetNestUIPages.ClearFailure();
            Refresh();
        }

        #endregion

        #region 刷新

        private void Refresh()
        {
            try
            {
                bool samePage = _hasRendered && _lastRenderedPage == _page && _lastRenderedHelp == _showHelp;
                PruneBatchSelection();
                PetNestPageContent content = BuildPageContent();
                if (content == null) return;

                float[] scroll = samePage ? CaptureScroll() : null;
                ClearSpawned();
                RefreshHeader();

                bool nest = content.Nest != null;
                SetNestBodyVisible(nest);
                _contentRoot.parent.gameObject.SetActive(!nest);
                if (nest)
                {
                    _actionRoot.parent.gameObject.SetActive(false);
                    _actionDivider.SetActive(false);
                    RenderNest(content.Nest);
                }
                else
                {
                    RenderPage(content);
                }

                Canvas.ForceUpdateCanvases();
                RestoreScroll(scroll);
                // 切页才错峰入场；同页重绘（点选中 / 勾选）不重播，否则每点一下整页都在动
                if (scroll == null) PlayCardEntrance();
                _hasRendered = true;
                _lastRenderedPage = _page;
                _lastRenderedHelp = _showHelp;
                _nextLiveTick = Time.unscaledTime + 1f;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板刷新失败: " + e.Message);
            }
        }

        /// <summary>页签选中态 + 待办数字，「说明」按钮的字。</summary>
        private void RefreshHeader()
        {
            foreach (KeyValuePair<PetNestUIPage, Button> pair in _tabs)
            {
                bool selected = pair.Key == _page && !_showHelp;
                // 选中页签用 AccentFill（Accent 压深降饱和、走白字）：Accent 只用于描边与小字强调，
                // 不再整块平涂（2026-09-23 全 Mod 口径）
                Color color = selected ? BossRushUIColors.AccentFill : BossRushUIColors.SurfaceRaised;
                // 底色与标签色一起走共享入口；直接写 Image.color 会和 ColorTint 相乘，
                // 选中的页签反而比未选中的更暗。
                ZombieModeUIHelper.SetButtonBaseColor(pair.Value, color);
                TextMeshProUGUI label = pair.Value.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    // 页签徽标（iOS 标签栏口径）：「孵化 ·2」= 有两件事能做，没事就只写名字
                    label.text = TabLabel(pair.Key) + (PetNestUIPages.DescribeTabBadge(pair.Key) ?? string.Empty);
                    label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                }
            }
            if (_helpButton != null)
            {
                TextMeshProUGUI help = _helpButton.GetComponentInChildren<TextMeshProUGUI>();
                if (help != null) help.text = _showHelp ? L10n.T("返回", "Back") : L10n.T("说明", "Guide");
            }
        }

        /// <summary>分页（孵化 / 远征 / 博物馆 / 说明）：内容区 + 底部动作条。</summary>
        private void RenderPage(PetNestPageContent content)
        {
            // 失败提示排在最前：不给反馈的话，巢满 / 写屏障 / 远征锁定这些失败
            // 在界面上与"点歪了"完全无法区分
            if (!string.IsNullOrEmpty(PetNestUIPages.LastFailureText))
            {
                SpawnLine(_contentRoot, PetNestUIPages.LastFailureText, 18f, BossRushUIColors.DangerText, ContentWidth);
            }
            if (!string.IsNullOrEmpty(content.Notice))
            {
                SpawnLine(_contentRoot, content.Notice, 18f, BossRushUIColors.WarningText, ContentWidth);
            }
            if (!string.IsNullOrEmpty(content.Body))
            {
                SpawnLine(_contentRoot, content.Body, 18f, BossRushUIColors.TextPrimary, ContentWidth);
            }
            if (content.CardsAsGrid) SpawnGrid(content.Cards);
            for (int i = 0; i < content.Sections.Count; i++)
            {
                SpawnSection(_contentRoot, content.Sections[i], ContentWidth);
            }

            bool inlineActions = SpawnActions(content.Actions);

            // 底部动作条按**实际条数**定高，剩下的全部还给内容区
            // （owner 2026-09-20：「可选择的地方太小了而且中间有很多留白」）。横排时固定一行。
            RectTransform viewport = _contentRoot.parent.GetComponent<RectTransform>();
            RectTransform actionViewport = _actionRoot.parent.GetComponent<RectTransform>();
            bool hasActions = content.Actions.Count > 0;

            float actionHeight = !hasActions
                ? 0f
                : (inlineActions
                    ? MinActionAreaHeight
                    : Mathf.Clamp(content.Actions.Count * ActionRowHeight + ActionPadding,
                        MinActionAreaHeight, MaxActionAreaHeight));
            float contentHeight = TotalBodyHeight - (hasActions ? actionHeight + ActionGap : 0f);

            viewport.sizeDelta = new Vector2(1120f, contentHeight);
            viewport.anchoredPosition = new Vector2(0f, BodyTop - contentHeight * 0.5f);

            if (hasActions)
            {
                actionViewport.sizeDelta = new Vector2(1120f, actionHeight);
                actionViewport.anchoredPosition = new Vector2(
                    0f, BodyTop - contentHeight - ActionGap - actionHeight * 0.5f);
                RectTransform dividerRect = _actionDivider.GetComponent<RectTransform>();
                dividerRect.anchoredPosition = new Vector2(0f, BodyTop - contentHeight - ActionGap * 0.5f);
            }
            _actionDivider.SetActive(hasActions);
            _actionRoot.parent.gameObject.SetActive(hasActions);
            _actionRoot.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// 倒计时走表（UA-22）：一秒一次，只改在途行与详情底栏的字；有一条到点就整页刷新一次去结算。
        /// 没有走表的字时第一行就返回。模态租约把 timeScale 压到 0，所以用 unscaled 时间。
        /// </summary>
        private void Update()
        {
            if (_liveBodies.Count == 0) return;
            float now = Time.unscaledTime;
            if (now < _nextLiveTick || BossRushUI.IsGamePaused()) return;
            _nextLiveTick = now + 1f;
            for (int i = 0; i < _liveBodies.Count; i++)
            {
                string text;
                try
                {
                    text = _liveBodySources[i]();
                }
                catch (Exception)
                {
                    continue;
                }
                if (text == null)
                {
                    Refresh();
                    return;
                }
                TextMeshProUGUI body = _liveBodies[i];
                if (body != null && !string.Equals(body.text, text, StringComparison.Ordinal))
                {
                    body.text = text;
                }
            }
        }

        /// <summary>切页后卡片错峰淡入升起（每张 0.045 秒）。布局已经算完，入场组件取到的是最终位置。</summary>
        private void PlayCardEntrance()
        {
            int count = Mathf.Min(_entranceTargets.Count, MaxEntranceCards);
            for (int i = 0; i < count; i++)
            {
                if (_entranceTargets[i] != null)
                {
                    BossRushUIEntranceAnimation.Play(_entranceTargets[i], i * 0.045f, 0.22f, 10f);
                }
            }
        }

        /// <summary>
        /// 建一个纵向滚动列表：ScrollRect + RectMask2D + VerticalLayoutGroup +
        /// ContentSizeFitter。内容超出可视区时可滚动，绝不静默截断。
        /// </summary>
        private static Transform CreateScrollList(
            Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject viewport = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            viewport.GetComponent<RectTransform>().anchoredPosition = position;
            viewport.AddComponent<RectMask2D>();

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            GameObject content = ZombieModeUIHelper.CreateRect(
                name + "_Content", viewport.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(-10f, 0f), new Vector2(-20f, 0f), new Vector2(0.5f, 1f));

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = content.GetComponent<RectTransform>();
            BossRushUI.ConfigureScrollRect(scroll);
            return content.transform;
        }

        private PetNestPageContent BuildPageContent()
        {
            if (_showHelp) return PetNestUIPages.BuildHelpPage();
            switch (_page)
            {
                case PetNestUIPage.Hatch:
                    return PetNestUIPages.BuildHatchPage(Refresh, OnHatched);
                case PetNestUIPage.Expedition:
                    return PetNestUIPages.BuildExpeditionPage(Refresh, ResolveDeparturePetId(), SelectPet);
                case PetNestUIPage.Museum:
                    return PetNestUIPages.BuildMuseumPage();
                default:
                    PetNestNestPageContext context = new PetNestNestPageContext();
                    context.SelectedPetId = ResolveSelectedPetId();
                    context.BatchMode = _batchMode;
                    context.BatchSelection = _batchSelection;
                    context.Refresh = Refresh;
                    context.Select = SelectPet;
                    context.Rename = OpenRename;
                    context.Release = OpenRelease;
                    context.SetBatchMode = SetBatchMode;
                    context.ToggleBatch = ToggleBatch;
                    context.SendOnExpedition = SendOnExpedition;
                    return PetNestUIPages.BuildNestPage(context);
            }
        }

        /// <summary>进 / 出批量放生模式。进出都清空勾选，避免上一次的勾选残留。</summary>
        private void SetBatchMode(bool enabled)
        {
            _batchMode = enabled;
            _batchSelection.Clear();
            PetNestUIPages.ClearFailure();
            Refresh();
        }

        private void ToggleBatch(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return;
            if (!_batchSelection.Remove(petId)) _batchSelection.Add(petId);
        }

        /// <summary>放生、远征之后已经不在巢里 / 远征中的崽从勾选里剔掉。</summary>
        private void PruneBatchSelection()
        {
            if (_batchSelection.Count == 0) return;
            List<string> stale = null;
            foreach (string id in _batchSelection)
            {
                PetNestPetRecord pet = PetNestService.TryGetPet(id);
                if (pet != null && pet.state != (int)PetNestPetState.OnExpedition) continue;
                if (stale == null) stale = new List<string>();
                stale.Add(id);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) _batchSelection.Remove(stale[i]);
        }

        /// <summary>选中一只崽（巢页详情、远征页的出发人选共用）。</summary>
        private void SelectPet(string petId)
        {
            _selectedPetId = petId;
        }

        /// <summary>巢页详情的「派去远征」：切到远征页，这只崽已经选好。</summary>
        private void SendOnExpedition(string petId)
        {
            _selectedPetId = petId;
            SwitchPage(PetNestUIPage.Expedition);
        }

        /// <summary>打开命名弹窗。关闭后刷新面板，让新名字立刻可见。</summary>
        private void OpenRename(string petId)
        {
            PetNestRenameModal.Open(petId, Refresh);
        }

        /// <summary>打开放生确认弹窗（单只或批量）。关闭后刷新面板，让列表与遗魂账本立刻同步。</summary>
        private void OpenRelease(IList<string> petIds)
        {
            ConfirmRelease(petIds, Refresh);
        }

        /// <summary>
        /// 玩家点关闭：顺手把面板内结算出来的远征结果翻掉。
        ///
        /// 远征页打开时就会 SettleDueExpeditions，但翻牌此前只挂在「回基地」场景回调上，
        /// 卡片会一直停在「已结算，等待翻牌」直到玩家出图再回来。
        /// 放在关闭时而不是打开时：翻牌是全屏演出，会被面板遮罩盖住。
        /// 只挂用户点击这一条路径，不挂静态 Close()——dormant / 异常清理时不该弹演出。
        /// </summary>
        private void CloseAndPlayPendingReveal()
        {
            CloseInternal(true);
            try
            {
                if (LevelManager.Instance == null || !LevelManager.Instance.IsBaseLevel) return;
                // PlayPending 自身幂等：没有待翻记录时 O(1) 返回
                PetNestExpeditionRevealView.PlayPending();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 关闭面板后翻牌失败: " + e.Message);
            }
        }

        /// <summary>
        /// 当前选中的崽。选中的崽已经不在（刚被放生 / 远征阵亡）时回退到出战崽或第一只在巢的崽，
        /// 并清掉旧 id（2026-09-23 复核第 11 项：旧写法留着失效的 id，结果没有高亮、放生按钮变灰）。
        /// </summary>
        private string ResolveSelectedPetId()
        {
            if (!string.IsNullOrEmpty(_selectedPetId))
            {
                if (PetNestService.TryGetPet(_selectedPetId) != null) return _selectedPetId;
                _selectedPetId = null;
            }
            PetNestPetRecord deployed = PetNestService.DeployedPet;
            if (deployed != null) return deployed.id;
            List<PetNestPetRecord> pets = PetNestService.Pets;
            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i] != null && pets[i].state == (int)PetNestPetState.InNest)
                {
                    return pets[i].id;
                }
            }
            return pets.Count > 0 && pets[0] != null ? pets[0].id : null;
        }

        /// <summary>
        /// 远征页的出发人选：选中的崽能出发就用它，否则出战崽，否则第一只能出发的。
        /// 不改 _selectedPetId——巢页里选着一只远征中的崽，远征页照样有人可派。
        /// </summary>
        private string ResolveDeparturePetId()
        {
            string reason;
            PetNestPetRecord selected = PetNestService.TryGetPet(ResolveSelectedPetId());
            if (selected != null && PetNestExpeditionService.CanDepart(selected, out reason)) return selected.id;
            PetNestPetRecord deployed = PetNestService.DeployedPet;
            if (deployed != null && PetNestExpeditionService.CanDepart(deployed, out reason)) return deployed.id;
            List<PetNestPetRecord> pets = PetNestService.Pets;
            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i] != null && PetNestExpeditionService.CanDepart(pets[i], out reason)) return pets[i].id;
            }
            return null;
        }

        /// <summary>
        /// 孵化成功回调。结果已经 commit，这里只交给演出层回放。
        /// </summary>
        private void OnHatched(PetNestHatchResult result)
        {
            PetNestHatchRevealView.Play(result);
        }

        #endregion

        #region 确认弹窗（放生 / 亡命出发）

        /// <summary>
        /// 放生确认（单只或批量）。2026-09-24 从自绘的放生确认框迁到共享 BossRushConfirmDialog（AGENTS §4.14），行为照旧：
        ///   - petIds 里查不到的崽略过、去重，一只都查不到不弹空窗；
        ///   - 名单用装饰名、最多点名 6 只（文案在 PetNestUINestPage.cs），单只异色崽的名字挂流光；稀有 / 出战单独一行黄字，
        ///     「不可逆 + 不进纪念碑」黄字、返还遗魂绿字（审美审查 UA-20 的配色，用 token 转成的 hex）；
        ///   - 确认只调服务层 TryReleasePets（单只也走它），失败原因经 NoteExternalFailure 回抛给面板；确认或取消都刷新面板。
        /// </summary>
        internal static void ConfirmRelease(IList<string> petIds, Action refresh)
        {
            List<PetNestPetRecord> pets = new List<PetNestPetRecord>();
            if (petIds != null)
            {
                for (int i = 0; i < petIds.Count; i++)
                {
                    PetNestPetRecord pet = PetNestService.TryGetPet(petIds[i]);
                    if (pet != null && !pets.Contains(pet)) pets.Add(pet);
                }
            }
            if (pets.Count == 0) return;

            List<string> ids = new List<string>();
            for (int i = 0; i < pets.Count; i++) ids.Add(pets[i].id);
            int refund = PetNestTuning.ReleaseSoulRefund * pets.Count;
            BossRushConfirmDialog.Options options = new BossRushConfirmDialog.Options
            {
                Title = LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Title"),
                Target = PetNestUIPages.DescribeReleaseTargets(pets),
                Body = Colorize(PetNestUIPages.DescribeReleaseRareTargets(pets), BossRushUIColors.WarningText),
                Warning = Colorize(LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Warn"),
                        BossRushUIColors.WarningText)
                    + "\n" + Colorize(L10n.T("返还遗魂 ", "Souls returned ") + "+" + refund
                        + (pets.Count > 1 ? "（" + PetNestTuning.ReleaseSoulRefund + " × " + pets.Count + "）" : string.Empty),
                        BossRushUIColors.SuccessText),
                ConfirmLabel = LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Confirm"),
                CancelLabel = LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Cancel"),
                Danger = true,
                OnConfirm = delegate
                {
                    string reason = null;
                    bool ok;
                    try
                    {
                        ok = PetNestService.TryReleasePets(ids, out reason);
                    }
                    catch (Exception e)
                    {
                        ok = false;
                        reason = "release_failed:" + e.GetType().Name;
                        ModBehaviour.DevLog("[PetNest] 放生失败: " + e.Message);
                    }
                    PetNestUIPages.NoteExternalFailure(ok, reason);
                },
            };
            if (pets.Count == 1 && pets[0].shiny) options.DecorateTarget = PetNestShinyTextShimmer.Attach;
            ShowConfirm(options, refresh);
        }

        /// <summary>
        /// 亡命档出发确认。2026-09-24 从自绘的出发确认框迁到共享 BossRushConfirmDialog，行为照旧：
        ///   - 崽查不到不弹空窗；写清谁、去哪、出发时固化的死亡率（红、20 号）、「回不来就只剩纪念碑」（黄）；
        ///   - 确认只经 PetNestUIPages.TryDepartAndNote 调服务层 TryDepart，异常记 depart_failed，失败原因回抛给面板；
        ///     确认或取消都刷新面板。
        /// </summary>
        internal static void ConfirmDepart(string petId, string destinationId, PetNestRiskTier tier, Action refresh)
        {
            PetNestPetRecord pet = PetNestService.TryGetPet(petId);
            if (pet == null) return;
            BossRushConfirmDialog.Options options = new BossRushConfirmDialog.Options
            {
                Title = L10n.T("确认亡命出发？", "Send on a desperate run?"),
                Target = PetNestService.GetDecoratedPetName(pet) + L10n.T(" → ", " -> ")
                    + PetNestLocalization.DescribeDestination(destinationId),
                // 后果块底色是 DangerText（Danger 确认）：死亡率一行沿用它，真死说明单独改回黄字
                Warning = "<size=20>" + LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "DeathRateLabel") + " "
                        + PetNestLocalization.FormatPercent(PetNestExpeditionService.GetDeathRate(tier)) + "</size>\n"
                    + Colorize(L10n.T("亡命档是真死：回不来，就只剩纪念碑上的名字。出发后不能召回。",
                        "Desperate runs kill for real: if it doesn't come back, only its name remains on the memorial. It can't be recalled."),
                        BossRushUIColors.WarningText),
                ConfirmLabel = L10n.T("出发", "Depart"),
                CancelLabel = L10n.T("再想想", "Not now"),
                Danger = true,
                OnConfirm = delegate
                {
                    try
                    {
                        PetNestUIPages.TryDepartAndNote(petId, destinationId, tier);
                    }
                    catch (Exception e)
                    {
                        PetNestUIPages.NoteExternalFailure(false, "depart_failed");
                        ModBehaviour.DevLog("[PetNest] 亡命出发失败: " + e.Message);
                    }
                },
            };
            if (pet.shiny) options.DecorateTarget = PetNestShinyTextShimmer.Attach;
            ShowConfirm(options, refresh);
        }

        /// <summary>
        /// 遗种巢的确认一律走共享 BossRushConfirmDialog。层级用它默认的 ModalConfirm（3200），压在主面板
        /// PetNestPanel（2100）与演出层 PetNestModal（3150）之上；模态租约与 ESC = 取消由共享框自己管。
        /// Anchor 指向主面板宿主：面板被关掉 / 销毁（切图、dormant、卸载）时弹窗按「取消」收场，不留抢着租约的孤儿。
        /// 确认或取消后刷新面板（原弹窗的 onClosed）；面板已经不在时不刷新。
        /// </summary>
        private static void ShowConfirm(BossRushConfirmDialog.Options options, Action refresh)
        {
            Action confirm = options.OnConfirm;
            Action refreshIfOpen = delegate
            {
                if (refresh != null && _instance != null) refresh();
            };
            options.OnConfirm = delegate
            {
                try
                {
                    if (confirm != null) confirm();
                }
                finally
                {
                    refreshIfOpen();
                }
            };
            options.OnCancel = refreshIfOpen;
            options.Anchor = _instance != null ? _instance.gameObject : null;
            BossRushConfirmDialog.Show(options);
        }

        /// <summary>给一段字包上 token 颜色（§4.14：富文本颜色用 token 转成的 hex）；空串原样返回（弹窗不画这一段）。</summary>
        private static string Colorize(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }

        #endregion

        #region 元素

        private void ClearSpawned()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    _spawned[i].SetActive(false);
                    UnityEngine.Object.Destroy(_spawned[i]);
                }
            }
            _spawned.Clear();
            _entranceTargets.Clear();
            _liveBodies.Clear();
            _liveBodySources.Clear();
        }

        /// <summary>给布局组里的元素设固定高度（VerticalLayoutGroup 按它排布）。</summary>
        private static void SetLayoutHeight(GameObject go, float height)
        {
            LayoutElement element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            go.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        /// <summary>
        /// 正文行。字号梯度（UA-15）：页摘要 / 失败 / 警示 18，区说明 16 次色。按实测高度撑开，不截断。
        /// </summary>
        private TextMeshProUGUI SpawnLine(Transform parent, string text, float fontSize, Color color, float width)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "Line", parent, text, fontSize,
                Vector2.zero, new Vector2(width, 34f),
                TextAlignmentOptions.Left, color);
            BossRushUI.ApplyGameFont(label);
            SetLayoutHeight(label.gameObject, BossRushUI.MeasureTextHeight(label, width, 30f));
            _spawned.Add(label.gameObject);
            return label;
        }

        /// <summary>
        /// 分区标题：20 号粗体主色，下接一条分隔线（UA-15）。
        /// 单行框高 34 ≥ 20×1.45+4，关了自动缩字也不会被 Ellipsis 整行清空。
        /// </summary>
        private void SpawnSectionHeader(Transform parent, string text, float width)
        {
            GameObject section = ZombieModeUIHelper.CreateRect(
                "Section", parent, new Vector2(0.5f, 0.5f), new Vector2(width, 50f));
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "SectionTitle", section.transform, text, 20f,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -25f), new Vector2(0f, 34f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = false;
            label.fontSize = 20f;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            BossRushUI.ApplyGameFont(label);
            ZombieModeUIHelper.CreateSeparator("SectionRule", section.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 4f), 2f, BossRushUIColors.Divider);
            SetLayoutHeight(section, 50f);
            _spawned.Add(section);
        }

        /// <summary>
        /// 博物馆血脉图鉴网格（UA-17）：4 列，一格 = 立绘 + 名字 + 两行数字。
        /// 旧写法几十条血脉各占一张 1080×140 的长卡，正文只有一行，下面大半张是空的。
        /// </summary>
        private void SpawnGrid(List<PetNestCardData> cards)
        {
            if (cards == null || cards.Count == 0) return;
            int rows = (cards.Count + GridColumns - 1) / GridColumns;
            GameObject grid = ZombieModeUIHelper.CreateRect(
                "Grid", _contentRoot, new Vector2(0.5f, 0.5f),
                new Vector2(GridColumns * GridCellSize.x + (GridColumns - 1) * GridSpacing, GridCellSize.y));
            GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = GridCellSize;
            layout.spacing = new Vector2(GridSpacing, GridSpacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = GridColumns;
            layout.childAlignment = TextAnchor.UpperLeft;
            SetLayoutHeight(grid, rows * GridCellSize.y + Mathf.Max(0, rows - 1) * GridSpacing);
            _spawned.Add(grid);

            for (int i = 0; i < cards.Count; i++)
            {
                GameObject cell = SpawnGridCell(grid.transform, cards[i]);
                if (cell != null) _entranceTargets.Add(cell);
            }
        }

        private static GameObject SpawnGridCell(Transform parent, PetNestCardData data)
        {
            if (data == null) return null;
            bool locked = data.IconLocked;

            GameObject cell = ZombieModeUIHelper.CreateRect("Cell", parent, new Vector2(0.5f, 0.5f), GridCellSize);
            Image background = cell.AddComponent<Image>();
            background.color = locked ? BossRushUIColors.Surface : BossRushUIColors.SurfaceRaised;
            background.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(background, 10, BossRushUISkinPart.Card);
            // 孵出过异色的血脉描一圈金边；不挂流光（标题是白字血脉名，复核第 12 项）
            BossRushUI.ApplyPanelStroke(background, 10, BossRushUISkinPart.Card,
                data.Shiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Stroke);

            float top = 14f;
            if (data.Icon != null)
            {
                CreateIconFrame(cell.transform, "Portrait", data.Icon,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f),
                    GridPortraitSize, data.Shiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Stroke,
                    locked);
                top = 10f + GridPortraitSize + 6f;
            }

            TextMeshProUGUI name = ZombieModeUIHelper.CreateText(
                "Name", cell.transform, data.Title, 18f,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(top + 15f)), new Vector2(-16f, 30f),
                TextAlignmentOptions.Center, locked ? BossRushUIColors.TextSecondary : BossRushUIColors.TextPrimary);
            name.fontStyle = locked ? FontStyles.Normal : FontStyles.Bold;
            name.fontSizeMin = 14f;
            BossRushUI.ApplyGameFont(name);

            TextMeshProUGUI first = ZombieModeUIHelper.CreateText(
                "Stats", cell.transform, data.Subtitle ?? string.Empty, 15f,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(top + 42f)), new Vector2(-16f, 26f),
                TextAlignmentOptions.Center, locked ? BossRushUIColors.TextSecondary : BossRushUIColors.Accent);
            BossRushUI.ApplyGameFont(first);

            if (!string.IsNullOrEmpty(data.Body))
            {
                TextMeshProUGUI second = ZombieModeUIHelper.CreateText(
                    "Stats2", cell.transform, data.Body, 14f,
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -(top + 64f)), new Vector2(-16f, 24f),
                    TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(second);
            }
            return cell;
        }

        /// <summary>
        /// 底部动作按钮。**不截断**：动作区是滚动列表，多出来的往下排。
        /// 不超过 4 条时横排右对齐（UA-23），危险操作排最左、与其余隔开 24；返回是否横排，供动作区定高。
        /// </summary>
        private bool SpawnActions(List<PetNestActionData> actions)
        {
            if (actions == null || actions.Count == 0) return false;

            if (actions.Count > MaxInlineActions)
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    PetNestActionData action = actions[i];
                    if (action == null) continue;
                    Button button = CreateActionButton(_actionRoot, "Action_" + i, action, new Vector2(1060f, 46f));
                    SetLayoutHeight(button.gameObject, 46f);
                    _spawned.Add(button.gameObject);
                }
                return false;
            }

            List<PetNestActionData> ordered = new List<PetNestActionData>(actions.Count);
            int dangerCount = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && actions[i].IsDanger && !actions[i].IsPrimary) { ordered.Add(actions[i]); dangerCount++; }
            }
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && (!actions[i].IsDanger || actions[i].IsPrimary)) ordered.Add(actions[i]);
            }
            if (ordered.Count == 0) return false;

            GameObject row = ZombieModeUIHelper.CreateRect(
                "ActionRow", _actionRoot, new Vector2(0.5f, 0.5f), new Vector2(ContentWidth, InlineActionHeight));
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = InlineActionSpacing;
            layout.padding = new RectOffset(0, 8, 0, 0);
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            SetLayoutHeight(row, InlineActionHeight);
            _spawned.Add(row);

            bool gap = dangerCount > 0 && dangerCount < ordered.Count;
            float available = ContentWidth - 8f - InlineActionSpacing * (ordered.Count - 1) - (gap ? InlineActionSpacing * 2f : 0f);
            float width = Mathf.Clamp(available / ordered.Count, InlineActionMinWidth, InlineActionMaxWidth);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (gap && i == dangerCount)
                {
                    // 零宽占位：两侧各算一次间距，危险操作与其余按钮拉开 24
                    ZombieModeUIHelper.CreateRect("DangerGap", row.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, InlineActionHeight));
                }
                CreateActionButton(row.transform, "Action_" + i, ordered[i], new Vector2(width, InlineActionHeight));
            }
            return true;
        }

        /// <summary>
        /// 动作按钮配色（§4.14 按钮口径）：主操作 AccentFill 实心（每屏最多一个）；主操作又是危险的（批量放生、亡命出发，
        /// 后面还有一道确认）Danger 实心；危险但不是主操作的（详情里的「放生」）DangerText 描边的次级样式；其余一律次级。
        /// </summary>
        private static Button CreateActionButton(Transform parent, string name, PetNestActionData action, Vector2 size)
        {
            bool enabled = action.Interactable && action.OnClick != null;
            Color fill = action.IsPrimary
                ? (action.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.AccentFill)
                : BossRushUIColors.SurfaceRaised;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, action.Label,
                new Vector2(0.5f, 0.5f), Vector2.zero, size,
                fill, 18f, new Vector2(size.x - 20f, size.y - 4f),
                action.OnClick != null ? new UnityEngine.Events.UnityAction(action.OnClick) : null,
                enabled);
            if (action.IsPrimary) return button;
            if (!action.IsDanger)
            {
                BossRushUIKit.StyleSecondaryButton(button);
                return button;
            }
            // 危险的次级按钮：红描边 + 红字（Apple 的 destructive 口径），深底不变，不做实心红块
            AddButtonStroke(button, BossRushUIColors.DangerText);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = BossRushUIColors.DangerText;
            return button;
        }

        /// <summary>按钮描边（次级按钮的彩色轮廓）。共享按钮的底图是 8px 按钮档。</summary>
        private static void AddButtonStroke(Button button, Color color)
        {
            Image image = button != null ? button.targetGraphic as Image : null;
            if (image == null || image.transform.Find("Stroke") != null) return;
            BossRushUI.ApplyPanelStroke(image, 8, BossRushUISkinPart.Button, color);
        }

        #endregion

        #region 共用小件（揭晓演出、弹窗、随从 HUD 也用）

        /// <summary>
        /// 立绘 / 图标框：Card 档深底 + 描边 + 投影，里面一层 RectMask2D 裁切的 Image（preserveAspect）。
        /// sprite 为 null 时什么都不建、返回 null——取不到图就不画那一格（UA-10），不退回汉字或灰方块。
        /// 裁切放在内层：放在外框上会把外框自己的投影也裁掉。
        /// <paramref name="silhouette"/> 为真时同一张图压成剪影（口径同图鉴 CodexTuning.LockedPortraitTint）。
        /// </summary>
        internal static Image CreateIconFrame(Transform parent, string name, Sprite sprite,
            Vector2 anchor, Vector2 pivot, Vector2 position, float size, Color stroke, bool silhouette)
        {
            if (parent == null || sprite == null) return null;
            GameObject frame = ZombieModeUIHelper.CreateRect(
                name, parent, anchor, anchor, position, new Vector2(size, size), pivot);
            Image frameImage = frame.AddComponent<Image>();
            frameImage.color = BossRushUIColors.Surface;
            frameImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(frameImage, 8, BossRushUISkinPart.Card);
            BossRushUI.ApplyPanelStroke(frameImage, 8, BossRushUISkinPart.Card, stroke);

            GameObject clip = ZombieModeUIHelper.CreateRect(
                name + "_Clip", frame.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-6f, -6f), new Vector2(0.5f, 0.5f));
            clip.AddComponent<RectMask2D>();

            GameObject picture = ZombieModeUIHelper.CreateRect(
                name + "_Image", clip.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image image = picture.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = silhouette ? CodexTuning.LockedPortraitTint : Color.white;
            return image;
        }

        /// <summary>
        /// 玩家主动关闭时的收场：画布从宿主上摘下来淡出 0.12 秒再销毁，宿主（挂脚本的空物体）立刻销毁。
        /// 调用前必须已经释放输入租约、清掉静态引用（BossRushUIKit.PlayCloseAndDestroy 的约定）。
        /// 摘下来的画布仍在 DontDestroyOnLoad 场景里（SetParent 不换场景）。
        /// </summary>
        internal static void FadeOutAndDestroy(GameObject host, Canvas canvas)
        {
            try
            {
                if (canvas != null && host != null && canvas.transform.IsChildOf(host.transform))
                {
                    canvas.transform.SetParent(null, false);
                    BossRushUIKit.PlayCloseAndDestroy(canvas.gameObject);
                }
            }
            catch (Exception)
            {
                // 淡出只是表现：失败就直接销毁
                if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            }
            if (host != null) UnityEngine.Object.Destroy(host);
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Close();
        }

        #endregion
    }
}

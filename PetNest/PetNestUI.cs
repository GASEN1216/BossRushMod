// ============================================================================
// PetNestUI.cs - 遗种巢主面板（实施计划 步骤 10）
// ============================================================================
// 唯一一个会创建 canvas 的遗种巢界面文件（PetNestUIPages.cs 只在既有 surface 内
// 摆内容，不碰 sortingOrder）。
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
// 2026-09-23 审美审查（UA-10 / 14–18 / 21–23）：卡片左侧画 Boss 立绘、博物馆改网格、
// 分区标题独立样式、卡片可悬停 / 按下、真勾选框、动作条横排、ESC 关闭与关闭淡出、远征倒计时走表。
// 本文件底部另有两个各窗口共用的静态小件：立绘框（CreateIconFrame）、关闭淡出（FadeOutAndDestroy）；
// ESC 组件 PetNestCancelKey 与异色流光 PetNestShinyTextShimmer 在 PetNestUIWidgets.cs。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>遗种巢主面板。四页共用一个 canvas，切页只重画内容区。</summary>
    internal sealed class PetNestUI : MonoBehaviour
    {
        #region 常量与状态

        private const string RootName = "BossRush_PetNestPanel";
        private static readonly Vector2 PanelSize = new Vector2(1180f, 760f);
        private static readonly Vector2 CardSize = new Vector2(1080f, 140f);

        // 正文区（内容 + 动作条）的垂直预算。BodyTop 是面板局部坐标里正文顶边的 y。
        // 旧写法把「有动作 = 412 / 无动作 = 572」两个数字直接写在 Refresh 里，
        // 动作条条数一多就装不下；现在按条数分配，两个区共享同一份预算。
        private const float BodyTop = 220f;
        private const float TotalBodyHeight = 572f;
        private const float ActionRowHeight = 56f;
        private const float ActionPadding = 16f;
        private const float ActionGap = 12f;
        private const float MinActionAreaHeight = 72f;
        private const float MaxActionAreaHeight = 200f;

        // 卡片左侧图框（UA-10）：104 见方、距左 22（让开最多三条身份色条）；有图时文字整体右移 IconColumn。
        private const float IconSize = 104f;
        private const float IconLeft = 22f;
        private const float IconColumn = 112f;

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
        private readonly Dictionary<PetNestUIPage, Button> _tabs = new Dictionary<PetNestUIPage, Button>();
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private PetNestUIPage _page;
        private string _selectedPetId;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<GameObject> _entranceTargets = new List<GameObject>();

        // 批量放生：勾选集合只活在面板里，关面板或切页即丢弃（owner 2026-09-22 要批量放生）。
        private bool _batchMode;
        private readonly HashSet<string> _batchSelection = new HashSet<string>();

        // 同一页重绘时保住滚动位置：点出战 / 勾选之后列表跳回顶部，看起来就是「闪一下」。
        private bool _hasRendered;
        private PetNestUIPage _lastRenderedPage;

        // 远征页倒计时走表（UA-22）：只改在途卡片的正文，一秒一次，不整页重建。
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
            // 子弹窗不能比宿主活得久，否则会留一个抢着 modal lease 的孤儿
            PetNestRenameModal.Close();
            PetNestReleaseConfirmModal.Close();
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

            // 内容区必须能滚动：巢容量上限 24、远征页 9 个档位按钮、博物馆的血脉卡 +
            // 碑文都远超一屏。早先按固定 y 预算铺元素会静默截断——第 5 只之后的崽、
            // 第三个远征目的地、整段纪念碑都会在 UI 上凭空消失。
            _contentRoot = CreateScrollList(
                surface.transform, "Content", new Vector2(0f, 14f), new Vector2(1120f, 412f));
            _actionRoot = CreateScrollList(
                surface.transform, "Actions", new Vector2(0f, -288f), new Vector2(1120f, 128f));
            ZombieModeUIHelper.CreateSeparator("ActionDivider", surface.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -208f), 1f, BossRushUIColors.Divider);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestPanel");
            // ESC / 手柄取消 = 关闭（UA-21）；上面压着改名 / 放生 / 演出时让给它们
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject,
                delegate { CloseAndPlayPendingReveal(); }, IsCoveredByChildWindow);
            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>主面板上面还压着自家的弹窗或演出：ESC 该给它们。</summary>
        private static bool IsCoveredByChildWindow()
        {
            return PetNestRenameModal.IsOpen || PetNestReleaseConfirmModal.IsOpen
                || PetNestHatchRevealView.IsOpen || PetNestExpeditionRevealView.IsOpen;
        }

        private void BuildHeader(Transform parent)
        {
            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", parent,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "SystemName"),
                34f, new Vector2(-60f, 330f), new Vector2(1000f, 52f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            title.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(title);

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
            string[] keys = { "Page_Nest", "Page_Hatch", "Page_Expedition", "Page_Museum" };

            for (int i = 0; i < pages.Length; i++)
            {
                PetNestUIPage page = pages[i];
                Button tab = ZombieModeUIHelper.CreateButton(
                    "Tab_" + page, parent,
                    LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + keys[i]),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-420f + i * 200f, 268f), new Vector2(190f, 44f),
                    BossRushUIColors.SurfaceRaised, 20f, new Vector2(180f, 40f),
                    delegate { _page = page; _batchMode = false; _batchSelection.Clear(); PetNestUIPages.ClearFailure(); Refresh(); }, true);
                // 页签描一圈边：未选中的深底页签对面板底只有约 1.03:1，不描边就是几行浮着的字
                BossRushUIKit.StyleSecondaryButton(tab);
                _tabs[page] = tab;
            }
        }

        #endregion

        #region 刷新

        private void Refresh()
        {
            try
            {
                RectTransform contentRect = _contentRoot as RectTransform;
                bool keepScroll = _hasRendered && _lastRenderedPage == _page && contentRect != null;
                float previousScroll = keepScroll ? contentRect.anchoredPosition.y : 0f;
                PruneBatchSelection();
                ClearSpawned();
                foreach (KeyValuePair<PetNestUIPage, Button> pair in _tabs)
                {
                    bool selected = pair.Key == _page;
                    // 选中页签用 AccentFill（Accent 压深降饱和、走白字）：Accent 只用于描边与小字强调，
                    // 不再整块平涂（2026-09-23 全 Mod 口径）
                    Color color = selected ? BossRushUIColors.AccentFill : BossRushUIColors.SurfaceRaised;
                    // 底色与标签色一起走共享入口；直接写 Image.color 会和 ColorTint 相乘，
                    // 选中的页签反而比未选中的更暗。
                    ZombieModeUIHelper.SetButtonBaseColor(pair.Value, color);
                    TextMeshProUGUI label = pair.Value.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null)
                    {
                        label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                    }
                }
                PetNestPageContent content = BuildPageContent();
                if (content == null) return;

                // 失败提示排在最前：不给反馈的话，巢满 / 写屏障 / 远征锁定这些失败
                // 在界面上与"点歪了"完全无法区分
                if (!string.IsNullOrEmpty(PetNestUIPages.LastFailureText))
                {
                    SpawnNotice(PetNestUIPages.LastFailureText, BossRushUIColors.DangerText);
                }
                if (!string.IsNullOrEmpty(content.Notice))
                {
                    SpawnNotice(content.Notice, BossRushUIColors.WarningText);
                }
                if (!string.IsNullOrEmpty(content.Body))
                {
                    SpawnLine(content.Body, 18f, BossRushUIColors.TextPrimary);
                }
                // 引导行在卡片之前（UA-14）
                for (int i = 0; i < content.Header.Count; i++)
                {
                    SpawnLine(content.Header[i], 18f, BossRushUIColors.TextPrimary);
                }

                // 不再按 y 预算截断：内容区是滚动列表，全部铺出来
                if (content.CardsAsGrid)
                {
                    SpawnGrid(content.Cards);
                }
                else
                {
                    for (int i = 0; i < content.Cards.Count; i++)
                    {
                        SpawnCard(content.Cards[i]);
                    }
                }
                if (!string.IsNullOrEmpty(content.LinesHeader))
                {
                    SpawnSectionHeader(content.LinesHeader);
                }
                for (int i = 0; i < content.Lines.Count; i++)
                {
                    SpawnLine(content.Lines[i], 17f, BossRushUIColors.TextSecondary);
                }

                bool inlineActions = SpawnActions(content.Actions);

                // 底部动作条按**实际条数**定高，剩下的全部还给内容区
                // （owner 2026-09-20：「可选择的地方太小了而且中间有很多留白」）。
                // 旧版无论一条还是九条都占 128px：一条时下面空一大截，
                // 九条时挤在 128px 的小滚动窗里怎么都看不全。横排时固定一行。
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
                }

                Transform divider = _contentRoot.parent.parent.Find("ActionDivider");
                if (divider != null)
                {
                    RectTransform dividerRect = divider.GetComponent<RectTransform>();
                    if (dividerRect != null)
                    {
                        dividerRect.anchoredPosition = new Vector2(
                            0f, BodyTop - contentHeight - ActionGap * 0.5f);
                    }
                    divider.gameObject.SetActive(hasActions);
                }
                _actionRoot.parent.gameObject.SetActive(hasActions);
                Canvas.ForceUpdateCanvases();
                if (keepScroll)
                {
                    // 同页重绘（出战、勾选、放生后）：留在原来看的位置，按新内容高度夹住
                    LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                    float maxScroll = Mathf.Max(0f, contentRect.rect.height - viewport.rect.height);
                    contentRect.anchoredPosition = new Vector2(
                        contentRect.anchoredPosition.x, Mathf.Clamp(previousScroll, 0f, maxScroll));
                }
                else
                {
                    _contentRoot.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1f;
                    // 切页才错峰入场；同页重绘（点选中 / 勾选）不重播，否则每点一下整页都在动
                    if (contentRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                    PlayCardEntrance();
                }
                _actionRoot.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1f;
                _hasRendered = true;
                _lastRenderedPage = _page;
                _nextLiveTick = Time.unscaledTime + 1f;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板刷新失败: " + e.Message);
            }
        }

        /// <summary>
        /// 远征页倒计时走表（UA-22）：一秒一次，只改在途卡片的正文；有一张到点就整页刷新一次去结算。
        /// 没有走表的卡片时第一行就返回。模态租约把 timeScale 压到 0，所以用 unscaled 时间。
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
            switch (_page)
            {
                case PetNestUIPage.Hatch:
                    return PetNestUIPages.BuildHatchPage(Refresh, OnHatched);
                case PetNestUIPage.Expedition:
                    return PetNestUIPages.BuildExpeditionPage(Refresh, ResolveSelectedPetId());
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

        /// <summary>选中一只崽作为远征目标。</summary>
        private void SelectPet(string petId)
        {
            _selectedPetId = petId;
        }

        /// <summary>打开命名弹窗。关闭后刷新面板，让新名字立刻可见。</summary>
        private void OpenRename(string petId)
        {
            PetNestRenameModal.Open(petId, Refresh);
        }

        /// <summary>打开放生确认弹窗（单只或批量）。关闭后刷新面板，让列表与遗魂账本立刻同步。</summary>
        private void OpenRelease(IList<string> petIds)
        {
            PetNestReleaseConfirmModal.Open(petIds, Refresh);
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

        private void SpawnNotice(string text, Color color)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "Notice", _contentRoot, text, 18f,
                Vector2.zero, new Vector2(1080f, 52f),
                TextAlignmentOptions.Left, color);
            BossRushUI.ApplyGameFont(label);
            SetLayoutHeight(label.gameObject, BossRushUI.MeasureTextHeight(label, 1080f, 34f));
            _spawned.Add(label.gameObject);
        }

        /// <summary>
        /// 正文行。字号梯度（UA-15）：页摘要 / 引导行 18 主色，账本 / 碑文 / 说明 17 次色；
        /// 旧写法 19 / 20 / 24 挤在一起、区头也是 19 号次色，看不出层级。
        /// </summary>
        private void SpawnLine(string text, float fontSize, Color color)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "Line", _contentRoot, text, fontSize,
                Vector2.zero, new Vector2(1080f, 34f),
                TextAlignmentOptions.Left, color);
            BossRushUI.ApplyGameFont(label);
            SetLayoutHeight(label.gameObject, BossRushUI.MeasureTextHeight(label, 1080f, 30f));
            _spawned.Add(label.gameObject);
        }

        /// <summary>
        /// 分区标题（遗魂账本、纪念碑）：20 号粗体主色，上留 12 空白，下接一条分隔线（UA-15）。
        /// 单行框高 34 ≥ 20×1.45+4，关了自动缩字也不会被 Ellipsis 整行清空。
        /// </summary>
        private void SpawnSectionHeader(string text)
        {
            GameObject section = ZombieModeUIHelper.CreateRect(
                "Section", _contentRoot, new Vector2(0.5f, 0.5f), new Vector2(1080f, 54f));
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "SectionTitle", section.transform, text, 20f,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -29f), new Vector2(0f, 34f),
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
            SetLayoutHeight(section, 54f);
            _spawned.Add(section);
        }

        private void SpawnCard(PetNestCardData data)
        {
            if (data == null) return;

            // 身份色条：炫彩画第一色、第二色两条；纯异色一条金色；
            // 异色 + 炫彩 = 两条炫彩色 + 第三条金色标记（2026-09-23 复核第 7 项：旧写法金色把第一色顶掉了）。
            Color chromaA, chromaB;
            bool hasA = TryParseHexColor(data.ChromaHexA, out chromaA);
            bool hasB = TryParseHexColor(data.ChromaHexB, out chromaB);
            bool chroma = hasA && hasB;
            Color accent = chroma
                ? chromaA
                : (data.Shiny
                    ? BossRushUIColors.RarityLegendary
                    : (data.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.Accent));

            GameObject card = BossRushUI.CreateCard(
                "Card", _contentRoot, Vector2.zero, CardSize,
                BossRushUIColors.SurfaceRaised, accent, true);
            SetLayoutHeight(card, CardSize.y);
            _spawned.Add(card);
            _entranceTargets.Add(card);

            if (chroma)
            {
                AddIdentityRail(card.transform, "Card_Accent2", 8f, chromaB);
                if (data.Shiny) AddIdentityRail(card.transform, "Card_ShinyMark", 13f, BossRushUIColors.RarityLegendary);
            }

            // 选中态用描边点出来（选中 = 远征 / 放生目标；批量模式下 = 已勾选），
            // 否则玩家无从判断「作用在哪只」。描边是 CreateCard 建的 Stroke 子物体。
            if (data.Selected)
            {
                Transform stroke = card.transform.Find("Stroke");
                Image strokeImage = stroke != null ? stroke.GetComponent<Image>() : null;
                if (strokeImage != null) strokeImage.color = BossRushUIColors.WarningText;
            }

            // 点卡片本身 = 选中 / 勾选。卡上的按钮在更上层，点按钮不会触发卡片。
            // 走共享按钮入口（UA-16）：悬停向 Accent 微微提亮、按下压暗，带官方 hover / click 音效与按下回弹。
            if (data.OnCardClick != null)
            {
                Image surface = card.GetComponent<Image>();
                Button cardButton = card.AddComponent<Button>();
                cardButton.targetGraphic = surface;
                Navigation navigation = cardButton.navigation;
                navigation.mode = Navigation.Mode.None;
                cardButton.navigation = navigation;
                Color rest = BossRushUIColors.SurfaceRaised;
                ZombieModeUIHelper.ApplyButtonColors(cardButton, rest,
                    Color.Lerp(rest, BossRushUIColors.Accent, 0.14f), rest);
                cardButton.onClick.AddListener(new UnityEngine.Events.UnityAction(data.OnCardClick));
            }

            // 左侧立绘 / 图标（UA-10）：取不到就不画这一格，文字照旧从 24 起排
            bool hasIcon = data.Icon != null;
            if (hasIcon)
            {
                Color frameStroke = data.Shiny
                    ? BossRushUIColors.RarityLegendary
                    : (chroma ? chromaA : BossRushUIColors.Stroke);
                CreateIconFrame(card.transform, "Portrait", data.Icon,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(IconLeft, -18f),
                    IconSize, frameStroke, data.IconLocked);
            }

            // 层级（UA-15）：标题 24 粗体主色 / 副标题 18 Accent / 正文 16 次色
            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", card.transform, data.Title, 24f,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -18f), new Vector2(824f, 34f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.margin = Vector4.zero;
            title.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(title);
            ShiftForIcon(title.rectTransform, hasIcon);
            // 异色的「小特效」：金字上走一道流光（owner 2026-09-20 / 09-22）
            if (data.Shiny) PetNestShinyTextShimmer.Attach(title);

            if (!string.IsNullOrEmpty(data.Subtitle))
            {
                TextMeshProUGUI subtitle = ZombieModeUIHelper.CreateText(
                    "Subtitle", card.transform, data.Subtitle, 18f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(24f, -54f), new Vector2(824f, 28f),
                    TextAlignmentOptions.Left, BossRushUIColors.Accent);
                subtitle.rectTransform.pivot = new Vector2(0f, 1f);
                subtitle.margin = Vector4.zero;
                BossRushUI.ApplyGameFont(subtitle);
                ShiftForIcon(subtitle.rectTransform, hasIcon);
            }

            if (!string.IsNullOrEmpty(data.Body))
            {
                TextMeshProUGUI body = ZombieModeUIHelper.CreateText(
                    "Body", card.transform, data.Body, 16f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(24f, -86f), new Vector2(824f, 52f),
                    TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
                body.rectTransform.pivot = new Vector2(0f, 1f);
                BossRushUI.ApplyGameFont(body);
                ShiftForIcon(body.rectTransform, hasIcon);
                float bodyWidth = hasIcon ? 824f - IconColumn : 824f;
                float bodyHeight = BossRushUI.MeasureTextHeight(body, bodyWidth, 32f);
                SetLayoutHeight(card, Mathf.Max(CardSize.y, 86f + bodyHeight + 18f));
                if (data.LiveBody != null)
                {
                    _liveBodies.Add(body);
                    _liveBodySources.Add(data.LiveBody);
                }
            }

            if (data.Checkbox) CreateCheckbox(card.transform, data.Selected);

            bool hasSecondary = !string.IsNullOrEmpty(data.SecondaryLabel);

            // 卡片上的按钮一律是次级样式：深色底 + 描边（普通 Accent、危险 DangerText）。
            // 旧写法每张崽卡都是一块实心青绿「设为出战」，十几张卡右侧整列都是色块（UA-23）。
            if (!string.IsNullOrEmpty(data.ActionLabel))
            {
                bool enabled = data.OnClick != null;
                Button action = ZombieModeUIHelper.CreateButton(
                    "CardAction", card.transform, data.ActionLabel,
                    new Vector2(1f, 0.5f),
                    new Vector2(-114f, hasSecondary ? 26f : 0f),
                    new Vector2(180f, hasSecondary ? 44f : 48f),
                    enabled ? BossRushUIColors.SurfaceRaised : BossRushUIColors.Disabled,
                    18f, new Vector2(170f, hasSecondary ? 40f : 44f),
                    enabled ? new UnityEngine.Events.UnityAction(data.OnClick) : null,
                    enabled);
                if (enabled)
                {
                    AddButtonStroke(action, data.IsDanger ? BossRushUIColors.DangerText : BossRushUIColors.Accent);
                }
            }

            if (hasSecondary)
            {
                Button secondary = ZombieModeUIHelper.CreateButton(
                    "CardSecondary", card.transform, data.SecondaryLabel,
                    new Vector2(1f, 0.5f), new Vector2(-114f, -26f), new Vector2(180f, 44f),
                    data.OnSecondary != null ? BossRushUIColors.SurfaceRaised : BossRushUIColors.Disabled,
                    17f, new Vector2(170f, 40f),
                    data.OnSecondary != null ? new UnityEngine.Events.UnityAction(data.OnSecondary) : null,
                    data.OnSecondary != null);
                if (data.OnSecondary != null) BossRushUIKit.StyleSecondaryButton(secondary);
            }
        }

        /// <summary>有图时把一段卡片文字右移 IconColumn、宽度减同样多（轴心在左上，左缘跟着走）。</summary>
        private static void ShiftForIcon(RectTransform rect, bool hasIcon)
        {
            if (!hasIcon || rect == null) return;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + IconColumn, rect.anchoredPosition.y);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(60f, rect.rect.width - IconColumn));
        }

        /// <summary>第二 / 第三条身份色条：与 CreateCard 的强调竖条同形，挨着排。</summary>
        private static void AddIdentityRail(Transform card, string name, float x, Color color)
        {
            GameObject rail = ZombieModeUIHelper.CreateRect(
                name, card,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(x, 0f), new Vector2(4f, -12f), new Vector2(0f, 0.5f));
            Image railImage = rail.AddComponent<Image>();
            railImage.color = color;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
            railImage.raycastTarget = false;
        }

        /// <summary>按钮描边（次级按钮的彩色轮廓）。共享按钮的底图是 8px 按钮档。</summary>
        private static void AddButtonStroke(Button button, Color color)
        {
            Image image = button != null ? button.targetGraphic as Image : null;
            if (image == null || image.transform.Find("Stroke") != null) return;
            BossRushUI.ApplyPanelStroke(image, 8, BossRushUISkinPart.Button, color);
        }

        /// <summary>
        /// 批量放生的勾选框（UA-18）：卡片右上角 26 见方，勾上 = Danger 底 + 白色「√」，没勾 = 深底 + 描边。
        /// 不吃点击：点卡片本身就是勾选 / 取消。√ 是 GBK 收录字符。
        /// </summary>
        private static void CreateCheckbox(Transform card, bool ticked)
        {
            GameObject box = ZombieModeUIHelper.CreateRect(
                "Checkbox", card, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-14f, -12f), new Vector2(26f, 26f), new Vector2(1f, 1f));
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = ticked ? BossRushUIColors.Danger : BossRushUIColors.Surface;
            boxImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(boxImage, 6, BossRushUISkinPart.Button);
            BossRushUI.ApplyPanelStroke(boxImage, 6, BossRushUISkinPart.Button,
                ticked ? BossRushUIColors.DangerText : BossRushUIColors.Stroke);
            if (!ticked) return;
            TextMeshProUGUI mark = ZombieModeUIHelper.CreateText(
                "Tick", box.transform, "√", 18f,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            mark.margin = Vector4.zero;
            mark.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(mark);
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
        /// 底部动作按钮。**不截断**：远征页是 3 目的地 × 3 档位 = 9 个按钮，
        /// 早先硬截断 6 个会让第三个目的地「极寒荒原」在整个游戏里不可达。
        /// 动作区是滚动列表，多出来的往下排。
        /// 不超过 4 条时横排右对齐（UA-23：旧写法三条横贯全宽的长条，像列表行不像按钮），
        /// 危险操作排最左、与其余隔开 24；返回是否横排，供动作区定高。
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
                if (actions[i] != null && actions[i].IsDanger) { ordered.Add(actions[i]); dangerCount++; }
            }
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && !actions[i].IsDanger) ordered.Add(actions[i]);
            }
            if (ordered.Count == 0) return false;

            GameObject row = ZombieModeUIHelper.CreateRect(
                "ActionRow", _actionRoot, new Vector2(0.5f, 0.5f), new Vector2(1080f, InlineActionHeight));
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
            float available = 1080f - 8f - InlineActionSpacing * (ordered.Count - 1) - (gap ? InlineActionSpacing * 2f : 0f);
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

        /// <summary>动作按钮：危险 = Danger 实心，其余一律次级（深底 + 描边），同一屏不堆色块。</summary>
        private static Button CreateActionButton(Transform parent, string name, PetNestActionData action, Vector2 size)
        {
            bool enabled = action.Interactable && action.OnClick != null;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, action.Label,
                new Vector2(0.5f, 0.5f), Vector2.zero, size,
                action.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.SurfaceRaised,
                18f, new Vector2(size.x - 20f, size.y - 4f),
                action.OnClick != null ? new UnityEngine.Events.UnityAction(action.OnClick) : null,
                enabled);
            if (!action.IsDanger) BossRushUIKit.StyleSecondaryButton(button);
            return button;
        }

        private static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.white;
            int r, g, b;
            if (!PetNestChroma.TryParseHex(hex, out r, out g, out b)) return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
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

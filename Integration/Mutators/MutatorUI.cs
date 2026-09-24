// ============================================================================
// MutatorUI.cs - 每局变异词条 UI 展示
// ============================================================================
// 模块说明：
//   开局后在屏幕左侧常驻本局词条列表，鼠标悬停某一行时在右侧展开详细说明。
//
//   实现说明：本文件原为 IMGUI（OnGUI + GUIStyle）。IMGUI 不随 CanvasScaler
//   缩放，高分屏上字号偏小、且与 Mod 其余界面观感割裂，因此改为 uGUI。
//   Canvas 走 BossRushUI.CreateCanvasRoot（interactive=true：本界面需要接收
//   悬停），面板与行套共享圆角皮肤，字体走统一的游戏字体回退。
//
//   悬停用 EventTrigger 而不是每帧 Rect.Contains：uGUI 的射线检测已经处理了
//   遮挡与缩放，自己算坐标会在非 1080p 下错位。
//
//   2026-09-23 审美审查 UD-40（P1，enableMutators 默认开，每局左侧都挂着它）：
//   - 字号按常驻 HUD 梯度：名字 16、详情正文 15、详情标题 18（正文档 15–18），标题 / 计数 15、分类 14（标签档 13–15）；
//     旧版 11–14 号，而且 CreateTMPText 的自动缩字还能再压到 10。这里全部关掉自动缩字，单行框高 ≥ 字号×1.45+4
//     （框不够高时 TMP Ellipsis 会把整行清空）。
//   - 配色全走 token：分类色 DangerText / SuccessText / WarningText，面板 Surface，行底取 Divider 同一系蓝灰降 alpha。
//   - 悬停不再硬切：行底 0.1 秒过渡，详情卡 0.12 秒淡入并上浮 6px，并对齐到悬停的那一行。
//     动效由每帧入口 Tick 推进（unscaled 时间；暂停菜单开着时 Tick 走抑制分支，动效自然停住），
//     没有过渡在跑时是 O(1) 早返。
//
//   2026-09-24 UI 共识对照审查 A-36：详情不再只能悬停——点一行把说明固定住（鼠标移开也不收），再点一下收起；
//   详情卡底部一行小字说明怎么固定 / 收起。固定的是行号，词条列表重建（新一局）时清掉。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 变异词条 UI 展示（uGUI 实现）
    /// </summary>
    public static class MutatorUI
    {
        // 缓存的词条信息（避免每帧调用 GetDisplayName）
        private static readonly List<CachedMutatorInfo> _cachedInfos = new List<CachedMutatorInfo>();

        private struct CachedMutatorInfo
        {
            public MutatorCategory Category;
            public string DisplayName;
            public string Description;
            public string CategoryLabel;
        }

        // 分类颜色：全走 token（旧版纯红 / 薄荷绿 / 橙三色都不在 token 里）
        private static readonly Color ColorEnemyBuff = BossRushUIColors.DangerText;
        private static readonly Color ColorPlayerBoon = BossRushUIColors.SuccessText;
        private static readonly Color ColorEnvironmentRule = BossRushUIColors.WarningText;
        private static readonly Color ColorPanel = BossRushUIColors.Surface;
        // 行底：Divider 同一系蓝灰降 alpha。叠在 Surface 上，常态比面板亮半档，悬停亮两档，不另起一套灰。
        private static readonly Color ColorRow = WithAlpha(BossRushUIColors.Divider, 0.08f);
        private static readonly Color ColorRowHover = WithAlpha(BossRushUIColors.Divider, 0.26f);

        // 布局常量（1920x1080 参考分辨率下的设计值，由 CanvasScaler 统一缩放）
        private const float PanelWidth = 260f;
        private const float PanelPadding = 10f;
        /// <summary>标题行高：15 号 ×1.45+4 ≈ 26，留到 28。</summary>
        private const float HeaderHeight = 28f;
        /// <summary>标题与第一行之间的空隙，中间放一条分隔线。</summary>
        private const float HeaderGap = 6f;
        /// <summary>行高：名字 16 号 ×1.45+4 ≈ 27.2，留到 34 让行与行之间有呼吸。</summary>
        private const float RowHeight = 34f;
        private const float RowSpacing = 4f;
        /// <summary>行内左侧：3px 分类竖条 + 留白。</summary>
        private const float RowTextLeft = 14f;
        /// <summary>行内右侧分类标签列宽（14 号「ENEMY」约 44px）。</summary>
        private const float CategoryColumnWidth = 60f;
        private const float PanelLeftMargin = 16f;
        private const float PanelTopOffset = -240f;
        private const float DetailWidth = 360f;
        private const float DetailGap = 8f;
        private const float DetailPadding = 14f;
        /// <summary>详情标题行高：18 号 ×1.45+4 ≈ 30.1。</summary>
        private const float DetailTitleHeight = 31f;
        /// <summary>详情卡底边离参考画布底边至少留这么多（10 条词条时最后一行的详情也不出屏）。</summary>
        private const float DetailBottomMargin = 24f;

        // 字号（常驻 HUD 梯度：正文 15–18、标签 13–15）
        private const float HeaderFontSize = 15f;
        private const float NameFontSize = 16f;
        private const float CategoryFontSize = 14f;
        private const float DetailTitleFontSize = 18f;
        private const float DetailBodyFontSize = 15f;

        // 悬停动效
        private const float RowHoverSeconds = 0.10f;
        private const float DetailEnterSeconds = 0.12f;
        private const float DetailRise = 6f;

        private static bool _cornerVisible;

        private static Canvas _canvas;
        private static GameObject _panelRoot;
        private static GameObject _detailRoot;
        private static RectTransform _detailRect;
        private static CanvasGroup _detailGroup;
        private static TextMeshProUGUI _detailTitleText;
        private static TextMeshProUGUI _detailBodyText;
        private static readonly List<Image> _rowBackgrounds = new List<Image>();
        private static readonly List<float> _rowWeights = new List<float>();
        private static int _hoveredIndex = -1;
        /// <summary>点击固定的那一行（-1 = 没有固定）。鼠标离开行时详情退回这一行而不是收起。</summary>
        private static int _pinnedIndex = -1;
        private static float _detailWeight;
        private static float _detailTarget;
        private static float _detailBaseY;
        private static bool _hoverAnimating;

        // ═══════════════════════════════════════════
        // 公共方法
        // ═══════════════════════════════════════════

        /// <summary>
        /// 准备本局词条 UI（历史方法名保留给现有调用点）
        /// </summary>
        public static void ShowBanner()
        {
            _cachedInfos.Clear();
            _pinnedIndex = -1;

            var mutators = MutatorManager.GetActiveMutators();
            if (mutators == null || mutators.Count == 0) return;

            for (int i = 0; i < mutators.Count; i++)
            {
                string displayName = mutators[i].GetDisplayName();
                string description = mutators[i].GetDescription();
                MutatorCategory category = mutators[i].Category;

                _cachedInfos.Add(new CachedMutatorInfo
                {
                    Category = category,
                    DisplayName = displayName,
                    Description = description,
                    CategoryLabel = GetCategoryLabel(category)
                });
            }

            _cornerVisible = true;
            DestroyCanvas();
        }

        /// <summary>
        /// 隐藏所有 UI（模式结束时调用）
        /// </summary>
        public static void HideAll()
        {
            _cornerVisible = false;
            _cachedInfos.Clear();
            _hoveredIndex = -1;
            _pinnedIndex = -1;
            DestroyCanvas();
        }

        /// <summary>
        /// 每帧调用：维护词条 UI 的可见性与悬停详情。
        ///
        /// 模态界面与自定义弹层会关闭 gameplay 输入；图片查看器则是把 timeScale
        /// 压到 0 但不申请输入租约，所以两条都要判。抑制期间只隐藏 Canvas 并清掉
        /// 悬停，缓存的词条列表在界面关闭后原样回来。
        /// </summary>
        public static void Tick()
        {
            if (!MutatorManager.IsActive || _cachedInfos.Count == 0 || !_cornerVisible)
            {
                SetCanvasVisible(false);
                return;
            }

            bool suppressed;
            try
            {
                suppressed = !InputManager.InputActived || Time.timeScale <= 0f;
            }
            catch
            {
                suppressed = true;
            }

            // 常驻浮层口径（2026-09-14）：官方界面（背包 / 地图 / 对话 / 捏脸 / 拍照模式）与暂停菜单开着时一起收起。
            // 上面两条只看 gameplay 输入与 timeScale，是否覆盖官方对话与全部 View 没有核实过；这里直接用与 SkyIslandHud 同一份判定。
            if (!suppressed && (BossRushUI.IsOfficialHudHidden() || BossRushUI.IsGamePaused()))
            {
                suppressed = true;
            }

            if (suppressed)
            {
                SetHoveredIndex(-1);
                SettleHover();
                SetCanvasVisible(false);
                return;
            }

            EnsureCanvas();
            SetCanvasVisible(true);
            // 抑制期间悬停被清掉了；固定的说明在界面回来时一起回来（O(1)，状态没变时 SetHoveredIndex 直接返回）
            if (_pinnedIndex >= 0 && _hoveredIndex < 0)
            {
                SetHoveredIndex(_pinnedIndex);
            }
            AnimateHover(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 释放 Canvas 与缓存。切场景 / OnDestroy 路径调用。
        /// </summary>
        public static void ResetStaticCaches()
        {
            _cornerVisible = false;
            _cachedInfos.Clear();
            _hoveredIndex = -1;
            _pinnedIndex = -1;
            DestroyCanvas();
        }

        // ═══════════════════════════════════════════
        // Canvas 构建
        // ═══════════════════════════════════════════

        private static void EnsureCanvas()
        {
            if (_canvas != null && _panelRoot != null)
            {
                return;
            }

            DestroyCanvas();

            try
            {
                _canvas = BossRushUI.CreateCanvasRoot("BossRush_MutatorOverlay", BossRushUILayers.HudOverlay, true);
                UnityEngine.Object.DontDestroyOnLoad(_canvas.gameObject);

                float panelHeight = PanelPadding * 2f + HeaderHeight + HeaderGap +
                                    _cachedInfos.Count * RowHeight +
                                    Mathf.Max(0, _cachedInfos.Count - 1) * RowSpacing;

                _panelRoot = ZombieModeUIHelper.CreateRect(
                    "MutatorPanel",
                    _canvas.transform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(PanelLeftMargin, PanelTopOffset),
                    new Vector2(PanelWidth, panelHeight),
                    new Vector2(0f, 1f));
                Image panelImage = _panelRoot.AddComponent<Image>();
                panelImage.color = ColorPanel;
                BossRushUI.ApplyFramedPanelSkin(panelImage, 10, BossRushUISkinPart.Card);

                BuildHeader();
                BuildRows();
                BuildDetailPanel();
                SetHoveredIndex(-1);
                SettleHover();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MutatorUI] 创建变异词条 UI 失败: " + e.Message);
                DestroyCanvas();
            }
        }

        private static void BuildHeader()
        {
            TextMeshProUGUI header = CreateLabel(
                "Header",
                _panelRoot.transform,
                L10n.T("本局变异", "ACTIVE MUTATORS"),
                HeaderFontSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(PanelPadding, -PanelPadding),
                new Vector2(-PanelPadding * 2f, HeaderHeight),
                TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextSecondary);
            // 全大写英文标题拉开字距；中文同样受益（小字号标签挤在一起时更难扫读）。
            header.characterSpacing = 4f;
            header.enableWordWrapping = false;
            header.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI count = CreateLabel(
                "Count",
                _panelRoot.transform,
                _cachedInfos.Count.ToString(),
                HeaderFontSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(PanelPadding, -PanelPadding),
                new Vector2(-PanelPadding * 2f, HeaderHeight),
                TextAlignmentOptions.MidlineRight,
                BossRushUIColors.TextPrimary);
            count.enableWordWrapping = false;

            // 标题与列表之间一条细分隔线（分隔线档，rect 自动撑到 ≥8 高、看到的是 1–2px）。
            GameObject rule = ZombieModeUIHelper.CreateSeparator(
                "HeaderRule",
                _panelRoot.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(PanelPadding + HeaderHeight + HeaderGap * 0.5f)),
                1f,
                BossRushUIColors.Divider);
            RectTransform ruleRect = rule.GetComponent<RectTransform>();
            ruleRect.sizeDelta = new Vector2(-PanelPadding * 2f, ruleRect.sizeDelta.y);
        }

        private static void BuildRows()
        {
            _rowBackgrounds.Clear();
            _rowWeights.Clear();
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                CachedMutatorInfo info = _cachedInfos[i];
                float top = GetRowTopInPanel(i);

                GameObject row = ZombieModeUIHelper.CreateRect(
                    "Row_" + i,
                    _panelRoot.transform,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(PanelPadding, top),
                    new Vector2(-PanelPadding * 2f, RowHeight),
                    new Vector2(0f, 1f));
                Image rowImage = row.AddComponent<Image>();
                rowImage.color = ColorRow;
                BossRushUI.ApplyPanelSkin(rowImage, 6, BossRushUISkinPart.Card);   // 浮层里的淡底行靠悬停提亮区分，不叠描边：每行加框会变成一格一格的表
                _rowBackgrounds.Add(rowImage);
                _rowWeights.Add(0f);

                // 分类色竖条：沿用奖励卡/模态标题的 accent rail 视觉语言（≤3px 细条走 Hairline 档，程序化绘制）。
                GameObject accent = ZombieModeUIHelper.CreateRect(
                    "Accent",
                    row.transform,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(4f, 0f),
                    new Vector2(3f, -12f),
                    new Vector2(0f, 0.5f));
                Image accentImage = accent.AddComponent<Image>();
                accentImage.color = GetCategoryColor(info.Category);
                accentImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(accentImage, 2, BossRushUISkinPart.Hairline);

                TextMeshProUGUI nameText = CreateLabel(
                    "Name",
                    row.transform,
                    info.DisplayName,
                    NameFontSize,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 0.5f),
                    new Vector2(RowTextLeft, 0f),
                    new Vector2(-(RowTextLeft + CategoryColumnWidth + 6f), 0f),
                    TextAlignmentOptions.MidlineLeft,
                    BossRushUIColors.TextPrimary);
                nameText.enableWordWrapping = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;

                TextMeshProUGUI categoryText = CreateLabel(
                    "Category",
                    row.transform,
                    info.CategoryLabel,
                    CategoryFontSize,
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0.5f),
                    new Vector2(-10f, 0f),
                    new Vector2(CategoryColumnWidth, 0f),
                    TextAlignmentOptions.MidlineRight,
                    GetCategoryColor(info.Category));
                categoryText.enableWordWrapping = false;
                categoryText.characterSpacing = 2f;

                AttachHoverHandler(row, i);
            }
        }

        /// <summary>
        /// 用 EventTrigger 挂悬停与点击回调。索引按值捕获，不能直接用循环变量。
        /// 点击 = 固定 / 收起这一行的说明（A-36）；离开行时详情退回固定的那一行。
        /// </summary>
        private static void AttachHoverHandler(GameObject row, int index)
        {
            EventTrigger trigger = row.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry();
            enter.eventID = EventTriggerType.PointerEnter;
            int enterIndex = index;
            enter.callback.AddListener(delegate { SetHoveredIndex(enterIndex); });
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry();
            exit.eventID = EventTriggerType.PointerExit;
            int exitIndex = index;
            exit.callback.AddListener(delegate
            {
                if (_hoveredIndex == exitIndex)
                {
                    SetHoveredIndex(_pinnedIndex);
                }
            });
            trigger.triggers.Add(exit);

            EventTrigger.Entry click = new EventTrigger.Entry();
            click.eventID = EventTriggerType.PointerClick;
            int clickIndex = index;
            click.callback.AddListener(delegate { TogglePinned(clickIndex); });
            trigger.triggers.Add(click);
        }

        /// <summary>点一行：固定它的说明；再点同一行收起固定（鼠标还在行上，说明照常跟着悬停）。</summary>
        private static void TogglePinned(int index)
        {
            _pinnedIndex = _pinnedIndex == index ? -1 : index;
            SetHoveredIndex(index, true);
        }

        private static void BuildDetailPanel()
        {
            _detailRoot = ZombieModeUIHelper.CreateRect(
                "Detail",
                _canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(PanelLeftMargin + PanelWidth + DetailGap, PanelTopOffset),
                new Vector2(DetailWidth, 148f),
                new Vector2(0f, 1f));
            _detailRect = _detailRoot.GetComponent<RectTransform>();
            Image detailImage = _detailRoot.AddComponent<Image>();
            detailImage.color = BossRushUIColors.Surface;
            detailImage.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(detailImage, 10, BossRushUISkinPart.Card);

            // 淡入与上浮走 CanvasGroup；详情卡只读，不吃射线（否则会挡住下一行的悬停）。
            _detailGroup = _detailRoot.AddComponent<CanvasGroup>();
            _detailGroup.alpha = 0f;
            _detailGroup.blocksRaycasts = false;
            _detailGroup.interactable = false;

            _detailTitleText = CreateLabel(
                "DetailTitle",
                _detailRoot.transform,
                string.Empty,
                DetailTitleFontSize,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(DetailPadding, -(DetailPadding - 4f)),
                new Vector2(-DetailPadding * 2f, DetailTitleHeight),
                TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextPrimary);
            _detailTitleText.enableWordWrapping = false;
            _detailTitleText.overflowMode = TextOverflowModes.Ellipsis;

            // 正文按内容量高（MeasureTextHeight），卡片跟着长；长段落左对齐。
            _detailBodyText = CreateLabel(
                "DetailBody",
                _detailRoot.transform,
                string.Empty,
                DetailBodyFontSize,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(DetailPadding, -(DetailPadding - 4f + DetailTitleHeight + 4f)),
                new Vector2(DetailWidth - DetailPadding * 2f, 24f),
                TextAlignmentOptions.TopLeft,
                BossRushUIColors.TextSecondary);
            _detailBodyText.enableWordWrapping = true;
            _detailBodyText.lineSpacing = 4f;

            _detailRoot.SetActive(false);
        }

        // ═══════════════════════════════════════════
        // 悬停状态
        // ═══════════════════════════════════════════

        /// <summary>
        /// 切换悬停行：只改目标状态与详情内容，过渡由 <see cref="AnimateHover"/> 在 Tick 里推进。
        /// 只在指针进出行时调用，不是每帧路径（正文测高也只在这里做一次）。
        /// </summary>
        private static void SetHoveredIndex(int index, bool force = false)
        {
            bool show = index >= 0 && index < _cachedInfos.Count;
            float target = show ? 1f : 0f;
            // 抑制期间 Tick 每帧都会传 -1：状态没变就什么都不做，保持每帧 O(1)。固定 / 收起时强制刷新提示行。
            if (!force && index == _hoveredIndex && _detailTarget == target)
            {
                return;
            }

            _hoveredIndex = index;
            _detailTarget = target;
            _hoverAnimating = true;
            if (_detailRoot == null || !show)
            {
                return;
            }

            // 先激活再测高：TMP 在激活状态下排版最可靠。卡片此时的透明度仍是当前过渡值，不会闪出来。
            if (!_detailRoot.activeSelf)
            {
                _detailRoot.SetActive(true);
            }

            CachedMutatorInfo info = _cachedInfos[index];
            if (_detailTitleText != null)
            {
                _detailTitleText.text = info.DisplayName;
                _detailTitleText.color = GetCategoryColor(info.Category);
            }

            float bodyHeight = 0f;
            if (_detailBodyText != null)
            {
                // 底部一行小字告诉玩家说明能固定（A-36）：只能悬停时鼠标一动说明就没了
                string hint = _pinnedIndex == index
                    ? L10n.T("已固定 · 再点这一行收起", "Pinned · click this row again to unpin")
                    : L10n.T("点这一行可以固定说明", "Click this row to pin the details");
                _detailBodyText.text = info.Description + "\n<size=" + CategoryFontSize + "><color="
                    + IntegrationUIFeedback.SecondaryHex + ">" + hint + "</color></size>";
                bodyHeight = BossRushUI.MeasureTextHeight(_detailBodyText, DetailWidth - DetailPadding * 2f, 24f);
            }

            float detailHeight = (DetailPadding - 4f) + DetailTitleHeight + 4f + bodyHeight + DetailPadding;
            if (_detailRect != null)
            {
                _detailRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, detailHeight);
            }

            // 详情卡顶边对齐悬停的那一行；行太靠下时往上收，底边不出参考画布。
            float rowTop = PanelTopOffset + GetRowTopInPanel(index);
            float lowestTop = detailHeight - (ZombieModeUIHelper.ReferenceResolution.y - DetailBottomMargin);
            _detailBaseY = Mathf.Max(rowTop, lowestTop);

            ApplyDetailWeight();
        }

        /// <summary>
        /// 推进悬停过渡。每帧入口调用；没有过渡在跑时 O(1) 早返，不分配。
        /// 行底 0.1 秒 SmoothStep（原地变色），详情卡 0.12 秒 EaseOut 淡入并上浮（位移类）。
        /// </summary>
        private static void AnimateHover(float unscaledDelta)
        {
            if (!_hoverAnimating)
            {
                return;
            }

            bool moving = false;
            float rowStep = unscaledDelta / RowHoverSeconds;
            for (int i = 0; i < _rowBackgrounds.Count && i < _rowWeights.Count; i++)
            {
                float target = i == _hoveredIndex ? 1f : 0f;
                float weight = _rowWeights[i];
                if (weight == target)
                {
                    continue;
                }
                weight = Mathf.MoveTowards(weight, target, rowStep);
                _rowWeights[i] = weight;
                ApplyRowWeight(i);
                if (weight != target)
                {
                    moving = true;
                }
            }

            if (_detailRoot != null && _detailWeight != _detailTarget)
            {
                _detailWeight = Mathf.MoveTowards(_detailWeight, _detailTarget, unscaledDelta / DetailEnterSeconds);
                ApplyDetailWeight();
                if (_detailWeight != _detailTarget)
                {
                    moving = true;
                }
                else if (_detailTarget <= 0f && _detailRoot.activeSelf)
                {
                    _detailRoot.SetActive(false);
                }
            }

            _hoverAnimating = moving;
        }

        /// <summary>抑制 / 重建时把过渡直接落到终点：隐藏期间不值得播，重新出现时也不该从半截接着播。</summary>
        private static void SettleHover()
        {
            if (!_hoverAnimating)
            {
                return;
            }

            for (int i = 0; i < _rowWeights.Count; i++)
            {
                _rowWeights[i] = i == _hoveredIndex ? 1f : 0f;
                ApplyRowWeight(i);
            }

            _detailWeight = _detailTarget;
            if (_detailRoot != null)
            {
                ApplyDetailWeight();
                bool show = _detailTarget > 0f;
                if (_detailRoot.activeSelf != show)
                {
                    _detailRoot.SetActive(show);
                }
            }
            _hoverAnimating = false;
        }

        private static void ApplyRowWeight(int index)
        {
            Image background = index < _rowBackgrounds.Count ? _rowBackgrounds[index] : null;
            if (background != null)
            {
                background.color = Color.Lerp(ColorRow, ColorRowHover, BossRushUI.SmoothStep(_rowWeights[index]));
            }
        }

        private static void ApplyDetailWeight()
        {
            float eased = BossRushUI.EaseOut(_detailWeight);
            if (_detailGroup != null)
            {
                _detailGroup.alpha = eased;
            }
            if (_detailRect != null)
            {
                _detailRect.anchoredPosition = new Vector2(
                    PanelLeftMargin + PanelWidth + DetailGap,
                    _detailBaseY - (1f - eased) * DetailRise);
            }
        }

        private static void SetCanvasVisible(bool visible)
        {
            if (_canvas == null)
            {
                return;
            }

            if (_canvas.gameObject.activeSelf != visible)
            {
                _canvas.gameObject.SetActive(visible);
            }
        }

        private static void DestroyCanvas()
        {
            if (_canvas != null)
            {
                try { UnityEngine.Object.Destroy(_canvas.gameObject); }
                catch (Exception e) { Debug.LogWarning("[MutatorUI] 销毁变异词条 UI 失败: " + e.Message); }
            }

            _canvas = null;
            _panelRoot = null;
            _detailRoot = null;
            _detailRect = null;
            _detailGroup = null;
            _detailTitleText = null;
            _detailBodyText = null;
            _rowBackgrounds.Clear();
            _rowWeights.Clear();
            _detailWeight = 0f;
            _detailTarget = 0f;
            _hoverAnimating = false;
        }

        // ═══════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════

        /// <summary>第 index 行顶边在面板内的 y（面板左上为原点，向下为负）。</summary>
        private static float GetRowTopInPanel(int index)
        {
            return -(PanelPadding + HeaderHeight + HeaderGap + index * (RowHeight + RowSpacing));
        }

        /// <summary>
        /// 本浮层的文字：共享字体入口建 TMP，然后关掉自动缩字、清掉默认内边距——
        /// 常驻 HUD 的字号梯度要守住，版式由 rect 决定（CreateTMPText 默认最小缩到 0.65 倍）。
        /// </summary>
        private static TextMeshProUGUI CreateLabel(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, anchorMin, anchorMax, anchoredPosition, sizeDelta, pivot);
            TextMeshProUGUI label = ZombieModeUIHelper.CreateTMPText(obj, text, fontSize, alignment, color);
            label.enableAutoSizing = false;
            label.margin = Vector4.zero;
            label.raycastTarget = false;
            return label;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color GetCategoryColor(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return ColorEnemyBuff;
                case MutatorCategory.PlayerBoon: return ColorPlayerBoon;
                case MutatorCategory.EnvironmentRule: return ColorEnvironmentRule;
                default: return BossRushUIColors.TextPrimary;
            }
        }

        private static string GetCategoryLabel(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return L10n.T("敌方", "ENEMY");
                case MutatorCategory.PlayerBoon: return L10n.T("增益", "BOON");
                case MutatorCategory.EnvironmentRule: return L10n.T("规则", "RULE");
                default: return L10n.T("其他", "OTHER");
            }
        }
    }
}

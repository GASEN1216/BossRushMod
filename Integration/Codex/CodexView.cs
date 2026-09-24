// ============================================================================
// CodexView.cs - 鸭皇图鉴主面板（骨架 / 生命周期 / 头部 / 进度条 / 滚动容器）
// ============================================================================
// 骨架照 Achievement/AchievementView.cs（单例 MonoBehaviour + DontDestroyOnLoad +
// 自建 Canvas + 官方 ScrollRect prefab 优先），网格卡片与详情弹层在同一个 partial
// 的续篇 CodexView_Grid.cs 里。
// 保留自绘的理由：官方 NoteIndex 不提供按 Boss 的击杀/速杀统计和待收集筛选。
//
// UI 硬约束（AGENTS.md 4.14，全部走共享库，无一例外）：
//   - Canvas 走 BossRushUI.CreateCanvasRoot + BossRushUILayers 常量，禁魔法数字；
//   - 遮罩走 BossRushUI.CreateBackdrop（Backdrop token），不引入第二套 (0,0,0,0.7)；
//   - 底图走 BossRushUI.ApplyPanelSkin，颜色只用 BossRushUIColors token；
//   - 全部文本用 TMP，字体走 BossRushUI.ApplyGameFont / ZombieModeUIHelper.CreateTMPText，
//     **严禁** Resources.GetBuiltinResource<Font>("Arial.ttf")（渲染不了中文）；
//   - 手写 CanvasScaler 时必须过 ZombieModeUIHelper.ConfigureCanvasScaler
//     （CreateCanvasRoot 内部已经过了，这里不再手写）。
//
// 性能硬约束：
//   - 仅打开、翻页、筛选、语言或已提交快照变化时重建；
//     Update() 常态只有 O(1) 变化比较，无目录扫描或字符串拼接。
//
// 输入（2026-09-24 UI 共识对照审查 A-43）：打开时占模态租约（ZombieModeUIHelper.ClaimModalInput，
//   时停 + 光标 + 输入占用一处管），ESC / 手柄取消走 PetNestCancelKey（订官方 OnCancelEarly 并用掉事件）——
//   旧版 Input.GetKeyDown(Escape) 不用掉官方取消事件，关面板的同时官方暂停菜单也会弹出来。
//   - 立绘走 CodexPortraitCache 的 fail-open 三级占位链，缺图不阻断面板。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>鸭皇图鉴主面板。全 Mod 唯一实例。</summary>
    public partial class CodexView : MonoBehaviour
    {
        #region 布局常量

        private const float HeaderHeight = 58f;
        private const float ProgressHeight = 92f;
        /// <summary>底部留白。分页控件已取消，这里只留一点视觉呼吸，不再放文字或按钮。</summary>
        private const float FooterHeight = 14f;

        /// <summary>滚轮灵敏度。卡片高 236px（CodexTuning.CardHeight），取 8 约等于一格滚过半张卡。</summary>
        private const float ScrollSensitivity = 8f;
        private const float PanelSidePadding = 20f;
        private const float GridPadding = 12f;

        private const float PanelHeightRatio = 0.82f;
        private const float MinPanelHeight = 560f;
        private const float MaxPanelHeight = 880f;

        // 字号四级（UI 共识第 7 节，A-20）：标题 28 / 名字与进度 17 / 正文 15 / 注脚 14。
        private const float TitleFontSize = 28f;
        private const float NameFontSize = 17f;
        private const float BodyFontSize = 15f;
        private const float NoteFontSize = 14f;
        private const float FilterSegmentHeight = 30f;

        #endregion

        #region 单例

        private static CodexView _instance;

        /// <summary>
        /// 面板门面。取用时若不存在会自动建实例（形态同 AchievementView）。
        /// 只想判"实例是否存在"请用 IsInstanceAlive，别拿这个属性判空。
        /// </summary>
        public static CodexView Instance
        {
            get
            {
                if (_instance == null)
                {
                    EnsureInstance();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 实例是否已存在（**不会**触发创建）。场景回调、开关热切一类的路径
        /// 必须用它，否则会在清理路径上凭空造出一个面板。
        /// </summary>
        internal static bool IsInstanceAlive
        {
            get { return _instance != null; }
        }

        /// <summary>幂等创建实例。</summary>
        public static void EnsureInstance()
        {
            try
            {
                if (_instance != null) return;

                GameObject obj = new GameObject("CodexView");
                _instance = obj.AddComponent<CodexView>();
                DontDestroyOnLoad(obj);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 创建图鉴面板失败: " + e.Message);
            }
        }

        /// <summary>
        /// 静态缓存重置：销毁实例并清引用（宿主销毁 / Mod 卸载）。
        ///
        /// **必须先走 Close()**：面板打开时占了模态租约（输入占用 + 时停），
        /// 只有 `Close()` 会把租约还回去。直接 Destroy 绕过它的话，
        /// 宿主在面板开着时销毁会把玩家输入**永久**锁死，只能重启游戏。
        /// </summary>
        public static void ResetStaticCaches()
        {
            try
            {
                if (_instance != null)
                {
                    // 先归还输入占用，再销毁对象。Close 幂等，且不动 _instance，
                    // 下面的销毁块照常执行（清 _instance 的是 OnDestroy）。
                    _instance._destroying = true;
                    try { _instance.Close(); }
                    catch (Exception closeEx)
                    {
                        ModBehaviour.DevLog(
                            CodexTuning.LogPrefix + "[WARNING] 图鉴面板关闭失败: " + closeEx.Message);
                    }
                }
                if (_instance != null)
                {
                    GameObject go = _instance.gameObject;
                    _instance = null;
                    if (go != null)
                    {
                        Destroy(go);
                    }
                }
            }
            catch (Exception)
            {
                // 销毁失败不得拖崩宿主清理链
            }
            _instance = null;
        }

        #endregion

        #region UI 引用

        private Canvas _canvas;
        /// <summary>画布根上的 CanvasGroup：关闭时整块（遮罩 + 面板）淡出，淡完才 SetActive(false)。</summary>
        private CanvasGroup _canvasGroup;
        private Image _backdropImage;
        private Coroutine _closeFade;
        /// <summary>销毁路径上 Close 走立即关闭，不在正在销毁的对象上起协程。</summary>
        private bool _destroying;
        private GameObject _panelRoot;
        private ScrollRect _scrollRect;
        private RectTransform _contentContainer;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _progressText;
        private Image _progressFill;
        private RectTransform _progressTrack;
        /// <summary>筛选分段（A-18）：「全部 / 待收集 · N」两颗并排，选中的一颗压 Accent 底 + WarningText 描边。</summary>
        private Button _filterAllButton;
        private Button _filterMissingButton;
        private TextMeshProUGUI _statusText;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;

        private float _panelWidth;
        private float _panelHeight;

        #endregion

        #region 状态

        private bool _isOpen;
        private bool _uiBuilt;
        private bool _isChinese;
        private CodexData _renderedData;

        /// <summary>当前渲染出来的卡片。重建网格时逐个销毁。</summary>
        private readonly List<GameObject> _cards = new List<GameObject>();

        #endregion

        #region 公共属性

        /// <summary>面板是否打开。</summary>
        public bool IsOpen
        {
            get { return _isOpen; }
        }

        #endregion

        #region 生命周期

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            try
            {
                CalculatePanelSize();
                CreateUI();
                _uiBuilt = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 图鉴面板构建失败: " + e.Message);
            }

            Close();
        }

        private void OnDestroy()
        {
            _destroying = true;
            try
            {
                Close();
                HideDetail();
                ClearCards();
            }
            catch (Exception)
            {
                // 清理失败静默：销毁路径不得抛
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>处理语言/已提交快照变化，稳定状态不重建。ESC 走 PetNestCancelKey（OnCancel）。</summary>
        private void Update()
        {
            if (!_isOpen) return;

            // 仅比较语言与已提交快照；变化时才更新，平时不扫描目录或创建文本。
            if (_isChinese != L10n.IsChinese || !ReferenceEquals(_renderedData, CodexPersistence.Current))
                RefreshAll();
        }

        /// <summary>ESC / 手柄取消：详情弹层开着时先收详情，再按一次才关面板。</summary>
        private void OnCancel()
        {
            if (IsDetailOpen)
            {
                HideDetail();
            }
            else
            {
                Close();
            }
        }

        #endregion

        #region 开关

        /// <summary>打开面板并全量刷新。</summary>
        public void Open()
        {
            if (_isOpen) return;
            if (!_uiBuilt) return;

            _isOpen = true;
            // 上一次关闭的淡出还没播完就又打开：停掉淡出、恢复成完整面板
            StopCloseFade();
            IntegrationUIFeedback.ResetFade(_canvasGroup);
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
            }
            // 遮罩只在建 UI 时淡入过一次；常驻面板每次打开都要重播，否则第二次起背景又是一帧压黑
            if (_backdropImage != null)
            {
                BossRushUIEntranceAnimation.Play(_backdropImage.gameObject, 0f, 0.15f, 0f);
            }

            // 模态租约（A-43）：时停、光标、输入占用与其它模态面板共用一个计数，关闭时成对归还
            if (_modalLease == null)
            {
                _modalLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "Codex");
            }
            if (_canvas != null)
            {
                _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject, OnCancel, IsCancelCovered);
            }

            RefreshAll();

            if (_scrollRect != null)
            {
                _scrollRect.verticalNormalizedPosition = 1f;
            }

            BossRushUI.PlayOpenAnimation(_panelRoot);
            ModBehaviour.DevLog(CodexTuning.LogPrefix + "图鉴面板已打开");
        }

        /// <summary>关闭面板。幂等。</summary>
        public void Close()
        {
            bool wasOpen = _isOpen;
            _isOpen = false;

            HideDetail();

            // 先还输入，再播淡出：动效绝不能变成输入延迟
            ReleaseModalInput();

            if (_canvas != null)
            {
                // 玩家关面板时 0.12 秒淡出（打开有长出来、关闭一帧消失，前后不对称，审美审查 UD-03）；
                // 构建期与销毁路径直接关。
                if (wasOpen && !_destroying && isActiveAndEnabled && _canvasGroup != null
                    && _canvas.gameObject.activeSelf)
                {
                    StopCloseFade();
                    _closeFade = StartCoroutine(IntegrationUIFeedback.FadeOutAndDeactivate(
                        _canvasGroup, _canvas.gameObject, BossRushUIKit.CloseSeconds));
                }
                else
                {
                    StopCloseFade();
                    _canvas.gameObject.SetActive(false);
                    IntegrationUIFeedback.ResetFade(_canvasGroup);
                }
            }

            if (wasOpen)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "图鉴面板已关闭");
            }
        }

        /// <summary>归还模态租约并停听取消键。幂等：Awake / OnDestroy / ResetStaticCaches 都会走到。</summary>
        private void ReleaseModalInput()
        {
            if (_cancelKey != null)
            {
                _cancelKey.Detach();
                _cancelKey = null;
            }
            try
            {
                if (_modalLease != null)
                {
                    _modalLease.Release();
                }
            }
            catch (Exception)
            {
                // 输入释放失败不阻断关闭
            }
            _modalLease = null;
        }

        /// <summary>共享确认框压在上面时 ESC 让给它。</summary>
        private static bool IsCancelCovered()
        {
            return BossRushConfirmDialog.IsOpen;
        }

        private void StopCloseFade()
        {
            if (_closeFade != null)
            {
                StopCoroutine(_closeFade);
                _closeFade = null;
            }
        }

        /// <summary>开/关切换（物品与调试入口都走它）。</summary>
        public void Toggle()
        {
            if (_isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>全量刷新：标题、进度条、网格。只在打开与显式刷新时调用。</summary>
        public void RefreshAll()
        {
            if (!_uiBuilt) return;

            try
            {
                CodexData data = CodexPersistence.Current;
                _renderedData = data;
                _isChinese = L10n.IsChinese;
                HideDetail();
                if (_titleText != null) _titleText.text = CodexBookConfig.GetDisplayName();

                // 老档补齐：面板打开时把该发未发的里程碑补上
                CodexMilestones.EvaluateOnPanelOpen(data);

                UpdateProgress(data);
                PopulateGrid(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 图鉴刷新失败: " + e.Message);
            }
        }

        #endregion

        #region 构建

        private void CalculatePanelSize()
        {
            // 宽度由网格反推：4 列 × 卡片宽 + 列间距 + 内边距 + 面板留白。
            // 这样不会出现"面板很宽、卡片挤在左边一坨"的空旷布局。
            int columns = CodexTuning.GridColumns > 0 ? CodexTuning.GridColumns : 4;
            float gridWidth = columns * CodexTuning.CardWidth
                + (columns - 1) * CodexTuning.CardSpacing
                + GridPadding * 2f;
            _panelWidth = gridWidth + PanelSidePadding * 2f;

            float screenHeight = ZombieModeUIHelper.GetReferenceViewportSize().y;
            _panelHeight = Mathf.Clamp(screenHeight * PanelHeightRatio, MinPanelHeight, MaxPanelHeight);
        }

        private void CreateUI()
        {
            Canvas canvas = BossRushUI.CreateCanvasRoot("CodexCanvas", BossRushUILayers.Panel, true);
            canvas.transform.SetParent(transform, false);
            _canvas = canvas;
            _canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();

            Image backdrop = BossRushUI.CreateBackdrop(canvas.transform);
            _backdropImage = backdrop;
            Button backdropButton = backdrop.gameObject.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.onClick.AddListener(Close);

            CreateMainPanel(canvas.transform);
            CreateHeader();
            CreateProgressBar();
            CreateScrollArea();
        }

        private void CreateMainPanel(Transform parent)
        {
            _panelRoot = ZombieModeUIHelper.CreateRect(
                "Panel",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(_panelWidth, _panelHeight),
                new Vector2(0.5f, 0.5f));

            Image panelImage = _panelRoot.AddComponent<Image>();
            panelImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(panelImage, 14, BossRushUISkinPart.Panel);

            // 吃掉穿透到 backdrop 的点击，否则点面板本体会把面板关掉
            Button panelButton = _panelRoot.AddComponent<Button>();
            panelButton.transition = Selectable.Transition.None;
        }

        private void CreateHeader()
        {
            GameObject header = ZombieModeUIHelper.CreateRect(
                "Header",
                _panelRoot.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, HeaderHeight),
                new Vector2(0.5f, 1f));

            // 标题栏不铺底色（审美审查 UD-01）：旧的圆角 Header 色块压在面板顶上，四角的弧和面板的弧对不上，
            // 下面再接直角的进度行，交界处露出两个缺口。层级改靠留白 + 一条分隔线，框线由面板描边一圈画完。
            GameObject rail = ZombieModeUIHelper.CreateRect(
                "TitleRail",
                header.transform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(PanelSidePadding, 0f),
                new Vector2(3f, 24f),
                new Vector2(0f, 0.5f));
            Image railImage = rail.AddComponent<Image>();
            railImage.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
            railImage.raycastTarget = false;

            _titleText = ZombieModeUIHelper.CreateText(
                "Title",
                header.transform,
                L10n.T("鸭皇图鉴", "Duckov Codex"),
                TitleFontSize,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero,
                TextAlignmentOptions.Left,
                BossRushUIColors.TextPrimary);
            _titleText.fontStyle = FontStyles.Bold;
            // 与下面「已解锁 X / Y」同一条左边线（旧写法 sizeDelta 居中收缩，标题比正文往右缩进了 60px）
            _titleText.rectTransform.offsetMin = new Vector2(PanelSidePadding + 10f, 0f);
            _titleText.rectTransform.offsetMax = new Vector2(-64f, 0f);

            // 关闭按钮：幽灵按钮，常态只有一个「×」，悬停才显出 Danger 底（审美审查 UD-32）。
            // 建的时候就传透明底色：先传实色的话共享层会先挂上投影与斜面，改色后也摘不掉。
            Color ghost = new Color(BossRushUIColors.Danger.r, BossRushUIColors.Danger.g, BossRushUIColors.Danger.b, 0f);
            Button closeButton = ZombieModeUIHelper.CreateButton(
                "CloseButton",
                header.transform,
                "×",
                new Vector2(1f, 0.5f),
                new Vector2(-14f, 0f),
                new Vector2(36f, 36f),
                ghost,
                24f,
                new Vector2(36f, 36f),
                Close,
                true);
            IntegrationUIFeedback.StyleGhostCloseButton(
                closeButton, closeButton.GetComponentInChildren<TextMeshProUGUI>(true));

            GameObject divider = ZombieModeUIHelper.CreateSeparator(
                "HeaderDivider",
                _panelRoot.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -HeaderHeight),
                2f,
                BossRushUIColors.Divider);
            InsetDivider(divider);
        }

        /// <summary>分隔线左右各让 12：满宽的线头会顶到面板的圆角描边上。</summary>
        private static void InsetDivider(GameObject divider)
        {
            if (divider == null) return;
            RectTransform rect = divider.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(-24f, rect.sizeDelta.y);
            }
        }

        /// <summary>
        /// 进度条：左侧「已解锁 X / 总数 Y」文本，右侧填充条 + 里程碑刻度。
        /// 刻度只画目录容得下的那几档（总数 8 的池子上画 20 的刻度毫无意义）。
        /// </summary>
        private void CreateProgressBar()
        {
            GameObject row = ZombieModeUIHelper.CreateRect(
                "ProgressRow",
                _panelRoot.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -HeaderHeight),
                new Vector2(0f, ProgressHeight),
                new Vector2(0.5f, 1f));

            // 进度行同样不铺底色：旧的全宽直角 SurfaceRaised 条在圆角面板里是一块「盒子套盒子」（UD-01），
            // 与网格之间用分隔线隔开。
            _progressText = ZombieModeUIHelper.CreateText(
                "ProgressText",
                row.transform,
                string.Empty,
                NameFontSize,
                new Vector2(0f, 0.42f),
                new Vector2(0.34f, 1f),
                new Vector2(PanelSidePadding, 0f),
                Vector2.zero,
                TextAlignmentOptions.Left,
                BossRushUIColors.TextSecondary);

            GameObject track = ZombieModeUIHelper.CreateRect(
                "ProgressTrack",
                row.transform,
                new Vector2(0.36f, 0.61f),
                new Vector2(1f, 0.79f),
                new Vector2(-PanelSidePadding * 0.5f, 0f),
                new Vector2(-PanelSidePadding * 1.5f, 0f),
                new Vector2(0.5f, 0.5f));
            _progressTrack = track.GetComponent<RectTransform>();

            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Disabled;
            BossRushUI.ApplyPanelSkin(trackImage, 4, BossRushUISkinPart.ScrollHandle);
            trackImage.raycastTarget = false;

            GameObject fill = ZombieModeUIHelper.CreateRect(
                "ProgressFill",
                track.transform,
                Vector2.zero,
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero,
                new Vector2(0f, 0.5f));
            _progressFill = fill.AddComponent<Image>();
            _progressFill.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(_progressFill, 4, BossRushUISkinPart.ScrollHandle);
            _progressFill.raycastTarget = false;

            // 筛选是两个视图之间切换，做成分段（A-18）而不是一颗改文案的开关按钮：
            // 旧版按钮上写的是「点了会怎样」，看不出现在是哪种视图。
            _filterAllButton = CreateNavigationButton("FilterAll", row.transform, new Vector2(0f, 0f),
                new Vector2(PanelSidePadding + 44f, 22f), new Vector2(88f, FilterSegmentHeight), ShowAllEntries);
            _filterMissingButton = CreateNavigationButton("FilterMissing", row.transform, new Vector2(0f, 0f),
                new Vector2(PanelSidePadding + 88f + 6f + 64f, 22f), new Vector2(128f, FilterSegmentHeight), ShowMissingEntries);
            // 状态注脚从分段右侧（左边距 246）排到进度条右沿
            _statusText = ZombieModeUIHelper.CreateText("Status", row.transform, string.Empty, NoteFontSize,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(113f, 22f),
                new Vector2(-266f, 34f), TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            _statusText.raycastTarget = false;

            GameObject divider = ZombieModeUIHelper.CreateSeparator(
                "ProgressDivider",
                _panelRoot.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(HeaderHeight + ProgressHeight)),
                2f,
                BossRushUIColors.Divider);
            InsetDivider(divider);
        }

        private void CreateScrollArea()
        {
            ScrollRect prefab = GetScrollRectPrefab();
            GameObject scrollViewObj;

            if (prefab != null)
            {
                _scrollRect = Instantiate(prefab, _panelRoot.transform);
                scrollViewObj = _scrollRect.gameObject;
                scrollViewObj.name = "CodexScrollView";
            }
            else
            {
                scrollViewObj = new GameObject("CodexScrollView");
                scrollViewObj.transform.SetParent(_panelRoot.transform, false);
                _scrollRect = scrollViewObj.AddComponent<ScrollRect>();

                GameObject viewport = new GameObject("Viewport");
                viewport.transform.SetParent(scrollViewObj.transform, false);
                Image viewportImage = viewport.AddComponent<Image>();
                viewportImage.color = BossRushUIColors.Surface;
                Mask mask = viewport.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                RectTransform viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;
                _scrollRect.viewport = viewportRect;

                GameObject content = new GameObject("Content");
                content.transform.SetParent(viewport.transform, false);
                _contentContainer = content.AddComponent<RectTransform>();
                _contentContainer.anchorMin = new Vector2(0f, 1f);
                _contentContainer.anchorMax = new Vector2(1f, 1f);
                _contentContainer.pivot = new Vector2(0.5f, 1f);
                _contentContainer.anchoredPosition = Vector2.zero;
                _scrollRect.content = _contentContainer;
            }

            RectTransform scrollRectTransform = scrollViewObj.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(8f, FooterHeight);
            scrollRectTransform.offsetMax = new Vector2(-8f, -(HeaderHeight + ProgressHeight + 4f));

            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Elastic;
            // 取消分页之后整册都在这一条滚动里，滚轮灵敏度必须按「一格滚过多少行卡片」来定：
            // 旧值 28 在 210px 高的卡片上一下就翻过大半屏（owner 2026-09-20 实测「轻轻一滚就好远」）。
            _scrollRect.scrollSensitivity = ScrollSensitivity;

            if (prefab != null)
            {
                _contentContainer = _scrollRect.content;
                if (_contentContainer == null)
                {
                    GameObject content = new GameObject("Content");
                    content.transform.SetParent(
                        _scrollRect.viewport != null ? _scrollRect.viewport : scrollViewObj.transform,
                        false);
                    _contentContainer = content.AddComponent<RectTransform>();
                    _contentContainer.anchorMin = new Vector2(0f, 1f);
                    _contentContainer.anchorMax = new Vector2(1f, 1f);
                    _contentContainer.pivot = new Vector2(0.5f, 1f);
                    _scrollRect.content = _contentContainer;
                }
            }

            EnsureGridLayout(_contentContainer);
        }

        /// <summary>给内容容器挂网格布局与自适应高度（幂等，官方 prefab 自带时不重挂）。</summary>
        private void EnsureGridLayout(RectTransform content)
        {
            if (content == null) return;

            // 官方 ScrollRect prefab 自带的是竖排布局，图鉴要网格，必须先摘掉
            VerticalLayoutGroup vertical = content.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
            {
                // LayoutGroup 禁止同物体并存；延迟 Destroy 会令本帧 AddComponent 失败。
                DestroyImmediate(vertical);
            }

            GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                grid = content.gameObject.AddComponent<GridLayoutGroup>();
            }
            grid.cellSize = new Vector2(CodexTuning.CardWidth, CodexTuning.CardHeight);
            grid.spacing = new Vector2(CodexTuning.CardSpacing, CodexTuning.CardSpacing);
            grid.padding = new RectOffset((int)GridPadding, (int)GridPadding, (int)GridPadding, (int)GridPadding);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = CodexTuning.GridColumns > 0 ? CodexTuning.GridColumns : 4;

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>小号次级按钮（筛选按钮复用）。走全 Mod 的次级按钮口径：SurfaceRaised 底 + Stroke 描边。</summary>
        private Button CreateNavigationButton(string name, Transform parent, Vector2 anchor,
            Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            Button button = ZombieModeUIHelper.CreateButton(name, parent, string.Empty, anchor, position, size,
                BossRushUIColors.SurfaceRaised, BodyFontSize, size, action, true);
            BossRushUIKit.StyleSecondaryButton(button);
            return button;
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null) return;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = value;
        }

        private static ScrollRect GetScrollRectPrefab()
        {
            try
            {
                return Duckov.Utilities.GameplayDataSettings.UIPrefabs.ScrollRect;
            }
            catch (Exception)
            {
                // 官方 prefab 取不到就走手写回退，不 fail
                return null;
            }
        }

        #endregion

        #region 进度

        /// <summary>刷新「已解锁 X / 总数 Y」与进度条填充、里程碑刻度。</summary>
        private void UpdateProgress(CodexData data)
        {
            int unlocked = data != null ? data.UnlockedCount : 0;
            int total = CodexBossCatalog.Count;

            if (_progressText != null)
            {
                _progressText.text = L10n.T("已解锁 ", "Unlocked ")
                    + unlocked.ToString()
                    + " / "
                    + total.ToString();
            }

            if (_progressFill != null)
            {
                float ratio = total > 0 ? Mathf.Clamp01((float)unlocked / total) : 0f;
                RectTransform fillRect = _progressFill.rectTransform;
                fillRect.anchorMax = new Vector2(ratio, 1f);
                _progressFill.color = ratio >= 1f && total > 0
                    ? BossRushUIColors.Success
                    : BossRushUIColors.Accent;
            }

            RebuildMilestoneTicks(unlocked, total);
            if (_statusText != null)
            {
                _statusText.text = CodexPersistence.HasWriteBarrier || CodexPersistence.IsStoreFaulted
                    ? L10n.T("记录暂不可保存，请重载后查看；原存档受保护。",
                        "Recording unavailable. Reload to check; your saved data is protected.")
                    : L10n.T("击杀自动记录 · 1 / 10 / 20 条与全收集解锁成就",
                        "Kills log automatically · Achievements at 1 / 10 / 20 entries and completion");
            }
            SetButtonLabel(_filterAllButton, L10n.T("全部", "All"));
            SetButtonLabel(_filterMissingButton, string.Format(L10n.T("待收集 · {0}", "Missing · {0}"),
                Mathf.Max(0, total - unlocked)));
            // 选中态：Accent 淡底 + WarningText 描边 + 粗体（共享口径，与 Boss 池页签同一份）
            IntegrationUIFeedback.StyleSegment(_filterAllButton, !_onlyMissing);
            IntegrationUIFeedback.StyleSegment(_filterMissingButton, _onlyMissing);
        }

        /// <summary>
        /// 重画里程碑刻度。刻度数量固定（最多 3 个），因此每次全拆重建的代价可忽略；
        /// 但它只在 RefreshAll 里被调用，绝不进 Update。
        /// </summary>
        private void RebuildMilestoneTicks(int unlocked, int total)
        {
            if (_progressTrack == null || total <= 0) return;

            // 旧刻度整批销毁：刻度是 track 的直接子节点里名字带前缀的那些
            for (int i = _progressTrack.childCount - 1; i >= 0; i--)
            {
                Transform child = _progressTrack.GetChild(i);
                if (child == null) continue;
                if (child.name != null && child.name.StartsWith("Tick_", StringComparison.Ordinal))
                {
                    Destroy(child.gameObject);
                }
            }

            AddMilestoneTick(CodexTuning.MilestoneTenThreshold, unlocked, total);
            AddMilestoneTick(CodexTuning.MilestoneTwentyThreshold, unlocked, total);
            if (total != CodexTuning.MilestoneTenThreshold && total != CodexTuning.MilestoneTwentyThreshold)
                AddMilestoneTick(total, unlocked, total);
        }

        private void AddMilestoneTick(int threshold, int unlocked, int total)
        {
            if (threshold <= 0 || total <= 0 || threshold > total) return;

            float ratio = Mathf.Clamp01((float)threshold / total);
            GameObject tick = ZombieModeUIHelper.CreateRect(
                "Tick_" + threshold.ToString(),
                _progressTrack,
                new Vector2(ratio, 0f),
                new Vector2(ratio, 1f),
                Vector2.zero,
                new Vector2(2f, 0f),
                new Vector2(0.5f, 0.5f));

            Image tickImage = tick.AddComponent<Image>();
            // 已达成的刻度点亮成 SuccessText（压在 Accent 填充上也分得清），未达成用 Stroke——
            // 旧的 Divider(α0.32) 压在 Disabled 轨道上几乎看不见。
            tickImage.color = unlocked >= threshold
                ? BossRushUIColors.SuccessText
                : BossRushUIColors.Stroke;
            // 2px 细条走细条档：没有 sprite 的裸 quad 在非整数画布缩放下时有时无（审美审查 UD-33）
            BossRushUI.ApplyPanelSkin(tickImage, 1, BossRushUISkinPart.Hairline);
            tickImage.raycastTarget = false;
        }

        #endregion
    }
}

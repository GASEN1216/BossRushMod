// ============================================================================
// CodexView.cs - 鸭皇图鉴主面板（骨架 / 生命周期 / 头部 / 进度条 / 滚动容器）
// ============================================================================
// 骨架照 Achievement/AchievementView.cs（单例 MonoBehaviour + DontDestroyOnLoad +
// 自建 Canvas + 官方 ScrollRect prefab 优先），网格卡片与详情弹层在同一个 partial
// 的续篇 CodexView_Grid.cs 里。
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
//   - 面板**不得每帧重建**。RefreshAll() 只在 Open() 与显式刷新时调；
//     Update() 里只处理 Escape，禁止任何目录扫描或字符串拼接。
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
        private const float ProgressHeight = 54f;
        private const float FooterHeight = 34f;
        private const float PanelSidePadding = 20f;
        private const float GridPadding = 12f;

        private const float PanelHeightRatio = 0.82f;
        private const float MinPanelHeight = 560f;
        private const float MaxPanelHeight = 880f;

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

        /// <summary>静态缓存重置：销毁实例并清引用（宿主销毁 / Mod 卸载）。</summary>
        public static void ResetStaticCaches()
        {
            try
            {
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
        private GameObject _panelRoot;
        private ScrollRect _scrollRect;
        private RectTransform _contentContainer;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _progressText;
        private Image _progressFill;
        private RectTransform _progressTrack;

        private float _panelWidth;
        private float _panelHeight;

        #endregion

        #region 状态

        private bool _isOpen;
        private bool _uiBuilt;

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
            try
            {
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

        /// <summary>只处理 Escape 关闭。禁止在这里做任何目录扫描或字符串拼接。</summary>
        private void Update()
        {
            if (!_isOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // 详情弹层开着时 Escape 先收详情，再按一次才关面板
                if (IsDetailOpen)
                {
                    HideDetail();
                }
                else
                {
                    Close();
                }
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
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
            }

            try
            {
                InputManager.DisableInput(gameObject);
            }
            catch (Exception)
            {
                // 输入占用失败不阻断呈现
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

            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }

            try
            {
                InputManager.ActiveInput(gameObject);
            }
            catch (Exception)
            {
                // 输入释放失败不阻断关闭
            }

            if (wasOpen)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "图鉴面板已关闭");
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

            float screenHeight = Screen.height;
            _panelHeight = Mathf.Clamp(screenHeight * PanelHeightRatio, MinPanelHeight, MaxPanelHeight);
        }

        private void CreateUI()
        {
            Canvas canvas = BossRushUI.CreateCanvasRoot("CodexCanvas", BossRushUILayers.Panel, true);
            canvas.transform.SetParent(transform, false);
            _canvas = canvas;

            Image backdrop = BossRushUI.CreateBackdrop(canvas.transform);
            Button backdropButton = backdrop.gameObject.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.onClick.AddListener(Close);

            CreateMainPanel(canvas.transform);
            CreateHeader();
            CreateProgressBar();
            CreateScrollArea();
            CreateFooterHint();
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
            BossRushUI.ApplyPanelSkin(panelImage, 14);

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

            Image headerImage = header.AddComponent<Image>();
            headerImage.color = BossRushUIColors.Header;
            BossRushUI.ApplyPanelSkin(headerImage, 12);
            headerImage.raycastTarget = false;

            _titleText = ZombieModeUIHelper.CreateText(
                "Title",
                header.transform,
                L10n.T("鸭皇图鉴", "Duckov Codex"),
                26f,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(PanelSidePadding, 0f),
                new Vector2(-120f, 0f),
                TextAlignmentOptions.Left,
                BossRushUIColors.TextPrimary);
            _titleText.fontStyle = FontStyles.Bold;

            // 关闭按钮走共享库，颜色用 Danger token
            Button closeButton = ZombieModeUIHelper.CreateButton(
                "CloseButton",
                header.transform,
                "×",
                new Vector2(1f, 0.5f),
                new Vector2(-14f, 0f),
                new Vector2(36f, 36f),
                BossRushUIColors.Danger,
                22f,
                new Vector2(36f, 36f),
                Close,
                true);
            ZombieModeUIHelper.ApplyButtonColors(
                closeButton,
                BossRushUIColors.Danger,
                Color.Lerp(BossRushUIColors.Danger, Color.white, 0.22f),
                BossRushUIColors.Disabled);
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

            Image rowImage = row.AddComponent<Image>();
            rowImage.color = BossRushUIColors.SurfaceRaised;
            rowImage.raycastTarget = false;

            _progressText = ZombieModeUIHelper.CreateText(
                "ProgressText",
                row.transform,
                string.Empty,
                15f,
                new Vector2(0f, 0f),
                new Vector2(0.34f, 1f),
                new Vector2(PanelSidePadding, 0f),
                Vector2.zero,
                TextAlignmentOptions.Left,
                BossRushUIColors.TextSecondary);

            GameObject track = ZombieModeUIHelper.CreateRect(
                "ProgressTrack",
                row.transform,
                new Vector2(0.36f, 0.34f),
                new Vector2(1f, 0.66f),
                new Vector2(-PanelSidePadding * 0.5f, 0f),
                new Vector2(-PanelSidePadding * 1.5f, 0f),
                new Vector2(0.5f, 0.5f));
            _progressTrack = track.GetComponent<RectTransform>();

            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Disabled;
            BossRushUI.ApplyPanelSkin(trackImage, 4);
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
            BossRushUI.ApplyPanelSkin(_progressFill, 4);
            _progressFill.raycastTarget = false;
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
            _scrollRect.scrollSensitivity = 28f;

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
                Destroy(vertical);
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

        private void CreateFooterHint()
        {
            TextMeshProUGUI hint = ZombieModeUIHelper.CreateText(
                "FooterHint",
                _panelRoot.transform,
                L10n.T("点击卡片查看详情 · ESC 关闭", "Click a card for details · ESC to close"),
                13f,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, FooterHeight * 0.5f),
                new Vector2(0f, FooterHeight),
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);
            hint.raycastTarget = false;
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
            // 已达成的刻度点亮成 Success，未达成留 Divider
            tickImage.color = unlocked >= threshold
                ? BossRushUIColors.Success
                : BossRushUIColors.Divider;
            tickImage.raycastTarget = false;
        }

        #endregion
    }
}

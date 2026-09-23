// ============================================================================
// AchievementView.cs - 成就页面主视图
// ============================================================================
// 模块说明：
//   成就页面的主容器，参考BossFilter的实现方式
//   支持 L 键打开/关闭，ESC 键关闭
//   包含成就列表、统计信息、一键领取功能
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Duckov.UI;
using TMPro;

namespace BossRush
{
    /// <summary>
    /// 成就页面主视图 - 使用VerticalLayoutGroup自动布局
    /// </summary>
    public class AchievementView : MonoBehaviour
    {
        #region 布局常量

        private const float PanelWidthRatio = 0.4f;
        private const float PanelHeightRatio = 0.75f;
        private const float MinPanelWidth = 500f;
        private const float MaxPanelWidth = 750f;
        private const float MinPanelHeight = 500f;
        private const float MaxPanelHeight = 850f;
        private const float HeaderHeight = 55f;
        private const float FooterHeight = 55f;
        private const float StatsHeight = 35f;

        // 配色只用共享 token（审美审查 UD-34）：旧版自建了一套 Color32 常量，标题是纯金 (255,215,0)，
        // 页头 / 统计栏 / 页脚各铺一条全宽直角底色，压在圆角面板上四角露出直角、顶边底边的框线也被盖掉（UD-01）。
        // 现在三条都不铺底色，层级靠留白 + 分隔线；标题白字，前面一根传说金竖条（成就 = 金）。
        private static readonly Color PanelBgColor = BossRushUIColors.Surface;
        private static readonly Color TitleColor = BossRushUIColors.TextPrimary;
        private static readonly Color TitleRailColor = BossRushUIColors.RarityLegendary;
        private static readonly Color StatsColor = BossRushUIColors.TextSecondary;
        private static readonly Color ProgressBarBgColor = BossRushUIColors.Disabled;
        private static readonly Color ProgressBarFillColor = BossRushUIColors.Accent;
        /// <summary>一键领取是这一屏唯一的主操作：AccentFill 底（全 Mod 按钮口径 2026-09-23）。只用在官方按钮 prefab 取不到时的回退按钮上。</summary>
        private static readonly Color ClaimAllFallbackColor = BossRushUIColors.AccentFill;
        private const float SideInset = 20f;

        #endregion

        #region 单例

        private static AchievementView _instance;
        public static AchievementView Instance
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

        #endregion

        #region UI组件引用

        private Canvas canvas;
        /// <summary>画布根上的 CanvasGroup：关闭时遮罩与面板一起淡出，淡完才 SetActive(false)。</summary>
        private CanvasGroup canvasGroup;
        private Image backdropImage;
        private Coroutine closeFade;
        /// <summary>销毁路径上 Close 走立即关闭，不在正在销毁的对象上起协程。</summary>
        private bool destroying;
        private GameObject panelRoot;
        private RectTransform panelRect;
        private ScrollRect scrollRect;
        private RectTransform contentContainer;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI statsText;
        private TextMeshProUGUI totalRewardText;
        private Image progressBarFill;
        private Button claimAllButton;
        private TextMeshProUGUI claimAllButtonText;
        private Button exitButton;
        private float calculatedPanelWidth;
        private float calculatedPanelHeight;

        #endregion

        #region 状态字段

        private bool isOpen;
        private List<AchievementEntryUI> entries = new List<AchievementEntryUI>();
        private bool isClaimingAll = false;

        #endregion

        #region 公共属性

        public bool IsOpen => isOpen;

        #endregion

        #region 初始化

        public static void EnsureInstance()
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("AchievementView");
                _instance = obj.AddComponent<AchievementView>();
                DontDestroyOnLoad(obj);
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            CalculatePanelSize();
            CreateUI();
            Close();
        }

        private void CalculatePanelSize()
        {
            Vector2 viewport = ZombieModeUIHelper.GetReferenceViewportSize();
            float screenWidth = viewport.x;
            float screenHeight = viewport.y;
            
            calculatedPanelWidth = Mathf.Clamp(screenWidth * PanelWidthRatio, MinPanelWidth, MaxPanelWidth);
            calculatedPanelHeight = Mathf.Clamp(screenHeight * PanelHeightRatio, MinPanelHeight, MaxPanelHeight);
        }

        void OnDestroy()
        {
            destroying = true;
            Close();
            foreach (AchievementEntryUI entry in entries) if (entry != null) entry.Cleanup();
            entries.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        internal static void Shutdown()
        {
            if (_instance == null) return;
            _instance.destroying = true;
            _instance.Close();
            _instance.gameObject.SetActive(false);
            Destroy(_instance.gameObject);
        }

        /// <summary>
        /// 创建UI - 参考BossFilter的实现
        /// </summary>
        private void CreateUI()
        {
            // 创建 Canvas
            GameObject canvasObj = new GameObject("AchievementCanvas");
            canvasObj.transform.SetParent(transform);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.Panel;

            // 原本 sortingOrder=10 会被任何模态压住；Scaler 也没设 match，
            // 默认只按宽度缩放，超宽屏上面板会溢出。
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

            canvasObj.AddComponent<GraphicRaycaster>();
            canvasGroup = canvasObj.AddComponent<CanvasGroup>();

            // 创建半透明背景（共享遮罩：Backdrop token + 暗角 + 0.15 秒淡入，审美审查 UD-04 / UD-10）
            Image bgImage = BossRushUI.CreateBackdrop(canvasObj.transform);
            GameObject bgObj = bgImage.gameObject;
            backdropImage = bgImage;

            Button bgButton = bgObj.AddComponent<Button>();
            bgButton.transition = Selectable.Transition.None;
            bgButton.onClick.AddListener(Close);

            // 创建主面板
            CreateMainPanel(canvasObj.transform);

            // 创建头部
            CreateHeader();

            // 创建统计栏
            CreateStatsBar();

            // 创建滚动区域
            CreateScrollArea();

            // 创建底部
            CreateFooter();
        }

        /// <summary>
        /// 创建主面板
        /// </summary>
        private void CreateMainPanel(Transform parent)
        {
            panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(parent, false);

            panelRect = panelRoot.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(calculatedPanelWidth, calculatedPanelHeight);

            Image panelImage = panelRoot.AddComponent<Image>();
            // 先上色再套皮：投影 / 斜面按底色不透明度决定挂不挂
            panelImage.color = PanelBgColor;
            BossRushUI.ApplyFramedPanelSkin(panelImage, 14, BossRushUISkinPart.Panel);

            Button panelButton = panelRoot.AddComponent<Button>();
            panelButton.transition = Selectable.Transition.None;
        }

        /// <summary>
        /// 创建头部区域
        /// </summary>
        private void CreateHeader()
        {
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(panelRoot.transform, false);

            RectTransform headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, HeaderHeight);

            // 页头不铺底色（UD-01），标题前一根传说金竖条
            GameObject railObj = ZombieModeUIHelper.CreateRect(
                "TitleRail",
                headerObj.transform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(SideInset, 0f),
                new Vector2(3f, 24f),
                new Vector2(0f, 0.5f));
            Image railImage = railObj.AddComponent<Image>();
            railImage.color = TitleRailColor;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
            railImage.raycastTarget = false;

            // 标题
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(headerObj.transform, false);

            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(SideInset + 12f, 0f);
            titleRect.offsetMax = new Vector2(-56f, 0f);

            titleText = titleObj.AddComponent<TextMeshProUGUI>();

            BossRushUI.ApplyGameFont(titleText);
            titleText.fontSize = 28;
            titleText.enableWordWrapping = false;
            titleText.overflowMode = TextOverflowModes.Ellipsis;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = TitleColor;
            titleText.alignment = TextAlignmentOptions.Left;
            titleText.raycastTarget = false;

            // 关闭按钮
            Button buttonPrefab = GetButtonPrefab();
            if (buttonPrefab != null)
            {
                Button closeBtn = UnityEngine.Object.Instantiate(buttonPrefab, headerObj.transform);
                RectTransform closeBtnRect = closeBtn.GetComponent<RectTransform>();
                closeBtnRect.anchorMin = new Vector2(1f, 0.5f);
                closeBtnRect.anchorMax = new Vector2(1f, 0.5f);
                closeBtnRect.pivot = new Vector2(1f, 0.5f);
                closeBtnRect.anchoredPosition = new Vector2(-10f, 0f);
                closeBtnRect.sizeDelta = new Vector2(35f, 35f);

                TextMeshProUGUI btnText = closeBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = "×";
                    btnText.fontSize = 20;
                }

                exitButton = closeBtn;
                exitButton.onClick.AddListener(Close);
            }
            else
            {
                // 回退：共享按钮 + 幽灵关闭样式（常态透明、悬停显出 Danger 底，审美审查 UD-32）。
                // 旧回退是手搓的 35px 平涂暗红块，没走 ApplyButtonColors，悬停几乎看不出、也没有音效。
                Color ghost = new Color(BossRushUIColors.Danger.r, BossRushUIColors.Danger.g, BossRushUIColors.Danger.b, 0f);
                exitButton = ZombieModeUIHelper.CreateButton(
                    "CloseButton",
                    headerObj.transform,
                    "×",
                    new Vector2(1f, 0.5f),
                    new Vector2(-10f - 17.5f, 0f),
                    new Vector2(35f, 35f),
                    ghost,
                    24f,
                    new Vector2(35f, 35f),
                    Close,
                    true);
                IntegrationUIFeedback.StyleGhostCloseButton(
                    exitButton, exitButton.GetComponentInChildren<TextMeshProUGUI>(true));
            }

            GameObject divider = ZombieModeUIHelper.CreateSeparator(
                "HeaderDivider",
                panelRoot.transform,
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
        /// 获取按钮预制件
        /// </summary>
        private Button GetButtonPrefab()
        {
            try
            {
                return Duckov.Utilities.GameplayDataSettings.UIPrefabs.Button;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 创建统计栏（包含进度条）
        /// </summary>
        private void CreateStatsBar()
        {
            GameObject statsObj = new GameObject("StatsBar");
            statsObj.transform.SetParent(panelRoot.transform, false);

            RectTransform statsRect = statsObj.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0f, 1f);
            statsRect.anchorMax = new Vector2(1f, 1f);
            statsRect.pivot = new Vector2(0.5f, 1f);
            statsRect.anchoredPosition = new Vector2(0f, -HeaderHeight);
            statsRect.sizeDelta = new Vector2(0f, StatsHeight);

            // 统计栏不铺底色（UD-01），与列表之间一条分隔线

            // 统计文本（左侧）
            GameObject statsTextObj = new GameObject("StatsText");
            statsTextObj.transform.SetParent(statsObj.transform, false);

            RectTransform statsTextRect = statsTextObj.AddComponent<RectTransform>();
            statsTextRect.anchorMin = new Vector2(0f, 0f);
            statsTextRect.anchorMax = new Vector2(0.35f, 1f);
            statsTextRect.offsetMin = new Vector2(SideInset, 0f);
            statsTextRect.offsetMax = Vector2.zero;

            statsText = statsTextObj.AddComponent<TextMeshProUGUI>();

            BossRushUI.ApplyGameFont(statsText);
            statsText.fontSize = 15;
            statsText.enableWordWrapping = false;
            statsText.overflowMode = TextOverflowModes.Ellipsis;
            statsText.color = StatsColor;
            statsText.alignment = TextAlignmentOptions.Left;
            statsText.raycastTarget = false;

            // 进度条背景
            GameObject progressBgObj = new GameObject("ProgressBarBg");
            progressBgObj.transform.SetParent(statsObj.transform, false);

            RectTransform progressBgRect = progressBgObj.AddComponent<RectTransform>();
            progressBgRect.anchorMin = new Vector2(0.38f, 0.3f);
            progressBgRect.anchorMax = new Vector2(0.95f, 0.7f);
            progressBgRect.offsetMin = Vector2.zero;
            progressBgRect.offsetMax = Vector2.zero;

            Image progressBgImage = progressBgObj.AddComponent<Image>();
            BossRushUI.ApplyPanelSkin(progressBgImage, 4, BossRushUISkinPart.ScrollHandle);
            progressBgImage.color = ProgressBarBgColor;

            // 进度条填充
            GameObject progressFillObj = new GameObject("ProgressBarFill");
            progressFillObj.transform.SetParent(progressBgObj.transform, false);

            RectTransform progressFillRect = progressFillObj.AddComponent<RectTransform>();
            progressFillRect.anchorMin = Vector2.zero;
            progressFillRect.anchorMax = new Vector2(0f, 1f);
            progressFillRect.pivot = new Vector2(0f, 0.5f);
            progressFillRect.offsetMin = Vector2.zero;
            progressFillRect.offsetMax = Vector2.zero;

            progressBarFill = progressFillObj.AddComponent<Image>();
            BossRushUI.ApplyPanelSkin(progressBarFill, 4, BossRushUISkinPart.ScrollHandle);
            progressBarFill.color = ProgressBarFillColor;

            GameObject divider = ZombieModeUIHelper.CreateSeparator(
                "StatsDivider",
                panelRoot.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -(HeaderHeight + StatsHeight)),
                2f,
                BossRushUIColors.Divider);
            InsetDivider(divider);
        }

        /// <summary>
        /// 创建滚动区域 - 参考BossFilter的实现
        /// </summary>
        private void CreateScrollArea()
        {
            // 尝试使用官方 ScrollRect prefab
            ScrollRect scrollRectPrefab = GetScrollRectPrefab();

            GameObject scrollViewObj;

            if (scrollRectPrefab != null)
            {
                scrollRect = UnityEngine.Object.Instantiate(scrollRectPrefab, panelRoot.transform);
                scrollViewObj = scrollRect.gameObject;
                scrollViewObj.name = "AchievementScrollView";
            }
            else
            {
                // 手动创建
                scrollViewObj = new GameObject("AchievementScrollView");
                scrollViewObj.transform.SetParent(panelRoot.transform, false);
                scrollRect = scrollViewObj.AddComponent<ScrollRect>();

                // 创建 Viewport
                GameObject viewport = new GameObject("Viewport");
                viewport.transform.SetParent(scrollViewObj.transform, false);
                Image vpImage = viewport.AddComponent<Image>();
                vpImage.color = BossRushUIColors.Surface;   // 只作遮罩形状（showMaskGraphic=false），不显示
                Mask mask = viewport.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                RectTransform vpRect = viewport.GetComponent<RectTransform>();
                vpRect.anchorMin = Vector2.zero;
                vpRect.anchorMax = Vector2.one;
                vpRect.offsetMin = Vector2.zero;
                vpRect.offsetMax = Vector2.zero;
                scrollRect.viewport = vpRect;

                // 创建 Content
                GameObject contentObj = new GameObject("Content");
                contentObj.transform.SetParent(viewport.transform, false);
                contentContainer = contentObj.AddComponent<RectTransform>();
                contentContainer.anchorMin = new Vector2(0f, 1f);
                contentContainer.anchorMax = new Vector2(1f, 1f);
                contentContainer.pivot = new Vector2(0.5f, 1f);
                contentContainer.anchoredPosition = Vector2.zero;

                VerticalLayoutGroup vlg = contentObj.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 8f;
                vlg.padding = new RectOffset(10, 10, 10, 10);
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;

                ContentSizeFitter csf = contentObj.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scrollRect.content = contentContainer;
            }

            // 设置 ScrollRect 位置和大小（考虑统计栏高度）
            RectTransform scrollRectTransform = scrollViewObj.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(8f, FooterHeight + 5f);
            scrollRectTransform.offsetMax = new Vector2(-8f, -(HeaderHeight + StatsHeight + 5f));

            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;

            // 如果使用了官方 prefab，获取 Content
            if (scrollRectPrefab != null)
            {
                contentContainer = scrollRect.content;
                if (contentContainer == null)
                {
                    GameObject contentObj = new GameObject("Content");
                    contentObj.transform.SetParent(scrollRect.viewport != null ? scrollRect.viewport : scrollViewObj.transform, false);
                    contentContainer = contentObj.AddComponent<RectTransform>();
                    contentContainer.anchorMin = new Vector2(0f, 1f);
                    contentContainer.anchorMax = new Vector2(1f, 1f);
                    contentContainer.pivot = new Vector2(0.5f, 1f);
                    scrollRect.content = contentContainer;
                }

                // 确保有布局组件
                if (contentContainer.GetComponent<VerticalLayoutGroup>() == null)
                {
                    VerticalLayoutGroup vlg = contentContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                    vlg.spacing = 8f;
                    vlg.padding = new RectOffset(10, 10, 10, 10);
                    vlg.childAlignment = TextAnchor.UpperLeft;
                    vlg.childForceExpandWidth = true;
                    vlg.childForceExpandHeight = false;
                    vlg.childControlWidth = true;
                    vlg.childControlHeight = false;
                }

                if (contentContainer.GetComponent<ContentSizeFitter>() == null)
                {
                    ContentSizeFitter csf = contentContainer.gameObject.AddComponent<ContentSizeFitter>();
                    csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
            }
        }

        /// <summary>
        /// 获取ScrollRect预制件
        /// </summary>
        private ScrollRect GetScrollRectPrefab()
        {
            try
            {
                return Duckov.Utilities.GameplayDataSettings.UIPrefabs.ScrollRect;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 创建底部区域
        /// </summary>
        private void CreateFooter()
        {
            GameObject footerObj = new GameObject("Footer");
            footerObj.transform.SetParent(panelRoot.transform, false);

            RectTransform footerRect = footerObj.AddComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = Vector2.zero;
            footerRect.sizeDelta = new Vector2(0f, FooterHeight);

            // 页脚不铺底色（UD-01），与列表之间一条分隔线
            GameObject divider = ZombieModeUIHelper.CreateSeparator(
                "FooterDivider",
                panelRoot.transform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, FooterHeight),
                2f,
                BossRushUIColors.Divider);
            InsetDivider(divider);

            // 已领取奖励总额
            GameObject totalObj = new GameObject("TotalReward");
            totalObj.transform.SetParent(footerObj.transform, false);

            RectTransform totalRect = totalObj.AddComponent<RectTransform>();
            totalRect.anchorMin = new Vector2(0f, 0f);
            totalRect.anchorMax = new Vector2(0.6f, 1f);
            totalRect.offsetMin = new Vector2(SideInset, 0f);
            totalRect.offsetMax = Vector2.zero;

            totalRewardText = totalObj.AddComponent<TextMeshProUGUI>();

            BossRushUI.ApplyGameFont(totalRewardText);
            totalRewardText.fontSize = 16;
            totalRewardText.enableWordWrapping = false;
            totalRewardText.overflowMode = TextOverflowModes.Ellipsis;
            totalRewardText.color = BossRushUIColors.WarningText;   // 金钱 = WarningText（审美口径第 2 条），不再用纯金 (255,215,0)
            totalRewardText.alignment = TextAlignmentOptions.Left;
            totalRewardText.raycastTarget = false;

            // 一键领取按钮
            Button buttonPrefab = GetButtonPrefab();
            if (buttonPrefab != null)
            {
                Button claimBtn = UnityEngine.Object.Instantiate(buttonPrefab, footerObj.transform);
                RectTransform claimBtnRect = claimBtn.GetComponent<RectTransform>();
                claimBtnRect.anchorMin = new Vector2(1f, 0.5f);
                claimBtnRect.anchorMax = new Vector2(1f, 0.5f);
                claimBtnRect.pivot = new Vector2(1f, 0.5f);
                claimBtnRect.anchoredPosition = new Vector2(-15f, 0f);
                // 放得下「一键领取 (12)」/「Claim All (12)」：可领数量写进按钮（UD-36）
                claimBtnRect.sizeDelta = new Vector2(156f, 40f);

                TextMeshProUGUI btnText = claimBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    claimAllButtonText = btnText;
                }

                claimAllButton = claimBtn;
                claimAllButton.onClick.AddListener(ClaimAllRewards);
            }
            else
            {
                // 回退：共享按钮（三态、音效、按下回弹、投影斜面都由共享层给），主操作 AccentFill 底
                claimAllButton = ZombieModeUIHelper.CreateButton(
                    "ClaimAllButton",
                    footerObj.transform,
                    string.Empty,
                    new Vector2(1f, 0.5f),
                    new Vector2(-15f - 78f, 0f),
                    new Vector2(156f, 40f),
                    ClaimAllFallbackColor,
                    16f,
                    new Vector2(140f, 32f),
                    ClaimAllRewards,
                    true);
                claimAllButtonText = claimAllButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (claimAllButtonText != null)
                {
                    claimAllButtonText.fontStyle = FontStyles.Bold;
                }
            }
        }

        #endregion

        #region 公共方法

        public void Open()
        {
            if (isOpen) return;

            isOpen = true;
            // 上一次关闭的淡出还没播完就又打开：停掉淡出、恢复成完整面板
            StopCloseFade();
            IntegrationUIFeedback.ResetFade(canvasGroup);
            canvas.gameObject.SetActive(true);
            // 遮罩只在建 UI 时淡入过一次；常驻面板每次打开都重播，否则第二次起背景一帧压黑
            if (backdropImage != null)
            {
                BossRushUIEntranceAnimation.Play(backdropImage.gameObject, 0f, 0.15f, 0f);
            }

            try
            {
                InputManager.DisableInput(gameObject);
            }
            catch { }

            BossRushAchievementManager.Initialize();
            RefreshAll();

            // 重置滚动位置
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f;
            }

            // 与图鉴同一口径：面板从中间长出来（审美审查 UD-34，旧版一帧直接出现）
            BossRushUI.PlayOpenAnimation(panelRoot);
            ModBehaviour.DevLog("[AchievementView] 成就页面已打开");
        }

        public void Close()
        {
            if (!isOpen && canvas != null && (!canvas.gameObject.activeSelf || closeFade != null)) return;

            bool wasOpen = isOpen;
            isOpen = false;

            // 先还输入，再播淡出：动效绝不能变成输入延迟
            try
            {
                InputManager.ActiveInput(gameObject);
            }
            catch { }

            if (canvas != null)
            {
                // 玩家关面板时 0.12 秒淡出（打开有长出来、关闭一帧消失，前后不对称，审美审查 UD-03）；构建期与销毁路径直接关
                if (wasOpen && !destroying && isActiveAndEnabled && canvasGroup != null && canvas.gameObject.activeSelf)
                {
                    StopCloseFade();
                    closeFade = StartCoroutine(CloseFadeRoutine());
                }
                else
                {
                    StopCloseFade();
                    canvas.gameObject.SetActive(false);
                    IntegrationUIFeedback.ResetFade(canvasGroup);
                }
            }

            ModBehaviour.DevLog("[AchievementView] 成就页面已关闭");
        }

        private System.Collections.IEnumerator CloseFadeRoutine()
        {
            yield return IntegrationUIFeedback.FadeOutAndDeactivate(canvasGroup, canvas.gameObject, BossRushUIKit.CloseSeconds);
            closeFade = null;
        }

        private void StopCloseFade()
        {
            if (closeFade != null)
            {
                StopCoroutine(closeFade);
                closeFade = null;
            }
        }

        public void Toggle()
        {
            if (isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void RefreshAll()
        {
            UpdateLocalizedTexts();
            PopulateEntries();
            UpdateStats();
            UpdateClaimAllButton();
        }

        public void ClaimAllRewards()
        {
            if (isClaimingAll) return;
            isClaimingAll = true;

            try
            {
                int claimedCount = 0;
                long totalCash = 0;

                List<AchievementEntryUI> claimableEntries = new List<AchievementEntryUI>();
                foreach (var entry in entries)
                {
                    if (entry != null && entry.CanClaim)
                    {
                        claimableEntries.Add(entry);
                    }
                }

                foreach (var entry in claimableEntries)
                {
                    if (entry == null) continue;
                    var achievement = entry.Achievement;
                    if (achievement == null) continue;

                    bool success = BossRushAchievementManager.ClaimReward(achievement.id);
                    if (success)
                    {
                        claimedCount++;
                        totalCash += achievement.reward != null ? achievement.reward.cashReward : 0;
                        entry.Refresh();
                        // 每行照样有「到账」的一拍（金框淡回、奖励数字回弹），音效整批只播一次
                        entry.PlayClaimFeedback();
                    }
                }

                if (claimedCount > 0)
                {
                    IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundSell);
                    string message = string.Format(
                        AchievementUIStrings.GetText(AchievementUIStrings.CN_ClaimedTotal, AchievementUIStrings.EN_ClaimedTotal),
                        totalCash.ToString("N0")
                    );
                    NotificationText.Push(message);
                    ModBehaviour.DevLog("[AchievementView] 一键领取完成: " + claimedCount + " 个成就, $" + totalCash);
                }
                else
                {
                    string message = AchievementUIStrings.GetText(AchievementUIStrings.CN_NoRewards, AchievementUIStrings.EN_NoRewards);
                    NotificationText.Push(message);
                }

                UpdateStats();
                UpdateClaimAllButton();
            }
            finally
            {
                isClaimingAll = false;
            }
        }

        #endregion

        #region 内部方法

        private void UpdateLocalizedTexts()
        {
            titleText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Title, AchievementUIStrings.EN_Title);
            // 一键领取的文字随可领数量刷新，见 UpdateClaimAllButton
        }

        /// <summary>
        /// 填充成就条目 - 使用LayoutElement
        /// 排序规则：已完成的成就排在前面，未完成的排在后面
        /// </summary>
        private void PopulateEntries()
        {
            // 清理现有条目
            foreach (var entry in entries)
            {
                if (entry != null)
                {
                    entry.Cleanup();
                    Destroy(entry.gameObject);
                }
            }
            entries.Clear();

            // 获取所有成就
            var achievements = BossRushAchievementManager.GetAllAchievements();
            if (achievements == null || achievements.Count == 0)
            {
                ModBehaviour.DevLog("[AchievementView] 没有成就数据，尝试重新初始化");
                BossRushAchievementManager.Initialize();
                achievements = BossRushAchievementManager.GetAllAchievements();
            }

            if (achievements == null || achievements.Count == 0)
            {
                ModBehaviour.DevLog("[AchievementView] 仍然没有成就数据");
                return;
            }

            // 排序：已完成的成就排在前面，隐藏成就排在最后，其余按分类和难度排序
            achievements.Sort((a, b) =>
            {
                bool aUnlocked = BossRushAchievementManager.IsUnlocked(a.id);
                bool bUnlocked = BossRushAchievementManager.IsUnlocked(b.id);
                
                // 已完成的排前面
                if (aUnlocked && !bUnlocked) return -1;
                if (!aUnlocked && bUnlocked) return 1;
                
                // 隐藏成就排最后
                if (a.isHidden && !b.isHidden) return 1;
                if (!a.isHidden && b.isHidden) return -1;
                
                // 同为已完成或未完成时，按分类排序
                if (a.category != b.category)
                    return ((int)a.category).CompareTo((int)b.category);
                
                // 同分类时，按难度排序
                return a.difficultyRating.CompareTo(b.difficultyRating);
            });

            ModBehaviour.DevLog("[AchievementView] 准备创建 " + achievements.Count + " 个成就条目（已排序）");

            // 创建条目
            foreach (var achievement in achievements)
            {
                try
                {
                    var entry = AchievementEntryUI.Create(contentContainer, achievement);
                    if (entry != null)
                    {
                        entry.OnRewardClaimed += OnEntryRewardClaimed;
                        entries.Add(entry);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[AchievementView] 创建条目失败: " + achievement.id + " - " + e.Message);
                }
            }

            ModBehaviour.DevLog("[AchievementView] 已创建 " + entries.Count + " 个成就条目");

            // 强制刷新布局
            Canvas.ForceUpdateCanvases();
        }

        private void UpdateStats()
        {
            var (unlocked, total) = BossRushAchievementManager.GetStats();
            string statsFormat = AchievementUIStrings.GetText(AchievementUIStrings.CN_Stats, AchievementUIStrings.EN_Stats);
            statsText.text = string.Format(statsFormat, unlocked, total);

            // 更新进度条
            if (progressBarFill != null)
            {
                float progress = total > 0 ? (float)unlocked / total : 0f;
                RectTransform fillRect = progressBarFill.rectTransform;
                fillRect.anchorMax = new Vector2(progress, 1f);
            }

            long claimedCash = BossRushAchievementManager.GetClaimedRewardCash();
            string totalFormat = AchievementUIStrings.GetText(AchievementUIStrings.CN_TotalReward, AchievementUIStrings.EN_TotalReward);
            totalRewardText.text = string.Format(totalFormat, claimedCash.ToString("N0"));
        }

        private void UpdateClaimAllButton()
        {
            int claimable = 0;
            foreach (var entry in entries)
            {
                if (entry != null && entry.CanClaim)
                {
                    claimable++;
                }
            }
            bool hasClaimable = claimable > 0;

            if (claimAllButton != null)
            {
                // 只切 interactable：官方按钮 prefab 有自己的底图与禁用态，回退按钮的三态住在 ColorBlock 里。
                // 旧写法把 Image.color 整块乘成平涂绿，官方形状 + Mod 颜色混搭，禁用时还再叠一层禁用乘色（审美审查 UD-36）。
                claimAllButton.interactable = hasClaimable;
            }
            if (claimAllButtonText != null)
            {
                // 「还有几个能领」直接写在按钮上：比一块绿按钮更能说明「这里有东西」
                string label = AchievementUIStrings.GetText(AchievementUIStrings.CN_ClaimAll, AchievementUIStrings.EN_ClaimAll);
                claimAllButtonText.text = hasClaimable ? label + " (" + claimable + ")" : label;
            }
        }

        private void OnEntryRewardClaimed(AchievementEntryUI entry)
        {
            IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundSell);
            if (entry != null)
            {
                entry.PlayClaimFeedback();
            }
            UpdateStats();
            UpdateClaimAllButton();
        }

        #endregion

        #region Update

        void Update()
        {
            if (!isOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        #endregion
    }
}

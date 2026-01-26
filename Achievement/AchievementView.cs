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

        private static readonly Color PanelBgColor = new Color32(18, 20, 25, 250);
        private static readonly Color HeaderBgColor = new Color32(28, 32, 40, 255);
        private static readonly Color FooterBgColor = new Color32(28, 32, 40, 255);
        private static readonly Color StatsBgColor = new Color32(22, 25, 32, 255);
        private static readonly Color TitleColor = new Color32(255, 215, 0, 255);
        private static readonly Color StatsColor = new Color32(180, 180, 180, 255);
        private static readonly Color ProgressBarBgColor = new Color32(40, 42, 50, 255);
        private static readonly Color ProgressBarFillColor = new Color32(76, 175, 80, 255);
        private static readonly Color ButtonColor = new Color32(76, 175, 80, 255);
        private static readonly Color ButtonHoverColor = new Color32(100, 200, 100, 255);
        private static readonly Color ButtonDisabledColor = new Color32(60, 60, 65, 255);
        private static readonly Color CloseButtonColor = new Color32(180, 50, 50, 255);
        private static readonly Color ScrollBgColor = new Color32(15, 17, 22, 255);

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
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            
            calculatedPanelWidth = Mathf.Clamp(screenWidth * PanelWidthRatio, MinPanelWidth, MaxPanelWidth);
            calculatedPanelHeight = Mathf.Clamp(screenHeight * PanelHeightRatio, MinPanelHeight, MaxPanelHeight);
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
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
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<GraphicRaycaster>();

            // 创建半透明背景
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.7f);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

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
            panelImage.color = PanelBgColor;

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

            Image headerImage = headerObj.AddComponent<Image>();
            headerImage.color = HeaderBgColor;

            // 标题
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(headerObj.transform, false);

            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-50f, 0f);

            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 24;
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
                // 回退创建简单按钮
                GameObject closeObj = new GameObject("CloseButton");
                closeObj.transform.SetParent(headerObj.transform, false);

                RectTransform closeRect = closeObj.AddComponent<RectTransform>();
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-10f, 0f);
                closeRect.sizeDelta = new Vector2(35f, 35f);

                Image closeImage = closeObj.AddComponent<Image>();
                closeImage.color = CloseButtonColor;

                exitButton = closeObj.AddComponent<Button>();
                exitButton.targetGraphic = closeImage;
                exitButton.onClick.AddListener(Close);

                GameObject closeTextObj = new GameObject("CloseText");
                closeTextObj.transform.SetParent(closeObj.transform, false);

                RectTransform closeTextRect = closeTextObj.AddComponent<RectTransform>();
                closeTextRect.anchorMin = Vector2.zero;
                closeTextRect.anchorMax = Vector2.one;
                closeTextRect.offsetMin = Vector2.zero;
                closeTextRect.offsetMax = Vector2.zero;

                TextMeshProUGUI closeText = closeTextObj.AddComponent<TextMeshProUGUI>();
                closeText.fontSize = 20;
                closeText.color = Color.white;
                closeText.alignment = TextAlignmentOptions.Center;
                closeText.text = "×";
                closeText.raycastTarget = false;
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

            Image statsImage = statsObj.AddComponent<Image>();
            statsImage.color = StatsBgColor;

            // 统计文本（左侧）
            GameObject statsTextObj = new GameObject("StatsText");
            statsTextObj.transform.SetParent(statsObj.transform, false);

            RectTransform statsTextRect = statsTextObj.AddComponent<RectTransform>();
            statsTextRect.anchorMin = new Vector2(0f, 0f);
            statsTextRect.anchorMax = new Vector2(0.35f, 1f);
            statsTextRect.offsetMin = new Vector2(15f, 0f);
            statsTextRect.offsetMax = Vector2.zero;

            statsText = statsTextObj.AddComponent<TextMeshProUGUI>();
            statsText.fontSize = 14;
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
            progressBarFill.color = ProgressBarFillColor;
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
                vpImage.color = ScrollBgColor;
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

            Image footerImage = footerObj.AddComponent<Image>();
            footerImage.color = FooterBgColor;

            // 已领取奖励总额
            GameObject totalObj = new GameObject("TotalReward");
            totalObj.transform.SetParent(footerObj.transform, false);

            RectTransform totalRect = totalObj.AddComponent<RectTransform>();
            totalRect.anchorMin = new Vector2(0f, 0f);
            totalRect.anchorMax = new Vector2(0.6f, 1f);
            totalRect.offsetMin = new Vector2(20f, 0f);
            totalRect.offsetMax = Vector2.zero;

            totalRewardText = totalObj.AddComponent<TextMeshProUGUI>();
            totalRewardText.fontSize = 16;
            totalRewardText.color = new Color32(255, 215, 0, 255);
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
                claimBtnRect.sizeDelta = new Vector2(120f, 40f);

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
                GameObject claimAllObj = new GameObject("ClaimAllButton");
                claimAllObj.transform.SetParent(footerObj.transform, false);

                RectTransform claimAllRect = claimAllObj.AddComponent<RectTransform>();
                claimAllRect.anchorMin = new Vector2(1f, 0.5f);
                claimAllRect.anchorMax = new Vector2(1f, 0.5f);
                claimAllRect.pivot = new Vector2(1f, 0.5f);
                claimAllRect.anchoredPosition = new Vector2(-15f, 0f);
                claimAllRect.sizeDelta = new Vector2(120f, 40f);

                Image claimAllImage = claimAllObj.AddComponent<Image>();
                claimAllImage.color = ButtonColor;

                claimAllButton = claimAllObj.AddComponent<Button>();
                claimAllButton.targetGraphic = claimAllImage;
                claimAllButton.onClick.AddListener(ClaimAllRewards);

                GameObject claimAllTextObj = new GameObject("ButtonText");
                claimAllTextObj.transform.SetParent(claimAllObj.transform, false);

                RectTransform claimAllTextRect = claimAllTextObj.AddComponent<RectTransform>();
                claimAllTextRect.anchorMin = Vector2.zero;
                claimAllTextRect.anchorMax = Vector2.one;
                claimAllTextRect.offsetMin = Vector2.zero;
                claimAllTextRect.offsetMax = Vector2.zero;

                claimAllButtonText = claimAllTextObj.AddComponent<TextMeshProUGUI>();
                claimAllButtonText.fontSize = 16;
                claimAllButtonText.fontStyle = FontStyles.Bold;
                claimAllButtonText.color = Color.white;
                claimAllButtonText.alignment = TextAlignmentOptions.Center;
                claimAllButtonText.raycastTarget = false;
            }
        }

        #endregion

        #region 公共方法

        public void Open()
        {
            if (isOpen) return;

            isOpen = true;
            canvas.gameObject.SetActive(true);

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

            ModBehaviour.DevLog("[AchievementView] 成就页面已打开");
        }

        public void Close()
        {
            if (!isOpen && canvas != null && !canvas.gameObject.activeSelf) return;

            isOpen = false;
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }

            try
            {
                InputManager.ActiveInput(gameObject);
            }
            catch { }

            ModBehaviour.DevLog("[AchievementView] 成就页面已关闭");
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
                    }
                }

                if (claimedCount > 0)
                {
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
            if (claimAllButtonText != null)
            {
                claimAllButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_ClaimAll, AchievementUIStrings.EN_ClaimAll);
            }
        }

        /// <summary>
        /// 填充成就条目 - 使用LayoutElement
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

            ModBehaviour.DevLog("[AchievementView] 准备创建 " + achievements.Count + " 个成就条目");

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
            bool hasClaimable = false;
            foreach (var entry in entries)
            {
                if (entry.CanClaim)
                {
                    hasClaimable = true;
                    break;
                }
            }

            if (claimAllButton != null)
            {
                claimAllButton.interactable = hasClaimable;
                Image btnImage = claimAllButton.GetComponent<Image>();
                if (btnImage != null)
                {
                    btnImage.color = hasClaimable ? ButtonColor : ButtonDisabledColor;
                }
            }
        }

        private void OnEntryRewardClaimed(AchievementEntryUI entry)
        {
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

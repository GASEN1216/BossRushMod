// ============================================================================
// AchievementView.cs - 成就页面主视图
// ============================================================================
// 模块说明：
//   成就页面的主容器，管理整个成就界面的显示和交互
//   支持 L 键打开/关闭，ESC 键关闭
//   包含成就列表、统计信息、一键领取功能
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// 成就页面主视图
    /// </summary>
    public class AchievementView : MonoBehaviour
    {
        #region 布局常量

        // 面板尺寸 - 使用屏幕百分比，确保不同分辨率下一致
        private const float PanelWidthPercent = 0.55f;   // 屏幕宽度的55%
        private const float PanelHeightPercent = 0.75f;  // 屏幕高度的75%
        private const float HeaderHeight = 70f;
        private const float FooterHeight = 60f;
        private const float ScrollPadding = 15f;

        // 颜色
        private static readonly Color PanelBgColor = new Color32(22, 25, 29, 245);
        private static readonly Color HeaderBgColor = new Color32(35, 38, 45, 255);
        private static readonly Color FooterBgColor = new Color32(35, 38, 45, 255);
        private static readonly Color TitleColor = new Color32(255, 215, 0, 255);  // 金色标题
        private static readonly Color StatsColor = new Color32(200, 200, 200, 255);
        private static readonly Color ButtonColor = new Color32(76, 175, 80, 255);
        private static readonly Color ButtonDisabledColor = new Color32(80, 80, 80, 255);
        private static readonly Color CloseButtonColor = new Color32(200, 60, 60, 255);
        private static readonly Color ScrollBgColor = new Color32(18, 20, 24, 255);

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
        private CanvasScaler canvasScaler;
        private GameObject panelRoot;
        private RectTransform panelRect;
        private ScrollRect scrollRect;
        private RectTransform contentContainer;
        private Text titleText;
        private Text statsText;
        private Text totalRewardText;
        private Button claimAllButton;
        private Text claimAllButtonText;
        private Button exitButton;

        #endregion

        #region 状态字段

        private bool isOpen;
        private List<AchievementEntryUI> entries = new List<AchievementEntryUI>();

        #endregion

        #region 公共属性

        /// <summary>
        /// 页面是否打开
        /// </summary>
        public bool IsOpen => isOpen;

        #endregion

        #region 初始化

        /// <summary>
        /// 确保实例存在
        /// </summary>
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

            CreateUI();
            Close(); // 初始状态为关闭
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 创建UI
        /// </summary>
        private void CreateUI()
        {
            // 创建 Canvas
            GameObject canvasObj = new GameObject("AchievementCanvas");
            canvasObj.transform.SetParent(transform);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            canvasScaler = canvasObj.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // 创建半透明背景遮罩
            CreateBackgroundMask(canvasObj.transform);

            // 创建主面板
            CreateMainPanel(canvasObj.transform);

            // 创建头部
            CreateHeader();

            // 创建滚动区域
            CreateScrollArea();

            // 创建底部
            CreateFooter();
        }

        /// <summary>
        /// 创建背景遮罩
        /// </summary>
        private void CreateBackgroundMask(Transform parent)
        {
            GameObject maskObj = new GameObject("BackgroundMask");
            maskObj.transform.SetParent(parent, false);

            RectTransform maskRect = maskObj.AddComponent<RectTransform>();
            maskRect.anchorMin = Vector2.zero;
            maskRect.anchorMax = Vector2.one;
            maskRect.offsetMin = Vector2.zero;
            maskRect.offsetMax = Vector2.zero;

            Image maskImage = maskObj.AddComponent<Image>();
            maskImage.color = new Color(0, 0, 0, 0.7f);

            // 点击遮罩关闭
            Button maskButton = maskObj.AddComponent<Button>();
            maskButton.transition = Selectable.Transition.None;
            maskButton.onClick.AddListener(Close);
        }

        /// <summary>
        /// 创建主面板
        /// </summary>
        private void CreateMainPanel(Transform parent)
        {
            panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(parent, false);

            panelRect = panelRoot.AddComponent<RectTransform>();
            // 使用锚点百分比定位，确保不同分辨率下一致
            panelRect.anchorMin = new Vector2(0.5f - PanelWidthPercent / 2f, 0.5f - PanelHeightPercent / 2f);
            panelRect.anchorMax = new Vector2(0.5f + PanelWidthPercent / 2f, 0.5f + PanelHeightPercent / 2f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image panelImage = panelRoot.AddComponent<Image>();
            panelImage.color = PanelBgColor;
        }

        /// <summary>
        /// 创建头部区域
        /// </summary>
        private void CreateHeader()
        {
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(panelRoot.transform, false);

            RectTransform headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, HeaderHeight);

            Image headerImage = headerObj.AddComponent<Image>();
            headerImage.color = HeaderBgColor;

            // 标题
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(headerObj.transform, false);

            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(0.3f, 1);
            titleRect.offsetMin = new Vector2(25f, 0);
            titleRect.offsetMax = Vector2.zero;

            titleText = titleObj.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 28;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = TitleColor;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.raycastTarget = false;

            // 统计信息
            GameObject statsObj = new GameObject("Stats");
            statsObj.transform.SetParent(headerObj.transform, false);

            RectTransform statsRect = statsObj.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0.3f, 0);
            statsRect.anchorMax = new Vector2(0.7f, 1);
            statsRect.offsetMin = Vector2.zero;
            statsRect.offsetMax = Vector2.zero;

            statsText = statsObj.AddComponent<Text>();
            statsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statsText.fontSize = 18;
            statsText.color = StatsColor;
            statsText.alignment = TextAnchor.MiddleCenter;
            statsText.raycastTarget = false;

            // 关闭按钮
            GameObject closeObj = new GameObject("CloseButton");
            closeObj.transform.SetParent(headerObj.transform, false);

            RectTransform closeRect = closeObj.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1, 0.5f);
            closeRect.anchorMax = new Vector2(1, 0.5f);
            closeRect.pivot = new Vector2(1, 0.5f);
            closeRect.anchoredPosition = new Vector2(-15f, 0);
            closeRect.sizeDelta = new Vector2(44f, 44f);

            Image closeImage = closeObj.AddComponent<Image>();
            closeImage.color = CloseButtonColor;

            exitButton = closeObj.AddComponent<Button>();
            exitButton.targetGraphic = closeImage;
            exitButton.onClick.AddListener(Close);

            // 关闭按钮文本 (X)
            GameObject closeTextObj = new GameObject("CloseText");
            closeTextObj.transform.SetParent(closeObj.transform, false);

            RectTransform closeTextRect = closeTextObj.AddComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;

            Text closeText = closeTextObj.AddComponent<Text>();
            closeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            closeText.fontSize = 26;
            closeText.fontStyle = FontStyle.Bold;
            closeText.color = Color.white;
            closeText.alignment = TextAnchor.MiddleCenter;
            closeText.text = "×";
            closeText.raycastTarget = false;
        }

        /// <summary>
        /// 创建滚动区域
        /// </summary>
        private void CreateScrollArea()
        {
            // 滚动视图容器
            GameObject scrollObj = new GameObject("ScrollView");
            scrollObj.transform.SetParent(panelRoot.transform, false);

            RectTransform scrollRectTransform = scrollObj.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0, 0);
            scrollRectTransform.anchorMax = new Vector2(1, 1);
            scrollRectTransform.offsetMin = new Vector2(ScrollPadding, FooterHeight);
            scrollRectTransform.offsetMax = new Vector2(-ScrollPadding, -HeaderHeight);

            // 滚动区域背景
            Image scrollBgImage = scrollObj.AddComponent<Image>();
            scrollBgImage.color = ScrollBgColor;

            scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.1f;
            scrollRect.scrollSensitivity = 40f;

            // 视口
            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollObj.transform, false);

            RectTransform viewportRect = viewportObj.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(10f, 10f);
            viewportRect.offsetMax = new Vector2(-10f, -10f);

            Image viewportImage = viewportObj.AddComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0);

            Mask viewportMask = viewportObj.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            scrollRect.viewport = viewportRect;

            // 内容容器
            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);

            contentContainer = contentObj.AddComponent<RectTransform>();
            contentContainer.anchorMin = new Vector2(0, 1);
            contentContainer.anchorMax = new Vector2(1, 1);
            contentContainer.pivot = new Vector2(0.5f, 1);
            contentContainer.anchoredPosition = Vector2.zero;
            // 初始高度设为0，由 ContentSizeFitter 自动调整
            contentContainer.sizeDelta = new Vector2(0, 0);

            // 添加垂直布局组件
            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 8f;
            layoutGroup.padding = new RectOffset(5, 5, 5, 5);

            // 添加 ContentSizeFitter
            ContentSizeFitter sizeFitter = contentObj.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentContainer;
        }

        /// <summary>
        /// 创建底部区域
        /// </summary>
        private void CreateFooter()
        {
            GameObject footerObj = new GameObject("Footer");
            footerObj.transform.SetParent(panelRoot.transform, false);

            RectTransform footerRect = footerObj.AddComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0, 0);
            footerRect.anchorMax = new Vector2(1, 0);
            footerRect.pivot = new Vector2(0.5f, 0);
            footerRect.anchoredPosition = Vector2.zero;
            footerRect.sizeDelta = new Vector2(0, FooterHeight);

            Image footerImage = footerObj.AddComponent<Image>();
            footerImage.color = FooterBgColor;

            // 已领取奖励总额
            GameObject totalObj = new GameObject("TotalReward");
            totalObj.transform.SetParent(footerObj.transform, false);

            RectTransform totalRect = totalObj.AddComponent<RectTransform>();
            totalRect.anchorMin = new Vector2(0, 0);
            totalRect.anchorMax = new Vector2(0.5f, 1);
            totalRect.offsetMin = new Vector2(25f, 0);
            totalRect.offsetMax = Vector2.zero;

            totalRewardText = totalObj.AddComponent<Text>();
            totalRewardText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            totalRewardText.fontSize = 18;
            totalRewardText.color = new Color32(255, 215, 0, 255);
            totalRewardText.alignment = TextAnchor.MiddleLeft;
            totalRewardText.raycastTarget = false;

            // 一键领取按钮
            GameObject claimAllObj = new GameObject("ClaimAllButton");
            claimAllObj.transform.SetParent(footerObj.transform, false);

            RectTransform claimAllRect = claimAllObj.AddComponent<RectTransform>();
            claimAllRect.anchorMin = new Vector2(1, 0.5f);
            claimAllRect.anchorMax = new Vector2(1, 0.5f);
            claimAllRect.pivot = new Vector2(1, 0.5f);
            claimAllRect.anchoredPosition = new Vector2(-25f, 0);
            claimAllRect.sizeDelta = new Vector2(140f, 44f);

            Image claimAllImage = claimAllObj.AddComponent<Image>();
            claimAllImage.color = ButtonColor;

            claimAllButton = claimAllObj.AddComponent<Button>();
            claimAllButton.targetGraphic = claimAllImage;
            claimAllButton.onClick.AddListener(ClaimAllRewards);

            // 按钮文本
            GameObject claimAllTextObj = new GameObject("ButtonText");
            claimAllTextObj.transform.SetParent(claimAllObj.transform, false);

            RectTransform claimAllTextRect = claimAllTextObj.AddComponent<RectTransform>();
            claimAllTextRect.anchorMin = Vector2.zero;
            claimAllTextRect.anchorMax = Vector2.one;
            claimAllTextRect.offsetMin = Vector2.zero;
            claimAllTextRect.offsetMax = Vector2.zero;

            claimAllButtonText = claimAllTextObj.AddComponent<Text>();
            claimAllButtonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            claimAllButtonText.fontSize = 18;
            claimAllButtonText.fontStyle = FontStyle.Bold;
            claimAllButtonText.color = Color.white;
            claimAllButtonText.alignment = TextAnchor.MiddleCenter;
            claimAllButtonText.raycastTarget = false;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 打开成就页面
        /// </summary>
        public void Open()
        {
            if (isOpen) return;

            isOpen = true;
            canvas.gameObject.SetActive(true);

            // 禁用玩家输入
            try
            {
                InputManager.DisableInput(gameObject);
            }
            catch { }

            // 确保成就系统已初始化
            BossRushAchievementManager.Initialize();

            // 刷新内容
            RefreshAll();

            ModBehaviour.DevLog("[AchievementView] 成就页面已打开");
        }

        /// <summary>
        /// 关闭成就页面
        /// </summary>
        public void Close()
        {
            if (!isOpen && canvas != null && !canvas.gameObject.activeSelf) return;

            isOpen = false;
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }

            // 恢复玩家输入
            try
            {
                InputManager.ActiveInput(gameObject);
            }
            catch { }

            ModBehaviour.DevLog("[AchievementView] 成就页面已关闭");
        }

        /// <summary>
        /// 切换打开/关闭状态
        /// </summary>
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

        /// <summary>
        /// 刷新所有内容
        /// </summary>
        public void RefreshAll()
        {
            UpdateLocalizedTexts();
            PopulateEntries();
            UpdateStats();
            UpdateClaimAllButton();
        }

        /// <summary>
        /// 一键领取所有奖励
        /// </summary>
        public void ClaimAllRewards()
        {
            int claimedCount = 0;
            long totalCash = 0;

            foreach (var entry in entries)
            {
                if (entry.CanClaim)
                {
                    var achievement = entry.Achievement;
                    if (entry.TryClaim())
                    {
                        claimedCount++;
                        totalCash += achievement.reward?.cashReward ?? 0;
                    }
                }
            }

            // 显示结果通知
            if (claimedCount > 0)
            {
                string message = string.Format(
                    AchievementUIStrings.GetText(AchievementUIStrings.CN_ClaimedTotal, AchievementUIStrings.EN_ClaimedTotal),
                    totalCash.ToString("N0")
                );
                NotificationText.Push(message);
                ModBehaviour.DevLog($"[AchievementView] 一键领取完成: {claimedCount} 个成就, ${totalCash}");
            }
            else
            {
                string message = AchievementUIStrings.GetText(AchievementUIStrings.CN_NoRewards, AchievementUIStrings.EN_NoRewards);
                NotificationText.Push(message);
            }

            UpdateStats();
            UpdateClaimAllButton();
        }

        #endregion

        #region 内部方法

        /// <summary>
        /// 更新本地化文本
        /// </summary>
        private void UpdateLocalizedTexts()
        {
            titleText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Title, AchievementUIStrings.EN_Title);
            claimAllButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_ClaimAll, AchievementUIStrings.EN_ClaimAll);
        }

        /// <summary>
        /// 填充成就条目
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

            ModBehaviour.DevLog($"[AchievementView] 准备创建 {achievements.Count} 个成就条目");

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
                    ModBehaviour.DevLog($"[AchievementView] 创建条目失败: {achievement.id} - {e.Message}");
                }
            }

            ModBehaviour.DevLog($"[AchievementView] 已创建 {entries.Count} 个成就条目");

            // 强制刷新布局
            Canvas.ForceUpdateCanvases();
            if (contentContainer != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer);
            }
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        private void UpdateStats()
        {
            var (unlocked, total) = BossRushAchievementManager.GetStats();
            string statsFormat = AchievementUIStrings.GetText(AchievementUIStrings.CN_Stats, AchievementUIStrings.EN_Stats);
            statsText.text = string.Format(statsFormat, unlocked, total);

            long claimedCash = BossRushAchievementManager.GetClaimedRewardCash();
            string totalFormat = AchievementUIStrings.GetText(AchievementUIStrings.CN_TotalReward, AchievementUIStrings.EN_TotalReward);
            totalRewardText.text = string.Format(totalFormat, claimedCash.ToString("N0"));
        }

        /// <summary>
        /// 更新一键领取按钮状态
        /// </summary>
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

            claimAllButton.interactable = hasClaimable;
            claimAllButton.GetComponent<Image>().color = hasClaimable ? ButtonColor : ButtonDisabledColor;
        }

        /// <summary>
        /// 条目奖励领取回调
        /// </summary>
        private void OnEntryRewardClaimed(AchievementEntryUI entry)
        {
            UpdateStats();
            UpdateClaimAllButton();
        }

        #endregion

        #region Update 处理

        void Update()
        {
            if (!isOpen) return;

            // ESC 键关闭
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        #endregion
    }
}

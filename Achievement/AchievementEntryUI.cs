// ============================================================================
// AchievementEntryUI.cs - 成就条目UI组件
// ============================================================================
// 模块说明：
//   单个成就条目的UI组件，显示成就图标、名称、描述、难度和奖励信息
//   支持四种状态：未解锁、隐藏未解锁、已解锁未领取、已解锁已领取
// ============================================================================

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 成就条目显示状态
    /// </summary>
    public enum AchievementEntryState
    {
        Locked,            // 未解锁，灰色显示
        LockedHidden,      // 隐藏成就未解锁，显示 ???
        UnlockedUnclaimed, // 已解锁未领取，可点击领取
        UnlockedClaimed    // 已解锁已领取，按钮禁用
    }

    /// <summary>
    /// 成就条目UI组件
    /// </summary>
    public class AchievementEntryUI : MonoBehaviour
    {
        #region UI布局常量

        // 条目尺寸
        public const float EntryHeight = 90f;
        public const float EntryPadding = 12f;
        public const float IconSize = 70f;

        // 颜色
        public static readonly Color BgColor = new Color32(40, 44, 52, 255);
        public static readonly Color BgColorUnlocked = new Color32(45, 55, 50, 255);
        public static readonly Color TextColor = new Color32(240, 240, 240, 255);
        public static readonly Color DescColor = new Color32(180, 180, 180, 255);
        public static readonly Color DisabledColor = new Color32(100, 100, 100, 255);
        public static readonly Color HighlightColor = new Color32(76, 175, 80, 255);
        public static readonly Color GoldColor = new Color32(255, 215, 0, 255);
        public static readonly Color LockedBgColor = new Color32(30, 32, 38, 255);
        public static readonly Color BorderColor = new Color32(60, 65, 75, 255);

        #endregion

        #region UI组件引用

        private RectTransform rectTransform;
        private Image backgroundImage;
        private Image borderImage;
        private RawImage iconImage;
        private Text nameText;
        private Text descText;
        private Text difficultyText;
        private Text rewardText;
        private Button claimButton;
        private Text claimButtonText;
        private Image claimButtonImage;

        #endregion

        #region 数据字段

        private BossRushAchievementDef achievement;
        private AchievementEntryState currentState;
        private Texture2D loadedIcon;

        #endregion

        #region 事件

        /// <summary>
        /// 奖励领取成功事件
        /// </summary>
        public event Action<AchievementEntryUI> OnRewardClaimed;

        #endregion

        #region 公共属性

        /// <summary>
        /// 当前成就定义
        /// </summary>
        public BossRushAchievementDef Achievement => achievement;

        /// <summary>
        /// 当前状态
        /// </summary>
        public AchievementEntryState State => currentState;

        /// <summary>
        /// 是否可以领取奖励
        /// </summary>
        public bool CanClaim => currentState == AchievementEntryState.UnlockedUnclaimed;

        #endregion

        #region 初始化

        /// <summary>
        /// 创建成就条目UI
        /// </summary>
        public static AchievementEntryUI Create(Transform parent, BossRushAchievementDef def)
        {
            if (def == null)
            {
                Debug.LogWarning("[AchievementEntryUI] Create called with null definition");
                return null;
            }

            // 创建条目容器
            GameObject entryObj = new GameObject("AchievementEntry_" + def.id);
            entryObj.transform.SetParent(parent, false);

            AchievementEntryUI entry = entryObj.AddComponent<AchievementEntryUI>();
            entry.CreateUI();
            entry.Setup(def);

            return entry;
        }

        /// <summary>
        /// 创建UI元素
        /// </summary>
        private void CreateUI()
        {
            // 设置 RectTransform
            rectTransform = gameObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(0, EntryHeight);

            // 添加 LayoutElement 用于 VerticalLayoutGroup
            LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = EntryHeight;
            layoutElement.preferredHeight = EntryHeight;
            layoutElement.flexibleWidth = 1f;

            // 边框背景
            borderImage = gameObject.AddComponent<Image>();
            borderImage.color = BorderColor;

            // 内部背景容器
            GameObject innerObj = new GameObject("Inner");
            innerObj.transform.SetParent(transform, false);

            RectTransform innerRect = innerObj.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(2f, 2f);
            innerRect.offsetMax = new Vector2(-2f, -2f);

            backgroundImage = innerObj.AddComponent<Image>();
            backgroundImage.color = BgColor;

            // 创建图标区域
            CreateIconArea(innerObj.transform);

            // 创建文本区域
            CreateTextArea(innerObj.transform);

            // 创建奖励和按钮区域
            CreateRewardArea(innerObj.transform);
        }

        /// <summary>
        /// 创建图标区域（左侧）
        /// </summary>
        private void CreateIconArea(Transform parent)
        {
            // 图标容器
            GameObject iconContainer = new GameObject("IconContainer");
            iconContainer.transform.SetParent(parent, false);

            RectTransform containerRect = iconContainer.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0, 0.5f);
            containerRect.anchorMax = new Vector2(0, 0.5f);
            containerRect.pivot = new Vector2(0, 0.5f);
            containerRect.anchoredPosition = new Vector2(EntryPadding, 0);
            containerRect.sizeDelta = new Vector2(IconSize, IconSize);

            // 图标背景
            Image iconBg = iconContainer.AddComponent<Image>();
            iconBg.color = new Color32(25, 27, 32, 255);

            // 图标
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(iconContainer.transform, false);

            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.05f, 0.05f);
            iconRect.anchorMax = new Vector2(0.95f, 0.95f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            iconImage = iconObj.AddComponent<RawImage>();
            iconImage.color = Color.white;
        }

        /// <summary>
        /// 创建文本区域（中间）
        /// </summary>
        private void CreateTextArea(Transform parent)
        {
            float textStartX = EntryPadding + IconSize + 15f;

            // 名称文本
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(parent, false);

            RectTransform nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0, 0.5f);
            nameRect.anchorMax = new Vector2(0.65f, 1);
            nameRect.offsetMin = new Vector2(textStartX, 5f);
            nameRect.offsetMax = new Vector2(0, -8f);

            nameText = nameObj.AddComponent<Text>();
            nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nameText.fontSize = 18;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = TextColor;
            nameText.alignment = TextAnchor.LowerLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.raycastTarget = false;

            // 描述文本
            GameObject descObj = new GameObject("DescText");
            descObj.transform.SetParent(parent, false);

            RectTransform descRect = descObj.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0, 0);
            descRect.anchorMax = new Vector2(0.65f, 0.5f);
            descRect.offsetMin = new Vector2(textStartX, 8f);
            descRect.offsetMax = new Vector2(0, -5f);

            descText = descObj.AddComponent<Text>();
            descText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            descText.fontSize = 13;
            descText.color = DescColor;
            descText.alignment = TextAnchor.UpperLeft;
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descText.verticalOverflow = VerticalWrapMode.Truncate;
            descText.raycastTarget = false;

            // 难度星级
            GameObject diffObj = new GameObject("DifficultyText");
            diffObj.transform.SetParent(parent, false);

            RectTransform diffRect = diffObj.AddComponent<RectTransform>();
            diffRect.anchorMin = new Vector2(0.65f, 0);
            diffRect.anchorMax = new Vector2(0.75f, 0.5f);
            diffRect.offsetMin = new Vector2(5f, 8f);
            diffRect.offsetMax = new Vector2(-5f, -5f);

            difficultyText = diffObj.AddComponent<Text>();
            difficultyText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            difficultyText.fontSize = 14;
            difficultyText.color = GoldColor;
            difficultyText.alignment = TextAnchor.UpperLeft;
            difficultyText.raycastTarget = false;
        }

        /// <summary>
        /// 创建奖励和按钮区域（右侧）
        /// </summary>
        private void CreateRewardArea(Transform parent)
        {
            // 奖励金额文本
            GameObject rewardObj = new GameObject("RewardText");
            rewardObj.transform.SetParent(parent, false);

            RectTransform rewardRect = rewardObj.AddComponent<RectTransform>();
            rewardRect.anchorMin = new Vector2(0.65f, 0.5f);
            rewardRect.anchorMax = new Vector2(0.85f, 1);
            rewardRect.offsetMin = new Vector2(5f, 5f);
            rewardRect.offsetMax = new Vector2(-5f, -8f);

            rewardText = rewardObj.AddComponent<Text>();
            rewardText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            rewardText.fontSize = 16;
            rewardText.fontStyle = FontStyle.Bold;
            rewardText.color = GoldColor;
            rewardText.alignment = TextAnchor.LowerRight;
            rewardText.raycastTarget = false;

            // 领取按钮
            GameObject buttonObj = new GameObject("ClaimButton");
            buttonObj.transform.SetParent(parent, false);

            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.85f, 0.5f);
            buttonRect.anchorMax = new Vector2(1, 0.5f);
            buttonRect.pivot = new Vector2(1, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-EntryPadding, 0);
            buttonRect.sizeDelta = new Vector2(90f, 36f);

            claimButtonImage = buttonObj.AddComponent<Image>();
            claimButtonImage.color = HighlightColor;

            claimButton = buttonObj.AddComponent<Button>();
            claimButton.targetGraphic = claimButtonImage;
            claimButton.onClick.AddListener(OnClaimClicked);

            // 按钮文本
            GameObject buttonTextObj = new GameObject("ButtonText");
            buttonTextObj.transform.SetParent(buttonObj.transform, false);

            RectTransform buttonTextRect = buttonTextObj.AddComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            claimButtonText = buttonTextObj.AddComponent<Text>();
            claimButtonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            claimButtonText.fontSize = 14;
            claimButtonText.fontStyle = FontStyle.Bold;
            claimButtonText.color = Color.white;
            claimButtonText.alignment = TextAnchor.MiddleCenter;
            claimButtonText.raycastTarget = false;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置成就数据
        /// </summary>
        public void Setup(BossRushAchievementDef def)
        {
            if (def == null)
            {
                Debug.LogWarning("[AchievementEntryUI] Setup called with null definition");
                return;
            }

            achievement = def;
            Refresh();
        }

        /// <summary>
        /// 刷新显示状态
        /// </summary>
        public void Refresh()
        {
            if (achievement == null) return;

            // 确定当前状态
            bool isUnlocked = BossRushAchievementManager.IsUnlocked(achievement.id);
            bool isClaimed = BossRushAchievementManager.IsRewardClaimed(achievement.id);

            if (isUnlocked)
            {
                currentState = isClaimed ? AchievementEntryState.UnlockedClaimed : AchievementEntryState.UnlockedUnclaimed;
            }
            else
            {
                currentState = achievement.isHidden ? AchievementEntryState.LockedHidden : AchievementEntryState.Locked;
            }

            UpdateVisuals();
        }

        /// <summary>
        /// 尝试领取奖励
        /// </summary>
        public bool TryClaim()
        {
            if (!CanClaim) return false;

            bool success = BossRushAchievementManager.ClaimReward(achievement.id);
            if (success)
            {
                Refresh();
                OnRewardClaimed?.Invoke(this);
            }

            return success;
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            if (loadedIcon != null)
            {
                UnityEngine.Object.Destroy(loadedIcon);
                loadedIcon = null;
            }
        }

        #endregion

        #region 内部方法

        /// <summary>
        /// 更新视觉显示
        /// </summary>
        private void UpdateVisuals()
        {
            bool isChinese = AchievementUIStrings.IsChinese();

            switch (currentState)
            {
                case AchievementEntryState.Locked:
                    // 未解锁：灰色显示
                    backgroundImage.color = LockedBgColor;
                    borderImage.color = new Color32(50, 52, 58, 255);
                    nameText.text = isChinese ? achievement.nameCN : achievement.nameEN;
                    nameText.color = DisabledColor;
                    descText.text = isChinese ? achievement.descCN : achievement.descEN;
                    descText.color = DisabledColor;
                    difficultyText.text = GetDifficultyStars(achievement.difficultyRating);
                    difficultyText.color = DisabledColor;
                    rewardText.text = "$" + achievement.reward.cashReward.ToString("N0");
                    rewardText.color = DisabledColor;
                    claimButton.gameObject.SetActive(false);
                    LoadIcon(achievement.iconFile, true);
                    break;

                case AchievementEntryState.LockedHidden:
                    // 隐藏成就未解锁：显示 ???
                    backgroundImage.color = LockedBgColor;
                    borderImage.color = new Color32(50, 52, 58, 255);
                    nameText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_HiddenName, AchievementUIStrings.EN_HiddenName);
                    nameText.color = DisabledColor;
                    descText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_HiddenDesc, AchievementUIStrings.EN_HiddenDesc);
                    descText.color = DisabledColor;
                    difficultyText.text = "?";
                    difficultyText.color = DisabledColor;
                    rewardText.text = "???";
                    rewardText.color = DisabledColor;
                    claimButton.gameObject.SetActive(false);
                    LoadIcon("default.png", false);
                    break;

                case AchievementEntryState.UnlockedUnclaimed:
                    // 已解锁未领取：高亮显示，可领取
                    backgroundImage.color = BgColorUnlocked;
                    borderImage.color = HighlightColor;
                    nameText.text = isChinese ? achievement.nameCN : achievement.nameEN;
                    nameText.color = TextColor;
                    descText.text = isChinese ? achievement.descCN : achievement.descEN;
                    descText.color = DescColor;
                    difficultyText.text = GetDifficultyStars(achievement.difficultyRating);
                    difficultyText.color = GoldColor;
                    rewardText.text = "$" + achievement.reward.cashReward.ToString("N0");
                    rewardText.color = GoldColor;
                    claimButton.gameObject.SetActive(true);
                    claimButton.interactable = true;
                    claimButtonImage.color = HighlightColor;
                    claimButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Claim, AchievementUIStrings.EN_Claim);
                    LoadIcon(achievement.iconFile, false);
                    break;

                case AchievementEntryState.UnlockedClaimed:
                    // 已解锁已领取：正常显示，按钮禁用
                    backgroundImage.color = BgColor;
                    borderImage.color = BorderColor;
                    nameText.text = isChinese ? achievement.nameCN : achievement.nameEN;
                    nameText.color = TextColor;
                    descText.text = isChinese ? achievement.descCN : achievement.descEN;
                    descText.color = DescColor;
                    difficultyText.text = GetDifficultyStars(achievement.difficultyRating);
                    difficultyText.color = GoldColor;
                    rewardText.text = "$" + achievement.reward.cashReward.ToString("N0");
                    rewardText.color = new Color32(120, 120, 120, 255);
                    claimButton.gameObject.SetActive(true);
                    claimButton.interactable = false;
                    claimButtonImage.color = new Color32(60, 60, 60, 255);
                    claimButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Claimed, AchievementUIStrings.EN_Claimed);
                    LoadIcon(achievement.iconFile, false);
                    break;
            }
        }

        /// <summary>
        /// 按钮点击处理
        /// </summary>
        private void OnClaimClicked()
        {
            TryClaim();
        }

        /// <summary>
        /// 加载成就图标
        /// </summary>
        private void LoadIcon(string iconFile, bool grayscale)
        {
            try
            {
                // 清理之前的纹理
                if (loadedIcon != null)
                {
                    UnityEngine.Object.Destroy(loadedIcon);
                    loadedIcon = null;
                }

                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    SetDefaultIcon(grayscale);
                    return;
                }

                string iconPath = Path.Combine(modPath, "Assets", "achievement", iconFile);
                if (!File.Exists(iconPath))
                {
                    // 尝试加载默认图标
                    iconPath = Path.Combine(modPath, "Assets", "achievement", "default.png");
                    if (!File.Exists(iconPath))
                    {
                        SetDefaultIcon(grayscale);
                        return;
                    }
                }

                byte[] data = File.ReadAllBytes(iconPath);
                loadedIcon = new Texture2D(64, 64);
                if (loadedIcon.LoadImage(data))
                {
                    iconImage.texture = loadedIcon;
                    iconImage.color = grayscale ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
                }
                else
                {
                    SetDefaultIcon(grayscale);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AchievementEntryUI] Failed to load icon {iconFile}: {e.Message}");
                SetDefaultIcon(grayscale);
            }
        }

        /// <summary>
        /// 设置默认图标
        /// </summary>
        private void SetDefaultIcon(bool grayscale)
        {
            iconImage.texture = null;
            iconImage.color = grayscale ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
        }

        /// <summary>
        /// 获取难度星级字符串
        /// </summary>
        private string GetDifficultyStars(int rating)
        {
            if (rating <= 0) return "";
            if (rating > 5) rating = 5;

            string stars = "";
            for (int i = 0; i < rating; i++)
            {
                stars += "★";
            }
            for (int i = rating; i < 5; i++)
            {
                stars += "☆";
            }
            return stars;
        }

        #endregion

        #region 生命周期

        void OnDestroy()
        {
            Cleanup();
        }

        #endregion
    }
}

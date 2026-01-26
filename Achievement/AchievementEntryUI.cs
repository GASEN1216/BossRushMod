// ============================================================================
// AchievementEntryUI.cs - 成就条目UI组件
// ============================================================================
// 模块说明：
//   单个成就条目的UI组件，使用LayoutElement指定高度
//   支持四种状态：未解锁、隐藏未解锁、已解锁未领取、已解锁已领取
// ============================================================================

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    /// 成就条目UI组件 - 使用LayoutElement，带图标缓存
    /// </summary>
    public class AchievementEntryUI : MonoBehaviour
    {
        #region 图标缓存

        private static Dictionary<string, Texture2D> iconCache = new Dictionary<string, Texture2D>();
        private static bool cacheInitialized = false;

        public static void ClearIconCache()
        {
            foreach (var tex in iconCache.Values)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            iconCache.Clear();
            cacheInitialized = false;
        }

        #endregion

        #region UI布局常量

        public const float EntryHeight = 70f;
        public const float Padding = 8f;

        private static readonly Color BgColor = new Color32(30, 33, 40, 255);
        private static readonly Color BgColorUnlocked = new Color32(40, 50, 45, 255);
        private static readonly Color BgColorLocked = new Color32(25, 27, 32, 255);
        private static readonly Color BorderColor = new Color32(60, 65, 75, 255);
        private static readonly Color BorderColorUnlocked = new Color32(76, 175, 80, 255);
        private static readonly Color TextColor = new Color32(240, 240, 240, 255);
        private static readonly Color DescColor = new Color32(180, 180, 180, 255);
        private static readonly Color DisabledColor = new Color32(100, 100, 100, 255);
        private static readonly Color GoldColor = new Color32(255, 215, 0, 255);
        private static readonly Color ButtonColor = new Color32(76, 175, 80, 255);
        private static readonly Color ButtonDisabledColor = new Color32(60, 60, 60, 255);

        #endregion

        #region UI组件引用

        private Image backgroundImage;
        private Image borderImage;
        private RawImage iconImage;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI descText;
        private TextMeshProUGUI rewardText;
        private Button claimButton;
        private TextMeshProUGUI claimButtonText;
        private Image claimButtonImage;

        #endregion

        #region 数据字段

        private BossRushAchievementDef achievement;
        private AchievementEntryState currentState;
        private string currentIconFile;

        #endregion

        #region 事件

        public event Action<AchievementEntryUI> OnRewardClaimed;

        #endregion

        #region 公共属性

        public BossRushAchievementDef Achievement => achievement;
        public AchievementEntryState State => currentState;
        public bool CanClaim => currentState == AchievementEntryState.UnlockedUnclaimed;

        #endregion

        #region 初始化

        /// <summary>
        /// 创建成就条目UI - 参考BossFilter的Toggle创建方式
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

            // 添加LayoutElement来指定高度（关键！）
            LayoutElement layoutElement = entryObj.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = EntryHeight;
            layoutElement.flexibleWidth = 1f;

            // 背景图片
            Image bgImage = entryObj.AddComponent<Image>();
            bgImage.color = BgColor;

            // 内边距容器
            GameObject innerObj = new GameObject("Inner");
            innerObj.transform.SetParent(entryObj.transform, false);

            RectTransform innerRect = innerObj.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(2f, 2f);
            innerRect.offsetMax = new Vector2(-2f, -2f);

            Image innerBg = innerObj.AddComponent<Image>();
            innerBg.color = BgColorLocked;

            AchievementEntryUI entry = entryObj.AddComponent<AchievementEntryUI>();
            entry.InitializeComponents(innerObj.transform);
            entry.Setup(def);

            return entry;
        }

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeComponents(Transform parent)
        {
            // 保存背景引用
            borderImage = GetComponent<Image>();
            backgroundImage = parent.GetComponent<Image>();

            // 创建图标区域
            CreateIconArea(parent);

            // 创建文本区域
            CreateTextArea(parent);

            // 创建按钮区域
            CreateButtonArea(parent);
        }

        /// <summary>
        /// 创建图标区域
        /// </summary>
        private void CreateIconArea(Transform parent)
        {
            // 图标容器
            GameObject iconContainer = new GameObject("IconContainer");
            iconContainer.transform.SetParent(parent, false);

            RectTransform containerRect = iconContainer.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 0.5f);
            containerRect.anchorMax = new Vector2(0f, 0.5f);
            containerRect.pivot = new Vector2(0f, 0.5f);
            containerRect.anchoredPosition = new Vector2(Padding, 0);
            containerRect.sizeDelta = new Vector2(60f, 60f);

            // 图标背景
            Image iconBg = iconContainer.AddComponent<Image>();
            iconBg.color = new Color32(20, 22, 28, 255);

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
        /// 创建文本区域
        /// </summary>
        private void CreateTextArea(Transform parent)
        {
            float textStartX = Padding + 60f + 12f;

            // 名称文本
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(parent, false);

            RectTransform nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(textStartX, 2f);
            nameRect.offsetMax = new Vector2(-100f, -2f);

            nameText = nameObj.AddComponent<TextMeshProUGUI>();
            nameText.fontSize = 20;
            nameText.fontStyle = FontStyles.Bold;
            nameText.color = TextColor;
            nameText.alignment = TextAlignmentOptions.Left;
            nameText.raycastTarget = false;

            // 描述文本
            GameObject descObj = new GameObject("DescText");
            descObj.transform.SetParent(parent, false);

            RectTransform descRect = descObj.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0f, 0f);
            descRect.anchorMax = new Vector2(1f, 0.5f);
            descRect.offsetMin = new Vector2(textStartX, 2f);
            descRect.offsetMax = new Vector2(-100f, -2f);

            descText = descObj.AddComponent<TextMeshProUGUI>();
            descText.fontSize = 15;
            descText.color = DescColor;
            descText.alignment = TextAlignmentOptions.Left;
            descText.raycastTarget = false;
        }

        /// <summary>
        /// 创建按钮区域
        /// </summary>
        private void CreateButtonArea(Transform parent)
        {
            // 奖励文本
            GameObject rewardObj = new GameObject("RewardText");
            rewardObj.transform.SetParent(parent, false);

            RectTransform rewardRect = rewardObj.AddComponent<RectTransform>();
            rewardRect.anchorMin = new Vector2(1f, 0.5f);
            rewardRect.anchorMax = new Vector2(1f, 0.5f);
            rewardRect.pivot = new Vector2(1f, 0.5f);
            rewardRect.anchoredPosition = new Vector2(-90f, 5f);
            rewardRect.sizeDelta = new Vector2(80f, 20f);

            rewardText = rewardObj.AddComponent<TextMeshProUGUI>();
            rewardText.fontSize = 17;
            rewardText.fontStyle = FontStyles.Bold;
            rewardText.color = GoldColor;
            rewardText.alignment = TextAlignmentOptions.Right;
            rewardText.raycastTarget = false;

            // 领取按钮
            GameObject buttonObj = new GameObject("ClaimButton");
            buttonObj.transform.SetParent(parent, false);

            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-Padding, 0);
            buttonRect.sizeDelta = new Vector2(75f, 30f);

            claimButtonImage = buttonObj.AddComponent<Image>();
            claimButtonImage.color = ButtonColor;

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

            claimButtonText = buttonTextObj.AddComponent<TextMeshProUGUI>();
            claimButtonText.fontSize = 15;
            claimButtonText.fontStyle = FontStyles.Bold;
            claimButtonText.color = Color.white;
            claimButtonText.alignment = TextAlignmentOptions.Center;
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
        /// 清理资源（不再销毁图标，因为使用缓存）
        /// </summary>
        public void Cleanup()
        {
            currentIconFile = null;
        }

        #endregion

        #region 内部方法

        /// <summary>
        /// 获取带进度的描述文本（用于累计成就）
        /// </summary>
        private string GetDescriptionWithProgress(bool isChinese)
        {
            string baseDesc = isChinese ? achievement.descCN : achievement.descEN;
            
            // 累计击杀成就显示实时进度
            switch (achievement.id)
            {
                case "kill_50_bosses":
                    return baseDesc + " (" + AchievementTracker.TotalBossKills + "/50)";
                case "kill_100_bosses":
                    return baseDesc + " (" + AchievementTracker.TotalBossKills + "/100)";
                case "kill_500_bosses":
                    return baseDesc + " (" + AchievementTracker.TotalBossKills + "/500)";
                case "kill_1000_bosses":
                    return baseDesc + " (" + AchievementTracker.TotalBossKills + "/1000)";
                case "dragon_slayer_master":
                    return baseDesc + " (" + AchievementTracker.TotalDragonKingKills + "/10)";
                case "clear_10_times":
                    return baseDesc + " (" + AchievementTracker.TotalClears + "/10)";
                case "clear_50_times":
                    return baseDesc + " (" + AchievementTracker.TotalClears + "/50)";
                case "clear_100_times":
                    return baseDesc + " (" + AchievementTracker.TotalClears + "/100)";
                default:
                    return baseDesc;
            }
        }

        /// <summary>
        /// 更新视觉显示
        /// </summary>
        private void UpdateVisuals()
        {
            if (nameText == null || descText == null || rewardText == null)
            {
                Debug.LogError("[AchievementEntryUI] Text组件未初始化!");
                return;
            }

            bool isChinese = AchievementUIStrings.IsChinese();

            switch (currentState)
            {
                case AchievementEntryState.Locked:
                    backgroundImage.color = BgColorLocked;
                    borderImage.color = new Color32(45, 48, 55, 255);
                    nameText.text = isChinese ? achievement.nameCN : achievement.nameEN;
                    nameText.color = DisabledColor;
                    descText.text = GetDescriptionWithProgress(isChinese);
                    descText.color = DisabledColor;
                    rewardText.text = "$" + achievement.reward.cashReward.ToString("N0");
                    rewardText.color = DisabledColor;
                    claimButton.gameObject.SetActive(false);
                    LoadIcon(achievement.iconFile, true);
                    break;

                case AchievementEntryState.LockedHidden:
                    backgroundImage.color = BgColorLocked;
                    borderImage.color = new Color32(45, 48, 55, 255);
                    nameText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_HiddenName, AchievementUIStrings.EN_HiddenName);
                    nameText.color = DisabledColor;
                    descText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_HiddenDesc, AchievementUIStrings.EN_HiddenDesc);
                    descText.color = DisabledColor;
                    rewardText.text = "???";
                    rewardText.color = DisabledColor;
                    claimButton.gameObject.SetActive(false);
                    LoadIcon("default.png", true);
                    break;

                case AchievementEntryState.UnlockedUnclaimed:
                    backgroundImage.color = BgColorUnlocked;
                    borderImage.color = BorderColorUnlocked;
                    nameText.text = isChinese ? achievement.nameCN : achievement.nameEN;
                    nameText.color = TextColor;
                    descText.text = GetDescriptionWithProgress(isChinese);
                    descText.color = DescColor;
                    rewardText.text = "$" + achievement.reward.cashReward.ToString("N0");
                    rewardText.color = GoldColor;
                    claimButton.gameObject.SetActive(true);
                    claimButton.interactable = true;
                    claimButtonImage.color = ButtonColor;
                    claimButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Claim, AchievementUIStrings.EN_Claim);
                    LoadIcon(achievement.iconFile, false);
                    break;

                case AchievementEntryState.UnlockedClaimed:
                    backgroundImage.color = BgColor;
                    borderImage.color = BorderColor;
                    nameText.text = isChinese ? achievement.nameCN : achievement.nameEN;
                    nameText.color = TextColor;
                    descText.text = GetDescriptionWithProgress(isChinese);
                    descText.color = DescColor;
                    rewardText.text = "$" + achievement.reward.cashReward.ToString("N0");
                    rewardText.color = DisabledColor;
                    claimButton.gameObject.SetActive(true);
                    claimButton.interactable = false;
                    claimButtonImage.color = ButtonDisabledColor;
                    claimButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Claimed, AchievementUIStrings.EN_Claimed);
                    LoadIcon(achievement.iconFile, false);
                    break;
            }
        }

        private void OnClaimClicked()
        {
            TryClaim();
        }

        /// <summary>
        /// 加载成就图标（使用缓存）
        /// </summary>
        private void LoadIcon(string iconFile, bool grayscale)
        {
            try
            {
                currentIconFile = iconFile;
                Texture2D tex = GetCachedIcon(iconFile);
                
                if (tex != null)
                {
                    iconImage.texture = tex;
                    iconImage.color = grayscale ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
                }
                else
                {
                    SetDefaultIcon(grayscale);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AchievementEntryUI] Failed to load icon " + iconFile + ": " + e.Message);
                SetDefaultIcon(grayscale);
            }
        }

        private static Texture2D GetCachedIcon(string iconFile)
        {
            if (string.IsNullOrEmpty(iconFile)) return null;

            if (iconCache.ContainsKey(iconFile))
            {
                return iconCache[iconFile];
            }

            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return null;

            string iconPath = Path.Combine(modPath, "Assets", "achievement", iconFile);
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(modPath, "Assets", "achievement", "default.png");
                if (!File.Exists(iconPath)) return null;
            }

            byte[] data = File.ReadAllBytes(iconPath);
            Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            if (tex.LoadImage(data))
            {
                iconCache[iconFile] = tex;
                return tex;
            }

            UnityEngine.Object.Destroy(tex);
            return null;
        }

        private void SetDefaultIcon(bool grayscale)
        {
            iconImage.texture = null;
            iconImage.color = grayscale ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
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

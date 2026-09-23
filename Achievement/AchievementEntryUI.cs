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

        /// <summary>
        /// 清除图标缓存（委托给共享的 AchievementIconLoader）
        /// </summary>
        public static void ClearIconCache()
        {
            AchievementIconLoader.ClearCache();
        }

        #endregion

        #region UI布局常量

        public const float EntryHeight = 70f;
        public const float Padding = 8f;

        // 配色只用共享 token（审美审查 UD-35 / UD-37）。旧版是一套 Material 配色：待领取行整圈亮绿边 +
        // 一块 Material 绿 (76,175,80) 平涂按钮配白字（对比约 2.8:1），领取后直接变灰、没有任何回馈。
        // 现在：卡片一层（Card 底 + 描边），描边颜色表达状态——待领取是传说金，其余是 Stroke；
        // 领取按钮是次级按钮配金字（这一屏的主操作是页脚的「一键领取」，每行一块主色按钮会满屏抢眼）。
        private static readonly Color BgColor = BossRushUIColors.Surface;
        private static readonly Color BgColorUnlocked = BossRushUIColors.SurfaceRaised;
        private static readonly Color BgColorLocked = BossRushUIColors.Surface;
        private static readonly Color BorderColor = BossRushUIColors.Stroke;
        private static readonly Color BorderColorUnlocked = new Color(
            BossRushUIColors.RarityLegendary.r, BossRushUIColors.RarityLegendary.g, BossRushUIColors.RarityLegendary.b, 0.8f);
        private static readonly Color BorderColorLocked = new Color(
            BossRushUIColors.Stroke.r, BossRushUIColors.Stroke.g, BossRushUIColors.Stroke.b, BossRushUIColors.Stroke.a * 0.5f);
        private static readonly Color TextColor = BossRushUIColors.TextPrimary;
        private static readonly Color DescColor = BossRushUIColors.TextSecondary;
        private static readonly Color DisabledColor = new Color(
            BossRushUIColors.TextSecondary.r, BossRushUIColors.TextSecondary.g, BossRushUIColors.TextSecondary.b, 0.7f);
        private static readonly Color GoldColor = BossRushUIColors.WarningText;
        /// <summary>领取反馈：描边从金色淡回 Stroke 的时长；奖励数字从 1.2 倍回弹的时长。</summary>
        private const float ClaimStrokeFadeSeconds = 0.5f;
        private const float ClaimPopSeconds = 0.2f;
        private const float IconSize = 60f;
        private const float IconTextGap = 12f;

        #endregion

        #region UI组件引用

        private Image backgroundImage;
        /// <summary>卡片描边（ApplyPanelStroke 返回的那层环），颜色表达状态。</summary>
        private Image borderImage;
        private GameObject iconContainer;
        private RectTransform nameRect;
        private RectTransform descRect;
        private Coroutine claimFeedback;
        private RawImage iconImage;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI descText;
        private TextMeshProUGUI rewardText;
        private Button claimButton;
        private TextMeshProUGUI claimButtonText;

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
                ModBehaviour.DevLog("[AchievementEntryUI] Create called with null definition");
                return null;
            }

            // 创建条目容器
            GameObject entryObj = new GameObject("AchievementEntry_" + def.id);
            entryObj.transform.SetParent(parent, false);

            // 添加LayoutElement来指定高度（关键！）
            LayoutElement layoutElement = entryObj.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = EntryHeight;
            layoutElement.flexibleWidth = 1f;

            // 卡片：一层 Card 底 + 共享描边（投影与顶边高光随描边一起来）。
            // 旧版是「外层 2px 色块当边框 + 内层再套一张卡」的卡套卡（审美审查 UD-35 / U2），
            // 状态色改由描边环本身表达。
            Image bgImage = entryObj.AddComponent<Image>();
            bgImage.color = BgColorUnlocked;   // 先给不透明底色：投影 / 斜面按底色不透明度决定挂不挂
            BossRushUI.ApplyPanelSkin(bgImage, 10, BossRushUISkinPart.Card);
            Image stroke = BossRushUI.ApplyPanelStroke(bgImage, 10, BossRushUISkinPart.Card, BorderColor);

            // 内边距容器（只负责布局，不再铺第二层底）
            GameObject innerObj = new GameObject("Inner");
            innerObj.transform.SetParent(entryObj.transform, false);

            RectTransform innerRect = innerObj.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(2f, 2f);
            innerRect.offsetMax = new Vector2(-2f, -2f);

            AchievementEntryUI entry = entryObj.AddComponent<AchievementEntryUI>();
            entry.backgroundImage = bgImage;
            entry.borderImage = stroke;
            entry.InitializeComponents(innerObj.transform);
            entry.Setup(def);

            return entry;
        }

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeComponents(Transform parent)
        {
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
            iconContainer = new GameObject("IconContainer");
            iconContainer.transform.SetParent(parent, false);

            RectTransform containerRect = iconContainer.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 0.5f);
            containerRect.anchorMax = new Vector2(0f, 0.5f);
            containerRect.pivot = new Vector2(0f, 0.5f);
            containerRect.anchoredPosition = new Vector2(Padding, 0);
            containerRect.sizeDelta = new Vector2(IconSize, IconSize);

            // 图标底：圆角凹槽（Surface，比卡片底更深一档），不再是圆角卡片里嵌一个直角黑方块（UD-37）
            Image iconBg = iconContainer.AddComponent<Image>();
            iconBg.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(iconBg, 8, BossRushUISkinPart.Card);
            iconBg.raycastTarget = false;

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
            iconImage.raycastTarget = false;
        }

        /// <summary>文字列的左边线：有图标时让出图标列，图标取不到时贴左（不留一块灰方块占位，UD-37）。</summary>
        private static float GetTextStartX(bool hasIcon)
        {
            return hasIcon ? Padding + IconSize + IconTextGap : Padding + 6f;
        }

        /// <summary>
        /// 创建文本区域
        /// </summary>
        private void CreateTextArea(Transform parent)
        {
            float textStartX = GetTextStartX(true);

            // 名称文本
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(parent, false);

            nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(textStartX, 2f);
            // 右侧让出「奖励金额 + 领取按钮」一整列，长名字不再压到金额上
            nameRect.offsetMax = new Vector2(-180f, -2f);

            nameText = nameObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(nameText);
            nameText.fontSize = 18;
            nameText.fontStyle = FontStyles.Bold;
            nameText.color = TextColor;
            nameText.alignment = TextAlignmentOptions.BottomLeft;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.raycastTarget = false;

            // 描述文本
            GameObject descObj = new GameObject("DescText");
            descObj.transform.SetParent(parent, false);

            descRect = descObj.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0f, 0f);
            descRect.anchorMax = new Vector2(1f, 0.5f);
            descRect.offsetMin = new Vector2(textStartX, 2f);
            descRect.offsetMax = new Vector2(-180f, -2f);

            descText = descObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(descText);
            descText.fontSize = 15;
            descText.color = DescColor;
            descText.alignment = TextAlignmentOptions.TopLeft;
            descText.raycastTarget = false;
        }

        /// <summary>图标列显隐与文字左边线一起切。</summary>
        private void SetIconVisible(bool visible)
        {
            if (iconContainer != null && iconContainer.activeSelf != visible)
            {
                iconContainer.SetActive(visible);
            }
            float x = GetTextStartX(visible);
            if (nameRect != null) nameRect.offsetMin = new Vector2(x, nameRect.offsetMin.y);
            if (descRect != null) descRect.offsetMin = new Vector2(x, descRect.offsetMin.y);
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
            rewardRect.anchoredPosition = new Vector2(-90f, 0f);
            // 17 号单行框高按 字号×1.45+4 给足（旧值 20 装不下一行）
            rewardRect.sizeDelta = new Vector2(80f, 30f);

            rewardText = rewardObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(rewardText);
            rewardText.fontSize = 17;
            rewardText.fontStyle = FontStyles.Bold;
            rewardText.color = GoldColor;
            rewardText.alignment = TextAlignmentOptions.Right;
            rewardText.enableWordWrapping = false;
            rewardText.raycastTarget = false;

            // 领取按钮：共享按钮 + 次级样式（三态、官方悬停 / 点击音效、按下回弹都由共享层给，UD-35）。
            // 旧版手搓 AddComponent<Button>、默认 ColorBlock，悬停只乘 0.96，看不出能点。
            claimButton = ZombieModeUIHelper.CreateButton(
                "ClaimButton",
                parent,
                string.Empty,
                new Vector2(1f, 0.5f),
                new Vector2(-Padding - 37.5f, 0f),
                new Vector2(75f, 30f),
                BossRushUIColors.SurfaceRaised,
                15f,
                new Vector2(71f, 26f),
                OnClaimClicked,
                true);
            BossRushUIKit.StyleSecondaryButton(claimButton);
            claimButtonText = claimButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (claimButtonText != null)
            {
                claimButtonText.fontStyle = FontStyles.Bold;
            }
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
                ModBehaviour.DevLog("[AchievementEntryUI] Setup called with null definition");
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
                ModBehaviour.LogError("[AchievementEntryUI] Text组件未初始化!");
                return;
            }

            bool isChinese = AchievementUIStrings.IsChinese();

            switch (currentState)
            {
                case AchievementEntryState.Locked:
                    backgroundImage.color = BgColorLocked;
                    borderImage.color = BorderColorLocked;
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
                    borderImage.color = BorderColorLocked;
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
                    // 底色住在 ColorBlock 里（StyleSecondaryButton），这里只切可点状态与字色，不写 Image.color
                    if (claimButtonText != null)
                    {
                        claimButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Claim, AchievementUIStrings.EN_Claim);
                        claimButtonText.color = GoldColor;
                    }
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
                    if (claimButtonText != null)
                    {
                        claimButtonText.text = AchievementUIStrings.GetText(AchievementUIStrings.CN_Claimed, AchievementUIStrings.EN_Claimed);
                        claimButtonText.color = DisabledColor;
                    }
                    LoadIcon(achievement.iconFile, false);
                    break;
            }
        }

        private void OnClaimClicked()
        {
            TryClaim();
        }

        /// <summary>
        /// 领取成功的那一拍（审美审查 UD-35）：金额数字从 1.2 倍回弹、字色从金色淡到「已领取」的灰，
        /// 卡片描边从传说金淡回 Stroke。音效由 AchievementView 播（一键领取整批只播一次）。
        /// 走 unscaled 时间并过暂停门；在 Refresh 之后调用，终点就是新状态的颜色。
        /// </summary>
        internal void PlayClaimFeedback()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            if (claimFeedback != null)
            {
                StopCoroutine(claimFeedback);
            }
            claimFeedback = StartCoroutine(ClaimFeedbackRoutine());
        }

        private System.Collections.IEnumerator ClaimFeedbackRoutine()
        {
            Color strokeTo = borderImage != null ? borderImage.color : BorderColor;
            Color rewardTo = rewardText != null ? rewardText.color : DisabledColor;
            RectTransform rewardRect = rewardText != null ? rewardText.rectTransform : null;
            float elapsed = 0f;
            while (elapsed < ClaimStrokeFadeSeconds)
            {
                if (!BossRushUI.IsGamePaused())
                {
                    elapsed += Time.unscaledDeltaTime;
                }
                float fade = BossRushUI.SmoothStep(elapsed / ClaimStrokeFadeSeconds);
                if (borderImage != null)
                {
                    borderImage.color = Color.Lerp(BorderColorUnlocked, strokeTo, fade);
                }
                if (rewardText != null)
                {
                    rewardText.color = Color.Lerp(GoldColor, rewardTo, fade);
                }
                if (rewardRect != null)
                {
                    rewardRect.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, BossRushUI.EaseOut(elapsed / ClaimPopSeconds));
                }
                yield return null;
            }
            if (borderImage != null) borderImage.color = strokeTo;
            if (rewardText != null) rewardText.color = rewardTo;
            if (rewardRect != null) rewardRect.localScale = Vector3.one;
            claimFeedback = null;
        }

        private void OnDisable()
        {
            // 动画中途被整页重建 / 关面板：直接落到终态，别停在放大的样子
            if (claimFeedback != null)
            {
                StopCoroutine(claimFeedback);
                claimFeedback = null;
                if (rewardText != null) rewardText.rectTransform.localScale = Vector3.one;
                UpdateVisuals();
            }
        }

        /// <summary>
        /// 加载成就图标（使用共享的 AchievementIconLoader）
        /// </summary>
        private void LoadIcon(string iconFile, bool grayscale)
        {
            try
            {
                currentIconFile = iconFile;
                string iconName = !string.IsNullOrEmpty(iconFile) ? Path.GetFileNameWithoutExtension(iconFile) : null;
                
                Texture2D tex = AchievementIconLoader.GetTexture(iconName);
                
                // 回退到默认图标
                if (tex == null)
                {
                    tex = AchievementIconLoader.GetTexture("default");
                }
                
                if (tex != null)
                {
                    SetIconVisible(true);
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
                ModBehaviour.DevLog("[AchievementEntryUI] Failed to load icon " + iconFile + ": " + e.Message);
                SetDefaultIcon(grayscale);
            }
        }

        /// <summary>
        /// 指定图标与 default 图标都取不到：去掉图标那一格、文字贴左，
        /// 不再画一块灰方块占位（审美审查 UD-37，owner 点名过「灰方块占位图标」）。
        /// </summary>
        private void SetDefaultIcon(bool grayscale)
        {
            iconImage.texture = null;
            SetIconVisible(false);
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

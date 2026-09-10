// ============================================================================
// SkyIslandHud.cs - 天空岛局内指引
// ============================================================================
// 【为什么不再在屏幕正上方写字】
//   旧版直接复用 `ArenaPrototypeControls.CreateHud`：屏幕正中偏上一块 920×90 的裸文字，
//   常驻四行（地名 / 目标 / 物资与委托 / 「地图键查阅全岛 · 站进撤离环停留 3 秒返航 · 保存状态」）。
//   那是**原型期的调试文本**，不是给玩家看的指引：正上方是视线焦点，常驻长句会一直抢注意力，
//   而且实测中文要 4 行 100 px、英文 6 行 150 px，90 px 的框根本装不下（超出部分直接画到框外）。
//
// 【现在的口径，照主流游戏的做法分三层】
//   1. **常驻只留一张小卡**，贴右边缘（与 `CampaignHud` 同一套视觉语言与同一列），
//      只放「当前区域 + 当前目标 + 两枚进度小标」。宽 320，高按内容实测。
//   2. **区域名做成进出时的一次性大标题**（淡入 → 停留 → 淡出），而不是常驻一行。
//      这是 AAA 里「进入新区域」的通用手法：给足仪式感，然后**消失**。
//   3. **其余全部按需出现**：撤离读秒只在站进环里时出现，公告只停留几秒，
//      存档状态**正常时完全不显示**——只有出问题才说话。静默是高级感的一部分。
//
// 【避让】
//   左上角是随机事件徽章与波次提示（见 `CampaignHud` 的同款注释），所以走右边缘。
//   右上角 y=-110 是 `CampaignHud` 的位置，而契约**可能**在岛上仍处于武装状态，
//   因此本卡片按 `CampaignObjectiveTracker.IsArmed` 动态下移一档，不与它叠。
//
// UI 硬约束（AGENTS 4.14）：Canvas 走 `BossRushUI.CreateCanvasRoot(..., interactive:false)`，
// sortingOrder 用常量，颜色只用 token，文本一律 TMP + 共享字体，所有 Graphic 的
// raycastTarget 置 false —— HUD 必须让点击穿透。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>天空岛局内指引。非交互，点击必须穿透。会话独占一个实例。</summary>
    internal sealed class SkyIslandHud : IDisposable
    {
        #region 版式常量

        private const float CardWidth = 320f;
        private const float CardRight = -24f;
        /// <summary>与 `CampaignHud` 同一个起始 y；契约在武装时本卡下移 CardStackOffset。</summary>
        private const float CardTop = -110f;
        private const float CardStackOffset = -112f;
        private const float CardPadX = 14f;
        private const float CardPadY = 12f;
        private const float AccentBarWidth = 3f;

        private const float TitleFont = 15f;
        private const float BodyFont = 13f;
        private const float ChipFont = 12f;

        /// <summary>
        /// 区域大标题相对屏幕中心的 y。**负数 = 中线偏下**，这是刻意的：
        /// 屏幕中上方是视线焦点，往那儿贴字正是要甩掉的网游味。
        /// 离线预览（tools/preview_sky_island_panel.py）读的就是这个常量，别改成字面量。
        /// </summary>
        private const float AreaTitleY = -196f;

        /// <summary>区域大标题的淡入 / 停留 / 淡出秒数。</summary>
        private const float BannerFadeIn = 0.35f;
        private const float BannerHold = 2.1f;
        private const float BannerFadeOut = 0.9f;

        /// <summary>公告停留秒数。超过就淡掉，不常驻。</summary>
        private const float NoticeHold = 4.5f;
        private const float NoticeFade = 0.6f;

        #endregion

        private Canvas canvas;
        private GameObject card;
        private RectTransform cardRect;
        private TextMeshProUGUI regionText, objectiveText, chipText, extractionText, noticeText;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerTitle, bannerHint;

        private string region = string.Empty, objective = string.Empty, chips = string.Empty;
        private string extraction, notice;
        private float bannerAge = -1f, noticeAge = -1f;
        private bool stacked;

        internal SkyIslandHud(Transform owner)
        {
            canvas = BossRushUI.CreateCanvasRoot("SkyIslandHud", BossRushUILayers.HudOverlay, false);
            if (owner != null) canvas.transform.SetParent(owner, false);
            BuildCard();
            BuildBanner();
            Apply();
        }

        #region 构建

        private void BuildCard()
        {
            card = ZombieModeUIHelper.CreateRect("SkyIslandTracker", canvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(CardRight, CardTop), new Vector2(CardWidth, 96f), new Vector2(1f, 1f));
            cardRect = card.GetComponent<RectTransform>();
            Image background = card.AddComponent<Image>();
            background.color = BossRushUIColors.Surface;
            background.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(background, 10);

            // 左侧的一条 accent 竖线：只有 3 px，但它把「这是一块有主的信息」说清楚了。
            GameObject bar = ZombieModeUIHelper.CreateRect("AccentBar", card.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(AccentBarWidth * 0.5f + 4f, 0f), new Vector2(AccentBarWidth, -16f),
                new Vector2(0.5f, 0.5f));
            Image barImage = bar.AddComponent<Image>();
            barImage.color = BossRushUIColors.Accent;
            barImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(barImage, 2);

            regionText = Text("Region", card.transform, TitleFont, BossRushUIColors.Accent,
                TextAlignmentOptions.Left);
            objectiveText = Text("Objective", card.transform, BodyFont, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.TopLeft);
            chipText = Text("Chips", card.transform, ChipFont, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.Left);
            extractionText = Text("Extraction", card.transform, TitleFont, BossRushUIColors.SuccessText,
                TextAlignmentOptions.Left);
            noticeText = Text("Notice", card.transform, BodyFont, BossRushUIColors.WarningText,
                TextAlignmentOptions.TopLeft);
        }

        private void BuildBanner()
        {
            // 放在**中线偏下**，不放屏幕中上方。中上方是视线焦点，往那儿贴字正是要甩掉的
            // 那种网游味；区域名这类仪式感元素在主流游戏里也基本都在下半屏（RDR2 左下、
            // 对马岛与老头环中下），既看得见又不挡正前方的战斗视野。
            GameObject root = ZombieModeUIHelper.CreateRect("SkyIslandAreaTitle", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, AreaTitleY), new Vector2(900f, 120f), new Vector2(0.5f, 0.5f));
            bannerGroup = root.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;

            // 压暗底垫在文字下面。岛上抬头是一片高亮云海，浅色字直接压上去几乎读不出来。
            // 上下对称渐变，没有硬边，看不出贴了块板。
            Sprite scrim = SkyIslandUiArt.GetTitleScrim();
            if (scrim != null)
            {
                GameObject shade = ZombieModeUIHelper.CreateRect("Scrim", root.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(1180f, 200f), new Vector2(0.5f, 0.5f));
                Image shadeImage = shade.AddComponent<Image>();
                shadeImage.sprite = scrim;
                shadeImage.type = Image.Type.Simple;
                shadeImage.raycastTarget = false;
            }

            bannerTitle = ZombieModeUIHelper.CreateText("Title", root.transform, string.Empty, 44f,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -34f), new Vector2(0f, 60f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            bannerTitle.raycastTarget = false;
            BossRushUI.ApplyGameFont(bannerTitle);

            // 细分隔线：大标题下方一道短横，是「区域名」这类仪式感元素的通用写法。
            GameObject rule = ZombieModeUIHelper.CreateRect("Rule", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(120f, 1f), new Vector2(0.5f, 0.5f));
            Image ruleImage = rule.AddComponent<Image>();
            ruleImage.color = BossRushUIColors.Divider;
            ruleImage.raycastTarget = false;

            bannerHint = ZombieModeUIHelper.CreateText("Hint", root.transform, string.Empty, 15f,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -88f), new Vector2(0f, 28f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            bannerHint.raycastTarget = false;
            BossRushUI.ApplyGameFont(bannerHint);
        }

        private static TextMeshProUGUI Text(string name, Transform parent, float size, Color color,
            TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(name, parent, string.Empty, size,
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero,
                new Vector2(-(CardPadX * 2f + AccentBarWidth + 6f), 20f), alignment, color);
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            // HUD 是固定框，绝不能用 Overflow：超出的行会直接画到卡片外面
            // （`BossRushUI.MeasureTextHeight` 的注释也明说这一点）。这里靠 Apply() 实测高度，
            // 万一还是不够就省略号收尾，不越界。
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        #endregion

        #region 对外写入

        /// <summary>当前区域。变化时触发一次区域大标题。</summary>
        internal void SetRegion(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(region, value, StringComparison.Ordinal)) return;
            region = value;
            if (!string.IsNullOrEmpty(value)) bannerAge = 0f;
            Apply();
        }

        internal void SetObjective(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(objective, value, StringComparison.Ordinal)) return;
            objective = value;
            Apply();
        }

        internal void SetChips(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(chips, value, StringComparison.Ordinal)) return;
            chips = value;
            Apply();
        }

        /// <summary>撤离读秒。传 null 表示不在圈里——那一行整条消失，不留空位。</summary>
        internal void SetExtraction(string value)
        {
            if (string.Equals(extraction, value, StringComparison.Ordinal)) return;
            extraction = value;
            Apply();
        }

        /// <summary>一次性公告。停留几秒后自己淡掉，不常驻。</summary>
        internal void Announce(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            notice = value;
            noticeAge = 0f;
            Apply();
        }

        /// <summary>落地时的一次性操作提示，挂在区域大标题下面，跟着它一起淡出。</summary>
        internal void SetLandingHint(string value)
        {
            if (bannerHint != null) bannerHint.text = value ?? string.Empty;
        }

        #endregion

        /// <summary>由会话每帧驱动。只推进淡入淡出与过期，不重排版式。</summary>
        internal void Tick(float unscaledDelta)
        {
            if (canvas == null) return;

            if (bannerAge >= 0f)
            {
                bannerAge += unscaledDelta;
                float total = BannerFadeIn + BannerHold + BannerFadeOut;
                if (bannerAge >= total)
                {
                    bannerAge = -1f;
                    if (bannerGroup != null) bannerGroup.alpha = 0f;
                }
                else if (bannerGroup != null)
                {
                    bannerGroup.alpha = bannerAge < BannerFadeIn
                        ? bannerAge / BannerFadeIn
                        : (bannerAge < BannerFadeIn + BannerHold
                            ? 1f
                            : 1f - (bannerAge - BannerFadeIn - BannerHold) / BannerFadeOut);
                }
            }

            if (noticeAge >= 0f)
            {
                noticeAge += unscaledDelta;
                if (noticeAge >= NoticeHold + NoticeFade)
                {
                    noticeAge = -1f;
                    notice = null;
                    Apply();
                }
                else if (noticeText != null)
                {
                    float alpha = noticeAge < NoticeHold
                        ? 1f
                        : 1f - (noticeAge - NoticeHold) / NoticeFade;
                    Color c = noticeText.color;
                    noticeText.color = new Color(c.r, c.g, c.b, alpha);
                }
            }

            // 契约 HUD 随时可能出现/消失，卡片跟着让位，避免两块叠在同一列。
            bool campaignArmed = false;
            try { campaignArmed = CampaignObjectiveTracker.IsArmed; }
            catch (Exception) { /* 契约系统不可用时按未武装处理 */ }
            if (campaignArmed != stacked)
            {
                stacked = campaignArmed;
                if (cardRect != null)
                    cardRect.anchoredPosition = new Vector2(CardRight,
                        CardTop + (stacked ? CardStackOffset : 0f));
            }
        }

        /// <summary>
        /// 重排卡片：**先量后排**，卡片高度由实际内容决定。
        /// 只在内容真的变化时调用（写入方法自己短路），不是每帧路径。
        /// </summary>
        private void Apply()
        {
            if (card == null) return;
            float width = CardWidth - CardPadX * 2f - AccentBarWidth - 6f;
            float left = CardPadX + AccentBarWidth + 6f;
            float y = -CardPadY;

            y -= Row(regionText, region, width, left, y, TitleFont);
            y -= Row(objectiveText, objective, width, left, y, BodyFont);
            y -= Row(chipText, chips, width, left, y, ChipFont);
            y -= Row(extractionText, extraction, width, left, y, TitleFont);
            if (noticeText != null && !string.IsNullOrEmpty(notice))
            {
                Color c = BossRushUIColors.WarningText;
                noticeText.color = new Color(c.r, c.g, c.b, 1f);
            }
            y -= Row(noticeText, notice, width, left, y, BodyFont);

            if (bannerTitle != null) bannerTitle.text = region;
            // 一行都没有就整张卡片收掉。装配期间（还没就绪、什么都没得说）留一块空底板在那儿，
            // 恰恰是「廉价感」的来源之一：没内容就不该有框。
            bool anything = !string.IsNullOrEmpty(region) || !string.IsNullOrEmpty(objective)
                || !string.IsNullOrEmpty(chips) || !string.IsNullOrEmpty(extraction)
                || !string.IsNullOrEmpty(notice);
            if (card.activeSelf != anything) card.SetActive(anything);
            cardRect.sizeDelta = new Vector2(CardWidth, Mathf.Max(48f, -y + CardPadY));
        }

        /// <summary>摆一行并返回它占掉的高度（含行距）。内容为空时整行隐藏、不占位。</summary>
        private static float Row(TextMeshProUGUI text, string value, float width, float left,
            float top, float font)
        {
            if (text == null) return 0f;
            if (string.IsNullOrEmpty(value))
            {
                if (text.gameObject.activeSelf) text.gameObject.SetActive(false);
                return 0f;
            }
            if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
            text.text = value;
            float height = Mathf.Max(font * 1.3f,
                Mathf.Ceil(text.GetPreferredValues(value, width, float.PositiveInfinity).y) + 2f);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, top);
            return height + 4f;
        }

        public void Dispose()
        {
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            card = null;
            cardRect = null;
            regionText = objectiveText = chipText = extractionText = noticeText = null;
            bannerGroup = null;
            bannerTitle = bannerHint = null;
        }
    }
}

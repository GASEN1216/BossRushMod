// ============================================================================
// ModeHUIPageParts.cs - Mode H 模态页的构件与样式（ModeHUIPages 的分部）
// ============================================================================
// 2026-09-23 审美打磨（审查 UB-01/03/04/09/11）新加的构件放在这里：页头横幅与胜负、整卡可点、
// 两栏键值行、整备选项卡片、按钮主次样式。拆文件只为单文件 1200 行预算（LargeFileBudgetGuard）；
// 守卫逐字钉住的页面骨架（Build、各页排版、动作带、押品滚动区）仍在 ModeHUIPages.cs。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>结算页的胜负色调（决定横幅描边与大字颜色）。</summary>
    internal enum ModeHResultTone
    {
        /// <summary>不是结算页，或结算记录不可用。</summary>
        None = 0,
        /// <summary>本场胜利。</summary>
        Victory = 1,
        /// <summary>本场失利（含超时判负、整队弃赛）。</summary>
        Defeat = 2
    }

    internal static partial class ModeHUIPages
    {
        private static bool TryCreateHeroHeader(Transform surface, Vector2 panelSize, ModeHPageContent content,
            bool result, out float contentTop)
        {
            contentTop = 0f;
            Sprite banner = ModeHPresentationAssetCache.GetBannerSprite();
            if (banner == null) return false;

            Color tone = ResolveResultColor(content.ResultTone);
            float height = result ? ResultHeroHeight : EntryHeroHeight;
            Vector2 size = new Vector2(panelSize.x - HeroInset * 2f, height);
            RectTransform hero = BossRushUIHero.CreateBanner(surface, "ModeH_Hero", banner,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -HeroInset), size,
                HeroRadius, HeroFocusY, result ? tone : BossRushUIColors.Stroke);
            if (hero == null) return false;

            float rowY = result ? -10f : 0f;
            TextMeshProUGUI title = CreateHeroText(hero, "ModeH_Title", result ? content.Body : content.Title,
                result ? ResultFontSize : ModeHUI.TitleFontSize, result ? tone : BossRushUIColors.TextPrimary, size.x - 48f);
            RectTransform emblem = BossRushUIHero.CreateEmblem(hero, "ModeH_TitleEmblem",
                ModeHPresentationAssetCache.GetEmblemSprite(), result ? 56f : 48f);
            BossRushUIHero.PlaceTitleRow(title, emblem, 14f, size.x - 96f, new Vector2(0f, rowY));
            if (result)
            {
                TextMeshProUGUI caption = CreateHeroText(hero, "ModeH_TitleCaption", content.Title, 16f,
                    BossRushUIColors.TextSecondary, size.x - 48f);
                caption.rectTransform.anchoredPosition = new Vector2(0f, rowY + 46f);
                if (!content.Refresh)
                {
                    // 胜负是这一页的主角：稍晚半拍升起来，读起来像「揭晓」
                    BossRushUIEntranceAnimation.Play(title.gameObject, 0.1f, 0.3f, 10f);
                    if (emblem != null) BossRushUIEntranceAnimation.Play(emblem.gameObject, 0.06f, 0.3f, 10f);
                }
            }
            contentTop = panelSize.y * 0.5f - HeroInset - height - 16f;
            return true;
        }

        /// <summary>压在插图上的一行字：单行不缩字；TMP 距离场描边 + 底影（共享材质），亮暗背景都读得出。</summary>
        private static TextMeshProUGUI CreateHeroText(Transform parent, string name, string value, float fontSize,
            Color color, float width)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, Mathf.Ceil(fontSize * 1.45f) + 8f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, value != null ? value : string.Empty, fontSize, TextAlignmentOptions.Center, color);
            BossRushUI.ApplyGameFont(text);
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            BossRushUIKit.ApplyWorldTextOutline(text);
            return text;
        }

        /// <summary>横幅取不到时的胜负卡：深色卡 + 胜负色竖条 + 胜负色大字。返回卡片下沿再往下一个间距。</summary>
        private static float CreateResultCard(Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            Color tone = ResolveResultColor(content.ResultTone);
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            GameObject card = BossRushUI.CreateCard("ModeH_Result", surface,
                new Vector2(0f, topY - ResultCardHeight * 0.5f), new Vector2(width, ResultCardHeight),
                BossRushUIColors.SurfaceRaised, tone, true);
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText("Text", card.transform,
                content.Body != null ? content.Body : string.Empty, 36f, Vector2.zero,
                new Vector2(width - 48f, ResultCardHeight - 8f), TextAlignmentOptions.Center, tone);
            text.enableAutoSizing = false;
            BossRushUI.ApplyGameFont(text);
            if (!content.Refresh) BossRushUIEntranceAnimation.Play(card, 0f, 0.28f, 12f);
            return topY - ResultCardHeight - 16f;
        }

        private static Color ResolveResultColor(ModeHResultTone tone)
        {
            if (tone == ModeHResultTone.Victory) return BossRushUIColors.SuccessText;
            if (tone == ModeHResultTone.Defeat) return BossRushUIColors.DangerText;
            return BossRushUIColors.TextPrimary;
        }

        /// <summary>
        /// 整张卡当按钮：底图就是按钮图（ColorBlock 绝对色，悬停向描边色提亮一点），挂共享手感（音效、按下回弹）；
        /// 悬停时描边换主色（ModeHCardHover）。卡里的「选他出战」是子按钮，点它只触发它自己。
        /// </summary>
        private static void MakeCardClickable(Transform card, UnityEngine.Events.UnityAction onClick)
        {
            Image image = card.GetComponent<Image>();
            if (image == null || onClick == null) return;
            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Color normal = BossRushUIColors.SurfaceRaised;
            Color hover = Color.Lerp(normal, BossRushUIColors.Stroke, CardHoverLift);
            hover.a = normal.a;
            ZombieModeUIHelper.ApplyButtonColors(button, normal, hover, BossRushUI.GetDisabledColor(normal));
            button.onClick.AddListener(onClick);
            Transform stroke = card.Find("Stroke");
            if (stroke != null)
            {
                card.gameObject.AddComponent<ModeHCardHover>().Bind(button, stroke.GetComponent<Image>());
            }
        }

        private static TextMeshProUGUI CreateOddsText(Transform surface, string name, string value, float fontSize,
            Color color, float width, float centerY, float height)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, surface, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, centerY), new Vector2(width, height), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, value != null ? value : string.Empty, fontSize, TextAlignmentOptions.Center, color);
            BossRushUI.ApplyGameFont(text);
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static TextMeshProUGUI CreateRowText(Transform row, string name, string value, float fontSize,
            Color color, float left, float top, float width)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, row, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(left, top), new Vector2(width, LineHeight), new Vector2(0f, 1f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, value != null ? value : string.Empty, fontSize, TextAlignmentOptions.TopLeft, color);
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        /// <summary>
        /// 「键：值」拆分：先认全角冒号，再认英文「: 」，最后认两个空格（赔率拆解「公开分差  +3」）。
        /// 键太长（超过 18 个字）说明这是一句话而不是键值，按段落排。
        /// </summary>
        private static bool SplitKeyValue(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (string.IsNullOrEmpty(line) || line.IndexOf('\n') >= 0) return false;
            int cut = line.IndexOf('：');
            int skip = 1;
            if (cut < 0)
            {
                cut = line.IndexOf(": ", StringComparison.Ordinal);
                skip = 2;
            }
            if (cut < 0)
            {
                cut = line.LastIndexOf("  ", StringComparison.Ordinal);
                skip = 2;
            }
            if (cut <= 0 || cut > KeyMaxChars) return false;
            key = line.Substring(0, cut).Trim();
            value = line.Substring(cut + skip).Trim();
            return key.Length > 0 && value.Length > 0;
        }

        private static void CreatePreparationRow(Transform host, ModeHActionData option, int index, float width,
            float rowHeight, bool animate)
        {
            if (option == null) return;
            string label = option.Label ?? string.Empty;
            int split = label.IndexOf('\n');
            string title = split >= 0 ? label.Substring(0, split) : label;
            string detail = split >= 0 ? label.Substring(split + 1) : null;

            Button row = ZombieModeUIHelper.CreateButton("ModeH_Preparation_" + index, host, title,
                new Vector2(0.5f, 1f), new Vector2(0f, -(index + 0.5f) * rowHeight),
                new Vector2(width, rowHeight - 8f), BossRushUIColors.SurfaceRaised,
                19f, new Vector2(width - 40f, PrepTitleHeight),
                option.OnClick != null ? new UnityEngine.Events.UnityAction(option.OnClick) : null,
                option.Interactable);
            float right = 20f;
            if (option.IsSelected)
            {
                StyleSelectedButton(row, BossRushUIColors.Accent, BossRushUIColors.Accent);
                right += AddSelectedBadge(row.transform) + 12f;
            }
            else
            {
                BossRushUIKit.StyleSecondaryButton(row);
            }

            TextMeshProUGUI titleText = FindLabel(row);
            if (titleText != null)
            {
                RectTransform titleRect = titleText.rectTransform;
                titleText.enableAutoSizing = false;
                titleText.enableWordWrapping = false;
                titleText.overflowMode = TextOverflowModes.Ellipsis;
                titleText.color = option.Interactable ? BossRushUIColors.TextPrimary : BossRushUIColors.TextSecondary;
                if (string.IsNullOrEmpty(detail))
                {
                    titleText.alignment = TextAlignmentOptions.MidlineLeft;
                    titleRect.anchorMin = Vector2.zero;
                    titleRect.anchorMax = Vector2.one;
                    titleRect.offsetMin = new Vector2(20f, 0f);
                    titleRect.offsetMax = new Vector2(-right, 0f);
                }
                else
                {
                    titleText.alignment = TextAlignmentOptions.TopLeft;
                    titleRect.anchorMin = new Vector2(0f, 1f);
                    titleRect.anchorMax = new Vector2(1f, 1f);
                    titleRect.offsetMin = new Vector2(20f, -(14f + PrepTitleHeight));
                    titleRect.offsetMax = new Vector2(-right, -14f);
                }
            }

            if (!string.IsNullOrEmpty(detail))
            {
                GameObject descObj = ZombieModeUIHelper.CreateRect("Desc", row.transform,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
                RectTransform descRect = descObj.GetComponent<RectTransform>();
                descRect.offsetMin = new Vector2(20f, 10f);
                descRect.offsetMax = new Vector2(-20f, -(18f + PrepTitleHeight));
                TextMeshProUGUI desc = ZombieModeUIHelper.CreateTMPText(descObj, detail, 15f,
                    TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(desc);
            }
            if (animate && index < MaxAnimatedRows)
            {
                BossRushUIEntranceAnimation.Play(row.gameObject, 0.04f * index, 0.24f, 10f);
            }
        }

        /// <summary>「√ 已选」角标：AccentFill 小底 + 按底色取字色，宽度按字量。返回角标宽度。</summary>
        private static float AddSelectedBadge(Transform row)
        {
            string text = L10n.T("√ 已选", "√ Selected");
            GameObject badge = ZombieModeUIHelper.CreateRect("SelectedBadge", row,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f),
                new Vector2(80f, SelectedBadgeHeight), new Vector2(1f, 1f));
            Image fill = badge.AddComponent<Image>();
            fill.color = BossRushUIColors.AccentFill;
            fill.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(fill, 8, BossRushUISkinPart.Button);
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Label", badge.transform, text, 13f,
                Vector2.zero, new Vector2(80f, SelectedBadgeHeight), TextAlignmentOptions.Center,
                BossRushUI.GetButtonTextColor(BossRushUIColors.AccentFill));
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.margin = Vector4.zero;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            float badgeWidth = Mathf.Ceil(label.GetPreferredValues(text).x) + 16f;
            badge.GetComponent<RectTransform>().sizeDelta = new Vector2(badgeWidth, SelectedBadgeHeight);
            return badgeWidth;
        }

        /// <summary>显式标了主操作就用它；都没标时，唯一一颗可点的非危险、非选中按钮自动当主操作；否则没有主按钮。</summary>
        internal static int ResolvePrimaryAction(IList<ModeHActionData> actions)
        {
            int flagged = -1;
            int forward = -1;
            int forwardCount = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                ModeHActionData action = actions[i];
                if (action == null) continue;
                if (action.IsPrimary && flagged < 0) flagged = i;
                if (action.Interactable && !action.IsDanger && !action.IsSelected)
                {
                    forwardCount++;
                    forward = i;
                }
            }
            if (flagged >= 0) return flagged;
            return forwardCount == 1 ? forward : -1;
        }

        internal static Color ResolveActionFill(ModeHActionData action, bool primary)
        {
            if (action == null || !action.Interactable) return BossRushUIColors.SurfaceRaised;
            if (action.IsDanger) return BossRushUIColors.Danger;
            if (primary) return BossRushUIColors.AccentFill;
            return BossRushUIColors.SurfaceRaised;
        }

        /// <summary>主 / 危险按钮保持实心；选中档染主色；其余次级（不可点的字换次级色）。恢复壳的动作行同一口径。</summary>
        internal static void StyleAction(Button button, ModeHActionData action, bool primary)
        {
            if (button == null || action == null) return;
            if (action.Interactable && (action.IsDanger || primary)) return;
            if (action.Interactable && action.IsSelected)
            {
                StyleSelectedButton(button, BossRushUIColors.Accent, BossRushUIColors.Accent);
                return;
            }
            BossRushUIKit.StyleSecondaryButton(button);
            if (!action.Interactable) SetLabelColor(button, BossRushUIColors.TextSecondary);
        }

        /// <summary>选中态：深色底染一点 <paramref name="tint"/>、描边换 <paramref name="stroke"/>。</summary>
        private static void StyleSelectedButton(Button button, Color tint, Color stroke)
        {
            if (button == null) return;
            Color picked = Color.Lerp(BossRushUIColors.SurfaceRaised, tint, SelectedTint);
            picked.a = BossRushUIColors.SurfaceRaised.a;
            ZombieModeUIHelper.SetButtonBaseColor(button, picked);
            Image image = button.targetGraphic as Image;
            if (image != null && image.transform.Find("Stroke") == null)
            {
                BossRushUI.ApplyPanelStroke(image, 8, BossRushUISkinPart.Button, stroke);
            }
        }

        /// <summary>描边按钮：深色底 + 彩色描边 + 同色字（选人卡上的「选他出战」）。</summary>
        private static void StyleOutlineButton(Button button, Color color)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null && image.transform.Find("Stroke") == null)
            {
                BossRushUI.ApplyPanelStroke(image, 8, BossRushUISkinPart.Button, color);
            }
            SetLabelColor(button, color);
        }

        private static TextMeshProUGUI FindLabel(Button button)
        {
            Transform label = button != null ? button.transform.Find("Text") : null;
            return label != null ? label.GetComponent<TextMeshProUGUI>() : null;
        }

        private static void SetLabelColor(Button button, Color color)
        {
            TextMeshProUGUI label = FindLabel(button);
            if (label != null) label.color = color;
        }
    }

    /// <summary>
    /// 可点卡片的悬停描边：指针进来描边换主色、离开换回描边色（整卡已由 ColorBlock 提亮底色）。
    /// 只收指针事件，没有 Update；卡片不可点时不变色。
    /// </summary>
    internal sealed class ModeHCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Selectable _target;
        private Image _stroke;

        internal void Bind(Selectable target, Image stroke)
        {
            _target = target;
            _stroke = stroke;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_stroke != null && _target != null && _target.IsInteractable())
            {
                _stroke.color = BossRushUIColors.Accent;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_stroke != null) _stroke.color = BossRushUIColors.Stroke;
        }

        private void OnDisable()
        {
            if (_stroke != null) _stroke.color = BossRushUIColors.Stroke;
        }
    }
}

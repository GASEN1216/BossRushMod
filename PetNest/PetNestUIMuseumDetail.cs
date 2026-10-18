// ============================================================================
// PetNestUIMuseumDetail.cs - 博物馆血脉详情浮层卡（2026-10-17 UI 与交互对照审查 E-2）
// ============================================================================
// 与 PetNestUI.cs 是同一个 partial 类。浮层挂在面板自己的 canvas 里、压在 surface 之上：
// **不创建 canvas、不碰 sortingOrder**。数据来自 PetNestUIPages.BuildLineageDetail（只读快照）。
//
// 交互：点博物馆的格子打开；点卡片外的暗处、「关闭」或 ESC 收起（ESC 先给浮层，面板的取消键在浮层开着时让位）；
// 切页、关面板时一并收起。出现时遮罩淡入、卡片走共享开场动画。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class PetNestUI
    {
        private static readonly Vector2 LineageCardSize = new Vector2(620f, 470f);

        private GameObject _lineageOverlay;

        /// <summary>血脉详情浮层开着（面板的取消键与翻页键此时让位）。</summary>
        private bool IsLineageDetailOpen
        {
            get { return _lineageOverlay != null && _lineageOverlay.activeSelf; }
        }

        private void OpenLineageDetail(string lineageKey)
        {
            CloseLineageDetail();
            if (_canvas == null || string.IsNullOrEmpty(lineageKey)) return;
            PetNestDetailData detail;
            try
            {
                detail = PetNestUIPages.BuildLineageDetail(lineageKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 血脉详情组装失败: " + e.Message);
                return;
            }

            // 遮罩：整块挡住面板，点它就收起
            GameObject shade = ZombieModeUIHelper.CreateRect("LineageShade", _canvas.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image shadeImage = shade.AddComponent<Image>();
            shadeImage.color = BossRushUIColors.Backdrop;
            BossRushUIKit.StyleBackdrop(shadeImage);
            Button shadeButton = shade.AddComponent<Button>();
            shadeButton.transition = Selectable.Transition.None;
            shadeButton.onClick.AddListener(CloseLineageDetail);
            _lineageOverlay = shade;

            GameObject card = ZombieModeUIHelper.CreateRect("LineageCard", shade.transform, new Vector2(0.5f, 0.5f), LineageCardSize);
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(cardImage, 14, BossRushUISkinPart.Panel);
            // 卡片本身吃掉点击，点卡片内部不收起
            card.AddComponent<Button>().transition = Selectable.Transition.None;

            float left = 28f;
            if (detail.Icon != null)
            {
                CreateIconFrame(card.transform, "Portrait", detail.Icon,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), 112f,
                    detail.Shiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Stroke, false);
                left = 28f + 112f + 18f;
            }
            float textWidth = LineageCardSize.x - left - 28f;
            TextMeshProUGUI title = ZombieModeUIHelper.CreateText("Title", card.transform, detail.Title ?? string.Empty, 28f,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -30f), new Vector2(textWidth, 42f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(left, -30f);
            title.fontStyle = FontStyles.Bold;
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;
            BossRushUI.ApplyGameFont(title);
            if (detail.Shiny) PetNestShinyTextShimmer.Attach(title);

            TextMeshProUGUI subtitle = ZombieModeUIHelper.CreateText("Subtitle", card.transform, detail.Subtitle ?? string.Empty, 18f,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -78f), new Vector2(textWidth, 30f),
                TextAlignmentOptions.Left, BossRushUIColors.Accent);
            subtitle.rectTransform.pivot = new Vector2(0f, 1f);
            subtitle.rectTransform.anchoredPosition = new Vector2(left, -78f);
            BossRushUI.ApplyGameFont(subtitle);

            // 分区写成一段富文本：区头 Accent 粗体、正文次级色，一区一段
            string body = string.Empty;
            string accentHex = IntegrationUIFeedback.AccentHex;
            string secondaryHex = IntegrationUIFeedback.SecondaryHex;
            for (int i = 0; i < detail.Sections.Count; i++)
            {
                KeyValuePair<string, string> section = detail.Sections[i];
                if (body.Length > 0) body += "\n";
                body += "<b><color=" + accentHex + ">" + section.Key + "</color></b>\n"
                    + "<color=" + secondaryHex + ">" + (section.Value ?? string.Empty) + "</color>";
            }
            float bodyTop = 160f;
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText("Body", card.transform, body, 17f,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -bodyTop),
                new Vector2(LineageCardSize.x - 56f, LineageCardSize.y - bodyTop - 76f),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.anchoredPosition = new Vector2(28f, -bodyTop);
            text.enableAutoSizing = true;
            text.fontSizeMin = 13f;
            text.fontSizeMax = 17f;
            BossRushUI.ApplyGameFont(text);

            Button close = ZombieModeUIHelper.CreateButton("Close", card.transform, L10n.T("关闭", "Close"),
                new Vector2(1f, 0f), new Vector2(-96f, 40f), new Vector2(150f, 44f),
                BossRushUIColors.SurfaceRaised, 19f, new Vector2(140f, 40f),
                new UnityEngine.Events.UnityAction(CloseLineageDetail), true);
            BossRushUIKit.StyleSecondaryButton(close);

            shade.transform.SetAsLastSibling();
            PetNestFadeIn.Play(shade, 0f, 0.14f);
            BossRushUI.PlayOpenAnimation(card);
            PetNestCancelKey.Attach(shade, CloseLineageDetail,
                delegate { return PetNestRenameModal.IsOpen || BossRushConfirmDialog.IsOpen; });
        }

        private void CloseLineageDetail()
        {
            if (_lineageOverlay == null) return;
            GameObject overlay = _lineageOverlay;
            _lineageOverlay = null;
            PetNestCancelKey key = overlay.GetComponent<PetNestCancelKey>();
            if (key != null) key.Detach();
            overlay.SetActive(false);
            UnityEngine.Object.Destroy(overlay);
        }
    }
}

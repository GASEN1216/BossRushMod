// ============================================================================
// BossRushUIHero.cs - 共享 UI 库：模式横幅（主视觉裁切）与徽记标题行
// ============================================================================
// 2026-09-23 审美审查 UB-01：Mode H 的徽记与横幅随包下发、入口还强制预检，却一处都没画——
// 所有页面是纯深色面板 + 标题字，拍铃徽章是青色圆里写一个「铃」字。Mode G 的确认页早就在用
// 横幅 + 徽记（ModeGInteractable 的 CreateHeroClip / GetHeroArtSize / GetHeroArtOffset / PlaceTitleRow），
// 同一个 Mod 两种档次。这里把那套写法提成与模式无关的共享版：
//   - 横幅：Card 档圆角图当 Mask 模板（showMaskGraphic=false，只写模板、自己不画），
//     插图按原图比例铺满裁切框，只露 focusY 那一带，描边画在插图上面；
//   - 徽记：保持比例的 Image，不吃点击；
//   - 标题行：徽记在左、标题在右，按标题实际宽度整组居中。
// 算式与 Mode G 逐字等价（Mode G 本轮不动，下一轮可原样改调这里）。
// 本文件不持有任何静态缓存：插图 Sprite 由各模式自己的展示缓存负责加载与卸载。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>模式横幅与徽记的共享构件。</summary>
    internal static class BossRushUIHero
    {
        /// <summary>插图的乘色：原色显示（Image.color 是乘在贴图上的，白 = 不改色）。</summary>
        internal static readonly Color ArtTint = Color.white;

        /// <summary>
        /// 建一条横幅：圆角裁切框 + 按比例铺满的插图 + 描边。<paramref name="art"/> 为空时不建、返回 null，
        /// 调用方退回无横幅的版式（取不到图就去掉那一格，不画灰块占位）。
        /// </summary>
        /// <param name="parent">父物体。</param>
        /// <param name="name">裁切框的物体名。</param>
        /// <param name="art">插图。</param>
        /// <param name="anchor">锚点（同时作 anchorMin / anchorMax）。</param>
        /// <param name="pivot">轴心。</param>
        /// <param name="anchoredPosition">位置。</param>
        /// <param name="size">裁切框尺寸（画布单位）。</param>
        /// <param name="radius">圆角半径（Card 档）。</param>
        /// <param name="focusY">插图里要放在框中央的那一带，按原图从上往下的比例（0 = 顶边，1 = 底边）。</param>
        /// <param name="strokeColor">描边色。深色面板上一般用 <see cref="BossRushUIColors.Stroke"/>。</param>
        internal static RectTransform CreateBanner(Transform parent, string name, Sprite art,
            Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            int radius, float focusY, Color strokeColor)
        {
            if (parent == null || art == null) return null;

            GameObject clip = ZombieModeUIHelper.CreateRect(name, parent, anchor, anchor,
                anchoredPosition, size, pivot);
            Image stencil = clip.AddComponent<Image>();
            stencil.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(stencil, radius, BossRushUISkinPart.Card);
            Mask mask = clip.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            Vector2 artSize = GetCoverSize(art, size);
            GameObject artObj = ZombieModeUIHelper.CreateRect("Art", clip.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, GetFocusOffset(artSize.y, size.y, focusY)), artSize, new Vector2(0.5f, 0.5f));
            Image artImage = artObj.AddComponent<Image>();
            artImage.sprite = art;
            artImage.color = ArtTint;
            artImage.preserveAspect = true;
            artImage.raycastTarget = false;

            // 描边建在插图之后，画在它上面；同在 Mask 里，圆角与模板一致（Mode G 同一口径）。
            BossRushUI.ApplyPanelStroke(stencil, radius, BossRushUISkinPart.Card, strokeColor);
            return clip.GetComponent<RectTransform>();
        }

        /// <summary>按原图比例铺满裁切框宽度；原图比框还扁时改按高度铺满，保证框里没有空边。</summary>
        internal static Vector2 GetCoverSize(Sprite art, Vector2 frame)
        {
            float aspect = art != null && art.rect.height > 0.5f ? art.rect.width / art.rect.height : 16f / 9f;
            if (aspect <= 0.01f) aspect = 16f / 9f;
            Vector2 size = new Vector2(frame.x, frame.x / aspect);
            if (size.y < frame.y) size = new Vector2(frame.y * aspect, frame.y);
            return size;
        }

        /// <summary>
        /// 把原图 focusY 那一带移到裁切框中央；夹住偏移，插图边缘不会露进框里。
        /// focusY 按原图从上往下量：插图整体上移 (focusY - 0.5) × 高度，框中央看到的就是那一带。
        /// </summary>
        internal static float GetFocusOffset(float artHeight, float frameHeight, float focusY)
        {
            float slack = Mathf.Max(0f, (artHeight - frameHeight) * 0.5f);
            return Mathf.Clamp((focusY - 0.5f) * artHeight, -slack, slack);
        }

        /// <summary>徽记：保持比例、不吃点击，锚在父物体中心。<paramref name="emblem"/> 为空时返回 null。</summary>
        internal static RectTransform CreateEmblem(Transform parent, string name, Sprite emblem, float size)
        {
            if (parent == null || emblem == null) return null;
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size, size), new Vector2(0.5f, 0.5f));
            Image image = obj.AddComponent<Image>();
            image.sprite = emblem;
            image.color = ArtTint;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return obj.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 徽记在左、标题在右，按标题实际宽度整组居中到 <paramref name="center"/>（父物体中心坐标系）。
        /// 标题关掉自动缩字、按字量定宽，宽度不超过 <paramref name="maxWidth"/>（超出按省略号收，框高须够一行）。
        /// 徽记为空时标题单独居中。两者都必须锚在父物体中心（anchor 0.5,0.5）。
        /// </summary>
        internal static void PlaceTitleRow(TextMeshProUGUI title, RectTransform emblem, float emblemGap,
            float maxWidth, Vector2 center)
        {
            if (title == null) return;
            RectTransform titleRect = title.rectTransform;
            title.enableAutoSizing = false;
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;
            float emblemWidth = emblem != null ? emblem.sizeDelta.x + emblemGap : 0f;
            float available = Mathf.Max(40f, maxWidth - emblemWidth);
            float width = Mathf.Min(available,
                Mathf.Ceil(title.GetPreferredValues(title.text).x) + title.margin.x + title.margin.z + 8f);
            titleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float group = emblemWidth + width;
            float left = center.x - group * 0.5f;
            if (emblem != null)
            {
                emblem.anchoredPosition = new Vector2(left + emblem.sizeDelta.x * 0.5f, center.y);
                left += emblemWidth;
            }
            titleRect.anchoredPosition = new Vector2(left + width * 0.5f, center.y);
        }
    }
}

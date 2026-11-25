// ============================================================================
// WikiUIManager_Illustrations.cs - 冒险家日志文内插画卡片与水墨大图灯箱
// ============================================================================
// 职责：
//   1. 为 TryBuildIllustratedArticlePages 提供基于真实精灵纵横比的高度测算与分段分页；
//   2. 在左右两栏动态预留并渲染水墨镶边插画卡（figure / icon / gallery 缩略网格）；
//   3. 提供点击放大的居中水墨灯箱（支持图库左右切换、遮罩与 Esc 优先关闭）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BossRush
{
    public partial class WikiUIManager
    {
        private const float CardGap = 10f;
        private const float MinTextHeight = 64f;

        private static readonly Color CardPaperFill = new Color(0.845f, 0.785f, 0.678f, 0.96f);
        private static readonly Color CardMatFill = new Color(0.925f, 0.885f, 0.805f, 0.96f);
        private static readonly Color LightboxBackdropColor = new Color(0.02f, 0.015f, 0.012f, 0.84f);

        private RectTransform leftIllustrationHost = null;
        private RectTransform rightIllustrationHost = null;

        private GameObject lightboxRoot = null;
        private RectTransform lightboxPanel = null;
        private Image lightboxImage = null;
        private TextMeshProUGUI lightboxCaption = null;
        private TextMeshProUGUI lightboxCounter = null;
        private Button lightboxPrevBtn = null;
        private Button lightboxNextBtn = null;

        private readonly List<WikiPageVisualSlot> currentPageIllustrations = new List<WikiPageVisualSlot>();
        private List<WikiIllustrationAsset> lightboxAssets = null;
        private int lightboxIndex = 0;

        // ============================================================================
        // 清理与生命周期
        // ============================================================================

        private void ClearIllustrationReferences()
        {
            leftIllustrationHost = null;
            rightIllustrationHost = null;
            ClearLightboxReferences();
            currentPageIllustrations.Clear();
        }

        private void ClearLightboxReferences()
        {
            lightboxRoot = null;
            lightboxPanel = null;
            lightboxImage = null;
            lightboxCaption = null;
            lightboxCounter = null;
            lightboxPrevBtn = null;
            lightboxNextBtn = null;
            lightboxAssets = null;
            lightboxIndex = 0;
        }

        private void OnCancelKeyPressed()
        {
            if (CloseLightbox()) return;
            CloseUI();
        }

        private float GetColumnTextHeightForPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= currentPageIllustrations.Count) return ColumnHeight;
            WikiPageVisualSlot slot = currentPageIllustrations[pageIndex];
            if (slot == null) return ColumnHeight;
            return Mathf.Max(MinTextHeight, ColumnHeight - slot.ReservedHeight);
        }

        // ============================================================================
        // 图文混排高度感知分页
        // ============================================================================

        private bool TryBuildIllustratedArticlePages(string entryId, string rawContent)
        {
            currentPageIllustrations.Clear();
            if (txtLeft == null || string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(rawContent)) return false;

            List<WikiContentSegment> segments = WikiIllustrations.BuildContentSegments(entryId, rawContent);
            if (segments == null || segments.Count == 0) return false;

            WikiPageVisualSlot pendingTop = null;

            for (int i = 0; i < segments.Count; i++)
            {
                WikiContentSegment seg = segments[i];
                if (seg.IsIllustration)
                {
                    WikiPageVisualSlot slot = CreateVisualSlot(seg);
                    if (slot == null) continue;

                    int lastIdx = currentArticlePages.Count - 1;
                    if (pendingTop == null
                        && lastIdx >= 0
                        && !seg.AfterHeading
                        && currentPageIllustrations[lastIdx] == null)
                    {
                        float usedH = MeasurePageUsedTextHeight(txtLeft, currentArticlePages[lastIdx], ColumnWidth, ColumnHeight);
                        if (ColumnHeight - usedH >= slot.ReservedHeight + 4f)
                        {
                            slot.AnchorTop = false;
                            slot.BottomTextUsedHeight = usedH;
                            currentPageIllustrations[lastIdx] = slot;
                            continue;
                        }
                    }

                    if (pendingTop != null)
                    {
                        currentArticlePages.Add(string.Empty);
                        currentPageIllustrations.Add(pendingTop);
                    }
                    slot.AnchorTop = true;
                    pendingTop = slot;
                    continue;
                }

                string parsedChunk = WikiContentManager.Instance.ParseMarkdown(seg.MarkdownChunk, seg.StripLeadingTitle);
                if (string.IsNullOrEmpty(parsedChunk)) continue;

                AppendParsedTextSegment(parsedChunk, ref pendingTop);
            }

            if (pendingTop != null)
            {
                currentArticlePages.Add(string.Empty);
                currentPageIllustrations.Add(pendingTop);
            }

            // 恢复标准列高后再交由 NormalizeArticlePageSlices 做逐页校验
            ApplyColumnBox(txtLeft, true, null);
            return currentArticlePages.Count > 0;
        }

        private void AppendParsedTextSegment(string parsedChunk, ref WikiPageVisualSlot pendingTop)
        {
            if (pendingTop == null)
            {
                AppendFullHeightTextPages(parsedChunk);
                return;
            }

            float availHeight = Mathf.Max(MinTextHeight, ColumnHeight - pendingTop.ReservedHeight);
            SetRectSizeAndCenterY(txtLeft.rectTransform, -ColumnCenterX, ColumnCenterY, ColumnWidth, availHeight);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(txtLeft.rectTransform);

            txtLeft.text = parsedChunk;
            txtLeft.overflowMode = TMPro.TextOverflowModes.Page;
            txtLeft.pageToDisplay = 1;
            txtLeft.ForceMeshUpdate(true, true);

            TMP_TextInfo info = txtLeft.textInfo;
            if (info == null || info.pageCount <= 1)
            {
                currentArticlePages.Add(parsedChunk);
                currentPageIllustrations.Add(pendingTop);
                pendingTop = null;
                return;
            }

            int splitIdx = ResolvePageSourceStart(info, 1, -1);
            if (splitIdx <= 0 || splitIdx >= parsedChunk.Length)
            {
                splitIdx = ResolvePageSourceEnd(info, 0, -1);
            }

            if (splitIdx > 0 && splitIdx < parsedChunk.Length)
            {
                string firstSlice = parsedChunk.Substring(0, splitIdx);
                string restSlice = parsedChunk.Substring(splitIdx);
                currentArticlePages.Add(RepairPageRichText(firstSlice, parsedChunk, 0, splitIdx));
                currentPageIllustrations.Add(pendingTop);
                pendingTop = null;

                string repairedRest = RepairPageRichText(restSlice, parsedChunk, splitIdx, parsedChunk.Length);
                if (!string.IsNullOrEmpty(repairedRest))
                {
                    AppendFullHeightTextPages(repairedRest);
                }
                return;
            }

            // 首字即无法容身时：先独占一页展示配图，正文从下一页满高起排
            currentArticlePages.Add(string.Empty);
            currentPageIllustrations.Add(pendingTop);
            pendingTop = null;
            AppendFullHeightTextPages(parsedChunk);
        }

        private void AppendFullHeightTextPages(string source)
        {
            if (string.IsNullOrEmpty(source)) return;

            SetRectSizeAndCenterY(txtLeft.rectTransform, -ColumnCenterX, ColumnCenterY, ColumnWidth, ColumnHeight);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(txtLeft.rectTransform);

            txtLeft.text = source;
            txtLeft.overflowMode = TMPro.TextOverflowModes.Page;
            txtLeft.pageToDisplay = 1;
            txtLeft.ForceMeshUpdate(true, true);

            TMP_TextInfo info = txtLeft.textInfo;
            int count = info != null ? info.pageCount : 1;
            if (count <= 1 || info == null || info.pageInfo == null || info.characterInfo == null)
            {
                currentArticlePages.Add(source);
                currentPageIllustrations.Add(null);
                return;
            }

            int[] bounds = new int[count + 1];
            bounds[0] = 0;
            bounds[count] = source.Length;
            for (int p = 1; p < count; p++)
            {
                int srcIdx = ResolvePageSourceStart(info, p, bounds[p - 1]);
                bounds[p] = Math.Max(bounds[p - 1], Math.Min(srcIdx, source.Length));
            }
            for (int p = 0; p < count; p++)
            {
                string slice = source.Substring(bounds[p], bounds[p + 1] - bounds[p]);
                string repaired = RepairPageRichText(slice, source, bounds[p], bounds[p + 1]);
                if (!string.IsNullOrEmpty(repaired))
                {
                    currentArticlePages.Add(repaired);
                    currentPageIllustrations.Add(null);
                }
            }
        }

        private static float MeasurePageUsedTextHeight(TMP_Text renderer, string text, float width, float maxHeight)
        {
            if (renderer == null || string.IsNullOrEmpty(text)) return 0f;

            SetRectSizeAndCenterY(renderer.rectTransform, renderer.rectTransform.anchoredPosition.x, ColumnCenterY, width, maxHeight);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(renderer.rectTransform);

            renderer.text = text;
            renderer.overflowMode = TMPro.TextOverflowModes.Page;
            renderer.pageToDisplay = 1;
            renderer.ForceMeshUpdate(true, true);

            TMP_TextInfo info = renderer.textInfo;
            if (info == null || info.lineCount <= 0 || info.lineInfo == null) return 0f;
            if (info.pageCount > 1) return maxHeight;

            int lastLine = info.lineCount - 1;
            while (lastLine > 0 && info.lineInfo[lastLine].characterCount <= 0)
            {
                lastLine--;
            }

            float topLocalY = renderer.rectTransform.rect.height * 0.5f;
            float descender = info.lineInfo[lastLine].descender;
            return Mathf.Clamp((topLocalY - descender) + renderer.margin.w + 4f, 0f, maxHeight);
        }

        private WikiPageVisualSlot CreateVisualSlot(WikiContentSegment seg)
        {
            if (seg == null || seg.Assets == null || seg.Assets.Count == 0) return null;

            string headingRich = null;
            float headingH = 0f;
            if (!string.IsNullOrEmpty(seg.HeadingLine))
            {
                headingRich = WikiContentManager.Instance.ParseMarkdown(seg.HeadingLine, false);
                if (!string.IsNullOrEmpty(headingRich))
                {
                    headingH = 34f;
                }
            }

            float cardH = ComputeCardHeight(seg.Kind, seg.Assets);
            return new WikiPageVisualSlot
            {
                AnchorTop = true,
                HeadingRichText = headingRich,
                Kind = seg.Kind,
                Assets = seg.Assets,
                HeadingHeight = headingH,
                CardHeight = cardH,
                ReservedHeight = headingH + cardH + CardGap
            };
        }

        private static float ComputeCardHeight(string kind, List<WikiIllustrationAsset> assets)
        {
            int n = assets.Count;
            if (n == 1 && string.Equals(kind, "icon", StringComparison.Ordinal))
            {
                return 92f;
            }
            if (n == 1)
            {
                Sprite sp = assets[0].ResolveSprite();
                float aspect = 1.6f;
                if (sp != null && sp.rect.height > 1f)
                {
                    aspect = sp.rect.width / sp.rect.height;
                }
                float imgH = aspect >= 2.2f ? 112f : (aspect >= 1.3f ? 174f : 150f);
                return imgH + 42f;
            }

            int cols = n == 4 ? 2 : (n <= 2 ? 2 : 3);
            int rows = (n + cols - 1) / cols;
            float rowH = rows <= 1 ? 104f : (rows == 2 ? 92f : (rows == 3 ? 82f : 74f));
            return Mathf.Min(ColumnHeight - 100f, rows * rowH + (rows - 1) * 6f + 34f);
        }

        // ============================================================================
        // 列框几何同步与文内水墨配图卡渲染
        // ============================================================================

        private static void SetRectSizeAndCenterY(RectTransform rect, float centerX, float centerY, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(centerX, centerY);
        }

        private void ApplyColumnBox(TMP_Text text, bool isLeft, WikiPageVisualSlot slot)
        {
            if (text == null) return;
            float centerX = isLeft ? -ColumnCenterX : ColumnCenterX;
            if (slot == null)
            {
                SetRectSizeAndCenterY(text.rectTransform, centerX, ColumnCenterY, ColumnWidth, ColumnHeight);
                return;
            }

            float availH = Mathf.Max(MinTextHeight, ColumnHeight - slot.ReservedHeight);
            float colTop = ColumnCenterY + ColumnHeight * 0.5f;
            float colBottom = ColumnCenterY - ColumnHeight * 0.5f;
            float centerY = slot.AnchorTop
                ? (colBottom + availH * 0.5f)
                : (colTop - availH * 0.5f);
            SetRectSizeAndCenterY(text.rectTransform, centerX, centerY, ColumnWidth, availH);
        }

        private void EnsureIllustrationHosts()
        {
            if (pagesGroup == null) return;
            if (leftIllustrationHost == null)
            {
                leftIllustrationHost = CreateColumnHost("LeftIllustrations", -ColumnCenterX);
            }
            if (rightIllustrationHost == null)
            {
                rightIllustrationHost = CreateColumnHost("RightIllustrations", ColumnCenterX);
            }
        }

        private RectTransform CreateColumnHost(string name, float centerX)
        {
            GameObject go = ZombieModeUIHelper.CreateRect(name, pagesGroup, new Vector2(0.5f, 0.5f), new Vector2(ColumnWidth, ColumnHeight));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(centerX, ColumnCenterY);
            return rect;
        }

        private void RenderPageIllustrations(int leftPageNumber, int rightPageNumber)
        {
            EnsureIllustrationHosts();
            RenderColumnSlot(leftIllustrationHost, leftPageNumber);
            RenderColumnSlot(rightIllustrationHost, rightPageNumber);
        }

        private void RenderColumnSlot(RectTransform host, int pageNumber)
        {
            if (host == null) return;
            ClearContainer(host);

            int idx = pageNumber - 1;
            if (idx < 0 || idx >= currentArticlePages.Count || idx >= currentPageIllustrations.Count)
            {
                host.gameObject.SetActive(false);
                return;
            }

            WikiPageVisualSlot slot = currentPageIllustrations[idx];
            if (slot == null || slot.Assets == null || slot.Assets.Count == 0)
            {
                host.gameObject.SetActive(false);
                return;
            }

            host.gameObject.SetActive(true);
            float hostTopY = ColumnHeight * 0.5f;
            float topY;
            if (slot.AnchorTop)
            {
                topY = hostTopY;
            }
            else
            {
                topY = Mathf.Max(-hostTopY + slot.CardHeight, hostTopY - slot.BottomTextUsedHeight - CardGap);
            }

            if (slot.AnchorTop && !string.IsNullOrEmpty(slot.HeadingRichText) && slot.HeadingHeight > 0f)
            {
                TextMeshProUGUI hdr = ZombieModeUIHelper.CreateText("SlotHeading", host, slot.HeadingRichText, 16f,
                    new Vector2(0f, topY - slot.HeadingHeight * 0.5f + 2f),
                    new Vector2(ColumnWidth - 8f, slot.HeadingHeight),
                    TextAlignmentOptions.BottomLeft, NoticeInk);
                BossRushUI.ApplyGameFont(hdr);
                hdr.richText = true;
                hdr.enableWordWrapping = false;
                hdr.overflowMode = TextOverflowModes.Ellipsis;
                hdr.raycastTarget = false;
                topY -= slot.HeadingHeight;
            }

            float cardCenterY = topY - slot.CardHeight * 0.5f;
            if (slot.Assets.Count == 1 && string.Equals(slot.Kind, "icon", StringComparison.Ordinal))
            {
                BuildCompactIconCard(host, slot, cardCenterY);
            }
            else if (slot.Assets.Count == 1)
            {
                BuildSingleFigureCard(host, slot, cardCenterY);
            }
            else
            {
                BuildGalleryGridCard(host, slot, cardCenterY);
            }
        }

        private Button CreateParchmentCardButton(Transform parent, string name, Vector2 center, Vector2 size, Action onClick)
        {
            Button btn = ZombieModeUIHelper.CreateButton(name, parent, string.Empty,
                new Vector2(0.5f, 0.5f), center, size, CardPaperFill, 14f, size,
                () => { if (!closing && onClick != null) onClick(); }, true);
            DisableUnityNavigation(btn);
            BossRushInkSkin.StyleInkButton(btn, BossRushInkButtonRole.Secondary);
            ZombieModeUIHelper.SetButtonBaseColor(btn, CardPaperFill);
            BossRushInkSkin.SetFrameColor(btn.targetGraphic, Fade(NoticeInk, 0.55f));
            return btn;
        }

        private void BuildCompactIconCard(RectTransform host, WikiPageVisualSlot slot, float centerY)
        {
            WikiIllustrationAsset item = slot.Assets[0];
            List<WikiIllustrationAsset> all = slot.Assets;
            Button card = CreateParchmentCardButton(host, "IconCard", new Vector2(0f, centerY),
                new Vector2(ColumnWidth, slot.CardHeight), () => OpenLightbox(all, 0));

            AddArtworkMat(card.transform, new Vector2(-ColumnWidth * 0.5f + 46f, 0f), new Vector2(72f, 72f), item.ResolveSprite());

            float textWidth = ColumnWidth - 104f;
            float textCenterX = 36f;

            TextMeshProUGUI cap = ZombieModeUIHelper.CreateText("Caption", card.transform, item.GetCaption(), 15f,
                new Vector2(textCenterX, 11f), new Vector2(textWidth, 34f),
                TextAlignmentOptions.Left, NoticeInk);
            BossRushUI.ApplyGameFont(cap);
            cap.fontStyle = FontStyles.Bold;
            cap.enableWordWrapping = true;
            cap.overflowMode = TextOverflowModes.Ellipsis;
            cap.raycastTarget = false;

            TextMeshProUGUI hint = ZombieModeUIHelper.CreateText("ZoomHint", card.transform,
                L10n.T("[点击查看大图]", "[Click to enlarge]"), 12f,
                new Vector2(textCenterX, -20f), new Vector2(textWidth, 22f),
                TextAlignmentOptions.Left, NoticeInkSoft);
            BossRushUI.ApplyGameFont(hint);
            hint.enableWordWrapping = false;
            hint.overflowMode = TextOverflowModes.Ellipsis;
            hint.raycastTarget = false;
        }

        private void BuildSingleFigureCard(RectTransform host, WikiPageVisualSlot slot, float centerY)
        {
            WikiIllustrationAsset item = slot.Assets[0];
            List<WikiIllustrationAsset> all = slot.Assets;
            Button card = CreateParchmentCardButton(host, "FigureCard", new Vector2(0f, centerY),
                new Vector2(ColumnWidth, slot.CardHeight), () => OpenLightbox(all, 0));

            float imgH = slot.CardHeight - 42f;
            AddArtworkMat(card.transform, new Vector2(0f, 14f), new Vector2(ColumnWidth - 20f, imgH), item.ResolveSprite());

            TextMeshProUGUI cap = ZombieModeUIHelper.CreateText("Caption", card.transform,
                item.GetCaption() + L10n.T("  [放大]", "  [Zoom]"), 12f,
                new Vector2(0f, -slot.CardHeight * 0.5f + 18f), new Vector2(ColumnWidth - 22f, 26f),
                TextAlignmentOptions.Center, NoticeInkSoft);
            BossRushUI.ApplyGameFont(cap);
            cap.enableWordWrapping = true;
            cap.overflowMode = TextOverflowModes.Ellipsis;
            cap.raycastTarget = false;
        }

        private void BuildGalleryGridCard(RectTransform host, WikiPageVisualSlot slot, float centerY)
        {
            GameObject frame = ZombieModeUIHelper.CreateRect("GalleryFrame", host, new Vector2(0.5f, 0.5f), new Vector2(ColumnWidth, slot.CardHeight));
            frame.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, centerY);
            Image bg = frame.AddComponent<Image>();
            bg.sprite = BossRushInkSkin.GetSurfaceSprite();
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2.4f;
            bg.color = Fade(CardPaperFill, 0.72f);
            bg.raycastTarget = false;
            BossRushInkSkin.AddInkFrame(bg, Fade(NoticeInk, 0.48f), 2f);

            List<WikiIllustrationAsset> items = slot.Assets;
            int n = items.Count;
            int cols = n == 4 ? 2 : (n <= 2 ? 2 : 3);
            int rows = (n + cols - 1) / cols;

            const float padH = 8f;
            const float padTop = 8f;
            const float gapX = 6f;
            const float gapY = 6f;
            const float footerH = 18f;

            float cellW = (ColumnWidth - padH * 2f - gapX * (cols - 1)) / cols;
            float availGridH = slot.CardHeight - padTop - footerH - 8f;
            float cellH = (availGridH - gapY * (rows - 1)) / rows;
            float imgH = Mathf.Max(26f, cellH - 28f);

            for (int i = 0; i < n; i++)
            {
                int row = i / cols;
                int col = i % cols;
                int itemsInRow = (row == rows - 1) ? (n - row * cols) : cols;
                float rowWidth = itemsInRow * cellW + (itemsInRow - 1) * gapX;
                float cellCenterX = -rowWidth * 0.5f + cellW * 0.5f + col * (cellW + gapX);
                float cellCenterY = slot.CardHeight * 0.5f - padTop - cellH * 0.5f - row * (cellH + gapY);

                int captureIndex = i;
                Button cellBtn = CreateParchmentCardButton(frame.transform, "Cell_" + i,
                    new Vector2(cellCenterX, cellCenterY), new Vector2(cellW, cellH),
                    () => OpenLightbox(items, captureIndex));

                AddArtworkMat(cellBtn.transform, new Vector2(0f, (cellH - imgH) * 0.5f - 4f),
                    new Vector2(cellW - 10f, imgH), items[i].ResolveSprite());

                TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Cap", cellBtn.transform, items[i].GetCaption(),
                    cols == 2 ? 12f : 11f,
                    new Vector2(0f, -cellH * 0.5f + 13f), new Vector2(cellW - 8f, 22f),
                    TextAlignmentOptions.Center, NoticeInk);
                BossRushUI.ApplyGameFont(label);
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.raycastTarget = false;
            }

            TextMeshProUGUI footer = ZombieModeUIHelper.CreateText("GalleryHint", frame.transform,
                L10n.T("点击任意配图可放大查看", "Click any illustration to zoom"), 11f,
                new Vector2(0f, -slot.CardHeight * 0.5f + 12f), new Vector2(ColumnWidth - 20f, 20f),
                TextAlignmentOptions.Center, NoticeInkSoft);
            BossRushUI.ApplyGameFont(footer);
            footer.enableWordWrapping = false;
            footer.raycastTarget = false;
        }

        private static void AddArtworkMat(Transform parent, Vector2 center, Vector2 size, Sprite sprite)
        {
            GameObject mat = ZombieModeUIHelper.CreateRect("ArtMat", parent, new Vector2(0.5f, 0.5f), size);
            mat.GetComponent<RectTransform>().anchoredPosition = center;
            Image matImg = mat.AddComponent<Image>();
            matImg.sprite = BossRushInkSkin.GetSurfaceSprite();
            matImg.type = Image.Type.Sliced;
            matImg.pixelsPerUnitMultiplier = 2.6f;
            matImg.color = CardMatFill;
            matImg.raycastTarget = false;

            GameObject art = ZombieModeUIHelper.CreateRect("Art", mat.transform, new Vector2(0.5f, 0.5f), size - new Vector2(8f, 8f));
            Image artImg = art.AddComponent<Image>();
            artImg.sprite = sprite;
            artImg.preserveAspect = true;
            artImg.color = Color.white;
            artImg.raycastTarget = false;
        }

        // ============================================================================
        // 水墨大图灯箱（点击任意卡片放大，支持图库翻看与 Esc 优先关闭）
        // ============================================================================

        private void OpenLightbox(List<WikiIllustrationAsset> assets, int index)
        {
            if (!IsUIOpen || closing || uiRoot == null || assets == null || assets.Count == 0) return;

            lightboxAssets = assets;
            lightboxIndex = Mathf.Clamp(index, 0, assets.Count - 1);

            if (lightboxRoot == null)
            {
                BuildLightboxOverlay();
            }
            else
            {
                lightboxRoot.SetActive(true);
            }

            RefreshLightboxContent();
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
            if (lightboxPanel != null) BossRushInkSkin.PlayInkReveal(lightboxPanel);
        }

        private bool CloseLightbox()
        {
            if (lightboxRoot == null || !lightboxRoot.activeSelf) return false;
            lightboxRoot.SetActive(false);
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
            return true;
        }

        private void BuildLightboxOverlay()
        {
            GameObject root = ZombieModeUIHelper.CreateRect("WikiLightbox", uiRoot.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            lightboxRoot = root;

            Button backdropBtn = ZombieModeUIHelper.CreateButton("Backdrop", root.transform, string.Empty,
                Vector2.zero, Vector2.zero, Vector2.zero, LightboxBackdropColor, 14f, new Vector2(1920f, 1080f),
                () => CloseLightbox(), false);
            RectTransform backRect = backdropBtn.GetComponent<RectTransform>();
            backRect.anchorMin = Vector2.zero;
            backRect.anchorMax = Vector2.one;
            backRect.offsetMin = Vector2.zero;
            backRect.offsetMax = Vector2.zero;
            DisableUnityNavigation(backdropBtn);
            ZombieModeUIHelper.SetButtonBaseColor(backdropBtn, LightboxBackdropColor);

            GameObject panelObj = ZombieModeUIHelper.CreateRect("LightboxPanel", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(980f, 740f));
            lightboxPanel = panelObj.GetComponent<RectTransform>();
            Image panelImg = panelObj.AddComponent<Image>();
            panelImg.raycastTarget = true;
            BossRushInkSkin.ApplyInkPanel(panelImg, true);

            GameObject matObj = ZombieModeUIHelper.CreateRect("LightboxMat", panelObj.transform,
                new Vector2(0.5f, 0.5f), new Vector2(912f, 548f));
            matObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 54f);
            Image matImg = matObj.AddComponent<Image>();
            matImg.sprite = BossRushInkSkin.GetSurfaceSprite();
            matImg.type = Image.Type.Sliced;
            matImg.pixelsPerUnitMultiplier = 1.6f;
            matImg.color = CardMatFill;
            matImg.raycastTarget = false;
            BossRushInkSkin.AddInkFrame(matImg, Fade(BossRushInkColors.Gold, 0.65f), 2.5f);

            GameObject imgObj = ZombieModeUIHelper.CreateRect("LightboxSprite", matObj.transform,
                new Vector2(0.5f, 0.5f), new Vector2(880f, 516f));
            lightboxImage = imgObj.AddComponent<Image>();
            lightboxImage.preserveAspect = true;
            lightboxImage.color = Color.white;
            lightboxImage.raycastTarget = false;

            lightboxCaption = ZombieModeUIHelper.CreateText("LightboxCaption", panelObj.transform, string.Empty, 18f,
                new Vector2(0f, -248f), new Vector2(880f, 44f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            BossRushUI.ApplyGameFont(lightboxCaption);
            lightboxCaption.fontStyle = FontStyles.Bold;
            lightboxCaption.enableWordWrapping = true;
            lightboxCaption.overflowMode = TextOverflowModes.Ellipsis;
            lightboxCaption.raycastTarget = false;

            lightboxCounter = ZombieModeUIHelper.CreateText("LightboxCounter", panelObj.transform, string.Empty, 14f,
                new Vector2(-120f, -314f), new Vector2(120f, 34f), TextAlignmentOptions.Center, BossRushInkColors.GoldSoft);
            BossRushUI.ApplyGameFont(lightboxCounter);
            lightboxCounter.raycastTarget = false;

            lightboxPrevBtn = CreateLightboxButton("Btn_LightboxPrev", L10n.T("上一张", "Prev"), new Vector2(-248f, -314f),
                new Vector2(124f, 44f), BossRushInkButtonRole.Secondary, () => StepLightbox(-1));
            lightboxNextBtn = CreateLightboxButton("Btn_LightboxNext", L10n.T("下一张", "Next"), new Vector2(8f, -314f),
                new Vector2(124f, 44f), BossRushInkButtonRole.Secondary, () => StepLightbox(1));

            CreateLightboxButton("Btn_LightboxClose", L10n.T("关　闭", "Close"), new Vector2(364f, -314f),
                new Vector2(146f, 46f), BossRushInkButtonRole.Primary, () => CloseLightbox());
        }

        private Button CreateLightboxButton(string name, string text, Vector2 pos, Vector2 size, BossRushInkButtonRole role, Action onClick)
        {
            Button btn = ZombieModeUIHelper.CreateButton(name, lightboxPanel, text,
                new Vector2(0.5f, 0.5f), pos, size,
                BossRushInkColors.SurfaceRaised, 17f, size, () => { if (onClick != null) onClick(); }, true);
            DisableUnityNavigation(btn);
            BossRushInkSkin.StyleInkButton(btn, role);
            return btn;
        }

        private void StepLightbox(int delta)
        {
            if (lightboxAssets == null || lightboxAssets.Count <= 1) return;
            lightboxIndex = (lightboxIndex + delta + lightboxAssets.Count) % lightboxAssets.Count;
            RefreshLightboxContent();
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
        }

        private void RefreshLightboxContent()
        {
            if (lightboxAssets == null || lightboxAssets.Count == 0) return;
            WikiIllustrationAsset asset = lightboxAssets[Mathf.Clamp(lightboxIndex, 0, lightboxAssets.Count - 1)];

            if (lightboxImage != null)
            {
                lightboxImage.sprite = asset.ResolveSprite();
            }
            if (lightboxCaption != null)
            {
                lightboxCaption.text = asset.GetCaption();
            }

            bool multi = lightboxAssets.Count > 1;
            if (lightboxPrevBtn != null) lightboxPrevBtn.gameObject.SetActive(multi);
            if (lightboxNextBtn != null) lightboxNextBtn.gameObject.SetActive(multi);
            if (lightboxCounter != null)
            {
                lightboxCounter.gameObject.SetActive(multi);
                lightboxCounter.text = (lightboxIndex + 1) + " / " + lightboxAssets.Count;
            }
        }
    }
}

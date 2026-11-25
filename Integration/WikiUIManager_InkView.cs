// ============================================================================
// WikiUIManager_InkView.cs - 冒险家日志水墨书卷界面构建与动效
// ============================================================================
// 从 WikiUIManager.cs 拆分（LargeFileBudgetGuard：单文件 1200 行上限），
// 负责左墨底目录选单与右旧宣纸展开卷轴的构建、水墨卡片样式及展开 / 落墨 / 翻页动效。
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BossRush
{
    public partial class WikiUIManager
    {
        // ============================================================================
        // 水墨双区界面构建（左墨底目录选单 + 右旧宣纸展开卷轴）
        // ============================================================================

        private void BuildUI(Transform canvasTransform)
        {
            BossRushUI.CreateBackdrop(canvasTransform);
            GameObject sheetObject = ZombieModeUIHelper.CreateRect("Sheet", canvasTransform, new Vector2(0.5f, 0.5f), SheetSize);
            sheet = sheetObject.GetComponent<RectTransform>();

            BuildDirectoryPanel(sheet);
            BuildScrollPanel(sheet);
        }

        private void BuildDirectoryPanel(Transform parent)
        {
            GameObject surface = ZombieModeUIHelper.CreateRect("Surface", parent, new Vector2(0.5f, 0.5f), PanelSize);
            surface.GetComponent<RectTransform>().anchoredPosition = new Vector2(PanelCenterX, 0f);
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.raycastTarget = true;
            BossRushInkSkin.ApplyInkPanel(surfaceImage, false);

            bool chinese = L10n.IsChinese;
            RectTransform title = BossRushInkSkin.CreateBrushTitle(surface.transform,
                L10n.T("冒险家日志", "Adventurer's Journal"),
                L10n.T("Adventurer's Journal", "BossRush · Qinglan"),
                chinese ? 40f : 26f);
            title.anchoredPosition = new Vector2(0f, 308f);
            if (!chinese) FitEnglishBrushTitle(title);

            BossRushInkSkin.CreateInkDivider(surface.transform, 500f, new Vector2(0f, 248f));

            BuildColumnHeader(surface.transform, L10n.T("典　籍", "CHAPTERS"), CategoryColumnX, 228f, 160f, TextAlignmentOptions.Center);
            txtEntryCount = BuildColumnHeader(surface.transform, L10n.T("篇　目", "ENTRIES"), EntryColumnX, 228f, 290f, TextAlignmentOptions.Left);

            // 垂直淡金分隔笔：隔开左侧典籍栏与右侧篇目栏
            BossRushInkSkin.CreateVerticalBrushStroke(surface.transform, "ColumnDivider", new Vector2(4f, 540f),
                new Vector2(-69f, ListAreaCenterY), Fade(BossRushInkColors.Gold, 0.22f));

            categoryScroll = CreateInkScrollArea(surface.transform, "Scroll_Categories",
                new Vector2(CategoryColumnX, ListAreaCenterY), new Vector2(CategoryColumnWidth, ListAreaHeight), out categoryContainer);
            entryScroll = CreateInkScrollArea(surface.transform, "Scroll_Entries",
                new Vector2(EntryColumnX, ListAreaCenterY), new Vector2(EntryColumnWidth, ListAreaHeight), out entryContainer);

            txtEmptyHint = ZombieModeUIHelper.CreateText("Txt_EmptyHint", surface.transform,
                L10n.T("此卷暂无条目。", "No entries in this chapter."), 15f,
                new Vector2(EntryColumnX, ListAreaCenterY), new Vector2(EntryColumnWidth - 28f, 60f),
                TextAlignmentOptions.Center, BossRushInkColors.PaperDim);
            BossRushUI.ApplyGameFont(txtEmptyHint);
            txtEmptyHint.enableWordWrapping = true;
            txtEmptyHint.overflowMode = TextOverflowModes.Ellipsis;
            txtEmptyHint.gameObject.SetActive(false);
        }

        private static void FitEnglishBrushTitle(RectTransform titleRoot)
        {
            if (titleRoot == null) return;
            Transform titleNode = titleRoot.Find("Title");
            TextMeshProUGUI label = titleNode != null ? titleNode.GetComponent<TextMeshProUGUI>() : null;
            if (label != null)
            {
                label.characterSpacing = 5f;
                float textWidth = Mathf.Clamp(label.GetPreferredValues(label.text).x, 120f, 420f);
                RectTransform underlayRect = titleRoot.Find("TitleUnderlay") as RectTransform;
                if (underlayRect != null)
                {
                    underlayRect.sizeDelta = new Vector2(textWidth + 84f, underlayRect.sizeDelta.y);
                }
                RectTransform strokeRect = titleRoot.Find("TitleStroke") as RectTransform;
                if (strokeRect != null)
                {
                    strokeRect.sizeDelta = new Vector2(textWidth + 32f, strokeRect.sizeDelta.y);
                }
            }
            Transform subNode = titleRoot.Find("Subtitle");
            TextMeshProUGUI sub = subNode != null ? subNode.GetComponent<TextMeshProUGUI>() : null;
            if (sub != null) sub.characterSpacing = 14f;
        }

        private static TextMeshProUGUI BuildColumnHeader(Transform parent, string text, float x, float y, float width, TextAlignmentOptions align)
        {
            TextMeshProUGUI header = ZombieModeUIHelper.CreateText("ColHeader", parent, text, 14f,
                new Vector2(x, y), new Vector2(width, 24f), align, BossRushInkColors.Gold);
            BossRushUI.ApplyGameFont(header);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = L10n.IsChinese ? 6f : 3f;
            header.enableWordWrapping = false;
            header.overflowMode = TextOverflowModes.Ellipsis;
            return header;
        }

        private static ScrollRect CreateInkScrollArea(Transform parent, string name, Vector2 position, Vector2 size, out Transform contentOut)
        {
            GameObject scrollObj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            scrollObj.GetComponent<RectTransform>().anchoredPosition = position;

            GameObject viewportObj = ZombieModeUIHelper.CreateRect("Viewport", scrollObj.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportObj.AddComponent<RectMask2D>();

            GameObject contentObj = ZombieModeUIHelper.CreateRect("Content", viewportRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            RectTransform contentRect = contentObj.GetComponent<RectTransform>();

            // 左右留 6px 让出 4px 外扩的墨笔边框，右侧留 22px 让出共享滚动条，避免被 RectMask2D 裁掉边框
            VerticalLayoutGroup layout = contentObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 22, 6, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            BossRushUI.ConfigureScrollRect(scroll);

            contentOut = contentRect;
            return scroll;
        }

        // ---- 右侧：旧宣纸展开卷轴 ----

        private void BuildScrollPanel(Transform parent)
        {
            GameObject root = ZombieModeUIHelper.CreateRect("Notice", parent, new Vector2(0.5f, 0.5f), new Vector2(NoticeSize.x + 80f, NoticeSize.y + 60f));
            notice = root.GetComponent<RectTransform>();
            notice.anchoredPosition = new Vector2(NoticeCenterX, 0f);
            float top = NoticeSize.y * 0.5f;

            // 卷开遮罩：从上轴往下伸展，纸面本身尺寸固定贴顶，不拉伸变形
            GameObject maskObject = ZombieModeUIHelper.CreateRect("Unroll", notice, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, top), new Vector2(NoticeSize.x + 40f, NoticeSize.y), new Vector2(0.5f, 1f));
            unrollMask = maskObject.GetComponent<RectTransform>();
            maskObject.AddComponent<RectMask2D>();

            GameObject paperObject = ZombieModeUIHelper.CreateRect("Paper", unrollMask, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, NoticeSize, new Vector2(0.5f, 1f));
            paper = paperObject.GetComponent<RectTransform>();
            Image paperImage = paperObject.AddComponent<Image>();
            paperImage.sprite = BossRushInkSkin.GetSurfaceSprite();
            paperImage.type = Image.Type.Sliced;
            paperImage.pixelsPerUnitMultiplier = 1.3f;
            paperImage.color = NoticePaper;
            paperImage.raycastTarget = true;

            Image fiber = CreatePaperLayer(paper, "Fiber", BossRushInkSkin.GetFiberSprite(), new Color(0.30f, 0.20f, 0.10f, 0.20f), 10f);
            fiber.type = Image.Type.Tiled;
            CreateStain(paper, new Vector2(-340f, 290f), 250f, 18f);
            CreateStain(paper, new Vector2(350f, -260f), 290f, -28f);
            CreateStain(paper, new Vector2(-110f, -310f), 210f, 11f);
            Image border = CreatePaperLayer(paper, "Border", BossRushInkSkin.GetFrameSprite(), Fade(NoticeInk, 0.62f), 18f);
            border.type = Image.Type.Sliced;
            border.fillCenter = false;
            border.pixelsPerUnitMultiplier = 1.5f;
            Image inner = CreatePaperLayer(paper, "BorderInner", BossRushInkSkin.GetFrameSprite(), Fade(NoticeInk, 0.22f), 30f);
            inner.type = Image.Type.Sliced;
            inner.fillCenter = false;
            inner.pixelsPerUnitMultiplier = 2.4f;

            BuildScrollContentSkeleton();
            BuildScrollFooter();

            CreateRod("RodTop", top + 2f);
            bottomRod = CreateRod("RodBottom", -top - 2f);
        }

        private void BuildScrollContentSkeleton()
        {
            GameObject holder = ZombieModeUIHelper.CreateRect("WritHolder", paper, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            writHolder = holder.GetComponent<RectTransform>();

            GameObject writObj = ZombieModeUIHelper.CreateRect("Writ", writHolder, new Vector2(0.5f, 0.5f), NoticeSize);
            writ = writObj.GetComponent<RectTransform>();
            CanvasGroup writGroup = writObj.AddComponent<CanvasGroup>();
            // 允许正文内超链接接收指针点击
            writGroup.blocksRaycasts = true;
            writGroup.interactable = true;

            bool chinese = L10n.IsChinese;
            txtKicker = CreatePaperText(writ, "Kicker", "", 14f, NoticeCinnabar, TextAlignmentOptions.Center, false);
            txtKicker.fontStyle = FontStyles.Bold;
            txtKicker.characterSpacing = chinese ? 10f : 6f;
            txtKicker.rectTransform.sizeDelta = new Vector2(280f, 26f);
            txtKicker.rectTransform.anchoredPosition = new Vector2(0f, 360f);

            for (int side = -1; side <= 1; side += 2)
            {
                BossRushInkSkin.CreateBrushStroke(writ, "KickerStroke", new Vector2(64f, 4f),
                    new Vector2(side * 124f, 360f), Fade(NoticeCinnabar, 0.68f));
            }

            txtArticleTitle = CreatePaperText(writ, "Txt_Title", "", chinese ? 34f : 26f, NoticeInk, TextAlignmentOptions.Center, false);
            txtArticleTitle.fontStyle = FontStyles.Bold;
            txtArticleTitle.characterSpacing = chinese ? 6f : 2f;
            txtArticleTitle.enableAutoSizing = true;
            txtArticleTitle.fontSizeMin = 16f;
            txtArticleTitle.fontSizeMax = chinese ? 34f : 26f;
            txtArticleTitle.rectTransform.sizeDelta = new Vector2(700f, 54f);
            txtArticleTitle.rectTransform.anchoredPosition = new Vector2(0f, 314f);

            titleStroke = BossRushInkSkin.CreateBrushStroke(writ, "TitleStroke", new Vector2(540f, 10f),
                new Vector2(0f, 276f), Fade(NoticeInk, 0.85f));

            // 书缝与双栏页面容器
            GameObject pagesObj = ZombieModeUIHelper.CreateRect("Pages", writ, new Vector2(0.5f, 0.5f), NoticeSize);
            pagesGroup = pagesObj.GetComponent<RectTransform>();
            pagesCanvasGroup = pagesObj.AddComponent<CanvasGroup>();
            pagesCanvasGroup.blocksRaycasts = true;
            pagesCanvasGroup.interactable = true;

            BossRushInkSkin.CreateVerticalBrushStroke(pagesGroup, "CenterSpine", new Vector2(4f, 520f),
                new Vector2(0f, ColumnCenterY), Fade(NoticeInk, 0.16f));

            txtLeft = CreateArticleColumn(pagesGroup, "Txt_Left", -ColumnCenterX);
            txtRight = CreateArticleColumn(pagesGroup, "Txt_Right", ColumnCenterX);

            BossRushInkSkin.CreateBrushStroke(paper, "FooterDivider", new Vector2(820f, 6f),
                new Vector2(0f, -322f), Fade(NoticeInk, 0.25f));
        }

        private Button CreateScrollButton(string name, string text, Vector2 pos, Vector2 size, float fontSize,
            BossRushInkButtonRole role, UnityEngine.Events.UnityAction onClick)
        {
            Color bg = role == BossRushInkButtonRole.Primary ? BossRushInkColors.CinnabarFill : BossRushInkColors.SurfaceRaised;
            Button button = ZombieModeUIHelper.CreateButton(name, paper, text, new Vector2(0.5f, 0.5f), pos, size,
                bg, fontSize, new Vector2(size.x - 16f, size.y - 10f), onClick, true);
            DisableUnityNavigation(button);
            BossRushInkSkin.StyleInkButton(button, role);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.enableAutoSizing = true;
                label.fontSizeMin = 14f;
                label.fontSizeMax = fontSize;
            }
            return button;
        }

        /// <summary>
        /// 一栏正文：TMP 直接挂在 <see cref="pagesGroup"/> 上，与插图宿主同一个坐标系。
        /// 列框（<see cref="ApplyColumnBox"/>）写的是 pagesGroup 里的绝对坐标（±ColumnCenterX, ColumnCenterY）；
        /// 若再套一层已经摆在列中心的页容器，同一组坐标会叠两次，正文甩到 ±444 出纸面。
        /// </summary>
        private static TMP_Text CreateArticleColumn(Transform parent, string textName, float centerX)
        {
            GameObject textObj = ZombieModeUIHelper.CreateRect(textName, parent, new Vector2(0.5f, 0.5f), new Vector2(ColumnWidth, ColumnHeight));
            textObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(centerX, ColumnCenterY);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(tmp);
            tmp.text = "";
            tmp.richText = true;
            tmp.raycastTarget = true;
            return tmp;
        }

        private RectTransform CreateRod(string name, float y)
        {
            float width = NoticeSize.x + 44f;
            GameObject rodObject = ZombieModeUIHelper.CreateRect(name, notice, new Vector2(0.5f, 0.5f), new Vector2(width, 20f));
            RectTransform rod = rodObject.GetComponent<RectTransform>();
            rod.anchoredPosition = new Vector2(0f, y);
            Image wood = rodObject.AddComponent<Image>();
            wood.sprite = BossRushInkSkin.GetSurfaceSprite();
            wood.type = Image.Type.Sliced;
            wood.pixelsPerUnitMultiplier = 3.2f;
            wood.color = RodWood;
            wood.raycastTarget = false;
            BossRushInkSkin.CreateBrushStroke(rod, "Sheen", new Vector2(width - 36f, 3f), new Vector2(0f, 4f), Fade(BossRushInkColors.Gold, 0.30f));
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject capObject = ZombieModeUIHelper.CreateRect("Cap", rod, new Vector2(0.5f, 0.5f), new Vector2(16f, 32f));
                capObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(side * (width * 0.5f + 6f), 0f);
                Image cap = capObject.AddComponent<Image>();
                cap.sprite = BossRushInkSkin.GetSurfaceSprite();
                cap.type = Image.Type.Sliced;
                cap.pixelsPerUnitMultiplier = 4f;
                cap.color = RodCap;
                cap.raycastTarget = false;
            }
            return rod;
        }

        private static Image CreatePaperLayer(RectTransform parent, string name, Sprite sprite, Color color, float inset)
        {
            GameObject layer = ZombieModeUIHelper.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = layer.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void CreateStain(RectTransform parent, Vector2 position, float size, float angle)
        {
            GameObject stain = ZombieModeUIHelper.CreateRect("Stain", parent, new Vector2(0.5f, 0.5f), new Vector2(size, size * 0.8f));
            RectTransform rect = stain.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image image = stain.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetBlotSprite();
            image.color = Stain;
            image.raycastTarget = false;
        }

        private static TextMeshProUGUI CreatePaperText(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions alignment, bool wrap)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(name, parent, text ?? string.Empty, size, Vector2.zero,
                new Vector2(NoticeSize.x - 100f, Mathf.Ceil(size * 1.45f) + 4f), alignment, color);
            BossRushUI.ApplyGameFont(label);
            label.raycastTarget = false;
            label.enableWordWrapping = wrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private void UpdateCornerSeal(string categoryId)
        {
            if (writ == null) return;
            if (cornerSeal != null) UnityEngine.Object.Destroy(cornerSeal.gameObject);
            string sealText = ResolveCategorySeal(categoryId);
            cornerSeal = BossRushInkSkin.CreateSeal(writ, sealText, 48f);
            cornerSeal.anchoredPosition = new Vector2(394f, 344f);
            cornerSeal.localRotation = Quaternion.Euler(0f, 0f, -7f);
            Image sealImage = cornerSeal.GetComponent<Image>();
            if (sealImage != null) sealImage.color = Fade(NoticeCinnabar, 0.86f);
        }

        private static string ResolveCategorySeal(string categoryId)
        {
            if (!L10n.IsChinese) return "LOG";
            if (string.IsNullOrEmpty(categoryId)) return "秘录";
            switch (categoryId.ToLowerInvariant())
            {
                case "start": return "初览";
                case "mode": return "诸界";
                case "map": return "舆图";
                case "boss": return "群魔";
                case "npc": return "故人";
                case "equipment": return "兵录";
                case "item": return "百宝";
                case "system": return "枢机";
                case "config": return "司律";
                case "tips": return "要诀";
                case "easter": return "逸闻";
                case "changelog": return "纪年";
                default: return "秘录";
            }
        }

        // ============================================================================
        // 水墨动效
        // ============================================================================

        private void PlayUnroll(float delay)
        {
            if (notice == null || unrollMask == null || bottomRod == null) return;
            float full = NoticeSize.y;
            float top = full * 0.5f;
            RectTransform mask = unrollMask;
            RectTransform rod = bottomRod;
            mask.sizeDelta = new Vector2(mask.sizeDelta.x, 0f);
            rod.anchoredPosition = new Vector2(0f, top - 2f);
            BossRushInkMotion motion = BossRushInkMotion.For(notice.gameObject);
            motion.Add(delay, UnrollSeconds, true, t =>
            {
                float h = full * t;
                if (mask != null) mask.sizeDelta = new Vector2(mask.sizeDelta.x, h);
                if (rod != null) rod.anchoredPosition = new Vector2(0f, top - h - 2f);
            });
            motion.Add(delay, 0.01f, false, t => { }, () => BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn));
        }

        private void PlayArticleInkRefresh()
        {
            if (writ == null) return;
            PlayThrottledPaperSfx();

            int seq = ++articleAnimSeq;
            // 取消翻页残留微动，避免切篇与翻页两条曲线叠加
            pageAnimSeq++;
            if (pagesCanvasGroup != null) pagesCanvasGroup.alpha = 1f;
            if (pagesGroup != null) pagesGroup.anchoredPosition = Vector2.zero;

            CanvasGroup group = writ.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 0f;
            writ.anchoredPosition = new Vector2(0f, -6f);
            RectTransform targetWrit = writ;
            BossRushInkMotion motion = BossRushInkMotion.For(writ.gameObject);
            motion.Add(0f, 0.26f, true, t =>
            {
                if (targetWrit == null || seq != articleAnimSeq) return;
                if (group != null) group.alpha = t;
                targetWrit.anchoredPosition = new Vector2(0f, -6f * (1f - t));
            });

            if (titleStroke != null)
            {
                RectTransform oldStroke = titleStroke;
                Vector2 strokePos = oldStroke.anchoredPosition;
                Color strokeColor = Fade(NoticeInk, 0.85f);
                UnityEngine.Object.Destroy(oldStroke.gameObject);
                titleStroke = BossRushInkSkin.CreateBrushStroke(writ, "TitleStroke", new Vector2(540f, 10f), strokePos, strokeColor);
                BossRushInkSkin.PlayBrushWrite(titleStroke, 0.04f, 0.34f, BossRushInkMotion.For(titleStroke.gameObject));
            }
        }

        private void PlayPageTurnInkRefresh()
        {
            if (pagesGroup == null || pagesCanvasGroup == null) return;
            PlayThrottledPaperSfx();

            int seq = ++pageAnimSeq;
            pagesCanvasGroup.alpha = 0f;
            pagesGroup.anchoredPosition = new Vector2(0f, -4f);
            RectTransform targetPages = pagesGroup;
            CanvasGroup group = pagesCanvasGroup;
            BossRushInkMotion.For(pagesGroup.gameObject).Add(0f, 0.22f, true, t =>
            {
                if (targetPages == null || seq != pageAnimSeq) return;
                if (group != null) group.alpha = t;
                targetPages.anchoredPosition = new Vector2(0f, -4f * (1f - t));
            });
        }

        private void PlayThrottledPaperSfx()
        {
            if (Time.unscaledTime - lastPaperSfxTime < 0.16f) return;
            lastPaperSfxTime = Time.unscaledTime;
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
        }

        // ============================================================================
        // 目录与条目水墨卡片构造
        // ============================================================================

        private NavCard CreateNavCard(Transform container, string objectName, string id, string displayText, float width, float height, Action onClick)
        {
            Button button = ZombieModeUIHelper.CreateButton(objectName, container, string.Empty,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height),
                BossRushInkColors.SurfaceRaised, 16f, new Vector2(width, height),
                () => { if (!closing && onClick != null) onClick(); }, true);
            DisableUnityNavigation(button);
            BossRushInkSkin.StyleInkButton(button, BossRushInkButtonRole.Secondary);

            LayoutElement layoutElement = button.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = height;

            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Name", button.transform, displayText, 16f,
                Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(width - 26f, height - 8f),
                TextAlignmentOptions.Left, BossRushInkColors.PaperDim);
            BossRushUI.ApplyGameFont(label);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(14f, 4f);
            label.rectTransform.offsetMax = new Vector2(-12f, -4f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            float maxSize = label.fontSize > 0 ? label.fontSize : 16f;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Min(14f, maxSize);
            label.fontSizeMax = maxSize;

            float underlineWidth = Mathf.Max(64f, width * 0.52f);
            RectTransform underline = BossRushInkSkin.CreateBrushStroke(button.transform, "Underline",
                new Vector2(underlineWidth, 5f), Vector2.zero, BossRushInkColors.Gold);
            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(0f, 0f);
            underline.pivot = new Vector2(0f, 0.5f);
            underline.anchoredPosition = new Vector2(12f, 5f);
            underline.sizeDelta = new Vector2(0f, 5f);

            return new NavCard
            {
                Id = id,
                Button = button,
                Rect = button.GetComponent<RectTransform>(),
                Label = label,
                Underline = underline,
                UnderlineWidth = underlineWidth
            };
        }

        private static void DisableUnityNavigation(Button button)
        {
            if (button == null) return;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
        }

        private static void ApplyCardVisual(NavCard card, bool selected, bool animate)
        {
            if (card == null || card.Button == null) return;
            ZombieModeUIHelper.SetButtonBaseColor(card.Button, selected ? SelectedFill : BossRushInkColors.SurfaceRaised);
            BossRushInkSkin.SetFrameColor(card.Button.targetGraphic, selected ? BossRushInkColors.Gold : IdleFrame);
            if (card.Label != null)
            {
                card.Label.color = selected ? BossRushInkColors.Paper : BossRushInkColors.PaperDim;
                card.Label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            }

            bool wasSelected = card.Selected;
            card.Selected = selected;
            if (card.Underline == null) return;

            RectTransform underline = card.Underline;
            float full = card.UnderlineWidth;
            int seq = ++card.AnimSeq;
            if (!selected)
            {
                underline.sizeDelta = new Vector2(0f, underline.sizeDelta.y);
                return;
            }
            if (!animate)
            {
                underline.sizeDelta = new Vector2(full, underline.sizeDelta.y);
                return;
            }
            if (wasSelected) return;
            underline.sizeDelta = new Vector2(0f, underline.sizeDelta.y);
            BossRushInkMotion.For(card.Rect.gameObject).Add(0f, 0.30f, true, t =>
            {
                if (underline != null && seq == card.AnimSeq) underline.sizeDelta = new Vector2(full * t, underline.sizeDelta.y);
            });
        }
    }
}

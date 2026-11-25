// ============================================================================
// WikiUIManager.cs - 冒险家日志（游戏内 Wiki）水墨书卷 UI 管理器
// ============================================================================
// 模块说明：
//   2026-11-25 改为纯代码构建的水墨东方双区对照书卷界面（风格对齐 WavesArena/ArenaExpeditionOrderPanel.cs）：
//   - 左侧深墨面板（BossRushInkSkin.ApplyInkPanel）：毛笔大标题「冒险家日志」、典籍分类 + 篇目两栏滚动列表；
//     选中的卡片亮鎏金墨笔框 + 从左向右写出金色毛笔下划线。
//   - 右侧旧宣纸展开卷轴（Notice）：上下木轴打开时从上往下卷开并响翻纸音，纸面带宣纸纤维、淡茶渍与焦墨双框；
//     切篇时整张正文重新落墨、标题焦墨横画重描；双栏 TMP 连续分片分页支持链接点击与翻页音效；
//     底栏放翻页控件与「在线 Wiki / 合卷」（主操作朱砂底放最右）。
//   - 模态与输入：ZombieModeUIHelper.ClaimModalInput("Wiki") 占模态租约，PetNestCancelKey 消费 ESC 直接合卷。
//   - 兼容性：保留 SetUIPrefab / UIPrefab 公开接口供 BossRushIntegrationRuntimeModule_WikiBook 等已有调用方使用；
//     严格保留 SetupContentWithTMPPaging / BuildArticlePageSlices / NormalizeArticlePageSlices 连续分片不溢出机制。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BossRush
{
    /// <summary>
    /// Wiki UI 管理器（单例，水墨书卷版）
    /// </summary>
    public partial class WikiUIManager
    {
        private const string RootName = "BossRush_WikiBookUI";
        private const string ExternalWikiCategoryId = "_wiki_link";
        private const string ExternalWikiUrl = "https://bossrushmod.pages.dev/";

        // 整页布局尺寸（1920x1080 Expand 参考画布）
        private static readonly Vector2 SheetSize = new Vector2(1580f, 900f);
        private static readonly Vector2 PanelSize = new Vector2(560f, 780f);
        private const float PanelCenterX = -490f;
        private const float CategoryColumnX = -168f;
        private const float CategoryColumnWidth = 188f;
        private const float CategoryCardWidth = 158f;
        private const float CategoryCardHeight = 42f;
        private const float EntryColumnX = 98f;
        private const float EntryColumnWidth = 324f;
        private const float EntryCardWidth = 294f;
        private const float EntryCardHeight = 46f;
        private const float ListAreaCenterY = -77f;
        private const float ListAreaHeight = 570f;

        private static readonly Vector2 NoticeSize = new Vector2(940f, 840f);
        private const float NoticeCenterX = 300f;
        private const float ColumnWidth = 400f;
        private const float ColumnHeight = 566f;
        private const float ColumnCenterX = 222f;
        private const float ColumnCenterY = -27f;
        private const float FooterY = -362f;
        private const float UnrollSeconds = 0.6f;

        // 水墨配色（与 ArenaExpeditionOrderPanel 同源）
        private static readonly Color SelectedFill = new Color(0.175f, 0.145f, 0.115f, 0.97f);
        private static readonly Color IdleFrame = new Color(BossRushInkColors.Gold.r, BossRushInkColors.Gold.g, BossRushInkColors.Gold.b, 0.28f);
        private static readonly Color NoticePaper = new Color(0.800f, 0.725f, 0.585f, 0.985f);
        private static readonly Color NoticeInk = new Color(0.120f, 0.090f, 0.070f, 1f);
        private static readonly Color NoticeInkSoft = new Color(0.300f, 0.240f, 0.185f, 1f);
        private static readonly Color NoticeCinnabar = new Color(0.560f, 0.130f, 0.100f, 1f);
        private static readonly Color RodWood = new Color(0.095f, 0.065f, 0.048f, 1f);
        private static readonly Color RodCap = new Color(0.560f, 0.430f, 0.180f, 1f);
        private static readonly Color Stain = new Color(0.420f, 0.280f, 0.140f, 0.10f);

        // ============================================================================
        // 单例与兼容接口
        // ============================================================================

        private static WikiUIManager _instance;
        public static WikiUIManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WikiUIManager();
                }
                return _instance;
            }
        }

        private sealed class NavCard
        {
            internal string Id;
            internal Button Button;
            internal RectTransform Rect;
            internal TextMeshProUGUI Label;
            internal RectTransform Underline;
            internal float UnderlineWidth;
            internal bool Selected;
            internal int AnimSeq;
        }

        private sealed class WikiLifecycleHook : MonoBehaviour
        {
            internal WikiUIManager Owner;

            private void Update()
            {
                if (Owner != null) Owner.CheckLanguageChange(this);
            }

            private void OnDestroy()
            {
                if (Owner != null) Owner.OnHostDestroyed(gameObject);
            }
        }

        // ============================================================================
        // 状态与节点引用
        // ============================================================================

        public bool IsUIOpen { get; private set; }
        internal GameObject UIPrefab { get { return uiPrefab; } }

        private GameObject uiPrefab = null;
        private GameObject uiRoot = null;
        private RectTransform sheet = null;
        private RectTransform notice = null;
        private RectTransform unrollMask = null;
        private RectTransform bottomRod = null;
        private RectTransform paper = null;
        private RectTransform writHolder = null;
        private RectTransform writ = null;
        private RectTransform pagesGroup = null;
        private CanvasGroup pagesCanvasGroup = null;
        private RectTransform titleStroke = null;
        private RectTransform cornerSeal = null;

        private ScrollRect categoryScroll = null;
        private Transform categoryContainer = null;
        private ScrollRect entryScroll = null;
        private Transform entryContainer = null;
        private TextMeshProUGUI txtEntryCount = null;
        private TextMeshProUGUI txtEmptyHint = null;

        private TextMeshProUGUI txtKicker = null;
        private TextMeshProUGUI txtArticleTitle = null;
        private TMP_Text txtLeft = null;
        private TMP_Text txtRight = null;
        private TextMeshProUGUI txtPageNumber = null;
        private Button btnPrevPage = null;
        private Button btnNextPage = null;
        private Button btnOnlineWiki = null;
        private Button btnClose = null;

        private ZombieModeUIHelper.ModalInputLease modalLease = null;
        private PetNestCancelKey cancelKey = null;
        private bool closing = false;
        private bool renderedChinese = true;
        private float lastPaperSfxTime = -100f;
        private int articleAnimSeq = 0;
        private int pageAnimSeq = 0;

        private string currentCategoryId = null;
        private string currentEntryId = null;
        private int currentPageIndex = 0;
        private int totalPages = 1;
        private int BookTotalPages => (totalPages + 1) / 2;

        private readonly List<NavCard> categoryCards = new List<NavCard>();
        private readonly List<NavCard> entryCards = new List<NavCard>();

        private string currentParsedContent = "";
        private readonly List<string> currentArticlePages = new List<string>();
        private int leftPageToDisplay = 1;
        private int rightPageToDisplay = 2;

        // ============================================================================
        // 公共方法
        // ============================================================================

        public void SetUIPrefab(GameObject prefab)
        {
            uiPrefab = prefab;
            ModBehaviour.DevLog("[WikiUI] Prefab 已登记（运行时走纯代码水墨书卷）");
        }

        public void OpenUI()
        {
            if (IsUIOpen) return;

            try
            {
                if (uiRoot != null)
                {
                    UnityEngine.Object.Destroy(uiRoot);
                    ClearCanvasReferences();
                }

                closing = false;
                WikiContentManager.Instance.LoadCatalog();
                renderedChinese = L10n.IsChinese;

                Canvas canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.Modal, true);
                uiRoot = canvas.gameObject;
                WikiLifecycleHook hook = uiRoot.AddComponent<WikiLifecycleHook>();
                hook.Owner = this;

                BuildUI(canvas.transform);

                List<WikiCategory> categories = WikiContentManager.Instance.GetCategories();
                currentCategoryId = ResolveContentCategoryId(categories);
                RefreshCategoryList();
                RefreshEntryList(currentCategoryId);
                currentEntryId = ResolveEntryIdForCategory(currentCategoryId, currentEntryId);
                ApplyCategorySelectionStates(false);
                ApplyEntrySelectionStates(false);
                ShowArticlePage(currentEntryId, false);

                modalLease = ZombieModeUIHelper.ClaimModalInput(uiRoot, "Wiki");
                cancelKey = PetNestCancelKey.Attach(uiRoot, OnCancelKeyPressed, null);
                IsUIOpen = true;

                BossRushInkSkin.PlayInkReveal(sheet);
                PlayUnroll(0.22f);
                ScrollSelectedCardsIntoView();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 打开失败: " + e.Message);
                CloseImmediate();
            }
        }

        public void CloseUI()
        {
            if (!IsUIOpen || closing) return;

            try
            {
                CloseLightbox();
                IsUIOpen = false;
                closing = true;
                ReleaseInput();
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);

                if (uiRoot != null)
                {
                    BossRushInkSkin.PlayInkDismiss(uiRoot, sheet);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 关闭失败: " + e.Message);
                CloseImmediate();
            }
        }

        internal static void Shutdown()
        {
            WikiIllustrations.ResetStaticCaches();
            WikiUIManager current = _instance;
            if (current == null) return;
            current.CloseImmediate();
            current.uiPrefab = null;
            _instance = null;
        }

        private void CloseImmediate()
        {
            IsUIOpen = false;
            closing = false;
            ReleaseInput();
            GameObject root = uiRoot;
            ClearCanvasReferences();
            if (root != null) UnityEngine.Object.Destroy(root);
        }

        private void OnHostDestroyed(GameObject destroyedRoot)
        {
            if (uiRoot != null && uiRoot != destroyedRoot) return;
            IsUIOpen = false;
            closing = false;
            ReleaseInput();
            ClearCanvasReferences();
        }

        private void CheckLanguageChange(WikiLifecycleHook hook)
        {
            if (!IsUIOpen || closing || hook == null || hook.gameObject != uiRoot) return;
            if (renderedChinese == L10n.IsChinese) return;

            // 语言热切换：原位重建整张水墨界面并保住当前分类与篇目
            string preservedCategory = currentCategoryId;
            string preservedEntry = currentEntryId;
            int preservedPage = currentPageIndex;
            CloseImmediate();
            currentCategoryId = preservedCategory;
            currentEntryId = preservedEntry;
            OpenUI();
            if (IsUIOpen && preservedPage > 0 && BookTotalPages > 1)
            {
                currentPageIndex = Mathf.Clamp(preservedPage, 0, BookTotalPages - 1);
                RefreshArticleContent();
            }
        }

        private void ClearCanvasReferences()
        {
            uiRoot = null;
            sheet = null;
            notice = null;
            unrollMask = null;
            bottomRod = null;
            paper = null;
            writHolder = null;
            writ = null;
            pagesGroup = null;
            pagesCanvasGroup = null;
            titleStroke = null;
            cornerSeal = null;
            categoryScroll = null;
            categoryContainer = null;
            entryScroll = null;
            entryContainer = null;
            txtEntryCount = null;
            txtEmptyHint = null;
            txtKicker = null;
            txtArticleTitle = null;
            txtLeft = null;
            txtRight = null;
            txtPageNumber = null;
            btnPrevPage = null;
            btnNextPage = null;
            btnOnlineWiki = null;
            btnClose = null;
            categoryCards.Clear();
            entryCards.Clear();
            ClearIllustrationReferences();
        }

        private void BuildScrollFooter()
        {
            btnPrevPage = CreateScrollButton("Btn_PrevPage", L10n.T("上一页", "Prev"), new Vector2(-346f, FooterY), new Vector2(124f, 44f),
                17f, BossRushInkButtonRole.Secondary, PrevPage);

            txtPageNumber = ZombieModeUIHelper.CreateText("Txt_PageNumber", paper, "1 / 1", 15f,
                new Vector2(-218f, FooterY), new Vector2(120f, 34f), TextAlignmentOptions.Center, NoticeInkSoft);
            BossRushUI.ApplyGameFont(txtPageNumber);
            txtPageNumber.fontStyle = FontStyles.Bold;
            txtPageNumber.enableWordWrapping = false;
            txtPageNumber.overflowMode = TextOverflowModes.Ellipsis;

            btnNextPage = CreateScrollButton("Btn_NextPage", L10n.T("下一页", "Next"), new Vector2(-90f, FooterY), new Vector2(124f, 44f),
                17f, BossRushInkButtonRole.Secondary, NextPage);

            btnOnlineWiki = CreateScrollButton("Btn_OnlineWiki", L10n.T("在线 Wiki", "Online Wiki"), new Vector2(194f, FooterY), new Vector2(156f, 46f),
                17f, BossRushInkButtonRole.Secondary, OpenExternalWiki);

            btnClose = CreateScrollButton("Btn_Close", L10n.T("合　卷", "Close"), new Vector2(358f, FooterY), new Vector2(136f, 46f),
                19f, BossRushInkButtonRole.Primary, CloseUI);
        }

        private static void ClearContainer(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(container.GetChild(i).gameObject);
            }
        }

        private void RefreshCategoryList()
        {
            categoryCards.Clear();
            if (categoryContainer == null) return;
            ClearContainer(categoryContainer);

            List<WikiCategory> categories = WikiContentManager.Instance.GetCategories();
            if (categories == null || categories.Count == 0) return;

            foreach (WikiCategory category in categories)
            {
                // 外链不是典籍分类（A-31）：有底栏「在线 Wiki」按钮时不进分类列表；建不出来才留在这里兜底
                if (category.Id == ExternalWikiCategoryId && btnOnlineWiki != null) continue;

                string categoryId = category.Id;
                NavCard card = CreateNavCard(categoryContainer, "Category_" + categoryId, categoryId,
                    category.GetTitle(), CategoryCardWidth, CategoryCardHeight, () => SelectCategory(categoryId));
                categoryCards.Add(card);
            }
        }

        private void RefreshEntryList(string categoryId)
        {
            entryCards.Clear();
            if (entryContainer == null) return;
            ClearContainer(entryContainer);

            List<WikiEntry> entries = WikiContentManager.Instance.GetEntries(categoryId);
            int count = entries != null ? entries.Count : 0;

            if (txtEntryCount != null)
            {
                txtEntryCount.text = count > 0
                    ? L10n.T("篇　目 · " + count, "ENTRIES · " + count)
                    : L10n.T("篇　目", "ENTRIES");
            }

            if (txtEmptyHint != null)
            {
                txtEmptyHint.gameObject.SetActive(count == 0);
            }

            if (entryScroll != null) entryScroll.verticalNormalizedPosition = 1f;
            if (count == 0) return;

            foreach (WikiEntry entry in entries)
            {
                string entryId = entry.Id;
                NavCard card = CreateNavCard(entryContainer, "Entry_" + entryId, entryId,
                    entry.GetTitle(), EntryCardWidth, EntryCardHeight, () => SelectEntry(entryId));
                entryCards.Add(card);
            }
        }

        private void SelectCategory(string categoryId)
        {
            if (categoryId == ExternalWikiCategoryId)
            {
                OpenExternalWiki();
                return;
            }
            if (currentCategoryId == categoryId) return;

            CloseLightbox();
            currentCategoryId = categoryId;
            RefreshEntryList(categoryId);
            currentEntryId = ResolveEntryIdForCategory(categoryId, null);
            ApplyCategorySelectionStates(true);
            ApplyEntrySelectionStates(true);
            ShowArticlePage(currentEntryId, true);
        }

        private void SelectEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId) || currentEntryId == entryId) return;
            CloseLightbox();
            currentEntryId = entryId;
            ApplyEntrySelectionStates(true);
            ShowArticlePage(entryId, true);
        }

        private void ApplyCategorySelectionStates(bool animate)
        {
            for (int i = 0; i < categoryCards.Count; i++)
            {
                ApplyCardVisual(categoryCards[i], categoryCards[i].Id == currentCategoryId, animate);
            }
        }

        private void ApplyEntrySelectionStates(bool animate)
        {
            for (int i = 0; i < entryCards.Count; i++)
            {
                ApplyCardVisual(entryCards[i], entryCards[i].Id == currentEntryId, animate);
            }
        }

        private string ResolveContentCategoryId(List<WikiCategory> categories)
        {
            if (categories == null || categories.Count == 0) return null;

            if (!string.IsNullOrEmpty(currentCategoryId) && currentCategoryId != ExternalWikiCategoryId)
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    if (categories[i].Id == currentCategoryId) return currentCategoryId;
                }
            }

            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i].Id != ExternalWikiCategoryId) return categories[i].Id;
            }
            return null;
        }

        private static string ResolveEntryIdForCategory(string categoryId, string preferredEntryId)
        {
            List<WikiEntry> entries = WikiContentManager.Instance.GetEntries(categoryId);
            if (entries == null || entries.Count == 0) return null;

            if (!string.IsNullOrEmpty(preferredEntryId))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Id == preferredEntryId) return preferredEntryId;
                }
            }
            return entries[0].Id;
        }

        private void ScrollSelectedCardsIntoView()
        {
            Canvas.ForceUpdateCanvases();
            ScrollSelectedIntoView(categoryScroll, categoryCards, currentCategoryId);
            ScrollSelectedIntoView(entryScroll, entryCards, currentEntryId);
        }

        private static void ScrollSelectedIntoView(ScrollRect scroll, List<NavCard> cards, string selectedId)
        {
            if (scroll == null || scroll.content == null || scroll.viewport == null || string.IsNullOrEmpty(selectedId)) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);

            NavCard target = null;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Id == selectedId) { target = cards[i]; break; }
            }
            if (target == null || target.Rect == null) return;

            float contentHeight = scroll.content.rect.height;
            float viewportHeight = scroll.viewport.rect.height;
            float scrollable = contentHeight - viewportHeight;
            if (scrollable <= 1f) return;

            float itemTop = -target.Rect.anchoredPosition.y - target.Rect.rect.height * 0.5f;
            float centered = itemTop - (viewportHeight - target.Rect.rect.height) * 0.5f;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - centered / scrollable);
        }

        private void ShowArticlePage(string entryId, bool animate)
        {
            List<WikiCategory> categories = WikiContentManager.Instance.GetCategories();
            WikiCategory activeCat = null;
            if (categories != null)
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    if (categories[i].Id == currentCategoryId) { activeCat = categories[i]; break; }
                }
            }

            if (txtKicker != null)
            {
                txtKicker.text = activeCat != null ? activeCat.GetTitle().ToUpperInvariant() : L10n.T("典　籍", "JOURNAL");
            }
            UpdateCornerSeal(currentCategoryId);

            if (string.IsNullOrEmpty(entryId))
            {
                if (txtArticleTitle != null) txtArticleTitle.text = L10n.T("暂无篇目", "No Entry");
                SetupContentWithTMPPaging(L10n.T("请从左侧选择一篇典籍阅读。", "Select an entry from the left to read."));
                RefreshArticleContent();
                if (animate) PlayArticleInkRefresh();
                return;
            }

            WikiEntry entry = WikiContentManager.Instance.GetEntry(entryId);
            string content = WikiContentManager.Instance.LoadEntryContent(entryId);

            if (txtArticleTitle != null)
            {
                txtArticleTitle.text = entry != null ? entry.GetTitle() : entryId;
            }

            SetupContentWithTMPPaging(content);
            currentPageIndex = 0;
            RefreshArticleContent();
            if (animate) PlayArticleInkRefresh();
        }

        private void OpenExternalWiki()
        {
            Application.OpenURL(ExternalWikiUrl);
        }

        // ============================================================================
        // 正文页 - TMP 连续分片分页（受 WikiUIPageOverflowGuard 守卫，保持边界不变式）
        // ============================================================================

        private void SetupContentWithTMPPaging(string content)
        {
            currentParsedContent = string.IsNullOrEmpty(content)
                ? L10n.T("这一篇还没写内容。", "This page has no content yet.")
                : WikiContentManager.Instance.ParseMarkdown(content);

            ApplyTextStyle(txtLeft);
            ApplyTextStyle(txtRight);
            ApplyColumnBox(txtLeft, true, null);
            ApplyColumnBox(txtRight, false, null);
            SyncRightTextPropertiesFromLeft();

            if (txtLeft != null && txtRight != null)
            {
                var lr = txtLeft.rectTransform;
                var rr = txtRight.rectTransform;
                rr.anchorMin = lr.anchorMin;
                rr.anchorMax = lr.anchorMax;
                rr.pivot = lr.pivot;
                // 只同步尺寸：两栏同挂 pagesGroup、中心锚点，offsetMin / offsetMax 里含着各自的列位置，抄过去右栏会跑到左栏上
                rr.sizeDelta = lr.sizeDelta;
            }

            Canvas.ForceUpdateCanvases();
            if (txtLeft != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(txtLeft.rectTransform);
            if (txtRight != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(txtRight.rectTransform);

            currentArticlePages.Clear();
            currentPageIllustrations.Clear();
            totalPages = 1;
            if (txtLeft != null)
            {
                bool builtIllustrated = !string.IsNullOrEmpty(currentEntryId)
                    && !string.IsNullOrEmpty(content)
                    && TryBuildIllustratedArticlePages(currentEntryId, content);
                if (!builtIllustrated)
                {
                    txtLeft.text = currentParsedContent;
                    txtLeft.overflowMode = TMPro.TextOverflowModes.Page;
                    txtLeft.pageToDisplay = 1;
                    txtLeft.ForceMeshUpdate();

                    BuildArticlePageSlices();
                }
            }

            if (currentArticlePages.Count == 0)
            {
                currentArticlePages.Add(currentParsedContent);
                currentPageIllustrations.Add(null);
            }
            while (currentPageIllustrations.Count < currentArticlePages.Count)
            {
                currentPageIllustrations.Add(null);
            }

            PrepareArticlePageRenderer(txtLeft);
            PrepareArticlePageRenderer(txtRight);
            NormalizeArticlePageSlices();
            totalPages = currentArticlePages.Count;

            currentPageIndex = 0;
            leftPageToDisplay = 1;
            rightPageToDisplay = 2;
        }

        /// <summary>
        /// 将 master TMP 的分页边界转换成连续源文本页块。每个源字符只属于一个页块，
        /// 富文本标签只在页块边界补齐，不参与决定下一页起点。
        /// </summary>
        private void BuildArticlePageSlices()
        {
            currentArticlePages.Clear();
            if (txtLeft == null || txtLeft.textInfo == null || string.IsNullOrEmpty(currentParsedContent)) return;

            var textInfo = txtLeft.textInfo;
            int pageCount = textInfo.pageCount;
            if (pageCount < 1 || textInfo.pageInfo == null || textInfo.characterInfo == null) return;

            int[] boundaries = new int[pageCount + 1];
            boundaries[0] = 0;
            boundaries[pageCount] = currentParsedContent.Length;

            for (int pageIndex = 1; pageIndex < pageCount; pageIndex++)
            {
                int sourceIndex = ResolvePageSourceStart(textInfo, pageIndex, boundaries[pageIndex - 1]);
                boundaries[pageIndex] = Math.Max(boundaries[pageIndex - 1], Math.Min(sourceIndex, currentParsedContent.Length));
            }

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                int sliceStart = boundaries[pageIndex];
                int sliceEnd = boundaries[pageIndex + 1];
                string slice = currentParsedContent.Substring(sliceStart, sliceEnd - sliceStart);
                currentArticlePages.Add(RepairPageRichText(slice, currentParsedContent, sliceStart, sliceEnd));
            }
        }

        private int ResolvePageSourceStart(TMP_TextInfo textInfo, int pageIndex, int fallback)
        {
            if (pageIndex < 0 || pageIndex >= textInfo.pageCount || pageIndex >= textInfo.pageInfo.Length) return fallback;

            int characterIndex = textInfo.pageInfo[pageIndex].firstCharacterIndex;
            if (characterIndex < 0 || characterIndex >= textInfo.characterCount) return fallback;

            int sourceIndex = textInfo.characterInfo[characterIndex].index;
            return sourceIndex >= 0 ? sourceIndex : fallback;
        }

        private static string RepairPageRichText(string slice, string fullSource, int sliceStart, int sliceEnd)
        {
            List<string> openAtStart = CollectOpenRichTextTags(fullSource, sliceStart);
            List<string> openAtEnd = CollectOpenRichTextTags(fullSource, sliceEnd);
            if (openAtStart.Count == 0 && openAtEnd.Count == 0) return slice;

            var result = new System.Text.StringBuilder(slice.Length + 64);
            for (int i = 0; i < openAtStart.Count; i++)
            {
                result.Append('<').Append(openAtStart[i]).Append('>');
            }

            result.Append(slice);

            for (int i = openAtEnd.Count - 1; i >= 0; i--)
            {
                result.Append("</").Append(GetRichTextTagName(openAtEnd[i])).Append('>');
            }
            return result.ToString();
        }

        private static List<string> CollectOpenRichTextTags(string source, int end)
        {
            var stack = new List<string>();
            if (string.IsNullOrEmpty(source) || end <= 0) return stack;
            if (end > source.Length) end = source.Length;

            int cursor = 0;
            while (cursor < end)
            {
                int open = source.IndexOf('<', cursor);
                if (open < 0 || open >= end) break;

                int nameStart = open + 1;
                if (nameStart < end && source[nameStart] == '/') nameStart++;
                if (nameStart >= end || !IsAsciiLetter(source[nameStart]))
                {
                    cursor = open + 1;
                    continue;
                }

                int close = source.IndexOf('>', open + 1);
                if (close < 0 || close >= end) break;

                string tag = source.Substring(open + 1, close - open - 1).Trim();
                bool isClosing = tag.StartsWith("/", StringComparison.Ordinal);
                string tagBody = isClosing ? tag.Substring(1).Trim() : tag;
                string tagName = GetRichTextTagName(tagBody);

                if (IsTrackedRichTextTag(tagName))
                {
                    if (isClosing)
                    {
                        for (int i = stack.Count - 1; i >= 0; i--)
                        {
                            if (string.Equals(GetRichTextTagName(stack[i]), tagName, StringComparison.OrdinalIgnoreCase))
                            {
                                stack.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    else if (!IsStandaloneRichTextTag(tagName, tagBody))
                    {
                        stack.Add(tagBody);
                    }
                }

                cursor = close + 1;
            }
            return stack;
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsTrackedRichTextTag(string tagName)
        {
            return string.Equals(tagName, "b", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "color", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "link", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "size", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "u", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRichTextTagName(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            int length = 0;
            while (length < tag.Length && tag[length] != '=' && !char.IsWhiteSpace(tag[length]) && tag[length] != '/')
            {
                length++;
            }
            return length > 0 ? tag.Substring(0, length) : "";
        }

        private static bool IsStandaloneRichTextTag(string tagName, string tagBody)
        {
            return tagBody.EndsWith("/", StringComparison.Ordinal)
                || string.Equals(tagName, "br", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "sprite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "space", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "page", StringComparison.OrdinalIgnoreCase);
        }

        private void PrepareArticlePageRenderer(TMP_Text text)
        {
            if (text == null) return;
            text.text = "";
            text.overflowMode = TMPro.TextOverflowModes.Page;
            text.pageToDisplay = 1;
            EnsureLinkHandler(text);
        }

        private void NormalizeArticlePageSlices()
        {
            int pageIndex = 0;
            int splitCount = 0;
            while (pageIndex < currentArticlePages.Count)
            {
                bool isLeft = pageIndex % 2 == 0 || txtRight == null;
                TMP_Text renderer = isLeft ? txtLeft : txtRight;
                WikiPageVisualSlot slot = pageIndex < currentPageIllustrations.Count
                    ? currentPageIllustrations[pageIndex]
                    : null;
                ApplyColumnBox(renderer, isLeft, slot);
                if (renderer != null)
                {
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(renderer.rectTransform);
                }

                string firstPage;
                string remainder;
                if (string.IsNullOrEmpty(currentArticlePages[pageIndex])
                    || !TrySplitOverflowingPage(renderer, currentArticlePages[pageIndex], out firstPage, out remainder))
                {
                    pageIndex++;
                    continue;
                }

                currentArticlePages[pageIndex] = firstPage;
                currentArticlePages.Insert(pageIndex + 1, remainder);
                if (pageIndex < currentPageIllustrations.Count)
                {
                    currentPageIllustrations.Insert(pageIndex + 1, null);
                }
                splitCount++;
            }

            if (splitCount > 0)
            {
                ModBehaviour.DevLog("[WikiUI] 页块二次校正完成: 追加 " + splitCount + " 个物理页");
            }
        }

        private bool TrySplitOverflowingPage(TMP_Text renderer, string pageText, out string firstPage, out string remainder)
        {
            firstPage = pageText;
            remainder = "";
            if (renderer == null || string.IsNullOrEmpty(pageText)) return false;

            renderer.text = pageText;
            renderer.overflowMode = TMPro.TextOverflowModes.Page;
            renderer.pageToDisplay = 1;
            renderer.ForceMeshUpdate(true, true);

            TMP_TextInfo textInfo = renderer.textInfo;
            if (textInfo == null || textInfo.pageCount <= 1) return false;

            int splitIndex = ResolvePageSourceStart(textInfo, 1, -1);
            if (splitIndex <= 0 || splitIndex >= pageText.Length)
            {
                splitIndex = ResolvePageSourceEnd(textInfo, 0, -1);
            }
            if (splitIndex <= 0 || splitIndex >= pageText.Length)
            {
                ModBehaviour.DevLog("[WikiUI] [WARNING] 无法解析溢出页边界，保留原页块");
                return false;
            }

            string firstSlice = pageText.Substring(0, splitIndex);
            string remainingSlice = pageText.Substring(splitIndex);
            firstPage = RepairPageRichText(firstSlice, pageText, 0, splitIndex);
            remainder = RepairPageRichText(remainingSlice, pageText, splitIndex, pageText.Length);
            return !string.IsNullOrEmpty(firstPage) && !string.IsNullOrEmpty(remainder);
        }

        private int ResolvePageSourceEnd(TMP_TextInfo textInfo, int pageIndex, int fallback)
        {
            if (pageIndex < 0 || pageIndex >= textInfo.pageCount || pageIndex >= textInfo.pageInfo.Length) return fallback;

            int characterIndex = textInfo.pageInfo[pageIndex].lastCharacterIndex;
            if (characterIndex < 0 || characterIndex >= textInfo.characterCount) return fallback;

            TMP_CharacterInfo character = textInfo.characterInfo[characterIndex];
            return character.index + Math.Max(1, character.stringLength);
        }

        private void SyncRightTextPropertiesFromLeft()
        {
            if (txtLeft == null || txtRight == null) return;

            txtRight.font = txtLeft.font;
            txtRight.fontStyle = txtLeft.fontStyle;
            txtRight.fontSize = txtLeft.fontSize;
            txtRight.fontSizeMin = txtLeft.fontSizeMin;
            txtRight.fontSizeMax = txtLeft.fontSizeMax;
            txtRight.enableAutoSizing = txtLeft.enableAutoSizing;
            txtRight.characterSpacing = txtLeft.characterSpacing;
            txtRight.wordSpacing = txtLeft.wordSpacing;
            txtRight.lineSpacing = txtLeft.lineSpacing;
            txtRight.paragraphSpacing = txtLeft.paragraphSpacing;
            txtRight.alignment = txtLeft.alignment;
            txtRight.enableWordWrapping = txtLeft.enableWordWrapping;
            txtRight.richText = txtLeft.richText;
            txtRight.margin = txtLeft.margin;
        }

        private void ApplyTextStyle(TMP_Text txt)
        {
            if (txt == null) return;
            txt.fontSize = 16f;
            txt.color = NoticeInk;
            txt.alignment = TextAlignmentOptions.TopLeft;
            txt.enableWordWrapping = true;
            txt.lineSpacing = 5f;
            txt.paragraphSpacing = 10f;
            txt.margin = new Vector4(8f, 6f, 8f, 6f);
        }

        private void RefreshArticleContent()
        {
            leftPageToDisplay = currentPageIndex * 2 + 1;
            rightPageToDisplay = currentPageIndex * 2 + 2;

            RenderArticlePage(txtLeft, leftPageToDisplay);
            RenderArticlePage(txtRight, rightPageToDisplay);
            RenderPageIllustrations(leftPageToDisplay, rightPageToDisplay);

            if (txtPageNumber != null)
            {
                txtPageNumber.text = L10n.T(
                    "第 " + (currentPageIndex + 1) + " / " + BookTotalPages + " 页",
                    (currentPageIndex + 1) + " / " + BookTotalPages);
            }

            if (btnPrevPage != null) btnPrevPage.gameObject.SetActive(currentPageIndex > 0);
            if (btnNextPage != null) btnNextPage.gameObject.SetActive(currentPageIndex < BookTotalPages - 1);
        }

        private void RenderArticlePage(TMP_Text text, int pageNumber)
        {
            if (text == null) return;

            bool hasPage = pageNumber >= 1 && pageNumber <= currentArticlePages.Count;
            if (!hasPage)
            {
                text.gameObject.SetActive(false);
                text.text = "";
                return;
            }

            if (string.IsNullOrEmpty(currentArticlePages[pageNumber - 1]))
            {
                text.gameObject.SetActive(false);
                text.text = "";
                return;
            }

            WikiPageVisualSlot slot = pageNumber - 1 < currentPageIllustrations.Count
                ? currentPageIllustrations[pageNumber - 1]
                : null;
            ApplyColumnBox(text, text == txtLeft, slot);
            text.gameObject.SetActive(true);
            text.text = currentArticlePages[pageNumber - 1];
            text.overflowMode = TMPro.TextOverflowModes.Page;
            text.pageToDisplay = 1;
            text.ForceMeshUpdate();
        }

        private void EnsureLinkHandler(TMP_Text tmpText)
        {
            if (tmpText == null) return;
            if (tmpText.GetComponent<TMPLinkHandler>() == null)
            {
                tmpText.gameObject.AddComponent<TMPLinkHandler>();
                tmpText.raycastTarget = true;
            }
        }

        private void NextPage()
        {
            if (!IsUIOpen || closing) return;
            CloseLightbox();
            if (currentPageIndex < BookTotalPages - 1)
            {
                currentPageIndex++;
                RefreshArticleContent();
                PlayPageTurnInkRefresh();
            }
        }

        private void PrevPage()
        {
            if (!IsUIOpen || closing) return;
            CloseLightbox();
            if (currentPageIndex > 0)
            {
                currentPageIndex--;
                RefreshArticleContent();
                PlayPageTurnInkRefresh();
            }
        }

        // ============================================================================
        // 输入与取消键生命周期
        // ============================================================================

        private void ReleaseInput()
        {
            try
            {
                if (cancelKey != null)
                {
                    cancelKey.Detach();
                    cancelKey = null;
                }
                if (modalLease != null)
                {
                    modalLease.Release();
                    modalLease = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 归还输入失败: " + e.Message);
            }
        }

        private static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }

    /// <summary>
    /// TMP 链接点击处理组件
    /// </summary>
    public class TMPLinkHandler : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text tmpText;

        void Awake()
        {
            tmpText = GetComponent<TMP_Text>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (tmpText == null) return;

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(tmpText, eventData.position, null);
            if (linkIndex != -1)
            {
                string url = tmpText.textInfo.linkInfo[linkIndex].GetLinkID();
                if (!string.IsNullOrEmpty(url))
                {
                    ModBehaviour.DevLog("[TMPLinkHandler] 点击链接: " + url);
                    Application.OpenURL(url);
                }
            }
        }
    }
}

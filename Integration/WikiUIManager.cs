// ============================================================================
// WikiUIManager.cs - Wiki UI 管理器
// ============================================================================
// 模块说明：
//   管理 Wiki Book UI 的生命周期、用户交互和内容显示，包括：
//   - UI 初始化和节点缓存
//   - 打开/关闭 UI
//   - 目录页和正文页切换
//   - 翻页功能
//   - ESC 键处理
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// Wiki UI 管理器（单例）
    /// </summary>
    public class WikiUIManager
    {
        // ============================================================================
        // 单例
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

        // ============================================================================
        // UI 状态
        // ============================================================================

        public bool IsUIOpen { get; private set; }
        private bool isOnArticlePage = false;
        private int currentPageIndex = 0;
        private int totalPages = 1;
        private string currentCategoryId = null;
        private string currentEntryId = null;

        /// <summary>书本总翻页数（每次翻页显示左右两栏）</summary>
        private int BookTotalPages => (totalPages + 1) / 2;

        // ============================================================================
        // UI 节点缓存
        // ============================================================================

        private GameObject uiPrefab = null;
        private GameObject uiRoot = null;

        // 目录页
        private Transform pageIndex = null;       // Page_Index_Left
        private Transform _pageIndexRight = null;  // Page_Index_Right
        private Transform categoryContainer = null;
        private Transform entryContainer = null;
        private GameObject categoryTemplate = null;
        private GameObject entryTemplate = null;
        private TMP_Text txtEmptyHint = null;
        private TMP_Text lblCategories = null;
        private TMP_Text lblEntries = null;

        // 正文页
        private Transform pageArticle = null;      // Page_Article_Left
        private Transform _pageArticleRight = null; // Page_Article_Right
        private TMP_Text txtArticleTitle = null;
        private TMP_Text txtLeft = null;
        private TMP_Text txtRight = null;
        private TMP_Text txtPageNumber = null;
        private Button btnPrevPage = null;
        private Button btnNextPage = null;
        private Button btnGoBack = null;
        private Button btnClose = null;

        // ============================================================================
        // 分页状态
        // ============================================================================

        private string currentParsedContent = "";
        private int leftPageToDisplay = 1;
        private int rightPageToDisplay = 2;

        // ============================================================================
        // 公共方法
        // ============================================================================

        public void SetUIPrefab(GameObject prefab)
        {
            uiPrefab = prefab;
            ModBehaviour.DevLog("[WikiUI] Prefab 已设置");
        }

        public void OpenUI()
        {
            if (IsUIOpen) return;

            try
            {
                if (uiRoot == null && !Initialize())
                {
                    ModBehaviour.DevLog("[WikiUI] 初始化失败");
                    return;
                }

                DisablePlayerInput();
                uiRoot.SetActive(true);
                IsUIOpen = true;
                ShowIndexPage();
                SubscribeEscapeKey();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 打开失败: " + e.Message);
            }
        }

        public void CloseUI()
        {
            if (!IsUIOpen) return;

            try
            {
                if (uiRoot != null) uiRoot.SetActive(false);
                IsUIOpen = false;
                EnablePlayerInput();
                UnsubscribeEscapeKey();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 关闭失败: " + e.Message);
            }
        }

        // ============================================================================
        // 初始化
        // ============================================================================

        private bool Initialize()
        {
            if (uiPrefab == null)
            {
                ModBehaviour.DevLog("[WikiUI] Prefab 未设置");
                return false;
            }

            try
            {
                uiRoot = UnityEngine.Object.Instantiate(uiPrefab);
                uiRoot.name = "WikiBookUI";
                UnityEngine.Object.DontDestroyOnLoad(uiRoot);
                uiRoot.SetActive(false);

                if (!CacheUINodes()) return false;

                BindButtonEvents();
                ReplaceAllFonts();
                WikiContentManager.Instance.LoadCatalog();

                ModBehaviour.DevLog("[WikiUI] 初始化完成");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 初始化异常: " + e.Message);
                return false;
            }
        }

        // ============================================================================
        // 节点查找辅助
        // ============================================================================

        /// <summary>递归查找子节点</summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 在 parent 下按路径查找组件，找不到则按名称递归查找。
        /// path 可以是 "A/B/C" 形式，fallbackName 默认取 path 最后一段。
        /// </summary>
        private T FindComponent<T>(Transform parent, string path, string fallbackName = null) where T : Component
        {
            if (parent == null) return null;

            // 先尝试精确路径
            Transform node = parent.Find(path);

            // 回退到递归查找
            if (node == null)
            {
                string name = fallbackName ?? path.Substring(path.LastIndexOf('/') + 1);
                node = FindChildRecursive(parent, name);
            }

            return node != null ? node.GetComponent<T>() : null;
        }

        /// <summary>按名称递归查找 Transform（不取组件）</summary>
        private Transform FindNode(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform node = parent.Find(name);
            return node ?? FindChildRecursive(parent, name);
        }

        // ============================================================================
        // 节点缓存
        // ============================================================================

        private bool CacheUINodes()
        {
            try
            {
                Transform root = uiRoot.transform;

                // 四个页面面板
                Transform pageIndexLeft = FindChildRecursive(root, "Page_Index_Left");
                Transform pageIndexRight = FindChildRecursive(root, "Page_Index_Right");
                Transform pageArticleLeft = FindChildRecursive(root, "Page_Article_Left");
                Transform pageArticleRight = FindChildRecursive(root, "Page_Article_Right");

                pageIndex = pageIndexLeft;
                pageArticle = pageArticleLeft;
                _pageIndexRight = pageIndexRight;
                _pageArticleRight = pageArticleRight;

                // 目录页 - 分类容器（左侧）
                if (pageIndexLeft != null)
                {
                    Transform leftCat = pageIndexLeft.Find("Left_Categories");
                    categoryContainer = leftCat != null
                        ? FindNode(leftCat, "Scroll_Categories")?.Find("Viewport/Content")
                        : null;
                    if (categoryContainer == null)
                        categoryContainer = FindChildRecursive(pageIndexLeft, "Content");

                    lblCategories = FindComponent<TMP_Text>(leftCat ?? pageIndexLeft, "Lbl_Categories");
                    // 书卷风 UI：隐藏"分类"列标题，左栏图标和列表本身已足够清晰，避免冗余文字占用空间
                    if (lblCategories != null) lblCategories.gameObject.SetActive(false);
                }

                // 目录页 - 条目容器（右侧）
                if (pageIndexRight != null)
                {
                    Transform rightEnt = pageIndexRight.Find("Right_Entries");
                    if (rightEnt != null)
                    {
                        Transform scroll = rightEnt.Find("Scroll_Entries");
                        entryContainer = scroll != null ? scroll.Find("Viewport/Content") : null;
                        txtEmptyHint = FindComponent<TMP_Text>(rightEnt, "Txt_EmptyHint");
                    }
                    if (entryContainer == null)
                        entryContainer = FindChildRecursive(pageIndexRight, "Content");

                    lblEntries = FindComponent<TMP_Text>(rightEnt ?? pageIndexRight, "Lbl_Entries");
                    // 书卷风 UI：隐藏"条目"列标题，右栏列表本身已足够清晰，避免冗余文字占用空间
                    if (lblEntries != null) lblEntries.gameObject.SetActive(false);
                }

                // 模板
                categoryTemplate = FindChildRecursive(root, "Tgl_CategoryTemplate")?.gameObject;
                entryTemplate = FindChildRecursive(root, "Tgl_EntryTemplate")?.gameObject;

                // TopBar
                Transform topBar = FindChildRecursive(root, "TopBar");
                txtArticleTitle = FindComponent<TMP_Text>(topBar, "Txt_Title");
                btnGoBack = FindComponent<Button>(topBar, "Btn_Back");

                // TopBar 标题：禁用换行 + 自动缩字 + Ellipsis 溢出，避免纯中文长标题被拆成多行
                if (txtArticleTitle != null)
                {
                    txtArticleTitle.enableWordWrapping = false;
                    txtArticleTitle.overflowMode = TextOverflowModes.Ellipsis;
                    float maxSize = txtArticleTitle.fontSize > 0 ? txtArticleTitle.fontSize : 28f;
                    txtArticleTitle.enableAutoSizing = true;
                    txtArticleTitle.fontSizeMin = 14f;
                    txtArticleTitle.fontSizeMax = maxSize;
                }

                // 正文页文本
                txtLeft = FindComponent<TMP_Text>(pageArticleLeft, "Txt_Left");
                txtRight = FindComponent<TMP_Text>(pageArticleRight, "Txt_Right");

                // BottomBar
                Transform bottomBar = FindChildRecursive(root, "BottomBar");
                txtPageNumber = FindComponent<TMP_Text>(bottomBar, "Txt_PageNumber");
                btnPrevPage = FindComponent<Button>(bottomBar, "Btn_PrevPage");
                btnNextPage = FindComponent<Button>(bottomBar, "Btn_NextPage");

                // 关闭按钮
                btnClose = FindComponent<Button>(root, "Btn_Close");

                // 汇总日志
                int found = 0, missing = 0;
                foreach (object node in new object[] {
                    pageIndexLeft, pageIndexRight, pageArticleLeft, pageArticleRight,
                    categoryContainer, entryContainer, categoryTemplate, entryTemplate,
                    txtArticleTitle, txtLeft, txtRight, txtPageNumber,
                    btnPrevPage, btnNextPage, btnGoBack, btnClose })
                {
                    if (node != null) found++; else missing++;
                }
                ModBehaviour.DevLog("[WikiUI] 节点缓存完成: " + found + " 个成功, " + missing + " 个缺失");

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 缓存节点异常: " + e.Message);
                return false;
            }
        }

        private void BindButtonEvents()
        {
            if (btnClose != null) btnClose.onClick.AddListener(CloseUI);
            if (btnPrevPage != null) btnPrevPage.onClick.AddListener(PrevPage);
            if (btnNextPage != null) btnNextPage.onClick.AddListener(NextPage);
            if (btnGoBack != null) btnGoBack.onClick.AddListener(GoBack);
        }

        // ============================================================================
        // 字体替换
        // ============================================================================

        private void ReplaceAllFonts()
        {
            try
            {
                TMP_FontAsset gameFont = ZombieModeUIHelper.GetGameFont();
                if (gameFont == null) return;

                var tmpComponents = uiRoot.GetComponentsInChildren<TMP_Text>(true);
                foreach (var tmp in tmpComponents)
                {
                    tmp.font = gameFont;
                }
                ModBehaviour.DevLog("[WikiUI] 已替换 " + tmpComponents.Length + " 个 TMP 字体");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUI] 字体替换失败: " + e.Message);
            }
        }

        // ============================================================================
        // 页面切换
        // ============================================================================

        /// <summary>统一设置目录页/正文页的可见性及相关按钮</summary>
        private void SetPageVisibility(bool showIndex, bool showArticle)
        {
            if (pageIndex != null) pageIndex.gameObject.SetActive(showIndex);
            if (_pageIndexRight != null) _pageIndexRight.gameObject.SetActive(showIndex);
            if (pageArticle != null) pageArticle.gameObject.SetActive(showArticle);
            if (_pageArticleRight != null) _pageArticleRight.gameObject.SetActive(showArticle);

            // 翻页控件只在正文页显示
            if (btnPrevPage != null) btnPrevPage.gameObject.SetActive(showArticle);
            if (btnNextPage != null) btnNextPage.gameObject.SetActive(showArticle);
            if (txtPageNumber != null) txtPageNumber.gameObject.SetActive(showArticle);
            if (btnGoBack != null) btnGoBack.gameObject.SetActive(showArticle);
        }

        private void ShowIndexPage()
        {
            isOnArticlePage = false;
            SetPageVisibility(showIndex: true, showArticle: false);

            if (txtArticleTitle != null) txtArticleTitle.text = "Boss Rush";

            RefreshCategoryList();
            ClearAllEntryToggles();

            // 恢复之前选中的分类，或默认选中第一个
            var categories = WikiContentManager.Instance.GetCategories();
            if (categories != null && categories.Count > 0)
            {
                string targetId = !string.IsNullOrEmpty(currentCategoryId)
                    ? currentCategoryId
                    : categories[0].Id;
                RefreshEntryList(targetId);
            }
        }

        private void ShowArticlePage(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return;

            currentEntryId = entryId;
            isOnArticlePage = true;
            SetPageVisibility(showIndex: false, showArticle: true);

            // 加载内容
            var entry = WikiContentManager.Instance.GetEntry(entryId);
            string content = WikiContentManager.Instance.LoadEntryContent(entryId);

            if (txtArticleTitle != null && entry != null)
                txtArticleTitle.text = entry.GetTitle();

            SetupContentWithTMPPaging(content);
            currentPageIndex = 0;
            RefreshArticleContent();
        }

        private void GoBack()
        {
            ShowIndexPage();
        }

        // ============================================================================
        // 目录页 - 列表管理
        // ============================================================================

        /// <summary>
        /// 从模板创建列表项，设置文本，返回 GameObject（未激活状态）。
        /// 调用方负责绑定 Toggle/Button 事件后再激活。
        /// </summary>
        private GameObject CreateListItem(GameObject template, Transform container, string itemName, string displayText)
        {
            var item = UnityEngine.Object.Instantiate(template, container);
            item.name = itemName;

            Transform txtNode = FindChildRecursive(item.transform, "Txt_Name");
            TMP_Text text = txtNode != null ? txtNode.GetComponent<TMP_Text>() : item.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = displayText;
                // 单行显示 + 自动缩字 + 溢出省略号，避免长标题换行显示丑
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                // 保留 Prefab 字号作为最大值，允许缩到 10（满足绝大部分中文长标题）
                float maxSize = text.fontSize > 0 ? text.fontSize : 16f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = maxSize;
            }

            return item;
        }

        /// <summary>配置 Toggle：移除 group，清除监听器，设置初始选中状态</summary>
        private Toggle SetupToggle(GameObject item, bool isOn)
        {
            var toggle = item.GetComponent<Toggle>();
            if (toggle == null) return null;

            toggle.group = null;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(isOn);
            return toggle;
        }

        private void ClearContainer(Transform container, GameObject template)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (child.gameObject != template)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private void ClearAllEntryToggles()
        {
            if (entryContainer == null) return;
            foreach (Transform child in entryContainer)
            {
                if (child.gameObject == entryTemplate) continue;
                var toggle = child.GetComponent<Toggle>();
                if (toggle != null && toggle.isOn) toggle.SetIsOnWithoutNotify(false);
            }
        }

        private void ClearOtherToggles(Transform container, GameObject template, GameObject currentItem)
        {
            if (container == null) return;
            foreach (Transform child in container)
            {
                if (child.gameObject == template || child.gameObject == currentItem) continue;
                var toggle = child.GetComponent<Toggle>();
                if (toggle != null && toggle.isOn) toggle.SetIsOnWithoutNotify(false);
            }
        }

        // ============================================================================
        // 目录页 - 分类列表
        // ============================================================================

        private void RefreshCategoryList()
        {
            if (categoryContainer == null || categoryTemplate == null) return;

            ClearContainer(categoryContainer, categoryTemplate);

            var categories = WikiContentManager.Instance.GetCategories();
            if (categories == null || categories.Count == 0) return;

            bool isFirst = true;
            foreach (var category in categories)
            {
                var item = CreateListItem(categoryTemplate, categoryContainer,
                    "Category_" + category.Id, category.GetTitle());

                // 确定初始选中状态
                bool shouldSelect = !string.IsNullOrEmpty(currentCategoryId)
                    ? (category.Id == currentCategoryId)
                    : isFirst;

                var toggle = SetupToggle(item, shouldSelect);
                if (toggle != null)
                {
                    string categoryId = category.Id;
                    toggle.onValueChanged.AddListener((isOn) =>
                    {
                        if (isOn)
                        {
                            ClearOtherToggles(categoryContainer, categoryTemplate, item);
                            SelectCategory(categoryId);
                        }
                    });
                }
                else
                {
                    var button = item.GetComponent<Button>();
                    if (button != null)
                    {
                        string categoryId = category.Id;
                        button.onClick.AddListener(() => SelectCategory(categoryId));
                    }
                }

                item.SetActive(true);
                isFirst = false;
            }
        }

        private void SelectCategory(string categoryId)
        {
            if (categoryId == "_wiki_link")
            {
                Application.OpenURL("https://bossrushmod.pages.dev/");
                return;
            }

            if (currentCategoryId == categoryId) return;

            currentCategoryId = categoryId;
            RefreshEntryList(categoryId);
        }

        // ============================================================================
        // 目录页 - 条目列表
        // ============================================================================

        private void RefreshEntryList(string categoryId)
        {
            if (entryContainer == null || entryTemplate == null) return;

            ClearContainer(entryContainer, entryTemplate);

            var entries = WikiContentManager.Instance.GetEntries(categoryId);

            if (txtEmptyHint != null)
                txtEmptyHint.gameObject.SetActive(entries == null || entries.Count == 0);

            if (entries == null || entries.Count == 0) return;

            foreach (var entry in entries)
            {
                var item = CreateListItem(entryTemplate, entryContainer,
                    "Entry_" + entry.Id, entry.GetTitle());

                var toggle = SetupToggle(item, false);
                if (toggle != null)
                {
                    string entryId = entry.Id;
                    toggle.onValueChanged.AddListener((isOn) =>
                    {
                        if (isOn && !isOnArticlePage)
                        {
                            ClearOtherToggles(entryContainer, entryTemplate, item);
                            ShowArticlePage(entryId);
                        }
                    });
                }
                else
                {
                    var button = item.GetComponent<Button>();
                    if (button != null)
                    {
                        string entryId = entry.Id;
                        button.onClick.AddListener(() => ShowArticlePage(entryId));
                    }
                }

                item.SetActive(true);
            }
        }

        // ============================================================================
        // 正文页 - 内容与分页
        // ============================================================================

        private void SetupContentWithTMPPaging(string content)
        {
            currentParsedContent = string.IsNullOrEmpty(content)
                ? "[内容为空]"
                : WikiContentManager.Instance.ParseMarkdown(content);

            // 统一排版属性
            ApplyTextStyle(txtLeft);
            ApplyTextStyle(txtRight);

            // 字体参数必须完全一致，否则两栏的 characterInfo 索引会错位
            SyncRightTextPropertiesFromLeft();

            // 同步右栏 RectTransform 布局，确保渲染区域完全一致
            if (txtLeft != null && txtRight != null)
            {
                var lr = txtLeft.rectTransform;
                var rr = txtRight.rectTransform;
                rr.anchorMin = lr.anchorMin;
                rr.anchorMax = lr.anchorMax;
                rr.pivot = lr.pivot;
                rr.sizeDelta = lr.sizeDelta;
                rr.offsetMin = lr.offsetMin;
                rr.offsetMax = lr.offsetMax;
            }

            // 强制 layout 立即生效，保证 ForceMeshUpdate 时 rect 已是最终尺寸
            Canvas.ForceUpdateCanvases();
            if (txtLeft != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(txtLeft.rectTransform);
            if (txtRight != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(txtRight.rectTransform);

            // 左栏：权威分页源（master），用 Page 模式产出 pageInfo
            totalPages = 1;
            if (txtLeft != null)
            {
                txtLeft.text = currentParsedContent;
                txtLeft.overflowMode = TMPro.TextOverflowModes.Page;
                txtLeft.pageToDisplay = 1;
                txtLeft.ForceMeshUpdate();

                totalPages = txtLeft.textInfo.pageCount;
                if (totalPages < 1) totalPages = 1;

                EnsureLinkHandler(txtLeft);
            }

            // 右栏：用 Overflow 模式，内容由 RefreshArticleContent 时从左栏 pageInfo 切片赋值。
            // 不用 firstVisibleCharacter：它不改变 TMP 布局位置，会导致字符渲染在 rect 外空白。
            if (txtRight != null)
            {
                txtRight.overflowMode = TMPro.TextOverflowModes.Overflow;
                txtRight.text = "";
                EnsureLinkHandler(txtRight);
            }

            currentPageIndex = 0;
            leftPageToDisplay = 1;
            rightPageToDisplay = 2;
        }

        /// <summary>
        /// 从左栏（master）的 pageInfo 中取第 pageNumber 页对应的源字符串切片。
        /// 用于右栏直接 txtRight.text = 切片，保证与左栏分页完全对齐且从 rect 左上角开始布局。
        /// </summary>
        private string ExtractPageSourceText(int pageNumber)
        {
            if (string.IsNullOrEmpty(currentParsedContent)) return "";
            if (txtLeft == null || txtLeft.textInfo == null) return "";
            if (pageNumber < 1 || pageNumber > totalPages) return "";

            var textInfo = txtLeft.textInfo;
            if (textInfo.pageInfo == null || pageNumber - 1 >= textInfo.pageInfo.Length) return "";
            if (textInfo.characterInfo == null) return "";

            int charInfoLen = textInfo.characterCount;
            if (charInfoLen <= 0) return "";

            var pageInfo = textInfo.pageInfo[pageNumber - 1];
            int firstCharIdx = pageInfo.firstCharacterIndex;
            int lastCharIdx = pageInfo.lastCharacterIndex;

            if (firstCharIdx < 0) firstCharIdx = 0;
            if (firstCharIdx >= charInfoLen) return "";
            if (lastCharIdx < firstCharIdx) lastCharIdx = firstCharIdx;
            if (lastCharIdx >= charInfoLen) lastCharIdx = charInfoLen - 1;

            var firstChar = textInfo.characterInfo[firstCharIdx];
            var lastChar = textInfo.characterInfo[lastCharIdx];

            int srcStart = firstChar.index;
            int srcEndExclusive = lastChar.index + Math.Max(1, lastChar.stringLength);

            if (srcStart < 0) srcStart = 0;
            if (srcStart >= currentParsedContent.Length) return "";
            if (srcEndExclusive > currentParsedContent.Length) srcEndExclusive = currentParsedContent.Length;
            if (srcEndExclusive <= srcStart) return "";

            string slice = currentParsedContent.Substring(srcStart, srcEndExclusive - srcStart);
            return RepairRichTextTags(slice, currentParsedContent, srcStart, srcEndExclusive);
        }

        /// <summary>
        /// 补全切片内 TMP rich text 标签的开闭配对。
        /// 1) 切片前（source[0..sliceStart]）所有已"打开"但未在切片前关闭的标签 → 补到切片开头。
        /// 2) 切片尾部未关闭的标签 → 补闭合标签到切片结尾。
        /// 只处理常见块级标签：b,i,u,s,color,size,mark,sub,sup,align,indent,link,style,voffset,cspace,line-height,mspace,pos,space,sprite,font。
        /// </summary>
        private static string RepairRichTextTags(string slice, string fullSource, int sliceStart, int sliceEndExclusive)
        {
            if (string.IsNullOrEmpty(slice)) return slice;

            // 第一步：分析 fullSource[0..sliceStart] 中打开但未关闭的标签
            List<string> openTags = CollectOpenTags(fullSource, 0, sliceStart);
            if (openTags.Count == 0) return slice;

            // 第二步：分析切片自身是否已关闭这些标签
            List<string> stillOpen = CollectOpenTags(fullSource, sliceStart, sliceEndExclusive);

            // 拼接：在切片前补开标签，在切片末补闭标签（逆序闭合）
            var sb = new System.Text.StringBuilder(slice.Length + 64);
            for (int i = 0; i < openTags.Count; i++)
            {
                sb.Append("<").Append(openTags[i]).Append(">");
            }
            sb.Append(slice);
            for (int i = stillOpen.Count - 1; i >= 0; i--)
            {
                string tag = stillOpen[i];
                int eq = tag.IndexOf('=');
                string tagName = eq >= 0 ? tag.Substring(0, eq) : tag;
                sb.Append("</").Append(tagName).Append(">");
            }
            return sb.ToString();
        }

        /// <summary>扫描 source[start..end] 中未闭合的 rich text 标签（栈方式），返回由旧到新的标签原文（不含尖括号）。</summary>
        private static List<string> CollectOpenTags(string source, int start, int end)
        {
            var stack = new List<string>();
            if (start < 0) start = 0;
            if (end > source.Length) end = source.Length;
            int i = start;
            while (i < end)
            {
                int lt = source.IndexOf('<', i);
                if (lt < 0 || lt >= end) break;
                int gt = source.IndexOf('>', lt + 1);
                if (gt < 0 || gt >= end) break;

                string inner = source.Substring(lt + 1, gt - lt - 1);
                if (inner.Length == 0) { i = gt + 1; continue; }

                bool isClosing = inner[0] == '/';
                string tagContent = isClosing ? inner.Substring(1) : inner;
                int eq = tagContent.IndexOf('=');
                string tagName = eq >= 0 ? tagContent.Substring(0, eq) : tagContent;
                tagName = tagName.Trim().ToLowerInvariant();

                // 过滤非 rich text 场景（无名 / 空白）
                if (tagName.Length > 0)
                {
                    if (isClosing)
                    {
                        // 从栈尾向前查找同名的开标签并移除
                        for (int k = stack.Count - 1; k >= 0; k--)
                        {
                            int ek = stack[k].IndexOf('=');
                            string sk = ek >= 0 ? stack[k].Substring(0, ek) : stack[k];
                            if (sk.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                            {
                                stack.RemoveAt(k);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // <br> 等自闭合标签不入栈
                        if (tagName != "br" && tagName != "sprite")
                        {
                            stack.Add(tagContent);
                        }
                    }
                }
                i = gt + 1;
            }
            return stack;
        }

        /// <summary>
        /// 将右栏的所有影响分页/布局的字体属性强制同步到左栏；
        /// characterInfo 数组仅依赖 text + 字体参数，这样右栏用左栏索引才能精确命中字符。
        /// </summary>
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
            txt.color = new Color(0.25f, 0.2f, 0.15f, 1f);
            txt.enableWordWrapping = true;
            txt.lineSpacing = 4f;
            txt.paragraphSpacing = 8f;
            txt.margin = new Vector4(10f, 8f, 10f, 8f);
        }

        private void RefreshArticleContent()
        {
            leftPageToDisplay = currentPageIndex * 2 + 1;
            rightPageToDisplay = currentPageIndex * 2 + 2;

            // 左栏：权威页，用 TMP 内置 pageToDisplay 显示
            if (txtLeft != null)
            {
                txtLeft.pageToDisplay = leftPageToDisplay;
                txtLeft.ForceMeshUpdate();
            }

            // 右栏：从左栏 pageInfo 切出源字符串范围直接赋值，保证从 rect 左上角开始布局显示。
            // （不使用 firstVisibleCharacter，因为它不改变布局位置，会导致右栏首字符渲染到 rect 外产生空白。）
            if (txtRight != null)
            {
                string rightText = ExtractPageSourceText(rightPageToDisplay);
                if (!string.IsNullOrEmpty(rightText))
                {
                    txtRight.gameObject.SetActive(true);
                    txtRight.text = rightText;
                    txtRight.ForceMeshUpdate();
                }
                else
                {
                    txtRight.text = "";
                    txtRight.gameObject.SetActive(false);
                }
            }

            if (txtPageNumber != null)
                txtPageNumber.text = (currentPageIndex + 1) + "/" + BookTotalPages;

            if (btnPrevPage != null) btnPrevPage.interactable = currentPageIndex > 0;
            if (btnNextPage != null) btnNextPage.interactable = currentPageIndex < BookTotalPages - 1;
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

        // ============================================================================
        // 翻页
        // ============================================================================

        private void NextPage()
        {
            if (!isOnArticlePage) return;
            if (currentPageIndex < BookTotalPages - 1)
            {
                currentPageIndex++;
                RefreshArticleContent();
            }
        }

        private void PrevPage()
        {
            if (!isOnArticlePage) return;
            if (currentPageIndex > 0)
            {
                currentPageIndex--;
                RefreshArticleContent();
            }
        }

        // ============================================================================
        // 输入控制
        // ============================================================================

        private void DisablePlayerInput()
        {
            try { if (uiRoot != null) InputManager.DisableInput(uiRoot); }
            catch (Exception e) { ModBehaviour.DevLog("[WikiUI] 禁用输入失败: " + e.Message); }
        }

        private void EnablePlayerInput()
        {
            try { if (uiRoot != null) InputManager.ActiveInput(uiRoot); }
            catch (Exception e) { ModBehaviour.DevLog("[WikiUI] 启用输入失败: " + e.Message); }
        }

        // ============================================================================
        // ESC 键处理
        // ============================================================================

        private void SubscribeEscapeKey()
        {
            try { UIInputManager.OnCancelEarly += OnEscapePressed; }
            catch (Exception e) { ModBehaviour.DevLog("[WikiUI] 订阅 ESC 失败: " + e.Message); }
        }

        private void UnsubscribeEscapeKey()
        {
            try { UIInputManager.OnCancelEarly -= OnEscapePressed; }
            catch (Exception e) { ModBehaviour.DevLog("[WikiUI] 取消订阅 ESC 失败: " + e.Message); }
        }

        private void OnEscapePressed(UIInputEventData data)
        {
            if (!IsUIOpen) return;

            data.Use();

            if (isOnArticlePage)
                GoBack();
            else
                CloseUI();
        }
    }

    /// <summary>
    /// TMP 链接点击处理组件
    /// </summary>
    public class TMPLinkHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        private TMP_Text tmpText;

        void Awake()
        {
            tmpText = GetComponent<TMP_Text>();
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
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

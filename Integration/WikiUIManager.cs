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
using System.Text;
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
        
        /// <summary>
        /// UI 是否已打开
        /// </summary>
        public bool IsUIOpen { get; private set; }
        
        /// <summary>
        /// 是否在正文页（false = 目录页）
        /// </summary>
        private bool isOnArticlePage = false;
        
        /// <summary>
        /// 当前页码（从0开始）
        /// </summary>
        private int currentPageIndex = 0;
        
        /// <summary>
        /// 总页数
        /// </summary>
        private int totalPages = 1;
        
        /// <summary>
        /// 当前选中的分类ID
        /// </summary>
        private string currentCategoryId = null;
        
        /// <summary>
        /// 当前显示的条目ID
        /// </summary>
        private string currentEntryId = null;
        
        /// <summary>
        /// 当前条目的分页内容（已废弃，改用TMP内置分页）
        /// </summary>
        // private List<string[]> pagedContent = new List<string[]>();
        
        // ============================================================================
        // UI 节点缓存
        // ============================================================================
        
        private GameObject uiPrefab = null;
        private GameObject uiRoot = null;
        
        // 目录页节点
        private Transform pageIndex = null;
        private Transform categoryContainer = null;
        private Transform entryContainer = null;
        private GameObject categoryTemplate = null;
        private GameObject entryTemplate = null;
        private TMP_Text txtEmptyHint = null;
        
        // 正文页节点
        private Transform pageArticle = null;
        private TMP_Text txtArticleTitle = null;
        private TMP_Text txtLeft = null;
        private TMP_Text txtRight = null;
        private TMP_Text txtPageNumber = null;
        private Button btnPrevPage = null;
        private Button btnNextPage = null;
        private Button btnGoBack = null;
        
        // 关闭按钮
        private Button btnClose = null;
        
        // 额外的页面引用（用于显示/隐藏）
        private Transform _pageIndexRight = null;
        private Transform _pageArticleRight = null;
        
        // 目录页标题标签
        private TMP_Text lblCategories = null;
        private TMP_Text lblEntries = null;
        
        // ============================================================================
        // 分页配置
        // ============================================================================
        
        /// <summary>
        /// 当前解析后的内容（用于TMP分页）
        /// </summary>
        private string currentParsedContent = "";
        
        /// <summary>
        /// 左栏当前显示的TMP页码（从1开始）
        /// </summary>
        private int leftPageToDisplay = 1;
        
        /// <summary>
        /// 右栏当前显示的TMP页码（从1开始）
        /// </summary>
        private int rightPageToDisplay = 2;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 设置 UI Prefab（由 WikiBookItem 调用）
        /// </summary>
        public void SetUIPrefab(GameObject prefab)
        {
            uiPrefab = prefab;
            ModBehaviour.DevLog("[WikiUIManager] UI Prefab 已设置");
        }
        
        /// <summary>
        /// 打开 Wiki UI
        /// </summary>
        public void OpenUI()
        {
            if (IsUIOpen)
            {
                ModBehaviour.DevLog("[WikiUIManager] UI 已经打开");
                return;
            }
            
            try
            {
                // 首次打开时初始化
                if (uiRoot == null)
                {
                    if (!Initialize())
                    {
                        ModBehaviour.DevLog("[WikiUIManager] 初始化失败，无法打开 UI");
                        return;
                    }
                }
                
                // 禁用玩家输入
                DisablePlayerInput();
                
                // 显示 UI
                uiRoot.SetActive(true);
                IsUIOpen = true;
                
                // 显示目录页
                ShowIndexPage();
                
                // 订阅 ESC 键事件
                SubscribeEscapeKey();
                
                ModBehaviour.DevLog("[WikiUIManager] UI 已打开");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 打开 UI 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 关闭 Wiki UI
        /// </summary>
        public void CloseUI()
        {
            if (!IsUIOpen)
            {
                return;
            }
            
            try
            {
                // 隐藏 UI
                if (uiRoot != null)
                {
                    uiRoot.SetActive(false);
                }
                
                IsUIOpen = false;
                
                // 恢复玩家输入
                EnablePlayerInput();
                
                // 取消订阅 ESC 键事件
                UnsubscribeEscapeKey();
                
                ModBehaviour.DevLog("[WikiUIManager] UI 已关闭");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 关闭 UI 失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 初始化
        // ============================================================================
        
        /// <summary>
        /// 初始化 UI
        /// </summary>
        private bool Initialize()
        {
            if (uiPrefab == null)
            {
                ModBehaviour.DevLog("[WikiUIManager] UI Prefab 未设置");
                return false;
            }
            
            try
            {
                // 实例化 UI
                uiRoot = UnityEngine.Object.Instantiate(uiPrefab);
                uiRoot.name = "WikiBookUI";
                UnityEngine.Object.DontDestroyOnLoad(uiRoot);
                uiRoot.SetActive(false);
                
                // 缓存 UI 节点
                if (!CacheUINodes())
                {
                    ModBehaviour.DevLog("[WikiUIManager] 缓存 UI 节点失败");
                    return false;
                }
                
                // 绑定按钮事件
                BindButtonEvents();
                
                // 替换字体
                ReplaceAllFonts();
                
                // 加载内容目录
                WikiContentManager.Instance.LoadCatalog();
                
                ModBehaviour.DevLog("[WikiUIManager] 初始化完成");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 初始化异常: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 缓存 UI 节点
        /// </summary>
        private bool CacheUINodes()
        {
            try
            {
                // 先输出完整的UI层级结构用于调试
                ModBehaviour.DevLog("[WikiUIManager] UI 层级结构:");
                PrintHierarchy(uiRoot.transform, 0);
                
                // 根据实际 UI 层级结构查找节点
                // 实际结构:
                // ContentRoot/Pages_Left/Page_Index_Left (目录页左侧)
                // ContentRoot/Pages_Left/Page_Article_Left (正文页左侧)
                // ContentRoot/Pages_Right/Page_Index_Right (目录页右侧)
                // ContentRoot/Pages_Right/Page_Article_Right (正文页右侧)
                
                Transform pagesLeft = FindChildRecursive(uiRoot.transform, "Pages_Left");
                Transform pagesRight = FindChildRecursive(uiRoot.transform, "Pages_Right");
                
                // 目录页节点（在 Pages_Left/Pages_Right 下）
                Transform pageIndexLeft = FindChildRecursive(uiRoot.transform, "Page_Index_Left");
                Transform pageIndexRight = FindChildRecursive(uiRoot.transform, "Page_Index_Right");
                
                // 正文页节点（在 Pages_Left/Pages_Right 下）
                Transform pageArticleLeft = FindChildRecursive(uiRoot.transform, "Page_Article_Left");
                Transform pageArticleRight = FindChildRecursive(uiRoot.transform, "Page_Article_Right");
                
                // 使用 pageIndexLeft 作为目录页的代表（用于显示/隐藏）
                pageIndex = pageIndexLeft;
                pageArticle = pageArticleLeft;
                
                // 目录页节点 - 分类在左侧
                // 路径: Page_Index_Left/Left_Categories/Scroll_Categories/Viewport/Content
                if (pageIndexLeft != null)
                {
                    Transform leftCategories = pageIndexLeft.Find("Left_Categories");
                    if (leftCategories != null)
                    {
                        Transform scrollCategories = leftCategories.Find("Scroll_Categories");
                        if (scrollCategories != null)
                        {
                            categoryContainer = scrollCategories.Find("Viewport/Content");
                        }
                    }
                    // 备用路径
                    if (categoryContainer == null)
                    {
                        categoryContainer = FindChildRecursive(pageIndexLeft, "Content");
                    }
                }
                
                // 目录页节点 - 条目在右侧
                // 路径: Page_Index_Right/Right_Entries/Scroll_Entries/Viewport/Content
                if (pageIndexRight != null)
                {
                    Transform rightEntries = pageIndexRight.Find("Right_Entries");
                    if (rightEntries != null)
                    {
                        Transform scrollEntries = rightEntries.Find("Scroll_Entries");
                        if (scrollEntries != null)
                        {
                            entryContainer = scrollEntries.Find("Viewport/Content");
                        }
                        // 空提示在 Right_Entries 下
                        txtEmptyHint = rightEntries.Find("Txt_EmptyHint")?.GetComponent<TMP_Text>();
                        if (txtEmptyHint == null)
                        {
                            txtEmptyHint = FindChildRecursive(rightEntries, "Txt_EmptyHint")?.GetComponent<TMP_Text>();
                        }
                    }
                    // 备用路径
                    if (entryContainer == null)
                    {
                        entryContainer = FindChildRecursive(pageIndexRight, "Content");
                    }
                }
                
                // 查找模板（在 Content 容器中）
                categoryTemplate = FindChildRecursive(uiRoot.transform, "Tgl_CategoryTemplate")?.gameObject;
                entryTemplate = FindChildRecursive(uiRoot.transform, "Tgl_EntryTemplate")?.gameObject;
                
                // 正文页节点 - 标题在 TopBar
                Transform topBar = FindChildRecursive(uiRoot.transform, "TopBar");
                if (topBar != null)
                {
                    txtArticleTitle = topBar.Find("Txt_Title")?.GetComponent<TMP_Text>();
                    if (txtArticleTitle == null)
                    {
                        txtArticleTitle = FindChildRecursive(topBar, "Txt_Title")?.GetComponent<TMP_Text>();
                    }
                }
                
                // 正文页节点 - 左侧内容（在 Page_Article_Left 中）
                if (pageArticleLeft != null)
                {
                    txtLeft = pageArticleLeft.Find("Txt_Left")?.GetComponent<TMP_Text>();
                    if (txtLeft == null)
                    {
                        txtLeft = FindChildRecursive(pageArticleLeft, "Txt_Left")?.GetComponent<TMP_Text>();
                    }
                }
                
                // 正文页节点 - 右侧内容（在 Page_Article_Right 中）
                if (pageArticleRight != null)
                {
                    txtRight = pageArticleRight.Find("Txt_Right")?.GetComponent<TMP_Text>();
                    if (txtRight == null)
                    {
                        txtRight = FindChildRecursive(pageArticleRight, "Txt_Right")?.GetComponent<TMP_Text>();
                    }
                }
                
                // 底部栏节点 - 翻页按钮和页码
                Transform bottomBar = FindChildRecursive(uiRoot.transform, "BottomBar");
                if (bottomBar != null)
                {
                    txtPageNumber = FindChildRecursive(bottomBar, "Txt_PageNumber")?.GetComponent<TMP_Text>();
                    btnPrevPage = FindChildRecursive(bottomBar, "Btn_PrevPage")?.GetComponent<Button>();
                    btnNextPage = FindChildRecursive(bottomBar, "Btn_NextPage")?.GetComponent<Button>();
                }
                
                // 返回按钮在 TopBar 下
                if (topBar != null)
                {
                    btnGoBack = FindChildRecursive(topBar, "Btn_Back")?.GetComponent<Button>();
                }
                
                // 关闭按钮（在 TopBar 下）
                btnClose = FindChildRecursive(uiRoot.transform, "Btn_Close")?.GetComponent<Button>();
                
                // 缓存额外的页面引用用于显示/隐藏
                _pageIndexRight = pageIndexRight;
                _pageArticleRight = pageArticleRight;
                
                // 查找并设置目录页标题标签
                // Lbl_Categories 在 Page_Index_Left/Left_Categories 下
                if (pageIndexLeft != null)
                {
                    Transform leftCategories = pageIndexLeft.Find("Left_Categories");
                    if (leftCategories != null)
                    {
                        lblCategories = leftCategories.Find("Lbl_Categories")?.GetComponent<TMP_Text>();
                    }
                    if (lblCategories == null)
                    {
                        lblCategories = FindChildRecursive(pageIndexLeft, "Lbl_Categories")?.GetComponent<TMP_Text>();
                    }
                    if (lblCategories != null)
                    {
                        lblCategories.text = "分类(demo)";
                        ModBehaviour.DevLog("[WikiUIManager] Lbl_Categories 文本已设置为: 分类(demo)");
                    }
                }
                
                // Lbl_Entries 在 Page_Index_Right/Right_Entries 下
                if (pageIndexRight != null)
                {
                    Transform rightEntries = pageIndexRight.Find("Right_Entries");
                    if (rightEntries != null)
                    {
                        lblEntries = rightEntries.Find("Lbl_Entries")?.GetComponent<TMP_Text>();
                    }
                    if (lblEntries == null)
                    {
                        lblEntries = FindChildRecursive(pageIndexRight, "Lbl_Entries")?.GetComponent<TMP_Text>();
                    }
                    if (lblEntries != null)
                    {
                        lblEntries.text = "条目";
                        ModBehaviour.DevLog("[WikiUIManager] Lbl_Entries 文本已设置为: 条目");
                    }
                }
                
                // 输出调试信息
                ModBehaviour.DevLog("[WikiUIManager] 节点缓存结果:");
                ModBehaviour.DevLog("  - pageIndexLeft: " + (pageIndexLeft != null));
                ModBehaviour.DevLog("  - pageIndexRight: " + (pageIndexRight != null));
                ModBehaviour.DevLog("  - pageArticleLeft: " + (pageArticleLeft != null));
                ModBehaviour.DevLog("  - pageArticleRight: " + (pageArticleRight != null));
                ModBehaviour.DevLog("  - categoryContainer: " + (categoryContainer != null));
                ModBehaviour.DevLog("  - entryContainer: " + (entryContainer != null));
                ModBehaviour.DevLog("  - categoryTemplate: " + (categoryTemplate != null));
                ModBehaviour.DevLog("  - entryTemplate: " + (entryTemplate != null));
                ModBehaviour.DevLog("  - txtArticleTitle: " + (txtArticleTitle != null));
                ModBehaviour.DevLog("  - txtLeft: " + (txtLeft != null));
                ModBehaviour.DevLog("  - txtRight: " + (txtRight != null));
                ModBehaviour.DevLog("  - txtPageNumber: " + (txtPageNumber != null));
                ModBehaviour.DevLog("  - btnPrevPage: " + (btnPrevPage != null));
                ModBehaviour.DevLog("  - btnNextPage: " + (btnNextPage != null));
                ModBehaviour.DevLog("  - btnGoBack: " + (btnGoBack != null));
                ModBehaviour.DevLog("  - btnClose: " + (btnClose != null));
                ModBehaviour.DevLog("  - lblCategories: " + (lblCategories != null));
                ModBehaviour.DevLog("  - lblEntries: " + (lblEntries != null));
                
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 缓存节点异常: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 递归查找子节点
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                var found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
        
        /// <summary>
        /// 打印UI层级结构（调试用）
        /// </summary>
        private void PrintHierarchy(Transform parent, int depth)
        {
            string indent = new string(' ', depth * 2);
            foreach (Transform child in parent)
            {
                ModBehaviour.DevLog(indent + "- " + child.name);
                if (depth < 5) // 增加深度限制以便更好地调试
                {
                    PrintHierarchy(child, depth + 1);
                }
            }
        }
        
        /// <summary>
        /// 获取 Transform 的完整路径（调试用）
        /// </summary>
        private string GetTransformPath(Transform t)
        {
            if (t == null) return "null";
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
        
        /// <summary>
        /// 绑定按钮事件
        /// </summary>
        private void BindButtonEvents()
        {
            if (btnClose != null)
            {
                btnClose.onClick.AddListener(CloseUI);
            }
            
            if (btnPrevPage != null)
            {
                btnPrevPage.onClick.AddListener(PrevPage);
            }
            
            if (btnNextPage != null)
            {
                btnNextPage.onClick.AddListener(NextPage);
            }
            
            if (btnGoBack != null)
            {
                btnGoBack.onClick.AddListener(GoBack);
            }
            
            ModBehaviour.DevLog("[WikiUIManager] 按钮事件已绑定");
        }
        
        /// <summary>
        /// 替换所有 TMP 组件的字体
        /// </summary>
        private void ReplaceAllFonts()
        {
            try
            {
                // 获取游戏原版字体
                TMP_FontAsset gameFont = GetGameFont();
                if (gameFont == null)
                {
                    ModBehaviour.DevLog("[WikiUIManager] 未找到游戏字体，跳过字体替换");
                    return;
                }
                
                // 替换所有 TMP 组件的字体
                var tmpComponents = uiRoot.GetComponentsInChildren<TMP_Text>(true);
                int count = 0;
                foreach (var tmp in tmpComponents)
                {
                    tmp.font = gameFont;
                    count++;
                }
                
                ModBehaviour.DevLog("[WikiUIManager] 已替换 " + count + " 个 TMP 组件的字体");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 字体替换失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取游戏原版字体
        /// </summary>
        private TMP_FontAsset GetGameFont()
        {
            try
            {
                // 尝试从现有 UI 获取字体
                var existingTMP = UnityEngine.Object.FindObjectOfType<TMP_Text>();
                if (existingTMP != null && existingTMP.font != null)
                {
                    return existingTMP.font;
                }
                
                // 尝试从资源加载
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    return fonts[0];
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 获取游戏字体失败: " + e.Message);
            }
            return null;
        }

        // ============================================================================
        // 页面切换
        // ============================================================================
        
        /// <summary>
        /// 显示目录页
        /// </summary>
        private void ShowIndexPage()
        {
            ModBehaviour.DevLog("[WikiUIManager] ShowIndexPage() 开始执行");
            
            isOnArticlePage = false;
            
            // 先隐藏正文页（左右两侧）- 在显示目录页之前
            if (pageArticle != null)
            {
                pageArticle.gameObject.SetActive(false);
                ModBehaviour.DevLog("[WikiUIManager] pageArticle SetActive(false), 结果: " + !pageArticle.gameObject.activeSelf);
            }
            if (_pageArticleRight != null)
            {
                _pageArticleRight.gameObject.SetActive(false);
                ModBehaviour.DevLog("[WikiUIManager] _pageArticleRight SetActive(false), 结果: " + !_pageArticleRight.gameObject.activeSelf);
            }
            
            // 再显示目录页（左右两侧）
            if (pageIndex != null)
            {
                pageIndex.gameObject.SetActive(true);
                ModBehaviour.DevLog("[WikiUIManager] pageIndex SetActive(true), 结果: " + pageIndex.gameObject.activeSelf);
            }
            if (_pageIndexRight != null)
            {
                _pageIndexRight.gameObject.SetActive(true);
                ModBehaviour.DevLog("[WikiUIManager] _pageIndexRight SetActive(true), 结果: " + _pageIndexRight.gameObject.activeSelf);
            }
            
            // 重置标题为默认值
            if (txtArticleTitle != null)
            {
                txtArticleTitle.text = "Boss Rush";
            }
            
            // 隐藏翻页按钮和页码（目录页不需要）
            if (btnPrevPage != null)
            {
                btnPrevPage.gameObject.SetActive(false);
            }
            if (btnNextPage != null)
            {
                btnNextPage.gameObject.SetActive(false);
            }
            if (txtPageNumber != null)
            {
                txtPageNumber.gameObject.SetActive(false);
            }
            
            // 隐藏返回按钮（目录页不需要）
            if (btnGoBack != null)
            {
                btnGoBack.gameObject.SetActive(false);
            }
            
            // 刷新分类列表（会根据 currentCategoryId 恢复选中状态）
            RefreshCategoryList();
            
            // 清除所有条目的选中状态（返回目录页时重置条目的 checkmark）
            ClearAllEntryToggles();
            
            // 恢复之前选中的分类，或默认选中第一个分类
            var categories = WikiContentManager.Instance.GetCategories();
            if (categories != null && categories.Count > 0)
            {
                // 如果之前有选中的分类，保持该分类；否则选中第一个
                string targetCategoryId = currentCategoryId;
                if (string.IsNullOrEmpty(targetCategoryId))
                {
                    targetCategoryId = categories[0].Id;
                }
                
                // 刷新条目列表（不重置 currentCategoryId）
                RefreshEntryList(targetCategoryId);
            }
            
            ModBehaviour.DevLog("[WikiUIManager] ShowIndexPage() 执行完成, isOnArticlePage=" + isOnArticlePage + ", currentCategoryId=" + currentCategoryId);
        }
        
        /// <summary>
        /// 清除所有条目的 Toggle 选中状态
        /// </summary>
        private void ClearAllEntryToggles()
        {
            if (entryContainer == null) return;
            
            foreach (Transform child in entryContainer)
            {
                if (child.gameObject == entryTemplate) continue;
                
                var toggle = child.GetComponent<Toggle>();
                if (toggle != null && toggle.isOn)
                {
                    toggle.SetIsOnWithoutNotify(false);
                }
            }
        }
        
        /// <summary>
        /// 清除其他分类的 Toggle 选中状态
        /// </summary>
        private void ClearOtherCategoryToggles(GameObject currentItem)
        {
            if (categoryContainer == null) return;
            
            foreach (Transform child in categoryContainer)
            {
                if (child.gameObject == categoryTemplate) continue;
                if (child.gameObject == currentItem) continue;
                
                var toggle = child.GetComponent<Toggle>();
                if (toggle != null && toggle.isOn)
                {
                    toggle.SetIsOnWithoutNotify(false);
                }
            }
        }
        
        /// <summary>
        /// 显示正文页
        /// </summary>
        private void ShowArticlePage(string entryId)
        {
            ModBehaviour.DevLog("[WikiUIManager] ShowArticlePage() 开始执行, entryId=" + entryId);
            
            if (string.IsNullOrEmpty(entryId))
            {
                ModBehaviour.DevLog("[WikiUIManager] ShowArticlePage() 条目ID为空，跳过");
                return;
            }
            
            currentEntryId = entryId;
            isOnArticlePage = true;
            
            // 隐藏目录页（左右两侧）
            if (pageIndex != null)
            {
                pageIndex.gameObject.SetActive(false);
                ModBehaviour.DevLog("[WikiUIManager] pageIndex 已隐藏: " + pageIndex.gameObject.activeSelf);
            }
            if (_pageIndexRight != null)
            {
                _pageIndexRight.gameObject.SetActive(false);
                ModBehaviour.DevLog("[WikiUIManager] _pageIndexRight 已隐藏: " + _pageIndexRight.gameObject.activeSelf);
            }
            
            // 显示正文页（左右两侧）
            if (pageArticle != null)
            {
                pageArticle.gameObject.SetActive(true);
                ModBehaviour.DevLog("[WikiUIManager] pageArticle 已激活: " + pageArticle.gameObject.activeSelf);
            }
            if (_pageArticleRight != null)
            {
                _pageArticleRight.gameObject.SetActive(true);
                ModBehaviour.DevLog("[WikiUIManager] _pageArticleRight 已激活: " + _pageArticleRight.gameObject.activeSelf);
            }
            
            // 显示翻页按钮、页码和返回按钮（正文页需要）
            if (btnPrevPage != null)
            {
                btnPrevPage.gameObject.SetActive(true);
            }
            if (btnNextPage != null)
            {
                btnNextPage.gameObject.SetActive(true);
            }
            if (txtPageNumber != null)
            {
                txtPageNumber.gameObject.SetActive(true);
            }
            if (btnGoBack != null)
            {
                btnGoBack.gameObject.SetActive(true);
            }
            
            // 加载条目内容
            var entry = WikiContentManager.Instance.GetEntry(entryId);
            string content = WikiContentManager.Instance.LoadEntryContent(entryId);
            
            // 设置标题
            if (txtArticleTitle != null && entry != null)
            {
                txtArticleTitle.text = entry.GetTitle();
            }
            
            // 使用TMP内置分页设置内容
            SetupContentWithTMPPaging(content);
            
            // 显示第一页
            currentPageIndex = 0;
            RefreshArticleContent();
            
            ModBehaviour.DevLog("[WikiUIManager] ShowArticlePage() 执行完成, isOnArticlePage=" + isOnArticlePage);
        }
        
        /// <summary>
        /// 返回目录页
        /// </summary>
        private void GoBack()
        {
            ModBehaviour.DevLog("[WikiUIManager] GoBack() 被调用, 当前 isOnArticlePage=" + isOnArticlePage);
            ShowIndexPage();
        }
        
        // ============================================================================
        // 目录页功能
        // ============================================================================
        
        /// <summary>
        /// 刷新分类列表
        /// </summary>
        private void RefreshCategoryList()
        {
            if (categoryContainer == null || categoryTemplate == null)
            {
                ModBehaviour.DevLog("[WikiUIManager] 分类容器或模板未找到");
                return;
            }
            
            // 输出调试信息
            ModBehaviour.DevLog("[WikiUIManager] categoryContainer 路径: " + GetTransformPath(categoryContainer));
            ModBehaviour.DevLog("[WikiUIManager] categoryTemplate 路径: " + GetTransformPath(categoryTemplate.transform));
            ModBehaviour.DevLog("[WikiUIManager] categoryContainer 子节点数: " + categoryContainer.childCount);
            
            // 清空现有分类（保留模板）
            ClearContainer(categoryContainer, categoryTemplate);
            
            // 获取分类列表
            var categories = WikiContentManager.Instance.GetCategories();
            if (categories == null || categories.Count == 0)
            {
                ModBehaviour.DevLog("[WikiUIManager] 没有分类数据");
                return;
            }
            
            // 创建分类项
            bool isFirst = true;
            foreach (var category in categories)
            {
                var item = UnityEngine.Object.Instantiate(categoryTemplate, categoryContainer);
                item.name = "Category_" + category.Id;
                
                // 设置文本 - 查找名为 Txt_Name 的文本组件
                Transform txtNameTransform = FindChildRecursive(item.transform, "Txt_Name");
                TMP_Text text = txtNameTransform != null ? txtNameTransform.GetComponent<TMP_Text>() : item.GetComponentInChildren<TMP_Text>();
                if (text != null)
                {
                    text.text = category.GetTitle();
                    ModBehaviour.DevLog("[WikiUIManager] 设置分类文本: " + category.GetTitle());
                }
                else
                {
                    ModBehaviour.DevLog("[WikiUIManager] 警告: 分类项未找到文本组件");
                }
                
                // 绑定点击事件
                var toggle = item.GetComponent<Toggle>();
                if (toggle != null)
                {
                    string categoryId = category.Id;
                    
                    // 移除 ToggleGroup 关联，避免自动选中问题
                    toggle.group = null;
                    
                    // 先移除所有监听器，避免重复绑定
                    toggle.onValueChanged.RemoveAllListeners();
                    
                    // 设置初始状态：如果有之前选中的分类则恢复，否则选中第一个
                    bool shouldBeSelected = false;
                    if (!string.IsNullOrEmpty(currentCategoryId))
                    {
                        // 恢复之前选中的分类
                        shouldBeSelected = (categoryId == currentCategoryId);
                    }
                    else
                    {
                        // 没有之前的选择，默认选中第一个
                        shouldBeSelected = isFirst;
                    }
                    toggle.SetIsOnWithoutNotify(shouldBeSelected);
                    
                    // 绑定事件
                    toggle.onValueChanged.AddListener((isOn) =>
                    {
                        if (isOn)
                        {
                            // 清除其他分类的选中状态
                            ClearOtherCategoryToggles(item);
                            ModBehaviour.DevLog("[WikiUIManager] 分类被选中: " + categoryId);
                            SelectCategory(categoryId);
                        }
                    });
                }
                else
                {
                    // 如果没有 Toggle，尝试使用 Button
                    var button = item.GetComponent<Button>();
                    if (button != null)
                    {
                        string categoryId = category.Id;
                        button.onClick.AddListener(() => SelectCategory(categoryId));
                    }
                }
                
                // 最后再激活，避免触发事件
                item.SetActive(true);
                isFirst = false;
            }
            
            ModBehaviour.DevLog("[WikiUIManager] 已刷新分类列表: " + categories.Count + " 个分类");
        }
        
        /// <summary>
        /// 选择分类
        /// </summary>
        private void SelectCategory(string categoryId)
        {
            if (currentCategoryId == categoryId)
            {
                return;
            }
            
            currentCategoryId = categoryId;
            RefreshEntryList(categoryId);
        }
        
        /// <summary>
        /// 刷新条目列表
        /// </summary>
        private void RefreshEntryList(string categoryId)
        {
            if (entryContainer == null || entryTemplate == null)
            {
                ModBehaviour.DevLog("[WikiUIManager] 条目容器或模板未找到");
                return;
            }
            
            // 输出调试信息
            ModBehaviour.DevLog("[WikiUIManager] entryContainer 路径: " + GetTransformPath(entryContainer));
            ModBehaviour.DevLog("[WikiUIManager] entryTemplate 路径: " + GetTransformPath(entryTemplate.transform));
            
            // 清空现有条目（保留模板）
            ClearContainer(entryContainer, entryTemplate);
            
            // 获取条目列表
            var entries = WikiContentManager.Instance.GetEntries(categoryId);
            
            // 显示/隐藏空提示
            if (txtEmptyHint != null)
            {
                txtEmptyHint.gameObject.SetActive(entries == null || entries.Count == 0);
            }
            
            if (entries == null || entries.Count == 0)
            {
                ModBehaviour.DevLog("[WikiUIManager] 分类 " + categoryId + " 没有条目");
                return;
            }
            
            // 创建条目项
            foreach (var entry in entries)
            {
                var item = UnityEngine.Object.Instantiate(entryTemplate, entryContainer);
                item.name = "Entry_" + entry.Id;
                
                // 设置文本 - 查找名为 Txt_Name 的文本组件
                Transform txtNameTransform = FindChildRecursive(item.transform, "Txt_Name");
                TMP_Text text = txtNameTransform != null ? txtNameTransform.GetComponent<TMP_Text>() : item.GetComponentInChildren<TMP_Text>();
                if (text != null)
                {
                    text.text = entry.GetTitle();
                    ModBehaviour.DevLog("[WikiUIManager] 设置条目文本: " + entry.GetTitle());
                }
                else
                {
                    ModBehaviour.DevLog("[WikiUIManager] 警告: 条目项未找到文本组件");
                }
                
                // 绑定点击事件
                var toggle = item.GetComponent<Toggle>();
                if (toggle != null)
                {
                    string entryId = entry.Id;
                    
                    // 移除 ToggleGroup 关联，避免自动选中
                    toggle.group = null;
                    
                    // 先移除所有监听器，避免重复绑定
                    toggle.onValueChanged.RemoveAllListeners();
                    
                    // 确保初始状态为未选中
                    toggle.SetIsOnWithoutNotify(false);
                    
                    // 绑定事件 - 点击时先清除其他条目的选中状态
                    toggle.onValueChanged.AddListener((isOn) =>
                    {
                        if (isOn && !isOnArticlePage)
                        {
                            // 清除其他条目的选中状态
                            ClearOtherEntryToggles(item);
                            ModBehaviour.DevLog("[WikiUIManager] 条目被选中: " + entryId);
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
                
                // 最后再激活，避免触发事件
                item.SetActive(true);
            }
            
            ModBehaviour.DevLog("[WikiUIManager] 已刷新条目列表: " + entries.Count + " 个条目");
        }
        
        /// <summary>
        /// 清除其他条目的 Toggle 选中状态
        /// </summary>
        private void ClearOtherEntryToggles(GameObject currentItem)
        {
            if (entryContainer == null) return;
            
            foreach (Transform child in entryContainer)
            {
                if (child.gameObject == entryTemplate) continue;
                if (child.gameObject == currentItem) continue;
                
                var toggle = child.GetComponent<Toggle>();
                if (toggle != null && toggle.isOn)
                {
                    toggle.SetIsOnWithoutNotify(false);
                }
            }
        }
        
        /// <summary>
        /// 清空容器（保留模板）
        /// </summary>
        private void ClearContainer(Transform container, GameObject template)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (child.gameObject != template)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        // ============================================================================
        // 正文页功能
        // ============================================================================
        
        /// <summary>
        /// 设置内容并使用TMP内置分页
        /// </summary>
        private void SetupContentWithTMPPaging(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                currentParsedContent = "[内容为空]";
            }
            else
            {
                // 解析 Markdown
                currentParsedContent = WikiContentManager.Instance.ParseMarkdown(content);
            }
            
            // 设置左栏为Page模式，让TMP自动计算分页
            if (txtLeft != null)
            {
                txtLeft.text = currentParsedContent;
                txtLeft.fontSize = 18f;
                txtLeft.color = new Color(0.25f, 0.2f, 0.15f, 1f);
                txtLeft.enableWordWrapping = true;
                txtLeft.overflowMode = TMPro.TextOverflowModes.Page;
                txtLeft.pageToDisplay = 1;
                
                // 强制更新以获取正确的pageCount
                txtLeft.ForceMeshUpdate();
                
                // 获取TMP计算出的总页数
                totalPages = txtLeft.textInfo.pageCount;
                if (totalPages < 1) totalPages = 1;
                
                // 计算书本的总"翻页数"（每次翻页显示左右两栏，即2个TMP页）
                // 例如：TMP有5页，则书本有3翻（1-2, 3-4, 5-空）
                int bookPages = (totalPages + 1) / 2;
                
                ModBehaviour.DevLog("[WikiUIManager] TMP分页完成: TMP页数=" + totalPages + ", 书本翻页数=" + bookPages);
                
                // 添加链接点击处理
                EnsureLinkHandler(txtLeft);
            }
            
            // 设置右栏
            if (txtRight != null)
            {
                txtRight.text = currentParsedContent;
                txtRight.fontSize = 18f;
                txtRight.color = new Color(0.25f, 0.2f, 0.15f, 1f);
                txtRight.enableWordWrapping = true;
                txtRight.overflowMode = TMPro.TextOverflowModes.Page;
                txtRight.pageToDisplay = 2; // 右栏显示第2页
                txtRight.ForceMeshUpdate();
                
                EnsureLinkHandler(txtRight);
            }
            
            // 初始化页码
            currentPageIndex = 0; // 书本的第0翻（显示TMP的第1-2页）
            leftPageToDisplay = 1;
            rightPageToDisplay = 2;
        }
        
        /// <summary>
        /// 刷新正文内容显示（使用TMP内置分页）
        /// </summary>
        private void RefreshArticleContent()
        {
            // 计算当前翻页对应的TMP页码
            leftPageToDisplay = currentPageIndex * 2 + 1;
            rightPageToDisplay = currentPageIndex * 2 + 2;
            
            ModBehaviour.DevLog("[WikiUIManager] RefreshArticleContent - 书本翻页: " + currentPageIndex + ", 左栏TMP页: " + leftPageToDisplay + ", 右栏TMP页: " + rightPageToDisplay + ", TMP总页数: " + totalPages);
            
            // 更新左栏显示的页
            if (txtLeft != null)
            {
                txtLeft.pageToDisplay = leftPageToDisplay;
                txtLeft.ForceMeshUpdate();
            }
            
            // 更新右栏显示的页（如果超出总页数则显示空）
            if (txtRight != null)
            {
                if (rightPageToDisplay <= totalPages)
                {
                    txtRight.pageToDisplay = rightPageToDisplay;
                }
                else
                {
                    // 右栏没有内容，设置为空或显示一个不存在的页
                    txtRight.pageToDisplay = rightPageToDisplay; // TMP会自动处理超出范围的情况
                }
                txtRight.ForceMeshUpdate();
            }
            
            // 计算书本的总翻页数
            int bookTotalPages = (totalPages + 1) / 2;
            
            // 更新页码显示
            if (txtPageNumber != null)
            {
                txtPageNumber.text = (currentPageIndex + 1) + "/" + bookTotalPages;
            }
            
            // 更新翻页按钮状态
            if (btnPrevPage != null)
            {
                btnPrevPage.interactable = currentPageIndex > 0;
            }
            if (btnNextPage != null)
            {
                btnNextPage.interactable = currentPageIndex < bookTotalPages - 1;
            }
        }
        
        /// <summary>
        /// 确保 TMP_Text 有链接点击处理组件
        /// </summary>
        private void EnsureLinkHandler(TMP_Text tmpText)
        {
            if (tmpText == null) return;
            
            // 如果没有 TMPLinkHandler 组件，添加一个
            if (tmpText.GetComponent<TMPLinkHandler>() == null)
            {
                tmpText.gameObject.AddComponent<TMPLinkHandler>();
                // TMP 需要启用 raycastTarget 才能接收点击事件
                tmpText.raycastTarget = true;
                ModBehaviour.DevLog("[WikiUIManager] 已为 " + tmpText.name + " 添加链接点击处理");
            }
        }
        
        // ============================================================================
        // 翻页功能
        // ============================================================================
        
        /// <summary>
        /// 下一页
        /// </summary>
        private void NextPage()
        {
            // 计算书本的总翻页数
            int bookTotalPages = (totalPages + 1) / 2;
            
            ModBehaviour.DevLog("[WikiUIManager] NextPage() 被调用, isOnArticlePage=" + isOnArticlePage + ", currentPageIndex=" + currentPageIndex + ", bookTotalPages=" + bookTotalPages);
            
            if (!isOnArticlePage)
            {
                ModBehaviour.DevLog("[WikiUIManager] NextPage() 在目录页被调用，忽略");
                return;
            }
            
            if (currentPageIndex < bookTotalPages - 1)
            {
                currentPageIndex++;
                RefreshArticleContent();
            }
        }
        
        /// <summary>
        /// 上一页
        /// </summary>
        private void PrevPage()
        {
            ModBehaviour.DevLog("[WikiUIManager] PrevPage() 被调用, isOnArticlePage=" + isOnArticlePage + ", currentPageIndex=" + currentPageIndex);
            
            if (!isOnArticlePage)
            {
                ModBehaviour.DevLog("[WikiUIManager] PrevPage() 在目录页被调用，忽略");
                return;
            }
            
            if (currentPageIndex > 0)
            {
                currentPageIndex--;
                RefreshArticleContent();
            }
        }
        
        // ============================================================================
        // 输入控制
        // ============================================================================
        
        /// <summary>
        /// 禁用玩家输入
        /// </summary>
        private void DisablePlayerInput()
        {
            try
            {
                // 使用游戏的 InputManager 禁用输入
                if (uiRoot != null)
                {
                    InputManager.DisableInput(uiRoot);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 禁用输入失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 启用玩家输入
        /// </summary>
        private void EnablePlayerInput()
        {
            try
            {
                // 使用游戏的 InputManager 启用输入
                if (uiRoot != null)
                {
                    InputManager.ActiveInput(uiRoot);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 启用输入失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // ESC 键处理
        // ============================================================================
        
        /// <summary>
        /// 订阅 ESC 键事件
        /// </summary>
        private void SubscribeEscapeKey()
        {
            try
            {
                UIInputManager.OnCancelEarly += OnEscapePressed;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 订阅 ESC 事件失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消订阅 ESC 键事件
        /// </summary>
        private void UnsubscribeEscapeKey()
        {
            try
            {
                UIInputManager.OnCancelEarly -= OnEscapePressed;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiUIManager] 取消订阅 ESC 事件失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// ESC 键按下处理
        /// </summary>
        private void OnEscapePressed(UIInputEventData data)
        {
            if (!IsUIOpen)
            {
                return;
            }
            
            // 标记事件已使用，防止其他 UI 响应
            data.Use();
            
            if (isOnArticlePage)
            {
                // 正文页按 ESC 返回目录
                GoBack();
            }
            else
            {
                // 目录页按 ESC 关闭 UI
                CloseUI();
            }
        }
    }
    
    /// <summary>
    /// TMP 链接点击处理组件
    /// 挂载到 TMP_Text 对象上，处理 link 标签的点击事件
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
            
            // 获取点击位置对应的链接索引
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(tmpText, eventData.position, null);
            
            if (linkIndex != -1)
            {
                // 获取链接信息
                TMP_LinkInfo linkInfo = tmpText.textInfo.linkInfo[linkIndex];
                string url = linkInfo.GetLinkID();
                
                if (!string.IsNullOrEmpty(url))
                {
                    ModBehaviour.DevLog("[TMPLinkHandler] 点击链接: " + url);
                    
                    // 在浏览器中打开链接
                    Application.OpenURL(url);
                }
            }
        }
    }
}

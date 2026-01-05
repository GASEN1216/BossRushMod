// ============================================================================
// BossFilter.cs - Boss 池筛选模块
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的 Boss 池筛选功能，包括：
//   - Boss 启用/禁用状态管理
//   - 使用官方 UI Prefab 的配置窗口（V1.3.10+ API）
//   - 配置持久化（集成到 BossRushModConfig.txt）
//   
// 快捷键：
//   - Ctrl+F10: 打开/关闭 Boss 池配置窗口
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Duckov.Utilities;
using TMPro;

namespace BossRush
{
    /// <summary>
    /// Boss 池筛选模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Boss 池筛选字段

        /// <summary>Boss 启用状态字典 (key: boss name, value: enabled)</summary>
        private Dictionary<string, bool> bossEnabledStates = new Dictionary<string, bool>();

        /// <summary>Boss 池配置窗口是否显示</summary>
        private bool showBossPoolWindow = false;

        /// <summary>Boss 池筛选是否已初始化</summary>
        private bool bossPoolFilterInitialized = false;
        
        // [性能优化] 过滤后的 Boss 列表缓存
        private List<EnemyPresetInfo> _filteredPresetsCache = null;
        private bool _filteredPresetsCacheDirty = true;

        /// <summary>Boss 池 UI Canvas</summary>
        private GameObject bossPoolCanvas = null;

        /// <summary>Boss 池 UI 面板</summary>
        private GameObject bossPoolPanel = null;

        /// <summary>Boss 池 UI ScrollRect 内容容器</summary>
        private RectTransform bossPoolContent = null;

        /// <summary>Boss 池 UI ScrollRect 组件引用</summary>
        private ScrollRect bossPoolScrollRect = null;

        /// <summary>Boss 池 UI Toggle 列表</summary>
        private Dictionary<string, Toggle> bossToggles = new Dictionary<string, Toggle>();

        /// <summary>Boss 池 UI 统计文本</summary>
        private TextMeshProUGUI statsText = null;

        #endregion

        #region Boss 池筛选初始化

        /// <summary>
        /// 初始化 Boss 池筛选配置
        /// 应在 enemyPresets 初始化后调用
        /// </summary>
        private void InitializeBossPoolFilter()
        {
            if (bossPoolFilterInitialized)
            {
                return;
            }

            try
            {
                bossEnabledStates.Clear();

                // 从 enemyPresets 获取所有 Boss
                if (enemyPresets != null && enemyPresets.Count > 0)
                {
                    foreach (var preset in enemyPresets)
                    {
                        if (preset == null || string.IsNullOrEmpty(preset.name))
                        {
                            continue;
                        }

                        // 默认启用所有 Boss
                        bossEnabledStates[preset.name] = true;
                    }
                }

                // 从配置中加载禁用的 Boss
                if (config != null && config.disabledBosses != null)
                {
                    foreach (string disabledBoss in config.disabledBosses)
                    {
                        if (!string.IsNullOrEmpty(disabledBoss) && bossEnabledStates.ContainsKey(disabledBoss))
                        {
                            bossEnabledStates[disabledBoss] = false;
                        }
                    }
                }

                bossPoolFilterInitialized = true;
                InvalidateFilteredPresetsCache();  // [性能优化] 初始化后标记缓存需要刷新

                int enabledCount = bossEnabledStates.Count(kv => kv.Value);
                int totalCount = bossEnabledStates.Count;
                DevLog("[BossRush] Boss 池筛选初始化完成，已启用 " + enabledCount + "/" + totalCount + " 个 Boss");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] InitializeBossPoolFilter 失败: " + ex.Message);
            }
        }

        #endregion

        #region Boss 启用状态管理

        /// <summary>
        /// 检查指定 Boss 是否启用
        /// </summary>
        public bool IsBossEnabled(string bossName)
        {
            if (string.IsNullOrEmpty(bossName))
            {
                return true;
            }

            bool enabled;
            if (bossEnabledStates.TryGetValue(bossName, out enabled))
            {
                return enabled;
            }

            return true;
        }

        /// <summary>
        /// 设置 Boss 启用状态
        /// </summary>
        public void SetBossEnabled(string bossName, bool enabled)
        {
            if (string.IsNullOrEmpty(bossName))
            {
                return;
            }

            bossEnabledStates[bossName] = enabled;
            InvalidateFilteredPresetsCache();  // [性能优化] 标记缓存需要刷新
        }

        /// <summary>
        /// 获取过滤后的 Boss 列表
        /// [性能优化] 使用缓存，只在 Boss 启用状态变化时重新计算
        /// </summary>
        public List<EnemyPresetInfo> GetFilteredEnemyPresets()
        {
            if (enemyPresets == null)
            {
                return new List<EnemyPresetInfo>();
            }
            
            // [性能优化] 如果缓存有效，直接返回缓存
            if (!_filteredPresetsCacheDirty && _filteredPresetsCache != null)
            {
                return _filteredPresetsCache;
            }

            // 重新计算过滤后的列表
            _filteredPresetsCache = enemyPresets.Where(preset => 
                preset != null && 
                !string.IsNullOrEmpty(preset.name) && 
                IsBossEnabled(preset.name)
            ).ToList();
            
            _filteredPresetsCacheDirty = false;
            return _filteredPresetsCache;
        }
        
        /// <summary>
        /// 标记过滤缓存为脏（需要重新计算）
        /// 在 Boss 启用状态变化时调用
        /// </summary>
        private void InvalidateFilteredPresetsCache()
        {
            _filteredPresetsCacheDirty = true;
        }

        /// <summary>
        /// 全选所有 Boss
        /// </summary>
        public void EnableAllBosses()
        {
            var keys = bossEnabledStates.Keys.ToList();
            foreach (string key in keys)
            {
                bossEnabledStates[key] = true;
            }
            InvalidateFilteredPresetsCache();  // [性能优化] 标记缓存需要刷新
            RefreshBossPoolUI();
        }

        /// <summary>
        /// 全不选所有 Boss
        /// </summary>
        public void DisableAllBosses()
        {
            var keys = bossEnabledStates.Keys.ToList();
            foreach (string key in keys)
            {
                bossEnabledStates[key] = false;
            }
            InvalidateFilteredPresetsCache();  // [性能优化] 标记缓存需要刷新
            RefreshBossPoolUI();
        }

        /// <summary>
        /// 将 Boss 池状态同步到配置并保存
        /// </summary>
        private void SyncBossPoolToConfig()
        {
            try
            {
                if (config == null)
                {
                    config = new BossRushConfig();
                }

                if (config.disabledBosses == null)
                {
                    config.disabledBosses = new List<string>();
                }
                else
                {
                    config.disabledBosses.Clear();
                }

                foreach (var kv in bossEnabledStates)
                {
                    if (!kv.Value)
                    {
                        config.disabledBosses.Add(kv.Key);
                    }
                }

                SaveConfigToFile();
                DevLog("[BossRush] Boss 池配置已保存，禁用 " + config.disabledBosses.Count + " 个 Boss");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] SyncBossPoolToConfig 失败: " + ex.Message);
            }
        }

        #endregion

        #region 官方 UI Prefab 窗口

        /// <summary>
        /// 打开 Boss 池配置窗口
        /// </summary>
        public void OpenBossPoolWindow()
        {
            // 如果 enemyPresets 为空，先初始化敌人预设列表
            if (enemyPresets == null || enemyPresets.Count == 0)
            {
                DevLog("[BossRush] Boss 池窗口打开时 enemyPresets 为空，尝试初始化...");
                InitializeEnemyPresets();
            }

            // 确保 Boss 池筛选已初始化
            if (!bossPoolFilterInitialized && enemyPresets != null && enemyPresets.Count > 0)
            {
                InitializeBossPoolFilter();
            }

            // 创建或显示 UI
            if (bossPoolCanvas == null)
            {
                CreateBossPoolUI();
            }
            else
            {
                bossPoolCanvas.SetActive(true);
                RefreshBossPoolUI();
            }

            // 将滚动位置重置到顶部
            if (bossPoolScrollRect != null)
            {
                bossPoolScrollRect.verticalNormalizedPosition = 1f;
            }

            // 禁用游戏输入，阻止 InputManager 更新鼠标状态
            InputManager.DisableInput(bossPoolCanvas);

            showBossPoolWindow = true;
            DevLog("[BossRush] 打开 Boss 池配置窗口，当前 Boss 数量: " + (enemyPresets != null ? enemyPresets.Count : 0));
        }

        /// <summary>
        /// 关闭 Boss 池配置窗口
        /// </summary>
        public void CloseBossPoolWindow()
        {
            // 恢复游戏输入
            if (bossPoolCanvas != null)
            {
                InputManager.ActiveInput(bossPoolCanvas);
                bossPoolCanvas.SetActive(false);
            }

            showBossPoolWindow = false;
            DevLog("[BossRush] 关闭 Boss 池配置窗口");
        }

        /// <summary>
        /// 创建 Boss 池 UI（使用官方 Prefab）
        /// </summary>
        private void CreateBossPoolUI()
        {
            try
            {
                // 创建 Canvas
                bossPoolCanvas = new GameObject("BossPoolCanvas");
                Canvas canvas = bossPoolCanvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;  // 降低层级，避免遮挡鼠标光标
                bossPoolCanvas.AddComponent<CanvasScaler>();
                bossPoolCanvas.AddComponent<GraphicRaycaster>();
                UnityEngine.Object.DontDestroyOnLoad(bossPoolCanvas);

                // 创建半透明背景
                GameObject bgObj = new GameObject("Background");
                bgObj.transform.SetParent(bossPoolCanvas.transform, false);
                Image bgImage = bgObj.AddComponent<Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0.7f);
                RectTransform bgRect = bgObj.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero;
                bgRect.offsetMax = Vector2.zero;

                // 创建主面板（加大尺寸）
                bossPoolPanel = new GameObject("Panel");
                bossPoolPanel.transform.SetParent(bossPoolCanvas.transform, false);
                Image panelImage = bossPoolPanel.AddComponent<Image>();
                panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
                RectTransform panelRect = bossPoolPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(550f, 650f);  // 加大面板尺寸

                // 创建标题
                CreateTitleBar(bossPoolPanel.transform);

                // 创建工具栏（全选/全不选按钮）
                CreateToolbar(bossPoolPanel.transform);

                // 创建滚动列表
                CreateScrollView(bossPoolPanel.transform);

                // 创建统计信息
                CreateStatsBar(bossPoolPanel.transform);

                // 创建底部按钮
                CreateBottomButtons(bossPoolPanel.transform);

                // 填充 Boss 列表
                PopulateBossList();

                DevLog("[BossRush] Boss 池 UI 创建完成");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] CreateBossPoolUI 失败: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        /// <summary>
        /// 创建标题栏
        /// </summary>
        private void CreateTitleBar(Transform parent)
        {
            GameObject titleBar = new GameObject("TitleBar");
            titleBar.transform.SetParent(parent, false);
            RectTransform titleRect = titleBar.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, 0f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            // 标题文本
            GameObject titleTextObj = new GameObject("TitleText");
            titleTextObj.transform.SetParent(titleBar.transform, false);
            TextMeshProUGUI titleText = titleTextObj.AddComponent<TextMeshProUGUI>();
            titleText.text = L10n.T("Boss池设置 (Ctrl+F10)", "Boss Pool Settings (Ctrl+F10)");
            titleText.fontSize = 24;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            RectTransform titleTextRect = titleTextObj.GetComponent<RectTransform>();
            titleTextRect.anchorMin = Vector2.zero;
            titleTextRect.anchorMax = Vector2.one;
            titleTextRect.offsetMin = new Vector2(10f, 0f);
            titleTextRect.offsetMax = new Vector2(-40f, 0f);

            // 关闭按钮（使用官方 Button prefab）
            Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
            if (buttonPrefab != null)
            {
                Button closeBtn = UnityEngine.Object.Instantiate(buttonPrefab, titleBar.transform);
                RectTransform closeBtnRect = closeBtn.GetComponent<RectTransform>();
                closeBtnRect.anchorMin = new Vector2(1f, 0.5f);
                closeBtnRect.anchorMax = new Vector2(1f, 0.5f);
                closeBtnRect.pivot = new Vector2(1f, 0.5f);
                closeBtnRect.anchoredPosition = new Vector2(-10f, 0f);
                closeBtnRect.sizeDelta = new Vector2(35f, 35f);

                // 设置按钮文本
                TextMeshProUGUI btnText = closeBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = "X";
                    btnText.fontSize = 20;
                }

                closeBtn.onClick.AddListener(() => CloseBossPoolWindow());
            }
        }

        /// <summary>
        /// 创建工具栏
        /// </summary>
        private void CreateToolbar(Transform parent)
        {
            GameObject toolbar = new GameObject("Toolbar");
            toolbar.transform.SetParent(parent, false);
            HorizontalLayoutGroup hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(10, 10, 5, 5);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            RectTransform toolbarRect = toolbar.GetComponent<RectTransform>();
            toolbarRect.anchorMin = new Vector2(0f, 1f);
            toolbarRect.anchorMax = new Vector2(1f, 1f);
            toolbarRect.pivot = new Vector2(0.5f, 1f);
            toolbarRect.anchoredPosition = new Vector2(0f, -50f);
            toolbarRect.sizeDelta = new Vector2(0f, 45f);

            Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
            if (buttonPrefab != null)
            {
                // 全选按钮
                Button selectAllBtn = UnityEngine.Object.Instantiate(buttonPrefab, toolbar.transform);
                LayoutElement le1 = selectAllBtn.gameObject.AddComponent<LayoutElement>();
                le1.preferredWidth = 100f;
                le1.preferredHeight = 35f;
                TextMeshProUGUI txt1 = selectAllBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (txt1 != null) txt1.text = L10n.T("全选", "Select All");
                selectAllBtn.onClick.AddListener(() => EnableAllBosses());

                // 全不选按钮
                Button deselectAllBtn = UnityEngine.Object.Instantiate(buttonPrefab, toolbar.transform);
                LayoutElement le2 = deselectAllBtn.gameObject.AddComponent<LayoutElement>();
                le2.preferredWidth = 100f;
                le2.preferredHeight = 35f;
                TextMeshProUGUI txt2 = deselectAllBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (txt2 != null) txt2.text = L10n.T("全不选", "Deselect All");
                deselectAllBtn.onClick.AddListener(() => DisableAllBosses());
            }
        }

        /// <summary>
        /// 创建滚动视图
        /// </summary>
        private void CreateScrollView(Transform parent)
        {
            // 尝试使用官方 ScrollRect prefab
            ScrollRect scrollRectPrefab = GameplayDataSettings.UIPrefabs.ScrollRect;
            
            GameObject scrollViewObj;
            ScrollRect scrollRect;

            if (scrollRectPrefab != null)
            {
                // 使用官方 prefab
                scrollRect = UnityEngine.Object.Instantiate(scrollRectPrefab, parent);
                scrollViewObj = scrollRect.gameObject;
                scrollViewObj.name = "BossScrollView";
            }
            else
            {
                // 回退：手动创建
                scrollViewObj = new GameObject("BossScrollView");
                scrollViewObj.transform.SetParent(parent, false);
                scrollRect = scrollViewObj.AddComponent<ScrollRect>();
                
                // 创建 Viewport
                GameObject viewport = new GameObject("Viewport");
                viewport.transform.SetParent(scrollViewObj.transform, false);
                Image vpImage = viewport.AddComponent<Image>();
                vpImage.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                Mask mask = viewport.AddComponent<Mask>();
                mask.showMaskGraphic = true;
                RectTransform vpRect = viewport.GetComponent<RectTransform>();
                vpRect.anchorMin = Vector2.zero;
                vpRect.anchorMax = Vector2.one;
                vpRect.offsetMin = Vector2.zero;
                vpRect.offsetMax = Vector2.zero;
                scrollRect.viewport = vpRect;

                // 创建 Content
                GameObject content = new GameObject("Content");
                content.transform.SetParent(viewport.transform, false);
                bossPoolContent = content.AddComponent<RectTransform>();
                bossPoolContent.anchorMin = new Vector2(0f, 1f);
                bossPoolContent.anchorMax = new Vector2(1f, 1f);
                bossPoolContent.pivot = new Vector2(0.5f, 1f);
                bossPoolContent.anchoredPosition = Vector2.zero;
                
                VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 5f;
                vlg.padding = new RectOffset(10, 10, 10, 10);
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;

                ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scrollRect.content = bossPoolContent;
            }

            // 设置 ScrollRect 位置和大小
            RectTransform scrollRectTransform = scrollViewObj.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(10f, 100f);
            scrollRectTransform.offsetMax = new Vector2(-10f, -100f);

            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 0.5f;  // 降低滚动灵敏度
            scrollRect.movementType = ScrollRect.MovementType.Clamped;  // 启用拖拽滚动
            scrollRect.inertia = true;  // 启用惯性
            scrollRect.decelerationRate = 0.135f;  // 惯性减速率

            // 保存 ScrollRect 引用，用于后续重置滚动位置
            bossPoolScrollRect = scrollRect;

            // 如果使用了官方 prefab，需要找到或创建 Content
            if (scrollRectPrefab != null)
            {
                bossPoolContent = scrollRect.content;
                if (bossPoolContent == null)
                {
                    // 创建 Content
                    GameObject content = new GameObject("Content");
                    content.transform.SetParent(scrollRect.viewport != null ? scrollRect.viewport : scrollViewObj.transform, false);
                    bossPoolContent = content.AddComponent<RectTransform>();
                    bossPoolContent.anchorMin = new Vector2(0f, 1f);
                    bossPoolContent.anchorMax = new Vector2(1f, 1f);
                    bossPoolContent.pivot = new Vector2(0.5f, 1f);
                    scrollRect.content = bossPoolContent;
                }

                // 确保有布局组件
                if (bossPoolContent.GetComponent<VerticalLayoutGroup>() == null)
                {
                    VerticalLayoutGroup vlg = bossPoolContent.gameObject.AddComponent<VerticalLayoutGroup>();
                    vlg.spacing = 5f;
                    vlg.padding = new RectOffset(10, 10, 10, 10);
                    vlg.childAlignment = TextAnchor.UpperLeft;
                    vlg.childForceExpandWidth = true;
                    vlg.childForceExpandHeight = false;
                    vlg.childControlWidth = true;
                    vlg.childControlHeight = true;
                }

                if (bossPoolContent.GetComponent<ContentSizeFitter>() == null)
                {
                    ContentSizeFitter csf = bossPoolContent.gameObject.AddComponent<ContentSizeFitter>();
                    csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
            }
        }

        /// <summary>
        /// 创建统计栏
        /// </summary>
        private void CreateStatsBar(Transform parent)
        {
            GameObject statsBar = new GameObject("StatsBar");
            statsBar.transform.SetParent(parent, false);
            RectTransform statsRect = statsBar.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0f, 0f);
            statsRect.anchorMax = new Vector2(1f, 0f);
            statsRect.pivot = new Vector2(0.5f, 0f);
            statsRect.anchoredPosition = new Vector2(0f, 55f);
            statsRect.sizeDelta = new Vector2(0f, 40f);

            // 统计文本
            GameObject statsTextObj = new GameObject("StatsText");
            statsTextObj.transform.SetParent(statsBar.transform, false);
            statsText = statsTextObj.AddComponent<TextMeshProUGUI>();
            statsText.fontSize = 18;
            statsText.alignment = TextAlignmentOptions.Center;
            statsText.color = Color.white;
            RectTransform statsTextRect = statsTextObj.GetComponent<RectTransform>();
            statsTextRect.anchorMin = Vector2.zero;
            statsTextRect.anchorMax = Vector2.one;
            statsTextRect.offsetMin = Vector2.zero;
            statsTextRect.offsetMax = Vector2.zero;

            UpdateStatsText();
        }

        /// <summary>
        /// 创建底部按钮
        /// </summary>
        private void CreateBottomButtons(Transform parent)
        {
            GameObject bottomBar = new GameObject("BottomBar");
            bottomBar.transform.SetParent(parent, false);
            HorizontalLayoutGroup hlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20f;
            hlg.padding = new RectOffset(10, 10, 5, 10);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            RectTransform bottomRect = bottomBar.GetComponent<RectTransform>();
            bottomRect.anchorMin = new Vector2(0f, 0f);
            bottomRect.anchorMax = new Vector2(1f, 0f);
            bottomRect.pivot = new Vector2(0.5f, 0f);
            bottomRect.anchoredPosition = new Vector2(0f, 0f);
            bottomRect.sizeDelta = new Vector2(0f, 55f);

            Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
            if (buttonPrefab != null)
            {
                // 保存并关闭按钮
                Button saveBtn = UnityEngine.Object.Instantiate(buttonPrefab, bottomBar.transform);
                LayoutElement le = saveBtn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 150f;
                le.preferredHeight = 40f;
                TextMeshProUGUI txt = saveBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = L10n.T("保存并关闭", "Save & Close");
                saveBtn.onClick.AddListener(() => {
                    SyncBossPoolToConfig();
                    CloseBossPoolWindow();
                });
            }
        }

        /// <summary>
        /// 填充 Boss 列表
        /// </summary>
        private void PopulateBossList()
        {
            if (bossPoolContent == null) return;

            // 清空现有内容
            bossToggles.Clear();
            foreach (Transform child in bossPoolContent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            if (enemyPresets == null || enemyPresets.Count == 0)
            {
                // 显示提示信息
                GameObject tipObj = new GameObject("Tip");
                tipObj.transform.SetParent(bossPoolContent, false);
                TextMeshProUGUI tipText = tipObj.AddComponent<TextMeshProUGUI>();
                tipText.text = L10n.T("暂无 Boss 数据，请先进入游戏", "No Boss data available, please enter the game first");
                tipText.fontSize = 16;
                tipText.alignment = TextAlignmentOptions.Center;
                tipText.color = new Color(0.7f, 0.7f, 0.7f);
                LayoutElement le = tipObj.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
                return;
            }

            // 为每个 Boss 创建 Toggle
            foreach (var preset in enemyPresets)
            {
                if (preset == null || string.IsNullOrEmpty(preset.name)) continue;

                CreateBossToggle(preset);
            }

            UpdateStatsText();
        }

        /// <summary>
        /// 创建单个 Boss Toggle
        /// </summary>
        private void CreateBossToggle(EnemyPresetInfo preset)
        {
            GameObject toggleObj = new GameObject("Toggle_" + preset.name);
            toggleObj.transform.SetParent(bossPoolContent, false);

            // 背景
            Image bgImage = toggleObj.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // 布局
            HorizontalLayoutGroup hlg = toggleObj.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(15, 15, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement toggleLE = toggleObj.AddComponent<LayoutElement>();
            toggleLE.preferredHeight = 40f;

            // Toggle 组件
            Toggle toggle = toggleObj.AddComponent<Toggle>();

            // Checkmark 背景
            GameObject checkBg = new GameObject("CheckBackground");
            checkBg.transform.SetParent(toggleObj.transform, false);
            Image checkBgImage = checkBg.AddComponent<Image>();
            checkBgImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            LayoutElement checkBgLE = checkBg.AddComponent<LayoutElement>();
            checkBgLE.preferredWidth = 24f;
            checkBgLE.preferredHeight = 24f;

            // Checkmark
            GameObject checkmark = new GameObject("Checkmark");
            checkmark.transform.SetParent(checkBg.transform, false);
            Image checkmarkImage = checkmark.AddComponent<Image>();
            checkmarkImage.color = new Color(0.3f, 0.8f, 0.3f, 1f);
            RectTransform checkmarkRect = checkmark.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.15f, 0.15f);
            checkmarkRect.anchorMax = new Vector2(0.85f, 0.85f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;

            toggle.graphic = checkmarkImage;
            toggle.targetGraphic = checkBgImage;

            // 标签文本
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(toggleObj.transform, false);
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            string displayName = !string.IsNullOrEmpty(preset.displayName) ? preset.displayName : preset.name;
            labelText.text = displayName;
            labelText.fontSize = 18;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = Color.white;
            LayoutElement labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1f;

            // 设置初始状态
            bool isEnabled = IsBossEnabled(preset.name);
            toggle.isOn = isEnabled;

            // 添加事件监听
            string bossName = preset.name;
            toggle.onValueChanged.AddListener((bool value) => {
                SetBossEnabled(bossName, value);
                UpdateStatsText();
            });

            bossToggles[preset.name] = toggle;
        }

        /// <summary>
        /// 更新统计文本
        /// </summary>
        private void UpdateStatsText()
        {
            if (statsText == null) return;

            int enabledCount = bossEnabledStates.Count(kv => kv.Value);
            int totalCount = bossEnabledStates.Count;

            string text = L10n.T("已启用: ", "Enabled: ") + enabledCount + "/" + totalCount;

            if (enabledCount == 0 && totalCount > 0)
            {
                text += "\n<color=#FF6666>" + L10n.T("警告：至少需要启用一个 Boss！", "Warning: At least one Boss must be enabled!") + "</color>";
            }

            statsText.text = text;
        }

        /// <summary>
        /// 刷新 Boss 池 UI
        /// </summary>
        private void RefreshBossPoolUI()
        {
            // 更新所有 Toggle 状态
            foreach (var kv in bossToggles)
            {
                if (kv.Value != null && bossEnabledStates.ContainsKey(kv.Key))
                {
                    kv.Value.isOn = bossEnabledStates[kv.Key];
                }
            }

            UpdateStatsText();
        }

        /// <summary>
        /// 检测 Boss 池窗口快捷键（在 Update 中调用）
        /// </summary>
        private void CheckBossPoolWindowHotkey()
        {
            // Ctrl+F10 打开/关闭 Boss 池配置窗口
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.F10))
                {
                    if (showBossPoolWindow)
                    {
                        CloseBossPoolWindow();
                    }
                    else
                    {
                        OpenBossPoolWindow();
                    }
                }
            }
        }

        /// <summary>
        /// LateUpdate 中强制暂停和鼠标状态（在所有 Update 之后执行）
        /// </summary>
        private void BossPoolLateUpdate()
        {
            if (showBossPoolWindow)
            {
                Time.timeScale = 0f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        /// <summary>
        /// 销毁 Boss 池 UI
        /// </summary>
        private void DestroyBossPoolUI()
        {
            if (bossPoolCanvas != null)
            {
                UnityEngine.Object.Destroy(bossPoolCanvas);
                bossPoolCanvas = null;
                bossPoolPanel = null;
                bossPoolContent = null;
                bossPoolScrollRect = null;
                bossToggles.Clear();
                statsText = null;
            }
        }

        #endregion
    }
}

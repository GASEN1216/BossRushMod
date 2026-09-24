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
//
// 2026-09-24 UI 共识对照审查 A-26…A-30、A-43：两个页签（出场 Boss / 无间炼狱因子），列表头操作跟着页签换；
//   「全部恢复默认」先确认；×、ESC、Ctrl+F10、「保存并关闭」走同一条「先保存再关」，一个 Boss 都没启用时不存不关、
//   原因写在统计行；打开时占模态租约（时停 + 光标 + 输入占用一处管）。
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

        /// <summary>是否处于无间炼狱因子编辑模式</summary>
        private bool isInfiniteHellFactorMode = false;

        /// <summary>无间炼狱因子等级定义</summary>
        private static readonly string[] factorLevelNames = { "极低", "低", "中", "高", "极高" };
        private static readonly string[] factorLevelNamesEn = { "Very Low", "Low", "Medium", "High", "Very High" };
        private static readonly float[] factorLevelValues = { 0.2f, 0.5f, 1.0f, 1.5f, 2.0f };

        /// <summary>Boss 无间炼狱刷新因子字典 (key: boss name, value: factor)</summary>
        private Dictionary<string, float> bossInfiniteHellFactors = new Dictionary<string, float>();

        /// <summary>工具栏按钮引用：两个页签（bossTabButton / infiniteHellFactorButton）+ 各页的列表头操作</summary>
        private Button selectAllButton = null;
        private Button deselectAllButton = null;
        private Button infiniteHellFactorButton = null;
        private Button bossTabButton = null;
        private Button resetFactorsButton = null;
        private ZombieModeUIHelper.ModalInputLease bossPoolModalLease = null;

        /// <summary>Boss 因子选择器 UI 字典</summary>
        private Dictionary<string, GameObject> bossFactorSelectors = new Dictionary<string, GameObject>();

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

                // 从配置中加载无间炼狱因子
                bossInfiniteHellFactors.Clear();
                if (config != null && config.bossInfiniteHellFactors != null)
                {
                    foreach (var kv in config.bossInfiniteHellFactors)
                    {
                        bossInfiniteHellFactors[kv.Key] = kv.Value;
                    }
                }
                // 为没有配置的 Boss 设置默认因子（中 = 1.0）
                foreach (var preset in enemyPresets)
                {
                    if (preset != null && !string.IsNullOrEmpty(preset.name) && !bossInfiniteHellFactors.ContainsKey(preset.name))
                    {
                        bossInfiniteHellFactors[preset.name] = 1.0f;
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

            // 血脉目录的资格来源就是这张过滤池，因此过滤一变目录必须跟着重建，
            // 否则玩家在场内改了 Boss 池之后，掉落资格仍停在旧快照上。
            // 这是唯一的咽喉点，覆盖初始化/单点开关/全开/全关/预设刷新五条路径。
            try
            {
                if (PetNestRuntime != null) PetNestRuntime.NotifyEnemyPresetsRefreshed();
                // 图鉴目录的展示池同样源自这张过滤池，过滤一变目录必须跟着重建，
                // 否则玩家在场内改了 Boss 池之后，图鉴条目仍停在旧快照上。
                if (CodexRuntime != null) CodexRuntime.NotifyEnemyPresetsRefreshed();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] Boss 池过滤变化后刷新玩法目录失败: " + e.Message);
            }
        }

        private void ResetBossPoolFilterStateForEnemyPresetRefresh()
        {
            bossPoolFilterInitialized = false;
            showBossPoolWindow = false;
            bossEnabledStates.Clear();
            bossInfiniteHellFactors.Clear();
            InvalidateFilteredPresetsCache();
            DestroyBossPoolUI();
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

                // 同步无间炼狱因子
                if (config.bossInfiniteHellFactors == null)
                {
                    config.bossInfiniteHellFactors = new Dictionary<string, float>();
                }
                else
                {
                    config.bossInfiniteHellFactors.Clear();
                }

                foreach (var kv in bossInfiniteHellFactors)
                {
                    // 只保存非默认值（非1.0）的因子
                    if (!Mathf.Approximately(kv.Value, 1.0f))
                    {
                        config.bossInfiniteHellFactors[kv.Key] = kv.Value;
                    }
                }

                SaveConfigToFile();
                DevLog("[BossRush] Boss 池配置已保存，禁用 " + config.disabledBosses.Count + " 个 Boss，自定义因子 " + config.bossInfiniteHellFactors.Count + " 个");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] SyncBossPoolToConfig 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 获取 Boss 的无间炼狱刷新因子
        /// </summary>
        public float GetBossInfiniteHellFactor(string bossName)
        {
            if (string.IsNullOrEmpty(bossName))
            {
                return 1.0f;
            }

            float factor;
            if (bossInfiniteHellFactors.TryGetValue(bossName, out factor))
            {
                return factor;
            }

            return 1.0f;
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

            // 创建或显示 UI；每次打开都从「出场 Boss」页开始（页签选中态、列表头操作、列表内容一起切）
            if (bossPoolCanvas == null)
            {
                CreateBossPoolUI();
            }
            else
            {
                bossPoolCanvas.SetActive(true);
            }
            ShowBossPoolTab(false);

            // 将滚动位置重置到顶部
            if (bossPoolScrollRect != null)
            {
                bossPoolScrollRect.verticalNormalizedPosition = 1f;
            }

            // 打开淡入微放大（UB-27：旧版一帧出现）；关闭见 CloseBossPoolWindow 的淡出
            if (!showBossPoolWindow) BossRushUI.PlayOpenAnimation(bossPoolPanel);

            // 模态租约（A-43）：时停、光标、输入占用与其它模态面板共用一个计数；ESC / 手柄取消 = 保存并关闭（A-26）
            if (bossPoolCanvas != null)
            {
                if (bossPoolModalLease == null) bossPoolModalLease = ZombieModeUIHelper.ClaimModalInput(bossPoolCanvas, "BossPool");
                PetNestCancelKey.Attach(bossPoolCanvas, SaveAndCloseBossPoolWindow, () => BossRushConfirmDialog.IsOpen);
            }

            showBossPoolWindow = true;
            DevLog("[BossRush] 打开 Boss 池配置窗口，当前 Boss 数量: " + (enemyPresets != null ? enemyPresets.Count : 0));
        }

        /// <summary>
        /// 关闭 Boss 池配置窗口
        /// </summary>
        public void CloseBossPoolWindow()
        {
            // 归还模态租约（在 ReleaseBossPoolUIReferences 里）；面板淡出后销毁（UB-32），引用立刻清空，下次打开重建一份
            if (bossPoolCanvas != null)
            {
                GameObject closing = bossPoolCanvas;
                ReleaseBossPoolUIReferences();
                BossRushUIKit.PlayCloseAndDestroy(closing);
            }

            showBossPoolWindow = false;
            DevLog("[BossRush] 关闭 Boss 池配置窗口");
        }

        /// <summary>
        /// 所有关闭入口（×、ESC、Ctrl+F10、「保存并关闭」）走同一条路（A-27）：先保存再关。旧版只有「保存并关闭」存盘，
        /// × 与 Ctrl+F10 只关窗不存也不回滚：本局按新设置刷怪、重启后又变回去，没有任何提示。
        /// 一个 Boss 都没启用时不存也不关（A-30）：竞技场会无怪可刷，原因写在统计行并闪一下。
        /// </summary>
        private void SaveAndCloseBossPoolWindow()
        {
            if (bossEnabledStates.Count > 0 && !bossEnabledStates.ContainsValue(true))
            {
                UpdateStatsText();
                if (statsText != null) BossRushUIEntranceAnimation.Play(statsText.gameObject, 0f, 0.25f, 6f);
                return;
            }
            SyncBossPoolToConfig();
            CloseBossPoolWindow();
        }

        /// <summary>
        /// 切换页签（A-28）：「出场 Boss」是开关列表 + 全选 / 全不选；「无间炼狱因子」是档位列表 + 全部恢复默认。
        /// 两张列表逐行对齐（同一批 Boss），切换时保住滚动位置（UB-27）。
        /// </summary>
        private void ShowBossPoolTab(bool factorTab)
        {
            isInfiniteHellFactorMode = factorTab;
            IntegrationUIFeedback.StyleSegment(bossTabButton, !factorTab);
            IntegrationUIFeedback.StyleSegment(infiniteHellFactorButton, factorTab);
            if (selectAllButton != null) selectAllButton.gameObject.SetActive(!factorTab);
            if (deselectAllButton != null) deselectAllButton.gameObject.SetActive(!factorTab);
            if (resetFactorsButton != null) resetFactorsButton.gameObject.SetActive(factorTab);

            float scroll = bossPoolScrollRect != null ? bossPoolScrollRect.verticalNormalizedPosition : 1f;
            if (factorTab) RefreshBossListForFactorMode();
            else PopulateBossList();
            RestoreBossPoolScroll(scroll);
        }

        /// <summary>「全部恢复默认」一次复位全部因子、不能撤销：先弹确认（A-28）。</summary>
        private void RequestResetAllBossFactors()
        {
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("把全部因子恢复成「中」？", "Reset every factor to Medium?"),
                Body = L10n.T("无间炼狱里每个 Boss 的出场频率都回到默认档。",
                    "Every Boss goes back to the default spawn weight in Infinite Hell."),
                Warning = L10n.T("你调过的档位会全部丢掉，不能撤销。", "All your adjustments are lost; this can't be undone."),
                ConfirmLabel = L10n.T("全部恢复默认", "Reset all"),
                Danger = true,
                OnConfirm = ResetAllBossFactors,
                Anchor = bossPoolPanel
            });
        }

        /// <summary>
        /// 重置所有 Boss 因子为默认值
        /// </summary>
        private void ResetAllBossFactors()
        {
            var keys = bossInfiniteHellFactors.Keys.ToList();
            foreach (string key in keys)
            {
                bossInfiniteHellFactors[key] = 1.0f;
            }
            RefreshBossListForFactorMode();
        }

        /// <summary>
        /// 刷新 Boss 列表为因子编辑模式
        /// </summary>
        private void RefreshBossListForFactorMode()
        {
            if (bossPoolContent == null) return;

            // 清空现有内容
            bossToggles.Clear();
            bossFactorSelectors.Clear();
            ClearBossPoolContent();

            if (enemyPresets == null || enemyPresets.Count == 0)
            {
                // 显示提示信息
                GameObject tipObj = new GameObject("Tip");
                tipObj.transform.SetParent(bossPoolContent, false);
                TextMeshProUGUI tipText = tipObj.AddComponent<TextMeshProUGUI>();
                BossRushUI.ApplyGameFont(tipText);
                tipText.text = L10n.T("暂无 Boss 数据，请先进入游戏", "No Boss data available, please enter the game first");
                tipText.fontSize = 16;
                tipText.alignment = TextAlignmentOptions.Center;
                tipText.color = BossRushUIColors.TextSecondary;
                LayoutElement le = tipObj.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
                return;
            }

            // 为每个 Boss 创建因子选择器
            foreach (var preset in enemyPresets)
            {
                if (preset == null || string.IsNullOrEmpty(preset.name)) continue;
                CreateBossFactorSelector(preset);
            }

            UpdateStatsTextForFactorMode();
        }

        /// <summary>
        /// 创建单个 Boss 因子选择器
        /// </summary>
        private void CreateBossFactorSelector(EnemyPresetInfo preset)
        {
            GameObject selectorObj = new GameObject("FactorSelector_" + preset.name);
            selectorObj.transform.SetParent(bossPoolContent, false);

            // 背景：与开关列表同一种行底（UB-26）
            AddBossPoolRowBackground(selectorObj);

            // 布局
            HorizontalLayoutGroup hlg = selectorObj.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(15, 15, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement selectorLE = selectorObj.AddComponent<LayoutElement>();
            selectorLE.preferredHeight = 40f;

            // Boss 名称标签
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(selectorObj.transform, false);
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(labelText);
            string displayName = !string.IsNullOrEmpty(preset.displayName) ? preset.displayName : preset.name;
            labelText.text = displayName;
            labelText.fontSize = 18;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = BossRushUIColors.TextPrimary;
            LayoutElement labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1f;

            // 获取当前因子
            float currentFactor = 1.0f;
            if (bossInfiniteHellFactors.ContainsKey(preset.name))
            {
                currentFactor = bossInfiniteHellFactors[preset.name];
            }
            int currentIndex = GetFactorLevelIndex(currentFactor);

            // 因子选择器容器
            GameObject factorContainer = new GameObject("FactorContainer");
            factorContainer.transform.SetParent(selectorObj.transform, false);
            HorizontalLayoutGroup factorHlg = factorContainer.AddComponent<HorizontalLayoutGroup>();
            factorHlg.spacing = 5f;
            factorHlg.childAlignment = TextAnchor.MiddleCenter;
            factorHlg.childForceExpandWidth = false;
            factorHlg.childForceExpandHeight = false;
            LayoutElement factorContainerLE = factorContainer.AddComponent<LayoutElement>();
            factorContainerLE.preferredWidth = 180f;

            Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
            string bossName = preset.name;

            // 左箭头按钮 <
            if (buttonPrefab != null)
            {
                Button leftBtn = UnityEngine.Object.Instantiate(buttonPrefab, factorContainer.transform);
                LayoutElement leftLE = leftBtn.gameObject.AddComponent<LayoutElement>();
                leftLE.preferredWidth = 30f;
                leftLE.preferredHeight = 30f;
                TextMeshProUGUI leftTxt = leftBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (leftTxt != null) leftTxt.text = "<";
                leftBtn.onClick.AddListener(() => DecreaseBossFactor(bossName));
            }

            // 因子等级文本
            GameObject factorTextObj = new GameObject("FactorText");
            factorTextObj.transform.SetParent(factorContainer.transform, false);
            TextMeshProUGUI factorText = factorTextObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(factorText);
            factorText.text = GetFactorLevelDisplayText(currentIndex);
            factorText.fontSize = 16;
            factorText.alignment = TextAlignmentOptions.Center;
            factorText.color = GetFactorLevelColor(currentIndex);
            LayoutElement factorTextLE = factorTextObj.AddComponent<LayoutElement>();
            factorTextLE.preferredWidth = 100f;

            // 右箭头按钮 >
            if (buttonPrefab != null)
            {
                Button rightBtn = UnityEngine.Object.Instantiate(buttonPrefab, factorContainer.transform);
                LayoutElement rightLE = rightBtn.gameObject.AddComponent<LayoutElement>();
                rightLE.preferredWidth = 30f;
                rightLE.preferredHeight = 30f;
                TextMeshProUGUI rightTxt = rightBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (rightTxt != null) rightTxt.text = ">";
                rightBtn.onClick.AddListener(() => IncreaseBossFactor(bossName));
            }

            bossFactorSelectors[preset.name] = selectorObj;
        }

        /// <summary>
        /// 获取因子等级索引
        /// </summary>
        private int GetFactorLevelIndex(float factor)
        {
            for (int i = 0; i < factorLevelValues.Length; i++)
            {
                if (Mathf.Approximately(factor, factorLevelValues[i]))
                {
                    return i;
                }
            }
            // 默认返回中等
            return 2;
        }

        /// <summary>
        /// 获取因子等级显示文本
        /// </summary>
        private string GetFactorLevelDisplayText(int index)
        {
            if (index < 0 || index >= factorLevelNames.Length)
            {
                index = 2;
            }
            string levelName = L10n.T(factorLevelNames[index], factorLevelNamesEn[index]);
            return levelName;
        }

        /// <summary>
        /// 获取因子等级颜色
        /// </summary>
        private Color GetFactorLevelColor(int index)
        {
            // 五档走 token（UB-26：旧版是另一套手写的灰 / 绿 / 白 / 橙 / 红）
            switch (index)
            {
                case 0: return BossRushUIColors.TextSecondary; // 极低
                case 1: return BossRushUIColors.SuccessText;   // 低
                case 2: return BossRushUIColors.TextPrimary;   // 中
                case 3: return BossRushUIColors.WarningText;   // 高
                case 4: return BossRushUIColors.DangerText;    // 极高
                default: return BossRushUIColors.TextPrimary;
            }
        }

        /// <summary>
        /// 减少 Boss 因子
        /// </summary>
        private void DecreaseBossFactor(string bossName)
        {
            if (!bossInfiniteHellFactors.ContainsKey(bossName)) return;

            float currentFactor = bossInfiniteHellFactors[bossName];
            int currentIndex = GetFactorLevelIndex(currentFactor);
            if (currentIndex > 0)
            {
                bossInfiniteHellFactors[bossName] = factorLevelValues[currentIndex - 1];
                UpdateBossFactorDisplay(bossName);
            }
        }

        /// <summary>
        /// 增加 Boss 因子
        /// </summary>
        private void IncreaseBossFactor(string bossName)
        {
            if (!bossInfiniteHellFactors.ContainsKey(bossName)) return;

            float currentFactor = bossInfiniteHellFactors[bossName];
            int currentIndex = GetFactorLevelIndex(currentFactor);
            if (currentIndex < factorLevelValues.Length - 1)
            {
                bossInfiniteHellFactors[bossName] = factorLevelValues[currentIndex + 1];
                UpdateBossFactorDisplay(bossName);
            }
        }

        /// <summary>
        /// 更新单个 Boss 因子显示
        /// </summary>
        private void UpdateBossFactorDisplay(string bossName)
        {
            if (!bossFactorSelectors.ContainsKey(bossName)) return;

            GameObject selectorObj = bossFactorSelectors[bossName];
            if (selectorObj == null) return;

            // 找到因子文本组件
            Transform factorContainer = selectorObj.transform.Find("FactorContainer");
            if (factorContainer == null) return;

            Transform factorTextTransform = factorContainer.Find("FactorText");
            if (factorTextTransform == null) return;

            TextMeshProUGUI factorText = factorTextTransform.GetComponent<TextMeshProUGUI>();
            if (factorText == null) return;

            float currentFactor = bossInfiniteHellFactors[bossName];
            int currentIndex = GetFactorLevelIndex(currentFactor);
            factorText.text = GetFactorLevelDisplayText(currentIndex);
            factorText.color = GetFactorLevelColor(currentIndex);
        }

        /// <summary>
        /// 更新因子模式下的统计文本
        /// </summary>
        private void UpdateStatsTextForFactorMode()
        {
            if (statsText == null) return;

            // 因子模式下不显示统计文本
            statsText.text = "";
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
                vpImage.color = BossRushUIColors.Surface;
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

            // 滚动手感走共享口径（UB-27：旧版灵敏度 0.5，滚轮一格几乎不动）：纵向、Clamped、灵敏度 32、空白处也能滚
            BossRushUI.ConfigureScrollRect(scrollRect);
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
            BossRushUI.ApplyGameFont(statsText);
            statsText.fontSize = 18;
            statsText.alignment = TextAlignmentOptions.Center;
            statsText.color = BossRushUIColors.TextSecondary;
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
            hlg.padding = new RectOffset(10, 16, 5, 10);
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            RectTransform bottomRect = bottomBar.GetComponent<RectTransform>();
            bottomRect.anchorMin = new Vector2(0f, 0f);
            bottomRect.anchorMax = new Vector2(1f, 0f);
            bottomRect.pivot = new Vector2(0.5f, 0f);
            bottomRect.anchoredPosition = new Vector2(0f, 0f);
            bottomRect.sizeDelta = new Vector2(0f, 55f);

            // 这一屏唯一的主操作：AccentFill、放最右（A-29）。×、ESC、Ctrl+F10 也走同一条「先保存再关」（A-27）
            Button saveBtn = ZombieModeUIHelper.CreateButton("SaveAndClose", bottomBar.transform, L10n.T("保存并关闭", "Save & Close"),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 40f), BossRushUIColors.AccentFill, 18f,
                new Vector2(140f, 36f), SaveAndCloseBossPoolWindow, true);
            LayoutElement le = saveBtn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 150f;
            le.preferredHeight = 40f;
        }

        /// <summary>
        /// 填充 Boss 列表
        /// </summary>
        private void PopulateBossList()
        {
            if (bossPoolContent == null) return;

            // 清空现有内容
            bossToggles.Clear();
            bossFactorSelectors.Clear();
            ClearBossPoolContent();

            if (enemyPresets == null || enemyPresets.Count == 0)
            {
                // 显示提示信息
                GameObject tipObj = new GameObject("Tip");
                tipObj.transform.SetParent(bossPoolContent, false);
                TextMeshProUGUI tipText = tipObj.AddComponent<TextMeshProUGUI>();
                BossRushUI.ApplyGameFont(tipText);
                tipText.text = L10n.T("暂无 Boss 数据，请先进入游戏", "No Boss data available, please enter the game first");
                tipText.fontSize = 16;
                tipText.alignment = TextAlignmentOptions.Center;
                tipText.color = BossRushUIColors.TextSecondary;
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

            // 背景：Card 档圆角 + SurfaceRaised（旧版无 sprite 的直角灰条，UB-26）；整行可点，悬停 / 按下由行底三态给
            Image bgImage = AddBossPoolRowBackground(toggleObj);

            // 布局
            HorizontalLayoutGroup hlg = toggleObj.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(15, 15, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement toggleLE = toggleObj.AddComponent<LayoutElement>();
            toggleLE.preferredHeight = 40f;

            // Toggle 组件：targetGraphic 是整行底图，悬停整行提亮（旧版只有 24px 的小方块有反馈）
            Toggle toggle = toggleObj.AddComponent<Toggle>();
            ApplyBossPoolRowColors(toggle, bgImage);

            // 勾选框：圆角描边小框（比行底深一档，读起来是「凹进去的格子」）
            GameObject checkBg = new GameObject("CheckBackground");
            checkBg.transform.SetParent(toggleObj.transform, false);
            Image checkBgImage = checkBg.AddComponent<Image>();
            checkBgImage.color = BossRushUIColors.Surface;
            checkBgImage.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(checkBgImage, 4, BossRushUISkinPart.Button);
            LayoutElement checkBgLE = checkBg.AddComponent<LayoutElement>();
            checkBgLE.preferredWidth = 24f;
            checkBgLE.preferredHeight = 24f;

            // 勾：「√」字（GBK 有字形），不再是一块绿色实心方块（UB-26）
            GameObject checkmark = new GameObject("Checkmark");
            checkmark.transform.SetParent(checkBg.transform, false);
            TextMeshProUGUI checkmarkText = checkmark.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(checkmarkText);
            checkmarkText.text = "√";
            checkmarkText.fontSize = 18;
            checkmarkText.alignment = TextAlignmentOptions.Center;
            checkmarkText.color = BossRushUIColors.SuccessText;
            checkmarkText.raycastTarget = false;
            RectTransform checkmarkRect = checkmark.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;

            toggle.graphic = checkmarkText;

            // 标签文本
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(toggleObj.transform, false);
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(labelText);
            string displayName = !string.IsNullOrEmpty(preset.displayName) ? preset.displayName : preset.name;
            labelText.text = displayName;
            labelText.fontSize = 18;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = BossRushUIColors.TextPrimary;
            labelText.raycastTarget = false;
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
                text += "\n<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">"
                    + L10n.T("一个 Boss 都没选：至少启用一个才能保存关闭", "No Boss enabled: enable at least one to save and close") + "</color>";
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
                        SaveAndCloseBossPoolWindow();
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
                ReleaseBossPoolUIReferences();
            }
        }

        #endregion
    }
}

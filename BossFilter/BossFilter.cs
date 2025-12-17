// ============================================================================
// BossFilter.cs - Boss 池筛选模块
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的 Boss 池筛选功能，包括：
//   - Boss 启用/禁用状态管理
//   - IMGUI 配置窗口
//   - 配置持久化（集成到 BossRushModConfig.txt）
//   
// 快捷键：
//   - Ctrl+F10: 打开/关闭 Boss 池配置窗口
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

        /// <summary>Boss 池配置窗口滚动位置</summary>
        private Vector2 bossPoolScrollPosition = Vector2.zero;

        /// <summary>Boss 池配置窗口矩形</summary>
        private Rect bossPoolWindowRect = new Rect(100, 100, 420, 500);

        /// <summary>Boss 池配置窗口 ID</summary>
        private const int BossPoolWindowId = 19527;

        /// <summary>Boss 池筛选是否已初始化</summary>
        private bool bossPoolFilterInitialized = false;

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

                int enabledCount = bossEnabledStates.Count(kv => kv.Value);
                int totalCount = bossEnabledStates.Count;
                DevLog("[BossRush] Boss 池筛选初始化完成，已启用 " + enabledCount + "/" + totalCount + " 个 Boss");
            }
            catch (Exception ex)
            {
                Debug.LogError("[BossRush] InitializeBossPoolFilter 失败: " + ex.Message);
            }
        }

        #endregion

        #region Boss 启用状态管理

        /// <summary>
        /// 检查指定 Boss 是否启用
        /// </summary>
        /// <param name="bossName">Boss 内部名称</param>
        /// <returns>是否启用，未找到时返回 true（默认启用）</returns>
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

            // 未找到的 Boss 默认启用
            return true;
        }

        /// <summary>
        /// 设置 Boss 启用状态
        /// </summary>
        /// <param name="bossName">Boss 内部名称</param>
        /// <param name="enabled">是否启用</param>
        public void SetBossEnabled(string bossName, bool enabled)
        {
            if (string.IsNullOrEmpty(bossName))
            {
                return;
            }

            bossEnabledStates[bossName] = enabled;
        }

        /// <summary>
        /// 获取过滤后的 Boss 列表
        /// </summary>
        /// <returns>只包含已启用 Boss 的列表</returns>
        public List<EnemyPresetInfo> GetFilteredEnemyPresets()
        {
            if (enemyPresets == null)
            {
                return new List<EnemyPresetInfo>();
            }

            return enemyPresets.Where(preset => 
                preset != null && 
                !string.IsNullOrEmpty(preset.name) && 
                IsBossEnabled(preset.name)
            ).ToList();
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

                // 只保存被禁用的 Boss
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
                Debug.LogError("[BossRush] SyncBossPoolToConfig 失败: " + ex.Message);
            }
        }

        #endregion

        #region IMGUI 窗口

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

            showBossPoolWindow = true;
            DevLog("[BossRush] 打开 Boss 池配置窗口，当前 Boss 数量: " + (enemyPresets != null ? enemyPresets.Count : 0));
        }

        /// <summary>
        /// 关闭 Boss 池配置窗口
        /// </summary>
        public void CloseBossPoolWindow()
        {
            showBossPoolWindow = false;
            DevLog("[BossRush] 关闭 Boss 池配置窗口");
        }

        /// <summary>
        /// 绘制 Boss 池配置窗口（在 OnGUI 中调用）
        /// </summary>
        private void DrawBossPoolWindow()
        {
            if (!showBossPoolWindow)
            {
                return;
            }

            // 确保窗口在屏幕范围内
            bossPoolWindowRect.x = Mathf.Clamp(bossPoolWindowRect.x, 0, Screen.width - bossPoolWindowRect.width);
            bossPoolWindowRect.y = Mathf.Clamp(bossPoolWindowRect.y, 0, Screen.height - bossPoolWindowRect.height);

            bossPoolWindowRect = GUI.Window(BossPoolWindowId, bossPoolWindowRect, DrawBossPoolWindowContent, L10n.T("Boss池设置 (Ctrl+F10)", "Boss Pool Settings (Ctrl+F10)"));
        }

        /// <summary>
        /// 绘制 Boss 池配置窗口内容
        /// </summary>
        private void DrawBossPoolWindowContent(int windowId)
        {
            // 关闭按钮
            if (GUI.Button(new Rect(bossPoolWindowRect.width - 25, 5, 20, 20), "X"))
            {
                CloseBossPoolWindow();
                return;
            }

            GUILayout.BeginVertical();

            // 工具栏：全选/全不选
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("全选", "Select All"), GUILayout.Width(80)))
            {
                EnableAllBosses();
            }
            if (GUILayout.Button(L10n.T("全不选", "Deselect All"), GUILayout.Width(80)))
            {
                DisableAllBosses();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // 滚动列表
            bossPoolScrollPosition = GUILayout.BeginScrollView(bossPoolScrollPosition, GUILayout.Height(350));

            if (enemyPresets != null && enemyPresets.Count > 0)
            {
                foreach (var preset in enemyPresets)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.name))
                    {
                        continue;
                    }

                    bool currentEnabled = IsBossEnabled(preset.name);
                    string displayName = !string.IsNullOrEmpty(preset.displayName) ? preset.displayName : preset.name;
                    
                    bool newEnabled = GUILayout.Toggle(currentEnabled, " " + displayName);
                    if (newEnabled != currentEnabled)
                    {
                        SetBossEnabled(preset.name, newEnabled);
                    }
                }
            }
            else
            {
                GUILayout.Label(L10n.T("暂无 Boss 数据，请先进入游戏", "No Boss data available, please enter the game first"));
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);

            // 统计信息
            int enabledCount = bossEnabledStates.Count(kv => kv.Value);
            int totalCount = bossEnabledStates.Count;
            GUILayout.Label(L10n.T("已启用: ", "Enabled: ") + enabledCount + "/" + totalCount);

            // 警告：如果全部禁用
            if (enabledCount == 0 && totalCount > 0)
            {
                GUI.color = Color.red;
                GUILayout.Label(L10n.T("警告：至少需要启用一个 Boss！", "Warning: At least one Boss must be enabled!"));
                GUI.color = Color.white;
            }

            GUILayout.Space(10);

            // 保存并关闭按钮
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L10n.T("保存并关闭", "Save & Close"), GUILayout.Width(120)))
            {
                SyncBossPoolToConfig();
                CloseBossPoolWindow();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            // 允许拖动窗口
            GUI.DragWindow(new Rect(0, 0, bossPoolWindowRect.width - 30, 25));
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

        #endregion
    }
}

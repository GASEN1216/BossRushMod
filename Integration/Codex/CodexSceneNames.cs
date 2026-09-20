// ============================================================================
// CodexSceneNames.cs - 图鉴「初见场景」的记录与显示名解析
// ============================================================================
// owner 2026-09-20：详情页的「初见模式」改成「初见场景」，而且**不能**把
// Zeroarea_basic_01 这种裸场景名直接摆给玩家，必须走官方语言本地化。
//
// 两件事分开：
//   1) Capture()  —— 击杀那一刻记录**场景 id**（"Level_GroundZero_Main" 这种）。
//      优先复用 MapPointSceneResolver 解析实际子场景的官方 id；
//      未取到时再回落 MultiSceneCore.MainSceneID 和活动场景名。
//   2) Resolve()  —— 显示时把它翻成当前语言的地图名：
//      SceneInfoCollection.GetSceneInfo(id).DisplayName 是官方地图选择界面用的同一份，
//      因此玩家看到的字眼与官方界面一致；官方查不到时再试我们自己的地图配置表
//      （BossRush 的 9 张图有中英双语名），最后才认输返回空串。
//
// 硬约束：
//   - 全程 no-throw：官方数据没就绪时任何一步都可能抛，图鉴不能因此打不开；
//   - Resolve 结果按 (场景 id, 语言) 缓存，详情页每次开只查一次；
//   - **绝不返回裸场景 id**：解析不出来返回 null，由调用方决定回落到什么。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>初见场景的记录与显示名解析。静态、no-throw、带语言感知缓存。</summary>
    internal static class CodexSceneNames
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, string> _cache =
            new Dictionary<string, string>(16, StringComparer.Ordinal);
        private static bool _cachedChinese;
        private static bool _cachedLanguageKnown;

        /// <summary>
        /// 记录用的场景 id。取不到返回空串（调用方照常写档，只是这条没有场景信息）。
        /// </summary>
        internal static string Capture()
        {
            // 竞技场和官方多场景地图以实际战斗子场景为准，不能把所有子图记成同一主场景。
            string activeSceneId = MapPointSceneResolver.Resolve();
            if (!string.IsNullOrEmpty(activeSceneId)) return activeSceneId;
            try
            {
                string mainSceneId = Duckov.Scenes.MultiSceneCore.MainSceneID;
                if (!string.IsNullOrEmpty(mainSceneId)) return mainSceneId;
            }
            catch (Exception)
            {
                // MainSceneID 内部会解 Scene?.Value，多场景系统没就绪时会抛：按缺失处理
            }

            try
            {
                string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                return string.IsNullOrEmpty(active) ? string.Empty : active;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 场景 id → 当前语言的地图名。解析不出来返回 null（**不返回裸 id**）。
        /// </summary>
        internal static string Resolve(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return null;

            bool chinese = IsChineseSafe();
            lock (_lock)
            {
                // 玩家能在游戏里切语言：语言一变整张缓存作废
                if (!_cachedLanguageKnown || _cachedChinese != chinese)
                {
                    _cache.Clear();
                    _cachedChinese = chinese;
                    _cachedLanguageKnown = true;
                }

                string cached;
                if (_cache.TryGetValue(sceneId, out cached)) return cached;
            }

            string resolved = ResolveUncached(sceneId);
            lock (_lock)
            {
                // 数据/本地化尚未就绪时保留重试机会，不能把缺失缓存到下次切语言。
                if (!string.IsNullOrEmpty(resolved)) _cache[sceneId] = resolved;
            }
            return resolved;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _cache.Clear();
                _cachedLanguageKnown = false;
            }
        }

        private static string ResolveUncached(string sceneId)
        {
            // 1) 官方场景表：与官方地图选择界面同一份显示名
            try
            {
                SceneInfoEntry info = SceneInfoCollection.GetSceneInfo(sceneId);
                if (info != null)
                {
                    string display = info.DisplayName;
                    // 官方在 displayName 为空时返回 id 本身，那等于没解析出来
                    if (!string.IsNullOrEmpty(display)
                        && !string.Equals(display, sceneId, StringComparison.Ordinal)
                        && display.IndexOf('*') < 0)
                    {
                        return display;
                    }
                }
            }
            catch (Exception)
            {
                // 官方数据没就绪：继续下一级
            }

            // 2) 我们自己的地图配置表（BossRush 的 9 张图，中英双语）。
            //    sceneID（主场景）与 sceneName（子场景）都认，老档里两者都可能出现。
            try
            {
                BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
                if (configs != null)
                {
                    for (int i = 0; i < configs.Length; i++)
                    {
                        BossRushMapConfig config = configs[i];
                        if (config == null) continue;
                        if (string.Equals(config.sceneID, sceneId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(config.sceneName, sceneId, StringComparison.OrdinalIgnoreCase))
                        {
                            string display = config.displayName;
                            if (!string.IsNullOrEmpty(display)) return display;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 地图表没就绪：继续下一级
            }

            // 3) 直接当本地化 key 试一次（自定义场景可能注入过）
            try
            {
                string localized = LocalizationHelper.GetLocalizedText(sceneId);
                if (!string.IsNullOrEmpty(localized)
                    && !string.Equals(localized, sceneId, StringComparison.Ordinal)
                    && localized.IndexOf('*') < 0)
                {
                    return localized;
                }
            }
            catch (Exception)
            {
                // 本地化表没就绪
            }

            // 解析不出来就认输：绝不把裸场景 id 摆给玩家
            return null;
        }

        private static bool IsChineseSafe()
        {
            try
            {
                return L10n.IsChinese;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}

// ============================================================================
// NPCAssetBundleHelper.cs - NPC AssetBundle 加载通用辅助
// ============================================================================
// 模块说明：
//   提取 GoblinNPC 和 NurseNPC 中重复的 AssetBundle 加载逻辑为通用方法。
//   新增 NPC 只需调用 LoadNPCPrefab() 即可完成加载。
// ============================================================================

using System;
using System.IO;
using UnityEngine;

namespace BossRush.Utils
{
    /// <summary>
    /// NPC AssetBundle 加载通用辅助
    /// </summary>
    public static class NPCAssetBundleHelper
    {
        /// <summary>
        /// 加载NPC预制体（通用流程：路径拼接 → 加载Bundle → 查找预制体 → Animator检查）
        /// </summary>
        /// <param name="bundleFileName">Assets/npcs/ 下的文件名，如 "goblinnpc"</param>
        /// <param name="prefabName">预制体名称，如 "GoblinNPC"</param>
        /// <param name="logPrefix">日志前缀，如 "[GoblinNPC]"</param>
        /// <param name="cachedBundle">缓存的 AssetBundle 引用（ref）</param>
        /// <param name="cachedPrefab">缓存的预制体引用（ref）</param>
        /// <returns>是否加载成功</returns>
        public static bool LoadNPCPrefab(
            string bundleFileName,
            string prefabName,
            string logPrefix,
            ref AssetBundle cachedBundle,
            ref GameObject cachedPrefab)
        {
            if (cachedPrefab != null)
            {
                ModBehaviour.DevLog(logPrefix + " 预制体已缓存，跳过加载");
                return true;
            }

            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "npcs", bundleFileName);

                ModBehaviour.DevLog(logPrefix + " 尝试加载 AssetBundle: " + bundlePath);

                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog(logPrefix + " 错误：未找到 AssetBundle 文件: " + bundlePath);
                    return false;
                }

                // 如果之前加载过但预制体为空，先卸载
                if (cachedBundle != null)
                {
                    cachedBundle.Unload(false);
                    cachedBundle = null;
                }

                cachedBundle = AssetBundle.LoadFromFile(bundlePath);
                if (cachedBundle == null)
                {
                    ModBehaviour.DevLog(logPrefix + " 错误：加载 AssetBundle 失败（可能已被加载或文件损坏）: " + bundlePath);
                    return false;
                }

                ModBehaviour.DevLog(logPrefix + " AssetBundle 加载成功，开始查找预制体...");

                // 列出所有资源名称用于调试
                string[] assetNames = cachedBundle.GetAllAssetNames();
                ModBehaviour.DevLog(logPrefix + " AssetBundle 包含 " + assetNames.Length + " 个资源:");
                foreach (string name in assetNames)
                {
                    ModBehaviour.DevLog(logPrefix + "   - " + name);
                }

                // 尝试加载指定名称的预制体
                cachedPrefab = cachedBundle.LoadAsset<GameObject>(prefabName);

                // 如果没找到，尝试小写名称
                if (cachedPrefab == null)
                {
                    ModBehaviour.DevLog(logPrefix + " 未找到 '" + prefabName + "'，尝试小写名称...");
                    cachedPrefab = cachedBundle.LoadAsset<GameObject>(prefabName.ToLowerInvariant());
                }

                // 如果还是没找到，加载第一个 GameObject
                if (cachedPrefab == null)
                {
                    ModBehaviour.DevLog(logPrefix + " 尝试加载所有 GameObject...");
                    GameObject[] allPrefabs = cachedBundle.LoadAllAssets<GameObject>();
                    if (allPrefabs != null && allPrefabs.Length > 0)
                    {
                        cachedPrefab = allPrefabs[0];
                        ModBehaviour.DevLog(logPrefix + " 使用第一个 GameObject: " + cachedPrefab.name);
                    }
                }

                if (cachedPrefab == null)
                {
                    ModBehaviour.DevLog(logPrefix + " 错误：AssetBundle 中未找到任何 GameObject 预制体");
                    return false;
                }

                // 检查预制体的 Animator 组件
                ModBehaviour.DevLog(logPrefix + " 成功加载预制体: " + cachedPrefab.name);
                Animator animator = cachedPrefab.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    ModBehaviour.DevLog(logPrefix + " 预制体包含 Animator 组件");
                    if (animator.runtimeAnimatorController != null)
                    {
                        ModBehaviour.DevLog(logPrefix + " Animator Controller: " + animator.runtimeAnimatorController.name);
                    }
                    else
                    {
                        ModBehaviour.DevLog(logPrefix + " 警告：Animator 没有 Controller！动画可能无法播放");
                    }
                }
                else
                {
                    ModBehaviour.DevLog(logPrefix + " 警告：预制体没有 Animator 组件！");
                }

                return true;
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, logPrefix + " LoadNPCPrefab");
                return false;
            }
        }
    }
}

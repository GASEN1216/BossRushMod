// ============================================================================
// GoblinNPC.cs - 哥布林NPC系统（ModBehaviour partial）
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的哥布林 NPC，包括：
//   - 从 AssetBundle 加载哥布林模型
//   - 生成和销毁哥布林实例
//   - 召唤哥布林跑向玩家
//   
//   注意：GoblinNPCController 和 GoblinMovement 类已拆分到独立文件
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 哥布林NPC系统 - ModBehaviour 的 partial class
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 哥布林实例和资源
        // ============================================================================
        
        // 哥布林实例
        private GameObject goblinNPCInstance = null;
        private GoblinNPCController goblinController = null;
        
        // AssetBundle 缓存
        private static AssetBundle goblinAssetBundle = null;
        private static GameObject goblinPrefab = null;
        
        /// <summary>
        /// 加载哥布林 AssetBundle
        /// </summary>
        private bool LoadGoblinAssetBundle()
        {
            if (goblinPrefab != null)
            {
                DevLog("[GoblinNPC] 预制体已缓存，跳过加载");
                return true;
            }
            
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = System.IO.Path.GetDirectoryName(assemblyLocation);
                string bundlePath = System.IO.Path.Combine(modDir, "Assets", "npcs", "goblinnpc");
                
                DevLog("[GoblinNPC] 尝试加载 AssetBundle: " + bundlePath);
                
                if (!File.Exists(bundlePath))
                {
                    DevLog("[GoblinNPC] 错误：未找到 goblinnpc AssetBundle 文件: " + bundlePath);
                    return false;
                }
                
                // 如果之前加载过但预制体为空，先卸载
                if (goblinAssetBundle != null)
                {
                    goblinAssetBundle.Unload(false);
                    goblinAssetBundle = null;
                }
                
                // 直接使用 AssetBundle API 加载
                goblinAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                if (goblinAssetBundle == null)
                {
                    DevLog("[GoblinNPC] 错误：加载 AssetBundle 失败（可能已被加载或文件损坏）: " + bundlePath);
                    return false;
                }
                
                DevLog("[GoblinNPC] AssetBundle 加载成功，开始查找预制体...");
                
                // 列出所有资源名称用于调试
                string[] assetNames = goblinAssetBundle.GetAllAssetNames();
                DevLog("[GoblinNPC] AssetBundle 包含 " + assetNames.Length + " 个资源:");
                foreach (string name in assetNames)
                {
                    DevLog("[GoblinNPC]   - " + name);
                }
                
                // 尝试加载名为 GoblinNPC 的预制体
                goblinPrefab = goblinAssetBundle.LoadAsset<GameObject>("GoblinNPC");
                
                // 如果没找到，尝试其他常见名称
                if (goblinPrefab == null)
                {
                    DevLog("[GoblinNPC] 未找到 'GoblinNPC'，尝试其他名称...");
                    goblinPrefab = goblinAssetBundle.LoadAsset<GameObject>("goblinnpc");
                }
                
                // 如果还是没找到，加载第一个 GameObject
                if (goblinPrefab == null)
                {
                    DevLog("[GoblinNPC] 尝试加载所有 GameObject...");
                    GameObject[] allPrefabs = goblinAssetBundle.LoadAllAssets<GameObject>();
                    if (allPrefabs != null && allPrefabs.Length > 0)
                    {
                        goblinPrefab = allPrefabs[0];
                        DevLog("[GoblinNPC] 使用第一个 GameObject: " + goblinPrefab.name);
                    }
                }
                
                if (goblinPrefab == null)
                {
                    DevLog("[GoblinNPC] 错误：AssetBundle 中未找到任何 GameObject 预制体");
                    return false;
                }
                
                // 检查预制体的组件
                DevLog("[GoblinNPC] 成功加载哥布林预制体: " + goblinPrefab.name);
                Animator animator = goblinPrefab.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    DevLog("[GoblinNPC] 预制体包含 Animator 组件");
                    if (animator.runtimeAnimatorController != null)
                    {
                        DevLog("[GoblinNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    }
                    else
                    {
                        DevLog("[GoblinNPC] 警告：Animator 没有 Controller！动画可能无法播放");
                    }
                }
                else
                {
                    DevLog("[GoblinNPC] 警告：预制体没有 Animator 组件！");
                }
                
                return true;
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "ModBehaviour.LoadGoblinAssetBundle");
                return false;
            }
        }
        
        // ============================================================================
        // 刷新位置辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取哥布林刷新位置（避开快递员位置）
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetGoblinSpawnPosition(string sceneName)
        {
            Vector3 courierPosition = Vector3.zero;
            
            // 获取快递员位置（如果存在）
            NPCExceptionHandler.TryExecute(() =>
            {
                if (courierNPCInstance != null)
                {
                    courierPosition = courierNPCInstance.transform.position;
                    DevLog("[GoblinNPC] 检测到快递员位置: " + courierPosition + "，将避开此位置");
                }
            }, "ModBehaviour.GetGoblinSpawnPosition - 获取快递员位置");
            
            // 从配置中查询哥布林刷新位置（传入快递员位置以避免重复）
            if (NPCSpawnConfig.TryGetGoblinSpawnPosition(sceneName, out Vector3 position, courierPosition, 10f))
            {
                if (courierPosition != Vector3.zero)
                {
                    float distance = Vector3.Distance(position, courierPosition);
                    DevLog("[GoblinNPC] 刷新位置与快递员距离: " + distance.ToString("F1") + "米");
                }
                return position;
            }
            
            // 未配置的场景使用随机刷新点
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                DevLog("[GoblinNPC] 随机刷新点 [" + randomIndex + "/" + spawnPoints.Length + "]");
                return spawnPoints[randomIndex];
            }
            return Vector3.zero;
        }
        
        /// <summary>
        /// 检查当前场景是否应该生成哥布林
        /// </summary>
        private bool ShouldSpawnGoblin(string sceneName)
        {
            return NPCSpawnConfig.HasGoblinConfig(sceneName);
        }
        
        /// <summary>
        /// 生成哥布林 NPC
        /// </summary>
        public void SpawnGoblinNPC()
        {
            DevLog("[GoblinNPC] 开始生成哥布林...");
            
            // 懒加载：在哥布林生成时检查并应用每日好感度衰减
            // 这样只有玩家真正遇到哥布林时才会触发衰减计算
            int decayAmount = AffinityManager.CheckAndApplyDailyDecay(GoblinAffinityConfig.NPC_ID);
            if (decayAmount > 0)
            {
                DevLog("[GoblinNPC] 好感度衰减已应用: -" + decayAmount);
            }
            
            // 如果已经存在，不重复生成
            if (goblinNPCInstance != null)
            {
                DevLog("[GoblinNPC] 哥布林已存在，跳过生成");
                return;
            }
            
            // 加载 AssetBundle
            if (!LoadGoblinAssetBundle())
            {
                DevLog("[GoblinNPC] 无法加载哥布林资源，跳过生成");
                return;
            }
            
            // 获取生成位置
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            
            // 检查场景是否配置了哥布林刷新点
            if (!ShouldSpawnGoblin(currentSceneName))
            {
                DevLog("[GoblinNPC] 场景 " + currentSceneName + " 未配置哥布林刷新点，跳过生成");
                return;
            }
            
            Vector3 spawnPos = GetGoblinSpawnPosition(currentSceneName);
            DevLog("[GoblinNPC] 场景: " + currentSceneName + ", 位置: " + spawnPos);
            
            // 检查是否获取到有效位置
            if (spawnPos == Vector3.zero)
            {
                DevLog("[GoblinNPC] 无法获取刷新点，跳过生成");
                return;
            }
            
            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(spawnPos + Vector3.up * 1f, Vector3.down, out hit, 5f))
            {
                spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                DevLog("[GoblinNPC] Raycast修正后位置: " + spawnPos);
            }
            else
            {
                DevLog("[GoblinNPC] Raycast修正失败，使用原始坐标: " + spawnPos);
            }
            
            try
            {
                // 实例化预制体
                goblinNPCInstance = UnityEngine.Object.Instantiate(goblinPrefab, spawnPos, Quaternion.identity);
                goblinNPCInstance.name = "GoblinNPC_BossRush";
                DevLog("[GoblinNPC] 预制体实例化成功");
                
                // 确保所有子对象都激活
                goblinNPCInstance.SetActive(true);
                foreach (Transform child in goblinNPCInstance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.SetActive(true);
                }
                
                // 修复 Shader（从 Standard 替换为游戏使用的 Shader）
                FixGoblinShaders(goblinNPCInstance);
                
                // 设置 Layer（确保渲染正确）
                SetGoblinLayerRecursively(goblinNPCInstance, LayerMask.NameToLayer("Default"));
                
                // 添加控制器组件
                goblinController = goblinNPCInstance.AddComponent<GoblinNPCController>();
                DevLog("[GoblinNPC] 控制器组件添加成功");
                
                // 添加移动控制组件
                GoblinMovement movement = goblinNPCInstance.AddComponent<GoblinMovement>();
                movement.SetSceneName(currentSceneName);
                DevLog("[GoblinNPC] 移动组件添加成功");
                
                // 添加交互组件（重铸服务）
                GoblinInteractable interactable = goblinNPCInstance.AddComponent<GoblinInteractable>();
                DevLog("[GoblinNPC] 交互组件添加成功");
                
                DevLog("[GoblinNPC] 哥布林生成成功，位置: " + spawnPos);
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "ModBehaviour.SpawnGoblinNPC - 生成哥布林");
            }
        }
        
        /// <summary>
        /// 修复哥布林模型的 Shader（从 Standard 替换为游戏 Shader）
        /// </summary>
        private void FixGoblinShaders(GameObject obj)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                // 尝试获取游戏使用的 Shader
                Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
                if (gameShader == null)
                {
                    gameShader = Shader.Find("Standard");
                }
                
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.materials != null)
                    {
                        foreach (Material mat in renderer.materials)
                        {
                            if (mat != null && mat.shader != null)
                            {
                                string shaderName = mat.shader.name;
                                // 如果是 Standard shader，替换为游戏 shader
                                if (shaderName == "Standard" || shaderName.Contains("Standard"))
                                {
                                    if (gameShader != null)
                                    {
                                        mat.shader = gameShader;
                                        DevLog("[GoblinNPC] 替换 Shader: " + shaderName + " -> " + gameShader.name);
                                    }
                                }
                            }
                        }
                    }
                }
            }, "ModBehaviour.FixGoblinShaders");
        }
        
        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        private void SetGoblinLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetGoblinLayerRecursively(child.gameObject, layer);
            }
        }
        
        /// <summary>
        /// 销毁哥布林 NPC
        /// </summary>
        public void DestroyGoblinNPC()
        {
            if (goblinNPCInstance != null)
            {
                UnityEngine.Object.Destroy(goblinNPCInstance);
                goblinNPCInstance = null;
                goblinController = null;
                DevLog("[GoblinNPC] 哥布林已销毁");
            }
        }
        
        /// <summary>
        /// 召唤哥布林跑向玩家
        /// 当玩家使用特定物品时调用此方法
        /// </summary>
        public void SummonGoblin()
        {
            if (goblinController != null)
            {
                goblinController.RunToPlayer();
                DevLog("[GoblinNPC] 哥布林被召唤，开始跑向玩家");
            }
            else
            {
                DevLog("[GoblinNPC] 哥布林控制器不存在，无法召唤");
            }
        }
        
        /// <summary>
        /// 获取哥布林NPC实例
        /// </summary>
        public GoblinNPCController GetGoblinController()
        {
            return goblinController;
        }
    }
}

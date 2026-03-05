// ============================================================================
// NurseNPC.cs - 护士NPC系统（ModBehaviour partial）
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的护士 NPC"羽织"，包括：
//   - 从 AssetBundle 加载护士模型
//   - 生成和销毁护士实例
//   - 刷新位置管理
//   
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 护士NPC系统 - ModBehaviour 的 partial class
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 护士实例和资源
        // ============================================================================
        
        // 护士实例
        private GameObject nurseNPCInstance = null;
        private NurseNPCController nurseController = null;
        
        // AssetBundle 缓存
        private static AssetBundle nurseAssetBundle = null;
        private static GameObject nursePrefab = null;
        
        /// <summary>
        /// 加载护士 AssetBundle
        /// </summary>
        private bool LoadNurseAssetBundle()
        {
            return NPCAssetBundleHelper.LoadNPCPrefab(
                "nursenpc", "NurseNPC", "[NurseNPC]",
                ref nurseAssetBundle, ref nursePrefab);
        }
        
        // ============================================================================
        // 刷新位置辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取护士刷新位置（避开其他NPC位置）
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetNurseSpawnPosition(string sceneName)
        {
            Vector3 courierPosition = Vector3.zero;
            Vector3 goblinPosition = Vector3.zero;
            
            // 获取其他NPC位置（如果存在）以避免重叠
            NPCExceptionHandler.TryExecute(() =>
            {
                if (courierNPCInstance != null)
                {
                    courierPosition = courierNPCInstance.transform.position;
                    DevLog("[NurseNPC] 检测到快递员位置: " + courierPosition + "，将避开");
                }
                if (goblinNPCInstance != null)
                {
                    goblinPosition = goblinNPCInstance.transform.position;
                    DevLog("[NurseNPC] 检测到哥布林位置: " + goblinPosition + "，将避开");
                }
            }, "ModBehaviour.GetNurseSpawnPosition - 获取其他NPC位置");
            
            // 从配置中查询护士刷新位置
            if (NPCSpawnConfig.TryGetNurseSpawnPosition(sceneName, out Vector3 position, courierPosition, goblinPosition, 10f))
            {
                return position;
            }
            
            // 未配置的场景使用随机刷新点
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                DevLog("[NurseNPC] 随机刷新点 [" + randomIndex + "/" + spawnPoints.Length + "]");
                return spawnPoints[randomIndex];
            }
            return Vector3.zero;
        }
        
        /// <summary>
        /// 检查当前场景是否应该生成护士
        /// </summary>
        private bool ShouldSpawnNurse(string sceneName)
        {
            return NPCSpawnConfig.HasNurseConfig(sceneName);
        }
        
        /// <summary>
        /// 生成护士 NPC
        /// </summary>
        public void SpawnNurseNPC()
        {
            DevLog("[NurseNPC] 开始生成护士...");
            
            // 懒加载：在NPC生成时统一检查并应用每日好感度衰减
            NPCAffinityInteractionHelper.ApplyDailyDecayOnSpawn(NurseAffinityConfig.NPC_ID, "[NurseNPC]");
            
            // 如果已经存在，不重复生成
            if (nurseNPCInstance != null)
            {
                DevLog("[NurseNPC] 护士已存在，跳过生成");
                return;
            }
            
            // 加载 AssetBundle
            if (!LoadNurseAssetBundle())
            {
                DevLog("[NurseNPC] 无法加载护士资源，跳过生成");
                return;
            }
            
            // 获取生成位置
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            
            // 检查场景是否配置了护士刷新点
            if (!ShouldSpawnNurse(currentSceneName))
            {
                DevLog("[NurseNPC] 场景 " + currentSceneName + " 未配置护士刷新点，跳过生成");
                return;
            }
            
            Vector3 spawnPos = GetNurseSpawnPosition(currentSceneName);
            DevLog("[NurseNPC] 场景: " + currentSceneName + ", 位置: " + spawnPos);
            
            // 检查是否获取到有效位置
            if (spawnPos == Vector3.zero)
            {
                DevLog("[NurseNPC] 无法获取刷新点，跳过生成");
                return;
            }
            
            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(spawnPos + Vector3.up * 1f, Vector3.down, out hit, 5f))
            {
                // 与哥布林逻辑保持一致
                spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                DevLog("[NurseNPC] Raycast 修正位置: " + spawnPos);
            }
            
            try
            {
                // 实例化护士
                nurseNPCInstance = UnityEngine.Object.Instantiate(nursePrefab, spawnPos, Quaternion.identity);
                nurseNPCInstance.name = "NurseNPC_YuZhi";

                // 确保实例及其子对象全部激活，避免模型子节点未激活导致“只有名字没有模型”
                nurseNPCInstance.SetActive(true);
                foreach (Transform child in nurseNPCInstance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.SetActive(true);
                }

                // 修复材质 Shader（与哥布林一致），避免粉模/不可见
                FixNurseShaders(nurseNPCInstance);

                // 统一 Layer，确保渲染与交互层级稳定
                SetNurseLayerRecursively(nurseNPCInstance, LayerMask.NameToLayer("Default"));
                
                // 添加控制器组件
                nurseController = nurseNPCInstance.GetComponent<NurseNPCController>();
                if (nurseController == null)
                {
                    nurseController = nurseNPCInstance.AddComponent<NurseNPCController>();
                }

                // 添加移动组件（护士漫步，目标点来源与其他NPC同配置）
                NurseMovement nurseMovement = nurseNPCInstance.GetComponent<NurseMovement>();
                if (nurseMovement == null)
                {
                    nurseMovement = nurseNPCInstance.AddComponent<NurseMovement>();
                }
                nurseMovement.SetSceneName(currentSceneName);
                DevLog("[NurseNPC] 移动组件添加成功");
                
                // 添加交互组件（使用游戏原生 interactableGroup 模式）
                NurseInteractable interactable = nurseNPCInstance.GetComponent<NurseInteractable>();
                if (interactable == null)
                {
                    interactable = nurseNPCInstance.AddComponent<NurseInteractable>();
                }
                
                DevLog("[NurseNPC] 护士NPC生成成功，位置: " + spawnPos);
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "ModBehaviour.SpawnNurseNPC - 实例化");
                if (nurseNPCInstance != null)
                {
                    UnityEngine.Object.Destroy(nurseNPCInstance);
                    nurseNPCInstance = null;
                }
                nurseController = null;
            }
        }

        /// <summary>
        /// 修复护士模型的 Shader（从 Standard 替换为游戏 Shader）
        /// </summary>
        private void FixNurseShaders(GameObject obj)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
                if (gameShader == null)
                {
                    gameShader = Shader.Find("Standard");
                }

                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.materials == null) continue;

                    foreach (Material mat in renderer.materials)
                    {
                        if (mat == null || mat.shader == null) continue;

                        string shaderName = mat.shader.name;
                        if (shaderName == "Standard" || shaderName.Contains("Standard"))
                        {
                            if (gameShader != null)
                            {
                                mat.shader = gameShader;
                                DevLog("[NurseNPC] 替换 Shader: " + shaderName + " -> " + gameShader.name);
                            }
                        }
                    }
                }
            }, "ModBehaviour.FixNurseShaders");
        }

        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        private void SetNurseLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetNurseLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>
        /// 销毁护士 NPC
        /// </summary>
        public void DestroyNurseNPC()
        {
            if (nurseNPCInstance != null)
            {
                DevLog("[NurseNPC] 销毁护士NPC");
                UnityEngine.Object.Destroy(nurseNPCInstance);
                nurseNPCInstance = null;
                nurseController = null;
            }
        }
        
        /// <summary>
        /// 获取护士控制器引用
        /// </summary>
        public NurseNPCController GetNurseController()
        {
            return nurseController;
        }
        
        /// <summary>
        /// 护士NPC是否已生成
        /// </summary>
        public bool IsNurseSpawned()
        {
            return nurseNPCInstance != null;
        }
    }
}

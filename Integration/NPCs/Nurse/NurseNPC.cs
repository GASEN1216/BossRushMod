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
            Vector3[] sharedSpawnPoints = GetSharedCommonNPCSpawnPointsForScene(sceneName);
            if (NPCSpawnConfig.TryGetSharedSpawnPosition(sharedSpawnPoints, out Vector3 position, new[] { courierPosition, goblinPosition }, 10f, requireAvoidance: true))
            {
                return position;
            }

            if (sharedSpawnPoints != null && sharedSpawnPoints.Length > 0)
            {
                DevLog("[NurseNPC] 所有护士刷新点都与其他NPC过近，跳过本次生成");
                return Vector3.zero;
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
            if (ShouldUseRandomSupportNpcSelection(sceneName))
            {
                return IsValidBossRushArenaScene(sceneName);
            }

            if (IsModeEActive || IsModeFActive)
            {
                return IsValidBossRushArenaScene(sceneName);
            }

            return NPCSpawnConfig.HasCourierNormalModeConfig(sceneName);
        }
        
        /// <summary>
        /// 生成护士 NPC
        /// </summary>
        /// <param name="overrideSpawnPos">强制刷新位置（用于婚礼教堂等特殊场景）</param>
        /// <param name="stayStillOnSpawn">刷新后是否保持不动</param>
        /// <param name="forceSpawn">是否忽略普通模式刷新条件</param>
        public void SpawnNurseNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)
        {
            DevLog("[NurseNPC] 开始生成护士...");
            
            // 懒加载：在NPC生成时统一检查并应用每日好感度衰减
            NPCAffinityInteractionHelper.ApplyDailyDecayOnSpawn(NurseAffinityConfig.NPC_ID, "[NurseNPC]");

            // 已婚后不再参与普通地图刷新（仅婚礼教堂强制生成）
            if (!forceSpawn && AffinityManager.IsMarriedToPlayer(NurseAffinityConfig.NPC_ID))
            {
                DevLog("[NurseNPC] 已与玩家结婚，跳过普通地图刷新");
                return;
            }
            
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
            if (!forceSpawn && !ShouldSpawnNurse(currentSceneName))
            {
                DevLog("[NurseNPC] 场景 " + currentSceneName + " 未配置护士刷新点，跳过生成");
                return;
            }
            
            Vector3 spawnPos = overrideSpawnPos.HasValue
                ? overrideSpawnPos.Value
                : GetNurseSpawnPosition(currentSceneName);
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

                NPCCommonUtils.FixShaders(nurseNPCInstance, "[NurseNPC]");
                NPCCommonUtils.SetLayerRecursively(nurseNPCInstance, LayerMask.NameToLayer("Default"));
                
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

                // 婚礼教堂中的已婚NPC暂时站桩不动
                if (stayStillOnSpawn)
                {
                    nurseMovement.StopMove();
                    nurseMovement.enabled = false;
                    DevLog("[NurseNPC] 已设置为站桩模式（不移动）");
                }
                
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

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
            return NPCAssetBundleHelper.LoadNPCPrefab(
                "goblinnpc", "GoblinNPC", "[GoblinNPC]",
                ref goblinAssetBundle, ref goblinPrefab);
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
            
            Vector3[] sharedSpawnPoints = GetSharedCommonNPCSpawnPointsForScene(sceneName);
            if (NPCSpawnConfig.TryGetSharedSpawnPosition(
                sharedSpawnPoints,
                out Vector3 position,
                new[] { courierPosition },
                10f,
                requireAvoidance: true))
            {
                if (courierPosition != Vector3.zero)
                {
                    float distance = Vector3.Distance(position, courierPosition);
                    DevLog("[GoblinNPC] 刷新位置与快递员距离: " + distance.ToString("F1") + "米");
                }
                return position;
            }

            return Vector3.zero;
        }
        
        /// <summary>
        /// 检查当前场景是否应该生成哥布林
        /// </summary>
        private bool ShouldSpawnGoblin(string sceneName)
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
        /// 生成哥布林 NPC
        /// </summary>
        /// <param name="overrideSpawnPos">强制刷新位置（用于婚礼教堂等特殊场景）</param>
        /// <param name="stayStillOnSpawn">刷新后是否保持不动</param>
        /// <param name="forceSpawn">是否忽略普通模式刷新条件</param>
        public void SpawnGoblinNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)
        {
            DevLog("[GoblinNPC] 开始生成哥布林...");
            
            // 懒加载：在NPC生成时统一检查并应用每日好感度衰减
            NPCAffinityInteractionHelper.ApplyDailyDecayOnSpawn(GoblinAffinityConfig.NPC_ID, "[GoblinNPC]");

            // 已婚后不再参与普通地图刷新（仅婚礼教堂强制生成）
            if (!forceSpawn && AffinityManager.IsMarriedToPlayer(GoblinAffinityConfig.NPC_ID))
            {
                DevLog("[GoblinNPC] 已与玩家结婚，跳过普通地图刷新");
                return;
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
            if (!forceSpawn && !ShouldSpawnGoblin(currentSceneName))
            {
                DevLog("[GoblinNPC] 场景 " + currentSceneName + " 未配置哥布林刷新点，跳过生成");
                return;
            }
            
            Vector3 spawnPos = overrideSpawnPos.HasValue
                ? overrideSpawnPos.Value
                : GetGoblinSpawnPosition(currentSceneName);
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
                
                NPCCommonUtils.FixShaders(goblinNPCInstance, "[GoblinNPC]");
                NPCCommonUtils.SetLayerRecursively(goblinNPCInstance, LayerMask.NameToLayer("Default"));
                
                // 添加控制器组件
                goblinController = goblinNPCInstance.AddComponent<GoblinNPCController>();
                DevLog("[GoblinNPC] 控制器组件添加成功");
                
                // 添加移动控制组件
                GoblinMovement movement = goblinNPCInstance.AddComponent<GoblinMovement>();
                movement.SetSceneName(currentSceneName);
                DevLog("[GoblinNPC] 移动组件添加成功");

                // 婚礼教堂中的已婚NPC暂时站桩不动
                if (stayStillOnSpawn)
                {
                    movement.StopMove();
                    movement.enabled = false;
                    if (goblinController != null)
                    {
                        goblinController.EnterStationaryIdleState();
                    }
                    DevLog("[GoblinNPC] 已设置为站桩模式（不移动）");
                }
                
                // 添加交互组件（重铸服务）
                GoblinInteractable interactable = goblinNPCInstance.AddComponent<GoblinInteractable>();
                DevLog("[GoblinNPC] 交互组件添加成功");
                
                DevLog("[GoblinNPC] 哥布林生成成功，位置: " + spawnPos);
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "ModBehaviour.SpawnGoblinNPC - spawn goblin");
                if (goblinNPCInstance != null)
                {
                    UnityEngine.Object.Destroy(goblinNPCInstance);
                    goblinNPCInstance = null;
                }
                goblinController = null;
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

// ============================================================================
// CourierNPC.cs - 快递员NPC系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的快递员 NPC，包括：
//   - 从 AssetBundle 加载快递员模型
//   - 动画状态管理（Walking、Idle、Dancing、Cheer、Running）
//   - 与玩家的距离检测和行为逻辑
//   - 快递服务交互选项
//   - 使用 A* Pathfinding Seeker 进行寻路（与游戏原生系统一致）
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Duckov.Utilities;
using Pathfinding;
using Cysharp.Threading.Tasks;
using Saves;
using ItemStatsSystem;
using Dialogues;
using NodeCanvas.DialogueTrees;
using SodaCraft.Localizations;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 快递员NPC系统
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 快递员实例和资源
        // ============================================================================

        // 快递员实例
        private GameObject courierNPCInstance = null;
        private CourierNPCController courierController = null;

        // AssetBundle 缓存
        private static AssetBundle courierAssetBundle = null;
        private static GameObject courierPrefab = null;

        /// <summary>
        /// 加载快递员 AssetBundle
        /// </summary>
        private bool LoadCourierAssetBundle()
        {
            return NPCAssetBundleHelper.LoadNPCPrefab(
                "couriernpc",
                "CourierNPC",
                "[CourierNPC]",
                ref courierAssetBundle,
                ref courierPrefab);
        }

        // ============================================================================
        // 刷新位置辅助方法
        // ============================================================================

        /// <summary>
        /// 获取BossRush竞技场模式的刷新位置
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetBossRushArenaSpawnPosition(string sceneName)
        {
            // 从配置中查询BossRush模式固定位置
            if (NPCSpawnConfig.TryGetCourierBossRushPosition(sceneName, out Vector3 position))
            {
                return position;
            }

            // 未配置的场景使用随机刷新点
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                DevLog("[CourierNPC] BossRush模式随机刷新点 [" + randomIndex + "/" + spawnPoints.Length + "]");
                return spawnPoints[randomIndex];
            }
            return Vector3.zero;
        }

        /// <summary>
        /// 获取普通模式的刷新位置（非BossRush模式）
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetNormalModeSpawnPosition(string sceneName)
        {
            if (NPCSpawnConfig.TryGetCourierNormalModePosition(sceneName, out Vector3 position))
            {
                int count = NPCSpawnConfig.GetCourierNormalModeSpawnPointCount(sceneName);
                DevLog("[CourierNPC] 普通模式随机刷新点，场景: " + sceneName + ", 可选点数: " + count);
                return position;
            }

            // 没有配置则返回零向量表示不生成
            DevLog("[CourierNPC] 场景 " + sceneName + " 未配置普通模式刷新点");
            return Vector3.zero;
        }

        /// <summary>
        /// 检查当前场景是否应该在普通模式下生成快递员
        /// </summary>
        private bool ShouldSpawnCourierInNormalMode(string sceneName)
        {
            return NPCSpawnConfig.HasCourierNormalModeConfig(sceneName);
        }

        /// <summary>
        /// 在 BossRush 竞技场生成快递员 NPC
        /// 支持BossRush模式（固定位置）和普通模式（随机刷新点）
        /// </summary>
        public void SpawnCourierNPC()
        {
            DevLog("[CourierNPC] 开始生成快递员...");

            // 如果已经存在，不重复生成
            if (courierNPCInstance != null)
            {
                DevLog("[CourierNPC] 快递员已存在，跳过生成");
                return;
            }

            // 加载 AssetBundle
            if (!LoadCourierAssetBundle())
            {
                DevLog("[CourierNPC] 无法加载快递员资源，跳过生成");
                return;
            }

            // 获取生成位置
            Vector3 spawnPos;
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // 检测是否为BossRush模式（包括ModeD、ModeE和竞技场激活状态）
            bool isBossRushMode = IsAnyBossRushLikeModeActive();
            DevLog("[CourierNPC] 模式检测: IsActive=" + IsActive + ", IsModeDActive=" + IsModeDActive + ", IsBossRushArenaActive=" + IsBossRushArenaActive + ", IsModeEActive=" + IsModeEActive + " => BossRush模式=" + isBossRushMode);

            if (isBossRushMode)
            {
                if (UsesArenaSupportNpcPlacement())
                {
                    // Mode E：优先使用地图配置的 modeEPlayerSpawnPos（与玩家传送目标一致），
                    // 避免依赖 player.transform.position（场景初始化阶段位置可能尚未稳定）
                    BossRushMapConfig mapConfig = GetCurrentMapConfig();
                    if (mapConfig != null && mapConfig.modeEPlayerSpawnPos.HasValue)
                    {
                        spawnPos = mapConfig.modeEPlayerSpawnPos.Value;
                        DevLog("[CourierNPC] Mode E 模式，使用地图配置 modeEPlayerSpawnPos: " + spawnPos);
                    }
                    else
                    {
                        CharacterMainControl player = CharacterMainControl.Main;
                        if (player != null)
                        {
                            spawnPos = player.transform.position;
                            DevLog("[CourierNPC] Mode E 模式，兜底使用玩家位置: " + spawnPos);
                        }
                        else
                        {
                            DevLog("[CourierNPC] Mode E 模式但玩家为空，跳过生成");
                            return;
                        }
                    }
                }
                else
                {
                    // BossRush模式：使用竞技场固定位置
                    spawnPos = GetBossRushArenaSpawnPosition(currentSceneName);
                    DevLog("[CourierNPC] BossRush模式，场景: " + currentSceneName + ", 位置: " + spawnPos);

                    // 检查是否获取到有效位置
                    if (spawnPos == Vector3.zero)
                    {
                        DevLog("[CourierNPC] BossRush模式无法获取刷新点，跳过生成");
                        return;
                    }
                }
            }
            else
            {
                // 普通模式：检查是否有配置的随机刷新点
                if (ShouldSpawnCourierInNormalMode(currentSceneName))
                {
                    spawnPos = GetNormalModeSpawnPosition(currentSceneName);
                    DevLog("[CourierNPC] 普通模式，场景: " + currentSceneName + ", 随机位置: " + spawnPos);

                    // 检查是否获取到有效位置
                    if (spawnPos == Vector3.zero)
                    {
                        DevLog("[CourierNPC] 普通模式无法获取刷新点，跳过生成");
                        return;
                    }
                }
                else
                {
                    // 普通模式下，未配置的场景不生成快递员
                    DevLog("[CourierNPC] 普通模式，场景 " + currentSceneName + " 未配置刷新点，跳过生成");
                    return;
                }
            }

            // 修正落点到地面：优先 NavMesh 采样（更可靠），回退到 Raycast
            UnityEngine.AI.NavMeshHit navHit;
            if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out navHit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                spawnPos = navHit.position + new Vector3(0f, 0.1f, 0f);
                DevLog("[CourierNPC] NavMesh修正后位置: " + spawnPos);
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(spawnPos + Vector3.up * 1f, Vector3.down, out hit, 50f))
                {
                    spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                    DevLog("[CourierNPC] Raycast修正后位置: " + spawnPos);
                }
                else
                {
                    DevLog("[CourierNPC] 地面修正失败，使用原始坐标: " + spawnPos);
                }
            }

            try
            {
                // 实例化预制体
                courierNPCInstance = UnityEngine.Object.Instantiate(courierPrefab, spawnPos, Quaternion.identity);
                courierNPCInstance.name = "CourierNPC_BossRush";
                DevLog("[CourierNPC] 预制体实例化成功");

                // 确保所有子对象都激活
                courierNPCInstance.SetActive(true);
                foreach (Transform child in courierNPCInstance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.SetActive(true);
                }

                NPCCommonUtils.FixShaders(courierNPCInstance, "[CourierNPC]");
                NPCCommonUtils.SetLayerRecursively(courierNPCInstance, LayerMask.NameToLayer("Default"));

                // 添加控制器组件
                courierController = courierNPCInstance.AddComponent<CourierNPCController>();
                DevLog("[CourierNPC] 控制器组件添加成功");

                // 添加移动控制组件（内部会延迟初始化 NavMeshAgent）
                CourierMovement movement = courierNPCInstance.AddComponent<CourierMovement>();
                DevLog("[CourierNPC] 移动组件添加成功");

                // 设置移动模式
                if (UsesArenaSupportNpcPlacement())
                {
                    // Mode E：设置固定模式，快递员站在玩家出生点不移动
                    movement.SetStationary(true);
                    DevLog("[CourierNPC] Mode E 模式，已设置固定模式，快递员将站在原地");
                }
                else if (!isBossRushMode)
                {
                    movement.SetNormalMode(true, currentSceneName);
                    DevLog("[CourierNPC] 设置为普通模式，使用场景 " + currentSceneName + " 的刷新点");
                }

                // 添加交互组件
                AddCourierInteraction(courierNPCInstance);

                DevLog("[CourierNPC] 快递员生成成功，位置: " + spawnPos);
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 生成快递员出错: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 为快递员添加交互选项
        /// </summary>
        private void AddCourierInteraction(GameObject courier)
        {
            try
            {
                DevLog("[CourierNPC] 开始添加交互组件...");

                // 添加交互组件（CourierInteractable.Awake 会自动处理 Collider 和 Layer）
                CourierInteractable interactable = courier.AddComponent<CourierInteractable>();

                DevLog("[CourierNPC] 交互组件添加成功");
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 添加交互组件出错: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 销毁快递员 NPC
        /// </summary>
        public void DestroyCourierNPC()
        {
            try
            {
                CourierPaidLootSweepService.ReleasePendingSweepResultToPlayer(true, false);
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 清理付费扫箱待领结果失败: " + e.Message);
            }

            if (courierNPCInstance != null)
            {
                UnityEngine.Object.Destroy(courierNPCInstance);
                courierNPCInstance = null;
                courierController = null;
                DevLog("[CourierNPC] 快递员已销毁");
            }
        }

        /// <summary>
        /// 通知快递员 Boss 战开始
        /// </summary>
        public void NotifyCourierBossFightStart()
        {
            if (courierController != null)
            {
                courierController.SetBossFight(true);
            }

            // 同时通知移动组件
            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetBossFight(true);
                }
            }
        }

        /// <summary>
        /// 通知快递员 Boss 战结束
        /// </summary>
        public void NotifyCourierBossFightEnd()
        {
            if (courierController != null)
            {
                courierController.SetBossFight(false);
            }

            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetBossFight(false);
                }
            }
        }

        /// <summary>
        /// 通知快递员当前没有Boss（召唤间隔期间）
        /// </summary>
        public void NotifyCourierNoBoss(bool noBoss)
        {
            if (courierController != null)
            {
                courierController.SetNoBoss(noBoss);
            }

            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetNoBoss(noBoss);
                }
            }
        }

        /// <summary>
        /// 通知快递员 BossRush 通关
        /// </summary>
        public void NotifyCourierBossRushCompleted()
        {
            if (courierController != null)
            {
                courierController.SetCompleted();
            }

            // 同时通知移动组件停止移动
            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetCompleted(true);
                }
            }
        }

        /// <summary>
        /// 传送玩家到快递员NPC身边（调试功能，F12 调用）
        /// </summary>
        public void TeleportToCourierNPC()
        {
            // 检查快递员是否存在
            if (courierNPCInstance == null)
            {
                DevLog("[BossRush] F12 传送：快递员NPC不存在");
                return;
            }

            // 获取玩家引用
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] F12 传送：未找到玩家 CharacterMainControl");
                return;
            }

            // 计算传送目标位置（快递员位置偏移2米，避免重叠）
            Vector3 courierPos = courierNPCInstance.transform.position;
            Vector3 offset = new Vector3(2f, 0f, 0f);  // X轴偏移2米
            Vector3 targetPos = courierPos + offset;

            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(targetPos + Vector3.up * 1f, Vector3.down, out hit, 5f))
            {
                targetPos = hit.point + new Vector3(0f, 0.1f, 0f);
            }

            // 执行传送
            main.transform.position = targetPos;
            DevLog("[BossRush] F12 传送：已将玩家传送到快递员身边，位置: " + targetPos);
        }
    }


}

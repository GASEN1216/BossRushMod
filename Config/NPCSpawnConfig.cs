// ============================================================================
// NPCSpawnConfig.cs - NPC刷新点配置系统
// ============================================================================
// 模块说明：
//   集中管理所有NPC在不同场景的刷新点配置，支持：
//   - 多NPC类型配置（快递员、未来其他NPC）
//   - 多场景刷新点配置
//   - BossRush模式和普通模式的区分
//   
// 扩展方式：
//   1. 添加新场景刷新点：在对应NPC的配置字典中添加新条目
//   2. 添加新NPC类型：创建新的刷新点数组和配置字典
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    // ============================================================================
    // NPC刷新配置数据结构
    // ============================================================================
    
    /// <summary>
    /// NPC场景刷新配置
    /// 用于定义NPC在特定场景的刷新点
    /// </summary>
    public struct NPCSceneSpawnConfig
    {
        /// <summary>场景名称</summary>
        public string sceneName;
        
        /// <summary>刷新点数组</summary>
        public Vector3[] spawnPoints;
        
        /// <summary>是否随机选择刷新点</summary>
        public bool useRandomSpawn;
        
        public NPCSceneSpawnConfig(string scene, Vector3[] points, bool random = true)
        {
            sceneName = scene;
            spawnPoints = points;
            useRandomSpawn = random;
        }
    }
    
    /// <summary>
    /// NPC刷新点配置管理器
    /// 集中管理所有NPC在不同场景的刷新点
    /// </summary>
    public static class NPCSpawnConfig
    {
        // ============================================================================
        // 快递员NPC刷新点配置
        // ============================================================================
        
        #region 快递员 - BossRush模式固定位置
        
        /// <summary>
        /// 快递员在BossRush模式下的固定刷新位置
        /// Key: 场景名称, Value: 固定位置
        /// </summary>
        public static readonly Dictionary<string, Vector3> CourierBossRushPositions = new Dictionary<string, Vector3>
        {
            { "Level_DemoChallenge_1", new Vector3(234.80f, -7.99f, 206.56f) },
            { "Level_ChallengeSnow", new Vector3(221.48f, 0.01f, 287.92f) },
            { "Level_GroundZero_1", new Vector3(428.25f, 0.02f, 284.11f) },
            { "Level_HiddenWarehouse", new Vector3(129.37f, 0.02f, 186.54f) },
            { "Level_Farm_01", new Vector3(383.43f, 0.04f, 585.01f) },
            { "Level_JLab_1", new Vector3(-35.53f, 0.09f, -56.07f) },
            { "Level_StormZone_B0", new Vector3(109.99f, 0.02f, 489.79f) },
            { "Level_SnowMilitaryBase", new Vector3(469.11f, 0.04f, 549.42f) },
            { "Level_SnowMilitaryBase_ColdStorage", new Vector3(9.09f, 0.02f, -37.73f) }
        };
        
        #endregion
        
        #region 快递员 - 普通模式随机刷新点
        
        /// <summary>
        /// GroundZero场景快递员刷新点（普通模式）
        /// 注意：已移除高台位置 (345.69, 17.75, 231.50)，该点NPC无法通过A*寻路到达
        /// </summary>
        private static readonly Vector3[] GroundZeroCourierSpawnPoints = new Vector3[]
        {
            new Vector3(412.26f, 4.03f, 158.38f),
            new Vector3(217.66f, 0.02f, 147.03f),
            new Vector3(427.62f, 0.02f, 244.91f),
            new Vector3(545.85f, 0.02f, 189.02f),
            new Vector3(335.51f, 0.02f, 305.36f)
        };
        
        /// <summary>
        /// HiddenWarehouse场景快递员刷新点（普通模式）
        /// </summary>
        private static readonly Vector3[] HiddenWarehouseCourierSpawnPoints = new Vector3[]
        {
            new Vector3(329.24f, 0.02f, 112.10f),
            new Vector3(261.25f, 0.02f, 115.09f),
            new Vector3(471.85f, 0.02f, 188.42f),
            new Vector3(430.79f, 0.02f, 292.65f),
            new Vector3(291.69f, 0.02f, 189.60f),
            new Vector3(123.24f, 0.02f, 324.71f),
            new Vector3(44.25f, 0.02f, 185.61f)
        };
        
        /// <summary>
        /// Farm场景快递员刷新点（普通模式）
        /// </summary>
        private static readonly Vector3[] FarmCourierSpawnPoints = new Vector3[]
        {
            new Vector3(185.56f, 2.02f, 640.58f),
            new Vector3(106.23f, 2.02f, 531.73f),
            new Vector3(126.54f, -1.22f, 375.72f),
            new Vector3(529.37f, 0.02f, 320.19f),
            new Vector3(883.88f, 0.02f, 295.61f),
            new Vector3(679.47f, 6.03f, 404.00f),
            new Vector3(602.69f, 0.02f, 512.19f),
            new Vector3(670.46f, 0.02f, 646.76f),
            new Vector3(665.98f, 0.02f, 843.02f),
            new Vector3(392.26f, 0.02f, 652.96f)
        };
        
        /// <summary>
        /// JLab场景快递员刷新点（普通模式）
        /// </summary>
        private static readonly Vector3[] JLabCourierSpawnPoints = new Vector3[]
        {
            new Vector3(-35.71f, 0.05f, -67.71f),
            new Vector3(-90.59f, 0.02f, -56.27f),
            new Vector3(-73.44f, 0.02f, -22.53f),
            new Vector3(-27.84f, 0.02f, -29.16f),
            new Vector3(-11.67f, 0.02f, -19.76f),
            new Vector3(13.26f, 0.02f, -53.81f)
        };
        
        /// <summary>
        /// JLab2场景快递员刷新点（普通模式）
        /// </summary>
        private static readonly Vector3[] JLab2CourierSpawnPoints = new Vector3[]
        {
            new Vector3(383.62f, 0.02f, -28.26f),
            new Vector3(383.39f, 0.02f, -62.47f),
            new Vector3(427.36f, 0.02f, -28.40f)
        };
        
        /// <summary>
        /// StormZone场景快递员刷新点（普通模式）
        /// </summary>
        private static readonly Vector3[] StormZoneCourierSpawnPoints = new Vector3[]
        {
            new Vector3(431.86f, 0.02f, 461.93f),
            new Vector3(471.49f, 0.02f, 389.59f),
            new Vector3(505.72f, 0.02f, 436.78f),
            new Vector3(581.14f, 0.02f, 366.63f),
            new Vector3(584.82f, 0.02f, 357.27f)
        };
        
        /// <summary>
        /// ChallengeSnow场景快递员刷新点（普通模式）
        /// </summary>
        private static readonly Vector3[] ChallengeSnowCourierSpawnPoints = new Vector3[]
        {
            new Vector3(245.80f, 0.04f, 88.34f),
            new Vector3(331.71f, 0.01f, 311.66f),
            new Vector3(268.68f, 0.02f, 220.83f)
        };
        
        // 未来可添加其他场景的刷新点数组，例如：
        // private static readonly Vector3[] FarmCourierSpawnPoints = new Vector3[] { ... };
        
        /// <summary>
        /// 快递员普通模式刷新配置
        /// Key: 场景名称, Value: 刷新配置
        /// </summary>
        public static readonly Dictionary<string, NPCSceneSpawnConfig> CourierNormalModeConfigs = 
            new Dictionary<string, NPCSceneSpawnConfig>
        {
            {
                "Level_GroundZero_1",
                new NPCSceneSpawnConfig("Level_GroundZero_1", GroundZeroCourierSpawnPoints, true)
            },
            {
                "Level_HiddenWarehouse",
                new NPCSceneSpawnConfig("Level_HiddenWarehouse", HiddenWarehouseCourierSpawnPoints, true)
            },
            {
                "Level_Farm_01",
                new NPCSceneSpawnConfig("Level_Farm_01", FarmCourierSpawnPoints, true)
            },
            {
                "Level_JLab_1",
                new NPCSceneSpawnConfig("Level_JLab_1", JLabCourierSpawnPoints, true)
            },
            {
                "Level_JLab_2",
                new NPCSceneSpawnConfig("Level_JLab_2", JLab2CourierSpawnPoints, true)
            },
            {
                "Level_StormZone_1",
                new NPCSceneSpawnConfig("Level_StormZone_1", StormZoneCourierSpawnPoints, true)
            },
            {
                "Level_ChallengeSnow",
                new NPCSceneSpawnConfig("Level_ChallengeSnow", ChallengeSnowCourierSpawnPoints, true)
            }
            // 未来可在此添加其他场景配置
        };
        
        #endregion
        
        // ============================================================================
        // 快递员刷新点查询方法
        // ============================================================================
        
        /// <summary>
        /// 获取快递员在BossRush模式下的刷新位置
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="position">输出位置</param>
        /// <returns>是否找到配置</returns>
        public static bool TryGetCourierBossRushPosition(string sceneName, out Vector3 position)
        {
            return CourierBossRushPositions.TryGetValue(sceneName, out position);
        }
        
        /// <summary>
        /// 获取快递员在普通模式下的随机刷新位置
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="position">输出位置（随机选择）</param>
        /// <returns>是否找到配置</returns>
        public static bool TryGetCourierNormalModePosition(string sceneName, out Vector3 position)
        {
            position = Vector3.zero;
            
            if (CourierNormalModeConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
            {
                if (config.spawnPoints != null && config.spawnPoints.Length > 0)
                {
                    if (config.useRandomSpawn)
                    {
                        int randomIndex = Random.Range(0, config.spawnPoints.Length);
                        position = config.spawnPoints[randomIndex];
                    }
                    else
                    {
                        position = config.spawnPoints[0];
                    }
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 检查场景是否配置了快递员普通模式刷新点
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns>是否有配置</returns>
        public static bool HasCourierNormalModeConfig(string sceneName)
        {
            return CourierNormalModeConfigs.ContainsKey(sceneName);
        }
        
        /// <summary>
        /// 获取快递员普通模式刷新点数量（用于日志）
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns>刷新点数量，未配置返回0</returns>
        public static int GetCourierNormalModeSpawnPointCount(string sceneName)
        {
            if (CourierNormalModeConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
            {
                return config.spawnPoints?.Length ?? 0;
            }
            return 0;
        }
        
        // ============================================================================
        // 哥布林NPC刷新点配置
        // ============================================================================
        
        #region 哥布林 - 刷新点配置（仅限特定场景）
        
        /// <summary>
        /// 哥布林刷新配置
        /// 哥布林只在以下三个场景刷新：GroundZero、HiddenWarehouse、Farm
        /// 复用快递员的刷新点，但初始化时会避免重复位置
        /// </summary>
        public static readonly Dictionary<string, NPCSceneSpawnConfig> GoblinSpawnConfigs = 
            new Dictionary<string, NPCSceneSpawnConfig>
        {
            {
                "Level_GroundZero_1",
                new NPCSceneSpawnConfig("Level_GroundZero_1", GroundZeroCourierSpawnPoints, true)
            },
            {
                "Level_HiddenWarehouse",
                new NPCSceneSpawnConfig("Level_HiddenWarehouse", HiddenWarehouseCourierSpawnPoints, true)
            },
            {
                "Level_Farm_01",
                new NPCSceneSpawnConfig("Level_Farm_01", FarmCourierSpawnPoints, true)
            }
        };
        
        #endregion
        
        // ============================================================================
        // 哥布林刷新点查询方法
        // ============================================================================
        
        /// <summary>
        /// 获取哥布林的随机刷新位置（避开快递员位置）
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="position">输出位置（随机选择）</param>
        /// <param name="courierPosition">快递员位置（用于避开）</param>
        /// <param name="minDistance">与快递员的最小距离，默认10米</param>
        /// <returns>是否找到配置</returns>
        public static bool TryGetGoblinSpawnPosition(string sceneName, out Vector3 position, Vector3 courierPosition = default(Vector3), float minDistance = 10f)
        {
            position = Vector3.zero;
            
            if (GoblinSpawnConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
            {
                if (config.spawnPoints != null && config.spawnPoints.Length > 0)
                {
                    // 如果提供了快递员位置，尝试找到远离快递员的刷新点
                    if (courierPosition != default(Vector3))
                    {
                        // 先收集所有满足距离要求的刷新点
                        List<Vector3> validPoints = new List<Vector3>();
                        foreach (Vector3 point in config.spawnPoints)
                        {
                            Vector3 diff = point - courierPosition;
                            diff.y = 0;  // 只计算水平距离
                            if (diff.magnitude >= minDistance)
                            {
                                validPoints.Add(point);
                            }
                        }
                        
                        // 如果有满足条件的点，从中随机选择
                        if (validPoints.Count > 0)
                        {
                            int randomIndex = Random.Range(0, validPoints.Count);
                            position = validPoints[randomIndex];
                            return true;
                        }
                    }
                    
                    // 如果没有提供快递员位置，或没有找到满足距离的点，使用默认随机选择
                    if (config.useRandomSpawn)
                    {
                        int randomIndex = Random.Range(0, config.spawnPoints.Length);
                        position = config.spawnPoints[randomIndex];
                    }
                    else
                    {
                        position = config.spawnPoints[0];
                    }
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 检查场景是否配置了哥布林刷新点
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns>是否有配置</returns>
        public static bool HasGoblinConfig(string sceneName)
        {
            return GoblinSpawnConfigs.ContainsKey(sceneName);
        }
        
        /// <summary>
        /// 获取哥布林刷新点数量（用于日志）
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns>刷新点数量，未配置返回0</returns>
        public static int GetGoblinSpawnPointCount(string sceneName)
        {
            if (GoblinSpawnConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
            {
                return config.spawnPoints?.Length ?? 0;
            }
            return 0;
        }
    }
}

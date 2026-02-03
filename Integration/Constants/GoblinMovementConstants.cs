// ============================================================================
// GoblinMovementConstants.cs - 哥布林移动相关常量
// ============================================================================
// 模块说明：
//   统一管理哥布林移动相关的魔法数字，便于修改和维护
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

namespace BossRush.Constants
{
    /// <summary>
    /// 哥布林移动相关常量
    /// 包含速度、寻路、漫步、CharacterController、物理等配置
    /// </summary>
    public static class GoblinMovementConstants
    {
        // ============================================================================
        // 速度配置（单位：米/秒）
        // ============================================================================
        
        /// <summary>
        /// 走路速度
        /// </summary>
        public const float WALK_SPEED = 2f;
        
        /// <summary>
        /// 跑步速度
        /// </summary>
        public const float RUN_SPEED = 6f;
        
        /// <summary>
        /// 转向速度（单位：度/秒）
        /// </summary>
        public const float TURN_SPEED = 360f;
        
        // ============================================================================
        // 寻路配置（单位：米）
        // ============================================================================
        
        /// <summary>
        /// 下一个路点距离（到达此距离时切换到下一个路点）
        /// </summary>
        public const float NEXT_WAYPOINT_DISTANCE = 0.5f;
        
        /// <summary>
        /// 停止距离（到达目标此距离时停止移动）
        /// </summary>
        public const float STOP_DISTANCE = 0.3f;
        
        // ============================================================================
        // 漫步配置
        // ============================================================================
        
        /// <summary>
        /// 漫步半径（单位：米）
        /// </summary>
        public const float WANDER_RADIUS = 8f;
        
        /// <summary>
        /// 漫步间隔（单位：秒）
        /// </summary>
        public const float WANDER_INTERVAL = 5f;
        
        // ============================================================================
        // 初始化配置
        // ============================================================================
        
        /// <summary>
        /// 初始化延迟时间（单位：秒）
        /// </summary>
        public const float INIT_DELAY = 0.5f;
        
        // ============================================================================
        // CharacterController 配置
        // ============================================================================
        
        /// <summary>
        /// 角色高度（单位：米）
        /// </summary>
        public const float CHARACTER_HEIGHT = 1.2f;
        
        /// <summary>
        /// 角色半径（单位：米）
        /// </summary>
        public const float CHARACTER_RADIUS = 0.25f;
        
        /// <summary>
        /// 角色中心Y轴偏移（单位：米）
        /// </summary>
        public const float CHARACTER_CENTER_Y = 0.6f;
        
        /// <summary>
        /// 坡度限制（单位：度）
        /// </summary>
        public const float SLOPE_LIMIT = 45f;
        
        /// <summary>
        /// 台阶偏移（单位：米）
        /// </summary>
        public const float STEP_OFFSET = 0.3f;
        
        /// <summary>
        /// 皮肤宽度（单位：米）
        /// </summary>
        public const float SKIN_WIDTH = 0.08f;
        
        // ============================================================================
        // 物理配置
        // ============================================================================
        
        /// <summary>
        /// 重力加速度（单位：米/秒²）
        /// </summary>
        public const float GRAVITY = -9.8f;
        
        /// <summary>
        /// 接地时的垂直速度（用于保持角色贴地）
        /// </summary>
        public const float GROUNDED_VELOCITY = -0.5f;
    }
}

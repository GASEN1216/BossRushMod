// ============================================================================
// GoblinNPCConstants.cs - 哥布林NPC相关常量
// ============================================================================
// 模块说明：
//   统一管理哥布林NPC相关的魔法数字，便于修改和维护
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

namespace BossRush.Constants
{
    /// <summary>
    /// 哥布林NPC相关常量
    /// 包含名字标签、距离阈值、待机时间、气泡显示等配置
    /// </summary>
    public static class GoblinNPCConstants
    {
        // ============================================================================
        // 名字标签配置
        // ============================================================================
        
        /// <summary>
        /// 名字标签高度（头顶上方，与快递员一致）
        /// </summary>
        public const float NAME_TAG_HEIGHT = 2.3f;
        
        // ============================================================================
        // 距离阈值配置（单位：米）
        // ============================================================================
        
        /// <summary>
        /// 急停动画触发距离（距离玩家此距离时播放急停动画）
        /// </summary>
        public const float BRAKE_ANIMATION_DISTANCE = 4f;
        
        /// <summary>
        /// 停止距离（距离玩家此距离时真正停下）
        /// </summary>
        public const float STOP_DISTANCE = 1f;
        
        /// <summary>
        /// 故事对话触发距离（玩家在此距离内时触发故事对话）
        /// </summary>
        public const float STORY_DIALOGUE_TRIGGER_DISTANCE = 3f;
        
        // ============================================================================
        // 待机时间配置（单位：秒）
        // ============================================================================
        
        /// <summary>
        /// 急停后待机时间
        /// </summary>
        public const float IDLE_DURATION_AFTER_STOP = 3f;
        
        /// <summary>
        /// 默认对话停留时间
        /// </summary>
        public const float DEFAULT_DIALOGUE_STAY_DURATION = 10f;
        
        // ============================================================================
        // 气泡显示配置
        // ============================================================================
        
        /// <summary>
        /// 气泡Y轴偏移量（相对于名字标签）
        /// </summary>
        public const float BUBBLE_OFFSET_Y = 0.3f;
        
        /// <summary>
        /// 气泡动画Y轴偏移量
        /// </summary>
        public const float BUBBLE_ANIMATION_OFFSET_Y = 0.8f;
        
        /// <summary>
        /// 气泡显示持续时间
        /// </summary>
        public const float BUBBLE_DURATION = 2f;
    }
}

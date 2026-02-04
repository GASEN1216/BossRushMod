// ============================================================================
// AffinityConfig.cs - 好感度系统全局配置
// ============================================================================
// 模块说明：
//   定义好感度系统的全局常量和默认配置。
//   各NPC可通过实现 INPCAffinityConfig 接口覆盖这些默认值。
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 好感度系统全局配置
    /// </summary>
    public static class AffinityConfig
    {
        // ============================================================================
        // 全局默认值
        // ============================================================================
        
        /// <summary>
        /// 默认最大好感度点数
        /// </summary>
        public const int DEFAULT_MAX_POINTS = 1000;
        
        /// <summary>
        /// 默认每级所需点数
        /// </summary>
        public const int DEFAULT_POINTS_PER_LEVEL = 100;
        
        /// <summary>
        /// 默认最大等级
        /// </summary>
        public const int DEFAULT_MAX_LEVEL = 10;
        
        /// <summary>
        /// 默认礼物好感度增益
        /// </summary>
        public const int DEFAULT_GIFT_VALUE = 20;
        
        // ============================================================================
        // 每日衰减配置
        // ============================================================================
        
        /// <summary>
        /// 每日不互动时的好感度衰减值
        /// 如果玩家当天没有与NPC聊天或送礼，第二天会扣除此值
        /// </summary>
        public const int DAILY_DECAY_AMOUNT = 15;
        
        /// <summary>
        /// 是否启用每日衰减
        /// </summary>
        public const bool ENABLE_DAILY_DECAY = true;
        
        // ============================================================================
        // 存档配置
        // ============================================================================
        
        /// <summary>
        /// Mod名称（用于 ModConfigAPI）
        /// </summary>
        public const string MOD_NAME = "BossRush";
        
        /// <summary>
        /// 存档键名
        /// </summary>
        public const string SAVE_KEY = "NPCAffinity";
    }
}

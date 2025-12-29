// ============================================================================
// DragonBreathConfig.cs - 龙息武器配置
// ============================================================================
// 模块说明：
//   定义龙息武器的所有可配置参数
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 龙息武器配置常量
    /// </summary>
    public static class DragonBreathConfig
    {
        // ========== 武器ID ==========
        
        /// <summary>
        /// 龙息武器TypeID
        /// </summary>
        public const int WEAPON_TYPE_ID = 500005;
        
        // ========== Buff配置 ==========
        
        /// <summary>
        /// 龙焰灼烧Buff ID
        /// </summary>
        public const int BUFF_ID = 500006;
        
        /// <summary>
        /// Buff触发概率 (50%)
        /// </summary>
        public const float TRIGGER_CHANCE = 0.5f;
        
        /// <summary>
        /// Buff基础名（用于EquipmentFactory查找，对应 dragon_Buff）
        /// </summary>
        public const string BUFF_BASE_NAME = "dragon";
        
        // ========== 本地化键 ==========
        
        /// <summary>
        /// 武器名称本地化键
        /// </summary>
        public const string LOC_KEY_WEAPON_NAME = "DragonBreath_Name";
        
        /// <summary>
        /// 武器描述本地化键
        /// </summary>
        public const string LOC_KEY_WEAPON_DESC = "DragonBreath_Desc";
        
        /// <summary>
        /// Buff名称本地化键
        /// </summary>
        public const string LOC_KEY_BUFF_NAME = "DragonBurn_Name";
        
        /// <summary>
        /// Buff描述本地化键
        /// </summary>
        public const string LOC_KEY_BUFF_DESC = "DragonBurn_Desc";
    }
}

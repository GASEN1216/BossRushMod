// ============================================================================
// ThunderRingConfig.cs - 雷电戒指配置
// ============================================================================
// 模块说明：
//   定义雷电戒指的属性和运行时参数
//   核心机制：受击时叠加伤害增益，典型的反制型装备
//   只有在你真的挨打时才会变强
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 雷电戒指配置
    /// </summary>
    public static class ThunderRingConfig
    {
        // ========== 本地化 ==========
        public const string DisplayNameCN = "雷电戒指";
        public const string DisplayNameEN = "Thunder Ring";
        public const string DescriptionCN = "一枚蕴含雷霆之力的戒指，每次受到伤害都会积蓄电能。电能叠满时，你的下一次攻击将附带强力雷击。\n<color=#FFD54F>【蓄雷】</color>每次受击叠加1层电能（最多5层），持续8秒。\n<color=#FFECB3>【雷霆释放】</color>满层时下次攻击额外造成40点雷电伤害并消耗所有层数。\n<color=#FFF9C4>【代价】</color>你必须挨打才能变强。";
        public const string DescriptionEN = "A ring imbued with thunder. Each hit taken charges it with electricity. At full charge, your next attack unleashes a powerful lightning strike.\n<color=#FFD54F>[Charge]</color> Each hit taken adds 1 charge (max 5), lasting 8s.\n<color=#FFECB3>[Thunder Release]</color> At max charges, next attack deals 40 bonus lightning damage and consumes all charges.\n<color=#FFF9C4>[Trade-off]</color> You must take hits to power up.";

        // ========== 物品属性 ==========
        public const int ItemQuality = 5;

        // ========== 运行时参数 ==========

        /// <summary>
        /// 最大电能层数
        /// </summary>
        public const int MaxCharges = 5;

        /// <summary>
        /// 电能持续时间（秒），超时清零
        /// </summary>
        public const float ChargeDuration = 8f;

        /// <summary>
        /// 满层释放伤害
        /// </summary>
        public const float ReleaseDamage = 40f;

        /// <summary>
        /// 受击冷却（秒），防止连续受击瞬间叠满
        /// </summary>
        public const float ChargeCooldown = 0.3f;

        /// <summary>
        /// 日志前缀
        /// </summary>
        public const string LogPrefix = "[ThunderRing]";
    }
}

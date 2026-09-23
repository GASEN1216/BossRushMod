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
        public const string DescriptionCN = "挨一下打，戒指就攒一点电；攒满了，你下一击就带雷。\n<color=#FFD54F>【蓄雷】</color>每次被攻击命中叠加1层电能（最多5层），持续8秒；灼烧、中毒一类的持续伤害不计。\n<color=#FFECB3>【雷霆释放】</color>满层时下次命中敌人额外造成40点雷电伤害，外加这一击实际伤害的60%，并消耗所有层数。\n<color=#FFF9C4>【代价】</color>你必须挨打才能变强。\n<color=#BBBBBB>来源：三枪哥 掉落 20% / 叮当的小店（好感 5 级）</color>";
        public const string DescriptionEN = "Every hit you take charges the ring a little. Once it's full, your next attack comes with a lightning strike.\n<color=#FFD54F>[Charge]</color> Each hit taken adds 1 charge (max 5), lasting 8s. Damage over time such as burning or poison does not charge it.\n<color=#FFECB3>[Thunder Release]</color> At max charges, your next hit on an enemy deals 40 bonus lightning damage plus 60% of that hit's actual damage, and consumes all charges.\n<color=#FFF9C4>[Trade-off]</color> You must take hits to power up.\n<color=#BBBBBB>Source: 20% drop from Triple-Shot Man / Dingdang's Shop (Affinity 5)</color>";

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
        /// 满层释放的固定伤害底
        /// </summary>
        public const float ReleaseDamage = 40f;

        /// <summary>
        /// 满层释放的成长项：触发这一击的**实际伤害**的比例。
        ///
        /// 为什么要有这一项（2026-09-18 拍板，好玩优先）：
        ///   原实现是固定 40 点，且要先挨满 5 下、8 秒内用掉。一把中期步枪单发实伤就在 20~30，
        ///   满层奖励只值一发多子弹，却要占掉一个图腾槽（对手是能飞的腾云驾雾与逆鳞）——
        ///   机制成立但没人会为它让出槽位，属于「加了但不会有人用」的内容。
        ///   改成「固定底 + 这一击实伤比例」后，它变成「攒满就换大伤害武器打一下」的主动决策，
        ///   和 Wiki 里那句「建议配合高暴击/高单次伤害武器」终于对得上。
        /// 回退办法：把本常量设为 0f，行为逐字回到旧版。
        /// 取 0.60：霰弹枪一次开火的多个弹丸只会消耗在第一颗上，比例过高会让霰弹亏太多；
        ///   0.6 让单发高伤武器拿到明显收益，又不会把一击变成两击。
        /// </summary>
        public const float ReleaseHitDamageRatio = 0.60f;

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

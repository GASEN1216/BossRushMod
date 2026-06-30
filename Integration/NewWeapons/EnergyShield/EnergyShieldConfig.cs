// ============================================================================
// EnergyShieldConfig.cs - 能量盾配置
// ============================================================================
// 模块说明：
//   定义能量盾的属性和运行时参数
//   核心机制：正面受击回补部分伤害，被包围时价值暴跌
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 能量盾配置
    /// </summary>
    public static class EnergyShieldConfig
    {
        // ========== 本地化 ==========
        public const string DisplayNameCN = "能量盾";
        public const string DisplayNameEN = "Energy Shield";
        public const string DescriptionCN = "一面由纯能量凝聚的护盾，能够吸收正面来袭的部分伤害并转化为生命值。但它只能防护前方，被包围时几乎无用。\n<color=#64B5F6>【正面吸收】</color>正面受击时回复受到伤害的30%生命值。\n<color=#90CAF9>【弱点】</color>侧面和背面攻击无法触发吸收。";
        public const string DescriptionEN = "A shield condensed from pure energy that absorbs frontal damage and converts it to health. It only protects the front, making it nearly useless when surrounded.\n<color=#64B5F6>[Frontal Absorption]</color> Recover 30% of frontal damage as HP.\n<color=#90CAF9>[Weakness]</color> Side and rear attacks cannot trigger absorption.";

        // ========== 物品属性 ==========
        public const int ItemQuality = 5;

        // ========== 运行时参数 ==========

        /// <summary>
        /// 正面受击回复比例（30%）
        /// </summary>
        public const float FrontalAbsorptionRate = 0.3f;

        /// <summary>
        /// 正面判定角度（前方 ±60 度内算正面）
        /// </summary>
        public const float FrontalAngleThreshold = 60f;

        /// <summary>
        /// 触发冷却（秒），防止连续触发过于强力
        /// </summary>
        public const float TriggerCooldown = 0.5f;

        /// <summary>
        /// 单次最大回复量（防止极端情况）
        /// </summary>
        public const float MaxHealPerTrigger = 25f;

        /// <summary>
        /// 提供的护甲加成
        /// </summary>
        public const float BodyArmorBonus = 3f;

        /// <summary>
        /// 日志前缀
        /// </summary>
        public const string LogPrefix = "[EnergyShield]";
    }
}

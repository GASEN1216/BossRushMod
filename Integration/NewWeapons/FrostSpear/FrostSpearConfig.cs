// ============================================================================
// FrostSpearConfig.cs - 冰霜长矛配置
// ============================================================================
// 模块说明：
//   定义冰霜长矛的属性和参数
//   核心机制：中距离冰属性攻击，附带减速控制，换掉纯爆发
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 冰霜长矛配置
    /// </summary>
    public static class FrostSpearConfig
    {
        // ========== 本地化 ==========
        public const string DisplayNameCN = "冰霜长矛";
        public const string DisplayNameEN = "Frost Spear";
        public const string DescriptionCN = "一柄凝结着永冻之力的长矛，每次刺击都会在敌人身上留下寒霜印记。虽然爆发力不如其他武器，但稳定的减速效果让你始终掌握安全距离。\n<color=#4FC3F7>【寒霜刺击】</color>攻击100%附带冰冻减速。\n<color=#81D4FA>【安全距离】</color>攻击范围2.4米，中距离控场。\n<color=#B3E5FC>【代价】</color>暴击率和暴击伤害较低。";
        public const string DescriptionEN = "A spear crystallized with permafrost. Each thrust leaves a frost mark on enemies. While lacking burst damage, the consistent slow keeps you at a safe distance.\n<color=#4FC3F7>[Frost Thrust]</color> 100% chance to apply freeze slow.\n<color=#81D4FA>[Safe Distance]</color> 2.4m attack range for mid-range control.\n<color=#B3E5FC>[Trade-off]</color> Low crit rate and crit damage.";

        // ========== 物品属性 ==========
        public const int ItemQuality = 5;

        // ========== 近战 Stats ==========
        public const float Damage = 32f;           // 中等伤害
        public const float AttackSpeed = 1.3f;     // 中等攻速
        public const float AttackRange = 2.4f;     // 长距离（长矛特性）
        public const float CritRate = 0.03f;       // 低暴击（代价）
        public const float CritDamageFactor = 1.2f; // 低暴击伤害（代价）
        public const float ArmorPiercing = 3f;
        public const float StaminaCost = 8f;
        public const float DealDamageTime = 0.09f;
        public const float BleedChance = 0f;
        public const float MoveSpeedMultiplier = 1.04f;
        public const float BlockBullet = 0.5f;     // 长矛可以格挡

        // ========== 冰冻参数 ==========

        /// <summary>
        /// 冰冻触发概率（100%）
        /// </summary>
        public const float FreezeChance = 1f;

        /// <summary>
        /// 提供的寒冷防护加成
        /// </summary>
        public const float ColdProtectionBonus = 1f;

        /// <summary>
        /// 日志前缀
        /// </summary>
        public const string LogPrefix = "[FrostSpear]";
    }
}

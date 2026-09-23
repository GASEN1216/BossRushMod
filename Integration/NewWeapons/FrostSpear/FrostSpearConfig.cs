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
        public const string DescriptionCN = "矛尖常年挂霜，扎到谁谁就慢下来。爆发不高，但能一直把敌人挡在够不着你的地方。\n<color=#4FC3F7>【寒霜刺击】</color>攻击100%附带冰冻减速。\n<color=#81D4FA>【安全距离】</color>攻击范围2.4米，中距离控场。\n<color=#B3E5FC>【代价】</color>暴击率和暴击伤害较低。\n<color=#BBBBBB>来源：大冰冰 掉落 20% / 叮当的小店（好感 5 级）</color>";
        public const string DescriptionEN = "The tip never thaws, and whatever it pokes slows down. Low burst, but it keeps enemies out of reach.\n<color=#4FC3F7>[Frost Thrust]</color> 100% chance to apply freeze slow.\n<color=#81D4FA>[Safe Distance]</color> 2.4m attack range for mid-range control.\n<color=#B3E5FC>[Trade-off]</color> Low crit rate and crit damage.\n<color=#BBBBBB>Source: 20% drop from Big Ice / Dingdang's Shop (Affinity 5)</color>";

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

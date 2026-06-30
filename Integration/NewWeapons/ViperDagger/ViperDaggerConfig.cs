// ============================================================================
// ViperDaggerConfig.cs - 毒蛇匕首配置
// ============================================================================
// 模块说明：
//   定义毒蛇匕首的属性、本地化文本和叠毒参数
//   核心机制：近身命中叠毒，叠层越多伤害越高，贴脸滚雪球
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>
    /// 毒蛇匕首配置
    /// </summary>
    public static class ViperDaggerConfig
    {
        // ========== 本地化 ==========
        public const string DisplayNameCN = "毒蛇匕首";
        public const string DisplayNameEN = "Viper Dagger";
        public const string DescriptionCN = "一柄浸泡在蛇毒中的短刃，每次命中都会在敌人体内注入毒素。毒素可叠加至5层，层数越高伤害越猛。\n<color=#7CFC00>【蛇毒注入】</color>命中敌人叠加中毒，最多5层。\n<color=#ADFF2F>【毒性爆发】</color>满层时额外造成一次爆发伤害。";
        public const string DescriptionEN = "A short blade soaked in serpent venom. Each strike injects toxin into the target. Poison stacks up to 5 layers with increasing damage.\n<color=#7CFC00>[Venom Injection]</color> Hits apply poison, stacking up to 5.\n<color=#ADFF2F>[Toxic Burst]</color> At max stacks, deals bonus burst damage.";

        // ========== 物品属性 ==========
        public const int ItemQuality = 5;

        // ========== 近战 Stats ==========
        public const float Damage = 22f;           // 基础伤害偏低（靠叠毒补偿）
        public const float AttackSpeed = 2.1f;     // 攻速快（匕首特性）
        public const float AttackRange = 1.4f;     // 短距离（贴脸）
        public const float CritRate = 0.08f;
        public const float CritDamageFactor = 1.5f;
        public const float ArmorPiercing = 2f;
        public const float StaminaCost = 4f;       // 低体力消耗（快速连击）
        public const float DealDamageTime = 0.06f;
        public const float BleedChance = 0f;       // 不流血，改为叠毒
        public const float MoveSpeedMultiplier = 1.12f; // 持匕首移速加成
        public const float BlockBullet = 0.2f;

        // ========== 叠毒参数 ==========

        /// <summary>
        /// 最大叠层数
        /// </summary>
        public const int MaxPoisonLayers = 5;

        /// <summary>
        /// 满层爆发伤害（一次性）
        /// </summary>
        public const float BurstDamageOnMaxStack = 35f;

        /// <summary>
        /// 毒素持续时间（秒），每次命中刷新
        /// </summary>
        public const float PoisonDuration = 6f;

        /// <summary>
        /// 日志前缀
        /// </summary>
        public const string LogPrefix = "[ViperDagger]";
    }
}

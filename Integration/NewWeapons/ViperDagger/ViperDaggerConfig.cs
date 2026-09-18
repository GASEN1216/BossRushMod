// ============================================================================
// ViperDaggerConfig.cs - 毒蛇匕首配置
// ============================================================================
// 模块说明：
//   定义毒蛇匕首的属性、本地化文本和叠毒参数
//   核心机制：近身命中叠毒，满 5 层引爆，引爆伤害随这 5 刀打出的实伤一起长——
//   刀越强、爆得越疼，贴脸滚雪球
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
        public const string DescriptionCN = "一柄浸泡在蛇毒中的短刃，每次命中都会在敌人体内注入毒素。毒素可叠加至5层，满层引爆。\n<color=#7CFC00>【蛇毒注入】</color>命中敌人叠加中毒，最多5层。\n<color=#ADFF2F>【毒性爆发】</color>叠满5层立即引爆，造成35点毒属性伤害，外加这5次命中实际伤害的20%，并清空层数。爆发伤害不吃暴击。\n<color=#BBBBBB>来源：典狱长 掉落 20% / 叮当的小店（好感 5 级）</color>";
        public const string DescriptionEN = "A short blade soaked in serpent venom. Each strike injects toxin into the target. Poison stacks up to 5, then detonates.\n<color=#7CFC00>[Venom Injection]</color> Hits apply poison, stacking up to 5.\n<color=#ADFF2F>[Toxic Burst]</color> The 5th stack detonates for 35 poison damage plus 20% of the damage those 5 hits actually dealt, then clears all stacks. The burst itself cannot crit.\n<color=#BBBBBB>Source: 20% drop from Warden / Dingdang's Shop (Affinity 5)</color>";

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
        /// 满层爆发的固定伤害底（一次性）
        /// </summary>
        public const float BurstDamageOnMaxStack = 35f;

        /// <summary>
        /// 满层爆发的成长项：本轮 5 次命中对该目标累计**实际伤害**的比例。
        ///
        /// 为什么要有这一项（2026-09-18 拍板，好玩优先）：
        ///   原实现是固定 35 点。基础面板下它约等于 +32% DPS，尚可；但暴击、词缀、套装
        ///   堆起来之后玩家单刀能打出几倍于 22 的实伤，固定 35 就退化成零头，
        ///   「叠毒滚雪球」的卖点在中后期彻底失效——典型的「机制在、但没人会为它选这把刀」。
        ///   改成「固定底 + 累计实伤比例」后，下限与旧版逐字一致（Wiki 承诺的 35 点不变），
        ///   上限随玩家自己的 build 成长，不需要玩家看任何数字。
        /// 回退办法：把本常量设为 0f，行为逐字回到旧版。
        /// 取 0.20：基础面板下 5 刀累计实伤约 75~110，爆发落在 50~57，相对旧版 +45%~+63%，
        ///   与「1.4 米贴脸、22 面板」的高风险定位相称；不设上限，因为它本就被玩家自己的输出封顶。
        /// </summary>
        public const float BurstAccumulatedDamageRatio = 0.20f;

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

// ============================================================================
// SummonStaffConfig.cs - 召唤法杖配置
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityConfig，定义右键技能「灵魂召唤」参数
//   核心机制：召出短命友军帮忙压场，自身直接伤害偏弱
// ============================================================================

using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 召唤法杖配置（右键技能参数）
    /// </summary>
    public class SummonStaffConfig : EquipmentAbilityConfig
    {
        // ========== 物品基础信息 ==========

        public override int ItemTypeId => NewWeaponIds.SummonStaffTypeId;
        public override string DisplayNameCN => "召唤法杖";
        public override string DisplayNameEN => "Summoning Staff";
        public override string DescriptionCN => "一根刻满古老符文的法杖，能够撕裂空间召唤短暂存在的灵魂战士。法杖本身攻击力平庸，但召唤物可以有效分散敌人火力。\n<color=#BA68C8>【灵魂召唤】</color>右键召唤3只灵魂战士，持续15秒后消散。冷却12秒。\n<color=#CE93D8>【代价】</color>自身近战伤害较低。";
        public override string DescriptionEN => "A staff carved with ancient runes that tears through space to summon ephemeral soul warriors. The staff itself deals modest damage, but summons effectively draw enemy fire.\n<color=#BA68C8>[Soul Summon]</color> Right-click to summon 3 soul warriors lasting 15s. 12s cooldown.\n<color=#CE93D8>[Trade-off]</color> Low personal melee damage.";
        public override int ItemQuality => 5;
        public override string[] ItemTags => new string[] { "Weapon", "MeleeWeapon", "DontDropOnDeadInSlot", "Special" };
        public override string IconAssetName => NewWeaponIds.SummonStaffIconAssetName;

        // ========== 右键技能参数 ==========

        public override float CooldownTime => 12f;
        public override float StartupStaminaCost => 12f;
        public override float StaminaDrainPerSecond => 0f;

        // ========== 近战 Stats（偏弱，靠召唤物补偿） ==========

        public const float Damage = 18f;
        public const float AttackSpeed = 1.2f;
        public const float AttackRange = 1.8f;
        public const float CritRate = 0.04f;
        public const float CritDamageFactor = 1.3f;
        public const float ArmorPiercing = 1f;
        public const float StaminaCost = 6f;
        public const float DealDamageTime = 0.1f;
        public const float BleedChance = 0f;
        public const float MoveSpeedMultiplier = 1.05f;
        public const float BlockBullet = 0.3f;

        // ========== 召唤参数 ==========

        /// <summary>
        /// 召唤数量
        /// </summary>
        public const int SummonCount = 3;

        /// <summary>
        /// 召唤半径（米）
        /// </summary>
        public const float SummonRadius = 2.2f;

        /// <summary>
        /// 召唤物生命值
        /// </summary>
        public const float SummonHealth = 80f;

        /// <summary>
        /// 召唤物存活时间（秒）
        /// </summary>
        public const float SummonLifetime = 15f;

        /// <summary>
        /// 召唤物预设名（复用僵尸预设）
        /// </summary>
        public const string SummonPresetName = "Cname_Zombie";

        /// <summary>
        /// 技能总持续时间（秒）
        /// </summary>
        public const float TotalActionDuration = 1.2f;

        // ========== 音效 ==========

        public override string StartSFX => null;
        public override string LoopSFX => null;
        public override string EndSFX => null;

        // ========== 日志 ==========

        public override string LogPrefix => "[SummonStaff]";
    }
}

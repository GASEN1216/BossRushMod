// ============================================================================
// SummonStaffConfig.cs - 召唤法杖配置
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityConfig，定义右键技能「灵魂投射」参数
//   核心机制：朝瞄准方向投放 3 只短命灵魂战士，它们消散或阵亡时原地爆裂
//   定位与霜之哀伤（5 只常驻亡灵、贴身缠斗）明确错开，理由见 SummonStaffAction 文件头
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
        public override string DescriptionCN => "一挥杖，隔着几米就把灵魂战士直接丢进敌群。杖本身打人不疼，但战士能替你挨枪子，散掉时还会炸开。\n<color=#BA68C8>【灵魂投射】</color>右键在瞄准方向前方 6 米处召出 3 只灵魂战士，持续 12 秒。冷却 12 秒。\n<color=#CE93D8>【灵魂爆裂】</color>灵魂战士消散或阵亡时原地爆开，对 3 米内敌人造成 45 点灵魂伤害，不伤你与友军。\n<color=#CE93D8>【代价】</color>自身近战伤害较低。\n<color=#BBBBBB>来源：大兴兴 掉落 20% / 叮当的小店（好感 5 级）</color>";
        public override string DescriptionEN => "One swing drops soul warriors straight into the enemy pack a few metres away. The staff itself hits like a stick, but the warriors draw fire and explode when they go.\n<color=#BA68C8>[Soul Projection]</color> Right-click to summon 3 soul warriors 6m ahead of your aim, lasting 12s. 12s cooldown.\n<color=#CE93D8>[Soul Burst]</color> When a warrior fades or falls it bursts for 45 ghost damage in a 3m radius. Never harms you or your allies.\n<color=#CE93D8>[Trade-off]</color> Low personal melee damage.\n<color=#BBBBBB>Source: 20% drop from Big Xing / Dingdang's Shop (Affinity 5)</color>";
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
        /// 投放点距玩家的距离（米）。朝瞄准方向量取；撞墙会自动缩短，
        /// 设为 0 则退回「召在脚边」的旧行为（回退开关）。
        /// 取 6 米：比法杖自己的攻击范围（1.8 米）远得多，投放才成为一次真正的决策；
        /// 又在玩家一眼能看清的范围内，不至于扔到视野外。
        /// </summary>
        public const float PlacementDistance = 6f;

        /// <summary>
        /// 召唤半径（米）：三只灵魂战士围着投放点等角散开的半径
        /// </summary>
        public const float SummonRadius = 2.2f;

        /// <summary>
        /// 召唤物生命值
        /// </summary>
        public const float SummonHealth = 80f;

        /// <summary>
        /// 召唤物存活时间（秒）。与冷却对齐成 12 秒：一组消散（并炸开）时正好能投下一组，
        /// 形成「投放 → 吸火力 → 爆裂 → 再投放」的节奏，而不是无脑挂着三个小弟。
        /// </summary>
        public const float SummonLifetime = 12f;

        /// <summary>
        /// 灵魂爆裂伤害。设为 0 即关闭爆裂（回退开关）。
        /// 取 45：单只约等于法杖自身两刀半，三只全炸 135；要吃满得让敌人贴着召唤物，
        /// 比霜之哀伤那 5 只持续输出更看走位，也更容易空。
        /// </summary>
        public const float SoulBurstDamage = 45f;

        /// <summary>
        /// 灵魂爆裂半径（米）
        /// </summary>
        public const float SoulBurstRadius = 3f;

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

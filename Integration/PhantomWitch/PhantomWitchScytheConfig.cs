// ============================================================================
// PhantomWitchScytheConfig.cs - 幽灵女巫大镰配置（右键技能「诅咒领域」参数）
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityConfig，定义大镰的显示名/描述/品质/标签
//   以及右键技能「诅咒领域」的全部参数。
//   与 FenHuangHalberdConfig / FrostmourneConfig 使用一致的范式。
// ============================================================================

using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫大镰配置（右键技能：诅咒领域）
    /// </summary>
    public class PhantomWitchScytheConfig : EquipmentAbilityConfig
    {
        // ========== 物品基础信息 ==========

        public override int ItemTypeId => PhantomWitchScytheIds.WeaponTypeId;
        public override string DisplayNameCN => "噬魂挽歌";
        public override string DisplayNameEN => "Soulreaper's Requiem";

        public override string DescriptionCN =>
            "镰刃拖着一缕冷焰，挥过时像有人在耳边低语。\n" +
            "<color=#C88BFF>【噬魂之力】</color>幽灵属性攻击，50% 概率施加诅咒，每层 -30% 移速，最多 3 层。\n" +
            "<color=#9B4DCA>【挽歌领域】</color>右键在脚下展开符文阵，持续4秒。\n阵内敌人每0.5秒受到诅咒和幽能伤害。冷却12秒。";

        public override string DescriptionEN =>
            "A cold flame trails the blade. Each swing brings a whisper close to your ear.\n" +
            "<color=#C88BFF>[Soulreaving Power]</color> Ghost-element attacks with a 50% chance to inflict Curse (-30% move speed per stack, up to 3 stacks).\n" +
            "<color=#9B4DCA>[Requiem Realm]</color> Right-click to unfurl a violet rune circle beneath you for 4s. Enemies inside take ghost damage and receive a curse stack every 0.5s. 12s cooldown.";

        public override int ItemQuality => 6;

        public override string[] ItemTags => new string[]
        {
            "Weapon",
            "MeleeWeapon",
            "DontDropOnDeadInSlot",
            "Special",
            "PhantomWitch"
        };

        public override string IconAssetName => PhantomWitchScytheIds.IconAssetName;

        /// <summary>
        /// 基础攻击范围（与 WeaponStats 中的 AttackRange 保持一致）
        /// </summary>
        public const float BaseAttackRange = 2.22f;
        public const float NormalAttackCurseChance = 0.5f;

        // ========== 右键技能「诅咒领域」参数 ==========

        /// <summary>
        /// 右键技能冷却（秒）
        /// </summary>
        public override float CooldownTime => 12f;

        /// <summary>
        /// 右键技能启动体力消耗
        /// </summary>
        public override float StartupStaminaCost => 20f;

        /// <summary>
        /// 不自动持续消耗体力
        /// </summary>
        public override float StaminaDrainPerSecond => 0f;

        /// <summary>
        /// 前摇时间（秒）
        /// </summary>
        public const float RealmCastTime = 0.25f;

        /// <summary>
        /// 动作总持续时间（包含前摇和收招）
        /// </summary>
        public const float RealmActionDuration = 0.6f;

        /// <summary>
        /// 领域半径（米）
        /// </summary>
        public const float RealmRadius = 4f;

        /// <summary>
        /// 领域持续时间（秒）
        /// </summary>
        public const float RealmDuration = 4f;

        /// <summary>
        /// 伤害判定间隔（秒）
        /// </summary>
        public const float RealmDamageInterval = 0.5f;

        /// <summary>
        /// 每次判定单次伤害
        /// </summary>
        public const float RealmDamagePerTick = 22f;

        /// <summary>
        /// 领域视觉符文阵的环形粒子数量
        /// </summary>
        public const int RealmRuneSegments = 24;

        /// <summary>
        /// 领域中心视觉特效高度
        /// </summary>
        public const float RealmVisualHeight = 0.15f;

        // ========== 音效 ==========

        public override string StartSFX => null;
        public override string LoopSFX => null;
        public override string EndSFX => null;

        // ========== 日志 ==========

        public override string LogPrefix => "[PhantomWitchScythe]";
    }
}

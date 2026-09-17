// ============================================================================
// FrostmourneConfig.cs - 霜之哀伤配置
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityConfig，定义右键技能「亡灵召唤」参数
// ============================================================================

using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 霜之哀伤配置（右键技能参数）
    /// </summary>
    public class FrostmourneConfig : EquipmentAbilityConfig
    {
        // ========== 物品基础信息 ==========

        public override int ItemTypeId => FrostmourneIds.WeaponTypeId;
        public override string DisplayNameCN => "霜之哀伤";
        public override string DisplayNameEN => "Frostmourne";
        public override string DescriptionCN => "剑刃覆着蓝霜，靠近就能听到低语。\n<color=#4FC3F7>【寒冰之力】</color>冰属性攻击，寒冷防护+2。\n<color=#81D4FA>【亡灵召唤】</color>右键召唤5只亡灵仆从。冷却10秒。";
        public override string DescriptionEN => "Blue frost coats the blade. It whispers when held close.\n<color=#4FC3F7>[Frost Power]</color> Ice attacks. Cold Protection +2.\n<color=#81D4FA>[Undead Summoning]</color> Right-click to summon 5 undead servants. Cooldown: 10s.";
        public override int ItemQuality => 6;
        public override string[] ItemTags => new string[] { "Weapon", "MeleeWeapon", "DontDropOnDeadInSlot", "Special", "DragonKing" };
        public override string IconAssetName => FrostmourneIds.IconAssetName;

        /// <summary>
        /// 基础攻击范围
        /// </summary>
        public const float BaseAttackRange = 2.0f;

        // ========== 右键技能「亡灵召唤」参数 ==========

        /// <summary>
        /// 右键技能冷却（秒）
        /// </summary>
        public override float CooldownTime => 10f;

        /// <summary>
        /// 右键技能体力消耗
        /// </summary>
        public override float StartupStaminaCost => 14f;

        /// <summary>
        /// 不自动持续消耗体力
        /// </summary>
        public override float StaminaDrainPerSecond => 0f;

        // ========== 召唤参数 ==========

        /// <summary>
        /// 召唤僵尸数量
        /// </summary>
        public const int SummonCount = 5;

        /// <summary>
        /// 召唤半径（米）
        /// </summary>
        public const float SummonRadius = 2.5f;

        /// <summary>
        /// 僵尸生命值
        /// </summary>
        public const float ZombieHealth = 100f;

        /// <summary>
        /// 僵尸预设名
        /// </summary>
        public const string ZombiePresetName = "Cname_Zombie";

        /// <summary>
        /// 技能总持续时间（秒）
        /// </summary>
        public const float TotalActionDuration = 1.5f;

        // ========== 音效 ==========

        public override string StartSFX => null;
        public override string LoopSFX => null;
        public override string EndSFX => null;

        // ========== 日志 ==========

        public override string LogPrefix => "[Frostmourne]";
    }
}

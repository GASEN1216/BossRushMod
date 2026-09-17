// ============================================================================
// FenHuangHalberdConfig.cs - 焚皇断界戟配置
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityConfig，定义右键技能「龙皇裂地」参数
//   以及三段连招各段参数
// ============================================================================

using System;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟配置（右键技能 + 三段连招参数）
    /// </summary>
    public class FenHuangHalberdConfig : EquipmentAbilityConfig
    {
        // ========== 物品基础信息 ==========

        public override int ItemTypeId => FenHuangHalberdIds.WeaponTypeId;
        public override string DisplayNameCN => "焚皇断界戟";
        public override string DisplayNameEN => "Inferno Emperor's Realm-Breaking Halberd";
        public override string DescriptionCN => "龙核余焰锻成的重戟，龙纹间还渗着火。\n<color=#FF6347>【三段连招】</color>横扫、挑击、重劈。每击附带灼烧，末击拉拽敌人。\n<color=#FF4500>【龙皇裂地】</color>右键跃起砸落，向前引出六道火柱。\n<color=#FFD700>【龙焰印记】</color>攻击叠加印记，最多5层。右键砸落引爆，层数越多伤害越高。";
        public override string DescriptionEN => "A heavy halberd forged in a dragon core's embers. Fire seeps through its runes.\n<color=#FF6347>[3-Hit Combo]</color> Sweep, launch, slam. Each hit burns; the last pulls enemies in.\n<color=#FF4500>[Dragon Emperor Fissure]</color> Right-click to leap and slam, raising six fire pillars ahead.\n<color=#FFD700>[Dragon Flame Mark]</color> Attacks apply up to 5 marks. The slam detonates them; more marks deal more damage.";
        public override int ItemQuality => 7;
        public override string[] ItemTags => new string[] { "Weapon", "MeleeWeapon", "DontDropOnDeadInSlot", "Special", "DragonKing" };
        public override string IconAssetName => FenHuangHalberdIds.IconAssetName;

        /// <summary>
        /// 断界戟默认基础攻击范围。
        /// 连招判定需要基于这个值计算每段的范围偏移，避免直接覆写实时 Stat。
        /// </summary>
        public const float BaseAttackRange = 2.5f;

        // ========== 右键技能「龙皇裂地」参数 ==========

        /// <summary>
        /// 右键技能冷却（秒）
        /// </summary>
        public override float CooldownTime => 5f;

        /// <summary>
        /// 右键技能体力消耗
        /// </summary>
        public override float StartupStaminaCost => 20f;

        /// <summary>
        /// 不自动持续消耗体力
        /// </summary>
        public override float StaminaDrainPerSecond => 0f;

        /// <summary>
        /// 前摇时间（秒）
        /// </summary>
        public const float FissureCastTime = 0.18f;

        public const float FissureLength = 8f;

        public const float LeapLandingRecoverTime = 0f;

        public const float LandingFireRingRadius = 1.8f;

        /// <summary>
        /// 砸落范围伤害
        /// </summary>
        public const float LandingImpactDamage = 80f;

        /// <summary>
        /// 砸落伤害半径
        /// </summary>
        public const float LandingImpactRadius = 3.5f;

        /// <summary>
        /// 火柱数量
        /// </summary>
        public const int FirePillarCount = 6;

        /// <summary>
        /// 火柱生成间隔时间（秒）
        /// </summary>
        public const float FirePillarInterval = 0.08f;

        /// <summary>
        /// 每个火柱的伤害
        /// </summary>
        public const float FirePillarDamage = 45f;

        /// <summary>
        /// 火柱持续时间（秒）
        /// </summary>
        public const float FirePillarDuration = 2f;

        /// <summary>
        /// 火柱伤害间隔（秒）
        /// </summary>
        public const float FirePillarDamageInterval = 0.5f;

        /// <summary>
        /// 火柱伤害半径
        /// </summary>
        public const float FirePillarRadius = 1.2f;

        /// <summary>
        /// 技能总持续时间（前摇 + 火柱释放时间）
        /// </summary>
        public const float MaxLeapTravelTime = 0.3f;

        public static float TotalActionDuration =>
            FissureCastTime +
            MaxLeapTravelTime +
            FirePillarCount * FirePillarInterval +
            LeapLandingRecoverTime;

        public const float ComboHitConfirmWindow = 0.35f;

        // ========== 爆燃参数 ==========

        /// <summary>
        /// 每层龙焰印记爆燃伤害
        /// </summary>
        public const float DetonationDamagePerMark = 30f;

        /// <summary>
        /// 爆燃特效持续时间
        /// </summary>
        public const float DetonationEffectDuration = 0.5f;

        // ========== 三段连招参数 ==========

        /// <summary>
        /// 连招窗口时间（秒），超时重置为第 1 段
        /// </summary>
        public const float ComboWindowTime = 0.75f;

        /// <summary>
        /// 第 1 段横扫：范围
        /// </summary>
        public const float Combo1Range = 2.8f;

        /// <summary>
        /// 第 1 段横扫：角度
        /// </summary>
        public const float Combo1Angle = 150f;

        /// <summary>
        /// 第 1 段横扫：伤害
        /// </summary>
        public const float Combo1Damage = 55f;

        /// <summary>
        /// 第 2 段上挑：范围
        /// </summary>
        public const float Combo2Range = 2.2f;

        /// <summary>
        /// 第 2 段上挑：角度
        /// </summary>
        public const float Combo2Angle = 90f;

        /// <summary>
        /// 第 2 段上挑：伤害
        /// </summary>
        public const float Combo2Damage = 65f;

        /// <summary>
        /// 第 2 段上挑：挑飞高度（米）
        /// </summary>
        public const float Combo2LaunchHeight = 1.5f;

        /// <summary>
        /// 第 3 段重劈：范围
        /// </summary>
        public const float Combo3Range = 3.3f;

        /// <summary>
        /// 第 3 段重劈：角度
        /// </summary>
        public const float Combo3Angle = 120f;

        /// <summary>
        /// 第 3 段重劈：伤害
        /// </summary>
        public const float Combo3Damage = 85f;

        /// <summary>
        /// 第 3 段重劈：拉扯距离（米）
        /// </summary>
        public const float Combo3PullDistance = 1.0f;

        /// <summary>
        /// 所有连招段数的火焰附加伤害
        /// </summary>
        public const float ComboFireDamageBonus = 15f;

        /// <summary>
        /// 所有连招段数的灼烧 Buff 持续时间（秒）
        /// </summary>
        public const float ComboBurnDuration = 2f;

        // ========== 龙焰印记参数 ==========

        /// <summary>
        /// 最大印记层数
        /// </summary>
        public const int MaxMarkStacks = 5;

        /// <summary>
        /// 印记持续时间（秒）
        /// </summary>
        public const float MarkDuration = 6f;

        // ========== 音效 ==========

        public override string StartSFX => null;  // 不使用自定义音效
        public override string LoopSFX => null;
        public override string EndSFX => null;

        // ========== 日志 ==========

        public override string LogPrefix => "[FenHuangHalberd]";
    }
}

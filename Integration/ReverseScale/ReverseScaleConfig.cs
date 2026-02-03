// ============================================================================
// ReverseScaleConfig.cs - 逆鳞图腾配置
// ============================================================================
// 模块说明：
//   定义逆鳞图腾的所有配置参数
//   继承自 EquipmentAbilityConfig，使用统一的配置系统
// ============================================================================

using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 逆鳞图腾配置类 - 统一所有逆鳞相关参数
    /// </summary>
    public class ReverseScaleConfig : EquipmentAbilityConfig
    {
        // 单例实例
        private static ReverseScaleConfig _instance;
        public static ReverseScaleConfig Instance
        {
            get
            {
                if (_instance == null) _instance = new ReverseScaleConfig();
                return _instance;
            }
        }

        private ReverseScaleConfig() { }

        // ========== 物品基础信息 ==========

        public override int ItemTypeId => TotemTypeId;

        public override string DisplayNameCN => "逆鳞";

        public override string DisplayNameEN => "Reverse Scale";

        public override string DescriptionCN => "龙之逆鳞，触之必怒。装备后在濒死时触发：恢复一定比例的生命值，并向四周发射多颗棱彩弹攻击敌人，随后图腾碎裂。";

        public override string DescriptionEN => "The forbidden scale of a dragon. When equipped, triggers upon near-death: restores a portion of health, fires multiple prismatic bolts at nearby enemies, then shatters.";

        public override int ItemQuality => 6;

        public override string[] ItemTags => new string[] { "Totem", "DontDropOnDeadInSlot" };

        public override string LogPrefix => "[ReverseScale]";

        // ========== 逆鳞效果参数 ==========

        /// <summary>
        /// 触发阈值（血量降至此值或以下时触发）
        /// </summary>
        public float TriggerHealthThreshold => 1f;

        /// <summary>
        /// 恢复血量比例（相对于最大血量）
        /// </summary>
        public float HealPercent => 0.5f;

        // ========== 棱彩弹参数（触之必怒）==========
        // 与龙王Boss的棱彩弹参数完全一致

        /// <summary>
        /// 棱彩弹数量
        /// </summary>
        public int PrismaticBoltCount => 8;

        /// <summary>
        /// 棱彩弹缩放（与龙王一致）
        /// </summary>
        public float PrismaticBoltScale => DragonKingConfig.PrismaticBoltScale;

        /// <summary>
        /// 棱彩弹速度（与龙王一致）
        /// </summary>
        public float PrismaticBoltSpeed => DragonKingConfig.PrismaticBoltSpeed;

        /// <summary>
        /// 棱彩弹伤害（与龙王一致）
        /// </summary>
        public float PrismaticBoltDamage => DragonKingConfig.PrismaticBoltDamage;

        /// <summary>
        /// 棱彩弹生命周期（与龙王一致）
        /// </summary>
        public float PrismaticBoltLifetime => DragonKingConfig.PrismaticBoltLifetime;

        /// <summary>
        /// 棱彩弹碰撞半径（与龙王一致）
        /// </summary>
        public float PrismaticBoltHitRadius => DragonKingConfig.ProjectileHitRadius;
        
        /// <summary>
        /// 棱彩弹追踪强度（与龙王一致）
        /// </summary>
        public float PrismaticBoltTrackingStrength => DragonKingConfig.PrismaticBoltTrackingStrength;
        
        /// <summary>
        /// 棱彩弹追踪持续时间（与龙王一致）
        /// </summary>
        public float PrismaticBoltTrackingDuration => DragonKingConfig.PrismaticBoltTrackingDuration;

        // ========== 能力参数（逆鳞无冷却，一次性触发）==========

        public override float CooldownTime => 0f;

        public override float StartupStaminaCost => 0f;

        public override float StaminaDrainPerSecond => 0f;

        // ========== 物品配置常量 ==========

        /// <summary>
        /// 图腾物品TypeID
        /// </summary>
        public const int TotemTypeId = 500013;

        /// <summary>
        /// 物品基础名（用于匹配 AssetBundle 中的 Prefab）
        /// </summary>
        public const string ItemBaseName = "ReverseScale_Lv1_Totem";

        /// <summary>
        /// 图腾装备槽名称前缀（游戏中实际槽位是 Totem1, Totem2）
        /// </summary>
        public const string TotemSlotPrefix = "Totem";

        // ========== 本地化键 ==========

        /// <summary>
        /// 物品显示名本地化键
        /// </summary>
        public const string LOC_KEY_DISPLAY = "BossRush_ReverseScale";

        /// <summary>
        /// 物品描述本地化键
        /// </summary>
        public const string LOC_KEY_DESC = "BossRush_ReverseScale_Desc";

        /// <summary>
        /// 气泡提示本地化键
        /// </summary>
        public const string LOC_KEY_BUBBLE = "BossRush_ReverseScale_Bubble";

        // ========== 属性键（用于 CustomData 字符对字符显示）==========

        /// <summary>
        /// 恢复生命值属性键
        /// </summary>
        public const string VAR_HEAL_PERCENT = "ReverseScale_HealPercent";

        /// <summary>
        /// 棱彩弹数量属性键
        /// </summary>
        public const string VAR_BOLT_COUNT = "ReverseScale_BoltCount";

        // ========== 气泡提示文本 ==========

        /// <summary>
        /// 气泡提示文本（中文）- 使用富文本让"逆鳞"显示为红色
        /// </summary>
        public const string BUBBLE_TEXT_CN = "<color=red>逆鳞</color>碎了...";

        /// <summary>
        /// 气泡提示文本（英文）- 使用富文本让"Reverse Scale"显示为红色
        /// </summary>
        public const string BUBBLE_TEXT_EN = "The <color=red>Reverse Scale</color> shattered...";
    }
}

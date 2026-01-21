// ============================================================================
// FlightConfig.cs - 飞行图腾配置
// ============================================================================
// 模块说明：
//   定义飞行1阶图腾的所有配置参数
//   继承自 EquipmentAbilityConfig，使用统一的配置系统
// ============================================================================

using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 飞行图腾配置类 - 统一所有飞行相关参数
    /// </summary>
    public class FlightConfig : EquipmentAbilityConfig
    {
        // 单例实例
        private static FlightConfig _instance;
        public static FlightConfig Instance
        {
            get
            {
                if (_instance == null) _instance = new FlightConfig();
                return _instance;
            }
        }

        private FlightConfig() { }

        // ========== 物品基础信息 ==========

        public override int ItemTypeId => TotemTypeIdBase;

        public override string DisplayNameCN => "腾云驾雾 I";

        public override string DisplayNameEN => "Cloud Soar I";

        public override string DescriptionCN => "尔等凡鸭怎知我俯瞰众生的疲惫";

        public override string DescriptionEN => "Mere ducks cannot fathom my exhaustion overlooking all beings";

        public override int ItemQuality => 6;

        public override string[] ItemTags => new string[] { "Totem", "DontDropOnDeadInSlot" };

        public override string IconAssetName => "birthday_cake";

        public override string LogPrefix => "[FlightTotem]";

        // ========== 飞行参数 ==========

        /// <summary>
        /// 最大向上速度（单位/秒）
        /// </summary>
        public float MaxUpwardSpeed => 10f;

        /// <summary>
        /// 加速到最大速度所需时间（秒）
        /// </summary>
        public float AccelerationTime => 0.3f;

        /// <summary>
        /// 向上加速度 = 最大速度 / 加速时间
        /// </summary>
        public float UpwardAcceleration => MaxUpwardSpeed / AccelerationTime;

        /// <summary>
        /// 滑翔时水平速度倍数（玩家移动速度的倍数）
        /// </summary>
        public float GlidingHorizontalSpeedMultiplier => 0.8f;

        /// <summary>
        /// 缓慢下落速度（负值，向下）
        /// </summary>
        public float SlowDescentSpeed => -0.8f;

        /// <summary>
        /// 缓慢下落时的体力消耗（每秒）
        /// </summary>
        public float SlowDescentStaminaDrainPerSecond => 30f;

        // ========== 能力参数 ==========

        public override float CooldownTime => 0.1f;

        public override float StartupStaminaCost => 5f;

        public override float StaminaDrainPerSecond => 50f;

        // ========== 音效配置 ==========

        public override string StartSFX => "Char/Footstep/dash";

        public override string LoopSFX => null;

        public override string EndSFX => null;

        // ========== 物品配置常量 ==========

        /// <summary>
        /// 图腾物品TypeID（动态分配，从500010开始）
        /// </summary>
        public const int TotemTypeIdBase = 500010;

        // ========== 属性键（用于 CustomData 字符对字符显示）==========

        /// <summary>
        /// 最大向上速度属性名
        /// </summary>
        public const string VAR_MAX_UPWARD_SPEED = "Flight_MaxUpwardSpeed";

        /// <summary>
        /// 加速时间属性名
        /// </summary>
        public const string VAR_ACCELERATION_TIME = "Flight_AccelerationTime";

        /// <summary>
        /// 滑翔水平移动系数属性名
        /// </summary>
        public const string VAR_GLIDING_MULTIPLIER = "Flight_GlidingMultiplier";

        /// <summary>
        /// 缓慢下落速度属性名
        /// </summary>
        public const string VAR_DESCENT_SPEED = "Flight_DescentSpeed";

        /// <summary>
        /// 启动体力消耗属性名
        /// </summary>
        public const string VAR_STARTUP_STAMINA = "Flight_StartupStamina";

        /// <summary>
        /// 飞行体力消耗属性名
        /// </summary>
        public const string VAR_FLIGHT_STAMINA_DRAIN = "Flight_FlightStaminaDrain";

        /// <summary>
        /// 滑翔体力消耗属性名
        /// </summary>
        public const string VAR_GLIDING_STAMINA_DRAIN = "Flight_GlidingStaminaDrain";

        // ========== 自定义数据键（用于显示文字值）==========

        /// <summary>
        /// 能力键自定义数据键（用于显示"翻滚键：腾云"）
        /// </summary>
        public const string VAR_ABILITY_KEY = "Flight_Ability";
    }
}

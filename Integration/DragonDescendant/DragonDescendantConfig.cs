// ============================================================================
// DragonDescendantConfig.cs - 龙裔遗族Boss配置
// ============================================================================
// 模块说明：
//   定义龙裔遗族Boss的所有可配置参数
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 龙裔遗族Boss配置参数
    /// </summary>
    public static class DragonDescendantConfig
    {
        // ========== Boss基础属性 ==========
        
        /// <summary>
        /// 基础血量
        /// </summary>
        public const float BaseHealth = 500f;
        
        /// <summary>
        /// 伤害倍率（一阶段）
        /// </summary>
        public const float DamageMultiplier = 0.3f;
        
        /// <summary>
        /// 二阶段伤害倍率
        /// </summary>
        public const float Phase2DamageMultiplier = 1.1f;
        
        // ========== 火箭弹配置 ==========
        
        /// <summary>
        /// 每多少发子弹触发一次火箭弹
        /// </summary>
        public const int BulletsPerRocket = 10;
        
        /// <summary>
        /// 火箭弹飞行速度
        /// </summary>
        public const float RocketSpeed = 25f;
        
        /// <summary>
        /// 火箭弹最大飞行距离
        /// </summary>
        public const float RocketMaxDistance = 50f;
        
        /// <summary>
        /// 火箭弹直接命中伤害
        /// </summary>
        public const float RocketDirectDamage = 20f;
        
        /// <summary>
        /// 火箭弹爆炸伤害
        /// </summary>
        public const float RocketExplosionDamage = 20f;
        
        /// <summary>
        /// 火箭弹爆炸范围（基础1m，如果Boss在范围内会动态扩大到 distToBoss+0.5m）
        /// </summary>
        public const float RocketExplosionRadius = 1f;
        
        /// <summary>
        /// 火箭弹爆炸触发距离（玩家距离Boss小于此值时才会爆炸）
        /// </summary>
        public const float RocketBossDamageRadius = 5f;
        
        // ========== 燃烧弹配置 ==========
        
        /// <summary>
        /// 正常状态下燃烧弹投掷间隔（秒）
        /// </summary>
        public const float NormalGrenadeInterval = 5f;
        
        /// <summary>
        /// 狂暴状态下燃烧弹投掷间隔（秒）
        /// </summary>
        public const float EnragedGrenadeInterval = 1f;
        
        /// <summary>
        /// 血量阈值（低于此比例时燃烧弹投向自己）
        /// </summary>
        public const float HealthThreshold = 0.5f;
        
        // ========== 复活配置 ==========
        
        /// <summary>
        /// 复活后恢复的血量比例
        /// </summary>
        public const float ResurrectionHealthPercent = 0.5f;
        
        /// <summary>
        /// 复活对话每个字符的显示间隔（秒）
        /// </summary>
        public const float DialogueCharInterval = 1f;
        
        /// <summary>
        /// 复活对话内容
        /// </summary>
        public const string ResurrectionDialogue = "我...命不该绝！";
        
        /// <summary>
        /// 复活对话英文版本
        /// </summary>
        public const string ResurrectionDialogueEN = "I...shall not perish!";
        
        // ========== 狂暴状态配置 ==========
        
        /// <summary>
        /// 狂暴状态下套装发光范围（米）
        /// </summary>
        public const float EnragedGlowRadius = 5f;
        
        /// <summary>
        /// 碰撞击退力
        /// </summary>
        public const float KnockbackForce = 10f;
        
        /// <summary>
        /// 碰撞伤害
        /// </summary>
        public const float CollisionDamage = 20f;
        
        /// <summary>
        /// 追逐速度倍率
        /// </summary>
        public const float ChaseSpeedMultiplier = 1.5f;
        
        /// <summary>
        /// 碰撞检测半径（米）
        /// </summary>
        public const float CollisionTriggerRadius = 1.5f;
        
        /// <summary>
        /// 碰撞冷却时间（秒）
        /// </summary>
        public const float CollisionCooldown = 0.5f;
        
        /// <summary>
        /// 对话气泡Y轴偏移（米）
        /// </summary>
        public const float DialogueBubbleYOffset = 2.5f;
        
        /// <summary>
        /// 二阶段子弹暴击伤害倍率
        /// </summary>
        public const float Phase2CritDamageFactor = 1.5f;
        
        /// <summary>
        /// 击退方向Y分量（稍微向上）
        /// </summary>
        public const float KnockbackYComponent = 0.3f;
        
        // ========== 预设名称 ==========
        
        /// <summary>
        /// 基础预设的nameKey（原版"???"敌人）
        /// </summary>
        public const string BasePresetNameKey = "???";
        
        // ========== 装备名称 ==========
        
        /// <summary>
        /// RPK武器名称
        /// </summary>
        public const string RPK_ITEM_NAME = "RPK";
        
        /// <summary>
        /// 龙头头盔名称（物品资源名）
        /// </summary>
        public const string DRAGON_HELM_NAME = "dargon_Helmet_Item";
        
        /// <summary>
        /// 龙甲护甲名称（物品资源名）
        /// </summary>
        public const string DRAGON_ARMOR_NAME = "dargon_Armor_Item";
        
        /// <summary>
        /// 龙头头盔TypeID
        /// </summary>
        public const int DRAGON_HELM_TYPE_ID = 500003;
        
        /// <summary>
        /// 龙甲护甲TypeID
        /// </summary>
        public const int DRAGON_ARMOR_TYPE_ID = 500004;
        
        /// <summary>
        /// 龙息武器TypeID（引用DragonBreathConfig）
        /// </summary>
        public const int DRAGON_BREATH_TYPE_ID = DragonBreathConfig.WEAPON_TYPE_ID;
        
        // ========== 掉落概率配置 ==========
        
        /// <summary>
        /// 龙头掉落概率 (30%)
        /// </summary>
        public const float DROP_CHANCE_HELM = 0.3f;
        
        /// <summary>
        /// 龙甲掉落概率 (60%)
        /// </summary>
        public const float DROP_CHANCE_ARMOR = 0.6f;
        
        /// <summary>
        /// 龙息武器掉落概率 (10%)
        /// </summary>
        public const float DROP_CHANCE_WEAPON = 0.1f;
        
        // ========== 本地化键 ==========
        
        /// <summary>
        /// Boss名称本地化键
        /// </summary>
        public const string BOSS_NAME_KEY = "DragonDescendant";
        
        /// <summary>
        /// Boss中文名称
        /// </summary>
        public const string BOSS_NAME_CN = "龙裔遗族";
        
        /// <summary>
        /// Boss英文名称
        /// </summary>
        public const string BOSS_NAME_EN = "Dragon Descendant";
    }
}

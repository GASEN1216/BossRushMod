// ============================================================================
// DragonBreathBuffHandler.cs - 龙息武器Buff触发处理器
// ============================================================================
// 模块说明：
//   监听Health.OnHurt事件，当龙息武器命中敌人时触发龙焰灼烧Buff
//   绕过原版buffChanceMultiplier限制，使用自定义概率判定
// ============================================================================

using UnityEngine;
using Duckov.Buffs;

namespace BossRush
{
    /// <summary>
    /// 龙息武器Buff触发处理器
    /// </summary>
    public static class DragonBreathBuffHandler
    {
        // 初始化状态
        private static bool isInitialized = false;
        
        // 缓存的Buff预制体（延迟加载）
        private static Buff cachedBuffPrefab = null;
        
        /// <summary>
        /// 初始化处理器，订阅全局伤害事件
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized)
            {
                ModBehaviour.DevLog("[DragonBreath] 已初始化，跳过重复初始化");
                return;
            }
            
            // 订阅全局伤害事件
            Health.OnHurt += OnGlobalHurt;
            isInitialized = true;
            
            ModBehaviour.DevLog("[DragonBreath] 初始化完成，已订阅Health.OnHurt事件");
        }
        
        /// <summary>
        /// 清理处理器，取消订阅事件
        /// </summary>
        public static void Cleanup()
        {
            if (!isInitialized)
            {
                return;
            }
            
            // 取消订阅事件
            Health.OnHurt -= OnGlobalHurt;
            isInitialized = false;
            cachedBuffPrefab = null;
            
            ModBehaviour.DevLog("[DragonBreath] 已清理，取消订阅Health.OnHurt事件");
        }
        
        /// <summary>
        /// 全局伤害事件回调
        /// </summary>
        private static void OnGlobalHurt(Health health, DamageInfo damageInfo)
        {
            // 安全检查：Health不能为null
            if (health == null) return;
            
            // 检查武器ID是否为龙息武器
            if (damageInfo.fromWeaponItemID != DragonBreathConfig.WEAPON_TYPE_ID) return;
            
            // 获取目标角色
            CharacterMainControl target = health.TryGetCharacter();
            if (target == null) return;
            
            // 友军检查：不对同队伍目标施加Buff
            CharacterMainControl attacker = damageInfo.fromCharacter;
            if (attacker != null && target.Team == attacker.Team) return;
            
            // 概率判定
            if (Random.value > DragonBreathConfig.TRIGGER_CHANCE) return;
            
            // 获取Buff预制体
            Buff buffPrefab = GetBuffPrefab();
            if (buffPrefab == null)
            {
                // 仅在首次失败时输出警告，避免刷屏
                return;
            }
            
            // 应用Buff（不输出日志，避免战斗中刷屏）
            target.AddBuff(buffPrefab, attacker, damageInfo.fromWeaponItemID);
        }
        
        /// <summary>
        /// 获取Buff预制体（延迟加载，首次调用时从EquipmentFactory获取）
        /// </summary>
        private static Buff GetBuffPrefab()
        {
            // 如果已缓存，直接返回
            if (cachedBuffPrefab != null) return cachedBuffPrefab;
            
            // 从EquipmentFactory获取
            cachedBuffPrefab = EquipmentFactory.GetLoadedBuff(DragonBreathConfig.BUFF_BASE_NAME);
            
            // 仅在首次获取失败时输出警告
            if (cachedBuffPrefab == null)
            {
                ModBehaviour.DevLog("[DragonBreath] 警告：无法获取Buff预制体，baseName=" + DragonBreathConfig.BUFF_BASE_NAME);
            }
            else
            {
                ModBehaviour.DevLog("[DragonBreath] 已缓存Buff预制体: " + cachedBuffPrefab.name);
            }
            
            return cachedBuffPrefab;
        }
    }
}

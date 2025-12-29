// ============================================================================
// DragonBreathBuffHandler.cs - 龙息武器Buff触发处理器
// ============================================================================
// 模块说明：
//   监听Health.OnHurt事件，当龙息武器命中敌人时触发龙焰灼烧Buff
//   绕过原版buffChanceMultiplier限制，使用自定义概率判定
//   
//   性能优化：只在玩家装备龙息武器时订阅事件，卸下时取消订阅
//   避免在所有伤害事件中进行武器ID检查
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
        // 事件订阅状态
        private static bool isSubscribed = false;
        
        // 缓存的Buff预制体（延迟加载）
        private static Buff cachedBuffPrefab = null;
        
        /// <summary>
        /// 订阅伤害事件（在玩家装备龙息武器时调用）
        /// </summary>
        public static void Subscribe()
        {
            if (isSubscribed) return;
            
            Health.OnHurt += OnDragonBreathHurt;
            isSubscribed = true;
            
            ModBehaviour.DevLog("[DragonBreath] 已订阅Health.OnHurt事件（装备龙息武器）");
        }
        
        /// <summary>
        /// 取消订阅伤害事件（在玩家卸下龙息武器时调用）
        /// </summary>
        public static void Unsubscribe()
        {
            if (!isSubscribed) return;
            
            Health.OnHurt -= OnDragonBreathHurt;
            isSubscribed = false;
            
            ModBehaviour.DevLog("[DragonBreath] 已取消订阅Health.OnHurt事件（卸下龙息武器）");
        }
        
        /// <summary>
        /// 清理处理器（Mod卸载时调用）
        /// </summary>
        public static void Cleanup()
        {
            Unsubscribe();
            cachedBuffPrefab = null;
        }
        
        /// <summary>
        /// 龙息武器伤害事件回调
        /// 注意：此回调只在玩家装备龙息武器时才会被调用，无需检查武器ID
        /// </summary>
        private static void OnDragonBreathHurt(Health health, DamageInfo damageInfo)
        {
            // 安全检查
            if (health == null) return;
            
            // 双重保险：确认是龙息武器造成的伤害
            // （虽然只在装备时订阅，但可能有其他武器同时造成伤害）
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
            if (buffPrefab == null) return;
            
            // 应用Buff
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

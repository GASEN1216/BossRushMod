// ============================================================================
// DragonBreathBuffHandler.cs - 龙息武器Buff触发处理器
// ============================================================================
// 模块说明：
//   监听Health.OnHurt事件，当龙息武器命中敌人时触发龙焰灼烧Buff
//   绕过原版buffChanceMultiplier限制，使用自定义概率判定
//   
//   性能优化：
//   - 只在玩家装备龙息武器时订阅事件，卸下时取消订阅
//   - 减少日志输出和字符串拼接，适配低端机
//
//   Buff伤害：每层0.3%最大生命值，最多10层（满层3%/次）
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
        private static bool isSubscribed = false;
        private static Buff cachedBuffPrefab = null;
        private static bool buffPrefabSearched = false;
        
        /// <summary>
        /// 订阅伤害事件（在玩家装备龙息武器时调用）
        /// </summary>
        public static void Subscribe()
        {
            if (isSubscribed) return;
            
            if (cachedBuffPrefab == null && !buffPrefabSearched)
            {
                buffPrefabSearched = true;
                cachedBuffPrefab = EquipmentFactory.GetLoadedBuff(DragonBreathConfig.BUFF_BASE_NAME);
            }
            
            Health.OnHurt += OnDragonBreathHurt;
            isSubscribed = true;
            
            ModBehaviour.DevLog("[DragonBreath] Buff处理器已启用");
        }
        
        /// <summary>
        /// 取消订阅伤害事件（在玩家卸下龙息武器时调用）
        /// </summary>
        public static void Unsubscribe()
        {
            if (!isSubscribed) return;
            
            Health.OnHurt -= OnDragonBreathHurt;
            isSubscribed = false;
        }
        
        /// <summary>
        /// 清理处理器（Mod卸载或场景切换时调用）
        /// </summary>
        public static void Cleanup()
        {
            Unsubscribe();
            cachedBuffPrefab = null;
            buffPrefabSearched = false;
        }
        
        /// <summary>
        /// 清理所有静态缓存（场景切换时调用，防止持有已销毁对象引用）
        /// </summary>
        public static void ClearStaticCache()
        {
            cachedBuffPrefab = null;
            buffPrefabSearched = false;
        }
        
        /// <summary>
        /// 龙息武器伤害事件回调
        /// </summary>
        private static void OnDragonBreathHurt(Health health, DamageInfo damageInfo)
        {
            // 快速过滤：武器ID不匹配直接返回
            if (damageInfo.fromWeaponItemID != DragonBreathConfig.WEAPON_TYPE_ID) return;
            if (health == null || cachedBuffPrefab == null) return;
            
            // 过滤Buff/Effect造成的伤害，避免循环触发
            if (damageInfo.isFromBuffOrEffect) return;
            
            // 获取目标角色
            CharacterMainControl target = health.TryGetCharacter();
            if (target == null) return;
            
            // 友军检查
            CharacterMainControl attacker = damageInfo.fromCharacter;
            if (attacker != null && target.Team == attacker.Team) return;
            
            // 概率判定（50%触发）
            if (Random.value > DragonBreathConfig.TRIGGER_CHANCE) return;
            
            // 记录应用前的生命值
            float hpBefore = health.CurrentHealth;
            
            // 应用Buff
            target.AddBuff(cachedBuffPrefab, attacker, damageInfo.fromWeaponItemID);
            
            // 获取BuffManager检查Buff状态
            var buffManager = target.GetBuffManager();
            int layers = 0;
            if (buffManager != null)
            {
                var buffs = buffManager.Buffs;
                for (int i = 0; i < buffs.Count; i++)
                {
                    if (buffs[i] != null && buffs[i].ID == cachedBuffPrefab.ID)
                    {
                        layers = buffs[i].CurrentLayers;
                        break;
                    }
                }
            }
            
            // [性能优化] 只在调试模式下输出详细日志，减少字符串拼接开销
            #if DEBUG
            ModBehaviour.DevLog("[DragonBreath] Buff已应用! 目标HP: " + hpBefore.ToString("F1") + "/" + health.MaxHealth.ToString("F1") + 
                ", 层数: " + layers + ", 预计每次伤害: " + (health.MaxHealth * 0.003f * layers).ToString("F2"));
            #endif
        }
    }
}

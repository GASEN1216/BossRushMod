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
//   Buff伤害：每层0.1%最大生命值 + 每层2点基础伤害，最多10层，持续10秒
//   
//   特效说明：
//   - 使用原版点燃Buff的特效（buffFxPfb），保持视觉一致性
//   - 逻辑（层数、伤害、持续时间）仍使用自定义龙焰灼烧Buff
// ============================================================================

using System.Reflection;
using UnityEngine;
using Duckov.Buffs;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 龙息武器Buff触发处理器
    /// </summary>
    public static class DragonBreathBuffHandler
    {
        private static bool isSubscribed = false;
        private static bool isBaseDamageSubscribed = false;  // 基础伤害监听器订阅状态
        private static Buff cachedBuffPrefab = null;
        private static bool buffPrefabSearched = false;
        private static bool fxReplaced = false;  // 标记是否已替换特效
        
        // 缓存反射字段，避免重复查找
        private static FieldInfo buffFxPfbField = null;
        private static bool fieldSearched = false;
        
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
                
                // 替换特效为原版点燃Buff的特效
                if (cachedBuffPrefab != null && !fxReplaced)
                {
                    ReplaceBuffFxWithVanillaBurn(cachedBuffPrefab);
                }
            }
            
            Health.OnHurt += OnDragonBreathHurt;
            isSubscribed = true;
            
            // 订阅基础伤害监听器（监听Buff造成的伤害，追加基础伤害）
            if (!isBaseDamageSubscribed)
            {
                Health.OnHurt += OnDragonBurnBaseDamage;
                isBaseDamageSubscribed = true;
            }
            
            ModBehaviour.DevLog("[DragonBreath] Buff处理器已启用");
        }
        
        /// <summary>
        /// 将龙焰灼烧Buff的特效替换为原版点燃Buff的特效
        /// 性能说明：反射操作只执行一次，字段引用会被缓存
        /// </summary>
        private static void ReplaceBuffFxWithVanillaBurn(Buff dragonBuff)
        {
            try
            {
                // 获取原版Burn Buff
                Buff vanillaBurn = GameplayDataSettings.Buffs.Burn;
                if (vanillaBurn == null)
                {
                    ModBehaviour.DevLog("[DragonBreath] 无法获取原版Burn Buff，跳过特效替换");
                    return;
                }
                
                // 缓存反射字段（只查找一次）
                if (!fieldSearched)
                {
                    fieldSearched = true;
                    buffFxPfbField = typeof(Buff).GetField("buffFxPfb", 
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }
                
                if (buffFxPfbField == null)
                {
                    ModBehaviour.DevLog("[DragonBreath] 无法找到buffFxPfb字段，跳过特效替换");
                    return;
                }
                
                // 获取原版Burn的特效预制体
                GameObject vanillaFx = buffFxPfbField.GetValue(vanillaBurn) as GameObject;
                if (vanillaFx == null)
                {
                    ModBehaviour.DevLog("[DragonBreath] 原版Burn Buff没有特效预制体");
                    return;
                }
                
                // 替换龙焰灼烧的特效（只是修改引用，无性能开销）
                buffFxPfbField.SetValue(dragonBuff, vanillaFx);
                fxReplaced = true;
                
                ModBehaviour.DevLog("[DragonBreath] 已将龙焰灼烧特效替换为原版点燃特效: " + vanillaFx.name);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[DragonBreath] 替换特效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 取消订阅伤害事件（在玩家卸下龙息武器时调用）
        /// </summary>
        public static void Unsubscribe()
        {
            if (!isSubscribed) return;
            
            Health.OnHurt -= OnDragonBreathHurt;
            isSubscribed = false;
            
            // 取消基础伤害监听器
            if (isBaseDamageSubscribed)
            {
                Health.OnHurt -= OnDragonBurnBaseDamage;
                isBaseDamageSubscribed = false;
            }
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
        /// 注意：必须先取消事件订阅，再重置缓存，避免事件重复订阅导致多次伤害
        /// </summary>
        public static void ClearStaticCache()
        {
            // [Bug修复] 必须先取消事件订阅！
            // 之前的实现只重置标志而不取消订阅，导致场景切换后重新Subscribe()时
            // 事件被重复订阅，每次Buff伤害会触发多次基础伤害回调（三次真伤bug）
            Unsubscribe();
            
            // 重置Buff预制体缓存
            cachedBuffPrefab = null;
            buffPrefabSearched = false;
            fxReplaced = false;
        }
        
        /// <summary>
        /// 龙息武器伤害事件回调 - 触发Buff
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
            float percentDmg = health.MaxHealth * 0.001f * layers;
            float baseDmg = DragonBreathConfig.BASE_DAMAGE_PER_LAYER * layers;
            ModBehaviour.DevLog("[DragonBreath] Buff已应用! 目标HP: " + hpBefore.ToString("F1") + "/" + health.MaxHealth.ToString("F1") + 
                ", 层数: " + layers + ", 预计每次伤害: " + (percentDmg + baseDmg).ToString("F2") + " (百分比:" + percentDmg.ToString("F2") + " + 基础:" + baseDmg.ToString("F2") + ")");
            #endif
        }
        
        /// <summary>
        /// 龙焰灼烧Buff基础伤害回调 - 在Buff造成伤害时追加基础伤害
        /// 性能优化：快速过滤条件前置，减少不必要的计算
        /// </summary>
        private static void OnDragonBurnBaseDamage(Health health, DamageInfo damageInfo)
        {
            // [性能优化] 快速过滤：前三个条件都是简单的布尔/整数比较，开销极低
            // 1. 只处理Buff/Effect造成的伤害
            if (!damageInfo.isFromBuffOrEffect) return;
            
            // 2. 检查是否是龙息武器造成的Buff伤害
            if (damageInfo.fromWeaponItemID != DragonBreathConfig.WEAPON_TYPE_ID) return;
            
            // 3. 空检查
            if (health == null) return;
            
            // 以下代码只在龙息Buff造成伤害时执行（每秒最多1次/目标）
            
            // 获取目标角色的Buff层数
            CharacterMainControl target = health.TryGetCharacter();
            if (target == null) return;
            
            var buffManager = target.GetBuffManager();
            if (buffManager == null) return;
            
            // 查找龙焰灼烧Buff层数
            int layers = 0;
            var buffs = buffManager.Buffs;
            int buffCount = buffs.Count;
            for (int i = 0; i < buffCount; i++)
            {
                var buff = buffs[i];
                if (buff != null && buff.ID == DragonBreathConfig.BUFF_ID)
                {
                    layers = buff.CurrentLayers;
                    break;
                }
            }
            
            if (layers <= 0) return;
            
            // 计算并造成基础伤害（每层1点）
            float baseDamage = DragonBreathConfig.BASE_DAMAGE_PER_LAYER * layers;
            
            // 对玩家的追加伤害上限为2点
            if (health.IsMainCharacterHealth && baseDamage > 2f)
            {
                baseDamage = 2f;
            }
            
            // 创建额外伤害信息（真实伤害，无视护甲）
            DamageInfo extraDamage = new DamageInfo(damageInfo.fromCharacter);
            extraDamage.damageValue = baseDamage;
            extraDamage.isFromBuffOrEffect = true;
            extraDamage.fromWeaponItemID = 0;  // 设为0避免再次触发此回调
            extraDamage.ignoreArmor = true;    // 真实伤害，无视护甲减免
            extraDamage.damagePoint = damageInfo.damagePoint;
            extraDamage.damageNormal = damageInfo.damageNormal;
            
            // [Bug修复] 添加火元素因子，避免被自动添加物理元素导致物理抗性减免
            // 注意：如果目标有火抗，伤害仍会被减免（龙套装玩家有火抗）
            extraDamage.elementFactors.Add(new ElementFactor(ElementTypes.fire, 1f));
            
            // 造成额外伤害
            health.Hurt(extraDamage);
        }
    }
}

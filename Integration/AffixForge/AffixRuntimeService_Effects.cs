// ============================================================================
// AffixRuntimeService_Effects.cs - 12 条词缀的实际效果实现（AffixRuntimeService 的 partial 续）
// ============================================================================
// 模块职责：
//   本文件只放"事件已经过完归因、CD 与重入守卫之后"的纯效果代码。
//   订阅/退订、context 重建、归因谓词全部在 AffixRuntimeService.cs 里，本文件不碰。
//
// 硬约束：
//   1. 一切自建伤害必须置 isFromBuffOrEffect = true —— 归因谓词第 3 项会把它挡回去，
//      这是荆棘 / 灌能 / 殉爆不自我循环的第一道防线。
//   2. DamageInfo 是 struct，按值传给 Health.OnHurt。在 handler 里改 elementFactors
//      对本次伤害毫无影响，所以"追加元素伤害"只能补一次独立的 Health.Hurt()。
//      任何"在 OnHurt 里加暴击/加元素"的写法都是错的。
//   3. 死契的持续流失只写 Health.CurrentHealth 并 clamp 最低 1 点血，绝不调 Hurt()。
//   4. TickDrain 是每帧热路径：零日志、零分配、零 GetComponent。
//
// 对 RuntimeStatModifierTracker 的刻意偏离（写明理由）：
//   RuntimeStatModifierTracker.TryAdd 把 ModifierType 写死成 PercentageAdd，
//   而 GunCritRateGain / MeleeCritRateGain / BodyArmor / HeadArmor 是"加点型"stat，
//   用百分比会算错量级。因此本文件自建 AffixStatModifierApplier.TryAdd 显式带
//   ModifierType 参数；**移除路径仍复用 RuntimeStatModifierTracker.RemoveAll**，
//   记录容器沿用同一个 ZombieModeAttributeModifierRecord，不产生第二套语义。
// ============================================================================

using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 运行时 Stat Modifier 挂载器（带显式 ModifierType）。
    /// 与 RuntimeStatModifierTracker 的差异见文件头，移除仍走 RemoveAll。
    /// </summary>
    internal static class AffixStatModifierApplier
    {
        internal static bool TryAdd(
            CharacterMainControl character,
            string statKey,
            ModifierType modifierType,
            float value,
            object source,
            List<ZombieModeAttributeModifierRecord> records,
            string context)
        {
            if (character == null || character.CharacterItem == null ||
                string.IsNullOrEmpty(statKey) || records == null || source == null)
            {
                return false;
            }

            if (System.Math.Abs(value) < 0.0001f)
            {
                return false;
            }

            try
            {
                Stat stat = character.CharacterItem.GetStat(statKey);
                if (stat == null)
                {
                    return false;
                }

                Modifier modifier = new Modifier(modifierType, value, source);
                stat.AddModifier(modifier);

                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = character.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statKey;
                records.Add(record);
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog(context + " [WARNING] 挂载 modifier 失败: " + statKey + ", " + e.Message);
                return false;
            }
        }
    }

    public static partial class AffixRuntimeService
    {
        // ---- Stat key 常量（拼写逐个核实过，禁止散落字面量）----
        private const string StatMaxHealth = "MaxHealth";
        private const string StatBodyArmor = "BodyArmor";
        private const string StatHeadArmor = "HeadArmor";
        private const string StatGunDamageMultiplier = "GunDamageMultiplier";
        private const string StatMeleeDamageMultiplier = "MeleeDamageMultiplier";
        private const string StatGunCritRateGain = "GunCritRateGain";
        private const string StatMeleeCritRateGain = "MeleeCritRateGain";

        // ====================================================================
        // 分发入口（由 AffixRuntimeService.cs 的热路径 handler 调用）
        // ====================================================================

        /// <summary>主角命中敌人。</summary>
        private static void DispatchPlayerHitEnemy(ActiveAffix active, Health victim, ref DamageInfo info, CharacterMainControl main)
        {
            string id = active.Def.Id;

            if (id == AffixDefinitions.Id_Lifesteal)
            {
                Do_Lifesteal(active, main);
                return;
            }

            if (id == AffixDefinitions.Id_Overcharge)
            {
                Do_Overcharge(active, victim, ref info, main);
            }
        }

        /// <summary>主角受击。</summary>
        private static void DispatchPlayerHurt(ActiveAffix active, ref DamageInfo info, CharacterMainControl main)
        {
            string id = active.Def.Id;

            if (id == AffixDefinitions.Id_Bulwark)
            {
                Do_Bulwark(active, main);
                return;
            }

            if (id == AffixDefinitions.Id_Thorns)
            {
                Do_Thorns(active, ref info, main);
            }
        }

        /// <summary>主角击杀敌人。</summary>
        private static void DispatchPlayerKill(ActiveAffix active, Vector3 position, CharacterMainControl main)
        {
            string id = active.Def.Id;

            if (id == AffixDefinitions.Id_Slaughter)
            {
                Do_HealByMaxHealthRatio(main, AffixDefinitions.GetTierValue(active.Def, active.Tier));
                return;
            }

            if (id == AffixDefinitions.Id_SwiftHand)
            {
                Do_SwiftHand(active, main);
                return;
            }

            if (id == AffixDefinitions.Id_Frenzy)
            {
                Do_Frenzy(active, main);
                return;
            }

            if (id == AffixDefinitions.Id_DeathBurst)
            {
                Do_DeathBurst(active, position, main);
                return;
            }

            if (id == AffixDefinitions.Id_DeathPact)
            {
                Do_HealByMaxHealthRatio(main, AffixDefinitions.GetTierValue(active.Def, active.Tier));
            }
        }

        // ====================================================================
        // 普通档
        // ====================================================================

        /// <summary>汲血：命中回血。</summary>
        private static void Do_Lifesteal(ActiveAffix active, CharacterMainControl main)
        {
            Do_HealByMaxHealthRatio(main, AffixDefinitions.GetTierValue(active.Def, active.Tier));
        }

        /// <summary>
        /// 按最大生命比例回血。
        /// 走 Health.AddHealth 而不是 CharacterMainControl.AddHealth：
        /// 前者不吃 HealGain 加成，与 blood_pact 现役口径一致，词缀数值才可控。
        /// </summary>
        private static void Do_HealByMaxHealthRatio(CharacterMainControl main, float ratio)
        {
            if (ratio <= 0f)
            {
                return;
            }

            Health health = main.Health;
            if (health == null || health.IsDead)
            {
                return;
            }

            float heal = health.MaxHealth * ratio;
            if (heal <= 0f)
            {
                return;
            }

            health.AddHealth(heal);
        }

        /// <summary>磐石：受击短暂加护甲（叠层由 ModifierAction.OnBuffLayerChanged 免费处理）。</summary>
        private static void Do_Bulwark(ActiveAffix active, CharacterMainControl main)
        {
            Buff buff = AffixBuffFactory.GetBulwarkBuff(active.Tier);
            if (buff == null)
            {
                return;
            }

            main.AddBuff(buff, main, 0);
        }

        /// <summary>迅手：击杀短暂加换弹速。</summary>
        private static void Do_SwiftHand(ActiveAffix active, CharacterMainControl main)
        {
            Buff buff = AffixBuffFactory.GetSwiftHandBuff(active.Tier);
            if (buff == null)
            {
                return;
            }

            main.AddBuff(buff, main, 0);
        }

        /// <summary>
        /// 荆棘：反弹伤害。
        /// info.finalDamage 已是过完护甲的实伤，不要再乘护甲系数。
        /// </summary>
        private static void Do_Thorns(ActiveAffix active, ref DamageInfo info, CharacterMainControl main)
        {
            CharacterMainControl attacker = info.fromCharacter;
            if (attacker == null || attacker.IsMainCharacter)
            {
                return;
            }

            Health target = attacker.Health;
            if (target == null || target.IsDead || target.Invincible)
            {
                return;
            }

            float ratio = AffixDefinitions.GetTierValue(active.Def, active.Tier);
            float damage = info.finalDamage * ratio;
            if (damage <= 0f)
            {
                return;
            }

            DamageInfo reflect = new DamageInfo(main);
            reflect.damageValue = damage;
            reflect.isFromBuffOrEffect = true;      // 防循环第一道
            reflect.damagePoint = attacker.transform != null ? attacker.transform.position : info.damagePoint;
            reflect.AddElementFactor(ElementTypes.physics, 1f);
            target.Hurt(reflect);
        }

        // ====================================================================
        // 稀有档
        // ====================================================================

        /// <summary>
        /// 殉爆：击杀爆炸。canHurtSelf = false（已定案，不伤自身）。
        /// fxType 必须是 normal —— fire 没有对应特效预制体，会出现"看不见的爆炸"。
        /// </summary>
        private static void Do_DeathBurst(ActiveAffix active, Vector3 position, CharacterMainControl main)
        {
            LevelManager level = LevelManager.Instance;
            if (level == null || level.ExplosionManager == null)
            {
                return;
            }

            float damage = AffixDefinitions.GetTierValue(active.Def, active.Tier);
            float radius = AffixDefinitions.GetTierValue2(active.Def, active.Tier);
            if (damage <= 0f || radius <= 0f)
            {
                return;
            }

            DamageInfo blast = new DamageInfo(main);
            blast.damageValue = damage;
            blast.damagePoint = position;
            blast.isExplosion = true;
            blast.isFromBuffOrEffect = true;        // 防连环殉爆递归
            blast.AddElementFactor(ElementTypes.fire, 1f);

            level.ExplosionManager.CreateExplosion(
                position,
                radius,
                blast,
                ExplosionFxTypes.normal,
                0.30f,
                false);
        }

        /// <summary>狂潮：击杀叠攻速 / 移速。</summary>
        private static void Do_Frenzy(ActiveAffix active, CharacterMainControl main)
        {
            Buff buff = AffixBuffFactory.GetFrenzyBuff(active.Tier);
            if (buff == null)
            {
                return;
            }

            main.AddBuff(buff, main, 0);
        }

        /// <summary>
        /// 灌能：命中追加电系伤害。
        /// 只能补一次独立 Hurt（DamageInfo 是 struct，改本次伤害无效）。
        /// ignoreArmor = true：补伤已按实伤比例算过一次护甲，不再吃第二次。
        /// 仍会吃目标的电系抗性，这是想要的——不偷渡元素反应系统。
        /// </summary>
        private static void Do_Overcharge(ActiveAffix active, Health victim, ref DamageInfo info, CharacterMainControl main)
        {
            if (victim == null || victim.IsDead || victim.Invincible)
            {
                return;
            }

            float ratio = AffixDefinitions.GetTierValue(active.Def, active.Tier);
            float damage = info.finalDamage * ratio;
            if (damage <= 0f)
            {
                return;
            }

            DamageInfo extra = new DamageInfo(main);
            extra.damageValue = damage;
            extra.isFromBuffOrEffect = true;
            extra.ignoreArmor = true;
            extra.damagePoint = info.damagePoint;
            extra.fromWeaponItemID = info.fromWeaponItemID;
            extra.AddElementFactor(ElementTypes.electricity, 1f);
            victim.Hurt(extra);
        }

        // ====================================================================
        // 常驻型词缀（鹰目 / 狂血 / 玻璃炮）
        // ====================================================================

        /// <summary>
        /// 按 context 挂上全部常驻 modifier。调用前 RebuildContext 已先 RemoveAll，
        /// 因此这里不需要去重。幅度全部是运行时 Modifier，绝不进存档。
        /// </summary>
        private static void ApplyPersistentModifiers(CharacterMainControl main)
        {
            if (main == null || main.CharacterItem == null)
            {
                return;
            }

            bool maxHealthTouched = false;

            for (int i = 0; i < _active.Count; i++)
            {
                ActiveAffix active = _active[i];
                if (active == null || active.Def == null)
                {
                    continue;
                }

                if ((active.Def.Hooks & AffixHookMask.Persistent) == 0)
                {
                    continue;
                }

                string id = active.Def.Id;
                float value = AffixDefinitions.GetTierValue(active.Def, active.Tier);
                float value2 = AffixDefinitions.GetTierValue2(active.Def, active.Tier);

                if (id == AffixDefinitions.Id_HawkEye)
                {
                    // 暴击率增益是加点型 stat，必须用 Add，不能用 PercentageAdd
                    AffixStatModifierApplier.TryAdd(main, StatGunCritRateGain, ModifierType.Add, value, ModifierSource, _persistentModifiers, LogPrefix);
                    AffixStatModifierApplier.TryAdd(main, StatMeleeCritRateGain, ModifierType.Add, value, ModifierSource, _persistentModifiers, LogPrefix);
                    continue;
                }

                if (id == AffixDefinitions.Id_BloodRage)
                {
                    AffixStatModifierApplier.TryAdd(main, StatGunDamageMultiplier, ModifierType.PercentageAdd, value, ModifierSource, _persistentModifiers, LogPrefix);
                    AffixStatModifierApplier.TryAdd(main, StatMeleeDamageMultiplier, ModifierType.PercentageAdd, value, ModifierSource, _persistentModifiers, LogPrefix);
                    if (AffixStatModifierApplier.TryAdd(main, StatMaxHealth, ModifierType.PercentageAdd, -value2, ModifierSource, _persistentModifiers, LogPrefix))
                    {
                        maxHealthTouched = true;
                    }
                    continue;
                }

                if (id == AffixDefinitions.Id_GlassCannon)
                {
                    AffixStatModifierApplier.TryAdd(main, StatGunDamageMultiplier, ModifierType.PercentageAdd, value, ModifierSource, _persistentModifiers, LogPrefix);
                    AffixStatModifierApplier.TryAdd(main, StatMeleeDamageMultiplier, ModifierType.PercentageAdd, value, ModifierSource, _persistentModifiers, LogPrefix);
                    // 护甲是加点型；负护甲在 Health.Hurt 里会被 Clamp 到 0，不会反向加伤
                    AffixStatModifierApplier.TryAdd(main, StatBodyArmor, ModifierType.Add, -value2, ModifierSource, _persistentModifiers, LogPrefix);
                    AffixStatModifierApplier.TryAdd(main, StatHeadArmor, ModifierType.Add, -value2, ModifierSource, _persistentModifiers, LogPrefix);
                }
            }

            if (maxHealthTouched)
            {
                ClampCurrentHealthToMax(main);
            }
        }

        /// <summary>
        /// 生命上限下调后必须钳当前血量（SetHealth 内部是 Min(MaxHealth, v)）。
        /// **移除 modifier 时故意不做反向操作**：否则玩家反复穿脱装备就能白嫖治疗。
        /// </summary>
        private static void ClampCurrentHealthToMax(CharacterMainControl main)
        {
            try
            {
                Health health = main.Health;
                if (health == null || health.IsDead)
                {
                    return;
                }

                health.SetHealth(health.CurrentHealth);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 钳制当前生命失败: " + e.Message);
            }
        }

        /// <summary>撤销全部常驻 modifier。切图后旧角色 Stat 失效由 RemoveAll 的 try/catch 兜住。</summary>
        private static void RemoveAllPersistentModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(_persistentModifiers, LogPrefix);
        }

        // ====================================================================
        // 死契：持续流失（每帧热路径）
        // ====================================================================

        /// <summary>
        /// 由 AffixRuntimeTicker.Update 调用。
        /// 绝不调 Health.Hurt —— Hurt 的致死分支会先发 OnDead，触发全 Mod 死亡链路
        /// 并把玩家真的打死。这里只写 CurrentHealth（public setter，只发 OnHealthChange）
        /// 并 clamp 在 1 点血，让死契永远不会直接致死。
        /// </summary>
        internal static void TickDrain(float deltaTime)
        {
            try
            {
                TickDrainCore(deltaTime);
            }
            catch
            {
                // 每帧热路径：异常静默自吞，不写日志（一旦出问题会每帧刷屏）
            }
        }

        private static void TickDrainCore(float deltaTime)
        {
            if (deltaTime <= 0f || _active.Count == 0)
            {
                return;
            }

            if (!IsEnabled)
            {
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                return;
            }

            Health health = main.Health;
            if (health == null || health.IsDead)
            {
                return;
            }

            // 基地不该掉血
            LevelManager level = LevelManager.Instance;
            if (level != null && level.IsBaseLevel)
            {
                return;
            }

            float ratioPerSecond = 0f;
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveAffix active = _active[i];
                if (active == null || active.Def == null)
                {
                    continue;
                }

                if ((active.Def.Hooks & AffixHookMask.Tick) == 0)
                {
                    continue;
                }

                if (active.Def.Id == AffixDefinitions.Id_DeathPact)
                {
                    ratioPerSecond += AffixDefinitions.GetTierValue2(active.Def, active.Tier);
                }
            }

            if (ratioPerSecond <= 0f)
            {
                return;
            }

            float drain = health.MaxHealth * ratioPerSecond * deltaTime;
            if (drain <= 0f)
            {
                return;
            }

            float current = health.CurrentHealth;
            float next = current - drain;
            if (next < DrainFloorHealth)
            {
                next = DrainFloorHealth;
            }

            if (next < current)
            {
                health.CurrentHealth = next;
            }
        }
    }
}

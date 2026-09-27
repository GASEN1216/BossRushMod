using System;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    internal static class BossStatScaling
    {
        internal static void ApplyBossStatMultiplier(CharacterMainControl character, float? multiplier, float defaultMultiplier)
        {
            if (character == null)
            {
                ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: character 为 null，跳过");
                return;
            }

            // 获取倍率值：优先使用传入参数，否则从配置读取
            float mult = multiplier ?? defaultMultiplier;

            ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 开始: mult=" + mult);

            // 倍率为 1 时无需处理
            if (Mathf.Approximately(mult, 1f))
            {
                ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: 倍率为 1，跳过");
                return;
            }

            try
            {
                var item = character.CharacterItem;
                if (item == null)
                {
                    ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: CharacterItem 为 null，跳过");
                    return;
                }

                // 使用与原版相同的方式：statName.GetHashCode() 获取 Stat
                // 参考 CharacterRandomPreset.MultiplyCharacterStat

                // 1. 修改生命值
                try
                {
                    Stat hpStat = item.GetStat("MaxHealth".GetHashCode());
                    if (hpStat != null)
                    {
                        float oldHp = hpStat.BaseValue;
                        hpStat.BaseValue *= mult;
                        ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: MaxHealth " + oldHp + " -> " + hpStat.BaseValue + " (x" + mult + ")");
                    }
                    else
                    {
                        ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: MaxHealth Stat 为 null");
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 修改 MaxHealth 失败: " + e.Message);
                }

                // 同步 Health 组件的当前血量
                try
                {
                    if (character.Health != null)
                    {
                        character.Health.SetHealth(character.Health.MaxHealth);
                    }
                }
                catch {}

                // 2. 修改枪械伤害倍率（上限为3倍）
                try
                {
                    Stat gunDmg = item.GetStat("GunDamageMultiplier".GetHashCode());
                    if (gunDmg != null)
                    {
                        float oldDmg = gunDmg.BaseValue;
                        float dmgMult = Mathf.Min(mult, 3f); // 伤害倍率上限为3
                        gunDmg.BaseValue *= dmgMult;
                        ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: GunDamageMultiplier " + oldDmg + " -> " + gunDmg.BaseValue + " (x" + dmgMult + ", 原倍率=" + mult + ")");
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 修改 GunDamageMultiplier 失败: " + e.Message);
                }

                // 3. 修改近战伤害倍率（上限为3倍）
                try
                {
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                    if (meleeDmg != null)
                    {
                        float oldDmg = meleeDmg.BaseValue;
                        float dmgMult = Mathf.Min(mult, 3f); // 伤害倍率上限为3
                        meleeDmg.BaseValue *= dmgMult;
                        ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: MeleeDamageMultiplier " + oldDmg + " -> " + meleeDmg.BaseValue + " (x" + dmgMult + ", 原倍率=" + mult + ")");
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 修改 MeleeDamageMultiplier 失败: " + e.Message);
                }

                // 4. 修改反应速度（通过 AICharacterController）
                // 反应时间越短，反应越快，所以用除法
                try
                {
                    AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
                    if (ai != null)
                    {
                        float oldReaction = ai.baseReactionTime;
                        float oldShootDelay = ai.shootDelay;

                        // 倍率越高，反应时间越短（更快）
                        ai.baseReactionTime /= mult;
                        ai.reactionTime /= mult;
                        ai.shootDelay /= mult;

                        ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: reactionTime " + oldReaction + " -> " + ai.baseReactionTime + " (/" + mult + ")");
                        ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier: shootDelay " + oldShootDelay + " -> " + ai.shootDelay + " (/" + mult + ")");
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 修改反应速度失败: " + e.Message);
                }

                ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ApplyBossStatMultiplier 失败: " + e.Message);
            }
        }
    }
}

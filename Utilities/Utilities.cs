// ============================================================================
// Utilities.cs - 工具方法
// ============================================================================
// 模块说明：
//   提供 BossRush 模组的通用工具方法，包括：
//   - 加油站创建和管理
//   - 其他辅助功能
//   
// 加油站：
//   在无间炼狱模式下提供弹药购买功能，包含常见弹药类型。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov.Economy;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 工具方法模块
    /// </summary>
    public partial class ModBehaviour
    {
        /// <summary>
        /// 确保加油站已创建
        /// <para>在无间炼狱模式下提供弹药购买功能</para>
        /// </summary>
        private void EnsureAmmoShop_Utilities()
        {
            if (ammoShop != null)
            {
                return;
            }

            try
            {
                GameObject go = new GameObject("BossRush_AmmoShop");
                try
                {
                    UnityEngine.Object.DontDestroyOnLoad(go);
                }
                catch {}

                ammoShop = go.AddComponent<StockShop>();

                try
                {
                    // 使用缓存的 FieldInfo
                    var fMerchant = ReflectionCache.StockShop_MerchantID;
                    if (fMerchant != null)
                    {
                        fMerchant.SetValue(ammoShop, "BossRushAmmo");
                    }
                }
                catch {}

                try
                {
                    // 设置 accountAvaliable = true，允许直接扣银行余额而非消耗现金物品
                    var fAccount = ReflectionCache.StockShop_AccountAvaliable;
                    if (fAccount != null)
                    {
                        fAccount.SetValue(ammoShop, true);
                    }
                }
                catch {}

                try
                {
                    if (ammoShop.entries == null)
                    {
                        ammoShop.entries = new List<StockShop.Entry>();
                    }
                    else
                    {
                        ammoShop.entries.Clear();
                    }
                }
                catch {}

                List<int> ammoIds = new List<int>
                {
                    105, 648, 870, 871, 649, 
                    612, 613, 615, 616, 698,
                    603, 604, 606, 607, 694,
                    594, 595, 597, 598, 691,
                    640, 708, 709, 710,
                    630, 631, 633, 634, 707,
                    621, 622, 700, 701, 702,
                    650, 1162, 918, 944, 1262,
                    326,
                    23, 24, 67, 66, 942, 660, 933, 941, 1366, 10, 17, 16, 15
                };

                try
                {
                    foreach (int id in ammoIds)
                    {
                        StockShopDatabase.ItemEntry raw = new StockShopDatabase.ItemEntry();
                        raw.typeID = id;
                        raw.maxStock = 9999;
                        raw.forceUnlock = true;
                        raw.priceFactor = 1.1f;
                        raw.possibility = 1f;
                        raw.lockInDemo = false;

                        StockShop.Entry entry = new StockShop.Entry(raw);
                        entry.CurrentStock = entry.MaxStock;
                        ammoShop.entries.Add(entry);
                    }
                }
                catch {}

                try
                {
                    // 使用缓存的 FieldInfo
                    var fItems = ReflectionCache.StockShop_ItemInstances;
                    if (fItems != null)
                    {
                        var dict = fItems.GetValue(ammoShop) as Dictionary<int, Item>;
                        if (dict == null)
                        {
                            dict = new Dictionary<int, Item>();
                            fItems.SetValue(ammoShop, dict);
                        }

                        foreach (int id in ammoIds)
                        {
                            if (dict.ContainsKey(id))
                            {
                                continue;
                            }
                            Item item = null;
                            try
                            {
                                item = ItemAssetsCollection.InstantiateSync(id);
                            }
                            catch {}
                            if (item == null)
                            {
                                continue;
                            }
                            try
                            {
                                dict[id] = item;
                            }
                            catch {}
                        }
                    }
                }
                catch {}
            }
            catch {}
        }

        // ============================================================================
        // Boss 数值倍率统一方法
        // ============================================================================

        /// <summary>
        /// 应用全局 Boss 数值倍率（统一方法，供所有模式复用）
        /// <para>影响：生命值、枪械伤害、近战伤害、反应速度</para>
        /// </summary>
        /// <param name="character">目标角色</param>
        /// <param name="multiplier">倍率值（默认从 config.bossStatMultiplier 读取）</param>
        private void ApplyBossStatMultiplier(CharacterMainControl character, float? multiplier = null)
        {
            if (character == null)
            {
                DevLog("[BossRush] ApplyBossStatMultiplier: character 为 null，跳过");
                return;
            }

            // 获取倍率值：优先使用传入参数，否则从配置读取
            float mult = multiplier ?? (config != null ? config.bossStatMultiplier : 1f);
            
            DevLog("[BossRush] ApplyBossStatMultiplier 开始: mult=" + mult);
            
            // 倍率为 1 时无需处理
            if (Mathf.Approximately(mult, 1f))
            {
                DevLog("[BossRush] ApplyBossStatMultiplier: 倍率为 1，跳过");
                return;
            }

            try
            {
                var item = character.CharacterItem;
                if (item == null)
                {
                    DevLog("[BossRush] ApplyBossStatMultiplier: CharacterItem 为 null，跳过");
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
                        DevLog("[BossRush] ApplyBossStatMultiplier: MaxHealth " + oldHp + " -> " + hpStat.BaseValue + " (x" + mult + ")");
                    }
                    else
                    {
                        DevLog("[BossRush] ApplyBossStatMultiplier: MaxHealth Stat 为 null");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] ApplyBossStatMultiplier 修改 MaxHealth 失败: " + e.Message);
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
                        DevLog("[BossRush] ApplyBossStatMultiplier: GunDamageMultiplier " + oldDmg + " -> " + gunDmg.BaseValue + " (x" + dmgMult + ", 原倍率=" + mult + ")");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] ApplyBossStatMultiplier 修改 GunDamageMultiplier 失败: " + e.Message);
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
                        DevLog("[BossRush] ApplyBossStatMultiplier: MeleeDamageMultiplier " + oldDmg + " -> " + meleeDmg.BaseValue + " (x" + dmgMult + ", 原倍率=" + mult + ")");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] ApplyBossStatMultiplier 修改 MeleeDamageMultiplier 失败: " + e.Message);
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
                        
                        DevLog("[BossRush] ApplyBossStatMultiplier: reactionTime " + oldReaction + " -> " + ai.baseReactionTime + " (/" + mult + ")");
                        DevLog("[BossRush] ApplyBossStatMultiplier: shootDelay " + oldShootDelay + " -> " + ai.shootDelay + " (/" + mult + ")");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] ApplyBossStatMultiplier 修改反应速度失败: " + e.Message);
                }

                DevLog("[BossRush] ApplyBossStatMultiplier 完成");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ApplyBossStatMultiplier 失败: " + e.Message);
            }
        }
    }
}

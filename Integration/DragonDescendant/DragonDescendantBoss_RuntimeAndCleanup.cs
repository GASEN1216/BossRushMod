// ============================================================================
// DragonDescendantBoss.cs - 龙裔遗族Boss主控制器
// ============================================================================
// 模块说明：
//   管理龙裔遗族Boss的生成、装备和生命周期
//   作为ModBehaviour的partial class实现
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 根据口径查找子弹（带缓存）
        /// [性能优化] 使用ItemAssetsCollection替代Resources.FindObjectsOfTypeAll
        /// </summary>
        private Item FindBulletByCaliber(string caliber)
        {
            // 检查缓存
            Item cachedBullet;
            if (cachedBulletsByCaliber.TryGetValue(caliber, out cachedBullet))
            {
                return cachedBullet;
            }

            try
            {
                Item bestBullet = null;
                int bestQuality = -1;
                int caliberHash = "Caliber".GetHashCode();

                // 获取Bullet Tag
                Duckov.Utilities.Tag bulletTag = null;
                try
                {
                    bulletTag = Duckov.Utilities.GameplayDataSettings.Tags.Bullet;
                }
                catch { }

                // [性能优化] 使用ItemAssetsCollection遍历，避免Resources.FindObjectsOfTypeAll
                var itemAssets = ItemAssetsCollection.Instance;
                if (itemAssets != null && itemAssets.entries != null)
                {
                    foreach (var entry in itemAssets.entries)
                    {
                        if (entry == null || entry.prefab == null) continue;

                        // 检查是否是子弹（通过Tag判断）
                        if (bulletTag != null && !entry.prefab.Tags.Contains(bulletTag)) continue;

                        // 检查口径是否匹配
                        string itemCaliber = entry.prefab.Constants.GetString(caliberHash, null);
                        if (string.IsNullOrEmpty(itemCaliber) || itemCaliber != caliber) continue;

                        // 获取品质
                        int quality = 0;
                        try
                        {
                            quality = (int)entry.prefab.Quality;
                        }
                        catch { }

                        // 选择最高品质的子弹
                        if (bestBullet == null || quality > bestQuality)
                        {
                            bestQuality = quality;
                            bestBullet = entry.prefab;
                        }
                    }
                }

                // 缓存结果
                if (bestBullet != null)
                {
                    cachedBulletsByCaliber[caliber] = bestBullet;
                }

                return bestBullet;
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 查找子弹失败: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 通过名称查找物品（带缓存）
        /// [性能优化] 使用ItemAssetsCollection替代Resources.FindObjectsOfTypeAll
        /// </summary>
        private Item FindItemByName(string itemName)
        {
            // 检查缓存
            Item cachedItem;
            if (cachedItemsByName.TryGetValue(itemName, out cachedItem))
            {
                return cachedItem;
            }

            try
            {
                // [性能优化] 使用ItemAssetsCollection遍历
                var itemAssets = ItemAssetsCollection.Instance;
                if (itemAssets == null || itemAssets.entries == null) return null;

                // 精确匹配
                foreach (var entry in itemAssets.entries)
                {
                    if (entry == null || entry.prefab == null) continue;
                    var item = entry.prefab;

                    if (item.name == itemName || item.DisplayName == itemName)
                    {
                        cachedItemsByName[itemName] = item;
                        return item;
                    }
                }

                // 模糊匹配
                foreach (var entry in itemAssets.entries)
                {
                    if (entry == null || entry.prefab == null) continue;
                    var item = entry.prefab;

                    if (item.name.Contains(itemName) ||
                        (item.DisplayName != null && item.DisplayName.Contains(itemName)))
                    {
                        cachedItemsByName[itemName] = item;
                        return item;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 设置AI仇恨
        /// Mode E 模式下不强制追踪玩家，让 AI 自然寻敌
        /// </summary>
        private void SetupAIAggro(CharacterMainControl character)
        {
            try
            {
                var ai = character.GetComponentInChildren<AICharacterController>();
                if (ai == null) return;

                // Mode E 模式：不主动设置目标，让 AI 自然感知范围内的敌人后再开打
                if (modeEActive)
                {
                    return;
                }

                // 非 Mode E：强制追踪玩家
                var main = CharacterMainControl.Main;
                if (main == null) return;

                if (main.mainDamageReceiver != null)
                {
                    ai.forceTracePlayerDistance = 500f;
                    ai.searchedEnemy = main.mainDamageReceiver;
                    ai.SetTarget(main.mainDamageReceiver.transform);
                    ai.SetNoticedToTarget(main.mainDamageReceiver);
                    ai.noticed = true;
                }
            }
            catch { }
        }

        /// <summary>
        /// 龙裔遗族死亡回调
        /// </summary>
        private void OnDragonDescendantDeath(DamageInfo damageInfo)
        {
            try
            {
                DevLog("[DragonDescendant] 龙裔遗族被击败");

                // 取消注册龙套装效果
                UnregisterDragonDescendantSetBonus();

                // 注意：龙套装专属掉落逻辑已由 Boss 掉落箱协程统一处理
                // 在 OnBossBeforeSpawnLoot_LootAndRewards -> AddBossSpecialLootToLootboxCoroutine 中追加

                // 清理引用
                if (dragonDescendantInstance != null && dragonDescendantInstance.Health != null)
                {
                    dragonDescendantInstance.Health.OnDeadEvent.RemoveListener(OnDragonDescendantDeath);
                }

                BossCleanupHelpers.DestroyRuntimePreset(
                    dragonDescendantInstance,
                    DragonDescendantConfig.BOSS_NAME_KEY,
                    "DragonDescendant_Preset",
                    "[DragonDescendant]");

                dragonDescendantInstance = null;
                dragonDescendantAbilities = null;

                // 触发BossRush标准死亡处理（如果在BossRush模式中）
                // 这会由BossRush系统的OnEnemyDiedWithDamageInfo自动处理
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 死亡处理失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理龙裔遗族Boss
        /// </summary>
        public void CleanupDragonDescendant()
        {
            try
            {
                // 取消注册龙套装效果
                UnregisterDragonDescendantSetBonus();

                if (dragonDescendantInstance != null)
                {
                    if (dragonDescendantInstance.Health != null)
                    {
                        dragonDescendantInstance.Health.OnDeadEvent.RemoveListener(OnDragonDescendantDeath);
                    }

                    if (currentBoss == dragonDescendantInstance)
                    {
                        currentBoss = null;
                    }

                    ClearBossRandomLootTracking(dragonDescendantInstance);
                    BossCleanupHelpers.DestroyRuntimePreset(
                        dragonDescendantInstance,
                        DragonDescendantConfig.BOSS_NAME_KEY,
                        "DragonDescendant_Preset",
                        "[DragonDescendant]");

                    UnityEngine.Object.Destroy(dragonDescendantInstance.gameObject);
                    dragonDescendantInstance = null;
                }

                dragonDescendantAbilities = null;
            }
            catch { }
        }


        // ========== BossRush系统集成 ==========

        /// <summary>
        /// 注册龙裔遗族到BossRush敌人预设系统
        /// 注意：每次 enemyPresets 被清空后都需要重新注册，所以不能依赖 static 标记
        /// </summary>
        private void RegisterDragonDescendantPreset()
        {
            try
            {
                if (enemyPresets == null)
                {
                    DevLog("[DragonDescendant] [WARNING] enemyPresets 为空，无法注册");
                    return;
                }

                // 检查是否已存在（每次都检查，因为 enemyPresets 可能被清空重建）
                bool exists = false;
                foreach (var p in enemyPresets)
                {
                    if (p != null && p.name == DragonDescendantConfig.BOSS_NAME_KEY)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    DevLog("[DragonDescendant] 已存在于预设列表中，跳过注册");
                    return;
                }

                // 创建EnemyPresetInfo
                var presetInfo = new EnemyPresetInfo
                {
                    name = DragonDescendantConfig.BOSS_NAME_KEY,
                    displayName = DragonDescendantConfig.BOSS_NAME_CN,
                    team = (int)Teams.wolf, // 狼群阵营（Mode E 阵营体系统一）
                    baseHealth = DragonDescendantConfig.BaseHealth,
                    baseDamage = 50f,
                    healthMultiplier = 1f,
                    damageMultiplier = DragonDescendantConfig.DamageMultiplier,
                    expReward = 500
                };

                enemyPresets.Add(presetInfo);
                dragonDescendantRegistered = true;
                DevLog("[DragonDescendant] 已注册到BossRush敌人预设系统");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [WARNING] 注册预设失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查是否是龙裔遗族预设
        /// </summary>
        private bool IsDragonDescendantPreset(EnemyPresetInfo preset)
        {
            if (preset == null) return false;
            return preset.name == DragonDescendantConfig.BOSS_NAME_KEY ||
                   preset.displayName == DragonDescendantConfig.BOSS_NAME_CN ||
                   preset.displayName == DragonDescendantConfig.BOSS_NAME_EN;
        }

        // ========== Boss龙套装效果 ==========

        /// <summary>
        /// 注册Boss龙套装效果（火焰伤害免疫）
        /// </summary>
        private void RegisterDragonDescendantSetBonus()
        {
            if (dragonDescendantSetBonusRegistered) return;

            try
            {
                // 缓存Health引用用于快速身份验证
                if (dragonDescendantInstance != null && dragonDescendantInstance.Health != null)
                {
                    cachedBossHealth = dragonDescendantInstance.Health;
                    Health.OnHurt += OnDragonDescendantHurt;
                    dragonDescendantSetBonusRegistered = true;
                    DevLog("[DragonDescendant] 已注册龙套装效果（火焰免疫），Health引用已缓存");
                }
                else
                {
                    DevLog("[DragonDescendant] [WARNING] 注册龙套装效果失败：Boss实例或Health为空");
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] 注册龙套装效果失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消注册Boss龙套装效果
        /// </summary>
        private void UnregisterDragonDescendantSetBonus()
        {
            if (!dragonDescendantSetBonusRegistered) return;

            try
            {
                Health.OnHurt -= OnDragonDescendantHurt;
                cachedBossHealth = null; // 清理缓存
                dragonDescendantSetBonusRegistered = false;
                DevLog("[DragonDescendant] 已取消注册龙套装效果");
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] 取消注册龙套装效果失败: " + e.Message);
            }
        }

        /// <summary>
        /// Boss伤害事件回调 - 火焰伤害免疫并转化为治疗
        /// [性能优化] 使用缓存的Health引用进行快速身份验证
        /// </summary>
        private void OnDragonDescendantHurt(Health health, DamageInfo damageInfo)
        {
            // [性能优化] 快速过滤：使用缓存引用直接比较
            if (cachedBossHealth == null || health != cachedBossHealth) return;

            try
            {
                // 检查是否有火焰伤害（快速路径）
                if (damageInfo.elementFactors == null || damageInfo.elementFactors.Count == 0) return;

                // 使用 finalDamage 计算火焰伤害占比
                float totalFinalDamage = damageInfo.finalDamage;
                if (totalFinalDamage <= 0f) return;

                // 计算火焰伤害占比
                float fireFactor = 0f;
                float totalFactor = 0f;
                var factors = damageInfo.elementFactors;
                int count = factors.Count;
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.factor > 0f)
                    {
                        totalFactor += ef.factor;
                        if (ef.elementType == ElementTypes.fire)
                        {
                            fireFactor += ef.factor;
                        }
                    }
                }

                // 没有火焰伤害则跳过
                if (fireFactor <= 0f || totalFactor <= 0f) return;

                // 计算火焰伤害在最终伤害中的占比，全部转化为治疗
                float fireRatio = fireFactor / totalFactor;
                float actualFireDamage = totalFinalDamage * fireRatio;
                float fireHealAmount = actualFireDamage; // 100% 转化为治疗

                // 将火焰伤害因子设为0（免疫火焰伤害）
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.elementType == ElementTypes.fire && ef.factor > 0f)
                    {
                        factors[i] = new ElementFactor(ElementTypes.fire, 0f);
                    }
                }

                DevLog("[DragonDescendant] Boss火焰伤害吸收: " + actualFireDamage.ToString("F1") + " -> 治疗: " + fireHealAmount.ToString("F1"));

                // 延迟治疗（在伤害计算完成后）
                if (fireHealAmount > 0f)
                {
                    StartCoroutine(DelayedBossHeal(health, fireHealAmount));
                }
            }
            catch (Exception e)
            {
                DevLog("[DragonDescendant] [ERROR] OnDragonDescendantHurt 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟治疗Boss（在伤害计算完成后）
        /// </summary>
        private System.Collections.IEnumerator DelayedBossHeal(Health health, float amount)
        {
            yield return null; // 等待一帧

            if (health != null && !health.IsDead)
            {
                health.AddHealth(amount);
                DevLog("[DragonDescendant] Boss火焰能量治疗: +" + amount.ToString("F1"));

                // 显示治疗数字
                try
                {
                    if (dragonDescendantInstance != null)
                    {
                        FX.PopText.Pop("+" + amount.ToString("F0"),
                            dragonDescendantInstance.transform.position + Vector3.up * 2.5f,
                            new Color(0.2f, 1f, 0.2f), 1.2f, null);
                    }
                }
                catch { }
            }
        }
    }
}

using System;
using System.Collections;
using UnityEngine;
using ItemStatsSystem;
using Duckov.ItemUsage;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal bool IsDragonDescendantBoss(CharacterMainControl boss)
        {
            if (boss == null) return false;

            try
            {
                // 方法1：检查GameObject名称（SpawnDragonDescendant中设置为"BossRush_DragonDescendant"）
                if (boss.gameObject != null && boss.gameObject.name.Contains("DragonDescendant"))
                {
                    return true;
                }

                // 方法2：检查预设nameKey
                if (boss.characterPreset != null &&
                    boss.characterPreset.nameKey == DragonDescendantConfig.BOSS_NAME_KEY)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("IsDragonDescendantBoss", "判断龙裔遗族 Boss 失败", e);
            }

            return false;
        }

        /// <summary>
        /// 判断Boss是否是龙王（支持多Boss模式）
        /// 通过GameObject名称或预设nameKey判断
        /// </summary>
        internal bool IsDragonKingBoss(CharacterMainControl boss)
        {
            if (boss == null) return false;

            try
            {
                // 方法1：检查GameObject名称（SpawnDragonKing中设置为"BossRush_DragonKing"）
                if (boss.gameObject != null && boss.gameObject.name.Contains("DragonKing"))
                {
                    return true;
                }

                // 方法2：检查预设nameKey
                if (boss.characterPreset != null &&
                    boss.characterPreset.nameKey == DragonKingConfig.BossNameKey)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("IsDragonKingBoss", "判断龙王 Boss 失败", e);
            }

            return false;
        }


        internal IEnumerator AddDragonDescendantLoot(Inventory inv)
        {
            // 按概率随机选择掉落物品：龙息10%、龙头30%、龙甲60%
            float roll = UnityEngine.Random.Range(0f, 1f);
            int selectedTypeId;
            string itemName;

            if (roll < DragonDescendantConfig.DROP_CHANCE_WEAPON)
            {
                selectedTypeId = DragonDescendantConfig.DRAGON_BREATH_TYPE_ID;
                itemName = "龙息";
            }
            else if (roll < DragonDescendantConfig.DROP_CHANCE_WEAPON + DragonDescendantConfig.DROP_CHANCE_HELM)
            {
                selectedTypeId = DragonDescendantConfig.DRAGON_HELM_TYPE_ID;
                itemName = "龙头";
            }
            else
            {
                selectedTypeId = DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID;
                itemName = "龙甲";
            }

            ModBehaviour.DevLog("[DragonDescendant] 随机选择龙套装掉落: " + itemName + " (TypeID=" + selectedTypeId + ", roll=" + roll.ToString("F3") + ")");

            try
            {
                if (!EnsureDragonBossRewardPrefabLoaded(selectedTypeId, "[DragonDescendant]"))
                {
                    yield break;
                }

                Item newItem = ItemAssetsCollection.InstantiateSync(selectedTypeId);
                if (newItem == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] 创建龙套装实例失败: TypeID=" + selectedTypeId);
                    yield break;
                }

                // 确保耐久度为满
                float maxDurability = newItem.MaxDurability;
                if (maxDurability > 0)
                {
                    newItem.Durability = maxDurability;
                    newItem.DurabilityLoss = 0f;
                }

                // 如果是龙息武器，需要配置武器属性
                if (selectedTypeId == DragonDescendantConfig.DRAGON_BREATH_TYPE_ID)
                {
                    DragonBreathWeaponConfig.ConfigureWeapon(newItem);
                    ModBehaviour.DevLog("[DragonDescendant] 已配置龙息武器属性");
                }

                if (!InteractableLootboxInventoryHelper.TryAddExtraItem(inv, newItem)) yield break;
                ModBehaviour.DevLog("[DragonDescendant] 已将 " + itemName + " 添加到掉落箱");

                // 记录收藏龙裔掉落物（用于成就追踪）
                try
                {
                    AchievementTracker.OnCollectDragonDescendantLoot(selectedTypeId);
                    CheckDragonDescendantCollectionAchievement();
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("AddDragonDescendantLoot_achievement", "记录龙裔收藏成就失败", e);
                }
            }
            catch (Exception addEx)
            {
                ModBehaviour.DevLog("[DragonDescendant] 添加龙套装到掉落箱失败: " + addEx.Message);
            }

            yield break;
        }

        /// <summary>
        /// 添加龙王专属掉落物到Inventory
        /// 共享掉落格：飞行图腾15%、龙王之冕15%、龙王鳞铠15%、焚皇断界戟15%、焚天龙铳1%、逆鳞39%
        /// </summary>
        internal IEnumerator AddDragonKingLoot(Inventory inv)
        {
            // 按概率随机选择掉落物品
            float roll = UnityEngine.Random.Range(0f, 1f);
            int selectedTypeId;
            string itemName;

            float threshold1 = DragonKingConfig.DROP_CHANCE_FLIGHT_TOTEM;           // 0.15
            float threshold2 = threshold1 + DragonKingConfig.DROP_CHANCE_CROWN;      // 0.30
            float threshold3 = threshold2 + DragonKingConfig.DROP_CHANCE_ARMOR;      // 0.45
            float threshold4 = threshold3 + DragonKingConfig.DROP_CHANCE_HALBERD;    // 0.60
            float threshold5 = threshold4 + DragonKingConfig.DROP_CHANCE_BOSS_GUN;   // 0.61

            if (roll < threshold1)
            {
                // 15% 飞行图腾
                selectedTypeId = DragonKingConfig.DRAGON_KING_LOOT_TYPE_ID;
                itemName = "腾云驾雾 I";
            }
            else if (roll < threshold2)
            {
                // 15% 龙王之冕
                selectedTypeId = DragonKingConfig.DRAGON_KING_HELM_TYPE_ID;
                itemName = "龙王之冕";
            }
            else if (roll < threshold3)
            {
                // 15% 龙王鳞铠
                selectedTypeId = DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID;
                itemName = "龙王鳞铠";
            }
            else if (roll < threshold4)
            {
                // 15% 焚皇断界戟
                selectedTypeId = DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID;
                itemName = "焚皇断界戟";
            }
            else if (roll < threshold5)
            {
                // 1% 焚天龙铳
                selectedTypeId = DragonKingBossGunConfig.WeaponTypeId;
                itemName = DragonKingBossGunConfig.WeaponNameCN;
            }
            else
            {
                // 39% 逆鳞
                selectedTypeId = DragonKingConfig.REVERSE_SCALE_TYPE_ID;
                itemName = "逆鳞";
            }

            ModBehaviour.DevLog("[DragonKing] 随机选择龙王掉落: " + itemName + " (TypeID=" + selectedTypeId + ", roll=" + roll.ToString("F3") + ")");

            try
            {
                if (!TryAddDragonKingLootItem(inv, selectedTypeId, itemName))
                {
                    yield break;
                }
            }
            catch (Exception addEx)
            {
                ModBehaviour.DevLog("[DragonKing] 添加掉落物到掉落箱失败: " + addEx.Message);
            }

            yield break;
        }

        internal bool TryAddDragonKingLootItem(Inventory inv, int typeId, string itemName)
        {
            try
            {
                if (!EnsureDragonBossRewardPrefabLoaded(typeId, "[DragonKing]"))
                {
                    return false;
                }

                Item newItem = ItemAssetsCollection.InstantiateSync(typeId);
                if (newItem == null)
                {
                    ModBehaviour.DevLog("[DragonKing] 创建掉落物实例失败: TypeID=" + typeId);
                    return false;
                }

                float maxDurability = newItem.MaxDurability;
                if (maxDurability > 0f)
                {
                    newItem.Durability = maxDurability;
                    newItem.DurabilityLoss = 0f;
                }

                if (!InteractableLootboxInventoryHelper.TryAddExtraItem(inv, newItem)) return false;
                ModBehaviour.DevLog("[DragonKing] 已将 " + itemName + " 添加到掉落箱");

                try
                {
                    AchievementTracker.OnCollectDragonKingLoot(typeId);
                    CheckDragonKingCollectionAchievement();
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("TryAddDragonKingLootItem_achievement", "记录龙王收藏成就失败", e);
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] 添加掉落物失败: " + itemName + " - " + e.Message);
                return false;
            }
        }

        private bool EnsureDragonBossRewardPrefabLoaded(int typeId, string logPrefix)
        {
            if (BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId))
            {
                return true;
            }

            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 兜底按需注册奖励 prefab 失败: " + e.Message);
            }

            if (BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId))
            {
                return true;
            }

            ModBehaviour.DevLog(logPrefix + " [WARNING] 掉落前仍未找到奖励 prefab: TypeID=" + typeId);
            return false;
        }

        /// <summary>
        /// 检查龙裔收藏成就
        /// </summary>
        private void CheckDragonDescendantCollectionAchievement()
        {
            // 龙裔专属掉落物列表：龙息、龙头、龙甲
            int[] requiredItems = new int[]
            {
                DragonDescendantConfig.DRAGON_BREATH_TYPE_ID,
                DragonDescendantConfig.DRAGON_HELM_TYPE_ID,
                DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID
            };

            bool allCollected = true;
            foreach (int typeId in requiredItems)
            {
                if (!AchievementTracker.CollectedDragonDescendantLoot.Contains(typeId))
                {
                    allCollected = false;
                    break;
                }
            }

            if (allCollected)
            {
                BossRushAchievementManager.TryUnlock("collect_dragon_descendant_loot");
            }
        }

        /// <summary>
        /// 检查龙王收藏成就
        /// </summary>
        private void CheckDragonKingCollectionAchievement()
        {
            // 龙王专属掉落物列表：飞行图腾、龙王之冕、龙王鳞铠、焚皇断界戟、逆鳞、焚天龙铳
            int[] requiredItems = new int[]
            {
                DragonKingConfig.DRAGON_KING_LOOT_TYPE_ID,
                DragonKingConfig.DRAGON_KING_HELM_TYPE_ID,
                DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID,
                DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID,
                DragonKingConfig.REVERSE_SCALE_TYPE_ID,
                DragonKingBossGunConfig.WeaponTypeId
            };

            bool allCollected = true;
            foreach (int typeId in requiredItems)
            {
                if (!AchievementTracker.CollectedDragonKingLoot.Contains(typeId))
                {
                    allCollected = false;
                    break;
                }
            }

            if (allCollected)
            {
                BossRushAchievementManager.TryUnlock("collect_dragon_king_loot");
            }
        }
    }
}

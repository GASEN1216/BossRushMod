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


        /// <summary>龙裔遗族专属掉落抽签：龙息10%、龙头30%、龙甲60%。箱子与无间炼狱落地共用。</summary>
        internal static int RollDragonDescendantLootTypeId(float roll, out string itemName)
        {
            if (roll < DragonDescendantConfig.DROP_CHANCE_WEAPON)
            {
                itemName = "龙息";
                return DragonDescendantConfig.DRAGON_BREATH_TYPE_ID;
            }
            if (roll < DragonDescendantConfig.DROP_CHANCE_WEAPON + DragonDescendantConfig.DROP_CHANCE_HELM)
            {
                itemName = "龙头";
                return DragonDescendantConfig.DRAGON_HELM_TYPE_ID;
            }
            itemName = "龙甲";
            return DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID;
        }

        /// <summary>
        /// 龙王专属掉落抽签。共享掉落格：飞行图腾15%、龙王之冕15%、龙王鳞铠15%、焚皇断界戟15%、焚天龙铳1%、逆鳞39%。
        /// </summary>
        internal static int RollDragonKingLootTypeId(float roll, out string itemName)
        {
            float threshold1 = DragonKingConfig.DROP_CHANCE_FLIGHT_TOTEM;           // 0.15
            float threshold2 = threshold1 + DragonKingConfig.DROP_CHANCE_CROWN;      // 0.30
            float threshold3 = threshold2 + DragonKingConfig.DROP_CHANCE_ARMOR;      // 0.45
            float threshold4 = threshold3 + DragonKingConfig.DROP_CHANCE_HALBERD;    // 0.60
            float threshold5 = threshold4 + DragonKingConfig.DROP_CHANCE_BOSS_GUN;   // 0.61

            if (roll < threshold1) { itemName = "腾云驾雾 I"; return DragonKingConfig.DRAGON_KING_LOOT_TYPE_ID; }
            if (roll < threshold2) { itemName = "龙王之冕"; return DragonKingConfig.DRAGON_KING_HELM_TYPE_ID; }
            if (roll < threshold3) { itemName = "龙王鳞铠"; return DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID; }
            if (roll < threshold4) { itemName = "焚皇断界戟"; return DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID; }
            if (roll < threshold5) { itemName = DragonKingBossGunConfig.WeaponNameCN; return DragonKingBossGunConfig.WeaponTypeId; }
            itemName = "逆鳞";
            return DragonKingConfig.REVERSE_SCALE_TYPE_ID;
        }

        /// <summary>创建一件满耐久的龙系奖励（龙息额外配置武器属性）；失败返回 null。</summary>
        private Item CreateDragonRewardItem(int typeId, string logPrefix)
        {
            if (!EnsureDragonBossRewardPrefabLoaded(typeId, logPrefix)) return null;
            Item newItem = ItemAssetsCollection.InstantiateSync(typeId);
            if (newItem == null)
            {
                ModBehaviour.DevLog(logPrefix + " 创建奖励实例失败: TypeID=" + typeId);
                return null;
            }
            float maxDurability = newItem.MaxDurability;
            if (maxDurability > 0f)
            {
                newItem.Durability = maxDurability;
                newItem.DurabilityLoss = 0f;
            }
            if (typeId == DragonDescendantConfig.DRAGON_BREATH_TYPE_ID)
            {
                DragonBreathWeaponConfig.ConfigureWeapon(newItem);
                ModBehaviour.DevLog(logPrefix + " 已配置龙息武器属性");
            }
            return newItem;
        }

        /// <summary>
        /// 无间炼狱没有奖励箱，龙皇 / 龙裔的专属掉落以前因此整份丢失（玩家：打无间炼狱龙皇什么都不掉）。
        /// 这里按同一张掉落表抽一件，落在 Boss 尸体处，成就记录与进箱路径一致。
        /// </summary>
        internal void DropDragonExclusiveLootIntoWorld(CharacterMainControl bossMain)
        {
            if (bossMain == null) return;
            bool descendant = IsDragonDescendantBoss(bossMain);
            if (!descendant && !IsDragonKingBoss(bossMain)) return;
            string prefix = descendant ? "[DragonDescendant]" : "[DragonKing]";
            string itemName;
            int typeId = descendant
                ? RollDragonDescendantLootTypeId(UnityEngine.Random.Range(0f, 1f), out itemName)
                : RollDragonKingLootTypeId(UnityEngine.Random.Range(0f, 1f), out itemName);
            Item item = null;
            try
            {
                item = CreateDragonRewardItem(typeId, prefix);
                if (item == null) return;
                Vector3 position = bossMain.transform.position + Vector3.up * 0.1f;
                item.Drop(position, true, UnityEngine.Random.insideUnitSphere.normalized, UnityEngine.Random.Range(30f, 60f));
                item = null;
                ModBehaviour.DevLog(prefix + " 无间炼狱专属掉落（世界掉落）: " + itemName + " TypeID=" + typeId);
                if (descendant)
                {
                    AchievementTracker.OnCollectDragonDescendantLoot(typeId);
                    CheckDragonDescendantCollectionAchievement();
                }
                else
                {
                    AchievementTracker.OnCollectDragonKingLoot(typeId);
                    CheckDragonKingCollectionAchievement();
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("InfiniteHellDragonDrop", "无间炼狱龙系专属掉落失败", e);
            }
            finally
            {
                if (item != null)
                {
                    try { item.DestroyTree(); } catch (Exception) { /* 未交付实例清理失败不阻断 */ }
                }
            }
        }

        internal IEnumerator AddDragonDescendantLoot(Inventory inv)
        {
            // 按概率随机选择掉落物品：龙息10%、龙头30%、龙甲60%
            float roll = UnityEngine.Random.Range(0f, 1f);
            string itemName;
            int selectedTypeId = RollDragonDescendantLootTypeId(roll, out itemName);

            ModBehaviour.DevLog("[DragonDescendant] 随机选择龙套装掉落: " + itemName + " (TypeID=" + selectedTypeId + ", roll=" + roll.ToString("F3") + ")");

            try
            {
                Item newItem = CreateDragonRewardItem(selectedTypeId, "[DragonDescendant]");
                if (newItem == null) yield break;

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
            string itemName;
            int selectedTypeId = RollDragonKingLootTypeId(roll, out itemName);

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
                Item newItem = CreateDragonRewardItem(typeId, "[DragonKing]");
                if (newItem == null) return false;

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

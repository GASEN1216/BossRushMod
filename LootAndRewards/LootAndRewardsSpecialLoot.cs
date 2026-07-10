// ============================================================================
// LootAndRewardsSpecialLoot.cs - Boss 特殊掉落与清理
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 调试：记录 Boss 掉落实际物品列表（LootAndRewards 分部实现）
        /// </summary>
        private IEnumerator LogBossLootInventory_LootAndRewards(InteractableLootbox lootbox)
        {
            if (lootbox == null)
            {
                yield break;
            }

            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                yield break;
            }

            // 等待 LootBoxLoader 完成填充
            int tries = 0;
            const int maxTries = 50;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            List<Item> content = inv.Content;
            if (content == null)
            {
                yield break;
            }

            try
            {
                DevLog("[BossRush] Boss 掉落实际物品列表开始, 总数=" + content.Count);
            }
            catch (Exception)
            {
                // 调试输出失败不应影响后续逻辑。
            }

            for (int i = 0; i < content.Count; i++)
            {
                Item item = content[i];
                if (item == null)
                {
                    continue;
                }

                int q = -1;
                int v = -1;
                string name = "<unknown>";
                string displayQ = "<unknown>";

                try
                {
                    q = item.Quality;
                }
                catch (Exception)
                {
                    q = -1;
                }

                try
                {
                    v = item.Value;
                }
                catch (Exception)
                {
                    v = -1;
                }

                try
                {
                    name = item.DisplayName;
                }
                catch (Exception)
                {
                    name = "<unknown>";
                }

                try
                {
                    displayQ = item.DisplayQuality.ToString();
                }
                catch (Exception)
                {
                    displayQ = "<unknown>";
                }

                DevLog("[BossRush] 实际掉落物: typeID=" + item.TypeID + ", 名称=" + name + ", Quality=" + q + ", DisplayQuality=" + displayQ + ", Value=" + v);
            }

            try
            {
                DevLog("[BossRush] Boss 掉落实际物品列表结束");
            }
            catch (Exception)
            {
                // 调试输出失败不应影响奖励箱处理。
            }

            // LootBoxLoader 填充完成后，根据实际物品数量调整 Inventory 容量
            // 这是解决"格子为64"问题的关键
            try
            {
                int lastPos = inv.GetLastItemPosition();
                int newCapacity = Mathf.Max(8, lastPos + 1);
                inv.SetCapacity(newCapacity);
                DevLog("[BossRush] Boss 奖励箱容量已调整为: " + newCapacity);
            }
            catch (Exception capEx)
            {
                DevLog("[BossRush] 调整 Boss 奖励箱容量失败: " + capEx.Message);
            }
        }

        private bool InventoryContainsItemAtLeastQuality(Inventory inv, int minimumQuality)
        {
            if (inv == null)
            {
                return false;
            }

            try
            {
                List<Item> content = inv.Content;
                if (content == null)
                {
                    return false;
                }

                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    int quality = 0;
                    try
                    {
                        quality = item.Quality;
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("InventoryContainsItemAtLeastQuality_quality", "读取掉落箱物品品质失败", e);
                    }

                    if (quality >= minimumQuality)
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("InventoryContainsItemAtLeastQuality_scan", "扫描掉落箱保底品质失败", e);
            }

            return false;
        }

        private int GetLegacyBossGuaranteeTypeId(int desiredQuality, out int actualQuality)
        {
            actualQuality = -1;

            // 复用实例级 scratch 容器，避免每次保底判定都分配候选列表/品质桶字典。
            // TryGetLegacyBossLootCandidates 内部会先 Clear 候选列表并重建品质桶，
            // 这里仅需在使用前清空品质桶的复用列表，使用后由 finally 再次清空释放引用。
            List<int> candidateIds = legacyBossGuaranteeCandidateScratch;
            Dictionary<int, List<int>> qualityBuckets = legacyBossGuaranteeQualityBucketsScratch;
            ClearLegacyBossGuaranteeQualityBucketsScratch();

            try
            {
                if (!TryGetLegacyBossLootCandidates(candidateIds, qualityBuckets))
                {
                    return -1;
                }

                for (int quality = desiredQuality; quality >= 5; quality--)
                {
                    List<int> bucket = null;
                    if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                    {
                        continue;
                    }

                    actualQuality = quality;
                    return bucket[UnityEngine.Random.Range(0, bucket.Count)];
                }

                return -1;
            }
            finally
            {
                candidateIds.Clear();
                ClearLegacyBossGuaranteeQualityBucketsScratch();
            }
        }

        private bool TryAddLegacyBossGuaranteeItem(Inventory inv)
        {
            if (inv == null)
            {
                return false;
            }

            if (InventoryContainsItemAtLeastQuality(inv, 5))
            {
                return false;
            }

            int desiredQuality = LegacyBossLootProbabilityModel.RollGuaranteeQuality(UnityEngine.Random.value);
            int actualQuality = -1;
            int rewardTypeId = GetLegacyBossGuaranteeTypeId(desiredQuality, out actualQuality);
            if (rewardTypeId <= 0)
            {
                DevLog("[BossRush] [WARNING] 原版战利品保底失败：未找到可用的 Q5+ 候选，desiredQuality=" + desiredQuality);
                return false;
            }

            Item reward = null;
            try
            {
                reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                if (reward == null)
                {
                    DevLog("[BossRush] [WARNING] 原版战利品保底失败：实例化物品失败, typeId=" + rewardTypeId);
                    return false;
                }

                if (!inv.AddItem(reward))
                {
                    DevLog("[BossRush] [WARNING] 原版战利品保底失败：AddItem 失败, typeId=" + rewardTypeId);
                    return false;
                }

                DevLog("[BossRush] 原版战利品保底已追加: desiredQuality=" + desiredQuality + ", actualQuality=" + actualQuality + ", typeId=" + rewardTypeId);
                reward = null;
                return true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 原版战利品保底追加失败: " + e.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (reward != null)
                    {
                        reward.DestroyTree();
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("TryAddLegacyBossGuaranteeItem_cleanup", "清理未成功追加的保底物品失败", e);
                }
            }
        }

        /// <summary>
        /// 清理通关奖励 Lootbox 中的低品质/低价值物品，仅保留高品质奖励（LootAndRewards 分部实现）
        /// </summary>
        private IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(InteractableLootbox lootbox, int highQualityCount)
        {
            if (lootbox == null)
            {
                yield break;
            }

            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                yield break;
            }

            // 等待一小段时间，给 LootBoxLoader.Setup 机会完成
            const int maxTries = 30;
            int tries = 0;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            try
            {
                List<Item> content = inv.Content;
                if (content == null || content.Count == 0)
                {
                    yield break;
                }

                // 调试：清理前先输出一次通关奖励箱实际物品列表
                try
                {
                    DevLog("[BossRush] 通关奖励清理前物品列表开始, 总数=" + content.Count);
                    for (int i = 0; i < content.Count; i++)
                    {
                        Item item = content[i];
                        if (item == null) continue;

                        int q = -1;
                        int v = -1;
                        string name = "<unknown>";
                        string displayQ = "<unknown>";

                        try { q = item.Quality; } catch (Exception) { q = -1; }
                        try { v = item.Value; } catch (Exception) { v = -1; }
                        try { name = item.DisplayName; } catch (Exception) { name = "<unknown>"; }
                        try { displayQ = item.DisplayQuality.ToString(); } catch (Exception) { displayQ = "<unknown>"; }

                        DevLog("[BossRush] 通关奖励实际物品(清理前): typeID=" + item.TypeID + ", 名称=" + name + ", Quality=" + q + ", DisplayQuality=" + displayQ + ", Value=" + v);
                    }
                    DevLog("[BossRush] 通关奖励清理前物品列表结束");
                }
                catch (Exception)
                {
                    // 调试输出失败不影响通关奖励清理逻辑。
                }

                int beforeCount = content.Count;

                // 按品质和价格分两档：高品质高价、高品质低价。
                // 低品质物品（Quality<5）一律不保留，无需单独装桶。
                // 复用实例级 scratch 列表，避免每次清理都分配。
                List<Item> preferred = difficultyRewardPreferredScratch;
                List<Item> fallbackHighQuality = difficultyRewardFallbackHighQualityScratch;
                preferred.Clear();
                fallbackHighQuality.Clear();

                const int priceThreshold = 2000;

                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Quality < 5)
                    {
                        continue;
                    }

                    int value = 0;
                    try
                    {
                        value = item.Value;
                    }
                    catch
                    {
                        value = 0;
                    }

                    if (value >= priceThreshold)
                    {
                        preferred.Add(item);
                    }
                    else
                    {
                        fallbackHighQuality.Add(item);
                    }
                }

                int target = highQualityCount;
                if (target < 1)
                {
                    target = 1;
                }
                if (target > beforeCount)
                {
                    target = beforeCount;
                }

                List<Item> keep = difficultyRewardKeepScratch;
                keep.Clear();

                // 1) 优先保留高品质高价物品
                for (int i = 0; i < preferred.Count && keep.Count < target; i++)
                {
                    keep.Add(preferred[i]);
                }

                // 2) 如果高价高品质不足以填满目标数量，则继续用高品质但低价的物品补齐
                for (int i = 0; i < fallbackHighQuality.Count && keep.Count < target; i++)
                {
                    keep.Add(fallbackHighQuality[i]);
                }

                // 3) 仍然完全不保留低品质物品（Quality<5），保证 Quality>=5

                int removed = 0;

                // 反向遍历，删除未入选的物品
                for (int i = content.Count - 1; i >= 0; i--)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (!keep.Contains(item))
                    {
                        removed++;
                        ItemTreeExtensions.DestroyTree(item);
                    }
                }

                // 调试：清理后再次输出通关奖励箱实际物品列表
                try
                {
                    List<Item> afterContent = inv.Content;
                    if (afterContent != null)
                    {
                        DevLog("[BossRush] 通关奖励清理后物品列表开始, 总数=" + afterContent.Count);
                        for (int i = 0; i < afterContent.Count; i++)
                        {
                            Item item = afterContent[i];
                            if (item == null) continue;

                            int q2 = -1;
                            int v2 = -1;
                            string name2 = "<unknown>";
                            string displayQ2 = "<unknown>";

                            try { q2 = item.Quality; } catch (Exception) { q2 = -1; }
                            try { v2 = item.Value; } catch (Exception) { v2 = -1; }
                            try { name2 = item.DisplayName; } catch (Exception) { name2 = "<unknown>"; }
                            try { displayQ2 = item.DisplayQuality.ToString(); } catch (Exception) { displayQ2 = "<unknown>"; }

                            DevLog("[BossRush] 通关奖励实际物品(清理后): typeID=" + item.TypeID + ", 名称=" + name2 + ", Quality=" + q2 + ", DisplayQuality=" + displayQ2 + ", Value=" + v2);
                        }
                        DevLog("[BossRush] 通关奖励清理后物品列表结束");
                    }
                }
                catch (Exception)
                {
                    // 调试输出失败不影响通关奖励清理逻辑。
                }

                if (removed > 0)
                {
                    DevLog("[BossRush] 调整通关奖励箱内容: 原总数=" + beforeCount + ", 目标高品质数量=" + target + ", 实际保留数量=" + keep.Count + ", 移除数量=" + removed);
                }
            }
            catch (Exception cleanEx)
            {
                DevLog("[BossRush] 清理通关奖励箱低品质物品失败: " + cleanEx.Message);
            }
            finally
            {
                // 清空 scratch 列表，释放对 Item 的引用，避免跨清理批次悬挂。
                ClearDifficultyRewardCleanupScratch();
            }
        }

        /// <summary>
        /// 清空通关奖励清理用的复用 scratch 列表，释放其中持有的 Item 引用。
        /// </summary>
        private void ClearDifficultyRewardCleanupScratch()
        {
            difficultyRewardPreferredScratch.Clear();
            difficultyRewardFallbackHighQualityScratch.Clear();
            difficultyRewardKeepScratch.Clear();
        }

        /// <summary>
        /// 判断Boss是否是龙裔遗族（支持多Boss模式）
        /// 通过GameObject名称或预设nameKey判断，不依赖单一实例引用
        /// </summary>
        private bool IsDragonDescendantBoss(CharacterMainControl boss)
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
        private bool IsDragonKingBoss(CharacterMainControl boss)
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


        /// <summary>
        /// Boss特殊掉落：统一处理所有Boss的专属掉落物（协程版本）
        /// 在掉落箱填充完成后添加专属掉落物
        /// </summary>
        private IEnumerator AddBossSpecialLootToLootboxCoroutine(
            InteractableLootbox lootbox,
            CharacterMainControl bossMain,
            bool useLegacyBossLootProbabilities,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            if (lootbox == null || bossMain == null)
            {
                FinalizeBossRushLootboxPathTracking(bossMain);
                yield break;
            }

            try
            {
                // 检查是否启用了随机掉落配置
                if (config == null || !config.enableRandomBossLoot)
                {
                    yield break;
                }

                // 等待掉落箱Inventory加载完成
                Inventory inv = lootbox.Inventory;
                if (inv == null)
                {
                    yield break;
                }

                int tries = 0;
                const int maxTries = 30;
                while (tries < maxTries && inv.Loading)
                {
                    tries++;
                    yield return new WaitForSeconds(0.1f);
                }

                if (useLegacyBossLootProbabilities && bossMaxHealth > LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH)
                {
                    TryAddLegacyBossGuaranteeItem(inv);
                }

                if (modeFPlunderLootPenaltyCount > 0)
                {
                    ApplyModeFPlunderLootPenalty(inv, modeFPlunderLootPenaltyCount);
                }

                // 根据Boss类型添加对应的专属掉落物
                if (IsDragonDescendantBoss(bossMain))
                {
                    // 龙裔遗族：按概率掉落龙套装（龙息10%、龙头30%、龙甲60%）
                    yield return AddDragonDescendantLoot(inv);
                }
                else if (IsDragonKingBoss(bossMain))
                {
                    // 龙王：按概率掉落专属物品（飞行图腾15%、龙王之冕15%、龙王鳞铠15%、焚皇断界戟15%、焚天龙铳1%、逆鳞39%）
                    yield return AddDragonKingLoot(inv);
                }

                if (modeFPlunderLootBonusCount > 0)
                {
                    AddModeFPlunderLootBonus(inv, modeFPlunderLootBonusCount);
                }

                FrostmourneBlueBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                PhantomWitchScytheBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                // 未来新增Boss在此添加 else if 分支
            }
            finally
            {
                FinalizeBossRushLootboxPathTracking(bossMain);
            }
        }

        internal bool ShouldDeferBlueBossExtraDropToBossRushLootbox(CharacterMainControl bossMain)
        {
            if (object.ReferenceEquals(bossMain, null))
            {
                return false;
            }

            return bossRushLootboxPathBosses.Contains(bossMain);
        }

        private void ApplyModeFPlunderLootPenalty(Inventory inv, int penaltyCount)
        {
            if (inv == null || penaltyCount <= 0)
            {
                return;
            }

            modeFPlunderPenaltyScratch.Clear();
            try
            {
                List<Item> content = inv.Content;
                if (content != null)
                {
                    for (int i = 0; i < content.Count; i++)
                    {
                        Item item = content[i];
                        if (item != null && item.Quality >= 6)
                        {
                            modeFPlunderPenaltyScratch.Add(item);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("ApplyModeFPlunderLootPenalty_scan", "扫描 Mode F 掠夺惩罚候选物失败", e);
            }

            modeFPlunderPenaltyScratch.Sort((a, b) =>
            {
                int qA = a != null ? a.Quality : 0;
                int qB = b != null ? b.Quality : 0;
                if (qA != qB)
                {
                    return qB.CompareTo(qA);
                }

                int vA = a != null ? a.Value : 0;
                int vB = b != null ? b.Value : 0;
                return vB.CompareTo(vA);
            });

            int removed = 0;
            for (int i = 0; i < modeFPlunderPenaltyScratch.Count && removed < penaltyCount; i++)
            {
                Item item = modeFPlunderPenaltyScratch[i];
                if (item == null)
                {
                    continue;
                }

                try { item.Detach(); } catch (Exception e) { LogLootWarningLimited("ApplyModeFPlunderLootPenalty_detach", "移除 Mode F 掠夺惩罚物品时 Detach 失败", e); }
                try { item.DestroyTree(); } catch (Exception e) { LogLootWarningLimited("ApplyModeFPlunderLootPenalty_destroy", "移除 Mode F 掠夺惩罚物品时 DestroyTree 失败", e); }
                removed++;
            }

            if (removed > 0)
            {
                DevLog("[ModeF] 已从被杀 Boss 奖励箱移除高品质战利品: " + removed);
            }

            modeFPlunderPenaltyScratch.Clear();
        }

        private void AddModeFPlunderLootBonus(Inventory inv, int bonusCount)
        {
            if (inv == null || bonusCount <= 0)
            {
                return;
            }

            int added = 0;
            for (int i = 0; i < bonusCount; i++)
            {
                Item reward = null;
                try
                {
                    int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                    if (rewardTypeId <= 0)
                    {
                        continue;
                    }

                    reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                    if (reward == null || reward.Quality < 6)
                    {
                        continue;
                    }

                    if (inv.AddItem(reward))
                    {
                        added++;
                        reward = null;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 添加掠夺奖励到掉落箱失败: " + e.Message);
                }
                finally
                {
                    try
                    {
                        if (reward != null)
                        {
                            reward.DestroyTree();
                        }
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("AddModeFPlunderLootBonus_cleanup", "清理未加入掉落箱的掠夺奖励失败", e);
                    }
                }
            }

            if (added > 0)
            {
                DevLog("[ModeF] 已向战胜 Boss 奖励箱补入高品质战利品: " + added);
            }
        }

        /// <summary>
        /// 添加龙裔遗族专属掉落物到Inventory
        /// </summary>
        private IEnumerator AddDragonDescendantLoot(Inventory inv)
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

            DevLog("[DragonDescendant] 随机选择龙套装掉落: " + itemName + " (TypeID=" + selectedTypeId + ", roll=" + roll.ToString("F3") + ")");

            try
            {
                if (!EnsureDragonBossRewardPrefabLoaded(selectedTypeId, "[DragonDescendant]"))
                {
                    yield break;
                }

                Item newItem = ItemAssetsCollection.InstantiateSync(selectedTypeId);
                if (newItem == null)
                {
                    DevLog("[DragonDescendant] 创建龙套装实例失败: TypeID=" + selectedTypeId);
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
                    DevLog("[DragonDescendant] 已配置龙息武器属性");
                }

                inv.AddItem(newItem);
                DevLog("[DragonDescendant] 已将 " + itemName + " 添加到掉落箱");

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
                DevLog("[DragonDescendant] 添加龙套装到掉落箱失败: " + addEx.Message);
            }

            yield break;
        }

        /// <summary>
        /// 添加龙王专属掉落物到Inventory
        /// 共享掉落格：飞行图腾15%、龙王之冕15%、龙王鳞铠15%、焚皇断界戟15%、焚天龙铳1%、逆鳞39%
        /// </summary>
        private IEnumerator AddDragonKingLoot(Inventory inv)
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

            DevLog("[DragonKing] 随机选择龙王掉落: " + itemName + " (TypeID=" + selectedTypeId + ", roll=" + roll.ToString("F3") + ")");

            try
            {
                if (!TryAddDragonKingLootItem(inv, selectedTypeId, itemName))
                {
                    yield break;
                }
            }
            catch (Exception addEx)
            {
                DevLog("[DragonKing] 添加掉落物到掉落箱失败: " + addEx.Message);
            }

            yield break;
        }

        private bool TryAddDragonKingLootItem(Inventory inv, int typeId, string itemName)
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
                    DevLog("[DragonKing] 创建掉落物实例失败: TypeID=" + typeId);
                    return false;
                }

                float maxDurability = newItem.MaxDurability;
                if (maxDurability > 0f)
                {
                    newItem.Durability = maxDurability;
                    newItem.DurabilityLoss = 0f;
                }

                inv.AddItem(newItem);
                DevLog("[DragonKing] 已将 " + itemName + " 添加到掉落箱");

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
                DevLog("[DragonKing] 添加掉落物失败: " + itemName + " - " + e.Message);
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
                DevLog(logPrefix + " [WARNING] 兜底按需注册奖励 prefab 失败: " + e.Message);
            }

            if (BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId))
            {
                return true;
            }

            DevLog(logPrefix + " [WARNING] 掉落前仍未找到奖励 prefab: TypeID=" + typeId);
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

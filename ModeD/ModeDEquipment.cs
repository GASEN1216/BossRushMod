// ============================================================================
// ModeDEquipment.cs - Mode D 装备系统
// ============================================================================
// 模块说明：
//   管理 Mode D 模式下的装备发放逻辑，包括：
//   - 玩家开局装备发放（武器、护甲、头盔、弹药、医疗品等）
//   - 敌人配装（替换默认装备，确保有合理掉落）
//   - 物品池管理（配件池、各类装备池）
//
// 开局装备规则：
//   - 武器：必给，优先低品质（1-3级），配件随机 30% 概率
//   - 弹药：必给，根据武器类型匹配
//   - 护甲/头盔：各 50% 概率
//   - 近战武器：40% 概率
//   - 医疗品：必给 3 格
//   - 图腾/面具：各 30% 概率
//   - 背包：40% 概率
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// Mode D 装备发放和敌人配装模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode D 装备系统字段

        /// <summary>保存发放的武器引用，用于后续给弹药</summary>
        private Item lastGivenWeapon = null;

        /// <summary>Mode D 全局物品池（静态缓存）</summary>
        private static List<int> modeDGlobalItemPool = null;

        /// <summary>Mode D 全局物品池是否已初始化</summary>
        private static bool modeDGlobalItemPoolInitialized = false;

        #endregion

        #region Mode D 敌人配装与掉落

        /// <summary>
        /// 按品质范围随机选择物品（优化版：有限随机抽样，无列表分配）
        /// </summary>
        private int GetRandomItemByQuality(List<int> pool, int minQuality, int maxQuality)
        {
            // P1-11: 先检查空池
            if (pool == null || pool.Count == 0)
            {
                return 0;
            }

            try
            {
                // P1-11 优化：使用有限随机抽样代替分配 filtered List
                // 最多尝试 30 次，如果都找不到符合品质的，就直接返回随机的一个
                const int MAX_QUALITY_ATTEMPTS = 30;

                for (int attempt = 0; attempt < MAX_QUALITY_ATTEMPTS; attempt++)
                {
                    int id = pool[UnityEngine.Random.Range(0, pool.Count)];
                    try
                    {
                        var meta = ItemAssetsCollection.GetMetaData(id);
                        if (meta.quality >= minQuality && meta.quality <= maxQuality)
                        {
                            return id;
                        }
                    }
                    catch {}
                }

                // 没有找到符合品质要求的，随机返回一个
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            catch
            {
                return pool.Count > 0 ? pool[0] : 0;
            }
        }

        private bool ShouldUseLegacyModeDStyleEnemyLootQualityDistribution()
        {
            return config != null && config.useLegacyBossLootProbabilities;
        }

        private const float BossRushStyleCrownWeightScale = 0.1f;

        private float ComputeModeDStyleEnemyLootBonusFactor(int qualityLevel)
        {
            return Mathf.InverseLerp(1f, 6f, Mathf.Clamp(qualityLevel, 1, 6));
        }

        private float ComputeModeDStyleEnemyLootBonusFactorFromHealth(float enemyHealth)
        {
            float clampedHealth = Mathf.Max(0f, enemyHealth);
            float refMin = minBossBaseHealth;
            float refMax = maxBossBaseHealth;

            if (refMax > refMin && refMin > 0f)
            {
                return Mathf.Clamp01(Mathf.InverseLerp(refMin, refMax, clampedHealth));
            }

            return Mathf.Clamp01((clampedHealth - 100f) / 1000f);
        }

        private int RollLegacyDesiredQualityForModeDStyleEnemyLoot(int qualityLevel, int minQuality, int maxQuality)
        {
            int clampedMin = Mathf.Clamp(minQuality, 1, 8);
            int clampedMax = Mathf.Clamp(maxQuality, clampedMin, 8);
            if (clampedMin >= clampedMax)
            {
                return clampedMin;
            }

            LegacyBossLootQualityDistribution distribution =
                LegacyBossLootProbabilityModel.BuildDistribution(ComputeModeDStyleEnemyLootBonusFactor(qualityLevel));

            double totalProbability = 0.0;
            for (int q = clampedMin; q <= clampedMax; q++)
            {
                totalProbability += distribution.GetProbabilityForQuality(q);
            }

            if (totalProbability <= 0.0)
            {
                return UnityEngine.Random.Range(clampedMin, clampedMax + 1);
            }

            double roll = UnityEngine.Random.value * totalProbability;
            for (int q = clampedMin; q <= clampedMax; q++)
            {
                roll -= distribution.GetProbabilityForQuality(q);
                if (roll <= 0.0)
                {
                    return q;
                }
            }

            return clampedMax;
        }

        private int TryGetRandomItemByExactQualityBucket(Dictionary<int, List<int>> poolByQuality, int exactQuality)
        {
            if (poolByQuality == null)
            {
                return 0;
            }

            int clampedQuality = Mathf.Clamp(exactQuality, 1, 8);
            List<int> bucket;
            if (!poolByQuality.TryGetValue(clampedQuality, out bucket) || bucket == null || bucket.Count == 0)
            {
                return 0;
            }

            return bucket[UnityEngine.Random.Range(0, bucket.Count)];
        }

        private int PickBossRushStyleBucketItemId(List<int> bucket)
        {
            if (bucket == null || bucket.Count == 0)
            {
                return 0;
            }

            float totalWeight = 0f;
            for (int i = 0; i < bucket.Count; i++)
            {
                int id = bucket[i];
                totalWeight += (id == 1254) ? BossRushStyleCrownWeightScale : 1f;
            }

            if (totalWeight <= 0f)
            {
                return bucket[UnityEngine.Random.Range(0, bucket.Count)];
            }

            float roll = UnityEngine.Random.value * totalWeight;
            for (int i = 0; i < bucket.Count; i++)
            {
                int id = bucket[i];
                roll -= (id == 1254) ? BossRushStyleCrownWeightScale : 1f;
                if (roll <= 0f)
                {
                    return id;
                }
            }

            return bucket[bucket.Count - 1];
        }

        private bool TryCreateBossRushStyleInventoryLootItemForSharedModes(float enemyHealth, out Item item)
        {
            item = null;
            float bonusFactor = ComputeModeDStyleEnemyLootBonusFactorFromHealth(enemyHealth);

            Dictionary<int, List<int>> qualityBuckets = new Dictionary<int, List<int>>();
            if (ShouldUseLegacyModeDStyleEnemyLootQualityDistribution())
            {
                List<int> candidateIds = new List<int>();
                if (!TryGetLegacyBossLootCandidates(candidateIds, qualityBuckets))
                {
                    return false;
                }

                LegacyBossLootQualityDistribution distribution =
                    LegacyBossLootProbabilityModel.BuildDistribution(bonusFactor);
                int selectedQuality = PickBossRushStyleQualityByLegacyDistribution(distribution, qualityBuckets, 1, 8);
                if (selectedQuality <= 0)
                {
                    return false;
                }

                List<int> bucket;
                if (!qualityBuckets.TryGetValue(selectedQuality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    return false;
                }

                int typeId = PickBossRushStyleBucketItemId(bucket);
                if (typeId <= 0)
                {
                    return false;
                }

                item = ItemAssetsCollection.InstantiateSync(typeId);
                return item != null;
            }

            HashSet<int> dynamicIds = BuildGeneralBossLootCandidateIdSet();
            if (dynamicIds.Count <= 0)
            {
                return false;
            }

            BuildLegacyBossLootQualityBucketsFromIds(dynamicIds, qualityBuckets);
            int quality = PickBossRushStyleQualityByNonLegacyWeights(bonusFactor, qualityBuckets, 1, 8);
            if (quality <= 0)
            {
                return false;
            }

            List<int> selectedBucket;
            if (!qualityBuckets.TryGetValue(quality, out selectedBucket) || selectedBucket == null || selectedBucket.Count == 0)
            {
                return false;
            }

            int selectedTypeId = PickBossRushStyleBucketItemId(selectedBucket);
            if (selectedTypeId <= 0)
            {
                return false;
            }

            item = ItemAssetsCollection.InstantiateSync(selectedTypeId);
            return item != null;
        }

        private int PickBossRushStyleQualityByLegacyDistribution(
            LegacyBossLootQualityDistribution distribution,
            Dictionary<int, List<int>> qualityBuckets,
            int minQuality,
            int maxQuality)
        {
            double totalWeight = 0.0;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                totalWeight += distribution.GetProbabilityForQuality(quality);
            }

            if (totalWeight <= 0.0)
            {
                return 0;
            }

            double roll = UnityEngine.Random.value * totalWeight;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                roll -= distribution.GetProbabilityForQuality(quality);
                if (roll <= 0.0)
                {
                    return quality;
                }
            }

            return 0;
        }

        private int PickBossRushStyleQualityByNonLegacyWeights(
            float bonusFactor,
            Dictionary<int, List<int>> qualityBuckets,
            int minQuality,
            int maxQuality)
        {
            float highChance = Mathf.Clamp01(bonusFactor);
            float lowTotalWeight = 1f - highChance;
            float[] qualityWeights = new float[9];

            for (int quality = 1; quality <= 4; quality++)
            {
                qualityWeights[quality] = lowTotalWeight * 0.25f;
            }

            qualityWeights[5] = highChance * 0.4f;
            qualityWeights[6] = highChance * 0.3f;
            qualityWeights[7] = highChance * 0.2f;
            qualityWeights[8] = highChance * 0.1f;

            float totalWeight = 0f;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                totalWeight += qualityWeights[quality];
            }

            if (totalWeight <= 0f)
            {
                return 0;
            }

            float roll = UnityEngine.Random.value * totalWeight;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                roll -= qualityWeights[quality];
                if (roll <= 0f)
                {
                    return quality;
                }
            }

            return 0;
        }

        /// <summary>
        /// Mode D 配件池（Accessory Tag）
        /// </summary>
        private readonly List<int> modeDAccessoryPool = new List<int>();
        private readonly Dictionary<int, List<int>> modeDAccessoryPoolByQuality = CreateModeDQualityBuckets();

        /// <summary>
        /// 初始化配件池（包含游戏所有配件）
        /// </summary>
        private void InitializeAccessoryPool()
        {
            try
            {
                modeDAccessoryPool.Clear();
                ClearModeDQualityBuckets(modeDAccessoryPoolByQuality);

                // 通过名字查找配件 Tag
                Duckov.Utilities.Tag accessoryTag = FindTagByName("Accessory");
                if (accessoryTag == null)
                {
                    DevLog("[ModeD] 未找到 Accessory Tag，跳过配件池初始化");
                    return;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { accessoryTag };
                filter.minQuality = 1;
                filter.maxQuality = 8; // 包含所有品质
                int[] accessoryIds = ItemAssetsCollection.Search(filter);

                AddDistinctItemIds(modeDAccessoryPool, accessoryIds);

                RebuildModeDQualityBuckets(modeDAccessoryPool, modeDAccessoryPoolByQuality);

                DevLog("[ModeD] 配件池初始化完成，数量: " + modeDAccessoryPool.Count);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] InitializeAccessoryPool 失败: " + e.Message);
            }
        }

        /// <summary>
        /// P1-7 优化：尝试用随机配件填充槽位（有限随机抽样，而非全池洗牌）
        /// </summary>
        private void TryFillSlotWithRandomAccessory(Item weapon, Slot slot)
        {
            try
            {
                if (modeDAccessoryPool.Count == 0) return;

                // P1-7 优化：改为有限次数随机抽样，而不是复制+洗牌整个池
                // 每个槽最多尝试 8 次，避免大量 Instantiate/Destroy 和 GC
                const int MAX_ACCESSORY_ATTEMPTS = 8;

                for (int attempt = 0; attempt < MAX_ACCESSORY_ATTEMPTS; attempt++)
                {
                    int accessoryId = modeDAccessoryPool[UnityEngine.Random.Range(0, modeDAccessoryPool.Count)];
                    try
                    {
                        Item accessory = ItemAssetsCollection.InstantiateSync(accessoryId);
                        if (accessory == null) continue;

                        // 使用 Slot.CanPlug 检查是否可以安装
                        if (slot.CanPlug(accessory))
                        {
                            Item replaced;
                            if (slot.Plug(accessory, out replaced))
                            {
                                DevLog("[ModeD] 安装配件: " + accessory.DisplayName + " 到 " + slot.DisplayName);

                                // 销毁被替换的配件（如果有）
                                if (replaced != null)
                                {
                                    UnityEngine.Object.Destroy(replaced.gameObject);
                                }
                                return; // 成功安装，退出
                            }
                        }

                        // 无法安装，销毁临时配件
                        UnityEngine.Object.Destroy(accessory.gameObject);
                    }
                    catch {}
                }

                // 尝试 8 次后仍未找到合适的配件，放弃此槽
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] TryFillSlotWithRandomAccessory 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为敌人配装（替换默认装备）
        /// 掉落物包含一把枪和匹配的子弹，其他格子随机填充
        /// </summary>
        /// <param name="enemy">敌人角色</param>
        /// <param name="waveIndex">当前波次</param>
        /// <param name="enemyHealth">敌人血量（用于决定装备品质）</param>
        /// <param name="isBoss">是否为Boss（Boss保留原有头盔和护甲）</param>
        public void EquipEnemyForModeD(CharacterMainControl enemy, int waveIndex, float enemyHealth, bool isBoss = false)
        {
            try
            {
                if (enemy == null) return;

                DevLog("[ModeD] 为敌人配装: wave=" + waveIndex + ", health=" + enemyHealth);

                // 根据血量和波次计算品质等级（1-6）
                int qualityLevel = CalculateQualityLevel(waveIndex, enemyHealth);

                bool hasPrimaryOrSecondaryWeapon = false;
                bool hasMeleeWeapon = false;

                try
                {
                    Slot prim = enemy.PrimWeaponSlot();
                    if (prim != null && prim.Content != null)
                    {
                        hasPrimaryOrSecondaryWeapon = true;
                    }
                }
                catch {}

                try
                {
                    Slot sec = enemy.SecWeaponSlot();
                    if (sec != null && sec.Content != null)
                    {
                        hasPrimaryOrSecondaryWeapon = true;
                    }
                }
                catch {}

                try
                {
                    Slot meleeSlot = enemy.MeleeWeaponSlot();
                    if (meleeSlot != null && meleeSlot.Content != null)
                    {
                        hasMeleeWeapon = true;
                    }
                }
                catch {}

                bool hasPetAI = false;
                try
                {
                    hasPetAI = enemy.GetComponentInChildren<PetAI>() != null;
                }
                catch {}

                bool keepOriginalMeleeSetup = (!hasPrimaryOrSecondaryWeapon && hasMeleeWeapon) || hasPetAI;

                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                // P1-4 修复：解耦 Inventory 检查，允许无背包敌人也能配装
                // 即使 inventory 为 null，仍然可以给敌人装备武器（TryPlug），只是不能往背包塞东西
                Inventory inventory = characterItem.Inventory;
                bool hasInventory = (inventory != null);

                if (keepOriginalMeleeSetup)
                {
                    DevLog("[ModeD] 检测到近战/宠物型敌人，保留原始武器配置，仅追加掉落");

                    // 只有有背包的敌人才追加掉落
                    if (hasInventory)
                    {
                        int meleeTargetItemCount = 5 + Mathf.FloorToInt(enemyHealth / 100f);
                        if (meleeTargetItemCount < 5)
                        {
                            meleeTargetItemCount = 5;
                        }

                        int meleeCurrentCount = 0;
                        try
                        {
                            if (inventory.Content != null)
                            {
                                meleeCurrentCount = inventory.Content.Count;
                            }
                        }
                        catch {}

                        int meleeRemainingToFill = Mathf.Max(0, meleeTargetItemCount - meleeCurrentCount);
                        if (meleeRemainingToFill > 0)
                        {
                            FillEnemyInventoryForModeD(enemy, qualityLevel, enemyHealth, meleeRemainingToFill);
                        }
                    }
                    else
                    {
                        DevLog("[ModeD] 敌人无背包，跳过追加掉落");
                    }

                    return;
                }

                // 清空敌人现有装备和背包（ClearEnemyInventory 内部已处理 inventory == null）
                // Boss保留原有头盔和护甲
                ClearEnemyInventory(enemy, isBoss);

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                // 1. 随机填充少量额外掉落物（不包含武器和弹药）- 需要背包
                if (hasInventory)
                {
                    int extraItems = 3;

                    for (int i = 0; i < extraItems; i++)
                    {
                        Item randomExtraItem = null;
                        if (TryCreateBossRushStyleInventoryLootItemForSharedModes(enemyHealth, out randomExtraItem) && randomExtraItem != null)
                        {
                            inventory.AddAndMerge(randomExtraItem, 0);
                        }
                    }
                }

                // 2. 确保敌人有武器可用（装备到手上，不需要背包）
                GiveEnemyEquippedWeapon(enemy, qualityLevel);
                GiveRandomMeleeWeaponToEnemy(enemy);

                // 3. 尝试将敌人背包进一步填满（仅限 Mode D，需要背包）
                if (hasInventory)
                {
                    int targetItemCount = 5 + Mathf.FloorToInt(enemyHealth / 100f);
                    if (targetItemCount < 5)
                    {
                        targetItemCount = 5;
                    }

                    int currentCount = 0;
                    try
                    {
                        if (inventory.Content != null)
                        {
                            currentCount = inventory.Content.Count;
                        }
                    }
                    catch {}

                    int remainingToFill = Mathf.Max(0, targetItemCount - currentCount);
                    if (remainingToFill > 0)
                    {
                        FillEnemyInventoryForModeD(enemy, qualityLevel, enemyHealth, remainingToFill);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] EquipEnemyForModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 尝试将敌人背包进一步填满一些（仅 Mode D 使用）
        /// </summary>
        private void FillEnemyInventoryForModeD(CharacterMainControl enemy, int qualityLevel, float enemyHealth, int maxItemsToAdd)
        {
            try
            {
                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                Inventory inventory = characterItem.Inventory;
                if (inventory == null) return;

                if (maxItemsToAdd <= 0)
                {
                    return;
                }

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                // 依据 Inventory.GetFirstEmptyPosition(0) 一直塞，直到没有空位或达到安全上限
                int safety = 60;
                int loops = 0;

                while (loops < safety && loops < maxItemsToAdd)
                {
                    int firstEmpty = -1;
                    try
                    {
                        firstEmpty = inventory.GetFirstEmptyPosition(0);
                    }
                    catch {}

                    if (firstEmpty < 0)
                    {
                        break; // 背包已满
                    }

                    Item randomItem = null;
                    if (!TryCreateBossRushStyleInventoryLootItemForSharedModes(enemyHealth, out randomItem))
                    {
                        randomItem = CreateRandomGlobalItemForModeD(minQ, maxQ, enemyHealth);
                    }
                    if (randomItem != null)
                    {
                        inventory.AddAndMerge(randomItem, 0);
                    }

                    loops++;
                }

                int finalCount = 0;
                try
                {
                    if (inventory.Content != null)
                    {
                        finalCount = inventory.Content.Count;
                    }
                }
                catch {}

                DevLog("[ModeD] 背包填充完成，物品数量: " + finalCount);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] FillEnemyInventoryForModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 创建随机弹药（品质 1-5 和 6-8 之间约 70% / 30% 概率分布）
        /// </summary>
        private Item CreateRandomAmmo(int minQ, int maxQ)
        {
            try
            {
                Duckov.Utilities.Tag bulletTag = GameplayDataSettings.Tags.Bullet;
                if (bulletTag == null) return null;

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { bulletTag };

                int[] lowIds = null;
                int[] highIds = null;

                int lowMin = minQ;
                int lowMax = Mathf.Min(5, maxQ);
                if (lowMin <= lowMax)
                {
                    filter.minQuality = lowMin;
                    filter.maxQuality = lowMax;
                    lowIds = ItemAssetsCollection.Search(filter);
                }

                int highMin = Mathf.Max(6, minQ);
                int highMax = Mathf.Min(8, maxQ);
                if (highMin <= highMax)
                {
                    filter.minQuality = highMin;
                    filter.maxQuality = highMax;
                    highIds = ItemAssetsCollection.Search(filter);
                }

                if ((lowIds == null || lowIds.Length == 0) && (highIds == null || highIds.Length == 0))
                {
                    filter.minQuality = minQ;
                    filter.maxQuality = maxQ;
                    int[] ids = ItemAssetsCollection.Search(filter);
                    if (ids != null && ids.Length > 0)
                    {
                        int id = ids[UnityEngine.Random.Range(0, ids.Length)];
                        return ItemAssetsCollection.InstantiateSync(id);
                    }
                    return null;
                }

                float lowWeight = (lowIds != null && lowIds.Length > 0) ? 0.85f : 0f;
                float highWeight = (highIds != null && highIds.Length > 0) ? 0.15f : 0f;

                float totalWeight = lowWeight + highWeight;
                if (totalWeight <= 0f)
                {
                    return null;
                }

                float roll = UnityEngine.Random.value * totalWeight;
                int[] chosen = null;

                if (roll < lowWeight && lowIds != null && lowIds.Length > 0)
                {
                    chosen = lowIds;
                }
                else if (highIds != null && highIds.Length > 0)
                {
                    chosen = highIds;
                }
                else if (lowIds != null && lowIds.Length > 0)
                {
                    chosen = lowIds;
                }

                if (chosen != null && chosen.Length > 0)
                {
                    int id = chosen[UnityEngine.Random.Range(0, chosen.Length)];
                    return ItemAssetsCollection.InstantiateSync(id);
                }
            }
            catch {}
            return null;
        }

        private Item CreateRandomAmmoForEnemyLoot(int qualityLevel, int minQ, int maxQ)
        {
            try
            {
                if (!ShouldUseLegacyModeDStyleEnemyLootQualityDistribution())
                {
                    return CreateRandomAmmo(minQ, maxQ);
                }

                Duckov.Utilities.Tag bulletTag = GameplayDataSettings.Tags.Bullet;
                if (bulletTag == null) return null;

                int clampedMin = Mathf.Clamp(minQ, 1, 8);
                int clampedMax = Mathf.Clamp(maxQ, clampedMin, 8);
                int desiredQuality = RollLegacyDesiredQualityForModeDStyleEnemyLoot(qualityLevel, clampedMin, clampedMax);

                for (int offset = 0; offset <= 7; offset++)
                {
                    int lower = desiredQuality - offset;
                    if (lower >= clampedMin)
                    {
                        int lowerId = TryGetRandomItemByExactQualityBucket(modeDAmmoPoolByQuality, lower);
                        if (lowerId > 0)
                        {
                            return ItemAssetsCollection.InstantiateSync(lowerId);
                        }
                    }

                    int upper = desiredQuality + offset;
                    if (offset > 0 && upper <= clampedMax)
                    {
                        int upperId = TryGetRandomItemByExactQualityBucket(modeDAmmoPoolByQuality, upper);
                        if (upperId > 0)
                        {
                            return ItemAssetsCollection.InstantiateSync(upperId);
                        }
                    }
                }
            }
            catch {}

            return CreateRandomAmmo(minQ, maxQ);
        }

        /// <summary>
        /// P1-10 优化：给敌人装备武器（装在手上用于战斗），检查返回值防止泄漏
        /// </summary>
        private void GiveEnemyEquippedWeapon(CharacterMainControl enemy, int qualityLevel)
        {
            try
            {
                if (modeDWeaponPool.Count == 0) return;

                int minQ = Mathf.Max(1, qualityLevel - 1);
                int maxQ = Mathf.Min(8, qualityLevel + 2);

                int weaponId = GetRandomItemByQuality(modeDWeaponPool, minQ, maxQ);
                Item weapon = ItemAssetsCollection.InstantiateSync(weaponId);
                if (weapon == null) return;

                // 随机加配件
                TryAddRandomAttachmentsFullRandom(weapon);

                // P1-10 修复：检查 TryPlug 返回值，失败时处理武器防止泄漏
                bool equipped = enemy.CharacterItem.TryPlug(weapon, true, null, 0);
                if (!equipped)
                {
                    // 装备失败，尝试放入背包
                    Inventory inventory = enemy.CharacterItem.Inventory;
                    if (inventory != null)
                    {
                        inventory.AddAndMerge(weapon, 0);
                    }
                    else
                    {
                        // 背包也不存在，销毁武器防止泄漏
                        UnityEngine.Object.Destroy(weapon.gameObject);
                        DevLog("[ModeD] [WARNING] 敌人武器装备失败且无背包，已销毁武器");
                        return;
                    }
                }

                // 给弹药和填满弹夹
                ItemSetting_Gun gunSetting = weapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting != null)
                {
                    if (gunSetting.TargetBulletID < 0)
                    {
                        // 为武器自动选择一个可用弹药类型
                        EnsureStarterGunHasBulletType(weapon);
                    }

                    if (gunSetting.TargetBulletID >= 0)
                    {
                        // 填满弹夹
                        FillGunMagazine(weapon);

                        // 注意：不再调用 FillGunInternalAmmo，避免敌人掉落的武器内有大量子弹
                        // 原代码：FillGunInternalAmmo(weapon, 2, 30, 60);
                        // 这会导致玩家捡到的枪里有60-120发额外子弹（如狙击枪80发）

                        // P1-4 修复：给背包弹药（供敌人战斗使用，会随尸体掉落），检查 Inventory 是否为 null
                        Inventory enemyInventory = enemy.CharacterItem.Inventory;
                        if (enemyInventory != null)
                        {
                            Item ammo = ItemAssetsCollection.InstantiateSync(gunSetting.TargetBulletID);
                            if (ammo != null)
                            {
                                ammo.StackCount = UnityEngine.Random.Range(30, 60);
                                enemyInventory.AddAndMerge(ammo, 0);
                            }

                            Item randomAmmo = ShouldUseLegacyModeDStyleEnemyLootQualityDistribution()
                                ? CreateRandomAmmoForEnemyLoot(
                                    qualityLevel,
                                    Mathf.Max(1, qualityLevel - 1),
                                    Mathf.Min(8, qualityLevel + 2))
                                : CreateRandomAmmo(
                                    Mathf.Max(1, qualityLevel - 1),
                                    Mathf.Min(8, qualityLevel + 2));
                            if (randomAmmo != null)
                            {
                                randomAmmo.StackCount = UnityEngine.Random.Range(60, 121);
                                enemyInventory.AddAndMerge(randomAmmo, 0);
                            }
                        }
                    }
                }

                DevLog("[ModeD] 敌人装备武器: " + weapon.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] GiveEnemyEquippedWeapon 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 计算装备品质等级
        /// </summary>
        private int CalculateQualityLevel(int waveIndex, float enemyHealth)
        {
            // 基础品质：波次越高越好
            int baseQuality = 1 + (waveIndex / 5); // 每5波提升1级

            // 血量加成：血量越高越好
            int healthBonus = (int)(enemyHealth / 500f); // 每500血+1级

            int quality = baseQuality + healthBonus;

            // 限制在1-6范围
            return Mathf.Clamp(quality, 1, 6);
        }

        /// <summary>
        /// P1-9 优化：清空敌人背包和装备（移除 ToArray() 避免 GC）
        /// </summary>
        /// <param name="enemy">敌人角色</param>
        /// <param name="preserveHelmetAndArmor">是否保留头盔和护甲（Boss专用）</param>
        private void ClearEnemyInventory(CharacterMainControl enemy, bool preserveHelmetAndArmor = false)
        {
            try
            {
                Item characterItem = enemy.CharacterItem;
                if (characterItem == null) return;

                // 清空背包 - P1-9 修复：使用倒序 for 循环代替 ToArray()
                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    var content = inventory.Content;
                    // 倒序遍历，避免在移除元素时出现问题
                    for (int i = content.Count - 1; i >= 0; --i)
                    {
                        var item = content[i];
                        if (item != null)
                        {
                            item.Detach();
                            UnityEngine.Object.Destroy(item.gameObject);
                        }
                    }
                }

                // 清空装备槽（如果 preserveHelmetAndArmor 为 true，则跳过头盔和护甲槽）
                foreach (Slot slot in characterItem.Slots)
                {
                    if (slot != null && slot.Content != null)
                    {
                        // 如果需要保留头盔和护甲，检查槽位类型
                        if (preserveHelmetAndArmor)
                        {
                            // 通过槽位 Key 判断是否为头盔或护甲槽
                            // 游戏中头盔槽 Key 为 "Helmat"，护甲槽 Key 为 "Armor"
                            string slotKey = slot.Key ?? "";
                            bool isHelmetOrArmorSlot = slotKey == "Helmat" || slotKey == "Armor";
                            if (isHelmetOrArmorSlot)
                            {
                                DevLog("[ModeD] 保留Boss头盔/护甲槽: " + slotKey);
                                continue; // 跳过此槽位
                            }
                        }

                        Item content = slot.Content;
                        slot.Unplug();
                        UnityEngine.Object.Destroy(content.gameObject);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] ClearEnemyInventory 失败: " + e.Message);
            }
        }

        #endregion
    }
}

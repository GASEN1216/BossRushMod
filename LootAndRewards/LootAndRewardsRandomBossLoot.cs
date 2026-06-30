// ============================================================================
// LootAndRewardsRandomBossLoot.cs - Boss 随机掉落流程
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
        private static class BossLootBoxLoaderReflection
        {
            private const BindingFlags InstancePrivate = BindingFlags.NonPublic | BindingFlags.Instance;
            private const BindingFlags InstancePublic = BindingFlags.Public | BindingFlags.Instance;
            private const BindingFlags NestedAny = BindingFlags.NonPublic | BindingFlags.Public;

            internal static readonly Type LoaderType;
            internal static readonly FieldInfo RandomCountField;
            internal static readonly FieldInfo QualitiesField;
            internal static readonly FieldInfo TagsField;
            internal static readonly FieldInfo ExcludeTagsField;
            internal static readonly FieldInfo RandomPoolField;
            internal static readonly FieldInfo FixedItemsField;
            internal static readonly FieldInfo FixedChanceField;
            internal static readonly Type LoaderEntryType;
            internal static readonly FieldInfo LootEntryItemIdField;
            internal static readonly FieldInfo RandomPoolEntriesField;
            internal static readonly Type RandomPoolEntryType;
            internal static readonly FieldInfo RandomPoolEntryValueField;
            internal static readonly FieldInfo RandomPoolEntryWeightField;

            static BossLootBoxLoaderReflection()
            {
                try
                {
                    LoaderType = typeof(Duckov.Utilities.LootBoxLoader);
                    RandomCountField = LoaderType.GetField("randomCount", InstancePrivate);
                    QualitiesField = LoaderType.GetField("qualities", InstancePrivate);
                    TagsField = LoaderType.GetField("tags", InstancePrivate);
                    ExcludeTagsField = LoaderType.GetField("excludeTags", InstancePrivate);
                    RandomPoolField = LoaderType.GetField("randomPool", InstancePrivate);
                    FixedItemsField = LoaderType.GetField("fixedItems", InstancePrivate);
                    FixedChanceField = LoaderType.GetField("fixedItemSpawnChance", InstancePrivate);
                    LoaderEntryType = LoaderType.GetNestedType("Entry", NestedAny);
                    LootEntryItemIdField = LoaderEntryType != null
                        ? LoaderEntryType.GetField("itemTypeID", InstancePublic)
                        : null;

                    Type randomPoolType = RandomPoolField != null ? RandomPoolField.FieldType : null;
                    if (randomPoolType != null)
                    {
                        RandomPoolEntriesField = randomPoolType.GetField("entries", InstancePublic);
                        Type randomPoolEntryType = randomPoolType.GetNestedType("Entry", NestedAny);
                        if (randomPoolEntryType != null && randomPoolEntryType.ContainsGenericParameters && randomPoolType.IsGenericType)
                        {
                            Type[] genericArgs = randomPoolType.GetGenericArguments();
                            if (genericArgs != null && genericArgs.Length > 0)
                            {
                                randomPoolEntryType = randomPoolEntryType.MakeGenericType(genericArgs);
                            }
                        }

                        RandomPoolEntryType = randomPoolEntryType;
                        if (RandomPoolEntryType != null)
                        {
                            RandomPoolEntryValueField = RandomPoolEntryType.GetField("value", InstancePublic);
                            RandomPoolEntryWeightField = RandomPoolEntryType.GetField("weight", InstancePublic);
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] BossLootBoxLoaderReflection 初始化失败: " + e.Message);
                }
            }
        }

        private readonly List<int> bossRandomLootCandidateIdScratch = new List<int>(1024);
        private readonly Dictionary<int, int> bossRandomLootQualityScratch = new Dictionary<int, int>(1024);
        private readonly int[] bossRandomLootHighQualityCountsScratch = new int[4];
        private readonly int[] bossRandomLootLowQualityCountsScratch = new int[4];
        private readonly float[] bossRandomLootHighWeightsScratch = new float[4];

        private static float GetBossLootHighQualityRatio(int index)
        {
            switch (index)
            {
                case 0:
                    return 4f;
                case 1:
                    return 3f;
                case 2:
                    return 2f;
                case 3:
                    return 1f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 玩家死亡保护（BossRush期间）- 参考keep_items_on_death实现（LootAndRewards 分部实现）
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush_LootAndRewards(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // 检查是否是BossRush期间的玩家死亡
                if (!IsActive) return;

                var character = deadHealth.GetComponent<CharacterMainControl>();
                if (character == null) return;

                // 检查是否是玩家
                bool isPlayer = false;
                try
                {
                    isPlayer = CharacterMainControlExtensions.IsMainCharacter(character);
                }
                catch
                {
                    isPlayer = (character == CharacterMainControl.Main);
                }

                if (!isPlayer) return;

                DevLog("[BossRush] 检测到玩家死亡，不再掉落物品，直接结束BossRush");

                // 结束BossRush
                SetBossRushRuntimeActive(false);
                bossRushArenaActive = false;
                currentBoss = null;
                try
                {
                    if (ammoShop != null)
                    {
                        try
                        {
                            if (ammoShop.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(ammoShop.gameObject);
                            }
                        }
                        catch (Exception e)
                        {
                            LogLootWarningLimited("OnPlayerDeathInBossRush_ammoShopDestroy", "玩家死亡时销毁加油站商店对象失败", e);
                        }
                        ammoShop = null;
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("OnPlayerDeathInBossRush_ammoShopCleanup", "玩家死亡时清理加油站商店失败", e);
                }

                // 取消敌人死亡监听
                Health.OnDead -= OnEnemyDiedWithDamageInfo;

                // 如果是 Mode D 模式，结束 Mode D
                if (modeDActive)
                {
                    EndModeD();
                }

                // 如果是 Mode E 模式，结束 Mode E
                if (modeEActive)
                {
                    EndModeE();
                }

                ShowMessage(L10n.T("BossRush挑战失败！", "BossRush challenge failed!"));
            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnPlayerDeathInBossRush错误: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 在Boss真正生成掉落物之前拦截并随机化掉落（LootAndRewards 分部实现）
        /// （事件来源：CharacterMainControl.BeforeCharacterSpawnLootOnDead）
        /// </summary>
        private void OnBossBeforeSpawnLoot_LootAndRewards(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            try
            {
                // [调试] 记录事件触发
                string bossName = bossMain != null ? bossMain.gameObject.name : "null";
                DevLog("[BossRush] OnBossBeforeSpawnLoot_LootAndRewards 被调用: bossName=" + bossName + ", IsActive=" + IsActive + ", modeFActive=" + modeFActive);

                // 空检查
                if (bossMain == null)
                {
                    DevLog("[BossRush] 掉落事件跳过: bossMain=null");
                    return;
                }

                // 龙王Boss特殊处理：即使IsActive=false，只要在bossSpawnTimes中有记录就继续处理
                // 原因：龙王第三阶段召唤龙裔遗族，龙裔死亡会先触发OnAllEnemiesDefeated设置IsActive=false
                // 然后龙王联动死亡才触发此事件，此时需要允许龙王的掉落逻辑执行
                bool isDragonKing = IsDragonKingBoss(bossMain);
                bool allowModeEFIndependentLoot = modeFActive || modeEActive;
                if (!IsActive && !isDragonKing && !allowModeEFIndependentLoot)
                {
                    FinalizeBossRushLootboxPathTracking(bossMain);
                    DevLog("[BossRush] 掉落事件跳过: IsActive=False，且既不是龙王Boss也不是 Mode E/F");
                    return;
                }
                if (!IsActive && isDragonKing)
                {
                    DevLog("[BossRush] 龙王Boss联动死亡特殊处理: 允许继续执行掉落逻辑");
                }
                else if (!IsActive && allowModeEFIndependentLoot)
                {
                    DevLog("[" + (modeFActive ? "ModeF" : "ModeE") + "] 独立模式掉落处理: 允许继续执行Boss掉落逻辑");
                }

                if (allowModeEFIndependentLoot)
                {
                    try
                    {
                        if (bossMain != null && bossMain.transform != null)
                        {
                            StartCoroutine(BossRushLootboxUtility.DecorateLootboxesNearPosition(this, bossMain.transform.position, true));
                        }
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("OnBossBeforeSpawnLoot_decorateModeEFOriginal", "登记 Mode E/F 原生掉落箱扫箱追踪失败", e);
                    }

                    DevLog("[" + (modeFActive ? "ModeF" : "ModeE") + "] 保留原生掉落箱，不再拦截为独立奖励箱");
                    return;
                }

                // 只处理由 BossRush 生成且被追踪的 Boss
                if (!bossSpawnTimes.ContainsKey(bossMain))
                {
                    FinalizeBossRushLootboxPathTracking(bossMain);
                    DevLog("[BossRush] 掉落事件跳过: bossSpawnTimes 不包含此Boss, 当前追踪数量=" + bossSpawnTimes.Count);
                    return;
                }

                DevLog("[BossRush] 掉落事件通过检查，开始处理Boss掉落: " + bossName);

                // 检查是否是孩儿护我召唤的龙裔（不在currentWaveBosses中但在bossSpawnTimes中）
                // 这类龙裔只需要掉落，不参与波次计数
                bool isChildProtectionDescendant = false;
                if (bossesPerWave > 1 && currentWaveBosses != null)
                {
                    // 多Boss模式：检查是否在当前波Boss列表中
                    bool foundInWave = false;
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        if (currentWaveBosses[i] == bossMain)
                        {
                            foundInWave = true;
                            break;
                        }
                    }
                    // 如果不在当前波列表中，且名字包含DragonDescendant，则是孩儿护我召唤的龙裔
                    if (!foundInWave && bossName.Contains("DragonDescendant"))
                    {
                        isChildProtectionDescendant = true;
                        DevLog("[BossRush] 检测到孩儿护我召唤的龙裔，跳过波次计数");
                    }
                }
                else if (bossesPerWave <= 1 && currentBoss != bossMain && bossName.Contains("DragonDescendant"))
                {
                    // 单Boss模式：如果不是当前Boss且是龙裔，则是孩儿护我召唤的
                    isChildProtectionDescendant = true;
                    DevLog("[BossRush] 检测到孩儿护我召唤的龙裔（单Boss模式），跳过波次计数");
                }

                // 双保险：基于掉落事件再做一次死亡判定（HandleBossDeath 内部会去重）
                // 孩儿护我召唤的龙裔跳过此调用，避免错误递增波次计数
                if (!isChildProtectionDescendant)
                {
                    HandleBossDeath(bossMain, dmgInfo);
                }

                // 注意：龙裔遗族特殊掉落已移至 RandomizeBossLoot_LootAndRewards 方法中
                // 在掉落箱创建并填充物品后，直接添加到掉落箱的Inventory中

                // 无间炼狱：完全禁止 lootbox 掉落，改为现金池逻辑
                if (infiniteHellMode)
                {
                    try
                    {
                        bossMain.dropBoxOnDead = false;
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("OnBossBeforeSpawnLoot_disableDrop", "无间炼狱关闭 Boss 掉落箱失败", e);
                    }
                    FinalizeBossRushLootboxPathTracking(bossMain);
                    return;
                }

                // ModConfig 变更已由 Config.TryLoadSingleModConfigValue 在事件回调里直接写入 config，
                // 此处无需再做反射式热刷新。
                bool useLegacyBossLootProbabilities = config != null && config.useLegacyBossLootProbabilities;

                int modeFPlunderLootPenaltyCount = 0;
                int modeFPlunderLootBonusCount = 0;
                bool useModeFAbstractPlunderLootTracking = modeFActive && config != null && config.enableRandomBossLoot;
                if (modeFActive)
                {
                    CharacterMainControl killer = null;
                    try { killer = dmgInfo.fromCharacter; } catch (Exception e) { LogLootWarningLimited("OnBossBeforeSpawnLoot_killer", "读取击杀者失败", e); }

                    if (killer != null)
                    {
                        TryHandleModeFBossPreLootPlunder(killer, bossMain);
                    }

                    if (useModeFAbstractPlunderLootTracking)
                    {
                        modeFPlunderLootPenaltyCount = ConsumeModeFBossPendingHighQualityLootPenaltyCount(bossMain);
                        modeFPlunderLootBonusCount = ConsumeModeFBossCarriedHighQualityLootCount(bossMain);
                    }

                    if (modeFPlunderLootPenaltyCount > 0 || modeFPlunderLootBonusCount > 0)
                    {
                        DevLog("[ModeF] 掉落箱高品质调节: boss=" + bossName
                            + ", penalty=" + modeFPlunderLootPenaltyCount
                            + ", bonus=" + modeFPlunderLootBonusCount);
                    }
                }

                if (config == null || !config.enableRandomBossLoot)
                {
                    if (modeEActive || modeFActive)
                    {
                        try
                        {
                            if (bossMain.transform != null)
                            {
                                StartCoroutine(BossRushLootboxUtility.DecorateLootboxesNearPosition(this, bossMain.transform.position, true));
                            }
                        }
                        catch (Exception e)
                        {
                            LogLootWarningLimited("OnBossBeforeSpawnLoot_decorateOriginal", "装饰 Mode E/F 原生掉落箱失败", e);
                        }
                    }

                    FinalizeBossRushLootboxPathTracking(bossMain);
                    return; // 未启用随机掉落，保持原版掉落
                }

                // 计算击杀耗时（用于概率加成与日志）
                float spawnTime = bossSpawnTimes[bossMain];
                float killDuration = Time.time - spawnTime;

                // 计算 Boss 最大生命（用于掉落格子数量、概率加成与日志）
                float maxHealth = 100f;
                try
                {
                    if (bossMain.Health != null)
                    {
                        maxHealth = bossMain.Health.MaxHealth;
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("OnBossBeforeSpawnLoot_maxHealth", "读取 Boss 最大生命失败，回退默认值", e);
                }

                // 基础掉落格子数量：按 Boss 池的基础血量范围，将当前 Boss 血量线性映射到 [7,15]
                int baseCount = 10;
                float refMin = minBossBaseHealth;
                float refMax = maxBossBaseHealth;
                if (refMax > refMin && refMin > 0f)
                {
                    float t = Mathf.InverseLerp(refMin, refMax, maxHealth);
                    float mapped = Mathf.Lerp(7f, 15f, t);
                    baseCount = Mathf.RoundToInt(mapped);
                }
                else
                {
                    // 如果未能正确初始化 Boss 池血量范围，则退化为按自身血量近似映射
                    float mapped = 7f + (maxHealth / 100f);
                    baseCount = Mathf.RoundToInt(mapped);
                }
                baseCount = Mathf.Clamp(baseCount, 7, 15);

                // 每100血量增加的高品质概率加成
                float lootHealthBonusRate = LOOT_HEALTH_BONUS_RATE;
                float highChanceBonusByHealth = (maxHealth / 100f) * lootHealthBonusRate;
                float legacyBonusFactor = ComputeLegacyBossLootBonusFactor(maxHealth, killDuration);

                // 击杀时间加成：击杀越快，加成越高，最多 LOOT_TIME_BONUS_RATE
                float timeBonus = ComputeBossKillSpeedFactor(maxHealth, killDuration) * LOOT_TIME_BONUS_RATE;
                highChanceBonusByHealth += timeBonus;

                DevLog("[BossRush] Boss击杀耗时: " + killDuration.ToString("F1")
                    + "秒, MaxHP=" + maxHealth
                    + ", 基础掉落数量=" + baseCount
                    + ", 高品质概率加成=" + highChanceBonusByHealth.ToString("P3")
                    + ", legacyBonusFactor=" + legacyBonusFactor.ToString("F3"));

                // 随机生成物品并填充到Boss掉落源（CharacterItem.Inventory），由原版逻辑创建LootBox
                RandomizeBossLoot_LootAndRewards(
                    bossMain,
                    baseCount,
                    killDuration,
                    highChanceBonusByHealth,
                    useLegacyBossLootProbabilities,
                    legacyBonusFactor,
                    maxHealth,
                    modeFPlunderLootBonusCount,
                    modeFPlunderLootPenaltyCount);

            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnBossBeforeSpawnLoot 错误: " + e.Message);
            }
            finally
            {
                try
                {
                    if (bossMain != null)
                    {
                        ClearBossRandomLootTracking(bossMain);
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("ClearBossRandomLootTracking", "清理随机掉落追踪时处理单个物品失败", e);
                }
            }
        }

        private void RandomizeBossLoot_LootAndRewards(
            CharacterMainControl bossMain,
            int totalCount,
            float killDuration,
            float highChanceBonusByHealth,
            bool useLegacyProbabilities,
            float legacyBonusFactor,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            try
            {
                if (bossMain == null)
                {
                    DevLog("[BossRush] BossMain 无效，无法生成奖励盒子");
                    return;
                }

                if (totalCount < 1)
                {
                    totalCount = 1;
                }
                float highChance = Mathf.Clamp01(highChanceBonusByHealth);
                LegacyBossLootQualityDistribution legacyDistribution = default(LegacyBossLootQualityDistribution);
                if (useLegacyProbabilities)
                {
                    legacyDistribution = LegacyBossLootProbabilityModel.BuildDistribution(Mathf.Clamp01(legacyBonusFactor));
                }

                bool useBossDeadBoxPrefab = false;
                InteractableLootbox prefab = null;

                // 当“掉落箱作为掩体（挡子弹）”选项关闭时，优先尝试使用敌人死亡掉落用的 Lootbox 预制体
                try
                {
                    if (config != null && !config.lootBoxBlocksBullets && bossMain != null && bossMain.deadLootBoxPrefab != null)
                    {
                        prefab = bossMain.deadLootBoxPrefab;
                        useBossDeadBoxPrefab = true;
                        DevLog("[BossRush] 使用 Boss 死亡掉落的 Lootbox 模板作为奖励箱");
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("BossRewardLootbox_prefab", "读取 Boss 原生掉落箱模板失败，回退到通用模板", e);
                }

                if (prefab == null)
                {
                    prefab = GetLootBoxTemplateWithLoader();
                }

                if (prefab == null)
                {
                    DevLog("[BossRush] 未找到可用的 Lootbox 模板，回退到原版 Boss 掉落逻辑");
                    FinalizeBossRushLootboxPathTracking(bossMain);
                    return;
                }

                try
                {
                    bossMain.dropBoxOnDead = false;
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("BossRewardLootbox_disableDrop", "关闭 Boss 原生掉落箱失败", e);
                }

                Vector3 position = bossMain.transform.position + Vector3.up * 0.1f;
                Quaternion rotation = bossMain.transform.rotation;

                InteractableLootbox lootbox = UnityEngine.Object.Instantiate(prefab, position, rotation);
                lootbox.needInspect = true;

                // 为 Boss 奖励箱创建独立本地 Inventory，避免与其他 Lootbox 通过位置哈希共享同一个库存
                // 这是解决"箱子没有奖励且格子为64"问题的关键
                try
                {
                    if (InteractableLootboxInventoryHelper.EnsureLocalInventory(lootbox, 512))
                    {
                        DevLog("[BossRush] Boss 奖励箱已创建独立本地 Inventory");
                    }
                }
                catch (Exception localInvEx)
                {
                    DevLog("[BossRush] 创建 Boss 奖励箱本地 Inventory 失败: " + localInvEx.Message);
                }

                try
                {
                    BossRushLootboxUtility.DecorateLootbox(lootbox, this, modeEActive || modeFActive, !useBossDeadBoxPrefab);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 装饰 Boss 奖励箱外观失败: " + e.Message);
                }

                // 仅在使用 BossRush 专用奖励箱模板时，按配置处理“挡子弹”和伪搬运交互
                if (!useBossDeadBoxPrefab)
                {
                    // 根据配置决定是否让掉落箱作为子弹掩体
                    try
                    {
                        ApplyLootBoxCoverSetting(lootbox);
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("BossRewardLootbox_cover", "应用 Boss 奖励箱掩体配置失败", e);
                    }

                }

                try
                {
                    MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex);
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("BossRewardLootbox_scene", "将 Boss 奖励箱移动到当前场景失败", e);
                }

                Duckov.Utilities.LootBoxLoader loader = lootbox.GetComponent<Duckov.Utilities.LootBoxLoader>();
                if (loader == null)
                {
                    try
                    {
                        loader = lootbox.gameObject.AddComponent<Duckov.Utilities.LootBoxLoader>();
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("BossRewardLootbox_loader", "添加 Boss 奖励箱 LootBoxLoader 失败", e);
                    }
                }

                if (loader != null)
                {
                    try
                    {
                        // 根据 totalCount 设定随机数量范围（不再向下浮动，保证不少于目标数量）
                        int minCount = Math.Max(1, totalCount);
                        int maxCount = Math.Max(minCount, totalCount + 1);

                        System.Reflection.FieldInfo randomCountField = BossLootBoxLoaderReflection.RandomCountField;
                        if (randomCountField != null)
                        {
                            Vector2Int rc = new Vector2Int(minCount, maxCount);
                            randomCountField.SetValue(loader, rc);
                        }

                        // 调整品质权重：开启原版概率开关时使用固定区间分布，否则沿用旧的高品质总概率逻辑
                        System.Reflection.FieldInfo qualitiesField = BossLootBoxLoaderReflection.QualitiesField;
                        if (qualitiesField != null)
                        {
                            Duckov.Utilities.RandomContainer<int> qualities = qualitiesField.GetValue(loader) as Duckov.Utilities.RandomContainer<int>;
                            if (qualities != null)
                            {
                                qualities.entries.Clear();

                                if (useLegacyProbabilities)
                                {
	                                    for (int q = 1; q <= 8; q++)
	                                    {
	                                        double probability = legacyDistribution.GetProbabilityForQuality(q);
	                                        if (probability > 0.0)
	                                        {
	                                            qualities.AddEntry(q, (float)probability);
	                                        }
	                                    }
                                }
                                else
                                {
                                    float clampedHigh = Mathf.Clamp01(highChance);
                                    float lowWeight = 1f - clampedHigh;
                                    float highWeight = clampedHigh;

                                    if (lowWeight <= 0f)
                                    {
                                        lowWeight = 0.01f;
                                    }
                                    if (highWeight <= 0f)
                                    {
                                        highWeight = 0.01f;
                                    }

                                    // 使用内部常量定义的品质范围
                                    int lowQualityMin = LOOT_LOW_QUALITY_MIN;
                                    int lowQualityMax = LOOT_LOW_QUALITY_MAX;
                                    int highQualityMin = LOOT_HIGH_QUALITY_MIN;
                                    int highQualityMax = LOOT_HIGH_QUALITY_MAX;

                                    // 普通品质
                                    int lowQualityTiers = lowQualityMax - lowQualityMin + 1;
                                    float perLowQualityWeight = lowWeight / lowQualityTiers;
	                                    for (int q = lowQualityMin; q <= lowQualityMax; q++)
	                                    {
	                                        qualities.AddEntry(q, perLowQualityWeight);
	                                    }

                                    // 高品质：按 4:3:2:1 比例分配（品质5占4份，品质6占3份，品质7占2份，品质8占1份）
                                    float totalRatio = 10f;
                                    for (int i = 0; i < 4; i++)
                                    {
                                        int q = highQualityMin + i;
	                                        if (q <= highQualityMax)
	                                        {
	                                            float weight = highWeight * (GetBossLootHighQualityRatio(i) / totalRatio);
	                                            qualities.AddEntry(q, weight);
	                                        }
	                                    }
                                }

                                qualities.RefreshPercent();
                            }
                        }

                        System.Reflection.FieldInfo tagsField = BossLootBoxLoaderReflection.TagsField;
                        System.Reflection.FieldInfo excludeTagsField = BossLootBoxLoaderReflection.ExcludeTagsField;

                        if (tagsField != null)
                        {
                            Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag> tagsContainer = tagsField.GetValue(loader) as Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag>;
                            if (tagsContainer != null)
                            {
                                tagsContainer.entries.Clear();

                                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                                if (tagsData != null && tagsData.AllTags != null)
                                {
                                    var allTags = tagsData.AllTags;
                                    // Quest-tag filtering should stay aligned with the pre-legacy Boss loot path
                                    // even when the legacy probability toggle is turned off.
                                    List<Duckov.Utilities.Tag> tagExclude = BuildGeneralLootExcludeTags(tagsData, true);
                                    for (int i = 0; i < allTags.Count; i++)
                                    {
                                        Duckov.Utilities.Tag t = allTags[i];
                                        if (t == null)
                                        {
                                            continue;
                                        }
                                        if (tagExclude.Contains(t))
                                        {
                                            continue;
                                        }
                                        tagsContainer.AddEntry(t, 1f);
                                    }
                                }

                                tagsContainer.RefreshPercent();
                            }
                        }

                        if (excludeTagsField != null)
                        {
                            List<Duckov.Utilities.Tag> excludeList = excludeTagsField.GetValue(loader) as List<Duckov.Utilities.Tag>;
                            if (excludeList == null)
                            {
                                excludeList = new List<Duckov.Utilities.Tag>();
                                excludeTagsField.SetValue(loader, excludeList);
                            }

                            Duckov.Utilities.GameplayDataSettings.TagsData tagsData2 = Duckov.Utilities.GameplayDataSettings.Tags;
                            MergeGeneralLootExcludeTags(excludeList, tagsData2);
                        }

                        try
                        {
                            System.Type loaderEntryType = BossLootBoxLoaderReflection.LoaderEntryType;
                            System.Reflection.FieldInfo randomPoolField = BossLootBoxLoaderReflection.RandomPoolField;
                            object randomPoolObj = (randomPoolField != null) ? randomPoolField.GetValue(loader) : null;

                            // 新增：在新增 LootBoxLoader 组件时，randomPool 可能为 null，这里显式创建一个实例
                            if (randomPoolObj == null && randomPoolField != null)
                            {
                                try
                                {
                                    randomPoolObj = Activator.CreateInstance(randomPoolField.FieldType);
                                    randomPoolField.SetValue(loader, randomPoolObj);
                                }
                                catch (Exception e)
                                {
                                    LogLootWarningLimited("BossRewardLootbox_randomPool", "创建 Boss 奖励箱 randomPool 失败", e);
                                }
                            }

                            if (loaderEntryType != null && randomPoolObj != null)
                            {
                                System.Reflection.FieldInfo entriesField = BossLootBoxLoaderReflection.RandomPoolEntriesField;
                                object entriesObj = (entriesField != null) ? entriesField.GetValue(randomPoolObj) : null;
                                System.Collections.IList entriesList = entriesObj as System.Collections.IList;
                                System.Type randomContainerEntryType = BossLootBoxLoaderReflection.RandomPoolEntryType;

                                // 新增：如果 entries 列表本身为 null，则创建一个新的列表实例
                                if (entriesList == null && entriesField != null)
                                {
                                    try
                                    {
                                        object newEntries = Activator.CreateInstance(entriesField.FieldType);
                                        entriesField.SetValue(randomPoolObj, newEntries);
                                        entriesList = newEntries as System.Collections.IList;
                                    }
                                    catch (Exception e)
                                    {
                                        LogLootWarningLimited("BossRewardLootbox_entries", "创建 Boss 奖励箱 randomPool.entries 失败", e);
                                    }
                                }

                                if (entriesList != null && randomContainerEntryType != null)
                                {
                                    entriesList.Clear();

                                    System.Reflection.FieldInfo lootEntryItemIdField = BossLootBoxLoaderReflection.LootEntryItemIdField;
                                    System.Reflection.FieldInfo rcValueField = BossLootBoxLoaderReflection.RandomPoolEntryValueField;
                                    System.Reflection.FieldInfo rcWeightField = BossLootBoxLoaderReflection.RandomPoolEntryWeightField;

                                    if (lootEntryItemIdField != null && rcValueField != null && rcWeightField != null)
                                    {
                                        List<int> candidateIds = bossRandomLootCandidateIdScratch;
                                        candidateIds.Clear();
                                        bool hasCandidateCache = false;
                                        if (useLegacyProbabilities)
                                        {
                                            hasCandidateCache = TryGetLegacyBossLootCandidates(candidateIds);
                                        }
                                        else
                                        {
                                            HashSet<int> idSet = BuildGeneralBossLootCandidateIdSet();
                                            if (idSet.Count > 0)
                                            {
                                                candidateIds.AddRange(idSet);
                                                hasCandidateCache = true;
                                            }
                                        }

                                        DevLog("[BossRush] Boss 奖励候选物品数量=" + candidateIds.Count + ", useLegacyBossLootProbabilities=" + useLegacyProbabilities + ", candidateCacheReady=" + _legacyBossLootCandidateCacheInitialized + ", candidateCacheUsed=" + hasCandidateCache);

                                        if (candidateIds.Count > 0)
                                        {
                                            // 使用内部常量定义的品质范围
                                            int highQualityMin = LOOT_HIGH_QUALITY_MIN;
                                            int highQualityMax = LOOT_HIGH_QUALITY_MAX;

                                            // 第一遍：统计各品质物品数量（按具体品质等级分别统计）
                                            int lowQualityItemCount = 0;
                                            Dictionary<int, int> itemQualities = bossRandomLootQualityScratch;
                                            itemQualities.Clear();
                                            // 统计各高品质等级的物品数量：quality5Count, quality6Count, quality7Count, quality8Count
                                            int[] highQualityCounts = bossRandomLootHighQualityCountsScratch; // 索引0=品质5, 1=品质6, 2=品质7, 3=品质8
                                            Array.Clear(highQualityCounts, 0, highQualityCounts.Length);
                                            // 低品质按 Q1-Q4 分别统计，供 legacy 分支直接使用，避免后续重复遍历 itemQualities
                                            int[] lowQualityCountsByGrade = bossRandomLootLowQualityCountsScratch; // 索引0=Q1, 1=Q2, 2=Q3, 3=Q4
                                            Array.Clear(lowQualityCountsByGrade, 0, lowQualityCountsByGrade.Length);

                                            for (int candidateIndex = 0; candidateIndex < candidateIds.Count; candidateIndex++)
                                            {
                                                int id2 = candidateIds[candidateIndex];
	                                                int quality = GetBossLootCandidateQuality(id2);

                                                itemQualities[id2] = quality;

                                                if (quality >= highQualityMin && quality <= highQualityMax)
                                                {
                                                    int idx = quality - highQualityMin;
                                                    if (idx >= 0 && idx < 4)
                                                    {
                                                        highQualityCounts[idx]++;
                                                    }
                                                }
                                                else if (quality >= LOOT_LOW_QUALITY_MIN && quality <= LOOT_LOW_QUALITY_MAX)
                                                {
                                                    lowQualityItemCount++;
                                                    int lowIdx = quality - LOOT_LOW_QUALITY_MIN;
                                                    if (lowIdx >= 0 && lowIdx < 4)
                                                    {
                                                        lowQualityCountsByGrade[lowIdx]++;
                                                    }
                                                }
                                                else
                                                {
                                                    // quality 不在 1-8 范围内（GetBossLootCandidateQuality 回退时可能返回 1，
                                                    // 但为防御其它异常取值，这里仍按低品质计入总数但不入任何具体分档）
                                                    lowQualityItemCount++;
                                                }
                                            }

                                            int totalHighQualityItemCount = highQualityCounts[0] + highQualityCounts[1] + highQualityCounts[2] + highQualityCounts[3];
                                            DevLog("[BossRush] Boss 奖励品质统计: 普通品质=" + lowQualityItemCount + ", 高品质=" + totalHighQualityItemCount + " (Q5=" + highQualityCounts[0] + ", Q6=" + highQualityCounts[1] + ", Q7=" + highQualityCounts[2] + ", Q8=" + highQualityCounts[3] + ")");

                                            float perLowWeight = 0f;
                                            float[] perHighWeightByQuality = bossRandomLootHighWeightsScratch;
                                            Array.Clear(perHighWeightByQuality, 0, perHighWeightByQuality.Length);
                                            if (useLegacyProbabilities)
                                            {
                                                // 直接复用第一遍统计的 lowQualityCountsByGrade / highQualityCounts，
                                                // 避免对 itemQualities（通常 600+ 元素）再做 4 次冗余 foreach。
                                                DevLog("[BossRush] Legacy Boss 概率分布: Q1=" + legacyDistribution.Quality1.ToString("P4")
                                                    + ", Q2=" + legacyDistribution.Quality2.ToString("P4")
                                                    + ", Q3=" + legacyDistribution.Quality3.ToString("P4")
                                                    + ", Q4=" + legacyDistribution.Quality4.ToString("P4")
                                                    + ", Q5=" + legacyDistribution.Quality5.ToString("P4")
                                                    + ", Q6=" + legacyDistribution.Quality6.ToString("P4")
                                                    + ", Q7=" + legacyDistribution.Quality7.ToString("P4")
                                                    + ", Q8=" + legacyDistribution.Quality8.ToString("P4")
                                                    + ", bonusFactor=" + legacyBonusFactor.ToString("P2"));

                                                for (int i = 0; i < 4; i++)
                                                {
                                                    int count = highQualityCounts[i];
                                                    double qualityTotalWeight = legacyDistribution.GetProbabilityForQuality(i + 5);
                                                    perHighWeightByQuality[i] = count > 0 ? (float)(qualityTotalWeight / count) : 0f;
                                                }

                                                int quality1Count = lowQualityCountsByGrade[0];
                                                int quality2Count = lowQualityCountsByGrade[1];
                                                int quality3Count = lowQualityCountsByGrade[2];
                                                int quality4Count = lowQualityCountsByGrade[3];
                                                float perQuality1Weight = quality1Count > 0 ? (float)(legacyDistribution.Quality1 / quality1Count) : 0f;
                                                float perQuality2Weight = quality2Count > 0 ? (float)(legacyDistribution.Quality2 / quality2Count) : 0f;
                                                float perQuality3Weight = quality3Count > 0 ? (float)(legacyDistribution.Quality3 / quality3Count) : 0f;
                                                float perQuality4Weight = quality4Count > 0 ? (float)(legacyDistribution.Quality4 / quality4Count) : 0f;

                                                for (int candidateIndex = 0; candidateIndex < candidateIds.Count; candidateIndex++)
                                                {
                                                    int id2 = candidateIds[candidateIndex];
                                                    object lootEntry = Activator.CreateInstance(loaderEntryType);
                                                    lootEntryItemIdField.SetValue(lootEntry, id2);

                                                    object rcEntry = Activator.CreateInstance(randomContainerEntryType);
                                                    rcValueField.SetValue(rcEntry, lootEntry);

                                                    int quality = 1;
                                                    itemQualities.TryGetValue(id2, out quality);

                                                    float itemWeight = perQuality1Weight;
                                                    switch (quality)
                                                    {
                                                        case 1: itemWeight = perQuality1Weight; break;
                                                        case 2: itemWeight = perQuality2Weight; break;
                                                        case 3: itemWeight = perQuality3Weight; break;
                                                        case 4: itemWeight = perQuality4Weight; break;
                                                        case 5: itemWeight = perHighWeightByQuality[0]; break;
                                                        case 6: itemWeight = perHighWeightByQuality[1]; break;
                                                        case 7: itemWeight = perHighWeightByQuality[2]; break;
                                                        case 8: itemWeight = perHighWeightByQuality[3]; break;
                                                    }

                                                    if (id2 == 1254)
                                                    {
                                                        itemWeight *= 0.1f;
                                                    }

                                                    rcWeightField.SetValue(rcEntry, itemWeight);
                                                    entriesList.Add(rcEntry);
                                                }
                                            }
                                            else
                                            {
                                                // 计算权重：使品质分布符合 highChance 设定，高品质内部按 4:3:2:1 比例分配
                                                float clampedHigh = Mathf.Clamp01(highChance);
                                                float lowTotalWeight = 1f - clampedHigh;
                                                float highTotalWeight = clampedHigh;

                                                int safeLowCount = (lowQualityItemCount == 0) ? 1 : lowQualityItemCount;
                                                perLowWeight = lowTotalWeight / safeLowCount;

                                                float totalRatio = 10f;
                                                for (int i = 0; i < 4; i++)
                                                {
                                                    float qualityTotalWeight = highTotalWeight * (GetBossLootHighQualityRatio(i) / totalRatio);
                                                    int count = highQualityCounts[i];
                                                    if (count == 0) count = 1;
                                                    perHighWeightByQuality[i] = qualityTotalWeight / count;
                                                }

                                                DevLog("[BossRush] Boss 奖励权重: 普通单个=" + perLowWeight.ToString("F4") + ", Q5单个=" + perHighWeightByQuality[0].ToString("F4") + ", Q6单个=" + perHighWeightByQuality[1].ToString("F4") + ", Q7单个=" + perHighWeightByQuality[2].ToString("F4") + ", Q8单个=" + perHighWeightByQuality[3].ToString("F4"));

                                                for (int candidateIndex = 0; candidateIndex < candidateIds.Count; candidateIndex++)
                                                {
                                                    int id2 = candidateIds[candidateIndex];
                                                    object lootEntry = Activator.CreateInstance(loaderEntryType);
                                                    lootEntryItemIdField.SetValue(lootEntry, id2);

                                                    object rcEntry = Activator.CreateInstance(randomContainerEntryType);
                                                    rcValueField.SetValue(rcEntry, lootEntry);

                                                    int quality = 1;
                                                    itemQualities.TryGetValue(id2, out quality);

                                                    float itemWeight;
                                                    if (quality >= highQualityMin && quality <= highQualityMax)
                                                    {
                                                        int idx = quality - highQualityMin;
                                                        itemWeight = (idx >= 0 && idx < 4) ? perHighWeightByQuality[idx] : perHighWeightByQuality[0];
                                                    }
                                                    else
                                                    {
                                                        itemWeight = perLowWeight;
                                                    }

                                                    if (id2 == 1254)
                                                    {
                                                        itemWeight *= 0.1f;
                                                    }

                                                    rcWeightField.SetValue(rcEntry, itemWeight);
                                                    entriesList.Add(rcEntry);
                                                }
                                            }

                                            DevLog("[BossRush] Boss 奖励 randomPool 条目数=" + entriesList.Count);
                                        }

                                        // 不使用 LootBoxLoader 自带的 fixedItems 保底；legacy Q5+ 保底在后续协程中追加
                                        // 这里仍需初始化 fixedItems 字段，避免 LootBoxLoader.Setup() 空引用
                                        System.Reflection.FieldInfo fixedItemsField = BossLootBoxLoaderReflection.FixedItemsField;
                                        System.Reflection.FieldInfo fixedChanceField = BossLootBoxLoaderReflection.FixedChanceField;

                                        if (fixedItemsField != null)
                                        {
                                            List<int> fixedItems = fixedItemsField.GetValue(loader) as List<int>;
                                            if (fixedItems == null)
                                            {
                                                fixedItems = new List<int>();
                                                fixedItemsField.SetValue(loader, fixedItems);
                                            }
                                            fixedItems.Clear();
                                        }

                                        // 禁用 LootBoxLoader 自带的 fixedItems 保底，避免与后续协程追加的保底重复
                                        if (fixedChanceField != null)
                                        {
                                            fixedChanceField.SetValue(loader, 0f);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception poolEx)
                        {
                            DevLog("[BossRush] 构建 Boss 奖励 randomPool 失败: " + poolEx.Message);
                        }

                        loader.randomFromPool = true;
                        loader.ignoreLevelConfig = true;
                        loader.CalculateChances();

                        // 关键：手动调用 Setup() 填充物品
                        // 因为 CreateLocalInventory 后 Inventory 属性直接返回 inventoryReference，
                        // 不会再走 GetOrCreateInventory 的逻辑，所以 LootBoxLoader.Setup() 不会被自动触发
                        try
                        {
                            loader.StartSetup();
                            DevLog("[BossRush] 已手动触发 LootBoxLoader.StartSetup() 填充物品");
                        }
                        catch (Exception setupEx)
                        {
                            DevLog("[BossRush] 调用 LootBoxLoader.StartSetup() 失败: " + setupEx.Message);
                        }
                    }
                    catch (Exception cfgEx)
                    {
                        DevLog("[BossRush] 配置 Boss 奖励 LootBoxLoader 失败: " + cfgEx.Message);
                    }
                }
                else
                {
                    DevLog("[BossRush] Boss 奖励盒子上没有 LootBoxLoader 组件，将使用 Prefab 默认内容");
                }

                // 访问一次 Inventory，确保需要搜索动画，并设置初始容量
                Inventory inventory = lootbox.Inventory;
                if (inventory != null)
                {
                    inventory.NeedInspection = lootbox.needInspect;
                    // 先设置一个较大的初始容量，等 LootBoxLoader 填充完成后再调整
                    inventory.SetCapacity(512);
                }

                if (useLegacyProbabilities)
                {
                    DevLog("[BossRush] 已为 Boss 生成专用奖励盒子，总目标物品数量=" + totalCount + ", legacyBonusFactor=" + legacyBonusFactor.ToString("P1") + "（击杀耗时: " + killDuration.ToString("F1") + "秒）");
                }
                else
                {
                    DevLog("[BossRush] 已为 Boss 生成专用奖励盒子，总目标物品数量=" + totalCount + ", 期望高品质比例=" + highChance.ToString("P0") + "（击杀耗时: " + killDuration.ToString("F1") + "秒）");
                }

                // Boss特殊掉落：在掉落箱创建后添加专属掉落物
                // 统一使用一个协程处理所有Boss的特殊掉落
                try
                {
                    this.StartCoroutine(AddBossSpecialLootToLootboxCoroutine(
                        lootbox,
                        bossMain,
                        useLegacyProbabilities,
                        bossMaxHealth,
                        modeFPlunderLootBonusCount,
                        modeFPlunderLootPenaltyCount));
                }
                catch (Exception specialLootEx)
                {
                    DevLog("[BossRush] 安排Boss特殊掉落协程失败: " + specialLootEx.Message);
                }

                // 调试：记录实际掉落物的品质与价格，方便验证过滤逻辑
                try
                {
                    this.StartCoroutine(LogBossLootInventory_LootAndRewards(lootbox));
                }
                catch (Exception logEx)
                {
                    DevLog("[BossRush] 安排 Boss 掉落实际物品日志协程失败: " + logEx.Message);
                }
            }
            catch (Exception e)
            {
                FinalizeBossRushLootboxPathTracking(bossMain);
                DevLog("[BossRush] RandomizeBossLoot 错误: " + e.Message);
            }
        }
    }
}

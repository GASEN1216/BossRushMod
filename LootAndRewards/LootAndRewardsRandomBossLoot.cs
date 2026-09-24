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
        internal bool ShouldUseBossDeadBoxPrefabForArena()
        {
            return config != null && !config.lootBoxBlocksBullets;
        }

        internal bool IsModeEOrFActiveForArenaLoot()
        {
            return modeEActive || modeFActive;
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
                    // 本分支不碰 dropBoxOnDead，官方 characterItem 箱照建；
                    // 但额外掉落已按"会有 BossRush 奖励箱"defer 进 pending，
                    // 下面的 Finalize 会把它们撤销。先还回 characterItem。
                    ReturnPendingExtraLootToCharacterItem(bossMain);
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
                    // 原生箱由 characterItem 建：后山种子直接放进去（此前 Mode E/F 永远不掉种子）
                    TryAddBackMountainSeedToCharacterItem(bossMain);
                    return;
                }

                // 只处理由 BossRush 生成且被追踪的 Boss
                if (!bossSpawnTimes.ContainsKey(bossMain))
                {
                    // 同上：官方箱照建，pending 必须先还回去
                    ReturnPendingExtraLootToCharacterItem(bossMain);
                    FinalizeBossRushLootboxPathTracking(bossMain);
                    DevLog("[BossRush] 掉落事件跳过: bossSpawnTimes 不包含此Boss, 当前追踪数量=" + bossSpawnTimes.Count);
                    return;
                }

                DevLog("[BossRush] 掉落事件通过检查，开始处理Boss掉落: " + bossName);

                // 检查是否是孩儿护我召唤的龙裔（不在currentWaveBosses中但在bossSpawnTimes中）
                // 这类龙裔只需要掉落，不参与波次计数
                //
                // 注意：这里的名字启发式如今是**第二道防线**。真正的口径已经收进
                // HandleBossDeath 的 IsCurrentWaveBossMember——它按引用判本波成员，不看名字，
                // 因此乱入 Boss、Mode D Boss 这些同样不在波次容器里的旁路 Boss 也一并被挡住。
                // 保留本段是因为 DragonDescendantBoss 的注释仍在引用这个语义，且它能少走一次调用。
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
                    // 无间炼狱既不建 BossRush 奖励箱、又刚把官方 characterItem 箱关掉，
                    // 所以四个 integration 的额外掉落在这条分支上无处可去。
                    // 走无间炼狱自己的奖励通道：把物品 Drop 到世界里
                    // （里程碑现金用的就是这条，见 LootAndRewardsInfiniteHell）。
                    // 必须排在 FinalizeBossRushLootboxPathTracking 之前——后者会撤销 pending。
                    DropPendingExtraLootIntoWorld(bossMain);
                    TryDropBackMountainSeedIntoWorld(bossMain);
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

                    // 保持原版掉落 = 官方箱照建，pending 必须先还回去
                    ReturnPendingExtraLootToCharacterItem(bossMain);
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
            wavesArenaRuntime.RandomizeBossLoot_LootAndRewards(bossMain, totalCount, killDuration,
                highChanceBonusByHealth, useLegacyProbabilities, legacyBonusFactor, bossMaxHealth,
                modeFPlunderLootBonusCount, modeFPlunderLootPenaltyCount);
        }
    }
}

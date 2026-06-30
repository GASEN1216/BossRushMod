// ============================================================================
// LootAndRewardsInfiniteHell.cs - 无间炼狱奖励流程
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
        /// 无间炼狱单波完成：掉落现金、更新显示并准备下一波（LootAndRewards 分部实现）
        /// </summary>
        private async void OnInfiniteHellWaveCompleted_LootAndRewards()
        {
            try
            {
                // 增加波次
                infiniteHellWaveIndex++;

                // 检查无间炼狱波次成就
                CheckInfiniteHellAchievements(infiniteHellWaveIndex);

                long cashThisWave = infiniteHellWaveCashThisWave;
                infiniteHellWaveCashThisWave = 0L;

                // 在路牌位置掉落 3 叠现金
                try
                {
                    if (cashThisWave > 0)
                    {
                        long perStack = cashThisWave / 3L;
                        if (perStack < 1L) perStack = cashThisWave; // 波次奖励太低时全部塞一叠

                        // 从配置系统获取当前地图的默认位置作为兜底
                        Vector3 basePos = GetCurrentSceneDefaultPosition();
                        try
                        {
                            if (_bossRushSignGameObject != null)
                            {
                                basePos = _bossRushSignGameObject.transform.position + Vector3.up * 0.2f;
                            }
                        }
                        catch (Exception e)
                        {
                            LogLootWarningLimited("InfiniteHellCash_basePos", "定位无间炼狱现金掉落基准点失败", e);
                        }

                        int stackCount = (cashThisWave >= perStack * 3L) ? 3 : 1;
                        for (int i = 0; i < stackCount; i++)
                        {
                            long stackValue = (stackCount == 1) ? cashThisWave : perStack;
                            if (stackValue <= 0) break;

                            Item cashItem = null;
                            try
                            {
                                cashItem = ItemAssetsCollection.InstantiateSync(EconomyManager.CashItemID);
                            }
                            catch (Exception e)
                            {
                                LogLootWarningLimited("InfiniteHellCash_create", "创建无间炼狱现金物品失败", e);
                            }

                            if (cashItem == null)
                            {
                                break;
                            }

                            try
                            {
                                cashItem.StackCount = (int)Mathf.Clamp(stackValue, 1, int.MaxValue);
                            }
                            catch (Exception e)
                            {
                                LogLootWarningLimited("InfiniteHellCash_stack", "设置无间炼狱现金堆叠数失败", e);
                            }

                            Vector3 offset = (stackCount == 1) ? Vector3.zero : (new Vector3(i - 1, 0f, 0f) * 0.3f);
                            Vector3 dropPos = basePos + offset;

                            try
                            {
                                cashItem.Drop(dropPos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                            }
                            catch (Exception e)
                            {
                                LogLootWarningLimited("InfiniteHellCash_drop", "掉落无间炼狱现金失败", e);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 无间炼狱单波现金奖励发放失败: " + e.Message);
                }

                // 只在无间炼狱下显示现金池文案
                try
                {
                    if (bossRushSignInteract != null && infiniteHellCashPool > 0)
                    {
                        bossRushSignInteract.UpdateInfiniteHellCashDisplay(infiniteHellCashPool);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 更新无间炼狱现金池路牌显示失败: " + e.Message);
                }

                // 玩家头顶气泡提示现金池累积
                try
                {
                    CharacterMainControl player = null;
                    try { player = CharacterMainControl.Main; } catch (Exception e) { LogLootWarningLimited("InfiniteHellBubble_main", "读取无间炼狱现金池气泡玩家失败", e); }
                    if (player == null && playerCharacter != null)
                    {
                        try { player = playerCharacter as CharacterMainControl; } catch (Exception e) { LogLootWarningLimited("InfiniteHellBubble_playerCharacter", "从 playerCharacter 解析无间炼狱现金池气泡玩家失败", e); }
                    }

                    if (player != null)
                    {
                        string bubble = "现金池已累计：<color=red>" + infiniteHellCashPool.ToString() + "</color>";
                        // 使用文档示例中的最简调用形式，避免可选参数带来的兼容性问题
                        await DialogueBubblesManager.Show(bubble, player.transform);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 显示无间炼狱现金池气泡失败: " + e.Message);
                }

                // 每 5 波奖励一次 1253 号物品（在路牌位置掉落）
                try
                {
                    if (infiniteHellWaveIndex > 0 && infiniteHellWaveIndex % 5 == 0)
                    {
                        int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                        if (rewardTypeId <= 0)
                        {
                            rewardTypeId = 1253;
                        }

                        Item reward5 = null;
                        try
                        {
                            reward5 = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                        }
                        catch (Exception e)
                        {
                            LogLootWarningLimited("InfiniteHellWave5_create", "创建无间炼狱 5 波奖励物品失败", e);
                        }

                        if (reward5 != null)
                        {
                            try
                            {
                                // 从配置系统获取当前地图的默认位置作为兜底
                                Vector3 basePos = GetCurrentSceneDefaultPosition();
                                try
                                {
                                    if (_bossRushSignGameObject != null)
                                    {
                                        basePos = _bossRushSignGameObject.transform.position + Vector3.up * 0.3f;
                                    }
                                }
                                catch (Exception e)
                                {
                                    LogLootWarningLimited("InfiniteHellWave5_basePos", "定位无间炼狱 5 波奖励基准点失败", e);
                                }

                                try
                                {
                                    reward5.Drop(basePos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                                }
                                catch (Exception e)
                                {
                                    LogLootWarningLimited("InfiniteHellWave5_drop", "掉落无间炼狱 5 波奖励失败", e);
                                }
                            }
                            catch (Exception e)
                            {
                                LogLootWarningLimited("InfiniteHellWave5_inner", "准备无间炼狱 5 波奖励失败", e);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 发放无间炼狱 5 波奖励失败: " + e.Message);
                }

                // 每100波递进式里程碑奖励（在路牌位置掉落）
                try
                {
                    int currentTier = infiniteHellWaveIndex / 100;
                    if (infiniteHellWaveIndex > 0 && infiniteHellWaveIndex % 100 == 0 && currentTier > infiniteHellMilestoneRewardTier)
                    {
                        // 递进倍率：2^(tier-1)，使用位运算
                        int multiplier = 1 << (currentTier - 1);
                        int crownCount = multiplier;                  // 皇冠数量：1, 2, 4, 8...
                        long totalCash = 10000000L * multiplier;      // 现金总额：1000万, 2000万, 4000万...
                        long cashPerStack = totalCash / 100;          // 每叠金额

                        // 获取掉落基准位置
                        Vector3 basePos = GetCurrentSceneDefaultPosition();
                        try
                        {
                            if (_bossRushSignGameObject != null)
                            {
                                basePos = _bossRushSignGameObject.transform.position + Vector3.up * 0.3f;
                            }
                        }
                        catch (Exception e)
                        {
                            LogLootWarningLimited("InfiniteHellMilestone_basePos", "定位无间炼狱 100 波里程碑基准点失败", e);
                        }

                        // 掉落皇冠（TypeID 1254）
                        for (int ci = 0; ci < crownCount; ci++)
                        {
                            try
                            {
                                Item crown = ItemAssetsCollection.InstantiateSync(1254);
                                if (crown != null)
                                {
                                    Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                                    crown.Drop(basePos, true, dir, UnityEngine.Random.Range(30f, 60f));
                                }
                            }
                            catch (Exception e)
                            {
                                LogLootWarningLimited("InfiniteHellMilestone_crown", "掉落无间炼狱里程碑皇冠失败", e);
                            }
                        }

                        // 掉落现金（100叠，每叠 cashPerStack）
                        for (int ci = 0; ci < 100; ci++)
                        {
                            try
                            {
                                Item cashReward = ItemAssetsCollection.InstantiateSync(EconomyManager.CashItemID);
                                if (cashReward != null)
                                {
                                    cashReward.StackCount = (int)cashPerStack;
                                    Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                                    cashReward.Drop(basePos, true, dir, UnityEngine.Random.Range(30f, 60f));
                                }
                            }
                            catch (Exception e)
                            {
                                LogLootWarningLimited("InfiniteHellMilestone_cash", "掉落无间炼狱里程碑现金失败", e);
                            }
                        }

                        // 更新已发放里程碑阶数
                        infiniteHellMilestoneRewardTier = currentTier;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 发放无间炼狱 100 波里程碑奖励失败: " + e.Message);
                }

                // 准备下一波（无终点：不调用 OnAllEnemiesDefeated）
                if (config != null && config.useInteractBetweenWaves)
                {
                    // 无间炼狱模式下，主路牌交互始终用于显示现金池，
                    // 下一波由单独的 BossRushNextWaveInteractable 处理，这里不切换路牌主文案，
                    // 只确保路牌 group 中存在一个“下一波”子选项（不添加清空箱子选项）。
                    try
                    {
                        if (bossRushSignInteract != null)
                        {
                            bossRushSignInteract.AddNextWaveOnly();
                        }
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("InfiniteHellNextWaveInteract", "无间炼狱切换下一波交互失败", e);
                    }
                }
                else
                {
                    StartNextWaveCountdown();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] OnInfiniteHellWaveCompleted 处理失败: " + e.Message);
            }
        }

        /// <summary>
        /// 从共享高品质奖励池中随机取一个奖励 TypeID。
        /// 当前候选规则为 Quality>=5，且优先选择价格>=10000 的条目；调用方如需更高门槛，需要自行二次过滤。
        /// </summary>
        private int GetRandomInfiniteHellHighQualityRewardTypeID()
        {
            // 皇冠（1254）权重降为0.1，与其他模式保持一致
            const int CROWN_TYPE_ID = 1254;
            const float CROWN_REROLL_CHANCE = 0.9f;

            if (infiniteHellHighQualityItemPoolInitialized && infiniteHellHighQualityItemPool.Count > 0)
            {
                return RollInfiniteHellHighQualityRewardTypeID(CROWN_TYPE_ID, CROWN_REROLL_CHANCE);
            }

            infiniteHellHighQualityItemPoolInitialized = true;

            const int priceThreshold = 10000;
            List<int> preferred = infiniteHellHighQualityPreferredScratch;
            List<int> fallbackHighQuality = infiniteHellHighQualityFallbackScratch;
            preferred.Clear();
            fallbackHighQuality.Clear();

            try
            {
                if (!BuildGeneralBossLootCandidateIdSet(infiniteHellHighQualityCandidateIdScratch))
                {
                    return -1;
                }

                foreach (int candidateId in infiniteHellHighQualityCandidateIdScratch)
                {
                    int v = 0;
                    int quality = -1;

                    if (!TryGetInfiniteHellRewardCandidateValueQuality(candidateId, out v, out quality))
                    {
                        continue;
                    }

                    if (quality >= 5)
                    {
                        if (v >= priceThreshold)
                        {
                            preferred.Add(candidateId);
                        }
                        else
                        {
                            fallbackHighQuality.Add(candidateId);
                        }
                    }
                }

                List<int> pool = null;

                if (preferred.Count > 0)
                {
                    pool = preferred;
                }
                else if (fallbackHighQuality.Count > 0)
                {
                    pool = fallbackHighQuality;
                }

                if (pool == null || pool.Count == 0)
                {
                    return -1;
                }

                infiniteHellHighQualityItemPool.Clear();
                infiniteHellHighQualityItemPool.AddRange(pool);
            }
            finally
            {
                ClearInfiniteHellHighQualityRewardScratch();
            }

            return RollInfiniteHellHighQualityRewardTypeID(CROWN_TYPE_ID, CROWN_REROLL_CHANCE);
        }

        private int RollInfiniteHellHighQualityRewardTypeID(int crownTypeId, float crownRerollChance)
        {
            if (infiniteHellHighQualityItemPool.Count <= 0)
            {
                return -1;
            }

            int index = UnityEngine.Random.Range(0, infiniteHellHighQualityItemPool.Count);
            int finalId = infiniteHellHighQualityItemPool[index];

            // 如果抽到皇冠，90%概率重新抽取
            if (finalId == crownTypeId && UnityEngine.Random.value < crownRerollChance)
            {
                index = UnityEngine.Random.Range(0, infiniteHellHighQualityItemPool.Count);
                finalId = infiniteHellHighQualityItemPool[index];
            }

            return finalId;
        }

        private bool TryGetInfiniteHellRewardCandidateValueQuality(int candidateId, out int value, out int quality)
        {
            if (TryGetCachedItemValue(candidateId, out value, out quality))
            {
                return true;
            }

            value = 0;
            quality = -1;
            Item temp = null;
            try
            {
                temp = ItemAssetsCollection.InstantiateSync(candidateId);
                if (temp == null)
                {
                    return false;
                }

                try { value = temp.Value; } catch { value = 0; }
                try { quality = temp.Quality; } catch { quality = -1; }
                return true;
            }
            catch
            {
                value = 0;
                quality = -1;
                return false;
            }
            finally
            {
                if (temp != null && temp.gameObject != null)
                {
                    UnityEngine.Object.Destroy(temp.gameObject);
                }
            }
        }

        private void ClearInfiniteHellHighQualityRewardScratch()
        {
            infiniteHellHighQualityCandidateIdScratch.Clear();
            infiniteHellHighQualityPreferredScratch.Clear();
            infiniteHellHighQualityFallbackScratch.Clear();
        }
    }
}

using System;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        /// <summary>
        /// 无间炼狱单波完成：掉落现金、更新显示并准备下一波（LootAndRewards 分部实现）
        /// </summary>
        internal async void OnInfiniteHellWaveCompleted_LootAndRewards()
        {
            Func<bool> isCompletionCurrent = WavesArenaRuntimeModule.CaptureValidity(owner, false, true);
            try
            {
                // 增加波次
                InfiniteHellWaveIndex++;

                // 检查无间炼狱波次成就
                owner.CheckInfiniteHellAchievementsForArena(InfiniteHellWaveIndex);

                long cashThisWave = InfiniteHellWaveCashThisWave;
                InfiniteHellWaveCashThisWave = 0L;

                // 在路牌位置掉落 3 叠现金
                try
                {
                    if (cashThisWave > 0)
                    {
                        long perStack = cashThisWave / 3L;
                        if (perStack < 1L) perStack = cashThisWave; // 波次奖励太低时全部塞一叠

                        // 从配置系统获取当前地图的默认位置作为兜底
                        Vector3 basePos = ModBehaviour.GetCurrentSceneDefaultPosition();
                        try
                        {
                            if (owner.ArenaRewardSignGameObject != null)
                            {
                                basePos = owner.ArenaRewardSignGameObject.transform.position + Vector3.up * 0.2f;
                            }
                        }
                        catch (Exception e)
                        {
                            owner.LogLootWarningLimitedForArena("InfiniteHellCash_basePos", "定位无间炼狱现金掉落基准点失败", e);
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
                                owner.LogLootWarningLimitedForArena("InfiniteHellCash_create", "创建无间炼狱现金物品失败", e);
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
                                owner.LogLootWarningLimitedForArena("InfiniteHellCash_stack", "设置无间炼狱现金堆叠数失败", e);
                            }

                            Vector3 offset = (stackCount == 1) ? Vector3.zero : (new Vector3(i - 1, 0f, 0f) * 0.3f);
                            Vector3 dropPos = basePos + offset;

                            try
                            {
                                cashItem.Drop(dropPos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                            }
                            catch (Exception e)
                            {
                                owner.LogLootWarningLimitedForArena("InfiniteHellCash_drop", "掉落无间炼狱现金失败", e);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 无间炼狱单波现金奖励发放失败: " + e.Message);
                }

                // 只在无间炼狱下显示现金池文案
                try
                {
                    if (owner.ArenaRewardSignInteract != null && InfiniteHellCashPool > 0)
                    {
                        owner.ArenaRewardSignInteract.UpdateInfiniteHellCashDisplay(InfiniteHellCashPool);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 更新无间炼狱现金池路牌显示失败: " + e.Message);
                }

                // 玩家头顶气泡提示现金池累积
                try
                {
                    CharacterMainControl player = null;
                    try { player = CharacterMainControl.Main; } catch (Exception e) { owner.LogLootWarningLimitedForArena("InfiniteHellBubble_main", "读取无间炼狱现金池气泡玩家失败", e); }
                    if (player == null && owner.ArenaPlayerCharacter != null)
                    {
                        try { player = owner.ArenaPlayerCharacter as CharacterMainControl; } catch (Exception e) { owner.LogLootWarningLimitedForArena("InfiniteHellBubble_owner.ArenaPlayerCharacter", "从 owner.ArenaPlayerCharacter 解析无间炼狱现金池气泡玩家失败", e); }
                    }

                    if (player != null)
                    {
                        string bubble = L10n.T("现金池已累计：", "Total cash earned: ") + "<color=red>" + InfiniteHellCashPool.ToString() + "</color>";
                        // 使用文档示例中的最简调用形式，避免可选参数带来的兼容性问题
                        await DialogueBubblesManager.Show(bubble, player.transform);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 显示无间炼狱现金池气泡失败: " + e.Message);
                }

                if (!isCompletionCurrent()) return;
                // 每 5 波奖励一次 1253 号物品（在路牌位置掉落）
                try
                {
                    if (InfiniteHellWaveIndex > 0 && InfiniteHellWaveIndex % 5 == 0)
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
                            owner.LogLootWarningLimitedForArena("InfiniteHellWave5_create", "创建无间炼狱 5 波奖励物品失败", e);
                        }

                        if (reward5 != null)
                        {
                            try
                            {
                                // 从配置系统获取当前地图的默认位置作为兜底
                                Vector3 basePos = ModBehaviour.GetCurrentSceneDefaultPosition();
                                try
                                {
                                    if (owner.ArenaRewardSignGameObject != null)
                                    {
                                        basePos = owner.ArenaRewardSignGameObject.transform.position + Vector3.up * 0.3f;
                                    }
                                }
                                catch (Exception e)
                                {
                                    owner.LogLootWarningLimitedForArena("InfiniteHellWave5_basePos", "定位无间炼狱 5 波奖励基准点失败", e);
                                }

                                try
                                {
                                    reward5.Drop(basePos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                                }
                                catch (Exception e)
                                {
                                    owner.LogLootWarningLimitedForArena("InfiniteHellWave5_drop", "掉落无间炼狱 5 波奖励失败", e);
                                }
                            }
                            catch (Exception e)
                            {
                                owner.LogLootWarningLimitedForArena("InfiniteHellWave5_inner", "准备无间炼狱 5 波奖励失败", e);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 发放无间炼狱 5 波奖励失败: " + e.Message);
                }

                // 每100波递进式里程碑奖励（在路牌位置掉落）
                try
                {
                    int currentTier = InfiniteHellWaveIndex / 100;
                    if (InfiniteHellWaveIndex > 0 && InfiniteHellWaveIndex % 100 == 0 && currentTier > InfiniteHellMilestoneRewardTier)
                    {
                        // 获取掉落基准位置
                        Vector3 basePos = ModBehaviour.GetCurrentSceneDefaultPosition();
                        try
                        {
                            if (owner.ArenaRewardSignGameObject != null)
                            {
                                basePos = owner.ArenaRewardSignGameObject.transform.position + Vector3.up * 0.3f;
                            }
                        }
                        catch (Exception e)
                        {
                            owner.LogLootWarningLimitedForArena("InfiniteHellMilestone_basePos", "定位无间炼狱 100 波里程碑基准点失败", e);
                        }

                        WavesArenaRuntimeModule.EnqueueMilestone(owner, currentTier, basePos);

                        // 更新已提交的里程碑阶数；未交付余额由运行期 owner 逐帧重试
                        InfiniteHellMilestoneRewardTier = currentTier;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 发放无间炼狱 100 波里程碑奖励失败: " + e.Message);
                }

                // 准备下一波（无终点：不调用 OnAllEnemiesDefeated）
                if (owner.UseInteractBetweenWavesForArena)
                {
                    // 无间炼狱模式下，主路牌交互始终用于显示现金池，
                    // 下一波由单独的 BossRushNextWaveInteractable 处理，这里不切换路牌主文案，
                    // 只确保路牌 group 中存在一个“下一波”子选项（不添加清空箱子选项）。
                    try
                    {
                        if (owner.ArenaRewardSignInteract != null)
                        {
                            owner.ArenaRewardSignInteract.AddNextWaveOnly();
                        }
                    }
                    catch (Exception e)
                    {
                        owner.LogLootWarningLimitedForArena("InfiniteHellNextWaveInteract", "无间炼狱切换下一波交互失败", e);
                    }
                }
                else
                {
                    owner.StartNextWaveCountdown();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] OnInfiniteHellWaveCompleted 处理失败: " + e.Message);
            }
        }
    }
}

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
        /// <summary>单波现金最多铺几堆；超过的部分直接记账户，避免一次刷出几十件实物。</summary>
        private const int InfiniteHellMaxCashPiles = 12;

        internal async void OnInfiniteHellWaveCompleted_LootAndRewards()
        {
            if (!BossRushCoop.HasWorldAuthority) return;
            Func<bool> isCompletionCurrent = WavesArenaRuntimeModule.CaptureValidity(owner, false, true);
            try
            {
                // 增加波次
                InfiniteHellWaveIndex++;

                // 检查无间炼狱波次成就
                if (BossRushCoop.IsConnected) DeliverCoopArenaFact("hell", InfiniteHellWaveIndex);
                else owner.CheckInfiniteHellAchievementsForArena(InfiniteHellWaveIndex);

                long cashThisWave = InfiniteHellWaveCashThisWave;
                bool personalRewards = BossRushCoop.IsConnected;
                int copies = personalRewards ? 0 : 1;
                if (personalRewards)
                {
                    int itemType = InfiniteHellWaveIndex > 0 && InfiniteHellWaveIndex % 5 == 0 ? GetRandomInfiniteHellHighQualityRewardTypeID() : 0;
                    if (InfiniteHellWaveIndex > 0 && InfiniteHellWaveIndex % 5 == 0 && itemType <= 0) itemType = 1253;
                    DeliverCoopArenaReward("waveReward", cashThisWave, 0, itemType, 0);
                    cashThisWave = 0; // 原数额已冻结到各个人资产，不能再给 host 铺共享份额或记账户。
                }
                InfiniteHellWaveCashThisWave = 0L;
                // 实际交出去之前都算欠账：落地失败的余额记账户，账户也记不进去就滚进下一波，不再凭空丢。
                long cashRemaining = cashThisWave;

                // 在路牌位置掉落现金：每堆不超过官方堆叠上限（超出部分以前被 StackCount 截掉），
                // 三等分的余数也不再丢弃；堆数有上限，放不下的余额直接记账户。
                try
                {
                    if (cashThisWave > 0)
                    {

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

                        Item cashPrefab = null;
                        try { cashPrefab = ItemAssetsCollection.GetPrefab(EconomyManager.CashItemID); }
                        catch (Exception e) { owner.LogLootWarningLimitedForArena("InfiniteHellCash_prefab", "读取现金堆叠上限失败", e); }
                        long stackLimit = cashPrefab != null && cashPrefab.MaxStackCount > 0 ? cashPrefab.MaxStackCount : int.MaxValue;
                        long neededPiles = cashThisWave / stackLimit + (cashThisWave % stackLimit > 0 ? 1L : 0L);
                        int stackCount = (int)Math.Min(InfiniteHellMaxCashPiles, Math.Max(cashThisWave >= 3L ? 3L : 1L, neededPiles));
                        long perStack = Math.Min(stackLimit, cashThisWave / stackCount + (cashThisWave % stackCount > 0 ? 1L : 0L));
                        for (int i = 0; i < stackCount && cashRemaining > 0; i++)
                        {
                            long stackValue = Math.Min(cashRemaining, perStack);

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

                            bool dropped = false;
                            try
                            {
                                try
                                {
                                    cashItem.StackCount = (int)Math.Min(stackValue, (long)int.MaxValue);
                                }
                                catch (Exception e)
                                {
                                    owner.LogLootWarningLimitedForArena("InfiniteHellCash_stack", "设置无间炼狱现金堆叠数失败", e);
                                }
                                // 以官方实际接受的堆叠数入账：被截断的部分留在余额里，不会凭空消失。
                                long actual = Math.Max(0, Math.Min(stackValue, (long)cashItem.StackCount));
                                if (actual <= 0) break;

                                Vector3 offset = new Vector3(i % 3 - 1, 0f, i / 3) * 0.3f;
                                Vector3 dropPos = basePos + offset;
                                cashItem.Drop(dropPos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                                dropped = true;
                                cashRemaining -= actual;
                            }
                            catch (Exception e)
                            {
                                owner.LogLootWarningLimitedForArena("InfiniteHellCash_drop", "掉落无间炼狱现金失败", e);
                            }
                            finally
                            {
                                if (!dropped)
                                {
                                    try { cashItem.DestroyTree(); } catch (Exception) { /* 未交付实例清理失败不阻断 */ }
                                }
                            }
                            if (!dropped) break;
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 无间炼狱单波现金奖励发放失败: " + e.Message);
                }

                // 落地之外的余额：先记账户（与里程碑同一口径），记不进去就并入下一波，绝不清零丢弃。
                if (cashRemaining > 0)
                {
                    long toAccount = cashRemaining;
                    if (!InfiniteHellMilestoneDelivery.TryCreditAccount(ref cashRemaining))
                    {
                        InfiniteHellWaveCashThisWave += cashRemaining;
                        Debug.LogWarning("[BossRush] 无间炼狱现金这一波没发完，余额 " + cashRemaining + " 并入下一波");
                    }
                    if (toAccount > cashRemaining)
                        owner.ShowMessage(L10n.T("这一波的现金太多，地上放不下的 " + (toAccount - cashRemaining) + " 已直接存入账户",
                            "Too much cash for the floor this wave; " + (toAccount - cashRemaining) + " went straight into your account"));
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
                        for (int rewardIndex = 0; rewardIndex < copies; rewardIndex++)
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
                                    reward5.Drop(basePos + new Vector3(rewardIndex * 0.35f, 0, 0), true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
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

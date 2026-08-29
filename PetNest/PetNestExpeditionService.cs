// ============================================================================
// PetNestExpeditionService.cs - 天灾远征（实施计划 步骤 8）
// ============================================================================
// 玩家不在场的异步玩法，也是整个系统**唯一的真死来源**。
//
// 硬约束（tests/PetNestExpeditionSettlementGuard.py 守卫）：
//   - **现实时间计时**：用 DateTime.UtcNow.Ticks，不用 GameClock。
//     GameClock 离线不推进（Update 里 Time.deltaTime*60），关掉游戏远征就冻住；
//     现实时间 ticks 是仓库既有先例（BossRush_WishReward_NextAvailableTicks）。
//   - **回拨钳制**：剩余时间一律 max(0, returnTicks - now)，系统时钟被往前调时
//     不会算出负数倒计时，往后调也只是提前到期（与官方冷却同口径，不做防作弊）。
//   - **死亡率随出发记录固化**：deathRate/successRate 在出发时写进记录。
//     出发前必须明示的那个数字，结算与纪念碑刻的必须是同一个；
//     后续调数值不影响已经出发的远征。
//   - **commit-before-reveal**：结算 = roll -> 结果与 settled 标记先落档 -> 翻牌只回放。
//     因此本文件不 import 任何 View 符号。
//   - 结算幂等：settled 的记录再调 Settle 直接返回，不重复 roll、不重复发奖。
//   - 派出期间崽锁定：state=OnExpedition，不可出战、不可移除。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>一个远征目的地的静态定义。</summary>
    internal sealed class PetNestDestinationInfo
    {
        internal string Id;
        internal ElementTypes Element;
    }

    /// <summary>天灾远征服务。数据层，无演出与 UI 依赖。</summary>
    internal static class PetNestExpeditionService
    {
        #region 目的地表

        /// <summary>三个天灾目的地。首版是纯结算模拟：不加载场景、不改天气。</summary>
        internal static readonly PetNestDestinationInfo[] Destinations =
        {
            new PetNestDestinationInfo { Id = PetNestTuning.DestinationStormSea, Element = ElementTypes.electricity },
            new PetNestDestinationInfo { Id = PetNestTuning.DestinationAcidRuins, Element = ElementTypes.poison },
            new PetNestDestinationInfo { Id = PetNestTuning.DestinationFrozenWaste, Element = ElementTypes.ice },
        };

        /// <summary>按 id 查目的地。查不到返回 null（调用方 fail-closed）。</summary>
        internal static PetNestDestinationInfo TryGetDestination(string destinationId)
        {
            if (string.IsNullOrEmpty(destinationId)) return null;
            for (int i = 0; i < Destinations.Length; i++)
            {
                if (string.Equals(Destinations[i].Id, destinationId, StringComparison.Ordinal))
                {
                    return Destinations[i];
                }
            }
            return null;
        }

        #endregion

        #region 档位口径（出发前明示的数据来源）

        /// <summary>该档位的时长（小时，现实时间）。</summary>
        internal static double GetDurationHours(PetNestRiskTier tier)
        {
            switch (tier)
            {
                case PetNestRiskTier.Rough: return PetNestTuning.ExpeditionHoursRough;
                case PetNestRiskTier.Desperate: return PetNestTuning.ExpeditionHoursDesperate;
                default: return PetNestTuning.ExpeditionHoursSafe;
            }
        }

        /// <summary>
        /// 该档位的死亡率。**出发前必须明示**：赌的知情权是底线。
        /// </summary>
        internal static float GetDeathRate(PetNestRiskTier tier)
        {
            switch (tier)
            {
                case PetNestRiskTier.Rough: return PetNestTuning.DeathRateRough;
                case PetNestRiskTier.Desperate: return PetNestTuning.DeathRateDesperate;
                default: return PetNestTuning.DeathRateSafe;
            }
        }

        /// <summary>该档位的负伤概率（未阵亡时）。</summary>
        internal static float GetInjuryRate(PetNestRiskTier tier)
        {
            switch (tier)
            {
                case PetNestRiskTier.Rough: return PetNestTuning.InjuryRateRough;
                case PetNestRiskTier.Desperate: return PetNestTuning.InjuryRateDesperate;
                default: return 0f;
            }
        }

        private static float GetBaseSuccessRate(PetNestRiskTier tier)
        {
            switch (tier)
            {
                case PetNestRiskTier.Rough: return PetNestTuning.SuccessRateRough;
                case PetNestRiskTier.Desperate: return PetNestTuning.SuccessRateDesperate;
                default: return PetNestTuning.SuccessRateSafe;
            }
        }

        /// <summary>
        /// 成功率 = 档位基础 + 元素亲和加成（血脉元素与目的地匹配时）。
        /// 收集不同血脉从此有功能意义，不只是图鉴数字。
        /// </summary>
        internal static float ComputeSuccessRate(PetNestPetRecord pet, string destinationId, PetNestRiskTier tier)
        {
            float rate = GetBaseSuccessRate(tier);
            if (HasElementAffinity(pet, destinationId))
            {
                rate += PetNestTuning.ElementAffinityBonus;
            }
            return Mathf.Clamp01(rate);
        }

        /// <summary>
        /// 血脉元素是否与目的地匹配。
        /// 目的地→元素只认 PetNestLineageCatalog.GetDestinationElement 这一份表，
        /// 不在本文件另建第二份——两处一旦不同步，元素亲和会静默偏差。
        /// </summary>
        internal static bool HasElementAffinity(PetNestPetRecord pet, string destinationId)
        {
            if (pet == null) return false;
            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) || lineage == null) return false;
            if (TryGetDestination(destinationId) == null) return false;
            return lineage.Element == PetNestLineageCatalog.GetDestinationElement(destinationId);
        }

        #endregion

        #region 出发

        /// <summary>进行中的远征列表（含已结算未翻牌）。</summary>
        internal static List<PetNestExpeditionRecord> Records
        {
            get
            {
                PetNestExpeditionData data = PetNestPersistenceAccess.Expedition;
                return data.records;
            }
        }

        /// <summary>
        /// 记录上的崽显示名。优先查活着的 PetRecord（改名会即时反映），
        /// 崽已阵亡被移除时回落出发时固化的名字，最后才回落内部 id。
        /// </summary>
        internal static string DescribePetName(PetNestExpeditionRecord record)
        {
            if (record == null) return string.Empty;
            PetNestPetRecord pet = PetNestService.TryGetPet(record.petId);
            if (pet != null) return PetNestService.GetPetDisplayName(pet);
            if (!string.IsNullOrEmpty(record.petDisplayName)) return record.petDisplayName;
            return record.petId;
        }


        /// <summary>
        /// 派出一只崽。成功后崽被锁定（state=OnExpedition），记录立刻落档。
        /// deathRate / successRate 在这里固化，之后调数值不影响这一次。
        /// </summary>
        internal static bool TryDepart(
            string petId, string destinationId, PetNestRiskTier tier,
            out PetNestExpeditionRecord record, out string failureReasonId)
        {
            record = null;
            failureReasonId = null;

            PetNestPetRecord pet = PetNestService.TryGetPet(petId);
            if (pet == null)
            {
                failureReasonId = "pet_not_found";
                return false;
            }
            if (pet.state == (int)PetNestPetState.OnExpedition)
            {
                failureReasonId = "pet_locked_by_expedition";
                return false;
            }
            if (TryGetDestination(destinationId) == null)
            {
                failureReasonId = "destination_unknown";
                return false;
            }

            try
            {
                PetNestExpeditionData data = PetNestPersistenceAccess.Expedition;
                data.idSerial++;

                long now = DateTime.UtcNow.Ticks;
                PetNestExpeditionRecord r = new PetNestExpeditionRecord();
                r.id = "exp_" + data.idSerial.ToString();
                r.petId = pet.id;
                r.petDisplayName = PetNestService.GetPetDisplayName(pet);
                r.destinationId = destinationId;
                r.riskTier = (int)tier;
                r.departTicks = now;
                r.returnTicks = now + (long)(GetDurationHours(tier) * TimeSpan.TicksPerHour);
                // 出发时固化：明示给玩家的、结算用的、纪念碑刻的，必须是同一个数字
                r.deathRate = GetDeathRate(tier);
                r.successRate = ComputeSuccessRate(pet, destinationId, tier);
                r.settled = false;
                r.revealed = false;
                r.outcomeLootTypeIds = new List<int>();
                r.outcomeLootCounts = new List<int>();
                r.Normalize();

                data.records.Add(r);

                // 崽锁定：不可出战、不可移除、不可陈列
                int previousState = pet.state;
                string previousLock = pet.lockedByExpeditionId;
                string previousDeployedId = PetNestService.Nest.deployedPetId;

                pet.state = (int)PetNestPetState.OnExpedition;
                pet.lockedByExpeditionId = r.id;
                if (string.Equals(PetNestService.Nest.deployedPetId, pet.id, StringComparison.Ordinal))
                {
                    PetNestService.Nest.deployedPetId = null;
                }

                if (!CommitBoth(out failureReasonId))
                {
                    // 落档失败：CommitBoth 保证一个字节都没入队，因此内存整体回滚即可
                    // （包括出战席位——不能让一次失败的派出顺手清空玩家的席位）
                    data.records.Remove(r);
                    data.idSerial--;
                    pet.state = previousState;
                    pet.lockedByExpeditionId = previousLock;
                    PetNestService.Nest.deployedPetId = previousDeployedId;
                    return false;
                }

                PetNestMuseumStats.RecordExpedition(pet);
                record = r;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "depart_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 远征出发失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 倒计时

        /// <summary>
        /// 剩余 ticks。**回拨钳制**：一律 max(0, returnTicks - now)。
        /// </summary>
        internal static long GetRemainingTicks(PetNestExpeditionRecord record)
        {
            if (record == null) return 0L;
            long remaining = record.returnTicks - DateTime.UtcNow.Ticks;
            return remaining > 0L ? remaining : 0L;
        }

        /// <summary>是否已到期可结算。</summary>
        internal static bool IsDue(PetNestExpeditionRecord record)
        {
            return record != null && !record.settled && GetRemainingTicks(record) <= 0L;
        }

        #endregion

        #region 结算（commit-before-reveal）

        /// <summary>
        /// 扫描并结算所有到期远征。返回本次新结算的条数。
        /// 幂等：settled 的记录直接跳过，不重复 roll、不重复发奖。
        /// </summary>
        internal static int SettleDueExpeditions()
        {
            int settled = 0;
            try
            {
                List<PetNestExpeditionRecord> records = Records;
                for (int i = 0; i < records.Count; i++)
                {
                    PetNestExpeditionRecord r = records[i];
                    if (!IsDue(r)) continue;
                    string reason;
                    if (TrySettle(r, out reason)) settled++;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征结算扫描失败: " + e.Message);
            }
            return settled;
        }

        /// <summary>
        /// 结算一次远征：roll -> **结果与 settled 标记先落档** -> 之后才允许翻牌回放。
        /// 幂等：已 settled 直接返回 true。
        /// </summary>
        internal static bool TrySettle(PetNestExpeditionRecord record, out string failureReasonId)
        {
            failureReasonId = null;
            if (record == null)
            {
                failureReasonId = "record_missing";
                return false;
            }
            if (record.settled) return true;
            if (!IsDue(record))
            {
                failureReasonId = "not_due";
                return false;
            }

            // **前置检查**：三个 key 里但凡有一个写不了就一步都不走。
            // roll 之后再发现写不下去，内存已经被改成"已结算"，回滚代价极大
            // （真死路径还删了 PetRecord、刻了碑）。
            if (!PetNestPersistenceAccess.CanStoreAll)
            {
                failureReasonId = PetNestPersistenceAccess.HasAnyWriteBarrier
                    ? "save_write_barrier"
                    : "save_store_faulted";
                return false;
            }

            try
            {
                PetNestPetRecord pet = PetNestService.TryGetPet(record.petId);

                // —— roll ——（用出发时固化的概率，不重新读常量）
                bool dead = pet != null && UnityEngine.Random.value < record.deathRate;
                bool injured = false;
                bool success = false;
                if (!dead)
                {
                    success = UnityEngine.Random.value < record.successRate;
                    injured = UnityEngine.Random.value
                        < GetInjuryRate((PetNestRiskTier)record.riskTier);
                }

                record.outcomeDead = dead;
                record.outcomeInjured = injured;
                record.outcomeCash = success ? RollCashReward((PetNestRiskTier)record.riskTier) : 0L;
                record.outcomeLootTypeIds = new List<int>();
                record.outcomeLootCounts = new List<int>();
                if (success)
                {
                    RollLoot(record, pet);
                }
                record.settled = true;

                // —— 崽的去向 ——
                if (pet != null)
                {
                    pet.lockedByExpeditionId = null;
                    pet.expeditionCount++;
                    pet.careerCount++;
                    // 图鉴的"最高等级"以履历推进点为采样时机
                    PetNestMuseumStats.RecordLevel(pet);

                    if (dead)
                    {
                        // 真死不可逆：移除 PetRecord，纪念碑刻档
                        AppendMemorial(record, pet);
                        PetNestService.Nest.pets.Remove(pet);
                        if (string.Equals(PetNestService.Nest.deployedPetId, pet.id, StringComparison.Ordinal))
                        {
                            PetNestService.Nest.deployedPetId = null;
                        }
                    }
                    else
                    {
                        pet.state = (int)PetNestPetState.InNest;
                        // 活着回来给经验。AddExp 只改内存，落档并进下面的 CommitBoth，
                        // 不破坏这条链「结果与 settled 一次原子写入」的语义。
                        PetNestProgressionService.AddExp(pet, PetNestTuning.PetExpExpeditionSurvive);
                        if (injured)
                        {
                            PetNestDownedHandler.AppendScar(pet, record.destinationId, DescribeDisaster(record));
                        }
                    }
                }

                // commit-before-reveal：结果与 settled 标记必须先落档，翻牌才只是回放
                if (!CommitBoth(out failureReasonId))
                {
                    return false;
                }

                // 发奖走独立的可恢复通道：落档成功而发奖失败（或中途崩溃）时，
                // rewardsGranted 仍为 false，下次回基地按条目续发欠账（现金与战利品
                // 各自记账，已发的不重发），连续 MaxRewardGrantAttempts 次仍发不全才放弃。
                TryGrantPendingRewards();
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "settle_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 远征结算失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 补发所有"已结算但未发奖"的远征奖励。幂等：全部发完才置 rewardsGranted 并落档。
        ///
        /// 独立于 TrySettle 的原因：落档成功而发奖失败（EconomyManager.Add 返回 false、
        /// 物品实例化失败、发奖途中崩溃）时，settled 已经是 true，TrySettle 再也不会
        /// 重入；只有第二个标记才能让补发既幂等又可恢复。
        ///
        /// **按条目记账**：现金走 cashGranted，战利品走 grantedLootUnits 游标，
        /// 补发只重做真正失败的那一格 —— 已到账的现金绝不会因为一件物品失败而重发。
        /// **有上限**：连续 MaxRewardGrantAttempts 次仍发不全就放弃并置 rewardsGranted，
        /// 否则一件永远发不出去的战利品会让 MarkRevealed 永远返回 rewards_pending，
        /// 把这张翻牌（以及这条记录）永久卡在待翻列表里。
        /// </summary>
        internal static int TryGrantPendingRewards()
        {
            int granted = 0;
            try
            {
                List<PetNestExpeditionRecord> records = Records;
                bool changed = false;
                for (int i = 0; i < records.Count; i++)
                {
                    PetNestExpeditionRecord r = records[i];
                    if (r == null || !r.settled || r.rewardsGranted) continue;

                    bool complete = GrantRewards(r);
                    if (!complete)
                    {
                        r.rewardGrantAttempts++;
                        if (r.rewardGrantAttempts >= PetNestTuning.MaxRewardGrantAttempts)
                        {
                            // 放弃：宁可少发一次，也不能让翻牌与记录永久卡死
                            complete = true;
                            ModBehaviour.DevLog("[PetNest] [ERROR] 远征奖励连续 "
                                + r.rewardGrantAttempts + " 次未发全，放弃补发: " + r.id
                                + "（现金已发=" + r.cashGranted
                                + "，已投件数=" + r.grantedLootUnits + "）");
                        }
                    }
                    if (complete)
                    {
                        r.rewardsGranted = true;
                        granted++;
                    }
                    changed = true;
                }
                if (changed)
                {
                    string ignored;
                    CommitBoth(out ignored);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征奖励补发失败: " + e.Message);
            }
            return granted;
        }

        /// <summary>
        /// 孤儿远征锁自愈（回基地扫描）。
        ///
        /// 成因：物理 flush 是逐 key 独立失败的（PetNestSaveCoordinator.FlushBatch），
        /// Nest key 写成功而 Expedition key 异常进 StoreFaulted 时，官方任一次 SaveFile
        /// 会落盘「崽=OnExpedition」而远征记录缺失。下一会话那只崽 state=OnExpedition
        /// 却没有匹配记录：不可出战、不可放生、不可移除、永不结算，**永久锁死**。
        ///
        /// 只在确认无匹配时复位，绝不误解锁正在进行的合法远征：
        ///   - 三个 key 有任一写屏障/故障时一步都不走 —— 那时 records 为空很可能只是
        ///     没读回来，误判会把在途远征全部解锁，而且改动也根本落不了盘；
        ///   - 匹配放宽到「id 命中」或「同一只崽的未结算记录」两条，宁可漏修不可误修；
        ///   - 非 OnExpedition 的崽只清残留的 lockedByExpeditionId，不动它的席位状态。
        /// </summary>
        internal static int ReconcileOrphanedExpeditionLocks()
        {
            int repaired = 0;
            try
            {
                if (!PetNestPersistenceAccess.CanStoreAll) return 0;

                List<PetNestExpeditionRecord> records = Records;
                List<PetNestPetRecord> pets = PetNestService.Nest.pets;
                if (pets == null) return 0;

                for (int i = 0; i < pets.Count; i++)
                {
                    PetNestPetRecord pet = pets[i];
                    if (pet == null) continue;

                    bool claimsExpedition = pet.state == (int)PetNestPetState.OnExpedition
                        || !string.IsNullOrEmpty(pet.lockedByExpeditionId);
                    if (!claimsExpedition) continue;
                    if (HasMatchingExpedition(records, pet)) continue;

                    ModBehaviour.DevLog("[PetNest] [WARNING] 远征记录缺失，复位孤儿锁: pet="
                        + pet.id + ", state=" + pet.state
                        + ", lockedBy=" + (pet.lockedByExpeditionId ?? "<null>"));

                    if (pet.state == (int)PetNestPetState.OnExpedition)
                    {
                        pet.state = string.Equals(
                            pet.id, PetNestService.Nest.deployedPetId, StringComparison.Ordinal)
                            ? (int)PetNestPetState.Deployed
                            : (int)PetNestPetState.InNest;
                    }
                    pet.lockedByExpeditionId = null;
                    repaired++;
                }

                if (repaired > 0)
                {
                    string ignored;
                    CommitBoth(out ignored);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 孤儿远征锁自愈失败: " + e.Message);
            }
            return repaired;
        }

        /// <summary>
        /// 远征表里是否还有这只崽对应的记录。匹配故意放宽（id 命中或同崽未结算记录），
        /// 漏修只是继续锁着，误修则会把在途远征凭空解锁。
        /// </summary>
        private static bool HasMatchingExpedition(
            List<PetNestExpeditionRecord> records, PetNestPetRecord pet)
        {
            if (records == null || pet == null) return false;
            for (int i = 0; i < records.Count; i++)
            {
                PetNestExpeditionRecord r = records[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(pet.lockedByExpeditionId)
                    && string.Equals(r.id, pet.lockedByExpeditionId, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!r.settled && string.Equals(r.petId, pet.id, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 标记为已翻牌并移除记录。演出层放完动画后调，**不做任何 roll**。
        /// </summary>
        internal static bool MarkRevealed(PetNestExpeditionRecord record, out string failureReasonId)
        {
            failureReasonId = null;
            if (record == null)
            {
                failureReasonId = "record_missing";
                return false;
            }
            if (!record.settled)
            {
                failureReasonId = "not_settled";
                return false;
            }
            if (!record.rewardsGranted)
            {
                // 奖还没发就把记录移出待翻列表 = 奖励永久丢失。先补发再翻。
                TryGrantPendingRewards();
                if (!record.rewardsGranted)
                {
                    failureReasonId = "rewards_pending";
                    return false;
                }
            }

            // 先改内存再提交，失败必须整体回滚：否则记录已从待翻列表消失但盘上还在，
            // 重启后这张牌会再弹一次（奖励有 rewardsGranted 保护不会重发，但体验错乱）。
            // 回滚形态与 TryDepart 一致。
            List<PetNestExpeditionRecord> records = PetNestPersistenceAccess.Expedition.records;
            int previousIndex = records != null ? records.IndexOf(record) : -1;

            record.revealed = true;
            if (records != null) records.Remove(record);

            if (!CommitBoth(out failureReasonId))
            {
                record.revealed = false;
                if (records != null)
                {
                    if (previousIndex >= 0 && previousIndex <= records.Count)
                    {
                        records.Insert(previousIndex, record);
                    }
                    else
                    {
                        records.Add(record);
                    }
                }
                return false;
            }
            return true;
        }

        /// <summary>已结算但还没翻牌的记录（回基地时弹翻牌用）。</summary>
        internal static List<PetNestExpeditionRecord> GetPendingReveals()
        {
            List<PetNestExpeditionRecord> pending = new List<PetNestExpeditionRecord>();
            List<PetNestExpeditionRecord> records = Records;
            for (int i = 0; i < records.Count; i++)
            {
                PetNestExpeditionRecord r = records[i];
                if (r != null && r.settled && !r.revealed) pending.Add(r);
            }
            return pending;
        }

        #endregion

        #region 产出

        private static long RollCashReward(PetNestRiskTier tier)
        {
            int min;
            int max;
            switch (tier)
            {
                case PetNestRiskTier.Rough: min = 1200; max = 3600; break;
                case PetNestRiskTier.Desperate: min = 4000; max = 12000; break;
                default: min = 400; max = 1200; break;
            }
            return UnityEngine.Random.Range(min, max + 1);
        }

        /// <summary>
        /// 首版产出：现金 + 同血脉遗魂，亡命档小概率带回一枚遗种蛋。
        ///
        /// 材料 / 装备件的产出表需要经 owner 审定的官方物品 ID 表，首版不猜 ID；
        /// DTO 的 outcomeLootTypeIds 已经预留，补表时只填这里，不动结构。
        /// </summary>
        private static void RollLoot(PetNestExpeditionRecord record, PetNestPetRecord pet)
        {
            if (pet == null) return;

            int souls = record.riskTier == (int)PetNestRiskTier.Desperate ? 90
                : record.riskTier == (int)PetNestRiskTier.Rough ? 40 : 15;
            PetNestService.AddSouls(pet.lineageKey, souls, false);

            if (record.riskTier == (int)PetNestRiskTier.Desperate
                && UnityEngine.Random.value < 0.2f)
            {
                record.outcomeLootTypeIds.Add(RelicEggConfig.TYPE_ID);
                record.outcomeLootCounts.Add(1);
            }
        }

        /// <summary>
        /// 发奖。**必须在结果落档之后调**：先落档再发奖，中途崩溃最多少发一次奖，
        /// 不会出现"发了奖但记录还没 settled"的重复领取窗口。
        ///
        /// **按条目记账**：现金与战利品各有自己的账（cashGranted / grantedLootUnits），
        /// 每成功一格就立刻记账。返回 false 表示还有欠账，由 TryGrantPendingRewards
        /// 下次续发；已记账的格子不会重做，因此补发不可能造成重复发放。
        /// 记账只改内存，落档由调用方在扫完一轮后统一 CommitBoth。
        /// </summary>
        private static bool GrantRewards(PetNestExpeditionRecord record)
        {
            if (record == null) return true;
            bool complete = true;

            if (!record.cashGranted)
            {
                if (record.outcomeCash <= 0L)
                {
                    // 本来就没有现金产出：这一格没有欠账
                    record.cashGranted = true;
                }
                else
                {
                    bool ok = false;
                    try
                    {
                        // 官方 Add 在 Instance==null 时返回 false 而**不抛异常**，
                        // 丢弃返回值等于把"钱没发出去"当成发过了
                        ok = EconomyManager.Add(record.outcomeCash);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PetNest] 远征现金发放异常: " + e.Message);
                    }
                    if (ok) record.cashGranted = true;
                    else complete = false;
                }
            }

            try
            {
                int unit = 0;
                for (int i = 0; i < record.outcomeLootTypeIds.Count && complete; i++)
                {
                    int typeId = record.outcomeLootTypeIds[i];
                    int count = i < record.outcomeLootCounts.Count ? record.outcomeLootCounts[i] : 1;
                    for (int k = 0; k < count; k++)
                    {
                        // 游标之前的件数上一轮已经投出去了，绝不重投
                        if (unit < record.grantedLootUnits)
                        {
                            unit++;
                            continue;
                        }
                        if (!GrantOneItem(typeId, record))
                        {
                            complete = false;
                            break;
                        }
                        unit++;
                        record.grantedLootUnits = unit;
                    }
                }
            }
            catch (Exception e)
            {
                complete = false;
                ModBehaviour.DevLog("[PetNest] 远征战利品发放失败: " + e.Message);
            }

            return complete;
        }

        /// <summary>
        /// 投递一件战利品。返回 true 表示这一件已经了结（成功送达，或确定性废件不必再试）；
        /// 返回 false 表示可重试的失败，调用方保留欠账下次再发。
        /// </summary>
        private static bool GrantOneItem(int typeId, PetNestExpeditionRecord record)
        {
            Item item = null;
            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(typeId);
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    // 可重试：注册表/资源可能只是这一刻还没就绪
                    ModBehaviour.DevLog("[PetNest] 远征战利品实例化失败，保留欠账待补发: " + typeId);
                    return false;
                }

                if (typeId == RelicEggConfig.TYPE_ID)
                {
                    PetNestPetRecord pet = PetNestService.TryGetPet(record.petId);
                    string lineageKey = pet != null ? pet.lineageKey : null;
                    if (string.IsNullOrEmpty(lineageKey) || !RelicEggConfig.TryStampLineage(item, lineageKey))
                    {
                        // 血脉写不进去的蛋是废蛋，不如不发。这是**确定性**结果
                        //（崽已阵亡时血脉永远查不回来），重试无意义，记为已了结，
                        // 否则这条记录会永远发不全、把翻牌卡在 rewards_pending
                        ModBehaviour.DevLog("[PetNest] 远征遗种蛋无血脉可写，跳过该件: " + record.id);
                        return true;
                    }
                }

                ItemUtilities.SendToPlayer(item);
                item = null;
                return true;
            }
            catch (Exception e)
            {
                // 抛异常一律按"未送达"处理：宁可下次重发一件，也不当成已发把欠账抹掉
                ModBehaviour.DevLog("[PetNest] 远征物品发放失败: " + e.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (item != null) item.DestroyTree();
                }
                catch (Exception)
                {
                    // 回收失败只丢引用
                }
            }
        }

        #endregion

        #region 纪念碑

        /// <summary>
        /// 刻碑。**风险档位一定要刻**——"亡命"两个字是玩家自己选的，
        /// 碑文替系统记住这一点。同时刻上出发时固化的死亡率。
        /// </summary>
        private static void AppendMemorial(PetNestExpeditionRecord record, PetNestPetRecord pet)
        {
            try
            {
                PetNestMuseumData museum = PetNestPersistenceAccess.Museum;
                PetNestMemorialEntry entry = new PetNestMemorialEntry();
                entry.displayName = PetNestService.GetPetDisplayName(pet);
                entry.lineageKey = pet.lineageKey;
                entry.destinationId = record.destinationId;
                entry.riskTier = record.riskTier;
                entry.deathRate = record.deathRate;
                entry.deathTicks = DateTime.UtcNow.Ticks;
                entry.careerCount = pet.careerCount;
                entry.shiny = pet.shiny;
                museum.memorials.Add(entry);

                while (museum.memorials.Count > PetNestTuning.MaxMemorialEntries)
                {
                    museum.memorials.RemoveAt(0);
                    museum.mergedMemorialCount++;
                }

                PetNestMuseumStats.NotifyMemorialChanged();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 纪念碑刻档失败: " + e.Message);
            }
        }

        private static string DescribeDisaster(PetNestExpeditionRecord record)
        {
            PetNestDestinationInfo destination = TryGetDestination(record.destinationId);
            return destination != null ? destination.Id : "expedition";
        }

        #endregion

        #region 落档

        /// <summary>
        /// 巢 / 远征 / 博物馆三档**原子**提交：一次结算会同时改崽状态、远征记录和纪念碑，
        /// 分开提交会出现"崽没了但碑没刻"这类半截状态。
        ///
        /// 原子性靠两件事保证：
        /// 1) **先把三个 key 都 CanStore 过一遍再开始 Store**。逐个 Store 到一半失败时，
        ///    先成功的那个 key 会把 pending 留下来，被官方 OnCollectSaveData 独立落盘，
        ///    而调用方以为整个操作失败并回滚了内存——两边就此永久分叉
        ///    （崽被锁死在 OnExpedition、奖励发不出、遗魂凭空消失都是这一个根因）。
        /// 2) 万一 Store 仍在中途失败（并发改状态），把三个 key 的 pending 一并丢弃。
        ///
        /// 成败以入队为准，不以 flush 为准：pending 一旦入队，官方采集与后续 flush
        /// 都会把它写下去，此时因 flush 报错而让调用方回滚内存反而制造分叉。
        /// </summary>
        private static bool CommitBoth(out string failureReasonId)
        {
            failureReasonId = null;

            // 前置检查：任一 key 写不了就一个字节都不写
            if (!PetNestPersistenceAccess.CanStoreAll)
            {
                failureReasonId = PetNestPersistenceAccess.HasAnyWriteBarrier
                    ? "save_write_barrier"
                    : "save_store_faulted";
                return false;
            }

            bool staged = PetNestService.StageCommit()
                && PetNestPersistenceAccess.StageExpedition()
                && PetNestPersistenceAccess.StageMuseum();

            if (!staged)
            {
                // 中途失败：把已入队的部分一并撤掉，绝不允许半份落盘
                PetNestPersistenceAccess.DiscardAllPending();
                failureReasonId = "save_store_faulted";
                return false;
            }

            // best-effort：入队即视为已提交，落盘失败由协调器重试
            PetNestSaveCoordinator.RequestFlush();
            return true;
        }

        #endregion
    }
}

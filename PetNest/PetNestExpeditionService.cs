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

        /// <summary>血脉元素是否与目的地匹配。</summary>
        internal static bool HasElementAffinity(PetNestPetRecord pet, string destinationId)
        {
            if (pet == null) return false;
            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) || lineage == null) return false;
            PetNestDestinationInfo destination = TryGetDestination(destinationId);
            if (destination == null) return false;
            return lineage.Element == destination.Element;
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

        /// <summary>该崽是否正在远征途中。</summary>
        internal static PetNestExpeditionRecord FindActiveByPet(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            List<PetNestExpeditionRecord> records = Records;
            for (int i = 0; i < records.Count; i++)
            {
                PetNestExpeditionRecord r = records[i];
                if (r != null && !r.revealed
                    && string.Equals(r.petId, petId, StringComparison.Ordinal))
                {
                    return r;
                }
            }
            return null;
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
                pet.state = (int)PetNestPetState.OnExpedition;
                pet.lockedByExpeditionId = r.id;
                if (string.Equals(PetNestService.Nest.deployedPetId, pet.id, StringComparison.Ordinal))
                {
                    PetNestService.Nest.deployedPetId = null;
                }

                if (!CommitBoth(out failureReasonId))
                {
                    // 落档失败：回滚内存状态，不能让玩家以为派出去了
                    data.records.Remove(r);
                    pet.state = (int)PetNestPetState.InNest;
                    pet.lockedByExpeditionId = null;
                    return false;
                }

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

                GrantRewards(record);
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

            record.revealed = true;
            PetNestPersistenceAccess.Expedition.records.Remove(record);
            return CommitBoth(out failureReasonId);
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
        /// </summary>
        private static void GrantRewards(PetNestExpeditionRecord record)
        {
            if (record == null) return;
            try
            {
                if (record.outcomeCash > 0L)
                {
                    EconomyManager.Add(record.outcomeCash);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征现金发放失败: " + e.Message);
            }

            try
            {
                for (int i = 0; i < record.outcomeLootTypeIds.Count; i++)
                {
                    int typeId = record.outcomeLootTypeIds[i];
                    int count = i < record.outcomeLootCounts.Count ? record.outcomeLootCounts[i] : 1;
                    for (int k = 0; k < count; k++)
                    {
                        GrantOneItem(typeId, record);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征战利品发放失败: " + e.Message);
            }
        }

        private static void GrantOneItem(int typeId, PetNestExpeditionRecord record)
        {
            Item item = null;
            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(typeId);
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null) return;

                if (typeId == RelicEggConfig.TYPE_ID)
                {
                    PetNestPetRecord pet = PetNestService.TryGetPet(record.petId);
                    string lineageKey = pet != null ? pet.lineageKey : null;
                    if (string.IsNullOrEmpty(lineageKey) || !RelicEggConfig.TryStampLineage(item, lineageKey))
                    {
                        // 血脉写不进去的蛋是废蛋，不如不发
                        return;
                    }
                }

                ItemUtilities.SendToPlayer(item);
                item = null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征物品发放失败: " + e.Message);
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
        /// 巢 / 远征 / 博物馆三档一起提交：一次结算会同时改崽状态、远征记录和纪念碑，
        /// 分开提交会出现"崽没了但碑没刻"这类半截状态。
        /// </summary>
        private static bool CommitBoth(out string failureReasonId)
        {
            failureReasonId = null;
            if (!PetNestService.StageCommit())
            {
                failureReasonId = "save_write_barrier";
                return false;
            }
            if (!PetNestPersistenceAccess.StageExpedition())
            {
                failureReasonId = "save_write_barrier";
                return false;
            }
            if (!PetNestPersistenceAccess.StageMuseum())
            {
                failureReasonId = "save_write_barrier";
                return false;
            }

            string flushError;
            if (!PetNestSaveCoordinator.RequestFlush(out flushError))
            {
                if (!string.Equals(flushError, "flush_deferred_is_saving", StringComparison.Ordinal))
                {
                    failureReasonId = flushError;
                    return false;
                }
            }
            return true;
        }

        #endregion
    }
}

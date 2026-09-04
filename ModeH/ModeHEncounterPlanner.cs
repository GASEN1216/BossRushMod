using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 敌军计划器（设计提案 §17.5、§25.1）。
    ///
    /// 冻结契约：
    /// - 敌军计划先于玩家整备冻结；由
    ///   `runSeed + matchIndex + technicalRetrySequence + planCandidateIndex` 派生；
    /// - 三层剧本：编制骨架 / 进场剧本 / 擂台条件；
    /// - 威胁预算：单体分相加后乘行动数量系数 `1 + 0.20 * (count - 1)`，
    ///   组合协同再加至多 10%，结果必须落在 `[minFillPercent%, 105%]` 走廊内；
    /// - 候选先过全局 `archetypeCapabilityMatrix` 审计（不得同时封死五种原型），
    ///   再过 roster-level veto（存活合同选手中至少一名原型未被硬封锁）；
    /// - 连续 `MaxPlanCandidateAttempts` 个候选都失败才由调用方进入 `TechnicalAbort`；
    /// - 计划生成后赔率只读公开摘要，不得反向改计划；
    /// - 侦察每场至多一次，只揭示一个冻结字段。
    ///
    /// 本类是纯计算：不创建角色、不读 AI 实际表现、不访问玩家资产。
    /// </summary>
    internal static class ModeHEncounterPlanner
    {
        #region 计划生成

        /// <summary>
        /// 逐候选尝试冻结本场敌军计划。全部候选失败时返回 false，
        /// failureReasonId 为最后一次拒绝原因，调用方按 §17.5 进入 `TechnicalAbort`。
        /// </summary>
        /// <param name="enemyStableKeyPool">
        /// 敌军可用 stable key（认证 Passed 的生产池减去本季五席），已由调用方去重。
        /// </param>
        /// <param name="echoReturnStableKey">回场签核心的 stable key；第 5 场必需，其余场次可为空。</param>
        /// <param name="liveArchetypeIds">存活合同选手的公开原型（roster-level veto 输入）。</param>
        public static bool TryBuildPlan(
            long runSeed,
            int matchIndex,
            int technicalRetrySequence,
            IList<string> enemyStableKeyPool,
            string echoReturnStableKey,
            IList<string> liveArchetypeIds,
            out ModeHMatchPlanDto plan,
            out int usedCandidateIndex,
            out string failureReasonId)
        {
            plan = null;
            usedCandidateIndex = -1;
            failureReasonId = null;

            ModeHMatchCorridor corridor = GetCorridor(matchIndex);
            if (corridor == null)
            {
                failureReasonId = "plan_corridor_missing";
                return false;
            }
            if (enemyStableKeyPool == null || enemyStableKeyPool.Count == 0)
            {
                failureReasonId = "plan_enemy_pool_empty";
                return false;
            }
            if (liveArchetypeIds == null || liveArchetypeIds.Count == 0)
            {
                failureReasonId = "plan_no_live_roster";
                return false;
            }

            for (int candidateIndex = 0; candidateIndex < ModeHConfig.MaxPlanCandidateAttempts; candidateIndex++)
            {
                string reason;
                ModeHMatchPlanDto candidate = BuildCandidate(
                    runSeed, matchIndex, technicalRetrySequence, candidateIndex,
                    corridor, enemyStableKeyPool, echoReturnStableKey, liveArchetypeIds, out reason);
                if (candidate == null)
                {
                    failureReasonId = reason;
                    continue;
                }
                plan = candidate;
                usedCandidateIndex = candidateIndex;
                return true;
            }

            if (string.IsNullOrEmpty(failureReasonId)) failureReasonId = "plan_all_candidates_rejected";
            return false;
        }

        /// <summary>构造并审计单个候选；被拒绝时返回 null。</summary>
        private static ModeHMatchPlanDto BuildCandidate(
            long runSeed,
            int matchIndex,
            int technicalRetrySequence,
            int candidateIndex,
            ModeHMatchCorridor corridor,
            IList<string> enemyStableKeyPool,
            string echoReturnStableKey,
            IList<string> liveArchetypeIds,
            out string failureReasonId)
        {
            failureReasonId = null;

            int sequence = (matchIndex * 10000) + (technicalRetrySequence * 100) + candidateIndex;
            ulong derived = ModeHSeedStream.DeriveSeed(runSeed, ModeHSeedStream.Domains.EncounterPlan, sequence);
            ModeHSeedStream stream = ModeHSeedStream.Create(runSeed, ModeHSeedStream.Domains.EncounterPlan, sequence);

            ModeHSkeletonSpec skeleton = PickSkeleton(stream, corridor);
            if (skeleton == null)
            {
                failureReasonId = "plan_skeleton_missing";
                return null;
            }

            // 回场签骨架必须真的拿到回场核心，否则换候选
            string requiredCoreKey = null;
            if (skeleton.RequiresEchoReturn)
            {
                if (string.IsNullOrEmpty(echoReturnStableKey))
                {
                    failureReasonId = "plan_echo_core_missing";
                    return null;
                }
                requiredCoreKey = echoReturnStableKey;
            }

            ModeHEntryScriptSpec entryScript = PickEntryScript(stream, skeleton);
            if (entryScript == null)
            {
                failureReasonId = "plan_entry_script_missing";
                return null;
            }
            ModeHArenaConditionSpec condition = PickArenaCondition(stream);
            if (condition == null)
            {
                failureReasonId = "plan_condition_missing";
                return null;
            }

            int unitCount = stream.NextIntInclusive(skeleton.MinUnits, skeleton.MaxUnits);
            List<string> units;
            if (!TrySelectUnits(stream, enemyStableKeyPool, requiredCoreKey, unitCount, corridor, out units,
                    out failureReasonId))
            {
                return null;
            }

            List<int> batchIndices;
            if (!TryAssignBatches(units, entryScript, corridor.SimultaneousCap, PickCoreIndex(units),
                    out batchIndices, out failureReasonId))
            {
                return null;
            }

            // 全局能力矩阵审计：不得同时封死五种首发原型
            List<string> enemyCapabilities = CollectCapabilityTags(units, condition);
            List<string> lockedArchetypes = CollectLockedArchetypes(enemyCapabilities);
            if (lockedArchetypes.Count >= ModeHStableIds.AllArchetypes.Length)
            {
                failureReasonId = "plan_locks_all_archetypes";
                return null;
            }

            // roster-level veto：只读存活合同选手的公开原型，至少保留一种合法排列
            if (!HasLegalArrangement(liveArchetypeIds, lockedArchetypes))
            {
                failureReasonId = "plan_roster_no_legal_arrangement";
                return null;
            }

            ModeHMatchPlanDto plan = new ModeHMatchPlanDto();
            plan.matchIndex = matchIndex;
            plan.planId = "m" + matchIndex + "_r" + technicalRetrySequence + "_c" + candidateIndex;
            plan.planCandidateIndex = candidateIndex;
            plan.technicalRetrySequence = technicalRetrySequence;
            plan.skeletonId = skeleton.SkeletonId;
            plan.entryScriptId = entryScript.EntryScriptId;
            plan.conditionId = condition.ConditionId;
            plan.enemyStableKeys = units;
            plan.enemyBatchIndices = batchIndices;
            plan.threatBudget = corridor.ThreatBudget;
            plan.planSeed = unchecked((long)derived);
            plan.reconChoiceId = string.Empty;
            plan.reconResult = string.Empty;
            plan.publicSummary = BuildPublicSummary(units, skeleton, entryScript, condition, batchIndices);

            int coreIndex = PickCoreIndex(units);
            ModeHProfileTemplate coreTemplate = coreIndex >= 0
                ? ModeHProfileRegistry.GetByStableKey(units[coreIndex]) : null;
            plan.specialEnemyEligible = skeleton.HasHighThreatCore && coreTemplate != null;
            plan.specialEnemySourceTag = plan.specialEnemyEligible ? coreTemplate.ProfileTemplateId : string.Empty;

            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(plan, "planDigest", out digest, out digestError))
            {
                failureReasonId = "plan_digest_failed:" + digestError;
                return null;
            }
            plan.planDigest = digest;
            return plan;
        }

        #endregion

        #region 三层剧本取值

        /// <summary>
        /// 取该场次的走廊（冻结数据的只读查询）。
        /// 开放为 internal：分批入场要按 corridor.SimultaneousCap 判断何时放行下一批，
        /// 而该上限只存在于走廊里（计划 DTO 不带它）。
        /// </summary>
        internal static ModeHMatchCorridor GetCorridor(int matchIndex)
        {
            List<ModeHMatchCorridor> corridors = ModeHContentCatalog.MatchCorridors;
            if (corridors == null) return null;
            for (int i = 0; i < corridors.Count; i++)
            {
                if (corridors[i] != null && corridors[i].MatchIndex == matchIndex) return corridors[i];
            }
            return null;
        }

        private static ModeHSkeletonSpec PickSkeleton(ModeHSeedStream stream, ModeHMatchCorridor corridor)
        {
            if (corridor.SkeletonIds == null || corridor.SkeletonIds.Count == 0) return null;
            List<ModeHSkeletonSpec> usable = new List<ModeHSkeletonSpec>();
            List<ModeHSkeletonSpec> all = ModeHContentCatalog.Skeletons;
            if (all == null) return null;
            for (int i = 0; i < corridor.SkeletonIds.Count; i++)
            {
                string id = corridor.SkeletonIds[i];
                for (int j = 0; j < all.Count; j++)
                {
                    if (all[j] != null && string.Equals(all[j].SkeletonId, id, StringComparison.Ordinal))
                    {
                        usable.Add(all[j]);
                        break;
                    }
                }
            }
            if (usable.Count == 0) return null;
            return usable[stream.NextInt(usable.Count)];
        }

        /// <summary>
        /// 进场剧本优先取与骨架公开标签相合的一条；没有相合项时在全表内确定性取一条。
        /// </summary>
        private static ModeHEntryScriptSpec PickEntryScript(ModeHSeedStream stream, ModeHSkeletonSpec skeleton)
        {
            List<ModeHEntryScriptSpec> all = ModeHContentCatalog.EntryScripts;
            if (all == null || all.Count == 0) return null;

            List<ModeHEntryScriptSpec> aligned = new List<ModeHEntryScriptSpec>();
            for (int i = 0; i < all.Count; i++)
            {
                ModeHEntryScriptSpec script = all[i];
                if (script == null) continue;
                if (skeleton.HasHighThreatCore && script.CoreEntersLast) aligned.Add(script);
                else if (!skeleton.HasHighThreatCore && !script.CoreEntersLast) aligned.Add(script);
            }
            List<ModeHEntryScriptSpec> source = aligned.Count > 0 ? aligned : all;
            return source[stream.NextInt(source.Count)];
        }

        private static ModeHArenaConditionSpec PickArenaCondition(ModeHSeedStream stream)
        {
            List<ModeHArenaConditionSpec> all = ModeHContentCatalog.ArenaConditions;
            if (all == null || all.Count == 0) return null;
            return all[stream.NextInt(all.Count)];
        }

        #endregion

        #region 威胁预算与单位选取

        /// <summary>
        /// 选出 count 名敌人，使实际威胁落在走廊内。先确定性抽样，再做有界修复交换；
        /// 修复后仍越界则拒绝该候选。
        /// </summary>
        private static bool TrySelectUnits(
            ModeHSeedStream stream,
            IList<string> pool,
            string requiredCoreKey,
            int count,
            ModeHMatchCorridor corridor,
            out List<string> units,
            out string failureReasonId)
        {
            units = null;
            failureReasonId = null;

            List<string> available = new List<string>();
            for (int i = 0; i < pool.Count; i++)
            {
                string key = pool[i];
                if (string.IsNullOrEmpty(key)) continue;
                if (ModeHProfileRegistry.GetByStableKey(key) == null) continue;
                if (!available.Contains(key)) available.Add(key);
            }
            if (!string.IsNullOrEmpty(requiredCoreKey)
                && ModeHProfileRegistry.GetByStableKey(requiredCoreKey) != null
                && !available.Contains(requiredCoreKey))
            {
                available.Add(requiredCoreKey);
            }
            available.Sort(StringComparer.Ordinal);

            if (available.Count < count)
            {
                failureReasonId = "plan_enemy_pool_too_small";
                return false;
            }

            List<string> shuffled = new List<string>(available);
            stream.Shuffle(shuffled);

            List<string> selected = new List<string>();
            if (!string.IsNullOrEmpty(requiredCoreKey) && available.Contains(requiredCoreKey))
            {
                selected.Add(requiredCoreKey);
            }
            for (int i = 0; i < shuffled.Count && selected.Count < count; i++)
            {
                if (!selected.Contains(shuffled[i])) selected.Add(shuffled[i]);
            }
            if (selected.Count != count)
            {
                failureReasonId = "plan_enemy_selection_short";
                return false;
            }

            // 走廊与被比较值必须同量纲。`ThreatPlans.json` 的 threatBudget（100/115/130/
            // 145/165/190）是按**基础威胁和**编制的：12 个生产候选的分值 38..62，
            // n=2 的基础和 78..120、n=3 是 118..175、n=4 是 158..225——预算区间正落在
            // 这条量程上。而 ComputeEffectiveThreat 会再乘一遍行动溢价（每多一个单位
            // +20%），于是 n=3 的有效值变成 165..245、n=4 变成 252..360，直接飞出走廊。
            // 后果最严重的是第 4 场：两套骨架 pack / mixed_range 都固定 3 单位，
            // 任意三件组合的有效值最小 165 > 上界 152，**全部 8 个候选必然被拒**，
            // 赛季必定卡死在第 4 场（第 3 场的 relay_squad 分支同理恒不可行）。
            //
            // 修法是把预算一并按同一份行动溢价放大，等价于用「基础和 × 协同」与原始
            // 走廊比较：协同仍是真实约束（高协同组合照样会被顶出上界），只是不再把
            // 单位数溢价重复计一次。数据表与 ModeHConfig.MatchThreatBudgets 一字不动。
            int budgetPremiumMilli = 1000
                + (int)(ModeHConfig.ThreatActionCountCoefficient * 1000f) * (count - 1);
            long scaledBudget = (long)corridor.ThreatBudget * budgetPremiumMilli / 1000L;
            int lowerBound = (int)(scaledBudget * corridor.MinFillPercent / 100L);
            int upperBound = (int)(scaledBudget
                * (100 + (int)(ModeHConfig.ThreatBudgetTolerance * 100f)) / 100L);

            int maxRepairSteps = available.Count * count + 8;
            for (int step = 0; step <= maxRepairSteps; step++)
            {
                int effective = ComputeEffectiveThreat(selected);
                if (effective >= lowerBound && effective <= upperBound)
                {
                    units = selected;
                    return true;
                }
                bool needSmaller = effective > upperBound;
                if (!TryRepairSwap(selected, available, requiredCoreKey, needSmaller))
                {
                    break;
                }
            }

            failureReasonId = "plan_threat_out_of_corridor";
            return false;
        }

        /// <summary>
        /// 一次修复交换：needSmaller 时把当前最高威胁成员换成未选中的次高（严格更小）成员，
        /// 反之把最低威胁成员换成严格更大的成员。无可换项返回 false。
        /// </summary>
        private static bool TryRepairSwap(
            List<string> selected, IList<string> available, string requiredCoreKey, bool needSmaller)
        {
            int targetIndex = -1;
            int targetScore = needSmaller ? int.MinValue : int.MaxValue;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!string.IsNullOrEmpty(requiredCoreKey)
                    && string.Equals(selected[i], requiredCoreKey, StringComparison.Ordinal))
                {
                    continue; // 回场核心不可被换出
                }
                int score = GetThreatScore(selected[i]);
                if (needSmaller ? score > targetScore : score < targetScore)
                {
                    targetScore = score;
                    targetIndex = i;
                }
            }
            if (targetIndex < 0) return false;

            string best = null;
            int bestScore = needSmaller ? int.MinValue : int.MaxValue;
            for (int i = 0; i < available.Count; i++)
            {
                string key = available[i];
                if (selected.Contains(key)) continue;
                int score = GetThreatScore(key);
                if (needSmaller)
                {
                    if (score >= targetScore) continue;
                    if (score > bestScore) { bestScore = score; best = key; }
                }
                else
                {
                    if (score <= targetScore) continue;
                    if (score < bestScore) { bestScore = score; best = key; }
                }
            }
            if (best == null) return false;
            selected[targetIndex] = best;
            return true;
        }

        /// <summary>
        /// §17.5 预算实现规则：单体分相加，乘行动数量系数，再加协同上浮（整数运算）。
        /// </summary>
        public static int ComputeEffectiveThreat(IList<string> stableKeys)
        {
            if (stableKeys == null || stableKeys.Count == 0) return 0;
            int baseThreat = 0;
            for (int i = 0; i < stableKeys.Count; i++)
            {
                baseThreat += GetThreatScore(stableKeys[i]);
            }
            int actionPremiumMilli = 1000
                + (int)(ModeHConfig.ThreatActionCountCoefficient * 1000f) * (stableKeys.Count - 1);
            long effective = (long)baseThreat * actionPremiumMilli / 1000L;

            int synergyPercent = ComputeSynergyPercent(stableKeys);
            effective += effective * synergyPercent / 100L;
            if (effective > int.MaxValue) effective = int.MaxValue;
            return (int)effective;
        }

        /// <summary>协同上浮百分比：每命中一个协同类别 +budgetShare，合计封顶 10%。</summary>
        public static int ComputeSynergyPercent(IList<string> stableKeys)
        {
            List<string> tags = CollectCapabilityTags(stableKeys, null);
            int percent = 0;
            List<ModeHSynergyCategory> categories = ModeHContentCatalog.SynergyCategories;
            if (categories == null) return 0;
            for (int i = 0; i < categories.Count; i++)
            {
                ModeHSynergyCategory category = categories[i];
                if (category == null || string.IsNullOrEmpty(category.PublicTag)) continue;
                if (tags.Contains(category.PublicTag)) percent += category.BudgetShare;
            }
            int cap = (int)(ModeHConfig.ThreatSynergyPremiumCap * 100f);
            return percent > cap ? cap : percent;
        }

        private static int GetThreatScore(string stableKey)
        {
            ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(stableKey);
            return template != null ? template.ThreatScore : 0;
        }

        /// <summary>威胁最高者为核心；同分按 stable key ordinal 取小者，保证确定性。</summary>
        private static int PickCoreIndex(IList<string> units)
        {
            int bestIndex = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < units.Count; i++)
            {
                int score = GetThreatScore(units[i]);
                if (score > bestScore
                    || (score == bestScore && bestIndex >= 0
                        && string.CompareOrdinal(units[i], units[bestIndex]) < 0))
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        #endregion

        #region 分批入场

        /// <summary>
        /// 按进场剧本把单位分批。冻结判据：每一批的人数都不得超过同屏上限；
        /// 总人数可以超过上限，超出部分只能在前批减员后由生成事务放行。
        /// </summary>
        private static bool TryAssignBatches(
            IList<string> units,
            ModeHEntryScriptSpec entryScript,
            int simultaneousCap,
            int coreIndex,
            out List<int> batchIndices,
            out string failureReasonId)
        {
            batchIndices = null;
            failureReasonId = null;

            List<int> batchSizes = new List<int>();
            if (entryScript.BatchPattern != null)
            {
                for (int i = 0; i < entryScript.BatchPattern.Count; i++)
                {
                    int size = entryScript.BatchPattern[i];
                    if (size > 0) batchSizes.Add(size);
                }
            }
            if (batchSizes.Count == 0) batchSizes.Add(units.Count);

            int total = 0;
            for (int i = 0; i < batchSizes.Count; i++) total += batchSizes[i];

            // 把批次序列调整到恰好等于实际人数：先从尾批增减，必要时追加新批
            while (total < units.Count)
            {
                int last = batchSizes.Count - 1;
                if (batchSizes[last] < simultaneousCap) batchSizes[last]++;
                else batchSizes.Add(1);
                total++;
            }
            while (total > units.Count)
            {
                int last = batchSizes.Count - 1;
                if (batchSizes[last] > 1) batchSizes[last]--;
                else batchSizes.RemoveAt(last);
                total--;
                if (batchSizes.Count == 0)
                {
                    failureReasonId = "plan_batch_underflow";
                    return false;
                }
            }

            for (int i = 0; i < batchSizes.Count; i++)
            {
                if (batchSizes[i] > simultaneousCap)
                {
                    failureReasonId = "plan_batch_exceeds_cap";
                    return false;
                }
            }

            // 先按批次顺序填充，再按 coreEntersLast 把核心搬到目标批
            List<int> assignment = new List<int>();
            int cursor = 0;
            for (int b = 0; b < batchSizes.Count; b++)
            {
                for (int k = 0; k < batchSizes[b]; k++)
                {
                    assignment.Add(b);
                    cursor++;
                }
            }
            if (cursor != units.Count)
            {
                failureReasonId = "plan_batch_size_mismatch";
                return false;
            }

            if (coreIndex >= 0 && coreIndex < assignment.Count)
            {
                int targetBatch = entryScript.CoreEntersLast ? batchSizes.Count - 1 : 0;
                if (assignment[coreIndex] != targetBatch)
                {
                    for (int i = 0; i < assignment.Count; i++)
                    {
                        if (i == coreIndex || assignment[i] != targetBatch) continue;
                        int swap = assignment[i];
                        assignment[i] = assignment[coreIndex];
                        assignment[coreIndex] = swap;
                        break;
                    }
                }
            }

            batchIndices = assignment;
            return true;
        }

        #endregion

        #region 能力矩阵审计

        /// <summary>收集敌军能力标签与擂台条件公开标签的并集（ordinal 升序去重）。</summary>
        public static List<string> CollectCapabilityTags(
            IList<string> stableKeys, ModeHArenaConditionSpec condition)
        {
            List<string> tags = new List<string>();
            if (stableKeys != null)
            {
                for (int i = 0; i < stableKeys.Count; i++)
                {
                    ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(stableKeys[i]);
                    if (template == null || template.CapabilityTags == null) continue;
                    for (int j = 0; j < template.CapabilityTags.Count; j++)
                    {
                        string tag = template.CapabilityTags[j];
                        if (!string.IsNullOrEmpty(tag) && !tags.Contains(tag)) tags.Add(tag);
                    }
                }
            }
            if (condition != null && condition.PublicTags != null)
            {
                for (int i = 0; i < condition.PublicTags.Count; i++)
                {
                    string tag = condition.PublicTags[i];
                    if (!string.IsNullOrEmpty(tag) && !tags.Contains(tag)) tags.Add(tag);
                }
            }
            tags.Sort(StringComparer.Ordinal);
            return tags;
        }

        /// <summary>被硬封锁的原型集合：命中任一 hardLockedBy 标签即视为封锁。</summary>
        public static List<string> CollectLockedArchetypes(IList<string> enemyCapabilityTags)
        {
            List<string> locked = new List<string>();
            List<ModeHArchetypeCapability> matrix = ModeHContentCatalog.ArchetypeCapabilities;
            if (matrix == null || enemyCapabilityTags == null) return locked;
            for (int i = 0; i < matrix.Count; i++)
            {
                ModeHArchetypeCapability entry = matrix[i];
                if (entry == null || entry.HardLockedBy == null) continue;
                for (int j = 0; j < entry.HardLockedBy.Count; j++)
                {
                    if (!enemyCapabilityTags.Contains(entry.HardLockedBy[j])) continue;
                    if (!locked.Contains(entry.ArchetypeId)) locked.Add(entry.ArchetypeId);
                    break;
                }
            }
            locked.Sort(StringComparer.Ordinal);
            return locked;
        }

        /// <summary>
        /// roster-level veto：一个排列合法当且仅当其先发原型未被硬封锁。
        /// 单人阵容只有一种排列；双人阵容两种排列都被封死才拒绝该候选。
        /// </summary>
        public static bool HasLegalArrangement(
            IList<string> liveArchetypeIds, IList<string> lockedArchetypes)
        {
            if (liveArchetypeIds == null || liveArchetypeIds.Count == 0) return false;
            if (lockedArchetypes == null) return true;
            for (int i = 0; i < liveArchetypeIds.Count; i++)
            {
                if (!lockedArchetypes.Contains(liveArchetypeIds[i])) return true;
            }
            return false;
        }

        #endregion

        #region 公开摘要与侦察

        /// <summary>
        /// 构造公开摘要：只写 §17.5 允许公开的字段。精确 stable key、精确入场序列
        /// 与异常检查时点都不进入摘要；核心隐藏特征在侦察前保持为空。
        /// </summary>
        private static ModeHPublicSummaryDto BuildPublicSummary(
            IList<string> units,
            ModeHSkeletonSpec skeleton,
            ModeHEntryScriptSpec entryScript,
            ModeHArenaConditionSpec condition,
            IList<int> batchIndices)
        {
            ModeHPublicSummaryDto summary = new ModeHPublicSummaryDto();
            summary.enemyCountMax = units.Count;
            summary.enemyCountMin = entryScript.HiddenSeat && units.Count > 1 ? units.Count - 1 : units.Count;

            int coreIndex = PickCoreIndex(units);
            ModeHProfileTemplate core = coreIndex >= 0
                ? ModeHProfileRegistry.GetByStableKey(units[coreIndex]) : null;
            summary.primaryArchetypeId = core != null ? core.ArchetypeId : string.Empty;
            summary.entryScriptId = entryScript.EntryScriptId;
            summary.conditionId = condition.ConditionId;
            summary.hasHighThreatCore = skeleton.HasHighThreatCore;

            List<string> synergyTags = new List<string>();
            List<string> capabilities = CollectCapabilityTags(units, null);
            List<ModeHSynergyCategory> categories = ModeHContentCatalog.SynergyCategories;
            if (categories != null)
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    ModeHSynergyCategory category = categories[i];
                    if (category == null || string.IsNullOrEmpty(category.PublicTag)) continue;
                    if (capabilities.Contains(category.PublicTag) && !synergyTags.Contains(category.PublicTag))
                    {
                        synergyTags.Add(category.PublicTag);
                    }
                }
            }
            // 骨架与剧本的公开标签同样进入摘要（赔率的口令相合/冲突读这一份）
            AppendTags(synergyTags, skeleton.PublicTags);
            AppendTags(synergyTags, entryScript.PublicTags);
            AppendTags(synergyTags, condition.PublicTags);
            synergyTags.Sort(StringComparer.Ordinal);
            summary.synergyTags = synergyTags;

            // 带伤敌人数量属于侦察 current_injury 才揭示的隐藏字段
            summary.visibleWoundedEnemyCount = 0;

            List<string> anomalies = new List<string>();
            for (int i = 0; i < units.Count; i++)
            {
                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(units[i]);
                if (template == null || string.IsNullOrEmpty(template.AnomalyId)) continue;
                anomalies.Add(template.AnomalyId);
            }
            anomalies.Sort(StringComparer.Ordinal);
            summary.visibleAnomalyIds = anomalies;

            summary.coreTraitTags = new List<string>();
            summary.reconRevealKey = string.Empty;
            return summary;
        }

        private static void AppendTags(List<string> target, IList<string> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                string tag = source[i];
                if (!string.IsNullOrEmpty(tag) && !target.Contains(tag)) target.Add(tag);
            }
        }

        /// <summary>
        /// 免费侦察：每场至多一次，只揭示一个冻结字段；确认后不可重选。
        /// 揭示结果写入 publicSummary 后 planDigest 重算。
        /// </summary>
        public static bool TryApplyRecon(
            ModeHMatchPlanDto plan, string reconChoiceId, out string failureReasonId)
        {
            failureReasonId = null;
            if (plan == null || plan.publicSummary == null)
            {
                failureReasonId = "recon_plan_missing";
                return false;
            }
            if (!string.IsNullOrEmpty(plan.reconChoiceId))
            {
                failureReasonId = "recon_already_consumed";
                return false;
            }
            ModeHReconChoiceSpec choice = GetReconChoice(reconChoiceId);
            if (choice == null)
            {
                failureReasonId = "recon_choice_unknown";
                return false;
            }

            ModeHSkeletonSpec skeleton = GetSkeleton(plan.skeletonId);
            int coreIndex = PickCoreIndex(plan.enemyStableKeys);
            ModeHProfileTemplate core = coreIndex >= 0
                ? ModeHProfileRegistry.GetByStableKey(plan.enemyStableKeys[coreIndex]) : null;

            if (string.Equals(choice.RevealField, "coreTraitTags", StringComparison.Ordinal))
            {
                List<string> traits = new List<string>();
                if (core != null)
                {
                    if (!string.IsNullOrEmpty(core.QuirkId)) traits.Add(core.QuirkId);
                    if (!string.IsNullOrEmpty(core.TemperamentId)) traits.Add(core.TemperamentId);
                }
                traits.Sort(StringComparer.Ordinal);
                plan.publicSummary.coreTraitTags = traits;
            }
            else if (string.Equals(choice.RevealField, "visibleWoundedEnemyCount", StringComparison.Ordinal))
            {
                plan.publicSummary.visibleWoundedEnemyCount = skeleton != null ? skeleton.WoundedUnits : 0;
            }
            else if (string.Equals(choice.RevealField, "entryOrderHint", StringComparison.Ordinal))
            {
                plan.reconResult = BuildBatchHint(plan.enemyBatchIndices);
            }
            else if (string.Equals(choice.RevealField, "secondaryEquipmentHint", StringComparison.Ordinal))
            {
                plan.reconResult = core != null && core.CapabilityTags != null && core.CapabilityTags.Count > 0
                    ? string.Join(",", core.CapabilityTags.ToArray())
                    : string.Empty;
            }
            else
            {
                failureReasonId = "recon_reveal_field_unknown";
                return false;
            }

            plan.reconChoiceId = choice.ReconChoiceId;
            plan.publicSummary.reconRevealKey = choice.NameKey;

            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(plan, "planDigest", out digest, out digestError))
            {
                failureReasonId = "recon_digest_failed:" + digestError;
                return false;
            }
            plan.planDigest = digest;
            return true;
        }

        /// <summary>入场节奏提示：只公开每批人数，不公开精确成员顺序。</summary>
        private static string BuildBatchHint(IList<int> batchIndices)
        {
            if (batchIndices == null || batchIndices.Count == 0) return string.Empty;
            List<int> sizes = new List<int>();
            for (int i = 0; i < batchIndices.Count; i++)
            {
                int batch = batchIndices[i];
                while (sizes.Count <= batch) sizes.Add(0);
                sizes[batch]++;
            }
            string[] parts = new string[sizes.Count];
            for (int i = 0; i < sizes.Count; i++) parts[i] = sizes[i].ToString();
            return string.Join("-", parts);
        }

        private static ModeHReconChoiceSpec GetReconChoice(string reconChoiceId)
        {
            List<ModeHReconChoiceSpec> all = ModeHContentCatalog.ReconChoices;
            if (all == null || string.IsNullOrEmpty(reconChoiceId)) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null
                    && string.Equals(all[i].ReconChoiceId, reconChoiceId, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }
            return null;
        }

        private static ModeHSkeletonSpec GetSkeleton(string skeletonId)
        {
            List<ModeHSkeletonSpec> all = ModeHContentCatalog.Skeletons;
            if (all == null || string.IsNullOrEmpty(skeletonId)) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].SkeletonId, skeletonId, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }
            return null;
        }

        #endregion
    }
}

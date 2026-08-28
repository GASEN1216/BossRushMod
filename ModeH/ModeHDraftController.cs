using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 五席试棚与落选三路分流（设计提案 §17.2、§17.3、§25.1）。
    ///
    /// 冻结契约：
    /// - 每季只生成一次五名候选，展示顺序由 runSeed 固定，关闭页面不重抽；
    /// - 五席必须覆盖突进/远程/重装/消耗/残局五种公开原型各一名；
    /// - 五席中最多一名稀有异常；至少两名稳定型底色；
    /// - stableKey 与 profileId 均两两不同；
    /// - 候选不是运行时角色实例，只是稳定 key 的公开档案（不持有 Unity 对象引用）；
    /// - 签约顺序固定为“先主将、后替补”；
    /// - 落选三席以固定种子做一次 Fisher-Yates，得到回场签/候签/撕票三张去向牌。
    ///
    /// 任何一条不变式不满足都 fail-closed（不生成残缺五席），由调用方按 §17.2
    /// 走 `ModeHAvailability=Unavailable` 的退款离场路径。
    /// </summary>
    internal static class ModeHDraftController
    {
        #region 五席试棚

        /// <summary>
        /// 由已认证生产池确定性生成五席候选。失败时 candidates 为 null。
        /// </summary>
        /// <param name="runSeed">本季 runSeed。</param>
        /// <param name="productionTemplates">
        /// 当前认证为 Passed 的生产目录（由 ModeHPresetRegistry 物化，已按 productionOrder 升序）。
        /// </param>
        public static bool TryBuildDraft(
            long runSeed,
            IList<ModeHProfileTemplate> productionTemplates,
            out List<ModeHProfileDto> candidates,
            out string failureReasonId)
        {
            candidates = null;
            failureReasonId = null;

            if (productionTemplates == null || productionTemplates.Count < ModeHConfig.MinProductionCandidateCount)
            {
                failureReasonId = "draft_pool_too_small";
                return false;
            }

            List<ModeHProfileTemplate> picked;
            if (!TryPickArchetypeCoverage(runSeed, productionTemplates, out picked, out failureReasonId))
            {
                return false;
            }

            if (!TryEnforceAnomalyCap(runSeed, productionTemplates, picked, out failureReasonId)) return false;
            if (!TryEnforceStableTemperament(runSeed, productionTemplates, picked, out failureReasonId)) return false;

            // 展示顺序由 runSeed 固定：同一 runSeed 反复调用得到同一顺序
            ModeHSeedStream orderStream = ModeHSeedStream.Create(runSeed, ModeHSeedStream.Domains.Draft, 900);
            orderStream.Shuffle(picked);

            List<ModeHProfileDto> result = new List<ModeHProfileDto>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seenProfileIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < picked.Count; i++)
            {
                ModeHProfileTemplate template = picked[i];
                if (template == null || string.IsNullOrEmpty(template.StableKey))
                {
                    failureReasonId = "draft_template_invalid";
                    return false;
                }
                if (!seenKeys.Add(template.StableKey))
                {
                    failureReasonId = "draft_duplicate_stable_key";
                    return false;
                }
                ModeHProfileDto dto = CreateProfileDto(template);
                if (!seenProfileIds.Add(dto.profileId))
                {
                    failureReasonId = "draft_duplicate_profile_id";
                    return false;
                }
                result.Add(dto);
            }

            if (result.Count != ModeHConfig.DraftCandidateCount)
            {
                failureReasonId = "draft_candidate_count";
                return false;
            }

            candidates = result;
            return true;
        }

        /// <summary>把静态模板物化为 profile DTO（只保存 profile ID，不保存 Unity 引用）。</summary>
        private static ModeHProfileDto CreateProfileDto(ModeHProfileTemplate template)
        {
            ModeHProfileDto dto = new ModeHProfileDto();
            dto.profileId = template.ProfileTemplateId;
            dto.stableKey = template.StableKey;
            dto.displayNameKey = template.DisplayNameKey;
            dto.archetypeId = template.ArchetypeId;
            dto.temperamentId = template.TemperamentId;
            dto.quirkId = template.QuirkId != null ? template.QuirkId : string.Empty;
            dto.anomalyId = template.AnomalyId != null ? template.AnomalyId : string.Empty;
            dto.signatureCommandId = template.SignatureCommandId;
            dto.rumorKey = template.RumorKey;
            dto.standInPatternId = template.StandInPatternId;
            dto.status = (int)ModeHParticipantStatus.Available;
            dto.injuryId = string.Empty;
            dto.scarIds = new List<string>();
            dto.fameDisplayCount = 0;
            dto.enteredMatchCount = 0;
            dto.behaviorStatuses = new List<ModeHBehaviorStatusDto>();
            return dto;
        }

        #endregion

        #region 不变式收敛

        /// <summary>五种原型各取一名；任一原型无合格候选即 fail-closed。</summary>
        private static bool TryPickArchetypeCoverage(
            long runSeed,
            IList<ModeHProfileTemplate> pool,
            out List<ModeHProfileTemplate> picked,
            out string failureReasonId)
        {
            picked = new List<ModeHProfileTemplate>();
            failureReasonId = null;

            for (int i = 0; i < ModeHStableIds.AllArchetypes.Length; i++)
            {
                string archetypeId = ModeHStableIds.AllArchetypes[i];
                List<ModeHProfileTemplate> byArchetype = FilterByArchetype(pool, archetypeId);
                if (byArchetype.Count == 0)
                {
                    failureReasonId = "draft_archetype_uncovered:" + archetypeId;
                    picked = null;
                    return false;
                }
                ModeHSeedStream stream = ModeHSeedStream.Create(runSeed, ModeHSeedStream.Domains.Draft, i);
                picked.Add(byArchetype[stream.NextInt(byArchetype.Count)]);
            }

            if (picked.Count != ModeHConfig.RequiredArchetypeCoverage)
            {
                failureReasonId = "draft_archetype_coverage";
                picked = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 至多一名稀有异常。超出时按原型顺序从后往前把多余异常席换成同原型的非异常候选；
        /// 同原型没有非异常候选则 fail-closed。
        /// </summary>
        private static bool TryEnforceAnomalyCap(
            long runSeed,
            IList<ModeHProfileTemplate> pool,
            List<ModeHProfileTemplate> picked,
            out string failureReasonId)
        {
            failureReasonId = null;
            for (int i = picked.Count - 1; i >= 0; i--)
            {
                if (CountAnomalies(picked) <= ModeHConfig.MaxAnomalyCandidatesInDraft) return true;
                if (!ModeHProfileRegistry.HasAnomaly(picked[i])) continue;

                List<ModeHProfileTemplate> alternatives = FilterByArchetype(pool, picked[i].ArchetypeId);
                ModeHProfileTemplate replacement = PickFirstMatching(
                    runSeed, ModeHSeedStream.Domains.Draft, 100 + i, alternatives, picked, false, null);
                if (replacement == null)
                {
                    failureReasonId = "draft_anomaly_cap_unsatisfiable";
                    return false;
                }
                picked[i] = replacement;
            }
            return CountAnomalies(picked) <= ModeHConfig.MaxAnomalyCandidatesInDraft
                || Fail(out failureReasonId, "draft_anomaly_cap_unsatisfiable");
        }

        /// <summary>
        /// 至少两名稳定型底色。不足时按原型顺序把非稳定席换成同原型的稳定底色候选；
        /// 同原型没有稳定底色候选则 fail-closed。
        /// </summary>
        private static bool TryEnforceStableTemperament(
            long runSeed,
            IList<ModeHProfileTemplate> pool,
            List<ModeHProfileTemplate> picked,
            out string failureReasonId)
        {
            failureReasonId = null;
            for (int i = 0; i < picked.Count; i++)
            {
                if (CountStableTemperaments(picked) >= ModeHConfig.MinStableTemperamentCandidatesInDraft) return true;
                if (ModeHProfileRegistry.IsStableTemperament(picked[i])) continue;

                List<ModeHProfileTemplate> alternatives = FilterByArchetype(pool, picked[i].ArchetypeId);
                ModeHProfileTemplate replacement = PickFirstMatching(
                    runSeed, ModeHSeedStream.Domains.Draft, 200 + i, alternatives, picked, true, picked[i]);
                if (replacement == null) continue;
                picked[i] = replacement;
            }
            return CountStableTemperaments(picked) >= ModeHConfig.MinStableTemperamentCandidatesInDraft
                || Fail(out failureReasonId, "draft_stable_temperament_unsatisfiable");
        }

        private static bool Fail(out string failureReasonId, string reason)
        {
            failureReasonId = reason;
            return false;
        }

        /// <summary>
        /// 在同原型候选里确定性取一个替换项：必须不在已选集合内；
        /// requireStable=true 时只取稳定底色，否则只取非异常。
        /// </summary>
        private static ModeHProfileTemplate PickFirstMatching(
            long runSeed,
            string domain,
            int sequence,
            IList<ModeHProfileTemplate> alternatives,
            IList<ModeHProfileTemplate> alreadyPicked,
            bool requireStable,
            ModeHProfileTemplate excluded)
        {
            List<ModeHProfileTemplate> usable = new List<ModeHProfileTemplate>();
            for (int i = 0; i < alternatives.Count; i++)
            {
                ModeHProfileTemplate candidate = alternatives[i];
                if (candidate == null) continue;
                if (excluded != null && ReferenceEquals(candidate, excluded)) continue;
                if (ContainsStableKey(alreadyPicked, candidate.StableKey)) continue;
                if (requireStable)
                {
                    if (!ModeHProfileRegistry.IsStableTemperament(candidate)) continue;
                }
                else
                {
                    if (ModeHProfileRegistry.HasAnomaly(candidate)) continue;
                }
                usable.Add(candidate);
            }
            if (usable.Count == 0) return null;
            ModeHSeedStream stream = ModeHSeedStream.Create(runSeed, domain, sequence);
            return usable[stream.NextInt(usable.Count)];
        }

        private static List<ModeHProfileTemplate> FilterByArchetype(
            IList<ModeHProfileTemplate> pool, string archetypeId)
        {
            List<ModeHProfileTemplate> result = new List<ModeHProfileTemplate>();
            for (int i = 0; i < pool.Count; i++)
            {
                ModeHProfileTemplate template = pool[i];
                if (template == null || !template.ProductionCandidate) continue;
                if (!string.Equals(template.ArchetypeId, archetypeId, StringComparison.Ordinal)) continue;
                result.Add(template);
            }
            // 生产目录已按 productionOrder 升序，这里再按稳定 key 兜底排序，防止上游顺序漂移
            result.Sort(CompareByProductionOrder);
            return result;
        }

        private static int CompareByProductionOrder(ModeHProfileTemplate a, ModeHProfileTemplate b)
        {
            if (a.ProductionOrder != b.ProductionOrder) return a.ProductionOrder.CompareTo(b.ProductionOrder);
            return string.CompareOrdinal(a.StableKey, b.StableKey);
        }

        private static bool ContainsStableKey(IList<ModeHProfileTemplate> list, string stableKey)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && string.Equals(list[i].StableKey, stableKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountAnomalies(IList<ModeHProfileTemplate> list)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (ModeHProfileRegistry.HasAnomaly(list[i])) count++;
            }
            return count;
        }

        private static int CountStableTemperaments(IList<ModeHProfileTemplate> list)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (ModeHProfileRegistry.IsStableTemperament(list[i])) count++;
            }
            return count;
        }

        #endregion

        #region 签约

        /// <summary>
        /// 五选二：签约顺序固定为“先主将、后替补”。两个 ID 必须都在候选列表内且互不相同。
        /// </summary>
        public static bool TrySignContracts(
            IList<ModeHProfileDto> candidates,
            string contractMainProfileId,
            string contractSubProfileId,
            out ModeHContractDto contract,
            out string failureReasonId)
        {
            contract = null;
            failureReasonId = null;

            if (candidates == null || candidates.Count != ModeHConfig.DraftCandidateCount)
            {
                failureReasonId = "sign_candidates_invalid";
                return false;
            }
            if (string.IsNullOrEmpty(contractMainProfileId))
            {
                failureReasonId = "sign_main_missing";
                return false;
            }
            if (string.IsNullOrEmpty(contractSubProfileId))
            {
                failureReasonId = "sign_sub_missing";
                return false;
            }
            if (string.Equals(contractMainProfileId, contractSubProfileId, StringComparison.Ordinal))
            {
                failureReasonId = "sign_duplicate_profile";
                return false;
            }
            if (!ContainsProfileId(candidates, contractMainProfileId))
            {
                failureReasonId = "sign_main_not_candidate";
                return false;
            }
            if (!ContainsProfileId(candidates, contractSubProfileId))
            {
                failureReasonId = "sign_sub_not_candidate";
                return false;
            }

            contract = new ModeHContractDto();
            contract.contractMainProfileId = contractMainProfileId;
            contract.contractSubProfileId = contractSubProfileId;
            return true;
        }

        private static bool ContainsProfileId(IList<ModeHProfileDto> candidates, string profileId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null
                    && string.Equals(candidates[i].profileId, profileId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region 落选三路分流

        /// <summary>
        /// 剩余三席立即以固定种子做一次 Fisher-Yates，得到回场签/候签/撕票三张去向牌。
        /// 三张牌一一对应，不重复、不落空。
        /// </summary>
        public static bool TryAssignEchoDestinations(
            long runSeed,
            IList<ModeHProfileDto> candidates,
            ModeHContractDto contract,
            out List<ModeHEchoAssignmentDto> assignments,
            out string failureReasonId)
        {
            assignments = null;
            failureReasonId = null;

            if (candidates == null || contract == null)
            {
                failureReasonId = "echo_input_missing";
                return false;
            }

            List<string> remaining = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                ModeHProfileDto dto = candidates[i];
                if (dto == null || string.IsNullOrEmpty(dto.profileId)) continue;
                if (string.Equals(dto.profileId, contract.contractMainProfileId, StringComparison.Ordinal)) continue;
                if (string.Equals(dto.profileId, contract.contractSubProfileId, StringComparison.Ordinal)) continue;
                remaining.Add(dto.profileId);
            }

            if (remaining.Count != ModeHConfig.EchoAssignmentCount)
            {
                failureReasonId = "echo_remaining_count";
                return false;
            }

            // 先按 ordinal 稳定排序再洗牌，保证与上游顺序无关
            remaining.Sort(StringComparer.Ordinal);
            ModeHSeedStream stream = ModeHSeedStream.Create(runSeed, ModeHSeedStream.Domains.Echo, 0);
            stream.Shuffle(remaining);

            assignments = new List<ModeHEchoAssignmentDto>();
            assignments.Add(CreateAssignment(
                remaining[0], ModeHStableIds.EchoDestinationReturnEnemy, ModeHConfig.EchoReturnMatchIndex));
            assignments.Add(CreateAssignment(
                remaining[1], ModeHStableIds.EchoDestinationTransferCandidate,
                ModeHConfig.FirstTransferWindowMatchIndex));
            assignments.Add(CreateAssignment(
                remaining[2], ModeHStableIds.EchoDestinationRemoved, 0));
            return true;
        }

        private static ModeHEchoAssignmentDto CreateAssignment(
            string profileId, string destinationId, int scheduledMatchIndex)
        {
            ModeHEchoAssignmentDto dto = new ModeHEchoAssignmentDto();
            dto.profileId = profileId;
            dto.destinationId = destinationId;
            dto.scheduledMatchIndex = scheduledMatchIndex;
            dto.resolved = false;
            return dto;
        }

        /// <summary>取指定去向的落选 profileId；没有则返回空串。</summary>
        public static string GetEchoProfileId(
            IList<ModeHEchoAssignmentDto> assignments, string destinationId)
        {
            if (assignments == null) return string.Empty;
            for (int i = 0; i < assignments.Count; i++)
            {
                ModeHEchoAssignmentDto assignment = assignments[i];
                if (assignment == null) continue;
                if (string.Equals(assignment.destinationId, destinationId, StringComparison.Ordinal))
                {
                    return assignment.profileId != null ? assignment.profileId : string.Empty;
                }
            }
            return string.Empty;
        }

        /// <summary>撕票选手不得再出现在任何候选、敌军或市场来源里。</summary>
        public static bool IsRemovedProfile(IList<ModeHEchoAssignmentDto> assignments, string profileId)
        {
            if (assignments == null || string.IsNullOrEmpty(profileId)) return false;
            for (int i = 0; i < assignments.Count; i++)
            {
                ModeHEchoAssignmentDto assignment = assignments[i];
                if (assignment == null) continue;
                if (!string.Equals(assignment.destinationId, ModeHStableIds.EchoDestinationRemoved,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.Equals(assignment.profileId, profileId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        #endregion
    }
}

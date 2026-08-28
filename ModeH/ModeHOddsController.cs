using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>赔率拆解的一行（只读展示用，不参与存档）。</summary>
    internal sealed class ModeHOddsBreakdownEntry
    {
        /// <summary>分量本地化 key。</summary>
        public string LabelKey;
        /// <summary>该分量的整数得分。</summary>
        public int Value;
        /// <summary>是否属于敌方分。</summary>
        public bool IsEnemySide;
    }

    /// <summary>一次完整报价（公开分差 + 拆解）。</summary>
    internal sealed class ModeHOddsQuote
    {
        /// <summary>玩家公开分。</summary>
        public int PlayerPublicScore;
        /// <summary>敌方公开分。</summary>
        public int EnemyPublicScore;
        /// <summary>公开分差。</summary>
        public int PublicEdge;
        /// <summary>净赔率档（1..5）。</summary>
        public int Odds;
        /// <summary>盘口称呼 key。</summary>
        public string ToneKey;
        /// <summary>逐分量拆解（顺序固定，供 UI 直接渲染）。</summary>
        public List<ModeHOddsBreakdownEntry> Breakdown;
    }

    /// <summary>玩家侧赔率输入（只读快照，由 LoadoutEditing 组装）。</summary>
    internal sealed class ModeHOddsPlayerInput
    {
        /// <summary>先发选手公开档案。</summary>
        public ModeHProfileDto Starter;
        /// <summary>接力者公开档案；为 null 表示 matchRelay=Empty。</summary>
        public ModeHProfileDto Relay;
        /// <summary>先发已选 kit。</summary>
        public List<string> StarterKitIds;
        /// <summary>接力者已选 kit。</summary>
        public List<string> RelayKitIds;
        /// <summary>本场锁定的口令 ID。</summary>
        public string CommandId;
    }

    /// <summary>
    /// Mode H 赔率服务（设计提案 §17.5、§25.1）。
    ///
    /// 冻结契约：
    /// - `publicEdge = playerPublicScore - enemyPublicScore`，档位阈值由 §17.5 冻结；
    /// - 只读公开摘要与玩家当前公开整备，不反向改计划、不读 AI 实际表现；
    /// - 只有 `VerifiedBehavior` 的行为进入分数；`ReportOnly` 计 0 分；
    ///   `Unavailable` 不得被抽取（由 registry 的选择门保证）；
    /// - 每个字段按其作用域只计一次，无隐藏小数与随机修正；
    /// - 下注额不参与赔率计算（防止循环定价）。
    /// </summary>
    internal static class ModeHOddsController
    {
        #region 报价

        /// <summary>组装一次完整报价。plan 为 null 时返回 null。</summary>
        public static ModeHOddsQuote BuildQuote(ModeHOddsPlayerInput input, ModeHMatchPlanDto plan)
        {
            if (plan == null || plan.publicSummary == null || input == null) return null;

            List<ModeHOddsBreakdownEntry> breakdown = new List<ModeHOddsBreakdownEntry>();
            int playerScore = ComputePlayerPublicScore(input, plan, breakdown);
            int enemyScore = ComputeEnemyPublicScore(plan, breakdown);

            ModeHOddsQuote quote = new ModeHOddsQuote();
            quote.PlayerPublicScore = playerScore;
            quote.EnemyPublicScore = enemyScore;
            quote.PublicEdge = playerScore - enemyScore;
            quote.Odds = ModeHStateModel.ResolveOddsTier(quote.PublicEdge);
            quote.ToneKey = GetToneKey(quote.Odds);
            quote.Breakdown = breakdown;
            return quote;
        }

        private static string GetToneKey(int odds)
        {
            List<ModeHOddsTier> tiers = ModeHContentCatalog.OddsTiers;
            if (tiers == null) return string.Empty;
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiers[i] != null && tiers[i].Odds == odds)
                {
                    return tiers[i].ToneKey != null ? tiers[i].ToneKey : string.Empty;
                }
            }
            return string.Empty;
        }

        #endregion

        #region 玩家公开分

        /// <summary>
        /// `playerPublicScore = roster + matchup + equipment + injury + anomaly + scar + command + arena`。
        /// </summary>
        public static int ComputePlayerPublicScore(
            ModeHOddsPlayerInput input, ModeHMatchPlanDto plan, List<ModeHOddsBreakdownEntry> breakdown)
        {
            if (input == null || input.Starter == null || plan == null || plan.publicSummary == null) return 0;
            ModeHJsonValue w = ModeHContentCatalog.PlayerWeights;
            ModeHPublicSummaryDto summary = plan.publicSummary;
            int total = 0;

            // roster：接力者有无
            bool hasRelay = input.Relay != null && !string.IsNullOrEmpty(input.Relay.profileId);
            total += Add(breakdown, false, hasRelay ? "Odds_RelayAvailable" : "Odds_RelayEmpty",
                hasRelay
                    ? ModeHContentCatalog.GetWeight(w, "relayAvailable", 5)
                    : ModeHContentCatalog.GetWeight(w, "relayEmpty", -12));

            // matchup：原型克制关系（先发 ±8，接力 ±4）
            int starterMatchup = ComputeMatchupScore(
                input.Starter.archetypeId, summary.primaryArchetypeId,
                ModeHContentCatalog.GetWeight(w, "starterCounters", 8),
                ModeHContentCatalog.GetWeight(w, "starterCountered", -8));
            if (starterMatchup != 0) total += Add(breakdown, false, "Odds_StarterMatchup", starterMatchup);
            if (hasRelay)
            {
                int relayMatchup = ComputeMatchupScore(
                    input.Relay.archetypeId, summary.primaryArchetypeId,
                    ModeHContentCatalog.GetWeight(w, "relayCounters", 4),
                    ModeHContentCatalog.GetWeight(w, "relayCountered", -4));
                if (relayMatchup != 0) total += Add(breakdown, false, "Odds_RelayMatchup", relayMatchup);
            }

            // equipment：品质分（四件合计封顶）+ 整套 tag 克制只计一次
            int equipment = ComputeEquipmentScore(input, summary, w);
            if (equipment != 0) total += Add(breakdown, false, "Odds_Equipment", equipment);

            // injury：只有 VerifiedBehavior 的伤病计分
            int injury = 0;
            if (HasVerifiedInjury(input.Starter))
            {
                injury += ModeHContentCatalog.GetWeight(w, "starterInjured", -5);
            }
            if (hasRelay && HasVerifiedInjury(input.Relay))
            {
                injury += ModeHContentCatalog.GetWeight(w, "relayInjured", -3);
            }
            if (injury != 0) total += Add(breakdown, false, "Odds_Injury", injury);

            // anomaly：仅 VerifiedBehavior 计入
            int anomaly = ComputeAnomalyScore(input.Starter, w)
                + (hasRelay ? ComputeAnomalyScore(input.Relay, w) : 0);
            if (anomaly != 0) total += Add(breakdown, false, "Odds_Anomaly", anomaly);

            // scar：benefit/cost 命中，合计限制在 [-8, +8]
            int scar = ComputeScarScore(input.Starter, summary, w)
                + (hasRelay ? ComputeScarScore(input.Relay, summary, w) : 0);
            int scarMin = ModeHContentCatalog.GetWeight(w, "scarTotalMin", -8);
            int scarMax = ModeHContentCatalog.GetWeight(w, "scarTotalMax", 8);
            if (scar < scarMin) scar = scarMin;
            if (scar > scarMax) scar = scarMax;
            if (scar != 0) total += Add(breakdown, false, "Odds_Scar", scar);

            // command：通用口令相合/冲突 + 招牌口令归属
            int command = ComputeCommandScore(input, summary, w);
            if (command != 0) total += Add(breakdown, false, "Odds_Command", command);

            // arena：整场只计一次
            int arena = ComputeArenaScore(input, summary.conditionId, w);
            if (arena != 0) total += Add(breakdown, false, "Odds_Arena", arena);

            return total;
        }

        /// <summary>原型矩阵：克制取正、被克制取负、无关系为 0。</summary>
        private static int ComputeMatchupScore(
            string ownArchetype, string enemyArchetype, int countersValue, int counteredValue)
        {
            if (string.IsNullOrEmpty(ownArchetype) || string.IsNullOrEmpty(enemyArchetype)) return 0;
            int forward = ModeHContentCatalog.GetArchetypeMatchup(ownArchetype, enemyArchetype);
            if (forward > 0) return countersValue;
            int backward = ModeHContentCatalog.GetArchetypeMatchup(enemyArchetype, ownArchetype);
            if (backward > 0) return counteredValue;
            return 0;
        }

        /// <summary>
        /// 装备分：每个根物品按原版 gameQuality 取 +0..+5，四件合计封顶；
        /// 整套公开 tag 明确克制/被克制只计一次，同时命中取 0。
        /// </summary>
        private static int ComputeEquipmentScore(
            ModeHOddsPlayerInput input, ModeHPublicSummaryDto summary, ModeHJsonValue w)
        {
            int qualityScore = 0;
            qualityScore += SumKitQuality(input.StarterKitIds, w);
            qualityScore += SumKitQuality(input.RelayKitIds, w);
            int cap = ModeHContentCatalog.GetWeight(w, "kitQualityTotalCap", 12);
            if (qualityScore > cap) qualityScore = cap;

            bool counters = false;
            bool countered = false;
            EvaluateKitTags(input.StarterKitIds, summary, ref counters, ref countered);
            EvaluateKitTags(input.RelayKitIds, summary, ref counters, ref countered);
            int tagScore = 0;
            if (counters && !countered) tagScore = ModeHContentCatalog.GetWeight(w, "equipmentTagCounters", 4);
            else if (countered && !counters) tagScore = ModeHContentCatalog.GetWeight(w, "equipmentTagCountered", -4);

            return qualityScore + tagScore;
        }

        private static int SumKitQuality(IList<string> kitIds, ModeHJsonValue w)
        {
            if (kitIds == null) return 0;
            int score = 0;
            for (int i = 0; i < kitIds.Count; i++)
            {
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(kitIds[i]);
                if (kit == null || !kit.Available) continue;
                int quality = kit.ResolvedQuality;
                if (quality < ModeHConfig.MinGameQuality || quality > ModeHConfig.MaxGameQuality) continue;
                score += ModeHContentCatalog.GetWeightAt(w, "kitQualityByGameQuality", quality - 1, 0);
            }
            return score;
        }

        private static void EvaluateKitTags(
            IList<string> kitIds, ModeHPublicSummaryDto summary, ref bool counters, ref bool countered)
        {
            if (kitIds == null || summary == null || summary.synergyTags == null) return;
            for (int i = 0; i < kitIds.Count; i++)
            {
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(kitIds[i]);
                if (kit == null || kit.Spec == null || kit.Spec.PublicTags == null) continue;
                for (int j = 0; j < kit.Spec.PublicTags.Count; j++)
                {
                    string tag = kit.Spec.PublicTags[j];
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (tag.StartsWith("counter_", StringComparison.Ordinal))
                    {
                        if (summary.synergyTags.Contains(tag.Substring(8))) counters = true;
                    }
                    else if (tag.StartsWith("weak_", StringComparison.Ordinal))
                    {
                        if (summary.synergyTags.Contains(tag.Substring(5))) countered = true;
                    }
                }
            }
        }

        /// <summary>伤病只在其行为被认证为 VerifiedBehavior 时进入赔率。</summary>
        private static bool HasVerifiedInjury(ModeHProfileDto profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.injuryId)) return false;
            return IsVerified(profile, profile.injuryId);
        }

        private static int ComputeAnomalyScore(ModeHProfileDto profile, ModeHJsonValue w)
        {
            if (profile == null || string.IsNullOrEmpty(profile.anomalyId)) return 0;
            if (!IsVerified(profile, profile.anomalyId)) return 0;
            if (string.Equals(profile.anomalyId, ModeHStableIds.AnomalyCowardBlood, StringComparison.Ordinal))
            {
                return ModeHContentCatalog.GetWeight(w, "anomalyBlood", -5);
            }
            if (string.Equals(profile.anomalyId, ModeHStableIds.AnomalyCowardCrowd, StringComparison.Ordinal))
            {
                return ModeHContentCatalog.GetWeight(w, "anomalyCrowd", -7);
            }
            if (string.Equals(profile.anomalyId, ModeHStableIds.AnomalyCowardStrong, StringComparison.Ordinal))
            {
                return ModeHContentCatalog.GetWeight(w, "anomalyStrong", -4);
            }
            if (string.Equals(profile.anomalyId, ModeHStableIds.AnomalyError, StringComparison.Ordinal))
            {
                return ModeHContentCatalog.GetWeight(w, "anomalyError", -2);
            }
            return 0;
        }

        /// <summary>战痕：benefit/cost tag 命中公开摘要各 ±3；双方同时命中取 0。</summary>
        private static int ComputeScarScore(
            ModeHProfileDto profile, ModeHPublicSummaryDto summary, ModeHJsonValue w)
        {
            if (profile == null || profile.scarIds == null || summary == null) return 0;
            List<ModeHScarSpec> scars = ModeHContentCatalog.Scars;
            if (scars == null) return 0;
            int score = 0;
            for (int i = 0; i < profile.scarIds.Count; i++)
            {
                string scarId = profile.scarIds[i];
                if (!IsVerified(profile, scarId)) continue;
                ModeHScarSpec spec = null;
                for (int j = 0; j < scars.Count; j++)
                {
                    if (scars[j] != null && string.Equals(scars[j].ScarId, scarId, StringComparison.Ordinal))
                    {
                        spec = scars[j];
                        break;
                    }
                }
                if (spec == null) continue;
                bool benefit = HasTag(summary, spec.BenefitTag);
                bool cost = HasTag(summary, spec.CostTag);
                if (benefit && cost) continue;
                if (benefit) score += ModeHContentCatalog.GetWeight(w, "scarBenefit", 3);
                else if (cost) score += ModeHContentCatalog.GetWeight(w, "scarCost", -3);
            }
            return score;
        }

        private static bool HasTag(ModeHPublicSummaryDto summary, string tag)
        {
            if (string.IsNullOrEmpty(tag) || summary.synergyTags == null) return false;
            return summary.synergyTags.Contains(tag);
        }

        /// <summary>口令：通用相合/冲突 ±4/-3；招牌由先发持有且相合 +5，接力者持有 +2。</summary>
        private static int ComputeCommandScore(
            ModeHOddsPlayerInput input, ModeHPublicSummaryDto summary, ModeHJsonValue w)
        {
            if (string.IsNullOrEmpty(input.CommandId)) return 0;

            ModeHCommandTagMapping mapping = null;
            List<ModeHCommandTagMapping> map = ModeHContentCatalog.CommandTagMap;
            if (map != null)
            {
                for (int i = 0; i < map.Count; i++)
                {
                    if (map[i] != null && string.Equals(map[i].CommandId, input.CommandId, StringComparison.Ordinal))
                    {
                        mapping = map[i];
                        break;
                    }
                }
            }

            int score = 0;
            if (mapping != null)
            {
                bool aligned = ContainsAny(summary.synergyTags, mapping.AlignedTags);
                bool conflicted = ContainsAny(summary.synergyTags, mapping.ConflictedTags);
                if (aligned && !conflicted) score += ModeHContentCatalog.GetWeight(w, "commandAligned", 4);
                else if (conflicted && !aligned) score += ModeHContentCatalog.GetWeight(w, "commandConflicted", -3);
            }
            else
            {
                // 招牌口令：只有其持有者在场且与公开摘要相合时计分
                bool starterOwns = input.Starter != null
                    && string.Equals(input.Starter.signatureCommandId, input.CommandId, StringComparison.Ordinal);
                bool relayOwns = input.Relay != null
                    && string.Equals(input.Relay.signatureCommandId, input.CommandId, StringComparison.Ordinal);
                if (starterOwns && IsSignatureAligned(input.CommandId, summary))
                {
                    score += ModeHContentCatalog.GetWeight(w, "signatureCommandStarter", 5);
                }
                else if (relayOwns && IsSignatureAligned(input.CommandId, summary))
                {
                    score += ModeHContentCatalog.GetWeight(w, "signatureCommandRelay", 2);
                }
            }
            return score;
        }

        /// <summary>
        /// 招牌口令相合判定：招牌口令在 commandTagMap 中没有条目，改按其冻结语义
        /// 与公开摘要标签对照（weakness/last_mag 对已知核心，anchor 对人海，
        /// together 对护卫阵，handoff 对后程增援）。
        /// </summary>
        private static bool IsSignatureAligned(string commandId, ModeHPublicSummaryDto summary)
        {
            if (summary == null || summary.synergyTags == null) return false;
            if (string.Equals(commandId, "weakness", StringComparison.Ordinal)
                || string.Equals(commandId, "last_mag", StringComparison.Ordinal))
            {
                return summary.hasHighThreatCore || summary.synergyTags.Contains("single_core");
            }
            if (string.Equals(commandId, "anchor", StringComparison.Ordinal))
            {
                return summary.synergyTags.Contains("crowd") || summary.synergyTags.Contains("crossfire");
            }
            if (string.Equals(commandId, "together", StringComparison.Ordinal))
            {
                return summary.synergyTags.Contains("escort_screen");
            }
            if (string.Equals(commandId, "handoff", StringComparison.Ordinal))
            {
                return summary.synergyTags.Contains("late_reinforcement")
                    || summary.synergyTags.Contains("reinforcement");
            }
            return false;
        }

        /// <summary>擂台条件：对当前双人组合整场只计一次；同时有利与不利取 0。</summary>
        private static int ComputeArenaScore(
            ModeHOddsPlayerInput input, string conditionId, ModeHJsonValue w)
        {
            if (string.IsNullOrEmpty(conditionId)) return 0;
            List<ModeHArenaConditionSpec> conditions = ModeHContentCatalog.ArenaConditions;
            if (conditions == null) return 0;
            ModeHArenaConditionSpec spec = null;
            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] != null
                    && string.Equals(conditions[i].ConditionId, conditionId, StringComparison.Ordinal))
                {
                    spec = conditions[i];
                    break;
                }
            }
            if (spec == null) return 0;

            bool favored = false;
            bool disfavored = false;
            EvaluateArena(spec, input.Starter, ref favored, ref disfavored);
            EvaluateArena(spec, input.Relay, ref favored, ref disfavored);
            if (favored && disfavored) return 0;
            if (favored) return ModeHContentCatalog.GetWeight(w, "arenaFavorable", 4);
            if (disfavored) return ModeHContentCatalog.GetWeight(w, "arenaUnfavorable", -4);
            return 0;
        }

        private static void EvaluateArena(
            ModeHArenaConditionSpec spec, ModeHProfileDto profile, ref bool favored, ref bool disfavored)
        {
            if (profile == null || string.IsNullOrEmpty(profile.archetypeId)) return;
            if (spec.FavoredArchetypeIds != null && spec.FavoredArchetypeIds.Contains(profile.archetypeId))
            {
                favored = true;
            }
            if (spec.DisfavoredArchetypeIds != null && spec.DisfavoredArchetypeIds.Contains(profile.archetypeId))
            {
                disfavored = true;
            }
        }

        #endregion

        #region 敌方公开分

        /// <summary>
        /// `enemyPublicScore = stage + count + core + synergy + visibleEnemyStatus`。
        /// 只聚合一次公开计划字段，绝不按隐藏 stable key 或每个未公开敌人重复累计。
        /// </summary>
        public static int ComputeEnemyPublicScore(
            ModeHMatchPlanDto plan, List<ModeHOddsBreakdownEntry> breakdown)
        {
            if (plan == null || plan.publicSummary == null) return 0;
            ModeHJsonValue w = ModeHContentCatalog.EnemyWeights;
            ModeHPublicSummaryDto summary = plan.publicSummary;
            int total = 0;

            int stageIndex = plan.matchIndex - ModeHConfig.FirstMatchIndex;
            total += Add(breakdown, true, "Odds_EnemyStage",
                ModeHContentCatalog.GetWeightAt(w, "stageByMatchIndex", stageIndex, 0));

            int countIndex = summary.enemyCountMax - 1;
            if (countIndex < 0) countIndex = 0;
            total += Add(breakdown, true, "Odds_EnemyCount",
                ModeHContentCatalog.GetWeightAt(w, "countUpperBound", countIndex, 0));

            if (summary.hasHighThreatCore)
            {
                total += Add(breakdown, true, "Odds_EnemyCore",
                    ModeHContentCatalog.GetWeight(w, "highThreatCore", 10));
            }

            int synergy = ComputeEnemySynergyScore(summary, w);
            if (synergy != 0) total += Add(breakdown, true, "Odds_EnemySynergy", synergy);

            int status = ComputeEnemyStatusScore(plan, summary, w);
            if (status != 0) total += Add(breakdown, true, "Odds_EnemyStatus", status);

            return total;
        }

        private static int ComputeEnemySynergyScore(ModeHPublicSummaryDto summary, ModeHJsonValue w)
        {
            if (summary.synergyTags == null) return 0;
            List<ModeHSynergyCategory> categories = ModeHContentCatalog.SynergyCategories;
            if (categories == null) return 0;
            int perCategory = ModeHContentCatalog.GetWeight(w, "synergyPerCategory", 5);
            int cap = ModeHContentCatalog.GetWeight(w, "synergyCap", 10);
            int score = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                ModeHSynergyCategory category = categories[i];
                if (category == null || string.IsNullOrEmpty(category.PublicTag)) continue;
                if (summary.synergyTags.Contains(category.PublicTag)) score += perCategory;
            }
            return score > cap ? cap : score;
        }

        /// <summary>公开带伤敌人与公开异常；只有 VerifiedBehavior 才计分。</summary>
        private static int ComputeEnemyStatusScore(
            ModeHMatchPlanDto plan, ModeHPublicSummaryDto summary, ModeHJsonValue w)
        {
            int score = 0;
            if (summary.visibleWoundedEnemyCount > 0)
            {
                int perWounded = ModeHContentCatalog.GetWeight(w, "woundedEnemy", -5);
                for (int i = 0; i < summary.visibleWoundedEnemyCount; i++)
                {
                    if (IsEnemyInjuryVerified(plan)) score += perWounded;
                }
            }
            if (summary.visibleAnomalyIds != null)
            {
                for (int i = 0; i < summary.visibleAnomalyIds.Count; i++)
                {
                    string anomalyId = summary.visibleAnomalyIds[i];
                    if (!IsEnemyAnomalyVerified(plan, anomalyId)) continue;
                    if (string.Equals(anomalyId, ModeHStableIds.AnomalyCowardBlood, StringComparison.Ordinal))
                    {
                        score += ModeHContentCatalog.GetWeight(w, "anomalyBlood", -5);
                    }
                    else if (string.Equals(anomalyId, ModeHStableIds.AnomalyCowardCrowd, StringComparison.Ordinal))
                    {
                        score += ModeHContentCatalog.GetWeight(w, "anomalyCrowd", -7);
                    }
                    else if (string.Equals(anomalyId, ModeHStableIds.AnomalyCowardStrong, StringComparison.Ordinal))
                    {
                        score += ModeHContentCatalog.GetWeight(w, "anomalyStrong", -4);
                    }
                    else if (string.Equals(anomalyId, ModeHStableIds.AnomalyError, StringComparison.Ordinal))
                    {
                        score += ModeHContentCatalog.GetWeight(w, "anomalyError", -2);
                    }
                }
            }
            return score;
        }

        /// <summary>敌方带伤：任一计划成员的伤病行为通过认证才计分。</summary>
        private static bool IsEnemyInjuryVerified(ModeHMatchPlanDto plan)
        {
            if (plan == null || plan.enemyStableKeys == null) return false;
            for (int i = 0; i < plan.enemyStableKeys.Count; i++)
            {
                if (ModeHCommandCompatibilityRegistry.HasVerifiedInjuryBehavior(plan.enemyStableKeys[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>敌方异常：其持有者的对应行为通过认证才计分。</summary>
        private static bool IsEnemyAnomalyVerified(ModeHMatchPlanDto plan, string anomalyId)
        {
            if (plan == null || plan.enemyStableKeys == null || string.IsNullOrEmpty(anomalyId)) return false;
            for (int i = 0; i < plan.enemyStableKeys.Count; i++)
            {
                string key = plan.enemyStableKeys[i];
                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(key);
                if (template == null) continue;
                if (!string.Equals(template.AnomalyId, anomalyId, StringComparison.Ordinal)) continue;
                if (ModeHCommandCompatibilityRegistry.HasVerifiedAnomalyBehavior(key, anomalyId)) return true;
            }
            return false;
        }

        #endregion

        #region 通用

        /// <summary>行为状态只有 VerifiedBehavior 才进入赔率；其余按 0 分处理。</summary>
        private static bool IsVerified(ModeHProfileDto profile, string behaviorId)
        {
            if (profile == null || string.IsNullOrEmpty(behaviorId)) return false;
            if (profile.behaviorStatuses != null)
            {
                for (int i = 0; i < profile.behaviorStatuses.Count; i++)
                {
                    ModeHBehaviorStatusDto status = profile.behaviorStatuses[i];
                    if (status == null) continue;
                    if (!string.Equals(status.entryId, behaviorId, StringComparison.Ordinal)) continue;
                    return status.status == (int)ModeHCommandCompatibilityStatus.VerifiedBehavior;
                }
            }
            // 没有实测记录时按 ReportOnly 处理（0 分），而不是乐观假定已验证
            return false;
        }

        private static bool ContainsAny(IList<string> source, IList<string> probes)
        {
            if (source == null || probes == null) return false;
            for (int i = 0; i < probes.Count; i++)
            {
                if (source.Contains(probes[i])) return true;
            }
            return false;
        }

        private static int Add(
            List<ModeHOddsBreakdownEntry> breakdown, bool isEnemySide, string labelSuffix, int value)
        {
            if (breakdown != null)
            {
                ModeHOddsBreakdownEntry entry = new ModeHOddsBreakdownEntry();
                entry.LabelKey = ModeHConfig.LocalizationKeyPrefix + labelSuffix;
                entry.Value = value;
                entry.IsEnemySide = isEnemySide;
                breakdown.Add(entry);
            }
            return value;
        }

        #endregion

        #region 自检

        /// <summary>
        /// 用 OddsWeights.json 的三个冻结测试向量自检档位映射。
        /// 任一向量不符即内容不可用（由入口 fail-closed）。
        /// </summary>
        public static bool VerifyTestVectors(out string failureReasonId)
        {
            failureReasonId = null;
            List<ModeHOddsTestVector> vectors = ModeHContentCatalog.OddsTestVectors;
            if (vectors == null || vectors.Count == 0)
            {
                failureReasonId = "odds_test_vectors_missing";
                return false;
            }
            for (int i = 0; i < vectors.Count; i++)
            {
                ModeHOddsTestVector vector = vectors[i];
                if (vector == null) continue;
                int edge = vector.PlayerPublicScore - vector.EnemyPublicScore;
                int odds = ModeHStateModel.ResolveOddsTier(edge);
                if (odds != vector.ExpectedOdds)
                {
                    failureReasonId = "odds_test_vector_mismatch:" + vector.VectorId;
                    return false;
                }
            }
            return true;
        }

        #endregion
    }
}

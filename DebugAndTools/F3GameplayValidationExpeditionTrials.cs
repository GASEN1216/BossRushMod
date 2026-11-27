// ============================================================================
// F3GameplayValidationExpeditionTrials.cs - 出征令用例的判定：一局「单挑 · 困难 · 短征」的核对，
// 以及战帖局内机制开关、词缀条数、血债补丁是否装上的同步核对
// ============================================================================
// 2026-11-27 从 F3GameplayValidationWishes.cs 原样挪出（LargeFileBudgetGuard 1200 行），用例登记与 Record 仍在那边；
// 本文件只放返回失败原因的判定函数（CR-2026-11-27-001）。
// ============================================================================

#if BOSSRUSH_DEV
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>各档在局内开哪些机制：轻松 / 标准只换天色；困难 / 噩梦的词缀、厄运与四条常驻机制按悬赏令。</summary>
        private static string WishTrialProfileMismatch(ArenaExpeditionTier tier, ArenaTrialProfile profile)
        {
            if (profile == null) return "missing";
            bool hard = tier == ArenaExpeditionTier.Hard;
            bool nightmare = tier == ArenaExpeditionTier.Nightmare;
            if (!hard && !nightmare)
            {
                bool skiesOnly = profile.Skies && !profile.HasOmens && !profile.HasHazards && !profile.HasAffixes && profile.CurseLevel == 0
                    && !profile.Blind && !profile.CutSupply && !profile.BloodDebt && !profile.FireRing;
                return skiesOnly ? null : "not_skies_only";
            }
            if (profile.Skies || !profile.HasOmens || !profile.HasHazards || !profile.AffixDistinctKinds) return "mechanics";
            if (profile.Affixes != (hard ? 1 : 3) || profile.FinaleAffixes != (hard ? 2 : 4)) return "affixes";
            if (profile.CurseLevel != (hard ? 1 : 2)) return "curse";
            if (profile.Blind != nightmare || profile.CutSupply != nightmare || profile.BloodDebt != nightmare || profile.FireRing != nightmare)
            {
                return "nightmare_only";
            }
            if ((profile.HazardDoubleChance > 0f) != nightmare) return "double_hazard";
            return null;
        }

        /// <summary>两份局内配置的机制是否一模一样（不比较对象身份）。</summary>
        private static bool WishSameMechanics(ArenaTrialProfile a, ArenaTrialProfile b)
        {
            if (a == null || b == null) return false;
            return a.Skies == b.Skies && a.OmenWavesMin == b.OmenWavesMin && a.OmenWavesMax == b.OmenWavesMax
                && WishNear(a.HazardSecondsMin, b.HazardSecondsMin) && WishNear(a.HazardSecondsMax, b.HazardSecondsMax)
                && WishNear(a.HazardDoubleChance, b.HazardDoubleChance)
                && a.Affixes == b.Affixes && a.FinaleAffixes == b.FinaleAffixes && a.AffixDistinctKinds == b.AffixDistinctKinds
                && a.CurseLevel == b.CurseLevel && a.Blind == b.Blind && a.CutSupply == b.CutSupply
                && a.BloodDebt == b.BloodDebt && a.FireRing == b.FireRing;
        }

        /// <summary>
        /// 加码（2026-11-27）：标准 5 级 / 困难 8 级、轻松噩梦不加；加满后局内机制与下一档相同、共享配置不被改；
        /// 逐级生效的节点（第 1 级起变天、标准第 3 级急行军、困难第 2 级失明）；每级取的战规就在下一档的表里；
        /// 困难加满时悬赏令略去 4 条被换掉的、共 10 条与噩梦同数；奖励每级 +5%；厄运池 8 种都有名字。
        /// </summary>
        private static string WishRaiseMismatch()
        {
            int[] max = { 0, 5, 8, 0 };
            for (int i = 0; i < 4; i++)
            {
                if (ArenaExpeditionRaise.MaxFor((ArenaExpeditionTier)i) != max[i]) return "max_" + (ArenaExpeditionTier)i;
            }
            if (ArenaExpeditionRaise.Clamp(ArenaExpeditionTier.Standard, 99) != 5 || ArenaExpeditionRaise.Clamp(ArenaExpeditionTier.Easy, 3) != 0)
            {
                return "clamp";
            }
            ArenaTrialProfile standardFull = ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Standard, 5);
            ArenaTrialProfile hardFull = ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard, 8);
            if (!WishSameMechanics(standardFull, ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard))) return "standard5_not_hard";
            if (!WishSameMechanics(hardFull, ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Nightmare))) return "hard8_not_nightmare";
            if (ReferenceEquals(standardFull, ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Standard))
                || !ReferenceEquals(ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard, 0), ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard)))
            {
                return "copy_semantics";
            }
            if (WishTrialProfileMismatch(ArenaExpeditionTier.Standard, ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Standard)) != null
                || WishTrialProfileMismatch(ArenaExpeditionTier.Hard, ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard)) != null)
            {
                return "shared_profile_mutated";
            }
            ArenaTrialProfile standardOne = ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Standard, 1);
            if (standardOne.Skies || !standardOne.HasOmens || standardOne.HasHazards || standardOne.CurseLevel != 0) return "standard1_progression";
            if (!WishNear(ArenaExpeditionTuning.RestMultiplier(ArenaExpeditionTier.Standard, 2), 1f)
                || !ArenaExpeditionTuning.KeepsMilestoneRest(ArenaExpeditionTier.Standard, 2)
                || !WishNear(ArenaExpeditionTuning.RestMultiplier(ArenaExpeditionTier.Standard, 3), 0.5f)
                || ArenaExpeditionTuning.KeepsMilestoneRest(ArenaExpeditionTier.Standard, 3))
            {
                return "forced_march_rung";
            }
            if (ArenaExpeditionTuning.HidesMapBeacons(ArenaExpeditionTier.Hard, 1) || !ArenaExpeditionTuning.HidesMapBeacons(ArenaExpeditionTier.Hard, 2))
            {
                return "blinded_rung";
            }
            for (int t = 1; t <= 2; t++)
            {
                ArenaExpeditionTier tier = (ArenaExpeditionTier)t;
                IList<ArenaExpeditionRule> next = ArenaExpeditionTuning.RulesFor((ArenaExpeditionTier)(t + 1));
                IList<ArenaExpeditionRung> ladder = ArenaExpeditionRaise.LadderFor(tier);
                for (int r = 0; r < ladder.Count; r++)
                {
                    ArenaExpeditionRule rule = ArenaExpeditionTuning.RaiseRule(ladder[r]);
                    if (rule == null || !next.Contains(rule)) return "rule_" + ladder[r] + "_not_in_" + (ArenaExpeditionTier)(t + 1);
                }
            }
            int shown = 0;
            IList<ArenaExpeditionRule> hardRules = ArenaExpeditionTuning.RulesFor(ArenaExpeditionTier.Hard);
            for (int i = 0; i < hardRules.Count; i++)
            {
                if (!ArenaExpeditionTuning.IsSuperseded(ArenaExpeditionTier.Hard, 8, hardRules[i])) shown++;
            }
            if (shown + 8 != ArenaExpeditionTuning.RulesFor(ArenaExpeditionTier.Nightmare).Count) return "hard8_notice_count_" + (shown + 8);
            if (ArenaExpeditionTuning.VictoryChestCount(10, ArenaExpeditionTier.Hard, 40, 40, 8) != 14
                || ArenaExpeditionTuning.VictoryChestCount(10, ArenaExpeditionTier.Standard, 40, 40, 0) != 10
                || ArenaExpeditionTuning.VictoryChestCount(10, ArenaExpeditionTier.Easy, 40, 40, 5) != 5
                || !WishNear(ArenaExpeditionTuning.HellCashMultiplier(ArenaExpeditionTier.Hard, 8), 1.75f))
            {
                return "reward";
            }
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int c = 1; c <= ArenaTrialsTuning.CurseKinds; c++)
            {
                string name = ArenaTrialsTuning.CurseName((ArenaTrialCurse)c);
                if (string.IsNullOrEmpty(name) || !names.Add(name)) return "curse_name_" + c;
            }
            if (ArenaTrialsTuning.CurseKinds != 8) return "curse_kinds_" + ArenaTrialsTuning.CurseKinds;
            return null;
        }

        /// <summary>
        /// 词缀条数与悬赏令一致（每种抽 40 次）：普通 Boss 不同类；内容 Boss 只有身法 / 死相，噩梦 3 条、压轴 4 条也要抽满，
        /// 额外掉落随条数（1–2 条多 1 件、3–4 条多 2 件）。
        /// </summary>
        private static string WishAffixRollMismatch()
        {
            List<ArenaAffix> affixes = new List<ArenaAffix>(4);
            List<ArenaAffixKind> scratch = new List<ArenaAffixKind>(4);
            int[] counts = { 1, 2, 3, 4 };
            for (int round = 0; round < 40; round++)
            {
                for (int c = 0; c < counts.Length; c++)
                {
                    for (int preset = 0; preset < 2; preset++)
                    {
                        bool hasPreset = preset == 1;
                        ArenaTrialsAffixes.RollInto(affixes, scratch, hasPreset, counts[c], true);
                        string tag = (hasPreset ? "regular" : "content") + counts[c];
                        if (affixes.Count != counts[c]) return tag + "_count_" + affixes.Count;
                        HashSet<ArenaAffix> seen = new HashSet<ArenaAffix>(affixes);
                        if (seen.Count != affixes.Count) return tag + "_duplicate";
                        HashSet<ArenaAffixKind> kinds = new HashSet<ArenaAffixKind>();
                        for (int i = 0; i < affixes.Count; i++)
                        {
                            ArenaAffixKind kind = ArenaTrialsTuning.KindOf(affixes[i]);
                            kinds.Add(kind);
                            if (!hasPreset && kind != ArenaAffixKind.Motion && kind != ArenaAffixKind.Death) return tag + "_body_affix_on_content_boss";
                        }
                        int wantKinds = Math.Min(counts[c], hasPreset ? 4 : 2);
                        if (kinds.Count != wantKinds) return tag + "_kinds_" + kinds.Count;
                        if (ArenaTrialsTuning.AffixBonusLoot(affixes.Count) != (counts[c] >= 3 ? 2 : 1)) return tag + "_bonus_loot";
                    }
                }
            }
            return null;
        }

        /// <summary>某个官方方法上是否挂着本 Mod 指定的前缀补丁。</summary>
        private static string DescribeWishPrefix(Type target, string method, Type patchType)
        {
            try
            {
                MethodBase original = HarmonyLib.AccessTools.Method(target, method);
                if (original == null) return "target_missing";
                HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(original);
                if (info == null) return "no_patches";
                foreach (HarmonyLib.Patch patch in info.Prefixes)
                {
                    if (patch != null && patch.PatchMethod != null && patch.PatchMethod.DeclaringType == patchType) return "installed";
                }
                return "prefixes_" + info.Prefixes.Count + "_without_ours";
            }
            catch (Exception e)
            {
                return "error_" + e.GetType().Name;
            }
        }

        private string JudgeArenaExpeditionRun(List<EnemyPresetInfo> pool, CharacterMainControl boss, out string metrics)
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null)
            {
                metrics = "active=" + _host.IsActive;
                return "expedition_run_missing";
            }
            int expectedWaves = ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Short, run.PoolCount);
            bool queueExpected = expectedWaves >= 2 && expectedWaves < run.PoolCount;
            bool orderOk = run.Order.Lineup == ArenaExpeditionLineup.Single && run.Order.Tier == ArenaExpeditionTier.Hard
                && run.Order.Length == ArenaExpeditionLength.Short;
            bool queueOk;
            bool finaleOk = true;
            string finaleName = run.Finale != null ? run.Finale.name : "none";
            bool finaleStrong = run.Finale != null && WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(run.Finale.name);
            if (!queueExpected) queueOk = !run.HasQueue;
            else
            {
                queueOk = run.HasQueue && run.Queue.Count == expectedWaves && run.Finale != null
                    && ReferenceEquals(run.Queue[run.Queue.Count - 1], run.Finale) && run.IsFinaleWave(expectedWaves - 1);
                // 前面的波里没用到的启用 Boss 中有强 Boss，压轴就必须是强 Boss；一只都没有时退到血最厚的。
                HashSet<string> usedBeforeFinale = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; queueOk && i < run.Queue.Count - 1; i++)
                {
                    if (run.Queue[i] != null && run.Queue[i].name != null) usedBeforeFinale.Add(run.Queue[i].name);
                }
                bool strongAvailable = false;
                for (int i = 0; queueOk && pool != null && i < pool.Count; i++)
                {
                    EnemyPresetInfo preset = pool[i];
                    if (preset == null || preset.name == null || usedBeforeFinale.Contains(preset.name)) continue;
                    if (WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(preset.name)) strongAvailable = true;
                }
                for (int i = 0; queueOk && i < run.Queue.Count - 1; i++)
                {
                    if (ReferenceEquals(run.Queue[i], run.Finale)) queueOk = false;
                }
                finaleOk = !strongAvailable || finaleStrong;
            }
            float factor = boss != null ? ArenaExpeditionBossScaling.HealthFactorOf(boss) : 0f;
            bool scaled = WishNear(factor, ArenaExpeditionTuning.HealthMultiplier(ArenaExpeditionTier.Hard), 0.01f);
            GameObject hudRoot = GameObject.Find(WishExpeditionHudName);
            Canvas hudCanvas = hudRoot != null ? hudRoot.GetComponent<Canvas>() : null;
            bool hudShown = hudCanvas != null && (hudCanvas.enabled || BossRushUI.IsOfficialHudHidden());
            // 困难战规全是竞技场自己的机制（2026-11-25 机制化，不再借变异词条），出征令开的局也不抽随机词条：
            // 场上一条变异词条都不该有（CR-2026-11-27-001）。
            IReadOnlyList<MutatorDefinition> activeMutators = MutatorManager.GetActiveMutators();
            // 局内战规：困难档开波就定下天象和这一波的轻诅咒，首领登记时抽 1 条词缀（压轴 2 条）、名缀写进血条
            ArenaTrials trials = ArenaTrials.Active;
            bool trialsOn = trials != null && trials.Tier == ArenaExpeditionTier.Hard;
            ArenaTrialOmen omen = trials != null && trials.Sky != null ? trials.Sky.Omen : ArenaTrialOmen.None;
            ArenaTrialCurse curse = trials != null && trials.Curses != null ? trials.Curses.Current : ArenaTrialCurse.None;
            string prefix = boss != null ? ArenaTrialsAffixes.NamePrefixFor(boss.Health) : null;
            int bonus = trials != null && trials.Affixes != null && boss != null ? trials.Affixes.BonusLootFor(boss) : -1;
            metrics = "active=" + _host.IsActive + ",pool=" + run.PoolCount + ",waves=" + run.Waves + ",expected=" + expectedWaves
                + ",queue=" + run.HasQueue + ",finale=" + finaleName + ",finale_strong=" + finaleStrong
                + ",boss=" + (boss != null ? boss.name : "none") + ",hp_factor=" + factor.ToString("0.00", CultureInfo.InvariantCulture)
                + ",seal_hud=" + hudShown + ",active_mutators=" + activeMutators.Count
                + ",trials=" + (trials != null ? trials.Tier.ToString() : "none") + ",omen=" + omen + ",curse=" + curse
                + ",affix_prefix=" + !string.IsNullOrEmpty(prefix) + ",affix_bonus=" + bonus;
            if (!orderOk) return "order_not_taken";
            if (!queueOk) return queueExpected ? "short_queue_wrong" : "queue_built_when_pool_small";
            if (!finaleOk) return "finale_not_strong";
            if (boss == null) return "boss_not_spawned";
            if (!scaled) return "hard_health_factor_missing";
            if (!hudShown) return "expedition_seal_missing";
            if (activeMutators.Count != 0) return "mutators_rolled_in_expedition";
            if (!trialsOn) return "hard_trials_missing";
            if (omen == ArenaTrialOmen.None) return "hard_omen_missing";
            if (curse == ArenaTrialCurse.None) return "hard_curse_missing";
            if (string.IsNullOrEmpty(prefix)) return "boss_affix_missing";
            if (bonus != 1) return "affix_bonus_loot_wrong";
            return null;
        }
    }
}
#endif

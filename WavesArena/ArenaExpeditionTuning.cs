// ============================================================================
// ArenaExpeditionTuning.cs - 出征令（竞技场 阵容 × 战帖 × 征程）的数值、战规与文案
// ============================================================================
// 2026-11-24 玩家许愿「难度档位」「波数太长」，owner 拍板：
//   - 战帖四档只作用于竞技场 Legacy 波次（弹指可灭 / 有点意思 / 无间炼狱），按局在出征令上选，
//     不注册 ModConfig，叠乘在 ModConfig 全局 Boss 倍率（BossRush_BossStatMultiplier / BossHealthPercent）之上；
//   - 每只 Boss 自带的掉落不随战帖变：掉落按「未乘战帖」的生命算（ArenaExpeditionBossScaling.HealthFactorOf）；
//   - 短征 / 长征的最后一波固定压轴强 Boss；通关箱件数 = ceil(基数 × √(L / 全池数)) 再乘战帖；
//   - 旧通关 / 无伤 / 速通 / 累计成就只认「全征 + 标准及以上」，无间成就只认「标准及以上」；
//     噩梦通关另有成就，只认全征。
//
// 2026-11-25 owner：「难度梯度多元化，参考杀戮尖塔等肉鸽，包括但不限于对玩家的削弱、中立机制、场景限制」。
//   每档战帖改成一叠「战规」，像杀戮尖塔的进阶一样逐档叠上去，分四类印：
//     首领（敌人更强）· 己身（玩家被削弱）· 战场（场地 / 节奏限制）· 天时（中立机制：对谁都一样，看你怎么用）。
//   轻松：三条宽限（首领收手、治疗 +50%、休整更长）；标准：不加；
//   困难：五条（首领磨刀、神枪敌军、伤口难愈、急行军、嗜血）；
//   噩梦：八条（首领杀心、神枪敌军、Boss 回春、伤口难愈、急行军、盲战、血之契约、天降殉爆）。
//   借用的战规直接用共享变异词条（MutatorPool）的现成效果与文案，由 MutatorManager 作为「战帖指定词条」先上、
//   不占随机条数、同互斥组退出随机候选；休整倍率与盲战由竞技场自己实现（WavesArenaRuntimeModule_Expedition）。
// 数值理由：
//   - 难度改由机制叠出来，生命 / 伤害倍率随之回落（困难 1.4/1.25 → 1.3/1.15，噩梦 2.0/1.5 → 1.6/1.3）：
//     噩梦叠了 Boss 回春，生命再 ×2 会变成磨血墙，杀戮尖塔的高进阶也是「更多事」而不是「更多血」。
//   - 噩梦的己身是「伤口难愈 + 血之契约」：伤药只剩三成，亲手击杀回 16%——逼人压上去打，和 Boss 回春互相咬住。
//   - 轻松的宽限不碰掉落，只让手残党能看到后半池；奖励仍按原规则减半。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal enum ArenaExpeditionLineup
    {
        Single = 0,
        Triple = 1,
        Hell = 2,
    }

    internal enum ArenaExpeditionTier
    {
        Easy = 0,
        Standard = 1,
        Hard = 2,
        Nightmare = 3,
    }

    internal enum ArenaExpeditionLength
    {
        Short = 0,
        Long = 1,
        Full = 2,
    }

    /// <summary>战规的四类印。</summary>
    internal enum ArenaExpeditionRuleKind
    {
        /// <summary>首领：敌人更强。</summary>
        Foe = 0,
        /// <summary>己身：玩家被削弱（轻松档是宽限）。</summary>
        Self = 1,
        /// <summary>战场：场地与节奏的限制。</summary>
        Field = 2,
        /// <summary>天时：中立机制，对谁都一样。</summary>
        Omen = 3,
    }

    /// <summary>
    /// 一条战规。借用变异词条的（<see cref="MutatorId"/> 非空）名字与说明直接读 MutatorPool，文案只写一处；
    /// 竞技场自己实现的写在这里。
    /// </summary>
    internal sealed class ArenaExpeditionRule
    {
        internal readonly ArenaExpeditionRuleKind Kind;
        /// <summary>宽限（轻松档对玩家有利的那几条），悬赏令上用金印。</summary>
        internal readonly bool Grace;
        internal readonly string MutatorId;
        private readonly string nameCn, nameEn, lineCn, lineEn;

        private ArenaExpeditionRule(ArenaExpeditionRuleKind kind, bool grace, string mutatorId,
            string nameCn, string nameEn, string lineCn, string lineEn)
        {
            Kind = kind;
            Grace = grace;
            MutatorId = mutatorId;
            this.nameCn = nameCn;
            this.nameEn = nameEn;
            this.lineCn = lineCn;
            this.lineEn = lineEn;
        }

        internal static ArenaExpeditionRule Own(ArenaExpeditionRuleKind kind, bool grace, string nameCn, string nameEn, string lineCn, string lineEn)
        {
            return new ArenaExpeditionRule(kind, grace, null, nameCn, nameEn, lineCn, lineEn);
        }

        internal static ArenaExpeditionRule Mutator(ArenaExpeditionRuleKind kind, bool grace, string mutatorId)
        {
            return new ArenaExpeditionRule(kind, grace, mutatorId, null, null, null, null);
        }

        internal string Name
        {
            get
            {
                MutatorDefinition definition = Definition();
                return definition != null ? definition.GetDisplayName() : L10n.T(nameCn ?? MutatorId, nameEn ?? MutatorId);
            }
        }

        internal string Line
        {
            get
            {
                MutatorDefinition definition = Definition();
                return definition != null ? definition.GetDescription() : L10n.T(lineCn ?? string.Empty, lineEn ?? string.Empty);
            }
        }

        internal MutatorDefinition Definition()
        {
            if (string.IsNullOrEmpty(MutatorId)) return null;
            MutatorDefinition[] all = MutatorPool.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].Id, MutatorId, StringComparison.Ordinal)) return all[i];
            }
            return null;
        }
    }

    internal static class ArenaExpeditionTuning
    {
        internal const int ShortWaves = 12;
        internal const int LongWaves = 24;
        internal const int TripleBossesPerWave = 3;

        /// <summary>噩梦通关成就 ID（Achievement/BossRushAchievementManager 登记）。</summary>
        internal const string NightmareClearAchievementId = "nightmare_clear";

        // 噩梦 2026-11-25 owner：「噩梦加的倍率不是很多啊，你可以大胆加加」→ 生命 ×2.5、伤害 ×1.75、出手快五成，
        // 无间奖金跟着 ×2（打得更久，按分钟算不该比标准少）。困难不动。
        private static readonly float[] HealthMultipliers = { 0.6f, 1f, 1.3f, 2.5f };
        private static readonly float[] DamageMultipliers = { 0.7f, 1f, 1.15f, 1.75f };
        private static readonly float[] ReactionDivisors = { 1f, 1f, 1.15f, 1.5f };
        private static readonly float[] HellCashMultipliers = { 0.5f, 1f, 1.25f, 2f };
        /// <summary>波间休整倍率：轻松从容 ×1.5，困难 / 噩梦急行军 ×0.5（每 5 波的额外休整也取消）。</summary>
        private static readonly float[] RestMultipliers = { 1.5f, 1f, 0.5f, 0.5f };

        internal static float HealthMultiplier(ArenaExpeditionTier tier) { return HealthMultipliers[Index(tier)]; }
        internal static float DamageMultiplier(ArenaExpeditionTier tier) { return DamageMultipliers[Index(tier)]; }
        internal static float ReactionDivisor(ArenaExpeditionTier tier) { return ReactionDivisors[Index(tier)]; }
        internal static float HellCashMultiplier(ArenaExpeditionTier tier) { return HellCashMultipliers[Index(tier)]; }
        internal static float RestMultiplier(ArenaExpeditionTier tier) { return RestMultipliers[Index(tier)]; }

        /// <summary>每 5 波的额外休整是否保留（急行军取消它）。</summary>
        internal static bool KeepsMilestoneRest(ArenaExpeditionTier tier) { return tier <= ArenaExpeditionTier.Standard; }

        /// <summary>盲战：M 图上不显示竞技场首领的烽烟圈。</summary>
        internal static bool HidesMapBeacons(ArenaExpeditionTier tier) { return tier == ArenaExpeditionTier.Nightmare; }

        /// <summary>战帖是否改动 Boss 数值（标准档不挂任何东西）。</summary>
        internal static bool ScalesBosses(ArenaExpeditionTier tier) { return tier != ArenaExpeditionTier.Standard; }

        // ------------------------------------------------------------------
        // 战规（逐档叠上去；顺序就是悬赏令上的顺序：首领 → 己身 → 战场 → 天时）
        // ------------------------------------------------------------------

        private static readonly ArenaExpeditionRule[] EasyRules =
        {
            ArenaExpeditionRule.Own(ArenaExpeditionRuleKind.Foe, true, "收手", "Holding Back",
                "首领生命 " + FormatMultiplier(HealthMultipliers[0]) + "，伤害 " + FormatMultiplier(DamageMultipliers[0]),
                "Boss HP " + FormatMultiplier(HealthMultipliers[0]) + ", damage " + FormatMultiplier(DamageMultipliers[0])),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Self, true, "player_swift_heal"),
            ArenaExpeditionRule.Own(ArenaExpeditionRuleKind.Field, true, "从容", "At Ease",
                "波间休整 " + FormatMultiplier(RestMultipliers[0]), "Rest between waves " + FormatMultiplier(RestMultipliers[0])),
        };

        private static readonly ArenaExpeditionRule[] StandardRules = new ArenaExpeditionRule[0];

        private static readonly ArenaExpeditionRule ForcedMarch = ArenaExpeditionRule.Own(ArenaExpeditionRuleKind.Field, false,
            "急行军", "Forced March", "波间休整减半，每 5 波的额外休整取消", "Rest between waves halved; no extra rest every 5 waves");

        private static readonly ArenaExpeditionRule[] HardRules =
        {
            ArenaExpeditionRule.Own(ArenaExpeditionRuleKind.Foe, false, "磨刀", "Whetted Blades",
                "首领生命 " + FormatMultiplier(HealthMultipliers[2]) + "，伤害 " + FormatMultiplier(DamageMultipliers[2]) + "，出手快一成半",
                "Boss HP " + FormatMultiplier(HealthMultipliers[2]) + ", damage " + FormatMultiplier(DamageMultipliers[2]) + ", 15% quicker to act"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Foe, false, "enemy_sharpshooter"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Self, false, "heal_reduced"),
            ForcedMarch,
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Omen, false, "lifesteal_on_kill"),
        };

        private static readonly ArenaExpeditionRule[] NightmareRules =
        {
            ArenaExpeditionRule.Own(ArenaExpeditionRuleKind.Foe, false, "杀心", "Killing Intent",
                "首领生命 " + FormatMultiplier(HealthMultipliers[3]) + "，伤害 " + FormatMultiplier(DamageMultipliers[3]) + "，出手快五成",
                "Boss HP " + FormatMultiplier(HealthMultipliers[3]) + ", damage " + FormatMultiplier(DamageMultipliers[3]) + ", 50% quicker to act"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Foe, false, "enemy_sharpshooter"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Foe, false, "boss_regen"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Self, false, "heal_reduced"),
            ForcedMarch,
            ArenaExpeditionRule.Own(ArenaExpeditionRuleKind.Field, false, "盲战", "Fighting Blind",
                "M 图上不显示首领的烽烟圈，要自己找", "No boss beacons on the map; find them yourself"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Omen, false, "blood_pact"),
            ArenaExpeditionRule.Mutator(ArenaExpeditionRuleKind.Omen, false, "explode_on_death"),
        };

        internal static IList<ArenaExpeditionRule> RulesFor(ArenaExpeditionTier tier)
        {
            switch (tier)
            {
                case ArenaExpeditionTier.Easy: return EasyRules;
                case ArenaExpeditionTier.Hard: return HardRules;
                case ArenaExpeditionTier.Nightmare: return NightmareRules;
                default: return StandardRules;
            }
        }

        /// <summary>这档战帖指定的变异词条（开波时交给 MutatorManager 先上）。标准档返回 null。</summary>
        internal static List<string> ForcedMutatorIds(ArenaExpeditionTier tier)
        {
            IList<ArenaExpeditionRule> rules = RulesFor(tier);
            List<string> ids = null;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null || string.IsNullOrEmpty(rules[i].MutatorId)) continue;
                if (ids == null) ids = new List<string>();
                if (!ids.Contains(rules[i].MutatorId)) ids.Add(rules[i].MutatorId);
            }
            return ids;
        }

        /// <summary>征程长度对应的波数；全征返回启用池大小。</summary>
        internal static int WavesFor(ArenaExpeditionLength length, int poolCount)
        {
            if (length == ArenaExpeditionLength.Short) return Math.Min(ShortWaves, poolCount);
            if (length == ArenaExpeditionLength.Long) return Math.Min(LongWaves, poolCount);
            return poolCount;
        }

        /// <summary>
        /// 通关箱件数：ceil(基数 × √(L / 全池数))，再按战帖：轻松减半向上取整、至少 1，噩梦 ×1.5 向上取整。
        /// 开根号让短征拿得不至于太寒碜：12 / 40 波约拿到 55%。
        /// </summary>
        internal static int VictoryChestCount(int baseCount, ArenaExpeditionTier tier, int waves, int poolCount)
        {
            if (baseCount <= 0) return 0;
            int count = baseCount;
            if (poolCount > 0 && waves > 0 && waves < poolCount)
            {
                count = (int)Math.Ceiling(baseCount * Math.Sqrt(waves / (double)poolCount));
            }
            if (tier == ArenaExpeditionTier.Easy) count = (int)Math.Ceiling(count * 0.5);
            else if (tier == ArenaExpeditionTier.Nightmare) count = (int)Math.Ceiling(count * 1.5);
            return Math.Max(1, count);
        }

        /// <summary>通关箱品质下限：困难、噩梦从 6 档起。</summary>
        internal static int VictoryQualityMin(ArenaExpeditionTier tier, int baseMin)
        {
            return tier >= ArenaExpeditionTier.Hard ? Math.Max(baseMin, 6) : baseMin;
        }

        private static int Index(ArenaExpeditionTier tier)
        {
            int index = (int)tier;
            return index < 0 || index > 3 ? 1 : index;
        }

        // ------------------------------------------------------------------
        // 文案（取用时解析语言）
        // ------------------------------------------------------------------

        internal static string LineupName(ArenaExpeditionLineup lineup)
        {
            switch (lineup)
            {
                case ArenaExpeditionLineup.Triple: return L10n.T("有点意思", "Getting Interesting");
                case ArenaExpeditionLineup.Hell: return L10n.T("无间炼狱", "Infinite Hell");
                default: return L10n.T("弹指可灭", "A Snap");
            }
        }

        internal static string LineupNameEn(ArenaExpeditionLineup lineup)
        {
            switch (lineup)
            {
                case ArenaExpeditionLineup.Triple: return "Getting Interesting";
                case ArenaExpeditionLineup.Hell: return "Infinite Hell";
                default: return "A Snap";
            }
        }

        internal static string LineupLine(ArenaExpeditionLineup lineup)
        {
            switch (lineup)
            {
                case ArenaExpeditionLineup.Triple: return L10n.T("每波 3 只", "3 per wave");
                case ArenaExpeditionLineup.Hell: return L10n.T("波波不止", "No last wave");
                default: return L10n.T("每波 1 只", "1 per wave");
            }
        }

        /// <summary>战帖名（中文两字，印章用）。</summary>
        internal static string TierSeal(ArenaExpeditionTier tier)
        {
            switch (tier)
            {
                case ArenaExpeditionTier.Easy: return "轻松";
                case ArenaExpeditionTier.Hard: return "困难";
                case ArenaExpeditionTier.Nightmare: return "噩梦";
                default: return "标准";
            }
        }

        internal static string TierName(ArenaExpeditionTier tier)
        {
            switch (tier)
            {
                case ArenaExpeditionTier.Easy: return L10n.T("轻松", "Relaxed");
                case ArenaExpeditionTier.Hard: return L10n.T("困难", "Hard");
                case ArenaExpeditionTier.Nightmare: return L10n.T("噩梦", "Nightmare");
                default: return L10n.T("标准", "Standard");
            }
        }

        internal static string TierNameEn(ArenaExpeditionTier tier)
        {
            switch (tier)
            {
                case ArenaExpeditionTier.Easy: return "Relaxed";
                case ArenaExpeditionTier.Hard: return "Hard";
                case ArenaExpeditionTier.Nightmare: return "Nightmare";
                default: return "Standard";
            }
        }

        /// <summary>战帖卡上的一行：几条战规（轻松是几条宽限）。</summary>
        internal static string TierCardLine(ArenaExpeditionTier tier)
        {
            int count = RulesFor(tier).Count;
            if (count == 0) return L10n.T("不加战规", "No extra rules");
            if (tier == ArenaExpeditionTier.Easy) return L10n.T("宽限 " + count + " 条", count + " graces");
            return L10n.T("战规 " + count + " 条", count + " rules");
        }

        /// <summary>悬赏令上战规的类别印（中文两字）。</summary>
        internal static string KindSeal(ArenaExpeditionRuleKind kind)
        {
            switch (kind)
            {
                case ArenaExpeditionRuleKind.Self: return "己身";
                case ArenaExpeditionRuleKind.Field: return "战场";
                case ArenaExpeditionRuleKind.Omen: return "天时";
                default: return "首领";
            }
        }

        /// <summary>英文界面下的类别标签（印里放不下英文，改成一行朱字）。</summary>
        internal static string KindLabelEn(ArenaExpeditionRuleKind kind)
        {
            switch (kind)
            {
                case ArenaExpeditionRuleKind.Self: return "SELF";
                case ArenaExpeditionRuleKind.Field: return "FIELD";
                case ArenaExpeditionRuleKind.Omen: return "OMEN";
                default: return "FOES";
            }
        }

        /// <summary>悬赏令标题下那行：阵容 · 每波几只 · 征程。</summary>
        internal static string CourseLine(ArenaExpeditionLineup lineup, ArenaExpeditionLength length, int poolCount)
        {
            string head = LineupName(lineup) + " · " + LineupLine(lineup);
            if (lineup == ArenaExpeditionLineup.Hell) return head;
            int waves = WavesFor(length, poolCount);
            if (length == ArenaExpeditionLength.Full || waves >= poolCount)
            {
                return head + " · " + L10n.T("全征 " + waves + " 波", "full " + waves + " waves");
            }
            return head + " · " + LengthName(length) + " " + L10n.T(waves + " 波，末波压轴", waves + " waves, headliner last");
        }

        /// <summary>悬赏令「赏格」一栏：每行一句，第二项为 true 的是吃亏的那句（朱字）。</summary>
        internal static List<KeyValuePair<string, bool>> RewardLines(ArenaExpeditionTier tier, ArenaExpeditionLineup lineup,
            ArenaExpeditionLength length, int poolCount)
        {
            List<KeyValuePair<string, bool>> lines = new List<KeyValuePair<string, bool>>();
            if (lineup == ArenaExpeditionLineup.Hell)
            {
                string cash = FormatMultiplier(HellCashMultiplier(tier));
                lines.Add(new KeyValuePair<string, bool>(L10n.T("无间奖金 " + cash, "Hell cash " + cash), tier == ArenaExpeditionTier.Easy));
                lines.Add(tier == ArenaExpeditionTier.Easy
                    ? new KeyValuePair<string, bool>(L10n.T("不计无间成就", "No Hell achievements"), true)
                    : new KeyValuePair<string, bool>(L10n.T("计入无间成就", "Counts for Hell achievements"), false));
            }
            else
            {
                switch (tier)
                {
                    case ArenaExpeditionTier.Easy:
                        lines.Add(new KeyValuePair<string, bool>(L10n.T("通关箱件数减半，至少 1 件", "Half the victory chest, at least 1"), true));
                        break;
                    case ArenaExpeditionTier.Hard:
                        lines.Add(new KeyValuePair<string, bool>(L10n.T("通关箱品质 6 档起", "Victory chest quality 6+"), false));
                        break;
                    case ArenaExpeditionTier.Nightmare:
                        lines.Add(new KeyValuePair<string, bool>(L10n.T("通关箱多五成，品质 6 档起", "Victory chest +50%, quality 6+"), false));
                        break;
                    default:
                        lines.Add(new KeyValuePair<string, bool>(L10n.T("通关箱照旧", "Victory chest as usual"), false));
                        break;
                }
                int waves = WavesFor(length, poolCount);
                bool partial = length != ArenaExpeditionLength.Full && waves < poolCount;
                if (partial)
                {
                    lines.Add(new KeyValuePair<string, bool>(L10n.T("通关箱按 " + waves + " / " + poolCount + " 波折算",
                        "Victory chest scaled by " + waves + " / " + poolCount + " waves"), true));
                }
                if (tier == ArenaExpeditionTier.Easy || partial)
                {
                    lines.Add(new KeyValuePair<string, bool>(L10n.T("不计通关成就", "No clear achievements"), true));
                }
                else if (tier == ArenaExpeditionTier.Nightmare)
                {
                    lines.Add(new KeyValuePair<string, bool>(L10n.T("计入通关成就，另得「噩梦通关」", "Counts for clear achievements, plus Nightmare Clear"), false));
                }
                else
                {
                    lines.Add(new KeyValuePair<string, bool>(L10n.T("计入通关成就", "Counts for clear achievements"), false));
                }
            }
            lines.Add(new KeyValuePair<string, bool>(L10n.T("每只首领自带的掉落不变", "Each boss's own drops stay the same"), false));
            return lines;
        }

        internal static string LengthName(ArenaExpeditionLength length)
        {
            switch (length)
            {
                case ArenaExpeditionLength.Short: return L10n.T("短征", "Short March");
                case ArenaExpeditionLength.Long: return L10n.T("长征", "Long March");
                default: return L10n.T("全征", "Full Campaign");
            }
        }

        internal static string LengthNameEn(ArenaExpeditionLength length)
        {
            switch (length)
            {
                case ArenaExpeditionLength.Short: return "Short March";
                case ArenaExpeditionLength.Long: return "Long March";
                default: return "Full Campaign";
            }
        }

        internal static string LengthCardLine(ArenaExpeditionLength length, int poolCount)
        {
            int waves = WavesFor(length, poolCount);
            return L10n.T(waves + " 波", waves + " waves");
        }

        internal static string FormatMultiplier(float value)
        {
            return "×" + value.ToString(Mathf.Approximately(value, Mathf.Round(value)) ? "0" : "0.0#",
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

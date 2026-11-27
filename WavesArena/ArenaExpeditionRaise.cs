// ============================================================================
// ArenaExpeditionRaise.cs - 出征令「加码」：在所选战帖之上，像杀戮尖塔的进阶一样逐级背上下一档的战规
// ============================================================================
// 2026-11-27 owner：「第三批搞成杀戮尖塔那种，战规简单到难排序，玩家自己选择加码」。
//   - 标准可加 5 级（借困难的战规），困难可加 8 级（借噩梦的战规）；轻松是宽限档、噩梦已是顶档，都不加码。
//   - 加码是累进的：加到第 N 级就同时背上第 1..N 级；梯子由易到难排，第 N 级 = 第 N-1 级 + 这一条。
//     标准：天象无常 → 厄运 → 急行军 → 天灾 → 凶相；
//     困难：天翻地覆 → 失明 → 断补 → 天灾连环 → 厄运（重）→ 焚城 → 血债 → 凶煞。
//     排序理由：先给双刃的（天象有利有弊），再给能靠操作抵掉的（诅咒、节奏），最后给硬削弱（只能击杀回血、首领多词缀）。
//   - 标准 +5 的局内机制与困难相同、困难 +8 与噩梦相同，只差档位自带的首领倍率（磨刀 / 杀心）与噩梦标题幕；
//     F3 ARENA_EXPEDITION_TUNING 逐项核对。
//   - 奖励：每级通关箱件数与无间奖金 +5%（件数向上取整）；困难 +8 是 ×1.4，仍低于噩梦的 ×1.5（噩梦另有首领倍率）。
//   - 本地偏好（BossRushLocalPrefs，不进存档、不注册 ModConfig）记两样：每档上次加到几级，每档通关过的最高级（面板上标「最高」）。
// 规则文案不另写一份：ArenaExpeditionTuning.RaiseRule 直接取困难 / 噩梦战规表里的那一条；
// 局内机制由 ArenaTrialsTuning.ProfileFor(tier, raise) 调 ApplyTo 改一份副本，节奏（急行军）与地图（失明）走 ArenaExpeditionTuning 的重载。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>加码的一级：就是下一档战帖里的一条战规。</summary>
    internal enum ArenaExpeditionRung
    {
        FickleSkies = 0,
        Hex = 1,
        ForcedMarch = 2,
        Calamity = 3,
        FellAspect = 4,
        Upheaval = 5,
        Blinded = 6,
        CutSupply = 7,
        ChainedCalamities = 8,
        DireHex = 9,
        CityAblaze = 10,
        BloodDebt = 11,
        DreadAspect = 12,
    }

    internal static class ArenaExpeditionRaise
    {
        private const string PrefChosen = "arena.expedition.raise.";
        private const string PrefBest = "arena.expedition.raise_best.";

        /// <summary>每级加码的奖励增幅（通关箱件数、无间奖金）。</summary>
        internal const float RewardPerRung = 0.05f;

        private static readonly ArenaExpeditionRung[] StandardLadder =
        {
            ArenaExpeditionRung.FickleSkies, ArenaExpeditionRung.Hex, ArenaExpeditionRung.ForcedMarch,
            ArenaExpeditionRung.Calamity, ArenaExpeditionRung.FellAspect,
        };

        private static readonly ArenaExpeditionRung[] HardLadder =
        {
            ArenaExpeditionRung.Upheaval, ArenaExpeditionRung.Blinded, ArenaExpeditionRung.CutSupply,
            ArenaExpeditionRung.ChainedCalamities, ArenaExpeditionRung.DireHex, ArenaExpeditionRung.CityAblaze,
            ArenaExpeditionRung.BloodDebt, ArenaExpeditionRung.DreadAspect,
        };

        private static readonly ArenaExpeditionRung[] NoLadder = new ArenaExpeditionRung[0];

        /// <summary>这一档能加的梯子（由易到难）；轻松 / 噩梦是空的。</summary>
        internal static IList<ArenaExpeditionRung> LadderFor(ArenaExpeditionTier tier)
        {
            switch (tier)
            {
                case ArenaExpeditionTier.Standard: return StandardLadder;
                case ArenaExpeditionTier.Hard: return HardLadder;
                default: return NoLadder;
            }
        }

        internal static int MaxFor(ArenaExpeditionTier tier) { return LadderFor(tier).Count; }

        internal static int Clamp(ArenaExpeditionTier tier, int raise) { return Mathf.Clamp(raise, 0, MaxFor(tier)); }

        /// <summary>加到 raise 级时是否背着这一级。</summary>
        internal static bool Has(ArenaExpeditionTier tier, int raise, ArenaExpeditionRung rung)
        {
            IList<ArenaExpeditionRung> ladder = LadderFor(tier);
            int count = Clamp(tier, raise);
            for (int i = 0; i < count; i++)
            {
                if (ladder[i] == rung) return true;
            }
            return false;
        }

        /// <summary>通关箱件数 / 无间奖金的加码倍率。</summary>
        internal static float RewardFactor(int raise) { return 1f + RewardPerRung * Mathf.Max(0, raise); }

        /// <summary>
        /// 把前 raise 级的局内机制叠到 profile（必须是副本）上。数值取困难 / 噩梦那一档的现成配置，不另写一份。
        /// 急行军与失明的地图部分不在战规对象里（休整倒计时、烽烟圈来源各自读 ArenaExpeditionTuning 的重载）。
        /// </summary>
        internal static void ApplyTo(ArenaTrialProfile profile, ArenaExpeditionTier tier, int raise)
        {
            if (profile == null) return;
            ArenaTrialProfile hard = ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard);
            ArenaTrialProfile nightmare = ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Nightmare);
            IList<ArenaExpeditionRung> ladder = LadderFor(tier);
            int count = Clamp(tier, raise);
            for (int i = 0; i < count; i++)
            {
                switch (ladder[i])
                {
                    case ArenaExpeditionRung.FickleSkies: CopyOmens(profile, hard); break;
                    case ArenaExpeditionRung.Upheaval: CopyOmens(profile, nightmare); break;
                    case ArenaExpeditionRung.Hex: profile.CurseLevel = Mathf.Max(profile.CurseLevel, hard.CurseLevel); break;
                    case ArenaExpeditionRung.DireHex: profile.CurseLevel = Mathf.Max(profile.CurseLevel, nightmare.CurseLevel); break;
                    case ArenaExpeditionRung.Calamity: CopyHazards(profile, hard); break;
                    case ArenaExpeditionRung.ChainedCalamities: CopyHazards(profile, nightmare); break;
                    case ArenaExpeditionRung.FellAspect: CopyAffixes(profile, hard); break;
                    case ArenaExpeditionRung.DreadAspect: CopyAffixes(profile, nightmare); break;
                    case ArenaExpeditionRung.Blinded: profile.Blind = true; break;
                    case ArenaExpeditionRung.CutSupply: profile.CutSupply = true; break;
                    case ArenaExpeditionRung.CityAblaze: profile.FireRing = true; break;
                    case ArenaExpeditionRung.BloodDebt: profile.BloodDebt = true; break;
                }
            }
        }

        private static void CopyOmens(ArenaTrialProfile to, ArenaTrialProfile from)
        {
            to.Skies = false;
            to.OmenWavesMin = from.OmenWavesMin;
            to.OmenWavesMax = from.OmenWavesMax;
        }

        private static void CopyHazards(ArenaTrialProfile to, ArenaTrialProfile from)
        {
            to.HazardSecondsMin = from.HazardSecondsMin;
            to.HazardSecondsMax = from.HazardSecondsMax;
            to.HazardDoubleChance = from.HazardDoubleChance;
        }

        private static void CopyAffixes(ArenaTrialProfile to, ArenaTrialProfile from)
        {
            to.Affixes = from.Affixes;
            to.FinaleAffixes = from.FinaleAffixes;
            to.AffixDistinctKinds = from.AffixDistinctKinds;
        }

        // ------------------------------------------------------------------
        // 文案（只用 GBK 字符：罗马数字 Ⅰ–Ⅷ 在 GB2312 里；英文界面用 ASCII 罗马数字）
        // ------------------------------------------------------------------

        private static readonly string[] NumeralsCn = { "", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ", "Ⅶ", "Ⅷ" };
        private static readonly string[] NumeralsEn = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        internal static string Numeral(int raise)
        {
            int index = Mathf.Clamp(raise, 0, NumeralsCn.Length - 1);
            return L10n.T(NumeralsCn[index], NumeralsEn[index]);
        }

        /// <summary>「加码 Ⅴ」；0 级返回空串。</summary>
        internal static string Label(int raise)
        {
            if (raise <= 0) return string.Empty;
            return L10n.T("加码 ", "Raise ") + Numeral(raise);
        }

        // ------------------------------------------------------------------
        // 本地偏好
        // ------------------------------------------------------------------

        /// <summary>这一档上次加到几级（换档时面板按它复原）。</summary>
        internal static int Remembered(ArenaExpeditionTier tier)
        {
            return Clamp(tier, BossRushLocalPrefs.GetInt(PrefChosen + (int)tier, 0));
        }

        internal static void Remember(ArenaExpeditionTier tier, int raise)
        {
            if (MaxFor(tier) <= 0) return;
            BossRushLocalPrefs.SetInt(PrefChosen + (int)tier, Clamp(tier, raise));
        }

        /// <summary>这一档通关过的最高级；没通关过加码局返回 -1（0 表示通关过不加码的）。</summary>
        internal static int Best(ArenaExpeditionTier tier)
        {
            return Mathf.Clamp(BossRushLocalPrefs.GetInt(PrefBest + (int)tier, -1), -1, MaxFor(tier));
        }

        /// <summary>通关一局：比以前的最高级高才写。</summary>
        internal static bool RecordCleared(ArenaExpeditionTier tier, int raise)
        {
            if (MaxFor(tier) <= 0) return false;
            int clamped = Clamp(tier, raise);
            if (clamped <= Best(tier)) return false;
            BossRushLocalPrefs.SetInt(PrefBest + (int)tier, clamped);
            return true;
        }
    }
}

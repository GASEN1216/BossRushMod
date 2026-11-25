// ============================================================================
// F3GameplayValidationModConfigLabels.cs - MODCONFIG_OPTION_LABELS：ModConfig 下拉项标题的本地化键（2026-11-24 修复）
// ============================================================================
// 整份只在 Dev 构建里编译。由 F3GameplayValidationWishes.cs 的 RunWishBaseCases 调用（完整验收与「只跑本次改动」专项都跑）；
// 单列一份是因为许愿用例文件已到大文件行数上限。生产侧见 Localization/ModConfigOptionLocalization.cs。
// 会切一次游戏语言（只动玩家偏好里的语言项），同一帧内在 finally 里切回原语言；不写存档。
// ============================================================================

#if BOSSRUSH_DEV
using System;
using System.Collections.Generic;
using SodaCraft.Localizations;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>
        /// ModConfig 下拉项标题。官方 OptionsUIEntry_Dropdown 按 Options_&lt;配置键&gt; 查表，键不在表里就显示成带星号的键名
        /// （2026-11-24 owner 实测整排如此）。先看当前语言下每个登记过的键查出来都是注册时那句话，再切到另一种语言，
        /// 看宿主的切语言重注入有没有跟上，最后切回原语言再看一遍。切语言只动玩家偏好里的语言项，同一帧内切回。
        /// 没装 ModConfig 时没有登记项，记 SKIP。
        /// </summary>
        private static bool ValidateModConfigOptionLabels(out string metrics, out string reason)
        {
            List<KeyValuePair<string, string[]>> labels = ModConfigOptionLocalization.SnapshotForValidation();
            if (labels.Count == 0) throw new SkyIslandSkipCase("modconfig_not_installed_or_not_registered", "registered=0");
            SystemLanguage original = LocalizationManager.CurrentLanguage;
            bool originalChinese = L10n.IsChinese;
            string sampleBefore, sampleSwitched, sampleRestored;
            int wrongBefore = CountWrongModConfigLabels(labels, out sampleBefore);
            int wrongSwitched;
            SystemLanguage reached;
            bool switchedChinese;
            try
            {
                LocalizationManager.SetLanguage(originalChinese ? SystemLanguage.English : SystemLanguage.ChineseSimplified);
                reached = LocalizationManager.CurrentLanguage;
                switchedChinese = L10n.IsChinese;
                wrongSwitched = CountWrongModConfigLabels(labels, out sampleSwitched);
            }
            finally
            {
                if (LocalizationManager.CurrentLanguage != original) LocalizationManager.SetLanguage(original);
            }
            int wrongRestored = CountWrongModConfigLabels(labels, out sampleRestored);
            bool restored = LocalizationManager.CurrentLanguage == original;
            bool switched = switchedChinese != originalChinese;
            metrics = "registered=" + labels.Count + ",language=" + original + "->" + reached + "->" + LocalizationManager.CurrentLanguage
                + ",wrong_before=" + wrongBefore + ",wrong_switched=" + wrongSwitched + ",wrong_restored=" + wrongRestored
                + (sampleBefore.Length > 0 ? ",before=" + sampleBefore : string.Empty)
                + (sampleSwitched.Length > 0 ? ",switched=" + sampleSwitched : string.Empty)
                + (sampleRestored.Length > 0 ? ",restored=" + sampleRestored : string.Empty);
            if (!switched) reason = "language_switch_not_applied";
            else if (!restored) reason = "language_not_restored";
            else if (wrongBefore + wrongSwitched + wrongRestored > 0) reason = "下拉项标题键没注入，或与当前语言的文字不符";
            else reason = null;
            return reason == null;
        }

        /// <summary>按官方组件同一口径（键 ToPlainText）取标题，与注册时那句话逐字比；不符的前三条写进样本。</summary>
        private static int CountWrongModConfigLabels(List<KeyValuePair<string, string[]>> labels, out string sample)
        {
            int wrong = 0;
            sample = string.Empty;
            for (int i = 0; i < labels.Count; i++)
            {
                string expected = L10n.T(labels[i].Value[0], labels[i].Value[1]);
                string shown = (ModConfigOptionLocalization.OfficialLabelKeyPrefix + labels[i].Key).ToPlainText();
                if (string.Equals(shown, expected, StringComparison.Ordinal)) continue;
                if (wrong++ < 3) sample += (sample.Length > 0 ? ";" : string.Empty) + labels[i].Key + "=" + shown;
            }
            return wrong;
        }
    }
}
#endif

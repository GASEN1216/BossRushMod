using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode F 击杀奖励气泡文本：把一次击杀的回血、生命上限成长、命火变化与悬赏印记
    /// 拼成玩家头顶气泡的提示。过载开始时优先显示高优先级警告。
    ///
    /// 配色与分段（2026-09-23 审美审查 UB-23）：旧版收益用纯红（读起来像扣血），四段用「 | 」挤成一长串。
    /// 现在收益 SuccessText、命火与悬赏 WarningText、过载警告 DangerText；段间用全角空格，两段一行、第 3 段起换行。
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private string BuildModeFKillRewardBubbleText(
            bool isBountyBoss,
            float healAmount,
            float maxHealthGain,
            float bloodfireGain,
            bool overloadStarted,
            float overloadExtension)
        {
            if (overloadStarted)
            {
                return L10n.T(
                    RichDangerTag + "命火过载！</color>　火力 +40%　移速 +15%\n失血 x2　已被烧伤",
                    RichDangerTag + "Bloodfire Overload!</color>  Power +40%  Speed +15%\nBleed x2  Burning");
            }

            string result = null;
            int parts = 0;

            if (healAmount > 0.01f)
            {
                result = JoinModeFRewardPart(result, ref parts, L10n.T(
                    "血量 " + RichSuccessTag + "+" + Mathf.RoundToInt(healAmount) + "</color>",
                    "HP " + RichSuccessTag + "+" + Mathf.RoundToInt(healAmount) + "</color>"));
            }

            if (maxHealthGain > 0.01f)
            {
                result = JoinModeFRewardPart(result, ref parts, L10n.T(
                    "生命上限 " + RichSuccessTag + "+" + Mathf.RoundToInt(maxHealthGain) + "</color>",
                    "Max HP " + RichSuccessTag + "+" + Mathf.RoundToInt(maxHealthGain) + "</color>"));
            }

            // 阈值取 0.5：续时被 24 秒上限截断成零点几秒时四舍五入会显示“+0秒”。
            if (overloadExtension >= 0.5f)
            {
                result = JoinModeFRewardPart(result, ref parts, L10n.T(
                    "命火续燃 " + RichWarningTag + "+" + Mathf.RoundToInt(overloadExtension) + "秒</color>",
                    "Overload " + RichWarningTag + "+" + Mathf.RoundToInt(overloadExtension) + "s</color>"));
            }
            else if (bloodfireGain > 0.01f)
            {
                result = JoinModeFRewardPart(result, ref parts, L10n.T(
                    "命火 " + RichWarningTag + "+" + Mathf.RoundToInt(bloodfireGain) + "</color>",
                    "Bloodfire " + RichWarningTag + "+" + Mathf.RoundToInt(bloodfireGain) + "</color>"));
            }

            if (isBountyBoss)
            {
                result = JoinModeFRewardPart(result, ref parts, L10n.T(
                    "悬赏印记 " + RichWarningTag + "+1</color>",
                    "Bounty " + RichWarningTag + "+1</color>"));
            }

            if (parts == 0)
            {
                return L10n.T("奖励已结算", "Reward applied");
            }

            return result;
        }

        /// <summary>
        /// 接上一段：两段一行，第 3 段起换行（段间全角空格）。不建临时列表，直接拼串（每次击杀一次，非热路径）。
        /// </summary>
        private static string JoinModeFRewardPart(string result, ref int parts, string part)
        {
            parts++;
            if (parts == 1)
            {
                return part;
            }
            return result + (parts % 2 == 1 ? "\n" : L10n.T("　", "  ")) + part;
        }
    }
}

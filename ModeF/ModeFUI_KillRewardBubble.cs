using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode F 击杀奖励气泡文本：把一次击杀的回血、生命上限成长、命火变化与悬赏印记
    /// 拼成玩家头顶气泡的一行提示。过载开始时优先显示高优先级警告。
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
                    "<color=#FF4500>命火过载！</color> 火力 +40% | 移速 +15% | 失血 x2 | 已被烧伤",
                    "<color=#FF4500>Bloodfire Overload!</color> Power +40% | Speed +15% | Bleed x2 | Burning");
            }

            string result = null;
            bool hasPart = false;

            if (healAmount > 0.01f)
            {
                result = L10n.T(
                    "血量 <color=red>+" + Mathf.RoundToInt(healAmount) + "</color>",
                    "HP <color=red>+" + Mathf.RoundToInt(healAmount) + "</color>");
                hasPart = true;
            }

            if (maxHealthGain > 0.01f)
            {
                string maxHealthText = L10n.T(
                    "生命上限 <color=red>+" + Mathf.RoundToInt(maxHealthGain) + "</color>",
                    "Max HP <color=red>+" + Mathf.RoundToInt(maxHealthGain) + "</color>");
                result = hasPart ? result + " | " + maxHealthText : maxHealthText;
                hasPart = true;
            }

            // 阈值取 0.5：续时被 24 秒上限截断成零点几秒时四舍五入会显示“+0秒”。
            if (overloadExtension >= 0.5f)
            {
                string extensionText = L10n.T(
                    "命火续燃 <color=#FF8C00>+" + Mathf.RoundToInt(overloadExtension) + "秒</color>",
                    "Overload <color=#FF8C00>+" + Mathf.RoundToInt(overloadExtension) + "s</color>");
                result = hasPart ? result + " | " + extensionText : extensionText;
                hasPart = true;
            }
            else if (bloodfireGain > 0.01f)
            {
                string bloodfireText = L10n.T(
                    "命火 <color=#FF8C00>+" + Mathf.RoundToInt(bloodfireGain) + "</color>",
                    "Bloodfire <color=#FF8C00>+" + Mathf.RoundToInt(bloodfireGain) + "</color>");
                result = hasPart ? result + " | " + bloodfireText : bloodfireText;
                hasPart = true;
            }

            if (isBountyBoss)
            {
                string bountyText = L10n.T(
                    "悬赏印记 <color=red>+1</color>",
                    "Bounty <color=red>+1</color>");
                result = hasPart ? result + " | " + bountyText : bountyText;
                hasPart = true;
            }

            if (!hasPart)
            {
                return L10n.T("奖励已结算", "Reward applied");
            }

            return result;
        }
    }
}

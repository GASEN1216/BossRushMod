// ============================================================================
// AchievementUIStrings.cs - 成就UI本地化字符串
// ============================================================================
// 模块说明：
//   定义成就页面UI的所有本地化字符串常量
//   提供语言检测和文本获取方法
// ============================================================================

using System;
using UnityEngine;
using SodaCraft.Localizations;

namespace BossRush
{
    /// <summary>
    /// 成就UI本地化字符串
    /// </summary>
    public static class AchievementUIStrings
    {
        #region 中文字符串

        public const string CN_Title = "成就";
        public const string CN_ClaimAll = "一键领取";
        public const string CN_Claim = "领取";
        public const string CN_Claimed = "已领取";
        public const string CN_Stats = "已解锁: {0}/{1}";
        public const string CN_TotalReward = "已领取奖励: ${0}";
        public const string CN_HiddenName = "???";
        public const string CN_HiddenDesc = "完成特定条件解锁";
        public const string CN_NoRewards = "没有可领取的奖励";
        public const string CN_ClaimedTotal = "已领取 ${0}";

        #endregion

        #region 英文字符串

        public const string EN_Title = "Achievements";
        public const string EN_ClaimAll = "Claim All";
        public const string EN_Claim = "Claim";
        public const string EN_Claimed = "Claimed";
        public const string EN_Stats = "Unlocked: {0}/{1}";
        public const string EN_TotalReward = "Claimed Rewards: ${0}";
        public const string EN_HiddenName = "???";
        public const string EN_HiddenDesc = "Complete specific conditions to unlock";
        public const string EN_NoRewards = "No rewards available";
        public const string EN_ClaimedTotal = "Claimed ${0}";

        #endregion

        #region 语言检测

        /// <summary>
        /// 检测当前语言是否为中文
        /// </summary>
        public static bool IsChinese()
        {
            try
            {
                var lang = LocalizationManager.CurrentLanguage;
                return lang == SystemLanguage.ChineseSimplified ||
                       lang == SystemLanguage.ChineseTraditional ||
                       lang == SystemLanguage.Chinese;
            }
            catch
            {
                // 默认返回中文
                return true;
            }
        }

        #endregion

        #region 文本获取

        /// <summary>
        /// 根据当前语言获取本地化文本
        /// </summary>
        /// <param name="textCN">中文文本</param>
        /// <param name="textEN">英文文本</param>
        /// <returns>当前语言对应的文本</returns>
        public static string GetText(string textCN, string textEN)
        {
            return IsChinese() ? textCN : textEN;
        }

        #endregion
    }
}

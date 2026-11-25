// ============================================================================
// WishFountainLanternFilter.cs - 星灯上墙前的清洗与去重
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 3 项）：
//   飞书里拉回来的心愿要写上星灯给所有人看。上墙前再过一遍：
//   - 折成单行、去首尾空白；
//   - 连续重复三次以上的片段（1~6 字）压到两份：「更新更新更新更新」→「更新更新」，
//     「谢谢」「哈哈」这种正常叠字不动；
//   - 重跑提交时的联系方式、敏感词、字符多样性三道检查，旧规则放进来的漏网条目这里拦下；
//   - 有效字数不足 MIN_CHARS 的不上墙；
//   - 规范化（只留字母、数字、汉字，小写）后相同的只留第一条。
// ============================================================================

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BossRush
{
    public static partial class WishFountainService
    {
        private static readonly Regex LanternRepeatRegex =
            new Regex(@"(.{1,6}?)\1{2,}", RegexOptions.CultureInvariant);

        /// <summary>星灯上墙前的清洗与去重。输入不改，返回新列表（可能为空）。</summary>
        internal static List<string> FilterLanternWishes(List<string> raw)
        {
            List<string> result = new List<string>();
            if (raw == null || raw.Count == 0) return result;

            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                string cleaned = CleanLanternWish(raw[i]);
                if (cleaned == null) continue;
                string key = LanternDedupeKey(cleaned);
                if (key.Length == 0 || !seen.Add(key)) continue;
                result.Add(cleaned);
            }
            return result;
        }

        private static string CleanLanternWish(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string oneLine = Regex.Replace(raw, @"\s+", " ").Trim();
            if (oneLine.Length == 0) return null;
            oneLine = LanternRepeatRegex.Replace(oneLine, "$1$1");

            string ignored;
            if (!CheckNoContactInfo(oneLine, out ignored)) return null;
            if (!CheckNoProfanity(oneLine, out ignored)) return null;
            if (!CheckCharDiversity(oneLine, out ignored)) return null;
            if (CountEffectiveWishChars(oneLine) < MIN_CHARS) return null;
            return oneLine;
        }

        private static string LanternDedupeKey(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (IsEffectiveWishChar(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}

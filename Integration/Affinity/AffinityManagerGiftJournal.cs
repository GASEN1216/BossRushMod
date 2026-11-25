// ============================================================================
// AffinityManagerGiftJournal.cs - 心意簿：送礼时摸清的喜好
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 6 项）：
//   玩家给 NPC 送礼，NPCGiftSystem 判定反应时命中了哪条规则（某件物品 / 某类标签 / 收集品的 LikedBy），
//   就把那条规则记进这位 NPC 的心意簿；落在类型基础值（普通反应）的不记。
//   记法是逗号分隔的令牌：item:<TypeID>、tag:<标签名>，存在 AffinityData.knownLikes / knownDislikes，
//   旧档缺字段读成空串。好感页签（AffinityTabData）按当前配置对照着显示，配置删掉的旧令牌只是不显示。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    public static partial class AffinityManager
    {
        internal const string GiftItemTokenPrefix = "item:";
        internal const string GiftTagTokenPrefix = "tag:";

        /// <summary>
        /// 记下这位 NPC 对一条礼物规则的心意。第一次记下返回 true（调用方据此提示「心意簿添了一笔」），
        /// 已经记过、参数无效或当前不可写返回 false。
        /// </summary>
        internal static bool TryRecordGiftPreference(string npcId, bool liked, string token)
        {
            if (!CanWrite || string.IsNullOrEmpty(npcId) || !IsValidGiftToken(token)) return false;
            AffinityData data;
            if (!npcDataMap.TryGetValue(npcId, out data) || data == null)
            {
                data = new AffinityData(npcId);
                npcDataMap[npcId] = data;
            }
            string list = liked ? data.knownLikes : data.knownDislikes;
            if (ContainsGiftToken(list, token)) return false;
            list = string.IsNullOrEmpty(list) ? token : list + "," + token;
            if (liked) data.knownLikes = list;
            else data.knownDislikes = list;
            MarkDirty();
            return true;
        }

        /// <summary>这位 NPC 心意簿里已经摸清的令牌（喜欢 / 不喜欢），按记下的先后。</summary>
        internal static List<string> GetKnownGiftPreferences(string npcId, bool liked)
        {
            List<string> result = new List<string>();
            AffinityData data;
            if (string.IsNullOrEmpty(npcId) || !npcDataMap.TryGetValue(npcId, out data) || data == null) return result;
            string list = liked ? data.knownLikes : data.knownDislikes;
            if (string.IsNullOrEmpty(list)) return result;
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (IsValidGiftToken(token) && !result.Contains(token)) result.Add(token);
            }
            return result;
        }

        internal static string GiftItemToken(int typeId)
        {
            return GiftItemTokenPrefix + typeId;
        }

        internal static string GiftTagToken(string tagName)
        {
            return GiftTagTokenPrefix + tagName;
        }

        private static bool IsValidGiftToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.IndexOf(',') >= 0) return false;
            return (token.StartsWith(GiftItemTokenPrefix, StringComparison.Ordinal) && token.Length > GiftItemTokenPrefix.Length)
                || (token.StartsWith(GiftTagTokenPrefix, StringComparison.Ordinal) && token.Length > GiftTagTokenPrefix.Length);
        }

        private static bool ContainsGiftToken(string list, string token)
        {
            if (string.IsNullOrEmpty(list)) return false;
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), token, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}

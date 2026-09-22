using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    public static class AffinityJsonSerializer
    {
        public static string Serialize(Dictionary<string, AffinityData> dataMap)
        {
            var sb = SimpleJsonHelper.GetBuilder();
            sb.Append("{\"npcDataList\":[");

            bool first = true;
            foreach (var kvp in dataMap)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeAffinityData(sb, kvp.Value);
            }

            sb.Append("]}");
            return sb.ToString();
        }

        public static bool Deserialize(string json, Dictionary<string, AffinityData> dataMap)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
            List<BossRushJsonValue> entries;
            if (root == null || !root.TryGetArray("npcDataList", out entries)) return false;
            var candidate = new Dictionary<string, AffinityData>();
            foreach (var entry in entries)
            {
                var data = ParseAffinityData(entry);
                if (data == null || string.IsNullOrEmpty(data.npcId) || candidate.ContainsKey(data.npcId))
                    return false;
                candidate.Add(data.npcId, data);
            }
            foreach (var entry in candidate) dataMap[entry.Key] = entry.Value;
            return true;
        }

        private static void SerializeAffinityData(StringBuilder sb, AffinityData data)
        {
            sb.Append('{');
            SimpleJsonHelper.AppendString(sb, "npcId", data.npcId);
            SimpleJsonHelper.AppendInt(sb, "points", data.points);
            SimpleJsonHelper.AppendInt(sb, "lastGiftDay", data.lastGiftDay);
            SimpleJsonHelper.AppendInt(sb, "lastGiftReaction", data.lastGiftReaction);
            SimpleJsonHelper.AppendInt(sb, "lastChatDay", data.lastChatDay);
            SimpleJsonHelper.AppendString(sb, "interactionHistoryDays", data.interactionHistoryDays ?? string.Empty);
            SimpleJsonHelper.AppendBool(sb, "hasMet", data.hasMet);
            SimpleJsonHelper.AppendBool(sb, "hasTriggeredStory5", data.hasTriggeredStory5);
            SimpleJsonHelper.AppendBool(sb, "hasTriggeredStory10", data.hasTriggeredStory10);
            SimpleJsonHelper.AppendString(sb, "triggeredEventKeys", data.triggeredEventKeys ?? string.Empty);
            SimpleJsonHelper.AppendString(sb, "claimedRewardKeys", data.claimedRewardKeys ?? string.Empty);
            SimpleJsonHelper.AppendBool(sb, "isMarriedToPlayer", data.isMarriedToPlayer);
            SimpleJsonHelper.AppendBool(sb, "isFollowingPlayer", data.isFollowingPlayer);
            SimpleJsonHelper.AppendString(sb, "marriageDateText", data.marriageDateText ?? string.Empty);
            SimpleJsonHelper.AppendInt(sb, "cheatingIncidentCount", data.cheatingIncidentCount);
            SimpleJsonHelper.AppendBool(sb, "hasPendingCheatingRebuke", data.hasPendingCheatingRebuke);
            SimpleJsonHelper.AppendInt(sb, "lastDecayCheckDay", data.lastDecayCheckDay, addComma: false);
            sb.Append('}');
        }

        private static AffinityData ParseAffinityData(BossRushJsonValue entry)
        {
            if (entry == null || entry.Kind != BossRushJsonKind.Object) return null;
            // 可选旧字段保持默认值；已声明却损坏的字段拒收整份记录。
            foreach (string key in new[] { "points", "lastGiftDay", "lastGiftReaction", "lastChatDay", "cheatingIncidentCount", "lastDecayCheckDay" })
            {
                int value;
                if (entry.GetProperty(key) != null && !entry.TryGetInt(key, out value)) return null;
            }
            foreach (string key in new[] { "hasMet", "hasTriggeredStory5", "hasTriggeredStory10", "isMarriedToPlayer", "isFollowingPlayer", "hasPendingCheatingRebuke" })
            {
                bool value;
                if (entry.GetProperty(key) != null && !entry.TryGetBool(key, out value)) return null;
            }
            foreach (string key in new[] { "npcId", "interactionHistoryDays", "triggeredEventKeys", "claimedRewardKeys", "marriageDateText" })
            {
                string value;
                if (entry.GetProperty(key) != null && !entry.TryGetString(key, out value)) return null;
            }
            return new AffinityData
            {
                npcId = entry.GetString("npcId", null),
                points = entry.GetInt("points", 0),
                lastGiftDay = entry.GetInt("lastGiftDay", -1),
                lastGiftReaction = entry.GetInt("lastGiftReaction", 0),
                lastChatDay = entry.GetInt("lastChatDay", -1),
                interactionHistoryDays = entry.GetString("interactionHistoryDays", string.Empty),
                hasMet = entry.GetBool("hasMet", false),
                hasTriggeredStory5 = entry.GetBool("hasTriggeredStory5", false),
                hasTriggeredStory10 = entry.GetBool("hasTriggeredStory10", false),
                triggeredEventKeys = entry.GetString("triggeredEventKeys", string.Empty),
                claimedRewardKeys = entry.GetString("claimedRewardKeys", string.Empty),
                isMarriedToPlayer = entry.GetBool("isMarriedToPlayer", false),
                isFollowingPlayer = entry.GetBool("isFollowingPlayer", false),
                marriageDateText = entry.GetString("marriageDateText", string.Empty),
                cheatingIncidentCount = entry.GetInt("cheatingIncidentCount", 0),
                hasPendingCheatingRebuke = entry.GetBool("hasPendingCheatingRebuke", false),
                lastDecayCheckDay = entry.GetInt("lastDecayCheckDay", -1)
            };
        }
    }
}

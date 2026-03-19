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
            if (!SimpleJsonHelper.FindArrayBounds(json, out int arrayStart, out int arrayEnd))
                return false;

            SimpleJsonHelper.ForEachObject(json, arrayStart, arrayEnd, (j, start, end) =>
            {
                var data = ParseAffinityData(j, start, end);
                if (data != null && !string.IsNullOrEmpty(data.npcId))
                {
                    dataMap[data.npcId] = data;
                }
            });

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

        private static AffinityData ParseAffinityData(string json, int start, int end)
        {
            return new AffinityData
            {
                npcId = SimpleJsonHelper.ExtractString(json, "npcId", start, end),
                points = SimpleJsonHelper.ExtractInt(json, "points", start, end),
                lastGiftDay = SimpleJsonHelper.ExtractInt(json, "lastGiftDay", start, end),
                lastGiftReaction = SimpleJsonHelper.ExtractInt(json, "lastGiftReaction", start, end),
                lastChatDay = SimpleJsonHelper.ExtractInt(json, "lastChatDay", start, end),
                interactionHistoryDays = SimpleJsonHelper.ExtractString(json, "interactionHistoryDays", start, end),
                hasMet = SimpleJsonHelper.ExtractBool(json, "hasMet", start, end),
                hasTriggeredStory5 = SimpleJsonHelper.ExtractBool(json, "hasTriggeredStory5", start, end),
                hasTriggeredStory10 = SimpleJsonHelper.ExtractBool(json, "hasTriggeredStory10", start, end),
                triggeredEventKeys = SimpleJsonHelper.ExtractString(json, "triggeredEventKeys", start, end),
                claimedRewardKeys = SimpleJsonHelper.ExtractString(json, "claimedRewardKeys", start, end),
                isMarriedToPlayer = SimpleJsonHelper.ExtractBool(json, "isMarriedToPlayer", start, end),
                isFollowingPlayer = SimpleJsonHelper.ExtractBool(json, "isFollowingPlayer", start, end),
                marriageDateText = SimpleJsonHelper.ExtractString(json, "marriageDateText", start, end),
                cheatingIncidentCount = SimpleJsonHelper.ExtractInt(json, "cheatingIncidentCount", start, end),
                hasPendingCheatingRebuke = SimpleJsonHelper.ExtractBool(json, "hasPendingCheatingRebuke", start, end),
                lastDecayCheckDay = SimpleJsonHelper.ExtractInt(json, "lastDecayCheckDay", start, end)
            };
        }
    }
}

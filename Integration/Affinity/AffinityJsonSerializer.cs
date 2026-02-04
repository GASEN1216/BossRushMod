// ============================================================================
// AffinityJsonSerializer.cs - 好感度数据JSON序列化器
// ============================================================================
// 模块说明：
//   好感度数据的专用序列化器，基于 SimpleJsonHelper 实现。
// ============================================================================

using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// 好感度数据JSON序列化器
    /// </summary>
    public static class AffinityJsonSerializer
    {
        /// <summary>
        /// 将好感度数据字典序列化为JSON字符串
        /// </summary>
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
        
        /// <summary>
        /// 从JSON字符串反序列化好感度数据
        /// </summary>
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
        
        /// <summary>
        /// 序列化单个 AffinityData 对象
        /// </summary>
        private static void SerializeAffinityData(StringBuilder sb, AffinityData data)
        {
            sb.Append('{');
            SimpleJsonHelper.AppendString(sb, "npcId", data.npcId);
            SimpleJsonHelper.AppendInt(sb, "points", data.points);
            SimpleJsonHelper.AppendInt(sb, "lastGiftDay", data.lastGiftDay);
            SimpleJsonHelper.AppendInt(sb, "lastGiftReaction", data.lastGiftReaction);
            SimpleJsonHelper.AppendInt(sb, "lastChatDay", data.lastChatDay);
            SimpleJsonHelper.AppendBool(sb, "hasMet", data.hasMet);
            SimpleJsonHelper.AppendBool(sb, "hasTriggeredStory5", data.hasTriggeredStory5);
            SimpleJsonHelper.AppendBool(sb, "hasTriggeredStory10", data.hasTriggeredStory10);
            SimpleJsonHelper.AppendInt(sb, "lastDecayCheckDay", data.lastDecayCheckDay, addComma: false);
            sb.Append('}');
        }
        
        /// <summary>
        /// 解析单个 AffinityData 对象
        /// </summary>
        private static AffinityData ParseAffinityData(string json, int start, int end)
        {
            return new AffinityData
            {
                npcId = SimpleJsonHelper.ExtractString(json, "npcId", start, end),
                points = SimpleJsonHelper.ExtractInt(json, "points", start, end),
                lastGiftDay = SimpleJsonHelper.ExtractInt(json, "lastGiftDay", start, end),
                lastGiftReaction = SimpleJsonHelper.ExtractInt(json, "lastGiftReaction", start, end),
                lastChatDay = SimpleJsonHelper.ExtractInt(json, "lastChatDay", start, end),
                hasMet = SimpleJsonHelper.ExtractBool(json, "hasMet", start, end),
                hasTriggeredStory5 = SimpleJsonHelper.ExtractBool(json, "hasTriggeredStory5", start, end),
                hasTriggeredStory10 = SimpleJsonHelper.ExtractBool(json, "hasTriggeredStory10", start, end),
                lastDecayCheckDay = SimpleJsonHelper.ExtractInt(json, "lastDecayCheckDay", start, end)
            };
        }
    }
}

// ============================================================================
// SimpleJsonHelper.cs - 轻量级JSON工具类
// ============================================================================
// 模块说明（2026-11-19 核对现状）：
//   嵌套 JSON 的解析一律用 Common/Data/BossRushJsonValue.cs 的 BossRushJsonParser，这里不再扩展读侧。
//   本文件只剩两类用途：
//   - 扁平写出：GetBuilder + Append* + EscapeString（存档编码、配置写回、许愿台请求体等）；
//   - 许愿台飞书接口返回的扁平取值：ExtractString / ExtractInt / UnescapeString / ForEachObject
//     （WishFountainConfigAndValidation / SendPipeline / FetchPipeline；按原文本搜键，只适合这些已知形状的小响应）。
//   不再使用的 ExtractLong / ExtractFloat / ExtractBool / FindArrayBounds 已删除。
//
// 性能说明：
//   - 写出：调用方使用独立 StringBuilder，避免嵌套序列化互相清空
//   - 取值：基于字符串查找，适合小数据量，不适合大量数据或高频调用场景
// ============================================================================

using System;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// 轻量级JSON工具类
    /// </summary>
    public static class SimpleJsonHelper
    {
        #region StringBuilder 构建器
        
        /// <summary>
        /// 获取独立的 StringBuilder，避免嵌套序列化时共享实例被清空
        /// </summary>
        public static StringBuilder GetBuilder()
        {
            return new StringBuilder(1024);
        }
        
        /// <summary>
        /// 追加JSON字符串字段
        /// </summary>
        public static void AppendString(StringBuilder sb, string key, string value, bool addComma = true)
        {
            sb.Append('"').Append(key).Append("\":\"");
            EscapeString(sb, value);
            sb.Append('"');
            if (addComma) sb.Append(',');
        }
        
        /// <summary>
        /// 追加JSON整数字段
        /// </summary>
        public static void AppendInt(StringBuilder sb, string key, int value, bool addComma = true)
        {
            sb.Append('"').Append(key).Append("\":").Append(value);
            if (addComma) sb.Append(',');
        }
        
        /// <summary>
        /// 追加JSON长整数字段
        /// </summary>
        public static void AppendLong(StringBuilder sb, string key, long value, bool addComma = true)
        {
            sb.Append('"').Append(key).Append("\":").Append(value);
            if (addComma) sb.Append(',');
        }
        
        /// <summary>
        /// 追加JSON浮点数字段
        /// </summary>
        public static void AppendFloat(StringBuilder sb, string key, float value, bool addComma = true)
        {
            // NaN / ±Infinity 的 ToString 是 "NaN" / "Infinity"，不是合法 JSON：读侧的节点解析器
            // （BossRushJsonValue）会整篇拒收，该 key 从此进写屏障。非有限值一律写 0，
            // 宁可丢一个数值也不能毁掉整份存档（与 BossRushJsonWriter.Num 同口径）。
            string text = float.IsNaN(value) || float.IsInfinity(value)
                ? "0"
                : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.Append('"').Append(key).Append("\":").Append(text);
            if (addComma) sb.Append(',');
        }
        
        /// <summary>
        /// 追加JSON布尔字段
        /// </summary>
        public static void AppendBool(StringBuilder sb, string key, bool value, bool addComma = true)
        {
            sb.Append('"').Append(key).Append("\":").Append(value ? "true" : "false");
            if (addComma) sb.Append(',');
        }
        
        /// <summary>
        /// 转义JSON字符串中的特殊字符
        /// </summary>
        public static void EscapeString(StringBuilder sb, string str)
        {
            if (string.IsNullOrEmpty(str)) return;
            
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default: sb.Append(c); break;
                }
            }
        }
        
        #endregion
        
        #region 值提取器
        
        /// <summary>
        /// 从JSON字符串中提取字符串值
        /// </summary>
        /// <param name="json">JSON字符串</param>
        /// <param name="key">键名</param>
        /// <param name="searchStart">搜索起始位置</param>
        /// <param name="searchEnd">搜索结束位置（-1表示到末尾）</param>
        /// <returns>提取的字符串值，未找到返回空字符串</returns>
        public static string ExtractString(string json, string key, int searchStart = 0, int searchEnd = -1)
        {
            if (string.IsNullOrEmpty(json)) return "";
            if (searchEnd < 0) searchEnd = json.Length;
            
            // 查找 "key":"
            string pattern = "\"" + key + "\":\"";
            int keyPos = json.IndexOf(pattern, searchStart);
            
            if (keyPos < 0 || keyPos >= searchEnd)
                return "";
            
            int valueStart = keyPos + pattern.Length;
            
            // 查找结束引号（处理转义）
            int valueEnd = valueStart;
            while (valueEnd < searchEnd && valueEnd < json.Length)
            {
                if (json[valueEnd] == '"')
                {
                    // 检查是否被转义
                    int backslashCount = 0;
                    int checkPos = valueEnd - 1;
                    while (checkPos >= valueStart && json[checkPos] == '\\')
                    {
                        backslashCount++;
                        checkPos--;
                    }
                    // 偶数个反斜杠表示引号未被转义
                    if (backslashCount % 2 == 0)
                        break;
                }
                valueEnd++;
            }
            
            if (valueEnd >= json.Length)
                return "";
            
            string raw = json.Substring(valueStart, valueEnd - valueStart);
            return UnescapeString(raw);
        }
        
        /// <summary>
        /// 从JSON字符串中提取整数值
        /// </summary>
        public static int ExtractInt(string json, string key, int searchStart = 0, int searchEnd = -1)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            if (searchEnd < 0) searchEnd = json.Length;
            
            string pattern = "\"" + key + "\":";
            int keyPos = json.IndexOf(pattern, searchStart);
            
            if (keyPos < 0 || keyPos >= searchEnd)
                return 0;
            
            int valueStart = keyPos + pattern.Length;
            
            // 跳过空白
            while (valueStart < searchEnd && valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            
            // 读取数字（包括负号）
            int valueEnd = valueStart;
            if (valueEnd < json.Length && json[valueEnd] == '-')
                valueEnd++;
            
            while (valueEnd < searchEnd && valueEnd < json.Length && char.IsDigit(json[valueEnd]))
                valueEnd++;
            
            if (valueEnd == valueStart)
                return 0;
            
            int.TryParse(json.Substring(valueStart, valueEnd - valueStart), out int result);
            return result;
        }
        
        /// <summary>
        /// 反转义JSON字符串
        /// </summary>
        public static string UnescapeString(string str)
        {
            if (string.IsNullOrEmpty(str) || str.IndexOf('\\') < 0)
                return str;
            
            var sb = new StringBuilder(str.Length);
            
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '\\' && i + 1 < str.Length)
                {
                    char next = str[i + 1];
                    switch (next)
                    {
                        case '\\': sb.Append('\\'); i++; break;
                        case '"': sb.Append('"'); i++; break;
                        case 'n': sb.Append('\n'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case 't': sb.Append('\t'); i++; break;
                        case 'b': sb.Append('\b'); i++; break;
                        case 'f': sb.Append('\f'); i++; break;
                        default: sb.Append(str[i]); break;
                    }
                }
                else
                {
                    sb.Append(str[i]);
                }
            }
            
            return sb.ToString();
        }
        
        #endregion
        
        #region 数组解析辅助
        
        /// <summary>
        /// 遍历JSON数组中的对象
        /// </summary>
        /// <param name="json">JSON字符串</param>
        /// <param name="arrayStart">数组起始位置</param>
        /// <param name="arrayEnd">数组结束位置</param>
        /// <param name="callback">每个对象的回调（参数：json, objStart, objEnd）</param>
        public static void ForEachObject(string json, int arrayStart, int arrayEnd, Action<string, int, int> callback)
        {
            if (callback == null) return;
            
            int pos = arrayStart + 1;
            while (pos < arrayEnd)
            {
                // 跳过空白和逗号
                while (pos < arrayEnd && (char.IsWhiteSpace(json[pos]) || json[pos] == ','))
                    pos++;
                
                // 查找对象起始
                if (pos >= arrayEnd || json[pos] != '{')
                    break;
                
                // 找到对象结束位置
                int objStart = pos;
                int depth = 1;
                pos++;
                
                while (pos < arrayEnd && depth > 0)
                {
                    char c = json[pos];
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                    else if (c == '"')
                    {
                        // 跳过字符串内容
                        pos++;
                        while (pos < arrayEnd)
                        {
                            if (json[pos] == '"')
                            {
                                int backslashCount = 0;
                                int checkPos = pos - 1;
                                while (checkPos >= objStart && json[checkPos] == '\\')
                                {
                                    backslashCount++;
                                    checkPos--;
                                }
                                if (backslashCount % 2 == 0)
                                    break;
                            }
                            pos++;
                        }
                    }
                    pos++;
                }
                
                // 回调处理对象
                if (depth == 0)
                {
                    callback(json, objStart, pos);
                }
            }
        }
        
        #endregion
    }
}

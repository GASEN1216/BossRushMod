// ============================================================================
// SimpleJsonHelper.cs - 轻量级JSON工具类
// ============================================================================
// 模块说明：
//   提供简单的JSON序列化和反序列化功能。
//   由于 Unity JsonUtility 对普通C#类支持不完善，使用手动实现。
//   
// 使用场景：
//   - 需要持久化的简单数据结构
//   - 不想引入第三方JSON库
//   - 数据量较小，性能要求不高
//   
// 性能说明：
//   - 序列化：使用 StringBuilder 避免字符串拼接产生的GC
//   - 反序列化：基于字符串查找，适合小数据量
//   - 不适合大量数据或高频调用场景
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
        // 复用 StringBuilder 减少GC（非线程安全，仅主线程使用）
        private static readonly StringBuilder _sharedBuilder = new StringBuilder(1024);
        
        #region StringBuilder 构建器
        
        /// <summary>
        /// 获取共享的 StringBuilder（使用前会自动清空）
        /// </summary>
        public static StringBuilder GetBuilder()
        {
            _sharedBuilder.Clear();
            return _sharedBuilder;
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
            sb.Append('"').Append(key).Append("\":").Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        /// 从JSON字符串中提取长整数值
        /// </summary>
        public static long ExtractLong(string json, string key, int searchStart = 0, int searchEnd = -1)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            if (searchEnd < 0) searchEnd = json.Length;
            
            string pattern = "\"" + key + "\":";
            int keyPos = json.IndexOf(pattern, searchStart);
            
            if (keyPos < 0 || keyPos >= searchEnd)
                return 0;
            
            int valueStart = keyPos + pattern.Length;
            
            while (valueStart < searchEnd && valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            
            int valueEnd = valueStart;
            if (valueEnd < json.Length && json[valueEnd] == '-')
                valueEnd++;
            
            while (valueEnd < searchEnd && valueEnd < json.Length && char.IsDigit(json[valueEnd]))
                valueEnd++;
            
            if (valueEnd == valueStart)
                return 0;
            
            long.TryParse(json.Substring(valueStart, valueEnd - valueStart), out long result);
            return result;
        }
        
        /// <summary>
        /// 从JSON字符串中提取浮点数值
        /// </summary>
        public static float ExtractFloat(string json, string key, int searchStart = 0, int searchEnd = -1)
        {
            if (string.IsNullOrEmpty(json)) return 0f;
            if (searchEnd < 0) searchEnd = json.Length;
            
            string pattern = "\"" + key + "\":";
            int keyPos = json.IndexOf(pattern, searchStart);
            
            if (keyPos < 0 || keyPos >= searchEnd)
                return 0f;
            
            int valueStart = keyPos + pattern.Length;
            
            while (valueStart < searchEnd && valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            
            int valueEnd = valueStart;
            if (valueEnd < json.Length && json[valueEnd] == '-')
                valueEnd++;
            
            while (valueEnd < searchEnd && valueEnd < json.Length && 
                   (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.' || json[valueEnd] == 'e' || json[valueEnd] == 'E' || json[valueEnd] == '+' || json[valueEnd] == '-'))
            {
                // 防止读取到下一个负数
                if ((json[valueEnd] == '-' || json[valueEnd] == '+') && valueEnd > valueStart && 
                    json[valueEnd - 1] != 'e' && json[valueEnd - 1] != 'E')
                    break;
                valueEnd++;
            }
            
            if (valueEnd == valueStart)
                return 0f;
            
            float.TryParse(json.Substring(valueStart, valueEnd - valueStart), 
                System.Globalization.NumberStyles.Float, 
                System.Globalization.CultureInfo.InvariantCulture, 
                out float result);
            return result;
        }
        
        /// <summary>
        /// 从JSON字符串中提取布尔值
        /// </summary>
        public static bool ExtractBool(string json, string key, int searchStart = 0, int searchEnd = -1)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (searchEnd < 0) searchEnd = json.Length;
            
            string pattern = "\"" + key + "\":";
            int keyPos = json.IndexOf(pattern, searchStart);
            
            if (keyPos < 0 || keyPos >= searchEnd)
                return false;
            
            int valueStart = keyPos + pattern.Length;
            
            while (valueStart < searchEnd && valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            
            // 检查是否为 true
            if (valueStart + 4 <= json.Length && valueStart + 4 <= searchEnd)
            {
                return json.Substring(valueStart, 4).Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            
            return false;
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
        /// 查找JSON数组的边界
        /// </summary>
        /// <param name="json">JSON字符串</param>
        /// <param name="arrayStart">输出：数组起始位置（'['的位置）</param>
        /// <param name="arrayEnd">输出：数组结束位置（']'的位置）</param>
        /// <returns>是否找到有效数组</returns>
        public static bool FindArrayBounds(string json, out int arrayStart, out int arrayEnd)
        {
            arrayStart = json?.IndexOf('[') ?? -1;
            arrayEnd = json?.LastIndexOf(']') ?? -1;
            return arrayStart >= 0 && arrayEnd > arrayStart;
        }
        
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

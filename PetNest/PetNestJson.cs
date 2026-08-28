// ============================================================================
// PetNestJson.cs - 遗种巢极简 JSON 编解码（实施计划 步骤 2）
// ============================================================================
// 为什么自带一份：
//   - Unity JsonUtility 对普通 C# 类支持不完善（同 Utilities/SimpleJsonHelper.cs 的理由）；
//   - 仓库既有的 SimpleJsonHelper 只支持"扁平对象 + 一层数组"（FindArrayBounds 只找
//     第一个数组），而遗种巢的 NestData 是「巢 -> 崽[] -> 天赋[] / 战痕[]」两层嵌套；
//   - ModeH 的 ModeHJsonValue 与 Mode H 的赛季契约耦合，按实施计划**不 import**，
//     避免两套系统互相成为对方的升级阻塞项。
//
// 纪律：
//   - 只做遗种巢需要的子集：对象 / 数组 / 字符串 / 数字 / 布尔 / null；
//   - 解析失败一律返回 null，由持久化层 fail-closed（不覆盖原 key）；
//   - 数字统一按 InvariantCulture 读写，避免中文区小数点变逗号；
//   - 转义复用 SimpleJsonHelper.EscapeString，不再造第二套。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BossRush
{
    /// <summary>JSON 节点类型。</summary>
    internal enum PetNestJsonKind
    {
        Null = 0,
        Bool = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5,
    }

    /// <summary>
    /// 一个 JSON 节点。读侧只读，写侧不用它（写侧走 PetNestJsonBuilder 直出字符串）。
    /// </summary>
    internal sealed class PetNestJsonNode
    {
        internal PetNestJsonKind Kind;
        internal bool BoolValue;
        internal double NumberValue;
        internal string StringValue;
        internal List<PetNestJsonNode> Items;
        internal Dictionary<string, PetNestJsonNode> Members;

        #region 成员访问（全部 no-throw，缺失返回默认值）

        internal PetNestJsonNode Member(string key)
        {
            if (Kind != PetNestJsonKind.Object || Members == null || key == null) return null;
            PetNestJsonNode node;
            return Members.TryGetValue(key, out node) ? node : null;
        }

        internal string GetString(string key, string fallback)
        {
            PetNestJsonNode n = Member(key);
            if (n == null) return fallback;
            if (n.Kind == PetNestJsonKind.String) return n.StringValue;
            if (n.Kind == PetNestJsonKind.Null) return fallback;
            return fallback;
        }

        internal int GetInt(string key, int fallback)
        {
            PetNestJsonNode n = Member(key);
            if (n == null || n.Kind != PetNestJsonKind.Number) return fallback;
            return (int)Math.Round(n.NumberValue);
        }

        internal long GetLong(string key, long fallback)
        {
            PetNestJsonNode n = Member(key);
            if (n == null || n.Kind != PetNestJsonKind.Number) return fallback;
            return (long)Math.Round(n.NumberValue);
        }

        internal float GetFloat(string key, float fallback)
        {
            PetNestJsonNode n = Member(key);
            if (n == null || n.Kind != PetNestJsonKind.Number) return fallback;
            return (float)n.NumberValue;
        }

        internal bool GetBool(string key, bool fallback)
        {
            PetNestJsonNode n = Member(key);
            if (n == null || n.Kind != PetNestJsonKind.Bool) return fallback;
            return n.BoolValue;
        }

        /// <summary>取数组成员。不是数组时返回空列表（调用方无需判空）。</summary>
        internal List<PetNestJsonNode> GetArray(string key)
        {
            PetNestJsonNode n = Member(key);
            if (n == null || n.Kind != PetNestJsonKind.Array || n.Items == null)
            {
                return EmptyItems;
            }
            return n.Items;
        }

        /// <summary>取对象成员。不是对象时返回 null。</summary>
        internal PetNestJsonNode GetObject(string key)
        {
            PetNestJsonNode n = Member(key);
            return (n != null && n.Kind == PetNestJsonKind.Object) ? n : null;
        }

        /// <summary>数组元素里取字符串（自身是元素时用）。</summary>
        internal string AsString(string fallback)
        {
            return Kind == PetNestJsonKind.String ? StringValue : fallback;
        }

        /// <summary>数组元素里取整数（自身是元素时用）。</summary>
        internal int AsInt(int fallback)
        {
            return Kind == PetNestJsonKind.Number ? (int)Math.Round(NumberValue) : fallback;
        }

        private static readonly List<PetNestJsonNode> EmptyItems = new List<PetNestJsonNode>();

        #endregion
    }

    /// <summary>
    /// 极简 JSON 写入器：显式 Begin/End，自动维护逗号。
    /// 不做缩进（存档体积优先）。
    /// </summary>
    internal sealed class PetNestJsonBuilder
    {
        private readonly StringBuilder _sb;
        private bool _needComma;

        internal PetNestJsonBuilder()
        {
            _sb = new StringBuilder(1024);
        }

        private void Separator()
        {
            if (_needComma) _sb.Append(',');
            _needComma = true;
        }

        /// <summary>写一个带引号的 JSON 字符串。EscapeString 只转义、不带引号，引号在这里补。</summary>
        private void Quoted(string value)
        {
            _sb.Append('"');
            SimpleJsonHelper.EscapeString(_sb, value);
            _sb.Append('"');
        }

        private void Key(string name)
        {
            Separator();
            Quoted(name);
            _sb.Append(':');
        }

        internal PetNestJsonBuilder BeginObject()
        {
            Separator();
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        internal PetNestJsonBuilder BeginObject(string name)
        {
            Key(name);
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        internal PetNestJsonBuilder EndObject()
        {
            _sb.Append('}');
            _needComma = true;
            return this;
        }

        internal PetNestJsonBuilder BeginArray(string name)
        {
            Key(name);
            _sb.Append('[');
            _needComma = false;
            return this;
        }

        internal PetNestJsonBuilder EndArray()
        {
            _sb.Append(']');
            _needComma = true;
            return this;
        }

        internal PetNestJsonBuilder Str(string name, string value)
        {
            Key(name);
            if (value == null) _sb.Append("null");
            else Quoted(value);
            return this;
        }

        internal PetNestJsonBuilder Int(string name, int value)
        {
            Key(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        internal PetNestJsonBuilder Long(string name, long value)
        {
            Key(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        internal PetNestJsonBuilder Num(string name, float value)
        {
            Key(name);
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        internal PetNestJsonBuilder Bool(string name, bool value)
        {
            Key(name);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        /// <summary>
        /// 内联一段**已经是合法 JSON** 的文本（envelope 包 payload 用）。
        /// 调用方负责保证 rawJson 合法；传 null 写 null。
        /// </summary>
        internal PetNestJsonBuilder Raw(string name, string rawJson)
        {
            Key(name);
            if (string.IsNullOrEmpty(rawJson)) _sb.Append("null");
            else _sb.Append(rawJson);
            return this;
        }

        /// <summary>数组元素：裸整数。</summary>
        internal PetNestJsonBuilder ItemInt(int value)
        {
            Separator();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>数组元素：裸字符串。</summary>
        internal PetNestJsonBuilder ItemStr(string value)
        {
            Separator();
            if (value == null) _sb.Append("null");
            else Quoted(value);
            return this;
        }

        public override string ToString()
        {
            return _sb.ToString();
        }
    }

    /// <summary>极简 JSON 解析器。递归下降，失败返回 null。</summary>
    internal static class PetNestJson
    {
        /// <summary>解析 JSON 文本。任何语法错误一律返回 null（调用方 fail-closed）。</summary>
        internal static PetNestJsonNode Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                int index = 0;
                PetNestJsonNode root = ParseValue(text, ref index, 0);
                if (root == null) return null;
                SkipWhitespace(text, ref index);
                // 尾部允许空白，不允许残留内容
                return index >= text.Length ? root : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private const int MaxDepth = 24;

        private static PetNestJsonNode ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) return null;
            SkipWhitespace(s, ref i);
            if (i >= s.Length) return null;

            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"')
            {
                string str = ParseString(s, ref i);
                if (str == null) return null;
                return new PetNestJsonNode { Kind = PetNestJsonKind.String, StringValue = str };
            }
            if (c == 't' && Match(s, i, "true")) { i += 4; return new PetNestJsonNode { Kind = PetNestJsonKind.Bool, BoolValue = true }; }
            if (c == 'f' && Match(s, i, "false")) { i += 5; return new PetNestJsonNode { Kind = PetNestJsonKind.Bool, BoolValue = false }; }
            if (c == 'n' && Match(s, i, "null")) { i += 4; return new PetNestJsonNode { Kind = PetNestJsonKind.Null }; }
            return ParseNumber(s, ref i);
        }

        private static PetNestJsonNode ParseObject(string s, ref int i, int depth)
        {
            i++; // '{'
            PetNestJsonNode node = new PetNestJsonNode
            {
                Kind = PetNestJsonKind.Object,
                Members = new Dictionary<string, PetNestJsonNode>(StringComparer.Ordinal),
            };
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return node; }

            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') return null;
                string key = ParseString(s, ref i);
                if (key == null) return null;

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') return null;
                i++;

                PetNestJsonNode value = ParseValue(s, ref i, depth + 1);
                if (value == null) return null;
                node.Members[key] = value;

                SkipWhitespace(s, ref i);
                if (i >= s.Length) return null;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return node; }
                return null;
            }
            return null;
        }

        private static PetNestJsonNode ParseArray(string s, ref int i, int depth)
        {
            i++; // '['
            PetNestJsonNode node = new PetNestJsonNode
            {
                Kind = PetNestJsonKind.Array,
                Items = new List<PetNestJsonNode>(),
            };
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return node; }

            while (i < s.Length)
            {
                PetNestJsonNode value = ParseValue(s, ref i, depth + 1);
                if (value == null) return null;
                node.Items.Add(value);

                SkipWhitespace(s, ref i);
                if (i >= s.Length) return null;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return node; }
                return null;
            }
            return null;
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // 开引号
            StringBuilder sb = new StringBuilder(32);
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length) return null;
                    char e = s[i];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= s.Length) return null;
                            int code;
                            if (!int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out code))
                            {
                                return null;
                            }
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: return null;
                    }
                    i++;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return null;
        }

        private static PetNestJsonNode ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    i++;
                    continue;
                }
                break;
            }
            if (i == start) return null;

            double value;
            if (!double.TryParse(s.Substring(start, i - start),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return null;
            }
            return new PetNestJsonNode { Kind = PetNestJsonKind.Number, NumberValue = value };
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        private static bool Match(string s, int i, string literal)
        {
            if (i + literal.Length > s.Length) return false;
            return string.CompareOrdinal(s, i, literal, 0, literal.Length) == 0;
        }
    }
}

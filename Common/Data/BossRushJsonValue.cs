// Mode H 结构化 JSON token 与最小解析器（设计提案 §20.2）。
// 规范摘要必须先解析成 token 再重新写出，禁止直接对来源 JSON 文本做哈希；
// 写出规则实现在 ModeHCanonicalDigest.cs，拆分只为遵守单文件 1200 行预算。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BossRush
{
    /// <summary>JSON token 类别（Mode H 自有最小解析器）。</summary>
    public enum ModeHJsonKind
    {
        /// <summary>null</summary>
        Null = 0,
        /// <summary>true/false</summary>
        Bool = 1,
        /// <summary>整数（无小数点与指数）</summary>
        Integer = 2,
        /// <summary>有限浮点</summary>
        Float = 3,
        /// <summary>字符串</summary>
        String = 4,
        /// <summary>数组</summary>
        Array = 5,
        /// <summary>对象</summary>
        Object = 6
    }

    /// <summary>对象属性（保留解析顺序，规范写出时再排序）。</summary>
    public sealed class ModeHJsonProperty
    {
        /// <summary>属性名。</summary>
        public string Name;
        /// <summary>属性值。</summary>
        public ModeHJsonValue Value;
    }

    /// <summary>
    /// Mode H 结构化 JSON token。规范摘要必须先解析成 token 再重新写出，
    /// 禁止直接对来源 JSON 文本或 Dictionary 默认输出做哈希（§20.2）。
    /// </summary>
    public sealed class ModeHJsonValue
    {
        /// <summary>token 类别。</summary>
        public ModeHJsonKind Kind;
        /// <summary>布尔值。</summary>
        public bool BoolValue;
        /// <summary>整数值。</summary>
        public long IntegerValue;
        /// <summary>浮点值。</summary>
        public double FloatValue;
        /// <summary>字符串值。</summary>
        public string StringValue;
        /// <summary>数组元素。</summary>
        public List<ModeHJsonValue> Items;
        /// <summary>对象属性。</summary>
        public List<ModeHJsonProperty> Properties;

        /// <summary>构造 null token。</summary>
        public static ModeHJsonValue NewNull()
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.Null;
            return v;
        }

        /// <summary>构造布尔 token。</summary>
        public static ModeHJsonValue NewBool(bool value)
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.Bool;
            v.BoolValue = value;
            return v;
        }

        /// <summary>构造整数 token。</summary>
        public static ModeHJsonValue NewInteger(long value)
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.Integer;
            v.IntegerValue = value;
            return v;
        }

        /// <summary>构造浮点 token。</summary>
        public static ModeHJsonValue NewFloat(double value)
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.Float;
            v.FloatValue = value;
            return v;
        }

        /// <summary>构造字符串 token。</summary>
        public static ModeHJsonValue NewString(string value)
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.String;
            v.StringValue = value;
            return v;
        }

        /// <summary>构造数组 token。</summary>
        public static ModeHJsonValue NewArray()
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.Array;
            v.Items = new List<ModeHJsonValue>();
            return v;
        }

        /// <summary>构造对象 token。</summary>
        public static ModeHJsonValue NewObject()
        {
            ModeHJsonValue v = new ModeHJsonValue();
            v.Kind = ModeHJsonKind.Object;
            v.Properties = new List<ModeHJsonProperty>();
            return v;
        }

        /// <summary>追加对象属性（不做重名检查，写出时统一检查）。</summary>
        public void AddProperty(string name, ModeHJsonValue value)
        {
            if (Properties == null) Properties = new List<ModeHJsonProperty>();
            ModeHJsonProperty p = new ModeHJsonProperty();
            p.Name = name;
            p.Value = value;
            Properties.Add(p);
        }

        /// <summary>按名取属性值；不存在返回 null。</summary>
        public ModeHJsonValue GetProperty(string name)
        {
            if (Kind != ModeHJsonKind.Object || Properties == null || name == null) return null;
            for (int i = 0; i < Properties.Count; i++)
            {
                ModeHJsonProperty p = Properties[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.Ordinal)) return p.Value;
            }
            return null;
        }

        /// <summary>移除同名属性（用于排除摘要自身字段）。</summary>
        public bool RemoveProperty(string name)
        {
            if (Kind != ModeHJsonKind.Object || Properties == null || name == null) return false;
            bool removed = false;
            for (int i = Properties.Count - 1; i >= 0; i--)
            {
                ModeHJsonProperty p = Properties[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.Ordinal))
                {
                    Properties.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        /// <summary>读取字符串属性。</summary>
        public bool TryGetString(string name, out string value)
        {
            value = null;
            ModeHJsonValue v = GetProperty(name);
            if (v == null || v.Kind != ModeHJsonKind.String) return false;
            value = v.StringValue;
            return true;
        }

        /// <summary>读取整数属性。</summary>
        public bool TryGetInt(string name, out int value)
        {
            value = 0;
            ModeHJsonValue v = GetProperty(name);
            if (v == null || v.Kind != ModeHJsonKind.Integer) return false;
            if (v.IntegerValue > int.MaxValue || v.IntegerValue < int.MinValue) return false;
            value = (int)v.IntegerValue;
            return true;
        }

        /// <summary>读取浮点属性（整数 token 也接受）。</summary>
        public bool TryGetFloat(string name, out float value)
        {
            value = 0f;
            ModeHJsonValue v = GetProperty(name);
            if (v == null) return false;
            if (v.Kind == ModeHJsonKind.Integer) { value = v.IntegerValue; return true; }
            if (v.Kind != ModeHJsonKind.Float) return false;
            if (double.IsNaN(v.FloatValue) || double.IsInfinity(v.FloatValue)) return false;
            value = (float)v.FloatValue;
            return true;
        }

        /// <summary>读取布尔属性。</summary>
        public bool TryGetBool(string name, out bool value)
        {
            value = false;
            ModeHJsonValue v = GetProperty(name);
            if (v == null || v.Kind != ModeHJsonKind.Bool) return false;
            value = v.BoolValue;
            return true;
        }

        /// <summary>读取数组属性。</summary>
        public bool TryGetArray(string name, out List<ModeHJsonValue> items)
        {
            items = null;
            ModeHJsonValue v = GetProperty(name);
            if (v == null || v.Kind != ModeHJsonKind.Array) return false;
            items = v.Items != null ? v.Items : new List<ModeHJsonValue>();
            return true;
        }

        /// <summary>读取对象属性。</summary>
        public bool TryGetObject(string name, out ModeHJsonValue obj)
        {
            obj = null;
            ModeHJsonValue v = GetProperty(name);
            if (v == null || v.Kind != ModeHJsonKind.Object) return false;
            obj = v;
            return true;
        }

        /// <summary>读取字符串数组属性（元素必须全为字符串）。</summary>
        public bool TryGetStringList(string name, out List<string> values)
        {
            values = null;
            List<ModeHJsonValue> items;
            if (!TryGetArray(name, out items)) return false;
            List<string> result = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.String) return false;
                result.Add(item.StringValue);
            }
            values = result;
            return true;
        }
    }

    /// <summary>Mode H 最小 JSON 解析器（no-throw，失败返回 error id）。</summary>
    public static class ModeHJsonParser
    {
        private const int MaxDepth = 32;

        #region JSON 解析

        /// <summary>解析 JSON 文本为 token 树；失败返回 false 与 error id（no-throw）。</summary>
        public static bool TryParse(string json, out ModeHJsonValue root, out string error)
        {
            root = null;
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "json_empty";
                return false;
            }
            try
            {
                int index = 0;
                // 跳过 UTF-8 BOM
                if (json.Length > 0 && json[0] == '﻿') index = 1;
                ModeHJsonValue value;
                if (!ParseValue(json, ref index, 0, out value, out error)) return false;
                SkipWhitespace(json, ref index);
                if (index != json.Length)
                {
                    error = "json_trailing_content";
                    return false;
                }
                root = value;
                return true;
            }
            catch (Exception e)
            {
                error = "json_parse_exception:" + e.GetType().Name;
                return false;
            }
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        private static bool ParseValue(string s, ref int i, int depth, out ModeHJsonValue value, out string error)
        {
            value = null;
            error = null;
            if (depth > MaxDepth)
            {
                error = "json_depth_exceeded";
                return false;
            }
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
            {
                error = "json_unexpected_end";
                return false;
            }
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth, out value, out error);
            if (c == '[') return ParseArray(s, ref i, depth, out value, out error);
            if (c == '"')
            {
                string text;
                if (!ParseString(s, ref i, out text, out error)) return false;
                value = ModeHJsonValue.NewString(text);
                return true;
            }
            if (c == 't')
            {
                if (!MatchLiteral(s, ref i, "true", out error)) return false;
                value = ModeHJsonValue.NewBool(true);
                return true;
            }
            if (c == 'f')
            {
                if (!MatchLiteral(s, ref i, "false", out error)) return false;
                value = ModeHJsonValue.NewBool(false);
                return true;
            }
            if (c == 'n')
            {
                if (!MatchLiteral(s, ref i, "null", out error)) return false;
                value = ModeHJsonValue.NewNull();
                return true;
            }
            return ParseNumber(s, ref i, out value, out error);
        }

        private static bool MatchLiteral(string s, ref int i, string literal, out string error)
        {
            error = null;
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
            {
                error = "json_bad_literal";
                return false;
            }
            i += literal.Length;
            return true;
        }

        private static bool ParseObject(string s, ref int i, int depth, out ModeHJsonValue value, out string error)
        {
            value = null;
            error = null;
            ModeHJsonValue obj = ModeHJsonValue.NewObject();
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                value = obj;
                return true;
            }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    error = "json_expected_property_name";
                    return false;
                }
                string name;
                if (!ParseString(s, ref i, out name, out error)) return false;
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    error = "json_expected_colon";
                    return false;
                }
                i++;
                ModeHJsonValue child;
                if (!ParseValue(s, ref i, depth + 1, out child, out error)) return false;
                obj.AddProperty(name, child);
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                {
                    error = "json_unexpected_end";
                    return false;
                }
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == '}')
                {
                    i++;
                    value = obj;
                    return true;
                }
                error = "json_expected_comma_or_brace";
                return false;
            }
        }

        private static bool ParseArray(string s, ref int i, int depth, out ModeHJsonValue value, out string error)
        {
            value = null;
            error = null;
            ModeHJsonValue array = ModeHJsonValue.NewArray();
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                value = array;
                return true;
            }
            while (true)
            {
                ModeHJsonValue child;
                if (!ParseValue(s, ref i, depth + 1, out child, out error)) return false;
                array.Items.Add(child);
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                {
                    error = "json_unexpected_end";
                    return false;
                }
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == ']')
                {
                    i++;
                    value = array;
                    return true;
                }
                error = "json_expected_comma_or_bracket";
                return false;
            }
        }

        private static bool ParseString(string s, ref int i, out string text, out string error)
        {
            text = null;
            error = null;
            i++; // '"'
            StringBuilder sb = new StringBuilder(32);
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    i++;
                    text = sb.ToString();
                    return true;
                }
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length)
                    {
                        error = "json_bad_escape";
                        return false;
                    }
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
                            if (i + 4 >= s.Length)
                            {
                                error = "json_bad_unicode_escape";
                                return false;
                            }
                            int code;
                            if (!int.TryParse(
                                    s.Substring(i + 1, 4),
                                    NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture,
                                    out code))
                            {
                                error = "json_bad_unicode_escape";
                                return false;
                            }
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default:
                            error = "json_bad_escape";
                            return false;
                    }
                    i++;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            error = "json_unterminated_string";
            return false;
        }

        private static bool ParseNumber(string s, ref int i, out ModeHJsonValue value, out string error)
        {
            value = null;
            error = null;
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { i++; continue; }
                if (c == '.' || c == 'e' || c == 'E') { isFloat = true; i++; continue; }
                if ((c == '-' || c == '+') && i > start && (s[i - 1] == 'e' || s[i - 1] == 'E')) { i++; continue; }
                break;
            }
            if (i == start)
            {
                error = "json_bad_number";
                return false;
            }
            string raw = s.Substring(start, i - start);
            if (!isFloat)
            {
                long parsed;
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    value = ModeHJsonValue.NewInteger(parsed);
                    return true;
                }
            }
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                error = "json_bad_number";
                return false;
            }
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                error = "json_non_finite_number";
                return false;
            }
            value = ModeHJsonValue.NewFloat(d);
            return true;
        }
        #endregion

    }
}

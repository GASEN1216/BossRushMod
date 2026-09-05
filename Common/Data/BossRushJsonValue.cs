// ============================================================================
// BossRushJsonValue.cs - 全 Mod 共享的结构化 JSON token、最小解析器与写出器
// ============================================================================
// 来历：本文件原是 ModeH/ModeHJsonValue.cs（设计提案 §20.2 的 Mode H 自有解析器）。
//   2026-09-06 复审（D-3）确认它早已被 Audio / Campaign / DuckNpc / F3 验收等 9 个
//   ModeH 之外的文件依赖，事实上就是全 Mod 的共享解析器，因此原样迁到 Common/Data 并去掉
//   ModeH 前缀；遗种巢自带的 PetNestJson（节点 + PetNestJsonBuilder 写出器）一并并入，
//   仓库从此只有这一套嵌套 JSON 解析器（Utilities/SimpleJsonHelper.cs 只保留扁平写出与
//   转义工具，Codex / 日报的存档读侧也改走这里）。
//
// 契约（与 ModeHCanonicalDigest 的冻结依赖）：
//   - 规范摘要必须先解析成 token 再重新写出，禁止直接对来源 JSON 文本做哈希；
//     写出规则实现在 ModeH/ModeHCanonicalDigest.cs，本文件只负责 token 与解析；
//   - 对象属性保留解析顺序（List 而不是 Dictionary），规范写出时再排序；
//   - 解析 no-throw：TryParse 返回 error id；ParseOrNull 给存档 fail-closed 路径用；
//   - 数字按 InvariantCulture 读写；拒绝 NaN / Infinity；递归深度上限 MaxDepth。
//
// 读取 API 分两组：Try* 严格按 token 类别读（Mode H 内容表 / 摘要用）；
// Get*(name, fallback) 宽松读（存档解码用：缺字段、类型不符一律回落默认值，
// 整数 / 浮点互相接受，这是 SCHEMA+ 向后兼容扩展的基础）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BossRush
{
    /// <summary>JSON token 类别（全 Mod 共享的最小解析器）。</summary>
    public enum BossRushJsonKind
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
    public sealed class BossRushJsonProperty
    {
        /// <summary>属性名。</summary>
        public string Name;
        /// <summary>属性值。</summary>
        public BossRushJsonValue Value;
    }

    /// <summary>
    /// 结构化 JSON token。Mode H 的规范摘要必须先解析成 token 再重新写出，
    /// 禁止直接对来源 JSON 文本或 Dictionary 默认输出做哈希（§20.2）；
    /// 其余子系统（遗种巢 / 图鉴 / 日报存档，内容表）用它做 fail-closed 的读取。
    /// </summary>
    public sealed class BossRushJsonValue
    {
        /// <summary>token 类别。</summary>
        public BossRushJsonKind Kind;
        /// <summary>布尔值。</summary>
        public bool BoolValue;
        /// <summary>整数值。</summary>
        public long IntegerValue;
        /// <summary>浮点值。</summary>
        public double FloatValue;
        /// <summary>字符串值。</summary>
        public string StringValue;
        /// <summary>数组元素。</summary>
        public List<BossRushJsonValue> Items;
        /// <summary>对象属性。</summary>
        public List<BossRushJsonProperty> Properties;

        /// <summary>构造 null token。</summary>
        public static BossRushJsonValue NewNull()
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.Null;
            return v;
        }

        /// <summary>构造布尔 token。</summary>
        public static BossRushJsonValue NewBool(bool value)
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.Bool;
            v.BoolValue = value;
            return v;
        }

        /// <summary>构造整数 token。</summary>
        public static BossRushJsonValue NewInteger(long value)
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.Integer;
            v.IntegerValue = value;
            return v;
        }

        /// <summary>构造浮点 token。</summary>
        public static BossRushJsonValue NewFloat(double value)
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.Float;
            v.FloatValue = value;
            return v;
        }

        /// <summary>构造字符串 token。</summary>
        public static BossRushJsonValue NewString(string value)
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.String;
            v.StringValue = value;
            return v;
        }

        /// <summary>构造数组 token。</summary>
        public static BossRushJsonValue NewArray()
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.Array;
            v.Items = new List<BossRushJsonValue>();
            return v;
        }

        /// <summary>构造对象 token。</summary>
        public static BossRushJsonValue NewObject()
        {
            BossRushJsonValue v = new BossRushJsonValue();
            v.Kind = BossRushJsonKind.Object;
            v.Properties = new List<BossRushJsonProperty>();
            return v;
        }

        /// <summary>追加对象属性（不做重名检查，写出时统一检查）。</summary>
        public void AddProperty(string name, BossRushJsonValue value)
        {
            if (Properties == null) Properties = new List<BossRushJsonProperty>();
            BossRushJsonProperty p = new BossRushJsonProperty();
            p.Name = name;
            p.Value = value;
            Properties.Add(p);
        }

        /// <summary>按名取属性值；不存在返回 null。</summary>
        public BossRushJsonValue GetProperty(string name)
        {
            if (Kind != BossRushJsonKind.Object || Properties == null || name == null) return null;
            for (int i = 0; i < Properties.Count; i++)
            {
                BossRushJsonProperty p = Properties[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.Ordinal)) return p.Value;
            }
            return null;
        }

        /// <summary>移除同名属性（用于排除摘要自身字段）。</summary>
        public bool RemoveProperty(string name)
        {
            if (Kind != BossRushJsonKind.Object || Properties == null || name == null) return false;
            bool removed = false;
            for (int i = Properties.Count - 1; i >= 0; i--)
            {
                BossRushJsonProperty p = Properties[i];
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
            BossRushJsonValue v = GetProperty(name);
            if (v == null || v.Kind != BossRushJsonKind.String) return false;
            value = v.StringValue;
            return true;
        }

        /// <summary>读取整数属性。</summary>
        public bool TryGetInt(string name, out int value)
        {
            value = 0;
            BossRushJsonValue v = GetProperty(name);
            if (v == null || v.Kind != BossRushJsonKind.Integer) return false;
            if (v.IntegerValue > int.MaxValue || v.IntegerValue < int.MinValue) return false;
            value = (int)v.IntegerValue;
            return true;
        }

        /// <summary>读取浮点属性（整数 token 也接受）。</summary>
        public bool TryGetFloat(string name, out float value)
        {
            value = 0f;
            BossRushJsonValue v = GetProperty(name);
            if (v == null) return false;
            if (v.Kind == BossRushJsonKind.Integer) { value = v.IntegerValue; return true; }
            if (v.Kind != BossRushJsonKind.Float) return false;
            if (double.IsNaN(v.FloatValue) || double.IsInfinity(v.FloatValue)) return false;
            value = (float)v.FloatValue;
            return true;
        }

        /// <summary>读取布尔属性。</summary>
        public bool TryGetBool(string name, out bool value)
        {
            value = false;
            BossRushJsonValue v = GetProperty(name);
            if (v == null || v.Kind != BossRushJsonKind.Bool) return false;
            value = v.BoolValue;
            return true;
        }

        /// <summary>读取数组属性。</summary>
        public bool TryGetArray(string name, out List<BossRushJsonValue> items)
        {
            items = null;
            BossRushJsonValue v = GetProperty(name);
            if (v == null || v.Kind != BossRushJsonKind.Array) return false;
            items = v.Items != null ? v.Items : new List<BossRushJsonValue>();
            return true;
        }

        /// <summary>读取对象属性。</summary>
        public bool TryGetObject(string name, out BossRushJsonValue obj)
        {
            obj = null;
            BossRushJsonValue v = GetProperty(name);
            if (v == null || v.Kind != BossRushJsonKind.Object) return false;
            obj = v;
            return true;
        }

        /// <summary>读取字符串数组属性（元素必须全为字符串）。</summary>
        public bool TryGetStringList(string name, out List<string> values)
        {
            values = null;
            List<BossRushJsonValue> items;
            if (!TryGetArray(name, out items)) return false;
            List<string> result = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                BossRushJsonValue item = items[i];
                if (item == null || item.Kind != BossRushJsonKind.String) return false;
                result.Add(item.StringValue);
            }
            values = result;
            return true;
        }

        /// <summary>读取长整数属性（整数 token）。</summary>
        public bool TryGetLong(string name, out long value)
        {
            value = 0L;
            BossRushJsonValue v = GetProperty(name);
            if (v == null || v.Kind != BossRushJsonKind.Integer) return false;
            value = v.IntegerValue;
            return true;
        }

        #region 带默认值的宽松读取（存档解码用；全部 no-throw）

        // 缺失 / 非数组时返回的共享空列表：调用方只读遍历，不得修改。
        private static readonly List<BossRushJsonValue> EmptyItems = new List<BossRushJsonValue>();

        /// <summary>字符串属性；缺失、null 或类型不符返回 fallback。</summary>
        public string GetString(string name, string fallback)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null && v.Kind == BossRushJsonKind.String ? v.StringValue : fallback;
        }

        /// <summary>整数属性；浮点 token 四舍五入接受，越界或类型不符返回 fallback。</summary>
        public int GetInt(string name, int fallback)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null ? v.AsInt(fallback) : fallback;
        }

        /// <summary>长整数属性；浮点 token 四舍五入接受，越界或类型不符返回 fallback。</summary>
        public long GetLong(string name, long fallback)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null ? v.AsLong(fallback) : fallback;
        }

        /// <summary>浮点属性；整数 token 也接受，类型不符返回 fallback。</summary>
        public float GetFloat(string name, float fallback)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null ? v.AsFloat(fallback) : fallback;
        }

        /// <summary>布尔属性；类型不符返回 fallback。</summary>
        public bool GetBool(string name, bool fallback)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null && v.Kind == BossRushJsonKind.Bool ? v.BoolValue : fallback;
        }

        /// <summary>数组属性；缺失或不是数组时返回共享的空列表（调用方无需判空，不得修改）。</summary>
        public List<BossRushJsonValue> GetArray(string name)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null && v.Kind == BossRushJsonKind.Array && v.Items != null ? v.Items : EmptyItems;
        }

        /// <summary>对象属性；缺失或不是对象时返回 null。</summary>
        public BossRushJsonValue GetObject(string name)
        {
            BossRushJsonValue v = GetProperty(name);
            return v != null && v.Kind == BossRushJsonKind.Object ? v : null;
        }

        /// <summary>自身作为字符串元素读（数组遍历用）。</summary>
        public string AsString(string fallback)
        {
            return Kind == BossRushJsonKind.String ? StringValue : fallback;
        }

        /// <summary>自身作为整数元素读；浮点四舍五入，越界回落 fallback。</summary>
        public int AsInt(int fallback)
        {
            if (Kind == BossRushJsonKind.Integer)
            {
                return IntegerValue > int.MaxValue || IntegerValue < int.MinValue ? fallback : (int)IntegerValue;
            }
            if (Kind == BossRushJsonKind.Float)
            {
                double rounded = Math.Round(FloatValue);
                return rounded > int.MaxValue || rounded < int.MinValue ? fallback : (int)rounded;
            }
            return fallback;
        }

        /// <summary>自身作为长整数元素读；浮点四舍五入，越界回落 fallback。</summary>
        public long AsLong(long fallback)
        {
            if (Kind == BossRushJsonKind.Integer) return IntegerValue;
            if (Kind == BossRushJsonKind.Float)
            {
                double rounded = Math.Round(FloatValue);
                return rounded > long.MaxValue || rounded < long.MinValue ? fallback : (long)rounded;
            }
            return fallback;
        }

        /// <summary>自身作为浮点元素读；整数 token 也接受。</summary>
        public float AsFloat(float fallback)
        {
            if (Kind == BossRushJsonKind.Integer) return IntegerValue;
            if (Kind == BossRushJsonKind.Float) return (float)FloatValue;
            return fallback;
        }

        #endregion
    }

    /// <summary>全 Mod 共享的最小 JSON 解析器（no-throw，失败返回 error id）。</summary>
    public static class BossRushJsonParser
    {
        private const int MaxDepth = 32;

        #region JSON 解析

        /// <summary>解析 JSON 文本为 token 树；失败返回 false 与 error id（no-throw）。</summary>
        public static bool TryParse(string json, out BossRushJsonValue root, out string error)
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
                BossRushJsonValue value;
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

        /// <summary>
        /// 解析失败一律返回 null 的便捷入口。存档 fail-closed 路径用：调用方只关心
        /// 「读不读得动」，读不动就进写屏障、绝不覆盖原 key。
        /// </summary>
        public static BossRushJsonValue ParseOrNull(string json)
        {
            BossRushJsonValue root;
            string error;
            return TryParse(json, out root, out error) ? root : null;
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

        private static bool ParseValue(string s, ref int i, int depth, out BossRushJsonValue value, out string error)
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
                value = BossRushJsonValue.NewString(text);
                return true;
            }
            if (c == 't')
            {
                if (!MatchLiteral(s, ref i, "true", out error)) return false;
                value = BossRushJsonValue.NewBool(true);
                return true;
            }
            if (c == 'f')
            {
                if (!MatchLiteral(s, ref i, "false", out error)) return false;
                value = BossRushJsonValue.NewBool(false);
                return true;
            }
            if (c == 'n')
            {
                if (!MatchLiteral(s, ref i, "null", out error)) return false;
                value = BossRushJsonValue.NewNull();
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

        private static bool ParseObject(string s, ref int i, int depth, out BossRushJsonValue value, out string error)
        {
            value = null;
            error = null;
            BossRushJsonValue obj = BossRushJsonValue.NewObject();
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
                BossRushJsonValue child;
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

        private static bool ParseArray(string s, ref int i, int depth, out BossRushJsonValue value, out string error)
        {
            value = null;
            error = null;
            BossRushJsonValue array = BossRushJsonValue.NewArray();
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
                BossRushJsonValue child;
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

        private static bool ParseNumber(string s, ref int i, out BossRushJsonValue value, out string error)
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
                    value = BossRushJsonValue.NewInteger(parsed);
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
            value = BossRushJsonValue.NewFloat(d);
            return true;
        }
        #endregion

    }

    /// <summary>
    /// 极简 JSON 写入器：显式 Begin/End，自动维护逗号，不做缩进（存档体积优先）。
    /// 原为遗种巢的 PetNestJsonBuilder，2026-09-06 并入共享解析器；转义复用
    /// SimpleJsonHelper.EscapeString，不再造第二套。
    /// </summary>
    public sealed class BossRushJsonWriter
    {
        private readonly StringBuilder _sb;
        private bool _needComma;

        public BossRushJsonWriter()
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

        public BossRushJsonWriter BeginObject()
        {
            Separator();
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        public BossRushJsonWriter BeginObject(string name)
        {
            Key(name);
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        public BossRushJsonWriter EndObject()
        {
            _sb.Append('}');
            _needComma = true;
            return this;
        }

        public BossRushJsonWriter BeginArray(string name)
        {
            Key(name);
            _sb.Append('[');
            _needComma = false;
            return this;
        }

        public BossRushJsonWriter EndArray()
        {
            _sb.Append(']');
            _needComma = true;
            return this;
        }

        public BossRushJsonWriter Str(string name, string value)
        {
            Key(name);
            if (value == null) _sb.Append("null");
            else Quoted(value);
            return this;
        }

        public BossRushJsonWriter Int(string name, int value)
        {
            Key(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public BossRushJsonWriter Long(string name, long value)
        {
            Key(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public BossRushJsonWriter Num(string name, float value)
        {
            Key(name);
            // NaN / ±Infinity 的 "R" 输出是 "NaN" / "Infinity"，**不是合法 JSON**：
            // 写进去之后下次加载会解析失败 -> 该 key 进写屏障 -> 玩家从此静默存不上档。
            // 非有限值一律写 0，宁可丢一个数值也不能毁掉整份存档。
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                _sb.Append('0');
                return this;
            }
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public BossRushJsonWriter Bool(string name, bool value)
        {
            Key(name);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        /// <summary>
        /// 内联一段**已经是合法 JSON** 的文本（envelope 包 payload 用）。
        /// 调用方负责保证 rawJson 合法；传 null 写 null。
        /// </summary>
        public BossRushJsonWriter Raw(string name, string rawJson)
        {
            Key(name);
            if (string.IsNullOrEmpty(rawJson)) _sb.Append("null");
            else _sb.Append(rawJson);
            return this;
        }

        /// <summary>数组元素：裸整数。</summary>
        public BossRushJsonWriter ItemInt(int value)
        {
            Separator();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>数组元素：裸字符串。</summary>
        public BossRushJsonWriter ItemStr(string value)
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
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// 动态加载的 Mod DTO 不进入 Unity 构建期类型树，JsonUtility 会静默丢失嵌套类字段。
    /// 网络边界使用静态目标类型的字段映射；解析、转义沿用共享 JSON，不读取远端类型名。
    /// </summary>
    internal static class BossRushCoopJson
    {
        private const int MaxDepth = 28;
        private static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
        internal static void ResetStaticCaches() { lock (fieldCache) fieldCache.Clear(); }

        internal static string ToJson(object value)
        {
            var text = new StringBuilder(512);
            Write(text, value, 0);
            return text.ToString();
        }

        internal static T FromJson<T>(string json)
        {
            BossRushJsonValue root;
            string error;
            if (!BossRushJsonParser.TryParse(json, out root, out error)) throw new FormatException(error);
            return (T)Read(root, typeof(T), 0);
        }

        private static FieldInfo[] Fields(Type type)
        {
            lock (fieldCache)
            {
                FieldInfo[] fields;
                if (fieldCache.TryGetValue(type, out fields)) return fields;
                for (Type parent = type; parent != null; parent = parent.BaseType)
                    if (parent.FullName == "UnityEngine.Object") throw new NotSupportedException("Network DTO cannot contain engine objects.");
                // Vector3/Color 和官方 ItemTreeData 的部分叶节点没有 Serializable 标记。
                // 这里依赖编译期字段类型，不依赖 Unity 类型树或远端提供的类型名。
                if (type.IsPointer || typeof(Delegate).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
                    throw new NotSupportedException("Unsupported network DTO type: " + type.FullName);
                var selected = new List<FieldInfo>();
                for (Type parent = type; parent != null && parent != typeof(object); parent = parent.BaseType)
                    foreach (FieldInfo field in parent.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (field.IsNotSerialized || field.IsInitOnly || field.IsLiteral) continue;
                        bool visible = field.IsPublic;
                        if (!visible)
                            foreach (CustomAttributeData attribute in field.GetCustomAttributesData())
                                if (attribute.AttributeType.FullName == "UnityEngine.SerializeField") { visible = true; break; }
                        if (visible) selected.Add(field);
                    }
                selected.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                fields = selected.ToArray();
                fieldCache.Add(type, fields);
                return fields;
            }
        }

        private static void Quote(StringBuilder text, string value)
        { text.Append('"'); SimpleJsonHelper.EscapeString(text, value); text.Append('"'); }

        private static void Write(StringBuilder text, object value, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Network DTO is too deep or cyclic.");
            if (value == null) { text.Append("null"); return; }
            Type type = value.GetType();
            if (type == typeof(string) || type == typeof(char)) { Quote(text, Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            if (type == typeof(bool)) { text.Append((bool)value ? "true" : "false"); return; }
            // 共享 token 的整数域为 Int64；Mode G 的完整 UInt64 种子用十进制字符串保留全部位。
            if (type == typeof(ulong)) { Quote(text, ((ulong)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (type.IsEnum) { text.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture)); return; }
            if (type.IsPrimitive || type == typeof(decimal))
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Non-finite network number.");
                if (value is float) text.Append(((float)value).ToString("R", CultureInfo.InvariantCulture));
                else if (value is double) text.Append(((double)value).ToString("R", CultureInfo.InvariantCulture));
                else text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            IList list = value as IList;
            if (list != null)
            {
                text.Append('[');
                for (int i = 0; i < list.Count; i++) { if (i != 0) text.Append(','); Write(text, list[i], depth + 1); }
                text.Append(']'); return;
            }
            if (value is IDictionary || value is Delegate) throw new NotSupportedException("Network DTO contains an unsupported collection or delegate.");
            text.Append('{');
            bool comma = false;
            foreach (FieldInfo field in Fields(type))
            {
                if (comma) text.Append(','); comma = true;
                Quote(text, field.Name); text.Append(':'); Write(text, field.GetValue(value), depth + 1);
            }
            text.Append('}');
        }

        private static object Read(BossRushJsonValue value, Type type, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Network DTO is too deep.");
            if (value == null || value.Kind == BossRushJsonKind.Null) return type.IsValueType ? Activator.CreateInstance(type) : null;
            if (type == typeof(string)) { Require(value.Kind == BossRushJsonKind.String); return value.StringValue; }
            if (type == typeof(char)) { Require(value.Kind == BossRushJsonKind.String && value.StringValue.Length == 1); return value.StringValue[0]; }
            if (type == typeof(bool)) { Require(value.Kind == BossRushJsonKind.Bool); return value.BoolValue; }
            if (type == typeof(ulong))
            {
                ulong unsigned;
                if (value.Kind == BossRushJsonKind.String && ulong.TryParse(value.StringValue, NumberStyles.None, CultureInfo.InvariantCulture, out unsigned)) return unsigned;
                Require(value.Kind == BossRushJsonKind.Integer && value.IntegerValue >= 0);
                return (ulong)value.IntegerValue;
            }
            if (type.IsEnum) return Enum.ToObject(type, Read(value, Enum.GetUnderlyingType(type), depth));
            if (type.IsPrimitive || type == typeof(decimal))
            {
                Require(value.Kind == BossRushJsonKind.Integer || value.Kind == BossRushJsonKind.Float);
                object number = value.Kind == BossRushJsonKind.Integer ? (object)value.IntegerValue : value.FloatValue;
                if (type != typeof(float) && type != typeof(double) && type != typeof(decimal)) Require(value.Kind == BossRushJsonKind.Integer);
                object converted = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                if (converted is float) Require(!float.IsInfinity((float)converted) && !float.IsNaN((float)converted));
                return converted;
            }
            if (type.IsArray)
            {
                Require(value.Kind == BossRushJsonKind.Array && type.GetArrayRank() == 1);
                Type element = type.GetElementType();
                Array result = Array.CreateInstance(element, value.Items.Count);
                for (int i = 0; i < value.Items.Count; i++) result.SetValue(Read(value.Items[i], element, depth + 1), i);
                return result;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Require(value.Kind == BossRushJsonKind.Array);
                var result = (IList)Activator.CreateInstance(type);
                Type element = type.GetGenericArguments()[0];
                foreach (BossRushJsonValue item in value.Items) result.Add(Read(item, element, depth + 1));
                return result;
            }
            Require(value.Kind == BossRushJsonKind.Object);
            FieldInfo[] fields = Fields(type);
            object instance = type.IsValueType ? Activator.CreateInstance(type) : FormatterServices.GetUninitializedObject(type);
            foreach (FieldInfo field in fields)
            {
                BossRushJsonValue member = value.GetProperty(field.Name);
                if (member != null) field.SetValue(instance, Read(member, field.FieldType, depth + 1));
            }
            return instance;
        }

        private static void Require(bool valid) { if (!valid) throw new FormatException("Network DTO field has the wrong JSON type."); }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>JsonUtility 不序列化 Dictionary；因子仍使用原配置字段名，缺字段的旧配置按默认因子处理。</summary>
    internal static class BossPoolFactorJson
    {
        private const string Field = "bossInfiniteHellFactors";

        internal static Dictionary<string, float> Read(string json)
        {
            BossRushJsonValue root; string error;
            if (!BossRushJsonParser.TryParse(json, out root, out error) || root.Kind != BossRushJsonKind.Object)
                throw new FormatException("invalid boss configuration: " + error);
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            BossRushJsonValue factors = root.GetProperty(Field);
            if (factors == null || factors.Kind == BossRushJsonKind.Null) return result;
            if (factors.Kind != BossRushJsonKind.Object) throw new FormatException("invalid boss factors");
            foreach (BossRushJsonProperty property in factors.Properties)
            {
                float value = property.Value == null ? float.NaN : property.Value.AsFloat(float.NaN);
                if (string.IsNullOrEmpty(property.Name) || float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                    throw new FormatException("invalid boss factor: " + property.Name);
                result.Add(property.Name, value);
            }
            return result;
        }

        internal static string Write(string scalarJson, Dictionary<string, float> factors)
        {
            // scalarJson 来自 JsonUtility，它只包含可序列化的标量与列表。保持这些字段的既有表示。
            int closing = scalarJson.LastIndexOf('}');
            if (closing < 0) throw new FormatException("invalid scalar configuration");
            var sb = new StringBuilder(scalarJson.Substring(0, closing).TrimEnd());
            if (sb.Length > 0 && sb[sb.Length - 1] != '{') sb.Append(',');
            sb.Append("\n  \"").Append(Field).Append("\":{");
            bool first = true;
            if (factors != null)
            {
                var names = new List<string>(factors.Keys); names.Sort(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    float value = factors[name];
                    if (string.IsNullOrEmpty(name) || float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                        throw new FormatException("invalid boss factor: " + name);
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"'); SimpleJsonHelper.EscapeString(sb, name); sb.Append("\":");
                    sb.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return sb.Append("}\n}").ToString();
        }
    }
}

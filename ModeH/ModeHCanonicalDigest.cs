using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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

    /// <summary>
    /// Mode H 三签名、规范 JSON、SHA-256 与集合排序的唯一实现（设计提案 §20.2）。
    ///
    /// 冻结规则：
    /// - signatureAlgorithmVersion = 1，SHA-256，64 个字符的小写十六进制文本；
    /// - gameBuildSignature / modBuildSignature 是当前实际加载程序集文件的原始字节摘要；
    /// - contentSignature = 移除根 contentSignature 后的规范 JSON 摘要；
    /// - contentCatalogSignature = (相对路径, contentSignature) 数组按路径 ordinal 排序后的规范 JSON 摘要；
    /// - payloadDigest = 移除根 payloadDigest 后的完整 envelope 规范 JSON 摘要；
    /// - 规范 JSON：UTF-8 无 BOM、无空白、对象属性名 ordinal 升序、普通数组保序、
    ///   集合语义字段排序去重、指定对象数组按指定键排序、invariant 数字、拒绝 NaN/Infinity。
    ///
    /// 全部方法 no-throw 且 fail-closed：任何一步失败返回 false 并给出 error id。
    /// </summary>
    public static class ModeHCanonicalDigest
    {
        #region 常量与策略表

        /// <summary>签名算法版本。</summary>
        public const int SignatureAlgorithmVersion = 1;

        /// <summary>摘要文本长度（SHA-256 十六进制）。</summary>
        public const int DigestHexLength = 64;

        /// <summary>递归深度上限，防御环引用与畸形数据。</summary>
        private const int MaxDepth = 32;

        /// <summary>游戏程序集名（gameBuildSignature 来源）。</summary>
        private const string GameAssemblyName = "Assembly-CSharp";

        /// <summary>
        /// 语义为集合的字段：写入前按 ordinal 排序并去重（§20.2 冻结清单）。
        /// </summary>
        private static readonly HashSet<string> SetSemanticFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "unlockedKitIds",
            "appliedEventTokenIds",
            "scarIds",
            "enteredProfileIds",
            "starterKitIds",
            "relayKitIds",
            "passedStableKeys",
            "commonVerifiedCommandIds",
            "failureReasonIds"
        };

        /// <summary>
        /// 需要按指定键稳定排序的对象数组（§20.2 冻结清单）。
        /// </summary>
        private static readonly Dictionary<string, string> SortedObjectArrayFields =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "profiles", "profileId" },
                { "seasonRewardOperations", "operationId" },
                { "matchReports", "matchIndex" },
                { "behaviorStatuses", "entryId" },
                { "effectStatuses", "entryId" },
                { "records", "stableKey" },
                { "commandStatuses", "commandId" }
            };

        private static readonly object _signatureLock = new object();
        private static string _cachedGameSignature;
        private static string _cachedModSignature;

        #endregion

        #region 静态缓存重置

        /// <summary>清空构建签名缓存（Mod 重载/宿主重建时调用）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_signatureLock)
            {
                _cachedGameSignature = null;
                _cachedModSignature = null;
            }
        }

        #endregion

        #region SHA-256

        /// <summary>字节数组 -> 64 字符小写十六进制摘要。</summary>
        public static bool TryComputeSha256(byte[] bytes, out string hex, out string error)
        {
            hex = null;
            error = null;
            if (bytes == null)
            {
                error = "digest_null_input";
                return false;
            }
            try
            {
                using (SHA256 sha = SHA256.Create())
                {
                    if (sha == null)
                    {
                        error = "digest_provider_unavailable";
                        return false;
                    }
                    byte[] hash = sha.ComputeHash(bytes);
                    if (hash == null || hash.Length != 32)
                    {
                        error = "digest_length_mismatch";
                        return false;
                    }
                    StringBuilder sb = new StringBuilder(DigestHexLength);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    }
                    string result = sb.ToString();
                    if (result.Length != DigestHexLength)
                    {
                        error = "digest_length_mismatch";
                        return false;
                    }
                    hex = result;
                    return true;
                }
            }
            catch (Exception e)
            {
                error = "digest_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>UTF-8（无 BOM）文本摘要。</summary>
        public static bool TryComputeSha256OfText(string text, out string hex, out string error)
        {
            hex = null;
            error = null;
            if (text == null)
            {
                error = "digest_null_text";
                return false;
            }
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                return TryComputeSha256(bytes, out hex, out error);
            }
            catch (Exception e)
            {
                error = "digest_encode_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>摘要文本是否为合法的 64 字符小写十六进制。</summary>
        public static bool IsValidDigest(string digest)
        {
            if (string.IsNullOrEmpty(digest) || digest.Length != DigestHexLength) return false;
            for (int i = 0; i < digest.Length; i++)
            {
                char c = digest[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        #endregion

        #region 构建签名（§20.2）

        /// <summary>
        /// 当前实际加载的 Assembly-CSharp.dll 原始字节摘要。
        /// 路径为空、文件不存在、读取失败或摘要长度不符时 fail-closed。
        /// </summary>
        public static bool TryGetGameBuildSignature(out string signature, out string error)
        {
            signature = null;
            error = null;
            lock (_signatureLock)
            {
                if (_cachedGameSignature != null)
                {
                    signature = _cachedGameSignature;
                    return true;
                }
            }

            Assembly gameAssembly = ResolveLoadedAssembly(GameAssemblyName);
            if (gameAssembly == null)
            {
                error = "game_assembly_not_loaded";
                return false;
            }
            string hex;
            if (!TryComputeAssemblyFileDigest(gameAssembly, out hex, out error))
            {
                return false;
            }
            lock (_signatureLock)
            {
                _cachedGameSignature = hex;
            }
            signature = hex;
            return true;
        }

        /// <summary>当前实际加载的 BossRush.dll 原始字节摘要。</summary>
        public static bool TryGetModBuildSignature(out string signature, out string error)
        {
            signature = null;
            error = null;
            lock (_signatureLock)
            {
                if (_cachedModSignature != null)
                {
                    signature = _cachedModSignature;
                    return true;
                }
            }

            Assembly modAssembly = typeof(ModeHCanonicalDigest).Assembly;
            string hex;
            if (!TryComputeAssemblyFileDigest(modAssembly, out hex, out error))
            {
                return false;
            }
            lock (_signatureLock)
            {
                _cachedModSignature = hex;
            }
            signature = hex;
            return true;
        }

        private static Assembly ResolveLoadedAssembly(string simpleName)
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                if (assemblies == null) return null;
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly asm = assemblies[i];
                    if (asm == null) continue;
                    AssemblyName name = asm.GetName();
                    if (name == null) continue;
                    if (string.Equals(name.Name, simpleName, StringComparison.Ordinal)) return asm;
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        private static bool TryComputeAssemblyFileDigest(Assembly assembly, out string hex, out string error)
        {
            hex = null;
            error = null;
            if (assembly == null)
            {
                error = "assembly_null";
                return false;
            }
            string location;
            try
            {
                location = assembly.Location;
            }
            catch (Exception)
            {
                error = "assembly_location_unavailable";
                return false;
            }
            if (string.IsNullOrEmpty(location))
            {
                error = "assembly_location_empty";
                return false;
            }
            byte[] bytes;
            try
            {
                if (!File.Exists(location))
                {
                    error = "assembly_file_missing";
                    return false;
                }
                bytes = File.ReadAllBytes(location);
            }
            catch (Exception e)
            {
                error = "assembly_read_exception:" + e.GetType().Name;
                return false;
            }
            return TryComputeSha256(bytes, out hex, out error);
        }

        #endregion

        #region 内容签名（§20.2）

        /// <summary>
        /// 计算单个数据文件的 contentSignature：移除根 contentSignature 属性后的规范 JSON 摘要。
        /// </summary>
        public static bool TryComputeContentSignature(string rawJson, out string signature, out string error)
        {
            signature = null;
            ModeHJsonValue root;
            if (!TryParse(rawJson, out root, out error)) return false;
            if (root == null || root.Kind != ModeHJsonKind.Object)
            {
                error = "content_root_not_object";
                return false;
            }
            root.RemoveProperty("contentSignature");
            return TryComputeValueDigest(root, out signature, out error);
        }

        /// <summary>
        /// 解析并核对数据文件的 contentSignature；通过后返回 token 树供 registry 读取。
        /// </summary>
        public static bool TryParseAndVerifyContent(
            string rawJson,
            out ModeHJsonValue root,
            out string declaredSignature,
            out string error)
        {
            root = null;
            declaredSignature = null;
            ModeHJsonValue parsed;
            if (!TryParse(rawJson, out parsed, out error)) return false;
            if (parsed == null || parsed.Kind != ModeHJsonKind.Object)
            {
                error = "content_root_not_object";
                return false;
            }
            string declared;
            if (!parsed.TryGetString("contentSignature", out declared) || !IsValidDigest(declared))
            {
                error = "content_signature_missing";
                return false;
            }

            ModeHJsonValue reparsed;
            if (!TryParse(rawJson, out reparsed, out error)) return false;
            reparsed.RemoveProperty("contentSignature");
            string computed;
            if (!TryComputeValueDigest(reparsed, out computed, out error)) return false;
            if (!string.Equals(computed, declared, StringComparison.Ordinal))
            {
                error = "content_signature_mismatch";
                return false;
            }

            root = parsed;
            declaredSignature = declared;
            return true;
        }

        /// <summary>
        /// contentCatalogSignature：把全部必需数据文件的规范相对路径与已核对 contentSignature
        /// 组成数组，按相对路径 ordinal 排序后取规范 JSON 摘要。
        /// </summary>
        public static bool TryComputeContentCatalogSignature(
            IList<string> relativePaths,
            IList<string> contentSignatures,
            out string catalogSignature,
            out string error)
        {
            catalogSignature = null;
            error = null;
            if (relativePaths == null || contentSignatures == null || relativePaths.Count != contentSignatures.Count)
            {
                error = "catalog_input_mismatch";
                return false;
            }
            if (relativePaths.Count == 0)
            {
                error = "catalog_empty";
                return false;
            }

            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>(relativePaths.Count);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < relativePaths.Count; i++)
            {
                string path = relativePaths[i];
                string sig = contentSignatures[i];
                if (string.IsNullOrEmpty(path))
                {
                    error = "catalog_path_empty";
                    return false;
                }
                if (!IsValidDigest(sig))
                {
                    error = "catalog_signature_invalid:" + path;
                    return false;
                }
                if (!seen.Add(path))
                {
                    error = "catalog_path_duplicate:" + path;
                    return false;
                }
                entries.Add(new KeyValuePair<string, string>(path, sig));
            }

            entries.Sort(delegate (KeyValuePair<string, string> a, KeyValuePair<string, string> b)
            {
                return string.CompareOrdinal(a.Key, b.Key);
            });

            ModeHJsonValue array = ModeHJsonValue.NewArray();
            for (int i = 0; i < entries.Count; i++)
            {
                ModeHJsonValue item = ModeHJsonValue.NewObject();
                item.AddProperty("contentSignature", ModeHJsonValue.NewString(entries[i].Value));
                item.AddProperty("path", ModeHJsonValue.NewString(entries[i].Key));
                array.Items.Add(item);
            }
            return TryComputeValueDigest(array, out catalogSignature, out error);
        }

        #endregion

        #region 对象摘要（DTO -> 规范 JSON -> SHA-256）

        /// <summary>
        /// 计算 DTO 的规范摘要。excludeRootFieldName 用于排除摘要自身字段
        /// （Season/HallOfFame/journal 传 "payloadDigest"，战场快照传 "snapshotDigest"）。
        /// </summary>
        public static bool TryComputeObjectDigest(
            object dto,
            string excludeRootFieldName,
            out string digest,
            out string error)
        {
            digest = null;
            string canonical;
            if (!TryWriteCanonicalObject(dto, excludeRootFieldName, out canonical, out error)) return false;
            return TryComputeSha256OfText(canonical, out digest, out error);
        }

        /// <summary>把 DTO 转成规范 JSON 文本（调试与 guard 复核用）。</summary>
        public static bool TryWriteCanonicalObject(
            object dto,
            string excludeRootFieldName,
            out string canonicalJson,
            out string error)
        {
            canonicalJson = null;
            error = null;
            if (dto == null)
            {
                error = "canonical_null_dto";
                return false;
            }
            ModeHJsonValue root;
            if (!TryConvertToJsonValue(dto, 0, out root, out error)) return false;
            if (root == null || root.Kind != ModeHJsonKind.Object)
            {
                error = "canonical_root_not_object";
                return false;
            }
            if (!string.IsNullOrEmpty(excludeRootFieldName))
            {
                root.RemoveProperty(excludeRootFieldName);
            }
            return TryWriteCanonical(root, out canonicalJson, out error);
        }

        /// <summary>计算已解析 token 的规范摘要。</summary>
        public static bool TryComputeValueDigest(ModeHJsonValue value, out string digest, out string error)
        {
            digest = null;
            string canonical;
            if (!TryWriteCanonical(value, out canonical, out error)) return false;
            return TryComputeSha256OfText(canonical, out digest, out error);
        }

        #endregion

        #region 规范 JSON 写出

        /// <summary>按 §20.2 规则写出规范 JSON 文本。</summary>
        public static bool TryWriteCanonical(ModeHJsonValue value, out string canonicalJson, out string error)
        {
            canonicalJson = null;
            error = null;
            if (value == null)
            {
                error = "canonical_null_value";
                return false;
            }
            StringBuilder sb = new StringBuilder(1024);
            if (!WriteValue(value, null, sb, 0, out error)) return false;
            canonicalJson = sb.ToString();
            return true;
        }

        private static bool WriteValue(
            ModeHJsonValue value,
            string ownerFieldName,
            StringBuilder sb,
            int depth,
            out string error)
        {
            error = null;
            if (depth > MaxDepth)
            {
                error = "canonical_depth_exceeded";
                return false;
            }
            if (value == null)
            {
                sb.Append("null");
                return true;
            }

            switch (value.Kind)
            {
                case ModeHJsonKind.Null:
                    sb.Append("null");
                    return true;

                case ModeHJsonKind.Bool:
                    sb.Append(value.BoolValue ? "true" : "false");
                    return true;

                case ModeHJsonKind.Integer:
                    sb.Append(value.IntegerValue.ToString(CultureInfo.InvariantCulture));
                    return true;

                case ModeHJsonKind.Float:
                    return WriteFloat(value.FloatValue, sb, out error);

                case ModeHJsonKind.String:
                    WriteString(value.StringValue, sb);
                    return true;

                case ModeHJsonKind.Array:
                    return WriteArray(value, ownerFieldName, sb, depth, out error);

                case ModeHJsonKind.Object:
                    return WriteObject(value, sb, depth, out error);

                default:
                    error = "canonical_unknown_kind";
                    return false;
            }
        }

        private static bool WriteFloat(double d, StringBuilder sb, out string error)
        {
            error = null;
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                error = "canonical_non_finite_number";
                return false;
            }
            if (d == 0d) d = 0d; // 把 -0 规范为 0
            string text = d.ToString("R", CultureInfo.InvariantCulture);
            if (string.Equals(text, "-0", StringComparison.Ordinal)) text = "0";
            sb.Append(text);
            return true;
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private static bool WriteArray(
            ModeHJsonValue value,
            string ownerFieldName,
            StringBuilder sb,
            int depth,
            out string error)
        {
            error = null;
            List<ModeHJsonValue> items = value.Items != null ? value.Items : new List<ModeHJsonValue>();

            if (!string.IsNullOrEmpty(ownerFieldName) && SetSemanticFields.Contains(ownerFieldName))
            {
                List<string> texts = new List<string>(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    ModeHJsonValue item = items[i];
                    if (item == null || item.Kind != ModeHJsonKind.String)
                    {
                        error = "canonical_set_field_not_string:" + ownerFieldName;
                        return false;
                    }
                    texts.Add(item.StringValue != null ? item.StringValue : string.Empty);
                }
                texts.Sort(StringComparer.Ordinal);
                sb.Append('[');
                string previous = null;
                bool first = true;
                for (int i = 0; i < texts.Count; i++)
                {
                    if (previous != null && string.Equals(previous, texts[i], StringComparison.Ordinal)) continue;
                    if (!first) sb.Append(',');
                    WriteString(texts[i], sb);
                    previous = texts[i];
                    first = false;
                }
                sb.Append(']');
                return true;
            }

            string sortKey;
            if (!string.IsNullOrEmpty(ownerFieldName)
                && SortedObjectArrayFields.TryGetValue(ownerFieldName, out sortKey))
            {
                List<ModeHJsonValue> sorted = new List<ModeHJsonValue>(items);
                string sortError = null;
                sorted.Sort(delegate (ModeHJsonValue a, ModeHJsonValue b)
                {
                    return CompareByKey(a, b, sortKey, ref sortError);
                });
                if (sortError != null)
                {
                    error = sortError;
                    return false;
                }
                items = sorted;
            }

            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (!WriteValue(items[i], null, sb, depth + 1, out error)) return false;
            }
            sb.Append(']');
            return true;
        }

        private static int CompareByKey(ModeHJsonValue a, ModeHJsonValue b, string sortKey, ref string error)
        {
            if (a == null || b == null || a.Kind != ModeHJsonKind.Object || b.Kind != ModeHJsonKind.Object)
            {
                if (error == null) error = "canonical_sorted_array_not_object:" + sortKey;
                return 0;
            }
            ModeHJsonValue ka = a.GetProperty(sortKey);
            ModeHJsonValue kb = b.GetProperty(sortKey);
            if (ka == null || kb == null)
            {
                if (error == null) error = "canonical_sort_key_missing:" + sortKey;
                return 0;
            }
            if (ka.Kind == ModeHJsonKind.Integer && kb.Kind == ModeHJsonKind.Integer)
            {
                return ka.IntegerValue.CompareTo(kb.IntegerValue);
            }
            if (ka.Kind == ModeHJsonKind.String && kb.Kind == ModeHJsonKind.String)
            {
                return string.CompareOrdinal(
                    ka.StringValue != null ? ka.StringValue : string.Empty,
                    kb.StringValue != null ? kb.StringValue : string.Empty);
            }
            if (error == null) error = "canonical_sort_key_type:" + sortKey;
            return 0;
        }

        private static bool WriteObject(ModeHJsonValue value, StringBuilder sb, int depth, out string error)
        {
            error = null;
            List<ModeHJsonProperty> properties = value.Properties != null
                ? new List<ModeHJsonProperty>(value.Properties)
                : new List<ModeHJsonProperty>();

            properties.Sort(delegate (ModeHJsonProperty a, ModeHJsonProperty b)
            {
                string na = a != null && a.Name != null ? a.Name : string.Empty;
                string nb = b != null && b.Name != null ? b.Name : string.Empty;
                return string.CompareOrdinal(na, nb);
            });

            sb.Append('{');
            for (int i = 0; i < properties.Count; i++)
            {
                ModeHJsonProperty p = properties[i];
                if (p == null || string.IsNullOrEmpty(p.Name))
                {
                    error = "canonical_property_name_empty";
                    return false;
                }
                if (i > 0)
                {
                    if (string.Equals(properties[i - 1].Name, p.Name, StringComparison.Ordinal))
                    {
                        error = "canonical_duplicate_property:" + p.Name;
                        return false;
                    }
                    sb.Append(',');
                }
                WriteString(p.Name, sb);
                sb.Append(':');
                if (!WriteValue(p.Value, p.Name, sb, depth + 1, out error)) return false;
            }
            sb.Append('}');
            return true;
        }

        #endregion

        #region DTO -> token（反射，禁 Dictionary）

        private static bool TryConvertToJsonValue(object o, int depth, out ModeHJsonValue value, out string error)
        {
            value = null;
            error = null;
            if (depth > MaxDepth)
            {
                error = "canonical_depth_exceeded";
                return false;
            }
            if (o == null)
            {
                value = ModeHJsonValue.NewNull();
                return true;
            }

            Type type = o.GetType();

            if (type == typeof(string))
            {
                value = ModeHJsonValue.NewString((string)o);
                return true;
            }
            if (type == typeof(bool))
            {
                value = ModeHJsonValue.NewBool((bool)o);
                return true;
            }
            if (type.IsEnum)
            {
                value = ModeHJsonValue.NewInteger(Convert.ToInt64(o, CultureInfo.InvariantCulture));
                return true;
            }
            if (type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint) || type == typeof(long))
            {
                value = ModeHJsonValue.NewInteger(Convert.ToInt64(o, CultureInfo.InvariantCulture));
                return true;
            }
            if (type == typeof(ulong))
            {
                ulong raw = (ulong)o;
                if (raw > long.MaxValue)
                {
                    error = "canonical_ulong_overflow";
                    return false;
                }
                value = ModeHJsonValue.NewInteger((long)raw);
                return true;
            }
            if (type == typeof(float) || type == typeof(double))
            {
                double d = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    error = "canonical_non_finite_number";
                    return false;
                }
                value = ModeHJsonValue.NewFloat(d);
                return true;
            }
            if (type == typeof(decimal))
            {
                error = "canonical_decimal_not_supported";
                return false;
            }
            if (o is IDictionary)
            {
                error = "canonical_dictionary_not_supported";
                return false;
            }
            if (o is IEnumerable)
            {
                ModeHJsonValue array = ModeHJsonValue.NewArray();
                IEnumerator enumerator = ((IEnumerable)o).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    ModeHJsonValue item;
                    if (!TryConvertToJsonValue(enumerator.Current, depth + 1, out item, out error)) return false;
                    array.Items.Add(item);
                }
                value = array;
                return true;
            }
            if (type.IsClass)
            {
                ModeHJsonValue obj = ModeHJsonValue.NewObject();
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                if (fields != null)
                {
                    for (int i = 0; i < fields.Length; i++)
                    {
                        FieldInfo field = fields[i];
                        if (field == null || field.IsStatic || field.IsNotSerialized) continue;
                        object fieldValue;
                        try
                        {
                            fieldValue = field.GetValue(o);
                        }
                        catch (Exception e)
                        {
                            error = "canonical_field_read_exception:" + field.Name + ":" + e.GetType().Name;
                            return false;
                        }
                        ModeHJsonValue converted;
                        if (!TryConvertToJsonValue(fieldValue, depth + 1, out converted, out error)) return false;
                        obj.AddProperty(field.Name, converted);
                    }
                }
                value = obj;
                return true;
            }

            error = "canonical_unsupported_type:" + type.Name;
            return false;
        }

        #endregion

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

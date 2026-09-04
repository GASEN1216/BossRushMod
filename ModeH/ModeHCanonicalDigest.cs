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
            // 字段名必须与 ModeHStateDtos 里的**序列化字段**逐字一致：归一化按字段名匹配。
            // 这两个是**不同 DTO 上的两份**入场名单，都需要归一化，漏一个就等于
            // 那一份集合的摘要依赖写入顺序（两者都来自 HashSet 枚举）：
            //   enteredProfileIds -> ModeHMatchRosterDto（本场名单，随赛季落盘）
            //   entrantIds        -> ModeHMatchReportDto（实际登场名单，随战报落盘）
            // 曾经只登记了前者，后者一直没参与排序去重。
            "enteredProfileIds",
            "entrantIds",
            "starterKitIds",
            "relayKitIds",
            "passedStableKeys",
            "commonVerifiedCommandIds",
            "failureReasonIds"
        };

        /// <summary>
        /// 需要按指定键稳定排序的对象数组（§20.2 冻结清单）。
        /// </summary>
        /// <summary>
        /// §20.2 冻结的「按指定键稳定排序」的对象数组。值是**候选键列表**，按序取第一个
        /// 「所有元素都具备」的键。
        ///
        /// 之所以要候选列表：有两个不同 DTO 的字段都叫 `records`——认证报告
        /// （`ModeHProductionCertificationDto`）的元素带 `stableKey`，而名人堂信封
        /// （`ModeHHallOfFameEnvelopeDto`）的元素只有 `hallOfFameId`。旧写法一律按
        /// `stableKey` 排序，单条记录时比较器不被调用所以看不出来，**第 2 条名人堂记录
        /// 起必然 `canonical_sort_key_missing`**，摘要算不出来 → 名人堂写入失败 →
        /// 赛季被强制 Suspended。
        /// </summary>
        private static readonly Dictionary<string, string[]> SortedObjectArrayFields =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "profiles", new[] { "profileId" } },
                { "seasonRewardOperations", new[] { "operationId" } },
                { "matchReports", new[] { "matchIndex" } },
                { "behaviorStatuses", new[] { "entryId" } },
                { "effectStatuses", new[] { "entryId" } },
                { "records", new[] { "stableKey", "hallOfFameId" } },
                { "commandStatuses", new[] { "commandId" } }
            };

        private static readonly object _signatureLock = new object();

        /// <summary>复用的 SHA-256 provider（见 TryComputeSha256 的理由）。</summary>
        private static SHA256 _sharedSha256;

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
                if (_sharedSha256 != null)
                {
                    try { _sharedSha256.Dispose(); }
                    catch (Exception)
                    {
                        // provider 释放失败不影响卸载流程：置空后下次会重建
                    }
                    _sharedSha256 = null;
                }
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
                // provider 复用而不是每次 Create()：押品锁盘一次要对整个仓库逐件算语义摘要，
                // 再叠上整仓摘要，单次操作的调用量是「仓库件数 × 若干轮」。每次新建
                // CSP 实例既有分配也有句柄开销。SHA256 实例不是线程安全的，但本类的
                // 全部调用点都在主线程（存档 / 押品 / 认证），且 _signatureLock 已是同一约定。
                lock (_signatureLock)
                {
                    SHA256 sha = _sharedSha256;
                    if (sha == null)
                    {
                        sha = SHA256.Create();
                        _sharedSha256 = sha;
                    }
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
            if (!ModeHJsonParser.TryParse(rawJson, out root, out error)) return false;
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
            if (!ModeHJsonParser.TryParse(rawJson, out parsed, out error)) return false;
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
            if (!ModeHJsonParser.TryParse(rawJson, out reparsed, out error)) return false;
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

            string[] sortKeyCandidates;
            if (!string.IsNullOrEmpty(ownerFieldName)
                && SortedObjectArrayFields.TryGetValue(ownerFieldName, out sortKeyCandidates))
            {
                // 按序取第一个「所有元素都具备」的候选键。一个都对不上时不静默放行，
                // 报缺失并让上层 fail-closed——排序键漂移会让同一份数据算出不同摘要。
                string sortKey = ResolveSortKey(items, sortKeyCandidates);
                if (sortKey == null)
                {
                    error = "canonical_sort_key_missing:" + string.Join("|", sortKeyCandidates);
                    return false;
                }

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

        /// <summary>
        /// 取第一个「所有元素都具备」的候选键；元素为空数组时取第一个候选（排序无实际作用）。
        /// 一个都不满足返回 null，由调用方 fail-closed。
        /// </summary>
        private static string ResolveSortKey(List<ModeHJsonValue> items, string[] candidates)
        {
            if (candidates == null || candidates.Length == 0) return null;
            if (items == null || items.Count == 0) return candidates[0];

            for (int c = 0; c < candidates.Length; c++)
            {
                string candidate = candidates[c];
                bool allHave = true;
                for (int i = 0; i < items.Count; i++)
                {
                    ModeHJsonValue item = items[i];
                    if (item == null || item.Kind != ModeHJsonKind.Object
                        || item.GetProperty(candidate) == null)
                    {
                        allHave = false;
                        break;
                    }
                }
                if (allHave) return candidate;
            }
            return null;
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

        /// <summary>解析 JSON 文本为 token 树（转发到 ModeHJsonParser，保持单一实现）。</summary>
        public static bool TryParse(string json, out ModeHJsonValue root, out string error)
        {
            return ModeHJsonParser.TryParse(json, out root, out error);
        }

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
    }
}

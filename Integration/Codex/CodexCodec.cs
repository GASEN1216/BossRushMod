// ============================================================================
// CodexCodec.cs - 鸭皇图鉴存档编解码
// ============================================================================
// 硬约束（形态照 Integration/DailyReport/DailyReportCodec.cs）：
//   - 写侧走 Utilities/SimpleJsonHelper.cs 的 Append* 系列：字节格式是发布后冻结的存档面，
//     不因读侧升级而改；读侧走全 Mod 共享的节点解析器 Common/Data/BossRushJsonValue.cs
//     （2026-09-06 起，此前用 SimpleJsonHelper 的前缀提取器）。
//     节点解析器让 envelope 不再受「只能有一个数组」「entries 必须是最后一个字段」
//     「key 互不为带引号前缀」这些提取器约束；新增字段只需保持向后兼容默认值。
//   - CreateDefault() 是**唯一**默认值出处，避免「构造出来的默认」与
//     「读档读出来的默认」两套语义漂移。
//   - 全程 no-throw：解码失败返回 null，由 CodexPersistence 走写屏障 fail-closed。
//
// 存档字段表（schemaVersion = 1，发布后冻结）：
//   顶层 schemaVersion(int) / lastUpdatedTicks(long) / entries(array)
//   条目   k(string 必填) / n(string) / kills(int) / first(long) / fm(string) / fast(float)
//          fs(string, SCHEMA+ 2026-09-20 追加：初见场景 id；老档缺它，读出空串)
//   schemaVersion 保持 1：新字段可选、旧档读出有合理默认值，绝不为一个展示字段
//   把已有存档推进写屏障（AGENTS 4.16 SCHEMA+）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>图鉴 DTO 的 JSON 编解码。纯函数，无状态，全程 no-throw。</summary>
    internal static class CodexCodec
    {
        #region 默认值

        /// <summary>
        /// 新档默认值。**所有默认值只在这里给一处**。
        /// 图鉴的新档就是空图鉴：零条目、零时间戳。
        /// </summary>
        internal static CodexData CreateDefault()
        {
            CodexData data = new CodexData();
            data.LastUpdatedTicks = 0L;
            data.RebuildIndex();
            return data;
        }

        #endregion

        #region 编码

        /// <summary>把 DTO 编成 JSON。失败返回 null。</summary>
        internal static string Encode(CodexData data)
        {
            if (data == null) return null;

            try
            {
                StringBuilder sb = SimpleJsonHelper.GetBuilder();
                sb.Append('{');
                SimpleJsonHelper.AppendInt(sb, "schemaVersion", CodexTuning.CurrentSchemaVersion);
                SimpleJsonHelper.AppendLong(sb, "lastUpdatedTicks", data.LastUpdatedTicks);

                // 字段顺序保持不变：写出字节是冻结的存档面（回读核对按整串比对）。
                sb.Append("\"entries\":[");
                bool first = true;
                int written = 0;
                for (int i = 0; i < data.Entries.Count; i++)
                {
                    CodexEntry e = data.Entries[i];
                    if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                    if (written >= CodexTuning.MaxEntries) break;

                    if (!first) sb.Append(',');
                    first = false;
                    written++;

                    sb.Append('{');
                    SimpleJsonHelper.AppendString(sb, "k", e.Key);
                    SimpleJsonHelper.AppendString(sb, "n", e.DisplayName ?? string.Empty);
                    SimpleJsonHelper.AppendInt(sb, "kills", e.Kills);
                    SimpleJsonHelper.AppendLong(sb, "first", e.FirstKillTicks);
                    SimpleJsonHelper.AppendString(sb, "fm", e.FirstMode ?? string.Empty);
                    SimpleJsonHelper.AppendString(sb, "fs", e.FirstScene ?? string.Empty);
                    SimpleJsonHelper.AppendFloat(sb, "fast", e.FastestKillSeconds, false);
                    sb.Append('}');
                }
                sb.Append("]}");
                return sb.ToString();
            }
            catch (Exception)
            {
                // no-throw：编码失败由调用方（Store）判 null 并进故障态
                return null;
            }
        }

        #endregion

        #region 解码

        /// <summary>读取 payload 的 schemaVersion。缺字段或读不动返回 -1（走写屏障，绝不覆盖）。</summary>
        internal static int ReadSchemaVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            try
            {
                BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                int version;
                return root != null && root.TryGetInt("schemaVersion", out version) ? version : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// 解码 payload。失败返回 null（由持久化层 fail-closed 走写屏障）。
        /// 调用方应先用 ReadSchemaVersion 校验版本。
        /// </summary>
        internal static CodexData Decode(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                if (root == null || root.Kind != BossRushJsonKind.Object) return null;

                CodexData data = new CodexData();
                data.LastUpdatedTicks = root.GetLong("lastUpdatedTicks", 0L);

                List<BossRushJsonValue> entries;
                // 存在但损坏的收藏不能被解释为新档，或截断后覆盖原收藏。
                if (!root.TryGetArray("entries", out entries) || entries.Count > CodexTuning.MaxEntries)
                    return null;
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < entries.Count; i++)
                {
                    BossRushJsonValue node = entries[i];
                    if (node == null || node.Kind != BossRushJsonKind.Object) return null;
                    string key = node.GetString("k", null);
                    if (string.IsNullOrEmpty(key) || !keys.Add(key)) return null;

                    CodexEntry entry = new CodexEntry();
                    entry.Key = key;
                    // 旧档缺可选字段仍用默认值；已有字段类型错误则保留原始档并拒写。
                    if ((node.GetProperty("n") != null && !node.TryGetString("n", out entry.DisplayName))
                        || (node.GetProperty("kills") != null && !node.TryGetInt("kills", out entry.Kills))
                        || (node.GetProperty("first") != null && !node.TryGetLong("first", out entry.FirstKillTicks))
                        || (node.GetProperty("fm") != null && !node.TryGetString("fm", out entry.FirstMode))
                        || (node.GetProperty("fs") != null && !node.TryGetString("fs", out entry.FirstScene))
                        || (node.GetProperty("fast") != null && !node.TryGetFloat("fast", out entry.FastestKillSeconds)))
                        return null;
                    if (entry.Kills < 0 || entry.FirstKillTicks < 0L
                        || entry.FirstKillTicks > DateTime.MaxValue.Ticks
                        || entry.FastestKillSeconds < 0f || float.IsNaN(entry.FastestKillSeconds)
                        || float.IsInfinity(entry.FastestKillSeconds)) return null;
                    if (entry.DisplayName == null) entry.DisplayName = string.Empty;
                    if (entry.FirstMode == null) entry.FirstMode = string.Empty;
                    if (entry.FirstScene == null) entry.FirstScene = string.Empty;

                    data.Entries.Add(entry);
                }

                if (data.LastUpdatedTicks < 0L) data.LastUpdatedTicks = 0L;
                data.RebuildIndex();
                return data;
            }
            catch (Exception)
            {
                // 由持久层 fail-closed 走写屏障：只读不覆盖
                return null;
            }
        }

        #endregion
    }
}

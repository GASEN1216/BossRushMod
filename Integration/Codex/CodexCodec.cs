// ============================================================================
// CodexCodec.cs - 鸭皇图鉴存档编解码
// ============================================================================
// 硬约束（形态照 Integration/DailyReport/DailyReportCodec.cs）：
//   - 只用仓库既有的 Utilities/SimpleJsonHelper.cs，不再造第四套 JSON 解析器
//     （ModeH 有 ModeHJsonValue、遗种巢有 PetNestJson，互相 import 会让彼此成为
//     对方的升级阻塞项）。
//   - envelope 是**扁平对象 + 一层 entries 数组**：SimpleJsonHelper.FindArrayBounds
//     取的是全局第一个 '[' 与最后一个 ']'，因此 envelope 里**只能有一个数组**，
//     且 `entries` 必须是**最后一个字段**——在它之后再加任何字段都会破坏边界。
//   - 提取器按 `"key":` 前缀匹配，所有 key 必须两两不构成「带引号前缀」关系。
//     现有集合 schemaVersion / lastUpdatedTicks / k / n / kills / first / fm / fast
//     已逐对核对；将来加字段必须重新核对。
//   - CreateDefault() 是**唯一**默认值出处，避免「构造出来的默认」与
//     「读档读出来的默认」两套语义漂移。
//   - 全程 no-throw：解码失败返回 null，由 CodexPersistence 走写屏障 fail-closed。
//
// 存档字段表（schemaVersion = 1，发布后冻结）：
//   顶层 schemaVersion(int) / lastUpdatedTicks(long) / entries(array)
//   条目   k(string 必填) / n(string) / kills(int) / first(long) / fm(string) / fast(float)
// ============================================================================

using System;
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

                // entries 必须是**最后一个字段**：FindArrayBounds 取的是第一个 '[' 与
                // 最后一个 ']'，把数组放最后就不会被显示名里的方括号带偏。
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

        /// <summary>读取 payload 的 schemaVersion。缺字段返回 -1（走写屏障，绝不覆盖）。</summary>
        internal static int ReadSchemaVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            try
            {
                if (json.IndexOf("\"schemaVersion\":", StringComparison.Ordinal) < 0) return -1;
                return SimpleJsonHelper.ExtractInt(json, "schemaVersion");
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
                CodexData data = new CodexData();
                data.LastUpdatedTicks = SimpleJsonHelper.ExtractLong(json, "lastUpdatedTicks");

                int arrayStart, arrayEnd;
                if (SimpleJsonHelper.FindArrayBounds(json, out arrayStart, out arrayEnd))
                {
                    SimpleJsonHelper.ForEachObject(json, arrayStart, arrayEnd, delegate(string j, int s, int e)
                    {
                        string key = SimpleJsonHelper.ExtractString(j, "k", s, e);
                        if (string.IsNullOrEmpty(key)) return;
                        // 上限截断：超出的条目丢弃，已读进来的照常保留
                        if (data.Entries.Count >= CodexTuning.MaxEntries) return;

                        CodexEntry entry = new CodexEntry();
                        entry.Key = key;
                        entry.DisplayName = SimpleJsonHelper.ExtractString(j, "n", s, e);
                        entry.Kills = SimpleJsonHelper.ExtractInt(j, "kills", s, e);
                        entry.FirstKillTicks = SimpleJsonHelper.ExtractLong(j, "first", s, e);
                        entry.FirstMode = SimpleJsonHelper.ExtractString(j, "fm", s, e);
                        entry.FastestKillSeconds = SimpleJsonHelper.ExtractFloat(j, "fast", s, e);

                        // 取值收敛：老档缺字段时提取器返回 0，负数一律钳回合法域
                        if (entry.Kills < 0) entry.Kills = 0;
                        if (entry.FirstKillTicks < 0L) entry.FirstKillTicks = 0L;
                        if (entry.FastestKillSeconds < 0f) entry.FastestKillSeconds = 0f;
                        if (entry.DisplayName == null) entry.DisplayName = string.Empty;
                        if (entry.FirstMode == null) entry.FirstMode = string.Empty;

                        data.Entries.Add(entry);
                    });
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

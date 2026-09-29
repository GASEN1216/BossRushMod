// ============================================================================
// ModeHGroupHallOfFame.cs - 鸭王杯群战赛季的名人堂编码与排名（2026-09-29 owner 改版）
// ============================================================================
// 只依赖名人堂 DTO：持久化层（满员挤最末名）与名人堂页（排名）共用。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 群战赛季的名人堂记录编码与排名。复用原 DTO 字段、不加字段：
    /// archetypeId = "group" 作标记，quirkId = "group:胜场:场数:净赚"，signatureCommandId = 头号功臣的 Boss key。
    /// </summary>
    internal static class ModeHGroupHallOfFame
    {
        internal const string ArchetypeTag = "group";
        private const string ScorePrefix = "group:";

        internal static string EncodeScore(int wins, int matches, long net)
        {
            return ScorePrefix + wins + ":" + matches + ":" + net.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static bool IsGroupRecord(ModeHHallOfFameRecordDto record)
        {
            return record != null && string.Equals(record.archetypeId, ArchetypeTag, StringComparison.Ordinal)
                && record.quirkId != null && record.quirkId.StartsWith(ScorePrefix, StringComparison.Ordinal);
        }

        internal static bool TryDecodeScore(ModeHHallOfFameRecordDto record, out int wins, out int matches, out long net)
        {
            wins = 0;
            matches = 0;
            net = 0L;
            if (!IsGroupRecord(record)) return false;
            string[] parts = record.quirkId.Substring(ScorePrefix.Length).Split(':');
            return parts.Length == 3
                && int.TryParse(parts[0], out wins)
                && int.TryParse(parts[1], out matches)
                && long.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out net);
        }

        /// <summary>排名比较：胜场多的在前，同胜场净赚多的在前，再同按先入堂的在前。</summary>
        internal static int CompareRank(ModeHHallOfFameRecordDto a, ModeHHallOfFameRecordDto b)
        {
            int aw, am, bw, bm;
            long an, bn;
            TryDecodeScore(a, out aw, out am, out an);
            TryDecodeScore(b, out bw, out bm, out bn);
            if (aw != bw) return bw.CompareTo(aw);
            if (an != bn) return bn.CompareTo(an);
            string ac = a != null && a.createdUtc != null ? a.createdUtc : string.Empty;
            string bc = b != null && b.createdUtc != null ? b.createdUtc : string.Empty;
            return string.CompareOrdinal(ac, bc);
        }

        /// <summary>只取群战记录并排好名次。</summary>
        internal static List<ModeHHallOfFameRecordDto> Rank(List<ModeHHallOfFameRecordDto> records)
        {
            List<ModeHHallOfFameRecordDto> ranked = new List<ModeHHallOfFameRecordDto>();
            for (int i = 0; records != null && i < records.Count; i++)
                if (IsGroupRecord(records[i])) ranked.Add(records[i]);
            ranked.Sort(CompareRank);
            return ranked;
        }

        /// <summary>
        /// 名人堂满员时挤掉谁：有群战记录就挤排名最末的那条（排行榜只留前几名），否则沿用旧口径挤最早的那条（下标 0）。
        /// </summary>
        internal static int FindEvictionIndex(List<ModeHHallOfFameRecordDto> records)
        {
            int worst = -1;
            for (int i = 0; records != null && i < records.Count; i++)
            {
                if (!IsGroupRecord(records[i])) continue;
                if (worst < 0 || CompareRank(records[i], records[worst]) > 0) worst = i;
            }
            return worst >= 0 ? worst : 0;
        }
    }
}

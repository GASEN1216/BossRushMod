using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>复用共享 JSON token / writer；坏字段拒绝整份载入，由共享 store 保留原 key。</summary>
    internal static class SkyIslandStoryCodec
    {
        internal static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int value;
            return root != null && root.TryGetInt("schemaVersion", out value) ? value : -1;
        }

        internal static SkyIslandStoryData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int schema, flags, visited;
            List<string> cleared, notes;
            if (root == null || !root.TryGetInt("schemaVersion", out schema) || schema != SkyIslandStoryRules.SchemaVersion ||
                !root.TryGetInt("flags", out flags) || flags < 0 || (flags & ~SkyIslandStoryRules.KnownFlags) != 0 ||
                !root.TryGetInt("visitedRegions", out visited) || visited < 0 || (visited & ~4095) != 0 ||
                !root.TryGetStringList("clearedEncounters", out cleared) ||
                !root.TryGetStringList("discoveredNotes", out notes) || !ValidIds(cleared) || !ValidIds(notes)) return null;
            var data = new SkyIslandStoryData { schemaVersion = schema, flags = flags, visitedRegions = visited,
                clearedEncounters = cleared.ToArray(), discoveredNotes = notes.ToArray() };
            // 不猜测修复互斥结局或跳过前置的损坏存档，避免随后保存把原始证据覆盖。
            if (data.Has(SkyIslandStoryFlag.ZhelingReconciled | SkyIslandStoryFlag.ZhelingDefeated) ||
                data.Has(SkyIslandStoryFlag.BellKeeperReconciled | SkyIslandStoryFlag.BellKeeperDefeated) ||
                (data.Has(SkyIslandStoryFlag.Ending) && (!data.BothBeacons || !data.BellKeeperResolved)) ||
                // 噬风只在双航标点亮后才会到场，因此「已击败噬风但航标没亮」必然是坏数据。
                (data.Has(SkyIslandStoryFlag.StormSlain) && !data.BothBeacons)) return null;
            return data;
        }

        private static bool ValidIds(List<string> values)
        {
            if (values == null || values.Count > 256) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
                if (string.IsNullOrEmpty(value) || value.Length > 80 || !unique.Add(value)) return false;
            return true;
        }

        internal static string Encode(SkyIslandStoryData value)
        {
            if (value == null) return null;
            var writer = new BossRushJsonWriter();
            writer.BeginObject().Int("schemaVersion", SkyIslandStoryRules.SchemaVersion).Int("flags", value.flags)
                .Int("visitedRegions", value.visitedRegions).BeginArray("clearedEncounters");
            foreach (string id in value.clearedEncounters) writer.ItemStr(id);
            writer.EndArray().BeginArray("discoveredNotes");
            foreach (string id in value.discoveredNotes) writer.ItemStr(id);
            return writer.EndArray().EndObject().ToString();
        }
    }
}

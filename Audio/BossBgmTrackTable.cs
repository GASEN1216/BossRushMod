using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>Boss 战 BGM 条目；字段名保持 BgmTracks.json 契约。</summary>
    [Serializable]
    internal class BossBgmTrackEntry
    {
        public string bossKey;
        public string file;
        public bool loop = true;
        // V1 只播放 phase=0，其余阶段由协调器跳过。
        public int phase;
    }

    [Serializable]
    internal class BossBgmStingerEntry
    {
        public string eventKey;
        public string file;
    }

    [Serializable]
    internal class BossBgmJukeboxEntry
    {
        public string musicName;
        public string author;
        public string file;
    }

    /// <summary>
    /// 复用既有 token parser 显式读取数组，避免 Unity JsonUtility 在 Mod DTO 上静默产出空表。
    /// 缺省数组仍表示没有曲目；坏 JSON/错误数组类型返回原因，不伪装成装载成功。
    /// </summary>
    internal class BossBgmTrackTable
    {
        public int version;
        public BossBgmTrackEntry[] bossTracks;
        public BossBgmStingerEntry[] stingers;
        public BossBgmJukeboxEntry[] jukebox;

        internal static bool TryParse(string json, out BossBgmTrackTable table, out string error)
        {
            table = null;
            BossRushJsonValue root;
            if (!BossRushJsonParser.TryParse(json, out root, out error)) return false;
            if (root == null || root.Kind != BossRushJsonKind.Object)
            {
                error = "root_not_object";
                return false;
            }

            List<BossRushJsonValue> bossRows;
            List<BossRushJsonValue> stingerRows;
            List<BossRushJsonValue> jukeboxRows;
            if (!TryReadRows(root, "bossTracks", out bossRows, out error)
                || !TryReadRows(root, "stingers", out stingerRows, out error)
                || !TryReadRows(root, "jukebox", out jukeboxRows, out error)) return false;

            List<BossBgmTrackEntry> bosses = new List<BossBgmTrackEntry>();
            foreach (BossRushJsonValue row in bossRows)
            {
                if (row == null || row.Kind != BossRushJsonKind.Object) continue;
                BossBgmTrackEntry entry = new BossBgmTrackEntry();
                if (!row.TryGetString("bossKey", out entry.bossKey)
                    || !row.TryGetString("file", out entry.file)) continue;
                if (row.GetProperty("loop") != null && !row.TryGetBool("loop", out entry.loop)) continue;
                if (row.GetProperty("phase") != null && !row.TryGetInt("phase", out entry.phase)) continue;
                bosses.Add(entry);
            }

            List<BossBgmStingerEntry> events = new List<BossBgmStingerEntry>();
            foreach (BossRushJsonValue row in stingerRows)
            {
                if (row == null || row.Kind != BossRushJsonKind.Object) continue;
                BossBgmStingerEntry entry = new BossBgmStingerEntry();
                if (!row.TryGetString("eventKey", out entry.eventKey)
                    || !row.TryGetString("file", out entry.file)) continue;
                events.Add(entry);
            }

            List<BossBgmJukeboxEntry> music = new List<BossBgmJukeboxEntry>();
            foreach (BossRushJsonValue row in jukeboxRows)
            {
                if (row == null || row.Kind != BossRushJsonKind.Object) continue;
                BossBgmJukeboxEntry entry = new BossBgmJukeboxEntry();
                if (!row.TryGetString("musicName", out entry.musicName)
                    || !row.TryGetString("file", out entry.file)) continue;
                row.TryGetString("author", out entry.author);
                music.Add(entry);
            }

            table = new BossBgmTrackTable();
            root.TryGetInt("version", out table.version);
            table.bossTracks = bosses.ToArray();
            table.stingers = events.ToArray();
            table.jukebox = music.ToArray();
            error = null;
            return true;
        }

        private static bool TryReadRows(BossRushJsonValue root, string key,
            out List<BossRushJsonValue> rows, out string error)
        {
            error = null;
            BossRushJsonValue value = root.GetProperty(key);
            if (value == null || value.Kind == BossRushJsonKind.Null)
            {
                rows = new List<BossRushJsonValue>();
                return true;
            }
            if (root.TryGetArray(key, out rows)) return true;
            error = key + "_not_array";
            return false;
        }
    }
}

using System;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// COMPAT：群岛手记——把存档里已经记着的见闻、信鸽来信、船员名册与旅程进度排成一本可翻的手记。
    ///
    /// 以前 `discoveredNotes` 只记录不展示：20 处见闻收进去就再也看不到，也不知道还差哪几处。
    /// 手记分四个见闻章节（码头与风铃集 / 西线 / 东线 / 栈道与钟庭）+ 信鸽来信 + 船员名册：
    /// 收过的写标题与正文，没收过的只写标题并标「尚未收录」，当作找齐的线索；20 处见闻收齐后总览末尾出现终页。
    /// 只读存档，不写任何东西。标题与正文由调用方传入（见闻文案唯一来源仍是 `SkyIslandWorldStory.PointName / Lore`）。
    /// 纯逻辑、无 Unity 依赖，隔离回归直接执行。
    /// </summary>
    internal static class SkyIslandJournal
    {
        /// <summary>四个见闻章节，每章按走过去的顺序排；合起来恰好是 20 处见闻。</summary>
        internal static readonly string[][] Chapters =
        {
            new[] { "Search_A", "Search_A_02", "Search_B", "Search_B_02" },
            new[] { "Search_C", "Search_C_02", "Search_S1", "Search_D", "Search_D_02", "Search_S2" },
            new[] { "Search_F", "Search_F_02", "Search_S3", "Search_G", "Search_G_02", "Search_S4" },
            new[] { "Search_E", "Search_E_02", "Search_H", "Search_H_02" }
        };

        internal const int NoteCount = 20;

        internal static string ChapterName(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("见闻 · 码头与风铃集", "Notes · the dock and the market");
                case 1: return L10n.T("见闻 · 西线（梯田 / 悬根林 / 两座秘境）", "Notes · the west (terraces, root wood, two hidden isles)");
                case 2: return L10n.T("见闻 · 东线（镜水寺 / 工坊 / 两座秘境）", "Notes · the east (temple, workshop, two hidden isles)");
                case 3: return L10n.T("见闻 · 栈道与钟庭", "Notes · the boardwalk and the Bell Court");
                default: return L10n.T("见闻", "Notes");
            }
        }

        internal static bool Recorded(SkyIslandStoryData data, string id)
        {
            return data != null && data.discoveredNotes != null && Array.IndexOf(data.discoveredNotes, id) >= 0;
        }

        internal static int NotesRecorded(SkyIslandStoryData data)
        {
            int count = 0;
            for (int c = 0; c < Chapters.Length; c++)
                for (int i = 0; i < Chapters[c].Length; i++)
                    if (Recorded(data, Chapters[c][i])) count++;
            return count;
        }

        internal static bool NotesComplete(SkyIslandStoryData data) { return NotesRecorded(data) >= NoteCount; }

        internal static int RegionsVisited(SkyIslandStoryData data)
        {
            if (data == null) return 0;
            int bits = data.visitedRegions & 4095, count = 0;
            while (bits != 0) { bits &= bits - 1; count++; }
            return count;
        }

        /// <summary>总览：各类收录进度 + 旅程摘要（调用方传 `SkyIslandStoryService.Summary`）+ 收齐后的终页。</summary>
        internal static string Overview(SkyIslandStoryData data, string summary)
        {
            var text = new StringBuilder();
            text.Append(L10n.T("群岛手记 · 见闻 ", "Archipelago journal · notes "));
            text.Append(NotesRecorded(data)).Append('/').Append(NoteCount);
            text.Append(L10n.T(" · 信鸽来信 ", " · pigeon letters "));
            text.Append(SkyIslandLetters.CollectedCount(data)).Append('/').Append(SkyIslandLetters.Count);
            text.Append(L10n.T(" · 船员名册 ", " · crew roster "));
            text.Append(SkyIslandCrew.ReadCount(data)).Append('/').Append(SkyIslandCrew.Count);
            text.Append(L10n.T(" · 纪念品 ", " · keepsakes "));
            text.Append(SkyIslandItemRules.GrantedCount(data)).Append('/').Append(SkyIslandItemRules.Keepsakes.Length);
            text.Append(L10n.T(" · 到访区域 ", " · regions visited "));
            text.Append(RegionsVisited(data)).Append("/12");
            if (!string.IsNullOrEmpty(summary)) text.Append("\n\n").Append(summary);
            if (NotesComplete(data)) text.Append("\n\n").Append(Epilogue);
            return text.ToString();
        }

        /// <summary>收齐 20 处见闻后总览末尾的终页。</summary>
        internal static string Epilogue
        {
            get
            {
                return L10n.T("终页 · 晴岚手记\n二十处见闻都收进了这本手记。翻到最后，你发现每一页的边角都画着同一个小记号——一只系在码头的空船。浮舟说那是岛上的老规矩：把走过的路记下来，留给下一位旅人，就不会再有人迷路。",
                    "Last page · The Qinglan journal\nAll twenty notes are in this journal now. On the last page you notice the same small mark in the corner of every page — an empty boat tied at the dock. Fuzhou says it is an old island custom: write down the way you walked and leave it for the next traveller, so that no one gets lost again.");
            }
        }

        /// <summary>第 <paramref name="index"/> 个见闻章节：收过的写标题与正文，没收过的只写标题并标「尚未收录」。</summary>
        internal static string Chapter(int index, SkyIslandStoryData data, Func<string, string> title, Func<string, string> body)
        {
            if (index < 0 || index >= Chapters.Length) return string.Empty;
            var text = new StringBuilder();
            string[] keys = Chapters[index];
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0) text.Append("\n\n");
                string name = title != null ? title(keys[i]) : keys[i];
                if (Recorded(data, keys[i]))
                    text.Append("■ ").Append(name).Append('\n').Append(body != null ? body(keys[i]) : string.Empty);
                else
                    text.Append("□ ").Append(name).Append(L10n.T("（尚未收录）", " (not yet recorded)"));
            }
            return text.ToString();
        }

        /// <summary>信鸽来信：收到的写题目与正文，没收到的只写第几封（不剧透落点与内容）。</summary>
        internal static string Letters(SkyIslandStoryData data)
        {
            var text = new StringBuilder();
            SkyIslandLetter[] all = SkyIslandLetters.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (i > 0) text.Append("\n\n");
                if (SkyIslandLetters.Collected(data, all[i].Id))
                    text.Append("■ ").Append(all[i].Title).Append('\n').Append(all[i].Body);
                else
                    text.Append("□ ").Append(L10n.T("第 ", "Letter ")).Append(i + 1)
                        .Append(L10n.T(" 封（尚未收到）", " (not yet received)"));
            }
            return text.ToString();
        }

        /// <summary>纪念品：发过的写名字；没发过的写名字与怎么得到（纪念品本来就是明着给的目标）。</summary>
        internal static string Keepsakes(SkyIslandStoryData data)
        {
            var text = new StringBuilder();
            SkyIslandKeepsake[] all = SkyIslandItemRules.Keepsakes;
            for (int i = 0; i < all.Length; i++)
            {
                if (i > 0) text.Append('\n');
                string name = SkyIslandItemRules.Name(all[i].TypeId);
                if (SkyIslandItemRules.Granted(data, all[i].NoteId)) text.Append("■ ").Append(name);
                else text.Append("□ ").Append(name).Append(L10n.T("（尚未获得：", " (not yet: ")).Append(all[i].How).Append(L10n.T("）", ")"));
            }
            return text.ToString();
        }

        /// <summary>船员名册：读过的页照当前存档重新生成（选择变了，话也跟着变），没读过的只写名字。</summary>
        internal static string Crew(SkyIslandStoryData data)
        {
            var text = new StringBuilder();
            for (int i = 0; i < SkyIslandCrew.Count; i++)
            {
                if (i > 0) text.Append("\n\n");
                if (SkyIslandCrew.Read(data, i))
                    text.Append("■ ").Append(SkyIslandCrew.Name(i)).Append('\n').Append(SkyIslandCrew.Page(i, data));
                else
                    text.Append("□ ").Append(SkyIslandCrew.Name(i)).Append(L10n.T("（尚未读过）", " (not yet read)"));
            }
            return text.ToString();
        }
    }
}

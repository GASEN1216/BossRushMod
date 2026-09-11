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
            text.Append(L10n.T(" · 岛上的灯 ", " · lights on the isles "));
            text.Append(SkyIslandLights.LitCount(data)).Append('/').Append(SkyIslandLights.Target);
            if (!string.IsNullOrEmpty(summary)) text.Append("\n\n").Append(summary);
            if (NotesComplete(data)) text.Append("\n\n").Append(Epilogue);
            return text.ToString();
        }

        /// <summary>
        /// 「群岛之物」：十五件天空岛物品在岛上各拿来做什么。采集、合成、剧情、风晶灯与夜风怎么串在一起，就看这一页；
        /// 会影响选择的两个数（护符减伤、航徽折扣）取规则常量，不另写一份。
        /// </summary>
        internal static string Uses()
        {
            var text = new StringBuilder(L10n.T("群岛之物 · 用处", "What things on the isles are for"));
            Use(text, BossRushItemIds.SkyIslandGreenearSheaf, L10n.T("归航菜便当、驱风香", "homecoming bentos, windward incense"));
            Use(text, BossRushItemIds.SkyIslandDriftwood, L10n.T("风灯、便当的柴；栈道、镜水寺、邮亭与听雨洞的风晶灯",
                "wind lanterns, bento firewood; the windcrystal lamps on the boardwalk, at the temple, in the post hut and at the grotto"));
            Use(text, BossRushItemIds.SkyIslandCloudmossFiber, L10n.T("风灯、驱风香、星苔药膏；栈道与邮亭的风晶灯",
                "wind lanterns, windward incense, starmoss salve; the boardwalk and post hut lamps"));
            Use(text, BossRushItemIds.SkyIslandBrassScrap, L10n.T("晴岚护符、风标罗盘；工坊、钟庭与听雨洞的风晶灯",
                "Qinglan charms, wind-vane compasses; the workshop, Bell Court and grotto lamps"));
            Use(text, BossRushItemIds.SkyIslandWindcrystalShard, L10n.T("五片熔成晴岚风晶（星灯亮起之后）；护符、药膏、罗盘也要",
                "five fuse into a Qinglan Windcrystal (once the star lamp is lit); charms, salves and compasses need them too"));
            Use(text, BossRushItemIds.SkyIslandStardust, L10n.T("晴岚护符、残星瞭台的风晶灯；夜里风晶簇更容易出",
                "Qinglan charms and the Starfall Overlook lamp; clusters yield more at night"));
            Use(text, BossRushItemIds.SkyIslandQinglanWindcrystal, L10n.T("七盏风晶灯的灯芯：灯旁暖和，岛上的灯凑满十盏之后夜里不再起风",
                "the wick of the seven windcrystal lamps: warm beside them, and with ten lights on the isles the nights stop blowing"));
            Use(text, BossRushItemIds.SkyIslandWindLantern, L10n.T("挡微风、大风里挡一半、夜里照明；钟庭的风晶灯要挂一盏",
                "holds off a breeze and half of a gale, lights the night; the Bell Court lamp hangs one"));
            Use(text, BossRushItemIds.SkyIslandWindwardIncense, L10n.T("什么风都挡得住、耐力恢复加快；镜水寺的风晶灯要焚一炷",
                "holds off any wind and speeds stamina; the Mirrorwater Temple lamp burns one"));
            Use(text, BossRushItemIds.SkyIslandQinglanCharm, string.Format(L10n.T("本趟噬风的风暴伤害 −{0}%，生命上限与耐力恢复小幅提升",
                "{0}% less damage from the Windeater's storm this raid, a little more max health and stamina recovery"),
                Percent(SkyIslandFieldcraftRules.CharmStormWard)));
            Use(text, BossRushItemIds.SkyIslandHomecomingBento, L10n.T("菜畦重新开张之后在岛上吃，算作晴禾的归航菜",
                "once the garden has reopened, eaten on the isles it counts as Qinghe's homecoming meal"));
            Use(text, BossRushItemIds.SkyIslandStarmossSalve, L10n.T("不付钱、不等冷却地回血",
                "heals without paying Miantai or waiting on her remedy"));
            Use(text, BossRushItemIds.SkyIslandWindVaneCompass, L10n.T("指信鸽、目标、支线；都没有了就指还缺灯的地方或风晶簇",
                "points to pigeons, objectives and side paths; after that, to a place missing its lamp or a wind crystal cluster"));
            Use(text, BossRushItemIds.SkyIslandHomecomingBadge, string.Format(L10n.T("带在身上：渡口整备与眠苔的苔药只收 {0}%；在岛上使用：拉缆绳回登云码头（每趟一次）",
                "carried: the dock refit and Miantai's remedy cost {0}%; used on the isles: pull the line back to Cloudrise Dock (once per raid)"),
                Percent(SkyIslandItemRules.BadgeServiceRate)));
            Use(text, BossRushItemIds.SkyIslandWindeaterCore, L10n.T("带在身上：大风对你只算微风",
                "carried: a gale only counts as a breeze for you"));
            return text.ToString();
        }

        private static void Use(StringBuilder text, int typeId, string use)
        {
            text.Append("\n· ").Append(SkyIslandItemRules.Name(typeId)).Append(" → ").Append(use);
        }

        private static int Percent(double rate) { return (int)Math.Round(rate * 100.0); }

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

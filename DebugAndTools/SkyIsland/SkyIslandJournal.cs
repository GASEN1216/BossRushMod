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
    /// 只读存档，不写任何东西。标题与正文由调用方传入（见闻文案唯一来源仍是 `SkyIslandPointText.Name / Lore`）。
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

        /// <summary>
        /// 手记首页的**导语**：一句话，回答「这本手记现在什么样」。
        ///
        /// 【为什么单开这一条】旧版首页正文是 <see cref="Overview"/>——七组分数串成的一行，
        /// 新档实际输出「群岛手记 · 见闻 0/20 · 信鸽来信 0/12 · 船员名册 0/4 · 纪念品 0/3 ·
        /// 到访区域 0/12 · 岛上的灯 3/10 · 蛙鸣池的蛙 0/3」，中文 141 字。
        /// 那是**状态转储不是人话**，而且玩家一打开面板第一眼就撞上它。
        /// 数字没有丢：它们仍在 <see cref="Overview"/> 里，只是退到「这一趟」子页去了。
        /// </summary>
        internal static string Brief(SkyIslandStoryData data)
        {
            int notes = NotesRecorded(data);
            if (notes <= 0)
                return L10n.T("手记还空着。走到哪儿，就记到哪儿。",
                    "The journal is still blank. Whatever you walk past goes in here.");
            if (notes >= NoteCount)
                return L10n.T("二十页见闻都记满了，这本手记可以留给下一位旅人。",
                    "All twenty notes are in. This journal is ready for the next traveller.");
            return L10n.T("手记记了 ", "The journal holds ") + notes
                + L10n.T(" 页，还空着 ", " pages; ") + (NoteCount - notes)
                + L10n.T(" 页。", " are still blank.");
        }

        /// <summary>「来信与人」子页的导语。</summary>
        internal static string PeopleBrief(SkyIslandStoryData data)
        {
            return L10n.T("信、名册和带在身上的纪念品都收在这里。",
                "Letters, the crew roster and the keepsakes you carry are kept here.");
        }

        /// <summary>「岛上的事」子页的导语。</summary>
        internal static string IslesBrief(SkyIslandStoryData data)
        {
            return L10n.T("岛上点了几盏灯、手里的东西各有什么用，都在这一页。",
                "How many lights are up, and what everything you carry is good for.");
        }

        /// <summary>
        /// 总览：各类收录进度 + 旅程摘要（调用方传 `SkyIslandStoryService.Summary`）+ 收齐后的终页。
        /// **不再放在手记首页**——它是一张进度表，退到「这一趟」子页里。首页用 <see cref="Brief"/>。
        /// </summary>
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
            // 内容批次四：放回蛙鸣池的蛙卵（写在本槽手记里的 Frog_1..3）。
            text.Append(SkyIslandMosquitoRules.FrogProgress(data));
            if (!string.IsNullOrEmpty(summary)) text.Append("\n\n").Append(summary);
            if (NotesComplete(data)) text.Append("\n\n").Append(Epilogue);
            return text.ToString();
        }

        /// <summary>
        /// 「群岛之物」：十八件天空岛物品在岛上各拿来做什么。采集、合成、剧情、风晶灯、夜风与云蚋怎么串在一起，就看这一页；
        /// 会影响选择的两个数（护符减伤、航徽折扣）取规则常量，不另写一份。
        /// </summary>
        internal static string Uses()
        {
            var text = new StringBuilder(L10n.T("群岛之物 · 用处", "What things on the isles are for"));
            Use(text, BossRushItemIds.SkyIslandGreenearSheaf, L10n.T("归航菜便当、驱风香、药烟蒲扇", "homecoming bentos, windward incense, the remedy-smoke fan"));
            Use(text, BossRushItemIds.SkyIslandDriftwood, L10n.T("风灯、便当的柴、蒲扇的柄；栈道、镜水寺、邮亭与听雨洞的风晶灯",
                "wind lanterns, bento firewood, the fan's handle; the windcrystal lamps on the boardwalk, at the temple, in the post hut and at the grotto"));
            Use(text, BossRushItemIds.SkyIslandCloudmossFiber, L10n.T("风灯、驱风香、星苔药膏、云苔纱笠，夜里包蛙卵；栈道与邮亭的风晶灯",
                "wind lanterns, windward incense, starmoss salve, the cloudmoss veil, wrapping frogspawn at night; the boardwalk and post hut lamps"));
            Use(text, BossRushItemIds.SkyIslandBrassScrap, L10n.T("晴岚护符、风标罗盘、风晶灭蚊灯的罩；工坊、钟庭与听雨洞的风晶灯",
                "Qinglan charms, wind-vane compasses, the gnat zapper's cage; the workshop, Bell Court and grotto lamps"));
            Use(text, BossRushItemIds.SkyIslandWindcrystalShard, L10n.T("五片熔成晴岚风晶（星灯亮起之后）；护符、药膏、罗盘也要",
                "five fuse into a Qinglan Windcrystal (once the star lamp is lit); charms, salves and compasses need them too"));
            Use(text, BossRushItemIds.SkyIslandStardust, L10n.T("晴岚护符、云苔纱笠、残星瞭台的风晶灯；夜里风晶簇更容易出",
                "Qinglan charms, the cloudmoss veil and the Starfall Overlook lamp; clusters yield more at night"));
            Use(text, BossRushItemIds.SkyIslandQinglanWindcrystal, L10n.T("七盏风晶灯与灭蚊灯的灯芯：灯旁暖和，岛上的灯凑满十盏之后夜里不再起风",
                "the wick of the seven windcrystal lamps and of the gnat zapper: warm beside them, and with ten lights on the isles the nights stop blowing"));
            Use(text, BossRushItemIds.SkyIslandWindLantern, L10n.T("挡微风、大风里挡一半、夜里照明——光招来更多云蚋，可灯下的只绕着灯转：不叮人、也躲不开枪口，灯灭前扇掉或打掉；钟庭的风晶灯要挂一盏",
                "holds off a breeze and half of a gale, lights the night — its light draws more cloud gnats, but the ones in it only circle the flame: they will not bite and cannot dodge your aim, so clear them before it burns out; the Bell Court lamp hangs one"));
            Use(text, BossRushItemIds.SkyIslandWindwardIncense, L10n.T("什么风都挡得住、耐力恢复加快，烟能赶开云蚋；镜水寺的风晶灯要焚一炷",
                "holds off any wind and speeds stamina, and its smoke drives off cloud gnats; the Mirrorwater Temple lamp burns one"));
            Use(text, BossRushItemIds.SkyIslandQinglanCharm, string.Format(L10n.T("本趟噬风的风暴伤害 −{0}%，生命上限与耐力恢复小幅提升",
                "{0}% less damage from the Windeater's storm this raid, a little more max health and stamina recovery"),
                Percent(SkyIslandFieldcraftRules.CharmStormWard)));
            Use(text, BossRushItemIds.SkyIslandHomecomingBento, L10n.T("菜畦重新开张之后在岛上吃，算作晴禾的归航菜",
                "once the garden has reopened, eaten on the isles it counts as Qinghe's homecoming meal"));
            Use(text, BossRushItemIds.SkyIslandStarmossSalve, L10n.T("不付钱、不等冷却地回血；止云蚋的痒，抹上之后这一阵叮上也不痒（出门前先抹也算；苔药管伤，药膏管痒）",
                "heals without paying Miantai or waiting on her remedy; stops gnat itching, and for a while after it goes on new bites will not itch (applying it before you set out counts; the remedy is for wounds, the salve for itching)"));
            Use(text, BossRushItemIds.SkyIslandWindVaneCompass, L10n.T("捧着蛙卵时先指蛙鸣池；平时指信鸽、目标、支线，最后指缺灯处或风晶簇",
                "while carrying frogspawn, points to Frogsong Pool first; otherwise to pigeons, objectives, side paths, then missing lamps or wind crystal clusters"));
            Use(text, BossRushItemIds.SkyIslandHomecomingBadge, string.Format(L10n.T("带在身上：渡口整备与眠苔的苔药只收 {0}%；在岛上使用：拉缆绳回登云码头（每趟一次）",
                "carried: the dock refit and Miantai's remedy cost {0}%; used on the isles: pull the line back to Cloudrise Dock (once per raid)"),
                Percent(SkyIslandItemRules.BadgeServiceRate)));
            Use(text, BossRushItemIds.SkyIslandWindeaterCore, L10n.T("带在身上：大风对你只算微风",
                "carried: a gale only counts as a breeze for you"));
            Use(text, BossRushItemIds.SkyIslandCloudmossVeil, L10n.T("带在身上：云蚋只能在一米外打转，叮咬慢约三倍",
                "carried: cloud gnats can only circle a metre off, and bite about three times less often"));
            Use(text, BossRushItemIds.SkyIslandGnatZapper, L10n.T("放在地上约 5 分钟：把附近的云蚋引过去电落（守一片地方）",
                "set down for about 5 minutes: draws nearby cloud gnats in and zaps them (holds an area)"));
            Use(text, BossRushItemIds.SkyIslandSmokeFan, L10n.T("扇一下：扑落面前贴脸的云蚋、扇退远一点的（近身、瞬时，不消耗）",
                "one sweep: knocks down the gnats on your face and blows back the ones further off (close and instant, not consumed)"));
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
                {
                    text.Append("■ ").Append(all[i].Title).Append('\n').Append(all[i].Body);
                    // 内容批次四：三团蛙卵都放回了蛙鸣池，那封写给池子里青蛙的信有了回音。
                    if (all[i].Id == "Letter_04" && SkyIslandMosquitoRules.FrogsComplete(data))
                        text.Append(L10n.T("\n（三团蛙卵都回了蛙鸣池，长成的蛙夜里散到岛上各处的水边。池子里的青蛙会替那个孩子数灯。）",
                            "\n(All three clutches are back in Frogsong Pool, and the frogs that grew there spread along the isles' waterline at night. The frogs in the pool will count the lights for that child.)"));
                }
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

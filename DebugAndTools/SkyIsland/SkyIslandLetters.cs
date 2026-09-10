using System;

namespace BossRush
{
    /// <summary>一封信鸽来信：稳定 id、落点标记、所在区域、解锁前置与中英文题目、正文。</summary>
    internal sealed class SkyIslandLetter
    {
        internal string Id;
        /// <summary>信鸽落点所挂的作者标记：落点在标记外一个交互间距，方位按 id 取稳定散列。</summary>
        internal string Anchor;
        /// <summary>落点所在区域（A–H / S1–S4），落地提示念的地名。</summary>
        internal string Region;
        /// <summary>需要全部具备的剧情旗标；None 表示随时会来。</summary>
        internal SkyIslandStoryFlag Requires;
        internal string TitleCn, TitleEn, BodyCn, BodyEn;
        internal string Title { get { return L10n.T(TitleCn, TitleEn); } }
        internal string Body { get { return L10n.T(BodyCn, BodyEn); } }
    }

    /// <summary>
    /// COMPAT：信鸽来信——每次出击一只信鸽落在岛上某处，带来一封风灾那年寄不出去、如今才送到的信。
    ///
    /// 口径：
    /// - **一趟一只**：会话就绪时按 <see cref="NextFor"/> 选信，落地时字幕告诉玩家落在哪个区域；不上地图，要自己走过去找。
    /// - **收过的不再来**：读信时「收下」写进本槽手记（`SkyIslandStoryData.discoveredNotes`，id 前缀 <see cref="IdPrefix"/>），
    ///   复用见闻数组，不加存档字段、不加旗标。12 封信 + 20 处见闻 + 4 页船员名册远低于编解码的 256 条上限。
    /// - **应时的信先来**：有前置的信（双航标、星灯、敲钟之后）一旦前置满足，下一趟优先送到；其余按顺序。
    /// 纯逻辑、无 Unity 依赖，隔离回归直接执行。
    /// </summary>
    internal static class SkyIslandLetters
    {
        internal const string IdPrefix = "Letter_";

        private static readonly SkyIslandLetter[] letters = Build();

        internal static SkyIslandLetter[] All { get { return letters; } }
        internal static int Count { get { return letters.Length; } }

        internal static SkyIslandLetter Find(string id)
        {
            for (int i = 0; i < letters.Length; i++)
                if (string.Equals(letters[i].Id, id, StringComparison.Ordinal)) return letters[i];
            return null;
        }

        internal static bool Collected(SkyIslandStoryData data, string id)
        {
            return data != null && data.discoveredNotes != null && Array.IndexOf(data.discoveredNotes, id) >= 0;
        }

        internal static int CollectedCount(SkyIslandStoryData data)
        {
            int count = 0;
            for (int i = 0; i < letters.Length; i++) if (Collected(data, letters[i].Id)) count++;
            return count;
        }

        internal static bool Unlocked(SkyIslandStoryData data, SkyIslandLetter letter)
        {
            return data != null && letter != null && (letter.Requires == SkyIslandStoryFlag.None || data.Has(letter.Requires));
        }

        /// <summary>
        /// 本趟信鸽带哪一封：先挑「有前置且前置已满足」的，再按顺序挑无前置的；都收过了返回 null（这趟没有信鸽）。
        /// 结果只取决于存档，同一个存档每次进岛在收下之前都是同一封、落在同一处——没捡到的信会一直等你。
        /// </summary>
        internal static SkyIslandLetter NextFor(SkyIslandStoryData data)
        {
            if (data == null) return null;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < letters.Length; i++)
                {
                    SkyIslandLetter letter = letters[i];
                    bool gated = letter.Requires != SkyIslandStoryFlag.None;
                    if (gated != (pass == 0)) continue;
                    if (!Unlocked(data, letter) || Collected(data, letter.Id)) continue;
                    return letter;
                }
            }
            return null;
        }

        private static SkyIslandLetter[] Build()
        {
            return new[]
            {
                Letter("Letter_01", "Region_A", "A", SkyIslandStoryFlag.None,
                    "码头的回信", "A reply for the dock",
                    "浮舟：风灾那天我把最后一条缆绳交给了你。要是这封信到了，说明航路又通了——替我把缆绳解下来，挂回码头最高的那根桩上。——阿潮",
                    "Fuzhou — on the day of the storm I handed you the last mooring line. If this letter reaches you, the lanes are open again, so untie that line for me and hang it back on the tallest post at the dock. — Achao"),
                Letter("Letter_02", "Region_B", "B", SkyIslandStoryFlag.None,
                    "寄给修灯的人", "For the one who mends lamps",
                    "阿姐：我在岸上学会了修灯，可岸上的灯不会唱歌。等你把东西两端的风铃都挂回去，我就回来，给你修一盏会响的。——苇生",
                    "Sister — I learned to mend lamps on the shore, but shore lamps do not sing. Once you have the east and west chimes hung again, I will come home and make you one that rings. — Weisheng"),
                Letter("Letter_03", "Region_C", "C", SkyIslandStoryFlag.None,
                    "田埂上的空格", "The empty plot on the ridge",
                    "晴禾：田埂上那格空着的，是给我留的吧？我记得你说过，谁先回来，谁就先吃第一碗。别等太久，菜老了就不好吃了。——一个没署名的人",
                    "Qinghe — the empty plot on the ridge is the one you kept for me, is it not? You said whoever comes home first gets the first bowl. Do not wait too long; greens go tough when they are left. — someone who did not sign"),
                Letter("Letter_04", "Region_S1", "S1", SkyIslandStoryFlag.None,
                    "写给池子里的青蛙", "To the frogs in the pool",
                    "写给池子里的青蛙：你们替我数一数，岸上的灯亮了几盏。数到十盏的时候，我就该回家了。——一个等灯的孩子",
                    "To the frogs in the pool: count the lights on the shore for me. When you reach ten, it will be time for me to come home. — a child waiting for the lights"),
                Letter("Letter_05", "Region_S2", "S2", SkyIslandStoryFlag.None,
                    "邮亭值守记", "The post hut log",
                    "邮亭值守记：风灾后第三百天，邮袋还挂在根上。我把寄不出去的信都倒着挂，好让风一吹，它们就朝着家的方向。",
                    "Post hut log: three hundred days after the storm, the mailbags still hang in the roots. I hang the letters that could not be sent upside down, so that when the wind blows they point toward home."),
                Letter("Letter_06", "Region_F", "F", SkyIslandStoryFlag.None,
                    "寺里的扫地人", "From the one who sweeps the temple",
                    "折翎：寺里的钟我替你擦过了。你守着的那条路，我不怪你封上——我只怪那年风太大。等路修好，回来喝口热茶。——寺里的扫地人",
                    "Zheling — I polished the temple bell for you. I do not blame you for closing the road you guard; I only blame how hard the wind blew that year. When the road is mended, come back for a cup of hot tea. — the one who sweeps the temple"),
                Letter("Letter_07", "Region_S3", "S3", SkyIslandStoryFlag.None,
                    "航路图附言", "A note pinned to the chart",
                    "航路图附言：三道水声里，缓的那道最可靠。若你读到这里，替我把洞口的石头垒高一点，下一个躲雨的人会谢你。",
                    "A note pinned to the chart: of the three streams, the slow one is the one to trust. If you read this far, stack the stones at the cave mouth a little higher — the next person sheltering from the rain will thank you."),
                Letter("Letter_08", "Region_S4", "S4", SkyIslandStoryFlag.None,
                    "瞭台观星记", "The overlook stargazing log",
                    "观星记：镜片偏了，星星就走错了路，我们的船也跟着走错了路。等有人重新校准它，请替我看一眼正北那颗最亮的——那是回家的方向。",
                    "Stargazing log: when the lens drifts the stars go the wrong way, and our ships went the wrong way with them. When someone calibrates it again, look at the brightest star due north for me — that is the way home."),
                Letter("Letter_09", "Region_E", "E", SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp,
                    "栈道守望人", "From the boardwalk watch",
                    "栈道守望人：两盏灯都亮了。我在栏杆上刻下最后一道记号——风会循着光来，可光也会引我们回去。别怕它，跑出那一圈，然后回家。",
                    "From the boardwalk watch: both lamps are lit. I cut the last notch into the rail — the wind follows the light, but the light also leads us back. Do not fear it. Run clear of the ring, and then come home."),
                Letter("Letter_10", "POI_G", "G", SkyIslandStoryFlag.StarLamp,
                    "学徒的检修单", "The apprentice's work order",
                    "学徒小铆的检修单：星灯的铜环我擦了三遍。师傅说灯亮的时候钟庭会回应，我不信，除非亲耳听见。要是你听见了，写信告诉我。",
                    "Apprentice Xiaomao's work order: I polished the star lamp's brass rings three times. Master says the Bell Court answers when the lamp is lit. I will not believe it until I hear it myself. If you do, write and tell me."),
                Letter("Letter_11", "Lamp_H", "H", SkyIslandStoryFlag.Ending,
                    "名册的附页", "A loose page from the register",
                    "钟守名册的附页：钟响那天，名册上多了一行字。不知道是谁写的，只有四个字：『我回来了。』",
                    "A loose page from the Bell Keeper's register: on the day the bell rang, one new line appeared. Nobody knows who wrote it. It says only: 'I am home.'"),
                Letter("Letter_12", "Lamp_A", "A", SkyIslandStoryFlag.Ending,
                    "给下一位旅人", "To the next traveller",
                    "给下一位旅人：船系在码头，灯亮在云海上。你走过的每一座桥，都会有人接着走。谢谢你把路修好。——晴岚群岛的所有人",
                    "To the next traveller: the boat is tied at the dock and the lights burn over the cloud sea. Every bridge you crossed, someone will cross after you. Thank you for mending the way. — everyone on the Qinglan isles")
            };
        }

        private static SkyIslandLetter Letter(string id, string anchor, string region, SkyIslandStoryFlag requires,
            string titleCn, string titleEn, string bodyCn, string bodyEn)
        {
            return new SkyIslandLetter
            {
                Id = id, Anchor = anchor, Region = region, Requires = requires,
                TitleCn = titleCn, TitleEn = titleEn, BodyCn = bodyCn, BodyEn = bodyEn
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>一盏可以点起来的风晶灯：挂在哪个装置旁、哪封信在盼着它、要用什么点。</summary>
    internal sealed class SkyIslandLight
    {
        /// <summary>写进本槽手记的 id（前缀 <see cref="SkyIslandLights.IdPrefix"/>）。</summary>
        internal string Id;
        /// <summary>装置标记：灯亮在它上方，「点起风晶灯」挂在它的面板里。</summary>
        internal string Marker;
        /// <summary>所在区域（A–H / S1–S4），手记里念的地名。</summary>
        internal string Region;
        /// <summary>在信里盼着这盏灯的那封信（<see cref="SkyIslandLetters"/> 的 id）。</summary>
        internal string LetterId;
        /// <summary>点灯要用的东西：每盏都要一块晴岚风晶作灯芯，其余按这处地方的来历配。</summary>
        internal SkyIslandIngredient[] Inputs;
        internal string LitCn, LitEn;
        /// <summary>点亮时的那一句：信里的请求怎么实现了。</summary>
        internal string LitLine { get { return L10n.T(LitCn, LitEn); } }
    }

    /// <summary>
    /// COMPAT：岛上的灯——把批次三的风晶、材料、耗材与夜风，和批次二的来信、手记、名册串成一条线。
    ///
    /// 来历：倒挂邮亭那封旧信只求一件事「请别让岛上的灯熄灭」，写给蛙鸣池青蛙的孩子说「数到十盏灯就该回家了」。
    /// 三处居民的灶火一直亮着，算前三盏；其余七盏是七处装置旁缺的风晶灯，每一盏都是一封信里的请求
    /// （栈道守望人、寺里的扫地人、学徒小铆、钟守名册、邮亭值守、航路图附言、观星记）。
    ///
    /// 口径：
    /// - **持久**：点亮写进本槽手记（`discoveredNotes`，id 前缀 <see cref="IdPrefix"/>），不加存档字段、不加旗标位。
    ///   先记手记、再扣材料：写屏障下宁可这盏灯这趟点不起来，也不白扣一块风晶。
    /// - **有用**：灯旁与灶火旁一样暖和，什么风都挡得住（<see cref="SkyIslandWarmth.Shelter"/>）；
    ///   凑满 <see cref="Target"/> 盏之后，岛上的夜里不再起风（<see cref="SkyIslandFieldcraftRules.NightWind"/>，桥上照旧）。
    /// - **不新增交互体**：点灯是装置面板里的一个选项，灯只是光、没有碰撞体，不参与交互竞争。
    /// 纯逻辑、无 Unity 依赖，隔离回归直接执行。
    /// </summary>
    internal static class SkyIslandLights
    {
        internal const string IdPrefix = "Light_";

        /// <summary>三处居民的灶火（浮舟的渡口、晴禾的菜畦、眠苔的站位）：一直亮着，算前三盏。</summary>
        internal static readonly string[] HearthMarkers = { "Search_A", "Search_C", "POI_D" };

        /// <summary>蛙鸣池的孩子数到十盏就该回家：三处灶火 + 七盏风晶灯。</summary>
        internal const int Target = 10;

        private static readonly SkyIslandLight[] lights = Build();

        internal static SkyIslandLight[] All { get { return lights; } }

        internal static SkyIslandLight Find(string id)
        {
            for (int i = 0; i < lights.Length; i++)
                if (string.Equals(lights[i].Id, id, StringComparison.Ordinal)) return lights[i];
            return null;
        }

        /// <summary>这个装置旁缺的那盏灯；不是点灯的地方返回 null。</summary>
        internal static SkyIslandLight ForMarker(string marker)
        {
            for (int i = 0; i < lights.Length; i++)
                if (string.Equals(lights[i].Marker, marker, StringComparison.Ordinal)) return lights[i];
            return null;
        }

        internal static bool Lit(SkyIslandStoryData data, string id)
        {
            return data != null && data.discoveredNotes != null && id != null && Array.IndexOf(data.discoveredNotes, id) >= 0;
        }

        /// <summary>本存档点起来的风晶灯数（不含灶火）。</summary>
        internal static int LampsLit(SkyIslandStoryData data)
        {
            int count = 0;
            for (int i = 0; i < lights.Length; i++) if (Lit(data, lights[i].Id)) count++;
            return count;
        }

        /// <summary>岛上亮着的灯：三处灶火 + 点起来的风晶灯。</summary>
        internal static int LitCount(SkyIslandStoryData data) { return HearthMarkers.Length + LampsLit(data); }

        internal static bool AllLit(SkyIslandStoryData data) { return LitCount(data) >= Target; }

        /// <summary>还缺灯的装置标记（罗盘在带着晴岚风晶、又没有别的要找时指向最近的一处）。</summary>
        internal static IEnumerable<string> UnlitMarkers(SkyIslandStoryData data)
        {
            for (int i = 0; i < lights.Length; i++)
                if (!Lit(data, lights[i].Id)) yield return lights[i].Marker;
        }

        internal static string HearthName(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("浮舟的渡口灶火（一直亮着）", "Fuzhou's dock hearth (always lit)");
                case 1: return L10n.T("晴禾的菜畦灶火（一直亮着）", "Qinghe's garden stove (always lit)");
                default: return L10n.T("眠苔的药炉（一直亮着）", "Miantai's remedy fire (always lit)");
            }
        }

        /// <summary>装置面板上的按钮：「点起风晶灯（晴岚风晶 1/1 · 浮木 3/3 · 云苔纤维 2/2）」。</summary>
        internal static string ChoiceLabel(SkyIslandLight light, Func<int, int> countInPack)
        {
            if (light == null) return string.Empty;
            return L10n.T("点起风晶灯（", "Light a windcrystal lamp (") +
                SkyIslandFieldcraftRules.HaveNeedList(light.Inputs, countInPack) + L10n.T("）", ")");
        }

        /// <summary>「晴岚风晶 ×1 · 浮木 ×3」：手记里写还缺什么。</summary>
        internal static string CostList(SkyIslandLight light)
        {
            var text = new StringBuilder();
            if (light == null || light.Inputs == null) return string.Empty;
            for (int i = 0; i < light.Inputs.Length; i++)
            {
                if (i > 0) text.Append(" · ");
                text.Append(SkyIslandItemRules.Name(light.Inputs[i].TypeId)).Append(" ×").Append(light.Inputs[i].Count);
            }
            return text.ToString();
        }

        /// <summary>点亮时的字幕：那一句 + 「岛上的灯 n/10」。</summary>
        internal static string LitCaption(SkyIslandLight light, int litCount)
        {
            if (light == null) return string.Empty;
            return light.LitLine + L10n.T("（岛上的灯 ", " (lights on the isles ") + litCount + "/" + Target + L10n.T("）", ")");
        }

        internal static string AlreadyLit
        { get { return L10n.T("这里的风晶灯已经亮着了。", "The windcrystal lamp here is already burning."); } }

        /// <summary>第十盏亮起时的那一句。</summary>
        internal static string Capstone
        {
            get
            {
                return L10n.T("十盏灯都亮了。蛙鸣池边那个等灯的孩子，终于数到了十——从今夜起，岛上的夜里不再起风，只有桥上还留着一点。",
                    "All ten lights are burning. The child waiting by Frogsong Pool has finally counted to ten — from tonight the islands' nights are still, and only the bridges keep a little wind.");
            }
        }

        /// <summary>
        /// 手记里的「岛上的灯」一页：灶火与七盏风晶灯各一行，没亮的写还缺什么；盼着它的那封信收到了就写信名，没收到只说有一封信在等。
        /// 地名由调用方给（唯一来源仍是 `SkyIslandSession.RegionLabel`），这里不再抄一份。
        /// </summary>
        internal static string Chapter(SkyIslandStoryData data, Func<string, string> regionName)
        {
            var text = new StringBuilder();
            text.Append(L10n.T("岛上的灯 ", "Lights on the isles ")).Append(LitCount(data)).Append('/').Append(Target);
            for (int i = 0; i < HearthMarkers.Length; i++) text.Append("\n■ ").Append(HearthName(i));
            for (int i = 0; i < lights.Length; i++)
            {
                SkyIslandLight light = lights[i];
                bool lit = Lit(data, light.Id);
                text.Append('\n').Append(lit ? "■ " : "□ ").Append(regionName != null ? regionName(light.Region) : light.Region)
                    .Append(L10n.T(" · 风晶灯", " · windcrystal lamp"));
                if (!lit) text.Append(L10n.T("（", " (")).Append(CostList(light)).Append(L10n.T("）", ")"));
                SkyIslandLetter letter = SkyIslandLetters.Find(light.LetterId);
                if (letter != null && SkyIslandLetters.Collected(data, letter.Id))
                    text.Append(L10n.T(" — 《", " — \"")).Append(letter.Title).Append(L10n.T("》", "\""));
                else text.Append(L10n.T(" — 有一封信在盼着它", " — a letter is waiting on it"));
            }
            text.Append("\n\n").Append(L10n.T(
                "灶火与风晶灯旁暖和，什么风都挡得住。残星工坊的星灯亮起之后，浮舟的渡口工台能把五片风晶碎片熔成一块晴岚风晶，那就是灯芯。十盏灯都亮起来，岛上的夜里就不再起风（桥上照旧有风）。",
                "It is warm beside a hearth or a windcrystal lamp, and no wind gets through. Once the Fallen Star Workshop's star lamp is lit, Fuzhou's dock workbench can fuse five windcrystal shards into a Qinglan Windcrystal — that is the wick. With all ten lights burning, the islands' nights stop blowing (the bridges keep their wind)."));
            if (AllLit(data)) text.Append("\n\n").Append(Capstone);
            return text.ToString();
        }

        private static SkyIslandLight[] Build()
        {
            int crystal = BossRushItemIds.SkyIslandQinglanWindcrystal;
            return new[]
            {
                Lamp("Light_E", "Search_E", "E", "Letter_09",
                    "栈道栏杆最后一道记号旁亮起了灯。守望人说得对：风循着光来，光也引人回去。",
                    "A lamp comes up beside the last notch on the boardwalk rail. The watch was right: the wind follows the light, but the light leads people home.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandDriftwood, 3), In(BossRushItemIds.SkyIslandCloudmossFiber, 2)),
                Lamp("Light_F", "Search_F", "F", "Letter_06",
                    "镜水寺的香炉里焚着驱风香，烟绕着风晶灯转。扫地的老人说，这下给折翎留的茶一直是热的。",
                    "Windward incense smoulders in the Mirrorwater Temple censer, its smoke circling the windcrystal lamp. The old sweeper says the tea kept for Zheling will stay hot now.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandWindwardIncense, 1), In(BossRushItemIds.SkyIslandDriftwood, 2)),
                Lamp("Light_G", "Search_G", "G", "Letter_10",
                    "新打的铜环挂在工坊的灯下，亮得跟小铆擦过三遍的那几个一样。",
                    "The freshly beaten brass rings hang under the workshop lamp, shining like the ones Xiaomao polished three times.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandBrassScrap, 4)),
                Lamp("Light_H", "Search_H", "H", "Letter_11",
                    "钟架下挂起一盏灯。夜里也看得清名册上的每一个名字了。",
                    "A lamp now hangs beneath the bell frame. Every name in the register can be read, even at night.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandWindLantern, 1), In(BossRushItemIds.SkyIslandBrassScrap, 2)),
                Lamp("Light_S2", "Search_S2", "S2", "Letter_05",
                    "邮亭里倒挂的信都朝着有光的方向了——风一吹，它们就指着家。",
                    "Every letter hanging upside down in the post hut now points toward the light. When the wind blows, they point home.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandCloudmossFiber, 3), In(BossRushItemIds.SkyIslandDriftwood, 1)),
                Lamp("Light_S3", "Search_S3", "S3", "Letter_07",
                    "洞口的石头垒高了一截，灯挂在石头后面。下一个躲雨的人不用摸黑了。",
                    "The stones at the cave mouth stand a little higher, with a lamp hung behind them. The next person sheltering from the rain will not sit in the dark.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandDriftwood, 2), In(BossRushItemIds.SkyIslandBrassScrap, 2)),
                Lamp("Light_S4", "Search_S4", "S4", "Letter_08",
                    "观星镜旁亮起一盏撒了星屑的灯。正北那颗最亮的星，就在灯的正上方。",
                    "A lamp dusted with stardust glows beside the telescope. The brightest star due north sits right above it.",
                    In(crystal, 1), In(BossRushItemIds.SkyIslandStardust, 2))
            };
        }

        private static SkyIslandLight Lamp(string id, string marker, string region, string letterId, string litCn, string litEn,
            params SkyIslandIngredient[] inputs)
        {
            return new SkyIslandLight
            {
                Id = id, Marker = marker, Region = region, LetterId = letterId, LitCn = litCn, LitEn = litEn, Inputs = inputs
            };
        }

        private static SkyIslandIngredient In(int typeId, int count) { return new SkyIslandIngredient(typeId, count); }
    }
}

// ============================================================================
// SkyIslandPointText.cs - 20 处见闻点的三张文案表
// ============================================================================
// 从 `SkyIslandWorldStory` 拆出来：它们是**纯静态查表**，没有任何会话状态，
// 和 `SkyIslandJournal` / `SkyIslandLetters` 一样该各住各的（AGENTS 4.15），
// 而 WorldStory 卡在 1200 行预算上。
//
// 【三张表的分工，别混用】
//   - `Name`  —— 地名。面板标题、官方图鉴条目标题、地图指引点共用。
//   - `Brief` —— **一句导语**（≤40 中文字），回答「这是哪儿、我该干什么」。**只有面板正文用它**。
//   - `Lore`  —— 长文。去两个地方：官方笔记图鉴的条目正文（`SkyIslandNoteBridge`），
//                以及秘境谜题页的引子（`PuzzleBody`）。**不再进面板正文**——
//                此前面板正文是 `Lore + 目标卡`，中文最长 137 字，以灰字一次糊在面板上。
//
// 文案的唯一来源就是这里。图鉴那边取的是同一份，不许另写。
// ============================================================================

namespace BossRush
{
    /// <summary>见闻点文案表。全静态、无状态。</summary>
    internal static class SkyIslandPointText
    {
        internal static string Name(string key)
        {
            switch (key)
            {
                case "Search_A": return L10n.T("登云码头 · 渡口整备", "Cloudrise Dock · dock refit");
                case "Search_D": return L10n.T("校准风标 · 开启林边回程路",
                    "Calibrate the wind beacon · open the woodland way home");
                case "Search_G": return L10n.T("修复星灯 · 开启检修廊",
                    "Repair the star lamp · open the maintenance walk");
                case "Search_E": return L10n.T("双航标门 · 中轴旧桥与风眼",
                    "Twin-beacon gate · the old centre bridge and the storm's eye");
                case "Search_H": return L10n.T("归航钟 · 钟守留言",
                    "Homecoming Bell · the Bell Keeper's message");
                case "Search_B": return L10n.T("风铃集留言板 · 种植记录与航务委托",
                    "Windchime Market noticeboard · planting record and lane contracts");
                case "Search_C": return L10n.T("青穗梯田 · 归航菜畦", "Green Terraces · the homecoming garden");
                case "Search_F": return L10n.T("旧航路守卫 · 折翎", "Keeper of the old route · Zheling");
                case "Search_S1": return L10n.T("晴禾的种植记录", "Qinghe's planting record");
                case "Search_S2": return L10n.T("风没有送到的信", "The letter the wind never delivered");
                case "Search_S3": return L10n.T("听雨洞的旧航路图", "The old route chart in the Rainlisten Grotto");
                case "Search_S4": return L10n.T("修复观星镜", "Repair the telescope");
                // 8 个 _02 见闻点各有自己的标题与正文（以前全部落进 default，20 处见闻里 8 处是同一句话）。
                // 正文各指向一处支线或机制，见 Lore：种植记录、委托规矩、眠苔、旧信、噬风的风眼、航路图、观星镜、钟守要的证据。
                case "Search_A_02": return L10n.T("登云码头 · 渡船的等候名单", "Cloudrise Dock · the ferry waiting list");
                case "Search_B_02": return L10n.T("风铃集 · 委托单存根", "Windchime Market · contract stubs");
                case "Search_C_02": return L10n.T("青穗梯田 · 田埂上的记号", "Green Terraces · marks on the ridge");
                case "Search_D_02": return L10n.T("悬根林 · 吊在根上的邮袋", "Hanging Root Wood · a mailbag in the roots");
                case "Search_E_02": return L10n.T("鸣风栈道 · 栏杆上的刻痕", "Windsong Boardwalk · notches on the rail");
                case "Search_F_02": return L10n.T("镜水寺 · 池底的航路图拓本", "Mirrorwater Temple · a rubbing in the pool");
                case "Search_G_02": return L10n.T("残星工坊 · 瞭台检修日志", "Fallen Star Workshop · the overlook log");
                case "Search_H_02": return L10n.T("归航钟庭 · 钟架下的签名", "Homecoming Bell Court · names under the bell frame");
                default: return L10n.T("阅读群岛见闻", "Read the archipelago notes");
            }
        }

        /// <summary>
        /// 见闻点的**导语**：一句话，回答「这是哪儿、我该干什么」。面板正文只放它。
        ///
        /// 【为什么要拆出来】旧版面板正文是 <see cref="Lore"/> + 目标卡，中文最长 137 字
        /// （归航钟庭 114 字的长段 + 一行 `·` 分隔的目标），以 22px 灰字一次糊在面板上。
        /// 长文没有删，它去了两个更合适的地方：
        /// - **官方笔记图鉴**（<see cref="SkyIslandNoteBridge"/>）——收录之后回基地也翻得到；
        /// - 居民那边走官方对话逐句说（见 Talk）。
        /// 目标卡也没有删，它本来就常驻在右上角 HUD 上，不必在正文里再说一遍。
        /// </summary>
        internal static string Brief(string key)
        {
            switch (key)
            {
                case "Search_A": return L10n.T(
                    "浮舟的渡口。缆绳每天留着一条，工具也还在。",
                    "Fuzhou's dock. One mooring line kept free every day, and the tools still here.");
                case "Search_B": return L10n.T(
                    "留言板上钉着三张纸，谁都可以揭一张。",
                    "Three sheets pinned to the board. Anyone may take one.");
                case "Search_C": return L10n.T(
                    "晴禾的菜畦一层层种向云海，灶还温着。",
                    "Qinghe's beds step down toward the cloud sea. The stove is still warm.");
                case "Search_D": return L10n.T(
                    "风标卡在巨根之间。清掉附近的威胁再校准。",
                    "The wind beacon is jammed in the roots. Clear the threats nearby, then calibrate.");
                case "Search_E": return L10n.T(
                    "双航标门要两端的灯同时回应。门后就是钟庭。",
                    "The twin-beacon gate needs both lights answering. The Bell Court lies beyond.");
                case "Search_F": return L10n.T(
                    "折翎留了张告示：风灾没有夺走全部航路。",
                    "A notice from Zheling: the storm did not take every lane.");
                case "Search_G": return L10n.T(
                    "铜环还完整，星灯只等一次重新校准。",
                    "The brass rings are intact. The star lamp needs one recalibration.");
                case "Search_H": return L10n.T(
                    "归航钟不再催人出航。它为什么再响，看你。",
                    "The Homecoming Bell no longer sends anyone out. Why it rings again is up to you.");
                case "Search_S1": return L10n.T(
                    "池边的纸页记着菜种、日期，和每个归航人的名字。",
                    "The pages by the pool list seeds, dates, and everyone expected home.");
                case "Search_S2": return L10n.T(
                    "一封没寄出的旧信压在邮亭里。",
                    "An unsent letter is wedged inside the post hut.");
                case "Search_S3": return L10n.T(
                    "三道水声从洞壁传来。旧航路图把它们标成避风口。",
                    "Three streams sound through the cave wall. The old chart marks them as shelter.");
                case "Search_S4": return L10n.T(
                    "观星镜被守卫占着。清掉他们，校准镜片。",
                    "Guards have taken the telescope. Clear them out and align the lens.");
                case "Search_A_02": return L10n.T(
                    "等候名单划掉了大半，最后一行是浮舟新添的。",
                    "Most of the waiting list is crossed out. The last line is new, in Fuzhou's hand.");
                case "Search_B_02": return L10n.T(
                    "一摞交过的委托单存根，最底下写着苇白的规矩。",
                    "A stack of finished contract stubs, with Weibai's rule at the bottom.");
                case "Search_C_02": return L10n.T(
                    "田埂木桩上刻着箭头，一路指向悬根林。",
                    "Arrows cut into the ridge posts, all pointing at the Hanging Root Wood.");
                case "Search_D_02": return L10n.T(
                    "巨根上挂着一只空邮袋，根下留着眠苔的药臼。",
                    "An empty mailbag hangs in the roots; Miantai's mortar sits below them.");
                case "Search_E_02": return L10n.T(
                    "栏杆上新刻了一排记号，像有人在数日子。",
                    "A fresh row of notches along the rail, as if someone were counting days.");
                case "Search_F_02": return L10n.T(
                    "池底压着半张泡软的拓本，另一半在折翎手里。",
                    "Half a water-softened rubbing lies in the pool. Zheling holds the other half.");
                case "Search_G_02": return L10n.T(
                    "检修日志停在最后一页：镜片偏了三格。",
                    "The maintenance log stops on its last page: the lens has drifted three notches.");
                case "Search_H_02": return L10n.T(
                    "钟架横梁下签满了名字，每个名字旁记着一件东西。",
                    "The beam under the bell frame is covered in names, each with something noted beside it.");
                default: return L10n.T(
                    "旧木牌上记着岛民的一天。",
                    "An old board records a day on the islands.");
            }
        }

        /// <summary>
        /// 见闻点的长文。**唯一来源**：岛上的面板与官方笔记图鉴
        /// （`SkyIslandNoteBridge.InjectNoteKeys`）都取它，不许各写一份。
        /// </summary>
        internal static string Lore(string key)
        {
            switch (key)
            {
                case "Search_A": return L10n.T(
                    "浮舟的渡船日志：风灾之后，码头仍每天留着一条返航的缆绳。沿北面的桥去风铃集，苇白正在等能修灯的人。渡口的工具还在，钝了的家伙可以在这里回一回火。",
                    "Fuzhou's ferry log: since the storm, the dock still keeps one mooring line free every day. Take the north bridge to Windchime Market — Weibai is waiting for someone who can mend the lamps. The dock tools are still here, so anything gone blunt can be brought back to an edge.");
                case "Search_B": return L10n.T(
                    "留言板上钉着三张纸：苇白在找修复两端航标的帮手，晴禾在找落在蛙鸣池的种植记录，还有一张空白的委托单，谁都可以揭。即使主人离岛，留言也能送到。",
                    "Three sheets are pinned to the board: Weibai wants help restoring both beacons, Qinghe is looking for the planting record she left at Frogsong Pool, and one blank contract slip anyone may take. Messages get through even when their owners are away from the island.");
                case "Search_C": return L10n.T(
                    "晴禾把菜畦一层层种向云海。田埂上的空格属于尚未归来的船员。灶还温着——种植记录回来之后，谁路过都能讨一碗归航菜。夜里下地前，可以在这口灶上做驱风香或云苔纱笠；灶火的烟也能赶开云蚋。",
                    "Qinghe planted the beds in terraces stepping down toward the cloud sea. The gaps along the ridge belong to crew who have not come back. The stove is still warm — once the planting record returns, anyone passing may ask for a bowl of homecoming greens. Before working at night, make incense or a cloudmoss veil here; the hearth smoke drives cloud gnats away too.");
                case "Search_D": return L10n.T(
                    "风标卡在巨根之间。清掉附近的威胁后，校准指针，让西侧的航路重新有方向。",
                    "The wind beacon is jammed among the great roots. Clear the threats nearby, then calibrate the needle and give the western lane its bearing back.");
                case "Search_E": return L10n.T(
                    "双航标门需要风标与星灯同时回应。门后是归航钟庭；桥边的绞盘控制回村的中轴旧桥。栏杆上有一行后来刻的字：灯亮之后，别一个人站在桥心 —— 有东西会循着光过来。",
                    "The twin-beacon gate needs the wind beacon and the star lamp answering together. Beyond it lies the Homecoming Bell Court; the winch by the bridge works the old centre span back to the village. A later hand cut a line into the rail: once the lights are up, do not stand alone at mid-span — something comes for the light.");
                case "Search_F": return L10n.T(
                    "折翎留下的告示：风灾并未夺走全部航路。旧信与听雨洞的图纸或许能让他改变决定。",
                    "A notice left by Zheling: the storm did not take every lane. The old letter and the chart from the Rainlisten Grotto might change his mind.");
                case "Search_G": return L10n.T(
                    "工坊的铜环仍然完整。星灯只等一次重新校准，便能把东侧的光送回村庄。",
                    "The workshop's brass rings are still intact. The star lamp needs only one recalibration to send the eastern light back to the village.");
                // 守钟装置照着钟守的样子造（R-11 拍板：不是画错脸，是钟庭的规矩）。
                // 这句是玩家**唯一**能读到的解释，而挑战选项就挨在同一页上——不写清楚，
                // 打起来只会觉得「名字说是装置，脸却是他本人」。
                case "Search_H": return L10n.T(
                    "归航钟不再催促出航。两端的航标、归来的信件与守钟人的选择，将决定它为什么再次响起。钟架下立着守钟装置——钟庭的规矩，敲钟的机械一律照着当值守钟人的样子铸，好让归来的人远远就认得出谁在等。它如今空转不停，那张脸也就一直是钟守的脸。",
                    "The Homecoming Bell no longer urges anyone to sea. The two beacons, the letter that came home and the keeper's own choice will decide why it rings again. Beneath the bell frame stands the bell engine: by the Court's custom every ringing machine is cast in the likeness of the keeper on duty, so that those coming home can tell from far off who is waiting. It has been running empty ever since, and so the face it wears is still the Bell Keeper's.");
                case "Search_S1": return L10n.T(
                    "池边潮湿的纸页上记着菜种、日期，以及每一个归航人的名字。晴禾在页角留了话：镜水寺的青蛙还在繁育，夜里可用云苔纤维包一团蛙卵带回来，白天也能放。放回的会一直记着，顺路送一团就好，不用一趟来回跑齐。",
                    "The damp pages by the pool list seeds, dates, and the name of every person expected home. Qinghe added a note in the margin: the temple frogs still breed. Wrap a clutch of spawn in cloudmoss one night and bring it here; daylight is fine for release. Each clutch is remembered — bring one when passing, without making every trip in one raid.");
                case "Search_S2": return L10n.T(
                    "没有寄出的旧信压在倒挂邮亭里。字迹歪斜，却还清楚地写着：请别让岛上的灯熄灭。",
                    "An unsent letter is wedged inside the Upturned Post Hut. The hand is crooked but still plain: please do not let the island's lights go out.");
                case "Search_S3": return L10n.T(
                    "三道水声从洞壁传来；旧航路图把它们标成避风口。沿图上的虚线，船其实可以平安绕过风灾。",
                    "Three streams sound through the cave wall; the old route chart marks them as shelter. Follow the dotted line and a ship can in fact pass the storm safely.");
                case "Search_S4": return L10n.T(
                    "观星镜被守卫占据。清除威胁、校准镜片，无论白天黑夜都能找回群岛的星图。",
                    "Guards have taken the telescope. Clear them out and align the lens, and the archipelago's star chart comes back day or night.");
                // _02 见闻：每处一段自己的记录，各自给一条去处或机制的线索（主线目标卡只管航标与钟庭，支线全靠这些被发现）。
                case "Search_A_02": return L10n.T(
                    "码头的等候名单上划掉了大半的名字。最后一行是浮舟的字：『梯田那头的蛙鸣池有人捎信回来——晴禾的种植记录还泡在水边。』",
                    "Most of the names on the dock's waiting list are crossed out. The last line is in Fuzhou's hand: 'Word came back from Frogsong Pool, past the terraces — Qinghe's planting record is still lying by the water.'");
                case "Search_B_02": return L10n.T(
                    "一摞交过的委托单存根：清理航路、回收补给、巡视群岛。苇白在最底下记着规矩：『一趟最多派三单，一单比一单重；第三单的谢礼从工坊的旧货里出。』",
                    "A stack of stubs from finished contracts: clearing lanes, recovering supplies, surveying the isles. At the bottom Weibai has written down her rule: 'Three contracts a trip at most, each a little heavier than the last. The third payout comes out of the workshop's old stock.'");
                case "Search_C_02": return L10n.T(
                    "田埂的木桩上刻着箭头，一路指向悬根林：『风标卡住那天，林里的人都搬到根环后面去了。受了伤就去找眠苔，她的苔药按伤势收钱。』",
                    "Arrows are cut into the ridge posts, all pointing at the Hanging Root Wood: 'The day the wind beacon jammed, the wood folk moved in behind the root ring. If you are hurt, find Miantai — her moss remedy is priced by how badly you are hurt.'");
                case "Search_D_02": return L10n.T(
                    "巨根上挂着一只空邮袋，标签写着「倒挂邮亭」。袋底粘着一张回执：寄往镜水寺，收信人折翎。那封信，一直没有送到。根下留着眠苔的药臼，能配驱风香、星苔药膏，也能做一把可反复用的药烟蒲扇，赶开贴脸的云蚋。",
                    "An empty mailbag hangs in the great roots, tagged 'Upturned Post Hut'. A receipt is still stuck to the bottom: to Mirrorwater Temple, for Zheling. That letter never arrived. Miantai left her mortar below the roots: mix incense or salve, or make a reusable remedy-smoke fan for the cloud gnats around your face.");
                case "Search_E_02": return L10n.T(
                    "栏杆上新刻了一排记号，像是有人在数日子：『两盏灯都亮的那一夜，风从云海底下翻了上来。它收拢风眼之前，脚下先亮一圈光——看见光就往圈外跑，跑不出去就躲到石头后面。』",
                    "A fresh row of notches runs along the rail, as if someone were counting the days: 'The night both lamps were lit, the wind climbed up out of the cloud sea. Before it draws its eye shut, a ring of light shows at its feet. See the light, run out of the ring — or get behind solid rock.'");
                case "Search_F_02": return L10n.T(
                    "池底压着一张被水泡软的拓本，只看得清半条航线，终点圈着「听雨洞」。另一半在折翎手里——他说旧信和航路图都齐了，才肯坐下来谈。池边的石缝里挂着一团团蛙卵：风灾那年，蛙鸣池的青蛙逃到了这里。",
                    "A water-softened rubbing is weighted down at the bottom of the pool. Only half a route is legible, ending in a circle marked 'Rainlisten Grotto'. Zheling holds the other half; he will only sit down and talk once the old letter and the route chart are both on the table. Clumps of frogspawn cling between the stones at the edge: the year of the storm, the frogs of Frogsong Pool fled here.");
                case "Search_G_02": return L10n.T(
                    "检修日志的最后一页：『观星镜的镜片偏了三格，残星瞭台上来了一伙人，谁也上不去。等把他们清走，照着星灯的方向校准就行。』",
                    "The last page of the maintenance log: 'The telescope lens has drifted three notches, and a gang has taken over the Starfall Overlook, so nobody can get up there. Once they are cleared out, align it with the star lamp.'");
                case "Search_H_02": return L10n.T(
                    "钟架横梁下签满了名字，每个名字旁都记着一件东西：一封信，一张图，一面镜片。有一行只写了开头：『等那阵风散了……』钟守说，这些都是航路安全的证据。",
                    "The beam under the bell frame is covered in signatures, and beside each one something is noted: a letter, a chart, a lens. One line has only its opening: 'Once that wind is gone…' The Bell Keeper calls all of these proof that the lanes are safe.");
                default: return L10n.T(
                    "旧木牌上记录着岛民的一天：有人等信，有人修灯，有人把空船再系紧一点。你走过的地方，正在重新连接。",
                    "An old board records a day on the islands: someone waiting for a letter, someone mending a lamp, someone tying an empty boat a little tighter. The places you walk are joining back up.");
            }
        }
    }
}

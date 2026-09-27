// ============================================================================
// SkyIslandChatterLines.cs - 天空岛头顶气泡的全部话语（纯规则，无 Unity 依赖）
// ============================================================================
// 【为什么单独一份】这张图此前是静止的：居民钉在标记上不出声，敌人从生成到倒下一个字都没有。
//   基地的快递员阿稳早就会漫步 + 头顶冒气泡（`CourierMovement` / `CourierNPCController.ShowRandomDialogue`），
//   天空岛却连一句环境话都没有。本文件只放「说什么」，「什么时候说、说不说得出口」在 `SkyIslandChatter`。
//
// 【为什么无 Unity 依赖】与 `SkyIslandBossRules` / `SkyIslandStoryRules` 同形：隔离回归
//   （tests/fixtures/SkyIslandStory）直接链接本文件执行，挑选规则与覆盖面离线就能证。
//
// 【写法纪律：先定人设，再写话】每个池子上方都有一行 `// 【声音】`，写清这位是谁、怎么说话、
//   不会说什么。`tests/SkyIslandChatterGuard.py` 按它核对每个说话者都有人设、每条台词都双语且短。
//   - **不写通用喊话**。「发现敌人！」「同伴倒下！」这类模板放到哪张图都成立，等于没写。
//     岛上的拾荒者是风灾断航之后上来讨生活的人，游猎是占着中继平台收过路费的另一伙，
//     头目的话直接从 `SkyIslandBossRules` 每条档案上方的 `// 【五栏】` 推出来。
//   - **气泡不是字幕**。机制预警走 `SkyIslandHud.Caption` 的警示通道（一句话说清怎么躲）；
//     气泡只做演出，短、可漏读，漏了不影响打得过。所以中文 <= MaxChineseChars 字、
//     英文 <= MaxEnglishChars 字符——官方气泡定宽，长句会被挤成两三行糊在头顶。
//   - **有的角色不说话**。无声钟守只出钟声与省略号，失控的守钟装置是机器，噬风是一股风：
//     给它们配台词就等于把人设推翻。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>一句气泡是在什么时刻说的。小兵用前三项，头目 / 具名对手用 Noticed / Wounded / Down。</summary>
    internal enum SkyIslandChatterMoment
    {
        /// <summary>没在打你的时候：居民的日常，小兵的闲话。</summary>
        Idle = 0,
        /// <summary>小兵第一次注意到你（官方 `AICharacterController.noticed` 的跳变）；头目是出场那一句。</summary>
        Noticed = 1,
        /// <summary>小兵：身边刚倒下一个同伴。头目不用。</summary>
        AllyDown = 2,
        /// <summary>头目：跨过一档血线。小兵不用。</summary>
        Wounded = 3,
        /// <summary>头目：自己倒下。小兵不用。</summary>
        Down = 4
    }

    /// <summary>
    /// COMPAT：天空岛气泡话语表。纯逻辑，返回的是**已按当前语言解析过**的字符串
    /// （`L10n.T` 在本文件内部就取过了，口径同 <see cref="SkyIslandStoryService.DescribeNpc"/>），
    /// 因此玩家在岛上切语言时下一句就换语言，不需要重建任何对象。
    /// </summary>
    internal static class SkyIslandChatterLines
    {
        /// <summary>一句气泡的长度上限。官方气泡定宽，超了会在头顶糊成三行。守卫逐条核对。</summary>
        internal const int MaxChineseChars = 20;
        internal const int MaxEnglishChars = 56;

        private static readonly string[] Silent = new string[0];

        /// <summary>
        /// 从 <paramref name="count"/> 条里挑一条，**不与上一条重复**。纯函数：给定
        /// <paramref name="seed"/> 结果确定，隔离回归逐条复算。
        /// </summary>
        /// <param name="last">上一次挑中的下标；没说过传 -1。</param>
        /// <returns>下标；池子为空返回 -1。</returns>
        internal static int Pick(int count, int last, int seed)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            // 上一句无效（第一次说、或池子换小了）：在全部 count 条里挑。
            if (last < 0 || last >= count) return (int)((uint)seed % (uint)count);
            // 上一句有效：在剩下的 count-1 条里均匀挑，再把下标映射回原池子并跳过 last。
            int index = (int)((uint)seed % (uint)(count - 1));
            return index >= last ? index + 1 : index;
        }

        // ====================================================================
        // 居民
        // ====================================================================

        /// <summary>
        /// 这位居民此刻会嘟囔些什么。<paramref name="data"/> 为 null 时给早期版本（存档还没读出来的那一帧）。
        /// 话题只绕着自己的活计走：报路线、讲打法是 <see cref="SkyIslandStoryService.DescribeNpc"/> 里
        /// 面对面说的事，不往气泡里塞。
        /// </summary>
        internal static string[] Resident(string npcId, SkyIslandStoryData data)
        {
            bool ending = data != null && data.Has(SkyIslandStoryFlag.Ending);
            switch (npcId)
            {
                // 【声音】晴禾：种地的。短句、絮叨，拿吃的表达关心，话题绕着天气、火候、下一船。不报路线、不讲打法。
                case "sky_qinghe":
                    if (ending) return new[]
                    {
                        L10n.T("船靠岸的时候，饭得刚好出锅。", "Food comes off the stove just as the boat docks."),
                        L10n.T("最后一畦留给下一船。", "The last bed is for the next boat."),
                        L10n.T("热的。趁热。", "It's hot. Eat it hot."),
                        L10n.T("风车转得比去年顺。", "The pinwheel turns easier than last year."),
                        L10n.T("锅我还是温着。习惯了。", "I still keep the pot warm. Old habit.")
                    };
                    return new[]
                    {
                        L10n.T("风大，苗得压着点土。", "Wind's up. Bank the soil over these sprouts."),
                        L10n.T("锅一直温着。谁回来都有口热的。", "Pot's always warm. Anyone home gets something hot."),
                        L10n.T("这畦是留着的。别踩。", "This bed is spoken for. Mind your feet."),
                        L10n.T("你脸色不好。过会儿来吃点。", "You look rough. Come eat later."),
                        L10n.T("云海底下还有没有田？没人说得准。", "Farmland under the cloud sea? Nobody can say."),
                        L10n.T("天要变了。手上快点。", "Weather's turning. Work faster.")
                    };
                // 【声音】苇白：风铃集委托板的管事。嘴快、爱张罗，说话像在派活。不悲春伤秋。
                case "sky_weibai":
                    if (ending) return new[]
                    {
                        L10n.T("钟一响，两头的风铃跟着响了。", "The bell rang and both chime strings answered."),
                        L10n.T("板子我还挂着。路过就揭一张。", "Board's still up. Take a slip on your way past."),
                        L10n.T("这回的活，是给回来的人派的。", "This round of work is for the ones coming back."),
                        L10n.T("西边那串风铃，我总算调准了。", "Finally got the west chimes in tune.")
                    };
                    return new[]
                    {
                        L10n.T("板子上还挂着几张，谁揭谁得。", "Slips still on the board. First come, first served."),
                        L10n.T("风铃响得不对……西边那串又松了。", "Chimes are off. West string's loose again."),
                        L10n.T("有活就吆喝一声，我记得住。", "Shout if you've got work. I keep track."),
                        L10n.T("别站风口挡着我看路。", "Don't stand in the gap, you're blocking my view."),
                        L10n.T("桥那头来人了？我怎么没听见铃。", "Someone on the bridge? I didn't hear the bells."),
                        L10n.T("记账的纸又被风刮走一张。", "Wind took another page of my ledger.")
                    };
                // 【声音】浮舟：码头老手。话少、稳，说船、说绳结、说风。不啰嗦。
                case "sky_fuzhou":
                    if (ending) return new[]
                    {
                        L10n.T("名册又添了一页。得收在不漏雨的地方。", "Another page in the roster. Needs somewhere dry."),
                        L10n.T("船会回来的。这话我说了很多年。", "The boats come back. I've said it for years."),
                        L10n.T("桩子都上过油了。", "Posts are all oiled."),
                        L10n.T("今天靠岸顺。", "Easy docking today.")
                    };
                    return new[]
                    {
                        L10n.T("潮位在涨。缆再放半尺。", "Tide's rising. Give the line another half foot."),
                        L10n.T("这结不对。回头重打一遍。", "Bad knot. I'll redo it later."),
                        L10n.T("绳子得先备好，船才好回来。", "Ropes first. Then the boats can come home."),
                        L10n.T("风从西北来，今天不好靠岸。", "Northwest wind. Bad day to come alongside."),
                        L10n.T("木头受潮了，手感不一样。", "Wood's damp. You can feel the difference.")
                    };
                // 【声音】眠苔：药师。冷淡、专业，只关心伤口，不寒暄。不安慰人。
                case "sky_miantai":
                    return new[]
                    {
                        L10n.T("别用那只手拎东西。", "Stop carrying things with that arm."),
                        L10n.T("苔要阴干。晒过的没用。", "Moss dries in shade. Sun-dried is useless."),
                        L10n.T("伤口别沾水。说过了。", "Keep the wound dry. I've said it."),
                        L10n.T("疼是好事。不疼才该怕。", "Pain is fine. Numbness is the problem."),
                        L10n.T("站远点，你在往药臼里滴血。", "Step back. You're dripping in my mortar."),
                        L10n.T("这味道呛？那就对了。", "Sharp smell? Then it's working.")
                    };
                // 【声音】折翎：旧航路守卫。戒备、话里有过去，句子短而重。不闲聊。和解之后才松一点。
                case "sky_zheling":
                    if (data != null && data.Has(SkyIslandStoryFlag.ZhelingReconciled)) return new[]
                    {
                        L10n.T("灯我看着。你走你的。", "I'll watch the lamp. Go on."),
                        L10n.T("有人回来，总得有人接。", "If they come back, someone has to be here."),
                        L10n.T("这条路，我还是每天走一遍。", "I still walk this road once a day."),
                        L10n.T("风小了。今天好走。", "Wind's down. Easy walking today.")
                    };
                    return new[]
                    {
                        L10n.T("再往前就是旧航路。到此为止。", "The old route starts past here. Far enough."),
                        L10n.T("风灾那天，也是这样的天色。", "The sky looked like this the day of the storm."),
                        L10n.T("灯灭了以后，我就站在这儿。", "I've stood here since the lamps went out."),
                        L10n.T("你要过去？先说清楚为什么。", "You want through? Tell me why first.")
                    };
                // 【声音】无声钟守：**不说话**。只有钟声与省略号——名字里就写着「无声」，
                // 给他配台词等于把人设推翻。这是全表唯一的非语言说话者。
                case "sky_bellkeeper":
                    return new[]
                    {
                        L10n.T("……", "..."),
                        L10n.T("铛——", "Clang—"),
                        L10n.T("………", ".....")
                    };
                default: return Silent;
            }
        }

        // ====================================================================
        // 小兵：两个共享库
        // ====================================================================

        /// <summary>
        /// 【声音】云沿拾荒者：风灾断了航路之后上岛翻东西的人。不是怪物，是来讨生活的——
        /// 说的是残铜卖几个钱、风晶不敢往云海边采、这屋子以前住过人、攒够船钱就下山。
        /// 全岛拾荒者共用这一个池子（owner 要求「小兵话语少一点」，所以单人冷却比居民长得多，见 `SkyIslandChatter`）。
        /// </summary>
        internal static string[] Scav(SkyIslandChatterMoment moment)
        {
            switch (moment)
            {
                case SkyIslandChatterMoment.Idle: return new[]
                {
                    L10n.T("这堆残铜再翻翻，说不定还能卖。", "Dig the brass again. Might still be worth something."),
                    L10n.T("风一停就冷。冷了就想下山。", "Cold when the wind drops. Makes you want to leave."),
                    L10n.T("风晶在云海边上。谁敢去谁去。", "Crystal's out by the cloud sea. Go if you dare."),
                    L10n.T("这屋子住过人。锅还挂在墙上。", "Someone lived here. Pot's still on the wall."),
                    L10n.T("上头说下月来收货。上月也这么说。", "Collection's next month, they say. Said that last month."),
                    L10n.T("别往钟那边走。那边邪门。", "Don't go near the bell. That side's bad news."),
                    L10n.T("我干到攒够船钱就走。", "I work till I've got boat fare, then I'm gone."),
                    L10n.T("桥板松了一块，记着点。", "One bridge board's loose. Remember it."),
                    L10n.T("半夜有东西在飞，不是鸟。", "Something flies at night. Not birds."),
                    L10n.T("谁把灯点起来了？没见过这么亮。", "Who lit that lamp? Never seen it so bright.")
                };
                case SkyIslandChatterMoment.Noticed: return new[]
                {
                    L10n.T("有人上来了！这片是我们先到的！", "Someone's up here! We got to this patch first!"),
                    L10n.T("喂！那边那个！站住！", "Hey! You there! Hold it!"),
                    L10n.T("又是来抢货的。", "Another one after our haul."),
                    L10n.T("从桥那头来的。拦住！", "Came over the bridge. Head them off!"),
                    L10n.T("别碰那堆东西！", "Don't touch that pile!"),
                    L10n.T("这地方没你的份，走。", "There's no share for you here. Walk.")
                };
                case SkyIslandChatterMoment.AllyDown: return new[]
                {
                    L10n.T("又倒一个。这趟不值当。", "Another one down. This run isn't worth it."),
                    L10n.T("那份……先放着，别动。", "That share... leave it be for now."),
                    L10n.T("先别管了，把货看住。", "Leave it. Watch the haul."),
                    L10n.T("早说了别往这边来。", "I said not to come this way."),
                    L10n.T("回头谁去跟家里说一声？", "Who goes and tells the family?")
                };
                default: return Silent;
            }
        }

        /// <summary>
        /// 【声音】断风游猎：占着三座回程中继平台收过路费的另一伙（档案里的 `RivalFaction`，
        /// 官方 AI 下见了拾荒者就先打起来）。说话短、居高临下，话题只有桥、风、过路钱。
        /// </summary>
        internal static string[] Ranger(SkyIslandChatterMoment moment)
        {
            switch (moment)
            {
                case SkyIslandChatterMoment.Idle: return new[]
                {
                    L10n.T("桥是我们的。风也是。", "The bridge is ours. So is the wind."),
                    L10n.T("过路的，先把东西放下。", "Passing through? Put something down first."),
                    L10n.T("那帮翻垃圾的又上来了。", "The scrap-pickers are back up here."),
                    L10n.T("站桥上才知道风往哪走。", "Stand on a bridge and you learn the wind."),
                    L10n.T("这条线，我画过很多次。", "I've drawn this line many times."),
                    L10n.T("别在我的桥上跑。", "Don't run on my bridge.")
                };
                case SkyIslandChatterMoment.Noticed: return new[]
                {
                    L10n.T("过路钱。要么退回去。", "Toll. Or turn around."),
                    L10n.T("线已经画好了。", "The line is already drawn."),
                    L10n.T("你走错桥了。", "Wrong bridge."),
                    L10n.T("站住。别让我数到二。", "Stop. Don't make me count to two.")
                };
                case SkyIslandChatterMoment.AllyDown: return new[]
                {
                    L10n.T("风把他带走了。下一个。", "The wind took him. Next."),
                    L10n.T("桥还得有人站。", "Someone still has to hold the bridge."),
                    L10n.T("我记住你了。", "I'll remember you.")
                };
                default: return Silent;
            }
        }

        // ====================================================================
        // 头目 / 岛主：一位一个池子
        // ====================================================================

        /// <summary>
        /// 头目 / 岛主的专属话语。每一位的三句都直接从 <see cref="SkyIslandBossRules"/> 那条档案上方的
        /// `// 【五栏】`（小环境 / 核心招式 / 克制 / 装备联动 / 串联）推出来——它说什么，就是它在这场仗里做什么。
        /// <paramref name="variant"/> 只对断风游猎有意义（1 追 / 2 伏 / 3 守，三位说的不是同一套话）。
        /// </summary>
        internal static string[] Boss(SkyIslandBossKind kind, int variant, SkyIslandChatterMoment moment)
        {
            switch (kind)
            {
                // 【声音】残星匠首：守着星穹工台的匠人头儿。满口炉子、桩子、料，把星灯的活看成自己的活。
                case SkyIslandBossKind.Foreman:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("炉子刚起来。别碰那几根桩。", "Furnace just came up. Keep off the pylons."),
                        L10n.T("星灯的料在我手里。你拿不走。", "I hold the stock for the star lamp. It's not yours.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("……炉子撑不住了。", "...The furnace can't hold."),
                        L10n.T("再立一根桩。再撑一会儿。", "One more pylon. Hold a little longer.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("灯……灯还没修好。", "The lamp... the lamp isn't fixed yet.")
                    };
                    return Silent;
                // 【声音】瞭台观星手：站在群岛最高处、习惯用眼睛量人的哨兵。话都跟「看」有关。
                case SkyIslandBossKind.Stargazer:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("从这儿，整片云海都在我眼里。", "I can see the whole cloud sea from up here."),
                        L10n.T("别动。我在量你。", "Hold still. I'm taking your measure.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("太近了……看不清了。", "Too close... I can't read you.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("镜片碎了。什么都看不见。", "The lens is broken. I see nothing.")
                    };
                    return Silent;
                // 【声音】悬根猎首：把整片悬根林当陷阱场的猎人。说的是线、洞、底下的路。
                case SkyIslandBossKind.RootHunter:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("线已经拉好了。你踩上来了。", "Tripline's set. You just stepped on it."),
                        L10n.T("这片林子底下都是我的路。", "Every path under this wood is mine.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("根还在。我从下面来。", "The roots hold. I'll come from below.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("根……松了。", "The roots... have come loose.")
                    };
                    return Silent;
                // 【声音】截信人：抢的是信，不是钱。倒下那句才露出它到底在找什么。
                case SkyIslandBossKind.Waylayer:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("包给我看看。就看一眼。", "Let me see the bag. Just a look."),
                        L10n.T("信都是我先读的。", "I read every letter first.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("拿去，拿去！别打了。", "Take it, take it! Stop.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("信……我只想要一封写给我的。", "The letters... I just wanted one addressed to me.")
                    };
                    return Silent;
                // 【声音】穗镰：把梯田当自己家的把式。开闸、喊人、护田，说话像在指挥农活。
                case SkyIslandBossKind.Sickle:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("闸在我手上。田也是。", "The sluice is mine. So are the fields."),
                        L10n.T("下田就得沾泥。", "Step in the field, you get muddy.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("开闸！谷仓那边的，过来！", "Open the gates! Barn crew, over here!")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("田……要淹了。", "The fields... will flood.")
                    };
                    return Silent;
                // 【声音】听雨人：洞里靠耳朵活着的人。整场只关心「响了几声」。
                case SkyIslandBossKind.Listener:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("嘘。你刚才开了几枪？", "Hush. How many shots was that?"),
                        L10n.T("洞会记住声音。", "The cave remembers sound.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("太吵了……石头听见了。", "Too loud... the stones heard it.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("终于……安静了。", "Finally... quiet.")
                    };
                    return Silent;
                // 【声音】蚋笛翁：只在夜里的池边出现，跟云蚋说话比跟人多。
                case SkyIslandBossKind.Piper:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("夜里池边只有我的调子。", "At night this pool hears only my tune."),
                        L10n.T("它们听我的。你也听听。", "They answer to me. Now you listen.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("别碰笛子——", "Not the flute—")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("曲子断了……它们散了。", "The tune breaks... they scatter.")
                    };
                    return Silent;
                // 【声音】镜中客：靠倒影活着。说的全是「哪个才是你」，倒下时反过来问自己。
                case SkyIslandBossKind.Mirror:
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("池面照得清楚。你看见几个自己？", "The water is clear. How many of you do you see?"),
                        L10n.T("你背后那个，也是你。", "That one behind you? Also you.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("影子……不听话了。", "The reflection... won't obey.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("我认不出……哪个是我。", "I can't tell... which one was me.")
                    };
                    return Silent;
                // 【声音】断风游猎（三位）：同一家族、三种守法。追的挑衅，伏的耐心，守的只说规矩。
                case SkyIslandBossKind.Windhunter: return Windhunter(variant, moment);
                default: return Silent;
            }
        }

        private static string[] Windhunter(int variant, SkyIslandChatterMoment moment)
        {
            if (variant <= 1)
            {
                // 追（K1）：先画线，再看你跑。
                if (moment == SkyIslandChatterMoment.Noticed) return new[]
                {
                    L10n.T("线画好了。跑给我看。", "Line's drawn. Let's see you run.")
                };
                if (moment == SkyIslandChatterMoment.Wounded) return new[]
                {
                    L10n.T("还没追够。", "Not done chasing.")
                };
                if (moment == SkyIslandChatterMoment.Down) return new[]
                {
                    L10n.T("风……从桥上过去了。", "The wind... went on over the bridge.")
                };
                return Silent;
            }
            if (variant == 2)
            {
                // 伏（K2）：等得起。
                if (moment == SkyIslandChatterMoment.Noticed) return new[]
                {
                    L10n.T("我等了很久。别急着过桥。", "I've waited a long while. Don't rush the crossing.")
                };
                if (moment == SkyIslandChatterMoment.Wounded) return new[]
                {
                    L10n.T("再等一步。就一步。", "One more step. Just one.")
                };
                if (moment == SkyIslandChatterMoment.Down) return new[]
                {
                    L10n.T("等错人了。", "Waited on the wrong one.")
                };
                return Silent;
            }
            // 守（K3）：只讲规矩。
            if (moment == SkyIslandChatterMoment.Noticed) return new[]
            {
                L10n.T("这段桥归我守。回去。", "This span is mine to hold. Turn back.")
            };
            if (moment == SkyIslandChatterMoment.Wounded) return new[]
            {
                L10n.T("桥还在我脚下。", "The bridge is still under my feet.")
            };
            if (moment == SkyIslandChatterMoment.Down) return new[]
            {
                L10n.T("桥……交给谁？", "The bridge... who holds it now?")
            };
            return Silent;
        }

        // ====================================================================
        // 具名剧情对手
        // ====================================================================

        /// <summary>
        /// 折翎的战斗体与失控的守钟装置。两者都不是「头目」档案里的人，走这条单独的表。
        /// 噬风不在这里：它是风灾留下的一股风，没有嘴——它的机制提示照旧只走
        /// `SkyIslandStormBoss` 的字幕通道。
        /// </summary>
        internal static string[] Champion(string id, SkyIslandChatterMoment moment)
        {
            switch (id)
            {
                // 【声音】折翎（战斗体）：他守的是旧航路。倒下那句和他留下的旧腰牌刻的是同一行字。
                case "zheling":
                    if (moment == SkyIslandChatterMoment.Noticed) return new[]
                    {
                        L10n.T("你选了打。那就打。", "You chose the fight. So fight.")
                    };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[]
                    {
                        L10n.T("我守了这么久，不是为了输。", "I didn't hold this long to lose.")
                    };
                    if (moment == SkyIslandChatterMoment.Down) return new[]
                    {
                        L10n.T("航路……交给你了。", "The route... is yours now.")
                    };
                    return Silent;
                // 【声音】失控的守钟装置：**一台机器**。只有钟声，没有一句话。
                case "bellkeeper":
                    if (moment == SkyIslandChatterMoment.Noticed) return new[] { L10n.T("铛——", "Clang—") };
                    if (moment == SkyIslandChatterMoment.Wounded) return new[] { L10n.T("铛、铛——", "Clang, clang—") };
                    if (moment == SkyIslandChatterMoment.Down) return new[] { L10n.T("……嗡。", "...a hum, fading.") };
                    return Silent;
                default: return Silent;
            }
        }

        /// <summary>这一档敌人用哪个共享库：断风游猎是另一阵营，其余全是云沿拾荒者。</summary>
        internal static string[] Mob(bool rivalFaction, SkyIslandChatterMoment moment)
        {
            return rivalFaction ? Ranger(moment) : Scav(moment);
        }

        /// <summary>某个池子里有没有话可说。空池子的说话者（噬风）整条路径早返。</summary>
        internal static bool HasLines(string[] pool)
        {
            return pool != null && pool.Length > 0;
        }
    }
}

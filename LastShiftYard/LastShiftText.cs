namespace BossRush
{
    /// <summary>末班编组站的玩家可见短文本（地名、出口名），取用时按当前语言解析。</summary>
    internal static class LastShiftText
    {
        internal static string ExitName(string id)
        {
            switch (id)
            {
                case "north": return L10n.T("北公路", "North road");
                case "west": return L10n.T("西线检修便门", "West line service gate");
                case "east": return L10n.T("东台押运门", "East platform escort gate");
                case "bell": return L10n.T("轨道车接客亭", "Railcar shelter");
                default: return id;
            }
        }

        /// <summary>出口没开时写明开启条件（第七轮 owner 采纳：锁着的出口说清楚怎么才开）。</summary>
        internal static string ExitRule(string id)
        {
            switch (id)
            {
                case "west": return L10n.T("西线上没有列车时才开", "Opens when no train is on the west line");
                case "east": return L10n.T("东台卸完一班货才开", "Opens after a train unloads at the east platform");
                case "bell": return L10n.T("先拉轨道车铃，等轨道车到", "Ring the railcar bell and wait for it");
                default: return "";
            }
        }

        /// <summary>站进关着的出口时看到的样子（owner 2026-11-11：不念规则，写现场）。</summary>
        internal static string ExitLockedLine(string id)
        {
            switch (id)
            {
                case "west": return L10n.T("便门挂着锁，西线还有车压着。", "The service gate is padlocked. There's still a train on the west line.");
                case "east": return L10n.T("押运门从里面闩着，东台那班还没卸完。", "The escort gate is barred from inside. The east platform hasn't finished unloading.");
                case "bell": return L10n.T("接客亭空着，铃绳垂在柱子边上。", "The shelter is empty. The bell rope hangs by the post.");
                default: return "";
            }
        }

        internal static string LineName(char line) { return line == 'W' ? L10n.T("西线", "west") : L10n.T("东线", "east"); }

        internal static string PlatformName(char line) { return line == 'W' ? L10n.T("西", "West") : L10n.T("东", "East"); }

        /// <summary>班单三种之一（原型 layout.goods.manifests）。</summary>
        internal static string ManifestName(string id)
        {
            switch (id)
            {
                case "medical": return L10n.T("医务", "Medical");
                case "repair": return L10n.T("抢修", "Repair");
                default: return L10n.T("精械", "Precision");
            }
        }

        internal static string EventLabel(LastShiftEvent e)
        {
            switch (e)
            {
                case LastShiftEvent.Slow: return L10n.T("慢车", "Slow train");
                case LastShiftEvent.Heavy: return L10n.T("重载货列", "Heavy freight");
                case LastShiftEvent.Express: return L10n.T("直通快车", "Express");
                default: return L10n.T("普通货列", "Freight");
            }
        }

        internal static string EventNote(LastShiftEvent e)
        {
            switch (e)
            {
                case LastShiftEvent.Slow: return L10n.T("停七十秒，押运会到齐。", "It stands for seventy seconds and the whole escort turns up.");
                case LastShiftEvent.Heavy: return L10n.T("多挂了一节车。", "One car longer than usual.");
                case LastShiftEvent.Express: return L10n.T("不停站，离股道远点！", "It's not stopping. Get clear of the track!");
                default: return "";
            }
        }

        // ---------------- 「没人来领的末班票」 ----------------
        /// <summary>三处线索的交互名（打孔钳 / 旧录音 / 退件簿）。</summary>
        internal static string NoteVerb(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("看打孔钳", "Look at the ticket punch");
                case 1: return L10n.T("听旧录音", "Play the old tape");
                default: return L10n.T("翻退件簿", "Leaf through the returns ledger");
            }
        }

        /// <summary>现场找到线索时的一句（原型 sim.mjs 的 descriptions）。</summary>
        internal static string NoteFinding(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("钳口缺了一齿，钳柄上刻着最后一班的日期。", "One tooth is missing from the jaw. The date of the last shift is scratched into the handle.");
                case 1: return L10n.T("磁带转了半天，出来司机的声音：「我的座让给她。」", "The tape hisses for a while, then the driver: \"She can have my seat.\"");
                default: return L10n.T("退件簿末页夹着一张末班票，只打了两个孔。", "Tucked into the back of the ledger is a last-shift ticket with only two holes punched.");
            }
        }

        internal static string NoteTitle(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("末班票·司机室的打孔钳", "Last Ticket: The Cab's Punch");
                case 1: return L10n.T("末班票·维修棚的旧录音", "Last Ticket: The Repair Shed Tape");
                default: return L10n.T("末班票·押运库的退件簿", "Last Ticket: The Bond Store Ledger");
            }
        }

        internal static string NoteBody(int index)
        {
            switch (index)
            {
                case 0:
                    return L10n.T("悬车楼司机室的操纵台上压着一把打孔钳。钳口缺了一齿，打出来的孔边都带一个小豁口。\n钳柄上刻着最后一班的日期，下面是一行小字：「末班我来开。」",
                        "A ticket punch lies under the console in the suspended cab. One tooth of its jaw is gone, so every hole it makes has a little notch.\nThe date of the last shift is scratched into the handle, and beneath it, smaller: \"I'll drive the last one.\"");
                case 1:
                    return L10n.T("维修棚的录音机里还卡着半盘磁带。按下去，先是很长的杂音，然后是司机的嗓音：\n「疏散车坐满了，台上还剩一个候车的。我的座让给她。下一班我自己开出去。」\n背景里，有人在喊「余响」。",
                        "Half a tape is still jammed in the recorder in the repair shed. Press play: a long hiss, then the driver's voice.\n\"The evacuation train's full and there's one left on the platform. She can have my seat. I'll drive the next one out myself.\"\nIn the background, someone is calling \"Yu Xiang.\"");
                default:
                    return L10n.T("押运库的退件簿翻到末页，夹着一张末班票。票面只打了两个孔，持票人一栏写的是那位司机。\n退件原因：「无人认领。末班车未发出。」",
                        "At the back of the bond store's returns ledger is a last-shift ticket with only two holes punched. The holder named on it is the driver.\nReason for return: \"Uncollected. The last train never left.\"");
            }
        }

        internal static string EndingTitle { get { return L10n.T("没人来领的末班票", "The Ticket Nobody Came For"); } }

        internal static string EndingBody
        {
            get
            {
                return L10n.T("余响接过票根，翻过来看了很久。她从桌下摸出那把缺齿的打孔钳，打完最后一孔。孔边多了一个小豁口，和她自己那张疏散车票上的一模一样。\n「他说下一班他自己开出去。」她把票根夹进验票桌的抽屉，「那我就一直剪票，剪到有人来领。」",
                    "Yu Xiang takes the stub and turns it over for a long time. From under the desk she brings out the punch with the missing tooth and makes the last hole. Its edge has a little notch, exactly like the one on her own evacuation ticket.\n\"He said he'd drive the next one out himself.\" She slips the stub into the desk drawer. \"Then I'll keep punching tickets until someone comes for it.\"");
            }
        }

        /// <summary>站台记录名（原型 sim.mjs 的 RECORDS）。</summary>
        internal static string RecordName(string id)
        {
            switch (id)
            {
                case "sign:medical": return L10n.T("签收医务封签货", "Signed for medical cargo");
                case "sign:repair": return L10n.T("签收抢修封签货", "Signed for repair cargo");
                case "sign:precision": return L10n.T("签收精械封签货", "Signed for precision cargo");
                case "shadow": return L10n.T("借车影换边", "Crossed in a train's shadow");
                case "express": return L10n.T("躲过直通快车", "Dodged the express");
                case "bossTrain": return L10n.T("击倒随车而来的押运长", "Downed the escort chief off the train");
                case "red": return L10n.T("带出一件红（Q5+）", "Took out a red (Q5+)");
                default: return L10n.T("拉响库房警铃后仍撤离", "Got out after tripping a store alarm");
            }
        }

        /// <summary>收尾页下方的一行：图鉴多了一页，以及往后剪票的好处（owner 2026-11-11：她替你压道岔）。</summary>
        internal static string EndingWarning
        {
            get
            {
                return L10n.T("笔记里多了一页。往后剪票，余响会先跟你说下一班是什么车，还会让调度把首班放到加价台。",
                    "A new page in your notes. From now on Yu Xiang tells you about the next train when she punches your ticket, and gets dispatch to send the first one to the premium platform.");
            }
        }

        /// <summary>M 图上还没找到的线索：只写东西本身。</summary>
        internal static string ClueMapName(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("打孔钳", "Ticket punch");
                case 1: return L10n.T("录音机", "Tape recorder");
                default: return L10n.T("退件簿", "Returns ledger");
            }
        }

        // ---------------- 余响 ----------------
        internal static string YuXiangName { get { return L10n.T("余响", "Yu Xiang"); } }
        internal static string YuXiangTalkVerb { get { return L10n.T("对话", "Talk"); } }

        /// <summary>
        /// 还没带出过线索时她说的：第一次见面只聊站和末班车，给一个去的理由（今晚还有货车），不报线索在哪；
        /// 去过编组站以后提一句司机，把人往司机室引，但不说要找什么。
        /// </summary>
        internal static string YuXiangIntro(bool firstTime, bool visited)
        {
            if (firstTime)
                return L10n.T("这站早停了。末班车那晚没开出去，我就一直在这儿剪票。\n可货车夜里还是照样进站，封签货堆在台上，没人来签。想去看看，票递过来。",
                    "This station closed years ago. The last train never left that night, and I've been punching tickets here ever since.\nFreight still pulls in at night, though. Sealed cargo piles up on the platform and nobody comes to sign for it. If you want a look, hand me your ticket.");
            if (visited)
                return L10n.T("那晚开车的是个老司机。他的东西，多半还在悬车楼上。", "The one driving that night was an old hand. His things are probably still up in the suspended cab.");
            return L10n.T("车照样进站。票递过来就行。", "The trains still come in. Just hand me your ticket.");
        }

        /// <summary>余响按进度说的话：老检票员，话短，只说站里的事。下标 = 第一条还没带出去的线索；3 = 三条都看过。</summary>
        internal static string YuXiangHint(int missing)
        {
            switch (missing)
            {
                case 0: return L10n.T("悬车楼司机室，操纵台底下压着把钳子。缺了个齿，一看就是他的。", "Up in the suspended cab there's a punch under the console. Missing a tooth. That's his.");
                case 1: return L10n.T("维修棚那台录音机，他老在那儿对调度口令。磁带要是还在，你听听。", "He used to rehearse dispatch calls on the recorder in the repair shed. If the tape's still there, have a listen.");
                case 2: return L10n.T("押运库有本退件簿，没人领的东西都记在上头。押运长守着那儿，当心。", "The bond store keeps a returns ledger, everything nobody came for. The chief escort guards that place. Careful.");
                default: return L10n.T("钳子、磁带、退件簿……那张票还夹在簿子里吧。带出来给我。", "The punch, the tape, the ledger... The ticket's still in that ledger, isn't it. Bring it out to me.");
            }
        }

        internal static string YuXiangAfter(int runs)
        {
            switch (runs % 3)
            {
                case 0: return L10n.T("票我收着呢。下一班的事，上车前我跟你说。", "I've got the ticket put away. I'll tell you about the next train before you board.");
                case 1: return L10n.T("他说下一班自己开。我等着就是了。", "He said he'd drive the next one himself. So I wait.");
                default: return L10n.T("别站在股道上。车头不认人。", "Stay off the track. The engine doesn't care who you are.");
            }
        }

        internal static string YuXiangSeesStub { get { return L10n.T("她盯着你手里的票看了一会儿，伸出手：「给我吧。」", "She looks at the ticket in your hand for a while, then holds out her own. \"Give it here.\""); } }
        internal static string YuXiangCallout { get { return L10n.T("末班车，剪票上车。", "Last train. Tickets, please."); } }

        internal static string BaseNotice
        {
            get
            {
                return L10n.T("发车处贴了一张告示：「末班车恢复剪票。仓库区西场，第二座龙门吊下，找余响。」",
                    "A notice is pinned up by the departures: \"Last-train ticketing resumed. Warehouse district, west yard, under the second gantry crane. Ask for Yu Xiang.\"");
            }
        }

        /// <summary>HUD 地名：标记名是 `POI_poi_&lt;id&gt;`（构建器按布局生成）。</summary>
        internal static string PlaceName(string marker)
        {
            string id = marker != null && marker.StartsWith("POI_poi_", System.StringComparison.Ordinal) ? marker.Substring(8) : marker;
            switch (id)
            {
                case "spawn": return L10n.T("南班房外", "Outside the south crew room");
                case "switchThroat": return L10n.T("双臂岔心", "The turnout");
                case "platformW": return L10n.T("西台棚", "West platform shed");
                case "platformE": return L10n.T("东台露天坪", "East open platform");
                case "watch": return L10n.T("南护栏", "South rail");
                case "signal": return L10n.T("悬车楼司机室", "Suspended cab");
                case "repair": return L10n.T("维修棚", "Repair shed");
                case "bond": return L10n.T("押运库", "Bond store");
                case "bossArena": return L10n.T("押运库前院", "Bond store yard");
                default: return LastShiftRaidScene.DisplayName;
            }
        }
    }
}

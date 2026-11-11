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
                case LastShiftEvent.Slow: return L10n.T("慢车：停靠 70 秒，押运队会到齐。", "Slow train: it stands 70 s and the escorts all turn up.");
                case LastShiftEvent.Heavy: return L10n.T("重载货列：多挂一节车厢，车影更长。", "Heavy freight: one more car, a longer shadow to hide behind.");
                case LastShiftEvent.Express: return L10n.T("直通快车：不停靠、不卸货，车头致命，远离轨道。", "Express: no stop, no unloading, and the engine is lethal. Keep off the track.");
                default: return L10n.T("车头正面撞击致命。", "A head-on hit from the engine is fatal.");
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
                case 0: return L10n.T("打孔钳上缺了一齿，钳口留着最后一班的日期。", "The ticket punch is missing a tooth, and the date of the last shift is still on its jaw.");
                case 1: return L10n.T("旧录音里，司机把自己的疏散座位让给了候车的人。", "On the old tape, the driver gives his evacuation seat to someone still waiting on the platform.");
                default: return L10n.T("退件簿上，那张没被领取的末班票还夹在末页。", "In the returns ledger, the uncollected last-shift ticket is still tucked into the back page.");
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
                return L10n.T("余响接过票根，翻过来看了很久。她从桌下摸出那把缺齿的打孔钳，打完最后一孔。孔边多了一个小豁口，和她自己那张疏散车票上的一模一样。\n「他说下一班他自己开出去。」她把票根夹进验票桌的抽屉，「那我就一直剪票，剪到有人来领。」\n从这以后，每次剪票，她都会把下一班的底细告诉你。",
                    "Yu Xiang takes the stub and turns it over for a long time. From under the desk she brings out the punch with the missing tooth and makes the last hole. Its edge has a little notch, exactly like the one on her own evacuation ticket.\n\"He said he'd drive the next one out himself.\" She slips the stub into the desk drawer. \"Then I'll keep punching tickets until someone comes for it.\"\nFrom now on, every time she punches your ticket, she tells you what the next train is carrying.");
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

        // ---------------- 余响 ----------------
        internal static string YuXiangName { get { return L10n.T("余响", "Yu Xiang"); } }
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

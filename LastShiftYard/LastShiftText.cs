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
                case "last": return L10n.T("末班车押运车厢尾梯", "Last train's brake-van step");
                default: return id;
            }
        }

        /// <summary>出口没开时写明开启条件（第七轮 owner 采纳：锁着的出口说清楚怎么才开）。封站后条件出口都关。</summary>
        internal static string ExitRule(string id)
        {
            switch (id)
            {
                case "west": return L10n.T("西线上没有列车时才开；封站后落锁", "Opens when no train is on the west line; locked once the yard closes");
                case "east": return L10n.T("东台卸完一班货才开；封站后落锁", "Opens after a train unloads at the east platform; locked once the yard closes");
                case "bell": return L10n.T("先拉轨道车铃，等轨道车到；封站后不来", "Ring the railcar bell and wait for it; it stops running once the yard closes");
                case "last": return L10n.T("末班车停靠和出站时才有", "Only while the last train stands and pulls out");
                default: return "";
            }
        }

        // ---------------- 快节奏改版（2026-11-13）：末班车、封站、签收、小队长 ----------------
        internal static string LastTrainLabel { get { return L10n.T("末班车", "Last train"); } }

        internal static string LastTrainNote
        {
            get { return L10n.T("押运车厢尾梯放下就能扒车走。它一出站，站场就封了。", "Once the brake van's step is down you can ride it out. When it leaves, the yard closes."); }
        }

        /// <summary>签完一柜（第 <paramref name="index"/> 柜，从 1 起）后的一句：只写现场，不念数字。</summary>
        internal static string SignedLine(int index, string topItem)
        {
            if (index <= 1)
                return L10n.T("签好了，满满一柜，最上面压着" + topItem + "。押运库那头亮着灯，有人在点货。",
                    "Signed. The cabinet's packed, with " + topItem + " on top. Over at the bond store the lights are on; someone's counting stock.");
            if (index == 2)
                return L10n.T("又签下一柜，最上面是" + topItem + "。押运车厢里有人吹了一声长哨，下一班来的人只会更多。",
                    "Another cabinet signed, " + topItem + " on top. Someone in the brake van blows a long whistle. The next train will bring more of them.");
            return L10n.T("第" + index + "柜，最上面是" + topItem + "。站场喇叭里有人在点你的名。",
                "Cabinet number " + index + ", " + topItem + " on top. Someone on the yard tannoy is calling out your description.");
        }

        /// <summary>签收柜换了一班：上一班没搬走的货被押运车收回去（玩家贴着柜子时不换）。</summary>
        internal static string CargoReclaimedLine
        {
            get { return L10n.T("押运员把上一班没搬走的货扛回了车上。", "The escorts haul the last train's unclaimed goods back aboard."); }
        }

        internal static string TrainStrikeLine
        {
            get { return L10n.T("车头把押运员撞飞了出去。", "The engine sends an escort flying."); }
        }

        /// <summary>站进关着的出口时看到的样子（owner 2026-11-11：不念规则，写现场）。封站后条件出口另有一句。</summary>
        internal static string ExitLockedLine(string id, bool yardClosed)
        {
            if (yardClosed && id != "north")
                return L10n.T("门上新缠了铁链，挂着「封站」的牌子。", "Fresh chain on the gate, and a sign: YARD CLOSED.");
            return ExitLockedLine(id);
        }

        internal static string ExitLockedLine(string id)
        {
            switch (id)
            {
                case "west": return L10n.T("便门挂着锁，西线还有车压着。", "The service gate is padlocked. There's still a train on the west line.");
                case "east": return L10n.T("押运门从里面闩着，东台那班还没卸完。", "The escort gate is barred from inside. The east platform hasn't finished unloading.");
                case "bell": return L10n.T("接客亭空着，铃绳垂在柱子边上。", "The shelter is empty. The bell rope hangs by the post.");
                case "last": return L10n.T("尾梯收起来了。", "The step has been pulled up.");
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
        internal static string[][] YuXiangIntro(bool firstTime, bool visited)
        {
            if (firstTime)
                return new[]
                {
                    new[] { "这站早停了。末班车那晚没开出去，我就一直在这儿剪票。",
                        "This station closed years ago. The last train never left that night, and I've been punching tickets here ever since." },
                    new[] { "可货车夜里还是照样进站，封签货堆在台上，没人来签。想去看看，票递过来。",
                        "Freight still pulls in at night, though. Sealed cargo piles up on the platform and nobody comes to sign for it. If you want a look, hand me your ticket." },
                };
            if (visited)
                return new[] { new[] { "那晚开车的是个老司机。他的东西，多半还在悬车楼上。",
                    "The one driving that night was an old hand. His things are probably still up in the suspended cab." } };
            return new[] { new[] { "车照样进站。票递过来就行。", "The trains still come in. Just hand me your ticket." } };
        }

        /// <summary>下标是第一条还没带出去的线索。三条都看过时催玩家带回票根。</summary>
        internal static string[] YuXiangHint(int missing)
        {
            switch (missing)
            {
                case 0: return new[] { "悬车楼司机室，操纵台底下压着把钳子。缺了个齿，一看就是他的。", "Up in the suspended cab there's a punch under the console. Missing a tooth. That's his." };
                case 1: return new[] { "维修棚那台录音机，他老在那儿对调度口令。磁带要是还在，你听听。", "He used to rehearse dispatch calls on the recorder in the repair shed. If the tape's still there, have a listen." };
                case 2: return new[] { "押运库有本退件簿，没人领的东西都记在上头。库门口有押运员守着，当心。", "The bond store keeps a returns ledger, everything nobody came for. There's an escort on the door. Careful." };
                default: return new[] { "钳子、磁带、退件簿……那张票还夹在簿子里吧。带出来给我。", "The punch, the tape, the ledger... The ticket's still in that ledger, isn't it. Bring it out to me." };
            }
        }

        /// <summary>磁带这句先埋下她口袋里那张疏散车票，收尾时对上。</summary>
        internal static string[] YuXiangReaction(int index)
        {
            switch (index)
            {
                case 0: return new[] { "那把钳子……缺齿是那年冬天磕的。他说这样剪出来的孔好认，我一摸就知道是他剪的。",
                    "That punch... the tooth broke off that winter. He said it made his holes easy to tell apart. I could tell his by touch." };
                case 1: return new[] { "（她按住上衣口袋，沉默良久。）……那晚台上就剩一个人。",
                    "(She presses a hand to her coat pocket and goes quiet.) ...There was only one person left on the platform that night." };
                default: return new[] { "退件簿？他的票还夹在里面……那就是说，他一直没来领。",
                    "The ledger? His ticket's still in there... So he never came to collect it." };
            }
        }

        internal static string[] YuXiangAfter(int runs)
        {
            switch (runs % 3)
            {
                case 0: return new[] { "票我收着呢。下一班的事，上车前我跟你说。", "I've got the ticket put away. I'll tell you about the next train before you board." };
                case 1: return new[] { "他说下一班自己开。我等着就是了。", "He said he'd drive the next one himself. So I wait." };
                default: return new[] { "别站在股道上。车头不认人。", "Stay off the track. The engine doesn't care who you are." };
            }
        }

        internal static readonly string[] YuXiangSeesStub =
        {
            "（她看着你手里的票，伸出手。）给我吧。",
            "(She looks at the ticket in your hand and holds out hers.) Give it here.",
        };

        internal static readonly string[][] YuXiangHandInChoices =
        {
            new[] { "把票根给她", "Give her the stub" },
            new[] { "先留着", "Not yet" },
        };

        internal static readonly string[][] YuXiangHandInFailure =
        {
            new[] { "（她摆了摆手。）下回再说。", "(She waves a hand.) Next time." },
            new[] { "票根还在你身上。", "You still have the stub." },
        };

        internal static readonly string[][] YuXiangBoarding =
        {
            new[] { "上末班车去编组站？注意安全，那里可不是什么和平的仓库区。",
                "Taking the last train to the yard? Watch yourself. It's nothing like the quiet old warehouse district out there." },
        };

        internal static readonly string[] YuXiangBoardingStub =
        {
            "（她盯着你手里那张票。）那张……给我看看。",
            "(She eyes the ticket in your hand.) That one... let me see it.",
        };

        internal static readonly string[] YuXiangBoardingStamp =
        {
            "腰上那枚钢印还在？押运库那只封着的柜子，它能开一回。",
            "Still got the stamp? It'll open that sealed cabinet in the bond store, once.",
        };

        internal static readonly string[] YuXiangBandage =
        {
            "等你上车，我给你拿一卷绷带。", "I'll slip you a roll of bandage when you board.",
        };

        internal static readonly string[][] YuXiangBoardingChoices =
        {
            new[] { "剪票上车", "Punch and board" },
            new[] { "再等等", "Not yet" },
        };

        internal static readonly string[][] YuXiangEnding =
        {
            new[] { "（余响把票根翻过来看了很久，从桌下摸出那把缺齿的打孔钳。）",
                "(Yu Xiang turns the stub over for a long time, then takes the punch with the missing tooth from under the desk.)" },
            new[] { "（她打完最后一孔。孔边的豁口和她那张疏散车票上的一模一样。）",
                "(She punches the last hole. It has the same little notch as the one on her own evacuation ticket.)" },
            new[] { "他说下一班他自己开出去。（她把票根夹进验票桌的抽屉。）",
                "He said he'd drive the next one out himself. (She slips the stub into the desk drawer.)" },
            new[] { "那我就一直剪票，剪到有人来领。",
                "Then I'll keep punching tickets until someone comes for it." },
            new[] { "往后剪票，我先告诉你下一班是什么车，再让调度把首班放到加价台。",
                "Next time you board, I'll tell you what's coming and have dispatch send the first train to the premium platform." },
            new[] { "笔记里多了一页。", "A new page has been added to your notes." },
        };

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

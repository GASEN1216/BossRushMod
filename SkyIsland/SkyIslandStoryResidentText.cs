namespace BossRush
{
    /// <summary>COMPAT：婚姻与地点只改变说话方式；进度仍读同一份故事，不增加存档状态。</summary>
    internal sealed partial class SkyIslandStoryService
    {
        private static string OtherResidentHomeLine(string id)
        {
            switch (id)
            {
                case "sky_fuzhou":
                    return L10n.T("回来了，先歇脚。修家伙、做东西，还是岛上码头那张工台；我在家，钟庭之争那桩事也在那儿交代。",
                        "Back. Sit, rest your feet. Repairs and making things are still done at the island dock workbench. While I'm home, the Bell Court Standoff gets settled there too.");
                case "sky_miantai":
                    return L10n.T("我在家。上岛要苔药、药臼，去悬根林那口臼，东西都放着。",
                        "I'm home. For moss remedy or the mortar on the island, it's the one under the roots in Hanging Root Wood. Everything's still there.");
                case "sky_zheling":
                    return L10n.T("这回守的是家。岛上没了的事，到了镜水寺再说；家里不动刀。",
                        "This time I'm holding the house. Whatever's left on the island can wait until the temple. No blades at home.");
                case "sky_bellkeeper":
                    return L10n.T("（他在木牌上写：人在家，钟还在岛上。要敲钟，回钟庭找我。）",
                        "(He writes on a slate: I'm home; the bell is still out on the island. To ring it, come to the Bell Court.)");
                default: return null;
            }
        }

        private static string QingheStoryLine(SkyIslandStoryData data, bool married, bool onIsland)
        {
            string greeting = !married ? string.Empty : onIsland
                ? L10n.T("和你一起回来看看菜畦，心里踏实。\n", "It feels good to visit the garden with you.\n")
                : L10n.T("回家啦，先歇歇。岛上的菜畦我也惦记着。\n", "You're home. Rest a little; I've been thinking about the island garden too.\n");
            if (data.Has(SkyIslandStoryFlag.PlantingDelivered))
                return greeting + (onIsland && !married
                    ? (data.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("归航的人都吃上热菜了。最后一畦留给下一船。",
                            "Everyone who came home got a hot meal. The last bed is for the next boat.")
                        : L10n.T("新风车转起来了。等下一船靠岸，我就下锅。",
                            "The new pinwheel is turning. I'll start cooking when the next boat docks."))
                    : L10n.T("记录到手了，菜畦又能开火了。归航菜就留在畦边，回岛的时候，每趟来吃一顿热的。",
                        "I've got the record back, so the beds can cook again. The homecoming meal waits by the garden; come and eat a hot one each trip."));
            if (data.Has(SkyIslandStoryFlag.PlantingRecord))
                return greeting + (onIsland
                    ? L10n.T("种植记录找到了，泥手印还在呢。交给我，或留在风铃集委托板上，菜畦就能重新开张了。",
                        "You found the planting record, muddy prints and all. Hand it to me or leave it at the Windchime Market board to reopen the garden.")
                    : L10n.T("种植记录你已经找到了。下趟回岛留在风铃集委托板上就行，菜畦能重新开张。也可以带上我，在岛上直接交给我。",
                        "You already found the planting record. Leave it at the Windchime Market board next trip and the garden reopens. Or bring me along and hand it to me on the island."));
            return greeting + L10n.T("我的种植记录落在蛙鸣池了。路过帮我找找，纸上有泥手印。",
                "I left my planting record at Frogsong Pool. Look for the muddy handprints.");
        }

        private string WeibaiStoryLine(SkyIslandStoryData data, bool married, bool onIsland)
        {
            string greeting = !married ? string.Empty : onIsland
                ? L10n.T("今天和你一起跑航路，家里的事回去再张罗。\n", "Today we're walking the lanes together. Home can wait until we get back.\n")
                : L10n.T("回来啦！咱们成了家，岛上的事也不能丢。\n", "You're back! We've made a home together, but the island still needs us.\n");
            if (!onIsland)
                greeting += L10n.T("航路上的活得回岛上接、岛上交。我要是留在家，去风铃集的板子上揭也行。\n",
                    "Lane work has to be taken and handed in on the island. If I'm home, go and pull it off the board at Windchime Market.\n");
            if (!data.Has(SkyIslandStoryFlag.BeaconQuestDelivered))
            {
                bool accepted = data.Has(SkyIslandStoryFlag.BeaconQuestAccepted);
                // 灯亮了但没接单：先说「接」再说「交」，不能先催交、再说「你还没接」（2026-09-25 F3 英文复拍读出的前后矛盾）。
                string progress = data.BothBeacons
                    ? (accepted
                        ? L10n.T("两盏灯都亮了！这单还没交呢，交完再去码头找浮舟接下一单。\n",
                            "Both lamps are lit! Turn in the beacon quest first, then Fuzhou has the next quest at the dock.\n")
                        : L10n.T("两盏灯都亮了！这活你还没接，先来我这儿接上，再来交。交了去码头，浮舟那边还有一桩。\n",
                            "Both lamps are lit! You haven't taken this job yet, so take it from me first, then hand it in. After that, Fuzhou has the next one at the dock.\n"))
                    : data.Has(SkyIslandStoryFlag.WindBeacon)
                        ? L10n.T("西边风标修好了，就差东边残星工坊那盏星灯。灯一亮，回来找我。\n",
                            "The west beacon's mended. Only the star lamp at Fallen Star Workshop, on the east side, is left. When it burns, come find me.\n")
                        : data.Has(SkyIslandStoryFlag.StarLamp)
                            ? L10n.T("东边的灯亮了，西边悬根林那支风标还卡着。修顺了，来我这儿报个到。\n",
                                "The east lamp is up. The west beacon in Hanging Root Wood is still jammed. Once it turns, check in with me.\n")
                            : L10n.T("西边悬根林那支风标，东边残星工坊那盏星灯，都得修。两头一亮，双航标门自己就开。\n",
                                "The west beacon in Hanging Root Wood and the east star lamp at Fallen Star Workshop both need fixing. Light both ends and the twin-beacon gate opens itself.\n");
                if (!accepted && !data.BothBeacons)
                    progress += L10n.T("这活你还没接呢，先找我接上。\n",
                        "You haven't taken this job yet. Come to me and take it first.\n");
                return greeting + progress + L10n.T("接活交活都找我，就在岛上。我不在的话，风铃集的委托板上也挂着，照样能接能交。\n",
                    "Take it from me and hand it in to me, right here on the island. If I'm away, the board at Windchime Market has it too, same deal.\n");
            }
            if (!data.Has(SkyIslandStoryFlag.BellCourtQuestAccepted))
                return greeting + L10n.T("航标这桩活了结了。去码头找浮舟，钟庭之争那一桩，他等着派给你。\n",
                    "The beacon job's done. Go find Fuzhou at the dock; he has The Bell Court Standoff to hand you.\n");
            if (data.Has(SkyIslandStoryFlag.Ending))
                return greeting + L10n.T("钟响时，两头的风铃也响了。岛上委托板还挂着，回去可以再揭一张。\n",
                    "The chimes at both ends rang with the bell. There's still work on the island contract board next trip.\n") + CurrentObjective + "\n";
            return greeting + CurrentObjective + "\n";
        }
    }
}

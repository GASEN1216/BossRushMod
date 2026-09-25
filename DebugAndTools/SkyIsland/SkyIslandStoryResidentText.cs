namespace BossRush
{
    /// <summary>COMPAT：婚姻与地点只改变说话方式；进度仍读同一份故事，不增加存档状态。</summary>
    internal sealed partial class SkyIslandStoryService
    {
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
                    : L10n.T("记录交好了，岛上菜畦也重新开张了。归航菜留在菜畦，回岛时每趟都能吃一顿。",
                        "The record is returned and the island garden is growing again. A homecoming meal awaits at the garden each trip."));
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
                greeting += L10n.T("航路任务得回岛上接、岛上交。我留在家里的时候，去风铃集委托板找「航路任务」就行。\n",
                    "Route quests are taken and turned in on the island. When I stay home, the Windchime Market board has them under Route quests.\n");
            if (!data.Has(SkyIslandStoryFlag.BeaconQuestDelivered))
            {
                bool accepted = data.Has(SkyIslandStoryFlag.BeaconQuestAccepted);
                // 灯亮了但没接单：先说「接」再说「交」，不能先催交、再说「你还没接」（2026-09-25 F3 英文复拍读出的前后矛盾）。
                string progress = data.BothBeacons
                    ? (accepted
                        ? L10n.T("两盏灯都亮了！这单还没交呢，交完再去码头找浮舟接下一单。\n",
                            "Both lamps are lit! Turn in the beacon quest first, then Fuzhou has the next quest at the dock.\n")
                        : L10n.T("两盏灯都亮了！这单你还没接，先在岛上的「航路任务」里接下再交。交完去码头找浮舟接下一单。\n",
                            "Both lamps are lit! You haven't taken the beacon quest yet, so accept it under Route quests and turn it in. Then Fuzhou has the next quest at the dock.\n"))
                    : data.Has(SkyIslandStoryFlag.WindBeacon)
                        ? L10n.T("西边风标修好了，还差东边残星工坊那盏星灯。点亮了回来跟我说一声。\n",
                            "The west beacon is repaired. The east star lamp at Fallen Star Workshop still needs work. Come tell me once it burns.\n")
                        : data.Has(SkyIslandStoryFlag.StarLamp)
                            ? L10n.T("东边星灯亮了，还差西边悬根林那支风标。修好了回来跟我说一声。\n",
                                "The east star lamp is lit. The west beacon in Hanging Root Wood still needs work. Come tell me once it is fixed.\n")
                            : L10n.T("西边悬根林那支风标，东边残星工坊那盏星灯，都得修。两头一亮，双航标门自己就开。\n",
                                "The west beacon in Hanging Root Wood and the east star lamp at Fallen Star Workshop both need fixing. Light both ends and the twin-beacon gate opens itself.\n");
                if (!accepted && !data.BothBeacons)
                    progress += L10n.T("这单你还没接呢，先在岛上的「航路任务」里接一下。\n",
                        "You haven't taken this one yet. Accept it under Route quests on the island first.\n");
                return greeting + progress + L10n.T("在岛上找我接、找我交。我不在的时候，风铃集委托板上也有「航路任务」。\n",
                    "Take it from me and turn it in to me, on the island. When I am away, the Windchime Market board has Route quests too.\n");
            }
            if (!data.Has(SkyIslandStoryFlag.BellCourtQuestAccepted))
                return greeting + L10n.T("航标这单交好了。去码头找浮舟，在「航路任务」里接下钟庭之争。\n",
                    "The beacon quest is turned in. Find Fuzhou at the island dock and take The Bell Court Standoff under Route quests.\n");
            if (data.Has(SkyIslandStoryFlag.Ending))
                return greeting + L10n.T("钟响时，两头的风铃也响了。岛上委托板还挂着，回去可以再揭一张。\n",
                    "The chimes at both ends rang with the bell. There's still work on the island contract board next trip.\n") + CurrentObjective + "\n";
            return greeting + CurrentObjective + "\n";
        }
    }
}

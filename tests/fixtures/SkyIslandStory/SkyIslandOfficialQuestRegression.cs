using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// 岛上主线的官方任务表（SkyIslandOfficialQuestTable）与剧情规则同源：
/// 「任务页挂不挂得出来」「目标行写什么」「完成按钮点了会不会被拒」都只认 SkyIslandStoryRules 那一份判据。
/// 逐字链接生产文件执行；不碰 Unity。
/// </summary>
internal static class SkyIslandOfficialQuestRegression
{
    private static readonly SkyIslandStoryFlag[] RelevantBits =
    {
        SkyIslandStoryFlag.RouteUnlocked, SkyIslandStoryFlag.WindBeacon, SkyIslandStoryFlag.StarLamp,
        SkyIslandStoryFlag.BellKeeperReconciled, SkyIslandStoryFlag.Ending,
        SkyIslandStoryFlag.BeaconQuestAccepted, SkyIslandStoryFlag.BeaconQuestDelivered,
        SkyIslandStoryFlag.BellCourtQuestAccepted, SkyIslandStoryFlag.BellCourtQuestDelivered,
        SkyIslandStoryFlag.HomecomingQuestAccepted, SkyIslandStoryFlag.HomecomingQuestDelivered
    };

    private static SkyIslandStoryData Data(int flags)
    {
        SkyIslandStoryData data = SkyIslandStoryRules.CreateDefault();
        data.flags = flags;
        if ((flags & (int)SkyIslandStoryFlag.RouteUnlocked) != 0)
            data.flags |= (int)(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered);
        return data;
    }

    private static SkyIslandOfficialQuestContext Context(SkyIslandStoryData data, bool onIsland, bool canWrite)
    {
        return new SkyIslandOfficialQuestContext { Data = data, StoryReady = data != null, CanWrite = canWrite, OnIsland = onIsland, Slot = 1 };
    }

    internal static void Run(Action<bool, string> check)
    {
        IList<SkyIslandOfficialQuestDefinition> island = SkyIslandOfficialQuestTable.Island;
        check(island.Count == 3 && island[0].QuestId == SkyIslandOfficialQuestTable.BeaconQuestId
            && island[1].QuestId == SkyIslandOfficialQuestTable.BellCourtQuestId && island[2].QuestId == SkyIslandOfficialQuestTable.HomecomingQuestId,
            "island quest table lists the three main-line quests in chain order");
        check(island[0].GiverId == SkyIslandOfficialQuestTable.WeibaiGiverId && island[1].GiverId == SkyIslandOfficialQuestTable.FuzhouGiverId
            && island[2].GiverId == SkyIslandOfficialQuestTable.BellKeeperGiverId, "island quests are given by Weibai, Fuzhou and the Bell Keeper");
        foreach (SkyIslandOfficialQuestDefinition def in island)
        {
            check(def.AcceptedFlag != 0 && def.DeliveredFlag != 0 && def.AcceptedFlag != def.DeliveredFlag, "each quest has distinct accepted and delivered flags: " + def.QuestId);
            check(def.Tasks != null && def.Tasks.Length > 0, "each quest has at least one task: " + def.QuestId);
            check(SkyIslandOfficialQuestTable.GiverIdOfResident(SkyIslandOfficialQuestTable.ResidentOfGiver(def.GiverId)) == def.GiverId,
                "giver <-> resident mapping round-trips: " + def.QuestId);
            check(SkyIslandOfficialQuestTable.FallbackMarkerOfGiver(def.GiverId) != null, "every giver has a device fallback marker: " + def.QuestId);
            check(!string.IsNullOrEmpty(def.Name()) && !string.IsNullOrEmpty(def.Description()), "quest name and description are non-empty: " + def.QuestId);
        }

        // 1) 穷举相关旗标：任务页挂不挂 ⟺ 规则允不允许接；完成按钮能不能交 ⟺ 规则允不允许交。
        int combos = 1 << RelevantBits.Length;
        int offers = 0, delivers = 0;
        for (int mask = 0; mask < combos; mask++)
        {
            int flags = 0;
            for (int bit = 0; bit < RelevantBits.Length; bit++) if ((mask & (1 << bit)) != 0) flags |= (int)RelevantBits[bit];
            SkyIslandStoryData data = Data(flags);
            foreach (SkyIslandOfficialQuestDefinition def in island)
            {
                string blocker;
                bool ruleAccept = SkyIslandStoryRules.CanApply(data, def.AcceptAction, out blocker);
                bool offer = SkyIslandOfficialQuestTable.CanOffer(def, Context(data, true, true));
                // 表的门比规则严：规则只说「还没接过」，表还要求航线已开、前一条已交付。表为真时规则必为真。
                if (offer) { offers++; check(ruleAccept, "offered quest must be acceptable by the rules: " + def.QuestId + " flags=" + flags); }
                bool ruleDeliver = SkyIslandStoryRules.CanApply(data, def.DeliverAction, out blocker);
                bool deliver = SkyIslandOfficialQuestTable.CanDeliver(def, Context(data, true, true));
                bool atGiverStage = def.Gate == null || def.Gate(Context(data, true, true));
                check(deliver == (ruleDeliver && atGiverStage), "delivery requires both the story rule and the giver gate: " + def.QuestId + " flags=" + flags);
                check(!SkyIslandOfficialQuestTable.CanDeliver(def, Context(data, false, true)), "island quests cannot be delivered away from the island: " + def.QuestId);
                check(!SkyIslandOfficialQuestTable.CanDeliver(def, Context(data, true, false)), "read-only story cannot deliver: " + def.QuestId);
                if (deliver) delivers++;
                // 2) Task 判据 = Describe 的前置：目标没完成时交付动作必给出「还差什么」。
                bool tasksDone = SkyIslandOfficialQuestTable.TasksDone(def, data);
                if (data.Has(def.AcceptedFlag) && !data.Has(def.DeliveredFlag))
                    check(tasksDone == (blocker == null), "task completion mirrors the delivery blocker: " + def.QuestId + " flags=" + flags);
                // 4) 链条：航线没开一律不挂；前一条没交付不挂。
                if (!data.SkyIslandRouteUnlocked) check(!offer, "no island quest is offered before the route is unlocked: " + def.QuestId);
                check(!SkyIslandOfficialQuestTable.CanOffer(def, Context(data, false, true)), "island quests are never offered off the island: " + def.QuestId);
                check(!SkyIslandOfficialQuestTable.CanOffer(def, Context(data, true, false)), "island quests are never offered while the save is read-only: " + def.QuestId);
            }
            SkyIslandStoryData route = Data(flags | (int)SkyIslandStoryFlag.RouteUnlocked);
            if (!route.Has(SkyIslandStoryFlag.BeaconQuestDelivered))
                check(!SkyIslandOfficialQuestTable.CanOffer(island[1], Context(route, true, true)), "the Bell Court quest waits for the beacon quest to be delivered");
            if (!route.BothBeacons || !route.BellKeeperResolved)
                check(!SkyIslandOfficialQuestTable.CanOffer(island[2], Context(route, true, true)), "the Homecoming quest waits for safe beacons and a resolved Bell Keeper");
        }
        check(offers > 0 && delivers > 0, "the exhaustive sweep exercised both offer and deliver paths");

        // 3) 文案同源：钟庭之争未完成时的目标行就是面板上的「还差什么」；两端航标的目标行也取自绘面板同一句。
        SkyIslandStoryData lit = Data((int)(SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp
            | SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered | SkyIslandStoryFlag.BellCourtQuestAccepted));
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            string blocker;
            SkyIslandStoryRules.CanApply(lit, SkyIslandStoryAction.ReconcileBellKeeper, out blocker);
            string line = island[1].Tasks[0].Description(lit);
            check(line.Contains(chinese ? "守钟装置" : "bell engine"), "Bell Court objective always keeps the combat alternative visible");
            check(!string.IsNullOrEmpty(blocker) && island[1].Tasks[0].ExtraHint(lit) == blocker,
                "Bell Court optional evidence hint shares the panel blocker (" + (chinese ? "zh" : "en") + ")");
            SkyIslandStoryData bare = Data((int)(SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.BeaconQuestAccepted));
            SkyIslandStoryRules.CanApply(bare, SkyIslandStoryAction.RepairWindBeacon, out blocker);
            check(island[0].Tasks[0].Description(bare) == blocker, "wind beacon task line equals the panel blocker (" + (chinese ? "zh" : "en") + ")");
            check(island[0].Tasks[0].Description(lit) != blocker && island[0].Tasks[0].Description(lit).Length > 0, "a finished task reads as done, not as a blocker");
        }
        L10n.IsChinese = true;

        // 5) Codec 往返 + 矛盾态被拒。
        foreach (SkyIslandStoryFlag[] pair in SkyIslandStoryRules.IslandQuestFlags)
        {
            SkyIslandStoryData accepted = Data((int)(SkyIslandStoryFlag.RouteUnlocked | pair[0]));
            SkyIslandStoryData back = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(accepted));
            check(back != null && back.flags == accepted.flags, "accepted quest flag survives a codec round trip: " + pair[0]);
            check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(Data((int)pair[0]))) == null, "quest flags without an unlocked route are rejected: " + pair[0]);
            check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(Data((int)(SkyIslandStoryFlag.RouteUnlocked | pair[1])))) == null,
                "delivered without accepted is rejected: " + pair[1]);
        }
        check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(Data((int)(SkyIslandStoryFlag.RouteUnlocked
            | SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered)))) == null, "beacon quest delivered without both beacons is rejected");
        SkyIslandStoryData complete = Data((int)(SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp
            | SkyIslandStoryFlag.BellKeeperReconciled | SkyIslandStoryFlag.Ending
            | SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered
            | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.BellCourtQuestDelivered
            | SkyIslandStoryFlag.HomecomingQuestAccepted | SkyIslandStoryFlag.HomecomingQuestDelivered));
        SkyIslandStoryData completeBack = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(complete));
        check(completeBack != null && completeBack.flags == complete.flags, "a fully delivered main line round-trips");

        // 6) 老档回填：已敲钟的槽一次补齐六位，且结果自洽；空槽与未解锁航线的槽不动。
        SkyIslandStoryData legacy = Data((int)(SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp
            | SkyIslandStoryFlag.BellKeeperDefeated | SkyIslandStoryFlag.Ending));
        SkyIslandStoryData filled;
        check(SkyIslandStoryRules.TryBackfillIslandQuests(legacy, out filled) && filled != null
            && filled.Has(SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered
                | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.BellCourtQuestDelivered
                | SkyIslandStoryFlag.HomecomingQuestAccepted | SkyIslandStoryFlag.HomecomingQuestDelivered),
            "a legacy slot that already rang the bell is backfilled to all three delivered quests");
        check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(filled)) != null, "the backfilled slot decodes");
        check(!SkyIslandStoryRules.TryBackfillIslandQuests(filled, out filled), "backfill is idempotent");
        check(!SkyIslandStoryRules.TryBackfillIslandQuests(SkyIslandStoryRules.CreateDefault(), out filled), "an empty slot is not backfilled");
        check(!SkyIslandStoryRules.TryBackfillIslandQuests(Data((int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp)), out filled),
            "a slot without the route is not backfilled");
        SkyIslandStoryData partial = Data((int)(SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp));
        check(SkyIslandStoryRules.TryBackfillIslandQuests(partial, out filled) && filled.Has(SkyIslandStoryFlag.BeaconQuestDelivered)
            && !filled.Has(SkyIslandStoryFlag.BellCourtQuestAccepted), "backfill only grants what the facts support");

        VerifyJourneyAndMigration(check, island);

        // 7) 所有枚举位的并集 == KnownFlags：再加位忘了同步掩码，整份存档会被拒。
        int union = 0;
        foreach (object value in Enum.GetValues(typeof(SkyIslandStoryFlag))) union |= (int)value;
        check(union == SkyIslandStoryRules.KnownFlags, "KnownFlags equals the union of every SkyIslandStoryFlag bit");
    }
    private static void VerifyJourneyAndMigration(Action<bool, string> check, IList<SkyIslandOfficialQuestDefinition> island)
    {
        // 正常新档：实际执行序章、接任务、做目标、复命，HUD 不得在复命前催玩家离开。
        SkyIslandStoryData data = SkyIslandStoryRules.CreateDefault();
        string message;
        SkyIslandStoryData next;
        foreach (SkyIslandStoryAction action in new[] { SkyIslandStoryAction.AcceptPrelude,
            SkyIslandStoryAction.RecoverPreludeInstrument, SkyIslandStoryAction.UnlockRoute })
        {
            check(SkyIslandStoryRules.TryApply(data, action, out next, out message), "new journey accepts " + action);
            data = next;
        }
        check(data.islandQuestMigrationComplete, "normal route unlock is never eligible for legacy quest completion");
        check(!SkyIslandStoryRules.TryBackfillIslandQuests(data, out next), "new journey cannot be backfilled");
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            SkyIslandStoryData journey = data.Copy();
            for (int i = 0; i < island.Count; i++)
            {
                SkyIslandOfficialQuestDefinition quest = island[i];
                string objective = SkyIslandStoryRules.Objective(journey);
                check(objective.Contains(quest.Contact()) && objective.Contains(quest.Name()), "HUD names the next giver and quest");
                check(SkyIslandStoryRules.TryApply(journey, quest.AcceptAction, out next, out message), "journey accepts " + quest.QuestId);
                journey = next;
                check(SkyIslandOfficialQuestTable.NextContactObjective(journey) == null, "unfinished task keeps the exploration objective");
                if (i == 0)
                {
                    journey.clearedEncounters = new[] { "D", "D_02", "G", "G_02" };
                    foreach (SkyIslandStoryAction repair in new[] { SkyIslandStoryAction.RepairStarLamp, SkyIslandStoryAction.RepairWindBeacon })
                    {
                        check(SkyIslandStoryRules.TryApply(journey, repair, out next, out message), "either beacon order remains playable");
                        journey = next;
                    }
                }
                else
                {
                    SkyIslandStoryAction outcome = i == 1 ? SkyIslandStoryAction.BellKeeperDefeated : SkyIslandStoryAction.RingHomecomingBell;
                    check(SkyIslandStoryRules.TryApply(journey, outcome, out next, out message), "journey resolves " + outcome);
                    journey = next;
                }
                SkyIslandStoryData reloaded = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(journey));
                check(reloaded != null && !SkyIslandStoryRules.TryBackfillIslandQuests(reloaded, out next), "completed objective survives reload without automatic delivery");
                check(!reloaded.Has(quest.DeliveredFlag), "player still owns the hand-in choice after reload");
                objective = SkyIslandStoryRules.Objective(reloaded);
                if (i == 1)
                {
                    check(objective.Contains(island[2].Contact()), "HUD keeps the player at the Bell Court to ring the bell before the return journey");
                    check(SkyIslandOfficialQuestTable.CanOffer(island[2], Context(reloaded, true, true)), "Homecoming is available before reporting to Fuzhou");
                    SkyIslandStoryData direct = reloaded.Copy();
                    foreach (SkyIslandStoryAction local in new[] { SkyIslandStoryAction.AcceptHomecomingQuest,
                        SkyIslandStoryAction.RingHomecomingBell, SkyIslandStoryAction.DeliverHomecomingQuest })
                    {
                        check(SkyIslandStoryRules.TryApply(direct, local, out next, out message), "finish locally without a dock detour: " + local);
                        direct = next;
                    }
                    check(!direct.Has(SkyIslandStoryFlag.BellCourtQuestDelivered) && SkyIslandStoryRules.Objective(direct).Contains(quest.Contact()),
                        "after ringing and reporting to the keeper, the HUD guides the natural return to Fuzhou");
                    check(SkyIslandStoryRules.TryApply(direct, quest.DeliverAction, out next, out message) &&
                        !message.Contains(chinese ? "等钟声" : "wait for the bell"), "Fuzhou acknowledges the bell that already rang");
                }
                else check(objective.Contains(quest.Contact()) && objective.Contains(chinese ? "交付" : "complete"), "HUD directs completed objectives back to their giver");
                check(SkyIslandOfficialQuestTable.CanDeliver(quest, Context(reloaded, true, true)), "completed objective can be handed in on the island");
                check(SkyIslandStoryRules.TryApply(reloaded, quest.DeliverAction, out next, out message), "journey hands in " + quest.QuestId);
                journey = next;
            }
            check(SkyIslandOfficialQuestTable.NextContactObjective(journey) == null, "fully delivered story returns to free exploration");
        }
        L10n.IsChinese = true;

        // 上一版已经接任务的档（无迁移字段）只能盖迁移标记，不能把目标完成冒充复命。
        foreach (SkyIslandOfficialQuestDefinition quest in island)
        {
            SkyIslandStoryData pending = Data((int)(SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.WindBeacon |
                SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.BellKeeperDefeated | SkyIslandStoryFlag.Ending | quest.AcceptedFlag));
            string oldJson = SkyIslandStoryCodec.Encode(pending).Replace(",\"islandQuestMigrationComplete\":false", "");
            SkyIslandStoryData old = SkyIslandStoryCodec.Decode(oldJson);
            check(old != null && !old.islandQuestMigrationComplete, "missing migration field defaults to false");
            check(SkyIslandStoryRules.TryBackfillIslandQuests(old, out next) && next.flags == old.flags && next.islandQuestMigrationComplete,
                "legacy accepted quest retains all exact flags: " + quest.QuestId);
            check(old.flags == pending.flags && !old.islandQuestMigrationComplete, "migration does not mutate the source");
        }
        SkyIslandStoryData emptyRoute = Data((int)SkyIslandStoryFlag.RouteUnlocked);
        check(SkyIslandStoryRules.TryBackfillIslandQuests(emptyRoute, out next) && next.flags == emptyRoute.flags,
            "legacy route with no finished objectives is stamped once");
        next.flags |= (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        SkyIslandStoryData later = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(next));
        check(!SkyIslandStoryRules.TryBackfillIslandQuests(later, out next) && !later.Has(SkyIslandStoryFlag.BeaconQuestDelivered),
            "objectives completed on a later raid are never legacy-backfilled");
        check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(data).Replace("\"islandQuestMigrationComplete\":true", "\"islandQuestMigrationComplete\":1")) == null,
            "present migration metadata must be boolean");

        // 实际服务的落盘 / 重开：不只测 pure rule。
        Saves.SavesSystem.Switch(100171);
        var service = new SkyIslandStoryService();
        service.Open();
        foreach (SkyIslandStoryAction action in new[] { SkyIslandStoryAction.AcceptPrelude, SkyIslandStoryAction.RecoverPreludeInstrument,
            SkyIslandStoryAction.UnlockRoute, SkyIslandStoryAction.AcceptBeaconQuest })
            check(service.TryApply(action, out message), "service accepts " + action);
        foreach (string id in new[] { "D", "D_02", "G", "G_02" }) service.RecordEncounterCleared(id);
        check(service.TryApply(SkyIslandStoryAction.RepairWindBeacon, out message) && service.TryApply(SkyIslandStoryAction.RepairStarLamp, out message), "service completes both beacon objectives");
        check(service.TryClose(), "service saves before re-entry");
        service = new SkyIslandStoryService(); service.Open();
        bool migrated;
        check(service.EnsureRouteCompatibility(out migrated, out message) && !migrated && service.Current.BothBeacons &&
            !service.Current.Has(SkyIslandStoryFlag.BeaconQuestDelivered), "real service re-entry keeps a completed quest pending delivery");
        check(service.TryClose(), "service closes after re-entry");
    }

}

using System;
using System.IO;
using BossRush;
using Saves;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("FAIL " + description);
    }
    private static void Apply(SkyIslandStoryService story, SkyIslandStoryAction action)
    {
        string message;
        Check(story.TryApply(action, out message), "accept " + action + ": " + message);
    }
    private static void Reject(SkyIslandStoryService story, SkyIslandStoryAction action)
    {
        int before = story.Current.flags;
        string message;
        Check(!story.TryApply(action, out message), "reject " + action);
        Check(before == story.Current.flags, "rejection preserves state " + action);
    }
    private static SkyIslandStoryService Open(int slot)
    {
        SavesSystem.Switch(slot);
        var story = new SkyIslandStoryService(); story.Open(); return story;
    }
    private static void Main()
    {
        var story = Open(1);
        story.Open(); Check(SavesSystem.Subscribers == 1, "Open subscription idempotent");
        Reject(story, SkyIslandStoryAction.RepairWindBeacon);
        Reject(story, SkyIslandStoryAction.RingHomecomingBell);
        Reject(story, SkyIslandStoryAction.ReconcileZheling);
        Apply(story, SkyIslandStoryAction.FindOldLetter);
        Reject(story, SkyIslandStoryAction.ReconcileZheling);
        Apply(story, SkyIslandStoryAction.FindRouteChart);
        Apply(story, SkyIslandStoryAction.ReconcileZheling);
        Reject(story, SkyIslandStoryAction.ZhelingDefeated);
        Apply(story, SkyIslandStoryAction.FindPlantingRecord);
        Apply(story, SkyIslandStoryAction.DeliverPlantingRecord);
        Reject(story, SkyIslandStoryAction.DeliverPlantingRecord);
        Check(story.RecordEncounterCleared("G"), "east first accepted");
        Reject(story, SkyIslandStoryAction.RepairStarLamp);
        Check(story.RecordEncounterCleared("G_02"), "east second group accepted");
        Apply(story, SkyIslandStoryAction.RepairStarLamp);
        Apply(story, SkyIslandStoryAction.OpenShortcutK2);
        Reject(story, SkyIslandStoryAction.OpenShortcutK3);
        Check(story.RecordEncounterCleared("D"), "west cleared");
        Reject(story, SkyIslandStoryAction.RepairWindBeacon);
        Check(story.RecordEncounterCleared("D_02"), "west second group accepted");
        Apply(story, SkyIslandStoryAction.RepairWindBeacon);
        Apply(story, SkyIslandStoryAction.OpenShortcutK1);
        Apply(story, SkyIslandStoryAction.OpenShortcutK3);
        Reject(story, SkyIslandStoryAction.ReconcileBellKeeper);
        Apply(story, SkyIslandStoryAction.RepairTelescope);
        Apply(story, SkyIslandStoryAction.ReconcileBellKeeper);
        Apply(story, SkyIslandStoryAction.RingHomecomingBell);
        Reject(story, SkyIslandStoryAction.RingHomecomingBell);
        Reject(story, SkyIslandStoryAction.BellKeeperDefeated);
        Check(story.RecordRegionVisited("S4") && story.HasVisitedRegion("S4"), "side island exploration recorded");
        Check(!story.HasVisitedRegion("H"), "unvisited region stays hidden");
        Check(!story.RecordRegionVisited("bogus"), "unknown region rejected");
        int writes = SavesSystem.PhysicalWrites;
        LevelManager.Instance.IsBaseLevel = false;
        story.Tick(false); Check(SavesSystem.PhysicalWrites == writes, "combat on independent raid cannot write disk");
        SavesSystem.FailPhysical = true;
        story.Tick(true); Check(SavesSystem.PhysicalWrites == writes, "physical failure retained");
        // 官方 SaveFile 没有 try/finally，异常之后 saving 会卡在 true；恢复要靠官方自己再走完一次保存。
        Check(SavesSystem.IsSaving, "official save flag stays stuck after a physical write exception");
        SavesSystem.FailPhysical = false;
        SavesSystem.ClearStuckSaving();
        story.Tick(true); Check(SavesSystem.PhysicalWrites == writes + 1, "retry physical save after typed pending consumed");
        int completedFlags = story.Current.flags;
        story.Close(); story.Close(); Check(SavesSystem.Subscribers == 0, "Close unsubscribes idempotently");
        story = Open(1);
        Check(story.Current.flags == completedFlags && story.HasVisitedRegion("S4"), "reentry restores ending and exploration");
        Reject(story, SkyIslandStoryAction.RingHomecomingBell);
        story.Close();

        story = Open(6);
        Apply(story, SkyIslandStoryAction.FindOldLetter);
        SavesSystem.IsSaving = true;
        Check(!story.TryClose(), "busy official save retains service for recovery");
        Check(SavesSystem.Subscribers == 1, "deferred close retains subscription");
        SavesSystem.IsSaving = false;
        Check(story.TryClose(), "close recovery finishes when official save completes");
        Check(SavesSystem.Subscribers == 0, "successful recovery unsubscribes");
        story = Open(6);
        Check(story.Current.Has(SkyIslandStoryFlag.OldLetter), "deferred close did not lose accepted progress");
        story.Close();

        // 战斗路线不要求支线；中途退出再入仍能继续。
        story = Open(2);
        story.RecordEncounterCleared("D"); story.RecordEncounterCleared("D_02"); Apply(story, SkyIslandStoryAction.RepairWindBeacon);
        story.Close(); story = Open(2);
        Check(story.Current.Has(SkyIslandStoryFlag.WindBeacon) && !story.Current.BothBeacons, "interrupted journey resumes one beacon");
        story.RecordEncounterCleared("G"); story.RecordEncounterCleared("G_02"); Apply(story, SkyIslandStoryAction.RepairStarLamp);
        Apply(story, SkyIslandStoryAction.ZhelingDefeated);
        Apply(story, SkyIslandStoryAction.BellKeeperDefeated);
        Apply(story, SkyIslandStoryAction.RingHomecomingBell);
        Apply(story, SkyIslandStoryAction.FindPlantingRecord);
        Apply(story, SkyIslandStoryAction.DeliverPlantingRecord);
        Reject(story, SkyIslandStoryAction.ReconcileZheling);
        story.Tick(true);
        SavesSystem.Switch(3);
        Check(!story.IsCurrentSlot, "live slot switch invalidates old session");
        Reject(story, SkyIslandStoryAction.FindOldLetter);
        story.Close();
        Check(!SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey), "old slot never writes new slot");

        story = Open(4);
        Apply(story, SkyIslandStoryAction.FindOldLetter);
        SavesSystem.DeleteCurrent();
        Check(!story.IsCurrentSlot, "same slot deletion invalidates session");
        story.Close(); story = Open(4);
        Check(story.Current.flags == 0, "deleted slot starts clean"); story.Close();

        story = Open(5); story.Close();
        string future = "{\"schemaVersion\":99,\"flags\":1}";
        SavesSystem.Save(SkyIslandStoryRules.StorageKey, future);
        story = Open(5); Reject(story, SkyIslandStoryAction.FindOldLetter); story.Close();
        Check(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey) == future, "future schema preserved byte for byte");
        string broken = "{\"schemaVersion\":1,\"flags\":0,\"visitedRegions\":0,\"clearedEncounters\":42,\"discoveredNotes\":[]}";
        SavesSystem.Save(SkyIslandStoryRules.StorageKey, broken);
        story = Open(5); Reject(story, SkyIslandStoryAction.FindOldLetter); story.Close();
        Check(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey) == broken, "malformed payload protected");

        var data = SkyIslandStoryRules.CreateDefault();
        data.discoveredNotes = new[] { "中文\"见闻\\路径\n下一行" };
        string raw = SkyIslandStoryCodec.Encode(data);
        Check(SkyIslandStoryCodec.Decode(raw).discoveredNotes[0] == data.discoveredNotes[0], "shared writer round trips escaped narrative");
        data.flags = (int)(SkyIslandStoryFlag.ZhelingReconciled | SkyIslandStoryFlag.ZhelingDefeated);
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(data)) == null, "conflicting resolution fail closed");
        data.flags = (int)SkyIslandStoryFlag.Ending;
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(data)) == null, "invalid ending fail closed");
        Check(SavesSystem.Subscribers == 0, "all sessions released events");
        SkyIslandStoryData stormData = SkyIslandStoryRules.CreateDefault();
        SkyIslandStoryData stormCandidate; string stormMessage;
        Check(!SkyIslandStoryRules.TryApply(stormData, SkyIslandStoryAction.StormSlain, out stormCandidate, out stormMessage),
            "storm cannot be slain before both beacons");
        stormData.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        Check(SkyIslandStoryRules.TryApply(stormData, SkyIslandStoryAction.StormSlain, out stormCandidate, out stormMessage),
            "storm becomes available once both beacons are lit");
        Check((stormCandidate.flags & (int)SkyIslandStoryFlag.StormSlain) != 0, "storm flag recorded");
        Check(!SkyIslandStoryRules.TryApply(stormCandidate, SkyIslandStoryAction.StormSlain, out _, out stormMessage),
            "storm cannot be slain twice");
        // 第五条说服路线：没有四件物证，但风已经散了。
        SkyIslandStoryData persuade = SkyIslandStoryRules.CreateDefault();
        persuade.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        Check(!SkyIslandStoryRules.TryApply(persuade, SkyIslandStoryAction.ReconcileBellKeeper, out _, out stormMessage),
            "bell keeper still refuses without evidence");
        persuade.flags |= (int)SkyIslandStoryFlag.StormSlain;
        Check(SkyIslandStoryRules.TryApply(persuade, SkyIslandStoryAction.ReconcileBellKeeper, out _, out stormMessage),
            "slaying the storm persuades the bell keeper");
        SkyIslandStoryData impossible = SkyIslandStoryRules.CreateDefault();
        impossible.flags = (int)SkyIslandStoryFlag.StormSlain;
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(impossible)) == null,
            "storm without beacons is rejected as corrupt");
        SkyIslandStoryData legit = SkyIslandStoryRules.CreateDefault();
        legit.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.StormSlain);
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(legit)) != null, "legit storm state round trips");
        string world = File.ReadAllText(SkyIslandContent.RelativePath);
        SkyIslandContentData content; string contentError;
        Check(SkyIslandContent.TryParse(world, out content, out contentError) && content.Source == "Json", "formal world table parsed");
        Check(content.Encounters.Length == 16 && content.Gates.Length == 5, "complete encounter and gate set");
        SkyIslandEncounterDefinition storm = Array.Find(content.Encounters, e => e.Id == "Storm");
        Check(storm != null && storm.Manual && storm.Count == 3 && storm.Marker == "POI_E", "storm boss encounter present");
        Check(storm.TierFor(0) == SkyIslandEnemyTier.Storm && storm.TierFor(1) == SkyIslandEnemyTier.Elite,
            "storm group is boss plus elite escorts");
        SkyIslandEncounterDefinition beacon = Array.Find(content.Encounters, e => e.Id == "D");
        Check(beacon.TierFor(0) == SkyIslandEnemyTier.Elite && beacon.TierFor(1) == SkyIslandEnemyTier.Scav,
            "beacon guard group is led by an elite");
        Check(!SkyIslandContent.TryParse(world.Replace("\"lead\": \"Storm\"", "\"lead\": \"Scav\""),
            out content, out contentError), "tier downgrade rejected");
        Check(!SkyIslandContent.TryParse(world.Replace("\"tier\": \"Champion\"", "\"tier\": \"Nonsense\""),
            out content, out contentError), "unknown tier name rejected");
        SkyIslandContent.TryParse(world, out content, out contentError);
        Check(!content.IsGateOpen("BellCourt", SkyIslandStoryRules.CreateDefault()), "bell gate closed at new game");
        data.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        Check(content.IsGateOpen("BellCourt", data), "bell gate consumes both beacons");
        data.flags = (int)SkyIslandStoryFlag.ZhelingDefeated;
        Check(content.IsGateOpen("ZhelingPass", data), "combat resolution opens guard route");
        data.flags = (int)SkyIslandStoryFlag.ZhelingReconciled;
        Check(content.IsGateOpen("ZhelingPass", data), "peace resolution opens guard route");
        Check(!SkyIslandContent.TryParse(world.Replace("EnemySpawn_D", "EnemySpawn_Missing"), out content, out contentError), "wrong author marker rejected");
        Check(!SkyIslandContent.TryParse(world.Replace("\"count\": 2", "\"count\": 0"), out content, out contentError), "zero enemies rejected");
        Check(!SkyIslandContent.TryParse(world.Replace("\"requiredFlags\": 3,", "\"requiredFlags\": 0,"), out content, out contentError), "gate bypass rejected");
        Check(!SkyIslandContent.TryParse(world.Replace("\"version\": 1", "\"version\": 1, \"version\": 1"), out content, out contentError), "duplicate JSON property rejected");
        Check(!SkyIslandContent.TryParse(world.Replace("\"id\": \"D\"", "\"id\": \"C\""), out content, out contentError), "duplicate encounter ID rejected");

        // ---- CR-2026-09-08-004：宿主销毁不得吞掉已接受但尚未落盘的事实 ----
        // 报告原始复现：接受航路图 -> 官方存储忙 -> 正常离岛移交 -> 销毁 Mod 宿主 -> 解除忙 -> 重读同槽。
        story = Open(7);
        Apply(story, SkyIslandStoryAction.FindRouteChart);
        SavesSystem.IsSaving = true;
        var host = new UnityEngine.GameObject("ModBehaviour");
        SkyIslandStorySaveRecovery.CloseOrRetain(story);
        Check(SkyIslandStorySaveRecovery.IsPending(), "busy official storage hands the pending save to a standalone owner");
        SkyIslandStorySaveRecovery.CloseOrRetain(story);
        Check(UnityEngine.Object.FindObjectsOfType<SkyIslandStorySaveRecovery>(true).Length == 1, "repeat handover never creates a second owner");
        UnityEngine.Object.Destroy(host);
        Check(SkyIslandStorySaveRecovery.IsPending(), "recovery owner survives Mod host destruction");
        SavesSystem.IsSaving = false;
        UnityEngine.Time.unscaledTime += 2;
        UnityEngine.MonoBehaviour.PumpAll();
        Check(!SkyIslandStorySaveRecovery.IsPending() && SavesSystem.Subscribers == 0, "recovery completes once official storage frees up");
        story = Open(7);
        Check(story.Current.Has(SkyIslandStoryFlag.RouteChart), "accepted progress survives a host destroyed mid-save");
        story.Close();

        // ---- CR-2026-09-08-003：一次键写入异常之后必须仍有可到达的关闭终点 ----
        story = Open(8);
        Apply(story, SkyIslandStoryAction.FindOldLetter);
        SavesSystem.FailKeyWrite = true;
        story.Tick(true);
        SavesSystem.FailKeyWrite = false;
        Check(!story.CanWrite, "a key write exception puts the shared store into its one-way fault");
        Reject(story, SkyIslandStoryAction.FindRouteChart);
        UnityEngine.Time.unscaledTime += 2;
        Check(story.TryClose(), "storage recovery reaches a real close instead of blocking re-entry forever");
        Check(SavesSystem.Subscribers == 0, "recovered close releases the save subscription");
        story = Open(8);
        Check(story.Current.Has(SkyIslandStoryFlag.OldLetter) && story.CanWrite,
            "recovered slot keeps the accepted fact and accepts new writes");
        Apply(story, SkyIslandStoryAction.FindRouteChart);
        story.Tick(true);
        story.Close();
        Check(SavesSystem.Subscribers == 0, "all recovery sessions released events");
        Console.WriteLine("PASS SkyIslandStory: " + checks + " assertions (production rules, codec, store, coordinator and save recovery; host substitutes)");
    }
}

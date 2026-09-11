using System;
using System.Collections.Generic;
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
    /// <summary>英文界面下的玩家文案不许残留中文（CJK 表意、中文标点、全角形式）。</summary>
    private static bool ContainsCjk(string text)
    {
        foreach (char c in text ?? string.Empty)
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF)) return true;
        return false;
    }
    /// <summary>下一封信的 id；没有信时返回占位串，让「应时的信永远不来」这类破坏红在断言上而不是空引用上。</summary>
    private static string NextLetterId(SkyIslandStoryData data)
    {
        SkyIslandLetter next = SkyIslandLetters.NextFor(data);
        return next == null ? "(none)" : next.Id;
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
        // 区域判定改为「脚下是哪块地」：生成器按区域切出 COL_Ground_{区域}，岛返回 id，桥与其它名字一律 null。
        Check(SkyIslandStoryService.GroundRegionOf("COL_Ground_B") == "B", "island ground resolves to its region");
        Check(SkyIslandStoryService.GroundRegionOf("COL_Ground_S3") == "S3", "side island ground resolves to its region");
        foreach (string notRegion in new[] { "COL_Ground_AB", "COL_Ground_CS1", "COL_Ground_K1", "COL_Ground_",
            "COL_Ground_B_Mural", "COL_Wall_A", "VIS_Ground_B", "", null })
            Check(SkyIslandStoryService.GroundRegionOf(notRegion) == null, "bridge or foreign collider never resolves: " + notRegion);
        // 失败提示：中文界面保留异常原文，英文界面绝不把中文原文拼进提示条。
        Check(SkyIslandStoryRules.WithDetail("创建失败：", "官方加载器拒绝") == "创建失败：官方加载器拒绝", "chinese failure keeps detail");
        L10n.IsChinese = false;
        string english = SkyIslandStoryRules.WithDetail("Sky Islands setup failed", "官方加载器拒绝");
        Check(english.IndexOf("官方", StringComparison.Ordinal) < 0 && english.StartsWith("Sky Islands setup failed", StringComparison.Ordinal),
            "english failure never embeds chinese detail");
        L10n.IsChinese = true;
        // 目标文本缓存：同一剧情位与语言复用，换语言立刻重建。
        string objectiveZh = story.CurrentObjective;
        Check(ReferenceEquals(objectiveZh, story.CurrentObjective), "objective text reused while flags and language are unchanged");
        L10n.IsChinese = false;
        Check(story.CurrentObjective == SkyIslandStoryRules.Objective(story.Current) && !ReferenceEquals(objectiveZh, story.CurrentObjective),
            "objective text rebuilds when the language changes");
        L10n.IsChinese = true;
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

        // ---- 2026-09-10 可玩性评估：岛上落盘去抖 ----
        // 可重做的事实（到访 / 清场 / 见闻）在安全帧也要攒够 FlushDebounceSeconds 才整档写盘；
        // 剧情动作照旧下一个安全帧就写（顺带把攒着的事实一起写掉）；离岛绕闸落盘不受去抖影响。
        story = Open(9);
        int beforeWrites = SavesSystem.PhysicalWrites;
        Check(story.RecordRegionVisited("C"), "debounce: region visit accepted into the store");
        story.Tick(true);
        Check(SavesSystem.PhysicalWrites == beforeWrites, "debounce: a replayable fact does not trigger an immediate whole-file save");
        UnityEngine.Time.unscaledTime += SkyIslandStoryService.FlushDebounceSeconds - 1f;
        story.Tick(true);
        Check(SavesSystem.PhysicalWrites == beforeWrites, "debounce: still inside the window");
        UnityEngine.Time.unscaledTime += 2f;
        story.Tick(false);
        Check(SavesSystem.PhysicalWrites == beforeWrites, "debounce: an expired window still waits for a combat-safe frame");
        story.Tick(true);
        Check(SavesSystem.PhysicalWrites == beforeWrites + 1, "debounce: the batch is written once the window has passed");
        story.Tick(true);
        Check(SavesSystem.PhysicalWrites == beforeWrites + 1, "debounce: nothing pending, nothing written");
        Check(story.RecordEncounterCleared("C_02"), "debounce: clear accepted");
        Apply(story, SkyIslandStoryAction.FindPlantingRecord);
        story.Tick(true);
        Check(SavesSystem.PhysicalWrites == beforeWrites + 2,
            "a story action flushes on the next safe frame and takes the pending replayable facts with it");
        Check(story.RecordEncounterCleared("S1"), "close: clear accepted");
        Check(story.TryClose(), "close: the host-destroy path flushes a debounced fact");
        Check(SavesSystem.PhysicalWrites == beforeWrites + 3, "close: debounce never holds back the final save");
        story = Open(9);
        Check(story.Current.EncounterCleared("S1") && story.Current.EncounterCleared("C_02") && story.HasVisitedRegion("C")
            && story.Current.Has(SkyIslandStoryFlag.PlantingRecord), "close: every accepted fact survives re-entry");
        story.Close();

        // ---- 战斗里了结的三件事：场上字幕与 TryApply 的回话是同一份文案 ----
        foreach (SkyIslandStoryFlag outcome in SkyIslandStoryRules.CombatOutcomeFlags)
        {
            SkyIslandStoryAction outcomeAction = outcome == SkyIslandStoryFlag.ZhelingDefeated ? SkyIslandStoryAction.ZhelingDefeated
                : outcome == SkyIslandStoryFlag.BellKeeperDefeated ? SkyIslandStoryAction.BellKeeperDefeated : SkyIslandStoryAction.StormSlain;
            SkyIslandStoryData lit = SkyIslandStoryRules.CreateDefault();
            lit.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
            SkyIslandStoryData outcomeCandidate;
            string outcomeMessage;
            Check(SkyIslandStoryRules.TryApply(lit, outcomeAction, out outcomeCandidate, out outcomeMessage), "combat outcome accepted: " + outcomeAction);
            Check(!string.IsNullOrEmpty(outcomeMessage) && outcomeMessage == SkyIslandStoryRules.CombatOutcome(outcome),
                "combat outcome caption is the rule's own reply: " + outcomeAction);
        }
        Check(SkyIslandStoryRules.CombatOutcomeFlags.Length == 3, "exactly three combat-resolved outcomes are announced");
        Check(SkyIslandStoryRules.CombatOutcome(SkyIslandStoryFlag.ZhelingReconciled) == null
            && SkyIslandStoryRules.CombatOutcome(SkyIslandStoryFlag.Ending) == null, "panel actions have no combat caption");

        // ---- 分段计时：进岛从 0 起记、同一次清场重投只记一行、离岛记一行 ----
        UnityEngine.Time.realtimeSinceStartup = 100f;
        story = Open(10);
        Check(UnityEngine.Debug.LastLog != null && UnityEngine.Debug.LastLog.StartsWith("[SkyIsland] SKY_TIMING t=0.0 ev=raid_open", StringComparison.Ordinal),
            "timing: raid open is logged from zero");
        UnityEngine.Time.realtimeSinceStartup = 162.34f;
        Check(story.RecordEncounterCleared("D"), "timing: clear accepted");
        Check(UnityEngine.Debug.LastLog == "[SkyIsland] SKY_TIMING t=62.3 ev=clear id=D", "timing: clear logged with invariant one-decimal seconds");
        UnityEngine.Debug.LastLog = null;
        Check(!story.RecordEncounterCleared("D") && UnityEngine.Debug.LastLog == null, "timing: a repeated clear in the same raid is not logged twice");
        Check(story.TryClose(), "timing: close succeeds");
        Check(UnityEngine.Debug.LastLog != null && UnityEngine.Debug.LastLog.Contains(" ev=raid_close"), "timing: raid close is logged");
        story.Close();
        Check(SavesSystem.Subscribers == 0, "playtime sessions released events");

        // ---- 批次二：信鸽来信 ----
        SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
        Check(SkyIslandLetters.Count == 12, "twelve pigeon letters");
        var letterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkyIslandLetter letter in SkyIslandLetters.All)
        {
            Check(letterIds.Add(letter.Id) && letter.Id.StartsWith(SkyIslandLetters.IdPrefix, StringComparison.Ordinal),
                "letter id unique and prefixed: " + letter.Id);
            Check(SkyIslandStoryService.RegionBit(letter.Region) != 0, "letter lands in a real region: " + letter.Id);
            Check(!string.IsNullOrEmpty(letter.Anchor) && !string.IsNullOrEmpty(letter.TitleCn) && !string.IsNullOrEmpty(letter.TitleEn)
                && !string.IsNullOrEmpty(letter.BodyCn) && !string.IsNullOrEmpty(letter.BodyEn), "letter text is paired: " + letter.Id);
            Check(((int)letter.Requires & ~SkyIslandStoryRules.KnownFlags) == 0, "letter prerequisite uses registered flags: " + letter.Id);
        }
        Check(NextLetterId(fresh) == "Letter_01", "a new save gets the dock letter first");
        Check(SkyIslandLetters.NextFor(null) == null && SkyIslandLetters.Find("Letter_99") == null, "no letter for a missing save or an unknown id");
        SkyIslandStoryData mail = fresh.Copy();
        var arrival = new List<string>();
        for (SkyIslandLetter next = SkyIslandLetters.NextFor(mail); next != null && arrival.Count < 20; next = SkyIslandLetters.NextFor(mail))
        {
            arrival.Add(next.Id);
            var received = new List<string>(mail.discoveredNotes);
            received.Add(next.Id);
            mail.discoveredNotes = received.ToArray();
        }
        Check(arrival.Count == 8 && arrival[0] == "Letter_01" && arrival[7] == "Letter_08",
            "ungated letters arrive one per raid in order, then wait for their prerequisites");
        mail.flags |= (int)SkyIslandStoryFlag.StarLamp;
        Check(NextLetterId(mail) == "Letter_10", "a letter whose prerequisite just came true is delivered next");
        mail.flags |= (int)SkyIslandStoryFlag.WindBeacon;
        Check(NextLetterId(mail) == "Letter_09", "unlocked gated letters keep their order");
        SkyIslandStoryData mailRoundTrip = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(mail));
        Check(mailRoundTrip != null && mailRoundTrip.discoveredNotes.Length == 8, "letter ids round trip through the save codec");

        // ---- 批次二：秘境谜题（每一道题的每一种选法都走一遍） ----
        Check(SkyIslandPuzzles.All.Length == 4, "four hidden-isle puzzles");
        foreach (SkyIslandPuzzle puzzle in SkyIslandPuzzles.All)
        {
            SkyIslandStoryAction evidence;
            Check(SkyIslandStoryRules.TrySearchAction(puzzle.Key, out evidence), "puzzle sits on a side-evidence point: " + puzzle.Key);
            SkyIslandStoryData unlocked;
            string unlockMessage;
            Check(SkyIslandStoryRules.TryApply(SkyIslandStoryRules.CreateDefault(), evidence, out unlocked, out unlockMessage)
                && unlocked.flags == (int)puzzle.Flag, "puzzle flag is exactly what its evidence action writes: " + puzzle.Key);
            Check(SkyIslandPuzzles.For(puzzle.Key) == puzzle && puzzle.Steps.Length == 3, "three steps: " + puzzle.Key);
            var answers = new HashSet<int>();
            var state = new SkyIslandPuzzleState();
            string feedback;
            for (int s = 0; s < puzzle.Steps.Length; s++)
            {
                SkyIslandPuzzleStep step = puzzle.Steps[s];
                answers.Add(step.Answer);
                Check(step.Options.Length == 3 && step.Answer >= 0 && step.Answer < 3, "three options and a valid answer: " + puzzle.Key + "#" + s);
                SkyIslandPuzzleOutcome outcome = state.Choose(puzzle, step.Answer, out feedback);
                Check(outcome == (s + 1 == puzzle.Steps.Length ? SkyIslandPuzzleOutcome.Solved : SkyIslandPuzzleOutcome.Advanced)
                    && !string.IsNullOrEmpty(feedback), "the right answer advances: " + puzzle.Key + "#" + s);
            }
            Check(state.IsSolved(puzzle) && state.Choose(puzzle, 7, out feedback) == SkyIslandPuzzleOutcome.Invalid,
                "solved, and an out-of-range option is rejected: " + puzzle.Key);
            Check(answers.Count > 1, "answers are not all in the same position: " + puzzle.Key);
            for (int s = 0; s < puzzle.Steps.Length; s++)
            {
                SkyIslandPuzzleStep step = puzzle.Steps[s];
                for (int wrong = 0; wrong < step.Options.Length; wrong++)
                {
                    if (wrong == step.Answer) continue;
                    var retry = new SkyIslandPuzzleState();
                    for (int done = 0; done < s; done++) retry.Choose(puzzle, puzzle.Steps[done].Answer, out feedback);
                    Check(retry.Choose(puzzle, wrong, out feedback) == SkyIslandPuzzleOutcome.Hinted && feedback == step.Hint
                        && retry.CurrentStep(puzzle) == s, "first miss hints and stays: " + puzzle.Key + "#" + s + "/" + wrong);
                    Check(retry.Choose(puzzle, wrong, out feedback) == SkyIslandPuzzleOutcome.Revealed && feedback == step.Reveal
                        && retry.CurrentStep(puzzle) == s, "second miss reveals and stays: " + puzzle.Key + "#" + s + "/" + wrong);
                    SkyIslandPuzzleOutcome recovered = retry.Choose(puzzle, step.Answer, out feedback);
                    Check(recovered == SkyIslandPuzzleOutcome.Advanced || recovered == SkyIslandPuzzleOutcome.Solved,
                        "misses never lock the puzzle: " + puzzle.Key + "#" + s + "/" + wrong);
                }
            }
        }

        // ---- 批次二：归航船名册随分支变化 ----
        SkyIslandStoryData peaceful = SkyIslandStoryRules.CreateDefault();
        peaceful.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.OldLetter
            | SkyIslandStoryFlag.RouteChart | SkyIslandStoryFlag.Telescope | SkyIslandStoryFlag.PlantingRecord
            | SkyIslandStoryFlag.PlantingDelivered | SkyIslandStoryFlag.ZhelingReconciled | SkyIslandStoryFlag.BellKeeperReconciled
            | SkyIslandStoryFlag.StormSlain | SkyIslandStoryFlag.Ending);
        SkyIslandStoryData forceful = SkyIslandStoryRules.CreateDefault();
        forceful.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.ZhelingDefeated
            | SkyIslandStoryFlag.BellKeeperDefeated | SkyIslandStoryFlag.Ending);
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(peaceful)) != null
            && SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(forceful)) != null, "both branch saves are valid");
        for (int page = 0; page < SkyIslandCrew.Count; page++)
        {
            Check(SkyIslandCrew.Page(page, peaceful) != SkyIslandCrew.Page(page, forceful), "crew page reacts to this save's choices: " + page);
            Check(SkyIslandCrew.IndexOf(SkyIslandCrew.NoteId(page)) == page, "crew note id maps back to its page: " + page);
        }
        Check(SkyIslandCrew.IndexOf("Crew_5") < 0 && SkyIslandCrew.IndexOf("Letter_01") < 0 && SkyIslandCrew.IndexOf(null) < 0,
            "only the four roster pages are crew ids");

        // ---- 批次二：群岛手记 ----
        var journalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] chapter in SkyIslandJournal.Chapters)
            foreach (string key in chapter) Check(journalKeys.Add(key), "journal lists each note once: " + key);
        Check(journalKeys.Count == SkyIslandJournal.NoteCount, "journal chapters cover exactly twenty notes");
        foreach (string region in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
            Check(journalKeys.Contains("Search_" + region) && journalKeys.Contains("Search_" + region + "_02"), "journal covers both notes of region " + region);
        foreach (string side in new[] { "S1", "S2", "S3", "S4" })
            Check(journalKeys.Contains("Search_" + side), "journal covers side evidence " + side);
        SkyIslandStoryData wellRead = SkyIslandStoryRules.CreateDefault();
        wellRead.discoveredNotes = new List<string>(journalKeys).ToArray();
        Check(!SkyIslandJournal.Overview(fresh, null).Contains(SkyIslandJournal.Epilogue)
            && SkyIslandJournal.Overview(wellRead, null).Contains(SkyIslandJournal.Epilogue), "the last page appears only once all twenty notes are in");
        string unreadChapter = SkyIslandJournal.Chapter(0, fresh, key => "T:" + key, key => "B:" + key);
        string readChapter = SkyIslandJournal.Chapter(0, wellRead, key => "T:" + key, key => "B:" + key);
        Check(unreadChapter.Contains("□ T:Search_A") && !unreadChapter.Contains("B:Search_A"), "an unrecorded note shows its title only");
        Check(readChapter.Contains("■ T:Search_A\nB:Search_A"), "a recorded note shows its title and text");
        Check(SkyIslandJournal.Chapter(9, fresh, null, null) == string.Empty, "an out-of-range chapter is empty");
        Check(SkyIslandJournal.Letters(fresh).IndexOf(SkyIslandLetters.All[0].BodyCn, StringComparison.Ordinal) < 0
            && SkyIslandJournal.Letters(mail).IndexOf(SkyIslandLetters.All[0].BodyCn, StringComparison.Ordinal) >= 0,
            "letters stay unspoiled until received");
        Check(SkyIslandJournal.RegionsVisited(new SkyIslandStoryData { visitedRegions = 4095 }) == 12, "all twelve regions counted");

        // ---- 批次二：天空岛物品规则（纪念品台账、岛上特产、罗盘读数） ----
        Check(SkyIslandItemRules.AllTypeIds.Length == 18 && SkyIslandItemRules.AllTypeIds[0] == 500068
            && SkyIslandItemRules.AllTypeIds[17] == 500085, "sky island items occupy 500068-500085 (batches two, three and four)");
        for (int i = 1; i < SkyIslandItemRules.AllTypeIds.Length; i++)
            Check(SkyIslandItemRules.AllTypeIds[i] == SkyIslandItemRules.AllTypeIds[i - 1] + 1, "sky island item ids are contiguous: " + i);
        foreach (int typeId in SkyIslandItemRules.AllTypeIds)
            Check(SkyIslandItemRules.NameCn(typeId) != SkyIslandItemRules.NameCn(0) && SkyIslandItemRules.NameEn(typeId) != SkyIslandItemRules.NameEn(0),
                "item has its own name in both languages: " + typeId);
        SkyIslandKeepsake compassKeepsake = SkyIslandItemRules.FindKeepsake("Keepsake_Compass");
        SkyIslandKeepsake badgeKeepsake = SkyIslandItemRules.FindKeepsake("Keepsake_Badge");
        SkyIslandKeepsake coreKeepsake = SkyIslandItemRules.FindKeepsake("Keepsake_Core");
        Check(compassKeepsake != null && badgeKeepsake != null && coreKeepsake != null && SkyIslandItemRules.FindKeepsake("Keepsake_X") == null,
            "three registered keepsakes");
        Check(!SkyIslandItemRules.Due(fresh, compassKeepsake) && !SkyIslandItemRules.Due(fresh, badgeKeepsake)
            && !SkyIslandItemRules.Due(fresh, coreKeepsake), "a new save is owed no keepsake");
        Check(SkyIslandItemRules.Due(mail, compassKeepsake), "the first received letter brings the compass");
        Check(SkyIslandItemRules.Due(peaceful, badgeKeepsake) && SkyIslandItemRules.Due(peaceful, coreKeepsake)
            && SkyIslandItemRules.Due(forceful, badgeKeepsake) && !SkyIslandItemRules.Due(forceful, coreKeepsake),
            "the badge follows the ending and the core follows the Windeater");
        SkyIslandStoryData grantedSave = peaceful.Copy();
        grantedSave.discoveredNotes = new[] { "Keepsake_Badge" };
        Check(!SkyIslandItemRules.Due(grantedSave, badgeKeepsake) && SkyIslandItemRules.GrantedCount(grantedSave) == 1,
            "a granted keepsake is never owed again");
        Check(badgeKeepsake.ToStorage && coreKeepsake.ToStorage && !compassKeepsake.ToStorage,
            "keepsakes are sent to storage; the compass goes to the pack");
        const int rollGrid = 10000;
        var extras = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SkyIslandLootTier tier in new[] { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks })
        {
            Check(SkyIslandItemRules.IslandExtraFor(tier, 0.999) == 0, "most crates carry no island extra: " + tier);
            for (int i = 0; i < rollGrid; i++)
            {
                int extra = SkyIslandItemRules.IslandExtraFor(tier, (i + 0.5) / rollGrid);
                if (extra == 0) continue;
                string bucket = tier + ":" + extra;
                int seen;
                extras.TryGetValue(bucket, out seen);
                extras[bucket] = seen + 1;
            }
        }
        Check(extras.Count == 6 && extras["Supply:500071"] == 1200 && extras["Voyage:500072"] == 1000 && extras["Voyage:500071"] == 800
            && extras["Starworks:500072"] == 1800 && extras["Starworks:500071"] == 600 && extras["Starworks:500070"] == 200,
            "island extra rates per tier are exactly the documented ones");
        Check(SkyIslandItemRules.Bearing(0, 1) == "北" && SkyIslandItemRules.Bearing(1, 0) == "东" && SkyIslandItemRules.Bearing(0, -1) == "南"
            && SkyIslandItemRules.Bearing(-1, 0) == "西" && SkyIslandItemRules.Bearing(1, 1) == "东北" && SkyIslandItemRules.Bearing(-1, 1) == "西北"
            && SkyIslandItemRules.Bearing(-1, -1) == "西南" && SkyIslandItemRules.Bearing(1, -1) == "东南", "eight-way bearing with +z north and +x east");
        Check(SkyIslandItemRules.CompassReading(true, 0, 125, "信鸽").Contains("约 130 米：信鸽"), "compass distance rounds to the nearest ten metres");
        Check(SkyIslandItemRules.CompassReading(true, 3, 4, "信鸽").Contains("附近")
            && SkyIslandItemRules.CompassReading(false, 0, 0, null).Contains("静静"), "near and empty compass readings");

        // ---- 批次二：英文界面没有残留中文 ----
        L10n.IsChinese = false;
        string englishBatch = SkyIslandCrew.Page(1, forceful) + SkyIslandCrew.Page(3, peaceful) + SkyIslandCrew.Intro(forceful)
            + SkyIslandJournal.Overview(wellRead, null) + SkyIslandJournal.Letters(mail) + SkyIslandJournal.Keepsakes(fresh)
            + SkyIslandItemRules.CompassReading(true, 30, 40, "x") + SkyIslandPuzzles.All[3].Steps[2].Prompt + SkyIslandPuzzles.All[1].Solved;
        L10n.IsChinese = true;
        Check(!ContainsCjk(englishBatch), "batch-two text has an English half everywhere");

        // ---- 批次二：来信、名册与纪念品经剧情服务写进本槽手记 ----
        story = Open(11);
        string noteMessage;
        Check(story.RecordNote("Letter_01", out noteMessage) && SkyIslandLetters.Collected(story.Current, "Letter_01"),
            "a registered letter is written to the journal");
        Check(!story.RecordNote("Letter_01", out noteMessage), "the same letter is never written twice");
        Check(!story.RecordNote("Letter_99", out noteMessage) && !story.RecordNote("Search_Z", out noteMessage)
            && !story.RecordNote(null, out noteMessage) && story.Current.discoveredNotes.Length == 1, "unregistered journal ids never reach the save");
        Check(story.RecordNote(SkyIslandCrew.NoteId(1), out noteMessage) && story.RecordNote("Keepsake_Compass", out noteMessage),
            "roster pages and keepsake grants share the journal");
        Check(NextLetterId(story.Current) == "Letter_02", "the next raid brings the next letter");
        Check(story.DescribeNpc("sky_fuzhou").Contains("阿潮的缆绳") && !story.DescribeNpc("sky_weibai").Contains("苇生的信"),
            "residents react only to letters actually received");
        Check(story.TryClose(), "journal writes flush on close");
        story = Open(11);
        Check(SkyIslandCrew.Read(story.Current, 1) && SkyIslandItemRules.Granted(story.Current, "Keepsake_Compass")
            && SkyIslandLetters.Collected(story.Current, "Letter_01"), "journal entries survive re-entry");
        story.Close();
        Check(SavesSystem.Subscribers == 0, "batch-two sessions released events");

        // ---- 批次三：采集点表 ----
        SkyIslandGatherNode[] gatherNodes = SkyIslandFieldcraftRules.Nodes;
        Check(gatherNodes.Length == 30, "thirty gathering spots");
        var gatherIds = new HashSet<string>(StringComparer.Ordinal);
        var kindsPlaced = new HashSet<SkyIslandGatherKind>();
        foreach (SkyIslandGatherNode node in gatherNodes)
        {
            Check(gatherIds.Add(node.Id) && SkyIslandFieldcraftRules.FindNode(node.Id) == node, "gathering spot id unique: " + node.Id);
            Check(SkyIslandStoryService.RegionBit(node.Region) != 0, "gathering spot sits in a real region: " + node.Id);
            Check(node.Distance >= 6f && node.Distance <= 12f && node.Bearing >= 0f && node.Bearing < 360f, "gathering offset sane: " + node.Id);
            kindsPlaced.Add(node.Kind);
        }
        Check(kindsPlaced.Count == 5 && SkyIslandFieldcraftRules.FindNode("Z9") == null, "all five gathering kinds are placed, unknown ids are not");
        var gatherKinds = new[] { SkyIslandGatherKind.Grass, SkyIslandGatherKind.Driftwood, SkyIslandGatherKind.Moss, SkyIslandGatherKind.Ore, SkyIslandGatherKind.Crystal };
        var tiers = new[] { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks };
        foreach (SkyIslandGatherKind kind in gatherKinds)
        {
            int previousMin = 0, previousMax = 0;
            double previousExtra = 0.0;
            foreach (SkyIslandLootTier tier in tiers)
            {
                int min, max;
                SkyIslandFieldcraftRules.CountRange(kind, tier, out min, out max);
                Check(min >= 1 && min <= max && max <= 5, "count range sane: " + kind + "/" + tier);
                Check(min >= previousMin && max >= previousMax, "yield never shrinks as the isles get more dangerous: " + kind + "/" + tier);
                double extra = SkyIslandFieldcraftRules.ExtraChance(kind, tier, false);
                Check(extra >= previousExtra && SkyIslandFieldcraftRules.ExtraChance(kind, tier, true) >= extra,
                    "extras never shrink with danger, and night never lowers them: " + kind + "/" + tier);
                Check(SkyIslandFieldcraftRules.InteractSeconds(kind) >= 1f && SkyIslandFieldcraftRules.InteractSeconds(kind) <= 4f, "gathering read time is short: " + kind);
                previousMin = min; previousMax = max; previousExtra = extra;
            }
        }
        Check(SkyIslandFieldcraftRules.ExtraChance(SkyIslandGatherKind.Crystal, SkyIslandLootTier.Supply, true) == 0.0
            && SkyIslandFieldcraftRules.ExtraChance(SkyIslandGatherKind.Grass, SkyIslandLootTier.Starworks, true) == 0.0,
            "safe zones and plain spots never roll extras");

        // ---- 批次三：同一趟同一处的产出确定；昼夜只改附带、不改主产出件数 ----
        SkyIslandGatherNode deepCrystal = SkyIslandFieldcraftRules.FindNode("G3");
        Check(deepCrystal != null && deepCrystal.Kind == SkyIslandGatherKind.Crystal && deepCrystal.Tier == SkyIslandLootTier.Starworks,
            "G3 is a deep wind crystal cluster");
        SkyIslandYield[] firstRoll = SkyIslandFieldcraftRules.Roll(deepCrystal, SkyIslandLootTables.CreateStream(42, "gather:G3"), false);
        SkyIslandYield[] secondRoll = SkyIslandFieldcraftRules.Roll(deepCrystal, SkyIslandLootTables.CreateStream(42, "gather:G3"), false);
        Check(firstRoll.Length == secondRoll.Length && firstRoll[0].Count == secondRoll[0].Count, "the same raid seed and spot give the same yield");
        Check(SkyIslandFieldcraftRules.Roll(null, SkyIslandLootTables.CreateStream(1, "x"), false).Length == 0, "no spot, no yield");
        const int gatherSamples = 4000;
        int dayExtras = 0, nightExtras = 0;
        bool primaryStable = true, extraKept = true;
        for (int seed = 0; seed < gatherSamples; seed++)
        {
            SkyIslandYield[] day = SkyIslandFieldcraftRules.Roll(deepCrystal, SkyIslandLootTables.CreateStream(seed, "gather:G3"), false);
            SkyIslandYield[] night = SkyIslandFieldcraftRules.Roll(deepCrystal, SkyIslandLootTables.CreateStream(seed, "gather:G3"), true);
            primaryStable &= day[0].TypeId == BossRushItemIds.SkyIslandWindcrystalShard && day[0].Count >= 2 && day[0].Count <= 3
                && night[0].TypeId == day[0].TypeId && night[0].Count == day[0].Count;
            if (day.Length > 1)
            {
                dayExtras++;
                extraKept &= day[1].TypeId == BossRushItemIds.SkyIslandStardust && day[1].Count == 1 && night.Length > 1;
            }
            if (night.Length > 1) nightExtras++;
        }
        Check(primaryStable, "night only changes the extra, never the primary item or its count");
        Check(extraKept, "an extra rolled by day is still there by night");
        Check(Math.Abs(dayExtras / (double)gatherSamples - 0.35) < 0.03 && Math.Abs(nightExtras / (double)gatherSamples - 0.50) < 0.03,
            "deep cluster stardust is about 35% by day and 50% by night");

        // ---- 批次三：一趟采完的期望产出与价值（钉住表的形状；报告里的经济估算就是这组数） ----
        Dictionary<int, double> dayYield = SkyIslandFieldcraftRules.ExpectedRaidYield(false);
        Dictionary<int, double> nightYield = SkyIslandFieldcraftRules.ExpectedRaidYield(true);
        Check(dayYield.Count == 6 && Math.Abs(dayYield[BossRushItemIds.SkyIslandGreenearSheaf] - 14.5) < 1e-9
            && Math.Abs(dayYield[BossRushItemIds.SkyIslandDriftwood] - 14.5) < 1e-9 && Math.Abs(dayYield[BossRushItemIds.SkyIslandCloudmossFiber] - 14.0) < 1e-9
            && Math.Abs(dayYield[BossRushItemIds.SkyIslandBrassScrap] - 17.5) < 1e-9 && Math.Abs(dayYield[BossRushItemIds.SkyIslandWindcrystalShard] - 13.65) < 1e-9
            && Math.Abs(dayYield[BossRushItemIds.SkyIslandStardust] - 1.5) < 1e-9, "expected raid yield by day");
        Check(Math.Abs(nightYield[BossRushItemIds.SkyIslandStardust] - 2.4) < 1e-9
            && Math.Abs(nightYield[BossRushItemIds.SkyIslandWindcrystalShard] - 13.65) < 1e-9, "night adds stardust only");
        Check(Math.Abs(SkyIslandFieldcraftRules.ExpectedRaidValue(false) - 13932.5) < 1e-6
            && Math.Abs(SkyIslandFieldcraftRules.ExpectedRaidValue(true) - 14742.5) < 1e-6, "expected gathering value per raid");
        foreach (int typeId in SkyIslandItemRules.AllTypeIds)
            Check(SkyIslandItemRules.ValueOf(typeId) > 0, "every sky island item has a value: " + typeId);

        // ---- 批次三：配方 ----
        Check(SkyIslandFieldcraftRules.Recipes.Length == 11, "eleven recipes (batch three's eight plus the veil, the zapper and the fan)");
        var recipeIds = new HashSet<string>(StringComparer.Ordinal);
        var skyItems = new HashSet<int>(SkyIslandItemRules.AllTypeIds);
        foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes)
        {
            Check(recipeIds.Add(recipe.Id) && SkyIslandFieldcraftRules.FindRecipe(recipe.Id) == recipe, "recipe id unique: " + recipe.Id);
            Check(skyItems.Contains(recipe.OutputTypeId) && recipe.OutputCount >= 1 && recipe.Inputs.Length >= 1, "recipe makes a registered sky island item: " + recipe.Id);
            foreach (SkyIslandIngredient input in recipe.Inputs)
                Check(Array.IndexOf(SkyIslandFieldcraftRules.MaterialTypeIds, input.TypeId) >= 0 && input.Count >= 1 && input.TypeId != recipe.OutputTypeId,
                    "recipe inputs are island materials: " + recipe.Id);
            double ratio = SkyIslandFieldcraftRules.OutputValue(recipe) / (double)SkyIslandFieldcraftRules.InputValue(recipe);
            Check(ratio >= 0.9 && ratio <= 2.0, "crafting is a small premium, not a money printer: " + recipe.Id + " x" + ratio.ToString("0.00"));
        }
        foreach (SkyIslandCraftStation station in new[] { SkyIslandCraftStation.Dock, SkyIslandCraftStation.Stove, SkyIslandCraftStation.Mortar })
        {
            int stationRecipes = SkyIslandFieldcraftRules.RecipesFor(station).Count;
            // 批次四给渡口工台加了灭蚊灯（5 条）。合成面板上只有配方按钮，面板布局属性测试按最坏 6 个选项复算。
            Check(stationRecipes >= 2 && stationRecipes <= 5, "each station fits in one panel page: " + station);
        }
        // 每件批次三物品都拿得到：从采集点能出的出发，按配方做不动点闭包。
        var obtainable = new HashSet<int>();
        foreach (SkyIslandGatherNode node in gatherNodes)
        {
            obtainable.Add(SkyIslandFieldcraftRules.PrimaryTypeId(node.Kind));
            if (SkyIslandFieldcraftRules.ExtraTypeId(node.Kind) != 0 && SkyIslandFieldcraftRules.ExtraChance(node.Kind, node.Tier, false) > 0.0)
                obtainable.Add(SkyIslandFieldcraftRules.ExtraTypeId(node.Kind));
        }
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes)
            {
                bool ready = true;
                foreach (SkyIslandIngredient input in recipe.Inputs) ready &= obtainable.Contains(input.TypeId);
                if (ready && obtainable.Add(recipe.OutputTypeId)) grew = true;
            }
        }
        for (int typeId = BossRushItemIds.SkyIslandCloudmossFiber; typeId <= BossRushItemIds.SkyIslandQinglanCharm; typeId++)
            Check(obtainable.Contains(typeId), "every batch-three item has a way to get it: " + typeId);
        for (int typeId = BossRushItemIds.SkyIslandCloudmossVeil; typeId <= BossRushItemIds.SkyIslandSmokeFan; typeId++)
            Check(obtainable.Contains(typeId), "every batch-four gnat counter has a way to get it: " + typeId);
        SkyIslandRecipe lanternRecipe = SkyIslandFieldcraftRules.FindRecipe("Lantern");
        var pack = new Dictionary<int, int> { { BossRushItemIds.SkyIslandDriftwood, 1 }, { BossRushItemIds.SkyIslandCloudmossFiber, 5 } };
        Func<int, int> countInPack = id => { int have; return pack.TryGetValue(id, out have) ? have : 0; };
        List<SkyIslandIngredient> shortBy = SkyIslandFieldcraftRules.Missing(lanternRecipe, countInPack);
        Check(shortBy.Count == 1 && shortBy[0].TypeId == BossRushItemIds.SkyIslandDriftwood && shortBy[0].Count == 1
            && !SkyIslandFieldcraftRules.CanCraft(lanternRecipe, countInPack), "one driftwood short of a lantern");
        Check(SkyIslandFieldcraftRules.MissingMessage(shortBy) == "材料还差：浮木 ×1。", "missing materials are spelled out");
        pack[BossRushItemIds.SkyIslandDriftwood] = 2;
        Check(SkyIslandFieldcraftRules.CanCraft(lanternRecipe, countInPack) && SkyIslandFieldcraftRules.Missing(lanternRecipe, countInPack).Count == 0,
            "two driftwood and a fibre make a lantern");
        Check(SkyIslandFieldcraftRules.RecipeLabel(lanternRecipe, countInPack) == "制作 风灯（浮木 2/2 · 云苔纤维 1/1）",
            "recipe button shows have/need, capped at need");
        Check(!SkyIslandFieldcraftRules.CanCraft(null, countInPack) && SkyIslandFieldcraftRules.PackSummary(null).Contains("采集点"),
            "no recipe cannot be crafted; an empty pack points at the gathering spots");

        // ---- 批次三：耗材与夜风 ----
        Check(SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandWindLantern) == SkyIslandFieldBuff.Lantern
            && SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandWindwardIncense) == SkyIslandFieldBuff.Incense
            && SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandQinglanCharm) == SkyIslandFieldBuff.Charm
            && SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandHomecomingBento) == SkyIslandFieldBuff.Meal
            && SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandCloudmossFiber) == SkyIslandFieldBuff.None, "consumables map to their effects");
        Check(SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandGnatZapper) == SkyIslandFieldBuff.Zapper
            && SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandSmokeFan) == SkyIslandFieldBuff.Fan
            && SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandCloudmossVeil) == SkyIslandFieldBuff.None
            && SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Soothe).Length > 0, "the zapper and the fan are used on the isles; the veil only has to be carried");
        Check(SkyIslandFieldcraftRules.IsNight(21) && SkyIslandFieldcraftRules.IsNight(23.5) && SkyIslandFieldcraftRules.IsNight(4.99)
            && !SkyIslandFieldcraftRules.IsNight(5) && !SkyIslandFieldcraftRules.IsNight(12) && !SkyIslandFieldcraftRules.IsNight(20.99)
            && SkyIslandFieldcraftRules.IsNight(-1) && !SkyIslandFieldcraftRules.IsNight(double.NaN), "night is 21:00 to 05:00");
        // 内容批次四：判夜收成一个口径（SkyIslandNight），夜风的入口只是转交，逐点一致。
        for (double hour = -3; hour <= 27; hour += 0.125)
            Check(SkyIslandFieldcraftRules.IsNight(hour) == SkyIslandNight.IsNight(hour), "wind night follows the one night rule: " + hour);
        Check(SkyIslandFieldcraftRules.WindLevel(false, false, false, false) == 0 && SkyIslandFieldcraftRules.WindLevel(true, false, false, false) == 1
            && SkyIslandFieldcraftRules.WindLevel(false, true, false, false) == 1 && SkyIslandFieldcraftRules.WindLevel(true, true, false, false) == 2
            && SkyIslandFieldcraftRules.WindLevel(false, false, true, false) == 0 && SkyIslandFieldcraftRules.WindLevel(false, false, true, true) == 1
            && SkyIslandFieldcraftRules.WindLevel(true, true, true, true) == 2, "wind level: night +1, bridge +1, storm pending on boardwalk or bridge +1, capped at 2");
        float exposure = 0f;
        int breezeSeconds = 0;
        while (!SkyIslandFieldcraftRules.NextChilled(false, exposure) && breezeSeconds < 1000)
        { exposure = SkyIslandFieldcraftRules.StepExposure(exposure, 1, SkyIslandWarmth.None, 1f); breezeSeconds++; }
        exposure = 0f;
        int galeSeconds = 0;
        while (!SkyIslandFieldcraftRules.NextChilled(false, exposure) && galeSeconds < 1000)
        { exposure = SkyIslandFieldcraftRules.StepExposure(exposure, 2, SkyIslandWarmth.None, 1f); galeSeconds++; }
        Check(breezeSeconds == 143 && galeSeconds == 67, "a breeze chills in about 143 game seconds, a gale in about 67");
        Check(SkyIslandFieldcraftRules.StepExposure(100f, 2, SkyIslandWarmth.Shelter, 1f) == 96f
            && SkyIslandFieldcraftRules.StepExposure(10f, 0, SkyIslandWarmth.None, 10f) == 0f
            && SkyIslandFieldcraftRules.StepExposure(99f, 2, SkyIslandWarmth.None, 5f) == 100f
            && SkyIslandFieldcraftRules.StepExposure(50f, 2, SkyIslandWarmth.None, 0f) == 50f,
            "shelter beats any wind, calm recovers, exposure is clamped and paused time adds nothing");
        Check(SkyIslandFieldcraftRules.NextChilled(true, 41f) && !SkyIslandFieldcraftRules.NextChilled(true, 40f)
            && !SkyIslandFieldcraftRules.NextChilled(false, 99.9f) && SkyIslandFieldcraftRules.NextChilled(false, 100f), "wind chill hysteresis");
        Check(SkyIslandFieldcraftRules.ChillStaminaRecover < 0f && SkyIslandFieldcraftRules.ChillStaminaRecover >= -0.3f
            && SkyIslandFieldcraftRules.ChillEnergyCost > 0f && SkyIslandFieldcraftRules.ChillEnergyCost <= 0.3f, "wind chill stays a mild penalty");

        // ---- 串联：剧情让群岛长回来（加成只加在已抽出的数上，不多抽随机数） ----
        SkyIslandStoryData restored = SkyIslandStoryRules.CreateDefault();
        restored.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.PlantingRecord
            | SkyIslandStoryFlag.PlantingDelivered | SkyIslandStoryFlag.Telescope | SkyIslandStoryFlag.StormSlain);
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(restored)) != null, "a restored-isles save is valid");
        int bonusSpots = 0;
        double bonusChanceTotal = 0.0;
        foreach (SkyIslandGatherNode node in gatherNodes)
        {
            bool expectsCount = (node.Region == "C" && node.Kind == SkyIslandGatherKind.Grass) || (node.Region == "D" && node.Kind == SkyIslandGatherKind.Moss)
                || (node.Region == "G" && node.Kind == SkyIslandGatherKind.Ore) || (node.Region == "E" && node.Kind == SkyIslandGatherKind.Crystal);
            int bonus = SkyIslandFieldcraftRules.StoryBonusCount(node, restored);
            double bonusChance = SkyIslandFieldcraftRules.StoryBonusChance(node, restored);
            Check(bonus == (expectsCount ? 1 : 0), "only restored places grow back: " + node.Id);
            Check(SkyIslandFieldcraftRules.StoryBonusCount(node, fresh) == 0 && SkyIslandFieldcraftRules.StoryBonusCount(node, null) == 0
                && SkyIslandFieldcraftRules.StoryBonusChance(node, fresh) == 0.0 && SkyIslandFieldcraftRules.StoryBonusReason(node, fresh) == null,
                "nothing grows back before the story gets there: " + node.Id);
            Check((bonus > 0 || bonusChance > 0.0) == (SkyIslandFieldcraftRules.StoryBonusReason(node, restored) != null),
                "a grown-back spot always says why: " + node.Id);
            bonusSpots += bonus;
            bonusChanceTotal += bonusChance;
            bool sameDraws = true;
            for (int seed = 0; seed < 64; seed++)
            {
                SkyIslandYield[] plain = SkyIslandFieldcraftRules.Roll(node, SkyIslandLootTables.CreateStream(seed, "gather:" + node.Id), seed % 2 == 0);
                SkyIslandYield[] none = SkyIslandFieldcraftRules.Roll(node, SkyIslandLootTables.CreateStream(seed, "gather:" + node.Id), seed % 2 == 0, null);
                SkyIslandYield[] grown = SkyIslandFieldcraftRules.Roll(node, SkyIslandLootTables.CreateStream(seed, "gather:" + node.Id), seed % 2 == 0, restored);
                sameDraws &= plain.Length == none.Length && plain[0].Count == none[0].Count && grown[0].TypeId == plain[0].TypeId
                    && grown[0].Count == plain[0].Count + bonus && grown.Length >= plain.Length;
            }
            Check(sameDraws, "story growth adds to the same draws and never takes an extra away: " + node.Id);
        }
        Check(bonusSpots == 9 && Math.Abs(bonusChanceTotal - SkyIslandFieldcraftRules.TelescopeStardustBonus) < 1e-9,
            "nine spots grow one more and the overlook cluster sheds more stardust");
        Check(Math.Abs(SkyIslandFieldcraftRules.ExpectedRaidValue(false, null) - 13932.5) < 1e-6
            && Math.Abs(SkyIslandFieldcraftRules.ExpectedRaidValue(false, restored) - 15852.5) < 1e-6
            && Math.Abs(SkyIslandFieldcraftRules.ExpectedRaidValue(true, restored) - 16662.5) < 1e-6,
            "restoring the isles adds about 1.9k of materials per raid");

        // ---- 串联：配方随剧情解锁 ----
        SkyIslandRecipe bentoRecipe = SkyIslandFieldcraftRules.FindRecipe("Bento");
        SkyIslandRecipe fuseRecipe = SkyIslandFieldcraftRules.FindRecipe("Windcrystal");
        SkyIslandRecipe compassRecipe = SkyIslandFieldcraftRules.FindRecipe("Compass");
        int gatedRecipes = 0;
        foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes)
        {
            bool gated = recipe.RequiresFlag != SkyIslandStoryFlag.None || recipe.RequiresNote != null || recipe.RequiresLamps > 0;
            if (gated) gatedRecipes++;
            Check(SkyIslandFieldcraftRules.Unlocked(recipe, fresh) == !gated && SkyIslandFieldcraftRules.Unlocked(recipe, null) == !gated,
                "a new save knows exactly the ungated recipes: " + recipe.Id);
            Check(((int)recipe.RequiresFlag & ~SkyIslandStoryRules.KnownFlags) == 0
                && (recipe.RequiresNote == null || SkyIslandItemRules.FindKeepsake(recipe.RequiresNote) != null)
                && recipe.RequiresLamps >= 0 && recipe.RequiresLamps <= SkyIslandLights.All.Length,
                "every recipe gate is a real story flag, a registered keepsake or a reachable lamp count: " + recipe.Id);
            Check(SkyIslandFieldcraftRules.LockedLabel(recipe).Contains(SkyIslandItemRules.NameCn(recipe.OutputTypeId))
                && SkyIslandFieldcraftRules.UnlockHint(recipe).Length > 0 && SkyIslandFieldcraftRules.LockedMessage(recipe).Length > 0,
                "a locked recipe names its item and says when: " + recipe.Id);
        }
        Check(gatedRecipes == 4 && bentoRecipe.RequiresFlag == SkyIslandStoryFlag.PlantingDelivered
            && fuseRecipe.RequiresFlag == SkyIslandStoryFlag.StarLamp && compassRecipe.RequiresNote == SkyIslandItemRules.CompassKeepsake,
            "the bento waits for the planting record, the windcrystal for the star lamp, the compass for the first one");
        SkyIslandStoryData planted = fresh.Copy();
        planted.flags = (int)(SkyIslandStoryFlag.PlantingRecord | SkyIslandStoryFlag.PlantingDelivered);
        SkyIslandStoryData lampLit = fresh.Copy();
        lampLit.flags = (int)SkyIslandStoryFlag.StarLamp;
        SkyIslandStoryData compassHeld = fresh.Copy();
        compassHeld.discoveredNotes = new[] { SkyIslandItemRules.CompassKeepsake };
        Check(SkyIslandFieldcraftRules.Unlocked(bentoRecipe, planted) && !SkyIslandFieldcraftRules.Unlocked(fuseRecipe, planted)
            && SkyIslandFieldcraftRules.Unlocked(fuseRecipe, lampLit) && !SkyIslandFieldcraftRules.Unlocked(bentoRecipe, lampLit)
            && SkyIslandFieldcraftRules.Unlocked(compassRecipe, compassHeld) && !SkyIslandFieldcraftRules.Unlocked(compassRecipe, peaceful),
            "each gate opens its own recipe only");
        // 内容批次四：灭蚊灯要等岛上亮起两盏风晶灯（苇白要听清灯芯的调子）；纱笠与蒲扇是早期对策，一开始就会。
        SkyIslandRecipe zapperRecipe = SkyIslandFieldcraftRules.FindRecipe("Zapper");
        SkyIslandRecipe veilRecipe = SkyIslandFieldcraftRules.FindRecipe("Veil");
        SkyIslandRecipe fanRecipe = SkyIslandFieldcraftRules.FindRecipe("Fan");
        SkyIslandStoryData oneLamp = fresh.Copy();
        oneLamp.discoveredNotes = new[] { "Light_E" };
        SkyIslandStoryData twoLamps = fresh.Copy();
        twoLamps.discoveredNotes = new[] { "Light_E", "Light_G" };
        Check(zapperRecipe != null && zapperRecipe.Station == SkyIslandCraftStation.Dock && zapperRecipe.RequiresLamps == 2 && zapperRecipe.OutputCount == 2
            && !SkyIslandFieldcraftRules.Unlocked(zapperRecipe, fresh) && !SkyIslandFieldcraftRules.Unlocked(zapperRecipe, oneLamp)
            && SkyIslandFieldcraftRules.Unlocked(zapperRecipe, twoLamps) && SkyIslandFieldcraftRules.UnlockHint(zapperRecipe).Contains("2"),
            "the gnat zapper waits for two lit windcrystal lamps and says so");
        Check(veilRecipe != null && veilRecipe.Station == SkyIslandCraftStation.Stove && fanRecipe != null && fanRecipe.Station == SkyIslandCraftStation.Mortar
            && SkyIslandFieldcraftRules.Unlocked(veilRecipe, fresh) && SkyIslandFieldcraftRules.Unlocked(fanRecipe, fresh),
            "the veil is Qinghe's and the fan is Miantai's, both known from the start");

        // ---- 串联：三层风，三件耗材各挡一层 ----
        Check(SkyIslandFieldcraftRules.Warmth(true, false, false) == SkyIslandWarmth.Shelter && SkyIslandFieldcraftRules.Warmth(false, true, true) == SkyIslandWarmth.Shelter
            && SkyIslandFieldcraftRules.Warmth(false, false, true) == SkyIslandWarmth.Lantern && SkyIslandFieldcraftRules.Warmth(false, false, false) == SkyIslandWarmth.None,
            "hearths, lamps and incense shelter; a lantern alone half-shelters");
        Check(SkyIslandFieldcraftRules.StepExposure(50f, 1, SkyIslandWarmth.Lantern, 1f) == 46f && SkyIslandFieldcraftRules.StepExposure(50f, 2, SkyIslandWarmth.Shelter, 1f) == 46f
            && SkyIslandFieldcraftRules.StepExposure(50f, 2, SkyIslandWarmth.Lantern, 1f) > 50f, "a lantern beats a breeze but not a gale");
        exposure = 0f;
        int lanternGaleSeconds = 0;
        while (!SkyIslandFieldcraftRules.NextChilled(false, exposure) && lanternGaleSeconds < 1000)
        { exposure = SkyIslandFieldcraftRules.StepExposure(exposure, 2, SkyIslandWarmth.Lantern, 1f); lanternGaleSeconds++; }
        Check(lanternGaleSeconds == 134, "a lantern in a gale only halves the chill: about 134 game seconds instead of 67");
        Check(SkyIslandFieldcraftRules.NightWind(true, SkyIslandLights.Target - 1) && !SkyIslandFieldcraftRules.NightWind(true, SkyIslandLights.Target)
            && !SkyIslandFieldcraftRules.NightWind(false, 0), "ten lights still the nights");
        Check(SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(true, SkyIslandLights.Target), false, false, false) == 0
            && SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(true, SkyIslandLights.Target), true, false, false) == 1,
            "after the tenth light the islands are calm at night and the bridges keep a breeze");
        Check(SkyIslandFieldcraftRules.CoreEased(2, true) == 1 && SkyIslandFieldcraftRules.CoreEased(1, true) == 1 && SkyIslandFieldcraftRules.CoreEased(0, true) == 0
            && SkyIslandFieldcraftRules.CoreEased(2, false) == 2, "the Windeater Core turns a gale into a breeze and nothing else");
        Check(Math.Abs(SkyIslandFieldcraftRules.StormPulseDamage(38f, true) - 38f * (1f - SkyIslandFieldcraftRules.CharmStormWard)) < 1e-4
            && SkyIslandFieldcraftRules.StormPulseDamage(38f, false) == 38f && SkyIslandFieldcraftRules.CharmStormWard > 0f && SkyIslandFieldcraftRules.CharmStormWard <= 0.5f,
            "the charm softens the Windeater's storm without making it harmless");
        string wardPercent = ((int)Math.Round(SkyIslandFieldcraftRules.CharmStormWard * 100.0)).ToString() + "%";
        Check(SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Charm).Contains(wardPercent) && SkyIslandJournal.Uses().Contains(wardPercent),
            "the charm's storm ward shown to players matches the rule");
        Check(SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Meal).Length > 0 && SkyIslandFieldcraftRules.BuffStarted(SkyIslandFieldBuff.Meal).Length == 0,
            "the bento explains its meal; the meal service speaks for itself");

        // ---- 串联：岛上的灯 ----
        SkyIslandLight[] lamps = SkyIslandLights.All;
        Check(lamps.Length == 7 && SkyIslandLights.HearthMarkers.Length == 3 && SkyIslandLights.Target == SkyIslandLights.HearthMarkers.Length + lamps.Length,
            "three hearths and seven windcrystal lamps make the ten lights");
        var lampIds = new HashSet<string>(StringComparer.Ordinal);
        var lampLetters = new HashSet<string>(StringComparer.Ordinal);
        var skyItemSet = new HashSet<int>(SkyIslandItemRules.AllTypeIds);
        foreach (SkyIslandLight light in lamps)
        {
            Check(light.Id.StartsWith(SkyIslandLights.IdPrefix, StringComparison.Ordinal) && lampIds.Add(light.Id)
                && SkyIslandLights.Find(light.Id) == light && SkyIslandLights.ForMarker(light.Marker) == light, "lamp id and marker are unique: " + light.Id);
            Check(journalKeys.Contains(light.Marker) && SkyIslandStoryService.RegionBit(light.Region) != 0,
                "a lamp hangs at a real device with a panel: " + light.Id);
            Check(SkyIslandLetters.Find(light.LetterId) != null && lampLetters.Add(light.LetterId), "each lamp answers its own letter: " + light.Id);
            int crystals = 0;
            foreach (SkyIslandIngredient input in light.Inputs)
            {
                Check(skyItemSet.Contains(input.TypeId) && input.Count >= 1, "a lamp is lit with sky island things: " + light.Id);
                if (input.TypeId == BossRushItemIds.SkyIslandQinglanWindcrystal) crystals += input.Count;
            }
            Check(crystals == 1 && light.Inputs.Length >= 2, "every lamp burns exactly one windcrystal plus something from its place: " + light.Id);
            Check(light.LitCn.Length > 0 && light.LitEn.Length > 0 && !ContainsCjk(light.LitEn), "a lamp says what its letter wished for: " + light.Id);
        }
        Check(Array.IndexOf(SkyIslandLights.HearthMarkers, "Search_A") >= 0 && Array.IndexOf(SkyIslandLights.HearthMarkers, "Search_C") >= 0
            && Array.IndexOf(SkyIslandLights.HearthMarkers, "POI_D") >= 0, "the hearths are Fuzhou's, Qinghe's and Miantai's fires");
        Check(SkyIslandLights.LitCount(fresh) == 3 && SkyIslandLights.LampsLit(fresh) == 0 && !SkyIslandLights.AllLit(fresh)
            && new List<string>(SkyIslandLights.UnlitMarkers(fresh)).Count == 7 && SkyIslandLights.LitCount(null) == 3, "a new save has only the three hearths");
        SkyIslandStoryData allLit = mail.Copy();
        var allNotes = new List<string>(allLit.discoveredNotes);
        foreach (SkyIslandLight light in lamps) allNotes.Add(light.Id);
        allNotes.AddRange(new[] { "Letter_09", "Letter_10", "Letter_11" });
        allLit.discoveredNotes = allNotes.ToArray();
        Check(SkyIslandLights.LitCount(allLit) == SkyIslandLights.Target && SkyIslandLights.AllLit(allLit)
            && new List<string>(SkyIslandLights.UnlitMarkers(allLit)).Count == 0, "seven lamps make ten lights");
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(allLit)) != null
            && SkyIslandJournal.NoteCount + SkyIslandLetters.Count + SkyIslandCrew.Count + SkyIslandItemRules.Keepsakes.Length + lamps.Length <= 256,
            "lamp notes fit the journal codec");
        SkyIslandLight boardwalkLamp = SkyIslandLights.Find("Light_E");
        var lampPack = new Dictionary<int, int> { { BossRushItemIds.SkyIslandQinglanWindcrystal, 1 }, { BossRushItemIds.SkyIslandDriftwood, 5 } };
        Func<int, int> lampCount = id => { int have; return lampPack.TryGetValue(id, out have) ? have : 0; };
        Check(SkyIslandLights.ChoiceLabel(boardwalkLamp, lampCount) == "点起风晶灯（晴岚风晶 1/1 · 浮木 3/3 · 云苔纤维 0/2）",
            "the lamp button shows have/need");
        List<SkyIslandIngredient> lampShort = SkyIslandFieldcraftRules.Missing(boardwalkLamp.Inputs, lampCount);
        Check(lampShort.Count == 1 && lampShort[0].TypeId == BossRushItemIds.SkyIslandCloudmossFiber && lampShort[0].Count == 2, "a lamp missing two fibres says so");
        Check(SkyIslandLights.LitCaption(boardwalkLamp, 4).EndsWith("（岛上的灯 4/10）", StringComparison.Ordinal), "a lit lamp counts the lights");
        string unlitChapter = SkyIslandLights.Chapter(fresh, id => "R:" + id);
        string litChapter = SkyIslandLights.Chapter(allLit, id => "R:" + id);
        Check(unlitChapter.StartsWith("岛上的灯 3/10", StringComparison.Ordinal) && unlitChapter.Contains("□ R:E") && unlitChapter.Contains("有一封信在盼着它")
            && !unlitChapter.Contains(SkyIslandLights.Capstone), "the journal lists the missing lamps and their costs");
        Check(litChapter.Contains("■ R:E") && litChapter.Contains("《" + SkyIslandLetters.Find("Letter_09").TitleCn + "》") && litChapter.Contains(SkyIslandLights.Capstone)
            && !litChapter.Contains("□ "), "a fully lit journal page names the letters and closes the child's count");
        Check(SkyIslandJournal.Overview(fresh, null).Contains("岛上的灯 3/10") && SkyIslandJournal.Overview(allLit, null).Contains("岛上的灯 10/10"),
            "the journal overview counts the lights");
        SkyIslandStoryData workshopLit = peaceful.Copy();
        workshopLit.discoveredNotes = new[] { "Light_G", "Light_S2", "Light_H" };
        for (int page = 1; page < SkyIslandCrew.Count; page++)
            Check(SkyIslandCrew.Page(page, workshopLit) != SkyIslandCrew.Page(page, peaceful), "the crew notice the lamp tied to their page: " + page);
        SkyIslandStoryData crewAllLit = peaceful.Copy();
        crewAllLit.discoveredNotes = allLit.discoveredNotes;
        Check(SkyIslandCrew.Page(0, crewAllLit).Contains("十盏灯") && !SkyIslandCrew.Page(0, peaceful).Contains("十盏灯"),
            "the helmsman counts ten lights only when they burn");

        // ---- 串联：航徽半价 ----
        Check(SkyIslandItemRules.ServicePrice(480, false) == 480 && SkyIslandItemRules.ServicePrice(480, true) == 240
            && SkyIslandItemRules.ServicePrice(61, true) == 31 && SkyIslandItemRules.ServicePrice(0, true) == 0 && SkyIslandItemRules.BadgeServiceRate == 0.5,
            "the badge halves island services, rounding up, and the text says half price");
        // 拍板：航徽在岛上使用还能拉缆绳回码头（每趟一次，搬人由会话执行）。
        Check(SkyIslandFieldcraftRules.BuffFor(BossRushItemIds.SkyIslandHomecomingBadge) == SkyIslandFieldBuff.Recall
            && SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Recall).Contains("每趟一次")
            && SkyIslandFieldcraftRules.BuffStarted(SkyIslandFieldBuff.Recall).Length == 0
            && SkyIslandJournal.Uses().Contains("拉缆绳回登云码头") && SkyIslandFieldcraftRules.RecallArrived.Contains("登云码头"),
            "the badge pulls you back to the dock once per raid, and the item, caption and journal all say so");

        // ---- 拍板：岛上物资池的单件价值上限（皇冠与神秘钥匙出池，CR-2026-09-11-001） ----
        Check(!SkyIslandLootTables.AllowedInPool(21593218) && !SkyIslandLootTables.AllowedInPool(253228) && !SkyIslandLootTables.AllowedInPool(151675)
            && SkyIslandLootTables.AllowedInPool(66666) && SkyIslandLootTables.AllowedInPool(55898) && SkyIslandLootTables.AllowedInPool(7196)
            && SkyIslandLootTables.AllowedInPool(0) && SkyIslandLootTables.MaxPoolItemValue >= 60000 && SkyIslandLootTables.MaxPoolItemValue < 150000,
            "the crown and the mysterious keys stay out of island pools; blueprints and gold badges stay in");

        // ---- 串联：没有只为卖钱的东西（每件都有来路，也都有岛上的用处） ----
        string uses = SkyIslandJournal.Uses();
        foreach (int typeId in SkyIslandItemRules.AllTypeIds)
            Check(uses.Contains("· " + SkyIslandItemRules.NameCn(typeId) + " → "), "every sky island item has a line saying what it is for: " + typeId);
        var consumedBySomething = new HashSet<int>();
        foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes)
            foreach (SkyIslandIngredient input in recipe.Inputs) consumedBySomething.Add(input.TypeId);
        foreach (SkyIslandLight light in lamps)
            foreach (SkyIslandIngredient input in light.Inputs) consumedBySomething.Add(input.TypeId);
        foreach (int material in SkyIslandFieldcraftRules.MaterialTypeIds)
            Check(consumedBySomething.Contains(material), "no island material is a dead end: " + material);
        Check(consumedBySomething.Contains(BossRushItemIds.SkyIslandWindLantern) && consumedBySomething.Contains(BossRushItemIds.SkyIslandWindwardIncense),
            "crafted lanterns and incense also feed the lamps");
        foreach (int typeId in new[] { BossRushItemIds.SkyIslandWindLantern, BossRushItemIds.SkyIslandWindwardIncense,
            BossRushItemIds.SkyIslandQinglanCharm, BossRushItemIds.SkyIslandHomecomingBento, BossRushItemIds.SkyIslandGnatZapper,
            BossRushItemIds.SkyIslandSmokeFan })
            Check(SkyIslandFieldcraftRules.BuffFor(typeId) != SkyIslandFieldBuff.None, "island consumables do something on the isles: " + typeId);
        var sources = new HashSet<int>(obtainable);
        foreach (SkyIslandKeepsake keepsake in SkyIslandItemRules.Keepsakes) sources.Add(keepsake.TypeId);
        foreach (SkyIslandLootTier tier in tiers)
            for (int i = 0; i < 100; i++)
            {
                int crateExtra = SkyIslandItemRules.IslandExtraFor(tier, (i + 0.5) / 100);
                if (crateExtra != 0) sources.Add(crateExtra);
            }
        foreach (int typeId in SkyIslandItemRules.AllTypeIds)
            Check(sources.Contains(typeId), "every sky island item has a way to get it: " + typeId);

        // ---- 串联：点起来的灯经剧情服务写进本槽手记，离岛重进还亮着 ----
        story = Open(12);
        Check(story.RecordNote("Light_S3", out noteMessage) && SkyIslandLights.Lit(story.Current, "Light_S3")
            && SkyIslandLights.LitCount(story.Current) == 4, "a lit lamp is written to the journal");
        Check(!story.RecordNote("Light_S3", out noteMessage) && !story.RecordNote("Light_Z", out noteMessage),
            "a lamp is lit once; unknown lamps never reach the save");
        Check(story.DescribeNpc("sky_fuzhou").IndexOf("熔晶炉", StringComparison.Ordinal) < 0, "Fuzhou keeps quiet about the furnace before the star lamp");
        story.RecordEncounterCleared("G"); story.RecordEncounterCleared("G_02"); Apply(story, SkyIslandStoryAction.RepairStarLamp);
        Check(story.DescribeNpc("sky_fuzhou").Contains("熔晶炉"), "once the star lamp is lit Fuzhou tells you about the windcrystal lamps");
        Check(story.TryClose(), "lamp writes flush on close");
        story = Open(12);
        Check(SkyIslandLights.Lit(story.Current, "Light_S3") && SkyIslandLetters.CollectedCount(story.Current) == 0
            && SkyIslandCrew.ReadCount(story.Current) == 0 && SkyIslandItemRules.GrantedCount(story.Current) == 0,
            "lamps survive re-entry and are not mistaken for other journal entries");
        story.Close();
        Check(SavesSystem.Subscribers == 0, "lamp sessions released events");

        // ---- 内容批次四：云蚋——放生的蛙卵经剧情服务写进本槽手记，居民、名册与手记都有回音 ----
        story = Open(13);
        Check(story.DescribeNpc("sky_qinghe").Contains("纱笠") && !story.DescribeNpc("sky_qinghe").Contains("蛙叫"),
            "Qinghe talks about her veil before any frogs are back");
        Check(!story.RecordNote("Frog_4", out noteMessage) && story.RecordNote("Frog_1", out noteMessage)
            && SkyIslandMosquitoRules.FrogsReleased(story.Current) == 1, "frogspawn notes are registered journal ids; unknown ones never reach the save");
        Check(story.DescribeNpc("sky_qinghe").Contains("蛙卵"), "Qinghe hears that someone brought frogspawn back");
        Check(story.RecordNote("Frog_2", out noteMessage) && story.RecordNote("Frog_3", out noteMessage) && SkyIslandMosquitoRules.FrogsComplete(story.Current)
            && !story.RecordNote("Frog_3", out noteMessage), "three clutches fill the pool, each recorded once");
        Check(story.DescribeNpc("sky_qinghe").Contains("蛙叫") && SkyIslandJournal.Overview(story.Current, null).Contains("蛙鸣池的蛙 3/3"),
            "the garden and the journal notice the frogs");
        Check(story.DescribeNpc("sky_weibai").IndexOf("灭蚊灯", StringComparison.Ordinal) < 0, "Weibai says nothing about zappers before any lamp is lit");
        Check(story.RecordNote("Light_E", out noteMessage) && story.DescribeNpc("sky_weibai").Contains("再亮一盏"), "one lamp and Weibai is listening for the tune");
        Check(story.RecordNote("Light_G", out noteMessage) && story.DescribeNpc("sky_weibai").Contains("灭蚊灯的调子"), "two lamps and she hands the zapper to Fuzhou");
        Check(story.TryClose(), "frog and lamp writes flush on close");
        story = Open(13);
        Check(SkyIslandMosquitoRules.FrogsComplete(story.Current) && SkyIslandLights.LampsLit(story.Current) == 2
            && SkyIslandLetters.CollectedCount(story.Current) == 0 && SkyIslandCrew.ReadCount(story.Current) == 0,
            "frogs survive re-entry and are not mistaken for lamps, letters or crew pages");
        story.Close();
        story = Open(14);
        Apply(story, SkyIslandStoryAction.FindOldLetter);
        Apply(story, SkyIslandStoryAction.FindRouteChart);
        Apply(story, SkyIslandStoryAction.ReconcileZheling);
        Check(story.DescribeNpc("sky_zheling").Contains("捧一团蛙卵"), "reconciled Zheling points you to the frogspawn in the temple pool");
        Check(story.RecordNote("Frog_1", out noteMessage) && story.RecordNote("Frog_2", out noteMessage) && story.RecordNote("Frog_3", out noteMessage)
            && story.DescribeNpc("sky_zheling").Contains("回蛙鸣池去了"), "and notices when they have all gone home");
        story.Close();
        Check(SavesSystem.Subscribers == 0, "frog sessions released events");
        SkyIslandStoryData frogsHome = crewAllLit.Copy();
        var frogNotes = new List<string>(frogsHome.discoveredNotes) { "Frog_1", "Frog_2", "Frog_3" };
        if (!frogNotes.Contains("Letter_04")) frogNotes.Add("Letter_04");
        frogsHome.discoveredNotes = frogNotes.ToArray();
        Check(SkyIslandCrew.Page(3, frogsHome).Contains("蛙鸣池又有蛙叫") && !SkyIslandCrew.Page(3, crewAllLit).Contains("蛙鸣池又有蛙叫"),
            "the ship's doctor notices the frogs are back");
        Check(SkyIslandJournal.Letters(frogsHome).Contains("池子里的青蛙会替那个孩子数灯") && !SkyIslandJournal.Letters(mail).Contains("池子里的青蛙会替那个孩子数灯"),
            "the letter to the frogs in the pool gets its answer only once the pool is full");
        Check(SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(frogsHome)) != null
            && SkyIslandJournal.NoteCount + SkyIslandLetters.Count + SkyIslandCrew.Count + SkyIslandItemRules.Keepsakes.Length + lamps.Length
               + SkyIslandMosquitoRules.FrogTarget <= 256, "frog notes fit the journal codec");

        // ---- 批次三：英文界面没有残留中文 ----
        L10n.IsChinese = false;
        string englishThree = SkyIslandFieldcraftRules.PackSummary(countInPack) + SkyIslandFieldcraftRules.PackSummary(null)
            + SkyIslandFieldcraftRules.MissingMessage(shortBy) + SkyIslandFieldcraftRules.CraftedMessage(lanternRecipe)
            + SkyIslandFieldcraftRules.HarvestCaption(firstRoll) + SkyIslandFieldcraftRules.HarvestCaption(null)
            + SkyIslandFieldcraftRules.CharmAlreadyWorn + SkyIslandFieldcraftRules.OffIsland + SkyIslandFieldcraftRules.WindExplain(true)
            + SkyIslandFieldcraftRules.ExposureWarning + SkyIslandFieldcraftRules.ChillStarted + SkyIslandFieldcraftRules.ChillEnded;
        foreach (SkyIslandCraftStation station in new[] { SkyIslandCraftStation.Dock, SkyIslandCraftStation.Stove, SkyIslandCraftStation.Mortar })
            englishThree += SkyIslandFieldcraftRules.StationName(station) + SkyIslandFieldcraftRules.StationChoice(station) + SkyIslandFieldcraftRules.StationIntro(station);
        foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes) englishThree += SkyIslandFieldcraftRules.RecipeLabel(recipe, countInPack);
        foreach (SkyIslandGatherKind kind in gatherKinds) englishThree += SkyIslandFieldcraftRules.GatherLabel(kind);
        foreach (SkyIslandFieldBuff buff in new[] { SkyIslandFieldBuff.Lantern, SkyIslandFieldBuff.Incense, SkyIslandFieldBuff.Charm })
            englishThree += SkyIslandFieldcraftRules.UsageText(buff) + SkyIslandFieldcraftRules.BuffStarted(buff) + SkyIslandFieldcraftRules.BuffLow(buff) + SkyIslandFieldcraftRules.BuffEnded(buff);
        foreach (int typeId in SkyIslandItemRules.AllTypeIds) englishThree += SkyIslandItemRules.Name(typeId);
        // 串联新增的文案：灯、手记页、锁住的配方、剧情加成的原因、便当与噬风之核。
        englishThree += SkyIslandJournal.Uses() + SkyIslandLights.Chapter(allLit, id => "E") + SkyIslandLights.Chapter(fresh, null)
            + SkyIslandLights.LitCaption(boardwalkLamp, 5) + SkyIslandLights.ChoiceLabel(boardwalkLamp, lampCount) + SkyIslandLights.AlreadyLit
            + SkyIslandFieldcraftRules.CoreEasesGale + SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Meal)
            + SkyIslandCrew.Page(0, crewAllLit) + SkyIslandCrew.Page(1, workshopLit) + SkyIslandCrew.Page(2, workshopLit) + SkyIslandCrew.Page(3, workshopLit)
            + SkyIslandItemRules.BadgeDiscountNote + SkyIslandJournal.Overview(allLit, null)
            + SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Recall) + SkyIslandFieldcraftRules.RecallArrived + SkyIslandFieldcraftRules.RecallSpent
            + SkyIslandFieldcraftRules.RecallNotReady + SkyIslandFieldcraftRules.RecallFailed
            // 内容批次四：三件对策的使用说明、船医与信的回音。
            + SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Zapper) + SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Fan)
            + SkyIslandFieldcraftRules.UsageText(SkyIslandFieldBuff.Soothe) + SkyIslandCrew.Page(3, frogsHome) + SkyIslandJournal.Letters(frogsHome)
            + SkyIslandJournal.Overview(frogsHome, null);
        foreach (SkyIslandLight light in lamps) englishThree += light.LitLine;
        for (int i = 0; i < SkyIslandLights.HearthMarkers.Length; i++) englishThree += SkyIslandLights.HearthName(i);
        foreach (SkyIslandRecipe recipe in SkyIslandFieldcraftRules.Recipes)
            englishThree += SkyIslandFieldcraftRules.LockedLabel(recipe) + SkyIslandFieldcraftRules.LockedMessage(recipe);
        foreach (SkyIslandGatherNode node in gatherNodes) englishThree += SkyIslandFieldcraftRules.StoryBonusReason(node, restored) ?? string.Empty;
        englishThree += SkyIslandFieldcraftRules.HarvestCaption(firstRoll, SkyIslandFieldcraftRules.StoryBonusReason(SkyIslandFieldcraftRules.FindNode("E1"), restored));
        L10n.IsChinese = true;
        Check(!ContainsCjk(englishThree), "batch-three text has an English half everywhere");

        // ---- 内容批次四：云蚋的纯规则与躲闪离线模拟（生产 SkyIslandMosquitoRules / SkyIslandGnatMotor 原样执行） ----
        SkyIslandMosquitoRegression.Run(Check);
        SkyIslandGnatDodgeSimulation.Run(Check);
        Console.WriteLine("PASS SkyIslandStory: " + checks + " assertions (production rules, codec, store, coordinator and save recovery; host substitutes)");
    }
}

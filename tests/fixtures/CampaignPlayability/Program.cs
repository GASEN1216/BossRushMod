using System;
using BossRush;

internal static class Program
{
    private static int checks;
    private static readonly CharacterMainControl Player = new CharacterMainControl { IsMainCharacter = true };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static void Arm(string id)
    {
        CampaignObjectiveTracker.ResetSession();
        CampaignObjectiveCollector.ResetStaticCaches();
        CampaignProgressService.Active = id;
        CampaignProgressService.Notifications = 0;
        CampaignProgressService.Reject = false;
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        CharacterMainControl.Main = Player;
        Player.Health = new Health();
        ModBehaviour.Instance = new ModBehaviour { Wave = 1, bossRushArenaActive = true, IsActive = true };
        CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.GetChapter(id).Mode);
        Check(CampaignObjectiveTracker.IsArmed, id + " can start");
    }

    private static void Hit(float damage)
    {
        CampaignObjectiveCollector.OnGlobalHurt(new Health { IsMainCharacterHealth = true },
            new DamageInfo { finalDamage = damage });
    }

    private static void Kill(bool melee, bool boss, bool marked, bool player, Teams team = Teams.wolf)
    {
        ModBehaviour.Instance.modeFActive = marked;
        ModBehaviour.Instance.modeFState.BountyMarksByCharacterId[1] = marked ? 1 : 0;
        CampaignObjectiveCollector.OnGlobalDead(new Health {
            Character = new CharacterMainControl { isBossCharacter = boss, Marked = marked, Team = team }
        }, new DamageInfo { fromCharacter = player ? Player : new CharacterMainControl(), fromWeaponItemID = melee ? 1 : 2 });
    }

    private static void Main()
    {
        Check(CampaignContentCatalog.Source == "Json", "must execute deployed chapter table, not fallback");
        Check(CampaignContentCatalog.ContentSignature == CampaignContentCatalog.ExpectedContentSignature, "table and fallback agree");

        foreach (float damage in new[] { 0f, -1f, float.NaN })
        {
            Arm("ch1");
            Hit(damage);
            CampaignObjectiveTracker.ReportStandardClear();
            Check(CampaignProgressService.Notifications == 1, "non-damaging hit must preserve flawless: " + damage);
        }
        Arm("ch1");
        Hit(0.01f);
        CampaignObjectiveTracker.ReportStandardClear();
        Check(CampaignProgressService.Notifications == 0, "positive damage before threshold fails flawless");
        Arm("ch1");
        ModBehaviour.Instance.Wave = 2;
        Hit(1f);
        CampaignObjectiveTracker.ReportWaveReached(3);
        CampaignObjectiveTracker.ReportStandardClear();
        Check(CampaignProgressService.Notifications == 0, "threshold wave remains part of flawless requirement");
        Arm("ch1");
        ModBehaviour.Instance.Wave = 3;
        CampaignObjectiveTracker.ReportWaveReached(3);
        Hit(1f);
        CampaignObjectiveTracker.ReportStandardClear();
        CampaignObjectiveTracker.ReportStandardClear();
        Check(CampaignProgressService.Notifications == 1, "later damage allowed and completion is idempotent");

        Arm("ch2");
        Check(CampaignObjectiveTracker.Progress.Count == 2 && CampaignObjectiveTracker.Progress[0].Def.Kind == CampaignObjectiveKind.ReachWave,
            "base-scope garden objective stays out of the in-run tracker");
        Check(CampaignObjectiveTracker.NeedsMeleeStarterKit(), "active melee contract requests its starter tool");
        CampaignProgressService.State = CampaignChapterState.ReadyToDeliver;
        Check(!CampaignObjectiveTracker.NeedsMeleeStarterKit(), "ready contract does not grant extra starter gear");
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        CampaignObjectiveTracker.ReportWaveReached(5);
        for (int i = 0; i < 5; i++) Kill(true, false, false, false);
        Check(CampaignProgressService.Notifications == 0, "NPC melee kills do not count");
        for (int i = 0; i < 5; i++) Kill(true, false, false, true);
        Check(CampaignProgressService.Notifications == 1, "ch2 reachable through player melee and wave events");

        Arm("ch3");
        Check(!CampaignObjectiveTracker.NeedsMeleeStarterKit(), "other modes do not request a melee starter");
        for (int i = 0; i < 8; i++) Kill(false, true, false, true, Teams.player);
        for (int i = 0; i < 8; i++) Kill(false, true, false, true, Teams.middle);
        Check(CampaignObjectiveTracker.Progress[0].Current == 0, "allies and neutrals cannot farm the chapter");
        for (int i = 0; i < 7; i++) Kill(false, true, false, true);
        CampaignObjectiveTracker.Tick(600f);
        Check(CampaignProgressService.Notifications == 0, "waiting does not replace fighting");
        Kill(false, true, false, true);
        Check(CampaignProgressService.Notifications == 1, "eight hostile bosses finish ch3 without idle time");
        Kill(false, true, false, true);
        Check(CampaignObjectiveTracker.Progress[0].Current == 8, "completed counters stay at target");

        Arm("ch4");
        for (int i = 0; i < 3; i++) Kill(false, true, false, true);
        CampaignObjectiveTracker.ReportExtract();
        Check(CampaignProgressService.Notifications == 0, "unmarked kills do not satisfy bounty");
        for (int i = 0; i < 3; i++) Kill(false, true, true, true);
        Check(CampaignProgressService.Notifications == 1, "ch4 reachable through marked kills and extraction");

        Arm("ch5");
        CampaignObjectiveTracker.ReportWaveReached(4);
        CampaignObjectiveTracker.ReportExtract();
        Check(CampaignProgressService.Notifications == 0, "ch5 wave gate is five: wave four plus extraction is not enough");
        Arm("ch5");
        CampaignObjectiveTracker.ReportWaveReached(5);
        Check(CampaignProgressService.Notifications == 0, "ch5 requires extraction");
        CampaignObjectiveTracker.ReportExtract();
        Check(CampaignProgressService.Notifications == 1, "ch5 reachable through wave five and extraction");

        Arm("ch6");
        CampaignProgressService.Reject = true;
        CampaignObjectiveTracker.ReportFinalBossKill();
        Check(CampaignProgressService.Notifications == 0, "failed write cannot report completion");
        CampaignProgressService.Reject = false;
        CampaignObjectiveTracker.ReportFinalBossKill();
        Check(CampaignProgressService.Notifications == 1, "ch6 completion retries after storage rejection");

        Arm("ch1");
        Hit(1f);
        CampaignObjectiveTracker.ResetSession();
        CampaignObjectiveTracker.EnsureArmedFor("standard");
        CampaignObjectiveTracker.ReportStandardClear();
        Check(CampaignProgressService.Notifications == 1, "new run discards prior failure");
        CampaignObjectiveTracker.EnsureArmedFor("modeE");
        Check(!CampaignObjectiveTracker.IsArmed, "different mode disarms old contract");
        CampaignObjectiveTracker.EnsureArmedFor("standard");
        Check(!CampaignObjectiveTracker.IsArmed, "ready-to-deliver chapter cannot rearm");
        CheckBridges();
        CheckNotes();
        CheckQuestTable();
        FinalBossRegression.Run(Check);
        Console.WriteLine("CampaignPlayability: PASS (" + checks + " checks)");
    }

    private static void CheckQuestTable()
    {
        // ---- ID 映射：ch1..ch6 = 590101..590106，往返一致，越界 0 ----
        for (int order = 1; order <= 6; order++)
        {
            int id = CampaignQuestTable.QuestIdForOrder(order);
            Check(id == 590100 + order, "quest id for chapter " + order);
            Check(CampaignQuestTable.OrderForQuestId(id) == order, "quest id round trip " + order);
        }
        Check(CampaignQuestTable.QuestIdForOrder(0) == 0 && CampaignQuestTable.QuestIdForOrder(7) == 0, "out-of-range order yields 0");
        Check(CampaignQuestTable.OrderForQuestId(590100) == 0 && CampaignQuestTable.OrderForQuestId(590107) == 0 && CampaignQuestTable.OrderForQuestId(590001) == 0,
            "out-of-range quest id yields 0 (sky island prelude is not a chapter)");
        Check(CampaignQuestTable.JeffGiverId == 1, "giver is official Jeff");
        Check(CampaignQuestTable.NameKey("ch1") == "BossRush_Campaign_ch1_Name" && CampaignQuestTable.DescriptionKey("ch6") == "BossRush_Campaign_ch6_Description",
            "quest localization keys");

        // ---- CanOffer / CanDeliver 穷举 ----
        foreach (CampaignChapterState state in (CampaignChapterState[])Enum.GetValues(typeof(CampaignChapterState)))
            foreach (bool canWrite in new[] { true, false })
                foreach (bool another in new[] { true, false })
                {
                    bool expected = state == CampaignChapterState.Available && canWrite && !another;
                    Check(CampaignQuestTable.CanOffer(state, canWrite, another) == expected, "CanOffer " + state + " " + canWrite + " " + another);
                    foreach (bool baseDone in new[] { true, false })
                    {
                        bool expectedDeliver = state == CampaignChapterState.ReadyToDeliver && canWrite && baseDone;
                        Check(CampaignQuestTable.CanDeliver(state, canWrite, baseDone) == expectedDeliver, "CanDeliver " + state + " " + canWrite + " " + baseDone);
                    }
                }

        // ---- IsObjectiveDone：局内目标 vs 基地侧目标 ----
        CampaignChapterDef ch2 = CampaignContentCatalog.GetChapter("ch2");
        CampaignObjectiveDef garden = ch2.Objectives[0];
        CampaignObjectiveDef wave = ch2.Objectives[1];
        Check(garden.IsBaseScope && !wave.IsBaseScope, "ch2 objective scopes");
        Check(ch2.Objectives.Count == 3 && CampaignContentCatalog.GetChapter("ch3").Objectives[1].Kind == CampaignObjectiveKind.TrophyDisplayed,
            "ch2 has garden objective, ch3 has trophy objective");
        var satisfied = new CampaignObjectiveProgress { Def = wave, Current = 5 };
        var partial = new CampaignObjectiveProgress { Def = wave, Current = 2 };
        var failed = new CampaignObjectiveProgress { Def = wave, Current = 5, Failed = true };
        Check(CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.Completed, false, null, false), "completed chapter: run objective done");
        Check(CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.ReadyToDeliver, false, null, false), "ready chapter: run objective done without tracker");
        Check(!CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.ContractActive, false, satisfied, false), "unarmed run objective is not done after reload");
        Check(CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.ContractActive, true, satisfied, false), "armed satisfied run objective done");
        Check(!CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.ContractActive, true, partial, false), "partial progress not done");
        Check(!CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.ContractActive, true, failed, false), "failed progress not done");
        Check(!CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.Available, true, satisfied, false)
            && !CampaignQuestTable.IsObjectiveDone(wave, CampaignChapterState.Locked, true, satisfied, false), "unaccepted chapter objectives never done");
        Check(!CampaignQuestTable.IsObjectiveDone(garden, CampaignChapterState.ReadyToDeliver, true, null, false), "ready chapter: base objective still needs the base fact");
        Check(CampaignQuestTable.IsObjectiveDone(garden, CampaignChapterState.ContractActive, false, null, true), "base fact satisfies base objective regardless of run");
        Check(CampaignQuestTable.IsObjectiveDone(garden, CampaignChapterState.Completed, false, null, false), "completed chapter: base objective done");
        Check(!CampaignQuestTable.IsObjectiveDone(null, CampaignChapterState.Completed, true, satisfied, true), "null objective never done");

        // ---- FindProgress 按定义引用而不是下标 ----
        CampaignProgressService.Active = "ch2";
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        CampaignObjectiveTracker.ResetSession();
        CampaignObjectiveTracker.EnsureArmedFor("modeD");
        Check(CampaignQuestTable.FindProgress(CampaignObjectiveTracker.Progress, wave) != null
            && CampaignQuestTable.FindProgress(CampaignObjectiveTracker.Progress, garden) == null, "progress lookup by definition reference");

        // ---- DescribeObjective 文案与目标行同源 ----
        L10n.IsChinese = true;
        Check(CampaignQuestTable.DescribeObjective(wave, null, false, false) == "白手起家打到第 5 波", "unarmed run objective shows no number");
        Check(CampaignQuestTable.DescribeObjective(wave, partial, false, false) == "白手起家打到第 5 波 (2/5)", "partial progress shows x/y");
        Check(CampaignQuestTable.DescribeObjective(wave, failed, false, false).Contains("本局已失败"), "failed progress says failed this run");
        Check(CampaignQuestTable.DescribeObjective(wave, null, true, false).Contains("已达成"), "settled objective says done");
        Check(CampaignQuestTable.DescribeObjective(garden, null, false, false) == "基地：在基地建好菜地", "base objective is prefixed and has no number");
        Check(CampaignQuestTable.DescribeObjective(garden, null, false, true).Contains("已达成"), "base fact marks base objective done");
        L10n.IsChinese = false;
        Check(CampaignQuestTable.DescribeObjective(garden, null, false, false) == "Base: Build the garden at base", "english base objective");
        Check(CampaignQuestTable.GetModeDisplayName(CampaignContentCatalog.ModeZombie) == "Zombie Mode", "zombie mode display name is the mode's own name");
        Check(CampaignQuestTable.DescribeObjectiveHint(wave, CampaignContentCatalog.ModeModeD, false) == "Go to: From Scratch", "run objective hint points at the mode");
        Check(CampaignQuestTable.DescribeObjectiveHint(wave, CampaignContentCatalog.ModeModeD, true) == null, "settled objective has no hint");
        L10n.IsChinese = true;

        // ---- 基地侧事实注册表：无提供者 = 未完成，异常 = 未完成，可撤销 ----
        CampaignBaseObjectives.ResetStaticCaches();
        Check(!CampaignBaseObjectives.IsDone(CampaignObjectiveKind.GardenBuilt), "no provider means not done");
        Check(!CampaignBaseObjectives.AllDone(ch2) && CampaignBaseObjectives.FirstPending(ch2) == garden, "ch2 blocked on garden");
        Check(CampaignBaseObjectives.AllDone(CampaignContentCatalog.GetChapter("ch1")), "chapter without base objectives is all done");
        CampaignBaseObjectives.RegisterProvider(CampaignObjectiveKind.GardenBuilt, () => true);
        Check(CampaignBaseObjectives.IsDone(CampaignObjectiveKind.GardenBuilt) && CampaignBaseObjectives.AllDone(ch2) && CampaignBaseObjectives.DoneBits(ch2) == 1,
            "provider true completes garden objective");
        CampaignBaseObjectives.RegisterProvider(CampaignObjectiveKind.GardenBuilt, () => { throw new Exception("probe"); });
        Check(!CampaignBaseObjectives.IsDone(CampaignObjectiveKind.GardenBuilt), "throwing provider counts as not done");
        CampaignBaseObjectives.UnregisterProvider(CampaignObjectiveKind.GardenBuilt);
        Check(!CampaignBaseObjectives.HasProvider(CampaignObjectiveKind.GardenBuilt), "provider can be withdrawn");

        // ---- 事实指纹：状态 / 进度 / 基地位任一变化都变 ----
        int stampA = CampaignQuestTable.ComputeStateStamp(CampaignChapterState.ContractActive, true, new[] { partial }, 0);
        int stampB = CampaignQuestTable.ComputeStateStamp(CampaignChapterState.ContractActive, true, new[] { satisfied }, 0);
        int stampC = CampaignQuestTable.ComputeStateStamp(CampaignChapterState.ContractActive, true, new[] { partial }, 1);
        int stampD = CampaignQuestTable.ComputeStateStamp(CampaignChapterState.ReadyToDeliver, true, new[] { partial }, 0);
        Check(stampA != stampB && stampA != stampC && stampA != stampD, "state stamp reacts to progress, base bits and state");
    }

    private static void CheckBridges()
    {
        Arm("ch1");
        var owner = ModBehaviour.Instance;
        CampaignObjectiveTracker.ResetSession();
        owner.IsActive = false;
        owner.TickCampaignModeBridge(1f);
        Check(!CampaignObjectiveTracker.IsArmed, "arena lobby does not start flawless challenge");
        owner.IsActive = true;
        owner.TickCampaignModeBridge(1f);
        Check(CampaignObjectiveTracker.IsArmed, "standard sign activates tracking");
        owner.Wave = 3;
        owner.TickCampaignModeBridge(1f);
        owner.NotifyCampaignStandardCleared();
        Check(CampaignProgressService.Notifications == 1, "real standard bridge completes contract");
        Arm("ch2"); owner = ModBehaviour.Instance;
        owner.modeDActive = true; owner.ModeDWaveIndex = 5;
        owner.TickCampaignModeBridge(1f);
        for (int i = 0; i < 5; i++) Kill(true, false, false, true);
        Check(CampaignProgressService.Notifications == 1, "mode D bridge supplies wave five");
        Arm("ch4"); owner = ModBehaviour.Instance;
        for (int i = 0; i < 3; i++) Kill(false, true, true, true);
        owner.NotifyCampaignModeFExtracted();
        Check(CampaignProgressService.Notifications == 1, "mode F mark lookup and extract bridge complete together");
        Arm("ch5"); owner = ModBehaviour.Instance;
        owner.zombieModeRunState = new ZombieRun { LifecyclePhase = 1, CurrentWave = 5 };
        owner.TickCampaignModeBridge(1f);
        owner.NotifyCampaignZombieExtracted();
        Check(CampaignProgressService.Notifications == 1, "zombie wave and extraction bridge complete together");
        Arm("ch2"); owner = ModBehaviour.Instance; owner.modeDActive = true; owner.ModeDWaveIndex = 5;
        owner.TickCampaignModeBridge(1f);
        Player.Health.IsDead = true;
        owner.TickCampaignModeBridge(1f);
        Check(!CampaignObjectiveTracker.IsArmed, "death disarms tracking before mode flags clear");
        Player.Health.IsDead = false;
        owner.TickCampaignModeBridge(1f);
        Check(CampaignObjectiveTracker.Progress[0].Current == 5, "same mode rearm reobserves its current wave");
        Arm("ch1"); owner = ModBehaviour.Instance; owner.modeGActive = true;
        owner.TickCampaignModeBridge(1f);
        Check(owner.ResolveCampaignCurrentMode() == null, "Mode G cannot masquerade as standard");
    }

    private static void CheckNotes()
    {
        var index = new Duckov.NoteIndexs.NoteIndex();
        Duckov.NoteIndexs.NoteIndex.Instance = index;
        CampaignProgressService.Clues.Clear();
        string key = CampaignNoteBridge.BuildNoteKey("clue_ch1");
        index.UnlockedNotes.Add(key);
        CampaignNoteBridge.EnsureNotesRegistered();
        Check(index.Notes.Count == 6 && index.Index.Count == 6 && !index.UnlockedNotes.Contains(key),
            "notes list and lookup register, stale mirror relocks");
        CampaignProgressService.Clues.Add("clue_ch1");
        index.Index.Clear();
        CampaignNoteBridge.EnsureNotesRegistered();
        Check(index.Notes.Count == 6 && index.Index.Count == 6 && index.UnlockedNotes.Contains(key),
            "existing list repairs missing dictionary and restores earned evidence");
        CampaignNoteBridge.UnlockClue("clue_ch2");
        Check(index.UnlockedNotes.Count == 1, "late dialogue cannot grant an unearned clue");
        CampaignProgressService.Clues.Clear();
        CampaignPersistence.HasWriteBarrier = true;
        CampaignNoteBridge.EnsureNotesRegistered();
        Check(index.UnlockedNotes.Contains(key), "unreadable save must not revoke existing evidence");
        CampaignPersistence.HasWriteBarrier = false;
        CampaignNoteBridge.EnsureNotesRegistered();
        Check(!index.UnlockedNotes.Contains(key), "authoritative slot reset removes old mirror");
        Duckov.NoteIndexs.NoteIndex.Instance = null;
        CampaignNoteBridge.UnlockClue("clue_ch1");
    }
}

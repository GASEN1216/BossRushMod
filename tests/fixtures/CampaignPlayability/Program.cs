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
        Check(CampaignProgressService.Notifications == 0, "ch5 requires extraction");
        CampaignObjectiveTracker.ReportExtract();
        Check(CampaignProgressService.Notifications == 1, "ch5 reachable through wave four and extraction");

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
        FinalBossRegression.Run(Check);
        Console.WriteLine("CampaignPlayability: PASS (" + checks + " checks)");
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
        owner.zombieModeRunState = new ZombieRun { LifecyclePhase = 1, CurrentWave = 4 };
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

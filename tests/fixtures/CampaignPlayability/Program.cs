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
        ModBehaviour.Instance = new ModBehaviour { Wave = 1 };
        CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.GetChapter(id).Mode);
        Check(CampaignObjectiveTracker.IsArmed, id + " can start");
    }

    private static void Hit(float damage)
    {
        CampaignObjectiveCollector.OnGlobalHurt(new Health { IsMainCharacterHealth = true },
            new DamageInfo { finalDamage = damage });
    }

    private static void Kill(bool melee, bool boss, bool marked, bool player)
    {
        CampaignObjectiveCollector.OnGlobalDead(new Health {
            Character = new CharacterMainControl { isBossCharacter = boss, Marked = marked }
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
        CampaignObjectiveTracker.ReportWaveReached(5);
        for (int i = 0; i < 5; i++) Kill(true, false, false, false);
        Check(CampaignProgressService.Notifications == 0, "NPC melee kills do not count");
        for (int i = 0; i < 5; i++) Kill(true, false, false, true);
        Check(CampaignProgressService.Notifications == 1, "ch2 reachable through player melee and wave events");

        Arm("ch3");
        for (int i = 0; i < 8; i++) Kill(false, true, false, true);
        CampaignObjectiveTracker.Tick(599f);
        Check(CampaignProgressService.Notifications == 0, "ch3 still needs full survival duration");
        CampaignObjectiveTracker.Tick(0f);
        CampaignObjectiveTracker.Tick(-1f);
        Check(CampaignProgressService.Notifications == 0, "paused clock cannot advance survival");
        CampaignObjectiveTracker.Tick(1f);
        Check(CampaignProgressService.Notifications == 1, "ch3 reachable after ten minutes and eight bosses");

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
        Console.WriteLine("CampaignPlayability: PASS (" + checks + " checks)");
    }
}

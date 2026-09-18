using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
using Duckov.Economy;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception("FAIL " + label); }
    private static List<SkyIslandStoryPresentation.Choice> Empty() { return new List<SkyIslandStoryPresentation.Choice>(); }
    private static void Main()
    {
        PageSubmission();
        ServicesAndHints();
        PuzzleRecovery();
        RewardPlacement();
        GroundRing();
        AudioStop();
        LocalizationRegression.Run(Check);
        Check(HUDManager.Tokens.Count == 0 && ZombieModeUIHelper.Leases == 0, "all modal and HUD owners released");
        Console.WriteLine("PASS SkyIslandInteraction assertions=" + checks + " (production control flow; Unity, physics and audio substituted)");
    }

    private static void PageSubmission()
    {
        var ui = new SkyIslandStoryPresentation();
        int actions = 0;
        var choices = new List<SkyIslandStoryPresentation.Choice>();
        choices.Add(new SkyIslandStoryPresentation.Choice("make", delegate
        {
            actions++;
            Check(ZombieModeUIHelper.Leases == 1, "modal lease remains held while action runs");
            ui.Show("workbench", "long introduction", choices);
            Check(ui.TextForTest == "intro", "intermediate page is not rendered in callback");
            return "made";
        }));
        ui.Show("workbench", "intro", choices);
        ui.SelectForTest(0);
        int before = BossRushUI.Canvases, opens = BossRushUI.Opens;
        float bottom = ui.BottomForTest;
        ui.ClickForTest(0);
        Check(actions == 1 && BossRushUI.Canvases == before + 1, "successful action renders exactly one final page");
        Check(ui.TextForTest == "made" && ui.SelectionForTest == 0, "final receipt and keyboard selection survive");
        Check(Math.Abs(ui.BottomForTest - bottom) < .01f, "receipt anchors to the clicked page bottom");
        Check(BossRushUI.Opens == opens && HUDManager.Tokens.Count == 1 && ZombieModeUIHelper.Leases == 1,
            "refresh does not replay entrance or leak owners");
        before = BossRushUI.Canvases;
        ui.DispatchForTest(delegate { ui.Show("letters", "target intro", Empty()); return null; });
        Check(BossRushUI.Canvases == before + 1 && ui.TextForTest == "target intro" && ui.TitleForTest == "letters",
            "null navigation reply keeps destination text");
        before = BossRushUI.Canvases;
        ui.DispatchForTest(() => "target intro");
        Check(BossRushUI.Canvases == before, "equal reply without a page change performs no rebuild");
        ui.DispatchForTest(() => new string('x', 420));
        Check(ui.TextForTest.Length == 420 && BossRushUI.Canvases == before + 1, "long journal reply rebuilds with fresh measurement");
        before = BossRushUI.Canvases;
        ui.DispatchForTest(delegate { ui.Show("old", "ignore", Empty()); ui.Show("latest", "keep", Empty()); return null; });
        Check(BossRushUI.Canvases == before + 1 && ui.TitleForTest == "latest", "multiple requests commit only the last page");
        int nested = 0;
        ui.DispatchForTest(delegate { ui.DispatchForTest(delegate { nested++; return "nested"; }); return null; });
        Check(nested == 0 && !ui.PendingForTest, "reentrant selection never invokes a second gameplay action");
        before = BossRushUI.Canvases;
        try
        {
            ui.DispatchForTest(delegate { ui.Show("discard", "discard", Empty()); throw new InvalidOperationException("test callback"); });
            throw new Exception("callback exception was swallowed");
        }
        catch (InvalidOperationException) { }
        Check(!ui.PendingForTest && BossRushUI.Canvases == before && ui.TitleForTest == "latest", "throwing callback discards pending page and unlocks dispatcher");
        ui.DispatchForTest(() => "recovered");
        Check(ui.TextForTest == "recovered", "next action works after an exception");
        before = BossRushUI.Canvases;
        ui.DispatchForTest(delegate { ui.Show("should not open", "none", Empty()); ui.Dispose(); return "challenge started"; });
        Check(!ui.Visible && !ui.PendingForTest && BossRushUI.Canvases == before, "closing during action cancels queued page and receipt");
        ui.Show("new visit", "fresh", Empty());
        ui.DestroyCanvasForTest();
        Check(!ui.Visible, "destroyed Unity canvas compares equal to null");
        ui.Dispose(); ui.Dispose();
        Check(HUDManager.Tokens.Count == 0 && ZombieModeUIHelper.Leases == 0, "destroyed canvas still unregisters its HUD token exactly once");
    }

    private static void ServicesAndHints()
    {
        Time.time = 10;
        EconomyManager.Enough = EconomyManager.Payment = true;
        EconomyManager.Payments = 0;
        var player = new CharacterMainControl();
        var services = new SkyIslandServices(player);
        var world = new SkyIslandWorldStory(new SkyIslandSession { Services = services });
        int price, wait;
        Check(services.HealReadiness(out price, out wait) == SkyIslandServiceReadiness.Ready && price == 240,
            "injury quote follows missing health ratio");
        world.ShowHealForTest();
        int before = BossRushUI.Canvases;
        world.presentation.ClickForTest(0);
        Check(player.Health.CurrentHealth == 100 && EconomyManager.Payments == 1, "remedy pays and heals once");
        Check(world.presentation.ChoicesForTest == 0 && BossRushUI.Canvases == before + 1,
            "successful remedy removes obsolete button in one render");
        Check(services.HealReadiness(out price, out wait) == SkyIslandServiceReadiness.NothingToDo,
            "full health beats cooldown instead of exposing an idle timer");
        player.Health.CurrentHealth = 30;
        Check(services.HealReadiness(out price, out wait) == SkyIslandServiceReadiness.CoolingDown && wait == 300,
            "new injury during cooldown still reports actual wait");
        world.ShowHealForTest();
        Check(world.presentation.LabelForTest(0).Contains("300"), "cooldown button displays remaining game seconds");
        world.presentation.ClickForTest(0);
        Check(EconomyManager.Payments == 1 && player.Health.CurrentHealth == 30 && world.presentation.ChoicesForTest == 1,
            "cooldown click neither charges nor heals nor hides the available service");
        Time.time += SkyIslandServices.HealCooldown;
        EconomyManager.Enough = false;
        Check(services.HealReadiness(out price, out wait) == SkyIslandServiceReadiness.ShortOfMoney && price == 336,
            "quote remains truthful without enough money");
        EconomyManager.Enough = true;
        EconomyManager.Payment = false;
        world.ShowHealForTest();
        world.presentation.ClickForTest(0);
        Check(EconomyManager.Payments == 1 && player.Health.CurrentHealth == 30 && world.presentation.ChoicesForTest == 1,
            "failed payment keeps retry option and player state");
        EconomyManager.Payment = true;
        world.ShowRepairForTest();
        world.presentation.ClickForTest(0);
        Check(services.RepairsForTest == 1 && world.presentation.ChoicesForTest == 0, "refit shared callback refreshes fulfilled need");
        world.ShowMealForTest();
        world.presentation.ClickForTest(0);
        Check(services.MealsForTest == 1 && world.presentation.ChoicesForTest == 0, "meal shared callback hides already eaten action");
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            world.ShowHintForTest();
            world.presentation.ClickForTest(0);
            Check(world.presentation.TextForTest == "recorded\n\n" + L10n.T("还要清理林间道路", "Clear the woodland path next"),
                "successful receipt retains newly evaluated next step in current language");
            world.ShowNoHintForTest();
            world.presentation.ClickForTest(0);
            Check(world.presentation.TextForTest == "crafted", "page without new hints does not inherit old blockers");
        }
        L10n.IsChinese = true;
        world.Hide();
    }

    private static void PuzzleRecovery()
    {
        foreach (var puzzle in SkyIslandPuzzles.All)
        {
            var world = new SkyIslandWorldStory(new SkyIslandSession());
            world.ShowPuzzleForTest(puzzle);
            for (int step = 0; step < puzzle.Steps.Length; step++)
            {
                int before = BossRushUI.Canvases;
                world.presentation.ClickForTest(puzzle.Steps[step].Answer);
                Check(BossRushUI.Canvases == before + 1, "puzzle step submits one measured page: " + puzzle.Key);
            }
            Check(world.story.Records == 1 && world.presentation.ChoicesForTest == 1 && world.presentation.LabelForTest(0) == "retry record",
                "solved puzzle with blocked storage shows a record retry, not its final answer: " + puzzle.Key);
            Check(!world.story.Current.Has(puzzle.Flag), "storage failure does not publish evidence: " + puzzle.Key);
            world.story.AcceptRecord = true;
            world.presentation.ClickForTest(0);
            Check(world.story.Current.Has(puzzle.Flag) && world.presentation.ChoicesForTest == 0,
                "record retry succeeds without solving puzzle again: " + puzzle.Key);
            world.Hide();
        }
    }

    private static void RewardPlacement()
    {
        var services = new SkyIslandServices(new CharacterMainControl());
        var contract = new SkyIslandBounty();
        string message;
        SkyIslandLootTier tier;
        Check(contract.TryAccept(SkyIslandBountyKind.Threats, out message), "take real contract");
        for (int i = 0; i < contract.Target; i++) contract.ReportEncounterCleared();
        SkyIslandRewardCrate.CreateCalls = 0;
        SkyIslandRewardCrate.PlacementAvailable = false;
        var anchor = new Vector3(50, 5, 20);
        Check(!contract.TryClaim(t => services.DropBountyReward(anchor, t, 1), out tier, out message), "unsafe reward placement rejects claim");
        Check(contract.IsComplete && contract.CompletedRounds == 0 && SkyIslandRewardCrate.CreateCalls == 0,
            "no fallback crate inside interaction anchor and completed contract retained");
        SkyIslandRewardCrate.PlacementAvailable = true;
        SkyIslandRewardCrate.Created = false;
        Check(!contract.TryClaim(t => services.DropBountyReward(anchor, t, 1), out tier, out message) && contract.IsComplete,
            "crate creation failure also retains completed contract");
        SkyIslandRewardCrate.Created = true;
        Check(contract.TryClaim(t => services.DropBountyReward(anchor, t, 1), out tier, out message) && contract.CompletedRounds == 1 && !contract.HasActive,
            "safe placement retry commits reward and round once");
        int calls = SkyIslandRewardCrate.CreateCalls;
        Check(!contract.TryClaim(t => services.DropBountyReward(anchor, t, 1), out tier, out message) && SkyIslandRewardCrate.CreateCalls == calls,
            "consumed contract cannot deliver twice");
    }

    private static void GroundRing()
    {
        var line = new GameObject("ring").AddComponent<LineRenderer>();
        var tint = new Color(.4f, .7f, .3f, .5f);
        SkyIslandGroundRing.SetShape(line, 7, .2f, tint);
        Check(line.VertexWrites == SkyIslandGroundRing.Segments && line.positionCount == SkyIslandGroundRing.Segments,
            "first shape writes all ring vertices once");
        for (int frame = 0; frame < 120; frame++)
        {
            var pulse = new Color(tint.r, tint.g, tint.b, .5f + frame / 240f);
            SkyIslandGroundRing.SetShape(line, 7, .2f + frame / 600f, pulse);
        }
        Check(line.VertexWrites == SkyIslandGroundRing.Segments, "120 telegraph frames with fixed radius do not rewrite geometry");
        int width = line.WidthWrites, color = line.ColorWrites;
        SkyIslandGroundRing.SetShape(line, 7, line.widthMultiplier, line.startColor);
        Check(line.WidthWrites == width && line.ColorWrites == color, "unchanged or paused appearance performs no writes");
        SkyIslandGroundRing.SetShape(line, 8.5f, .5f, tint);
        Check(line.VertexWrites == 2 * SkyIslandGroundRing.Segments, "next wave radius rebuilds exactly once");
        for (int i = 0; i < SkyIslandGroundRing.Segments; i++)
        {
            Vector3 p = line.GetPosition(i);
            Check(Math.Abs(Math.Sqrt(p.x * p.x + p.y * p.y) - 8.5) < .0001 && p.z == 0,
                "all vertices preserve actual gameplay radius and local plane");
        }
        line.positionCount = 2;
        SkyIslandGroundRing.SetShape(line, 8.5f, .5f, tint);
        Check(line.VertexWrites == 3 * SkyIslandGroundRing.Segments && line.positionCount == SkyIslandGroundRing.Segments,
            "changed renderer topology rebuilds even at same radius");
        SkyIslandGroundRing.SetShape(null, 7, .2f, tint);
        UnityEngine.Object.Destroy(line.gameObject);
        SkyIslandGroundRing.SetShape(line, 7, .2f, tint);
        Check(line == null, "destroyed renderer is ignored");
    }

    private static void AudioStop()
    {
        foreach (string exit in new[] { "return", "death", "scene change", "runtime cleanup" })
        {
            var world = new SkyIslandWorldStory(new SkyIslandSession());
            var gnats = new SkyIslandGnats();
            var audio = gnats.StartBuzzForTest();
            world.BindAudioForTest(gnats);
            world.Hide();
            Check(audio.Stops == 0, "ordinary panel close does not interrupt island sound: " + exit);
            world.session.IsReady = false;
            world.Hide();
            Check(audio.Stops == 1 && audio.StoppedBeforeDestroy, "session shutdown stops owned loop before emitter destruction: " + exit);
            world.Hide();
            gnats.DestroyEmitterForTest();
            gnats.StopBuzz();
            Check(audio.Stops == 1 && audio == null, "late cleanup is idempotent after Unity destroyed emitter: " + exit);
        }
    }
}

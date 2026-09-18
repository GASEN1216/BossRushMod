using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BossRush;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS " + message);
    }
    private static bool Near(float actual, float expected) { return Math.Abs(actual - expected) < 0.0001f; }
    private static string Text(JsonElement row, string key)
    { JsonElement value; return row.TryGetProperty(key, out value) ? value.GetString() : null; }
    private static int Number(JsonElement row, string key)
    { JsonElement value; return row.TryGetProperty(key, out value) ? value.GetInt32() : 0; }
    private static bool Flag(JsonElement row, string key)
    { JsonElement value; return row.TryGetProperty(key, out value) && value.GetBoolean(); }
    private static List<ModeHEffectSpec> Components(JsonElement row)
    {
        var result = new List<ModeHEffectSpec>();
        foreach (JsonElement component in row.GetProperty("components").EnumerateArray())
        {
            JsonElement restore;
            result.Add(new ModeHEffectSpec {
                EffectId = Text(component, "effectId"), ControlPointId = Text(component, "controlPointId"),
                Op = Text(component, "op"), MultiplierMilli = Number(component, "multiplierMilli"),
                CapMilli = Number(component, "capMilli"), ValueMilli = Number(component, "valueMilli"),
                AddMilli = Number(component, "addMilli"), BoolValue = Flag(component, "boolValue"),
                SelfSettled = Flag(component, "selfSettled"), AppliesWhen = Text(component, "appliesWhen"),
                TargetCommandId = Text(component, "commandId"), TargetSlot = Text(component, "slot"),
                WindowSeconds = Number(component, "windowSeconds"),
                Restore = !component.TryGetProperty("restore", out restore) || restore.GetBoolean()
            });
        }
        return result;
    }
    private static void LoadCatalog()
    {
        using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scars.json"))))
        {
            ModeHContentCatalog.Scars = new List<ModeHScarSpec>();
            foreach (JsonElement row in document.RootElement.GetProperty("scars").EnumerateArray())
                ModeHContentCatalog.Scars.Add(new ModeHScarSpec { ScarId = Text(row, "scarId"),
                    Trigger = Text(row, "trigger"), WindowSeconds = Number(row, "windowSeconds"), Components = Components(row) });
            ModeHContentCatalog.Injuries = new List<ModeHInjurySpec>();
            foreach (JsonElement row in document.RootElement.GetProperty("injuries").EnumerateArray())
                ModeHContentCatalog.Injuries.Add(new ModeHInjurySpec { InjuryId = Text(row, "injuryId"),
                    Scope = Text(row, "scope"), RequiresEnemyCountAtLeast = Number(row, "requiresEnemyCountAtLeast"),
                    Components = Components(row) });
        }
        ModeHContentCatalog.Commands = new List<ModeHCommandSpec> {
            new ModeHCommandSpec { CommandId = "press", Effects = new List<ModeHEffectSpec> {
                new ModeHEffectSpec { EffectId = "fixture.press.sight", ControlPointId = "sightDistance",
                    Op = "multiply", MultiplierMilli = 1500, Restore = true } } }
        };
    }
    private static ModeHInjuryAndScarSystem Bind(AICharacterController ai, params string[] scars)
    {
        var effects = new ModeHInjuryAndScarSystem();
        effects.BindFighter(ai, "fighter", "fixture-key", 1,
            new ModeHCommandFireContext { ArenaConditionId = "danger_edge", LowestHealthEnemy = new DamageReceiver() });
        string reason;
        if (!effects.ApplyStandingScars(scars, out reason)) throw new Exception(reason);
        return effects;
    }
    private static void OwnershipAndCleanup()
    {
        var ai = new AICharacterController();
        var effects = Bind(ai);
        string reason;
        foreach (var scar in ModeHContentCatalog.Scars)
        {
            if (scar.WindowSeconds <= 0) continue;
            Check(!effects.TryOpenScarWindow(scar.ScarId, scar.Trigger, out reason)
                && reason == "scar_not_owned:" + scar.ScarId, "unowned trigger refused: " + scar.ScarId);
        }
        effects.OnHealthFractionChanged("old_wound", 0.1f);
        effects.OnEnemyCountChanged("spirit", 3);
        Check(effects.ActiveWindowCount == 0 && Near(effects.GetCommandScale("center"), 1f)
            && Near(ai.baseReactionTime, 0.5f), "uninjured fighter retains independent injury gates");
        effects.RestoreScarWindows(new[] { new ModeHScarWindowStateDto { scarId = "broken_shield_charge", remainingSeconds = 5 } });
        Check(effects.ActiveWindowCount == 0 && ai.shootCanMove, "snapshot cannot introduce unowned scar");
        effects = Bind(ai, "broken_shield_charge");
        Check(!effects.TryOpenScarWindow("broken_shield_charge", "wrong", out reason), "wrong trigger does not consume owned scar");
        ModeHCommandCompatibilityRegistry.Verified = false;
        Check(!effects.TryOpenScarWindow("broken_shield_charge", "armor_first_break", out reason), "unverified whole scar refused");
        ModeHCommandCompatibilityRegistry.Verified = true;
        Check(effects.TryOpenScarWindow("broken_shield_charge", "armor_first_break", out reason)
            && !ai.shootCanMove && Near(ai.sightDistance, 125f), "owned verified scar applies both benefit and cost");
        Check(!effects.TryOpenScarWindow("broken_shield_charge", "armor_first_break", out reason)
            && Near(ai.sightDistance, 125f), "owned trigger consumed once");
        effects.BindFighter(new AICharacterController(), "relay", "fixture-key", 1, new ModeHCommandFireContext());
        Check(ai.shootCanMove && Near(ai.sightDistance, 100f)
            && !effects.TryOpenScarWindow("broken_shield_charge", "armor_first_break", out reason), "relay restores former fighter and clears scar ownership");
        foreach (string id in new[] { "broken_shield_charge", "longshot_memory", "relay_expert" })
        {
            ai = new AICharacterController();
            effects = Bind(ai, id);
            var scar = ModeHContentCatalog.Scars.Find(s => s.ScarId == id);
            Check(effects.TryOpenScarWindow(id, scar.Trigger, out reason), "window starts: " + id);
            int frames = (int)Math.Ceiling(scar.WindowSeconds / 0.016f);
            for (int i = 0; i < frames + 2 && effects.ActiveWindowCount > 0; i++) effects.Tick(0.016f, null);
            Check(effects.ActiveWindowCount == 0 && ai.shootCanMove && Near(ai.sightDistance, 100f)
                && Near(ai.combatTurnSpeed, 10f) && Near(ai.skillSuccessChance, 0.4f), "expires and restores on 16ms boundary: " + id);
            effects.RestoreAll();
            Check(Near(ai.sightDistance, 100f) && Near(ai.skillSuccessChance, 0.4f), "cleanup remains idempotent: " + id);
        }
        ai = new AICharacterController();
        var adapter = new ModeHCommandAdapter();
        var comp = new[] { new ModeHEffectSpec { EffectId = "fixture", ControlPointId = "sightDistance", Op = "multiply", MultiplierMilli = 1250, Restore = true } };
        adapter.ApplyEffects(ai, "fixture", comp, 0.005f, 1f, new ModeHCommandFireContext(), out reason);
        adapter.Tick(0.016f, new ModeHCommandFireContext());
        Check(!adapter.IsActive && !adapter.NeedsFinalize && Near(ai.sightDistance, 100f), "adapter itself finalizes before reassert throttle");
    }
    private static void CommandScopesAndContext()
    {
        string reason;
        var ai = new AICharacterController();
        var effects = Bind(ai, "center_keeper", "blood_rush");
        Check(Near(effects.GetCommandScale("center"), 1.25f) && Near(effects.GetCommandScale("press"), 1f), "standing target command scope is honored before first Tick");
        Check(effects.TryOpenScarWindow("blood_rush", "enemy_first_low_health", out reason), "blood rush starts");
        Check(Near(effects.GetCommandScale("center"), 0.625f) && Near(effects.GetCommandScale("press"), 1f), "blood rush composes only for center");
        effects.Tick(5.016f, null);
        Check(Near(effects.GetCommandScale("center"), 1.25f), "blood rush expires independently of standing scar");
        effects.ApplyStandingInjury("spirit", out reason);
        Check(Near(effects.GetCommandScale("press"), 1f), "spirit waits for enemy threshold");
        effects.OnEnemyCountChanged("spirit", 2);
        effects.OnEnemyCountChanged("spirit", 3);
        Check(Near(effects.GetCommandScale("press"), 0.85f) && Near(effects.GetCommandScale("center"), 1.0625f), "spirit applies once and composes with scoped scar");
        effects.RestoreAll();
        Check(Near(effects.GetCommandScale("center"), 1f), "cleanup clears all self settled records");
        foreach (string condition in new[] { "danger_edge", "open_field" })
        {
            var combat = new ModeHCombatControl { ArenaCondition = condition };
            ai = new AICharacterController();
            var profile = new ModeHProfileDto { profileId = "starter", stableKey = "fixture-key", scarIds = new List<string> { "center_keeper" } };
            Check(combat.OnFighterEntered(new ModeHParticipantRef { Character = ai }, profile, out reason), "actual fighter entry succeeds: " + condition);
            Check(combat.RefreshCount == 1 && Near(combat.Effects.GetCommandScale("center"), condition == "danger_edge" ? 1.25f : 1f)
                && Near(ai.sightDistance, condition == "open_field" ? 85f : 100f), "actual fighter entry refreshes static context before applying: " + condition);
        }
        // A pure self-settled fixture entry verifies component windows as well as root windows.
        ModeHContentCatalog.Scars.Add(new ModeHScarSpec { ScarId = "fixture_timed", Trigger = "fixture_trigger", WindowSeconds = 6,
            Components = new List<ModeHEffectSpec> { new ModeHEffectSpec { EffectId = "fixture_timed.scale", ControlPointId = "command_scale",
                Op = "self_settled", SelfSettled = true, MultiplierMilli = 1200, WindowSeconds = 2 } } });
        effects = Bind(new AICharacterController(), "fixture_timed");
        effects.TryOpenScarWindow("fixture_timed", "fixture_trigger", out reason);
        Check(Near(effects.GetCommandScale("press"), 1.2f), "pure self settled generic scale applies");
        effects.Tick(2.016f, null);
        Check(Near(effects.GetCommandScale("press"), 1f), "pure self settled component window expires without engine adapter");
    }
    private static void BellConsumption()
    {
        string reason;
        var ai = new AICharacterController();
        var combat = new ModeHCombatControl { ArenaCondition = "danger_edge" };
        var profile = new ModeHProfileDto { profileId = "starter", stableKey = "fixture-key", scarIds = new List<string> { "bell_dependence" } };
        combat.OnFighterEntered(new ModeHParticipantRef { Character = ai }, profile, out reason);
        combat.Commands.LockCommand("press", "starter", 7, out reason);
        combat.SetOwner(8);
        Check(!combat.TryRingBell(null, out reason) && reason == "command_owner_mismatch"
            && !combat.Commands.BellConsumed && combat.Effects.ActiveWindowCount == 0
            && Near(combat.Effects.GetCommandScale("press"), 1f), "actual failed bell does not consume trigger or apply self settled record");
        combat.SetOwner(7);
        Check(combat.TryRingBell(null, out reason) && combat.Commands.BellConsumed
            && combat.Effects.ActiveWindowCount == 1 && Near(ai.sightDistance, 160f)
            && Near(ai.skillSuccessChance, 0.4f), "actual successful bell uses its +20% scale with post-bell condition context");
        Check(Near(combat.Effects.GetCommandScaleForBell("press"), 1.2f), "preview does not double count consumed bell scar");
        Check(!combat.TryRingBell(null, out reason) && reason == "command_bell_consumed"
            && Near(ai.sightDistance, 160f), "repeat bell cannot stack command or scar");
        combat.Effects.Tick(6.016f, combat.Context);
        combat.Commands.Tick(6.016f, combat.Context.ArenaCenter, null, null, 2);
        Check(Near(combat.Effects.GetCommandScale("press"), 1f) && Near(ai.sightDistance, 100f), "bell effect and command both expire and restore");
        // Inject a command-Apply rejection after qualification to exercise the existing
        // consumed-bell contract without pretending this malformed catalog is production data.
        ModeHContentCatalog.Commands.Add(new ModeHCommandSpec { CommandId = "fixture_invalid", Effects = null });
        combat = new ModeHCombatControl();
        combat.OnFighterEntered(new ModeHParticipantRef { Character = new AICharacterController() }, profile, out reason);
        combat.Commands.LockCommand("fixture_invalid", "starter", 7, out reason);
        Check(!combat.TryRingBell(null, out reason) && reason == "command_apply_invalid_input"
            && combat.Commands.BellConsumed && combat.Effects.ActiveWindowCount == 0
            && Near(combat.Effects.GetCommandScale("press"), 1f)
            && Near(combat.Effects.GetCommandScaleForBell("press"), 1.2f), "Apply rejection keeps consumed-bell contract without consuming scar trigger");
    }
    private static void OverlappingFields()
    {
        string reason;
        foreach (bool injuryFirst in new[] { true, false })
        {
            var ai = new AICharacterController();
            var context = new ModeHCommandFireContext();
            var injury = new ModeHCommandAdapter();
            var command = new ModeHCommandAdapter();
            var slow = new[] { new ModeHEffectSpec { EffectId = "injury.sight", ControlPointId = "sightDistance", Op = "multiply", MultiplierMilli = 850, Restore = true } };
            var boost = new[] { new ModeHEffectSpec { EffectId = "command.sight", ControlPointId = "sightDistance", Op = "multiply", MultiplierMilli = 1400, Restore = true } };
            Check(injury.ApplyEffects(ai, "injury", slow, 180f, 1f, context, out reason), "injury layer applies");
            Check(command.ApplyEffects(ai, "command", boost, 6f, 1f, context, out reason), "command layer applies");
            command.Tick(0.1f, context);
            injury.Tick(0.1f, context);
            Check(Near(ai.sightDistance, 119f), "overlapping fields compose after both reassert");
            if (injuryFirst) injury.Restore(); else command.Restore();
            Check(Near(ai.sightDistance, injuryFirst ? 140f : 85f), "out of order expiry preserves surviving layer");
            if (injuryFirst) command.Restore(); else injury.Restore();
            Check(Near(ai.sightDistance, 100f), "last layer restores true baseline");
            command.Restore(); injury.Restore();
            Check(Near(ai.sightDistance, 100f), "layer cleanup is idempotent");
        }
    }
    private static void MixedLayerTypes()
    {
        string reason;
        foreach (bool firstExpires in new[] { true, false })
        {
            var ai = new AICharacterController(); var other = new AICharacterController { sightDistance = 200f };
            var context = new ModeHCommandFireContext();
            var first = new ModeHCommandAdapter(); var second = new ModeHCommandAdapter(); var independent = new ModeHCommandAdapter();
            var a = new[] {
                new ModeHEffectSpec { EffectId="a.vector", ControlPointId="skillCoolTimeRange", Op="multiply", MultiplierMilli=1500, Restore=true },
                new ModeHEffectSpec { EffectId="a.bool", ControlPointId="shootCanMove", Op="set_bool", BoolValue=false, Restore=true },
                new ModeHEffectSpec { EffectId="a.cap", ControlPointId="itemSkillChance", Op="multiply_capped", MultiplierMilli=3000, CapMilli=1000, Restore=true } };
            var b = new[] {
                new ModeHEffectSpec { EffectId="b.vector", ControlPointId="skillCoolTimeRange", Op="multiply", MultiplierMilli=800, Restore=true },
                new ModeHEffectSpec { EffectId="b.bool", ControlPointId="shootCanMove", Op="set_bool", BoolValue=true, Restore=true },
                new ModeHEffectSpec { EffectId="b.value", ControlPointId="itemSkillChance", Op="set_value", ValueMilli=300, Restore=true } };
            first.ApplyEffects(ai,"a",a,180,1,context,out reason); second.ApplyEffects(ai,"b",b,180,1,context,out reason);
            Check(Near(ai.skillCoolTimeRange.x,6) && Near(ai.skillCoolTimeRange.y,12) && ai.shootCanMove && Near(ai.itemSkillChance,.3f), "vector bool and capped/set operations compose in order");
            if (firstExpires) first.Restore(); else second.Restore();
            Check(Near(ai.skillCoolTimeRange.x, firstExpires ? 4 : 7.5f) && ai.shootCanMove == firstExpires
                && Near(ai.itemSkillChance, firstExpires ? .3f : 1), "mixed operations preserve surviving layer");
            first.Restore(); second.Restore();
            Check(Near(ai.skillCoolTimeRange.x,5) && ai.shootCanMove && Near(ai.itemSkillChance,.5f), "mixed fields return to original baseline");
            var sight = new[] { new ModeHEffectSpec { EffectId="shared", ControlPointId="sightDistance", Op="multiply", MultiplierMilli=1500, Restore=true } };
            first.ApplyEffects(ai,"same",sight,180,1,context,out reason); independent.ApplyEffects(other,"same",sight,180,1,context,out reason);
            first.Restore(); independent.Reassert(context);
            Check(Near(ai.sightDistance,100) && Near(other.sightDistance,300), "same field on another character has independent ownership");
            independent.Restore();
        }
    }
    private static void SharedCombatContextAndAllocation()
    {
        string reason;
        var ai = new AICharacterController();
        var combat = new ModeHCombatControl { ArenaCondition = "open_field" };
        var profile = new ModeHProfileDto { profileId = "starter", stableKey = "fixture-key", injuryId = "leg", scarIds = new List<string> { "center_keeper" } };
        Check(combat.OnFighterEntered(new ModeHParticipantRef { Character = ai }, profile, out reason), "production entry binds overlapping injury and scar");
        combat.Commands.LockCommand("press", "starter", 7, out reason);
        Check(combat.TryRingBell(null, out reason), "production bell joins fighter effects");
        combat.Commands.Tick(0.1f, combat.Context.ArenaCenter, null, null, 2);
        combat.Effects.Tick(0.1f, combat.Context);
        Check(Near(ai.sightDistance, 100f * 0.85f * 0.85f * 1.5f), "production combat shares field composition across all owners");
        combat.Commands.RestoreAll(); combat.Effects.RestoreAll();
        Check(Near(ai.sightDistance, 100f), "production cleanup restores baseline after layered fields");

        var context = new ModeHCommandFireContext { ArenaConditionId = "open_field" };
        var adapter = new ModeHCommandAdapter();
        var effects = new[] { new ModeHEffectSpec { EffectId = "conditional.sight", ControlPointId = "sightDistance", Op = "multiply", MultiplierMilli = 850, Restore = true, AppliesWhen = "condition_open_field" } };
        adapter.ApplyEffects(ai, "conditional", effects, 180f, 1f, context, out reason);
        for (int i = 0; i < 1000; i++) adapter.Reassert(context);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) adapter.Reassert(context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "1000 steady reassertions allocate zero bytes in isolated .NET host: " + allocated);
        context.ArenaConditionId = "danger_edge";
        adapter.Reassert(context); Check(Near(ai.sightDistance, 100f), "condition false removes its layer");
        context.ArenaConditionId = "open_field";
        adapter.Reassert(context); Check(Near(ai.sightDistance, 85f), "condition true rejoins from baseline");
        adapter.Restore();
    }
    private static void SelectableCommandRoles()
    {
        ModeHContentCatalog.Commands.Add(new ModeHCommandSpec { CommandId = "handoff", IsSignature = true, RequiresRelayEntered = true });
        Check(!ModeHCommandController.GetSelectableCommands("finisher", null, "handoff", null).Contains("handoff"), "solo starter cannot select relay-only order");
        Check(!ModeHCommandController.GetSelectableCommands("finisher", "ranged", "handoff", "last_mag").Contains("handoff"), "starter handoff remains unusable with another relay");
        Check(ModeHCommandController.GetSelectableCommands("ranged", "finisher", "last_mag", "handoff").Contains("handoff"), "relay owner can select handoff");
        var starter = new ModeHProfileDto { profileId = "starter", signatureCommandId = "handoff" };
        var relay = new ModeHProfileDto { profileId = "relay", signatureCommandId = "handoff" };
        Check(ModeHCommandController.ResolveCommandOwner("handoff", starter, relay) == "relay", "two finishers still bind relay-only command to relay");
    }
    public static void Main()
    {
        LoadCatalog();
        OwnershipAndCleanup();
        CommandScopesAndContext();
        BellConsumption();
        OverlappingFields();
        MixedLayerTypes();
        SelectableCommandRoles();
        SharedCombatContextAndAllocation();
        Console.WriteLine("Mode H effects regression: " + _assertions + " assertions passed (host stubs; no Unity runtime).");
    }
}

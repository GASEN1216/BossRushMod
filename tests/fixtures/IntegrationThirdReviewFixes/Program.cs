using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BossRush;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        checks++;
    }
    private static void Near(float actual, float expected, string label)
    {
        Check(Math.Abs(actual - expected) <= Math.Max(0.0001f, Math.Abs(expected) * 0.00001f),
            label + " actual=" + actual + " expected=" + expected);
    }
    private static void Invoke(object owner, string method)
    {
        owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
    }
    private static DuckNpcMovement NewMovement(out AI_PathControl path, out Seeker seeker, out CharacterMainControl character)
    {
        Time.time = 0;
        var go = new GameObject();
        var movement = go.AddComponent<DuckNpcMovement>();
        character = new CharacterMainControl();
        Check(movement.Bind(character, Vector3.zero, 8), "movement binds official components");
        path = go.GetComponent<AI_PathControl>();
        seeker = go.GetComponent<Seeker>();
        return movement;
    }
    private static void StartWander(DuckNpcMovement movement) { Invoke(movement, "MoveToRandomPointNearHome"); }

    private static void TestMovement()
    {
        AI_PathControl path; Seeker seeker; CharacterMainControl character;
        var movement = NewMovement(out path, out seeker, out character);
        StartWander(movement);
        movement.Hold();
        Check(seeker.Requests[0].Cancelled, "Hold cancels A* request");
        seeker.Deliver(0); path.Update();
        Check(path.path == null && character.LastMove.sqrMagnitude == 0, "queued success after Hold cannot restart official motion");
        movement.Release(0); Invoke(movement, "Update");
        Check(seeker.Requests.Count == 2, "release creates fresh request");
        seeker.Deliver(1); var newPath = path.path; seeker.Deliver(0);
        Check(object.ReferenceEquals(path.path, newPath), "old completion cannot replace newer path after release");

        movement = NewMovement(out path, out seeker, out character);
        StartWander(movement); seeker.ThrowOnCancel = true; movement.Hold(); seeker.Deliver(0); path.Update();
        Check(path.path == null && character.LastMove.sqrMagnitude == 0, "failed native cancel still invalidates callback and movement");

        movement = NewMovement(out path, out seeker, out character);
        StartWander(movement); movement.PauseFor(6); seeker.Deliver(0); path.Update();
        Check(path.path == null, "timed pause rejects already queued callback");
        var target = new Transform { position = new Vector3(15, 0, 0) };
        movement.EnablePlayerFollow(target); Time.time = 1; movement.PauseFor(1); Invoke(movement, "Update");
        Check(seeker.Requests.Count == 1, "follow and shorter pause do not override outstanding pause");
        Time.time = 5.99f; Invoke(movement, "Update"); Check(seeker.Requests.Count == 1, "pause lasts full six seconds");
        Time.time = 6; Invoke(movement, "Update"); Check(seeker.Requests.Count == 2, "follow resumes at pause expiry");

        movement = NewMovement(out path, out seeker, out character);
        movement.EnablePlayerFollow(target); Invoke(movement, "Update"); seeker.Deliver(0); path.Update();
        target.position = new Vector3(25, 0, 0); Time.time = 0.59f; Invoke(movement, "Update");
        Check(seeker.Requests.Count == 1, "follow does not replan before interval");
        Time.time = 0.6f; Invoke(movement, "Update");
        Check(seeker.Requests.Count == 2 && seeker.Requests[1].Target.x == 25, "moving follower replans to current target at 0.6 seconds");
        seeker.Deliver(0); Check(path.path == null, "prior moving-path callback cannot undo replan");
        Time.time = 1.2f; Invoke(movement, "Update"); Check(seeker.Requests.Count == 2, "pending slow path is not starved by replan timer");
        Time.time = 13; Invoke(movement, "Update"); Check(seeker.Requests.Count == 3, "timed-out pending path retries");
        target.position = new Vector3(1, 0, 0); Time.time = 14; Invoke(movement, "Update"); seeker.Deliver(2);
        Check(path.path == null && seeker.Requests[2].Cancelled, "near player cancels pending follow");
        target.position = new Vector3(50, 0, 0); Time.time = 15; Invoke(movement, "Update");
        Near(character.transform.position.x, 50, "far player triggers catch-up while follow active");

        movement = NewMovement(out path, out seeker, out character);
        var marker = movement.gameObject.AddComponent<DuckNpcRuntimeMarker>(); marker.Bind("xiaoman", character);
        movement.Hold(); marker.StartDialogue(); marker.StartDialogue(); marker.EndDialogueWithStay(6);
        Time.time = 7; Invoke(movement, "Update");
        Check(movement.IsHeld && seeker.Requests.Count == 0, "repeated chat preserves resident hold");
        movement.Release(0); Invoke(movement, "Update"); Check(seeker.Requests.Count == 1, "resident explicitly released can wander again");
        marker.StartDialogue(); movement.Hold(); movement.Release(0); Time.time = 8; Invoke(movement, "Update");
        Check(movement.IsHeld && seeker.Requests.Count == 1, "residency release does not release dialogue owner");
        marker.EndDialogueWithStay(0); Invoke(movement, "Update"); Check(seeker.Requests.Count == 2, "dialogue owner independently releases");

        movement = NewMovement(out path, out seeker, out character);
        StartWander(movement); movement.enabled = false; Invoke(movement, "OnDisable"); seeker.Deliver(0);
        Check(path.path == null && seeker.Requests[0].Cancelled, "disable cancels and invalidates pending path");
        movement.enabled = true; Time.time = 20; Invoke(movement, "Update"); seeker.Deliver(1);
        Check(path.path != null, "re-enabled movement can issue fresh request");
        Console.WriteLine("PASS: NPC cancellation, stale owners, pause priority, follow replanning, timeout and resident/dialogue ownership");
    }

    private sealed class HitCase
    {
        internal string Name;
        internal float Base = 100, InitialHealth = 1000, Physics = 1, Electric = 1, Ice;
        internal float ElectricResistance = 0.5f, IceResistance = 0.3f, Armor, Pierce, CritRate, Difficulty = 1;
        internal bool IgnoreArmor, Real, Zombie, BreakArmor, BuffArmor;
    }
    private sealed class Result { internal float Final, Health, Portion, Armor; internal bool Dead; }
    private static Health MakeHealth(HitCase test, float initial)
    {
        LevelManager.Rule = new RuleData { DamageFactor_ToPlayer = test.Difficulty };
        LevelManager.Instance = new LevelManager(); Health.OnDead = null; Health.OnHurt = null;
        var character = new CharacterMainControl(); CharacterMainControl.Main = character; LevelManager.Instance.MainCharacter = character;
        var health = new GameObject().AddComponent<Health>(); character.Health = health; health.Owner = character;
        health.CurrentHealth = initial; health.isZombie = test.Zombie; health.BodyArmor = health.HeadArmor = test.Armor;
        health.Resistance[ElementTypes.electricity] = test.ElectricResistance;
        health.Resistance[ElementTypes.ice] = test.IceResistance;
        character.Armor = character.Helmet = new Item();
        if (test.BreakArmor) character.Armor.OnDurability = value => { if (value <= 0) { health.BodyArmor = health.HeadArmor = 0; } };
        return health;
    }
    private static DamageInfo MakeInfo(HitCase test, ElementTypes? only = null)
    {
        var info = new DamageInfo(null) { damageValue = test.Base, damageFactorToZombie = 1.5f, critDamageFactor = 2,
            critRate = test.CritRate, armorPiercing = test.Pierce, ignoreArmor = test.IgnoreArmor,
            damageType = test.Real ? DamageTypes.realDamage : DamageTypes.normal, armorBreak = test.BreakArmor ? 20 : 0 };
        // Passing null explicitly above invokes the official optional-argument constructor.
        if (!only.HasValue || only.Value == ElementTypes.physics) info.AddElementFactor(ElementTypes.physics, test.Physics);
        if (!only.HasValue || only.Value == ElementTypes.electricity) info.AddElementFactor(ElementTypes.electricity, test.Electric);
        if (!only.HasValue || only.Value == ElementTypes.ice) info.AddElementFactor(ElementTypes.ice, test.Ice);
        if (test.BuffArmor) info.buff = new Buff { Apply = h => { h.BodyArmor = h.HeadArmor = 18; } };
        info.buffChance = test.BuffArmor ? 1 : 0;
        return info;
    }
    private static Result Hit(HitCase test, ElementTypes? only, bool read, bool noCap = false)
    {
        var health = MakeHealth(test, noCap ? 1000000 : test.InitialHealth);
        var result = new Result();
        Health.OnHurt = (h, info) =>
        {
            result.Final = info.finalDamage;
            if (read) result.Portion = ModBehaviour.ReadPortion(h, info, ElementTypes.electricity);
        };
        health.Hurt(MakeInfo(test, only));
        result.Health = health.CurrentHealth; result.Dead = health.IsDead; result.Armor = health.BodyArmor;
        return result;
    }
    private static void TestDamage()
    {
        ModBehaviour.Instance = new ModBehaviour { thunderSetActive = true };
        var cases = new[]
        {
            new HitCase { Name = "mixed resistance" },
            new HitCase { Name = "electric immunity", ElectricResistance = 0 },
            new HitCase { Name = "three elements", Ice = 0.8f },
            new HitCase { Name = "per-element minimum", Base = 0.3f, Physics = 10 },
            new HitCase { Name = "final health cap", Base = 100, Electric = 0.00001f, InitialHealth = 10 },
            new HitCase { Name = "minimum plus cap", Base = 0.3f, Physics = 10, InitialHealth = 2 },
            new HitCase { Name = "armor", Armor = 8 },
            new HitCase { Name = "armor piercing", Armor = 8, Pierce = 7 },
            new HitCase { Name = "critical head armor", Armor = 8, CritRate = 1 },
            new HitCase { Name = "difficulty zombie", Difficulty = 0.7f, Zombie = true },
            new HitCase { Name = "ignore armor", Armor = 8, IgnoreArmor = true },
            new HitCase { Name = "real damage still has elements", Armor = 8, Real = true },
            new HitCase { Name = "armor breaks this hit with minimum", Base = 2, Physics = 10, Armor = 18, BreakArmor = true },
            new HitCase { Name = "buff changes armor before damage", Base = 2, Physics = 10, BuffArmor = true },
            new HitCase { Name = "pure electric", Physics = 0 },
            new HitCase { Name = "pure physics", Electric = 0 },
        };
        var baseline = new List<Result>(); var expected = new List<float>();
        foreach (var test in cases)
        {
            Result whole = Hit(test, null, false); baseline.Add(whole);
            float total = 0, electric = 0;
            if (test.Physics > 0) total += Hit(test, ElementTypes.physics, false, true).Final;
            if (test.Ice > 0) total += Hit(test, ElementTypes.ice, false, true).Final;
            if (test.Electric > 0) electric = Hit(test, ElementTypes.electricity, false, true).Final;
            total += electric; expected.Add(total > 0 ? whole.Final * electric / total : 0);
        }
        var harmony = new Harmony("bossrush.integration-third-review-fixture");
        System.IO.File.WriteAllLines("official-source-hurt-il.txt", PatchProcessor.GetOriginalInstructions(
            typeof(Health).GetMethod("Hurt"), (ILGenerator)null).Select((instruction, index) => index + ": " + instruction));
        harmony.CreateClassProcessor(typeof(SetBonusDamageObservation)).Patch();
        Check(SetBonusDamageObservation.IsSupported, "real Harmony transpiler finds official Hurt arithmetic");
        for (int i = 0; i < cases.Length; i++)
        {
            Result actual = Hit(cases[i], null, true);
            Near(actual.Final, baseline[i].Final, cases[i].Name + " damage unchanged");
            Near(actual.Health, baseline[i].Health, cases[i].Name + " health unchanged");
            Near(actual.Armor, baseline[i].Armor, cases[i].Name + " durability side effect unchanged");
            Check(actual.Dead == baseline[i].Dead, cases[i].Name + " death unchanged");
            Near(actual.Portion, expected[i], cases[i].Name + " actual element contribution");
        }
        TestNestedAndFailure();
        TestIlMismatch();
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("PASS: actual Harmony-patched official Hurt body; elemental resistances, minimum, cap, armor break, buff order, nested owners and failure cleanup");
    }
    private static void TestNestedAndFailure()
    {
        var test = new HitCase(); var h = MakeHealth(test, 1000); bool nested = false;
        Health.OnHurt = (health, info) =>
        {
            if (nested) { Near(ModBehaviour.ReadPortion(health, info, ElementTypes.electricity), 10, "nested hit has its own contribution"); return; }
            Near(ModBehaviour.ReadPortion(health, info, ElementTypes.electricity), 50, "outer contribution before nesting");
            nested = true; var inner = new HitCase { Base = 20, Physics = 0 }; health.Hurt(MakeInfo(inner));
            Near(ModBehaviour.ReadPortion(health, info, ElementTypes.electricity), 50, "outer contribution restored after nesting");
        };
        h.Hurt(MakeInfo(test));
        FieldInfo current = typeof(SetBonusDamageObservation).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
        Check(current.GetValue(null) == null, "normal Finalizer releases context");
        h = MakeHealth(test, 1000); h.BeforeElement = type => { throw new InvalidOperationException("injected element failure"); };
        bool threw = false; try { h.Hurt(MakeInfo(test)); } catch (InvalidOperationException) { threw = true; }
        Check(threw && current.GetValue(null) == null, "exception preserved and Finalizer releases context");
        h = MakeHealth(test, 1000); bool child = false;
        Health.OnHurt = (health, info) =>
        {
            if (child) return;
            child = true;
            health.BeforeElement = type => { throw new InvalidOperationException("nested failure"); };
            bool childFailed = false;
            try { health.Hurt(MakeInfo(test)); } catch (InvalidOperationException) { childFailed = true; }
            health.BeforeElement = null;
            Check(childFailed, "nested exception preserved");
            Near(ModBehaviour.ReadPortion(health, info, ElementTypes.electricity), 50, "outer context survives nested exception");
        };
        h.Hurt(MakeInfo(test));
        Check(current.GetValue(null) == null, "nested exception leaves no context after outer completion");
        h = MakeHealth(test, 1000);
        Health.OnHurt = (health, info) =>
        {
            var owner = ModBehaviour.Instance;
            ModBehaviour.Instance = new ModBehaviour { thunderSetActive = true };
            Near(ModBehaviour.ReadPortion(health, info, ElementTypes.electricity), 0, "new runtime owner cannot consume old hit");
            ModBehaviour.Instance = owner; owner.thunderSetActive = false;
            Near(ModBehaviour.ReadPortion(health, info, ElementTypes.electricity), 0, "equipment deactivation cancels consumption");
            owner.thunderSetActive = true;
        };
        h.Hurt(MakeInfo(test));
        ModBehaviour.Instance.thunderSetActive = false; h = MakeHealth(test, 1000);
        h.BeforeElement = type => { Check(current.GetValue(null) == null, "unequipped hit allocates no observation"); return 1; };
        h.Hurt(MakeInfo(test));
        ModBehaviour.Instance.thunderSetActive = true;
        Near(Hit(test, null, true).Portion, 50, "fresh hit after exception and equipment change");
        var infoOutside = MakeInfo(test); infoOutside.finalDamage = 150;
        Near(ModBehaviour.ReadPortion(h, infoOutside, ElementTypes.electricity), 0, "missing capture never estimates healing");
        Check(ModBehaviour.Logs.Any(message => message.Contains("set-bonus-damage-observation")), "missing capture produces explicit diagnostic");
    }
    private static void TestIlMismatch()
    {
        MethodInfo method = typeof(Health).GetMethod("Hurt");
        var original = PatchProcessor.GetOriginalInstructions(method, (ILGenerator)null);
        int element, sum; string reason;
        Check(SetBonusDamageObservation.TryFindObservationPoint(original, out element, out sum, out reason), "unmodified IL matches");
        var broken = original.Select(c => new CodeInstruction(c)).ToList(); broken[sum + 2].opcode = OpCodes.Sub;
        Check(!SetBonusDamageObservation.TryFindObservationPoint(broken, out element, out sum, out reason) && reason != null,
            "changed accumulator is rejected, no guessed local slot");
        broken = original.Select(c => new CodeInstruction(c)).ToList();
        broken.RemoveAll(c => c.opcode == OpCodes.Ldc_R4 && object.Equals(c.operand, 1f));
        Check(!SetBonusDamageObservation.TryFindObservationPoint(broken, out element, out sum, out reason), "missing per-element minimum is rejected");
    }
    private static int Main()
    {
        try { TestMovement(); TestDamage(); Console.WriteLine("PASS IntegrationThirdReviewFixes: " + checks + " checks"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

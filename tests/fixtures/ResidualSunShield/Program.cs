using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
using UObject = UnityEngine.Object;

internal static class Program
{
    private static int assertions, scenarios;
    private static CharacterMainControl player;

    private static int Main()
    {
        try
        {
            Run("real damage entry blocks before lethal health loss outside zombie mode", LethalBlock);
            Run("one guarded hit empowers exactly one defended direct hit", OneBlockOneCharge);
            Run("direction cone follows captured dodge facing and rejects side and rear", Direction);
            Run("effects explosions allies companions and unrelated damage cannot spend state", Attribution);
            Run("window charge and cooldown have exact boundaries", Boundaries);
            Run("invalid inputs and overflow cannot corrupt pending states", Invalid);
            Run("unequip cancels both effects without washing committed cooldown", Unequip);
            Run("death and main owner replacement discard old state", OwnerChanges);
            Run("scene fade cancels before destruction and loading end rebinds", SceneLifecycle);
            Run("late setup and reentry cannot duplicate subscriptions", LateSetup);
            Run("backpack and base remain inactive without a driver", Inactive);
            Run("stale driver cannot cancel a newly activated guard", StaleDriver);
            Run("real death prefix restores official identity before counter and corpse loot", () => DeathPrefixCases.Run(Expect));
            Cleanup();
            Console.WriteLine("ResidualSunShield: " + scenarios + " scenarios / " + assertions + " assertions PASS");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); foreach (string log in ModBehaviour.Logs) Console.Error.WriteLine(log); return 1; }
        finally { ResidualSunShieldRuntime.Unsubscribe(); GameObject.DestroyScene(); }
    }

    private static void Run(string name, Action test) { test(); scenarios++; Console.WriteLine("PASS " + name); }
    private static void Expect(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(float expected, float actual, string message)
    { assertions++; if (Math.Abs(expected - actual) > 0.001f) throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual); }
    private static void Cleanup()
    {
        ResidualSunShieldRuntime.Unsubscribe();
        GameObject.DestroyScene();
        Expect(Health.DeathSubscribers == 0, "death detached");
        Expect(CharacterMainControl.HoldSubscribers == 0 && CharacterMainControl.SlotSubscribers == 0, "equipment detached");
        Expect(SceneLoader.Subscribers == 0 && SceneLoader.FinishSubscribers == 0, "loading detached");
        Expect(LevelManager.BeginSubscribers == 0 && LevelManager.EndSubscribers == 0, "level lifecycle detached");
        CharacterMainControl.Main = null;
        player = null;
        NewWeaponEquipState.Equipped = false;
        PetNestCompanionAgent.Companions.Clear();
        BossRushUI.Paused = false;
        SceneLoader.IsSceneLoading = false;
        Time.time = 100f;
        Time.frameCount = 100;
        ModBehaviour.Instance = null;
        ModBehaviour.Logs.Clear();
        ResidualSunShieldFx.Absorbs = ResidualSunShieldFx.Ripostes = 0;
    }

    private static CharacterMainControl Character(Teams team)
    {
        GameObject go = new GameObject(team.ToString());
        CharacterMainControl value = go.AddComponent<CharacterMainControl>();
        value.Team = team;
        value.Health = go.AddComponent<Health>();
        value.Health.Character = value;
        value.transform.position = new Vector3(0f, 0f, 5f);
        return value;
    }
    private static void Start(bool equipped = true, bool inBase = false)
    {
        Cleanup();
        LevelManager.Instance = new LevelManager { IsBaseLevel = inBase };
        player = Character(Teams.player);
        player.transform.position = Vector3.zero;
        CharacterMainControl.Main = player;
        NewWeaponEquipState.Equipped = equipped;
        ResidualSunShieldRuntime.Subscribe();
    }
    private static void Dodge() { player.StartAction(new CA_Dash()); }
    private static void Hit(Health target, CharacterMainControl from, float amount, bool effect = false, bool explosion = false)
    { target.Hurt(new DamageInfo(from) { damageValue = amount, isFromBuffOrEffect = effect, isExplosion = explosion }); }
    private static void Equip(bool value) { NewWeaponEquipState.Equipped = value; player.EquipmentChanged(); }
    private static ResidualSunShieldDriver Driver()
    {
        foreach (GameObject go in GameObject.All)
        { if (go == null || !go.Active) continue; var value = go.GetComponent<ResidualSunShieldDriver>(); if (value != null) return value; }
        return null;
    }
    private static void Step(float elapsed)
    {
        if (!BossRushUI.Paused) Time.time += elapsed;
        var driver = Driver();
        if (driver != null) ResidualSunShieldRuntime.Tick(driver);
    }

    private static void LethalBlock()
    {
        Start();
        ModBehaviour.Instance = new ModBehaviour { IsZombieModeActive = false };
        player.Health.CurrentHealth = 100f;
        Dodge();
        Hit(player.Health, Character(Teams.wolf), 140f);
        Near(20f, player.Health.CurrentHealth, "140 damage reduced by capped 60 before death");
        Expect(!player.Health.IsDead && ResidualSunShieldRuntime.HasCharge, "lethal guard survives and charges");
        Near(80f, player.Health.Hits[0].finalDamage, "finalDamage includes guard");
        Expect(ResidualSunShieldFx.Absorbs == 1, "block feedback");
    }

    private static void OneBlockOneCharge()
    {
        Start(); Dodge();
        var enemy = Character(Teams.wolf);
        player.Health.DefenseFactor = 0.5f;
        Hit(player.Health, enemy, 80f);
        Near(12f, player.Health.Hits[0].finalDamage, "guard applies after 0.5 defense factor");
        Hit(player.Health, enemy, 80f);
        Near(40f, player.Health.Hits[1].finalDamage, "second incoming hit is unguarded");
        enemy.Health.DefenseFactor = 0.5f;
        Hit(enemy.Health, player, 40f);
        Near(48f, enemy.Health.Hits[0].finalDamage, "20 defended hit plus 28 absorbed bonus");
        Hit(enemy.Health, player, 40f);
        Near(20f, enemy.Health.Hits[1].finalDamage, "charge consumed once");
        Expect(ResidualSunShieldFx.Ripostes == 1, "one riposte feedback");
        Step(0.01f);
        Expect(Driver() == null, "spent states remove Update driver");
    }

    private static void Direction()
    {
        Start();
        player.CurrentAimDirection = new Vector3(0f, 0f, -1f);
        player.MoveInput = new Vector3(1f, 0f, 0f);
        Dodge();
        player.CurrentAimDirection = new Vector3(0f, 0f, -1f);
        var enemy = Character(Teams.wolf);
        Hit(player.Health, enemy, 20f);
        Near(20f, player.Health.Hits[0].finalDamage, "world +Z is side of captured +X");
        enemy.transform.position = new Vector3(-5f, 0f, 0f);
        Hit(player.Health, enemy, 20f);
        Near(20f, player.Health.Hits[1].finalDamage, "rear remains unguarded");
        enemy.transform.position = new Vector3(5f, 0f, 0f);
        Hit(player.Health, enemy, 20f);
        Near(6f, player.Health.Hits[2].finalDamage, "captured dodge direction guards despite later aim");
        Expect(ResidualSunShieldRules.IsFrontal(0f, 1f, 0.8660254f, 0.500001f), "inside cone edge");
        Expect(!ResidualSunShieldRules.IsFrontal(0f, 1f, 0.8660254f, 0.499999f), "outside cone edge");
    }

    private static void Attribution()
    {
        Start(); Dodge();
        var enemy = Character(Teams.wolf);
        Hit(player.Health, enemy, 10f, true);
        Hit(player.Health, enemy, 10f, false, true);
        Hit(player.Health, Character(Teams.player), 10f);
        Hit(player.Health, Character(Teams.middle), 10f);
        var companion = Character(Teams.wolf);
        PetNestCompanionAgent.Companions.Add(companion.Health);
        Hit(player.Health, companion, 10f);
        Expect(ResidualSunShieldRuntime.HasGuard && !ResidualSunShieldRuntime.HasCharge, "non direct hostile hits preserve guard");
        Hit(player.Health, enemy, 20f);
        Hit(enemy.Health, player, 10f, true);
        Hit(enemy.Health, player, 10f, false, true);
        Hit(Character(Teams.player).Health, player, 10f);
        Hit(enemy.Health, companion, 10f);
        Expect(ResidualSunShieldRuntime.HasCharge, "effects allies and NPC attacks preserve charge");
        Hit(enemy.Health, player, 10f);
        Expect(!ResidualSunShieldRuntime.HasCharge, "own direct enemy hit spends charge");
    }

    private static void Boundaries()
    {
        var rules = new ResidualSunShieldRules();
        Expect(rules.TryStartGuard(0f), "first use at zero");
        Expect(rules.HasGuard(0.649f), "before guard expiry");
        Near(0f, rules.TryBlock(40f, 0.65f), "exact window end excludes hit");
        Expect(!rules.TryStartGuard(3.999f), "cooldown before boundary");
        Expect(rules.TryStartGuard(4f), "cooldown exact boundary");
        Near(28f, rules.TryBlock(40f, 4f), "charged amount");
        Near(0f, rules.TryConsumeCharge(10f, 10f), "exact charge end excludes hit");
        Start(); Dodge();
        BossRushUI.Paused = true;
        Step(10f);
        Expect(ResidualSunShieldRuntime.HasGuard, "pause cannot advance time");
        BossRushUI.Paused = false;
        Step(0.65f);
        Expect(Driver() == null, "expired guard removes driver");
        Start(); BossRushUI.Paused = true; Dodge();
        Expect(player.ActionSubscribers == 1 && Driver() == null && !ResidualSunShieldRuntime.HasGuard,
            "paused action notification preserves equipped owner without starting a guard");
        BossRushUI.Paused = false; Dodge();
        Expect(ResidualSunShieldRuntime.HasGuard, "resume accepts the next dodge without an equipment event");
    }

    private static void Invalid()
    {
        var rules = new ResidualSunShieldRules();
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f, float.MaxValue })
            Expect(!rules.TryStartGuard(invalid), "invalid start rejected");
        Expect(rules.TryStartGuard(1f), "valid start");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f, 0f })
            Near(0f, rules.TryBlock(invalid, 1.1f), "invalid block rejected");
        Expect(rules.HasGuard(1.1f), "invalid block leaves valid guard");
        Near(60f, rules.TryBlock(float.MaxValue, 1.1f), "large finite damage capped");
        Near(0f, rules.TryConsumeCharge(float.NaN, 1.2f), "invalid attack cannot spend charge");
        Near(60f, rules.TryConsumeCharge(10f, 1.2f), "charge preserved");
        Expect(!ResidualSunShieldRules.IsFrontal(float.NaN, 1f, 1f, 1f), "invalid direction rejected");
        Expect(!ResidualSunShieldRules.IsFrontal(0f, 0f, 1f, 1f), "zero facing rejected");
    }

    private static void Unequip()
    {
        Start(); Dodge();
        Hit(player.Health, Character(Teams.wolf), 20f);
        Equip(false);
        Expect(!ResidualSunShieldRuntime.HasGuard && !ResidualSunShieldRuntime.HasCharge && Driver() == null, "unequip cancels pending");
        Expect(player.ActionSubscribers == 0, "owner action detached");
        Equip(true); Dodge();
        Expect(!ResidualSunShieldRuntime.HasGuard, "wearing again cannot reset cooldown");
        Step(4f); Dodge();
        Expect(ResidualSunShieldRuntime.HasGuard, "cooldown expires without idle driver");
    }

    private static void OwnerChanges()
    {
        Start(); Dodge();
        var old = player;
        player.Health.Die(new DamageInfo(null));
        Expect(Driver() == null && !ResidualSunShieldRuntime.HasGuard && old.ActionSubscribers == 0, "death cleans owner");
        player = Character(Teams.player); CharacterMainControl.Main = player; player.EquipmentChanged(); Dodge();
        Expect(ResidualSunShieldRuntime.HasGuard, "new owner gets independent guard");
        old = player;
        player = Character(Teams.player); CharacterMainControl.Main = player;
        Step(0.01f);
        Expect(!ResidualSunShieldRuntime.HasGuard && old.ActionSubscribers == 0, "replacement clears old guard");
        UObject.Destroy(old.gameObject);
        Dodge(); Expect(ResidualSunShieldRuntime.HasGuard, "destroyed old graph does not cancel new guard");
    }

    private static void SceneLifecycle()
    {
        Start(); Dodge();
        var old = player;
        SceneLoader.Begin();
        Expect(Driver() == null && player.ActionSubscribers == 0, "fade immediately cancels");
        UObject.Destroy(old.gameObject);
        LevelManager.Begin();
        player = Character(Teams.player); CharacterMainControl.Main = player;
        LevelManager.Finish();
        Expect(player.ActionSubscribers == 0, "loading screen still gates rebinding");
        SceneLoader.Finish();
        Expect(player.ActionSubscribers == 1, "loading completed rebinds");
        Dodge(); Expect(ResidualSunShieldRuntime.HasGuard, "new scene usable");
        SceneLoader.Begin();
        Expect(player.ActionSubscribers == 0, "second fade cancels");
        SceneLoader.Finish();
        Expect(player.ActionSubscribers == 1, "aborted load rebinds same surviving owner");
    }

    private static void LateSetup()
    {
        Start();
        ResidualSunShieldRuntime.Subscribe();
        ResidualSunShieldRuntime.SetupForScene();
        ResidualSunShieldRuntime.SetupForScene();
        Expect(player.ActionSubscribers == 1 && CharacterMainControl.SlotSubscribers == 1, "setup idempotent");
        Dodge(); Expect(ResidualSunShieldRuntime.HasGuard, "late setup preequipped guard");
    }

    private static void Inactive()
    {
        Start(false); Dodge();
        Expect(Driver() == null && player.ActionSubscribers == 0, "backpack inactive");
        Equip(true); Expect(player.ActionSubscribers == 1 && Driver() == null, "equipped idle has no driver");
        Start(true, true); Dodge();
        Expect(Driver() == null && player.ActionSubscribers == 0, "base inactive");
    }

    private static void StaleDriver()
    {
        Start(); Dodge(); var stale = Driver();
        Equip(false); Equip(true); Time.time += 4f; Dodge();
        var current = Driver(); Expect(current != null && !ReferenceEquals(current, stale), "new driver");
        ResidualSunShieldRuntime.OnDriverDestroyed(stale);
        ResidualSunShieldRuntime.Tick(stale);
        Expect(ResidualSunShieldRuntime.HasGuard && ReferenceEquals(current, Driver()), "stale callbacks ignored");
    }
}

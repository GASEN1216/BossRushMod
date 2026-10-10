using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
using Duckov.UI.DialogueBubbles;
using UObject = UnityEngine.Object;

internal static class Program
{
    private static int assertions;
    private static int scenarios;
    private static CharacterMainControl player;

    private static int Main()
    {
        try
        {
            Run("three different enemies use each first-hit damage and defer one frame", DistinctAndDeferred);
            Run("fatal direct hits count; dead and destroyed targets never receive the bonus", FatalHits);
            Run("first mark anchors the time window; duplicates neither extend nor replace it", RulesWindow);
            Run("cooldown boundaries and partial reset cannot backfill or wash cooldown", RulesCooldown);
            Run("invalid damage and timestamps cannot poison a sequence", RulesInvalidInputs);
            Run("friendly, neutral, companion, NPC and effect damage cannot mark", Attribution);
            Run("same-frame distinct pellets can complete one roll call", SameFrameTargets);
            Run("pause freezes both the window and pending settlement", Pause);
            Run("unequip cancels settlement and preserves the committed cooldown", Unequip);
            Run("death and owner replacement clear old target references", OwnerChanges);
            Run("owner replacement while unequipped does not inherit a prior cooldown", UnequippedOwnerChange);
            Run("scene fade cancels before level initialization and old graph destruction", SceneLifecycle);
            Run("late deferred setup rebinds pre-equipped players without another equipment event", LateSceneSetup);
            Run("load-finished binds after level initialization was still gated by the loading screen", LoadFinishedBinding);
            Run("scene loading inside damage stops the rest of the settlement", InterruptedSettlement);
            Run("changed allegiance and destroyed targets are rechecked at settlement", RecheckTargets);
            Run("backpack and base contexts stay inactive; subscriptions are idempotent", InactiveAndLifecycle);
            Run("a stale driver callback cannot cancel the next sequence", StaleDriver);
            Cleanup();
            Console.WriteLine("RollCallLedger: " + scenarios + " scenarios / " + assertions + " assertions PASS");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            foreach (string log in ModBehaviour.Logs) Console.Error.WriteLine(log);
            return 1;
        }
        finally { RollCallLedgerRuntime.Unsubscribe(); GameObject.DestroyScene(); }
    }

    private static void Run(string name, Action test) { test(); scenarios++; Console.WriteLine("PASS " + name); }
    private static void Expect(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message)
    { assertions++; if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual); }

    private static void Cleanup()
    {
        RollCallLedgerRuntime.Unsubscribe();
        GameObject.DestroyScene();
        Equal(0, Health.HurtSubscribers, "hurt subscription detached");
        Equal(0, Health.DeathSubscribers, "death subscription detached");
        Equal(0, CharacterMainControl.HoldSubscribers, "hold subscription detached");
        Equal(0, CharacterMainControl.SlotSubscribers, "slot subscription detached");
        Equal(0, LevelManager.BeginSubscribers, "level begin detached");
        Equal(0, LevelManager.EndSubscribers, "level end detached");
        Equal(0, SceneLoader.Subscribers, "scene fade detached");
        Equal(0, SceneLoader.FinishSubscribers, "scene finish detached");
        CharacterMainControl.Main = null;
        player = null;
        NewWeaponEquipState.Equipped = false;
        PetNestCompanionAgent.Companions.Clear();
        BossRushUI.Paused = false;
        SceneLoader.IsSceneLoading = false;
        Time.time = 100f;
        Time.deltaTime = 0f;
        Time.frameCount = 100;
        DialogueBubblesManager.Requests.Clear();
        NewWeaponFx.Bursts = 0;
        ModBehaviour.Logs.Clear();
    }

    private static CharacterMainControl Character(Teams team)
    {
        GameObject go = new GameObject(team.ToString());
        CharacterMainControl value = go.AddComponent<CharacterMainControl>();
        value.Team = team;
        value.Health = go.AddComponent<Health>();
        value.Health.Character = value;
        return value;
    }

    private static void Start(bool equipped = true, bool inBase = false)
    {
        Cleanup();
        LevelManager.Instance = new LevelManager { IsBaseLevel = inBase };
        player = Character(Teams.player);
        CharacterMainControl.Main = player;
        NewWeaponEquipState.Equipped = equipped;
        RollCallLedgerRuntime.Subscribe();
    }

    private static Health Enemy() { return Character(Teams.wolf).Health; }
    private static void Hit(Health target, float damage = 40f, bool fatal = false) { target.DirectHit(player, damage, fatal); }
    private static void Equip(bool value) { NewWeaponEquipState.Equipped = value; player.EquipmentChanged(); }
    private static RollCallLedgerDriver Driver()
    {
        foreach (GameObject go in GameObject.All)
        {
            if (go == null || !go.Active) continue;
            RollCallLedgerDriver value = go.GetComponent<RollCallLedgerDriver>();
            if (value != null) return value;
        }
        return null;
    }
    private static void Step(float elapsed = 0.02f)
    {
        Time.frameCount++;
        Time.deltaTime = elapsed;
        if (!BossRushUI.Paused) Time.time += elapsed;
        RollCallLedgerDriver value = Driver();
        if (value != null) RollCallLedgerRuntime.Tick(value);
    }
    private static Health[] Complete()
    {
        Health[] targets = { Enemy(), Enemy(), Enemy() };
        foreach (Health target in targets) Hit(target);
        Expect(RollCallLedgerRuntime.HasPendingSettlement, "third enemy schedules settlement");
        return targets;
    }

    private static void DistinctAndDeferred()
    {
        Start();
        Health a = Enemy(), b = Enemy(), c = Enemy();
        Hit(a, 40f); Hit(a, 200f);
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "same enemy is one mark");
        Hit(b, 80f); Hit(c, 120f);
        Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "third mark clears active list before damage");
        Equal(106f, RollCallLedgerRuntime.NextTriggerAt, "cooldown committed at trigger");
        RollCallLedgerRuntime.Tick(Driver());
        Equal(0, a.Hits.Count, "same frame does not settle");
        Step();
        Equal(30f, a.Hits[0].damageValue, "first hit snapshot survives later stronger hit");
        Equal(40f, b.Hits[0].damageValue, "second target owns its snapshot");
        Equal(50f, c.Hits[0].damageValue, "third target owns its snapshot");
        foreach (Health target in new[] { a, b, c })
        {
            Equal(1, target.Hits.Count, "one bonus per target without recursive proc");
            Expect(target.Hits[0].isFromBuffOrEffect && target.Hits[0].HasPhysicalElement, "physical effect damage");
            Equal(0, target.Hits[0].fromWeaponItemID, "effect does not spoof weapon attribution");
        }
        Equal(3, NewWeaponFx.Bursts, "three simultaneous stamps");
        Equal("1", DialogueBubblesManager.Requests[0].Text, "first ordinal visible");
        Equal("2", DialogueBubblesManager.Requests[1].Text, "second ordinal visible");
        Equal("3", DialogueBubblesManager.Requests[2].Text, "third ordinal visible");
        Equal(0.85f, DialogueBubblesManager.Requests[0].Duration, "duration reaches the correct official argument");
        Expect(Driver() == null, "cooldown needs no update driver");
    }

    private static void FatalHits()
    {
        Start();
        Health a = Enemy(), b = Enemy(), c = Enemy();
        Hit(a, 90f, true); Hit(b, 30f); Hit(c, 50f, true);
        Expect(RollCallLedgerRuntime.HasPendingSettlement, "fatal first and third hits both count");
        Step();
        Equal(0, a.Hits.Count, "dead first target skipped");
        Equal(27.5f, b.Hits[0].damageValue, "living target settles despite two dead classmates");
        Equal(0, c.Hits.Count, "dead third target skipped");
    }

    private static void RulesWindow()
    {
        RollCallLedgerRules rules = new RollCallLedgerRules(3, 5f, 6f);
        int ordinal; RollCallLedgerEntry[] result;
        Expect(rules.ObserveHit(1, 10f, 10f, out ordinal, out result), "first mark accepted");
        Expect(!rules.ObserveHit(1, 999f, 14f, out ordinal, out result), "duplicate rejected");
        rules.ObserveHit(2, 20f, 14.5f, out ordinal, out result);
        rules.ObserveHit(3, 30f, 15f, out ordinal, out result);
        Expect(result != null, "window endpoint included");
        Equal(10f, result[0].HitDamage, "duplicate never replaces first damage");
        rules.ResetAll();
        rules.ObserveHit(1, 10f, 10f, out ordinal, out result);
        rules.ObserveHit(2, 20f, 14.9f, out ordinal, out result);
        rules.ObserveHit(3, 30f, 15.01f, out ordinal, out result);
        Equal(1, ordinal, "late enemy starts a new roll call");
        Expect(result == null && rules.Count == 1, "expired marks cannot complete a later set");
        Expect(!rules.Expire(20.01f), "new window endpoint not expired");
        Expect(rules.Expire(20.02f), "expired active marks cleared without another hit");
    }

    private static void RulesCooldown()
    {
        RollCallLedgerRules rules = new RollCallLedgerRules(3, 5f, 6f);
        int ordinal; RollCallLedgerEntry[] result;
        for (int i = 1; i <= 3; i++) rules.ObserveHit(i, 10f, 10f, out ordinal, out result);
        rules.ResetSequence();
        Expect(!rules.ObserveHit(4, 10f, 15.999f, out ordinal, out result), "partial reset retains cooldown");
        Expect(rules.ObserveHit(4, 10f, 16f, out ordinal, out result), "cooldown endpoint reopens");
        Equal(1, ordinal, "blocked hits do not backfill");
        rules.ResetAll();
        Equal(0f, rules.NextTriggerAt, "new session has no old cooldown");
        Equal(0, rules.Count, "full reset clears names");
    }

    private static void RulesInvalidInputs()
    {
        RollCallLedgerRules rules = new RollCallLedgerRules(3, 5f, 6f);
        int ordinal; RollCallLedgerEntry[] result;
        foreach (float damage in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Expect(!rules.ObserveHit(1, damage, 100f, out ordinal, out result), "invalid damage ignored");
        Expect(!rules.ObserveHit(1, 10f, float.NaN, out ordinal, out result), "invalid time ignored");
        Expect(!rules.ObserveHit(0, 10f, 100f, out ordinal, out result), "missing target ignored");
        Equal(0, rules.Count, "invalid hits do not alter sequence");
        Equal(0f, RollCallLedgerRules.CalculateBonus(float.MaxValue, 20f, float.MaxValue), "overflow does not create infinite damage");
    }

    private static void Attribution()
    {
        Start();
        Hit(player.Health); Hit(Character(Teams.player).Health); Hit(Character(Teams.middle).Health);
        Health companion = Enemy(); PetNestCompanionAgent.Companions.Add(companion); Hit(companion);
        Health enemy = Enemy(); enemy.DirectHit(player, 40f, false, true);
        enemy.DirectHit(Character(Teams.player), 40f);
        enemy.DirectHit(null, 40f);
        Hit(enemy, 0f);
        Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "only direct positive player hits on enemies qualify");
        Expect(Driver() == null, "invalid hits create no runtime driver");
        player.Team = Teams.red;
        Hit(Character(Teams.red).Health);
        Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "current faction ally excluded");
        Hit(Character(Teams.blue).Health);
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "current faction enemy accepted");
    }

    private static void SameFrameTargets()
    {
        Start();
        Health[] targets = Complete();
        Hit(Enemy());
        Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "fourth same-frame target ignored in cooldown");
        Step();
        foreach (Health target in targets) Equal(1, target.Hits.Count, "same-frame distinct targets each settle");
    }

    private static void Pause()
    {
        Start();
        Hit(Enemy());
        BossRushUI.Paused = true; Step(20f);
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "pause preserves active window");
        Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "damage notifications while paused do not add names");
        BossRushUI.Paused = false;
        Hit(Enemy()); Health last = Enemy(); Hit(last);
        BossRushUI.Paused = true; Step(20f);
        Equal(0, last.Hits.Count, "pending bonus waits through pause");
        BossRushUI.Paused = false; Step();
        Equal(1, last.Hits.Count, "resume settles next live frame");
    }

    private static void Unequip()
    {
        Start(); Health[] targets = Complete(); RollCallLedgerDriver old = Driver();
        Equip(false);
        Expect(!RollCallLedgerRuntime.HasPendingSettlement && old == null, "unequip immediately cancels and destroys");
        Equal(0, Health.HurtSubscribers, "idle unequipped item has no hurt listener");
        Equip(true); Step();
        foreach (Health target in targets) Equal(0, target.Hits.Count, "cancelled settlement never returns");
        Hit(Enemy()); Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "re-equipping does not wash cooldown");
        Time.time = 106f; Hit(Enemy()); Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "normal cooldown eventually expires");
    }

    private static void OwnerChanges()
    {
        Start(); Health[] targets = Complete();
        player.Health.Die(new DamageInfo());
        Expect(!RollCallLedgerRuntime.HasPendingSettlement, "death cancels pending work");
        player.Health.IsDead = false; player.EquipmentChanged(); Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "revival starts fresh");
        CharacterMainControl old = player;
        player = Character(Teams.player); CharacterMainControl.Main = player;
        Step();
        Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "driver detects silent owner replacement");
        UObject.Destroy(old.gameObject);
        Expect(old == null && old.Health == null, "destroying player destroys its Health component");
        player.EquipmentChanged(); Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "new owner binds without inherited marks");
        foreach (Health target in targets) Equal(0, target.Hits.Count, "previous life never deals deferred damage");
    }

    private static void SceneLifecycle()
    {
        Start(); Health[] targets = Complete(); RollCallLedgerDriver old = Driver();
        SceneLoader.Begin();
        Expect(!RollCallLedgerRuntime.HasPendingSettlement && old == null, "fade start cancels before initialization");
        foreach (Health target in targets) Equal(0, target.Hits.Count, "no fade-time bonus");
        LevelManager.Begin(); GameObject.DestroyScene();
        Expect(player == null && targets[0] == null, "old scene object graph really destroyed");
        player = Character(Teams.player); CharacterMainControl.Main = player;
        SceneLoader.IsSceneLoading = false; LevelManager.Finish();
        Hit(Enemy()); Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "new scene can start roll call");
        Hit(Enemy()); Hit(Enemy());
        SceneLoader.IsSceneLoading = true; Step();
        Expect(!RollCallLedgerRuntime.HasPendingSettlement, "loading flag also cancels when notification was missed");
    }

    private static void UnequippedOwnerChange()
    {
        Start(); Complete(); Equip(false);
        CharacterMainControl old = player;
        player = Character(Teams.player); CharacterMainControl.Main = player;
        UObject.Destroy(old.gameObject);
        Equip(true); Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "replacement owner starts without the removed owner's cooldown");
    }

    private static void InterruptedSettlement()
    {
        Start(); Health[] targets = Complete();
        targets[0].OnReceive = delegate { SceneLoader.Begin(); };
        Step();
        Equal(1, targets[0].Hits.Count, "first damage entered before scene change");
        Equal(0, targets[1].Hits.Count, "generation cancels second target");
        Equal(0, targets[2].Hits.Count, "generation cancels third target");
        Expect(!RollCallLedgerRuntime.HasPendingSettlement, "interrupted settlement has no retained snapshot");
    }

    private static void LateSceneSetup()
    {
        Start();
        LevelManager.Finish();
        RollCallLedgerRuntime.SetupForScene();
        Equal(1, Health.HurtSubscribers, "late host setup leaves an active listener");
        Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "first shot after late setup works without changing equipment");
        Start(false);
        LevelManager.Begin();
        RollCallLedgerRuntime.SetupForScene();
        NewWeaponEquipState.Equipped = true;
        LevelManager.Finish();
        Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "early host setup also binds after official initialization");
    }

    private static void LoadFinishedBinding()
    {
        Start();
        SceneLoader.Begin(); LevelManager.Begin();
        RollCallLedgerRuntime.SetupForScene();
        LevelManager.Finish();
        Equal(0, Health.HurtSubscribers, "loading screen still gates level-initialized event");
        SceneLoader.Finish();
        Equal(1, Health.HurtSubscribers, "load completion rebinds already equipped item");
        Hit(Enemy());
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "first post-load shot works");
    }

    private static void RecheckTargets()
    {
        Start(); Health[] targets = Complete();
        targets[0].Character.Team = Teams.player;
        UObject.Destroy(targets[1].gameObject);
        Step();
        Equal(0, targets[0].Hits.Count, "new ally no longer receives damage");
        Equal(0, targets[1].Hits.Count, "destroyed target ignored with Unity null semantics");
        Equal(1, targets[2].Hits.Count, "remaining live enemy receives its own damage");
    }

    private static void InactiveAndLifecycle()
    {
        Start(false); Hit(Enemy());
        Equal(0, Health.HurtSubscribers, "backpack item subscribes no damage work");
        Expect(Driver() == null, "backpack item creates no driver");
        Equip(true); RollCallLedgerRuntime.Subscribe();
        Equal(1, Health.HurtSubscribers, "repeat subscribe is idempotent");
        Equal(1, CharacterMainControl.SlotSubscribers, "one lifecycle listener");
        Hit(Enemy()); Step(5.1f);
        Equal(0, RollCallLedgerRuntime.CurrentMarkCount, "window expires without another hit");
        Expect(Driver() == null, "empty window destroys driver");
        Start(true, true); Hit(Enemy());
        Equal(0, Health.HurtSubscribers, "base level disables item");
        Expect(Driver() == null, "base level creates no driver");
    }

    private static void StaleDriver()
    {
        Start(); Hit(Enemy()); RollCallLedgerDriver old = Driver();
        Equip(false); Equip(true); Hit(Enemy());
        RollCallLedgerDriver fresh = Driver();
        RollCallLedgerRuntime.OnDriverDestroyed(old);
        Equal(1, RollCallLedgerRuntime.CurrentMarkCount, "stale destroyed driver does not clear new marks");
        Expect(ReferenceEquals(fresh, Driver()), "new driver remains owner");
    }
}

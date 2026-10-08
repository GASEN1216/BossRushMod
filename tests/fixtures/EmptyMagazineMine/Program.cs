using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
using UObject = UnityEngine.Object;

internal static class Program
{
    private static int assertions;
    private static int scenarios;
    private static CharacterMainControl player;
    private static ItemAgent_Gun gun;

    private static int Main()
    {
        try
        {
            Run("six shots deploy only when the magazine becomes empty", SixShotsAndLastRound);
            Run("a short magazine cannot qualify; a pending mine cannot stack", RulesShortMagazineAndPending);
            Run("reload then cancel starts a new counting cycle", CancelledReload);
            Run("reload completion and gun swaps clear the old magazine", LoadedAndSwapped);
            Run("a shotgun shot counts once regardless of pellet count", ShotgunAndSubscriptionLifecycle);
            Run("cold empty magazines are discarded without deferred deployment", CooldownDoesNotBackfill);
            Run("unequip cancels the object and does not wash cooldown", UnequipPreservesCooldown);
            Run("death cancels a mine and clears the next life's state", DeathAndRevival);
            Run("scene transition destroys the old graph before a new owner binds", SceneTransition);
            Run("scene loading begins before fade and before target level initialization", SceneFadeCancelsArmedMine);
            Run("scene-loading flag blocks damage even without the start notification", SceneLoadingFlagBlocksDamage);
            Run("scene loading inside damage stops the rest of the blast", SceneStartsDuringBlast);
            Run("a delayed old driver callback cannot erase a new mine", StaleMineDestroyed);
            Run("pause freezes the fuse; a broken visual cannot stop damage", PauseAndVisualFailure);
            Run("blast obeys current team, companions, occlusion and Health deduplication", FriendlyFireAndOcclusion);
            Run("equipment invalidation during damage stops the remaining targets", InvalidationDuringDamage);
            Run("backpack, NPC and base contexts remain inactive", InactiveContexts);
            Cleanup();
            Console.WriteLine("EmptyMagazineMine: " + scenarios + " scenarios / " + assertions + " assertions PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            foreach (string log in ModBehaviour.Logs) Console.Error.WriteLine(log);
            return 1;
        }
        finally { EmptyMagazineMineRuntime.Unsubscribe(); GameObject.DestroyScene(); }
    }

    private static void Run(string name, Action scenario)
    {
        scenario();
        scenarios++;
        Console.WriteLine("PASS " + name);
    }

    private static void Expect(bool result, string name)
    {
        assertions++;
        if (!result) throw new InvalidOperationException("Assertion failed: " + name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual);
    }

    private static void Cleanup()
    {
        EmptyMagazineMineRuntime.Unsubscribe();
        // 每次用例都必经真实对象图销毁，不把 Unity 已销毁对象等于 null 只当成可选边界。
        GameObject.DestroyScene();
        CharacterMainControl.Main = null;
        player = null;
        gun = null;
        Equal(0, ItemAgent_Gun.ShootSubscribers, "static gun event detached");
        Equal(0, CharacterMainControl.HoldSubscribers, "hold event detached");
        Equal(0, CharacterMainControl.SlotSubscribers, "slot event detached");
        Equal(0, Health.DeathSubscribers, "death event detached");
        Equal(0, LevelManager.BeginSubscribers, "scene begin detached");
        Equal(0, LevelManager.EndSubscribers, "scene complete detached");
        Equal(0, SceneLoader.StartedSubscribers, "scene-loading start detached");
        SceneLoader.FinishScene();
        Physics.Reset();
        PetNestCompanionAgent.Companions.Clear();
        EmptyMagazineMineFx.Created = EmptyMagazineMineFx.Exploded = 0;
        EmptyMagazineMineFx.ThrowDuringFuse = false;
        NewWeaponEquipState.Equipped = false;
        ModBehaviour.Spectating = false;
        ModBehaviour.Logs.Clear();
        BossRushUI.Paused = false;
        Time.time = 100f;
        Time.deltaTime = 0f;
    }

    private static void Start(int bullets, bool equipped = true)
    {
        Cleanup();
        LevelManager.Instance = new LevelManager();
        player = Character("player", Teams.player);
        CharacterMainControl.Main = player;
        gun = Gun(player, bullets);
        player.CurrentHoldItemAgent = gun;
        EmptyMagazineMineRuntime.Subscribe();
        if (equipped) Equip(true);
    }

    private static CharacterMainControl Character(string name, Teams team)
    {
        GameObject go = new GameObject(name);
        CharacterMainControl value = go.AddComponent<CharacterMainControl>();
        value.Team = team;
        value.Health = go.AddComponent<Health>();
        value.Health.Character = value;
        return value;
    }

    private static ItemAgent_Gun Gun(CharacterMainControl holder, int bullets)
    {
        ItemAgent_Gun value = new GameObject("gun").AddComponent<ItemAgent_Gun>();
        value.Holder = holder;
        value.BulletCount = bullets;
        return value;
    }

    private static Collider Receiver(CharacterMainControl target, float x)
    {
        GameObject go = new GameObject("damage receiver");
        go.transform.SetParent(target.transform, false);
        DamageReceiver receiver = go.AddComponent<DamageReceiver>();
        receiver.health = target.Health;
        Collider collider = go.AddComponent<Collider>();
        collider.bounds = new Bounds { center = new Vector3(x, 0.7f, 0f) };
        return collider;
    }

    private static void Equip(bool value)
    {
        NewWeaponEquipState.Equipped = value;
        player.EquipmentChanged();
    }

    private static void Fire(int count, int pellets = 1)
    {
        for (int i = 0; i < count; i++) gun.Fire(pellets);
    }

    private static EmptyMagazineMineDriver Pending()
    {
        foreach (GameObject go in GameObject.All)
        {
            if (go == null || !go.Active) continue;
            EmptyMagazineMineDriver driver = go.GetComponent<EmptyMagazineMineDriver>();
            if (driver != null) return driver;
        }
        return null;
    }

    private static void Step(float seconds)
    {
        Time.deltaTime = seconds;
        if (!BossRushUI.Paused) Time.time += seconds;
        EmptyMagazineMineDriver mine = Pending();
        if (mine != null) EmptyMagazineMineRuntime.Tick(mine);
    }

    private static void SixShotsAndLastRound()
    {
        Start(8);
        Fire(6);
        Equal(6, EmptyMagazineMineRuntime.CurrentShotCount, "six recorded shots");
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "two rounds remain, no mine");
        Equal(0, EmptyMagazineMineFx.Created, "no visual prewarm before deployment");
        Equal(0, Physics.OverlapCalls, "no scan while only charging");
        Fire(1);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "one round remains");
        Fire(1);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "eighth shot empties the magazine");
        Equal(0, EmptyMagazineMineRuntime.CurrentShotCount, "empty magazine consumes its sequence");
        Equal(1, EmptyMagazineMineFx.Created, "exactly one armed visual");
        Equal(0, Physics.OverlapCalls, "fuse does not scan before expiration");
    }

    private static void RulesShortMagazineAndPending()
    {
        EmptyMagazineMineRules rules = new EmptyMagazineMineRules(6, 6f);
        for (int remaining = 4; remaining >= 0; remaining--)
            Expect(!rules.ObserveShot(11, remaining, 0f, false), "five-round magazine cannot deploy");
        for (int remaining = 5; remaining >= 0; remaining--)
            Expect(!rules.ObserveShot(11, remaining, 7f, true), "armed entity blocks another magazine");
        Equal(0f, rules.NextDeployAt, "blocked attempt does not consume a future cooldown");
        for (int remaining = 5; remaining >= 1; remaining--)
            Expect(!rules.ObserveShot(11, remaining, 8f, false), "second magazine not empty yet");
        Expect(rules.ObserveShot(11, 0, 8f, false), "six clean shots can deploy after pending clears");
        Equal(14f, rules.NextDeployAt, "cooldown measured from successful deployment");
        rules.ResetAll();
        Equal(0, rules.ShotCount, "full reset removes shot history");
        Equal(0f, rules.NextDeployAt, "full reset removes old-life cooldown");
    }

    private static void CancelledReload()
    {
        Start(10);
        Fire(5);
        Equal(5, EmptyMagazineMineRuntime.CurrentShotCount, "five shots before reload");
        player.BeginReload();
        Equal(0, EmptyMagazineMineRuntime.CurrentShotCount, "reload starts, without waiting for completion");
        player.CancelReload();
        Fire(5);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "cancelled reload does not recover the old five shots");
        gun.FinishLoading(6);
        Fire(6);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "fresh six-round magazine qualifies");
    }

    private static void LoadedAndSwapped()
    {
        Start(10);
        Fire(4);
        gun.FinishLoading(10);
        Equal(0, EmptyMagazineMineRuntime.CurrentShotCount, "loaded callback clears prior count");
        Fire(5);
        ItemAgent_Gun original = gun;
        ItemAgent_Gun second = Gun(player, 5);
        player.Hold(second);
        gun = second;
        Equal(0, original.LoadedSubscribers, "old weapon event detached");
        Equal(1, second.LoadedSubscribers, "new weapon event bound once");
        Fire(5);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "two guns cannot combine their shots");
        player.Hold(original);
        gun = original;
        Fire(5);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "switching back does not restore old progress");
    }

    private static void ShotgunAndSubscriptionLifecycle()
    {
        Start(6);
        EmptyMagazineMineRuntime.Subscribe();
        Equal(1, ItemAgent_Gun.ShootSubscribers, "Subscribe is idempotent");
        Equal(1, player.ActionSubscribers, "one player action owner");
        Equal(1, gun.LoadedSubscribers, "one gun load owner");
        Fire(1, 12);
        Equal(12, gun.PelletsEmitted, "fixture emitted twelve pellets");
        Equal(1, EmptyMagazineMineRuntime.CurrentShotCount, "pellets count as a single official firing event");
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "pellet count cannot arm prematurely");
        Fire(5, 12);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "six actual shotgun shots qualify");
        ItemAgent_Gun oldGun = gun;
        CharacterMainControl oldPlayer = player;
        EmptyMagazineMineDriver oldMine = Pending();
        EmptyMagazineMineRuntime.Unsubscribe();
        EmptyMagazineMineRuntime.Unsubscribe();
        Equal(0, oldGun.LoadedSubscribers, "cleanup detaches gun instance callback");
        Equal(0, oldPlayer.ActionSubscribers, "cleanup detaches player instance callback");
        Expect(oldMine == null && oldMine.Destroyed, "cleanup destroys armed object");
    }

    private static void CooldownDoesNotBackfill()
    {
        Start(6);
        Fire(6);
        Step(1.5f);
        Equal(1, EmptyMagazineMineFx.Exploded, "first mine completed");
        gun.FinishLoading(6);
        Fire(6);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "emptying during cooldown is discarded");
        Step(10f);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "time alone cannot backfill the discarded mine");
        Equal(1, EmptyMagazineMineFx.Created, "still only the first visual");
        gun.FinishLoading(6);
        Fire(6);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "new qualified empty magazine after cooldown");
    }

    private static void UnequipPreservesCooldown()
    {
        Start(6);
        Fire(6);
        EmptyMagazineMineDriver oldMine = Pending();
        EmptyMagazineMineFx oldVisual = oldMine.gameObject.transform.Children[0].gameObject.GetComponent<EmptyMagazineMineFx>();
        Equip(false);
        Expect(oldMine == null && oldVisual == null, "unequip destroys mine and its child visual");
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "unequip cancels pending state");
        Equal(0, player.ActionSubscribers, "unequip removes per-player work");
        Equip(true);
        gun.FinishLoading(6);
        Fire(6);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "re-equipping at the same time cannot bypass cooldown");
        Step(6f);
        gun.FinishLoading(6);
        Fire(6);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "cooldown expires normally after re-equipping");
    }

    private static void DeathAndRevival()
    {
        Start(6);
        Fire(6);
        EmptyMagazineMineDriver oldMine = Pending();
        player.Health.Die();
        Expect(oldMine == null, "death destroys the armed mine");
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "death clears pending state");
        Equal(0, EmptyMagazineMineRuntime.CurrentShotCount, "death clears shot history");
        player.Health.IsDead = false;
        Equip(true);
        gun.FinishLoading(6);
        Fire(6);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "new life has no inherited cooldown");
    }

    private static void SceneTransition()
    {
        Start(6);
        Fire(6);
        ItemAgent_Gun oldGun = gun;
        CharacterMainControl oldPlayer = player;
        EmptyMagazineMineDriver oldMine = Pending();
        LevelManager.BeginLevel();
        GameObject.DestroyScene();
        Expect(oldPlayer == null && oldGun == null && oldMine == null, "scene unload destroys components and returns Unity-null");
        Equal(0, oldGun.LoadedSubscribers, "old destroyed gun has no retained delegate");
        Equal(0, oldPlayer.ActionSubscribers, "old destroyed player has no retained delegate");
        player = Character("next scene player", Teams.player);
        CharacterMainControl.Main = player;
        gun = Gun(player, 6);
        player.CurrentHoldItemAgent = gun;
        LevelManager.FinishLevel();
        Fire(6);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "new scene owner binds cleanly");
        Step(1.5f);
        Equal(1, EmptyMagazineMineFx.Exploded, "only the new scene mine explodes");
    }

    private static void SceneFadeCancelsArmedMine()
    {
        Start(6);
        CharacterMainControl enemy = Character("enemy during fade", Teams.wolf);
        Physics.Hits = new[] { Receiver(enemy, 1f) };
        Fire(6);
        EmptyMagazineMineDriver oldMine = Pending();
        Step(1.3f);
        SceneLoader.BeginScene();
        Expect(player != null && enemy != null, "old scene remains alive during fade");
        Expect(oldMine == null && !EmptyMagazineMineRuntime.HasPendingMine, "scene start immediately cancels mine before level init");
        Step(0.3f);
        Equal(0, enemy.Health.Hits.Count, "remaining fuse cannot explode during fade");
        Equal(0, EmptyMagazineMineFx.Exploded, "cancelled mine has no explosion presentation");
        gun.FinishLoading(6);
        Fire(6);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "cannot deploy into a departing scene");
        // 加载尝试恢复到旧场景时，没有目标 LevelManager 的初始化事件。
        SceneLoader.FinishScene();
        gun.FinishLoading(6);
        Fire(6);
        Expect(EmptyMagazineMineRuntime.HasPendingMine, "ending a loading attempt restores normal use without target level init");
    }

    private static void SceneLoadingFlagBlocksDamage()
    {
        Start(6);
        CharacterMainControl enemy = Character("enemy at loading boundary", Teams.wolf);
        Physics.Hits = new[] { Receiver(enemy, 1f) };
        Fire(6);
        SceneLoader.IsSceneLoading = true;
        Step(1.5f);
        Equal(0, enemy.Health.Hits.Count, "loading state independently blocks explosion");
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "loading-state fallback clears pending object");
    }

    private static void SceneStartsDuringBlast()
    {
        Start(6);
        CharacterMainControl first = Character("first enemy", Teams.wolf);
        CharacterMainControl next = Character("enemy after transition", Teams.wolf);
        Physics.Hits = new[] { Receiver(first, 1f), Receiver(next, 2f) };
        first.Health.OnReceive = info => SceneLoader.BeginScene();
        Fire(6);
        Step(1.5f);
        Equal(1, first.Health.Hits.Count, "first target receives the hit that starts loading");
        Equal(0, next.Health.Hits.Count, "no more targets receive damage after loading starts");
    }

    private static void StaleMineDestroyed()
    {
        Start(6);
        Fire(6);
        EmptyMagazineMineDriver oldMine = Pending();
        Equip(false);
        Step(6f);
        Equip(true);
        gun.FinishLoading(6);
        Fire(6);
        EmptyMagazineMineDriver newMine = Pending();
        // 真实 Unity 在帧末回调 OnDestroy，模拟旧 driver 的迟到通知。
        EmptyMagazineMineRuntime.OnMineDestroyed(oldMine);
        Expect(EmptyMagazineMineRuntime.HasPendingMine && ReferenceEquals(newMine, Pending()), "old callback preserves new pending mine");
    }

    private static void PauseAndVisualFailure()
    {
        Start(6);
        CharacterMainControl enemy = Character("enemy", Teams.wolf);
        Physics.Hits = new[] { Receiver(enemy, 1f) };
        Fire(6);
        BossRushUI.Paused = true;
        Step(20f);
        Equal(0, EmptyMagazineMineFx.Exploded, "pause does not advance fuse even with supplied delta");
        Equal(0, enemy.Health.Hits.Count, "pause cannot damage enemies");
        BossRushUI.Paused = false;
        EmptyMagazineMineFx.ThrowDuringFuse = true;
        Step(0.75f);
        Equal(0, enemy.Health.Hits.Count, "half fuse is not enough");
        Step(0.75f);
        Equal(1, enemy.Health.Hits.Count, "visual exception does not swallow the gameplay explosion");
    }

    private static void FriendlyFireAndOcclusion()
    {
        Start(6);
        player.Team = Teams.red;
        CharacterMainControl ally = Character("Mode E ally", Teams.red);
        CharacterMainControl companion = Character("pet with hostile-looking team", Teams.blue);
        PetNestCompanionAgent.Companions.Add(companion.Health);
        CharacterMainControl neutral = Character("neutral", Teams.middle);
        CharacterMainControl dead = Character("dead", Teams.blue);
        dead.Health.IsDead = true;
        CharacterMainControl obstructed = Character("behind wall", Teams.blue);
        CharacterMainControl enemy = Character("exposed enemy", Teams.blue);
        Physics.Hits = new[] { Receiver(player, 0f), Receiver(ally, 0.5f), Receiver(companion, 0.6f),
            Receiver(neutral, 0.7f), Receiver(dead, 0.8f), Receiver(obstructed, 2f), Receiver(enemy, 1f), Receiver(enemy, 1.1f) };
        Physics.IsBlocked = point => point.x == 2f;
        Fire(6);
        Step(1.5f);
        Equal(0, player.Health.Hits.Count, "player is safe");
        Equal(0, ally.Health.Hits.Count, "current-team ally is safe even outside Teams.player");
        Equal(0, companion.Health.Hits.Count, "companion override is safe");
        Equal(0, neutral.Health.Hits.Count, "neutral is not an enemy");
        Equal(0, dead.Health.Hits.Count, "dead targets skipped");
        Equal(0, obstructed.Health.Hits.Count, "wall blocks the blast");
        Equal(1, enemy.Health.Hits.Count, "multiple colliders damage one Health once");
        Equal(4f, Physics.LastRadius, "four-meter search radius");
        Equal(3, Physics.LastWallMask, "wall and ground both participate in occlusion");
        DamageInfo damage = enemy.Health.Hits[0];
        Equal(160f, damage.damageValue, "configured physical blast input");
        Expect(damage.isFromBuffOrEffect && damage.isExplosion, "effect/explosion attribution prevents direct-hit recursion");
        Equal(0, damage.fromWeaponItemID, "blast is not a weapon direct hit");
        Expect(ReferenceEquals(player, damage.fromCharacter), "player receives kill attribution");
    }

    private static void InvalidationDuringDamage()
    {
        Start(6);
        CharacterMainControl first = Character("first enemy", Teams.wolf);
        CharacterMainControl second = Character("second enemy", Teams.wolf);
        Physics.Hits = new[] { Receiver(first, 1f), Receiver(second, 1.5f) };
        first.Health.OnReceive = info => Equip(false);
        Fire(6);
        Step(1.5f);
        Equal(1, first.Health.Hits.Count, "first target resolves before invalidation");
        Equal(0, second.Health.Hits.Count, "invalidated owner cannot continue to later targets");
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "damage callback does not resurrect a pending entity");
    }

    private static void InactiveContexts()
    {
        Start(6, false);
        Fire(6);
        Equal(0, EmptyMagazineMineRuntime.CurrentShotCount, "backpack-only item is inactive");
        Equal(0, EmptyMagazineMineFx.Created, "no inactive equipment resources");
        Equip(true);
        CharacterMainControl npc = Character("NPC", Teams.wolf);
        ItemAgent_Gun npcGun = Gun(npc, 6);
        for (int i = 0; i < 6; i++) npcGun.Fire();
        Equal(0, EmptyMagazineMineRuntime.CurrentShotCount, "foreign gun event cannot charge the main character");
        LevelManager.Instance.IsBaseLevel = true;
        gun.FinishLoading(6);
        Fire(6);
        Expect(!EmptyMagazineMineRuntime.HasPendingMine, "base level has no deployed mine");
        Equal(0, Physics.OverlapCalls, "all inactive contexts avoid scans");
    }
}

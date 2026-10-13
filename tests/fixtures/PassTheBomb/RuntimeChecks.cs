using System;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static class RuntimeChecks
{
    private static int checks;
    private static CharacterMainControl player, enemyA, enemyB;

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static CharacterMainControl Character(string name, Teams team, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var character = go.AddComponent<CharacterMainControl>();
        character.Team = team;
        character.Health = go.AddComponent<Health>();
        character.Health.Character = character;
        return character;
    }

    private static void Fresh(bool equipped = true)
    {
        PassTheBombRuntime.Unsubscribe();
        Check(Health.HurtSubscribers == 0 && Health.DeathSubscribers == 0
            && CharacterMainControl.HoldSubscribers == 0 && CharacterMainControl.SlotSubscribers == 0
            && SceneLoader.Subscribers == 0 && SceneLoader.FinishSubscribers == 0
            && LevelManager.BeginSubscribers == 0 && LevelManager.EndSubscribers == 0, "all subscriptions released");
        GameObject.DestroyScene();
        PassTheBombFx.ResetStaticCaches();
        CharacterMainControl.Main = null;
        player = Character("Player", Teams.player, new Vector3());
        CharacterMainControl.Main = player;
        enemyA = Character("Enemy A", Teams.wolf, new Vector3(2f, 0f, 0f));
        enemyB = Character("Enemy B", Teams.wolf, new Vector3(4f, 0f, 0f));
        Time.time = 10f;
        Time.deltaTime = 0.02f;
        NewWeaponEquipState.Equipped = equipped;
        BossRushUI.Hidden = BossRushUI.Paused = false;
        BossRushUI.CanvasCreated = 0;
        SceneLoader.IsSceneLoading = false;
        LevelManager.Instance = new LevelManager();
        Camera.main = new Camera();
        Physics.Hits = new Collider[0];
        Physics.Blocked = null;
        Physics.Scans = 0;
        EmptyMagazineMineFx.Explosions = 0;
        PetNestCompanionAgent.Companions.Clear();
        ModBehaviour.Logs.Clear();
        PassTheBombRuntime.Subscribe();
        PassTheBombRuntime.Subscribe();
        Check(CharacterMainControl.HoldSubscribers == 1 && CharacterMainControl.SlotSubscribers == 1
            && Health.DeathSubscribers == 1 && Health.HurtSubscribers == (equipped ? 1 : 0)
            && SceneLoader.Subscribers == 1 && SceneLoader.FinishSubscribers == 1
            && LevelManager.BeginSubscribers == 1 && LevelManager.EndSubscribers == 1, "subscribe is idempotent and hurt subscription follows equipped owner");
    }

    private static T First<T>() where T : class
    {
        foreach (GameObject go in GameObject.All)
            if (go != null && go.Active)
            {
                T value = go.GetComponent<T>();
                if (value != null) return value;
            }
        return null;
    }

    private static int Count<T>() where T : class
    {
        int count = 0;
        foreach (GameObject go in GameObject.All)
            if (go != null && go.Active && go.GetComponent<T>() != null) count++;
        return count;
    }

    private static Action LateUpdate(PassTheBombFx fx)
    {
        return (Action)Delegate.CreateDelegate(typeof(Action), fx,
            typeof(PassTheBombFx).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    private static void Arm()
    {
        enemyA.Health.DirectHit(player, 1f);
        Check(PassTheBombRuntime.HasPendingBomb && PassTheBombRuntime.CarrierId == enemyA.Health.GetInstanceID(), "real OnHurt arms the directly hit enemy");
    }

    private static void Tick(float time)
    {
        Time.time = time;
        PassTheBombRuntime.Tick(First<PassTheBombDriver>());
    }

    private static Collider Receiver(Health health, Vector3 point)
    {
        var go = new GameObject("Damage receiver");
        go.AddComponent<DamageReceiver>().health = health;
        var collider = go.AddComponent<Collider>();
        collider.bounds = new Bounds { center = point };
        return collider;
    }

    private static void RealDamageRelay()
    {
        Fresh(); Arm();
        var driver = First<PassTheBombDriver>();
        var fx = First<PassTheBombFx>();
        var canvas = First<Canvas>();
        Check(driver != null && fx != null && canvas != null, "arm creates real runtime driver and complete FX graph");
        Time.time = 11f;
        enemyB.Health.DirectHit(player, 1f);
        Check(PassTheBombRuntime.CarrierId == enemyA.Health.GetInstanceID(), "player attacking another enemy does not move A's bomb");
        player.Health.DirectHit(enemyA, 1f);
        Check(PassTheBombRuntime.CarrierId == player.Health.GetInstanceID(), "A's actual OnHurt attack sends bomb back to player");
        Time.time = 12f;
        enemyB.Health.DirectHit(player, 1f);
        Check(PassTheBombRuntime.CarrierId == enemyB.Health.GetInstanceID(), "player as current carrier passes bomb to B");
        enemyA.Health.DirectHit(enemyB, 1f, effect: true);
        Check(PassTheBombRuntime.CarrierId == enemyB.Health.GetInstanceID(), "buff, reflection and bomb damage cannot relay");
        Check(PassTheBombRuntime.RemainingSeconds == 3f && PassTheBombRuntime.NextTriggerAt == 22f, "real relay preserves original fuse and cooldown");
        Check(ReferenceEquals(driver, First<PassTheBombDriver>()) && ReferenceEquals(fx, First<PassTheBombFx>())
            && ReferenceEquals(canvas, First<Canvas>()) && BossRushUI.CanvasCreated == 1, "all relays reuse same driver, FX and canvas");
        Check(Count<PassTheBombDriver>() == 1 && Count<Canvas>() == 1, "single active bomb owns one driver and canvas");
        Time.time = 15f;
        enemyA.Health.DirectHit(enemyB, 1f);
        Check(PassTheBombRuntime.CarrierId == enemyB.Health.GetInstanceID(), "real hit at fuse deadline cannot save B");
        PassTheBombRuntime.Tick(driver);
        Check(enemyB.Health.Hits.Count == 1 && enemyB.Health.Hits[0].damageValue == PassTheBombConfig.BlastDamage, "carrier receives exactly one configured blast");
        Check(enemyB.Health.Hits[0].isFromBuffOrEffect && enemyB.Health.Hits[0].isExplosion
            && enemyB.Health.Hits[0].fromWeaponItemID == 0, "blast is attributed to an equipment effect, never a direct weapon hit");
        Check(!PassTheBombRuntime.HasPendingBomb && EmptyMagazineMineFx.Explosions == 1, "effect callbacks cannot arm another bomb while detonating");
        Check(driver == null && fx == null && canvas == null && Count<Canvas>() == 0, "completion destroys full Unity object graph");
    }

    private static void PauseAndVisibility()
    {
        Fresh(); Arm();
        var canvas = First<Canvas>();
        Action present = LateUpdate(First<PassTheBombFx>());
        Tick(11f); present();
        Check(canvas.enabled && PassTheBombRuntime.RemainingSeconds == 4f, "active countdown follows real runtime time");
        BossRushUI.Hidden = true; present();
        Check(!canvas.enabled, "official views hide countdown in real LateUpdate");
        BossRushUI.Hidden = false; BossRushUI.Paused = true;
        Tick(40f); present();
        Check(!canvas.enabled && PassTheBombRuntime.HasPendingBomb && PassTheBombRuntime.RemainingSeconds == 4f,
            "pause freezes fuse even when host clock keeps advancing");
        BossRushUI.Paused = false;
        Tick(41f); present();
        Check(canvas.enabled && PassTheBombRuntime.RemainingSeconds == 3f, "unpause resumes remaining fuse");
        Camera.main.Projection = new Vector3(0.5f, 0.5f, -1f); present();
        Check(!canvas.enabled, "marker behind camera is hidden");
        Tick(44f);
        Check(!PassTheBombRuntime.HasPendingBomb && EmptyMagazineMineFx.Explosions == 1, "paused duration is excluded from detonation deadline");
    }

    private static void CarrierDeathLeavesGroundBomb()
    {
        Fresh(); Arm();
        enemyA.transform.position = new Vector3(8f, 0f, 9f);
        enemyA.Health.Die(new DamageInfo(player));
        enemyA.transform.position = new Vector3(80f, 0f, 90f);
        player.Health.DirectHit(enemyA, 1f);
        Check(PassTheBombRuntime.CarrierId != player.Health.GetInstanceID(), "dead carrier's projectile cannot pick bomb back up");
        Tick(15f);
        Check(EmptyMagazineMineFx.Explosions == 1 && EmptyMagazineMineFx.LastPosition.x == 8f
            && EmptyMagazineMineFx.LastPosition.z == 9f, "dead carrier leaves timed blast at recorded death position");

        Fresh();
        enemyA.Health.DirectHit(player, 1f, kill: true);
        Check(PassTheBombRuntime.HasPendingBomb, "official OnDead-before-OnHurt order allows fatal first hit to leave a bomb");
        Tick(15f);
        Check(EmptyMagazineMineFx.Explosions == 1, "fatal first-hit bomb still reaches normal detonation");
    }

    private static void CancellationAndLifetime()
    {
        Fresh(); Arm();
        var canvas = First<Canvas>();
        player.HoldChanged();
        Check(PassTheBombRuntime.HasPendingBomb && ReferenceEquals(canvas, First<Canvas>()), "switching held weapon retains equipped totem's bomb");
        NewWeaponEquipState.Equipped = false; player.EquipmentChanged();
        Check(!PassTheBombRuntime.HasPendingBomb && canvas == null && Count<PassTheBombDriver>() == 0
            && Health.HurtSubscribers == 0 && PassTheBombRuntime.NextTriggerAt == 22f, "unequip cancels graph and subscription while preserving cooldown");
        NewWeaponEquipState.Equipped = true; player.EquipmentChanged();
        Time.time = 12f; enemyB.Health.DirectHit(player, 1f);
        Check(!PassTheBombRuntime.HasPendingBomb, "reequip cannot refund trigger cooldown");
        Time.time = 22f; enemyB.Health.DirectHit(player, 1f);
        Check(PassTheBombRuntime.HasPendingBomb, "reequipped totem works again after original cooldown");
        var active = First<PassTheBombDriver>();
        UnityEngine.Object.Destroy(active.gameObject);
        Check(!PassTheBombRuntime.HasPendingBomb && Count<Canvas>() == 0 && PassTheBombRuntime.NextTriggerAt == 34f,
            "unexpected driver destruction clears state and descendants without refund");

        foreach (Action cancel in new Action[] {
            () => SceneLoader.Begin(),
            () => LevelManager.Begin(),
            () => player.Health.Die(new DamageInfo(enemyA)),
            () => PassTheBombRuntime.Unsubscribe()
        })
        {
            Fresh(); Arm(); cancel();
            Check(!PassTheBombRuntime.HasPendingBomb && Count<Canvas>() == 0 && Count<PassTheBombDriver>() == 0,
                "scene, initialization, player death and shutdown destroy complete bomb graph");
            Check(Health.HurtSubscribers == 0, "cancellation releases damage listener");
        }
        Fresh(); Arm(); SceneLoader.Begin(); SceneLoader.Finish();
        Check(Health.HurtSubscribers == 1, "scene-finished event binds still-equipped player");
        enemyB.Health.DirectHit(player, 1f);
        Check(PassTheBombRuntime.HasPendingBomb, "new scene resets old scene cooldown");
    }

    private static void DamageAndAllyFiltering()
    {
        Fresh(); Arm();
        var ally = Character("Ally carrier", Teams.player, new Vector3(1f, 0f, 0f));
        var bystander = Character("Friendly bystander", Teams.player, new Vector3(2f, 0f, 0f));
        var neutral = Character("Neutral bystander", Teams.middle, new Vector3());
        ally.Health.DirectHit(enemyA, 1f);
        Check(PassTheBombRuntime.CarrierId == ally.Health.GetInstanceID(), "current carrier may pass bomb to a friendly character it directly hits");
        Physics.Hits = new[] {
            Receiver(ally.Health, new Vector3(1f, 0f, 0f)), Receiver(ally.Health, new Vector3(1f, 0f, 0f)),
            Receiver(player.Health, new Vector3()), Receiver(player.Health, new Vector3()),
            Receiver(enemyA.Health, new Vector3(2f, 0f, 0f)), Receiver(enemyB.Health, new Vector3(9f, 0f, 0f)),
            Receiver(bystander.Health, new Vector3(2f, 0f, 0f)), Receiver(neutral.Health, new Vector3())
        };
        Physics.Blocked = (from, to) => to.x == 9f;
        Tick(15f);
        Check(ally.Health.Hits.Count == 1 && ally.Health.Hits[0].damageValue == 160f, "friendly carrier remains responsible for its bomb");
        Check(player.Health.Hits.Count == 1 && player.Health.Hits[0].damageValue == 40f, "player takes configured base blast damage only once across colliders");
        Check(enemyA.Health.Hits.Count == 1 && enemyA.Health.Hits[0].damageValue == 160f, "unobstructed enemy takes one configured blast");
        Check(enemyB.Health.Hits.Count == 0 && bystander.Health.Hits.Count == 0 && neutral.Health.Hits.Count == 0,
            "wall blocks enemy while friendly and neutral bystanders are excluded");
        Check(Physics.Scans == 1, "explosion performs exactly one range query");

        Fresh(); Arm(); player.Health.DirectHit(enemyA, 1f); Tick(15f);
        Check(player.Health.Hits.Count == 1 && player.Health.Hits[0].damageValue == 40f, "player carrying bomb receives player base damage even without overlap hit");

        Fresh(); Arm();
        enemyA.Health.OnReceive = info => SceneLoader.Begin();
        Physics.Hits = new[] { Receiver(enemyB.Health, new Vector3(1f, 0f, 0f)) };
        Tick(15f);
        Check(enemyA.Health.Hits.Count == 1 && enemyB.Health.Hits.Count == 0 && !PassTheBombRuntime.HasPendingBomb,
            "scene change nested in first blast hit cancels remaining old-scene damage");
    }

    private static void InactiveAndHotPath()
    {
        Fresh(false);
        enemyA.Health.DirectHit(player, 1f);
        Check(!PassTheBombRuntime.HasPendingBomb && BossRushUI.CanvasCreated == 0 && Count<PassTheBombDriver>() == 0,
            "unequipped totem has no damage listener, driver or canvas");
        NewWeaponEquipState.Equipped = true; player.EquipmentChanged();
        LevelManager.Instance.IsBaseLevel = true;
        enemyA.Health.DirectHit(player, 1f);
        Check(!PassTheBombRuntime.HasPendingBomb && BossRushUI.CanvasCreated == 0, "base level cannot arm");
        LevelManager.Instance.IsBaseLevel = false; player.EquipmentChanged();
        enemyA.Health.DirectHit(player, 0f);
        enemyA.Health.DirectHit(player, 1f, effect: true);
        enemyA.Health.DirectHit(enemyB, 1f);
        Check(!PassTheBombRuntime.HasPendingBomb, "zero damage, effects and unrelated NPC combat cannot start bomb");
        Arm();
        var driver = First<PassTheBombDriver>();
        Action present = LateUpdate(First<PassTheBombFx>());
        Time.time = 11f;
        for (int i = 0; i < 10000; i++) { PassTheBombRuntime.Tick(driver); present(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { PassTheBombRuntime.Tick(driver); present(); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0L, "warmed runtime and actual LateUpdate allocate no managed objects in isolated host: " + allocated);
        Check(Physics.Scans == 0 && BossRushUI.CanvasCreated == 1, "active countdown does not scan scene or rebuild canvas per frame");
        Check(ModBehaviour.Logs.Count == 0, "tested runtime paths have no swallowed exceptions");
    }

    internal static void Run()
    {
        RealDamageRelay();
        PauseAndVisibility();
        CarrierDeathLeavesGroundBomb();
        CancellationAndLifetime();
        DamageAndAllyFiltering();
        InactiveAndHotPath();
        PassTheBombRuntime.Unsubscribe();
        Console.WriteLine("PassTheBomb runtime and FX: " + checks + " checks passed.");
    }
}

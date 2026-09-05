using System;
using UnityEngine.SceneManagement;

public static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("FAIL: " + message);
    }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .001f; }
    private static void Scene(int handle, string name = "Base")
    {
        SceneManager.Current = new Scene { handle = handle, name = name };
    }
    private static PermanentDuckNpcModule Fresh()
    {
        foreach (var task in TaskCapture.Tasks) Check(task.IsCompleted && !task.IsFaulted, "previous operation finished cleanly");
        TaskCapture.Tasks.Clear();
        DuckNpcSpawner.Pending.Clear();
        DuckNpcSpawner.Despawns = 0;
        PermanentDuckNpcRegistry.Instance = null;
        PermanentDuckNpcRegistry.ThrowRegister = false;
        AffinityManager.Married = false;
        SceneManager.ThrowRead = false;
        Scene(1);
        ModBehaviour.Instance = new ModBehaviour();
        return new PermanentDuckNpcModule();
    }
    private static CharacterMainControl Complete(int index)
    {
        var npc = new CharacterMainControl();
        DuckNpcSpawner.Pending[index].Completion.SetResult(npc);
        return npc;
    }
    private static void NpcCases()
    {
        var module = Fresh();
        var owner = ModBehaviour.Instance;
        module.Spawn(owner);
        module.Destroy(owner); Scene(2, "B"); module.Spawn(owner);
        module.Destroy(owner); Scene(3, "C"); module.Spawn(owner);
        Check(DuckNpcSpawner.Pending.Count == 1 && module.Pending, "ABC remains serial while A awaits");
        var a = Complete(0);
        Check(DuckNpcSpawner.Pending.Count == 2 && DuckNpcSpawner.Pending[1].Handle == 3, "ABC starts only latest C");
        Check(a.Destroyed && PermanentDuckNpcRegistry.Instance == null, "A is recycled before C registers");
        Check(module.Busy && !module.Pending, "old A finally preserves C busy owner");
        module.Spawn(owner); module.Spawn(owner);
        var c = Complete(1);
        Check(PermanentDuckNpcRegistry.Instance == c && !c.Destroyed, "C registers once");
        Check(DuckNpcSpawner.Pending.Count == 2 && !module.Busy && !module.Pending, "duplicate C requests coalesce without spawning again");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); Scene(2); module.Spawn(owner); module.Destroy(owner);
        a = Complete(0);
        Check(a.Destroyed && DuckNpcSpawner.Pending.Count == 1, "Destroy cancels queued successor without resurrection");
        Check(!module.Busy && !module.Pending && PermanentDuckNpcRegistry.Instance == null, "Destroy leaves no pending or registered actor");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); module.Destroy(owner); Scene(2); module.Spawn(owner);
        DuckNpcSpawner.Pending[0].Completion.SetException(new InvalidOperationException("creation failed"));
        Check(DuckNpcSpawner.Pending.Count == 2 && module.Busy, "failed old creation still drains successor");
        c = Complete(1);
        Check(PermanentDuckNpcRegistry.Instance == c && !module.Busy, "successor survives old exception/finally");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); Scene(2); module.Spawn(owner);
        a = Complete(0);
        Check(a.Destroyed && DuckNpcSpawner.Pending.Count == 2, "same-name reload rejected by scene handle without Destroy event");
        Complete(1);

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); module.Spawn(owner); owner.Destroyed = true;
        a = Complete(0);
        Check(a.Destroyed && DuckNpcSpawner.Pending.Count == 1 && !module.Busy, "destroyed owner cannot register or drain pending");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); ModBehaviour.Instance = new ModBehaviour(); module.Spawn(ModBehaviour.Instance);
        a = Complete(0);
        Check(a.Destroyed && DuckNpcSpawner.Pending.Count == 2 && module.Busy, "replaced owner drops old actor and starts current owner");
        Complete(1);

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); module.Spawn(owner); PermanentDuckNpcModule.ResetStaticCaches();
        a = Complete(0);
        Check(a.Destroyed && DuckNpcSpawner.Pending.Count == 1 && !module.Busy, "static reset invalidates active and queued generations");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); AffinityManager.Married = true;
        a = Complete(0);
        Check(a.Destroyed && PermanentDuckNpcRegistry.Instance == null, "marriage while awaiting prevents ordinary registration");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner);
        var newer = new CharacterMainControl(); PermanentDuckNpcRegistry.Instance = newer;
        a = Complete(0);
        Check(a.Destroyed && !newer.Destroyed && PermanentDuckNpcRegistry.Instance == newer, "competing registration keeps newer instance");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); PermanentDuckNpcRegistry.ThrowRegister = true;
        a = Complete(0);
        Check(a.Destroyed && !module.Busy, "post-await exception recycles actor and releases busy");
        PermanentDuckNpcRegistry.ThrowRegister = false;
        module.Spawn(owner); Complete(1);
        Check(!module.Busy && PermanentDuckNpcRegistry.Instance != null, "retry after registration exception succeeds");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); module.Spawn(owner); SceneManager.ThrowRead = true;
        a = Complete(0);
        Check(a.Destroyed && !module.Busy && !module.Pending, "scene-read failure fails closed and clears busy");

        module = Fresh(); owner = ModBehaviour.Instance;
        module.Spawn(owner); Scene(2, "NotAllowed"); module.Spawn(owner);
        a = Complete(0);
        Check(a.Destroyed && DuckNpcSpawner.Pending.Count == 1 && !module.Busy, "successor rechecks allowed-scene policy");
        Fresh();
    }
    private static Health Prepare(float current, float oldBonus, float newBonus)
    {
        var main = new CharacterMainControl(); CharacterMainControl.Main = main;
        main.Health.CurrentHealth = current;
        ShowcaseService.Configure(main.Health, oldBonus, newBonus);
        return main.Health;
    }
    private static void HealthCases()
    {
        var health = Prepare(100, 0, .025f); ShowcaseService.ReapplyBonuses();
        Check(Near(health.CurrentHealth, 102.5f) && health.Heals == 1, "initial full health receives new maximum");
        health = Prepare(102, .02f, .025f); ShowcaseService.ReapplyBonuses();
        Check(Near(health.CurrentHealth, 102.5f) && health.Heals == 1, "previously full health receives increased bonus");
        health = Prepare(90, .02f, .025f); ShowcaseService.ReapplyBonuses();
        Check(Near(health.CurrentHealth, 90) && health.Heals == 0, "injured below base stays injured");
        health = Prepare(101, .02f, .025f); ShowcaseService.ReapplyBonuses();
        Check(Near(health.CurrentHealth, 101) && health.Heals == 0, "injured above base stays injured");
        ShowcaseService.ReapplyBonuses();
        Check(Near(health.CurrentHealth, 101) && health.Heals == 0 && ShowcaseService.Records == 1, "repeat refresh neither heals nor stacks");
        health = Prepare(102, .02f, .02f); ShowcaseService.ReapplyBonuses();
        Check(Near(health.CurrentHealth, 102) && health.Heals == 0, "unchanged full maximum causes no extra healing");
        health = Prepare(90, .02f, 0); ShowcaseService.ReapplyBonuses();
        Check(Near(health.MaxHealth, 100) && ShowcaseService.Records == 0 && health.Heals == 0, "empty collection removes old bonus without healing");
        health = Prepare(101, .02f, .025f); BackMountainUnlocks.Unlocked = false; ShowcaseService.ReapplyBonuses();
        Check(ShowcaseService.Records == 0 && health.Heals == 0, "locked facility removes old bonus");
        health = Prepare(100, .02f, .025f); CharacterMainControl.Main = null; ShowcaseService.ReapplyBonuses();
        Check(ShowcaseService.Records == 0 && Near(health.Bonus, 0), "missing player still clears previous owner records");
        health = Prepare(100, 0, .025f); CharacterMainControl.Main.Health = null; ShowcaseService.ReapplyBonuses();
        Check(ShowcaseService.Records == 0, "missing health stays no-throw");
        health = Prepare(100, 0, .025f); RuntimeStatModifierTracker.RejectAdd = true; ShowcaseService.ReapplyBonuses();
        Check(health.Heals == 0 && Near(health.MaxHealth, 100), "failed modifier application does not heal");
        health = Prepare(101, .02f, .025f); health.ThrowMax = true; ShowcaseService.ReapplyBonuses();
        Check(health.Heals == 0 && Near(health.Bonus, .025f), "health-read exception disables healing but preserves bonus refresh");
    }
    public static int Main()
    {
        try { NpcCases(); HealthCases(); Console.WriteLine("ContentSecondReview: PASS (" + assertions + " assertions)"); return 0; }
        catch (Exception error) { Console.WriteLine(error.Message); return 1; }
    }
}

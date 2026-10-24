using System;
using System.Reflection;
using BossRush;
using UnityEngine;

// 整个预警/弹体组件及基类来自生产源码；这里只替换时钟、场景和伤害出口。
internal static class ZombieThreatCases
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            Console.Error.WriteLine("FAIL ZombieThreat: " + message);
            throw new Exception("FAIL ZombieThreat: " + message);
        }
        checks++;
    }

    private static ModBehaviour NewOwner()
    {
        Time.unscaledTime = 0f;
        Time.unscaledDeltaTime = 0f;
        CharacterMainControl.Main = null;
        return ModBehaviour.Instance = new ModBehaviour { ZombieModeCurrentRunId = 7 };
    }

    private static void Tick(ZombieModeTimedRunScopedRuntime runtime, float now, float delta = 0f)
    {
        Time.unscaledTime = now;
        Time.unscaledDeltaTime = delta;
        if (runtime != null && runtime.isActiveAndEnabled)
            typeof(ZombieModeTimedRunScopedRuntime).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runtime, null);
    }

    private static ZombieModeTimedRunScopedRuntime Telegraph(int kind, CharacterMainControl source = null)
    {
        var go = new GameObject();
        if (kind == 0)
        {
            var runtime = go.AddComponent<ZombieModeTelegraphedAreaDamageRuntime>();
            runtime.Initialize(7, source, Vector3.zero, 3f, 20f, 0.5f, source != null);
            return runtime;
        }
        if (kind == 1)
        {
            var runtime = go.AddComponent<ZombieModeTelegraphedPlayerSlowRuntime>();
            runtime.Initialize(7, Vector3.zero, 3f, 0.5f, 2f, 0.5f);
            return runtime;
        }
        var cloud = go.AddComponent<ZombieModeTelegraphedDamageCloudRuntime>();
        cloud.Initialize(7, source, Vector3.zero, 3f, 4f, 10f, 0.5f, 0.5f,
            "FixtureCloud", new Color(), source != null, source != null);
        return cloud;
    }

    private static int Effects(ModBehaviour owner, int kind)
    {
        return kind == 0 ? owner.Impacts : kind == 1 ? owner.SlowApplications : owner.CloudSpawns;
    }

    private static void TelegraphCases()
    {
        for (int kind = 0; kind < 3; kind++)
        {
            var owner = NewOwner();
            var runtime = Telegraph(kind);
            Tick(runtime, 0.49f);
            Check(Effects(owner, kind) == 0, "warning must finish before effect, kind=" + kind);
            Tick(runtime, 0.75f);
            Tick(runtime, 0.9f);
            Check(Effects(owner, kind) == 1 && runtime == null,
                "hitch across warning and expiry resolves exactly once, kind=" + kind);

            owner = NewOwner(); runtime = Telegraph(kind);
            owner.Paused = true; Tick(runtime, 0.4f); Tick(runtime, 20f);
            owner.Paused = false; Tick(runtime, 20.05f);
            Check(Effects(owner, kind) == 0, "pause retains warning time, kind=" + kind);
            Tick(runtime, 20.4f);
            Check(Effects(owner, kind) == 1, "resumed warning survives a hitch, kind=" + kind);

            owner = NewOwner(); runtime = Telegraph(kind);
            owner.ZombieModeCurrentRunId++;
            Tick(runtime, 1f);
            Check(Effects(owner, kind) == 0 && runtime == null, "old run cannot resolve a warning, kind=" + kind);

            if (kind != 1)
            {
                owner = NewOwner();
                var source = new CharacterMainControl { Health = new GameObject().AddComponent<Health>() };
                runtime = Telegraph(kind, source);
                source.Health.CurrentHealth = 0f;
                Tick(runtime, 1f);
                Check(Effects(owner, kind) == 0, "dead followed source cancels an overdue warning, kind=" + kind);
            }
        }
    }

    private static ZombieModeHarasserProjectileRuntime Projectile(Vector3 target)
    {
        var projectile = new GameObject().AddComponent<ZombieModeHarasserProjectileRuntime>();
        projectile.Initialize(7, null, Vector3.zero, target, 10f, 25f, 3.5f, 3.5f, 0.5f, 2f);
        return projectile;
    }

    private static void ProjectileCases()
    {
        var owner = NewOwner();
        CharacterMainControl.Main = new CharacterMainControl();
        CharacterMainControl.Main.transform.position = new Vector3(1.5f, 0f, 0f);
        var projectile = Projectile(new Vector3(10f, 0f, 0f));
        Tick(projectile, 0.3f, 0.3f);
        Tick(projectile, 0.6f, 0.3f);
        Check(owner.ProjectileImpacts == 1 && projectile == null,
            "a projectile crossing the player between frames hits once");

        owner = NewOwner();
        CharacterMainControl.Main = new CharacterMainControl();
        CharacterMainControl.Main.transform.position = new Vector3(1.5f, 0f, 2f);
        projectile = Projectile(new Vector3(1.5f, 0f, 0f));
        Tick(projectile, 0.3f, 0.3f);
        Check(owner.ProjectileImpacts == 0 && projectile == null,
            "crossing an abandoned target point completes a successful dodge");

        owner = NewOwner();
        CharacterMainControl.Main = new CharacterMainControl();
        CharacterMainControl.Main.transform.position = new Vector3(3f, 0f, 0f);
        projectile = Projectile(new Vector3(1.5f, 0f, 0f));
        Tick(projectile, 0.4f, 0.4f);
        Check(owner.ProjectileImpacts == 0 && projectile == null,
            "swept detection stops at the abandoned target instead of hitting farther behind it");

        owner = NewOwner();
        CharacterMainControl.Main = new CharacterMainControl();
        CharacterMainControl.Main.transform.position = new Vector3(1.5f, 0f, 2f);
        projectile = Projectile(new Vector3(10f, 0f, 0f));
        Tick(projectile, 0.3f, 0.3f);
        Check(owner.ProjectileImpacts == 0 && projectile != null,
            "a player outside the swept hit radius is not hit");
        Tick(projectile, 4f, 0.1f);
        Check(owner.ProjectileImpacts == 0 && projectile == null, "expired projectile does not cause invisible damage");
        CharacterMainControl.Main = null;
    }

    internal static void Run()
    {
        TelegraphCases();
        ProjectileCases();
        ModBehaviour.Instance = null;
        Console.WriteLine("PASS ZombieThreat: " + checks + " production warning/projectile assertions");
    }
}

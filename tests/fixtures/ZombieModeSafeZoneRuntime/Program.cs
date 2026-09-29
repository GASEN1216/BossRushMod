using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static CharacterMainControl Character(string name, Vector3 position)
    {
        var obj = new GameObject(name);
        obj.transform.position = position;
        var character = obj.AddComponent<CharacterMainControl>();
        character.mainDamageReceiver = obj.AddComponent<DamageReceiver>();
        return character;
    }

    private static ZombieModeEnemyRuntimeMarker Enemy(ZombieModeRuntimeModule module, ZombieModeRunState state, string name, Vector3 position, bool boss = false, bool cached = false)
    {
        var character = Character(name, position);
        var marker = character.gameObject.AddComponent<ZombieModeEnemyRuntimeMarker>();
        marker.RunId = state.RunId;
        marker.IsBoss = boss;
        marker.Owner = cached ? character : null;
        character.gameObject.AddComponent<AICharacterController>().forceTracePlayerDistance = 32f;
        module.RegisterZombieModeEnemyInstanceId(character, marker);
        module.RegisterZombieModeRunOnlyObject(state.RunId, boss ? ZombieModeRunOnlyObjectKind.Boss : ZombieModeRunOnlyObjectKind.Enemy,
            character.gameObject, cached ? marker : null, () => Probe.Trace.Add("prune:" + name));
        return marker;
    }

    private static ZombieModeRunState State(int run)
    {
        return new ZombieModeRunState { RunId = run, ActiveSafeZoneActive = true, ActiveSafeZoneRadius = 3f,
            PortableSafeZoneActive = true, PortableSafeZoneCenter = new Vector3(10, 0, 0), PortableSafeZoneRadius = 2f };
    }

    private static void TestBoundaryAndThreatRestoration()
    {
        var state = State(1);
        var module = new ZombieModeRuntimeModule(state);
        CharacterMainControl.Main = Character("player", new Vector3(12, 100, 0));
        Assert(module.IsZombieModePlayerInsideActiveSafeZone(), "Portable slot includes its horizontal radius boundary");
        CharacterMainControl.Main.transform.position = new Vector3(12.001f, 0, 0);
        Assert(!module.IsZombieModePlayerInsideActiveSafeZone(), "Portable slot must exclude positions beyond its radius");
        CharacterMainControl.Main.transform.position = Vector3.zero;
        var enemy = Enemy(module, state, "threat", Vector3.zero);
        var ai = enemy.gameObject.GetComponent<AICharacterController>();
        Time.unscaledTime = 1f;
        module.TickZombieModeSafeZone();
        Assert(enemy.Owner != null && ReferenceEquals(state.RunOnlyObjects[0].Target, enemy), "Hot-path owner and marker fallback must be cached");
        Assert(enemy.transform.position.z > 3f && ai.forceTracePlayerDistance == 0f && state.SafeZoneThreatSuppressed,
            "Inside player protection must eject enemies and suppress threats");
        Assert(enemy.SuppressedForceTraceDistance == 32f, "Threat suppression must snapshot the original force trace distance");
        CharacterMainControl.Main.transform.position = new Vector3(30, 0, 0);
        Time.unscaledTime = 2f;
        module.TickZombieModeSafeZone();
        Assert(ai.forceTracePlayerDistance == 32f && !enemy.HasSuppressedForceTraceDistance && !state.SafeZoneThreatSuppressed,
            "Leaving both slots must restore threat state");
        Assert(ReferenceEquals(ai.searchedEnemy, CharacterMainControl.Main.mainDamageReceiver) && ai.noticed,
            "Restored enemies must target the current player");
        enemy.transform.position = new Vector3(10, 0, 0);
        SpawnPositionHelper.Succeed = true;
        SpawnPositionHelper.Resolved = new Vector3(10, 0, 0);
        module.TryMoveZombieModeEnemyOutsideSafeZone(enemy.gameObject, enemy, false);
        Assert((enemy.transform.position - state.PortableSafeZoneCenter).sqrMagnitude > 4f,
            "A sampled NavMesh point inside the portable slot must not override the valid ejection");
        Assert(ai.forceTracePlayerDistance == 32f, "Ejection outside protection must retain aggression");
        SpawnPositionHelper.Succeed = false;
        enemy.transform.position = new Vector3(10, 0, 0);
        SpawnPositionHelper.RejectCandidates = true;
        SpawnPositionHelper.RejectFallback = true;
        Assert(!module.TryMoveZombieModeEnemyOutsideSafeZone(enemy.gameObject, enemy, false) && enemy.transform.position.x == 10,
            "Failed navigation must leave enemy in place, never eject onto raw terrain outside the map");
        SpawnPositionHelper.RejectCandidates = false;
        SpawnPositionHelper.RejectFallback = false;
    }

    private static void TestRemovalOrderAndRunIsolation()
    {
        var state = State(2);
        state.LivingZombieCount = 2;
        state.LivingNormalZombieCount = 1;
        state.CurrentWaveKillTarget = 25;
        var module = new ZombieModeRuntimeModule(state);
        var normal = Enemy(module, state, "normal", Vector3.zero, cached: true);
        var normalOwner = normal.Owner;
        var boss = Enemy(module, state, "boss", new Vector3(10, 0, 0), true);
        int unregisterCalls = 0;
        module.BindEnemyRecoveryUnregister(character =>
        {
            unregisterCalls++;
            Assert(ReferenceEquals(character, normalOwner), "Recovery removal must receive the same enemy");
            Assert(normal.RemovedFromRuntime && !module.IsZombieModeKnownEnemy(character), "Runtime identity must be removed before recovery state");
            Assert(state.LivingZombieCount == 2 && character.gameObject.activeInHierarchy, "Recovery removal must precede counter decrement and deactivation");
            Probe.Trace.Add("recovery:normal");
        });
        Probe.Trace.Clear();
        module.ClearZombieModeEnemiesInsideActiveSafeZone(state.RunId + 1, "stale");
        Assert(unregisterCalls == 0, "A stale run must not modify this owner");
        module.ClearZombieModeEnemiesInsideActiveSafeZone(state.RunId, "deployment");
        Assert(normal == null && boss != null && state.LivingZombieCount == 1 && state.LivingNormalZombieCount == 0,
            "Deployment must remove ordinary enemies while preserving bosses");
        Assert(state.CurrentWaveKillTarget == 25 && state.RunOnlyObjects.Count == 1 && unregisterCalls == 1,
            "Safety cleanup must retain the kill target and prune only removed enemies");
        Assert(Probe.Trace.IndexOf("recovery:normal") < Probe.Trace.IndexOf("active:normal:False") &&
            Probe.Trace.IndexOf("active:normal:False") < Probe.Trace.IndexOf("destroy:normal") &&
            Probe.Trace.IndexOf("destroy:normal") < Probe.Trace.IndexOf("prune:normal"), "Cleanup stages must retain their original order");

        var second = State(3);
        var other = new ZombieModeRuntimeModule(second);
        var otherEnemy = Enemy(other, second, "other", new Vector3(50, 0, 0));
        var list = new List<ZombieModeEnemyRuntimeMarker> { boss };
        other.CollectZombieModeRuntimeEnemyMarkers(2, list, true);
        Assert(list.Count == 0, "Stale collection must clear the caller buffer");
        other.CollectZombieModeRuntimeEnemyMarkers(3, list, false);
        Assert(list.Count == 1 && ReferenceEquals(list[0], otherEnemy), "Owner registries must stay isolated");
    }

    private static void TestSpatialQueriesAndAiCache()
    {
        var state = State(4);
        var module = new ZombieModeRuntimeModule(state);
        CharacterMainControl.Main = Character("query-player", Vector3.zero);
        var near = Enemy(module, state, "near", new Vector3(2, 0, 0));
        var far = Enemy(module, state, "far", new Vector3(5, 0, 0));
        var boss = Enemy(module, state, "query-boss", new Vector3(1, 0, 0), true);
        Assert(ReferenceEquals(module.TryFindZombieModeNearestEnemyTarget(4, null, 10f), near.Owner), "Nearest ordinary target must exclude bosses");
        module.RefreshZombieModeGravityWellTargets(4, Vector3.zero, 6f, 1f);
        Assert(near.transform.position.x == 1f && far.transform.position.x == 4f && boss.transform.position.x == 1f,
            "Gravity must retain its one-step pull and boss exclusion");
        var realAi = near.gameObject.GetComponent<AICharacterController>();
        near.CachedAI = far.gameObject.GetComponent<AICharacterController>();
        Assert(ReferenceEquals(ZombieModeRuntimeModule.GetZombieModeEnemyAI(near.gameObject, near), realAi),
            "Cached AI from another enemy must be replaced");
        UnityEngine.Object.Destroy(realAi);
        var replacement = near.gameObject.AddComponent<AICharacterController>();
        Assert(ReferenceEquals(ZombieModeRuntimeModule.GetZombieModeEnemyAI(near.gameObject, near), replacement),
            "Destroyed cached AI must be rediscovered");
    }

    private static void TestEventOwnership()
    {
        Assert(ItemAgent_Gun.SubscriberCount == 0, "Event test must start with no subscribers");
        var first = State(5);
        var second = State(6);
        var a = new ZombieModeRuntimeModule(first);
        var b = new ZombieModeRuntimeModule(second);
        a.TryRegisterZombieModeShootStealthBreaker(5);
        a.TryRegisterZombieModeShootStealthBreaker(5);
        b.TryRegisterZombieModeShootStealthBreaker(6);
        Assert(ItemAgent_Gun.SubscriberCount == 2 && first.RunOnlyObjects.Count == 1, "Each owner must register once");
        CharacterMainControl.Main = Character("shoot-player", Vector3.zero);
        ItemAgent_Gun.Fire();
        Assert(first.PlayerInsideSafeZone && second.PlayerInsideSafeZone, "Both live owners must receive the event");
        first.RunOnlyObjects[0].Cleanup(false);
        first.RunOnlyObjects.Clear();
        Assert(ItemAgent_Gun.SubscriberCount == 1, "Cleaning one run must retain the other subscription");
        a.TryRegisterZombieModeShootStealthBreaker(5);
        Assert(ItemAgent_Gun.SubscriberCount == 2, "Cleanup must release the idempotence flag");
        first.RunOnlyObjects[0].Cleanup(false);
        second.RunOnlyObjects[0].Cleanup(false);
        Assert(ItemAgent_Gun.SubscriberCount == 0, "All run cleanup paths must unsubscribe");
    }

    public static int Main()
    {
        TestBoundaryAndThreatRestoration();
        TestRemovalOrderAndRunIsolation();
        TestSpatialQueriesAndAiCache();
        TestEventOwnership();
        Console.WriteLine("ZombieModeSafeZoneRuntime PASS: boundaries, threat, removal order, ownership, queries, cache, subscriptions");
        return 0;
    }
}

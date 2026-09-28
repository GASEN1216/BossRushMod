using System;
using System.Collections.Generic;
using HarmonyLib;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;
using NodeCanvas.Framework;
using NodeCanvas.Tasks.Actions;

internal static class Program
{
    internal static readonly List<string> Warnings = new List<string>();
    private static int checks;
    internal static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("FAIL " + message); }
    private static DamageReceiver Add(Teams team, Vector3 point, bool character = true, Health share = null)
    {
        var go = new GameObject("receiver"); go.transform.position = point;
        var receiver = go.AddComponent<DamageReceiver>(); receiver.Team = team;
        if (character)
        {
            var actor = go.AddComponent<CharacterMainControl>(); actor.Team = team; actor.mainDamageReceiver = receiver;
            receiver.health = share ?? new Health { Character = actor };
        }
        Physics.Contacts.Add(go.AddComponent<Collider>());
        return receiver;
    }
    private static void Reset()
    {
        Physics.Contacts.Clear(); Physics.QueryCapacities.Clear(); Physics.Blocked = null; Physics.ThrowQuery = false;
        Warnings.Clear(); CameraShaker.OnShake = null; LevelManager.FxCount = 0;
        SceneManager.Active = new Scene { handle = 18 }; SkyIslandExplosionObstaclePatch.Disarm();
        var player = Add(Teams.player, new Vector3(20, 0, 0));
        CharacterMainControl.Main = player.health.Character;
        LevelManager.Instance = new LevelManager { MainCharacter = CharacterMainControl.Main };
        Physics.Contacts.Clear();
    }
    private static ExplosionManager Manager() { return new GameObject("explosions").AddComponent<ExplosionManager>(); }
    private static DamageInfo Hit(CharacterMainControl source = null) { return new DamageInfo(source) { damageValue = 10f }; }
    private static void Explode(ExplosionManager manager, CharacterMainControl source = null, bool self = false)
    { manager.CreateExplosion(Vector3.zero, 5f, Hit(source), ExplosionFxTypes.custom, 0f, self); }
    private static void BaselineTruncation()
    {
        Reset(); var manager = Manager(); var targets = new List<DamageReceiver>();
        for (int i = 0; i < 9; i++) targets.Add(Add(Teams.wolf, new Vector3(i * 0.2f, 0, 0), i < 3));
        Explode(manager);
        int hit = 0; foreach (var target in targets) if (target.Hits > 0) hit++;
        Check(hit == 8 && targets[8].Hits == 0, "unpatched official loop misses ninth overlapping receiver");
        Check(manager.Buffer.Length == 8, "installed official capacity baseline is eight");
        Console.WriteLine("REPRO L2: official CreateExplosion reaches 8 of 9 valid overlapping receivers");
    }
    private static AICharacterController Brain(DamageReceiver body, SkyIslandEnemyTier tier, bool manual)
    {
        var ai = body.gameObject.AddComponent<AICharacterController>(); ai.Bind(body.health.Character);
        ai.forceTracePlayerDistance = 321f; SkyIslandEnemyTiers.ApplyAi(ai, tier, manual); return ai;
    }
    private static void Targeting()
    {
        Reset(); var rival = Add(Teams.bear, Vector3.zero); var scav = Add(Teams.wolf, new Vector3(2, 0, 0));
        var ai = Brain(rival, SkyIslandEnemyTier.Chief, false);
        var search = new SearchEnemyAround { agent = ai, alwaysSuccess = true,
            result = new BBParameter<DamageReceiver> { Get = () => ai.searchedEnemy, Set = value => ai.searchedEnemy = value } };
        var sceneBrain = new AIMainBrain();
        sceneBrain.Search(new AIMainBrain.SearchTaskContext { searchCenter = new Vector3(0, 1.5f, 0),
            searchDirection = new Vector3(1, 0, 0), searchDistance = 30f, searchAngle = 180f, selfTeam = Teams.bear,
            checkObsticle = true, onSearchFinishedCallback = search.Deliver });
        Check(ai.searchedEnemy == scav, "official search and bound result choose nearby hostile NPC");
        ai.Tick();
        Check(ai.searchedEnemy == scav && ai.weaponOut, "automatic group keeps NPC target through official Update and draws weapon");
        var aim = new SetAim { agent = ai, aimTarget = new BBParameter<Transform> { Get = () => ai.searchedEnemy.transform } };
        aim.Execute(); Check(ai.aimTarget == scav.transform, "official attack entry aims at selected rival");
        // 两派都使用生产 ApplyAi；普通狼阵营能反击，不只把断风一侧解锁。
        var wolfAi = Brain(scav, SkyIslandEnemyTier.Scav, false); wolfAi.searchedEnemy = rival; wolfAi.Tick();
        Check(wolfAi.searchedEnemy == rival, "ordinary automatic group also retains hostile faction target");
        Physics.Contacts.Clear(); Physics.Contacts.Add(CharacterMainControl.Main.gameObject.GetComponent<Collider>());
        sceneBrain.Search(new AIMainBrain.SearchTaskContext { searchCenter = new Vector3(0, 1.5f, 0),
            searchDirection = new Vector3(1, 0, 0), searchDistance = 30f, searchAngle = 180f, selfTeam = Teams.bear,
            checkObsticle = true, onSearchFinishedCallback = search.Deliver });
        Check(ai.searchedEnemy == CharacterMainControl.Main.mainDamageReceiver, "natural enemy search still acquires hostile player");
        ai.Tick(); aim.Execute(); Check(ai.aimTarget == CharacterMainControl.Main.transform, "natural player target reaches official attack entry");
        var challenge = Add(Teams.wolf, Vector3.zero); var manual = Brain(challenge, SkyIslandEnemyTier.Storm, true);
        manual.searchedEnemy = rival; manual.Tick();
        Check(manual.searchedEnemy == CharacterMainControl.Main.mainDamageReceiver && manual.forceTracePlayerDistance == 100f,
            "manual story challenge retains forced player tracking");
        float applied = ai.forceTracePlayerDistance; SkyIslandEnemyTiers.ApplyAi(ai, SkyIslandEnemyTier.Storm, true);
        Check(ai.forceTracePlayerDistance == applied, "AI setup remains idempotent");
    }
    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
                Console.WriteLine("FAIL " + current.GetType().FullName + ": " + current.Message);
            return 1;
        }
    }
    private static void Run()
    {
        OfficialContract.Verify();
        BaselineTruncation();
        Targeting();
        BufferContract();
        Console.WriteLine("PASS SkyIslandCombatRuntime: " + checks + " assertions (production methods + real Harmony; controlled physics boundary)");
    }
    private static void BufferContract()
    {
        var harmony = new Harmony("BossRush.Tests.SkyIslandCombatRuntime");
        harmony.PatchAll(typeof(SkyIslandExplosionBufferPatch).Assembly);
        try
        {
            // 真实 Harmony 负责私有字段注入与 Finalizer；不在夹具中手动调用 Prefix。
            foreach (int count in new[] { 9, 16, 17, 64, 129, 257 })
            {
                Reset(); var manager = Manager(); var targets = new List<DamageReceiver>();
                for (int i = 0; i < count; i++) targets.Add(Add(Teams.wolf, new Vector3((i % 8) * 0.2f, 0, 0), i % 3 == 0));
                SkyIslandExplosionObstaclePatch.Arm(SceneManager.Active);
                Explode(manager);
                Check(targets.TrueForAll(target => target.Hits == 1), "saturated buffer retains every target: " + count);
                Check(manager.Buffer == null && manager.HealthBuffer == null && manager.ReceiverLayers.value == 0,
                    "first-use null fields and mask restored: " + count);
                Check(Physics.QueryCapacities[Physics.QueryCapacities.Count - 1] > count,
                    "official damage query receives unsaturated capacity: " + count);
                Collider[] first = null; List<Health> firstHealth = null; bool same = true;
                targets[0].OnHit = () =>
                {
                    if (first == null) { first = manager.Buffer; firstHealth = manager.HealthBuffer; }
                    else same &= ReferenceEquals(first, manager.Buffer) && ReferenceEquals(firstHealth, manager.HealthBuffer);
                };
                for (int i = 0; i < 6; i++) Explode(manager);
                Check(same, "stable density reuses collider array and deduplication list: " + count);
                Check(Array.TrueForAll(first, item => item == null) && firstHealth.Count == 0,
                    "returned frame does not retain actor references: " + count);
            }
            OriginalFieldRestoration();
            FilterParity();
            NestedAndFailedCalls();
        }
        finally { harmony.UnpatchAll(harmony.Id); SkyIslandExplosionObstaclePatch.Disarm(); }
    }

    private static void OriginalFieldRestoration()
    {
        Reset(); var manager = Manager(); Add(Teams.wolf, Vector3.zero);
        Explode(manager); var original = manager.Buffer; var originalHealth = manager.HealthBuffer;
        var originalMask = manager.ReceiverLayers;
        Check(manager.GetComponent<SkyIslandExplosionBuffers>() == null, "unarmed scene does not create buffer owner");
        for (int i = 0; i < 20; i++) Add(Teams.wolf, Vector3.zero);
        SkyIslandExplosionObstaclePatch.Arm(SceneManager.Active); Explode(manager);
        Check(ReferenceEquals(manager.Buffer, original) && ReferenceEquals(manager.HealthBuffer, originalHealth) &&
            manager.ReceiverLayers.value == originalMask.value && originalHealth.Count == 1,
            "pre-existing official buffers and their contents restored by reference");
        SceneManager.Active = new Scene { handle = 19 };
        foreach (var contact in Physics.Contacts) contact.GetComponent<DamageReceiver>().Hits = 0;
        Explode(manager);
        int hits = 0; foreach (var contact in Physics.Contacts) hits += contact.GetComponent<DamageReceiver>().Hits;
        Check(hits == 8 && ReferenceEquals(manager.Buffer, original), "same manager in another scene keeps vanilla capacity and behavior");
        var other = Manager(); Explode(other);
        Check(other.GetComponent<SkyIslandExplosionBuffers>() == null, "mismatched scene handle never acquires owner");
    }

    private static void FilterParity()
    {
        Reset(); var manager = Manager(); var source = Add(Teams.wolf, new Vector3(10, 0, 0));
        var friendly = new List<DamageReceiver>();
        for (int i = 0; i < 12; i++) friendly.Add(Add(Teams.wolf, Vector3.zero, false));
        var target = Add(Teams.player, new Vector3(1, 0, 0));
        var duplicate = Add(Teams.player, new Vector3(1.1f, 0, 0), true, target.health);
        var dodging = Add(Teams.bear, new Vector3(2, 0, 0)); dodging.health.Character.Dashing = true;
        var blocked = Add(Teams.player, new Vector3(3, 0, 0));
        var simple = Add(Teams.bear, new Vector3(3, 0, 0), false);
        var neutral = Add(Teams.middle, new Vector3(1, 0, 0), false);
        Physics.Blocked = (ray, distance) => Math.Abs((ray.origin + ray.direction * distance).x - 3f) < 0.01f;
        SkyIslandExplosionObstaclePatch.Arm(SceneManager.Active);
        manager.CreateExplosion(Vector3.zero, 5f, Hit(source.health.Character), ExplosionFxTypes.normal, 0f, false);
        Check(friendly.TrueForAll(value => value.Hits == 0), "friendly receivers do not consume hostile damage slots");
        Check(target.Hits + duplicate.Hits == 1, "official Health deduplication remains intact");
        Check(dodging.Hits == 0 && blocked.Hits == 0 && neutral.Hits == 0, "official dash, obstacle and neutral filters remain intact");
        Check(simple.Hits == 1, "official simple receiver obstacle semantics unchanged");
        Check(target.LastHit.damageValue == 10f && target.LastHit.fromCharacter == source.health.Character && target.LastHit.isExplosion,
            "damage value, source and explosion attribution remain official");
        Check(LevelManager.FxCount == 1, "official visual effect occurs once");
    }

    private static void NestedAndFailedCalls()
    {
        Reset(); var manager = Manager(); var targets = new List<DamageReceiver>();
        for (int i = 0; i < 20; i++) targets.Add(Add(Teams.wolf, new Vector3(0, 0, i * 0.05f)));
        bool nested = false; Collider[] outer = null, inner = null;
        targets[0].OnHit = () =>
        {
            if (nested) { inner = manager.Buffer; return; }
            outer = manager.Buffer; nested = true; Explode(manager);
            Check(ReferenceEquals(manager.Buffer, outer), "nested finalizer restores live outer collider buffer");
        };
        SkyIslandExplosionObstaclePatch.Arm(SceneManager.Active); Explode(manager);
        Check(targets.TrueForAll(value => value.Hits == 2) && !ReferenceEquals(outer, inner),
            "nested official calls each process every target using separate frames");
        Check(manager.Buffer == null && manager.HealthBuffer == null, "nested finalizers restore initial state");
        targets[0].OnHit = () => { throw new InvalidOperationException("damage listener failed"); };
        bool failed = false;
        try { Explode(manager); } catch (InvalidOperationException) { failed = true; }
        Check(failed && manager.Buffer == null && manager.HealthBuffer == null, "original exception propagates after buffers restore");
        targets[0].OnHit = null;
        Physics.ThrowQuery = true;
        failed = false;
        try { Explode(manager); } catch (InvalidOperationException) { failed = true; }
        Check(failed && manager.Buffer == null && manager.HealthBuffer == null && Warnings.Count > 0,
            "prepare failure leaves complete original state and original exception");
        Physics.ThrowQuery = false;
        foreach (var target in targets) target.Hits = 0;
        targets[0].OnHit = () => Check(ReferenceEquals(manager.Buffer, outer), "failed calls return frame depth for reuse");
        Explode(manager); Check(targets.TrueForAll(value => value.Hits == 1), "subsequent successful call still covers all targets");
        CameraShaker.OnShake = () => { throw new InvalidOperationException("shake failed before damage loop"); };
        failed = false;
        try { Explode(manager); } catch (InvalidOperationException) { failed = true; }
        Check(failed && manager.Buffer == null && manager.HealthBuffer == null, "early official exception also restores buffers");
    }
}

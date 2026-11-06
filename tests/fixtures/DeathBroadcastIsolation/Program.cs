using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

// 官方 Health.Hurt / CharacterMainControl.OnDead 方法体逐字提取（见 run.py），用真实 Harmony 安装生产补丁类。
internal static class Program
{
    private static int failures;

    private static void Check(bool condition, string name)
    {
        if (condition) { Console.WriteLine("  ok   " + name); return; }
        failures++;
        Console.WriteLine("  FAIL " + name);
    }

    private static DamageInfo Lethal()
    {
        DamageInfo info = new DamageInfo(null);
        info.damageValue = 5000f;
        info.damageType = DamageTypes.realDamage;
        return info;
    }

    private static Health MakeHealth(float hp)
    {
        Health health = new Health();
        health.CurrentHealth = hp;
        health.Owner = new CharacterMainControl();
        return health;
    }

    private sealed class Probe
    {
        public int DeadEventBefore, DeadEventAfter, StaticDeadAfter, StaticHurtAfter;
    }

    // 场景：每个广播的第一个订阅者模拟「击杀提示 Mod 不判空攻击者」抛 NRE，后面是依赖死亡推进的订阅者。
    private static Probe ArmThrowingSubscribers(Health health, List<Action> cleanup)
    {
        Probe probe = new Probe();
        UnityAction<DamageInfo> before = info => probe.DeadEventBefore++;
        UnityAction<DamageInfo> thrower = info => { throw new NullReferenceException("killnotice reads fromCharacter"); };
        UnityAction<DamageInfo> after = info => probe.DeadEventAfter++;
        health.OnDeadEvent.AddListener(before);
        health.OnDeadEvent.AddListener(thrower);
        health.OnDeadEvent.AddListener(after);
        Action<Health, DamageInfo> staticThrower = (h, i) => { throw new NullReferenceException("killnotice"); };
        Action<Health, DamageInfo> staticAfter = (h, i) => { if (h == health) probe.StaticDeadAfter++; };
        Action<Health, DamageInfo> hurtThrower = (h, i) => { throw new InvalidOperationException("hurt subscriber"); };
        Action<Health, DamageInfo> hurtAfter = (h, i) => { if (h == health) probe.StaticHurtAfter++; };
        Health.OnDead += staticThrower;
        Health.OnDead += staticAfter;
        Health.OnHurt += hurtThrower;
        Health.OnHurt += hurtAfter;
        cleanup.Add(() =>
        {
            Health.OnDead -= staticThrower; Health.OnDead -= staticAfter;
            Health.OnHurt -= hurtThrower; Health.OnHurt -= hurtAfter;
        });
        return probe;
    }

    private static void BaselineReproducesBug()
    {
        Console.WriteLine("baseline: unpatched official Hurt (reproduces the reported stuck wave)");
        var cleanup = new List<Action>();
        Health health = MakeHealth(100f);
        Probe probe = ArmThrowingSubscribers(health, cleanup);
        bool threw = false;
        try { health.Hurt(Lethal()); } catch (NullReferenceException) { threw = true; }
        Check(threw, "baseline: subscriber exception escapes Hurt");
        Check(health.IsDead && health.DestroyScheduled, "baseline: health dead and delayed destroy already scheduled");
        Check(probe.DeadEventAfter == 0, "baseline: later OnDeadEvent listener starved");
        Check(probe.StaticDeadAfter == 0, "baseline: later Health.OnDead subscriber starved (wave counter never decremented)");
        Check(health.gameObject.activeSelf, "baseline: SetActive(false) skipped");
        foreach (Action a in cleanup) a();
    }

    private static void PatchedIsolatesEverySubscriber()
    {
        Console.WriteLine("patched: lethal hit with throwing subscribers on every broadcast");
        var cleanup = new List<Action>();
        Health health = MakeHealth(100f);
        Probe probe = ArmThrowingSubscribers(health, cleanup);
        int faultsBefore = DeathBroadcastIsolation.SubscriberFaultCount;
        int warningsBefore = UnityEngine.Debug.Warnings.Count;
        bool result = false, threw = false;
        try { result = health.Hurt(Lethal()); } catch (Exception) { threw = true; }
        Check(!threw && result, "Hurt completes and returns true");
        Check(health.IsDead, "health dead");
        Check(probe.DeadEventBefore == 1 && probe.DeadEventAfter == 1, "every OnDeadEvent listener notified once");
        Check(probe.StaticDeadAfter == 1, "Health.OnDead subscriber after the thrower notified once");
        Check(probe.StaticHurtAfter == 1, "Health.OnHurt subscriber after the thrower notified once");
        Check(!health.gameObject.activeSelf, "official SetActive(false) still executed");
        Check(DeathBroadcastIsolation.SubscriberFaultCount - faultsBefore == 3, "three subscriber faults isolated and counted");
        Check(UnityEngine.Debug.Warnings.Count - warningsBefore == 1, "fault log throttled to one warning");
        foreach (Action a in cleanup) a();
    }

    private static void NonLethalAndNested()
    {
        Console.WriteLine("patched: non-lethal hit, nested death inside a subscriber, unsubscribe during dispatch");
        int deaths = 0, hurts = 0;
        Health victim = MakeHealth(1000f);
        Action<Health, DamageInfo> countDeath = (h, i) => deaths++;
        Action<Health, DamageInfo> countHurt = (h, i) => hurts++;
        Health.OnDead += countDeath;
        Health.OnHurt += countHurt;
        DamageInfo small = new DamageInfo(null) { damageValue = 10f, damageType = DamageTypes.realDamage };
        victim.Hurt(small);
        Check(deaths == 0 && hurts == 1 && !victim.IsDead, "non-lethal hit: OnHurt once, no death");

        // 连锁：第一个死亡回调里打死第二个目标（爆炸连锁），两次死亡都要完整送达。
        Health first = MakeHealth(10f), second = MakeHealth(10f);
        var seen = new List<Health>();
        Action<Health, DamageInfo> chain = (h, i) => { seen.Add(h); if (h == first) second.Hurt(Lethal()); };
        Health.OnDead += chain;
        first.Hurt(Lethal());
        Check(seen.Count == 2 && seen[0] == first && seen[1] == second, "nested death delivered to every subscriber in order");
        Check(!first.gameObject.activeSelf && !second.gameObject.activeSelf, "both nested deaths deactivate");
        Health.OnDead -= chain;

        // 订阅者在回调里退订另一个：与多播一致，本轮快照里的仍会收到。
        int late = 0;
        Action<Health, DamageInfo> lateSubscriber = (h, i) => late++;
        Action<Health, DamageInfo> remover = (h, i) => { Health.OnDead -= lateSubscriber; };
        Health.OnDead += remover;
        Health.OnDead += lateSubscriber;
        MakeHealth(1f).Hurt(Lethal());
        Check(late == 1, "unsubscribe during dispatch keeps current snapshot (multicast semantics)");
        MakeHealth(1f).Hurt(Lethal());
        Check(late == 1, "unsubscribed handler no longer notified on next death");
        Health.OnDead -= remover;
        Health.OnDead -= countDeath;
        Health.OnHurt -= countHurt;
    }

    private static void CachedInvocationList()
    {
        Console.WriteLine("patched: static broadcast invocation list cached per delegate");
        Health.ClearStaticSubscribers();
        Action<Health, DamageInfo> a = (h, i) => { };
        Health.OnHurt += a;
        DamageInfo small = new DamageInfo(null) { damageValue = 1f, damageType = DamageTypes.realDamage };
        Health target = MakeHealth(1000f);
        target.Hurt(small);
        FieldInfo listField = typeof(DeathBroadcastIsolation).GetField("cachedHurtList", BindingFlags.Static | BindingFlags.NonPublic);
        object first = listField.GetValue(null);
        target.Hurt(small);
        Check(first != null && ReferenceEquals(first, listField.GetValue(null)), "unchanged subscribers reuse the cached list (no allocation)");
        int count = 0;
        Action<Health, DamageInfo> b = (h, i) => count++;
        Health.OnHurt += b;
        target.Hurt(small);
        Check(!ReferenceEquals(first, listField.GetValue(null)) && count == 1, "new subscriber rebuilds the cache and is notified");
        Health.OnHurt -= a;
        Health.OnHurt -= b;
        DeathBroadcastIsolation.ResetStaticCaches();
        Check(listField.GetValue(null) == null, "ResetStaticCaches releases cached delegates");
    }

    private static void LootBroadcast()
    {
        Console.WriteLine("patched: official CharacterMainControl.OnDead loot broadcast");
        CharacterMainControl character = new CharacterMainControl();
        int after = 0;
        character.BeforeCharacterSpawnLootOnDead += info => { throw new NullReferenceException("loot mod"); };
        character.BeforeCharacterSpawnLootOnDead += info => after++;
        int boxes = InteractableLootbox.Created, unregistered = SetActiveByPlayerDistance.Unregistered;
        bool threw = false;
        try { character.Die(new DamageInfo(null)); } catch (Exception) { threw = true; }
        Check(!threw, "loot subscriber exception no longer escapes OnDead");
        Check(after == 1, "loot subscriber after the thrower notified");
        Check(InteractableLootbox.Created == boxes + 1, "official dead loot box still created");
        Check(SetActiveByPlayerDistance.Unregistered == unregistered + 1, "official distance-sleep unregister still executed");
    }

    private static void IlMismatchLeavesOriginal()
    {
        Console.WriteLine("transpiler: unknown IL is returned unchanged");
        var codes = new List<CodeInstruction>
        {
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ret),
        };
        int deathEvent, death, hurt;
        List<CodeInstruction> result = DeathBroadcastIsolation.IsolateHurtBroadcasts(codes, out deathEvent, out death, out hurt);
        Check(deathEvent == 0 && death == 0 && hurt == 0 && result.Count == 2
              && result[0].opcode == System.Reflection.Emit.OpCodes.Ldarg_0, "no match, no rewrite");
    }

    private static int Main()
    {
        Console.WriteLine("DeathBroadcastIsolation fixture (official method bodies + real Harmony)");
        BaselineReproducesBug();
        var harmony = new Harmony("bossrush.death-broadcast-isolation-fixture");
        harmony.CreateClassProcessor(typeof(HealthDeathBroadcastIsolationPatch)).Patch();
        harmony.CreateClassProcessor(typeof(CharacterLootBroadcastIsolationPatch)).Patch();
        Check(UnityEngine.Debug.Warnings.Count == 0, "both transpilers matched the official bodies (no mismatch warning)");
        PatchedIsolatesEverySubscriber();
        NonLethalAndNested();
        CachedInvocationList();
        LootBroadcast();
        IlMismatchLeavesOriginal();
        harmony.UnpatchAll(harmony.Id);
        if (failures > 0)
        {
            Console.WriteLine("FAIL: " + failures + " checks");
            return 1;
        }
        Console.WriteLine("PASS: official Hurt/OnDead bodies under real Harmony isolate every death/hurt/loot subscriber");
        return 0;
    }
}

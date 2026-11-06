using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BossRush;
using HarmonyLib;
using UnityEngine.Events;

// 只读本机现装游戏程序集：核对真实 IL 命中与真实 UnityEvent 实现。不建游戏对象、不调原生方法、不碰存档。
internal static class OfficialCheck
{
    private static int failures;

    private static void Check(bool condition, string name)
    {
        if (condition) { Console.WriteLine("  ok   " + name); return; }
        failures++;
        Console.WriteLine("  FAIL " + name);
    }

    private static int Main(string[] args)
    {
        string managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { return Run(); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        Console.WriteLine("installed game assemblies: " + typeof(Health).Assembly.Location);
        // 预先占住日志节流窗口：隔离路径不会去调 Unity 原生日志（本进程没有 Unity 运行时）。
        typeof(DeathBroadcastIsolation).GetField("hasLoggedFault", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        typeof(DeathBroadcastIsolation).GetField("lastFaultLogTick", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Environment.TickCount);

        MethodInfo hurt = typeof(Health).GetMethod("Hurt", new[] { typeof(DamageInfo) });
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(hurt, (ILGenerator)null);
        int deathEvent, death, hurtCount;
        List<CodeInstruction> rewritten = DeathBroadcastIsolation.IsolateHurtBroadcasts(original, out deathEvent, out death, out hurtCount);
        Check(deathEvent == 1 && death == 1 && hurtCount == 1, "installed Health.Hurt: OnDeadEvent / OnDead / OnHurt each rewritten exactly once ("
              + deathEvent + "/" + death + "/" + hurtCount + ")");
        Check(rewritten.Count == original.Count, "installed Health.Hurt: instruction count unchanged (in-place rewrite)");
        var targets = rewritten.Where(c => c.operand is MethodInfo && ((MethodInfo)c.operand).DeclaringType == typeof(DeathBroadcastIsolation))
            .Select(c => ((MethodInfo)c.operand).Name).ToList();
        Check(targets.SequenceEqual(new[] { "InvokeDeathUnityEvent", "InvokeDeathBroadcast", "InvokeHurtBroadcast" }),
              "installed Health.Hurt: broadcasts rewritten in official order " + string.Join(",", targets));
        Check(!rewritten.Any(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == "Invoke"
                  && ((MethodInfo)c.operand).DeclaringType == typeof(Action<Health, DamageInfo>)),
              "installed Health.Hurt: no unisolated static broadcast left");
        int unityInvokesLeft = rewritten.Count(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == "Invoke"
                  && ((MethodInfo)c.operand).DeclaringType == typeof(UnityEvent<DamageInfo>));
        Check(unityInvokesLeft == 1, "installed Health.Hurt: only per-hit OnHurtEvent stays on official UnityEvent.Invoke");
        File.WriteAllLines("installed-hurt-isolated-il.txt", rewritten.Select((c, i) => i + ": " + c));

        MethodInfo onDead = typeof(CharacterMainControl).GetMethod("OnDead", BindingFlags.Instance | BindingFlags.NonPublic);
        List<CodeInstruction> lootOriginal = PatchProcessor.GetOriginalInstructions(onDead, (ILGenerator)null);
        int lootRewritten;
        List<CodeInstruction> lootIl = DeathBroadcastIsolation.IsolateBeforeLootBroadcast(lootOriginal, out lootRewritten);
        Check(lootRewritten == 1 && lootIl.Count == lootOriginal.Count, "installed CharacterMainControl.OnDead: loot broadcast rewritten exactly once, in place");
        File.WriteAllLines("installed-ondead-isolated-il.txt", lootIl.Select((c, i) => i + ": " + c));

        Check(DeathBroadcastIsolation.EnsureUnityEventInternals(), "installed UnityEngine.CoreModule: PrepareInvoke / BaseInvokableCall.Invoke(object[]) resolved");

        // 真实 UnityEvent 实现：官方 Invoke 会被抛异常的监听器截断，隔离调用不会。
        var probe = new int[2];
        var deathEventObject = new UnityEvent<DamageInfo>();
        deathEventObject.AddListener(info => probe[0]++);
        deathEventObject.AddListener(info => { throw new NullReferenceException("listener"); });
        deathEventObject.AddListener(info => probe[1]++);
        bool officialThrew = false;
        try { deathEventObject.Invoke(default(DamageInfo)); } catch (NullReferenceException) { officialThrew = true; }
        Check(officialThrew && probe[0] == 1 && probe[1] == 0, "installed UnityEvent.Invoke truncates at a throwing listener (bug baseline)");
        int faults = DeathBroadcastIsolation.SubscriberFaultCount;
        DeathBroadcastIsolation.InvokeDeathUnityEvent(deathEventObject, default(DamageInfo));
        Check(probe[0] == 2 && probe[1] == 1, "installed UnityEvent: isolated invoke reaches every listener");
        Check(DeathBroadcastIsolation.SubscriberFaultCount == faults + 1, "installed UnityEvent: one listener fault isolated");

        int staticAfter = 0, lootAfter = 0;
        Action<Health, DamageInfo> broadcast = (h, i) => { throw new NullReferenceException("killnotice"); };
        broadcast += (h, i) => staticAfter++;
        DeathBroadcastIsolation.InvokeDeathBroadcast(broadcast, null, default(DamageInfo));
        Check(staticAfter == 1, "real Action<Health, DamageInfo>: subscriber after the thrower notified");
        Action<DamageInfo> loot = i => { throw new InvalidOperationException("loot"); };
        loot += i => lootAfter++;
        DeathBroadcastIsolation.InvokeBeforeLootBroadcast(loot, default(DamageInfo));
        Check(lootAfter == 1, "real Action<DamageInfo>: loot subscriber after the thrower notified");

        if (failures > 0)
        {
            Console.WriteLine("FAIL: " + failures + " checks");
            return 1;
        }
        Console.WriteLine("PASS: installed " + typeof(Health).Assembly.GetName().Name
            + " IL matched; installed UnityEngine UnityEvent isolated per listener; metadata/managed code only, no game objects");
        return 0;
    }
}

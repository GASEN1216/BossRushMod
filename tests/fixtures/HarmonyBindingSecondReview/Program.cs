using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BossRush
{
    public class ModBehaviour
    {
        public static readonly List<string> Logs = new List<string>();
        public static void CriticalLog(string key, string message) { Logs.Add(message); }
        public static void DevLog(string message) { Logs.Add(message); }
    }
    public static class HostTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static int Attack() { return 1; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static int Mixed() { return 2; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static int AllKinds() { return 3; }
    }
    [HarmonyPatch(typeof(HostTarget), "Attack")]
    public static class FirstPatch { public static void Postfix(ref int __result) { __result++; } }
    [HarmonyPatch(typeof(HostTarget), "Attack")]
    public static class SecondPatch { public static void Postfix(ref int __result) { __result += 10; } }
    [HarmonyPatch(typeof(HostTarget), "Mixed")]
    public static class MixedPatch
    {
        public static void Prefix() { }
        [HarmonyPostfix] public static void Adjust(ref int __result) { __result++; }
    }
    [HarmonyPatch(typeof(HostTarget), "AllKinds")]
    public static class FourKindsPatch
    {
        [HarmonyPrefix] public static void Before() { }
        [HarmonyPostfix] public static void After() { }
        [HarmonyTranspiler] public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions) { return instructions; }
        [HarmonyFinalizer] public static Exception Recover(Exception __exception) { return __exception; }
    }
    [HarmonyPatch]
    public static class DynamicPatch
    {
        public static MethodBase TargetMethod() { return AccessTools.Method(typeof(HostTarget), "Attack"); }
        public static void Postfix() { }
    }
    public static class NoPatchMethods { public static void Helper() { } }

    public static class Program
    {
        private static int checks;
        private static readonly MethodInfo Validate = typeof(HarmonyBindingSelfCheck).GetMethod(
            "IsPatchClassApplied", BindingFlags.Static | BindingFlags.NonPublic);
        private static bool Applied(Type patch, MethodBase target, string owner)
        { return (bool)Validate.Invoke(null, new object[] { patch, target, owner }); }
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        private static void RunCheck(Harmony h)
        {
            ModBehaviour.Logs.Clear();
            HarmonyBindingSelfCheck.ResetStaticCaches();
            HarmonyBindingSelfCheck.RunStartupSelfCheck(h);
        }
        public static void Main()
        {
            var h = new Harmony("com.bossrush.mod");
            var foreign = new Harmony("test.foreign");
            var attack = AccessTools.Method(typeof(HostTarget), "Attack");
            var mixed = AccessTools.Method(typeof(HostTarget), "Mixed");
            var allKinds = AccessTools.Method(typeof(HostTarget), "AllKinds");
            Check(!Applied(typeof(FirstPatch), attack, h.Id), "unpatched target fails verification");
            h.CreateClassProcessor(typeof(FirstPatch)).Patch();
            Check(HostTarget.Attack() == 2 && Harmony.GetPatchInfo(attack).Postfixes.Count == 1, "one real postfix is installed and executes");
            Check(Applied(typeof(FirstPatch), attack, h.Id), "installed class passes");
            Check(!Applied(typeof(SecondPatch), attack, h.Id), "same owner on shared target cannot cover a missing class");
            Check(!Applied(typeof(NoPatchMethods), attack, h.Id), "no declared patch cannot pass vacuously");
            RunCheck(h);
            Check(ModBehaviour.Logs.Exists(s => s.Contains("[ERROR]") && s.Contains("SecondPatch")), "startup reports the missing class");
            int logCount = ModBehaviour.Logs.Count;
            HarmonyBindingSelfCheck.RunStartupSelfCheck(h);
            Check(ModBehaviour.Logs.Count == logCount, "startup self-check remains once per lifecycle");
            foreign.CreateClassProcessor(typeof(SecondPatch)).Patch();
            Check(!Applied(typeof(SecondPatch), attack, h.Id), "same method installed by another owner is insufficient");
            foreign.UnpatchAll(foreign.Id);
            h.CreateClassProcessor(typeof(SecondPatch)).Patch();
            Check(Applied(typeof(SecondPatch), attack, h.Id) && HostTarget.Attack() == 12, "both shared-target classes independently pass");
            h.Patch(mixed, prefix: new HarmonyMethod(AccessTools.Method(typeof(MixedPatch), "Prefix")));
            Check(!Applied(typeof(MixedPatch), mixed, h.Id), "prefix does not cover a missing attributed postfix");
            h.Patch(mixed, postfix: new HarmonyMethod(AccessTools.Method(typeof(MixedPatch), "Adjust")));
            Check(Applied(typeof(MixedPatch), mixed, h.Id) && HostTarget.Mixed() == 3, "attributed nonconventional name passes when actually installed");
            h.CreateClassProcessor(typeof(FourKindsPatch)).Patch();
            Check(Applied(typeof(FourKindsPatch), allKinds, h.Id), "prefix postfix transpiler and finalizer all verify");
            h.Unpatch(allKinds, AccessTools.Method(typeof(FourKindsPatch), "Recover"));
            Check(!Applied(typeof(FourKindsPatch), allKinds, h.Id), "missing finalizer fails even with three other categories installed");
            h.Patch(allKinds, finalizer: new HarmonyMethod(AccessTools.Method(typeof(FourKindsPatch), "Recover")));
            h.Unpatch(allKinds, AccessTools.Method(typeof(FourKindsPatch), "Rewrite"));
            Check(!Applied(typeof(FourKindsPatch), allKinds, h.Id), "missing transpiler fails independently");
            h.Patch(allKinds, transpiler: new HarmonyMethod(AccessTools.Method(typeof(FourKindsPatch), "Rewrite")));
            RunCheck(h);
            Check(ModBehaviour.Logs.Exists(s => s.Contains("4/4")) && !ModBehaviour.Logs.Exists(s => s.Contains("[ERROR]")), "startup passes only after every static class is installed");
            Check(ModBehaviour.Logs.Exists(s => s.Contains("1")), "dynamic selector retains explicit skip");
            h.UnpatchAll(h.Id);
            Check(HostTarget.Attack() == 1 && !Applied(typeof(FirstPatch), attack, h.Id), "fixture unpatches only its own process and verification reflects removal");
            Console.WriteLine(checks + " assertions; full production self-check and real installed Harmony, isolated .NET Framework process.");
        }
    }
}

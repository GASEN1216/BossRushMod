using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BossRush;
using HarmonyLib;

namespace BossRush
{
    // No gameplay objects are created: this process only validates the installed method's IL.
    public sealed class ModBehaviour
    {
        public static ModBehaviour Instance { get { return null; } }
        public bool HasSetBonusElementHealing { get { return false; } }
        public static void CriticalLog(string key, string message) { Console.WriteLine(key + ": " + message); }
    }
}
internal static class OfficialIlCheck
{
    private static int Main(string[] args)
    {
        string managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { return Check(); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Check()
    {
        MethodInfo hurt = typeof(Health).GetMethod("Hurt");
        var instructions = PatchProcessor.GetOriginalInstructions(hurt, (ILGenerator)null);
        File.WriteAllLines("installed-game-hurt-il.txt", instructions.Select((c, i) => i + ": " + c));
        int factor, sum; string failure;
        if (!SetBonusDamageObservation.TryFindObservationPoint(instructions, out factor, out sum, out failure))
            throw new Exception("Installed official Health.Hurt did not match: " + failure);
        MethodInfo transpiler = typeof(SetBonusDamageObservation).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        var transformed = ((System.Collections.Generic.IEnumerable<CodeInstruction>)transpiler.Invoke(null,
            new object[] { instructions })).ToList();
        if (!SetBonusDamageObservation.IsSupported || transformed.Count != instructions.Count + 5)
            throw new Exception("Official IL was not transformed exactly once");
        File.WriteAllLines("installed-game-hurt-observed-il.txt", transformed.Select((c, i) => i + ": " + c));
        Console.WriteLine("PASS: installed " + typeof(Health).Assembly.GetName().Name
            + " Health.Hurt original IL matched (factor=" + factor + ", sum=" + sum
            + ") and production transpiler inserted exactly one observer; metadata/IL only, no game objects instantiated.");
        return 0;
    }
}

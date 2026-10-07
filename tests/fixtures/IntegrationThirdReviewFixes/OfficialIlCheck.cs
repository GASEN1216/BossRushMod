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
    // IL 接线探针不创建宠物，身份查询保持未上膛；防御行为由 ZombieBossCases 实跑。
    internal static class PetNestCompanionAgent
    {
        internal static bool IsCompanionHealth(Health health) { return false; }
    }
    // No gameplay objects are created: this process only validates the installed method's IL.
    public sealed class ModBehaviour
    {
        public static ModBehaviour Instance { get { return null; } }
        public void ApplyZombieModeEnemyDefense(Health health, ref DamageInfo info, ZombieModeEnemyRuntimeMarker marker) { }
        public bool IsZombieModeActive { get { return false; } }
        public int ZombieModeCurrentRunId { get { return 0; } }
        public bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl target, out ZombieModeEnemyRuntimeMarker marker) { marker = null; return false; }
        public float AbsorbZombieModeBossFinalDamage(CharacterMainControl target, ZombieModeEnemyRuntimeMarker marker, float damage) { return 0; }
        public float ApplyZombieModeShielderAuraFinalDamageReduction(CharacterMainControl target, float damage) { return 0; }
        public bool HasSetBonusElementHealing { get { return false; } }
        public static void CriticalLog(string key, string message) { Console.WriteLine(key + ": " + message); }
    }
}
namespace BossRush
{
    public sealed class ZombieModeEnemyRuntimeMarker
    {
        public int RunId;
        public bool IsBoss, DeathSettled, RemovedFromRuntime;
        public ZombieModeBossShieldRuntime AllyShield;
    }
    public sealed class ZombieModeBossShieldRuntime
    {
        public bool IsShieldActive() { return false; }
        public float AbsorbDamage(float damage) { return 0; }
    }
    public static class ZombieModeBossVisuals
    {
        public static void RestoreOfficialPreset(ZombieModeEnemyRuntimeMarker marker) { }
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
        var defended = ZombieModeDamageRuntime.InjectBeforeHealthLoss(transformed).ToList();
        if (defended.Count != transformed.Count + 3)
            throw new Exception("Zombie defense must inject exactly once into installed official IL");
        var defenseFirst = ZombieModeDamageRuntime.InjectBeforeHealthLoss(instructions).ToList();
        int otherFactor, otherSum; string otherFailure;
        if (!SetBonusDamageObservation.TryFindObservationPoint(defenseFirst, out otherFactor, out otherSum, out otherFailure))
            throw new Exception("Zombie defense must coexist in either transpiler order: " + otherFailure);
        Console.WriteLine("PASS: installed official Health.Hurt zombie defense before health loss, both transpiler orders");
        File.WriteAllLines("installed-game-hurt-observed-il.txt", transformed.Select((c, i) => i + ": " + c));
        Console.WriteLine("PASS: installed " + typeof(Health).Assembly.GetName().Name
            + " Health.Hurt original IL matched (factor=" + factor + ", sum=" + sum
            + ") and production transpiler inserted exactly one observer; metadata/IL only, no game objects instantiated.");
        return 0;
    }
}

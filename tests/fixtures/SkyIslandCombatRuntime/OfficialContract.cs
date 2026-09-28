using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

internal static class OfficialContract
{
    private static string managed;
    private static readonly Dictionary<short, OpCode> codes = new Dictionary<short, OpCode>();
    internal static void Verify()
    {
        managed = Environment.GetEnvironmentVariable("BOSSRUSH_GAME_MANAGED");
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += Resolve;
        try
        {
            string path = Path.Combine(managed, "TeamSoda.Duckov.Core.dll");
            Assembly game = Assembly.ReflectionOnlyLoadFrom(path);
            Type explosion = game.GetType("ExplosionManager", true), ai = game.GetType("AICharacterController", true);
            Program.Check(explosion.GetField("colliders", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.FullName == "UnityEngine.Collider[]", "actual official collider field binding");
            Program.Check(explosion.GetField("damagedHealth", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.GetGenericArguments()[0].FullName == "Health", "actual official deduplication field binding");
            Program.Check(explosion.GetField("damageReceiverLayers", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.FullName == "UnityEngine.LayerMask", "actual official receiver mask binding");
            MethodInfo create = explosion.GetMethod("CreateExplosion", BindingFlags.Public | BindingFlags.Instance);
            Program.Check(create != null && create.GetParameters().Length == 6 && create.ReturnType == typeof(void), "actual official CreateExplosion target signature");
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.FieldType == typeof(OpCode)) { var op = (OpCode)field.GetValue(null); codes[op.Value] = op; }
            var instructions = Read(create);
            bool eight = false;
            for (int i = 1; i < instructions.Count; i++)
                if (instructions[i] == "newarr:Collider" && instructions[i - 1] == "ldc.i4.8") eight = true;
            Program.Check(eight && instructions.Contains("stfld:colliders") && instructions.Contains("call:OverlapSphereNonAlloc"), "actual official loop still uses eight-entry nonalloc buffer");
            var update = Read(ai.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance));
            Program.Check(update.Contains("ldfld:forceTracePlayerDistance") && update.Contains("stfld:searchedEnemy"), "actual official AI Update force-target binding");
            using (var hash = SHA256.Create())
                Console.WriteLine("Official DLL SHA-256: " + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant());
        }
        finally { AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= Resolve; }
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string candidate = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(candidate) ? Assembly.ReflectionOnlyLoadFrom(candidate) : Assembly.ReflectionOnlyLoad(args.Name);
    }
    private static List<string> Read(MethodInfo method)
    {
        byte[] bytes = method.GetMethodBody().GetILAsByteArray(); var result = new List<string>();
        for (int i = 0; i < bytes.Length;)
        {
            short key = bytes[i++]; if (key == 0xfe) key = unchecked((short)(0xfe00 | bytes[i++]));
            OpCode op = codes[key]; string text = op.Name; int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + BitConverter.ToInt32(bytes, i) * 4; break;
                default: size = 4; break;
            }
            if (op.OperandType == OperandType.InlineField || op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineType)
                text += ":" + method.Module.ResolveMember(BitConverter.ToInt32(bytes, i)).Name;
            result.Add(text); i += size;
        }
        return result;
    }
}

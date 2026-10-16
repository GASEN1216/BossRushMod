using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BossRush;
using EscapeFromDuckovCoopMod;
using HarmonyLib;

internal static class SpectatorRegistrationProgram
{
    private static int checks, mappedInstructions;
    private static MethodInfo installedHandler;
    private static string[] roots;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static Exception Root(Exception error) { while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException; return error; }
    private static Exception Failure(Action action) { try { action(); return null; } catch (Exception e) { return Root(e); } }
    private static void Repair(Assembly assembly)
    {
        typeof(BossRushCoopTransport).GetMethod("EnsureSpectatorForceEndRegistration", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { assembly });
    }
    private static void FakeCases()
    {
        Assembly assembly = typeof(RpcRegistry).Assembly;
        RpcRegistry.Reset();
        Check(Failure(() => RpcRegistry.GetDescriptor<SpectatorForceEndRpc>()) is InvalidOperationException, "missing descriptor really fails before repair");
        Repair(assembly);
        Check(RpcRegistry.InitializeCalls == 1 && RpcRegistry.RegisterCalls == 1, "production method initializes and adds exactly one missing registration");
        var first = RpcRegistry.GetDescriptor<SpectatorForceEndRpc>();
        Check(first.Handler.Method == typeof(RPCScene).GetMethod("HandleSpectatorForceEnd"), "production method retains the native handler identity");
        Repair(assembly);
        Check(RpcRegistry.RegisterCalls == 1 && ReferenceEquals(first, RpcRegistry.GetDescriptor<SpectatorForceEndRpc>()), "second bind does not replace or duplicate native descriptor");
        Spectator.Instance = new Spectator();
        ((Action<RpcContext, SpectatorForceEndRpc>)first.Handler)(new RpcContext { Server = false }, new SpectatorForceEndRpc());
        Check(Spectator.Instance.EndCalls == 1 && Spectator.Instance.LastForce, "registered callback invokes complete installed client handler IL with force=true");
        ((Action<RpcContext, SpectatorForceEndRpc>)first.Handler)(new RpcContext { Server = true }, new SpectatorForceEndRpc());
        Check(Spectator.Instance.EndCalls == 1, "installed server-context branch preserves its early return");
        Spectator.Instance = null;
        Check(Failure(() => ((Action<RpcContext, SpectatorForceEndRpc>)first.Handler)(new RpcContext(), new SpectatorForceEndRpc())) == null, "installed null spectator branch remains a no-op");
        RpcRegistry.Reset(); int custom = 0;
        RpcRegistry.Register<SpectatorForceEndRpc>((context, message) => custom++);
        var existing = RpcRegistry.GetDescriptor<SpectatorForceEndRpc>(); Repair(assembly);
        ((Action<RpcContext, SpectatorForceEndRpc>)existing.Handler)(new RpcContext(), new SpectatorForceEndRpc());
        Check(custom == 1 && RpcRegistry.RegisterCalls == 1 && ReferenceEquals(existing, RpcRegistry.GetDescriptor<SpectatorForceEndRpc>()), "future native pre-registration keeps its exact original callback");
        RpcRegistry.Reset(); RpcRegistry.Conflict = true;
        Check(Failure(() => Repair(assembly)) is InvalidOperationException && RpcRegistry.RegisterCalls == 1 && RpcRegistry.Entries.Count == 0, "opcode conflict propagates and cannot create a partial typed registration");
        RpcRegistry.Reset(); RpcRegistry.GetFault = new InvalidOperationException("native initialization broke");
        Check(Failure(() => Repair(assembly)).Message == "native initialization broke" && RpcRegistry.RegisterCalls == 0, "unrelated native InvalidOperationException is not misread as a missing descriptor");
        RpcRegistry.Reset(); RpcRegistry.GetFault = new FormatException("bad native registry");
        Check(Failure(() => Repair(assembly)) is FormatException && RpcRegistry.RegisterCalls == 0, "different native error propagates without register attempt");
        RpcRegistry.Reset(); RpcRegistry.InitFault = new InvalidOperationException("init failed");
        Check(Failure(() => Repair(assembly)).Message == "init failed" && RpcRegistry.RegisterCalls == 0, "failed Initialize cannot be bypassed by a repair");
        RpcRegistry.Reset(); RpcRegistry.ReturnNull = true;
        Check(Failure(() => Repair(assembly)) is InvalidOperationException && RpcRegistry.RegisterCalls == 0, "null existing descriptor is a failed binding, never silently accepted or overwritten");
        Check(Failure(() => Repair(typeof(string).Assembly)) == null, "older assembly with no such message remains unchanged");
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        foreach (string root in roots) { string path = Path.Combine(root, name + ".dll"); if (File.Exists(path)) return Assembly.LoadFrom(path); }
        return null;
    }
    private static void NativeCases(Assembly native)
    {
        Type registry = native.GetType("EscapeFromDuckovCoopMod.RpcRegistry", true);
        Type message = native.GetType("EscapeFromDuckovCoopMod.SpectatorForceEndRpc", true);
        Type context = native.GetType("EscapeFromDuckovCoopMod.RpcContext", true);
        MethodInfo handler = native.GetType("EscapeFromDuckovCoopMod.RPCScene", true).GetMethod("HandleSpectatorForceEnd");
        Check(message.IsValueType && context.IsValueType && handler != null && handler.IsStatic && handler.IsPublic, "installed public handler uses the actual two native value types");
        MethodInfo get = registry.GetMethod("GetDescriptor").MakeGenericMethod(message);
        Exception missing = Failure(() => get.Invoke(null, null));
        Check(missing is InvalidOperationException && missing.Message == "Rpc message " + message.FullName + " not registered", "installed initialized registry actually lacks the descriptor before production repair");
        Repair(native);
        object descriptor = get.Invoke(null, null);
        Type descriptorType = descriptor.GetType();
        Check((Type)descriptorType.GetProperty("MessageType").GetValue(descriptor, null) == message, "production repair creates a real installed native typed descriptor");
        Check(Convert.ToString(descriptorType.GetProperty("Delivery").GetValue(descriptor, null)) == "ReliableOrdered"
            && Convert.ToString(descriptorType.GetProperty("Direction").GetValue(descriptor, null)) == "ServerToClient", "installed native RpcAttribute keeps its original reliability and direction");
        Delegate invoker = (Delegate)descriptorType.GetProperty("Invoker").GetValue(descriptor, null);
        Delegate capturedHandler = null;
        foreach (FieldInfo field in invoker.Target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (typeof(Delegate).IsAssignableFrom(field.FieldType)) capturedHandler = field.GetValue(invoker.Target) as Delegate;
        Check(capturedHandler != null && capturedHandler.Method == handler, "actual installed Register captures exactly its original RPCScene handler");
        Repair(native); Check(ReferenceEquals(descriptor, get.Invoke(null, null)), "production repeat against actual native registry is idempotent");
        MethodInfo add = registry.GetMethod("Register").MakeGenericMethod(message);
        Exception duplicate = Failure(() => add.Invoke(null, new object[] { Delegate.CreateDelegate(typeof(Action<,>).MakeGenericType(context, message), handler) }));
        Check(duplicate is InvalidOperationException && duplicate.Message.Contains("already registered")
            && ReferenceEquals(descriptor, get.Invoke(null, null)), "actual native duplicate opcode rejects replacement and retains descriptor");
        // Isolated non-game registry fault injection: preserve the original opcode and remove only this typed index.
        var byType = (IDictionary)registry.GetField("_byType", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var byOp = (IDictionary)registry.GetField("_byOp", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object op = descriptorType.GetProperty("Op").GetValue(descriptor, null); int before = byOp.Count;
        byType.Remove(message);
        try
        {
            Exception conflict = Failure(() => Repair(native));
            Check(conflict is InvalidOperationException && conflict.Message.Contains("already registered"), "production propagates the actual installed opcode conflict");
            Check(!byType.Contains(message) && byOp.Count == before && ReferenceEquals(byOp[op], descriptor), "actual native conflict is atomic and never changes the original opcode owner");
        }
        finally { byType[message] = descriptor; }
    }
    private static IEnumerable<CodeInstruction> CompleteNativeHandler(IEnumerable<CodeInstruction> unused, ILGenerator generator)
    {
        foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(installedHandler, generator))
        {
            var method = instruction.operand as MethodInfo; var field = instruction.operand as FieldInfo;
            string name = method != null ? method.DeclaringType.FullName : field != null ? field.DeclaringType.FullName : null;
            Type mapped = name == "EscapeFromDuckovCoopMod.RpcContext" ? typeof(RpcContext)
                : name == "EscapeFromDuckovCoopMod.Spectator" ? typeof(Spectator) : null;
            if (mapped != null)
            {
                instruction.operand = method != null ? (object)mapped.GetMethod(method.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    : mapped.GetField(field.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (instruction.operand == null) throw new Exception("unmapped native handler boundary:" + name);
            }
            mappedInstructions++; yield return instruction; // Preserve every original opcode, branch, constant and return.
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            roots = args; AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Assembly native = Assembly.LoadFrom(Path.Combine(args[0], "EscapeFromDuckovCoopMod.dll"));
            installedHandler = native.GetType("EscapeFromDuckovCoopMod.RPCScene", true).GetMethod("HandleSpectatorForceEnd");
            var harmony = new Harmony("BossRush.Fixture.SpectatorRegistration");
            try
            {
                harmony.Patch(typeof(RPCScene).GetMethod("HandleSpectatorForceEnd"), transpiler: new HarmonyMethod(typeof(SpectatorRegistrationProgram).GetMethod("CompleteNativeHandler", BindingFlags.Static | BindingFlags.NonPublic)));
                FakeCases(); NativeCases(native);
                Check(mappedInstructions > 8, "complete installed native handler IL was actually executed through mapped world boundary");
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Console.WriteLine("SpectatorRegistration PASS assertions=" + checks + " complete_native_handler_instructions=" + mappedInstructions + " no_game_process=True"); return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}

namespace EscapeFromDuckovCoopMod
{
    public struct SpectatorForceEndRpc { }
    public struct RpcContext { public bool Server; public bool IsServer { get { return Server; } } }
    public sealed class RpcDescriptor { public Delegate Handler; }
    public static class RpcRegistry
    {
        public static readonly Dictionary<Type, RpcDescriptor> Entries = new Dictionary<Type, RpcDescriptor>();
        public static int InitializeCalls, RegisterCalls; public static bool Conflict, ReturnNull;
        public static Exception GetFault, InitFault;
        public static void Reset() { Entries.Clear(); InitializeCalls = RegisterCalls = 0; Conflict = ReturnNull = false; GetFault = InitFault = null; }
        public static void Initialize() { InitializeCalls++; if (InitFault != null) throw InitFault; }
        public static RpcDescriptor GetDescriptor<T>() where T : struct
        {
            if (GetFault != null) throw GetFault;
            if (ReturnNull) return null;
            RpcDescriptor descriptor; if (Entries.TryGetValue(typeof(T), out descriptor)) return descriptor;
            throw new InvalidOperationException("Rpc message " + typeof(T).FullName + " not registered");
        }
        public static void Register<T>(Action<RpcContext, T> handler) where T : struct
        {
            RegisterCalls++; if (Conflict || Entries.ContainsKey(typeof(T))) throw new InvalidOperationException("Rpc opcode already registered");
            Entries[typeof(T)] = new RpcDescriptor { Handler = handler };
        }
    }
    public static class RPCScene
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void HandleSpectatorForceEnd(RpcContext context, SpectatorForceEndRpc message) { throw new Exception("installed handler IL not transplanted"); }
    }
    public sealed class Spectator
    {
        public static Spectator Instance; public int EndCalls; public bool LastForce;
        public void EndSpectatorAndShowClosure(bool force) { EndCalls++; LastForce = force; }
    }
}

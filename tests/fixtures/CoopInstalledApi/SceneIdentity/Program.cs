using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BossRush;
using Duckov.Scenes;
using EscapeFromDuckovCoopMod;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

internal static class SceneIdentityProgram
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static string[] roots;
    private static Assembly native;
    private static int checks;
    private static readonly Dictionary<MethodBase, MethodInfo> installed = new Dictionary<MethodBase, MethodInfo>();
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    { foreach (string root in roots) { string file = Path.Combine(root, new AssemblyName(args.Name).Name + ".dll"); if (File.Exists(file)) return Assembly.LoadFrom(file); } return null; }
    private static Type Map(Type type)
    {
        if (type.IsByRef) return Map(type.GetElementType()).MakeByRefType();
        if (type.IsGenericType) return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(Map).ToArray());
        return typeof(SceneIdentityProgram).Assembly.GetType(type.FullName, false) ?? type;
    }
    private static object Member(object operand)
    {
        Type type = operand as Type; if (type != null) return Map(type);
        FieldInfo field = operand as FieldInfo;
        if (field != null) { Type mapped = Map(field.DeclaringType); return mapped == field.DeclaringType ? field : mapped.GetField(field.Name, All) ?? throw new Exception("Missing field seam " + field); }
        MethodInfo method = operand as MethodInfo;
        if (method == null) return operand;
        Type owner = Map(method.DeclaringType);
        if (owner == method.DeclaringType) return method;
        MethodInfo result = owner.GetMethods(All).Single(value => value.Name == method.Name && value.GetParameters().Length == method.GetParameters().Length);
        return method.IsGenericMethod ? result.MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray()) : result;
    }
    private static IEnumerable<CodeInstruction> NativeIL(IEnumerable<CodeInstruction> ignored, MethodBase __originalMethod, ILGenerator generator)
    {
        MethodInfo source = installed[__originalMethod];
        int nativeLocalBase = generator.DeclareLocal(typeof(int)).LocalIndex + 1;
        var original = PatchProcessor.GetOriginalInstructions(source, generator);
        var instructions = original.ToList();
        LocalBuilder[] locals = source.GetMethodBody().LocalVariables.Select(value => generator.DeclareLocal(Map(value.LocalType))).ToArray();
        foreach (CodeInstruction code in instructions)
        {
            int index = -1; bool store = false, address = false;
            if (code.opcode == OpCodes.Ldloc_0 || code.opcode == OpCodes.Stloc_0) index = 0;
            if (code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Stloc_1) index = 1;
            if (code.opcode == OpCodes.Ldloc_2 || code.opcode == OpCodes.Stloc_2) index = 2;
            if (code.opcode == OpCodes.Ldloc_3 || code.opcode == OpCodes.Stloc_3) index = 3;
            if (code.opcode == OpCodes.Ldloc || code.opcode == OpCodes.Ldloc_S || code.opcode == OpCodes.Stloc || code.opcode == OpCodes.Stloc_S || code.opcode == OpCodes.Ldloca || code.opcode == OpCodes.Ldloca_S)
            {
                var local = code.operand as LocalBuilder;
                if (local != null)
                {
                    index = local.LocalIndex - nativeLocalBase;
                }
                else index = Convert.ToInt32(code.operand);
            }
            if (index >= 0)
            {
                store = code.opcode.Name.StartsWith("stloc", StringComparison.Ordinal); address = code.opcode.Name.StartsWith("ldloca", StringComparison.Ordinal);
                code.opcode = store ? OpCodes.Stloc : address ? OpCodes.Ldloca : OpCodes.Ldloc;
                code.operand = locals[index];
            }
            else code.operand = Member(code.operand);
            yield return code;
        }
    }
    private static void Replay(Type target, string name)
    {
        MethodInfo destination = target.GetMethod(name, All);
        MethodInfo source = native.GetType(target.FullName, true).GetMethod(name, All);
        installed.Add(destination, source);
        new Harmony("Test.FullNativeSceneIdentity").Patch(destination, transpiler: new HarmonyMethod(typeof(SceneIdentityProgram), "NativeIL") { priority = Priority.First });
        Console.WriteLine("FULL NATIVE IL " + source.DeclaringType.Name + "." + name + " bytes=" + source.GetMethodBody().GetILAsByteArray().Length);
    }
    private sealed class Scope : IDisposable
    {
        internal string Room = Guid.NewGuid().ToString("N");
        internal readonly object HostPeer = new object();
        internal BossRushCoopSceneIdentity Identity;
        internal BossRushCoopSceneIdentityBridge Bridge;
        internal Scope(bool host = false, bool core = true)
        {
            SceneLoader.IsSceneLoading = false; CoopTool.Wire.Clear(); CoopTool.FailSend = false; ModNetworkApi.Queued = 0;
            SceneNet.Instance = new SceneNet(); Spectator.Instance = new Spectator();
            NetService.Instance = new NetService { NetworkStarted = true, IsServer = host, connectedPeer = HostPeer };
            CharacterMainControl.Main = new CharacterMainControl();
            MultiSceneCore.Instance = null;
            if (core) Core("Base", 10);
            Identity = new BossRushCoopSceneIdentity(() => NetService.Instance.NetworkStarted, () => NetService.Instance.IsServer,
                () => NetService.Instance, () => NetService.Instance.connectedPeer, () => Room);
            Bridge = new BossRushCoopSceneIdentityBridge(typeof(SceneNet), Identity, () => NetService.Instance.NetworkStarted,
                () => NetService.Instance.IsServer, () => NetService.Instance, () => NetService.Instance.connectedPeer, () => Room,
                bytes => CoopTool.Wire.Add(new ModApiMessageRpc { Channel = BossRushCoopSceneIdentityBridge.MarkerChannel, Payload = bytes }));
            Bridge.Install(); Identity.ObserveCore(MultiSceneCore.Instance);
        }
        internal BossRushCoopSceneMarker Marker(string id = "Base", long serial = 1, string room = null)
        { return new BossRushCoopSceneMarker { session = room ?? Room, serial = serial, token = Guid.NewGuid().ToString("N"), sceneId = id, saveToFile = true }; }
        internal RpcContext Context(object sender = null, NetService service = null)
        { return new RpcContext { Sender = sender ?? HostPeer, Service = service ?? NetService.Instance }; }
        internal void Ingress(BossRushCoopSceneMarker marker, object sender = null, NetService service = null)
        { RPCModApi.HandleModApiMessage(Context(sender, service), new ModApiMessageRpc { Channel = BossRushCoopSceneIdentityBridge.MarkerChannel, Payload = System.Text.Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(marker)) }); }
        internal void Begin(BossRushCoopSceneMarker marker, object sender = null, NetService service = null)
        { RPCScene.HandleSceneBeginLoad(Context(sender, service), new SceneBeginLoadRpc { SceneId = marker.sceneId, SaveToFile = marker.saveToFile, CurtainGuid = marker.curtainGuid, NotifyEvac = marker.notifyEvac, UseLocation = marker.useLocation, LocationName = marker.locationName }); }
        internal void Load(string id = "Base", int handle = 20)
        {
            var old = MultiSceneCore.Instance;
            SceneLoader.StartActual("Scene_" + id);
            if (old != null) { SceneManager.Unload(old.gameObject.scene); Object.Destroy(old.gameObject); }
            Core(id, handle); SceneLoader.IsSceneLoading = false; Identity.ObserveCore(MultiSceneCore.Instance);
        }
        public void Dispose() { Bridge.Dispose(); Identity.Dispose(); }
    }
    private static void Core(string id, int handle)
    {
        var info = new SceneInfoEntry { ID = id, SceneReference = new SceneReference { Name = "Scene_" + id } };
        SceneInfoCollection.Entries[id] = info;
        MultiSceneCore.Instance = new MultiSceneCore { SceneInfo = info };
        MultiSceneCore.Instance.gameObject.scene = new Scene { handle = handle, isLoaded = true };
    }
    private static void NativeWireCases()
    {
        using (var scope = new Scope(true))
        { scope.Room = ""; scope.Identity.ResetSession(); scope.Identity.ObserveCore(MultiSceneCore.Instance); Check(scope.Identity.LocalToken == null, "empty host room cannot mint bootstrap token"); scope.Room = Guid.NewGuid().ToString("N"); scope.Identity.ObserveCore(MultiSceneCore.Instance); Check(scope.Identity.LocalToken != null, "host room initialized after first Core observation can bootstrap same stable Core"); }
        using (var scope = new Scope(true))
        {
            string initial = scope.Identity.LocalToken;
            Check(!string.IsNullOrEmpty(initial), "host stable initial Core has token");
            SceneNet.Instance.sceneTargetId = "Base"; SceneNet.Instance.sceneSaveToFile = true;
            SceneNet.Instance.BroadcastBegin();
            Check(CoopTool.Wire.Count == 2 && CoopTool.Wire[0] is ModApiMessageRpc && CoopTool.Wire[1] is SceneBeginLoadRpc, "actual native send is preceded synchronously by one marker");
            var marker = BossRushCoopJson.FromJson<BossRushCoopSceneMarker>(System.Text.Encoding.UTF8.GetString(((ModApiMessageRpc)CoopTool.Wire[0]).Payload));
            Check(marker.Valid() && marker.session == scope.Room && marker.sceneId == ((SceneBeginLoadRpc)CoopTool.Wire[1]).SceneId, "actual marker uses room and actual native message destination");
            Check(!scope.Identity.Matches(initial), "approved native travel stops old bound Core immediately");
            scope.Load(); Check(scope.Identity.Matches(marker.token), "host actual same-name reload binds exact sent token");
            SceneNet.Instance.BroadcastBegin();
            var second = BossRushCoopJson.FromJson<BossRushCoopSceneMarker>(System.Text.Encoding.UTF8.GetString(((ModApiMessageRpc)CoopTool.Wire[2]).Payload));
            Check(second.token != marker.token && second.serial > marker.serial, "same-name new native send has a fresh identity");
            scope.Load(handle: 30); Check(scope.Identity.Matches(second.token) && !scope.Identity.Matches(marker.token), "second same-name load cannot use old identity");
        }
        using (var scope = new Scope(true))
        {
            SceneNet.Instance.sceneTargetId = "Base"; SceneNet.Instance.sceneSaveToFile = true; CoopTool.FailSend = true;
            try { SceneNet.Instance.BroadcastBegin(); throw new Exception("expected native send failure"); } catch (InvalidOperationException) { }
            Check(CoopTool.Wire.Count == 1, "marker can exist if actual native send throws");
            scope.Load(); Check(scope.Identity.LocalToken == null, "failed native send never approves host unrelated later load");
        }
        using (var scope = new Scope(true))
        {
            Spectator.Instance._spectatorActive = Spectator.Instance._spectatorEndOnVotePending = true;
            Spectator.Instance.DuringClosure = () => { Check(CoopTool.Wire.Count == 0, "native spectator Closure occurs before any marker"); throw new InvalidOperationException("original closure failed"); };
            try { SceneNet.Instance.BroadcastBegin(); } catch (InvalidOperationException) { }
            Check(CoopTool.Wire.Count == 0, "native pre-send exception emits no marker");
        }
        using (var scope = new Scope())
        {
            var marker = scope.Marker(); scope.Ingress(marker);
            Check(ModNetworkApi.Queued == 1, "original full native ModApi handler still enqueues marker");
            scope.Begin(marker); scope.Load();
            Check(scope.Identity.Matches(marker.token), "marker binds before native ModNetworkPump has drained any queued messages");
        }
    }
    private static void GuestCases()
    {
        using (var scope = new Scope())
        {
            string initial = Guid.NewGuid().ToString("N"); Check(scope.Identity.TryAdoptInitial(initial, "Base"), "only first stable room Core may adopt bootstrap token");
            Check(!scope.Identity.TryAdoptInitial(Guid.NewGuid().ToString("N"), "Base"), "second roster cannot reidentify same initial Core");
            var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker);
            Check(!scope.Identity.Matches(initial), "guest approved travel invalidates old Core while Closure waits");
            for (int i = 0; i < 8; i++) scope.Identity.ObserveCore(MultiSceneCore.Instance);
            Check(!scope.Identity.Matches(marker.token), "host-first advanced roster never binds guest old same-name Core");
            scope.Load(); Check(scope.Identity.Matches(marker.token), "slow guest actual reload binds without needing another load");
        }
        using (var scope = new Scope())
        {
            var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Load();
            Check(scope.Identity.Matches(marker.token), "guest-first actual Core binds before host roster advances");
            scope.Identity.ObserveCore(MultiSceneCore.Instance); Check(scope.Identity.Matches(marker.token), "old then current roster arrival does not mutate service token");
            scope.Begin(marker); scope.Load(handle: 30); Check(scope.Identity.LocalToken == null, "unmarked same-target Begin cannot reuse consumed marker");
        }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Begin(marker); scope.Load(); Check(scope.Identity.LocalToken == null, "duplicate same-target Begin without fresh marker clears pending approval"); }
        using (var scope = new Scope())
        { var first = scope.Marker(serial: 1); var next = scope.Marker(serial: 3); scope.Ingress(first); scope.Ingress(next); scope.Begin(next); scope.Load(); Check(scope.Identity.Matches(next.token) && !scope.Identity.Matches(first.token), "missing intermediate native Begin cannot shift marker pairing"); }
        using (var scope = new Scope(false, false))
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Load(); Check(scope.Identity.Matches(marker.token), "menu/latejoin with no source Core binds only after actual first load"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Load("GroundZero"); Check(scope.Identity.LocalToken == null, "wrong actual target scene cannot bind"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Load(handle: 10); Check(scope.Identity.LocalToken == null, "new Core with source handle reused cannot claim completed travel"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); SceneLoader.StartActual("Wrong_Loader_Scene"); Core("Base", 20); SceneLoader.IsSceneLoading = false; scope.Identity.ObserveCore(MultiSceneCore.Instance); Check(scope.Identity.LocalToken == null, "correct resulting Core cannot hide mismatched actual loader event"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); Core("Base", 20); scope.Identity.ObserveCore(MultiSceneCore.Instance); Check(scope.Identity.LocalToken == null, "new same-name Core without actual load event cannot claim native approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); Spectator.Instance._spectatorActive = Spectator.Instance._spectatorEndOnVotePending = true; scope.Ingress(scope.Marker(serial: 2)); var slow = scope.Marker(serial: 3); scope.Ingress(slow); scope.Begin(slow); Check(Spectator.Instance.ClosureCalls == 1, "complete installed RPC executes spectator early Closure branch"); scope.Load(); Check(scope.Identity.Matches(slow.token), "spectator waits indefinitely without losing exact pending identity"); }
    }
    private static void BoundaryCases()
    {
        foreach (int boundary in Enumerable.Range(0, 3)) using (var scope = new Scope())
        {
            var marker = scope.Marker(); object sender = boundary == 0 ? new object() : scope.HostPeer;
            NetService context = boundary == 1 ? new NetService() : NetService.Instance;
            if (boundary == 2) NetService.Instance.IsServer = true;
            scope.Ingress(marker, sender, context); scope.Begin(marker, sender, context); scope.Load();
            Check(!scope.Identity.Matches(marker.token), "foreign sender/service/server RPC cannot approve identity " + boundary);
        }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); SceneNet.Instance.sceneVoteActive = false; SceneNet.Instance.Host_BeginSceneVote_Simple("Base", null, false, true, false, null); scope.Load(); Check(scope.Identity.LocalToken == null, "real new same-target vote revokes pending approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); SceneNet.Instance.sceneVoteActive = true; SceneNet.Instance.Host_BeginSceneVote_Simple("Base", null, false, true, false, null); scope.Load(); Check(scope.Identity.Matches(marker.token), "ignored already-active duplicate vote does not revoke approval"); }
        using (var scope = new Scope())
        {
            var marker = scope.Marker(); SceneNet.Instance.sceneVoteActive = false;
            SceneNet.Instance.NestedVote = () => { scope.Ingress(marker); scope.Begin(marker); SceneNet.Instance.sceneVoteActive = true; };
            SceneNet.Instance.Host_BeginSceneVote_Simple("Base", null, false, true, false, null);
            scope.Load(); Check(scope.Identity.Matches(marker.token), "outer vote postfix cannot erase an approval observed inside native call");
        }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); SceneNet.Instance.sceneVoteActive = true; SceneNet.Instance.Client_OnVoteCancelled(); scope.Load(); Check(scope.Identity.LocalToken == null, "native cancellation revokes pending approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); NetService.Instance.NetworkStarted = false; scope.Load(); Check(scope.Identity.LocalToken == null, "StopNetwork cannot bind old in-flight travel"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); NetService.Instance.connectedPeer = new object(); scope.Load(); Check(scope.Identity.LocalToken == null, "new host peer cannot inherit approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); Object.Destroy(MultiSceneCore.Instance.gameObject); scope.Load(); Check(scope.Identity.LocalToken == null, "destroyed source before actual start invalidates approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); Core("Base", 10); scope.Load(); Check(scope.Identity.LocalToken == null, "different source Core with reused handle cannot inherit travel approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); MultiSceneCore.Instance.gameObject.scene = new Scene { handle = 11, isLoaded = true }; scope.Load(); Check(scope.Identity.LocalToken == null, "changed source handle before actual start cannot inherit approval"); }
        using (var scope = new Scope())
        {
            string oldRoom = scope.Room; var old = scope.Marker(); scope.Ingress(old);
            string nextRoom = Guid.NewGuid().ToString("N"); var next = scope.Marker(serial: 2, room: nextRoom);
            scope.Ingress(next); scope.Begin(next); SceneLoader.StartActual("Scene_Base");
            var original = MultiSceneCore.Instance; SceneManager.Unload(original.gameObject.scene); Object.Destroy(original.gameObject);
            Core("Base", 20); SceneLoader.IsSceneLoading = false; scope.Identity.ObserveCore(MultiSceneCore.Instance);
            Check(scope.Identity.LocalToken == null, "native new-room load before queued welcome waits for exact accepted session");
            scope.Room = nextRoom; scope.Identity.ResetSession(nextRoom); scope.Identity.ObserveCore(MultiSceneCore.Instance);
            Check(scope.Identity.Matches(next.token), "exact welcome preserves actual in-flight new-room load");
            var delayedOld = scope.Marker(serial: 99, room: oldRoom);
            var current = scope.Marker(serial: 3); scope.Ingress(current); scope.Ingress(delayedOld); scope.Begin(current); scope.Load(handle: 25);
            Check(scope.Identity.Matches(current.token), "retired room marker cannot overwrite a staged current-room marker");
            scope.Ingress(old); scope.Begin(old); scope.Load(handle: 30); Check(scope.Identity.LocalToken == null, "delayed retired-room marker cannot overwrite current identity or approve later load");
        }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Identity.ResetSession(Guid.NewGuid().ToString("N")); scope.Load(); Check(scope.Identity.LocalToken == null, "different welcome cannot preserve unrelated pending approval"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Identity.ResetSession(scope.Room); scope.Ingress(marker); scope.Begin(marker); scope.Load(); Check(scope.Identity.LocalToken == null, "ResetSession preserves marker serial high-water so replay cannot approve"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Load(); scope.Ingress(marker); scope.Begin(marker); scope.Load(handle: 30); Check(scope.Identity.LocalToken == null, "replayed serialized marker cannot identify a second reload"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Begin(marker); Check(!scope.Identity.TryAdoptInitial(marker.token, "Base"), "any unmarked native Begin permanently forbids bootstrap adoption"); }
        using (var scope = new Scope())
        { SceneLoader.StartActual("Scene_Base"); SceneLoader.IsSceneLoading = false; Check(!scope.Identity.TryAdoptInitial(Guid.NewGuid().ToString("N"), "Base"), "any real local load forbids bootstrap adoption"); }
        using (var scope = new Scope())
        { MultiSceneCore.Instance.IsLoading = true; Check(!scope.Identity.TryAdoptInitial(Guid.NewGuid().ToString("N"), "Base"), "initializing Core cannot adopt"); }
        using (var scope = new Scope())
        { var marker = scope.Marker(); scope.Ingress(marker); scope.Begin(marker); scope.Bridge.Dispose(); scope.Identity.Dispose(); scope.Load(); Check(scope.Identity.LocalToken == null && SceneLoader.ListenerCount == 0, "Dispose unsubscribes real event and clears pending identity"); }
    }
    private static int Main(string[] args)
    {
        roots = args; AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            native = Assembly.LoadFrom(Path.Combine(args[0], "EscapeFromDuckovCoopMod.dll"));
            foreach (string name in new[] { "SceneBeginLoadRpc", "ModApiMessageRpc" })
            {
                var attribute = native.GetType("EscapeFromDuckovCoopMod." + name, true).GetCustomAttributesData().Single(value => value.AttributeType.Name == "RpcAttribute");
                var delivery = attribute.ConstructorArguments[1];
                Check(Enum.GetName(delivery.ArgumentType, delivery.Value) == "ReliableOrdered", "actual installed " + name + " attribute requires ReliableOrdered");
            }
            Replay(typeof(SceneNet), "Server_BroadcastBeginSceneLoad"); Replay(typeof(SceneNet), "Client_OnBeginSceneLoad");
            Replay(typeof(RPCScene), "HandleSceneBeginLoad"); Replay(typeof(RPCModApi), "HandleModApiMessage");
            NativeWireCases(); GuestCases(); BoundaryCases();
            Console.WriteLine("PASS scene identity " + checks + " actual production/native IL assertions"); return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}

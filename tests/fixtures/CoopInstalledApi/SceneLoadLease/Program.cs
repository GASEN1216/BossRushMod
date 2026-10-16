using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BossRush;
using HarmonyLib;
using EscapeFromDuckovCoopMod;

internal static class SceneLoadLeaseProgram
{
    private static int checks;
    private static string[] roots;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static BossRushCoopTransport Start()
    {
        TestClock.Now = 1f;
        BossRushCoop.RunId = "room-A"; BossRushCoop.LocalAssetId = "slot-A"; BossRushCoop.SceneGeneration = 3; BossRushCoop.IsConnected = true;
        CharacterMainControl.Main = new CharacterMainControl();
        Duckov.Scenes.MultiSceneCore.Instance = new Duckov.Scenes.MultiSceneCore();
        Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene = new UnityEngine.SceneManagement.Scene { handle = 101, isLoaded = true };
        SceneNet.Instance = new SceneNet();
        Spectator.Instance = new Spectator();
        NetService.Instance = new NetService { NetworkStarted = true, connectedPeer = new object() };
        var transport = new BossRushCoopTransport((peer, data) => { }, peer => { }, (id, body, local) => { }, (id, body) => { });
        Check(transport.TryBind(), "actual production Transport.TryBind installs the compatibility owner");
        return transport;
    }
    private static void Expire() { TestClock.Now += 20f; }
    private static void Approved()
    {
        SceneNet.Instance.Approve("Base"); Expire();
        Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "approved Base is retained beyond the native ten second TTL");
    }
    private static void Revocation(Action mutate, string text)
    {
        using (Start()) { Approved(); mutate(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), text); }
    }
    private static void RuntimeCases()
    {
        using (Start())
        {
            Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "unapproved first request cannot bypass native voting");
            SceneNet.Instance.Host_BeginSceneVote_Simple("Base", null, false, true, false, null);
            Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "vote start and readiness alone do not create authorization");
            Approved();
            Check(!SceneNet.Instance.IsLocalSceneLoadAllowed(null), "null query cannot broaden a retained Base grant");
            Check(!SceneNet.Instance.IsLocalSceneLoadAllowed(""), "empty query cannot broaden a retained Base grant");
            Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("GroundZero"), "other targets remain blocked");
            SceneNet.Instance.Continue(); Check(SceneNet.Instance.Loads == 1 && SceneNet.Instance.Votes == 1, "delayed real continuation loads once without an additional vote");
            Check(SceneNet.Instance.IsLocalSceneLoadAllowed("base"), "nested SceneReference/string checks survive until the source scene changes");
        }
        foreach (bool dead in new[] { false, true })
            using (Start()) { CharacterMainControl.Main.Dead = dead; Approved(); TestClock.Now += 1000f; SceneNet.Instance.Continue(); Check(SceneNet.Instance.Loads == 1, "alive/dead human waits do not expire a still-current approved return: " + dead); }
        Revocation(() => SceneNet.Instance.CancelVote(), "host cancel revokes old approval");
        Revocation(() => SceneNet.Instance.Client_OnVoteCancelled(), "client cancellation revokes old approval");
        Revocation(() => SceneNet.Instance.Host_BeginSceneVote_Simple("Base", null, false, true, false, null), "new same-target vote does not inherit old authorization");
        Revocation(() => SceneNet.Instance.Host_BeginSceneVote_Simple("GroundZero", null, false, true, false, null), "new other-target vote revokes old authorization");
        Revocation(() => SceneNet.Instance.Client_RequestBeginSceneVote("Base", null, false, true, false, null), "client request revokes even before host vote arrives");
        Revocation(() => SceneNet.Instance.Client_OnSceneVoteStart(new object()), "new native vote-start message revokes old authorization");
        Revocation(() => SceneNet.Instance.Clear(), "native ready clear revokes compatibility authorization");
        Revocation(() => { SceneNet.Instance.AuthorizeLocalSceneLoad("GroundZero"); SceneNet.Instance.allowLocalSceneLoad = false; Expire(); }, "new native authorization to another target revokes Base");
        Revocation(() => { SceneNet.Instance.AuthorizeLocalSceneLoad("Base"); SceneNet.Instance.allowLocalSceneLoad = false; Expire(); }, "native Authorize alone never recreates a retained approval");
        Revocation(() => BossRushCoop.ChangeSession(), "room session event revokes even when IDs are reused");
        Revocation(() => BossRushCoop.RunId = "room-B", "different room ID revokes old approval");
        Revocation(() => BossRushCoop.LocalAssetId = "slot-B", "different local asset identity revokes old approval");
        Revocation(() => Saves.SavesSystem.Select(), "switch away and back to a slot still revokes approval");
        Revocation(() => Saves.SavesSystem.Delete(), "slot deletion revokes approval");
        using (Start()) { Approved(); BossRushCoop.SceneGeneration++; Expire(); SceneNet.Instance.Continue(); Check(SceneNet.Instance.Loads == 1 && SceneNet.Instance.Votes == 0, "host already entering Base cannot revoke a slow guest's unchanged source body/core/handle grant"); }
        Revocation(() => Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene = new UnityEngine.SceneManagement.Scene { handle = 102, isLoaded = true }, "same logical map with new scene handle revokes approval");
        Revocation(() => { var old = Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene; UnityEngine.SceneManagement.SceneManager.Unload(old); }, "source unload revokes before the new core is installed");
        Revocation(() => { var scene = Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene; Duckov.Scenes.MultiSceneCore.Instance = new Duckov.Scenes.MultiSceneCore(); Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene = scene; }, "different scene instance with reused handle revokes approval");
        Revocation(() => CharacterMainControl.Main = new CharacterMainControl(), "respawned body cannot inherit an old closure lease");
        Revocation(() => UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject), "mandatory scene teardown destroys the body and revokes approval");
        Revocation(() => UnityEngine.Object.Destroy(Duckov.Scenes.MultiSceneCore.Instance.gameObject), "destroyed source core revokes approval");
        Revocation(() => BossRushCoop.IsConnected = false, "facade disconnection revokes approval");
        Revocation(() => NetService.Instance.NetworkStarted = false, "native disconnection revokes approval even before facade update");
        Revocation(() => { var old = SceneNet.Instance; SceneNet.Instance = new SceneNet(); Check(!old.IsLocalSceneLoadAllowed("Base"), "old native owner cannot retain authorization"); }, "replacement native owner has no inherited approval");
        using (var owner = Start()) { Approved(); owner.Dispose(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "Transport.Dispose unpatches and revokes approval"); }
        using (Start()) { SceneNet.Instance.FailLaunch = true; SceneNet.Instance.Approve("Base"); Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "failed proxy/direct launch cannot create a retained grant"); }
        using (Start()) { Approved(); SceneNet.Instance.Approve("GroundZero"); Check(SceneNet.Instance.IsLocalSceneLoadAllowed("GroundZero"), "unexpired native authorization to another target is untouched"); Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "new approved non-Base travel revokes old grant"); }
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        foreach (string root in roots) { string path = Path.Combine(root, name + ".dll"); if (File.Exists(path)) return Assembly.LoadFrom(path); }
        return null;
    }
    private static Type installedType;
    private static MethodInfo installedAuthorize, installedAllowed, installedBegin, installedClientBegin;
    private static void CountClientBegin(SceneNet __instance) { __instance.BeginCalls++; }
    private static IEnumerable<CodeInstruction> InstalledClientBeginIL(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        LocalBuilder entry = generator.DeclareLocal(typeof(MapSelectionEntry));
        foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(installedClientBegin, generator))
        {
            if (instruction.opcode == OpCodes.Ldloc_0) { instruction.opcode = OpCodes.Ldloc; instruction.operand = entry; }
            else if (instruction.opcode == OpCodes.Stloc_0) { instruction.opcode = OpCodes.Stloc; instruction.operand = entry; }
            var method = instruction.operand as MethodInfo; var field = instruction.operand as FieldInfo;
            string name = method != null ? method.DeclaringType.FullName : field != null ? field.DeclaringType.FullName : null;
            Type mapped = name == "EscapeFromDuckovCoopMod.SceneNet" ? typeof(SceneNet)
                : name == "EscapeFromDuckovCoopMod.SceneBeginLoadRpc" ? typeof(SceneBeginLoadRpc)
                : name == "EscapeFromDuckovCoopMod.CoopTool" ? typeof(CoopTool)
                : name == "UnityEngine.Object" ? typeof(UnityEngine.Object)
                : name != null && name.EndsWith(".MapSelectionEntry", StringComparison.Ordinal) ? typeof(MapSelectionEntry)
                : name == "LevelManager" ? typeof(LevelManager) : null;
            if (mapped != null)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                instruction.operand = method != null ? (object)mapped.GetMethod(method.Name, flags) : mapped.GetField(field.Name, flags);
                if (instruction.operand == null) throw new Exception("Missing Client_OnBegin boundary: " + name);
            }
            yield return instruction;
        }
    }
    private static IEnumerable<CodeInstruction> InstalledBeginIL(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        LocalBuilder scene = generator.DeclareLocal(typeof(SceneNet));
        foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(installedBegin, generator))
        {
            // The installed handler has one SceneNet local; retain every branch/return.
            if (instruction.opcode == OpCodes.Ldloc_0) { instruction.opcode = OpCodes.Ldloc; instruction.operand = scene; }
            else if (instruction.opcode == OpCodes.Stloc_0) { instruction.opcode = OpCodes.Stloc; instruction.operand = scene; }
            var method = instruction.operand as MethodInfo;
            var field = instruction.operand as FieldInfo;
            Type mapped = null;
            string type = method != null ? method.DeclaringType.FullName : field != null ? field.DeclaringType.FullName : null;
            if (type == "EscapeFromDuckovCoopMod.SceneNet") mapped = typeof(SceneNet);
            else if (type == "EscapeFromDuckovCoopMod.Spectator") mapped = typeof(Spectator);
            else if (type == "EscapeFromDuckovCoopMod.RpcContext") mapped = typeof(RpcContext);
            else if (type == "UnityEngine.Object") mapped = typeof(UnityEngine.Object);
            if (mapped != null)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                instruction.operand = method != null ? (object)mapped.GetMethod(method.Name, flags) : mapped.GetField(field.Name, flags);
                if (instruction.operand == null) throw new Exception("Missing native handler boundary: " + type);
            }
            yield return instruction;
        }
    }
    private static void SpectatorBegin(string destination = "Base", object sender = null, NetService service = null)
    {
        SceneNet.Instance.sceneTargetId = "Base";
        SceneNet.Instance.sceneVoteActive = true;
        SceneNet.Instance.localReady = true;
        SceneNet.Instance.sceneReady["guest"] = true;
        Spectator.Instance._spectatorActive = Spectator.Instance._spectatorEndOnVotePending = true;
        CharacterMainControl.Main.Dead = true;
        RPCScene.HandleSceneBeginLoad(new RpcContext { Service = service ?? NetService.Instance,
            Sender = sender ?? NetService.Instance.connectedPeer }, new SceneBeginLoadRpc { SceneId = destination });
    }
    private static void SpectatorCases()
    {
        using (Start())
        {
            // Reproduce the real L3 shape: launched=true is left over from the prior map,
            // while the Base message takes the dead spectator branch before Client_OnBegin.
            SceneNet.Instance.Approve("GroundZero"); SceneNet.Instance.Clear();
            SpectatorBegin();
            Check(Spectator.Instance.ClosureCalls == 1 && SceneNet.Instance.BeginCalls == 0, "installed complete RPC executes dead spectator early-return, without the normal Client_OnBegin path");
            Check(!SceneNet.Instance.sceneVoteActive && !SceneNet.Instance.localReady && SceneNet.Instance.sceneReady.Count == 0,
                "installed dead branch clears the real vote/ready fields");
            FieldInfo nativeGrant = typeof(SceneNet).GetField("_allowedLocalSceneLoadId", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(nativeGrant.GetValue(SceneNet.Instance) == null, "dead-branch fix never fabricates a native Authorize call or grant");
            Expire();
            Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "actual host-approved dead-guest Base RPC survives the absent native authorization");
            Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("GroundZero") && !SceneNet.Instance.IsLocalSceneLoadAllowed(null), "dead-guest grant covers only the actual Base destination");
            BossRushCoop.SceneGeneration++; TestClock.Now += 1000f;
            SceneNet.Instance.Continue();
            Check(SceneNet.Instance.Loads == 1 && SceneNet.Instance.Votes == 0, "actual dead RPC plus delayed Closure continuation reaches Base without another vote after host changes generation");
        }
        using (Start()) { SpectatorBegin(); Expire(); Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "fresh launched=false cannot block a real approved spectator return"); }
        using (Start())
        {
            SpectatorBegin(); Expire();
            Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "dead approval exists before duplicate normal Begin");
            RPCScene.HandleSceneBeginLoad(new RpcContext { Service = NetService.Instance, Sender = NetService.Instance.connectedPeer }, new SceneBeginLoadRpc { SceneId = "Base", SaveToFile = true });
            Check(Spectator.Instance.ClosureCalls == 1 && SceneNet.Instance.BeginCalls == 1, "duplicate approved Begin follows installed ordinary Client_OnBegin IL without a second spectator Closure");
            Check(!SceneNet.Instance.sceneVoteActive && !SceneNet.Instance.localReady && SceneNet.Instance.sceneReady.Count == 0, "installed normal Begin retains native vote and ready cleanup");
            Expire(); Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "duplicate Base Begin replaces spectator lease through original authorization and TryPerform chain");
            SceneNet.Instance.Continue(); Check(SceneNet.Instance.Loads == 1 && SceneNet.Instance.Votes == 0, "ordinary branch after spectator lease still permits delayed actual continuation");
        }
        using (Start())
        {
            RPCScene.HandleSceneBeginLoad(new RpcContext { Service = NetService.Instance, Sender = NetService.Instance.connectedPeer }, new SceneBeginLoadRpc { SceneId = "Base", SaveToFile = true });
            Check(SceneNet.Instance.BeginCalls == 1 && Spectator.Instance.ClosureCalls == 0, "ordinary first Begin executes installed native method without any spectator flags");
            Expire(); Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "ordinary installed Begin keeps its original approved Base continuation lease");
            SceneNet.Instance.Client_OnVoteCancelled(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "ordinary branch created lease still obeys native cancel");
        }
        using (Start()) { SpectatorBegin(sender: new object()); Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "another sender cannot create a host approval"); }
        using (Start()) { SpectatorBegin(service: new NetService()); Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "stale native service cannot create a host approval even with the same sender"); }
        using (Start()) { SpectatorBegin("GroundZero"); Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "payload destination must be Base, regardless of stale native Base target"); }
        using (Start()) { NetService.Instance.IsServer = true; SpectatorBegin(); Expire(); Check(Spectator.Instance.ClosureCalls == 0 && !SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "installed server-context return cannot approve the client spectator branch"); }
        using (Start()) { SceneNet.Instance.sceneTargetId = "Base"; Spectator.Instance._spectatorActive = Spectator.Instance._spectatorEndOnVotePending = true; Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "spectator flags and ordinary vote alone do not authorize Base"); }
        Action[] cancelDuring = { () => SceneNet.Instance.CancelVote(), () => SceneNet.Instance.Client_OnVoteCancelled(),
            () => SceneNet.Instance.Host_BeginSceneVote_Simple("Base", null, false, true, false, null), () => Saves.SavesSystem.Select(),
            () => CharacterMainControl.Main = new CharacterMainControl(), () => BossRushCoop.RunId = "room-B",
            () => NetService.Instance.connectedPeer = new object(), () => Spectator.Instance = new Spectator() };
        foreach (Action cancel in cancelDuring)
            using (Start()) { Spectator.Instance.DuringClosure = cancel; SpectatorBegin(); Expire(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "identity/cancel/new travel during actual spectator Closure cannot be restored by the postfix"); }
        Action[] revoke = { () => SceneNet.Instance.Clear(), () => Saves.SavesSystem.Select(), () => Saves.SavesSystem.Delete(),
            () => BossRushCoop.LocalAssetId = "slot-B", () => BossRushCoop.RunId = "room-B",
            () => CharacterMainControl.Main = new CharacterMainControl(), () => UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject),
            () => UnityEngine.SceneManagement.SceneManager.Unload(Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene),
            () => Duckov.Scenes.MultiSceneCore.Instance.gameObject.scene = new UnityEngine.SceneManagement.Scene { handle = 102, isLoaded = true },
            () => NetService.Instance.connectedPeer = new object(), () => NetService.Instance.NetworkStarted = false,
            () => SceneNet.Instance.Client_RequestBeginSceneVote("Base", null, false, true, false, null) };
        foreach (Action change in revoke)
            using (Start()) { SpectatorBegin(); Expire(); Check(SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "dead branch has a real lease before revocation"); change(); Check(!SceneNet.Instance.IsLocalSceneLoadAllowed("Base"), "dead-branch lease obeys original body/core/slot/room/native cancellation boundaries"); }
    }
    private static IEnumerable<CodeInstruction> InstalledAuthorizationIL(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
    {
        MethodInfo source = __originalMethod.Name == "AuthorizeLocalSceneLoad" ? installedAuthorize : installedAllowed;
        foreach (var instruction in PatchProcessor.GetOriginalInstructions(source, generator))
        {
            var method = instruction.operand as MethodInfo;
            var field = instruction.operand as FieldInfo;
            if (field != null && field.DeclaringType == installedType)
                instruction.operand = typeof(SceneNet).GetField(field.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null && method.DeclaringType.FullName == "UnityEngine.Time" && method.Name == "get_unscaledTime") instruction.operand = typeof(TestClock).GetMethod("Read");
            if (method != null && method.DeclaringType.FullName == "UnityEngine.Mathf" && method.Name == "Max") instruction.operand = typeof(Math).GetMethod("Max", new[] { typeof(float), typeof(float) });
            yield return instruction;
        }
    }
    private static void NativeDllCases(string workshop, string managed, string dependency)
    {
        roots = new[] { workshop, managed, dependency };
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Assembly assembly = Assembly.LoadFrom(Path.Combine(workshop, "EscapeFromDuckovCoopMod.dll"));
        Type type = assembly.GetType("EscapeFromDuckovCoopMod.SceneNet", true);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        MethodInfo authorize = type.GetMethod("AuthorizeLocalSceneLoad", flags, null, new[] { typeof(string), typeof(float) }, null);
        MethodInfo allowed = type.GetMethod("IsLocalSceneLoadAllowed", flags, null, new[] { typeof(string) }, null);
        MethodInfo perform = type.GetMethod("TryPerformSceneLoad_Local", flags, null, new[] { typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string) }, null);
        Console.WriteLine("Native authorization methods=" + (authorize != null) + "/" + (allowed != null) + "/" + (perform != null));
        Check(authorize != null && allowed != null && perform != null, "installed native exact method identities are present");
        Check(perform != null && perform.GetMethodBody().ExceptionHandlingClauses.Count > 0, "installed native TryPerform still contains real try/finally world launch");
        var original = PatchProcessor.GetOriginalInstructions(perform);
        bool hasAuthorize = false, hasProxy = false, hasLoader = false, hasLaunch = false, clearsTemporary = false;
        foreach (var instruction in original)
        {
            var method = instruction.operand as MethodInfo;
            var field = instruction.operand as FieldInfo;
            if (method != null && method.Name == "AuthorizeLocalSceneLoad" && method.DeclaringType == type) hasAuthorize = true;
            if (method != null && method.Name == "LoadScene" && method.DeclaringType.Name == "SceneLoaderProxy") hasProxy = true;
            if (method != null && method.Name == "LoadScene" && method.DeclaringType.Name == "SceneLoader") hasLoader = true;
            if (field != null && instruction.opcode == OpCodes.Stfld && field.Name == "_localSceneLoadLaunched") hasLaunch = true;
            if (field != null && instruction.opcode == OpCodes.Stfld && field.Name == "allowLocalSceneLoad") clearsTemporary = true;
        }
        Check(hasAuthorize && hasProxy && hasLoader && hasLaunch && clearsTemporary, "actual native IL retains authorize -> proxy/direct launch -> launched fact -> finally reset contract");
        installedType = type; installedAuthorize = authorize; installedAllowed = allowed;
        installedBegin = assembly.GetType("EscapeFromDuckovCoopMod.RPCScene", true).GetMethod("HandleSceneBeginLoad", flags);
        installedClientBegin = type.GetMethod("Client_OnBeginSceneLoad", flags);
        Check(installedClientBegin != null && installedClientBegin.GetMethodBody().LocalVariables.Count == 1
            && installedClientBegin.GetMethodBody().LocalVariables[0].LocalType.Name == "MapSelectionEntry",
            "installed complete Client_OnBegin handler has the mapped MapSelectionEntry local contract");
        Check(type.GetField("sceneParticipantIds", flags).FieldType == typeof(List<string>), "normal native participant collection uses exact same BCL field type: " + type.GetField("sceneParticipantIds", flags).FieldType.FullName);
        Check(installedBegin != null && installedBegin.GetMethodBody().LocalVariables.Count == 1
            && installedBegin.GetMethodBody().LocalVariables[0].LocalType == type,
            "installed complete BeginLoad handler has the mapped SceneNet local contract");
        var clockPatch = new Harmony("BossRush.Tests.NativeAuthorizationClock");
        clockPatch.Patch(typeof(SceneNet).GetMethod("AuthorizeLocalSceneLoad"), transpiler: new HarmonyMethod(typeof(SceneLoadLeaseProgram), "InstalledAuthorizationIL"));
        clockPatch.Patch(typeof(SceneNet).GetMethod("IsLocalSceneLoadAllowed"), transpiler: new HarmonyMethod(typeof(SceneLoadLeaseProgram), "InstalledAuthorizationIL"));
        clockPatch.Patch(typeof(RPCScene).GetMethod("HandleSceneBeginLoad"), transpiler: new HarmonyMethod(typeof(SceneLoadLeaseProgram), "InstalledBeginIL"));
        clockPatch.Patch(typeof(SceneNet).GetMethod("Client_OnBeginSceneLoad"), prefix: new HarmonyMethod(typeof(SceneLoadLeaseProgram), "CountClientBegin"), transpiler: new HarmonyMethod(typeof(SceneLoadLeaseProgram), "InstalledClientBeginIL"));
        try
        {
            // Unity native constructors cannot execute in a .NET Framework fixture.
            // Execute the installed original IL against equivalent field storage;
            // only Time and Mathf.Max are bound to managed host dependencies.
            var native = new SceneNet();
            TestClock.Now = 1f;
            native.AuthorizeLocalSceneLoad("Base", 10f); native.allowLocalSceneLoad = false;
            Check(native.IsLocalSceneLoadAllowed("Base"), "installed SceneNet original IL permits its unexpired authorized target");
            Check(!native.IsLocalSceneLoadAllowed("GroundZero"), "installed SceneNet original IL denies an unapproved target");
            TestClock.Now = 30f;
            Check(!native.IsLocalSceneLoadAllowed("Base"), "installed SceneNet original IL reproduces expired Closure return before the fix");
            TransportWriterCases.Run(Check, Start);
            RuntimeCases();
            SpectatorCases();
            using (var owner = Start())
            {
                Approved(); native = SceneNet.Instance;
                Check(native.IsLocalSceneLoadAllowed("Base"), "production Transport resumes installed IsAllowed IL after expiry");
                Check(!native.IsLocalSceneLoadAllowed("GroundZero"), "installed IL plus patch still rejects another target");
                Check(!native.IsLocalSceneLoadAllowed(null), "installed IL plus patch does not broaden a null query");
                native.AuthorizeLocalSceneLoad("GroundZero", 10f); native.allowLocalSceneLoad = false; Expire();
                Check(!native.IsLocalSceneLoadAllowed("Base"), "installed original Authorize IL revokes previous retained approval");
            }
        }
        finally { clockPatch.UnpatchAll(clockPatch.Id); }
        Console.WriteLine("Installed SceneNet assembly=" + assembly.Location);
    }
    private static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("Scene lease production cases start");
            RuntimeCases();
            Console.WriteLine("Scene lease installed native IL cases start; checks=" + checks);
            NativeDllCases(args[0], args[1], args[2]);
            Console.WriteLine("CoopSceneLoadLease: PASS " + checks + " production Transport/real Harmony/installed native IL assertions; world/UI boundary isolated.");
        }
        catch (Exception error)
        {
            while (error != null) { Console.WriteLine(error.GetType().FullName + ": " + error.Message); try { Console.WriteLine(error.StackTrace); } catch { } error = error.InnerException; }
            Environment.ExitCode = 1;
        }
    }
}

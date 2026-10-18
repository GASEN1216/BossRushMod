using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Duckov.Economy;
using Duckov.UI;
using BossRush;

public sealed class Health { public bool IsDead; }
public sealed class LevelManager : UnityEngine.Object
{
    public static LevelManager Instance { get; set; }
    public static int loadLevelBeaconIndex;
    public CharacterMainControl MainCharacter { get { return CharacterMainControl.Main; } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; public bool isLoaded; public bool IsValid() { return handle != 0; } }
    public static class SceneManager { public static event Action<Scene> sceneUnloaded; public static void Unload(Scene scene) { if(sceneUnloaded!=null)sceneUnloaded(scene); } }
}
namespace Duckov.Scenes
{
    public sealed class MultiSceneCore : UnityEngine.Component { public static MultiSceneCore Instance { get; set; } }
    public struct MultiSceneLocation { public string LocationName { get; set; } }
}
namespace Eflatun.SceneReference { public sealed class SceneReference { } }
namespace UnityEngine.EventSystems { public sealed class PointerEventData { } }
namespace Cysharp.Threading.Tasks
{
    public struct UniTaskVoid { public void Forget() { } }
    public struct UniTask { }
    public static class UniTaskExtensions { public static void Forget(UniTask value) { } }
}
public static class AudioManager { public static void Post(string key) { } }
namespace Duckov.UI
{
    public sealed partial class MapSelectionEntry { private MapSelectionView master=new MapSelectionView(); }
    public sealed partial class MapSelectionView
    {
        private string sfx_EntryClicked="native";private bool loading;
        public static int VanillaLoads;
        private Cysharp.Threading.Tasks.UniTask LoadTask(string scene,Cost cost){VanillaLoads++;return new Cysharp.Threading.Tasks.UniTask();}
    }
}
namespace LiteNetLib { public sealed class NetPeer { public string Id; } public sealed class NetManager { } }
namespace EscapeFromDuckovCoopMod.Utils { public static class SceneTriggerResetter { public static void ResetAllSceneTriggers() { } } }
namespace EscapeFromDuckovCoopMod
{
    public sealed class ModBehaviourF : UnityEngine.Object
    {
        public static ModBehaviourF Instance=new ModBehaviourF();
        public bool networkStarted { get { return NetService.Instance.NetworkStarted; } }
        public bool IsServer { get { return NetService.Instance.IsServer; } }
    }
    public static class Patch_Mapen_OnPointerClick
    { [MethodImpl(MethodImplOptions.NoInlining)]public static bool Prefix(MapSelectionEntry __instance,UnityEngine.EventSystems.PointerEventData eventData){throw new Exception("native IL required");} }
    public static class Patch_MapSelectionView_NotifyEntryClicked_Authority
    { [MethodImpl(MethodImplOptions.NoInlining)]public static bool Prefix(MapSelectionEntry mapSelectionEntry,UnityEngine.EventSystems.PointerEventData eventData){throw new Exception("native IL required");} }
    public static class SceneLoadVoteGuard
    {
        [MethodImpl(MethodImplOptions.NoInlining)]public static bool TryStartVote(string target,Eflatun.SceneReference.SceneReference curtain,bool notify,bool save,bool use,Duckov.Scenes.MultiSceneLocation location,string source){throw new Exception("native IL required");}
        private static string SafeSceneGuid(Eflatun.SceneReference.SceneReference curtain){return null;}
    }
    public sealed class NetService : UnityEngine.Object
    {
        public static NetService Instance;
        public bool NetworkStarted=true;
        public bool IsServer { get { return BossRushCoop.IsHost; } }
        public string GetPlayerId(LiteNetLib.NetPeer peer) { return peer==null?"host":peer.Id; }
    }
    public struct SceneVoteStartRpc { public string TargetSceneId,CurtainGuid,LocationName,HostSceneId; public bool NotifyEvac,SaveToFile,UseLocation; public string[] ParticipantIds; }
    public struct SceneReadySetRpc { public string PlayerId; public bool IsReady; }
    public struct SceneVoteCancelRpc { }
    public struct SceneVoteRequestRpc { public string TargetSceneId,CurtainGuid,LocationName; public bool NotifyEvac,SaveToFile,UseLocation; }
    public struct RpcContext { public bool IsServer { get { return NetService.Instance.IsServer; } } }
    public struct SceneBeginLoadRpc
    { public string SceneId,CurtainGuid,LocationName; public bool NotifyEvac,SaveToFile,UseLocation,ForceSpawnAtHost; public Vector3 HostSpawnPosition; public Quaternion HostSpawnRotation; }
    public sealed class Spectator
    { public static Spectator Instance = new Spectator(); public bool _spectatorActive,_spectatorEndOnVotePending; public void EndSpectatorAndShowClosure(bool value) { } }
    public sealed class CoopGeneralSettings { public bool TeleporterSpawnTogether; }
    public static class CoopAISettings { public static CoopGeneralSettings ActiveGeneral { get { return new CoopGeneralSettings(); } } }
    public sealed class LocalPlayerManager
    { public static LocalPlayerManager Instance=new LocalPlayerManager(); public bool ComputeIsInGame(out string scene) { scene="Base"; return true; } }
    public static class CoopLocalization { public static string Get(string key,object[] args) { return key; } }
    public static class MModUI { public static void ShowTip(string text,float duration) { } }
    public static class CoopTool
    {
        public static int Begins,Loads,VoteStarts,Cancels,GuestVoteRequests;
        public static bool ThrowBeforeSend,ThrowAfterSend;
        public static Action BeforeSend;
        public static List<string> BuildParticipantIds_Server() { return new List<string>{"host","guest1","guest2"}; }
        public static MapSelectionEntry GetMapSelectionEntrylist(string id) { return Probe.Entry; }
        public static bool GoTeleport(string target,string curtain) { Loads++;return true; }
        public static void SendRpc<T>(ref T value,LiteNetLib.NetPeer peer)
        {
            if(typeof(T)==typeof(SceneBeginLoadRpc))
            {
                if(BeforeSend!=null)BeforeSend();
                if(ThrowBeforeSend)throw new Exception("transport before send");
                if(!Probe.OtherMap && !BossRushMapSelectionHelper.HasPendingPrepaidTicket())throw new Exception("BeginLoad preceded actual receipt");
                Begins++;
                if(ThrowAfterSend)throw new Exception("transport after send");
            }
            if(typeof(T)==typeof(SceneVoteStartRpc))VoteStarts++;
            if(typeof(T)==typeof(SceneVoteCancelRpc))Cancels++;
        }
    }
    public sealed class SceneNet : UnityEngine.Object
    {
        public static SceneNet Instance;
        public bool sceneVoteActive,localReady,IsMapSelectionEntry,IsDoteleportMap,_localSceneLoadLaunched;
        public bool sceneNotifyEvac,sceneSaveToFile,sceneUseLocation,_srvPendingBeginLoad,_srvPendingSceneLoad,_srvLoadInProgress;
        public float _srvPendingBeginLoadTime,_srvPendingSceneLoadTime;
        public string sceneTargetId,sceneCurtainGuid,sceneLocationName;
        public List<string> sceneParticipantIds=new List<string>();
        public Dictionary<string,bool> sceneReady=new Dictionary<string,bool>();
        public bool IsServer { get { return NetService.Instance.IsServer; } }
        public bool networkStarted { get { return NetService.Instance.NetworkStarted; } }
        public LiteNetLib.NetManager netManager { get { return new LiteNetLib.NetManager(); } }
        public bool IsLocalSceneLoadAllowed(string target){return false;}
        public void Client_RequestBeginSceneVote(string target,string curtain,bool notify,bool save,bool use,string source){ CoopTool.GuestVoteRequests++; }
        [MethodImpl(MethodImplOptions.NoInlining)]public void Host_BeginSceneVote_Simple(string target,string curtain,bool notify,bool save,bool use,string source){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_OnSceneReadySet(LiteNetLib.NetPeer peer,bool ready){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_ScheduleBeginSceneLoad(float delay){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_TryExecutePendingBeginLoad(){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_BroadcastBeginSceneLoad(){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_ScheduleSceneLoad(float delay){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_TryExecutePendingLoad(){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Host_PerformSceneLoad(){throw new Exception("native IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void CancelVote(){throw new Exception("native IL required");}
        public void AuthorizeLocalSceneLoad(string target,float ttl){ }
        public void TryPerformSceneLoad_Local(string target,string curtain,bool notify,bool save,bool use,string source){CoopTool.Loads++;}
        public Cysharp.Threading.Tasks.UniTaskVoid FallbackRetryTeleport(string target,string curtain){return new Cysharp.Threading.Tasks.UniTaskVoid();}
        public Cysharp.Threading.Tasks.UniTaskVoid FallbackRetryLocalSceneLoad(string target,string curtain,bool notify,bool save,bool use,string source){return new Cysharp.Threading.Tasks.UniTaskVoid();}
    }
    public static class RPCScene { [MethodImpl(MethodImplOptions.NoInlining)]public static void HandleSceneVoteRequest(RpcContext context,SceneVoteRequestRpc message){throw new Exception("native IL required");} [MethodImpl(MethodImplOptions.NoInlining)]public static void HandleSceneVoteCancel(object context,SceneVoteCancelRpc message) { SceneNet.Instance.sceneVoteActive=false; } }
}
namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    { public abstract string ModuleName { get; } public virtual void OnUpdate(float a,float b){} public virtual void OnDestroy(){} }
}

internal static class NativeScene
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static readonly Dictionary<MethodBase,MethodInfo> bodies=new Dictionary<MethodBase,MethodInfo>();
    private static string[] roots;
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    { foreach(string root in roots){string path=Path.Combine(root,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null; }
    private static Type Map(Type type)
    {
        if(type.IsByRef)return Map(type.GetElementType()).MakeByRefType();
        if(type.IsArray)return Map(type.GetElementType()).MakeArrayType();
        if(type.IsGenericParameter)return type;
        if(type.IsGenericType)return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(Map).ToArray());
        if(type.Assembly==typeof(object).Assembly)return type;
        return typeof(NativeScene).Assembly.GetType(type.FullName,false)??type;
    }
    private static object Member(object operand)
    {
        var type=operand as Type;if(type!=null)return Map(type);
        var field=operand as FieldInfo;if(field!=null){var owner=Map(field.DeclaringType);return owner==field.DeclaringType?field:owner.GetField(field.Name,All)??throw new Exception("field seam "+field);}
        var ctor=operand as ConstructorInfo;if(ctor!=null){var owner=Map(ctor.DeclaringType);return owner==ctor.DeclaringType?ctor:owner.GetConstructor(All,null,ctor.GetParameters().Select(p=>Map(p.ParameterType)).ToArray(),null)??throw new Exception("ctor seam "+ctor);}
        var method=operand as MethodInfo;if(method==null)return operand;
        var mapped=Map(method.DeclaringType);if(mapped==method.DeclaringType)return method;
        var choices=mapped.GetMethods(All).Where(m=>m.Name==method.Name&&m.GetParameters().Length==method.GetParameters().Length&&m.GetGenericArguments().Length==method.GetGenericArguments().Length).ToArray();
        if(choices.Length==0)throw new InvalidOperationException("method seam "+method.DeclaringType.FullName+"::"+method.Name);
        var found=choices.Length==1?choices[0]:choices.Single(m=>m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(method.GetParameters().Select(p=>Map(p.ParameterType))));
        return method.IsGenericMethod?found.MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray()):found;
    }
    private static IEnumerable<CodeInstruction> Replay(IEnumerable<CodeInstruction> ignored,MethodBase __originalMethod,ILGenerator generator)
    {
        var source=bodies[__originalMethod];int localBase=generator.DeclareLocal(typeof(int)).LocalIndex+1;
        var instructions=PatchProcessor.GetOriginalInstructions(source,generator).ToList();
        var locals=source.GetMethodBody().LocalVariables.Select(v=>generator.DeclareLocal(Map(v.LocalType))).ToArray();
        foreach(var code in instructions)
        {
            int i=-1;
            if(code.opcode==OpCodes.Ldloc_0||code.opcode==OpCodes.Stloc_0)i=0;
            if(code.opcode==OpCodes.Ldloc_1||code.opcode==OpCodes.Stloc_1)i=1;
            if(code.opcode==OpCodes.Ldloc_2||code.opcode==OpCodes.Stloc_2)i=2;
            if(code.opcode==OpCodes.Ldloc_3||code.opcode==OpCodes.Stloc_3)i=3;
            if(code.opcode==OpCodes.Ldloc||code.opcode==OpCodes.Ldloc_S||code.opcode==OpCodes.Stloc||code.opcode==OpCodes.Stloc_S||code.opcode==OpCodes.Ldloca||code.opcode==OpCodes.Ldloca_S)
            {var local=code.operand as LocalBuilder;i=local!=null?local.LocalIndex-localBase:Convert.ToInt32(code.operand);}
            if(i>=0){bool store=code.opcode.Name.StartsWith("stloc"),address=code.opcode.Name.StartsWith("ldloca");code.opcode=store?OpCodes.Stloc:address?OpCodes.Ldloca:OpCodes.Ldloc;code.operand=locals[i];}
            else code.operand=Member(code.operand);
            yield return code;
        }
    }
    internal static void Install(string[] paths)
    {
        roots=paths;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        var native=Assembly.LoadFrom(Path.Combine(paths[0],"EscapeFromDuckovCoopMod.dll"));
        var harmony=new Harmony("Test.CoopMapPayment.Native");
        foreach(var target in new[]{typeof(EscapeFromDuckovCoopMod.SceneNet),typeof(EscapeFromDuckovCoopMod.Patch_Mapen_OnPointerClick),typeof(EscapeFromDuckovCoopMod.Patch_MapSelectionView_NotifyEntryClicked_Authority),typeof(EscapeFromDuckovCoopMod.SceneLoadVoteGuard),typeof(EscapeFromDuckovCoopMod.RPCScene)})
        foreach(var method in target.GetMethods(All).Where(m=>m.Name=="HandleSceneVoteRequest"||m.Name=="Prefix"||m.Name=="TryStartVote"||m.Name.StartsWith("Server_")||m.Name=="Host_PerformSceneLoad"||m.Name=="Host_BeginSceneVote_Simple"||m.Name=="CancelVote"))
        {
            if(method.DeclaringType!=target)continue;
            Console.WriteLine("Bind installed native "+method.Name);
            var real=native.GetType(target.FullName,true);
            var source=real.GetMethods(All).Single(m=>m.Name==method.Name&&m.GetParameters().Select(p=>Map(p.ParameterType)).SequenceEqual(method.GetParameters().Select(p=>p.ParameterType)));
            bodies.Add(method,source);
            try { harmony.Patch(method,transpiler:new HarmonyMethod(typeof(NativeScene),"Replay")); }
            catch(Exception e)
            {
                while(e!=null){Console.WriteLine("PATCH ERROR "+e.GetType().FullName+": "+e.Message);e=e.InnerException;}
                throw new InvalidOperationException("Could not replay "+method.Name);
            }
            Console.WriteLine("FULL INSTALLED NATIVE IL "+source+" bytes="+source.GetMethodBody().GetILAsByteArray().Length);
        }
    }
    internal static void ClickHarmony(bool nativeFirst,Action click)
    {
        const string owner="Test.CoopMapPayment.Click";
        var harmony=new Harmony(owner);
        var pointer=typeof(MapSelectionEntry).GetMethod("OnPointerClick");
        var notify=typeof(MapSelectionView).GetMethod("NotifyEntryClicked",All);
        Action native=()=>
        {
            harmony.Patch(pointer,prefix:new HarmonyMethod(typeof(EscapeFromDuckovCoopMod.Patch_Mapen_OnPointerClick),"Prefix"));
            harmony.Patch(notify,prefix:new HarmonyMethod(typeof(EscapeFromDuckovCoopMod.Patch_MapSelectionView_NotifyEntryClicked_Authority),"Prefix"));
        };
        Action own=()=>
        {
            harmony.Patch(pointer,prefix:new HarmonyMethod(typeof(BossRushMapEntryPointerSelectionPatch),"BeforePointer"){priority=Priority.First});
            harmony.Patch(notify,prefix:new HarmonyMethod(typeof(BossRushMapEntrySelectionPatch),"BeforeNotify"){priority=Priority.First});
        };
        try { if(nativeFirst){native();own();}else{own();native();}click(); }
        finally { harmony.UnpatchAll(owner); }
    }
}

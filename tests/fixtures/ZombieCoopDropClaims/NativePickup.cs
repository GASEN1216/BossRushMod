using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BossRush;
using HarmonyLib;
using UnityEngine;
using ItemStatsSystem;
namespace LiteNetLib{public sealed class NetPeer{}}
namespace EscapeFromDuckovCoopMod
{
    public sealed class NetService:UnityEngine.Object
    {
        public static NetService Instance=new NetService();public bool networkStarted=true;
        public bool IsServer{get{return BossRushCoop.IsHost;}}
    }
    public sealed class SceneNet{public static SceneNet Instance=new SceneNet();public bool IsServerLoadInProgress(){return false;}}
    public static class COOPManager{public static ItemNet ItemNet=new ItemNet();}
    public static class GunAmmoUnloadGuard{public static bool InClientGunAmmoUnload{get{return false;}}public static void LogSuppressedDrop(Item item){}}
    public static class NetSilenceGuards{public static bool InPickupItem;}
    public sealed class ItemNet
    {
        public static bool InNetworkDrop;
        private uint _nextDropId=100;
        private readonly HashSet<uint> _pendingPickups=new HashSet<uint>();
        [MethodImpl(MethodImplOptions.NoInlining)]public void Client_RequestPickup(Item item){throw new Exception("native client pickup IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_HandleLocalPickup(Item item){throw new Exception("native local pickup IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_HandlePickupRequest(RpcContext ctx,ItemPickupRequestRpc request){throw new Exception("native server pickup IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Client_HandleDespawn(ItemDespawnRpc request){throw new Exception("native client despawn IL required");}
        public void Client_RequestDrop(Item item,Vector3 pos,bool createRigidbody,Vector3 direction,float angle,bool reliable){BossRushCoop.Current.NativeDropRequests++;}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Server_RegisterLocalDrop(Item item,DuckovItemAgent agent,Vector3 pos,bool createRigidbody,Vector3 direction,float angle){throw new Exception("native register-local-drop IL required");}
    }
    public struct RpcContext
    {
        public NetService Service{get;private set;}public LiteNetLib.NetPeer Sender{get;private set;}
        public RpcContext(NetService service,LiteNetLib.NetPeer sender){Service=service;Sender=sender;}
        public bool IsServer{get{return Service!=null&&Service.IsServer;}}
    }
    public struct ItemPickupRequestRpc{public uint DropId;}
    public struct ItemDespawnRpc{public uint DropId;}
    public struct ItemSpawnRpc{public uint Token,DropId;public Vector3 Position,Direction;public float Angle;public bool CreateRigidbody;public ItemSnapshot Snapshot;}
    public struct ItemSnapshot{public int TypeID,Count,Maximum;public Dictionary<string,string> Metadata;}
    public sealed class NetDropTag:MonoBehaviour{public uint id;}
    public static class ItemTool
    {
        public static bool HasQuestTag(Item item){return false;}public static bool HasQuestTag(GameObject gameObject){return false;}
        public static void AddNetDropTag(GameObject gameObject,uint id){var tag=gameObject.GetComponent<NetDropTag>()??gameObject.AddComponent<NetDropTag>();tag.id=id;}
        public static ItemSnapshot MakeSnapshot(Item item)
        {
            var data=new Dictionary<string,string>();BossRushCoop.Capture(item,data);
            return new ItemSnapshot{TypeID=item.TypeID,Count=item.StackCount,Maximum=item.MaxStackCount,Metadata=data};
        }
    }
    public sealed class DropSyncEntry
    {public uint DropId{get;set;}public Item Item{get;set;}public GameObject Agent{get;set;}}
    public sealed class DropSyncRegistry
    {
        private readonly Dictionary<uint,DropSyncEntry> _entries=new Dictionary<uint,DropSyncEntry>();
        private readonly Dictionary<Item,DropSyncEntry> _byItem=new Dictionary<Item,DropSyncEntry>();
        public int Count{get{return _entries.Count;}}
        [MethodImpl(MethodImplOptions.NoInlining)]public DropSyncEntry Register(uint id,Item item,GameObject agent){throw new Exception("native register IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public bool TryGetById(uint id,out DropSyncEntry entry){throw new Exception("native by-ID IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public bool TryGetByItem(Item item,out DropSyncEntry entry){throw new Exception("native by-item IL required");}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Unregister(uint id){throw new Exception("native unregister IL required");}
    }
    public static class CoopSyncDatabase{public static DropSyncRegistry Drops{get{return BossRushCoop.Current.NativeDrops;}}}
    public static class CoopTool
    {
        public static int PickupNotificationExceptions;
        public static void SendRpc<T>(ref T value,LiteNetLib.NetPeer peer)
        {
            if(typeof(T)==typeof(ItemPickupRequestRpc))
            {
                BossRushCoop.Current.NativePickups++;var request=(ItemPickupRequestRpc)(object)value;var source=BossRushCoop.Current;
                BossRushCoop.Wire.Enqueue(()=>{var saved=BossRushCoop.Current;BossRushCoop.Use(BossRushCoop.Peers[0]);try{COOPManager.ItemNet.Server_HandlePickupRequest(new RpcContext(NetService.Instance,new LiteNetLib.NetPeer()),request);}finally{BossRushCoop.Use(saved);}});
                if(PickupNotificationExceptions>0){PickupNotificationExceptions--;throw new InvalidOperationException("native pickup notification exception after enqueue");}
            }
            else if(typeof(T)==typeof(ItemDespawnRpc))
            {
                var request=(ItemDespawnRpc)(object)value;BossRushCoop.Current.NativeDespawns++;
                foreach(var target in BossRushCoop.Peers.Where(p=>!p.Host))
                    BossRushCoop.Wire.Enqueue(()=>{var saved=BossRushCoop.Current;BossRushCoop.Use(target);try{COOPManager.ItemNet.Client_HandleDespawn(request);}finally{BossRushCoop.Use(saved);}});
            }
            else if(typeof(T)==typeof(ItemSpawnRpc))
            {
                var request=(ItemSpawnRpc)(object)value;
                foreach(var target in BossRushCoop.Peers.Where(p=>!p.Host))
                    BossRushCoop.Wire.Enqueue(()=>
                    {
                        var saved=BossRushCoop.Current;BossRushCoop.Use(target);
                        try
                        {
                            var item=new Item{TypeID=request.Snapshot.TypeID,MaxStackCount=request.Snapshot.Maximum};item.StackCount=request.Snapshot.Count;
                            var agent=new DuckovItemAgent();agent.transform.position=request.Position;item.AgentUtilities.ActiveAgent=agent;
                            CoopSyncDatabase.Drops.Register(request.DropId,item,agent.gameObject);ItemTool.AddNetDropTag(agent.gameObject,request.DropId);
                            target.SpawnedItems.Add(item);BossRushCoop.Restore(item,request.Snapshot.Metadata);
                        }
                        finally{BossRushCoop.Use(saved);}
                    });
            }
        }
    }
    public static class CharacterPickupPatch
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(CharacterMainControl __instance,Item item,bool __result){throw new Exception("native IL must replace pickup postfix");}
    }
    public static class Patch_CharacterItemControl_PickupItem
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Prefix(){throw new Exception("native IL must replace pickup context prefix");}
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Finalizer(){throw new Exception("native IL must replace pickup context finalizer");}
    }
    public static class ItemDropPatch
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool Prefix(Item item,Vector3 pos,bool createRigidbody,Vector3 dropDirection,float randomAngle,ref DuckovItemAgent __result){throw new Exception("native IL must replace drop prefix");}
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(Item item,Vector3 pos,bool createRigidbody,Vector3 dropDirection,float randomAngle,DuckovItemAgent __result){throw new Exception("native IL must replace drop postfix");}
    }
}
internal static class NativePickup
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static readonly Dictionary<MethodBase,MethodInfo> Bodies=new Dictionary<MethodBase,MethodInfo>();private static string[] roots;
    private static Assembly Resolve(object sender,ResolveEventArgs args){foreach(string root in roots){string path=Path.Combine(root,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;}
    private static void AuditLocalPickup(Item item){BossRushCoop.Current.NativePickups++;}
    private static void AuditServerPickup(EscapeFromDuckovCoopMod.RpcContext __0){if(__0.Sender==null)BossRushCoop.Current.NullPeerServerRequests++;}
    private static void AuditRegisterDrop(){BossRushCoop.Current.NativeDropRegistrations++;}
    private static Type Map(Type type)
    {
        if(type.IsByRef)return Map(type.GetElementType()).MakeByRefType();if(type.IsArray)return Map(type.GetElementType()).MakeArrayType();if(type.IsGenericParameter)return type;
        if(type.IsGenericType)return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(Map).ToArray());
        if(type.Assembly==typeof(object).Assembly)return type;
        return typeof(NativePickup).Assembly.GetType(type.FullName,false)??typeof(NativePickup).Assembly.GetTypes().FirstOrDefault(t=>t.Name==type.Name)??type;
    }
    private static object Member(object operand)
    {
        Type type=operand as Type;if(type!=null)return Map(type);
        FieldInfo field=operand as FieldInfo;if(field!=null){Type owner=Map(field.DeclaringType);return owner==field.DeclaringType?field:owner.GetField(field.Name,All)??throw new Exception("missing field seam "+field);}
        ConstructorInfo ctor=operand as ConstructorInfo;if(ctor!=null){Type owner=Map(ctor.DeclaringType);return owner==ctor.DeclaringType?ctor:owner.GetConstructors(All).Single(c=>c.IsStatic==ctor.IsStatic&&c.GetParameters().Select(p=>p.ParameterType).SequenceEqual(ctor.GetParameters().Select(p=>Map(p.ParameterType))));}
        MethodInfo method=operand as MethodInfo;if(method==null)return operand;Type mapped=Map(method.DeclaringType);if(mapped==method.DeclaringType)return method;
        var choices=mapped.GetMethods(All).Where(m=>m.Name==method.Name&&m.GetParameters().Length==method.GetParameters().Length&&m.GetGenericArguments().Length==method.GetGenericArguments().Length).ToArray();
        if(choices.Length==0)throw new Exception("missing method seam "+method.DeclaringType+"."+method);
        MethodInfo found=choices.Length==1?choices[0]:choices.Single(m=>m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(method.GetParameters().Select(p=>Map(p.ParameterType))));
        return method.IsGenericMethod?found.MakeGenericMethod(method.GetGenericArguments().Select(Map).ToArray()):found;
    }
    private static IEnumerable<CodeInstruction> NativeIL(IEnumerable<CodeInstruction> ignored,MethodBase __originalMethod,ILGenerator generator)
    {
        MethodInfo source=Bodies[__originalMethod];int nativeLocalBase=generator.DeclareLocal(typeof(int)).LocalIndex+1;
        var instructions=PatchProcessor.GetOriginalInstructions(source,generator).ToList();var locals=source.GetMethodBody().LocalVariables.Select(v=>generator.DeclareLocal(Map(v.LocalType))).ToArray();
        foreach(var code in instructions)
        {
            int index=-1;if(code.opcode==OpCodes.Ldloc_0||code.opcode==OpCodes.Stloc_0)index=0;if(code.opcode==OpCodes.Ldloc_1||code.opcode==OpCodes.Stloc_1)index=1;if(code.opcode==OpCodes.Ldloc_2||code.opcode==OpCodes.Stloc_2)index=2;if(code.opcode==OpCodes.Ldloc_3||code.opcode==OpCodes.Stloc_3)index=3;
            if(code.opcode==OpCodes.Ldloc||code.opcode==OpCodes.Ldloc_S||code.opcode==OpCodes.Stloc||code.opcode==OpCodes.Stloc_S||code.opcode==OpCodes.Ldloca||code.opcode==OpCodes.Ldloca_S){var local=code.operand as LocalBuilder;index=local!=null?local.LocalIndex-nativeLocalBase:Convert.ToInt32(code.operand);}
            if(index>=0){bool store=code.opcode.Name.StartsWith("stloc"),address=code.opcode.Name.StartsWith("ldloca");code.opcode=store?OpCodes.Stloc:address?OpCodes.Ldloca:OpCodes.Ldloc;code.operand=locals[index];}else code.operand=Member(code.operand);yield return code;
        }
    }
    internal static void Install(string[] args)
    {
        roots=args;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;var native=Assembly.LoadFrom(Path.Combine(args[0],"EscapeFromDuckovCoopMod.dll"));var harmony=new Harmony("Test.ZombieDropNativePickup");
        foreach(Type target in new[]{typeof(EscapeFromDuckovCoopMod.CharacterPickupPatch),typeof(EscapeFromDuckovCoopMod.ItemDropPatch),typeof(EscapeFromDuckovCoopMod.Patch_CharacterItemControl_PickupItem),typeof(EscapeFromDuckovCoopMod.ItemNet),typeof(EscapeFromDuckovCoopMod.DropSyncRegistry),typeof(EscapeFromDuckovCoopMod.RpcContext)})
            foreach(MethodInfo method in target.GetMethods(All).Where(m=>m.DeclaringType==target&&!m.Name.StartsWith("set_")))
            {if(method.Name=="Client_RequestDrop"||method.Name=="get_Count")continue;Console.WriteLine("Binding native "+target.Name+"."+method.Name);var source=native.GetType(target.FullName,true).GetMethods(All).Single(m=>m.Name==method.Name&&m.GetParameters().Select(p=>Map(p.ParameterType)).SequenceEqual(method.GetParameters().Select(p=>p.ParameterType)));Bodies.Add(method,source);harmony.Patch(method,transpiler:new HarmonyMethod(typeof(NativePickup),"NativeIL"));Console.WriteLine("FULL NATIVE IL "+source.DeclaringType.Name+"."+method.Name+" bytes="+source.GetMethodBody().GetILAsByteArray().Length);}
        harmony.Patch(typeof(EscapeFromDuckovCoopMod.ItemNet).GetMethod("Server_HandleLocalPickup"),prefix:new HarmonyMethod(typeof(NativePickup),"AuditLocalPickup"));
        harmony.Patch(typeof(EscapeFromDuckovCoopMod.ItemNet).GetMethod("Server_HandlePickupRequest"),prefix:new HarmonyMethod(typeof(NativePickup),"AuditServerPickup"));
        harmony.Patch(typeof(EscapeFromDuckovCoopMod.ItemNet).GetMethod("Server_RegisterLocalDrop"),prefix:new HarmonyMethod(typeof(NativePickup),"AuditRegisterDrop"));
    }
}

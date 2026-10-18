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

namespace LiteNetLib{public sealed class NetPeer{public string Id;}}
namespace EscapeFromDuckovCoopMod
{
    public sealed class RemoteReplicaTag:Component{}
    public sealed class AutoRequestHealthBar:Component{}
    public sealed class ModBehaviourF:UnityEngine.Object{public static ModBehaviourF Instance=new ModBehaviourF();public bool networkStarted{get{return BossRushCoop.IsConnected;}}public bool IsServer{get{return BossRushCoop.IsHost;}}}
    public sealed class NetService:UnityEngine.Object
    {
        public static NetService Instance=new NetService();public bool Invincible;
        public string GetPlayerId(LiteNetLib.NetPeer peer){return peer.Id;}
        public bool TryGetPlayerId(CharacterMainControl character,out string id){id=BossRushCoop.GetPlayerId(character);return id!=null;}
        public bool IsPlayerInvincible(string id){return Invincible;}
    }
    public static class LocalHitKillFx{public static void ClientPlayForPlayer(CharacterMainControl c,DamageInfo d,bool killed){}public static void RememberLastBaseDamage(float value){}}
    public struct PlayerDamageForwardRpc{public string PlayerId;public DamageForwardPayload Damage;}
    public struct PlayerDamageRequestRpc{public string TargetPlayerId;public DamageForwardPayload Damage;}
    public struct DamageForwardPayload
    {
        public float DamageValue,ArmorPiercing,CritDamageFactor,CritRate,BleedChance,BuffChance,DamageFactorToZombie,ArmorBreak;
        public int Crit,WeaponItemId,BuffId;public bool IsExplosion,IsFromBuffOrEffect,IgnoreArmor,IgnoreDifficulty;
        public Vector3 HitPoint,HitNormal;public DamageTypes DamageType;public List<ElementFactor> ElementFactors;
        [MethodImpl(MethodImplOptions.NoInlining)]public static DamageForwardPayload FromDamageInfo(DamageInfo? info){throw new Exception("actual installed IL must replace this seam");}
        [MethodImpl(MethodImplOptions.NoInlining)]public DamageInfo ToDamageInfo(CharacterMainControl source,DamageReceiver receiver,Duckov.Buffs.Buff buff){throw new Exception("actual installed IL must replace this seam");}
    }
    public static class Patch_ServerForwardRemotePlayerDamage
    {
        [MethodImpl(MethodImplOptions.NoInlining)]public static bool Prefix(DamageReceiver receiver,ref DamageInfo info){throw new Exception("actual installed IL must replace this seam");}
    }
    public static class CoopTool
    {
        public static readonly List<PlayerDamageForwardRpc> Forwards=new List<PlayerDamageForwardRpc>();
        public static LiteNetLib.NetPeer TryGetPeerForCharacter(CharacterMainControl actor){string id=BossRushCoop.GetPlayerId(actor);return id==null?null:new LiteNetLib.NetPeer{Id=id};}
        public static void SendRpc<T>(ref T rpc,LiteNetLib.NetPeer peer){}
        public static void SendRpcTo<T>(LiteNetLib.NetPeer peer,ref T rpc){var hit=(PlayerDamageForwardRpc)(object)rpc;if(hit.PlayerId!=peer.Id)throw new Exception("native destination mismatch");Forwards.Add(hit);}
    }
}
internal static class NativeDamage
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static readonly Dictionary<MethodBase,MethodInfo> Bodies=new Dictionary<MethodBase,MethodInfo>();private static string[] roots;
    private static Assembly Resolve(object sender,ResolveEventArgs args){foreach(string root in roots){string path=Path.Combine(root,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;}
    private static Type Map(Type type)
    {
        if(type.IsByRef)return Map(type.GetElementType()).MakeByRefType();if(type.IsArray)return Map(type.GetElementType()).MakeArrayType();if(type.IsGenericParameter)return type;
        if(type.IsGenericType)return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(Map).ToArray());
        if(type.Assembly==typeof(object).Assembly)return type;
        return typeof(NativeDamage).Assembly.GetType(type.FullName,false)??typeof(NativeDamage).Assembly.GetTypes().FirstOrDefault(t=>t.Name==type.Name)??type;
    }
    private static object Member(object operand)
    {
        Type type=operand as Type;if(type!=null)return Map(type);
        FieldInfo field=operand as FieldInfo;if(field!=null){Type owner=Map(field.DeclaringType);return owner==field.DeclaringType?field:owner.GetField(field.Name,All)??throw new Exception("missing field seam "+field);}
        ConstructorInfo ctor=operand as ConstructorInfo;if(ctor!=null){Type owner=Map(ctor.DeclaringType);return owner==ctor.DeclaringType?ctor:owner.GetConstructors(All).Single(c=>c.IsStatic==ctor.IsStatic&&c.GetParameters().Select(p=>p.ParameterType).SequenceEqual(ctor.GetParameters().Select(p=>Map(p.ParameterType))));}
        MethodInfo method=operand as MethodInfo;if(method==null)return operand;Type mapped=Map(method.DeclaringType);if(mapped==method.DeclaringType)return method;
        var choices=mapped.GetMethods(All).Where(m=>m.Name==method.Name&&m.GetParameters().Length==method.GetParameters().Length&&m.GetGenericArguments().Length==method.GetGenericArguments().Length).ToArray();
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
        roots=args;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;var native=Assembly.LoadFrom(Path.Combine(args[0],"EscapeFromDuckovCoopMod.dll"));var harmony=new Harmony("Test.ZombieZoneNativeDamage");
        foreach(Type target in new[]{typeof(EscapeFromDuckovCoopMod.DamageForwardPayload),typeof(EscapeFromDuckovCoopMod.Patch_ServerForwardRemotePlayerDamage)})
            foreach(MethodInfo method in target.GetMethods(All).Where(m=>m.DeclaringType==target))
            {Console.WriteLine("Binding native "+target.Name+"."+method.Name);var source=native.GetType(target.FullName,true).GetMethods(All).Single(m=>m.Name==method.Name&&m.GetParameters().Length==method.GetParameters().Length);Bodies.Add(method,source);harmony.Patch(method,transpiler:new HarmonyMethod(typeof(NativeDamage),"NativeIL"));Console.WriteLine("FULL NATIVE IL "+source.DeclaringType.Name+"."+method.Name+" bytes="+source.GetMethodBody().GetILAsByteArray().Length);}
    }
}

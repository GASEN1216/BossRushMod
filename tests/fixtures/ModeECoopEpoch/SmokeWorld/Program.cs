using System;
using System.Collections.Generic;
using BossRush;

namespace UnityEngine
{
    internal struct Vector3 { public float x,y,z; }
    internal struct Quaternion { internal static Quaternion identity {get{return new Quaternion();}} }
    internal sealed class Object
    {
        internal static int Instances;
        internal static Object Instantiate(Object prefab,Vector3 position,Quaternion rotation) {Instances++;return new Object();}
    }
}
namespace BossRush
{
    internal static class BossRushCoop
    {
        internal static bool IsHost,HasBoundScene;
        internal static bool IsClient {get{return !IsHost;}}
        internal static string RunId,SceneToken,LocalAssetId;
        internal static int SceneGeneration;
        internal static string BroadcastJson;
        internal static void Broadcast<T>(string topic,T value) {BroadcastJson=BossRushCoopJson.ToJson(value);}
    }
    internal sealed class BossRushCoopMessage
    {
        internal bool IsFromHost;
        internal string Json;
        internal T Read<T>() {return BossRushCoopJson.FromJson<T>(Json);}
    }
    internal static class ModBehaviour {internal static void DevLog(string text) {}}
    internal static class L10n {internal static string T(string a,string b) {return a;}}
    internal static class RespawnItemConfig
    {internal const int TAUNT_SMOKE_TYPE_ID=1,CHAOS_DETONATOR_TYPE_ID=2,BOSSCALL_WHISTLE_TYPE_ID=3,ALL_KINGS_BANNER_TYPE_ID=4;}
    internal sealed class HostBoundary
    {
        internal bool Delivery=true;
        internal int Refunds;
        internal bool TryGiveItemToPlayerOrDrop(int type,string text,bool banner) {if(!Delivery)return false;Refunds++;return true;}
        internal void ShowMessage(string text) {}
    }
    internal partial class ModeERuntimeModule
    {
        private readonly HashSet<string> coopUtilitySmokeSeen=new HashSet<string>();
        private readonly Dictionary<string,CoopModeEUtilityRequest> coopUtilityRequests=new Dictionary<string,CoopModeEUtilityRequest>();
        private readonly HostBoundary modeEHost=new HostBoundary();
        private UnityEngine.Object GetModeESmokeVfxPrefab() {return new UnityEngine.Object();}
        internal void Packet(string id,string token,bool host=true,string run="room",int scene=7)
        {
            OnCoopUtilitySmoke(new BossRushCoopMessage {IsFromHost=host,Json=BossRushCoopJson.ToJson(new SmokeWire
            {Id=id,Run=run,Scene=scene,SceneToken=token,Position=new UnityEngine.Vector3()})});
        }
        internal void Publish() {PublishCoopUtilitySmoke(new UnityEngine.Vector3());}
        internal static void RefundCases(Action<bool,string> check)
        {
            var mode=new ModeERuntimeModule();
            mode.coopUtilityRequests.Add("old",new CoopModeEUtilityRequest {Id="old",AssetId="original",Item=1,Run="retired",Scene=1});
            var receipt=new CoopModeEPersonal {UtilityId="old",UtilitySucceeded=false};
            BossRushCoop.HasBoundScene=false;BossRushCoop.SceneToken=null;BossRushCoop.LocalAssetId="wrong";
            check(!mode.ApplyCoopUtilityReceipt(receipt)&&mode.modeEHost.Refunds==0,"wrong asset cannot claim original refund");
            BossRushCoop.LocalAssetId="original";mode.modeEHost.Delivery=false;
            check(!mode.ApplyCoopUtilityReceipt(receipt)&&mode.coopUtilityRequests.Count==1,"unbound delivery failure retains original refund obligation");
            mode.modeEHost.Delivery=true;
            check(mode.ApplyCoopUtilityReceipt(receipt)&&mode.modeEHost.Refunds==1&&mode.coopUtilityRequests.Count==0,"original refund remains payable without a bound world");
            check(mode.ApplyCoopUtilityReceipt(receipt)&&mode.modeEHost.Refunds==1,"replayed original refund cannot double pay");
        }
    }
    [Serializable] internal sealed class SmokeWire
    {public string Id,Run,SceneToken;public int Scene;public UnityEngine.Vector3 Position;}
}
internal static class Program
{
    private static int checks;
    private static void Check(bool yes,string why) {checks++;if(!yes)throw new Exception(why);}
    private static void Main()
    {
        BossRushCoop.RunId="room";BossRushCoop.SceneGeneration=7;BossRushCoop.SceneToken="new-Core";
        BossRushCoop.HasBoundScene=false;
        var mode=new ModeERuntimeModule();string id=Guid.NewGuid().ToString("N");
        mode.Packet(id,"new-Core");Check(UnityEngine.Object.Instances==0,"unbound actual scene cannot instantiate world smoke");
        BossRushCoop.HasBoundScene=true;
        mode.Packet(id,"old-Core");Check(UnityEngine.Object.Instances==0,"old token at unchanged public generation cannot instantiate into new Core");
        mode.Packet(id,"new-Core");Check(UnityEngine.Object.Instances==1,"rejected stale packet cannot consume correct-token GUID");
        mode.Packet(id,"new-Core");Check(UnityEngine.Object.Instances==1,"matching smoke remains once per GUID");
        mode.Packet(Guid.NewGuid().ToString("N"),"new-Core",host:false);
        mode.Packet(Guid.NewGuid().ToString("N"),"new-Core",run:"other");
        mode.Packet(Guid.NewGuid().ToString("N"),"new-Core",scene:6);
        Check(UnityEngine.Object.Instances==1,"existing native host/room/generation validation remains active");
        BossRushCoop.IsHost=true;BossRushCoop.HasBoundScene=false;mode.Publish();
        Check(BossRushCoop.BroadcastJson==null,"unbound host cannot publish smoke");
        BossRushCoop.HasBoundScene=true;mode.Publish();
        var packet=BossRushCoopJson.FromJson<SmokeWire>(BossRushCoop.BroadcastJson);
        Check(packet!=null&&packet.SceneToken=="new-Core"&&packet.Scene==7,"production publish preserves actual token through real codec");
        ModeERuntimeModule.RefundCases(Check);
        Console.WriteLine("PASS ModeE smoke: "+checks+" production assertions; actual receiver/publisher/Instantiate call and original refund method");
    }
}

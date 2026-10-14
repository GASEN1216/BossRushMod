using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
namespace UnityEngine { internal static class Mathf { internal static float Max(float a,float b) { return Math.Max(a,b); } } }
internal sealed class CharacterMainControl { internal int Team; internal bool isBossCharacter; }
internal sealed class Health { internal CharacterMainControl Character; internal CharacterMainControl TryGetCharacter() { return Character; } }
internal struct DamageInfo { internal float finalDamage; internal CharacterMainControl fromCharacter; }
internal static class Team { internal static bool IsEnemy(int a,int b) { return a!=b; } }
namespace BossRush
{
    internal class BossRushRuntimeModuleBase { public virtual string ModuleName { get { return ""; } } public virtual void OnAwake(ModBehaviour owner) { } public virtual void OnUpdate(float d,float u) { } public virtual void OnDestroy() { } }
    internal sealed class ModBehaviour { }
    internal sealed class CoopWaylayerEscrow:IDisposable { internal void Tick(float delta) { } public void Dispose() { } }
    internal sealed class BossRushCoopPlayer { internal string Id,AssetId; internal bool IsConnected=true; internal CharacterMainControl Character; }
    internal sealed class BossRushCoopMessage { internal string SenderId; internal object Payload; internal T Read<T>() { return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Payload)); } }
    internal static class BossRushCoop
    {
        internal static bool IsHost=true,IsConnected=true; internal static bool IsClient { get { return !IsHost; } }
        internal static string RunId="room",LocalAssetId="local"; internal static int SceneGeneration;
        internal static List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();
        internal static Action<BossRushCoopMessage> Handler; internal static object Sent;
        private sealed class Subscription:IDisposable { public void Dispose() { Handler=null; } }
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> handler) { Handler=handler;return new Subscription(); }
        internal static void SendToHost<T>(string topic,T data) { Sent=data; }
        internal static BossRushCoopPlayer FindPlayer(string id) { return Players.Find(p=>p.Id==id); }
        internal static string GetPlayerId(CharacterMainControl c) { var player=Players.Find(p=>ReferenceEquals(p.Character,c));return player==null?null:player.Id; }
    }
    internal sealed class CoopPersonalDelivery<T>:IDisposable where T:class
    {
        internal sealed class Receipt { internal string Player,Asset; internal T Payload; }
        internal static readonly List<Receipt> Receipts=new List<Receipt>(); internal static bool Reject;
        internal static Func<T,bool> Apply;
        internal CoopPersonalDelivery(string name,Func<T,bool> apply) { Apply=apply; }
        internal bool Deliver(string player,string id,T payload,string asset=null) { if(Reject)return false;Receipts.Add(new Receipt { Player=player,Asset=asset??BossRushCoop.FindPlayer(player).AssetId,Payload=payload });return true; }
        internal void Tick(float d) { } public void Dispose() { }
    }
    internal enum BackMountainFacility { Garden }
    internal static class BackMountainUnlocks { internal static bool IsFacilityUnlocked(BackMountainFacility facility) { return false; } }
    internal static class DailyReportStatsCollector { internal static bool IsSubscribed=true; }
    internal static class DailyReportPersistence { internal static bool HasWriteBarrier,IsStoreFaulted; }
    internal static class DailyReportService
    {
        internal static int Kills,BossKills,Deaths; internal static float Dealt,Taken,Peak;
        internal static void ReportKill(bool boss) { Kills++;if(boss)BossKills++; } internal static void ReportPlayerDeath() { Deaths++; }
        internal static void ReportCoopDamageDealt(float d,float p) { Dealt+=d;Peak=Math.Max(Peak,p); } internal static void ReportDamageTaken(float d) { Taken+=d; }
    }
    internal static class CodexKillCollector { internal static bool ApplyCoopKill(string key,float seconds,string mode,string scene,long ticks=0) { return true; } }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string reason) { if(!value)throw new Exception(reason);checks++; }
    private static BossRushCoopPlayer Add(string id,string asset)
    { var player=new BossRushCoopPlayer { Id=id,AssetId=asset,Character=new CharacterMainControl { Team=1 } };BossRushCoop.Players.Add(player);return player; }
    private static void Hit(BossRushCoopPlayer player,float amount)
    { CoopPersonalProgressRuntimeModule.Hurt(new Health { Character=new CharacterMainControl { Team=2 } },new DamageInfo { fromCharacter=player.Character,finalDamage=amount }); }
    private static void Garden(string id,string asset)
    {
        Type type=typeof(CoopPersonalProgressRuntimeModule).GetNestedType("GardenEligibility",BindingFlags.NonPublic);
        object data=Activator.CreateInstance(type);type.GetField("Run").SetValue(data,BossRushCoop.RunId);type.GetField("Asset").SetValue(data,asset);type.GetField("Unlocked").SetValue(data,true);
        BossRushCoop.Handler(new BossRushCoopMessage { SenderId=id,Payload=data });
    }
    private static void Main()
    {
        var module=new CoopPersonalProgressRuntimeModule();module.OnAwake(new ModBehaviour());module.OnUpdate(0,0);
        var apply=CoopPersonalDelivery<CoopPersonalProgressRuntimeModule.Report>.Apply;
        var report=new CoopPersonalProgressRuntimeModule.Report { Kills=3,BossKills=2,Deaths=1,Dealt=17,Taken=5,Peak=9 };
        DailyReportPersistence.HasWriteBarrier=true;
        Check(!apply(report)&&DailyReportService.Kills==0,"personal write barrier defers complete report without partial counts");
        DailyReportPersistence.HasWriteBarrier=false;DailyReportPersistence.IsStoreFaulted=true;
        Check(!apply(report)&&DailyReportService.Kills==0,"personal store fault defers complete report");
        DailyReportPersistence.IsStoreFaulted=false;
        Check(apply(report)&&DailyReportService.Kills==3&&DailyReportService.BossKills==2&&DailyReportService.Deaths==1&&DailyReportService.Dealt==17&&DailyReportService.Taken==5&&DailyReportService.Peak==9,"production report application forwards exact personal facts");
        var receipts=CoopPersonalDelivery<CoopPersonalProgressRuntimeModule.Report>.Receipts;
        var peer=Add("same-peer","slot-a");Hit(peer,17);peer.AssetId="slot-b";Hit(peer,9);module.OnUpdate(1,1);
        Check(receipts.Count==2,"both slots retain distinct accumulated reports");
        Check(receipts[0].Asset=="slot-a"&&receipts[0].Payload.Dealt==17,"old-slot damage keeps its original asset identity");
        Check(receipts[1].Asset=="slot-b"&&receipts[1].Payload.Dealt==9,"new-slot damage is independent");
        receipts.Clear();
        for(int cycle=0;cycle<4;cycle++)
        {
            for(int i=0;i<16;i++)Hit(Add("member-"+cycle+"-"+i,"asset-"+cycle+"-"+i),i+1);
            module.OnUpdate(1,1);
        }
        Check(receipts.Count==64,"successive full-room cohorts do not exhaust a fixed participant array");
        for(int i=0;i<receipts.Count;i++)Check(receipts[i].Payload.Dealt==i%16+1,"each participant retains independent statistics");
        receipts.Clear();CoopPersonalDelivery<CoopPersonalProgressRuntimeModule.Report>.Reject=true;Hit(peer,5);module.OnUpdate(1,1);
        Check(receipts.Count==0,"failed enqueue is not counted as delivery");peer.AssetId="slot-c";
        CoopPersonalDelivery<CoopPersonalProgressRuntimeModule.Report>.Reject=false;module.OnUpdate(1,1);
        Check(receipts.Count==1&&receipts[0].Asset=="slot-b"&&receipts[0].Payload.Dealt==5,"retry preserves original statistics and slot");
        Garden(peer.Id,"slot-b");Check(!CoopPersonalProgressRuntimeModule.AnyGardenUnlocked,"stale-slot garden state rejected");
        Garden(peer.Id,"slot-c");Check(CoopPersonalProgressRuntimeModule.AnyGardenUnlocked,"eligible current participant permits seed drops");
        peer.AssetId="slot-d";Check(!CoopPersonalProgressRuntimeModule.AnyGardenUnlocked,"switching slots expires cached eligibility immediately");
        receipts.Clear();Hit(peer,13);BossRushCoop.RunId="next-room";module.OnUpdate(1,1);
        Check(receipts.Count==0,"unflushed previous-room statistics cannot enter next room");
        module.OnDestroy();Check(CoopPersonalProgressRuntimeModule.Current==null&&BossRushCoop.Handler==null,"runtime tears down owner and subscription");
        Console.WriteLine("CoopPersonalProgress: PASS "+checks+" production assertions");
    }
}

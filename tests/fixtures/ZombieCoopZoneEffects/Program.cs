using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BossRush;
using UnityEngine;
using EscapeFromDuckovCoopMod;

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static bool operator ==(Object a,Object b){bool x=ReferenceEquals(a,null)||a.destroyed,y=ReferenceEquals(b,null)||b.destroyed;return x||y?x==y:ReferenceEquals(a,b);}
        public static bool operator !=(Object a,Object b){return !(a==b);}public static implicit operator bool(Object a){return a!=null;}
        public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return RuntimeHelpers.GetHashCode(this);}
        public static void Destroy(GameObject value){value.destroyed=true;foreach(Component component in value.Components)component.destroyed=true;}
    }
    public class Component:Object
    {
        public GameObject gameObject;public Transform transform{get{return gameObject.transform;}}
        public Component(){gameObject=new GameObject();gameObject.Components.Add(this);}
        public T GetComponentInChildren<T>() where T:Component{return gameObject.GetComponent<T>();}
    }
    public class MonoBehaviour:Component { public bool enabled=true; public bool isActiveAndEnabled { get { return this!=null&&enabled&&gameObject!=null&&gameObject.Active; } } }
    public sealed class GameObject:Object
    {
        internal readonly List<Component> Components=new List<Component>();public readonly Transform transform=new Transform();public bool Active=true;
        public T GetComponent<T>() where T:Component{return Components.OfType<T>().FirstOrDefault();}
        public T AddComponent<T>() where T:Component,new(){var value=new T();value.gameObject=this;Components.Add(value);return value;}
    }
    public sealed class Transform{public Vector3 position;}
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 up{get{return new Vector3(0,1,0);}}public static Vector3 forward{get{return new Vector3(0,0,1);}}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}public Vector3 normalized{get{float n=(float)Math.Sqrt(sqrMagnitude);return new Vector3(x/n,y/n,z/n);}}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
    }
    public static class Mathf{public static float Max(float a,float b){return Math.Max(a,b);}public static float Min(float a,float b){return Math.Min(a,b);}}
    public static class Time{public static float unscaledTime{get{return BossRushCoop.Current.Now;}}}
}
namespace Duckov.Buffs{public sealed class Buff:UnityEngine.Object{public int ID{get;set;}}}
public enum DamageTypes{normal}
public struct ElementFactor{public int Type;public float Factor;}
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;public DamageReceiver toDamageReceiver;public DamageTypes damageType;
    public float damageValue,armorPiercing,critDamageFactor,critRate,bleedChance,buffChance,damageFactorToZombie,armorBreak;
    public int crit,fromWeaponItemID;public bool isExplosion,isFromBuffOrEffect,ignoreArmor,ignoreDifficulty;
    public Vector3 damagePoint,damageNormal;public Duckov.Buffs.Buff buff;public List<ElementFactor> elementFactors;
    public DamageInfo(CharacterMainControl source){this=default(DamageInfo);fromCharacter=source;elementFactors=new List<ElementFactor>();}
}
public sealed class Health:Component{public CharacterMainControl Owner;public float CurrentHealth{get;set;}=100;public bool IsDead{get{return CurrentHealth<=0;}}public CharacterMainControl TryGetCharacter(){return Owner;}}
public sealed class DamageReceiver:Component
{
    public Health health;public int Calls;public DamageInfo Last;
    public void Hurt(DamageInfo info){Last=info;if(Patch_ServerForwardRemotePlayerDamage.Prefix(this,ref info)){Calls++;health.CurrentHealth-=info.damageValue;}}
}
public sealed class CharacterMainControl:Component
{
    public static CharacterMainControl Main{get{return BossRushCoop.Current.Body;}}public Health Health;public DamageReceiver mainDamageReceiver;
    public CharacterMainControl(){Health=new Health{Owner=this,gameObject=gameObject};mainDamageReceiver=new DamageReceiver{health=Health,gameObject=gameObject};gameObject.Components.Add(Health);gameObject.Components.Add(mainDamageReceiver);}
}
namespace BossRush
{
    internal enum ZombieModeCombatPhase{Combat,RewardSelection}
    internal static class ZombieModePhaseGuards{internal static bool ShouldPauseModePressure(ZombieModeCombatPhase p){return p==ZombieModeCombatPhase.RewardSelection;}}
    internal sealed class ZombieModeRunState{internal int RunId=11,CurrentWave=3;internal ZombieModeCombatPhase CombatPhase;}
    public sealed class ZombieModePlayerSlowRuntime:Component{public int Calls;public float Percent,Duration,Ends,MaxEnds;public void ApplySlow(int run,float percent,float duration){Calls++;Percent=percent;Duration=duration;Ends=Time.unscaledTime+duration;MaxEnds=Math.Max(MaxEnds,Ends);}}
    public sealed partial class ModBehaviour
    {
        internal ZombieModeRuntimeModule Module;
        private ZombieModeRuntimeModule zombieModeRuntimeModule { get { return Module; } }
        internal static ModBehaviour Instance { get { return BossRushCoop.Current.Module == null ? null : BossRushCoop.Current.Module.owner; } }
        internal int ZombieModeCurrentRunId { get { return Module.Invalid ? 0 : Module.runState.RunId; } }
        internal bool IsZombieModeRuntimePaused() { return Module.Paused; }
        internal void TryApplyZombieModePlayerSlow(int run,float percent,float duration){Module.TryApplyZombieModePlayerSlow(run,percent,duration);}
        internal void DealZombieModeRuntimeAreaDamageToPlayer(int run, CharacterMainControl source, Vector3 origin, float radius, float damage, Duckov.Buffs.Buff buff = null) { Module.DealZombieModeAreaDamageToPlayer(run,source,origin,radius,damage,buff); }
    }
    internal static class ZombieModeZoneVisuals
    {
        internal static void SetCountdown(GameObject value, float progress) { }
        internal static void FadeOutAndDestroy(GameObject value, MonoBehaviour runtime) { runtime.enabled=false; UnityEngine.Object.Destroy(value); }
    }
    internal sealed class BossRushCoopMessage{internal string SenderId,Json;internal bool IsFromHost;internal T Read<T>(){return BossRushCoopJson.FromJson<T>(Json);}}
    internal sealed class BossRushCoopPlayer
    {
        internal string Id,AssetId="slot-A";internal bool IsConnected=true;internal CharacterMainControl Character;
        internal bool IsLocal{get{return Id==BossRushCoop.LocalPlayerId;}}internal bool IsAlive{get{return IsConnected&&Character!=null&&Character.Health!=null&&!Character.Health.IsDead;}}
    }
    internal sealed class Peer
    {
        internal string Id,Asset="slot-A",Room="room",Token="scene";internal bool Host,Bound=true;internal int Generation=1;
        internal float Now, ClockOffset; internal ZombieModeRuntimeModule Module;
        internal readonly CharacterMainControl Body=new CharacterMainControl();internal readonly Dictionary<string,List<Action<BossRushCoopMessage>>> Handlers=new Dictionary<string,List<Action<BossRushCoopMessage>>>();
    }
    internal static class BossRushCoop
    {
        internal static Peer Current;internal static bool IsConnected=true,DropAck;internal static readonly List<Peer> Peers=new List<Peer>();internal static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();internal static readonly Queue<Action> Wire=new Queue<Action>();
        internal static bool IsHost{get{return Current.Host;}}internal static bool IsClient{get{return IsConnected&&!IsHost;}}internal static bool HasWorldAuthority{get{return !IsConnected||IsHost;}}internal static bool HasBoundScene{get{return Current.Bound;}}
        internal static string RunId{get{return Current.Room;}}internal static string SceneToken{get{return Current.Token;}}internal static string LocalPlayerId{get{return Current.Id;}}internal static string LocalAssetId{get{return Current.Asset;}}internal static int SceneGeneration{get{return Current.Generation;}}
        internal static string GetPlayerId(CharacterMainControl body){var p=Players.Find(x=>ReferenceEquals(x.Character,body));return p==null?null:p.Id;}
        internal static BossRushCoopPlayer FindPlayer(string id){return Players.Find(x=>x.Id==id);}
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback){List<Action<BossRushCoopMessage>> list;if(!Current.Handlers.TryGetValue(topic,out list))Current.Handlers.Add(topic,list=new List<Action<BossRushCoopMessage>>());list.Add(callback);return new Token(()=>list.Remove(callback));}
        internal static bool SendToHost<T>(string topic,T payload){return SendToPlayer(Peers[0].Id,topic,payload);}
        internal static bool TimedWire, DropNextOffer, DropNextProbe, DropNextReceipt;
        internal static float PhysicalTime, Uplink, Downlink;
        private sealed class Scheduled { internal float At; internal Action Action; }
        private static readonly List<Scheduled> ScheduledWire = new List<Scheduled>();
        internal static void Schedule(float at, Action action) { ScheduledWire.Add(new Scheduled { At=at, Action=action }); }
        internal static void Advance(float until)
        {
            while (ScheduledWire.Any(x=>x.At<=until))
            {
                Scheduled next=ScheduledWire.OrderBy(x=>x.At).First(); ScheduledWire.Remove(next);
                PhysicalTime=next.At; foreach(Peer peer in Peers) peer.Now=peer.ClockOffset+PhysicalTime;
                next.Action();
            }
            PhysicalTime=until; foreach(Peer peer in Peers) peer.Now=peer.ClockOffset+PhysicalTime;
        }
        internal static void ResetWire() { ScheduledWire.Clear(); TimedWire=false; DropNextOffer=DropNextProbe=DropNextReceipt=false; PhysicalTime=0; }
        internal static bool SendToPlayer<T>(string id,string topic,T payload)
        {
            if(DropAck&&topic.EndsWith(".ack"))return true;
            if(DropNextOffer&&topic=="BossRush.Zombie.ZoneOffer"){DropNextOffer=false;return true;}
            if(DropNextProbe&&topic=="BossRush.Zombie.ZoneProbe"){DropNextProbe=false;return true;}
            if(DropNextReceipt&&topic=="BossRush.Personal.ZombieZones"){DropNextReceipt=false;return true;}
            var target=Peers.Find(x=>x.Id==id);if(target==null)return false;var source=Current;string json=BossRushCoopJson.ToJson(payload);
            Action deliver=()=>{var saved=Current;Current=target;try{List<Action<BossRushCoopMessage>> list;if(target.Handlers.TryGetValue(topic,out list))foreach(var c in list.ToArray())c(new BossRushCoopMessage{SenderId=source.Id,IsFromHost=source.Host,Json=json});}finally{Current=saved;}};
            if(TimedWire) Schedule(PhysicalTime+(source.Host?Downlink:Uplink),deliver);else Wire.Enqueue(deliver);
            return true;
        }
        internal static void Flush(){while(Wire.Count>0)Wire.Dequeue()();}private sealed class Token:IDisposable{private Action action;internal Token(Action a){action=a;}public void Dispose(){action?.Invoke();action=null;}}
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal readonly ZombieModeRunState runState=new ZombieModeRunState();internal readonly ModBehaviour owner;
        private string coopZombieLocalAsset="slot-A";private readonly Dictionary<string,string> coopZombieParticipants=new Dictionary<string,string>();private readonly HashSet<string> coopZombieStarterReady=new HashSet<string>();internal bool Paused,Invalid;
        internal ZombieModeRuntimeModule(){owner=new ModBehaviour{Module=this};BossRushCoop.Current.Module=this;InitializeCoopZombieZoneEffects();}
        internal void Admit(BossRushCoopPlayer p){coopZombieParticipants[p.Id]=p.AssetId;coopZombieStarterReady.Add(CoopZombieParticipantKey(p));}
        internal void Tick(float deltaTime=2){TickCoopZombieZoneEffects(deltaTime);}internal void Reset(){ResetCoopZombieZoneEffects();}internal void Dispose(){CleanupCoopZombieZoneEffects();}
        internal int Facts{get{return coopZombieZoneFacts.Count;}}internal int Pending{get{return coopZombieZonePending.Count;}}
        private bool IsZombieModeRunValid(int run){return !Invalid&&run==runState.RunId;}private bool IsZombieModeRuntimePaused(){return Paused;}
        private float GetZombieModeRuntimeNow(){return Time.unscaledTime;}
    }
}
internal static partial class Program
{
    private static int checks;private static Peer host,guest;private static ZombieModeRuntimeModule h,g;private static BossRushCoopPlayer hp,gp;
    internal static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    private static ZombieModePlayerSlowRuntime Slow(Peer p){return p.Body.gameObject.GetComponent<ZombieModePlayerSlowRuntime>();}
    private static void ConfirmInFlight(){BossRushCoop.Wire.Dequeue()();BossRushCoop.Wire.Dequeue()();}
    private static void Setup()
    {
        BossRushCoop.ResetWire();BossRushCoop.Peers.Clear();BossRushCoop.Players.Clear();BossRushCoop.Wire.Clear();CoopTool.Forwards.Clear();BossRushCoop.DropAck=false;BossRushCoop.IsConnected=true;
        host=new Peer{Id="host",Host=true};guest=new Peer{Id="guest"};BossRushCoop.Peers.Add(host);BossRushCoop.Peers.Add(guest);
        hp=new BossRushCoopPlayer{Id="host",Character=host.Body};gp=new BossRushCoopPlayer{Id="guest",Character=guest.Body};BossRushCoop.Players.Add(hp);BossRushCoop.Players.Add(gp);
        guest.Body.gameObject.AddComponent<RemoteReplicaTag>();guest.Body.transform.position=new Vector3(1,0,0);
        BossRushCoop.Current=host;h=new ZombieModeRuntimeModule();h.Admit(hp);h.Admit(gp);BossRushCoop.Current=guest;g=new ZombieModeRuntimeModule();g.Admit(hp);g.Admit(gp);BossRushCoop.Current=host;
    }
    private static void Damage()
    {
        Setup();var enemy=new CharacterMainControl();var buff=new Duckov.Buffs.Buff{ID=47};
        h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);
        Check(host.Body.mainDamageReceiver.Calls==1&&CoopTool.Forwards.Count==1&&guest.Body.mainDamageReceiver.Calls==0,"real native prefix applies host locally and forwards exactly one guest hit without proxy health damage");
        var rpc=CoopTool.Forwards[0];Check(rpc.PlayerId=="guest"&&rpc.Damage.DamageValue==4&&rpc.Damage.BuffId==47&&rpc.Damage.BuffChance==1,"real native payload retains actual recipient, damage and official poison Buff");
        var restored=rpc.Damage.ToDamageInfo(enemy,guest.Body.mainDamageReceiver,buff);Check(restored.fromCharacter==enemy&&restored.buff==buff&&restored.buffChance==1&&restored.damageValue==4,"real native ToDamageInfo round trip preserves source seam, poison and damage");
        int n=CoopTool.Forwards.Count;h.DealZombieModeAreaDamageToPlayer(12,enemy,new Vector3(),4,4,buff);h.Paused=true;h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);h.Paused=false;
        BossRushCoop.Current=guest;g.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);BossRushCoop.Current=host;host.Bound=false;h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);host.Bound=true;
        Check(CoopTool.Forwards.Count==n&&host.Body.mainDamageReceiver.Calls==1,"stale run, pause, client and unbound scene cannot settle zone damage");
        gp.AssetId="slot-B";h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);gp.AssetId="slot-A";guest.Body.transform.position=new Vector3(8,0,0);h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);guest.Body.transform.position=new Vector3(1,0,0);guest.Body.Health.CurrentHealth=0;h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);guest.Body.Health.CurrentHealth=100;
        Check(CoopTool.Forwards.Count==n,"changed asset, outside area and dead guest cannot receive zone damage");
        var outsider=new BossRushCoopPlayer{Id="outsider",Character=new CharacterMainControl()};outsider.Character.gameObject.AddComponent<RemoteReplicaTag>();BossRushCoop.Players.Add(outsider);h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);
        Check(CoopTool.Forwards.Count==n+1&&CoopTool.Forwards.Last().PlayerId=="guest","unadmitted player cannot be hit even when alive inside a zone");
        NetService.Instance.Invincible=true;h.DealZombieModeAreaDamageToPlayer(11,enemy,new Vector3(),4,4,buff);Check(CoopTool.Forwards.Count==n+1,"real installed prefix retains remote player invincibility");NetService.Instance.Invincible=false;
        host.Body.Health.CurrentHealth=0;h.DealZombieModeAreaDamageToPlayer(11,new Vector3(),4,4);Check(CoopTool.Forwards.Count==n+2&&CoopTool.Forwards.Last().Damage.IsFromBuffOrEffect,"dead host still settles guest hazards and null-source effects remain marked");
        h.Dispose();BossRushCoop.Current=guest;g.Dispose();BossRushCoop.Current=host;
    }
    private static void SlowEffects()
    {
        Setup();BossRushCoop.DropAck=true;h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);BossRushCoop.Flush();
        Check(Slow(host).Calls==1&&Slow(guest).Calls==1&&Slow(guest).Percent==0.3f&&Slow(guest).Duration==2,"production area, receipt and local runtime give both participants the original slow");
        h.Tick();BossRushCoop.Flush();Check(Slow(guest).Calls==1,"lost ACK retry cannot extend slow duration or apply twice");BossRushCoop.DropAck=false;h.Tick();BossRushCoop.Flush();
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);guest.Body.transform.position=new Vector3(8,0,0);BossRushCoop.Flush();Check(Slow(guest).Calls==1,"guest leaving the visible area before delivery consumes old effect without chasing it");guest.Body.transform.position=new Vector3(1,0,0);
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();g.runState.CurrentWave++;BossRushCoop.Flush();Check(Slow(guest).Calls==1,"prior wave in-flight receipt cannot slow the next wave");g.runState.CurrentWave--;
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();guest.Asset="slot-B";BossRushCoop.Flush();Check(Slow(guest).Calls==1,"prior asset in-flight receipt cannot slow the switched save");guest.Asset="slot-A";h.Tick();BossRushCoop.Flush();Check(Slow(guest).Calls==2,"same fight can resume its one pending effect on the original asset");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();guest.Generation++;BossRushCoop.Flush();guest.Generation--;Check(Slow(guest).Calls==2,"different scene generation discards in-flight effect");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();guest.Token="other";BossRushCoop.Flush();guest.Token="scene";Check(Slow(guest).Calls==2,"different scene token discards in-flight effect");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();g.runState.RunId++;BossRushCoop.Flush();g.runState.RunId--;Check(Slow(guest).Calls==2,"different run discards in-flight effect");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();guest.Body.Health.CurrentHealth=0;BossRushCoop.Flush();guest.Body.Health.CurrentHealth=100;Check(Slow(guest).Calls==2,"dead guest consumes pending slow without adding modifiers");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();g.runState.CombatPhase=ZombieModeCombatPhase.RewardSelection;BossRushCoop.Flush();g.runState.CombatPhase=ZombieModeCombatPhase.Combat;Check(Slow(guest).Calls==2,"reward phase discards combat pressure receipt");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();g.Paused=true;BossRushCoop.Flush();Check(Slow(guest).Calls==2,"paused client holds effect");g.Paused=false;h.Tick();BossRushCoop.Flush();Check(Slow(guest).Calls==3,"resumed valid fight applies held effect once");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();g.Invalid=true;BossRushCoop.Flush();g.Invalid=false;Check(Slow(guest).Calls==3,"cleanup/invalid run consumes in-flight slow");
        BossRushCoop.Current=guest;int local=Slow(guest).Calls;g.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);Check(Slow(guest).Calls==local,"replica zone tick cannot create client authority");
        UnityEngine.Object.Destroy(guest.Body.gameObject);BossRushCoop.Current=host;h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);BossRushCoop.Flush();Check(guest.Body==null&&Slow(guest).Calls==local,"destroyed character and components cannot receive another effect");
        h.Dispose();BossRushCoop.Current=guest;g.Dispose();Check(host.Handlers.Values.All(x=>x.Count==0)&&guest.Handlers.Values.All(x=>x.Count==0),"cleanup unsubscribes both receipt and ACK owners");
        Setup();BossRushCoop.IsConnected=false;h.DealZombieModeAreaDamageToPlayer(11,new Vector3(),4,3);h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.2f,1);
        Check(host.Body.mainDamageReceiver.Calls==1&&Slow(host).Calls==1&&CoopTool.Forwards.Count==0,"offline path retains original main-player damage and slow");
    }
    private static void ShortLivedFacts()
    {
        Setup();guest.Now=10000;h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);host.Now=3;BossRushCoop.Flush();
        Check(Slow(guest)==null&&h.Facts==1&&g.Pending==1,"late first offer cannot resurrect an expired host zone tick even with unrelated local clocks");h.Tick();BossRushCoop.Current=guest;guest.Now+=3;g.Tick();BossRushCoop.Current=host;
        Check(h.Facts==0&&g.Pending==0,"expired host facts and unanswered client nonces are reclaimed by time");
        h.Dispose();BossRushCoop.Current=guest;g.Dispose();Setup();guest.Now=20000;h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);ConfirmInFlight();guest.Now+=3;BossRushCoop.Flush();
        Check(Slow(guest)==null&&h.Facts==0&&g.Pending==0,"late final reliable receipt is ACK discarded after local full RTT exceeds remaining host lifetime");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);BossRushCoop.Wire.Dequeue()();guest.Now+=0.4f;host.Now+=0.2f;BossRushCoop.Wire.Dequeue()();guest.Now+=0.2f;BossRushCoop.Flush();
        Check(Slow(guest).Calls==1&&Slow(guest).Duration>1.19f&&Slow(guest).Duration<1.21f,"confirmation duration subtracts measured full RTT from current host remainder without clock synchronization");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);BossRushCoop.Wire.Dequeue()();Check(g.Pending==1&&h.Facts==1,"waiting offer owns one bounded nonce and host fact");
        h.runState.CurrentWave++;h.Tick();BossRushCoop.Current=guest;g.runState.CurrentWave++;g.Tick();BossRushCoop.Flush();BossRushCoop.Current=host;
        Check(h.Facts==0&&g.Pending==0,"wave reset releases previous offers, confirmations, retries and nonce state");
        h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);BossRushCoop.Flush();int before=Slow(guest).Calls;
        h.runState.CurrentWave++;h.TryApplyZombieModePlayerSlowInArea(11,new Vector3(),4,0.3f,2);BossRushCoop.Flush();Check(g.Pending==1&&Slow(guest).Calls==before,"offer can arrive before its world wave snapshot without applying early");
        BossRushCoop.Current=guest;g.runState.CurrentWave++;g.Tick();BossRushCoop.Flush();BossRushCoop.Current=host;Check(Slow(guest).Calls==before+1&&g.Pending==0,"world catch-up preserves current-wave offer and confirms its still-live host fact");
        h.Dispose();BossRushCoop.Current=guest;g.Dispose();
    }
    public static int Main(string[] args){try{NativeDamage.Install(args);Damage();SlowEffects();ShortLivedFacts();PersistentZones();Console.WriteLine("ZombieCoopZoneEffects PASS "+checks+" assertions; complete current native damage prefix/payload IL executed; no game launched.");return 0;}catch(Exception e){for(;e!=null;e=e.InnerException){Console.WriteLine(e.GetType().FullName);try{Console.WriteLine(e.Message);Console.WriteLine(e.StackTrace);}catch{Console.WriteLine("native diagnostic message unavailable");}}return 1;}}
}

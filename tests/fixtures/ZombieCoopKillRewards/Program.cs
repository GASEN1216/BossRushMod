using System;
using System.Collections;
using System.Collections.Generic;
using BossRush;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 up {get{return new Vector3(0,1,0);}} public static Vector3 forward {get{return new Vector3(0,0,1);}}
        public float sqrMagnitude {get{return x*x+y*y+z*z;}}public Vector3 normalized {get{float n=(float)Math.Sqrt(sqrMagnitude);return new Vector3(x/n,y/n,z/n);}}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
    }
    public struct Quaternion {private float y;public static Quaternion Euler(float x,float y,float z){return new Quaternion{y=y};}public static Vector3 operator *(Quaternion a,Vector3 b){double r=a.y*Math.PI/180;return new Vector3((float)(b.x*Math.Cos(r)+b.z*Math.Sin(r)),b.y,(float)(b.z*Math.Cos(r)-b.x*Math.Sin(r)));}}
    public sealed class Transform {public Vector3 position,forward=Vector3.forward;}
    public sealed class Coroutine {internal IEnumerator Value;}
    public static class Random {public static float value=0;}
    public static class Mathf {public static int Max(int a,int b){return Math.Max(a,b);}public static float Max(float a,float b){return Math.Max(a,b);}public static int Min(int a,int b){return Math.Min(a,b);}public static float Min(float a,float b){return Math.Min(a,b);}public static int FloorToInt(float a){return (int)Math.Floor(a);}public static int Clamp(int a,int min,int max){return Math.Max(min,Math.Min(max,a));}}
}
namespace Duckov.Utilities {public enum ExplosionFxTypes{normal}}
public sealed class Health {public CharacterMainControl Owner;public bool IsDead;public float Value=20;public int Heals;public void AddHealth(float value){Value+=value;Heals++;}public CharacterMainControl TryGetCharacter(){return Owner;}}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main {get{return BossRushCoop.Current.Body;}}
    public Health Health;public readonly Transform transform=new Transform();public bool IsMainCharacter {get{return ReferenceEquals(this,Main);}}
    public CharacterMainControl(){Health=new Health{Owner=this};}
}
public struct DamageInfo {public CharacterMainControl fromCharacter;public bool isFromBuffOrEffect,isExplosion;public float damageValue;public Vector3 damagePoint,damageNormal;public DamageInfo(CharacterMainControl actor){fromCharacter=actor;isFromBuffOrEffect=isExplosion=false;damageValue=0;damagePoint=damageNormal=new Vector3();}}
public sealed class ExplosionManager {public readonly List<DamageInfo> Hits=new List<DamageInfo>();public void CreateExplosion(Vector3 point,float radius,DamageInfo info,Duckov.Utilities.ExplosionFxTypes fx,float scale,bool hurtSelf){Hits.Add(info);}}
public sealed class LevelManager {public static LevelManager Instance=new LevelManager();public readonly ExplosionManager ExplosionManager=new ExplosionManager();}
namespace BossRush
{
    internal enum ZombieModeRewardType {TriggerPurificationSiphon,TriggerSecondWind,TriggerDoomPulse,Other}
    internal enum ZombieModeLifecyclePhase {None,Active}
    internal enum ZombieModeCombatPhase {Combat,RewardSelection}
    internal enum ZombieModeEnemyKind {Normal,Elite,Special}
    internal enum ZombieModeRunOnlyObjectKind {Coroutine}
    internal sealed class ZombieModeRunOnlyRecord {internal int RunId;internal ZombieModeRunOnlyObjectKind Kind;internal Action CleanupAction;}
    internal sealed class ZombieModeOptionRuntimeState {internal int TriggerLifestealChancePercent,TriggerLifestealHealAmount,TriggerPurificationSiphonStacks,TriggerSecondWindStacks,TriggerDoomPulseStacks,DoomPulseKillCounter;}
    internal sealed class ZombieModeEnemyRuntimeMarker {internal int RunId=11,PurificationPointValue=10;internal bool IsBoss,DeathSettled,RemovedFromRuntime;internal ZombieModeEnemyKind EnemyKind;}
    internal sealed class ZombieModeRunState {internal int RunId=11,CurrentWave=5,LivingZombieCount=1000,LivingNormalZombieCount=1000,CurrentWaveKills,CurrentWaveKillTarget=10000,CurrentWaveBossesRemaining=100;internal ZombieModeLifecyclePhase LifecyclePhase=ZombieModeLifecyclePhase.Active;internal ZombieModeCombatPhase CombatPhase;internal readonly ZombieModeOptionRuntimeState OptionRuntime=new ZombieModeOptionRuntimeState();internal readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects=new List<ZombieModeRunOnlyRecord>();}
    internal sealed class ModBehaviour {internal readonly List<Coroutine> Coroutines=new List<Coroutine>();internal Coroutine StartCoroutine(IEnumerator value){var c=new Coroutine{Value=value};Coroutines.Add(c);value.MoveNext();return c;}internal void StopCoroutine(Coroutine c){(c.Value as IDisposable)?.Dispose();Coroutines.Remove(c);}internal void Advance(){foreach(var c in Coroutines.ToArray())if(!c.Value.MoveNext())Coroutines.Remove(c);}internal static void DevLog(string text){} }
    internal sealed class BossRushCoopMessage {internal string SenderId,Json;internal bool IsFromHost;internal T Read<T>(){return BossRushCoopJson.FromJson<T>(Json);}}
    internal sealed class BossRushCoopPlayer {internal string Id,AssetId="slot-A";internal bool IsConnected=true;internal CharacterMainControl Character=new CharacterMainControl();internal bool IsLocal{get{return Id==BossRushCoop.LocalPlayerId;}}internal bool IsAlive{get{return IsConnected&&Character!=null&&!Character.Health.IsDead;}}}
    internal sealed class Peer {internal string Id,Asset="slot-A",Room="room-A",Token="scene-A";internal bool Host,Ready=true,Bound=true;internal int Generation=1;internal readonly CharacterMainControl Body=new CharacterMainControl();internal readonly Dictionary<string,List<Action<BossRushCoopMessage>>> Handlers=new Dictionary<string,List<Action<BossRushCoopMessage>>>();}
    internal static class BossRushCoop
    {
        internal static Peer Current;internal static readonly List<Peer> Peers=new List<Peer>();internal static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();internal static readonly Queue<Action> Wire=new Queue<Action>();internal static bool DropAck;internal static bool IsConnected=true;
        internal static bool IsHost{get{return Current.Host;}}internal static bool IsClient{get{return !Current.Host;}}internal static bool HasWorldAuthority{get{return !IsConnected||IsHost;}}internal static bool HasBoundScene{get{return Current.Bound;}}internal static string RunId{get{return Current.Room;}}internal static string SceneToken{get{return Current.Token;}}internal static string LocalPlayerId{get{return Current.Id;}}internal static string LocalAssetId{get{return Current.Asset;}}internal static int SceneGeneration{get{return Current.Generation;}}internal static bool AnyPlayerAlive{get{return Players.Exists(p=>p.IsAlive);}}
        internal static BossRushCoopPlayer FindPlayer(string id){return Players.Find(p=>p.Id==id);}internal static string GetPlayerId(CharacterMainControl actor){var p=Players.Find(x=>ReferenceEquals(x.Character,actor));return p==null?null:p.Id;}internal static bool IsPlayer(CharacterMainControl actor){return GetPlayerId(actor)!=null;}
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback){List<Action<BossRushCoopMessage>> list;if(!Current.Handlers.TryGetValue(topic,out list))Current.Handlers.Add(topic,list=new List<Action<BossRushCoopMessage>>());list.Add(callback);return new Token(()=>list.Remove(callback));}
        internal static bool SendToHost<T>(string topic,T payload){return Current.Ready&&SendToPlayer(Peers[0].Id,topic,payload);}
        internal static bool SendToPlayer<T>(string id,string topic,T payload){var target=Peers.Find(p=>p.Id==id);if(target==null)return false;if(DropAck&&topic.EndsWith(".ack"))return true;var source=Current;string json=BossRushCoopJson.ToJson(payload);Wire.Enqueue(()=>{var saved=Current;Current=target;try{List<Action<BossRushCoopMessage>> list;if(target.Handlers.TryGetValue(topic,out list))foreach(var c in list.ToArray())c(new BossRushCoopMessage{SenderId=source.Id,IsFromHost=source.Host,Json=json});}finally{Current=saved;}});return true;}
        internal static void Flush(){while(Wire.Count>0)Wire.Dequeue()();}private sealed class Token:IDisposable{private Action action;internal Token(Action a){action=a;}public void Dispose(){action?.Invoke();action=null;}}
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal readonly ZombieModeRunState runState=new ZombieModeRunState();internal readonly ModBehaviour owner=new ModBehaviour();internal readonly Dictionary<string,string> coopZombieParticipants=new Dictionary<string,string>();private readonly Dictionary<CharacterMainControl,ZombieModeEnemyRuntimeMarker> markers=new Dictionary<CharacterMainControl,ZombieModeEnemyRuntimeMarker>();internal int Stars,Pulses,LocalDeaths;internal bool Paused;private const int ZombieModeLifestealChanceCapPercent=100;
        internal void Initialize(){InitializeCoopZombieKillRewards();}internal void Reset(){ResetCoopZombieKillRewards();}internal void Dispose(){CleanupCoopZombieKillRewards();}internal void Tick(){TickCoopZombieKillRewards(2);}internal void Select(BossRushCoopPlayer p,ZombieModeRewardType r){RecordCoopZombieKillRewardSelection(p,r);}internal bool Receipt(CoopZombieKillReceipt r){return ApplyCoopZombieKillReceipt(r);}internal void Kill(CharacterMainControl victim,DamageInfo info){HandleZombieModeHealthDead(runState.RunId,victim.Health,info);}internal CharacterMainControl Enemy(){var enemy=new CharacterMainControl();markers.Add(enemy,new ZombieModeEnemyRuntimeMarker{RunId=runState.RunId});return enemy;}
        private bool IsZombieModeRunValid(int run){return run==runState.RunId&&runState.LifecyclePhase!=ZombieModeLifecyclePhase.None;}private bool IsCoopZombieLocalAssetCurrent(){string asset;return coopZombieParticipants.TryGetValue(BossRushCoop.LocalPlayerId,out asset)&&asset==BossRushCoop.LocalAssetId;}private bool IsCoopZombieParticipant(BossRushCoopPlayer p){string a;return p.IsConnected&&coopZombieParticipants.TryGetValue(p.Id,out a)&&a==p.AssetId;}private static string CoopZombieParticipantKey(BossRushCoopPlayer p){return p.Id+":"+p.AssetId;}
        private bool IsZombieModeRuntimePaused(){return Paused;}private bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl actor,out ZombieModeEnemyRuntimeMarker marker){return markers.TryGetValue(actor,out marker);}private void UnregisterZombieModeEnemyInstanceId(CharacterMainControl actor){markers.Remove(actor);}private void TryHandleZombieModeSafeZonePlayerAttack(int run,DamageInfo info,CharacterMainControl victim){}private void SpawnZombieModeDeathStars(int run,Vector3 point,int value,int count){Stars+=value;}private int GetZombieModeDeathStarCount(ZombieModeEnemyRuntimeMarker marker){return 1;}private void FailZombieModeActive(int run){}private void HandleZombieModeBossDefeated(int run,ZombieModeEnemyRuntimeMarker marker,CharacterMainControl actor){}private void HandleZombieModeBossDeathEffects(int run,ZombieModeEnemyRuntimeMarker marker,CharacterMainControl actor){}private void TrySpawnZombieModeBossDrop(int run,ZombieModeEnemyRuntimeMarker marker,Vector3 p){}private void CompleteZombieModeWave(int run){}private void PruneZombieModeRunOnlyEnemyRecords(int run){}private void HandleZombieModeEliteDeathEffects(int run,ZombieModeEnemyRuntimeMarker marker,CharacterMainControl actor){}private void HandleZombieModeSpecialDeathEffects(int run,ZombieModeEnemyRuntimeMarker marker,CharacterMainControl actor){}private void TrySpawnZombieModeEnemyDrop(int run,ZombieModeEnemyRuntimeMarker marker,Vector3 p){}private void TriggerZombieModeDoomPulse(int run,int stacks){Pulses++;}
    }
}
internal static class Program
{
    private static int checks;private static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    private static void Main()
    {
        var host=new Peer{Id="host",Host=true};var guest=new Peer{Id="guest"};BossRushCoop.Peers.Add(host);BossRushCoop.Peers.Add(guest);BossRushCoop.Current=host;
        var hp=new BossRushCoopPlayer{Id=host.Id,Character=host.Body};var gp=new BossRushCoopPlayer{Id=guest.Id,Character=guest.Body};BossRushCoop.Players.Add(hp);BossRushCoop.Players.Add(gp);
        var server=new ZombieModeRuntimeModule();server.coopZombieParticipants.Add(host.Id,host.Asset);server.coopZombieParticipants.Add(guest.Id,guest.Asset);server.Initialize();
        BossRushCoop.Current=guest;var client=new ZombieModeRuntimeModule();client.coopZombieParticipants.Add(guest.Id,guest.Asset);client.Initialize();BossRushCoop.Current=host;
        server.Select(gp,ZombieModeRewardType.TriggerSecondWind);server.Select(gp,ZombieModeRewardType.TriggerPurificationSiphon);server.Select(gp,ZombieModeRewardType.TriggerDoomPulse);
        var enemy=server.Enemy();server.Kill(enemy,new DamageInfo(guest.Body));BossRushCoop.DropAck=true;BossRushCoop.Flush();Check(guest.Body.Health.Value==22&&host.Body.Health.Value==20,"guest direct kill heals only its owner using host death settlement");Check(server.Stars==12&&client.Stars==0,"guest siphon adds the original shared world stars exactly once at host");
        server.Kill(enemy,new DamageInfo(guest.Body));server.Tick();BossRushCoop.Flush();Check(guest.Body.Health.Value==22,"duplicate death and dropped ACK cannot duplicate personal healing");BossRushCoop.DropAck=false;server.Tick();BossRushCoop.Flush();
        for(int i=1;i<40;i++)server.Kill(server.Enemy(),new DamageInfo(guest.Body));Check(LevelManager.Instance.ExplosionManager.Hits.Count==0&&server.owner.Coroutines.Count==1,"fortieth confirmed guest kill defers world explosion out of death event");host.Body.Health.IsDead=true;guest.Body.transform.position=new Vector3(30,0,10);server.owner.Advance();Check(LevelManager.Instance.ExplosionManager.Hits.Count==3,"guest doom pulse still executes when host body is dead");foreach(var hit in LevelManager.Instance.ExplosionManager.Hits)Check(ReferenceEquals(hit.fromCharacter,guest.Body)&&hit.isFromBuffOrEffect&&hit.isExplosion&&hit.damageValue==30,"all host explosions retain guest source, damage, and recursion protection");Check(server.runState.RunOnlyObjects.Count==0,"finished pulse immediately releases run-only record");BossRushCoop.Flush();
        int stars=server.Stars,heals=guest.Body.Health.Heals;server.Kill(server.Enemy(),new DamageInfo(guest.Body){isFromBuffOrEffect=true});BossRushCoop.Flush();Check(server.Stars==stars+10&&guest.Body.Health.Heals==heals,"effect kills do not recursively trigger direct-kill rewards");
        gp.AssetId="slot-B";server.Kill(server.Enemy(),new DamageInfo(guest.Body));BossRushCoop.Flush();Check(guest.Body.Health.Heals==heals,"changed asset cannot inherit frozen participant kill options");gp.AssetId="slot-A";
        server.Kill(server.Enemy(),new DamageInfo(guest.Body));guest.Asset="slot-B";BossRushCoop.Flush();Check(guest.Body.Health.Heals==heals,"in-flight kill receipt cannot heal another asset");guest.Asset="slot-A";server.Tick();BossRushCoop.Flush();Check(guest.Body.Health.Heals==heals+1,"same-fight return to original asset retries the earned heal");
        server.Kill(server.Enemy(),new DamageInfo(guest.Body));client.runState.CurrentWave++;BossRushCoop.Flush();Check(guest.Body.Health.Heals==heals+1,"late receipt cannot heal a later wave");
        BossRushCoop.Current=guest;var predicted=client.Enemy();int localStars=client.Stars;client.Kill(predicted,new DamageInfo(guest.Body));Check(client.Stars==localStars,"client replica OnDead cannot create world rewards or independent kill settlement");BossRushCoop.Current=host;
        host.Body.Health.IsDead=false;server.runState.OptionRuntime.TriggerLifestealChancePercent=100;server.runState.OptionRuntime.TriggerLifestealHealAmount=3;server.Kill(server.Enemy(),new DamageInfo(host.Body));Check(host.Body.Health.Value==23,"host fatal direct hit still receives original lifesteal exactly once");
        server.Reset();stars=server.Stars;server.Kill(server.Enemy(),new DamageInfo(guest.Body));BossRushCoop.Flush();Check(server.Stars==stars+10,"new run reset clears prior participant reward stacks");
        server.Dispose();BossRushCoop.Current=guest;client.Dispose();Console.WriteLine("ZombieCoopKillRewards PASS "+checks+" production death/delivery assertions; Unity/network boundary isolated");
    }
}

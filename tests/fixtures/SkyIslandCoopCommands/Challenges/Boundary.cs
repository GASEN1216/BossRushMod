using System;using System.Collections;using System.Collections.Generic;using UnityEngine;
namespace UnityEngine {
 public static class Time {public static float time=1,unscaledTime=1,unscaledDeltaTime=.1f,realtimeSinceStartup=1;}
 public struct Vector3 { public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public float sqrMagnitude {get{return x*x+y*y+z*z;}}public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static float Distance(Vector3 a,Vector3 b){return (float)Math.Sqrt((a-b).sqrMagnitude);} }
 public sealed class Transform {public Vector3 position;}
 public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}}
 public static class Debug {public static void LogWarning(string text) {}}
}
public sealed class Health {public bool IsDead;}
public sealed class CharacterMainControl {public static CharacterMainControl Main;public Health Health=new Health();public Transform transform=new Transform();}
namespace BossRush {
 internal static class L10n {internal static bool IsChinese {get{return false;}}internal static string T(string cn,string en){return en;}}
 internal sealed class BossRushCoopPlayer {internal string Id,AssetId="asset";internal CharacterMainControl Character;internal bool IsAlive=true,IsConnected=true;internal bool IsLocal {get{return Id==BossRushCoop.LocalPlayerId;}}}
 internal sealed class BossRushCoopMessage {internal string SenderId,Json;internal bool IsFromHost;internal T Read<T>(){return BossRushCoopJson.FromJson<T>(Json);}}
 internal static class BossRushCoop {
  internal static bool IsConnected=true,IsHost=true,HasBoundScene=true,SessionReady=true;
  internal static bool IsClient {get{return IsConnected&&!IsHost;}}internal static bool HasWorldAuthority {get{return !IsConnected||IsHost;}}
  internal static string LocalPlayerId="host",LocalAssetId="asset",RunId="room",SceneToken="sky-core";internal static int SceneGeneration=2;
  internal static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();
  internal sealed class Sent {internal string Sender,Target,Topic,Json;}
  internal static readonly List<Sent> Outbox=new List<Sent>();internal static readonly Dictionary<string,Action<BossRushCoopMessage>> Handlers=new Dictionary<string,Action<BossRushCoopMessage>>();
  internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback) {string key=LocalPlayerId+":"+topic;Handlers[key]=callback;return new End(()=>Handlers.Remove(key));}
  private sealed class End:IDisposable {private Action close;internal End(Action value){close=value;}public void Dispose(){close();}}
  internal static BossRushCoopPlayer FindPlayer(string id){return Players.Find(p=>p.Id==id);}internal static CharacterMainControl GetCombatTarget(Vector3 at){return Players.Find(p=>p.IsAlive)?.Character;}
  internal static bool SendToHost<T>(string topic,T payload){return SendToPlayer("host",topic,payload);}
  internal static bool SendToPlayer<T>(string target,string topic,T payload){Outbox.Add(new Sent{Sender=LocalPlayerId,Target=target,Topic=topic,Json=BossRushCoopJson.ToJson(payload)});return true;}
  internal static bool Broadcast<T>(string topic,T payload){return true;}
  internal static void Emit(Sent packet,bool fromHost){Handlers[packet.Target+":"+packet.Topic](new BossRushCoopMessage{SenderId=packet.Sender,IsFromHost=fromHost,Json=packet.Json});}
 }
 internal sealed class ModBehaviour {internal SkyIslandSession Session;internal T GetComponent<T>() where T:class{return Session as T;}internal void ShowMessage(string value){}}
 internal static class SkyIslandSceneReferenceBridge {internal static bool IsActiveScene=true;}
 internal static class SkyIslandOfficialQuestTable {internal static string NextContactObjective(SkyIslandStoryData value){return null;}}
 internal static class SkyIslandOfficialQuestStory {internal static readonly Dictionary<string,SkyIslandStoryService> Stores=new Dictionary<string,SkyIslandStoryService>();internal static SkyIslandStoryService Resolve(ModBehaviour owner,out bool onIsland){onIsland=owner.Session!=null;SkyIslandStoryService value;return Stores.TryGetValue(BossRushCoop.LocalAssetId,out value)?value:null;}}
 internal sealed class SkyIslandStoryService {
  internal bool CanWrite=true;internal SkyIslandStoryData Current=SkyIslandStoryRules.CreateDefault();internal int Applies,Timings;
  internal bool TryApply(SkyIslandStoryAction action,out string message){SkyIslandStoryData next;if(!CanWrite||!SkyIslandStoryRules.TryApply(Current,action,out next,out message)){message="blocked";return false;}Current=next;Applies++;return true;}
  internal void LogTiming(string name,string id){Timings++;}internal void RecordEncounterCleared(string id){}
 }
 internal static class SkyIslandStormEchoRules {internal const int MaxPerRaid=1;internal const string EncounterId="StormEcho";internal const string Opened="Echo opened",StartFailed="Echo failed";internal static bool IsEcho(string id){return id==EncounterId;}}
 internal sealed class SkyIslandInventoryTransaction {internal void Commit(){}internal void Dispose(){}}
 internal sealed class Residents {internal bool Visible;internal void SetVisible(string id,bool value) {Visible=value;}}
 internal sealed class SkyIslandBounty {internal int Clears;internal void ReportEncounterCleared(){Clears++;}}
 internal sealed partial class SkyIslandSession {
  internal SkyIslandEncounters encounters;private SkyIslandStoryService story;private bool stormEchoCleared;private int stormEchoStarts;private float nextCoopState;
  private CoopChallenge coopEchoRequest,coopNamedRequest;private float coopEchoRequestAt,coopNamedRequestAt;
  private SkyIslandInventoryTransaction coopEchoCost;private CoopPersonalDelivery<CoopChallengeReply> coopChallengeDelivery;
  private readonly HashSet<string> coopCompletedChallenges=new HashSet<string>();private readonly Dictionary<string,CoopChallengeReply> coopChallengeReplies=new Dictionary<string,CoopChallengeReply>();private readonly Dictionary<string,int> coopEchoStarts=new Dictionary<string,int>();
  private readonly HashSet<string> bountyCredited=new HashSet<string>();internal readonly SkyIslandBounty bounty=new SkyIslandBounty();
  private readonly Residents residents=new Residents();internal bool ResidentVisible {get{return residents.Visible;}}private bool IsStoryChallengeActive(string id) {return encounters.IsBusy(id);}
  internal bool WorldValid=true;internal string StatusText;internal bool StatusError;
  internal SkyIslandSession(SkyIslandStoryService value){story=value;encounters=new SkyIslandEncounters(id=>EncounterWasSaved(id),OnEncounterCleared);}
  internal void Wire(){coopChallengeDelivery=new CoopPersonalDelivery<CoopChallengeReply>("sky.echo",ApplyCoopChallengeReply);BossRushCoop.Register("sky.challenge.request",OnCoopChallenge);}
  internal void Close(){coopChallengeDelivery.Dispose();}
  private bool IsWorldSessionValid(){return WorldValid&&BossRushCoop.HasBoundScene;}
  internal bool Request(string id){return RequestCoopChallenge(id);}internal string RequestId {get{return coopNamedRequest?.RequestId;}}
  internal void Send(){BossRushCoop.SendToHost("sky.challenge.request",coopNamedRequest);}
  internal bool Complete(string id,bool cleared){return CompleteCoopChallenge(id,cleared);}
  internal void Clear(string id){OnEncounterCleared(id);}private void AnnounceGuardProgress(string id){}
  private bool RecordStormEchoCleared(string id){return false;}private void Status(string value,bool error){StatusText=value;StatusError=error;}
  internal static bool CanEnter(ModBehaviour owner,out string reason){reason=null;return owner.Session==null;}
  internal static void EnterPrepared(ModBehaviour owner,Action<string,bool> status){}internal static void EnterCoopArrival(ModBehaviour owner,Action<string,bool> status){}
  internal bool CoopTravelActive=true;
 }
 internal sealed class SkyIslandEncounterDefinition {}internal struct SkyIslandChatterEvent {}
 internal sealed class SkyIslandEnemyRecord {internal bool Died;internal object Life;}
 internal sealed partial class SkyIslandEncounters {
  private readonly List<Encounter> encounters=new List<Encounter>();private bool closed;private float nextTick;
  private readonly Func<bool> valid=()=>BossRushCoop.HasBoundScene;private readonly Func<string,bool> completed;private readonly Action<string> cleared;
  private readonly Action<string,bool> report=(s,e)=>{};private readonly Func<string,string> describe=s=>s;private readonly CharacterMainControl player;
  internal int Spawns;private const float ChallengeRange=70,AutoSpawnRange=55;private const int MaxActiveActors=12,ChallengeReserve=3;
  internal SkyIslandEncounters(Func<string,bool> saved,Action<string> clear){completed=saved;cleared=clear;player=CharacterMainControl.Main;foreach(string id in new[]{"Zheling","BellKeeper","Storm"})encounters.Add(new Encounter{Id=id,Manual=true,Marker=new Transform(),Count=3,Actors=new[]{new SkyIslandEnemyRecord(),new SkyIslandEnemyRecord(),new SkyIslandEnemyRecord()}});}
  internal bool IsBusy(string id) {var value=Find(id);return value.Started&&!value.Cleared&&!value.AllDead;}internal bool WasStartedThisRaid(string id) {return Find(id).Started;}
  private void Spawn(Encounter value){Spawns++;value.Started=true;foreach(var actor in value.Actors)actor.Life=new object();}
  private int CountActiveActors(){int n=0;foreach(var e in encounters)n+=CountLiving(e);return n;}
  private int CountLiving(Encounter e){int n=0;foreach(var a in e.Actors)if(!a.Died&&a.Life!=null)n++;return n;}
  private int CountMissing(Encounter e){int n=0;foreach(var a in e.Actors)if(!a.Died&&a.Life==null)n++;return n;}
  private bool NightLeadDue(Encounter e){return false;}private void TickChatter(){}
  internal bool Cleared(string id){return Find(id).Cleared;}internal void Kill(string id){foreach(var actor in Find(id).Actors)actor.Died=true;Time.time+=2;Tick();}
 }
}

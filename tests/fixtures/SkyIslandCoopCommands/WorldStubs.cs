using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
    }
    public sealed class Transform { public Vector3 position; }
    public struct Color { }
    public static class Mathf { public static float Abs(float x) { return Math.Abs(x); } public static float Sqrt(float x) { return (float)Math.Sqrt(x); } public static float Max(float a,float b) { return Math.Max(a,b); } public static bool Approximately(float a,float b) { return Math.Abs(a-b)<0.0001f; } }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics { public static readonly HashSet<float> BlockedX=new HashSet<float>(); public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction interaction) { return BlockedX.Contains(b.x); } }
    public static class Debug { public static void LogWarning(string text) { } }
}
public sealed class Health { public bool IsDead; }
public sealed class DamageReceiver { public int Hits; public float Total; public void Hurt(DamageInfo value) { Hits++;Total+=value.damageValue; } }
public sealed class ItemAgent_Gun { public CharacterMainControl Holder; }
namespace Eflatun.SceneReference { public sealed class SceneReference { public bool Owned; } }
namespace BossRush
{
    internal sealed class PlayerCollection : IEnumerable<BossRushCoopPlayer>
    {
        private readonly Dictionary<string,BossRushCoopPlayer> values=new Dictionary<string,BossRushCoopPlayer>();
        internal BossRushCoopPlayer this[string key] { get { return values[key]; } set { values[key]=value; } }
        internal bool TryGetValue(string key,out BossRushCoopPlayer value) { return values.TryGetValue(key,out value); }
        internal void Clear() { values.Clear(); }
        public IEnumerator<BossRushCoopPlayer> GetEnumerator() { return values.Values.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    internal sealed class CallbackLease : IDisposable { private readonly Action action; internal CallbackLease(Action action) { this.action=action; } public void Dispose() { action(); } }
    internal static partial class BossRushCoop
    {
        internal static int SceneGeneration,Marks; internal static bool BarrierReady;
        internal static readonly Dictionary<string,Action<BossRushCoopMessage>> Handlers=new Dictionary<string,Action<BossRushCoopMessage>>();
        internal static readonly List<BossRushCoopMessage> Broadcasts=new List<BossRushCoopMessage>(),Personal=new List<BossRushCoopMessage>();
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback) { Handlers[topic]=callback;return new CallbackLease(()=>Handlers.Remove(topic)); }
        internal static void Emit(string topic,object value,bool fromHost=true) { Handlers[topic](new BossRushCoopMessage { SenderId="host",Payload=value,IsFromHost=fromHost }); }
        internal static bool SendAccepted=true;internal static bool Broadcast<T>(string topic,T value) { if(!SendAccepted)return false;Broadcasts.Add(new BossRushCoopMessage { IsFromHost=true,Payload=value });return true; }
        internal static void SendToPlayer<T>(string id,string topic,T value) { Personal.Add(new BossRushCoopMessage { SenderId=id,Payload=value }); }
        internal static void MarkSceneReady(string key) { Marks++; }
        internal static bool AllPlayersSceneReady(string key) { return BarrierReady; }
        internal static CharacterMainControl GetCombatTarget(Vector3 position) { foreach(var player in Players)if(player.IsAlive)return player.Character;return null; }
        internal static CharacterMainControl FindAi(int id) { return ProjectionActor; }
        internal static CharacterMainControl ProjectionActor;
    }
    internal sealed class ModBehaviour
    {
        internal SkyIslandSession Session;
        internal T GetComponent<T>() where T:class { return Session as T; }
        internal void ShowMessage(string message) { }
    }
    internal sealed partial class SkyIslandSession
    {
        internal void CreditChallengeBounty(string id) { }
        internal static int Prepared,Joined;
        internal bool CoopTravelActive=true; private bool closed,coopJoining;
        internal static bool CanEnter(ModBehaviour owner,out string reason) { reason=null;return owner.Session==null; }
        internal static void EnterPrepared(ModBehaviour owner,Action<string,bool> report) { Prepared++;owner.Session=new SkyIslandSession(); }
        internal static void EnterCoopArrival(ModBehaviour owner,Action<string,bool> report) { Joined++;owner.Session=new SkyIslandSession { coopJoining=true }; }
        internal IEnumerator Barrier(string key) { return WaitForCoopScene(key); }
        internal int Cleanups;
        private void Cleanup(string reason) { Cleanups++;closed=true; }
        internal void BindScope() { CaptureCoopSessionScope(); }
        internal bool ScopeValid { get { return IsCoopSessionScopeValid(); } }
        internal void SessionChanged() { OnCoopSessionChanged(); }
    }
    internal static partial class SkyIslandSceneReferenceBridge
    {
        internal const string SceneId="BossRush_SkyIsland"; internal static bool IsActiveScene;
        private static bool OwnsReference(Eflatun.SceneReference.SceneReference value) { return value!=null && value.Owned; }
        internal static void NativePrefix(bool owned) { CoopArrivalPrefix(new Eflatun.SceneReference.SceneReference { Owned=owned }); }
    }
    internal static class L10n { internal static string T(string cn,string en) { return en; } }
    internal static class ZombieModeStatNames { internal const string WalkSpeed="walk",RunSpeed="run"; }
    internal sealed class BossRushStatModifierRecord { internal CharacterMainControl Target; }
    internal static class RuntimeStatModifierTracker
    {
        internal static readonly Dictionary<CharacterMainControl,int> Active=new Dictionary<CharacterMainControl,int>();
        internal static bool TryAdd(CharacterMainControl actor,string stat,float amount,object source,List<BossRushStatModifierRecord> records,string context)
        { if(actor==null)return false;records.Add(new BossRushStatModifierRecord { Target=actor });int count;Active.TryGetValue(actor,out count);Active[actor]=count+1;return true; }
        internal static void RemoveAll(List<BossRushStatModifierRecord> records,string context)
        { foreach(var record in records)Active[record.Target]--;records.Clear(); }
    }
    internal static class SkyIslandBossGearWorn { internal static bool Earmuffs; internal static int RootweavePieces; internal static int WorldRootweavePieces { get { return SkyIslandCoopWorldEffects.WorldRootweavePieces; } } }
    internal static partial class SkyIslandBossRules { internal static readonly int[] RootweaveSet={1,2,3}; }
    internal static partial class SkyIslandRewardCrate { internal static int BonusPieces(bool shared) { return RootweavePiecesFor(shared); } }
    internal sealed partial class SkyIslandCoopWorldEffects
    {
        private static SkyIslandCoopWorldEffects current;
        private sealed class Line { } private sealed class Prop { } private sealed class Lamp { }
        private World latest;
        internal static event Action<CharacterMainControl,long> PlayerGunNoise;
        private readonly Dictionary<string,PlayerEffectsState> playerEffects=new Dictionary<string,PlayerEffectsState>();
        private readonly Dictionary<string,LocalSlow> slows=new Dictionary<string,LocalSlow>();
        private readonly Dictionary<string,long> slowRevisions=new Dictionary<string,long>();
        private readonly List<string> expiredSlows=new List<string>();
        private string localAsset,localStream; private long localNoise,localRevision,sequence,fixtureSlowRevision;
        internal SkyIslandCoopWorldEffects() { current=this; }
        internal int SlowCount { get { return slows.Count; } }
        internal long WorldRevision { get { return latest != null ? latest.Revision : 0; } }
        internal void WorldPacket(long revision,bool fromHost=true,string travel=null,string sceneToken=null)
        { ReceiveWorld(new BossRushCoopMessage { IsFromHost=fromHost,Payload=new World { TravelId=travel??TravelId,SceneToken=sceneToken??BossRushCoop.SceneToken,Revision=revision } }); }
        internal bool WorldCurrent {get{return IsWorldCurrent();}}
        internal void FlushEffects() { PublishPlayerEffects(); }
        internal void Shot(CharacterMainControl actor) { OnLocalShot(new ItemAgent_Gun { Holder=actor }); }
        internal object Snapshot(long count,bool ears,string stream="stream-1",string asset="asset-1",int rootweave=0) { return new PlayerEffects { TravelId=TravelId,SceneToken=BossRushCoop.SceneToken,AssetId=asset,Stream=stream,Noise=count,Revision=count,Earmuffs=ears,Rootweave=rootweave }; }
        internal void Receive(string sender,object value) { ReceivePlayerEffects(new BossRushCoopMessage { SenderId=sender,Payload=value }); }
        internal void AddLocalSlow(string id,float seconds) { SlowPacket(id,++fixtureSlowRevision,-.3f,seconds); }
        internal void SlowPacket(string id,long revision,float percent,float seconds=2,string asset="asset-1") { ReceiveSlow(new BossRushCoopMessage { IsFromHost=true,Payload=new Slow { TravelId=TravelId,SceneToken=BossRushCoop.SceneToken,AssetId=asset,Id=id,Percent=percent,Seconds=seconds,Revision=revision } }); }
        internal void Expire() { TickSlows(); }
    }
    internal sealed class SkyIslandBossContext { internal Transform Root=new Transform(); }
    internal static class SkyIslandBossForge { internal static int Detonations; internal static void Detonate(CharacterMainControl boss,Vector3 at,float radius,float damage,bool flag,float shake,Color tint) { Detonations++; } }
    internal static class SkyIslandImpactFx { internal static void Shake(Vector3 at,float amount) { } internal static void Play(Transform root,Vector3 center,float radius,Color tint,int count,bool flag) { } }
    internal static class GameplayDataSettings { internal static class Layers { internal const int wallLayerMask=1; } }
    internal sealed partial class SkyIslandBellEngineMoves
    {
        private CharacterMainControl boss=new CharacterMainControl();private SkyIslandBossContext context=new SkyIslandBossContext();private SkyIslandPlayerSlow slow=new SkyIslandPlayerSlow("bell");
        private static readonly Color FarTint=new Color();
        internal void Strike(Vector3 center) { StrikeFar(center,0f); }
        internal void Expire(float now) { slow.Tick(now); }
    }
}

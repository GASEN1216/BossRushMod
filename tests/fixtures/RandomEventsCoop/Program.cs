using System;
using System.Collections.Generic;
using BossRush;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed; public static float LastDelay;
        public static bool operator ==(Object a,Object b) { return ReferenceEquals(a,b)||((ReferenceEquals(a,null)||a.Destroyed)&&(ReferenceEquals(b,null)||b.Destroyed)); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object other) { return this==other as Object; } public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value) { if(ReferenceEquals(value,null))return; value.Destroyed=true; var go=value as GameObject; if(!ReferenceEquals(go,null))go.transform.Destroyed=true; }
        public static void Destroy(Object value,float delay) { LastDelay=delay; }
    }
    public sealed class Transform : Object { public Vector3 position; }
    public sealed class GameObject : Object { public Transform transform=new Transform(); public GameObject(string name) { } }
    public struct Vector3 { public float x,y,z; public static Vector3 zero,up; public static Vector3 operator +(Vector3 a,Vector3 b) { return a; } public static Vector3 operator *(Vector3 a,float b) { return a; } }
    public static class Mathf { public static float Max(float a,float b) { return Math.Max(a,b); } public static float Min(float a,float b) { return Math.Min(a,b); } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex,handle; }
    public static class SceneManager { public static int Handle=1; public static Scene GetActiveScene() { return new Scene { handle=Handle }; } }
}
namespace BossRush
{
    internal static class BossRushCoop { internal static bool IsConnected=true,IsClient=true,HasBoundScene; internal static string RunId="room",SceneToken; internal static int SceneGeneration=3; }
    internal sealed class BossRushCoopMessage { internal bool IsFromHost; internal object Payload; internal T Read<T>() { return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Payload)); } }
    internal static class L10n { internal static string T(string cn,string en) { return en; } }
    internal static class BossRushUIColors { internal const int Accent=1; }
    internal static class RandomEventFx
    {
        internal static int Plays,Markers; internal static UnityEngine.GameObject MarkerOwner;
        internal static void PlayFirework(UnityEngine.Vector3 p,int i) { Plays++; } internal static void PlayAirdropLanding(UnityEngine.Vector3 p) { Plays++; }
        internal static void SpawnAirdropMarker(UnityEngine.Vector3 p,UnityEngine.GameObject owner,float duration,UnityEngine.Transform parent) { Markers++;MarkerOwner=owner; if(parent!=owner.transform)throw new Exception("marker must attach to the visual owner"); }
    }
    internal sealed partial class RandomEventsRuntimeModule
    {
        private string coopFxRun; private int coopFxScene=-1; private readonly HashSet<string> coopReceivedFx=new HashSet<string>(StringComparer.Ordinal);
        private readonly List<UnityEngine.GameObject> coopAirdropVisuals=new List<UnityEngine.GameObject>();
        private void ClearCoopMerchant() { }
        private void PopRandomEventText(string text,UnityEngine.Vector3 pos,int color,float time) { RandomEventFx.Plays++; }
        private void PlayRandomEventModSound(string path) { }
        internal void Receive(BossRushCoopMessage message) { ReceiveCoopFx(message); }
    }
    internal static class RandomEventsTuning { internal const string LogPrefix=""; internal const float MaxDeltaPerFrame=1f, RunSignaturePollIntervalSeconds=1f; }
    internal class ModBehaviour { internal static void DevLog(string value) { } internal void ShowRandomEventDirectionalBanner(string name,UnityEngine.Vector3 anchor) { } }
    internal class RuntimeScope { internal int Clears; internal RuntimeScope(string name,ModBehaviour owner) { } internal void Clear(string reason) { Clears++; } }
    internal static class RandomEventEffectHelpers { internal static void ClearScope(RandomEventContext c,RandomEventEndReason r) { c.Scope.Clear(r.ToString()); } }
    internal static class RandomEventModeGate { internal static int Signature=1; internal static int ComputeRunSignature(ModBehaviour owner) { return Signature; } }
    internal class ProbeEvent : RandomEventBase
    {
        internal int Starts,Stops,Ticks; internal RandomEventId Event;
        internal ProbeEvent(RandomEventId id) { Event=id; }
        internal override RandomEventId Id { get { return Event; } }
        internal override string DisplayName { get { return Event.ToString(); } }
        internal override float DurationSeconds { get { return 24f; } }
        internal override float Weight { get { return 1f; } }
        internal override bool OnTrigger(RandomEventContext context) { Starts++; return true; }
        internal override void OnTick(RandomEventContext c,float d) { Ticks++; }
        internal override void OnCleanup(RandomEventContext c,RandomEventEndReason r) { Stops++; }
    }
    internal sealed class RandomEventBloodMoon : ProbeEvent { internal RandomEventBloodMoon():base(RandomEventId.BloodMoon) { } }
    internal sealed class RandomEventTempo : ProbeEvent { internal RandomEventTempo():base(RandomEventId.WildChase) { } }
    internal sealed class RandomEventWanderingMerchant : ProbeEvent { internal RandomEventWanderingMerchant():base(RandomEventId.WanderingMerchant) { } internal string CoopEntity { get { return "a:-12"; } } }
    internal static class RandomEventCatalog
    {
        internal static Dictionary<RandomEventId,ProbeEvent> Events=new Dictionary<RandomEventId,ProbeEvent>();
        internal static RandomEventBase Find(RandomEventId id) { ProbeEvent evt; return Events.TryGetValue(id,out evt)?evt:null; }
        internal static void Reset() { Events.Clear(); for(int i=1;i<=11;i++) Events[(RandomEventId)i]=new ProbeEvent((RandomEventId)i); Events[RandomEventId.BloodMoon]=new RandomEventBloodMoon(); Events[RandomEventId.WildChase]=new RandomEventTempo(); }
    }
    internal sealed partial class RandomEventDirector
    {
        private ModBehaviour _owner=new ModBehaviour(); private RandomEventContext _activeContext; private RandomEventBase _activeEvent;
        private RandomEventPhase _phase; private int _eventsFiredThisRun,_maxEventsThisRun,_generation,_runSignature=1;
        private float _signaturePollTimer,_phaseTimer; private RandomEventId _lastTriggeredId; private bool _activeTickFaulted,_activeEventCounted;
        internal int WorldTicks;
        internal RandomEventId ActiveEventId { get { return _activeEvent!=null?_activeEvent.Id:RandomEventId.None; } }
        internal float ActiveDurationSeconds { get { return _activeContext!=null?_activeContext.DurationSeconds:0f; } }
        internal float Remaining { get { return _activeContext!=null?_activeContext.RemainingSeconds:0f; } }
        private void HandleRunSignatureChanged(int value) { _runSignature=value; }
        private void TickArmed(float d) { WorldTicks++; } private void TickEventActive(float d) { WorldTicks++; } private void TickCooldown(float d) { WorldTicks++; }
        private void LogFailure(string step,Exception error) { throw error; }
        internal void Clear() { EndActiveEvent(RandomEventEndReason.SceneChanged); }
    }
}
class Program
{
    static int checks;
    static void Check(bool value,string reason) { if(!value)throw new Exception(reason);checks++; }
    static CoopRandomEventState State(int id,int serial) { return new CoopRandomEventState { Id=id,Serial=serial,Phase=(int)RandomEventPhase.EventActive,Elapsed=7f,Duration=24f }; }
    static void Main()
    {
        for(int peer=0;peer<16;peer++)
        {
            RandomEventCatalog.Reset(); var director=new RandomEventDirector();
            for(int id=1;id<=11;id++)
            {
                var state=State(id,id); director.ApplyCoop(state); director.ApplyCoop(state); director.Tick(.5f);
                ProbeEvent evt=RandomEventCatalog.Events[(RandomEventId)id]; bool local=id==2||id==9;
                Check(evt.Starts==(local?1:0),"projection must not invoke world factories or repeat personal modifiers");
                Check(director.ActiveEventId==(RandomEventId)id,"all event kinds retain client HUD");
                Check(director.WorldTicks==0,"client director never rolls or advances shared world");
                Check(director.Remaining==16.5f,"late join mirrors actual remaining duration");
                director.Clear(); if(local)Check(evt.Stops==1,"scene cleanup releases local effects");
            }
        }
        RandomEventCatalog.Reset(); var retry=new RandomEventDirector(); var first=State(9,90);
        retry.ApplyCoop(first); retry.Clear(); retry.ApplyCoop(first);
        Check(RandomEventCatalog.Events[RandomEventId.WildChase].Starts==2,"same snapshot rebinds after destroyed scene projection");
        retry.ApplyCoop(State(9,91));Check(RandomEventCatalog.Events[RandomEventId.WildChase].Starts==3,"next occurrence of same event starts once");
        var a=retry.CaptureCoop();var b=retry.CaptureCoop();Check(a.Serial==b.Serial,"periodic snapshots keep same instance token");
        retry.ApplyCoop(State(0,92));Check(retry.ActiveEventId==RandomEventId.None,"inactive snapshot clears effect");
        Check(retry.CaptureCoop().Serial>a.Serial,"context change invalidates previous instance");
        BossRushCoop.IsClient=false;retry.ApplyCoop(first);Check(retry.ActiveEventId==RandomEventId.None,"host rejects snapshot application");
        BossRushCoop.IsClient=true;var fxOwner=new RandomEventsRuntimeModule();
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="random-core";
        var fx=new CoopRandomEventFx { Run="room",Token=BossRushCoop.SceneToken,Scene=3,Kind=1,Id="same" };
        var message=new BossRushCoopMessage { IsFromHost=true,Payload=fx };
        BossRushCoop.HasBoundScene=false;fxOwner.Receive(message);Check(RandomEventFx.Plays==0,"unbound Core cannot consume same-generation FX");
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="new-random-core";fxOwner.Receive(message);Check(RandomEventFx.Plays==0,"new Core with old public generation rejects old-token FX");
        BossRushCoop.SceneToken=fx.Token;
        fxOwner.Receive(message);fxOwner.Receive(message);Check(RandomEventFx.Plays==1,"duplicate FX Guid only plays once");
        fx.Id="guest";message.IsFromHost=false;fxOwner.Receive(message);Check(RandomEventFx.Plays==1,"client cannot inject world FX");
        message.IsFromHost=true;fx.Id="old";fx.Run="previous";fxOwner.Receive(message);Check(RandomEventFx.Plays==1,"old room FX rejected");
        fx.Run="room";fx.Scene=2;fxOwner.Receive(message);Check(RandomEventFx.Plays==1,"old scene FX rejected");
        fx.Scene=3;fx.Position=new UnityEngine.Vector3 { x=float.NaN };
        bool positionRejected=false;try { fxOwner.Receive(message); } catch (FormatException) { positionRejected=true; }
        Check(positionRejected&&RandomEventFx.Plays==1,"real network codec rejects nonfinite position before presentation");
        fx.Position=new UnityEngine.Vector3();fx.Kind=5;fxOwner.Receive(message);Check(RandomEventFx.Plays==1,"unknown FX kind rejected");
        fx.Kind=2;fxOwner.Receive(message);Check(RandomEventFx.Plays==2,"valid scene text delivered");
        fx.Id="airdrop";fx.Kind=4;fx.Duration=float.NaN;
        bool durationRejected=false;try { fxOwner.Receive(message); } catch (FormatException) { durationRejected=true; }
        Check(durationRejected&&RandomEventFx.Markers==0,"real network codec rejects nonfinite marker lifetime before presentation");
        fx.Duration=-1;fxOwner.Receive(message);Check(RandomEventFx.Markers==0,"negative marker lifetime rejected");
        fx.Duration=4;fxOwner.Receive(message);fxOwner.Receive(message);Check(RandomEventFx.Markers==1,"airdrop marker is idempotent");
        var marker=RandomEventFx.MarkerOwner;Check(marker!=null&&UnityEngine.Object.LastDelay==4.5f,"marker keeps a bounded visual lifetime");
        BossRushCoop.SceneGeneration=4;fx.Scene=4;fx.Id="same";fx.Kind=3;fxOwner.Receive(message);Check(RandomEventFx.Plays==3,"new scene resets presentation receipts");
        Check(marker==null&&marker.transform==null,"scene change destroys marker and attached presentation");
        BossRushCoop.IsClient=false;fx.Id="host";fxOwner.Receive(message);Check(RandomEventFx.Plays==3,"host never repeats its local presentation");
        BossRushCoop.IsConnected=true;BossRushCoop.IsClient=true;BossRushCoop.RunId="original";
        RandomEventCatalog.Reset();var disconnect=new RandomEventDirector();disconnect.ApplyCoop(State(9,100));disconnect.Tick(.1f);
        BossRushCoop.RunId="";Check(disconnect.ValidateCoopRun(),"same-room empty handshake preserves original event");
        BossRushCoop.RunId="original";Check(disconnect.ActiveEventId==RandomEventId.WildChase,"handshake cannot erase original event");
        BossRushCoop.IsConnected=BossRushCoop.IsClient=false;disconnect.Tick(.1f);disconnect.Tick(10f);
        Check(disconnect.ActiveEventId==RandomEventId.None && RandomEventCatalog.Events[RandomEventId.WildChase].Stops==1 && disconnect.WorldTicks==0,"true Stop releases client effects exactly once and never becomes offline world director");
        BossRushCoop.IsConnected=true;BossRushCoop.IsClient=false;BossRushCoop.RunId="new-room";disconnect.Tick(10f);
        Check(disconnect.WorldTicks==0,"old same-map run cannot promote even after reconnect as host");
        RandomEventModeGate.Signature=0;disconnect.Tick(.1f);RandomEventModeGate.Signature=1;disconnect.Tick(2f);disconnect.Tick(2f);
        Check(disconnect.ValidateCoopRun(),"actual old mode completion permits a fresh independent run");
        BossRushCoop.IsConnected=false;BossRushCoop.RunId="";UnityEngine.SceneManagement.SceneManager.Handle=2;
        var solo=new RandomEventDirector();solo.Tick(1f);Check(solo.ValidateCoopRun(),"new no-COOP director retains normal solo ownership");
        Console.WriteLine("RandomEventsCoop: PASS "+checks+" production assertions");
    }
}

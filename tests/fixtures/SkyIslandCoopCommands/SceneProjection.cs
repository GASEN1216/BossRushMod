using System;
using BossRush;
using UnityEngine;

namespace BossRush
{
    internal static class CoopAiPredictionHealthProtection
    { internal static int Marks; internal static void MarkReplica(CharacterMainControl actor) { Marks++; } }
    internal sealed class SkyIslandEnemyRecord { internal bool Died; internal SkyIslandEnemyLife Life; }
    internal sealed class SkyIslandEnemyLife
    { internal static int Binds; internal void Bind(CharacterMainControl actor,SkyIslandEnemyRecord record) { Binds++;actor.Life=this; } }
    internal sealed partial class SkyIslandEncounters
    {
        private bool closed;
        private sealed class Encounter { internal int Count=1;internal bool Started,Spawning,Cleared;internal SkyIslandEnemyRecord[] Actors={new SkyIslandEnemyRecord()}; }
        private readonly Encounter target=new Encounter();
        private Encounter Find(string id) { return id=="mirror"?target:null; }
        internal bool Started {get{return target.Started;}}
        internal void Receive(string token) { ApplyCoopState(new CoopState { SceneToken=token, Encounters=new[]{new CoopEncounter {Id="mirror",Started=true,Actors=new[]{1},Dead=new[]{false}}} }); }
    }
    internal sealed partial class SkyIslandFieldcraft
    {
        private string TravelId {get{return "travel-1";}}
        private sealed class Gathering {internal int Marks;internal void MarkHarvested(string id) {Marks++;}}
        private readonly Gathering gathering=new Gathering();
        internal int Marks {get{return gathering.Marks;}}
        internal void State(string token) {OnCoopGatherState(new BossRushCoopMessage {IsFromHost=true,Payload=new GatherState {TravelId=TravelId,SceneToken=token,Claimed=new[]{"B1"}}});}
    }
    internal sealed partial class SkyIslandGnats
    {
        private sealed class Gnat {internal Body Root;internal string CoopKey;internal Vector3 Position,LastStep;internal Motor Motor=new Motor();internal float BiteShownUntil;}
        private sealed class Motor {internal int Phase;internal Vector3 DashDirection;}
        private sealed class Body {internal bool activeSelf=true;internal Transform transform=new Transform();}
        private sealed class Zapper {internal Body Root;}
        private readonly Zapper[] zappers=new Zapper[8];
        private float nextSpawnCheck,spawnCooldownUntil;private bool veilCarried,lantern,coopSamplingRemote,NightNow;private EnvironmentState coopRemoteEnvironment;
        private static Vector3 ToVector(Vector3 value) {return value;}
        private void Sample(bool night,int wind,string region,CharacterMainControl player,bool incense,bool light,bool smoke,bool lamp,float interval) { }
        internal void PublishGnats() { TickCoopGnats(); }
        internal int PendingKills {get{return coopRecentKilled.Count;}}
        internal static int DeathsIn(object value) {return ((GnatWorld)value).Killed.Length;}
        internal static long RevisionIn(object value) {return ((GnatWorld)value).Revision;}
        internal int RememberedKills {get{return coopKilled.Count;}}
        private readonly Gnat[] gnats=new Gnat[8];
        
        private GnatWorld coopLatest;private int coopZapperCount;
        internal void Ingest(BossRushCoopMessage message) {ReceiveCoopGnats(message);}
        internal bool WorldCurrent {get{return IsCoopWorldCurrent();}}
        internal void World(string token) {ReceiveCoopGnats(new BossRushCoopMessage {IsFromHost=true,Payload=new GnatWorld {Revision=1,Travel=CoopTravel,SceneToken=token,Gnats=new GnatPose[0],Killed=new string[0],Zappers=new Vector3[0]}});}
    }
}
internal static class SceneProjection
{
    internal static void Run(Action<bool,string> check)
    {
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="batch-core";
        var source=new SkyIslandGnats();BossRushCoop.Broadcasts.Clear();Time.unscaledTime=10;
        for(int i=0;i<5000;i++)source.Kill("gnat-"+i,null);
        check(source.PendingKills==5000,"every new kill retained until actual reliable send succeeds");
        BossRushCoop.SendAccepted=false;source.PublishGnats();check(source.PendingKills==5000&&BossRushCoop.Broadcasts.Count==0,"failed send cannot consume death events");
        BossRushCoop.SendAccepted=true;Time.unscaledTime+=.2f;source.PublishGnats();
        check(source.PendingKills==904&&SkyIslandGnats.DeathsIn(BossRushCoop.Broadcasts[0].Payload)==4096,"bounded wire batch preserves remaining kill quantity");
        Time.unscaledTime+=.2f;source.PublishGnats();check(source.PendingKills==0&&SkyIslandGnats.DeathsIn(BossRushCoop.Broadcasts[1].Payload)==904,"next batch delivers all remaining kills");
        Time.unscaledTime+=.2f;source.PublishGnats();check(SkyIslandGnats.DeathsIn(BossRushCoop.Broadcasts[2].Payload)==0,"idle 10 Hz poses never repeat whole-raid kill history");
        BossRushCoop.IsHost=false;var replica=new SkyIslandGnats();
        foreach(var packet in BossRushCoop.Broadcasts)replica.Ingest(packet);
        check(replica.RememberedKills==5000,"client merges death batches once at receipt instead of each render frame");

        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.SceneGeneration=10;
        BossRushCoop.SceneToken="core-A";BossRushCoop.HasBoundScene=false;
        var gnats=new SkyIslandGnats();gnats.World("core-A");
        check(!gnats.WorldCurrent,"matching gnat snapshot retained but cannot render in unbound Core");
        BossRushCoop.HasBoundScene=true;check(gnats.WorldCurrent,"matching gnat snapshot remains available after binding");
        BossRushCoop.SceneToken="core-B";check(!gnats.WorldCurrent,"Core advance invalidates retained gnat state while public generation is unchanged");
        gnats.World("core-A");check(!gnats.WorldCurrent,"late old gnat message cannot rebind the new Core");
        gnats.World("core-B");check(gnats.WorldCurrent,"correct new gnat message restores world presentation");

        var effects=new SkyIslandCoopWorldEffects();effects.WorldPacket(100,sceneToken:"core-B");check(effects.WorldCurrent,"correct effects snapshot can render");
        BossRushCoop.HasBoundScene=false;check(!effects.WorldCurrent,"world effects apply checks actual binding on every frame");
        BossRushCoop.SceneToken="core-C";BossRushCoop.HasBoundScene=true;
        check(!effects.WorldCurrent,"retained old effect cannot apply to newly loaded Core before G changes");
        effects.WorldPacket(999,sceneToken:"core-B");check(!effects.WorldCurrent,"late wrong-token high revision cannot poison presentation");
        effects.WorldPacket(1,sceneToken:"core-C");check(effects.WorldCurrent&&effects.WorldRevision==1,"correct new token starts its own revision ordering");

        var encounters=new SkyIslandEncounters();BossRushCoop.ProjectionActor=new CharacterMainControl();SkyIslandEnemyLife.Binds=CoopAiPredictionHealthProtection.Marks=0;
        BossRushCoop.HasBoundScene=false;encounters.Receive("core-C");
        check(!encounters.Started&&SkyIslandEnemyLife.Binds==0,"unbound encounter state cannot attach AI or advance flags");
        BossRushCoop.HasBoundScene=true;encounters.Receive("core-B");
        check(!encounters.Started&&SkyIslandEnemyLife.Binds==0,"same generation old-token encounter cannot attach a new native actor");
        encounters.Receive("core-C");
        check(encounters.Started&&SkyIslandEnemyLife.Binds==1&&CoopAiPredictionHealthProtection.Marks==1,"correct encounter calls actual bind branch and replica registration");

        var gather=new SkyIslandFieldcraft();BossRushCoop.HasBoundScene=false;gather.State("core-C");
        check(gather.Marks==0,"gather world claims cannot mutate unbound map");
        BossRushCoop.HasBoundScene=true;gather.State("core-B");check(gather.Marks==0,"old map gather claims cannot hide a fresh node");
        gather.State("core-C");check(gather.Marks==1,"actual matching gather state still marks the shared node");
        check(BossRushCoop.SceneGeneration==10,"all token boundary cases hold even when public generation has not advanced");
    }
}

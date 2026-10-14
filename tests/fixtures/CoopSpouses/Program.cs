using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition,string label) { checks++;if(!condition)throw new Exception(label); }
    private static BossRushCoopPlayer Player(string id,string asset)
    { var p=new BossRushCoopPlayer {Id=id,AssetId=asset,Character=new GameObject(id).AddComponent<CharacterMainControl>()};BossRushCoop.Players.Add(p);return p; }
    private static CoopNpcWorld.SpouseIntent Intent(BossRushCoopPlayer p,string npc,long sequence=1,bool present=true,bool following=true)
    { return new CoopNpcWorld.SpouseIntent {NpcId=npc,Asset=p.AssetId,Sequence=sequence,Present=present,Following=following,Home=new Vector3(10,0,10),Run=BossRushCoop.RunId,Scene=BossRushCoop.SceneGeneration}; }
    private static void Send(BossRushCoopPlayer p,CoopNpcWorld.SpouseIntent request)
    {BossRushCoop.Channels["npc.spouse.request"](new BossRushCoopMessage {SenderId=p.Id,Payload=request});}
    private static GameObject Spouse(BossRushCoopPlayer p,string npc)
    {return CoopNpcWorld.FindObject(CoopNpcWorld.SpouseWorldId(p.Id,p.AssetId,npc));}
    private static void Tick(CoopNpcWorld world,int count=2) {for(int i=0;i<count;i++){Time.unscaledTime+=.3f;Time.time+=.3f;world.Tick();}}

    private static void Main()
    {
        var owner=new ModBehaviour();var world=new CoopNpcWorld(owner);var host=Player("host","host-asset");CharacterMainControl.Main=host.Character;
        var publicNpc=new GameObject("public goblin");CoopNpcWorld.Track("goblin","goblin",publicNpc);
        var players=new List<BossRushCoopPlayer>();
        for(int i=0;i<16;i++)players.Add(Player("P"+i,"A"+i));
        foreach(var p in players)Send(p,Intent(p,"goblin"));
        Tick(world);
        Check(BossRushCoop.Subscribers==1,"one disconnect owner subscribed");
        var bodies=new HashSet<GameObject>();
        foreach(var p in players)
        {
            GameObject npc=Spouse(p,"goblin");Check(npc!=null && bodies.Add(npc),"all 16 owners have distinct spouse bodies");
            Check(npc.GetComponent<GoblinMovement>().Target==p.Character.transform,"each spouse follows its actual owner");
            Check(npc.GetComponent<GoblinInteractable>()!=null,"each independent spouse retains original service shell");
        }
        Check(CoopNpcWorld.FindObject("goblin")==publicNpc && publicNpc!=null,"personal spouses cannot replace public NPC");
        int creates=Probe.Creates;
        foreach(var p in players)Send(p,Intent(p,"goblin"));Tick(world);
        Check(Probe.Creates==creates,"same-sequence retry never creates another body");
        GameObject idle=Spouse(players[0],"goblin");Send(players[0],Intent(players[0],"goblin",2,true,false));Tick(world);
        Check(idle.GetComponent<GoblinMovement>().Target==null && !idle.GetComponent<GoblinMovement>().enabled && idle.GetComponent<WeddingNpcResidentMarker>()!=null,"return home affects only personal body and stops wandering");
        Check(idle.transform.position.x==10,"return home uses personal chapel position");
        Send(players[0],Intent(players[0],"goblin",1,true,true));Tick(world);
        Check(idle.GetComponent<GoblinMovement>().Target==null,"older follow intent cannot override newer return home");
        int refreshed=idle.GetComponent<GoblinInteractable>().Refreshes;Tick(world,3);
        Check(idle.GetComponent<GoblinInteractable>().Refreshes==refreshed,"repeated projection does not reapply presentation");
        Send(players[1],Intent(players[1],"goblin",2,false,false));Tick(world);
        Check(Spouse(players[1],"goblin")==null && Spouse(players[2],"goblin")!=null,"divorce/remove releases only requesting owner");
        GameObject prior=Spouse(players[2],"goblin");players[2].AssetId="B2";Tick(world);
        Check(prior==null,"slot switch retires old asset's body");
        var forged=Intent(players[2],"nurse");forged.Asset="A2";Send(players[2],forged);Tick(world);
        Check(Spouse(players[2],"nurse")==null,"old asset request cannot deploy into current slot");
        Send(players[2],Intent(players[2],"nurse"));Tick(world);
        Check(Spouse(players[2],"nurse")!=null && Spouse(players[3],"goblin")!=null,"new slot receives independent current spouse");
        var saved=players[3].Character;GameObject companion=Spouse(players[3],"goblin");players[3].IsAlive=false;Tick(world);
        Check(companion.GetComponent<GoblinMovement>().Target==null,"dead owner suspends personal movement");
        players[3].Character=new GameObject("respawn").AddComponent<CharacterMainControl>();players[3].IsAlive=true;Tick(world);
        Check(companion.GetComponent<GoblinMovement>().Target==players[3].Character.transform,"respawn rebinds personal follower");
        Send(players[4],Intent(players[4],"goblin",99));Tick(world);
        BossRushCoop.Disconnect(players[4].Id);Check(Spouse(players[4],"goblin")==null,"disconnect releases only disconnected owner");
        players[4].IsConnected=true;Send(players[4],Intent(players[4],"goblin",1));Tick(world);
        Check(Spouse(players[4],"goblin")!=null,"same scene reconnect can restart local intent sequence");
        AsyncReplacement(world,players[5]);
        Send(players[6],Intent(players[6],"duck",2));Tick(world);
        Check(Spouse(players[6],"duck")!=null && Probe.Attaches>0 && Probe.SpawnsWithTrack==0 && Probe.RegistryWrites==0,"permanent spouse has original services without overwriting public named registry");
        AffinityManager.Spouse="goblin";AffinityManager.Following=false;Tick(world);
        GameObject local=CoopNpcWorld.FindLocalSpouse("goblin");Check(local!=null && local!=publicNpc,"host also receives personal chapel body");
        CoopNpcWorld.DeferLocalSpouseRelease(2.8f);AffinityManager.Spouse=null;Tick(world,3);
        Check(CoopNpcWorld.FindLocalSpouse("goblin")==local,"divorce dialogue retains own body during original delay");
        Tick(world,10);Check(local==null && publicNpc!=null && Spouse(players[3],"goblin")!=null,"divorce grace ends without deleting public or other spouse");
        var projection=(CoopNpcWorld.State)BossRushCoop.Broadcasts.Last();
        Check(projection.Npcs.Count(e=>!string.IsNullOrEmpty(e.Owner))==15,"world snapshot retains all remaining independent owners");
        world.Dispose();Check(BossRushCoop.Subscribers==0,"dispose unsubscribes disconnect owner");

        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.Broadcasts.Clear();
        var guest=new CoopNpcWorld(owner);Tick(guest,1);
        BossRushCoop.Channels["npc.world"](new BossRushCoopMessage {IsFromHost=true,Payload=projection});Tick(guest);
        foreach(var entry in projection.Npcs.Where(e=>!string.IsNullOrEmpty(e.Owner)))
        {var npc=CoopNpcWorld.FindObject(entry.Id);Check(npc!=null,"late join restores each independent spouse");Check(!npc.GetComponent<NPCFollowMovementBase>()?.enabled ?? !npc.GetComponent<DuckNpcMovement>().enabled,"guest movement is suppressed");}
        creates=Probe.Creates;Tick(guest,3);Check(Probe.Creates==creates,"late join repeated state is idempotent");
        BossRushCoop.LocalPlayerId=players[0].Id;BossRushCoop.LocalAssetId=players[0].AssetId;
        Check(CoopNpcWorld.FindLocalSpouse("goblin")==CoopNpcWorld.FindObject(CoopNpcWorld.SpouseWorldId(players[0].Id,players[0].AssetId,"goblin")),"personal lookup resolves own composite");
        GameObject oldLocal=CoopNpcWorld.FindLocalSpouse("goblin");BossRushCoop.LocalAssetId="other-slot";
        Check(CoopNpcWorld.FindLocalSpouse("goblin")==null && oldLocal!=null,"switching asset does not borrow another slot's spouse");
        var removal=new CoopNpcWorld.State {RunId=projection.RunId,Generation=projection.Generation,Sequence=projection.Sequence+1,Npcs=new CoopNpcWorld.Entry[0]};
        BossRushCoop.Channels["npc.world"](new BossRushCoopMessage {IsFromHost=true,Payload=removal});Tick(guest);
        Check(oldLocal==null,"current authoritative removal retires the original guest spouse component tree");
        creates=Probe.Creates;
        BossRushCoop.Channels["npc.world"](new BossRushCoopMessage {IsFromHost=true,Payload=projection});Tick(guest);
        Check(Probe.Creates==creates && CoopNpcWorld.FindObject(CoopNpcWorld.SpouseWorldId(players[0].Id,players[0].AssetId,"goblin"))==null,"delayed pre-divorce world snapshot cannot recreate a removed spouse");
        BossRushCoop.SceneGeneration++;Tick(guest);
        foreach(var entry in projection.Npcs.Where(e=>!string.IsNullOrEmpty(e.Owner)))Check(CoopNpcWorld.FindObject(entry.Id)==null,"scene generation clears all old spouse mirrors");
        guest.Dispose();Check(BossRushCoop.Subscribers==0,"guest owner cleanup unsubscribes");
        Check(CoopNpcWorld.SpouseWorldId("a:b","c","npc")!=CoopNpcWorld.SpouseWorldId("a","b:c","npc"),"length encoded world keys cannot alias participants/assets");
        Console.WriteLine("CoopSpouses PASS: "+checks+" assertions; 16 owners, own slot, independent services/home/follow/divorce, retries, stale sequence, disconnect/reconnect, async replacement, late join and scene cleanup. No live transport/game/save is exercised.");
    }

    private static void AsyncReplacement(CoopNpcWorld world,BossRushCoopPlayer p)
    {
        Probe.DuckGate=new TaskCompletionSource<CharacterMainControl>();Send(p,Intent(p,"duck",2));Tick(world,1);
        Send(p,Intent(p,"nurse",3));Tick(world);
        GameObject replacement=Spouse(p,"nurse");Check(replacement!=null,"replacement deploys while old spouse factory is awaiting");
        var obsolete=new GameObject("obsolete duck").AddComponent<CharacterMainControl>();Probe.DuckGate.SetResult(obsolete);
        foreach(Task pending in Probe.Tasks)pending.GetAwaiter().GetResult();Probe.Tasks.Clear();Probe.DuckGate=null;
        Check(obsolete==null && Spouse(p,"nurse")==replacement,"stale async result cleans itself without removing replacement");
        Check(Spouse(p,"duck")==null,"obsolete factory cannot register stale world body");
    }
}

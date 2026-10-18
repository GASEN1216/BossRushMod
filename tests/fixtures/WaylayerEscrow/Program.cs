using System;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using UnityEngine;

internal static partial class Program
{
    private static int checks;
    private static CoopWaylayerEscrow host,guest;
    private static CharacterMainControl hostPlayer,guestPlayer,boss;
    private static SkyIslandWaylayerChief chief;
    private static BossRushCoopPlayer member;
    private static void Check(bool value,string reason) {if(!value)throw new Exception(reason);checks++;}
    private static CharacterMainControl Player() {return new CharacterMainControl {CharacterItem=new Item {Inventory=new Inventory()}};}
    private static void Host() {Time.frameCount++;BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;BossRushCoop.LocalPlayerId="host";BossRushCoop.LocalAssetId="host-slot";CharacterMainControl.Main=hostPlayer;Saves.SavesSystem.CurrentSlot=1;Context(host);}
    private static void Guest(string asset="guest-slot") {Time.frameCount++;BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.LocalPlayerId="guest";BossRushCoop.LocalAssetId=asset;CharacterMainControl.Main=guestPlayer;Saves.SavesSystem.CurrentSlot=asset=="other-slot"?3:2;Context(guest);}
    private static readonly Action<CoopWaylayerClaims> setClaimsOwner=(Action<CoopWaylayerClaims>)Delegate.CreateDelegate(typeof(Action<CoopWaylayerClaims>),typeof(CoopWaylayerClaims).GetProperty("Current",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetSetMethod(true));
    private static void Context(CoopWaylayerEscrow escrow) {if(escrow!=null)setClaimsOwner((CoopWaylayerClaims)typeof(CoopWaylayerEscrow).GetField("claims",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(escrow));}
    private static void Setup(bool restore=false)
    {
        if(guest!=null){Guest();guest.Dispose();}if(host!=null){Host();host.Dispose();}
        Saves.SavesSystem.Reset();
        Duckov.UI.NotificationText.Notices.Clear();
        BossRushCoop.Players.Clear();BossRushCoop.Entities.Clear();BossRushCoop.Sent.Clear();BossRushCoop.Handlers.Clear();
        CoopPersonalDelivery<CoopWaylayerJournal.Commit>.Queue.Clear();CoopPersonalDelivery<CoopWaylayerJournal.Commit>.Apply.Clear();
        CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Clear();CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Apply.Clear();
        CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.RejectDelivery=false;
        ItemTreeData.Deferred=null;ItemTreeData.FactoryCalls=0;ItemTreeData.Fail=false;ItemUtilities.RejectDrop=ItemUtilities.RejectSend=false;
        ItemAssetsCollection.Prefabs[5001]=new Item {TypeID=5001};ItemAssetsCollection.Prefabs[5002]=new Item {TypeID=5002};
        BossRushCoop.RunId="room";BossRushCoop.SceneGeneration=2;BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="scene-2";hostPlayer=Player();guestPlayer=Player();boss=Player();chief=new SkyIslandWaylayerChief();
        BossRushCoop.Players["host"]=new BossRushCoopPlayer {Id="host",AssetId="host-slot",Character=hostPlayer,IsLocal=true};
        member=new BossRushCoopPlayer {Id="guest",AssetId="guest-slot",Character=guestPlayer};BossRushCoop.Players["guest"]=member;BossRushCoop.Entities["boss"]=boss;
        Host();if(restore)Saves.SavesSystem.Reload();host=new CoopWaylayerEscrow();Guest();if(restore)Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();Handshake();
    }
    private static void Handshake()
    {
        Guest();guest.Tick(1);var hello=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.identity").Value;
        Host();BossRushCoop.Receive("host","sky.waylayer.identity",hello,"guest",false);
        var answer=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.identity").Value;
        Guest();BossRushCoop.Receive("guest","sky.waylayer.identity",answer,"host",true);
    }
    private static Item Add(CharacterMainControl player,int type,int count,string metadata)
    {var item=new Item {TypeID=type,StackCount=count,Metadata=metadata};player.CharacterItem.Inventory.AddItem(item);return item;}
    private static CoopWaylayerEscrow.Packet Start()
    {
        Host();Check(host.Begin(chief,guestPlayer,boss),"host freezes a connected target and its asset");host.Tick(1);
        var packet=(CoopWaylayerEscrow.Packet)BossRushCoop.Sent.Last(p=>p.Topic=="sky.waylayer.reserve").Value;
        Guest();BossRushCoop.Receive("guest","sky.waylayer.reserve",packet,"host",true);return packet;
    }
    private static CoopWaylayerEscrow.Packet Response() {return (CoopWaylayerEscrow.Packet)BossRushCoop.Sent.Last(p=>p.Topic=="sky.waylayer.reserved").Value;}
    private static void Respond(CoopWaylayerEscrow.Packet response)
    {Host();BossRushCoop.Receive("host","sky.waylayer.reserved",response,"guest",false);}
    private static bool Apply(CoopWaylayerEscrow.Decision decision)
    {
        bool applied=CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Apply["guest"](decision);
        // A terminal decision may persist its journal in this frame. Its private refund
        // publication retries on a later simulated frame, using the same actual callback.
        if(!applied&&!decision.Success){Time.frameCount++;applied=CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Apply["guest"](decision);}
        return applied;
    }
    private static CoopWaylayerEscrow.Decision Decision() {return CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Last().Value;}
    private static async Task Main(string[] args)
    {
        try {
#if WAYLAYER_HARMONY_FIXTURE
            var claimsHarmony=new HarmonyLib.Harmony("BossRush.Tests.WaylayerClaims");
            foreach(var hook in new[]{typeof(CoopWaylayerClaimAddAtHook),typeof(CoopWaylayerClaimAddItemHook),typeof(CoopWaylayerClaimPlugHook),typeof(CoopWaylayerClaimCombineHook),typeof(CoopWaylayerClaimMergeHook),typeof(CoopWaylayerClaimPickupHook),typeof(CoopWaylayerClaimDetachHook),typeof(CoopWaylayerClaimDropHook)})claimsHarmony.CreateClassProcessor(hook).Patch();
#endif
            if(args.Length>0) {CrashProcess(args);return;}
            await MainBody(); await ClaimCases(); await PublicationCases(); await CrashCases();Console.WriteLine("WaylayerEscrowFinal: PASS "+checks+" assertions including independent-process crashes."); }
        catch(Exception error)
        {
            Console.WriteLine("FAIL TYPE: "+error.GetType().FullName);
            Console.WriteLine("FAIL MESSAGE: "+error.Message);
            Console.WriteLine("FAIL STACK: "+error.StackTrace);
            Environment.ExitCode=1;
        }
    }
    private static async Task MainBody()
    {
        Setup();var mine=Add(hostPlayer,5002,7,"host");var stack=Add(guestPlayer,5002,3,"frozen guest metadata");var cheaper=Add(guestPlayer,5001,2,"cheap");
        var packet=Start();Check(stack.StackCount==2&&mine.StackCount==7&&cheaper.StackCount==2,"only selected guest loses one highest value consumable");
        var response=Response();BossRushCoop.Receive("guest","sky.waylayer.reserve",packet,"host",true);
        Check(stack.StackCount==2&&Response().Tree.RootData.StackCount==1,"repeated reserve reuses escrow snapshot without a second decrement");
        Respond(response);Respond(response);
        Check(chief.Snatches==1&&chief.Bag.Content.Count(i=>i!=null)==1&&ItemTreeData.FactoryCalls==1,"duplicate reserved replies create one stolen world item and one snatch");
        Check(chief.Bag.GetItemAt(0).Metadata=="frozen guest metadata","world item retains frozen source metadata");
        Guest();Check(Apply(Decision())&&Apply(Decision())&&stack.StackCount==2,"committed personal decision is idempotent");
        BossRushCoop.Receive("guest","sky.waylayer.reserve",packet,"host",true);Check(stack.StackCount==2,"late reserve cannot reopen a settled transaction");
        int queued=CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Count;Host();
        for(int generation=3;generation<30;generation++){BossRushCoop.SceneGeneration=generation;host.Tick(1);host.Tick(1);}
        Check(CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Count==queued,"completed steal registers its personal receipt once across arbitrarily many scene generations; delivery owns retries");

        Setup();stack=Add(guestPlayer,5002,3,"refund");Start();ItemTreeData.Fail=true;Respond(Response());
        Check(!Decision().Success&&chief.Snatches==0,"factory failure reports rejection without gameplay count");
        Guest();Check(Apply(Decision())&&Apply(Decision())&&stack.StackCount==3,"failed async materialization restores exactly one consumed unit");

        Setup();stack=Add(guestPlayer,5002,4,"asset freeze");Start();response=Response();
        ItemTreeData.Deferred=new TaskCompletionSource<Item>();Respond(response);member.AssetId="other-slot";var stale=ItemTreeData.Clone(response.Tree);
        ItemTreeData.Deferred.SetResult(stale);await Task.Yield();
        Check(!Decision().Success&&stale.IsBeingDestroyed&&chief.Snatches==0,"asset changed during factory await cancels and destroys late world item");
        Guest("other-slot");Check(!Apply(Decision())&&stack.StackCount==3,"receipt cannot refund into a different personal asset");
        Guest();member.AssetId="guest-slot";Check(Apply(Decision())&&stack.StackCount==4,"original asset receives its refund when it returns");

        Setup();var whole=Add(guestPlayer,5002,1,"whole exact object");Start();response=Response();
        Check(whole.InInventory==null&&!whole.IsBeingDestroyed,"single item remains physically held in local escrow");
        ItemTreeData.Deferred=new TaskCompletionSource<Item>();Respond(response);UnityEngine.Object.Destroy(chief);var late=ItemTreeData.Clone(response.Tree);
        ItemTreeData.Deferred.SetResult(late);await Task.Yield();Check(!Decision().Success&&late.IsBeingDestroyed,"destroyed chief rejects factory continuation");
        Guest();guestPlayer.gameObject.DestroyTree();Check(guestPlayer==null&&guestPlayer.Health==null,"scene destruction applies Unity component null semantics");
        guestPlayer=Player();member.Character=guestPlayer;CharacterMainControl.Main=guestPlayer;
        Check(Apply(Decision())&&ReferenceEquals(whole,guestPlayer.CharacterItem.Inventory.GetItemAt(0)),"failed steal returns original single item to respawned owner inventory");

        Setup();whole=Add(guestPlayer,5002,1,"retry refund");Start();ItemTreeData.Fail=true;Respond(Response());Guest();
        guestPlayer.CharacterItem.Inventory.Reject=true;ItemUtilities.RejectSend=ItemUtilities.RejectDrop=true;
        Check(!Apply(Decision())&&whole.InInventory==null&&!whole.IsBeingDestroyed,"failed refund retains physical escrow for later delivery");
        guestPlayer.CharacterItem.Inventory.Reject=false;ItemUtilities.RejectSend=ItemUtilities.RejectDrop=false;
        Check(Apply(Decision())&&Apply(Decision())&&guestPlayer.CharacterItem.Inventory.Content.Count(i=>i!=null)==1,"refund retry settles once after a real inventory adoption");

        Setup();stack=Add(guestPlayer,5002,3,"notification");stack.ThrowAfterCount=true;chief.Bag.ThrowAfterAdd=true;chief.ThrowEffect=true;
        Start();Respond(Response());Check(Decision().Success&&chief.Snatches==1&&chief.Bag.GetItemAt(0)!=null,"post adoption notifications and effects cannot turn committed steal into refund");
        Guest();Check(Apply(Decision())&&stack.StackCount==2,"post mutation count notification still consumes only one unit");

        Setup();stack=Add(guestPlayer,5002,3,"new scene");Start();response=Response();ItemTreeData.Deferred=new TaskCompletionSource<Item>();Respond(response);
        BossRushCoop.SceneGeneration++;late=ItemTreeData.Clone(response.Tree);ItemTreeData.Deferred.SetResult(late);await Task.Yield();
        Check(!Decision().Success&&late.IsBeingDestroyed,"same map reload generation rejects old factory continuation");Guest();Check(Apply(Decision())&&stack.StackCount==3,"scene cancellation restores original quantity");

        Setup();stack=Add(guestPlayer,5002,3,"new room");Start();BossRushCoop.RunId="next-room";Guest("other-slot");guest.Tick(1);
        Check(stack.StackCount==2,"room cancellation preserves obligation while original asset is unavailable");Guest();BossRushCoop.LocalPlayerId="new-room-peer";guest.Tick(1);guest.Tick(1);
        Check(stack.StackCount==2,"unknown old room commit remains escrowed, never blindly refunded");
        await JournalCases();
#if WAYLAYER_HARMONY_FIXTURE
        await HookCases();
#endif
        Guest();guest.Dispose();Host();host.Dispose();Check(BossRushCoop.Handlers.Count==0,"escrow owner disposes all protocol subscriptions");
        Console.WriteLine("WaylayerEscrow: PASS "+checks+" production assertions; transport, Unity and item factory are controlled boundaries.");
    }
}

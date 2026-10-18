using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BossRush;
using EscapeFromDuckovCoopMod;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static Peer host,first,second;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void In(Peer peer,Action action){var saved=BossRushCoop.Current;BossRushCoop.Use(peer);try{action();}finally{BossRushCoop.Use(saved);}}
    private static Item Ground(){var item=new Item();item.AgentUtilities.ActiveAgent=new DuckovItemAgent();return item;}
    private static void Setup()
    {
        foreach(var peer in BossRushCoop.Peers.ToArray())In(peer,()=>peer.Module.Dispose());
        BossRushCoop.Peers.Clear();BossRushCoop.Wire.Clear();BossRushCoop.Connected=true;BossRushCoop.DropAck=false;BossRushCoop.DropResults=false;
        ItemNet.InNetworkDrop=false;CoopTool.PickupNotificationExceptions=0;ItemUtilities.OnPlayerItemOperation=null;ItemUtilities.OnItemSentToPlayerInventory=null;
        host=new Peer{Id="host",Host=true};first=new Peer{Id="first"};second=new Peer{Id="second"};
        BossRushCoop.Peers.AddRange(new[]{host,first,second});
        foreach(var peer in BossRushCoop.Peers)
        {
            BossRushCoop.Use(peer);peer.Module=new ZombieModeRuntimeModule();ZombieModeRuntimeModule.Select(peer.Module);
            foreach(var p in BossRushCoop.Players)peer.Module.Admit(p);
        }
        BossRushCoop.Use(host);
    }
    private static Item[] Shared(int count=1,int maximum=1,bool clientSnapshotConnected=true)
    {
        var items=new[]{Ground(),Ground(),Ground()};var data=new Dictionary<string,string>();
        foreach(var item in items){item.MaxStackCount=maximum;item.StackCount=count;}
        In(host,()=>host.Module.Mark(items[0]));
        for(int i=0;i<items.Length;i++){int index=i;In(BossRushCoop.Peers[index],()=>{var entry=CoopSyncDatabase.Drops.Register(55,items[index],items[index].ActiveAgent.gameObject);Check(ReferenceEquals(entry.Agent,items[index].ActiveAgent.gameObject),"actual native registry retains the original agent");});}
        In(host,()=>BossRushCoop.Capture(items[0],data));
        Check(data.Count>0,"host marks the actual shared item before native snapshot serialization");
        BossRushCoop.Connected=clientSnapshotConnected;
        try{In(first,()=>BossRushCoop.Restore(items[1],data));In(second,()=>BossRushCoop.Restore(items[2],data));}
        finally{BossRushCoop.Connected=true;}
        return items;
    }
    private static int Added(Peer peer){return peer.Body.CharacterItem.Inventory.AddCalls;}
    private static void Pickup(Peer peer,Item item){In(peer,()=>peer.Body.PickupItem(item));}
    private static void MergeTarget(Peer peer,Item source,int existing=4,int maximum=5)
    {
        source.MaxStackCount=maximum;var target=new Item{MaxStackCount=maximum,TypeID=source.TypeID};target.StackCount=existing;
        peer.Body.CharacterItem.Inventory.AddAt(target,0);
    }
    private static void TickAll(){foreach(var peer in BossRushCoop.Peers)In(peer,()=>peer.Module.Tick());BossRushCoop.Flush();}
    private static void ConcurrentAndHost()
    {
        Setup();var items=Shared();Pickup(first,items[1]);Pickup(second,items[2]);
        Check(Added(first)==0&&Added(second)==0&&first.NativePickups==0&&second.NativePickups==0,"both guests must wait for host authorization before any official inventory mutation");
        // 两个请求先到房主，再让房主自己的手动拾取竞争同一尚存的地面对象。
        BossRushCoop.Wire.Dequeue()();BossRushCoop.Wire.Dequeue()();Pickup(host,items[0]);
        BossRushCoop.Flush();
        Check(Added(first)+Added(second)==1&&first.NativePickups+second.NativePickups==1,"two concurrent guests commit one original shared item and one native pickup notification");
        TickAll();
        Check(Added(host)==0&&Added(first)+Added(second)==1,"host cannot take an already claimed guest item");
        Check(host.NativeDrops.Count==0&&items[0].ActiveAgent==null,"actual native server handler retires the authoritative drop registry and pickup agent registry="+host.NativeDrops.Count+" active="+(items[0].ActiveAgent!=null)+" despawns="+host.NativeDespawns+" destroys="+UnityEngine.Object.Destructions);
        Setup();items=Shared();Pickup(host,items[0]);Pickup(first,items[1]);BossRushCoop.Flush();
        Check(Added(host)==1&&Added(first)==0&&host.NativePickups==1,"host's own pickup uses the same serial claim and bypasses the official method exactly once");
    }
    private static void ReliableAndIdentity()
    {
        Setup();var items=Shared();BossRushCoop.DropAck=true;Pickup(first,items[1]);BossRushCoop.Flush();TickAll();TickAll();
        Check(Added(first)==1&&first.NativePickups==1,"lost grant ACK and reliable retries cannot repeat inventory addition or native pickup notification");
        BossRushCoop.DropAck=false;TickAll();
        Setup();items=Shared();Pickup(first,items[1]);BossRushCoop.Wire.Dequeue()();
        first.Asset="slot-B";BossRushCoop.Flush();
        Check(Added(first)==0,"late authorization cannot enter a replacement save asset");
        Pickup(second,items[2]);BossRushCoop.Flush();TickAll();
        Check(Added(second)==0,"unconfirmed old-asset grant is not reassigned by a timer");
        first.Asset="slot-A";TickAll();Check(Added(first)==1,"returning original asset can finish its one frozen authorization");
        Setup();items=Shared();Pickup(first,items[1]);BossRushCoop.Wire.Dequeue()();first.Connected=false;BossRushCoop.Flush();
        Pickup(second,items[2]);BossRushCoop.Flush();TickAll();Check(Added(second)==0,"disconnect after authorization cannot make an unknown result eligible for another claimant");
        first.Connected=true;TickAll();Check(Added(first)==1,"original participant reconnect finishes at most one authorized pickup");
    }
    private static void FailureAndNativeDrop()
    {
        Setup();var items=Shared();items[1].Slots=new Slots{Master=items[1]};first.Body.CharacterItem.Inventory.Accept=false;Pickup(first,items[1]);BossRushCoop.Flush();
        Check(Added(first)==1&&items[1].GroundDrops==1&&first.NativeDropRequests==0&&first.NativePickups==0,"explicit official pickup failure recreates one local agent without a duplicate native Drop request");
        Check(!ItemNet.InNetworkDrop,"temporary native InNetworkDrop guard is restored after ordinary failure");
        Pickup(second,items[2]);BossRushCoop.Flush();
        Check(Added(second)==1&&second.NativePickups==1,"host releases only an explicitly uncommitted failure for the next real claimant");
        Setup();items=Shared();first.Body.CharacterItem.Inventory.Accept=false;items[1].ThrowOnCreateAgent=true;Pickup(first,items[1]);BossRushCoop.Flush();
        Check(!ItemNet.InNetworkDrop,"native agent creation exception still restores InNetworkDrop in finally");
        Pickup(second,items[2]);BossRushCoop.Flush();TickAll();Check(Added(second)==0,"ambiguous exception during official pickup is not reported as false and reassigned");
        Setup();items=Shared();first.Body.CharacterItem.Inventory.Accept=false;ItemNet.InNetworkDrop=true;Pickup(first,items[1]);BossRushCoop.Flush();
        Check(ItemNet.InNetworkDrop,"nested native network-drop guard preserves its previous true value");ItemNet.InNetworkDrop=false;
    }
    private static void PostCommitExceptions()
    {
        Setup();var items=Shared();object frozen=null;In(host,()=>frozen=BossRushCoopTransport.CaptureNetworkDrop(items[0]));
        ItemUtilities.OnPlayerItemOperation=()=>{throw new InvalidOperationException("postcommit inventory event");};
        Pickup(first,items[1]);BossRushCoop.Flush();ItemUtilities.OnPlayerItemOperation=null;TickAll();Pickup(second,items[2]);BossRushCoop.Flush();
        Check(items[1]!=null&&Added(first)==1&&items[1].InInventory==first.Body.CharacterItem.Inventory&&first.Body.CharacterItem.Inventory.Items.Count==1&&Added(second)==0,"actual official postcommit notification exception cannot duplicate or destroy an already added item");
        In(host,()=>{Check(BossRushCoopTransport.CompleteNetworkDrop(frozen)&&BossRushCoopTransport.CompleteNetworkDrop(frozen),"repeat native despawn uses the same frozen ID idempotently");});BossRushCoop.Flush();
        Check(items[1]!=null&&first.Body.CharacterItem.Inventory.Items.Count==1&&host.NativeDrops.Count==0&&second.NativeDrops.Count==0,"repeated actual Client_HandleDespawn preserves the committed inventory item and clears every ground replica");
        Setup();items=Shared();MergeTarget(first,items[1]);ItemUtilities.OnPlayerItemOperation=()=>{throw new InvalidOperationException("postcommit merged event");};
        Pickup(first,items[1]);BossRushCoop.Flush();ItemUtilities.OnPlayerItemOperation=null;TickAll();Pickup(second,items[2]);BossRushCoop.Flush();
        Check(items[1]==null&&!ReferenceEquals(items[1],null)&&Added(first)==1&&first.Body.CharacterItem.Inventory.Quantity==5&&Added(second)==0,"merged and Unity-destroyed item remains committed after the official notification throws");
        Check(host.NativeDrops.Count==0&&first.NativeDrops.Count==0&&second.NativeDrops.Count==0&&items[0].ActiveAgent==null,"guest merged item with skipped postfix still retires the actual native registry and all world agents");
        Setup();items=Shared();MergeTarget(host,items[0]);Pickup(host,items[0]);BossRushCoop.Flush();
        Check(items[0]==null&&Added(host)==1&&host.Body.CharacterItem.Inventory.Quantity==5&&host.NativeDrops.Count==0&&first.NativeDrops.Count==0&&second.NativeDrops.Count==0&&host.NullPeerServerRequests>0,"host merge-to-Unity-null also uses the frozen native DropId and null-peer server context to retire every replica");
        Setup();items=Shared();first.Body.CharacterItem.AcceptPlug=true;first.Body.CharacterItem.ThrowPlugAfter=true;
        Pickup(first,items[1]);BossRushCoop.Flush();TickAll();Pickup(second,items[2]);BossRushCoop.Flush();
        Check(items[1]!=null&&items[1].PluggedIntoSlot!=null&&Added(first)==0&&Added(second)==0,"equipped item remains alive and committed when TryPlug throws after its slot mutation");
        Setup();items=Shared();CoopTool.PickupNotificationExceptions=1;Pickup(first,items[1]);BossRushCoop.Flush();TickAll();
        Check(items[1]!=null&&Added(first)==1&&first.Body.CharacterItem.Inventory.Items.Count==1&&host.NativeDrops.Count==0&&second.NativeDrops.Count==0,"native pickup postfix exception after its send keeps one actual inventory item and retires all replicas");
        Setup();items=Shared();first.Body.CharacterItem.Inventory.ThrowBefore=true;
        Pickup(first,items[1]);BossRushCoop.Flush();TickAll();Pickup(second,items[2]);BossRushCoop.Flush();
        Check(Added(first)==1&&Added(second)==0,"unproven precommit exception conservatively retains the original claim without repeating native mutation");
    }
    private static void ScopeAndCleanup()
    {
        Setup();var raw=Ground();var data=new Dictionary<string,string>();BossRushCoop.Capture(raw,data);int custom=BossRushCoop.CustomMessages;In(first,()=>CoopSyncDatabase.Drops.Register(99,raw,raw.ActiveAgent.gameObject));
        Pickup(first,raw);Check(data.Count==0&&Added(first)==1&&first.NativePickups==1&&BossRushCoop.CustomMessages==custom,"ordinary unmarked item follows official pickup directly with no custom protocol or snapshot metadata");
        Setup();var items=Shared();Pickup(first,items[1]);BossRushCoop.Wire.Dequeue()();first.Generation++;BossRushCoop.Flush();
        Check(Added(first)==0,"old scene generation authorization cannot mutate the new scene inventory");
        Setup();items=Shared();Pickup(first,items[1]);BossRushCoop.Wire.Dequeue()();first.Room="new-room";BossRushCoop.Flush();
        Check(Added(first)==0,"old room authorization cannot mutate the new room inventory");
        Setup();items=Shared();Pickup(first,items[1]);BossRushCoop.Wire.Dequeue()();In(first,()=>first.Module.Dispose());BossRushCoop.Flush();
        Check(Added(first)==0,"cleanup cancels an already in-flight grant before official pickup");
        foreach(var peer in BossRushCoop.Peers)In(peer,()=>{peer.Module.Dispose();Check(BossRushCoop.SnapshotSubscriptions==0&&peer.Handlers.Values.All(x=>x.Count==0),"cleanup removes snapshot, request, result, receipt and ACK subscriptions");});
    }
    private static void MetadataBeforeWorld()
    {
        foreach(bool adapterConnected in new[]{true,false})
        {
            Setup();first.Module.runState.RunId=0;second.Module.runState.RunId=0;var items=Shared(clientSnapshotConnected:adapterConnected);
            Pickup(first,items[1]);Pickup(second,items[2]);
            Check(Added(first)==0&&Added(second)==0&&BossRushCoop.Wire.Count==0,"native ItemSpawn before current-run initialization stays gated even when metadata is present, adapter="+adapterConnected);
            In(first,()=>first.Module.BindRun(11));In(second,()=>second.Module.BindRun(11));
            Pickup(first,items[1]);Pickup(second,items[2]);BossRushCoop.Flush();
            Check(Added(first)+Added(second)==1&&host.NativeDrops.Count==0,"metadata received at Run=0 survives the real new-run reset and serializes two later guest pickups, adapter="+adapterConnected);
        }
    }
    private static void FailedAttemptReplay()
    {
        Setup();var items=Shared();first.Body.CharacterItem.Inventory.Accept=false;BossRushCoop.DropAck=true;Pickup(first,items[1]);
        var oldRequest=BossRushCoop.Wire.Peek();BossRushCoop.Wire.Dequeue()();var oldGrant=BossRushCoop.Wire.Peek();BossRushCoop.Flush();
        Check(Added(first)==1&&first.Module.PendingResults==1,"explicit failure with a lost result ACK retains only its reliable result");
        oldRequest();BossRushCoop.Flush();Pickup(second,items[2]);BossRushCoop.Flush();
        Check(Added(first)==1&&Added(second)==1,"old finalized failure request cannot relock the drop before another participant claims it");
        oldGrant();BossRushCoop.Flush();BossRushCoop.DropAck=false;TickAll();
        Check(Added(first)==1&&Added(second)==1&&first.Module.PendingResults==0,"late old grant and failure-result retry neither repeat PickupItem nor unlock the committed winner");
        Setup();items=Shared();first.Body.CharacterItem.Inventory.Accept=false;BossRushCoop.DropResults=true;Pickup(first,items[1]);BossRushCoop.Flush();
        Pickup(second,items[2]);BossRushCoop.Flush();Check(Added(second)==0,"lost explicit-failure result remains reserved until reliable host receipt");
        BossRushCoop.DropResults=false;TickAll();Pickup(second,items[2]);BossRushCoop.Flush();
        Check(Added(first)==1&&Added(second)==1,"reliable failure-result retry releases the original item without rerunning the first pickup");
    }
    private static void Retirement()
    {
        Setup();var items=Shared();In(first,()=>first.Module.BindRun(12));Pickup(first,items[1]);
        Check(items[1]==null&&Added(first)==0,"guest new-run reset destroys its previous ground item before clearing the marker");
        Setup();items=Shared();In(host,()=>host.Module.BindRun(12));BossRushCoop.Flush();
        Pickup(first,items[1]);Pickup(second,items[2]);BossRushCoop.Flush();
        Check(items[0]==null&&items[1].ActiveAgent==null&&items[2].ActiveAgent==null&&host.NativeDrops.Count==0&&first.NativeDrops.Count==0&&second.NativeDrops.Count==0&&Added(first)==0&&Added(second)==0,
            "host new-run reset uses the frozen native lease to retire every unclaimed world agent and registry");
        Setup();items=Shared();In(first,()=>first.Module.Dispose());Pickup(first,items[1]);
        Check(items[1]==null&&Added(first)==0,"cleanup destroys local unclaimed drops so an unpatched original pickup cannot salvage them");
        Setup();items=Shared();Pickup(first,items[1]);BossRushCoop.Flush();In(first,()=>first.Module.Dispose());
        Check(items[1]!=null&&items[1].InInventory==first.Body.CharacterItem.Inventory&&first.Body.CharacterItem.Inventory.Quantity==1,
            "cleanup preserves the actual live inventory item already committed to its winner");
    }
    private static void PartialMergeRemainder()
    {
        foreach(bool hostTakesFirst in new[]{false,true})
        {
            Setup();var items=Shared(5,10);var winner=hostTakesFirst?host:first;int winnerIndex=hostTakesFirst?0:1;
            MergeTarget(winner,items[winnerIndex],7,10);winner.Body.CharacterItem.Inventory.Accept=false;
            BossRushCoop.DropAck=true;Pickup(winner,items[winnerIndex]);BossRushCoop.Flush();TickAll();
            Check(Added(winner)==1&&winner.Body.CharacterItem.Inventory.Quantity==10,"actual AddAndMerge partially combines 3 units before its full inventory returns false, host="+hostTakesFirst);
            Check(second.SpawnedItems.Count==1&&first.SpawnedItems.Count==1&&second.SpawnedItems[0].StackCount==2,
                "partial commit replaces the old native drop with exactly one marked 2-unit remainder snapshot, host="+hostTakesFirst);
            Check(host.NativeDrops.Count==1&&first.NativeDrops.Count==1&&second.NativeDrops.Count==1&&host.NativeDropRegistrations>0,
                "complete native Server_RegisterLocalDrop publishes one remaining world entry on every peer, host="+hostTakesFirst);
            Check(items[1].ActiveAgent==null&&items[2].ActiveAgent==null,"actual old-ID Client_HandleDespawn retires original guest pickup agents before the new shared drop, host="+hostTakesFirst);
            var remainder=second.SpawnedItems[0];Pickup(second,remainder);BossRushCoop.Flush();
            Check(Added(second)==1&&second.Body.CharacterItem.Inventory.Quantity==2&&winner.Body.CharacterItem.Inventory.Quantity+second.Body.CharacterItem.Inventory.Quantity==12,
                "another participant takes only the 2-unit remainder and total initial 7 plus drop 5 stays conserved, host="+hostTakesFirst);
            TickAll();Check(second.SpawnedItems.Count==1&&Added(winner)==1&&Added(second)==1&&host.NativeDrops.Count==0&&first.NativeDrops.Count==0&&second.NativeDrops.Count==0,
                "lost result ACKs never recreate a second remainder or repeat either official inventory mutation, host="+hostTakesFirst);
            BossRushCoop.DropAck=false;TickAll();
        }
    }
    private static void LeaseIdentity()
    {
        foreach(string changed in new[]{"room","generation","scene","registry","entry","item","itemnet","service"})
        {
            Setup();var items=Shared();object lease=BossRushCoopTransport.CaptureNetworkDrop(items[0]);var replacement=changed=="item"?Ground():items[0];
            if(changed=="room")host.Room="next-room";
            if(changed=="generation")host.Generation++;
            if(changed=="scene")host.Scene="next-scene";
            if(changed=="registry")host.NativeDrops=new DropSyncRegistry();
            if(changed=="itemnet"){host.NetworkItems=new ItemNet();BossRushCoop.Use(host);}
            if(changed=="service")NetService.Instance=new NetService();
            if(changed=="entry")host.NativeDrops.Unregister(55);
            var replacementEntry=host.NativeDrops.Register(55,replacement,replacement.ActiveAgent.gameObject);int before=host.NativeDespawns;
            Check(!BossRushCoopTransport.CompleteNetworkDrop(lease),"a frozen native lease rejects changed "+changed+" before invoking the actual server handler");BossRushCoop.Flush();
            DropSyncEntry entry;Check(host.NativeDrops.TryGetById(55,out entry)&&ReferenceEquals(entry,replacementEntry)&&replacement!=null&&replacement.ActiveAgent!=null&&host.NativeDespawns==before,
                "same numeric DropId reused after "+changed+" keeps the replacement item and sends no destructive native despawn");
        }
    }
    private static void UncapturedOldRoomReset()
    {
        Setup();var item=Ground();host.Module.Mark(item);host.Room="new-room";
        var entry=host.NativeDrops.Register(55,item,item.ActiveAgent.gameObject);int before=host.NativeDespawns;
        host.Module.Reset();DropSyncEntry current;
        Check(item!=null&&item.ActiveAgent!=null&&host.NativeDrops.TryGetById(55,out current)&&ReferenceEquals(current,entry)&&host.NativeDespawns==before,
            "old-room marker without captured lease cannot acquire new-room retirement permission during Reset");
        var metadata=new Dictionary<string,string>();BossRushCoop.Capture(item,metadata);
        Check(metadata.Count==0,"old-room Reset removes only its own stale marker on the newly registered same CLR item");
        Pickup(host,item);BossRushCoop.Flush();
        Check(Added(host)==1&&item!=null&&item.InInventory==host.Body.CharacterItem.Inventory&&host.NativeDrops.Count==0,
            "new-room owner can still take the preserved item through normal unmarked native pickup");
    }
    private static void InstallHooks(string[] args)
    {
        NativePickup.Install(args);var native=new Harmony("Test.ZombieDrop.NativeHooks");
        var pickup=typeof(CharacterItemControl).GetMethod("PickupItem");
        native.Patch(pickup,prefix:new HarmonyMethod(typeof(Patch_CharacterItemControl_PickupItem),"Prefix"),finalizer:new HarmonyMethod(typeof(Patch_CharacterItemControl_PickupItem),"Finalizer"));
        native.Patch(typeof(CharacterMainControl).GetMethod("PickupItem"),postfix:new HarmonyMethod(typeof(CharacterPickupPatch),"Postfix"));
        native.Patch(typeof(ItemExtensions).GetMethod("Drop"),prefix:new HarmonyMethod(typeof(ItemDropPatch),"Prefix"),postfix:new HarmonyMethod(typeof(ItemDropPatch),"Postfix"));
        native.Patch(typeof(ItemUtilities).GetMethod("AddAndMerge"),prefix:new HarmonyMethod(typeof(Program),"CountAddAndMerge"));
        new Harmony("Test.ZombieDrop.Claims").PatchAll(typeof(ZombieModeRuntimeModule).Assembly);
    }
    private static void CountAddAndMerge(Inventory inventory){inventory.AddCalls++;if(inventory.ThrowBefore)throw new InvalidOperationException("injected precommit inventory exception");}
    public static int Main(string[] args)
    {
        try{InstallHooks(args);ConcurrentAndHost();ReliableAndIdentity();FailureAndNativeDrop();FailedAttemptReplay();PostCommitExceptions();MetadataBeforeWorld();Retirement();PartialMergeRemainder();LeaseIdentity();UncapturedOldRoomReset();ScopeAndCleanup();Console.WriteLine("PASS ZombieCoopDropClaims "+checks+" actual claim / official pickup / installed native-hook assertions");return 0;}
        catch(Exception error){Console.Error.WriteLine("FAIL "+error.GetType().FullName+": "+error.Message);return 1;}
    }
}

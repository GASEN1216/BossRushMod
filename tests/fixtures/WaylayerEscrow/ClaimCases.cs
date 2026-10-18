using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;

internal static partial class Program
{
    private static CoopWaylayerClaims Claims(CoopWaylayerEscrow value) {return (CoopWaylayerClaims)typeof(CoopWaylayerEscrow).GetField("claims",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
    private static Item TicketItem(CoopWaylayerEscrow value,string id)
    {
        var worlds=(IDictionary)typeof(CoopWaylayerClaims).GetField("worlds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Claims(value));
        if(!worlds.Contains(id))return null;
        return (Item)worlds[id].GetType().GetField("Item",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(worlds[id]);
    }
    private static int UsableTickets(CoopWaylayerEscrow value)
    {
        var worlds=(IDictionary)typeof(CoopWaylayerClaims).GetField("worlds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Claims(value));int result=0;
        foreach(DictionaryEntry entry in worlds){var item=(Item)entry.Value.GetType().GetField("Item",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry.Value);if(item!=null&&item.ActiveAgent!=null&&CoopWaylayerClaims.Locked(item))result++;}return result;
    }
    private static object SendClaim(Item world)
    {
        Guest();var mirror=ItemTreeData.Clone(ItemTreeData.FromItem(world));
        BossRushCoop.Restore(mirror,new Dictionary<string,string>{{"BossRush.WaylayerClaim",world.GetString(CoopWaylayerJournal.ClaimLockKey)}});
        Check(!new CharacterItemControl {characterMainControl=guestPlayer}.PickupItem(mirror),"world ticket cannot enter native local pickup before durable claim");
        return BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.claim").Value;
    }
    private static void ReceiveClaim(object claim)
    {Host();BossRushCoop.Receive("host","sky.waylayer.claim",claim,"guest",false);}
    private static void DeliverClaim(bool ack=false)
    {
        var receipt=BossRushCoop.Sent.Last(v=>v.Topic=="BossRush.Personal.waylayer-claim").Value;
        Guest();BossRushCoop.Receive("guest","BossRush.Personal.waylayer-claim",receipt,"host",true);UnityEngine.Time.frameCount++;guest.Tick(1);
        if(ack){var acknowledgement=BossRushCoop.Sent.Last(v=>v.Topic=="BossRush.Personal.waylayer-claim.ack").Value;Host();BossRushCoop.Receive("host","BossRush.Personal.waylayer-claim.ack",acknowledgement,"guest",false);}
    }
    private static async Task ClaimCases()
    {
        Setup();var stack=Add(guestPlayer,5002,3,"durable claim original");var packet=Start();Respond(Response());Guest();Check(Apply(Decision()),"new steal debits through real terminal decision");
        Host();boss.Health.IsDead=true;var world=chief.Bag.GetItemAt(0);var claim=SendClaim(world);ReceiveClaim(claim);
        var committed=Journal(host).FindCommit(packet.Id);
        Check(committed.PublicationProtocol==3&&CoopWaylayerJournal.ValidId(committed.ClaimId)&&committed.ClaimOwner==packet.Owner,"native claimant is frozen with persistent original-host claim proof");
        Check(world.IsBeingDestroyed&&chief.Bag.Content.All(v=>v==null),"claim retires the locked corpse ticket before personal original delivery");
        DeliverClaim();Check(guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3,"actual personal receipt adopts precisely one original tree without merging");
        int factories=ItemTreeData.FactoryCalls;Host();host.Tick(2);DeliverClaim();ReceiveClaim(claim);DeliverClaim();
        Check(ItemTreeData.FactoryCalls==factories&&guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3,"lost personal ACK and repeated native claims cannot instantiate a second original");
        Guest();var saved=ItemTreeData.FromItem(guestPlayer.CharacterItem);guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(saved));guest.Tick(2);
        Check(Journal(guest).IsReturned(committed.ClaimId)&&guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3,"native saved root receipt preserves claimed quantity through new runtime and reload");
        // Consume/remove the adopted grant, then normal save and reload: Returned is the ownership fact.
        var owned=guestPlayer.CharacterItem.Inventory.Content.First(v=>v!=null&&v.GetString(CoopWaylayerJournal.GrantKey)==committed.ClaimId);owned.DestroyTree();Journal(guest).BeforeNativeSave(guestPlayer.CharacterItem);saved=ItemTreeData.FromItem(guestPlayer.CharacterItem);
        guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(saved));guest.Tick(2);
        Check(guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==2&&Journal(guest).IsReturned(committed.ClaimId),"already claimed and consumed grant never regenerates from its durable tree after native reload");

        foreach(string operation in new[]{"inventory","at","slot","combine","detach","drop","same-destination","snapshot"})
        {
            Setup();Add(guestPlayer,5002,3,"reentrant "+operation);packet=Start();Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;world=chief.Bag.GetItemAt(0);claim=SendClaim(world);ReceiveClaim(claim);
            var foreign=new Inventory();var slot=new Slot {Master=hostPlayer.CharacterItem};var merge=new Item {TypeID=5002,StackCount=5};bool called=false;Item observed=null;
            guestPlayer.CharacterItem.Inventory.AfterAdd=item=>
            {
                called=true;observed=item;
                if(operation=="inventory"){item.Detach();foreign.AddItem(item);}
                else if(operation=="at"){item.Detach();foreign.AddAt(item,0);}
                else if(operation=="slot")slot.Plug(item,out var ignored);
                else if(operation=="combine")merge.Combine(item);
                else if(operation=="detach")item.Detach();
                else if(operation=="drop")item.Drop(guestPlayer,true);
                else if(operation=="same-destination")guestPlayer.CharacterItem.Inventory.AddAt(item,7);
                else if(operation=="snapshot")BossRushCoop.Restore(new Item(),new Dictionary<string,string>{{"BossRush.WaylayerClaim",item.GetString(CoopWaylayerJournal.ClaimLockKey)}});
            };
            DeliverClaim();
            Check(called&&observed!=null&&!observed.IsBeingDestroyed&&observed.InInventory==guestPlayer.CharacterItem.Inventory,"exact one-mutation admission survives synchronous "+operation+" callback");
            Check(!observed.OnGround&&observed.ActiveAgent==null&&slot.Content==null&&merge.StackCount==5&&foreign.Content.All(v=>v==null),"reentrant "+operation+" cannot expose a second inventory, slot, merge or ground owner");
            Check(guestPlayer.CharacterItem.Inventory.Content.Count(v=>ReferenceEquals(v,observed))==1&&!CoopWaylayerClaims.Locked(observed),"grant unlocks once only after final exact native inventory ownership: "+operation);
        }

        Setup();Add(guestPlayer,5002,3,"save during notification");packet=Start();Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;world=chief.Bag.GetItemAt(0);claim=SendClaim(world);ReceiveClaim(claim);
        ItemTreeData during=null;guestPlayer.CharacterItem.Inventory.AfterAdd=item=>{Journal(guest).BeforeNativeSave(guestPlayer.CharacterItem);during=ItemTreeData.FromItem(guestPlayer.CharacterItem);};guestPlayer.CharacterItem.Inventory.ThrowAfterAdd=true;DeliverClaim();
        Check(during!=null&&during.entries.Any(v=>v.variables.Any(c=>c.Key==CoopWaylayerJournal.GrantKey)),"real AddAt notification captures the grant proof before returned callback completes");
        Guest();var grantId=Journal(guest).Data.Grants.Single().Id;guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();guestPlayer.CharacterItem=ItemTreeData.Clone(Journal(guest).PrepareLoad(during));guest.Tick(2);
        Check(Journal(guest).IsReturned(grantId)&&guestPlayer.CharacterItem.GetAllChildren(true,false).All(v=>!CoopWaylayerClaims.Locked(v)),"native notification save reload proves adoption and clears every root/child private lock");

        Setup();Add(guestPlayer,5002,3,"host self claimant");packet=Start();Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;world=chief.Bag.GetItemAt(0);
        Check(!new CharacterItemControl {characterMainControl=hostPlayer}.PickupItem(world),"host native pickup follows the same locked claim entry");
        UnityEngine.Time.frameCount++;host.Tick(2);
        Check(Journal(host).Data.Grants.Count==1&&Journal(host).IsReturned(Journal(host).Data.Grants[0].Id)&&hostPlayer.CharacterItem.Inventory.Content.Count(v=>v!=null)==1,"actual personal delivery applies Self against the same slot containing the host commit");
        var hostSaved=ItemTreeData.FromItem(hostPlayer.CharacterItem);host.Dispose();Saves.SavesSystem.Reload();host=new CoopWaylayerEscrow();hostPlayer.CharacterItem=ItemTreeData.Clone(Journal(host).PrepareLoad(hostSaved));host.Tick(2);
        Check(hostPlayer.CharacterItem.Inventory.Content.Count(v=>v!=null)==1&&UsableTickets(host)==0,"host self claimed native save reload cannot rebuild the public original");
        foreach(string boundary in new[]{"factory-death","notification-death","factory-slot"})
        {
            Setup();Add(guestPlayer,5002,3,"lease "+boundary);packet=Start();Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;world=chief.Bag.GetItemAt(0);claim=SendClaim(world);ReceiveClaim(claim);
            var beforeBody=guestPlayer;int beforeFactories=ItemTreeData.FactoryCalls;
            if(boundary.StartsWith("factory"))ItemTreeData.Deferred=new TaskCompletionSource<Item>();
            else guestPlayer.CharacterItem.Inventory.AfterAdd=item=>guestPlayer.Health.IsDead=true;
            DeliverClaim();
            if(boundary.StartsWith("factory"))
            {
                var deferred=ItemTreeData.Deferred;ItemTreeData.Deferred=null;
                Guest();var tree=Journal(guest).Data.Grants.Single().Tree;
                if(boundary=="factory-death")guestPlayer.Health.IsDead=true;else Guest("other-slot");
                deferred.SetResult(ItemTreeData.Clone(tree));await Task.Yield();
            }
            Guest();var grant=Journal(guest).Data.Grants.FirstOrDefault();
            Check(grant!=null&&!Journal(guest).IsReturned(grant.Id),"death/asset lease never acknowledges or marks a private grant returned: "+boundary);
            if(boundary=="notification-death")
            {
                var held=beforeBody.CharacterItem.Inventory.Content.Single(v=>v!=null&&v.GetString(CoopWaylayerJournal.GrantKey)==grant.Id);
                Check(CoopWaylayerClaims.Locked(held)&&!held.IsBeingDestroyed,"notification death preserves the same owned locked original");
                Guest();guest.Tick(2);Check(ItemTreeData.FactoryCalls==beforeFactories+1,"dead owned grant cannot be re-created while its exact instance survives");
            }
            else Check(beforeBody.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==2,"async death/slot change never adopts into stale body");
        }

        Setup();Add(guestPlayer,5002,3,"unique authenticated owner");packet=Start();Respond(Response());Guest();Apply(Decision());Host();boss.Health.IsDead=true;world=chief.Bag.GetItemAt(0);claim=SendClaim(world);
        Host();BossRushCoop.Players["duplicate"]=new BossRushCoopPlayer {Id="duplicate",AssetId="different-native-asset",Character=Player()};
        BossRushCoop.Receive("host","sky.waylayer.identity",new CoopWaylayerEscrow.IdentityMessage {Identity=packet.Owner,Asset="different-native-asset",Run=BossRushCoop.RunId},"duplicate",false);
        int receipts=BossRushCoop.Sent.Count(v=>v.Topic=="BossRush.Personal.waylayer-claim");ReceiveClaim(claim);
        Check(string.IsNullOrEmpty(Journal(host).FindCommit(packet.Id).ClaimId)&&BossRushCoop.Sent.Count(v=>v.Topic=="BossRush.Personal.waylayer-claim")==receipts,"two actual peers reporting one durable journal identity cannot freeze or deliver one grant to both assets");
        BossRushCoop.Players.Remove("duplicate");world.transform.position=new UnityEngine.Vector3 {x=10000};ReceiveClaim(claim);DeliverClaim();
        Check(guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3,"corpse claim uses authoritative chief/container anchor even when item transform stays far away");

        Setup();Host();var historyJournal=Journal(host);string offlineOwner=Guid.NewGuid().ToString("N"),proof=Guid.NewGuid().ToString("N");
        var originalTree=ItemTreeData.FromItem(new Item {TypeID=5002,StackCount=1});
        for(int n=0;n<5000;n++)historyJournal.Data.Commits.Add(new CoopWaylayerJournal.Commit {Id=Guid.NewGuid().ToString("N"),Host=historyJournal.Identity,Owner=offlineOwner,Proof=proof,Tree=originalTree,State=CoopWaylayerJournal.Success,PublicationProtocol=3,PublicationStarted=true,ClaimId=Guid.NewGuid().ToString("N"),ClaimOwner=offlineOwner,ClaimProof=proof});
        Check(historyJournal.Persist(),"5000 protocol3 claimed originals are physically persisted before owner restart");
        host.Dispose();Saves.SavesSystem.Reload();host=new CoopWaylayerEscrow();Handshake();
        for(int n=0;n<100;n++){Host();host.Tick(1);Guest();guest.Tick(1);}
        int historySends=BossRushCoop.Sent.Count,historyWrites=Saves.SavesSystem.PhysicalWrites;var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);Func<long> allocations=method==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);long startBytes=allocations==null?0:allocations();
        for(int n=0;n<10000;n++){Host();host.Tick(1);Guest();guest.Tick(1);}
        long historyBytes=allocations==null?-1:allocations()-startBytes;
        Check(BossRushCoop.Sent.Count==historySends&&Saves.SavesSystem.PhysicalWrites==historyWrites,"protocol3 claimed history performs no periodic delivery or physical save");
        Check(((IDictionary)typeof(CoopWaylayerClaims).GetField("worlds",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(Claims(host))).Count==0,"completed claim tombstones stay outside the active world index");
        if(allocations!=null)Check(historyBytes<65536,"5000 completed protocol3 claims allocate no history-sized work on 10000 idle ticks");
        Console.WriteLine("WaylayerClaimHistoryMetrics: ticks=10000 claimed=5000 sends=0 saves=0 active_worlds=0 allocated_bytes="+historyBytes);
        Console.WriteLine("WaylayerProtocol3: PASS durable claims, real personal delivery/ACK boundary, exact native adoption and native-load proof.");
        await Task.Yield();
    }
}

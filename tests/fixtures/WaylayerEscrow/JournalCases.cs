using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;
internal static partial class Program
{
    private static CoopWaylayerJournal Journal(CoopWaylayerEscrow value){return (CoopWaylayerJournal)typeof(CoopWaylayerEscrow).GetField("journal",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
    private static async Task JournalCases()
    {
        Setup();Guest();var j=Journal(guest);
        var original=Add(guestPlayer,5002,3,"equal");var other=Add(guestPlayer,5002,3,"equal");
        ItemTreeData disk=ItemTreeData.FromItem(guestPlayer.CharacterItem);
        guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));member.Character=guestPlayer;
        var mine=guestPlayer.CharacterItem.Inventory.GetItemAt(1);var unaffected=guestPlayer.CharacterItem.Inventory.GetItemAt(0);
        var packet=Start();var response=Response();Check(mine.StackCount==2&&unaffected.StackCount==3,"real reserve selects one specific equal source instance");
        Respond(response);Guest();var decision=Decision();
        string beforeDisk= (string)Saves.SavesSystem.ReadDisk(Saves.SavesSystem.CurrentFilePath,"BossRush_CoopWaylayerJournal");
        BossRushCoop.RunId="different-host-room";guest.Tick(1);Check(mine.StackCount==2,"unknown committed result never refunds on another room");
        guest.Dispose();Check(mine.StackCount==2,"unknown committed result never refunds on module unload");
        Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();j=Journal(guest);
        var overlay=j.PrepareLoad(disk);guestPlayer.CharacterItem=ItemTreeData.Clone(overlay);
        Check(guestPlayer.CharacterItem.Inventory.GetItemAt(0).StackCount==3&&guestPlayer.CharacterItem.Inventory.GetItemAt(1).StackCount==2,"restart excludes only frozen equal source, leaves other instance unchanged");
        Check(disk.entries.Single(v=>v.instanceID==disk.RootData.inventory[1].instanceID).StackCount==3,"overlay never edits saved tree object");
        // The current authenticated original host answers a journal query after ephemeral asset IDs changed.
        BossRushCoop.RunId="room-reconnect";Handshake();Guest();guest.Tick(1);
        var query=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.query").Value;
        Host();host.Tick(1);BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);
        var result=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.result").Value;
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Check(j.FindDebt(packet.Id).State==CoopWaylayerJournal.Success&&guestPlayer.CharacterItem.Inventory.GetItemAt(1).StackCount==2,"persistent original-host result settles once after process-local identity changes");
        j.BeforeNativeSave(guestPlayer.CharacterItem);var naturallySaved=ItemTreeData.FromItem(guestPlayer.CharacterItem);Saves.SavesSystem.SaveFile(false);
        var reloaded=j.PrepareLoad(naturallySaved);
        Check(reloaded.RootData.inventory.Count==2&&reloaded.GetEntry(reloaded.RootData.inventory[1].instanceID).StackCount==2,"natural-save exclusion receipt prevents a second deduction");
        Check(Saves.SavesSystem.CharacterWrites==0,"journal never invokes whole-character save");
        Host();var taken=chief.Bag.GetItemAt(0);taken.Detach();hostPlayer.CharacterItem.Inventory.AddItem(taken);
        host.Dispose();Saves.SavesSystem.Reload();host=new CoopWaylayerEscrow();Handshake();
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Host();BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);
        result=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.result").Value;
        Check(((CoopWaylayerEscrow.Decision)result).Success&&taken.InInventory==hostPlayer.CharacterItem.Inventory&&chief.Bag.Content.All(v=>v==null),"known published success stays complete after original custody item was legitimately taken and host restarted");
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Check(guestPlayer.CharacterItem.Inventory.GetItemAt(1).StackCount==2,"taken published original cannot trigger a second refund or tree creation");

        Setup();Guest();j=Journal(guest);Add(guestPlayer,5002,3,"base original");disk=ItemTreeData.FromItem(guestPlayer.CharacterItem);
        guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));
        Add(guestPlayer,5002,1,"new raid exact object");packet=Start();response=Response();Guest();guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();j=Journal(guest);
        overlay=j.PrepareLoad(disk);guestPlayer.CharacterItem=ItemTreeData.Clone(overlay);
        Check(guestPlayer.CharacterItem.Inventory.GetItemAt(0).StackCount==3,"raid-only source absent from disk cannot deduct another same-type base item");
        ItemTreeData.Fail=true;Respond(response);ItemTreeData.Fail=false;Guest();Check(Apply(Decision()),"explicit original-host failure refunds raid-only frozen tree");
        Check(guestPlayer.CharacterItem.Inventory.Content.Count(v=>v!=null)==2,"only missing exact source is materialized, quantity not guessed by type");

        Setup();Guest();j=Journal(guest);Add(guestPlayer,5002,3,"failed before response");disk=ItemTreeData.FromItem(guestPlayer.CharacterItem);
        guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));packet=Start();response=Response();
        guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();j=Journal(guest);guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));
        ItemTreeData.Fail=true;Respond(response);ItemTreeData.Fail=false;Guest();Check(Apply(Decision())&&guestPlayer.CharacterItem.Inventory.Content.Where(v=>v!=null).Sum(v=>v.StackCount)==3,"failure after restart returns precisely held one, source rollback not duplicated");

        Setup();var stack=Add(guestPlayer,5002,3,"post-write exception");packet=Start();response=Response();
        Host();Saves.SavesSystem.ThrowAfterDisk=true;Respond(response);Saves.SavesSystem.ThrowAfterDisk=false;
        Check(Decision().Success&&chief.Bag.GetItemAt(0)!=null,"actual disk readback recognizes successful commit despite post-write exception");
        Guest();Check(Apply(Decision())&&stack.StackCount==2,"post-write exception cannot create a refund plus published copy");
        // Missing response: only the original persisted host can authoritatively reject.
        Setup();stack=Add(guestPlayer,5002,3,"request never reached host");packet=Start();response=Response();
        Guest();guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();j=Journal(guest);
        BossRushCoop.RunId="original-host-reconnected";Host();host.Tick(1);Handshake();Guest();guest.Tick(1);
        query=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.query").Value;
        Host();BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);result=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.result").Value;
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Check(j.FindDebt(packet.Id).State==CoopWaylayerJournal.Failure,"original host durably rejects absent commit after reconnect");
        var frozenState=j.FindDebt(packet.Id).State;
        var forged=(CoopWaylayerEscrow.Decision)result;forged.Success=true;
        BossRushCoop.Receive("guest","sky.waylayer.result",forged,"guest-impostor",false);
        Check(j.FindDebt(packet.Id).State==frozenState,"non-host cannot forge terminal journal decision");

        Setup();stack=Add(guestPlayer,5002,3,"host restart after pending");packet=Start();response=Response();
        ItemTreeData.Deferred=new TaskCompletionSource<Item>();Respond(response);Host();var hj=Journal(host);
        Check(hj.FindCommit(packet.Id).State==CoopWaylayerJournal.Reserved,"host tree is durable pending before actual async item creation");
        host.Dispose();var late=ItemTreeData.Clone(response.Tree);ItemTreeData.Deferred.SetResult(late);await Task.Yield();ItemTreeData.Deferred=null;
        Check(late.IsBeingDestroyed,"unloaded factory continuation cannot publish pending custody");
        Saves.SavesSystem.Reload();host=new CoopWaylayerEscrow();BossRushCoop.RunId="after-host-process-restart";Handshake();Guest();guest.Tick(1);
        query=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.query").Value;
        Host();BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);result=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.result").Value;
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Host();Check(stack.StackCount==3&&Journal(host).FindCommit(packet.Id).State==CoopWaylayerJournal.Failure,"pending-before-publication restores one original and a durable host rejection");

        Setup();var whole=Add(guestPlayer,5002,1,"late old-slot factory");packet=Start();response=Response();
        ItemTreeData.Fail=true;Respond(response);ItemTreeData.Fail=false;Guest();whole.DestroyTree();
        ItemTreeData.Deferred=new TaskCompletionSource<Item>();Check(!Apply(Decision()),"refund awaits actual original tree factory");
        Guest("other-slot");var wrong=Player();CharacterMainControl.Main=wrong;
        late=ItemTreeData.Clone(response.Tree);ItemTreeData.Deferred.SetResult(late);await Task.Yield();ItemTreeData.Deferred=null;
        Check(late.IsBeingDestroyed&&wrong.CharacterItem.Inventory.Content.Count==0,"old-slot async refund destroys unowned late object without touching new asset");
        Guest();Check(Apply(Decision())&&guestPlayer.CharacterItem.Inventory.Content.Count(v=>v!=null)==1,"original slot can retry exact tree after late foreign-slot continuation");

        Setup();Guest();j=Journal(guest);var nested=new Item {TypeID=5002,StackCount=1,Inventory=new Inventory(),Metadata="nested original"};
        nested.Inventory.AddItem(new Item {TypeID=101,StackCount=1,Metadata="original attachment"});guestPlayer.CharacterItem.Inventory.AddItem(nested);
        disk=ItemTreeData.FromItem(guestPlayer.CharacterItem);guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));
        packet=Start();response=Response();overlay=j.PrepareLoad(disk);
        Check(overlay.entries.Count==1&&overlay.RootData.inventory.Count==0,"withholding exact whole source removes its graph subtree and parent link only");
        ItemTreeData.Fail=true;Respond(response);ItemTreeData.Fail=false;Guest();Check(Apply(Decision()),"full nested tree explicitly refunded");
        var restored=guestPlayer.CharacterItem.Inventory.GetItemAt(0);
        Check(restored.Inventory.GetItemAt(0).Metadata=="original attachment","original full nested metadata survives escrow refund");

        Setup();stack=Add(guestPlayer,5002,3,"same generation wrong token");Host();Check(host.Begin(chief,guestPlayer,boss),"token test begins real request");host.Tick(1);
        packet=(CoopWaylayerEscrow.Packet)BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.reserve").Value;
        Guest();BossRushCoop.SceneToken="new-core-token";BossRushCoop.Receive("guest","sky.waylayer.reserve",packet,"host",true);
        Check(stack.StackCount==3,"stale world reserve cannot debit new Core with unchanged generation");

        Setup();Guest();j=Journal(guest);Add(guestPlayer,5002,3,"save while journal write unavailable");
        disk=ItemTreeData.FromItem(guestPlayer.CharacterItem);guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));
        packet=Start();response=Response();Guest();
        Saves.SavesSystem.FailBeforeDisk=true;j.BeforeNativeSave(guestPlayer.CharacterItem);
        naturallySaved=ItemTreeData.FromItem(guestPlayer.CharacterItem);Saves.SavesSystem.FailBeforeDisk=false;
        var savedAgain=j.PrepareLoad(naturallySaved);
        Check(savedAgain.GetEntry(savedAgain.RootData.inventory[0].instanceID).StackCount==2,"normal character-tree receipt prevents double debit even if another journal write is unavailable");
        // A sort/merge may destroy the old source stack; the character receipt still owns the excluded unit.
        var remaining=guestPlayer.CharacterItem.Inventory.GetItemAt(0);var merged=Add(guestPlayer,5002,remaining.StackCount,"merged destination");remaining.DestroyTree();
        var afterMerge=j.PrepareLoad(ItemTreeData.FromItem(guestPlayer.CharacterItem));
        Check(afterMerge.GetEntry(afterMerge.RootData.inventory.Single().instanceID).StackCount==2,"source movement or merge cannot erase character-level exclusion receipt");

        Setup();stack=Add(guestPlayer,5002,3,"read failed after success write");packet=Start();response=Response();
        Host();Saves.SavesSystem.FailReadAfterWrites=Saves.SavesSystem.PhysicalWrites+2;Respond(response);
        hj=Journal(host);Check(hj.FindCommit(packet.Id).State==CoopWaylayerJournal.Success&&chief.Bag.Content.Count(v=>v!=null)==1,"successful official physical write keeps real boss custody despite separate readback failure");
        Check(!hj.FinishCommit(hj.FindCommit(packet.Id),false),"uncertain success can never become a refundable rejection");
        Saves.SavesSystem.FailReadAfterWrites=0;Respond(response);
        Check(Decision().Success&&chief.Snatches==1&&chief.Bag.Content.Count(v=>v!=null)==1,"same terminal retry cannot republish original tree after durable official write");
        Guest();Check(Apply(Decision())&&stack.StackCount==2,"readback recovery cannot duplicate original refund plus boss custody");

        Setup();Guest();j=Journal(guest);stack=Add(guestPlayer,5002,1,"count rejection original");stack.RejectDetach=true;
        packet=Start();response=Response();Respond(response);Guest();Check(Apply(Decision())&&stack.InInventory==guestPlayer.CharacterItem.Inventory,"unreserved explicit failure leaves original physical item untouched");
        Check(j.FindDebt(packet.Id).State==CoopWaylayerJournal.Prepared,"unreserved failure cannot persist a future refundable copy");

        Setup();stack=Add(guestPlayer,5002,3,"finished refund idle IO");packet=Start();response=Response();
        ItemTreeData.Fail=true;Respond(response);ItemTreeData.Fail=false;Guest();Check(Apply(Decision()),"finished refund establishes settled physical owner");
        int writes=Saves.SavesSystem.PhysicalWrites;
        for(int i=0;i<4000;i++)guest.Tick(1);
        Check(Saves.SavesSystem.PhysicalWrites==writes,"4000 ticks after full refund cause zero repeated physical journal writes");

        Setup();Guest();j=Journal(guest);Add(guestPlayer,5002,1,"target slot original");
        disk=ItemTreeData.FromItem(guestPlayer.CharacterItem);guestPlayer.CharacterItem=ItemTreeData.Clone(j.PrepareLoad(disk));
        packet=Start();response=Response();ItemTreeData.Fail=true;Respond(response);ItemTreeData.Fail=false;Guest();
        var oldBody=guestPlayer;var foreign=Player();foreign.CharacterItem.SetString(CoopWaylayerJournal.CharacterOwnerKey,Guid.NewGuid().ToString("N"));
        guestPlayer=foreign;CharacterMainControl.Main=foreign;
        Check(!Apply(Decision())&&foreign.CharacterItem.Inventory.Content.Count==0,"new slot journal cannot refund into previous slot Main body during load gap");
        guestPlayer=oldBody;CharacterMainControl.Main=oldBody;
        Check(Apply(Decision()),"original loaded character tree resumes its own held original refund");

        Setup();stack=Add(guestPlayer,5002,3,"compound storage fault before publication");packet=Start();response=Response();
        Host();writes=Saves.SavesSystem.PhysicalWrites;Saves.SavesSystem.ThrowAfterDisk=true;Saves.SavesSystem.FailReadAfterWrites=writes+2;
        Respond(response);
        Check(chief.Bag.Content.All(v=>v==null)&&CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Count==0,"physical exception plus unreadable confirmation cannot publish original or send terminal success");
        BossRushCoop.SceneGeneration++;host.Tick(1);Check(CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Count==0,"scene exit cannot turn uncertain unpublished custody into success");
        host.Dispose();Saves.SavesSystem.ThrowAfterDisk=false;Saves.SavesSystem.FailReadAfterWrites=0;Saves.SavesSystem.Reload();
        host=new CoopWaylayerEscrow();BossRushCoop.RunId="compound-fault-reconnect";Handshake();Guest();guest.Tick(1);
        query=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.query").Value;
        Host();BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);
        result=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.result").Value;
        Check(!((CoopWaylayerEscrow.Decision)result).Success,"restarted original host rejects unpublished custody after physical store recovers");
        Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Check(stack.StackCount==3&&chief.Bag.Content.All(v=>v==null),"compound fault recovery restores one original without inaccessible success or world duplication");

        Setup();stack=Add(guestPlayer,5002,3,"publication barrier compound fault");packet=Start();response=Response();
        Host();writes=Saves.SavesSystem.PhysicalWrites;Saves.SavesSystem.ThrowAfterDisk=true;Saves.SavesSystem.FailReadAfterWrites=writes+3;Respond(response);
        Check(chief.Bag.Content.All(v=>v==null)&&CoopPersonalDelivery<CoopWaylayerEscrow.Decision>.Queue.Count==0,"failed publication barrier retracts item before public frame and sends no terminal");
        Saves.SavesSystem.ThrowAfterDisk=false;Saves.SavesSystem.FailReadAfterWrites=0;BossRushCoop.SceneGeneration++;host.Tick(1);
        host.Dispose();Saves.SavesSystem.Reload();host=new CoopWaylayerEscrow();BossRushCoop.RunId="publication-barrier-reconnect";Handshake();Guest();guest.Tick(1);
        query=BossRushCoop.Sent.Last(v=>v.Topic=="sky.waylayer.query").Value;Host();BossRushCoop.Receive("host","sky.waylayer.query",query,"guest",false);
        var recovered=BossRushCoop.Sent.LastOrDefault(v=>v.Topic=="sky.waylayer.result");
        Check(recovered!=null,"known pre-publication cancellation must produce original-host terminal after readable recovery");
        result=recovered.Value;Guest();BossRushCoop.Receive("guest","sky.waylayer.result",result,"host",true);
        Check(!((CoopWaylayerEscrow.Decision)result).Success&&stack.StackCount==3,"known pre-publication cancellation recovers exact original after restart and readable original host");

        Setup();Guest();j=Journal(guest);
        for(int i=0;i<5000;i++) j.Data.Debts.Add(new CoopWaylayerJournal.Debt {
            Id=Guid.NewGuid().ToString("N"),Host=Journal(host).Identity,Owner=j.Identity,Proof=Guid.NewGuid().ToString("N"),
            Tree=ItemTreeData.FromItem(new Item {TypeID=5002,StackCount=1}),State=CoopWaylayerJournal.Success });
        Check(j.Persist(),"large terminal history is actually durable before restart");
        guest.Dispose();Saves.SavesSystem.Reload();guest=new CoopWaylayerEscrow();Handshake();
        for(int i=0;i<100;i++){Host();host.Tick(1);Guest();guest.Tick(1);}
        int sent=BossRushCoop.Sent.Count;writes=Saves.SavesSystem.PhysicalWrites;
        var counter=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
        Func<long> allocated=counter==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),counter);
        long begin=allocated==null?0:allocated();
        for(int i=0;i<10000;i++){Host();host.Tick(1);Guest();guest.Tick(1);}
        long bytes=allocated==null?-1:allocated()-begin;
        Check(BossRushCoop.Sent.Count==sent&&Saves.SavesSystem.PhysicalWrites==writes,"10000 idle ticks with 5000 durable terminal debts send no identity, query or physical save");
        var active=(System.Collections.IDictionary)typeof(CoopWaylayerEscrow).GetField("pendingDebts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(guest);
        var work=(System.Collections.IList)typeof(CoopWaylayerEscrow).GetField("debtWork",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(guest);
        Check(active.Count==0&&work.Count==0,"terminal history is not copied into periodic debt work");
        Console.WriteLine("WaylayerIdleMetrics: ticks=10000 history=5000 sends="+(BossRushCoop.Sent.Count-sent)+" saves="+(Saves.SavesSystem.PhysicalWrites-writes)+" active="+active.Count+" debt_work="+work.Count+" allocated_bytes="+bytes);
        // CLR tiered compilation can charge a few KB once to this thread; the old per-tick history copy costs >400 MB.
        if(allocated!=null) Check(bytes<65536,"idle production owner performs no history-sized allocation after warmup");
        BossRushCoop.LocalReadyEpoch="new-ready-lease";Guest();guest.Tick(1);
        Check(BossRushCoop.Sent.Count==sent+1&&BossRushCoop.Sent.Last().Topic=="sky.waylayer.identity","actual ready lease change reopens identity handshake without heartbeat polling");
        Handshake();sent=BossRushCoop.Sent.Count;guest.Tick(1);
        Check(BossRushCoop.Sent.Count==sent,"confirmed replacement ready lease stops identity retry again");
        Setup();stack=Add(guestPlayer,5002,3,"visible unresolved original");packet=Start();response=Response();
        Check(Duckov.UI.NotificationText.Notices.Count==0,"normal snatch reservation does not announce exceptional reconciliation");
        Respond(response);
        Guest();BossRushCoop.RunId="another-room";guest.Tick(1);guest.Tick(1);
        Check(Duckov.UI.NotificationText.Notices.Count==1&&Duckov.UI.NotificationText.Notices[0].Contains("1 original item"),"actual unresolved room exit shows one bilingual-policy notification without repeated tick spam");
        Guest();Check(Apply(Decision()),"original host successful result still settles after room exit");
        Check(Duckov.UI.NotificationText.Notices.Count==2&&Duckov.UI.NotificationText.Notices[1].Contains("1 item was stolen"),"delayed success explains one-item original ownership once");
        await Task.Yield();
    }
}

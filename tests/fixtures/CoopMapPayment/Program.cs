using System;
using System.Linq;
using System.Reflection;
using BossRush;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using EscapeFromDuckovCoopMod;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Probe
{
    internal static bool FailCreate,RejectDrop,ThrowAfterDrop,ThrowMessage,RejectMoney,OtherMap;
    internal static int Created,Dropped,TicketMaxStack=99;
    internal static MapSelectionEntry Entry;
    internal static BossRushCoopMapPaymentCompatibility Owner;
    private static int checks;
    internal static void Check(bool condition,string text) { checks++;if(!condition)throw new Exception(text); }
    internal static int TicketCount { get { return ItemUtilities.GetItemCount(500001)+Dropped; } }
    internal static void Reset(BossRushPendingEntryKind kind=BossRushPendingEntryKind.None)
    {
        if(Owner!=null){Owner.Dispose();Owner=null;}
        BossRushCoopMapPaymentCompatibility.ResetStaticCaches();
        BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.Asset="asset";BossRushCoop.RunId="room-A";BossRushCoop.SceneToken="token-A";BossRushCoop.SceneGeneration=1;
        Saves.SavesSystem.CurrentSlot=1;SceneLoader.IsSceneLoading=false;
        ModBehaviour.Instance=new ModBehaviour();BossRushMapSelectionHelper.bossRushEntryObjects.Clear();BossRushMapSelectionHelper.ResetStaticCaches();ModeHTicketRefund.ResetStaticCaches();
        EconomyManager.Attempts=0;CoopTool.Begins=CoopTool.Loads=CoopTool.Cancels=CoopTool.VoteStarts=CoopTool.GuestVoteRequests=0;
        CoopTool.ThrowBeforeSend=CoopTool.ThrowAfterSend=false;CoopTool.BeforeSend=null;
        FailCreate=RejectDrop=ThrowAfterDrop=ThrowMessage=RejectMoney=OtherMap=false;Created=Dropped=0;
        var root=new Item{IsCharacter=true};root.Inventory=new Inventory{Master=root};
        CharacterMainControl.Main=new CharacterMainControl{CharacterItem=root};
        var ticket=new Item{TypeID=500001,MaxStackCount=99};ticket.StackCount=3;root.Inventory.AddItem(ticket);
        MultiSceneCore.Instance=new MultiSceneCore();MultiSceneCore.Instance.gameObject.scene=new Scene{handle=101,isLoaded=true};
        NetService.Instance=new NetService();SceneNet.Instance=new SceneNet();
        LevelManager.Instance=new LevelManager();
        Install();
        Select(kind);
    }
    private static void Install()
    {
        Owner=new BossRushCoopMapPaymentCompatibility(typeof(SceneNet),()=>NetService.Instance.NetworkStarted,()=>NetService.Instance.IsServer,()=>NetService.Instance);
        Owner.Install();
    }
    private static void Rebind() { Owner.Dispose();Install(); }
    internal static void Select(BossRushPendingEntryKind kind=BossRushPendingEntryKind.None)
    {
        if(kind==BossRushPendingEntryKind.ModeH)BossRushMapSelectionHelper.FreezeModeHEntryIntent("arena","main");
        BossRushMapSelectionHelper.MarkEntryFlowFromMapSelectionUi(kind);
        Entry=new MapSelectionEntry{Cost=new Cost{money=0,items=new[]{new Cost.ItemEntry{id=500001,amount=1}}},Marker=new BossRushMapEntryClickHandler{entryIndex=0}};
        BossRushMapSelectionHelper.bossRushEntryObjects.Add(new GameObject{Entry=Entry});
        BossRushMapEntrySelectionPatch.RecordSelection(Entry);
    }
    internal static void StartVote() { SceneNet.Instance.Host_BeginSceneVote_Simple("main",null,false,false,false,"OnPointerClick"); }
    internal static void Commit()
    {
        SceneNet.Instance.Server_OnSceneReadySet(null,true);
        int before = EconomyManager.Attempts;
        SceneNet.Instance.Server_OnSceneReadySet(new LiteNetLib.NetPeer{Id="guest1"},true);
        Check(EconomyManager.Attempts==before,"host plus one guest ready does not charge");
        SceneNet.Instance.Server_OnSceneReadySet(new LiteNetLib.NetPeer{Id="guest2"},true);
        Time.unscaledTime+=1;SceneNet.Instance.Server_TryExecutePendingBeginLoad();
    }
    private static void ActualLoad()
    { Time.unscaledTime+=1;SceneNet.Instance.Server_TryExecutePendingLoad(); }
    private static void Normal()
    {
        Reset();StartVote();Check(CoopVote(),"real native three-party vote started");
        BossRushMapEntrySelectionPatch.RecordSelection(Entry);
        Commit();Check(EconomyManager.Attempts==1&&TicketCount==2&&CoopTool.Begins==1,"one actual ticket before the first real native BeginLoad");
        Check(BossRushMapSelectionHelper.HasPendingPrepaidTicket(),"real co-op payment is published to existing modes");
        SceneNet.Instance.Server_BroadcastBeginSceneLoad();Check(EconomyManager.Attempts==1&&CoopTool.Begins==1,"duplicate Broadcast cannot pay or send twice");
        ActualLoad();SceneNet.Instance.Host_PerformSceneLoad();Check(CoopTool.Loads==1,"paid host load permission is consumed once");
        Check(!BossRushCoopMapPaymentCompatibility.CanOpenMapSelection(),"same source blocks another bill until actual source retirement");
        var source=MultiSceneCore.Instance.gameObject.scene;SceneManager.Unload(source);UnityEngine.Object.Destroy(MultiSceneCore.Instance.gameObject);
        MultiSceneCore.Instance=new MultiSceneCore();MultiSceneCore.Instance.gameObject.scene=new Scene{handle=102,isLoaded=true};
        Check(BossRushCoopMapPaymentCompatibility.CanOpenMapSelection(),"actual new core retires committed capture");
        Select();StartVote();Commit();Check(EconomyManager.Attempts==2&&TicketCount==1&&CoopTool.Begins==2,"second real selection pays a second ticket after actual entry/return");
    }
    private static bool CoopVote() { return SceneNet.Instance.sceneVoteActive&&CoopTool.VoteStarts==1&&SceneNet.Instance.sceneParticipantIds.Count==3; }
    private static void Failures()
    {
        Reset();StartVote();SceneNet.Instance.CancelVote();Commit();
        Check(EconomyManager.Attempts==0&&TicketCount==3&&CoopTool.Begins==0,"cancel before ready is free and cannot launch");
        Reset();StartVote();foreach(var item in CharacterMainControl.Main.CharacterItem.Inventory.ToArray())if(item!=null)item.DestroyTree();
        Commit();Check(EconomyManager.Attempts==0&&CoopTool.Begins==0&&!SceneNet.Instance.sceneVoteActive,"insufficient ticket cancels the whole vote without any Begin");
        Reset();StartVote();Action reenter=()=>SceneNet.Instance.Server_BroadcastBeginSceneLoad();ItemUtilities.OnPlayerItemOperation+=reenter;
        try{Commit();}finally{ItemUtilities.OnPlayerItemOperation-=reenter;}
        Check(EconomyManager.Attempts==1&&TicketCount==2&&CoopTool.Begins==1,"ConsumeItems notification reentry cannot pay twice");
        Reset();StartVote();Action fail=()=>{throw new Exception("actual post-consumption notification");};ItemUtilities.OnPlayerItemOperation+=fail;
        try{Commit();}finally{ItemUtilities.OnPlayerItemOperation-=fail;}
        Check(EconomyManager.Attempts==1&&TicketCount==2&&CoopTool.Begins==0,"throw after real consumption stays Unknown and blocks all peers");
        Check(!BossRushCoopMapPaymentCompatibility.CanOpenMapSelection(),"Unknown cannot create another bill for the original asset");
        Rebind();SceneNet.Instance.sceneVoteActive=true;SceneNet.Instance.Server_BroadcastBeginSceneLoad();
        Check(EconomyManager.Attempts==1&&CoopTool.Begins==0,"transport rebind cannot erase Unknown and replay old native pending Begin");
        Reset();StartVote();Action<Cost> late=cost=>{throw new Exception("later cost listener");};EconomyManager.OnCostPaid+=late;
        try{Commit();}finally{EconomyManager.OnCostPaid-=late;}
        Check(EconomyManager.Attempts==1&&TicketCount==2&&CoopTool.Begins==1,"exact complete receipt survives later listener exception");
        Reset();StartVote();CoopTool.ThrowBeforeSend=true;
        try{Commit();throw new Exception("expected send fault");}catch(Exception e){Check(e.Message=="transport before send","real native send exception is preserved");}
        CoopTool.ThrowBeforeSend=false;Rebind();
        Check(!BossRushCoopMapPaymentCompatibility.CanOpenMapSelection(),"possibly broadcast payment survives same-core rebind");
        OtherMap=true;SceneNet.Instance.Host_BeginSceneVote_Simple("other",null,false,false,false,"DoTeleport");
        Check(!BossRushCoopMapPaymentCompatibility.CanOpenMapSelection(),"another native travel cannot erase possibly broadcast payment");
        Check(EconomyManager.Attempts==1&&TicketCount==2,"ambiguous send never auto-refunds or charges again");
        Reset();StartVote();RejectMoney=true;Commit();
        Check(EconomyManager.Attempts==1&&TicketCount==3&&CoopTool.Begins==0&&!BossRushCoopMapPaymentCompatibility.CanOpenMapSelection(),"false Pay retains unknown outcome without automatic second attempt");
    }
    private static void LeaseCases()
    {
        foreach(Action mutate in new Action[]{()=>BossRushCoop.Asset="replacement",()=>Saves.SavesSystem.CurrentSlot=2,
            ()=>BossRushCoop.RunId="room-B",()=>BossRushCoop.SceneToken="token-B",()=>BossRushCoop.SceneGeneration++,
            ()=>NetService.Instance=new NetService(),()=>CharacterMainControl.Main=new CharacterMainControl{CharacterItem=CharacterMainControl.Main.CharacterItem},
            ()=>CharacterMainControl.Main.Health.IsDead=true,()=>MultiSceneCore.Instance=new MultiSceneCore(),
            ()=>Entry.Cost=new Cost{items=new[]{new Cost.ItemEntry{id=500001,amount=1}}},()=>Entry.Cost.items[0].amount=2})
        {
            Reset();StartVote();mutate();Commit();Check(EconomyManager.Attempts==0&&CoopTool.Begins==0,"pre-pay identity/cost mutation is refused without any consumption");
            SceneNet.Instance._srvPendingSceneLoad=true;Time.unscaledTime+=1;SceneNet.Instance.Server_TryExecutePendingLoad();
            Check(CoopTool.Loads==0,"stale native pending host load cannot bypass rejected payment");
        }
        Reset();StartVote();BossRushCoop.IsHost=false;SceneNet.Instance.Server_BroadcastBeginSceneLoad();
        Check(EconomyManager.Attempts==0,"compatibility does not charge guest even on direct native method call");
        Reset();Owner.Dispose();Owner=null;BossRushCoop.IsConnected=false;
        BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();Check(BossRushMapSelectionHelper.HasPendingPrepaidTicket(),"single-player target callback retains its prior behavior");
        Reset();OtherMap=true;SceneNet.Instance.Host_BeginSceneVote_Simple("other",null,false,false,false,"DoTeleport");Commit();
        Check(EconomyManager.Attempts==0&&CoopTool.Begins==1,"unrelated native map keeps original voting and fees");
    }
    private static void Main(string[] args)
    {
        NativeScene.Install(args);
        Normal();Failures();LeaseCases();ReceiptCases.Run();ClickCases();
        if(Owner!=null)Owner.Dispose();
        Console.WriteLine("PASS CoopMapPayment: "+checks+" assertions; full installed vote/ready/begin/load/cancel IL, actual official Cost.Pay/ConsumeItems; L2 only");
    }
    private static void ClickCases()
    {
        foreach(bool nativeFirst in new[]{false,true})
        foreach(bool directEntry in new[]{false,true})
        {
            Reset();Owner.Dispose();Owner=null;BossRushCoopMapPaymentCompatibility.ResetStaticCaches();
            Install();
            // Reset/Select observed once manually for non-UI cases. Retire it so
            // these cases can succeed only through the actual native UI prefix.
            Owner.Dispose();BossRushCoopMapPaymentCompatibility.ResetStaticCaches();Install();
            NativeScene.ClickHarmony(nativeFirst,()=>
            {
                if(directEntry)Entry.OnPointerClick(new UnityEngine.EventSystems.PointerEventData());
                else new MapSelectionView().NotifyEntryClicked(Entry,new UnityEngine.EventSystems.PointerEventData());
                Commit();
                Check(EconomyManager.Attempts==1&&TicketCount==2&&CoopTool.Begins==1,"actual native Entry/Notify ordering pays before Begin: "+nativeFirst+"/"+directEntry);
                Check(MapSelectionView.VanillaLoads==0,"COOP still suppresses the official async map LoadTask");
            });
        }
        foreach(bool nativeFirst in new[]{false,true})
        foreach(bool directEntry in new[]{false,true})
        {
            Reset();BossRushCoop.IsHost=false;
            NativeScene.ClickHarmony(nativeFirst,()=>
            {
                if(directEntry)Entry.OnPointerClick(new UnityEngine.EventSystems.PointerEventData());
                else new MapSelectionView().NotifyEntryClicked(Entry,new UnityEngine.EventSystems.PointerEventData());
                Check(CoopTool.GuestVoteRequests==0&&EconomyManager.Attempts==0,"guest owned Entry/Notify cannot send native vote or consume host fee");
                Entry.Marker=null;
                if(directEntry)Entry.OnPointerClick(new UnityEngine.EventSystems.PointerEventData());
                else new MapSelectionView().NotifyEntryClicked(Entry,new UnityEngine.EventSystems.PointerEventData());
                Check(CoopTool.GuestVoteRequests==1&&EconomyManager.Attempts==0,"guest ordinary map retains actual native vote request");
            });
        }
        Reset();Owner.Dispose();BossRushCoopMapPaymentCompatibility.ResetStaticCaches();Install();
        RPCScene.HandleSceneVoteRequest(new RpcContext(),new SceneVoteRequestRpc{TargetSceneId="main",LocationName="OnPointerClick"});
        Check(!SceneNet.Instance.sceneVoteActive&&CoopTool.VoteStarts==0&&EconomyManager.Attempts==0,"remote native request cannot start visible owned target without exact host payment context");
        RPCScene.HandleSceneVoteRequest(new RpcContext(),new SceneVoteRequestRpc{TargetSceneId="other",LocationName="OnPointerClick"});
        Check(SceneNet.Instance.sceneVoteActive&&CoopTool.VoteStarts==1&&EconomyManager.Attempts==0,"remote ordinary native map retains original request path");
        foreach(bool unknown in new[]{true,false})
        {
            Reset();StartVote();
            if(unknown)
            {
                Action fail=()=>{throw new Exception("unknown before receipt");};ItemUtilities.OnPlayerItemOperation+=fail;
                try{Commit();}finally{ItemUtilities.OnPlayerItemOperation-=fail;}
            }
            else
            {
                CoopTool.ThrowBeforeSend=true;try{Commit();}catch(Exception e){Check(e.Message=="transport before send","ambiguous original Begin exception");}
                CoopTool.ThrowBeforeSend=false;
            }
            NativeScene.ClickHarmony(true,()=>
            {
                var second=new MapSelectionEntry{SceneID="second",Cost=new Cost{items=new[]{new Cost.ItemEntry{id=500001,amount=1}}},Marker=new BossRushMapEntryClickHandler{entryIndex=1}};
                second.OnPointerClick(new UnityEngine.EventSystems.PointerEventData());
                Commit();
                Check(EconomyManager.Attempts==1&&CoopTool.Begins==0,"old UI cannot select a second owned destination to bypass locked payment");
            });
        }
    }
}

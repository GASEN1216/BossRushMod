using System;
using System.Collections.Generic;
using System.Text;
using BossRush;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string label) { assertions++; if (!value) throw new Exception(label); }
    private static BossRushCoopItemVariable Variable(string key, CustomDataType type, byte[] raw, bool display = false)
    { return new BossRushCoopItemVariable { key = key, type = (int)type, raw = Convert.ToBase64String(raw), display = display }; }
    private static void Apply(BossRushCoopContentRuntimeModule module, Item item, params BossRushCoopItemVariable[] variables)
    {
        var state = new BossRushCoopItemState { typeId = item.TypeID, variables = new List<BossRushCoopItemVariable>(variables) };
        module.Apply(item, new Dictionary<string,string> { [BossRushCoopContentRuntimeModule.SnapshotKey] = BossRushCoopJson.ToJson(state) });
    }
    private static void Main()
    {
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="travel-initial";
        var module = new BossRushCoopContentRuntimeModule();
        var item = new Item { Value = 1.5f };
        item.Variables.SetRaw("RF_STAT_Damage", CustomDataType.Float, BitConverter.GetBytes(.5f), true, false);
        item.Variables.SetRaw("AFX_SLOT_1", CustomDataType.String, Encoding.UTF8.GetBytes("old:3"), true, false);
        CustomData uiEntry = item.Variables.Find(entry => entry.Key == "AFX_SLOT_1");
        var delta = Variable("RF_STAT_Damage", CustomDataType.Float, BitConverter.GetBytes(.2f));
        var slot = Variable("AFX_SLOT_1", CustomDataType.String, Encoding.UTF8.GetBytes("new:2"));
        Apply(module, item, delta, slot);
        Check(Math.Abs(item.Value - 1.2f) < .0001f, "new delta replaces old runtime value");
        Check(ReferenceEquals(uiEntry, item.Variables.Find(entry => entry.Key == "AFX_SLOT_1")), "UI entry identity survives replacement");
        Check(Encoding.UTF8.GetString(uiEntry.Raw) == "new:2", "affix identity applies");
        int refreshed = NewWeaponEquipState.Dirty;
        Apply(module, item, delta, slot);
        Check(NewWeaponEquipState.Dirty == refreshed && Math.Abs(item.Value - 1.2f) < .0001f, "duplicate snapshot is idempotent");
        Apply(module, item, Variable("RF_STAT_Damage", CustomDataType.Float, BitConverter.GetBytes(0f)));
        Check(item.Value == 1f && uiEntry.Raw.Length == 0, "zero delta and absent affix undo old effects");
        Apply(module, item);
        Check(item.Value == 1f && item.Variables.Find(entry => entry.Key == "RF_STAT_Damage") == null, "empty manifest removes stale reforge key");
        Check(ReferenceEquals(uiEntry, item.Variables.Find(entry => entry.Key == "AFX_SLOT_1")), "clearing affix retains UI entry");
        var bad = new BossRushCoopItemState { typeId = item.TypeID, variables = new List<BossRushCoopItemVariable> { slot, slot } };
        Check(!BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "duplicate keys rejected");
        bad.variables = new List<BossRushCoopItemVariable> { Variable("AFX_SLOT_1", CustomDataType.Int, BitConverter.GetBytes(2)) };
        Check(!BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "frozen key type rejected");
        bad.variables = new List<BossRushCoopItemVariable> { Variable("AFX_NAME_1", CustomDataType.String, Encoding.UTF8.GetBytes("key")) };
        Check(BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "hidden affix name preserves valid production snapshot");
        bad.variables = new List<BossRushCoopItemVariable> { Variable("RF_STAT_Damage", CustomDataType.Float, new byte[1]) };
        Check(!BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "truncated primitive rejected");
        bad.variables = new List<BossRushCoopItemVariable> { Variable("RF_STAT_Damage", CustomDataType.Float, BitConverter.GetBytes(float.NaN)) };
        Check(!BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "non-finite delta rejected");
        bad.variables = new List<BossRushCoopItemVariable> { delta }; bad.typeId++;
        Check(!BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "different item identity rejected");
        var fort = new ModeFRuntimeModule();
        fort.coopFortRequests["one"] = new CoopModeFFortificationRequest { Id="one", Item=500037, Operation="place" };
        var rejected = new CoopModeFPersonal { TransactionId="one", TransactionSucceeded=false };
        fort.GiveSucceeds=false;
        Check(!fort.Apply(rejected) && fort.coopFortRequests.ContainsKey("one") && fort.Refunded==0, "failed refund remains pending for receipt retry");
        fort.GiveSucceeds=true;
        Check(fort.Apply(rejected) && fort.Refunded==1 && !fort.coopFortRequests.ContainsKey("one"), "rejection refunds the consumed personal item");
        Check(fort.Apply(rejected) && fort.Refunded==1 && fort.GiveCalls==2, "duplicate rejected receipt never refunds twice");
        fort.coopFortRequests["two"] = new CoopModeFFortificationRequest { Id="two", Item=500040, Operation="repair" };
        Check(fort.Apply(new CoopModeFPersonal { TransactionId="two", TransactionSucceeded=true }) && fort.Refunded==1, "successful repair keeps original consumed cost");
        var hiring=new ModeERuntimeModule();var first=new CharacterMainControl();var second=new CharacterMainControl();
        CharacterMainControl.Main=first;
        for(int i=0;i<6;i++)
        { var boss=new CharacterMainControl();hiring.modeEHiredBosses[boss]=new ModeERuntimeModule.ModeEBossHireState { Owner=i<2?first:second,Character=boss }; }
        var offer=new ModeERuntimeModule.ModeEBossHireState { BasePrice=100 };
        Check(hiring.Price(offer,first)==400 && hiring.Price(offer,second)==1600, "each owner's hire price grows independently of room mates");
        var deadBoss=new CharacterMainControl();deadBoss.Health.IsDead=true;
        hiring.modeEHiredBosses[deadBoss]=new ModeERuntimeModule.ModeEBossHireState { Owner=first,Character=deadBoss };
        Check(hiring.Price(offer,first)==400, "dead hired follower does not inflate owner price");
        hiring.coopUtilityRequests["utility"] = new CoopModeEUtilityRequest { Id="utility", Item=500025 };
        var utilityRejection = new CoopModeEPersonal { UtilityId="utility", UtilitySucceeded=false };
        hiring.modeEHost.GiveSucceeds=false;
        Check(!hiring.UtilityReceipt(utilityRejection) && hiring.coopUtilityRequests.ContainsKey("utility"), "utility refund failure retains consumed-item obligation");
        hiring.modeEHost.GiveSucceeds=true;
        Check(hiring.UtilityReceipt(utilityRejection) && hiring.modeEHost.Refunded==1, "utility rejection refunds personal item once");
        Check(hiring.UtilityReceipt(utilityRejection) && hiring.modeEHost.GiveCalls==2, "utility duplicate does not repeat refund");
        hiring.coopUtilityRequests["accepted"] = new CoopModeEUtilityRequest { Id="accepted", Item=500026 };
        Check(hiring.UtilityReceipt(new CoopModeEPersonal { UtilityId="accepted", UtilitySucceeded=true }) && hiring.modeEHost.Refunded==1, "accepted world effect retains original personal cost");
        hiring.coopCoalRequests["coal"] = new CoopModeECoalRequest { Id="coal", Hire=true };
        hiring.modeEHost.GiveSucceeds=false;
        var coalRejection = new CoopModeEPersonal { CoalId="coal", CoalSucceeded=false };
        Check(!hiring.CoalReceipt(coalRejection) && hiring.coopCoalRequests.ContainsKey("coal"), "coal hire refund failure remains retryable");
        hiring.modeEHost.GiveSucceeds=true;
        Check(hiring.CoalReceipt(coalRejection) && hiring.CoalReceipt(coalRejection) && hiring.modeEHost.Refunded==2, "coal rejection and duplicate refund exactly one item");
        AffixProductionShape();
        SummonSceneLifetime();
        AssetTransactions();
        PetAsyncReplacement();
        SceneBoundConsumers();
        SummonAssetOwnership();
        SummonResultContract();
        Console.WriteLine("CoopContent PASS: replacement, duplicate, zero, empty, UI identity, typed/display contracts, item identity.");
        Console.WriteLine("CoopContent owner transactions PASS: refund retry, duplicate receipt, consumed cost, individual hire growth. No transport is simulated.");
        Console.WriteLine("CoopContent assertions: " + assertions);
    }

    private static void AffixProductionShape()
    {
        var source=new Item();Check(AffixItemData.EnsureInitialized(source,3),"actual affix initializer creates all typed empty slots");
        Check(AffixItemData.WriteSlot(source,1,"known",2),"actual writer stamps visible known affix name");
        Check(AffixItemData.ClearSlot(source,2),"actual clear writes hidden empty name");
        source.Variables.Set("AFX_SLOT_3","removed:2",true);Check(!AffixItemData.StampNameKV(source,3,"removed"),"actual unknown-name writer leaves hidden empty display entry");
        source.Variables.SetRaw("RF_STAT_Damage",CustomDataType.Float,BitConverter.GetBytes(.3f),true,false);
        var snapshot=BossRushCoopContentRuntimeModule.Snapshot(source);var target=new Item {Value=1.8f};target.Variables.SetRaw("RF_STAT_Damage",CustomDataType.Float,BitConverter.GetBytes(.8f),true,false);
        var module=new BossRushCoopContentRuntimeModule();module.Apply(target,snapshot);
        Check(Math.Abs(target.Value-1.3f)<.0001f && target.Variables.GetString("AFX_SLOT_1",null)=="known:2" && target.Variables.GetString("AFX_NAME_2",null)=="" && target.Variables.GetString("AFX_SLOT_3",null)=="removed:2","real production manifest with known empty and unknown slots restores complete RF and AFX state");
    }
    private static void SummonSceneLifetime()
    {
        BossRushCoop.IsConnected=false;var module=new BossRushCoopContentRuntimeModule();var solo=new CharacterMainControl();CharacterMainControl.Main=solo;
        FrostmourneAction.summonOwners[solo]=solo;SummonStaffAction.summonOwners[solo]=solo;module.SceneLoaded();module.SceneLoaded();
        Check(FrostmourneAction.Resets==0 && SummonStaffAction.Resets==0,"actual sceneLoaded leaves solo and absent-COOP summon caches untouched");
        CharacterMainControl owner;Check(FrostmourneAction.TryGetSummonOwner(solo,out owner)&&owner==solo && SummonStaffAction.TryGetSummonOwner(solo,out owner),"actual original summon owner lookup retains single-player ownership");
        CharacterMainControl.Main=new CharacterMainControl();Check(!FrostmourneAction.TryGetSummonOwner(solo,out owner),"replaced solo main body cannot inherit original summon kill credit");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;BossRushCoop.RunId="lifetime-room";BossRushCoop.SceneToken="lifetime-token";BossRushCoop.HasBoundScene=true;
        module.SceneLoaded();var caster=new CharacterMainControl();BossRushCoop.Players["caster"]=new BossRushCoopPlayer {Id="caster",AssetId="caster-asset",Character=caster};
        var ally=new CharacterMainControl();var action=new MonoBehaviour();BossRushCoopContentRuntimeModule.TrackSummon(ally,caster);module.TrackCast(action);
        FrostmourneAction.summonOwners[ally]=caster;SummonStaffAction.summonOwners[ally]=caster;
        module.SceneLoaded();Check(!ally.gameObject.Destroyed&&!action.Destroyed,"same actual Core additive load retains every registered summon and in-flight cast");
        Check(FrostmourneAction.TryGetSummonOwner(ally,out owner)&&owner==caster && SummonStaffAction.TryGetSummonOwner(ally,out owner),"network owner lookup uses original tables and current body asset token");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;Check(!FrostmourneAction.TryGetSummonOwner(ally,out owner)&&!SummonStaffAction.TryGetSummonOwner(ally,out owner),"native client replica cannot claim original host summon ownership");BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;
        BossRushCoop.Players["caster"].AssetId="changed";Check(!FrostmourneAction.TryGetSummonOwner(ally,out owner),"old asset summon cannot credit the current character");
        BossRushCoop.Players["caster"].AssetId="caster-asset";BossRushCoop.RunId="";module.SessionChanged();Check(!ally.gameObject.Destroyed,"empty welcome handshake preserves current summon world");
        BossRushCoop.RunId="lifetime-room";Duckov.Scenes.MultiSceneCore.Instance=new object();BossRushCoop.SceneToken="next-lifetime-token";module.SceneLoaded();
        Check(ally.gameObject.Destroyed&&action.Destroyed && FrostmourneAction.Resets==0&&SummonStaffAction.Resets==0,"actual travel retires only network registered objects and casts without resetting weapon-global solo state");
    }

    private static void SceneBoundConsumers()
    {
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.IsConnected=true;BossRushCoop.SessionReady=true;BossRushCoop.RunId="bound-room";BossRushCoop.SceneGeneration=9;
        BossRushCoop.LocalPlayerId="bound-owner";BossRushCoop.LocalAssetId="bound-asset";
        var master=new CharacterMainControl();var body=new CharacterMainControl();
        BossRushCoop.Players["bound-owner"]=new BossRushCoopPlayer {Id="bound-owner",AssetId="bound-asset",Character=master};BossRushCoop.Ai[-99]=body;
        var pet=new PetNestPetRecord {id="bound-pet",lineageKey="lineage"};PetNestService.Pets[pet.id]=pet;
        BossRushCoop.HasBoundScene=true;var ackClient=new BossRushCoopContentRuntimeModule();int beforeRequest=BossRushCoop.Sent.Count;BossRushCoopContentRuntimeModule.RequestSummon(1);
        Check(ackClient.Pending==1 && BossRushCoop.Sent.Count==beforeRequest+1,"actual summon input exists before scene unbinding");var committed=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];
        BossRushCoop.HasBoundScene=false;ackClient.Ack(new BossRushCoopMessage {IsFromHost=true,Packet=committed});
        Check(ackClient.Pending==0,"committed summon ACK retires pending input during unbound Core window");BossRushCoop.HasBoundScene=true;
        var module=new PetNestCoopRuntimeModule();
        var state=new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run="bound-room",scene=9,owner="bound-owner",asset="bound-asset",ai=-99,pet=pet,scale=.8f,stateSequence=1};
        BossRushCoop.HasBoundScene=false;int attached=PetNestCompanionSpawner.Attached;
        module.State(state);module.NativeAi(-99,body);
        Check(!module.entries.ContainsKey("bound-owner") && module.Pending==1 && PetNestCompanionSpawner.Attached==attached,"unbound Core retains pet state without binding native AI on receive or spawn event");
        BossRushCoop.HasBoundScene=true;module.NativeAi(-99,body);
        Check(module.entries.ContainsKey("bound-owner") && module.Pending==0,"bound Core consumes retained pet state through real AI spawn callback");
        var handle=module.entries["bound-owner"].Handle;BossRushCoop.HasBoundScene=false;
        module.State(new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run="bound-room",scene=9,owner="bound-owner",asset="bound-asset",ai=0,stateSequence=2});
        Check(!handle.CleanedUp && module.Pending==1,"unbound Core cannot apply old-scene pet removal");
        BossRushCoop.HasBoundScene=true;module.NativeAi(0,null);
        Check(handle.CleanedUp && module.Pending==0,"actual bound scene applies retained authoritative pet removal");
        BossRushCoop.HasBoundScene=false;int outcomes=PetNestService.Outcomes;
        module.Award(new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run="bound-room",scene=9,owner="bound-owner",asset="bound-asset",pet=pet,rewardId="bound-reward",experience=13});
        Check(pet.exp==13 && PetNestService.Outcomes==outcomes+1,"scene binding gate does not discard or delay personal experience receipt");
        BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;
        var petHost=new PetNestCoopRuntimeModule();var deploy=new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run="bound-room",scene=9,owner="bound-owner",asset="bound-asset",atBase=true,requestSequence=1,pet=pet};
        petHost.Request(deploy,"bound-owner");Check(petHost.Spawns==0,"unbound host does not accept old-scene pet deploy");
        BossRushCoop.HasBoundScene=true;petHost.Request(deploy,"bound-owner");Check(petHost.Spawns==1,"same pet deploy retries after actual Core binds");
        Check(petHost.SpawnCurrent(petHost.entries["bound-owner"],master,"bound-room",9),"actual pet await predicate retains current bound owner");
        string sameGenToken=BossRushCoop.SceneToken;BossRushCoop.SceneToken="different-native-before-gen";
        Check(!petHost.SpawnCurrent(petHost.entries["bound-owner"],master,"bound-room",9),"pet await cannot cross native travel at unchanged public generation");BossRushCoop.SceneToken=sameGenToken;
        BossRushCoop.HasBoundScene=false;
        Check(!petHost.SpawnCurrent(petHost.entries["bound-owner"],master,"bound-room",9),"actual pet await predicate rejects completion in unbound Core");
        master.CurrentHoldItemAgent=new HoldAgent {Item=new Item {TypeID=NewWeaponIds.SummonStaffTypeId}};
        var summon=new BossRushCoopContentRuntimeModule();var request=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken,id=Guid.NewGuid().ToString("N"),kind=1,run="bound-room",scene=9,asset="bound-asset"};
        var message=new BossRushCoopMessage {SenderId="bound-owner",Packet=request};int sent=BossRushCoop.Sent.Count;
        summon.Receive(message);
        Check(summon.Executed==0 && BossRushCoop.Sent.Count==sent,"unbound host cannot accept or ACK previous-scene summon input");
        BossRushCoop.HasBoundScene=true;summon.Receive(message);
        Check(summon.Executed==1 && BossRushCoop.Sent.Count==sent+1,"same summon request retries and executes after actual scene binding");
        Check(BossRushCoopContentRuntimeModule.IsSummonOwnerCurrent(master,"bound-asset",BossRushCoop.SceneToken),"bound actual summon continuation retains its owner");
        BossRushCoop.HasBoundScene=false;Check(!BossRushCoopContentRuntimeModule.IsSummonOwnerCurrent(master,"bound-asset",BossRushCoop.SceneToken),"scene unbinding cancels actual summon owner continuation");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;var client=new BossRushCoopContentRuntimeModule();
        BossRushCoopContentRuntimeModule.RequestSummon(1);Check(client.Pending==0,"unbound client cannot originate summon input under previous generation");
        client.Count(new BossRushCoopMessage {IsFromHost=true,Packet=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken,run="bound-room",scene=9,asset="bound-asset",staff=3}});
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==0,"unbound Core cannot consume old-scene summon count");
        BossRushCoop.HasBoundScene=true;client.Count(new BossRushCoopMessage {IsFromHost=true,Packet=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken,run="bound-room",scene=9,asset="bound-asset",staff=3}});
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==3,"bound Core accepts full current summon count retry");
        BossRushCoop.HasBoundScene=false;state.stateSequence=3;module.State(state);BossRushCoop.SceneGeneration=10;BossRushCoop.HasBoundScene=true;attached=PetNestCompanionSpawner.Attached;module.NativeAi(-99,body);
        Check(module.Pending==0 && !module.entries.ContainsKey("bound-owner") && PetNestCompanionSpawner.Attached==attached,"new generation discards only obsolete pet world projection rather than binding it to new AI");
        state.scene=10;state.stateSequence=4;string oldToken=BossRushCoop.SceneToken;BossRushCoop.HasBoundScene=false;module.State(state);Check(module.Pending==1,"unbound pet packet is explicitly retained under its actual native travel");
        BossRushCoop.SceneToken="new-pet-travel";BossRushCoop.HasBoundScene=true;module.NativeAi(-99,body);
        Check(module.Pending==0 && !module.entries.ContainsKey("bound-owner"),"old cached pet token cannot bind after same-generation native travel changes");
        state.stateSequence=5;module.State(state);Check(module.Pending==0 && !module.entries.ContainsKey("bound-owner"),"old pet state token is rejected before pending on new native travel");
        state.sceneToken=BossRushCoop.SceneToken;state.stateSequence=6;module.State(state);Check(module.entries.ContainsKey("bound-owner"),"current native travel pet snapshot attaches full original owner record");
        module.Award(new PetNestCoopPacket {sceneToken=oldToken,run="bound-room",scene=10,owner="bound-owner",asset="bound-asset",pet=pet,rewardId="cross-native-reward",experience=7});
        Check(pet.exp==20,"personal experience receipt survives native travel token change");
        BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;
        Check(!BossRushCoopContentRuntimeModule.IsSummonOwnerCurrent(master,"bound-asset",oldToken),"summon await captures native travel token rather than reading it after await");
        request.id=Guid.NewGuid().ToString("N");request.scene=10;sent=BossRushCoop.Sent.Count;summon.Receive(message);
        Check(BossRushCoop.Sent.Count==sent,"old native-travel summon input cannot be ACKed into a new Core at the same public generation");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;var travelClient=new BossRushCoopContentRuntimeModule();
        travelClient.Count(new BossRushCoopMessage {IsFromHost=true,Packet=new BossRushCoopSummonPacket {token=oldToken,run="bound-room",scene=10,asset="bound-asset",staff=3}});
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==0,"old native-travel summon count cannot contaminate current Core");
    }

    private static void SummonAssetOwnership()
    {
        BossRushCoop.Players.Clear(); BossRushCoop.Sent.Clear();
        BossRushCoop.IsHost=false; BossRushCoop.IsClient=true; BossRushCoop.SessionReady=true;
        BossRushCoop.RunId="room"; BossRushCoop.SceneGeneration=7; BossRushCoop.LocalAssetId="A";
        var client=new BossRushCoopContentRuntimeModule();
        BossRushCoopContentRuntimeModule.RequestSummon(1);
        BossRushCoopSummonPacket pending=BossRushCoop.Sent[0];
        Check(pending.asset=="A" && client.Pending==1, "summon request freezes original asset");
        var count=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken, run="room", scene=7, asset="A", staff=3, frost=5 };
        client.Count(new BossRushCoopMessage { IsFromHost=true, Packet=count });
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==3, "matching asset receives owner count");
        BossRushCoop.LocalAssetId="B";
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==0 && client.Pending==0, "slot change clears old request and count before local readiness");
        client.Count(new BossRushCoopMessage { IsFromHost=true, Packet=count });
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==0, "old asset count cannot block new asset skill");
        BossRushCoopContentRuntimeModule.RequestSummon(1);
        pending=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];
        var staleAck=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken, id=pending.id, run="room", scene=7, asset="A" };
        client.Ack(new BossRushCoopMessage { IsFromHost=true, Packet=staleAck });
        Check(client.Pending==1, "old asset acknowledgment cannot delete current pending input");
        client.Ack(new BossRushCoopMessage { IsFromHost=true, Packet=pending });
        Check(client.Pending==0, "matching asset acknowledgment consumes request once");
        BossRushCoop.IsHost=true; BossRushCoop.IsClient=false; Time.time=10f;
        var host=new BossRushCoopContentRuntimeModule();
        int expectedCasts=0;
        for(int i=0;i<16;i++)
        {
            string id="player"+i;
            var character=new CharacterMainControl { CurrentHoldItemAgent=new HoldAgent { Item=new Item { TypeID=NewWeaponIds.SummonStaffTypeId } } };
            var player=new BossRushCoopPlayer { Id=id, AssetId="A", Character=character };
            BossRushCoop.Players[id]=player;
            var packet=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken, id=Guid.NewGuid().ToString("N"), run="room", scene=7, asset="A", kind=1 };
            var message=new BossRushCoopMessage { SenderId=id, Packet=packet };
            host.Receive(message); host.Receive(message);
            expectedCasts++;
            Check(host.Executed==expectedCasts, "full room duplicate input executes once per owner");
            packet=new BossRushCoopSummonPacket {token=BossRushCoop.SceneToken, id=Guid.NewGuid().ToString("N"), run="room", scene=7, asset="B", kind=1 };
            host.Receive(new BossRushCoopMessage { SenderId=id, Packet=packet });
            Check(host.Executed==expectedCasts, "unbound asset rejected before acceptance and cooldown");
            var summon=new CharacterMainControl();
            BossRushCoopContentRuntimeModule.TrackSummon(summon,character);
            Check(BossRushCoopContentRuntimeModule.IsSummonOwnedBy(summon,character), "current summoned body belongs to original asset");
            player.AssetId="B";
            Check(!BossRushCoopContentRuntimeModule.IsSummonOwnerCurrent(character,"A",BossRushCoop.SceneToken), "await continuation invalidated by slot change on same character");
            Check(!BossRushCoopContentRuntimeModule.IsSummonOwnedBy(summon,character), "old summoned body excluded before deferred destroy");
            BossRushCoopContentRuntimeModule.PruneSummonOwners();
            Check(!summon.gameObject.Active && summon.gameObject.Destroyed, "host disables previous asset body before deferred destroy without applying combat damage");
            host.Receive(new BossRushCoopMessage { SenderId=id, Packet=packet });
            expectedCasts++;
            Check(host.Executed==expectedCasts, "new slot fresh input uses independent cooldown and previously rejected id is accepted");
        }
        BossRushCoop.IsHost=false;
        Console.WriteLine("CoopContent summon ownership PASS: 16 owners, duplicates, asset scopes, counts, cooldown, await owner validity, stale-body retirement. Transport and character factory are substitutes.");
    }

    private static void SummonResultContract()
    {
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.SessionReady=true;BossRushCoop.HasBoundScene=false;
        var guest=new BossRushCoopContentRuntimeModule();bool? outcome=null;int notices=ModBehaviour.Instance.Messages;
        Check(!BossRushCoopContentRuntimeModule.RequestSummon(1,ok=>outcome=ok) && outcome==false && guest.Pending==0 && ModBehaviour.Instance.Messages==notices+1,"unbound actual input reports rejection and retains no phantom pending action");
        BossRushCoop.HasBoundScene=true;BossRushCoop.LocalAssetId="result-asset";BossRushCoop.LocalPlayerId="result-owner";BossRushCoop.RunId="result-room";
        outcome=null;Check(BossRushCoopContentRuntimeModule.RequestSummon(1,ok=>outcome=ok),"bound client queues actual summon request");var packet=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];
        var caster=new CharacterMainControl();BossRushCoop.Players["result-owner"]=new BossRushCoopPlayer {Id="result-owner",AssetId="result-asset",Character=caster};
        BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;var host=new BossRushCoopContentRuntimeModule();host.Receive(new BossRushCoopMessage {SenderId="result-owner",Packet=packet});
        var denied=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];Check(!denied.accepted&&host.Executed==0,"host hand-held lag is a negative execution result");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;guest.Ack(new BossRushCoopMessage {IsFromHost=true,Packet=denied});
        Check(outcome==false&&guest.Pending==0&&ModBehaviour.Instance.Messages==notices+2,"negative host result reaches real callback and visible cooldown-refund notice");
        caster.CurrentHoldItemAgent=new HoldAgent {Item=new Item {TypeID=NewWeaponIds.SummonStaffTypeId}};BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;Time.time=10;
        packet.id=Guid.NewGuid().ToString("N");host.Receive(new BossRushCoopMessage {SenderId="result-owner",Packet=packet});Time.time=21.6f;
        packet.id=Guid.NewGuid().ToString("N");host.Receive(new BossRushCoopMessage {SenderId="result-owner",Packet=packet});
        Check(host.Executed==2&&BossRushCoop.Sent[BossRushCoop.Sent.Count-1].accepted,"host cooldown allows bounded arrival jitter while retaining per-owner duplicate dedup");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.HasBoundScene=false;new BossRushCoopContentRuntimeModule();Time.time=50;
        var staffAction=new SummonStaffAction();staffAction.Begin();staffAction.Frame();var frostAction=new FrostmourneAction();frostAction.Begin();frostAction.Frame();
        Check(staffAction.CooldownAt==-999f&&!staffAction.Cooling&&frostAction.CooldownAt==-999f&&!frostAction.Cooling,"actual ability input and actual base OnStop refund both unsent summon cooldowns");
        BossRushCoop.HasBoundScene=true;var actionGuest=new BossRushCoopContentRuntimeModule();staffAction.Begin();staffAction.Frame();var firstCast=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];
        Check(staffAction.CooldownAt==Time.time&&staffAction.Cooling,"queued ability follows actual base cooldown assignment");
        firstCast.accepted=false;actionGuest.Ack(new BossRushCoopMessage {IsFromHost=true,Packet=firstCast});
        Check(staffAction.CooldownAt==-999f&&!staffAction.Cooling,"host negative result refunds a stopped actual action cooldown");
        staffAction.Begin();staffAction.Frame();firstCast=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];Time.time=70;staffAction.Begin();staffAction.Frame();
        firstCast.accepted=false;actionGuest.Ack(new BossRushCoopMessage {IsFromHost=true,Packet=firstCast});
        Check(staffAction.CooldownAt==70&&staffAction.Cooling,"late failure cannot refund a newer actual ability attempt");
        frostAction.Begin();frostAction.Frame();var oldFrost=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];Time.time=90;frostAction.Begin();frostAction.Frame();
        oldFrost.accepted=false;actionGuest.Ack(new BossRushCoopMessage {IsFromHost=true,Packet=oldFrost});Check(frostAction.CooldownAt==90&&frostAction.Cooling,"late frost failure cannot refund its newer actual ability attempt");
        var cancelGuest=new BossRushCoopContentRuntimeModule();int cancelled=0;BossRushCoopContentRuntimeModule.RequestSummon(1,ok=>{if(!ok)cancelled++;});
        BossRushCoop.LocalAssetId="switched-result-asset";BossRushCoopContentRuntimeModule.LocalSummonCount(1);BossRushCoopContentRuntimeModule.LocalSummonCount(1);
        Check(cancelled==1&&cancelGuest.Pending==0,"slot replacement cancels each old actual input callback exactly once");

    }

    private static void AssetTransactions()
    {
        BossRushCoop.LocalAssetId = "B"; BossRushCoop.RunId = "room"; BossRushCoop.IsConnected = true;
        var fort = new ModeFRuntimeModule(); var personal = new ModeERuntimeModule();
        fort.coopFortRequests["fort"] = new CoopModeFFortificationRequest { Id="fort", Item=500037, AssetId="A", Run="room" };
        personal.coopUtilityRequests["utility"] = new CoopModeEUtilityRequest { Id="utility", Item=500025, AssetId="A", Run="room" };
        personal.coopCoalRequests["coal"] = new CoopModeECoalRequest { Id="coal", Hire=true, AssetId="A", Run="room" };
        Check(!fort.Apply(new CoopModeFPersonal { TransactionId="fort" }) && fort.GiveCalls==0, "wrong asset cannot confirm or refund old fortification");
        Check(!personal.UtilityReceipt(new CoopModeEPersonal { UtilityId="utility" }) && personal.modeEHost.GiveCalls==0, "wrong asset cannot confirm or refund old utility");
        Check(!personal.CoalReceipt(new CoopModeEPersonal { CoalId="coal" }) && personal.modeEHost.GiveCalls==0, "wrong asset cannot confirm or refund old pet hire");
        fort.Cancel(); personal.CancelUtilities(); personal.CancelCoal();
        Check(fort.coopFortRequests.Count==1 && personal.coopUtilityRequests.Count==1 && personal.coopCoalRequests.Count==1, "wrong asset cleanup preserves original obligations");
        BossRushCoop.LocalAssetId="A";
        fort.Cancel(); personal.CancelUtilities(); personal.CancelCoal();
        Check(fort.GiveCalls==0 && personal.modeEHost.GiveCalls==0, "same room cleanup cannot refund unknown world outcomes");
        Check(fort.Apply(new CoopModeFPersonal { TransactionId="fort", TransactionSucceeded=true }) && fort.Refunded==0, "original asset accepts prior successful world commit without refund");
        Check(personal.UtilityReceipt(new CoopModeEPersonal { UtilityId="utility" }) && personal.CoalReceipt(new CoopModeEPersonal { CoalId="coal" }) && personal.modeEHost.Refunded==2, "original asset refunds confirmed rejected transactions");
        BossRushCoop.LocalAssetId="B"; BossRushCoop.RunId="new-room";
        fort.coopFortRequests["old"] = new CoopModeFFortificationRequest { Id="old", Item=500037, AssetId="A", Run="room" };
        personal.coopUtilityRequests["old"] = new CoopModeEUtilityRequest { Id="old", Item=500025, AssetId="A", Run="room" };
        personal.coopCoalRequests["old"] = new CoopModeECoalRequest { Id="old", Hire=true, AssetId="A", Run="room" };
        fort.Cancel(); personal.CancelUtilities(); personal.CancelCoal();
        Check(fort.Refunded==0 && personal.modeEHost.Refunded==2, "old room refund is frozen until original asset returns");
        BossRushCoop.LocalAssetId="A"; fort.GiveSucceeds=false; personal.modeEHost.GiveSucceeds=false;
        fort.Cancel(); personal.CancelUtilities(); personal.CancelCoal();
        Check(fort.coopFortRequests.Count==1 && personal.coopUtilityRequests.Count==1 && personal.coopCoalRequests.Count==1, "failed cancellation refund stays pending for retry");
        fort.GiveSucceeds=true; personal.modeEHost.GiveSucceeds=true;
        fort.Cancel(); personal.CancelUtilities(); personal.CancelCoal(); fort.Cancel(); personal.CancelUtilities(); personal.CancelCoal();
        Check(fort.Refunded==1 && personal.modeEHost.Refunded==4 && fort.coopFortRequests.Count==0 && personal.coopUtilityRequests.Count==0 && personal.coopCoalRequests.Count==0, "old room cancellation retries and refunds exactly once in original asset");
    }

    private static void PetAsyncReplacement()
    {
        var module = new PetNestCoopRuntimeModule();
        var old = new PetNestCoopRuntimeModule.Entry { Owner="owner", Spawning=true };
        var replacement = new PetNestCoopRuntimeModule.Entry { Owner="owner", Spawning=true };
        module.entries["owner"] = replacement;
        module.Finish(old);
        Check(!old.Spawning && ReferenceEquals(module.entries["owner"], replacement) && replacement.Spawning, "cancelled old spawn cannot remove replacement deployment");
        module.Finish(replacement);
        Check(!replacement.Spawning && !module.entries.ContainsKey("owner"), "current failed spawn releases itself for retry");
        var active = new PetNestCoopRuntimeModule.Entry { Owner="owner", Spawning=true, Handle=new PetNestCompanionHandle() };
        module.entries["owner"] = active; module.Finish(active); module.Finish(old);
        Check(!active.Spawning && ReferenceEquals(module.entries["owner"], active), "completed current spawn retains active pet when stale continuation finishes");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.RunId="pet-room";BossRushCoop.SceneGeneration=5;
        BossRushCoop.LocalPlayerId="pet-owner";BossRushCoop.LocalAssetId="pet-asset";
        var master=new CharacterMainControl();var body=new CharacterMainControl();
        BossRushCoop.Players["pet-owner"]=new BossRushCoopPlayer {Id="pet-owner",AssetId="pet-asset",Character=master};BossRushCoop.Ai[-7]=body;
        var pet=new PetNestPetRecord {id="pet",lineageKey="lineage"};PetNestService.Pets[pet.id]=pet;
        var state=new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run="pet-room",scene=5,owner="pet-owner",asset="pet-asset",ai=-7,pet=pet,scale=.75f,stateSequence=1};
        module.State(state);var original=module.entries["pet-owner"].Handle;
        Check(original.Character==body && original.Agent.Master==master && PetNestCompanionRuntime.Bound==original,"personal native replica attaches original owner and active pet interface");
        state.stateSequence=2;module.State(state);
        Check(ReferenceEquals(module.entries["pet-owner"].Handle,original) && PetNestCompanionSpawner.Attached==1,"repeated current body projection preserves original pet handle");
        body.Destroyed=true;body.Health.Destroyed=true;body.gameObject.Destroyed=true;
        var replacementBody=new CharacterMainControl();BossRushCoop.Ai[-7]=replacementBody;state.stateSequence=3;module.State(state);
        var rebound=module.entries["pet-owner"].Handle;
        Check(original.CleanedUp && rebound.Character==replacementBody && PetNestCompanionRuntime.Bound==rebound && PetNestCompanionSpawner.Attached==2,"same AI identity with replacement body releases old handle and reattaches personal interfaces");
        var newMaster=new CharacterMainControl();BossRushCoop.Players["pet-owner"].Character=newMaster;state.stateSequence=4;module.State(state);
        Check(rebound.Agent.Master==newMaster && rebound.Agent.BindCalls==1,"respawned owner rebinds the same pet body immediately on current state");
        module.State(new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run="pet-room",scene=5,owner="pet-owner",ai=0,stateSequence=5});
        Check(!module.entries.ContainsKey("pet-owner") && rebound.CleanedUp,"authoritative release removes personal pet handle");
        int attached=PetNestCompanionSpawner.Attached;state.stateSequence=4;module.State(state);
        Check(!module.entries.ContainsKey("pet-owner") && PetNestCompanionSpawner.Attached==attached,"delayed deployed state cannot resurrect an already released pet");
        PetRequestOrderAndReconnect();
        Console.WriteLine("CoopContent asset and pet async cases PASS: slot A/B, unknown outcomes, deferred/refailed cancellation, exact current-entry ownership. No transport or async engine is simulated.");
    }

    private static void PetRequestOrderAndReconnect()
    {
        BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;BossRushCoop.RunId="pet-order-room";BossRushCoop.SceneGeneration=8;
        LevelManager.Instance=new LevelManager {IsBaseLevel=true};
        var module=new PetNestCoopRuntimeModule();
        var packet=new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run=BossRushCoop.RunId,scene=8,owner="pet-owner",asset="pet-asset",atBase=true,requestSequence=1,
            pet=new PetNestPetRecord {id="first",lineageKey="lineage"}};
        module.Request(packet,"pet-owner");Check(module.Spawns==1&&module.entries["pet-owner"].Pet.id=="first","first personal deploy uses production host request path");
        module.Request(packet,"pet-owner");Check(module.Spawns==1,"duplicate request cannot spawn twice");
        var release=new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run=packet.run,scene=8,owner=packet.owner,asset=packet.asset,requestSequence=3};
        module.Release(release,"pet-owner");packet.requestSequence=2;module.Request(packet,"pet-owner");
        Check(module.Spawns==1&&!module.entries.ContainsKey("pet-owner"),"release revision rejects an older deploy instead of resurrecting the pet");
        packet.requestSequence=4;packet.pet.id="second";module.Request(packet,"pet-owner");
        Check(module.Spawns==2&&module.entries["pet-owner"].Pet.id=="second","new deployment after release still creates the chosen pet");
        release.requestSequence=3;module.Release(release,"pet-owner");Check(module.entries.ContainsKey("pet-owner"),"old release cannot remove a newer deployment");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.LocalPlayerId="pet-owner";BossRushCoop.LocalAssetId="pet-asset";
        var pet=new PetNestPetRecord {id="second",lineageKey="lineage"};PetNestService.Pets[pet.id]=pet;int outcomes=PetNestService.Outcomes;
        var award=new PetNestCoopPacket {sceneToken=BossRushCoop.SceneToken,run=packet.run,scene=8,owner=packet.owner,asset=packet.asset,pet=pet,experience=20,rewardId="unacked"};
        PetNestService.AcceptOutcome=false;module.Award(award);Check(pet.exp==0,"failed personal outcome does not become a delivered experience receipt");
        PetNestService.AcceptOutcome=true;module.Award(award);Check(pet.exp==20&&PetNestService.Outcomes==outcomes+1,"personal experience applies through the production award callback");
        module.killBudgets["pet-owner:pet-asset"]=77;module.outstanding["pending"]=award;
        BossRushCoop.RunId="";module.ClearNow();BossRushCoop.RunId=packet.run;module.ClearNow();module.Award(award);
        Check(pet.exp==20&&PetNestService.Outcomes==outcomes+1&&module.killBudgets["pet-owner:pet-asset"]==77&&module.outstanding.Count==1,
            "same room handshake retains experience dedup pending facts and run budget");
        BossRushCoop.RunId="new-pet-room";module.ClearNow();Check(module.killBudgets.Count==0&&module.outstanding.Count==0,"actual new room clears previous room personal facts");
    }
}

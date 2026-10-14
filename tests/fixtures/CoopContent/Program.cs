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
        Check(!BossRushCoopContentRuntimeModule.Validate(bad, item.TypeID), "display contract rejected");
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
        AssetTransactions();
        PetAsyncReplacement();
        SummonAssetOwnership();
        Console.WriteLine("CoopContent PASS: replacement, duplicate, zero, empty, UI identity, typed/display contracts, item identity.");
        Console.WriteLine("CoopContent owner transactions PASS: refund retry, duplicate receipt, consumed cost, individual hire growth. No transport is simulated.");
        Console.WriteLine("CoopContent assertions: " + assertions);
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
        var count=new BossRushCoopSummonPacket { run="room", scene=7, asset="A", staff=3, frost=5 };
        client.Count(new BossRushCoopMessage { IsFromHost=true, Packet=count });
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==3, "matching asset receives owner count");
        BossRushCoop.LocalAssetId="B";
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==0 && client.Pending==0, "slot change clears old request and count before local readiness");
        client.Count(new BossRushCoopMessage { IsFromHost=true, Packet=count });
        Check(BossRushCoopContentRuntimeModule.LocalSummonCount(1)==0, "old asset count cannot block new asset skill");
        BossRushCoopContentRuntimeModule.RequestSummon(1);
        pending=BossRushCoop.Sent[BossRushCoop.Sent.Count-1];
        var staleAck=new BossRushCoopSummonPacket { id=pending.id, run="room", scene=7, asset="A" };
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
            var packet=new BossRushCoopSummonPacket { id=Guid.NewGuid().ToString("N"), run="room", scene=7, asset="A", kind=1 };
            var message=new BossRushCoopMessage { SenderId=id, Packet=packet };
            host.Receive(message); host.Receive(message);
            expectedCasts++;
            Check(host.Executed==expectedCasts, "full room duplicate input executes once per owner");
            packet=new BossRushCoopSummonPacket { id=Guid.NewGuid().ToString("N"), run="room", scene=7, asset="B", kind=1 };
            host.Receive(new BossRushCoopMessage { SenderId=id, Packet=packet });
            Check(host.Executed==expectedCasts, "unbound asset rejected before acceptance and cooldown");
            var summon=new CharacterMainControl();
            BossRushCoopContentRuntimeModule.TrackSummon(summon,character);
            Check(BossRushCoopContentRuntimeModule.IsSummonOwnedBy(summon,character), "current summoned body belongs to original asset");
            player.AssetId="B";
            Check(!BossRushCoopContentRuntimeModule.IsSummonOwnerCurrent(character,"A"), "await continuation invalidated by slot change on same character");
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
        var state=new PetNestCoopPacket {run="pet-room",scene=5,owner="pet-owner",asset="pet-asset",ai=-7,pet=pet,scale=.75f,stateSequence=1};
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
        module.State(new PetNestCoopPacket {run="pet-room",scene=5,owner="pet-owner",ai=0,stateSequence=5});
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
        var packet=new PetNestCoopPacket {run=BossRushCoop.RunId,scene=8,owner="pet-owner",asset="pet-asset",atBase=true,requestSequence=1,
            pet=new PetNestPetRecord {id="first",lineageKey="lineage"}};
        module.Request(packet,"pet-owner");Check(module.Spawns==1&&module.entries["pet-owner"].Pet.id=="first","first personal deploy uses production host request path");
        module.Request(packet,"pet-owner");Check(module.Spawns==1,"duplicate request cannot spawn twice");
        var release=new PetNestCoopPacket {run=packet.run,scene=8,owner=packet.owner,asset=packet.asset,requestSequence=3};
        module.Release(release,"pet-owner");packet.requestSequence=2;module.Request(packet,"pet-owner");
        Check(module.Spawns==1&&!module.entries.ContainsKey("pet-owner"),"release revision rejects an older deploy instead of resurrecting the pet");
        packet.requestSequence=4;packet.pet.id="second";module.Request(packet,"pet-owner");
        Check(module.Spawns==2&&module.entries["pet-owner"].Pet.id=="second","new deployment after release still creates the chosen pet");
        release.requestSequence=3;module.Release(release,"pet-owner");Check(module.entries.ContainsKey("pet-owner"),"old release cannot remove a newer deployment");
        BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;BossRushCoop.LocalPlayerId="pet-owner";BossRushCoop.LocalAssetId="pet-asset";
        var pet=new PetNestPetRecord {id="second",lineageKey="lineage"};PetNestService.Pets[pet.id]=pet;int outcomes=PetNestService.Outcomes;
        var award=new PetNestCoopPacket {run=packet.run,scene=8,owner=packet.owner,asset=packet.asset,pet=pet,experience=20,rewardId="unacked"};
        PetNestService.AcceptOutcome=false;module.Award(award);Check(pet.exp==0,"failed personal outcome does not become a delivered experience receipt");
        PetNestService.AcceptOutcome=true;module.Award(award);Check(pet.exp==20&&PetNestService.Outcomes==outcomes+1,"personal experience applies through the production award callback");
        module.killBudgets["pet-owner:pet-asset"]=77;module.outstanding["pending"]=award;
        BossRushCoop.RunId="";module.ClearNow();BossRushCoop.RunId=packet.run;module.ClearNow();module.Award(award);
        Check(pet.exp==20&&PetNestService.Outcomes==outcomes+1&&module.killBudgets["pet-owner:pet-asset"]==77&&module.outstanding.Count==1,
            "same room handshake retains experience dedup pending facts and run budget");
        BossRushCoop.RunId="new-pet-room";module.ClearNow();Check(module.killBudgets.Count==0&&module.outstanding.Count==0,"actual new room clears previous room personal facts");
    }
}

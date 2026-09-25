using System;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static void Assert(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static ZombieModeTemporaryNpc Service(string name)
    {
        var npc=new ZombieModeTemporaryNpc {ServiceType=name,GameObject=new GameObject(name),ServiceState=new ZombieModeNpcServiceState()};
        npc.ServiceState.MerchantStockRemaining.Add(2); npc.ServiceState.NurseUsesRemaining.Add(2); return npc;
    }
    private static void TestServiceTransactions()
    {
        var state=new ZombieModeRunState {RunId=7,PurificationPoints=100,CurrentWave=1};
        var module=new ZombieModeRuntimeModule(state);
        var merchant=Service("Merchant"); var nurse=Service("Nurse"); state.TemporaryNpcs.Add(merchant); state.TemporaryNpcs.Add(nurse);
        ZombieModeNpcCatalog.NormalWaveStock=new[] {new ZombieModeNpcCatalog.MerchantStockEntry {BasePrice=20,TypeId=4,DisplayKey="grant"}};
        ZombieModeNpcCatalog.BossNodeStock=ZombieModeNpcCatalog.NormalWaveStock;
        ZombieModeNpcCatalog.NurseServices=new[] {new ZombieModeNpcCatalog.NurseServiceEntry {BasePrice=15,ServiceKey="heal"}};
        int calls=0;
        module.ItemGrant=id=> {calls++; Assert(state.PurificationPoints==80,"Merchant must deduct before item delivery"); Assert(merchant.ServiceState.MerchantStockRemaining[0]==2,"Stock must not decrease before delivery"); return false;};
        Assert(!module.TryPurchaseZombieModeMerchantStock(8,"Merchant",0) && calls==0,"Stale run must not deliver rewards");
        Assert(!module.TryPurchaseZombieModeMerchantStock(7,"Merchant",0) && state.PurificationPoints==100 && merchant.ServiceState.MerchantStockRemaining[0]==2,"Failed item delivery must refund without consuming stock");
        module.ItemGrant=id=> {Assert(state.PurificationPoints==80,"Successful grant must also follow deduction"); return true;};
        Assert(module.TryPurchaseZombieModeMerchantStock(7,"Merchant",0) && state.PurificationPoints==80 && merchant.ServiceState.MerchantStockRemaining[0]==1,"Successful delivery must consume exactly one stock");
        module.NurseEffect=id=> {Assert(state.PurificationPoints==65,"Nurse effect must follow deduction"); return false;};
        Assert(!module.TryUseZombieModeNurseService(7,"Nurse",0) && state.PurificationPoints==80 && nurse.ServiceState.NurseUsesRemaining[0]==2,"Failed healing must refund without consuming uses");
        module.NurseEffect=id=>true;
        Assert(module.TryUseZombieModeNurseService(7,"Nurse",0) && state.PurificationPoints==65 && nurse.ServiceState.NurseUsesRemaining[0]==1,"Successful healing must consume one use");
    }
    private static void TestEventOwnership()
    {
        var aState=new ZombieModeRunState {RunId=1}; var bState=new ZombieModeRunState {RunId=2};
        aState.OptionRuntime.MutatorBulletTimeEnabled=true; bState.OptionRuntime.MutatorGuardianShieldEnabled=true;
        var a=new ZombieModeRuntimeModule(aState); var b=new ZombieModeRuntimeModule(bState);
        var original=new CharacterMainControl(); CharacterMainControl.Main=original;
        a.RegisterEffects(); a.RegisterEffects(); b.RegisterEffects();
        Assert(original.Health.OnHealthChange.Count==2 && original.HoldSubscribers==2 && a.Cleanups.Count==1,"Each module must own exactly one health/hold subscription");
        var replacement=new CharacterMainControl(); CharacterMainControl.Main=replacement; a.RegisterEffects();
        Assert(original.Health.OnHealthChange.Count==1 && replacement.Health.OnHealthChange.Count==1 && original.HoldSubscribers==1,"Player replacement must detach only that module's old listeners");
        a.AddSpreadSnapshot(1); b.AddSpreadSnapshot(1); Probe.Trace.Clear(); a.RemoveZombieModeOptionRuntimeEffects();
        Assert(a.SnapshotCount==0 && b.SnapshotCount==1 && replacement.Health.OnHealthChange.Count==0 && replacement.HoldSubscribers==0,"Cleanup must remove this owner only");
        Assert(Probe.Trace.IndexOf("phase2-remove")<Probe.Trace.IndexOf("ZombieMode Projectile Spread") && Probe.Trace.IndexOf("ZombieMode Projectile Spread")<Probe.Trace.IndexOf("health-remove") && Probe.Trace.IndexOf("health-remove")<Probe.Trace.IndexOf("options-reset"),"Option cleanup order must remain phase2/spread/health/reset");
        aState.OptionRuntime.MutatorBulletTimeEnabled=true;
        a.RegisterEffects(); Assert(a.Cleanups.Count==2 && replacement.Health.OnHealthChange.Count==1,"Cleanup must release subscription registration sentinel");
        a.Cleanups[a.Cleanups.Count-1](); a.RemoveZombieModeOptionRuntimeEffects(); b.RemoveZombieModeOptionRuntimeEffects();
        Assert(original.Health.OnHealthChange.Count==0 && original.HoldSubscribers==0 && replacement.Health.OnHealthChange.Count==0 && replacement.HoldSubscribers==0,"Run cleanup must leave no event subscriptions");
    }
    private static void TestNpcBindingAndRecycle()
    {
        var state=new ZombieModeRunState {RunId=4}; var module=new ZombieModeRuntimeModule(state);
        int resolves=0,interactions=0; var prefab=new GameObject("courier-prefab");
        module.BindZombieModeTemporaryNpcServices(kind=> {resolves++; Assert(kind=="Courier","Courier must request the same NPC resource"); Probe.Trace.Add("resolve"); return prefab;}, npc=>
        {interactions++; Assert(npc.GetComponent<CourierMovement>()!=null && npc.GetComponent<CourierNPCController>()!=null,"Interaction must be wired after runtime components"); Probe.Trace.Add("interaction");});
        Probe.Trace.Clear(); var created=module.CreateCourier();
        Assert(resolves==1 && interactions==1 && Probe.Trace.IndexOf("resolve")<Probe.Trace.IndexOf("instantiate:courier-prefab") && Probe.Trace.IndexOf("talking:False")<Probe.Trace.IndexOf("interaction"),"NPC resource and interaction calls must retain order and multiplicity");
        var retained=new GameObject("retained"); var bound=new GameObject("bound"); var boundComponent=bound.AddComponent<CourierMovement>();
        state.TemporaryRealNpcs.Add(new ZombieModeTemporaryRealNpcRecord {GameObject=retained});
        state.TemporaryRealNpcs.Add(new ZombieModeTemporaryRealNpcRecord {GameObject=bound,SafeZoneBound=true});
        module.ObjectCleanups[bound]=()=>Probe.Close("callback",bound.transform);
        Probe.Trace.Clear(); module.RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(5);
        Assert(bound!=null,"Stale cleanup must retain live NPCs");
        module.RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(4);
        Assert(bound==null && boundComponent==null && retained!=null && state.TemporaryRealNpcs.Count==1,"Bound recycling must preserve unbound NPC and destroy components");
        Assert(string.Join(",",Probe.Trace)=="remove-record:bound,callback:bound,destroy:bound","Bound cleanup must remove record and invoke cleanup before destruction");
        state.TemporaryRealNpcs.Add(new ZombieModeTemporaryRealNpcRecord {GameObject=created});
        Probe.Trace.Clear(); module.RecycleZombieModeTemporaryRealNpcs(4);
        Assert(state.TemporaryRealNpcs.Count==0 && created==null && retained==null,"Run completion must destroy all real NPCs");
        Assert(Probe.Trace[0]=="shop:ZombieMode_TemporaryRealNpc_Courier" && Probe.Trace[4]=="sweep:ZombieMode_TemporaryRealNpc_Courier" && Probe.Trace[5]=="destroy:ZombieMode_TemporaryRealNpc_Courier","Real NPC reverse cleanup must close all five services before destroy");
    }
    public static int Main()
    {
        TestServiceTransactions(); TestEventOwnership(); TestNpcBindingAndRecycle();
        Console.WriteLine("ZombieModeRewardRuntime PASS: service deduction/refund, event ownership, cleanup order, NPC binding/recycling"); return 0;
    }
}

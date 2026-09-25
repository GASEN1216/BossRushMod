using System;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static void Assert(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private static ZombieModeRuntimeModule New(out ZombieModeRunState state)
    {
        Probe.Trace.Clear();Probe.Grants.Clear();Probe.Ammo=true;Probe.Enough=true;Probe.ThrowEconomy=false;
        state=new ZombieModeRunState(); return new ZombieModeRuntimeModule(new ModBehaviour(),state);
    }
    private static void Starter()
    {
        ZombieModeRunState state;var module=New(out state); module.SelectZombieModeStarterLoadout(1,ZombieModeStarterLoadout.Melee);
        Assert(string.Join(",",Probe.Trace)=="grant:MeleeWeapon,grant:Healing,grant:Healing,grant-many:Medic:3,grant-many:Food:3,grant-many:Drink:2,grant:BodyArmor,grant:Helmet,grant:Headset,finalize,unlock:Active,prepare:InitialPreparation,banner","Melee grant and entry state transition order must remain unchanged");
        Assert(state.StarterLoadout==ZombieModeStarterLoadout.Melee && state.LifecyclePhase==ZombieModeLifecyclePhase.Active,"Success must record loadout and activate the run");
        module=New(out state);Probe.Grants.Enqueue(true);Probe.Grants.Enqueue(true);Probe.Grants.Enqueue(false);module.SelectZombieModeStarterLoadout(1,ZombieModeStarterLoadout.Melee);
        Assert(string.Join(",",Probe.Trace)=="grant:MeleeWeapon,grant:Healing,grant:Healing,fail" && state.LifecyclePhase==ZombieModeLifecyclePhase.WaitingStarterChoice,"Missing guaranteed healing must fail before resources finalize or run activation");
        module=New(out state);Probe.Ammo=false;module.SelectZombieModeStarterLoadout(1,ZombieModeStarterLoadout.Gunner);
        Assert(string.Join(",",Probe.Trace)=="select-gun,instantiate-gun,caliber,send-gun,ammo:9mm:2000,fail" && state.StarterAmmoCaliber=="9mm","Gunner must retain gun/caliber/ammo order and fail without active transition when ammo fails");
        module=New(out state);module.SelectZombieModeStarterLoadout(2,ZombieModeStarterLoadout.Melee);Assert(Probe.Trace.Count==0,"Stale run selection must not grant items");
    }
    private static void Cash()
    {
        ZombieModeRunState state;var module=New(out state);string failure;
        Assert(!module.ConfigureZombieModePendingCashInvestment(100,out failure) && state.PendingCashInvestment==0,"Cash editing requires SelectingMap");
        state.LifecyclePhase=ZombieModeLifecyclePhase.SelectingMap;state.PendingCashInvestment=700;Probe.Enough=false;
        Assert(!module.ConfigureZombieModePendingCashInvestment(900,out failure) && state.PendingCashInvestment==700,"Insufficient cash must retain the prior choice");
        Probe.ThrowEconomy=true;Assert(!module.ConfigureZombieModePendingCashInvestment(900,out failure) && state.PendingCashInvestment==700,"Economy exceptions must reject without changing the prior choice");
        Probe.Trace.Clear();Assert(module.ConfigureZombieModePendingCashInvestment(-1,out failure) && state.PendingCashInvestment==0 && Probe.Trace.Count==0,"Negative cash must clamp to zero without querying the economy");
        Probe.ThrowEconomy=false;Probe.Enough=true;Assert(module.ConfigureZombieModePendingCashInvestment(299,out failure) && module.PreviewZombieModeInitialPurificationPoints()==2,"Cash preview must floor by the configured ratio");
        state.PendingCashInvestment=long.MaxValue;Assert(module.PreviewZombieModeInitialPurificationPoints()==int.MaxValue,"Large cash preview must preserve integer saturation");
    }
    private static void PurificationAndIsolation()
    {
        ZombieModeRunState state;var module=New(out state);var first=new ZombiePurificationStar { PointsValue=2,Visual=new GameObject("first") };var second=new ZombiePurificationStar { PointsValue=3,Visual=new GameObject("second") };
        state.PendingPurificationStars.Add(first);state.PendingPurificationStars.Add(second);module.ForceCollectZombieModePendingPurificationStars(1);
        Assert(string.Join(",",Probe.Trace)=="absorb:second,absorb:first" && state.PendingPurificationStars.Count==0 && state.PurificationPoints==5,"Force collect must settle stars in reverse order without skipping removal");
        module.CollectZombieModePurificationPoint(1,99,null,first);Assert(state.PurificationPoints==5,"Settled stars must not award twice");
        var stale=new ZombiePurificationStar { PointsValue=9,Visual=new GameObject("stale") };state.PendingPurificationStars.Add(stale);var transform=stale.Visual.transform;UnityEngine.Object.Destroy(stale.Visual);Assert(transform==null,"Scene object destruction must cascade to components");
        module.CollectZombieModePurificationPoint(2,9,stale.Visual,stale);Assert(stale.Settled && stale.Visual==null && state.PendingPurificationStars.Count==0 && state.PurificationPoints==5,"Stale collection must remove bookkeeping while rejecting the award");
        Probe.ResourceScans=0;module.PrepareSoulCubePrefabCacheForZombieRun();module.PrepareSoulCubePrefabCacheForZombieRun();Assert(Probe.ResourceScans==2,"SoulCube cache misses must be shared across run preparation without rescanning");
        Probe.Trace.Clear();Assert(module.ApplyZombieModeMapIsolationShell(1),"Valid run must isolate its map");module.RestoreZombieModeMapIsolationShell();
        Assert(string.Join(",",Probe.Trace)=="precache,reset-spawners,disable-spawners,disable-extraction,disable-characters,restore-characters,reset-spawners,restore-extraction","Map precache, isolation and restoration order changed");
    }
    private static void VisualCleanup()
    {
        Probe.Trace.Clear();var alive=new GameObject("alive");var destroyed=new GameObject("destroyed");UnityEngine.Object.Destroy(destroyed);
        var marker=new ZombieModeEnemyRuntimeMarker();marker.VisualScaleRecords.Add(new ZombieModeVisualScaleRecord { Target=alive.transform,OriginalScale=new Vector3(2,3,4) });marker.VisualScaleRecords.Add(new ZombieModeVisualScaleRecord { Target=destroyed.transform });
        ZombieModeRuntimeModule.RestoreZombieModeVisualScale(marker);Assert(alive.transform.localScale.y==3 && marker.VisualScaleRecords.Count==0,"Visual restore must tolerate destroyed scene targets and clear all records");
        marker.VisualFootMarker=new GameObject("foot");marker.VisualFootMarkerFallbackApplied=true;ZombieModeRuntimeModule.ReleaseZombieModeFootMarker(marker);ZombieModeRuntimeModule.ReleaseZombieModeFootMarker(marker);
        Assert(marker.VisualFootMarker==null && !marker.VisualFootMarkerFallbackApplied && string.Join(",",Probe.Trace)=="release:foot","Foot marker must clear owner state and release to the pool exactly once");
    }
    public static void Main() { Starter();Cash();PurificationAndIsolation();VisualCleanup();Console.WriteLine("PASS ZombieModeStarterRuntime"); }
}

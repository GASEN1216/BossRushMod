using System;
using System.Collections;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Finish(Coroutine coroutine)
    {
        while (coroutine.Iterator.MoveNext()) { }
    }

    private static void WaitTwoFrames(Coroutine coroutine)
    {
        Check(coroutine.Iterator.MoveNext() && coroutine.Iterator.Current == null, "first frame");
        Check(coroutine.Iterator.MoveNext() && coroutine.Iterator.Current == null, "second frame");
    }

    private static void OwnersAndSceneCache()
    {
        var dailyOwner = new ModBehaviour();
        var nestOwner = new ModBehaviour();
        var dailyCalls = new List<int>();
        var nestCalls = new List<int>();
        bool repairDaily = false;
        var daily = new BuildingRestoreCore(dailyOwner, () => true,
            c => c.Kind == "daily", go => repairDaily, go => dailyCalls.Add(go.GetInstanceID()));
        var nest = new BuildingRestoreCore(nestOwner, () => true,
            c => c.Kind == "nest", go => false, go => nestCalls.Add(go.GetInstanceID()));
        var dailyGo = new GameObject(11, "daily");
        var nestGo = new GameObject(22, "nest");
        var destroyed = new Component(new GameObject(33, "destroyed"), "daily") { Destroyed = true };
        ObjectCache.Buildings = new UnityEngine.Object[] {
            new Component(dailyGo, "daily"), destroyed, new Component(nestGo, "nest") };
        SceneManager.ActiveHandle = 1;
        daily.Request("SceneInit");
        daily.Request("OnBuildingBuilt");
        nest.Request("SceneInit");
        Check(dailyOwner.Started == 1 && nestOwner.Started == 1, "owner-local request merge");
        WaitTwoFrames(dailyOwner.Coroutines[0]);
        WaitTwoFrames(nestOwner.Coroutines[0]);
        Check(dailyCalls.Count == 0 && nestCalls.Count == 0, "two frame delay");
        SceneManager.ActiveHandle = 2;
        Finish(dailyOwner.Coroutines[0]);
        Finish(nestOwner.Coroutines[0]);
        Check(dailyCalls.Count == 1 && dailyCalls[0] == 11, "daily target and destroyed fake null");
        Check(nestCalls.Count == 1 && nestCalls[0] == 22, "nest target and separate owner");
        daily.Request("same scene"); Finish(dailyOwner.Coroutines[1]);
        Check(dailyCalls.Count == 1, "instance cache in same scene");
        repairDaily = true;
        daily.Request("repair"); Finish(dailyOwner.Coroutines[2]);
        Check(dailyCalls.Count == 2, "repair check reassembles cached instance");
        repairDaily = false;
        SceneManager.ActiveHandle = 3;
        daily.Request("new scene"); Finish(dailyOwner.Coroutines[3]);
        Check(dailyCalls.Count == 3, "scene cache refresh after wait");
        daily.ResetPreparedCache();
        Check(ObjectCache.Invalidations == 1, "cache invalidation on owner reset");
        daily.Request("after reset"); Finish(dailyOwner.Coroutines[4]);
        Check(dailyCalls.Count == 4, "reset permits same instance restoration");
    }

    private static void CancelAndLateFinally()
    {
        var owner = new ModBehaviour();
        var calls = new List<int>();
        BuildingRestoreCore core = null;
        bool cancelInsideEnsure = true;
        core = new BuildingRestoreCore(owner, () => true, c => true, go => false, go => {
            calls.Add(go.GetInstanceID());
            if (cancelInsideEnsure)
            {
                cancelInsideEnsure = false;
                core.Cancel();
                core.Request("replacement");
            }
        });
        ObjectCache.Buildings = new UnityEngine.Object[] {
            new Component(new GameObject(44, "a"), "any"),
            new Component(new GameObject(45, "b"), "any") };
        SceneManager.ActiveHandle = 4;
        core.Request("first");
        WaitTwoFrames(owner.Coroutines[0]);
        Finish(owner.Coroutines[0]);
        Check(owner.Stopped == 1 && owner.Started == 2, "cancel and replacement during sweep");
        Check(calls.Count == 1, "cancelled sweep must stop before second building");
        core.Request("merged replacement");
        Check(owner.Started == 2, "old finally must not clear replacement handle");
        Finish(owner.Coroutines[1]);
        Check(calls.Count == 3, "replacement restores both objects");
        core.Cancel();
        core.Request("after completed cancellation");
        Check(owner.Started == 3, "new request after cancellation");
    }

    private static void FailureAndRetry()
    {
        var owner = new ModBehaviour();
        bool fail = true;
        int attempts = 0;
        var core = new BuildingRestoreCore(owner, () => true, c => true, go => false, go => {
            attempts++;
            if (fail) throw new InvalidOperationException("assembly failed");
        });
        ObjectCache.Buildings = new UnityEngine.Object[] { new Component(new GameObject(50, "fault"), "any") };
        SceneManager.ActiveHandle = 5;
        core.Request("failure");
        bool threw = false;
        try { Finish(owner.Coroutines[0]); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && attempts == 1, "single assembly failure surfaces");
        fail = false;
        core.Request("retry");
        Check(owner.Started == 2, "finally releases request after failure");
        Finish(owner.Coroutines[1]);
        Check(attempts == 2, "failed object retried");
        core.ClearPrepared();
        core.Request("destroy event"); Finish(owner.Coroutines[2]);
        Check(attempts == 3, "destroy callback clears prepared IDs");
    }

    private static void Main()
    {
        OwnersAndSceneCache();
        CancelAndLateFinally();
        FailureAndRetry();
        Console.WriteLine("BuildingRestoreCore: PASS");
    }
}

using System;
using System.IO;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static string modPath;
    private static TimeOfDayConfig template;
    private static void Check(bool value,string name){checks++;if(!value)throw new Exception("FAIL "+name);}
    private static SkyIslandRaidLease New()
    {
        SceneManager.Reset();SceneLoader.Loads=SceneLoader.Returns=0;SceneLoader.IsSceneLoading=false;Time.unscaledTime=0;
        SceneLoader.LoadingComment=null;Time.realtimeSinceStartup=0;
        CharacterMainControl.Main=new CharacterMainControl();AssetBundle.ScenePath=SkyIslandSceneReferenceBridge.ScenePath;
        SkyIslandSceneReferenceBridge.Reset();SkyIslandOfficialContract.ActivationAllowed=true;SkyIslandOfficialContract.Calls=0;
        SkyIslandOfficialContract.ConfiguredAtCall=false;
        // 官方层级：LevelConfig/TimeOfDayConfig/TimeOfDay_*，全是基地场景里的 MonoBehaviour。
        template=new GameObject("TimeOfDayConfig").AddComponent<TimeOfDayConfig>();
        var lease=new SkyIslandRaidLease();lease.Prepare(modPath,template);
        Check(SkyIslandOutdoorDaylight.Last!=null && SkyIslandOutdoorDaylight.Last!=template,"outdoor daylight volume is applied to the persistent clone, never the base original");
        // Prepare 之后基地场景必然卸载（官方 SceneLoader 独占关卡转换），模板随之销毁。
        // 租约若把模板原样带过图，注入到岛上的就是已销毁引用 —— 这一步让夹具与实机同构。
        UnityEngine.Object.Destroy(template.gameObject);
        return lease;
    }
    private static void Main()
    {
        modPath=Path.Combine(Environment.CurrentDirectory,"Build/runtime-regressions/SkyIslandRaidLease/fake-mod");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(modPath,SkyIslandRaidLease.BundleRelativePath)));
        File.WriteAllText(Path.Combine(modPath,SkyIslandRaidLease.BundleRelativePath),"fixture");
        ModBehaviour.ModPath=modPath;
        Check(SkyIslandRaidLease.IsBundleDeployed(),"deployed bundle is detected before any entry is offered");
        var lease=New();int callbacks=0;AssetBundle bundle=AssetBundle.Last;
        lease.Release(()=>callbacks++);
        Check(bundle.Unloaded && callbacks==1 && SceneManager.Subscribers==0,"prepare failure cleanup releases without load");

        lease=New();lease.BeginLoad();bundle=AssetBundle.Last;
        SkyIslandSceneReferenceBridge.PendingLoad=true;SceneLoader.Finish(true);lease.Release(null);
        Check(SkyIslandSceneReferenceBridge.Failure!=null,
            "official entry failure explicitly aborts the native operation before the recovery fence waits on it");
        Check(SkyIslandSceneReferenceBridge.VisualBegins==1 && SkyIslandSceneReferenceBridge.VisualEnds==1,
            "failed official task completes its visual lease before exposing LoadFinished");
        Check(!bundle.Unloaded,"cancelled loader retains bundle while native scene operation is still pending");
        lease.PumpRelease();Check(!bundle.Unloaded,"recovery cannot bypass the native operation ownership fence");
        SkyIslandSceneReferenceBridge.PendingLoad=false;lease.PumpRelease();
        Check(bundle.Unloaded,"finished native operation permits normal resource cleanup");

        lease=New();lease.BeginLoad();SceneLoader.Finish(true);bundle=AssetBundle.Last;
        var session=new SkyIslandSession(lease);
        session.TickReturn();
        Check(SceneLoader.Returns==1,"failure before island sceneLoaded still dispatches a base recovery");
        Check(session.Closed && !bundle.Unloaded,
            "no-island recovery closes the session while its existing lease holds resources through the return");
        SceneLoader.Finish();Time.unscaledTime=3;session.TickReturn();
        Check(SceneLoader.Returns==1 && bundle.Unloaded && session.ReleaseCallbacks==1,
            "successful no-island recovery cannot repeatedly reload the base without an island unload event");

        lease=New();lease.BeginLoad();SceneLoader.Finish(true);bundle=AssetBundle.Last;
        session=new SkyIslandSession(lease);session.TickReturn();
        SkyIslandSceneReferenceBridge.PendingLoad=true;SceneLoader.Finish(true);Time.unscaledTime=3;
        lease.PumpRelease();
        Check(SceneLoader.Returns==1 && !bundle.Unloaded,"failed base return waits for its native operation");
        SkyIslandSceneReferenceBridge.PendingLoad=false;lease.PumpRelease();
        Check(SceneLoader.Returns==2 && !bundle.Unloaded,
            "failed base return retries after native completion even when the island was already unloaded");
        Check(SkyIslandSceneReferenceBridge.PreserveTargetSnapshot,
            "retry must preserve the target character snapshot until a complete return succeeds");
        SceneLoader.Finish();
        Check(bundle.Unloaded && session.ReleaseCallbacks==1,"base retry success closes the retained recovery once");

        lease=New();lease.BeginLoad();bundle=AssetBundle.Last;callbacks=0;
        lease.Release(()=>callbacks++);
        Check(!bundle.Unloaded && callbacks==0,"host exit while loading retains bundle");
        SceneManager.LoadRaid();
        GameObject[] roots=SceneManager.Raid.GetRootGameObjects();
        TimeOfDayConfig injected=roots[1].GetComponent<LevelConfig>().timeOfDayConfig;
        Check(roots[1].activeSelf && injected!=null,"lease assembles official services after session owner disappears");
        Check(SkyIslandOfficialContract.ConfiguredAtCall,"official contract judges the scene after the weather and start buffs are injected");
        // CR-2026-09-10-003：注入的必须是租约自己的常驻副本，基地那份已随场景卸载销毁。
        Check(!ReferenceEquals(injected,template),"injected weather is the lease's persistent copy, not the base scene instance");
        Check(roots[0].activeSelf,"cancelled load keeps landing geometry active");
        Check(SkyIslandExplosionObstaclePatch.Armed,"raid scene arms the explosion obstacle patch");
        lease.PumpRelease();Check(SceneLoader.Returns==0,"return waits initial official load");
        SceneLoader.Finish();Check(lease.LoadFinished && !bundle.Unloaded,"finished load still retains active scene assets");
        lease.PumpRelease();Check(SceneLoader.Returns==1 && !SceneLoader.LastEvacuated,"orphaned session initiates ordinary base recovery");
        SceneManager.UnloadRaid();Check(!bundle.Unloaded,"unload event alone does not release during return task");
        SceneLoader.Finish();Check(bundle.Unloaded && callbacks==1,"return completion releases bundle after scene unload");
        Check(injected==null,"release destroys the persistent weather copy instead of leaking a DontDestroyOnLoad object");
        Check(SceneManager.Subscribers==0,"successful recovery releases both scene handlers");
        Check(!SkyIslandExplosionObstaclePatch.Armed && SkyIslandExplosionObstaclePatch.Disarms>0,
            "leaving the raid disarms the explosion obstacle patch");

        lease=New();lease.BeginLoad();SceneManager.LoadRaid();SceneLoader.Finish();bundle=AssetBundle.Last;
        CharacterMainControl.Main.Health.IsDead=true;lease.Release(null);lease.PumpRelease();
        Check(SceneLoader.Returns==0 && !bundle.Unloaded,"death waits official character loss and tomb pipeline");
        SceneManager.UnloadRaid();Check(bundle.Unloaded,"official death unload completes release");

        lease=New();lease.BeginLoad();SceneManager.LoadRaid();SceneLoader.Finish();bundle=AssetBundle.Last;
        Duckov.UI.ClosureView.Shown=0;LevelManager.Evacuations=0;SceneLoader.LastCurtain=null;
        lease.ReturnToBase(true);Check(SceneLoader.LastEvacuated,"voluntary return preserves official evacuation event");
        // 官方 SceneLoaderProxy 的撤离顺序：NotifyEvacuated -> ClosureView(1s) -> 幕布换撤离画面。
        Check(LevelManager.Evacuations==1,"evacuation notifies the official level manager first");
        Check(Duckov.UI.ClosureView.Shown==1,"evacuation shows the official closure screen");
        Check(SceneLoader.LastCurtain!=null && SceneLoader.LastCurtain.Name=="EvacuateScreen",
            "evacuation swaps the loading curtain for the official evacuate screen");
        SceneLoader.Finish(true);
        // CR-2026-09-30-007：官方加载异常后不放下 IsSceneLoading；租约必须放下自己那次加载置起的标志，否则重试永远被挡。
        Check(!SceneLoader.IsSceneLoading,"faulted official return releases the loading flag it left behind");
        lease.ReturnToBase(true);Check(SceneLoader.Returns==1,"failed return retry is throttled");
        Time.unscaledTime=3;lease.ReturnToBase(true);Check(SceneLoader.Returns==2,"failed return can retry");
        lease.Release(null);SceneManager.UnloadRaid();SceneLoader.Finish();Check(bundle.Unloaded,"successful retry releases resources");

        lease=New();lease.BeginLoad();SceneManager.LoadRaid(false);
        Check(lease.Error!=null,"official service assembly failure reaches Build error channel");
        SceneLoader.Finish(true);lease.Release(null);SceneManager.UnloadRaid();Check(AssetBundle.Last.Unloaded,"failed assembly cleans up after actual unload");

        // CR-2026-09-08-005：缺 provider 等最小装配问题必须在激活官方服务之前定论，并给官方等待一个取消信号。
        lease=New();Check(ReferenceEquals(SkyIslandSceneReferenceBridge.Owner,lease),"prepare claims the initialization token");
        SkyIslandOfficialContract.ActivationAllowed=false;
        lease.BeginLoad();SceneManager.LoadRaid();
        roots=SceneManager.Raid.GetRootGameObjects();
        Check(SkyIslandOfficialContract.Calls==1 && SkyIslandSceneReferenceBridge.BoundScenes==1,"activation contract runs against the bound scene");
        Check(!roots[1].activeSelf,"failed activation contract must not start official services");
        Check(lease.Error!=null && SkyIslandSceneReferenceBridge.Failure!=null,"failed activation raises an observable cancellation signal");
        SceneLoader.Finish(true);bundle=AssetBundle.Last;callbacks=0;
        Check(!SceneLoader.IsSceneLoading,"faulted official entry load releases the loading flag before recovery");
        lease.Release(()=>callbacks++);lease.PumpRelease();
        Check(SceneLoader.Returns==1,"initialization failure still reaches a bounded base return");
        SceneManager.UnloadRaid();SceneLoader.Finish();
        Check(bundle.Unloaded && callbacks==1,"initialization failure releases the bundle after real unload");
        Check(SkyIslandSceneReferenceBridge.Owner==null && SkyIslandSceneReferenceBridge.Ends>0,"release returns the initialization token");

        // 令牌漏还会让下一次进岛在 Prepare 直接抛；释放之后必须能干净重入。
        SkyIslandOfficialContract.ActivationAllowed=true;
        lease=New();Check(ReferenceEquals(SkyIslandSceneReferenceBridge.Owner,lease),"a released token allows the next journey to claim it");
        lease.BeginLoad();SceneManager.LoadRaid();SceneLoader.Finish();
        lease.MarkInitialized();
        Check(SkyIslandSceneReferenceBridge.Owner==null && SkyIslandSceneReferenceBridge.Failure==null,"official contract success clears the cancellation signal");
        lease.Release(null);SceneManager.UnloadRaid();
        Check(AssetBundle.Last.Unloaded,"normal journey releases without a second official task");

        // 加载途中取消：官方等待必须收到取消信号，否则外层永远等一个不会到来的 LevelInited。
        lease=New();lease.BeginLoad();
        lease.Release(null);
        Check(SkyIslandSceneReferenceBridge.Failure!=null,"cancelling during load signals the official loader wait");
        SceneManager.LoadRaid();SceneLoader.Finish();lease.PumpRelease();SceneManager.UnloadRaid();SceneLoader.Finish();
        Check(AssetBundle.Last.Unloaded && SkyIslandSceneReferenceBridge.Owner==null,"cancelled load still releases token and bundle");
        // 官方先停在点击继续，再激活目标场景；玩家读提示或离开电脑不能烧掉 120 秒加载预算。
        lease=New();lease.BeginLoad();float deadline=120f;
        SceneLoader.LoadingComment="Wait for click...";Time.realtimeSinceStartup=130f;
        Check(lease.HasSceneLoadTimeRemaining(ref deadline),"waiting for the player's click survives the original load deadline");
        Time.realtimeSinceStartup=7200f;
        Check(lease.HasSceneLoadTimeRemaining(ref deadline),"the click screen allows a long player-controlled wait");
        SceneLoader.LoadingComment="Allowing activation...";Time.realtimeSinceStartup=7201f;
        Check(lease.HasSceneLoadTimeRemaining(ref deadline),"clicking still leaves time to activate the scene");
        Time.realtimeSinceStartup=7321f;
        Check(!lease.HasSceneLoadTimeRemaining(ref deadline),"a stalled activation still times out after the click");
        SceneLoader.LoadingComment="Wait for click...";SceneLoader.IsSceneLoading=false;
        Check(!lease.HasSceneLoadTimeRemaining(ref deadline),"a stale comment outside loading cannot extend the deadline");
        SceneLoader.IsSceneLoading=true;lease.Abort("cancelled");
        Check(!lease.HasSceneLoadTimeRemaining(ref deadline),"failed initialization is never kept alive by the click screen");
        lease.Release(null);SceneLoader.Finish(true);
        Check(AssetBundle.Last.Unloaded,"cancelled click wait still releases its lease");
        lease=New();lease.BeginLoad();deadline=120f;
        SceneLoader.LoadingComment="Waiting for scene loading operation...";Time.realtimeSinceStartup=121f;
        Check(!lease.HasSceneLoadTimeRemaining(ref deadline),"actual asset loading retains the original timeout");
        lease.Release(null);SceneLoader.Finish(true);
        Console.WriteLine("PASS SkyIslandRaidLease: "+checks+" assertions (production lease with official loader and Unity substitutes)");
    }
}

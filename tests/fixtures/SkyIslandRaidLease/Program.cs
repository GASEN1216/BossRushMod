using System;
using System.IO;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static string modPath;
    private static void Check(bool value,string name){checks++;if(!value)throw new Exception("FAIL "+name);}
    private static SkyIslandRaidLease New()
    {
        SceneManager.Reset();SceneLoader.Loads=SceneLoader.Returns=0;SceneLoader.IsSceneLoading=false;Time.unscaledTime=0;
        CharacterMainControl.Main=new CharacterMainControl();AssetBundle.ScenePath=SkyIslandSceneReferenceBridge.ScenePath;
        SkyIslandSceneReferenceBridge.Reset();SkyIslandOfficialContract.ActivationAllowed=true;SkyIslandOfficialContract.Calls=0;
        var lease=new SkyIslandRaidLease();lease.Prepare(modPath,new TimeOfDayConfig());return lease;
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

        lease=New();lease.BeginLoad();bundle=AssetBundle.Last;callbacks=0;
        lease.Release(()=>callbacks++);
        Check(!bundle.Unloaded && callbacks==0,"host exit while loading retains bundle");
        SceneManager.LoadRaid();
        GameObject[] roots=SceneManager.Raid.GetRootGameObjects();
        Check(roots[1].activeSelf && roots[1].GetComponent<LevelConfig>().timeOfDayConfig!=null,"lease assembles official services after session owner disappears");
        Check(roots[0].activeSelf,"cancelled load keeps landing geometry active");
        Check(SkyIslandExplosionObstaclePatch.Armed,"raid scene arms the explosion obstacle patch");
        lease.PumpRelease();Check(SceneLoader.Returns==0,"return waits initial official load");
        SceneLoader.Finish();Check(lease.LoadFinished && !bundle.Unloaded,"finished load still retains active scene assets");
        lease.PumpRelease();Check(SceneLoader.Returns==1 && !SceneLoader.LastEvacuated,"orphaned session initiates ordinary base recovery");
        SceneManager.UnloadRaid();Check(!bundle.Unloaded,"unload event alone does not release during return task");
        SceneLoader.Finish();Check(bundle.Unloaded && callbacks==1,"return completion releases bundle after scene unload");
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
        SceneLoader.Finish(true);lease.ReturnToBase(true);Check(SceneLoader.Returns==1,"failed return retry is throttled");
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
        Console.WriteLine("PASS SkyIslandRaidLease: "+checks+" assertions (production lease with official loader and Unity substitutes)");
    }
}

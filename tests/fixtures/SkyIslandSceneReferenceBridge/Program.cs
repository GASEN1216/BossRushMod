using System;
using BossRush;
using Duckov.Scenes;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message + " | " + string.Join("; ", ModBehaviour.Errors));
    }
    private static Scene Scene(string path, int handle)
    { return new Scene { path = path, handle = handle, buildIndex = -1, isLoaded = true }; }
    /// <summary>驱动一次官方加载等待，返回它抛出的异常（正常完成返回 null）。</summary>
    private static Exception RunLoad(SceneReference reference)
    {
        try { SceneLoader.Instance.LoadScene(reference).GetAwaiter().GetResult(); return null; }
        catch (Exception e) { return e; }
    }
    private static int Main()
    {
        // Harmony / 类型加载失败时 Exception.ToString() 本身可能抛，夹具必须还能报出类型和消息。
        try { Run(); return 0; }
        catch (Exception e)
        {
            Console.WriteLine("SkyIslandSceneReferenceBridge: FAIL " + e.GetType().FullName + ": " + e.Message);
            for (Exception inner = e.InnerException; inner != null; inner = inner.InnerException)
                Console.WriteLine("  inner " + inner.GetType().FullName + ": " + inner.Message);
            return 1;
        }
    }
    private static void Run()
    {
        var other = Scene("Assets/Other/SkyIslandRaid.unity", 1);
        var sky = Scene(SkyIslandSceneReferenceBridge.ScenePath, 2);
        SceneManager.Scenes.Add(other);
        SceneManager.Scenes.Add(sky);
        SceneManager.Active = other;
        var foreignProvider = new SceneLocationsProvider("Foreign");
        foreignProvider.Locations.Add("StartPoints/PlayerSpawn", new GameObject("ForeignSpawn").transform);
        SceneManager.Active = sky;
        var provider = new SceneLocationsProvider("Sky");
        var spawn = new GameObject("PlayerSpawn").transform;
        provider.Locations.Add("StartPoints/PlayerSpawn", spawn);
        var core = new MultiSceneCore("SkyCore");
        MultiSceneCore.Instance = core;
        core.SubScenes.Add(new SubSceneEntry { sceneID = SkyIslandSceneReferenceBridge.SceneId });

        Check(SkyIslandSceneReferenceBridge.EnsureRegistered(), "bridge installs actual Harmony patches");
        var reference = SkyIslandSceneReferenceBridge.SceneReference;
        Check(reference.BuildIndex == -1, "bundle build index remains -1");
        Check(reference.Path == sky.path && reference.Name == "SkyIslandRaid", "actual path and scene name resolve");
        Check(reference.LoadedScene.handle == sky.handle, "LoadedScene resolves actual path");
        Check(reference.UnsafeReason == SceneReferenceUnsafeReason.None, "only owned reference is accepted by official loader");
        Check(SceneInfoCollection.Entries.Count == 0, "global entries remain free of ambiguous -1 metadata");
        // 官方小地图用 GetSceneID(GetActiveScene().buildIndex) 反查地图身份；bundle 场景恒为 -1，
        // 所以站在天空岛上必须解析得出来。但这条收窄只能按活动场景生效：
        // 换到别的场景后 -1 必须重新变回未知，否则就是被禁的全局 -1 别名。
        Check(SceneInfoCollection.GetSceneID(-1) == SkyIslandSceneReferenceBridge.SceneId,
            "-1 resolves to the island only while it is the active scene");
        SceneManager.Active = other;
        Check(SceneInfoCollection.GetSceneID(-1) == null, "unqualified -1 identity stays unchanged elsewhere");
        Check(SceneInfoCollection.GetSceneID(1) == null, "positive build indices are never hijacked");
        SceneManager.Active = sky;
        Check(SceneInfoCollection.GetSceneID(1) == null, "positive build indices stay untouched on the island too");
        Check(SceneInfoCollection.GetSceneID(reference) == SkyIslandSceneReferenceBridge.SceneId, "reference query resolves own stable id");
        Check(SceneInfoCollection.GetSceneInfo(SkyIslandSceneReferenceBridge.SceneId).SceneReference == reference, "string metadata resolves own reference");
        Check(core.SceneInfo != null && core.DisplayName != "original", "own core metadata getters are bridged");
        Check(MultiSceneCore.MainSceneID == SkyIslandSceneReferenceBridge.SceneId, "own main scene id available");
        Check(MultiSceneCore.ActiveSubSceneID == null, "no fake active subscene before activation");
        Check(SceneLocationsProvider.GetProviderOfScene(reference) == provider, "reference provider lookup ignores other -1 scene");
        Check(SceneLocationsProvider.GetProviderOfScene(sky) == provider, "scene provider lookup compares real handle");
        Check(SceneLocationsProvider.GetLocation(reference, "StartPoints/PlayerSpawn") == spawn, "real provider location used by official teleport");
        Check(SceneLocationsProvider.GetLocation(SkyIslandSceneReferenceBridge.SceneId, "StartPoints/PlayerSpawn") == spawn, "id provider lookup is precise");

        int events = 0;
        MultiSceneCore.OnSubSceneLoaded += (owner, scene) => { Check(owner == core && scene.handle == sky.handle && !core.IsLoading, "official callback owns actual active scene"); events++; };
        var frame = new System.Threading.Tasks.TaskCompletionSource<bool>();
        Cysharp.Threading.Tasks.UniTask.NextFrameTask = frame.Task;
        var pending = core.Load(reference);
        Check(core.IsLoading && MultiSceneCore.ActiveSubSceneID == null, "pending activation preserves official loading identity semantics");
        SceneManager.Active = other;
        frame.SetResult(true);
        Check(!pending.GetAwaiter().GetResult() && !core.IsLoading && events == 0, "scene change cancels pending activation before callback");
        SceneManager.Active = sky;
        Check(MultiSceneCore.ActiveSubSceneID == null, "cancelled activation restores uninitialized subscene state");
        Cysharp.Threading.Tasks.UniTask.NextFrameTask = System.Threading.Tasks.Task.CompletedTask;
        Check(core.Load(reference).GetAwaiter().GetResult(), "already loaded own scene activates without loading it twice");
        Check(core.OriginalLoadCalls == 0 && events == 1, "int load bypassed and official subscene event delivered once");
        Check(MultiSceneCore.ActiveSubSceneID == SkyIslandSceneReferenceBridge.SceneId, "death service receives stable active scene identity");
        var tombParent = core.GetSetActiveWithSceneParent(-1);
        Check(tombParent.gameObject.activeSelf && tombParent.gameObject.scene.handle == sky.handle, "tomb owner container stays visible in own scene");
        Check(core.GetSetActiveWithSceneParent(-1) == tombParent, "owner parent creation is idempotent");
        Check(core.Load(reference).GetAwaiter().GetResult() && events == 1, "repeat self activation avoids duplicate tomb events");

        SceneManager.Active = other;
        var foreignCore = new MultiSceneCore("OtherCore");
        MultiSceneCore.Instance = foreignCore;
        Check(!SkyIslandSceneReferenceBridge.IsScene(other), "same basename in foreign path is isolated");
        Check(foreignCore.SceneInfo == null && MultiSceneCore.MainSceneID == "original_main", "foreign core metadata unmodified");
        Check(!foreignCore.Load(reference).GetAwaiter().GetResult() && foreignCore.OriginalLoadCalls == 1, "foreign core still executes original loader");
        Check(!core.GetSetActiveWithSceneParent(-1).gameObject.activeSelf, "inactive own core cannot reinterpret another active scene -1");
        SceneGuidToPathMapProvider.Forward.Add("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", other.path);
        SceneGuidToPathMapProvider.Reverse.Add(other.path, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var foreignReference = new SceneReference("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Check(foreignReference.UnsafeReason == SceneReferenceUnsafeReason.NotInBuild, "foreign bundle reference remains unsafe");

        // ---- CR-2026-09-08-005：官方初始化失败必须有可观察的取消信号和有界退出 ----
        LevelManager.SetLevelInited(false);
        var foreignManager = new LevelManager("OtherLevelManager");
        foreignManager.MainCharacter = new CharacterMainControl();
        SceneManager.Active = sky;
        var manager = new LevelManager("SkyLevelManager");
        manager.MainCharacter = new CharacterMainControl();
        int loads = SceneLoader.LoadCalls;
        object token = new object();
        SkyIslandSceneReferenceBridge.BeginInitialization(token);
        SkyIslandSceneReferenceBridge.BindInitializationScene(token, sky);
        Check(RunLoad(reference) == null && SceneLoader.LoadCalls == loads + 1,
            "a healthy initialization leaves the official wait untouched");
        SkyIslandSceneReferenceBridge.AbortInitialization(token, "天空岛缺少真实 SceneLocationsProvider");
        Exception cancelled = RunLoad(reference);
        Check(cancelled is OperationCanceledException && cancelled.Message.Contains("SceneLocationsProvider"),
            "abort turns the official LevelInited wait into an observable cancellation");
        Check(RunLoad(foreignReference) == null, "cancellation never leaks into other maps' loads");
        int saves = LevelManager.SaveBeforeLoadCalls;
        manager.NotifySaveBeforeLoadScene(true);
        Check(LevelManager.SaveBeforeLoadCalls == saves,
            "a half-initialized character must not overwrite the official pre-departure snapshot");
        foreignManager.NotifySaveBeforeLoadScene(true);
        Check(LevelManager.SaveBeforeLoadCalls == saves + 1, "other maps' pre-departure save is never suppressed");
        manager.MainCharacter.Health.IsDead = true;
        manager.NotifySaveBeforeLoadScene(true);
        Check(LevelManager.SaveBeforeLoadCalls == saves + 2, "official death still owns its own save");
        SkyIslandSceneReferenceBridge.EndInitialization(token);
        Check(RunLoad(reference) == null, "ending initialization restores the native wait");
        LevelManager.SetLevelInited(true);
        SceneManager.Active = other;

        SkyIslandSceneReferenceBridge.Shutdown();
        Check(reference.Path == sky.path && SceneInfoCollection.GetSceneInfo(SkyIslandSceneReferenceBridge.SceneId) != null,
            "shutdown keeps references alive until the owned scene is unloaded");
        SceneLoader.IsSceneLoading = true;
        SceneManager.Unload(sky);
        Check(reference.Path == sky.path, "unload during official loading defers reference removal until completion");
        SceneLoader.Finish();
        Check(SceneInfoCollection.GetSceneInfo(SkyIslandSceneReferenceBridge.SceneId) == null, "metadata patches removed at shutdown");
        Check(SceneGuidToPathMapProvider.Forward.Count == 1, "only owned map entry removed");
        Check(foreignReference.Path == other.path, "foreign map entry retained");
        Check(SkyIslandSceneReferenceBridge.EnsureRegistered(), "bridge can be initialized again after shutdown");
        SkyIslandSceneReferenceBridge.Shutdown();
        Console.WriteLine("SkyIslandSceneReferenceBridge: PASS (" + assertions + " assertions, real Harmony; Unity substituted)");
    }
}

using System;
using System.IO;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static string Prepare()
    {
        SceneManager.Reset();
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Build", "stone-outpost-fixture");
        Directory.CreateDirectory(Path.Combine(path, "Assets", "arenas"));
        File.WriteAllText(Path.Combine(path, StoneOutpostSceneLease.BundleRelativePath), "fixture");
        return path;
    }
    private static void Main()
    {
        string directory = Prepare();
        var lease = new StoneOutpostSceneLease();
        AsyncOperation operation = lease.BeginLoad(directory);
        operation.isDone = true; // Unity 可先恢复 yield，再投递 completed。
        Check(!lease.LoadResolved && lease.Root == null, "isDone 不得冒充租约就绪");
        GameObject root = new GameObject("StoneOutpost");
        SceneManager.Add("assets/stoneoutpost/stoneoutpost.unity", root);
        operation.Complete();
        Check(lease.LoadResolved && lease.Loaded, "完成回调应发布已解析状态");
        Check(lease.Root == root && lease.LoadError == null, "大小写规范化后应找到真实根节点");
        int callbacks = 0;
        lease.Release(() => callbacks++);
        Check(!root.activeSelf, "等待卸载时先隐藏资源根");
        Check(callbacks == 0 && !AssetBundle.Last.Unloaded, "卸载完成前不得释放 bundle 或通知结束");
        SceneManager.PendingUnload.Complete();
        Check(callbacks == 1 && AssetBundle.Last.Unloaded, "卸载完成后只通知一次并释放 bundle");

        directory = Prepare();
        lease = new StoneOutpostSceneLease();
        operation = lease.BeginLoad(directory);
        callbacks = 0;
        lease.Release(() => callbacks++);
        Check(SceneManager.PendingUnload == null && callbacks == 0, "未完成加载时取消应等待迟到结果");
        Check(!AssetBundle.Last.Unloaded, "不得提前释放仍在加载的场景包");
        SceneManager.Add(StoneOutpostSceneLease.ScenePath, new GameObject("StoneOutpost"));
        operation.Complete();
        Check(SceneManager.PendingUnload != null, "迟到的场景仍必须卸载");
        SceneManager.PendingUnload.Complete();
        Check(callbacks == 1 && AssetBundle.Last.Unloaded, "取消后的所有权必须最终释放");

        directory = Prepare();
        lease = new StoneOutpostSceneLease();
        operation = lease.BeginLoad(directory);
        SceneManager.Add("Assets/Foreign/StoneOutpost.unity", new GameObject("StoneOutpost"));
        SceneManager.Add(StoneOutpostSceneLease.ScenePath, new GameObject("WrongRoot"));
        operation.Complete();
        Check(lease.LoadResolved && lease.LoadError != null && lease.Root == null, "根节点缺失须明确失败，不能误取同名外部场景");
        lease.Release(null);
        Check(SceneManager.PendingUnload != null, "解析失败仍要卸载本次已加载场景");
        SceneManager.PendingUnload.Complete();
        Check(SceneManager.GetSceneAt(0).isLoaded, "不得卸载外部场景");
        Check(AssetBundle.Last.Unloaded, "解析失败也必须释放 bundle");
        Console.WriteLine("StoneOutpostSceneLease: PASS (" + assertions + " assertions)");
    }
}

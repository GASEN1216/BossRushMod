using System;
using System.IO;
using BossRush;
using UnityEngine;

static class BuildingModels
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    internal static void Run()
    {
        ResourceBundleLoader.ResetStaticCaches();
        AssetBundle lease;
        int loads = AssetBundle.Loads;
        Check(!BuildingModelHelper.TryInstantiateBundle("missing", "Prefab", new Transform(), out lease), "missing building falls back");
        Check(lease == null && AssetBundle.Loads == loads, "missing file performs no native load");
        string path = Path.Combine(ModBehaviour.Root, "Assets", "buildings", "fixture");
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture");
        var bundle = new AssetBundle(); AssetBundle.Sync = bundle;
        Check(!BuildingModelHelper.TryInstantiateBundle("fixture", "Prefab", new Transform(), out lease), "missing named prefab falls back");
        Check(lease == null && bundle.Unloads == 1 && bundle.UnloadedObjects, "failed acquisition frees bundle assets");
        bundle = new AssetBundle(); bundle.Prefabs["Prefab"] = new GameObject(); AssetBundle.Sync = bundle;
        var parent = new Transform();
        Check(BuildingModelHelper.TryInstantiateBundle("fixture", "Prefab", parent, out lease), "valid model installs");
        Check(ReferenceEquals(lease, bundle) && bundle.Unloads == 0, "lease transferred exactly once");
        Check(GameObject.LastInstance.Parent == parent && GameObject.LastInstance.Active && GameObject.LastInstance.name == "Model", "model under existing graphics container");
        bundle.Unload(false);
        bundle = new AssetBundle(); bundle.Prefabs["Prefab"] = new GameObject(); AssetBundle.Sync = bundle;
        GameObject.FailActivation = true;
        try
        {
            Check(!BuildingModelHelper.TryInstantiateBundle("fixture", "Prefab", parent, out lease), "failed instance activation falls back");
            Check(GameObject.LastInstance.Destroyed && lease == null && bundle.Unloads == 1 && bundle.UnloadedObjects, "failed instance and bundle cleaned");
        }
        finally { GameObject.FailActivation = false; }
    }
}

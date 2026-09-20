using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static void Destroy(Object value) { value.Destroyed = true; }
        public static GameObject Instantiate(GameObject prefab, Transform parent, bool worldPositionStays)
        { return GameObject.Instantiate(prefab, parent, worldPositionStays); }
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { return (ReferenceEquals(a,null)||a.Destroyed) ? (ReferenceEquals(b,null)||b.Destroyed) : ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) { return !(a==b); }
        public override bool Equals(object b) { return ReferenceEquals(this,b); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public class Transform : Object { }
    public class GameObject : Object
    {
        public static GameObject LastInstance;
        public static bool FailActivation;
        public Transform Parent;
        public bool Active;
        public static GameObject Instantiate(GameObject prefab, Transform parent, bool worldPositionStays)
        { return LastInstance = new GameObject { Parent = parent }; }
        public void SetActive(bool value) { if(FailActivation) throw new Exception("activation"); Active = value; }
    }
    public class Texture2D : Object { }
    public class Sprite : Object { public Texture2D texture; }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public static class Debug { public static void LogWarning(string s) { } }
    public class AsyncOperation
    {
        public bool isDone;
        public event Action<AsyncOperation> completed;
        public void Finish() { isDone=true; completed?.Invoke(this); }
    }
    public class AssetBundleCreateRequest : AsyncOperation
    {
        public AssetBundle Result;
        public int ForcedReads;
        public AssetBundle assetBundle { get { if (!isDone) { ForcedReads++; Finish(); } return Result; } }
    }
    public class AssetBundleRequest : AsyncOperation { public Object[] allAssets = new Object[0]; }
    public class AssetBundle : Object
    {
        public static AssetBundleCreateRequest Next;
        public static AssetBundle Sync;
        public static int Loads;
        public int Unloads;
        public bool UnloadedObjects;
        public AssetBundleRequest Assets = new AssetBundleRequest();
        public Dictionary<string,Sprite> Sprites = new Dictionary<string,Sprite>();
        public static AssetBundle LoadFromFile(string path) { Loads++; return Sync; }
        public static AssetBundleCreateRequest LoadFromFileAsync(string path) { Loads++; return Next; }
        public AssetBundleRequest LoadAllAssetsAsync<T>() where T:Object { return Assets; }
        public T[] LoadAllAssets<T>() where T:Object { return new T[0]; }
        public readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        public T LoadAsset<T>(string key) where T:Object { if(typeof(T)==typeof(GameObject)) { GameObject p; return Prefabs.TryGetValue(key,out p) ? p as T : null; } Sprite s; return Sprites.TryGetValue(key,out s) ? s as T : null; }
        public void Unload(bool objects) { Unloads++; UnloadedObjects=objects; Destroyed=true; if(objects) foreach(var s in Sprites.Values) { s.Destroyed=true; s.texture.Destroyed=true; } }
    }
}
namespace BossRush
{
    public class ModBehaviour : UnityEngine.Object
    {
        internal static string Root;
        public static string GetModPath() { return Root; }
        public static void DevLog(string s) { }
        internal void EnsureItemContentConfiguratorsRegisteredForDynamicRegistry() { }
    }
    internal static class BossRushDynamicItemRegistry { internal static void EnsureRegistered(int id) { } }
    internal static class BossRushItemIds { internal const int BossRushTicket = 500001; }
    public static partial class EquipmentFactory
    {
        private static readonly HashSet<string> loadedBundles = new HashSet<string>();
        private const string EQUIPMENT_PATH = "Assets/Equipment";
        private static int LoadBundle(string name) { return 0; }
    }
    public static partial class ItemFactory
    {
        private static readonly HashSet<string> loadedBundles = new HashSet<string>();
        private static readonly Dictionary<int,object> loadedItems = new Dictionary<int,object>();
        private const string ITEMS_PATH = "Assets/items";
        private static int LoadBundle(string name) { return 0; }
    }
}
public static class SceneLoader { public static bool IsSceneLoading; }
public static class LevelManager { public static bool LevelInitializing; }
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager
    {
        public static int ActiveHandle;
        public static Scene GetActiveScene() { return new Scene { handle = ActiveHandle }; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    { public AsyncMethodBuilderAttribute(Type type) { } }
}

namespace Cysharp.Threading.Tasks
{
    public static class UniTask
    {
        public static Task NextFrameTask = Task.CompletedTask;
        public static Task NextFrame() { return NextFrameTask; }
    }
    [AsyncMethodBuilder(typeof(FixtureTaskBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> Inner;
        public TaskAwaiter<T> GetAwaiter() { return Inner.GetAwaiter(); }
        public static UniTask<T> FromResult(T value) { return new UniTask<T> { Inner = Task.FromResult(value) }; }
    }
    public struct FixtureTaskBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;
        public static FixtureTaskBuilder<T> Create() { return new FixtureTaskBuilder<T> { builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T> { Inner = builder.Task }; } }
        public void SetResult(T value) { builder.SetResult(value); }
        public void SetException(Exception e) { builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine value) { builder.SetStateMachine(value); }
        public void Start<TStateMachine>(ref TStateMachine state) where TStateMachine : IAsyncStateMachine { builder.Start(ref state); }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine state)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine state)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
}

namespace UnityEngine
{
    public static class Debug { public static void Log(string value) { } public static void LogWarning(string value) { } }
    public class GameObject
    {
        public readonly string name;
        public UnityEngine.SceneManagement.Scene scene;
        public readonly Transform transform;
        public bool activeSelf = true;
        public GameObject(string value) { name = value; scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); transform = new Transform(this); }
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Transform
    {
        public readonly GameObject gameObject;
        public Transform parent;
        public Transform(GameObject owner) { gameObject = owner; }
        public void SetParent(Transform value, bool worldPositionStays) { parent = value; gameObject.scene = value.gameObject.scene; }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string path;
        public int handle;
        public int buildIndex;
        public bool isLoaded;
        public bool IsValid() { return handle != 0; }
    }
    public static class SceneManager
    {
        public static readonly List<Scene> Scenes = new List<Scene>();
        public static Scene Active;
        public static event Action<Scene> sceneUnloaded;
        public static Scene GetActiveScene() { return Active; }
        public static Scene GetSceneByPath(string path)
        { return Scenes.Find(scene => string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase)); }
        public static void Unload(Scene scene) { Scenes.RemoveAll(value => value.handle == scene.handle); sceneUnloaded?.Invoke(scene); }
    }
}

namespace Eflatun.SceneReference
{
    public enum SceneReferenceUnsafeReason { None, Empty, NotInMaps, NotInBuild }
    public static class SceneGuidToPathMapProvider
    {
        public static readonly Dictionary<string, string> Forward = new Dictionary<string, string>();
        public static readonly Dictionary<string, string> Reverse = new Dictionary<string, string>();
        public static IReadOnlyDictionary<string, string> SceneGuidToPathMap { get { return Forward; } }
        public static IReadOnlyDictionary<string, string> ScenePathToGuidMap { get { return Reverse; } }
    }
    public class SceneReference
    {
        public string Guid { get; private set; }
        public SceneReference(string guid)
        {
            if (!SceneGuidToPathMapProvider.Forward.ContainsKey(guid)) throw new InvalidOperationException("missing guid");
            Guid = guid;
        }
        public string Path { get { return SceneGuidToPathMapProvider.Forward[Guid]; } }
        public string Name { get { return System.IO.Path.GetFileNameWithoutExtension(Path); } }
        public int BuildIndex { get { return -1; } }
        public UnityEngine.SceneManagement.Scene LoadedScene { get { return UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Path); } }
        public SceneReferenceUnsafeReason UnsafeReason
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get { return SceneReferenceUnsafeReason.NotInBuild; }
        }
    }
}

public class SceneInfoEntry
{
    private readonly string id;
    private readonly Eflatun.SceneReference.SceneReference reference;
    private string displayName = "";
    public SceneInfoEntry(string id, Eflatun.SceneReference.SceneReference reference) { this.id = id; this.reference = reference; }
    public string ID { get { return id; } }
    public Eflatun.SceneReference.SceneReference SceneReference { get { return reference; } }
    public string DisplayName { get { return displayName; } }
    public string DisplayNameRaw { get { return displayName; } }
}
public static class SceneInfoCollection
{
    public static readonly List<SceneInfoEntry> Entries = new List<SceneInfoEntry>();
    [MethodImpl(MethodImplOptions.NoInlining)] public static SceneInfoEntry GetSceneInfo(string sceneID) { return Entries.Find(e => e.ID == sceneID); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static string GetSceneID(Eflatun.SceneReference.SceneReference sceneRef) { return null; }
    public static string GetSceneID(int buildIndex) { return null; }
}
public class SceneLoadingContext { }
public class Health { public bool IsDead; }
public class CharacterMainControl
{
    public static CharacterMainControl Main;
    public Health Health = new Health();
}
public class LevelManager
{
    // 官方 LevelInited 是**属性**，transpiler 用 AccessTools.PropertyGetter 定位它；
    // 这里必须同样是属性，写成字段会让 PropertyGetter 返回 null，Harmony 随后抛 ArgumentNullException。
    private static bool levelInited;
    public static bool LevelInited
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return levelInited; }
    }
    public static void SetLevelInited(bool value) { levelInited = value; }
    public static int SaveBeforeLoadCalls;
    public readonly UnityEngine.GameObject gameObject;
    public CharacterMainControl MainCharacter;
    public LevelManager(string name) { gameObject = new UnityEngine.GameObject(name); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void NotifySaveBeforeLoadScene(bool saveToFile) { SaveBeforeLoadCalls++; }
}
public class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onFinishedLoadingScene;
    public static void Finish() { IsSceneLoading = false; onFinishedLoadingScene?.Invoke(new SceneLoadingContext()); }
    public static readonly SceneLoader Instance = new SceneLoader();
    public static int LoadCalls;

    /// <summary>
    /// 形状对齐官方 <c>LoadScene(SceneReference, SceneReference, bool, bool, bool, bool, MultiSceneLocation, bool, bool)</c>：
    /// 参数名与类型逐个一致（transpiler 要读状态机的 <c>sceneReference</c> 字段），方法体只保留
    /// 「循环等待 LevelInited」这一处读取——生产 transpiler 断言必须恰好命中 1 处。
    /// **替身边界**：官方返回非泛型 UniTask，本夹具的 <c>UniTask</c> 名字已被静态辅助类占用，
    /// 因此返回 <c>UniTask&lt;bool&gt;</c>；AccessTools 按名字 + 参数类型匹配，补丁目标不受影响。
    /// 循环上限 1000 只是防夹具挂死，不代表官方有上限。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Cysharp.Threading.Tasks.UniTask<bool> LoadScene(
        Eflatun.SceneReference.SceneReference sceneReference,
        Eflatun.SceneReference.SceneReference overrideCurtainScene = null,
        bool clickToConinue = false, bool notifyEvacuation = false, bool doCircleFade = true, bool useLocation = false,
        Duckov.Scenes.MultiSceneLocation location = default(Duckov.Scenes.MultiSceneLocation),
        bool saveToFile = true, bool hideTips = false)
    {
        LoadCalls++;
        for (int step = 0; step < 1000; step++)
        {
            if (LevelManager.LevelInited) return true;
            await Cysharp.Threading.Tasks.UniTask.NextFrame();
        }
        return false;
    }
}

namespace Duckov.Scenes
{
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Eflatun.SceneReference;
    using Cysharp.Threading.Tasks;

    public struct MultiSceneLocation { public string SceneID; public string LocationName; }
    public class SubSceneEntry { public string sceneID; }
    public class MultiSceneCore
    {
        public static MultiSceneCore Instance;
        public readonly GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        private Scene activeSubScene;
        private SubSceneEntry cachedSubsceneEntry;
        private bool isLoading;
        private readonly Dictionary<int, GameObject> setActiveWithSceneObjects = new Dictionary<int, GameObject>();
        public List<SubSceneEntry> SubScenes = new List<SubSceneEntry>();
        public int OriginalLoadCalls;
        public static int Visited;
        public static event Action<MultiSceneCore, Scene> OnSubSceneLoaded;
        public MultiSceneCore(string name) { gameObject = new GameObject(name); }
        public bool IsLoading { get { return isLoading; } }
        public SceneInfoEntry SceneInfo { [MethodImpl(MethodImplOptions.NoInlining)] get { return null; } }
        public string DisplayName { [MethodImpl(MethodImplOptions.NoInlining)] get { return "original"; } }
        public string DisplaynameRaw { [MethodImpl(MethodImplOptions.NoInlining)] get { return "original_raw"; } }
        public static string MainSceneID { [MethodImpl(MethodImplOptions.NoInlining)] get { return "original_main"; } }
        public static string ActiveSubSceneID { [MethodImpl(MethodImplOptions.NoInlining)] get { return "original_active"; } }
        public static void SetVisited(string id) { Visited++; }
        [MethodImpl(MethodImplOptions.NoInlining)] private UniTask<bool> LoadSubScene(SceneReference targetScene, bool withBlackScreen = true)
        { OriginalLoadCalls++; return UniTask<bool>.FromResult(false); }
        public UniTask<bool> Load(SceneReference reference) { return LoadSubScene(reference, false); }
        [MethodImpl(MethodImplOptions.NoInlining)] public Transform GetSetActiveWithSceneParent(int sceneBuildIndex)
        {
            var parent = new GameObject("original"); parent.SetActive(false); return parent.transform;
        }
        private void LocalOnSubSceneLoaded(Scene scene) { GetSetActiveWithSceneParent(scene.buildIndex).gameObject.SetActive(true); }
        public void KeepBackingFields() { if (activeSubScene.IsValid() && cachedSubsceneEntry != null && isLoading) OnSubSceneLoaded?.Invoke(this, activeSubScene); }
    }
    public class SceneLocationsProvider
    {
        public static readonly List<SceneLocationsProvider> ActiveProviders = new List<SceneLocationsProvider>();
        public readonly GameObject gameObject;
        public readonly Dictionary<string, Transform> Locations = new Dictionary<string, Transform>();
        public SceneLocationsProvider(string name) { gameObject = new GameObject(name); ActiveProviders.Add(this); }
        [MethodImpl(MethodImplOptions.NoInlining)] public static SceneLocationsProvider GetProviderOfScene(SceneReference sceneReference)
        { return ActiveProviders.Count == 0 ? null : ActiveProviders[0]; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static SceneLocationsProvider GetProviderOfScene(Scene scene)
        { return ActiveProviders.Count == 0 ? null : ActiveProviders[0]; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static Transform GetLocation(SceneReference scene, string name)
        { return GetProviderOfScene(scene)?.GetLocation(name); }
        [MethodImpl(MethodImplOptions.NoInlining)] public static Transform GetLocation(string sceneID, string name)
        { return ActiveProviders.Count == 0 ? null : ActiveProviders[0].GetLocation(name); }
        public Transform GetLocation(string name) { Transform result; return Locations.TryGetValue(name, out result) ? result : null; }
    }
}

namespace BossRush
{
    public static class ModBehaviour { public static readonly List<string> Errors = new List<string>(); public static void CriticalLog(string key, string text) { Errors.Add(text); } }
    public static class L10n { public static string T(string zh, string en) { return zh; } }
    public static class LocalizationHelper { public static void InjectLocalization(string key, string value) { } }
}

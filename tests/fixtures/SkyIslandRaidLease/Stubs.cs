using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Cysharp.Threading.Tasks { }
namespace UnityEngine
{
    /// <summary>
    /// 官方 `UnityEngine.Object` 的替身。**必须模拟「已销毁对象 == null」**：Unity 重载了 `==`，
    /// 而天空岛 2026-09-10 那次进不去正是踩在这上面——把基地场景里的 `TimeOfDayConfig`
    /// 带过图，基地一卸载它就成了已销毁引用，注入进去等于注入 null（CR-2026-09-10-003）。
    /// 替身若只当它是普通托管对象，夹具永远红不了。
    /// </summary>
    public class Object
    {
        internal bool Destroyed;
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value,null)) return;
            value.Destroyed=true;
            // 官方销毁 GameObject 会连同其组件一起销毁；持有组件引用的一方同样会看到 == null。
            GameObject go=value as GameObject;
            if (!ReferenceEquals(go,null)) foreach (var part in go.Parts) part.Destroyed=true;
        }
        /// <summary>官方 Instantiate 克隆整棵子树并重映射内部引用，副本与原件生命周期自此独立。</summary>
        public static T Instantiate<T>(T original) where T:Component,new()
        {
            return new GameObject(original.gameObject.name+"(Clone)").AddComponent<T>();
        }
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull=ReferenceEquals(left,null)||left.Destroyed;
            bool rightNull=ReferenceEquals(right,null)||right.Destroyed;
            return leftNull||rightNull ? leftNull&&rightNull : ReferenceEquals(left,right);
        }
        public static bool operator !=(Object left, Object right) { return !(left==right); }
        public override bool Equals(object other) { return ReferenceEquals(this,other); }
        public override int GetHashCode()
        { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public class Component : Object { public GameObject gameObject; }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public string name; public bool activeSelf;
        internal readonly List<Component> Parts = new List<Component>();
        public GameObject(string name) { this.name=name; }
        public void SetActive(bool value) { activeSelf=value; }
        public T GetComponent<T>() where T:Component { foreach (var part in Parts) if (part is T) return (T)part; return null; }
        public T AddComponent<T>() where T:Component,new() { var part=new T {gameObject=this}; Parts.Add(part); return part; }
    }
    public class AssetBundle : Object
    {
        internal static AssetBundle Last;
        internal static string ScenePath = BossRush.SkyIslandSceneReferenceBridge.ScenePath;
        internal bool Unloaded;
        public static AssetBundle LoadFromFile(string path) { return Last=new AssetBundle(); }
        public string[] GetAllScenePaths() { return new[] {ScenePath}; }
        public void Unload(bool all) { Unloaded=true; }
    }
    public struct Vector3 { public float x, y, z; public static Vector3 zero { get { return default(Vector3); } } }
    public static class Time { public static float unscaledTime; }
    public static class Debug { public static void LogError(string value) { } public static void LogWarning(string value) { } }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single }
    internal class SceneState { internal bool Loaded; internal string Path; internal GameObject[] Roots; }
    public struct Scene
    {
        internal SceneState State;
        public string path { get { return State == null ? null : State.Path; } }
        public bool isLoaded { get { return State != null && State.Loaded; } }
        public bool IsValid() { return State != null; }
        public GameObject[] GetRootGameObjects() { return State.Roots; }
    }
    public static class SceneManager
    {
        public static event Action<Scene,LoadSceneMode> sceneLoaded;
        public static event Action<Scene> sceneUnloaded;
        internal static Scene Raid;
        public static Scene GetSceneByPath(string path) { return Raid.path == path ? Raid : default(Scene); }
        internal static void LoadRaid(bool validServices=true)
        {
            var world=new GameObject("SkyIslandWorld"); var services=new GameObject("SkyIslandLevel");
            if(validServices) services.AddComponent<LevelConfig>();
            Raid=new Scene { State=new SceneState {Loaded=true,Path=BossRush.SkyIslandSceneReferenceBridge.ScenePath,Roots=new[]{world,services}}};
            if(sceneLoaded!=null) sceneLoaded(Raid,LoadSceneMode.Single);
        }
        internal static void UnloadRaid() { if(Raid.State!=null) Raid.State.Loaded=false; if(sceneUnloaded!=null) sceneUnloaded(Raid); }
        internal static int Subscribers {get{return (sceneLoaded==null?0:sceneLoaded.GetInvocationList().Length)+(sceneUnloaded==null?0:sceneUnloaded.GetInvocationList().Length);}}
        internal static void Reset(){Raid=default(Scene); sceneLoaded=null;sceneUnloaded=null;}
    }
}
namespace Eflatun.SceneReference { public class SceneReference { public string Name; } }
namespace Duckov.Scenes
{
    public struct MultiSceneLocation {public string SceneID,LocationName;}
    public static class MultiSceneCore { public static string ActiveSubSceneID = "BossRush_SkyIsland"; }
}
namespace Duckov.UI
{
    public static class ClosureView
    {
        internal static int Shown;
        internal static bool Throw;
        public static System.Threading.Tasks.Task ShowAndReturnTask(float duration)
        {
            if (Throw) throw new Exception("closure view unavailable");
            Shown++;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static class SceneManagement
        {
            public static Eflatun.SceneReference.SceneReference BaseScene =
                new Eflatun.SceneReference.SceneReference { Name = "Base" };
            public static Eflatun.SceneReference.SceneReference EvacuateScreenScene =
                new Eflatun.SceneReference.SceneReference { Name = "EvacuateScreen" };
        }
    }
}
public struct EvacuationInfo
{
    public string subsceneID; public UnityEngine.Vector3 position;
    public EvacuationInfo(string id, UnityEngine.Vector3 pos) { subsceneID = id; position = pos; }
}
public class LevelManager
{
    public static LevelManager Instance = new LevelManager();
    internal static int Evacuations;
    public void NotifyEvacuated(EvacuationInfo info) { Evacuations++; }
}
namespace Duckov.Buffs {public class Buff {}}
public class AstarPath:UnityEngine.Component {public static AstarPath active;}
public class TimeOfDayConfig:UnityEngine.MonoBehaviour {}
public class LevelConfig:UnityEngine.Component {public TimeOfDayConfig timeOfDayConfig;public List<Duckov.Buffs.Buff> startBuffPrefabs;}
public class Health {public bool IsDead;}
public class CharacterMainControl {public static CharacterMainControl Main;public Health Health=new Health();}
public class SceneLoader
{
    public static SceneLoader Instance=new SceneLoader();public static bool IsSceneLoading;
    internal static TaskCompletionSource<bool> Pending;
    internal static int Loads,Returns;
    internal static bool LastEvacuated;
    public Task LoadScene(string id,Duckov.Scenes.MultiSceneLocation location,bool clickToConinue,bool notifyEvacuation,bool saveToFile)
    {Loads++;IsSceneLoading=true;Pending=new TaskCompletionSource<bool>();return Pending.Task;}
    internal static Eflatun.SceneReference.SceneReference LastCurtain;
    // 与官方同形：返航走 SceneReference 重载，第二个参数才是幕布覆盖。
    public Task LoadScene(Eflatun.SceneReference.SceneReference sceneReference,
        Eflatun.SceneReference.SceneReference overrideCurtainScene=null, bool clickToConinue=false,
        bool notifyEvacuation=false, bool doCircleFade=true, bool useLocation=false,
        Duckov.Scenes.MultiSceneLocation location=default(Duckov.Scenes.MultiSceneLocation),
        bool saveToFile=true, bool hideTips=false)
    {Returns++;LastEvacuated=notifyEvacuation;LastCurtain=overrideCurtainScene;IsSceneLoading=true;Pending=new TaskCompletionSource<bool>();return Pending.Task;}
    internal static void Finish(bool failed=false)
    {var pending=Pending;Pending=null;IsSceneLoading=false;if(failed)pending.SetException(new Exception("official load failed"));else pending.SetResult(true);}
}
namespace BossRush
{
    internal static class ModBehaviour
    {
        internal static string ModPath = System.Environment.CurrentDirectory;
        internal static string GetModPath() { return ModPath; }
    }

    internal static class SkyIslandSceneReferenceBridge
    {
        internal const string SceneId="BossRush_SkyIsland",ScenePath="Assets/SkyIsland/SkyIslandRaid.unity";
        internal static bool EnsureRegistered(){return true;}
        // 初始化令牌替身：只记录真实租约的调用序列，语义与生产桥一致（重复 Begin 不同 owner 直接抛）。
        internal static object Owner; internal static int BoundScenes; internal static string Failure; internal static int Ends;
        internal static void Reset(){Owner=null;BoundScenes=0;Failure=null;Ends=0;}
        internal static void BeginInitialization(object owner)
        {
            if(owner==null) throw new ArgumentNullException("owner");
            if(Owner!=null && !ReferenceEquals(Owner,owner)) throw new InvalidOperationException("上一段天空岛初始化尚未释放");
            Owner=owner;BoundScenes=0;Failure=null;
        }
        internal static void BindInitializationScene(object owner,UnityEngine.SceneManagement.Scene scene)
        { if(ReferenceEquals(Owner,owner)) BoundScenes++; }
        internal static void AbortInitialization(object owner,string reason)
        { if(ReferenceEquals(Owner,owner) && Failure==null) Failure=reason??"cancelled"; }
        internal static void EndInitialization(object owner)
        { if(!ReferenceEquals(Owner,owner)) return; Owner=null;Failure=null;Ends++; }
    }

    internal static class SkyIslandOfficialContract
    {
        internal static bool ActivationAllowed=true;
        internal static int Calls;
        internal static bool ConfiguredAtCall;
        internal static bool VerifyBeforeActivation(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.GameObject services,UnityEngine.GameObject world,out string reason)
        {
            Calls++;
            // 真合同要求 timeOfDayConfig / startBuffPrefabs 非空，而这两项是租约注入的，
            // 包里本来就是空的。如实记下调用当刻的装配状态：租约把注入排到验证之后，
            // 真游戏里就是 100% 进不去岛（CR-2026-09-10-002），这里必须能红。
            LevelConfig config=services==null?null:services.GetComponent<LevelConfig>();
            ConfiguredAtCall=config!=null && config.timeOfDayConfig!=null && config.startBuffPrefabs!=null;
            reason=ActivationAllowed?null:"天空岛必须包含唯一且启用的真实 SceneLocationsProvider";
            return ActivationAllowed;
        }
    }

    /// <summary>
    /// 官方爆炸遮挡补丁的替身。真补丁需要 Harmony 与 UnityEngine.Physics，隔离夹具里跑不了，
    /// 但**它的武装/撤销生命周期是租约的职责**，必须在这里如实记账：
    /// 漏撤销就意味着回到官方地图后爆炸遮挡仍按天空岛口径算。
    /// </summary>
    internal static class SkyIslandExplosionObstaclePatch
    {
        internal static int Arms, Disarms;
        internal static bool Armed;
        internal static void Arm(UnityEngine.SceneManagement.Scene scene){Arms++;Armed=true;}
        internal static void Disarm(){Disarms++;Armed=false;}
        internal static void Reset(){Arms=0;Disarms=0;Armed=false;}
    }
}

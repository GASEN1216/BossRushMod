using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Object> All = new List<Object>();
        public bool Destroyed;
        public Object() { All.Add(this); }
        public static implicit operator bool(Object value) { return !ReferenceEquals(value, null) && !value.Destroyed; }
        public static bool operator ==(Object a, Object b) { bool an = !a, bn = !b; return an || bn ? an && bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static T FindObjectOfType<T>() where T : Object { return FindObjectsOfType<T>().FirstOrDefault(); }
        public static T[] FindObjectsOfType<T>() where T : Object
        { return All.OfType<T>().Where(x => x && (!(x is Component) || ((Component)(Object)x).gameObject.activeInHierarchy)).ToArray(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (Component c in go.Parts) c.Destroyed = true;
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T : Component { return gameObject.GetComponentsInChildren<T>(includeInactive); }
        public T GetComponentInChildren<T>(bool includeInactive=false) where T : Component { return GetComponentsInChildren<T>(includeInactive).FirstOrDefault(); }
    }
    public class MonoBehaviour : Component { public bool enabled=true; }
    public class GameObject : Object
    {
        public string name;
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy) && !Destroyed; } }
        public SceneManagement.Scene scene = new SceneManagement.Scene { handle = 1, isLoaded = true };
        public readonly List<Component> Parts = new List<Component>();
        public readonly RectTransform transform;
        public GameObject(string name = "") { this.name = name; transform = new RectTransform { gameObject = this }; Parts.Add(transform); }
        public void SetActive(bool active) { activeSelf = active; }
        public T AddComponent<T>() where T : Component, new() { T value = new T { gameObject = this }; Parts.Add(value); return value; }
        public T GetComponent<T>() where T : Component { return Parts.OfType<T>().FirstOrDefault(x => x); }
        public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T : Component
        {
            var result = new List<T>();
            if (includeInactive || activeInHierarchy) result.AddRange(Parts.OfType<T>().Where(x => x));
            foreach (Transform child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
    }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public Vector3 position, localPosition;
        public Vector3 forward = Vector3.forward;
        private Quaternion currentRotation;
        public Quaternion rotation { get { return currentRotation; } set { currentRotation = value; forward = value.direction; } }
        public void SetParent(Transform next, bool worldPositionStays = false) { if (parent != null) parent.Children.Remove(this); parent = next; if (next != null) next.Children.Add(this); }
        public void SetAsLastSibling() { }
        public Vector3 TransformPoint(Vector3 point)
        { Vector3 f=forward.normalized; return position + new Vector3(f.z*point.x+f.x*point.z,point.y,-f.x*point.x+f.z*point.z); }
    }
    public class RectTransform : Transform { public int LayoutUpdates; public void ForceUpdateRectTransforms() { LayoutUpdates++; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return magnitude > 0 ? this / magnitude : new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float s) { return new Vector3(a.x*s,a.y*s,a.z*s); }
        public static Vector3 operator /(Vector3 a, float s) { return a*(1/s); }
        public static implicit operator Vector3(Vector2 a) { return new Vector3(a.x,a.y,0); }
        public static implicit operator Vector2(Vector3 a) { return new Vector2(a.x,a.y); }
        public static float Distance(Vector3 a, Vector3 b) { return (a-b).magnitude; }
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
    public struct Quaternion { public Vector3 direction; public static Quaternion LookRotation(Vector3 d) { return new Quaternion { direction=d.normalized }; } }
    public struct Color { }
    public class Collider : Component { public bool enabled = true, isTrigger; }
    public class SphereCollider : Collider { public float radius; public Vector3 center; }
    public struct LayerMask { private int value; public static implicit operator LayerMask(int v) { return new LayerMask {value=v}; } public static int NameToLayer(string name) { return 2; } }
    public static class Physics
    {
        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] result, LayerMask mask)
        {
            int count=0;
            foreach (SphereCollider c in Object.FindObjectsOfType<SphereCollider>())
                if (c.enabled && Vector3.Distance(position,c.transform.TransformPoint(c.center)) <= radius+c.radius && count<result.Length) result[count++]=c;
            return count;
        }
    }
    public static class Mathf
    {
        public const float PI=(float)Math.PI;
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Clamp01(float v) { return Math.Max(0,Math.Min(1,v)); }
        public static float Min(float a,float b) { return Math.Min(a,b); }
        public static int RoundToInt(float v) { return (int)Math.Round(v); }
    }
    public class AudioClip : Object { public static AudioClip Create(string n,int samples,int channels,int rate,bool stream) { return new AudioClip(); } public void SetData(float[] samples,int offset) { } }
    public class AudioSource : Component
    {
        public bool playOnAwake, isPlaying;
        public float spatialBlend,volume,minDistance,maxDistance,dopplerLevel;
        public AudioClip clip;
        public int Plays;
        public void Play() { Plays++; isPlaying=true; }
        public void Stop() { isPlaying=false; }
    }
    public class ParticleSystem { public void Emit(int count) { } }
    public class Sprite : Object { }
    public class CanvasGroup : Component { public float alpha; }
    public class Camera : Object { public static Camera main=new Camera(); public Vector3 WorldToScreenPoint(Vector3 p) { return p; } }
    public static class RectTransformUtility { public static bool ScreenPointToLocalPointInRectangle(RectTransform r, Vector2 p, Camera c,ref Vector2 result) { result=p; return true; } }
    public static class Shader { public static int PropertyToID(string n) { return 1; } public static void SetGlobalFloat(int i,float v) { } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; public bool isLoaded; }
    public static class SceneManager { public static void MoveGameObjectToScene(GameObject go,Scene scene) { go.scene=scene; } }
}
namespace UnityEngine.Events
{
    public class UnityEvent { public static int TotalInvocations; public int Invocations; public void Invoke() { Invocations++; TotalInvocations++; } }
    public class UnityEvent<T1,T2> { public int Invocations; public void Invoke(T1 a,T2 b) { Invocations++; } }
}
namespace UnityEngine.UI { public class Image : Component { public Sprite sprite; } }
namespace UnityEngine.UI.ProceduralImage { public class ProceduralImage : Component { public Color color; } }
namespace TMPro { public class TextMeshProUGUI : Component { public string text; } }
namespace SodaCraft.Localizations
{
    public class LocalizationKeyAttribute : Attribute { public LocalizationKeyAttribute(string s) { } }
    public static class LocalizationManager
    {
        public static readonly Dictionary<string,string> Text = new Dictionary<string,string>();
        public static string ToPlainText(this string key) { string value; return Text.TryGetValue(key ?? "",out value) ? value : "*"+key+"*"; }
    }
}
namespace Cysharp.Threading.Tasks { public static class Extensions { public static async void Forget(this Task task) { await task; } } }
namespace Duckov
{
    public static class AudioManager
    {
        public class Bus { public float Volume=1f; public bool Mute; }
        public static Bus Master = new Bus(), Sfx = new Bus();
        public static Bus GetBus(string name) { return name=="Master" ? Master : name=="Master/SFX" ? Sfx : null; }
    }
}
namespace Duckov.Economy { public class EconomyManager { public static EconomyManager Instance=new EconomyManager(); public static long Money; public static bool Add(long v) { Money+=v; return true; } } }
namespace Duckov.MiniMaps { public class SimplePointOfInterest : Component { public Color Color; public float ScaleFactor; public bool IsArea; public void Setup(object a,string key) { } } }
namespace Duckov.Utilities
{
    public class PrefabPool<T> where T : Component
    {
        private readonly List<T> active = new List<T>();
        private readonly Stack<T> inactive = new Stack<T>();
        public PrefabPool(T prefab,object a,object b,object c,object d,bool e,int f,int g,object h) { }
        public T Get(object context)
        {
            T row=inactive.Count>0 ? inactive.Pop() : (T)(Component)BossRush.Probe.NewRow();
            row.gameObject.SetActive(true); active.Add(row); return row;
        }
        public void ReleaseAll()
        {
            foreach(T row in active) { row.gameObject.SetActive(false); inactive.Push(row); }
            active.Clear();
        }
    }
}
public interface IProgress { Progress GetProgress(); }
public struct Progress { public bool inProgress; }
public sealed partial class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main;
    public Transform modelRoot;
    public Health Health = new Health();
    public void PopText(string text,float lifetime) { }
    public bool IsMainCharacter=true;
    public CA_Interact interactAction;
    public CharacterActionBase CurrentAction;
}
public class Health { public bool IsDead; }
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance;
    public CharacterMainControl MainCharacter;
    public GameCamera GameCamera=new GameCamera();
}
public class GameCamera { public Camera renderCamera=Camera.main; }
public static class InputManager { public static bool InputActived=true; }
public partial class InteractableBase : MonoBehaviour
{
    private CharacterMainControl interactCharacter;
    public static event Action<InteractableBase> OnInteractStartStaticEvent;
    private Component markerObject;
    private float interactTimer, lastStopTime=-1f;
    private bool timeOut;
    public bool requireItemUsed, finishWhenTimeOut=true, disableOnFinish;
    public float coolTime, InteractTime;
    public UnityEngine.Events.UnityEvent OnRequiredItemUsedEvent;
    public UnityEngine.Events.UnityEvent<CharacterMainControl,InteractableBase> OnInteractStartEvent, OnInteractTimeoutEvent, OnInteractFinishedEvent;
    private int requireItemDataKeyCached;
    private bool UseRequiredItem(CharacterMainControl character) { throw new InvalidOperationException("Required-item branch is outside fixture scope"); }
    protected virtual void OnInteractStart(CharacterMainControl character) { }
    protected virtual void OnInteractStop() { }
    protected virtual void OnInteractFinished() { }
    protected virtual void OnUpdate(CharacterMainControl character,float delta) { }

    public bool overrideInteractName, MarkerActive, interactableGroup, requireItem;
    public string _overrideInteractNameKey, defaultInteractNameKey;
    public Collider interactCollider;
    public Vector3 interactMarkerOffset;
    public List<InteractableBase> otherInterablesInGroup;
    private List<InteractableBase> _interactbleList = new List<InteractableBase>();
    public enum WhenToUseRequireItemTypes { None, OnFinshed, OnTimeOut, OnStartInteract }
    public WhenToUseRequireItemTypes whenToUseRequireItem;
    protected virtual void Awake() { }
    protected virtual void Start() { }
    protected virtual bool IsInteractable() { return true; }
    protected virtual void OnTimeOut() { }
    public Progress GetProgress() { return new Progress(); }
    public string GetRequiredItemName() { return ""; }
    public Sprite GetRequireditemIcon() { return null; }
    public Tuple<bool,object> TryGetRequiredItem(CharacterMainControl player) { return Tuple.Create(false,(object)null); }
}
public class CourierInteractable : InteractableBase { }
public enum Teams { player }
public enum SoundTypes { unknowNoise }
public struct AISound { public Vector3 pos; public float radius; public Teams fromTeam; public SoundTypes soundType; public CharacterMainControl fromCharacter; public UnityEngine.Object fromObject; }
public static class AIMainBrain { public static readonly List<AISound> Sounds = new List<AISound>(); public static void MakeSound(AISound sound) { Sounds.Add(sound); } }
namespace BossRush.Utils
{
    public static class NPCInteractionGroupHelper
    {
        public static List<InteractableBase> GetOrCreateGroupList(InteractableBase owner,string label)
        { owner.interactableGroup=true; if(owner.otherInterablesInGroup==null) owner.otherInterablesInGroup=new List<InteractableBase>(); return owner.otherInterablesInGroup; }
        public static List<InteractableBase> PrepareGroupedInteractionOwner(InteractableBase owner,string label) { return GetOrCreateGroupList(owner,label); }
        public static T AddSubInteractable<T>(Transform parent,string name,List<InteractableBase> group,Action<T> init) where T:InteractableBase,new()
        { GameObject go=new GameObject(name); go.SetActive(false); go.transform.SetParent(parent,false); T value=go.AddComponent<T>(); init(value); Probe.Call(value,"Awake"); Probe.Call(value,"Start"); go.SetActive(true); group.Add(value); return value; }
    }
}
namespace BossRush
{
    public class ModBehaviour { public static void DevLog(string m) { Console.WriteLine(m); } }
    public struct SceneRuntimeContext { }
    public abstract class BossRushRuntimeModuleBase
    {
        public virtual string ModuleName { get { return ""; } }
        public virtual void OnAwake(ModBehaviour host) { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float delta,float unscaled) { }
        public virtual void OnDestroy() { }
    }
    public static class BossRushUI
    {
        public static bool Paused,Hidden;
        public static bool IsGamePaused() { return Paused; }
        public static bool IsOfficialHudHidden() { return Hidden; }
    }
    public static class BossRushUIColors { public static Color WarningText; }
    public static class LocalizationHelper { public static void InjectLocalization(string key,string value) { SodaCraft.Localizations.LocalizationManager.Text[key]=value; } }
    public static class L10n { public static bool English; public static string T(string cn,string en) { return English?en:cn; } }
    public static class FieldAdventureSupport
    {
        public static bool Ground=true, Raid=true;
        public static int SceneHandle=1;
        public static Vector3 GroundPosition;
        public static bool FixedGround;
        public static bool TryGetRaid(ModBehaviour owner,out CharacterMainControl player,out UnityEngine.SceneManagement.Scene scene)
        { player=CharacterMainControl.Main; scene=new UnityEngine.SceneManagement.Scene {handle=SceneHandle,isLoaded=true}; return Raid; }
        public static bool TryFindGround(Vector3 origin,float min,float max,out Vector3 result)
        { result=FixedGround ? GroundPosition : origin + new Vector3(min>20 ? 40 : min>2.9f ? 3 : 1,0,0); return Ground; }
        public static bool TryFindGroundBands(Vector3 origin,float[] bands,out Vector3 result)
        { result=default(Vector3); for(int i=0;i+1<bands.Length;i+=2) if(TryFindGround(origin,bands[i],bands[i+1],out result)) return true; return false; }
        public static void Notify(string cn,string en) { }
    }
    public static class DialogueActorFactory { public static object CreateBilingual(GameObject g,string k,string cn,string en) { return new object(); } public static void RefreshBilingualName(string k,string cn,string en) { } }
    public static class DialogueManager
    {
        public static Task ShowDialogueSequenceBilingual(object actor,string[][] lines,string key,CancellationToken token) { return Task.CompletedTask; }
        public static int Choice;
        public static TaskCompletionSource<int> PendingChoice;
        public static Task<int> ShowMultipleChoiceBilingual(object actor,string[][] lines,float delay,string key,CancellationToken token)
        { return PendingChoice != null ? PendingChoice.Task : Task.FromResult(Choice); }
    }
    internal sealed partial class OddParcelVisual : IDisposable
    {
        private readonly GameObject root;
        private readonly OddParcelKind kind;
        private AudioSource voice;
        private AudioClip quack;
        private float shout,bounce;
        private ParticleSystem spray;
        public OddParcelVisual(GameObject parent,OddParcelKind kind,bool station,bool destination)
        { this.kind=kind; root=new GameObject("VoiceHost"); root.transform.SetParent(parent.transform,false); if(!station) BuildVoice(); }
        public void Tick(float dt,float pressure,bool carried) { }
        // 爆发 / 声浪粒子属于共享 FxKit，执行回归只验证音量与叫声时序。
        private void PlayBurstFx(bool celebrate) { }
        private void PlayShoutRing() { }
        public void Dispose() { if(voice!=null) voice.Stop(); UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(quack); }
    }
    internal static class Probe
    {
        internal static InteractHUD Hud;
        internal static InteractSelectionHUD NewRow()
        {
            GameObject go=new GameObject("OfficialRow"); go.transform.SetParent(Hud.master,false);
            InteractSelectionHUD row=go.AddComponent<InteractSelectionHUD>();
            row.text=go.AddComponent<TMPro.TextMeshProUGUI>();
            row.selectIndicator=new GameObject("Selected"); row.selectionPoint=new GameObject("Point"); row.upDownIndicator=new GameObject("UpDown");
            row.requireCanvasGroup=go.AddComponent<CanvasGroup>(); row.OnSelectedEvent=new UnityEngine.Events.UnityEvent();
            row.background=go.AddComponent<UnityEngine.UI.ProceduralImage.ProceduralImage>(); return row;
        }
        internal static object Call(object target,string name,params object[] args)
        {
            Type type=target.GetType(); MethodInfo method=null;
            while(type!=null && method==null) { method=type.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly); type=type.BaseType; }
            try { return method.Invoke(target,args); } catch(TargetInvocationException e) { throw e.InnerException; }
        }
        internal static void Set(object target,string field,object value) { target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(target,value); }
        internal static T Get<T>(object target,string field) { return (T)target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(target); }
    }
}

namespace UnityEngine
{
    public static class Time { public static float time; }
    public static class Debug { public static void Log(string message) { } public static void LogException(Exception exception) { throw new InvalidOperationException("Unexpected official interaction exception",exception); } }
}
namespace Duckov.Scenes
{
    public sealed class MultiSceneCore : UnityEngine.Object
    {
        public static MultiSceneCore Instance;
        public readonly Dictionary<int,object> inLevelData=new Dictionary<int,object>();
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(TaskBuilder))]
    public struct UniTask
    {
        internal Task Task;
        public TaskAwaiter GetAwaiter() { return Task.GetAwaiter(); }
        internal static readonly Queue<TaskCompletionSource<bool>> Delays = new Queue<TaskCompletionSource<bool>>();
        public static UniTask Delay(TimeSpan time, DelayType type)
        { var t = new TaskCompletionSource<bool>(); Delays.Enqueue(t); return new UniTask { Task = t.Task }; }
    }
    public struct UniTask<T>
    {
        internal Task<T> Task;
        public TaskAwaiter<T> GetAwaiter() { return Task.GetAwaiter(); }
    }
    [AsyncMethodBuilder(typeof(VoidBuilder))]
    public struct UniTaskVoid { internal Task Task; public void Forget() { Pending.Add(Task); } internal static List<Task> Pending = new List<Task>(); }
    public struct VoidBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static VoidBuilder Create() { return new VoidBuilder { builder = AsyncTaskMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid { Task = builder.Task }; } }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception e) { builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { builder.SetStateMachine(s); }
        public void Start<T>(ref T s) where T : IAsyncStateMachine { builder.Start(ref s); }
        public void AwaitOnCompleted<T, S>(ref T a, ref S s) where T : INotifyCompletion where S : IAsyncStateMachine { builder.AwaitOnCompleted(ref a, ref s); }
        public void AwaitUnsafeOnCompleted<T, S>(ref T a, ref S s) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref a, ref s); }
    }
    public enum DelayType { UnscaledDeltaTime }
    public struct TaskBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static TaskBuilder Create() { return new TaskBuilder { builder=AsyncTaskMethodBuilder.Create() }; }
        public UniTask Task { get { return new UniTask { Task=builder.Task }; } }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception e) { builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { builder.SetStateMachine(s); }
        public void Start<T>(ref T s) where T:IAsyncStateMachine { builder.Start(ref s); }
        public void AwaitOnCompleted<T,S>(ref T a,ref S s) where T:INotifyCompletion where S:IAsyncStateMachine { builder.AwaitOnCompleted(ref a,ref s); }
        public void AwaitUnsafeOnCompleted<T,S>(ref T a,ref S s) where T:ICriticalNotifyCompletion where S:IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref a,ref s); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed, bn=ReferenceEquals(b,null)||b.Destroyed; return an||bn ? an==bn : ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) { return !(a==b); }
        public override bool Equals(object o) { return this == o as Object; }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator /(Vector3 a,float b) { return new Vector3(a.x/b,a.y/b,a.z/b); }
        public static float Distance(Vector3 a,Vector3 b) { return (float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z)); }
    }
    public class Transform { public Vector3 position; }
    public class GameObject { public string name = "Character"; }
    public static class Debug { public static void Log(string s) { } public static void LogWarning(string s) { } }
    public class Coroutine { }
    public class Sprite { }
    public static class Time { public static float unscaledTime=1, unscaledDeltaTime=.5f; }
    public static class Mathf { public static int RoundToInt(float f) { return (int)Math.Round(f); } }
}
namespace UnityEngine.SceneManagement { public struct Scene { public string name; } public static class SceneManager { public static Scene GetActiveScene() { return new Scene(); } } }
namespace Duckov.Scenes { public static class MultiSceneCore { public static string MainSceneID="main"; } }
public class SceneInfoEntry { public string DisplayName; }
public static class SceneInfoCollection
{
    public static Dictionary<string,SceneInfoEntry> Infos=new Dictionary<string,SceneInfoEntry>();
    public static SceneInfoEntry GetSceneInfo(string id) { SceneInfoEntry value; return Infos.TryGetValue(id,out value)?value:null; }
}
public enum Teams { player,scav,wolf,middle }
public static class Team { public static bool IsEnemy(Teams a,Teams b) { return a!=b && a!=Teams.middle && b!=Teams.middle; } }
public struct DamageInfo { public CharacterMainControl fromCharacter; public Vector3 damagePoint; }
public class LevelManager
{
    public static LevelManager Instance=new LevelManager();
    public static bool LevelInited, AfterInit;
    public bool IsBaseLevel;
    private CharacterMainControl petCharacter;
    public CharacterMainControl PetCharacter { get { return petCharacter; } }
    public void SetOfficialPet(CharacterMainControl pet) { petCharacter=pet; }
}
public static class SceneLoader { public static bool IsSceneLoading; }
public static class LevelConfig { public static bool SavePet=true; }
public static class PetProxy { public static ItemStatsSystem.Inventory PetInventory; }
public static class PlayerStorage
{
    public static List<ItemStatsSystem.Item> Rescued=new List<ItemStatsSystem.Item>();
    public static List<ItemStatsSystem.Item> IncomingItemBuffer { get { return Rescued; } }
    public static bool ThrowBeforeAccept, ThrowAfterAccept;
    public static void Push(ItemStatsSystem.Item item,bool buffer)
    {
        if(ThrowBeforeAccept)throw new Exception("notification before accept");
        Rescued.Add(item);
        item.Detach();
        if(ThrowAfterAccept)throw new Exception("notification after accept");
        item.DestroyTree();
    }
}
public class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main=new CharacterMainControl();
    public Transform transform=new Transform(); public Health Health=new Health();
    public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();
    public ItemStatsSystem.Item CharacterItem;
    public int PetCapcity { get { return CharacterItem==null?0:Mathf.RoundToInt(CharacterItem.GetStatValue("PetCapcity".GetHashCode())); } }
    public bool IsMainCharacter, IsCompanion; public Teams Team;
}
namespace HarmonyLib
{
    static class AccessTools { public static System.Reflection.FieldInfo Field(Type t,string name) { return t.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic); } }
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add }
    public class Modifier
    {
        public float Value; public object Source;
        public Modifier(ModifierType type,float value,object source) { Value=value;Source=source; }
    }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object
    {
        public int BaseCapacity; public bool HasCapacityStat=true;
        public Inventory InInventory;
        public void Detach() { if(InInventory!=null)InInventory.RemoveItem(this); }
        public void DestroyTree() { Destroyed=true; }
        private readonly List<Stats.Modifier> modifiers=new List<Stats.Modifier>();
        public float GetStatValue(int key) { float value=BaseCapacity;foreach(var m in modifiers)value+=m.Value;return value; }
        public void RemoveAllModifiersFrom(object source) { modifiers.RemoveAll(m=>m.Source==source); }
        public bool AddModifier(string key,Stats.Modifier m) { if(!HasCapacityStat || key!="PetCapcity")return false; modifiers.Add(m);return true; }
    }
    public class Inventory : UnityEngine.Object
    {
        public int Capacity; public bool Loading; public readonly List<Item> Content=new List<Item>();
        public void SetCapacity(int n) { Capacity=n; }
        public void RemoveItem(Item item) { int i=Content.IndexOf(item);if(i>=0)Content[i]=null;item.InInventory=null; }
    }
}
public class Health { public bool IsDead,IsMainCharacterHealth,IsCompanion; public CharacterMainControl Character; public CharacterMainControl TryGetCharacter(){return Character;} }
public class CharacterRandomPreset { }

namespace BossRush
{
    public partial class ModBehaviour : UnityEngine.Object
    {
        public static BossRushMapConfig[] Maps;
        public static BossRushMapConfig[] GetAllMapConfigs() { return Maps; }
        public static void DevLog(string s) { }
        public void ShowMessage(string s) { }
        public static bool IsModeHRunInProgressSafe() { return false; }
    }
    static class L10n { public static bool IsChinese=true; public static string T(string a,string b) { return IsChinese?a:b; } }
    static class LocalizationHelper { public static string GetLocalizedText(string key) { return key; } }
    static class MapPointSceneResolver { public static string Active="sub"; public static string Resolve() { return Active; } }
    enum PetNestPetState { InNest,Deployed,Downed,OnExpedition }
    class PetNestTalentEntry { public string statKey; public bool percentage; public float value; }
    class PetNestPetRecord { public string id,lineageKey; public int state,level; public List<PetNestTalentEntry> talents; }
    static class PetNestService
    {
        public static PetNestPetRecord DeployedPet;
        public static PetNestPetRecord TryGetPet(string id) { return DeployedPet; }
        public static string GetPetDisplayName(PetNestPetRecord p) { return p.id; }
        public static void StageCommit() { }
    }
    static class PetNestCompanionAgent { public static bool IsCompanionHealth(Health h) { return h.IsCompanion; } public static bool IsCompanionCharacter(CharacterMainControl c) { return c.IsCompanion; } }
    class PetNestLineageInfo { public float ModelScale; public string DisplayName="lineage"; public string LineageKey="boss"; }
    static class PetNestLineageCatalog
    {
        public static IList<PetNestLineageInfo> All=new List<PetNestLineageInfo>();
        public static bool TryGet(string key,out PetNestLineageInfo info) { info=new PetNestLineageInfo();return true; }
    }
    class PetNestCompanionHandle { public CharacterMainControl Character; public bool Activated; public int Cleanups; }
    static class PetNestCompanionSpawner
    {
        public static Vector3 StagingOffset,SpawnOffset;
        public static readonly Queue<TaskCompletionSource<PetNestCompanionHandle>> Requests=new Queue<TaskCompletionSource<PetNestCompanionHandle>>();
        public static int Activated;
        public static CharacterRandomPreset ResolveCompanionSourcePreset(string s) { return new CharacterRandomPreset(); }
        public static Cysharp.Threading.Tasks.UniTask<PetNestCompanionHandle> CreateIsolatedAsync(CharacterRandomPreset s,string key,float scale,Vector3 p)
        { var t=new TaskCompletionSource<PetNestCompanionHandle>(); Requests.Enqueue(t); return new Cysharp.Threading.Tasks.UniTask<PetNestCompanionHandle> { Task=t.Task }; }
        public static bool TryActivate(PetNestCompanionHandle h,Vector3 pos,CharacterMainControl p,ModBehaviour o,PetNestPetRecord pet,out string reason)
        { reason=null;h.Activated=true;Activated++;return true; }
        public static void CleanupOnce(PetNestCompanionHandle h) { if(h==null)return;h.Cleanups++;h.Activated=false;if(h.Character!=null)h.Character.Destroyed=true;h.Character=null; }
    }
    static class PetNestTuning { public const int MaxBaseIdleCompanions=3,CompanionPetCapacityBonus=1,PetLevelsPerCapacityBonus=3; public const float BaseIdleSpawnIntervalSeconds=.1f; }
    static class PetNestModeGate { public const string ReasonQueryFailed="query"; public static bool Allowed=true; public static bool IsCompanionAllowed(ModBehaviour o,out string reason) { reason=null;return Allowed; } }
    static class PetNestLocalization { public static string DescribeFailure(string s) { return s; } }
    static class PetNestDownedHandler { public static void EnsureHurtSubscribed() { } public static void ShutdownHurtSubscription() { } }
    static class PetNestProgressionService { public static void EnsureKillTrackingSubscribed() { } public static void ShutdownKillTracking() { } }
    static class PetNestCompanionHudView { public static void EnsureCreated() { } public static void Destroy() { } }
    class PetNestPersonality { public int ExtraPetCapacity; public static PetNestPersonality Resolve(PetNestPetRecord p) { return new PetNestPersonality(); } }
    static class PetNestPersistenceAccess { public static bool BeginTransaction(out string reason) { reason=null;return true; } public static void AbortTransaction() { } }
    class Label { public string text; public float fontSize, fontSizeMax; public object gameObject = new object(); }
    class RevealResult { public string LineageDisplayName="actual"; public bool Shiny=true; public PetNestPetRecord Pet=new PetNestPetRecord { lineageKey="boss" }; }
    static class BossRushUI { public static bool Paused; public static bool IsGamePaused() { return Paused; } }
    static class BossRushUIColors { public const int Accent=1, AccentFill=2, RarityLegendary=3; }
    // 2026-09-23 审美修复（UA-10 / UA-11）：孵化揭晓与远征翻牌换成有图的卡片、入场动效与流光。表现层替身只要能编译，不参与断言。
    static class BossRushUIEntranceAnimation { public static void Play(object go, float delay, float duration, float rise) { } }
    static class PetNestShinyTextShimmer { public static void Attach(Label text) { } }
    static class PetNestUIPages
    {
        public static UnityEngine.Sprite ResolveItemIcon(int typeId) { return null; }
        public static UnityEngine.Sprite ResolveLineagePortrait(string lineageKey) { return null; }
    }
    static class RelicEggConfig { public const int TYPE_ID = 500059; }
    class FakeCard { public bool activeSelf = true; }
    class FakeStroke { public int color; }
    static class ZombieModeUIHelper { public static void SetButtonBaseColor(object button,int color) { } }
    partial class PetNestHatchRevealView
    {
        const float BeginSeconds=1,RollBeginSeconds=1,RollStepSeconds=.2f,ShowResultSeconds=1,PickupSeconds=.5f;
        const int RollStepCount=3;
        Label _rollText=new Label(),_resultText=new Label(),_detailText=new Label(),_dismissLabel=new Label();
        object _dismissButton=new object();
        RevealResult _result=new RevealResult();
        Coroutine _playRoutine=new Coroutine();
        bool _resultShown,_finished;
        bool _shaking; float _popElapsed; FakeCard _card=new FakeCard(); FakeStroke _cardStroke=new FakeStroke();
        void SetCardSprite(UnityEngine.Sprite sprite,bool rolling) { }
        void ResetCardTransform() { }
        void SetDetail(string text) { _detailText.text=text; }
        void AddChromaRails() { }
        // 结果出来后玩家点「关闭」走 CloseByPlayer（旧写法直接 Stop）；计数语义不变。
        void CloseByPlayer() { Closed++; }
        public static int Music,Closed;
        static void SetText(Label t,string s) { t.text=s; }
        static void Stop() { Closed++; }
        void StopCoroutine(Coroutine c) { }
        string BuildResultTitle() { return "name"; }
        string BuildDetailText() { return "personality + talents + chroma"; }
        static void PlayJackpotMusic() { Music++; }
        public void Click() { OnDismiss(); }
        public bool Complete { get { return _finished && _detailText.text==BuildDetailText(); } }
        public static IEnumerator Wait() { return WaitForPresentation(1); }
    }
    class PetNestExpeditionRecord { public string id; }
    static class PetNestExpeditionService
    {
        public static int Revealed;
        public static bool MarkRevealed(PetNestExpeditionRecord record, out string reason) { reason = null; Revealed++; return true; }
    }
    partial class PetNestExpeditionRevealView
    {
        const float CardEnterSeconds = .3f, CardFlipSeconds = .55f, CardHoldSeconds = 1.4f;
        Label _cardText = new Label(), _detailText = new Label(), _skipLabel = new Label();
        object _skipButton = new object();
        bool _finished;
        public bool Finished { get { return _finished; } }
        void ShowBack(PetNestExpeditionRecord record) { }
        void ClearLoot() { }
        IEnumerator Flip(PetNestExpeditionRecord record) { yield break; }
        void BuildLoot(PetNestExpeditionRecord record) { }
        List<PetNestExpeditionRecord> _pending = new List<PetNestExpeditionRecord> { new PetNestExpeditionRecord { id = "trip" } };
        public static int Closed;
        static void SetText(Label target, string text) { target.text = text; }
        static string BuildCardTitle(PetNestExpeditionRecord record) { return record.id; }
        static string BuildCardDetail(PetNestExpeditionRecord record) { return "result"; }
        static void Stop() { Closed++; }
        public bool HasDetail { get { return !string.IsNullOrEmpty(_detailText.text); } }
        public IEnumerator Play() { return PlayRoutine(); }
    }
}

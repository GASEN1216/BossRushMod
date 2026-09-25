using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

internal static class Probe
{
    internal static readonly List<string> Trace = new List<string>();
    internal static readonly Queue<TaskCompletionSource<bool>> Yields = new Queue<TaskCompletionSource<bool>>();
    internal static Task NextFrame() { var completion = new TaskCompletionSource<bool>(); Yields.Enqueue(completion); return completion.Task; }
    internal static void Frame() { var current = Yields.Dequeue(); current.SetResult(true); }
}

namespace Cysharp.Threading.Tasks
{
    public static class UniTask { public static Task Yield() { return Probe.NextFrame(); } }
    [AsyncMethodBuilder(typeof(AsyncUniTaskMethodBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> Inner;
        public TaskAwaiter<T> GetAwaiter() { return Inner.GetAwaiter(); }
    }
    public struct AsyncUniTaskMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> inner;
        public static AsyncUniTaskMethodBuilder<T> Create() { return new AsyncUniTaskMethodBuilder<T> { inner = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T> { Inner = inner.Task }; } }
        public void SetResult(T value) { inner.SetResult(value); }
        public void SetException(Exception exception) { inner.SetException(exception); }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { inner.SetStateMachine(stateMachine); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine { inner.Start(ref stateMachine); }
        public void AwaitOnCompleted<TAwaiter,TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { inner.AwaitOnCompleted(ref awaiter, ref stateMachine); }
        public void AwaitUnsafeOnCompleted<TAwaiter,TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine); }
    }
    public sealed class UniTaskCompletionSource<T>
    {
        private readonly TaskCompletionSource<T> inner = new TaskCompletionSource<T>();
        public UniTask<T> Task { get { return new UniTask<T> { Inner = inner.Task }; } }
        public bool TrySetResult(T result) { return inner.TrySetResult(result); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed, bn=ReferenceEquals(b,null)||b.Destroyed; return an||bn ? an==bn : ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) { return !(a==b); }
        public override bool Equals(object other) { return ReferenceEquals(this,other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            var gameObject=value as GameObject;
            if (gameObject != null) { Probe.Trace.Add("destroy"); foreach(var component in gameObject.Components) component.Destroyed=true; }
            value.Destroyed=true;
        }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public sealed class Transform : Component { public Vector3 position; }
    public sealed class GameObject : Object
    {
        public string name; public Transform transform; internal readonly List<Component> Components=new List<Component>();
        public GameObject(string value) { name=value; transform=new Transform { gameObject=this }; Components.Add(transform); }
    }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public static class Mathf { public static int Max(int a,int b) { return Math.Max(a,b); } public static float Max(float a,float b) { return Math.Max(a,b); } }
}

namespace BossRush
{
    public enum Teams { player, scav, wolf }
    public static class Team { public static bool IsEnemy(Teams player,Teams enemy) { return enemy==Teams.wolf; } }
    public sealed class Health { public float MaxHealth=50; public void SetHealth(float value) { Probe.Trace.Add("heal"); } }
    public sealed class CharacterMainControl : Component
    {
        public static CharacterMainControl Main; public Teams Team; public bool dropBoxOnDead=true; public Health Health=new Health();
        public readonly AICharacterController AI=new AICharacterController();
        public CharacterMainControl(string name) { gameObject=new GameObject(name); gameObject.Components.Add(this); }
        public void SetTeam(Teams team) { Team=team; Probe.Trace.Add("team:"+team); }
    }
    public sealed class AICharacterController { public float forceTracePlayerDistance; public bool noticed; }
    public enum ZombieModeEnemyKind { Normal, Special, Elite }
    public enum ZombieModeSpecialKind { None, Test }
    public enum ZombieModeEliteAffix { Test }
    public enum ZombieModeBossKind { Titan, Hunter }
    public sealed class ZombieModeEnemyRuntimeMarker : Component { }
    public sealed class EnemyPresetInfo { public string name,displayName; public float baseHealth; }
    public sealed class EnemySpawnContext { public CharacterMainControl character; }
    public sealed class ZombieModeBossInstance
    {
        public CharacterMainControl Character; public ZombieModeBossKind Kind; public ZombieModeEnemyRuntimeMarker Marker;
        public readonly LifecycleState Lifecycle=new LifecycleState();
        public sealed class LifecycleState { public bool Alive; public Vector3 LastKnownPosition; public float LastReachableTime,LastHurtTime; }
    }
    public sealed class ZombieModeRunState
    {
        public int RunId=1,LivingNormalZombieCount,PendingNormalZombieSpawns,LivingZombieCount;
        public readonly List<ZombieModeBossInstance> CurrentWaveBossInstances=new List<ZombieModeBossInstance>();
    }
    public static class ZombieModeTuning { public const int MaxNormalZombieCount=20; public const float NormalZombieForceTraceDistance=120; }
    public sealed class SpawnRequest
    {
        public bool Boss,Equipment,Multiplier,Normalize,SkipLoot; public Func<bool> Active; public Action<EnemySpawnContext> Success; public Action Failure;
        public void Complete(CharacterMainControl value) { Success(new EnemySpawnContext { character=value }); }
    }
    public sealed class ModBehaviour
    {
        public readonly Queue<SpawnRequest> Requests=new Queue<SpawnRequest>();
        public static void DevLog(string text) { }
        public void EnsureCharacterPresetsCacheReady() { Probe.Trace.Add("ensure"); }
        public void SanitizeBossRushZombieSpawn(CharacterMainControl character,string context) { Probe.Trace.Add("sanitize"); }
        public void RegisterZombieModeEnemyRecoveryAnchorForRuntimeModule(CharacterMainControl character,Vector3 position) { Probe.Trace.Add("anchor"); }
        public void SpawnEnemyCore(EnemyPresetInfo preset,Vector3 position,bool isBoss,Func<bool> isActiveCheck,Action<EnemySpawnContext> onSpawned,Action onFailed,bool applyEquipment,bool applyBossMultiplier,bool normalizeDamageMultiplier,bool skipBossRushLootTracking=false)
        {
            Probe.Trace.Add(isBoss?"spawn:boss":"spawn:normal");
            Requests.Enqueue(new SpawnRequest { Boss=isBoss,Equipment=applyEquipment,Multiplier=applyBossMultiplier,Normalize=normalizeDamageMultiplier,SkipLoot=skipBossRushLootTracking,Active=isActiveCheck,Success=onSpawned,Failure=onFailed });
        }
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        private const string ZOMBIE_MODE_NORMAL_PRESET_NAME="Cname_Zombie";
        private readonly ModBehaviour owner; private readonly ZombieModeRunState runState;
        internal bool Paused,Invalid,SuppressThreat;
        internal ZombieModeRuntimeModule(ModBehaviour owner,ZombieModeRunState state) { this.owner=owner;runState=state; }
        private bool IsZombieModeRunValid(int runId) { return !Invalid && runState.RunId==runId; }
        private bool IsZombieModeRuntimePaused() { return Paused; }
        private ZombieModeEnemyKind RollZombieModeEnemyKind() { Probe.Trace.Add("roll-kind"); return ZombieModeEnemyKind.Special; }
        private ZombieModeSpecialKind RollZombieModeSpecialKind() { Probe.Trace.Add("roll-special"); return ZombieModeSpecialKind.Test; }
        private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes() { Probe.Trace.Add("roll-affix"); return new List<ZombieModeEliteAffix>(); }
        private ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(int runId,CharacterMainControl value,bool boss=false,ZombieModeBossKind kind=ZombieModeBossKind.Titan,int points=-1,ZombieModeEnemyKind enemy=ZombieModeEnemyKind.Normal,ZombieModeSpecialKind special=ZombieModeSpecialKind.None,List<ZombieModeEliteAffix> affixes=null)
        {
            Probe.Trace.Add("register:"+runState.PendingNormalZombieSpawns+":"+runState.LivingZombieCount);
            var marker=new ZombieModeEnemyRuntimeMarker { gameObject=value.gameObject }; value.gameObject.Components.Add(marker); return marker;
        }
        private void ApplyZombieModeEnemyTuning(CharacterMainControl character,ZombieModeEnemyRuntimeMarker marker) { Probe.Trace.Add("tuning"); }
        private void ApplyZombieModeBossTuning(CharacterMainControl character,ZombieModeBossKind kind,ZombieModeEnemyRuntimeMarker marker) { Probe.Trace.Add("boss-tuning"); }
        private bool ShouldSuppressZombieModeEnemyAggroForSafeZone() { return SuppressThreat; }
        private bool TryMoveZombieModeEnemyOutsideSafeZone(GameObject value,ZombieModeEnemyRuntimeMarker marker,bool suppressed) { Probe.Trace.Add("safe:"+runState.LivingZombieCount); return true; }
        private int GetZombieModeBossPointValue(ZombieModeBossKind kind) { Probe.Trace.Add("boss-points"); return 10; }
        private float GetZombieModeRuntimeNow() { Probe.Trace.Add("clock"); return 12; }
        private void RegisterZombieModeBossRuntime(int runId,CharacterMainControl character,ZombieModeBossKind kind) { Probe.Trace.Add("boss-runtime:"+runState.CurrentWaveBossInstances.Count); }
        private AICharacterController GetZombieModeEnemyAI(GameObject value,ZombieModeEnemyRuntimeMarker marker) { return ((CharacterMainControl)value.Components[1]).AI; }
        private void SetZombieModeEnemyThreatSuppressed(GameObject value,ZombieModeEnemyRuntimeMarker marker,bool suppressed) { Probe.Trace.Add("suppress"); }
        private void SetZombieModeEnemyTargetToMainPlayer(AICharacterController ai) { Probe.Trace.Add("target"); }
    }
}

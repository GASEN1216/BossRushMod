using System;
using System.Collections.Generic;
using BossRush;
using NodeCanvas.Tasks.Actions;

internal static class GameCallbackChecks
{
    internal static void Run()
    {
        Program.Check("original queued AI callbacks reproduce null-agent failures", () => {
            bool searchFailed = false, obstacleFailed = false;
            try { new SearchEnemyAround().Deliver(null, null, false); } catch (NullReferenceException) { searchFailed = true; }
            try { new CheckObsticle().Deliver(true, false); } catch (NullReferenceException) { obstacleFailed = true; }
            Program.Require(searchFailed && obstacleFailed, "original callback failure was not reproduced");
        });
        Program.Check("both AI callbacks discard a missing owner", () => {
            var search = new SearchEnemyAround(); var obstacle = new CheckObsticle();
            search.Deliver(new DamageReceiver(), new InteractablePickup(), true); obstacle.Deliver(true, true);
            Program.Require(search.Callbacks == 0 && obstacle.Callbacks == 0, "ownerless callback reached the original dereference");
        });
        Program.Check("Unity destroyed-object equality is checked before accessing gameObject", () => {
            var owner = new AICharacterController { Destroyed = true };
            var search = new SearchEnemyAround { agent = owner }; var obstacle = new CheckObsticle { agent = owner };
            search.Deliver(null, null, true); obstacle.Deliver(true, true);
            Program.Require(search.Callbacks == 0 && obstacle.Callbacks == 0, "destroyed component was dereferenced");
        });
        Program.Check("live search preserves target and pickup result and completes the action", () => {
            var search = new SearchEnemyAround { agent = new AICharacterController(), isRunning = true };
            var target = new DamageReceiver(); var pickup = new InteractablePickup();
            search.Deliver(target, pickup, true);
            Program.Require(ReferenceEquals(search.result.value, target) && ReferenceEquals(search.pickupResult.value, pickup)
                && search.Ends == 1 && !search.Waiting, "live search result changed");
        });
        Program.Check("live obstacle check applies both positive and negative results", () => {
            var obstacle = new CheckObsticle { agent = new AICharacterController(), isRunning = true };
            obstacle.Deliver(true, true);
            Program.Require(obstacle.agent.hasObsticleToTarget && obstacle.Ends == 1, "positive result was lost");
            obstacle.Deliver(false, true);
            Program.Require(!obstacle.agent.hasObsticleToTarget && obstacle.Ends == 2 && !obstacle.Waiting,
                "negative result was lost");
        });
        Program.Check("original dynamically added interaction reproduces the missing-group failure", () => {
            bool failed = false;
            try { new InteractableBase().Initialize(false); } catch (NullReferenceException) { failed = true; }
            Program.Require(failed, "original null-list foreach was not reached");
        });
        Program.Check("missing interaction group is initialized while official Awake still runs", () => {
            var interaction = new InteractableBase(); interaction.Initialize(true);
            Program.Require(interaction.Group != null && interaction.Group.Count == 0 && interaction.Key == 42
                && interaction.Collider != null && !interaction.Collider.enabled && interaction.ListInitialized,
                "the null group was not repaired or official initialization was skipped");
        });
        Program.Check("existing interaction groups retain their identity and official child setup", () => {
            var child = new InteractableBase(); var group = new List<InteractableBase> { child };
            var interaction = new InteractableBase { Group = group };
            interaction.Initialize(true); interaction.Initialize(true);
            Program.Require(ReferenceEquals(interaction.Group, group) && group.Count == 1 && !child.MarkerActive
                && ReferenceEquals(child.transform.position, interaction.transform.position),
                "existing child interactions were cleared or original positioning was skipped");
        });
        Program.Check("original smoke continuation cannot start a coroutine after scene destruction", () => {
            bool failed = false;
            try { WaitAtEndOfFrame(new FowSmoke { Destroyed = true }, false); } catch (NullReferenceException) { failed = true; }
            Program.Require(failed, "destroyed coroutine owner failure was not reproduced");
        });
        Program.Check("destroyed smoke ends the pending frame wait as cancellation", () => {
            var result = WaitAtEndOfFrame(new FowSmoke { Destroyed = true }, true);
            Program.Require(result.Cancelled && !result.NativeWait, "destroyed smoke reached coroutine scheduling");
        });
        Program.Check("live smoke retains the official frame timing", () => {
            var result = WaitAtEndOfFrame(new FowSmoke(), true);
            Program.Require(!result.Cancelled && result.NativeWait, "live smoke frame wait was changed");
        });
        Program.Check("smoke cancellation does not hide other owners' invalid coroutine requests", () => {
            bool failed = false;
            try { WaitAtEndOfFrame(new UnityEngine.MonoBehaviour { Destroyed = true }, true); }
            catch (NullReferenceException) { failed = true; }
            Program.Require(failed, "the smoke patch changed an unrelated owner");
        });
        Program.Check("existing chapel is restored without requiring the historic affinity marker", () => {
            AffinityManager.Unlocked = false;
            var host = new ModBehaviour { ChapelPlaced = true }; host.InitWeddingBuilding();
            Program.Require(host.Prefabs == 1 && host.Injections == 1 && host.Repaints == 1,
                "saved chapel cannot be rendered");
        });
        Program.Check("early scene entry also restores a placed chapel before the normal repaint", () => {
            AffinityManager.Unlocked = false;
            var host = new ModBehaviour { ChapelPlaced = true }; host.Early(); host.InitWeddingBuilding();
            Program.Require(host.Prefabs == 1 && host.Events == 1 && host.Repaints == 1,
                "early restore was gated or repeated initialization duplicated resources");
        });
        Program.Check("unlocked empty slot registers chapel while locked empty slot still cannot build", () => {
            AffinityManager.Unlocked = false;
            var locked = new ModBehaviour(); locked.Early(); locked.InitWeddingBuilding();
            AffinityManager.Unlocked = true;
            var unlocked = new ModBehaviour(); unlocked.InitWeddingBuilding();
            Program.Require(locked.Prefabs == 0 && unlocked.Injections == 1 && unlocked.Repaints == 0,
                "normal unlock rules changed");
        });
    }

    private static Cysharp.Threading.Tasks.UniTask WaitAtEndOfFrame(UnityEngine.MonoBehaviour owner, bool patched)
    {
        var result = default(Cysharp.Threading.Tasks.UniTask);
        if (patched && !FowSmokeDestroyedRunnerPatch.Prefix(owner, ref result)) return result;
        if (owner == null) throw new NullReferenceException("StartCoroutine on destroyed native owner");
        return new Cysharp.Threading.Tasks.UniTask { NativeWait = true };
    }
}

// Only the Unity boundary is substituted. Production prefixes and the decompiled
// official callback/Awake methods are compiled unchanged by run.py.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute { internal HarmonyPatch(Type type, string method, Type[] args) { } }
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class HarmonyPrefix : Attribute { }
}

namespace UnityEngine
{
    public class Component
    {
        public bool Destroyed;
        private readonly GameObject target = new GameObject();
        public readonly Transform transform = new Transform();
        public GameObject gameObject { get { if (Destroyed) throw new InvalidOperationException("destroyed native component"); return target; } }
        public T GetComponent<T>() where T : class { return null; }
        public static bool operator ==(Component left, Component right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Component left, Component right) { return !(left == right); }
        public static implicit operator bool(Component value) { return value != null; }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class GameObject { public int layer; public T AddComponent<T>() where T : new() { return new T(); } }
    public class Transform { public object position = new object(), rotation = new object(); }
    public class Collider : Component { public bool enabled = true; }
    public class MonoBehaviour : Component { }
    public sealed class BoxCollider : Collider { }
    public static class LayerMask { public static int NameToLayer(string name) { return 19; } }
    public static class Time { public static float time; }
}

namespace Cysharp.Threading.Tasks
{
    public struct UniTask
    {
        public bool Cancelled, NativeWait;
        public static UniTask FromCanceled(System.Threading.CancellationToken token)
        { return new UniTask { Cancelled = token.IsCancellationRequested }; }
    }
}
public class FowSmoke : UnityEngine.MonoBehaviour { }

public class AICharacterController : UnityEngine.Component { public bool hasObsticleToTarget; }
public class DamageReceiver { }
public class InteractablePickup { }
public class Parameter<T> { public T value; }
public class FakeActionTask
{
    public AICharacterController agent;
    public bool isRunning;
    public int Ends;
    protected void EndAction(bool result) { Ends++; }
}
namespace NodeCanvas.Tasks.Actions
{
    public partial class SearchEnemyAround : FakeActionTask
    {
        public Parameter<DamageReceiver> result = new Parameter<DamageReceiver>();
        public Parameter<InteractablePickup> pickupResult = new Parameter<InteractablePickup>();
        public bool setNullIfNotFound, alwaysSuccess;
        private float searchStartTimeMarker;
        private bool waitingSearchResult = true;
        public int Callbacks;
        public bool Waiting { get { return waitingSearchResult; } }
        public void Deliver(DamageReceiver target, InteractablePickup pickup, bool patched)
        { if (!patched || StaleAISearchCallbackPatch.Prefix(this)) { Callbacks++; OnSearchFinished(target, pickup); } }
    }
    public partial class CheckObsticle : FakeActionTask
    {
        public bool alwaysSuccess;
        private bool waitingResult = true;
        public int Callbacks;
        public bool Waiting { get { return waitingResult; } }
        public void Deliver(bool result, bool patched)
        { if (!patched || StaleAIObstacleCallbackPatch.Prefix(this)) { Callbacks++; OnCheckFinished(result); } }
    }
}
public partial class InteractableBase : UnityEngine.Component
{
    private List<InteractableBase> otherInterablesInGroup, _interactbleList;
    private int requireItemDataKeyCached;
    private UnityEngine.Collider interactCollider;
    public bool MarkerActive = true;
    public object interactMarkerOffset;
    public List<InteractableBase> Group { get { return otherInterablesInGroup; } set { otherInterablesInGroup = value; } }
    public int Key { get { return requireItemDataKeyCached; } }
    public bool ListInitialized { get { return _interactbleList != null; } }
    public UnityEngine.Collider Collider { get { return interactCollider; } }
    private int GetKey() { return 42; }
    public void Initialize(bool patched)
    { if (patched) InteractableAwakeGroupInitializationPatch.Prefix(ref otherInterablesInGroup); Awake(); }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; public bool IsValid() { return true; } }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "Base" }; } }
}
namespace BossRush
{
    internal static class AffinityManager { internal static bool Unlocked; internal static bool HasAnyNPCEverReachedMaxLevel() { return Unlocked; } }
    internal static class LocalizationInjector { internal static void InjectWeddingBuildingLocalization() { } }
    internal class FakeBuildingDataCollection { public static object Instance { get { return typeof(FakeBuildingDataCollection); } } }
    internal partial class ModBehaviour
    {
        private bool weddingBuildingInjected;
        private const int WEDDING_BUILDING_REQUIRED_AFFINITY_LEVEL = 10;
        internal bool ChapelPlaced;
        internal int Prefabs, Injections, Repaints, Events;
        internal void Early() { TryInitializeWeddingBuildingEarly(); }
        private bool RefreshWeddingBuildingPresence() { return ChapelPlaced; }
        private void LoadWeddingBuildingIcon() { }
        private void LoadWeddingBuildingModel() { }
        private void CreateWeddingBuildingPrefab() { Prefabs++; }
        private void InjectWeddingBuildingData() { Injections++; }
        private void RegisterWeddingBuildingEvents() { Events++; }
        private void RequestBaseBuildingAreaRepaint(string source) { Repaints++; }
        private static bool IsBaseHubSceneName(string scene) { return scene == "Base"; }
        private static Type FindGameType(string name) { return typeof(FakeBuildingDataCollection); }
        internal static void DevLog(string text) { }
        internal static void LogError(string text) { throw new Exception(text); }
    }
}

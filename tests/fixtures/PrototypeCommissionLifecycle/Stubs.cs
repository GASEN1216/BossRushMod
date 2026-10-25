using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object child in go.Components) child.Destroyed = true;
        }
        public static T[] FindObjectsOfType<T>() { return new T[0]; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public sealed class GameObject : Object
    {
        public bool activeInHierarchy = true;
        internal readonly List<Object> Components = new List<Object>();
    }
    public class Component : Object
    {
        public readonly GameObject gameObject = new GameObject();
        public readonly Transform transform = new Transform();
        public Component() { gameObject.Components.Add(this); }
        public T GetComponent<T>() where T : class { return null; }
    }
    public sealed class Transform : Object { }
    public struct Vector3 { public Vector3(float x, float y, float z) { } }
    public static class Time { public static float unscaledTime; }
}
namespace Saves { public static class SavesSystem { public static int CurrentSlot; } }
public sealed class Health { public bool IsDead; }
public sealed class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public Health Health;
}
public sealed class LevelManager : Component
{
    public static LevelManager Instance;
    public static bool AfterInit;
    public bool IsBaseLevel;
}
public sealed class SceneLoadingContext { }
public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static int Subscribers { get { return onStartedLoadingScene == null ? 0 : onStartedLoadingScene.GetInvocationList().Length; } }
    public static void StartLoading()
    {
        IsSceneLoading = true;
        if (onStartedLoadingScene != null) onStartedLoadingScene(new SceneLoadingContext());
    }
}
public class InteractableBase : Component { }

namespace BossRush
{
    internal class SceneRuntimeContext { }
    internal class ModBehaviour { public static readonly List<string> Errors = new List<string>(); public static void DevLog(string value) { Errors.Add(value); } }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour host) { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float dt, float unscaled) { }
        public virtual void OnDestroy() { }
    }
    internal sealed class GoblinInteractable : InteractableBase
    {
        internal readonly List<InteractableBase> Group = new List<InteractableBase>();
        internal readonly GoblinNPCController Controller = new GoblinNPCController();
        internal GoblinInteractable() { gameObject.Components.Add(Controller); }
        public new T GetComponent<T>() where T : class { return Controller as T; }
    }
    internal sealed class GoblinNPCController : Component
    {
        public int Starts, Ends;
        public void StartDialogue() { Starts++; }
        public void EndDialogueWithStay(float value) { Ends++; }
    }
    internal sealed class DangerousPrototypeCommissionInteractable : InteractableBase
    {
        internal DangerousPrototypeCommission Owner;
        internal bool GroupMember;
    }
    internal static class GoblinAffinityConfig { internal const string NPC_ID = "goblin"; }
    internal static class AffinityManager { internal static int Level = 10; internal static int GetLevel(string id) { return Level; } }
    internal static class NPCInteractionGroupHelper
    {
        internal static List<InteractableBase> GetOrCreateGroupList(GoblinInteractable host, string label) { return host.Group; }
        internal static T AddSubInteractable<T>(Transform parent, string name, List<InteractableBase> group, Action<T> setup) where T : InteractableBase, new()
        { var value = new T(); setup(value); group.Add(value); return value; }
    }
    internal static class DialogueActorFactory
    {
        internal static object CreateBilingual(GameObject go, string id, string cn, string en, Vector3 position) { return go; }
    }
    internal static class FieldAdventureSupport { internal static void Notify(string cn, string en) { } }
    internal static class DialogueManager
    {
        internal sealed class Pending
        {
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();
            internal CancellationToken Token;
        }
        internal static readonly List<Pending> Requests = new List<Pending>();
        internal static Task ShowDialogueSequenceBilingual(object actor, string[][] story, string id, CancellationToken token)
        {
            // Deliberately deliver a late successful callback even after cancellation.
            var request = new Pending { Token = token };
            Requests.Add(request);
            return request.Completion.Task;
        }
    }
    internal sealed class DangerousPrototypeCommissionPersistence : IDisposable
    {
        internal static readonly List<int> WrittenSlots = new List<int>();
        private readonly Action slotChanged;
        internal bool HasWriteBarrier;
        internal bool Writable = true;
        internal DangerousPrototypeCommissionPersistence(Action changed, Func<bool> collect, Func<bool> flush) { slotChanged = changed; }
        internal DangerousPrototypeCommissionData Load() { return new DangerousPrototypeCommissionData(); }
        internal bool Store(DangerousPrototypeCommissionData value) { WrittenSlots.Add(Saves.SavesSystem.CurrentSlot); return true; }
        internal void Tick() { }
        internal void ChangeSlot() { slotChanged(); }
        public void Dispose() { }
    }
}

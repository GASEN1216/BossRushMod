using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle; public string name;
        public static bool operator ==(Scene a, Scene b) { return a.handle == b.handle; }
        public static bool operator !=(Scene a, Scene b) { return !(a == b); }
        public override bool Equals(object o) { return o is Scene && this == (Scene)o; }
        public override int GetHashCode() { return handle; }
    }
    public static class SceneManager
    {
        public static Scene Current;
        public static Scene GetActiveScene() { return Current; }
    }
}
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        internal virtual bool Gone { get { return Destroyed; } }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Gone, bn = ReferenceEquals(b, null) || b.Gone;
            return an || bn ? an && bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
            foreach (Component part in go.Parts) part.Destroyed = true;
            go.Destroyed = true;
            go.Lifetime.Cancel();
        }
    }
    public class GameObject : Object
    {
        public string name;
        public SceneManagement.Scene scene;
        public Transform transform;
        internal List<Component> Parts = new List<Component>();
        internal System.Threading.CancellationTokenSource Lifetime = new System.Threading.CancellationTokenSource();
        public System.Threading.CancellationToken GetCancellationTokenOnDestroy() { return Lifetime.Token; }
        public GameObject(string name)
        {
            this.name = name; scene = SceneManagement.SceneManager.Current;
            transform = AddComponent<Transform>();
        }
        public T AddComponent<T>() where T : Component, new()
        { var part = new T { gameObject = this }; Parts.Add(part); return part; }
        public T GetComponent<T>()
        { foreach (var part in Parts) if (part is T && part != null) return (T)(object)part; return default(T); }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component
        {
            T own = GetComponent<T>(); if (own != null) return own;
            foreach (var child in transform.Children) { T found = child.gameObject.GetComponentInChildren<T>(includeInactive); if (found != null) return found; }
            return null;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public string name { get { return gameObject.name; } }
        internal override bool Gone { get { return Destroyed || gameObject == null; } }
        public T GetComponent<T>() { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component { return gameObject.GetComponentInChildren<T>(includeInactive); }
    }
    public class Transform : Component
    {
        internal List<Transform> Children = new List<Transform>();
        internal Transform Parent;
        internal void SetParent(Transform parent) { Parent = parent; parent.Children.Add(this); }
        public bool IsChildOf(Transform parent) { return Parent != null && (Parent == parent || Parent.IsChildOf(parent)); }
        public Transform Find(string name) { foreach(var child in Children) if(child != null && child.name == name) return child; return null; }
    }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; }
}
public static class SceneLoader { public static bool IsSceneLoading; }
namespace Duckov.Utilities { }
namespace BossRush.Utils
{
    public static class NPCInteractionGroupHelper
    {
        public static List<InteractableBase> GetOrCreateGroupList(InteractableBase owner, string label) { return owner.Group; }
        public static List<InteractableBase> PrepareGroupedInteractionOwner(InteractableBase owner, string label) { return owner.Group; }
        public static T AddSubInteractable<T>(Transform parent, string name, List<InteractableBase> group, Action<T> setup) where T : InteractableBase, new()
        {
            var go = new GameObject(name); go.transform.SetParent(parent);
            T part = go.AddComponent<T>(); setup(part); group.Add(part); return part;
        }
    }
}
namespace BossRush
{
    public class InteractableBase : MonoBehaviour { public readonly List<InteractableBase> Group = new List<InteractableBase>(); }
    public abstract class BossRushBuildingInteractableBase : InteractableBase
    {
        protected abstract string InteractNameKey { get; }
        protected abstract string LogPrefix { get; }
        protected abstract string InteractionGroupLabel { get; }
        protected abstract bool IsBuildingInteractable();
        protected abstract void OnInteractCompleted();
        protected virtual void OnDestroy() { }
        internal void Click() { OnInteractCompleted(); }
        internal bool Available { get { return IsBuildingInteractable(); } }
    }
    public sealed class Health { public bool IsDead; }
    public class CharacterMainControl : MonoBehaviour { public static CharacterMainControl Main; public Health Health; }
    public sealed class PermanentDuckNpcInteractable : InteractableBase { }
    public sealed class SkyIslandSearchPoint : InteractableBase { }
    public sealed class DuckNpcRuntimeMarker : Component { public void FacePlayer() {} public void StartDialogue() {} public void EndDialogueWithStay(float seconds) {} }
    internal static class L10n { internal static bool IsChinese = true; internal static string T(string cn, string en) { return IsChinese ? cn : en; } }
    internal static class LocalizationHelper
    {
        internal static readonly Dictionary<string,string> Text = new Dictionary<string,string>();
        internal static void InjectLocalization(string key, string value) { Text[key] = value; }
    }
    internal static class AffinityManager
    {
        internal static string Spouse; internal static bool Following;
        internal static bool IsMarriedToPlayer(string id) { return id == Spouse; }
        internal static bool IsSpouseFollowingPlayer(string id) { return Spouse == id && Following; }
        internal static string GetCurrentSpouseNpcId() { return Spouse; }
        internal static bool MarkMarriedToPlayer(string id, string date)
        { if (Spouse != null) return false; Spouse = id; Following = false; return true; }
        internal static bool DivorceFromPlayer(string id, bool resetAffinityToZero)
        { if (Spouse != id) return false; Spouse = null; Following = false; return true; }
        internal static NpcConfig GetNPCConfig(string id) { return new NpcConfig { DisplayName = SkyIslandWorldStory.ResidentName(id) }; }
    }
    internal static class SceneRuntimeGate { internal static bool IsBaseHubSceneName(string name) { return name == "Base"; } }
    internal static class PermanentDuckNpcRegistry
    {
        internal static readonly Dictionary<string,CharacterMainControl> Instances = new Dictionary<string,CharacterMainControl>();
        internal static bool IsPermanentDuckNpc(string id) { return id == "sky_qinghe" || id == "sky_weibai"; }
        internal static CharacterMainControl GetInstance(string id) { CharacterMainControl npc; return Instances.TryGetValue(id, out npc) ? npc : null; }
        internal static void UnregisterInstance(string id) { Instances.Remove(id); }
    }
    internal static class DuckNpcSpawner { internal static void Despawn(CharacterMainControl npc) { UnityEngine.Object.Destroy(npc.gameObject); } }
    internal sealed class SkyIslandResidents
    {
        internal static string[] AllIds { get { return new[] { "sky_qinghe", "sky_weibai" }; } }
        internal bool SpawnFinished;
        internal readonly Dictionary<string,InteractableBase> Owners = new Dictionary<string,InteractableBase>();
        internal InteractableBase FindQuestInteractionOwner(string id) { InteractableBase owner; return Owners.TryGetValue(id, out owner) ? owner : null; }
    }
    internal sealed class SkyIslandWorldStory
    {
        internal int Talks; internal string LastId;
        internal static string ResidentName(string id)
        { return id == "sky_qinghe" ? L10n.T("晴禾", "Qinghe") : id == "sky_weibai" ? L10n.T("苇白", "Weibai") : id; }
        internal void Talk(string id, Transform speaker) { Talks++; LastId = id; }
    }
    internal sealed class SkyIslandStoryService
    {
        internal bool IsCurrentSlot;
        internal string DescribeNpc(string id, bool married, bool onIsland) { return id + ":" + married + ":" + onIsland; }
    }
    internal static class SkyIslandOfficialQuestStory
    {
        internal static SkyIslandStoryService Source; internal static bool OnIsland;
        internal static SkyIslandStoryService Resolve(ModBehaviour host, out bool onIsland) { onIsland = OnIsland; return Source; }
    }
    internal sealed class SkyIslandResidentDialogue
    {
        internal static SkyIslandResidentDialogue Last;
        internal bool Active, Business; internal Func<bool> Valid; internal string Body;
        internal static SkyIslandResidentDialogue Run(string id, Transform speaker, string body, Action openPanel, Func<bool> valid, Func<bool> hasBusiness)
        {
            if (!valid()) return null;
            Last = new SkyIslandResidentDialogue { Active = true, Valid = valid, Business = hasBusiness(), Body = body }; return Last;
        }
        internal bool CanContinue() { return Active && Valid(); }
        internal void Dispose() { Active = false; }
    }
    internal sealed partial class SkyIslandSession : MonoBehaviour
    {
        internal SkyIslandResidents residents; internal GameObject root; internal CharacterMainControl player;
        internal SkyIslandWorldStory worldStory; internal SkyIslandStoryService story; internal bool Valid;
        private bool IsSessionValid() { return Valid; }
    }
    internal sealed class QuestGiver : InteractableBase { internal int ID; }
    internal sealed class SkyIslandOfficialQuestDefinition { internal int GiverId; }
    internal static class SkyIslandOfficialQuestTable
    {
        internal static IList<SkyIslandOfficialQuestDefinition> Island;
        internal static int GiverIdOfResident(string id) { return id == "sky_weibai" ? 5901 : 0; }
        internal static string ResidentOfGiver(int id) { return id == 5901 ? "sky_weibai" : "resident_" + id; }
        internal static string FallbackMarkerOfGiver(int id) { return id == 5901 ? "Search_B" : "device_" + id; }
    }
    internal static partial class SkyIslandOfficialQuestGivers
    {
        internal static bool UiReady;
        private static bool Attach(Transform parent, List<InteractableBase> group, int id)
        {
            if (parent == null || !UiReady) return false;
            if (parent.Find("IslandQuestGiver") != null) return true;
            var go = new GameObject("IslandQuestGiver"); go.transform.SetParent(parent);
            QuestGiver giver = go.AddComponent<QuestGiver>(); giver.ID = id; attached.Add(giver);
            if (group != null) group.Add(giver);
            return true;
        }
        internal static void Initially(InteractableBase owner, int id) { Attach(owner.transform, null, id); }
        internal static void Reset() { attached.Clear(); ClearSessionState(); }
        internal static int DoneCount { get { return fallbackDone.Count; } }
        internal static int Attempts { get { return fallbackAttempts; } }
    }
    internal static class GoblinAffinityConfig { public const string NPC_ID = "goblin"; }
    internal static class NurseAffinityConfig { public const string NPC_ID = "nurse"; }
    public partial class ModBehaviour : MonoBehaviour
    {
        public static ModBehaviour Instance;
        internal GameObject goblinNPCInstance, nurseNPCInstance;
        internal Transform WeddingTarget;
        internal int WeddingAttempts;
        public Transform TrySpawnMarriedNpcAtWeddingPoint() { WeddingAttempts++; return WeddingTarget; }
        internal int Invalidations, PlaceholderRemovals, GoblinRemovals, NurseRemovals;
        private void InvalidatePermanentSpouseRestore() { Invalidations++; }
        private void DestroyWeddingPlaceholder() { PlaceholderRemovals++; }
        public void DestroyGoblinNPC() { GoblinRemovals++; UnityEngine.Object.Destroy(goblinNPCInstance); goblinNPCInstance = null; }
        private void SpawnGoblinNPC(object p, bool a, bool b) { }
        public void DestroyNurseNPC() { NurseRemovals++; UnityEngine.Object.Destroy(nurseNPCInstance); nurseNPCInstance = null; }
        private void SpawnNurseNPC(object p, bool a, bool b) { }
        public static void DevLog(string message) { }
    }
}

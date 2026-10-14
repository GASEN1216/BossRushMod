using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed;
            bool bn = ReferenceEquals(b, null) || b.destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(GameObject value) { value.destroyed = true; foreach (var component in value.Components) component.destroyed = true; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Component() { gameObject = new GameObject(); gameObject.Components.Add(this); }
        public Component GetComponent(Type type) { return gameObject.GetComponent(type); }
        public T GetComponentInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponent(typeof(T)) as T; }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public UnityEngine.SceneManagement.Scene scene;
        public Component GetComponent(Type type) { return Components.Find(value => type.IsInstanceOfType(value)); }
        public T GetComponent<T>() where T : Component { return GetComponent(typeof(T)) as T; }
        public Component AddComponent(Type type) { var value = (Component)Activator.CreateInstance(type); value.gameObject = this; Components.Add(value); return value; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public bool isLoaded;
        public bool IsValid() { return handle != 0; }
    }
    public static class SceneManager
    {
        public static event Action<Scene> sceneUnloaded;
        public static void Unload(Scene scene) { if (sceneUnloaded != null) sceneUnloaded(scene); }
    }
}
namespace Duckov.Scenes
{
    public class MultiSceneCore : UnityEngine.Component { public static MultiSceneCore Instance; }
}
namespace Saves
{
    public static class SavesSystem
    {
        public static event Action OnSetFile, OnSaveDeleted;
        public static void Select() { if (OnSetFile != null) OnSetFile(); }
        public static void Delete() { if (OnSaveDeleted != null) OnSaveDeleted(); }
    }
}
public class CharacterMainControl : UnityEngine.Component { public static CharacterMainControl Main; public bool Dead; }
public class AICharacterController : UnityEngine.Component { public CharacterMainControl CharacterMainControl; }
public class HealthSimpleBase : UnityEngine.Component { }
public class InteractableLootbox : UnityEngine.Component { public object Inventory; }
namespace ItemStatsSystem { public class Item { } }
namespace BossRush
{
    internal static class ModBehaviour
    {
        public static void CriticalLog(string key, string text) { throw new Exception(key + ": " + text); }
        public static void DevLog(string text) { }
    }
    internal static class BossRushCoop
    {
        public static bool IsConnected;
        public static string RunId, LocalAssetId;
        public static int SceneGeneration;
        public static event Action SessionChanged;
        public static void ChangeSession() { if (SessionChanged != null) SessionChanged(); }
        public static void RaiseItemRequested(ItemStatsSystem.Item item, Dictionary<string, string> data) { }
        public static void RaiseItemApplied(ItemStatsSystem.Item item, IReadOnlyDictionary<string, string> data) { }
    }
}
namespace EscapeFromDuckovCoopMod
{
    public class Writer { public void Put(byte[] value) { } }
    public class Message { public ArraySegment<byte> Payload; public object Sender; }
    public class NetService
    {
        public static NetService Instance;
        public bool NetworkStarted, IsServer;
        public object connectedPeer, netManager;
        public string GetPlayerId(object peer) { return "peer"; }
        public string GetSelfNetworkId() { return "local"; }
    }
    public static class ModNetworkApi
    {
        private class Subscription : IDisposable { public void Dispose() { } }
        public static IDisposable RegisterHandler(string channel, Action<Message> handler) { return new Subscription(); }
        public static bool SendToServer(string channel, Action<Writer> writer) { return false; }
        public static bool SendToClient(object peer, string channel, Action<Writer> writer, bool reliable) { return false; }
        public static void Broadcast(string channel, Action<Writer> writer, bool reliable) { }
        public static event Action<object> PeerDisconnected;
    }
    public static class ModApiEvents
    {
        public static event Action<CharacterMainControl, string, bool> PlayerSpawned;
        public static event Action<int, CharacterMainControl> AiSpawned;
        public static event Action<object> ServerRegisterAI;
        public static event Action<ItemStatsSystem.Item, Dictionary<string, string>> ItemSnapshotCustomDataRequested;
        public static event Action<ItemStatsSystem.Item, IReadOnlyDictionary<string, string>> ItemSnapshotCustomDataApplied;
    }
    // Only the world/UI boundary is simulated. Native authorization is copied verbatim
    // from the installed 1.3.6 source, and independently executed from its actual DLL below.
    public class SceneNet
    {
        public static SceneNet Instance;
        public string sceneTargetId;
        public bool allowLocalSceneLoad;
        private string _allowedLocalSceneLoadId;
        private float _allowedLocalSceneLoadUntil;
        private bool _localSceneLoadLaunched;
        public Action Continue;
        public int Loads, Votes;
        public bool FailLaunch;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AuthorizeLocalSceneLoad(string sceneId, float seconds = 10f)
        {
            allowLocalSceneLoad = true;
            _allowedLocalSceneLoadId = sceneId ?? string.Empty;
            _allowedLocalSceneLoadUntil = TestClock.Now + Math.Max(0.5f, seconds);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool IsLocalSceneLoadAllowed(string sceneId = null)
        {
            if (allowLocalSceneLoad) return true;
            if (TestClock.Now > _allowedLocalSceneLoadUntil) return false;
            if (string.IsNullOrEmpty(sceneId) || string.IsNullOrEmpty(_allowedLocalSceneLoadId)) return true;
            return string.Equals(_allowedLocalSceneLoadId, sceneId, StringComparison.OrdinalIgnoreCase);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ClearLocalSceneLoadAuthorization() { allowLocalSceneLoad = false; _allowedLocalSceneLoadId = null; _allowedLocalSceneLoadUntil = 0f; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void TryPerformSceneLoad_Local(string targetSceneId, string curtainGuid, bool notifyEvac, bool save, bool useLocation, string locationName)
        {
            try
            {
                AuthorizeLocalSceneLoad(targetSceneId);
                _localSceneLoadLaunched = !FailLaunch;
                if (!FailLaunch) Continue = () => { if (IsLocalSceneLoadAllowed(targetSceneId)) Loads++; else Votes++; };
            }
            finally { allowLocalSceneLoad = false; }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Approve(string targetSceneId) { sceneTargetId = targetSceneId; TryPerformSceneLoad_Local(targetSceneId, null, false, true, false, null); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Host_BeginSceneVote_Simple(string target, string curtain, bool notifyEvac, bool save, bool useLocation, string locationName) { sceneTargetId = target; Votes++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Client_RequestBeginSceneVote(string target, string curtain, bool notifyEvac, bool save, bool useLocation, string locationName) { Votes++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Client_OnSceneVoteStart(object message) { Votes++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void CancelVote() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Client_OnVoteCancelled() { }
        public void Clear() { ClearLocalSceneLoadAuthorization(); }
    }
}
public static class TestClock { public static float Now; public static float Read() { return Now; } }

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; }
    public struct Quaternion { public float x, y, z, w; }
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
        public static implicit operator bool(Object value) { return value != null; }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(GameObject value) { value.destroyed = true; foreach (var component in value.Components) component.destroyed = true; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public Component() { gameObject = new GameObject(); gameObject.Components.Add(this); }
        public Component GetComponent(Type type) { return gameObject.GetComponent(type); }
        public T GetComponentInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponent(typeof(T)) as T; }
    }
    public class GameObject : Object
    {
        public readonly Transform transform = new Transform();
        internal readonly List<Component> Components = new List<Component>();
        public UnityEngine.SceneManagement.Scene scene;
        public Component GetComponent(Type type) { return Components.Find(value => type.IsInstanceOfType(value)); }
        public T GetComponent<T>() where T : Component { return GetComponent(typeof(T)) as T; }
        public Component AddComponent(Type type) { var value = (Component)Activator.CreateInstance(type); value.gameObject = this; Components.Add(value); return value; }
    }
    public class Transform { public Vector3 position { get; set; } public Quaternion rotation { get; set; } }
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
    public class MultiSceneCore : UnityEngine.Component { public static MultiSceneCore Instance; public SceneInfoEntry SceneInfo; public bool IsLoading; }
}
public class SceneReference { public string Name; }
public class SceneInfoEntry { public string ID; public SceneReference SceneReference; }
public static class SceneInfoCollection
{
    public static readonly Dictionary<string, SceneInfoEntry> Entries = new Dictionary<string, SceneInfoEntry>();
    public static SceneInfoEntry GetSceneInfo(string id) { SceneInfoEntry value; return Entries.TryGetValue(id, out value) ? value : null; }
}
public struct SceneLoadingContext { public string sceneName; }
public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static int ListenerCount { get { return onStartedLoadingScene == null ? 0 : onStartedLoadingScene.GetInvocationList().Length; } }
    public static void StartActual(string name) { IsSceneLoading = true; if (onStartedLoadingScene != null) onStartedLoadingScene(new SceneLoadingContext { sceneName = name }); }
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
public class CharacterMainControl : UnityEngine.Component { public static CharacterMainControl Main { get; set; } public bool Dead; public UnityEngine.Component modelRoot = new UnityEngine.Component(); }
public class AICharacterController : UnityEngine.Component { public CharacterMainControl CharacterMainControl; }
public class HealthSimpleBase : UnityEngine.Component { }
public class InteractableLootbox : UnityEngine.Component { public object Inventory; }
public class MapSelectionEntry : UnityEngine.Object { public int BeaconIndex { get; set; } }
public class LevelManager : UnityEngine.Component { public static LevelManager Instance { get; set; } public static int loadLevelBeaconIndex; public CharacterMainControl MainCharacter { get; set; } }
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
        public static bool IsClient { get { return IsConnected && EscapeFromDuckovCoopMod.NetService.Instance != null && !EscapeFromDuckovCoopMod.NetService.Instance.IsServer; } }
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
    public struct ChatMessageRpc { public string SenderId, SenderName, Content; }
    public class Writer { public byte[] Bytes; public void Put(byte[] value) { Bytes = (byte[])value.Clone(); } }
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
        public static readonly List<Action<Writer>> Writers = new List<Action<Writer>>();
        public static readonly List<byte[]> Payloads = new List<byte[]>();
        public static Action BeforeWrite;
        public static bool SendAccepted, ThrowAfterWrite;
        private static void Build(string channel, Action<Writer> writer)
        {
            Writers.Add(writer);
            Action nested = BeforeWrite; BeforeWrite = null; if (nested != null) nested();
            var target = new Writer(); writer(target); Payloads.Add(target.Bytes);
            if (ThrowAfterWrite) throw new InvalidOperationException("injected send failure");
        }
        public static bool SendToServer(string channel, Action<Writer> writer) { Build(channel, writer); return SendAccepted; }
        public static bool SendToClient(object peer, string channel, Action<Writer> writer, bool reliable) { Build(channel, writer); return SendAccepted; }
        public static void Broadcast(string channel, Action<Writer> writer, bool reliable) { Build(channel, writer); }
        public static event Action<object> PeerDisconnected;
        public static int Queued;
        public static void Receive(NetService service, object sender, string channel, byte[] bytes) { Queued++; }
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
    public class SceneNet : UnityEngine.Object
    {
        public static SceneNet Instance;
        public string sceneTargetId;
        public bool allowLocalSceneLoad;
        public bool sceneVoteActive, localReady;
        public string sceneCurtainGuid, sceneLocationName;
        public bool sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, IsMapSelectionEntry, IsDoteleportMap;
        public bool _cliForceSpawnAtHost, _cliHostReadyReceived, _cliPendingPostLoadSnap;
        public UnityEngine.Vector3 _cliHostSpawnPosition;
        public UnityEngine.Quaternion _cliHostSpawnRotation;
        public readonly List<string> sceneParticipantIds = new List<string>();
        public readonly Dictionary<string, bool> sceneReady = new Dictionary<string, bool>();
        private string _allowedLocalSceneLoadId;
        private float _allowedLocalSceneLoadUntil;
        private bool _localSceneLoadLaunched;
        public Action Continue;
        public int Loads, Votes, BeginCalls;
        public bool FailLaunch;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Server_BroadcastBeginSceneLoad() { var value = new SceneBeginLoadRpc { SceneId = sceneTargetId }; CoopTool.SendRpc(in value, null); }
        public void BroadcastBegin() { Server_BroadcastBeginSceneLoad(); }
        private void Server_ScheduleSceneLoad(float seconds) { NativeScheduled++; }
        public int NativeScheduled;
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
        public void Host_BeginSceneVote_Simple(string target, string curtain, bool notifyEvac, bool save, bool useLocation, string locationName) { if (sceneVoteActive) return; sceneTargetId = target; sceneVoteActive = true; Votes++; if (NestedVote != null) NestedVote(); }
        public Action NestedVote;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Client_RequestBeginSceneVote(string target, string curtain, bool notifyEvac, bool save, bool useLocation, string locationName) { Votes++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Client_OnSceneVoteStart(object message) { sceneVoteActive = true; Votes++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Client_OnBeginSceneLoad(SceneBeginLoadRpc message)
        { throw new InvalidOperationException("Installed Client_OnBeginSceneLoad IL was not installed."); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void CancelVote() { sceneVoteActive = false; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Client_OnVoteCancelled() { sceneVoteActive = false; }
        public void Clear() { ClearLocalSceneLoadAuthorization(); }
        public void ClosureContinued() { if (IsLocalSceneLoadAllowed("Base")) Loads++; else Votes++; }
    }
    public struct RpcContext
    {
        public NetService Service { get; set; }
        public object Sender { get; set; }
        public bool IsServer { get { return Service != null && Service.IsServer; } }
    }
    public struct SceneBeginLoadRpc
    {
        public string SceneId, CurtainGuid, LocationName;
        public bool NotifyEvac, SaveToFile, UseLocation, ForceSpawnAtHost;
        public UnityEngine.Vector3 HostSpawnPosition;
        public UnityEngine.Quaternion HostSpawnRotation;
    }
    public struct ModApiMessageRpc { public string Channel { get; set; } public byte[] Payload { get; set; } }
    public static class RPCModApi
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static void HandleModApiMessage(RpcContext context, ModApiMessageRpc message)
        { ModNetworkApi.Receive(context.Service, context.Sender, message.Channel, message.Payload); }
    }
    public class CoopGeneralSettings { public bool TeleporterSpawnTogether; }
    public static class CoopAISettings { public static CoopGeneralSettings ActiveGeneral { get { return new CoopGeneralSettings(); } } }
    public static class CoopTool
    {
        public static MapSelectionEntry Entry;
        public static int Teleports;
        public static readonly List<object> Wire = new List<object>();
        public static bool FailSend;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void SendRpc<T>(in T value, object excluded = null) where T : struct
        { if (FailSend) throw new InvalidOperationException("native send failed"); Wire.Add(value); }
        public static MapSelectionEntry GetMapSelectionEntrylist(string target) { return Entry; }
        public static bool GoTeleport(string target, string curtain) { Teleports++; return true; }
    }
    public class Spectator
    {
        public static Spectator Instance;
        public bool _spectatorActive, _spectatorEndOnVotePending;
        public int ClosureCalls;
        public Action DuringClosure;
        public void EndSpectatorAndShowClosure(bool force)
        {
            _spectatorActive = _spectatorEndOnVotePending = false;
            ClosureCalls++;
            SceneNet.Instance.Continue = SceneNet.Instance.ClosureContinued;
            if (DuringClosure != null) DuringClosure();
        }
    }
    public static class RPCScene
    {
        // The test must replace this body with ALL installed RPC instructions.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void HandleSceneBeginLoad(RpcContext context, SceneBeginLoadRpc message)
        { throw new InvalidOperationException("Installed HandleSceneBeginLoad IL was not installed."); }
    }
}
public static class TestClock { public static float Now; public static float Read() { return Now; } }

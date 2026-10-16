using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal static class Mathf { internal static float Max(float a, float b) { return Math.Max(a, b); } }
}
namespace BossRush
{
    internal sealed class BossRushCoopMessage
    {
        public string SenderId, Json;
        public bool IsFromHost;
        public T Read<T>() { return BossRushCoopJson.FromJson<T>(Json); }
    }
    internal sealed class BossRushCoopPlayer { public string Id, AssetId, ReadyEpoch; public bool IsConnected = true, IsLocal; }
    internal sealed class TestPeer
    {
        internal string Id, Run = "room-A";
        internal string AssetId = "slot-A";
        internal bool Host, Ready = true, Connected = true;
        internal int Scene = 1;
        internal bool Bound;
        internal int Epoch;
        internal string Token;
        internal object NativeService = new object();
        internal object NativePeer = new object();
        internal Duckov.Scenes.MultiSceneCore Core;
        internal void BindScene() { Core = new Duckov.Scenes.MultiSceneCore(); Bound = true; Token = "scene-" + Scene; }
        internal string ReadyEpoch { get { return "epoch:" + Id + ":" + AssetId + ":" + Epoch; } }
        internal Dictionary<string, List<Action<BossRushCoopMessage>>> Handlers = new Dictionary<string, List<Action<BossRushCoopMessage>>>();
    }
    internal static class BossRushCoop
    {
        internal static event Action<string> PlayerDisconnected;
        internal static void Disconnect(TestPeer peer) { peer.Connected = false; peer.Epoch++; peer.NativePeer = new object(); PlayerDisconnected?.Invoke(peer.Id); }
        internal static TestPeer Current;
        internal static readonly List<TestPeer> Peers = new List<TestPeer>();
        internal static readonly Queue<Action> Wire = new Queue<Action>();
        internal static int FailSends, DropAcks, SendAttempts;
        internal static bool IsHost { get { return Current.Host; } }
        internal static bool IsClient { get { return IsConnected && !Current.Host; } }
        internal static bool IsConnected { get { return Current.Connected; } }
        internal static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        internal static bool SessionReady { get { return Current.Ready; } }
        internal static bool HasBoundScene { get { return Current.Bound && Current.Core != null && Current.Core.gameObject.scene.isLoaded; } }
        internal static string SceneToken { get { return Current.Core != null && Current.Core.gameObject.scene.isLoaded ? Current.Token : null; } }
        internal static string LocalReadyEpoch { get { return Current.ReadyEpoch; } }
        internal static object NativeService { get { return Current.NativeService; } }
        internal static object NativeHostPeer { get { return Current.NativePeer; } }
        internal static string RunId { get { return Current.Run; } }
        internal static string LocalPlayerId { get { return Current.Id; } }
        internal static string LocalAssetId { get { return Current.AssetId; } }
        internal static int SceneGeneration { get { return Current.Scene; } }
        internal static BossRushCoopPlayer FindPlayer(string id)
        {
            TestPeer peer = Peers.Find(p => p.Id == id);
            return peer == null ? null : new BossRushCoopPlayer { Id = id, AssetId = peer.AssetId, ReadyEpoch = peer.ReadyEpoch, IsConnected = peer.Connected, IsLocal = id == Current.Id };
        }
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> action)
        {
            var owner = Current;
            List<Action<BossRushCoopMessage>> list;
            if (!owner.Handlers.TryGetValue(topic, out list)) owner.Handlers.Add(topic, list = new List<Action<BossRushCoopMessage>>());
            list.Add(action); return new Token(() => list.Remove(action));
        }
        // Match Runtime.SendToHost: a client without the room handshake cannot ACK,
        // while the host's SendToPlayer remains available for retained receipts.
        internal static bool SendToHost<T>(string topic, T value) { return SessionReady && SendToPlayer(Peers[0].Id, topic, value); }
        internal static bool SendToPlayer<T>(string id, string topic, T value)
        {
            var source = Current; var target = Peers.Find(p => p.Id == id);
            if (target == null || !target.Connected) return false;
            SendAttempts++;
            if (FailSends > 0) { FailSends--; return false; }
            if (topic.EndsWith(".ack", StringComparison.Ordinal) && DropAcks > 0) { DropAcks--; return true; }
            string json = BossRushCoopJson.ToJson(value);
            Wire.Enqueue(() => Deliver(target, topic, new BossRushCoopMessage { SenderId = source.Id, IsFromHost = source.Host, Json = json }));
            return true;
        }
        internal static bool Broadcast<T>(string topic, T value)
        {
            foreach (TestPeer peer in Peers) if (peer != Current && peer.Connected) SendToPlayer(peer.Id, topic, value);
            return true;
        }
        internal static void Deliver(TestPeer target, string topic, BossRushCoopMessage message)
        {
            TestPeer saved = Current; Current = target;
            try
            {
                List<Action<BossRushCoopMessage>> list;
                if (target.Handlers.TryGetValue(topic, out list)) foreach (var callback in list.ToArray()) callback(message);
            }
            finally { Current = saved; }
        }
        internal static void Flush() { int budget = 10000; while (Wire.Count > 0) { if (--budget == 0) throw new Exception("network loop"); Wire.Dequeue()(); } }
        private sealed class Token : IDisposable
        { private Action action; internal Token(Action action) { this.action = action; } public void Dispose() { if (action != null) action(); action = null; } }
    }
}

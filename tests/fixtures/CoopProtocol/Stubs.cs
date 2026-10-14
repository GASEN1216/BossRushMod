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
    internal sealed class BossRushCoopPlayer { public string Id, AssetId; public bool IsConnected = true, IsLocal; }
    internal sealed class TestPeer
    {
        internal string Id, Run = "room-A";
        internal string AssetId = "slot-A";
        internal bool Host, Ready = true, Connected = true;
        internal int Scene = 1;
        internal Dictionary<string, List<Action<BossRushCoopMessage>>> Handlers = new Dictionary<string, List<Action<BossRushCoopMessage>>>();
    }
    internal static class BossRushCoop
    {
        internal static event Action<string> PlayerDisconnected;
        internal static void Disconnect(TestPeer peer) { peer.Connected = false; PlayerDisconnected?.Invoke(peer.Id); }
        internal static TestPeer Current;
        internal static readonly List<TestPeer> Peers = new List<TestPeer>();
        internal static readonly Queue<Action> Wire = new Queue<Action>();
        internal static bool IsHost { get { return Current.Host; } }
        internal static bool IsClient { get { return !Current.Host; } }
        internal static bool IsConnected { get { return Current.Connected; } }
        internal static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        internal static bool SessionReady { get { return Current.Ready; } }
        internal static string RunId { get { return Current.Run; } }
        internal static string LocalPlayerId { get { return Current.Id; } }
        internal static string LocalAssetId { get { return Current.AssetId; } }
        internal static int SceneGeneration { get { return Current.Scene; } }
        internal static BossRushCoopPlayer FindPlayer(string id)
        {
            TestPeer peer = Peers.Find(p => p.Id == id);
            return peer == null ? null : new BossRushCoopPlayer { Id = id, AssetId = peer.AssetId, IsConnected = peer.Connected, IsLocal = id == Current.Id };
        }
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> action)
        {
            var owner = Current;
            List<Action<BossRushCoopMessage>> list;
            if (!owner.Handlers.TryGetValue(topic, out list)) owner.Handlers.Add(topic, list = new List<Action<BossRushCoopMessage>>());
            list.Add(action); return new Token(() => list.Remove(action));
        }
        internal static bool SendToHost<T>(string topic, T value) { return SendToPlayer(Peers[0].Id, topic, value); }
        internal static bool SendToPlayer<T>(string id, string topic, T value)
        {
            var source = Current; var target = Peers.Find(p => p.Id == id);
            if (target == null || !target.Connected) return false;
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

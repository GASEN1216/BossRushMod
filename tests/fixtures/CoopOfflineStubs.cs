// 仅供既有单机宿主隔离夹具显式引用。联网协议由 CoopProtocol 等生产链接夹具验证。
// 不在 Directory.Build.props 自动注入；需要 Character / Vector3 的签名由夹具的 partial 补齐。
using System;

namespace BossRush
{
    // 既有单机夹具不进入预测保护；生产 helper 由 CoopAiPrediction 单独完整链接。
    internal static class CoopAiPredictionHealthProtection
    {
        internal static void MarkReplica(object character) { }
        internal static void TrackAuthority(object character) { }
        internal static bool HasEntityAuthority(object character) { return character != null && BossRushCoop.HasWorldAuthority; }
        internal static bool TryClamp(object health, ref float value) { return false; }
    }
    internal static partial class BossRushCoop
    {
        internal static bool IsConnected;
        internal static bool IsHost;
        internal static bool IsClient { get { return IsConnected && !IsHost; } }
        internal static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        internal static bool SessionReady = true;
        internal static bool AnyPlayerAlive = true;
        internal static string LocalPlayerId = "local";
        internal static string LocalAssetId = "offline";
        internal static string RunId = string.Empty;
        internal static int SceneGeneration;
        internal static event Action SessionChanged;
        internal static event Action<string> PlayerDisconnected;
        internal static event Action<string> SceneReadyChanged;
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> handler) { return new OfflineToken(); }
        internal static bool SendToHost<T>(string topic, T payload) { return false; }
        internal static bool SendToPlayer<T>(string player, string topic, T payload) { return false; }
        internal static bool Broadcast<T>(string topic, T payload) { return false; }
        internal static void MarkSceneReady(string key) { }
        internal static bool AllPlayersSceneReady(string key) { return !IsConnected; }
        internal static string BeginControlLease(string player, string entity) { return null; }
        internal static void EndControlLease(string lease) { }
        internal static bool HasControlLease(string lease) { return false; }
        private sealed class OfflineToken : IDisposable { public void Dispose() { } }
    }
    internal sealed class BossRushCoopMessage
    {
        public string SenderId, Json, MessageId, Topic;
        public bool IsFromHost;
        public T Read<T>() { throw new InvalidOperationException("Offline fixture cannot receive co-op messages."); }
    }
    internal sealed class CoopGameplayCommand
    {
        public string RunId, Action, Text, PlayerId, AssetId;
        public int SceneGeneration, Value;
        public long Sequence;
        public bool Flag;
    }
    internal sealed class CoopGameplayChannel<T> : IDisposable where T : class
    {
        internal CoopGameplayChannel(string mode, Func<T> capture, Action<T> apply, Action<CoopGameplayCommand> execute) { }
        internal bool Request(string action, int value = 0, string text = null, bool flag = false) { return false; }
        internal void Tick(float delta) { }
        internal void Publish() { }
        public void Dispose() { }
    }
    internal sealed class CoopPersonalDelivery<T> : IDisposable where T : class
    {
        internal CoopPersonalDelivery(string name, Func<T, bool> apply) { }
        internal bool HasPendingDeliveries { get { return false; } }
        internal bool HasPendingConnectedDeliveries { get { return false; } }
        internal bool HasPendingForIdPrefix(string prefix, Func<string, bool> includesPlayer = null) { return false; }
        internal bool Deliver(string player, string id, T payload, string targetAssetId = null) { return false; }
        internal void Tick(float delta) { }
        public void Dispose() { }
    }
}

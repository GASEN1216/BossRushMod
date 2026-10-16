using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class BossRushCoopMessage
    {
        internal string SenderId, Json;
        internal bool IsFromHost;
        internal T Read<T>() { return BossRushCoopJson.FromJson<T>(Json); }
    }

    // This is the native boundary only. Delivery, retry keys, target assets and
    // JSON serialization are the actual production classes in this assembly.
    internal static partial class BossRushCoop
    {
        internal static string LocalPlayerId;
        internal static Action BeforeLocalAssetRead;
        internal static string LocalAssetId
        {
            get
            {
                // Production GetLocalAssetId synchronously refreshes the local
                // player's slot identity. This hook controls exactly that native
                // metadata read, after DispatchKill's owner validation.
                Action refresh = BeforeLocalAssetRead; BeforeLocalAssetRead = null; refresh?.Invoke();
                var player = FindPlayer(LocalPlayerId); return player != null ? player.AssetId : null;
            }
        }
        private static readonly Dictionary<string, List<Action<BossRushCoopMessage>>> handlers
            = new Dictionary<string, List<Action<BossRushCoopMessage>>>(StringComparer.Ordinal);
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> action)
        {
            List<Action<BossRushCoopMessage>> list;
            if (!handlers.TryGetValue(topic, out list)) handlers.Add(topic, list = new List<Action<BossRushCoopMessage>>());
            list.Add(action); return new Subscription(() => list.Remove(action));
        }
        internal static bool SendToHost<T>(string topic, T value) { return SendToPlayer(LocalPlayerId, topic, value); }
        internal static bool SendToPlayer<T>(string playerId, string topic, T value)
        {
            var player = FindPlayer(playerId);
            if (player == null || !player.IsConnected || !player.IsLocal) return false;
            List<Action<BossRushCoopMessage>> list;
            if (handlers.TryGetValue(topic, out list))
                foreach (var action in list.ToArray()) action(new BossRushCoopMessage {
                    SenderId = LocalPlayerId, IsFromHost = IsHost, Json = BossRushCoopJson.ToJson(value) });
            return true;
        }
        internal static void ClearWire() { handlers.Clear(); }
        private sealed class Subscription : IDisposable
        {
            private Action action;
            internal Subscription(Action action) { this.action = action; }
            public void Dispose() { if (action != null) action(); action = null; }
        }
    }
}

using System;
using System.Collections.Generic;

namespace BossRush
{
    // WIRE+: 独立于联机 Mod 的消息版本；不复用它的内部 opcode。
    [Serializable]
    internal sealed class BossRushCoopEnvelope
    {
        public int version = 1;
        public string session;
        public string topic;
        public string messageId;
        public string json;
    }

    internal sealed class BossRushCoopReplayWindow
    {
        private readonly int capacity;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> order = new Queue<string>();

        internal BossRushCoopReplayWindow(int capacity = 8192)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
        }

        internal bool Accept(string sender, string messageId)
        {
            if (string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(messageId) || messageId.Length > 80) return false;
            string key = sender + "\n" + messageId;
            if (!seen.Add(key)) return false;
            order.Enqueue(key);
            while (order.Count > capacity) seen.Remove(order.Dequeue());
            return true;
        }

        internal void Clear() { seen.Clear(); order.Clear(); }
    }

    internal sealed class BossRushCoopSceneBarrier
    {
        private readonly Dictionary<string, HashSet<string>> ready = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        internal void Mark(string playerId, string sceneKey)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(sceneKey) || sceneKey.Length > 256) return;
            HashSet<string> players;
            if (!ready.TryGetValue(sceneKey, out players))
            {
                // 每个 generation 有界；不接受远端不断创造新屏障耗尽内存。
                if (ready.Count >= 64) return;
                ready.Add(sceneKey, players = new HashSet<string>(StringComparer.Ordinal));
            }
            players.Add(playerId);
        }

        internal bool AllReady(string sceneKey, IEnumerable<string> participants)
        {
            HashSet<string> players;
            if (!ready.TryGetValue(sceneKey, out players)) return false;
            bool any = false;
            foreach (string id in participants)
            {
                any = true;
                if (!players.Contains(id)) return false;
            }
            return any;
        }

        internal string[] ReadyPlayers(string sceneKey)
        {
            HashSet<string> players;
            if (!ready.TryGetValue(sceneKey, out players)) return new string[0];
            var result = new string[players.Count];
            players.CopyTo(result);
            return result;
        }

        internal void Remove(string playerId)
        {
            foreach (HashSet<string> players in ready.Values) players.Remove(playerId);
        }

        internal void Clear() { ready.Clear(); }
    }

    [Serializable]
    internal sealed class BossRushCoopHello
    {
        public long sequence;
        public string manifest;
        public string reconnectToken;
        public string nativeId;
        public string assetId;
    }

    [Serializable]
    internal sealed class BossRushCoopWelcome
    {
        public long helloSequence;
        public string session;
        public string playerId;
        public string reconnectToken;
        public string error;
        public int generation;
    }

    [Serializable]
    internal sealed class BossRushCoopRosterEntry
    {
        public string id;
        public string nativeId;
        public string assetId;
        public bool connected;
        public bool alive;
    }

    [Serializable]
    internal sealed class BossRushCoopRoster
    {
        public int generation;
        public long revision;
        public bool ready;
        public BossRushCoopRosterEntry[] players;
    }

    [Serializable]
    internal sealed class BossRushCoopReady
    {
        public int generation;
        public string key;
        public string[] players;
    }

    /// <summary>只索引本机槽号，不读取存档内容；网络只发送随机身份，切回原槽可继续领取原收据。</summary>
    internal sealed class BossRushCoopAssetIdentity
    {
        private readonly Dictionary<int, string> identities = new Dictionary<int, string>();
        internal string Get(int slot)
        {
            if (slot < 0) return string.Empty;
            string id;
            if (!identities.TryGetValue(slot, out id)) identities.Add(slot, id = Guid.NewGuid().ToString("N"));
            return id;
        }
        internal void Invalidate(int slot) { identities.Remove(slot); }
        internal void Clear() { identities.Clear(); }
    }
}

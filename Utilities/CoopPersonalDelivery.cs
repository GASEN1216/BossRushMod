using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopPersonalReceipt
    {
        public string RunId, Id, Payload, TargetAssetId;
        public int SceneGeneration;
    }

    /// <summary>房主产生事实，个人客户端提交原有本地奖励。重发必须沿用同一收据 ID。</summary>
    internal sealed class CoopPersonalDelivery<T> : IDisposable where T : class
    {
        private sealed class Pending
        {
            internal string PlayerId;
            internal CoopPersonalReceipt Receipt;
        }

        private readonly string topic;
        private readonly Func<T, bool> apply;
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
        private readonly HashSet<string> delivered = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> applying = new HashSet<string>(StringComparer.Ordinal);
        private readonly IDisposable receiptSubscription, ackSubscription;
        private string runId;
        private string localAssetId;
        private int generation = -1;
        private float timer;
        private bool disposed;

        internal bool HasPendingDeliveries { get { return pending.Count > 0; } }
        internal bool HasPendingForIdPrefix(string prefix, Func<string, bool> includesPlayer = null)
        {
            if (string.IsNullOrEmpty(prefix)) return false;
            foreach (Pending item in pending.Values)
            {
                if (includesPlayer != null && !includesPlayer(item.PlayerId)) continue;
                string id = item.Receipt.Id;
                int separator = id.IndexOf(':');
                if (separator < 0 || id.Length - separator - 1 < prefix.Length
                    || string.Compare(id, separator + 1, prefix, 0, prefix.Length, StringComparison.Ordinal) != 0) continue;
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(item.PlayerId);
                if (player != null && player.IsConnected && IsCurrentAsset(item)) return true;
            }
            return false;
        }
        internal bool HasPendingForPlayers(IEnumerable<string> participants)
        {
            if (participants == null) return false;
            var ids = new HashSet<string>(participants, StringComparer.Ordinal);
            foreach (Pending item in pending.Values)
                if (ids.Contains(item.PlayerId) && IsCurrentAsset(item)) return true;
            return false;
        }
        internal bool HasPendingConnectedDeliveries
        {
            get
            {
                foreach (Pending item in pending.Values)
                {
                    BossRushCoopPlayer player = BossRushCoop.FindPlayer(item.PlayerId);
                    if (player != null && player.IsConnected && IsCurrentAsset(item)) return true;
                }
                return false;
            }
        }

        internal CoopPersonalDelivery(string name, Func<T, bool> apply)
        {
            topic = "BossRush.Personal." + name;
            this.apply = apply;
            receiptSubscription = BossRushCoop.Register(topic, Receive);
            ackSubscription = BossRushCoop.Register(topic + ".ack", Acknowledge);
        }

        private void RefreshSession()
        {
            // 同步读取槽身份，避免 OnUpdate 尚未执行时接收旧槽在途收据。
            localAssetId = BossRushCoop.LocalAssetId;
            generation = BossRushCoop.SceneGeneration;
            string currentRun = BossRushCoop.RunId;
            // 原生连接先恢复，welcome 后才恢复房间身份。此间空值不是新房间；
            // 保留已提交记录，避免 ack 丢失的收据在重连后再次付款。
            if (string.IsNullOrEmpty(currentRun) || runId == currentRun) return;
            runId = currentRun;
            pending.Clear();
            delivered.Clear();
        }

        internal bool Deliver(string playerId, string id, T payload, string targetAssetId = null)
        {
            if (disposed || !BossRushCoop.HasWorldAuthority || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(id) || payload == null) return false;
            RefreshSession();
            id = generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + id;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            if (player == null) return false;
            if (targetAssetId == null) targetAssetId = player.AssetId;
            if (string.IsNullOrEmpty(targetAssetId) || targetAssetId.Length > 80) return false;
            string key = playerId + ":" + targetAssetId + ":" + id;
            if (delivered.Contains(key) || pending.ContainsKey(key)) return true;
            CoopPersonalReceipt receipt = new CoopPersonalReceipt
            {
                RunId = runId, SceneGeneration = generation, Id = id, Payload = BossRushCoopJson.ToJson(payload), TargetAssetId = targetAssetId
            };
            if (!BossRushCoop.IsHost && !player.IsLocal) return false;
            Pending queued = new Pending { PlayerId = playerId, Receipt = receipt };
            pending.Add(key, queued);
            TryDeliverPending(key, queued);
            return true;
        }

        private void TryDeliverPending(string key, Pending item)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(item.PlayerId);
            if (player == null || !player.IsConnected) return;
            if (!player.IsLocal)
            {
                BossRushCoop.SendToPlayer(item.PlayerId, topic, item.Receipt);
                return;
            }
            if (item.Receipt.TargetAssetId != localAssetId) return;
            if (!TryApply(key, item.Receipt.Payload)) return;
            // 回调可能提交其它收据或结束整个会话，不能重新标记已失效的 session。
            if (disposed || BossRushCoop.RunId != item.Receipt.RunId || BossRushCoop.LocalAssetId != item.Receipt.TargetAssetId) return;
            pending.Remove(key);
            delivered.Add(key);
        }

        internal void Tick(float deltaTime)
        {
            if (disposed || !BossRushCoop.IsConnected) return;
            RefreshSession();
            timer -= Mathf.Max(0f, deltaTime);
            if (!BossRushCoop.IsHost || timer > 0f || pending.Count == 0) return;
            timer = 1f;
            // 本机提交及 ack 都可能同步修改队列；按本帧快照投递。
            foreach (KeyValuePair<string, Pending> pair in new List<KeyValuePair<string, Pending>>(pending))
                if (pending.ContainsKey(pair.Key)) TryDeliverPending(pair.Key, pair.Value);
        }

        private bool IsCurrent(CoopPersonalReceipt receipt)
        {
            return receipt != null && receipt.RunId == runId && !string.IsNullOrEmpty(receipt.Id)
                && !string.IsNullOrEmpty(receipt.TargetAssetId) && receipt.TargetAssetId.Length <= 80;
        }
        private static bool IsCurrentAsset(Pending item)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(item.PlayerId);
            return player != null && player.AssetId == item.Receipt.TargetAssetId;
        }

        private bool TryApply(string key, string json)
        {
            if (!applying.Add(key)) return false;
            try
            {
                T payload = BossRushCoopJson.FromJson<T>(json);
                return payload != null && apply(payload);
            }
            catch (Exception) { return false; }
            finally { applying.Remove(key); }
        }

        private void Receive(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsClient || message == null || !message.IsFromHost) return;
            RefreshSession();
            CoopPersonalReceipt receipt = message.Read<CoopPersonalReceipt>();
            if (!IsCurrent(receipt) || receipt.TargetAssetId != localAssetId) return;
            string key = BossRushCoop.LocalPlayerId + ":" + receipt.TargetAssetId + ":" + receipt.Id;
            if (!delivered.Contains(key))
            {
                if (!TryApply(key, receipt.Payload) || disposed || BossRushCoop.RunId != receipt.RunId || BossRushCoop.LocalAssetId != receipt.TargetAssetId) return;
                delivered.Add(key);
            }
            BossRushCoop.SendToHost(topic + ".ack", new CoopPersonalReceipt
            {
                RunId = runId, SceneGeneration = generation, Id = receipt.Id, TargetAssetId = receipt.TargetAssetId
            });
        }

        private void Acknowledge(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsHost || message == null) return;
            RefreshSession();
            CoopPersonalReceipt receipt = message.Read<CoopPersonalReceipt>();
            if (!IsCurrent(receipt)) return;
            string key = message.SenderId + ":" + receipt.TargetAssetId + ":" + receipt.Id;
            if (!pending.Remove(key)) return;
            delivered.Add(key);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            receiptSubscription.Dispose();
            ackSubscription.Dispose();
            pending.Clear();
            delivered.Clear();
        }
    }
}

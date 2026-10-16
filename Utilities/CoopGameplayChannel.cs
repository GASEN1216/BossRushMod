using System;
using System.Collections.Generic;
using Duckov.Scenes;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopGameplayCommand
    {
        public string RunId;
        public string AssetId;
        public string ReadyEpoch;
        public string SceneToken;
        public int SceneGeneration;
        public long Sequence;
        public string Action;
        public int Value;
        public string Text;
        public bool Flag;
        [NonSerialized] public string PlayerId;
    }

    [Serializable]
    internal sealed class CoopGameplaySnapshot
    {
        public string RunId;
        public string SceneToken;
        public int SceneGeneration;
        public long Revision;
        public string Payload;
    }

    [Serializable]
    internal sealed class CoopGameplayAck
    {
        public string RunId, AssetId, ReadyEpoch, SceneToken;
        public int SceneGeneration;
        public long Sequence;
    }

    /// <summary>
    /// 模式自己的网络频道。世界快照只含局内状态，绝不传递玩家存档；
    /// 传输层给出的参与者身份是命令的唯一 owner，客户端 payload 不能冒领。
    /// </summary>
    internal sealed class CoopGameplayChannel<T> : IDisposable where T : class
    {
        private readonly string topic;
        private readonly Func<T> capture;
        private readonly Action<T> apply;
        private readonly Action<CoopGameplayCommand> execute;
        private sealed class CommandWindow
        {
            internal string Epoch;
            internal long Next = 1;
            internal readonly SortedDictionary<long, CoopGameplayCommand> Waiting = new SortedDictionary<long, CoopGameplayCommand>();
        }
        private sealed class PendingRequest
        {
            internal CoopGameplayCommand Command;
            internal MultiSceneCore Core;
            internal int Handle;
            internal object Service, Peer;
            internal string Room, Asset, Epoch, Token;
            internal float Retry;
        }
        private readonly Dictionary<string, CommandWindow> sequences = new Dictionary<string, CommandWindow>(StringComparer.Ordinal);
        private readonly List<PendingRequest> pendingRequests = new List<PendingRequest>();
        private IDisposable commands;
        private IDisposable snapshots;
        private IDisposable acknowledgements;
        private CoopGameplaySnapshot pendingSnapshot;
        private long nextSequence;
        private long revision;
        private long appliedRevision;
        private int generation = -1;
        private string runId;
        private string localEpoch;
        private float publishTimer;
        private bool publishDirty;
        private bool disposed;

        internal CoopGameplayChannel(string mode, Func<T> capture, Action<T> apply, Action<CoopGameplayCommand> execute)
        {
            topic = "BossRush.Gameplay." + mode;
            this.capture = capture;
            this.apply = apply;
            this.execute = execute;
            commands = BossRushCoop.Register(topic + ".command", HandleCommand);
            snapshots = BossRushCoop.Register(topic + ".snapshot", HandleSnapshot);
            acknowledgements = BossRushCoop.Register(topic + ".ack", HandleAcknowledgement);
            BossRushCoop.PlayerDisconnected += OnPlayerDisconnected;
        }

        internal bool Request(string action, int value = 0, string text = null, bool flag = false)
        {
            return QueueRequest(action, value, text, flag, false);
        }

        // Periodic reports of the same fact/transaction share only an outstanding request.
        // User actions still use Request, and a completed ACK permits a later business retry.
        internal bool RequestRepeat(string action, int value = 0, string text = null, bool flag = false)
        {
            return QueueRequest(action, value, text, flag, true);
        }

        private bool QueueRequest(string action, int value, string text, bool flag, bool repeat)
        {
            if (disposed || !BossRushCoop.IsClient) return false;
            RefreshSession();
            if (repeat)
            {
                foreach (PendingRequest pending in pendingRequests)
                    if (IsRequestCurrent(pending) && pending.Command.Action == action && pending.Command.Value == value
                        && pending.Command.Text == text && pending.Command.Flag == flag)
                        return true;
            }
            MultiSceneCore core = MultiSceneCore.Instance;
            pendingRequests.Add(new PendingRequest
            {
                Core = core, Handle = core != null ? core.gameObject.scene.handle : int.MinValue,
                Service = BossRushCoop.NativeService, Peer = BossRushCoop.NativeHostPeer,
                Room = BossRushCoop.RunId, Asset = BossRushCoop.LocalAssetId,
                Epoch = BossRushCoop.LocalReadyEpoch, Token = BossRushCoop.SceneToken,
                Command = new CoopGameplayCommand { Action = action, Value = value, Text = text, Flag = flag }
            });
            FlushRequests(0f);
            return true;
        }

        internal void Tick(float deltaTime)
        {
            if (disposed) return;
            if (!BossRushCoop.IsConnected) { pendingRequests.Clear(); pendingSnapshot = null; return; }
            RefreshSession();
            if (BossRushCoop.IsClient) { FlushRequests(deltaTime); ApplyPendingSnapshot(); return; }
            if (!BossRushCoop.HasWorldAuthority) return;
            publishTimer -= Mathf.Max(0f, deltaTime);
            if (publishTimer > 0f && !publishDirty) return;
            publishTimer = 0.25f;
            Publish();
        }

        internal void Publish()
        {
            if (disposed || !BossRushCoop.IsHost || !BossRushCoop.SessionReady || !BossRushCoop.HasBoundScene || capture == null) return;
            RefreshSession();
            T state = capture();
            if (state == null) return;
            publishDirty = false;
            BossRushCoop.Broadcast(topic + ".snapshot", new CoopGameplaySnapshot
            {
                RunId = BossRushCoop.RunId,
                SceneGeneration = BossRushCoop.SceneGeneration,
                SceneToken = BossRushCoop.SceneToken,
                Revision = ++revision,
                Payload = BossRushCoopJson.ToJson(state)
            });
        }

        private void RefreshSession()
        {
            string epoch = BossRushCoop.LocalReadyEpoch;
            if (epoch != localEpoch) { localEpoch = epoch; nextSequence = 0; }
            if (generation == BossRushCoop.SceneGeneration && string.Equals(runId, BossRushCoop.RunId, StringComparison.Ordinal)) return;
            generation = BossRushCoop.SceneGeneration;
            runId = BossRushCoop.RunId;
            sequences.Clear();
            nextSequence = revision = appliedRevision = 0;
            publishTimer = 0f;
            publishDirty = false;
            if (pendingSnapshot != null && (pendingSnapshot.RunId != runId || pendingSnapshot.SceneGeneration < generation)) pendingSnapshot = null;
        }

        private bool IsRequestCurrent(PendingRequest pending)
        {
            var core = MultiSceneCore.Instance;
            return BossRushCoop.IsClient && pending.Core != null && ReferenceEquals(pending.Core, core)
                && core.gameObject.scene.handle == pending.Handle && core.gameObject.scene.isLoaded
                && ReferenceEquals(pending.Service, BossRushCoop.NativeService) && ReferenceEquals(pending.Peer, BossRushCoop.NativeHostPeer)
                && pending.Asset == BossRushCoop.LocalAssetId && (string.IsNullOrEmpty(pending.Room) || pending.Room == BossRushCoop.RunId)
                && (string.IsNullOrEmpty(pending.Epoch) || pending.Epoch == BossRushCoop.LocalReadyEpoch)
                && (string.IsNullOrEmpty(pending.Token) || pending.Token == BossRushCoop.SceneToken);
        }

        private void FlushRequests(float deltaTime)
        {
            foreach (PendingRequest pending in pendingRequests.ToArray())
            {
                if (!IsRequestCurrent(pending)) { pendingRequests.Remove(pending); continue; }
                if (!BossRushCoop.SessionReady || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(localEpoch)) continue;
                pending.Retry -= Mathf.Max(0f, deltaTime);
                if (pending.Retry > 0f) continue;
                CoopGameplayCommand command = pending.Command;
                if (command.Sequence == 0)
                {
                    pending.Room = command.RunId = BossRushCoop.RunId;
                    pending.Epoch = command.ReadyEpoch = localEpoch;
                    pending.Token = command.SceneToken = BossRushCoop.SceneToken;
                    command.AssetId = pending.Asset; command.SceneGeneration = generation; command.Sequence = ++nextSequence;
                }
                // Reliable transport acceptance is not world execution. Keep the exact sequence until its ACK.
                if (BossRushCoop.SendToHost(topic + ".command", command)) pending.Retry = 1f;
            }
        }

        private void HandleCommand(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsHost || !BossRushCoop.SessionReady || !BossRushCoop.HasBoundScene || message == null || execute == null) return;
            RefreshSession();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (player == null || !player.IsConnected) return;
            CoopGameplayCommand command = message.Read<CoopGameplayCommand>();
            if (command == null || command.SceneGeneration != generation ||
                !string.Equals(command.RunId, runId, StringComparison.Ordinal) || command.Sequence <= 0
                || string.IsNullOrEmpty(command.AssetId) || command.AssetId != player.AssetId
                || string.IsNullOrEmpty(command.ReadyEpoch) || command.ReadyEpoch != player.ReadyEpoch
                || string.IsNullOrEmpty(command.SceneToken) || command.SceneToken != BossRushCoop.SceneToken) return;
            CommandWindow window;
            if (!sequences.TryGetValue(message.SenderId, out window) || window.Epoch != command.ReadyEpoch)
                sequences[message.SenderId] = window = new CommandWindow { Epoch = command.ReadyEpoch };
            if (command.Sequence < window.Next) { Acknowledge(command, message.SenderId); return; }
            // Flow control bounds untrusted gaps. The owner retains every request and retries it.
            if (command.Sequence - window.Next > 128) return;
            command.PlayerId = message.SenderId;
            if (!window.Waiting.ContainsKey(command.Sequence)) window.Waiting.Add(command.Sequence, command);
            CoopGameplayCommand next;
            while (window.Waiting.TryGetValue(window.Next, out next))
            {
                if (!BossRushCoop.HasBoundScene || !player.IsConnected || player.AssetId != next.AssetId
                    || player.ReadyEpoch != next.ReadyEpoch || next.SceneToken != BossRushCoop.SceneToken) break;
                // ACK means terminal dispatch, not a successful purchase/start. Preserve the original
                // at-most-once handler contract even when it throws after a partial business effect.
                window.Waiting.Remove(window.Next++);
                // All effects and ACKs remain immediate. Multiple commands received before
                // the next module Tick share one capture/serialization/broadcast.
                publishDirty = true;
                try { execute(next); }
                finally { Acknowledge(next, message.SenderId); }
                if (!BossRushCoop.HasBoundScene || generation != BossRushCoop.SceneGeneration || runId != BossRushCoop.RunId) break;
            }
        }

        private void Acknowledge(CoopGameplayCommand command, string player)
        {
            BossRushCoop.SendToPlayer(player, topic + ".ack", new CoopGameplayAck { RunId = command.RunId,
                AssetId = command.AssetId, ReadyEpoch = command.ReadyEpoch, SceneToken = command.SceneToken,
                SceneGeneration = command.SceneGeneration, Sequence = command.Sequence });
        }

        private void HandleAcknowledgement(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsClient || message == null || !message.IsFromHost) return;
            var ack = message.Read<CoopGameplayAck>();
            if (ack == null) return;
            pendingRequests.RemoveAll(pending => IsRequestCurrent(pending) && pending.Command.Sequence > 0
                && pending.Command.Sequence == ack.Sequence && pending.Command.RunId == ack.RunId
                && pending.Command.AssetId == ack.AssetId && pending.Command.ReadyEpoch == ack.ReadyEpoch
                && pending.Command.SceneToken == ack.SceneToken && pending.Command.SceneGeneration == ack.SceneGeneration);
        }

        private void HandleSnapshot(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsClient || message == null || !message.IsFromHost || apply == null) return;
            RefreshSession();
            CoopGameplaySnapshot snapshot = message.Read<CoopGameplaySnapshot>();
            if (snapshot == null || snapshot.SceneGeneration < generation || snapshot.Revision <= 0
                || !string.Equals(snapshot.RunId, runId, StringComparison.Ordinal) || string.IsNullOrEmpty(snapshot.Payload)
                || string.IsNullOrEmpty(snapshot.SceneToken)) return;
            if (snapshot.SceneGeneration == generation && !string.IsNullOrEmpty(BossRushCoop.SceneToken)
                && snapshot.SceneToken != BossRushCoop.SceneToken) return;
            if (pendingSnapshot != null && (snapshot.SceneGeneration < pendingSnapshot.SceneGeneration
                || snapshot.SceneGeneration == pendingSnapshot.SceneGeneration && snapshot.Revision <= pendingSnapshot.Revision)) return;
            pendingSnapshot = snapshot;
            ApplyPendingSnapshot();
        }
        private void ApplyPendingSnapshot()
        {
            var snapshot = pendingSnapshot;
            if (snapshot == null || !BossRushCoop.SessionReady || !BossRushCoop.HasBoundScene || snapshot.SceneGeneration != generation
                || snapshot.SceneToken != BossRushCoop.SceneToken || snapshot.RunId != runId || snapshot.Revision <= appliedRevision) return;
            T state = BossRushCoopJson.FromJson<T>(snapshot.Payload);
            if (state == null) return;
            apply(state);
            appliedRevision = snapshot.Revision;
            pendingSnapshot = null;
        }
        private void OnPlayerDisconnected(string playerId)
        {
            // 稳定玩家 ID 会复用于重连；新连接的首个命令不能被旧连接的高序号封住。
            if (!disposed && !string.IsNullOrEmpty(playerId)) sequences.Remove(playerId);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (commands != null) commands.Dispose();
            if (snapshots != null) snapshots.Dispose();
            if (acknowledgements != null) acknowledgements.Dispose();
            commands = snapshots = acknowledgements = null;
            BossRushCoop.PlayerDisconnected -= OnPlayerDisconnected;
            sequences.Clear();
            pendingRequests.Clear(); pendingSnapshot = null;
        }
    }
}

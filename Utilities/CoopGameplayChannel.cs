using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopGameplayCommand
    {
        public string RunId;
        public string AssetId;
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
        public int SceneGeneration;
        public long Revision;
        public string Payload;
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
        private readonly Dictionary<string, long> sequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private IDisposable commands;
        private IDisposable snapshots;
        private long nextSequence;
        private long revision;
        private long appliedRevision;
        private int generation = -1;
        private string runId;
        private float publishTimer;
        private bool disposed;

        internal CoopGameplayChannel(string mode, Func<T> capture, Action<T> apply, Action<CoopGameplayCommand> execute)
        {
            topic = "BossRush.Gameplay." + mode;
            this.capture = capture;
            this.apply = apply;
            this.execute = execute;
            commands = BossRushCoop.Register(topic + ".command", HandleCommand);
            snapshots = BossRushCoop.Register(topic + ".snapshot", HandleSnapshot);
            BossRushCoop.PlayerDisconnected += OnPlayerDisconnected;
        }

        internal bool Request(string action, int value = 0, string text = null, bool flag = false)
        {
            if (!BossRushCoop.IsClient) return false;
            if (!BossRushCoop.SessionReady) return true;
            RefreshSession();
            BossRushCoop.SendToHost(topic + ".command", new CoopGameplayCommand
            {
                RunId = BossRushCoop.RunId,
                AssetId = BossRushCoop.LocalAssetId,
                SceneGeneration = BossRushCoop.SceneGeneration,
                Sequence = ++nextSequence,
                Action = action,
                Value = value,
                Text = text,
                Flag = flag
            });
            return true;
        }

        internal void Tick(float deltaTime)
        {
            if (disposed || !BossRushCoop.IsConnected) return;
            RefreshSession();
            if (!BossRushCoop.HasWorldAuthority) return;
            publishTimer -= Mathf.Max(0f, deltaTime);
            if (publishTimer > 0f) return;
            publishTimer = 0.25f;
            Publish();
        }

        internal void Publish()
        {
            if (disposed || !BossRushCoop.IsHost || capture == null) return;
            RefreshSession();
            T state = capture();
            if (state == null) return;
            BossRushCoop.Broadcast(topic + ".snapshot", new CoopGameplaySnapshot
            {
                RunId = BossRushCoop.RunId,
                SceneGeneration = BossRushCoop.SceneGeneration,
                Revision = ++revision,
                Payload = BossRushCoopJson.ToJson(state)
            });
        }

        private void RefreshSession()
        {
            if (generation == BossRushCoop.SceneGeneration && string.Equals(runId, BossRushCoop.RunId, StringComparison.Ordinal)) return;
            generation = BossRushCoop.SceneGeneration;
            runId = BossRushCoop.RunId;
            sequences.Clear();
            nextSequence = revision = appliedRevision = 0;
            publishTimer = 0f;
        }

        private void HandleCommand(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsHost || !BossRushCoop.SessionReady || message == null || execute == null) return;
            RefreshSession();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (player == null || !player.IsConnected) return;
            CoopGameplayCommand command = message.Read<CoopGameplayCommand>();
            if (command == null || command.SceneGeneration != generation ||
                !string.Equals(command.RunId, runId, StringComparison.Ordinal) || command.Sequence <= 0
                || string.IsNullOrEmpty(command.AssetId) || command.AssetId != player.AssetId) return;
            long previous;
            if (sequences.TryGetValue(message.SenderId, out previous) && command.Sequence <= previous) return;
            sequences[message.SenderId] = command.Sequence;
            command.PlayerId = message.SenderId;
            execute(command);
            Publish();
        }

        private void HandleSnapshot(BossRushCoopMessage message)
        {
            if (disposed || !BossRushCoop.IsClient || message == null || !message.IsFromHost || apply == null) return;
            RefreshSession();
            CoopGameplaySnapshot snapshot = message.Read<CoopGameplaySnapshot>();
            if (snapshot == null || snapshot.SceneGeneration != generation || snapshot.Revision <= appliedRevision ||
                !string.Equals(snapshot.RunId, runId, StringComparison.Ordinal) || string.IsNullOrEmpty(snapshot.Payload)) return;
            T state = BossRushCoopJson.FromJson<T>(snapshot.Payload);
            if (state == null) return;
            apply(state);
            appliedRevision = snapshot.Revision;
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
            commands = snapshots = null;
            BossRushCoop.PlayerDisconnected -= OnPlayerDisconnected;
            sequences.Clear();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Utilities;
using Duckov.UI.DialogueBubbles;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>异步 Boss 创建的原世界身份，Stop 后不得沿同一个场景续作成离线世界。</summary>
    internal sealed class CoopBossCreationLease
    {
        private readonly bool connected = BossRushCoop.IsConnected, host = BossRushCoop.IsHost;
        private string room = BossRushCoop.RunId;
        private bool revoked;
        internal bool IsValid()
        {
            if (revoked) return false;
            if (connected != BossRushCoop.IsConnected || connected && host != BossRushCoop.IsHost
                || connected && !string.IsNullOrEmpty(room) && !string.IsNullOrEmpty(BossRushCoop.RunId)
                    && !string.Equals(room, BossRushCoop.RunId, StringComparison.Ordinal)) revoked = true;
            else if (connected && string.IsNullOrEmpty(room) && !string.IsNullOrEmpty(BossRushCoop.RunId)) room = BossRushCoop.RunId;
            return !revoked && (!connected || host);
        }
    }

    /// <summary>远端模型由 COOP 插值，碰撞击退须在角色本人执行；生命令牌防止旧碰撞推动复活后的新角色。</summary>
    internal sealed class DragonKingCoopContactRuntimeModule : BossRushRuntimeModuleBase
    {
        [Serializable] private sealed class Body { public string Run, Asset, Stream, Token; public int Scene; public long Revision; }
        [Serializable] private sealed class PushEvent { public string Run, Asset, Player, Token; public int Scene; public Vector3 Velocity; }
        private sealed class BodyState
        {
            internal Body Value; internal CharacterMainControl Character;
            internal readonly HashSet<string> Retired = new HashSet<string>(StringComparer.Ordinal);
        }
        private sealed class WaitingPush
        {
            internal string Id, Player, Asset, Run; internal int Scene;
            internal CharacterMainControl Character; internal Vector3 Velocity; internal float Until;
        }
        private static DragonKingCoopContactRuntimeModule active;
        private readonly Dictionary<string, BodyState> bodies = new Dictionary<string, BodyState>(StringComparer.Ordinal);
        private readonly List<WaitingPush> waiting = new List<WaitingPush>();
        private readonly string stream = Guid.NewGuid().ToString("N");
        private CoopPersonalDelivery<PushEvent> pushes;
        private IDisposable bodySubscription;
        private CharacterMainControl localBody;
        private string token, asset, run;
        private int scene = -1; private long revision; private float nextBodyPublish;
        private bool subscribed;
        public override string ModuleName { get { return "DragonKingCoopContact"; } }
        public override void OnAwake(ModBehaviour owner)
        {
            active = this;
            bodySubscription = BossRushCoop.Register("dragon.contact.body", ReceiveBody);
            pushes = new CoopPersonalDelivery<PushEvent>("DragonKing.Contact", ApplyPush);
            if (!subscribed) { BossRushCoop.SessionChanged += Reset; subscribed = true; }
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!BossRushCoop.IsConnected) { Reset(); return; }
            RefreshBody();
            if (Time.unscaledTime >= nextBodyPublish)
            { nextBodyPublish = Time.unscaledTime + 0.25f; PublishBody(); }
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                WaitingPush value = waiting[i];
                BossRushCoopPlayer member = BossRushCoop.FindPlayer(value.Player);
                if (Time.unscaledTime >= value.Until || value.Run != BossRushCoop.RunId || value.Scene != BossRushCoop.SceneGeneration
                    || member == null || !member.IsAlive || member.AssetId != value.Asset || member.Character != value.Character
                    || SendPush(value)) waiting.RemoveAt(i);
            }
            pushes.Tick(unscaledDeltaTime);
        }
        private void RefreshBody()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) player = null;
            if (run == BossRushCoop.RunId && scene == BossRushCoop.SceneGeneration && asset == BossRushCoop.LocalAssetId && localBody == player) return;
            if (run != BossRushCoop.RunId || scene != BossRushCoop.SceneGeneration) { bodies.Clear(); waiting.Clear(); }
            localBody = player; run = BossRushCoop.RunId; scene = BossRushCoop.SceneGeneration; asset = BossRushCoop.LocalAssetId;
            token = player != null ? Guid.NewGuid().ToString("N") : string.Empty;
            nextBodyPublish = 0f;
        }
        private void PublishBody()
        {
            if (string.IsNullOrEmpty(run)) return;
            var value = new Body { Run = run, Scene = scene, Asset = asset, Stream = stream, Token = token, Revision = ++revision };
            if (BossRushCoop.IsHost) ApplyBody(BossRushCoop.LocalPlayerId, value);
            else BossRushCoop.SendToHost("dragon.contact.body", value);
        }
        private void ReceiveBody(BossRushCoopMessage message)
        { if (BossRushCoop.IsHost) { RefreshBody(); ApplyBody(message.SenderId, message.Read<Body>()); } }
        private void ApplyBody(string player, Body value)
        {
            BossRushCoopPlayer member = BossRushCoop.FindPlayer(player);
            if (member == null || value == null || value.Run != BossRushCoop.RunId || value.Scene != BossRushCoop.SceneGeneration
                || value.Asset != member.AssetId || string.IsNullOrEmpty(value.Stream) || value.Stream.Length > 64
                || value.Token == null || value.Token.Length > 64 || value.Revision < 0) return;
            BodyState state;
            if (!bodies.TryGetValue(player, out state) || state.Value.Asset != value.Asset)
            { state = new BodyState(); bodies[player] = state; }
            if (state.Value != null && state.Value.Stream != value.Stream)
            {
                if (state.Retired.Contains(value.Stream)) return;
                state.Retired.Add(state.Value.Stream); state.Value = null;
            }
            if (state.Value != null && value.Revision <= state.Value.Revision) return;
            state.Value = value; state.Character = member.Character;
        }
        internal static void Push(CharacterMainControl player, Vector3 velocity, string id)
        {
            if (active == null || !BossRushCoop.IsHost) return;
            BossRushCoopPlayer member = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(player));
            if (member == null || !member.IsAlive) return;
            active.RefreshBody();
            if (member.Id == BossRushCoop.LocalPlayerId) active.PublishBody();
            var value = new WaitingPush { Id = id, Player = member.Id, Asset = member.AssetId, Character = player,
                Run = BossRushCoop.RunId, Scene = BossRushCoop.SceneGeneration, Velocity = velocity, Until = Time.unscaledTime + 1f };
            if (!active.SendPush(value)) active.waiting.Add(value);
        }
        private bool SendPush(WaitingPush value)
        {
            BodyState state;
            if (!bodies.TryGetValue(value.Player, out state) || state.Character != value.Character
                || state.Value.Asset != value.Asset || string.IsNullOrEmpty(state.Value.Token)) return false;
            pushes.Deliver(value.Player, value.Id, new PushEvent { Run = value.Run, Scene = value.Scene, Asset = value.Asset,
                Player = value.Player, Token = state.Value.Token, Velocity = value.Velocity }, value.Asset);
            return true;
        }
        private bool ApplyPush(PushEvent value)
        {
            RefreshBody();
            if (value == null || value.Run != run || value.Scene != scene || value.Asset != asset || value.Player != BossRushCoop.LocalPlayerId
                || value.Token != token || localBody == null || localBody.Health == null || localBody.Health.IsDead) return true;
            localBody.SetForceMoveVelocity(value.Velocity);
            return true;
        }
        private void Reset()
        { bodies.Clear(); waiting.Clear(); localBody = null; token = asset = run = null; scene = -1; nextBodyPublish = 0f; }
        public override void OnDestroy()
        {
            if (subscribed) { BossRushCoop.SessionChanged -= Reset; subscribed = false; }
            if (bodySubscription != null) bodySubscription.Dispose();
            if (pushes != null) pushes.Dispose();
            Reset(); if (active == this) active = null;
        }
    }
}

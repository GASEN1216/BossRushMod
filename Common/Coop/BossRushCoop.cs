using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed class BossRushCoopPlayer
    {
        public string Id;
        public string AssetId;
        public CharacterMainControl Character;
        public bool IsLocal;
        public bool IsConnected;
        internal string NativeId;
        internal object Peer;
        internal string ReconnectToken;
        internal long HelloSequence;
        internal long AssetRevision;
        internal string ReadyEpoch;
        internal bool HostAlive;
        internal string HostAliveAssetId;
        internal int HostAliveGeneration = -1;
        internal CharacterMainControl HostAliveCharacter;
        public bool IsAlive
        {
            get
            {
                if (!IsConnected || Character == null || Character.Health == null) return false;
                if (IsLocal || (BossRushCoop.Runtime != null && BossRushCoop.Runtime.Host)) return !Character.Health.IsDead;
                if (!BossRushCoop.IsClient || !BossRushCoop.Runtime.HasHostPeer || !BossRushCoop.Runtime.HasBoundScene) { HostAlive = false; return false; }
                // 原生客端代理的 Health.IsDead 不一定随远端死亡更新；只使用房主当前名单的事实。
                return BossRushCoop.IsClient && HostAlive && HostAliveGeneration == BossRushCoop.SceneGeneration
                    && string.Equals(HostAliveAssetId, AssetId, StringComparison.Ordinal)
                    && ReferenceEquals(HostAliveCharacter, Character);
            }
        }
    }

    internal sealed class BossRushCoopMessage
    {
        public string SenderId { get; internal set; }
        public bool IsFromHost { get; internal set; }
        public string Json { get; internal set; }
        public string MessageId { get; internal set; }
        public string Topic { get; internal set; }
        public T Read<T>() { return BossRushCoopJson.FromJson<T>(Json); }
    }

    /// <summary>可选联机门面。单机不会加载、引用或要求联机程序集。</summary>
    internal static class BossRushCoop
    {
        internal static BossRushCoopRuntimeModule Runtime;
        private static readonly IReadOnlyList<BossRushCoopPlayer> emptyPlayers = new BossRushCoopPlayer[0];
        internal static void RegisterModules(BossRushRuntimeModuleHost host)
        {
            host.Register(new BossRushCoopRuntimeModule());
            host.Register(new BossRushCoopContentRuntimeModule());
            host.Register(new BossRushCoopFxRuntimeModule());
            host.Register(new PetNestCoopRuntimeModule());
            host.Register(new CoopPersonalProgressRuntimeModule());
            host.Register(new MutatorCoopRuntimeModule());
            host.Register(new BossRushMapEntryAssetRuntimeModule());
            host.Register(new CoopAiPredictionHealthRuntimeModule());
            host.Register(new DragonKingCoopContactRuntimeModule());
        }
        public static bool IsConnected { get { return Runtime != null && (Runtime.Connected || Runtime.NetworkBlocked); } }
        public static bool IsHost { get { return IsConnected && !Runtime.NetworkBlocked && Runtime.Host; } }
        public static bool IsClient { get { return IsConnected && !IsHost; } }
        public static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        public static string LocalPlayerId { get { return Runtime != null ? Runtime.LocalId : "local"; } }
        public static string LocalAssetId { get { return Runtime != null ? Runtime.GetLocalAssetId() : string.Empty; } }
        public static string RunId { get { return Runtime != null ? Runtime.SessionId : string.Empty; } }
        public static int SceneGeneration { get { return Runtime != null ? Runtime.Generation : 0; } }
        public static bool SessionReady { get { return !IsConnected || Runtime.Ready; } }
        internal static bool HasBoundScene { get { return !IsConnected || (Runtime != null && Runtime.HasBoundScene); } }
        internal static string SceneToken { get { return Runtime != null ? Runtime.SceneToken : null; } }
        internal static string LocalReadyEpoch { get { return Runtime != null ? Runtime.LocalReadyEpoch : null; } }
        internal static object NativeService { get { return Runtime != null ? Runtime.NativeService : null; } }
        internal static object NativeHostPeer { get { return Runtime != null ? Runtime.NativeHostPeer : null; } }
        public static IReadOnlyList<BossRushCoopPlayer> Players { get { return Runtime != null ? Runtime.Players : emptyPlayers; } }
        public static bool AnyPlayerAlive
        {
            get
            {
                if (!IsConnected) return CharacterMainControl.Main != null && CharacterMainControl.Main.Health != null && !CharacterMainControl.Main.Health.IsDead;
                foreach (var player in Players) if (player.IsAlive) return true;
                return false;
            }
        }

        public static event Action SessionChanged;
        public static event Action<BossRushCoopPlayer> PlayerSpawned;
        public static event Action<string> PlayerDisconnected;
        public static event Action<int, CharacterMainControl> AiSpawned;
        public static event Action<string> SceneReadyChanged;
        public static event Action<Item, Dictionary<string, string>> ItemSnapshotRequested;
        public static event Action<Item, IReadOnlyDictionary<string, string>> ItemSnapshotApplied;
        public static event Action<string, string, string> ControlLeaseChanged;

        public static BossRushCoopPlayer FindPlayer(string id)
        {
            foreach (var player in Players) if (string.Equals(player.Id, id, StringComparison.Ordinal)) return player;
            return null;
        }

        public static string GetPlayerId(CharacterMainControl character)
        {
            if (character == null) return null;
            if (character == CharacterMainControl.Main) return LocalPlayerId;
            foreach (var player in Players) if (player.Character == character) return player.Id;
            return null;
        }

        public static bool IsPlayer(CharacterMainControl character) { return !string.IsNullOrEmpty(GetPlayerId(character)); }
        public static CharacterMainControl GetCombatTarget(Vector3 origin)
        {
            if (!IsConnected) return CharacterMainControl.Main;
            CharacterMainControl nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var player in Players)
            {
                if (!player.IsAlive) continue;
                float candidate = (player.Character.transform.position - origin).sqrMagnitude;
                if (candidate < distance) { nearest = player.Character; distance = candidate; }
            }
            return nearest;
        }

        public static int GetAiId(CharacterMainControl character) { return Runtime != null ? Runtime.GetAiId(character) : 0; }
        public static CharacterMainControl FindAi(int id) { return Runtime != null ? Runtime.FindAi(id) : null; }
        public static bool RequestAiRegistration(CharacterMainControl character)
        {
            CoopAiPredictionHealthProtection.TrackAuthority(character);
            return Runtime != null && Runtime.RegisterAi(character);
        }
        public static string GetEntityId(CharacterMainControl character)
        {
            string player = GetPlayerId(character);
            if (!string.IsNullOrEmpty(player)) return "p:" + player;
            int ai = GetAiId(character);
            return ai != 0 ? "a:" + ai.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        public static CharacterMainControl FindEntity(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("p:", StringComparison.Ordinal)) { var player = FindPlayer(id.Substring(2)); return player != null ? player.Character : null; }
            int ai;
            return id.StartsWith("a:", StringComparison.Ordinal) && int.TryParse(id.Substring(2), out ai) ? FindAi(ai) : null;
        }

        public static IDisposable Register(string topic, Action<BossRushCoopMessage> handler)
        {
            if (Runtime == null) throw new InvalidOperationException("Coop runtime must be registered before consumers.");
            return Runtime.Register(topic, handler);
        }
        public static bool SendToHost<T>(string topic, T payload) { return Runtime != null && Runtime.SendToHost(topic, BossRushCoopJson.ToJson(payload)); }
        public static bool SendToPlayer<T>(string playerId, string topic, T payload) { return Runtime != null && Runtime.SendToPlayer(playerId, topic, BossRushCoopJson.ToJson(payload)); }
        public static bool Broadcast<T>(string topic, T payload) { return Runtime != null && Runtime.Broadcast(topic, BossRushCoopJson.ToJson(payload)); }
        public static void MarkSceneReady(string key) { if (Runtime != null) Runtime.MarkSceneReady(key); }
        public static bool AllPlayersSceneReady(string key) { return !IsConnected || (Runtime != null && Runtime.AllPlayersSceneReady(key)); }
        public static bool ArePlayersSceneReady(string key, IEnumerable<string> participants)
        { return !IsConnected || (Runtime != null && Runtime.ArePlayersSceneReady(key, participants)); }
        public static bool RegisterWorldLoot(InteractableLootbox box) { return Runtime != null && Runtime.RegisterWorldLoot(box); }
        public static bool RegisterWorldDestructible(HealthSimpleBase health, string entityKey) { return Runtime != null && Runtime.RegisterWorldDestructible(health, entityKey); }
        public static string BeginControlLease(string playerId, string entityId) { return Runtime != null ? Runtime.BeginControlLease(playerId, entityId) : null; }
        public static void EndControlLease(string leaseId) { if (Runtime != null) Runtime.EndControlLease(leaseId); }
        public static bool HasControlLease(string leaseId) { return Runtime != null && Runtime.HasControlLease(leaseId); }
        public static bool SubmitControlInput(string leaseId, Vector3 move, Vector3 aim, bool fire, bool dash, bool reload, int weaponSlot)
        { return Runtime != null && Runtime.SubmitControlInput(leaseId, move, aim, fire, dash, reload, weaponSlot); }
        internal static void RaiseControlLeaseChanged(string player, string lease, string entity)
        { InvokeEach(ControlLeaseChanged, handler => ((Action<string, string, string>)handler)(player, lease, entity)); }
        internal static void RaiseSessionChanged() { InvokeEach(SessionChanged, handler => ((Action)handler)()); }
        internal static void RaisePlayerSpawned(BossRushCoopPlayer player)
        { InvokeEach(PlayerSpawned, handler => ((Action<BossRushCoopPlayer>)handler)(player)); }
        internal static void RaisePlayerDisconnected(string id)
        { InvokeEach(PlayerDisconnected, handler => ((Action<string>)handler)(id)); }
        internal static void RaiseAiSpawned(int id, CharacterMainControl character)
        { InvokeEach(AiSpawned, handler => ((Action<int, CharacterMainControl>)handler)(id, character)); }
        internal static void RaiseSceneReadyChanged(string key)
        { InvokeEach(SceneReadyChanged, handler => ((Action<string>)handler)(key)); }
        internal static void RaiseItemRequested(Item item, Dictionary<string, string> data)
        { InvokeEach(ItemSnapshotRequested, handler => ((Action<Item, Dictionary<string, string>>)handler)(item, data)); }
        internal static void RaiseItemApplied(Item item, IReadOnlyDictionary<string, string> data)
        { InvokeEach(ItemSnapshotApplied, handler => ((Action<Item, IReadOnlyDictionary<string, string>>)handler)(item, data)); }
        private static void InvokeEach(Delegate handlers, Action<Delegate> invoke)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { invoke(handler); }
                catch (Exception e) { ModBehaviour.CriticalLog("coop-event-" + e.GetType().Name, "[BossRush Coop] Event failed: " + e.Message); }
            }
        }
        internal static void Shutdown(BossRushCoopRuntimeModule owner)
        {
            if (Runtime != owner) return;
            Runtime = null;
            SessionChanged = null; PlayerSpawned = null; PlayerDisconnected = null; AiSpawned = null;
            SceneReadyChanged = null; ItemSnapshotRequested = null; ItemSnapshotApplied = null;
            ControlLeaseChanged = null;
        }
    }
}

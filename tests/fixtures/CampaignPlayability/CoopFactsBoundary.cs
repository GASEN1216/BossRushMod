using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
}

namespace BossRush
{
    // Explicit mutable facade inputs; no native scene, health, wallet or disk is simulated as proven.
    internal static partial class BossRushCoop
    {
        internal static bool IsConnected, IsHost, HasBoundScene;
        internal static bool IsClient { get { return IsConnected && !IsHost; } }
        internal static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        internal static bool SessionReady = true, AnyPlayerAlive = true;
        internal static string LocalPlayerId = "local", LocalAssetId = "offline", RunId = string.Empty, SceneToken;
        internal static int SceneGeneration;
        internal static event Action SessionChanged;
        internal static event Action<string> PlayerDisconnected, SceneReadyChanged;
        internal static readonly List<BossRushCoopPlayer> Players = new List<BossRushCoopPlayer>();
        internal static BossRushCoopPlayer FindPlayer(string id) { return Players.Find(value => value.Id == id); }
        internal static string GetEntityId(CharacterMainControl value) { return value == null ? null : value.GetInstanceID().ToString(); }
        internal static CharacterMainControl FindEntity(string id) { return null; }
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> action) { return CampaignFactNetwork.Register(topic, action); }
        internal static bool SendToHost<T>(string topic, T value) { return CampaignFactNetwork.Send("host", topic, value); }
        internal static bool SendToPlayer<T>(string player, string topic, T value) { return CampaignFactNetwork.Send(player, topic, value); }
        internal static bool Broadcast<T>(string topic, T value) { return CampaignFactNetwork.Send("all", topic, value); }
        internal static void MarkSceneReady(string key) { }
        internal static bool AllPlayersSceneReady(string key) { return !IsConnected; }
    }
    internal sealed class BossRushCoopPlayer
    {
        internal string Id, AssetId;
        internal bool IsConnected, IsAlive, IsLocal;
        internal CharacterMainControl Character;
    }
    internal sealed class BossRushCoopMessage
    {
        internal string SenderId, Topic, Json;
        internal bool IsFromHost;
        internal T Read<T>() { return BossRushCoopJson.FromJson<T>(Json); }
    }
    internal static class CampaignFactNetwork
    {
        internal sealed class Frame { internal string Player, Topic, Json; }
        private sealed class Subscription : IDisposable
        {
            internal string Topic;
            internal Action<BossRushCoopMessage> Handler;
            public void Dispose() { List<Action<BossRushCoopMessage>> list; if (handlers.TryGetValue(Topic, out list)) list.Remove(Handler); }
        }
        private static readonly Dictionary<string, List<Action<BossRushCoopMessage>>> handlers = new Dictionary<string, List<Action<BossRushCoopMessage>>>();
        internal static readonly List<Frame> Sent = new List<Frame>();
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> handler)
        {
            List<Action<BossRushCoopMessage>> list;
            if (!handlers.TryGetValue(topic, out list)) handlers[topic] = list = new List<Action<BossRushCoopMessage>>();
            list.Add(handler);
            return new Subscription { Topic = topic, Handler = handler };
        }
        internal static bool Send<T>(string player, string topic, T value)
        {
            if (!BossRushCoop.IsConnected) return false;
            Sent.Add(new Frame { Player = player, Topic = topic, Json = BossRushCoopJson.ToJson(value) });
            return true;
        }
        internal static void Inject(Frame frame, bool host, string sender)
        {
            List<Action<BossRushCoopMessage>> list;
            if (!handlers.TryGetValue(frame.Topic, out list)) return;
            foreach (var handler in list.ToArray()) handler(new BossRushCoopMessage { Topic = frame.Topic, Json = frame.Json, IsFromHost = host, SenderId = sender });
        }
        internal static void Reset() { handlers.Clear(); Sent.Clear(); BossRushCoop.Players.Clear(); }
    }
    public partial class ModBehaviour
    {
        private readonly ModeDRuntimeModule modeDRuntime = new ModeDRuntimeModule();
        private readonly ModeERuntimeModule modeERuntime = new ModeERuntimeModule();
        private readonly ZombieModeRuntimeModule zombieModeRuntimeModule = new ZombieModeRuntimeModule();
        internal void SetCampaignEntryForTest(string epoch)
        {
            modeDRuntime.SetEpochForTest(epoch); modeERuntime.SetEpochForTest(epoch);
            modeFRuntime.SetEpochForTest(epoch); wavesArenaRuntime.SetEpochForTest(epoch);
        }
        internal void AdmitCampaignForTest(string id, string asset)
        {
            modeDRuntime.SetParticipantForTest(id, asset); modeERuntime.SetParticipantForTest(id, asset);
            modeFRuntime.SetParticipantForTest(id, asset); wavesArenaRuntime.SetParticipantForTest(id, asset);
            zombieModeRuntimeModule.runState = zombieModeRunState;
            zombieModeRuntimeModule.coopZombieParticipants[id] = asset;
            zombieModeRuntimeModule.coopZombieStarterReady.Add(id + ":" + asset);
        }
    }
    internal sealed partial class CampaignRuntimeModule
    {
        internal void InitFactsForTest() { InitializeCoopCampaign(); }
        internal CoopCampaignWorld CaptureWorldForTest() { return CaptureCoopCampaignWorld(); }
        internal void ApplyWorldForTest(CoopCampaignWorld value) { ApplyCoopCampaignWorld(value); }
        internal bool ApplyFactForTest(CoopCampaignFact value) { return ApplyCoopCampaignFact(value); }
        internal CoopCampaignFact FactForTest(string kind, string id, bool melee = false, bool boss = false, bool bounty = false, int wave = 0)
        {
            return new CoopCampaignFact { Kind = kind, FactId = id, Mode = kind == "final" ? CampaignContentCatalog.ModeFinal : ResolveCampaignWorldMode(),
                Scope = ResolveCoopCampaignEntry(kind == "final" ? CampaignContentCatalog.ModeFinal : ResolveCampaignWorldMode()),
                SceneToken = BossRushCoop.SceneToken, RunId = BossRushCoop.RunId, SceneGeneration = BossRushCoop.SceneGeneration,
                Melee = melee, Boss = boss, Bounty = bounty, Wave = wave };
        }
        internal bool DeliverForTest(string id, CoopCampaignFact value) { return DeliverCoopCampaignFact(id, value); }
        internal void ResetProjectionForTest() { ResetCoopCampaignProjection(); }
        internal void FactsTickForTest() { coopFacts.Tick(1f); }
        internal bool FactsPendingForTest { get { return coopFacts.HasPendingDeliveries; } }
        internal int FactLeaseCountForTest { get { return coopCampaignLeases.Count; } }
        internal int EntryTokenCountForTest { get { return coopCampaignEntryTokens.Count; } }
        internal void FactsCleanupForTest() { if (coopFacts != null) coopFacts.Dispose(); CleanupCoopCampaignFacts(); }
    }
}

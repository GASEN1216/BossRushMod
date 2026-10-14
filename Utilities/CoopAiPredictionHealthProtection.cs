using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>只有玩法 owner 明确登记的远端 AI 才保护预测生命；房主快照仍可写入真实死亡。</summary>
    internal static class CoopAiPredictionHealthProtection
    {
        [ThreadStatic] private static Health predicting;
        internal static CoopAiPredictionHealthRuntimeModule Owner;
        internal static void TrackAuthority(CharacterMainControl character)
        {
            if (!BossRushCoop.IsHost || character == null) return;
            CoopAiPredictionReplicaMarker marker = character.GetComponent<CoopAiPredictionReplicaMarker>();
            if (marker == null) marker = character.gameObject.AddComponent<CoopAiPredictionReplicaMarker>();
            if (marker.Replica) return;
            if (!marker.AuthorityBound)
            { marker.AuthorityBound = true; marker.AuthorityRun = BossRushCoop.RunId; }
            if (Owner != null && HasEntityAuthority(character)) Owner.Track(character);
        }
        internal static bool HasEntityAuthority(CharacterMainControl character)
        {
            if (character == null) return false;
            CoopAiPredictionReplicaMarker marker = character.GetComponent<CoopAiPredictionReplicaMarker>();
            if (marker != null && marker.Replica) return false;
            if (!BossRushCoop.HasWorldAuthority) return false;
            return marker == null || !marker.AuthorityBound
                || BossRushCoop.IsHost && !string.IsNullOrEmpty(marker.AuthorityRun) && marker.AuthorityRun == BossRushCoop.RunId;
        }
        internal static void MarkReplica(CharacterMainControl character)
        {
            if (!BossRushCoop.IsClient || character == null) return;
            CoopAiPredictionReplicaMarker marker = character.GetComponent<CoopAiPredictionReplicaMarker>();
            if (marker == null) marker = character.gameObject.AddComponent<CoopAiPredictionReplicaMarker>();
            // 这是实体出生归属；停止网络或清理预测 owner 都不能把旧镜像变成单机世界实体。
            marker.Replica = true;
            if (Owner == null) return;
            marker.Armed = true; marker.Owner = Owner; Owner.Remember(marker);
        }
        internal static Health Begin(Health health, CharacterMainControl attacker)
        {
            Health previous = predicting; predicting = null;
            if (!BossRushCoop.IsClient || health == null || attacker == null
                || BossRushCoop.GetPlayerId(attacker) != BossRushCoop.LocalPlayerId) return previous;
            CharacterMainControl character = health.TryGetCharacter();
            CoopAiPredictionReplicaMarker marker = character != null ? character.GetComponent<CoopAiPredictionReplicaMarker>() : null;
            if (marker != null && marker.Armed && marker.Owner == Owner) predicting = health;
            return previous;
        }
        internal static void End(Health previous) { predicting = previous; }
        internal static bool TryClamp(Health health, ref float value)
        {
            if (!BossRushCoop.IsClient || health == null || health != predicting || value > 0f) return false;
            value = 1f; return true;
        }
        internal static void ResetStaticCaches() { predicting = null; Owner = null; }
    }

    internal sealed class CoopAiPredictionReplicaMarker : MonoBehaviour
    {
        internal bool Armed, Replica, AuthorityBound;
        internal string AuthorityRun;
        internal CoopAiPredictionHealthRuntimeModule Owner;
    }

    [HarmonyPatch(typeof(Health), nameof(Health.Hurt), new Type[] { typeof(DamageInfo) })]
    internal static class CoopAiPredictionHealthHurtPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Health __instance, DamageInfo damageInfo, out Health __state)
        { __state = CoopAiPredictionHealthProtection.Begin(__instance, damageInfo.fromCharacter); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Health __state)
        { CoopAiPredictionHealthProtection.End(__state); return __exception; }
    }

    internal sealed class CoopAiPredictionHealthRuntimeModule : BossRushRuntimeModuleBase
    {
        public override string ModuleName { get { return "CoopAiPredictionHealth"; } }
        // JsonUtility 的字段由网络填充。
#pragma warning disable 0649
        [Serializable] private sealed class AuthorityState { public string Run; public int Generation; public int[] Ids; }
#pragma warning restore 0649
        private sealed class Authority { internal CharacterMainControl Character; internal int Id; }
        private readonly List<Authority> authorities = new List<Authority>();
        private readonly HashSet<CharacterMainControl> tracked = new HashSet<CharacterMainControl>();
        private readonly Dictionary<int, CharacterMainControl> bound = new Dictionary<int, CharacterMainControl>();
        private readonly HashSet<CoopAiPredictionReplicaMarker> markers = new HashSet<CoopAiPredictionReplicaMarker>();
        private AuthorityState latest;
        private IDisposable subscription;
        private string run;
        private int generation;
        private float nextTick, nextPublish;
        public override void OnAwake(ModBehaviour owner)
        {
            CoopAiPredictionHealthProtection.Owner = this;
            subscription = BossRushCoop.Register("ai.predict", Receive);
            BossRushCoop.AiSpawned += OnAiSpawned;
        }
        internal void Track(CharacterMainControl character)
        {
            RefreshWorld();
            if (character != null && tracked.Add(character))
            { authorities.Add(new Authority { Character = character }); nextTick = nextPublish = 0f; }
        }
        internal void Remember(CoopAiPredictionReplicaMarker marker) { if (marker != null) markers.Add(marker); }
        private void OnAiSpawned(int id, CharacterMainControl character)
        {
            if (id == 0 || character == null || !BossRushCoop.SessionReady) return;
            if (BossRushCoop.IsHost && tracked.Contains(character))
            {
                foreach (Authority entry in authorities) if (entry.Character == character) { entry.Id = id; break; }
                PublishAuthority();
            }
            else if (BossRushCoop.IsClient && latest != null && latest.Run == BossRushCoop.RunId
                && latest.Generation == BossRushCoop.SceneGeneration && Array.IndexOf(latest.Ids, id) >= 0)
                BindReplica(id, character);
        }
        private void Receive(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            RefreshWorld();
            AuthorityState state = message.Read<AuthorityState>();
            if (state == null || state.Run != run || state.Generation != generation || state.Ids == null || state.Ids.Length > 4096) return;
            latest = state; nextTick = 0f;
            foreach (int id in state.Ids)
            { CharacterMainControl character = BossRushCoop.FindAi(id); if (id != 0 && character != null) BindReplica(id, character); }
        }
        private void RefreshWorld()
        {
            if (run == BossRushCoop.RunId && generation == BossRushCoop.SceneGeneration) return;
            ClearWorld(); run = BossRushCoop.RunId; generation = BossRushCoop.SceneGeneration;
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            RefreshWorld();
            if (!BossRushCoop.SessionReady || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.2f;
            if (BossRushCoop.IsHost)
            {
                for (int index = authorities.Count - 1; index >= 0; index--)
                {
                    Authority entry = authorities[index];
                    if (entry.Character == null) { tracked.Remove(entry.Character); authorities.RemoveAt(index); continue; }
                    if (entry.Id == 0) entry.Id = BossRushCoop.GetAiId(entry.Character);
                }
                if (Time.unscaledTime < nextPublish) return;
                nextPublish = Time.unscaledTime + 0.5f;
                PublishAuthority();
            }
            else if (latest != null)
            {
                foreach (int id in latest.Ids)
                {
                    if (id == 0) continue;
                    CharacterMainControl character = BossRushCoop.FindAi(id); if (character == null) continue;
                    BindReplica(id, character);
                }
            }
        }
        private void PublishAuthority()
        {
            var ids = new List<int>(authorities.Count);
            foreach (Authority entry in authorities) if (entry.Id != 0) ids.Add(entry.Id);
            BossRushCoop.Broadcast("ai.predict", new AuthorityState { Run = run, Generation = generation, Ids = ids.ToArray() });
        }
        private void BindReplica(int id, CharacterMainControl character)
        {
            CharacterMainControl previous;
            if (bound.TryGetValue(id, out previous) && previous == character) return;
            if (previous != null)
            {
                CoopAiPredictionReplicaMarker old = previous.GetComponent<CoopAiPredictionReplicaMarker>();
                if (old != null && old.Owner == this) { old.Armed = false; old.Owner = null; markers.Remove(old); }
            }
            CoopAiPredictionHealthProtection.MarkReplica(character);
            bound[id] = character;
        }
        private void ClearWorld()
        {
            // 组件随原生实体释放；先即时撤销资格，避免延迟 Destroy 删除本帧的新绑定。
            foreach (CoopAiPredictionReplicaMarker marker in markers)
                if (marker != null && marker.Owner == this) { marker.Armed = false; marker.Owner = null; }
            authorities.Clear(); tracked.Clear(); bound.Clear(); markers.Clear(); latest = null;
            nextTick = nextPublish = 0f;
        }
        public override void OnDestroy()
        {
            if (subscription != null) subscription.Dispose(); subscription = null;
            BossRushCoop.AiSpawned -= OnAiSpawned;
            ClearWorld();
            if (CoopAiPredictionHealthProtection.Owner == this) CoopAiPredictionHealthProtection.ResetStaticCaches();
        }
    }
}

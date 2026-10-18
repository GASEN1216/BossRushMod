using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopZombieZoneSlow
    {
        public int Run, Wave, Generation;
        public string SceneToken;
        public Vector3 Origin;
        public float Radius, Percent, Duration, Validity;
        public long Sequence;
        public string Nonce;
    }

    [Serializable]
    internal sealed class CoopZombieZoneProbe
    {
        public int Run, Wave;
        public long Sequence;
        public string Nonce;
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private CoopPersonalDelivery<CoopZombieZoneSlow> coopZombieZoneDelivery;
        private int coopZombieZoneWave = -1;
        private long coopZombieZoneSequence;
        private IDisposable coopZombieZoneOfferSubscription, coopZombieZoneProbeSubscription;
        private sealed class ZombieZoneFact
        {
            internal CoopZombieZoneSlow Effect;
            internal string Player, Asset;
            internal float Expires;
            internal ZombieModeAreaTickRuntime Area;
            internal bool Persistent;
            internal bool Sent;
            internal float LastOfferedAt;
        }
        private sealed class ZombieZonePending
        {
            internal CoopZombieZoneSlow Effect;
            internal string Nonce;
            internal float OfferedAt, RequestedAt, LastRequestedAt;
            internal bool Requested;
        }
        private readonly Dictionary<long, ZombieZoneFact> coopZombieZoneFacts = new Dictionary<long, ZombieZoneFact>();
        private readonly Dictionary<long, ZombieZonePending> coopZombieZonePending = new Dictionary<long, ZombieZonePending>();

        private void InitializeCoopZombieZoneEffects()
        {
            if (coopZombieZoneDelivery != null) return;
            coopZombieZoneWave = runState.CurrentWave;
            coopZombieZoneDelivery = new CoopPersonalDelivery<CoopZombieZoneSlow>("ZombieZones", ApplyCoopZombieZoneSlow);
            coopZombieZoneOfferSubscription = BossRushCoop.Register("BossRush.Zombie.ZoneOffer", ReceiveCoopZombieZoneOffer);
            coopZombieZoneProbeSubscription = BossRushCoop.Register("BossRush.Zombie.ZoneProbe", ConfirmCoopZombieZoneProbe);
        }

        private void TickCoopZombieZoneEffects(float deltaTime)
        {
            // 高频区域收据只保留当前波次，换波时释放旧重试和幂等记录。
            if (coopZombieZoneDelivery == null) return;
            if (coopZombieZoneWave != runState.CurrentWave) ResetCoopZombieZoneEffects(true);
            coopZombieZoneDelivery.Tick(deltaTime);
            if (coopZombieZoneFacts.Count > 0)
            {
                foreach (var pair in new List<KeyValuePair<long, ZombieZoneFact>>(coopZombieZoneFacts))
                {
                    ZombieZoneFact fact = pair.Value;
                    BossRushCoopPlayer player = BossRushCoop.FindPlayer(fact.Player);
                    if (GetCoopZombieZoneFactRemaining(fact) <= 0f || !IsCoopZombieZoneCurrent(fact.Effect) || !IsCoopZombieParticipant(player)
                        || player.AssetId != fact.Asset || !player.IsAlive) coopZombieZoneFacts.Remove(pair.Key);
                    else if (!fact.Sent || Time.unscaledTime - fact.LastOfferedAt >= 0.25f)
                    {
                        fact.Sent = BossRushCoop.SendToPlayer(fact.Player, "BossRush.Zombie.ZoneOffer", fact.Effect);
                        fact.LastOfferedAt = Time.unscaledTime;
                    }
                }
            }
            if (coopZombieZonePending.Count > 0)
                foreach (var pair in new List<KeyValuePair<long, ZombieZonePending>>(coopZombieZonePending))
                {
                    ZombieZonePending pending = pair.Value;
                    if (!IsCoopZombieZoneCurrent(pending.Effect) || Time.unscaledTime - pending.OfferedAt >= GetCoopZombieZoneValidity(pending.Effect))
                        coopZombieZonePending.Remove(pair.Key);
                    else TryRequestCoopZombieZoneConfirmation(pending);
                }
        }

        private void ResetCoopZombieZoneEffects(bool keepCurrentWaveOffers = false)
        {
            if (coopZombieZoneDelivery == null) return;
            coopZombieZoneDelivery.Dispose();
            coopZombieZoneWave = runState.CurrentWave;
            coopZombieZoneDelivery = new CoopPersonalDelivery<CoopZombieZoneSlow>("ZombieZones", ApplyCoopZombieZoneSlow);
            coopZombieZoneFacts.Clear();
            if (!keepCurrentWaveOffers) coopZombieZonePending.Clear();
            else if (coopZombieZonePending.Count > 0)
                foreach (var pair in new List<KeyValuePair<long, ZombieZonePending>>(coopZombieZonePending))
                    if (!IsCoopZombieZoneCurrent(pair.Value.Effect) || pair.Value.Effect.Wave != runState.CurrentWave)
                        coopZombieZonePending.Remove(pair.Key);
        }

        private void CleanupCoopZombieZoneEffects()
        {
            if (coopZombieZoneDelivery != null) coopZombieZoneDelivery.Dispose();
            coopZombieZoneDelivery = null;
            if (coopZombieZoneOfferSubscription != null) coopZombieZoneOfferSubscription.Dispose();
            if (coopZombieZoneProbeSubscription != null) coopZombieZoneProbeSubscription.Dispose();
            coopZombieZoneOfferSubscription = coopZombieZoneProbeSubscription = null;
            coopZombieZoneFacts.Clear();
            coopZombieZonePending.Clear();
            coopZombieZoneWave = -1;
            coopZombieZoneSequence = 0;
        }

        // 区域只由世界 owner 结算；客端移动属性通过下面的个人收据应用。
        private IEnumerable<CharacterMainControl> EnumerateZombieModeZoneTargets(int runId)
        {
            if (!IsZombieModeRunValid(runId) || !BossRushCoop.HasWorldAuthority) yield break;
            if (!BossRushCoop.IsConnected)
            {
                CharacterMainControl local = CharacterMainControl.Main;
                if (local != null && local.Health != null && !local.Health.IsDead) yield return local;
                yield break;
            }
            if (!BossRushCoop.HasBoundScene) yield break;
            foreach (BossRushCoopPlayer participant in BossRushCoop.Players)
            {
                if (!IsZombieModeRunValid(runId) || !BossRushCoop.HasWorldAuthority || !BossRushCoop.HasBoundScene) yield break;
                if (IsCoopZombieParticipant(participant) && participant.IsAlive)
                    yield return participant.Character;
            }
        }

        private void SendCoopZombieZoneSlow(int runId, CharacterMainControl target, Vector3 origin,
            float radius, float percent, float duration, ZombieModeAreaTickRuntime area)
        {
            if (!BossRushCoop.IsHost || coopZombieZoneDelivery == null) return;
            BossRushCoopPlayer participant = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(target));
            if (!IsCoopZombieParticipant(participant) || !participant.IsAlive || participant.IsLocal) return;
            if (coopZombieZoneWave != runState.CurrentWave) ResetCoopZombieZoneEffects(true);
            float validity = area != null ? area.CoopSlowRemainingSeconds : duration;
            if (validity <= 0f) return;
            var effect = new CoopZombieZoneSlow { Run = runId, Wave = runState.CurrentWave,
                    Generation = BossRushCoop.SceneGeneration, SceneToken = BossRushCoop.SceneToken,
                    Origin = origin, Radius = radius, Percent = percent, Duration = duration, Validity = validity, Sequence = ++coopZombieZoneSequence };
            var fact = new ZombieZoneFact { Effect = effect, Player = participant.Id, Asset = coopZombieParticipants[participant.Id],
                Expires = GetZombieModeRuntimeNow() + duration, Area = area, Persistent = area != null };
            coopZombieZoneFacts.Add(effect.Sequence, fact);
            fact.Sent = BossRushCoop.SendToPlayer(participant.Id, "BossRush.Zombie.ZoneOffer", effect);
            fact.LastOfferedAt = Time.unscaledTime;
        }

        private static float GetCoopZombieZoneValidity(CoopZombieZoneSlow effect)
        {
            return effect.Validity > 0f ? effect.Validity : effect.Duration;
        }

        private float GetCoopZombieZoneFactRemaining(ZombieZoneFact fact)
        {
            if (!fact.Persistent) return fact.Expires - GetZombieModeRuntimeNow();
            // A destroyed/disabled zone cannot turn an old tick into a new timed effect.
            return fact.Area != null ? fact.Area.CoopSlowRemainingSeconds : 0f;
        }

        private bool IsCoopZombieZoneCurrent(CoopZombieZoneSlow effect)
        {
            return effect.Run == runState.RunId && effect.Wave >= runState.CurrentWave
                && effect.Generation == BossRushCoop.SceneGeneration && effect.SceneToken == BossRushCoop.SceneToken
                && IsCoopZombieLocalAssetCurrent() && IsZombieModeRunValid(effect.Run);
        }

        private void ReceiveCoopZombieZoneOffer(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || message == null || !message.IsFromHost) return;
            CoopZombieZoneSlow effect = message.Read<CoopZombieZoneSlow>();
            if (effect == null || !IsCoopZombieZoneCurrent(effect) || effect.Duration <= 0f || coopZombieZonePending.ContainsKey(effect.Sequence)) return;
            var pending = new ZombieZonePending { Effect = effect, Nonce = Guid.NewGuid().ToString("N"), OfferedAt = Time.unscaledTime };
            coopZombieZonePending.Add(effect.Sequence, pending);
            TryRequestCoopZombieZoneConfirmation(pending);
        }

        private void TryRequestCoopZombieZoneConfirmation(ZombieZonePending pending)
        {
            if (pending.Effect.Wave != runState.CurrentWave || !BossRushCoop.HasBoundScene || IsZombieModeRuntimePaused()) return;
            float now = Time.unscaledTime;
            if (pending.Requested && now - pending.LastRequestedAt < 0.25f) return;
            // Retrying keeps the original nonce clock; packet loss never grants extra lifetime.
            if (!pending.Requested) pending.RequestedAt = now;
            pending.LastRequestedAt = now;
            pending.Requested |= BossRushCoop.SendToHost("BossRush.Zombie.ZoneProbe", new CoopZombieZoneProbe
            { Run = pending.Effect.Run, Wave = pending.Effect.Wave, Sequence = pending.Effect.Sequence, Nonce = pending.Nonce });
        }

        private void ConfirmCoopZombieZoneProbe(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || message == null || !BossRushCoop.HasBoundScene || IsZombieModeRuntimePaused()
                || ZombieModePhaseGuards.ShouldPauseModePressure(runState.CombatPhase)) return;
            CoopZombieZoneProbe probe = message.Read<CoopZombieZoneProbe>();
            ZombieZoneFact fact;
            if (probe == null || string.IsNullOrEmpty(probe.Nonce) || probe.Nonce.Length > 80
                || !coopZombieZoneFacts.TryGetValue(probe.Sequence, out fact) || fact.Player != message.SenderId) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            CoopZombieZoneSlow effect = fact.Effect;
            float remaining = GetCoopZombieZoneFactRemaining(fact);
            if (probe.Run != effect.Run || probe.Wave != effect.Wave || effect.Wave != runState.CurrentWave
                || !IsCoopZombieZoneCurrent(effect) || !IsCoopZombieParticipant(player) || !player.IsAlive
                || player.AssetId != fact.Asset || remaining <= 0f) return;
            Vector3 origin = fact.Persistent ? fact.Area.CoopSlowOrigin : effect.Origin;
            if (fact.Persistent && (player.Character.transform.position - origin).sqrMagnitude > effect.Radius * effect.Radius) return;
            var confirmed = new CoopZombieZoneSlow { Run = effect.Run, Wave = effect.Wave, Generation = effect.Generation,
                SceneToken = effect.SceneToken, Origin = origin, Radius = effect.Radius, Percent = effect.Percent,
                Duration = fact.Persistent ? effect.Duration : remaining, Validity = remaining, Sequence = effect.Sequence, Nonce = probe.Nonce };
            if (coopZombieZoneDelivery.Deliver(player.Id, effect.Run + ":zone:" + effect.Sequence, confirmed, fact.Asset))
                coopZombieZoneFacts.Remove(probe.Sequence);
        }

        private bool ApplyCoopZombieZoneSlow(CoopZombieZoneSlow effect)
        {
            if (effect == null) return false;
            if (effect.Run != runState.RunId || effect.Wave < runState.CurrentWave
                || effect.Generation != BossRushCoop.SceneGeneration || effect.SceneToken != BossRushCoop.SceneToken
                || !IsCoopZombieLocalAssetCurrent() || !IsZombieModeRunValid(effect.Run))
            { coopZombieZonePending.Remove(effect.Sequence); return true; }
            if (effect.Wave > runState.CurrentWave || !BossRushCoop.HasBoundScene) return false;
            if (ZombieModePhaseGuards.ShouldPauseModePressure(runState.CombatPhase))
            { coopZombieZonePending.Remove(effect.Sequence); return true; }
            if (IsZombieModeRuntimePaused()) return false;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null) return false;
            ZombieZonePending pending;
            if (!coopZombieZonePending.TryGetValue(effect.Sequence, out pending) || !pending.Requested || pending.Nonce != effect.Nonce) return true;
            coopZombieZonePending.Remove(effect.Sequence);
            // Duration is the original tick strength; Validity is the actual zone lifetime.
            // Subtract the full nonce RTT from that lifetime without assuming symmetric latency.
            float duration = Mathf.Min(effect.Duration,
                GetCoopZombieZoneValidity(effect) - Mathf.Max(0f, Time.unscaledTime - pending.RequestedAt));
            if (duration <= 0f) return true;
            // 已离开区域或死亡时消费旧事实，不把重试变成追踪减速。
            if (player.Health.IsDead || (player.transform.position - effect.Origin).sqrMagnitude > effect.Radius * effect.Radius) return true;
            TryApplyZombieModePlayerSlow(effect.Run, effect.Percent, duration);
            return true;
        }
    }
}

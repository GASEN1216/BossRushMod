using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        private bool coopJoining;
        private bool coopScopeConnected, coopScopeHost, coopScopeLost;
        private string coopScopeRun;

        private void CaptureCoopSessionScope()
        {
            coopScopeConnected = BossRushCoop.IsConnected;
            coopScopeHost = BossRushCoop.IsHost;
            coopScopeRun = BossRushCoop.RunId;
            coopScopeLost = false;
        }
        private bool IsCoopSessionScopeValid()
        {
            if (coopScopeLost) return false;
            if (coopScopeConnected != BossRushCoop.IsConnected
                || coopScopeConnected && coopScopeHost != BossRushCoop.IsHost
                || coopScopeConnected && !string.IsNullOrEmpty(coopScopeRun) && !string.IsNullOrEmpty(BossRushCoop.RunId)
                    && !string.Equals(coopScopeRun, BossRushCoop.RunId, StringComparison.Ordinal))
                coopScopeLost = true;
            else if (coopScopeConnected && string.IsNullOrEmpty(coopScopeRun) && !string.IsNullOrEmpty(BossRushCoop.RunId))
                coopScopeRun = BossRushCoop.RunId;
            return !coopScopeLost;
        }
        private void OnCoopSessionChanged()
        {
            // StopNetwork 不卸载当前地图；原联网旅程只能清理，不能变成第二个离线世界。
            if (!IsCoopSessionScopeValid()) Cleanup("coop_session_lost");
        }
        internal bool CoopTravelActive { get { return !closed && !returning && !returnRequested; } }

        internal static void EnterCoopArrival(ModBehaviour owner, Action<string, bool> status)
        {
            if (owner == null || owner.GetComponent<SkyIslandSession>() != null) return;
            SkyIslandSession session = owner.gameObject.AddComponent<SkyIslandSession>();
            session.host = owner; session.report = status; session.coopJoining = true;
            session.lease = new SkyIslandRaidLease();
            try
            {
                // 此调用发生在官方加载方法入口；天气模板仍属于上一张完整关卡。
                session.lease.Prepare(ModBehaviour.GetModPath(), LevelConfig.Instance.timeOfDayConfig);
                session.Subscribe();
                session.StartCoroutine(session.BuildGuarded());
            }
            catch
            {
                session.Cleanup("coop_arrival_prepare_failed");
                throw;
            }
        }
        private static Transform Nearest(List<Transform> markers, Vector3 point)
        {
            Transform result = null;
            float distance = float.MaxValue;
            foreach (Transform marker in markers)
            {
                if (marker == null) continue;
                float value = (marker.position - point).sqrMagnitude;
                if (value < distance) { distance = value; result = marker; }
            }
            return result;
        }
        private static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }
        private static void Safe(string step, Action action)
        {
            try { action(); } catch (Exception e) { Debug.LogWarning("[SkyIsland] cleanup " + step + ": " + e.Message); }
        }
        private bool IsWorldSessionValid()
        {
            if (!IsCoopSessionScopeValid()) return false;
            return coopScopeConnected ? this != null && !closed && moved && !returning && root != null
                && entryScene.IsValid() && entryScene.isLoaded && !SceneLoader.IsSceneLoading && BossRushCoop.HasBoundScene && BossRushCoop.AnyPlayerAlive
                : IsSessionValid();
        }
        private void TickCoopWorldWhileDead()
        {
            try { TickCoop(); } catch (Exception e) { TickFault("coop", e); }
            try { if (encounters != null) encounters.Tick(); } catch (Exception e) { TickFault("encounters", e); }
            try { if (patrols != null) patrols.Tick(); } catch (Exception e) { TickFault("patrols", e); }
            try { if (scavenging != null) scavenging.Tick(); } catch (Exception e) { TickFault("scavenging", e); }
            try { if (SkyIslandFieldcraft.Current != null) SkyIslandFieldcraft.Current.TickCoopWorld(); } catch (Exception e) { TickFault("gnats", e); }
            try { if (gates != null) gates.Apply(CoopGateStory()); } catch (Exception e) { TickFault("gates", e); }
        }
        [Serializable] internal sealed class CoopChallenge { public string Id, TravelId, AssetId, RequestId, SceneToken; public bool Echo; }
        [Serializable] private sealed class CoopChallengeReply { public bool Accepted, Echo; public string Id, TravelId, AssetId, RequestId; }
        [Serializable] private sealed class CoopGateFlags { public string TravelId, SceneToken; public int Flags; }
        private IDisposable coopStateSubscription, coopChallengeSubscription, coopChallengeReplySubscription, coopGateSubscription;
        private SkyIslandCoopWorldEffects coopEffects;
        private SkyIslandEncounters.CoopState pendingCoopState;
        private float nextCoopState;
        private int coopWorldGateFlags;
        private readonly SkyIslandStoryData coopGateStory = new SkyIslandStoryData();
        private SkyIslandInventoryTransaction coopEchoCost;
        private CoopChallenge coopEchoRequest, coopNamedRequest;
        private float coopEchoRequestAt, coopNamedRequestAt;
        private CoopPersonalDelivery<CoopChallengeReply> coopChallengeDelivery;
        private readonly HashSet<string> coopCompletedChallenges = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopChallengeReply> coopChallengeReplies = new Dictionary<string, CoopChallengeReply>(StringComparer.Ordinal);
        private readonly System.Collections.Generic.Dictionary<string, int> coopEchoStarts = new System.Collections.Generic.Dictionary<string, int>();
        private readonly System.Collections.Generic.HashSet<string> coopCredited = new System.Collections.Generic.HashSet<string>();

        private void SubscribeCoop()
        {
            CaptureCoopSessionScope();
            BossRushCoop.SessionChanged += OnCoopSessionChanged;
            coopEffects = new SkyIslandCoopWorldEffects(delegate { return root != null && moved ? root.transform : null; });
            coopStateSubscription = BossRushCoop.Register("sky.encounters.state", OnCoopState);
            coopChallengeSubscription = BossRushCoop.Register("sky.challenge.request", OnCoopChallenge);
            coopChallengeReplySubscription = BossRushCoop.Register("sky.challenge.reply", OnCoopChallengeReply);
            coopChallengeDelivery = new CoopPersonalDelivery<CoopChallengeReply>("sky.echo", ApplyCoopChallengeReply);
            coopGateSubscription = BossRushCoop.Register("sky.gates.request", OnCoopGates);
        }

        private bool RequestCoopChallenge(string id)
        {
            if (coopNamedRequest != null) return false;
            coopNamedRequest = new CoopChallenge { Id = id, SceneToken = BossRushCoop.SceneToken, TravelId = SkyIslandCoopRuntime.Current.TravelId,
                AssetId = BossRushCoop.LocalAssetId, RequestId = Guid.NewGuid().ToString("N") };
            coopNamedRequestAt = 0f;
            return true;
        }
        private bool RequestCoopEcho(SkyIslandInventoryTransaction crystal)
        {
            coopEchoRequest = new CoopChallenge { Id = SkyIslandStormEchoRules.EncounterId, Echo = true,
                TravelId = SkyIslandCoopRuntime.Current.TravelId, SceneToken = BossRushCoop.SceneToken, AssetId = BossRushCoop.LocalAssetId, RequestId = Guid.NewGuid().ToString("N") };
            coopEchoCost = crystal; coopEchoRequestAt = 0f;
            return true;
        }

        private void OnCoopChallenge(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost) return;
            BossRushCoopPlayer requester = BossRushCoop.FindPlayer(message.SenderId);
            CoopChallenge data = message.Read<CoopChallenge>();
            if (requester == null || data == null || string.IsNullOrEmpty(data.AssetId) || data.AssetId.Length > 80
                || string.IsNullOrEmpty(data.RequestId) || data.RequestId.Length > 64 || string.IsNullOrEmpty(data.Id) || data.Id.Length > 64) return;
            string key = requester.Id + ":" + data.AssetId + ":" + data.RequestId;
            CoopChallengeReply reply;
            if (coopChallengeReplies.TryGetValue(key, out reply))
            { coopChallengeDelivery.Deliver(requester.Id, key, reply, data.AssetId); return; }
            string ownerKey = requester.Id + ":" + data.AssetId;
            int starts; coopEchoStarts.TryGetValue(ownerKey, out starts);
            bool accepted = IsWorldSessionValid() && encounters != null && requester.IsAlive && requester.Character != null
                && data.Echo == SkyIslandStormEchoRules.IsEcho(data.Id) && SkyIslandCoopRuntime.Current != null
                && data.SceneToken == BossRushCoop.SceneToken && data.TravelId == SkyIslandCoopRuntime.Current.TravelId
                && data.AssetId == requester.AssetId && !coopCompletedChallenges.Contains(ownerKey + ":" + data.Id)
                && (!data.Echo || starts < SkyIslandStormEchoRules.MaxPerRaid)
                && encounters.BeginRemoteChallenge(data.Id, requester.Character.transform.position, requester.Id, data.AssetId, data.RequestId);
            if (accepted)
            {
                nextCoopState = 0f;
                if (data.Echo) coopEchoStarts[ownerKey] = starts + 1;
            }
            reply = new CoopChallengeReply { Id = data.Id, Echo = data.Echo, Accepted = accepted, TravelId = data.TravelId,
                AssetId = data.AssetId, RequestId = data.RequestId };
            coopChallengeReplies[key] = reply;
            coopChallengeDelivery.Deliver(requester.Id, key, reply, data.AssetId);
        }

        private void OnCoopChallengeReply(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost || SkyIslandCoopRuntime.Current == null) return;
            CoopChallengeReply data = message.Read<CoopChallengeReply>();
            ApplyCoopChallengeReply(data);
        }
        private bool ApplyCoopChallengeReply(CoopChallengeReply data)
        {
            if (data == null || data.AssetId != BossRushCoop.LocalAssetId || SkyIslandCoopRuntime.Current == null
                || data.TravelId != SkyIslandCoopRuntime.Current.TravelId) return false;
            CoopChallenge pending = data.Echo ? coopEchoRequest : coopNamedRequest;
            if (pending == null || data.RequestId != pending.RequestId || data.Id != pending.Id) return true;
            if (data.Echo && coopEchoCost != null && coopEchoRequest != null && data.RequestId == coopEchoRequest.RequestId)
            {
                if (data.Accepted) { coopEchoCost.Commit(); stormEchoStarts++; }
                coopEchoCost.Dispose(); coopEchoCost = null; coopEchoRequest = null;
            }
            if (!data.Echo) coopNamedRequest = null;
            string message = data.Echo ? (data.Accepted ? SkyIslandStormEchoRules.Opened : SkyIslandStormEchoRules.StartFailed)
                : data.Accepted ? L10n.T("挑战开始", "The challenge begins")
                : L10n.T("挑战未开始：房主未能确认这次请求。请确认仍在挑战地点，等当前战斗结束后重试。",
                    "Challenge not started: the host could not confirm this request. Stay near the challenge site and retry after the current fight ends.");
            Status(message, !data.Accepted);
            if (data.Accepted && story != null) story.LogTiming("challenge", data.Id);
            return true;
        }
        private void OnCoopGates(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || BossRushCoop.FindPlayer(message.SenderId) == null || SkyIslandCoopRuntime.Current == null) return;
            CoopGateFlags data = message.Read<CoopGateFlags>();
            if (data != null && data.TravelId == SkyIslandCoopRuntime.Current.TravelId && !string.IsNullOrEmpty(data.SceneToken) && data.SceneToken == BossRushCoop.SceneToken) coopWorldGateFlags |= data.Flags & SkyIslandStoryRules.KnownFlags;
        }
        private SkyIslandStoryData CoopGateStory()
        {
            if (!BossRushCoop.IsConnected) return story.Current;
            coopWorldGateFlags |= story.Current.flags;
            coopGateStory.flags = coopWorldGateFlags;
            return coopGateStory;
        }

        private void OnCoopState(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost || SkyIslandCoopRuntime.Current == null) return;
            SkyIslandEncounters.CoopState state = message.Read<SkyIslandEncounters.CoopState>();
            if (state != null && state.TravelId == SkyIslandCoopRuntime.Current.TravelId && !string.IsNullOrEmpty(state.SceneToken) && state.SceneToken == BossRushCoop.SceneToken) pendingCoopState = state;
        }

        private void TickCoop()
        {
            if (coopChallengeDelivery != null) coopChallengeDelivery.Tick(Time.unscaledDeltaTime);
            if (coopEchoRequest != null && coopEchoRequest.AssetId == BossRushCoop.LocalAssetId && Time.unscaledTime >= coopEchoRequestAt)
            { coopEchoRequestAt = Time.unscaledTime + 0.5f; BossRushCoop.SendToHost("sky.challenge.request", coopEchoRequest); }
            if (coopNamedRequest != null && coopNamedRequest.AssetId == BossRushCoop.LocalAssetId && Time.unscaledTime >= coopNamedRequestAt)
            { coopNamedRequestAt = Time.unscaledTime + 0.5f; BossRushCoop.SendToHost("sky.challenge.request", coopNamedRequest); }
            if (coopEffects != null) coopEffects.Tick();
            if (SkyIslandFieldcraft.Current != null) SkyIslandFieldcraft.Current.TickCoopHarvest();
            SkyIslandWorldStory.TickCoopBossNotes();
            if (!BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene || encounters == null) return;
            if (BossRushCoop.IsHost && Time.unscaledTime >= nextCoopState)
            {
                nextCoopState = Time.unscaledTime + 0.5f;
                SkyIslandEncounters.CoopState state = encounters.CollectCoopState();
                state.GateFlags = CoopGateStory().flags;
                state.PatrolActors = patrols != null ? patrols.CollectCoopActors() : new int[0];
                pendingCoopState = state;
                BossRushCoop.Broadcast("sky.encounters.state", state);
            }
            if (BossRushCoop.IsClient && Time.unscaledTime >= nextCoopState)
            {
                nextCoopState = Time.unscaledTime + 0.5f;
                BossRushCoop.SendToHost("sky.gates.request", new CoopGateFlags { TravelId = SkyIslandCoopRuntime.Current.TravelId, SceneToken = BossRushCoop.SceneToken, Flags = story.Current.flags });
            }
            if (BossRushCoop.IsClient && pendingCoopState != null && pendingCoopState.SceneToken == BossRushCoop.SceneToken)
            {
                // 最新快照继续保留：网络角色晚于状态包抵达时下一拍继续绑定。
                encounters.ApplyCoopState(pendingCoopState);
                if (patrols != null) patrols.ApplyCoopActors(pendingCoopState.PatrolActors);
                coopWorldGateFlags = pendingCoopState.GateFlags;
            }
            if (pendingCoopState != null && pendingCoopState.SceneToken == BossRushCoop.SceneToken)
            {
                if (pendingCoopState.Encounters == null) return;
                foreach (SkyIslandEncounters.CoopEncounter entry in pendingCoopState.Encounters)
                {
                    SkyIslandStoryAction namedOutcome;
                    if (entry != null && TryChallengeOutcome(entry.Id, out namedOutcome)) continue;
                    if (entry != null && entry.Started && entry.Cleared && !coopCredited.Contains(entry.Id))
                    {
                        OnEncounterCleared(entry.Id);
                        if (EncounterWasSaved(entry.Id)) coopCredited.Add(entry.Id);
                    }
                }
            }
        }

        private bool CompleteCoopChallenge(string id, bool cleared)
        {
            SkyIslandStoryAction outcome;
            if (!BossRushCoop.IsConnected || !TryChallengeOutcome(id, out outcome)) return false;
            if (!BossRushCoop.IsHost || encounters == null || SkyIslandCoopRuntime.Current == null) return true;
            string playerId, assetId, requestId;
            if (!encounters.TryGetChallengeOwner(id, out playerId, out assetId, out requestId)) return true;
            coopCompletedChallenges.Add(playerId + ":" + assetId + ":" + id);
            SkyIslandCoopRuntime.Current.CompleteChallenge(playerId, assetId, requestId, id, cleared);
            return true;
        }
        internal void CreditChallengeBounty(string id)
        {
            if (!bountyCredited.Contains(id)) AnnounceGuardProgress(id);
            if (bounty != null && bountyCredited.Add(id)) bounty.ReportEncounterCleared();
        }

        private void DisposeCoop()
        {
            BossRushCoop.SessionChanged -= OnCoopSessionChanged;
            if (coopEffects != null) coopEffects.Dispose(); coopEffects = null;
            if (coopStateSubscription != null) coopStateSubscription.Dispose();
            if (coopChallengeSubscription != null) coopChallengeSubscription.Dispose();
            if (coopChallengeReplySubscription != null) coopChallengeReplySubscription.Dispose();
            if (coopGateSubscription != null) coopGateSubscription.Dispose();
            if (coopEchoCost != null) coopEchoCost.Dispose();
            if (coopChallengeDelivery != null) coopChallengeDelivery.Dispose(); coopChallengeDelivery = null; coopEchoRequest = coopNamedRequest = null;
            coopEchoCost = null;
            coopChallengeReplySubscription = coopGateSubscription = null;
            coopStateSubscription = coopChallengeSubscription = null;
            pendingCoopState = null;
        }

        private IEnumerator WaitForCoopScene(string key)
        {
            if (!BossRushCoop.IsConnected) yield break;
            if (coopJoining && key == "sky.prepare") yield break;
            SkyIslandCoopRuntime travel = SkyIslandCoopRuntime.Current;
            float deadline = Time.realtimeSinceStartup + 120f;
            while (!closed && BossRushCoop.IsConnected && (travel == null || string.IsNullOrEmpty(travel.TravelId)))
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("等待天空岛航行身份超时");
                yield return null;
                travel = SkyIslandCoopRuntime.Current;
            }
            if (closed || !BossRushCoop.IsConnected) yield break;
            bool arriving = key.EndsWith(":sky.runtime", StringComparison.Ordinal);
            key = SkyIslandSceneReferenceBridge.SceneId + ":" + travel.TravelId + ":" + key;
            while (!closed && BossRushCoop.IsConnected)
            {
                // 到图后必须先拿到房主的新 generation；上一张图的准备集合不能提前放行。
                if (BossRushCoop.SessionReady && (!arriving || BossRushCoop.SceneGeneration > travel.DepartureGeneration))
                {
                    BossRushCoop.MarkSceneReady(key);
                    if (BossRushCoop.AllPlayersSceneReady(key)) yield break;
                }
                if (Time.realtimeSinceStartup > deadline)
                    throw new TimeoutException("等待联机玩家准备天空岛超时");
                yield return null;
            }
        }
    }

    internal sealed partial class SkyIslandEncounters
    {
        [Serializable] internal sealed class CoopState { public string TravelId, SceneToken; public int GateFlags; public CoopEncounter[] Encounters; public int[] PatrolActors; }
        [Serializable] internal sealed class CoopEncounter
        {
            public string Id;
            public bool Started, Cleared, Spawning;
            public bool StormBodyDead;
            public int[] Actors;
            public bool[] Dead;
        }

        internal CoopState CollectCoopState()
        {
            var result = new CoopEncounter[encounters.Count];
            for (int i = 0; i < encounters.Count; i++)
            {
                Encounter source = encounters[i];
                var entry = new CoopEncounter { Id = source.Id, Started = source.Started, Cleared = source.Cleared,
                    Spawning = source.Spawning, Actors = new int[source.Count], Dead = new bool[source.Count] };
                for (int j = 0; j < source.Count; j++)
                {
                    var actor = source.Actors[j];
                    entry.Dead[j] = actor.Died;
                    if (actor.Life != null) entry.Actors[j] = BossRushCoop.GetAiId(actor.Life.GetComponent<CharacterMainControl>());
                }
                entry.StormBodyDead = source.Count > 0 && source.Definition.TierFor(0) == SkyIslandEnemyTier.Storm && source.Actors[0].Died;
                result[i] = entry;
            }
            return new CoopState { TravelId = SkyIslandCoopRuntime.Current != null ? SkyIslandCoopRuntime.Current.TravelId : null, SceneToken = BossRushCoop.SceneToken, Encounters = result };
        }

        internal void ApplyCoopState(CoopState state)
        {
            if (!BossRushCoop.IsClient || !BossRushCoop.HasBoundScene || closed || state == null || state.Encounters == null
                || state.SceneToken != BossRushCoop.SceneToken) return;
            foreach (CoopEncounter entry in state.Encounters)
            {
                Encounter target = entry == null ? null : Find(entry.Id);
                if (target == null || entry.Actors == null || entry.Dead == null
                    || entry.Actors.Length != target.Count || entry.Dead.Length != target.Count) continue;
                target.Started = entry.Started; target.Spawning = entry.Spawning; target.Cleared = entry.Cleared;
                for (int j = 0; j < target.Count; j++)
                {
                    target.Actors[j].Died = entry.Dead[j];
                    CharacterMainControl actor = BossRushCoop.FindAi(entry.Actors[j]);
                    if (actor != null) CoopAiPredictionHealthProtection.MarkReplica(actor);
                    if (actor == null || target.Actors[j].Life != null) continue;
                    SkyIslandEnemyLife life = actor.GetComponent<SkyIslandEnemyLife>() ?? actor.gameObject.AddComponent<SkyIslandEnemyLife>();
                    life.Bind(actor, target.Actors[j]); target.Actors[j].Life = life;
                }
            }
        }

        internal bool TryGetChallengeOwner(string id, out string playerId, out string assetId, out string requestId)
        {
            Encounter target = Find(id);
            playerId = target != null ? target.ChallengePlayer : null;
            assetId = target != null ? target.ChallengeAsset : null;
            requestId = target != null ? target.ChallengeRequest : null;
            return !string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(assetId) && !string.IsNullOrEmpty(requestId);
        }

        internal bool BeginRemoteChallenge(string id, Vector3 requester, string challenger = null, string asset = null, string request = null)
        {
            Encounter target = Find(id);
            if (!BossRushCoop.HasWorldAuthority || closed || !valid() || target == null || !target.Manual
                || (target.Started && !target.Cleared) || AnySpawning()
                || (requester - target.Marker.position).sqrMagnitude > ChallengeRange * ChallengeRange
                || CountActiveActors() + target.Count > MaxActiveActors + ChallengeReserve) return false;
            if (target.Cleared)
                for (int i = 0; i < target.Count; i++) target.Actors[i] = new SkyIslandEnemyRecord();
            target.Cleared = false;
            target.ChallengePlayer = challenger; target.ChallengeAsset = asset; target.ChallengeRequest = request;
            Spawn(target);
            return true;
        }
    }
}

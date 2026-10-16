using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopCampaignFact
    {
        public string Kind, Mode, RunId, SceneToken, Scope, FactId;
        public int SceneGeneration;
        public bool Melee, Boss, Bounty;
        public int Wave;
    }

    [Serializable]
    internal sealed class CoopCampaignParticipant
    {
        public string Player, Asset;
    }

    [Serializable]
    internal sealed class CoopCampaignWorld
    {
        public bool FinalActive, SpawnResolved;
        public int FinalRun;
        public string Boss, Mode, SceneToken, Scope;
        public CoopCampaignParticipant[] FinalParticipants;
    }

    internal sealed partial class CampaignRuntimeModule
    {
        private static CampaignRuntimeModule coopOwner;
        private CoopPersonalDelivery<CoopCampaignFact> coopFacts;
        private CoopGameplayChannel<CoopCampaignWorld> coopWorld;
        private bool coopRemoteFinalAdmission;
        private string coopCampaignRoom, coopCampaignToken, coopCampaignMode, coopCampaignScope;
        private string coopCampaignTrackedKey;
        private readonly Dictionary<string, string> coopCampaignSourceScopes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CampaignObjectiveTracker.ExtractionLease> coopCampaignLeases = new Dictionary<string, CampaignObjectiveTracker.ExtractionLease>(StringComparer.Ordinal);
        private readonly HashSet<string> coopCampaignObservedScopes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> coopCampaignEntryTokens = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> coopCampaignFinalParticipants = new Dictionary<string, string>(StringComparer.Ordinal);
        private int coopCampaignFinalEntry;

        private static string CoopCampaignLeaseKey(string room, string token, string scope, string asset)
        {
            return room + ":" + token + ":" + scope + ":" + asset;
        }

        private string ResolveCoopCampaignEntry(string mode)
        {
            string entry = null;
            if (mode == CampaignContentCatalog.ModeModeD) entry = _owner.CampaignModeDEntryForRuntime;
            else if (mode == CampaignContentCatalog.ModeModeE) entry = _owner.CampaignModeEEntryForRuntime;
            else if (mode == CampaignContentCatalog.ModeModeF) entry = _owner.CampaignModeFEntryForRuntime;
            else if (mode == CampaignContentCatalog.ModeStandard) entry = _owner.CampaignArenaEntryForRuntime;
            else if (mode == CampaignContentCatalog.ModeZombie && _owner.CampaignZombieRunForRuntime != null
                && _owner.CampaignZombieRunForRuntime.RunId != 0)
                entry = Math.Abs((long)_owner.CampaignZombieRunForRuntime.RunId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else if (mode == CampaignContentCatalog.ModeFinal && campaignFinalBossRunId > 0)
                entry = campaignFinalBossRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(entry) ? null : mode + ":" + entry;
        }

        private bool IsCoopCampaignEntryCurrent(string scope)
        {
            if (string.IsNullOrEmpty(scope) || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(BossRushCoop.SceneToken)) return false;
            string key = BossRushCoop.RunId + ":" + scope;
            string token;
            if (coopCampaignEntryTokens.TryGetValue(key, out token)) return token == BossRushCoop.SceneToken;
            coopCampaignEntryTokens.Add(key, BossRushCoop.SceneToken);
            return true;
        }

        // The modes' real entry epochs are shared before Campaign's periodic snapshot.
        // Facts may consume that entry, but cannot create one or arm a new contract.
        private void RefreshCoopCampaignSource(string mode)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(BossRushCoop.SceneToken)) return;
            if (coopCampaignRoom != BossRushCoop.RunId || coopCampaignToken != BossRushCoop.SceneToken)
            {
                coopCampaignRoom = BossRushCoop.RunId; coopCampaignToken = BossRushCoop.SceneToken;
                coopCampaignMode = coopCampaignScope = null;
                coopCampaignSourceScopes.Clear();
            }
            string scope = ResolveCoopCampaignEntry(mode);
            if (scope != null && !IsCoopCampaignEntryCurrent(scope)) return;
            if (mode == coopCampaignMode && scope == coopCampaignScope) return;
            coopCampaignMode = mode;
            coopCampaignScope = scope;
            if (coopCampaignScope != null)
            {
                coopCampaignSourceScopes[mode] = coopCampaignScope;
                coopCampaignObservedScopes.Add(CoopCampaignLeaseKey(coopCampaignRoom, coopCampaignToken, coopCampaignScope, string.Empty));
            }
        }

        private bool IsCoopCampaignTrackingCurrent(string mode)
        {
            if (!BossRushCoop.IsConnected) return true;
            string scope = ResolveCoopCampaignEntry(mode);
            if (string.IsNullOrEmpty(BossRushCoop.RunId) || !IsCoopCampaignEntryCurrent(scope)) return false;
            string participantAsset;
            if (!TryGetCoopCampaignParticipant(mode, scope, BossRushCoop.LocalPlayerId, out participantAsset)
                || participantAsset != BossRushCoop.LocalAssetId)
            {
                CampaignObjectiveTracker.ResetSession();
                return false;
            }
            string key = CoopCampaignLeaseKey(BossRushCoop.RunId, BossRushCoop.SceneToken, scope, BossRushCoop.LocalAssetId);
            CampaignObjectiveTracker.ExtractionLease previous;
            if (coopCampaignTrackedKey != key && coopCampaignTrackedKey != null
                && coopCampaignLeases.TryGetValue(coopCampaignTrackedKey, out previous)
                && CampaignObjectiveTracker.IsExtractionLeaseCurrent(previous)) CampaignObjectiveTracker.ResetSession();
            coopCampaignTrackedKey = key;
            CampaignObjectiveTracker.ExtractionLease original;
            if (coopCampaignLeases.TryGetValue(key, out original))
                return CampaignObjectiveTracker.RestoreExtractionLease(original);
            return true;
        }

        internal static bool PrepareCoopCampaignEntry(string mode)
        {
            return coopOwner == null || coopOwner.IsCoopCampaignTrackingCurrent(mode);
        }

        private void CaptureCoopCampaignTracking(string mode)
        {
            if (!BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(mode)
                || string.IsNullOrEmpty(BossRushCoop.RunId) || string.IsNullOrEmpty(BossRushCoop.SceneToken)) return;
            CharacterMainControl local = CharacterMainControl.Main;
            if (local == null || local.Health == null || local.Health.IsDead) return;
            string scope = ResolveCoopCampaignEntry(mode);
            if (!IsCoopCampaignEntryCurrent(scope)) return;
            string key = CoopCampaignLeaseKey(BossRushCoop.RunId, BossRushCoop.SceneToken, scope, BossRushCoop.LocalAssetId);
            if (coopCampaignLeases.ContainsKey(key)) return;
            CampaignObjectiveTracker.ExtractionLease lease = CampaignObjectiveTracker.CaptureExtractionLease(mode);
            coopCampaignLeases.Add(key, lease); // null freezes an actually observed entry without an accepted contract.
        }

        private void ResetCoopCampaignProjection()
        {
            // Register lingering mode fields against the departing token before the new
            // native Core can be bound; a stale epoch is never a new scene entry.
            if (!string.IsNullOrEmpty(coopCampaignToken))
                foreach (string mode in new[] { CampaignContentCatalog.ModeStandard, CampaignContentCatalog.ModeModeD,
                    CampaignContentCatalog.ModeModeE, CampaignContentCatalog.ModeModeF, CampaignContentCatalog.ModeZombie, CampaignContentCatalog.ModeFinal })
                {
                    string scope = ResolveCoopCampaignEntry(mode);
                    if (scope != null) coopCampaignEntryTokens[coopCampaignRoom + ":" + scope] = coopCampaignToken;
                }
            coopCampaignRoom = coopCampaignToken = coopCampaignMode = coopCampaignScope = null;
            coopCampaignTrackedKey = null;
            coopCampaignSourceScopes.Clear();
        }

        private void CleanupCoopCampaignFacts()
        {
            ResetCoopCampaignProjection();
            coopCampaignLeases.Clear();
            coopCampaignObservedScopes.Clear();
            coopCampaignEntryTokens.Clear();
            coopCampaignFinalParticipants.Clear(); coopCampaignFinalEntry = 0;
        }

        private void InitializeCoopCampaign()
        {
            coopOwner = this;
            coopFacts = new CoopPersonalDelivery<CoopCampaignFact>("Campaign", ApplyCoopCampaignFact);
            coopWorld = new CoopGameplayChannel<CoopCampaignWorld>("Campaign", CaptureCoopCampaignWorld, ApplyCoopCampaignWorld, ExecuteCoopCampaignCommand);
        }

        private void BeginCoopCampaignFinalEntry()
        {
            if (!BossRushCoop.IsConnected) return;
            if (BossRushCoop.IsHost)
            {
                coopCampaignFinalParticipants.Clear(); coopCampaignFinalEntry = campaignFinalBossRunId;
                foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                    if (player.IsConnected && player.IsAlive && !string.IsNullOrEmpty(player.AssetId))
                        coopCampaignFinalParticipants[player.Id] = player.AssetId;
            }
            RefreshCoopCampaignSource(CampaignContentCatalog.ModeFinal);
            if (IsCoopCampaignTrackingCurrent(CampaignContentCatalog.ModeFinal))
                CaptureCoopCampaignTracking(CampaignContentCatalog.ModeFinal);
            if (BossRushCoop.IsHost && coopWorld != null) coopWorld.Publish();
        }

        private CoopCampaignWorld CaptureCoopCampaignWorld()
        {
            string mode = ResolveCampaignWorldMode();
            RefreshCoopCampaignSource(mode);
            return new CoopCampaignWorld
            {
                Mode = mode, SceneToken = BossRushCoop.SceneToken, Scope = coopCampaignScope,
                FinalActive = campaignFinalBossActive, SpawnResolved = campaignFinalBossSpawnResolved,
                FinalRun = campaignFinalBossRunId, Boss = BossRushCoop.GetEntityId(campaignFinalBossInstance),
                FinalParticipants = CaptureCoopCampaignFinalParticipants()
            };
        }

        private CoopCampaignParticipant[] CaptureCoopCampaignFinalParticipants()
        {
            var result = new List<CoopCampaignParticipant>();
            foreach (var participant in coopCampaignFinalParticipants)
                result.Add(new CoopCampaignParticipant { Player = participant.Key, Asset = participant.Value });
            return result.ToArray();
        }

        private bool TryGetCoopCampaignParticipant(string mode, string scope, string player, out string asset)
        {
            asset = null;
            if (string.IsNullOrEmpty(scope) || !scope.StartsWith(mode + ":", StringComparison.Ordinal)) return false;
            string epoch = scope.Substring(mode.Length + 1);
            if (mode == CampaignContentCatalog.ModeFinal)
                return epoch == coopCampaignFinalEntry.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    && coopCampaignFinalParticipants.TryGetValue(player, out asset);
            return _owner.TryGetCampaignParticipantAssetForRuntime(mode, epoch, player, out asset);
        }

        private void ApplyCoopCampaignWorld(CoopCampaignWorld state)
        {
            if (state == null || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(state.SceneToken)
                || state.SceneToken != BossRushCoop.SceneToken) return;
            if (coopCampaignScope != null && coopCampaignScope != state.Scope)
            {
                CampaignObjectiveTracker.ExtractionLease previous;
                if (coopCampaignLeases.TryGetValue(CoopCampaignLeaseKey(coopCampaignRoom, coopCampaignToken, coopCampaignScope, BossRushCoop.LocalAssetId), out previous)
                    && CampaignObjectiveTracker.IsExtractionLeaseCurrent(previous)) CampaignObjectiveTracker.ResetSession();
            }
            coopCampaignRoom = BossRushCoop.RunId; coopCampaignToken = state.SceneToken;
            coopCampaignMode = state.Mode; coopCampaignScope = state.Scope;
            if (!string.IsNullOrEmpty(state.Scope)) coopCampaignObservedScopes.Add(CoopCampaignLeaseKey(coopCampaignRoom, state.SceneToken, state.Scope, string.Empty));
            if (state.FinalActive && !campaignFinalScopeBound) CaptureCampaignFinalScope();
            if (!IsCampaignFinalScopeValid()) return;
            campaignFinalBossActive = state.FinalActive;
            campaignFinalBossSpawnResolved = state.SpawnResolved;
            campaignFinalBossRunId = state.FinalRun;
            if (state.FinalActive)
            {
                coopCampaignFinalEntry = state.FinalRun; coopCampaignFinalParticipants.Clear();
                if (state.FinalParticipants != null)
                    foreach (CoopCampaignParticipant participant in state.FinalParticipants)
                        if (participant != null && !string.IsNullOrEmpty(participant.Player) && !string.IsNullOrEmpty(participant.Asset))
                            coopCampaignFinalParticipants[participant.Player] = participant.Asset;
            }
            campaignFinalBossInstance = BossRushCoop.FindEntity(state.Boss);
            CoopAiPredictionHealthProtection.MarkReplica(campaignFinalBossInstance);
            CharacterMainControl local = CharacterMainControl.Main;
            if (state.FinalActive && local != null && local.Health != null && !local.Health.IsDead)
            {
                if (!IsCoopCampaignTrackingCurrent(CampaignContentCatalog.ModeFinal)) return;
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal);
                CaptureCoopCampaignTracking(CampaignContentCatalog.ModeFinal);
            }
        }

        private void ExecuteCoopCampaignCommand(CoopGameplayCommand command)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (command.Action != "final-start" || player == null || !player.IsAlive || !IsCampaignArenaSceneCached()
                || _owner == null || !_owner.CampaignArenaActiveForRuntime || campaignFinalBossActive || IsAnyGameplayModeActiveForCampaign()) return;
            // 章节资格在请求者自己的本地入口判定；服务器只接管已通过资格入口的共享召唤。
            coopRemoteFinalAdmission = true;
            try { StartCampaignFinalBoss(); }
            finally { coopRemoteFinalAdmission = false; }
        }

        internal static bool ReportCoopCampaignKill(CharacterMainControl killer, CharacterMainControl victim, bool melee, bool boss, bool bounty)
        {
            if (!BossRushCoop.IsConnected) return false;
            if (!BossRushCoop.IsHost || coopOwner == null || victim == null) return true;
            coopOwner.DeliverCoopCampaignFact("kill:" + victim.GetInstanceID(), new CoopCampaignFact
            { Kind = "kill", Melee = melee, Boss = boss, Bounty = bounty }, BossRushCoop.GetPlayerId(killer));
            return true;
        }

        private bool DeliverCoopCampaignFact(string id, CoopCampaignFact fact, string recipient = null)
        {
            if (!BossRushCoop.IsConnected) return false;
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(BossRushCoop.SceneToken)) return true;
            fact.RunId = BossRushCoop.RunId; fact.SceneGeneration = BossRushCoop.SceneGeneration;
            fact.SceneToken = BossRushCoop.SceneToken;
            if (string.IsNullOrEmpty(fact.Mode)) fact.Mode = fact.Kind == "final" ? CampaignContentCatalog.ModeFinal : ResolveCampaignWorldMode();
            RefreshCoopCampaignSource(ResolveCampaignWorldMode());
            string scope;
            if (string.IsNullOrEmpty(fact.Mode) || !coopCampaignSourceScopes.TryGetValue(fact.Mode, out scope)) return true;
            fact.Scope = scope; fact.FactId = id;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (fact.Kind == "kill" && (string.IsNullOrEmpty(recipient) || player.Id != recipient || !player.IsAlive)) continue;
                string asset;
                if (TryGetCoopCampaignParticipant(fact.Mode, scope, player.Id, out asset))
                {
                    if (fact.Kind == "kill" && asset != player.AssetId) continue;
                    coopFacts.Deliver(player.Id, scope + ":" + id, fact, asset);
                }
            }
            return true;
        }

        private bool ApplyCoopCampaignFact(CoopCampaignFact fact)
        {
            if (fact == null || string.IsNullOrEmpty(fact.RunId) || string.IsNullOrEmpty(fact.Scope)
                || string.IsNullOrEmpty(fact.SceneToken) || string.IsNullOrEmpty(fact.FactId)) return true;
            // The delivery owner freezes the recipient asset. A later Core, death or
            // disabled board cannot erase an already earned original contract fact.
            CampaignObjectiveTracker.ExtractionLease lease;
            if (coopCampaignLeases.TryGetValue(CoopCampaignLeaseKey(fact.RunId, fact.SceneToken, fact.Scope, BossRushCoop.LocalAssetId), out lease))
                return lease == null || fact.Mode != lease.Mode || CampaignObjectiveTracker.ReportCoopFactLease(lease, fact);
            // A fact may arrive before the normal world snapshot/entry tick. Keep it
            // pending until that real entry captures eligibility; never arm on receipt.
            if (!BossRushCoop.HasBoundScene || string.IsNullOrEmpty(BossRushCoop.SceneToken)) return false;
            if (fact.RunId != BossRushCoop.RunId || fact.SceneToken != BossRushCoop.SceneToken) return true;
            string observed = CoopCampaignLeaseKey(fact.RunId, fact.SceneToken, fact.Scope, string.Empty);
            return coopCampaignObservedScopes.Contains(observed) && fact.Scope != coopCampaignScope;
        }
    }
}

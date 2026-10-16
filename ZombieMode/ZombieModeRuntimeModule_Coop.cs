using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopZombieEnemy
    {
        public string Entity;
        public bool Boss;
        public int Kind, BossKind, SpecialKind, Points;
    }

    [Serializable]
    internal sealed class CoopZombieArea
    {
        public Vector3 Origin;
        public float Radius, Strength;
    }

    [Serializable]
    internal sealed class CoopZombieTerminal
    {
        public string Id, Service;
        public bool Real;
        public Vector3 Position;
        public bool BossStock;
    }

    [Serializable]
    internal sealed class CoopZombieWorld
    {
        public int Run, Scene, Lifecycle, Combat, Wave, Kills, Target, Bosses, Living, Normal, Natural, Contracts;
        public string SceneName;
        public float Preparation, Radius, PortableRadius;
        public Vector3 Center, PortableCenter;
        public bool Safe, Portable, Beacon, Extracting;
        public CoopZombieEnemy[] Enemies;
        public CoopZombieTerminal[] Terminals;
        public string[] StarterReady, Skipped;
    }

    [Serializable]
    internal sealed class CoopZombiePersonal
    {
        public int Run, Amount;
        public string Kind;
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private bool BeginCoopZombiePersonalRewardNode()
        {
            if (!IsCoopZombieLocalAssetCurrent()) return false;
            if (BossRushCoop.IsConnected && coopZombieCompletedRewardWave == runState.CurrentWave) return false;
            coopZombieLocalRewardWave = runState.CurrentWave;
            return true;
        }
        private static ZombieModeRuntimeModule coopZombieOwner;
        private string coopZombieProjectionRoom;

        private void RetireLostCoopZombieProjection()
        {
            if (coopZombieProjectionRoom == null || (BossRushCoop.IsClient && coopZombieProjectionRoom == BossRushCoop.RunId)) return;
            coopZombieProjectionRoom = null;
            coopZombieAdmissionPending = false;
            owner.CleanupZombieModeForRuntimeModule((runState.ExtractionSuccessHandled || runState.CombatPhase == ZombieModeCombatPhase.SuccessExit) ? ZombieModeFailureReason.SuccessfulExtraction : ZombieModeFailureReason.Unknown);
            coopZombieLocalRun = 0; coopZombieTerminals.Clear(); coopZombieRealNpcs.Clear();
        }
        private CoopGameplayChannel<CoopZombieWorld> coopZombieWorld;
        private CoopPersonalDelivery<CoopZombiePersonal> coopZombiePersonal;
        private sealed class CoopZombieExtractionEligibility
        {
            internal bool LastAlive, LastInside, CountdownQualified;
        }
        private readonly Dictionary<string, CoopZombieExtractionEligibility> coopZombieExtractionEligibility
            = new Dictionary<string, CoopZombieExtractionEligibility>(StringComparer.Ordinal);

        private void RefreshCoopZombieExtractionEligibility(CountDownArea area)
        {
            if (area == null) return;
            float radius = ZombieModeTuning.ExtractionAreaTriggerRadius;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!IsCoopZombieParticipant(player)) continue;
                string key = CoopZombieParticipantKey(player);
                CoopZombieExtractionEligibility state;
                if (!coopZombieExtractionEligibility.TryGetValue(key, out state))
                    coopZombieExtractionEligibility[key] = state = new CoopZombieExtractionEligibility();
                state.LastAlive = player.IsAlive;
                Vector3 delta = player.Character != null ? player.Character.transform.position - area.transform.position : Vector3.zero;
                delta.y = 0f;
                state.LastInside = state.LastAlive && delta.sqrMagnitude <= radius * radius;
                state.CountdownQualified = state.LastAlive && runState.ExtractionChanneling;
            }
        }

        private bool IsCoopZombieExtractionRecipient(string id, string asset)
        {
            if (string.IsNullOrEmpty(asset) || !coopZombieStarterReady.Contains(id + ":" + asset)) return false;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(id);
            if (player != null && player.IsConnected) return player.AssetId == asset && IsCoopZombieParticipant(player) && player.IsAlive;
            CoopZombieExtractionEligibility state;
            return coopZombieExtractionEligibility.TryGetValue(id + ":" + asset, out state)
                && state.LastAlive && (state.LastInside || state.CountdownQualified);
        }

        private readonly HashSet<string> coopZombieStarterReady = new HashSet<string>();
        private readonly Dictionary<string, int> coopZombieRewardReady = new Dictionary<string, int>();
        private readonly HashSet<string> coopZombieWorldSelections = new HashSet<string>();
        private readonly Dictionary<string, string> coopZombieParticipants = new Dictionary<string, string>(StringComparer.Ordinal);
        internal bool TryGetCoopCampaignParticipantAsset(string expectedEpoch, string playerId, out string assetId)
        {
            assetId = null;
            string participantAsset;
            if (runState == null || expectedEpoch != Math.Abs((long)runState.RunId).ToString(System.Globalization.CultureInfo.InvariantCulture)
                || string.IsNullOrEmpty(playerId) || !coopZombieParticipants.TryGetValue(playerId, out participantAsset)
                || string.IsNullOrEmpty(participantAsset) || !coopZombieStarterReady.Contains(playerId + ":" + participantAsset)) return false;
            assetId = participantAsset;
            return true;
        }
        private readonly HashSet<string> coopZombieDeclinedAssets = new HashSet<string>(StringComparer.Ordinal);
        private string coopZombieLocalAsset;
        private int coopZombieDeclinedRun;
        private float coopZombieReadyRetry;
        private string[] coopZombieConfirmedStarter;
        private bool coopZombieStarterGranted;
        private readonly HashSet<string> coopZombieSkipped = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> coopZombieSkippedAssets = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> coopZombieSkipQueued = new HashSet<string>(StringComparer.Ordinal);

        private void ApplyCoopZombieStarterConfirmation(int run, string[] participants)
        {
            if (run != runState.RunId) return;
            coopZombieConfirmedStarter = participants;
            if (!BossRushCoop.IsClient) return;
            coopZombieStarterReady.Clear();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                string key = CoopZombieParticipantKey(player), asset;
                if (participants == null || Array.IndexOf(participants, key) < 0
                    || coopZombieParticipants.TryGetValue(player.Id, out asset) && asset != player.AssetId) continue;
                coopZombieParticipants[player.Id] = player.AssetId;
                coopZombieStarterReady.Add(key);
            }
        }
        private int coopZombieEntrySequence;
        private sealed class CoopZombieRefund
        {
            internal string AssetId;
            internal long Cash;
            internal bool Invitation;
        }
        private readonly Dictionary<string, CoopZombieRefund> coopZombieRefunds = new Dictionary<string, CoopZombieRefund>(StringComparer.Ordinal);
        private sealed class CoopZombieWallet { internal int Points, Received, MaxWave; internal bool Extracted, CampaignCaptured; internal CampaignObjectiveTracker.ExtractionLease Campaign; }
        private string coopZombieWalletRoom;
        private readonly Dictionary<string, CoopZombieWallet> coopZombieWallets = new Dictionary<string, CoopZombieWallet>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> coopZombiePointsIssued = new Dictionary<string, int>(StringComparer.Ordinal);

        internal void RememberCoopZombieWallet()
        {
            if (!BossRushCoop.IsConnected || runState == null || runState.RunId == 0 || runState.LifecyclePhase == ZombieModeLifecyclePhase.None || string.IsNullOrEmpty(coopZombieLocalAsset)) return;
            string key = coopZombieWalletRoom + ":" + coopZombieLocalAsset + ":" + Math.Abs(runState.RunId);
            CoopZombieWallet wallet;
            if (!coopZombieWallets.TryGetValue(key, out wallet)) { wallet = new CoopZombieWallet(); coopZombieWallets.Add(key, wallet); }
            if (!wallet.CampaignCaptured && coopZombieStarterChosen && coopZombieStarterGranted && coopZombieLocalAsset == BossRushCoop.LocalAssetId)
            {
                wallet.Campaign = owner.IsCampaignConfiguredEnabled() ? CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeZombie) : null;
                wallet.CampaignCaptured = true;
            }
            wallet.MaxWave = Math.Max(wallet.MaxWave, runState.CurrentWave);
            if (!wallet.Extracted) wallet.Points = runState.PurificationPoints;
        }
        private int coopZombieLocalRun, coopZombieLocalRewardWave = -1, coopZombieCompletedRewardWave = -1;
        private bool coopZombieAdmissionPending, coopZombieStarterChosen, coopZombiePersonalDeath, coopZombieExtractionPending;
        private CharacterMainControl coopZombieCommandPlayer;
        private long coopZombieReceiptSequence;
        private readonly Dictionary<string, ZombieModeTemporaryRealNpcRecord> coopZombieRealNpcs = new Dictionary<string, ZombieModeTemporaryRealNpcRecord>();
        private readonly Dictionary<string, ZombieModeTemporaryNpc> coopZombieTerminals = new Dictionary<string, ZombieModeTemporaryNpc>();

        private void InitializeCoopZombie()
        {
            coopZombieOwner = this;
            coopZombieWorld = new CoopGameplayChannel<CoopZombieWorld>("Zombie", CaptureCoopZombieWorld, ApplyCoopZombieWorld, ExecuteCoopZombieCommand);
            coopZombiePersonal = new CoopPersonalDelivery<CoopZombiePersonal>("Zombie", ApplyCoopZombiePersonal);
        }

        private void ResetCoopZombieEntryTransaction()
        {
            QueueCoopZombieWrongAssetRefund();
            coopZombieLocalAsset = BossRushCoop.LocalAssetId;
            coopZombieEntrySequence++;
            entryTransaction.Reset();
            runState.PendingCashInvestment = 0L;
            runState.ConfirmedCashInvested = 0L;
        }

        private void ResetCoopZombieRun(int runId)
        {
            RememberCoopZombieWallet();
            coopZombiePointsIssued.Clear();
            coopZombieExtractionEligibility.Clear();
            coopZombieStarterReady.Clear(); coopZombieRewardReady.Clear(); coopZombieWorldSelections.Clear();
            coopZombieParticipants.Clear(); coopZombieDeclinedAssets.Clear();
            coopZombieWalletRoom = BossRushCoop.RunId;
            coopZombieLocalRun = runId; coopZombieLocalAsset = BossRushCoop.LocalAssetId;
            coopZombieDeclinedRun = 0;
            coopZombieConfirmedStarter = null;
            coopZombieStarterGranted = false; coopZombieSkipped.Clear(); coopZombieSkippedAssets.Clear(); coopZombieSkipQueued.Clear();
            coopZombieStarterChosen = coopZombiePersonalDeath = coopZombieExtractionPending = false;
            coopZombieLocalRewardWave = coopZombieCompletedRewardWave = -1;
        }

        private void CleanupCoopZombie()
        {
            if (coopZombieWorld != null) coopZombieWorld.Dispose();
            if (coopZombiePersonal != null) coopZombiePersonal.Dispose();
            coopZombieWorld = null; coopZombiePersonal = null;
            if (coopZombieOwner == this) coopZombieOwner = null;
        }

        private bool IsCoopZombieLocalAssetCurrent()
        {
            return string.IsNullOrEmpty(coopZombieLocalAsset) || coopZombieLocalAsset == BossRushCoop.LocalAssetId;
        }

        internal void RefundZombieModePurificationPoints(int amount, string reason)
        {
            if (!IsCoopZombieLocalAssetCurrent()) return;
            amount = Mathf.Max(0, amount);
            if (amount <= 0)
            {
                return;
            }

            runState.PurificationPoints += amount;
            ModBehaviour.DevLog("[ZombieMode] Refund purification points: +" + amount + ", reason=" + reason);
        }

        private static string CoopZombieParticipantKey(BossRushCoopPlayer player) { return player.Id + ":" + player.AssetId; }

        private bool IsCoopZombieParticipant(BossRushCoopPlayer player)
        {
            string asset;
            return player != null && player.IsConnected && coopZombieParticipants.TryGetValue(player.Id, out asset)
                && asset == player.AssetId && coopZombieStarterReady.Contains(CoopZombieParticipantKey(player));
        }

        private bool QueueCoopZombieWrongAssetRefund()
        {
            if (IsCoopZombieLocalAssetCurrent()) return false;
            string key = coopZombieLocalAsset + ":" + coopZombieEntrySequence;
            if (ShouldRollbackZombieModeEntryResources() && !coopZombieRefunds.ContainsKey(key))
                coopZombieRefunds.Add(key, new CoopZombieRefund
                { AssetId = coopZombieLocalAsset, Cash = entryTransaction.CashTemporarilyHeld ? entryTransaction.CashWithheldAmount : 0,
                    Invitation = entryTransaction.InvitationTemporarilyHeld });
            return true;
        }

        private void TickCoopZombieRefunds()
        {
            // 每帧调用（单机也走）：没有欠款时 O(1) 返回，不拷贝字典。
            if (coopZombieRefunds.Count == 0) return;
            foreach (var pair in new List<KeyValuePair<string, CoopZombieRefund>>(coopZombieRefunds))
            {
                CoopZombieRefund refund = pair.Value;
                if (refund.AssetId != BossRushCoop.LocalAssetId) continue;
                if (refund.Cash > 0 && ZombieModeEntryDebt.RefundCash(refund.Cash)) refund.Cash = 0;
                if (refund.Invitation && ZombieModeEntryDebt.RefundInvitation()) refund.Invitation = false;
                if (refund.Cash == 0 && !refund.Invitation) coopZombieRefunds.Remove(pair.Key);
            }
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (coopZombieWorld == null || runState == null) return;
            RetireLostCoopZombieProjection();
            RememberCoopZombieWallet();
            TickCoopZombieRefunds();
            coopZombieWorld.Tick(unscaledDeltaTime);
            coopZombiePersonal.Tick(unscaledDeltaTime);
            if (!BossRushCoop.IsConnected) return;
            if (coopZombieExtractionPending && BossRushCoop.IsHost && !coopZombiePersonal.HasPendingForIdPrefix(runState.RunId + ":extract:"))
            {
                coopZombieExtractionPending = false;
                TryNotifyZombieModeExtraction();
                owner.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.SuccessfulExtraction);
                TryLoadBaseSceneAfterZombieModeExtraction();
                return;
            }
            if (runState.RunId <= 0) return;
            CharacterMainControl local = CharacterMainControl.Main;
            if (IsCoopZombieLocalAssetCurrent() && local != null && local.Health != null && local.Health.IsDead && !coopZombiePersonalDeath)
            {
                coopZombiePersonalDeath = true;
                SettleZombieModeFailureInsuranceShell(runState.RunId);
                RemoveZombieModeAttributeModifiers();
                RemoveZombieModeOptionRuntimeEffects();
            }
            if (IsCoopZombieLocalAssetCurrent() && coopZombieStarterChosen && Time.unscaledTime >= coopZombieReadyRetry)
            {
                coopZombieReadyRetry = Time.unscaledTime + 1f;
                if (coopZombieConfirmedStarter == null || Array.IndexOf(coopZombieConfirmedStarter, BossRushCoop.LocalPlayerId + ":" + coopZombieLocalAsset) < 0)
                    SendCoopZombieCommand("starter", repeat: true);
                if (coopZombieCompletedRewardWave == runState.CurrentWave && runState.CombatPhase == ZombieModeCombatPhase.RewardSelection)
                    SendCoopZombieCommand("reward-done", runState.CurrentWave, repeat: true);
            }
            if (!BossRushCoop.HasWorldAuthority || !BossRushCoop.HasBoundScene) return;
            RefreshCoopZombieExtractionEligibility(runState.ActiveExtractionArea);
            QueueCoopZombieSkippedRefunds();
            if (runState.LifecyclePhase == ZombieModeLifecyclePhase.WaitingStarterChoice && CoopZombieEveryoneReady(true))
            {
                runState.LifecyclePhase = ZombieModeLifecyclePhase.Active;
                UnlockZombieModeContainersForActiveRun(runState.RunId);
                BeginZombieModePreparation(runState.RunId, true, false);
            }
            else if (runState.CombatPhase == ZombieModeCombatPhase.RewardSelection && CoopZombieEveryoneReady(false))
            {
                if (IsZombieModeBossWave(runState.CurrentWave)) BeginZombieModeExtractionOpportunity(runState.RunId);
                else BeginZombieModePreparation(runState.RunId, false, false);
            }
            if (runState.LifecyclePhase == ZombieModeLifecyclePhase.Active && !CoopZombieAnyParticipantAlive() && !coopZombieExtractionPending)
                FailZombieModeActive(runState.RunId);
        }

        private void RetireCoopZombieUnconfirmedEntry(int nextRun)
        {
            if (coopZombieLocalRun > 0 && coopZombieLocalRun != nextRun && !coopZombieStarterGranted)
                ApplyCoopZombieSkippedEntry(coopZombieLocalRun);
        }

        private static bool CoopZombieContainsPlayer(string[] participants, string playerId)
        {
            if (participants == null) return false;
            string prefix = playerId + ":";
            foreach (string participant in participants)
                if (participant != null && participant.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private bool ApplyCoopZombieAdmissionState(int run, string[] confirmed, string[] skipped)
        {
            string key = BossRushCoop.LocalPlayerId + ":" + BossRushCoop.LocalAssetId;
            if (CoopZombieContainsPlayer(skipped, BossRushCoop.LocalPlayerId)
                || CoopZombieContainsPlayer(confirmed, BossRushCoop.LocalPlayerId) && Array.IndexOf(confirmed, key) < 0)
            {
                ApplyCoopZombieSkippedEntry(run);
                return false;
            }
            ApplyCoopZombieStarterConfirmation(run, confirmed);
            CommitCoopZombieStarterIfConfirmed(run);
            return true;
        }

        private bool ApplyCoopZombieSkippedEntry(int run)
        {
            if (coopZombieDeclinedRun == run) return true;
            // 旧收据不能取消下一局已建立的入场事务。
            if (coopZombieLocalRun == run || runState.RunId == run)
            {
                if (coopZombieStarterGranted) return false;
                if (!IsCoopZombieLocalAssetCurrent())
                {
                    QueueCoopZombieWrongAssetRefund();
                    coopZombieAdmissionPending = coopZombieStarterChosen = false;
                    owner.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.Unknown);
                    coopZombieLocalRun = 0; coopZombieDeclinedRun = run;
                    NotifyCoopZombieSkippedEntry(true, true);
                    return true;
                }
                coopZombieAdmissionPending = false;
                coopZombieStarterChosen = false;
                RollbackZombieModeInventoryTransfer();
                string refundKey = coopZombieLocalAsset + ":" + coopZombieEntrySequence;
                if (!coopZombieRefunds.ContainsKey(refundKey))
                    coopZombieRefunds.Add(refundKey, new CoopZombieRefund { AssetId = coopZombieLocalAsset,
                        Cash = entryTransaction.CashTemporarilyHeld ? entryTransaction.CashWithheldAmount : 0L,
                        Invitation = entryTransaction.InvitationTemporarilyHeld });
                entryTransaction.CashTemporarilyHeld = entryTransaction.InvitationTemporarilyHeld = false;
                entryTransaction.CashWithheldAmount = 0L;
                TickCoopZombieRefunds();
                coopZombieDeclinedRun = run;
                owner.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.Unknown);
                coopZombieLocalRun = 0;
                NotifyCoopZombieSkippedEntry(true, coopZombieRefunds.ContainsKey(refundKey));
            }
            if (coopZombieDeclinedRun != run) NotifyCoopZombieSkippedEntry(false, false);
            coopZombieDeclinedRun = run;
            return true;
        }

        private static void NotifyCoopZombieSkippedEntry(bool heldEntry, bool refundPending)
        {
            Duckov.UI.NotificationText.Push(!heldEntry
                ? L10n.T("BossRush_ZombieMode_Coop_SkippedNoCharge")
                : refundPending
                    ? L10n.T("BossRush_ZombieMode_Coop_SkippedRefundPending")
                    : L10n.T("BossRush_ZombieMode_Coop_SkippedRefunded"));
        }

        private void StartCoopZombieWithConfirmedPlayers(int run)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || !IsZombieModeRunValid(run)
                || runState.LifecyclePhase != ZombieModeLifecyclePhase.WaitingStarterChoice
                || !coopZombieStarterReady.Contains(BossRushCoop.LocalPlayerId + ":" + BossRushCoop.LocalAssetId)) return;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (!string.IsNullOrEmpty(player.AssetId) && !coopZombieStarterReady.Contains(CoopZombieParticipantKey(player)))
                { coopZombieSkipped.Add(CoopZombieParticipantKey(player)); coopZombieSkippedAssets[player.Id] = player.AssetId; coopZombieDeclinedAssets.Add(CoopZombieParticipantKey(player)); }
            QueueCoopZombieSkippedRefunds();
            coopZombieWorld.Publish();
        }

        private void QueueCoopZombieSkippedRefunds()
        {
            foreach (var skipped in coopZombieSkippedAssets)
            {
                string key = skipped.Key + ":" + skipped.Value;
                if (!coopZombieSkipQueued.Contains(key)
                    && coopZombiePersonal.Deliver(skipped.Key, runState.RunId + ":skip-entry",
                        new CoopZombiePersonal { Run = runState.RunId, Kind = "skip-entry" }, skipped.Value)) coopZombieSkipQueued.Add(key);
            }
        }

        private bool RegisterCoopZombieStarter(BossRushCoopPlayer player)
        {
            if (player == null || !player.IsAlive || runState.RunId <= 0
                || coopZombieSkippedAssets.ContainsKey(player.Id) || coopZombieSkipped.Contains(CoopZombieParticipantKey(player)) || coopZombieDeclinedAssets.Contains(CoopZombieParticipantKey(player))) return false;
            string previous;
            if (coopZombieParticipants.TryGetValue(player.Id, out previous) && previous != player.AssetId) return false;
            coopZombieParticipants[player.Id] = player.AssetId;
            coopZombieStarterReady.Add(CoopZombieParticipantKey(player));
            return true;
        }

        private void CommitCoopZombieStarterIfConfirmed(int run)
        {
            if (!IsZombieModeRunValid(run) || !IsCoopZombieLocalAssetCurrent() || !coopZombieStarterChosen || coopZombieStarterGranted) return;
            string key = BossRushCoop.LocalPlayerId + ":" + coopZombieLocalAsset;
            if (BossRushCoop.IsHost ? !coopZombieStarterReady.Contains(key)
                : coopZombieConfirmedStarter == null || Array.IndexOf(coopZombieConfirmedStarter, key) < 0) return;
            if (!GrantZombieModeStarterLoadout(runState.StarterLoadout))
            {
                SendCoopZombieCommand("decline");
                FailZombieModeBeforeActive(ZombieModeFailureReason.StarterLoadoutFailed);
                return;
            }
            coopZombieStarterGranted = true;
            FinalizeZombieModeEntryResources();
            RememberCoopZombieWallet();
        }

        private bool CanOfferCoopZombieStart(int run)
        {
            return BossRushCoop.IsHost && IsZombieModeRunValid(run) && IsCoopZombieLocalAssetCurrent()
                && coopZombieStarterGranted && runState.LifecyclePhase == ZombieModeLifecyclePhase.WaitingStarterChoice;
        }

        private void ShowCoopZombieStartConfirmation(int run, GameObject anchor = null)
        {
            if (!CanOfferCoopZombieStart(run) || !BossRushCoop.HasBoundScene) return;
            string asset = BossRushCoop.LocalAssetId, room = BossRushCoop.RunId, token = BossRushCoop.SceneToken;
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("BossRush_ZombieMode_Coop_StartTitle"),
                Body = L10n.T("BossRush_ZombieMode_Coop_StartBody"),
                ConfirmLabel = L10n.T("BossRush_ZombieMode_Coop_StartConfirm"),
                CancelLabel = L10n.T("BossRush_ZombieMode_Coop_KeepWaiting"), Danger = false, SortingOrder = BossRushUILayers.ZombieModal, Anchor = anchor,
                OnConfirm = delegate { if (asset == BossRushCoop.LocalAssetId && room == BossRushCoop.RunId && token == BossRushCoop.SceneToken) StartCoopZombieWithConfirmedPlayers(run); }
            });
        }

        private bool CoopZombieEveryoneReady(bool starter)
        {
            bool found = false;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsConnected || !player.IsAlive) continue;
                string key = CoopZombieParticipantKey(player), asset;
                if (coopZombieDeclinedAssets.Contains(key) || coopZombieParticipants.TryGetValue(player.Id, out asset) && asset != player.AssetId) continue;
                if (!starter && !IsCoopZombieParticipant(player)) continue;
                found = true;
                int wave;
                if (starter ? !coopZombieStarterReady.Contains(key)
                    : !coopZombieRewardReady.TryGetValue(key, out wave) || wave != runState.CurrentWave) return false;
            }
            return found;
        }

        private CoopZombieWorld CaptureCoopZombieWorld()
        {
            var enemies = new List<CoopZombieEnemy>();
            foreach (ZombieModeEnemyRuntimeMarker marker in zombieModeEnemyMarkersByInstanceId.Values)
                if (marker != null && marker.Owner != null && !marker.DeathSettled && !marker.RemovedFromRuntime)
                    enemies.Add(new CoopZombieEnemy { Entity = BossRushCoop.GetEntityId(marker.Owner), Boss = marker.IsBoss,
                        Kind = (int)marker.EnemyKind, BossKind = (int)marker.BossKind, SpecialKind = (int)marker.SpecialKind, Points = marker.PurificationPointValue });
            var terminals = new List<CoopZombieTerminal>();
            foreach (ZombieModeTemporaryNpc terminal in runState.TemporaryNpcs)
                if (terminal != null && terminal.GameObject != null) terminals.Add(new CoopZombieTerminal
                { Id = terminal.GameObject.GetInstanceID().ToString(), Service = terminal.ServiceType, Position = terminal.GameObject.transform.position,
                    BossStock = terminal.ServiceState != null && terminal.ServiceState.BossNodeStock });
            foreach (ZombieModeTemporaryRealNpcRecord npc in runState.TemporaryRealNpcs)
                if (npc != null && npc.GameObject != null) terminals.Add(new CoopZombieTerminal
                { Id = npc.GameObject.GetInstanceID().ToString(), Service = npc.NpcType, Real = true, Position = npc.GameObject.transform.position });
            return new CoopZombieWorld { Run = runState.RunId, Scene = runState.SceneBuildIndex, SceneName = runState.SceneName,
                Lifecycle = (int)runState.LifecyclePhase, Combat = (int)runState.CombatPhase, Wave = runState.CurrentWave,
                Kills = runState.CurrentWaveKills, Target = runState.CurrentWaveKillTarget, Bosses = runState.CurrentWaveBossesRemaining,
                Living = runState.LivingZombieCount, Normal = runState.LivingNormalZombieCount, Natural = runState.PollutionFromNatural,
                Contracts = runState.PollutionFromContracts, Preparation = runState.PreparationTimer, Center = runState.ActiveSafeZoneCenter,
                Radius = runState.ActiveSafeZoneRadius, Safe = runState.ActiveSafeZoneActive, PortableCenter = runState.PortableSafeZoneCenter,
                PortableRadius = runState.PortableSafeZoneRadius, Portable = runState.PortableSafeZoneActive,
                Beacon = runState.BeaconChanneling, Extracting = runState.ExtractionChanneling, Enemies = enemies.ToArray(), Terminals = terminals.ToArray(),
                StarterReady = new List<string>(coopZombieStarterReady).ToArray(), Skipped = new List<string>(coopZombieSkipped).ToArray() };
        }

        private void ApplyCoopZombieWorld(CoopZombieWorld state)
        {
            RetireLostCoopZombieProjection();
            RememberCoopZombieWallet();
            if (!IsCoopZombieLocalAssetCurrent() && (state.Run == coopZombieLocalRun || state.Run <= 0)) return;
            if (state.Run <= 0 || state.Lifecycle == (int)ZombieModeLifecyclePhase.None)
            {
                if (coopZombieLocalRun > 0 && !coopZombieAdmissionPending)
                {
                    RefundZombieModeInvitationIfNeeded(); RefundZombieModeCashIfNeeded();
                    owner.CleanupZombieModeForRuntimeModule((runState.ExtractionSuccessHandled || runState.CombatPhase == ZombieModeCombatPhase.SuccessExit) ? ZombieModeFailureReason.SuccessfulExtraction : ZombieModeFailureReason.Unknown);
                    coopZombieLocalRun = 0; coopZombieTerminals.Clear(); coopZombieRealNpcs.Clear();
                }
                return;
            }
            if (BossRushCoop.IsClient) coopZombieProjectionRoom = BossRushCoop.RunId;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != state.SceneName || !scene.isLoaded || !IsOfficialLevelAfterInitForZombieMode()) return;
            RetireCoopZombieUnconfirmedEntry(state.Run);
            if (!ApplyCoopZombieAdmissionState(state.Run, state.StarterReady, state.Skipped)) return;
            if (coopZombieLocalRun != state.Run && coopZombieDeclinedRun != state.Run && !coopZombieAdmissionPending)
            {
                coopZombieAdmissionPending = true;
                coopZombieWalletRoom = BossRushCoop.RunId;
                coopZombieLocalRun = state.Run;
                coopZombieLocalAsset = BossRushCoop.LocalAssetId;
                string admissionAsset = coopZombieLocalAsset;
                coopZombieConfirmedStarter = null; coopZombieStarterGranted = false;
                coopZombieStarterChosen = coopZombiePersonalDeath = false;
                coopZombieLocalRewardWave = coopZombieCompletedRewardWave = -1;
                runState.ResetForNewRun(state.Run, scene.buildIndex, scene.name);
                runState.MapProfile = BuildZombieModeMapProfile(scene.buildIndex, scene.name);
                runState.LifecyclePhase = ZombieModeLifecyclePhase.SelectingMap;
                coopZombieEntrySequence++;
                entryTransaction.Reset();
                ShowZombieModeCashInvestmentPrompt(delegate
                {
                    if (!BossRushCoop.IsClient || BossRushCoop.RunId != coopZombieProjectionRoom || BossRushCoop.LocalAssetId != admissionAsset || coopZombieLocalRun != state.Run)
                    { coopZombieAdmissionPending = false; return; }
                    ZombieModeFailureReason reason;
                    if (!TryRunZombieModePrechecks(out reason) || !CommitZombieModeEntryResourcesShell(out reason))
                    { RefundZombieModeInvitationIfNeeded(); RefundZombieModeCashIfNeeded(); coopZombieAdmissionPending = false; coopZombieDeclinedRun = state.Run; SendCoopZombieCommand("decline"); return; }
                    runState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
                    runState.PurificationPoints = (int)Math.Min(int.MaxValue, runState.ConfirmedCashInvested / ZombieModeTuning.CashToPurificationRatio);
                    if (!InitializeCoopZombiePersonalRun(state.Run))
                    { RefundZombieModeInvitationIfNeeded(); RefundZombieModeCashIfNeeded(); coopZombieAdmissionPending = false; coopZombieDeclinedRun = state.Run; SendCoopZombieCommand("decline"); return; }
                    coopZombieAdmissionPending = false;
                }, delegate
                {
                    if (!BossRushCoop.IsClient || BossRushCoop.RunId != coopZombieProjectionRoom || BossRushCoop.LocalAssetId != admissionAsset || coopZombieLocalRun != state.Run) return;
                    coopZombieAdmissionPending = false; coopZombieDeclinedRun = state.Run; SendCoopZombieCommand("decline");
                });
            }
            if (coopZombieDeclinedRun == state.Run) return;
            if (coopZombieAdmissionPending || runState.RunId != state.Run) return;
            ApplyCoopZombieStarterConfirmation(state.Run, state.StarterReady);
            runState.CurrentWave = state.Wave; runState.CurrentWaveKills = state.Kills; runState.CurrentWaveKillTarget = state.Target;
            runState.CurrentWaveBossesRemaining = state.Bosses; runState.LivingZombieCount = state.Living; runState.LivingNormalZombieCount = state.Normal;
            runState.PollutionFromNatural = state.Natural; runState.PollutionFromContracts = state.Contracts;
            runState.PreparationTimer = state.Preparation; runState.BeaconChanneling = state.Beacon; runState.ExtractionChanneling = state.Extracting;
            runState.ActiveSafeZoneCenter = state.Center; runState.ActiveSafeZoneRadius = state.Radius; runState.ActiveSafeZoneActive = state.Safe;
            runState.PortableSafeZoneCenter = state.PortableCenter; runState.PortableSafeZoneRadius = state.PortableRadius; runState.PortableSafeZoneActive = state.Portable;
            if (!coopZombieStarterGranted) return;
            runState.LifecyclePhase = (ZombieModeLifecyclePhase)state.Lifecycle;
            runState.CombatPhase = (ZombieModeCombatPhase)state.Combat;
            ClearZombieModeEnemyInstanceIds();
            if (state.Enemies != null) foreach (CoopZombieEnemy enemy in state.Enemies)
            {
                CharacterMainControl character = BossRushCoop.FindEntity(enemy.Entity);
                if (character == null) continue;
                ZombieModeEnemyRuntimeMarker marker = character.GetComponent<ZombieModeEnemyRuntimeMarker>() ?? character.gameObject.AddComponent<ZombieModeEnemyRuntimeMarker>();
                CoopAiPredictionHealthProtection.MarkReplica(character);
                marker.RunId = state.Run; marker.Owner = character; marker.IsBoss = enemy.Boss; marker.EnemyKind = (ZombieModeEnemyKind)enemy.Kind;
                marker.BossKind = (ZombieModeBossKind)enemy.BossKind; marker.SpecialKind = (ZombieModeSpecialKind)enemy.SpecialKind;
                marker.PurificationPointValue = enemy.Points; marker.DeathSettled = false; marker.RemovedFromRuntime = false;
                RegisterZombieModeEnemyInstanceId(character, marker);
            }
            MirrorCoopZombieTerminals(state.Terminals);
            MirrorCoopZombieSafeZone(false); MirrorCoopZombieSafeZone(true);
            UpdateZombieModeSafeZoneVisual();
            if (runState.CombatPhase == ZombieModeCombatPhase.RewardSelection && coopZombieLocalRewardWave != state.Wave && !coopZombiePersonalDeath)
            {
                coopZombieLocalRewardWave = state.Wave;
                TryGiveZombieModeWaveClearHealingItem();
                ShowZombieModeRewardSelection(state.Run, IsZombieModeBossWave(state.Wave));
            }
            if (runState.CombatPhase == ZombieModeCombatPhase.ExtractionOpportunity)
            { EnsureZombieModeExtractionArea(state.Run); ShowZombieModeExtractionOpportunityUi(state.Run); }
        }

        private void MirrorCoopZombieSafeZone(bool portable)
        {
            bool active = portable ? runState.PortableSafeZoneActive : runState.ActiveSafeZoneActive;
            GameObject visual = portable ? runState.PortableSafeZoneVisual : runState.ActiveSafeZoneVisual;
            if (!active)
            {
                if (visual != null) UnityEngine.Object.Destroy(visual);
                if (portable) runState.PortableSafeZoneVisual = null; else runState.ActiveSafeZoneVisual = null;
                return;
            }
            if (visual != null) return;
            Vector3 center = portable ? runState.PortableSafeZoneCenter : runState.ActiveSafeZoneCenter;
            float radius = portable ? runState.PortableSafeZoneRadius : runState.ActiveSafeZoneRadius;
            visual = CreateZombieModeFlatZoneVisual("ZombieMode_CoopSafeZone", center + Vector3.up * 0.03f, radius, 0.05f, ZombieModeZoneVisuals.SafeZoneColor);
            AttachZombieModeSafeZoneBoundaryVisual(visual, center, radius);
            RegisterZombieModeRunOnlyObject(runState.RunId, ZombieModeRunOnlyObjectKind.Unknown, visual, visual, null);
            if (portable) runState.PortableSafeZoneVisual = visual; else runState.ActiveSafeZoneVisual = visual;
        }

        private void MirrorCoopZombieTerminals(CoopZombieTerminal[] terminals)
        {
            var present = new HashSet<string>();
            if (terminals != null) foreach (CoopZombieTerminal terminal in terminals)
            {
                present.Add(terminal.Id);
                if (terminal.Real)
                {
                    ZombieModeTemporaryRealNpcRecord real;
                    if (!coopZombieRealNpcs.TryGetValue(terminal.Id, out real))
                    {
                        GameObject npc = CreateZombieModeTemporaryRealNpc(terminal.Service);
                        if (npc == null) continue;
                        foreach (AICharacterController ai in npc.GetComponentsInChildren<AICharacterController>(true)) ai.enabled = false;
                        foreach (MonoBehaviour component in npc.GetComponentsInChildren<MonoBehaviour>(true))
                            if (component is GoblinMovement || component is NurseMovement || component is CourierMovement) component.enabled = false;
                        AttachZombieModeTemporaryRealNpcMarker(npc, runState.RunId, terminal.Service);
                        real = new ZombieModeTemporaryRealNpcRecord { GameObject = npc, NpcType = terminal.Service, SpawnWave = runState.CurrentWave };
                        coopZombieRealNpcs.Add(terminal.Id, real); runState.TemporaryRealNpcs.Add(real);
                        RegisterZombieModeRunOnlyObject(runState.RunId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, npc, () => CloseZombieModeTemporaryRealNpcServices(npc));
                    }
                    if (real.GameObject != null) real.GameObject.transform.position = terminal.Position;
                    continue;
                }
                if (coopZombieTerminals.ContainsKey(terminal.Id)) continue;
                GameObject body = CreateZombieModeTemporaryServiceTerminal(runState.RunId, terminal.Service);
                if (body == null) continue;
                body.transform.position = terminal.Position;
                ZombieModeTemporaryNpc record = CreateZombieModeTemporaryNpcRecord(body, terminal.Service, terminal.BossStock);
                coopZombieTerminals.Add(terminal.Id, record); runState.TemporaryNpcs.Add(record);
                RegisterZombieModeRunOnlyObject(runState.RunId, ZombieModeRunOnlyObjectKind.TemporaryNpc, body, body, null);
            }
            foreach (string id in new List<string>(coopZombieRealNpcs.Keys))
                if (!present.Contains(id))
                {
                    ZombieModeTemporaryRealNpcRecord record = coopZombieRealNpcs[id];
                    runState.TemporaryRealNpcs.Remove(record);
                    if (record.GameObject != null) { CloseZombieModeTemporaryRealNpcServices(record.GameObject); UnityEngine.Object.Destroy(record.GameObject); }
                    coopZombieRealNpcs.Remove(id);
                }
            foreach (string id in new List<string>(coopZombieTerminals.Keys))
                if (!present.Contains(id))
                {
                    ZombieModeTemporaryNpc record = coopZombieTerminals[id];
                    runState.TemporaryNpcs.Remove(record);
                    if (record.GameObject != null) UnityEngine.Object.Destroy(record.GameObject);
                    coopZombieTerminals.Remove(id);
                }
        }

        private bool InitializeCoopZombiePersonalRun(int run)
        {
            if (!PrepareZombieModeInventoryTransfer(run) || !GrantZombieModeBeacon(run)) return false;
            owner.RegisterZombieModeEventListenersForRuntimeModule(run);
            owner.CreateZombieModeHudForRuntimeModule(run);
            runState.LifecyclePhase = ZombieModeLifecyclePhase.WaitingStarterChoice;
            ShowZombieModeStarterChoice(run);
            return true;
        }

        internal static bool RequestCoopZombieMapEntry(int mapIndex)
        {
            return coopZombieOwner != null && coopZombieOwner.coopZombieWorld.Request("enter-map", mapIndex);
        }

        private void SendCoopZombieCommand(string action, int value = 0, string text = null, bool flag = false, bool repeat = false)
        {
            if (!BossRushCoop.IsConnected) return;
            if (BossRushCoop.IsHost) ExecuteCoopZombieCommand(new CoopGameplayCommand
                { PlayerId = BossRushCoop.LocalPlayerId, AssetId = BossRushCoop.LocalAssetId, Action = action, Value = value, Text = text, Flag = flag });
            else if (repeat) coopZombieWorld.RequestRepeat(action, value, text, flag);
            else coopZombieWorld.Request(action, value, text, flag);
        }

        private void ExecuteCoopZombieCommand(CoopGameplayCommand command)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (player == null || !player.IsAlive || command.AssetId != player.AssetId) return;
            if (command.Action == "enter-map")
            {
                BossRushMapConfig[] maps = ModBehaviour.GetAllMapConfigs();
                if (maps == null || command.Value < 0 || command.Value >= maps.Length || owner.IsAnyBossRushLikeModeActive()) return;
                string failure;
                if (ZombieModeMapSelectionHelper.ShowZombieModeMapSelection(out failure))
                    ZombieModeMapSelectionHelper.ConfirmZombieModeMapEntry(command.Value);
                return;
            }
            if (runState.RunId <= 0) return;
            coopZombieCommandPlayer = player.Character;
            try
            {
                string participantKey = CoopZombieParticipantKey(player), previousAsset;
                if (command.Action == "decline") { coopZombieDeclinedAssets.Add(participantKey); coopZombieStarterReady.Remove(participantKey); return; }
                if (coopZombieSkipped.Contains(participantKey)) return;
                if (coopZombieParticipants.TryGetValue(player.Id, out previousAsset) && previousAsset != player.AssetId) return;
                if (command.Action == "starter") RegisterCoopZombieStarter(player);
                else if (command.Action == "reward-done" && command.Value == runState.CurrentWave && runState.CombatPhase == ZombieModeCombatPhase.RewardSelection)
                    coopZombieRewardReady[participantKey] = command.Value;
                else if (command.Action == "reward-world" && runState.CombatPhase == ZombieModeCombatPhase.RewardSelection)
                {
                    ZombieModeRewardType reward;
                    if (!Enum.TryParse(command.Text, out reward) || !Enum.IsDefined(typeof(ZombieModeRewardType), reward)) return;
                    if (!IsCoopZombieParticipant(player)) return;
                    string key = participantKey + ":" + runState.CurrentWave + ":" + command.Value;
                    int maxSelections = IsZombieModeBossWave(runState.CurrentWave) ? owner.GetZombieModeBossRewardSelectionCountForRewardRuntimeModule(runState.CurrentWave) : 1;
                    int readyWave;
                    if (command.Value < 1 || command.Value > maxSelections || coopZombieRewardReady.TryGetValue(participantKey, out readyWave) && readyWave == runState.CurrentWave) return;
                    if (!coopZombieWorldSelections.Add(key)) return;
                    if (player.Id != BossRushCoop.LocalPlayerId) runState.PollutionFromContracts += GetCoopZombieRewardPollution(reward);
                    if (reward == ZombieModeRewardType.MapEventEliteSquad && player.Id != BossRushCoop.LocalPlayerId) runState.PendingEliteSquadCount += 3;
                    SpawnCoopZombieRewardNpc(reward);
                }
                else if (command.Action == "gravity")
                {
                    CoopZombieArea area = BossRushCoopJson.FromJson<CoopZombieArea>(command.Text);
                    if (area == null || !ZombieModePhaseGuards.IsRunActive(runState.LifecyclePhase) || area.Radius <= 0f || area.Radius > 60f
                        || area.Strength < 0f || area.Strength > 100f || (area.Origin - player.Character.transform.position).sqrMagnitude > 3600f) return;
                    RefreshZombieModeGravityWellTargets(runState.RunId, area.Origin, area.Radius, area.Strength);
                }
                else if (command.Action == "beacon") TryUseZombieModeBeacon();
                else if (command.Action == "portable") TryUseZombieModePortableSafeZoneDevice();
                else if (command.Action == "continue") ContinueZombieModeAfterExtractionOpportunity(runState.RunId);
                else if (command.Action == "extract") StartZombieModeExtractionFromUi(runState.RunId);
            }
            finally { coopZombieCommandPlayer = null; }
        }

        private int GetCoopZombieRewardPollution(ZombieModeRewardType reward)
        {
            bool boss = IsZombieModeBossWave(runState.CurrentWave);
            switch (reward)
            {
                case ZombieModeRewardType.ContractPollutionDeal: return boss ? 2 : 1;
                case ZombieModeRewardType.ContractGearDeal: return boss ? 3 : 2;
                case ZombieModeRewardType.ContractHugePurification: return 3;
                case ZombieModeRewardType.ContractInsurance: return 2;
                case ZombieModeRewardType.ContractDevilBargain: case ZombieModeRewardType.ContractCursePool: return boss ? 4 : 3;
                case ZombieModeRewardType.ContractCursedReload: case ZombieModeRewardType.ContractBloodPrice: return boss ? 3 : 2;
                case ZombieModeRewardType.InsuranceNearFull: return 5;
                default: return GetZombieModeOptionTradeoffPollutionGain(reward);
            }
        }

        private void SpawnCoopZombieRewardNpc(ZombieModeRewardType reward)
        {
            string service = GetZombieModePendingTemporaryNpcServiceType(reward);
            if (!string.IsNullOrEmpty(service) && service != "Merchant") SpawnZombieModeTemporaryNpc(runState.RunId, service, IsZombieModeBossWave(runState.CurrentWave));
            if (reward == ZombieModeRewardType.TempGoblinNpc) SpawnZombieModeTemporaryRealNpc(runState.RunId, "Goblin");
            else if (reward == ZombieModeRewardType.TempNurseNpc) SpawnZombieModeTemporaryRealNpc(runState.RunId, "NurseNpc");
            else if (reward == ZombieModeRewardType.TempCourierNpc) SpawnZombieModeTemporaryRealNpc(runState.RunId, "Courier");
        }

        private bool ApplyCoopZombiePersonal(CoopZombiePersonal receipt)
        {
            if (receipt != null && receipt.Kind == "skip-entry") return ApplyCoopZombieSkippedEntry(receipt.Run);
            bool current = IsCoopZombieLocalAssetCurrent() && coopZombieWalletRoom == BossRushCoop.RunId && receipt.Run == runState.RunId && runState.LifecyclePhase != ZombieModeLifecyclePhase.None;
            if (current) RememberCoopZombieWallet();
            CoopZombieWallet wallet;
            if (!coopZombieWallets.TryGetValue(BossRushCoop.RunId + ":" + BossRushCoop.LocalAssetId + ":" + receipt.Run, out wallet)) return false;
            if (wallet.Extracted) return true;
            if (!current)
            {
                if (receipt.Kind == "points")
                {
                    wallet.Points = (int)Math.Min(int.MaxValue, (long)wallet.Points + Math.Max(1, receipt.Amount));
                    wallet.Received = (int)Math.Min(int.MaxValue, (long)wallet.Received + Math.Max(1, receipt.Amount));
                }
                else if (receipt.Kind == "extract" && !wallet.Extracted)
                {
                    if (wallet.Received < receipt.Amount) return false;
                    if (wallet.Points > 0 && !Duckov.Economy.EconomyManager.Add(wallet.Points)) return false;
                    wallet.Points = 0;
                    CampaignObjectiveTracker.ObserveExtractionLeaseWave(wallet.Campaign, wallet.MaxWave);
                    if (!CampaignObjectiveTracker.ReportExtractionLease(wallet.Campaign)) return false;
                    wallet.Extracted = true;
                }
                return true;
            }
            if (receipt.Kind == "points")
            {
                runState.PurificationPoints = (int)Math.Min(int.MaxValue, (long)runState.PurificationPoints + Math.Max(1, receipt.Amount));
                wallet.Points = runState.PurificationPoints;
                wallet.Received = (int)Math.Min(int.MaxValue, (long)wallet.Received + Math.Max(1, receipt.Amount));
            }
            else if (receipt.Kind == "extract")
            {
                if (wallet.Received < receipt.Amount) return false;
                if (!SettleZombieModeExtractionCashShell()) return false;
                wallet.Points = 0;
                CampaignObjectiveTracker.ObserveExtractionLeaseWave(wallet.Campaign, wallet.MaxWave);
                if (!CampaignObjectiveTracker.ReportExtractionLease(wallet.Campaign)) return false;
                wallet.Extracted = true;
                runState.ExtractionSuccessHandled = true;
                runState.CombatPhase = ZombieModeCombatPhase.SuccessExit;
            }
            return true;
        }

        private void DeliverCoopZombiePoints(int amount)
        {
            string id = runState.RunId + ":points:" + (++coopZombieReceiptSequence);
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (IsCoopZombieParticipant(player) && player.IsAlive)
                {
                    string key = player.Id + ":" + coopZombieParticipants[player.Id];
                    int issued; coopZombiePointsIssued.TryGetValue(key, out issued);
                    coopZombiePointsIssued[key] = (int)Math.Min(int.MaxValue, (long)issued + Math.Max(1, amount));
                    coopZombiePersonal.Deliver(player.Id, id,
                        new CoopZombiePersonal { Run = runState.RunId, Kind = "points", Amount = amount }, coopZombieParticipants[player.Id]);
                }
        }

        private bool CoopZombieAllPlayersInsideSafeZone()
        {
            if (!CoopZombieAnyParticipantAlive()) return false;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (IsCoopZombieParticipant(player) && player.IsAlive && !IsZombieModePositionInsideActiveSafeZone(player.Character.transform.position)) return false;
            return true;
        }

        private bool CoopZombieAllInsideExtraction(CountDownArea area)
        {
            if (!BossRushCoop.IsConnected) return IsPlayerInsideZombieModeExtractionArea(area);
            if (area == null || !CoopZombieAnyParticipantAlive()) return false;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (IsCoopZombieParticipant(player) && player.IsAlive)
                {
                    Vector3 offset = player.Character.transform.position - area.transform.position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude > ZombieModeTuning.ExtractionAreaTriggerRadius * ZombieModeTuning.ExtractionAreaTriggerRadius) return false;
                }
            return true;
        }

        private bool CoopZombieAnyParticipantAlive()
        {
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (IsCoopZombieParticipant(player) && player.IsAlive) return true;
            return false;
        }

        private CharacterMainControl GetCoopZombieCombatTarget(Vector3 origin)
        {
            if (!BossRushCoop.IsConnected) return CharacterMainControl.Main;
            CharacterMainControl nearest = null;
            float distance = float.PositiveInfinity;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!IsCoopZombieParticipant(player) || !player.IsAlive) continue;
                float candidate = (player.Character.transform.position - origin).sqrMagnitude;
                if (candidate < distance) { distance = candidate; nearest = player.Character; }
            }
            return nearest;
        }

        private CharacterMainControl GetCoopZombieSpawnAnchor(Vector3 origin)
        {
            // 导航点在显示 starter 界面前收集，此时个人准入表尚为空。
            // 这里只读初始地图的活人位置；战斗阶段仍限于已准入的原资产。
            if (BossRushCoop.IsConnected && runState.LifecyclePhase == ZombieModeLifecyclePhase.InitializingRun)
                return BossRushCoop.GetCombatTarget(origin);
            return GetCoopZombieCombatTarget(origin);
        }

        internal static CharacterMainControl GetCoopZombieTarget(Vector3 origin)
        {
            return coopZombieOwner != null ? coopZombieOwner.GetCoopZombieCombatTarget(origin) : BossRushCoop.GetCombatTarget(origin);
        }
    }
}

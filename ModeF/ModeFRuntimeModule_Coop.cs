using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopModeFPlayerState
    {
        public string Id, AssetId;
        public int Marks, Kills;
        public bool Dead, Extracted, Admitted, Declined;
        [NonSerialized] public bool StarterSent;
        [NonSerialized] public bool ExtractionLastAlive, ExtractionLastInside, ExtractionCountdownQualified;
    }

    [Serializable]
    internal sealed class CoopModeFState
    {
        public bool Active, ExtractionSpawned, ExtractionResolved;
        public string Epoch, Leader;
        public string Merchant;
        public int Phase, Session, LeaderMarks;
        public float Elapsed, Duration;
        public Vector3 ExtractionPosition;
        public string[] Bosses;
        public int[] Marks;
        public CoopModeFPlayerState[] Players;
        public CoopModeFFortificationState[] Fortifications;
        public float ExtractionElapsed;
    }

    [Serializable]
    internal sealed class CoopModeFPersonal
    {
        public string Epoch, Kind;
        public int VictimMarks, Marks, Kills;
        public string TransactionId;
        public bool TransactionSucceeded;
    }

    internal sealed partial class ModeFRuntimeModule
    {
        private sealed class CoopModeFRewardDebt
        {
            internal int TypeId;
            internal ItemStatsSystem.Data.ItemTreeData Data;
            internal bool Delivered;
        }

        private sealed class CoopModeFLocalLedger
        {
            internal string AssetId, Room;
            internal int Kills, PendingKill, ExtractionMarks = -1;
            internal bool Extracted, CampaignCaptured;
            internal CampaignObjectiveTracker.ExtractionLease Campaign;
            internal readonly List<CoopModeFRewardDebt> KillRewards = new List<CoopModeFRewardDebt>();
            internal readonly List<CoopModeFRewardDebt> ExtractionRewards = new List<CoopModeFRewardDebt>();
        }

        // 个人收据按房间重发，世界 epoch 变化不能抹去旧局欠账。
        private readonly Dictionary<string, CoopModeFLocalLedger> coopLocalLedgers = new Dictionary<string, CoopModeFLocalLedger>(StringComparer.Ordinal);
        private int coopModeFPrepaidBountyRewards = -1;
        private string coopLedgerRoom;
        private CoopGameplayChannel<CoopModeFState> coopChannel;
        private CoopPersonalDelivery<CoopModeFPersonal> coopPersonal;
        private readonly Dictionary<string, CoopModeFPlayerState> coopPlayers = new Dictionary<string, CoopModeFPlayerState>(StringComparer.Ordinal);
        private string coopEpoch, coopStartedEpoch;
        internal string CampaignEntryEpoch { get { return coopEpoch; } }
        internal bool TryGetCoopCampaignParticipantAsset(string expectedEpoch, string playerId, out string assetId)
        {
            assetId = null;
            CoopModeFPlayerState participant;
            if (expectedEpoch != coopEpoch || !coopPlayers.TryGetValue(playerId, out participant) || !participant.Admitted || participant.Declined) return false;
            assetId = participant.AssetId;
            return !string.IsNullOrEmpty(assetId);
        }
        private string coopLocalAsset;
        private readonly HashSet<string> coopStartedAssets = new HashSet<string>(StringComparer.Ordinal);
        private int coopAppliedKillCount;
        private string coopExtractedEpoch;
        private bool coopPersonalSettlementPending;
        private readonly Dictionary<string, bool> coopAdmissionDecisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        private float coopAdmissionRetry;

        private bool IsCoopModeFLocalAdmitted()
        { return !BossRushCoop.IsConnected || coopLocalAsset == BossRushCoop.LocalAssetId
            && coopStartedEpoch == BossRushCoop.LocalAssetId + ":" + coopEpoch; }

        private bool RetryCoopModeFAdmission()
        {
            string personalEpoch = BossRushCoop.LocalAssetId + ":" + coopEpoch;
            if (coopLocalAsset != BossRushCoop.LocalAssetId || !modeE.ReopenCoopEntryDecision("F:" + personalEpoch)) return false;
            ApplyCoopModeFPersonal(new CoopModeFPersonal { Epoch = coopEpoch, Kind = "start" });
            return IsCoopModeFLocalAdmitted();
        }
        private float coopExtractionElapsed;
        private Vector3 coopExtractionPosition;
        private bool coopExtractionAwaitingReceipts;

        private void InitializeCoopModeF()
        {
            coopChannel = new CoopGameplayChannel<CoopModeFState>("ModeF", CaptureCoopModeF, ApplyCoopModeF, ExecuteCoopModeF);
            coopPersonal = new CoopPersonalDelivery<CoopModeFPersonal>("ModeF", ApplyCoopModeFPersonal);
        }

        private bool TryHandleCoopModeFPersonalDeath()
        {
            if (!BossRushCoop.IsConnected || (!BossRushCoop.IsClient && !CoopModeFAnyParticipantAlive())) return false;
            if (modeFPlayerDeathHandled) return true;
            modeFPlayerDeathHandled = true;
            modeFState.PlayerBountyMarks = 0;
            CleanupModeFPlayerMaxHealthGrowth();
            EndModeFBloodfireOverload(false);
            MarkModeFPlayerNameTagDirty();
            return true;
        }

        private void FindCoopModeFBountyPlayer(CharacterMainControl boss, ref CharacterMainControl bestTarget, ref int bestMarks)
        {
            if (!BossRushCoop.IsConnected) return;
            foreach (BossRushCoopPlayer participant in BossRushCoop.Players)
            {
                int marks = GetCoopModeFPlayerMarks(participant.Character);
                if (participant.IsAlive && IsModeFValidCombatTarget(boss, participant.Character) && marks > bestMarks)
                {
                    bestTarget = participant.Character; bestMarks = marks;
                }
            }
        }

        private void BeginCoopModeF()
        {
            PreserveCoopModeFLocalLedger();
            coopEpoch = Guid.NewGuid().ToString("N");
            coopPlayers.Clear();
            coopLocalAsset = BossRushCoop.LocalAssetId;
            coopAppliedKillCount = 0;
            coopPersonalSettlementPending = false;
            coopExtractionAwaitingReceipts = false;
            coopStartedAssets.Clear();
            PreserveCoopModeFLocalLedger();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsAlive && !string.IsNullOrEmpty(player.AssetId))
                    coopPlayers[player.Id] = new CoopModeFPlayerState { Id = player.Id, AssetId = player.AssetId, Admitted = player.IsLocal };
            coopExtractionElapsed = 0f;
        }

        private string coopProjectionRoom;
        private bool coopProjectionRetiring;

        private void RetireLostCoopProjection()
        {
            if (coopProjectionRoom == null || (BossRushCoop.IsClient && coopProjectionRoom == BossRushCoop.RunId)) return;
            // 原客端镜像只属于原房间；网络停止或换房后先撤销，不能提升为单机世界。
            coopProjectionRetiring = true;
            try
            {
                PreserveCoopModeFLocalLedger();
                            coopExtractionAwaitingReceipts = coopPersonalSettlementPending = false;
                            ExitModeF(false);
            }
            finally { coopProjectionRetiring = false; coopProjectionRoom = null; }
        }

        private void TickCoopModeF(float deltaTime)
        {
            RetireLostCoopProjection();
            coopChannel.Tick(deltaTime);
            coopPersonal.Tick(deltaTime);
            coopAdmissionRetry -= Mathf.Max(0f, deltaTime);
            bool admitted;
            CoopModeFPlayerState confirmed;
            if (BossRushCoop.IsClient && modeFActive && coopLocalAsset == BossRushCoop.LocalAssetId && coopAdmissionRetry <= 0f
                && coopAdmissionDecisions.TryGetValue(BossRushCoop.LocalAssetId + ":" + coopEpoch, out admitted)
                && (!coopPlayers.TryGetValue(BossRushCoop.LocalPlayerId, out confirmed) || confirmed.AssetId != coopLocalAsset
                    || !(admitted ? confirmed.Admitted : confirmed.Declined)))
            { coopAdmissionRetry = 1f; coopChannel.RequestRepeat("admission", text: coopEpoch, flag: admitted); }
            TickCoopFortificationTransactions(deltaTime);
            UpdateCoopExtractionPresentation();
            if (BossRushCoop.IsHost && coopExtractionAwaitingReceipts && !coopPersonal.HasPendingForIdPrefix("extract:" + coopEpoch + ":"))
            {
                coopExtractionAwaitingReceipts = false;
                TryNotifyModeFExtraction(coopExtractionPosition);
                TryLoadBaseSceneAfterModeFExtraction();
            }
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || !modeFActive) return;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                CoopModeFPlayerState state;
                if (!coopPlayers.TryGetValue(player.Id, out state))
                {
                    state = new CoopModeFPlayerState { Id = player.Id, AssetId = player.AssetId };
                    coopPlayers.Add(player.Id, state);
                }
                if (!IsCoopModeFParticipant(player, state, false)) continue;
                // 断线只挂起参与者；真正死亡按原规则清空其个人印记。
                if (player.IsConnected && player.Character != null && player.Character.Health != null)
                {
                    bool dead = player.Character.Health.IsDead;
                    if (dead && !state.Dead) { state.Marks = 0; state.Dead = true; MarkModeFBountyLeaderDirty(); }
                    if (!dead) state.Dead = false;
                }
                if (player.IsAlive && !state.StarterSent && coopPersonal.Deliver(player.Id, "start:" + coopEpoch,
                    new CoopModeFPersonal { Epoch = coopEpoch, Kind = "start" }, state.AssetId)) state.StarterSent = true;
            }
            if (!CoopModeFAnyParticipantAlive() && coopPlayers.Count > 0)
            {
                modeFPlayerDeathHandled = false;
                OnModeFPlayerDeath();
                return;
            }
            if (!modeFState.ExtractionPointSpawned || modeFState.ExtractionResolved) return;
            RefreshCoopModeFExtractionEligibility();
            bool inside = true;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                CoopModeFPlayerState state;
                if (player.IsAlive && coopPlayers.TryGetValue(player.Id, out state) && IsCoopModeFParticipant(player, state)
                    && (player.Character.transform.position - coopExtractionPosition).sqrMagnitude > 9f) { inside = false; break; }
            }
            coopExtractionElapsed = inside ? coopExtractionElapsed + Mathf.Max(0f, deltaTime) : 0f;
            if (coopExtractionElapsed >= MODEF_EXTRACTION_COUNTDOWN_SECONDS) CompleteCoopModeFExtraction();
        }

        private CoopModeFState CaptureCoopModeF()
        {
            List<string> ids = new List<string>(); List<int> marks = new List<int>();
            foreach (CharacterMainControl boss in modeFState.ActiveBosses)
            {
                string id = BossRushCoop.GetEntityId(boss);
                if (string.IsNullOrEmpty(id)) continue;
                int count; modeFState.BountyMarksByCharacterId.TryGetValue(boss.GetInstanceID(), out count);
                ids.Add(id); marks.Add(count);
            }
            return new CoopModeFState
            {
                Active = modeFActive, Epoch = coopEpoch, Phase = (int)modeFState.CurrentPhase,
                Merchant = merchantRuntime.CoopEntity,
                Session = modeFState.RuntimeSessionToken, Elapsed = modeFState.PhaseElapsed, Duration = modeFState.PhaseDuration,
                Leader = BossRushCoop.GetEntityId(modeFState.CurrentBountyLeader), LeaderMarks = modeFState.CurrentBountyLeaderMarks,
                ExtractionSpawned = modeFState.ExtractionPointSpawned, ExtractionResolved = modeFState.ExtractionResolved,
                ExtractionPosition = coopExtractionPosition, Bosses = ids.ToArray(), Marks = marks.ToArray(),
                Players = new List<CoopModeFPlayerState>(coopPlayers.Values).ToArray(),
                Fortifications = CaptureCoopFortifications(), ExtractionElapsed = coopExtractionElapsed
            };
        }

        private void ApplyCoopModeF(CoopModeFState state)
        {
            RetireLostCoopProjection();
            if (BossRushCoop.IsClient && state.Active) coopProjectionRoom = BossRushCoop.RunId;
            PreserveCoopModeFLocalLedger();
            bool started = state.Active && (!modeFActive || coopEpoch != state.Epoch);
            // 新局到达时撤掉旧局临时成长/过载；旧物品欠账已由独立账本保留。
            if (modeFActive && state.Active && coopEpoch != state.Epoch) ExitModeF(false);
            bool owesSettlement = false;
            if (!state.Active && state.Players != null && coopExtractedEpoch != state.Epoch)
                foreach (CoopModeFPlayerState participant in state.Players)
                    if (participant != null && participant.Id == BossRushCoop.LocalPlayerId
                        && participant.AssetId == coopLocalAsset && participant.Extracted) { owesSettlement = true; break; }
            // inactive 世界快照可能先于个人击杀/撤离收据。保留个人补给和成长账本，收据完成后再退出。
            if (modeFActive && !state.Active && !owesSettlement) ExitModeF(false);
            coopPersonalSettlementPending = owesSettlement;
            coopEpoch = state.Epoch;
            modeFActive = state.Active || owesSettlement; modeFState.IsActive = state.Active;
            modeFState.RuntimeSessionToken = state.Session;
            modeFState.CurrentPhase = (ModeFPhase)state.Phase;
            modeFState.PhaseElapsed = state.Elapsed; modeFState.PhaseDuration = state.Duration;
            modeFState.CurrentBountyLeader = BossRushCoop.FindEntity(state.Leader);
            modeFState.CurrentBountyLeaderMarks = state.LeaderMarks;
            modeFState.ExtractionPointSpawned = state.ExtractionSpawned;
            modeFState.ExtractionResolved = state.ExtractionResolved;
            coopExtractionPosition = state.ExtractionPosition;
            coopExtractionElapsed = state.ExtractionElapsed;
            coopPlayers.Clear();
            if (state.Players != null) foreach (CoopModeFPlayerState player in state.Players)
                if (player != null && !string.IsNullOrEmpty(player.Id)) coopPlayers[player.Id] = player;
            if (started)
            {
                coopLocalAsset = BossRushCoop.LocalAssetId;
                coopAppliedKillCount = 0;
                coopStartedAssets.Clear();
                PreserveCoopModeFLocalLedger();
                modeFState.PlayerKillCount = modeFState.PlayerBountyMarks = 0;
                modeFState.TempMaxHealthGrowth = modeFState.BloodfireCharge = modeFState.BloodfireOverloadRemaining = 0f;
                modeFState.BloodfireOverloadActive = false;
                modeFPlayerDeathHandled = false;
                InitializeModeDItemPools();
                equipment.EnsureModeDGlobalItemPool();
                EnsureModeFPlayerNameTag();
            }
            if (state.Active) merchantRuntime.BindCoopEntity(state.Merchant);
            modeFState.ActiveBosses.Clear(); modeFActiveBossSet.Clear(); modeFState.BountyMarksByCharacterId.Clear();
            if (state.Bosses != null && state.Marks != null)
                for (int i = 0; i < Math.Min(state.Bosses.Length, state.Marks.Length); i++)
                {
                    CharacterMainControl boss = BossRushCoop.FindEntity(state.Bosses[i]);
                    if (boss == null) continue;
                    CoopAiPredictionHealthProtection.MarkReplica(boss);
                    modeFState.ActiveBosses.Add(boss); modeFActiveBossSet.Add(boss);
                    modeFState.BountyMarksByCharacterId[boss.GetInstanceID()] = state.Marks[i];
                }
            if (state.ExtractionSpawned && modeFExtractionMapMarker == null) CreateModeFExtractionMapMarker(state.ExtractionPosition);
            ApplyCoopFortifications(state.Fortifications);
            UpdateCoopExtractionPresentation();
            MarkModeFHealthBarNamesDirty();
        }

        private bool ApplyCoopModeFPersonal(CoopModeFPersonal receipt)
        {
            if (!string.IsNullOrEmpty(receipt.TransactionId)) return ApplyCoopFortificationReceipt(receipt);
            if (CharacterMainControl.Main == null || string.IsNullOrEmpty(receipt.Epoch)) return false;
            if (receipt.Epoch == coopEpoch) PreserveCoopModeFLocalLedger();
            CoopModeFLocalLedger ledger;
            if (!coopLocalLedgers.TryGetValue(receipt.Epoch, out ledger) || ledger.AssetId != BossRushCoop.LocalAssetId) return false;
            bool current = receipt.Epoch == coopEpoch && modeFActive && ledger.Room == BossRushCoop.RunId;
            if (receipt.Kind == "start")
            {
                // 已结束的入场邀请不再付款、发装或改变新局准入。
                if (!current) return true;
                string personalEpoch = BossRushCoop.LocalAssetId + ":" + receipt.Epoch;
                if (coopStartedEpoch == personalEpoch || coopStartedAssets.Contains(personalEpoch)) return true;
                if (coopLocalAsset != BossRushCoop.LocalAssetId) return false;
                if (!modeFActive) return false;
                bool admitted = modeE.CommitCoopModeEntry("F:" + personalEpoch, true, delegate
                {
                    modeE.ModeEGiveColdWeatherGear();
                    GiveModeFItem(FoldableCoverPackConfig.TYPE_ID, L10n.T("折叠掩体包", "Foldable Cover Pack"));
                });
                coopAdmissionDecisions[personalEpoch] = admitted;
                coopChannel.Request("admission", text: receipt.Epoch, flag: admitted);
                if (!admitted) return true;
                modeFState.InitialMaxHealthSnapshot = CharacterMainControl.Main.Health.MaxHealth;
                coopStartedEpoch = personalEpoch;
                coopStartedAssets.Add(personalEpoch);
                PreserveCoopModeFLocalLedger();
                return true;
            }
            if (receipt.Kind == "kill")
            {
                if (receipt.Kills <= ledger.Kills) return true;
                // 个人收据可乱序；只沿连续击杀序号应用成长、阶梯补给和印记。
                if (receipt.Kills != ledger.Kills + 1) return false;
                if (current && !modeFActive && !coopPersonalSettlementPending) return false;
                if (!CampaignObjectiveTracker.ObserveExtractionLeaseKill(ledger.Campaign, receipt.Kills, receipt.VictimMarks > 0)) return false;
                if (ledger.PendingKill == 0)
                {
                    ledger.PendingKill = receipt.Kills;
                    foreach (int typeId in GetModeFKillRewardTypeIds(receipt.Kills))
                        ledger.KillRewards.Add(new CoopModeFRewardDebt { TypeId = typeId });
                    if (current)
                    {
                        modeFState.PlayerBountyMarks = receipt.Marks; modeFState.PlayerKillCount = receipt.Kills;
                        var reward = ApplyModeFKillReward(receipt.VictimMarks > 0, receipt.VictimMarks);
                        ShowModeFRewardBubble(BuildModeFKillRewardBubbleText(receipt.VictimMarks > 0,
                            reward.healAmount, reward.maxHealthGain, reward.bloodfireGain, reward.overloadStarted, reward.overloadExtension), reward.overloadStarted ? 4f : 2.5f);
                        MarkModeFPlayerNameTagDirty();
                    }
                }
                if (current)
                {
                    foreach (CoopModeFRewardDebt debt in ledger.KillRewards)
                        if (!debt.Delivered)
                        {
                            if (!TryGiveItemToPlayerOrDrop(debt.TypeId, GetModeFUtilityItemDisplayName(debt.TypeId))) return false;
                            debt.Delivered = true;
                        }
                }
                else if (!TryStoreCoopModeFRewards(ledger.KillRewards)) return false;
                ledger.KillRewards.Clear(); ledger.PendingKill = 0; ledger.Kills = receipt.Kills;
                if (current && receipt.Epoch == coopEpoch && ledger.AssetId == BossRushCoop.LocalAssetId) coopAppliedKillCount = receipt.Kills;
                return true;
            }
            if (receipt.Kind == "extract")
            {
                if (ledger.Extracted) return true;
                if (ledger.Kills < receipt.Kills) return false;
                if (ledger.ExtractionMarks < 0)
                {
                    ledger.ExtractionMarks = Math.Max(0, receipt.Marks);
                    for (int i = 0; i < ledger.ExtractionMarks; i++) ledger.ExtractionRewards.Add(new CoopModeFRewardDebt());
                }
                if (!TryStoreCoopModeFRewards(ledger.ExtractionRewards)) return false;
                if (!CampaignObjectiveTracker.ReportExtractionLease(ledger.Campaign)) return false;
                if (!current)
                {
                    ledger.Extracted = true; ledger.ExtractionRewards.Clear();
                    modeFSuccessfulExtractionCount++;
                    return true;
                }
                // 结算收据可能晚于 inactive 快照；只重建本人的结算字段，不恢复世界生成。
                modeFActive = true; modeFState.IsActive = true;
                modeFState.PlayerBountyMarks = receipt.Marks; modeFState.PlayerKillCount = receipt.Kills;
                coopModeFPrepaidBountyRewards = ledger.ExtractionMarks;
                try { OnModeFExtractionSuccess(); }
                finally { coopModeFPrepaidBountyRewards = -1; }
                if (!modeFActive) { ledger.Extracted = true; ledger.ExtractionRewards.Clear(); coopExtractedEpoch = receipt.Epoch; coopPersonalSettlementPending = false; }
                return !modeFActive;
            }
            return true;
        }

        private void PreserveCoopModeFLocalLedger()
        {
            string room = BossRushCoop.RunId;
            if (!string.IsNullOrEmpty(room) && room != coopLedgerRoom)
            {
                coopLedgerRoom = room;
                var completed = new List<string>();
                foreach (var entry in coopLocalLedgers)
                    if (entry.Value.Extracted && entry.Value.Room != room) completed.Add(entry.Key);
                foreach (string epoch in completed) coopLocalLedgers.Remove(epoch);
            }
            if (string.IsNullOrEmpty(coopEpoch) || string.IsNullOrEmpty(coopLocalAsset)) return;
            CoopModeFLocalLedger ledger;
            if (!coopLocalLedgers.TryGetValue(coopEpoch, out ledger))
            {
                ledger = new CoopModeFLocalLedger { AssetId = coopLocalAsset, Room = room, Kills = coopAppliedKillCount, Extracted = coopExtractedEpoch == coopEpoch };
                coopLocalLedgers.Add(coopEpoch, ledger);
            }
            if (!ledger.CampaignCaptured && ledger.AssetId == BossRushCoop.LocalAssetId && IsCoopModeFLocalAdmitted())
            {
                ledger.Campaign = owner.IsCampaignConfiguredEnabled() ? CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF) : null;
                ledger.CampaignCaptured = true;
            }
        }

        private bool TryStoreCoopModeFRewards(List<CoopModeFRewardDebt> debts)
        {
            bool complete = true, staged = false;
            foreach (CoopModeFRewardDebt debt in debts)
            {
                if (debt.Delivered) continue;
                Item item = null;
                try
                {
                    if (debt.TypeId <= 0) debt.TypeId = arena.GetRandomInfiniteHellHighQualityRewardTypeID();
                    if (debt.TypeId <= 0) { complete = false; break; }
                    if (debt.Data == null)
                    {
                        item = ItemAssetsCollection.InstantiateSync(debt.TypeId);
                        if (item == null) { complete = false; break; }
                        debt.Data = ItemStatsSystem.Data.ItemTreeData.FromItem(item);
                        if (debt.Data == null) { complete = false; break; }
                    }
                    // 官方 Push(true) 也写此缓冲；保留同一树对象可识别 SaveBuffer 抛错后的重试。
                    if (!IsCoopModeFRewardBuffered(debt.Data)) PlayerStorageBuffer.Buffer.Add(debt.Data);
                    staged = true;
                }
                catch (Exception e) { ModBehaviour.DevLog("[ModeF] 个人奖励暂存失败: " + e.Message); complete = false; break; }
                finally
                {
                    if (item != null) SafeRuntime.Run("ModeF personal reward materialization cleanup", item.DestroyTree);
                }
            }
            if (staged)
            {
                try { PlayerStorageBuffer.SaveBuffer(); }
                catch (Exception e) { ModBehaviour.DevLog("[ModeF] 个人奖励存盘待重试: " + e.Message); return false; }
                foreach (CoopModeFRewardDebt debt in debts)
                    if (!debt.Delivered && debt.Data != null && IsCoopModeFRewardBuffered(debt.Data))
                    { debt.Delivered = true; debt.Data = null; }
            }
            return complete;
        }

        private static bool IsCoopModeFRewardBuffered(ItemStatsSystem.Data.ItemTreeData data)
        {
            foreach (ItemStatsSystem.Data.ItemTreeData buffered in PlayerStorageBuffer.Buffer)
                if (buffered != null && (object.ReferenceEquals(buffered, data)
                    || buffered.rootInstanceID == data.rootInstanceID && buffered.RootTypeID == data.RootTypeID)) return true;
            return false;
        }

        private void DeliverCoopModeFKill(CharacterMainControl victim, CharacterMainControl killer)
        {
            string id = BossRushCoop.GetPlayerId(killer);
            CoopModeFPlayerState state;
            if (string.IsNullOrEmpty(id) || !coopPlayers.TryGetValue(id, out state))
            {
                if (string.IsNullOrEmpty(id)) return;
                BossRushCoopPlayer participant = BossRushCoop.FindPlayer(id);
                if (participant == null || string.IsNullOrEmpty(participant.AssetId)) return;
                state = new CoopModeFPlayerState { Id = id, AssetId = participant.AssetId }; coopPlayers[id] = state;
            }
            if (!IsCoopModeFParticipant(BossRushCoop.FindPlayer(id), state)) return;
            int marks; modeFState.BountyMarksByCharacterId.TryGetValue(victim.GetInstanceID(), out marks);
            LatchModeFPlayerBountyKill(victim.GetInstanceID(), marks > 0);
            modeFState.BountyMarksByCharacterId.Remove(victim.GetInstanceID());
            state.Kills++; if (marks > 0) state.Marks++;
            coopPersonal.Deliver(id, "kill:" + coopEpoch + ":" + victim.GetInstanceID(), new CoopModeFPersonal
            { Epoch = coopEpoch, Kind = "kill", VictimMarks = marks, Marks = state.Marks, Kills = state.Kills }, state.AssetId);
            ApplyModeFPhasePressure(); MarkModeFBountyLeaderDirty(killer); RefreshModeFBountyLeaderIfDirty();
        }

        private int GetCoopModeFPlayerMarks(CharacterMainControl character)
        {
            if (!BossRushCoop.IsConnected) return character == CharacterMainControl.Main ? modeFState.PlayerBountyMarks : 0;
            string id = BossRushCoop.GetPlayerId(character); CoopModeFPlayerState state;
            return id != null && coopPlayers.TryGetValue(id, out state) && IsCoopModeFParticipant(BossRushCoop.FindPlayer(id), state)
                && !state.Dead && !state.Extracted ? state.Marks : 0;
        }

        private static bool IsCoopModeFParticipant(BossRushCoopPlayer player, CoopModeFPlayerState state, bool requireAdmission = true)
        { return player != null && state != null && !string.IsNullOrEmpty(state.AssetId) && state.AssetId == player.AssetId
            && !state.Declined && (!requireAdmission || state.Admitted); }

        private bool CoopModeFAnyParticipantAlive()
        {
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                CoopModeFPlayerState state;
                if (player.IsAlive && coopPlayers.TryGetValue(player.Id, out state) && IsCoopModeFParticipant(player, state, false)) return true;
            }
            return false;
        }

        private void TickCoopModeFPersonal(float deltaTime)
        {
            ModeFStatusHud.Tick(IsCoopModeFLocalAdmitted() ? modeFState : null, MODEF_BLOODFIRE_MAX_CHARGE);
            if (!modeFActive || !modeFState.IsActive || !IsCoopModeFLocalAdmitted()) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) { OnModeFPlayerDeath(); return; }
            modeFPlayerDeathHandled = false;
            TickModeFBloodfireOverload(deltaTime); ApplyModeFBleedDamage(player, deltaTime);
            UpdateModeFPlayerNameTag(); UpdateModeFBountyRadarUI();
            UpdateModeFFortificationHighlights(); UpdateFortPlacementMode(); UpdateModeFRepairSelection();
            if (Time.frameCount % 120 == 0) UpdateModeFPendingUtilityRewards();
        }

        private void RefreshCoopModeFExtractionEligibility()
        {
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                CoopModeFPlayerState state;
                if (!player.IsConnected || !coopPlayers.TryGetValue(player.Id, out state)
                    || !IsCoopModeFParticipant(player, state)) continue;
                state.ExtractionLastAlive = player.IsAlive && !state.Dead;
                state.ExtractionLastInside = state.ExtractionLastAlive
                    && (player.Character.transform.position - coopExtractionPosition).sqrMagnitude <= 9f;
                state.ExtractionCountdownQualified = state.ExtractionLastAlive && coopExtractionElapsed > 0f;
            }
        }

        private bool IsCoopModeFExtractionRecipient(CoopModeFPlayerState state)
        {
            if (state == null || !state.Admitted || state.Declined || state.Dead) return false;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(state.Id);
            if (player != null && player.IsConnected) return IsCoopModeFParticipant(player, state) && player.IsAlive;
            // 只保留最后一次真实圈内或倒计时开始后的断线资格；不为早离圈外者补资格。
            return state.ExtractionLastAlive && (state.ExtractionLastInside || state.ExtractionCountdownQualified);
        }

        private void CompleteCoopModeFExtraction()
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || modeFState.ExtractionResolved) return;
            modeFState.ExtractionResolved = true;
            RefreshCoopModeFExtractionEligibility();
            foreach (CoopModeFPlayerState state in coopPlayers.Values)
            {
                if (!IsCoopModeFExtractionRecipient(state)) continue;
                state.Extracted = true;
                coopPersonal.Deliver(state.Id, "extract:" + coopEpoch + ":", new CoopModeFPersonal
                { Epoch = coopEpoch, Kind = "extract", Marks = state.Marks, Kills = state.Kills }, state.AssetId);
            }
            if (modeFActive) ExitModeF();
            coopExtractionAwaitingReceipts = true;
        }

        private void ExecuteCoopModeF(CoopGameplayCommand command)
        {
            if (command.Action == "admission")
            {
                CoopModeFPlayerState state;
                if (command.Text == coopEpoch && coopPlayers.TryGetValue(command.PlayerId, out state) && state.AssetId == command.AssetId)
                { state.Admitted = command.Flag; state.Declined = !command.Flag; }
                return;
            }
            if (command.Action == "fortification") { ExecuteCoopFortification(command); return; }
            if (command.Action == "fortification-hit") { ExecuteCoopFortificationHit(command); return; }
            if (!BossRushCoop.SessionReady || owner == null) return;
            if (command.Action == "enter" && !modeFActive && owner.IsCurrentSceneValidBossRushArena()) TryStartModeF();
        }
    }
}

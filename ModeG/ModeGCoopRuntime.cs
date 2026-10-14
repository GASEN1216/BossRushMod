using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    [Serializable] internal sealed class ModeGCoopActor { public int Id, Slot; public string Key; public float MaxHealth; }
    [Serializable] internal sealed class ModeGCoopParticipant { public string Id, Asset; public long Order; }
    [Serializable] internal sealed class ModeGCoopWorld
    {
        public ulong Id, Seed; public long Counter, Created; public int Wave, Phase, Exit, Format, Expected, Resolved, Committed;
        public string SceneName, SceneId, Verification; public string[] Signatures; public ModeGCoopActor[] Actors;
        public ModeGCoopParticipant[] Participants;
        public bool Active, LastStand, Intermission, ActorsComplete; public float LastSeconds, RestSeconds;
    }
    [Serializable] internal sealed class ModeGCoopCombat
    {
        public ulong Run; public int Wave, Boss, Weapon, DamageType, Crit, Phase; public long Sequence, Order;
        public string Source; public float Damage, Final; public bool Explosion, Effect, Dead, Admission, AdmissionDenied;
        public Vector3 Point, SourcePosition;
    }

    /// <summary>频道属于稳定入口服务，单局实体在每端保留本人的遥测、契约、档案与物品事务。</summary>
    internal sealed partial class ModeGEntryRuntime
    {
        private static ModeGEntryRuntime coopOwner;
        private CoopGameplayChannel<ModeGCoopWorld> coop;
        private CoopPersonalDelivery<ModeGCoopCombat> coopCombat;
        private ModeGCoopWorld coopLatest;
        private bool coopWaitingEntry;
        private long coopCombatSequence;
        private float coopEntryDeadline;
        private ulong coopDeclinedRun;
        private string coopWaitingAsset;
        private ModeGCoopWorld coopFinalWorld;
        private readonly HashSet<string> coopDeparted = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> coopRecipientOrders = new Dictionary<string, long>(StringComparer.Ordinal);
        private ulong coopOrderRun;
        private string coopReceiptRoom;
        private readonly HashSet<string> coopClosedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> coopDeclinedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModeGCoopCombat> coopAdmissionFacts = new Dictionary<string, ModeGCoopCombat>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> coopUnqualifiedOrders = new Dictionary<string, long>(StringComparer.Ordinal);
        private float coopAdmissionRetry;
        private float coopAdmissionDeadline;
        private void RefreshCoopReceiptRoom()
        {
            string room = BossRushCoop.RunId;
            if (string.IsNullOrEmpty(room) || room == coopReceiptRoom) return;
            coopReceiptRoom = room;
            coopClosedRuns.Clear();
            coopDeclinedRuns.Clear();
            coopAdmissionFacts.Clear();
            coopUnqualifiedOrders.Clear();
            coopLatest = null; coopDeclinedRun = 0;
        }
        internal void EnsureCoop()
        {
            if (coop != null) return;
            coopOwner = this;
            coop = new CoopGameplayChannel<ModeGCoopWorld>("G", CaptureCoopWorld, ApplyCoopWorld, ExecuteCoopCommand);
            coopCombat = new CoopPersonalDelivery<ModeGCoopCombat>("G.Combat", ApplyCoopCombat);
        }
        internal static void DisposeCoopOwner()
        {
            if (coopOwner == null) return;
            if (coopOwner.coop != null) coopOwner.coop.Dispose();
            if (coopOwner.coopCombat != null) coopOwner.coopCombat.Dispose();
            coopOwner.coop = null; coopOwner.coopCombat = null; coopOwner = null;
        }
        private bool QueueCoopEntry()
        {
            EnsureCoop();
            if (!BossRushCoop.IsClient || (coopLatest != null && coopLatest.Active)) return false;
            if (!coopWaitingEntry)
            {
                coopWaitingEntry = true; coopWaitingAsset = BossRushCoop.LocalAssetId; coopEntryDeadline = Time.realtimeSinceStartup + 120f;
                coop.Request("join");
                ShowMessage(L10n.T("正在等待房主准备宿命回响。", "Waiting for the host to prepare Fate Echo."));
            }
            return true;
        }
        private ModeGEntryPreview FreezeCoopPreview(ModeGEntryPreview personal)
        {
            if (!BossRushCoop.IsClient || coopLatest == null) return personal;
            return new ModeGEntryPreview(coopLatest.Seed, coopLatest.Counter, (ModeGRunFormat)coopLatest.Format,
                coopLatest.Signatures, personal.contractCandidateIds, coopLatest.SceneName, coopLatest.SceneId,
                coopLatest.Verification, DateTime.UtcNow.Ticks);
        }
        private void ExecuteCoopCommand(CoopGameplayCommand command)
        {
            if (command.Action == "join" && !modeGActive) TryStartModeG();
            if (command.Action == "leave") coopDeparted.Add(command.PlayerId);
            if (command.Action == "admitted") AdmitCoopParticipant(command);
        }
        private void AdmitCoopParticipant(CoopGameplayCommand command)
        {
            ulong run; long requested;
            string[] fields = (command.Text ?? string.Empty).Split(':');
            if (fields.Length != 2 || !ulong.TryParse(fields[0], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out run)
                || !long.TryParse(fields[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out requested) || requested < 0
                || modeGRuntime == null || modeGRuntime.State == null || modeGRuntime.State.IsTerminal
                || modeGRuntime.State.runId != run || modeGRuntime.CoopRunRoom != BossRushCoop.RunId) return;
            RefreshCoopOrders(run);
            if (modeGRuntime.GetCoopParticipantAsset(command.PlayerId) != command.AssetId)
            {
                coopCombat.Deliver(command.PlayerId, run.ToString("x") + ":admission-denied:" + command.AssetId,
                    new ModeGCoopCombat { Run = run, AdmissionDenied = true }, command.AssetId);
                return;
            }
            long sent; coopRecipientOrders.TryGetValue(command.PlayerId, out sent);
            if (requested > sent) return;
            ModeGCoopCombat baseline;
            if (!coopAdmissionFacts.TryGetValue(command.PlayerId, out baseline))
            {
                // 冻结客户端实际启动前已知的事实边界；命令在途期间的新伤害仍须提交。
                baseline = new ModeGCoopCombat { Run = run, Order = requested, Sequence = ++coopCombatSequence, Admission = true };
                coopAdmissionFacts.Add(command.PlayerId, baseline);
            }
            if (baseline.Order != requested) return;
            coopDeparted.Remove(command.PlayerId);
            coopCombat.Deliver(command.PlayerId, run.ToString("x") + ":admission", baseline, command.AssetId);
        }
        private void RefreshCoopOrders(ulong run)
        {
            if (coopOrderRun == run) return;
            coopOrderRun = run; coopRecipientOrders.Clear(); coopDeparted.Clear(); coopAdmissionFacts.Clear();
        }
        private void NotifyCoopAdmission()
        {
            if (BossRushCoop.IsClient && modeGRuntime != null)
            {
                RefreshCoopReceiptRoom();
                long order; coopUnqualifiedOrders.TryGetValue(BossRushCoop.RunId + ":" + modeGRuntime.State.runId.ToString("x") + ":" + BossRushCoop.LocalAssetId, out order);
                modeGRuntime.FreezeCoopAdmission(coopLatest, order);
            }
            coopAdmissionRetry = 0f;
            coopAdmissionDeadline = Time.realtimeSinceStartup + 120f;
            RetryCoopAdmission(0f);
        }
        private void RetryCoopAdmission(float deltaTime)
        {
            if (!BossRushCoop.IsClient || modeGRuntime == null || modeGRuntime.State == null || modeGRuntime.State.IsTerminal
                || !modeGRuntime.IsCoopAssetCurrent || modeGRuntime.CoopAdmissionReady) return;
            if (Time.realtimeSinceStartup >= coopAdmissionDeadline)
            { modeGRuntime.End(ModeGExitReason.TechnicalIntegrityLoss); return; }
            coopAdmissionRetry -= Mathf.Max(0f, deltaTime);
            if (coopAdmissionRetry > 0f) return;
            coopAdmissionRetry = 1f;
            EnsureCoop(); coop.Request("admitted", 0, modeGRuntime.State.runId.ToString("x") + ":"
                + modeGRuntime.CoopRequestedAdmissionBaseline.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        private bool IsCoopRunClosedForLocalAsset()
        {
            RefreshCoopReceiptRoom();
            return BossRushCoop.IsClient && coopLatest != null && coopLatest.Active
                && coopClosedRuns.Contains(BossRushCoop.RunId + ":" + coopLatest.Id.ToString("x") + ":" + BossRushCoop.LocalAssetId);
        }
        private bool IsCoopParticipantAssetRejected()
        {
            if (!BossRushCoop.IsClient || coopLatest == null || !coopLatest.Active || coopLatest.Participants == null) return false;
            foreach (ModeGCoopParticipant participant in coopLatest.Participants)
                if (participant != null && participant.Id == BossRushCoop.LocalPlayerId)
                    return participant.Asset != BossRushCoop.LocalAssetId;
            return false;
        }
        private ModeGCoopWorld CaptureCoopWorld()
        {
            if (modeGRuntime == null || modeGRuntime.State == null) return coopFinalWorld;
            RefreshCoopOrders(modeGRuntime.State.runId);
            ModeGCoopWorld world = modeGRuntime.CaptureCoopWorld();
            var participants = new List<ModeGCoopParticipant>();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsConnected) continue;
                long order; coopRecipientOrders.TryGetValue(player.Id, out order);
                participants.Add(new ModeGCoopParticipant { Id = player.Id, Asset = modeGRuntime.GetCoopParticipantAsset(player.Id), Order = order });
            }
            world.Participants = participants.ToArray(); return world;
        }
        private void ApplyCoopWorld(ModeGCoopWorld state)
        {
            if (state == null || state.Wave < 0 || state.Wave >= ModeGWavePlan.WaveCount || state.Actors == null || state.Actors.Length > 3) return;
            if (state.Participants != null)
            {
                if (state.Participants.Length > 16) return;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ModeGCoopParticipant participant in state.Participants)
                    if (participant == null || string.IsNullOrEmpty(participant.Id) || string.IsNullOrEmpty(participant.Asset)
                        || participant.Asset.Length > 80 || participant.Order < 0 || !ids.Add(participant.Id)) return;
            }
            RefreshCoopReceiptRoom();
            if (modeGRuntime != null && modeGRuntime.State != null && !modeGRuntime.State.IsTerminal
                && (modeGRuntime.State.runId != state.Id || modeGRuntime.CoopRunRoom != BossRushCoop.RunId))
            { modeGRuntime.End(ModeGExitReason.SceneChanged); ShutdownModeG(); }
            coopLatest = state;
            if (state.Active && state.Expected > 0 && modeGRuntime == null && coopDeclinedRun != state.Id)
            { coopWaitingEntry = true; coopWaitingAsset = BossRushCoop.LocalAssetId; coopEntryDeadline = Time.realtimeSinceStartup + 120f; }
            if (modeGRuntime != null && modeGRuntime.State != null && modeGRuntime.State.runId == state.Id)
                modeGRuntime.ApplyCoopWorld(state);
        }
        private void TickCoopEntry(float deltaTime)
        {
            RefreshCoopReceiptRoom();
            EnsureCoop();
            if (modeGRuntime != null && !modeGRuntime.CheckCoopAsset() && modeGRuntime.State.IsTerminal) ShutdownModeG();
            coop.Tick(deltaTime); coopCombat.Tick(deltaTime);
            RetryCoopAdmission(deltaTime);
            if (coopWaitingEntry && coopWaitingAsset != BossRushCoop.LocalAssetId)
            { coopWaitingEntry = false; BossRushMapSelectionHelper.ClearPendingEntryFlowState(); return; }
            if (BossRushCoop.IsClient && coopLatest != null && coopDeclinedRun == coopLatest.Id)
                BossRushCoop.MarkSceneReady(coopLatest.SceneId + ":G:" + coopLatest.Id.ToString("x") + ":wave:" + coopLatest.Wave);
            if (!BossRushCoop.IsClient || !coopWaitingEntry) return;
            if (coopLatest != null && coopLatest.Active && BossRushCoop.SessionReady)
            {
                coopWaitingEntry = false;
                if (!TryStartModeG())
                {
                    coopDeclinedRun = coopLatest.Id;
                    coopDeclinedRuns.Add(BossRushCoop.RunId + ":" + coopDeclinedRun.ToString("x") + ":" + BossRushCoop.LocalAssetId);
                    coop.Request("leave");
                }
            }
            else if (!BossRushCoop.IsConnected || Time.realtimeSinceStartup >= coopEntryDeadline)
            {
                coopWaitingEntry = false; TryRefundModeGPendingPrepaidTicket();
                ShowMessage(L10n.T("等待宿命回响超时，船票已按入场事务返还。", "Fate Echo preparation timed out; the entry transaction returned your ticket."));
            }
        }
        internal static void PublishCoopCombat(Health health, DamageInfo info, bool dead)
        {
            if (!BossRushCoop.IsHost || coopOwner == null || coopOwner.modeGRuntime == null || health == null) return;
            ModeGRuntimeModule run = coopOwner.modeGRuntime; ModeGRunState state = run.State;
            if (state == null || !state.IsRegisteredBossHealth(health)) return;
            CharacterMainControl actor = health.GetComponent<CharacterMainControl>(); int ai = BossRushCoop.GetAiId(actor);
            if (ai == 0) return;
            var fact = new ModeGCoopCombat { Run = state.runId, Wave = state.waveEpoch, Boss = ai,
                Weapon = info.fromWeaponItemID, DamageType = (int)info.damageType, Crit = info.crit,
                Source = BossRushCoop.GetEntityId(info.fromCharacter), Damage = info.damageValue, Final = info.finalDamage,
                Explosion = info.isExplosion, Effect = info.isFromBuffOrEffect, Point = info.damagePoint, Dead = dead,
                SourcePosition = info.fromCharacter != null ? info.fromCharacter.transform.position : info.damagePoint,
                Sequence = ++coopOwner.coopCombatSequence };
            fact.Phase = (int)state.combatPhase;
            string receipt = state.runId.ToString("x") + ":combat:" + fact.Sequence;
            if (dead)
            {
                foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                    if (!player.IsLocal && !coopOwner.coopDeparted.Contains(player.Id)) coopOwner.DeliverOrdered(player.Id, receipt, fact);
            }
            else
            {
                string id = BossRushCoop.GetPlayerId(info.fromCharacter);
                if (id != null && id != BossRushCoop.LocalPlayerId) coopOwner.DeliverOrdered(id, receipt, fact);
            }
        }
        internal static void PublishCoopPlayerDeath(Health health, DamageInfo info)
        {
            if (!BossRushCoop.IsHost || coopOwner == null || coopOwner.modeGRuntime == null || health == null) return;
            string player = BossRushCoop.GetPlayerId(health.GetComponent<CharacterMainControl>());
            if (player == null || player == BossRushCoop.LocalPlayerId) return;
            ModeGRunState state = coopOwner.modeGRuntime.State;
            var fact = new ModeGCoopCombat { Run = state.runId, Wave = state.waveEpoch, Source = BossRushCoop.GetEntityId(info.fromCharacter),
                Dead = true, Boss = 0, Sequence = ++coopOwner.coopCombatSequence };
            coopOwner.DeliverOrdered(player, state.runId.ToString("x") + ":death:" + fact.Sequence, fact);
        }
        private void DeliverOrdered(string player, string receipt, ModeGCoopCombat fact)
        {
            if (modeGRuntime == null || modeGRuntime.State == null || modeGRuntime.State.runId != fact.Run) return;
            string asset = modeGRuntime.GetCoopParticipantAsset(player);
            if (string.IsNullOrEmpty(asset)) return;
            RefreshCoopOrders(fact.Run);
            long order; coopRecipientOrders.TryGetValue(player, out order); fact.Order = order + 1;
            if (coopCombat.Deliver(player, receipt, fact, asset)) coopRecipientOrders[player] = fact.Order;
        }
        private bool ApplyCoopCombat(ModeGCoopCombat fact)
        {
            if (fact == null) return false;
            RefreshCoopReceiptRoom();
            if (modeGRuntime != null && modeGRuntime.State != null && modeGRuntime.State.runId == fact.Run
                && !modeGRuntime.State.IsTerminal)
                return modeGRuntime.ApplyCoopCombat(fact);
            // 原 owner 已走完 End，或本人未取得该局资格；迟到事实只确认，不写新局。
            string key = BossRushCoop.RunId + ":" + fact.Run.ToString("x") + ":" + BossRushCoop.LocalAssetId;
            if (coopClosedRuns.Contains(key)) return true;
            if (coopDeclinedRuns.Contains(key))
            {
                long order; coopUnqualifiedOrders.TryGetValue(key, out order);
                if (!fact.Admission && !fact.AdmissionDenied && fact.Order > order) coopUnqualifiedOrders[key] = fact.Order;
                return true;
            }
            if (modeGRuntime == null || modeGRuntime.State == null || modeGRuntime.State.runId != fact.Run) return false;
            return modeGRuntime.ApplyCoopCombat(fact);
        }
        internal static bool PendingCoopCombat
        { get { return coopOwner != null && coopOwner.coopCombat != null && coopOwner.modeGRuntime != null
            && coopOwner.modeGRuntime.State != null
            && coopOwner.coopCombat.HasPendingForIdPrefix(coopOwner.modeGRuntime.State.runId.ToString("x") + ":",
                player => !coopOwner.coopDeparted.Contains(player)); } }
        internal static void PublishCoopWorldNow() { if (coopOwner != null && coopOwner.coop != null) coopOwner.coop.Publish(); }
        internal static void FinishCoopRun(ModeGRuntimeModule run)
        {
            if (coopOwner == null || run == null || run.State == null) return;
            coopOwner.RefreshCoopReceiptRoom();
            if (run.State.IsTerminal && !string.IsNullOrEmpty(run.CoopRunRoom) && !string.IsNullOrEmpty(run.CoopRunAsset))
                coopOwner.coopClosedRuns.Add(run.CoopRunRoom + ":" + run.State.runId.ToString("x") + ":" + run.CoopRunAsset);
            if (!BossRushCoop.IsConnected) return;
            if (run.CoopRunRoom != BossRushCoop.RunId) return;
            if (BossRushCoop.IsHost) { coopOwner.coopFinalWorld = run.CaptureCoopWorld(); coopOwner.coop.Publish(); }
            else { coopOwner.coopDeclinedRun = run.State.runId; coopOwner.coop.Request("leave"); }
        }
        internal static bool HasLeft(string player) { return coopOwner != null && coopOwner.coopDeparted.Contains(player); }
        internal static bool CanApplyPersonalCombat
        { get { return coopOwner == null || coopOwner.modeGRuntime == null || (coopOwner.modeGRuntime.IsCoopAssetCurrent && !coopOwner.modeGRuntime.CoopPersonalDefeated
            && (!coopOwner.modeGRuntime.CoopRunWasClient || BossRushCoop.IsConnected)); } }
    }

    internal sealed partial class ModeGRuntimeModule
    {
        private ModeGCoopWorld coopWorld;
        private int coopGuestWave = -1;
        private bool coopWaveTelemetryReady;
        internal bool CoopPersonalDefeated { get; private set; }
        private readonly Dictionary<int, CharacterMainControl> coopBosses = new Dictionary<int, CharacterMainControl>();
        private readonly HashSet<long> coopCombatApplied = new HashSet<long>();
        private long coopAppliedOrder;
        private long coopAdmissionBaseline;
        private bool coopAdmissionReady;
        private long coopRequestedAdmissionBaseline;
        private int coopEntryWave;
        private bool coopEntryActorsKnown, coopRunWasClient;
        private readonly HashSet<int> coopEntryActors = new HashSet<int>();
        internal long CoopRequestedAdmissionBaseline { get { return coopRequestedAdmissionBaseline; } }
        internal bool CoopRunWasClient { get { return coopRunWasClient; } }
        internal bool CoopRunHasWorldAuthority { get { return !coopRunWasClient && BossRushCoop.HasWorldAuthority; } }
        internal bool CoopAdmissionReady { get { return coopAdmissionReady; } }
        private string coopRunAsset;
        private string coopRunRoom;
        internal string CoopRunAsset { get { return coopRunAsset; } }
        internal string CoopRunRoom { get { return coopRunRoom; } }
        private bool coopAssetBound;
        private readonly Dictionary<string, string> coopParticipantAssets = new Dictionary<string, string>(StringComparer.Ordinal);
        private void FreezeCoopAsset()
        {
            coopAssetBound = true; coopRunAsset = BossRushCoop.LocalAssetId;
            coopRunRoom = BossRushCoop.RunId;
            coopRunWasClient = BossRushCoop.IsClient;
            coopAdmissionReady = !coopRunWasClient;
            coopParticipantAssets.Clear();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (!string.IsNullOrEmpty(player.AssetId)) coopParticipantAssets[player.Id] = player.AssetId;
        }
        internal void FreezeCoopAdmission(ModeGCoopWorld world, long unqualifiedOrder)
        {
            if (!coopRunWasClient || coopAdmissionReady) return;
            coopRequestedAdmissionBaseline = Math.Max(0, unqualifiedOrder);
            coopEntryActors.Clear(); coopEntryActorsKnown = false;
            if (world == null || world.Id != _state.runId) return;
            coopEntryWave = world.Wave;
            if (world.Participants != null)
                foreach (ModeGCoopParticipant participant in world.Participants)
                    if (participant != null && participant.Id == BossRushCoop.LocalPlayerId && participant.Asset == coopRunAsset)
                        coopRequestedAdmissionBaseline = Math.Max(coopRequestedAdmissionBaseline, participant.Order);
            coopEntryActorsKnown = world.ActorsComplete && world.Expected > 0 && world.Resolved == world.Expected
                && world.Phase != (int)ModeGCombatPhase.Spawning && world.Actors != null;
            if (coopEntryActorsKnown) foreach (ModeGCoopActor actor in world.Actors) if (actor != null) coopEntryActors.Add(actor.Id);
        }
        internal string GetCoopParticipantAsset(string playerId)
        {
            string asset;
            if (playerId == null) return null;
            if (coopParticipantAssets.TryGetValue(playerId, out asset)) return asset;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            if (player == null || string.IsNullOrEmpty(player.AssetId)) return null;
            coopParticipantAssets.Add(playerId, player.AssetId);
            return player.AssetId;
        }
        internal bool CoopAnyParticipantAlive()
        {
            if (!BossRushCoop.IsConnected) return CharacterMainControl.Main != null && CharacterMainControl.Main.Health != null && !CharacterMainControl.Main.Health.IsDead;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                string asset;
                if (!coopParticipantAssets.TryGetValue(player.Id, out asset))
                { asset = player.AssetId; if (!string.IsNullOrEmpty(asset)) coopParticipantAssets[player.Id] = asset; }
                if (player.IsAlive && asset == player.AssetId && !ModeGEntryRuntime.HasLeft(player.Id)
                    && (!player.IsLocal || IsCoopAssetCurrent && !CoopPersonalDefeated)) return true;
            }
            return false;
        }
        internal bool IsCoopAssetCurrent { get { return !coopAssetBound || coopRunAsset == BossRushCoop.LocalAssetId; } }
        internal bool CheckCoopAsset()
        {
            if (coopAssetBound && !string.IsNullOrEmpty(coopRunRoom) && !string.IsNullOrEmpty(BossRushCoop.RunId)
                && coopRunRoom != BossRushCoop.RunId)
            { End(ModeGExitReason.SceneChanged); return false; }
            if (IsCoopAssetCurrent) return true;
            // 房主仍负责队友的共享 Boss 和波次；切槽只撤销自己的个人资格。
            // 新槽不能承接旧局档案、宿敌、Modifier 或入场退款。
            DisarmStartupRefund();
            if (BossRushCoop.IsHost)
            {
                if (!CoopPersonalDefeated)
                {
                    CoopPersonalDefeated = true;
                    if (_adaptive != null) _adaptive.RestoreAllModifiers();
                }
                return true;
            }
            End(ModeGExitReason.SceneChanged); return false;
        }
        internal ModeGCoopWorld CaptureCoopWorld()
        {
            _characterScratch.Clear(); _state.CollectTrackedBosses(_characterScratch);
            var actors = new List<ModeGCoopActor>();
            foreach (CharacterMainControl character in _characterScratch)
            {
                int id = BossRushCoop.GetAiId(character); if (id == 0) { BossRushCoop.RequestAiRegistration(character); id = BossRushCoop.GetAiId(character); }
                string key; if (id == 0 || !_spawnTransaction.TryGetCommittedKey(character, out key)) continue;
                int slot = -1;
                foreach (var entry in _spawnTransaction.Journal) if (entry.instanceId == character.GetInstanceID()) { slot = entry.slotIndex; break; }
                if (slot >= 0) actors.Add(new ModeGCoopActor { Id = id, Slot = slot, Key = key, MaxHealth = character.Health.MaxHealth });
            }
            return new ModeGCoopWorld { Id = _state.runId, Seed = _state.runSeed, Counter = _preview.sessionCounter,
                Created = _preview.createdTicks, Format = (int)_preview.runFormat, SceneName = _preview.sceneName,
                SceneId = _preview.sceneId, Verification = _preview.verificationRevision, Signatures = _preview.signatureRotation,
                Wave = _state.waveEpoch, Phase = (int)_state.combatPhase, Exit = (int)_state.exitReason,
                Active = _state.IsActive || _state.IsStarting || _state.IsRewarding, Actors = actors.ToArray(),
                ActorsComplete = actors.Count == _characterScratch.Count,
                Expected = _state.SlotExpected, Resolved = _state.SlotResolved, Committed = _state.SlotCommitted,
                LastStand = _state.lastStandActive, LastSeconds = _state.lastStandTimer,
                Intermission = _state.intermissionActive, RestSeconds = _state.intermissionTimer };
        }
        internal async UniTask<bool> WaitForCoopWave()
        {
            if (!BossRushCoop.IsConnected) return true;
            float deadline = Time.realtimeSinceStartup + 120f;
            float nextPublish = 0f;
            string key = _preview.sceneId + ":G:" + _state.runId.ToString("x") + ":wave:" + _state.waveEpoch;
            while (CanContinueRun() && BossRushCoop.IsConnected)
            {
                if (Time.unscaledTime >= nextPublish) { ModeGEntryRuntime.PublishCoopWorldNow(); nextPublish = Time.unscaledTime + 0.25f; }
                BossRushCoop.MarkSceneReady(key);
                if (BossRushCoop.SessionReady && BossRushCoop.AllPlayersSceneReady(key) && !ModeGEntryRuntime.PendingCoopCombat) return true;
                if (Time.realtimeSinceStartup >= deadline) return false;
                await UniTask.Yield();
            }
            return false;
        }
        private async UniTask<bool> WaitForCoopCombatReceipts()
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while (BossRushCoop.IsConnected && ModeGEntryRuntime.PendingCoopCombat && CanContinueRun())
            {
                if (Time.realtimeSinceStartup >= deadline) return false;
                await UniTask.Yield();
            }
            return CanContinueRun();
        }
        internal void ApplyCoopWorld(ModeGCoopWorld world) { coopWorld = world; }
        private void BeginCoopGuestWave(ModeGCoopWorld world)
        {
            if (coopGuestWave >= 0)
            {
                if (_spawnTransaction.ActiveBossCount != 0) return;
                if (_waveSettlementPending) { _waveSettlementPending = false; SettleCurrentWave(); }
                if (_state.IsTerminal || !_state.TryResetSlotsForNextWave()) return;
                _spawnTransaction.ResetForNextWave();
            }
            coopGuestWave = world.Wave; coopBosses.Clear();
            coopWaveTelemetryReady = false;
            _state.waveEpoch = world.Wave; _state.actIndex = world.Wave / 3; _state.combatPhase = ModeGCombatPhase.Spawning;
            _state.intermissionActive = false; _state.lastStandActive = false;
            _lastTerminalFamily = ModeGDirectDamageClass.NotScoreable; _activeDistanceVerdict = ModeGDistanceVerdict.None;
            _state.TrySetWaveSlotPlan(world.Expected);
            ModeGCounterAxis axis = ModeGAdaptiveCombat.GetAxisForWave(world.Wave);
            if (axis == ModeGCounterAxis.Distance)
            { _activeDistanceVerdict = _lastTerminalDistance; if (_activeDistanceVerdict != ModeGDistanceVerdict.None) { _distanceEchoCount++; _axisAttemptDistance++; } }
            else if (axis == ModeGCounterAxis.Ammo)
            {
                int ammo = _preparedAmmoBanWaveEpoch == world.Wave ? _preparedAmmoBanTypeId : ModeGAdaptiveCombat.SelectAmmoBan(_state.runSeed, world.Wave, _telemetry);
                _telemetry.ArmAmmoBan(ammo); if (ammo > 0) { _ammoBanCount++; _axisAttemptAmmo++; _ammoBanNemesisWaveCount++; }
            }
            else if (axis == ModeGCounterAxis.Attribute && _adaptive.ApplyAttributeLock(CharacterMainControl.Main, _telemetry))
            { _attributeLockCount++; _axisAttemptAttribute++; }
            _host.ShowModeGWaveBanner(world.Wave, _wavePlan.GetWave(world.Wave), axis, _runNemesisTemperament);
        }
        private void DriveCoopGuest()
        {
            if (coopWorld == null || _state.IsTerminal || coopWorld.Expected <= 0) return;
            if (!CheckCoopAsset()) return;
            if (!BossRushCoop.IsConnected) { End(ModeGExitReason.TechnicalIntegrityLoss); return; }
            CharacterMainControl player = CharacterMainControl.Main;
            if (!_state.IsActive) return;
            if (coopWorld.Wave != coopGuestWave) BeginCoopGuestWave(coopWorld);
            if (coopWorld.Wave != coopGuestWave || !_state.IsActive) return;
            foreach (ModeGCoopActor actor in coopWorld.Actors)
            {
                if (actor == null || coopBosses.ContainsKey(actor.Id)) continue;
                CharacterMainControl character = BossRushCoop.FindAi(actor.Id);
                if (character != null) CoopAiPredictionHealthProtection.MarkReplica(character);
                if (character != null && _spawnTransaction.TryCommit(actor.Slot, actor.Key, character)) coopBosses[actor.Id] = character;
            }
            if (_state.SlotResolved == coopWorld.Committed && coopWorld.Resolved == coopWorld.Expected)
                for (int slot = 0; slot < coopWorld.Expected; slot++)
                { bool exists = false; foreach (ModeGCoopActor actor in coopWorld.Actors) if (actor != null && actor.Slot == slot) exists = true; if (!exists) _spawnTransaction.MarkExhausted(slot); }
            if (_state.AreAllSlotsResolved && _state.combatPhase == ModeGCombatPhase.Spawning)
            {
                float max = 0f; foreach (ModeGCoopActor actor in coopWorld.Actors) if (actor != null) max += actor.MaxHealth;
                if (!coopWaveTelemetryReady) { _telemetry.BeginWave(player, max); coopWaveTelemetryReady = true; }
                BossRushCoop.MarkSceneReady(_preview.sceneId + ":G:" + _state.runId.ToString("x") + ":wave:" + coopGuestWave);
                if (coopWorld.Phase != (int)ModeGCombatPhase.Spawning)
                { _state.combatPhase = ModeGCombatPhase.Fighting; }
            }
            if (coopAdmissionReady && !_firstWaveCombatStarted && _state.combatPhase != ModeGCombatPhase.Spawning)
            { _firstWaveCombatStarted = true; DisarmStartupRefund(); }
            if (!coopAdmissionReady) return;
            if (_waveSettlementPending && _spawnTransaction.ActiveBossCount == 0)
            { _waveSettlementPending = false; SettleCurrentWave(); }
            _state.lastStandActive = coopWorld.LastStand; _state.lastStandTimer = coopWorld.LastSeconds;
            _state.intermissionTimer = coopWorld.RestSeconds;
            if (!coopWorld.Active && !_state.IsRewarding && !_state.IsTerminal && _spawnTransaction.ActiveBossCount == 0) End((ModeGExitReason)coopWorld.Exit);
            TickAmmoViolationAnnounce();
        }
        internal bool ApplyCoopCombat(ModeGCoopCombat fact)
        {
            if (!CheckCoopAsset()) return false;
            if (!IsCoopAssetCurrent || CoopPersonalDefeated) return false;
            if (fact == null || fact.Run != _state.runId) return false;
            if (fact.AdmissionDenied)
            {
                if (coopAdmissionReady) return false;
                End(ModeGExitReason.TechnicalIntegrityLoss); return _state.IsTerminal;
            }
            if (_state.IsTerminal || coopCombatApplied.Contains(fact.Sequence)) return true;
            if (fact.Admission)
            {
                if (fact.Order != coopRequestedAdmissionBaseline || fact.Sequence <= 0 || !coopRunWasClient) return false;
                if (coopAdmissionReady) return fact.Order == coopAdmissionBaseline;
                coopAdmissionBaseline = coopAppliedOrder = fact.Order;
                coopAdmissionReady = true; coopCombatApplied.Add(fact.Sequence); return true;
            }
            if (!coopAdmissionReady) return false;
            // 可靠准入事实明确划定未取得资格前的范围；后续事实仍逐条严格有序。
            if (fact.Order > 0 && fact.Order <= coopAdmissionBaseline) return true;
            if (fact.Order != coopAppliedOrder + 1) return false;
            if (fact.Wave < coopEntryWave || (fact.Wave == coopEntryWave && fact.Boss != 0 && coopEntryActorsKnown && !coopEntryActors.Contains(fact.Boss)))
            { coopAppliedOrder = fact.Order; coopCombatApplied.Add(fact.Sequence); return true; }
            if (fact != null && fact.Boss == 0 && fact.Dead)
            {
                if (_state.IsTerminal) return true;
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null) return false;
                ModeGDeathRouting.HandlePlayerDeath(this, player.Health, new DamageInfo(BossRushCoop.FindEntity(fact.Source)));
                if (_state.IsTerminal) { coopAppliedOrder = fact.Order; coopCombatApplied.Add(fact.Sequence); return true; }
                return false;
            }
            if (fact == null || fact.Wave != coopGuestWave || !_state.IsActive) return _state.IsTerminal;
            if (coopCombatApplied.Contains(fact.Sequence)) return true;
            CharacterMainControl boss; if (!coopBosses.TryGetValue(fact.Boss, out boss) || boss == null || boss.Health == null) return false;
            if (fact.Phase == (int)ModeGCombatPhase.Fighting || fact.Phase == (int)ModeGCombatPhase.LastStand)
            { _state.combatPhase = (ModeGCombatPhase)fact.Phase; _state.lastStandActive = fact.Phase == (int)ModeGCombatPhase.LastStand; }
            if (!_firstWaveCombatStarted) { _firstWaveCombatStarted = true; DisarmStartupRefund(); }
            CharacterMainControl source = BossRushCoop.FindEntity(fact.Source);
            var info = new DamageInfo(source) { fromWeaponItemID = fact.Weapon, damageType = (DamageTypes)fact.DamageType,
                crit = fact.Crit, damageValue = fact.Damage, finalDamage = fact.Final, isExplosion = fact.Explosion,
                isFromBuffOrEffect = fact.Effect, damagePoint = fact.Point };
            // 保留命中当时的真实距离，网络延迟不能把远程命中改算成近战距离。
            if (source == CharacterMainControl.Main) info.damagePoint = source.transform.position + fact.Point - fact.SourcePosition;
            _telemetry.AcceptCoopDamage(boss.Health, info);
            if (fact.Dead) HandleBossDead(boss.Health, info);
            coopCombatApplied.Add(fact.Sequence); coopAppliedOrder = fact.Order; return true;
        }
        internal void RecordCoopPersonalDefeat(CharacterMainControl player, DamageInfo info)
        {
            if (CoopPersonalDefeated) return; CoopPersonalDefeated = true;
            if (!IsCoopAssetCurrent) return;
            ModeGDeathRouting.AttributeCoopDefeat(this, _state, player, info);
            ModeGProfilePersistence.RecordRun(ModeGBattleResult.Defeat, _state.waveEpoch + 1, RunElapsedSeconds,
                TotalBossKills, false, "defeat_" + _state.runId.ToString("x"));
        }
    }
}

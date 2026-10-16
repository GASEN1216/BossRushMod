using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    // 只传比赛公开事实。个人 Season、钱包、押品树、名人堂不在房间快照里。
    [Serializable] internal sealed class ModeHCoopFighter
    {
        public string Key, Cn, En;
        public int Power;
        public bool Custom;
    }
    [Serializable] internal sealed class ModeHCoopWorld
    {
        public bool Active, BellConsumed;
        public string Run, Scene, Round;
        public long Seed;
        public int Match, Retry, Phase, Rerolls, BlueAlive, BlueTotal, RedAlive, RedTotal, BellEffect;
        public float Remaining, BellRemaining;
        public Vector3 Spectator;
        public ModeHCoopFighter[] Blue, Red;
        public string[] Requested, Funded, Exited, Skipped;
        public int[] BlueEntities, RedEntities;
    }
    [Serializable] internal sealed class ModeHCoopResult
    {
        public string Run, Token;
        public int Match, Winner;
        public long Seed;
        public bool Started, Final, Skipped;
    }

    internal sealed partial class ModeHRuntimeModule
    {
        private CoopGameplayChannel<ModeHCoopWorld> _coopChannel;
        private CoopPersonalDelivery<ModeHCoopResult> _coopResults;
        private readonly HashSet<string> _coopRequested = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _coopFunded = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _coopForfeited = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _coopExited = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _coopRequestedAssets = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _coopFundedAssets = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _coopSkipped = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _coopSkipQueued = new HashSet<string>(StringComparer.Ordinal);
        private ModeHCoopWorld _coopView;
        private bool _coopProjection, _coopStarting, _coopSubscribed;
        private string _coopProjectionRoom;
        private string _coopRound, _coopPrepared, _coopUiStamp, _coopStartedRound, _coopIgnoredRun;
        private string _coopParticipantRun;
        private string _coopBellRequester;
        private float _coopPrepareTimer;
        private int _coopCamera;
        private string _coopControlLease;
        private ModeHCoopLedger _coopLedger;
        private sealed class CoopSpectatorState
        {
            internal CharacterMainControl Character;
            internal Teams Team;
            internal bool Invincible;
        }
        private readonly Dictionary<string, CoopSpectatorState> _coopSpectators = new Dictionary<string, CoopSpectatorState>(StringComparer.Ordinal);
        private float _coopSpectatorTimer;
        private int _coopSlotGeneration = -1;
        private string _coopWorldAsset, _coopWorldRun;
        private string _coopWorldRoom;
        private bool _coopWorldWasConnected;
        private string _coopProjectionToken;
        private bool IsCoopProjectionSceneCurrent
        { get { return BossRushCoop.HasBoundScene && (!_coopProjection || !string.IsNullOrEmpty(_coopProjectionToken) && _coopProjectionToken == BossRushCoop.SceneToken); } }

        private void BindCoopWorldAsset()
        {
            _coopWorldAsset = BossRushCoop.LocalAssetId;
            _coopWorldRun = _runState != null ? _runState.RunId : null;
            _coopWorldRoom = BossRushCoop.RunId;
            _coopWorldWasConnected = BossRushCoop.IsConnected;
        }

        private bool IsCoopWorldAuthorityCurrent
        {
            get
            {
                if (_coopProjection) return false;
                if (_runState == null || _coopWorldRun != _runState.RunId) return true;
                if (!_coopWorldWasConnected) return !BossRushCoop.IsConnected;
                return BossRushCoop.IsHost && (string.IsNullOrEmpty(BossRushCoop.RunId)
                    || string.IsNullOrEmpty(_coopWorldRoom) || _coopWorldRoom == BossRushCoop.RunId);
            }
        }

        private bool CheckCoopWorldAuthority()
        {
            if (_coopProjection || (_restoredSeasonPending && !_resumeScenePending) || !HasActiveRun
                || IsCoopWorldAuthorityCurrent) return true;
            // 仅关闭原世界；原 Season、押注和已产生的个人收据保留原恢复/交付管线。
            RequestExit(ModeHExitReason.TechnicalAbort, "coop_world_owner_changed");
            return false;
        }

        private bool IsCoopWorldPersonalAssetCurrent
        { get { return _runState == null || _coopWorldRun != _runState.RunId || _coopWorldAsset == BossRushCoop.LocalAssetId; } }
        private bool CanUseCoopNativePersonalSeason
        { get { return IsCoopWorldPersonalAssetCurrent && (!BossRushCoop.IsConnected || !_coopExited.Contains(BossRushCoop.LocalPlayerId)); } }

        private bool RetainCoopWorldOnSlotChange()
        {
            if (!BossRushCoop.IsHost || !HasActiveRun || !GroupModeEnabled || _coopProjection) return false;
            // 群战仍由房主推进；原槽的押注收据保留原资产，换槽身体退出个人参赛。
            _coopExited.Add(BossRushCoop.LocalPlayerId);
            _coopRequested.Remove(BossRushCoop.LocalPlayerId);
            if (_coopFunded.Contains(BossRushCoop.LocalPlayerId)) _coopForfeited.Add(BossRushCoop.LocalPlayerId);
            if (_spectatorLease != null) _spectatorLease.Release(_sceneGeneration);
            _spectatorLease = null;
            ModeHRuntimeGates.SetRunOwnerActive(true);
            EnsureContentScanned();
            return true;
        }

        private void EnsureCoopHostSpectator()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (_spectatorLease != null || _map == null || _runState == null || player == null || player.Health == null || player.Health.IsDead) return;
            var lease = new ModeHSpectatorLease();
            string failure;
            if (lease.TryAcquire(_map.SpectatorPos, _sceneGeneration, _runState.OwnerToken, out failure)) _spectatorLease = lease;
        }

        private void InitializeCoop()
        {
            if (_coopChannel != null) return;
            _coopLedger = new ModeHCoopLedger();
            _coopChannel = new CoopGameplayChannel<ModeHCoopWorld>("H", CaptureCoopWorld, ApplyCoopWorld, ExecuteCoopCommand);
            _coopResults = new CoopPersonalDelivery<ModeHCoopResult>("H", ApplyCoopResult);
            if (!_coopSubscribed)
            {
                BossRushCoop.ControlLeaseChanged += OnCoopControlLease;
                _coopSubscribed = true;
            }
        }

        private void ShutdownCoop()
        {
            RestoreCoopSpectators();
            if (_coopSubscribed) BossRushCoop.ControlLeaseChanged -= OnCoopControlLease;
            _coopSubscribed = false;
            ReleaseCoopProjection();
            if (_coopChannel != null) _coopChannel.Dispose();
            if (_coopResults != null) _coopResults.Dispose();
            if (_coopLedger != null) _coopLedger.Dispose();
            _coopChannel = null; _coopResults = null; _coopLedger = null;
            _coopRequested.Clear(); _coopFunded.Clear(); _coopForfeited.Clear();
            _coopRequestedAssets.Clear(); _coopFundedAssets.Clear();
            _coopExited.Clear(); _coopParticipantRun = null;
        }

        private string CurrentCoopRound()
        {
            if (_runState == null || _groupRoster == null) return null;
            return _runState.RunId + ":" + GroupMatchIndex + ":" + _runState.TechnicalRetrySequence + ":" + _groupRoster.RerollsUsed;
        }

        private void RefreshCoopRound()
        {
            string round = CurrentCoopRound();
            string run = _runState != null ? _runState.RunId : null;
            if (run != _coopParticipantRun) { _coopParticipantRun = run; _coopExited.Clear(); }
            if (round == _coopRound) { RefreshCoopParticipantAssets(); return; }
            _coopRound = round;
            _coopRequested.Clear(); _coopFunded.Clear(); _coopForfeited.Clear();
            _coopRequestedAssets.Clear(); _coopFundedAssets.Clear();
            _coopStartedRound = null;
            _coopSkipped.Clear(); _coopSkipQueued.Clear();
        }

        private void TickCoop(float deltaTime)
        {
            if (_ui != null) _ui.ApplyHudVisibility();
            if (_coopChannel == null) return;
            if (_coopProjection && (!BossRushCoop.IsClient || _coopProjectionRoom != BossRushCoop.RunId)) ReleaseCoopProjection();
            RefreshCoopSlot();
            _coopLedger.Tick();
            _coopResults.Tick(deltaTime);
            if (!CheckCoopWorldAuthority()) return;
            if (!BossRushCoop.IsConnected)
            {
                ReleaseCoopProjection();
                RestoreCoopSpectators();
                return;
            }
            if (!IsCoopProjectionSceneCurrent) { _coopChannel.Tick(deltaTime); return; }
            if (BossRushCoop.IsHost && HasActiveRun && GroupModeEnabled)
            {
                EnsureCoopHostSpectator();
                ProtectCoopSpectators(deltaTime);
                RefreshCoopRound();
                BossRushCoop.MarkSceneReady("H:" + _runState.RunId);
                if (_runState.Lifecycle == ModeHLifecycle.MatchFighting && _coopStartedRound != _coopRound)
                {
                    _coopStartedRound = _coopRound;
                    foreach (string player in _coopFunded)
                        _coopResults.Deliver(player, "h-start:" + _coopRound, new ModeHCoopResult
                        { Run = _runState.RunId, Match = GroupMatchIndex, Seed = _runState.RunSeed, Started = true }, FundedAsset(player));
                }
                QueueCoopSkippedRefunds();
                if (IsGroupRosterPhase() && _coopRequested.Contains(BossRushCoop.LocalPlayerId))
                    TryPrepareCoopBet(deltaTime, _coopRound);
                if (IsGroupRosterPhase() && _coopFunded.Count > 0 && !_coopStarting
                    && BossRushCoop.SessionReady && CoopParticipantsSceneReady()
                    && !HasPendingCoopParticipantResults())
                {
                    bool ready = true;
                    foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                        if (player.IsConnected && !_coopExited.Contains(player.Id) && !IsCoopSkipped(player.Id, player.AssetId) && !IsCoopFunded(player.Id)) { ready = false; break; }
                    if (ready)
                    {
                        _coopStarting = true;
                        try { StartGroupMatch(); }
                        finally { _coopStarting = false; }
                    }
                }
            }
            else if (BossRushCoop.IsHost) RestoreCoopSpectators();
            if (BossRushCoop.IsClient && _coopProjection && _coopView != null)
            {
                BossRushCoop.MarkSceneReady("H:" + _coopView.Run);
                if (Contains(_coopView.Requested, BossRushCoop.LocalPlayerId)) TryPrepareCoopBet(deltaTime, _coopView.Round);
                TickCoopSpectator(deltaTime);
            }
            _coopChannel.Tick(deltaTime);
        }

        private ModeHCoopWorld CaptureCoopWorld()
        {
            bool active = GroupModeEnabled && HasActiveRun && _map != null && !_shutdownCompleted
                && IsCoopWorldAuthorityCurrent;
            if (!active) return new ModeHCoopWorld();
            if (IsGroupRosterPhase()) EnsureGroupRoster();
            RefreshCoopRound();
            ModeHCoopWorld state = new ModeHCoopWorld
            {
                Active = true, Run = _runState.RunId, Scene = _runState.SceneName, Seed = _runState.RunSeed,
                Match = GroupMatchIndex, Retry = _runState.TechnicalRetrySequence, Phase = (int)_runState.Lifecycle,
                Round = _coopRound, Rerolls = _groupRoster != null ? _groupRoster.RerollsUsed : 0,
                Spectator = _map.SpectatorPos, Blue = CaptureCoopTeam(_groupRoster != null ? _groupRoster.Allies : null),
                Red = CaptureCoopTeam(_groupRoster != null ? _groupRoster.Enemies : null),
                Requested = CurrentAssetParticipants(_coopRequested, _coopRequestedAssets), Funded = CurrentAssetParticipants(_coopFunded, _coopFundedAssets),
                Exited = new List<string>(_coopExited).ToArray(), Skipped = CaptureCoopSkipped()
            };
            if (_groupBattle != null)
            {
                state.BlueAlive = _groupBattle.AllyAlive; state.BlueTotal = _groupBattle.AllyTotal;
                state.RedAlive = _groupBattle.EnemyAlive; state.RedTotal = _groupBattle.EnemyTotal;
                state.Remaining = _groupBattle.RemainingSeconds; state.BellConsumed = _groupBattle.BellConsumed;
                state.BellEffect = (int)_groupBattle.BellEffect; state.BellRemaining = _groupBattle.BellRemaining;
                List<int> blue = new List<int>(), red = new List<int>();
                foreach (ModeHGroupUnit unit in _groupBattle.Units)
                {
                    if (unit == null || unit.Character == null) continue;
                    int id = BossRushCoop.GetAiId(unit.Character);
                    if (id == 0) { BossRushCoop.RequestAiRegistration(unit.Character); id = BossRushCoop.GetAiId(unit.Character); }
                    if (id != 0) (unit.IsEnemy ? red : blue).Add(id);
                }
                state.BlueEntities = blue.ToArray(); state.RedEntities = red.ToArray();
            }
            return state;
        }

        private bool CoopParticipantsSceneReady()
        {
            List<string> participants = new List<string>();
            bool funded = false;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsConnected || _coopExited.Contains(player.Id) || IsCoopSkipped(player.Id, player.AssetId)) continue;
                participants.Add(player.Id);
                if (IsCoopFunded(player.Id)) funded = true;
            }
            return funded && BossRushCoop.ArePlayersSceneReady("H:" + _runState.RunId, participants);
        }

        private bool HasPendingCoopParticipantResults()
        {
            List<string> participants = new List<string>();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsConnected && !_coopExited.Contains(player.Id) && !IsCoopSkipped(player.Id, player.AssetId)) participants.Add(player.Id);
            return _coopResults.HasPendingForPlayers(participants);
        }

        private string FundedAsset(string playerId)
        { string asset; return _coopFundedAssets.TryGetValue(playerId, out asset) ? asset : string.Empty; }
        private bool IsCoopFunded(string playerId)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            return player != null && _coopFunded.Contains(playerId) && FundedAsset(playerId) == player.AssetId;
        }
        private static string[] CurrentAssetParticipants(HashSet<string> members, Dictionary<string, string> assets)
        {
            List<string> result = new List<string>();
            foreach (string id in members)
            {
                string asset; BossRushCoopPlayer player = BossRushCoop.FindPlayer(id);
                if (player != null && assets.TryGetValue(id, out asset) && player.AssetId == asset) result.Add(id);
            }
            return result.ToArray();
        }
        private void RefreshCoopParticipantAssets()
        {
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                string asset;
                if (player.Character != null && player.Character.Health != null && player.Character.Health.IsDead)
                {
                    _coopExited.Add(player.Id); _coopRequested.Remove(player.Id);
                    if (_coopFunded.Contains(player.Id)) _coopForfeited.Add(player.Id);
                    continue;
                }
                bool changed = _coopRequestedAssets.TryGetValue(player.Id, out asset) && asset != player.AssetId;
                if (_coopFundedAssets.TryGetValue(player.Id, out asset) && asset != player.AssetId) changed = true;
                if (!changed) continue;
                // 已付款的资产仍保留至本场结算；换槽后的身体退出参赛，不能继承旧槽准备状态。
                _coopExited.Add(player.Id); _coopRequested.Remove(player.Id);
                if (_coopFunded.Contains(player.Id)) _coopForfeited.Add(player.Id);
            }
        }

        private void RefreshCoopSlot()
        {
            int slotGeneration = ModeHRuntimeGates.SlotGeneration;
            if (_coopSlotGeneration == slotGeneration) return;
            bool changed = _coopSlotGeneration >= 0;
            _coopSlotGeneration = slotGeneration;
            _coopPrepared = null;
            if (!changed || !_coopProjection || _coopView == null) return;
            // 换槽后不能继续沿用旧槽的 funded，也不能向新槽写房间投影。
            _coopChannel.Request("leave", text: _coopView.Round);
            _coopIgnoredRun = _coopView.Run;
            ReleaseCoopProjection();
        }

        private static ModeHCoopFighter[] CaptureCoopTeam(List<ModeHGroupEntry> team)
        {
            List<ModeHCoopFighter> result = new List<ModeHCoopFighter>();
            if (team != null) foreach (ModeHGroupEntry entry in team)
                result.Add(new ModeHCoopFighter { Key = entry.Key, Cn = entry.DisplayNameCn, En = entry.DisplayNameEn, Power = entry.Power, Custom = entry.IsCustom });
            return result.ToArray();
        }

        private static void ApplyCoopTeam(List<ModeHGroupEntry> team, ModeHCoopFighter[] source)
        {
            team.Clear();
            if (source == null) return;
            foreach (ModeHCoopFighter entry in source)
                if (entry != null) team.Add(new ModeHGroupEntry { Key = entry.Key, DisplayNameCn = entry.Cn, DisplayNameEn = entry.En, Power = entry.Power, IsCustom = entry.Custom });
        }

        private void ApplyCoopWorld(ModeHCoopWorld state)
        {
            if (!BossRushCoop.IsClient || !BossRushCoop.HasBoundScene || state == null) return;
            if (_coopProjection && _coopProjectionRoom != BossRushCoop.RunId) ReleaseCoopProjection();
            if (!state.Active) { ReleaseCoopProjection(); return; }
            if (state.Run == _coopIgnoredRun || string.IsNullOrEmpty(state.Run) || !IsLevelAfterInit()
                || CharacterMainControl.Main == null || !ModeHMapSupportRegistry.TryGetMap(state.Scene, out _map)) return;
            // 原生场景投票的 CompletedTask 不代表已抵达；必须等实际角色所在场景。
            if (!UnityEngine.SceneManagement.SceneManager.GetSceneByName(state.Scene).isLoaded) return;
            if (!_coopProjection || _coopView == null || _coopView.Run != state.Run)
            {
                ReleaseCoopProjection();
                ModeHMapSupportRegistry.TryGetMap(state.Scene, out _map);
                _coopProjection = true;
                _coopProjectionRoom = BossRushCoop.RunId;
                _shutdownCompleted = false; _commandsClosed = false; _restoredSeasonPending = false;
                _runState = new ModeHRunState(state.Run, state.Seed, state.Scene, _sceneGeneration);
                _season = new ModeHSeasonDto();
                _spectatorLease = new ModeHSpectatorLease();
                string failure;
                if (!_spectatorLease.TryAcquire(state.Spectator, _sceneGeneration, _runState.OwnerToken, out failure))
                { ReleaseCoopProjection(); return; }
                ModeHRuntimeGates.SetRunOwnerActive(true);
                ModeHGroupTeamTags.SetActive(true);
                _coopPrepared = null;
            }
            _coopView = state;
            _coopProjectionToken = BossRushCoop.SceneToken;
            _runState.RestoreMatchIndex(state.Match, state.Retry, 0);
            _runState.RestoreLifecycle((ModeHLifecycle)state.Phase, ModeHLifecycle.None, ModeHLifecycle.None);
            ModeHRuntimeGates.SetCombatFrameActive((ModeHLifecycle)state.Phase == ModeHLifecycle.MatchFighting);
            if (_groupRoster == null) _groupRoster = new ModeHGroupRoster();
            _groupRoster.RunId = state.Run; _groupRoster.MatchIndex = state.Match; _groupRoster.RerollsUsed = state.Rerolls;
            ApplyCoopTeam(_groupRoster.Allies, state.Blue); ApplyCoopTeam(_groupRoster.Enemies, state.Red);
            foreach (int ai in state.BlueEntities ?? new int[0]) CoopAiPredictionHealthProtection.MarkReplica(BossRushCoop.FindAi(ai));
            foreach (int ai in state.RedEntities ?? new int[0]) CoopAiPredictionHealthProtection.MarkReplica(BossRushCoop.FindAi(ai));
            string stamp = state.Round + ":" + state.Phase + ":" + string.Join(",", state.Requested ?? new string[0])
                + ":" + string.Join(",", state.Funded ?? new string[0]);
            if (stamp != _coopUiStamp)
            {
                _coopUiStamp = stamp;
                RouteUiForLifecycle(_runState.Lifecycle);
            }
        }

        private void ReleaseCoopProjection()
        {
            if (!_coopProjection) return;
            if (!string.IsNullOrEmpty(_coopControlLease)) BossRushCoop.EndControlLease(_coopControlLease);
            _coopControlLease = null;
            if (_spectatorLease != null) _spectatorLease.Release(_sceneGeneration);
            _spectatorLease = null;
            DestroyUi();
            _coopProjection = false; _coopProjectionRoom = null; _coopProjectionToken = null; _coopView = null; _coopUiStamp = null; _coopPrepared = null;
            _groupRoster = null; _map = null; _season = null; _runState = null;
            ModeHRuntimeGates.SetCombatFrameActive(false);
            ModeHGroupTeamTags.SetActive(false);
            ModeHRuntimeGates.SetRunOwnerActive(false);
            RestoreFromSaveIfPresent();
        }

        private bool IsCoopSkipped(string id, string asset)
        { return _coopSkipped.ContainsKey(id); }

        private string[] CaptureCoopSkipped()
        {
            List<string> result = new List<string>();
            foreach (var skipped in _coopSkipped) result.Add(skipped.Key);
            return result.ToArray();
        }

        private void StartCoopWithConfirmedPlayers()
        {
            if (!BossRushCoop.IsHost || !HasActiveRun || !IsCoopWorldAuthorityCurrent || !IsCoopProjectionSceneCurrent || !IsGroupRosterPhase()
                || !IsCoopFunded(BossRushCoop.LocalPlayerId)) return;
            RefreshCoopRound();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (IsCoopFunded(player.Id)) continue;
                string asset;
                if (!_coopRequestedAssets.TryGetValue(player.Id, out asset)) asset = player.AssetId;
                if (!string.IsNullOrEmpty(asset)) _coopSkipped[player.Id] = asset;
            }
            QueueCoopSkippedRefunds();
            _coopChannel.Publish();
        }

        private void QueueCoopSkippedRefunds()
        {
            foreach (var skipped in _coopSkipped)
                if (!_coopSkipQueued.Contains(skipped.Key) && _coopResults.Deliver(skipped.Key, "h-skip:" + _coopRound,
                    new ModeHCoopResult { Run = _runState.RunId, Match = GroupMatchIndex, Seed = _runState.RunSeed, Skipped = true }, skipped.Value))
                    _coopSkipQueued.Add(skipped.Key);
        }

        private bool RequestCoopStart()
        {
            if (!BossRushCoop.IsConnected || _coopStarting) return false;
            if (!IsCoopProjectionSceneCurrent) return true;
            if (BossRushCoop.IsHost && _coopExited.Contains(BossRushCoop.LocalPlayerId)) return true;
            string round = BossRushCoop.IsClient && _coopView != null ? _coopView.Round : CurrentCoopRound();
            if (string.IsNullOrEmpty(round)) return true;
            if (BossRushCoop.IsClient) _coopChannel.Request("ready", text: round);
            else ExecuteCoopCommand(new CoopGameplayCommand { Action = "ready", Text = round, PlayerId = BossRushCoop.LocalPlayerId, AssetId = BossRushCoop.LocalAssetId });
            return true;
        }

        private bool CoopRosterLocked
        {
            get { return BossRushCoop.IsHost ? _coopRequested.Count > 0 : _coopView != null && _coopView.Requested != null && _coopView.Requested.Length > 0; }
        }
        private bool CoopLocalReady
        {
            get { return BossRushCoop.IsHost ? _coopRequested.Contains(BossRushCoop.LocalPlayerId)
                : _coopView != null && Contains(_coopView.Requested, BossRushCoop.LocalPlayerId); }
        }

        private bool RequestCoopReroll()
        {
            if (!BossRushCoop.IsConnected) return false;
            if (CoopRosterLocked) return true;
            return BossRushCoop.IsClient && _coopChannel.Request("reroll", text: _coopView != null ? _coopView.Round : null);
        }

        private void ExecuteCoopCommand(CoopGameplayCommand command)
        {
            if (!BossRushCoop.IsHost || !HasActiveRun || _commandsClosed || command == null
                || !IsCoopWorldAuthorityCurrent) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(command.AssetId) || command.AssetId != player.AssetId) return;
            RefreshCoopRound();
            if (command.Text != _coopRound) return;
            if (command.Action == "leave")
            {
                _coopExited.Add(command.PlayerId);
                if (_coopFunded.Contains(command.PlayerId)) _coopForfeited.Add(command.PlayerId);
            }
            else if (_coopExited.Contains(command.PlayerId) || IsCoopSkipped(command.PlayerId, command.AssetId)) return;
            else if (command.Action == "ready" && IsGroupRosterPhase())
            { _coopRequested.Add(command.PlayerId); _coopRequestedAssets[command.PlayerId] = command.AssetId; }
            else if (command.Action == "funded" && IsGroupRosterPhase() && _coopRequested.Contains(command.PlayerId))
            { _coopFunded.Add(command.PlayerId); _coopFundedAssets[command.PlayerId] = command.AssetId; }
            else if (command.Action == "reroll" && IsGroupRosterPhase() && !CoopRosterLocked) RerollGroupRoster();
            else if (command.Action == "bell" && _runState.Lifecycle == ModeHLifecycle.MatchFighting && IsCoopFunded(command.PlayerId))
            {
                if (_combatControl != null) _combatControl.SetCoopOperator(command.PlayerId);
                _coopBellRequester = command.PlayerId;
                try { OnBellPressed(); }
                finally { _coopBellRequester = null; }
            }
            else if (command.Action == "forfeit" && _coopFunded.Contains(command.PlayerId)) _coopForfeited.Add(command.PlayerId);
            _coopChannel.Publish();
            if (IsGroupRosterPhase()) RouteUiForLifecycle(_runState.Lifecycle);
        }

        private void TryPrepareCoopBet(float deltaTime, string round)
        {
            if (BossRushCoop.IsConnected && !IsCoopProjectionSceneCurrent) return;
            if (string.IsNullOrEmpty(round) || _runState == null || !IsGroupRosterPhase()) return;
            if (BossRushCoop.IsHost ? IsCoopSkipped(BossRushCoop.LocalPlayerId, BossRushCoop.LocalAssetId)
                : _coopView != null && Contains(_coopView.Skipped, BossRushCoop.LocalPlayerId)) return;
            if (_coopPrepared == round && !_coopLedger.Owns(_runState.RunId, GroupMatchIndex)) _coopPrepared = null;
            _coopPrepareTimer -= Mathf.Max(deltaTime, 0f);
            if (_coopPrepareTimer > 0f) return;
            _coopPrepareTimer = 1f;
            if (_coopPrepared != round)
            {
                _groupLockedBetOnRed = _coopLedger.ResolveSide(_runState.RunId, GroupMatchIndex, _groupBetOnRed);
                _groupBetOnRed = _groupLockedBetOnRed;
                if (!_coopLedger.Prepare(_runState.RunId, GroupMatchIndex, _runState.RunSeed, _groupLockedBetOnRed,
                    ResolveGroupOdds(_groupLockedBetOnRed))) return;
                ModeHCashBetRecord old = ModeHCashBetService.Current;
                if (old != null && old.status == ModeHCashBetService.StatusReserved &&
                    (old.runId != _runState.RunId || old.matchIndex != GroupMatchIndex))
                {
                    // 上一场赢注的个人收据可能晚于下一场世界快照；不能误判为弃赛。
                    if (old.runId == _runState.RunId && _coopLedger.Owns(old.runId, old.matchIndex)
                        && !_coopLedger.IsResolved(old.runId, old.matchIndex)) return;
                    if (old.combatStarted != 0) ForfeitStartedCashBet(old, "coop_next_round");
                    else RefundCashBet("coop_next_round");
                    old = ModeHCashBetService.Current;
                    if (old != null && old.status == ModeHCashBetService.StatusReserved) return;
                }
                if (CarriedGroupBet() == null)
                {
                    // 第一场 Drafting 的生产 MatchIndex 尚为 0；个人账本始终使用页面场次 1。
                    int previous = _runState.MatchIndex;
                    _runState.RestoreMatchIndex(GroupMatchIndex, _runState.TechnicalRetrySequence, _runState.StateSequence);
                    try
                    {
                        if (ModeHItemBetStake.HasSelection) ReserveItemBet(ResolveGroupOdds(_groupLockedBetOnRed));
                        else if (ModeHCashBetService.StandingAmount > 0)
                        {
                            string failure;
                            if (!ModeHCashBetService.TryReserve(_runState.RunId, GroupMatchIndex, ResolveGroupOdds(_groupLockedBetOnRed),
                                ModeHCashBetService.StandingAmount, out failure, LockedGroupBetSide())) return;
                        }
                    }
                    finally { _runState.RestoreMatchIndex(previous, _runState.TechnicalRetrySequence, _runState.StateSequence); }
                    if (ModeHItemBetStake.HasSelection && CarriedGroupBet() == null) return;
                }
                if (!_coopLedger.AlignReservedBet(_runState.RunId, GroupMatchIndex, CarriedGroupBet())) return;
                _groupLockedBetOnRed = _coopLedger.ResolveSide(_runState.RunId, GroupMatchIndex, _groupLockedBetOnRed);
                _groupBetOnRed = _groupLockedBetOnRed;
                _coopPrepared = round;
                _showItemBetPicker = false;
            }
            if (BossRushCoop.IsClient)
            {
                if (_coopView == null || _coopView.Round != round || !Contains(_coopView.Funded, BossRushCoop.LocalPlayerId))
                    _coopChannel.RequestRepeat("funded", text: round);
            }
            else { _coopFunded.Add(BossRushCoop.LocalPlayerId); _coopFundedAssets[BossRushCoop.LocalPlayerId] = BossRushCoop.LocalAssetId; }
        }

        private void PublishCoopResult(ModeHGroupBattle battle)
        {
            if (!BossRushCoop.IsHost || battle == null || _runState == null || !IsCoopWorldAuthorityCurrent) return;
            foreach (string player in _coopFunded)
                _coopResults.Deliver(player, "h-result:" + _coopRound, new ModeHCoopResult
                {
                    Run = _runState.RunId, Match = GroupMatchIndex, Seed = _runState.RunSeed,
                    Winner = battle.Surrendered || _coopForfeited.Contains(player) ? 0 : battle.Winner,
                    Token = "coop-result:" + _coopRound, Final = GroupMatchIndex >= ModeHConfig.SeasonMatchCount
                }, FundedAsset(player));
        }

        private bool ApplyCoopResult(ModeHCoopResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.Run)) return false;
            if (result.Skipped) return _coopLedger.CancelUnstarted(result.Run, result.Match, result.Seed);
            if (result.Started)
            {
                ModeHCashBetRecord record = ModeHCashBetService.ReservedFor(result.Run, result.Match);
                return record == null || ModeHCashBetService.TryMarkCombatStarted(result.Run, result.Match);
            }
            bool applied = _coopLedger.Apply(result, BossRushCoop.IsHost && CanUseCoopNativePersonalSeason);
            if (applied && _coopProjection && _runState != null) RouteUiForLifecycle(_runState.Lifecycle);
            return applied;
        }

        private bool RouteCoopGuestPage(ModeHLifecycle lifecycle)
        {
            if (!_coopProjection || _coopView == null) return false;
            if (_showItemBetPicker && IsGroupRosterPhase())
            { _ui.OpenPage(ModeHPage.ItemBet, lifecycle, _runState.RunId, BuildItemBetPickerPage()); return true; }
            if (IsGroupRosterPhase())
            { _ui.OpenPage(ModeHPage.Brief, lifecycle, _runState.RunId, BuildGroupPageContent()); return true; }
            if (lifecycle == ModeHLifecycle.MatchFighting || lifecycle == ModeHLifecycle.RelayPending)
            {
                _ui.ClosePage();
                _ui.EnsureHud(RequestCoopBell, RequestCoopForfeit, ExitCoopSpectator);
                return true;
            }
            _ui.DestroyHud();
            ModeHPageContent page = new ModeHPageContent
            {
                Title = L10n.T("鸭王杯 · 房间比赛", "Duck Cup · Room match"),
                Body = _coopLedger.Describe(_runState.RunId, GroupMatchIndex)
            };
            if (lifecycle == ModeHLifecycle.HallOfFame)
            {
                page = BuildGroupHallOfFamePageContent();
                page.Actions.Clear();
                page.Actions.Add(new ModeHActionData { Label = L10n.T("关闭", "Close"), OnClick = _ui.ClosePage });
            }
            else page.Lines.Add(L10n.T("等待房主推进比赛；下一场仍需各自确认押注。", "Waiting for the host. Everyone confirms their own bet for the next match."));
            _ui.OpenPage(ModeHPage.Brief, lifecycle, _runState.RunId, page);
            return true;
        }

        private void DecorateCoopBetPage(ModeHPageContent page)
        {
            if (!BossRushCoop.IsConnected || page == null) return;
            if (CoopLocalReady) page.OptionRows.Clear();
            int ready = 0;
            int total = 0;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsConnected || (BossRushCoop.IsHost ? _coopExited.Contains(player.Id) : _coopView != null && Contains(_coopView.Exited, player.Id))) continue;
                total++;
                if (BossRushCoop.IsHost ? IsCoopFunded(player.Id) : _coopView != null && Contains(_coopView.Funded, player.Id)) ready++;
            }
            page.Lines.Add(L10n.T("已准备 " + ready + " / " + total + "；房主可开始并跳过未确认者。", "Ready " + ready + " / " + total + "; the host can start and skip unconfirmed players."));
            if (page.Actions.Count > 0) page.Actions[page.Actions.Count - 1].Label = L10n.T("确认押注并准备", "Confirm bet and ready");
            if (BossRushCoop.IsHost && IsCoopFunded(BossRushCoop.LocalPlayerId))
                page.Actions.Add(new ModeHActionData { Label = L10n.T("开始并跳过未确认", "Start and skip unconfirmed"), OnClick = StartCoopWithConfirmedPlayers });
        }

        private void RequestCoopBell()
        {
            if (_coopView != null) _coopChannel.Request("bell", text: _coopView.Round);
        }
        private void PrepareCoopBellOperator()
        {
            if (BossRushCoop.IsConnected && _combatControl != null)
                _combatControl.SetCoopOperator(_coopBellRequester ?? BossRushCoop.LocalPlayerId);
        }
        private void RequestCoopForfeit()
        {
            if (_runState == null) return;
            string run = _runState.RunId;
            int match = GroupMatchIndex;
            string round = _coopView != null ? _coopView.Round : CurrentCoopRound();
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("放弃本场押注？", "Forfeit your bet?"),
                Body = L10n.T("你的本场押注按输结清，房间比赛继续。", "Your bet is settled as lost. The room's match continues."),
                ConfirmLabel = L10n.T("放弃", "Forfeit"), Danger = true,
                OnConfirm = delegate
                {
                    TryForfeitCoopRound(run, match, round, false);
                }
            });
        }
        private void ExitCoopSpectator()
        {
            if (!BossRushCoop.IsConnected || _runState == null) return;
            string run = _runState.RunId;
            int match = GroupMatchIndex;
            string round = _coopView != null ? _coopView.Round : CurrentCoopRound();
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("退出观战？", "Leave spectating?"),
                Body = L10n.T("本场押注按输结清。其他人的比赛继续，你可以通过联机菜单离开房间。",
                    "Your bet is settled as lost. The match continues; you can leave the room from the co-op menu."),
                ConfirmLabel = L10n.T("退出", "Leave"), Danger = true,
                OnConfirm = delegate
                {
                    if (!TryForfeitCoopRound(run, match, round, true)) return;
                    if (_coopProjection) { _coopIgnoredRun = run; ReleaseCoopProjection(); }
                    // 房主保留世界 owner 与推进页面，房间后续比赛仍可继续。
                }
            });
        }

        private bool TryForfeitCoopRound(string run, int match, string round, bool leaving)
        {
            if (_runState == null || _runState.RunId != run || GroupMatchIndex != match || _coopLedger == null) return false;
            if (!_coopLedger.Forfeit(run, match)) return false;
            ModeHCashBetRecord bet = ModeHCashBetService.ReservedFor(run, match);
            if (bet != null) ForfeitStartedCashBet(bet, leaving ? "coop_leave" : "coop_forfeit");
            string action = leaving ? "leave" : "forfeit";
            if (BossRushCoop.IsClient) _coopChannel.Request(action, text: round);
            else ExecuteCoopCommand(new CoopGameplayCommand { Action = action, Text = round, PlayerId = BossRushCoop.LocalPlayerId, AssetId = BossRushCoop.LocalAssetId });
            return true;
        }

        private void TickCoopSpectator(float deltaTime)
        {
            ModeHCoopWorld state = _coopView;
            bool live = state != null && ((ModeHLifecycle)state.Phase == ModeHLifecycle.MatchFighting || (ModeHLifecycle)state.Phase == ModeHLifecycle.RelayPending);
            if (!live || _spectatorLease == null || !string.IsNullOrEmpty(_coopControlLease)) return;
            int[] blue = state.BlueEntities ?? new int[0], red = state.RedEntities ?? new int[0];
            int count = blue.Length + red.Length;
            if (count > 0)
            {
                if (!BossRushUI.IsGamePaused() && !BossRushConfirmDialog.IsOpen)
                {
                    var events = UnityEngine.EventSystems.EventSystem.current;
                    bool over = events != null && events.IsPointerOverGameObject();
                    if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || (!over && Input.GetMouseButtonDown(0))) _coopCamera--;
                    if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || (!over && Input.GetMouseButtonDown(1))) _coopCamera++;
                    if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) _coopCamera = 0;
                    if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) _coopCamera = blue.Length;
                }
                _coopCamera = ((_coopCamera % count) + count) % count;
                CharacterMainControl target = BossRushCoop.FindAi(_coopCamera < blue.Length ? blue[_coopCamera] : red[_coopCamera - blue.Length]);
                _spectatorLease.SyncCameraTarget(target, target != null);
            }
            if (_ui == null) return;
            _ui.HudFirstLabel = ModeHGroupTeamTags.TeamName(false);
            _ui.HudSecondLabel = ModeHGroupTeamTags.TeamName(true);
            _ui.HudThirdLabel = L10n.T("场上共", "On field");
            _ui.TickHud(deltaTime, state.Remaining, state.BlueAlive + " / " + state.BlueTotal, state.RedAlive + " / " + state.RedTotal,
                state.BlueAlive + state.RedAlive, !state.BellConsumed, state.BellConsumed,
                ModeHGroupBattle.DescribeEffect((ModeHGroupBellEffect)state.BellEffect), state.BellRemaining);
        }

        private void OnCoopControlLease(string player, string lease, string entity)
        {
            if (player != BossRushCoop.LocalPlayerId || !_coopProjection || _spectatorLease == null) return;
            _coopControlLease = lease;
            if (!string.IsNullOrEmpty(lease)) _spectatorLease.YieldInputForErrorSwap();
            else _spectatorLease.ReclaimInputAfterErrorSwap();
        }
        private static bool Contains(string[] values, string value)
        { return values != null && Array.IndexOf(values, value) >= 0; }

        private void ProtectCoopSpectators(float deltaTime)
        {
            _coopSpectatorTimer -= Mathf.Max(0f, deltaTime);
            if (_coopSpectatorTimer > 0f) return;
            _coopSpectatorTimer = 0.25f;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsConnected || player.IsLocal || player.Character == null) continue;
                CoopSpectatorState state;
                if (!_coopSpectators.TryGetValue(player.Id, out state) || state.Character != player.Character)
                {
                    if (state != null) RestoreCoopSpectator(state);
                    state = new CoopSpectatorState { Character = player.Character, Team = player.Character.Team,
                        Invincible = player.Character.Health != null && player.Character.Health.Invincible };
                    _coopSpectators[player.Id] = state;
                }
                if (state.Character.Team != Teams.middle) state.Character.SetTeam(Teams.middle);
                if (state.Character.Health != null && !state.Character.Health.Invincible) state.Character.Health.SetInvincible(true);
            }
        }
        private static void RestoreCoopSpectator(CoopSpectatorState state)
        {
            if (state == null || state.Character == null) return;
            try
            {
                state.Character.SetTeam(state.Team);
                if (state.Character.Health != null) state.Character.Health.SetInvincible(state.Invincible);
            }
            catch (Exception e) { LogFailure("coop_spectator_restore", e); }
        }
        private void RestoreCoopSpectators()
        {
            foreach (CoopSpectatorState state in _coopSpectators.Values) RestoreCoopSpectator(state);
            _coopSpectators.Clear();
        }
    }
}

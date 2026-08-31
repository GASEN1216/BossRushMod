// Mode H 实战、接力与单批结算；运行时对象统一声明在 SceneFlow。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private bool EnsurePreparedMatchSelection(out string failureReasonId)
        {
            failureReasonId = null;
            if (_season == null || _runState == null || _season.currentMatchPlan == null)
            {
                failureReasonId = "match_selection_input_missing";
                return false;
            }

            if (_season.unlockedKitIds == null || _season.unlockedKitIds.Count == 0)
            {
                _season.unlockedKitIds = ModeHLoadoutKitRegistry.GetStarterKitIds();
            }

            if (_season.matchRoster == null
                || _season.matchRoster.matchIndex != _runState.MatchIndex)
            {
                List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
                if (live == null || live.Count == 0)
                {
                    failureReasonId = "match_roster_no_live_contract";
                    return false;
                }

                ModeHProfileDto starter = FindSeasonProfile(live[0]);
                ModeHProfileDto relay = live.Count > 1 ? FindSeasonProfile(live[1]) : null;
                if (starter == null)
                {
                    failureReasonId = "match_starter_missing";
                    return false;
                }

                ModeHMatchRosterDto roster = new ModeHMatchRosterDto();
                roster.matchIndex = _runState.MatchIndex;
                roster.matchStarterProfileId = starter.profileId;
                roster.matchRelayProfileId = relay != null ? relay.profileId : string.Empty;
                roster.starterKitIds = BuildDefaultKitSelection(starter);
                roster.relayKitIds = relay != null
                    ? BuildDefaultKitSelection(relay)
                    : new List<string>();
                roster.activeProfileId = starter.profileId;
                roster.enteredProfileIds = new List<string>();
                roster.relayConsumed = false;

                string digest, digestError;
                roster.loadoutDigest = string.Empty;
                if (!ModeHCanonicalDigest.TryComputeObjectDigest(
                        roster, null, out digest, out digestError))
                {
                    failureReasonId = "loadout_digest_failed:" + digestError;
                    return false;
                }
                roster.loadoutDigest = digest;
                _season.matchRoster = roster;
            }

            ModeHProfileDto selectedStarter =
                FindSeasonProfile(_season.matchRoster.matchStarterProfileId);
            ModeHProfileDto selectedRelay =
                FindSeasonProfile(_season.matchRoster.matchRelayProfileId);
            if (selectedStarter == null)
            {
                failureReasonId = "match_selected_starter_missing";
                return false;
            }
            _starterDisplayName = ResolveProfileDisplayName(selectedStarter.profileId);
            _relayDisplayName = selectedRelay != null
                ? ResolveProfileDisplayName(selectedRelay.profileId) : "-";

            List<string> commands = ModeHCommandController.GetSelectableCommands(
                selectedStarter.stableKey,
                selectedRelay != null ? selectedRelay.stableKey : null,
                selectedStarter.signatureCommandId,
                selectedRelay != null ? selectedRelay.signatureCommandId : null);
            if (commands == null || commands.Count == 0)
            {
                failureReasonId = "match_no_selectable_command";
                return false;
            }

            string commandId = commands.Contains(selectedStarter.signatureCommandId)
                ? selectedStarter.signatureCommandId
                : commands[0];
            ModeHOddsPlayerInput input = new ModeHOddsPlayerInput();
            input.Starter = selectedStarter;
            input.Relay = selectedRelay;
            input.StarterKitIds = _season.matchRoster.starterKitIds;
            input.RelayKitIds = _season.matchRoster.relayKitIds;
            input.CommandId = commandId;
            _currentOddsQuote = ModeHOddsController.BuildQuote(input, _season.currentMatchPlan);
            if (_currentOddsQuote == null)
            {
                failureReasonId = "odds_quote_failed";
                return false;
            }

            _selectedVirtualStake = ModeHVirtualStakeController.ClampStake(
                _selectedVirtualStake, _season.virtualStakeCredits);
            return true;
        }
        private List<string> BuildDefaultKitSelection(ModeHProfileDto profile)
        {
            List<string> selected = new List<string>();
            if (profile == null) return selected;

            List<ModeHResolvedKit> available = ModeHLoadoutKitRegistry.GetSelectableKits(
                _season.unlockedKitIds, profile.archetypeId, profile.profileId);
            HashSet<string> usedSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < available.Count && selected.Count < ModeHConfig.MaxKitsPerFighter; i++)
            {
                ModeHResolvedKit kit = available[i];
                if (kit == null || kit.Spec == null || !kit.Available) continue;
                string slot = kit.Spec.ReplaceSlot != null ? kit.Spec.ReplaceSlot : string.Empty;
                if (!usedSlots.Add(slot)) continue;
                selected.Add(kit.Spec.KitId);
            }
            selected.Sort(StringComparer.Ordinal);
            return selected;
        }
        private void SelectVirtualStake(int stake)
        {
            if (_season == null || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.OddsPreview
                && _runState.Lifecycle != ModeHLifecycle.LoadoutEditing)
            {
                return;
            }
            _selectedVirtualStake = ModeHVirtualStakeController.ClampStake(
                stake, _season.virtualStakeCredits);
            RouteUiForLifecycle(_runState.Lifecycle);
        }
        private bool PrepareLockedMatch(out string failureReasonId)
        {
            failureReasonId = null;
            if (!EnsurePreparedMatchSelection(out failureReasonId)) return false;

            ModeHMatchRosterDto roster = _season.matchRoster;
            ModeHProfileDto starter = FindSeasonProfile(roster.matchStarterProfileId);
            ModeHProfileDto relay = FindSeasonProfile(roster.matchRelayProfileId);
            if (starter == null || _currentOddsQuote == null)
            {
                failureReasonId = "lock_selection_missing";
                return false;
            }

            List<string> commands = ModeHCommandController.GetSelectableCommands(
                starter.stableKey,
                relay != null ? relay.stableKey : null,
                starter.signatureCommandId,
                relay != null ? relay.signatureCommandId : null);
            string commandId = commands.Contains(starter.signatureCommandId)
                ? starter.signatureCommandId
                : (commands.Count > 0 ? commands[0] : null);
            if (string.IsNullOrEmpty(commandId))
            {
                failureReasonId = "lock_command_missing";
                return false;
            }

            ModeHPreMatchSnapshotDto snapshot = new ModeHPreMatchSnapshotDto();
            snapshot.matchIndex = _runState.MatchIndex;
            snapshot.contractMainProfileSnapshot = CloneProfile(
                FindSeasonProfile(_season.contract != null
                    ? _season.contract.contractMainProfileId : null));
            snapshot.contractSubProfileSnapshot = CloneProfile(
                FindSeasonProfile(_season.contract != null
                    ? _season.contract.contractSubProfileId : null));
            snapshot.matchStarterProfileId = roster.matchStarterProfileId;
            snapshot.matchRelayProfileId = roster.matchRelayProfileId;
            snapshot.starterKitIds = new List<string>(roster.starterKitIds);
            snapshot.relayKitIds = new List<string>(roster.relayKitIds);
            snapshot.loadoutDigest = roster.loadoutDigest;
            snapshot.commandId = commandId;
            snapshot.lockedOdds = _currentOddsQuote.Odds;
            snapshot.capturedStateSequence = _runState.StateSequence;

            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(
                    _season.currentMatchPlan.publicSummary, null, out digest, out digestError))
            {
                failureReasonId = "public_summary_digest_failed:" + digestError;
                return false;
            }
            snapshot.publicSummaryDigest = digest;

            if (!ModeHVirtualStakeController.TryReserve(
                    _season, snapshot, _selectedVirtualStake, out failureReasonId))
            {
                return false;
            }

            ModeHLoadoutLockDto locked = new ModeHLoadoutLockDto();
            locked.matchIndex = _runState.MatchIndex;
            locked.matchStarterProfileId = roster.matchStarterProfileId;
            locked.matchRelayProfileId = roster.matchRelayProfileId;
            locked.starterKitIds = new List<string>(roster.starterKitIds);
            locked.relayKitIds = new List<string>(roster.relayKitIds);
            locked.commandId = commandId;
            locked.lockedOdds = _currentOddsQuote.Odds;
            locked.reservedVirtualStake = _selectedVirtualStake;
            locked.planId = _season.currentMatchPlan.planId;
            locked.planDigest = _season.currentMatchPlan.planDigest;
            locked.loadoutDigest = roster.loadoutDigest;
            locked.lockedStateSequence = _runState.StateSequence;
            locked.realStakeSelected = false;

            _season.preMatchSnapshot = snapshot;
            _season.currentLoadoutLock = locked;
            return true;
        }

        private IEnumerator DriveCompleteMatchSpawning()
        {
            if (_runState == null || _season == null || _season.currentMatchPlan == null
                || _season.currentLoadoutLock == null || _season.matchRoster == null)
            {
                AbortMatchSpawning("spawn_inputs_missing");
                yield break;
            }

            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            string failureReasonId;
            ModeHMatchPlanDto plan = _season.currentMatchPlan;
            ModeHLoadoutLockDto locked = _season.currentLoadoutLock;
            ModeHProfileDto starter = FindSeasonProfile(locked.matchStarterProfileId);
            if (starter == null)
            {
                AbortMatchSpawning("spawn_starter_profile_missing");
                yield break;
            }

            List<CharacterRandomPreset> enemyPresets = new List<CharacterRandomPreset>();
            List<string> enemyKeys = new List<string>();
            if (plan.enemyStableKeys == null || plan.enemyStableKeys.Count == 0)
            {
                AbortMatchSpawning("spawn_enemy_plan_empty");
                yield break;
            }
            for (int i = 0; i < plan.enemyStableKeys.Count; i++)
            {
                string key = plan.enemyStableKeys[i];
                CharacterRandomPreset preset = ModeHPresetRegistry.GetAuditedPreset(key);
                if (preset == null)
                {
                    AbortMatchSpawning("enemy_preset_missing:" + key);
                    yield break;
                }
                enemyKeys.Add(key);
                enemyPresets.Add(preset);
            }

            CharacterRandomPreset fighterPreset = ModeHPresetRegistry.GetAuditedPreset(starter.stableKey);
            if (fighterPreset == null)
            {
                AbortMatchSpawning("starter_preset_missing:" + starter.stableKey);
                yield break;
            }

            _spawnTransaction = new ModeHSpawnTransaction();
            if (!_spawnTransaction.Begin(_map, generation, ownerToken, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "spawn_tx_begin_failed");
                yield break;
            }

            ModeHSpawnDiagnostics diagnostics = new ModeHSpawnDiagnostics();
            ModeHSpawnBatchResult enemyResult = new ModeHSpawnBatchResult();
            IEnumerator enemyBatch = _spawnTransaction.SpawnBatch(
                enemyPresets, enemyKeys, Teams.wolf, false, diagnostics, enemyResult);
            while (enemyBatch.MoveNext())
            {
                if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                yield return enemyBatch.Current;
            }
            if (!enemyResult.Success)
            {
                AbortMatchSpawning(enemyResult.FailureReasonId ?? "enemy_spawn_failed");
                yield break;
            }

            ModeHSpawnBatchResult fighterResult = new ModeHSpawnBatchResult();
            IEnumerator fighterBatch = _spawnTransaction.SpawnBatch(
                new CharacterRandomPreset[] { fighterPreset },
                new string[] { starter.stableKey },
                Teams.scav, true, diagnostics, fighterResult);
            while (fighterBatch.MoveNext())
            {
                if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                yield return fighterBatch.Current;
            }
            if (!fighterResult.Success || _spawnTransaction.FighterHandles.Count != 1)
            {
                AbortMatchSpawning(fighterResult.FailureReasonId ?? "fighter_spawn_failed");
                yield break;
            }

            _activeFighterHandle = _spawnTransaction.FighterHandles[0];
            _activeFighterHandle.ProfileId = starter.profileId;
            if (!ModeHLoadoutKitApplicator.TryApply(
                    _activeFighterHandle, locked.starterKitIds,
                    out _activeKitApplication, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "starter_kit_failed");
                yield break;
            }

            if (!InitializeCombatRuntime(starter, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "combat_runtime_init_failed");
                yield break;
            }

            if (!_spawnTransaction.TryCommit(
                    _map.ArenaSpawnPoints, _map.PlayerSpawnPos, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "spawn_commit_failed");
                yield break;
            }

            yield return null;
            if (!IsCallbackStillValid(ownerToken, generation)) yield break;
            if (!_spawnTransaction.VerifyTeamsStableNextFrame(false, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "spawn_team_drift");
                yield break;
            }

            for (int i = 0; i < _enemyParticipants.Count; i++)
            {
                _combatControl.OnEnemyEntered(_enemyParticipants[i]);
            }
            if (!_combatControl.OnFighterEntered(_starterParticipant, starter, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "starter_enter_failed");
                yield break;
            }
            starter.enteredMatchCount++;
            _season.matchRoster.enteredProfileIds.Add(starter.profileId);
            _season.matchRoster.activeProfileId = starter.profileId;

            RefreshBattleSnapshotContext();
            _combatControl.OnEnemyBatchEntered(ResolveLastEntryBatch(plan), _battleSnapshotContext);
            AttachAndPersistBattleSnapshot("initial_batch_entered");

            _spawnRoutine = null;
            if (!TryTransition(ModeHLifecycle.MatchSpawning, ModeHLifecycle.MatchFighting, "combat_started"))
            {
                RequestTechnicalRetry("combat_transition_rejected");
                yield break;
            }
            TryPersistSeason("match_fighting");
        }
        private bool InitializeCombatRuntime(ModeHProfileDto starter, out string failureReasonId)
        {
            failureReasonId = null;
            _combatTelemetry = new ModeHCombatTelemetry();
            _combatControl = new ModeHCombatControl();
            string highThreatKey = null;
            if (_season.currentMatchPlan.publicSummary != null
                && _season.currentMatchPlan.publicSummary.hasHighThreatCore
                && _season.currentMatchPlan.enemyStableKeys != null
                && _season.currentMatchPlan.enemyStableKeys.Count > 0)
            {
                highThreatKey = _season.currentMatchPlan.enemyStableKeys[0];
            }
            _combatControl.BeginMatch(
                _runState, _map, _combatTelemetry, _runState.MatchIndex,
                _runState.RunSeed, highThreatKey);

            _starterParticipant = BuildParticipant(_activeFighterHandle, starter.profileId, false, -1, false);
            ModeHProfileDto relay = FindSeasonProfile(_season.matchRoster.matchRelayProfileId);
            _relayParticipant = relay != null
                ? new ModeHParticipantRef
                {
                    ProfileId = relay.profileId,
                    StableKey = relay.stableKey,
                    IsEnemy = false,
                    IsRelay = true,
                    PlanSlotIndex = -1,
                    Character = null,
                }
                : null;
            _combatControl.SetRoster(_starterParticipant, _relayParticipant);

            _enemyParticipants.Clear();
            for (int i = 0; i < _spawnTransaction.EnemyHandles.Count; i++)
            {
                ModeHSpawnHandle handle = _spawnTransaction.EnemyHandles[i];
                handle.PlanSlotIndex = i;
                ModeHParticipantRef enemy = BuildParticipant(handle, null, true, i, false);
                _enemyParticipants.Add(enemy);
                ModeHSnapshotEnemyInput snapshotEnemy = new ModeHSnapshotEnemyInput();
                snapshotEnemy.PlanSlotIndex = i;
                snapshotEnemy.StableKey = handle.StableKey;
                snapshotEnemy.ProfileId = string.Empty;
                snapshotEnemy.Character = handle.Character;
                _snapshotEnemies.Add(snapshotEnemy);
            }

            if (!ModeHEventRouter.Bind(_runState.OwnerToken, _combatTelemetry, out failureReasonId))
            {
                return false;
            }
            ModeHEventRouter.SetContext(_runState, _sceneGeneration, _runState.MatchIndex);
            RegisterParticipant(_activeFighterHandle, _starterParticipant);
            for (int i = 0; i < _spawnTransaction.EnemyHandles.Count; i++)
            {
                RegisterParticipant(_spawnTransaction.EnemyHandles[i], _enemyParticipants[i]);
            }

            if (!_combatControl.CommandController.LockCommand(
                    _season.currentLoadoutLock.commandId, starter.profileId,
                    _runState.OwnerToken, out failureReasonId))
            {
                return false;
            }
            _combatControl.BindPlayerBody(CharacterMainControl.Main);
            EnsureBattleSnapshotContext();
            return true;
        }

        private static ModeHParticipantRef BuildParticipant(
            ModeHSpawnHandle handle, string profileId, bool enemy, int planSlotIndex, bool relay)
        {
            return new ModeHParticipantRef
            {
                ProfileId = profileId ?? string.Empty,
                StableKey = handle != null ? handle.StableKey : string.Empty,
                PlanSlotIndex = planSlotIndex,
                IsEnemy = enemy,
                IsRelay = relay,
                Character = handle != null ? handle.Character : null,
            };
        }

        private static void RegisterParticipant(ModeHSpawnHandle handle, ModeHParticipantRef participant)
        {
            if (handle != null && handle.Health != null && participant != null)
            {
                ModeHEventRouter.RegisterParticipant(handle.Health, participant);
            }
        }

        private static int ResolveLastEntryBatch(ModeHMatchPlanDto plan)
        {
            int max = 0;
            if (plan == null || plan.enemyBatchIndices == null) return max;
            for (int i = 0; i < plan.enemyBatchIndices.Count; i++)
            {
                if (plan.enemyBatchIndices[i] > max) max = plan.enemyBatchIndices[i];
            }
            return max;
        }

        private void TickActiveCombat(float deltaTime)
        {
            if (_combatControl == null || _combatTelemetry == null) return;
            RefreshBattleSnapshotContext();

            if (_ui != null)
            {
                _ui.TickHud(
                    deltaTime,
                    _combatTelemetry.RemainingSeconds,
                    _starterDisplayName,
                    _relayDisplayName,
                    _combatTelemetry.LiveEnemyCount,
                    _combatControl.CommandController.CanRingBell,
                    _combatControl.CommandController.BellConsumed,
                    _combatControl.CommandController.LockedCommandId,
                    _combatControl.CommandController.CommandWindowRemainingSeconds);
            }

            if (_runState.Lifecycle == ModeHLifecycle.RelayPending) return;

            int snapshotSequence = _combatControl.Snapshot.SnapshotSequence;
            bool resultClaimed = _combatControl.Tick(deltaTime, _battleSnapshotContext);
            if (_combatControl.Snapshot.SnapshotSequence != snapshotSequence)
            {
                AttachAndPersistBattleSnapshot("combat_snapshot");
            }

            if (resultClaimed || _combatTelemetry.HasResult)
            {
                BeginMatchSettlement();
                return;
            }

            if (_combatControl.IsRelayWindowOpen && _relaySpawnRoutine == null)
            {
                if (!TryTransition(ModeHLifecycle.MatchFighting, ModeHLifecycle.RelayPending,
                        "starter_down_relay"))
                {
                    RequestTechnicalRetry("relay_transition_rejected");
                    return;
                }
                _relaySpawnRoutine = _owner.StartCoroutine(DriveRelaySpawning());
            }
        }

        private IEnumerator DriveRelaySpawning()
        {
            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            ModeHProfileDto relay = FindSeasonProfile(_season.matchRoster.matchRelayProfileId);
            if (relay == null)
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry("relay_profile_missing");
                yield break;
            }

            if (_activeFighterHandle != null)
            {
                try { ModeHEventRouter.UnregisterParticipant(_activeFighterHandle.Health); }
                catch (Exception e) { LogFailure("relay_unregister_starter", e); }
                ModeHLoadoutKitApplicator.Recycle(_activeKitApplication);
                _activeKitApplication = null;
                if (_spawnTransaction != null) _spawnTransaction.RecycleFighter(_activeFighterHandle);
                _activeFighterHandle = null;
            }

            CharacterRandomPreset preset = ModeHPresetRegistry.GetAuditedPreset(relay.stableKey);
            string failureReasonId;
            if (preset == null)
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry("relay_preset_missing:" + relay.stableKey);
                yield break;
            }

            _relaySpawnTransaction = new ModeHSpawnTransaction();
            if (!_relaySpawnTransaction.Begin(_map, generation, ownerToken, out failureReasonId))
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry(failureReasonId ?? "relay_tx_begin_failed");
                yield break;
            }

            ModeHSpawnBatchResult result = new ModeHSpawnBatchResult();
            IEnumerator batch = _relaySpawnTransaction.SpawnBatch(
                new CharacterRandomPreset[] { preset }, new string[] { relay.stableKey },
                Teams.scav, true, new ModeHSpawnDiagnostics(), result);
            while (batch.MoveNext())
            {
                if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                yield return batch.Current;
            }
            if (!result.Success || _relaySpawnTransaction.FighterHandles.Count != 1)
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry(result.FailureReasonId ?? "relay_spawn_failed");
                yield break;
            }

            ModeHSpawnHandle handle = _relaySpawnTransaction.FighterHandles[0];
            handle.ProfileId = relay.profileId;
            if (!ModeHLoadoutKitApplicator.TryApply(
                    handle, _season.currentLoadoutLock.relayKitIds,
                    out _activeKitApplication, out failureReasonId))
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry(failureReasonId ?? "relay_kit_failed");
                yield break;
            }

            _relayParticipant.Character = handle.Character;
            RegisterParticipant(handle, _relayParticipant);
            if (!_relaySpawnTransaction.TryCommit(
                    new Vector3[0], _map.PlayerSpawnPos, out failureReasonId))
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry(failureReasonId ?? "relay_commit_failed");
                yield break;
            }
            yield return null;
            if (!IsCallbackStillValid(ownerToken, generation)) yield break;
            if (!_relaySpawnTransaction.VerifyTeamsStableNextFrame(false, out failureReasonId))
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry(failureReasonId ?? "relay_team_drift");
                yield break;
            }

            _activeFighterHandle = handle;
            RefreshBattleSnapshotContext();
            if (!_combatControl.CommitRelay(relay, _battleSnapshotContext, out failureReasonId))
            {
                _relaySpawnRoutine = null;
                RequestTechnicalRetry(failureReasonId ?? "relay_commit_control_failed");
                yield break;
            }
            relay.enteredMatchCount++;
            _season.matchRoster.enteredProfileIds.Add(relay.profileId);
            _season.matchRoster.activeProfileId = relay.profileId;
            _season.matchRoster.relayConsumed = true;
            AttachAndPersistBattleSnapshot("relay_committed");

            _relaySpawnRoutine = null;
            if (!TryTransition(ModeHLifecycle.RelayPending, ModeHLifecycle.MatchFighting,
                    "relay_entered"))
            {
                RequestTechnicalRetry("relay_resume_rejected");
            }
        }

        private void EnsureBattleSnapshotContext()
        {
            if (_battleSnapshotContext != null) return;
            _battleSnapshotContext = new ModeHBattleSnapshotContext();
            _battleSnapshotContext.PendingBatchStableKeys = _pendingBatchKeys;
            _battleSnapshotContext.Enemies = _snapshotEnemies;
        }

        private void RefreshBattleSnapshotContext()
        {
            EnsureBattleSnapshotContext();
            _pendingBatchKeys.Clear();
            for (int i = _snapshotEnemies.Count - 1; i >= 0; i--)
            {
                ModeHSnapshotEnemyInput input = _snapshotEnemies[i];
                bool active = false;
                try
                {
                    active = input != null && input.Character != null
                        && input.Character.gameObject.activeInHierarchy;
                }
                catch (Exception) { active = false; }
                if (!active) _snapshotEnemies.RemoveAt(i);
            }

            if (_activeFighterHandle != null && _activeFighterHandle.Character != null)
            {
                _snapshotEntrant.PlanSlotIndex = -1;
                _snapshotEntrant.StableKey = _activeFighterHandle.StableKey;
                _snapshotEntrant.ProfileId = _activeFighterHandle.ProfileId;
                _snapshotEntrant.Character = _activeFighterHandle.Character;
                _battleSnapshotContext.Entrant = _snapshotEntrant;
            }
            else
            {
                _battleSnapshotContext.Entrant = null;
            }
            _battleSnapshotContext.EntrantIsRelay = _season != null && _season.matchRoster != null
                && _season.matchRoster.relayConsumed;
            _battleSnapshotContext.ErrorSwapActive = _combatControl != null
                && _combatControl.IsErrorSwapActive;
            _battleSnapshotContext.ErrorSwapProfileId = _combatControl != null
                ? _combatControl.ErrorSwapProfileId : string.Empty;
            _battleSnapshotContext.StandInPatternId = _combatControl != null
                ? _combatControl.StandInPatternId : string.Empty;
        }

        private void AttachAndPersistBattleSnapshot(string reasonId)
        {
            if (_combatControl == null || _season == null) return;
            _combatControl.Snapshot.AttachTo(_season);
            TryPersistSeason(reasonId);
        }

        private void BeginMatchSettlement()
        {
            if (_runState == null || _season == null || _combatTelemetry == null
                || _combatTelemetry.Result == null)
            {
                RequestTechnicalRetry("settlement_result_missing");
                return;
            }
            if (!TryTransition(ModeHLifecycle.MatchFighting, ModeHLifecycle.MatchSettling,
                    "result_claimed"))
            {
                return;
            }

            try
            {
                ModeHLoadoutLockDto locked = _season.currentLoadoutLock;
                int odds = locked != null ? locked.lockedOdds : ModeHConfig.MinOdds;
                string survivingProfileId = _combatControl.ActiveProfileId;
                _combatTelemetry.FinalizeSpecialKill(odds, survivingProfileId);

                ModeHMatchReportDto report = new ModeHMatchReportDto();
                report.reportStatus = (int)ModeHMatchReportStatus.SettledPendingArchive;
                report.injuryEvents = new List<ModeHInjuryEventDto>();
                _combatTelemetry.WriteReport(
                    report,
                    _combatControl.ErrorTriggered,
                    _combatControl.CommandController.LockedCommandId,
                    _combatControl.CommandController.BellConsumed);

                ResolveDownInjury(report, locked != null ? locked.matchStarterProfileId : null);
                ResolveDownInjury(report, locked != null ? locked.matchRelayProfileId : null);

                bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
                int rewardCandidates = ModeHVirtualStakeController.Settle(
                    _season, _season.preMatchSnapshot, report, odds, won);

                ModeHProfileDto rewardProfile = FindSeasonProfile(
                    !string.IsNullOrEmpty(survivingProfileId)
                        ? survivingProfileId
                        : (locked != null ? locked.matchStarterProfileId : null));
                if (won && rewardProfile != null
                    && ModeHVirtualStakeController.MeetsScarOfferGate(odds))
                {
                    string scarFailure;
                    report.scarOfferId = ModeHInjuryAndScarSystem.PickScarOffer(
                        rewardProfile, _runState.RunSeed, _runState.MatchIndex, out scarFailure);
                    if (!string.IsNullOrEmpty(report.scarOfferId))
                    {
                        string acceptFailure;
                        if (rewardProfile.scarIds != null
                            && rewardProfile.scarIds.Count >= ModeHConfig.MaxScarsPerProfile)
                        {
                            ModeHInjuryAndScarSystem.DeclineScar(rewardProfile);
                        }
                        else
                        {
                            ModeHInjuryAndScarSystem.TryAcceptScar(
                                rewardProfile, report.scarOfferId, null, out acceptFailure);
                        }
                    }
                }

                string rewardFailure;
                ModeHSeasonRewardOperationDto operation = ModeHSeasonRewardService.BuildOrGet(
                    _season, report, rewardProfile != null ? rewardProfile.profileId : string.Empty,
                    rewardCandidates, out rewardFailure);
                if (operation == null)
                {
                    RequestTechnicalRetry(rewardFailure ?? "reward_operation_failed");
                    return;
                }
                report.seasonRewardOperationId = operation.operationId;
                UpsertMatchReport(report);
                _combatControl.Snapshot.ClearFrom(_season);

                _lastSettlementReport = report;
                _lastRewardOperation = operation;
                if (!TryPersistSeason("match_settling"))
                {
                    // 战报与奖励 operation 已完整构造，保留它们进入恢复壳；恢复时直接
                    // 回 Intermission，绝不能退回看盘重打一场并重复结算。
                    ReleaseCombatRuntimeObjects();
                    RequestSuspended("settlement_persist_failed");
                    return;
                }

                ReleaseCombatRuntimeObjects();
                if (TryTransition(ModeHLifecycle.MatchSettling, ModeHLifecycle.Intermission,
                        "settlement_committed"))
                {
                    TryPersistSeason("intermission");
                }
            }
            catch (Exception e)
            {
                LogFailure("match_settlement", e);
                if (_lastSettlementReport != null)
                {
                    ReleaseCombatRuntimeObjects();
                    RequestSuspended("settlement_exception_after_report");
                }
                else
                {
                    RequestTechnicalRetry("settlement_exception");
                }
            }
        }

        private void ResolveDownInjury(ModeHMatchReportDto report, string profileId)
        {
            if (report == null || string.IsNullOrEmpty(profileId) || _combatTelemetry == null) return;
            string token = _combatTelemetry.GetDownToken(profileId);
            if (string.IsNullOrEmpty(token)) return;
            ModeHProfileDto profile = FindSeasonProfile(profileId);
            ModeHInjuryEventDto injury = _combatControl.InjuryAndScar.ResolveDownEvent(
                profile, _runState.RunSeed, _runState.MatchIndex, token,
                report.injuryEvents.Count + 1);
            if (injury != null) report.injuryEvents.Add(injury);
        }

        private void UpsertMatchReport(ModeHMatchReportDto report)
        {
            if (_season.matchReports == null) _season.matchReports = new List<ModeHMatchReportDto>();
            for (int i = 0; i < _season.matchReports.Count; i++)
            {
                if (_season.matchReports[i] != null
                    && _season.matchReports[i].matchIndex == report.matchIndex)
                {
                    _season.matchReports[i] = report;
                    return;
                }
            }
            _season.matchReports.Add(report);
            _season.matchReports.Sort(delegate(ModeHMatchReportDto a, ModeHMatchReportDto b)
            {
                return (a != null ? a.matchIndex : 0).CompareTo(b != null ? b.matchIndex : 0);
            });
        }

        private ModeHPageContent BuildCompletedSettlementPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Settlement");
            ModeHMatchReportDto report = _lastSettlementReport ?? FindLatestPendingReport();
            ModeHSeasonRewardOperationDto operation = _lastRewardOperation
                ?? FindRewardOperation(report != null ? report.seasonRewardOperationId : null);
            if (report == null)
            {
                page.Body = L10n.T("结算记录不可用", "Settlement record unavailable");
                return page;
            }

            bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
            page.Body = won ? L10n.T("本场胜利", "Victory") : L10n.T("本场失利", "Defeat");
            page.Lines.Add(L10n.T("耗时：", "Time: ") + report.elapsedSeconds.ToString("0.0") + "s");
            page.Lines.Add(L10n.T("赔率：x", "Odds: x") + report.lockedOdds
                + L10n.T("　下注：", "  Stake: ") + report.virtualStakeAmount);
            page.Lines.Add(L10n.T("筹码：", "Credits: ") + report.virtualStakeBalanceBefore
                + " → " + report.virtualStakeBalanceAfter);
            if (report.injuryEvents != null && report.injuryEvents.Count > 0)
            {
                page.Lines.Add(L10n.T("倒地伤病：", "Down injuries: ") + report.injuryEvents.Count);
            }

            if (operation != null
                && operation.status == (int)ModeHSeasonRewardOperationStatus.Offered
                && operation.candidateKitIds != null)
            {
                for (int i = 0; i < operation.candidateKitIds.Count; i++)
                {
                    string kitId = operation.candidateKitIds[i];
                    string selectedKitId = kitId;
                    page.Actions.Add(new ModeHActionData
                    {
                        Label = L10n.T("解锁整备：", "Unlock kit: ") + kitId,
                        OnClick = delegate { SelectSettlementReward(selectedKitId, false); },
                    });
                }
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("放弃整备，换取名声", "Decline kits for fame"),
                    OnClick = delegate { SelectSettlementReward(null, true); },
                });
            }
            else
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    OnClick = CompleteSettlementAndRoute,
                });
            }
            return page;
        }

        private void SelectSettlementReward(string kitId, bool decline)
        {
            if (_season == null || _lastRewardOperation == null || _runState == null
                || _runState.Lifecycle != ModeHLifecycle.Intermission)
            {
                return;
            }
            string failureReasonId;
            bool ok = decline
                ? ModeHSeasonRewardService.TryDeclineToFame(
                    _season, _lastRewardOperation.operationId, out failureReasonId)
                : ModeHSeasonRewardService.TrySelectKit(
                    _season, _lastRewardOperation.operationId, kitId, out failureReasonId);
            if (!ok)
            {
                ModBehaviour.DevLog("[ModeH] 奖励选择失败: " + (failureReasonId ?? "unknown"));
                return;
            }
            CompleteSettlementAndRoute();
        }

        private void CompleteSettlementAndRoute()
        {
            if (_season == null || _runState == null
                || _runState.Lifecycle != ModeHLifecycle.Intermission)
            {
                return;
            }
            ModeHMatchReportDto report = _lastSettlementReport ?? FindLatestPendingReport();
            ModeHSeasonRewardOperationDto operation = _lastRewardOperation
                ?? FindRewardOperation(report != null ? report.seasonRewardOperationId : null);
            if (report == null || operation == null) return;

            string failureReasonId;
            if (operation.status == (int)ModeHSeasonRewardOperationStatus.Offered) return;
            if (operation.status == (int)ModeHSeasonRewardOperationStatus.Applied
                && !ModeHSeasonRewardService.TryArchive(
                    _season, operation.operationId, out failureReasonId))
            {
                return;
            }
            report.reportStatus = (int)ModeHMatchReportStatus.Archived;
            if (!TryPersistSeason("intermission_archive"))
            {
                RequestSuspended("intermission_archive_failed");
                return;
            }
            RouteAfterIntermission(report);
        }

        private void RouteAfterIntermission(ModeHMatchReportDto report)
        {
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
            if (live == null || live.Count == 0)
            {
                FinishSeason("no_live_contracts");
                return;
            }
            if (_runState.MatchIndex >= ModeHConfig.SeasonMatchCount)
            {
                if (report.winner == (int)ModeHMatchOutcome.PlayerVictory)
                {
                    EnterHallOfFame();
                }
                else
                {
                    FinishSeason("final_match_defeat");
                }
                return;
            }
            if (ModeHConfig.IsTransferWindowMatch(_runState.MatchIndex))
            {
                TryTransition(ModeHLifecycle.Intermission, ModeHLifecycle.TransferWindow,
                    "transfer_window_open");
                return;
            }
            OpenNextMatchBrief("intermission_complete");
        }

        private void EnterHallOfFame()
        {
            ModeHHallOfFameRecordDto record = BuildHallOfFameRecord();
            if (record == null)
            {
                RequestSuspended("hall_record_build_failed");
                return;
            }
            string digest, error;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(record, null, out digest, out error))
            {
                RequestSuspended("hall_record_digest_failed:" + error);
                return;
            }

            ModeHHallOfFameCommandDto command = new ModeHHallOfFameCommandDto();
            command.hallOfFameId = record.hallOfFameId;
            command.recordSnapshot = record;
            command.recordDigest = digest;
            command.status = (int)ModeHHallOfFameCommandStatus.Pending;
            _season.hallOfFameCommand = command;
            if (!TryPersistSeason("hall_command_pending"))
            {
                RequestSuspended("hall_command_persist_failed");
                return;
            }
            if (!ModeHSaveFlushCoordinator.RequestHallOfFameInsert(record, out error))
            {
                RequestSuspended("hall_insert_failed:" + error);
                return;
            }
            command.status = (int)ModeHHallOfFameCommandStatus.Completed;
            if (!TryPersistSeason("hall_command_completed"))
            {
                RequestSuspended("hall_complete_persist_failed");
                return;
            }
            TryTransition(ModeHLifecycle.Intermission, ModeHLifecycle.HallOfFame,
                "champion_recorded");
        }

        private ModeHHallOfFameRecordDto BuildHallOfFameRecord()
        {
            if (_season == null || _runState == null) return null;
            ModeHProfileDto champion = FindSeasonProfile(
                _season.contract != null ? _season.contract.contractMainProfileId : null);
            if (champion == null) return null;

            ModeHHallOfFameRecordDto record = new ModeHHallOfFameRecordDto();
            record.hallOfFameId = "hof|" + _runState.RunId;
            record.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            record.seasonVersion = ModeHConfig.CurrentSchemaVersion;
            record.championProfileSnapshot = CloneProfile(champion);
            record.aliasKey = champion.displayNameKey ?? string.Empty;
            record.archetypeId = champion.archetypeId ?? string.Empty;
            record.temperamentId = champion.temperamentId ?? string.Empty;
            record.quirkId = champion.quirkId ?? string.Empty;
            record.anomalyId = champion.anomalyId ?? string.Empty;
            record.signatureCommandId = champion.signatureCommandId ?? string.Empty;
            record.scarIds = champion.scarIds != null
                ? new List<string>(champion.scarIds) : new List<string>();
            record.matchReportIds = new List<string>();
            record.substituteHistory = new List<string>();
            if (_season.contract != null
                && !string.IsNullOrEmpty(_season.contract.contractSubProfileId))
            {
                record.substituteHistory.Add(_season.contract.contractSubProfileId);
            }

            if (_season.matchReports != null)
            {
                for (int i = 0; i < _season.matchReports.Count; i++)
                {
                    ModeHMatchReportDto report = _season.matchReports[i];
                    if (report == null) continue;
                    record.matchReportIds.Add(report.resultToken ?? string.Empty);
                    if (report.winner == (int)ModeHMatchOutcome.PlayerVictory)
                    {
                        if (report.lockedOdds > record.maxOddsWin) record.maxOddsWin = report.lockedOdds;
                        if (report.virtualStakeAmount > record.maxVirtualStakeWin)
                        {
                            record.maxVirtualStakeWin = report.virtualStakeAmount;
                        }
                    }
                }
            }
            record.finalVirtualStakeCredits = _season.virtualStakeCredits;
            record.maxRealStakeWin = 0;
            record.createdUtc = DateTime.UtcNow.ToString("O");
            record.gameBuildSignature = _season.gameBuildSignature ?? string.Empty;
            record.modBuildSignature = _season.modBuildSignature ?? string.Empty;
            return record;
        }

        private void ReleaseCombatRuntimeObjects()
        {
            try
            {
                if (_relaySpawnRoutine != null && _owner != null)
                {
                    _owner.StopCoroutine(_relaySpawnRoutine);
                }
            }
            catch (Exception e) { LogFailure("relay_coroutine_stop", e); }
            _relaySpawnRoutine = null;

            try { if (_combatControl != null) _combatControl.RestoreAll(); }
            catch (Exception e) { LogFailure("combat_restore", e); }

            try
            {
                ModeHEventRouter.ClearMatchRegistry();
                ModeHEventRouter.Unbind();
            }
            catch (Exception e) { LogFailure("combat_router_release", e); }

            ModeHLoadoutKitApplicator.Recycle(_activeKitApplication);
            _activeKitApplication = null;

            try
            {
                if (_relaySpawnTransaction != null) _relaySpawnTransaction.RollbackAll();
            }
            catch (Exception e) { LogFailure("relay_tx_release", e); }
            _relaySpawnTransaction = null;

            try
            {
                if (_spawnTransaction != null) _spawnTransaction.RollbackAll();
            }
            catch (Exception e) { LogFailure("match_tx_release", e); }
            _spawnTransaction = null;

            _activeFighterHandle = null;
            _starterParticipant = null;
            _relayParticipant = null;
            _enemyParticipants.Clear();
            _snapshotEnemies.Clear();
            _combatControl = null;
            _combatTelemetry = null;
            try { if (_ui != null) _ui.DestroyHud(); }
            catch (Exception e) { LogFailure("combat_hud_release", e); }
        }

        private void RestoreMatchReservationAndSnapshot()
        {
            if (_season == null) return;
            ModeHPreMatchSnapshotDto snapshot = _season.preMatchSnapshot;
            ModeHVirtualStakeController.RestoreReservation(_season, snapshot);
            if (snapshot != null)
            {
                ReplaceSeasonProfile(snapshot.contractMainProfileSnapshot);
                ReplaceSeasonProfile(snapshot.contractSubProfileSnapshot);
            }
            _season.currentLoadoutLock = null;
            _season.preMatchSnapshot = null;
            _season.currentBattleSnapshot = null;
            _season.matchRoster = null;
            _currentOddsQuote = null;
            _selectedVirtualStake = 0;
            _starterDisplayName = null;
            _relayDisplayName = null;
            RemoveUnarchivedSettlementForCurrentMatch();
        }

        private void RemoveUnarchivedSettlementForCurrentMatch()
        {
            if (_season == null || _runState == null || _season.matchReports == null) return;
            for (int i = _season.matchReports.Count - 1; i >= 0; i--)
            {
                ModeHMatchReportDto report = _season.matchReports[i];
                if (report == null || report.matchIndex != _runState.MatchIndex
                    || report.reportStatus == (int)ModeHMatchReportStatus.Archived) continue;
                string operationId = report.seasonRewardOperationId;
                _season.matchReports.RemoveAt(i);
                if (_season.seasonRewardOperations == null) continue;
                for (int j = _season.seasonRewardOperations.Count - 1; j >= 0; j--)
                {
                    ModeHSeasonRewardOperationDto operation = _season.seasonRewardOperations[j];
                    if (operation != null && string.Equals(
                            operation.operationId, operationId, StringComparison.Ordinal))
                    {
                        _season.seasonRewardOperations.RemoveAt(j);
                    }
                }
            }
            _lastSettlementReport = null;
            _lastRewardOperation = null;
        }

    }
}

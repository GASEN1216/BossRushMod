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
                _selectedMatchCommandId = null;
                _showLoadoutEditor = false;
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

            if (!commands.Contains(_selectedMatchCommandId))
                _selectedMatchCommandId = commands.Contains(selectedStarter.signatureCommandId)
                    ? selectedStarter.signatureCommandId : commands[0];
            string commandId = _selectedMatchCommandId;
            if (!RefreshSelectedLoadoutDigest(out failureReasonId)) return false;
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
            string commandId = commands.Contains(_selectedMatchCommandId) ? _selectedMatchCommandId : null;
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

            // 真实押品：锁盘是唯一允许把玩家物品移出仓库的时刻（§22.2）。
            // 没选押品时 TryLockForMatch 直接返回 true 且不建 journal（默认不押）。
            // 失败必须整体拒绝锁盘——绝不能"当作没押"继续开战，那会让玩家
            // 以为物品安全而实际可能已脱离仓库。
            if (!ModeHRealStakeService.TryLockForMatch(
                    _runState.RunId, _runState.MatchIndex, _runState.RunSeed, out failureReasonId))
            {
                ModeHVirtualStakeController.RestoreReservation(_season, snapshot);
                return false;
            }
            locked.realStakeSelected = ModeHWarehouseStakeJournal.Active != null;

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
            // 只放行第 0 批：后续批次由 TryReleaseNextEnemyBatch 在前批减员后补放。
            // 详见 ModeHRuntimeModule_CombatProfiles.SplitEnemyBatches 的注释。
            if (!SplitEnemyBatches(plan, enemyPresets, enemyKeys, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId ?? "spawn_enemy_batch_split_failed");
                yield break;
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
                    _activeFighterHandle, FilterKitsForInjury(locked.starterKitIds, starter.injuryId),
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
            _combatControl.OnEnemyBatchEntered(_currentEntryBatchIndex, _battleSnapshotContext);
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
            // 擂台条件与最后批次序号都是分量条件（appliesWhen）的输入，整场不变，
            // 因此和 highThreatKey 一样在开场一次性交给战斗控制，
            // 而不是让它反过来持有 Season 引用。
            _combatControl.BeginMatch(
                _runState, _map, _combatTelemetry, _runState.MatchIndex,
                _runState.RunSeed, highThreatKey,
                _season.currentMatchPlan.conditionId,
                ResolveLastEntryBatch(_season.currentMatchPlan));

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
                // 入场批次跟着计划走，供战痕条件 first_wave_alive 判断第一批是否还有活口。
                enemy.BatchIndex = ResolveEnemyBatchIndex(_season.currentMatchPlan, i);
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

            string lockedCommand = _season.currentLoadoutLock.commandId;
            ModeHProfileDto commandRelay = FindSeasonProfile(_season.currentLoadoutLock.matchRelayProfileId);
            string commandOwner = commandRelay != null && lockedCommand == commandRelay.signatureCommandId
                && lockedCommand != starter.signatureCommandId ? commandRelay.profileId : starter.profileId;
            if (!_combatControl.CommandController.LockCommand(
                    lockedCommand, commandOwner,
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

        /// <summary>取该计划槽位的入场批次序号；查不到按第一批（0）处理。</summary>
        private static int ResolveEnemyBatchIndex(ModeHMatchPlanDto plan, int planSlotIndex)
        {
            if (plan == null || plan.enemyBatchIndices == null) return 0;
            if (planSlotIndex < 0 || planSlotIndex >= plan.enemyBatchIndices.Count) return 0;
            int batch = plan.enemyBatchIndices[planSlotIndex];
            return batch > 0 ? batch : 0;
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
                    // 形参要的是**名字**，此前直接把内部 ID 传了进去，
                    // 玩家在拍铃按钮上看到的是 "steady" / "all_in" 这类下划线标识。
                    // Command_<id> 的中英文案早已注入，这里补上转换。
                    ResolveCommandDisplayName(_combatControl.CommandController.LockedCommandId),
                    _combatControl.CommandController.CommandWindowRemainingSeconds);
            }

            if (_runState.Lifecycle == ModeHLifecycle.RelayPending) return;

            // 前批减员到同时上限之下时放行下一批（见 CombatProfiles.TryReleaseNextEnemyBatch）
            TryReleaseNextEnemyBatch();

            int snapshotSequence = _combatControl.Snapshot.SnapshotSequence;
            bool resultClaimed = _combatControl.Tick(deltaTime, _battleSnapshotContext);
            // 紧跟 Tick：互换相位可能在 Tick 内部翻转（CompleteSwapHandover / RestoreErrorSwap）
            SyncErrorSwapInputYield();
            if (_combatControl.Snapshot.SnapshotSequence != snapshotSequence)
            {
                AttachAndPersistBattleSnapshot("combat_snapshot");
            }

            if (resultClaimed || _combatTelemetry.HasResult)
            {
                BeginMatchSettlement();
                return;
            }

            // 终局已锁定后不再开新互换，所以排在上面那个分支之后
            TryBeginErrorSwapIfDue();

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

        /// <summary>
        /// ERROR 完整互换的**唯一生产调用点**（§17.6.5）。
        ///
        /// profile 必须是当前登场选手自己的档案：异常门读的是 _activeAnomalyId，
        /// 接管的也是 _activeFighter.Character，取错人等于把控制权交给另一名选手。
        /// ActiveProfileId 由 OnFighterEntered 从同一个 profile 写入，构造上保证一致。
        ///
        /// 为什么由模块回查而不是让 ModeHCombatControl 自己持有 DTO：后者完全不持有
        /// Season 引用（只存三个字符串），而模块本来就要在这里决定输入让渡。
        ///
        /// 开始失败不判负、不技术中止、不消耗同场重试预算——§17.6.5 第 7 条要求
        /// 完整回滚后比赛照常继续。每场至多一次由 ErrorSwapAttempted 闩住。
        /// </summary>
        private void TryBeginErrorSwapIfDue()
        {
            if (_combatControl == null) return;
            if (!_combatControl.ErrorTriggered || _combatControl.ErrorSwapAttempted) return;

            ModeHProfileDto profile = FindSeasonProfile(_combatControl.ActiveProfileId);
            // 引用尚未就绪：闩还没置位，下一帧自然重试
            if (profile == null) return;

            string failureReasonId;
            if (!_combatControl.TryBeginErrorSwap(profile, out failureReasonId))
            {
                ModBehaviour.DevLog("[ModeH] ERROR 互换未开始: "
                    + (failureReasonId != null ? failureReasonId : "unknown"));
            }
        }

        /// <summary>
        /// 把「互换是否生效」同步成「租约是否让渡输入」。
        /// 只在状态位翻转时才真正调用 InputManager：每帧路径，O(1)、零分配。
        ///
        /// 观战租约在开战前就 DisableInput 了，不让渡的话玩家接到手的是一个动不了的选手。
        /// </summary>
        private void SyncErrorSwapInputYield()
        {
            if (_spectatorLease == null || _combatControl == null) return;
            bool wanted = _combatControl.IsErrorSwapActive;
            if (wanted == _errorSwapInputYielded) return;
            _errorSwapInputYielded = wanted;
            try
            {
                if (wanted) _spectatorLease.YieldInputForErrorSwap();
                else _spectatorLease.ReclaimInputAfterErrorSwap();
            }
            catch (Exception e)
            {
                LogFailure("error_swap_input_sync", e);
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
                    handle,
                    FilterKitsForInjury(_season.currentLoadoutLock.relayKitIds, relay.injuryId),
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

                // 濒退制的另一半：没踏上擂台的带伤选手赛后解除带伤。
                // 必须排在 ResolveDownInjury 之后——那条路径只在有倒地 token 时动手
                // （意味着登场过），与 HasRested 互斥，顺序在此只是为了读起来是
                // 「先结算这场发生了什么，再结算谁休息好了」。
                _restedProfileIds.Clear();
                ResolveRestRecovery(locked != null ? locked.matchStarterProfileId : null);
                ResolveRestRecovery(locked != null ? locked.matchRelayProfileId : null);

                // 退役结算（§17.3）必须排在人事步骤最后：ResolveDownInjury 是赛季里唯一
                // 把 profile 写成 Retired 的路径，而 ResolveRestRecovery 只能解除
                // 「从未登场者」的带伤，不可能把人反退役。
                //
                // 不接这一步的后果不在排兵布阵上（GetLiveContractProfileIds 本来就过滤
                // Retired，所以下一场照样派活人上），而在**合同槽本身**：
                // BuildHallOfFameRecord 读 contract.contractMainProfileId 认冠军、
                // 读 contractSubProfileId 填 substituteHistory。槽不结算，
                // 名人堂就会把已退役的主选手记成冠军，把真正打完 3-6 场的替补记成替补。
                string retireFailure;
                if (!ModeHTransferMarket.ApplyRetirement(_season, out retireFailure))
                {
                    // false 只有两种含义：contract 缺失，或两名合同选手都已退役。
                    // 后者是赛季自然终局，两个槽保持原样，GetLiveContractProfileIds 返回空，
                    // RouteAfterIntermission 已经会走 FinishSeason("no_live_contracts")，
                    // 且 live.Count == 0 短路在 EnterHallOfFame 之前。这里绝不自己跳状态机：
                    // 那会跳过结算页与已构造的奖励 operation，而且这不是技术故障，
                    // 不该消耗同场重试预算。
                    ModBehaviour.DevLog("[ModeH] 退役结算未改动合同槽: "
                        + (retireFailure != null ? retireFailure : "unknown"));
                }

                bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
                int rewardCandidates = ModeHVirtualStakeController.Settle(
                    _season, _season.preMatchSnapshot, report, odds, won);

                // 真实押品结算：无 journal 时是 no-op（本场没押）。
                // 失败**不重打这一场**——虚拟筹码已经结算过，重来会重复发奖；
                // journal 内部已进人工介入或保持 pending，交给恢复壳只读展示处置。
                string realStakeFailure;
                if (!ModeHRealStakeService.TrySettleMatch(
                        _runState.RunSeed, _runState.MatchIndex, won, odds, out realStakeFailure))
                {
                    ModBehaviour.CriticalLog(
                        "[ModeH] [WARNING] 真实押品结算未完成，已保留 journal 交恢复流程: "
                        + (realStakeFailure != null ? realStakeFailure : "unknown"));
                    // CriticalLog 之外必须有玩家可见的一句：这里涉及玩家的真实仓库装备，
                    // 只写日志等于让他以为押品已经结算完毕（本仓库反复出现的「静默失败」形态）。
                    if (_owner != null)
                    {
                        _owner.ShowMessage(
                            L10n.T(ModeHConfig.LocalizationKeyPrefix + "Settle_Failed"));
                    }
                }

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
                    // 候选只**抽出并记进战报**，处置权交给结算页（BuildSettlementScarActions）。
                    // 旧写法在这里替玩家定了：满三条直接 DeclineScar、否则直接 TryAcceptScar，
                    // 玩家既没得选也看不到提示，而 §17 冻结契约里的「满三条时明确替换一条」
                    // 因此永远走不到——replacedScarId 在生产代码里唯一的实参是 null。
                    // 留在 pending 的候选由结算页负责收口；玩家若直接关页，
                    // 下次打开结算页仍会看到它（战报里的 scarOfferId 是持久化的）。
                    _pendingScarProfileId = !string.IsNullOrEmpty(report.scarOfferId)
                        ? rewardProfile.profileId : null;
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

        /// <summary>
        /// 本场完整休息（带伤且从未登场）的选手赛后解除带伤。
        ///
        /// 结果只记进运行时的 <see cref="_restedProfileIds"/> 供结算页展示，
        /// **不写任何 DTO 字段**：ModeHCanonicalDigest 按反射遍历全部公有实例字段
        /// （null 也计入），给持久化 DTO 加字段会让所有已存赛季 VerifyDigest 失败并进写屏障。
        /// </summary>
        private void ResolveRestRecovery(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;
            if (_combatTelemetry == null || _combatControl == null) return;
            // 只要本场实际登场过（含接力后短暂登场）就不算休息
            if (!_combatTelemetry.HasRested(profileId)) return;

            ModeHProfileDto profile = FindSeasonProfile(profileId);
            if (_combatControl.InjuryAndScar.ResolveRestRecovery(profile))
            {
                _restedProfileIds.Add(profileId);
            }
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
            // 已知残留：ApplyRetirement 晋升替补后会把 subProfileId 清空，
            // 于是「主选手中途退役、替补顶上并夺冠」这一支的 substituteHistory 是空的
            // （冠军字段本身已经对了——那正是接通退役结算修好的部分）。
            // 要把被晋升者也记进来就得加持久字段，而本 DTO 进 canonical digest，
            // 加字段会让所有已存名人堂信封 VerifyDigest 失败。留待单独评估。
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

            // 停止接收拍铃。本方法是结算、倒地收尾、技术中止与离场的共同必经点，
            // 正是 StopAcceptingBell 注释写的那四个时机。
            //
            // 此前这个门从未被调用，`IsBellAccepting` 也没有读者：拍铃只靠
            // _commandsClosed + 生命周期挡着，而战斗运行时已经释放、
            // 观战租约却还没 Release 的那一小段窗口里两者都还没变，
            // 玩家此刻点铃会打到一个 _combatControl 已为 null 的空档。
            try { if (_spectatorLease != null) _spectatorLease.StopAcceptingBell(); }
            catch (Exception e) { LogFailure("bell_gate_close", e); }

            // 兜底收回输入阻断：结算与技术中止路径不会再进 TickActiveCombat，
            // 靠 SyncErrorSwapInputYield 的状态翻转已经等不到了。
            // 收回而不是放着不管，是因为看台身体在租约释放前仍应保持不可操作。
            if (_errorSwapInputYielded)
            {
                try { if (_spectatorLease != null) _spectatorLease.ReclaimInputAfterErrorSwap(); }
                catch (Exception e) { LogFailure("error_swap_input_reclaim", e); }
                _errorSwapInputYielded = false;
            }

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

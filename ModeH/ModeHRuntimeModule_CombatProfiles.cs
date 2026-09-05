// Mode H 实战使用的赛季选手查询与深拷贝辅助。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private void ReplaceSeasonProfile(ModeHProfileDto snapshot)
        {
            if (snapshot == null || _season == null || _season.profiles == null) return;
            for (int i = 0; i < _season.profiles.Count; i++)
            {
                if (_season.profiles[i] != null
                    && string.Equals(_season.profiles[i].profileId, snapshot.profileId,
                        StringComparison.Ordinal))
                {
                    _season.profiles[i] = CloneProfile(snapshot);
                    return;
                }
            }
        }

        private ModeHProfileDto FindSeasonProfile(string profileId)
        {
            if (_season == null || _season.profiles == null || string.IsNullOrEmpty(profileId)) return null;
            for (int i = 0; i < _season.profiles.Count; i++)
            {
                ModeHProfileDto profile = _season.profiles[i];
                if (profile != null
                    && string.Equals(profile.profileId, profileId, StringComparison.Ordinal))
                {
                    return profile;
                }
            }
            return null;
        }

        private static ModeHProfileDto CloneProfile(ModeHProfileDto source)
        {
            if (source == null) return null;
            ModeHProfileDto copy = new ModeHProfileDto();
            copy.profileId = source.profileId;
            copy.stableKey = source.stableKey;
            copy.displayNameKey = source.displayNameKey;
            copy.archetypeId = source.archetypeId;
            copy.temperamentId = source.temperamentId;
            copy.quirkId = source.quirkId;
            copy.anomalyId = source.anomalyId;
            copy.signatureCommandId = source.signatureCommandId;
            copy.rumorKey = source.rumorKey;
            copy.standInPatternId = source.standInPatternId;
            copy.status = source.status;
            copy.injuryId = source.injuryId;
            copy.scarIds = source.scarIds != null
                ? new List<string>(source.scarIds) : new List<string>();
            copy.fameDisplayCount = source.fameDisplayCount;
            copy.enteredMatchCount = source.enteredMatchCount;
            copy.behaviorStatuses = new List<ModeHBehaviorStatusDto>();
            if (source.behaviorStatuses != null)
            {
                for (int i = 0; i < source.behaviorStatuses.Count; i++)
                {
                    ModeHBehaviorStatusDto item = source.behaviorStatuses[i];
                    if (item == null) continue;
                    copy.behaviorStatuses.Add(new ModeHBehaviorStatusDto
                    {
                        entryId = item.entryId,
                        entryKind = item.entryKind,
                        status = item.status,
                    });
                }
            }
            return copy;
        }

        private string ResolveProfileDisplayName(string profileId)
        {
            ModeHProfileDto profile = FindSeasonProfile(profileId);
            if (profile == null) return "-";
            string key = !string.IsNullOrEmpty(profile.displayNameKey)
                ? profile.displayNameKey
                : ModeHConfig.LocalizationKeyPrefix + "Fighter_" + profile.profileId;
            return L10n.T(key);
        }

        private ModeHMatchReportDto FindLatestPendingReport()
        {
            if (_season == null || _season.matchReports == null) return null;
            for (int i = _season.matchReports.Count - 1; i >= 0; i--)
            {
                ModeHMatchReportDto report = _season.matchReports[i];
                if (report != null && report.matchIndex == _runState.MatchIndex) return report;
            }
            return null;
        }

        private ModeHSeasonRewardOperationDto FindRewardOperation(string operationId)
        {
            if (_season == null || _season.seasonRewardOperations == null
                || string.IsNullOrEmpty(operationId)) return null;
            for (int i = 0; i < _season.seasonRewardOperations.Count; i++)
            {
                ModeHSeasonRewardOperationDto operation = _season.seasonRewardOperations[i];
                if (operation != null
                    && string.Equals(operation.operationId, operationId, StringComparison.Ordinal))
                {
                    return operation;
                }
            }
            return null;
        }

        /// <summary>
        /// 按选手伤病剔除被禁用的整备槽。`armor` 伤病的语义就是「本场不许穿护甲」，
        /// 而此前唯一的落点是 `IsArmorKitDisabled`——零读者，且置位还晚于整备应用，
        /// 于是这条伤病是彻底的空操作。这里在应用前就把对应槽的整备摘掉。
        /// </summary>
        private static IList<string> FilterKitsForInjury(IList<string> kitIds, string injuryId)
        {
            if (kitIds == null || kitIds.Count == 0 || string.IsNullOrEmpty(injuryId)) return kitIds;

            List<string> kept = null;
            for (int i = 0; i < kitIds.Count; i++)
            {
                string kitId = kitIds[i];
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(kitId);
                bool blocked = kit != null && kit.Spec != null
                    && ModeHInjuryAndScarSystem.InjuryDisablesKitSlot(injuryId, kit.Spec.ReplaceSlot);
                if (!blocked)
                {
                    if (kept != null) kept.Add(kitId);
                    continue;
                }
                if (kept == null)
                {
                    kept = new List<string>(kitIds.Count);
                    for (int j = 0; j < i; j++) kept.Add(kitIds[j]);
                }
                ModBehaviour.DevLog("[ModeH] 伤病 " + injuryId + " 禁用整备槽，已剔除: " + kitId);
            }
            return kept != null ? (IList<string>)kept : kitIds;
        }

        /// <summary>口令内部 ID -> 已注入的显示名。空 ID 原样返回空串。</summary>
        private static string ResolveCommandDisplayName(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return string.Empty;
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "Command_" + commandId);
        }

        #region 敌军分批入场

        /// <summary>一条尚未放行的后续批次敌军。</summary>
        private sealed class ModeHPendingEnemyEntry
        {
            /// <summary>入场批次序号（>0；第 0 批开场直接生成）。</summary>
            internal int BatchIndex;

            /// <summary>敌军 preset 的 stable key。</summary>
            internal string StableKey;
        }

        /// <summary>尚未放行的后续批次，按批号升序消费。</summary>
        private readonly List<ModeHPendingEnemyEntry> _pendingEnemyBatchKeys =
            new List<ModeHPendingEnemyEntry>();

        /// <summary>当前已入场的最大批次序号。</summary>
        private int _currentEntryBatchIndex;

        /// <summary>增援生成协程句柄（同时只允许一路）。</summary>
        private Coroutine _reinforcementRoutine;

        private readonly List<ModeHSpawnTransaction> _reinforcementTransactions =
            new List<ModeHSpawnTransaction>();
        private bool _reinforcementSpawnInFlight;
        private int _reinforcementReservedEnemyCount;
        private int _reinforcementOwnerVersion;

        /// <summary>
        /// 按 plan.enemyBatchIndices 把敌军拆成「开场批」与「后续批」。
        ///
        /// 旧写法把 plan.enemyStableKeys 整份一次交给一次 SpawnBatch、完全不看
        /// enemyBatchIndices，于是规划器注释里「总人数可以超过同时上限，超出部分
        /// 只能在前批减员后由生成事务放行」（ModeHEncounterPlanner.TryAssignBatches）
        /// 从未落地；更糟的是 _entryBatchIndex 开场即等于最后一批，
        /// ReinforcementPending（= _entryBatchIndex 小于 _lastEntryBatchIndex）因此恒 false，
        /// skill_saver_scar 这类「还有后续批次时给收益」的分量只剩代价。
        /// </summary>
        private bool SplitEnemyBatches(
            ModeHMatchPlanDto plan,
            List<CharacterRandomPreset> firstBatchPresets,
            List<string> firstBatchKeys,
            out string failureReasonId)
        {
            failureReasonId = null;
            ReleaseReinforcementRuntimeObjects();
            _currentEntryBatchIndex = 0;

            for (int i = 0; i < plan.enemyStableKeys.Count; i++)
            {
                string key = plan.enemyStableKeys[i];
                CharacterRandomPreset preset = ModeHPresetRegistry.GetAuditedPreset(key);
                if (preset == null)
                {
                    failureReasonId = "enemy_preset_missing:" + key;
                    return false;
                }

                int batchIndex = ResolveEnemyBatchIndex(plan, i);
                if (batchIndex <= 0)
                {
                    firstBatchKeys.Add(key);
                    firstBatchPresets.Add(preset);
                }
                else
                {
                    ModeHPendingEnemyEntry entry = new ModeHPendingEnemyEntry();
                    entry.BatchIndex = batchIndex;
                    entry.StableKey = key;
                    _pendingEnemyBatchKeys.Add(entry);
                }
            }

            if (firstBatchPresets.Count == 0)
            {
                // 规划器保证第 0 批非空；真出现说明计划数据坏了，按技术故障处理
                failureReasonId = "spawn_enemy_first_batch_empty";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 整批加上当前活敌与在途预留均不超限时才放行，不改变计划的整批入场语义。
        /// </summary>
        private void TryReleaseNextEnemyBatch()
        {
            if (_pendingEnemyBatchKeys.Count == 0) return;
            if (_reinforcementSpawnInFlight || _reinforcementRoutine != null) return;
            if (_combatTelemetry == null || _combatControl == null) return;
            if (_combatTelemetry.HasResult) return;
            if (_runState == null || _runState.Lifecycle != ModeHLifecycle.MatchFighting) return;

            ModeHMatchCorridor corridor = ModeHEncounterPlanner.GetCorridor(_runState.MatchIndex);
            int cap = corridor != null && corridor.SimultaneousCap > 0
                ? Math.Min(corridor.SimultaneousCap, ModeHConfig.MaxConcurrentEnemyInstances)
                : ModeHConfig.MaxConcurrentEnemyInstances;
            int nextBatch = int.MaxValue;
            int nextBatchCount = 0;
            for (int i = 0; i < _pendingEnemyBatchKeys.Count; i++)
            {
                int batch = _pendingEnemyBatchKeys[i].BatchIndex;
                if (batch < nextBatch) { nextBatch = batch; nextBatchCount = 0; }
                if (batch == nextBatch) nextBatchCount++;
            }
            if (nextBatchCount > cap)
            {
                RequestTechnicalRetry("reinforcement_batch_exceeds_cap");
                return;
            }
            if (nextBatchCount > cap - _combatTelemetry.LiveEnemyCount
                    - _reinforcementReservedEnemyCount) return;

            if (_owner == null) return;
            _reinforcementSpawnInFlight = true;
            _reinforcementReservedEnemyCount = nextBatchCount;
            int version = _reinforcementOwnerVersion;
            try
            {
                Coroutine routine = _owner.StartCoroutine(DriveReinforcementSpawn(version));
                // StartCoroutine 会同步推进到首个 yield；同步失败后不得把旧句柄重新写回来。
                if (version == _reinforcementOwnerVersion && _reinforcementSpawnInFlight)
                    _reinforcementRoutine = routine;
            }
            catch (Exception e)
            {
                if (version != _reinforcementOwnerVersion) return;
                _reinforcementSpawnInFlight = false;
                _reinforcementReservedEnemyCount = 0;
                RequestTechnicalRetry("reinforcement_start_exception:" + e.GetType().Name);
            }
        }

        /// <summary>
        /// 每批事务创建后立即归本场所有，提交成功后仍持有到统一收尾。
        /// 失败请求技术重试；取消、异常与手工驱动的子迭代器都从 finally 释放。
        /// </summary>
        private IEnumerator DriveReinforcementSpawn(int version)
        {
            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            int matchIndex = _runState.MatchIndex;
            ModeHCombatControl control = _combatControl;
            ModeHSpawnTransaction tx = new ModeHSpawnTransaction();
            _reinforcementTransactions.Add(tx);
            IEnumerator batch = null;
            ModeHSpawnBatchResult result = new ModeHSpawnBatchResult();
            string failureReasonId = null;
            int nextBatch = int.MaxValue;
            bool completed = false;
            try
            {
                try
                {
                    for (int i = 0; i < _pendingEnemyBatchKeys.Count; i++)
                        nextBatch = Math.Min(nextBatch, _pendingEnemyBatchKeys[i].BatchIndex);
                    List<CharacterRandomPreset> presets = new List<CharacterRandomPreset>();
                    List<string> keys = new List<string>();
                    for (int i = _pendingEnemyBatchKeys.Count - 1; i >= 0; i--)
                    {
                        ModeHPendingEnemyEntry entry = _pendingEnemyBatchKeys[i];
                        if (entry.BatchIndex != nextBatch) continue;
                        CharacterRandomPreset preset = ModeHPresetRegistry.GetAuditedPreset(entry.StableKey);
                        if (preset == null)
                        {
                            failureReasonId = "reinforcement_preset_missing:" + entry.StableKey;
                            break;
                        }
                        keys.Add(entry.StableKey);
                        presets.Add(preset);
                    }
                    if (failureReasonId == null && presets.Count == 0)
                        failureReasonId = "reinforcement_batch_empty";
                    if (failureReasonId == null && tx.Begin(_map, generation, ownerToken, out failureReasonId))
                    {
                        for (int i = _pendingEnemyBatchKeys.Count - 1; i >= 0; i--)
                            if (_pendingEnemyBatchKeys[i].BatchIndex == nextBatch)
                                _pendingEnemyBatchKeys.RemoveAt(i);
                        batch = tx.SpawnBatch(presets, keys, Teams.wolf, false,
                            new ModeHSpawnDiagnostics(), result);
                    }
                }
                catch (Exception e) { failureReasonId = "reinforcement_prepare_exception:" + e.GetType().Name; }
                if (batch == null)
                {
                    if (failureReasonId == null) failureReasonId = "reinforcement_tx_begin_failed";
                    yield break;
                }

                while (IsReinforcementOwnerCurrent(version, ownerToken, generation, matchIndex, control))
                {
                    bool moved = false;
                    object current = null;
                    try { moved = batch.MoveNext(); if (moved) current = batch.Current; }
                    catch (Exception e) { failureReasonId = "reinforcement_spawn_exception:" + e.GetType().Name; }
                    if (!moved || failureReasonId != null) break;
                    yield return current;
                }
                if (!IsReinforcementOwnerCurrent(version, ownerToken, generation, matchIndex, control)) yield break;
                if (failureReasonId != null) yield break;
                if (!result.Success)
                {
                    failureReasonId = result.FailureReasonId ?? "reinforcement_spawn_failed";
                    yield break;
                }

                try
                {
                    if (!tx.TryCommit(_map.ArenaSpawnPoints, _map.PlayerSpawnPos, out failureReasonId))
                    {
                        if (failureReasonId == null) failureReasonId = "reinforcement_commit_failed";
                    }
                    else if (IsReinforcementOwnerCurrent(version, ownerToken, generation, matchIndex, control))
                    {
                        for (int i = 0; i < tx.EnemyHandles.Count; i++)
                        {
                            ModeHSpawnHandle handle = tx.EnemyHandles[i];
                            ModeHParticipantRef enemy = BuildParticipant(handle, null, true, -1, false);
                            enemy.BatchIndex = nextBatch;
                            _enemyParticipants.Add(enemy);
                            RegisterParticipant(handle, enemy);
                            control.OnEnemyEntered(enemy);
                        }
                        _currentEntryBatchIndex = nextBatch;
                        RefreshBattleSnapshotContext();
                        control.OnEnemyBatchEntered(nextBatch, _battleSnapshotContext);
                        AttachAndPersistBattleSnapshot("reinforcement_batch_entered");
                        completed = true;
                    }
                }
                catch (Exception e) { failureReasonId = "reinforcement_commit_exception:" + e.GetType().Name; }
            }
            finally
            {
                try { IDisposable disposable = batch as IDisposable; if (disposable != null) disposable.Dispose(); }
                catch (Exception e) { LogFailure("reinforcement_batch_dispose", e); }
                if (!completed)
                {
                    try { tx.Cancel(); }
                    catch (Exception e) { LogFailure("reinforcement_tx_cancel", e); }
                    _reinforcementTransactions.Remove(tx);
                }
                // 同图下一场可能仍有相同 run owner/generation；旧 finally 不能清新场句柄或请求重试。
                if (version == _reinforcementOwnerVersion)
                {
                    _reinforcementRoutine = null;
                    _reinforcementSpawnInFlight = false;
                    _reinforcementReservedEnemyCount = 0;
                    if (failureReasonId != null && IsReinforcementOwnerCurrent(
                            version, ownerToken, generation, matchIndex, control))
                        RequestTechnicalRetry(failureReasonId);
                }
            }
        }

        private bool IsReinforcementOwnerCurrent(int version, long ownerToken, int generation,
            int matchIndex, ModeHCombatControl control)
        {
            return version == _reinforcementOwnerVersion && IsCallbackStillValid(ownerToken, generation)
                && _runState != null && _runState.MatchIndex == matchIndex
                && ReferenceEquals(control, _combatControl)
                && (_runState.Lifecycle == ModeHLifecycle.MatchFighting
                    || _runState.Lifecycle == ModeHLifecycle.RelayPending);
        }

        private void ReleaseReinforcementRuntimeObjects()
        {
            _reinforcementOwnerVersion++;
            Coroutine routine = _reinforcementRoutine;
            _reinforcementRoutine = null;
            _reinforcementSpawnInFlight = false;
            _reinforcementReservedEnemyCount = 0;
            _pendingEnemyBatchKeys.Clear();
            try { if (routine != null && _owner != null) _owner.StopCoroutine(routine); }
            catch (Exception e) { LogFailure("reinforcement_coroutine_stop", e); }
            for (int i = _reinforcementTransactions.Count - 1; i >= 0; i--)
            {
                try { _reinforcementTransactions[i].Cancel(); }
                catch (Exception e) { LogFailure("reinforcement_tx_release", e); }
            }
            _reinforcementTransactions.Clear();
        }

        #endregion
    }
}

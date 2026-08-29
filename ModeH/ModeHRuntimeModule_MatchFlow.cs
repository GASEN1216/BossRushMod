// ============================================================================
// ModeHRuntimeModule_MatchFlow.cs - Mode H 赛季主循环（设计提案 §17.2-§17.5、§18.2）
// ============================================================================
// 补上四个 partial 接入点中的 OnUpdateInternal，并承载从选秀到锁盘的全部命令处理器
// 与各页面的内容组装。
//
// 页面纪律（§25.3）：页面自身不读全局状态；这里组装内容、绑定回调，
// 每个按钮都在点击时重新校验 lifecycle，避免玩家用一个过期页面推进状态。
//
// 每帧纪律（ModeHPerformanceGuard）：OnUpdateInternal 里不得出现 FindObjectsOfType、
// LINQ 或任何分配；租约完整性巡检按秒节流，不每帧全场扫。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        #region 每帧驱动

        partial void OnUpdateInternal(float deltaTime, float unscaledDeltaTime)
        {
            if (_commandsClosed) return;
            if (_runState == null) return;

            // 租约完整性：只查已登记的 spawner 与晚到实例，按秒节流（§19.2 明令不得每帧全场扫）
            _leaseCheckAccumulator += deltaTime;
            if (_leaseCheckAccumulator >= LeaseCheckIntervalSeconds)
            {
                _leaseCheckAccumulator = 0f;
                TickLeaseIntegrity();
            }
        }

        /// <summary>租约巡检间隔。设计只要求「不每帧扫」，1 秒足够发现晚到 spawner。</summary>
        private const float LeaseCheckIntervalSeconds = 1f;

        private void TickLeaseIntegrity()
        {
            if (_arenaLease == null || !_arenaLease.IsActive) return;
            try
            {
                string failureReasonId;
                if (!_arenaLease.CheckStillIsolated(_sceneGeneration, out failureReasonId))
                {
                    RequestExit(ModeHExitReason.SceneGenerationMismatch,
                        failureReasonId != null ? failureReasonId : "arena_isolation_lost");
                    return;
                }
                if (!_arenaLease.CheckLateSpawners(out failureReasonId))
                {
                    // 晚到 spawner 属技术故障，不判负：按 §17.4 走同场重开路径
                    RequestRecovering(failureReasonId != null ? failureReasonId : "late_spawner");
                }
            }
            catch (Exception e)
            {
                LogFailure("lease_integrity", e);
            }
        }

        #endregion

        #region 战斗运行期清理

        /// <summary>
        /// 回收本场战斗的运行期对象。由 ReleaseRuntimeObjects 调用，
        /// 对应 §18.3 的第 3-5 步（取消战斗计时、恢复 adapter、回收临时角色与 kit）。
        /// 首发尚未接入 CombatControl 实例，这里保持幂等空实现的形态，
        /// 后续接线时只在本方法内补，不改 ReleaseRuntimeObjects 的顺序。
        /// </summary>
        private void ReleaseMatchRuntime()
        {
            // 2. 停生成队列
            try
            {
                if (_spawnRoutine != null && _owner != null) _owner.StopCoroutine(_spawnRoutine);
            }
            catch (Exception) { /* 协程已结束 */ }
            _spawnRoutine = null;

            try
            {
                if (_spawnTransaction != null) _spawnTransaction.RollbackAll();
            }
            catch (Exception e)
            {
                LogFailure("release_spawn_tx", e);
            }
            _spawnTransaction = null;

            // 3-5. 取消战斗计时、恢复 adapter、回收临时角色与 kit
            try
            {
                ModeHEventRouter.ClearMatchRegistry();
                ModeHEventRouter.Unbind();
            }
            catch (Exception e)
            {
                LogFailure("release_match_runtime", e);
            }
        }

        /// <summary>观战 HUD 的拍铃按钮。战斗未接入前只记录，不改任何事实。</summary>
        private void OnBellPressed()
        {
            if (_commandsClosed || _runState == null) return;
            ModBehaviour.DevLog("[ModeH] 拍铃请求 lifecycle=" + _runState.Lifecycle);
        }

        #endregion

        #region 选秀（Drafting -> RosterLocked）

        private ModeHPageContent BuildDraftPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Entry");
            // §22.1：真实押品没有开关，进入模式即知情同意，入口页必须固定披露风险
            page.ShowRealStakeNotice = true;

            if (_season == null) return page;

            EnsureDraftCandidates();

            List<ModeHProfileDto> profiles = _season.profiles;
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    ModeHProfileDto profile = profiles[i];
                    if (profile == null) continue;
                    page.Cards.Add(BuildProfileCard(profile));
                }
            }

            page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Summary_Draft");
            return page;
        }

        /// <summary>五席试棚只生成一次：关页重开不得重抽（§17.2）。</summary>
        private void EnsureDraftCandidates()
        {
            if (_season == null || _runState == null) return;
            if (_season.draftCandidateProfileIds != null
                && _season.draftCandidateProfileIds.Count > 0)
            {
                return;
            }

            try
            {
                List<ModeHProfileDto> candidates;
                string failureReasonId;
                if (!ModeHDraftController.TryBuildDraft(
                        _runState.RunSeed, ModeHProfileRegistry.ProductionCatalog,
                        out candidates, out failureReasonId))
                {
                    ModBehaviour.DevLog("[ModeH] 试棚生成失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    RequestRecovering(failureReasonId != null ? failureReasonId : "draft_failed");
                    return;
                }

                _season.profiles = candidates;
                _season.draftCandidateProfileIds = new List<string>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] != null) _season.draftCandidateProfileIds.Add(candidates[i].profileId);
                }
                TryPersistSeason("draft_candidates");
            }
            catch (Exception e)
            {
                LogFailure("build_draft", e);
            }
        }

        private ModeHCardData BuildProfileCard(ModeHProfileDto profile)
        {
            ModeHCardData card = new ModeHCardData();
            card.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Fighter_" + profile.profileId);
            card.Subtitle = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Archetype_" + profile.archetypeId);
            card.ActionLabel = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Sign");

            string signedId = profile.profileId;
            card.OnClick = delegate { OnDraftPick(signedId); };
            return card;
        }

        /// <summary>
        /// 选秀点击：先主将后替补，签满两人即锁定名单。
        /// 每次点击都重新校验 lifecycle，避免过期页面推进状态。
        /// </summary>
        private void OnDraftPick(string profileId)
        {
            if (_commandsClosed || _season == null || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.Drafting) return;

            try
            {
                if (_pendingContractMainId == null)
                {
                    _pendingContractMainId = profileId;
                    RouteUiForLifecycle(_runState.Lifecycle);
                    return;
                }
                if (string.Equals(_pendingContractMainId, profileId, StringComparison.Ordinal)) return;

                ModeHContractDto contract;
                string failureReasonId;
                if (!ModeHDraftController.TrySignContracts(
                        _season.profiles, _pendingContractMainId, profileId,
                        out contract, out failureReasonId))
                {
                    ModBehaviour.DevLog("[ModeH] 签约失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    return;
                }

                List<ModeHEchoAssignmentDto> assignments;
                if (!ModeHDraftController.TryAssignEchoDestinations(
                        _runState.RunSeed, _season.profiles, contract,
                        out assignments, out failureReasonId))
                {
                    ModBehaviour.DevLog("[ModeH] 落选分流失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    return;
                }

                _season.contract = contract;
                _season.echoAssignments = assignments;
                _pendingContractMainId = null;

                if (TryTransition(ModeHLifecycle.Drafting, ModeHLifecycle.RosterLocked, "contracts_signed"))
                {
                    // 早期恢复子表要求 roster 快照有效，这里是一个显式落盘点
                    TryPersistSeason("roster_locked");
                }
            }
            catch (Exception e)
            {
                LogFailure("draft_pick", e);
            }
        }

        /// <summary>已点选的主将（等待第二次点击选替补）。</summary>
        private string _pendingContractMainId;

        #endregion

        #region 看盘与赔率页（内容组装）

        private ModeHPageContent BuildBriefPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Brief");
            page.ShowRealStakeNotice = true;
            if (_season == null || _runState == null) return page;

            // matchIndex 是 1-based（0 表示尚未开赛），展示时不再 +1
            int displayIndex = _runState.MatchIndex > 0
                ? _runState.MatchIndex
                : ModeHConfig.FirstMatchIndex;
            page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                + " " + displayIndex + " / " + ModeHConfig.SeasonMatchCount;

            // RosterLocked 只是签完约，还要先把本场敌军计划建出来才能看盘
            if (_runState.Lifecycle == ModeHLifecycle.RosterLocked)
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    OnClick = delegate { OpenFirstMatchBrief(); },
                });
                return page;
            }

            EnsureMatchPlan();
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_LockIn"),
                Interactable = _season.currentMatchPlan != null,
                OnClick = delegate { EnterLoadoutEditing(); },
            });
            return page;
        }

        /// <summary>
        /// RosterLocked -> 第一场 MatchBrief。
        /// matchIndex 是 1-based（构造时为 0 = 尚未开赛），这里推进到 FirstMatchIndex；
        /// 后续场次的推进在 SeasonFlow.OpenNextMatchBrief，两处合起来仍是「只有真正
        /// 打开下一场看盘时才推进一格」（§18.2）。
        /// </summary>
        private void OpenFirstMatchBrief()
        {
            if (_runState == null) return;
            if (_runState.MatchIndex < ModeHConfig.FirstMatchIndex)
            {
                string failureReasonId;
                if (!_runState.TryAdvanceMatchIndex(out failureReasonId))
                {
                    RequestRecovering(failureReasonId != null ? failureReasonId : "match_index_failed");
                    return;
                }
            }
            if (TryTransition(ModeHLifecycle.RosterLocked, ModeHLifecycle.MatchBrief, "roster_confirmed"))
            {
                EnsureMatchPlan();
                TryPersistSeason("first_match_brief");
            }
        }

        /// <summary>
        /// 本场敌军计划只建一次：关页重开不得重抽（同 §17.2 的试棚纪律）。
        /// 计划生成失败属技术故障，走恢复而不是判负。
        /// </summary>
        private void EnsureMatchPlan()
        {
            if (_season == null || _runState == null) return;
            if (_season.currentMatchPlan != null
                && _season.currentMatchPlan.matchIndex == _runState.MatchIndex)
            {
                return;
            }

            try
            {
                ModeHMatchPlanDto plan;
                int usedCandidateIndex;
                string failureReasonId;
                if (!ModeHEncounterPlanner.TryBuildPlan(
                        _runState.RunSeed,
                        _runState.MatchIndex,
                        _runState.TechnicalRetrySequence,
                        ModeHPresetRegistry.ProductionKeys,
                        ResolveEchoReturnStableKey(),
                        ModeHTransferMarket.GetLiveContractProfileIds(_season),
                        out plan, out usedCandidateIndex, out failureReasonId))
                {
                    ModBehaviour.DevLog("[ModeH] 敌军计划生成失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    RequestRecovering(failureReasonId != null ? failureReasonId : "plan_failed");
                    return;
                }

                _season.currentMatchPlan = plan;
                TryPersistSeason("match_plan");
            }
            catch (Exception e)
            {
                LogFailure("build_plan", e);
                RequestRecovering("plan_exception");
            }
        }

        /// <summary>
        /// 第 5 场的回响返场 key（落选选手以敌军身份回来打你）；其余场次为空。
        /// assignments 只记 profileId，stableKey 要回 profiles 里查。
        /// </summary>
        private string ResolveEchoReturnStableKey()
        {
            if (_season == null || _runState == null) return null;
            if (_runState.MatchIndex != ModeHConfig.EchoReturnMatchIndex) return null;

            List<ModeHEchoAssignmentDto> assignments = _season.echoAssignments;
            List<ModeHProfileDto> profiles = _season.profiles;
            if (assignments == null || profiles == null) return null;

            for (int i = 0; i < assignments.Count; i++)
            {
                ModeHEchoAssignmentDto a = assignments[i];
                if (a == null || a.resolved) continue;
                if (!string.Equals(a.destinationId, "return_enemy", StringComparison.Ordinal)) continue;

                for (int j = 0; j < profiles.Count; j++)
                {
                    ModeHProfileDto p = profiles[j];
                    if (p == null) continue;
                    if (string.Equals(p.profileId, a.profileId, StringComparison.Ordinal))
                    {
                        return p.stableKey;
                    }
                }
            }
            return null;
        }

        private void EnterLoadoutEditing()
        {
            if (_runState == null) return;
            TryTransition(ModeHLifecycle.MatchBrief, ModeHLifecycle.LoadoutEditing, "open_loadout");
        }

        private ModeHPageContent BuildOddsPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Odds");
            page.ShowRealStakeNotice = true;

            // 押品选择器：真实资产路径尚未接线，按 §22.1 的只读派生结果显示禁用原因，
            // 绝不做成可写开关（ModeHConfigApiGuard 禁止任何 RealWarehouseStake 开关符号）。
            page.RealStakeSelectorEnabled = false;
            page.RealStakeDisabledReason =
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Disabled");

            if (_season == null || _runState == null) return page;

            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_LockIn"),
                Interactable = _season.currentMatchPlan != null && _season.contract != null,
                OnClick = LockLoadoutAndStartMatch,
            });
            return page;
        }

        #endregion

        #region 锁盘与开打

        /// <summary>
        /// 锁盘后立刻进入生成阶段。
        /// 主干走 LoadoutEditing/OddsPreview -> LoadoutLocked -> MatchSpawning，
        /// **不经过 StakePrepared**——那是真实押品支路（ModeHStateMachineGuard 冻结这一点）。
        /// </summary>
        private void LockLoadoutAndStartMatch()
        {
            if (_commandsClosed || _season == null || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.LoadoutEditing
                && _runState.Lifecycle != ModeHLifecycle.OddsPreview)
            {
                return;
            }

            try
            {
                if (!TryTransition(_runState.Lifecycle, ModeHLifecycle.LoadoutLocked, "loadout_locked"))
                {
                    return;
                }
                // 开战前的最后一个显式落盘点：技术中止要按它回到同一场
                TryPersistSeason("loadout_locked");
                StartMatchSpawning();
            }
            catch (Exception e)
            {
                LogFailure("lock_loadout", e);
                RequestRecovering("lock_loadout_exception");
            }
        }

        private void StartMatchSpawning()
        {
            if (_owner == null || _map == null || _runState == null) return;
            if (!TryTransition(ModeHLifecycle.LoadoutLocked, ModeHLifecycle.MatchSpawning, "spawn_begin"))
            {
                return;
            }
            _spawnRoutine = _owner.StartCoroutine(DriveMatchSpawning());
        }

        /// <summary>
        /// 分帧生成本场敌军与我方选手，提交后交给 CombatControl。
        /// 任何一步失败都走「技术中止 + 同场重开」，**绝不判负**（§17.4）。
        /// </summary>
        private System.Collections.IEnumerator DriveMatchSpawning()
        {
            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            string failureReasonId = null;
            bool ok = false;

            ModeHMatchPlanDto plan = _season != null ? _season.currentMatchPlan : null;
            List<CharacterRandomPreset> enemyPresets = new List<CharacterRandomPreset>();
            List<string> enemyKeys = new List<string>();

            if (plan == null || plan.enemyStableKeys == null || plan.enemyStableKeys.Count == 0)
            {
                AbortMatchSpawning("plan_missing");
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
                enemyPresets.Add(preset);
                enemyKeys.Add(key);
            }

            _spawnTransaction = new ModeHSpawnTransaction();
            if (!_spawnTransaction.Begin(_map, generation, ownerToken, out failureReasonId))
            {
                AbortMatchSpawning(failureReasonId != null ? failureReasonId : "spawn_tx_begin_failed");
                yield break;
            }

            ModeHSpawnBatchResult result = new ModeHSpawnBatchResult();
            ModeHSpawnDiagnostics diagnostics = new ModeHSpawnDiagnostics();

            System.Collections.IEnumerator batch = _spawnTransaction.SpawnBatch(
                enemyPresets, enemyKeys, Teams.wolf, false, diagnostics, result);
            while (batch.MoveNext())
            {
                if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                yield return null;
            }
            ok = result.Success;
            if (!ok)
            {
                AbortMatchSpawning(result.FailureReasonId != null
                    ? result.FailureReasonId : "enemy_spawn_failed");
                yield break;
            }

            if (!IsCallbackStillValid(ownerToken, generation)) yield break;

            _spawnRoutine = null;

            // 本批交付到「敌军已按计划就位」为止；战斗驱动（口令窗口、接力、结算）尚未接线。
            // 干净地回滚并退回看盘：**不**消耗同场重试预算、**不**挂起赛季——
            // 那两者是给真实技术故障准备的，用在「功能没写完」上会把玩家的赛季推进死路。
            try
            {
                if (_spawnTransaction != null) _spawnTransaction.RollbackAll();
            }
            catch (Exception e)
            {
                LogFailure("spawn_rollback_pending", e);
            }
            _spawnTransaction = null;

            if (!IsCallbackStillValid(ownerToken, generation)) yield break;

            ModBehaviour.DevLog("[ModeH] 生成校验通过（敌军 " + enemyKeys.Count
                + " 名已就位并回滚）；战斗驱动尚未接线，退回看盘");
            if (_owner != null)
            {
                _owner.ShowMessage(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Match_NotWiredYet"));
            }
            TryTransition(_runState.Lifecycle, ModeHLifecycle.MatchBrief, "combat_wiring_pending");
        }

        /// <summary>
        /// 生成阶段失败的统一出口：回滚整批、按同场重开计数，超过上限则挂起。
        /// 绝不判负——技术故障不是玩家的锅（§17.4）。
        /// </summary>
        private void AbortMatchSpawning(string reasonId)
        {
            _spawnRoutine = null;
            try
            {
                if (_spawnTransaction != null) _spawnTransaction.RollbackAll();
            }
            catch (Exception e)
            {
                LogFailure("spawn_rollback", e);
            }
            _spawnTransaction = null;

            if (_runState == null) return;
            int retries = _runState.IncrementTechnicalRetry();
            ModBehaviour.DevLog("[ModeH] 生成中止 (" + (reasonId ?? "unknown")
                + ") retry=" + retries);

            if (retries > ModeHConfig.MaxAutomaticTechnicalRetriesPerMatch)
            {
                RequestSuspended(reasonId != null ? reasonId : "spawn_retry_exhausted");
                return;
            }
            RequestRecovering(reasonId != null ? reasonId : "spawn_failed");
        }

        /// <summary>本场生成协程句柄，关停时取消。</summary>
        private Coroutine _spawnRoutine;

        /// <summary>本场生成事务。</summary>
        private ModeHSpawnTransaction _spawnTransaction;

        private ModeHPageContent BuildSettlementPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Settlement");
            return page;
        }

        #endregion
    }
}

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

            // Recovering 是过渡态不是终点：进来之后必须有人把它推回同一场，
            // 否则技术故障出口全部通向一个没有按钮的壳（CR-2026-08-29-010）。
            // 放在这里而不是 RequestRecovering 内同步推进，是因为 EnsureMatchPlan 会在
            // 页面组装（RouteUiForLifecycle）里报故障，同步回落会变成无限递归。
            if (_runState.Lifecycle == ModeHLifecycle.Recovering)
            {
                DriveRecovery();
                return;
            }

            if (_runState.Lifecycle == ModeHLifecycle.MatchFighting
                || _runState.Lifecycle == ModeHLifecycle.RelayPending)
            {
                TickActiveCombat(deltaTime);
            }

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
                    // 晚到 spawner 属技术故障，不判负：按 §17.4 消耗一次同场重试预算后回落
                    RequestTechnicalRetry(failureReasonId != null ? failureReasonId : "late_spawner");
                }
            }
            catch (Exception e)
            {
                LogFailure("lease_integrity", e);
            }
        }

        #endregion

        #region 恢复驱动（Recovering -> 同场）

        /// <summary>上一次驱动恢复时的状态序号，用于每次进入 Recovering 只驱动一次。</summary>
        private int _recoveryDriveStateSequence = -1;

        /// <summary>
        /// 把 Recovering 推回可玩状态。每进入一次 Recovering 只尝试一次
        /// （按 stateSequence 判重），避免失败时每帧重试刷日志。
        /// </summary>
        private void DriveRecovery()
        {
            if (_runState == null) return;
            if (_runState.StateSequence == _recoveryDriveStateSequence) return;
            _recoveryDriveStateSequence = _runState.StateSequence;

            ModeHLifecycle resume = ResolveRecoveryResumeLifecycle();
            if (resume == ModeHLifecycle.Unknown)
            {
                // 开局阶段（EntryIntent/SceneLoading/ProductionCertifying）没有可回落的同场，
                // 挂起把处置权交回玩家，好过停在死态
                RequestSuspended("recovery_target_unresolved");
                return;
            }

            // 自动重试预算（§17.4）。故障点已经各自消耗过预算，这里是防御性兜底。
            if (_runState.TechnicalRetrySequence > ModeHConfig.MaxAutomaticTechnicalRetriesPerMatch)
            {
                RequestSuspended("recovery_retry_exhausted");
                return;
            }

            if (!TryTransition(ModeHLifecycle.Recovering, resume, "recovery_resume"))
            {
                RequestSuspended("recovery_resume_rejected");
            }
        }

        /// <summary>
        /// 恢复目标：战前/战中的任何故障一律回落到**同一场**看盘（§20.3），
        /// 早期状态与幕间状态回落到自己（冻结表与早期恢复子表都允许）。
        /// </summary>
        private ModeHLifecycle ResolveRecoveryResumeLifecycle()
        {
            if (_runState == null) return ModeHLifecycle.Unknown;

            ModeHLifecycle origin = _runState.RecoveryResumeTarget;
            if (origin == ModeHLifecycle.Unknown) origin = _runState.RecoveryOriginalLifecycle;

            switch (origin)
            {
                case ModeHLifecycle.Drafting:
                case ModeHLifecycle.RosterLocked:
                case ModeHLifecycle.Intermission:
                case ModeHLifecycle.TransferWindow:
                case ModeHLifecycle.HallOfFame:
                    return origin;

                case ModeHLifecycle.MatchBrief:
                case ModeHLifecycle.LoadoutEditing:
                case ModeHLifecycle.OddsPreview:
                case ModeHLifecycle.LoadoutLocked:
                case ModeHLifecycle.StakePrepared:
                case ModeHLifecycle.MatchSpawning:
                case ModeHLifecycle.MatchFighting:
                case ModeHLifecycle.RelayPending:
                case ModeHLifecycle.MatchSettling:
                    return ModeHLifecycle.MatchBrief;

                case ModeHLifecycle.Suspended:
                case ModeHLifecycle.Unknown:
                    // 玩家从挂起点「同场重开」时，ApplyTransition 会把故障源**覆盖成 Suspended**
                    // （Suspended -> Recovering 这一跳满足它重置恢复元数据的条件），
                    // 真实故障源已经丢失。从赛季进度反推才是可靠的，
                    // 否则重开按钮会解析不出目标又弹回挂起。
                    return DeriveResumeFromSeasonProgress();

                default:
                    // EntryIntent / SceneLoading / ProductionCertifying：开局阶段没有可回落的同场，
                    // 这些状态的失败本来就走 AbortSetup 退款离场
                    return ModeHLifecycle.Unknown;
            }
        }

        /// <summary>按赛季实际进度反推可回落的状态：已开赛回同场看盘，签完约回名单，否则回选秀。</summary>
        private ModeHLifecycle DeriveResumeFromSeasonProgress()
        {
            if (_runState == null || _season == null) return ModeHLifecycle.Unknown;
            if (FindLatestPendingReport() != null) return ModeHLifecycle.Intermission;
            if (_runState.MatchIndex >= ModeHConfig.FirstMatchIndex) return ModeHLifecycle.MatchBrief;
            if (_season.contract != null) return ModeHLifecycle.RosterLocked;
            return ModeHLifecycle.Drafting;
        }

        /// <summary>
        /// 技术故障统一入口：消耗一次同场重试预算，未超预算转 Recovering
        /// （由 DriveRecovery 回落到同一场），超预算则挂起。绝不判负（§17.4）。
        /// </summary>
        private void RequestTechnicalRetry(string reasonId)
        {
            if (_runState == null) return;
            ModeHLifecycle origin = _runState.Lifecycle;
            bool committedCombatFact = origin == ModeHLifecycle.MatchFighting
                || origin == ModeHLifecycle.RelayPending
                || origin == ModeHLifecycle.MatchSettling;
            bool ownsMatchRuntime = committedCombatFact
                || origin == ModeHLifecycle.MatchSpawning;
            if (ownsMatchRuntime)
            {
                ReleaseCombatRuntimeObjects();
                RestoreMatchReservationAndSnapshot();
            }
            int retries = _runState.IncrementTechnicalRetry();
            ModBehaviour.DevLog("[ModeH] 技术故障 (" + (reasonId ?? "unknown") + ") retry=" + retries);
            if (retries > ModeHConfig.MaxAutomaticTechnicalRetriesPerMatch)
            {
                RequestSuspended(reasonId != null ? reasonId : "technical_retry_exhausted");
                return;
            }
            if (committedCombatFact)
            {
                RequestErrorRecoveryPending(reasonId != null ? reasonId : "technical_fault");
                if (_runState.Lifecycle != ModeHLifecycle.ErrorRecoveryPending
                    || !TryTransition(ModeHLifecycle.ErrorRecoveryPending, ModeHLifecycle.Recovering,
                        "recovery_barrier_complete"))
                {
                    RequestSuspended("recovery_barrier_rejected");
                    return;
                }
            }
            else
            {
                RequestRecovering(reasonId != null ? reasonId : "technical_fault");
            }
            TryPersistSeason("technical_retry_reset");
        }

        #endregion

        #region 战斗运行期清理

        /// <summary>
        /// 回收本场战斗的运行期对象。由 ReleaseRuntimeObjects 调用，
        /// 对应 §18.3 的第 3-5 步（取消战斗计时、恢复 adapter、回收临时角色与 kit）。
        /// 战斗控制、adapter、事件路由、临时角色与 kit 都在这里按逆序幂等释放。
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
            ReleaseCombatRuntimeObjects();
        }

        /// <summary>观战 HUD 的拍铃按钮。每场至多一次，直接交给战斗控制器。</summary>
        private void OnBellPressed()
        {
            if (_commandsClosed || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.MatchFighting || _combatControl == null) return;
            RefreshBattleSnapshotContext();
            string failureReasonId;
            if (!_combatControl.TryRingBell(_battleSnapshotContext, out failureReasonId))
            {
                ModBehaviour.DevLog("[ModeH] 拍铃被拒绝: "
                    + (failureReasonId != null ? failureReasonId : "unknown"));
                return;
            }
            AttachAndPersistBattleSnapshot("bell_committed");
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
                    // 本方法由页面组装调用：必须消耗重试预算，否则「回落 -> 重建页面 -> 再失败」
                    // 会变成每帧一次的死循环
                    RequestTechnicalRetry(failureReasonId != null ? failureReasonId : "draft_failed");
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
            // Label_Match 是「第 {0} 场」这样的模板，不是纯前缀：直接拼接会把 "{0}" 原样显示给玩家
            page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                    .Replace("{0}", displayIndex.ToString())
                + " / " + ModeHConfig.SeasonMatchCount;

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
                    RequestTechnicalRetry(failureReasonId != null ? failureReasonId : "match_index_failed");
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
        ///
        /// 缓存判据是 (matchIndex, technicalRetrySequence) 两项：只比 matchIndex 会让技术重试
        /// 复用刚刚失败的同一份计划，与 §17.4「重试换下一个候选」相悖（CR-2026-08-29-018 第 4 项）。
        /// </summary>
        private void EnsureMatchPlan()
        {
            if (_season == null || _runState == null) return;
            if (_season.currentMatchPlan != null
                && _season.currentMatchPlan.matchIndex == _runState.MatchIndex
                && _season.currentMatchPlan.technicalRetrySequence == _runState.TechnicalRetrySequence)
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
                    // 同 EnsureDraftCandidates：本方法在页面组装里被调用，必须消耗重试预算
                    RequestTechnicalRetry(failureReasonId != null ? failureReasonId : "plan_failed");
                    return;
                }

                _season.currentMatchPlan = plan;
                TryPersistSeason("match_plan");
            }
            catch (Exception e)
            {
                LogFailure("build_plan", e);
                RequestTechnicalRetry("plan_exception");
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
            if (!TryTransition(ModeHLifecycle.MatchBrief, ModeHLifecycle.LoadoutEditing, "open_loadout"))
            {
                return;
            }
            // 同一个页面既是配装也是看赔率，开页即进入 OddsPreview：
            // §18.2 冻结表里锁盘只有 OddsPreview -> LoadoutLocked 一条边，
            // 停在 LoadoutEditing 会让锁盘按钮永远被拒（CR-2026-08-29-008）。
            EnsureOddsPreview();
        }

        /// <summary>
        /// 把 LoadoutEditing 推进到 OddsPreview —— 冻结表中锁盘的唯一合法前驱。
        /// 已在 OddsPreview 时是幂等真返回；不在这两个状态时返回 false，调用方不得继续锁盘。
        /// </summary>
        private bool EnsureOddsPreview()
        {
            if (_runState == null) return false;
            if (_runState.Lifecycle == ModeHLifecycle.OddsPreview) return true;
            if (_runState.Lifecycle != ModeHLifecycle.LoadoutEditing) return false;
            return TryTransition(ModeHLifecycle.LoadoutEditing, ModeHLifecycle.OddsPreview, "open_odds");
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

            string prepareFailure;
            if (!EnsurePreparedMatchSelection(out prepareFailure))
            {
                page.Body = L10n.T("本场整备不可用：", "Match setup unavailable: ")
                    + (prepareFailure != null ? prepareFailure : "unknown");
                return page;
            }

            page.Body = L10n.T("我方公开分 ", "Player public score ")
                + _currentOddsQuote.PlayerPublicScore
                + L10n.T("　敌方公开分 ", "  Enemy public score ")
                + _currentOddsQuote.EnemyPublicScore
                + L10n.T("　锁定赔率 x", "  Locked odds x") + _currentOddsQuote.Odds
                + L10n.T("\n虚拟筹码余额 ", "\nVirtual credits ") + _season.virtualStakeCredits
                + L10n.T("　当前下注 ", "  Current stake ") + _selectedVirtualStake;

            if (_currentOddsQuote.Breakdown != null)
            {
                for (int i = 0; i < _currentOddsQuote.Breakdown.Count; i++)
                {
                    ModeHOddsBreakdownEntry entry = _currentOddsQuote.Breakdown[i];
                    if (entry == null) continue;
                    page.Lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + entry.LabelKey)
                        + "  " + (entry.Value >= 0 ? "+" : string.Empty) + entry.Value);
                }
            }

            int maxStake = ModeHVirtualStakeController.GetMaxStake(_season.virtualStakeCredits);
            for (int stake = 0; stake <= maxStake; stake++)
            {
                int selected = stake;
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("下注 ", "Stake ") + selected,
                    Interactable = selected != _selectedVirtualStake,
                    OnClick = delegate { SelectVirtualStake(selected); },
                });
            }

            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_LockIn"),
                Interactable = _season.currentMatchPlan != null && _season.matchRoster != null
                    && _currentOddsQuote != null,
                OnClick = LockLoadoutAndStartMatch,
            });
            return page;
        }

        #endregion

        #region 锁盘与开打

        /// <summary>
        /// 锁盘后立刻进入生成阶段。
        /// 主干走 LoadoutEditing -> OddsPreview -> LoadoutLocked -> MatchSpawning，
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
                // 冻结表只有 OddsPreview -> LoadoutLocked 这一条锁盘边。
                // 正常路径开页时已经跳过去了，这里再补一次是为了任何进入 LoadoutEditing
                // 的其它路径也能锁盘，而不是让按钮静默失效。
                if (!EnsureOddsPreview()) return;
                string prepareFailure;
                if (!PrepareLockedMatch(out prepareFailure))
                {
                    ModBehaviour.DevLog("[ModeH] 锁盘准备失败: "
                        + (prepareFailure != null ? prepareFailure : "unknown"));
                    return;
                }
                if (!TryTransition(ModeHLifecycle.OddsPreview, ModeHLifecycle.LoadoutLocked, "loadout_locked"))
                {
                    RestoreMatchReservationAndSnapshot();
                    return;
                }
                // 开战前的最后一个显式落盘点：技术中止要按它回到同一场
                if (!TryPersistSeason("loadout_locked"))
                {
                    RestoreMatchReservationAndSnapshot();
                    RequestSuspended("loadout_persist_failed");
                    return;
                }
                StartMatchSpawning();
            }
            catch (Exception e)
            {
                LogFailure("lock_loadout", e);
                RequestTechnicalRetry("lock_loadout_exception");
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
            System.Collections.IEnumerator inner = DriveCompleteMatchSpawning();
            while (inner.MoveNext()) yield return inner.Current;
        }

        /// <summary>
        /// 生成阶段失败的统一出口：回滚整批、按同场重开计数，超过上限则挂起。
        /// 绝不判负——技术故障不是玩家的锅（§17.4）。
        /// </summary>
        private void AbortMatchSpawning(string reasonId)
        {
            _spawnRoutine = null;
            ReleaseCombatRuntimeObjects();
            try
            {
                if (_spawnTransaction != null) _spawnTransaction.RollbackAll();
            }
            catch (Exception e)
            {
                LogFailure("spawn_rollback", e);
            }
            _spawnTransaction = null;

            RestoreMatchReservationAndSnapshot();

            if (_runState == null) return;
            RequestTechnicalRetry(reasonId != null ? reasonId : "spawn_failed");
        }

        /// <summary>本场生成协程句柄，关停时取消。</summary>
        private Coroutine _spawnRoutine;

        /// <summary>本场生成事务。</summary>
        private ModeHSpawnTransaction _spawnTransaction;

        private ModeHPageContent BuildSettlementPageContent()
        {
            return BuildCompletedSettlementPageContent();
        }

        #endregion
    }
}

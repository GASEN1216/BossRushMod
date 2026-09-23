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
            if (_ui != null) _ui.ApplyHudVisibility(); // 观战 HUD 跟随官方界面与暂停收起，刷怪期也要（见 ModeHUI）
            if (_commandsClosed) return;
            if (_runState == null) return;
            if (_restoredSeasonPending || _resumeScenePending) return;

            // 观战镜头：开打期间对准当前登场选手，离开交战相位对回玩家身体。O(1)、零分配（见 ModeHSpectatorLease）。
            if (_spectatorLease != null)
            {
                _spectatorLease.SyncCameraTarget(
                    _activeFighterHandle != null ? _activeFighterHandle.Character : null,
                    IsCombatLifecycle(_runState.Lifecycle));
            }

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

            // 自动重试和恢复壳共用资产屏障；未结清的押品不得进入可重新锁盘的页面。
            if (resume == ModeHLifecycle.MatchBrief)
            {
                string assetFailure;
                if (!ModeHRealStakeService.TryPrepareTechnicalRetry(
                        _runState.RunId, _runState.RunSeed, _runState.MatchIndex, out assetFailure))
                {
                    RequestSuspended(assetFailure ?? "retry_assets_unresolved", false);
                    return;
                }
                // 同会话挂起后重试可能复用原租约，不经过续赛切图设置 reset 标志。
                if (_resumeNeedsMatchReset || (_season != null && _season.preMatchSnapshot != null))
                    RestoreMatchReservationAndSnapshot();
            }
            _resumeNeedsMatchReset = false;

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
                    return FindLatestPendingReport() != null
                        ? ModeHLifecycle.Intermission : ModeHLifecycle.MatchBrief;

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
            }
            // 已有完整战报时只恢复结算；绝不撤销结果或重复发奖。
            if (FindLatestPendingReport() == null)
            {
                string assetFailure;
                if (!ModeHRealStakeService.TryPrepareTechnicalRetry(
                        _runState.RunId, _runState.RunSeed, _runState.MatchIndex, out assetFailure))
                {
                    RequestSuspended(assetFailure ?? "retry_assets_unresolved", false);
                    return;
                }
                if (ownsMatchRuntime || (_season != null && _season.preMatchSnapshot != null))
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
            // 观战租约的拍铃门：ReleaseCombatRuntimeObjects 已经关门时不再受理。
            // 租约缺失不阻断（租约本来就允许取不到，那时按旧口径只靠上面两道门）。
            if (_spectatorLease != null && !_spectatorLease.IsBellAccepting) return;
            RefreshBattleSnapshotContext();
            string failureReasonId;
            if (!_combatControl.TryRingBell(_battleSnapshotContext, out failureReasonId))
            {
                ModBehaviour.DevLog("[ModeH] 拍铃被拒绝: "
                    + (failureReasonId != null ? failureReasonId : "unknown"));
                // 拍铃是整场比赛唯一的玩家干预手段且每场限一次，失败必须有可见反馈，
                // 否则玩家只看到"按钮没反应"。次数未被消耗，可以再次尝试。
                ShowBellFailureMessage(failureReasonId);
                return;
            }
            AttachAndPersistBattleSnapshot("bell_committed");
        }

        /// <summary>拍铃失败的玩家可见提示。提示失败不得影响比赛流程。</summary>
        private void ShowBellFailureMessage(string failureReasonId)
        {
            try
            {
                if (_owner == null) return;
                _owner.ShowMessage(L10n.T(ModeHCommandController.GetBellFailureLocalizationKey(failureReasonId)));
            }
            catch (Exception)
            {
                // 提示失败不得影响比赛流程
            }
        }

        #endregion

        #region 选秀（Drafting -> RosterLocked）

        /// <summary>
        /// 入口页 = 唯一的选人页（2026-09-23 owner：「只弄一个选择武将的页面，选完后就开始」）。
        /// 五张大卡，点「选他出战」即签下主将、自动配接力并一路开打（OnDraftPick → RunAutoAdvance）。
        /// </summary>
        private ModeHPageContent BuildDraftPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Entry");
            // §22.1：真实押品没有开关，进入模式即知情同意，入口页必须固定披露风险。
            // 选人页按默认值开打、从不押真实物品，所以披露放在页脚，不再占顶部红条。
            page.ShowRealStakeNotice = true;
            page.CompactRiskNotice = true;

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

        /// <summary>
        /// 侦察揭示的核心特质 ID -> 显示名。`coreTraitTags` 里混着底色（temperament）
        /// 与怪癖（quirk）两类 ID，前缀不同，按已冻结的 ID 表判定该用哪个。
        /// </summary>
        private static string ResolveTraitDisplayName(string traitId)
        {
            if (string.IsNullOrEmpty(traitId)) return string.Empty;
            string prefix = ModeHConfig.LocalizationKeyPrefix;
            for (int i = 0; i < ModeHStableIds.AllTemperaments.Length; i++)
            {
                if (string.Equals(ModeHStableIds.AllTemperaments[i], traitId, StringComparison.Ordinal))
                {
                    return L10n.T(prefix + "Temperament_" + traitId);
                }
            }
            return L10n.T(prefix + "Quirk_" + traitId);
        }

        /// <summary>
        /// 选人卡：图鉴立绘 + 名字 + 打法定位 + 两三句白话（`Fighter_&lt;id&gt;_Plain`）。
        /// 旧卡把「异常名 / 招牌口令名 / 传闻」三行术语拼在一起，玩家读不懂（2026-09-23 owner 实测）；
        /// 怪癖、异常与招牌口令的效果已经揉进白话里讲清楚。转会页复用同一张卡（去掉按钮）。
        /// 立绘键是 stableKey（= 官方 preset nameKey = 鸭皇图鉴条目键）。
        /// </summary>
        private ModeHCardData BuildProfileCard(ModeHProfileDto profile)
        {
            string prefix = ModeHConfig.LocalizationKeyPrefix;
            ModeHCardData card = new ModeHCardData();
            card.Title = L10n.T(prefix + "Fighter_" + profile.profileId);
            card.Subtitle = L10n.T(prefix + "Archetype_" + profile.archetypeId + "_Plain");
            card.ActionLabel = L10n.T(prefix + "Button_Sign");
            card.IsAnomaly = !string.IsNullOrEmpty(profile.anomalyId);
            card.PortraitKey = profile.stableKey;
            card.Body = ResolveFighterPlainDescription(profile);

            string signedId = profile.profileId;
            card.OnClick = delegate { OnDraftPick(signedId); };
            return card;
        }

        /// <summary>
        /// 选手的白话说明。没有逐人说明（将来新增模板忘了补文案）时退回传闻一句，绝不显示 *raw key*。
        /// rumorKey 已经是完整本地化 key，不再补前缀。
        /// </summary>
        private static string ResolveFighterPlainDescription(ModeHProfileDto profile)
        {
            if (profile == null) return string.Empty;
            string plain = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Fighter_" + profile.profileId + "_Plain");
            if (!string.IsNullOrEmpty(plain) && plain[0] != '*' && plain.IndexOf("_Plain", StringComparison.Ordinal) < 0)
                return plain;
            return !string.IsNullOrEmpty(profile.rumorKey) ? L10n.T(profile.rumorKey) : string.Empty;
        }

        /// <summary>
        /// 选人点击：一次点击就定下出战选手，并按展示顺序自动配一位能组成完整六场的搭档做接力，
        /// 随即一路推到开打（RosterLocked → 看盘 → 整备 → 按默认值锁盘 → 入场，见 RunAutoAdvance）。
        /// 签约、落选分流与六场可行性仍走原有三道门，规则不在这里另写一份。
        /// 搭档优先挑与主将打法不同的（近战配远程更好看），都不行再放宽到任意一位。
        /// 每次点击都重新校验 lifecycle，避免过期页面推进状态。
        /// </summary>
        private void OnDraftPick(string profileId)
        {
            if (_commandsClosed || _season == null || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.Drafting) return;

            try
            {
                ModeHProfileDto main = FindSeasonProfile(profileId);
                if (main == null) return;

                ModeHContractDto contract = null;
                List<ModeHEchoAssignmentDto> assignments = null;
                string failureReasonId = null;
                bool viable = false;
                for (int pass = 0; pass < 2 && !viable; pass++)
                {
                    for (int i = 0; _season.profiles != null && i < _season.profiles.Count && !viable; i++)
                    {
                        ModeHProfileDto partner = _season.profiles[i];
                        if (partner == null || string.Equals(partner.profileId, profileId, StringComparison.Ordinal)) continue;
                        if (pass == 0 && string.Equals(partner.archetypeId, main.archetypeId, StringComparison.Ordinal)) continue;
                        if (!ModeHDraftController.TrySignContracts(
                                _season.profiles, profileId, partner.profileId,
                                out contract, out failureReasonId)) continue;
                        if (!ModeHDraftController.TryAssignEchoDestinations(
                                _runState.RunSeed, _season.profiles, contract,
                                out assignments, out failureReasonId)) continue;
                        viable = CanConstructFullSeason(contract, assignments, out failureReasonId);
                    }
                }

                if (!viable)
                {
                    // 对手池是「认证池减去这五名候选」：五种搭配都凑不出六场，只能退出重进重抽。
                    if (_owner != null) _owner.ShowMessage(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Draft_NoViablePair"));
                    ModBehaviour.DevLog("[ModeH] 选人后找不到能组成六场的搭档: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    return;
                }

                _season.contract = contract;
                _season.echoAssignments = assignments;

                RunAutoAdvance("champion_picked", delegate
                {
                    if (TryTransition(ModeHLifecycle.Drafting, ModeHLifecycle.RosterLocked, "contracts_signed"))
                    {
                        // 早期恢复子表要求 roster 快照有效，这里是一个显式落盘点
                        TryPersistSeason("roster_locked");
                    }
                });
            }
            catch (Exception e)
            {
                LogFailure("draft_pick", e);
            }
        }

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

            // 正常流程不经过这一页（选人 / 下一场都由 RunAutoAdvance 直接推到开打）。
            // 只有技术重试回落、续赛或自动开打被拒时才停在这里：给一个「开打」走同一条默认值链，
            // 另留「自己调整再开打」进原整备 / 赔率页，免费侦察也照旧可用。
            if (_runState.Lifecycle == ModeHLifecycle.RosterLocked)
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_StartMatch"),
                    OnClick = delegate { RunAutoAdvance("roster_start", null); },
                });
                return page;
            }

            EnsureMatchPlan();
            AppendMatchPreview(page);
            AppendReconLinesAndActions(page);
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_CustomSetup"),
                Interactable = _season.currentMatchPlan != null,
                OnClick = delegate { EnterLoadoutEditing(); },
            });
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_StartMatch"),
                Interactable = _season.currentMatchPlan != null,
                OnClick = delegate { RunAutoAdvance("brief_start", null); },
            });
            return page;
        }

        /// <summary>
        /// 免费侦察的看盘页呈现（§17.5）。
        ///
        /// 此前 `ModeHEncounterPlanner.TryApplyRecon` 与四条 `reconChoices` 数据、
        /// `Button_Recon` / `Recon_Consumed` 文案全都写好了，但**没有任何按钮调用它**，
        /// 于是「每场一次免费侦察」这条设计在游戏里根本不存在：玩家只能盲押。
        ///
        /// 呈现口径：
        /// - 未用过：先出一行「免费侦察一次」当小标题，再列出有实战信息的可选项；
        /// - 已用过：只回显揭示了哪一项，不再出按钮（TryApplyRecon 自己也会以
        ///   `recon_already_consumed` 拒绝，这里是让玩家看得见，而不是靠点了才知道）。
        ///
        /// `nameKey` 在 ThreatPlans.json 里存的是**完整** key（`BossRush_ModeH_Recon_*`），
        /// 不要再拼 LocalizationKeyPrefix，否则会变成 BossRush_ModeH_BossRush_ModeH_xxx。
        /// </summary>
        private void AppendReconLinesAndActions(ModeHPageContent page)
        {
            if (page == null || _season == null) return;
            ModeHMatchPlanDto plan = _season.currentMatchPlan;
            if (plan == null) return;

            if (!string.IsNullOrEmpty(plan.reconChoiceId))
            {
                string line = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recon_Consumed");
                string revealKey = plan.publicSummary != null ? plan.publicSummary.reconRevealKey : null;
                if (!string.IsNullOrEmpty(revealKey))
                {
                    line += L10n.T("：", ": ") + L10n.T(revealKey);
                }
                // reconResult 是「成员顺序」「第二装备」两项的文本结果。
                if (!string.IsNullOrEmpty(plan.reconResult) || plan.reconChoiceId == "current_injury")
                {
                    line += "　" + DescribeReconResult(plan);
                }
                // coreTraitTags（「隐藏坏习惯」那一项）此前**全仓零消费**：写进
                // publicSummary 后再没人读，玩家消耗掉本场唯一一次侦察机会却什么都看不到。
                // 旧存档的履历侦察仅回显，新的按钮不消费纯履历信息。
                List<string> traits = plan.publicSummary != null
                    ? plan.publicSummary.coreTraitTags : null;
                if (traits != null && traits.Count > 0)
                {
                    for (int t = 0; t < traits.Count; t++)
                    {
                        if (string.IsNullOrEmpty(traits[t])) continue;
                        line += (t == 0 ? "　" : "、") + ResolveTraitDisplayName(traits[t]);
                    }
                }
                page.Lines.Add(line);
                return;
            }

            List<ModeHReconChoiceSpec> choices = ModeHContentCatalog.ReconChoices;
            if (choices == null || choices.Count == 0) return;

            page.Lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Recon"));
            for (int i = 0; i < choices.Count; i++)
            {
                ModeHReconChoiceSpec choice = choices[i];
                if (!ModeHEncounterPlanner.IsReconChoicePlayable(choice) || string.IsNullOrEmpty(choice.ReconChoiceId)) continue;
                // 闭包不能捕获循环变量，否则按钮点下去都是最后一条（照 SelectSettlementReward 的写法）
                string selectedReconId = choice.ReconChoiceId;
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(choice.NameKey),
                    OnClick = delegate { ApplyRecon(selectedReconId); },
                });
            }
        }

        /// <summary>
        /// 免费侦察的**唯一生产调用点**。只在看盘页（MatchBrief）允许，
        /// 因为揭示结果要在整备与下注之前对玩家可见才有决策价值。
        ///
        /// 侦察会改写 publicSummary 与 planDigest，属于赛季状态变更，必须落盘；
        /// 落盘失败不回滚——TryApplyRecon 已经把结果写进内存中的 plan，
        /// 这里再退回去反而会让「按钮点了没反应」，而侦察本身不涉及任何资产。
        /// </summary>
        private void ApplyRecon(string reconChoiceId)
        {
            if (_season == null || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.MatchBrief) return;

            ModeHMatchPlanDto plan = _season.currentMatchPlan;
            if (plan == null) return;

            string failureReasonId;
            if (!ModeHEncounterPlanner.TryApplyRecon(plan, reconChoiceId, out failureReasonId))
            {
                ModBehaviour.DevLog("[ModeH] 侦察未生效: "
                    + (failureReasonId != null ? failureReasonId : "unknown"));
                return;
            }

            TryPersistSeason("recon_applied");
            RouteUiForLifecycle(_runState.Lifecycle);
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
                // EncounterPlanner 的 roster veto 按公开 archetypeId 判断；调用方之前把
                // profileId 直接传入，导致克制标签永远匹配不上。敌军池也必须排除本季
                // 五席候选，否则撕票/回场签规则会被绕开。
                List<string> liveArchetypeIds = BuildLiveArchetypeIds();
                List<string> enemyPool = BuildPlanEnemyPool();
                ModeHMatchPlanDto plan;
                int usedCandidateIndex;
                string failureReasonId;
                if (!ModeHEncounterPlanner.TryBuildPlan(
                        _runState.RunSeed,
                        _runState.MatchIndex,
                        _runState.TechnicalRetrySequence,
                        enemyPool,
                        ResolveEchoReturnStableKey(),
                        liveArchetypeIds,
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
            if (_season == null || _runState == null
                || _runState.MatchIndex != ModeHConfig.EchoReturnMatchIndex) return null;
            return ResolveEchoReturnStableKey(_season.echoAssignments);
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

        /// <summary>
        /// 看盘页的真实押品区：当前已押件数、最坏损失、胜利可得，以及逐格的选/取消按钮。
        ///
        /// 只在 IsSlotConsistent 为真时给出按钮（§22.1 的只读派生结果）。
        /// 列表本身按仓库槽位顺序枚举**前若干格里能押的物品**，不做搜索与筛选 UI：
        /// 这是看盘页的一个副区，不是仓库管理器；玩家真要挑特定物品可以先在
        /// 官方仓库界面整理好位置。
        /// </summary>
        private void AppendRealStakeLinesAndActions(ModeHPageContent page)
        {
            if (page == null) return;
            try
            {
                int selectedCount = ModeHRealStakeService.SelectedCount;
                page.Lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_SelectedCount")
                    + "  " + selectedCount + " / " + ModeHConfig.MaxRealStakeItemsPerMatch);

                if (!ModeHWarehouseStakeJournal.IsSlotConsistent)
                {
                    // 原因已由 RealStakeDisabledReason 在选择器上原位展示，这里不重复喷文案
                    return;
                }

                if (selectedCount > 0 && _currentOddsQuote != null)
                {
                    page.Lines.Add(
                        L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_WorstCasePreview")
                        + "  " + ModeHRealStakeService.PreviewWorstCaseLossCount()
                        + L10n.T(" 件", " item(s)")
                        + "　"
                        + L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_RewardPreview")
                        + "  " + ModeHRealStakeService.PreviewRewardCount(_currentOddsQuote.Odds)
                        + L10n.T(" 件", " item(s)"));
                }

                List<int> selectable = ModeHRealStakeService.GetSelectablePositions();
                List<int> selected = ModeHRealStakeService.GetSelectedPositions();
                for (int i = 0; i < selectable.Count; i++)
                {
                    int position = selectable[i];
                    bool isSelected = selected.Contains(position);
                    string label = ModeHRealStakeService.DescribePosition(position);
                    // 走 RealStakeSlots 而不是 Actions：押品格数 = 仓库前 40 格的非空格数，
                    // 无上界；塞进底部动作行会把单行居中平铺撑出屏幕，把排在最后的
                    // 「锁盘」推到点不到的地方，玩家就卡在这个时停模态页了。
                    page.RealStakeSlots.Add(new ModeHActionData
                    {
                        Label = (isSelected ? "● " : "○ ") + label,
                        // 已选中的永远可点（要能取消）；未选中的在满员时置灰
                        Interactable = isSelected
                            || selectedCount < ModeHConfig.MaxRealStakeItemsPerMatch,
                        // 押真实物品是不可逆风险动作，用 Danger token 与虚拟下注区分开
                        IsDanger = true,
                        OnClick = delegate { ToggleRealStakeSelection(position); },
                    });
                }
            }
            catch (Exception e)
            {
                LogFailure("odds_real_stake_section", e);
            }
        }

        /// <summary>切换一格押品选中态并刷新页面。失败时原位提示，不静默。</summary>
        private void ToggleRealStakeSelection(int position)
        {
            if (_commandsClosed || _runState == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.OddsPreview
                && _runState.Lifecycle != ModeHLifecycle.LoadoutEditing)
            {
                return;
            }

            string failureReasonId;
            if (!ModeHRealStakeService.ToggleSelection(position, out failureReasonId))
            {
                ModBehaviour.DevLog("[ModeH] 押品选择被拒: "
                    + (failureReasonId != null ? failureReasonId : "unknown"));
                // 本文件头的契约要求"任何一步失败都不静默"：只写 DevLog 的话，
                // 玩家点了装备却毫无反应，会以为是按钮坏了。
                if (_owner != null) _owner.ShowMessage(ResolveStakeRejectReason(failureReasonId));
            }
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        /// <summary>
        /// 把押品选择被拒的内部 reasonId 翻译成玩家看得懂的一句话。
        /// 分因的意义：「已达上限」是玩家自己可调整的，而「这件装备读不出品质」
        /// 说明该格不可押、换一件即可，两者的下一步动作完全不同。
        /// 未登记的原因回落通用文案，绝不把 reasonId 原文喷给玩家。
        /// </summary>
        private static string ResolveStakeRejectReason(string failureReasonId)
        {
            if (!string.IsNullOrEmpty(failureReasonId))
            {
                if (failureReasonId.IndexOf("stake_slot_inconsistent", StringComparison.Ordinal) >= 0)
                {
                    return ResolveRealStakeDisabledReason();
                }
                if (failureReasonId.IndexOf("stake_limit_reached", StringComparison.Ordinal) >= 0)
                {
                    return L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Reject_LimitReached");
                }
            }
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Reject_Unstakeable");
        }

        /// <summary>
        /// 把锁盘准备失败的内部 reasonId 翻译成玩家看得懂的一句话。
        ///
        /// 分因的意义与押品被拒同理：「没有可用口令」说明这名选手的预设没通过认证、
        /// 换人或重新认证才有用；「阵容不可用」是合同选手全退役这类赛季级状况，
        /// 两者的下一步动作完全不同。押品类原因直接委托给既有的押品文案，
        /// 避免同一件事出现两套说法。未登记的原因回落通用文案，
        /// **绝不把 reasonId 原文喷给玩家**。
        /// </summary>
        private static string ResolveLockRejectReason(string failureReasonId)
        {
            if (!string.IsNullOrEmpty(failureReasonId))
            {
                // 押品链的失败沿用押品自己的分因文案（含 slot 不一致的细分）
                if (failureReasonId.IndexOf("stake_", StringComparison.Ordinal) >= 0)
                {
                    return ResolveStakeRejectReason(failureReasonId);
                }
                if (failureReasonId.IndexOf("command_missing", StringComparison.Ordinal) >= 0
                    || failureReasonId.IndexOf("no_selectable_command", StringComparison.Ordinal) >= 0)
                {
                    return L10n.T(ModeHConfig.LocalizationKeyPrefix + "LockReject_CommandUnavailable");
                }
                if (failureReasonId.IndexOf("roster", StringComparison.Ordinal) >= 0
                    || failureReasonId.IndexOf("starter_missing", StringComparison.Ordinal) >= 0
                    || failureReasonId.IndexOf("selection_missing", StringComparison.Ordinal) >= 0)
                {
                    return L10n.T(ModeHConfig.LocalizationKeyPrefix + "LockReject_RosterMissing");
                }
            }
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "LockReject_Generic");
        }

        /// <summary>
        /// 把 IsSlotConsistent=false 的内部 reasonId 翻译成玩家看得懂的一句话。
        /// 分因很重要：「上一笔还没结算」是玩家能自己去恢复面板处理的，
        /// 而笼统的「无法证明资产安全」会被读成"我的存档坏了"。
        /// 未登记的原因回落通用文案，绝不把 reasonId 原文喷给玩家。
        /// </summary>
        private static string ResolveRealStakeDisabledReason()
        {
            string reasonId = ModeHWarehouseStakeJournal.SlotInconsistentReasonId;
            string key = "RealStake_Disabled";
            if (!string.IsNullOrEmpty(reasonId))
            {
                if (reasonId.IndexOf("slot_active_journal", StringComparison.Ordinal) >= 0
                    || reasonId.IndexOf("slot_phase_unknown", StringComparison.Ordinal) >= 0)
                {
                    key = "RealStake_Disabled_PendingTx";
                }
                else if (reasonId.IndexOf("slot_manual_intervention", StringComparison.Ordinal) >= 0)
                {
                    key = "RealStake_Disabled_ManualIntervention";
                }
                else if (reasonId.IndexOf("slot_storage_unavailable", StringComparison.Ordinal) >= 0)
                {
                    key = "RealStake_Disabled_StorageUnavailable";
                }
            }
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + key);
        }

        private ModeHPageContent BuildOddsPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Odds");

            // 押品选择器可用性是 §22.1 的**只读派生结果**，不是开关
            // （ModeHConfigApiGuard 禁止任何 RealWarehouseStake 开关符号）。
            // 证据不足时禁用并原位说明原因，赛季照常用虚拟筹码跑完整闭环。
            page.RealStakeSelectorEnabled = ModeHWarehouseStakeJournal.IsSlotConsistent;
            // 风险提示只在真的能押的时候出：功能不可用还挂着「失败永久没收」
            // 会让玩家以为自己的存档坏了，而不是"这个功能现在用不了"。
            page.ShowRealStakeNotice = page.RealStakeSelectorEnabled;
            if (!page.RealStakeSelectorEnabled)
            {
                page.RealStakeDisabledReason = ResolveRealStakeDisabledReason();
            }

            if (_season == null || _runState == null) return page;

            string prepareFailure;
            if (!EnsurePreparedMatchSelection(out prepareFailure))
            {
                // 这条分支此前直接 return，page.Actions 为空——而 CreateActions 遇到
                // 零按钮直接 return，一个控件都不画；赔率页却已经 ClaimModalInput
                // （timeScale=0 + 禁输入）且没有 ESC 处理，玩家就被困在时停页上。
                // 整备拿不出阵容属技术故障，与 EnsureMatchPlan 同口径消耗一次重试预算，
                // 由状态机把玩家路由到恢复壳（那里有可点的动作），而不是留在死页上。
                page.Body = L10n.T("本场整备不可用：", "Match setup unavailable: ")
                    + L10n.T(ModeHAvailability.GetReasonLocalizationKey(prepareFailure));
                RequestTechnicalRetry(prepareFailure != null ? prepareFailure : "prepare_failed");
                return page;
            }

            if (_showLoadoutEditor) return BuildLoadoutEditorPage();

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
                    // LabelKey 在 ModeHOddsController.Add 里已经拼过 LocalizationKeyPrefix，
                    // 存的是**完整** key；这里再拼一次会变成
                    // BossRush_ModeH_BossRush_ModeH_Odds_xxx，18 条分量标签全显示星号 raw key。
                    // 同一个坑在本文件 :523 已有告警，正确写法见 :562。
                    page.Lines.Add(L10n.T(entry.LabelKey)
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

            AppendRealStakeLinesAndActions(page);

            ModeHMatchRosterDto editOwner = _season.matchRoster;
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("调整阵容 / 配装 / 口令", "Edit roster / kits / command"),
                OnClick = delegate
                {
                    if (!CanEditLoadout(editOwner)) return;
                    _showLoadoutEditor = true;
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });

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
                    // 只写 DevLog 不行：它带 [Conditional("BOSSRUSH_DEV")]，正式构建里
                    // 整个被剥离，玩家点了锁盘会**毫无反应**，和按钮坏了没有区别。
                    // 锁盘是开战前的最后一步，静默失败等于把人堵死在赔率页。
                    if (_owner != null) _owner.ShowMessage(ResolveLockRejectReason(prepareFailure));
                    return;
                }
                if (!TryTransition(ModeHLifecycle.OddsPreview, ModeHLifecycle.LoadoutLocked, "loadout_locked"))
                {
                    RequestTechnicalRetry("loadout_transition_failed");
                    return;
                }
                // 开战前的最后一个显式落盘点：技术中止要按它回到同一场
                if (!TryPersistSeason("loadout_locked", true))
                {
                    // 挂起保留赛前快照；恢复时经资产屏障确认退款后才撤销预约。
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

            // 押了真实物品的这一场要经 StakePrepared 再进生成：冻结表为真实资产
            // 支路专门留了 LoadoutLocked -> StakePrepared -> MatchSpawning 这条边，
            // 让恢复流程能从 lifecycle 一眼看出"这一场有押品"。
            // 没押的主干仍是 LoadoutLocked -> MatchSpawning 直连（guard 冻结这一点）。
            ModeHLifecycle spawnOrigin = ModeHLifecycle.LoadoutLocked;
            if (_season != null && _season.currentLoadoutLock != null
                && _season.currentLoadoutLock.realStakeSelected)
            {
                if (!TryTransition(ModeHLifecycle.LoadoutLocked, ModeHLifecycle.StakePrepared,
                        "stake_prepared"))
                {
                    return;
                }
                spawnOrigin = ModeHLifecycle.StakePrepared;
            }

            if (!TryTransition(spawnOrigin, ModeHLifecycle.MatchSpawning, "spawn_begin"))
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

            // 真实押品与虚拟预约在统一技术重试入口按屏障顺序收口。
            if (_runState == null) return;
            RequestTechnicalRetry(reasonId != null ? reasonId : "spawn_failed");
        }

        /// <summary>本场生成协程句柄，关停时取消。</summary>
        private Coroutine _spawnRoutine;

        /// <summary>本场生成事务。</summary>
        private ModeHSpawnTransaction _spawnTransaction;

        /// <summary>
        /// 结算页：战报本体仍由 BuildCompletedSettlementPageContent 组装（身份围栏不变），
        /// 这里补上自动处理的战痕 / 整备说明，并把唯一的「确认」换成「下一场」直接开打（见 UiFlow）。
        /// </summary>
        private ModeHPageContent BuildSettlementPageContent()
        {
            ModeHPageContent page = BuildCompletedSettlementPageContent();
            DecorateSettlementPage(page);
            return page;
        }

        #endregion
    }
}

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
            if (_season == null) return page;

            page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                + " " + (_runState != null ? _runState.MatchIndex + 1 : 1)
                + " / " + ModeHConfig.SeasonMatchCount;
            return page;
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
            return page;
        }

        private ModeHPageContent BuildSettlementPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Settlement");
            return page;
        }

        #endregion
    }
}

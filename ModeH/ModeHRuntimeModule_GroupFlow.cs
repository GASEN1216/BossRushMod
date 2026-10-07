// ============================================================================
// ModeHRuntimeModule_GroupFlow.cs - 鸭王杯群战的玩家流程（2026-09-29 owner 改版，第二轮）
// ============================================================================
// 每场：一张页面同时摆出蓝队（左）与红队（右），两队一起抽、按战力配平（差 ≤ 500），战力与人数随场次爬升；
//     玩家选押哪一队赢 + 押多少 →「开打」（两群同时上场）→ 结算 → 下一场；6 场打完进名人堂看排名。
//     「换一批」两队一起重抽，页面原地换内容（同一标题走 ModeHUI 的同页刷新，不重播打开 / 入场动画）。
// 沿用原有冻结状态机与落盘点：
//   Drafting（第 1 场）→ RosterLocked → MatchBrief（第 2~6 场）→ LoadoutEditing → OddsPreview → LoadoutLocked
//   → MatchSpawning → MatchFighting → MatchSettling → Intermission。
// 单挑版的选秀 / 接力 / 口令 / 伤病 / 战痕 / 转会 / 战前调整代码保留不删，群战模式下不再走到。
// 两队只存在运行时（ModeHGroupRoster）：中断重进回到这一页重新抽，押注按原有账本沿用或结清。
// 押哪边随押注记进账本（ModeHCashBetRecord.betSide）：沿用押注时恢复这一边，并收起「押哪边赢」与「换一批」，
// 免得重进后换边 / 换阵容还按旧赔率结算（2026-10-01 审查 P1）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        /// <summary>群战模式开关（owner 定：鸭王杯改成群战；旧单挑流程代码保留）。</summary>
        internal static bool GroupModeEnabled { get { return ModeHGroupConfig.Enabled; } }

        private ModeHGroupRoster _groupRoster;
        private List<ModeHGroupEntry> _groupPool;
        private ModeHGroupBattle _groupBattle;
        private readonly List<string> _groupResultLines = new List<string>();
        private string _groupResultRunId;
        private int _groupResultMatchIndex = -1;
        /// <summary>玩家押哪一队赢（页面上的选择，跨场沿用）；true = 红队。</summary>
        private bool _groupBetOnRed;
        /// <summary>锁盘那一刻押的队（本场结算按它，开打后改页面选择不影响本场）。</summary>
        private bool _groupLockedBetOnRed;

        /// <summary>群战页面当前对应的场次：第 1 场在 Drafting 时 MatchIndex 还是 0。</summary>
        private int GroupMatchIndex
        {
            get
            {
                if (_runState == null) return ModeHConfig.FirstMatchIndex;
                return _runState.MatchIndex > 0 ? _runState.MatchIndex : ModeHConfig.FirstMatchIndex;
            }
        }

        #region 两队

        private List<ModeHGroupEntry> EnsureGroupPool()
        {
            // 只有官方 Boss 也进了池才缓存：预设表还没就绪时建出来的只剩三只自定义，下一次要重建
            if (_groupPool != null && CountOfficial(_groupPool) > 0) return _groupPool;
            try
            {
                if (_owner != null && (_owner.BossFilterEnemyPresets == null || _owner.BossFilterEnemyPresets.Count == 0))
                    _owner.InitializeBossFilterEnemyPresets();
            }
            catch (Exception e) { LogFailure("group_pool_presets", e); }
            _groupPool = ModeHGroupPool.Build(_owner);
            return _groupPool;
        }

        private static int CountOfficial(List<ModeHGroupEntry> pool)
        {
            int count = 0;
            for (int i = 0; pool != null && i < pool.Count; i++) if (pool[i] != null && !pool[i].IsCustom) count++;
            return count;
        }

        private System.Random CreateGroupRng(int salt)
        {
            long seed = _runState != null ? _runState.RunSeed : 0L;
            unchecked
            {
                seed = seed * 31L + GroupMatchIndex * 7919L + salt * 104729L
                    + (_runState != null ? _runState.TechnicalRetrySequence * 15485863L : 0L);
                return new System.Random((int)(seed ^ (seed >> 32)));
            }
        }

        /// <summary>本场两队：没有或是上一场的就两队一起重新抽。</summary>
        private ModeHGroupRoster EnsureGroupRoster()
        {
            if (_runState == null) return null;
            int matchIndex = GroupMatchIndex;
            if (_groupRoster != null && _groupRoster.MatchIndex == matchIndex
                && string.Equals(_groupRoster.RunId, _runState.RunId, StringComparison.Ordinal)
                && _groupRoster.Allies.Count > 0 && _groupRoster.Enemies.Count > 0) return _groupRoster;
            ModeHGroupRoster roster = new ModeHGroupRoster();
            roster.RunId = _runState.RunId;
            roster.MatchIndex = matchIndex;
            if (!ModeHGroupPool.TryRollTeams(CreateGroupRng(0), EnsureGroupPool(), matchIndex, roster)) return null;
            _groupRoster = roster;
            return roster;
        }

        private bool IsGroupRosterReadyForCurrentMatch()
        {
            return _runState != null && _groupRoster != null
                && _groupRoster.MatchIndex == GroupMatchIndex
                && string.Equals(_groupRoster.RunId, _runState.RunId, StringComparison.Ordinal)
                && _groupRoster.Allies.Count > 0 && _groupRoster.Enemies.Count > 0;
        }

        private bool IsGroupRosterPhase()
        {
            return _runState != null && (_runState.Lifecycle == ModeHLifecycle.Drafting
                || _runState.Lifecycle == ModeHLifecycle.MatchBrief);
        }

        /// <summary>「换一批」：两队一起重抽，同一张页原地换内容（不播入场动画，免得闪一下）。</summary>
        private void RerollGroupRoster()
        {
            if (_commandsClosed || _runState == null || !IsGroupRosterPhase()) return;
            if (CarriedGroupBet() != null) return; // 沿用押注的这一场不能换阵容（赔率是押注时定的）
            ModeHGroupRoster roster = EnsureGroupRoster();
            if (roster == null || roster.RerollsUsed >= ModeHGroupConfig.RerollsPerMatch) return;
            int used = roster.RerollsUsed + 1;
            if (!ModeHGroupPool.TryRollTeams(CreateGroupRng(used), EnsureGroupPool(), roster.MatchIndex, roster)) return;
            roster.RerollsUsed = used;
            EnsureMatchPlan();
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        private void SelectGroupBetSide(bool red)
        {
            if (_commandsClosed || _runState == null || !IsGroupRosterPhase() || _groupBetOnRed == red) return;
            if (IsGroupBetSideLocked(CarriedGroupBet())) return; // 沿用押注：押哪边已定，不能换边
            _groupBetOnRed = red;
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        /// <summary>「开打」：第 1 场顺带签完名单进首场，再沿原自动链锁盘、生成。</summary>
        private void StartGroupMatch()
        {
            if (_commandsClosed || _runState == null || !IsGroupRosterPhase() || !IsGroupRosterReadyForCurrentMatch()) return;
            if (_runState.Lifecycle != ModeHLifecycle.Drafting)
            {
                StartMatchFromBrief();
                return;
            }
            _allowBriefToLoadout = true;
            try
            {
                RunAutoAdvance("group_start", delegate
                {
                    if (TryTransition(ModeHLifecycle.Drafting, ModeHLifecycle.RosterLocked, "group_start"))
                        TryPersistSeason("roster_locked");
                });
            }
            finally { _allowBriefToLoadout = false; }
        }

        /// <summary>按当前两队建本场计划（敌方 = 红队）。计划仍走原有字段与摘要，锁盘、恢复与账本照旧认它。</summary>
        private void EnsureGroupMatchPlan()
        {
            if (_season == null || _runState == null) return;
            if (_runState.MatchIndex < ModeHConfig.FirstMatchIndex) return; // 第 1 场开打时才进 MatchBrief 建计划
            if (!IsGroupRosterReadyForCurrentMatch())
            {
                // 读档回来：盘上那份计划对不上现在这两队，作废，等页面重新抽
                if (_season.currentMatchPlan != null && _season.currentMatchPlan.matchIndex == _runState.MatchIndex
                    && _runState.Lifecycle == ModeHLifecycle.MatchBrief)
                    _season.currentMatchPlan = null;
                return;
            }
            string planId = "group|" + _runState.MatchIndex + "|" + _groupRoster.RerollsUsed + "|"
                + _runState.TechnicalRetrySequence;
            if (_season.currentMatchPlan != null && _season.currentMatchPlan.matchIndex == _runState.MatchIndex
                && string.Equals(_season.currentMatchPlan.planId, planId, StringComparison.Ordinal)) return;
            try
            {
                ModeHMatchPlanDto plan = new ModeHMatchPlanDto();
                plan.matchIndex = _runState.MatchIndex;
                plan.planId = planId;
                plan.technicalRetrySequence = _runState.TechnicalRetrySequence;
                plan.skeletonId = "group_battle";
                plan.entryScriptId = "all_in";
                plan.conditionId = string.Empty;
                plan.enemyStableKeys = new List<string>();
                plan.enemyBatchIndices = new List<int>();
                for (int i = 0; i < _groupRoster.Enemies.Count; i++)
                {
                    plan.enemyStableKeys.Add(_groupRoster.Enemies[i].Key);
                    plan.enemyBatchIndices.Add(0);
                }
                ModeHPublicSummaryDto summary = new ModeHPublicSummaryDto();
                summary.enemyCountMin = summary.enemyCountMax = _groupRoster.Enemies.Count;
                summary.primaryArchetypeId = string.Empty;
                summary.entryScriptId = plan.entryScriptId;
                summary.conditionId = string.Empty;
                summary.synergyTags = new List<string>();
                summary.visibleAnomalyIds = new List<string>();
                summary.coreTraitTags = new List<string>();
                summary.reconRevealKey = string.Empty;
                plan.publicSummary = summary;
                plan.reconChoiceId = string.Empty;
                plan.reconResult = string.Empty;
                plan.threatBudget = _groupRoster.EnemyPower;
                plan.planSeed = unchecked(_runState.RunSeed + _runState.MatchIndex * 7919L);
                plan.specialEnemySourceTag = string.Empty;
                plan.planDigest = string.Empty;
                string digest, digestError;
                if (!ModeHCanonicalDigest.TryComputeObjectDigest(plan, "planDigest", out digest, out digestError))
                {
                    RequestTechnicalRetry("group_plan_digest_failed:" + digestError);
                    return;
                }
                plan.planDigest = digest;
                _season.currentMatchPlan = plan;
                TryPersistSeason("match_plan");
            }
            catch (Exception e)
            {
                LogFailure("group_plan", e);
                RequestTechnicalRetry("group_plan_exception");
            }
        }

        /// <summary>页面展示用的返还倍率：只看押的那一队与对面的合计战力（与单挑版同一条分差公式与档位）。</summary>
        private int ResolveGroupOdds(bool red)
        {
            if (_groupRoster == null) return ModeHConfig.MinOdds;
            int mine = red ? _groupRoster.EnemyPower : _groupRoster.AllyPower;
            int theirs = red ? _groupRoster.AllyPower : _groupRoster.EnemyPower;
            return ModeHStateModel.ResolveOddsTier(ModeHOddsController.ComputePreparedPowerEdge(mine, theirs));
        }

        /// <summary>锁盘用的报价：押哪队就按哪队的角度算分差。</summary>
        private bool EnsureGroupOddsQuote(out string failureReasonId)
        {
            failureReasonId = null;
            if (_season == null || _runState == null || !IsGroupRosterReadyForCurrentMatch()
                || _season.currentMatchPlan == null)
            {
                failureReasonId = "group_roster_not_ready";
                return false;
            }
            RestoreCarriedGroupBetSide(); // 沿用押注：锁盘与结算都按押注时那一边
            int mine = _groupBetOnRed ? _groupRoster.EnemyPower : _groupRoster.AllyPower;
            int theirs = _groupBetOnRed ? _groupRoster.AllyPower : _groupRoster.EnemyPower;
            int edge = ModeHOddsController.ComputePreparedPowerEdge(mine, theirs);
            ModeHOddsQuote quote = new ModeHOddsQuote();
            quote.PlayerPublicScore = 100 + edge;
            quote.EnemyPublicScore = 100;
            quote.PublicEdge = edge;
            quote.Odds = ModeHStateModel.ResolveOddsTier(edge);
            quote.ToneKey = string.Empty;
            quote.Breakdown = new List<ModeHOddsBreakdownEntry>();
            _currentOddsQuote = quote;
            _selectedVirtualStake = 0;
            return true;
        }

        #endregion

        #region 页面

        /// <summary>群战接管的页面路由；返回 false 的相位（结算、恢复等）照旧走单挑版的路由。</summary>
        private bool RouteGroupPage(ModeHLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case ModeHLifecycle.Drafting:
                case ModeHLifecycle.MatchBrief:
                    OpenLifecyclePage(ModeHPage.Brief, lifecycle, BuildGroupPageContent);
                    return true;
                case ModeHLifecycle.RosterLocked:
                    // 只有恢复会停在这里：直接推到首场页面，不留一张只有「开打」的空页
                    RunAutoAdvance("group_roster_locked", null);
                    return true;
                case ModeHLifecycle.MatchSpawning:
                case ModeHLifecycle.MatchFighting:
                case ModeHLifecycle.RelayPending:
                    _ui.ClosePage();
                    _ui.EnsureHud(OnBellPressed, OnSurrenderPressed, OnSpectatorExitPressed);
                    ApplyGroupHud();
                    return true;
                case ModeHLifecycle.HallOfFame:
                    OpenLifecyclePage(ModeHPage.HallOfFame, lifecycle, BuildGroupHallOfFamePageContent);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 群战的唯一赛前页（Drafting 与 MatchBrief 共用，标题固定）：蓝红两队 + 押哪边赢 + 押注 +「换一批 / 开打」。
        /// 标题不变，换一批 / 换押注 / 换押哪边都走 ModeHUI 的同页刷新：只换内容、保留滚动、不播动画。
        /// </summary>
        private ModeHPageContent BuildGroupPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Brief");
            if (_season == null || _runState == null) return page;
            page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match").Replace("{0}", GroupMatchIndex.ToString())
                + " / " + ModeHConfig.SeasonMatchCount;
            ModeHGroupRoster roster = EnsureGroupRoster();
            if (roster == null)
            {
                page.Body += "\n" + L10n.T("Boss 池还没准备好，稍后再试。", "The boss pool isn't ready yet; try again shortly.");
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("重新抽", "Draw again"),
                    OnClick = delegate { if (_runState != null) RouteUiForLifecycle(_runState.Lifecycle); },
                });
                return page;
            }
            EnsureMatchPlan();
            page.PlayerSideTitle = ModeHGroupTeamTags.TeamName(false);
            page.EnemySideTitle = ModeHGroupTeamTags.TeamName(true);
            page.TeamColorSides = true;
            AppendGroupSide(page.PlayerFighters, roster.Allies);
            AppendGroupSide(page.EnemyFighters, roster.Enemies);
            page.PlayerSideNote = DescribeGroupSide(roster.Allies.Count, roster.AllyPower);
            page.EnemySideNote = DescribeGroupSide(roster.Enemies.Count, roster.EnemyPower);
            page.MatchNote = L10n.T("两队同时上场，一边全倒就分胜负；拍铃是一次全场天灾，两队都挨。",
                "Both teams fight at once; the side left standing wins. The bell calls one disaster on everyone.");
            // 中断后重打沿用押注：押哪边恢复成押注时那一边，倍率按押注时定下的那一档（结算就按它赔）
            ModeHCashBetRecord carried = RestoreCarriedGroupBetSide();
            page.Headline = L10n.T("押" + ModeHGroupTeamTags.TeamName(_groupBetOnRed) + "赢 · 返还倍率",
                "Back " + ModeHGroupTeamTags.TeamName(_groupBetOnRed) + " · payout");
            page.HeadlineValue = carried != null ? FormatPayoutMultiplier(carried) : FormatPayoutMultiplier(ResolveGroupOdds(_groupBetOnRed));

            if (!IsGroupBetSideLocked(carried))
            {
                ModeHOptionRow side = new ModeHOptionRow();
                side.Label = L10n.T("押哪边赢", "Back");
                for (int i = 0; i < 2; i++)
                {
                    bool red = i == 1;
                    side.Options.Add(new ModeHActionData
                    {
                        Label = ModeHGroupTeamTags.TeamName(red) + " " + FormatPayoutMultiplier(ResolveGroupOdds(red)),
                        IsSelected = _groupBetOnRed == red,
                        OnClick = delegate { SelectGroupBetSide(red); },
                    });
                }
                page.OptionRows.Add(side);
            }
            AppendCashBetRow(page);

            int left = ModeHGroupConfig.RerollsPerMatch - roster.RerollsUsed;
            if (left > 0 && carried == null)
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("换一批（剩 " + left + " 次）", "Redraw (" + left + " left)"),
                    OnClick = RerollGroupRoster,
                });
            }
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_StartMatch") + DescribeStandingBetSuffix(),
                IsPrimary = true,
                OnClick = StartGroupMatch,
            });
            return page;
        }

        private static string DescribeGroupSide(int count, int power)
        {
            return L10n.T(count + " 人 · 合计战力 ", count + " fighters · Power ") + FormatMoney(power);
        }

        /// <summary>群战卡片只放立绘、名字与战力（二十个人一人一张大卡放不下），UI 按紧凑网格排。</summary>
        private static void AppendGroupSide(List<ModeHCardData> cards, List<ModeHGroupEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ModeHGroupEntry entry = entries[i];
                if (entry == null) continue;
                cards.Add(new ModeHCardData
                {
                    Title = entry.DisplayName,
                    Subtitle = L10n.T("战力 ", "Power ") + FormatMoney(entry.Power)
                        + (entry.IsCustom ? L10n.T(" · 模组 Boss", " · Mod boss") : string.Empty),
                    PortraitKey = entry.Key,
                });
            }
        }

        #endregion

        #region 锁盘与生成

        /// <summary>群战锁盘：沿用单挑版的快照 / 锁盘 DTO 与真实押品闸门，阵容与整备字段留空。</summary>
        private bool PrepareGroupLockedMatch(out string failureReasonId)
        {
            failureReasonId = null;
            if (!EnsureGroupOddsQuote(out failureReasonId)) return false;
            ModeHMatchPlanDto plan = _season.currentMatchPlan;

            ModeHPreMatchSnapshotDto snapshot = new ModeHPreMatchSnapshotDto();
            snapshot.matchIndex = _runState.MatchIndex;
            snapshot.matchStarterProfileId = string.Empty;
            snapshot.matchRelayProfileId = string.Empty;
            snapshot.starterKitIds = new List<string>();
            snapshot.relayKitIds = new List<string>();
            snapshot.loadoutDigest = plan.planDigest ?? string.Empty;
            snapshot.commandId = GroupBellCommandId;
            snapshot.lockedOdds = _currentOddsQuote.Odds;
            snapshot.capturedStateSequence = _runState.StateSequence;
            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(plan.publicSummary, null, out digest, out digestError))
            {
                failureReasonId = "public_summary_digest_failed:" + digestError;
                return false;
            }
            snapshot.publicSummaryDigest = digest;
            if (!ModeHVirtualStakeController.TryReserve(_season, snapshot, 0, out failureReasonId)) return false;

            ModeHLoadoutLockDto locked = new ModeHLoadoutLockDto();
            locked.matchIndex = _runState.MatchIndex;
            locked.matchStarterProfileId = string.Empty;
            locked.matchRelayProfileId = string.Empty;
            locked.starterKitIds = new List<string>();
            locked.relayKitIds = new List<string>();
            locked.commandId = GroupBellCommandId;
            locked.lockedOdds = _currentOddsQuote.Odds;
            locked.reservedVirtualStake = 0;
            locked.planId = plan.planId;
            locked.planDigest = plan.planDigest;
            locked.loadoutDigest = plan.planDigest ?? string.Empty;
            locked.lockedStateSequence = _runState.StateSequence;
            if (!ModeHRealStakeService.TryLockForMatch(
                    _runState.RunId, _runState.MatchIndex, _runState.RunSeed, out failureReasonId))
            {
                ModeHVirtualStakeController.RestoreReservation(_season, snapshot);
                return false;
            }
            locked.realStakeSelected = ModeHRealStakeService.HasLockedStakeForMatch(_runState.RunId, _runState.MatchIndex);
            _groupLockedBetOnRed = _groupBetOnRed;
            _season.preMatchSnapshot = snapshot;
            _season.currentLoadoutLock = locked;
            return true;
        }

        /// <summary>锁盘 DTO 里的口令字段：群战没有口令，拍铃是全局天灾。</summary>
        private const string GroupBellCommandId = "group_disaster";

        /// <summary>右边的锚点：擂台点里离左边锚点最远的那个。</summary>
        private Vector3 ResolveGroupEnemyAnchor(Vector3 allyAnchor)
        {
            Vector3 best = _map.ArenaCenter;
            float bestSqr = -1f;
            Vector3[] points = _map.ArenaSpawnPoints;
            for (int i = 0; points != null && i < points.Length; i++)
            {
                float sqr = (points[i] - allyAnchor).sqrMagnitude;
                if (sqr > bestSqr)
                {
                    bestSqr = sqr;
                    best = points[i];
                }
            }
            return best;
        }

        /// <summary>自定义 Boss 托管生成的回执（协程轮询；owner 失效时由异步侧自行回收）。</summary>
        private sealed class GroupCustomTicket
        {
            public bool Done;
            public string Failure;
            public ModeHGroupUnit Unit;
        }

        /// <summary>
        /// 群战生成：官方 Boss 走原有生成事务（隔离创建 → 统一提交 → 同帧激活），三只自定义 Boss 走托管生成；
        /// 任何一步失败都按技术故障同场重开，绝不判负。
        /// </summary>
        private IEnumerator DriveGroupMatchSpawning()
        {
            ModeHGroupRoster roster = _groupRoster;
            if (_runState == null || _season == null || _map == null || _owner == null
                || !IsGroupRosterReadyForCurrentMatch())
            {
                AbortMatchSpawning("group_spawn_inputs_missing");
                yield break;
            }
            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            string failure;

            Vector3 allyAnchor = _map.PlayerSpawnPos;
            Vector3 enemyAnchor = ResolveGroupEnemyAnchor(allyAnchor);
            List<Vector3> allySlots = ModeHGroupFormation.Build(allyAnchor, roster.Allies.Count);
            List<Vector3> enemySlots = ModeHGroupFormation.Build(enemyAnchor, roster.Enemies.Count);

            List<CharacterRandomPreset> enemyPresets = new List<CharacterRandomPreset>();
            List<string> enemyKeys = new List<string>();
            List<Vector3> enemyPositions = new List<Vector3>();
            List<ModeHGroupEntry> enemyEntries = new List<ModeHGroupEntry>();
            List<CharacterRandomPreset> allyPresets = new List<CharacterRandomPreset>();
            List<string> allyKeys = new List<string>();
            List<Vector3> allyPositions = new List<Vector3>();
            List<ModeHGroupEntry> allyEntries = new List<ModeHGroupEntry>();
            List<ModeHGroupEntry> customEntries = new List<ModeHGroupEntry>();
            List<bool> customSides = new List<bool>();
            List<Vector3> customSlots = new List<Vector3>();
            for (int side = 0; side < 2; side++)
            {
                bool enemy = side == 1;
                List<ModeHGroupEntry> entries = enemy ? roster.Enemies : roster.Allies;
                List<Vector3> slots = enemy ? enemySlots : allySlots;
                for (int i = 0; i < entries.Count; i++)
                {
                    ModeHGroupEntry entry = entries[i];
                    if (entry.IsCustom)
                    {
                        customEntries.Add(entry);
                        customSides.Add(enemy);
                        customSlots.Add(slots[i]);
                        continue;
                    }
                    CharacterRandomPreset preset = ModeHProductionCertification.ResolveAuditedPreset(entry.Key);
                    if (preset == null)
                    {
                        AbortMatchSpawning("group_preset_missing:" + entry.Key);
                        yield break;
                    }
                    (enemy ? enemyPresets : allyPresets).Add(preset);
                    (enemy ? enemyKeys : allyKeys).Add(entry.Key);
                    (enemy ? enemyPositions : allyPositions).Add(slots[i]);
                    (enemy ? enemyEntries : allyEntries).Add(entry);
                }
            }

            ModeHGroupBattle battle = new ModeHGroupBattle(unchecked(_runState.RunSeed + _runState.MatchIndex * 104729L));
            _groupBattle = battle;
            _spawnTransaction = new ModeHSpawnTransaction();
            if (!_spawnTransaction.Begin(_map, generation, ownerToken, out failure))
            {
                AbortMatchSpawning(failure ?? "spawn_tx_begin_failed");
                yield break;
            }
            _spawnTransaction.GroupCapacity = Math.Max(roster.Allies.Count, roster.Enemies.Count) + 1;

            ModeHSpawnDiagnostics diagnostics = new ModeHSpawnDiagnostics();
            if (enemyPresets.Count > 0)
            {
                ModeHSpawnBatchResult enemyResult = new ModeHSpawnBatchResult();
                IEnumerator enemyBatch = _spawnTransaction.SpawnBatch(enemyPresets, enemyKeys, Teams.wolf, false,
                    diagnostics, enemyResult);
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
            }
            if (allyPresets.Count > 0)
            {
                ModeHSpawnBatchResult allyResult = new ModeHSpawnBatchResult();
                IEnumerator allyBatch = _spawnTransaction.SpawnBatch(allyPresets, allyKeys, Teams.scav, true,
                    diagnostics, allyResult);
                while (allyBatch.MoveNext())
                {
                    if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                    yield return allyBatch.Current;
                }
                if (!allyResult.Success)
                {
                    AbortMatchSpawning(allyResult.FailureReasonId ?? "fighter_spawn_failed");
                    yield break;
                }
            }

            // 三只自定义 Boss：逐只托管准备（inactive + 无敌），提交阶段再激活
            List<ModeHGroupUnit> customUnits = new List<ModeHGroupUnit>();
            for (int i = 0; i < customEntries.Count; i++)
            {
                GroupCustomTicket ticket = new GroupCustomTicket();
                PrepareGroupCustomUnitAsync(battle, customEntries[i], customSides[i], customSlots[i],
                    ownerToken, generation, ticket).Forget();
                while (!ticket.Done)
                {
                    if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                    yield return null;
                }
                if (ticket.Unit == null)
                {
                    AbortMatchSpawning(ticket.Failure ?? ("group_custom_prepare_failed:" + customEntries[i].Key));
                    yield break;
                }
                customUnits.Add(ticket.Unit);
                yield return null;
            }
            if (!IsCallbackStillValid(ownerToken, generation) || !ReferenceEquals(_groupBattle, battle)) yield break;

            if (!_spawnTransaction.TryCommit(enemyPositions, allyPositions, allyAnchor, out failure))
            {
                AbortMatchSpawning(failure ?? "spawn_commit_failed");
                yield break;
            }
            AddCommittedGroupUnits(battle, _spawnTransaction.EnemyHandles, enemyEntries, enemyPositions, true);
            AddCommittedGroupUnits(battle, _spawnTransaction.FighterHandles, allyEntries, allyPositions, false);
            for (int i = 0; i < customUnits.Count; i++)
            {
                if (!ActivateGroupCustomUnit(customUnits[i]))
                {
                    AbortMatchSpawning("group_custom_activate_failed:" + customUnits[i].Entry.Key);
                    yield break;
                }
            }

            yield return null;
            if (!IsCallbackStillValid(ownerToken, generation)) yield break;
            if (!_spawnTransaction.VerifyTeamsStableNextFrame(false, out failure))
            {
                AbortMatchSpawning(failure ?? "spawn_team_drift");
                yield break;
            }
            for (int i = 0; i < customUnits.Count; i++) EnforceGroupUnitTeam(customUnits[i]);

            if (!ModeHEventRouter.Bind(ownerToken, battle, out failure))
            {
                AbortMatchSpawning(failure ?? "group_router_bind_failed");
                yield break;
            }
            ModeHEventRouter.SetContext(_runState, _sceneGeneration, _runState.MatchIndex);
            List<ModeHGroupUnit> units = battle.Units;
            for (int i = 0; i < units.Count; i++)
            {
                ModeHGroupUnit unit = units[i];
                unit.Participant = new ModeHParticipantRef
                {
                    ProfileId = unit.IsEnemy ? string.Empty : "group|" + i,
                    StableKey = unit.Entry.Key,
                    PlanSlotIndex = unit.IsEnemy ? i : -1,
                    IsEnemy = unit.IsEnemy,
                    IsRelay = false,
                    Character = unit.Character,
                };
                if (unit.Health != null) ModeHEventRouter.RegisterParticipant(unit.Health, unit.Participant);
            }
            battle.SetCameraPreference(_groupLockedBetOnRed);
            battle.ArmFriendlyFireBarrier();
            battle.Begin();
            if (_owner != null)
                _owner.ShowMessage(L10n.T("观战：A / D、← / →、鼠标左右键切换选手，W / ↑ 看蓝队，S / ↓ 看红队",
                    "Spectate: A / D, ← / → or mouse buttons to switch; W / ↑ Blue Team, S / ↓ Red Team"));

            _spawnRoutine = null;
            if (!TryTransition(ModeHLifecycle.MatchSpawning, ModeHLifecycle.MatchFighting, "combat_started"))
            {
                RequestTechnicalRetry("combat_transition_rejected");
                yield break;
            }
            if (_spectatorLease != null) _spectatorLease.StartAcceptingBell();
            ModeHCashBetService.TryMarkCombatStarted(_runState.RunId, _runState.MatchIndex);
            TryPersistSeason("match_fighting");
        }

        private static void AddCommittedGroupUnits(ModeHGroupBattle battle, List<ModeHSpawnHandle> handles,
            List<ModeHGroupEntry> entries, List<Vector3> positions, bool enemy)
        {
            for (int i = 0; i < handles.Count && i < entries.Count; i++)
            {
                ModeHSpawnHandle handle = handles[i];
                if (handle == null || handle.Character == null) continue;
                handle.ProfileId = enemy ? "enemy|" + i : "group|" + i;
                battle.AddUnit(new ModeHGroupUnit
                {
                    Entry = entries[i],
                    IsEnemy = enemy,
                    Character = handle.Character,
                    Health = handle.Health,
                    SpawnPosition = i < positions.Count ? positions[i] : handle.Character.transform.position,
                });
            }
        }

        /// <summary>
        /// 一只自定义 Boss 的托管准备。准备完 owner 已失效（切图、技术中止）就地回收，不把 inactive 实例漏在场景里。
        /// 召唤物（随从、龙裔）经辅助契约登记到本场，跟随召唤者阵营、比赛结束一起回收。
        /// </summary>
        private async UniTask PrepareGroupCustomUnitAsync(ModeHGroupBattle battle, ModeHGroupEntry entry, bool enemy,
            Vector3 slot, long ownerToken, int generation, GroupCustomTicket ticket)
        {
            ManagedBossPrepareResult prepared = null;
            Teams team = enemy ? Teams.wolf : Teams.scav;
            ModeHGroupUnit[] summoner = new ModeHGroupUnit[1]; // 准备完才有单位，召唤物登记时再取
            try
            {
                ManagedBossSpawnContext ctx = ManagedBossSpawnContext.CreateModeHPrimary(entry.Key,
                    delegate { return IsCallbackStillValid(ownerToken, generation) && ReferenceEquals(_groupBattle, battle); });
                ctx.TryCommitAuxiliaryBeforeActivation = delegate(CharacterMainControl child, ManagedBossRole role)
                {
                    return ReferenceEquals(_groupBattle, battle) && battle.RegisterAuxiliary(child, team, summoner[0]);
                };
                ctx.OnAuxiliaryReleased = delegate(CharacterMainControl child, ManagedBossRole role) { };
                prepared = await ModeHGroupBattle.PrepareCustomAsync(_owner, entry.Key, slot, ctx);
            }
            catch (Exception e)
            {
                ticket.Failure = "group_custom_prepare_exception:" + e.GetType().Name;
            }
            try
            {
                if (prepared == null || prepared.Character == null || prepared.Handle == null)
                {
                    if (prepared != null && prepared.Handle != null) prepared.Handle.CleanupOnce(ManagedBossCleanupReason.SpawnRejected);
                    if (ticket.Failure == null) ticket.Failure = "group_custom_prepare_failed:" + entry.Key;
                    return;
                }
                if (!IsCallbackStillValid(ownerToken, generation) || !ReferenceEquals(_groupBattle, battle))
                {
                    prepared.Handle.CleanupOnce(ManagedBossCleanupReason.OwnerInvalid);
                    return;
                }
                CharacterMainControl character = prepared.Character;
                ModeHGroupUnit unit = new ModeHGroupUnit
                {
                    Entry = entry,
                    IsEnemy = enemy,
                    Character = character,
                    Health = character.Health,
                    ManagedHandle = prepared.Handle,
                    SpawnPosition = slot,
                };
                try
                {
                    unit.SuppressedPreset = character.characterPreset;
                    if (unit.SuppressedPreset != null) ModeHDeathSuppressionRegistry.RegisterPreset(unit.SuppressedPreset);
                    ModeHDeathSuppressionRegistry.RegisterCharacter(unit.Health, character);
                    if (character.CharacterItem != null) character.CharacterItem.SetInt("Exp", 0, true);
                }
                catch (Exception e) { LogFailure("group_custom_register", e); }
                ScaleGroupCustomHealth(character, entry.Key);
                battle.AddUnit(unit);
                summoner[0] = unit;
                ticket.Unit = unit;
            }
            finally
            {
                ticket.Done = true;
            }
        }

        /// <summary>
        /// 鸭王杯里给焚天龙皇加血（2026-09-29 实测：800 血对面一群官方 Boss 集火，半秒就掉进二阶段，一阶段一个技能都放不出来）。
        /// 在激活前改 MaxHealth 基础值再回满，阶段阈值按比例走，二、三阶段也跟着变长；只动这一只实例。
        /// </summary>
        private static void ScaleGroupCustomHealth(CharacterMainControl character, string key)
        {
            float scale = ModeHGroupConfig.CustomHealthScale(key);
            if (scale <= 1.001f || character == null || character.CharacterItem == null || character.Health == null) return;
            try
            {
                ItemStatsSystem.Stat stat = character.CharacterItem.GetStat("MaxHealth");
                if (stat == null) return;
                stat.BaseValue = stat.BaseValue * scale;
                character.Health.SetHealth(character.Health.MaxHealth);
            }
            catch (Exception e) { LogFailure("group_custom_health", e); }
        }

        /// <summary>托管激活（开能力、开 AI），然后改回本边阵营、放到站位、摘掉强追玩家与距离休眠。</summary>
        private bool ActivateGroupCustomUnit(ModeHGroupUnit unit)
        {
            if (unit == null || unit.ManagedHandle == null || unit.Character == null) return false;
            if (!unit.ManagedHandle.ActivateOnce()) return false;
            try
            {
                unit.Character.SetPosition(unit.SpawnPosition);
                EnforceGroupUnitTeam(unit);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(unit.Character);
                return true;
            }
            catch (Exception e)
            {
                LogFailure("group_custom_activate", e);
                return false;
            }
        }

        private static void EnforceGroupUnitTeam(ModeHGroupUnit unit)
        {
            if (unit == null || unit.Character == null) return;
            try
            {
                Teams team = unit.IsEnemy ? Teams.wolf : Teams.scav;
                if (unit.Character.Team != team) unit.Character.SetTeam(team);
                AICharacterController ai = unit.Character.aiCharacterController;
                if (ai == null) ai = unit.Character.GetComponentInChildren<AICharacterController>(true);
                if (ai != null)
                {
                    ai.forceTracePlayerDistance = 0f;
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null && ReferenceEquals(ai.searchedEnemy, main.mainDamageReceiver))
                    {
                        ai.searchedEnemy = null;
                        ai.noticed = false;
                    }
                }
            }
            catch (Exception) { /* 角色刚被回收 */ }
        }

        #endregion

        #region 交战

        private void TickGroupCombat(float deltaTime)
        {
            ModeHGroupBattle battle = _groupBattle;
            if (battle == null) return;
            TickGroupSpectatorInput(battle);
            bool finished = battle.Tick(deltaTime);
            if (_ui != null)
            {
                _ui.TickHud(deltaTime, battle.RemainingSeconds,
                    battle.AllyAlive + " / " + battle.AllyTotal,
                    battle.EnemyAlive + " / " + battle.EnemyTotal,
                    battle.AllyAlive + battle.EnemyAlive,
                    !battle.BellConsumed && (_spectatorLease == null || _spectatorLease.IsBellAccepting),
                    battle.BellConsumed,
                    ModeHGroupBattle.DescribeEffect(battle.BellEffect),
                    battle.BellRemaining);
            }
            if (finished || battle.HasResult) BeginGroupMatchSettlement();
        }

        /// <summary>
        /// 观战切换（2026-09-29 owner）：A / ← / 鼠标左键上一个，D / → / 鼠标右键下一个，W / ↑ 跳到蓝队，S / ↓ 跳到红队。
        /// 暂停、确认框或指针在界面上（点拍铃 / 投降）时不切；玩家身体的输入由观战租约关着，不会跟着走。
        /// </summary>
        private void TickGroupSpectatorInput(ModeHGroupBattle battle)
        {
            try
            {
                if (BossRushUI.IsGamePaused() || BossRushConfirmDialog.IsOpen) return;
                UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
                bool overUi = events != null && events.IsPointerOverGameObject();
                if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || (!overUi && Input.GetMouseButtonDown(0)))
                    battle.CycleCamera(-1);
                else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || (!overUi && Input.GetMouseButtonDown(1)))
                    battle.CycleCamera(1);
                else if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
                    battle.FocusSide(false);
                else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
                    battle.FocusSide(true);
            }
            catch (Exception) { /* 输入读不到：本帧不切 */ }
        }

        /// <summary>HUD：两行改成两队存活，拍铃卡写天灾。</summary>
        private void ApplyGroupHud()
        {
            if (_ui == null) return;
            _ui.HudFirstLabel = ModeHGroupTeamTags.TeamName(false) + L10n.T("存活", " standing");
            _ui.HudSecondLabel = ModeHGroupTeamTags.TeamName(true) + L10n.T("存活", " standing");
            _ui.HudThirdLabel = L10n.T("场上共", "On field");
            ModeHGroupBellEffect effect = _groupBattle != null ? _groupBattle.BellEffect : ModeHGroupBellEffect.None;
            _ui.SetBellCommand(ModeHGroupBattle.DescribeEffect(effect), ModeHGroupBattle.DescribeEffectPlain(effect));
        }

        private void OnGroupBellPressed()
        {
            ModeHGroupBattle battle = _groupBattle;
            if (battle == null) return;
            ModeHGroupBellEffect effect;
            if (!battle.TryRingBell(out effect)) return;
            ApplyGroupHud();
            if (_owner != null)
                _owner.ShowMessage(L10n.T("天灾降临：", "Disaster: ") + ModeHGroupBattle.DescribeEffect(effect));
        }

        #endregion

        #region 结算

        private void BeginGroupMatchSettlement()
        {
            ModeHGroupBattle battle = _groupBattle;
            if (_runState == null || _season == null || battle == null || !battle.HasResult)
            {
                RequestTechnicalRetry("settlement_result_missing");
                return;
            }
            if (!TryTransition(ModeHLifecycle.MatchFighting, ModeHLifecycle.MatchSettling, "result_claimed")) return;
            _lastSettlementReport = null;
            _lastRewardOperation = null;
            try
            {
                ModeHLoadoutLockDto locked = _season.currentLoadoutLock;
                int odds = locked != null ? locked.lockedOdds : ModeHConfig.MinOdds;
                bool betRed = _groupLockedBetOnRed;
                bool won = !battle.Surrendered && battle.Winner == (betRed
                    ? ModeHGroupBattle.WinnerRed : ModeHGroupBattle.WinnerBlue);
                ModeHMatchOutcome outcome = won ? ModeHMatchOutcome.PlayerVictory : ModeHMatchOutcome.PlayerDefeat;

                ModeHMatchReportDto report = new ModeHMatchReportDto();
                report.reportStatus = (int)ModeHMatchReportStatus.SettledPendingArchive;
                report.matchIndex = _runState.MatchIndex;
                report.resultToken = "result|" + _runState.MatchIndex + "|" + (int)outcome + "|"
                    + Mathf.RoundToInt(battle.Elapsed * 1000f);
                report.winner = (int)outcome;
                report.timeout = battle.Timeout;
                report.cowardiceType = battle.Surrendered ? "player_surrender" : string.Empty;
                report.errorTriggered = false;
                report.entrantIds = new List<string>();
                if (_groupRoster != null)
                {
                    // 战报记玩家押的那一队（名人堂头号功臣从这里数）
                    List<ModeHGroupEntry> backed = betRed ? _groupRoster.Enemies : _groupRoster.Allies;
                    for (int i = 0; i < backed.Count; i++) report.entrantIds.Add(backed[i].Key);
                }
                report.injuryEvents = new List<ModeHInjuryEventDto>();
                report.scarOfferId = string.Empty;
                report.finalDefeatedProfileSnapshot = string.Empty;
                report.specialEnemySourceTag = string.Empty;
                report.specialEnemyEligible = false;
                report.consumedCommandId = battle.BellConsumed ? "group_" + (int)battle.BellEffect : string.Empty;
                report.bellConsumed = battle.BellConsumed;
                report.elapsedSeconds = battle.Elapsed;

                ModeHVirtualStakeController.Settle(_season, _season.preMatchSnapshot, report, odds, won);
                string realStakeFailure;
                if (!ModeHRealStakeService.TrySettleMatch(
                        _runState.RunSeed, _runState.MatchIndex, won, odds, out realStakeFailure))
                {
                    ModBehaviour.CriticalLog("[ModeH] [WARNING] 真实押品结算未完成，已保留 journal 交恢复流程: "
                        + (realStakeFailure != null ? realStakeFailure : "unknown"));
                    if (_owner != null) _owner.ShowMessage(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Settle_Failed"));
                }

                string rewardFailure;
                ModeHSeasonRewardOperationDto operation = ModeHSeasonRewardService.BuildOrGet(
                    _season, report, string.Empty, 0, out rewardFailure);
                if (operation == null)
                {
                    RequestTechnicalRetry(rewardFailure ?? "reward_operation_failed");
                    return;
                }
                report.seasonRewardOperationId = operation.operationId;
                UpsertMatchReport(report);
                _season.currentBattleSnapshot = null;
                _lastSettlementReport = report;
                _lastRewardOperation = operation;
                CaptureGroupResultLines(battle);
                if (!TryPersistSeason("match_settling", true))
                {
                    ReleaseCombatRuntimeObjects();
                    RequestSuspended("settlement_persist_failed");
                    return;
                }
                SettleCashBetForMatch(won);
                ReleaseCombatRuntimeObjects();
                if (TryTransition(ModeHLifecycle.MatchSettling, ModeHLifecycle.Intermission, "settlement_committed"))
                    TryPersistSeason("intermission");
            }
            catch (Exception e)
            {
                LogFailure("group_match_settlement", e);
                if (_lastSettlementReport != null)
                {
                    ReleaseCombatRuntimeObjects();
                    RequestSuspended("settlement_exception_after_report");
                }
                else RequestTechnicalRetry("settlement_exception");
            }
        }

        private void CaptureGroupResultLines(ModeHGroupBattle battle)
        {
            _groupResultLines.Clear();
            _groupResultRunId = _runState != null ? _runState.RunId : null;
            _groupResultMatchIndex = _runState != null ? _runState.MatchIndex : -1;
            string outcome = battle.Surrendered ? L10n.T("投降", "Surrendered")
                : battle.Winner == ModeHGroupBattle.WinnerBlue ? ModeHGroupTeamTags.TeamName(false) + L10n.T("胜", " wins")
                : battle.Winner == ModeHGroupBattle.WinnerRed ? ModeHGroupTeamTags.TeamName(true) + L10n.T("胜", " wins")
                : L10n.T("同归于尽（庄家赢）", "Mutual wipe-out (house wins)");
            _groupResultLines.Add(L10n.T("战果：", "Result: ") + outcome);
            _groupResultLines.Add(L10n.T("你押了：", "You backed: ") + ModeHGroupTeamTags.TeamName(_groupLockedBetOnRed));
            _groupResultLines.Add(ModeHGroupTeamTags.TeamName(false) + L10n.T("存活：", " standing: ") + battle.AllyAlive + " / " + battle.AllyTotal);
            _groupResultLines.Add(ModeHGroupTeamTags.TeamName(true) + L10n.T("存活：", " standing: ") + battle.EnemyAlive + " / " + battle.EnemyTotal);
            if (battle.BellConsumed)
                _groupResultLines.Add(L10n.T("天灾：", "Disaster: ") + ModeHGroupBattle.DescribeEffect(battle.BellEffect));
            if (battle.Timeout)
                _groupResultLines.Add(L10n.T("到时按剩余战力判定", "Decided by remaining power at time-out"));
        }

        /// <summary>结算页补两边存活与天灾（只在同一场的结算页上画，读档回来的旧战报不补）。</summary>
        private void AppendGroupResultLines(ModeHPageContent page)
        {
            if (page == null || _runState == null || _groupResultLines.Count == 0
                || _groupResultMatchIndex != _runState.MatchIndex
                || !string.Equals(_groupResultRunId, _runState.RunId, StringComparison.Ordinal)) return;
            page.Lines.InsertRange(0, _groupResultLines);
        }

        /// <summary>第 1~5 场打完进下一场选人，第 6 场打完进名人堂；不论输赢都打满 6 场。</summary>
        private void RouteGroupAfterIntermission()
        {
            if (_runState.MatchIndex >= ModeHConfig.SeasonMatchCount)
            {
                EnterHallOfFame();
                return;
            }
            OpenNextMatchBrief("intermission_complete");
        }

        #endregion

        #region 名人堂排名

        /// <summary>一季群战的名人堂记录。沿用原 DTO 字段，不加字段（名人堂信封整份算摘要，加字段会让旧档校验失败）。</summary>
        private ModeHHallOfFameRecordDto BuildGroupHallOfFameRecord()
        {
            if (_season == null || _runState == null) return null;
            int wins = 0;
            int matches = 0;
            int maxOddsWin = 0;
            Dictionary<string, int> picks = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> tokens = new List<string>();
            if (_season.matchReports != null)
            {
                for (int i = 0; i < _season.matchReports.Count; i++)
                {
                    ModeHMatchReportDto report = _season.matchReports[i];
                    if (report == null) continue;
                    matches++;
                    tokens.Add(report.resultToken ?? string.Empty);
                    bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
                    if (won)
                    {
                        wins++;
                        if (report.lockedOdds > maxOddsWin) maxOddsWin = report.lockedOdds;
                    }
                    if (report.entrantIds == null) continue;
                    for (int j = 0; j < report.entrantIds.Count; j++)
                    {
                        string key = report.entrantIds[j];
                        if (string.IsNullOrEmpty(key)) continue;
                        int count;
                        picks.TryGetValue(key, out count);
                        picks[key] = count + (won ? 3 : 1);
                    }
                }
            }
            string mvp = string.Empty;
            int best = 0;
            foreach (KeyValuePair<string, int> pair in picks)
            {
                if (pair.Value > best || (pair.Value == best && string.CompareOrdinal(pair.Key, mvp) < 0))
                {
                    best = pair.Value;
                    mvp = pair.Key;
                }
            }

            ModeHHallOfFameRecordDto record = new ModeHHallOfFameRecordDto();
            record.hallOfFameId = "hof|" + _runState.RunId;
            record.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            record.seasonVersion = ModeHConfig.CurrentSchemaVersion;
            record.championProfileSnapshot = null;
            record.aliasKey = string.Empty;
            record.archetypeId = ModeHGroupHallOfFame.ArchetypeTag;
            record.temperamentId = string.Empty;
            record.quirkId = ModeHGroupHallOfFame.EncodeScore(wins, matches, ComputeGroupSeasonNet());
            record.anomalyId = string.Empty;
            record.signatureCommandId = mvp;
            record.scarIds = new List<string>();
            record.matchReportIds = tokens;
            record.substituteHistory = new List<string>();
            record.maxOddsWin = maxOddsWin;
            record.maxVirtualStakeWin = 0;
            record.finalVirtualStakeCredits = wins;
            record.maxRealStakeWin = 0;
            record.createdUtc = DateTime.UtcNow.ToString("O");
            record.gameBuildSignature = _season.gameBuildSignature ?? string.Empty;
            record.modBuildSignature = _season.modBuildSignature ?? string.Empty;
            return record;
        }

        /// <summary>名人堂页：群战赛季按胜场、净赚排名，本季那一行高亮；旧版冠军记录排在后面。</summary>
        private ModeHPageContent BuildGroupHallOfFamePageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_HallOfFame");
            string currentId = _runState != null ? "hof|" + _runState.RunId : null;
            try
            {
                List<ModeHHallOfFameRecordDto> records = ModeHHallOfFamePersistence.GetRecords();
                List<ModeHHallOfFameRecordDto> ranked = ModeHGroupHallOfFame.Rank(records);
                int rank = 0;
                int currentRank = 0;
                for (int i = 0; i < ranked.Count; i++)
                {
                    ModeHHallOfFameRecordDto record = ranked[i];
                    int wins, matches;
                    long net;
                    if (!ModeHGroupHallOfFame.TryDecodeScore(record, out wins, out matches, out net)) continue;
                    rank++;
                    bool mine = string.Equals(record.hallOfFameId, currentId, StringComparison.Ordinal);
                    if (mine) currentRank = rank;
                    ModeHCardData card = new ModeHCardData();
                    card.Title = L10n.T("第 " + rank + " 名", "#" + rank) + " · "
                        + L10n.T("押中 " + wins + " / " + matches + " 场", wins + " / " + matches + " called");
                    card.Subtitle = L10n.T("净赚 ", "Net ") + FormatSignedMoney(net);
                    List<string> body = new List<string>();
                    if (!string.IsNullOrEmpty(record.signatureCommandId))
                        body.Add(L10n.T("头号功臣：", "MVP: ") + ResolveOfficialBossName(record.signatureCommandId));
                    if (record.maxOddsWin > 1)
                        body.Add(L10n.T("赢过 " + FormatPayoutMultiplier(record.maxOddsWin) + " 的冷门",
                            "Won an upset at " + FormatPayoutMultiplier(record.maxOddsWin)));
                    if (!string.IsNullOrEmpty(record.createdUtc) && record.createdUtc.Length >= 10)
                        body.Add(record.createdUtc.Substring(0, 10));
                    card.Body = string.Join(L10n.T("；", "; "), body.ToArray());
                    card.PortraitKey = record.signatureCommandId;
                    card.IsSelected = mine;
                    if (mine) card.SelectedBadge = L10n.T("本季", "This season");
                    page.Cards.Add(card);
                }
                int legacy = 0;
                for (int i = 0; records != null && i < records.Count; i++)
                {
                    if (ModeHGroupHallOfFame.IsGroupRecord(records[i])) continue;
                    ModeHCardData card = BuildHallOfFameCard(records[i]);
                    if (card == null) continue;
                    card.Subtitle = L10n.T("往届单挑冠军", "Past duel champion")
                        + (string.IsNullOrEmpty(card.Subtitle) ? string.Empty : " · " + card.Subtitle);
                    page.Cards.Add(card);
                    legacy++;
                }
                page.Body = rank > 0
                    ? (currentRank > 0
                        ? L10n.T("本季排第 " + currentRank + " 名（共 " + rank + " 季）", "This season ranks #" + currentRank + " of " + rank)
                        : L10n.T("本季没挤进前 " + ModeHConfig.MaxHallOfFameRecords + " 名", "This season missed the top " + ModeHConfig.MaxHallOfFameRecords))
                    : L10n.T("名人堂还是空的", "The Hall of Fame is empty");
            }
            catch (Exception e)
            {
                page.Body = L10n.T("名人堂记录暂时读取不到", "Hall of Fame records are unavailable");
                LogFailure("group_hall_of_fame_records", e);
            }
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("结束赛季", "End season"),
                IsPrimary = true,
                OnClick = delegate { FinishSeason("hall_of_fame_ack"); },
            });
            return page;
        }

        #endregion

        #region 回收

        private void ReleaseGroupBattle()
        {
            ModeHGroupBattle battle = _groupBattle;
            _groupBattle = null;
            if (battle == null) return;
            try { battle.Release(); }
            catch (Exception e) { LogFailure("group_battle_release", e); }
        }

        #endregion
    }
}

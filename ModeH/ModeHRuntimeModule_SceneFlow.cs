// ============================================================================
// ModeHRuntimeModule_SceneFlow.cs - Mode H 场景到达编排与关停（设计提案 §17.1、§18.2、§18.3、§19.2）
// ============================================================================
// 本文件补上 ModeHRuntimeModule 四个 partial 接入点中的两个：
//   OnSceneLoadedInternal —— 场景到达后的完整开局链；
//   ShutdownRuntimeInternal —— §18.3 冻结的十步退出顺序。
//
// 与 Config/ConfigModeG.cs 同理，拆成独立 partial 只为单文件行数预算
// （LargeFileBudgetGuard 硬上限 1200 行）；语义上就是 ModeHRuntimeModule 本身。
//
// 本文件同时是**运行时字段的唯一声明处**：Season、租约、认证、UI 等运行期对象都在这里，
// 其它 partial 只读写它们，不再各自持有副本。
//
// 硬约束：
//   - 双租约顺序冻结：先 arena isolation，再 spectator；释放严格逆序（§19.2）；
//   - arena 租约一旦清过原生敌人就**不得**回落 Legacy BossRush，只能退款离场（§19.2）；
//   - 认证通过后才用固定 runSeed 原子创建首份 lifecycle=Drafting 的 Season 并读回，
//     任何一步失败都退款、安全离场并回到 None，不留残缺 run（§18.2）；
//   - 全程 no-throw：异常一律转 RequestExit(TechnicalAbort)，不得拖崩宿主。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        #region 运行时字段（唯一声明处）

        /// <summary>当前赛季 payload 的内存副本。创建 Drafting 之后才非空。</summary>
        private ModeHSeasonDto _season;

        /// <summary>本局地图点位快照。</summary>
        private ModeHSupportedMap _map;

        /// <summary>原图隔离租约（先取先放的外层）。</summary>
        private ModeHArenaIsolationLease _arenaLease;

        /// <summary>观战租约（后取先放的内层）。</summary>
        private ModeHSpectatorLease _spectatorLease;

        /// <summary>生产认证实例（协程持有者）。</summary>
        private ModeHProductionCertification _certification;

        /// <summary>最近一次认证是否命中持久化缓存（F3 验收只读）。</summary>
        private bool _lastCertificationUsedCache;

        /// <summary>认证协程句柄，用于关停时取消。</summary>
        private Coroutine _certificationRoutine;

        /// <summary>Season 内存副本是否有未落盘的改动。</summary>
        private bool _seasonDirty;

        /// <summary>是否已停止接收新命令（关停第一步）。</summary>
        private bool _commandsClosed;

        /// <summary>低频巡检累加器：租约完整性每秒查一次，不每帧扫。</summary>
        private float _leaseCheckAccumulator;

        /// <summary>当前比赛的战斗控制与遥测；只在 MatchFighting 生命周期非空。</summary>
        private ModeHCombatControl _combatControl;
        private ModeHCombatTelemetry _combatTelemetry;

        /// <summary>战场快照上下文及其复用集合，避免每帧创建容器。</summary>
        private ModeHBattleSnapshotContext _battleSnapshotContext;
        private readonly List<string> _pendingBatchKeys = new List<string>();
        private readonly List<ModeHSnapshotEnemyInput> _snapshotEnemies =
            new List<ModeHSnapshotEnemyInput>();
        private readonly ModeHSnapshotEnemyInput _snapshotEntrant = new ModeHSnapshotEnemyInput();

        /// <summary>当前比赛的运行时角色、身份映射与虚拟 kit 事务。</summary>
        private ModeHSpawnHandle _activeFighterHandle;
        private ModeHParticipantRef _starterParticipant;
        private ModeHParticipantRef _relayParticipant;
        private readonly List<ModeHParticipantRef> _enemyParticipants =
            new List<ModeHParticipantRef>();
        private ModeHKitApplication _activeKitApplication;

        /// <summary>接力者按需生成；先发倒地前不预热第二个角色。</summary>
        private ModeHSpawnTransaction _relaySpawnTransaction;
        private Coroutine _relaySpawnRoutine;

        /// <summary>看盘阶段冻结的公开报价、虚拟下注与最近结算。</summary>
        private ModeHOddsQuote _currentOddsQuote;
        private int _selectedVirtualStake;
        private string _starterDisplayName;
        private string _relayDisplayName;
        private ModeHMatchReportDto _lastSettlementReport;

        /// <summary>
        /// 本场完整休息（带伤且从未登场、赛后已解除带伤）的选手 ID。
        /// **纯运行时**：不进任何 DTO、不落盘——ModeHCanonicalDigest 会把持久化 DTO 的
        /// 新增字段一起算进摘要，加字段等于让已存赛季 VerifyDigest 失败并进写屏障。
        /// 只服务结算页展示，每场结算前由 SettleMatch 清空。
        /// </summary>
        private readonly List<string> _restedProfileIds = new List<string>();
        private ModeHSeasonRewardOperationDto _lastRewardOperation;

        #endregion

        #region 场景到达

        /// <summary>
        /// 场景到达后的 Mode H 开局链。
        ///
        /// 只有命中本次冻结的入场意图才会真正开局；其它场景一律只做清理，
        /// 绝不因为「路过一张受支持地图」就抢占它。
        /// </summary>
        partial void OnSceneLoadedInternal(SceneRuntimeContext context)
        {
            // 已有活动 run 时先做归属校验：离开本局场景就按 §18.3 安全离场
            if (HasActiveRun && _arenaLease != null && _arenaLease.IsActive)
            {
                if (!string.Equals(context.SceneName, _runState.SceneName, StringComparison.Ordinal))
                {
                    RequestExit(ModeHExitReason.SceneGenerationMismatch, "scene_left_active_run");
                }
                return;
            }

            if (!IsEnabled) return;

            string sceneId = ResolveSceneId(context);
            int frozenGeneration;
            if (!BossRushMapSelectionHelper.TryMatchModeHSceneIntent(
                    context.SceneName, sceneId, out frozenGeneration))
            {
                // 不是本次冻结的目标场景：什么都不做（不推进任何状态、不抢占地图）
                return;
            }

            // 命中了一次**新的**入场意图：船票已经预扣，这里必须复位上一局关停时落下的闩锁。
            // 否则模块对新一局的全部命令静默早返，玩家站在没有任何模式接管的原图上，
            // 票还被吞了（CR-2026-08-29-011）。闩锁复位必须发生在意图匹配之后，
            // 才不会因为「路过一张受支持地图」就把关停状态解除。
            BeginNewRunSession();

            BeginSeasonSetup(context.SceneName, sceneId);
        }

        /// <summary>
        /// 新一局开始前复位「上一局残留」。关停闩锁与每局一次性字段都在这里归零，
        /// 保证同一次游戏会话里可以连续开多局（§18.3 的关停是幂等的，但不是永久的）。
        /// </summary>
        private void BeginNewRunSession()
        {
            _commandsClosed = false;
            _shutdownCompleted = false;
            _lastExitReasonId = null;
            _pendingContractMainId = null;
            _recoveryDriveStateSequence = -1;
            _leaseCheckAccumulator = 0f;
            _seasonDirty = false;
            _selectedVirtualStake = 0;
            _currentOddsQuote = null;
            _starterDisplayName = null;
            _relayDisplayName = null;
            _lastSettlementReport = null;
            _lastRewardOperation = null;
        }

        private static string ResolveSceneId(SceneRuntimeContext context)
        {
            try
            {
                ModeHSupportedMap map;
                if (ModeHMapSupportRegistry.TryGetMap(context.SceneName, out map) && map != null)
                {
                    return map.SceneId;
                }
            }
            catch (Exception)
            {
                // 取不到 sceneId 时按空串处理：TryMatchModeHSceneIntent 对空 id 宽松匹配
            }
            return string.Empty;
        }

        /// <summary>
        /// 开局链前半段（同步部分）：建 run owner → 取双租约 → 交给认证协程。
        /// 任何一步失败都走同一个 AbortSetup 出口。
        /// </summary>
        private void BeginSeasonSetup(string sceneName, string sceneId)
        {
            try
            {
                if (!ModeHMapSupportRegistry.TryGetMap(sceneName, out _map) || _map == null)
                {
                    AbortSetup("map_unsupported", false);
                    return;
                }

                // run owner：runSeed 一旦冻结就贯穿整局，所有确定性抽取都从它派生
                string runId = ComposeRunId(sceneName, _sceneGeneration);
                long runSeed = ComposeRunSeed(runId);
                _runState = new ModeHRunState(runId, runSeed, sceneName, _sceneGeneration);
                ModeHRuntimeGates.SetRunOwnerActive(true);

                if (!TryTransition(ModeHLifecycle.None, ModeHLifecycle.EntryIntent, "scene_intent_matched")
                    || !TryTransition(ModeHLifecycle.EntryIntent, ModeHLifecycle.SceneLoading, "scene_arrived"))
                {
                    AbortSetup("lifecycle_entry_rejected", false);
                    return;
                }

                // §19.2 顺序冻结：先隔离原图，再接管玩家身体
                _arenaLease = new ModeHArenaIsolationLease();
                string failureReasonId;
                if (!_arenaLease.TryAcquire(sceneName, _sceneGeneration, _runState.OwnerToken,
                        out failureReasonId))
                {
                    AbortSetup(failureReasonId != null ? failureReasonId : "arena_lease_failed", false);
                    return;
                }

                _spectatorLease = new ModeHSpectatorLease();
                if (!_spectatorLease.TryAcquire(_map.SpectatorPos, _sceneGeneration,
                        _runState.OwnerToken, out failureReasonId))
                {
                    AbortSetup(failureReasonId != null ? failureReasonId : "spectator_lease_failed", true);
                    return;
                }

                if (!TryTransition(ModeHLifecycle.SceneLoading, ModeHLifecycle.ProductionCertifying,
                        "leases_acquired"))
                {
                    AbortSetup("lifecycle_certifying_rejected", true);
                    return;
                }

                StartCertification();
            }
            catch (Exception e)
            {
                LogFailure("season_setup", e);
                AbortSetup("season_setup_exception", true);
            }
        }

        /// <summary>
        /// 本局唯一 runId。只用场景名 + generation：同一存档槽内单调递增的 generation
        /// 保证不同局不会撞 id，也不引入时钟依赖（存档回放要可复现）。
        /// </summary>
        private static string ComposeRunId(string sceneName, int sceneGeneration)
        {
            return "mh_" + (sceneName ?? "unknown") + "_" + sceneGeneration.ToString("x");
        }

        /// <summary>runId 派生固定 runSeed：同一局的全部确定性抽取都以它为根。</summary>
        private static long ComposeRunSeed(string runId)
        {
            return unchecked((long)ModeHSeedStream.Fnv1a64(runId ?? string.Empty));
        }

        #endregion

        #region 生产认证

        private void StartCertification()
        {
            _certification = new ModeHProductionCertification();
            _lastCertificationUsedCache = false;
            if (_owner == null)
            {
                AbortSetup("owner_missing", true);
                return;
            }

            // 首次进入时 ProductionKeys 尚未物化；认证输入必须来自静态生产目录。
            // 有效缓存会把报告重新物化进注册表，因此仍需在双租约和地图审计之后命中。
            if (_certification.TryUseCachedReport(ModeHRuntimeGates.SlotGeneration))
            {
                _lastCertificationUsedCache = true;
                CreateDraftingSeason(_certification.Report);
                return;
            }

            List<string> keys = ModeHProfileRegistry.GetProductionStableKeys();
            if (keys == null || keys.Count == 0)
            {
                AbortSetup("production_catalog_empty", true);
                return;
            }

            ModeHCertificationResult result = new ModeHCertificationResult();

            // 认证要真刀真枪跑一遍生成/战斗/掉落，只能走协程；诊断层给玩家一个可取消的进度页
            try
            {
                EnsureUi();
                if (_ui != null) _ui.EnsureDiagnostics(CancelSetupFromDiagnostics);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 认证诊断页创建失败: " + e.Message);
            }

            _certificationRoutine = _owner.StartCoroutine(DriveCertification(keys, result));
        }

        internal bool LastCertificationUsedCache { get { return _lastCertificationUsedCache; } }

        /// <summary>Dev 验收专用：把 Drafting 测试赛季归档成 None，再走正常关停。</summary>
        internal bool DebugFinishValidationSeason()
        {
            if (!ModBehaviour.DevModeEnabled || _runState == null) return false;
            if (_runState.Lifecycle != ModeHLifecycle.Drafting) return false;
            if (!TryTransition(ModeHLifecycle.Drafting, ModeHLifecycle.None, "f3_validation")) return false;
            bool persisted = TryPersistSeason("f3_validation_finished");
            RequestExit(ModeHExitReason.SeasonComplete, "f3_validation_finished");
            return persisted;
        }

        /// <summary>
        /// Dev 验收专用：强制重置运行时状态但不切场景。
        /// ValidationSafeCleanup 调用此方法而非 RequestExit，避免 Mode H 超时失败时切回基地打断后续用例。
        /// </summary>
        internal void ForceResetStateForValidation()
        {
            if (!ModBehaviour.DevModeEnabled) return;
            try
            {
                // 复用完整逆序释放：先停认证/生成，再清选手、租约和 UI。
                // 清空状态前保留押品返还所需的 runSeed / matchIndex。
                TryReturnRealStakeOnAbort("f3_validation_cleanup");
                ReleaseRuntimeObjects();
                if (BossRushMapSelectionHelper.HasPendingModeHEntryIntent()) ModeHEntry.CancelPendingEntry();
                _runState = null;
                _season = null;
                _seasonDirty = false;
                _map = null;
                ModeHRuntimeGates.SetRunOwnerActive(false);
            }
            catch (Exception e)
            {
                LogFailure("force_reset_validation", e);
            }
        }

        private IEnumerator DriveCertification(List<string> keys, ModeHCertificationResult result)
        {
            long ownerToken = _runState != null ? _runState.OwnerToken : 0L;
            int generation = _sceneGeneration;

            IEnumerator inner = _certification.Run(keys, _map, result);
            while (true)
            {
                bool moveNext;
                try
                {
                    moveNext = inner.MoveNext();
                }
                catch (Exception e)
                {
                    LogFailure("certification", e);
                    AbortSetup("certification_exception", true);
                    yield break;
                }
                if (!moveNext) break;

                // 认证期间玩家可能已经离场/切图：每帧比对 owner 与 generation
                if (!IsCallbackStillValid(ownerToken, generation))
                {
                    yield break;
                }
                if (_ui != null)
                {
                    try { _ui.UpdateDiagnostics(DescribeCertificationProgress(result)); }
                    catch (Exception) { /* 诊断页失败不影响认证本身 */ }
                }
                // 必须把 inner.Current 透传出去：Run 内部是 `yield return CertifyKey(...)`，
                // 即 yield 出子 IEnumerator 交给 Unity 协程调度器递归驱动。曾经在这里写死
                // `yield return null`，子协程被创建但一次都没 MoveNext——逐 key 的生成、
                // 阵营核对、受控击杀、RecordPassed/RecordRejected 全部没执行，
                // 每个 key 都打出一条空原因的「认证拒绝」，_records 恒空，
                // 门槛必然撞 MinProductionCandidateCount 失败 → Mode H 完全无法开局。
                // Current 为 null 时语义与 `yield return null` 等价（等一帧），
                // 所以每帧的 owner/generation 校验与诊断页刷新节奏不变。
                yield return inner.Current;
            }

            _certificationRoutine = null;
            if (!IsCallbackStillValid(ownerToken, generation)) yield break;

            try { if (_ui != null) _ui.DestroyDiagnostics(); }
            catch (Exception) { /* 销毁失败不阻断后续 */ }

            if (!result.Completed || !result.Passed)
            {
                AbortSetup(result.FailureReasonId != null
                    ? result.FailureReasonId
                    : "certification_failed", true);
                yield break;
            }

            try
            {
                ModeHPresetRegistry.MaterializeFromReport(result.Report);
            }
            catch (Exception e)
            {
                LogFailure("preset_materialize", e);
                AbortSetup("preset_materialize_failed", true);
                yield break;
            }

            string cacheError;
            if (!ModeHSaveFlushCoordinator.RequestCertificationCacheWrite(
                    result.Report, ModeHRuntimeGates.SlotGeneration, out cacheError))
            {
                // 缓存只负责下一次入场加速，不能让已通过的认证反过来阻断本局。
                ModBehaviour.DevLog("[ModeH] [WARNING] 生产认证缓存写入失败，本局继续: "
                    + (cacheError ?? "unknown"));
            }

            CreateDraftingSeason(result.Report);
        }

        /// <summary>认证进度文案。只读 result，不分配大对象（每帧调一次）。</summary>
        private static string DescribeCertificationProgress(ModeHCertificationResult result)
        {
            if (result == null) return string.Empty;
            if (!string.IsNullOrEmpty(result.FailureReasonId)) return result.FailureReasonId;
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "Diag_Progress");
        }

        /// <summary>诊断页的取消按钮：等同于一次带退款的安全离场。</summary>
        private void CancelSetupFromDiagnostics()
        {
            try
            {
                if (_certification != null) _certification.Cancel();
                AbortSetup("player_cancelled_certification", true);
            }
            catch (Exception e)
            {
                LogFailure("cancel_setup", e);
            }
        }

        /// <summary>延迟回调三重门控：owner token + scene generation + 未关停。</summary>
        private bool IsCallbackStillValid(long ownerToken, int generation)
        {
            if (_shutdownCompleted || _commandsClosed) return false;
            if (_runState == null) return false;
            if (_runState.OwnerToken != ownerToken) return false;
            if (_sceneGeneration != generation) return false;
            return true;
        }

        #endregion

        #region 首份 Season 创建

        /// <summary>
        /// 认证通过后原子创建首份 lifecycle=Drafting 的 Season 并读回。
        /// 读回失败必须退款离场：宁可玩家重来一次，也不能带着一份写不下去的赛季继续。
        /// </summary>
        private void CreateDraftingSeason(ModeHProductionCertificationDto report)
        {
            try
            {
                ModeHSeasonDto season = new ModeHSeasonDto();
                season.schemaVersion = ModeHConfig.CurrentSchemaVersion;
                season.signatureAlgorithmVersion = ModeHConfig.CurrentSignatureAlgorithmVersion;
                season.slotGeneration = ModeHRuntimeGates.SlotGeneration;
                season.productionCertificationSnapshot = report;

                string signature;
                string signatureError;
                if (ModeHCanonicalDigest.TryGetModBuildSignature(out signature, out signatureError))
                {
                    season.modBuildSignature = signature;
                }
                if (ModeHCanonicalDigest.TryGetGameBuildSignature(out signature, out signatureError))
                {
                    season.gameBuildSignature = signature;
                }
                season.contentCatalogSignature = ModeHContentCatalog.ContentCatalogSignature;

                season.profiles = new List<ModeHProfileDto>();
                season.draftCandidateProfileIds = new List<string>();
                season.echoAssignments = new List<ModeHEchoAssignmentDto>();
                season.matchReports = new List<ModeHMatchReportDto>();
                season.seasonRewardOperations = new List<ModeHSeasonRewardOperationDto>();
                season.appliedEventTokenIds = new List<string>();
                season.unlockedKitIds = ModeHLoadoutKitRegistry.GetStarterKitIds();
                season.virtualStakeCredits = ModeHConfig.InitialVirtualStakeCredits;
                season.reservedVirtualStake = 0;

                _season = season;

                if (!TryTransition(ModeHLifecycle.ProductionCertifying, ModeHLifecycle.Drafting,
                        "certification_passed"))
                {
                    AbortSetup("lifecycle_drafting_rejected", true);
                    return;
                }

                // OnTransitionApplied 已把 runState 投影进 _season，这里做首次原子写入 + 读回
                string error;
                if (!ModeHSaveFlushCoordinator.RequestSeasonWrite(_season, out error))
                {
                    AbortSetup(error != null ? "season_write_failed:" + error : "season_write_failed", true);
                    return;
                }
                if (ModeHProfilePersistence.LoadCurrent() == null)
                {
                    AbortSetup("season_readback_failed", true);
                    return;
                }
                _seasonDirty = false;

                // 首份赛季写入并读回后，入场已完成：消费意图与预扣票所有权。
                // 失败路径仍在此前保留退款凭据；成功后不能在下一次经过此地图时重开赛季。
                ModeHEntry.CancelPendingEntry();
                ModBehaviour.DevLog("[ModeH] 赛季已创建 runId=" + _runState.RunId
                    + " seed=" + _runState.RunSeed.ToString("x"));
            }
            catch (Exception e)
            {
                LogFailure("create_season", e);
                AbortSetup("create_season_exception", true);
            }
        }

        #endregion

        #region 失败出口

        /// <summary>
        /// 开局失败的统一出口：退款 + 安全离场 + 回到无 run 状态。
        ///
        /// leasesTaken 为 true 表示已经动过场景（至少取了 arena 租约），此时必须离场——
        /// arena 租约清过原生敌人的场景绝不能原地回落 Legacy BossRush（§19.2）。
        /// </summary>
        private void AbortSetup(string reasonId, bool leasesTaken)
        {
            ModBehaviour.DevLog("[ModeH] 开局中止: " + (reasonId ?? "unknown"));
            _lastExitReasonId = reasonId;

            bool mustExitScene = leasesTaken
                || (_arenaLease != null && _arenaLease.HasClearedNativeEnemies);

            // 真实押品必须先还回去，再清 _runState —— 返还计划要用 RunSeed / MatchIndex。
            // 中止不没收任何东西；无 journal 时是 no-op。
            TryReturnRealStakeOnAbort("abort_setup:" + (reasonId != null ? reasonId : "unknown"));

            try { ReleaseRuntimeObjects(); }
            catch (Exception e) { LogFailure("abort_release", e); }

            _season = null;
            _seasonDirty = false;
            _runState = null;
            _map = null;
            ModeHRuntimeGates.SetRunOwnerActive(false);

            // 玩家带着船票专程进了图，被无声传回基地是不可接受的：
            // 先给一句解释 + 退票告知，再执行退款离场（CR-2026-08-29-013）。
            ShowAbortMessage(reasonId);

            try { ModeHEntry.AbortAndRefund(_owner, reasonId, mustExitScene); }
            catch (Exception e) { LogFailure("abort_refund", e); }
        }

        /// <summary>
        /// 中止路径的真实押品完整返还。无 active journal 时是 no-op。
        ///
        /// 失败不抛、不阻断退款离场：journal 会留在非终态，下次进入时
        /// RecomputeSlotConsistency 立起 external-asset 闸并把处置权交给恢复壳，
        /// 那比在中止路径上卡住玩家更好。
        /// </summary>
        private void TryReturnRealStakeOnAbort(string context)
        {
            try
            {
                if (ModeHWarehouseStakeJournal.Active == null) return;
                long runSeed = _runState != null ? _runState.RunSeed : 0L;
                int matchIndex = _runState != null ? _runState.MatchIndex : 0;

                string failureReasonId;
                if (!ModeHRealStakeService.TryAbortReturn(runSeed, matchIndex, out failureReasonId))
                {
                    ModBehaviour.CriticalLog(
                        "[ModeH] [WARNING] 中止返还真实押品未完成（" + context + "）: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                }
            }
            catch (Exception e)
            {
                LogFailure("abort_real_stake_return", e);
            }
        }

        /// <summary>开局中止的玩家可见提示：一句归类文案 + 退票告知。失败不阻断退款。</summary>
        private void ShowAbortMessage(string reasonId)
        {
            try
            {
                if (_owner == null) return;
                string text = L10n.T(ModeHConfig.LocalizationKeyPrefix + ResolveAbortMessageKey(reasonId))
                    + " " + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Unavailable_TicketRefunded");
                _owner.ShowMessage(text);
            }
            catch (Exception)
            {
                // 提示失败不得影响退款与离场
            }
        }

        /// <summary>
        /// 把 AbortSetup 的内部 reasonId 归类成已注入的文案 key。
        /// 未登记的原因回落 Abort_Generic —— 宁可给一句笼统解释，也不能让玩家看到
        /// 未注入 key 的 *星号原文*。
        /// </summary>
        private static string ResolveAbortMessageKey(string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId)) return "Abort_Generic";

            if (reasonId.IndexOf("map_unsupported", StringComparison.Ordinal) >= 0)
            {
                return "Abort_MapUnsupported";
            }
            if (reasonId.IndexOf("player_cancelled", StringComparison.Ordinal) >= 0)
            {
                return "Abort_Cancelled";
            }
            if (reasonId.IndexOf("lease", StringComparison.Ordinal) >= 0)
            {
                return "Abort_Lease";
            }
            if (reasonId.IndexOf("certification", StringComparison.Ordinal) >= 0)
            {
                return "Abort_Certification";
            }
            if (reasonId.IndexOf("season_write", StringComparison.Ordinal) >= 0
                || reasonId.IndexOf("season_readback", StringComparison.Ordinal) >= 0
                || reasonId.IndexOf("create_season", StringComparison.Ordinal) >= 0)
            {
                return "Abort_Save";
            }
            if (reasonId.IndexOf("preset", StringComparison.Ordinal) >= 0
                || reasonId.IndexOf("production_pool", StringComparison.Ordinal) >= 0)
            {
                return "Abort_Content";
            }
            return "Abort_Generic";
        }

        #endregion

        #region 关停（§18.3 十步冻结顺序）

        partial void ShutdownRuntimeInternal(ModeHExitReason reason, string reasonId)
        {
            // 1. 停止接收新命令
            _commandsClosed = true;

            // 1.5 真实押品返还：必须在清 _runState 之前，返还计划要用 RunSeed/MatchIndex。
            // _escrowItems 是纯内存 List，快照只能用于核对、无法重建 Item，所以关停时若不
            // 返还，物品就随进程永久消失，而恢复壳因 IsSlotConsistent=false 会把所有补救
            // 按钮置灰，玩家除删档外无出路。TryReturnRealStakeOnAbort 幂等且无 journal 时 no-op。
            TryReturnRealStakeOnAbort("shutdown:" + (reasonId != null ? reasonId : "unknown"));

            // 2-5 + 6-7（租约逆序）+ 9（UI）：全部收敛到同一个幂等清理入口，
            // 保证 AbortSetup 与正常关停走完全一样的顺序。
            try { ReleaseRuntimeObjects(); }
            catch (Exception e) { LogFailure("shutdown_release", e); }

            // 8. 完成 Season flush：只 stage，物理落盘由外层 TryFlushOnHostDestroy 统一做
            try
            {
                if (_seasonDirty && _season != null)
                {
                    string error;
                    ModeHProfilePersistence.StageWrite(_season, out error);
                    _seasonDirty = false;
                }
            }
            catch (Exception e)
            {
                LogFailure("shutdown_stage_season", e);
            }

            _season = null;
            _map = null;
            _runState = null;
            // 10. 释放 runtime owner 由外层 ShutdownRuntime 统一做（SetRunOwnerActive(false)）
        }

        /// <summary>
        /// 运行期对象的幂等清理。顺序按 §18.3：
        /// 生成队列 → 战斗计时/adapter → 临时角色与 kit → spectator → arena → UI。
        /// 释放严格与取得相反。
        /// </summary>
        private void ReleaseRuntimeObjects()
        {
            // 2. 停止生成队列与认证协程
            try
            {
                if (_certificationRoutine != null && _owner != null)
                {
                    _owner.StopCoroutine(_certificationRoutine);
                }
            }
            catch (Exception) { /* 协程已结束 */ }
            _certificationRoutine = null;

            try { if (_certification != null) _certification.Cancel(); }
            catch (Exception) { /* 认证已停 */ }
            _certification = null;

            ReleaseMatchRuntime();

            // 6. 释放 spectator lease（后取先放）
            try
            {
                if (_spectatorLease != null) _spectatorLease.Release(_sceneGeneration);
            }
            catch (Exception e) { LogFailure("release_spectator", e); }
            _spectatorLease = null;

            // 7. 释放 arena isolation lease
            try
            {
                if (_arenaLease != null) _arenaLease.Release(_sceneGeneration);
            }
            catch (Exception e) { LogFailure("release_arena", e); }
            _arenaLease = null;

            // 9. 销毁 UI
            try { DestroyUi(); }
            catch (Exception e) { LogFailure("destroy_ui", e); }

            _leaseCheckAccumulator = 0f;
        }

        #endregion
    }
}

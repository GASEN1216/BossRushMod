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
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private bool _certificationFromF3;

        // 原生 sceneLoaded 早于官方子场景传送结束。新开局/续赛共用这一个等待 owner。
        private Coroutine _sceneReadyRoutine;
        private int _sceneReadyRequestSerial;
        private int _sceneReadyIntentGeneration;
        private const float SceneReadyTimeoutSeconds = 30f;

        /// <summary>Season 内存副本是否有未落盘的改动。</summary>
        private bool _seasonDirty;

        /// <summary>是否已停止接收新命令（关停第一步）。</summary>
        private bool _commandsClosed;

        /// <summary>低频巡检累加器：租约完整性每秒查一次，不每帧扫。</summary>
        private float _leaseCheckAccumulator;

        /// <summary>
        /// ERROR 互换期间是否已让渡输入给玩家（见 SyncErrorSwapInputYield）。
        /// 与租约自己的 _inputYielded 是两份记账：这一份是模块的「我请求过让渡」，
        /// 那一份是租约的「我确实改过 InputManager」，租约不在时这一份也要能归零。
        /// </summary>
        private bool _errorSwapInputYielded;

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

        /// <summary>选人页两步选择的临时状态：首发锁定后只刷新其余席位。</summary>
        private string _draftPrimaryProfileId;
        private string _draftRelayProfileId;
        private int _draftRefreshCount;
        private const int DraftMaxRefreshes = 3;

        /// <summary>押注开盘揭晓期间暂缓生成战场，动画结束后由宿主 tick 继续。</summary>
        private bool _waitingForBetReveal;

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
            if (TryHandleSeasonResumeScene(context)) return;
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

            ScheduleSceneReadyWait(context, sceneId, frozenGeneration, false);
        }

        private void ScheduleSceneReadyWait(SceneRuntimeContext context, string sceneId,
            int intentGeneration, bool resume)
        {
            CancelSceneReadyWait();
            int request = _sceneReadyRequestSerial;
            _sceneReadyIntentGeneration = intentGeneration;
            try
            {
                _sceneReadyRoutine = _owner.StartCoroutine(WaitForModeHSceneReady(context, sceneId,
                    intentGeneration, resume, request, _sceneGeneration, ModeHRuntimeGates.SlotGeneration,
                    resume && _runState != null ? _runState.OwnerToken : 0L));
            }
            catch (Exception e)
            {
                LogFailure("scene_ready_schedule", e);
                FailSceneReadyWait(resume, "scene_ready_schedule_failed");
            }
        }

        private IEnumerator WaitForModeHSceneReady(SceneRuntimeContext context, string sceneId,
            int intentGeneration, bool resume, int request, int sceneGeneration, int slotGeneration,
            long ownerToken)
        {
            // 至少让原生 sceneLoaded 返回；AfterInit 已确认官方最终落点，再跨帧稳定检查。
            yield return null;
            float elapsed = 0f;
            bool readyLastFrame = false;
            while (IsSceneReadyRequestCurrent(context.SceneName, sceneId, intentGeneration,
                resume, request, sceneGeneration, slotGeneration, ownerToken))
            {
                if (!context.Scene.IsValid() || !context.Scene.isLoaded) break;
                bool ready = IsModeHSceneReady(context.Scene);
                if (ready && readyLastFrame)
                {
                    _sceneReadyRoutine = null;
                    _sceneReadyIntentGeneration = 0;
                    if (resume) CompleteSeasonResumeScene();
                    else BeginSeasonSetup(context.SceneName, sceneId);
                    yield break;
                }
                readyLastFrame = ready;
                if (!BossRushUI.IsGamePaused()) elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
                if (elapsed >= SceneReadyTimeoutSeconds) break;
                yield return null;
            }

            // 旧请求不得清新句柄或退新一轮的票；换槽后的资产也不在这里操作。
            if (request != _sceneReadyRequestSerial) yield break;
            _sceneReadyRoutine = null;
            _sceneReadyIntentGeneration = 0;
            int currentIntent;
            if (slotGeneration != ModeHRuntimeGates.SlotGeneration
                || !BossRushMapSelectionHelper.TryMatchModeHSceneIntent(
                    context.SceneName, sceneId, out currentIntent)
                || currentIntent != intentGeneration) yield break;
            FailSceneReadyWait(resume, "scene_not_ready");
        }

        private bool IsSceneReadyRequestCurrent(string sceneName, string sceneId, int intentGeneration,
            bool resume, int request, int sceneGeneration, int slotGeneration, long ownerToken)
        {
            if (_owner == null || !IsEnabled || _shutdownCompleted || _commandsClosed
                || request != _sceneReadyRequestSerial || sceneGeneration != _sceneGeneration
                || slotGeneration != ModeHRuntimeGates.SlotGeneration) return false;
            int currentIntent;
            if (!BossRushMapSelectionHelper.TryMatchModeHSceneIntent(sceneName, sceneId, out currentIntent)
                || currentIntent != intentGeneration) return false;
            return !resume || IsSeasonResumeRequestCurrent(ownerToken, slotGeneration, intentGeneration);
        }

        private static bool IsModeHSceneReady(Scene scene)
        {
            try
            {
                // 官方 LevelInited 后还会等待 0.25 秒再 SetPosition，只有 AfterInit 在那之后。
                if (SceneLoader.IsSceneLoading || !LevelManager.LevelInited || !LevelManager.AfterInit
                    || SceneManager.GetActiveScene().handle != scene.handle) return false;
                MultiSceneCore core = MultiSceneCore.Instance;
                if (core != null && core.IsLoading) return false;
                CharacterMainControl player = CharacterMainControl.Main;
                return player != null && player.Health != null && !player.Health.IsDead;
            }
            catch (Exception) { return false; }
        }

        private bool HasSceneReadyWait(int intentGeneration)
        {
            return _sceneReadyRoutine != null && _sceneReadyIntentGeneration == intentGeneration;
        }

        /// <summary>验收等候使用：入场等待尚未建立 runState，但仍由当前请求持有。</summary>
        internal bool IsSceneEntryPending { get { return _sceneReadyRoutine != null; } }

        private void FailSceneReadyWait(bool resume, string reason)
        {
            if (resume)
            {
                CancelSeasonResume();
                FailSeasonResume(reason);
            }
            else AbortSetup(reason, false);
        }

        private void CancelSceneReadyWait()
        {
            _sceneReadyRequestSerial++;
            Coroutine routine = _sceneReadyRoutine;
            _sceneReadyRoutine = null;
            _sceneReadyIntentGeneration = 0;
            try { if (routine != null && _owner != null) _owner.StopCoroutine(routine); }
            catch (Exception) { /* 场景或宿主已释放协程 */ }
        }

        /// <summary>
        /// 新一局开始前复位「上一局残留」。关停闩锁与每局一次性字段都在这里归零，
        /// 保证同一次游戏会话里可以连续开多局（§18.3 的关停是幂等的，但不是永久的）。
        /// </summary>
        private void BeginNewRunSession()
        {
            _restoredSeasonPending = false;
            _resumeNeedsMatchReset = false;
            _commandsClosed = false;
            _shutdownCompleted = false;
            _lastExitReasonId = null;
            _recoveryDriveStateSequence = -1;
            _leaseCheckAccumulator = 0f;
            _errorSwapInputYielded = false;
            _waitingForBetReveal = false;
            _draftPrimaryProfileId = null;
            _draftRelayProfileId = null;
            _draftRefreshCount = 0;
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
                // 每局从已登记的真实刷怪点中抽取可走的擂台位置。抽取在场景
                // ready 后执行，NavMesh 不可用或没有可达组合时安全退出。
                ModeHSupportedMap runMap;
                string mapVariantReason;
                if (ModeHMapSupportRegistry.TryCreateRunVariant(
                        _map, runSeed, out runMap, out mapVariantReason) && runMap != null)
                {
                    _map = runMap;
                    if (!string.IsNullOrEmpty(mapVariantReason))
                    {
                        ModBehaviour.DevLog("[ModeH] 擂台随机点位回退: " + mapVariantReason);
                    }
                }
                else
                {
                    AbortSetup(mapVariantReason ?? "map_no_safe_arena", true);
                    return;
                }
                // 上一季挂着没结的押金（崩在比赛中、换过档）原样退回：新赛季的 runId 不同
                ReconcileCashBetOnRestore();
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
        /// 新赛季只生成一次随机身份，避免进程内 generation 在重启后复用。
        /// runId/runSeed 随 Season 保存；恢复沿用原 DTO，不重新抽取。
        /// </summary>
        private static string ComposeRunId(string sceneName, int sceneGeneration)
        {
            return "mh_" + (sceneName ?? "unknown") + "_" + sceneGeneration.ToString("x")
                + "_" + Guid.NewGuid().ToString("N");
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

            // 普通玩家入口在正式/Dev 构建完全相同；逐项认证只能由 F3 显式请求。
            if (!_certification.TryUseReleaseCatalog())
            {
                AbortSetup("release_catalog_unavailable", true);
                return;
            }
            if (BlockSetupIfPersistedSeasonActive()) return;
            CreateDraftingSeason(_certification.Report);
        }

        internal bool CanRunCertificationFromF3(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled || _owner == null || _shutdownCompleted || _commandsClosed
                || _runState == null || _runState.Lifecycle != ModeHLifecycle.Drafting || _season == null
                || _map == null || _arenaLease == null || !_arenaLease.IsActive
                || _spectatorLease == null || !_spectatorLease.IsActive)
            {
                reason = L10n.T("请先进入鸭王杯并停在选人页", "Enter the Duck Cup and stay on the fighter selection page first");
                return false;
            }
            if (_certificationFromF3 || _certificationRoutine != null)
            { reason = L10n.T("鸭王杯逐项认证正在运行", "Duck Cup certification is already running"); return false; }
            return true;
        }

        /// <summary>唯一调用方是 F3 验收页按钮；普通入场及自动验收均不调用。</summary>
        internal bool StartCertificationFromF3(out string reason)
        {
            if (!F3GameplayValidationRunner.CanRunModeHCertification(_owner, out reason)) return false;
            List<string> keys = ModeHProfileRegistry.GetProductionStableKeys();
            if (keys == null || keys.Count == 0)
            {
                reason = L10n.T("鸭王杯选手目录未就绪", "Duck Cup fighter catalog is not ready");
                return false;
            }
            ModeHProductionCertification certification = new ModeHProductionCertification();
            _certification = certification;
            _certificationFromF3 = true;
            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            ModeHCertificationResult result = new ModeHCertificationResult();
            try
            {
                EnsureUi();
                if (_ui != null)
                {
                    // F3 已先关闭，才释放选人页的暂停租约，避免 F3 把 timeScale 再恢复成 0。
                    _ui.ClosePage();
                    _ui.EnsureDiagnostics(CancelSetupFromDiagnostics);
                }
                Coroutine routine = _owner.StartCoroutine(DriveCertification(keys, result, certification, ownerToken, generation));
                if (_certificationFromF3 && ReferenceEquals(_certification, certification)) _certificationRoutine = routine;
                return true;
            }
            catch (Exception e)
            {
                LogFailure("f3_certification_start", e);
                RestorePlayerFlowAfterCertification(certification, ownerToken, generation);
                reason = L10n.T("鸭王杯逐项认证未能启动", "Duck Cup certification could not start");
                return false;
            }
        }

        internal bool LastCertificationUsedCache { get { return _lastCertificationUsedCache; } }
        internal bool IsCertificationDiagnosticRunning { get { return _certificationFromF3; } }

        /// <summary>Dev 验收专用：把 Drafting 测试赛季归档成 None，再走正常关停。</summary>
        internal bool DebugFinishValidationSeason()
        {
            if (!ModBehaviour.DevModeEnabled || _runState == null) return false;
            if (_runState.Lifecycle != ModeHLifecycle.Drafting) return false;
            if (!TryTransition(ModeHLifecycle.Drafting, ModeHLifecycle.None, "f3_validation")) return false;
            // 认证缓存可能刚在同帧写盘；归档是退出前的 durable 屏障，不能被普通节流延期。
            bool persisted = TryPersistSeason("f3_validation_finished", true);
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
                RefundCashBet("f3_validation_cleanup");
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

        private IEnumerator DriveCertification(List<string> keys, ModeHCertificationResult result,
            ModeHProductionCertification certification, long ownerToken, int generation)
        {
            if (!_certificationFromF3 || !ModBehaviour.DevModeEnabled
                || !ReferenceEquals(_certification, certification) || !IsCallbackStillValid(ownerToken, generation)) yield break;
            int shownFinishedKeys = -1;
            ValidationCoroutineStack inner = new ValidationCoroutineStack(certification.Run(keys, _map, result));
            try
            {
                while (true)
                {
                    // 旧协程不能继续写新一次认证的共享兼容表，也不能在 finally 清掉新请求。
                    if (!ReferenceEquals(_certification, certification) || !_certificationFromF3
                        || !IsCallbackStillValid(ownerToken, generation)) yield break;
                    bool moveNext;
                    try { moveNext = inner.MoveNext(); }
                    catch (Exception e)
                    {
                        LogFailure("f3_certification", e);
                        result.Completed = true;
                        result.Passed = false;
                        result.FailureReasonId = "certification_exception";
                        break;
                    }
                    if (!moveNext) break;
                    if (!IsCallbackStillValid(ownerToken, generation)) yield break;
                    if (_ui != null && result.FinishedKeys != shownFinishedKeys)
                    {
                        shownFinishedKeys = result.FinishedKeys;
                        _ui.UpdateDiagnostics(DescribeCertificationProgress(result),
                            result.TotalKeys > 0 ? (float)result.FinishedKeys / result.TotalKeys : 0f);
                    }
                    // 由共享栈驱动所有子认证并捕获其异常，仍透传叶子等待对象。
                    yield return inner.Current;
                }
                if (!ReferenceEquals(_certification, certification) || !_certificationFromF3
                    || !IsCallbackStillValid(ownerToken, generation)) yield break;
                ModBehaviour.DevLog("[ModeH] F3_CERTIFICATION read_only=false save_writes=false passed="
                    + result.Passed + " finished=" + result.FinishedKeys + "/" + result.TotalKeys
                    + " reason=" + (result.FailureReasonId ?? "none"));
                _owner.ShowMessage(result.Passed
                    ? L10n.T("鸭王杯逐项认证通过，已返回选人页", "Duck Cup certification passed; returning to fighter selection")
                    : L10n.T("鸭王杯逐项认证已结束，详情见日志；已返回选人页", "Duck Cup certification ended; see the log for details and select a fighter"));
            }
            finally
            {
                try { inner.Dispose(); }
                catch (Exception e) { LogFailure("f3_certification_dispose", e); }
                RestorePlayerFlowAfterCertification(certification, ownerToken, generation);
            }
        }

        private void RestorePlayerFlowAfterCertification(ModeHProductionCertification certification,
            long ownerToken, int generation)
        {
            // 场景清理已取消并释放旧实例；迟到的旧 finally 不能碰当前实例/协程/诊断 UI。
            if (!ReferenceEquals(_certification, certification) || !_certificationFromF3
                || !IsCallbackStillValid(ownerToken, generation)) return;
            _certificationFromF3 = false;
            _certificationRoutine = null;
            try { certification.Cancel(); }
            catch (Exception e) { LogFailure("f3_certification_cleanup", e); }
            // 诊断会暂时改兼容矩阵；恢复发布目录及原选人页，不写报告进玩家赛季/认证缓存。
            _certification = new ModeHProductionCertification();
            if (!_certification.TryUseReleaseCatalog())
            {
                RequestSuspended("f3_release_catalog_restore_failed");
                return;
            }
            if (_ui != null) _ui.DestroyDiagnostics();
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        /// <summary>
        /// F3 逐项认证进度。只读 result；UpdateDiagnostics 只在文字变化时写 TMP。
        /// 旧写法在失败时直接把内部 reasonId 显示给玩家，失败原因现在只走中止提示（ResolveAbortMessageKey）。
        /// </summary>
        private static string DescribeCertificationProgress(ModeHCertificationResult result)
        {
            if (result == null || result.TotalKeys <= 0) return string.Empty;
            if (result.FinishedKeys >= result.TotalKeys)
                return L10n.T(ModeHConfig.LocalizationKeyPrefix + "Diag_Finishing");
            int current = Math.Min(result.FinishedKeys + 1, Math.Max(1, result.TotalKeys));
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "Diag_Progress")
                .Replace("{0}", current.ToString()).Replace("{1}", result.TotalKeys.ToString());
        }

        /// <summary>取消 F3 认证，协程收尾回收诊断选手并恢复原选人页，不退出赛季或退票。</summary>
        private void CancelSetupFromDiagnostics()
        {
            try
            {
                if (_certificationFromF3)
                {
                    if (_certification != null) _certification.Cancel();
                    return;
                }
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
        /// <summary>
        /// 磁盘上是否还挂着一份活动赛季。内存里没有活动赛季**不等于**磁盘上没有：
        /// 局内退出会把 _season/_runState 清成 null，而此前 recovery-only 闸并没有立起，
        /// 于是玩家再进一次图就会新建赛季并把旧的整份覆盖掉——合同选手、战痕、名声、
        /// 虚拟筹码与已打场次全部静默清零（CR-2026-08-29-012 同类，但走的是进程内路径）。
        ///
        /// 命中时立回 recovery-only 闸，把处置权交还恢复壳（那里有「放弃赛季」出口）。
        /// </summary>
        private bool BlockSetupIfPersistedSeasonActive()
        {
            try
            {
                ModeHSeasonDto existing = ModeHProfilePersistence.LoadCurrent();
                if (existing == null || existing.runState == null) return false;

                ModeHLifecycle lifecycle = ModeHStateModel.ToLifecycle(existing.runState.lifecycle);
                if (lifecycle == ModeHLifecycle.None || lifecycle == ModeHLifecycle.SeasonEnded)
                {
                    return false;
                }

                ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, "season_recovery_shell");
                AbortSetup("season_active_record_exists", true);
                return true;
            }
            catch (Exception e)
            {
                // 读不出来不阻断开局：闸与恢复壳仍是兜底，这里宁可放行也不误伤新档
                LogFailure("check_persisted_season", e);
                return false;
            }
        }

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
                if (!ModeHSaveFlushCoordinator.RequestSeasonWrite(_season, out error, true))
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
            // 押钱 / 押物品不在这里退：挂起、关停、切图都还能回来重打这一场，押注跟着这一场走；
            // 这一季不再打了才退（放弃赛季、开新赛季时对账、F3 清理），见 ModeHRuntimeModule_BetFlow。
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

            _preparedStats.Clear();
            _preparedOutfits.Clear();
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
            CancelSceneReadyWait();
            // 收尾不能让诊断协程的 finally 再打开选人页。
            _certificationFromF3 = false;
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

            try { ReleaseMatchRuntime(); }
            catch (Exception e) { LogFailure("release_match", e); }

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
            _errorSwapInputYielded = false;
            _waitingForBetReveal = false;
        }

        #endregion
    }
}

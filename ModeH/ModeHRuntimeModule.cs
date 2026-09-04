using System;

namespace BossRush
{
    /// <summary>
    /// Mode H 运行时模块（设计提案 §18.1、§18.3、§25.1）。
    ///
    /// 硬约束：
    /// - 全模式只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
    ///   BossRushRuntimeModuleHost；入口、交互点、恢复面板和场景回调都只能委托这份实例，
    ///   禁止再次 new（Mode G 当前存在入口实例与 host 实例分裂的反例，本模式不复制）；
    /// - 只复用 host 已有的六个回调；ShutdownRuntime、owner/token 校验和异常转 RequestExit
    ///   由本模块自己实现，host 的 catch 只是最后兜底日志；
    /// - 每个实例持有 runOwnerToken / runId / sceneGeneration，所有延迟回调先比较三者；
    /// - host 的 OnUpdate 受 gameplay gate 限制，不能假设加载期持续收到 tick。
    /// </summary>
    internal sealed partial class ModeHRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private ModeHRunState _runState;
        private int _sceneGeneration;
        private int _contentScanSlotGeneration = -1;
        private bool _shutdownCompleted;
        private string _lastExitReasonId;
        private bool _levelEventSubscribed;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return ModeHConfig.ModeName; } }

        /// <summary>宿主 owner。</summary>
        internal ModBehaviour Owner { get { return _owner; } }

        /// <summary>当前赛季内存状态（无活动赛季时为 null）。</summary>
        internal ModeHRunState RunState { get { return _runState; } }

        /// <summary>当前 scene generation。</summary>
        internal int SceneGeneration { get { return _sceneGeneration; } }

        /// <summary>是否存在活动赛季 owner。</summary>
        internal bool HasActiveRun
        {
            get { return _runState != null && _runState.Lifecycle != ModeHLifecycle.None; }
        }

        /// <summary>入口总开关（只经 owner getter 读取，禁止缓存可写副本）。</summary>
        internal bool IsEnabled
        {
            get
            {
                try
                {
                    return _owner != null && _owner.IsModeHConfiguredEnabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>最后一次退出原因。</summary>
        internal string LastExitReasonId { get { return _lastExitReasonId; } }

        #endregion

        #region host 六回调

        /// <summary>host Awake：登记 owner、订阅存档、执行一次轻量风险扫描。</summary>
        public override void OnAwake(ModBehaviour owner)
        {
            try
            {
                _owner = owner;
                _shutdownCompleted = false;
                ModeHSaveFlushCoordinator.EnsureSubscribed();
                EnsureLevelReadySubscription();
                ModeHRuntimeGates.ResetForSlotChange();
                ModeHRuntimeGates.InitializeRiskForSlot(ModeHRuntimeGates.SlotGeneration);
                RestoreFromSaveIfPresent();
            }
            catch (Exception e)
            {
                LogFailure("awake", e);
            }
        }

        /// <summary>host Start：配置开启或存在 H 恢复记录时才执行内容扫描。</summary>
        public override void OnStart()
        {
            try
            {
                // 补跑：宿主可能在关卡初始化之后才起来（Mod 热重载、宿主重建），
                // 那一次 OnAfterLevelInitialized 已经过去了。照 BackMountainRuntimeModule
                // 的 EnsureBootstrapped 写法，用 LevelManager.AfterInit 兜住。
                if (IsLevelAfterInit())
                {
                    ModeHWarehouseStakeJournal.TryRecomputeDeferredSlotConsistency();
                }
                EnsureContentScanned();
            }
            catch (Exception e)
            {
                LogFailure("start", e);
            }
        }

        /// <summary>
        /// host 场景回调：更新 scene generation。
        /// 注意 OnSceneLoadedIntegrationRuntime 先于本回调执行，Legacy 接管决策此时已定型；
        /// arena isolation lease 与 spectator lease 在本回调内获取。
        /// </summary>
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            try
            {
                _sceneGeneration++;
                if (_runState != null)
                {
                    _runState.UpdateSceneGeneration(_sceneGeneration);
                }
                // 兜底：正常情况下 OnAfterLevelInitialized 会先补算，但那次事件
                // 若因宿主时序被错过，这里再试一次（不就绪就原样保持推迟，零副作用）
                ModeHWarehouseStakeJournal.TryRecomputeDeferredSlotConsistency();
                OnSceneLoadedInternal(context);
            }
            catch (Exception e)
            {
                LogFailure("scene", e);
                RequestExit(ModeHExitReason.TechnicalAbort, "scene_callback_exception");
            }
        }

        /// <summary>host tick：只做存档重试与状态机驱动，禁止全量扫描或每帧分配。</summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            try
            {
                ModeHSaveFlushCoordinator.Tick();
                OnUpdateInternal(deltaTime, unscaledDeltaTime);
            }
            catch (Exception e)
            {
                LogFailure("update", e);
                RequestExit(ModeHExitReason.TechnicalAbort, "update_exception");
            }
        }

        /// <summary>host LateUpdate：首发无需处理。</summary>
        public override void OnLateUpdate()
        {
        }

        /// <summary>host 销毁：走同一个幂等 shutdown 入口。</summary>
        public override void OnDestroy()
        {
            try
            {
                // 先退订官方事件，再走 shutdown：宿主销毁后 LevelManager 不得再回调进来
                ShutdownLevelReadySubscription();
                ShutdownRuntime(ModeHExitReason.ModDestroyed, "host_destroy");
                // 幂等兜底：即使 shutdown 早退，也必须在宿主销毁路径上清空全部静态缓存
                ModeHRuntimeModule.ResetModeHStaticCaches();
            }
            catch (Exception e)
            {
                LogFailure("destroy", e);
            }
        }

        #endregion

        #region 关卡就绪回调（槽位一致性补算）

        /// <summary>
        /// 幂等订阅关卡就绪事件。用命名方法（AGENTS.md 4.6，lambda 退订不掉）。
        ///
        /// **不能按 IsEnabled 门控**：这条回调补算的是 external asset 闸，而那道闸
        /// 拦的是七个**旧模式**入口，与 Mode H 自己开没开无关。
        /// </summary>
        private void EnsureLevelReadySubscription()
        {
            try
            {
                if (_levelEventSubscribed) return;
                LevelManager.OnAfterLevelInitialized += HandleLevelReady;
                _levelEventSubscribed = true;
            }
            catch (Exception e)
            {
                LogFailure("level_subscribe", e);
            }
        }

        /// <summary>幂等退订。宿主销毁路径必须调。</summary>
        private void ShutdownLevelReadySubscription()
        {
            try
            {
                if (!_levelEventSubscribed) return;
                LevelManager.OnAfterLevelInitialized -= HandleLevelReady;
            }
            catch (Exception e)
            {
                LogFailure("level_unsubscribe", e);
            }
            finally
            {
                // 退订失败也要把标记置回，避免重复订阅越滚越多
                _levelEventSubscribed = false;
            }
        }

        /// <summary>
        /// 关卡初始化完成：`PlayerStorage` 此刻必然已 `HasInitialized()`
        /// （它在自己的 Awake 里 `RegisterWaitForInitialization`），
        /// 补一次被推迟的槽位一致性判定。
        /// </summary>
        private void HandleLevelReady()
        {
            try
            {
                ModeHWarehouseStakeJournal.TryRecomputeDeferredSlotConsistency();
            }
            catch (Exception e)
            {
                LogFailure("level_ready", e);
            }
        }

        /// <summary>关卡是否已完成初始化。no-throw。</summary>
        private static bool IsLevelAfterInit()
        {
            try
            {
                return LevelManager.AfterInit;
            }
            catch (Exception)
            {
                // 读不到就当作未就绪：补算会等下一次 OnAfterLevelInitialized
                return false;
            }
        }

        #endregion

        #region 内容扫描与存档恢复

        /// <summary>幂等执行一次静态内容扫描。</summary>
        internal void EnsureContentScanned()
        {
            int slotGen = ModeHRuntimeGates.SlotGeneration;
            if (_contentScanSlotGeneration == slotGen) return;

            bool hasRecoveryRecord = ModeHRuntimeGates.IsModeHRecoveryOnlyBlocked
                || ModeHRuntimeGates.IsModeHExternalAssetRiskBlocked;
            if (!IsEnabled && !hasRecoveryRecord) return;

            _contentScanSlotGeneration = slotGen;
            ModeHRuntimeGates.InitializeContentForSlot();
        }

        /// <summary>
        /// 换档 / 删档后重建内存 run owner。
        ///
        /// 存档恢复过去只在 OnAwake 跑一次，而 OnAwake 每个进程只执行一次：
        /// 玩家从主菜单载入一份「中断赛季」存档时，恢复逻辑根本不会重跑，
        /// recovery-only 闸不会立起来，玩家可以直接开新赛季，
        /// CreateDraftingSeason 会**静默覆盖**那份旧赛季（CR-2026-08-29-012）。
        /// 由 ModeHProfilePersistence 的换档回调调用。
        /// </summary>
        internal static void NotifySlotRestored()
        {
            try
            {
                // 走 Mode H 唯一的宿主解析器，不在这里再取一次单例（分类基线：ModeH 恒为 1）
                ModBehaviour mod = ModeHInteractable.ResolveHost(null);
                ModeHRuntimeModule runtime = mod != null ? mod.ModeHRuntime : null;
                if (runtime == null) return;
                runtime.RestoreForSlotChange();
            }
            catch (Exception)
            {
                // 换档回调不得抛：恢复失败时保持无 run 状态，入口仍受可用性门保护
            }
        }

        /// <summary>
        /// 换档时重建 run owner。
        ///
        /// 旧写法在已有活动 run 时直接早退，注释说「交给既有的场景失配出口处理」——
        /// 但那个出口只在 `OnSceneLoaded` 里触发，主菜单换档不产生 Mode H 认可的场景回调。
        /// 结果是旧槽的 `_runState` 留在内存里，之后任何 `TryPersistSeason` 都会把
        /// 旧槽赛季写进**新槽**。换槽本身就是 run owner 失效的充分条件，直接丢弃。
        ///
        /// 这里**不能**走 `ShutdownRuntime`：它的中止路径会把押品退还到「当前」仓库，
        /// 而此刻 `PlayerStorage` 已经指向新槽，退还等于把旧槽装备搬进新槽。
        /// 旧槽押品由该槽自己的 journal 记录，重新载入那个槽时经恢复壳处置。
        /// </summary>
        private void RestoreForSlotChange()
        {
            _runState = null;
            RestoreFromSaveIfPresent();
        }

        /// <summary>存在活动 Season 时重建内存 run owner（生成新的 owner token）。</summary>
        private void RestoreFromSaveIfPresent()
        {
            ModeHSeasonDto season = ModeHProfilePersistence.LoadCurrent();
            if (season == null || season.runState == null)
            {
                _runState = null;
                ModeHRuntimeGates.SetRunOwnerActive(false);
                return;
            }

            ModeHLifecycle lifecycle = ModeHStateModel.ToLifecycle(season.runState.lifecycle);
            if (lifecycle == ModeHLifecycle.Unknown)
            {
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, "season_lifecycle_unknown");
                _runState = null;
                return;
            }
            if (lifecycle == ModeHLifecycle.None || lifecycle == ModeHLifecycle.SeasonEnded)
            {
                _runState = null;
                ModeHRuntimeGates.SetRunOwnerActive(false);
                return;
            }

            _runState = ModeHRunState.FromDto(season.runState);
            if (_runState != null)
            {
                _runState.RestoreEventTokens(season.appliedEventTokenIds);
                _sceneGeneration = _runState.SceneGeneration;
                ModeHRuntimeGates.SetRunOwnerActive(true);
                // 活动赛季重建后只允许恢复流程，不允许直接开新赛季
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, "season_recovery_shell");
            }
        }

        #endregion

        #region 状态机门面

        /// <summary>
        /// 唯一状态转换入口的门面：调用方只提交命令，不直接写状态字段。
        /// </summary>
        internal bool TryTransition(ModeHLifecycle expected, ModeHLifecycle next, string reason)
        {
            if (_runState == null) return false;
            ModeHTransitionRecord record;
            string failureReasonId;
            bool ok = ModeHStateMachine.TryTransition(
                _runState, expected, next, _runState.OwnerToken, reason, out record, out failureReasonId);
            if (!ok)
            {
                ModBehaviour.DevLog("[ModeH] 状态转换被拒绝: " + expected + " -> " + next
                    + " (" + (failureReasonId != null ? failureReasonId : "unknown") + ")");
                return false;
            }
            OnTransitionApplied(record);
            return true;
        }

        /// <summary>技术故障统一出口：没有已提交事实时进入 Recovering。</summary>
        internal void RequestRecovering(string reasonId)
        {
            if (_runState == null) return;
            TryTransition(_runState.Lifecycle, ModeHLifecycle.Recovering, reasonId);
        }

        /// <summary>已提交事实的恢复屏障：先进入 ErrorRecoveryPending。</summary>
        internal void RequestErrorRecoveryPending(string reasonId)
        {
            if (_runState == null) return;
            TryTransition(_runState.Lifecycle, ModeHLifecycle.ErrorRecoveryPending, reasonId);
        }

        /// <summary>恢复证据不足或自动重试预算耗尽：持久挂起。</summary>
        internal void RequestSuspended(string reasonId)
        {
            if (_runState == null) return;
            if (TryTransition(_runState.Lifecycle, ModeHLifecycle.Suspended, reasonId))
            {
                // 挂起可能跨进程重启，而 _escrowItems 是纯内存 List：不在这里返还，
                // 物品就永久丢失。同场重开只会回落到 MatchBrief（见 ResolveRecoveryResumeLifecycle），
                // 不会回到已锁盘状态，所以此处结清 journal 不会破坏恢复路径。
                TryReturnRealStakeOnAbort("suspended:" + (reasonId != null ? reasonId : "unknown"));
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, reasonId);
                // Suspended 是显式持久化点：恢复壳必须跨重启可达，不能只留内存脏标记。
                TryPersistSeason("suspended");
            }
        }

        #endregion

        #region 退出与关闭

        /// <summary>
        /// 幂等退出入口。地图返回、Mod 销毁、场景失配和 Mode H 自身异常都调用它。
        /// 退出顺序见 §18.3，由 ShutdownRuntime 实现。
        /// </summary>
        internal void RequestExit(ModeHExitReason reason, string reasonId)
        {
            try
            {
                ShutdownRuntime(reason, reasonId);
            }
            catch (Exception e)
            {
                LogFailure("request_exit", e);
            }
        }

        /// <summary>
        /// 幂等 shutdown：停止接收新命令 -> 停止生成队列 -> 取消战斗计时 -> 恢复 command adapter
        /// -> 回收临时角色/kit/runtime preset clone -> 释放 spectator lease -> 释放 arena isolation lease
        /// -> 完成 Season flush/readback -> 销毁 UI -> 释放 runtime owner。
        /// </summary>
        internal void ShutdownRuntime(ModeHExitReason reason, string reasonId)
        {
            if (_shutdownCompleted) return;
            _shutdownCompleted = true;
            _lastExitReasonId = reasonId;

            ShutdownRuntimeInternal(reason, reasonId);

            try
            {
                ModeHSaveFlushCoordinator.TryFlushOnHostDestroy();
            }
            catch (Exception)
            {
                // flush 失败保留 pending，由恢复流程处理
            }

            ModeHRuntimeGates.SetRunOwnerActive(false);

            // 关停清掉了内存里的 _season/_runState，但磁盘上那份可能仍是活动 lifecycle。
            // 不把 recovery-only 闸立回去，玩家回基地就能直接开新赛季，
            // CreateDraftingSeason 会把中断的赛季整份覆盖掉。
            try
            {
                ModeHSeasonDto persisted = ModeHProfilePersistence.LoadCurrent();
                if (persisted != null && persisted.runState != null)
                {
                    ModeHLifecycle lifecycle = ModeHStateModel.ToLifecycle(persisted.runState.lifecycle);
                    if (lifecycle != ModeHLifecycle.None
                        && lifecycle != ModeHLifecycle.SeasonEnded)
                    {
                        ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, "season_recovery_shell");
                    }
                }
            }
            catch (Exception)
            {
                // 读不出来就不动闸：InitializeRiskForSlot 与恢复壳仍是兜底
            }

            if (reason == ModeHExitReason.ModDestroyed)
            {
                ModeHRuntimeModule.ResetModeHStaticCaches();
            }
        }

        /// <summary>
        /// Mode H 全部静态缓存的唯一清理入口（设计提案 §18.3、§24.3）。
        /// Mod 卸载 / 宿主重建时调用；正常离场只释放 run owner，不清内容目录。
        /// </summary>
        internal static void ResetModeHStaticCaches()
        {
            // 先退订再清缓存（4.6）：Mod 销毁后 SavesSystem 的三个事件不得再回调进 Mode H。
            // 形态与 PetNest / 日报 / Mode G 的销毁路径一致。
            ModeHSaveFlushCoordinator.ShutdownSubscription();
            ModeHRuntimeGates.ResetStaticCaches();
            ModeHSaveFlushCoordinator.ResetStaticCaches();
            ModeHContentCatalog.ResetStaticCaches();
            ModeHProfileRegistry.ResetStaticCaches();
            ModeHLoadoutKitRegistry.ResetStaticCaches();
            ModeHCommandCompatibilityRegistry.ResetStaticCaches();
            ModeHMapSupportRegistry.ResetStaticCaches();
            ModeHPresentationAssetCache.ResetStaticCaches();
            ModeHCanonicalDigest.ResetStaticCaches();
            ModeHStateMachine.ResetStaticCaches();
            ModeHEventRouter.ResetStaticCaches();
            ModeHWarehouseStakeJournal.ResetStaticCaches();
            ModeHStakeJournalPersistence.ResetStaticCaches();
            ModeHRealStakeService.ResetStaticCaches();
            ModeHPresetRegistry.ResetStaticCaches();
            ModeHDeathSuppressionRegistry.ResetStaticCaches();
            ModeHCombatTelemetry.ResetStaticCaches();
        }

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.CriticalLog(
                    "modeh-runtime-" + stage,
                    "[ModeH] 运行时阶段失败: " + stage + " - " + e.GetType().Name + ": " + e.Message);
            }
            catch (Exception)
            {
                // 日志失败不再抛出
            }
        }

        #endregion

        #region 后续步骤接入点（分部实现）

        /// <summary>场景到达后的 Mode H 处理（隔离租约、观战租约、认证与状态推进）。</summary>
        partial void OnSceneLoadedInternal(SceneRuntimeContext context);

        /// <summary>每帧驱动（战斗计时、口令窗口、快照采集）。</summary>
        partial void OnUpdateInternal(float deltaTime, float unscaledDeltaTime);

        /// <summary>状态转换后的副作用（持久化投影、UI 刷新）。</summary>
        partial void OnTransitionApplied(ModeHTransitionRecord record);

        /// <summary>shutdown 的具体清理顺序。</summary>
        partial void ShutdownRuntimeInternal(ModeHExitReason reason, string reasonId);

        #endregion
    }
}

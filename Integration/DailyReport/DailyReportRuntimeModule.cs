// ============================================================================
// DailyReportRuntimeModule.cs - 日报运行时模块宿主（P0 步骤 3）
// ============================================================================
// 硬约束（形态照 PetNest/PetNestRuntimeModule.cs）：
//   - 全系统只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
//     BossRushRuntimeModuleHost；建筑、面板与场景回调都只能委托这份实例，禁止再次 new；
//   - 只复用 host 已有的回调，不新增全局 hook；
//   - **dailyReportEnabled = false 时全系统 dormant**：不订阅存档、不计时、不提示。
//     开关是运行时可变的（ModConfig 单键回调），因此 bootstrap 走幂等的
//     EnsureBootstrapped，而不是只在 Awake 判一次。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>日报运行时模块。宿主回调的唯一落点。</summary>
    internal sealed class DailyReportRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return DailyReportTuning.ModuleName; } }

        /// <summary>宿主 owner。</summary>
        internal ModBehaviour Owner { get { return _owner; } }

        /// <summary>当前 scene generation。</summary>
        internal int SceneGeneration { get { return _sceneGeneration; } }

        /// <summary>是否已完成一次 bootstrap（订阅存档）。</summary>
        internal bool IsBootstrapped { get { return _bootstrapped; } }

        /// <summary>入口总开关（只经 owner getter 读取，禁止缓存可写副本）。</summary>
        internal bool IsEnabled
        {
            get
            {
                try
                {
                    return _owner != null && _owner.IsDailyReportConfiguredEnabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        #endregion

        #region host 回调

        /// <summary>host Awake：记 owner。开关开启时才 bootstrap。</summary>
        public override void OnAwake(ModBehaviour owner)
        {
            try
            {
                _owner = owner;
                _bootstrapped = false;
                EnsureBootstrapped();
            }
            catch (Exception e)
            {
                LogFailure("awake", e);
            }
        }

        /// <summary>host Start：开关可能在 Awake 之后才由 ModConfig 载入，这里再判一次。</summary>
        public override void OnStart()
        {
            try
            {
                EnsureBootstrapped();
            }
            catch (Exception e)
            {
                LogFailure("start", e);
            }
        }

        /// <summary>
        /// host 场景回调：推进 scene generation；回基地时补发挂起的出刊提示
        /// （跨天发生在战斗中时不打扰玩家，回基地才提示）。
        /// </summary>
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            try
            {
                _sceneGeneration++;
                if (!IsEnabled)
                {
                    ShutdownIfEnabledTurnedOff();
                    return;
                }
                EnsureBootstrapped();

                if (DailyReportService.HasPendingIssueBanner && IsBaseScene())
                {
                    AnnounceNewIssue();
                }
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>
        /// host tick：推进自算计时器 + 驱动存档协调器的 deferred 重试。
        /// 关闭或未 bootstrap 时零成本早返。
        /// </summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped)
            {
                // 开关是运行时可变的：关掉再打开时必须当帧复活，否则计时器一直冻结、
                // 订阅也不恢复，要等到下次切场景才活过来。
                // 关闭状态下这里是两次 bool + 一次 no-throw getter，仍是 O(1) 早返。
                EnsureBootstrapped();
                if (!_bootstrapped) return;
            }
            try
            {
                if (!IsEnabled)
                {
                    ShutdownIfEnabledTurnedOff();
                    return;
                }

                // 与官方 GameClock.Update 同源：用受 timeScale 影响的 deltaTime。
                DailyReportService.Tick(deltaTime);
                DailyReportSaveCoordinator.Tick();

                if (DailyReportService.HasPendingIssueBanner && IsBaseScene())
                {
                    AnnounceNewIssue();
                }
            }
            catch (Exception e)
            {
                LogFailure("update", e);
            }
        }

        /// <summary>host 销毁：尽力落盘一次，随后退订并清状态。</summary>
        public override void OnDestroy()
        {
            try
            {
                if (_bootstrapped)
                {
                    DailyReportService.SyncCarrySecondsToPersistence();
                    DailyReportSaveCoordinator.TryFlushOnHostDestroy();
                    DailyReportSaveCoordinator.ShutdownSubscription();
                    DailyReportStatsCollector.ShutdownSubscription();
                }
                _bootstrapped = false;
                _owner = null;
            }
            catch (Exception e)
            {
                LogFailure("destroy", e);
            }
        }

        #endregion

        #region bootstrap

        /// <summary>
        /// 幂等 bootstrap：只在开关开启时订阅存档。
        /// 开关是运行时可变的，因此每个回调都调它，而不是只在 Awake 判一次。
        /// </summary>
        internal void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            if (!IsEnabled) return;

            DailyReportSaveCoordinator.EnsureSubscribed();
            DailyReportStatsCollector.EnsureSubscribed();
            _bootstrapped = true;
            ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "运行时模块已启动");
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：落盘、退订、回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            if (!_bootstrapped) return;
            try
            {
                DailyReportService.SyncCarrySecondsToPersistence();
                DailyReportSaveCoordinator.TryFlushOnHostDestroy();
                DailyReportSaveCoordinator.ShutdownSubscription();
                // 与 OnDestroy 对齐：dormant 契约要求不留任何订阅，否则关掉开关后
                // 经济/raid 事件仍在回调里空转，统计也会继续写进持久层。
                DailyReportStatsCollector.ShutdownSubscription();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }
            _bootstrapped = false;
            ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 提示

        /// <summary>发出「新一期已送达」横幅并清挂起标志。</summary>
        private void AnnounceNewIssue()
        {
            try
            {
                if (_owner != null)
                {
                    _owner.ShowBigBanner(L10n.T(
                        "《鸭科夫日报》新一期已送达信箱",
                        "A new issue of the Duckov Daily has arrived"));
                }
            }
            catch (Exception)
            {
                // 提示失败不影响玩法：照样把标志清掉，避免每帧重试刷屏
            }
            DailyReportService.ConsumeIssueBanner();
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 运行时模块 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 连日志都失败时静默：绝不让诊断路径拖崩宿主
            }
        }

        private static bool IsBaseScene()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}

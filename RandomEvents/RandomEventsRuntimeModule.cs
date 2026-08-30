// ============================================================================
// RandomEventsRuntimeModule.cs - 随机事件运行时模块宿主（方案二 步骤 5）
// ============================================================================
// 硬约束（形态逐字照 Integration/DailyReport/DailyReportRuntimeModule.cs）：
//   - 全系统只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
//     BossRushRuntimeModuleHost；HUD、F3 与事件实现只能委托这份实例，禁止再次 new；
//   - 只复用 host 已有的回调，不新增任何全局 hook、不新增 Harmony 补丁；
//   - **randomEventsEnabled = false 时全系统 dormant**：不建调度器、不计时、不建 HUD。
//     开关是运行时可变的（ModConfig 单键回调），因此 bootstrap 走幂等的 EnsureBootstrapped，
//     而不是只在 Awake 判一次；关掉时走 ShutdownIfEnabledTurnedOff 当帧回 dormant；
//   - 本文件不得引用任何波次状态机符号（tests/RandomEventsWaveIsolationGuard.py 守卫）。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>随机事件运行时模块。宿主回调的唯一落点。</summary>
    internal sealed class RandomEventsRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;
        private RandomEventDirector _director;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return RandomEventsTuning.ModuleName; } }

        /// <summary>宿主 owner。</summary>
        internal ModBehaviour Owner { get { return _owner; } }

        /// <summary>当前 scene generation。</summary>
        internal int SceneGeneration { get { return _sceneGeneration; } }

        /// <summary>是否已完成一次 bootstrap（建好调度器）。</summary>
        internal bool IsBootstrapped { get { return _bootstrapped; } }

        /// <summary>入口总开关（只经 owner getter 读取，禁止缓存可写副本）。no-throw。</summary>
        internal bool IsEnabled
        {
            get
            {
                try
                {
                    return _owner != null && _owner.IsRandomEventsConfiguredEnabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 唯一调度器实例的只读门面。HUD / F3 / 事件实现只能用它，禁止二次 new。
        /// 未 bootstrap（开关关闭）时为 null。
        /// </summary>
        internal RandomEventDirector Director { get { return _director; } }

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
        /// host 场景回调：推进 scene generation，作废在途异步续作并强制收掉在跑事件。
        /// 关闭状态下顺带把可能残留的运行时收干净。
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

                if (_director != null)
                {
                    _director.OnSceneChanged();
                }
                RandomEventHud.HideImmediate();
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>
        /// host tick：驱动调度器与 HUD。
        /// 关闭或未 bootstrap 时零成本早返；dormant 态下调度器本身也只做 float 累加。
        /// </summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped)
            {
                // 开关是运行时可变的：关掉再打开时必须当帧复活，否则要等下次切场景才活过来。
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

                if (_director == null) return;

                // 与官方 GameClock.Update 同源：用受 timeScale 影响的 deltaTime。
                _director.Tick(deltaTime);
                RandomEventHud.Tick(_director);
            }
            catch (Exception e)
            {
                LogFailure("update", e);
            }
        }

        /// <summary>host 销毁：收掉在跑事件、销毁 HUD、复位静态缓存。</summary>
        public override void OnDestroy()
        {
            try
            {
                if (_director != null)
                {
                    _director.ShutdownRuntime(RandomEventEndReason.HostDestroyed);
                }
                RandomEventHud.Destroy();
                RandomEventDirector.ResetStaticCaches();
            }
            catch (Exception e)
            {
                LogFailure("destroy", e);
            }
            _director = null;
            _bootstrapped = false;
            _owner = null;
        }

        #endregion

        #region bootstrap

        /// <summary>
        /// 幂等 bootstrap：只在开关开启时建调度器。
        /// 开关是运行时可变的，因此每个回调都调它，而不是只在 Awake 判一次。
        /// </summary>
        internal void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            if (!IsEnabled) return;

            _director = new RandomEventDirector(_owner);
            _bootstrapped = true;
            ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "运行时模块已启动");
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：收掉在跑事件、销毁 HUD、回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            if (!_bootstrapped) return;
            try
            {
                if (_director != null)
                {
                    _director.ShutdownRuntime(RandomEventEndReason.SwitchDisabled);
                }
                // 与 OnDestroy 对齐：dormant 契约要求不留任何生成物与 UI 对象，
                // 否则关掉开关后徽章还挂在屏幕上、空投箱还留在地上。
                RandomEventHud.Destroy();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }
            _director = null;
            _bootstrapped = false;
            ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 运行时模块 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 连日志都失败时静默：绝不让诊断路径拖崩宿主
            }
        }

        #endregion
    }
}

// ============================================================================
// CodexRuntimeModule.cs - 鸭皇图鉴运行时模块宿主
// ============================================================================
// 硬约束（形态逐字照 Integration/DailyReport/DailyReportRuntimeModule.cs）：
//   - 全系统只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
//     BossRushRuntimeModuleHost；物品入口、Wiki 交叉入口、面板与场景回调都只能
//     委托这份实例，禁止再次 new。
//   - 只复用 host 已有的回调，不新增全局 hook、不新增 Harmony patch。
//   - **codexEnabled = false 时全系统 dormant**：不订阅存档、不建目录、不采集。
//     开关是运行时可变的（ModConfig 单键回调），因此 bootstrap 走幂等的
//     EnsureBootstrapped 而不是只在 Awake 判一次。
//   - OnDestroy / 热切关闭：**先落盘、再退订**，顺序是硬约束。反过来会把
//     pending 的图鉴写入连同订阅一起丢掉。
//   - OnUpdate 只驱动 CodexSaveCoordinator.Tick()，**不做**每帧目录扫描或
//     字符串拼接（AGENTS.md 4.7 / 4.12）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>图鉴运行时模块。宿主回调的唯一落点。全系统唯一实例。</summary>
    internal sealed class CodexRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return CodexTuning.ModuleName; } }

        /// <summary>宿主 owner。</summary>
        internal ModBehaviour Owner { get { return _owner; } }

        /// <summary>当前 scene generation。</summary>
        internal int SceneGeneration { get { return _sceneGeneration; } }

        /// <summary>是否已完成一次 bootstrap（订阅存档 + 建目录）。</summary>
        internal bool IsBootstrapped { get { return _bootstrapped; } }

        /// <summary>入口总开关（只经 owner getter 读取，禁止缓存可写副本）。</summary>
        internal bool IsEnabled
        {
            get
            {
                try
                {
                    return _owner != null && _owner.IsCodexConfiguredEnabled();
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
        /// host 场景回调：推进 scene generation，并清掉 run 级采集状态
        /// （上一张图的角色实例全部作废，去重集与计时表继续留着只是内存垃圾）。
        /// </summary>
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            try
            {
                _sceneGeneration++;

                // 采集器的 run 级清理与开关无关：即使开关关着也要把残留清干净
                CodexKillCollector.ClearRunScoped();

                if (!IsEnabled)
                {
                    ShutdownIfEnabledTurnedOff();
                    return;
                }

                EnsureBootstrapped();

                // 过图时把面板收掉：面板持有的是上一张图的目录快照与卡片
                CloseViewIfOpen();
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>
        /// host tick：只驱动存档协调器的 deferred 重试。
        /// 关闭或未 bootstrap 时零成本早返，热路径无日志无分配。
        /// </summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped)
            {
                // 开关是运行时可变的：关掉再打开时必须当帧复活，否则订阅不恢复，
                // 要等到下次切场景才活过来。关闭状态下这里是两次 bool
                // 加一次 no-throw getter，仍是 O(1) 早返。
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

                CodexSaveCoordinator.Tick();
            }
            catch (Exception e)
            {
                LogFailure("update", e);
            }
        }

        /// <summary>host 销毁：尽力落盘一次，随后退订并清状态（顺序是硬约束）。</summary>
        public override void OnDestroy()
        {
            try
            {
                if (_bootstrapped)
                {
                    CodexSaveCoordinator.TryFlushOnHostDestroy();
                    CodexSaveCoordinator.ShutdownSubscription();
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
        /// 幂等 bootstrap：只在开关开启时订阅存档并建目录。
        /// 开关是运行时可变的，因此每个回调都调它，而不是只在 Awake 判一次。
        /// </summary>
        internal void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            if (!IsEnabled) return;

            CodexSaveCoordinator.EnsureSubscribed();
            // 官方 Boss 池过去只会在进竞技场时初始化，导致新会话在基地首次打开图鉴
            // 只能看到自定义条目。共享预热入口自身幂等，且仅在内容系统启用时触发。
            if (_owner != null) _owner.EnsureEnemyPresetsReadyForGameplayCatalogs();
            CodexBossCatalog.EnsureBuilt(_owner);
            _bootstrapped = true;
            ModBehaviour.DevLog(CodexTuning.LogPrefix + "运行时模块已启动");
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：落盘、退订、收面板、回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            if (!_bootstrapped) return;

            try
            {
                // 先落盘再退订：反过来会把 pending 写入连同订阅一起丢掉
                CodexSaveCoordinator.TryFlushOnHostDestroy();
                CodexSaveCoordinator.ShutdownSubscription();
                CodexKillCollector.ClearRunScoped();
                CloseViewIfOpen();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }

            _bootstrapped = false;
            ModBehaviour.DevLog(CodexTuning.LogPrefix + "入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 门面

        /// <summary>
        /// 打开/切换图鉴面板（物品与 Wiki 交叉入口的唯一门面）。
        /// 开关关闭时不呈现——dormant 契约要求关掉之后连界面都不该出现。
        /// </summary>
        internal void ToggleCodexPanel()
        {
            try
            {
                if (!IsEnabled)
                {
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "入口开关已关闭，忽略打开图鉴请求");
                    return;
                }

                EnsureBootstrapped();
                // 目录可能被 Boss 池筛选作废过，这里补一次幂等重建
                CodexBossCatalog.EnsureBuilt(_owner);

                CodexView.EnsureInstance();
                if (CodexView.Instance != null)
                {
                    CodexView.Instance.Toggle();
                }
            }
            catch (Exception e)
            {
                LogFailure("toggle_panel", e);
            }
        }

        /// <summary>
        /// Boss 过滤池变化后作废目录（由 BossFilter 并联失效调用）。
        /// 只作废不重建：重建放到下次 EnsureBuilt（面板打开或下次 bootstrap），
        /// 避免玩家在筛选面板里连点时每次都全量扫 preset。
        /// </summary>
        internal void NotifyEnemyPresetsRefreshed()
        {
            try
            {
                CodexBossCatalog.Invalidate();
                // 面板开着时目录已经渲染出来了，必须当场重画，否则玩家看到的是旧池。
                // 这里用 IsInstanceAlive 而不是 Instance != null：Instance 是会
                // 自动建实例的门面，拿它判空等于在筛选回调里凭空造一个面板。
                if (CodexView.IsInstanceAlive && CodexView.Instance.IsOpen)
                {
                    CodexBossCatalog.EnsureBuilt(_owner);
                    CodexView.Instance.RefreshAll();
                }
            }
            catch (Exception e)
            {
                LogFailure("presets_refreshed", e);
            }
        }

        /// <summary>调试导出用的目录快照（F3 DumpCodexCatalog）。</summary>
        internal IList<CodexBossInfo> GetCatalogSnapshot()
        {
            try
            {
                CodexBossCatalog.EnsureBuilt(_owner);
                return CodexBossCatalog.All;
            }
            catch (Exception e)
            {
                LogFailure("catalog_snapshot", e);
                return null;
            }
        }

        #endregion

        #region 诊断

        /// <summary>
        /// 面板开着就收掉。不销毁实例（下次打开还要用）。
        /// 走 IsInstanceAlive 判存活：Instance 会自动建实例，
        /// 在场景回调/关开关路径上凭空造面板是明确的 bug。
        /// </summary>
        private static void CloseViewIfOpen()
        {
            try
            {
                if (CodexView.IsInstanceAlive && CodexView.Instance.IsOpen)
                {
                    CodexView.Instance.Close();
                }
            }
            catch (Exception)
            {
                // 收面板失败不影响生命周期推进
            }
        }

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 运行时模块 "
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

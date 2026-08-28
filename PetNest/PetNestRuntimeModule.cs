// ============================================================================
// PetNestRuntimeModule.cs - 遗种巢运行时模块宿主（实施计划 步骤 3）
// ============================================================================
// 硬约束（tests/PetNestRuntimeModuleGuard.py 守卫）：
//   - 全系统只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
//     BossRushRuntimeModuleHost；建筑、面板、掉落与场景回调都只能委托这份实例，
//     禁止再次 new（照 Mode H 的写法；Mode G 的入口实例/host 实例分裂是反例，
//     见 Common/Lifecycle/BossRushRuntimeModuleRegistration.cs 的注释）；
//   - 只复用 host 已有的六个回调，不新增全局 hook；
//   - **petNestEnabled = false 时全系统 dormant**：不订阅存档、不建血脉目录、
//     不 tick 协调器、不生成任何东西。开关是运行时可变的（ModConfig 单键回调），
//     因此 bootstrap 走幂等的 EnsureBootstrapped，而不是只在 Awake 判一次。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>遗种巢运行时模块。宿主六回调的唯一落点。</summary>
    internal sealed class PetNestRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;
        private bool _lastEnabledState;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return PetNestTuning.ModuleName; } }

        /// <summary>宿主 owner。</summary>
        internal ModBehaviour Owner { get { return _owner; } }

        /// <summary>当前 scene generation。</summary>
        internal int SceneGeneration { get { return _sceneGeneration; } }

        /// <summary>
        /// 入口总开关（只经 owner getter 读取，禁止缓存可写副本）。
        /// </summary>
        internal bool IsEnabled
        {
            get
            {
                try
                {
                    return _owner != null && _owner.IsPetNestConfiguredEnabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>是否已完成一次 bootstrap（订阅存档 + 建目录）。</summary>
        internal bool IsBootstrapped { get { return _bootstrapped; } }

        #endregion

        #region host 六回调

        /// <summary>host Awake：记 owner。开关开启时才 bootstrap。</summary>
        public override void OnAwake(ModBehaviour owner)
        {
            try
            {
                _owner = owner;
                _bootstrapped = false;
                _lastEnabledState = false;
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
        /// host 场景回调：推进 scene generation。
        /// 关闭时只推进代数，不做任何订阅或生成（dormant）。
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

                // 回基地：把「本局重伤退场」复位为在巢待命
                if (IsBaseScene())
                {
                    PetNestService.RestoreDownedPetsOnReturnToBase();
                }
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>host tick：只驱动存档协调器的 deferred 重试。关闭时零成本早返。</summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped) return;
            try
            {
                if (!IsEnabled)
                {
                    ShutdownIfEnabledTurnedOff();
                    return;
                }
                PetNestSaveCoordinator.Tick();
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
                    PetNestSaveCoordinator.TryFlushOnHostDestroy();
                    PetNestSaveCoordinator.ShutdownSubscription();
                }
                PetNestLineageCatalog.Invalidate();
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
        /// 幂等 bootstrap：只在开关开启时订阅存档并建血脉目录。
        /// 开关是运行时可变的，因此每个回调都调它，而不是只在 Awake 判一次。
        /// </summary>
        internal void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            if (!IsEnabled) return;

            PetNestSaveCoordinator.EnsureSubscribed();
            PetNestLineageCatalog.EnsureBuilt(_owner);
            _bootstrapped = true;
            _lastEnabledState = true;
            ModBehaviour.DevLog("[PetNest] 运行时模块已启动，血脉条目数="
                + PetNestLineageCatalog.Count);
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：退订、作废目录、回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            if (!_bootstrapped) return;
            try
            {
                PetNestSaveCoordinator.TryFlushOnHostDestroy();
                PetNestSaveCoordinator.ShutdownSubscription();
                PetNestLineageCatalog.Invalidate();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }
            _bootstrapped = false;
            _lastEnabledState = false;
            ModBehaviour.DevLog("[PetNest] 入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 运行时模块 " + stage + " 异常: " + e.Message);
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

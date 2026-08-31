// ============================================================================
// BackMountainRuntimeModule.cs - 竞技场后山运行时模块宿主（M0 骨架）
// ============================================================================
// 硬约束（tests/BackMountainStructureGuard.py 守卫）：
//   - 全系统只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
//     BossRushRuntimeModuleHost；建筑、面板、作物注入与曲目追加都只能委托
//     这份实例，禁止再次 new（照 Mode H / PetNest 的写法）；
//   - 只复用 host 已有的六个回调，不新增全局 hook；
//   - **backMountainEnabled = false 时全系统 dormant**：不注入作物数据、
//     不注册建筑、不追加点唱机曲目、不订阅战役事件。开关运行时可变，
//     因此 bootstrap 走幂等的 EnsureBootstrapped。
//
// M0 的 bootstrap 只订阅战役解锁事件并留出即时解锁回调；
// 真正的设施接入按里程碑逐个挂进 HandleFacilityUnlocked 与 OnSceneLoaded。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>竞技场后山运行时模块。宿主六回调的唯一落点。</summary>
    internal sealed class BackMountainRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return BackMountainConfig.ModuleName; } }

        /// <summary>宿主 owner。</summary>
        internal ModBehaviour Owner { get { return _owner; } }

        /// <summary>当前 scene generation。</summary>
        internal int SceneGeneration { get { return _sceneGeneration; } }

        /// <summary>入口总开关（只经 owner getter 读取，禁止缓存可写副本）。</summary>
        internal bool IsEnabled
        {
            get
            {
                try
                {
                    return _owner != null && _owner.IsBackMountainConfiguredEnabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>是否已完成一次 bootstrap。</summary>
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
        ///
        /// 这里是后山「全量重查解锁状态」的正规时机：战役读档灌入 token 时按契约
        /// 不发事件，因此每次进基地都必须重查一遍，而不是只信事件。
        /// 关闭时只推进代数并回到 dormant。
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
                RefreshFacilitiesForScene();
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>
        /// host tick：驱动 bootstrap 重试与开关状态跟随。
        /// 关闭状态下是两次 bool + 一次 no-throw getter 的 O(1) 早返。
        /// </summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped)
            {
                EnsureBootstrapped();
                if (!_bootstrapped) return;
            }
            try
            {
                if (!IsEnabled)
                {
                    ShutdownIfEnabledTurnedOff();
                }
            }
            catch (Exception e)
            {
                LogFailure("update", e);
            }
        }

        /// <summary>host 销毁：退订战役事件并清静态缓存。</summary>
        public override void OnDestroy()
        {
            try
            {
                RaidMealService.ResetStaticCaches();
                GardenSeedInjector.ResetStaticCaches();
                ShowcaseService.ResetStaticCaches();
                ShowcaseUI.ResetStaticCaches();
                BackMountainItems.ResetStaticCaches();
                BackMountainUnlocks.ResetStaticCaches();
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
        /// 幂等 bootstrap：只在开关开启时订阅战役解锁事件。
        /// 开关是运行时可变的，因此每个回调都调它。
        /// </summary>
        internal void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            if (!IsEnabled) return;

            BackMountainUnlocks.EnsureSubscribed(_owner, HandleFacilityUnlocked);

            _bootstrapped = true;
            ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "运行时模块已启动");
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：退订、回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            if (!_bootstrapped) return;
            try
            {
                // 关掉开关也要摘掉已生效的加成，否则它们会一直挂在角色身上
                RaidMealService.ClearForRun();
                ShowcaseService.ClearBonuses();
                ShowcaseUI.Close();
                BackMountainUnlocks.Shutdown();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }
            _bootstrapped = false;
            ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 设施接入

        /// <summary>
        /// 每次场景加载时按当前解锁状态刷新设施。
        /// 菜地、点唱机、展示柜三者的注入都必须自身幂等——每次进基地都会走到这里。
        /// </summary>
        private void RefreshFacilitiesForScene()
        {
            try
            {
                bool isBase = IsBaseScene();

                if (isBase)
                {
                    // 回基地：清掉上一局的出击餐加成（角色已重建，记录必须作废）
                    RaidMealService.ClearForRun();
                }
                else
                {
                    // 进局：应用登记的出击餐。放在场景回调而不是等 tick，
                    // 是为了尽早生效——晚一秒玩家可能已经开打了。
                    RaidMealService.ApplyForRun();
                }

                // 展示柜加成每次场景就绪都重挂一遍：角色是新的，上一个角色身上的
                // Modifier 记录已经失效，ReapplyBonuses 内部会先摘干净再挂。
                ShowcaseService.ReapplyBonuses();

                if (!BackMountainUnlocks.IsAnyFacilityUnlocked()) return;
                if (!isBase) return;

                // 三个设施的注入都必须自身幂等：每次进基地都会走到这里
                GardenSeedInjector.EnsureInjected();
                JukeboxTrackInjector.EnsureInjected();
                if (_owner != null) _owner.InitBackMountainShowcase();
            }
            catch (Exception e)
            {
                LogFailure("refresh_facilities", e);
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

        /// <summary>
        /// 战役实时授予了某设施的 token：当场接入，不必等玩家重进基地。
        /// 走 RefreshFacilitiesForScene 复用同一条幂等注入路径。
        /// </summary>
        private void HandleFacilityUnlocked(BackMountainFacility facility)
        {
            try
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "设施解锁: " + facility);
                RefreshFacilitiesForScene();
            }
            catch (Exception e)
            {
                LogFailure("facility_unlocked", e);
            }
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 运行时模块 "
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

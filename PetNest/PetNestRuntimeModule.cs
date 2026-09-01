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
        private bool _baseMaintenancePending;
        private float _nextBaseMaintenanceTime;
        private float _lastHomecomingSettleTime;

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
                CloseAllInteractiveViewsForSceneChange();
                // 跨局去重集合必须清，否则累积死引用
                PetNestMuseumStats.ClearCountedKills();
                PetNestProgressionService.ClearSceneKillDedup();
                // 掉落追踪同样按局清（CR-2026-08-29-020）：未死亡也未被逐只清理的 Boss
                //（中途弃局、直接撤离、切图销毁）在 _hooks 里的条目永不移除，
                // 长会话跨多局无上限累积（死角色 key + 捕获 owner/character 的委托）。
                // 上一局的角色已随场景销毁，这里整表清空不会误清本局的追踪：
                // Boss 是场景加载完成之后才生成并注册的。
                PetNestDropService.ClearAllTracking();
                if (!IsEnabled)
                {
                    // 关掉开关也要把上一局的随从、闲逛崽与借席清干净
                    PetNestBaseIdleSpawner.CleanupAll();
                    PetNestCompanionRuntime.CleanupOnce();
                    ShutdownIfEnabledTurnedOff();
                    return;
                }
                EnsureBootstrapped();

                // 回基地：把「本局重伤退场」复位为在巢待命
                if (IsBaseScene())
                {
                    // 必须在任何读血脉目录的一步之前：会话重启后 enemyPresets 还是空的，
                    // 目录里一个官方血脉都没有（CR-2026-08-29-015）。
                    EnsureOfficialLineagesPrimed();
                    // sceneLoaded 早于主角、经济与 ItemAssets 准备完成；其余基地工作延后到
                    // OnUpdate 的 LevelManager.AfterInit 门控，避免一次场景回调烧光奖励重试。
                    _baseMaintenancePending = true;
                    _nextBaseMaintenanceTime = 0f;
                    return;
                }

                // 离开基地：闲逛崽全清；演出层宿主是 DontDestroyOnLoad，必须显式停，
                // 否则翻牌/孵化演出会跟着过图，用全屏遮罩盖住战斗
                _baseMaintenancePending = false;
                _nextBaseMaintenanceTime = 0f;
                PetNestBaseIdleSpawner.RefreshForScene(_owner, _sceneGeneration, false);
                PetNestExpeditionRevealView.Stop();
                PetNestHatchRevealView.Stop();

                // 切图：先清上一局，再按出战席位与门控决定是否入场
                PetNestCompanionRuntime.OnSceneChanged(_owner, _sceneGeneration);
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>host tick：只驱动存档协调器的 deferred 重试。关闭时零成本早返。</summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped)
            {
                // 开关运行时可变：关掉再打开必须当帧复活，否则要等到下次切场景。
                // 关闭状态下仍是两次 bool + 一次 no-throw getter 的 O(1) 早返。
                EnsureBootstrapped();
                if (!_bootstrapped) return;
            }
            try
            {
                if (!IsEnabled)
                {
                    // 与 OnSceneLoaded 的关闭分支对齐：不清的话随从会留在场上，
                    // 而 DownedHandler.Tick 已经停摆（致死钳只剩 1.5s 兜底解无敌，不退场）。
                    PetNestBaseIdleSpawner.CleanupAll();
                    PetNestCompanionRuntime.CleanupOnce();
                    ShutdownIfEnabledTurnedOff();
                    return;
                }
                PetNestDownedHandler.Tick();
                if (IsBaseScene()) TickBaseMaintenance();
                // 入场重试：模式标志通常晚于 sceneLoaded 才置位，只采样一次会永远进不了场
                if (!IsBaseScene())
                {
                    PetNestCompanionRuntime.TickSpawnRetry(_owner, _sceneGeneration);
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
                CloseAllInteractiveViewsForSceneChange();
                PetNestBaseIdleSpawner.ResetStaticCaches();
                PetNestMuseumStats.ResetStaticCaches();
                PetNestCompanionRuntime.CleanupOnce();
                PetNestUI.ResetStaticCaches();
                PetNestRenameModal.ResetStaticCaches();
                PetNestReleaseConfirmModal.ResetStaticCaches();
                PetNestProgressionService.ResetStaticCaches();
                PetNestHatchRevealView.ResetStaticCaches();
                PetNestExpeditionRevealView.ResetStaticCaches();
                PetNestCompanionHudView.ResetStaticCaches();
                if (_bootstrapped)
                {
                    PetNestSaveCoordinator.TryFlushOnHostDestroy();
                    PetNestSaveCoordinator.ShutdownSubscription();
                }
                PetNestLineageCatalog.Invalidate();
                // 与 RegisterOpener 成对：面板关掉并把打开器注销，避免 dormant 后还能开面板
                PetNestUI.UnregisterOpener();
                PetNestUIBridge.UnbindRuntime();
                _baseMaintenancePending = false;
                _nextBaseMaintenanceTime = 0f;
                _bootstrapped = false;
                _owner = null;
            }
            catch (Exception e)
            {
                LogFailure("destroy", e);
            }
        }

        #endregion

        private void TickBaseMaintenance()
        {
            if (!_baseMaintenancePending && !PetNestExpeditionService.HasPendingRewardDebt) return;
            if (UnityEngine.Time.unscaledTime < _nextBaseMaintenanceTime) return;
            if (LevelManager.Instance == null || !LevelManager.AfterInit
                || CharacterMainControl.Main == null) return;

            _nextBaseMaintenanceTime = UnityEngine.Time.unscaledTime + 5f;
            try
            {
                if (_baseMaintenancePending)
                {
                    PetNestExpeditionService.ReconcileOrphanedExpeditionLocks();
                    // 归巢经验结算加冷却，防止玩家在非竞技场其他场景（Raid）长时间滞留后
                    // 回基地一次性获得大量经验。10s 冷却足够隔离连续切场景的误触。
                    float now = UnityEngine.Time.unscaledTime;
                    if (now - _lastHomecomingSettleTime >= PetNestTuning.HomecomingSettleCooldownSeconds)
                    {
                        PetNestProgressionService.SettleRunHomecoming(
                            PetNestCompanionRuntime.ActiveCompanionPetId);
                        _lastHomecomingSettleTime = now;
                    }
                    PetNestService.RestoreDownedPetsOnReturnToBase();
                    PetNestCompanionRuntime.CleanupOnce();
                    PetNestExpeditionService.SettleDueExpeditions();
                    PetNestBaseIdleSpawner.RefreshForScene(_owner, _sceneGeneration, true);
                    _baseMaintenancePending = false;
                }
                PetNestExpeditionService.TryGrantPendingRewards();
                PetNestExpeditionRevealView.PlayPending();
            }
            catch (Exception e)
            {
                LogFailure("base_maintenance", e);
                _baseMaintenancePending = true;
            }
        }

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
            PetNestUIBridge.BindRuntime(this);
            PetNestUI.RegisterOpener();
            _bootstrapped = true;
            ModBehaviour.DevLog("[PetNest] 运行时模块已启动，血脉条目数="
                + PetNestLineageCatalog.Count);
        }

        /// <summary>
        /// 敌人预设表填充完成、或 Boss 池过滤状态变化后的血脉目录重建入口。
        ///
        /// 为什么必须有它：目录在 bootstrap（ModBehaviour.Start）时构建，而
        /// <c>enemyPresets</c> 要等玩家第一次进竞技场才由 InitializeEnemyPresets 填充。
        /// 目录构建时读到的是空池，`_built` 一旦置位又永不重建，结果是全部官方 Boss
        /// 都不在目录里——不掉蛋、不记遗魂、已有的蛋孵不出、崽也带不进局。
        ///
        /// 未 bootstrap 时跳过是安全的：之后 EnsureBootstrapped 会用已填充的池建目录。
        /// 重建幂等且只发生在预设初始化与 Boss 池点选这类低频时刻，无每帧成本。
        /// </summary>
        internal void NotifyEnemyPresetsRefreshed()
        {
            try
            {
                if (!_bootstrapped) return;
                PetNestLineageCatalog.Invalidate();
                PetNestLineageCatalog.EnsureBuilt(_owner);
            }
            catch (Exception e)
            {
                LogFailure("presets_refreshed", e);
            }
        }

        /// <summary>
        /// 基地侧的血脉目录预热（CR-2026-08-29-015）。
        ///
        /// InitializeEnemyPresets 的调用点全在进竞技场路径与调试面板，基地启动一处都不触发；
        /// 血脉目录的资格口径又正是那张池，于是每次重启会话后、进第一次竞技场之前，
        /// 官方血脉在基地全面不可用（蛋孵出 lineage_unknown、巢页裸 key、遗魂账本缺行）。
        ///
        /// 4.12 门控：只在已 bootstrap（= 玩家开着遗种巢开关）且回到基地时做一次，
        /// 没开开关的玩家一次也不会为它付出全量预设扫描的成本；
        /// 池已填充后 owner 侧零成本早返，之后每次回基地都不再有额外开销。
        /// 填充成功由 owner 侧回调 NotifyEnemyPresetsRefreshed 重建目录，这里不重复建。
        /// </summary>
        private void EnsureOfficialLineagesPrimed()
        {
            try
            {
                if (!_bootstrapped || _owner == null) return;
                _owner.EnsureEnemyPresetsReadyForGameplayCatalogs();
            }
            catch (Exception e)
            {
                LogFailure("prime_presets", e);
            }
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：退订、作废目录、回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            // **必须在 _bootstrapped 早返之前**（CR-2026-08-29-016）：handler 是在
            // bootstrap 期间挂到 per-boss 事件上的，_bootstrapped 置回 false 之后它们
            // 仍然挂在场上的 Boss 身上。不清的话，竞技场中途关掉开关后，
            // 已追踪 Boss 死亡照样记遗魂/可能掉蛋/弹「可凝蛋」提示，
            // 违反「关闭即不产蛋不记魂」的 dormant 契约。
            // 两个关闭分支（OnSceneLoaded / OnUpdate）都经这里，是唯一的咽喉点；
            // 无追踪时 ClearAllTracking 自身是 O(1) 早返，不破坏关闭态零开销。
            PetNestDropService.ClearAllTracking();
            if (!_bootstrapped) return;
            try
            {
                CloseAllInteractiveViewsForSceneChange();
                PetNestSaveCoordinator.TryFlushOnHostDestroy();
                PetNestSaveCoordinator.ShutdownSubscription();
                // **必须清缓存**：退订会把 OnSetFile 一起摘掉，之后玩家切档没人清缓存；
                // 再打开开关时缓存里还是上一个档的崽与遗魂，一写就把旧档覆盖到新档上。
                PetNestPersistenceAccess.ResetCachesForSlotReload();
                PetNestLineageCatalog.Invalidate();
                // 与 RegisterOpener 成对：面板关掉并把打开器注销，避免 dormant 后还能开面板
                PetNestUI.UnregisterOpener();
                PetNestUIBridge.UnbindRuntime();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }
            _bootstrapped = false;
            _baseMaintenancePending = false;
            _nextBaseMaintenanceTime = 0f;
            ModBehaviour.DevLog("[PetNest] 入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 跨场景 UI 清理

        /// <summary>
        /// 关闭全部遗种巢交互层。所有关闭函数都幂等；集中在这里可保证过图、热关闭与
        /// 宿主销毁不会遗漏 DontDestroyOnLoad 根节点或模态输入租约。
        /// </summary>
        internal static void CloseAllInteractiveViewsForSceneChange()
        {
            try { PetNestUI.Close(); }
            catch (Exception) { /* 清理路径继续 */ }
            try { PetNestRenameModal.Close(); }
            catch (Exception) { /* 清理路径继续 */ }
            try { PetNestReleaseConfirmModal.Close(); }
            catch (Exception) { /* 清理路径继续 */ }
            try { PetNestHatchRevealView.Stop(); }
            catch (Exception) { /* 清理路径继续 */ }
            try { PetNestExpeditionRevealView.Stop(); }
            catch (Exception) { /* 清理路径继续 */ }
            try { PetNestCompanionHudView.Destroy(); }
            catch (Exception) { /* 清理路径继续 */ }
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

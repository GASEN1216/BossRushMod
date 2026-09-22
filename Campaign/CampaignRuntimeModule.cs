// ============================================================================
// CampaignRuntimeModule.cs - 鸭王征程运行时模块宿主（M0 骨架）
// ============================================================================
// 硬约束（tests/CampaignSkeletonGuard.py 守卫）：
//   - 全系统只有一个实例：由 ModBehaviour 持有并把**同一个引用**注册给
//     BossRushRuntimeModuleHost；官方任务客户端、契约追踪与终章决战都只能委托
//     这份实例，禁止再次 new（照 Mode H / PetNest 的写法）；
//   - 只复用 host 已有的六个回调，不新增全局 hook；
//   - **campaignEnabled = false 时全系统 dormant**：不订阅存档、不注入建筑、
//     不采集击杀、不发 token。开关运行时可变（ModConfig 单键回调），
//     因此 bootstrap 走幂等的 EnsureBootstrapped，而不是只在 Awake 判一次。
//
// M0 的 bootstrap 只做一件事：把解锁契约标成「已装载（空集）」，
// 让 CampaignFacilityUnlocks 的查询 API 在后山侧可用且答案正确（尚无章节完成）。
// M3 接入 CampaignPersistence 后，由存档读出的真实 token 集整体替换这个空集。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>鸭王征程运行时模块。宿主六回调的唯一落点。</summary>
    internal sealed class CampaignRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;
        /// <summary>官方任务客户端（六章投影到杰夫的任务页）。唯一 owner 是本模块。</summary>
        private CampaignOfficialQuestClient _questClient;

        #endregion

        #region 只读

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        public override string ModuleName { get { return CampaignTuning.ModuleName; } }

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
                    return _owner != null && _owner.IsCampaignConfiguredEnabled();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>是否已完成一次 bootstrap。</summary>
        internal bool IsBootstrapped { get { return _bootstrapped; } }

        /// <summary>Dev 演练：立刻刷杰夫任务页的 Task 与头顶标记（平时由核心 0.25 秒一拍自动做）。</summary>
        internal void SyncOfficialQuests()
        {
            if (_questClient != null) _questClient.NotifyProgressChanged();
        }

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
        /// 关闭时只推进代数并回到 dormant，不做任何订阅或注入。
        /// </summary>
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            try
            {
                _sceneGeneration++;
                CampaignObjectiveTracker.ResetSession();
                CampaignDialoguePlayer.InvalidatePlayback();
                if (_questClient != null) _questClient.ClearPending();

                // 场景已换 = 上一场决战无论输赢都结束了。玩家打输时 Boss 随场景销毁、
                // 死亡回调不会来，只有在这里收尾才能让 campaignFinalBossActive 复位，
                // 否则召唤石永远不再出现、终章再也打不了。收尾本身幂等。
                if (_owner != null)
                {
                    _owner.CleanupCampaignFinalBoss(false);
                }

                if (!IsEnabled)
                {
                    ShutdownIfEnabledTurnedOff();
                    return;
                }
                EnsureBootstrapped();
            }
            catch (Exception e)
            {
                LogFailure("scene_loaded", e);
            }
        }

        /// <summary>
        /// host tick：驱动 bootstrap 重试、开关跟随与目标追踪轮询。
        /// 关闭状态下是两次 bool + 一次 no-throw getter 的 O(1) 早返。
        /// </summary>
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!_bootstrapped)
            {
                // 开关运行时可变：关掉再打开必须当帧复活，否则要等到下次切场景。
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

                if (_owner != null)
                {
                    _owner.TickCampaignModeBridge(deltaTime);
                }
                CampaignHud.Tick();
                CampaignProgressService.RetryPendingObjectives(unscaledDeltaTime);
                CampaignSaveCoordinator.Tick();
            }
            catch (Exception e)
            {
                LogFailure("update", e);
            }
        }

        /// <summary>host 销毁：清静态缓存并回到未装载态。</summary>
        public override void OnDestroy()
        {
            try
            {
                if (_owner != null) _owner.CleanupCampaignFinalBoss(true);
                if (_questClient != null) _questClient.UnregisterAll();
                if (_bootstrapped)
                {
                    // 销毁是最后机会：绕过基地场景闸尽力落一次盘，宁可在战斗帧写一次
                    CampaignSaveCoordinator.TryFlushOnHostDestroy();
                    CampaignSaveCoordinator.ShutdownSubscription();
                }
                CampaignPersistence.ResetStaticCaches();
                CampaignSaveCoordinator.ResetStaticCaches();
                CampaignProgressService.ResetStaticCaches();
                CampaignObjectiveTracker.ResetStaticCaches();
                CampaignObjectiveCollector.ResetStaticCaches();
                CampaignContentCatalog.ResetStaticCaches();
                CampaignHud.ResetStaticCaches();
                CampaignDialoguePlayer.ResetStaticCaches();
                CampaignNoteBridge.ResetStaticCaches();
                CampaignAssetCache.ResetStaticCaches();
                CampaignFacilityUnlocks.ResetStaticCaches();
                CampaignBaseObjectives.ResetStaticCaches();
                _questClient = null;
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
        /// 幂等 bootstrap：只在开关开启时装载解锁契约。
        /// 开关是运行时可变的，因此每个回调都调它，而不是只在 Awake 判一次。
        /// </summary>
        internal void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            if (!IsEnabled) return;

            // 顺序要紧：先订阅存档事件，再让进度服务读档并把已授予 token
            // 发布给跨系统契约。反过来的话，读档发生在订阅之前，
            // 后续换档不会收到 OnSetFile，A 档的解锁会泄漏到 B 档。
            CampaignSaveCoordinator.EnsureSubscribed();
            CampaignProgressService.EnsureInitialized();

            _bootstrapped = true;
            // 六章登记进共享官方任务投影核心（挂在杰夫名下）。核心模块先于本模块注册，这里一定拿得到。
            if (_questClient == null) _questClient = new CampaignOfficialQuestClient(this);
            _questClient.RegisterAll(_owner != null && _owner.OfficialQuestRuntime != null
                ? _owner.OfficialQuestRuntime.Projection : null);
            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "运行时模块已启动");
        }

        /// <summary>
        /// 开关被玩家在运行时关掉：回到 dormant。
        /// 幂等；从未 bootstrap 过时 O(1) 早返。
        /// </summary>
        private void ShutdownIfEnabledTurnedOff()
        {
            if (!_bootstrapped) return;
            try
            {
                // 关掉开关也要把已入队的进度落下去，否则玩家刚交付的章节会丢
                CampaignSaveCoordinator.TryFlushOnHostDestroy();
                CampaignSaveCoordinator.ShutdownSubscription();
                if (_owner != null) _owner.CleanupCampaignFinalBoss(true);
                // 关掉开关即从杰夫的任务页撤走六章（核心先冻结所有权再清投影）
                if (_questClient != null) _questClient.UnregisterAll();
                CampaignObjectiveTracker.ResetSession();
                CampaignProgressService.ResetStaticCaches();
                CampaignDialoguePlayer.InvalidatePlayback();
                CampaignHud.ResetStaticCaches();
                // 必须复位解锁契约：否则关掉战役后，后山仍能查到 token 并保持设施可见，
                // 违反「关闭即 dormant」。同时清掉装载标记，让查询回到 fail-closed。
                CampaignFacilityUnlocks.ResetForSlotReload();
            }
            catch (Exception e)
            {
                LogFailure("shutdown_disabled", e);
            }
            _bootstrapped = false;
            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "入口开关已关闭，运行时模块回到 dormant");
        }

        #endregion

        #region 诊断

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 运行时模块 "
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

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
//
// 【两类刷新分属两个时机，不能合并】
//   1. **设施注入**（作物表/点唱机/展示柜建筑）只需要场景就绪，走 OnSceneLoaded。
//   2. **角色加成**（出击餐 Modifier、展示柜加成）需要主角**已经生成**。
//      官方主角由 LevelManager.CreateMainCharacterAsync 异步创建，sceneLoaded 那一刻
//      CharacterMainControl.Main 必然还是 null——在那里挂加成会静默失败且无重试，
//      表现为「饭吃了没效果」「登记了不加血」。因此角色相关的一律等
//      LevelManager.OnAfterLevelInitialized（官方 BuildingEffect 与本 mod 的
//      SetBonusManager / DragonSetBonus 用的都是这个时机）。
//
// 【换槽必须复位展示柜与菜地的按槽内存态】
//   ShowcaseService 的收藏是按槽存档的内存缓存。不复位的话，同一次会话里
//   从 A 档切到 B 档会让 A 档的收藏在 B 档继续生效，并在 B 档登记时
//   把「A 档收藏 + 新条目」整体写进 B 档存档——永久污染。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>竞技场后山运行时模块。宿主六回调的唯一落点。</summary>
    internal sealed class BackMountainRuntimeModule : BossRushRuntimeModuleBase
    {
        #region 状态

        private ModBehaviour _owner;
        private int _sceneGeneration;
        private bool _bootstrapped;

        /// <summary>关卡就绪事件的订阅幂等标记（AGENTS.md 4.6）。</summary>
        private bool _levelEventSubscribed;

        /// <summary>存档换槽事件的订阅幂等标记（AGENTS.md 4.6）。</summary>
        private bool _saveEventSubscribed;

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
        ///
        /// 角色相关的加成**不在这里挂**：此刻主角还没生成（异步创建），
        /// 那部分归 HandleLevelReady。这里只把上一个角色的残留记录清掉。
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

                // 上一个角色已随场景销毁，Modifier 记录整体作废；
                // 同时复位「本局已应用」标记，好让 HandleLevelReady 给新角色重挂一次
                ClearCharacterBoundEffects();
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

        /// <summary>host 销毁：退订全部事件并清静态缓存。</summary>
        public override void OnDestroy()
        {
            try
            {
                ShutdownSubscriptions();
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
            EnsureSubscriptions();

            _bootstrapped = true;
            ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "运行时模块已启动");

            // 可能在关卡初始化之后才 bootstrap（宿主重建、开关中途打开）：
            // 那一次 OnAfterLevelInitialized 已经放过去了，这里补一次，
            // 否则出击餐与展示柜加成要等到下次切场景才生效。
            if (IsLevelAfterInit())
            {
                RefreshCharacterBoundEffects();
            }
        }

        /// <summary>
        /// 幂等订阅两个官方事件。两者都用命名方法（AGENTS.md 4.6，lambda 退订不掉）。
        /// </summary>
        private void EnsureSubscriptions()
        {
            try
            {
                if (!_levelEventSubscribed)
                {
                    LevelManager.OnAfterLevelInitialized += HandleLevelReady;
                    _levelEventSubscribed = true;
                }
                if (!_saveEventSubscribed)
                {
                    SavesSystem.OnSetFile += HandleSaveSlotChanged;
                    SavesSystem.OnSaveDeleted += HandleSaveSlotChanged;
                    _saveEventSubscribed = true;
                }
            }
            catch (Exception e)
            {
                LogFailure("subscribe", e);
            }
        }

        /// <summary>幂等退订。dormant 与宿主销毁两条路径都必须调。</summary>
        private void ShutdownSubscriptions()
        {
            try
            {
                if (_levelEventSubscribed)
                {
                    LevelManager.OnAfterLevelInitialized -= HandleLevelReady;
                    _levelEventSubscribed = false;
                }
                if (_saveEventSubscribed)
                {
                    SavesSystem.OnSetFile -= HandleSaveSlotChanged;
                    SavesSystem.OnSaveDeleted -= HandleSaveSlotChanged;
                    _saveEventSubscribed = false;
                }
            }
            catch (Exception e)
            {
                // 退订失败也要把标记置回，避免重复订阅越滚越多
                _levelEventSubscribed = false;
                _saveEventSubscribed = false;
                LogFailure("unsubscribe", e);
            }
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
                ShutdownSubscriptions();
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
        /// 按当前解锁状态注入三个设施。
        /// 菜地、点唱机、展示柜三者的注入都必须自身幂等——每次进基地都会走到这里。
        ///
        /// **只做注入，不碰角色加成**：那部分依赖主角就绪，归 HandleLevelReady。
        /// 本方法也被实时解锁回调复用，在那条路径上摘加成是错的（玩家就在基地站着）。
        /// </summary>
        private void RefreshFacilitiesForScene()
        {
            try
            {
                if (!BackMountainUnlocks.IsAnyFacilityUnlocked()) return;
                if (!IsBaseScene()) return;

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

        /// <summary>
        /// 关卡初始化完成（主角已生成、Stat 已就绪）：挂上角色相关的两类加成。
        /// 这是官方给建筑加成用的同一个时机，也是 SetBonusManager / DragonSetBonus
        /// 处理「已穿戴装备进入游戏」用的时机。
        /// </summary>
        private void HandleLevelReady()
        {
            try
            {
                if (!IsEnabled) return;
                RefreshCharacterBoundEffects();
            }
            catch (Exception e)
            {
                LogFailure("level_ready", e);
            }
        }

        /// <summary>
        /// 按当前场景挂/摘角色相关加成。要求主角已存在，只能由关卡就绪路径调用。
        /// </summary>
        private void RefreshCharacterBoundEffects()
        {
            try
            {
                if (IsBaseScene())
                {
                    // 回基地：上一局的出击餐加成随角色一起作废（登记已在进局时消费）
                    RaidMealService.ClearForRun();
                }
                else
                {
                    // 进局：应用登记的出击餐，并消费掉登记——一顿饭只管一局
                    RaidMealService.ApplyForRun();
                }

                // 展示柜加成每关重挂一遍：角色是新的，ReapplyBonuses 内部先摘干净再挂
                ShowcaseService.ReapplyBonuses();
            }
            catch (Exception e)
            {
                LogFailure("character_effects", e);
            }
        }

        /// <summary>
        /// 摘掉角色相关加成并复位「本局已应用」标记。
        /// 切场景时调用：记录指向的角色已经销毁，留着只会让下一局重复挂。
        /// </summary>
        private void ClearCharacterBoundEffects()
        {
            try
            {
                RaidMealService.ClearForRun();
                ShowcaseService.ClearBonuses();
            }
            catch (Exception e)
            {
                LogFailure("clear_character_effects", e);
            }
        }

        /// <summary>
        /// 换槽 / 删档：复位按槽的内存态。
        /// 不复位会让 A 档的展示柜收藏在 B 档继续生效，并在 B 档登记时写脏 B 档存档。
        /// </summary>
        private void HandleSaveSlotChanged()
        {
            try
            {
                ShowcaseService.NotifySlotChanged();
                // 新槽的菜地棘轮状态不同，必须重新判定（不从官方表里摘条目）
                GardenSeedInjector.NotifySlotChanged();
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "存档槽已切换，后山按槽状态已复位");
            }
            catch (Exception e)
            {
                LogFailure("slot_changed", e);
            }
        }

        /// <summary>关卡是否已完成初始化（主角已就绪）。no-throw。</summary>
        private static bool IsLevelAfterInit()
        {
            try
            {
                return LevelManager.AfterInit;
            }
            catch (Exception)
            {
                return false;
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
                // 解锁发生在基地交付契约的那一刻，主角就在场：展示柜加成当场生效，
                // 不必等玩家重进一次基地
                RefreshCharacterBoundEffects();
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

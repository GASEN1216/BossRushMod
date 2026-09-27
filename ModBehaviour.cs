using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using SodaCraft.Localizations;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.Utilities;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;
using HarmonyLib;

namespace BossRush
{
    /// <summary>
    /// Boss Rush Mod
    /// 继承自游戏的ModBehaviour基类
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 单例
        public static ModBehaviour Instance { get; private set; }
        private readonly BossRushRuntimeModuleHost runtimeModuleHost = new BossRushRuntimeModuleHost();

        // 地图刷新点注册表（JSON 数据源）
        private readonly BossRushMapRuntime mapRuntime = new BossRushMapRuntime();

        // ============================================================================
        // BossRush map config runtime data
        // JSON is the only runtime source for map configs.
        // ============================================================================

        // 当前地图使用的刷新点（根据场景动态选择）


        // ============================================================================
        // BossRush 地图配置查询方法
        // ============================================================================

        /// <summary>
        /// 根据运行时场景名获取地图配置
        /// </summary>
        public static BossRushMapConfig GetMapConfigBySceneName(string sceneName)
        { return BossRushMapRuntime.GetMapConfigBySceneName(sceneName); }

        /// <summary>
        /// 根据加载用场景ID获取地图配置
        /// </summary>
        public static BossRushMapConfig GetMapConfigBySceneID(string sceneID)
        { return BossRushMapRuntime.GetMapConfigBySceneID(sceneID); }

        /// <summary>
        /// 获取当前场景的地图配置
        /// </summary>
        public static BossRushMapConfig GetCurrentMapConfig()
        { return BossRushMapRuntime.GetCurrentMapConfig(); }

        /// <summary>
        /// 获取所有地图配置
        /// </summary>
        public static BossRushMapConfig[] GetAllMapConfigs()
        { return BossRushMapRuntime.GetAllMapConfigs(); }

        /// <summary>
        /// 检查指定场景是否是有效的 BossRush 竞技场场景
        /// </summary>
        public bool IsValidBossRushArenaScene(string sceneName)
        { return mapRuntime.IsValidBossRushArenaScene(sceneName); }

        /// <summary>
        /// 检查当前场景是否是有效的 BossRush 竞技场场景
        /// </summary>
        public bool IsCurrentSceneValidBossRushArena()
        { return mapRuntime.IsCurrentSceneValidBossRushArena(); }

        /// <summary>
        /// 获取指定场景的刷新点
        /// </summary>
        public static Vector3[] GetSpawnPointsForScene(string sceneName)
        { return BossRushMapRuntime.GetSpawnPointsForScene(sceneName); }

        /// <summary>
        /// 公共 NPC 共享的刷新/漫步点池。
        /// Mode E 和普通模式优先复用快递员普通模式点位；其他 BossRush 相关模式使用地图 Boss 刷新点池。
        /// </summary>
        public static Vector3[] GetSharedCommonNPCSpawnPointsForScene(string sceneName)
        { ModBehaviour mod = Instance; return CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene(sceneName, mod != null ? mod.commonNpcRuntime : null); }

        /// <summary>
        /// 当前是否应让公共 NPC 使用 BossRush 地图刷怪点池。
        /// </summary>
        public bool ShouldUseBossRushCommonNPCSpawnPoints(string sceneName = null)
        { return commonNpcRuntime.ShouldUseBossRushCommonNPCSpawnPoints(sceneName); }

        /// <summary>
        /// BossRush 激活且非 Mode E 时，仅随机刷新一个支援型公共 NPC。
        /// </summary>
        public bool ShouldUseRandomSupportNpcSelection(string sceneName = null)
        {
            return ShouldUseBossRushCommonNPCSpawnPoints(sceneName);
        }

        /// <summary>
        /// 获取当前场景的刷新点
        /// </summary>
        public Vector3[] GetCurrentSceneSpawnPoints()
        { return mapRuntime.GetCurrentSceneSpawnPoints(); }

        /// <summary>
        /// 获取当前场景的默认传送位置（用于玩家传送、路牌位置等）
        /// 优先使用 customSpawnPos，其次使用 defaultSignPos，最后使用 DEMO 竞技场默认位置
        /// </summary>
        public static Vector3 GetCurrentSceneDefaultPosition()
        { return BossRushMapRuntime.GetCurrentSceneDefaultPosition(); }

        /// <summary>
        /// 获取指定场景的默认传送位置
        /// </summary>
        public static Vector3 GetDefaultPositionForScene(string sceneName)
        { return BossRushMapRuntime.GetDefaultPositionForScene(sceneName); }

        // 公共方法：获取竞技场场景名称（DEMO竞技场，保留兼容）
        public string GetArenaSceneName()
        {
            return BossRushArenaSceneName;
        }

        // 公共方法：获取无间炼狱模式每波Boss数量（来自配置，带默认值）
        public int GetInfiniteHellBossesPerWaveFromConfig()
        {
            int value = 3;
            try
            {
                if (config != null && config.infiniteHellBossesPerWave > 0)
                {
                    value = config.infiniteHellBossesPerWave;
                }
            }
            catch {}
            return value;
        }

        // 配置当前BossRush模式（每波Boss数量）
        public void ConfigureBossRushMode(int bossesPerWave)
        {
            // 兼容旧调用：默认不是无间炼狱模式
            ConfigureBossRushMode(bossesPerWave, false);
        }

        // 配置当前BossRush模式（支持无间炼狱标记）
        public void ConfigureBossRushMode(int bossesPerWave, bool useInfiniteHell)
        { wavesArenaRuntime.ConfigureBossRushMode(bossesPerWave, useInfiniteHell); }

        private void EnsureAmmoShop()
        {
            EnsureAmmoShop_Utilities();
        }

        public void ShowAmmoShop()
        { bossRushIntegrationRuntime.ShowAmmoShop(); }

        // Boss管理
        private MonoBehaviour currentBoss
        {
            get { return wavesArenaRuntime.CurrentBoss; }
            set { wavesArenaRuntime.CurrentBoss = value; }
        }
        internal MonoBehaviour CurrentBossForWavesArena { get { return currentBoss; } }
        private MonoBehaviour playerCharacter { get { return wavesArenaRuntime.PlayerCharacter; } set { wavesArenaRuntime.PlayerCharacter = value; } }
        private int currentEnemyIndex
        {
            get { return wavesArenaRuntime.CurrentEnemyIndex; }
            set { wavesArenaRuntime.CurrentEnemyIndex = value; }
        }
        // 记录由 BossRush 自己生成的“大兴兴”Boss，用于区分原版 DEMO 地图刷出的同名 Boss
        private HashSet<CharacterMainControl> bossRushOwnedDaXingXing { get { return wavesArenaRuntime.OwnedDaXingXing; } }
        // 状态
        public bool IsActive { get { return wavesArenaRuntime != null && wavesArenaRuntime.IsActive; } }
        private void SetBossRushRuntimeActive(bool active)
        { wavesArenaRuntime.SetBossRushRuntimeActive(active); }

        // UI提示（字段定义已移动到 UIAndSigns 部分类中）

        // 交互相关
        private const string BossRushArenaSceneID = "Level_DemoChallenge_Main"; // 用于SceneLoader加载的场景ID
        private const string BossRushArenaSceneName = "Level_DemoChallenge_1";  // 实际运行时的场景名称

        // Base 主场景名。下面的 IsBaseHubSceneName 会把 Base / Base_SceneV2_Sub_01 /
        // 下水道等 active scene 一并视为同一个入口环境——那份完整清单在
        // SceneRuntimeGate 里，本文件只保留实际被引用的这一个常量，
        // 避免同一组场景名在两处各存一份副本、改一处漏一处。
        private const string BaseSceneName = "Base_SceneV2";
        private static readonly Vector3 BaseEntryPosition = new Vector3(101.73f, 0.02f, -59.46f);
        private static readonly Vector3 ArenaEntryPosition = new Vector3(236.76f, -4.98f, 170.26f);

        internal static bool IsBaseHubSceneName(string sceneName)
        {
            return SceneRuntimeGate.IsBaseHubSceneName(sceneName);
        }

        internal static bool IsGameplaySceneName(string sceneName)
        {
            return SceneRuntimeGate.IsGameplaySceneName(sceneName);
        }

        internal static bool CanRunGameplayRuntimeNow(string sceneName)
        {
            return SceneRuntimeGate.CanRunGameplayRuntimeNow(sceneName);
        }




        internal static bool CanRunGameplayRuntimeCached()
        { return SceneRuntimeGate.CanRunGameplayRuntimeCached(); }

        internal static bool ShouldRunGameplaySceneRuntimeHooks(string sceneName)
        {
            return CanRunGameplayRuntimeNow(sceneName);
        }

        internal bool IsBaseHubBoatInteractable(InteractableBase interactable)
        { return uiAndSignsRuntime.IsBaseHubBoatInteractable(interactable); }

        internal bool TryInjectBaseHubBoatInteractable(InteractableBase interactable)
        { return uiAndSignsRuntime.TryInjectBaseHubBoatInteractable(interactable); }

        // DevMode 仅保留源码硬编码开关，不再暴露给玩家配置。
        // 常规源码默认保持 false；本地开发调试请使用 compile_dev.bat 注入 BOSSRUSH_DEV。
        private const bool HardcodedDevModeEnabled = false;

        internal static bool DevModeEnabled
        {
            get
            {
#if BOSSRUSH_DEV
                return true;
#else
                return HardcodedDevModeEnabled;
#endif
            }
        }





        // 手动维护的掉落黑名单（不希望进入 Boss 掉落 / 通关奖励池的物品 ID）

        // 无间炼狱模式状态

        private static bool dynamicItemsInitialized { get { return IntegrationRuntimeModule.DynamicItemsInitialized; } set { IntegrationRuntimeModule.DynamicItemsInitialized = value; } }
        private static int bossRushTicketTypeId { get { return IntegrationRuntimeModule.BossRushTicketTypeId; } set { IntegrationRuntimeModule.BossRushTicketTypeId = value; } }

        // BossRush 进入 DEMO 挑战场景的来源标记
        private static bool bossRushArenaPlanned { get { return WavesArenaRuntimeModule.ArenaPlanned; } set { WavesArenaRuntimeModule.ArenaPlanned = value; } }  // 通过 BossRush 启动的 DEMO 挑战加载已发起但尚未完成
        private static bool bossRushArenaActive { get { return WavesArenaRuntimeModule.ArenaActive; } set { WavesArenaRuntimeModule.ArenaActive = value; } }   // 当前 DEMO 挑战场景是否处于 BossRush 控制之下

        /// <summary>
        /// 竞技场是否激活（通关后仍为true，直到离开场景）
        /// 用于子弹商店等功能在通关后仍可使用
        /// </summary>
        public bool IsBossRushArenaActive => bossRushArenaActive;

        private Vector3 demoChallengeStartPosition
        {
            get { return wavesArenaRuntime.DemoChallengeStartPosition; }
            set { wavesArenaRuntime.DemoChallengeStartPosition = value; }
        }

        // 单波生成模式
        // 每波生成的Boss数量和当前波次的Boss列表
        // 变异词条：单Boss模式回血用的临时列表（避免每帧分配）
        private readonly MutatorBossRegenRuntime mutatorBossRegenRuntime = new MutatorBossRegenRuntime();
        // 波次完整性自检计时器
        internal const float WaveIntegrityCheckInterval = 10f;
        // Mode E 独立自检计时器（Mode E 不激活 IsActive，需要单独计时）
        // 大兴兴清理定时器（只在 BossRush 进行期间启用）
        internal const float DaXingXingCleanInterval = 0.5f;

        // [性能优化] 角色缓存列表，避免每次清理时都调用 FindObjectsOfType
        // [性能优化] 缓存定时刷新计时器，用于捕获动态生成的敌人

        // [性能优化] 复用的销毁列表，避免每次清理时分配新的 List

        // [性能优化] 缓存 CharacterSpawnerRoot.created 字段的反射引用

        // Boss 掉落随机化相关

        // 是否已禁用spawner
        private bool spawnersDisabled { get { return wavesArenaRuntime.SpawnersDisabled; } set { wavesArenaRuntime.SpawnersDisabled = value; } }

        // [性能优化] 竞技场范围限制 - 以路牌为圆心的清理/禁用范围
        internal const float ARENA_RADIUS = 500f; // 竞技场半径（米）

        /// <summary>
        /// 根据地图配置设置竞技场中心位置
        /// 在禁用 spawner 和清理敌人之前调用，确保范围限制生效
        /// </summary>
        private void SetArenaCenterFromMapConfig(string sceneName)
        { wavesArenaRuntime.SetArenaCenterFromMapConfig(sceneName); }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            DevLog("[BossRush] 正在加载 Boss Rush Mod...");
            Instance = this;
            DontDestroyOnLoad(gameObject);
            RegisterRuntimeModules();
            runtimeModuleHost.OnAwake(this);

            InitializeBootstrapRuntime();
            InitializeAlwaysOnRuntime();

            RegisterPlayerLifecycleRuntimeEvents();

            InitializeDebugToolsRuntime();

            // 初始化成就系统和成就页面UI
            InitializeAchievementRuntime();
        }

        void OnGUI()
        {
            if (!CanRunGameplayThisFrame())
            {
                return;
            }

            // Boss 池配置窗口与变异词条 UI 现在都走 Unity UI Canvas，不再需要 OnGUI

            DrawDebugToolsRuntimeGui();
        }

        private bool CanRunGameplayThisFrame()
        {
            return CanRunGameplayRuntimeCached();
        }

        void Update()
        {
            bool runGameplaySceneHooks = CanRunGameplayThisFrame();

            TickAlwaysOnRuntime();

            if (!runGameplaySceneHooks)
            {
                return;
            }

            runtimeModuleHost.OnUpdate(Time.deltaTime, Time.unscaledDeltaTime);

            // 变异词条 overlay：uGUI 实现，按帧维护可见性与悬停详情。
            MutatorUI.Tick();

            // 龙套装冲刺检测
            TickEquipmentAbilityRuntime();

            // 无间炼狱现金磁铁吸附更新
            TickGameplaySupportRuntime();

            if (TickModeRuntimeGroup(Time.deltaTime, Time.unscaledDeltaTime))
            {
                return;
            }

            // 变异词条：Boss 回血 Tick
            mutatorBossRegenRuntime.Tick();

            if (f3DebugCheatMenuVisible)
            {
                return;
            }

            TickDebugToolsAfterModalGate();
        }

        void LateUpdate()
        {
            if (!CanRunGameplayThisFrame())
            {
                return;
            }

            runtimeModuleHost.OnLateUpdate();
            LateUpdateModeRuntimeGroup();
        }

        private static void InjectBossRushTicketLocalization()
        {
            InjectBossRushTicketLocalization_Integration();
        }

        private void InitializeDynamicItems()
        {
            InitializeDynamicItems_Integration();
        }

        private void InjectBossRushTicketIntoShops(string targetSceneName = null)
        {
            InjectBossRushTicketIntoShops_Integration(targetSceneName);
        }
        void Start()
        {
            StartIntegrationRuntime();
            runtimeModuleHost.OnStart();
        }

        void OnDestroy()
        {
            // Awake 拒绝的重复实例没有装配模块，也不能清理活动宿主的全局资源。
            if (!ReferenceEquals(Instance, this)) return;

            // Mode G 宿主销毁预备（加法分支，任务 #7）：至迟在 CleanupAchievementRuntime() 之前，
            // 先通知 Mode G 释放共享引用/登记。幂等、no-throw 由 Felix 侧保证；
            // 无 Mode G run 时 O(1) 早返。此处额外 try/catch 兜底，绝不影响后续清理。
            try
            {
                BossRush.ModeG.PrepareHostDestroy();
            }
            catch { }

            ShutdownLanguageChangeSubscription();

            CleanupDebugToolsOnDestroy();

            CleanupPlayerLifecycleRuntimeEvents();

            // 清理成就追踪事件
            CleanupAchievementRuntime();
            SafeRuntime.Run("BossRushAchievementManager.ResetStaticCaches", () => BossRushAchievementManager.ResetStaticCaches());
            SafeRuntime.Run("AchievementIconLoader.ResetStaticCaches", () => AchievementIconLoader.ResetStaticCaches());
            SafeRuntime.Run("BossRushUI.ResetStaticCaches", () => BossRushUI.ResetStaticCaches());
            // 必须先复位皮肤注入点再卸 bundle，否则注入点里留的是已销毁的 Sprite
            SafeRuntime.Run("BossRushUISkinLoader.Cleanup", () => BossRushUISkinLoader.Cleanup());
            SafeRuntime.Run("BossBgmCoordinator.ResetStaticCaches", () => BossBgmCoordinator.ResetStaticCaches());
            SafeRuntime.Run("MutatorUI.ResetStaticCaches", () => MutatorUI.ResetStaticCaches());

            // 遗种巢 / 日报 / 图鉴 / 随机事件 / 征程 / 后山 / Mode H 的宿主销毁清理各自只有
            // 一个 owner：对应 RuntimeModule 的 OnDestroy()，经下方 runtimeModuleHost.OnDestroy()
            // 到达。宿主不再逐条内联同一批 ResetStaticCaches（曾与模块各写一份、宿主先清，
            // 导致模块自己的落盘空转）。词缀锻造没有运行时模块，仍由具名方法收口。
            CleanupAffixForgeRuntimeOnDestroy();

            // 取消订阅好感度系统事件并保存数据
            CleanupAlwaysOnRuntimeOnDestroy();

            CleanupIntegrationRuntimeOnDestroy();
            CleanupModeRuntimeOnDestroy();
            runtimeModuleHost.OnDestroy();
            // 跨子系统的每帧落盘闸：必须等全部模块做完最后一次 TryFlushOnHostDestroy 之后再复位
            SafeRuntime.Run("BossRushSaveFileThrottle.ResetStaticCaches", () => BossRushSaveFileThrottle.ResetStaticCaches());
            HarmonyPatchGroupRegistrar.Clear();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (StoneOutpostSceneLease.IsResourceScene(scene)) return;
            if (BossRushMapSelectionHelper.ShouldIgnoreAuxiliarySceneLoad(scene, mode,
                bossRushArenaPlanned || bossRushArenaActive)) return;
            PrepareSceneRuntimeForLoad();

            // 场景切换时清理好感度系统UI缓存
            OnSceneUnloadAlwaysOnRuntime();
            CleanupEnemyRecoveryForSceneChange();
            CleanupModeRuntimeForSceneLoad(scene);

            // 场景切换时清理现金磁铁飞行状态
            CleanupCashMagnetForSceneChange();

            OnSceneLoadedDebugToolsRuntime(scene, mode);
            OnSceneLoadedIntegrationRuntime(scene, mode);
            runtimeModuleHost.OnSceneLoaded(new SceneRuntimeContext(scene, mode));
        }

        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup(Scene scene)
        {
            return WaitForLevelInitializedThenSetup_Integration(scene);
        }


        /// <summary>
        /// 尝试将 BossRush 相关文本注入到本地化管理器
        /// </summary>
        private void InjectLocalization()
        {
            InjectLocalization_Extra_Integration();
        }

        /// <summary>切语言重注入是否已订阅（幂等，AGENTS 4.6）。</summary>
        private bool languageChangeSubscribed;

        /// <summary>
        /// 幂等订阅官方语言切换事件。
        ///
        /// 全 Mod 的注入型文案都是在注入那一刻用 `L10n.T(中,英)` 求值后写进
        /// `LocalizationManager.SetOverrideText` 的，值被烘死；玩家中途切语言时
        /// 官方自己的文案会跟着变，而 Mod 新增内容的物品名、交互名、Mode H 全部文案
        /// 仍停在旧语言。注入本身是字典覆盖写，天然幂等，重跑一遍即可。
        /// </summary>
        private void EnsureLanguageChangeSubscription()
        {
            if (languageChangeSubscribed) return;
            try
            {
                LocalizationManager.OnSetLanguage += OnGameLanguageChanged;
                languageChangeSubscribed = true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 订阅语言切换失败: " + e.Message);
            }
        }

        /// <summary>幂等退订。宿主销毁路径必须调。</summary>
        private void ShutdownLanguageChangeSubscription()
        {
            if (!languageChangeSubscribed) return;
            try
            {
                LocalizationManager.OnSetLanguage -= OnGameLanguageChanged;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 退订语言切换失败: " + e.Message);
            }
            finally
            {
                languageChangeSubscribed = false;
            }
        }

        private void OnGameLanguageChanged(SystemLanguage language)
        {
            try
            {
                DevLog("[BossRush] 语言切换，重注入本地化: " + language);
                InjectLocalization();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 语言切换重注入失败: " + e.Message);
            }
        }

        /// <summary>
        /// 从交互系统启动 BossRush
        /// </summary>
        public void StartBossRushFromInteraction(BossRushInteractable interactionSource)
        {
            DevLog("[BossRush] 从交互系统启动BossRush");
            if (interactionSource != null)
            {
                ConfigureBossRushMode(interactionSource.bossesPerWave);
            }
            StartBossRush(interactionSource);
        }

        internal static InteractableLootbox GetLootBoxTemplateWithLoader()
        { return WavesArenaRuntimeModule.GetLootBoxTemplateWithLoader(); }

        internal static InteractableLootbox GetDifficultyRewardLootBoxTemplate()
        { return WavesArenaRuntimeModule.GetDifficultyRewardLootBoxTemplate(); }

        internal void ApplyLootBoxCoverSetting(InteractableLootbox lootbox, bool ignoreConfig = false)
        { wavesArenaRuntime.ApplyLootBoxCoverSetting(lootbox, ignoreConfig); }

        /// <summary>
        /// 开始Boss Rush模式
        /// </summary>
        public void StartBossRush(BossRushInteractable interactionSource = null)
        {
            StartBossRush_WavesArena(interactionSource);
            return;
        }

        private void TeleportToBossRushAsync()
        {
            TeleportToBossRushAsync_WavesArena();
            return;
        }

        /// <summary>
        /// 在指定位置生成敌人（使用CharacterRandomPreset）
        /// 返回生成的角色，如果失败返回 null
        /// </summary>
        private UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync(EnemyPresetInfo preset, Vector3 position, Func<bool> isActiveCheck = null)
        { return wavesArenaRuntime.SpawnEnemyAtPositionAsync(preset, position, isActiveCheck); }

        /// <summary>
        /// 对无间炼狱模式下生成的Boss应用按波次递增的生命值与伤害强化
        /// </summary>
        private void ApplyInfiniteHellScaling(CharacterMainControl character, EnemyPresetInfo preset)
        { wavesArenaRuntime.ApplyInfiniteHellScaling(character, preset); }

        /// <summary>
        /// 判断预设是否为“大兴兴”Boss（通过显示名或内部名称粗略匹配）
        /// </summary>
        private bool IsDaXingXingPreset(EnemyPresetInfo preset)
        { return wavesArenaRuntime.IsDaXingXingPreset(preset); }

        /// <summary>
        /// 在 BossRush 期间清理任何非 BossRush 召唤的“大兴兴”Boss
        /// （用于屏蔽 DEMO 挑战地图自带的固定点刷“大兴兴”逻辑）
        /// </summary>
        internal void TryCleanNonBossRushDaXingXing()
        { wavesArenaRuntime.TryCleanNonBossRushDaXingXing(); }

        /// <summary>
        /// [性能优化] 刷新角色缓存列表
        /// 场景加载时立即刷新，之后每隔一段时间定时刷新以捕获动态生成的敌人
        /// </summary>
        private void RefreshCharacterCache()
        { WavesArenaRuntimeModule.RefreshCharacterCache(); }

        /// <summary>
        /// 获取CharacterRandomPreset的Team（直接访问public字段）
        /// </summary>
        private int GetPresetTeam(CharacterRandomPreset preset)
        { return wavesArenaRuntime.GetPresetTeam(preset); }


        private void TryCreateReturnInteractable()
        {
            TryCreateReturnInteractable_WavesArena();
        }

        private void UpdateMessage()
        {
            UpdateMessage_UIAndSigns();
        }

        public void ShowMessage(string msg)
        {
            ShowMessage_UIAndSigns(msg);
        }

        /// <summary>
        /// 显示敌人生成横幅
        /// 单Boss模式：显示名字 + 方位
        /// 多Boss模式（同一波多个Boss同时刷新）：显示“已将你包围”提示，不显示方向
        /// </summary>
        private void ShowEnemyBanner(string enemyName, Vector3 enemyPos, Vector3 playerPos)
        {
            ShowEnemyBanner_UIAndSigns(enemyName, enemyPos, playerPos, currentEnemyIndex, totalEnemies, infiniteHellMode, infiniteHellWaveIndex, bossesPerWave);
        }

        /// <summary>
        /// 显示大横幅（使用游戏通知系统）
        /// </summary>
        public void ShowBigBanner(string text)
        {
            ShowBigBanner_UIAndSigns(text);
        }


        public void ReturnToBossRushStart()
        { wavesArenaRuntime.ReturnToBossRushStart(); }
    }

    public class EnemyPresetInfo
    {
        public string name;
        public string displayName;
        public int team;
        public float baseHealth;
        public float baseDamage;
        public float healthMultiplier = 1f;
        public float damageMultiplier = 1f;
        public int expReward;
    }
}

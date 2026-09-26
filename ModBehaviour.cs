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
        private static readonly MapSpawnPointRegistry _mapSpawnRegistry = new MapSpawnPointRegistry();

        // ============================================================================
        // BossRush map config runtime data
        // JSON is the only runtime source for map configs.
        // ============================================================================

        // 当前地图使用的刷新点（根据场景动态选择）
        private Vector3[] currentMapSpawnPoints = null;

        // ============================================================================
        // BossRush 地图配置查询方法
        // ============================================================================

        /// <summary>
        /// 根据运行时场景名获取地图配置
        /// </summary>
        public static BossRushMapConfig GetMapConfigBySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            return _mapSpawnRegistry.TryGet(sceneName);
        }

        /// <summary>
        /// 根据加载用场景ID获取地图配置
        /// </summary>
        public static BossRushMapConfig GetMapConfigBySceneID(string sceneID)
        {
            if (string.IsNullOrEmpty(sceneID)) return null;

            // 优先从注册表遍历查找（按 sceneID 匹配）
            foreach (var config in _mapSpawnRegistry.All())
            {
                if (config.sceneID == sceneID)
                {
                    return config;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取当前场景的地图配置
        /// </summary>
        public static BossRushMapConfig GetCurrentMapConfig()
        {
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return GetMapConfigBySceneName(currentScene);
        }

        /// <summary>
        /// 获取所有地图配置
        /// </summary>
        public static BossRushMapConfig[] GetAllMapConfigs()
        {
            return _mapSpawnRegistry.All().ToArray();
        }

        /// <summary>
        /// 检查指定场景是否是有效的 BossRush 竞技场场景
        /// </summary>
        public bool IsValidBossRushArenaScene(string sceneName)
        {
            return GetMapConfigBySceneName(sceneName) != null;
        }

        /// <summary>
        /// 检查当前场景是否是有效的 BossRush 竞技场场景
        /// </summary>
        public bool IsCurrentSceneValidBossRushArena()
        {
            return GetCurrentMapConfig() != null;
        }

        /// <summary>
        /// 获取指定场景的刷新点
        /// </summary>
        public static Vector3[] GetSpawnPointsForScene(string sceneName)
        {
            BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
            return mapConfig != null ? mapConfig.spawnPoints : null;
        }

        /// <summary>
        /// 公共 NPC 共享的刷新/漫步点池。
        /// Mode E 和普通模式优先复用快递员普通模式点位；其他 BossRush 相关模式使用地图 Boss 刷新点池。
        /// </summary>
        public static Vector3[] GetSharedCommonNPCSpawnPointsForScene(string sceneName)
        {
            ModBehaviour mod = Instance;
            if (mod != null && mod.ShouldUseBossRushCommonNPCSpawnPoints(sceneName))
            {
                return GetSpawnPointsForScene(sceneName);
            }

            Vector3[] normalModePoints = NPCSpawnConfig.GetCourierNormalModeSpawnPoints(sceneName);
            if (normalModePoints != null && normalModePoints.Length > 0)
            {
                return normalModePoints;
            }

            return GetSpawnPointsForScene(sceneName);
        }

        /// <summary>
        /// 当前是否应让公共 NPC 使用 BossRush 地图刷怪点池。
        /// </summary>
        public bool ShouldUseBossRushCommonNPCSpawnPoints(string sceneName = null)
        {
            if (UsesArenaSupportNpcPlacement())
            {
                return false;
            }

            if (!IsActive && !IsModeDActive && !IsBossRushArenaActive)
            {
                return false;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }

            return IsValidBossRushArenaScene(sceneName);
        }

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
        {
            // 优先使用动态设置的刷新点
            if (currentMapSpawnPoints != null && currentMapSpawnPoints.Length > 0)
            {
                return currentMapSpawnPoints;
            }

            BossRushMapConfig mapConfig = GetCurrentMapConfig();
            return mapConfig != null ? mapConfig.spawnPoints : null;
        }

        /// <summary>
        /// 获取当前场景的默认传送位置（用于玩家传送、路牌位置等）
        /// 优先使用 customSpawnPos，其次使用 defaultSignPos，最后使用 DEMO 竞技场默认位置
        /// </summary>
        public static Vector3 GetCurrentSceneDefaultPosition()
        {
            BossRushMapConfig mapConfig = GetCurrentMapConfig();
            if (mapConfig != null)
            {
                // 优先使用自定义传送位置
                if (mapConfig.customSpawnPos.HasValue)
                {
                    return mapConfig.customSpawnPos.Value;
                }
                // 其次使用默认路牌位置
                if (mapConfig.defaultSignPos.HasValue)
                {
                    return mapConfig.defaultSignPos.Value;
                }
            }
            // 兜底：DEMO 竞技场默认位置
            return new Vector3(235.48f, -7.99f, 202.41f);
        }

        /// <summary>
        /// 获取指定场景的默认传送位置
        /// </summary>
        public static Vector3 GetDefaultPositionForScene(string sceneName)
        {
            BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
            if (mapConfig != null)
            {
                if (mapConfig.customSpawnPos.HasValue)
                {
                    return mapConfig.customSpawnPos.Value;
                }
                if (mapConfig.defaultSignPos.HasValue)
                {
                    return mapConfig.defaultSignPos.Value;
                }
            }
            // 兜底：DEMO 竞技场默认位置
            return new Vector3(235.48f, -7.99f, 202.41f);
        }

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
        {
            // [DEBUG] 记录传入参数
            DevLog("[BossRush] ConfigureBossRushMode 调用: 传入 bossesPerWave=" + bossesPerWave + ", useInfiniteHell=" + useInfiniteHell + ", 当前 this.bossesPerWave=" + this.bossesPerWave);

            if (bossesPerWave < 1)
            {
                bossesPerWave = 1;
            }

            infiniteHellMode = useInfiniteHell;

            // 无间炼狱模式下优先使用配置文件中的每波 Boss 数
            if (infiniteHellMode && config != null && config.infiniteHellBossesPerWave > 0)
            {
                this.bossesPerWave = config.infiniteHellBossesPerWave;
                DevLog("[BossRush] ConfigureBossRushMode: 无间炼狱模式，使用配置值 this.bossesPerWave=" + this.bossesPerWave);
            }
            else
            {
                this.bossesPerWave = bossesPerWave;
                DevLog("[BossRush] ConfigureBossRushMode: 普通模式，设置 this.bossesPerWave=" + this.bossesPerWave);
            }

            // 重置无间炼狱进度状态
            if (infiniteHellMode)
            {
                infiniteHellWaveIndex = 0;
                infiniteHellCashPool = 0L;
                infiniteHellMilestoneRewardTier = 0;

                try
                {
                    if (bossRushSignInteract != null)
                    {
                        bossRushSignInteract.AddAmmoRefillOption();
                    }
                }
                catch {}
            }

            try
            {
                DevLog("[BossRush] 已设置每波Boss数量: " + this.bossesPerWave + (infiniteHellMode ? " (无间炼狱)" : string.Empty));
            }
            catch {}
        }

        private void EnsureAmmoShop()
        {
            EnsureAmmoShop_Utilities();
        }

        public void ShowAmmoShop()
        {
            try
            {
                // 每次打开加油站时重置 ID 105 购买计数
                item105PurchaseCount = 0;

                EnsureAmmoShop();
                if (ammoShop != null)
                {
                    ammoShop.ShowUI();
                }
            }
            catch {}
        }

        // Boss管理
        private MonoBehaviour currentBoss
        {
            get { return wavesArenaRuntime.CurrentBoss; }
            set { wavesArenaRuntime.CurrentBoss = value; }
        }
        internal MonoBehaviour CurrentBossForWavesArena { get { return currentBoss; } }
        private MonoBehaviour playerCharacter;  // CharacterMainControl
        private int currentEnemyIndex
        {
            get { return wavesArenaRuntime.CurrentEnemyIndex; }
            set { wavesArenaRuntime.CurrentEnemyIndex = value; }
        }
        // 记录由 BossRush 自己生成的“大兴兴”Boss，用于区分原版 DEMO 地图刷出的同名 Boss
        private HashSet<CharacterMainControl> bossRushOwnedDaXingXing { get { return wavesArenaRuntime.OwnedDaXingXing; } }
        // 状态
        public bool IsActive { get; private set; }
        private void SetBossRushRuntimeActive(bool active)
        {
            IsActive = active;
            ClearEnemyRecoveryMonitorState();

            // [Bug修复] BossRush开始时确保订阅龙息Buff处理器
            // 无论玩家手上拿什么武器，龙裔遗族Boss的龙息都应该能触发龙焰灼烧
            if (active)
            {
                DragonBreathBuffHandler.Subscribe();
            }

            // 模式结束时清理变异词条和现金磁铁飞行状态
            if (!active)
            {
                MutatorManager.RemoveAll();
                MutatorUI.HideAll();
                ClearCashMagnetState();
            }
        }

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

        private static int _staticCanRunFrame = -1;
        private static bool _staticCanRunResult;

        internal static bool CanRunGameplayRuntimeCached()
        {
            int frame = Time.frameCount;
            if (frame != _staticCanRunFrame)
            {
                _staticCanRunFrame = frame;
                _staticCanRunResult = SceneRuntimeGate.CanRunGameplayRuntimeNow(
                    SceneManager.GetActiveScene().name);
            }
            return _staticCanRunResult;
        }

        internal static bool ShouldRunGameplaySceneRuntimeHooks(string sceneName)
        {
            return CanRunGameplayRuntimeNow(sceneName);
        }

        internal bool IsBaseHubBoatInteractable(InteractableBase interactable)
        {
            if (interactable == null || interactable.gameObject == null)
            {
                return false;
            }

            if (interactable is BossRushInteractable)
            {
                return false;
            }

            string sceneName = string.Empty;
            try { sceneName = interactable.gameObject.scene.name; } catch { }
            if (!IsBaseHubSceneName(sceneName))
            {
                return false;
            }

            string goName = interactable.gameObject.name ?? string.Empty;
            bool isMainInteract = goName == "Interact" || interactable.interactableGroup;
            bool isSubInteract = goName.Contains("_");
            if (!isMainInteract || isSubInteract)
            {
                return false;
            }

            string path = GetGameObjectPath(interactable.gameObject);
            return path.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal bool TryInjectBaseHubBoatInteractable(InteractableBase interactable)
        {
            if (!IsBaseHubBoatInteractable(interactable))
            {
                return false;
            }

            return InjectIntoInteractableBaseGroup(interactable);
        }

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

        // 保留的婚姻系统测试面板（仅 DevMode）
        private bool marriageTestUIVisible = false;
        private Rect marriageTestWindowRect = new Rect(430f, 40f, 560f, 760f);
        private Vector2 marriageTestLogScroll = Vector2.zero;
        private string marriageTestLog = "";

        private StockShop ammoShop;

        // 手动维护的掉落黑名单（不希望进入 Boss 掉落 / 通关奖励池的物品 ID）

        // 无间炼狱模式状态

        private static bool dynamicItemsInitialized { get { return IntegrationRuntimeModule.DynamicItemsInitialized; } set { IntegrationRuntimeModule.DynamicItemsInitialized = value; } }
        private static int bossRushTicketTypeId { get { return IntegrationRuntimeModule.BossRushTicketTypeId; } set { IntegrationRuntimeModule.BossRushTicketTypeId = value; } }

        // BossRush 进入 DEMO 挑战场景的来源标记
        private static bool bossRushArenaPlanned = false;  // 通过 BossRush 启动的 DEMO 挑战加载已发起但尚未完成
        private static bool bossRushArenaActive = false;   // 当前 DEMO 挑战场景是否处于 BossRush 控制之下

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
        {
            try
            {
                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    DevLog("[BossRush] ReturnToBossRushStart: 无法找到玩家角色");
                    return;
                }

                Vector3 targetPos = demoChallengeStartPosition;
                if (targetPos == Vector3.zero)
                {
                    targetPos = main.transform.position;
                }

                try
                {
                    main.SetPosition(targetPos);
                    DevLog("[BossRush] ReturnToBossRushStart: 使用 SetPosition 将玩家传送回 BossRush 起始位置 " + targetPos);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] ReturnToBossRushStart: SetPosition 出错: " + e.Message + "，改用 transform.position");
                    main.transform.position = targetPos;
                }

                ShowMessage(L10n.T("已返回出生点", "Returned to spawn point"));
            }
            catch {}
        }
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

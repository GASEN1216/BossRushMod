using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
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
        private MonoBehaviour currentBoss;  // CharacterMainControl
        private MonoBehaviour playerCharacter;  // CharacterMainControl
        private static SpawnEgg cachedSpawnEggBehavior = null;
        private static CharacterRandomPreset eggSpawnPreset = null; // 记录下蛋所用的角色预设，清理敌人时保留这类鸭鸭
        private int currentEnemyIndex = 0;
        // 记录由 BossRush 自己生成的“大兴兴”Boss，用于区分原版 DEMO 地图刷出的同名 Boss
        private readonly HashSet<CharacterMainControl> bossRushOwnedDaXingXing = new HashSet<CharacterMainControl>();
        // 状态
        public bool IsActive { get; private set; }
        // Mode F 状态
        private bool modeFActive = false;
        private ModeFState modeFState = new ModeFState();
        public bool IsModeFActive { get { return modeFActive; } }
        public bool IsModeFPreparationPhase
        {
            get
            {
                return modeFActive &&
                       modeFState != null &&
                       modeFState.CurrentPhase == ModeFPhase.Preparation;
            }
        }

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

        // Base 集散地与下水道区域会在不同 active scene 间切换，这几个场景都视为同一个入口环境
        private const string BaseRootSceneName = "Base";
        private const string BaseSceneName = "Base_SceneV2";
        private const string BaseSceneSubName = "Base_SceneV2_Sub_01";
        private const string BaseSewerSceneName = "Level_HiddenWarehouse_CellarUnderGround";
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
        // 本地开发调试时手动改为 true，正式发布前保持 false。
        private const bool HardcodedDevModeEnabled = false;

        internal static bool DevModeEnabled
        {
            get
            {
                return HardcodedDevModeEnabled;
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

        private static bool dynamicItemsInitialized = false;
        private static int bossRushTicketTypeId = -1;

        // BossRush 进入 DEMO 挑战场景的来源标记
        private static bool bossRushArenaPlanned = false;  // 通过 BossRush 启动的 DEMO 挑战加载已发起但尚未完成
        private static bool bossRushArenaActive = false;   // 当前 DEMO 挑战场景是否处于 BossRush 控制之下

        /// <summary>
        /// 竞技场是否激活（通关后仍为true，直到离开场景）
        /// 用于子弹商店等功能在通关后仍可使用
        /// </summary>
        public bool IsBossRushArenaActive => bossRushArenaActive;

        private Vector3 demoChallengeStartPosition = Vector3.zero;

        // 单波生成模式
        private bool waitingForNextWave = false;
        private float waveCountdown = 0f;
        private int lastWaveCountdownSeconds = -1;
        private int totalEnemies = 0;
        private int defeatedEnemies = 0;
        private string nextWaveBossName = null;
        // 每波生成的Boss数量和当前波次的Boss列表
        private int bossesPerWave = 1;
        private int bossesInCurrentWaveTotal = 0;
        private int bossesInCurrentWaveRemaining = 0;
        private readonly List<MonoBehaviour> currentWaveBosses = new List<MonoBehaviour>();
        // 变异词条：单Boss模式回血用的临时列表（避免每帧分配）
        private readonly List<MonoBehaviour> _singleBossRegenList = new List<MonoBehaviour>(1);
        // 波次完整性自检计时器
        private float waveIntegrityCheckTimer = 0f;
        private const float WaveIntegrityCheckInterval = 10f;
        // Mode E 独立自检计时器（Mode E 不激活 IsActive，需要单独计时）
        private float modeEIntegrityTimer = 0f;
        // 大兴兴清理定时器（只在 BossRush 进行期间启用）
        private float daXingXingCleanTimer = 0f;
        private const float DaXingXingCleanInterval = 0.5f;

        // [性能优化] 角色缓存列表，避免每次清理时都调用 FindObjectsOfType
        private static List<CharacterMainControl> _cachedCharacters = new List<CharacterMainControl>();
        private static bool _characterCacheNeedsRefresh = true;
        // [性能优化] 缓存定时刷新计时器，用于捕获动态生成的敌人
        private static float _characterCacheRefreshTimer = 0f;
        private const float CharacterCacheRefreshInterval = 10f; // 每 10 秒强制刷新一次缓存（从 5f 优化为 10f）

        // [性能优化] 复用的销毁列表，避免每次清理时分配新的 List
        private static readonly List<GameObject> _reusableDestroyList = new List<GameObject>(32);

        // [性能优化] 缓存 CharacterSpawnerRoot.created 字段的反射引用
        private static System.Reflection.FieldInfo _cachedCreatedField = null;
        private static bool _createdFieldCached = false;

        // Boss 掉落随机化相关

        // 是否已禁用spawner
        private bool spawnersDisabled = false;

        // [性能优化] 竞技场范围限制 - 以路牌为圆心的清理/禁用范围
        private const float ARENA_RADIUS = 500f; // 竞技场半径（米）
        private static Vector3 _arenaCenter = Vector3.zero; // 竞技场中心位置（路牌位置）
        private static bool _arenaCenterSet = false; // 是否已设置竞技场中心

        /// <summary>
        /// 根据地图配置设置竞技场中心位置
        /// 在禁用 spawner 和清理敌人之前调用，确保范围限制生效
        /// </summary>
        private void SetArenaCenterFromMapConfig(string sceneName)
        {
            try
            {
                BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
                if (mapConfig != null)
                {
                    // 优先使用默认路牌位置，其次使用自定义传送位置
                    if (mapConfig.defaultSignPos.HasValue)
                    {
                        _arenaCenter = mapConfig.defaultSignPos.Value;
                    }
                    else if (mapConfig.customSpawnPos.HasValue)
                    {
                        _arenaCenter = mapConfig.customSpawnPos.Value;
                    }
                    else if (mapConfig.spawnPoints != null && mapConfig.spawnPoints.Length > 0)
                    {
                        // 兜底：使用刷新点的中心位置
                        Vector3 sum = Vector3.zero;
                        for (int i = 0; i < mapConfig.spawnPoints.Length; i++)
                        {
                            sum += mapConfig.spawnPoints[i];
                        }
                        _arenaCenter = sum / mapConfig.spawnPoints.Length;
                    }
                    _arenaCenterSet = true;
                    DevLog("[BossRush] 已设置竞技场中心: " + _arenaCenter + " (场景=" + sceneName + ", 半径=" + ARENA_RADIUS + "m)");
                }
                else
                {
                    DevLog("[BossRush] 未找到场景 " + sceneName + " 的地图配置，范围限制未启用");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] SetArenaCenterFromMapConfig 出错: " + e.Message);
            }
        }

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

            // Boss 池配置窗口现在使用 Unity UI Canvas 实现，不再需要 OnGUI

            // 绘制变异词条 UI
            MutatorUI.DrawGUI();

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

            // 龙套装冲刺检测
            TickEquipmentAbilityRuntime();

            // 无间炼狱现金磁铁吸附更新
            TickGameplaySupportRuntime();

            if (TickModeRuntimeGroup(Time.deltaTime, Time.unscaledDeltaTime))
            {
                return;
            }

            // 变异词条：Boss 回血 Tick
            if (MutatorManager.BossRegenEnabled)
            {
                if (IsActive)
                {
                    if (bossesPerWave > 1)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, currentWaveBosses);
                    }
                    else if (currentBoss != null)
                    {
                        // 复用静态临时列表避免每帧分配
                        _singleBossRegenList.Clear();
                        _singleBossRegenList.Add(currentBoss);
                        MutatorManager.TickBossRegen(Time.deltaTime, _singleBossRegenList);
                    }
                }
                if (modeDActive && modeDCurrentWaveEnemies.Count > 0)
                {
                    // Mode D：把当前波次所有存活敌人都喂给 BossRegen
                    // （Mode D 的"敌人"逻辑上都是 Boss 池里的角色，回血一致处理）
                    _singleBossRegenList.Clear();
                    for (int i = 0; i < modeDCurrentWaveEnemies.Count; i++)
                    {
                        CharacterMainControl boss = modeDCurrentWaveEnemies[i];
                        if (boss != null) _singleBossRegenList.Add(boss);
                    }
                    if (_singleBossRegenList.Count > 0)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, _singleBossRegenList);
                    }
                }
                else if (modeEActive && ModeEAliveEnemies != null && ModeEAliveEnemies.Count > 0)
                {
                    // Mode E：所有阵营的 Boss 都回血（设计上对称，所有阵营吃同一条规则）
                    _singleBossRegenList.Clear();
                    for (int i = 0; i < ModeEAliveEnemies.Count; i++)
                    {
                        CharacterMainControl boss = ModeEAliveEnemies[i];
                        if (boss != null) _singleBossRegenList.Add(boss);
                    }
                    if (_singleBossRegenList.Count > 0)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, _singleBossRegenList);
                    }
                }
                else if (modeFActive && modeFActiveBossSet.Count > 0)
                {
                    // Mode F：HashSet 转列表喂入。Mode F 已经在用 BleedRateMultiplier，
                    // 这里再补 BossRegen 让两个环境规则词条都能在血猎模式生效
                    _singleBossRegenList.Clear();
                    foreach (CharacterMainControl boss in modeFActiveBossSet)
                    {
                        if (boss != null) _singleBossRegenList.Add(boss);
                    }
                    if (_singleBossRegenList.Count > 0)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, _singleBossRegenList);
                    }
                }
            }

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
            CleanupDebugToolsOnDestroy();

            CleanupPlayerLifecycleRuntimeEvents();

            // 清理成就追踪事件
            CleanupAchievementRuntime();
            SafeRuntime.Run("BossRushAchievementManager.ResetStaticCaches", () => BossRushAchievementManager.ResetStaticCaches());
            SafeRuntime.Run("AchievementIconLoader.ResetStaticCaches", () => AchievementIconLoader.ResetStaticCaches());

            // 取消订阅好感度系统事件并保存数据
            CleanupAlwaysOnRuntimeOnDestroy();

            CleanupIntegrationRuntimeOnDestroy();
            CleanupModeRuntimeOnDestroy();
            runtimeModuleHost.OnDestroy();
            HarmonyPatchGroupRegistrar.Clear();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
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

        private static InteractableLootbox GetLootBoxTemplateWithLoader()
        {
            if (_cachedLootBoxTemplateWithLoader != null)
            {
                return _cachedLootBoxTemplateWithLoader;
            }

            try
            {
                var all = Resources.FindObjectsOfTypeAll<InteractableLootbox>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var box = all[i];
                        if (box == null)
                        {
                            continue;
                        }

                        var loader = box.GetComponent<Duckov.Utilities.LootBoxLoader>();
                        if (loader != null)
                        {
                            _cachedLootBoxTemplateWithLoader = box;
                            DevLog("[BossRush] 发现带 LootBoxLoader 的 Lootbox 模板: " + box.name);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 查找 LootBoxLoader 模板失败: " + ex.Message);
            }

            return _cachedLootBoxTemplateWithLoader;
        }

        private static InteractableLootbox GetDifficultyRewardLootBoxTemplate()
        {
            if (_cachedDifficultyRewardLootBoxTemplate != null)
            {
                try
                {
                    DevLog("[BossRush] 使用缓存的通关奖励 Lootbox 模板: " + _cachedDifficultyRewardLootBoxTemplate.name);
                }
                catch {}
                return _cachedDifficultyRewardLootBoxTemplate;
            }

            try
            {
                var all = Resources.FindObjectsOfTypeAll<InteractableLootbox>();
                if (all != null)
                {
                    string mainName = null;
                    if (_cachedLootBoxTemplateWithLoader != null)
                    {
                        mainName = _cachedLootBoxTemplateWithLoader.name;
                        if (!string.IsNullOrEmpty(mainName) && mainName.EndsWith("(Clone)", StringComparison.Ordinal))
                        {
                            mainName = mainName.Substring(0, mainName.Length - "(Clone)".Length);
                        }
                    }

                    for (int i = 0; i < all.Length; i++)
                    {
                        var box = all[i];
                        if (box == null)
                        {
                            continue;
                        }

                        var loader = box.GetComponent<Duckov.Utilities.LootBoxLoader>();
                        if (loader == null)
                        {
                            continue;
                        }

                        _cachedDifficultyRewardLootBoxTemplate = box;
                        DevLog("[BossRush] 发现用于通关奖励的 Lootbox 模板: " + box.name);
                        break;
                    }

                    if (_cachedDifficultyRewardLootBoxTemplate == null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            var box = all[i];
                            if (box == null)
                            {
                                continue;
                            }

                            _cachedDifficultyRewardLootBoxTemplate = box;
                            DevLog("[BossRush] 通关奖励未找到专用 Lootbox 模板，退回使用: " + box.name);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] 查找通关奖励 Lootbox 模板失败: " + ex.Message);
            }

            // 如果没有找到不同的，就退回到 Boss 掉落使用的模板
            if (_cachedDifficultyRewardLootBoxTemplate == null)
            {
                _cachedDifficultyRewardLootBoxTemplate = GetLootBoxTemplateWithLoader();
            }

            return _cachedDifficultyRewardLootBoxTemplate;
        }

        private void ApplyLootBoxCoverSetting(InteractableLootbox lootbox, bool ignoreConfig = false)
        {
            if (lootbox == null)
            {
                return;
            }

            if (!ignoreConfig)
            {
                if (config == null || config.lootBoxBlocksBullets)
                {
                    return;
                }
            }

            try
            {
                Collider selfCol = lootbox.GetComponent<Collider>();
                if (selfCol != null && !selfCol.isTrigger)
                {
                    selfCol.isTrigger = true;
                }

                Collider[] childCols = lootbox.GetComponentsInChildren<Collider>(true);
                if (childCols != null)
                {
                    for (int i = 0; i < childCols.Length; i++)
                    {
                        Collider c = childCols[i];
                        if (c != null && !c.isTrigger)
                        {
                            c.isTrigger = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ApplyLootBoxCoverSetting 失败: " + e.Message);
            }
        }

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
        private async UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync(EnemyPresetInfo preset, Vector3 position)
        {
            try
            {
                // 检查是否是龙裔遗族Boss，使用专门的生成方法
                if (IsDragonDescendantPreset(preset))
                {
                    // 龙裔遗族使用独立生成逻辑，等待生成完成并返回结果
                    var dragonBoss = await SpawnDragonDescendant(
                        position,
                        isChildProtectionSummon: false,
                        notifyBossRushOnFailure: false);
                    MutatorManager.ApplyToEnemy(dragonBoss);
                    return dragonBoss;
                }

                // 检查是否是龙王Boss，使用专门的生成方法
                if (IsDragonKingPreset(preset))
                {
                    // 龙王使用独立生成逻辑
                    var dragonKing = await SpawnDragonKing(position, notifyBossRushOnFailure: false);
                    MutatorManager.ApplyToEnemy(dragonKing);
                    return dragonKing;
                }

                // 检查是否是幽灵女巫Boss，使用专门的生成方法
                if (IsPhantomWitchPreset(preset))
                {
                    var phantomWitch = await SpawnPhantomWitch(position, notifyBossRushOnFailure: false);
                    MutatorManager.ApplyToEnemy(phantomWitch);
                    return phantomWitch;
                }

                // 查找所有CharacterRandomPreset（从Resources中查找）
                var allPresets = ObjectCache.GetCharacterPresets();
                CharacterRandomPreset targetPreset = null;

                // 优先通过本地化键（nameKey）精确匹配预设
                foreach (var p in allPresets)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(p.nameKey) && p.nameKey == preset.name)
                    {
                        targetPreset = p;
                        DevLog("[BossRush] 找到匹配的预设: " + p.name + " (nameKey=" + p.nameKey + ")");
                        break;
                    }
                }

                // 如果没找到精确匹配，找同阵营且会显示名字的预设（强敌）
                if (targetPreset == null)
                {
                    foreach (var p in allPresets)
                    {
                        if (p == null)
                        {
                            continue;
                        }

                        if (!p.showName)
                        {
                            continue;
                        }

                        var presetTeam = GetPresetTeam(p);
                        if (presetTeam == preset.team)
                        {
                            targetPreset = p;
                            DevLog("[BossRush] 使用同阵营强敌预设: " + p.name + " (nameKey=" + p.nameKey + ")");
                            break;
                        }
                    }
                }

                if (targetPreset == null)
                {
                    DevLog("[BossRush] 未找到合适的CharacterRandomPreset");
                    return null;
                }

                // 使用CharacterRandomPreset的CreateCharacterAsync方法生成敌人
                Vector3 dir = Vector3.forward;
                // 先生成非激活状态，以便修改属性
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                var character = await targetPreset.CreateCharacterAsync(position, dir, relatedScene, null, false);

                if (character == null)
                {
                    DevLog("[BossRush] 生成敌人失败");
                    return null;
                }

                currentBoss = character;
                character.gameObject.name = "BossRush_" + preset.displayName;

                // 标记由 BossRush 自己生成的大兴兴 Boss，后续清理时保留
                try
                {
                    if (IsDaXingXingPreset(preset))
                    {
                        if (bossRushOwnedDaXingXing != null && !bossRushOwnedDaXingXing.Contains(character))
                        {
                            bossRushOwnedDaXingXing.Add(character);
                        }
                    }
                }
                catch {}

                // 无间炼狱：在角色生成后按当前波次进行生命值和伤害强化
                if (infiniteHellMode)
                {
                    ApplyInfiniteHellScaling(character, preset);
                }

                // 应用全局 Boss 数值倍率（所有模式生效）
                ApplyBossStatMultiplier(character);

                // 多Boss模式下，将本次生成的敌人加入当前波列表，便于统一统计死亡
                if (bossesPerWave > 1)
                {
                    if (currentWaveBosses != null && !currentWaveBosses.Contains(character))
                    {
                        currentWaveBosses.Add(character);
                    }
                }

                // 激活敌人
                character.gameObject.SetActive(true);

                // 应用变异词条效果到新生成的敌人
                MutatorManager.ApplyToEnemy(character);

                // 记录 Boss 生成时间和原始掉落数量（用于掉落随机化）
                try
                {
                    if (character != null)
                    {
                        int originalLootCount = 0;
                        if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                        {
                            // 记录原始库存大小作为基础掉落规模参考
                            originalLootCount = 3; // 默认基础掉落数量
                        }
                        RegisterBossRandomLootTracking(character, originalLootCount);

                        DevLog("[BossRush] 记录 Boss 生成信息并订阅掉落事件 - 时间: " + Time.time + ", 原始掉落数量: " + originalLootCount);
                    }
                }
                catch (Exception recordEx)
                {
                    DevLog("[BossRush] 记录 Boss 生成信息失败: " + recordEx.Message);
                }

                // 强制设置 AI 仇恨到玩家
                // 设置 forceTracePlayerDistance 为较大值，确保远距离生成的敌人也会追踪玩家
                var main = CharacterMainControl.Main;
                if (main != null)
                {
                    var ai = character.GetComponentInChildren<AICharacterController>();
                    if (ai != null && main.mainDamageReceiver != null)
                    {
                        // 设置强制追踪距离为 500，确保无论多远都会追踪玩家
                        ai.forceTracePlayerDistance = 500f;
                        ai.searchedEnemy = main.mainDamageReceiver;
                        ai.SetTarget(main.mainDamageReceiver.transform);
                        ai.SetNoticedToTarget(main.mainDamageReceiver);
                        ai.noticed = true;
                    }
                }

                // 延迟校验Boss位置，防止低配玩家地形加载慢导致Boss卡在地下
                StartCoroutine(DelayedBossPositionValidation(character, 0.5f));
                RegisterEnemyRecoveryAnchor(character, position);

                ShowMessage(L10n.T("第 " + (currentEnemyIndex + 1) + " 波: " + preset.displayName, "Wave " + (currentEnemyIndex + 1) + ": " + preset.displayName));
                DevLog("[BossRush] 成功生成敌人: " + preset.displayName + " at " + position);

                return character;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] SpawnEnemyAtPositionAsync 错误: " + e.Message + "\n" + e.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// 对无间炼狱模式下生成的Boss应用按波次递增的生命值与伤害强化
        /// </summary>
        private void ApplyInfiniteHellScaling(CharacterMainControl character, EnemyPresetInfo preset)
        {
            if (character == null)
            {
                return;
            }

            try
            {
                // 每波提升 2%：第 1 波为 1.00，第 2 波为 1.02，以此类推
                float scale = 1f + 0.02f * Mathf.Max(0, infiniteHellWaveIndex);

                // 1. 提升 MaxHealth
                try
                {
                    var item = character.CharacterItem;
                    if (item != null)
                    {
                        Stat hpStat = null;
                        try
                        {
                            hpStat = item.GetStat("MaxHealth");
                        }
                        catch {}

                        if (hpStat != null)
                        {
                            hpStat.BaseValue *= scale;
                        }
                    }
                }
                catch {}

                try
                {
                    if (character.Health != null)
                    {
                        // 让当前血量等于新的最大生命
                        character.Health.SetHealth(character.Health.MaxHealth);
                    }
                }
                catch {}

                // 2. 提升攻击伤害（枪械与近战）
                try
                {
                    var item = character.CharacterItem;
                    if (item != null)
                    {
                        Stat gunDmg = null;
                        Stat meleeDmg = null;

                        try { gunDmg = item.GetStat("GunDamageMultiplier"); } catch {}
                        try { meleeDmg = item.GetStat("MeleeDamageMultiplier"); } catch {}

                        if (gunDmg != null)
                        {
                            gunDmg.BaseValue *= scale;
                        }
                        if (meleeDmg != null)
                        {
                            meleeDmg.BaseValue *= scale;
                        }
                    }
                }
                catch {}
            }
            catch {}
        }

        /// <summary>
        /// 判断预设是否为“大兴兴”Boss（通过显示名或内部名称粗略匹配）
        /// </summary>
        private bool IsDaXingXingPreset(EnemyPresetInfo preset)
        {
            if (preset == null)
            {
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(preset.displayName) && (preset.displayName.Contains("大兴兴") || preset.displayName.Contains("小兴兴")))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(preset.name))
                {
                    if (preset.name.IndexOf("daxing", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch {}

            return false;
        }

        /// <summary>
        /// 在 BossRush 期间清理任何非 BossRush 召唤的“大兴兴”Boss
        /// （用于屏蔽 DEMO 挑战地图自带的固定点刷“大兴兴”逻辑）
        /// </summary>
        private void TryCleanNonBossRushDaXingXing()
        {
            try
            {
                // [性能优化] 使用缓存的角色列表，避免每次都调用 FindObjectsOfType
                // 只有 DEMO 地图才需要定时刷新缓存（因为只有 DEMO 地图有原生大兴兴 spawner）
                // 其他地图只在场景加载时刷新一次即可
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                bool isDemoMap = (currentScene == "Level_DemoChallenge_1");

                // [性能优化] 范围限制参数
                bool useRangeLimit = _arenaCenterSet;
                float radiusSq = ARENA_RADIUS * ARENA_RADIUS;

                _characterCacheRefreshTimer += DaXingXingCleanInterval;
                if (_characterCacheNeedsRefresh || (isDemoMap && _characterCacheRefreshTimer >= CharacterCacheRefreshInterval))
                {
                    RefreshCharacterCache();
                    _characterCacheRefreshTimer = 0f;
                }

                // 清理缓存中已销毁的引用
                _cachedCharacters.RemoveAll(c => c == null);

                if (_cachedCharacters.Count == 0)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                // 清理 bossRushOwnedDaXingXing 中已经被销毁的引用
                // [性能优化] 使用 RemoveWhere 替代创建临时 List，减少 GC 压力
                try
                {
                    if (bossRushOwnedDaXingXing != null && bossRushOwnedDaXingXing.Count > 0)
                    {
                        bossRushOwnedDaXingXing.RemoveWhere(owned => owned == null);
                    }
                }
                catch {}

                foreach (var c in _cachedCharacters)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    // 跳过玩家角色
                    bool isMain = false;
                    try
                    {
                        if (main != null && c == main)
                        {
                            isMain = true;
                        }
                        else
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(c);
                        }
                    }
                    catch {}

                    if (isMain)
                    {
                        continue;
                    }

                    bool isDaXing = false;
                    try
                    {
                        CharacterRandomPreset preset = c.characterPreset;
                        if (preset != null)
                        {
                            string displayName = null;
                            try
                            {
                                displayName = preset.DisplayName;
                            }
                            catch {}

                            if (!string.IsNullOrEmpty(displayName) && (displayName.Contains("大兴兴") || displayName.Contains("小兴兴")))
                            {
                                isDaXing = true;
                            }
                            else
                            {
                                string key = null;
                                try
                                {
                                    key = preset.nameKey;
                                }
                                catch {}

                                if (!string.IsNullOrEmpty(key))
                                {
                                    if (key.IndexOf("daxing", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isDaXing = true;
                                    }
                                }
                            }
                        }
                    }
                    catch {}

                    if (!isDaXing)
                    {
                        continue;
                    }

                    // BossRush 自己生成的大兴兴：保留
                    bool isOwnedByBossRush = false;
                    try
                    {
                        if (bossRushOwnedDaXingXing != null && bossRushOwnedDaXingXing.Contains(c))
                        {
                            isOwnedByBossRush = true;
                        }
                    }
                    catch {}

                    if (isOwnedByBossRush)
                    {
                        continue;
                    }

                    // [性能优化] 范围检查：只清理竞技场范围内的大兴兴
                    if (useRangeLimit && c.transform != null)
                    {
                        float distSq = (c.transform.position - _arenaCenter).sqrMagnitude;
                        if (distSq > radiusSq)
                        {
                            continue; // 超出范围，跳过
                        }
                    }

                    // 其余所有大兴兴都视为 DEMO 地图原生刷出的 BossRush 外来 Boss，直接清除
                    try
                    {
                        DevLog("[BossRush] 清理非 BossRush 源的大兴兴: goName=" + c.gameObject.name +
                               ", presetKey=" + (c.characterPreset != null ? c.characterPreset.nameKey : "<null>") +
                               ", scene=" + c.gameObject.scene.name +
                               ", pos=" + c.transform.position);
                    }
                    catch {}

                    try
                    {
                        UnityEngine.Object.Destroy(c.gameObject);
                    }
                    catch {}
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] TryCleanNonBossRushDaXingXing 出错: " + e.Message);
            }
        }

        /// <summary>
        /// [性能优化] 刷新角色缓存列表
        /// 场景加载时立即刷新，之后每隔一段时间定时刷新以捕获动态生成的敌人
        /// </summary>
        private void RefreshCharacterCache()
        {
            try
            {
                var characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                _cachedCharacters.Clear();
                if (characters != null)
                {
                    _cachedCharacters.AddRange(characters);
                }
                _characterCacheNeedsRefresh = false;
                DevLog("[BossRush] 角色缓存已刷新，共 " + _cachedCharacters.Count + " 个角色");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] RefreshCharacterCache 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 获取CharacterRandomPreset的Team（直接访问public字段）
        /// </summary>
        private int GetPresetTeam(CharacterRandomPreset preset)
        {
            if (preset == null) return 0;
            // team是public字段，直接访问
            return (int)preset.team;
        }


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


        /// <summary>
        /// 计算敌人相对于玩家的方位（8个方向）
        /// </summary>
        private string GetDirectionFromPlayer(Vector3 enemyPos, Vector3 playerPos)
        {
            Vector3 direction = enemyPos - playerPos;
            direction.y = 0; // 只考虑水平方向
            direction.Normalize();

            // 使用经过实际测量校准的地图北方（与小地图朝向一致）
            // 根据 TeleportMonitor 记录推算：在 Level_DemoChallenge_Main 中，
            // 向小地图“下方”移动对应世界坐标增量约为 (2.97, -0.88)，
            // 因此小地图“北”(上) 对应的世界方向约为 (-2.97, 0.88) 归一化
            // 从地图配置系统获取当前地图的北方向量
            BossRushMapConfig currentMapConfig = GetCurrentMapConfig();
            Vector3 mapNorth;
            if (currentMapConfig != null)
            {
                mapNorth = currentMapConfig.mapNorth;
            }
            else
            {
                // 默认使用 DEMO 竞技场的北方向量
                mapNorth = new Vector3(-0.959f, 0f, 0.284f);
            }
            mapNorth.Normalize();

            float angle = Vector3.SignedAngle(mapNorth, direction, Vector3.up);

            // 将角度转换为0-360度
            if (angle < 0) angle += 360f;

            // 8个方位划分（每个方位45度）
            if (angle >= 337.5f || angle < 22.5f)
                return "正北";
            else if (angle >= 22.5f && angle < 67.5f)
                return "东北";
            else if (angle >= 67.5f && angle < 112.5f)
                return "正东";
            else if (angle >= 112.5f && angle < 157.5f)
                return "东南";
            else if (angle >= 157.5f && angle < 202.5f)
                return "正南";
            else if (angle >= 202.5f && angle < 247.5f)
                return "西南";
            else if (angle >= 247.5f && angle < 292.5f)
                return "正西";
            else // 292.5f - 337.5f
                return "西北";
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

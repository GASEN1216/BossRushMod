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
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

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
        
        // ============================================================================
        // BossRush 地图配置系统（集中管理，方便后续添加新地图）
        // ============================================================================
        
        /// <summary>
        /// BossRush 地图配置类（统一管理所有地图相关配置）
        /// </summary>
        public class BossRushMapConfig
        {
            public string sceneName;           // 运行时场景名称（如 Level_DemoChallenge_1）
            public string sceneID;             // 加载用场景ID（如 Level_DemoChallenge_Main）
            public string displayNameCN;       // 显示名称（中文）
            public string displayNameEN;       // 显示名称（英文）
            public Vector3[] spawnPoints;      // Boss 刷新点
            public Vector3? customSpawnPos;    // 玩家自定义传送位置（null 表示使用默认）
            public Vector3? defaultSignPos;    // 默认路牌位置（null 表示使用玩家位置偏移）
            public int beaconIndex;            // 信标索引（用于地图选择UI）
            public string previewImageName;    // 预览图文件名（可选，用于地图选择UI）
            public Vector3 mapNorth;           // 地图北方向量（用于方位播报，与小地图朝向一致）
            
            /// <summary>
            /// 获取本地化的显示名称
            /// </summary>
            public string displayName { get { return L10n.T(displayNameCN, displayNameEN); } }
            
            public BossRushMapConfig(string name, string id, string displayCN, string displayEN, Vector3[] spawns, Vector3? customPos = null, Vector3? signPos = null, int beacon = 0, string preview = null, Vector3? north = null)
            {
                sceneName = name;
                sceneID = id;
                displayNameCN = displayCN;
                displayNameEN = displayEN;
                spawnPoints = spawns;
                customSpawnPos = customPos;
                defaultSignPos = signPos;
                beaconIndex = beacon;
                previewImageName = preview;
                // 默认使用 DEMO 竞技场的北方向量
                mapNorth = north.HasValue ? north.Value : new Vector3(-0.959f, 0f, 0.284f);
            }
        }
        
        // DEMO 竞技场刷新点
        private static readonly Vector3[] DemoChallengeSpawnPoints = new Vector3[]
        {
            new Vector3(232.01f, -7.98f, 182.38f),
            new Vector3(234.05f, -7.98f, 182.23f),
            new Vector3(237.19f, -7.98f, 181.60f),
            new Vector3(240.00f, -7.99f, 182.02f),
            new Vector3(242.16f, -7.67f, 183.34f),
            new Vector3(240.41f, -7.98f, 189.71f),
            new Vector3(252.37f, -7.74f, 196.98f),
            new Vector3(255.19f, -7.87f, 198.51f),
            new Vector3(259.80f, -7.95f, 200.02f),
            new Vector3(260.75f, -7.99f, 203.25f),
            new Vector3(262.71f, -7.99f, 206.51f),
            new Vector3(262.88f, -7.98f, 210.13f),
            new Vector3(262.37f, -7.89f, 214.27f),
            new Vector3(260.52f, -7.96f, 217.05f),
            new Vector3(258.01f, -7.99f, 219.57f),
            new Vector3(254.88f, -7.98f, 220.09f),
            new Vector3(252.17f, -7.98f, 220.23f),
            new Vector3(248.35f, -7.98f, 221.56f),
            new Vector3(243.97f, -7.98f, 223.85f),
            new Vector3(241.10f, -7.85f, 226.24f),
            new Vector3(237.70f, -7.99f, 224.00f),
            new Vector3(234.41f, -7.99f, 222.38f),
            new Vector3(233.38f, -7.61f, 218.86f),
        };
        
        // 零号区刷新点
        private static readonly Vector3[] GroundZeroSpawnPoints = new Vector3[]
        {
            new Vector3(415.19f, 0.01f, 260.08f),
            new Vector3(420.59f, 0.01f, 255.98f),
            new Vector3(424.44f, 0.01f, 255.94f),
            new Vector3(428.62f, 0.01f, 256.38f),
            new Vector3(431.39f, 0.01f, 256.86f),
            new Vector3(430.83f, 0.01f, 262.11f),
            new Vector3(436.16f, 0.01f, 265.04f),
            new Vector3(440.31f, 0.01f, 264.93f),
            new Vector3(445.54f, 0.01f, 264.69f),
            new Vector3(449.59f, 0.01f, 264.74f),
            new Vector3(453.48f, 0.01f, 265.38f),
            new Vector3(453.75f, 0.01f, 269.68f),
            new Vector3(453.36f, 0.01f, 273.52f),
            new Vector3(451.73f, 0.01f, 279.18f),
            new Vector3(451.99f, 0.01f, 296.23f),
            new Vector3(447.45f, 0.01f, 299.28f),
            new Vector3(448.98f, 0.01f, 304.90f),
            new Vector3(438.40f, 0.01f, 302.60f),
            new Vector3(432.79f, 0.01f, 305.06f),
            new Vector3(419.29f, 0.01f, 300.64f),
            new Vector3(415.02f, 0.01f, 299.25f),
            new Vector3(415.09f, 0.01f, 286.28f),
            new Vector3(415.34f, 0.01f, 279.90f),
            new Vector3(418.43f, 0.01f, 265.79f),
        };
        
        // 零度挑战刷新点（Level_ChallengeSnow）
        private static readonly Vector3[] ChallengeSnowSpawnPoints = new Vector3[]
        {
            new Vector3(234.35f, 0.01f, 254.65f),
            new Vector3(235.37f, 0.01f, 258.41f),
            new Vector3(232.42f, 0.01f, 263.32f),
            new Vector3(228.14f, 0.01f, 263.83f),
            new Vector3(228.06f, 0.01f, 252.52f),
            new Vector3(224.85f, 0.01f, 253.22f),
            new Vector3(221.40f, 0.01f, 253.45f),
            new Vector3(221.49f, 0.01f, 258.26f),
            new Vector3(219.95f, 0.01f, 263.19f),
            new Vector3(219.68f, 0.01f, 269.28f),
            new Vector3(219.81f, 0.01f, 273.64f),
            new Vector3(228.37f, 0.01f, 270.90f),
            new Vector3(233.89f, 0.01f, 271.26f),
            new Vector3(237.32f, 0.01f, 272.13f),
            new Vector3(237.15f, 0.01f, 276.96f),
            new Vector3(246.94f, 0.01f, 293.57f),
            new Vector3(250.41f, 0.01f, 294.05f),
            new Vector3(250.59f, 0.01f, 297.42f),
            new Vector3(249.84f, 0.01f, 300.39f),
            new Vector3(246.66f, 0.01f, 300.58f),
            new Vector3(243.86f, 0.01f, 300.33f),
            new Vector3(240.00f, 0.01f, 302.76f),
            new Vector3(237.94f, 0.01f, 300.77f),
            new Vector3(235.35f, 0.01f, 300.72f),
            new Vector3(231.55f, 0.01f, 301.00f),
            new Vector3(227.64f, 0.01f, 299.78f),
            new Vector3(224.68f, 0.01f, 297.40f),
            new Vector3(219.81f, 0.01f, 298.57f),
            new Vector3(220.25f, 0.01f, 294.99f),
        };
        
        /// <summary>
        /// 所有支持的 BossRush 地图配置（添加新地图只需在此处添加配置）
        /// </summary>
        private static readonly BossRushMapConfig[] BossRushMapConfigs = new BossRushMapConfig[]
        {
            // DEMO 竞技场（默认地图）
            new BossRushMapConfig(
                "Level_DemoChallenge_1",      // 运行时场景名
                "Level_DemoChallenge_Main",   // 加载用场景ID
                "DEMO终极挑战",                // 显示名称（中文）
                "DEMO Ultimate Challenge",    // 显示名称（英文）
                DemoChallengeSpawnPoints,     // 刷新点
                null,                         // 使用默认传送位置
                new Vector3(235.48f, -7.99f, 202.41f),  // 默认路牌位置
                0,                            // 信标索引
                "demo-preview.png",           // 预览图
                new Vector3(-0.959f, 0f, 0.284f)  // 地图北方（DEMO竞技场校准值）
            ),
            // 零度挑战（第二位）
            new BossRushMapConfig(
                "Level_ChallengeSnow",        // 运行时场景名（实际子场景名，不带_1后缀）
                "Level_ChallengeSnow_Main",   // 加载用场景ID（需要_Main后缀才能正确初始化）
                "零度挑战",                    // 显示名称（中文）
                "Zero Challenge",             // 显示名称（英文）
                ChallengeSnowSpawnPoints,     // 刷新点
                new Vector3(227.46f, 0.01f, 287.58f),  // 玩家传送位置
                new Vector3(225.32f, 0.01f, 285.64f),  // 交互点位置（与篝火相同）
                0,                            // 信标索引
                "zero-preview.png",           // 预览图
                new Vector3(0f, 0f, 1f)       // 地图北方（零度挑战：Z+ 方向）
            ),
            // 零号区（第三位）
            new BossRushMapConfig(
                "Level_GroundZero_1",         // 运行时场景名
                "Level_GroundZero_Main",      // 加载用场景ID
                "零号区",                      // 显示名称（中文）
                "Ground Zero",                // 显示名称（英文）
                GroundZeroSpawnPoints,        // 刷新点
                new Vector3(429.77f, 0.02f, 279.19f),  // 自定义传送位置
                null,                         // 路牌位置（使用玩家位置偏移）
                0,                            // 信标索引
                null,                         // 预览图（暂无）
                new Vector3(0f, 0f, 1f)       // 地图北方（零号区：Z+ 方向）
            ),
            // 后续添加新地图只需在此处添加 BossRushMapConfig
        };
        
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
            
            for (int i = 0; i < BossRushMapConfigs.Length; i++)
            {
                if (BossRushMapConfigs[i].sceneName == sceneName)
                {
                    return BossRushMapConfigs[i];
                }
            }
            return null;
        }
        
        /// <summary>
        /// 根据加载用场景ID获取地图配置
        /// </summary>
        public static BossRushMapConfig GetMapConfigBySceneID(string sceneID)
        {
            if (string.IsNullOrEmpty(sceneID)) return null;
            
            for (int i = 0; i < BossRushMapConfigs.Length; i++)
            {
                if (BossRushMapConfigs[i].sceneID == sceneID)
                {
                    return BossRushMapConfigs[i];
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
            return BossRushMapConfigs;
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
            return mapConfig != null ? mapConfig.spawnPoints : DemoChallengeSpawnPoints;
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
                infiniteHell100WaveRewardGiven = false;

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
        // 注：刷新点数组已移至 BossRushMapConfig 配置系统中统一管理（见文件顶部）
        // 以下为兼容旧代码的别名引用
        private static Vector3[] ArenaSpawnPoints { get { return DemoChallengeSpawnPoints; } }
        
        // 状态
        public bool IsActive { get; private set; }
        private bool arenaCreated = false;

        private void SetBossRushRuntimeActive(bool active)
        {
            IsActive = active;
        }
        
        // UI提示（字段定义已移动到 UIAndSigns 部分类中）
        
        // 交互相关
        private GameObject arenaStartPoint;
        private const string BossRushArenaSceneID = "Level_DemoChallenge_Main"; // 用于SceneLoader加载的场景ID
        private const string BossRushArenaSceneName = "Level_DemoChallenge_1";  // 实际运行时的场景名称

        // Base 集散地与竞技场内用于查找最近交互点的锚点坐标
        private const string BaseSceneName = "Base_SceneV2_Sub_01";
        private static readonly Vector3 BaseEntryPosition = new Vector3(101.73f, 0.02f, -59.46f);
        private static readonly Vector3 ArenaEntryPosition = new Vector3(236.76f, -4.98f, 170.26f);

        // 扫描调试日志开关（默认关闭，避免刷屏；需要时可设为 true 重新启用）
        private const bool EnableScanDebugLogs = false;

        internal const bool DevModeEnabled = false;

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
        // 波次完整性自检计时器
        private float waveIntegrityCheckTimer = 0f;
        private const float WaveIntegrityCheckInterval = 10f;
        // 大兴兴清理定时器（只在 BossRush 进行期间启用）
        private float daXingXingCleanTimer = 0f;
        private const float DaXingXingCleanInterval = 0.5f;
        
        // Boss 掉落随机化相关
        
        // 是否已禁用spawner
        private bool spawnersDisabled = false;
        private const float CLEAR_RADIUS = 9999f; // 清理半径开到最大（整张地图级别）

        void Awake()
        {
            DevLog("[BossRush] 正在加载 Boss Rush Mod...");
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // 监听玩家死亡事件
            Health.OnDead += OnPlayerDeathInBossRush;

            // 注册交互调试监听（仅在 DevModeEnabled = true 时生效）
            RegisterInteractDebugListener();
        }

        void OnGUI()
        {
            // Boss 池配置窗口现在使用 Unity UI Canvas 实现，不再需要 OnGUI
            
            // 绘制物品生成器 UI（F1 呼出）
            DrawItemSpawnerUI();
        }
        
        void Update()
        {
            // 更新UI消息
            UpdateMessage();
            
            // 检测 Boss 池窗口快捷键（Ctrl+F10）
            CheckBossPoolWindowHotkey();
            
            // 检测物品生成器快捷键（F2）
            CheckItemSpawnerHotkey();
            
            // 持续清理功能已移除，改为禁用spawner
            
            // 单波模式倒计时
            if (waitingForNextWave && waveCountdown > 0f)
            {
                // 如果 BossRush 已经结束（例如通关、玩家死亡等），则立即停止倒计时，防止继续刷“下一波将在 X 秒后开始”
                if (!IsActive && !bossRushArenaActive)
                {
                    waitingForNextWave = false;
                    waveCountdown = 0f;
                    lastWaveCountdownSeconds = -1;
                    return;
                }

                waveCountdown -= Time.deltaTime;

                float interval = GetWaveIntervalSeconds();

                // 显示倒计时（每秒更新一次）：仅大横幅
                if (interval > 5f)
                {
                    int seconds = Mathf.CeilToInt(waveCountdown);
                    if (seconds != lastWaveCountdownSeconds && seconds > 0)
                    {
                        lastWaveCountdownSeconds = seconds;

                        if (seconds % 5 == 0)
                        {
                            if (!infiniteHellMode && !string.IsNullOrEmpty(nextWaveBossName))
                            {
                                ShowBigBanner(L10n.T(
                                    "<color=red>" + nextWaveBossName + "</color> 将在 <color=yellow>" + seconds + "</color> 秒后抵达战场...",
                                    "<color=red>" + nextWaveBossName + "</color> arriving in <color=yellow>" + seconds + "</color> seconds..."
                                ));
                            }
                            else
                            {
                                ShowBigBanner(L10n.T(
                                    "下一波将在 <color=yellow>" + seconds + "</color> 秒后开始...",
                                    "Next wave in <color=yellow>" + seconds + "</color> seconds..."
                                ));
                            }
                        }
                    }
                }

                if (waveCountdown <= 0f)
                {
                    waitingForNextWave = false;
                    lastWaveCountdownSeconds = -1;
                    SpawnNextEnemy();
                }
            }
            
            // 波次完整性自检：每隔一段时间检查当前波是否出现“没有任何存活Boss但计数未清零”的异常
            if (IsActive)
            {
                waveIntegrityCheckTimer += Time.deltaTime;
                if (waveIntegrityCheckTimer >= WaveIntegrityCheckInterval)
                {
                    waveIntegrityCheckTimer = 0f;
                    // Mode D 使用独立的自检逻辑
                    if (modeDActive)
                    {
                        TryFixStuckWaveIfNoModeDEnemyAlive();
                    }
                    else
                    {
                        TryFixStuckWaveIfNoBossAlive();
                    }
                }
            }
            else
            {
                waveIntegrityCheckTimer = 0f;
            }

            // BossRush 期间，定期清理任何非 BossRush 召唤的“大兴兴”Boss
            if (IsActive || bossRushArenaActive)
            {
                daXingXingCleanTimer += Time.deltaTime;
                if (daXingXingCleanTimer >= DaXingXingCleanInterval)
                {
                    daXingXingCleanTimer = 0f;
                    TryCleanNonBossRushDaXingXing();
                }
            }
            else
            {
                daXingXingCleanTimer = 0f;
            }
            
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
            {
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null)
                    {
                        DevLog("[BossRush] F5 调试：未找到玩家 CharacterMainControl");
                    }
                    else
                    {
                        Vector3 playerPos = main.transform.position;
                        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                        
                        // 基地场景输出建筑物信息，其他场景输出最近的 GameObject
                        if (sceneName.Contains("Base_Scene"))
                        {
                            LogNearbyBuildingInfo(playerPos, 15f);
                        }
                        else
                        {
                            // 战斗场景输出最近的一个 GameObject
                            LogNearbyGameObjects(playerPos, 10f, 1);
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] F5 调试失败: " + e.Message);
                }
            }

            // 调试快捷键 F6：切换放置模式
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
            {
                try
                {
                    TogglePlacementMode();
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] F6 放置模式切换失败: " + e.Message);
                }
            }
            
            // 放置模式更新（每帧调用）
            if (DevModeEnabled && IsPlacementModeActive())
            {
                try
                {
                    UpdatePlacementMode();
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] 放置模式更新失败: " + e.Message);
                }
            }

            // 调试快捷键 F9
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                DevLog("[BossRush] F9按下，开始BossRush");
                StartBossRush();
            }

            // 调试快捷键 F8：输出场景中除玩家外所有角色信息
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null)
                    {
                        DevLog("[BossRush] F8 调试：未找到玩家 CharacterMainControl");
                    }
                    else
                    {
                        Vector3 playerPos = main.transform.position;
                        CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                        if (characters == null || characters.Length == 0)
                        {
                            DevLog("[BossRush] F8 调试：场景中未找到任何 CharacterMainControl");
                        }
                        else
                        {
                            DevLog("[BossRush] F8 调试：玩家位置=" + playerPos + "，开始列出除玩家外的所有角色");

                            foreach (var c in characters)
                            {
                                if (c == null)
                                {
                                    continue;
                                }

                                bool isMain = false;
                                try
                                {
                                    if (c == main)
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

                                Vector3 pos = c.transform.position;
                                float dist = (pos - playerPos).magnitude;

                                string presetKey = "";
                                Teams team = Teams.scav;
                                try
                                {
                                    if (c.characterPreset != null)
                                    {
                                        presetKey = c.characterPreset.nameKey;
                                        team = c.characterPreset.team;
                                    }
                                }
                                catch {}

                                float maxHealth = -1f;
                                try
                                {
                                    if (c.Health != null)
                                    {
                                        maxHealth = c.Health.MaxHealth;
                                    }
                                }
                                catch {}

                                DevLog("[BossRush] F8 角色：goName=" + c.gameObject.name +
                                       ", presetKey=" + presetKey +
                                       ", team=" + team +
                                       ", MaxHP=" + maxHealth +
                                       ", pos=" + pos +
                                       ", dist=" + dist.ToString("F1"));
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] F8 调试失败: " + e.Message);
                }
            }

            // 调试快捷键 F7：输出玩家附近最近交互点的信息，辅助定位BossRush入口
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null)
                    {
                        DevLog("[BossRush] F7 调试：未找到玩家 CharacterMainControl");
                    }
                    else
                    {
                        Vector3 playerPos = main.transform.position;
                        var allInteractables = UnityEngine.Object.FindObjectsOfType<InteractableBase>(true);
                        InteractableBase nearest = null;
                        float bestDistSq = float.MaxValue;

                        if (allInteractables != null)
                        {
                            foreach (var it in allInteractables)
                            {
                                if (it == null || it.gameObject == null) continue;

                                float distSq = (it.transform.position - playerPos).sqrMagnitude;
                                if (distSq < bestDistSq)
                                {
                                    bestDistSq = distSq;
                                    nearest = it;
                                }
                            }
                        }

                        if (nearest != null)
                        {
                            float dist = UnityEngine.Mathf.Sqrt(bestDistSq);
                            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                            string name = nearest.gameObject.name;
                            string interactName = "";
                            try { interactName = nearest.InteractName; } catch { }

                            // 组成员数量
                            int groupCount = 0;
                            try
                            {
                                var list = nearest.GetInteractableList();
                                groupCount = (list != null) ? list.Count : 0;
                            }
                            catch { }

                            DevLog("[BossRush] F7 调试：当前场景=" + sceneName +
                                      ", 玩家位置=" + playerPos +
                                      ", 最近交互点 name=" + name +
                                      ", InteractName=" + interactName +
                                      ", 位置=" + nearest.transform.position +
                                      ", 距离=" + dist +
                                      ", 组内成员数量=" + groupCount);
                        }
                        else
                        {
                            DevLog("[BossRush] F7 调试：场景中未找到任何 InteractableBase");
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] F7 调试失败: " + e.Message);
                }
            }

            if (DevModeEnabled && IsActive && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                try
                {
                    DevLog("[BossRush] F10 调试：直接清场并触发通关流程");
                    try
                    {
                        ClearEnemiesForBossRush();
                    }
                    catch {}
                    OnAllEnemiesDefeated();
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] F10 调试触发通关失败: " + e.Message);
                }
            }
            
            // 调试快捷键 F11：给予生日蛋糕并显示祝福横幅
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11))
            {
                try
                {
                    DevLog("[BossRush] F11 调试：给予生日蛋糕");
                    DebugGiveBirthdayCake();
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] F11 调试给予生日蛋糕失败: " + e.Message);
                }
            }
        }

        void LateUpdate()
        {
            // Boss 池窗口的暂停和鼠标状态控制（在所有 Update 之后执行，确保覆盖游戏的设置）
            BossPoolLateUpdate();
        }
        
        private static void InjectBossRushTicketLocalization()
        {
            InjectBossRushTicketLocalization_Integration();
        }

        private void InitializeDynamicItems()
        {
            InitializeDynamicItems_Integration();
        }

        private void InjectBossRushTicketIntoShops()
        {
            InjectBossRushTicketIntoShops_Integration();
        }
        void Start()
        {
            Start_Integration();
        }

        void OnDestroy()
        {
            // 注销交互调试监听
            UnregisterInteractDebugListener();

            OnDestroy_Integration();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            OnSceneLoaded_Integration(scene, mode);
        }

        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup(Scene scene)
        {
            return WaitForLevelInitializedThenSetup_Integration(scene);
        }

        private void TryCreateArenaDifficultyEntryPoint()
        {
            TryCreateArenaDifficultyEntryPoint_UIAndSigns();
        }
        
        /// <summary>
        /// 在指定位置创建 BossRush 难度选择路牌
        /// </summary>
        private void TryCreateArenaDifficultyEntryPoint(Vector3 position)
        {
            TryCreateArenaDifficultyEntryPoint_UIAndSigns(position);
        }

        private void TryCreateNextWaveEntryPoint()
        {
            TryCreateNextWaveEntryPoint_UIAndSigns();
        }

        private System.Collections.IEnumerator EnsureArenaEntryPointCreated()
        {
            return EnsureArenaEntryPointCreated_UIAndSigns();
        }

        private System.Collections.IEnumerator EnsureNextWaveEntryPointCreated()
        {
            const string name = "BossRush_NextWaveEntry";
            const float maxDuration = 30f;
            const float interval = 0.5f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxDuration)
            {
                attempt++;

                GameObject existing = null;
                string sceneName = string.Empty;
                Vector3 playerPos = Vector3.zero;

                try
                {
                    existing = GameObject.Find(name);
                }
                catch {}

                try
                {
                    sceneName = SceneManager.GetActiveScene().name;
                }
                catch {}

                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        playerPos = main.transform.position;
                    }
                }
                catch {}

                bool exists = (existing != null);
                DevLog("[BossRush] EnsureNextWaveEntryPoint: 第 " + attempt + " 次检查, scene=" + sceneName + ", elapsed=" + elapsed + ", exists=" + exists + ", playerPos=" + playerPos);

                if (exists)
                {
                    try
                    {
                        DevLog("[BossRush] EnsureNextWaveEntryPoint: 已确认 BossRush_NextWaveEntry 存在, 位置=" + existing.transform.position + ", 总尝试次数=" + attempt + ", elapsed=" + elapsed + " 秒");
                    }
                    catch {}
                    yield break;
                }

                TryCreateNextWaveEntryPoint();

                yield return new UnityEngine.WaitForSeconds(interval);
                elapsed += interval;
            }

            DevLog("[BossRush] EnsureNextWaveEntryPoint: 在 " + 30f + " 秒内仍未能确认 BossRush_NextWaveEntry 存在, 共尝试 " + attempt + " 次，放弃重试。");
        }

        private System.Collections.IEnumerator SetupBossRushInDemoChallenge(Scene scene)
        {
            // 重置 spawner 禁用标志（确保能重新禁用新场景的 spawner）
            spawnersDisabled = false;
            
            // 禁用场景中的spawner，阻止敌怪生成
            DisableAllSpawners();
            DevLog("[BossRush] 已禁用所有敌怪生成器");
            
            // 启动持续清理敌人协程（直到波次开始）
            StartCoroutine(ContinuousClearEnemiesUntilWaveStart());
            
            // 等待场景初始化（缩短等待时间，尽快传送玩家）
            yield return new UnityEngine.WaitForSeconds(0.5f);
            
            // 清理场景中现有的敌人
            ClearEnemiesForBossRush();
            
            // 传送玩家到指定位置（从配置系统获取当前地图的默认位置）
            Vector3 targetPosition = GetCurrentSceneDefaultPosition();
            try
            {
                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                // 如果静态 Main 还没准备好，尝试使用我们在 StartBossRush 中记录的 playerCharacter
                if (main == null && playerCharacter != null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                // 仍然为空时，兜底再扫描一次场景中的 CharacterMainControl
                if (main == null)
                {
                    try
                    {
                        var candidate = FindObjectOfType<CharacterMainControl>();
                        if (candidate != null)
                        {
                            main = candidate;
                        }
                    }
                    catch {}
                }

                if (main != null)
                {
                    // 先将玩家移到当前场景，避免出生点逻辑覆盖我们的传送
                    try
                    {
                        if (main.gameObject.scene != scene)
                        {
                            SceneManager.MoveGameObjectToScene(main.gameObject, scene);
                            DevLog("[BossRush] 已将玩家移动到场景: " + scene.name);
                        }
                    }
                    catch {}

                    Vector3 currentPos = main.transform.position;
                    if ((currentPos - targetPosition).sqrMagnitude > 0.25f)
                    {
                        // 使用 SetPosition 方法（如果存在），否则直接设置 transform.position
                        try
                        {
                            main.SetPosition(targetPosition);
                        }
                        catch
                        {
                            main.transform.position = targetPosition;
                        }
                        demoChallengeStartPosition = targetPosition;
                        DevLog("[BossRush] 已将玩家传送到指定位置: " + targetPosition);
                    }
                    else
                    {
                        demoChallengeStartPosition = targetPosition;
                    }
                }
                else
                {
                    DevLog("[BossRush] SetupBossRushInDemoChallenge: 未找到玩家角色，无法传送到指定位置");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 传送玩家到 BossRush 难度入口附近失败: " + e.Message);
            }

            try
            {
                CreateRescueTeleportBubble();
            }
            catch {}

            // 检测 Mode D 条件：玩家裸体入场
            bool shouldStartModeD = false;
            try
            {
                shouldStartModeD = IsPlayerNaked();
                if (shouldStartModeD)
                {
                    DevLog("[BossRush] 检测到玩家裸体入场，将启动 Mode D");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 检测 Mode D 条件失败: " + e.Message);
            }

            // 在 DEMO 场景中创建 BossRush 难度入口
            TryCreateArenaDifficultyEntryPoint();
            StartCoroutine(EnsureArenaEntryPointCreated());

            // 如果满足 Mode D 条件，延迟启动 Mode D（等待路牌创建完成）
            if (shouldStartModeD)
            {
                yield return new UnityEngine.WaitForSeconds(0.5f);
                TryStartModeD();
            }
            
            // 生成快递员 NPC
            SpawnCourierNPC();
        }

        private void CreateRescueTeleportBubble()
        {
            CreateRescueTeleportBubble_UIAndSigns();
        }

        private void TryCreateRescueRoadsign(Vector3 position)
        {
            TryCreateRescueRoadsign_UIAndSigns(position);
        }

        /// <summary>
        /// 创建初始传送气泡，用于在玩家被错误传送时提供返回目标点的功能
        /// 注：由于路牌上已有完整的交互选项，此气泡作为备用
        /// </summary>
        private void CreateInitialTeleportBubble()
        {
            CreateInitialTeleportBubble_UIAndSigns();
        }

        /// <summary>
        /// 备用方法：使用游戏内的 SimpleTeleport 模板克隆创建传送气泡
        /// </summary>
        private void CreateTeleportBubbleFromTemplate(Vector3 pos, string name)
        {
            CreateTeleportBubbleFromTemplate_UIAndSigns(pos, name);
        }

        private System.Collections.IEnumerator EnsurePlayerTeleportedInDemoChallenge()
        {
            // 从配置系统获取当前地图的默认位置
            Vector3 targetPosition = GetCurrentSceneDefaultPosition();
            const float maxDuration = 30f;
            const float interval = 0.5f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxDuration)
            {
                attempt++;

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
                        var candidate = FindObjectOfType<CharacterMainControl>();
                        if (candidate != null)
                        {
                            main = candidate;
                        }
                    }
                    catch {}
                }

                if (main != null)
                {
                    Vector3 currentPos = main.transform.position;
                    if ((currentPos - targetPosition).sqrMagnitude > 0.25f)
                    {
                        main.transform.position = targetPosition;
                        demoChallengeStartPosition = targetPosition;
                        DevLog("[BossRush] EnsurePlayerTeleportedInDemoChallenge: 第 " + attempt + " 次尝试，将玩家传送到指定位置: " + targetPosition + " (elapsed=" + elapsed + ")");
                    }
                    else
                    {
                        demoChallengeStartPosition = targetPosition;
                        DevLog("[BossRush] EnsurePlayerTeleportedInDemoChallenge: 第 " + attempt + " 次尝试检测到玩家已在目标位置附近 (elapsed=" + elapsed + ")");
                    }
                    yield break;
                }

                yield return new UnityEngine.WaitForSeconds(interval);
                elapsed += interval;
            }

            DevLog("[BossRush] EnsurePlayerTeleportedInDemoChallenge: 在 30 秒内未能找到玩家角色，放弃自动传送，共尝试 " + attempt + " 次");
        }

        private void ClearEnemiesForBossRush()
        {
            try
            {
                var characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (characters == null)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                int clearedCount = 0;
                foreach (var c in characters)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    // 检查是否为玩家角色
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

                    // 检查是否为蛋孵出来的友方鸭鸭（使用我们下蛋时的 spawnCharacter 预设）
                    bool isEggDuck = false;
                    try
                    {
                        if (eggSpawnPreset != null && c.characterPreset == eggSpawnPreset)
                        {
                            isEggDuck = true;
                        }
                    }
                    catch {}

                    if (isEggDuck)
                    {
                        continue;
                    }

                    // 检查是否为宠物 (PetAI组件)
                    bool isPet = false;
                    try
                    {
                        isPet = c.GetComponent<PetAI>() != null;
                    }
                    catch {}

                    if (isPet)
                    {
                        continue;
                    }

                    // 检查队伍 - 只清理敌对队伍 (scav, usec, bear, lab, wolf)
                    bool isEnemy = false;
                    try
                    {
                        if (c.characterPreset != null)
                        {
                            Teams team = c.characterPreset.team;
                            // 只清理明确的敌对队伍
                            isEnemy = (team == Teams.scav || team == Teams.usec || 
                                      team == Teams.bear || team == Teams.lab || team == Teams.wolf);
                        }
                    }
                    catch {}

                    // 如果不是敌对队伍，保留
                    if (!isEnemy)
                    {
                        continue;
                    }

                    // 清理敌对单位
                    try
                    {
                        UnityEngine.Object.Destroy(c.gameObject);
                        clearedCount++;
                    }
                    catch {}
                }

            }
            catch (Exception e)
            {
                DevLog("[BossRush] ClearEnemiesForBossRush 出错: " + e.Message);
            }
        }

        private System.Collections.IEnumerator ContinuousClearEnemiesUntilWaveStart()
        {
            while (!IsActive)
            {
                ClearEnemiesForBossRush();
                // 缩短清理间隔，减少敌人短暂刷出后才被清掉的延迟感
                yield return new UnityEngine.WaitForSeconds(0.3f);
            }
        }

        /// <summary>
        /// 尝试将 Boss Rush 注入到本地化管理器
        /// </summary>
        private void InjectLocalization()
        {
            InjectLocalization_Extra_Integration();
        }
        
        /// <summary>
        /// 查找并注入交互目标
        /// scanTimes: 扫描次数，<=0 表示无限(不建议)
        /// Bug #2 修复：成功注入后立即停止扫描
        /// </summary>
        private System.Collections.IEnumerator FindInteractionTargets(int scanTimes)
        {
            int count = 0;
            while (count < scanTimes)
            {
                bool injected = ScanAndInject();
                count++;
                
                // 早停优化：成功注入后立即结束扫描
                if (injected)
                {
                    DevLog("[BossRush] 场景扫描成功，已注入 BossRush 交互点，停止扫描。");
                    yield break;
                }
                
                yield return new UnityEngine.WaitForSeconds(1f);
            }
            DevLog("[BossRush] 场景扫描结束（未找到合适的注入点）。");
        }

        private bool ScanAndInject()
        {
            bool anyInjected = false;
            try
            {
                // 只在 Base 集散地和 BossRush 竞技场两个场景里注入交互
                var activeScene = SceneManager.GetActiveScene();
                string sceneName = activeScene.name;

                Vector3 targetPos;
                bool isBaseScene = sceneName == BaseSceneName;
                bool isArenaScene = sceneName == BossRushArenaSceneName;

                if (isBaseScene)
                {
                    targetPos = BaseEntryPosition;
                }
                else if (isArenaScene)
                {
                    return false;
                }
                else
                {
                    return false;
                }

                // 扫描所有 InteractableBase，找到船坞主交互点
                var allInteractables = FindObjectsOfType<InteractableBase>(true);
                InteractableBase boatInteract = null;
                float boatDistSq = float.MaxValue;

                if (allInteractables != null)
                {
                    foreach (var interact in allInteractables)
                    {
                        if (interact == null || interact.gameObject == null) continue;
                        if (interact is BossRushInteractable) continue;
                        
                        string goName = interact.gameObject.name;
                        string path = GetGameObjectPath(interact.gameObject);
                        float distSq = (interact.transform.position - targetPos).sqrMagnitude;
                        
                        // 查找船坞主交互点：路径包含 "Boat"，名称是 "Interact" 或 isGroup=True，排除子交互点
                        bool isBoatPath = path.Contains("Boat");
                        bool isMainInteract = goName == "Interact" || interact.interactableGroup;
                        bool isSubInteract = goName.Contains("_");
                        
                        if (isBoatPath && isMainInteract && !isSubInteract && distSq < boatDistSq)
                        {
                            boatDistSq = distSq;
                            boatInteract = interact;
                        }
                    }
                }

                // 如果找到船坞交互点，尝试注入
                if (boatInteract != null)
                {
                    if (InjectIntoInteractableBaseGroup(boatInteract))
                    {
                        anyInjected = true;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 扫描出错: " + e.Message + "\n" + e.StackTrace);
            }
            return anyInjected;
        }
        
        /// <summary>
        /// 获取 GameObject 的完整层级路径
        /// </summary>
        private string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "<null>";
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        /// <summary>
        /// 检查对象是否是我们的目标
        /// </summary>
        private bool CheckIsTarget(GameObject obj, string interactName = null)
        {
            if (obj == null) return false;
            string name = interactName ?? "";
            string objName = obj.name;
            return IsTargetName(name.ToLower()) || IsTargetName(objName.ToLower());
        }

        private bool IsTargetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // 只匹配看起来像阅读点 / 挑战入口 / 撤离点的交互名字，避免泛匹配所有 Interact_*
            return name.Contains("boat") || name.Contains("ferry") || name.Contains("ticket") || 
                   name.Contains("sewer") || name.Contains("farm") || name.Contains("town") || 
                   name.Contains("demo") || name.Contains("extract") ||
                   name.Contains("船") || name.Contains("票") || name.Contains("下水道") || 
                   name.Contains("农场") || name.Contains("挑战") ||
                   name.Contains("read") || name.Contains("阅读");
        }

        /// <summary>
        /// 获取私有的 group 列表
        /// </summary>
        private List<InteractableBase> GetGroupList(InteractableBase target)
        {
             try
             {
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(target) as List<InteractableBase>;
                }
             }
             catch {}
             return null;
        }

        /// <summary>
        /// 通过反射注入到 InteractableBase 的 group 列表
        /// </summary>
        private bool InjectIntoInteractableBaseGroup(InteractableBase target)
        {
            return InjectIntoInteractableBaseGroup_UIAndSigns(target);
        }

        /// <summary>
        /// 将 BossRush 选项注入到路牌的原生 InteractableBase 中
        /// 主交互变为"哎哟~你干嘛~"（生小鸡），同时添加难度选项到组中
        /// </summary>
        private bool InjectBossRushOptionsIntoSign(InteractableBase target)
        {
            return InjectBossRushOptionsIntoSign_UIAndSigns(target);
        }

        /// <summary>
        /// 向路牌添加下一波选项（实现位于 UIAndSigns 部分类中）
        /// </summary>

        /// <summary>
        /// 注入包含 BossRushEntryInteractable（生小鸡）和难度选项的完整交互组到路牌上
        /// </summary>
        private bool InjectIntoInteractableBaseGroupWithEntry(InteractableBase target)
        {
            return InjectIntoInteractableBaseGroupWithEntry_UIAndSigns(target);
        }

        private bool InjectIntoMultiInteraction(MultiInteraction multi)
        {
            return InjectIntoMultiInteraction_UIAndSigns(multi);
        }
        
        /// <summary>
        /// 从交互系统启动Boss Rush
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
        
        public void TrySpawnEggForPlayer()
        {
            try
            {
                try
                {
                    TryPlayNgmSound();
                }
                catch {}

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null && playerCharacter != null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 无法找到玩家角色");
                    return;
                }

                SpawnEgg behavior = null;
                try
                {
                    behavior = cachedSpawnEggBehavior;
                }
                catch {}

                if (behavior == null)
                {
                    try
                    {
                        var all = Resources.FindObjectsOfTypeAll<SpawnEgg>();
                        if (all != null && all.Length > 0)
                        {
                            behavior = all[0];
                            cachedSpawnEggBehavior = behavior;
                        }
                    }
                    catch {}
                }

                if (behavior == null || behavior.eggPrefab == null)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 未找到 SpawnEgg 配置或 eggPrefab，跳过下蛋");
                    return;
                }

                // 记录这次下蛋所用的角色预设，用于在清理敌人时保留这类鸭鸭
                try
                {
                    if (behavior.spawnCharacter != null)
                    {
                        eggSpawnPreset = behavior.spawnCharacter;
                    }
                }
                catch {}

                Egg egg = null;
                try
                {
                    egg = UnityEngine.Object.Instantiate<Egg>(behavior.eggPrefab, main.transform.position, Quaternion.identity);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 实例化蛋失败: " + e.Message);
                    return;
                }

                try
                {
                    Collider eggCol = null;
                    try { eggCol = egg.GetComponent<Collider>(); } catch {}
                    Collider playerCol = null;
                    try { playerCol = main.GetComponent<Collider>(); } catch {}
                    if (eggCol != null && playerCol != null)
                    {
                        Physics.IgnoreCollision(eggCol, playerCol, true);
                    }
                }
                catch {}

                try
                {
                    egg.Init(main.transform.position, main.CurrentAimDirection * 1f, main, behavior.spawnCharacter, behavior.eggSpawnDelay);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 初始化蛋失败: " + e.Message);
                }
            }
            catch {}
        }

        private void TryPlayNgmSound()
        {
            try
            {
                string baseDir = null;
                try
                {
                    baseDir = info.path;
                }
                catch {}

                if (string.IsNullOrEmpty(baseDir))
                {
                    return;
                }

                string filePath = null;
                try
                {
                    string candidate1 = null;
                    string candidate2 = null;
                    try
                    {
                        string assetsDir = Path.Combine(baseDir, "Assets");
                        candidate1 = Path.Combine(assetsDir, "ngm.mp3");
                    }
                    catch {}
                    try
                    {
                        candidate2 = Path.Combine(baseDir, "ngm.mp3");
                    }
                    catch {}

                    try
                    {
                        if (!string.IsNullOrEmpty(candidate1) && File.Exists(candidate1))
                        {
                            filePath = candidate1;
                        }
                        else if (!string.IsNullOrEmpty(candidate2) && File.Exists(candidate2))
                        {
                            filePath = candidate2;
                        }
                    }
                    catch {}
                }
                catch {}

                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                GameObject target = null;
                try
                {
                    CharacterMainControl main = null;
                    try
                    {
                        main = CharacterMainControl.Main;
                    }
                    catch {}
                    if (main != null)
                    {
                        target = main.gameObject;
                    }
                }
                catch {}

                try
                {
                    System.Type audioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                    if (audioManagerType == null)
                    {
                        return;
                    }
                    var method = audioManagerType.GetMethod("PostCustomSFX", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method == null)
                    {
                        return;
                    }
                    object[] args = new object[] { filePath, target, false };
                    try
                    {
                        method.Invoke(null, args);
                    }
                    catch {}
                }
                catch {}
            }
            catch {}
        }
        
        /// <summary>
        /// 禁用场景中的所有spawner
        /// </summary>
        private void DisableAllSpawners()
        {
            if (spawnersDisabled)
            {
                return;
            }

            try
            {
                int destroyedCount = 0;
                
                // 销毁 RandomCharacterSpawner
                var randomSpawners = UnityEngine.Object.FindObjectsOfType<RandomCharacterSpawner>();
                foreach (var spawner in randomSpawners)
                {
                    if (spawner != null)
                    {
                        UnityEngine.Object.Destroy(spawner.gameObject);
                        destroyedCount++;
                        DevLog("[BossRush] 已销毁 RandomCharacterSpawner: " + spawner.gameObject.name);
                    }
                }

                // 销毁 WaveCharacterSpawner
                var waveSpawners = UnityEngine.Object.FindObjectsOfType<WaveCharacterSpawner>();
                foreach (var spawner in waveSpawners)
                {
                    if (spawner != null)
                    {
                        UnityEngine.Object.Destroy(spawner.gameObject);
                        destroyedCount++;
                        DevLog("[BossRush] 已销毁 WaveCharacterSpawner: " + spawner.gameObject.name);
                    }
                }

                // 销毁 CharacterSpawnerRoot（触发器刷怪，如 DEMO 地图的大兴兴）
                var spawnerRoots = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>();
                foreach (var root in spawnerRoots)
                {
                    if (root != null)
                    {
                        UnityEngine.Object.Destroy(root.gameObject);
                        destroyedCount++;
                        DevLog("[BossRush] 已销毁 CharacterSpawnerRoot: " + root.gameObject.name);
                    }
                }

                spawnersDisabled = true;
                DevLog("[BossRush] 已销毁 " + destroyedCount + " 个spawner");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 销毁spawner时出错: " + e.Message);
            }
        }

        /// <summary>
        /// 定期自检：如果当前波计数大于0但场上已没有任何由BossRush生成的存活Boss，则强制修正并推进波次
        /// </summary>
        private void TryFixStuckWaveIfNoBossAlive()
        {
            try
            {
                if (!IsActive)
                {
                    return;
                }

                int aliveBossCount = 0;
                bool hasWaveToCheck = false;

                if (bossesPerWave > 1)
                {
                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        return;
                    }

                    if (currentWaveBosses != null && currentWaveBosses.Count > 0)
                    {
                        hasWaveToCheck = true;

                        for (int i = 0; i < currentWaveBosses.Count; i++)
                        {
                            MonoBehaviour boss = currentWaveBosses[i];
                            if (boss == null)
                            {
                                continue;
                            }

                            try
                            {
                                Health h = boss.GetComponent<Health>();
                                if (h != null && !h.IsDead)
                                {
                                    aliveBossCount++;
                                }
                            }
                            catch {}
                        }
                    }
                }
                else
                {
                    MonoBehaviour bossMb = null;
                    try
                    {
                        bossMb = currentBoss as MonoBehaviour;
                    }
                    catch {}

                    if (bossMb == null)
                    {
                        // 当前没有挂起的单Boss波次
                        return;
                    }

                    hasWaveToCheck = true;

                    try
                    {
                        Health h = bossMb.GetComponent<Health>();
                        if (h != null && !h.IsDead)
                        {
                            aliveBossCount = 1;
                        }
                    }
                    catch {}
                }

                if (!hasWaveToCheck)
                {
                    return;
                }

                if (aliveBossCount <= 0)
                {
                    try
                    {
                        DevLog("[BossRush] 自检：当前波没有任何存活 Boss，自动修正并推进下一波");
                    }
                    catch {}

                    if (bossesPerWave > 1)
                    {
                        bossesInCurrentWaveRemaining = 0;
                    }

                    ProceedAfterWaveFinished();
                }
            }
            catch {}
        }

        /// <summary>
        /// 无间炼狱单波完成：掉落现金、更新显示并准备下一波
        /// </summary>
        private void OnInfiniteHellWaveCompleted()
        {
            OnInfiniteHellWaveCompleted_LootAndRewards();
            return;
        }
        
        /// <summary>
        /// 玩家手动强制结束当前波次（作为boss刷不出来的兜底方案）
        /// 清理当前波次的所有Boss并推进到下一波或结束挑战
        /// 支持：弹指可灭、有点意思、无间炼狱、白手起家
        /// </summary>
        public void ForceEndCurrentWave()
        {
            try
            {
                // 检查是否在BossRush或ModeD模式中
                if (!IsActive && !IsModeDActive)
                {
                    DevLog("[BossRush] ForceEndCurrentWave: BossRush和ModeD都未激活，忽略");
                    return;
                }
                
                // 白手起家模式：清理ModeD敌人列表
                if (IsModeDActive)
                {
                    DevLog("[ModeD] ForceEndCurrentWave: 玩家手动结束当前波次");
                    
                    // 清理当前波次的所有敌人
                    if (modeDCurrentWaveEnemies != null)
                    {
                        for (int i = 0; i < modeDCurrentWaveEnemies.Count; i++)
                        {
                            CharacterMainControl enemy = modeDCurrentWaveEnemies[i];
                            if (enemy != null)
                            {
                                try
                                {
                                    Health h = enemy.Health;
                                    if (h != null && !h.IsDead)
                                    {
                                        UnityEngine.Object.Destroy(enemy.gameObject);
                                    }
                                }
                                catch {}
                            }
                        }
                        modeDCurrentWaveEnemies.Clear();
                    }
                    
                    // 触发ModeD波次完成
                    OnModeDWaveComplete();
                    return;
                }
                
                // BossRush模式（弹指可灭、有点意思、无间炼狱）
                DevLog("[BossRush] ForceEndCurrentWave: 玩家手动结束当前波次");
                
                // 清理当前波次的所有Boss
                if (bossesPerWave > 1)
                {
                    // 多Boss模式：清理currentWaveBosses列表中的所有Boss
                    if (currentWaveBosses != null)
                    {
                        for (int i = 0; i < currentWaveBosses.Count; i++)
                        {
                            MonoBehaviour boss = currentWaveBosses[i];
                            if (boss != null)
                            {
                                try
                                {
                                    Health h = boss.GetComponent<Health>();
                                    if (h != null && !h.IsDead)
                                    {
                                        // 直接销毁Boss对象
                                        UnityEngine.Object.Destroy(boss.gameObject);
                                    }
                                }
                                catch {}
                            }
                        }
                        currentWaveBosses.Clear();
                    }
                    bossesInCurrentWaveRemaining = 0;
                }
                else
                {
                    // 单Boss模式：清理currentBoss
                    if (currentBoss != null)
                    {
                        try
                        {
                            MonoBehaviour bossMb = currentBoss as MonoBehaviour;
                            if (bossMb != null)
                            {
                                UnityEngine.Object.Destroy(bossMb.gameObject);
                            }
                        }
                        catch {}
                        currentBoss = null;
                    }
                }
                
                // 推进到下一波或结束挑战
                ProceedAfterWaveFinished();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] ForceEndCurrentWave 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 所有敌人击败完成
        /// </summary>
        private void OnAllEnemiesDefeated()
        {
            OnAllEnemiesDefeated_LootAndRewards();
            return;
        }
        
        /// <summary>
        /// 玩家死亡保护（BossRush期间）- 参考keep_items_on_death实现
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush(Health deadHealth, DamageInfo damageInfo)
        {
            OnPlayerDeathInBossRush_LootAndRewards(deadHealth, damageInfo);
            return;
        }

        /// <summary>
        /// 在角色真正生成掉落物之前拦截玩家掉落逻辑
        /// （事件来源：CharacterMainControl.BeforeCharacterSpawnLootOnDead）
        /// </summary>
        private void OnMainCharacterBeforeSpawnLoot(DamageInfo dmgInfo)
        {
            OnMainCharacterBeforeSpawnLoot_LootAndRewards(dmgInfo);
            return;
        }

        /// <summary>
        /// 在Boss真正生成掉落物之前拦截并随机化掉落
        /// （事件来源：CharacterMainControl.BeforeCharacterSpawnLootOnDead）
        /// </summary>
        private void OnBossBeforeSpawnLoot(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            OnBossBeforeSpawnLoot_LootAndRewards(bossMain, dmgInfo);
            return;
        }

        /// <summary>
        /// 随机化Boss掉落物品 - 生成专用奖励盒子，并交给 LootBoxLoader 按品质概率随机物品
        /// </summary>
        private void RandomizeBossLoot(CharacterMainControl bossMain, int totalCount, int highQualityCount, float killDuration, float highChanceBonusByHealth)
        {
            RandomizeBossLoot_LootAndRewards(bossMain, totalCount, highQualityCount, killDuration, highChanceBonusByHealth);
            return;
        }

        private void SpawnDifficultyRewardLootbox(int highQualityCount)
        {
            SpawnDifficultyRewardLootbox_LootAndRewards(highQualityCount);
            return;
        }

        private IEnumerator LogBossLootInventory(InteractableLootbox lootbox)
        {
            return LogBossLootInventory_LootAndRewards(lootbox);
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

        private IEnumerator CleanupDifficultyRewardLootboxInventory(InteractableLootbox lootbox, int highQualityCount)
        {
            return CleanupDifficultyRewardLootboxInventory_LootAndRewards(lootbox, highQualityCount);
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

        private void EnsureBossRushArenaForScene(Scene scene)
        {
            if (arenaCreated && arenaStartPoint != null && arenaStartPoint.scene == scene)
            {
                return;
            }

            try
            {
                SceneLocationsProvider provider = null;
                System.Collections.ObjectModel.ReadOnlyCollection<SceneLocationsProvider> providers = SceneLocationsProvider.ActiveProviders;
                if (providers != null)
                {
                    foreach (SceneLocationsProvider p in providers)
                    {
                        if (p != null && p.gameObject.scene == scene)
                        {
                            provider = p;
                            break;
                        }
                    }
                }

                if (provider == null)
                {
                    DevLog("[BossRush] EnsureBossRushArenaForScene: 未找到 SceneLocationsProvider, scene=" + scene.name);
                    return;
                }

                Transform arenaRoot = provider.transform.Find("BossRushArena");
                if (arenaRoot == null)
                {
                    GameObject arenaRootObj = new GameObject("BossRushArena");
                    arenaRootObj.transform.SetParent(provider.transform);
                    arenaRootObj.transform.localPosition = new Vector3(0f, 150f, 0f);
                    arenaRoot = arenaRootObj.transform;
                }

                arenaStartPoint = arenaRoot.gameObject;
                arenaCreated = true;

                Transform spawn = arenaRoot.Find("SpawnPoint");
                if (spawn == null)
                {
                    GameObject sp = new GameObject("SpawnPoint");
                    sp.transform.SetParent(arenaRoot);
                    sp.transform.localPosition = new Vector3(0f, 2f, 0f);
                }

                BuildBossRushArenaGeometry(arenaRoot);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] EnsureBossRushArenaForScene 出错: " + e.Message);
            }
        }

        private void BuildBossRushArenaGeometry(Transform arenaRoot)
        {
            if (arenaRoot == null)
            {
                return;
            }

            // 如果已经有开始按钮，认为竞技场已经搭建完毕
            if (arenaRoot.Find("BossRushStartButton") != null)
            {
                return;
            }

            try
            {
                // 创建地面平台
                GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = "BossRushPlatform";
                platform.transform.SetParent(arenaRoot);
                platform.transform.localPosition = Vector3.zero;
                platform.transform.localScale = new Vector3(100, 2, 100); // 100x100的平台
                int originalLayer = platform.layer;

                // 将平台的 Layer 设置为游戏的 groundLayerMask 中的第一个有效层，确保角色与平台的交互与官方地面一致
                try
                {
                    LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                    int maskValue = groundMask.value;
                    int groundLayer = originalLayer;
                    for (int i = 0; i < 32; i++)
                    {
                        if ((maskValue & (1 << i)) != 0)
                        {
                            groundLayer = i;
                            break;
                        }
                    }
                    platform.layer = groundLayer;
                    DevLog("[BossRush] BuildBossRushArenaGeometry: 设置平台 Layer 为 groundLayerMask 中的层: " + groundLayer);
                }
                catch {}

                // 设置材质颜色，方便视觉识别
                Renderer renderer = platform.GetComponent<Renderer>();
                MeshFilter srcMeshFilter = platform.GetComponent<MeshFilter>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                }
                if (renderer != null && srcMeshFilter != null)
                {
                    GameObject visual = new GameObject("BossRushPlatform_Visual");
                    visual.transform.SetParent(arenaRoot);
                    visual.transform.localPosition = platform.transform.localPosition;
                    visual.transform.localRotation = platform.transform.localRotation;
                    visual.transform.localScale = platform.transform.localScale;
                    MeshFilter visualMF = visual.AddComponent<MeshFilter>();
                    visualMF.sharedMesh = srcMeshFilter.sharedMesh;
                    MeshRenderer visualRenderer = visual.AddComponent<MeshRenderer>();
                    visualRenderer.sharedMaterial = renderer.material;
                    visual.layer = originalLayer;
                    renderer.enabled = false;
                }

                // 在平台四周创建可见矮墙，防止玩家掉出平台
                try
                {
                    float arenaSize = 100f; // 对应 plane 10x10 缩放后的尺寸
                    float halfSize = arenaSize * 0.5f;
                    float wallHeight = 4f;
                    float wallThickness = 2f;

                    // 计算墙体使用的 Layer（来自 wallLayerMask）
                    LayerMask wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                    int wallMaskValue = wallMask.value;
                    int wallLayer = platform.layer;
                    for (int i = 0; i < 32; i++)
                    {
                        if ((wallMaskValue & (1 << i)) != 0)
                        {
                            wallLayer = i;
                            break;
                        }
                    }

                    // 北墙（+Z）
                    GameObject northWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    northWall.name = "BossRushWall_North";
                    northWall.transform.SetParent(arenaRoot);
                    northWall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, halfSize);
                    northWall.transform.localScale = new Vector3(arenaSize, wallHeight, wallThickness);
                    northWall.layer = wallLayer;

                    // 南墙（-Z）
                    GameObject southWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    southWall.name = "BossRushWall_South";
                    southWall.transform.SetParent(arenaRoot);
                    southWall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, -halfSize);
                    southWall.transform.localScale = new Vector3(arenaSize, wallHeight, wallThickness);
                    southWall.layer = wallLayer;

                    // 东墙（+X）
                    GameObject eastWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    eastWall.name = "BossRushWall_East";
                    eastWall.transform.SetParent(arenaRoot);
                    eastWall.transform.localPosition = new Vector3(halfSize, wallHeight * 0.5f, 0f);
                    eastWall.transform.localScale = new Vector3(wallThickness, wallHeight, arenaSize);
                    eastWall.layer = wallLayer;

                    // 西墙（-X）
                    GameObject westWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    westWall.name = "BossRushWall_West";
                    westWall.transform.SetParent(arenaRoot);
                    westWall.transform.localPosition = new Vector3(-halfSize, wallHeight * 0.5f, 0f);
                    westWall.transform.localScale = new Vector3(wallThickness, wallHeight, arenaSize);
                    westWall.layer = wallLayer;

                    // 在场地中添加一些简单掩体
                    Vector3[] coverPositions = new Vector3[]
                    {
                        new Vector3(15f, 1.5f, 0f),
                        new Vector3(-15f, 1.5f, 0f),
                        new Vector3(0f, 1.5f, 15f),
                        new Vector3(0f, 1.5f, -15f)
                    };

                    for (int ci = 0; ci < coverPositions.Length; ci++)
                    {
                        GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cover.name = "BossRushCover_" + ci;
                        cover.transform.SetParent(arenaRoot);
                        cover.transform.localPosition = coverPositions[ci];
                        cover.transform.localScale = new Vector3(4f, 3f, 4f);
                        cover.layer = wallLayer;
                    }

                    // 在竞技场周围创建一圈简单的可见边界方块，模拟粒子环效果
                    try
                    {
                        GameObject fxRoot = new GameObject("BossRushBoundaryFX");
                        fxRoot.transform.SetParent(arenaRoot);
                        fxRoot.transform.localPosition = Vector3.zero;

                        float fxRadius = halfSize + 5f;
                        int fxCount = 16;
                        for (int i = 0; i < fxCount; i++)
                        {
                            float ang = (float)i / (float)fxCount * Mathf.PI * 2f;
                            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            marker.name = "BoundaryFX_" + i;
                            marker.transform.SetParent(fxRoot.transform);
                            marker.transform.localPosition = new Vector3(Mathf.Cos(ang) * fxRadius, 1.5f, Mathf.Sin(ang) * fxRadius);
                            marker.transform.localScale = new Vector3(1.5f, 3f, 1.5f);
                            marker.layer = wallLayer;

                            Renderer mr = marker.GetComponent<Renderer>();
                            if (mr != null)
                            {
                                mr.material.color = new Color(0.2f, 0.8f, 1.0f, 1f);
                            }
                        }

                        // 为竞技场中心添加一点局部光源，避免场景过暗
                        GameObject lightObj = new GameObject("BossRushLight");
                        lightObj.transform.SetParent(arenaRoot);
                        lightObj.transform.localPosition = new Vector3(0f, 15f, 0f);
                        Light pointLight = lightObj.AddComponent<Light>();
                        pointLight.type = LightType.Point;
                        pointLight.range = arenaSize + 40f;
                        pointLight.intensity = 1.5f;
                        pointLight.color = new Color(0.9f, 0.9f, 1.0f, 1f);
                        pointLight.shadows = LightShadows.None;
                    }
                    catch {}

                    DevLog("[BossRush] BuildBossRushArenaGeometry: 创建围墙、掩体和边界标记完成，使用 Layer: " + wallLayer);
                }
                catch {}

                DevLog("[BossRush] BuildBossRushArenaGeometry: 竞技场创建完成");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] BuildBossRushArenaGeometry 出错: " + e.Message);
            }
        }

        private System.Collections.IEnumerator TeleportPlayerToArenaDelayed()
        {
            return TeleportPlayerToArenaDelayed_WavesArena();
        }

        private bool TryResolveTeleportLocation(BossRushInteractable interactionSource, CharacterMainControl main, out MultiSceneLocation location)
        {
            return TryResolveTeleportLocation_WavesArena(interactionSource, main, out location);
        }
        
        /// <summary>
        /// 创建竞技场
        /// </summary>
        private void CreateArena()
        {
            CreateArena_WavesArena();
        }
        
        /// <summary>
        /// 传送玩家到竞技场区域
        /// </summary>
        private void TeleportPlayerToArena()
        {
            TeleportPlayerToArena_WavesArena();
        }

        /// <summary>
        /// 清理竞技场中的敌人和掉落物
        /// </summary>
        private void ClearArena()
        {
            // 清理现有Boss
            if (currentBoss != null)
            {
                Destroy(currentBoss.gameObject);
                currentBoss = null;
            }
            
            // TODO: 清理地面物品
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
                    var dragonBoss = await SpawnDragonDescendant(position);
                    return dragonBoss;
                }
                
                // 查找所有CharacterRandomPreset（从Resources中查找）
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
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
                
                // 记录 Boss 生成时间和原始掉落数量（用于掉落随机化）
                try
                {
                    if (character != null)
                    {
                        bossSpawnTimes[character] = Time.time + 1f;
                        
                        // 记录原始掉落物品数量
                        int originalLootCount = 0;
                        if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                        {
                            // 记录原始库存大小作为基础掉落规模参考
                            originalLootCount = 3; // 默认基础掉落数量
                        }
                        bossOriginalLootCounts[character] = originalLootCount;
                        
                        // 关键：订阅 Boss 的掉落事件（使用lambda捕获Boss引用）
                        character.BeforeCharacterSpawnLootOnDead += (dmgInfo) => OnBossBeforeSpawnLoot(character, dmgInfo);
                        
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
                    string lower = preset.name.ToLowerInvariant();
                    if (lower.Contains("daxing"))
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
                var characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (characters == null || characters.Length == 0)
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
                try
                {
                    if (bossRushOwnedDaXingXing != null && bossRushOwnedDaXingXing.Count > 0)
                    {
                        var toRemove = new List<CharacterMainControl>();
                        foreach (var owned in bossRushOwnedDaXingXing)
                        {
                            if (owned == null)
                            {
                                toRemove.Add(owned);
                            }
                        }
                        for (int i = 0; i < toRemove.Count; i++)
                        {
                            bossRushOwnedDaXingXing.Remove(toRemove[i]);
                        }
                    }
                }
                catch {}

                foreach (var c in characters)
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
                                    string lowerKey = key.ToLowerInvariant();
                                    if (lowerKey.Contains("daxing"))
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
        private void ShowBigBanner(string text)
        {
            ShowBigBanner_UIAndSigns(text);
        }
        
        /// <summary>
        /// 获取有效的生成位置（确保在NavMesh上）
        /// </summary>
        private Vector3 GetValidSpawnPosition(Vector3 playerPos)
        {
            // 尝试在玩家周围5-10米随机距离生成
            int maxAttempts = 10;
            for (int i = 0; i < maxAttempts; i++)
            {
                // 随机角度和距离
                float angle = UnityEngine.Random.Range(0f, 360f);
                float distance = UnityEngine.Random.Range(5f, 10f);
                
                // 计算目标位置（水平面）
                Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 targetPos = playerPos + direction * distance;
                
                // 使用NavMesh采样找到最近的有效位置
                NavMeshHit hit;
                if (NavMesh.SamplePosition(targetPos, out hit, 5f, NavMesh.AllAreas))
                {
                    DevLog("[BossRush] 找到有效生成位置: " + hit.position);
                    return hit.position;
                }
            }
            
            // 如果找不到，使用玩家前方5米
            DevLog("[BossRush] 未找到有效NavMesh位置，使用玩家前方");
            Vector3 fallbackPos = playerPos + Vector3.forward * 5f;
            
            // 尝试采样前方位置
            NavMeshHit fallbackHit;
            if (NavMesh.SamplePosition(fallbackPos, out fallbackHit, 10f, NavMesh.AllAreas))
            {
                return fallbackHit.position;
            }
            
            // 最后的备用方案：直接返回玩家前方（可能不在NavMesh上）
            return fallbackPos;
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

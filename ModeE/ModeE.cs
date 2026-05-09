// ============================================================================
// ModeE.cs - 划地为营模式核心逻辑
// ============================================================================
// 模块说明：
//   Mode E（划地为营）是 BossRush 的多阵营沙盒混战模式。
//   玩家裸装携带"营旗"进入竞技场，系统根据营旗类型分配阵营，
//   地图刷怪点平均分配给所有参战阵营，每个阵营在各自领地一次性生成 Boss。
//   同阵营实体互不伤害，不同阵营自动敌对交战。
//   敌人按"出生时死亡基线"计算个人层数：每层生命/伤害 +5%（各阵营独立累计）。
//
// 主要功能：
//   - 入场条件检测（营旗 + 裸装）
//   - 阵营分配（随机/指定）
//   - 阵营气泡显示
//   - 模式启动与结束
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using HarmonyLib;

namespace BossRush
{
    /// <summary>
    /// Mode E（划地为营）：多阵营沙盒混战模式
    /// <para>玩家裸装+营旗入场，分配阵营，Boss一次性生成，按个人基线层数动态缩放</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 状态变量

        /// <summary>是否处于 Mode E 模式</summary>
        private bool modeEActive = false;

        /// <summary>玩家被分配的阵营</summary>
        private Teams modeEPlayerFaction = Teams.player;

        /// <summary>Mode E 会话序号，防止上一局异步对象晚到污染新局。</summary>
        private int modeESessionSerial = 0;

        /// <summary>当前有效的 Mode E 会话令牌。</summary>
        private int modeESessionToken = 0;

        /// <summary>当前所有存活的 Mode E 敌人（跨阵营）</summary>
        private readonly List<CharacterMainControl> modeEAliveEnemies = new List<CharacterMainControl>();

        /// <summary>Mode E 存活敌人的去重集合，避免重复注册导致列表和扫描路径膨胀。</summary>
        private readonly HashSet<CharacterMainControl> modeEAliveEnemySet = new HashSet<CharacterMainControl>();

        /// <summary>Mode E 存活敌人的阵营缓存，避免清理路径回退到全阵营扫描。</summary>
        private readonly Dictionary<CharacterMainControl, Teams> modeEAliveEnemyFactionMap
            = new Dictionary<CharacterMainControl, Teams>();

        /// <summary>各阵营死亡计数（用于计算每个敌人的个人层数：当前死亡数 - 出生基线）</summary>
        private Dictionary<Teams, int> modeEFactionDeathCount = new Dictionary<Teams, int>();

        /// <summary>
        /// [P4性能优化] 按阵营维护的独立存活敌人列表，避免缩放时全量遍历 modeEAliveEnemies
        /// Key = 阵营, Value = 该阵营的存活敌人列表
        /// </summary>
        private readonly Dictionary<Teams, List<CharacterMainControl>> modeEFactionAliveMap
            = new Dictionary<Teams, List<CharacterMainControl>>();

        /// <summary>Mode E 入场预热线程，尽量把重初始化提前摊到前置等待阶段。</summary>
        private Coroutine modeEStartupWarmupCoroutine = null;

        /// <summary>当前预热对应的场景名，用于避免跨场景误复用协程状态。</summary>
        private string modeEStartupWarmupSceneName = null;
        private const float MODEE_STARTUP_VERIFICATION_TIMEOUT_SECONDS = 8f;
        private HashSet<int> modeEStartupInventorySnapshot = null;
        private int modeEStartupFlagTypeId = -1;
        private bool modeEStartupRecoveryArmed = false;
        private bool modeEStartupFirstBossSpawned = false;
        private bool modeEStartupHasPlayerPosition = false;
        private Vector3 modeEStartupPlayerPosition = Vector3.zero;

        private const float MODEE_PLAYER_NAME_CACHE_INTERVAL = 5f;
        private const float MODEE_HEALTHBAR_LOOKUP_INTERVAL = 1f;
        private const float MODEE_UI_WARNING_LOG_INTERVAL = 5f;
        private static readonly BindingFlags ModeEUiInstanceBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static MethodInfo modeERefreshCharacterIconMethod = null;
        private static MethodInfo modeESteamFriendsGetPersonaNameMethod = null;
        private static MethodInfo modeESteamManagerGetSteamDisplayMethod = null;

        private string modeECachedPlayerName = null;
        private float modeENextPlayerNameRefreshTime = 0f;
        private HealthBar modeECachedPlayerHealthBar = null;
        private float modeENextHealthBarLookupTime = 0f;
        private float modeENextUiWarningLogTime = 0f;
        private readonly Dictionary<int, string> modeEHealthBarBaseTextByBarId = new Dictionary<int, string>();
        private readonly Dictionary<int, string> modeEHealthBarDesiredTextByBarId = new Dictionary<int, string>();
        private readonly Dictionary<int, int> modeEHealthBarTargetIdsByBarId = new Dictionary<int, int>();
        private readonly Dictionary<int, int> modeEHealthBarAppliedVersionByBarId = new Dictionary<int, int>();
        private int modeEHealthBarNameVersion = 1;
        private bool? modeELastHealthBarLanguageIsChinese = null;

        #endregion

        #region Mode E 配置

        /// <summary>Mode E 可用阵营池（排除 player/middle/all）</summary>
        private static readonly Teams[] ModeEAvailableFactions = new Teams[]
        {
            Teams.scav,   // 拾荒者
            Teams.usec,   // USEC雇佣兵
            Teams.bear,   // BEAR雇佣兵
            Teams.lab,    // 实验室
            Teams.wolf    // 狼群
        };

        /// <summary>随机营旗 TypeID（引用 FactionFlagConfig 常量）</summary>
        private int modeERandomFlagTypeId = FactionFlagConfig.RANDOM_FLAG_TYPE_ID;

        /// <summary>指定阵营营旗 TypeID → Teams 映射（引用 FactionFlagConfig 常量）</summary>
        private Dictionary<int, Teams> modeEFactionFlagMap = new Dictionary<int, Teams>
        {
            { FactionFlagConfig.SCAV_FLAG_TYPE_ID, Teams.scav },     // 拾荒者营旗
            { FactionFlagConfig.USEC_FLAG_TYPE_ID, Teams.usec },     // USEC营旗
            { FactionFlagConfig.BEAR_FLAG_TYPE_ID, Teams.bear },     // BEAR营旗
            { FactionFlagConfig.LAB_FLAG_TYPE_ID,  Teams.lab },      // 实验室营旗
            { FactionFlagConfig.WOLF_FLAG_TYPE_ID, Teams.wolf },     // 狼群营旗
            { FactionFlagConfig.PLAYER_FLAG_TYPE_ID, Teams.player }  // 爷的营旗（独立阵营，敌对所有Boss）
        };

        #endregion

        #region Mode E 公共属性

        /// <summary>是否处于 Mode E 模式</summary>
        public bool IsModeEActive { get { return modeEActive; } }

        /// <summary>玩家当前所属阵营</summary>
        public Teams ModeEPlayerFaction { get { return modeEPlayerFaction; } }

        internal int CurrentModeESessionToken { get { return modeESessionToken; } }

        /// <summary>当前所有存活的 Mode E 敌人列表（只读访问，供龙王等系统查找攻击目标）</summary>
        public List<CharacterMainControl> ModeEAliveEnemies { get { return modeEAliveEnemies; } }

        #endregion

        #region Mode E UI

        private void ResetModeEUiCaches()
        {
            modeECachedPlayerName = null;
            modeENextPlayerNameRefreshTime = 0f;
            modeECachedPlayerHealthBar = null;
            modeENextHealthBarLookupTime = 0f;
            modeENextUiWarningLogTime = 0f;
            modeEHealthBarBaseTextByBarId.Clear();
            modeEHealthBarDesiredTextByBarId.Clear();
            modeEHealthBarTargetIdsByBarId.Clear();
            modeEHealthBarAppliedVersionByBarId.Clear();
            modeEHealthBarNameVersion = 1;
            modeELastHealthBarLanguageIsChinese = null;
        }

        private void MarkModeEHealthBarNamesDirty()
        {
            if (modeEHealthBarNameVersion < int.MaxValue)
            {
                modeEHealthBarNameVersion++;
            }
            else
            {
                modeEHealthBarNameVersion = 1;
                modeEHealthBarAppliedVersionByBarId.Clear();
            }
        }

        private void SyncModeEHealthBarNameLanguageState()
        {
            bool isChinese = L10n.IsChinese;
            if (!modeELastHealthBarLanguageIsChinese.HasValue ||
                modeELastHealthBarLanguageIsChinese.Value != isChinese)
            {
                modeELastHealthBarLanguageIsChinese = isChinese;
                MarkModeEHealthBarNamesDirty();
            }
        }

        private void LogModeEUiWarningLimited(string message, Exception e = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (Time.unscaledTime < modeENextUiWarningLogTime)
            {
                return;
            }

            modeENextUiWarningLogTime = Time.unscaledTime + MODEE_UI_WARNING_LOG_INTERVAL;
            DevLog("[ModeE] [WARNING] " + message + (e != null ? ": " + e.Message : string.Empty));
        }

        private static MethodInfo GetModeERefreshCharacterIconMethod()
        {
            if (modeERefreshCharacterIconMethod == null)
            {
                modeERefreshCharacterIconMethod = typeof(HealthBar).GetMethod(
                    "RefreshCharacterIcon",
                    ModeEUiInstanceBindingFlags);
            }

            return modeERefreshCharacterIconMethod;
        }

        private static MethodInfo GetModeESteamFriendsGetPersonaNameMethod()
        {
            if (modeESteamFriendsGetPersonaNameMethod != null)
            {
                return modeESteamFriendsGetPersonaNameMethod;
            }

            Type steamFriendsType = AccessTools.TypeByName("Steamworks.SteamFriends");
            if (steamFriendsType != null)
            {
                modeESteamFriendsGetPersonaNameMethod = steamFriendsType.GetMethod(
                    "GetPersonaName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            return modeESteamFriendsGetPersonaNameMethod;
        }

        private static MethodInfo GetModeESteamManagerGetSteamDisplayMethod()
        {
            if (modeESteamManagerGetSteamDisplayMethod != null)
            {
                return modeESteamManagerGetSteamDisplayMethod;
            }

            Type steamManagerType = AccessTools.TypeByName("SteamManager");
            if (steamManagerType != null)
            {
                modeESteamManagerGetSteamDisplayMethod = steamManagerType.GetMethod(
                    "GetSteamDisplay",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            return modeESteamManagerGetSteamDisplayMethod;
        }

        private string TryGetModeESteamPersonaName()
        {
            try
            {
                MethodInfo getPersonaName = GetModeESteamFriendsGetPersonaNameMethod();
                if (getPersonaName != null)
                {
                    string personaName = getPersonaName.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(personaName))
                    {
                        return personaName;
                    }
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Steam PersonaName 失败", e);
            }

            try
            {
                MethodInfo getSteamDisplay = GetModeESteamManagerGetSteamDisplayMethod();
                if (getSteamDisplay != null)
                {
                    object display = getSteamDisplay.IsStatic ? getSteamDisplay.Invoke(null, null) : null;
                    string displayName = display as string;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        return displayName;
                    }
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Steam 显示名失败", e);
            }

            return null;
        }

        internal string GetModeEPlayerName()
        {
            if (Time.unscaledTime < modeENextPlayerNameRefreshTime && !string.IsNullOrEmpty(modeECachedPlayerName))
            {
                return modeECachedPlayerName;
            }

            try
            {
                string steamName = TryGetModeESteamPersonaName();
                modeECachedPlayerName = !string.IsNullOrEmpty(steamName)
                    ? steamName
                    : L10n.T("我", "Me");
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("刷新 Mode E 玩家显示名失败，已回退默认名称", e);
                modeECachedPlayerName = L10n.T("我", "Me");
            }

            modeENextPlayerNameRefreshTime = Time.unscaledTime + MODEE_PLAYER_NAME_CACHE_INTERVAL;
            return modeECachedPlayerName;
        }

        internal string GetModeEActorDisplayName(CharacterMainControl actor, bool treatNullAsPlayer = false)
        {
            if (actor == null)
            {
                return treatNullAsPlayer ? GetModeEPlayerName() : L10n.T("未知目标", "Unknown");
            }

            try
            {
                if (actor.CharacterItem != null && !string.IsNullOrEmpty(actor.CharacterItem.DisplayName))
                {
                    return actor.CharacterItem.DisplayName;
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Mode E 角色显示名失败", e);
            }

            try
            {
                if (actor.gameObject != null && !string.IsNullOrEmpty(actor.gameObject.name))
                {
                    return actor.gameObject.name;
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Mode E 角色对象名失败", e);
            }

            return L10n.T("未知目标", "Unknown");
        }

        private void EnsureModeEPlayerNameTag()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            try
            {
                player.Health.showHealthBar = true;

                HealthBar healthBar = FindModeEHealthBar(player.Health);
                if (healthBar != null)
                {
                    ForceRefreshModeEHealthBarName(healthBar);
                    return;
                }

                player.Health.RequestHealthBar();
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("刷新玩家血条名牌失败", e);
            }
        }

        private void UpdateModeEPlayerNameTag()
        {
            if (!modeEActive)
            {
                return;
            }

            if (Time.frameCount % 120 == 0)
            {
                EnsureModeEPlayerNameTag();
            }
        }

        private void CleanupModeEPlayerNameTag()
        {
            modeECachedPlayerHealthBar = null;
            modeENextHealthBarLookupTime = 0f;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            HealthBar healthBar = FindModeEHealthBar(player.Health);
            if (healthBar == null)
            {
                return;
            }

            ForceRefreshModeEHealthBarName(healthBar);
        }

        private HealthBar FindModeEHealthBar(Health health)
        {
            if (health == null)
            {
                return null;
            }

            if (modeECachedPlayerHealthBar != null)
            {
                if (modeECachedPlayerHealthBar.target == health)
                {
                    return modeECachedPlayerHealthBar;
                }

                modeECachedPlayerHealthBar = null;
            }

            if (Time.unscaledTime < modeENextHealthBarLookupTime)
            {
                return null;
            }

            modeENextHealthBarLookupTime = Time.unscaledTime + MODEE_HEALTHBAR_LOOKUP_INTERVAL;

            HealthBar[] healthBars = UnityEngine.Object.FindObjectsOfType<HealthBar>();
            for (int i = 0; i < healthBars.Length; i++)
            {
                HealthBar healthBar = healthBars[i];
                if (healthBar != null && healthBar.target == health)
                {
                    modeECachedPlayerHealthBar = healthBar;
                    return healthBar;
                }
            }

            return null;
        }

        private void ForceRefreshModeEHealthBarName(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            try
            {
                MethodInfo refreshCharacterIcon = GetModeERefreshCharacterIconMethod();
                if (refreshCharacterIcon != null)
                {
                    refreshCharacterIcon.Invoke(healthBar, null);
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("强制刷新玩家血条名字失败", e);
            }
        }

        #endregion

        #region Mode E 核心方法

        /// <summary>
        /// Mode E 入场分段耗时统计器，仅在开发模式下输出关键阶段耗时。
        /// </summary>
        private sealed class ModeEStartupProfiler
        {
            private readonly bool enabled;
            private readonly string scope;
            private readonly float startTime;
            private float lastCheckpointTime;
            private bool completed;

            public ModeEStartupProfiler(string scope, string detail = null)
            {
                enabled = DevModeEnabled && ModeEStartupProfilingEnabled;
                if (!enabled)
                {
                    return;
                }

                this.scope = string.IsNullOrEmpty(detail) ? scope : scope + " [" + detail + "]";
                startTime = Time.realtimeSinceStartup;
                lastCheckpointTime = startTime;
                DevLog("[ModeE] [Profile] " + this.scope + " begin");
            }

            public void Mark(string stageName)
            {
                if (!enabled || completed)
                {
                    return;
                }

                float now = Time.realtimeSinceStartup;
                DevLog("[ModeE] [Profile] " + scope + " | " + stageName + ": +" + ((now - lastCheckpointTime) * 1000f).ToString("F1") + " ms");
                lastCheckpointTime = now;
            }

            public void Complete(string status = "completed")
            {
                if (!enabled || completed)
                {
                    return;
                }

                completed = true;
                float now = Time.realtimeSinceStartup;
                DevLog("[ModeE] [Profile] " + scope + " | " + status + " | total=" + ((now - startTime) * 1000f).ToString("F1") + " ms");
            }
        }

        private int BeginModeESession()
        {
            modeESessionToken = ++modeESessionSerial;
            return modeESessionToken;
        }

        private void InvalidateModeESession()
        {
            modeESessionSerial++;
            modeESessionToken = 0;
        }

        private void ResetModeESharedRuntimeState(bool clearSpawnAllocation, bool clearSpawnerCache, bool stopWarmupCoroutine)
        {
            // 尝试对称清理玩家缩放 Modifier；若玩家对象暂不可用，保留句柄供后续重试。
            RemoveModeEPlayerScalingModifiers();

            modeEPlayerFaction = Teams.player;
            modeEAliveEnemies.Clear();
            modeEAliveEnemySet.Clear();
            modeEAliveEnemyFactionMap.Clear();
            modeEFactionDeathCount.Clear();
            modeEFactionAliveMap.Clear();
            modeEEnemyScalingStates.Clear();
            modeEPlayerLastHitKillCount = 0;
            modeEEnemyDeathHandlers.Clear();
            modeEEnemyLootHandlers.Clear();
            modeEPendingScalingFactions.Clear();
            modeEScalingBatchTimer = 0f;
            modeETotalSpawnExpected = 0;
            modeESpawnResolved = 0;
            modeEDragonDescendantSpawned = false;
            modeEDragonKingSpawned = false;
            modeEWolfBossCount = 0;
            modeEWolfBossAssigned = 0;
            modeESpawnerRootRegisteredEnemies.Clear();
            modeEIntegrityTimer = 0f;

            if (clearSpawnAllocation)
            {
                modeESpawnAllocation = null;
                modeEFlattenedSpawnPoints = null;
                modeECachedSmokeVfxPrefab = null;
            }

            if (clearSpawnerCache)
            {
                modeECachedSpawnerPositions = null;
                modeECachedSpawnerSceneName = null;
            }

            if (stopWarmupCoroutine)
            {
                if (modeEStartupWarmupCoroutine != null)
                {
                    StopCoroutine(modeEStartupWarmupCoroutine);
                    modeEStartupWarmupCoroutine = null;
                }

                modeEStartupWarmupSceneName = null;
            }

            CleanupModeEVirtualSpawnerRoot();
            ClearPendingBossAggroQueue();
            ResetModeERespawnRuntimeState();
            ResetModeEFLootboxTrackerState();
        }

        internal bool IsModeESessionStillValid(int sessionToken, int relatedScene)
        {
            if (sessionToken <= 0)
            {
                return false;
            }

            return modeEActive &&
                   modeESessionToken == sessionToken &&
                   UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == relatedScene;
        }

        internal bool IsModeEOrModeFSpawnSessionStillValid(
            int modeFSessionToken,
            int modeFRelatedScene,
            int modeESessionToken,
            int modeESessionRelatedScene)
        {
            if (modeFSessionToken > 0)
            {
                return IsModeFSessionStillValid(modeFSessionToken, modeFRelatedScene);
            }

            if (modeESessionToken > 0)
            {
                return IsModeESessionStillValid(modeESessionToken, modeESessionRelatedScene);
            }

            return modeEActive || modeFActive;
        }

        /// <summary>
        /// 在进入 Mode E 前预热重初始化逻辑，尽量把首帧卡顿摊到前置等待阶段。
        /// </summary>
        private void ScheduleModeEStartupWarmup(string reason)
        {
            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (modeEStartupWarmupCoroutine != null && modeEStartupWarmupSceneName == sceneName)
                {
                    return;
                }

                if (modeEStartupWarmupCoroutine != null)
                {
                    StopCoroutine(modeEStartupWarmupCoroutine);
                    modeEStartupWarmupCoroutine = null;
                }

                modeEStartupWarmupSceneName = sceneName;
                modeEStartupWarmupCoroutine = StartCoroutine(PrepareModeEStartupCoroutine(sceneName, reason));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ScheduleModeEStartupWarmup failed: " + e.Message);
            }
        }

        private void ClearModeEStartupWarmupCoroutine(string sceneName)
        {
            if (modeEStartupWarmupSceneName == sceneName)
            {
                modeEStartupWarmupCoroutine = null;
            }
        }

        private void StopModeEStartupWarmupIfPending()
        {
            if (modeEStartupWarmupCoroutine != null)
            {
                StopCoroutine(modeEStartupWarmupCoroutine);
                modeEStartupWarmupCoroutine = null;
            }

            modeEStartupWarmupSceneName = null;
        }

        private bool TryRunModeEStartupWarmupStep(Action action, ModeEStartupProfiler profiler, string stageName, string errorContext, string sceneName)
        {
            try
            {
                action();
                profiler.Mark(stageName);
                return true;
            }
            catch (Exception e)
            {
                profiler.Complete("failed");
                DevLog("[ModeE] [ERROR] " + errorContext + " failed: " + e.Message);
                ClearModeEStartupWarmupCoroutine(sceneName);
                return false;
            }
        }

        /// <summary>
        /// 分帧预热 Mode E 入场所需的重缓存，未跑完时由正式启动流程继续兜底。
        /// </summary>
        private System.Collections.IEnumerator PrepareModeEStartupCoroutine(string sceneName, string reason)
        {
            ModeEStartupProfiler profiler = new ModeEStartupProfiler("PrepareModeEStartup", sceneName + ", " + reason);
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                InitializeModeDItemPools,
                profiler,
                "InitializeModeDItemPools",
                "PrepareModeEStartup.InitializeModeDItemPools",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                InitializeModeDEnemyPools,
                profiler,
                "InitializeModeDEnemyPools",
                "PrepareModeEStartup.InitializeModeDEnemyPools",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                BuildModeEFactionPresetCaches,
                profiler,
                "BuildModeEFactionPresetCaches",
                "PrepareModeEStartup.BuildModeEFactionPresetCaches",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                TryPrewarmModeDGlobalItemPool,
                profiler,
                "TryPrewarmModeDGlobalItemPool",
                "PrepareModeEStartup.TryPrewarmModeDGlobalItemPool",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                PreCacheMapSpawnerPositions,
                profiler,
                "PreCacheMapSpawnerPositions",
                "PrepareModeEStartup.PreCacheMapSpawnerPositions",
                sceneName))
            {
                yield break;
            }
            yield return null;

            yield return StartCoroutine(WarmModeEMerchantCachesAsync());
            profiler.Mark("WarmModeEMerchantCachesAsync");
            profiler.Complete();
            ClearModeEStartupWarmupCoroutine(sceneName);
        }

        /// <summary>
        /// 检测玩家背包中是否存在营旗物品
        /// 返回阵营和对应的 Item 引用；未找到则返回 (null, null)
        /// </summary>
        public (Teams? faction, Item flagItem) DetectFactionFlag()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return (null, null);

                Item characterItem = main.CharacterItem;
                if (characterItem == null) return (null, null);

                Inventory inventory = characterItem.Inventory;
                if (inventory == null || inventory.Content == null) return (null, null);

                // 遍历背包，匹配营旗 TypeID
                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item == null) continue;

                    int typeId = -1;
                    try { typeId = item.TypeID; } catch { continue; }

                    // 检查是否为随机营旗
                    if (modeERandomFlagTypeId > 0 && typeId == modeERandomFlagTypeId)
                    {
                        // 随机营旗：从可用阵营池中随机选择
                        Teams randomFaction = ModeEAvailableFactions[UnityEngine.Random.Range(0, ModeEAvailableFactions.Length)];
                        DevLog("[ModeE] 检测到随机营旗 (TypeID=" + typeId + ")，随机分配阵营: " + randomFaction);
                        return (randomFaction, item);
                    }

                    // 检查是否为指定阵营营旗
                    Teams assignedFaction;
                    if (modeEFactionFlagMap.TryGetValue(typeId, out assignedFaction))
                    {
                        DevLog("[ModeE] 检测到指定营旗 (TypeID=" + typeId + ")，阵营: " + assignedFaction);
                        return (assignedFaction, item);
                    }
                }

                return (null, null);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] DetectFactionFlag 失败: " + e.Message);
                return (null, null);
            }
        }

        /// <summary>
        /// 消耗（销毁）指定的营旗物品
        /// </summary>
        private bool TryConsumeModeEntryItem(Item item, string modeTag, string itemLabel)
        {
            if (item == null)
            {
                DevLog("[" + modeTag + "] [WARNING] " + itemLabel + " 为 null，跳过消耗");
                return false;
            }

            try
            {
                item.Detach();
                item.DestroyTree();
                DevLog("[" + modeTag + "] " + itemLabel + "已消耗");
                return true;
            }
            catch (Exception e)
            {
                DevLog("[" + modeTag + "] [WARNING] 消耗" + itemLabel + "失败: " + e.Message);
                return false;
            }
        }

        private bool ConsumeFactionFlag(Item flagItem)
        {
            return TryConsumeModeEntryItem(flagItem, "ModeE", "营旗");
        }

        private void ResetModeEStartupRecoveryState()
        {
            modeEStartupInventorySnapshot = null;
            modeEStartupFlagTypeId = -1;
            modeEStartupRecoveryArmed = false;
            modeEStartupFirstBossSpawned = false;
            modeEStartupHasPlayerPosition = false;
            modeEStartupPlayerPosition = Vector3.zero;
        }

        private void ArmModeEStartupRecovery(HashSet<int> startupInventorySnapshot, int flagTypeId, Vector3 playerPosition)
        {
            modeEStartupInventorySnapshot = startupInventorySnapshot != null
                ? new HashSet<int>(startupInventorySnapshot)
                : null;
            modeEStartupFlagTypeId = flagTypeId;
            modeEStartupRecoveryArmed = true;
            modeEStartupFirstBossSpawned = false;
            modeEStartupHasPlayerPosition = true;
            modeEStartupPlayerPosition = playerPosition;
        }

        private void DisarmModeEStartupRecovery(string reason)
        {
            if (!modeEStartupRecoveryArmed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(reason))
            {
                DevLog("[ModeE] 启动验证通过，结束回滚监控: " + reason);
            }

            ResetModeEStartupRecoveryState();
        }

        private void MarkModeEStartupBossSpawned()
        {
            if (modeEStartupRecoveryArmed)
            {
                modeEStartupFirstBossSpawned = true;
            }
        }

        private bool CaptureModeEStartupInventorySnapshot(out HashSet<int> snapshot)
        {
            snapshot = new HashSet<int>();

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Item characterItem = main != null ? main.CharacterItem : null;
                if (characterItem == null)
                {
                    DevLog("[ModeE] [WARNING] 无法捕获启动前物资快照：玩家或 CharacterItem 为空");
                    snapshot = null;
                    return false;
                }

                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        Item item = inventory.Content[i];
                        if (item != null)
                        {
                            snapshot.Add(item.GetInstanceID());
                        }
                    }
                }

                if (characterItem.Slots != null)
                {
                    foreach (Slot slot in characterItem.Slots)
                    {
                        if (slot != null && slot.Content != null)
                        {
                            snapshot.Add(slot.Content.GetInstanceID());
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 捕获启动前物资快照失败: " + e.Message);
                snapshot = null;
                return false;
            }
        }

        private bool TryCaptureModeEStartupPlayerPosition(out Vector3 playerPosition)
        {
            playerPosition = Vector3.zero;

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] 无法记录启动前玩家位置：玩家或 transform 为空");
                    return false;
                }

                playerPosition = player.transform.position;
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 记录启动前玩家位置失败: " + e.Message);
                playerPosition = Vector3.zero;
                return false;
            }
        }

        private bool TryRestoreModeEStartupPlayerPosition(Vector3 playerPosition)
        {
            CharacterController cc = null;
            bool controllerWasEnabled = false;

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] 恢复启动前玩家位置失败：玩家或 transform 为空");
                    return false;
                }

                cc = player.GetComponent<CharacterController>();
                if (cc != null)
                {
                    controllerWasEnabled = cc.enabled;
                    if (controllerWasEnabled)
                    {
                        cc.enabled = false;
                    }
                }

                try
                {
                    player.SetPosition(playerPosition);
                }
                catch (Exception setPositionEx)
                {
                    DevLog("[ModeE] [WARNING] 恢复玩家位置时 SetPosition 失败，改用 transform.position: " + setPositionEx.Message);
                    player.transform.position = playerPosition;
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 恢复启动前玩家位置失败: " + e.Message);
                return false;
            }
            finally
            {
                if (cc != null)
                {
                    try
                    {
                        cc.enabled = controllerWasEnabled;
                    }
                    catch (Exception e)
                    {
                        DevLog("[ModeE] [WARNING] 恢复玩家位置后还原 CharacterController 状态失败: " + e.Message);
                    }
                }
            }
        }

        private bool RollbackModeEStartupInventory(HashSet<int> startupInventorySnapshot)
        {
            if (startupInventorySnapshot == null)
            {
                DevLog("[ModeE] [WARNING] 启动物资回滚失败：快照为空");
                return false;
            }

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Item characterItem = main != null ? main.CharacterItem : null;
                if (characterItem == null)
                {
                    DevLog("[ModeE] [WARNING] 启动物资回滚失败：玩家或 CharacterItem 为空");
                    return false;
                }

                List<Item> rollbackItems = new List<Item>();
                HashSet<int> queuedItemIds = new HashSet<int>();

                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = inventory.Content.Count - 1; i >= 0; i--)
                    {
                        Item item = inventory.Content[i];
                        if (item == null)
                        {
                            continue;
                        }

                        int itemId = item.GetInstanceID();
                        if (!startupInventorySnapshot.Contains(itemId) && queuedItemIds.Add(itemId))
                        {
                            rollbackItems.Add(item);
                        }
                    }
                }

                if (characterItem.Slots != null)
                {
                    foreach (Slot slot in characterItem.Slots)
                    {
                        if (slot == null || slot.Content == null)
                        {
                            continue;
                        }

                        Item item = slot.Content;
                        int itemId = item.GetInstanceID();
                        if (!startupInventorySnapshot.Contains(itemId) && queuedItemIds.Add(itemId))
                        {
                            rollbackItems.Add(item);
                        }
                    }
                }

                bool rollbackSucceeded = true;
                int removedCount = 0;
                for (int i = 0; i < rollbackItems.Count; i++)
                {
                    Item item = rollbackItems[i];
                    if (item == null)
                    {
                        continue;
                    }

                    try
                    {
                        item.Detach();
                    }
                    catch (Exception e)
                    {
                        rollbackSucceeded = false;
                        DevLog("[ModeE] [WARNING] 回滚启动新增物品时 Detach 失败: " + e.Message);
                    }

                    try
                    {
                        item.DestroyTree();
                        removedCount++;
                    }
                    catch (Exception e)
                    {
                        rollbackSucceeded = false;
                        DevLog("[ModeE] [WARNING] 回滚启动新增物品时 DestroyTree 失败: " + e.Message);
                    }
                }

                DevLog("[ModeE] 启动失败时已回滚新增物品数量: " + removedCount);
                return rollbackSucceeded;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 启动物资回滚失败: " + e.Message);
                return false;
            }
        }

        private bool TryGetModeEStartupFlagTypeId(Item flagItem, out int typeId)
        {
            typeId = -1;
            if (flagItem == null)
            {
                DevLog("[ModeE] [WARNING] 启动前营旗引用为空，已取消启动");
                return false;
            }

            try
            {
                typeId = flagItem.TypeID;
                if (typeId > 0)
                {
                    return true;
                }

                DevLog("[ModeE] [WARNING] 启动前营旗 TypeID 非法: " + typeId);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 读取营旗 TypeID 失败，已取消启动以避免吞旗: " + e.Message);
            }

            return false;
        }

        private bool TryRefundModeEStartupFlag(int typeId)
        {
            if (typeId <= 0)
            {
                return false;
            }

            bool refunded = TryGiveItemToPlayerOrDrop(typeId, L10n.T("营旗", "Faction Flag"), false);
            if (!refunded)
            {
                DevLog("[ModeE] [WARNING] 返还营旗失败: typeId=" + typeId);
            }

            return refunded;
        }

        private bool HandleModeEStartupFailureRecovery(string reason)
        {
            if (!modeEStartupRecoveryArmed)
            {
                DevLog("[ModeE] [WARNING] 启动失败，但未找到可用的回滚上下文: " + reason);
                return false;
            }

            HashSet<int> startupInventorySnapshot = modeEStartupInventorySnapshot != null
                ? new HashSet<int>(modeEStartupInventorySnapshot)
                : null;
            int consumedFlagTypeId = modeEStartupFlagTypeId;
            bool hasPlayerPosition = modeEStartupHasPlayerPosition;
            Vector3 startupPlayerPosition = modeEStartupPlayerPosition;

            DevLog("[ModeE] [WARNING] " + reason + "，开始回滚启动现场");
            StopModeEStartupWarmupIfPending();
            ResetModeEStartupRecoveryState();

            try
            {
                if (modeEActive)
                {
                    EndModeE(false);
                }
            }
            catch (Exception cleanupException)
            {
                DevLog("[ModeE] [WARNING] 启动失败后的 EndModeE 清理异常: " + cleanupException.Message);
            }

            bool restoredPlayerPosition = !hasPlayerPosition || TryRestoreModeEStartupPlayerPosition(startupPlayerPosition);
            bool rollbackSucceeded = RollbackModeEStartupInventory(startupInventorySnapshot);
            bool refunded = TryRefundModeEStartupFlag(consumedFlagTypeId);
            ShowModeEStartupFailureRecoveryMessage(rollbackSucceeded, refunded, restoredPlayerPosition);
            return false;
        }

        private void ShowModeEStartupFailureRecoveryMessage(bool rollbackSucceeded, bool refunded, bool restoredPlayerPosition)
        {
            string chineseMessage;
            string englishMessage;

            if (rollbackSucceeded && refunded && restoredPlayerPosition)
            {
                chineseMessage = "划地为营模式启动失败，已恢复玩家位置、回滚启动物资并返还营旗。";
                englishMessage = "Faction Battle start failed. Player position was restored, startup items were rolled back, and the faction flag was refunded.";
            }
            else
            {
                chineseMessage = "划地为营模式启动失败，已尝试恢复玩家位置、回滚启动物资并返还营旗；其中部分恢复失败，请查看日志。";
                englishMessage = "Faction Battle start failed. Player position restore, startup rollback, and faction flag refund were attempted, but some recovery steps failed. Check the log.";
            }

            ShowMessage(L10n.T(chineseMessage, englishMessage));
        }

        private System.Collections.IEnumerator WaitForModeEStartupVerification(Action<bool> onCompleted)
        {
            bool verified = false;

            try
            {
                if (!modeEStartupRecoveryArmed)
                {
                    yield break;
                }

                float deadline = Time.unscaledTime + MODEE_STARTUP_VERIFICATION_TIMEOUT_SECONDS;
                while (Time.unscaledTime < deadline)
                {
                    if (modeEStartupFirstBossSpawned || modeEAliveEnemies.Count > 0)
                    {
                        DisarmModeEStartupRecovery("已检测到首个成功生成的Boss");
                        verified = true;
                        break;
                    }

                    if (!modeEActive)
                    {
                        HandleModeEStartupFailureRecovery("Mode E 在启动验证阶段提前退出");
                        break;
                    }

                    yield return null;
                }

                if (!verified && modeEStartupRecoveryArmed)
                {
                    if (modeEStartupFirstBossSpawned || modeEAliveEnemies.Count > 0)
                    {
                        DisarmModeEStartupRecovery("超时前已检测到成功生成的Boss");
                        verified = true;
                    }
                    else
                    {
                        HandleModeEStartupFailureRecovery(
                            "Mode E 启动验证超时，未检测到任何成功生成的Boss (resolved="
                            + modeESpawnResolved + "/" + modeETotalSpawnExpected + ")");
                    }
                }
            }
            finally
            {
                onCompleted?.Invoke(verified);
            }
        }

        /// <summary>
        /// 检查并尝试启动 Mode E（在进入竞技场时调用）
        /// </summary>
        public bool TryStartModeE()
        {
            ModeEStartupProfiler profiler = new ModeEStartupProfiler("TryStartModeE");
            string profileStatus = "failed";
            int consumedFlagTypeId = -1;
            HashSet<int> startupInventorySnapshot = null;
            Vector3 startupPlayerPosition = Vector3.zero;
            try
            {
                ResetModeEStartupRecoveryState();

                // 互斥保护：Mode D 已激活时不启动 Mode E
                if (modeDActive)
                {
                    profileStatus = "skipped: ModeD active";
                    DevLog("[ModeE] Mode D 已激活，跳过 Mode E 启动");
                    return false;
                }

                // 检测营旗
                var (faction, flagItem) = DetectFactionFlag();
                profiler.Mark("DetectFactionFlag");
                if (!faction.HasValue || flagItem == null)
                {
                    profileStatus = "skipped: no faction flag";
                    DevLog("[ModeE] 未检测到营旗，不启动 Mode E");
                    return false;
                }

                // 检查裸装条件（复用 Mode D 的裸装检测）
                if (!IsPlayerNaked())
                {
                    profiler.Mark("IsPlayerNaked");
                    profileStatus = "skipped: player not naked";
                    DevLog("[ModeE] 玩家不满足裸装条件，拒绝启动");
                    ShowMessage(L10n.T(
                        "划地为营模式需要裸装入场！请清空所有装备后重试。",
                        "Faction Battle requires naked entry! Please remove all equipment."
                    ));
                    return false;
                }

                if (!TryCaptureModeEStartupPlayerPosition(out startupPlayerPosition))
                {
                    profileStatus = "failed: player position capture failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：无法记录玩家当前位置。",
                        "Faction Battle start failed: unable to capture the player's current position."
                    ));
                    return false;
                }

                if (!CaptureModeEStartupInventorySnapshot(out startupInventorySnapshot))
                {
                    profileStatus = "failed: snapshot capture failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：无法建立启动回滚快照。",
                        "Faction Battle start failed: unable to capture the startup rollback snapshot."
                    ));
                    return false;
                }

                // 消耗营旗
                profiler.Mark("IsPlayerNaked");
                if (!TryGetModeEStartupFlagTypeId(flagItem, out consumedFlagTypeId))
                {
                    profileStatus = "failed: flag type lookup failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：营旗数据异常，已取消消耗。",
                        "Faction Battle start failed: the faction flag data is invalid, so it was not consumed."
                    ));
                    return false;
                }
                if (!ConsumeFactionFlag(flagItem))
                {
                    profileStatus = "failed: flag consume failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：营旗消耗异常。",
                        "Faction Battle start failed: unable to consume the faction flag."
                    ));
                    return false;
                }
                ArmModeEStartupRecovery(startupInventorySnapshot, consumedFlagTypeId, startupPlayerPosition);
                profiler.Mark("ConsumeFactionFlag");

                // 启动 Mode E
                bool started = StartModeE(faction.Value);
                profiler.Mark("StartModeE");
                if (!started)
                {
                    profileStatus = "failed: startup rejected after consume";
                    HandleModeEStartupFailureRecovery("StartModeE 返回失败");
                    return false;
                }
                profileStatus = "success";
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] TryStartModeE 失败: " + e.Message);
                if (modeEStartupRecoveryArmed)
                {
                    HandleModeEStartupFailureRecovery("TryStartModeE 异常: " + e.Message);
                    profileStatus = "failed: exception recovery invoked";
                }
                return false;
            }
            finally
            {
                profiler.Complete(profileStatus);
            }
        }

        /// <summary>
        /// 启动 Mode E 模式
        /// </summary>
        private bool StartModeE(Teams faction)
        {
            ModeEStartupProfiler profiler = new ModeEStartupProfiler("StartModeE", faction.ToString());
            try
            {
                DevLog("[ModeE] 启动 Mode E 模式，阵营: " + faction);

                // 清理可能从无间炼狱残留的状态，避免 InfiniteHellCashMagnet/UI 提示误激活
                infiniteHellMode = false;
                infiniteHellWaveIndex = 0;
                infiniteHellCashPool = 0L;
                infiniteHellMilestoneRewardTier = 0;
                infiniteHellWaveCashThisWave = 0L;
                ClearCashMagnetState();

                modeEActive = true;
                int modeESessionToken = BeginModeESession();
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                modeEPlayerLastHitKillCount = 0;
                RemoveModeEPlayerScalingModifiers();
                ResetModeESharedRuntimeState(clearSpawnAllocation: true, clearSpawnerCache: false, stopWarmupCoroutine: false);
                ResetModeEUiCaches();
                modeEPlayerFaction = faction;
                ClearEnemyRecoveryMonitorState();

                // 重置龙裔/龙王全局限制标记
                modeEDragonDescendantSpawned = false;
                modeEDragonKingSpawned = false;

                // 初始化各阵营死亡计数
                for (int i = 0; i < ModeEAvailableFactions.Length; i++)
                {
                    modeEFactionDeathCount[ModeEAvailableFactions[i]] = 0;
                }
                profiler.Mark("ResetState");

                // 设置玩家阵营
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    // 爷的营旗：玩家保持 player 阵营，不需要 SetTeam
                    if (faction != Teams.player)
                    {
                        player.SetTeam(faction);
                        DevLog("[ModeE] 玩家阵营已设置为: " + faction);
                    }
                    else
                    {
                        DevLog("[ModeE] 爷的营旗：玩家保持 player 阵营，所有Boss均为敌对");
                    }
                }
                profiler.Mark("SetupPlayerFaction");

                EnsureModeEPlayerNameTag();
                profiler.Mark("SetupPlayerNameTag");

                // 显示阵营气泡
                ShowFactionBubble(faction);
                profiler.Mark("ShowFactionBubble");

                // 初始化物品池和敌人池（复用 Mode D 逻辑）
                InitializeModeDItemPools();
                profiler.Mark("InitializeModeDItemPools");
                InitializeModeDEnemyPools();
                profiler.Mark("InitializeModeDEnemyPools");
                BuildModeEFactionPresetCaches();
                profiler.Mark("BuildModeEFactionPresetCaches");

                // 前置构建全局掉落池（避免战斗中首次调用时卡顿）
                EnsureModeDGlobalItemPool();
                profiler.Mark("EnsureModeDGlobalItemPool");

                // Mode E 不激活 BossRush 运行时状态（IsActive 保持 false）
                // 仅订阅龙息Buff处理器，确保龙裔遗族Boss的龙息能触发龙焰灼烧
                DragonBreathBuffHandler.Subscribe();
                profiler.Mark("SubscribeDragonBreath");

                // 分配刷怪点给各阵营（优先使用地图配置的 Mode E 专用刷怪点，无配置时兜底使用原地图 spawner 位置）
                AllocateSpawnPoints();
                profiler.Mark("AllocateSpawnPoints");

                // 传送玩家到安全位置（远离Boss的安全点）
                TeleportPlayerToSafePosition();
                profiler.Mark("TeleportPlayerToSafePosition");

                // 发放初始装备（复用 Mode D 的 Starter Kit）
                GivePlayerStarterKit();

                // 零度挑战地图：额外发放保暖装备（头盔 ID:1312 + 护甲 ID:1307）
                ModeEGiveColdWeatherGear();

                // 独狼阵营：额外发放补给物品（3个id=881 + 3个id=660）
                if (faction == Teams.player)
                {
                    ModeEGiveLoneWolfSupplies();
                }
                profiler.Mark("GiveLoadout");

                CaptureModeEFLootboxBaseline();
                profiler.Mark("CaptureLootboxBaseline");

                // 一次性生成所有阵营的 Boss（UniTaskVoid fire-and-forget，抑制 CS4014 警告）
                #pragma warning disable CS4014
                ModeESpawnAllBosses(modeESessionToken: modeESessionToken, modeESessionRelatedScene: relatedScene);
                profiler.Mark("ScheduleBosses");
                #pragma warning restore CS4014

                // 生成神秘商人 NPC（fire-and-forget）
                #pragma warning disable CS4014
                SpawnModeEMerchant(modeESessionToken: modeESessionToken, modeESessionRelatedScene: relatedScene);
                profiler.Mark("ScheduleMerchant");
                #pragma warning restore CS4014

                // 在玩家出生点生成快递员阿稳（站在原地不移动）
                SpawnCourierNPC();
                profiler.Mark("SpawnCourier");

                ShowMessage(L10n.T(
                    "划地为营模式已激活！阵营：" + GetFactionDisplayName(faction),
                    "Faction Battle activated! Faction: " + faction.ToString()
                ));
                ShowBigBanner(L10n.T(
                    "欢迎来到 <color=red>划地为营</color>！",
                    "Welcome to <color=red>Faction Battle</color>!"
                ));
                profiler.Mark("ShowModeEUI");
                profiler.Complete("success");
                return true;
            }
            catch (Exception e)
            {
                profiler.Complete("failed");
                DevLog("[ModeE] [ERROR] StartModeE 失败: " + e.Message);
                try
                {
                    EndModeE(false);
                }
                catch (Exception cleanupException)
                {
                    DevLog("[ModeE] [WARNING] StartModeE 失败后的清理异常: " + cleanupException.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// 在玩家头顶显示阵营气泡（"阵营：xxx"）
        /// </summary>
        private void ShowFactionBubble(Teams faction)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] ShowFactionBubble: 玩家或 transform 为 null");
                    return;
                }

                string factionName = GetFactionDisplayName(faction);
                string bubbleText = L10n.T("阵营：" + factionName, "Faction: " + faction.ToString());

                // 使用游戏原版 DialogueBubblesManager 显示气泡，时长 3 秒
                DialogueBubblesManager.Show(bubbleText, player.transform, 2.5f, false, false, -1f, 3f);
                DevLog("[ModeE] 显示阵营气泡: " + bubbleText);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ShowFactionBubble 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 结束 Mode E 模式
        /// </summary>
        public void EndModeE(bool showEndMessage = true)
        {
            try
            {
                if (!modeEActive) return;

                DevLog("[ModeE] 结束 Mode E 模式");

                // 先置 modeEActive = false，防止后续 Hurt() 触发的 OnModeEEnemyDeath
                // 回调中再对即将死亡的敌人执行无意义的 ApplyFactionDeathScaling
                modeEActive = false;
                InvalidateModeESession();
                ClearEnemyRecoveryMonitorState();
                ClearPendingBossAggroQueue();
                RemoveModeEPlayerScalingModifiers();
                modeEPlayerLastHitKillCount = 0;

                // 恢复玩家阵营
                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        player.SetTeam(Teams.player);
                        DevLog("[ModeE] 玩家阵营已恢复为 player");
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeE] [WARNING] 恢复玩家阵营失败: " + e.Message);
                }

                CleanupModeEPlayerNameTag();
                ResetModeEUiCaches();

                // 清理所有存活的 Mode E 敌人（优先使用游戏API触发正常死亡流程）
                // [L4修复] 清理前先阻止所有敌人掉落战利品箱子，防止模式结束时友军Boss掉落一堆箱子
                CharacterMainControl[] trackedEnemies = modeEAliveEnemies.ToArray();
                for (int i = trackedEnemies.Length - 1; i >= 0; i--)
                {
                    try
                    {
                        CharacterMainControl enemy = trackedEnemies[i];
                        if (enemy != null && enemy.gameObject != null)
                        {
                            Teams? enemyFaction = null;
                            try
                            {
                                enemyFaction = enemy.Team;
                            }
                            catch (Exception e)
                            {
                                DevLog("[ModeE] [WARNING] 结束模式时读取敌人阵营失败: index=" + i + ", " + e.Message);
                            }

                            CleanupModeEEnemyRuntimeState(enemy, enemyFaction);

                            // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
                            try
                            {
                                if (enemy.characterPreset != null)
                                {
                                    UnityEngine.Object.Destroy(enemy.characterPreset);
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[ModeE] [WARNING] 结束模式时销毁敌人 characterPreset 失败: index=" + i + ", " + e.Message);
                            }

                            // 阻止掉落战利品箱子（模式结束清理，不应产生掉落物）
                            enemy.dropBoxOnDead = false;

                            // 使用 Health.Hurt() 造成致命伤害，触发正常死亡流程（动画等）
                            Health health = enemy.Health;
                            if (health != null && !health.IsDead)
                            {
                                DamageInfo dmgInfo = new DamageInfo();
                                dmgInfo.damageValue = health.MaxHealth * 10f;
                                dmgInfo.ignoreArmor = true;
                                health.Hurt(dmgInfo);
                            }
                            else
                            {
                                // Health 不可用时回退到直接销毁
                                UnityEngine.Object.Destroy(enemy.gameObject);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        DevLog("[ModeE] [WARNING] 结束模式时清理敌人失败: index=" + i + ", " + e.Message);
                    }
                }

                // 清理神秘商人 NPC
                CleanupModeEMerchant();

                // 清理快递员阿稳 NPC
                DestroyCourierNPC();

                // 重置所有状态（modeEActive 已在清理前置为 false）
                ResetModeESharedRuntimeState(clearSpawnAllocation: true, clearSpawnerCache: true, stopWarmupCoroutine: true);

                // 重置刷怪消耗品击杀计数器
                modeERespawnKillCounter = 0;

                // 清理龙息Buff处理器（防止非 BossRush 场景中意外触发龙焰灼烧）
                DragonBreathBuffHandler.Cleanup();

                if (showEndMessage)
                {
                    ShowMessage(L10n.T(
                        "划地为营模式已结束！",
                        "Faction Battle ended!"
                    ));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] EndModeE 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 自检机制

        /// <summary>
        /// Mode E 存活敌人列表自检：清理已死亡/已销毁的敌人引用，补偿丢失的死亡事件
        /// <para>防止敌人死亡事件丢失（瞬杀、事件触发时机等）导致列表残留和缩放计算不准确</para>
        /// </summary>
        private void ModeEIntegrityCheck()
        {
            try
            {
                if (!modeEActive) return;

                int removedCount = 0;
                for (int i = modeEAliveEnemies.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = modeEAliveEnemies[i];
                    if (object.ReferenceEquals(enemy, null))
                    {
                        modeEAliveEnemies.RemoveAt(i);
                        removedCount++;
                        continue;
                    }

                    if (enemy == null || enemy.gameObject == null || enemy.Health == null || enemy.Health.IsDead)
                    {
                        // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
                        try
                        {
                            if (enemy != null && enemy.characterPreset != null)
                            {
                                UnityEngine.Object.Destroy(enemy.characterPreset);
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[ModeE] [WARNING] 自检时销毁敌人 characterPreset 失败: index=" + i + ", " + e.Message);
                        }

                        // 从虚拟 SpawnerRoot 移除
                        UnregisterModeEEnemyFromSpawnerRoot(enemy);

                        // 补偿丢失的死亡事件：递增该阵营死亡计数
                        // [修复] 当 enemy.Team 访问失败时，从所有阵营列表中暴力移除，防止残留无效引用
                    try
                    {
                        if (!(enemy == null))
                        {
                            Teams faction = enemy.Team;
                            if (modeEFactionDeathCount.ContainsKey(faction))
                            {
                                modeEFactionDeathCount[faction]++;
                            }
                            CleanupModeEEnemyRuntimeState(enemy, faction);
                        }
                        else
                        {
                            CleanupModeEEnemyRuntimeState(enemy);
                        }
                    }
                    catch (Exception e)
                    {
                        // Unity 已销毁对象，无法读取 Team —— 从所有阵营列表中暴力移除
                        DevLog("[ModeE] [WARNING] 自检时读取敌人阵营失败，改为全量清理: index=" + i + ", " + e.Message);
                        CleanupModeEEnemyRuntimeState(enemy);
                        }

                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    DevLog("[ModeE] 自检清理了 " + removedCount + " 个已死亡/已销毁的敌人引用");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEIntegrityCheck 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 辅助方法

        /// <summary>
        /// 零度挑战地图专用：发放保暖装备（头盔 + 护甲）
        /// 仅在 Level_ChallengeSnow 场景下生效，硬编码物品ID
        /// </summary>
        private void ModeEGiveColdWeatherGear()
        {
            try
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                // 零度挑战地图和37号实验区都需要发放保暖装备
                if (currentScene != "Level_ChallengeSnow" && currentScene != "Level_SnowMilitaryBase") return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                DevLog("[ModeE] 零度挑战地图：发放保暖装备...");

                // 头盔 ID:1312
                Item helmet = ItemAssetsCollection.InstantiateSync(1312);
                if (helmet != null)
                {
                    bool equipped = main.CharacterItem.TryPlug(helmet, true, null, 0);
                    if (!equipped) ItemUtilities.SendToPlayerCharacterInventory(helmet, false);
                    DevLog("[ModeE] 发放保暖头盔: " + helmet.DisplayName);
                }

                // 护甲 ID:1307
                Item armor = ItemAssetsCollection.InstantiateSync(1307);
                if (armor != null)
                {
                    bool equipped = main.CharacterItem.TryPlug(armor, true, null, 0);
                    if (!equipped) ItemUtilities.SendToPlayerCharacterInventory(armor, false);
                    DevLog("[ModeE] 发放保暖护甲: " + armor.DisplayName);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEGiveColdWeatherGear 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 独狼阵营专属补给：发放3个id=881和3个id=660的物品到玩家背包
        /// </summary>
        private void ModeEGiveLoneWolfSupplies()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                DevLog("[ModeE] 独狼阵营：发放专属补给物品...");

                // 发放3个 id=881 的物品
                for (int i = 0; i < 3; i++)
                {
                    Item item881 = ItemAssetsCollection.InstantiateSync(881);
                    if (item881 != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(item881, false);
                        DevLog("[ModeE] 独狼补给：发放物品 881 - " + item881.DisplayName);
                    }
                }

                // 发放3个 id=660 的物品
                for (int i = 0; i < 3; i++)
                {
                    Item item660 = ItemAssetsCollection.InstantiateSync(660);
                    if (item660 != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(item660, false);
                        DevLog("[ModeE] 独狼补给：发放物品 660 - " + item660.DisplayName);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeEGiveLoneWolfSupplies 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取阵营的中文显示名称
        /// </summary>
        private string GetFactionDisplayName(Teams faction)
        {
            switch (faction)
            {
                case Teams.scav:    return L10n.T("拾荒者", "Scav");
                case Teams.usec:    return L10n.T("USEC", "USEC");
                case Teams.bear:    return L10n.T("BEAR", "BEAR");
                case Teams.lab:     return L10n.T("实验室", "Lab");
                case Teams.wolf:    return L10n.T("狼群", "Wolf");
                case Teams.player:  return L10n.T("独狼", "Lone Wolf");
                default:            return faction.ToString();
            }
        }

        /// <summary>
        /// 获取阵营后缀字符串（供 Harmony 补丁在 HealthBar 名字后追加）
        /// 格式：" - 阵营名"，非 Mode E 阵营返回 null
        /// </summary>
        public string GetModeEFactionSuffix(Teams faction)
        {
            string name = GetFactionDisplayName(faction);
            if (string.IsNullOrEmpty(name)) return null;
            return " - " + name;
        }

        private void ClearModeEHealthBarOverrideCache(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            int barId = healthBar.GetInstanceID();
            modeEHealthBarBaseTextByBarId.Remove(barId);
            modeEHealthBarDesiredTextByBarId.Remove(barId);
            modeEHealthBarTargetIdsByBarId.Remove(barId);
            modeEHealthBarAppliedVersionByBarId.Remove(barId);
        }

        private string BuildModeEDesiredHealthBarText(
            CharacterMainControl character,
            TextMeshProUGUI nameText,
            int barId,
            int targetId,
            bool forceShowName)
        {
            if (character == null)
            {
                return null;
            }

            string baseText = null;
            if (forceShowName)
            {
                baseText = GetModeEPlayerName();
            }
            else
            {
                int cachedTargetId;
                bool needsBaseRefresh =
                    !modeEHealthBarBaseTextByBarId.TryGetValue(barId, out baseText) ||
                    string.IsNullOrEmpty(baseText) ||
                    !modeEHealthBarTargetIdsByBarId.TryGetValue(barId, out cachedTargetId) ||
                    cachedTargetId != targetId;

                if (needsBaseRefresh)
                {
                    baseText = nameText != null ? StripModeEFactionSuffix(nameText.text) : null;
                    if (string.IsNullOrEmpty(baseText))
                    {
                        baseText = GetModeEActorDisplayName(character);
                    }

                    if (!string.IsNullOrEmpty(baseText))
                    {
                        modeEHealthBarBaseTextByBarId[barId] = baseText;
                    }
                }
            }

            if (string.IsNullOrEmpty(baseText))
            {
                return null;
            }

            Teams displayFaction = forceShowName ? ModeEPlayerFaction : character.Team;
            string factionSuffix = GetModeEFactionSuffix(displayFaction);
            return string.IsNullOrEmpty(factionSuffix) ? baseText : baseText + factionSuffix;
        }

        /// <summary>
        /// 在 HealthBar 名字后追加阵营后缀，供统一的 HealthBar patch 调用。
        /// </summary>
        internal void ApplyModeEHealthBarNameOverride(HealthBar healthBar, TextMeshProUGUI nameText)
        {
            if (healthBar == null || nameText == null) return;

            Health target = healthBar.target;
            if (target == null)
            {
                ClearModeEHealthBarOverrideCache(healthBar);
                return;
            }

            CharacterMainControl character = target.TryGetCharacter();
            if (character == null)
            {
                ClearModeEHealthBarOverrideCache(healthBar);
                return;
            }

            bool forceShowName = character.IsMainCharacter;
            if (!forceShowName && !nameText.gameObject.activeSelf) return;

            SyncModeEHealthBarNameLanguageState();

            int barId = healthBar.GetInstanceID();
            int targetId = target.GetInstanceID();
            string desiredText = null;
            int appliedVersion = 0;
            int cachedTargetId = 0;
            bool targetChanged =
                !modeEHealthBarTargetIdsByBarId.TryGetValue(barId, out cachedTargetId) ||
                cachedTargetId != targetId;
            if (targetChanged)
            {
                modeEHealthBarBaseTextByBarId.Remove(barId);
                modeEHealthBarDesiredTextByBarId.Remove(barId);
                modeEHealthBarAppliedVersionByBarId.Remove(barId);
            }

            bool needsRebuild =
                forceShowName ||
                targetChanged ||
                !modeEHealthBarDesiredTextByBarId.TryGetValue(barId, out desiredText) ||
                string.IsNullOrEmpty(desiredText) ||
                !modeEHealthBarAppliedVersionByBarId.TryGetValue(barId, out appliedVersion) ||
                appliedVersion != modeEHealthBarNameVersion;

            if (needsRebuild)
            {
                desiredText = BuildModeEDesiredHealthBarText(character, nameText, barId, targetId, forceShowName);
                if (string.IsNullOrEmpty(desiredText))
                {
                    ClearModeEHealthBarOverrideCache(healthBar);
                    return;
                }

                modeEHealthBarDesiredTextByBarId[barId] = desiredText;
                modeEHealthBarAppliedVersionByBarId[barId] = modeEHealthBarNameVersion;
                modeEHealthBarTargetIdsByBarId[barId] = targetId;
            }

            if (forceShowName && !nameText.gameObject.activeSelf)
            {
                nameText.gameObject.SetActive(true);
            }

            if (!string.Equals(nameText.text, desiredText, StringComparison.Ordinal))
            {
                nameText.text = desiredText;
            }
        }

        private string StripModeEFactionSuffix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string sanitized = text;
            while (TryTrimTrailingModeEFactionSuffix(ref sanitized))
            {
            }
            return sanitized;
        }

        private bool TryTrimTrailingModeEFactionSuffix(ref string text)
        {
            return TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.scav)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.usec)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.bear)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.lab)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.wolf)) ||
                   TryTrimOneModeEFactionSuffix(ref text, GetModeEFactionSuffix(Teams.player));
        }

        private static bool TryTrimOneModeEFactionSuffix(ref string text, string suffix)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(suffix)) return false;
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) return false;
            text = text.Substring(0, text.Length - suffix.Length);
            return true;
        }

        #region Mode E 阵营存活列表管理（P4性能优化）

        /// <summary>
        /// 将敌人添加到阵营独立存活列表
        /// </summary>
        private void AddToFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (!modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                list = new List<CharacterMainControl>(8);
                modeEFactionAliveMap[faction] = list;
            }

            list.Add(enemy);
        }

        /// <summary>
        /// 从阵营独立存活列表中移除敌人
        /// </summary>
        private void RemoveFromFactionAliveList(Teams faction, CharacterMainControl enemy)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (object.ReferenceEquals(list[i], enemy))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 将敌人登记为 Mode E 运行时存活对象，避免重复加入全局/阵营列表。
        /// </summary>
        private void TrackModeEAliveEnemy(CharacterMainControl enemy, Teams faction)
        {
            if (enemy == null)
            {
                return;
            }

            if (!modeEAliveEnemySet.Add(enemy))
            {
                return;
            }

            modeEAliveEnemies.Add(enemy);
            modeEAliveEnemyFactionMap[enemy] = faction;
            AddToFactionAliveList(faction, enemy);
        }

        /// <summary>
        /// 从 Mode E 运行时存活对象登记中移除敌人。
        /// </summary>
        private void UntrackModeEAliveEnemy(CharacterMainControl enemy, Teams? faction = null)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            modeEAliveEnemySet.Remove(enemy);
            modeEAliveEnemies.Remove(enemy);

            Teams trackedFaction;
            if (!faction.HasValue && modeEAliveEnemyFactionMap.TryGetValue(enemy, out trackedFaction))
            {
                faction = trackedFaction;
            }

            modeEAliveEnemyFactionMap.Remove(enemy);

            if (faction.HasValue)
            {
                RemoveFromFactionAliveList(faction.Value, enemy);
                return;
            }

            foreach (KeyValuePair<Teams, List<CharacterMainControl>> kvp in modeEFactionAliveMap)
            {
                RemoveFromFactionAliveList(kvp.Key, enemy);
            }
        }

        /// <summary>
        /// 获取指定阵营的存活敌人列表（只读访问，用于缩放遍历）
        /// </summary>
        private List<CharacterMainControl> GetFactionAliveList(Teams faction)
        {
            List<CharacterMainControl> list;
            if (modeEFactionAliveMap.TryGetValue(faction, out list))
            {
                return list;
            }
            return null;
        }

        #endregion

        #endregion
    }
}

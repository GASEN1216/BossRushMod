// ============================================================================
// ModeEBattle.cs - Boss 生成与动态难度缩放
// ============================================================================
// 模块说明：
//   负责 Mode E 的 Boss 一次性生成、敌人死亡追踪和按阵营动态难度缩放。
//   敌人生成复用 SpawnEnemyCore 通用方法（支持龙裔遗族和龙王）。
//
// 动态缩放规则：
//   - 每个敌人在出生时记录阵营死亡基线 deathBaseline
//   - 个人层数 = 当前阵营死亡计数 - deathBaseline（最小为0）
//   - 每层生命/枪伤/近战伤害 +5%，玩家层数按最终击杀独立累计
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// Mode E Boss 生成与动态难度缩放模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 战斗管理字段

        /// <summary>预期生成的敌人总数</summary>
        private int modeETotalSpawnExpected = 0;

        /// <summary>已完成生成（成功或失败）的计数</summary>
        private int modeESpawnResolved = 0;

        /// <summary>Mode E 专用的虚拟 CharacterSpawnerRoot，用于让 BossLiveMapMod 检测到 Mode E 生成的敌人</summary>
        private CharacterSpawnerRoot modeEVirtualSpawnerRoot = null;

        /// <summary>虚拟 SpawnerRoot 中已登记的 Mode E 敌人，避免重复 AddCreatedCharacter。</summary>
        private readonly HashSet<CharacterMainControl> modeESpawnerRootRegisteredEnemies = new HashSet<CharacterMainControl>();

        private static FieldInfo modeESpawnerRootCreatedCharactersField = null;
        private static PropertyInfo modeESpawnerRootCreatedCharactersProperty = null;
        private static bool modeESpawnerRootCreatedCharactersAccessorCached = false;
        private static bool modeESpawnerRootCreatedCharactersAccessorMissingLogged = false;

        /// <summary>Mode E 中是否已生成龙裔遗族（全局限制最多1个）</summary>
        private bool modeEDragonDescendantSpawned = false;

        /// <summary>Mode E 中是否已生成龙王（全局限制最多1个）</summary>
        private bool modeEDragonKingSpawned = false;

        #endregion

        #region Mode E Boss 生成方法

        /// <summary>狼阵营可用的不重复 Boss 预设数量（在 ModeESpawnAllBosses 开始时预计算）</summary>
        private int modeEWolfBossCount = 0;

        /// <summary>狼阵营已分配的 Boss 刷怪点计数（每分配一个 Boss 递增，超过 modeEWolfBossCount 后出小怪）</summary>
        private int modeEWolfBossAssigned = 0;

        private readonly Dictionary<Teams, List<EnemyPresetInfo>> modeEBossPoolByFaction
            = new Dictionary<Teams, List<EnemyPresetInfo>>();
        private readonly Dictionary<Teams, List<EnemyPresetInfo>> modeEBossPoolByFactionWithoutDragonDescendant
            = new Dictionary<Teams, List<EnemyPresetInfo>>();
        private readonly Dictionary<Teams, List<EnemyPresetInfo>> modeEMinionPoolByFaction
            = new Dictionary<Teams, List<EnemyPresetInfo>>();
        private readonly Dictionary<Teams, float> modeEWeightedMinionTotalHealthByFaction
            = new Dictionary<Teams, float>();
        private bool modeEFactionPresetCachesBuilt = false;
        private List<EnemyPresetInfo> modeECachedBossPoolSource = null;
        private int modeECachedBossPoolCount = -1;
        private int modeECachedMinionPoolCount = -1;

        private List<EnemyPresetInfo> GetOrCreateModeEPresetList(
            Dictionary<Teams, List<EnemyPresetInfo>> presetMap,
            Teams faction)
        {
            List<EnemyPresetInfo> list;
            if (!presetMap.TryGetValue(faction, out list))
            {
                list = new List<EnemyPresetInfo>();
                presetMap[faction] = list;
            }

            return list;
        }

        private void BuildModeEFactionPresetCaches()
        {
            List<EnemyPresetInfo> filteredBossPool = GetFilteredEnemyPresets();
            int bossPoolCount = filteredBossPool != null ? filteredBossPool.Count : 0;
            int minionPoolCount = modeDMinionPool != null ? modeDMinionPool.Count : 0;
            if (modeEFactionPresetCachesBuilt &&
                object.ReferenceEquals(modeECachedBossPoolSource, filteredBossPool) &&
                modeECachedBossPoolCount == bossPoolCount &&
                modeECachedMinionPoolCount == minionPoolCount)
            {
                return;
            }

            modeEBossPoolByFaction.Clear();
            modeEBossPoolByFactionWithoutDragonDescendant.Clear();
            modeEMinionPoolByFaction.Clear();
            modeEWeightedMinionTotalHealthByFaction.Clear();

            if (filteredBossPool != null)
            {
                for (int i = 0; i < filteredBossPool.Count; i++)
                {
                    EnemyPresetInfo boss = filteredBossPool[i];
                    if (boss == null || string.IsNullOrEmpty(boss.name) || IsDragonKingPreset(boss))
                    {
                        continue;
                    }

                    Teams faction = (Teams)boss.team;
                    GetOrCreateModeEPresetList(modeEBossPoolByFaction, faction).Add(boss);
                    if (!IsDragonDescendantPreset(boss))
                    {
                        GetOrCreateModeEPresetList(modeEBossPoolByFactionWithoutDragonDescendant, faction).Add(boss);
                    }
                }
            }

            if (modeDMinionPool != null)
            {
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo minion = modeDMinionPool[i];
                    if (minion == null || string.IsNullOrEmpty(minion.name))
                    {
                        continue;
                    }

                    Teams faction = (Teams)minion.team;
                    GetOrCreateModeEPresetList(modeEMinionPoolByFaction, faction).Add(minion);

                    float totalWeight = 0f;
                    modeEWeightedMinionTotalHealthByFaction.TryGetValue(faction, out totalWeight);
                    modeEWeightedMinionTotalHealthByFaction[faction] = totalWeight + Mathf.Max(minion.baseHealth, 1f);
                }
            }

            modeECachedBossPoolSource = filteredBossPool;
            modeECachedBossPoolCount = bossPoolCount;
            modeECachedMinionPoolCount = minionPoolCount;
            modeEFactionPresetCachesBuilt = true;
        }

        private List<EnemyPresetInfo> GetModeEBossPoolForFaction(Teams faction, bool includeDragonDescendant)
        {
            BuildModeEFactionPresetCaches();

            List<EnemyPresetInfo> list;
            var source = includeDragonDescendant
                ? modeEBossPoolByFaction
                : modeEBossPoolByFactionWithoutDragonDescendant;
            return source.TryGetValue(faction, out list) ? list : null;
        }

        private List<EnemyPresetInfo> GetModeEMinionPoolForFaction(Teams faction)
        {
            BuildModeEFactionPresetCaches();

            List<EnemyPresetInfo> list;
            return modeEMinionPoolByFaction.TryGetValue(faction, out list) ? list : null;
        }

        /// <summary>
        /// 在所有阵营的刷怪点一次性生成全部 Boss
        /// 按距离玩家由近到远分批生成，每批之间让出一帧，避免卡顿
        /// </summary>
        public async UniTaskVoid ModeESpawnAllBosses(
            int modeFSessionToken = 0,
            int modeFRelatedScene = -1,
            int modeESessionToken = 0,
            int modeESessionRelatedScene = -1)
        {
            try
            {
                if (!IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene) ||
                    modeESpawnAllocation == null)
                {
                    DevLog("[ModeE] ModeESpawnAllBosses: no active map mode or spawn allocation");
                    return;
                }

                DevLog("[ModeE] 开始分批生成所有阵营 Boss...");
                BuildModeEFactionPresetCaches();

                // 重置生成计数
                modeETotalSpawnExpected = 0;
                modeESpawnResolved = 0;

                // 收集所有待生成任务（阵营 + 刷怪点），按距离玩家由近到远排序
                Vector3 playerPos = Vector3.zero;
                CharacterMainControl playerRef = CharacterMainControl.Main;
                if (playerRef != null) playerPos = playerRef.transform.position;

                Vector3[] flattenedSpawnPoints = GetModeEFlattenedSpawnPoints();
                var spawnTasks = flattenedSpawnPoints.Length > 0
                    ? new List<(Teams faction, Vector3 pos)>(flattenedSpawnPoints.Length)
                    : new List<(Teams faction, Vector3 pos)>();
                foreach (var kvp in modeESpawnAllocation)
                {
                    Teams faction = kvp.Key;
                    List<Vector3> spawnPoints = kvp.Value;
                    for (int i = 0; i < spawnPoints.Count; i++)
                    {
                        spawnTasks.Add((faction, spawnPoints[i]));
                    }
                }

                // 按距离玩家由近到远排序
                spawnTasks.Sort((a, b) =>
                {
                    float distA = Vector3.SqrMagnitude(a.pos - playerPos);
                    float distB = Vector3.SqrMagnitude(b.pos - playerPos);
                    return distA.CompareTo(distB);
                });

                // 开局延迟刷怪期间，未来待生成 Boss 也要计入重刷道具的压力上限。
                modeETotalSpawnExpected = spawnTasks.Count;

                // 预计算狼阵营可用的不重复 Boss 数量（用于"先刷完所有 Boss 再出小怪"逻辑）
                modeEWolfBossCount = 0;
                modeEWolfBossAssigned = 0;
                List<EnemyPresetInfo> wolfBossPool = GetModeEBossPoolForFaction(Teams.wolf, true);
                if (wolfBossPool != null)
                {
                    modeEWolfBossCount = wolfBossPool.Count;
                    DevLog("[ModeE] 狼阵营可用 Boss 预设数量: " + modeEWolfBossCount);
                }

                // 分批生成：每个boss之间让出足够时间，分散到多帧执行，避免低端机卡顿
                // 前几个boss距离玩家最近，优先生成；后续逐步生成远处boss
                const int SPAWN_DELAY_MS = 500;
                const int INITIAL_BATCH_DELAY_MS = 800;

                for (int i = 0; i < spawnTasks.Count; i++)
                {
                    if (!IsModeEOrModeFSpawnSessionStillValid(
                            modeFSessionToken,
                            modeFRelatedScene,
                            modeESessionToken,
                            modeESessionRelatedScene))
                    {
                        break;
                    }

                    var task = spawnTasks[i];
                    SpawnSingleModeEBoss(
                        task.faction,
                        task.pos,
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene,
                        countSpawnAttemptImmediately: false);

                    // 每个boss之间等待，给角色创建和配装充足时间完成，减少帧率尖刺
                    if (i + 1 < spawnTasks.Count)
                    {
                        // 前3个boss使用更长间隔（初始化阶段资源竞争最激烈）
                        int delay = i < 3 ? INITIAL_BATCH_DELAY_MS : SPAWN_DELAY_MS;
                        await UniTask.Delay(delay);
                    }
                }

                DevLog("[ModeE] Boss 生成任务已全部下发，预期总数: " + modeETotalSpawnExpected);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ModeESpawnAllBosses 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 从 Boss 池中获取属于指定阵营的随机 Boss 预设
        /// 根据 EnemyPresetInfo.team 匹配阵营，尊重 Boss 的原始阵营归属
        /// </summary>
        private EnemyPresetInfo GetBossPresetForFaction(Teams faction)
        {
            List<EnemyPresetInfo> candidates = GetModeEBossPoolForFaction(faction, !modeEDragonDescendantSpawned);
            if (candidates == null || candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// 从小怪池中获取属于指定阵营的随机小怪预设（Boss 池不足时的兜底）
        /// </summary>
        private EnemyPresetInfo GetMinionPresetForFaction(Teams faction)
        {
            List<EnemyPresetInfo> candidates = GetModeEMinionPoolForFaction(faction);
            if (candidates == null || candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// 从小怪池中获取属于指定阵营的加权随机小怪预设（高血量优先但保持随机性）
        /// 权重 = baseHealth，血量越高被选中概率越大，但不保证每次都是最高血量的
        /// 用于狼阵营混入小怪时优先选择较强的小怪
        /// </summary>
        private EnemyPresetInfo GetWeightedMinionPresetForFaction(Teams faction)
        {
            List<EnemyPresetInfo> candidates = GetModeEMinionPoolForFaction(faction);
            if (candidates == null || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            float totalWeight = 0f;
            modeEWeightedMinionTotalHealthByFaction.TryGetValue(faction, out totalWeight);
            if (totalWeight <= 0f)
            {
                return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += Mathf.Max(candidates[i].baseHealth, 1f);
                if (roll <= cumulative)
                {
                    return candidates[i];
                }
            }

            // 兜底（浮点精度问题时返回最后一个）
            return candidates[candidates.Count - 1];
        }

        private void ResolveModeESpawnAttempt()
        {
            if (modeESpawnResolved < modeETotalSpawnExpected)
            {
                modeESpawnResolved++;
            }
            else
            {
                modeESpawnResolved = modeETotalSpawnExpected;
            }
        }

        private void ResolveModeESpawnAttemptIfCounted(bool spawnAttemptCounted)
        {
            if (spawnAttemptCounted)
            {
                ResolveModeESpawnAttempt();
            }
        }

        /// <summary>
        /// 生成单个 Mode E Boss（从 ModeESpawnAllBosses 分批调用）
        ///
        /// 阵营匹配设计：
        ///   Boss 的阵营由其预设中的 EnemyPresetInfo.team 决定（来自游戏原版 CharacterRandomPreset.team）。
        ///   每个阵营的刷怪点只从该阵营的 Boss 池中抽取，不随机覆盖阵营。
        ///   如果该阵营没有 Boss，则从该阵营的小怪池补充（提升为Boss，克隆预设设 showName=true）。
        ///   如果该阵营连小怪都没有，直接跳过该刷怪点（不从全局池抽取，避免阵营混乱）。
        ///   狼阵营特殊：先刷完所有 wolf Boss，剩余刷怪点才出小怪。
        ///   BEAR阵营特殊：原版游戏无 bear 预设，从全阵营小怪池兜底，并提升150%属性。
        /// </summary>
        private void SpawnSingleModeEBoss(
            Teams faction,
            Vector3 spawnPoint,
            int modeFSessionToken = 0,
            int modeFRelatedScene = -1,
            int modeESessionToken = 0,
            int modeESessionRelatedScene = -1,
            bool countSpawnAttemptImmediately = true)
        {
            bool spawnAttemptCounted = !countSpawnAttemptImmediately;
            bool reservedDragonDescendantSlot = false;

            try
            {
                EnemyPresetInfo bossPreset = null;
                bool isThisDragonDescendant = false;
                bool isBoss = true;
                // 标记：该敌人是否为小怪被提升为 Boss（需要在生成后克隆预设设 showName）
                bool isMinionPromotedToBoss = false;

                // 狼阵营特殊逻辑：先把所有 wolf Boss 刷完，剩余刷怪点出小怪（提升为Boss）
                if (faction == Teams.wolf)
                {
                    if (modeEWolfBossAssigned < modeEWolfBossCount)
                    {
                        // 还有 Boss 没刷完，优先出 Boss
                        bossPreset = GetBossPresetForFaction(faction);
                        if (bossPreset != null)
                        {
                            modeEWolfBossAssigned++;
                            DevLog("[ModeE] 狼阵营出Boss (" + modeEWolfBossAssigned + "/" + modeEWolfBossCount + "): " + bossPreset.displayName);
                        }
                    }

                    // Boss 已刷完或 Boss 池为空：出小怪（提升为Boss）
                    if (bossPreset == null)
                    {
                        bossPreset = GetWeightedMinionPresetForFaction(faction);
                        if (bossPreset != null)
                        {
                            // 小怪提升为 Boss：isBoss 保持 true（走 Boss 配装流程），标记需要克隆预设
                            isMinionPromotedToBoss = true;
                            DevLog("[ModeE] 狼阵营Boss已刷完，出小怪(提升为Boss): " + bossPreset.displayName);
                        }
                    }
                }
                else
                {
                    // 非狼阵营：原有逻辑，优先 Boss 池
                    bossPreset = GetBossPresetForFaction(faction);
                }

                // 第2优先：该阵营没有 Boss（非狼阵营）或狼阵营连小怪都没有，从该阵营的小怪池补充（提升为Boss）
                if (bossPreset == null)
                {
                    DevLog("[ModeE] 阵营 " + faction + " 无匹配Boss，尝试小怪池");
                    bossPreset = GetMinionPresetForFaction(faction);
                    if (bossPreset != null)
                    {
                        isMinionPromotedToBoss = true;
                    }
                }

                // BEAR阵营兜底：原版游戏无 bear 预设，从全阵营小怪池随机抽取
                if (bossPreset == null && faction == Teams.bear)
                {
                    DevLog("[ModeE] bear 阵营无匹配预设，从全阵营小怪池兜底");
                    bossPreset = GetAllFactionMinionPreset();
                    if (bossPreset != null)
                    {
                        isMinionPromotedToBoss = true;
                    }
                }

                // 该阵营无任何匹配预设（Boss池和小怪池都为空），直接跳过该刷怪点
                // 不从全局 Boss 池抽取，避免混入其他阵营的 Boss 导致阵营混乱
                if (bossPreset == null)
                {
                    DevLog("[ModeE] [WARNING] 阵营 " + faction + " 无任何匹配预设（Boss池+小怪池均为空），跳过该刷怪点");
                    ResolveModeESpawnAttemptIfCounted(spawnAttemptCounted);
                    return;
                }

                // 记录龙裔标记（龙皇在 Mode E 中已被完全排除，无需追踪）
                isThisDragonDescendant = IsDragonDescendantPreset(bossPreset);
                if (isThisDragonDescendant)
                {
                    modeEDragonDescendantSpawned = true;
                    reservedDragonDescendantSlot = true;
                }

                // 安全距离检查：如果分配的刷怪点距玩家太近，优先从本阵营刷怪点中选安全点
                CharacterMainControl modeEPlayer = CharacterMainControl.Main;
                Vector3 modeEPlayerPos = modeEPlayer != null ? modeEPlayer.transform.position : Vector3.zero;
                float spawnDistSqr = (spawnPoint - modeEPlayerPos).sqrMagnitude;
                Vector3 spawnPos;
                if (spawnDistSqr < SPAWN_SAFE_DISTANCE_SQR)
                {
                    // 先尝试本阵营的刷怪点
                    List<Vector3> factionPoints;
                    if (modeESpawnAllocation != null && modeESpawnAllocation.TryGetValue(faction, out factionPoints) && factionPoints.Count > 0)
                    {
                        spawnPos = FindNearestSafeSpawnPoint(factionPoints, modeEPlayerPos);
                    }
                    else
                    {
                        // 本阵营无可用点，回退到所有刷怪点
                        Vector3[] allModeEPoints = GetModeEFlattenedSpawnPoints();
                        spawnPos = FindNearestSafeSpawnPoint(allModeEPoints, modeEPlayerPos);
                    }
                }
                else
                {
                    spawnPos = GetSafeBossSpawnPosition(spawnPoint);
                }

                if (countSpawnAttemptImmediately)
                {
                    modeETotalSpawnExpected++;
                    spawnAttemptCounted = true;
                }

                Teams capturedFaction = faction;

                // skipDragonDescendant：防止 SpawnEnemyCore 重试时意外生成额外的龙裔
                // skipDragonKing：Mode E 完全排除龙皇，始终跳过
                bool skipDragon = !isThisDragonDescendant;
                bool skipKing = true; // Mode E 完全排除龙皇

                // 捕获龙裔标记，用于生成失败时回退
                bool capturedIsDD = isThisDragonDescendant;

                DevLog("[ModeE] 阵营 " + faction + " 生成: " + bossPreset.displayName + " (预设team=" + bossPreset.team + ", isBoss=" + isBoss + ", promoted=" + isMinionPromotedToBoss + ")");

                // 捕获小怪提升标记，传递给生成回调
                bool capturedPromoted = isMinionPromotedToBoss;

                SpawnEnemyCore(
                    bossPreset,
                    spawnPos,
                    isBoss,
                    isActiveCheck: () => IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene),
                    onSpawned: (ctx) =>
                    {
                        SyncModeEDragonDescendantSpawnFlag(capturedIsDD, ctx != null ? ctx.preset : null, "ModeE");
                        OnModeEEnemySpawned(ctx, capturedFaction, capturedPromoted);
                    },
                    onFailed: () =>
                    {
                        // 龙裔生成失败时回退全局标记，允许后续刷怪点再次尝试
                        if (capturedIsDD)
                        {
                            modeEDragonDescendantSpawned = false;
                            DevLog("[ModeE] 龙裔遗族生成失败，回退全局标记");
                        }

                        ResolveModeESpawnAttempt();
                        DevLog("[ModeE] 生成失败结案: resolved=" + modeESpawnResolved + "/" + modeETotalSpawnExpected);
                    },
                    waveIndex: 1,
                    skipDragonDescendant: skipDragon,
                    skipDragonKing: skipKing
                );
            }
            catch (Exception e)
            {
                if (reservedDragonDescendantSlot)
                {
                    modeEDragonDescendantSpawned = false;
                    DevLog("[ModeE] 龙裔遗族同步生成异常，回退全局标记");
                }

                ResolveModeESpawnAttemptIfCounted(spawnAttemptCounted);
                DevLog("[ModeE] [ERROR] SpawnSingleModeEBoss 失败: " + e.Message);
            }
        }

        private void SyncModeEDragonDescendantSpawnFlag(bool reservedDragonDescendantSlot, EnemyPresetInfo actualPreset, string modeTag)
        {
            bool actualIsDragonDescendant = IsDragonDescendantPreset(actualPreset);
            if (reservedDragonDescendantSlot && !actualIsDragonDescendant)
            {
                modeEDragonDescendantSpawned = false;
                DevLog("[" + modeTag + "] 龙裔候选在重试后替换为普通Boss，已回退龙裔占位标记");
                return;
            }

            if (actualIsDragonDescendant)
            {
                modeEDragonDescendantSpawned = true;
            }
        }

        /// <summary>
        /// Mode E 敌人生成成功后的回调：设置阵营、命名、AI配置、死亡注册
        /// 阵营来自 Boss 预设的原始 team，通过 SetTeam 确保运行时一致性
        /// </summary>
        /// <param name="promotedToBoss">是否为小怪被提升为 Boss（需要克隆预设设 showName=true）</param>
        private void OnModeEEnemySpawned(EnemySpawnContext ctx, Teams faction, bool promotedToBoss = false)
        {
            try
            {
                CharacterMainControl character = ctx.character;
                bool isModeFRun = modeFActive && !modeEActive;
                if (character == null)
                {
                    return;
                }

                Teams runtimeFaction = isModeFRun
                    ? ResolveModeFBossCombatTeam(faction, ctx.preset, ctx.position)
                    : faction;
                Teams trackedFaction = runtimeFaction;

                // 克隆 characterPreset 副本，避免修改原版 ScriptableObject
                // 1) 统一设置 aiCombatFactor=1，使 AI 互相攻击时伤害不被缩放
                // 2) 小怪提升为 Boss，或被 Mode F 复用时，额外设置 showName/showHealthBar
                if (character.characterPreset != null)
                {
                    CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(character.characterPreset);
                    // AI打AI伤害系数统一为1，确保各阵营Boss互殴时伤害公平
                    customPreset.aiCombatFactor = 1f;
                    if (promotedToBoss || isModeFRun)
                    {
                        customPreset.showName = true;
                        customPreset.showHealthBar = true;
                    }
                    character.characterPreset = customPreset;
                    DevLog("[ModeE] 已克隆预设并设置 aiCombatFactor=1"
                        + ((promotedToBoss || isModeFRun) ? ", showName=true" : "")
                        + ": " + ctx.preset.displayName);
                }

                if (isModeFRun && ctx.preset != null)
                {
                    SetModeFBossDisplayName(character, ctx.preset.displayName, runtimeFaction);
                }

                // 命名
                character.gameObject.name = (isModeFRun ? "ModeF_" : "ModeE_") + runtimeFaction + "_" + ctx.preset.displayName;

                // 设置阵营（确保运行时 team 与预设原始阵营一致）
                // 对于龙裔/龙王：CreateCharacterAsync 使用的是 Cname_Boss_Red 基础预设（team=scav），
                // 但 EnemyPresetInfo.team 记录的是 wolf，所以 SetTeam 是必要的
                character.SetTeam(runtimeFaction);
                DevLog("[ModeE] 敌人阵营已设置: " + ctx.preset.displayName + " -> " + runtimeFaction + " (预设team=" + ctx.preset.team + ")");

                // Mode E AI：不主动设置目标，让 AI 自然感知范围内的敌人后再开打
                // 不设置 forceTracePlayerDistance，不设置初始 searchedEnemy
                // 【修复】强制清零 forceTracePlayerDistance，阻止原版AI中硬编码的 Teams.player 追踪逻辑
                var ai = character.GetComponentInChildren<AICharacterController>();
                if (ai != null)
                {
                    ai.forceTracePlayerDistance = 0f;
                }

                if (!isModeFRun)
                {
                    // Mode E 基础血量提升：所有敌人血量 × 1.5（跳过玩家所属阵营）
                    ApplyModeEBaseHealthBoost(character);

                    // BEAR阵营属性提升：血量和伤害提升150%（× 2.5），补偿小怪基础数值偏低
                    if (faction == Teams.bear)
                    {
                        ApplyBearFactionStatBoost(character);
                    }
                }

                if (ctx.isBoss && faction != modeEPlayerFaction && !isModeFRun)
                {
                    ApplyModeDStyleLootToModeESpecialEnemy(character, ctx, faction, promotedToBoss);
                }

                // 先清理该实例可能残留的旧注册，再以当前 Mode E 生命周期重新登记。
                CleanupModeEEnemyRuntimeState(character);
                ModeEEnemyScalingState scalingState = new ModeEEnemyScalingState();
                scalingState.deathBaseline = GetModeEFactionDeathCount(trackedFaction);
                modeEEnemyScalingStates[character] = scalingState;
                TrackModeEAliveEnemy(character, trackedFaction);
                RegisterEnemyRecoveryAnchor(character, ctx.position);

                // 注册到虚拟 CharacterSpawnerRoot，使 BossLiveMapMod 能检测到
                RegisterModeEEnemyToSpawnerRoot(character);

                // 注册死亡事件
                RegisterModeEEnemyDeath(character);
                RegisterModeEEnemyLootHandler(character, trackedFaction);
                if (isModeFRun)
                {
                    RegisterModeFBoss(character);
                }

                // 更新生成计数
                ResolveModeESpawnAttempt();
                MarkModeEStartupBossSpawned();
                DevLog("[ModeE] 生成结案: resolved=" + modeESpawnResolved + "/" + modeETotalSpawnExpected);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] OnModeEEnemySpawned 失败: " + e.Message);
            }
        }

        private void ApplyModeDStyleLootToModeESpecialEnemy(CharacterMainControl character, EnemySpawnContext ctx, Teams faction, bool promotedToBoss)
        {
            try
            {
                if (character == null || ctx == null || ctx.preset == null)
                {
                    return;
                }

                bool isDragonDescendant = IsDragonDescendantPreset(ctx.preset);
                bool isBearPromotedMinion = faction == Teams.bear && promotedToBoss;
                if (!isDragonDescendant && !isBearPromotedMinion)
                {
                    return;
                }

                InitializeModeDItemPools();
                EnsureModeDGlobalItemPool();

                float lootHealth = 100f;
                try
                {
                    if (character.Health != null)
                    {
                        lootHealth = character.Health.MaxHealth;
                    }
                    else if (ctx.preset.baseHealth > 0f)
                    {
                        lootHealth = ctx.preset.baseHealth;
                    }
                }
                catch {}

                int virtualWave = promotedToBoss ? 5 : 10;
                bool preserveBossArmor = !promotedToBoss;
                EquipEnemyForModeD(character, virtualWave, lootHealth, preserveBossArmor);

                DevLog("[ModeE] 已应用白手起家式随机掉落: " + character.gameObject.name
                    + " (dragonDescendant=" + isDragonDescendant
                    + ", bearPromotedMinion=" + isBearPromotedMinion
                    + ", virtualWave=" + virtualWave + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] 应用白手起家式随机掉落失败: " + e.Message);
            }
        }


        #endregion
    }
}

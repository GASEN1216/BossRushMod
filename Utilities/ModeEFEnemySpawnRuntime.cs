using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    // E/F share one preset cache, spawn accounting and reservation owner.
    internal sealed class ModeEFEnemySpawnRuntime
    {
        private readonly ModeEFSpawnPreparation spawnPreparation;
        private readonly EnemySpawnRuntime spawnCore;
        private Func<List<EnemyPresetInfo>> GetFilteredEnemyPresets;
        private Func<IReadOnlyList<EnemyPresetInfo>> getMinionPresets;
        private Func<EnemyPresetInfo, bool> IsDragonKingPreset;
        private Func<EnemyPresetInfo, bool> IsDragonDescendantPreset;
        private Action InitializeEnemyPresets;
        private Action InitializeModeDEnemyPools;
        private Func<int, int, int, int, bool> IsModeEOrModeFSpawnSessionStillValid;
        private Func<EnemySpawnContext, Teams, bool, bool> OnModeEEnemySpawned;

        internal ModeEFEnemySpawnRuntime(ModeEFSpawnPreparation spawnPreparation, EnemySpawnRuntime spawnCore)
        {
            this.spawnPreparation = spawnPreparation;
            this.spawnCore = spawnCore;
        }

        internal void BindPresetQueries(Func<List<EnemyPresetInfo>> filteredBosses,
            Func<IReadOnlyList<EnemyPresetInfo>> minions, Func<EnemyPresetInfo, bool> isDragonKing,
            Func<EnemyPresetInfo, bool> isDragonDescendant, Action initializeEnemyPresets,
            Action initializeEnemyPools)
        {
            GetFilteredEnemyPresets = filteredBosses;
            getMinionPresets = minions;
            IsDragonKingPreset = isDragonKing;
            IsDragonDescendantPreset = isDragonDescendant;
            InitializeEnemyPresets = initializeEnemyPresets;
            InitializeModeDEnemyPools = initializeEnemyPools;
        }

        internal void BindSpawnCallbacks(Func<int, int, int, int, bool> isSessionValid,
            Func<EnemySpawnContext, Teams, bool, bool> onSpawned)
        {
            IsModeEOrModeFSpawnSessionStillValid = isSessionValid;
            OnModeEEnemySpawned = onSpawned;
        }

        private IReadOnlyList<EnemyPresetInfo> MinionPresets { get { return getMinionPresets(); } }
        private Dictionary<Teams, List<Vector3>> modeESpawnAllocation { get { return spawnPreparation.SpawnAllocation; } }
        private Vector3[] GetModeEFlattenedSpawnPoints() { return spawnPreparation.GetModeEFlattenedSpawnPoints(); }
        internal int TotalSpawnExpected { get { return modeETotalSpawnExpected; } }
        internal int SpawnResolved { get { return modeESpawnResolved; } }
        internal bool DragonDescendantSpawned { get { return modeEDragonDescendantSpawned; } set { modeEDragonDescendantSpawned = value; } }
        internal bool DragonKingSpawned { get { return modeEDragonKingSpawned; } set { modeEDragonKingSpawned = value; } }
        internal bool ContainsBossPreset(EnemyPresetInfo preset) { return ModeEPresetMapContains(modeEBossPoolByFaction, preset); }
        internal bool ContainsMinionPreset(EnemyPresetInfo preset) { return ModeEPresetMapContains(modeEMinionPoolByFaction, preset); }

        internal void ResetSpawnTracking()
        {
            modeETotalSpawnExpected = 0;
            modeESpawnResolved = 0;
            modeEDragonDescendantSpawned = false;
            modeEDragonKingSpawned = false;
            modeEWolfBossCount = 0;
            modeEWolfBossAssigned = 0;
        }

        #region Mode E 战斗管理字段

        /// <summary>预期生成的敌人总数</summary>
        private int modeETotalSpawnExpected = 0;

        /// <summary>已完成生成（成功或失败）的计数</summary>
        private int modeESpawnResolved = 0;

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

        internal void BuildModeEFactionPresetCaches()
        {
            List<EnemyPresetInfo> filteredBossPool = GetFilteredEnemyPresets();
            int bossPoolCount = filteredBossPool != null ? filteredBossPool.Count : 0;
            int minionPoolCount = MinionPresets != null ? MinionPresets.Count : 0;
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

            if (MinionPresets != null)
            {
                for (int i = 0; i < MinionPresets.Count; i++)
                {
                    EnemyPresetInfo minion = MinionPresets[i];
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

        internal void EnsureModeEFSpawnPoolsReady(string sourceTag)
        {
            try
            {
                InitializeEnemyPresets();
                InitializeModeDEnemyPools();
                BuildModeEFactionPresetCaches();

                if (ModBehaviour.VerboseStartupDebugLogsEnabled)
                {
                    ModBehaviour.DevLog("[ModeE/F] 生成池缓存已就绪: " + sourceTag);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/F] [WARNING] 生成池缓存预热失败(" + sourceTag + "): " + e.Message);
            }
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

        private static bool ModeEPresetIdentityMatches(EnemyPresetInfo left, EnemyPresetInfo right)
        {
            if (object.ReferenceEquals(left, right)) return true;
            return left != null && right != null &&
                   !string.IsNullOrEmpty(left.name) &&
                   string.Equals(left.name, right.name, StringComparison.Ordinal);
        }

        private static bool ModeEPresetMapContains(
            Dictionary<Teams, List<EnemyPresetInfo>> map,
            EnemyPresetInfo preset)
        {
            if (map == null || preset == null) return false;
            foreach (KeyValuePair<Teams, List<EnemyPresetInfo>> pair in map)
            {
                List<EnemyPresetInfo> list = pair.Value;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    if (ModeEPresetIdentityMatches(list[i], preset)) return true;
                }
            }
            return false;
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
                    ModBehaviour.DevLog("[ModeE] ModeESpawnAllBosses: no active map mode or spawn allocation");
                    return;
                }

                ModBehaviour.DevLog("[ModeE] 开始分批生成所有阵营 Boss...");
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
                    ModBehaviour.DevLog("[ModeE] 狼阵营可用 Boss 预设数量: " + modeEWolfBossCount);
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

                ModBehaviour.DevLog("[ModeE] Boss 生成任务已全部下发，预期总数: " + modeETotalSpawnExpected);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ModeESpawnAllBosses 失败: " + e.Message);
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

        internal void ResolveModeESpawnAttempt()
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
        internal void SpawnSingleModeEBoss(
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
                            ModBehaviour.DevLog("[ModeE] 狼阵营出Boss (" + modeEWolfBossAssigned + "/" + modeEWolfBossCount + "): " + bossPreset.displayName);
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
                            ModBehaviour.DevLog("[ModeE] 狼阵营Boss已刷完，出小怪(提升为Boss): " + bossPreset.displayName);
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
                    ModBehaviour.DevLog("[ModeE] 阵营 " + faction + " 无匹配Boss，尝试小怪池");
                    bossPreset = GetMinionPresetForFaction(faction);
                    if (bossPreset != null)
                    {
                        isMinionPromotedToBoss = true;
                    }
                }

                // BEAR阵营兜底：原版游戏无 bear 预设，从全阵营小怪池随机抽取
                if (bossPreset == null && faction == Teams.bear)
                {
                    ModBehaviour.DevLog("[ModeE] bear 阵营无匹配预设，从全阵营小怪池兜底");
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
                    ModBehaviour.DevLog("[ModeE] [WARNING] 阵营 " + faction + " 无任何匹配预设（Boss池+小怪池均为空），跳过该刷怪点");
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
                if (spawnDistSqr < (SpawnPositionHelper.DefaultSafeDistance * SpawnPositionHelper.DefaultSafeDistance))
                {
                    // 先尝试本阵营的刷怪点
                    List<Vector3> factionPoints;
                    if (modeESpawnAllocation != null && modeESpawnAllocation.TryGetValue(faction, out factionPoints) && factionPoints.Count > 0)
                    {
                        spawnPos = SpawnPositionHelper.FindNearestSafeSpawnPoint(factionPoints, modeEPlayerPos);
                    }
                    else
                    {
                        // 本阵营无可用点，回退到所有刷怪点
                        Vector3[] allModeEPoints = GetModeEFlattenedSpawnPoints();
                        spawnPos = SpawnPositionHelper.FindNearestSafeSpawnPoint(allModeEPoints, modeEPlayerPos);
                    }
                }
                else
                {
                    spawnPos = SpawnPositionHelper.SnapToGround(spawnPoint);
                }

                if (countSpawnAttemptImmediately)
                {
                    modeETotalSpawnExpected++;
                    spawnAttemptCounted = true;
                }

                Teams capturedFaction = faction;

                // skipDragonDescendant：防止 spawnCore.SpawnEnemyCore 重试时意外生成额外的龙裔
                // skipDragonKing：Mode E 完全排除龙皇，始终跳过
                bool skipDragon = !isThisDragonDescendant;
                bool skipKing = true; // Mode E 完全排除龙皇

                // 捕获龙裔标记，用于生成失败时回退
                bool capturedIsDD = isThisDragonDescendant;

                ModBehaviour.DevLog("[ModeE] 阵营 " + faction + " 生成: " + bossPreset.displayName + " (预设team=" + bossPreset.team + ", isBoss=" + isBoss + ", promoted=" + isMinionPromotedToBoss + ")");

                // 捕获小怪提升标记，传递给生成回调
                bool capturedPromoted = isMinionPromotedToBoss;

                spawnCore.SpawnEnemyCore(
                    bossPreset,
                    spawnPos,
                    isBoss,
                    isActiveCheck: () => IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene),
                    onSpawned: null,
                    onFailed: () =>
                    {
                        if (!IsModeEOrModeFSpawnSessionStillValid(modeFSessionToken, modeFRelatedScene,
                            modeESessionToken, modeESessionRelatedScene)) return;
                        // 龙裔生成失败时回退全局标记，允许后续刷怪点再次尝试
                        if (capturedIsDD)
                        {
                            modeEDragonDescendantSpawned = false;
                            ModBehaviour.DevLog("[ModeE] 龙裔遗族生成失败，回退全局标记");
                        }

                        ResolveModeESpawnAttempt();
                        ModBehaviour.DevLog("[ModeE] 生成失败结案: resolved=" + modeESpawnResolved + "/" + modeETotalSpawnExpected);
                    },
                    waveIndex: 1,
                    skipDragonDescendant: skipDragon,
                    skipDragonKing: skipKing,
                    deferActivationUntilNextFrame: true,
                    onCommit: (ctx) =>
                    {
                        SyncModeEDragonDescendantSpawnFlag(capturedIsDD, ctx != null ? ctx.preset : null, "ModeE");
                        return OnModeEEnemySpawned(ctx, capturedFaction, capturedPromoted);
                    }
                );
            }
            catch (Exception e)
            {
                if (reservedDragonDescendantSlot)
                {
                    modeEDragonDescendantSpawned = false;
                    ModBehaviour.DevLog("[ModeE] 龙裔遗族同步生成异常，回退全局标记");
                }

                ResolveModeESpawnAttemptIfCounted(spawnAttemptCounted);
                ModBehaviour.DevLog("[ModeE] [ERROR] SpawnSingleModeEBoss 失败: " + e.Message);
            }
        }

        internal void SyncModeEDragonDescendantSpawnFlag(bool reservedDragonDescendantSlot, EnemyPresetInfo actualPreset, string modeTag)
        {
            bool actualIsDragonDescendant = IsDragonDescendantPreset(actualPreset);
            if (reservedDragonDescendantSlot && !actualIsDragonDescendant)
            {
                modeEDragonDescendantSpawned = false;
                ModBehaviour.DevLog("[" + modeTag + "] 龙裔候选在重试后替换为普通Boss，已回退龙裔占位标记");
                return;
            }

            if (actualIsDragonDescendant)
            {
                modeEDragonDescendantSpawned = true;
            }
        }

        /// <summary>
        /// 从全阵营小怪池随机抽取一个预设（不限阵营过滤）
        /// 用于 bear 阵营兜底（原版游戏无 bear 预设）
        /// </summary>
        private EnemyPresetInfo GetAllFactionMinionPreset()
        {
            if (MinionPresets == null || MinionPresets.Count == 0) return null;
            return MinionPresets[UnityEngine.Random.Range(0, MinionPresets.Count)];
        }

        #endregion
    }
}

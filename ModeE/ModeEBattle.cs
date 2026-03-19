// ============================================================================
// ModeEBattle.cs - Boss 生成与动态难度缩放
// ============================================================================
// 模块说明：
//   负责 Mode E 的 Boss 一次性生成、敌人死亡追踪和按阵营动态难度缩放。
//   敌人生成复用 SpawnEnemyCore 通用方法（支持龙裔遗族和龙王）。
//
// 动态缩放规则：
//   - 每当某阵营有单位阵亡，该阵营所有存活单位属性提升 5%
//   - 倍率 = 1.0 + 该阵营已死亡单位数 × 0.05
//   - 各阵营独立计算，互不影响
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Cysharp.Threading.Tasks;

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

        #if false
        private void ApplyModeDStyleLootToModeESpecialEnemy_Temp(CharacterMainControl character, EnemySpawnContext ctx, Teams faction, bool promotedToBoss)
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

        #endif
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
            int bossPoolCount = modeDBossPool != null ? modeDBossPool.Count : 0;
            int minionPoolCount = modeDMinionPool != null ? modeDMinionPool.Count : 0;
            if (modeEFactionPresetCachesBuilt &&
                object.ReferenceEquals(modeECachedBossPoolSource, modeDBossPool) &&
                modeECachedBossPoolCount == bossPoolCount &&
                modeECachedMinionPoolCount == minionPoolCount)
            {
                return;
            }

            modeEBossPoolByFaction.Clear();
            modeEBossPoolByFactionWithoutDragonDescendant.Clear();
            modeEMinionPoolByFaction.Clear();
            modeEWeightedMinionTotalHealthByFaction.Clear();

            if (modeDBossPool != null)
            {
                for (int i = 0; i < modeDBossPool.Count; i++)
                {
                    EnemyPresetInfo boss = modeDBossPool[i];
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

            modeECachedBossPoolSource = modeDBossPool;
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
                        modeESessionRelatedScene);

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
            int modeESessionRelatedScene = -1)
        {
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
                    return;
                }

                // 记录龙裔标记（龙皇在 Mode E 中已被完全排除，无需追踪）
                isThisDragonDescendant = IsDragonDescendantPreset(bossPreset);
                if (isThisDragonDescendant) modeEDragonDescendantSpawned = true;

                Vector3 spawnPos = GetSafeBossSpawnPosition(spawnPoint);
                modeETotalSpawnExpected++;

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
                    },
                    waveIndex: 1,
                    skipDragonDescendant: skipDragon,
                    skipDragonKing: skipKing
                );
            }
            catch (Exception e)
            {
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

                // 命名
                character.gameObject.name = (isModeFRun ? "ModeF_" : "ModeE_") + faction + "_" + ctx.preset.displayName;

                // 设置阵营（确保运行时 team 与预设原始阵营一致）
                // 对于龙裔/龙王：CreateCharacterAsync 使用的是 Cname_Boss_Red 基础预设（team=scav），
                // 但 EnemyPresetInfo.team 记录的是 wolf，所以 SetTeam 是必要的
                character.SetTeam(faction);
                DevLog("[ModeE] 敌人阵营已设置: " + ctx.preset.displayName + " -> " + faction + " (预设team=" + ctx.preset.team + ")");

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

                if (!isModeFRun && ctx.isBoss && faction != modeEPlayerFaction)
                {
                    ApplyModeDStyleLootToModeESpecialEnemy(character, ctx, faction, promotedToBoss);
                }

                // 先清理该实例可能残留的旧注册，再以当前 Mode E 生命周期重新登记。
                CleanupModeEEnemyRuntimeState(character);
                TrackModeEAliveEnemy(character, faction);
                RegisterEnemyRecoveryAnchor(character, ctx.position);

                // 注册到虚拟 CharacterSpawnerRoot，使 BossLiveMapMod 能检测到
                RegisterModeEEnemyToSpawnerRoot(character);

                // 注册死亡事件
                RegisterModeEEnemyDeath(character);
                RegisterModeEEnemyLootHandler(character, faction);
                if (isModeFRun)
                {
                    RegisterModeFBoss(character);
                }

                // 更新生成计数
                modeESpawnResolved++;
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

        #region Mode E 敌人死亡与动态缩放

        /// <summary>
        /// 缓存每个敌人上一次应用的缩放 Modifier，用于移除旧值避免累积叠加
        /// Key = CharacterMainControl 实例, Value = (HP Modifier, GunDmg Modifier, MeleeDmg Modifier)
        /// </summary>
        private readonly Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg, Modifier meleeDmg)> modeEScalingModifiers
            = new Dictionary<CharacterMainControl, (Modifier, Modifier, Modifier)>();

        /// <summary>缓存死亡事件句柄，避免对象复用或重复注册导致 UnityEvent 持续膨胀。</summary>
        private readonly Dictionary<CharacterMainControl, UnityAction<DamageInfo>> modeEEnemyDeathHandlers
            = new Dictionary<CharacterMainControl, UnityAction<DamageInfo>>();

        /// <summary>缓存掉落拦截句柄，确保 Mode E 结束或对象复用时可以对称取消订阅。</summary>
        private readonly Dictionary<CharacterMainControl, Action<DamageInfo>> modeEEnemyLootHandlers
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();

        /// <summary>需要延迟批量缩放的阵营集合（死亡时记录，定时批量应用）</summary>
        private readonly HashSet<Teams> modeEPendingScalingFactions = new HashSet<Teams>();

        /// <summary>缩放批量应用计时器</summary>
        private float modeEScalingBatchTimer = 0f;

        /// <summary>缩放批量应用间隔（秒）- 每 5 秒统一应用一次，避免连锁死亡时的帧率尖刺</summary>
        private const float MODE_E_SCALING_BATCH_INTERVAL = 5f;

        /// <summary>
        /// 注册敌人死亡事件，触发按阵营的动态缩放
        /// </summary>
        private void RegisterModeEEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                if (enemy == null)
                {
                    return;
                }

                UnregisterModeEEnemyDeath(enemy);

                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    CharacterMainControl capturedEnemy = enemy;
                    UnityAction<DamageInfo> handler = null;
                    handler = (dmgInfo) =>
                    {
                        UnregisterModeEEnemyDeath(capturedEnemy);
                        OnModeEEnemyDeath(capturedEnemy);
                    };
                    modeEEnemyDeathHandlers[enemy] = handler;
                    health.OnDeadEvent.AddListener(handler);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] RegisterModeEEnemyDeath 失败: " + e.Message);
            }
        }

        private void UnregisterModeEEnemyDeath(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            UnityAction<DamageInfo> handler;
            if (!modeEEnemyDeathHandlers.TryGetValue(enemy, out handler))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    Health health = enemy.GetComponent<Health>();
                    if (health != null)
                    {
                        health.OnDeadEvent.RemoveListener(handler);
                    }
                }
            }
            catch { }

            modeEEnemyDeathHandlers.Remove(enemy);
        }

        private void RegisterModeEEnemyLootHandler(CharacterMainControl enemy, Teams faction)
        {
            try
            {
                if (enemy == null)
                {
                    return;
                }

                UnregisterModeEEnemyLootHandler(enemy);

                CharacterMainControl capturedEnemy = enemy;
                Teams capturedFaction = faction;
                Action<DamageInfo> handler = (dmgInfo) =>
                {
                    if (!modeEActive || capturedFaction != modeEPlayerFaction || capturedEnemy == null)
                    {
                        return;
                    }

                    capturedEnemy.dropBoxOnDead = false;
                    DevLog("[ModeE] 同阵营Boss死亡，阻止掉落战利品箱子: " + capturedEnemy.gameObject.name);
                };

                modeEEnemyLootHandlers[enemy] = handler;
                enemy.BeforeCharacterSpawnLootOnDead += handler;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] RegisterModeEEnemyLootHandler 失败: " + e.Message);
            }
        }

        private void UnregisterModeEEnemyLootHandler(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            Action<DamageInfo> handler;
            if (!modeEEnemyLootHandlers.TryGetValue(enemy, out handler))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    enemy.BeforeCharacterSpawnLootOnDead -= handler;
                }
            }
            catch { }

            modeEEnemyLootHandlers.Remove(enemy);
        }

        private void RemoveModeEScalingModifiers(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            (Modifier hp, Modifier gunDmg, Modifier meleeDmg) oldMods;
            if (!modeEScalingModifiers.TryGetValue(enemy, out oldMods))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    Item characterItem = enemy.CharacterItem;
                    if (characterItem != null)
                    {
                        try
                        {
                            Stat oldHpStat = characterItem.GetStat("MaxHealth");
                            if (oldHpStat != null && oldMods.hp != null) oldHpStat.RemoveModifier(oldMods.hp);
                        }
                        catch { }

                        try
                        {
                            Stat oldGunStat = characterItem.GetStat("GunDamageMultiplier");
                            if (oldGunStat != null && oldMods.gunDmg != null) oldGunStat.RemoveModifier(oldMods.gunDmg);
                        }
                        catch { }

                        try
                        {
                            Stat oldMeleeStat = characterItem.GetStat("MeleeDamageMultiplier");
                            if (oldMeleeStat != null && oldMods.meleeDmg != null) oldMeleeStat.RemoveModifier(oldMods.meleeDmg);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            modeEScalingModifiers.Remove(enemy);
        }

        private void CleanupModeEEnemyRuntimeState(CharacterMainControl enemy, Teams? faction = null)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            UnregisterModeEEnemyDeath(enemy);
            UnregisterModeEEnemyLootHandler(enemy);
            UnregisterModeEEnemyFromSpawnerRoot(enemy);
            UnregisterEnemyRecovery(enemy);
            modeEPendingAggroTraceDistance.Remove(enemy);
            RemoveModeEScalingModifiers(enemy);
            UntrackModeEAliveEnemy(enemy, faction);
        }

        /// <summary>
        /// Mode E 敌人死亡回调
        /// [性能优化] 死亡时只记录计数和标记脏阵营，不立即遍历应用缩放
        /// 缩放由 ModeEScalingBatchUpdate() 定时批量执行
        /// </summary>
        private void OnModeEEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                if (!modeEActive) return;

                // 获取死亡敌人的阵营
                Teams enemyFaction = enemy.Team;

                // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
                try
                {
                    if (enemy.characterPreset != null)
                    {
                        UnityEngine.Object.Destroy(enemy.characterPreset);
                    }
                }
                catch { }

                // 从所有运行时注册表移除，避免事件/列表/虚拟 spawner root 持续膨胀
                CleanupModeEEnemyRuntimeState(enemy, enemyFaction);

                // 递增该阵营死亡计数
                if (modeEFactionDeathCount.ContainsKey(enemyFaction))
                {
                    modeEFactionDeathCount[enemyFaction]++;
                }
                else
                {
                    modeEFactionDeathCount[enemyFaction] = 1;
                }

                int deathCount = modeEFactionDeathCount[enemyFaction];
                DevLog("[ModeE] 阵营 " + enemyFaction + " 单位阵亡，累计死亡: " + deathCount);

                // 标记该阵营需要延迟缩放（不立即执行，等批量定时器触发）
                modeEPendingScalingFactions.Add(enemyFaction);

                // 累计击杀计数，每10次自动发放挑衅烟雾弹
                CheckRespawnItemAutoGrant();
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] OnModeEEnemyDeath 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode E 缩放批量更新（由 Update 定时调用）
        /// 将累积的阵营死亡缩放一次性批量应用，避免每次死亡都遍历全列表
        /// </summary>
        public void ModeEScalingBatchUpdate()
        {
            if (!modeEActive) return;

            modeEScalingBatchTimer += Time.deltaTime;
            if (modeEScalingBatchTimer < MODE_E_SCALING_BATCH_INTERVAL) return;
            modeEScalingBatchTimer = 0f;

            if (modeEPendingScalingFactions.Count == 0) return;

            // 批量应用所有待处理阵营的缩放
            foreach (Teams faction in modeEPendingScalingFactions)
            {
                ApplyFactionDeathScaling(faction);
            }
            modeEPendingScalingFactions.Clear();
        }

        /// <summary>
        /// 对指定阵营的所有存活单位应用属性缩放（生命值 + 伤害）
        /// 倍率 = 1.0 + 该阵营已死亡单位数 × 0.05
        /// 每次先移除旧 Modifier 再添加新的，避免累积叠加
        /// [P4性能优化] 使用阵营独立存活列表，只遍历同阵营单位，避免全量遍历
        /// </summary>
        private void ApplyFactionDeathScaling(Teams faction)
        {
            try
            {
                float multiplier = GetFactionScaleMultiplier(faction);
                DevLog("[ModeE] 应用阵营缩放: " + faction + " 倍率=" + multiplier);

                // [P4] 使用阵营独立列表，只遍历该阵营的存活单位
                List<CharacterMainControl> factionList = GetFactionAliveList(faction);
                if (factionList == null || factionList.Count == 0) return;

                for (int i = factionList.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = factionList[i];
                    if (enemy == null || enemy.gameObject == null)
                    {
                        // 清理无效引用
                        factionList.RemoveAt(i);
                        CleanupModeEEnemyRuntimeState(enemy, faction);
                        continue;
                    }

                    try
                    {
                        var characterItem = enemy.CharacterItem;
                        if (characterItem == null) continue;

                        // 移除该敌人上一次的缩放 Modifier
                        RemoveModeEScalingModifiers(enemy);

                        // 计算新的增量（基于 BaseValue，不受 Modifier 影响）
                        Modifier newHpMod = null;
                        Modifier newGunMod = null;
                        Modifier newMeleeMod = null;

                        // 生命值缩放
                        Stat maxHealthStat = characterItem.GetStat("MaxHealth");
                        if (maxHealthStat != null)
                        {
                            float hpDelta = maxHealthStat.BaseValue * (multiplier - 1.0f);
                            if (hpDelta > 0)
                            {
                                // 记录旧上限，用于计算本次新增量
                                float oldMaxHp = maxHealthStat.Value;

                                newHpMod = new Modifier(ModifierType.Add, hpDelta, this);
                                maxHealthStat.AddModifier(newHpMod);

                                // 当前血量 += 新增的上限部分（提升多少上限就回多少血）
                                Health health = enemy.Health;
                                if (health != null)
                                {
                                    float actualIncrease = maxHealthStat.Value - oldMaxHp;
                                    health.CurrentHealth = Mathf.Min(health.CurrentHealth + actualIncrease, maxHealthStat.Value);
                                }
                            }
                        }

                        // 枪械伤害缩放
                        Stat gunDmgStat = characterItem.GetStat("GunDamageMultiplier");
                        if (gunDmgStat != null)
                        {
                            float gunDelta = gunDmgStat.BaseValue * (multiplier - 1.0f);
                            if (gunDelta > 0)
                            {
                                newGunMod = new Modifier(ModifierType.Add, gunDelta, this);
                                gunDmgStat.AddModifier(newGunMod);
                            }
                        }

                        // 近战伤害缩放
                        Stat meleeDmgStat = characterItem.GetStat("MeleeDamageMultiplier");
                        if (meleeDmgStat != null)
                        {
                            float meleeDelta = meleeDmgStat.BaseValue * (multiplier - 1.0f);
                            if (meleeDelta > 0)
                            {
                                newMeleeMod = new Modifier(ModifierType.Add, meleeDelta, this);
                                meleeDmgStat.AddModifier(newMeleeMod);
                            }
                        }

                        // 缓存新的 Modifier 引用
                        modeEScalingModifiers[enemy] = (newHpMod, newGunMod, newMeleeMod);
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ApplyFactionDeathScaling 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取指定阵营的当前缩放倍率
        /// </summary>
        private float GetFactionScaleMultiplier(Teams faction)
        {
            int deathCount = 0;
            if (modeEFactionDeathCount.ContainsKey(faction))
            {
                deathCount = modeEFactionDeathCount[faction];
            }
            return 1.0f + deathCount * 0.05f;
        }

        #endregion

        #region Mode E BossLiveMapMod 集成

        /// <summary>
        /// 获取或创建 Mode E 专用的虚拟 CharacterSpawnerRoot
        /// BossLiveMapMod 通过遍历 CharacterSpawnerRoot.CreatedCharacters 来发现敌人，
        /// Mode E 的敌人通过 SpawnEnemyCore 直接生成，不经过游戏原版 spawner 系统，
        /// 因此需要创建一个虚拟的 CharacterSpawnerRoot 来注册这些敌人
        /// </summary>
        private CharacterSpawnerRoot GetOrCreateModeESpawnerRoot()
        {
            if (modeEVirtualSpawnerRoot != null) return modeEVirtualSpawnerRoot;

            try
            {
                GameObject spawnerObj = new GameObject("ModeE_VirtualSpawnerRoot");
                UnityEngine.Object.DontDestroyOnLoad(spawnerObj);
                modeEVirtualSpawnerRoot = spawnerObj.AddComponent<CharacterSpawnerRoot>();
                // This virtual root is only a registry bridge; keep Update/Init from entering the vanilla spawn pipeline.
                modeEVirtualSpawnerRoot.enabled = false;
                DevLog("[ModeE] 创建虚拟 CharacterSpawnerRoot 用于 BossLiveMapMod 集成");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] 创建虚拟 CharacterSpawnerRoot 失败: " + e.Message);
            }

            return modeEVirtualSpawnerRoot;
        }

        private System.Collections.IList GetModeESpawnerRootCreatedCharactersList()
        {
            if (modeEVirtualSpawnerRoot == null)
            {
                return null;
            }

            try
            {
                if (!modeESpawnerRootCreatedCharactersAccessorCached)
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    Type rootType = typeof(CharacterSpawnerRoot);

                    modeESpawnerRootCreatedCharactersField =
                        rootType.GetField("CreatedCharacters", flags) ??
                        rootType.GetField("createdCharacters", flags);

                    if (modeESpawnerRootCreatedCharactersField == null)
                    {
                        modeESpawnerRootCreatedCharactersProperty =
                            rootType.GetProperty("CreatedCharacters", flags) ??
                            rootType.GetProperty("createdCharacters", flags);
                    }

                    modeESpawnerRootCreatedCharactersAccessorCached = true;

                    if (modeESpawnerRootCreatedCharactersField == null &&
                        modeESpawnerRootCreatedCharactersProperty == null &&
                        !modeESpawnerRootCreatedCharactersAccessorMissingLogged)
                    {
                        modeESpawnerRootCreatedCharactersAccessorMissingLogged = true;
                        DevLog("[ModeE] [WARNING] 未找到虚拟 SpawnerRoot 的 createdCharacters/CreatedCharacters 访问器");
                    }
                }

                if (modeESpawnerRootCreatedCharactersField != null)
                {
                    return modeESpawnerRootCreatedCharactersField.GetValue(modeEVirtualSpawnerRoot) as System.Collections.IList;
                }

                if (modeESpawnerRootCreatedCharactersProperty != null)
                {
                    return modeESpawnerRootCreatedCharactersProperty.GetValue(modeEVirtualSpawnerRoot, null) as System.Collections.IList;
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 获取虚拟 SpawnerRoot CreatedCharacters 失败: " + e.Message);
            }

            return null;
        }

        private void RemoveModeEEnemyFromSpawnerRootList(CharacterMainControl character)
        {
            try
            {
                System.Collections.IList list = GetModeESpawnerRootCreatedCharactersList();
                if (list == null)
                {
                    return;
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    object entry = list[i];
                    if (entry == null)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    CharacterMainControl existing = entry as CharacterMainControl;
                    if (existing != null && object.ReferenceEquals(existing, character))
                    {
                        list.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 从虚拟 CharacterSpawnerRoot 中移除敌人，防止 CreatedCharacters 列表无限膨胀
        /// 通过反射获取 CreatedCharacters 列表（无公开 Remove API）
        /// </summary>
        private void UnregisterModeEEnemyFromSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                modeESpawnerRootRegisteredEnemies.Remove(character);

                if (modeEVirtualSpawnerRoot == null) return;

                RemoveModeEEnemyFromSpawnerRootList(character);
            }
            catch { }
        }

        /// <summary>
        /// 将 Mode E 生成的敌人注册到虚拟 CharacterSpawnerRoot，
        /// 使 BossLiveMapMod 能通过标准流程检测到这些敌人
        /// </summary>
        private void RegisterModeEEnemyToSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                if (character == null)
                {
                    return;
                }

                CharacterSpawnerRoot root = GetOrCreateModeESpawnerRoot();
                if (root != null)
                {
                    RemoveModeEEnemyFromSpawnerRootList(character);
                    modeESpawnerRootRegisteredEnemies.Add(character);
                    root.AddCreatedCharacter(character);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] RegisterModeEEnemyToSpawnerRoot 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理 Mode E 虚拟 CharacterSpawnerRoot
        /// </summary>
        private void CleanupModeEVirtualSpawnerRoot()
        {
            try
            {
                if (modeEVirtualSpawnerRoot != null)
                {
                    try
                    {
                        System.Collections.IList list = GetModeESpawnerRootCreatedCharactersList();
                        if (list != null)
                        {
                            list.Clear();
                        }
                    }
                    catch { }

                    if (modeEVirtualSpawnerRoot.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(modeEVirtualSpawnerRoot.gameObject);
                    }
                    modeEVirtualSpawnerRoot = null;
                    modeESpawnerRootRegisteredEnemies.Clear();
                    DevLog("[ModeE] 已清理虚拟 CharacterSpawnerRoot");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] CleanupModeEVirtualSpawnerRoot 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 基础血量提升

        /// <summary>
        /// Mode E 基础血量提升：非玩家阵营敌人血量 × 1.5
        /// 在敌人生成时一次性应用，不影响后续阵营死亡缩放机制
        /// 玩家所属阵营不应用（bear 阵营由 ApplyBearFactionStatBoost 独立处理）
        /// </summary>
        private void ApplyModeEBaseHealthBoost(CharacterMainControl character)
        {
            try
            {
                // 玩家所属阵营不应用血量提升（避免友方Boss被额外加强）
                if (character.Team == modeEPlayerFaction) return;

                ApplyStatBoostPercent(character, "MaxHealth", 0.5f, true);
                DevLog("[ModeE] 基础血量提升: " + character.gameObject.name + " HP × 1.5");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ApplyModeEBaseHealthBoost 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E BEAR阵营兜底

        /// <summary>
        /// 从全阵营小怪池随机抽取一个预设（不限阵营过滤）
        /// 用于 bear 阵营兜底（原版游戏无 bear 预设）
        /// </summary>
        private EnemyPresetInfo GetAllFactionMinionPreset()
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0) return null;
            return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
        }

        /// <summary>
        /// BEAR阵营专属属性提升：血量和伤害提升150%（最终为原始值的 2.5 倍）
        /// 补偿小怪基础数值偏低，使其达到 Boss 级强度
        /// </summary>
        private void ApplyBearFactionStatBoost(CharacterMainControl character)
        {
            try
            {
                ApplyStatBoostPercent(character, "MaxHealth", 1.5f, true);
                ApplyStatBoostPercent(character, "GunDamageMultiplier", 1.5f, false);
                ApplyStatBoostPercent(character, "MeleeDamageMultiplier", 1.5f, false);
                DevLog("[ModeE] BEAR阵营属性提升: " + character.gameObject.name + " HP/Dmg × 2.5");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ApplyBearFactionStatBoost 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 属性提升工具方法

        /// <summary>
        /// 通用属性百分比提升：给角色的指定 Stat 增加 (BaseValue × percent) 的加法 Modifier
        /// <para>例如 percent=0.5 表示提升50%（最终 BaseValue × 1.5），percent=1.5 表示提升150%（最终 BaseValue × 2.5）</para>
        /// </summary>
        /// <param name="syncHealth">如果为 true 且 statName 为 MaxHealth，则同步当前血量到新上限</param>
        private void ApplyStatBoostPercent(CharacterMainControl character, string statName, float percent, bool syncHealth)
        {
            try
            {
                var characterItem = character.CharacterItem;
                if (characterItem == null) return;

                Stat stat = characterItem.GetStat(statName);
                if (stat == null) return;

                float boostAmount = stat.BaseValue * percent;
                Modifier mod = new Modifier(ModifierType.Add, boostAmount, this);
                stat.AddModifier(mod);

                if (syncHealth)
                {
                    Health health = character.Health;
                    if (health != null)
                    {
                        health.CurrentHealth = stat.Value;
                    }
                }
            }
            catch {}
        }

        #endregion
    }
}

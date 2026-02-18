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
using UnityEngine;
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

        /// <summary>
        /// 在所有阵营的刷怪点一次性生成全部 Boss
        /// 按距离玩家由近到远分批生成，每批之间让出一帧，避免卡顿
        /// </summary>
        public async UniTaskVoid ModeESpawnAllBosses()
        {
            try
            {
                if (!modeEActive || modeESpawnAllocation == null)
                {
                    DevLog("[ModeE] ModeESpawnAllBosses: Mode E 未激活或刷怪点未分配");
                    return;
                }

                DevLog("[ModeE] 开始分批生成所有阵营 Boss...");

                // 重置生成计数
                modeETotalSpawnExpected = 0;
                modeESpawnResolved = 0;

                // 收集所有待生成任务（阵营 + 刷怪点），按距离玩家由近到远排序
                Vector3 playerPos = Vector3.zero;
                CharacterMainControl playerRef = CharacterMainControl.Main;
                if (playerRef != null) playerPos = playerRef.transform.position;

                var spawnTasks = new List<(Teams faction, Vector3 pos)>();
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
                if (modeDBossPool != null)
                {
                    int wolfTeam = (int)Teams.wolf;
                    for (int i = 0; i < modeDBossPool.Count; i++)
                    {
                        EnemyPresetInfo boss = modeDBossPool[i];
                        if (boss == null || string.IsNullOrEmpty(boss.name)) continue;
                        if (boss.team != wolfTeam) continue;
                        // 排除龙皇（Mode E 完全排除）
                        if (IsDragonKingPreset(boss)) continue;
                        modeEWolfBossCount++;
                    }
                    DevLog("[ModeE] 狼阵营可用 Boss 预设数量: " + modeEWolfBossCount);
                }

                // 分批生成：每个boss让出一帧，分散到多帧执行，避免开局卡顿
                for (int i = 0; i < spawnTasks.Count; i++)
                {
                    if (!modeEActive) break; // 模式已结束，停止生成

                    var task = spawnTasks[i];
                    SpawnSingleModeEBoss(task.faction, task.pos);

                    // 每个boss让出多帧，把生成压力分散到更长时间
                    if (i + 1 < spawnTasks.Count)
                    {
                        // 每个boss之间等待0.25秒，给角色创建和配装充足时间完成，减少帧率尖刺
                        await UniTask.Delay(250);
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
            if (modeDBossPool == null || modeDBossPool.Count == 0) return null;

            int targetTeam = (int)faction;

            // 使用复用缓存避免 GC
            presetFilterCache.Clear();
            for (int i = 0; i < modeDBossPool.Count; i++)
            {
                EnemyPresetInfo boss = modeDBossPool[i];
                if (boss == null || string.IsNullOrEmpty(boss.name)) continue;
                if (boss.team != targetTeam) continue;

                // 龙裔全局限制1个；龙皇在 Mode E 中完全排除
                if (IsDragonDescendantPreset(boss) && modeEDragonDescendantSpawned) continue;
                if (IsDragonKingPreset(boss)) continue;

                presetFilterCache.Add(boss);
            }

            if (presetFilterCache.Count == 0) return null;
            return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
        }

        /// <summary>
        /// 从小怪池中获取属于指定阵营的随机小怪预设（Boss 池不足时的兜底）
        /// </summary>
        private EnemyPresetInfo GetMinionPresetForFaction(Teams faction)
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0) return null;

            int targetTeam = (int)faction;

            presetFilterCache.Clear();
            for (int i = 0; i < modeDMinionPool.Count; i++)
            {
                EnemyPresetInfo minion = modeDMinionPool[i];
                if (minion == null || string.IsNullOrEmpty(minion.name)) continue;
                if (minion.team != targetTeam) continue;
                presetFilterCache.Add(minion);
            }

            if (presetFilterCache.Count == 0) return null;
            return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
        }

        /// <summary>
        /// 从小怪池中获取属于指定阵营的加权随机小怪预设（高血量优先但保持随机性）
        /// 权重 = baseHealth，血量越高被选中概率越大，但不保证每次都是最高血量的
        /// 用于狼阵营混入小怪时优先选择较强的小怪
        /// </summary>
        private EnemyPresetInfo GetWeightedMinionPresetForFaction(Teams faction)
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0) return null;

            int targetTeam = (int)faction;

            // 筛选该阵营的小怪
            presetFilterCache.Clear();
            for (int i = 0; i < modeDMinionPool.Count; i++)
            {
                EnemyPresetInfo minion = modeDMinionPool[i];
                if (minion == null || string.IsNullOrEmpty(minion.name)) continue;
                if (minion.team != targetTeam) continue;
                presetFilterCache.Add(minion);
            }

            if (presetFilterCache.Count == 0) return null;
            if (presetFilterCache.Count == 1) return presetFilterCache[0];

            // 按 baseHealth 加权随机：血量越高权重越大
            float totalWeight = 0f;
            for (int i = 0; i < presetFilterCache.Count; i++)
            {
                float w = Mathf.Max(presetFilterCache[i].baseHealth, 1f);
                totalWeight += w;
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < presetFilterCache.Count; i++)
            {
                cumulative += Mathf.Max(presetFilterCache[i].baseHealth, 1f);
                if (roll <= cumulative)
                {
                    return presetFilterCache[i];
                }
            }

            // 兜底（浮点精度问题时返回最后一个）
            return presetFilterCache[presetFilterCache.Count - 1];
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
        /// </summary>
        private void SpawnSingleModeEBoss(Teams faction, Vector3 spawnPoint)
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
                bool skipDragon = !isThisDragonDescendant && modeEDragonDescendantSpawned;
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
                    isActiveCheck: () => modeEActive,
                    onSpawned: (ctx) => OnModeEEnemySpawned(ctx, capturedFaction, capturedPromoted),
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

                // 小怪提升为 Boss：克隆 characterPreset 副本，设置 showName=true 使血条显示名字
                // 参考龙裔/龙王的做法：Instantiate 创建独立副本，避免修改原版 ScriptableObject
                if (promotedToBoss && character.characterPreset != null)
                {
                    CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(character.characterPreset);
                    customPreset.showName = true;
                    customPreset.showHealthBar = true;
                    character.characterPreset = customPreset;
                    DevLog("[ModeE] 小怪提升为Boss，已克隆预设并设置 showName=true: " + ctx.preset.displayName);
                }

                // 命名
                character.gameObject.name = "ModeE_" + faction + "_" + ctx.preset.displayName;

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

                // Mode E 基础血量提升：所有敌人血量 × 1.5
                ApplyModeEBaseHealthBoost(character);

                // 加入存活敌人列表
                modeEAliveEnemies.Add(character);

                // [P4] 加入阵营独立存活列表（缩放时只遍历同阵营列表，避免全量遍历）
                AddToFactionAliveList(faction, character);

                // 注册到虚拟 CharacterSpawnerRoot，使 BossLiveMapMod 能检测到
                RegisterModeEEnemyToSpawnerRoot(character);

                // 注册死亡事件
                RegisterModeEEnemyDeath(character);

                // 订阅掉落事件：同阵营 Boss 死亡时阻止掉落战利品箱子
                CharacterMainControl capturedChar = character;
                Teams capturedFac = faction;
                capturedChar.BeforeCharacterSpawnLootOnDead += (dmgInfo) =>
                {
                    // 同阵营的 Boss 死亡不掉落战利品箱子
                    if (modeEActive && capturedFac == modeEPlayerFaction)
                    {
                        capturedChar.dropBoxOnDead = false;
                        DevLog("[ModeE] 同阵营Boss死亡，阻止掉落战利品箱子: " + capturedChar.gameObject.name);
                    }
                };

                // 更新生成计数
                modeESpawnResolved++;
                DevLog("[ModeE] 生成结案: resolved=" + modeESpawnResolved + "/" + modeETotalSpawnExpected);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] OnModeEEnemySpawned 失败: " + e.Message);
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
                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    health.OnDeadEvent.AddListener((dmgInfo) => OnModeEEnemyDeath(enemy));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] RegisterModeEEnemyDeath 失败: " + e.Message);
            }
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

                // 从存活列表移除（全局 + 阵营独立列表）
                modeEAliveEnemies.Remove(enemy);
                RemoveFromFactionAliveList(enemyFaction, enemy);

                // 清理死亡敌人的 Modifier 缓存
                modeEScalingModifiers.Remove(enemy);

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
                        modeEAliveEnemies.Remove(enemy);
                        modeEScalingModifiers.Remove(enemy);
                        continue;
                    }

                    try
                    {
                        var characterItem = enemy.CharacterItem;
                        if (characterItem == null) continue;

                        // 移除该敌人上一次的缩放 Modifier
                        // [安全改进] 移除失败时清空引用，防止后续 AddModifier 导致属性只增不减
                        if (modeEScalingModifiers.TryGetValue(enemy, out var oldMods))
                        {
                            try
                            {
                                Stat oldHpStat = characterItem.GetStat("MaxHealth");
                                if (oldHpStat != null && oldMods.hp != null) oldHpStat.RemoveModifier(oldMods.hp);
                            }
                            catch { oldMods.hp = null; }
                            try
                            {
                                Stat oldGunStat = characterItem.GetStat("GunDamageMultiplier");
                                if (oldGunStat != null && oldMods.gunDmg != null) oldGunStat.RemoveModifier(oldMods.gunDmg);
                            }
                            catch { oldMods.gunDmg = null; }
                            try
                            {
                                Stat oldMeleeStat = characterItem.GetStat("MeleeDamageMultiplier");
                                if (oldMeleeStat != null && oldMods.meleeDmg != null) oldMeleeStat.RemoveModifier(oldMods.meleeDmg);
                            }
                            catch { oldMods.meleeDmg = null; }
                        }

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
                DevLog("[ModeE] 创建虚拟 CharacterSpawnerRoot 用于 BossLiveMapMod 集成");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] 创建虚拟 CharacterSpawnerRoot 失败: " + e.Message);
            }

            return modeEVirtualSpawnerRoot;
        }

        /// <summary>
        /// 将 Mode E 生成的敌人注册到虚拟 CharacterSpawnerRoot，
        /// 使 BossLiveMapMod 能通过标准流程检测到这些敌人
        /// </summary>
        private void RegisterModeEEnemyToSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                CharacterSpawnerRoot root = GetOrCreateModeESpawnerRoot();
                if (root != null)
                {
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
                    if (modeEVirtualSpawnerRoot.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(modeEVirtualSpawnerRoot.gameObject);
                    }
                    modeEVirtualSpawnerRoot = null;
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
        /// Mode E 基础血量提升：非狼阵营敌人血量 × 1.5
        /// 在敌人生成时一次性应用，不影响后续阵营死亡缩放机制
        /// 狼阵营（玩家可能加入的阵营）不应用此提升
        /// </summary>
        private void ApplyModeEBaseHealthBoost(CharacterMainControl character)
        {
            try
            {
                // 狼阵营不应用血量提升
                if (character.Team == Teams.wolf) return;

                var characterItem = character.CharacterItem;
                if (characterItem == null) return;

                Stat maxHealthStat = characterItem.GetStat("MaxHealth");
                if (maxHealthStat == null) return;

                // 增加 50% 血量（等效于 × 1.5）
                float hpBoost = maxHealthStat.BaseValue * 0.5f;
                Modifier boostMod = new Modifier(ModifierType.Add, hpBoost, this);
                maxHealthStat.AddModifier(boostMod);

                // 同步当前血量到新上限
                Health health = character.Health;
                if (health != null)
                {
                    health.CurrentHealth = maxHealthStat.Value;
                }

                DevLog("[ModeE] 基础血量提升: " + character.gameObject.name + " HP × 1.5 (+" + hpBoost + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ApplyModeEBaseHealthBoost 失败: " + e.Message);
            }
        }

        #endregion
    }
}

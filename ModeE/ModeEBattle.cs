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
using UnityEngine.UI;
using TMPro;
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

        /// <summary>
        /// 在所有阵营的刷怪点一次性生成全部 Boss
        /// 按距离玩家由近到远分批生成，每批之间让出一帧，避免卡顿
        /// </summary>
        public async void ModeESpawnAllBosses()
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

                // 分批生成：每个boss让出一帧，分散到多帧执行，避免开局卡顿
                // 允许拉长到5秒完全加载完
                const int BATCH_SIZE = 1;
                for (int i = 0; i < spawnTasks.Count; i++)
                {
                    if (!modeEActive) break; // 模式已结束，停止生成

                    var task = spawnTasks[i];
                    SpawnSingleModeEBoss(task.faction, task.pos);

                    // 每个boss让出多帧，把生成压力分散到更长时间
                    if (i + 1 < spawnTasks.Count)
                    {
                        // 每个boss之间等待0.15秒，让角色创建和配装有时间完成
                        await UniTask.Delay(150);
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
        /// 生成单个 Mode E Boss（从 ModeESpawnAllBosses 分批调用）
        /// 
        /// 阵营匹配设计：
        ///   Boss 的阵营由其预设中的 EnemyPresetInfo.team 决定（来自游戏原版 CharacterRandomPreset.team）。
        ///   每个阵营的刷怪点只从该阵营的 Boss 池中抽取，不随机覆盖阵营。
        ///   如果该阵营没有 Boss，则从该阵营的小怪池补充。
        ///   如果该阵营连小怪都没有，则从全局 Boss 池随机抽取并用 SetTeam 覆盖（兜底）。
        /// </summary>
        private void SpawnSingleModeEBoss(Teams faction, Vector3 spawnPoint)
        {
            try
            {
                EnemyPresetInfo bossPreset = null;
                bool isThisDragonDescendant = false;
                bool isBoss = true;

                // 第1优先：从该阵营的 Boss 池中抽取
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    bossPreset = GetBossPresetForFaction(faction);
                    if (bossPreset != null) break;
                    // 该阵营 Boss 池已耗尽（龙裔/龙王被过滤后可能为空），直接跳出
                    break;
                }

                // 第2优先：该阵营没有 Boss，从该阵营的小怪池补充
                if (bossPreset == null)
                {
                    DevLog("[ModeE] 阵营 " + faction + " 无匹配Boss，尝试小怪池");
                    bossPreset = GetMinionPresetForFaction(faction);
                    if (bossPreset != null) isBoss = false;
                }

                // 第3优先（兜底）：该阵营连小怪都没有，从全局 Boss 池随机抽取
                if (bossPreset == null)
                {
                    DevLog("[ModeE] [WARNING] 阵营 " + faction + " 无任何匹配预设，从全局池兜底");
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        bossPreset = GetRandomBossPreset();
                        if (bossPreset == null) continue;
                        if (IsDragonDescendantPreset(bossPreset) && modeEDragonDescendantSpawned) { bossPreset = null; continue; }
                        if (IsDragonKingPreset(bossPreset)) { bossPreset = null; continue; }
                        break;
                    }
                    isBoss = true;
                }

                // 记录龙裔标记（龙皇在 Mode E 中已被完全排除，无需追踪）
                if (bossPreset != null)
                {
                    isThisDragonDescendant = IsDragonDescendantPreset(bossPreset);
                    if (isThisDragonDescendant) modeEDragonDescendantSpawned = true;
                }

                if (bossPreset != null)
                {
                    Vector3 spawnPos = GetSafeBossSpawnPosition(spawnPoint);
                    modeETotalSpawnExpected++;

                    Teams capturedFaction = faction;

                    // skipDragonDescendant：防止 SpawnEnemyCore 重试时意外生成额外的龙裔
                    // skipDragonKing：Mode E 完全排除龙皇，始终跳过
                    bool skipDragon = !isThisDragonDescendant && modeEDragonDescendantSpawned;
                    bool skipKing = true; // Mode E 完全排除龙皇

                    // 捕获龙裔标记，用于生成失败时回退
                    bool capturedIsDD = isThisDragonDescendant;

                    DevLog("[ModeE] 阵营 " + faction + " 生成: " + bossPreset.displayName + " (预设team=" + bossPreset.team + ", isBoss=" + isBoss + ")");

                    SpawnEnemyCore(
                        bossPreset,
                        spawnPos,
                        isBoss,
                        isActiveCheck: () => modeEActive,
                        onSpawned: (ctx) => OnModeEEnemySpawned(ctx, capturedFaction),
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
                else
                {
                    DevLog("[ModeE] [WARNING] 阵营 " + faction + " 无法获取任何预设，跳过");
                }
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
        private void OnModeEEnemySpawned(EnemySpawnContext ctx, Teams faction)
        {
            try
            {
                CharacterMainControl character = ctx.character;

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

                // 加入存活敌人列表
                modeEAliveEnemies.Add(character);

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

                // 在 Boss 名字上方显示阵营标签（白色文字）
                CreateFactionLabel(character, faction);

                // 更新生成计数
                modeESpawnResolved++;
                DevLog("[ModeE] 生成结案: resolved=" + modeESpawnResolved + "/" + modeETotalSpawnExpected);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] OnModeEEnemySpawned 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 查找距离指定角色最近的敌对阵营单位（用于 AI 初始目标设置）
        /// </summary>
        private CharacterMainControl FindNearestEnemyForFaction(CharacterMainControl self, Teams selfFaction)
        {
            try
            {
                CharacterMainControl nearest = null;
                float nearestDist = float.MaxValue;

                for (int i = 0; i < modeEAliveEnemies.Count; i++)
                {
                    CharacterMainControl other = modeEAliveEnemies[i];
                    if (other == null || other == self) continue;
                    if (other.Team == selfFaction) continue; // 跳过同阵营

                    float dist = Vector3.Distance(self.transform.position, other.transform.position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = other;
                    }
                }

                // 也考虑玩家作为潜在目标（如果玩家不是同阵营）
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Team != selfFaction)
                {
                    float playerDist = Vector3.Distance(self.transform.position, player.transform.position);
                    if (playerDist < nearestDist)
                    {
                        nearest = player;
                    }
                }

                return nearest;
            }
            catch
            {
                return null;
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
        /// </summary>
        private void OnModeEEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                if (!modeEActive) return;

                // 获取死亡敌人的阵营
                Teams enemyFaction = enemy.Team;

                // 从存活列表移除
                modeEAliveEnemies.Remove(enemy);

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

                // 对该阵营存活单位应用缩放
                ApplyFactionDeathScaling(enemyFaction);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] OnModeEEnemyDeath 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 对指定阵营的所有存活单位应用属性缩放（生命值 + 伤害）
        /// 倍率 = 1.0 + 该阵营已死亡单位数 × 0.05
        /// 每次先移除旧 Modifier 再添加新的，避免累积叠加
        /// </summary>
        private void ApplyFactionDeathScaling(Teams faction)
        {
            try
            {
                float multiplier = GetFactionScaleMultiplier(faction);
                DevLog("[ModeE] 应用阵营缩放: " + faction + " 倍率=" + multiplier);

                for (int i = modeEAliveEnemies.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = modeEAliveEnemies[i];
                    if (enemy == null || enemy.gameObject == null)
                    {
                        modeEAliveEnemies.RemoveAt(i);
                        modeEScalingModifiers.Remove(enemy);
                        continue;
                    }

                    // 只对同阵营的存活单位应用缩放
                    if (enemy.Team != faction) continue;

                    try
                    {
                        var characterItem = enemy.CharacterItem;
                        if (characterItem == null) continue;

                        // 移除该敌人上一次的缩放 Modifier
                        if (modeEScalingModifiers.TryGetValue(enemy, out var oldMods))
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

        #region Mode E 阵营标签

        /// <summary>
        /// 阵营标签 UI 的 Screen Space Canvas 容器（所有标签共用）
        /// </summary>
        private static Canvas modeEFactionLabelCanvas;

        /// <summary>
        /// 获取或创建阵营标签的 Screen Space Overlay Canvas
        /// 复用游戏原生 HealthBar 的渲染方式，确保文字清晰
        /// </summary>
        private static Canvas GetOrCreateFactionLabelCanvas()
        {
            if (modeEFactionLabelCanvas != null) return modeEFactionLabelCanvas;

            GameObject canvasObj = new GameObject("ModeE_FactionLabelCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasObj);

            modeEFactionLabelCanvas = canvasObj.AddComponent<Canvas>();
            modeEFactionLabelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            modeEFactionLabelCanvas.sortingOrder = 90; // 略低于 HealthBar，避免遮挡血条

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<GraphicRaycaster>();

            return modeEFactionLabelCanvas;
        }

        /// <summary>
        /// 在 Boss 头顶创建阵营标签（Screen Space UI，复用游戏原生 HealthBar 的定位方式）
        /// </summary>
        private void CreateFactionLabel(CharacterMainControl character, Teams faction)
        {
            try
            {
                if (character == null || character.transform == null) return;

                string factionName = GetFactionDisplayName(faction);
                Canvas canvas = GetOrCreateFactionLabelCanvas();

                // 创建标签 UI 元素
                GameObject labelObj = new GameObject("ModeE_FactionLabel_" + character.GetInstanceID());
                labelObj.transform.SetParent(canvas.transform, false);

                // 添加 RectTransform（SetParent 到 Canvas 下会自动添加）
                RectTransform rect = labelObj.GetComponent<RectTransform>();
                if (rect == null) rect = labelObj.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200f, 30f);

                // 使用 TextMeshProUGUI，和游戏原生 HealthBar 的 nameText 一致
                TextMeshProUGUI tmpText = labelObj.AddComponent<TextMeshProUGUI>();
                tmpText.text = factionName;
                tmpText.fontSize = 16f;
                tmpText.color = new Color(1f, 1f, 1f, 1f); // 纯白色，和游戏原生名字一致
                tmpText.fontStyle = FontStyles.Bold;
                tmpText.alignment = TextAlignmentOptions.Center;
                tmpText.enableWordWrapping = false;
                tmpText.overflowMode = TextOverflowModes.Overflow;
                tmpText.raycastTarget = false; // 不拦截点击

                // 添加跟踪组件，负责将世界坐标映射到屏幕坐标
                ModeEFactionLabelTracker tracker = labelObj.AddComponent<ModeEFactionLabelTracker>();
                tracker.target = character.transform;
                // 计算头顶偏移（复用 HealthBar 的逻辑：头盔位置 + 额外偏移）
                float headHeight = 1.5f;
                if (character.characterModel != null)
                {
                    Transform helmetSocket = character.characterModel.HelmatSocket;
                    if (helmetSocket != null)
                    {
                        headHeight = Vector3.Distance(character.transform.position, helmetSocket.position) + 0.5f;
                    }
                }
                // 在 HealthBar 名字上方再偏移一点
                tracker.worldOffset = Vector3.up * (headHeight + 0.3f);
                // 屏幕 Y 额外偏移（HealthBar 用 0.02，我们用更大值让标签在名字和血条上方）
                tracker.screenYOffset = 0.055f;

                DevLog("[ModeE] 阵营标签已创建(UI): " + factionName + " -> " + character.gameObject.name);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] CreateFactionLabel 失败: " + e.Message);
            }
        }

        #endregion
    }

    /// <summary>
    /// Mode E 阵营标签跟踪组件 - 将世界坐标映射到屏幕坐标（复用 HealthBar 的定位方式）
    /// </summary>
    public class ModeEFactionLabelTracker : MonoBehaviour
    {
        /// <summary>跟踪的目标 Transform（敌人角色）</summary>
        public Transform target;
        /// <summary>世界空间偏移（头顶高度）</summary>
        public Vector3 worldOffset = Vector3.up * 2f;
        /// <summary>屏幕 Y 方向额外偏移比例（相对于屏幕高度）</summary>
        public float screenYOffset = 0.035f;

        void LateUpdate()
        {
            // 目标已销毁，清理自身
            if (target == null)
            {
                Destroy(gameObject);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            // 检查目标是否在相机前方
            Vector3 worldPos = target.position + worldOffset;
            Vector3 toTarget = worldPos - cam.transform.position;
            if (Vector3.Dot(toTarget, cam.transform.forward) <= 0f)
            {
                // 在相机背后，隐藏
                gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf) gameObject.SetActive(true);

            // 世界坐标转屏幕坐标（和 HealthBar.UpdatePosition 完全一致的方式）
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            screenPos.y += screenYOffset * Screen.height;
            transform.position = screenPos;
        }
    }
}

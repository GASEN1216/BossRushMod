// ============================================================================
// ModeDWaves.cs - Mode D 波次管理
// ============================================================================
// 模块说明：
//   管理 Mode D 模式的波次系统，包括敌人生成、波次进度、难度递增等。
//   
// 波次规则：
//   - 第 1-5 波：全小怪
//   - 第 6-10 波：1 个 Boss + 小怪
//   - 第 11-15 波：2 个 Boss + 小怪
//   - 第 16+ 波：全 Boss
//   
// 主要功能：
//   - 开始下一波敌人
//   - 计算每波的 Boss/小怪配比
//   - 生成敌人并应用波次强化
//   - 处理敌人死亡和波次完成事件
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// Mode D 波次管理模块
    /// <para>无限波次，按波次决定小怪/Boss配比，每波提升 3% 属性</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // P2-6: 复用 List 缓存，减少每波分配
        private static readonly List<Vector3> reusableVector3List = new List<Vector3>();
        private static readonly List<SpawnInfo> reusableSpawnQueue = new List<SpawnInfo>();

        /// <summary>
        /// Mode D: 开始下一波
        /// </summary>
        /// <returns>是否成功开始下一波（false 表示仍有敌人存活等条件不满足）</returns>
        public bool ModeDStartNextWave()
        {
            try
            {
                if (!modeDActive)
                {
                    DevLog("[ModeD] ModeDStartNextWave: Mode D 未激活");
                    return false;
                }

                // P1-2 修复：生成中禁止开波的硬护栏
                // 如果上一波仍有生成任务未结案，禁止开新波（防止跳波/状态不一致）
                if (modeDWaveIndex > 0 && modeDSpawnResolvedInCurrentWave < modeDExpectedEnemiesInCurrentWave)
                {
                    ShowMessage(L10n.T(
                        "正在生成敌人，请稍候...",
                        "Still spawning enemies, please wait..."));
                    DevLog("[ModeD] [WARNING] ModeDStartNextWave: 上一波生成未完成，禁止开新波 (resolved=" +
                           modeDSpawnResolvedInCurrentWave + ", expected=" + modeDExpectedEnemiesInCurrentWave + ")");
                    return false;
                }

                // 取消可能存在的自动下一波协程（防止重复开波）
                if (modeDAutoNextWaveCoroutine != null)
                {
                    StopCoroutine(modeDAutoNextWaveCoroutine);
                    modeDAutoNextWaveCoroutine = null;
                }

                SetBossRushRuntimeActive(true);

                // P0-2 修复：显式隐藏"冲下一波"选项（调用当前 ModBehaviour 的方法，而非路牌的）
                HideModeDNextWaveOption();

                // 检查当前波是否还有敌人存活
                CleanupDeadEnemies();
                if (modeDCurrentWaveEnemies.Count > 0)
                {
                    ShowMessage(L10n.T(
                        "当前波次还有 " + modeDCurrentWaveEnemies.Count + " 个敌人存活！",
                        modeDCurrentWaveEnemies.Count + " enemies still alive in current wave!"));
                    return false;
                }
                
                modeDWaveIndex++;
                // 重置波次完成标志，允许下一波完成时触发
                modeDWaveCompletePending = false;
                // 重置生成计数
                modeDExpectedEnemiesInCurrentWave = 0;
                modeDSpawnResolvedInCurrentWave = 0;
                DevLog("[ModeD] 开始第 " + modeDWaveIndex + " 波");

                // 每次开新波前动态刷新每波敌人数
                if (config != null && config.modeDEnemiesPerWave > 0)
                {
                    modeDEnemiesPerWave = Mathf.Clamp(config.modeDEnemiesPerWave, 1, 10);
                }
                
                // 计算本波的 Boss 数量和小怪数量
                int totalEnemies = modeDEnemiesPerWave;
                int bossCount = GetModeDWaveBossCount(modeDWaveIndex, totalEnemies);
                int minionCount = totalEnemies - bossCount;
                
                DevLog("[ModeD] 波次 " + modeDWaveIndex + ": 总敌人=" + totalEnemies + 
                       ", Boss=" + bossCount + ", 小怪=" + minionCount);
                
                // 显示波次横幅
                ShowBigBanner(L10n.T(
                    "第 <color=yellow>" + modeDWaveIndex + "</color> 波开始！",
                    "Wave <color=yellow>" + modeDWaveIndex + "</color> started!"
                ));
                
                // 波次开始时切换路牌到加油状态（支持连点10次结束波次的兜底机制）
                if (bossRushSignInteract != null)
                {
                    bossRushSignInteract.SetCheerMode();
                }

                // 生成敌人
                SpawnModeDWaveEnemies(bossCount, minionCount);

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] ModeDStartNextWave 失败: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 根据波次计算 Boss 数量
        /// </summary>
        /// <remarks>
        /// 波次规则：
        /// <list type="bullet">
        /// <item>第 1-5 波：全小怪（0 个 Boss）</item>
        /// <item>第 6-10 波：1 个 Boss</item>
        /// <item>第 11-15 波：2 个 Boss</item>
        /// <item>第 16+ 波：全 Boss</item>
        /// </list>
        /// </remarks>
        /// <param name="waveIndex">当前波次索引</param>
        /// <param name="totalEnemies">本波总敌人数</param>
        /// <returns>Boss 数量</returns>
        private int GetModeDWaveBossCount(int waveIndex, int totalEnemies)
        {
            if (waveIndex <= 5)
            {
                // 第1-5波：全小怪
                return 0;
            }
            else if (waveIndex <= 10)
            {
                // 第6-10波：1个Boss（或按比例）
                return Mathf.Min(1, totalEnemies);
            }
            else if (waveIndex <= 15)
            {
                // 第11-15波：2个Boss（或按比例）
                return Mathf.Min(2, totalEnemies);
            }
            else
            {
                // 第16+波：全Boss
                return totalEnemies;
            }
        }
        
        /// <summary>
        /// 生成 Mode D 波次敌人
        /// </summary>
        /// <summary>
        /// 生成 Mode D 波次敌人（分帧生成，避免低端机帧尖刺）
        /// </summary>
        private System.Collections.IEnumerator SpawnModeDWaveEnemiesCoroutine(int bossCount, int minionCount)
        {
            // 初始化部分（无 yield）
            modeDCurrentWaveEnemies.Clear();

            CharacterMainControl playerMain = CharacterMainControl.Main;
            if (playerMain == null)
            {
                DevLog("[ModeD] [ERROR] 未找到玩家");
                yield break;
            }

            // 获取刷怪点并洗牌，确保随机但不重复
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                spawnPoints = DemoChallengeSpawnPoints;
            }

            // 兜底：如果刷怪点仍为空，基于玩家位置生成圆环随机点
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Vector3 playerPos = playerMain.transform.position;
                spawnPoints = GenerateFallbackSpawnPointsAroundPlayer(playerPos);
                DevLog("[ModeD] 使用玩家位置生成的兜底刷怪点");
            }

            var shuffledPositions = ShuffleSpawnPoints(spawnPoints);
            int spawnIndex = 0;

            bool bannerShown = false;
            Vector3 bannerPos = Vector3.zero;

            // P2-6 优化：使用复用的 reusableSpawnQueue，避免每波分配新列表
            reusableSpawnQueue.Clear();

            // 生成 Boss（带重试机制，防止预设为空导致卡波次）
            for (int i = 0; i < bossCount; i++)
            {
                EnemyPresetInfo bossPreset = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    bossPreset = GetRandomBossPreset();
                    if (bossPreset != null) break;
                }

                if (bossPreset != null)
                {
                    Vector3 spawnPos = GetSafeBossSpawnPosition(shuffledPositions[spawnIndex % shuffledPositions.Count]);
                    spawnIndex++;

                    if (!bannerShown)
                    {
                        bannerPos = spawnPos;
                        bannerShown = true;
                    }

                    modeDExpectedEnemiesInCurrentWave++;
                    reusableSpawnQueue.Add(new SpawnInfo { preset = bossPreset, position = spawnPos, isBoss = true });
                }
                else
                {
                    DevLog("[ModeD] [WARNING] Boss预设获取失败，跳过该敌人（已重试5次）");
                }
            }

            // 生成小怪
            for (int i = 0; i < minionCount; i++)
            {
                EnemyPresetInfo minionPreset = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    minionPreset = GetRandomMinionPreset();
                    if (minionPreset != null) break;
                }

                if (minionPreset != null)
                {
                    Vector3 spawnPos = GetSafeBossSpawnPosition(shuffledPositions[spawnIndex % shuffledPositions.Count]);
                    spawnIndex++;

                    if (!bannerShown)
                    {
                        bannerPos = spawnPos;
                        bannerShown = true;
                    }

                    modeDExpectedEnemiesInCurrentWave++;
                    reusableSpawnQueue.Add(new SpawnInfo { preset = minionPreset, position = spawnPos, isBoss = false });
                }
                else
                {
                    DevLog("[ModeD] [WARNING] 小怪预设获取失败，跳过该敌人（已重试5次）");
                }
            }

            // 如果预期敌人数为0，直接完成本波
            if (modeDExpectedEnemiesInCurrentWave == 0)
            {
                DevLog("[ModeD] [WARNING] 本波无任何敌人可生成，直接完成波次");
                TryResolveModeDWaveComplete();
                yield break;
            }

            // 显示敌人方位横幅
            if (bannerShown)
            {
                try
                {
                    ShowEnemyBanner_UIAndSigns(L10n.T("敌人", "Enemies"), bannerPos, playerMain.transform.position,
                        0, 1, true, Mathf.Max(0, modeDWaveIndex - 1), 1);
                }
                catch {}
            }

            // 分帧逐个生成敌人（每生成一个等待一帧，避免低端机帧尖刺）
            // 注意：这部分不能放在 try-catch 中，因为 C# 不允许在包含 catch 的 try 块中使用 yield
            for (int i = 0; i < reusableSpawnQueue.Count; i++)
            {
                var info = reusableSpawnQueue[i];
                SpawnModeDEnemy(info.preset, info.position, info.isBoss);

                // 每生成一个敌人后等待一帧（低端机友好）
                yield return null;
            }

            DevLog("[ModeD] 本波敌人生成完成（分帧生成，共" + reusableSpawnQueue.Count + "个）");
        }

        /// <summary>
        /// 生成 Mode D 波次敌人（入口方法，启动协程）
        /// </summary>
        private void SpawnModeDWaveEnemies(int bossCount, int minionCount)
        {
            StartCoroutine(SpawnModeDWaveEnemiesCoroutine(bossCount, minionCount));
        }

        /// <summary>
        /// 刷怪信息结构（用于分帧生成）
        /// </summary>
        private struct SpawnInfo
        {
            public EnemyPresetInfo preset;
            public Vector3 position;
            public bool isBoss;
        }
        /// <summary>
        /// 获取随机 Boss 预设
        /// </summary>
        /// <remarks>
        /// 前期波次（第6-10波）会过滤掉强力 Boss（口口口口和四骑士，即 StormBoss1-5），
        /// 避免白手起家模式下玩家在装备不足时遇到过强的敌人。
        /// </remarks>
        private EnemyPresetInfo GetRandomBossPreset()
        {
            if (modeDBossPool == null || modeDBossPool.Count == 0)
            {
                DevLog("[ModeD] Boss池为空");
                return null;
            }

            // 第6-10波（首次出Boss的波次）：过滤掉强力Boss
            if (modeDWaveIndex <= 10)
            {
                // 使用复用缓存避免 GC
                presetFilterCache.Clear();
                for (int i = 0; i < modeDBossPool.Count; i++)
                {
                    EnemyPresetInfo boss = modeDBossPool[i];
                    if (boss == null || string.IsNullOrEmpty(boss.name))
                    {
                        continue;
                    }

                    // 排除强力 Boss
                    if (EarlyWaveExcludedBosses.Contains(boss.name))
                    {
                        continue;
                    }

                    presetFilterCache.Add(boss);
                }

                if (presetFilterCache.Count > 0)
                {
                    DevLog("[ModeD] 前期波次Boss过滤：可用Boss数=" + presetFilterCache.Count + "/" + modeDBossPool.Count);
                    return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                }

                // 如果过滤后没有可用Boss，回退到完整池
                DevLog("[ModeD] 前期波次过滤后无可用Boss，使用完整池");
            }

            return modeDBossPool[UnityEngine.Random.Range(0, modeDBossPool.Count)];
        }

        /// <summary>
        /// 获取随机小怪预设
        /// </summary>
        /// <remarks>
        /// 前10波会过滤掉幽灵（Cname_Ghost）
        /// </remarks>
        private EnemyPresetInfo GetRandomMinionPreset()
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0)
            {
                DevLog("[ModeD] 小怪池为空，使用Boss池替代");
                return GetRandomBossPreset();
            }

            // 第 1~2 波：只刷血量最低的敌人（拾荒者等最普通的鸭鸭敌人）
            // 同时过滤掉幽灵和"???"名字的敌人
            if (modeDWaveIndex <= 2)
            {
                // 第一步：从原始池过滤掉幽灵和"???"名字的敌人
                presetFilterCache.Clear();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;
                    if (info.name == "Cname_Ghost") continue; // 排除幽灵

                    string name = info.displayName;
                    if (name == null) name = string.Empty;
                    if (name == "???" || name == "？？？") continue; // 排除"???"名字的敌人

                    presetFilterCache.Add(info);
                }

                if (presetFilterCache.Count > 0)
                {
                    // 找出血量最低值
                    float minHealth = float.MaxValue;
                    for (int i = 0; i < presetFilterCache.Count; i++)
                    {
                        if (presetFilterCache[i].baseHealth < minHealth)
                        {
                            minHealth = presetFilterCache[i].baseHealth;
                        }
                    }

                    // 筛选出血量最低的敌人（允许 10% 的误差范围，以包含同级别的敌人）
                    float healthThreshold = minHealth * 1.1f;
                    presetFilterCache2.Clear();
                    for (int i = 0; i < presetFilterCache.Count; i++)
                    {
                        if (presetFilterCache[i].baseHealth <= healthThreshold)
                        {
                            presetFilterCache2.Add(presetFilterCache[i]);
                        }
                    }

                    if (presetFilterCache2.Count > 0)
                    {
                        DevLog("[ModeD] 前两波限制：只刷血量最低的敌人，可选数量=" + presetFilterCache2.Count + ", 血量阈值=" + healthThreshold);
                        return presetFilterCache2[UnityEngine.Random.Range(0, presetFilterCache2.Count)];
                    }
                    else
                    {
                        // 没有符合血量条件的，使用过滤后的池
                        return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                    }
                }
                // 过滤后为空，使用原始池
                return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
            }
            // 第 3~5 波：过滤掉幽灵和"???"名字的敌人，但不限制血量
            else if (modeDWaveIndex <= 5)
            {
                presetFilterCache.Clear();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;
                    if (info.name == "Cname_Ghost") continue; // 排除幽灵

                    string name = info.displayName;
                    if (name == null) name = string.Empty;
                    if (name == "???" || name == "？？？") continue; // 过滤掉"???"名字的小怪

                    presetFilterCache.Add(info);
                }

                if (presetFilterCache.Count > 0)
                {
                    return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                }
                // 过滤后为空，使用原始池
                return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
            }
            // 第 6~10 波：只过滤掉幽灵
            else if (modeDWaveIndex <= 10)
            {
                presetFilterCache.Clear();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;
                    if (info.name == "Cname_Ghost") continue; // 排除幽灵
                    presetFilterCache.Add(info);
                }
                if (presetFilterCache.Count > 0)
                {
                    return presetFilterCache[UnityEngine.Random.Range(0, presetFilterCache.Count)];
                }
                // 过滤后为空，使用原始池
                return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
            }

            // 第 11+ 波：使用完整池
            return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
        }

        /// <summary>
        /// 洗牌刷怪点数组（Fisher-Yates 算法）
        /// P2-6 优化：使用复用 List 缓存，避免每波分配新列表
        /// </summary>
        private List<Vector3> ShuffleSpawnPoints(Vector3[] spawnPoints)
        {
            reusableVector3List.Clear();
            reusableVector3List.AddRange(spawnPoints);
            for (int i = reusableVector3List.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                Vector3 temp = reusableVector3List[i];
                reusableVector3List[i] = reusableVector3List[j];
                reusableVector3List[j] = temp;
            }
            return reusableVector3List;
        }

        /// <summary>
        /// 基于玩家位置生成兜底刷怪点（圆环分布）
        /// </summary>
        /// <param name="playerPos">玩家位置</param>
        /// <param name="pointCount">生成的点数（默认10个）</param>
        /// <param name="minRadius">最小半径</param>
        /// <param name="maxRadius">最大半径</param>
        private Vector3[] GenerateFallbackSpawnPointsAroundPlayer(Vector3 playerPos, int pointCount = 10, float minRadius = 8f, float maxRadius = 15f)
        {
            Vector3[] points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                float angle = (360f / pointCount) * i;
                float radius = UnityEngine.Random.Range(minRadius, maxRadius);
                float rad = angle * Mathf.Deg2Rad;

                float x = playerPos.x + Mathf.Cos(rad) * radius;
                float z = playerPos.z + Mathf.Sin(rad) * radius;

                // 尝试找到地面高度
                float y = playerPos.y;
                try
                {
                    // 从高处向下射线检测地面
                    RaycastHit hit;
                    if (Physics.Raycast(new Vector3(x, playerPos.y + 50f, z), Vector3.down, out hit, 100f))
                    {
                        y = hit.point.y;
                    }
                }
                catch {}

                points[i] = new Vector3(x, y, z);
            }
            return points;
        }

        /// <summary>
        /// 生成 Mode D 敌人（带最多 5 次重试）
        /// </summary>
        private async void SpawnModeDEnemy(EnemyPresetInfo preset, Vector3 position, bool isBoss)
        {
            // 防止跨波迟到敌人污染下一波
            int waveToken = modeDWaveIndex;

            try
            {
                const int maxAttempts = 5;
                EnemyPresetInfo currentPresetInfo = preset;

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        if (currentPresetInfo == null)
                        {
                            currentPresetInfo = isBoss ? GetRandomBossPreset() : GetRandomMinionPreset();
                        }

                        if (currentPresetInfo == null)
                        {
                            DevLog("[ModeD] SpawnModeDEnemy: 预设为空, attempt=" + attempt + ", isBoss=" + isBoss);
                            continue;
                        }

                        DevLog("[ModeD] 生成敌人尝试: " + currentPresetInfo.displayName + " (isBoss=" + isBoss + ", attempt=" + attempt + ")");

                        // 生成敌人
                        int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                        CharacterMainControl character = null;

                        // 检查是否是龙裔遗族Boss（自定义Boss，没有原生CharacterRandomPreset）
                        if (IsDragonDescendantPreset(currentPresetInfo))
                        {
                            DevLog("[ModeD] 检测到龙裔遗族Boss，使用专用生成方法");
                            try
                            {
                                character = await SpawnDragonDescendant(position);
                                if (character != null)
                                {
                                    DevLog("[ModeD] 龙裔遗族Boss生成成功");
                                }
                                else
                                {
                                    DevLog("[ModeD] 龙裔遗族Boss生成失败，重新随机");
                                    currentPresetInfo = null;
                                    continue;
                                }
                            }
                            catch (Exception dragonEx)
                            {
                                DevLog("[ModeD] 龙裔遗族Boss生成异常: " + dragonEx.Message);
                                currentPresetInfo = null;
                                continue;
                            }
                        }
                        else
                        {
                            // 查找预设（使用缓存字典 O(1)，避免每次 FindObjectsOfTypeAll）
                            CharacterRandomPreset targetPreset = null;
                            if (cachedCharacterPresets != null)
                            {
                                cachedCharacterPresets.TryGetValue(currentPresetInfo.name, out targetPreset);
                            }

                            if (targetPreset == null)
                            {
                                DevLog("[ModeD] 未找到预设: " + currentPresetInfo.name + " (attempt=" + attempt + ")");
                                currentPresetInfo = null;
                                continue;
                            }

                            try
                            {
                                character = await targetPreset.CreateCharacterAsync(position, Vector3.forward, relatedScene, null, false);
                            }
                            catch (Exception createEx)
                            {
                                DevLog("[ModeD] 生成敌人异常: " + currentPresetInfo.displayName + " - " + createEx.Message);
                                currentPresetInfo = null;
                                continue;
                            }

                            if (character == null)
                            {
                                DevLog("[ModeD] 生成敌人失败: " + currentPresetInfo.displayName + " (attempt=" + attempt + ")");
                                currentPresetInfo = null;
                                continue;
                            }
                        }

                        // 如果波次已经变化或模式结束，销毁并退出，避免污染下一波
                        if (!modeDActive || modeDWaveIndex != waveToken)
                        {
                            UnityEngine.Object.Destroy(character.gameObject);
                            DevLog("[ModeD] 敌人生成完成但波次已变化，销毁敌人");
                            return;
                        }

                        character.gameObject.name = "ModeD_" + currentPresetInfo.displayName;
                        
                        // 标记由 Mode D 生成的大兴兴，防止被 TryCleanNonBossRushDaXingXing 误清理
                        try
                        {
                            if (IsDaXingXingPreset(currentPresetInfo))
                            {
                                if (bossRushOwnedDaXingXing != null && !bossRushOwnedDaXingXing.Contains(character))
                                {
                                    bossRushOwnedDaXingXing.Add(character);
                                    DevLog("[ModeD] 已标记大兴兴到 bossRushOwnedDaXingXing 集合");
                                }
                            }
                        }
                        catch {}
                        
                        // 统一伤害倍率为1，避免不同Boss预设的damageMultiplier差异导致伤害过高
                        NormalizeDamageMultiplier(character);

                        // 应用 Mode D 配装（Boss保留原有头盔和护甲）
                        EquipEnemyForModeD(character, modeDWaveIndex, currentPresetInfo.baseHealth, isBoss);

                        // 应用全局 Boss 数值倍率（使用统一方法）
                        ApplyBossStatMultiplier(character);

                        // 渐进式难度：按波次提升敌人属性
                        ApplyModeDWaveScaling(character, modeDWaveIndex);

                        // 激活敌人
                        character.gameObject.SetActive(true);

                        // 强制设置 AI 仇恨到玩家
                        // 设置 forceTracePlayerDistance 为较大值，确保远距离生成的敌人也会追踪玩家
                        try
                        {
                            CharacterMainControl playerMain = CharacterMainControl.Main;
                            if (playerMain != null && playerMain.mainDamageReceiver != null)
                            {
                                AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
                                if (ai != null)
                                {
                                    ai.forceTracePlayerDistance = 500f;
                                    ai.searchedEnemy = playerMain.mainDamageReceiver;
                                    ai.SetTarget(playerMain.mainDamageReceiver.transform);
                                    ai.SetNoticedToTarget(playerMain.mainDamageReceiver);
                                    ai.noticed = true;
                                }
                            }
                        }
                        catch {}

                        // 如果波次已经变化，销毁并退出
                        if (!modeDActive || modeDWaveIndex != waveToken)
                        {
                            UnityEngine.Object.Destroy(character.gameObject);
                            DevLog("[ModeD] 敌人配置完成但波次已变化，销毁敌人");
                            return;
                        }

                        // 加入当前波敌人列表
                        modeDCurrentWaveEnemies.Add(character);

                        // 注册死亡事件
                        RegisterModeDEnemyDeath(character);

                        DevLog("[ModeD] 敌人生成成功: " + currentPresetInfo.displayName + " (attempt=" + attempt + ")");
                        return;
                    }
                    catch (Exception e)
                    {
                        DevLog("[ModeD] [ERROR] SpawnModeDEnemy 尝试失败: " + e.Message);
                        // 下次重试时重新随机预设
                        currentPresetInfo = null;
                    }
                }

                DevLog("[ModeD] [ERROR] SpawnModeDEnemy 多次尝试仍然失败 (isBoss=" + isBoss + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] SpawnModeDEnemy 异常: " + e.Message);
            }
            finally
            {
                // 波次一致性守卫：只有当前波的任务才计数，防止跨波异步回流污染
                if (modeDActive && modeDWaveIndex == waveToken)
                {
                    // 结案：成功或失败都算 +1
                    modeDSpawnResolvedInCurrentWave++;
                    DevLog("[ModeD] 生成结案: resolved=" + modeDSpawnResolvedInCurrentWave + "/" + modeDExpectedEnemiesInCurrentWave);

                    // 尝试解析波次是否完成
                    TryResolveModeDWaveComplete();
                }
                else
                {
                    DevLog("[ModeD] 生成任务完成但波次已变化(waveToken=" + waveToken + ", current=" + modeDWaveIndex + ")，跳过结案计数");
                }
            }
        }

        /// <summary>
        /// 尝试解析波次是否完成
        /// 只有当满足以下所有条件时才触发波次完成：
        /// 1. 波次未标记为完成中（防止重复触发）
        /// 2. 当前没有存活敌人
        /// 3. 所有敌人生成都已结案（成功或最终失败，防止"迟到的怪"）
        /// </summary>
        private void TryResolveModeDWaveComplete()
        {
            if (modeDWaveCompletePending) return;

            CleanupDeadEnemies();

            // 还有存活敌人，肯定不能结算
            if (modeDCurrentWaveEnemies.Count > 0)
            {
                DevLog("[ModeD] TryResolve: 还有 " + modeDCurrentWaveEnemies.Count + " 个存活敌人");
                return;
            }

            // 生成还没全部结案，不能结算（防止"迟到的怪"）
            if (modeDSpawnResolvedInCurrentWave < modeDExpectedEnemiesInCurrentWave)
            {
                DevLog("[ModeD] TryResolve: 生成未全部结案 (" + modeDSpawnResolvedInCurrentWave + "/" + modeDExpectedEnemiesInCurrentWave + ")");
                return;
            }

            DevLog("[ModeD] TryResolve: 满足波次完成条件，触发 OnModeDWaveComplete");
            OnModeDWaveComplete();
        }

        /// <summary>
        /// 将敌人的伤害倍率统一设置为 1
        /// </summary>
        /// <remarks>
        /// 原版游戏中不同 Boss 有不同的 damageMultiplier（如 1.0、1.5、2.0 等），
        /// 在白手起家模式下给玩家发放武器后，高倍率 Boss 伤害会过高，
        /// 因此需要统一为 1 以保持游戏平衡。
        /// </remarks>
        /// <param name="character">需要调整的敌人角色</param>
        private void NormalizeDamageMultiplier(CharacterMainControl character)
        {
            try
            {
                if (character == null) return;

                var item = character.CharacterItem;
                if (item == null) return;

                // 将枪械伤害倍率设置为1
                try
                {
                    Stat gunDmg = item.GetStat("GunDamageMultiplier");
                    if (gunDmg != null)
                    {
                        float oldValue = gunDmg.BaseValue;
                        gunDmg.BaseValue = 1f;
                        DevLog("[ModeD] 统一枪械伤害倍率: " + oldValue + " -> 1.0");
                    }
                }
                catch {}

                // 将近战伤害倍率设置为1
                try
                {
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier");
                    if (meleeDmg != null)
                    {
                        float oldValue = meleeDmg.BaseValue;
                        meleeDmg.BaseValue = 1f;
                        DevLog("[ModeD] 统一近战伤害倍率: " + oldValue + " -> 1.0");
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] NormalizeDamageMultiplier 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 应用 Mode D 波次数值强化
        /// </summary>
        private void ApplyModeDWaveScaling(CharacterMainControl character, int waveIndex)
        {
            try
            {
                // 每波提升 3% 属性
                float scale = 1f + 0.03f * Mathf.Max(0, waveIndex - 1);

                // 通过 Item 的 Stat 系统修改生命值（MaxHealth 是只读属性，通过 Modifier 修改）
                try
                {
                    var characterItem = character.CharacterItem;
                    if (characterItem != null)
                    {
                        Stat maxHealthStat = characterItem.GetStat("MaxHealth");
                        if (maxHealthStat != null)
                        {
                            float maxHp = maxHealthStat.Value;
                            float newHp = maxHp * scale;

                            // 计算需要增加的数值并添加 Modifier
                            float hpDelta = newHp - maxHp;
                            if (hpDelta > 0)
                            {
                                // 使用 ModifierType.Add 添加加法修饰符
                                Modifier modifier = new Modifier(ItemStatsSystem.Stats.ModifierType.Add, hpDelta, this);
                                maxHealthStat.AddModifier(modifier);
                            }

                            // 同步 CurrentHealth 到新的 MaxHealth
                            Health health = character.Health;
                            if (health != null)
                            {
                                health.CurrentHealth = newHp;
                            }
                        }
                    }
                }
                catch {}

                DevLog("[ModeD] 应用波次强化: wave=" + waveIndex + ", scale=" + scale);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] ApplyModeDWaveScaling 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注册敌人死亡事件
        /// </summary>
        private void RegisterModeDEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    health.OnDeadEvent.AddListener((dmgInfo) => OnModeDEnemyDeath(enemy));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] RegisterModeDEnemyDeath 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode D 敌人死亡处理
        /// </summary>
        private void OnModeDEnemyDeath(CharacterMainControl enemy)
        {
            // 日志单独隔离，避免 enemy 为 null 时影响后续关键操作
            try
            {
                DevLog("[ModeD] 敌人死亡: " + (enemy != null ? enemy.name : "<null>"));
            }
            catch {}

            // 获取死亡位置（独立 try，不影响后续操作）
            // 使用 bool 标记而不是 Vector3.zero，避免敌人死在世界原点时被误判
            bool hasDeathPos = false;
            Vector3 deathPosition = Vector3.zero;
            try
            {
                if (enemy != null && enemy.transform != null)
                {
                    deathPosition = enemy.transform.position;
                    hasDeathPos = true;
                }
            }
            catch {}

            // 标记 lootbox（独立 try，不影响后续操作）
            try
            {
                if (hasDeathPos)
                {
                    StartCoroutine(MarkModeDLootboxAtPosition(deathPosition));
                }
            }
            catch {}

            // 关键操作：从列表移除 + 检查波次完成（独立 try，确保一定执行）
            try
            {
                modeDCurrentWaveEnemies.Remove(enemy);
                TryResolveModeDWaveComplete();
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] OnModeDEnemyDeath 关键操作失败: " + e.Message);
            }
        }

        // P2-6 修复：复用数组避免 GC，容量提升到 128 以防极端情况截断
        private static readonly Collider[] LootboxHits = new Collider[128];

        private System.Collections.IEnumerator MarkModeDLootboxAtPosition(Vector3 deathPosition)
        {
            // 等待一帧，确保尸体掉落箱已经生成
            yield return null;

            const float radius = 3f;
            const int lootboxLayerMask = -1;  // -1 = 所有层，可根据实际情况调整

            // 使用 OverlapSphereNonAlloc 做局部查询（无 GC，避免数组分配）
            int hitCount = 0;
            try
            {
                hitCount = Physics.OverlapSphereNonAlloc(deathPosition, radius, LootboxHits, lootboxLayerMask);
            }
            catch {}

            if (hitCount <= 0)
            {
                yield break;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = LootboxHits[i];
                if (col == null) continue;

                InteractableLootbox box = null;
                try
                {
                    box = col.GetComponent<InteractableLootbox>();
                }
                catch {}

                if (box == null) continue;

                try
                {
                    try
                    {
                        if (box.GetComponentInParent<PetAI>() != null)
                        {
                            continue;
                        }
                    }
                    catch {}

                    try
                    {
                        if (box.GetComponentInParent<PetProxy>() != null)
                        {
                            continue;
                        }
                    }
                    catch {}

                    BossRushLootboxMarker marker = box.GetComponent<BossRushLootboxMarker>();
                    if (marker == null)
                    {
                        box.gameObject.AddComponent<BossRushLootboxMarker>();
                    }

                    BossRushDeleteLootboxInteractable deleteInteract = null;
                    try
                    {
                        deleteInteract = box.gameObject.GetComponent<BossRushDeleteLootboxInteractable>();
                    }
                    catch {}

                    if (deleteInteract == null)
                    {
                        try
                        {
                            deleteInteract = box.gameObject.AddComponent<BossRushDeleteLootboxInteractable>();
                        }
                        catch {}
                    }

                    try
                    {
                        box.interactableGroup = true;

                        // 使用缓存的 FieldInfo
                        System.Reflection.FieldInfo othersField = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                        if (othersField != null)
                        {
                            System.Collections.Generic.List<InteractableBase> hostList = othersField.GetValue(box) as System.Collections.Generic.List<InteractableBase>;
                            if (hostList == null)
                            {
                                hostList = new System.Collections.Generic.List<InteractableBase>();
                                othersField.SetValue(box, hostList);
                            }
                            if (deleteInteract != null && !hostList.Contains(deleteInteract))
                            {
                                hostList.Add(deleteInteract);
                            }
                        }
                    }
                    catch {}
                }
                catch {}
            }
        }

        /// <summary>
        /// 清理已死亡的敌人引用
        /// P2-6 优化：使用倒序循环代替 RemoveAll，避免 lambda 委托开销
        /// </summary>
        private void CleanupDeadEnemies()
        {
            try
            {
                // 倒序遍历，删除已死亡的敌人引用
                for (int i = modeDCurrentWaveEnemies.Count - 1; i >= 0; --i)
                {
                    var e = modeDCurrentWaveEnemies[i];
                    if (e == null || e.Health == null || e.Health.IsDead)
                    {
                        modeDCurrentWaveEnemies.RemoveAt(i);
                    }
                }
            }
            catch {}
        }

        /// <summary>
        /// Mode D 波次完整性自检：定期检查是否有敌人存活，如果没有则自动推进波次
        /// </summary>
        /// <remarks>
        /// 解决问题：敌人死亡事件可能丢失（瞬杀、事件触发时机等），导致波次卡住无法推进。
        /// 此方法作为兜底机制，每隔一段时间检查一次敌人存活状态。
        /// </remarks>
        private void TryFixStuckWaveIfNoModeDEnemyAlive()
        {
            try
            {
                if (!modeDActive)
                {
                    return;
                }

                // 如果当前波次索引为0，说明还没开始第一波，不需要自检
                if (modeDWaveIndex <= 0)
                {
                    return;
                }

                // 清理已死亡的敌人引用
                CleanupDeadEnemies();

                // 使用统一的解析方法检查波次是否完成（遵守 resolved 达标约束）
                DevLog("[ModeD] 自检：调用 TryResolveModeDWaveComplete");
                TryResolveModeDWaveComplete();
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] TryFixStuckWaveIfNoModeDEnemyAlive 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode D 波次完成
        /// </summary>
        private void OnModeDWaveComplete()
        {
            try
            {
                // 防止重复触发（敌人死亡事件和自检可能同时触发）
                if (modeDWaveCompletePending)
                {
                    return;
                }
                modeDWaveCompletePending = true;

                DevLog("[ModeD] 第 " + modeDWaveIndex + " 波完成！");

                ShowBigBanner(L10n.T(
                    "第 <color=yellow>" + modeDWaveIndex + "</color> 波完成！",
                    "Wave <color=yellow>" + modeDWaveIndex + "</color> completed!"
                ));
                
                // 波次结束时切换路牌回生小鸡状态
                if (bossRushSignInteract != null)
                {
                    bossRushSignInteract.SetEntryMode();
                }

                bool useInteract = false;
                try
                {
                    if (config != null)
                    {
                        useInteract = config.useInteractBetweenWaves;
                    }
                }
                catch {}

                if (useInteract)
                {
                    ShowMessage(L10n.T("可以通过路牌开始下一波", "Use the signpost to start next wave"));

                    // 显示 Mode D 专用的"冲下一波"选项
                    ShowModeDNextWaveOption();
                }
                else
                {
                    float interval = 15f;
                    try
                    {
                        interval = GetWaveIntervalSeconds();
                    }
                    catch {}

                    if (interval <= 0f)
                    {
                        if (modeDActive)
                        {
                            ModeDStartNextWave();
                        }
                    }
                    else
                    {
                        int secondsInt = Mathf.RoundToInt(interval);
                        if (secondsInt < 1)
                        {
                            secondsInt = 1;
                        }

                        ShowMessage(L10n.T(
                            "下一波将在 " + secondsInt + " 秒后自动开始",
                            "Next wave starts in " + secondsInt + " seconds"));

                        // 取消旧协程，启动新协程（防止重复开波）
                        ScheduleAutoNextWave(interval);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] OnModeDWaveComplete 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 调度自动下一波协程（取消旧协程，防止重复开波）
        /// </summary>
        private void ScheduleAutoNextWave(float delay)
        {
            // 取消旧协程
            if (modeDAutoNextWaveCoroutine != null)
            {
                StopCoroutine(modeDAutoNextWaveCoroutine);
                modeDAutoNextWaveCoroutine = null;
            }

            // 捕获当前波次作为令牌
            int tokenWave = modeDWaveIndex;
            modeDAutoNextWaveCoroutine = StartCoroutine(ModeDAutoNextWave(delay, tokenWave));
        }

        /// <summary>
        /// 自动下一波协程（带波次令牌验证，防止跨波误触发）
        /// </summary>
        private System.Collections.IEnumerator ModeDAutoNextWave(float delay, int tokenWave)
        {
            if (delay <= 0f)
            {
                // 立即开波前二次校验
                if (modeDActive && modeDWaveIndex == tokenWave && modeDWaveCompletePending)
                {
                    ModeDStartNextWave();
                }
                modeDAutoNextWaveCoroutine = null;
                yield break;
            }

            float timer = delay;
            while (timer > 0f)
            {
                // 每帧校验：模式仍激活 + 波次未变 + 仍在等待状态
                try
                {
                    if (!modeDActive || modeDWaveIndex != tokenWave || !modeDWaveCompletePending)
                    {
                        DevLog("[ModeD] 自动下一波协程已取消（模式/波次状态变化）");
                        modeDAutoNextWaveCoroutine = null;
                        yield break;
                    }
                }
                catch {}

                // P1-3 修复：使用 unscaledDeltaTime 避免时间缩放（慢动作/暂停）影响自动开波
                timer -= Time.unscaledDeltaTime;
                yield return null;
            }

            // 到时间后二次校验再开波
            if (modeDActive && modeDWaveIndex == tokenWave && modeDWaveCompletePending)
            {
                DevLog("[ModeD] 自动下一波协程触发开波");
                ModeDStartNextWave();
            }
            else
            {
                DevLog("[ModeD] 自动下一波协程跳过（状态已变化）");
            }
            modeDAutoNextWaveCoroutine = null;
        }
    }
}

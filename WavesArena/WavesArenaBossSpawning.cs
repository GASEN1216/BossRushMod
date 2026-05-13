// ============================================================================
// WavesArenaBossSpawning.cs - BossRush wave spawning and spawn verification
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 开始第一波Boss（在竞技场内）- 单波生成模式
        /// </summary>
        public void StartFirstWave()
        {
            // [DEBUG] 记录当前状态
            DevLog("[BossRush] StartFirstWave 调用: IsActive=" + IsActive + ", bossesPerWave=" + bossesPerWave + ", infiniteHellMode=" + infiniteHellMode);

            if (!IsActive)
            {
                // 记录玩家当前位置作为出生点（BossRush失败时传送回此处）
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        demoChallengeStartPosition = main.transform.position;
                        DevLog("[BossRush] 已记录玩家出生点: " + demoChallengeStartPosition);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 记录玩家出生点失败: " + e.Message);
                }

                // 每次挑战开始时随机打乱本次要挑战的敌人顺序
                try
                {
                    if (enemyPresets != null && enemyPresets.Count > 1)
                    {
                        // Fisher-Yates 洗牌
                        for (int i = enemyPresets.Count - 1; i > 0; i--)
                        {
                            int j = UnityEngine.Random.Range(0, i + 1);
                            if (j != i)
                            {
                                var tmp = enemyPresets[i];
                                enemyPresets[i] = enemyPresets[j];
                                enemyPresets[j] = tmp;
                            }
                        }

                        // 弹指可灭/有点意思模式：预处理，确保前20波不出现强力Boss
                        // 将前20位中的强力Boss与第20位之后的普通Boss交换
                        if (!infiniteHellMode)
                        {
                            EnsureEarlyWavesNoStrongBoss();
                        }

                        DevLog("[BossRush] 已随机打乱本次 BossRush 的敌人出场顺序");
                    }
                }
                catch (Exception shuffleEx)
                {
                    DevLog("[BossRush] [WARNING] 打乱敌人顺序时出错: " + shuffleEx.Message);
                }

                // 清理场景中现有的敌人，准备开始BossRush
                ClearEnemiesForBossRush();

                BeginAchievementSession(infiniteHellMode ? "InfiniteHell" : "BossRush");
                ShowMessage(L10n.T("开始BossRush挑战！", "BossRush challenge started!"));
                SetBossRushRuntimeActive(true);
                currentEnemyIndex = 0;
                defeatedEnemies = 0;

                // 使用过滤后的 Boss 池计算总数，确保横幅显示正确的 Boss 数量
                var filteredPresetsForCount = GetFilteredEnemyPresets();
                totalEnemies = (filteredPresetsForCount != null) ? filteredPresetsForCount.Count : 0;

                bossesInCurrentWaveTotal = 0;
                bossesInCurrentWaveRemaining = 0;
                currentWaveBosses.Clear();

                // 清空掉落追踪字典
                bossSpawnTimes.Clear();
                bossOriginalLootCounts.Clear();
                countedDeadBosses.Clear();

                DevLog("[BossRush] 启动单波生成模式，共 " + totalEnemies + " 个敌人（已过滤）");

                // 订阅敌人死亡事件（只订阅一次）
                Health.OnDead -= OnEnemyDiedWithDamageInfo; // 先取消避免重复
                Health.OnDead += OnEnemyDiedWithDamageInfo;

                // 立即生成第一个敌人
                SpawnNextEnemy();
            }
        }

        /// <summary>
        /// 获取安全的Boss生成位置（只修正Y轴高度，不改变XZ坐标）
        /// </summary>
        /// <remarks>
        /// 委托 SpawnPositionHelper.SnapToGround：Raycast(groundLayerMask) 优先 → NavMesh 兜底 → +0.5m 兜底。
        /// 避免 NavMesh 采样把敌人吸到非预设点（屋顶、楼梯下、墙体内的 NavMesh）。
        /// </remarks>
        private static Vector3 GetSafeBossSpawnPosition(Vector3 rawPosition)
        {
            return SpawnPositionHelper.SnapToGround(rawPosition);
        }

        /// <summary>
        /// 玩家安全距离（米）：刷怪点距玩家小于此距离时不会被选中
        /// </summary>
        private const float SPAWN_SAFE_DISTANCE = 15f;
        private const float SPAWN_SAFE_DISTANCE_SQR = SPAWN_SAFE_DISTANCE * SPAWN_SAFE_DISTANCE;

        /// <summary>
        /// 从刷怪点数组中选取距玩家最近但不在安全距离内的点
        /// <para>如果所有点都在安全距离内，回退到距玩家最远的点</para>
        /// </summary>
        /// <param name="spawnPoints">候选刷怪点数组</param>
        /// <param name="playerPos">玩家当前位置</param>
        /// <returns>经过 GetSafeBossSpawnPosition Y轴修正后的安全刷怪位置</returns>
        private static Vector3 FindNearestSafeSpawnPoint(Vector3[] spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindNearestSafeSpawnPoint(spawnPoints, playerPos, SPAWN_SAFE_DISTANCE);
        }

        /// <summary>
        /// 从刷怪点列表中选取距玩家最近但不在安全距离内的点（List版本）
        /// </summary>
        private static Vector3 FindNearestSafeSpawnPoint(List<Vector3> spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindNearestSafeSpawnPoint(spawnPoints, playerPos, SPAWN_SAFE_DISTANCE);
        }

        /// <summary>
        /// 从刷怪点数组中选取多个不在安全距离内的点，按距玩家由近到远排序
        /// <para>用于多Boss同波生成时分配不重复的安全刷怪位置</para>
        /// </summary>
        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, Vector3[] spawnPoints, Vector3 playerPos)
        {
            var result = new List<Vector3>(count);
            if (spawnPoints == null || spawnPoints.Length == 0 || count <= 0)
            {
                return result;
            }

            // 收集所有安全距离外的点，按距玩家由近到远排序
            var safeCandidates = new List<(int idx, Vector3 pos, float distSqr)>();
            var allByDistance = new List<(int idx, Vector3 pos, float distSqr)>(spawnPoints.Length);

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                float distSqr = (spawnPoints[i] - playerPos).sqrMagnitude;
                allByDistance.Add((i, spawnPoints[i], distSqr));
                if (distSqr >= SPAWN_SAFE_DISTANCE_SQR)
                {
                    safeCandidates.Add((i, spawnPoints[i], distSqr));
                }
            }

            // 按距离由近到远排序
            safeCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            var usedIndices = new HashSet<int>();

            // 优先使用安全距离外的点
            for (int i = 0; i < safeCandidates.Count && result.Count < count; i++)
            {
                result.Add(GetSafeBossSpawnPosition(safeCandidates[i].pos));
                usedIndices.Add(safeCandidates[i].idx);
            }

            // 不够的话，从未使用的点中按距离由远到近补充
            if (result.Count < count)
            {
                allByDistance.Sort((a, b) => b.distSqr.CompareTo(a.distSqr));
                for (int i = 0; i < allByDistance.Count && result.Count < count; i++)
                {
                    if (!usedIndices.Contains(allByDistance[i].idx))
                    {
                        result.Add(GetSafeBossSpawnPosition(allByDistance[i].pos));
                        usedIndices.Add(allByDistance[i].idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 从刷怪点列表中选取多个不在安全距离内的点（List版本）
        /// </summary>
        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, List<Vector3> spawnPoints, Vector3 playerPos)
        {
            var result = new List<Vector3>(count);
            if (spawnPoints == null || spawnPoints.Count == 0 || count <= 0)
            {
                return result;
            }

            var safeCandidates = new List<(int idx, Vector3 pos, float distSqr)>();
            var allByDistance = new List<(int idx, Vector3 pos, float distSqr)>(spawnPoints.Count);

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                float distSqr = (spawnPoints[i] - playerPos).sqrMagnitude;
                allByDistance.Add((i, spawnPoints[i], distSqr));
                if (distSqr >= SPAWN_SAFE_DISTANCE_SQR)
                {
                    safeCandidates.Add((i, spawnPoints[i], distSqr));
                }
            }

            safeCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            var usedIndices = new HashSet<int>();

            for (int i = 0; i < safeCandidates.Count && result.Count < count; i++)
            {
                result.Add(GetSafeBossSpawnPosition(safeCandidates[i].pos));
                usedIndices.Add(safeCandidates[i].idx);
            }

            if (result.Count < count)
            {
                allByDistance.Sort((a, b) => b.distSqr.CompareTo(a.distSqr));
                for (int i = 0; i < allByDistance.Count && result.Count < count; i++)
                {
                    if (!usedIndices.Contains(allByDistance[i].idx))
                    {
                        result.Add(GetSafeBossSpawnPosition(allByDistance[i].pos));
                        usedIndices.Add(allByDistance[i].idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 校验并修正Boss位置（生成后调用，防止Boss卡在地下）
        /// </summary>
        private void ValidateAndFixBossPosition(CharacterMainControl boss)
        {
            if (boss == null) return;

            try
            {
                Vector3 currentPos = boss.transform.position;

                bool needsRecovery = false;
                string reason = null;

                Vector3 groundAlignedPos;
                if (TryResolveGroundAlignedPosition(currentPos, 8f, 5f, out groundAlignedPos))
                {
                    if (groundAlignedPos.y - currentPos.y >= 0.75f)
                    {
                        needsRecovery = true;
                        reason = "spawn_below_ground";
                    }
                }
                else
                {
                    EnemyRecoveryState recoveryState;
                    if (enemyRecoveryStates.TryGetValue(boss, out recoveryState) &&
                        recoveryState.hasExcludedAnchorPosition &&
                        recoveryState.excludedAnchorPosition.y - currentPos.y >= 6f)
                    {
                        needsRecovery = true;
                        reason = "spawn_void";
                    }
                }

                if (!needsRecovery)
                {
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    return;
                }

                EnemyRecoveryState state;
                if (!enemyRecoveryStates.TryGetValue(boss, out state))
                {
                    state = new EnemyRecoveryState
                    {
                        lastSamplePosition = currentPos,
                        lastMovedTime = Time.time,
                        lastRecoveryTime = -4f,
                        excludedAnchorPosition = currentPos,
                        hasExcludedAnchorPosition = true,
                        continuousFallSamples = 0
                    };
                }

                Vector3 recoveredPos;
                if (TryRecoverEnemyToNearestSpawnPoint(boss, state, main, reason, out recoveredPos))
                {
                    state.lastMovedTime = Time.time;
                    state.lastRecoveryTime = Time.time;
                    state.lastSamplePosition = recoveredPos;
                    enemyRecoveryStates[boss] = state;
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ValidateAndFixBossPosition 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟校验Boss位置的协程（给地形加载留出时间）
        /// </summary>
        private IEnumerator DelayedBossPositionValidation(CharacterMainControl boss, float delay)
        {
            if (boss == null) yield break;

            yield return new WaitForSeconds(delay);

            if (boss != null && boss.gameObject != null)
            {
                ValidateAndFixBossPosition(boss);
            }
        }

        /// <summary>
        /// 生成下一个敌人（根据 bossesPerWave 支持单Boss或多Boss一波）
        /// </summary>
        private void SpawnNextEnemy()
        {
            // [DEBUG] 记录当前状态
            DevLog("[BossRush] SpawnNextEnemy 调用: bossesPerWave=" + bossesPerWave + ", currentEnemyIndex=" + currentEnemyIndex + ", totalEnemies=" + totalEnemies);

            // 通知快递员 Boss 战开始
            NotifyCourierBossFightStart();

            // 通知快递员有Boss了（不再是召唤间隔）
            NotifyCourierNoBoss(false);

            // 获取过滤后的 Boss 列表
            var filteredPresets = GetFilteredEnemyPresets();

            // 检查 Boss 池是否为空
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                ShowMessage(L10n.T("Boss池为空！请至少启用一个Boss。(Ctrl+F10 打开设置)", "Boss pool is empty! Enable at least one Boss. (Ctrl+F10 to open settings)"));
                DevLog("[BossRush] [WARNING] SpawnNextEnemy: Boss 池为空，无法生成敌人");
                return;
            }

            // 普通模式：跑完列表后直接通关
            if (!infiniteHellMode)
            {
                if (currentEnemyIndex >= filteredPresets.Count)
                {
                    // 所有敌人已击败，显示完成对话
                    OnAllEnemiesDefeated();
                    return;
                }
            }

            EnemyPresetInfo preset = null;
            if (infiniteHellMode)
            {
                // 无间炼狱：每一波按权重随机选择Boss，不再依赖 currentEnemyIndex 作为索引
                preset = PickRandomEnemyForInfiniteHell();
                if (preset == null)
                {
                    DevLog("[BossRush] [ERROR] SpawnNextEnemy: InfiniteHell 模式下未找到可用敌人预设");
                    return;
                }
            }
            else
            {
                // 弹指可灭/有点意思模式：按顺序选取Boss
                // 强力Boss已在挑战开始时预处理，前20波不会出现
                preset = filteredPresets[currentEnemyIndex];
            }

            DevLog("[BossRush] 生成第 " + (currentEnemyIndex + 1) + "/" + totalEnemies + " 波: " + preset.displayName);

            try
            {
                // 获取玩家
                CharacterMainControl playerMain = CharacterMainControl.Main;
                if (playerMain == null)
                {
                    DevLog("[BossRush] [ERROR] 玩家未找到，无法生成敌人");
                    return;
                }

                // 使用当前地图的刷新点（根据场景动态选择）
                Vector3[] spawnPoints = GetCurrentSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    DevLog("[BossRush] [ERROR] 当前地图刷新点为空，无法生成敌人");
                    return;
                }

                if (bossesPerWave <= 1)
                {
                    // 单Boss模式：每波只生成一个Boss，同样维护波次计数，便于自检逻辑使用
                    bossesInCurrentWaveTotal = 1;
                    bossesInCurrentWaveRemaining = 1;
                    currentWaveBosses.Clear();

                    Vector3 spawnPos = FindNearestSafeSpawnPoint(spawnPoints, playerMain.transform.position);

                    // 显示敌人生成横幅（在生成前显示）
                    ShowEnemyBanner(preset.displayName, spawnPos, playerMain.transform.position);

                    // 使用带验证的异步生成方法
                    SpawnBossWithVerificationAsync(preset, spawnPos, spawnPoints).Forget();
                }
                else
                {
                    // 多Boss模式：同一波生成 bossesPerWave 个相同Boss
                    bossesInCurrentWaveTotal = bossesPerWave;
                    bossesInCurrentWaveRemaining = bossesPerWave;
                    currentWaveBosses.Clear();

                    // 收集本波需要生成的所有Boss预设信息（位置由生成方法内部分配，确保不重复）
                    var bossSpawnInfos = new List<(EnemyPresetInfo preset, Vector3 position)>();

                    for (int i = 0; i < bossesPerWave; i++)
                    {
                        EnemyPresetInfo wavePreset = preset;
                        if (infiniteHellMode)
                        {
                            var altPreset = PickRandomEnemyForInfiniteHell();
                            if (altPreset != null)
                            {
                                wavePreset = altPreset;
                            }
                        }

                        // 位置占位，实际位置由 SpawnMultipleBossesWithVerificationAsync 内部分配
                        bossSpawnInfos.Add((wavePreset, Vector3.zero));
                    }

                    // 显示第一个Boss的横幅（使用第一个刷怪点作为参考）
                    if (bossSpawnInfos.Count > 0)
                    {
                        Vector3 bannerPos = GetSafeBossSpawnPosition(spawnPoints[0]);
                        ShowEnemyBanner(bossSpawnInfos[0].preset.displayName, bannerPos, playerMain.transform.position);
                    }

                    // 使用带验证和重试的批量生成方法
                    SpawnMultipleBossesWithVerificationAsync(bossSpawnInfos, spawnPoints).Forget();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 生成敌人失败: " + e.Message);
            }
        }

        /// <summary>
        /// 单Boss模式：带验证的异步生成（包含重试机制）
        /// </summary>
        private async UniTaskVoid SpawnBossWithVerificationAsync(EnemyPresetInfo preset, Vector3 position, Vector3[] spawnPoints)
        {
            const int maxRetries = 3;
            int attempt = 0;
            CharacterMainControl spawnedBoss = null;

            while (attempt < maxRetries)
            {
                attempt++;

                if (attempt > 1)
                {
                    DevLog("[BossRush] 单Boss生成重试 #" + attempt + ": " + preset.displayName);
                    // 重试时使用安全距离外最近的刷怪点
                    CharacterMainControl retryPlayer = CharacterMainControl.Main;
                    Vector3 retryPlayerPos = retryPlayer != null ? retryPlayer.transform.position : Vector3.zero;
                    position = FindNearestSafeSpawnPoint(spawnPoints, retryPlayerPos);
                }

                spawnedBoss = await SpawnEnemyAtPositionAsync(preset, position);
                if (spawnedBoss != null)
                {
                    break;
                }

                if (attempt < maxRetries)
                {
                    // 等待一小段时间后重试
                    await UniTask.Delay(200);
                }
            }

            if (spawnedBoss == null)
            {
                DevLog("[BossRush] [ERROR] 单Boss生成失败，尝试次数: " + attempt + ", preset=" + preset.displayName);
                OnBossSpawnFailed(preset);
            }
            else
            {
                DevLog("[BossRush] 单Boss生成成功: " + preset.displayName + " (尝试次数: " + attempt + ")");
            }
        }

        /// <summary>
        /// 多Boss模式：带验证和重试的批量生成
        /// 确保每个Boss使用不同的刷怪点，避免位置冲突
        /// </summary>
        private async UniTaskVoid SpawnMultipleBossesWithVerificationAsync(
            List<(EnemyPresetInfo preset, Vector3 position)> bossSpawnInfos,
            Vector3[] spawnPoints)
        {
            const int maxRetries = 3;
            int expectedCount = bossSpawnInfos.Count;

            DevLog("[BossRush] 开始批量生成 " + expectedCount + " 个Boss");

            // 重新分配刷怪点，确保每个Boss使用不同的安全位置
            CharacterMainControl multiPlayer = CharacterMainControl.Main;
            Vector3 multiPlayerPos = multiPlayer != null ? multiPlayer.transform.position : Vector3.zero;
            var assignedPositions = FindMultipleSafeSpawnPoints(expectedCount, spawnPoints, multiPlayerPos);
            for (int i = 0; i < bossSpawnInfos.Count && i < assignedPositions.Count; i++)
            {
                bossSpawnInfos[i] = (bossSpawnInfos[i].preset, assignedPositions[i]);
            }

            // 第一轮：串行生成所有Boss（避免并行时的潜在冲突）
            var results = new List<CharacterMainControl>();
            var failedInfos = new List<(EnemyPresetInfo preset, int originalIndex)>();

            for (int i = 0; i < bossSpawnInfos.Count; i++)
            {
                var info = bossSpawnInfos[i];
                CharacterMainControl spawnResult = null;

                try
                {
                    spawnResult = await SpawnEnemyAtPositionAsync(info.preset, info.position);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] Boss生成异常 #" + i + ": " + e.Message);
                }

                results.Add(spawnResult);

                if (spawnResult == null)
                {
                    failedInfos.Add((info.preset, i));
                }

                // 每个Boss生成后短暂等待，确保游戏状态稳定
                if (i < bossSpawnInfos.Count - 1)
                {
                    await UniTask.Delay(50);
                }
            }

            // 统计成功生成的数量
            int resolvedCount = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null)
                {
                    resolvedCount++;
                }
            }

            DevLog("[BossRush] 首轮生成完成: 已处理=" + resolvedCount + ", 失败=" + failedInfos.Count);

            // 重试失败的Boss
            int retryAttempt = 0;
            while (failedInfos.Count > 0 && retryAttempt < maxRetries)
            {
                retryAttempt++;
                DevLog("[BossRush] 开始重试失败的Boss (第 " + retryAttempt + " 轮), 剩余: " + failedInfos.Count);

                // 等待一小段时间后重试
                await UniTask.Delay(300);

                var stillFailed = new List<(EnemyPresetInfo preset, int originalIndex)>();

                // 为所有失败的Boss一次性分配不同的安全重试位置
                CharacterMainControl retryPlayerRef = CharacterMainControl.Main;
                Vector3 retryPlayerPos = retryPlayerRef != null ? retryPlayerRef.transform.position : Vector3.zero;
                var retryPositions = FindMultipleSafeSpawnPoints(failedInfos.Count, spawnPoints, retryPlayerPos);

                for (int ri = 0; ri < failedInfos.Count; ri++)
                {
                    var failedInfo = failedInfos[ri];
                    Vector3 newPos = ri < retryPositions.Count
                        ? retryPositions[ri]
                        : FindNearestSafeSpawnPoint(spawnPoints, retryPlayerPos);

                    CharacterMainControl retryResult = null;
                    try
                    {
                        retryResult = await SpawnEnemyAtPositionAsync(failedInfo.preset, newPos);
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] Boss重试生成异常: " + e.Message);
                    }

                    if (retryResult != null)
                    {
                        resolvedCount++;
                        DevLog("[BossRush] 重试成功: " + failedInfo.preset.displayName);
                    }
                    else
                    {
                        stillFailed.Add(failedInfo);
                    }

                    // 每次重试后短暂等待
                    await UniTask.Delay(100);
                }

                failedInfos = stillFailed;
                DevLog("[BossRush] 重试轮 " + retryAttempt + " 完成: 当前已处理总数=" + resolvedCount + ", 仍失败=" + failedInfos.Count);
            }

            // 最终验证
            int finalFailCount = expectedCount - resolvedCount;
            if (finalFailCount > 0)
            {
                DevLog("[BossRush] [WARNING] 最终有 " + finalFailCount + " 个Boss生成失败，修正波次计数");
                int liveBossCount = PruneAndCountTrackedWaveBosses();

                // 修正波次计数，remaining 必须与当前仍存活并被追踪的 Boss 数量一致，避免卡波
                bossesInCurrentWaveTotal = liveBossCount;
                bossesInCurrentWaveRemaining = liveBossCount;

                // 如果全部失败，直接推进下一波
                if (liveBossCount <= 0)
                {
                    DevLog("[BossRush] [ERROR] 本波所有Boss生成失败，跳过本波");
                    ProceedAfterWaveFinished();
                }
            }
            else
            {
                DevLog("[BossRush] 批量生成完成: 本波 " + expectedCount + " 个目标Boss已全部处理");
            }
        }

        private int PruneAndCountTrackedWaveBosses()
        {
            if (currentWaveBosses == null || currentWaveBosses.Count == 0)
            {
                return 0;
            }

            int liveBossCount = 0;
            for (int i = currentWaveBosses.Count - 1; i >= 0; i--)
            {
                MonoBehaviour boss = currentWaveBosses[i];
                if (boss == null)
                {
                    currentWaveBosses.RemoveAt(i);
                    continue;
                }

                liveBossCount++;
            }

            return liveBossCount;
        }

        /// <summary>
        /// 获取当前地图的刷新点数组（使用 BossRushMapConfig 配置系统）
        /// </summary>
        private Vector3[] GetCurrentSpawnPoints()
        {
            // 使用配置系统获取当前场景的刷新点
            return GetCurrentSceneSpawnPoints();
        }


    }
}

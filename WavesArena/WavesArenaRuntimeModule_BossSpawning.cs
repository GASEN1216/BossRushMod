using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private static Vector3 GetSafeBossSpawnPosition(Vector3 rawPosition)
        {
            return SpawnPositionHelper.SnapToGround(rawPosition);
        }

        private static Vector3 FindNearestSafeSpawnPoint(Vector3[] spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindNearestSafeSpawnPoint(spawnPoints, playerPos, SpawnPositionHelper.DefaultSafeDistance);
        }

        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, Vector3[] spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindMultipleSafeSpawnPoints(count, spawnPoints, playerPos, SpawnPositionHelper.DefaultSafeDistance);
        }

        /// <summary>
        /// 生成下一个敌人（根据 BossesPerWave 支持单Boss或多Boss一波）
        /// </summary>
        internal void SpawnNextEnemy()
        {
            // [DEBUG] 记录当前状态
            ModBehaviour.DevLog("[BossRush] SpawnNextEnemy 调用: BossesPerWave=" + BossesPerWave + ", CurrentEnemyIndex=" + CurrentEnemyIndex + ", TotalEnemies=" + TotalEnemies);

            // 通知快递员 Boss 战开始
            owner.NotifyCourierBossFightStart();

            // 通知快递员有Boss了（不再是召唤间隔）
            owner.NotifyCourierNoBoss(false);

            // 获取过滤后的 Boss 列表
            var filteredPresets = owner.GetFilteredEnemyPresets();

            // 检查 Boss 池是否为空
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                owner.ShowMessage(L10n.T("Boss池为空！请至少启用一个Boss。(Ctrl+F10 打开设置)", "Boss pool is empty! Enable at least one Boss. (Ctrl+F10 to open settings)"));
                ModBehaviour.DevLog("[BossRush] [WARNING] SpawnNextEnemy: Boss 池为空，无法生成敌人");
                return;
            }

            // 普通模式：跑完列表后直接通关
            if (!InfiniteHellMode)
            {
                if (CurrentEnemyIndex >= filteredPresets.Count)
                {
                    // 所有敌人已击败，显示完成对话
                    owner.OnAllEnemiesDefeatedForArena();
                    return;
                }
            }

            EnemyPresetInfo preset = null;
            if (InfiniteHellMode)
            {
                // 无间炼狱：每一波按权重随机选择Boss，不再依赖 CurrentEnemyIndex 作为索引
                preset = PickRandomEnemyForInfiniteHell();
                if (preset == null)
                {
                    ModBehaviour.DevLog("[BossRush] [ERROR] SpawnNextEnemy: InfiniteHell 模式下未找到可用敌人预设");
                    return;
                }
            }
            else
            {
                // 弹指可灭/有点意思模式：按顺序选取Boss
                // 强力Boss已在挑战开始时预处理，前20波不会出现
                preset = filteredPresets[CurrentEnemyIndex];
            }

            ModBehaviour.DevLog("[BossRush] 生成第 " + (CurrentEnemyIndex + 1) + "/" + TotalEnemies + " 波: " + preset.displayName);

            try
            {
                // 获取玩家
                CharacterMainControl playerMain = CharacterMainControl.Main;
                if (playerMain == null)
                {
                    ModBehaviour.DevLog("[BossRush] [ERROR] 玩家未找到，无法生成敌人");
                    return;
                }

                // 使用当前地图的刷新点（根据场景动态选择）
                Vector3[] spawnPoints = GetCurrentSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    ModBehaviour.DevLog("[BossRush] [ERROR] 当前地图刷新点为空，无法生成敌人");
                    return;
                }

                Func<bool> isSpawnCurrent = WavesArenaRuntimeModule.CaptureValidity(owner, true, true);
                if (BossesPerWave <= 1)
                {
                    // 单Boss模式：每波只生成一个Boss，同样维护波次计数，便于自检逻辑使用
                    BossesInCurrentWaveTotal = 1;
                    BossesInCurrentWaveRemaining = 1;
                    CurrentWaveBosses.Clear();

                    Vector3 spawnPos = FindNearestSafeSpawnPoint(spawnPoints, playerMain.transform.position);

                    // 显示敌人生成横幅（在生成前显示）
                    owner.ShowEnemyBannerForArena(preset.displayName, spawnPos, playerMain.transform.position);

                    // 使用带验证的异步生成方法
                    SpawnBossWithVerificationAsync(preset, spawnPos, spawnPoints, isSpawnCurrent).Forget();
                }
                else
                {
                    // 多Boss模式：同一波生成 BossesPerWave 个相同Boss
                    BossesInCurrentWaveTotal = BossesPerWave;
                    BossesInCurrentWaveRemaining = BossesPerWave;
                    CurrentWaveBosses.Clear();

                    // 收集本波需要生成的所有Boss预设信息（位置由生成方法内部分配，确保不重复）
                    var bossSpawnInfos = new List<(EnemyPresetInfo preset, Vector3 position)>();

                    for (int i = 0; i < BossesPerWave; i++)
                    {
                        EnemyPresetInfo wavePreset = preset;
                        if (InfiniteHellMode)
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
                        owner.ShowEnemyBannerForArena(bossSpawnInfos[0].preset.displayName, bannerPos, playerMain.transform.position);
                    }

                    // 使用带验证和重试的批量生成方法
                    SpawnMultipleBossesWithVerificationAsync(bossSpawnInfos, spawnPoints, isSpawnCurrent).Forget();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] 生成敌人失败: " + e.Message);
            }
        }

        /// <summary>
        /// 单Boss模式：带验证的异步生成（包含重试机制）
        /// </summary>
        private async UniTaskVoid SpawnBossWithVerificationAsync(EnemyPresetInfo preset, Vector3 position, Vector3[] spawnPoints, Func<bool> isSpawnCurrent)
        {
            const int maxRetries = 3;
            int attempt = 0;
            CharacterMainControl spawnedBoss = null;

            while (attempt < maxRetries)
            {
                if (!isSpawnCurrent()) return;
                attempt++;

                if (attempt > 1)
                {
                    ModBehaviour.DevLog("[BossRush] 单Boss生成重试 #" + attempt + ": " + preset.displayName);
                    // 重试时使用安全距离外最近的刷怪点
                    CharacterMainControl retryPlayer = CharacterMainControl.Main;
                    Vector3 retryPlayerPos = retryPlayer != null ? retryPlayer.transform.position : Vector3.zero;
                    position = FindNearestSafeSpawnPoint(spawnPoints, retryPlayerPos);
                }

                spawnedBoss = await owner.SpawnEnemyAtPositionForArenaAsync(preset, position, isSpawnCurrent);
                if (!isSpawnCurrent()) return;
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
                ModBehaviour.DevLog("[BossRush] [ERROR] 单Boss生成失败，尝试次数: " + attempt + ", preset=" + preset.displayName);
                owner.OnBossSpawnFailedForArena(preset);
            }
            else
            {
                ModBehaviour.DevLog("[BossRush] 单Boss生成成功: " + preset.displayName + " (尝试次数: " + attempt + ")");
            }
        }

        /// <summary>
        /// 多Boss模式：带验证和重试的批量生成
        /// 确保每个Boss使用不同的刷怪点，避免位置冲突
        /// </summary>
        private async UniTaskVoid SpawnMultipleBossesWithVerificationAsync(
            List<(EnemyPresetInfo preset, Vector3 position)> bossSpawnInfos,
            Vector3[] spawnPoints, Func<bool> isSpawnCurrent)
        {
            const int maxRetries = 3;
            int expectedCount = bossSpawnInfos.Count;

            ModBehaviour.DevLog("[BossRush] 开始批量生成 " + expectedCount + " 个Boss");

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
                if (!isSpawnCurrent()) return;
                var info = bossSpawnInfos[i];
                CharacterMainControl spawnResult = null;

                try
                {
                    spawnResult = await owner.SpawnEnemyAtPositionForArenaAsync(info.preset, info.position, isSpawnCurrent);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] Boss生成异常 #" + i + ": " + e.Message);
                }

                if (!isSpawnCurrent()) return;
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

            ModBehaviour.DevLog("[BossRush] 首轮生成完成: 已处理=" + resolvedCount + ", 失败=" + failedInfos.Count);

            // 重试失败的Boss
            int retryAttempt = 0;
            while (failedInfos.Count > 0 && retryAttempt < maxRetries)
            {
                retryAttempt++;
                ModBehaviour.DevLog("[BossRush] 开始重试失败的Boss (第 " + retryAttempt + " 轮), 剩余: " + failedInfos.Count);

                // 等待一小段时间后重试
                await UniTask.Delay(300);
                if (!isSpawnCurrent()) return;

                var stillFailed = new List<(EnemyPresetInfo preset, int originalIndex)>();

                // 为所有失败的Boss一次性分配不同的安全重试位置
                CharacterMainControl retryPlayerRef = CharacterMainControl.Main;
                Vector3 retryPlayerPos = retryPlayerRef != null ? retryPlayerRef.transform.position : Vector3.zero;
                var retryPositions = FindMultipleSafeSpawnPoints(failedInfos.Count, spawnPoints, retryPlayerPos);

                for (int ri = 0; ri < failedInfos.Count; ri++)
                {
                    if (!isSpawnCurrent()) return;
                    var failedInfo = failedInfos[ri];
                    Vector3 newPos = ri < retryPositions.Count
                        ? retryPositions[ri]
                        : FindNearestSafeSpawnPoint(spawnPoints, retryPlayerPos);

                    CharacterMainControl retryResult = null;
                    try
                    {
                        retryResult = await owner.SpawnEnemyAtPositionForArenaAsync(failedInfo.preset, newPos, isSpawnCurrent);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[BossRush] Boss重试生成异常: " + e.Message);
                    }

                    if (!isSpawnCurrent()) return;
                    if (retryResult != null)
                    {
                        resolvedCount++;
                        ModBehaviour.DevLog("[BossRush] 重试成功: " + failedInfo.preset.displayName);
                    }
                    else
                    {
                        stillFailed.Add(failedInfo);
                    }

                    // 每次重试后短暂等待
                    await UniTask.Delay(100);
                }

                failedInfos = stillFailed;
                ModBehaviour.DevLog("[BossRush] 重试轮 " + retryAttempt + " 完成: 当前已处理总数=" + resolvedCount + ", 仍失败=" + failedInfos.Count);
            }

            if (!isSpawnCurrent()) return;
            // 最终验证
            int finalFailCount = expectedCount - resolvedCount;
            if (finalFailCount > 0)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 最终有 " + finalFailCount + " 个Boss生成失败，修正波次计数");
                int liveBossCount = PruneAndCountTrackedWaveBosses();

                // 修正波次计数，remaining 必须与当前仍存活并被追踪的 Boss 数量一致，避免卡波
                BossesInCurrentWaveTotal = liveBossCount;
                BossesInCurrentWaveRemaining = liveBossCount;

                // 如果全部失败，直接推进下一波
                if (liveBossCount <= 0)
                {
                    ModBehaviour.DevLog("[BossRush] [ERROR] 本波所有Boss生成失败，跳过本波");
                    owner.ProceedAfterWaveFinished();
                }
            }
            else
            {
                ModBehaviour.DevLog("[BossRush] 批量生成完成: 本波 " + expectedCount + " 个目标Boss已全部处理");
            }
        }

        private int PruneAndCountTrackedWaveBosses()
        {
            if (CurrentWaveBosses == null || CurrentWaveBosses.Count == 0)
            {
                return 0;
            }

            int liveBossCount = 0;
            for (int i = CurrentWaveBosses.Count - 1; i >= 0; i--)
            {
                MonoBehaviour boss = CurrentWaveBosses[i];
                if (boss == null)
                {
                    CurrentWaveBosses.RemoveAt(i);
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
            return owner.GetCurrentSceneSpawnPoints();
        }
    }
}

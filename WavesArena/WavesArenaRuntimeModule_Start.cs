using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        public void StartFirstWave()
        {
            // [DEBUG] 记录当前状态
            ModBehaviour.DevLog("[BossRush] StartFirstWave 调用: IsActive=" + owner.IsActive + ", bossesPerWave=" + BossesPerWave + ", infiniteHellMode=" + InfiniteHellMode);

            if (!owner.IsActive)
            {
                var availablePresets = owner.GetFilteredEnemyPresets();
                if (availablePresets == null || availablePresets.Count == 0)
                {
                    owner.ShowMessage(L10n.T("Boss池为空！请至少启用一个Boss。(Ctrl+F10 打开设置)", "Boss pool is empty! Enable at least one Boss. (Ctrl+F10 to open settings)"));
                    return;
                }
                // 记录玩家当前位置作为出生点（BossRush失败时传送回此处）
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        DemoChallengeStartPosition = main.transform.position;
                        ModBehaviour.DevLog("[BossRush] 已记录玩家出生点: " + DemoChallengeStartPosition);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 记录玩家出生点失败: " + e.Message);
                }

                // 每次挑战开始时随机打乱本次要挑战的敌人顺序
                try
                {
                    if (EnemyPresets != null && EnemyPresets.Count > 1)
                    {
                        // Fisher-Yates 洗牌
                        for (int i = EnemyPresets.Count - 1; i > 0; i--)
                        {
                            int j = UnityEngine.Random.Range(0, i + 1);
                            if (j != i)
                            {
                                var tmp = EnemyPresets[i];
                                EnemyPresets[i] = EnemyPresets[j];
                                EnemyPresets[j] = tmp;
                            }
                        }

                        // 弹指可灭/有点意思模式：预处理，确保前20波不出现强力Boss
                        // 将前20位中的强力Boss与第20位之后的普通Boss交换
                        if (!InfiniteHellMode)
                        {
                            EnsureEarlyWavesNoStrongBoss();
                        }

                        ModBehaviour.DevLog("[BossRush] 已随机打乱本次 BossRush 的敌人出场顺序");
                    }
                }
                catch (Exception shuffleEx)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 打乱敌人顺序时出错: " + shuffleEx.Message);
                }

                // 清理场景中现有的敌人，准备开始BossRush
                owner.ClearEnemiesForBossRushForArena();

                owner.BeginAchievementSessionForArena(InfiniteHellMode ? "InfiniteHell" : "BossRush");
                owner.ShowMessage(L10n.T("开始BossRush挑战！", "BossRush challenge started!"));
                owner.SetBossRushRuntimeActiveForArena(true);
                WavesArenaRuntimeModule.ResetMilestones(owner);

                // 抽取并应用本局变异词条（必须在生成第一个敌人之前，敌人增益才能作用到首波）
                owner.TryRollMutatorsForArena(InfiniteHellMode ? "InfiniteHell" : "BossRush");

                CurrentEnemyIndex = 0;
                DefeatedEnemies = 0;

                // 使用过滤后的 Boss 池计算总数，确保横幅显示正确的 Boss 数量
                var filteredPresetsForCount = owner.GetFilteredEnemyPresets();
                TotalEnemies = (filteredPresetsForCount != null) ? filteredPresetsForCount.Count : 0;

                BossesInCurrentWaveTotal = 0;
                BossesInCurrentWaveRemaining = 0;
                CurrentWaveBosses.Clear();

                // 清空掉落追踪字典
                bossSpawnTimes.Clear();
                bossOriginalLootCounts.Clear();
                CountedDeadBosses.Clear();

                ModBehaviour.DevLog("[BossRush] 启动单波生成模式，共 " + TotalEnemies + " 个敌人（已过滤）");

                // 订阅敌人死亡事件（只订阅一次）
                owner.SubscribeArenaBossDeaths();

                // 立即生成第一个敌人
                SpawnNextEnemy();
            }
        }

    }
}

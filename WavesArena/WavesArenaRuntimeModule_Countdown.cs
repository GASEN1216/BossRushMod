using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        public void StartNextWaveCountdown(bool showInitialBanner = true, bool suppressImmediateRepeatBanner = false)
        {
            float interval = owner.GetWaveIntervalSeconds();
            bool milestoneBonusApplied = false;

            // 每5波额外休息时间
            float milestoneBonus = owner.GetMilestoneRestBonusSeconds();
            if (milestoneBonus > 0f)
            {
                // 模式A/B: CurrentEnemyIndex 已在 ProceedAfterWaveFinished 中自增，代表已完成波数
                // 模式C: InfiniteHellWaveIndex 已在 OnInfiniteHellWaveCompleted 中自增，代表已完成波数
                int completedWave = InfiniteHellMode ? InfiniteHellWaveIndex : CurrentEnemyIndex;
                if (completedWave > 0 && completedWave % 5 == 0)
                {
                    interval += milestoneBonus;
                    milestoneBonusApplied = true;
                    ModBehaviour.DevLog("[BossRush] 第 " + completedWave + " 波完成，额外休息 " + milestoneBonus + " 秒");
                }
            }

            if (!InfiniteHellMode)
            {
                try
                {
                    NextWaveBossName = null;
                    // 使用过滤后的 Boss 列表，确保预告的 Boss 与实际生成的一致
                    var filteredPresets = owner.GetFilteredEnemyPresets();
                    int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;
                    if (CurrentEnemyIndex >= 0 && CurrentEnemyIndex < presetCount)
                    {
                        EnemyPresetInfo nextPreset = filteredPresets[CurrentEnemyIndex];
                        if (nextPreset != null)
                        {
                            NextWaveBossName = nextPreset.displayName;
                        }
                    }
                }
                catch
                {
                    NextWaveBossName = null;
                }
            }
            else
            {
                NextWaveBossName = null;
            }
            if (interval <= 0f)
            {
                WaitingForNextWave = false;
                LastWaveCountdownSeconds = -1;
                SpawnNextEnemy();
                return;
            }

            // 重置上一轮倒计时状态
            WaitingForNextWave = true;
            WaveCountdown = interval;
            int secondsInt = Mathf.RoundToInt(interval);
            if (secondsInt < 1)
            {
                secondsInt = 1;
            }

            if (showInitialBanner && (interval <= 5f || milestoneBonusApplied))
            {
                ShowNextWaveCountdownBanner(secondsInt);
                LastWaveCountdownSeconds = secondsInt;
            }
            else if (suppressImmediateRepeatBanner)
            {
                LastWaveCountdownSeconds = secondsInt;
            }
            else
            {
                LastWaveCountdownSeconds = -1;
            }
        }

        internal void ShowNextWaveCountdownBanner(int secondsInt)
        {
            if (secondsInt < 1)
            {
                secondsInt = 1;
            }

            if (!InfiniteHellMode && !string.IsNullOrEmpty(NextWaveBossName))
            {
                owner.ShowBigBanner(L10n.T(
                    ModBehaviour.RichDangerTag + NextWaveBossName + "</color> 将在 " + ModBehaviour.RichWarningTag + secondsInt + "</color> 秒后抵达战场...",
                    ModBehaviour.RichDangerTag + NextWaveBossName + "</color> arriving in " + ModBehaviour.RichWarningTag + secondsInt + "</color> seconds..."
                ));
            }
            else
            {
                owner.ShowBigBanner(L10n.T(
                    "下一波将在 " + ModBehaviour.RichWarningTag + secondsInt + "</color> 秒后开始...",
                    "Next wave in " + ModBehaviour.RichWarningTag + secondsInt + "</color> seconds..."
                ));
            }
        }

    }
}

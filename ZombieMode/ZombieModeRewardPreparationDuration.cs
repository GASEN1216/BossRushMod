using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const int ZombieModePreparationDurationMinimumSeconds = 15;
        private const int ZombieModePreparationDurationMaximumSeconds = 300;
        private static readonly int[] ZombieModePreparationDurationOptions =
        {
            15, 30, 45, 60, 75,
            90, 105, 120, 135, 150,
            165, 180, 195, 210, 225,
            240, 255, 270, 285, 300
        };

        public IList<int> GetZombieModePreparationDurationOptions(int runId)
        {
            return ZombieModePreparationDurationOptions;
        }

        public int GetZombieModeSelectedPreparationDuration(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return Mathf.RoundToInt(ZombieModeTuning.PreparationCountdownSeconds);
            }

            if (zombieModeRunState.SelectedPreparationDurationSeconds > 0)
            {
                return Mathf.Clamp(
                    zombieModeRunState.SelectedPreparationDurationSeconds,
                    ZombieModePreparationDurationMinimumSeconds,
                    ZombieModePreparationDurationMaximumSeconds);
            }

            return Mathf.RoundToInt(ZombieModeTuning.PreparationCountdownSeconds);
        }

        public void OpenZombieModePreparationDurationEditor(int runId)
        {
            if (!IsZombieModeRunValid(runId) ||
                zombieModeRunState.CombatPhase != ZombieModeCombatPhase.RewardSelection ||
                zombieModeRunState.CurrentRewardNode == null)
            {
                return;
            }

            ShowZombieModeRewardSelection(runId, zombieModeRunState.CurrentRewardNode.BossNode, true);
        }

        public void SetZombieModePreparationDuration(int runId, int seconds)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.RewardSelection)
            {
                return;
            }

            for (int i = 0; i < ZombieModePreparationDurationOptions.Length; i++)
            {
                if (ZombieModePreparationDurationOptions[i] != seconds)
                {
                    continue;
                }

                zombieModeRunState.SelectedPreparationDurationSeconds = seconds;
                bool bossNode = zombieModeRunState.CurrentRewardNode != null && zombieModeRunState.CurrentRewardNode.BossNode;
                ShowZombieModeRewardSelection(runId, bossNode, false);
                return;
            }
        }
    }
}

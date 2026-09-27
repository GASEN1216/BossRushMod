using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private EnemyRecoveryMonitor enemyRecoveryMonitor;

        internal void BindEnemyRecoveryMonitor(EnemyRecoveryMonitor monitor)
        {
            enemyRecoveryMonitor = monitor;
        }

        internal void MonitorNormalBossRushRecovery(CharacterMainControl player)
        {
            if (BossesPerWave > 1)
            {
                for (int i = CurrentWaveBosses.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = CurrentWaveBosses[i] as CharacterMainControl;
                    enemyRecoveryMonitor.MonitorEnemyRecovery(enemy, player);
                }

                return;
            }

            CharacterMainControl singleBoss = CurrentBoss as CharacterMainControl;
            enemyRecoveryMonitor.MonitorEnemyRecovery(singleBoss, player);
        }
    }
}

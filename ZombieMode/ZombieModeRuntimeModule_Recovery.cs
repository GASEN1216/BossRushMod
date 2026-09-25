using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private EnemyRecoveryMonitor enemyRecoveryMonitor;

        internal void BindEnemyRecoveryMonitor(EnemyRecoveryMonitor monitor)
        {
            enemyRecoveryMonitor = monitor;
        }

        internal void MonitorZombieModeEnemyRecovery(CharacterMainControl player)
        {
            if (runState == null || runState.RunOnlyObjects.Count <= 0)
            {
                return;
            }

            for (int i = runState.RunOnlyObjects.Count - 1; i >= 0; i--)
            {
                ZombieModeRunOnlyRecord record = runState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null && record.GameObject != null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (marker != null)
                    {
                        record.Target = marker;
                    }
                }

                CharacterMainControl enemy = marker != null ? marker.Owner : null;
                if (enemy == null)
                {
                    enemy = record.GameObject.GetComponent<CharacterMainControl>();
                    if (enemy == null)
                    {
                        enemy = record.GameObject.GetComponentInChildren<CharacterMainControl>(true);
                    }

                    if (marker != null)
                    {
                        marker.Owner = enemy;
                    }
                }

                enemyRecoveryMonitor.MonitorEnemyRecovery(enemy, player, marker);
            }
        }

        internal void AppendZombieModeRecoverySpawnCandidates(List<Vector3> candidates, ref bool prevalidated)
        {
            if (runState == null)
            {
                return;
            }

            List<ZombieModeSpawnPoint> effectivePoints = runState.EffectiveSpawnPoints;
            if (effectivePoints != null && effectivePoints.Count > 0)
            {
                for (int i = 0; i < effectivePoints.Count; i++)
                {
                    candidates.Add(effectivePoints[i].Position);
                }
                prevalidated = false;
                return;
            }

            List<ZombieModeSpawnPoint> spawnPoints = runState.SpawnPoints;
            if (spawnPoints == null)
            {
                return;
            }

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                candidates.Add(spawnPoints[i].Position);
            }

            prevalidated = false;
        }
    }
}

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        internal void ResolveModeDSpawnCount(int waveToken)
        {
            if (modeDActive && modeDWaveIndex == waveToken)
            {
                modeDSpawnResolvedInCurrentWave++;
                ModBehaviour.DevLog("[ModeD] 生成结案: resolved=" + modeDSpawnResolvedInCurrentWave + "/" + modeDExpectedEnemiesInCurrentWave);
                TryResolveModeDWaveComplete();
            }
            else
            {
                ModBehaviour.DevLog("[ModeD] 生成任务完成但波次已变化(waveToken=" + waveToken + ", current=" + modeDWaveIndex + ")，跳过结案计数");
            }
        }

        internal void TryResolveModeDWaveComplete()
        {
            if (modeDWaveCompletePending) return;

            CleanupDeadEnemies();

            // 还有存活敌人，肯定不能结算
            if (modeDCurrentWaveEnemies.Count > 0)
            {
                ModBehaviour.DevLog("[ModeD] TryResolve: 还有 " + modeDCurrentWaveEnemies.Count + " 个存活敌人");
                return;
            }

            // 生成还没全部结案，不能结算（防止"迟到的怪"）
            if (modeDSpawnResolvedInCurrentWave < modeDExpectedEnemiesInCurrentWave)
            {
                ModBehaviour.DevLog("[ModeD] TryResolve: 生成未全部结案 (" + modeDSpawnResolvedInCurrentWave + "/" + modeDExpectedEnemiesInCurrentWave + ")");
                return;
            }

            ModBehaviour.DevLog("[ModeD] TryResolve: 满足波次完成条件，触发 OnModeDWaveComplete");
            OnModeDWaveComplete();
        }

        internal void CleanupDeadEnemies()
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
    }
}

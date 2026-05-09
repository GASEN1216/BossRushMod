using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal bool TickWavesArenaRuntime(float deltaTime)
        {
            // 单波模式倒计时
            if (waitingForNextWave && waveCountdown > 0f)
            {
                // 如果 BossRush 已经结束（例如通关、玩家死亡等），则立即停止倒计时，防止继续刷"下一波将在 X 秒后开始"
                if (!IsActive && !bossRushArenaActive)
                {
                    waitingForNextWave = false;
                    waveCountdown = 0f;
                    lastWaveCountdownSeconds = -1;
                    return true;
                }

                waveCountdown -= deltaTime;

                float interval = GetWaveIntervalSeconds();

                // 显示倒计时（每秒更新一次）：仅大横幅
                if (interval > 5f)
                {
                    int seconds = Mathf.CeilToInt(waveCountdown);
                    if (seconds != lastWaveCountdownSeconds && seconds > 0)
                    {
                        lastWaveCountdownSeconds = seconds;

                        if (seconds % 5 == 0)
                        {
                            ShowNextWaveCountdownBanner(seconds);
                        }
                    }
                }

                if (waveCountdown <= 0f)
                {
                    waitingForNextWave = false;
                    lastWaveCountdownSeconds = -1;
                    SpawnNextEnemy();
                }
            }

            // 波次完整性自检：每隔一段时间检查当前波是否出现"没有任何存活Boss但计数未清零"的异常
            if (IsActive)
            {
                if (!modeDActive)
                {
                    waveIntegrityCheckTimer += deltaTime;
                    if (waveIntegrityCheckTimer >= WaveIntegrityCheckInterval)
                    {
                        waveIntegrityCheckTimer = 0f;
                        TryFixStuckWaveIfNoBossAlive();
                    }
                }
            }
            else
            {
                waveIntegrityCheckTimer = 0f;
            }

            return false;
        }

        internal void TickWavesArenaBossCleanupRuntime(float deltaTime)
        {
            // BossRush / 丧尸模式期间，定期清理任何非模式召唤的"大兴兴"Boss
            // （DEMO 地图原生刷怪器可能在 DisableAllSpawners 之后仍有残留实例）
            if (IsActive || bossRushArenaActive || IsZombieModeActive)
            {
                daXingXingCleanTimer += deltaTime;
                if (daXingXingCleanTimer >= DaXingXingCleanInterval)
                {
                    daXingXingCleanTimer = 0f;
                    TryCleanNonBossRushDaXingXing();
                }
            }
            else
            {
                daXingXingCleanTimer = 0f;
            }
        }
    }
}

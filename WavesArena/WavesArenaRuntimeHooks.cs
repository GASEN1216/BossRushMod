using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal bool TickWavesArenaRuntime(float deltaTime)
        {
            // Mode G 门控（加法分支）：Mode G Starting/Active/Rewarding/Exiting 时
            // 冻结并清零 Legacy 波次倒计时，绝不调用 SpawnNextEnemy（含波次完整性自检路径）。
            // 查询 no-throw、默认 false；未运行时本分支不命中，后续逻辑逐字不变。
            // 返回 false 保证后续 TickWavesArenaBossCleanupRuntime（大兴兴 owner-aware 清理）继续运行。
            if (IsModeGRunInProgressSafe())
            {
                if (waitingForNextWave || waveCountdown > 0f || lastWaveCountdownSeconds >= 0)
                {
                    waitingForNextWave = false;
                    waveCountdown = 0f;
                    lastWaveCountdownSeconds = -1;
                }
                waveIntegrityCheckTimer = 0f;
                return false;
            }

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

        /// <summary>
        /// Mode G 运行状态的全 partial 共享 no-throw 读取（异常视为未运行，保持 Legacy 行为）。
        /// 只反映 lifecycle（LifecyclePhase != None），绝不包含 late sink quarantine。
        /// </summary>
        internal static bool IsModeGRunInProgressSafe()
        {
            try
            {
                return ModeGRuntimeGates.IsModeGRunInProgress;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mode H 是否正在进行（no-throw，未运行时恒 false）。
        /// 供 Legacy 清怪循环等旧路径做加法分支使用（设计提案 §19.2）。
        /// </summary>
        internal static bool IsModeHRunInProgressSafe()
        {
            try
            {
                return ModeHRuntimeGates.IsModeHRunOwnerActive;
            }
            catch
            {
                return false;
            }
        }
    }
}

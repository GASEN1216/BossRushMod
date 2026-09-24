using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal bool TickWavesArenaRuntime(float deltaTime)
        {
            // Mode G 门控（加法分支）：Mode G Starting/Active/Rewarding/Exiting 时
            // 冻结并清零 Legacy 波次倒计时，绝不调用 SpawnNextEnemy（含波次完整性自检路径）。
            // 查询 no-throw、默认 false；未运行时本分支不命中，后续逻辑逐字不变。
            // 返回 false 保证后续 TickWavesArenaBossCleanupRuntime（大兴兴 owner-aware 清理）继续运行。
            if (ModBehaviour.IsModeGRunInProgressSafe())
            {
                if (WaitingForNextWave || WaveCountdown > 0f || LastWaveCountdownSeconds >= 0)
                {
                    WaitingForNextWave = false;
                    WaveCountdown = 0f;
                    LastWaveCountdownSeconds = -1;
                }
                WaveIntegrityCheckTimer = 0f;
                return false;
            }

            // 单波模式倒计时
            if (WaitingForNextWave && WaveCountdown > 0f)
            {
                // 如果 BossRush 已经结束（例如通关、玩家死亡等），则立即停止倒计时，防止继续刷"下一波将在 X 秒后开始"
                if (!owner.IsActive && !owner.IsBossRushArenaActive)
                {
                    WaitingForNextWave = false;
                    WaveCountdown = 0f;
                    LastWaveCountdownSeconds = -1;
                    return true;
                }

                WaveCountdown -= deltaTime;

                float interval = owner.GetWaveIntervalSeconds();

                // 显示倒计时（每秒更新一次）：仅大横幅
                if (interval > 5f)
                {
                    int seconds = Mathf.CeilToInt(WaveCountdown);
                    if (seconds != LastWaveCountdownSeconds && seconds > 0)
                    {
                        bool firstTick = LastWaveCountdownSeconds < 0;
                        LastWaveCountdownSeconds = seconds;

                        // 只在倒计时开始与剩 3 秒各推一条：旧版每 5 秒一条，一段 15 秒休整连推 3 条（审美审查 UB-07）
                        if (firstTick || seconds == 3)
                        {
                            owner.ShowNextWaveCountdownBanner(seconds);
                        }
                    }
                }

                if (WaveCountdown <= 0f)
                {
                    WaitingForNextWave = false;
                    LastWaveCountdownSeconds = -1;
                    owner.SpawnNextEnemy();
                }
            }

            // 波次完整性自检：每隔一段时间检查当前波是否出现"没有任何存活Boss但计数未清零"的异常
            if (owner.IsActive)
            {
                if (!owner.IsModeDActive)
                {
                    WaveIntegrityCheckTimer += deltaTime;
                    if (WaveIntegrityCheckTimer >= ModBehaviour.WaveIntegrityCheckInterval)
                    {
                        WaveIntegrityCheckTimer = 0f;
                        owner.TryFixStuckWaveIfNoBossAlive();
                    }
                }
            }
            else
            {
                WaveIntegrityCheckTimer = 0f;
            }

            return false;
        }

        internal void TickWavesArenaBossCleanupRuntime(float deltaTime)
        {
            // BossRush / 丧尸模式期间，定期清理任何非模式召唤的"大兴兴"Boss
            // （DEMO 地图原生刷怪器可能在 DisableAllSpawners 之后仍有残留实例）
            if (owner.IsActive || owner.IsBossRushArenaActive || owner.IsZombieModeActive)
            {
                DaXingXingCleanTimer += deltaTime;
                if (DaXingXingCleanTimer >= ModBehaviour.DaXingXingCleanInterval)
                {
                    DaXingXingCleanTimer = 0f;
                    owner.TryCleanNonBossRushDaXingXing();
                }
            }
            else
            {
                DaXingXingCleanTimer = 0f;
            }
        }

    }
}

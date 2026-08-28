namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void RegisterPlayerLifecycleRuntimeEvents()
        {
            Health.OnDead += OnPlayerDeathInBossRush;
            Health.OnHurt += PrimeDeathWraithData_DeathWraith;
            Health.OnDead += RecordDeathWraithData_DeathWraith;

            // 日报战绩采集。命名 handler 而不是 lambda：AGENTS.md 4.6 要求能对称退订。
            // handler 内部自带开关门控，关闭时零成本早返。
            Health.OnDead += DailyReportStatsCollector.OnGlobalDead;
            Health.OnHurt += DailyReportStatsCollector.OnGlobalHurt;
        }

        internal void CleanupPlayerLifecycleRuntimeEvents()
        {
            Health.OnDead -= OnPlayerDeathInBossRush;
            Health.OnHurt -= PrimeDeathWraithData_DeathWraith;
            Health.OnDead -= RecordDeathWraithData_DeathWraith;

            Health.OnDead -= DailyReportStatsCollector.OnGlobalDead;
            Health.OnHurt -= DailyReportStatsCollector.OnGlobalHurt;
        }
    }
}

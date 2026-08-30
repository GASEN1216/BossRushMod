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

            // 鸭皇图鉴击杀采集。同样是命名 handler（AGENTS.md 4.6），handler 内自带开关门控，
            // 关闭时零成本早返；OnHurt 只用于「首次玩家伤害→死亡」的最快击杀计时。
            Health.OnDead += CodexKillCollector.OnGlobalDead;
            Health.OnHurt += CodexKillCollector.OnGlobalHurt;
        }

        internal void CleanupPlayerLifecycleRuntimeEvents()
        {
            Health.OnDead -= OnPlayerDeathInBossRush;
            Health.OnHurt -= PrimeDeathWraithData_DeathWraith;
            Health.OnDead -= RecordDeathWraithData_DeathWraith;

            Health.OnDead -= DailyReportStatsCollector.OnGlobalDead;
            Health.OnHurt -= DailyReportStatsCollector.OnGlobalHurt;

            Health.OnDead -= CodexKillCollector.OnGlobalDead;
            Health.OnHurt -= CodexKillCollector.OnGlobalHurt;
        }
    }
}

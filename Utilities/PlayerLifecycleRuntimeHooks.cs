namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void RegisterPlayerLifecycleRuntimeEvents()
        {
            Health.OnDead += OnPlayerDeathInBossRush;
            Health.OnHurt += PrimeDeathWraithData_DeathWraith;
            Health.OnDead += RecordDeathWraithData_DeathWraith;
        }

        internal void CleanupPlayerLifecycleRuntimeEvents()
        {
            Health.OnDead -= OnPlayerDeathInBossRush;
            Health.OnHurt -= PrimeDeathWraithData_DeathWraith;
            Health.OnDead -= RecordDeathWraithData_DeathWraith;
        }
    }
}

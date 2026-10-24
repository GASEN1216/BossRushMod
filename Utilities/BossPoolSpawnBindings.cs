namespace BossRush
{
    /// <summary>
    /// 把有专属工厂的入池 Boss（冠军之影、蚀日花冠）接到共享刷怪核心。
    /// 绑定由各 Boss 的池适配器拥有，宿主只转发一行，不在 ModBehaviour partial 里持有逐个 Boss 的生成委托。
    /// </summary>
    internal static class BossPoolSpawnBindings
    {
        internal static void Bind(EnemySpawnRuntime runtime, ModBehaviour host)
        {
            runtime.BindSandstormChampionService((position, notify, defer, nonWave, active) =>
                SandstormChampionPoolAdapter.SpawnAsync(host, position, active, nonWave, defer));
            runtime.BindEclipseBloomService((position, notify, defer, nonWave, active) =>
                EclipseBloomPoolAdapter.SpawnAsync(host, position, active, nonWave, defer));
        }
    }
}

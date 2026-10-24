namespace BossRush
{
    /// <summary>探索内容的创建入口；生命周期仍由共享宿主逐模块分发。</summary>
    internal static class ExplorationRuntimeRegistration
    {
        internal static void Register(BossRushRuntimeModuleHost host)
        {
            host.Register(new EclipseBloomIntroduction());
            host.Register(new EclipseBloomRuntimeModule());
            host.Register(new GreedyMimicRuntimeModule());
            host.Register(new OddParcelRuntimeModule());
            host.Register(new DangerousPrototypeRuntimeModule());
        }
    }
}

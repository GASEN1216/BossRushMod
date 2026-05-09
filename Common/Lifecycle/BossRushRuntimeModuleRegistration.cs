namespace BossRush
{
    public partial class ModBehaviour
    {
        private void RegisterRuntimeModules()
        {
            runtimeModuleHost.Register(new ArchitectureSentinelRuntimeModule());
            runtimeModuleHost.Register(new ModeDRuntimeModule());
            runtimeModuleHost.Register(new DebugToolsRuntimeModule());
            runtimeModuleHost.Register(new AchievementRuntimeModule());
            runtimeModuleHost.Register(new CommonNpcRuntimeModule());
            runtimeModuleHost.Register(new WavesArenaRuntimeModule());
            runtimeModuleHost.Register(new ModeERuntimeModule());
            runtimeModuleHost.Register(new ModeFRuntimeModule());
            runtimeModuleHost.Register(new ZombieModeRuntimeModule());
        }
    }
}

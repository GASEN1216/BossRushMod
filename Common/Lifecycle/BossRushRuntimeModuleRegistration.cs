namespace BossRush
{
    public partial class ModBehaviour
    {
        private void RegisterRuntimeModules()
        {
            runtimeModuleHost.Register(new ArchitectureSentinelRuntimeModule());
        }
    }
}

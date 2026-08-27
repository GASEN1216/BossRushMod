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
            runtimeModuleHost.Register(new ModeGRuntimeModule());

            // Mode H 只允许一个实例：先创建并保存到 ModBehaviour 字段，再把**同一个引用**
            // 注册给 host。入口、交互点和恢复面板都必须委托 ModeHRuntime，禁止二次 new
            // （设计提案 §18.1；Mode G 当前的入口实例/host 实例分裂是本模式要避免的反例）。
            modeHRuntime = new ModeHRuntimeModule();
            runtimeModuleHost.Register(modeHRuntime);
        }

        /// <summary>Mode H 唯一运行时实例。</summary>
        private ModeHRuntimeModule modeHRuntime;

        /// <summary>
        /// Mode H 唯一实例的只读门面。入口、交互点、恢复面板与场景回调都只能用它，
        /// 不得再次 new ModeHRuntimeModule()。
        /// </summary>
        internal ModeHRuntimeModule ModeHRuntime { get { return modeHRuntime; } }
    }
}

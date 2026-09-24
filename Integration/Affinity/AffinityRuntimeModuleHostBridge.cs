namespace BossRush
{
    /// <summary>保留好感度原宿主调用槽，将生命周期工作转发给唯一运行时模块。</summary>
    public partial class ModBehaviour
    {
        private AffinityRuntimeModule affinityRuntime;

        internal void AttachAffinityRuntimeModule(AffinityRuntimeModule module)
        {
            affinityRuntime = module;
        }

        internal void DetachAffinityRuntimeModule(AffinityRuntimeModule module)
        {
            if (ReferenceEquals(affinityRuntime, module))
            {
                affinityRuntime = null;
            }
        }

        private void InitializeAffinitySystem()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.InitializeAffinitySystem();
            }
        }

        internal void TickAffinityRuntimeFromHost()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.TickAffinityRuntime();
            }
        }

        internal void OnSceneUnloadAffinityRuntimeFromHost()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.OnAffinitySceneUnload();
            }
        }

        internal void CleanupAffinityRuntimeFromHost()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.Cleanup();
            }
        }
    }
}

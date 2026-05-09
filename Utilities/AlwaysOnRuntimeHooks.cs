namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void TickAlwaysOnRuntime()
        {
            UpdateMessage();
            AffinityManager.UpdateDeferredSave();
        }

        internal void OnSceneUnloadAlwaysOnRuntime()
        {
            AffinityUIManager.OnSceneUnload();
            AffinityManager.OnSceneUnload();
        }

        internal void CleanupAlwaysOnRuntimeOnDestroy()
        {
            try
            {
                AffinityManager.OnAffinityChanged -= OnAffinityChanged;
                AffinityManager.OnLevelUp -= OnAffinityLevelUp;
                AffinityManager.Shutdown();
                AffinityUIManager.Cleanup();
            }
            catch
            {
            }

            try
            {
                EntityModelFactory.Shutdown();
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] EntityModelFactory 卸载异常: " + e.Message);
            }
        }
    }
}

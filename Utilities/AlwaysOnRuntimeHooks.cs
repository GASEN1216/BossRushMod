namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void InitializeAlwaysOnRuntime()
        {
            try
            {
                string modPath = GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    EntityModelFactory.Initialize(modPath);
                }
                else
                {
                    DevLog("[BossRush] [WARNING] 无法获取 Mod 路径，EntityModelFactory 未初始化");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] [WARNING] EntityModelFactory 初始化异常: " + e.Message);
            }

            WikiContentManager.Instance.ResetCache();
            InitializeAffinitySystem();
        }

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

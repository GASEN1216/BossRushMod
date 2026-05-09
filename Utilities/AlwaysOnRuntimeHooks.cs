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
    }
}

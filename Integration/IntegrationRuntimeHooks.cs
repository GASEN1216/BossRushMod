using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void StartIntegrationRuntime()
        {
            Start_Integration();
        }

        internal void OnSceneLoadedIntegrationRuntime(Scene scene, LoadSceneMode mode)
        {
            // 与遗种掉落同样按场景释放 per-character 订阅；不能等下一只 Boss 生成才裁剪死引用。
            AffixForgeStoneDropService.ClearAllTracking();
            OnSceneLoaded_Integration(scene, mode);
        }

        internal void CleanupIntegrationRuntimeOnDestroy()
        {
            SafeRuntime.Run("WikiUIManager.Shutdown", WikiUIManager.Shutdown);
            SafeRuntime.Run("ImageViewerUI.Shutdown", ImageViewerUI.Shutdown);
            OnDestroy_Integration();
        }
    }
}

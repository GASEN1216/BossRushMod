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
            OnSceneLoaded_Integration(scene, mode);
        }

        internal void CleanupIntegrationRuntimeOnDestroy()
        {
            OnDestroy_Integration();
        }
    }
}

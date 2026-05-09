using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void TickZombieModeRuntime(float unscaledDeltaTime)
        {
            TickZombieMode(unscaledDeltaTime);
        }

        internal void LateUpdateZombieModeRuntime()
        {
            ZombieModeUIHelper.EnforceModalInputPause();
        }

        internal void CleanupZombieModeForSceneLoad(Scene scene)
        {
            if (!ShouldPreserveZombieModeStartupForSceneLoad(scene))
            {
                CleanupZombieModeForSceneChange(ZombieModeFailureReason.SceneSwitched);
            }
        }

        internal void CleanupZombieModeOnDestroyRuntime()
        {
            CleanupZombieModeOnDestroy();
        }
    }
}

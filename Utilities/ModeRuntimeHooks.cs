using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal bool TickModeRuntimeGroup(float deltaTime, float unscaledDeltaTime)
        {
            if (TickWavesArenaRuntime(deltaTime))
            {
                return true;
            }

            TickModeERuntime(deltaTime);
            TickModeFRuntime(deltaTime);
            TickZombieModeRuntime(unscaledDeltaTime);
            TickWavesArenaBossCleanupRuntime(deltaTime);

            return false;
        }

        internal void LateUpdateModeRuntimeGroup()
        {
            LateUpdateZombieModeRuntime();
        }

        internal void CleanupModeRuntimeForSceneLoad(Scene scene)
        {
            CleanupZombieModeForSceneLoad(scene);
            CleanupModeFForSceneChange();
        }

        internal void CleanupModeRuntimeOnDestroy()
        {
            CleanupZombieModeOnDestroyRuntime();
        }
    }
}

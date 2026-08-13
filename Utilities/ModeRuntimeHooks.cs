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

            if (modeEActive || modeFActive)
            {
                TickModeEFSpawnPostprocessScheduler();
            }

            TickModeERuntime(deltaTime);
            TickModeFRuntime(deltaTime);
            UpdateModeG(deltaTime);
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
            if (modeGActive)
            {
                DevLog("[ModeG] 场景切换清理 Mode G");
                ShutdownModeG();
            }
        }

        internal void CleanupModeRuntimeOnDestroy()
        {
            ModBehaviour.ResetModeDStaticCaches();
            ModBehaviour.ResetModeDGlobalLootStaticCaches();
            BossRushHealthBarNamePatch.ResetStaticCaches();
            CleanupZombieModeOnDestroyRuntime();
            ShutdownModeG();
        }
    }
}

using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal bool TickModeRuntimeGroup(float deltaTime, float unscaledDeltaTime)
        {
            // 共享刷怪核心的分帧后处理并不只服务 Mode E/F：标准模式中的随机事件
            // Boss 也会借用这条队列。必须在 WavesArena 的 early-return 之前推进，
            // 否则标准模式下任务会永久停在 Queued，直到下一模式清空 scheduler。
            TickModeEFSpawnPostprocessScheduler();

            if (TickWavesArenaRuntime(deltaTime))
            {
                return true;
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
                // 先显式以 SceneChanged 终局，再关停：否则终局原因被 Dispose 兜底成 ModDestroyed，
                // 玩家侧也拿不到「离开战场，挑战中止」的提示。End 幂等，正常终局后不会走到这里
                // （modeGActive 已为 false）。
                try { if (modeGRuntime != null) modeGRuntime.End(ModeGExitReason.SceneChanged); }
                catch { /* no-throw：清理路径不得抛出 */ }
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

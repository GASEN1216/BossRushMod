namespace BossRush
{
    internal sealed partial class ModeFRuntimeModule
    {
        internal void TickModeFRuntime(float deltaTime)
        {
            if (coopChannel != null) TickCoopModeF(deltaTime);
            if (!BossRushCoop.HasWorldAuthority) { TickCoopModeFPersonal(deltaTime); return; }
            if (modeFActive)
            {
                TickModeF(deltaTime);
            }
        }

        internal void CleanupModeFForSceneChange()
        {
            if (modeFActive)
            {
                try
                {
                    ExitModeF();
                }
                catch (System.Exception ex)
                {
                    ModBehaviour.DevLog("[ModeF] 场景切换清理异常: " + ex.Message);
                }
            }
        }
    }
}

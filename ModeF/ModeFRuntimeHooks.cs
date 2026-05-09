namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void TickModeFRuntime(float deltaTime)
        {
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
                    DevLog("[ModeF] 场景切换清理异常: " + ex.Message);
                }
            }
        }
    }
}

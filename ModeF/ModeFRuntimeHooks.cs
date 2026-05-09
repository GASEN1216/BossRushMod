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
    }
}

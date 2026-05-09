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
    }
}

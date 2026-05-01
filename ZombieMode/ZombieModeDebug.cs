namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void DebugResetZombieModeShell()
        {
            CleanupZombieModeForSceneChange(ZombieModeFailureReason.ManualExit);
        }
    }
}

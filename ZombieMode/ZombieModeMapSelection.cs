namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool TryBeginZombieModeMapSelectionShell()
        {
            if (IsAnyBossRushLikeModeActive() || IsZombieModeStartupInProgress())
            {
                return false;
            }

            pendingZombieModeEntry = true;
            zombieModeEntryTransaction.Reset();
            zombieModeRunState.PendingCashInvestment = 0L;
            zombieModeRunState.ConfirmedCashInvested = 0L;
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.SelectingMap;
            return true;
        }

        private void CancelZombieModeMapSelectionShell()
        {
            if (zombieModeRunState.LifecyclePhase == ZombieModeLifecyclePhase.SelectingMap)
            {
                zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.None;
            }

            pendingZombieModeEntry = false;
            zombieModeRunState.PendingCashInvestment = 0L;
            zombieModeRunState.ConfirmedCashInvested = 0L;
            zombieModeEntryTransaction.Reset();
        }
    }
}

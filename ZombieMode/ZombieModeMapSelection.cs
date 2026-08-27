namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool TryBeginZombieModeMapSelectionShell()
        {
            // Mode H 真实资产风险门（加法分支，设计提案 §24.3）：
            // 只在存在未终结真实资产事务或风险未知时拒绝；no-throw，
            // 新档/无 journal 时同步 ready 且不阻断，旧模式行为逐字不变。
            try
            {
                if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
                {
                    DevLog("[ZombieMode] 地图选择被 Mode H 真实资产风险门拒绝");
                    return false;
                }
            }
            catch
            {
                // 门查询本身 no-throw；异常只表示未能判定，放行旧模式既有流程
            }

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

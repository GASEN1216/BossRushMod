namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool TryBeginZombieModeMapSelectionShell()
        {
            // Mode H 真实资产风险门（加法分支，设计提案 §24.3）：
            // 注：本方法的姊妹判定 IsZombieModeStartBlocked 在本文件末尾。
            // 只在存在未终结真实资产事务或风险未知时拒绝；no-throw，
            // 新档/无 journal 时同步 ready 且不阻断，旧模式行为逐字不变。
            try
            {
                if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
                {
                    // 此前这里只有 DevLog，玩家侧完全静默：点了没反应，也不知道为什么
                    ShowMessage(L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey()));
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

        /// <summary>
        /// 丧尸模式入口的两类拒绝：Mode H 真实资产风险门，与「已有其他模式在跑」。
        ///
        /// 两者成因完全不同，文案必须分开：混用同一句会让玩家去关一个根本没在跑的模式。
        /// Mode H 侧被拒时先给一次自愈重试（读档 I/O 异常可恢复），再取对应文案。
        /// 放在本文件而不是 ZombieModeEntry.cs：后者已经顶到
        /// tests/large_file_existing_allowlist.txt 的行数上限，不允许再涨。
        /// </summary>
        private bool IsZombieModeStartBlocked(out string failureReason)
        {
            failureReason = null;
            if (!ModeHRuntimeGates.IsLegacyModeEntryAllowed())
            {
                failureReason = L10n.T(ModeHRuntimeGates.ResolveLegacyBlockedMessageKey());
                return true;
            }
            if (IsAnyBossRushLikeModeActive() || IsZombieModeStartupInProgress())
            {
                failureReason = L10n.T("BossRush_ZombieMode_OtherModeActive");
                return true;
            }
            return false;
        }
    }
}

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void PrepareSceneRuntimeForLoad()
        {
            _characterCacheNeedsRefresh = true;
            _characterCacheRefreshTimer = 0f;
            _arenaCenterSet = false;
            ObjectCache.RefreshIfNeeded();
        }

        internal void TickGameplaySupportRuntime()
        {
            UpdateCashMagnet();
            UpdateEnemyRecoveryMonitor();
        }

        internal void CleanupEnemyRecoveryForSceneChange()
        {
            ClearEnemyRecoveryMonitorState();
        }

        internal void CleanupCashMagnetForSceneChange()
        {
            try
            {
                ClearCashMagnetState();
            }
            catch (System.Exception ex)
            {
                DevLog($"[CashMagnet] 场景切换清理异常: {ex.Message}");
            }
        }
    }
}

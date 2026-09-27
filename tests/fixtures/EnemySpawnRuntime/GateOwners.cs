namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    { public virtual void OnAwake(ModBehaviour owner) { } public virtual void OnDestroy() { } }
    // Cleanup dependencies are no-ops: production lifetime gates must reject stale
    // work on their own. Full cleanup effects execute in ModeDestroyLifecycle.
    internal sealed partial class ModeERuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour modeEHost;
        private bool modeERuntimeDestroyed, modeECleanupPending, modeEActive;
        private int modeESessionToken, modeESessionSerial;
        internal int Begin() { modeEActive = true; return BeginModeESession(); }
        private void EndModeE(bool show) { }
        private void StopModeEStartupWarmupIfPending() { }
        private void DestroyModeEShellRuntimeState() { }
        private void ResetModeEMerchantStaticCaches() { }
    }
    internal sealed class ModeFState { internal bool IsActive; internal int RuntimeSessionToken; }
    internal sealed partial class ModeFRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private bool modeFRuntimeDestroyed, modeFCleanupPending;
        internal bool modeFActive;
        private int modeFSessionSerial;
        private readonly ModeFState modeFState = new ModeFState();
        internal int Begin() { modeFActive = modeFState.IsActive = true; return BeginModeFSession(); }
        private void ExitModeF(bool show) { }
        private void ResetPlayerBountyKillLatch() { }
        private void CleanupModeFDeferredExitBossObjects() { }
    }
    internal static class ModeFStatusHud { internal static void Dispose() { } }
}

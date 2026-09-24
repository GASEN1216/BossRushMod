namespace BossRush
{
    public partial class ModBehaviour
    {
        internal bool IsInfiniteHellMode_WavesArena { get { return infiniteHellMode; } }

        private void UpdateCashMagnet()
        {
            if (wavesArenaRuntime != null) wavesArenaRuntime.UpdateCashMagnet();
        }

        private void ClearCashMagnetState()
        {
            if (wavesArenaRuntime != null) wavesArenaRuntime.ClearCashMagnetState();
        }
    }
}

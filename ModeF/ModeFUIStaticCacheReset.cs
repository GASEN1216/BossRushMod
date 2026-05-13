namespace BossRush
{
    public static partial class BossRushHealthBarNamePatch
    {
        public static void ResetStaticCaches()
        {
            cachedInstance = null;
            lastRefreshFrame = -1;
            lastProcessedFrameByBarId.Clear();
            lastCleanupFrame = -1;
            cachedPlayerBarId = -1;
            playerBarIdCheckFrame = -1;
            staleBarIdScratch.Clear();
        }
    }
}

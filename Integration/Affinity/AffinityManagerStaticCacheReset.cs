namespace BossRush
{
    public static partial class AffinityManager
    {
        public static void ResetStaticCaches()
        {
            npcDataMap.Clear();
            npcConfigMap.Clear();
            pendingDecayNotices.Clear();
            isInitialized = false;
            isDirty = false;
            lastSaveTime = 0f;
            OnAffinityChanged = null;
            OnLevelUp = null;
        }
    }
}

namespace BossRush
{
    public static partial class AffinityManager
    {
        public static void ResetStaticCaches()
        {
            npcDataMap.Clear();
            npcConfigMap.Clear();
            isInitialized = false;
            isDirty = false;
            lastSaveTime = 0f;
            OnAffinityChanged = null;
            OnLevelUp = null;
        }
    }
}

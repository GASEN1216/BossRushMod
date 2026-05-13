namespace BossRush
{
    public partial class ModBehaviour
    {
        private static void ResetModeDStaticCaches()
        {
            if (cachedCharacterPresets != null)
            {
                cachedCharacterPresets.Clear();
                cachedCharacterPresets = null;
            }

            presetFilterCache.Clear();
            presetFilterCache2.Clear();
        }
    }
}

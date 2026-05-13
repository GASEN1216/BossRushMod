using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private static void ResetModeDGlobalLootStaticCaches()
        {
            lastGlobalPoolAttemptTime = -999f;

            if (modeDGlobalItemPool != null)
            {
                modeDGlobalItemPool.Clear();
                modeDGlobalItemPool = null;
            }
            modeDGlobalItemPoolInitialized = false;

            if (modeDGlobalItemPoolByQuality != null)
            {
                foreach (KeyValuePair<int, List<int>> pair in modeDGlobalItemPoolByQuality)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Clear();
                    }
                }
                modeDGlobalItemPoolByQuality.Clear();
                modeDGlobalItemPoolByQuality = null;
            }
        }
    }
}

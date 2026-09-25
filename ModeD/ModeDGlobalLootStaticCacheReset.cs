using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private static void ResetModeDGlobalLootStaticCaches()
        {
            ModeDItemPool.ResetGlobalLootStaticCaches();
        }
    }
}

// ============================================================================
// LootBlacklistRegistry.cs - 掉落物品黑名单注册系统
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    public static class LootBlacklistRegistry
    {
        private static readonly HashSet<int> _blacklist = new HashSet<int>();
        private static bool _initialized = false;

        public static bool Contains(int itemId)
        {
            EnsureInitialized();
            return _blacklist.Contains(itemId);
        }

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            RegisterBlacklist();
        }

        private static void RegisterBlacklist()
        {
            int[] blacklistIds = new int[]
            {
                153, 284, 292, 293, 294, 295, 297, 299, 300,
                313, 314, 315, 316, 317, 366, 373, 375, 376,
                385, 386, 397, 427, 672, 745, 747, 750, 751,
                753, 757, 760, 761, 762, 763, 766, 774, 775,
                778, 809, 811, 814, 816, 817, 823, 824, 825,
                835, 910, 913, 1046, 1047, 1048, 1049, 1050, 1051,
                1052, 1053, 1054, 1062, 1064, 1065, 1066, 1067, 1068,
                1069, 1073, 1092, 1158, 1164, 1214, 1225, 1249, 1273,

                500003,
                500004,
                500005,

                500010,

                500011,
                500012,
                500013,
                500034,
                500035,

                500016,
            };

            foreach (int id in blacklistIds)
            {
                _blacklist.Add(id);
            }
        }
    }
}

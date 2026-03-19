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

                DragonDescendantConfig.DRAGON_HELM_TYPE_ID,
                DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID,
                DragonBreathConfig.WEAPON_TYPE_ID,

                FlightConfig.TotemTypeIdBase,

                DragonKingConfig.DRAGON_KING_HELM_TYPE_ID,
                DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID,
                ReverseScaleConfig.TotemTypeId,
                DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID,
                DragonKingBossGunConfig.WeaponTypeId,

                FactionFlagConfig.RANDOM_FLAG_TYPE_ID,
                FactionFlagConfig.SCAV_FLAG_TYPE_ID,
                FactionFlagConfig.USEC_FLAG_TYPE_ID,
                FactionFlagConfig.BEAR_FLAG_TYPE_ID,
                FactionFlagConfig.LAB_FLAG_TYPE_ID,
                FactionFlagConfig.WOLF_FLAG_TYPE_ID,
                FactionFlagConfig.PLAYER_FLAG_TYPE_ID,

                RespawnItemConfig.TAUNT_SMOKE_TYPE_ID,
                RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID,
                RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID,
                RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID,

                BloodhuntTransponderConfig.TYPE_ID,
                FoldableCoverPackConfig.TYPE_ID,
                ReinforcedRoadblockPackConfig.TYPE_ID,
                BarbedWirePackConfig.TYPE_ID,
                EmergencyRepairSprayConfig.TYPE_ID,

                DingdangDrawingConfig.TYPE_ID,
            };

            foreach (int id in blacklistIds)
            {
                _blacklist.Add(id);
            }
        }
    }
}

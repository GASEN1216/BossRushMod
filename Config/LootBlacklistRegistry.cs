// ============================================================================
// LootBlacklistRegistry.cs - 掉落物品黑名单注册系统
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    public static class LootBlacklistRegistry
    {
        private const string DataFileName = "LootBlacklist.json";
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
            int[] blacklistIds = LoadJsonBlacklistIds();
            if (blacklistIds == null || blacklistIds.Length == 0)
            {
                blacklistIds = CreateFallbackBlacklistIds();
            }

            foreach (int id in blacklistIds)
            {
                _blacklist.Add(id);
            }
        }

        private static int[] LoadJsonBlacklistIds()
        {
            string json;
            if (!JsonDataRegistry.TryReadDataFile(DataFileName, out json))
            {
                return null;
            }

            int[] ids = ParseItemIds(json);
            if (ids == null || ids.Length == 0)
            {
                ModBehaviour.DevLog("[LootBlacklistRegistry] [WARNING] LootBlacklist.json 无有效 itemIds，使用硬编码兜底");
                return null;
            }

            ModBehaviour.DevLog("[LootBlacklistRegistry] 已从 JSON 加载黑名单 " + ids.Length + " 项");
            return ids;
        }

        internal static int[] ParseItemIds(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            int keyIndex = json.IndexOf("\"itemIds\"", System.StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            int openIndex = json.IndexOf('[', keyIndex);
            if (openIndex < 0)
            {
                return null;
            }

            int closeIndex = json.IndexOf(']', openIndex + 1);
            if (closeIndex < 0 || closeIndex <= openIndex)
            {
                return null;
            }

            string arrayText = json.Substring(openIndex + 1, closeIndex - openIndex - 1);
            List<int> ids = new List<int>();
            string[] tokens = arrayText.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                int id;
                if (!int.TryParse(token, out id))
                {
                    ModBehaviour.DevLog("[LootBlacklistRegistry] [WARNING] LootBlacklist.json itemIds 含非法值: " + token);
                    return null;
                }

                ids.Add(id);
            }

            return ids.ToArray();
        }

        private static int[] CreateFallbackBlacklistIds()
        {
            return new int[]
            {
                153, 284, 292, 293, 294, 295, 297, 299, 300,
                313, 314, 315, 316, 317, 366, 373, 375, 376,
                385, 386, 397, 427, 672, 745, 747, 750, 751,
                753, 757, 760, 761, 762, 763, 766, 774, 775,
                778, 809, 811, 814, 816, 817, 823, 824, 825,
                835, 910, 913, 1046, 1047, 1048, 1049, 1050, 1051,
                1052, 1053, 1054, 1062, 1064, 1065, 1066, 1067, 1068,
                1069, 1073, 1092, 1158, 1164, 1214, 1225, 1249, 1273,
                500018, 500007,

                DragonDescendantConfig.DRAGON_HELM_TYPE_ID,
                DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID,
                DragonBreathConfig.WEAPON_TYPE_ID,

                FlightConfig.TotemTypeIdBase,

                DragonKingConfig.DRAGON_KING_HELM_TYPE_ID,
                DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID,
                ReverseScaleConfig.TotemTypeId,
                DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID,
                DragonKingBossGunConfig.WeaponTypeId,
                FrostmourneIds.WeaponTypeId,
                PhantomWitchScytheIds.WeaponTypeId,
                NewWeaponIds.ViperDaggerTypeId,
                NewWeaponIds.SummonStaffTypeId,
                NewWeaponIds.EnergyShieldTypeId,
                NewWeaponIds.FrostSpearTypeId,
                NewWeaponIds.ThunderRingTypeId,
                500053, 500054, 500055, 500056,

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
                ZombieTideInvitationConfig.TYPE_ID,
                ZombieTideBeaconConfig.TYPE_ID,
            };
        }
    }
}

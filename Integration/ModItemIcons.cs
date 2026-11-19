// ============================================================================
// ModItemIcons.cs - Mod 物品图标的唯一入口
// ============================================================================
// 规则（ModItemIconGuard 守卫）：
//   1. 每个 Mod 物品号的图标名只声明一次：
//      - 有自己定义表的模块写在定义表里（天空岛物品与头目装备、潮闸镇、末班编组站、后山、收集品），
//        由各模块的 IconNameOf 交出来；
//      - 其余物品写在下面的 LegacyIconName。
//   2. 图标统一放在 Assets/Items/<图标名>.png，登记 tools/production_icon_manifest.json；
//      按路径取图走 ModSprites：正式构建只从 production_icons 压缩包借用，Dev 或缺包时才解码散图。
//   3. 只有 BossRushDynamicItemRegistry.Publish 会把图标套到 prefab 上，而且在 AddDynamicEntry 之前：
//      官方第一次读元数据时会把 prefab.Icon 缓存下来，注册后再改图标改不到缓存。
//      配置器与界面都不直接写 Item.Icon，也不按路径读物品图标，界面要图标时调 ForType。
//   4. Mod 停用时生产包随之卸载，留在官方表里的 prefab 图标跟着失效；
//      重新启用时由 BossRushDynamicItemRegistry.RefreshPublishedIcons 统一补图并重新注册。
// ============================================================================

using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal static class ModItemIcons
    {
        private const string IconFolder = "Assets/Items/";

        // 没有模块定义表的物品：多是早期从 bundle prefab 加载的物品，以及单件克隆物品。
        // 写成 switch：没有静态状态；同一个物品号写两次会直接编译报错（重复的 case）。
        private static string LegacyIconName(int typeId)
        {
            switch (typeId)
            {
                case BossRushItemIds.BossRushTicket: return "bossrush_ticket";
                case BossRushItemIds.BirthdayCake: return "birthday_cake";
                case DragonDescendantConfig.DRAGON_HELM_TYPE_ID: return "dragon_helm";
                case DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID: return "dragon_armor";
                case DragonBreathConfig.WEAPON_TYPE_ID: return "dragon_breath";
                case BossRushItemIds.AdventureJournal: return "adventure_journal";
                case AwenCourierTokenConfig.TYPE_ID: return "awen_courier_token";
                case FlightConfig.TotemTypeIdBase: return "flight_totem";
                case DragonKingConfig.DRAGON_KING_HELM_TYPE_ID: return "dragon_king_helm";
                case DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID: return "dragon_king_armor";
                case ReverseScaleConfig.TotemTypeId: return "reverse_scale";
                case ColdQuenchFluidConfig.TYPE_ID: return "cold_quench_fluid";
                case BrickStoneConfig.TYPE_ID: return "brick_stone";
                case DingdangDrawingConfig.TYPE_ID: return "dingdang_drawing";
                case DiamondConfig.TYPE_ID: return "diamond";
                case AchievementMedalConfig.TYPE_ID: return "achievement_medal";
                case WildHornConfig.TYPE_ID: return "wild_horn";
                case FactionFlagConfig.RANDOM_FLAG_TYPE_ID: return "faction_flag_random";
                case FactionFlagConfig.SCAV_FLAG_TYPE_ID: return "faction_flag_scav";
                case FactionFlagConfig.USEC_FLAG_TYPE_ID: return "faction_flag_usec";
                case FactionFlagConfig.BEAR_FLAG_TYPE_ID: return "faction_flag_bear";
                case FactionFlagConfig.LAB_FLAG_TYPE_ID: return "faction_flag_lab";
                case FactionFlagConfig.WOLF_FLAG_TYPE_ID: return "faction_flag_wolf";
                case FactionFlagConfig.PLAYER_FLAG_TYPE_ID: return "faction_flag_player";
                case RespawnItemConfig.TAUNT_SMOKE_TYPE_ID: return "taunt_smoke";
                case RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID: return "chaos_detonator";
                case DiamondRingConfig.TYPE_ID: return "diamond_ring";
                case CalmingDropsConfig.TYPE_ID: return "calming_drops";
                case PeaceCharmConfig.TYPE_ID: return "peace_charm";
                case RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID: return "bosscall_whistle";
                case RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID: return "bloodhunt_beacon";
                case FenHuangHalberdIds.WeaponTypeId: return "fenhuang_halberd";
                case DragonKingBossGunConfig.WeaponTypeId: return "dragon_king_gun";
                case BloodhuntTransponderConfig.TYPE_ID: return "bloodhunt_transponder";
                case FoldableCoverPackConfig.TYPE_ID: return "foldable_cover_pack";
                case ReinforcedRoadblockPackConfig.TYPE_ID: return "reinforced_roadblock_pack";
                case BarbedWirePackConfig.TYPE_ID: return "barbed_wire_pack";
                case EmergencyRepairSprayConfig.TYPE_ID: return "emergency_repair_spray";
                case FrostmourneIds.WeaponTypeId: return "frostmourne";
                case AwenLootSweepTokenConfig.TYPE_ID: return "awen_loot_sweep_token";
                case PhantomWitchScytheIds.WeaponTypeId: return "phantom_scythe";
                case BossRushItemIds.ZombieTideInvitation: return "zombie_tide_invitation";
                case BossRushItemIds.ZombieTideBeacon: return "zombie_tide_beacon";
                case NewWeaponIds.ViperDaggerTypeId: return "viper_dagger_icon";
                case NewWeaponIds.SummonStaffTypeId: return "summon_staff_icon";
                case NewWeaponIds.EnergyShieldTypeId: return "energy_shield_icon";
                case NewWeaponIds.FrostSpearTypeId: return "frost_spear_icon";
                case NewWeaponIds.ThunderRingTypeId: return "thunder_ring_icon";
                case FrostThunderSetConfig.FROST_HELMET_ID: return "frost_crown";
                case FrostThunderSetConfig.FROST_ARMOR_ID: return "ice_armor";
                case FrostThunderSetConfig.THUNDER_HELMET_ID: return "thunder_horn";
                case FrostThunderSetConfig.THUNDER_ARMOR_ID: return "thunder_armor";
                case FateEchoRelicConfig.TYPE_ID: return "fate_echo_relic";
                case BossRushItemIds.PortableSafeZoneDevice: return "portable_safe_zone_device";
                case BossRushItemIds.RelicEgg: return "relic_egg";
                case BossRushItemIds.AffixForgeStone: return "affix_forge_stone";
                case BossRushItemIds.CodexBook: return "codex_book";
                case BossRushItemIds.SkyIslandNavInstrument: return "sky_island_nav_instrument";
                case BossRushItemIds.AstralStaff: return "astral_staff_icon";
                case BossRushItemIds.EmptyMagazineMine: return "empty_magazine_mine_icon";
                case BossRushItemIds.RollCallLedger: return "roll_call_ledger_icon";
                case BossRushItemIds.ReloadMetronome: return "reload_metronome";
                case BossRushItemIds.TemperingSolvent: return "tempering_solvent";
                case BossRushItemIds.PassTheBomb: return "pass_the_bomb";
                case BossRushItemIds.ResidualSunShield: return "residual_sun_shield_icon";
                default: return null;
            }
        }

        /// <summary>按物品号取图标名；不是 Mod 物品或没声明时返回 false。</summary>
        internal static bool TryGetIconName(int typeId, out string iconName)
        {
            iconName = LegacyIconName(typeId)
                ?? SkyIslandItems.IconNameOf(typeId)
                ?? SkyIslandBossGearIconName(typeId)
                ?? TidegateItems.IconNameOf(typeId)
                ?? LastShiftItems.IconNameOf(typeId)
                ?? BackMountainIconName(typeId)
                ?? CollectibleItems.IconNameOf(typeId);
            return iconName != null;
        }

        /// <summary>物品图标的相对路径（与 production_icon_manifest 的 path 一致）。</summary>
        internal static string IconPath(string iconName)
        {
            return IconFolder + iconName + ".png";
        }

        /// <summary>界面要某个 Mod 物品的图标时调这里；与物品格里显示的是同一张。</summary>
        internal static Sprite ForType(int typeId)
        {
            string iconName;
            return TryGetIconName(typeId, out iconName) ? ModSprites.Get(IconPath(iconName)) : null;
        }

        /// <summary>只给 BossRushDynamicItemRegistry.Publish 用：注册前把声明的图标套到 prefab 上。</summary>
        internal static void ApplyBeforePublish(Item prefab)
        {
            if (prefab == null) return;
            string iconName;
            if (!TryGetIconName(prefab.TypeID, out iconName)) return;
            Sprite sprite = ModSprites.Get(IconPath(iconName));
            if (sprite != null)
            {
                prefab.Icon = sprite;
                return;
            }
            Debug.LogWarning("[ModItemIcons] 图标缺失，保留原图: " + prefab.TypeID + " -> " + IconPath(iconName));
        }

        private static string SkyIslandBossGearIconName(int typeId)
        {
            SkyIslandBossGearSpec spec = SkyIslandBossRules.GearSpec(typeId);
            return spec != null ? spec.IconName : null;
        }

        private static string BackMountainIconName(int typeId)
        {
            BackMountainItems.Definition def = BackMountainItems.GetDefinition(typeId);
            return def != null ? def.IconName : null;
        }
    }
}

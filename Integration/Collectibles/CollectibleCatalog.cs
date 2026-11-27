// ============================================================================
// CollectibleCatalog.cs - 收集品定义表（100 件）与系列表
// ============================================================================
// TypeID 500119-500218 连续、按表顺序排列（Get 按下标直取，不建字典、不扫官方物品表）；
// 收藏册 500219 在 CollectionBookItem.cs。台账见 AGENTS.md §4.3、docs/contracts.md §1、docs/reference/Bossrush使用物品ID表.md。
// 一经发布，TypeID 与 Key 都是存档契约：改顺序、删条目、改号都算 BREAKING（AGENTS.md §10）。
//
// 【来路 / 用途 / 串到哪】
//   - 来路：官方出击图与自研三图的场景箱，第一次开箱时按箱子档次判一次，额外多一格（CollectibleLootRuntime）；
//     不进官方搜箱池，也登记掉落黑名单，不进许愿台、日报与各类品质池。
//   - 用途：交给收藏册登记（CollectionBookStore）、凑齐系列拿奖金与永久小加成（CollectionSeriesBonus）、
//     送给喜欢它的 NPC（LikedBy，接 INPCGiftConfig.PositiveItems）；也可以按官方价卖掉。
//   - 价值按官方同档收藏品 / 奢侈品区间（tests/fixtures/TidegateTreasure/catalog.txt，2026-11-07 快照）。
// 名称与描述在 Localization/CollectiblesLocalization.cs；抽签与档次常量在 CollectibleTuning.cs。
// ============================================================================

namespace BossRush
{
    /// <summary>收集品系列。数值写进存档（已领奖励的系列），只能在末尾追加。</summary>
    internal enum CollectibleSeries
    {
        GroundZero = 0,
        Warehouse = 1,
        Farm = 2,
        JLab = 3,
        StormZone = 4,
        TestArea37 = 5,
        SkyIsland = 6,
        Tidegate = 7,
        LastShift = 8,
        Arena = 9,
        Companions = 10,
        Treasures = 11,
    }

    /// <summary>一件收集品的定义。</summary>
    internal sealed class CollectibleDefinition
    {
        internal readonly int TypeId;
        internal readonly string Key;
        internal readonly string LocKey;
        internal readonly CollectibleSeries Series;
        internal readonly int Quality;
        internal readonly int Value;
        internal readonly float Weight;
        /// <summary>喜欢它的 NPC：goblin / nurse 与 DuckNpcs.json 的永久 NPC id。</summary>
        internal readonly string[] LikedBy;

        internal CollectibleDefinition(int typeId, string key, CollectibleSeries series, int quality, int value, float weight, params string[] likedBy)
        {
            TypeId = typeId;
            Key = key;
            LocKey = "BossRush_Collectible_" + key;
            Series = series;
            Quality = quality;
            Value = value;
            Weight = weight;
            LikedBy = likedBy ?? new string[0];
        }
    }

    /// <summary>系列显示名与范围。Local 只在对应地图出；全图系列在任何参与的地图都能出；珍品组不算集齐。</summary>
    internal sealed class CollectibleSeriesInfo
    {
        internal readonly CollectibleSeries Series;
        internal readonly string NameCN, NameEN;
        internal readonly bool Local;
        internal readonly bool CountsForSet;

        internal CollectibleSeriesInfo(CollectibleSeries series, string nameCN, string nameEN, bool local, bool countsForSet)
        {
            Series = series; NameCN = nameCN; NameEN = nameEN; Local = local; CountsForSet = countsForSet;
        }

        internal string Name { get { return L10n.T(NameCN, NameEN); } }
    }

    internal static class CollectibleCatalog
    {
        internal const int FirstTypeId = BossRushItemIds.CollectibleFirst;
        internal const int LastTypeId = BossRushItemIds.CollectibleLast;

        private static readonly CollectibleSeriesInfo[] SeriesTable =
        {
            new CollectibleSeriesInfo(CollectibleSeries.GroundZero, "零号区", "Ground Zero", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.Warehouse, "仓库区", "Hidden Warehouse", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.Farm, "农场镇", "Farm Town", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.JLab, "J-Lab 实验室", "J-Lab", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.StormZone, "风暴区", "Storm Zone", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.TestArea37, "37 号实验区", "Experimental Area 37", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.SkyIsland, "晴岚群岛", "Qinglan Archipelago", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.Tidegate, "潮闸镇", "Tidegate", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.LastShift, "末班编组站", "Last Shift Yard", true, true),
            new CollectibleSeriesInfo(CollectibleSeries.Arena, "竞技场传说", "Arena Legends", false, true),
            new CollectibleSeriesInfo(CollectibleSeries.Companions, "伙伴旧物", "Companions' Keepsakes", false, true),
            new CollectibleSeriesInfo(CollectibleSeries.Treasures, "珍品", "Treasures", false, false),
        };

        private static readonly CollectibleDefinition[] Items =
        {
            // 零号区
            new CollectibleDefinition(500119, "gz_missing_poster", CollectibleSeries.GroundZero, 1, 854, 0.05f),
            new CollectibleDefinition(500120, "gz_bus_pass", CollectibleSeries.GroundZero, 1, 988, 0.02f),
            new CollectibleDefinition(500121, "gz_gas_card", CollectibleSeries.GroundZero, 2, 1391, 0.02f),
            new CollectibleDefinition(500122, "gz_scout_whistle", CollectibleSeries.GroundZero, 2, 1503, 0.05f),
            new CollectibleDefinition(500123, "gz_waddle_medal", CollectibleSeries.GroundZero, 3, 2027, 0.15f),
            new CollectibleDefinition(500124, "gz_shortcut_map", CollectibleSeries.GroundZero, 3, 2568, 0.1f, "duck_npc_xiaoman"),
            new CollectibleDefinition(500125, "gz_gilded_doorplate", CollectibleSeries.GroundZero, 4, 3476, 1.2f),
            new CollectibleDefinition(500126, "gz_founders_photo", CollectibleSeries.GroundZero, 5, 6151, 0.8f, "duck_npc_xiaoman"),
            // 仓库区
            new CollectibleDefinition(500127, "wh_clerk_pen", CollectibleSeries.Warehouse, 1, 841, 0.02f),
            new CollectibleDefinition(500128, "wh_container_seal", CollectibleSeries.Warehouse, 1, 872, 0.05f),
            new CollectibleDefinition(500129, "wh_forklift_keychain", CollectibleSeries.Warehouse, 2, 1289, 0.05f),
            new CollectibleDefinition(500130, "wh_stock_clipboard", CollectibleSeries.Warehouse, 2, 1465, 0.4f),
            new CollectibleDefinition(500131, "wh_shelf_plate", CollectibleSeries.Warehouse, 3, 1914, 0.3f),
            new CollectibleDefinition(500132, "wh_best_clerk_pennant", CollectibleSeries.Warehouse, 3, 2595, 0.3f),
            new CollectibleDefinition(500133, "wh_antique_weight", CollectibleSeries.Warehouse, 4, 4003, 2.5f, "goblin_dingdang"),
            new CollectibleDefinition(500134, "wh_smuggled_vase", CollectibleSeries.Warehouse, 5, 8765, 1.6f),
            // 农场镇
            new CollectibleDefinition(500135, "farm_horseshoe", CollectibleSeries.Farm, 1, 846, 0.4f),
            new CollectibleDefinition(500136, "farm_straw_duck", CollectibleSeries.Farm, 1, 938, 0.2f, "nurse_yuzhi"),
            new CollectibleDefinition(500137, "farm_pumpkin_ribbon", CollectibleSeries.Farm, 2, 1251, 0.05f),
            new CollectibleDefinition(500138, "farm_kazoo_score", CollectibleSeries.Farm, 2, 1555, 0.1f),
            new CollectibleDefinition(500139, "farm_windmill_model", CollectibleSeries.Farm, 3, 2099, 0.9f),
            new CollectibleDefinition(500140, "farm_market_bell", CollectibleSeries.Farm, 3, 2193, 0.7f),
            new CollectibleDefinition(500141, "farm_pickle_crock", CollectibleSeries.Farm, 4, 3904, 3.5f),
            new CollectibleDefinition(500142, "farm_harvest_trophy", CollectibleSeries.Farm, 5, 7609, 1.8f),
            // J-Lab 实验室
            new CollectibleDefinition(500143, "jlab_coffee_mug", CollectibleSeries.JLab, 1, 836, 0.3f),
            new CollectibleDefinition(500144, "jlab_visitor_badge", CollectibleSeries.JLab, 1, 995, 0.02f),
            new CollectibleDefinition(500145, "jlab_petri_dish", CollectibleSeries.JLab, 2, 1334, 0.1f),
            new CollectibleDefinition(500146, "jlab_glow_vial", CollectibleSeries.JLab, 2, 1637, 0.1f, "nurse_yuzhi"),
            new CollectibleDefinition(500147, "jlab_control_jar", CollectibleSeries.JLab, 3, 2031, 0.8f),
            new CollectibleDefinition(500148, "jlab_torn_report", CollectibleSeries.JLab, 3, 2722, 0.1f),
            new CollectibleDefinition(500149, "jlab_centrifuge_model", CollectibleSeries.JLab, 4, 4825, 1.1f),
            new CollectibleDefinition(500150, "jlab_gold_pen", CollectibleSeries.JLab, 5, 8844, 0.05f),
            // 风暴区
            new CollectibleDefinition(500151, "storm_bent_sign", CollectibleSeries.StormZone, 1, 836, 0.6f),
            new CollectibleDefinition(500152, "storm_windproof_lighter", CollectibleSeries.StormZone, 1, 1135, 0.08f),
            new CollectibleDefinition(500153, "storm_glass", CollectibleSeries.StormZone, 2, 1472, 0.5f),
            new CollectibleDefinition(500154, "storm_anemometer", CollectibleSeries.StormZone, 2, 1786, 0.8f),
            new CollectibleDefinition(500155, "storm_lightning_rod", CollectibleSeries.StormZone, 3, 2154, 0.9f),
            new CollectibleDefinition(500156, "storm_weather_log", CollectibleSeries.StormZone, 3, 2707, 0.4f),
            new CollectibleDefinition(500157, "storm_eye_photo", CollectibleSeries.StormZone, 4, 5177, 0.05f),
            new CollectibleDefinition(500158, "storm_barometer", CollectibleSeries.StormZone, 5, 10668, 1.4f),
            // 37 号实验区
            new CollectibleDefinition(500159, "z37_eclipse_card", CollectibleSeries.TestArea37, 1, 888, 0.02f),
            new CollectibleDefinition(500160, "z37_safety_sticker", CollectibleSeries.TestArea37, 1, 1018, 0.01f),
            new CollectibleDefinition(500161, "z37_petal_specimen", CollectibleSeries.TestArea37, 2, 1515, 0.05f),
            new CollectibleDefinition(500162, "z37_counter", CollectibleSeries.TestArea37, 2, 1691, 0.6f),
            new CollectibleDefinition(500163, "z37_zone_badge", CollectibleSeries.TestArea37, 3, 2081, 0.05f),
            new CollectibleDefinition(500164, "z37_scorched_eyepiece", CollectibleSeries.TestArea37, 3, 2834, 0.3f),
            new CollectibleDefinition(500165, "z37_corona_amber", CollectibleSeries.TestArea37, 4, 4965, 0.3f),
            new CollectibleDefinition(500166, "z37_sun_relief", CollectibleSeries.TestArea37, 5, 8406, 3.0f),
            // 晴岚群岛
            new CollectibleDefinition(500167, "sky_bronze_windbell", CollectibleSeries.SkyIsland, 1, 936, 0.2f, "sky_weibai"),
            new CollectibleDefinition(500168, "sky_pigeon_band", CollectibleSeries.SkyIsland, 1, 937, 0.01f, "sky_fuzhou"),
            new CollectibleDefinition(500169, "sky_greenear_knot", CollectibleSeries.SkyIsland, 2, 1367, 0.1f, "sky_qinghe"),
            new CollectibleDefinition(500170, "sky_fortune_tube", CollectibleSeries.SkyIsland, 2, 1471, 0.6f, "sky_miantai"),
            new CollectibleDefinition(500171, "sky_registry_rubbing", CollectibleSeries.SkyIsland, 3, 1999, 0.1f, "sky_fuzhou"),
            new CollectibleDefinition(500172, "sky_starlamp_shard", CollectibleSeries.SkyIsland, 3, 2348, 0.2f, "sky_weibai"),
            new CollectibleDefinition(500173, "sky_gear_badge", CollectibleSeries.SkyIsland, 4, 4415, 0.15f, "goblin_dingdang", "sky_zheling"),
            new CollectibleDefinition(500174, "sky_bell_model", CollectibleSeries.SkyIsland, 5, 8195, 2.2f, "sky_bellkeeper"),
            // 潮闸镇
            new CollectibleDefinition(500175, "tide_bottle_message", CollectibleSeries.Tidegate, 1, 819, 0.4f, "tidegate_zhabo"),
            new CollectibleDefinition(500176, "tide_shanty_record", CollectibleSeries.Tidegate, 1, 1098, 0.3f, "tidegate_zhabo"),
            new CollectibleDefinition(500177, "tide_gauge_plate", CollectibleSeries.Tidegate, 2, 1344, 1.0f, "tidegate_zhabo"),
            new CollectibleDefinition(500178, "tide_bankbook", CollectibleSeries.Tidegate, 2, 1573, 0.1f, "tidegate_zhabo"),
            new CollectibleDefinition(500179, "tide_waterworks_award", CollectibleSeries.Tidegate, 3, 2302, 0.5f, "tidegate_zhabo"),
            new CollectibleDefinition(500180, "tide_dredger_bell", CollectibleSeries.Tidegate, 3, 2843, 1.6f, "tidegate_zhabo"),
            new CollectibleDefinition(500181, "tide_lighthouse_lens", CollectibleSeries.Tidegate, 4, 4444, 1.8f, "tidegate_zhabo"),
            new CollectibleDefinition(500182, "tide_cigar_box", CollectibleSeries.Tidegate, 5, 7545, 0.7f, "tidegate_zhabo"),
            // 末班编组站
            new CollectibleDefinition(500183, "ls_enamel_mug", CollectibleSeries.LastShift, 1, 851, 0.3f),
            new CollectibleDefinition(500184, "ls_timetable", CollectibleSeries.LastShift, 1, 1027, 0.1f),
            new CollectibleDefinition(500185, "ls_signal_flags", CollectibleSeries.LastShift, 2, 1391, 0.4f),
            new CollectibleDefinition(500186, "ls_escort_armband", CollectibleSeries.LastShift, 2, 1568, 0.05f),
            new CollectibleDefinition(500187, "ls_train_plate", CollectibleSeries.LastShift, 3, 2162, 1.3f),
            new CollectibleDefinition(500188, "ls_switch_lever", CollectibleSeries.LastShift, 3, 2531, 1.0f),
            new CollectibleDefinition(500189, "ls_headlamp", CollectibleSeries.LastShift, 4, 4263, 2.0f),
            new CollectibleDefinition(500190, "ls_brass_punch", CollectibleSeries.LastShift, 5, 7528, 0.4f),
            // 竞技场传说
            new CollectibleDefinition(500191, "arena_cup_stub", CollectibleSeries.Arena, 1, 902, 0.01f),
            new CollectibleDefinition(500192, "arena_glowstick", CollectibleSeries.Arena, 1, 1036, 0.05f),
            new CollectibleDefinition(500193, "arena_ticket_card", CollectibleSeries.Arena, 1, 1123, 0.02f),
            new CollectibleDefinition(500194, "arena_scale_rubbing", CollectibleSeries.Arena, 2, 1703, 0.05f, "goblin_dingdang"),
            new CollectibleDefinition(500195, "arena_witch_candle", CollectibleSeries.Arena, 2, 2015, 0.2f),
            new CollectibleDefinition(500196, "arena_signed_sandbag", CollectibleSeries.Arena, 4, 3798, 4.0f),
            new CollectibleDefinition(500197, "arena_hell_counter", CollectibleSeries.Arena, 4, 4117, 1.5f),
            new CollectibleDefinition(500198, "arena_belt_buckle", CollectibleSeries.Arena, 4, 5431, 0.6f),
            new CollectibleDefinition(500199, "arena_soul_orb", CollectibleSeries.Arena, 6, 18309, 2.0f),
            new CollectibleDefinition(500200, "arena_dragon_horn", CollectibleSeries.Arena, 6, 26362, 3.0f, "goblin_dingdang"),
            new CollectibleDefinition(500201, "arena_gold_belt", CollectibleSeries.Arena, 6, 31829, 3.5f),
            // 伙伴旧物
            new CollectibleDefinition(500202, "pal_awen_badge", CollectibleSeries.Companions, 1, 826, 0.02f),
            new CollectibleDefinition(500203, "pal_daily_first_issue", CollectibleSeries.Companions, 1, 1058, 0.1f, "duck_npc_xiaoman"),
            new CollectibleDefinition(500204, "pal_wish_coin", CollectibleSeries.Companions, 1, 1264, 0.02f),
            new CollectibleDefinition(500205, "pal_nurse_cap_pin", CollectibleSeries.Companions, 2, 1619, 0.02f, "nurse_yuzhi"),
            new CollectibleDefinition(500206, "pal_first_hammer", CollectibleSeries.Companions, 2, 2187, 1.2f, "goblin_dingdang"),
            new CollectibleDefinition(500207, "pal_eggshell_fossil", CollectibleSeries.Companions, 4, 4483, 0.4f),
            new CollectibleDefinition(500208, "pal_chapel_glass", CollectibleSeries.Companions, 4, 6693, 0.5f, "nurse_yuzhi"),
            new CollectibleDefinition(500209, "pal_jeff_photo", CollectibleSeries.Companions, 5, 10002, 0.05f),
            new CollectibleDefinition(500210, "pal_anvil_model", CollectibleSeries.Companions, 6, 21924, 6.0f, "goblin_dingdang"),
            new CollectibleDefinition(500211, "pal_codex_manuscript", CollectibleSeries.Companions, 6, 29336, 1.0f),
            new CollectibleDefinition(500212, "pal_wish_star", CollectibleSeries.Companions, 6, 42171, 0.8f),
            // 珍品
            new CollectibleDefinition(500213, "rare_vault_gold_bar", CollectibleSeries.Treasures, 7, 340817, 12.0f),
            new CollectibleDefinition(500214, "rare_gold_rubber_duck", CollectibleSeries.Treasures, 7, 401848, 3.2f),
            new CollectibleDefinition(500215, "rare_qinglan_star_core", CollectibleSeries.Treasures, 7, 519513, 1.8f),
            new CollectibleDefinition(500216, "rare_duck_king_scepter", CollectibleSeries.Treasures, 7, 692184, 6.5f),
            new CollectibleDefinition(500217, "rare_dragon_pearl", CollectibleSeries.Treasures, 7, 761633, 4.0f),
            new CollectibleDefinition(500218, "rare_first_king_egg", CollectibleSeries.Treasures, 8, 29470423, 18.0f),
        };

        internal static int Count { get { return Items.Length; } }
        internal static int SeriesCount { get { return SeriesTable.Length; } }

        internal static CollectibleDefinition At(int index)
        {
            return index >= 0 && index < Items.Length ? Items[index] : null;
        }

        /// <summary>按 TypeID 直取（表按 TypeID 连续排列）；不是收集品返回 null。</summary>
        internal static CollectibleDefinition Get(int typeId)
        {
            int index = typeId - FirstTypeId;
            if (index < 0 || index >= Items.Length) return null;
            CollectibleDefinition def = Items[index];
            return def.TypeId == typeId ? def : null;
        }

        internal static bool IsCollectible(int typeId) { return Get(typeId) != null; }

        internal static CollectibleSeriesInfo GetSeries(CollectibleSeries series)
        {
            int index = (int)series;
            return index >= 0 && index < SeriesTable.Length ? SeriesTable[index] : null;
        }
    }
}

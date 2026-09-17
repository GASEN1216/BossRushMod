// ============================================================================
// SkyIslandItems.cs - 天空岛（晴岚群岛）物品：纪念品、道具与岛上特产
// ============================================================================
// TypeID 500068-500082（台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3；常量在 Config/ConfigItemIds.cs）。
// 价值统一取 SkyIslandItemRules.ValueOf（配方经济与报告共用同一份数字）。
//
// 【零新增 bundle】与后山物品同一套克隆注册（形态照 Integration/BackMountain/BackMountainItems.cs）：
// 从既有 mod 物品克隆 prefab、改 TypeID、配属性、AddDynamicEntry；图标读 Assets/Items/<IconName>.png，
// 缺图时保持克隆源的图标，不 fail。
//
// 【三种形态】
//   - 纪念品（晴岚航徽、噬风之核）：不可使用、不可堆叠，只在剧情节点发一次
//     （发放台账在 DebugAndTools/SkyIsland/SkyIslandItemRules.cs，发放记录写进本槽群岛手记）；
//     带在背包里上岛才有用：航徽让整备与苔药半价（SkyIslandServices），在岛上使用还能拉缆绳回码头（每趟一次，SkyIslandSessionRecall）；
//     噬风之核让大风只算微风（SkyIslandFieldcraft）。
//   - 道具（风标罗盘）：使用不消耗（耐久 999，形态照鸭皇图鉴），在岛上指向信鸽或下一个目标。
//   - 岛上特产（归航菜便当、星苔药膏）：可堆叠消耗品，复用官方 FoodDrink / Drug 使用行为；
//     便当另挂一项 SkyIslandFieldcraftUsage（Meal）：菜畦重新开张之后在岛上吃，算作晴禾的归航菜。
//     从天空岛的箱子里出（SkyIslandItemRules.IslandExtraFor），也能在灶台 / 药臼合成（便当要等种植记录交还晴禾）。
//   - 群岛材料（批次三：云苔纤维、青穗草、浮木、残铜片、风晶碎片、星屑、晴岚风晶）：可堆叠、不可使用；
//     前六种只从岛上的采集点出，晴岚风晶由五片风晶碎片在渡口工台凑整（星灯亮起之后），是七盏风晶灯的灯芯（SkyIslandLights）。
//   - 局内耗材（批次三：风灯、驱风香、晴岚护符）：可堆叠，使用行为 SkyIslandFieldcraftUsage，效果由岛上会话执行、离岛失效。
// 十五件全部登记掉落黑名单（Config/LootBlacklistRegistry.cs + Assets/Data/LootBlacklist.json），不进许愿台、日报与各类品质池。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.ItemUsage;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>天空岛物品的注册、配置、本地化与发放。</summary>
    public static class SkyIslandItems
    {
        private const string LogPrefix = "[SkyIslandItems] ";

        /// <summary>风标罗盘的耐久：使用后不消耗（官方 CA_UseItem 对不可堆叠又没有耐久的物品用完即销毁）。</summary>
        private const float NonConsumableDurability = 999f;

        private enum Kind { Keepsake, Compass, Food, Medicine, Material, Consumable, Gear, Tool }

        private sealed class Definition
        {
            internal int TypeId;
            internal Kind Kind;
            internal SkyIslandFieldBuff Buff = SkyIslandFieldBuff.None;
            internal string LocKey;
            internal string DescCN;
            internal string DescEN;
            internal string IconName;
            internal int Value;
            internal int Quality;
            internal int MaxStack;
            internal float UseTime;
            internal float Energy;
            internal float Water;
            internal int Heal;
        }

        private static Definition[] _definitions;

        private static Definition[] Definitions
        {
            get
            {
                if (_definitions == null) _definitions = BuildDefinitions();
                return _definitions;
            }
        }

        private static Definition[] BuildDefinitions()
        {
            return new[]
            {
                WithUse(Make(BossRushItemIds.SkyIslandHomecomingBadge, Kind.Keepsake, "BossRush_SkyIsland_HomecomingBadge",
                    "敲响归航钟后获得的铜铃航徽。\n携带上岛：渡口整备、眠苔苔药半价。\n岛上使用：回到登云码头，每趟一次，不消耗。附近有敌人时无法使用。\n岛上死亡会随背包掉落。",
                    "A brass bell badge earned by ringing the Homecoming Bell.\nCarry on Qinglan: half-price dock refits and Miantai's remedies.\nUse on Qinglan: return to Cloudrise Dock once per raid. Not consumed; unavailable near enemies.\nDrops with your pack if you die on the isles.",
                    "sky_island_homecoming_badge", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandHomecomingBadge), 5, 1, 2f, 0f, 0f, 0),
                    SkyIslandFieldBuff.Recall),
                Make(BossRushItemIds.SkyIslandWindeaterCore, Kind.Keepsake, "BossRush_SkyIsland_WindeaterCore",
                    "噬风留下的核，壳里还有一团风在转。\n携带上岛：大风按微风计算。\n敲响归航钟后，可在双航标门引回噬风的回响。每趟一次，消耗1块晴岚风晶，核不消耗。\n岛上死亡会随背包掉落。",
                    "A remnant of the Windeater, with wind still turning inside.\nCarry on Qinglan: gales count as breezes.\nAfter ringing the Homecoming Bell, call its echo at the twin-beacon gate. Once per raid; costs 1 Qinglan Windcrystal, not the core.\nDrops with your pack if you die on the isles.",
                    "sky_island_windeater_core", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandWindeaterCore), 6, 1, 0f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandWindVaneCompass, Kind.Compass, "BossRush_SkyIsland_WindVaneCompass",
                    "浮舟的旧罗盘，指针换成了小风标。\n岛上使用：指向目标并显示距离，不消耗。\n携带蛙卵时先指蛙鸣池，否则依次找信鸽、主线、支线。\n都完成后，有晴岚风晶就找未点亮的灯，否则找未采的风晶簇。\n丢失后可在渡口工台重做。离岛无效。",
                    "Fuzhou's compass, fitted with a tiny wind vane.\nUse on Qinglan to show a target and distance. Not consumed.\nCarrying frogspawn? It points to Frogsong Pool. Otherwise: pigeons, main objectives, then side paths.\nWhen those are done: unlit lamps if you carry a crystal, or ungathered crystal clusters.\nReplace it at the dock workbench. No effect off the isles.",
                    "sky_island_wind_vane_compass", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandWindVaneCompass), 4, 1, 0.6f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandHomecomingBento, Kind.Food, "BossRush_SkyIsland_HomecomingBento",
                    "晴禾装的便当，上面插着纸风车。\n回复饱食、水分和少量生命。\n交还种植记录后，在岛上吃可提升本趟生命上限和跑速。与晴禾的归航菜共用每趟一次的增益。\n来源：群岛箱子；交还记录后可在灶台制作。",
                    "Qinghe's lunch box, topped with a paper pinwheel.\nRestores energy, water and a little health.\nAfter returning her planting record, eat on Qinglan for extra max HP and speed this raid. Shares one buff per raid with her homecoming meal.\nFound in island crates; craft at the stove after returning the record.",
                    "sky_island_homecoming_bento", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandHomecomingBento), 3, 10, 2.5f, 35f, 20f, 15),
                Make(BossRushItemIds.SkyIslandStarmossSalve, Kind.Medicine, "BossRush_SkyIsland_StarmossSalve",
                    "眠苔熬的凉药膏，打开就闻得到苔味。\n回复生命。在岛上使用还可止痒，并暂时防止叮咬发痒。\n来源：群岛箱子，或在药臼用云苔纤维和风晶碎片制作。",
                    "Miantai's cool salve smells of moss.\nRestores health. On Qinglan it also stops itching and prevents new itches for a while.\nFound in island crates, or made at the mortar from cloudmoss fiber and windcrystal shards.",
                    "sky_island_starmoss_salve", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandStarmossSalve), 4, 10, 3f, 0f, 0f, 40),

                // ---- 内容批次三：群岛材料（只从岛上的采集点出；晴岚风晶由风晶碎片凑整）----
                Material(BossRushItemIds.SkyIslandCloudmossFiber, "BossRush_SkyIsland_CloudmossFiber",
                    "晾干的云苔，纤维柔韧。\n用于药膏、驱风香、风灯，也能搓绳挂风晶灯。\n采集岛上的云苔可得。修好风标后，悬根林产量增加。",
                    "Dried cloudmoss with tough fibers.\nUsed for salves, incense, lanterns and cords to hang crystal lamps.\nGather from cloudmoss patches. Repairing the beacon increases yields in Hanging Root Wood.",
                    "sky_island_cloudmoss_fiber", 2, 30),
                Material(BossRushItemIds.SkyIslandGreenearSheaf, "BossRush_SkyIsland_GreenearSheaf",
                    "穗子能吃，秆子能编。\n用于归航菜便当和驱风香。\n采集岛上的青穗草丛可得。交还种植记录后，梯田产量增加。",
                    "Edible ears and stalks fit for weaving.\nUsed for homecoming bentos and windward incense.\nGather from greenear tufts. Returning the planting record increases terrace yields.",
                    "sky_island_greenear_sheaf", 1, 30),
                Material(BossRushItemIds.SkyIslandDriftwood, "BossRush_SkyIsland_Driftwood",
                    "云海冲上岸的木头，干得一点就着。\n用于风灯、灶火和部分风晶灯的灯杆。\n在码头、桥头和岛边的浮木处采集。",
                    "Dry wood washed ashore by the cloud sea.\nUsed for lanterns, hearths and some crystal lamp posts.\nGather from driftwood at docks, bridges and island edges.",
                    "sky_island_driftwood", 1, 20),
                Material(BossRushItemIds.SkyIslandBrassScrap, "BossRush_SkyIsland_BrassScrap",
                    "旧机件崩下的铜片，边缘还有齿。\n用于护符、罗盘和部分风晶灯罩。\n采集残铜矿脉可得，深处产量更多。点亮星灯后，工坊矿脉产量增加。",
                    "Toothed brass scraps from old machinery.\nUsed for charms, compasses and some crystal lamp housings.\nGather from brass veins; deeper isles yield more. Lighting the star lamp increases workshop yields.",
                    "sky_island_brass_scrap", 2, 30),
                Material(BossRushItemIds.SkyIslandWindcrystalShard, "BossRush_SkyIsland_WindcrystalShard",
                    "迎着风会轻响的碎晶。\n点亮星灯后，渡口工台可将5片熔成1块晴岚风晶。也用于护符、药膏和罗盘。\n采集风晶簇可得，深处铜脉偶尔也有。击败噬风后，栈道风晶产量增加。",
                    "A crystal shard that hums in the wind.\nAfter lighting the star lamp, fuse 5 into 1 Qinglan Windcrystal at the dock. Also used for charms, salves and compasses.\nGather from crystal clusters; deep brass veins sometimes hold one. Defeating the Windeater increases boardwalk yields.",
                    "sky_island_windcrystal_shard", 3, 20),
                Material(BossRushItemIds.SkyIslandStardust, "BossRush_SkyIsland_Stardust",
                    "积在风晶缝里的亮粉，夜里更多。\n用于晴岚护符；瞭台风晶灯需要2份。\n采集栈道、听雨洞和更深处的风晶簇可得，夜间概率更高。校准观星镜后，瞭台产出概率增加。",
                    "Glittering dust in windcrystal cracks, richer at night.\nUsed for Qinglan charms; the overlook lamp needs 2 portions.\nGather from clusters on the boardwalk, in the grotto and further in. More common at night and at the overlook after calibrating its telescope.",
                    "sky_island_stardust", 4, 10),
                Material(BossRushItemIds.SkyIslandQinglanWindcrystal, "BossRush_SkyIsland_QinglanWindcrystal",
                    "5片碎晶熔成的灯芯，迎风也吹不灭。\n用于七盏风晶灯。加上三处灶火，十盏全亮后岛上夜风停息，桥上仍有风。\n敲响归航钟并击败噬风后，携带噬风之核可在双航标门消耗1块引回回响。每趟一次。\n点亮星灯后，在渡口工台制作。",
                    "A wick fused from 5 shards. Wind cannot put it out.\nUsed for seven crystal lamps. Light all seven and the three hearths to stop night winds on the isles; bridges stay windy.\nAfter ringing the bell and defeating the Windeater, carry its core and spend 1 crystal at the twin-beacon gate to call its echo. Once per raid.\nCraft at the dock after lighting the star lamp.",
                    "sky_island_qinglan_windcrystal", 5, 5),

                // ---- 内容批次三：局内耗材（只在岛上的合成台做；效果只在晴岚群岛上生效，离岛失效）----
                Consumable(BossRushItemIds.SkyIslandWindLantern, SkyIslandFieldBuff.Lantern, "BossRush_SkyIsland_WindLantern",
                    "浮木灯架，云苔纸罩。\n群岛使用约4分钟：照明，免疫微风寒意，大风寒意减半。\n夜里引来云蚋；灯下的蚋不咬人，也不躲子弹。灯灭前清掉它们。\n用于钟庭风晶灯。在渡口工台制作，离岛失效。",
                    "Driftwood frame, cloudmoss shade.\nOn Qinglan for about 4 min: light, no breeze chill, half gale chill.\nAt night it attracts gnats. Those at the light neither bite nor dodge; clear them before it goes out.\nAlso used for the Bell Court lamp. Made at the dock. Ends when you leave.",
                    "sky_island_wind_lantern", 2, 5, 1.2f),
                Consumable(BossRushItemIds.SkyIslandWindwardIncense, SkyIslandFieldBuff.Incense, "BossRush_SkyIsland_WindwardIncense",
                    "云苔和青穗压成的香饼，烟是暖的。\n群岛使用约5分钟：抵挡所有风寒，加快耐力恢复。\n用于镜水寺风晶灯。在灶台或药臼制作，离岛失效。",
                    "Pressed cloudmoss and greenear with warm smoke.\nOn Qinglan for about 5 min: blocks all wind chill and speeds stamina recovery.\nAlso used for the temple lamp. Made at the stove or mortar. Ends when you leave.",
                    "sky_island_windward_incense", 3, 5, 2f),
                Consumable(BossRushItemIds.SkyIslandQinglanCharm, SkyIslandFieldBuff.Charm, "BossRush_SkyIsland_QinglanCharm",
                    "残铜底座里嵌着风晶与星屑。\n本趟群岛出击：噬风及其回响的风暴伤害−35%。生命上限、耐力恢复小幅提升。\n不叠加，离岛失效。\n来源：渡口工台制作，或回响遗存。",
                    "Windcrystal and stardust set in brass.\nThis Qinglan raid: 35% less storm damage from the Windeater and its echo. Slightly higher max HP and stamina recovery.\nDoes not stack. Ends when you leave.\nMade at the dock; also found in echo caches.",
                    "sky_island_qinglan_charm", 4, 3, 1.5f),

                // ---- 内容批次四：云蚋的对策（夜里的蚊群，SkyIslandGnats）----
                Gear(BossRushItemIds.SkyIslandCloudmossVeil, "BossRush_SkyIsland_CloudmossVeil",
                    "晴禾织的纱笠，纱里点着星屑。\n放在背包中上岛，云蚋叮咬频率约降至三分之一。\n在晴禾的灶台用云苔纤维和星屑制作。",
                    "Qinghe's woven veil, flecked with stardust.\nCarry in your pack on Qinglan to reduce gnat bites to about one-third.\nMade at Qinghe's stove from cloudmoss fiber and stardust.",
                    "sky_island_cloudmoss_veil", 4),
                Consumable(BossRushItemIds.SkyIslandGnatZapper, SkyIslandFieldBuff.Zapper, "BossRush_SkyIsland_GnatZapper",
                    "苇白调灯芯，浮舟打铜罩。\n群岛使用约5分钟：吸引12米内的云蚋，靠近灯3.2米内才逐只电落。同时最多两盏。\n点亮两盏风晶灯后，可用晴岚风晶和残铜片在渡口工台制作，每次两盏。离岛失效。",
                    "Weibai tuned the wick; Fuzhou made the brass cage.\nOn Qinglan for about 5 min: lures gnats from 12m and zaps them one at a time within 3.2m. Up to two active.\nAfter lighting two crystal lamps, craft at the dock with a Qinglan Windcrystal and brass scrap. Two per batch. Ends when you leave.",
                    "sky_island_gnat_zapper", 4, 4, 1.2f),
                Tool(BossRushItemIds.SkyIslandSmokeFan, SkyIslandFieldBuff.Fan, "BossRush_SkyIsland_SmokeFan",
                    "眠苔的旧蒲扇，浸着苔药烟味。\n群岛使用：扑落前方约3米内的云蚋，击退并眩晕稍远的蚋。\n使用不消耗，两次挥扇之间需稍等。\n在药臼用青穗草和浮木制作。离岛无效。",
                    "Miantai's old fan, steeped in moss smoke.\nOn Qinglan: knocks down gnats within about 3m ahead; pushes back and stuns those further away.\nNot consumed. Pause between sweeps.\nMade at the mortar from greenear and driftwood. No effect off the isles.",
                    "sky_island_smoke_fan", 3, 0.3f)
            };
        }

        /// <summary>随身装备（内容批次四：云苔纱笠）：不可使用、不可堆叠，放在背包顶层上岛就生效（效果在岛上的蚊群 owner 里数背包）。</summary>
        private static Definition Gear(int typeId, string locKey, string descCN, string descEN, string iconName, int quality)
        {
            return Make(typeId, Kind.Gear, locKey, descCN, descEN, iconName, SkyIslandItemRules.ValueOf(typeId), quality, 1, 0f, 0f, 0f, 0);
        }

        /// <summary>岛上的工具（内容批次四：药烟蒲扇）：使用不消耗（耐久同罗盘），效果由岛上的局内 owner 转给蚊群 owner。</summary>
        private static Definition Tool(int typeId, SkyIslandFieldBuff buff, string locKey, string descCN, string descEN, string iconName,
            int quality, float useTime)
        {
            Definition definition = Make(typeId, Kind.Tool, locKey, descCN, descEN, iconName, SkyIslandItemRules.ValueOf(typeId),
                quality, 1, useTime, 0f, 0f, 0);
            definition.Buff = buff;
            return definition;
        }

        /// <summary>给纪念品挂上岛上的使用效果（晴岚航徽：拉缆绳回码头）。噬风之核不挂，只看带没带在身上。</summary>
        private static Definition WithUse(Definition definition, SkyIslandFieldBuff buff)
        {
            definition.Buff = buff;
            return definition;
        }

        /// <summary>群岛材料：可堆叠、不可使用。</summary>
        private static Definition Material(int typeId, string locKey, string descCN, string descEN, string iconName,
            int quality, int maxStack)
        {
            return Make(typeId, Kind.Material, locKey, descCN, descEN, iconName, SkyIslandItemRules.ValueOf(typeId),
                quality, maxStack, 0f, 0f, 0f, 0);
        }

        /// <summary>局内耗材：可堆叠，使用行为是 <see cref="SkyIslandFieldcraftUsage"/>，效果由岛上会话执行。</summary>
        private static Definition Consumable(int typeId, SkyIslandFieldBuff buff, string locKey, string descCN, string descEN,
            string iconName, int quality, int maxStack, float useTime)
        {
            Definition definition = Make(typeId, Kind.Consumable, locKey, descCN, descEN, iconName,
                SkyIslandItemRules.ValueOf(typeId), quality, maxStack, useTime, 0f, 0f, 0);
            definition.Buff = buff;
            return definition;
        }

        private static Definition Make(int typeId, Kind kind, string locKey, string descCN, string descEN, string iconName,
            int value, int quality, int maxStack, float useTime, float energy, float water, int heal)
        {
            return new Definition
            {
                TypeId = typeId, Kind = kind, LocKey = locKey, DescCN = descCN, DescEN = descEN, IconName = iconName,
                Value = value, Quality = quality, MaxStack = maxStack, UseTime = useTime, Energy = energy, Water = water, Heal = heal
            };
        }

        private static Definition GetDefinition(int typeId)
        {
            Definition[] all = Definitions;
            for (int i = 0; i < all.Length; i++)
                if (all[i].TypeId == typeId) return all[i];
            return null;
        }

        #region 注册与配置

        private static bool _configuratorsRegistered;

        /// <summary>幂等注册全部物品配置器。</summary>
        public static void RegisterConfigurators()
        {
            if (_configuratorsRegistered) return;
            _configuratorsRegistered = true;
            Definition[] all = Definitions;
            for (int i = 0; i < all.Length; i++)
            {
                int typeId = all[i].TypeId;
                ItemFactory.RegisterConfigurator(typeId, delegate(Item item) { ConfigureItem(typeId, item); });
            }
            ModBehaviour.DevLog(LogPrefix + "物品配置器已注册 " + all.Length + " 件");
            // 头目 / 岛主的专属装备走装备 bundle，配置器与岛上物品同一时点登记（官方按 TypeID 实例化时补齐名字、数值与图标）。
            SkyIslandBossGearConfig.RegisterConfigurators();
        }

        private static void ConfigureItem(int typeId, Item item)
        {
            if (item == null) return;
            Definition def = GetDefinition(typeId);
            if (def == null) return;
            try
            {
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                item.DisplayNameRaw = def.LocKey;
                item.name = SkyIslandItemRules.NameEn(typeId);
                // 不设就会隐式继承克隆兜底源的堆叠上限。
                item.MaxStackCount = def.MaxStack;
                item.StackCount = 1;
                item.Value = def.Value;
                item.Quality = def.Quality;
                string description = L10n.T(def.DescCN, def.DescEN);
                ModeFItemConfigHelper.SetHiddenMember(item, "description", description);
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", description);
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelperIcon.TryInjectIcon(item, null, def.IconName);
                switch (def.Kind)
                {
                    case Kind.Keepsake:
                        // 晴岚航徽能在岛上拉缆绳回码头：不消耗（耐久同罗盘），效果由岛上的局内 owner 转给会话；噬风之核没有使用行为。
                        if (def.Buff != SkyIslandFieldBuff.None)
                        {
                            item.MaxDurability = NonConsumableDurability;
                            item.Durability = NonConsumableDurability;
                            SkyIslandFieldcraftUsage recall = Component<SkyIslandFieldcraftUsage>(item);
                            recall.buff = (int)def.Buff;
                            AttachUsage(item, def.UseTime, recall);
                        }
                        break;
                    case Kind.Compass:
                        item.MaxDurability = NonConsumableDurability;
                        item.Durability = NonConsumableDurability;
                        AttachUsage(item, def.UseTime, Component<SkyIslandCompassUsage>(item));
                        break;
                    case Kind.Food:
                        FoodDrink food = Component<FoodDrink>(item);
                        food.energyValue = def.Energy;
                        food.waterValue = def.Water;
                        Drug snack = Component<Drug>(item);
                        snack.healValue = def.Heal;
                        // 归航菜：菜畦重新开张之后在岛上吃，算作晴禾那一顿（加成在 SkyIslandServices，经岛上的局内 owner 调用）。
                        SkyIslandFieldcraftUsage meal = Component<SkyIslandFieldcraftUsage>(item);
                        meal.buff = (int)SkyIslandFieldBuff.Meal;
                        // 官方 UsageUtilities 只要有一个行为可用就能吃，且只调可用的那几项：满血时 Drug 不可用、
                        // 离岛或这趟已经吃过归航菜时 meal 不可用，照样能吃饱喝足。
                        AttachUsage(item, def.UseTime, food, snack, meal);
                        break;
                    case Kind.Medicine:
                        Drug drug = Component<Drug>(item);
                        drug.healValue = def.Heal;
                        // 内容批次四：星苔药膏顺带止痒（在岛上随时能抹，之后这一阵再被叮也不痒——防痒本来就该在出门前用）。
                        // 满血又离岛时两项都不可用、按钮置灰。
                        SkyIslandFieldcraftUsage soothe = Component<SkyIslandFieldcraftUsage>(item);
                        soothe.buff = (int)SkyIslandFieldBuff.Soothe;
                        AttachUsage(item, def.UseTime, drug, soothe);
                        break;
                    case Kind.Consumable:
                        // 离岛时 CanBeUsed 返回 false（按钮置灰），不会白吃掉一件；效果与计时在 SkyIslandFieldcraft。
                        SkyIslandFieldcraftUsage fieldUse = Component<SkyIslandFieldcraftUsage>(item);
                        fieldUse.buff = (int)def.Buff;
                        AttachUsage(item, def.UseTime, fieldUse);
                        break;
                    case Kind.Tool:
                        // 药烟蒲扇：扇一下不消耗（耐久同罗盘）；离岛或刚扇过时按钮置灰。
                        item.MaxDurability = NonConsumableDurability;
                        item.Durability = NonConsumableDurability;
                        SkyIslandFieldcraftUsage swing = Component<SkyIslandFieldcraftUsage>(item);
                        swing.buff = (int)def.Buff;
                        AttachUsage(item, def.UseTime, swing);
                        break;
                    // Kind.Material 与 Kind.Gear（云苔纱笠）：没有使用行为（克隆源带过来的已被 ClearInheritedUsage 清掉）。
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "配置物品失败 " + typeId + ": " + e.Message);
            }
        }

        private static T Component<T>(Item item) where T : Component
        {
            T existing = item.GetComponent<T>();
            return existing != null ? existing : item.gameObject.AddComponent<T>();
        }

        /// <summary>挂使用行为：克隆源带过来的行为先清掉，再挂本物品自己的；私有成员绑定复用 ModeFItemConfigHelper。</summary>
        private static void AttachUsage(Item item, float useTime, params UsageBehavior[] behaviors)
        {
            UsageUtilities usage = item.GetComponent<UsageUtilities>();
            if (usage == null) usage = item.gameObject.AddComponent<UsageUtilities>();
            if (usage.behaviors == null) usage.behaviors = new List<UsageBehavior>();
            else usage.behaviors.Clear();
            for (int i = 0; i < behaviors.Length; i++)
                if (behaviors[i] != null && !usage.behaviors.Contains(behaviors[i])) usage.behaviors.Add(behaviors[i]);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usage, useTime);
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        private static readonly HashSet<int> _runtimeRegistered = new HashSet<int>();

        /// <summary>从既有物品克隆一个 prefab 顶上。形态照 BackMountainItems.EnsureRuntimeRegistration；供 BossRushDynamicItemRegistry 调用。</summary>
        public static bool EnsureRuntimeRegistration(int typeId)
        {
            try
            {
                if (GetDefinition(typeId) == null) return false;
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); }
                catch (Exception)
                {
                    // prefab 查询失败按「尚未注册」处理，继续走克隆路径
                }
                if (existing != null)
                {
                    ConfigureItem(typeId, existing);
                    return true;
                }

                existing = ItemFactory.GetLoadedItem(typeId);
                if (existing != null)
                {
                    ConfigureItem(typeId, existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); }
                    catch (Exception)
                    {
                        // 已在表内时重复登记会抛，忽略即可
                    }
                    return true;
                }

                if (_runtimeRegistered.Contains(typeId))
                {
                    try { return ItemAssetsCollection.GetPrefab(typeId) != null; }
                    catch (Exception) { return false; }
                }

                Item source = FindRuntimeFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "找不到可克隆的兜底物品: " + typeId);
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null) return false;
                clone.gameObject.name = GetDefinition(typeId).LocKey;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(typeId);
                ConfigureItem(typeId, clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                _runtimeRegistered.Add(typeId);
                ModBehaviour.DevLog(LogPrefix + "运行时兜底物品已注册: " + typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "兜底注册失败 " + typeId + ": " + e.Message);
                return false;
            }
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds =
            {
                BossRushItemIds.RelicEgg,
                BossRushItemIds.PortableSafeZoneDevice,
                BossRushItemIds.ZombieTideBeacon,
                BossRushItemIds.ZombieTideInvitation,
            };
            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null) return prefab;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 查找兜底模板物品失败: " + e.Message);
                }
            }
            return null;
        }

        #endregion

        #region 发放

        /// <summary>
        /// 把一件天空岛物品发给玩家：<paramref name="toStorage"/> 直接寄回基地仓库，否则先放背包、放不下再寄回（官方 SendToPlayer）。
        /// 实例化之前先问 prefab：缺资源时官方给的空壳带着同一个 TypeID，回读分辨不出来（与天空岛箱子同一条纪律）。
        /// 实例准备成功之后才调用 <paramref name="recordGrant"/> 记发放台账，记成功后才转移物品。
        /// 缺资源或实例化失败不会烧掉永久领取资格；台账拒绝时清掉临时实例，不发物品。
        /// </summary>
        internal static bool TryGive(int typeId, bool toStorage, Func<bool> recordGrant, Func<bool> rollbackGrant = null)
        {
            Item item = null;
            bool transferStarted = false;
            int instanceId = 0;
            try
            {
                if (GetDefinition(typeId) == null || ItemAssetsCollection.GetPrefab(typeId) == null) return false;
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId) return false;
                instanceId = item.GetInstanceID();
                if (recordGrant == null || !recordGrant()) return false;
                transferStarted = true;
                if (toStorage) ItemUtilities.SendToPlayerStorage(item);
                else ItemUtilities.SendToPlayer(item);
                item = null;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "发放物品失败 " + typeId + ": " + e.Message);
                // 官方先放进背包/仓库，再通知；仓库缓冲成功时会销毁原实例。
                // 通知异常不能把已交付的纪念品删掉，也不应该再发第二份。
                bool owned = transferStarted && item != null
                    && (item.IsBeingDestroyed || SkyIslandInventoryTransaction.HasOwner(item));
                if (transferStarted && !owned && SkyIslandInventoryTransaction.HasBufferReceipt(instanceId, typeId))
                    owned = true;
                if (transferStarted && !owned && rollbackGrant != null)
                {
                    try
                    {
                        if (rollbackGrant())
                            return false;
                    }
                    catch (Exception rollbackError)
                    {
                        ModBehaviour.DevLog(LogPrefix + "发放台账回滚失败 " + typeId + ": " + rollbackError.Message);
                    }
                    ModBehaviour.DevLog(LogPrefix + "发放台账无法回滚，保留现有记录等待人工补偿 " + typeId);
                }
                return owned;
            }
            finally
            {
                SkyIslandInventoryTransaction.DestroyUnowned(item);
            }
        }

        #endregion

        #region 本地化与清理

        /// <summary>注入全部物品名。DisplayNameRaw 设了就必须注入，否则游戏里会显示 *BossRush_SkyIsland_...*（AGENTS.md 4.4）。</summary>
        public static void InjectLocalization()
        {
            try
            {
                var map = new Dictionary<string, string>();
                Definition[] all = Definitions;
                for (int i = 0; i < all.Length; i++)
                    map[all[i].LocKey] = SkyIslandItemRules.Name(all[i].TypeId);
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            _runtimeRegistered.Clear();
        }

        #endregion
    }
}

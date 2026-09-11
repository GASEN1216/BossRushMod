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
//     带在背包里上岛才有用：航徽让整备与苔药半价（SkyIslandServices），噬风之核让大风只算微风（SkyIslandFieldcraft）。
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

        private enum Kind { Keepsake, Compass, Food, Medicine, Material, Consumable }

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
                Make(BossRushItemIds.SkyIslandHomecomingBadge, Kind.Keepsake, "BossRush_SkyIsland_HomecomingBadge",
                    "晴岚群岛的纪念航徽：铜铃形的徽面上是一朵云和一只小帆船，只有敲响归航钟的人才拿得到。浮舟说，戴着它回来，码头永远留着一条缆绳给你——带在背包里上岛，渡口整备与眠苔的苔药都只收半价。死在岛上时它和背包里的东西一起留在原地。",
                    "A keepsake badge of the Qinglan isles: a bell-shaped face with a cloud and a little sailboat, given only to those who rang the Homecoming Bell. Fuzhou says that if you come back wearing it, the dock will always keep a mooring line for you — carry it in your pack on the isles and the dock refit and Miantai's moss remedy cost half. If you fall on the isles, it stays behind with the rest of your pack.",
                    "sky_island_homecoming_badge", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandHomecomingBadge), 5, 1, 0f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandWindeaterCore, Kind.Keepsake, "BossRush_SkyIsland_WindeaterCore",
                    "噬风散去时留下的核心，玻璃般的球壳里还锁着一小团打转的风，握在手里能感觉到它轻轻推着掌心。带在背包里上岛，那团风会把你身边的风吃掉一截：夜里的桥上、噬风将至时的栈道，大风对你只算微风。死在岛上时它和背包里的东西一起留在原地。",
                    "The heart the Windeater left behind when it broke apart. A small whirl of wind still turns inside its glassy shell, nudging your palm. Carry it in your pack on the isles and that whirl eats into the wind around you: on night bridges and on the boardwalk before the storm, a gale only counts as a breeze for you. If you fall on the isles, it stays behind with the rest of your pack.",
                    "sky_island_windeater_core", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandWindeaterCore), 6, 1, 0f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandWindVaneCompass, Kind.Compass, "BossRush_SkyIsland_WindVaneCompass",
                    "浮舟的旧罗盘，指针换成了一枚小风标。在晴岚群岛上使用：先指向还没收下的信鸽，再指向当前目标或还没了结的支线；都没有了，带着晴岚风晶时指向还缺一盏风晶灯的地方，否则指向这一趟还没采的风晶簇，并报出大致距离。使用不消耗；离开群岛它只会乱转。丢了可以在浮舟的渡口工台用残铜片和风晶碎片重做一只。",
                    "Fuzhou's old compass, its needle replaced with a tiny wind vane. Use it on the Qinglan isles: it points to an uncollected carrier pigeon first, then to your current objective or an unfinished side path; when there is nothing left, to a place still missing its windcrystal lamp if you carry a Qinglan Windcrystal, or else to a wind crystal cluster you have not gathered this trip, with a rough distance. Not consumed on use; away from the isles it just spins. Lost it? Fuzhou's dock workbench can make another from brass scrap and windcrystal shards.",
                    "sky_island_wind_vane_compass", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandWindVaneCompass), 4, 1, 0.6f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandHomecomingBento, Kind.Food, "BossRush_SkyIsland_HomecomingBento",
                    "晴禾装的便当：米饭上铺着归航菜，插着一只纸风车。回复饱食与水分，还能回一点生命。菜畦重新开张之后（种植记录交还晴禾），在晴岚群岛上吃它就算吃过晴禾的归航菜：本次出击生命上限与跑速小幅提升，与她那一顿共用一次。那时起也能在晴禾的灶台用青穗草和浮木做；天空岛的箱子里偶尔也有。",
                    "A lunch box packed by Qinghe: rice topped with homecoming greens and a little paper pinwheel. Restores energy and water, and a little health. Once the garden has reopened (Qinghe has her planting record back), eating it on the Qinglan isles counts as her homecoming meal: a little more max health and running speed for this raid, shared with her own meal. From then on it can also be cooked at Qinghe's stove from greenear and driftwood; Sky Islands crates sometimes hold one.",
                    "sky_island_homecoming_bento", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandHomecomingBento), 3, 10, 2.5f, 35f, 20f, 15),
                Make(BossRushItemIds.SkyIslandStarmossSalve, Kind.Medicine, "BossRush_SkyIsland_StarmossSalve",
                    "眠苔用星苔熬的药膏，抹上去凉丝丝的，伤口很快就不疼了。回复生命——不用付眠苔苔药的钱，也不用等她下一副药熬好。在眠苔的药臼用云苔纤维和一片风晶碎片配；天空岛的箱子里偶尔能找到。",
                    "A salve Miantai boils down from star moss. It goes on cool and the wound stops hurting almost at once. Restores health — without paying for Miantai's moss remedy or waiting for her next dose. Ground at Miantai's mortar from cloudmoss fiber and a windcrystal shard; Sky Islands crates sometimes hold one.",
                    "sky_island_starmoss_salve", SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandStarmossSalve), 4, 10, 3f, 0f, 0f, 40),

                // ---- 内容批次三：群岛材料（只从岛上的采集点出；晴岚风晶由风晶碎片凑整）----
                Material(BossRushItemIds.SkyIslandCloudmossFiber, "BossRush_SkyIsland_CloudmossFiber",
                    "晴岚群岛岩壁与树根上的云苔，扯下来晾干就是柔韧的纤维。眠苔用它配药膏和驱风香，浮舟用它糊风灯；搓成绳，还能挂起栈道和邮亭的风晶灯。在岛上的云苔处采集，风标转回来之后悬根林长得更旺。",
                    "Cloudmoss from the cliffs and roots of the Qinglan isles, pulled free and dried into a tough fibre. Miantai grinds it into salves and windward incense, Fuzhou papers wind lanterns with it, and twisted into cord it hangs the windcrystal lamps on the boardwalk and in the post hut. Gathered from cloudmoss patches; the Hanging Root Wood grows more once the wind beacon is turned back.",
                    "sky_island_cloudmoss_fiber", 2, 30),
                Material(BossRushItemIds.SkyIslandGreenearSheaf, "BossRush_SkyIsland_GreenearSheaf",
                    "青穗梯田一带长得最旺的草，穗子能吃，秆子能编。晴禾拿它装归航菜便当、焚驱风香。在岛上的青穗草丛采集，种植记录回到晴禾手里之后梯田长得更旺。",
                    "The grass that grows thickest around the Green Terraces: the ears are edible and the stalks can be woven. Qinghe packs homecoming bentos with it and burns it in windward incense. Gathered from greenear tufts; the terraces grow more once Qinghe has her planting record back.",
                    "sky_island_greenear_sheaf", 1, 30),
                Material(BossRushItemIds.SkyIslandDriftwood, "BossRush_SkyIsland_Driftwood",
                    "被云海托上岸的木头，轻、干、耐烧。风灯的骨、灶里的柴，也是栈道、镜水寺、邮亭与听雨洞那几盏风晶灯的灯杆。在码头、桥头与岛边搁浅的浮木处采集。",
                    "Wood the cloud sea carried ashore — light, dry and slow-burning. The frame of a wind lantern, fuel for the stove, and the posts for the windcrystal lamps on the boardwalk, at the temple, in the post hut and at the grotto. Gathered from stranded driftwood at the dock, on the bridges and along island edges.",
                    "sky_island_driftwood", 1, 20),
                Material(BossRushItemIds.SkyIslandBrassScrap, "BossRush_SkyIsland_BrassScrap",
                    "工坊旧机件崩落的铜片，边缘还带着齿。浮舟能把它敲成护符的底座、罗盘的壳，工坊、钟庭和听雨洞的风晶灯也要用它打灯罩。在残铜矿脉采集，越深的岛出得越多，星灯亮起之后工坊的铜脉更旺。",
                    "Brass flaked off old workshop machinery, its edge still toothed. Fuzhou beats it into charm backings and compass cases, and the windcrystal lamps at the workshop, the Bell Court and the grotto need it for their housings. Gathered from brass veins; the deeper isles yield more, and the workshop's veins yield more once the star lamp is lit.",
                    "sky_island_brass_scrap", 2, 30),
                Material(BossRushItemIds.SkyIslandWindcrystalShard, "BossRush_SkyIsland_WindcrystalShard",
                    "从风晶簇上敲下来的碎片，迎着风会轻轻发响。攒够五片，等残星工坊的星灯亮起，浮舟就能熔成一整块晴岚风晶；护符、药膏和罗盘里也要镶一两片。在风晶簇采集，深处的残铜矿脉偶尔也会带出一片；噬风散去之后，鸣风栈道的风晶簇长得更旺。",
                    "A shard knocked from a wind crystal cluster; it hums faintly when held to the wind. Once the Fallen Star Workshop's star lamp is lit, Fuzhou can fuse five of them into a whole Qinglan Windcrystal; charms, salves and compasses each take a shard or two. Gathered from wind crystal clusters, and now and then from deeper brass veins; the boardwalk clusters grow more once the Windeater is gone.",
                    "sky_island_windcrystal_shard", 3, 20),
                Material(BossRushItemIds.SkyIslandStardust, "BossRush_SkyIsland_Stardust",
                    "风晶簇缝里积下的细亮粉末，夜里格外多。晴岚护符离不开它，残星瞭台观星镜旁那盏风晶灯也要两撮。在鸣风栈道、听雨洞与更深处的风晶簇采集，夜里更容易出；观星镜校准之后，瞭台上的风晶簇更常带出星屑。",
                    "Fine glittering dust that collects in the cracks of wind crystal clusters, most of all at night. A Qinglan charm cannot be made without it, and the windcrystal lamp beside the Starfall Overlook telescope takes two pinches. Gathered from wind crystal clusters on Windsong Boardwalk, in the Rainlisten Grotto and deeper in, more often at night; once the telescope is calibrated, the overlook cluster yields it more often.",
                    "sky_island_stardust", 4, 10),
                Material(BossRushItemIds.SkyIslandQinglanWindcrystal, "BossRush_SkyIsland_QinglanWindcrystal",
                    "五片风晶碎片熔成的一整块风晶，里面锁着一缕停不下来的风——拿它当灯芯，风只会喂火、吹不灭它。群岛上七处装置旁各缺一盏风晶灯，每一盏都是一封信里的请求：灯旁暖和、夜风吹不透；岛上的灯凑满十盏（三处灶火算三盏）之后，夜里不再起风。残星工坊的星灯亮起之后，在浮舟的渡口工台用风晶碎片凑整。",
                    "A whole wind crystal fused from five shards, with a wisp of restless wind locked inside — as a wick, the wind only feeds the flame and cannot put it out. Seven devices on the isles are each missing a windcrystal lamp, and each lamp is something a letter asked for: beside a lamp it is warm and the night wind cannot get through, and once the isles have ten lights (the three hearths count as three) the nights stop blowing. Fused from windcrystal shards at Fuzhou's dock workbench once the Fallen Star Workshop's star lamp is lit.",
                    "sky_island_qinglan_windcrystal", 5, 5),

                // ---- 内容批次三：局内耗材（只在岛上的合成台做；效果只在晴岚群岛上生效，离岛失效）----
                Consumable(BossRushItemIds.SkyIslandWindLantern, SkyIslandFieldBuff.Lantern, "BossRush_SkyIsland_WindLantern",
                    "浮木作骨、云苔纤维糊罩，里面一截慢慢烧的芯。在晴岚群岛上点亮约 4 分钟：微风里不积寒意，大风里火苗压低、只挡得住一半；夜里照亮身边。归航钟庭那盏风晶灯要挂一盏风灯。在浮舟的渡口工台制作。",
                    "A driftwood frame, a cloudmoss paper shade and a slow wick. On the Qinglan isles it burns for about 4 minutes: no chill builds in a breeze, and in a gale the flame gutters and holds off only half; at night it lights your way. The windcrystal lamp at the Homecoming Bell Court hangs one. Made at Fuzhou's dock workbench.",
                    "sky_island_wind_lantern", 2, 5, 1.2f),
                Consumable(BossRushItemIds.SkyIslandWindwardIncense, SkyIslandFieldBuff.Incense, "BossRush_SkyIsland_WindwardIncense",
                    "云苔与青穗草捣成的香饼，烟是暖的。在晴岚群岛上焚起约 5 分钟：什么风都侵不了身——夜里的桥上、噬风将至时的栈道也一样——耐力恢复加快。镜水寺那盏风晶灯要在香炉里焚一炷。在晴禾的灶台或眠苔的药臼制作。",
                    "A cake of pounded cloudmoss and greenear whose smoke is warm. On the Qinglan isles it burns for about 5 minutes: no wind can chill you — not on night bridges, not on the boardwalk before the storm — and stamina recovers faster. The windcrystal lamp at Mirrorwater Temple burns one in its censer. Made at Qinghe's stove or Miantai's mortar.",
                    "sky_island_windward_incense", 3, 5, 2f),
                Consumable(BossRushItemIds.SkyIslandQinglanCharm, SkyIslandFieldBuff.Charm, "BossRush_SkyIsland_QinglanCharm",
                    "残铜作底、嵌一片风晶和一撮星屑的小护符，噬风那阵风碰上它会让开几分。本趟出击：噬风的风暴伤害 −35%，生命上限与耐力恢复小幅提升；离岛失效、不叠加。在浮舟的渡口工台制作。",
                    "A small charm on a brass backing, set with a windcrystal shard and a pinch of stardust; the Windeater's gusts give way around it. For this raid: 35% less damage from the Windeater's storm, and a little more max health and stamina recovery; it ends when you leave the isles and does not stack. Made at Fuzhou's dock workbench.",
                    "sky_island_qinglan_charm", 4, 3, 1.5f)
            };
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
                        AttachUsage(item, def.UseTime, drug);
                        break;
                    case Kind.Consumable:
                        // 离岛时 CanBeUsed 返回 false（按钮置灰），不会白吃掉一件；效果与计时在 SkyIslandFieldcraft。
                        SkyIslandFieldcraftUsage fieldUse = Component<SkyIslandFieldcraftUsage>(item);
                        fieldUse.buff = (int)def.Buff;
                        AttachUsage(item, def.UseTime, fieldUse);
                        break;
                    // Kind.Material：没有使用行为（克隆源带过来的已被 ClearInheritedUsage 清掉）。
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
        /// </summary>
        internal static bool TryGive(int typeId, bool toStorage)
        {
            Item item = null;
            try
            {
                if (GetDefinition(typeId) == null || ItemAssetsCollection.GetPrefab(typeId) == null) return false;
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId) return false;
                if (toStorage) ItemUtilities.SendToPlayerStorage(item);
                else ItemUtilities.SendToPlayer(item);
                item = null;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "发放物品失败 " + typeId + ": " + e.Message);
                return false;
            }
            finally
            {
                if (item != null)
                {
                    try { item.DestroyTree(); }
                    catch (Exception)
                    {
                        // 没送出去的实例清理失败不影响返回值
                    }
                }
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

using System;
using System.Collections.Generic;
using System.Text;
using Duckov.MiniMaps;
using ItemStatsSystem;
using UnityEngine;
using Cat = BossRush.SkyIslandLootCategory;
using Tier = BossRush.SkyIslandLootTier;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的搜刮箱 owner。箱子是官方 `InteractableLootbox`（`SkyIslandRewardCrate.Build`，交互名「搜集」），
    /// 物品从官方物资池按档次与类别抽（`SkyIslandLootPools`，带价值上限与黑名单）。走近 60 m 才建，0.5 秒最多建一个；
    /// 泡在深水里的箱子收起来（原型规则：水深超过 0.5 m 不能搜），水退了再放出来——滩涂、沉船、金库的价值来自这一点。
    /// </summary>
    internal sealed partial class TidegateLoot : IDisposable
    {
        private const float ActivationRange = 60f;
        private const float TickInterval = 0.5f;
        /// <summary>
        /// 玩家贴着箱子时不收起：官方搜刮界面可能正开着。只管「收起」这一个方向——被淹的箱子已经停用、没有界面开着它，
        /// 水退了就马上放出来，站在旁边等退潮的人不用先走开再回来。
        /// </summary>
        private const float KeepNearRange = 4f;
        /// <summary>走到被淹的箱柜跟前这么近，提示一次「等退潮」（模型是场景静态件、照样看得见，交互体却停用了）。</summary>
        private const float FloodHintRange = 3.5f;

        /// <summary>
        /// 剧情箱柜：场景包标记名 → 原型布局（`v2/js/layout.js` 与导出的 `tidegate_layout.json` 的 `fixed`）里这只箱子的固定内容。
        /// 布局重导会重新编号：`tools/build_tidegate_navigation.py --check` 逐条核对这张表与布局，对不上就失败（CR-2026-11-05-026）。
        /// </summary>
        internal static readonly string[,] StoryContainers =
        {
            { "LOOT_safe_68", "page1" },     // 第三水厂控制室保险箱
            { "LOOT_house_69", "page2" },    // 镇区泵站值班桌
            { "LOOT_house_70", "page3" },    // 潮位站记录柜
            { "LOOT_vault_71", "page4" },    // 储蓄所所长的保险柜
            { "LOOT_vault_71", "tidewatch" },
            { "LOOT_locker_72", "wheel" },   // 挖沙船船舱储物柜
            { "LOOT_safe_77", "goldbar" },   // 沉船船长的保险柜：原型放一根小金条，正式版放一件金库档的值钱物
        };

        private sealed class Point
        {
            internal Transform Marker;
            internal string Id, Kind;
            internal float Yaw, Ground;
            internal bool Lagoon;
            internal InteractableLootbox Box;
            internal bool Built, Failed, Flooded, Opened, FloodHinted;
            internal Transform BuriedVisual;
            internal Shipment Offer;
        }

        private sealed class Spec
        {
            internal readonly Tier Tier;
            internal readonly Cat First;
            internal readonly Cat[] Extras;
            internal Spec(Tier tier, Cat first, params Cat[] extras) { Tier = tier; First = first; Extras = extras; }
        }

        private static readonly Spec House = new Spec(Tier.Supply, Cat.Pantry, Cat.Pantry, Cat.General, Cat.Medical);
        private static readonly Spec Tool = new Spec(Tier.Supply, Cat.Workshop, Cat.Workshop, Cat.Workshop, Cat.General);
        private static readonly Spec Fish = new Spec(Tier.Supply, Cat.Fishing, Cat.Fishing, Cat.Fishing, Cat.General);
        private static readonly Spec Med = new Spec(Tier.Supply, Cat.Medical, Cat.Medical, Cat.Supplies);
        private static readonly Spec Crate = new Spec(Tier.Supply, Cat.General, Cat.Workshop, Cat.General, Cat.Ammo, Cat.Pantry);
        private static readonly Spec Buried = new Spec(Tier.Voyage, Cat.General, Cat.General, Cat.Valuable, Cat.Gear);
        private static readonly Spec Wreck = new Spec(Tier.Voyage, Cat.Gear, Cat.Weapon, Cat.Ammo, Cat.General);
        private static readonly Spec Safe = new Spec(Tier.Voyage, Cat.Valuable, Cat.General, Cat.General, Cat.Gear);
        private static readonly Spec Locker = new Spec(Tier.Voyage, Cat.Gear, Cat.General, Cat.Weapon, Cat.Ammo);
        private static readonly Spec Vault = new Spec(Tier.Starworks, Cat.Valuable, Cat.General, Cat.General, Cat.Gear);

        // 精品只换危险箱原有的一格；剧情物、普通生活物资与其它地图均不使用这张表。
        internal static int PrizeFloor(string pointId, string kind)
        {
            if (HasFixed(pointId, "goldbar") || HasFixed(pointId, "tidewatch")) return 6;
            if (kind == "vault") return 5;
            if (kind == "buried" || kind == "wreck" || HasFixed(pointId, "wheel") || HasFixed(pointId, "page1")) return 4;
            return 0;
        }

        private static int PrizeCeiling(string kind, int floor)
        {
            if (floor >= 5) return floor + 2;
            return kind == "wreck" || kind == "locker" ? 6 : 5;
        }

        private static int RollPrizeGrade(int floor, int ceiling, System.Random random)
        {
            int roll = random.Next(100);
            if (floor == 6) return roll < 78 ? 6 : roll < 98 ? 7 : 8;
            if (floor == 5) return roll < 68 ? 5 : roll < 94 ? 6 : 7;
            if (ceiling == 6) return roll < 55 ? 4 : roll < 90 ? 5 : 6;
            return roll < 72 ? 4 : 5;
        }

        /// <summary>本次进图的官方现货。先掷品质，再在该品质的合法实物中抽样，避免商品目录大的档次变相挤掉稀有档。</summary>
        internal sealed class PrizeCatalog
        {
            private readonly Dictionary<Cat, List<int>[]> stock = new Dictionary<Cat, List<int>[]>();
            private static readonly Cat[] Themes = { Cat.Valuable, Cat.Gear, Cat.Weapon, Cat.Medical, Cat.Workshop, Cat.General };
            private const int BatchSize = 32;

            // 高品质的一发子弹或廉价配方仍然不是值钱的一格；按官方标价筛选，可卖性走商店同一个判断。
            private static int MinPrizeValue(int grade)
            {
                return grade >= 7 ? 12000 : grade == 6 ? 7500 : grade == 5 ? 2500 : 1000;
            }

            internal static bool MeetsPrizeTerms(Item item)
            {
                if (item == null || item.Quality < 4 || item.Quality > 8 || !item.CanBeSold
                    || item.Value < MinPrizeValue(item.Quality) || !SkyIslandLootTables.AllowedInPool(item.Value)) return false;
                // 某些跨地图通行证可以卖，官方只给 Key / SpecialKey；别用精品槽绕过原版的取得条件。
                return item.Tags != null && !item.Tags.Contains("Key") && !item.Tags.Contains("SpecialKey");
            }

            private System.Collections.IEnumerator Prepare(Cat category)
            {
                if (stock.ContainsKey(category)) yield break;
                var grades = new List<int>[9];
                // 官方类别、查询黑名单及价格上限和天空岛相同；只细分已允许的 4–8 档。
                int[] ids = SkyIslandLootPools.GetCategoryBand(category, 4, 8);
                if (ids != null)
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int id = ids[i];
                        try
                        {
                            var item = SkyIslandEnemyArmoryRules.IsOwnModItem(id) ? null : ItemAssetsCollection.GetPrefab(id);
                            if (item != null && item.TypeID == id && MeetsPrizeTerms(item))
                            {
                                var band = grades[item.Quality] ?? (grades[item.Quality] = new List<int>());
                                band.Add(id);
                            }
                        }
                        catch (Exception) { /* 移除/失效的预制件不是可交付的货。 */ }
                        if ((i + 1) % BatchSize == 0) yield return null;
                    }
                stock[category] = grades;
            }

            internal System.Collections.IEnumerator Prewarm()
            {
                foreach (Cat category in Themes)
                {
                    var prepare = Prepare(category);
                    while (prepare.MoveNext()) yield return prepare.Current;
                    yield return null;
                }
            }

            private List<int>[] Get(Cat category)
            {
                List<int>[] result;
                if (!stock.TryGetValue(category, out result))
                {
                    var build = Prepare(category);
                    while (build.MoveNext()) { }
                    result = stock[category];
                }
                return result;
            }

            internal int Pick(Cat category, int preferred, int floor, int ceiling, System.Random random, Cat fallback)
            {
                if (floor < 4 || ceiling > 8 || preferred < floor || preferred > ceiling) return 0;
                List<int>[] primary = Get(category), secondary = fallback == category ? primary : Get(fallback);
                for (int offset = 0; offset <= ceiling - floor; offset++)
                {
                    // 同档本类优先，再找指定备用类；目标档缺货时选最近的可用档，不跨过精品底线。
                    for (int side = 0; side < (offset == 0 ? 1 : 2); side++)
                    {
                        int grade = preferred + (side == 0 ? -offset : offset);
                        if (grade < floor || grade > ceiling) continue;
                        List<int> band = primary[grade] ?? secondary[grade];
                        if (band != null) return band[random.Next(band.Count)];
                    }
                }
                return 0;
            }
        }

        private readonly PrizeCatalog prizes;
        internal int PickPrize(Cat category, int preferred, int floor, int ceiling, System.Random random, Cat fallbackCategory = Cat.General)
        {
            return prizes.Pick(category, preferred, floor, ceiling, random, fallbackCategory);
        }

        private int RolledPrize(Point point, int floor, System.Random random, out Cat category)
        {
            int ceiling = PrizeCeiling(point.Kind, floor);
            int grade = RollPrizeGrade(floor, ceiling, random);
            int theme = random.Next(100);
            if (HasFixed(point.Id, "goldbar"))
                // 船长私藏：随身武器、护具、船上专用仪器，有时才是真正的宝石。
                category = theme < 38 ? Cat.Weapon : theme < 68 ? Cat.Gear : theme < 83 ? Cat.Workshop : theme < 93 ? Cat.Valuable : Cat.General;
            else if (point.Kind == "vault" && grade >= 6)
                // 储蓄所收的抵押品不全是珠宝。官方 6 档只有一种珠宝，偶尔见到比每次见到更有意义。
                category = theme < 41 ? Cat.Gear : theme < 65 ? Cat.Weapon : theme < 78 ? Cat.Valuable : Cat.General;
            else if (point.Kind == "wreck" || point.Kind == "locker")
                category = theme < 40 ? Cat.Weapon : Cat.Gear;
            else if (HasFixed(point.Id, "page1"))
                category = theme < 75 ? Cat.Workshop : Cat.Gear;
            else
                category = Cat.Valuable;
            return PickPrize(category, grade, floor, ceiling, random, Cat.General);
        }

        // 两条装运线竞争同一个空船位。先开封的那批留下，另一批上船；两个地方的原有箱子都可以继续搜。
        private sealed class Delivery
        {
            internal readonly string Place, Kind, Key, Zh, En;
            internal readonly Cat Category;
            internal Delivery(string place, string kind, string key, Cat category, string zh, string en)
            { Place = "POI_" + place; Kind = kind; Key = "BossRush_TidegateCargo_" + key; Category = category; Zh = zh; En = en; }
        }
        private static readonly Delivery[] Deliveries =
        {
            new Delivery("栈桥", "crate", "Pier", Cat.Medical, "栈桥救急货", "Ferry pier: medical supplies"),
            new Delivery("第三水厂", "tool", "Works", Cat.Workshop, "水厂检修货（有守卫）", "Waterworks: parts (guarded)"),
            new Delivery("滩涂", "buried", "Flats", Cat.Valuable, "滩涂寄存货（退水可取）", "Mudflats: valuables (ebb access)")
        };
        private sealed class Shipment
        {
            internal Delivery Route;
            internal Point Target;
            internal int Item;
            internal GameObject Pin;
            internal bool Reserved;
            internal int OrdinaryItem, OrdinaryAmount;
            internal Cat OrdinaryCategory;
        }

        private readonly List<Shipment> offers = new List<Shipment>(2);
        private bool cargoHinted;
        private const string ForemanCabinet = "LOOT_safe_35";
        private string shiftKeyBox, luckyBox;
        private bool carryingShiftKey, openedCabinet, lockExplained;

        // 老镇西南红色检修柜的轮班钥匙挂在盖内，码头领班柜用。它属于这趟出击，撤离后不能带去别图开门。
        private void PrepareShift()
        {
            var r = SkyIslandLootTables.CreateStream(seed, "Tidegate#shift");
            var keySites = points.FindAll(p => p.Id == "LOOT_tool_3" || p.Id == "LOOT_tool_4");
            if (keySites.Count != 0 && points.Exists(p => p.Id == ForemanCabinet))
                shiftKeyBox = keySites[r.Next(keySites.Count)].Id;
            if (r.Next(100) >= 12) return;
            var candidates = points.FindAll(p => !IsFixedContainer(p.Id) && (p.Kind == "fish" || p.Kind == "med" || p.Kind == "tool"));
            if (candidates.Count > 0) luckyBox = candidates[r.Next(candidates.Count)].Id;
        }

        private bool Locked(Point p) { return p.Id == ForemanCabinet && shiftKeyBox != null && !carryingShiftKey && !openedCabinet; }

        internal string ManifestObjective
        {
            get
            {
                if (offers.Count == 0) return null;
                if (!cargoHinted) return L10n.T("可选：翻一个普通箱，找今天的装船货单", "Optional: search an ordinary box for today's manifest");
                if (offers[0].Pin == null || (offers.Count == 2 && offers[1].Pin == null))
                {
                    var first = offers[0].Route;
                    if (offers.Count == 1) return L10n.T("可选：取" + first.Zh, "Optional shipment: " + first.En);
                    var second = offers[1].Route;
                    return L10n.T("任选一处：" + first.Zh + " / " + second.Zh, "Choose one: " + first.En + " / " + second.En);
                }
                if (offers.Count == 2) return L10n.T("可选：按 M 看两批货，先开哪箱就留哪批", "Optional: press M for two shipments; the first seal you open is yours");
                return L10n.T("可选：按 M 找到剩下的那批货", "Optional: press M to find the remaining shipment");
            }
        }

        internal static void InjectCargoLabels()
        {
            foreach (Delivery d in Deliveries) LocalizationHelper.InjectLocalization(d.Key, L10n.T(d.Zh, d.En));
        }

        private readonly List<Point> points = new List<Point>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly int seed;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> announce;
        private float nextTick;
        private int opened;
        private bool closed, subscribed;

        internal int OpenedCount { get { return opened; } }
        /// <summary>某个搜刮点第一次被打开（参数是标记名 `LOOT_<种类>_<序号>`）。</summary>
        internal event Action<string> Opened;
        internal int PointCount { get { return points.Count; } }

        internal TidegateLoot(TidegateWorld world, TidegateTide tide, int seed, Func<bool> valid, Action<string, bool> announce, PrizeCatalog catalog = null)
        {
            this.world = world;
            this.tide = tide;
            this.seed = seed;
            this.valid = valid;
            this.announce = announce;
            prizes = catalog ?? new PrizeCatalog();
            foreach (Transform marker in world.LootMarkers)
            {
                // LOOT_<种类>_<序号>
                string[] parts = marker.name.Split('_');
                var point = new Point { Marker = marker, Id = marker.name, Kind = parts.Length >= 2 ? parts[1] : "crate", Yaw = marker.eulerAngles.y };
                if (point.Kind == "buried")
                {
                    point.BuriedVisual = world.Root.transform.Find("Visual/VIS_containers/" + point.Id);
                    if (!world.TryGroundAt(marker.position, out point.Ground)) point.Ground = marker.position.y;
                    point.Lagoon = world.InLagoon(marker.position);
                    point.Flooded = IsFlooded(point);
                    if (point.BuriedVisual != null) point.BuriedVisual.gameObject.SetActive(!point.Flooded);
                }
                points.Add(point);
            }
            ChooseDelivery();
            PrepareShift();
            InteractableLootbox.OnStartLoot += OnStartLoot;
            subscribed = true;
        }

        private void ChooseDelivery()
        {
            var random = SkyIslandLootTables.CreateStream(seed, "Tidegate#manifest");
            var available = new List<Shipment>(Deliveries.Length);
            foreach (Delivery d in Deliveries)
            {
                Transform place = world.Pois.Find(p => p.name == d.Place);
                if (place == null) continue;
                Point target = null;
                int sites = 0;
                foreach (Point p in points)
                {
                    if (p.Kind != d.Kind || IsFixedContainer(p.Id)) continue;
                    if ((p.Marker.position - place.position).sqrMagnitude >= 30f * 30f) continue;
                    if (random.Next(++sites) == 0) target = p;
                }
                if (target == null) continue;
                int floor = d.Category == Cat.Valuable ? 5 : 4;
                int preferred = floor == 5 || random.Next(100) < 25 ? 5 : 4;
                int item = PickPrize(d.Category, preferred, floor, 5, random, d.Category);
                if (!CanShip(item)) continue;
                available.Add(new Shipment { Route = d, Target = target, Item = item });
            }
            while (available.Count > 0 && offers.Count < 2)
            {
                int i = random.Next(available.Count);
                Shipment pick = available[i];
                available.RemoveAt(i);
                pick.Target.Offer = pick;
                offers.Add(pick);
            }
        }

        // 官方奖池允许残留没有预制件的旧 ID。不能向玩家报一张实际派不出货的运单。
        private static bool CanShip(int typeId)
        {
            if (typeId <= 0) return false;
            try { return ItemAssetsCollection.GetPrefab(typeId) != null; }
            catch (Exception) { return false; }
        }

        private static bool IsFixedContainer(string id)
        {
            for (int i = 0; i < StoryContainers.GetLength(0); i++) if (StoryContainers[i, 0] == id) return true;
            return false;
        }

        /// <summary>这只箱柜在剧情表里有没有这个固定内容标签。</summary>
        internal static bool HasFixed(string pointId, string tag)
        {
            for (int i = 0; i < StoryContainers.GetLength(0); i++)
                if (StoryContainers[i, 0] == pointId && StoryContainers[i, 1] == tag) return true;
            return false;
        }

        /// <summary>
        /// 四页闸工日志的出处（剧情表里 `page1`～`page4` 的四个箱柜）。
        /// 翻开就算找到（存档权威在 <see cref="TidegateStory.RecordPage"/>），正文进官方笔记图鉴。
        /// </summary>
        internal static int PageIndexOf(string pointId)
        {
            for (int page = 0; page < TidegateStory.PageCount; page++)
                if (HasFixed(pointId, "page" + (page + 1))) return page;
            return -1;
        }

        /// <summary>
        /// 每趟固定放进箱子的潮闸镇物品：船舱储物柜里的阀门手轮、所长保险柜里的潮汐怀表；滩涂埋货有小概率出鸭蹼脚掌。
        /// 怀表只在玩家身上没带着一块时放（带着它进来看潮的人不会再白拿一块 6000 的去卖）。返回 0 表示没有。
        /// </summary>
        internal static int FixedItemOf(string pointId, string kind, System.Random random, bool carryingWatch)
        {
            if (HasFixed(pointId, "tidewatch")) return carryingWatch ? 0 : BossRushItemIds.TidegateTideWatch;
            if (HasFixed(pointId, "wheel")) return BossRushItemIds.TidegateHandwheel;
            if (kind == "buried" && random.Next(100) < FlippersChancePercent) return BossRushItemIds.TidegateFlippers;
            return 0;
        }

        /// <summary>滩涂埋货出鸭蹼脚掌的概率（%）。六个埋货点、只在退潮时够得着，一趟大约三成能见到一双。</summary>
        internal const int FlippersChancePercent = 6;

        /// <summary>
        /// 箱子种类 → 档次与格子类别。镇上生活物资为主；要等退潮或开闸才够得着的（滩涂埋货、沉船、金库）和
        /// 有人守的（保险箱）档次更高，这是「跟着潮水走」的回报。
        /// </summary>
        private static Spec SpecOf(string kind)
        {
            switch (kind)
            {
                case "house": return House;
                case "tool": return Tool;
                case "fish": return Fish;
                case "med": return Med;
                case "buried": return Buried;
                case "wreck": return Wreck;
                case "safe": return Safe;
                case "locker": return Locker;
                case "vault": return Vault;
                default: return Crate;
            }
        }

        // 容量对应作者 kit-props 的实际模型：矮渔篓、三抽屉家柜、方木箱、多屉检修柜/分层医疗柜、整座金库柜。
        internal static int RollCount(string kind, int roll)
        {
            if (kind == "vault") return roll < 25 ? 3 : roll < 65 ? 4 : roll < 90 ? 5 : 6;
            if (kind == "buried" || kind == "safe" || kind == "wreck" || kind == "locker")
                return roll < 12 ? 1 : roll < 40 ? 2 : roll < 67 ? 3 : roll < 90 ? 4 : 5;
            if (kind == "fish") return roll < 15 ? 1 : roll < 70 ? 2 : 3;
            if (kind == "house") return roll < 12 ? 1 : roll < 62 ? 2 : roll < 94 ? 3 : 4;
            if (kind == "tool" || kind == "med") return roll < 36 ? 2 : roll < 76 ? 3 : roll < 94 ? 4 : 5;
            return roll < 5 ? 1 : roll < 48 ? 2 : roll < 85 ? 3 : roll < 97 ? 4 : 5;
        }

        // 一张班次单决定头两格的搭配；ID 仍须在本机官方同类别、同品质带、已过黑名单与价限的池中。
        private static int ShiftItem(string kind, int variant, int slot, ref Cat category)
        {
            if (slot > 1) return 0;
            int want;
            if (kind == "tool") { category = Cat.Workshop; want = variant == 0 ? (slot == 0 ? 332 : 335) : (slot == 0 ? 91 : 30); }
            else if (kind == "fish")
            {
                category = slot == 0 ? Cat.Fishing : (variant == 0 ? Cat.Workshop : Cat.Pantry);
                want = slot == 0 ? 1106 : (variant == 0 ? 87 : 13);
            }
            else if (kind == "med")
            {
                category = slot == 0 || variant == 0 ? Cat.Medical : Cat.Pantry;
                want = variant == 0 ? (slot == 0 ? 10 : 20) : (slot == 0 ? 17 : 428);
            }
            else return 0;
            var stock = SkyIslandLootPools.GetCategoryBand(category, SkyIslandLootTables.MinQuality(Tier.Supply), SkyIslandLootTables.MaxQuality(Tier.Supply));
            return stock != null && Array.IndexOf(stock, want) >= 0 ? want : 0;
        }

        internal static System.Collections.IEnumerator PrewarmThemes()
        {
            foreach (Spec spec in new[] { House, Tool, Fish, Med, Crate, Buried, Wreck, Safe, Locker, Vault })
            {
                int min = SkyIslandLootTables.MinQuality(spec.Tier), max = SkyIslandLootTables.MaxQuality(spec.Tier);
                for (int i = -1; i < spec.Extras.Length; i++)
                {
                    Cat category = i < 0 ? spec.First : spec.Extras[i];
                    if (category == Cat.General || SkyIslandLootPools.IsCategoryCached(category, min, max)) continue;
                    SkyIslandLootPools.GetCategoryBand(category, min, max);
                    yield return null;
                }
            }
            foreach (Delivery d in Deliveries)
            {
                int min = SkyIslandLootTables.MinQuality(Tier.Voyage), max = SkyIslandLootTables.MaxQuality(Tier.Voyage);
                if (SkyIslandLootPools.IsCategoryCached(d.Category, min, max)) continue;
                SkyIslandLootPools.GetCategoryBand(d.Category, min, max);
                yield return null;
            }
        }

        private bool IsFlooded(Point point)
        {
            float level = point.Lagoon ? tide.LagoonLevel : tide.Level;
            return point.Kind == "buried" ? level > point.Ground + 0.12f : TidegateTideRules.IsDeep(level, point.Ground);
        }

        internal void Tick(CharacterMainControl player)
        {
            if (closed || player == null || Time.time < nextTick || valid == null || !valid()) return;
            nextTick = Time.time + TickInterval;
            Vector3 origin = player.transform.position;
            Point nearest = null;
            float best = ActivationRange * ActivationRange;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                float distance = (point.Marker.position - origin).sqrMagnitude;
                if (!point.Built && point.BuriedVisual != null)
                {
                    point.Flooded = IsFlooded(point);
                    if (point.BuriedVisual.gameObject.activeSelf == point.Flooded) point.BuriedVisual.gameObject.SetActive(!point.Flooded);
                }
                if (point.Built && point.Box != null)
                {
                    bool flooded = IsFlooded(point);
                    if (Locked(point) && !lockExplained && distance <= FloodHintRange * FloodHintRange && announce != null)
                    {
                        lockExplained = true;
                        announce(L10n.T("二号仓库领班柜锁着。牌上写着：轮班钥匙留在老镇五金店的红工具柜里。", "Warehouse 2 foreman's cabinet locked. Tag: shift key in a red tool chest at the Old Town hardware store."), false);
                    }
                    // 直接摸到目标也先看得到两种选择，开官方搜刮窗才算选了这批。
                    if (!cargoHinted && point.Offer != null && !flooded && distance <= FloodHintRange * FloodHintRange)
                        RevealOffers(true);
                    if (flooded != point.Flooded && (!flooded || distance > KeepNearRange * KeepNearRange))
                    {
                        point.Flooded = flooded;
                        point.Box.gameObject.SetActive(!flooded && !Locked(point));
                        if (point.BuriedVisual != null) point.BuriedVisual.gameObject.SetActive(!flooded);
                        if (!flooded) point.FloodHinted = false;
                    }
                    // 被淹的箱柜：模型还在、交互没了，走到跟前提示一次，水退之前不再说（CR-2026-11-05-024）。
                    if (point.Flooded && !point.FloodHinted && distance <= FloodHintRange * FloodHintRange && announce != null)
                    {
                        point.FloodHinted = true;
                        announce(L10n.T("这只箱子泡在深水里，搜不了，等退潮再来。", "This container is under deep water. Come back at low tide."), false);
                    }
                }
                if (point.Built || point.Failed || distance >= best) continue;
                best = distance;
                nearest = point;
            }
            // 一次只建一个：走进一片新街区时不集中做实例化与物品创建。
            if (nearest != null) Build(nearest, player);
        }

        private void Build(Point point, CharacterMainControl player)
        {
            point.Built = true;
            string error;
            InteractableLootbox box = SkyIslandRewardCrate.Build(world.Root.transform, point.Marker.position, point.Yaw,
                "TidegateLoot_" + point.Id, out error, true);
            if (box == null)
            {
                point.Failed = true;
                if (point.Id == shiftKeyBox)
                {
                    shiftKeyBox = null; // 唯一钥匙源缺模型时开放普通柜，不能留下无解的锁。
                    Point cabinet = points.Find(p => p.Id == ForemanCabinet);
                    if (cabinet != null && cabinet.Box != null && !cabinet.Flooded) cabinet.Box.gameObject.SetActive(true);
                }
                if (point.Offer != null) LoseOffer(point.Offer);
                Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮点 " + point.Id + " 准备失败：" + error);
                return;
            }
            point.Box = box;
            // 场景包里每个搜刮点本来就摆着自己的箱柜模型（TG_containers）；官方尸体箱的模型藏起来，只留交互与库存。
            foreach (Renderer renderer in box.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Spec spec = SpecOf(point.Kind);
            var random = SkyIslandLootTables.CreateStream(seed, point.Id);
            int count = RollCount(point.Kind, random.Next(100));
            int kit = SkyIslandLootTables.CreateStream(seed, point.Id + "#shift-kit").Next(3); // 第三种保留杂物和自行搭配
            int prizeFloor = PrizeFloor(point.Id, point.Kind);
            var prizeRandom = prizeFloor > 0 ? SkyIslandLootTables.CreateStream(seed, point.Id + "#prize") : null;
            bool captain = HasFixed(point.Id, "goldbar");
            var log = new StringBuilder();
            int added = 0;
            for (int i = 0; i < count; i++)
            {
                Cat category = i == 0 ? spec.First : spec.Extras[random.Next(spec.Extras.Length)];
                int typeId = kit < 2 ? ShiftItem(point.Kind, kit, i, ref category) : 0;
                if (typeId == 0) typeId = SkyIslandLootPools.PickCategory(category, spec.Tier, random);
                int amount = typeId > 0 && category == Cat.Ammo ? 20 + random.Next(31) : 1;
                if (typeId == 0) { category = Cat.General; typeId = SkyIslandLootPools.Pick(spec.Tier, false, random); }
                if (i == 0 && prizeFloor > 0 && !captain)
                {
                    Cat prizeCategory;
                    int prize = RolledPrize(point, prizeFloor, prizeRandom, out prizeCategory);
                    if (prize > 0) { typeId = prize; amount = 1; category = prizeCategory; }
                    else log.Append(" prize_stock_unavailable");
                }
                if (i == 0 && (point.Id == luckyBox || (point.Id == ForemanCabinet && shiftKeyBox != null)))
                {
                    Cat theme = point.Kind == "med" ? Cat.Medical : point.Kind == "tool" ? Cat.Workshop : Cat.Valuable;
                    int surprise = PickPrize(theme, 4, 4, 4, random, theme);
                    if (surprise > 0) { typeId = surprise; amount = 1; category = theme; }
                }
                if (DeferCargoSlot(point, i, count, typeId, amount, category)) continue;
                if (SkyIslandRewardCrate.AddOfficialStack(box, typeId, amount, log, category.ToString())) added++;
            }
            bool carryingWatch = HasFixed(point.Id, "tidewatch") && TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch);
            int fixedItem = FixedItemOf(point.Id, point.Kind, SkyIslandLootTables.CreateStream(seed, point.Id + "#story"), carryingWatch);
            if (fixedItem != 0 && SkyIslandRewardCrate.AddOfficialStack(box, fixedItem, 1, log, "Tidegate")) added++;
            // 沉船船长的保险柜：原型里固定一根小金条（低潮才够得着、涨潮最先淹），正式版从金库档的值钱物里抽一件，同样过价值上限与黑名单。
            if (HasFixed(point.Id, "goldbar"))
            {
                Cat prizeCategory;
                int valuable = RolledPrize(point, prizeFloor, prizeRandom, out prizeCategory);
                if (valuable != 0 && SkyIslandRewardCrate.AddOfficialStack(box, valuable, 1, log, "TidegateWreck")) added++;
                else log.Append(" captain_prize_unavailable");
            }
            Vector3 position = point.Marker.position;
            if (!world.TryGroundAt(position, out point.Ground)) point.Ground = position.y;
            point.Lagoon = world.InLagoon(position);
            point.Flooded = IsFlooded(point);
            if (point.Kind == "buried")
            {
                if (point.BuriedVisual != null) point.BuriedVisual.gameObject.SetActive(!point.Flooded);
            }
            if (point.Flooded || Locked(point)) box.gameObject.SetActive(false);
            else if (!cargoHinted && point.Offer != null && (position - player.transform.position).sqrMagnitude <= FloodHintRange * FloodHintRange)
                RevealOffers(true);
            Debug.Log(TidegateRaidScene.LogTag + " LOOT_READY id=" + point.Id + " tier=" + spec.Tier + " items=" + added
                + " flooded=" + point.Flooded + " " + log);
        }

        private void OnStartLoot(InteractableLootbox box)
        {
            if (closed || box == null) return;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.Box != box || point.Opened || point.Flooded || Locked(point)) continue;
                point.Opened = true;
                opened++;
                Action<string> handler = Opened;
                if (handler != null) handler(point.Id);
                ReadDelivery(point);
                if (point.Id == shiftKeyBox && !carryingShiftKey && !openedCabinet)
                {
                    carryingShiftKey = true;
                    Point cabinet = points.Find(p => p.Id == ForemanCabinet);
                    if (cabinet != null && cabinet.Box != null && !cabinet.Flooded) cabinet.Box.gameObject.SetActive(true);
                    if (announce != null) announce(L10n.T("箱盖里挂着轮班钥匙：已取下。本趟可开码头二号仓库的领班柜。", "A shift key hangs under the lid. Taken: it opens the foreman's cabinet in dock warehouse 2 this raid."), true);
                }
                else if (point.Id == ForemanCabinet && carryingShiftKey)
                {
                    openedCabinet = true; carryingShiftKey = false;
                    if (announce != null) announce(L10n.T("轮班钥匙留在锁孔里。领班柜已打开。", "The shift key stays in the lock. The foreman's cabinet is open."), false);
                }
                return;
            }
        }

        // 正常抽完箱子，再给两条线路各预留末格。只有翻箱时才装入胜出的那份实物；
        // 无需往另一只库存里伸手删除物品。哪怕两箱都预建、或总共只有一格，也沿用官方原格数。
        private static bool DeferCargoSlot(Point point, int index, int count, int typeId, int amount, Cat category)
        {
            Shipment offer = point.Offer;
            if (offer == null || index != count - 1) return false;
            offer.Reserved = true;
            offer.OrdinaryItem = typeId;
            offer.OrdinaryAmount = amount;
            offer.OrdinaryCategory = category;
            return true;
        }

        private static void RestoreOrdinary(Shipment offer)
        {
            if (!offer.Reserved) return;
            offer.Reserved = false;
            if (offer.Target.Box != null && offer.OrdinaryItem > 0)
                SkyIslandRewardCrate.AddOfficialStack(offer.Target.Box, offer.OrdinaryItem, offer.OrdinaryAmount,
                    new StringBuilder(), offer.OrdinaryCategory.ToString());
        }

        private void ReadDelivery(Point point)
        {
            if (offers.Count == 0) return;
            Shipment take = point.Offer;
            if (take == null)
            {
                if (!cargoHinted && !IsFixedContainer(point.Id)) RevealOffers(false);
                return;
            }
            // 此时官方 StartLoot 已核实库存；库存 AddItem 会通知正打开的 LootView 刷新。
            if (!take.Reserved || !SkyIslandRewardCrate.AddOfficialStack(point.Box, take.Item, 1, new StringBuilder(), "TidegateCargo"))
            { LoseOffer(take); return; }
            take.Reserved = false;
            bool paired = offers.Count > 1;
            string name = L10n.T(take.Route.Zh, take.Route.En);
            for (int i = offers.Count - 1; i >= 0; i--) RetireOffer(offers[i], true);
            if (announce != null) announce(L10n.T("留下了", "Shipment secured: ") + name
                + (paired ? L10n.T("；另一批已装船。", ". The other shipment is aboard.") : ""), false);
        }

        private void RevealOffers(bool atSeal)
        {
            cargoHinted = true;
            var text = new StringBuilder(atSeal ? L10n.T("箱上贴着装船封签：", "Shipping seal: ") : L10n.T("箱盖下的装船货单：", "Shipping manifest: "));
            bool marked = true;
            foreach (Shipment offer in offers)
            {
                if (offer != offers[0]) text.Append(L10n.T(" / ", " / "));
                text.Append(L10n.T(offer.Route.Zh, offer.Route.En));
                try
                {
                    offer.Pin = new GameObject("TidegateCargoPin_" + offer.Target.Id);
                    offer.Pin.transform.SetParent(world.Root.transform, false);
                    offer.Pin.transform.position = offer.Target.Marker.position;
                    var pin = offer.Pin.AddComponent<SimplePointOfInterest>();
                    pin.Color = new Color(0.96f, 0.76f, 0.43f);
                    pin.IsArea = true;
                    pin.AreaRadius = 3f;
                    pin.Setup(null, offer.Route.Key);
                }
                catch (Exception e) { ClearCargoPin(offer); marked = false; Debug.LogWarning(TidegateRaidScene.LogTag + " 货单地图标记：" + e.Message); }
            }
            text.Append(offers.Count == 2 ? L10n.T("。先开哪箱就留哪批，另一批装船。", ". Open either seal; the other shipment sails.")
                : L10n.T("。只剩这一批。", ". One shipment remains."));
            if (marked) text.Append(L10n.T(" 按 M 看金圈。", " Press M for the gold circles."));
            if (announce != null) announce(text.ToString(), false);
        }

        private static void ClearCargoPin(Shipment offer)
        {
            if (offer.Pin == null) return;
            offer.Pin.SetActive(false);
            UnityEngine.Object.Destroy(offer.Pin);
            offer.Pin = null;
        }

        private void RetireOffer(Shipment offer, bool restore)
        {
            offers.Remove(offer);
            offer.Target.Offer = null;
            ClearCargoPin(offer);
            if (restore) RestoreOrdinary(offer);
        }

        private void LoseOffer(Shipment offer)
        {
            RetireOffer(offer, true);
            if (cargoHinted && announce != null) announce(offers.Count > 0
                ? L10n.T("这处没取到货；另一批还在。", "This shipment is gone; the other one is still waiting.")
                : L10n.T("货单上的货已经不在了。", "The shipment is already gone."), false);
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            for (int i = offers.Count - 1; i >= 0; i--) RetireOffer(offers[i], false);
            if (subscribed) { InteractableLootbox.OnStartLoot -= OnStartLoot; subscribed = false; }
            Opened = null;
            foreach (Point point in points)
            {
                if (point.Box == null) continue;
                // 官方 LootView 仍可能持有引用；先停用再销毁。
                point.Box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(point.Box.gameObject);
                point.Box = null;
            }
            points.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Duckov.MiniMaps;
using Duckov.UI;
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
        /// 涨潮时仅允许贴着原箱继续已经打开的官方搜刮界面，水退就马上放出箱子。
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
            internal float RetryAt;
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
            return IsUnderwaterContainer(pointId, kind) ? 4 : LegacyPrizeFloor(pointId, kind);
        }

        // 旧抽样仍推进本趟去重状态，保证陆地箱沿原有路线走出的内容不受水下调价影响。
        private static int LegacyPrizeFloor(string pointId, string kind)
        {
            if (HasFixed(pointId, "goldbar") || HasFixed(pointId, "tidewatch")) return 6;
            if (kind == "vault") return 5;
            if (kind == "buried" || kind == "wreck" || HasFixed(pointId, "wheel") || HasFixed(pointId, "page1")) return 4;
            return 0;
        }

        internal static bool IsUnderwaterContainer(string pointId, string kind)
        {
            return kind == "buried" || kind == "wreck" || kind == "vault" || HasFixed(pointId, "goldbar");
        }

        // 按玩家确认，预算只统计数值品质 >= 5；固定怀表豁免。显示光晕可能来自克隆模板或外观 Mod，
        // 不能混入数值品质判据，否则品质 4 的鸭蹼继承红光晕也会被误报为高级奖。
        internal static bool CountsAsUnderwaterJackpot(Item item)
        {
            return item != null && item.TypeID != BossRushItemIds.TidegateTideWatch
                && item.Quality >= 5;
        }

        private static int PrizeCeiling(string kind, int floor)
        {
            if (floor >= 5) return floor + 2;
            return kind == "wreck" || kind == "locker" ? 6 : 5;
        }

        private static int RollPrizeGrade(int floor, int ceiling, System.Random random)
        {
            int roll = random.Next(100);
            if (floor == 6) return roll < 72 ? 6 : roll < 96 ? 7 : 8;
            if (floor == 5) return roll < 60 ? 5 : roll < 92 ? 6 : 7;
            if (ceiling == 6) return roll < 48 ? 4 : roll < 87 ? 5 : 6;
            return roll < 65 ? 4 : 5;
        }

        /// <summary>本次进图的官方现货。先掷品质，再在该品质的合法实物中抽样，避免商品目录大的档次变相挤掉稀有档。</summary>
        internal sealed class PrizeCatalog
        {
            private readonly Dictionary<Cat, List<int>[]> stock = new Dictionary<Cat, List<int>[]>();
            private static readonly Cat[] Themes = { Cat.Valuable, Cat.Gear, Cat.Weapon, Cat.Medical, Cat.Workshop, Cat.General };
            private static readonly Cat[] WaterThemes = { Cat.General, Cat.Valuable, Cat.Gear, Cat.Weapon, Cat.Ammo, Cat.Workshop };
            private sealed class WaterStock
            {
                internal readonly List<int> Ids = new List<int>(), Cumulative = new List<int>();
                internal readonly List<int>[] Prizes = new List<int>[6];
            }
            private readonly Dictionary<Cat, WaterStock> waterStock = new Dictionary<Cat, WaterStock>();
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
                foreach (Cat category in WaterThemes)
                {
                    var prepare = PrepareWater(category);
                    while (prepare.MoveNext()) yield return prepare.Current;
                    yield return null;
                }
            }

            private System.Collections.IEnumerator PrepareWater(Cat category)
            {
                if (waterStock.ContainsKey(category)) yield break;
                var result = new WaterStock();
                int running = 0;
                int[] ids = SkyIslandLootPools.GetCategoryBand(category, 2, 4);
                if (ids != null)
                    for (int i = 0; i < ids.Length; i++)
                    {
                        try
                        {
                            int id = ids[i];
                            Item item = SkyIslandEnemyArmoryRules.IsOwnModItem(id) ? null : ItemAssetsCollection.GetPrefab(id);
                            if (item != null && item.TypeID == id && item.Quality >= 2 && item.Quality <= 4
                                && !CountsAsUnderwaterJackpot(item) && SkyIslandLootTables.AllowedInPool(item.Value))
                            {
                                result.Ids.Add(id);
                                running += SkyIslandLootTables.QualityWeight(item.Quality, 2);
                                result.Cumulative.Add(running);
                                if (MeetsPrizeTerms(item))
                                    (result.Prizes[item.Quality] ?? (result.Prizes[item.Quality] = new List<int>())).Add(id);
                            }
                        }
                        catch (Exception) { /* 缺少实物的条目不进入水下保底池。 */ }
                        if ((i + 1) % BatchSize == 0) yield return null;
                    }
                waterStock[category] = result;
            }

            private WaterStock Water(Cat category)
            {
                WaterStock result;
                if (!waterStock.TryGetValue(category, out result))
                {
                    var prepare = PrepareWater(category);
                    while (prepare.MoveNext()) { }
                    result = waterStock[category];
                }
                return result;
            }

            internal int PickWater(Cat category, System.Random random, int prizeGrade = 0)
            {
                WaterStock primary = Water(category), fallback = Water(Cat.General);
                if (prizeGrade > 0)
                {
                    List<int> band = primary.Prizes[4] ?? fallback.Prizes[4];
                    return band != null && band.Count > 0 ? band[random.Next(band.Count)] : 0;
                }
                WaterStock pool = primary.Ids.Count > 0 ? primary : fallback;
                if (pool.Ids.Count == 0) return 0;
                int roll = random.Next(pool.Cumulative[pool.Cumulative.Count - 1]);
                int index = pool.Cumulative.BinarySearch(roll + 1);
                return pool.Ids[index < 0 ? ~index : index];
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

        // 陆地奖励沿用旧的跨箱去重状态；水下独立计划也按同样重试上限避免精品重样。
        private readonly HashSet<int> prizesThisRaid = new HashSet<int>();
        private const int DistinctPrizeAttempts = 6;

        private int RolledPrize(Point point, int floor, System.Random random, out Cat category)
        {
            int prize = 0;
            category = Cat.General;
            for (int attempt = 0; attempt < DistinctPrizeAttempts; attempt++)
            {
                prize = RollPrizeOnce(point, floor, random, out category);
                if (prize <= 0 || prizesThisRaid.Add(prize)) return prize;
            }
            return prize;
        }

        private int DistinctPrize(Cat category, int preferred, int floor, int ceiling, System.Random random, Cat fallback)
        {
            int prize = 0;
            for (int attempt = 0; attempt < DistinctPrizeAttempts; attempt++)
            {
                prize = PickPrize(category, preferred, floor, ceiling, random, fallback);
                if (prize <= 0 || prizesThisRaid.Add(prize)) return prize;
            }
            return prize;
        }

        /// <summary>
        /// 已经带着响铊的人再打倒沙老大：尸体里换成一件挖沙船私货（品质 5–6 的护具、武器或值钱物），和本趟其它精品不重样。
        /// 返回 0 表示目录里没有合格的货，调用方照旧给响铊。
        /// </summary>
        internal int RollBossSpare()
        {
            var random = SkyIslandLootTables.CreateStream(seed, "Tidegate#leadline-spare");
            int roll = random.Next(100);
            Cat category = roll < 40 ? Cat.Gear : roll < 70 ? Cat.Weapon : Cat.Valuable;
            return DistinctPrize(category, random.Next(100) < 70 ? 5 : 6, 5, 6, random, Cat.General);
        }

        private int RollPrizeOnce(Point point, int floor, System.Random random, out Cat category)
        {
            int ceiling = PrizeCeiling(point.Kind, floor);
            int grade = RollPrizeGrade(floor, ceiling, random);
            category = PrizeCategory(point, grade, random.Next(100));
            return PickPrize(category, grade, floor, ceiling, random, Cat.General);
        }

        private static Cat PrizeCategory(Point point, int grade, int theme)
        {
            if (HasFixed(point.Id, "goldbar"))
                // 船长私藏：随身武器、护具、船上专用仪器，有时才是真正的宝石。
                return theme < 38 ? Cat.Weapon : theme < 68 ? Cat.Gear : theme < 83 ? Cat.Workshop : theme < 93 ? Cat.Valuable : Cat.General;
            else if (point.Kind == "vault" && grade >= 6)
                // 储蓄所收的抵押品不全是珠宝。官方 6 档只有一种珠宝，偶尔见到比每次见到更有意义。
                return theme < 41 ? Cat.Gear : theme < 65 ? Cat.Weapon : theme < 78 ? Cat.Valuable : Cat.General;
            else if (point.Kind == "wreck" || point.Kind == "locker")
                return theme < 40 ? Cat.Weapon : Cat.Gear;
            else if (HasFixed(point.Id, "page1"))
                return theme < 75 ? Cat.Workshop : Cat.Gear;
            else
                return Cat.Valuable;
        }

        internal const int UnderwaterJackpotChancePercent = 80;
        private string underwaterJackpotPoint;
        private int underwaterJackpotItem;
        private bool underwaterJackpotRolled;
        private readonly Dictionary<string, int> underwaterPrizes = new Dictionary<string, int>(StringComparer.Ordinal);

        // 一次进图只抽一次，再稳定选一只原有箱子的一格。与建箱先后、开箱次数、货单 POI 是否存在无关。
        private void PrepareUnderwaterPrizes()
        {
            var sites = points.FindAll(p => IsUnderwaterContainer(p.Id, p.Kind));
            sites.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            if (sites.Count == 0) return;
            var roll = SkyIslandLootTables.CreateStream(seed, "Tidegate#underwater-jackpot");
            underwaterJackpotRolled = roll.Next(100) < UnderwaterJackpotChancePercent;
            if (underwaterJackpotRolled)
            {
                underwaterJackpotPoint = sites[roll.Next(sites.Count)].Id;
                underwaterJackpotItem = PickPrize(Cat.General, roll.Next(100) < 80 ? 5 : 6, 5, 6, roll);
                if (underwaterJackpotItem == 0)
                    Debug.LogWarning(TidegateRaidScene.LogTag + " UNDERWATER_JACKPOT_STOCK_UNAVAILABLE seed=" + seed + " point=" + underwaterJackpotPoint);
            }
            var seen = new HashSet<int>();
            foreach (Point point in sites)
            {
                var random = SkyIslandLootTables.CreateStream(seed, point.Id + "#water-prize");
                int item = 0;
                for (int attempt = 0; attempt < DistinctPrizeAttempts; attempt++)
                {
                    int grade = 4;
                    item = prizes.PickWater(PrizeCategory(point, grade, random.Next(100)), random, grade);
                    if (item == 0 || seen.Add(item)) break;
                }
                underwaterPrizes[point.Id] = point.Id == underwaterJackpotPoint && underwaterJackpotItem > 0 ? underwaterJackpotItem : item;
            }
            Debug.Log(TidegateRaidScene.LogTag + " UNDERWATER_JACKPOT_PLAN seed=" + seed + " rolled=" + underwaterJackpotRolled
                + " point=" + underwaterJackpotPoint + " item=" + underwaterJackpotItem + " sites=" + sites.Count);
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
        internal const string ForemanPinKey = "BossRush_TidegateCargo_ForemanCabinet";
        private string shiftKeyBox, luckyBox;
        private bool carryingShiftKey, openedCabinet, lockExplained;
        private GameObject keyPin;

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

        /// <summary>钥匙不进背包（它只属于这一趟），所以拿到以后 HUD 目标行和 M 图一直指着领班柜，直到打开。</summary>
        internal string ShiftKeyObjective
        {
            get
            {
                return carryingShiftKey && !openedCabinet
                    ? L10n.T("带着轮班钥匙去码头二号仓库，打开领班柜", "Take the shift key to dock warehouse 2 and open the foreman's cabinet")
                    : null;
            }
        }

        private void TakeShiftKey()
        {
            carryingShiftKey = true;
            Point cabinet = points.Find(p => p.Id == ForemanCabinet);
            PinCabinet(cabinet);
            if (cabinet != null && cabinet.Box != null && !IsFlooded(cabinet)) cabinet.Box.gameObject.SetActive(true);
            Say(L10n.T("箱盖里挂着轮班钥匙：已取下。本趟可开码头二号仓库的领班柜，按 M 看位置。",
                "A shift key hangs under the lid. Taken: it opens the foreman's cabinet in dock warehouse 2 this raid. Press M to find it."), true);
        }

        private void PinCabinet(Point cabinet)
        {
            ClearKeyPin();
            if (cabinet == null || cabinet.Marker == null) return;
            try
            {
                keyPin = new GameObject("TidegateForemanPin");
                keyPin.transform.SetParent(world.Root.transform, false);
                keyPin.transform.position = cabinet.Marker.position;
                var pin = keyPin.AddComponent<SimplePointOfInterest>();
                pin.Color = new Color(0.96f, 0.76f, 0.43f);
                pin.IsArea = true;
                pin.AreaRadius = 2f;
                pin.Setup(null, ForemanPinKey);
            }
            catch (Exception e) { ClearKeyPin(); Debug.LogWarning(TidegateRaidScene.LogTag + " 领班柜地图标记：" + e.Message); }
        }

        private void ClearKeyPin()
        {
            if (keyPin == null) return;
            keyPin.SetActive(false);
            UnityEngine.Object.Destroy(keyPin);
            keyPin = null;
        }

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
            LocalizationHelper.InjectLocalization(ForemanPinKey, L10n.T("领班柜（轮班钥匙）", "Foreman's cabinet (shift key)"));
        }

        private readonly List<Point> points = new List<Point>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly int seed;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> announce;
        private float nextTick;
        private GameObject staging;
        private Point pendingLootClose;
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
            PrepareUnderwaterPrizes();
            ChooseDelivery();
            PrepareShift();
            AssignKits();
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
            // 两条路线的抽选与陆地货物沿用旧流；选定以后才用独立流调整水下货。
            // 否则避让中奖点会少掷一次 reservoir 随机数，连陆地路线与奖品也一起变了。
            var waterRandom = SkyIslandLootTables.CreateStream(seed, "Tidegate#manifest-water");
            for (int i = offers.Count - 1; i >= 0; i--)
            {
                Shipment offer = offers[i];
                if (!IsUnderwaterContainer(offer.Target.Id, offer.Target.Kind)) continue;
                if (offer.Target.Id == underwaterJackpotPoint)
                {
                    Transform place = world.Pois.Find(p => p.name == offer.Route.Place);
                    var alternatives = points.FindAll(p => p.Kind == offer.Route.Kind && !IsFixedContainer(p.Id)
                        && p.Id != underwaterJackpotPoint && place != null
                        && (p.Marker.position - place.position).sqrMagnitude < 30f * 30f);
                    alternatives.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
                    if (alternatives.Count == 0) { RetireOffer(offer, false); continue; }
                    offer.Target.Offer = null;
                    offer.Target = alternatives[waterRandom.Next(alternatives.Count)];
                    offer.Target.Offer = offer;
                }
                offer.Item = prizes.PickWater(offer.Route.Category, waterRandom, 4);
                if (!CanShip(offer.Item)) RetireOffer(offer, false);
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

        // 容量对应作者 kit-props 的实际模型；略提高多格概率，保持容量上限与普通 / 危险区的品质差异。
        internal static int RollCount(string kind, int roll)
        {
            if (kind == "vault") return roll < 20 ? 3 : roll < 57 ? 4 : roll < 87 ? 5 : 6;
            if (kind == "buried" || kind == "safe" || kind == "wreck" || kind == "locker")
                return roll < 8 ? 1 : roll < 32 ? 2 : roll < 60 ? 3 : roll < 88 ? 4 : 5;
            if (kind == "fish") return roll < 10 ? 1 : roll < 62 ? 2 : 3;
            if (kind == "house") return roll < 8 ? 1 : roll < 54 ? 2 : roll < 90 ? 3 : 4;
            if (kind == "tool" || kind == "med") return roll < 28 ? 2 : roll < 70 ? 3 : roll < 92 ? 4 : 5;
            return roll < 3 ? 1 : roll < 40 ? 2 : roll < 80 ? 3 : roll < 97 ? 4 : 5;
        }

        // 工具柜、渔篓、药柜的头两格成对出：主件看用途（工具 / 鱼 / 药），副件是配套的耗材（零件 / 饵 / 针剂或饮料）。
        // 两格各自从本机官方同类别、同品质带（已过黑名单与价限）里按官方标签筛出的小池里加权不放回地抽：
        // 一趟里同类箱子的开头互不相同，池子抽完才轮第二遍；品质越低越常见。池子空了就退回原来的类别随机抽。
        // 2026-11-07 实机：写死的两套班次单让一趟 7 只渔篓开头全是同一种沙丁鱼、6/10 只工具柜都是螺栓加螺母。
        private sealed class KitSource
        {
            internal readonly Cat Category;
            internal readonly string[] Need, Avoid;
            /// <summary>这一格最高品质（不超过普通档的 3）。</summary>
            internal int MaxQuality = 3;
            internal KitSource(Cat category, string[] need, params string[] avoid) { Category = category; Need = need; Avoid = avoid; }
        }

        private struct KitItem
        {
            internal int Id, Weight;
            internal Cat Category;
        }

        private static readonly string[] KitKinds = { "tool", "fish", "med" };
        private readonly Dictionary<string, KitItem[]> kits = new Dictionary<string, KitItem[]>(StringComparer.Ordinal);

        private static KitSource[] KitRole(string kind, int slot)
        {
            switch (kind)
            {
                case "tool":
                    return slot == 0 ? new[] { new KitSource(Cat.Workshop, new[] { "Tool" }, "Material") }
                        : new[] { new KitSource(Cat.Workshop, new[] { "Material", "Electric" }) };
                case "fish":
                    // 渔篓里是码头的小杂鱼（1–2 档九种，够一趟七只篓不重样）；石斑、竹荚鱼这些 3 档大鱼留给钓鱼，免得普通篓变相涨价。
                    return slot == 0 ? new[] { new KitSource(Cat.Fishing, new[] { "Fish" }) { MaxQuality = 2 } }
                        : new[] { new KitSource(Cat.Fishing, new[] { "Bait" }) };
                case "med":
                    return slot == 0 ? new[] { new KitSource(Cat.Medical, new[] { "Medic" }, "Injector") }
                        : new[] { new KitSource(Cat.Medical, new[] { "Injector" }), new KitSource(Cat.Pantry, new[] { "Drink" }) };
            }
            return null;
        }

        private static bool HasAnyTag(Item item, string[] names)
        {
            if (names == null) return false;
            foreach (string name in names) if (item.Tags.Contains(name)) return true;
            return false;
        }

        private static List<KitItem> KitPool(KitSource[] sources)
        {
            var pool = new List<KitItem>();
            if (sources == null) return pool;
            int min = SkyIslandLootTables.MinQuality(Tier.Supply), max = SkyIslandLootTables.MaxQuality(Tier.Supply);
            foreach (KitSource source in sources)
            {
                int[] band = SkyIslandLootPools.GetCategoryBand(source.Category, min, max);
                if (band == null) continue;
                foreach (int id in band)
                {
                    if (SkyIslandEnemyArmoryRules.IsOwnModItem(id) || pool.Exists(k => k.Id == id)) continue;
                    Item item;
                    try { item = ItemAssetsCollection.GetPrefab(id); }
                    catch (Exception) { continue; }
                    if (item == null || item.TypeID != id || item.Quality > source.MaxQuality || item.Tags == null
                        || !HasAnyTag(item, source.Need) || HasAnyTag(item, source.Avoid)) continue;
                    pool.Add(new KitItem { Id = id, Category = source.Category, Weight = item.Quality <= 1 ? 4 : item.Quality == 2 ? 2 : 1 });
                }
            }
            // 抽取只由种子决定，不随官方目录的枚举顺序变化。
            pool.Sort((a, b) => a.Id.CompareTo(b.Id));
            return pool;
        }

        private static List<KitItem> WeightedOrder(List<KitItem> pool, System.Random random)
        {
            var left = new List<KitItem>(pool);
            var order = new List<KitItem>(pool.Count);
            while (left.Count > 0)
            {
                int total = 0;
                foreach (KitItem k in left) total += k.Weight;
                int roll = random.Next(total), pick = 0;
                while (roll >= left[pick].Weight) { roll -= left[pick].Weight; pick++; }
                order.Add(left[pick]);
                left.RemoveAt(pick);
            }
            return order;
        }

        // 进图时一次排好：同类箱按标记名排序后依次发牌，结果与玩家先走哪条街无关，日志里的种子能复现。
        private void AssignKits()
        {
            foreach (string kind in KitKinds)
            {
                List<Point> sites = points.FindAll(p => p.Kind == kind && !IsFixedContainer(p.Id));
                if (sites.Count == 0) continue;
                sites.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                List<KitItem> first = WeightedOrder(KitPool(KitRole(kind, 0)), SkyIslandLootTables.CreateStream(seed, "Tidegate#kit:" + kind + ":0"));
                List<KitItem> second = WeightedOrder(KitPool(KitRole(kind, 1)), SkyIslandLootTables.CreateStream(seed, "Tidegate#kit:" + kind + ":1"));
                int cursor = 0;
                for (int i = 0; i < sites.Count; i++)
                {
                    var pair = new KitItem[2];
                    if (first.Count > 0) pair[0] = first[i % first.Count];
                    // 主副件可能同属两个标签（例如带 Electric 的工具）：同一只箱子不放两件一样的。
                    for (int tries = 0; tries < second.Count; tries++)
                    {
                        KitItem candidate = second[cursor++ % second.Count];
                        if (candidate.Id == pair[0].Id) continue;
                        pair[1] = candidate;
                        break;
                    }
                    kits[sites[i].Id] = pair;
                }
            }
        }

        private int KitItemFor(string pointId, int slot, ref Cat category)
        {
            KitItem[] pair;
            if (slot > 1 || !kits.TryGetValue(pointId, out pair) || pair[slot].Id <= 0) return 0;
            category = pair[slot].Category;
            return pair[slot].Id;
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
            if (closed || player == null || valid == null || !valid()) return;
            Point pending = pendingLootClose;
            pendingLootClose = null;
            // 官方 StartLoot 返回后会覆盖箱子的状态，静态事件的后续订阅者还可能打开界面。
            // 交互调用栈结束后重置箱子，只关闭仍不合法的界面。
            if (pending != null && pending.Box != null)
            {
                if (IsFlooded(pending) || Locked(pending)) HideLootBox(pending.Box, true);
                else if (!IsCurrentLootView(pending.Box)) pending.Box.StopInteract();
            }
            if (Time.time < nextTick) return;
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
                    bool keepOpen = flooded && distance <= KeepNearRange * KeepNearRange && IsCurrentLootView(point.Box);
                    point.Flooded = flooded;
                    bool active = (!flooded || keepOpen) && !Locked(point);
                    if (point.Box.gameObject.activeSelf != active)
                    {
                        if (active) point.Box.gameObject.SetActive(true);
                        else HideLootBox(point.Box);
                    }
                    if (point.BuriedVisual != null && point.BuriedVisual.gameObject.activeSelf == flooded)
                        point.BuriedVisual.gameObject.SetActive(!flooded);
                    if (!flooded) point.FloodHinted = false;
                    // 被淹的箱柜：模型还在、交互没了，走到跟前提示一次，水退之前不再说（CR-2026-11-05-024）。
                    if (point.Flooded && !keepOpen && !point.FloodHinted && distance <= FloodHintRange * FloodHintRange && announce != null)
                    {
                        point.FloodHinted = true;
                        announce(L10n.T("这只箱子泡在深水里，搜不了，等退潮再来。", "This container is under deep water. Come back at low tide."), false);
                    }
                }
                if (point.Built || point.Failed || Time.time < point.RetryAt || distance >= best) continue;
                best = distance;
                nearest = point;
            }
            // 一次只建一个：走进一片新街区时不集中做实例化与物品创建。
            if (nearest != null) Build(nearest, player);
        }

        private void Build(Point point, CharacterMainControl player)
        {
            point.Built = true;
            if (staging == null)
            {
                staging = new GameObject("TidegateLootStaging");
                staging.SetActive(false);
                staging.transform.SetParent(world.Root.transform, false);
            }
            string error;
            InteractableLootbox box = SkyIslandRewardCrate.Build(staging.transform, point.Marker.position, point.Yaw,
                "TidegateLoot_" + point.Id, out error, true);
            if (box == null)
            {
                if (RequiresStock(point)) RetryBuild(point, error);
                else
                {
                    point.Failed = true;
                    if (point.Offer != null) LoseOffer(point.Offer);
                    Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮点 " + point.Id + " 准备失败：" + error);
                }
                return;
            }
            HashSet<int> priorPrizes = RequiresStock(point) ? new HashSet<int>(prizesThisRaid) : null;
            try
            {
                if (!closed && valid() && StockAndPublish(point, box, player)) return;
                error = "必要货物未进入库存或场景已关闭";
            }
            catch (Exception e) { error = e.Message; }
            if (box != null)
            {
                box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(box.gameObject);
            }
            point.Box = null;
            if (!closed)
            {
                if (priorPrizes != null) prizesThisRaid.IntersectWith(priorPrizes);
                if (point.Offer != null) point.Offer.Reserved = false;
                RetryBuild(point, error);
            }
        }

        private bool StockAndPublish(Point point, InteractableLootbox box, CharacterMainControl player)
        {
            // 场景包里每个搜刮点本来就摆着自己的箱柜模型（TG_containers）；官方尸体箱的模型藏起来，只留交互与库存。
            foreach (Renderer renderer in box.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Spec spec = SpecOf(point.Kind);
            var random = SkyIslandLootTables.CreateStream(seed, point.Id);
            int count = RollCount(point.Kind, random.Next(100));
            int prizeFloor = LegacyPrizeFloor(point.Id, point.Kind);
            var prizeRandom = prizeFloor > 0 ? SkyIslandLootTables.CreateStream(seed, point.Id + "#prize") : null;
            bool captain = HasFixed(point.Id, "goldbar");
            bool underwater = IsUnderwaterContainer(point.Id, point.Kind);
            var waterRandom = underwater ? SkyIslandLootTables.CreateStream(seed, point.Id + "#water-stock") : null;
            var log = new StringBuilder();
            int added = 0, foremanPrize = 0;
            for (int i = 0; i < count; i++)
            {
                Cat category = i == 0 ? spec.First : spec.Extras[random.Next(spec.Extras.Length)];
                int typeId = KitItemFor(point.Id, i, ref category);
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
                    int surprise = DistinctPrize(theme, 4, 4, 4, random, theme);
                    if (surprise > 0) { typeId = surprise; amount = 1; category = theme; }
                    if (point.Id == ForemanCabinet) foremanPrize = surprise;
                }
                if (underwater)
                {
                    // 保留上面的旧随机/去重推进以兼容陆地奖励；水下实际入箱一律走独立、限额后的计划。
                    typeId = i == 0 && !captain ? underwaterPrizes[point.Id] : prizes.PickWater(category, waterRandom);
                    amount = typeId > 0 && category == Cat.Ammo ? 20 + waterRandom.Next(31) : 1;
                    if (i == 0 && !captain) amount = 1;
                    if (typeId == 0) log.Append(" water_stock_unavailable");
                }
                if (DeferCargoSlot(point, i, count, typeId, amount, category)) continue;
                if (SkyIslandRewardCrate.AddOfficialStack(box, typeId, amount, log, category.ToString())) added++;
                if (closed || box == null) return false;
            }
            bool carryingWatch = HasFixed(point.Id, "tidewatch") && TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch);
            int fixedItem = FixedItemOf(point.Id, point.Kind, SkyIslandLootTables.CreateStream(seed, point.Id + "#story"), carryingWatch);
            if (fixedItem != 0 && SkyIslandRewardCrate.AddOfficialStack(box, fixedItem, 1, log, "Tidegate")) added++;
            if (closed || box == null) return false;
            if (fixedItem != 0 && (HasFixed(point.Id, "wheel") || HasFixed(point.Id, "tidewatch")) && !ContainsStock(box, fixedItem)) return false;
            // 沉船船长的保险柜原有额外一格继续保留，但也服从整趟水下高级奖励预算。
            if (HasFixed(point.Id, "goldbar"))
            {
                Cat prizeCategory;
                int valuable = RolledPrize(point, prizeFloor, prizeRandom, out prizeCategory);
                if (underwater) valuable = underwaterPrizes[point.Id];
                if (valuable != 0 && SkyIslandRewardCrate.AddOfficialStack(box, valuable, 1, log, "TidegateWreck")) added++;
                else log.Append(" captain_prize_unavailable");
            }
            if (closed || box == null || !valid()) return false;
            if (underwater && underwaterPrizes.TryGetValue(point.Id, out int plannedPrize) && plannedPrize > 0)
            {
                bool jackpot = point.Id == underwaterJackpotPoint && underwaterJackpotItem > 0;
                bool reservedForCargo = !jackpot && point.Offer != null && point.Offer.Reserved && point.Offer.OrdinaryItem == plannedPrize;
                if (!reservedForCargo && !ContainsStock(box, plannedPrize, jackpot ? 5 : 4, jackpot ? 6 : 4)) return false;
            }
            if (point.Id == ForemanCabinet && shiftKeyBox != null
                && (added == 0 || foremanPrize > 0 && !ContainsStock(box, foremanPrize, 4, 4))) return false;
            Vector3 position = point.Marker.position;
            if (!world.TryGroundAt(position, out point.Ground)) point.Ground = position.y;
            point.Lagoon = world.InLagoon(position);
            point.Flooded = IsFlooded(point);
            if (point.Kind == "buried")
            {
                if (point.BuriedVisual != null) point.BuriedVisual.gameObject.SetActive(!point.Flooded);
            }
            bool active = !point.Flooded && !Locked(point);
            box.gameObject.SetActive(active);
            box.transform.SetParent(world.Root.transform, true);
            if (closed || box == null || !valid() || active && !box.gameObject.activeInHierarchy) return false;
            point.Box = box;
            Debug.Log(TidegateRaidScene.LogTag + " LOOT_READY id=" + point.Id + " tier=" + spec.Tier + " items=" + added
                + " flooded=" + point.Flooded + " " + log);
            if (active && !cargoHinted && point.Offer != null && (position - player.transform.position).sqrMagnitude <= FloodHintRange * FloodHintRange)
                RevealOffers(true);
            return true;
        }

        private void OnStartLoot(InteractableLootbox box)
        {
            if (closed || box == null) return;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.Box != box) continue;
                try
                {
                    if (!valid() || IsFlooded(point) || Locked(point))
                    {
                        pendingLootClose = point;
                        HideLootBox(box);
                        return;
                    }
                    if (point.Opened) return;
                    point.Opened = true;
                    opened++;
                    if (point.Id == shiftKeyBox && !carryingShiftKey && !openedCabinet) TakeShiftKey();
                    else if (point.Id == ForemanCabinet && carryingShiftKey)
                    {
                        openedCabinet = true;
                        carryingShiftKey = false;
                        ClearKeyPin();
                        Say(L10n.T("轮班钥匙留在锁孔里。领班柜已打开。", "The shift key stays in the lock. The foreman's cabinet is open."), false);
                    }
                    if (closed) return;
                    ReadDelivery(point);
                    if (!closed) Opened?.Invoke(point.Id);
                }
                catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮回调 " + point.Id + "：" + e.Message); }
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
            if (closed || point.Box == null) return;
            take.Reserved = false;
            bool paired = offers.Count > 1;
            string name = L10n.T(take.Route.Zh, take.Route.En);
            for (int i = offers.Count - 1; i >= 0; i--) RetireOffer(offers[i], true);
            Say(L10n.T("留下了", "Shipment secured: ") + name
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
            Say(text.ToString(), false);
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
            if (cargoHinted) Say(offers.Count > 0
                ? L10n.T("这处没取到货；另一批还在。", "This shipment is gone; the other one is still waiting.")
                : L10n.T("货单上的货已经不在了。", "The shipment is already gone."), false);
        }

        private void Say(string message, bool important)
        {
            try { announce?.Invoke(message, important); }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮提示：" + e.Message); }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            ClearKeyPin();
            for (int i = offers.Count - 1; i >= 0; i--) RetireOffer(offers[i], false);
            if (subscribed) { InteractableLootbox.OnStartLoot -= OnStartLoot; subscribed = false; }
            Opened = null;
            foreach (Point point in points)
            {
                if (point.Box == null) continue;
                // 官方 LootView 仍可能持有引用；退出时先关当前箱的界面，再销毁库存。
                try { HideLootBox(point.Box); }
                catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 搜刮界面收起失败：" + e.Message); }
                if (point.Box != null) UnityEngine.Object.Destroy(point.Box.gameObject);
                point.Box = null;
            }
            points.Clear();
            pendingLootClose = null;
            underwaterPrizes.Clear();
            if (staging != null) UnityEngine.Object.Destroy(staging);
            staging = null;
        }
    }
}

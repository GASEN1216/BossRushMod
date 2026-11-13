using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BossRush.Utils;
using ItemStatsSystem;
using UnityEngine;
using Cat = BossRush.SkyIslandLootCategory;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的搜刮 owner（口径照潮闸镇换算，数据源 `docs/design/last-shift-yard/proto/layout.mjs` 的 `goods.lootPlan`，
    /// 规则照 `proto/sim.mjs` 的 planLoot / fillContainer / prize / openDrop / 封签货签收 / 铅封侧柜）。
    /// 箱子是官方 `InteractableLootbox`（`SkyIslandRewardCrate.Build`，交互名「搜集」），走近 60 m 才建、0.5 秒最多建一个；
    /// 场景包里每只箱子本来就有模型，官方尸体箱的模型藏起来，只留交互与库存。
    /// 普通箱与侧柜的内容开局按独立随机流（<see cref="LastShiftTimetable.Stream"/>）一次排好，与开箱先后无关：
    /// 品质带内每高一档权重 ×0.6；医药箱 / 工具箱头两格成套；风险柜头格 Q4 精品；整趟 80% 一件红（Q5 80% / Q6 20%，按 Quality 计）
    /// 落在一只风险柜；12% 幸运箱；精品同趟不重样。封签货、零担箱、铅封侧柜三种「有条件才开」的箱子先停用，
    /// 条件满足前同一位置放一个机关交互，说明为什么还开不了。
    /// </summary>
    internal sealed partial class LastShiftLoot : IDisposable
    {
        private const float ActivationRange = 60f;
        private const float TickInterval = 0.5f;
        /// <summary>零担新批到站时玩家贴着箱子（搜刮界面可能开着）就不清上一批的剩货，只往里追加。</summary>
        private const float KeepNearRange = 4f;
        private const int AmmoMin = 20, AmmoMax = 50, DistinctAttempts = 6, KitAttempts = 4;
        private const int RedChance = 80, LuckyChance = 12, DropVoyageChance = 25, PrizeGrade = 4;

        // ---------------- 原型数据（layout.mjs goods.lootPlan） ----------------

        private sealed class Spec
        {
            internal readonly int Lo, Hi, Prize;
            internal readonly Cat First;
            internal readonly Cat[] Extras, Kit;
            internal readonly int[][] Counts;
            internal Spec(int lo, int hi, int prize, Cat first, Cat[] extras, Cat[] kit, int[][] counts)
            { Lo = lo; Hi = hi; Prize = prize; First = first; Extras = extras; Kit = kit; Counts = counts; }
        }

        private static int[][] Table(params int[] pairs)
        {
            var rows = new int[pairs.Length / 2][];
            for (int i = 0; i < rows.Length; i++) rows[i] = new[] { pairs[i * 2], pairs[i * 2 + 1] };
            return rows;
        }

        // 品质带：普通（生活物资）Q1–3，风险柜普通格 Q2–4，航务 Q2–5。
        private static readonly Spec MedicalSpec = new Spec(1, 3, 0, Cat.Medical, new[] { Cat.Medical, Cat.General },
            new[] { Cat.Medical, Cat.Medical }, Table(2, 28, 3, 42, 4, 30));
        private static readonly Spec ToolSpec = new Spec(1, 3, 0, Cat.Workshop, new[] { Cat.Workshop, Cat.Workshop, Cat.General },
            new[] { Cat.Workshop, Cat.Workshop }, Table(2, 28, 3, 42, 4, 22, 5, 8));
        private static readonly Spec SupplySpec = new Spec(1, 3, 0, Cat.General, new[] { Cat.Workshop, Cat.General, Cat.Ammo, Cat.Pantry },
            null, Table(1, 3, 2, 37, 3, 40, 4, 17, 5, 3));
        private static readonly Spec RiskSpec = new Spec(2, 4, PrizeGrade, Cat.Gear, new[] { Cat.Gear, Cat.General, Cat.Weapon, Cat.Ammo },
            null, Table(1, 8, 2, 24, 3, 28, 4, 28, 5, 12));
        private static readonly Cat[] DropExtras = { Cat.Workshop, Cat.General, Cat.Ammo, Cat.Pantry, Cat.Medical };
        private static readonly int[][] DropCounts = Table(2, 30, 3, 40, 4, 22, 5, 8);
        private static readonly Spec DropSpec = new Spec(1, 3, 0, Cat.General, DropExtras, null, DropCounts);
        private static readonly Spec DropVoyageSpec = new Spec(2, 5, 0, Cat.General, DropExtras, null, DropCounts);
        private static readonly Spec SideSpec = new Spec(2, 5, PrizeGrade, Cat.Valuable, new[] { Cat.Valuable, Cat.General, Cat.General, Cat.Gear },
            null, Table(1, 8, 2, 24, 3, 28, 4, 28, 5, 12));

        private static readonly int[][] KitWeights = Table(1, 4, 2, 2, 3, 1);
        private static readonly int[][] RedGrades = Table(5, 80, 6, 20);
        private static readonly int[][] SealedGrades = Table(4, 75, 5, 25);
        private static readonly int[][] SealedPremiumGrades = Table(4, 50, 5, 50);
        /// <summary>封签货一柜的件数（普通台 / 加价台）与补货品质带，见 <see cref="PlanCargo"/>。箱子 12 格。</summary>
        internal const int SealedCount = 8, SealedPremiumCount = 9;
        /// <summary>
        /// 续签（2026-11-13「每班可签」）：同一趟第 2 柜起每柜 4 件（加价台 5 件），只保底头格一件精品；
        /// 头格出 Q5 的概率随本趟已签柜数上涨（第 2 柜 40%，每柜 +10%，封顶 70%；加价台再 +20%，封顶 90%）。
        /// 签得越多押运来得越多（刷怪 owner 读 <see cref="Signatures"/>），所以奖励也越往后越好。
        /// </summary>
        internal const int ResignCount = 4, ResignPremiumCount = 5;
        internal const int ResignQ5Base = 30, ResignQ5Step = 10, ResignQ5Cap = 70, ResignPremiumBonus = 20, ResignPremiumCap = 90;
        private const int SealedFillLo = 2, SealedFillHi = 4;
        private static readonly Cat[] MedicalFill = { Cat.Medical, Cat.Medical, Cat.Pantry, Cat.General };
        private static readonly Cat[] RepairFill = { Cat.Workshop, Cat.Workshop, Cat.Ammo, Cat.General };
        private static readonly Cat[] PrecisionFill = { Cat.Gear, Cat.Workshop, Cat.Ammo, Cat.General };

        /// <summary>
        /// 箱子 id → 种类与格数（布局 `loot` 的 type / capacity）。场景包的标记只带名字，种类在这里登记；
        /// 布局追加了新箱子而这里没登记时按名字猜，猜不出按补给柜。
        /// </summary>
        private static readonly string[,] Registry =
        {
            { "cargoW", "sealed", "12" }, { "cargoE", "sealed", "12" },
            { "signalMed", "medical", "4" }, { "repairMed", "medical", "4" }, { "eastCab", "medical", "4" }, { "bellMed", "medical", "4" },
            { "southTools", "tool", "6" }, { "repairTools", "tool", "6" }, { "westTools", "tool", "6" }, { "eastTools", "tool", "6" }, { "workTools", "tool", "6" },
            { "southCab", "supply", "8" }, { "westCab", "supply", "8" }, { "northRoad", "supply", "8" }, { "eastLocker", "supply", "8" },
            { "westRisk", "risk", "10" }, { "bondCab", "risk", "10" }, { "eastRisk", "risk", "10" },
            { "dropW", "drop", "8" }, { "dropE", "drop", "8" },
        };
        private const string SideId = "stamp";
        private const int SideCapacity = 6;

        private static void Classify(string id, out string kind, out int capacity)
        {
            for (int i = 0; i < Registry.GetLength(0); i++)
                if (Registry[i, 0] == id) { kind = Registry[i, 1]; capacity = int.Parse(Registry[i, 2]); return; }
            if (id.EndsWith("Med", StringComparison.Ordinal)) { kind = "medical"; capacity = 4; }
            else if (id.EndsWith("Tools", StringComparison.Ordinal)) { kind = "tool"; capacity = 6; }
            else if (id.EndsWith("Risk", StringComparison.Ordinal)) { kind = "risk"; capacity = 10; }
            else { kind = "supply"; capacity = 8; }
        }

        private static Spec SpecOf(string kind)
        {
            switch (kind)
            {
                case "medical": return MedicalSpec;
                case "tool": return ToolSpec;
                case "risk": return RiskSpec;
                case "side": return SideSpec;
                default: return SupplySpec;
            }
        }

        /// <summary>精品格的类别分配（原型 prizeTheme）：检修库 / 东线风箱武器 40% 护具 60%，库内货柜值钱物，侧柜工具 / 值钱物各半。</summary>
        private static int[][] PrizeTheme(string id, string kind)
        {
            if (id == "bondCab") return Table((int)Cat.Valuable, 100);
            if (kind == "side") return Table((int)Cat.Workshop, 50, (int)Cat.Valuable, 50);
            return Table((int)Cat.Weapon, 40, (int)Cat.Gear, 60);
        }

        /// <summary>班单的封签货类别：医务 = 医疗，抢修 = 工具，精械 = 护具 / 仪器（仪器归官方工具类）。</summary>
        private static int[][] SealedTheme(string manifest)
        {
            if (manifest == "medical") return Table((int)Cat.Medical, 100);
            if (manifest == "repair") return Table((int)Cat.Workshop, 100);
            return Table((int)Cat.Gear, 60, (int)Cat.Workshop, 40);
        }

        private static int PickTable(System.Random r, int[][] table)
        {
            int total = 0;
            for (int i = 0; i < table.Length; i++) total += table[i][1];
            int x = r.Next(total);
            for (int i = 0; i < table.Length; i++) { if (x < table[i][1]) return table[i][0]; x -= table[i][1]; }
            return table[table.Length - 1][0];
        }

        /// <summary>品质带内按档抽：每高一档权重 ×0.6（与天空岛 / 潮闸镇同一个整数权重函数）。</summary>
        private static int PickGrade(System.Random r, int lo, int hi)
        {
            int total = 0;
            for (int q = lo; q <= hi; q++) total += SkyIslandLootTables.QualityWeight(q, lo);
            int x = r.Next(total);
            for (int q = lo; q <= hi; q++) { int w = SkyIslandLootTables.QualityWeight(q, lo); if (x < w) return q; x -= w; }
            return hi;
        }

        // ---------------- 官方现货：按类别 × 品质分档 ----------------

        /// <summary>
        /// 普通格的官方现货：先掷品质再在该品质的合法实物里均匀抽（与精品 <see cref="TidegateLoot.PrizeCatalog"/> 同一思路），
        /// 池子来自 <see cref="SkyIslandLootPools.GetCategoryBand"/>（黑名单、排除标签、价值上限 10 万、进程缓存都在那边），
        /// 这里只按 prefab 的 `Quality`（不看显示品质）分档，另外去掉跨图钥匙与门票。
        /// </summary>
        private sealed class GradeStock
        {
            internal const int MinGrade = 1, MaxGrade = 5;
            internal static readonly Cat[] Categories = { Cat.General, Cat.Medical, Cat.Workshop, Cat.Ammo, Cat.Pantry, Cat.Gear, Cat.Weapon, Cat.Valuable };
            private readonly Dictionary<Cat, List<int>[]> stock = new Dictionary<Cat, List<int>[]>();

            private List<int>[] Get(Cat category)
            {
                List<int>[] bands;
                if (stock.TryGetValue(category, out bands)) return bands;
                bands = new List<int>[MaxGrade + 1];
                int[] ids = SkyIslandLootPools.GetCategoryBand(category, MinGrade, MaxGrade);
                if (ids == null || ids.Length == 0) return bands; // 标签表还没就绪时不缓存空池，下次再查
                for (int i = 0; i < ids.Length; i++)
                {
                    try
                    {
                        Item prefab = ItemAssetsCollection.GetPrefab(ids[i]);
                        if (prefab == null || prefab.TypeID != ids[i] || prefab.Quality < MinGrade || prefab.Quality > MaxGrade) continue;
                        if (prefab.Tags != null && (prefab.Tags.Contains("Key") || prefab.Tags.Contains("SpecialKey"))) continue;
                        (bands[prefab.Quality] ?? (bands[prefab.Quality] = new List<int>())).Add(ids[i]);
                    }
                    catch (Exception) { /* 失效的预制件不是可交付的货 */ }
                }
                stock[category] = bands;
                return bands;
            }

            /// <summary>本类本档优先；缺货时在带内找最近的档，再退通用池。<paramref name="avoid"/> 里的货重抽几次（成套头两格不重样）。</summary>
            internal int Pick(Cat category, int grade, int lo, int hi, System.Random r, HashSet<int> avoid)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1 && category == Cat.General) break;
                    List<int>[] bands = Get(pass == 0 ? category : Cat.General);
                    for (int offset = 0; offset <= hi - lo; offset++)
                        for (int side = 0; side < (offset == 0 ? 1 : 2); side++)
                        {
                            int g = grade + (side == 0 ? -offset : offset);
                            if (g < lo || g > hi || g < MinGrade || g > MaxGrade) continue;
                            List<int> band = bands[g];
                            if (band == null || band.Count == 0) continue;
                            int id = band[r.Next(band.Count)];
                            for (int t = 0; avoid != null && t < KitAttempts && avoid.Contains(id); t++) id = band[r.Next(band.Count)];
                            return id;
                        }
                }
                return 0;
            }
        }

        /// <summary>读条画面下把本图用到的官方物品池预热好：每个池子占一帧，已缓存跳过（口径同 <see cref="TidegateLoot.PrewarmThemes"/>）。</summary>
        internal static IEnumerator PrewarmThemes()
        {
            foreach (Cat category in GradeStock.Categories)
            {
                if (SkyIslandLootPools.IsCategoryCached(category, GradeStock.MinGrade, GradeStock.MaxGrade)) continue;
                SkyIslandLootPools.GetCategoryBand(category, GradeStock.MinGrade, GradeStock.MaxGrade);
                yield return null;
            }
            // 精品现货（TidegateLoot.PrizeCatalog）读 4–8 档。
            foreach (Cat category in new[] { Cat.Valuable, Cat.Gear, Cat.Weapon, Cat.Medical, Cat.Workshop, Cat.General })
            {
                if (SkyIslandLootPools.IsCategoryCached(category, 4, 8)) continue;
                SkyIslandLootPools.GetCategoryBand(category, 4, 8);
                yield return null;
            }
        }

        // ---------------- 运行时状态 ----------------

        private struct Slot
        {
            internal int TypeId, Amount;
            internal Cat Category;
        }

        private sealed class Box
        {
            internal string Id, Kind;
            internal int Capacity;
            internal Transform Anchor;
            internal float Yaw;
            internal InteractableLootbox Loot;
            internal bool Built, Failed, Opened, Unlocked;
            /// <summary>封签货、零担箱、铅封侧柜：条件满足前箱子停用，同位置的机关交互说明原因。</summary>
            internal bool Gated;
            internal GameObject Device;
            internal List<Slot> Planned;
            // 零担：这一批的班次、待装的货、这一批是否押运零担 / 是否已翻过。
            internal int BatchTrip;
            internal List<Slot> PendingBatch;
            internal bool BatchVoyage, BatchOpened;
            /// <summary>签收柜：新一班停到这一侧时，上一班签出来的柜子等官方界面关了再收回、换成新的签收机关。</summary>
            internal bool RearmPending;
        }

        private readonly LastShiftWorld world;
        private readonly LastShiftTimetable timetable;
        private readonly string manifest;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> status;
        private readonly TidegateLoot.PrizeCatalog prizes = new TidegateLoot.PrizeCatalog();
        private readonly GradeStock grades = new GradeStock();
        private readonly List<Box> boxes = new List<Box>();
        private readonly List<string> riskIds = new List<string>();
        private readonly HashSet<int> prizesUsed = new HashSet<int>();
        private readonly Dictionary<string, HashSet<int>> kitsUsed = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private string redCrate, luckyCrate;
        private int redGrade;
        private readonly List<Item> stampsBroughtIn;
        private float nextTick;
        private GameObject cabinetStaging;
        private bool cargoPreparing;
        private bool stampPreparing;
        private bool closed, lootSubscribed, phaseSubscribed;

        /// <summary>本趟至少签过一柜封签货（站务记录、剧情用）。</summary>
        internal bool CargoClaimed { get { return Signatures > 0; } }
        /// <summary>本趟一共签了几柜（每班停靠最多一柜）：也是押运加派的警戒度。</summary>
        internal int Signatures { get; private set; }
        /// <summary>最近一次签收所在的班次（-1 表示还没签过）。</summary>
        internal int SignedTrip { get; private set; } = -1;
        /// <summary>当前这一班已经签过了。</summary>
        internal bool SignedThisTrain { get { return SignedTrip == timetable.Trip; } }
        /// <summary>官方界面（搜刮、背包）开着：签收柜换班时等它关上再收回（会话接 `View.ActiveView`）。</summary>
        internal Func<bool> UiBusy { get; set; }
        /// <summary>某只箱柜第一次被打开（参数是箱子 id；铅封侧柜是 `stamp`）。</summary>
        internal event Action<string> Opened;
        /// <summary>风险柜（布局 pool == 'risk'）：打开会拉响库房警铃。</summary>
        internal IEnumerable<string> RiskContainerIds { get { return riskIds; } }

        internal LastShiftLoot(LastShiftWorld world, LastShiftTimetable timetable, string manifest, int seed, CharacterMainControl player, Func<bool> valid, Action<string, bool> status)
        {
            this.world = world;
            this.timetable = timetable;
            this.manifest = manifest ?? "medical";
            this.valid = valid;
            this.status = status;
            stampsBroughtIn = CarriedStamps(player);
            // seed 由班次表持有（Stream 已按它派生），这里不再单独用；留在签名里与潮闸镇一致。
            foreach (Transform marker in world.LootMarkers)
            {
                string kind; int capacity;
                Classify(marker.name, out kind, out capacity);
                var box = new Box { Id = marker.name, Kind = kind, Capacity = capacity, Anchor = marker, Yaw = marker.eulerAngles.y,
                    Gated = kind == "sealed" || kind == "drop" };
                boxes.Add(box);
                if (kind == "risk") riskIds.Add(box.Id);
            }
            Transform sideAnchor;
            if (world.Interacts.TryGetValue(SideId, out sideAnchor))
                boxes.Add(new Box { Id = SideId, Kind = "side", Capacity = SideCapacity, Anchor = sideAnchor, Yaw = sideAnchor.eulerAngles.y, Gated = true });
            else Debug.LogWarning(LastShiftRaidScene.LogTag + " 缺少铅封侧柜标记 Interact/" + SideId);
            riskIds.Sort(StringComparer.Ordinal);
            PlanRaid();
            foreach (Box box in boxes) if (box.Gated) box.Device = AddDevice(box);
            InteractableLootbox.OnStartLoot += OnStartLoot;
            lootSubscribed = true;
            timetable.PhaseChanged += OnPhase;
            phaseSubscribed = true;
        }

        // ---------------- 开局排货（原型 planLoot） ----------------

        private void PlanRaid()
        {
            System.Random plan = timetable.Stream("raid#plan");
            bool red = plan.Next(100) < RedChance;
            if (red && riskIds.Count > 0) { redCrate = riskIds[plan.Next(riskIds.Count)]; redGrade = PickTable(plan, RedGrades); }
            var luckyPool = new List<string>();
            foreach (Box b in boxes) if (b.Kind == "medical" || b.Kind == "tool") luckyPool.Add(b.Id);
            luckyPool.Sort(StringComparer.Ordinal);
            if (plan.Next(100) < LuckyChance && luckyPool.Count > 0) luckyCrate = luckyPool[plan.Next(luckyPool.Count)];
            // 按 id 顺序把每只普通箱和侧柜排一次：精品去重只看这个固定顺序，与开箱先后无关。
            var order = boxes.FindAll(b => b.Kind != "sealed" && b.Kind != "drop");
            order.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (Box box in order)
            {
                try { box.Planned = Fill(box.Id, box.Kind, SpecOf(box.Kind), box.Capacity); }
                catch (Exception e) { box.Planned = new List<Slot>(); Debug.LogWarning(LastShiftRaidScene.LogTag + " 搜刮点 " + box.Id + " 排货失败：" + e.Message); }
            }
            Debug.Log(LastShiftRaidScene.LogTag + " LOOT_PLAN boxes=" + boxes.Count + " red=" + (redCrate ?? "-") + (redCrate != null ? "/Q" + redGrade : "")
                + " lucky=" + (luckyCrate ?? "-") + " manifest=" + manifest + " premium=" + timetable.Premium);
        }

        /// <summary>原型 fillContainer：件数表 → 每格类别与品质 → 成套头两格 → 精品头格（红 / 幸运）。</summary>
        private List<Slot> Fill(string streamId, string kind, Spec spec, int capacity)
        {
            System.Random r = timetable.Stream(streamId + "#stock");
            int count = Math.Min(capacity, PickTable(r, spec.Counts));
            var slots = new List<Slot>(count);
            for (int i = 0; i < count; i++)
            {
                Cat category = i == 0 ? spec.First : spec.Extras[r.Next(spec.Extras.Length)];
                int grade = PickGrade(r, spec.Lo, spec.Hi);
                int typeId;
                if (spec.Kit != null && i < 2)
                {
                    // 成套头两格：Q1 : Q2 : Q3 = 4 : 2 : 1，同一趟同一格位不重样。
                    category = spec.Kit[i];
                    grade = PickTable(r, KitWeights);
                    HashSet<int> used;
                    string key = kind + i;
                    if (!kitsUsed.TryGetValue(key, out used)) kitsUsed[key] = used = new HashSet<int>();
                    typeId = grades.Pick(category, grade, 1, 3, r, used);
                    if (typeId > 0) used.Add(typeId);
                }
                else typeId = grades.Pick(category, grade, spec.Lo, spec.Hi, r, null);
                if (typeId <= 0) continue;
                slots.Add(new Slot { TypeId = typeId, Amount = category == Cat.Ammo ? AmmoMin + r.Next(AmmoMax - AmmoMin + 1) : 1, Category = category });
            }
            System.Random pr = timetable.Stream(streamId + "#prize");
            if (spec.Prize > 0)
            {
                int grade = streamId == redCrate ? redGrade : spec.Prize;
                Cat category;
                int prize = PickPrize(pr, PrizeTheme(streamId, kind), grade, out category);
                if (prize > 0) SetHead(slots, prize, category);
            }
            if (streamId == luckyCrate)
            {
                Cat theme = kind == "medical" ? Cat.Medical : Cat.Workshop;
                Cat category;
                int prize = PickPrize(pr, Table((int)theme, 100), PrizeGrade, out category);
                if (prize > 0) SetHead(slots, prize, category);
            }
            return slots;
        }

        private static void SetHead(List<Slot> slots, int typeId, Cat category)
        {
            var head = new Slot { TypeId = typeId, Amount = 1, Category = category };
            if (slots.Count == 0) slots.Add(head); else slots[0] = head;
        }

        /// <summary>
        /// 精品一格：Q4 只出 Q4（不越过红的限额）；Q5 / Q6 是红，目标档缺货时退到最近的红档。同一趟不重样，最多重抽 6 次（原型 prize）。
        /// 现货与筛选（可卖、按品质的最低标价、不给钥匙）全用潮闸镇的 <see cref="TidegateLoot.PrizeCatalog"/>。
        /// </summary>
        private int PickPrize(System.Random r, int[][] theme, int grade, out Cat category,
            TidegateLoot.PrizeCatalog catalog = null, HashSet<int> used = null)
        {
            catalog = catalog ?? prizes;
            used = used ?? prizesUsed;
            int floor = grade >= 5 ? 5 : 4, id = 0;
            category = Cat.General;
            for (int attempt = 0; attempt < DistinctAttempts; attempt++)
            {
                category = (Cat)PickTable(r, theme);
                id = catalog.Pick(category, grade, floor, grade, r, Cat.General);
                if (id > 0 && !used.Contains(id)) break;
            }
            if (id > 0) used.Add(id);
            return id;
        }

        // ---------------- 每帧（0.5 秒一次）：懒建箱子 ----------------

        internal void Tick(CharacterMainControl player)
        {
            if (closed || player == null || Time.time < nextTick || valid == null || !valid()) return;
            nextTick = Time.time + TickInterval;
            Vector3 origin = player.transform.position;
            Box nearest = null;
            float best = ActivationRange * ActivationRange;
            for (int i = 0; i < boxes.Count; i++)
            {
                Box box = boxes[i];
                if (box.RearmPending) Rearm(box);
                // 机关在完成回调里不直接停用自己，留到这里收起；签收柜收回重挂后机关再亮出来。
                if (box.Unlocked && box.Device != null && box.Device.activeSelf) box.Device.SetActive(false);
                else if (!box.Unlocked && box.Gated && box.Device != null && !box.Device.activeSelf) box.Device.SetActive(true);
                if (box.Built || box.Failed) continue;
                float distance = (box.Anchor.position - origin).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = box;
            }
            // 一次只建一个：走进一片新区域时不集中做实例化与物品创建。
            if (nearest != null) Build(nearest);
        }

        private void Build(Box box)
        {
            box.Built = true;
            Transform parent = world.Root.transform;
            if (box.Kind == "sealed" || box.Kind == "side")
            {
                // 共享建箱会立即启用新对象，先挂在停用父节点下隔离交互和库存。
                if (cabinetStaging == null)
                {
                    cabinetStaging = new GameObject("LastShiftCabinetStaging");
                    cabinetStaging.SetActive(false);
                    cabinetStaging.transform.SetParent(parent, false);
                }
                parent = cabinetStaging.transform;
            }
            string error;
            InteractableLootbox loot = SkyIslandRewardCrate.Build(parent, box.Anchor.position, box.Yaw, "LastShiftLoot_" + box.Id, out error, true);
            if (loot == null)
            {
                box.Failed = true;
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 搜刮点 " + box.Id + " 准备失败：" + error);
                return;
            }
            box.Loot = loot;
            foreach (Renderer renderer in loot.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            // 官方箱子预制件的提示高度是按它自己那只小木箱定的；本图的箱柜高矮不一，提示挪到箱面中部（官方 InteractHUD 每帧读这个偏移）
            loot.interactMarkerOffset = LastShiftWorld.CrateMarkerOffset(box.Kind);
            try { loot.Inventory.SetCapacity(box.Capacity); } catch (Exception) { /* 容量写不进去就用共享建箱的 12 格 */ }
            var log = new StringBuilder();
            int added = 0;
            if (box.Planned != null)
                foreach (Slot slot in box.Planned)
                    if (SkyIslandRewardCrate.AddOfficialStack(loot, slot.TypeId, slot.Amount, log, slot.Category.ToString())) added++;
            if (box.Gated && !box.Unlocked) loot.gameObject.SetActive(false);
            Debug.Log(LastShiftRaidScene.LogTag + " LOOT_READY id=" + box.Id + " kind=" + box.Kind + " items=" + added + " " + log);
            if (box.PendingBatch != null) ApplyDrop(box);
        }

        private Box Find(string id) { return boxes.Find(b => b.Id == id); }

        /// <summary>有条件的箱子开放：确保已建、箱子启用、机关停用（机关下一次 Tick 才收起，期间它的可用判断已经返回 false）。</summary>
        private bool Unlock(Box box)
        {
            if (!box.Built) Build(box);
            if (box.Loot == null) return false;
            box.Unlocked = true;
            box.Loot.gameObject.SetActive(true);
            return true;
        }

        private void OnStartLoot(InteractableLootbox loot)
        {
            if (closed || loot == null) return;
            for (int i = 0; i < boxes.Count; i++)
            {
                Box box = boxes[i];
                if (box.Loot != loot) continue;
                if (box.Kind == "drop" && !box.BatchOpened)
                {
                    box.BatchOpened = true;
                    if (box.BatchVoyage && status != null)
                        status(L10n.T("这箱是押运车上卸下来的，封条还新。", "This crate came off the escort van. The seal's still fresh."), false);
                }
                if (box.Opened) return;
                box.Opened = true;
                Action<string> handler = Opened;
                if (handler != null)
                {
                    try { handler(box.Id); }
                    catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 开箱回调异常 " + box.Id + "：" + e.Message); }
                }
                return;
            }
        }

        // ---------------- 机关：封签货 / 零担箱 / 铅封侧柜 ----------------

        private GameObject AddDevice(Box box)
        {
            try
            {
                var host = new GameObject("LastShiftLootDevice_" + box.Id);
                host.transform.SetParent(world.Root.transform, false);
                host.transform.position = box.Anchor.position;
                float seconds = box.Kind == "sealed" ? 1f : box.Kind == "side" ? 1.2f : 0.3f;
                Func<string> label = delegate { return DeviceLabel(box); };
                Func<bool> available = delegate { return !closed && !box.Unlocked && valid != null && valid(); };
                Action action = delegate { OnDevice(box); };
                var device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(host.transform, "Interact", LastShiftWorld.CrateMarkerOffset(box.Kind), x =>
                {
                    x.Bind(box.Id, seconds, label, available, action);
                    SphereCollider sensor = x.GetComponent<SphereCollider>();
                    if (sensor != null) { sensor.radius = 1.2f; sensor.isTrigger = true; }
                });
                if (device == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 箱柜机关创建失败：" + box.Id);
                return host;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 箱柜机关创建失败：" + box.Id + "：" + e.Message);
                return null;
            }
        }

        private string DeviceLabel(Box box)
        {
            switch (box.Kind)
            {
                case "sealed": return SignedThisTrain ? L10n.T("封签货（这一班签过了）", "Sealed cargo (signed for this train)") : L10n.T("签收封签货", "Sign for the sealed cargo");
                case "drop": return L10n.T("零担箱（空）", "Freight-lot crate (empty)");
                default: return L10n.T("用钢印开铅封侧柜", "Open the sealed cabinet with the stamp");
            }
        }

        private void OnDevice(Box box)
        {
            if (closed || box.Unlocked || valid == null || !valid()) return;
            try
            {
                if (box.Kind == "sealed") ClaimCargo(box);
                else if (box.Kind == "drop") Say(L10n.T("箱子空的。等下一班车停" + LastShiftText.PlatformName(SideOf(box)) + "台。",
                    "The crate's empty. Wait for the next train to stop at the " + LastShiftText.LineName(SideOf(box)) + " platform."), false);
                else OpenSideCabinet(box);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 箱柜机关 " + box.Id + " 处理失败：" + e.Message);
            }
        }

        private static char SideOf(Box box) { return box.Id.EndsWith("E", StringComparison.Ordinal) ? 'E' : 'W'; }

        /// <summary>本班停在这一侧才签得了，每班停靠最多签一柜（2026-11-13 起不再一趟只签一次）；一柜货见 <see cref="PlanCargo"/>。</summary>
        private void ClaimCargo(Box box)
        {
            if (cargoPreparing) return;
            char side = SideOf(box);
            if (SignedThisTrain) { Say(L10n.T("这一班签过了，等下一班。", "Already signed for this train. Wait for the next one."), false); return; }
            if (timetable.Phase != LastShiftPhase.Dwell)
            { Say(L10n.T("台边没停车，没东西可签。", "No train at the platform. Nothing to sign for."), false); return; }
            if (timetable.LineUsed != side)
            {
                Say(L10n.T("车停在" + LastShiftText.PlatformName(timetable.LineUsed) + "台那边。",
                    "The train's standing at the " + LastShiftText.LineName(timetable.LineUsed) + " platform."), false);
                return;
            }
            bool premium = timetable.Premium == side;
            Item head = null;
            cargoPreparing = true;
            try { head = DeliverCargo(box, premium); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 封签货装柜失败 id=" + box.Id + "：" + e.Message); }
            finally
            {
                cargoPreparing = false;
                if (!SignedThisTrain) RemoveCargoBox(box);
            }
            if (head == null)
            {
                if (!closed) Say(L10n.T("货还没点齐，等会儿再签。", "The cargo isn't ready. Try signing again in a moment."), true);
                return;
            }
            // 第一柜顺手提一句押运库那头的动静（owner 2026-11-11），之后几柜写押运加派的动静；都不念数字。
            Say(LastShiftText.SignedLine(Signatures, NameOf(head.TypeID)), false);
        }

        /// <summary>第 <paramref name="index"/> 柜（0 起）的件数：第一柜 8 / 9，续签 4 / 5。</summary>
        internal static int CargoCount(bool premium, int index)
        {
            if (index == 0) return premium ? SealedPremiumCount : SealedCount;
            return premium ? ResignPremiumCount : ResignCount;
        }

        /// <summary>第 <paramref name="index"/> 柜保底几格精品：第一柜两格（Q4+ 头格、Q4 第二格），续签只有头格。</summary>
        internal static int PrizeSlots(int index) { return index == 0 ? 2 : 1; }

        /// <summary>续签头格出 Q5 的百分比（第 <paramref name="index"/> 柜，1 起）。</summary>
        internal static int ResignQ5Chance(bool premium, int index)
        {
            int chance = Math.Min(ResignQ5Cap, ResignQ5Base + ResignQ5Step * Math.Max(1, index));
            return premium ? Math.Min(ResignPremiumCap, chance + ResignPremiumBonus) : chance;
        }

        private Item DeliverCargo(Box box, bool premium)
        {
            int index = Signatures;
            int total = CargoCount(premium, index);
            int grade;
            List<Slot> cargo = PlanCargo(premium, index, out grade);
            if (cargo.Count != total)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 封签货没排齐 id=" + box.Id + " planned=" + cargo.Count + "/" + total);
                return null;
            }
            if (box.Loot == null) Build(box);
            InteractableLootbox loot = box.Loot;
            if (loot == null) return null;
            Inventory inventory = loot.Inventory;
            if (inventory == null || inventory.gameObject != loot.gameObject) return null;
            var log = new StringBuilder();
            foreach (Slot slot in cargo)
                if (!SkyIslandRewardCrate.AddOfficialStack(loot, slot.TypeId, slot.Amount, log, slot.Category.ToString()) || closed)
                    return null;
            int prizeSlots = PrizeSlots(index);
            if (!CargoMatches(inventory, cargo, grade, prizeSlots))
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 封签货实装与货单不符 id=" + box.Id + " " + log);
                return null;
            }
            if (!valid() || timetable.Phase != LastShiftPhase.Dwell || timetable.LineUsed != SideOf(box)) return null;
            Item head = inventory.GetItemAt(0), second = inventory.GetItemAt(1);
            // 箱子保持停用，等挪出暂存节点后再开放。开放前的失败可销毁整个本地库存，下次重试仍从空箱开始。
            loot.transform.SetParent(world.Root.transform, true);
            loot.gameObject.SetActive(true);
            if (closed || !loot.gameObject.activeInHierarchy) return null;
            box.Unlocked = true;
            box.RearmPending = false;
            Signatures = index + 1;
            SignedTrip = timetable.Trip;
            prizesUsed.Add(head.TypeID);
            if (prizeSlots > 1) prizesUsed.Add(second.TypeID);
            Debug.Log(LastShiftRaidScene.LogTag + " CARGO_CLAIMED id=" + box.Id + " items=" + total + "/" + total + " sig=" + Signatures
                + " trip=" + SignedTrip + " q=" + head.Quality + "," + second.Quality + " premium=" + premium + " " + log);
            return head;
        }

        private static bool CargoMatches(Inventory inventory, List<Slot> cargo, int headGrade, int prizeSlots)
        {
            if (inventory == null || inventory.GetItemCount() != cargo.Count) return false;
            for (int i = 0; i < cargo.Count; i++)
            {
                Item item = inventory.GetItemAt(i);
                if (item == null || item.InInventory != inventory || item.IsBeingDestroyed || item.TypeID != cargo[i].TypeId) return false;
                if (i < prizeSlots)
                {
                    if (item.Quality != (i == 0 ? headGrade : PrizeGrade)) return false;
                }
                else if (item.Quality < SealedFillLo || item.Quality > SealedFillHi) return false;
            }
            return true;
        }

        /// <summary>
        /// 新一班停到这一侧：把上一班签出来的柜子收回去（官方界面开着时等它关上），换回签收机关。
        /// 柜里还剩东西就说一句「被押运车收回去了」——想要就得在下一班进站前搬空。
        /// </summary>
        private void Rearm(Box box)
        {
            if (!box.RearmPending) return;
            if (UiBusy != null && UiBusy()) return;
            box.RearmPending = false;
            if (!box.Unlocked) return;
            int left = 0;
            try { if (box.Loot != null && box.Loot.Inventory != null) left = box.Loot.Inventory.GetItemCount(); }
            catch (Exception) { /* 数不到就当空柜 */ }
            RemoveCargoBox(box);
            box.Unlocked = false;
            box.Opened = false; // 新一柜是新箱子：开箱回调照常再报一次
            if (box.Device != null && !box.Device.activeSelf) box.Device.SetActive(true);
            if (left > 0) Say(LastShiftText.CargoReclaimedLine, false);
            Debug.Log(LastShiftRaidScene.LogTag + " CARGO_REARMED id=" + box.Id + " trip=" + timetable.Trip + " reclaimed=" + left);
        }

        private static void RemoveCargoBox(Box box)
        {
            InteractableLootbox loot = box.Loot;
            box.Loot = null;
            if (loot == null) return;
            try { loot.gameObject.SetActive(false); }
            finally { UnityEngine.Object.Destroy(loot.gameObject); }
        }

        /// <summary>
        /// 封签货一柜（owner 2026-11-13「签收奖励太少，起码 7、8 件；品质不用都好，保底几件好的」）：
        /// 第一柜头格照旧（Q4 75% / Q5 25%，加价台各半），第二格保底 Q4，都按班单类别抽精品；其余按班单类别补到 <see cref="SealedCount"/> 件
        /// （加价台 <see cref="SealedPremiumCount"/> 件），品质 Q2–Q4、每高一档权重 ×0.6（同风险柜普通格）。
        /// 续签（第 2 柜起，<paramref name="index"/> ≥ 1）只保底头格，Q5 概率见 <see cref="ResignQ5Chance"/>，件数见 <see cref="CargoCount"/>。
        /// 同一趟同一柜号同一侧结果固定，与签收时刻无关。
        /// </summary>
        private List<Slot> PlanCargo(bool premium, int index, out int headGrade)
        {
            bool first = index == 0;
            System.Random r = timetable.Stream("sealed#" + manifest + (first ? "" : "#" + index) + (premium ? "#premium" : ""));
            int total = CargoCount(premium, index), prizeSlots = PrizeSlots(index);
            var slots = new List<Slot>(total);
            headGrade = first ? PickTable(r, premium ? SealedPremiumGrades : SealedGrades) : (r.Next(100) < ResignQ5Chance(premium, index) ? 5 : PrizeGrade);
            int[][] theme = SealedTheme(manifest);
            // 先在尝试自己的目录与去重集合里抽，空池可重查，失败的货也不占用本趟已发放的精品记录。
            var catalog = new TidegateLoot.PrizeCatalog();
            var used = new HashSet<int>(prizesUsed);
            foreach (int grade in new[] { headGrade, PrizeGrade })
            {
                if (slots.Count >= prizeSlots) break; // 续签只保底头格
                Cat category;
                int prize = PickPrize(r, theme, grade, out category, catalog, used);
                if (prize <= 0) return slots;
                slots.Add(new Slot { TypeId = prize, Amount = 1, Category = category });
            }
            Cat[] fill = SealedFill(manifest);
            var cargoStock = new GradeStock();
            for (int attempt = 0; slots.Count < total && attempt < total * 3; attempt++)
            {
                Cat category = fill[r.Next(fill.Length)];
                int typeId = cargoStock.Pick(category, PickGrade(r, SealedFillLo, SealedFillHi), SealedFillLo, SealedFillHi, r, null);
                if (typeId > 0) slots.Add(new Slot { TypeId = typeId, Amount = category == Cat.Ammo ? AmmoMin + r.Next(AmmoMax - AmmoMin + 1) : 1, Category = category });
            }
            return slots;
        }

        /// <summary>封签货补货的类别（等概率抽一项）：医务 = 医疗为主带口粮，抢修 = 工具为主带弹药，精械 = 护具 / 工具 / 弹药。</summary>
        private static Cat[] SealedFill(string manifest)
        {
            if (manifest == "medical") return MedicalFill;
            if (manifest == "repair") return RepairFill;
            return PrecisionFill;
        }

        /// <summary>原型 activate(stamp)：要上一趟带出来的押运钢印（本趟刚拿的不算），消耗一枚开一次。</summary>
        private void OpenSideCabinet(Box box)
        {
            if (stampPreparing) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            List<Item> carried = CarriedStamps(player);
            if (carried.Count == 0) { Say(L10n.T("柜门压着铅封，得用押运长腰上那枚钢印。", "The door is lead-sealed. It takes the stamp off the chief escort's belt."), false); return; }
            Item stamp = carried.Find(stampsBroughtIn.Contains);
            if (stamp == null)
            {
                Say(L10n.T("钢印刚从押运长身上摘下来，铅封认不出。带出站，下趟再来。", "The stamp's fresh off the chief escort; the seal won't take it. Get it out of the yard and come back next run."), false);
                return;
            }
            stampPreparing = true;
            bool opened = false;
            try { opened = TryOpenStampedBox(box, stamp); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 铅封侧柜开启失败：" + e.Message); }
            finally { stampPreparing = false; }
            if (!opened)
            {
                if (!closed) Say(L10n.T("侧柜卡住了，钢印仍归你。", "The cabinet is stuck. The stamp stays yours."), true);
                return;
            }
            Debug.Log(LastShiftRaidScene.LogTag + " SIDE_CABINET_OPENED");
            Say(L10n.T("钢印压下去，铅封裂开了。", "The stamp comes down and the lead seal cracks."), false);
        }

        private bool TryOpenStampedBox(Box box, Item stamp)
        {
            if (box.Loot == null) Build(box);
            InteractableLootbox loot = box.Loot;
            if (closed || loot == null || loot.Inventory == null || loot.Inventory.gameObject != loot.gameObject
                || loot.Inventory.GetItemCount() == 0) return false;
            TidegateInventory.Reservation stampReservation;
            if (!TidegateInventory.Reservation.TryCreate(stamp, false, out stampReservation)) return false;
            using (stampReservation)
            {
                try
                {
                    if (closed || loot == null || !valid()) return false;
                    loot.transform.SetParent(world.Root.transform, true);
                    loot.gameObject.SetActive(true);
                    if (closed || loot == null || !loot.gameObject.activeInHierarchy || !valid()) return false;
                    box.Unlocked = true;
                    stampReservation.Commit();
                    return true;
                }
                finally
                {
                    if (!box.Unlocked && loot != null && loot.gameObject.activeSelf) loot.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>原型 trainArrived：首趟第 2 班起，每列停靠的车在它停的那一台卸一批零担（快车不停靠）；约 1/4 是押运零担（航务档）。</summary>
        private void OnPhase(LastShiftPhase previous, LastShiftPhase next)
        {
            if (closed || next != LastShiftPhase.Dwell || timetable.IsExpress) return;
            // 每班可签：这一侧上一班签出来的柜子收回，换成这一班的签收机关。
            Box signed = Find(timetable.LineUsed == 'E' ? "cargoE" : "cargoW");
            if (signed != null && signed.Unlocked)
            {
                signed.RearmPending = true;
                try { Rearm(signed); }
                catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 签收柜换班失败：" + e.Message); }
            }
            if (timetable.Training) return;
            try
            {
                char line = timetable.LineUsed;
                Box box = Find(line == 'E' ? "dropE" : "dropW");
                if (box == null || box.BatchTrip == timetable.Trip) return;
                string batch = "drop" + line + "#" + timetable.Trip;
                bool voyage = timetable.Stream("drop#" + line + "#" + timetable.Trip).Next(100) < DropVoyageChance;
                box.BatchTrip = timetable.Trip;
                box.BatchVoyage = voyage;
                box.BatchOpened = false;
                box.PendingBatch = Fill(batch, "drop", voyage ? DropVoyageSpec : DropSpec, box.Capacity);
                if (box.Built) ApplyDrop(box);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 零担卸货失败：" + e.Message); }
        }

        private void ApplyDrop(Box box)
        {
            List<Slot> batch = box.PendingBatch;
            box.PendingBatch = null;
            if (batch == null || box.Loot == null) return;
            // 上一批没拿走的随车走了；玩家贴着箱子（搜刮界面可能开着）时不动它，只往里追加。
            CharacterMainControl player = CharacterMainControl.Main;
            bool near = player != null && (player.transform.position - box.Anchor.position).sqrMagnitude <= KeepNearRange * KeepNearRange;
            if (!near && box.Loot.Inventory != null)
            {
                try { box.Loot.Inventory.DestroyAllContent(); } catch (Exception) { /* 清不掉就和新货混在一起 */ }
            }
            var log = new StringBuilder();
            int added = 0;
            foreach (Slot slot in batch)
                if (SkyIslandRewardCrate.AddOfficialStack(box.Loot, slot.TypeId, slot.Amount, log, slot.Category.ToString())) added++;
            Unlock(box);
            Debug.Log(LastShiftRaidScene.LogTag + " DROP_LOT id=" + box.Id + " trip=" + box.BatchTrip + " voyage=" + box.BatchVoyage + " items=" + added + " " + log);
        }

        // ---------------- 小工具 ----------------

        private void Say(string text, bool error) { if (status != null) status(text, error); }

        private static string NameOf(int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null && !string.IsNullOrEmpty(prefab.DisplayName)) return prefab.DisplayName;
            }
            catch (Exception) { /* 名字取不到就只报货单 */ }
            return L10n.T("货单物品", "the listed goods");
        }

        /// <summary>钢印不能堆叠；进图和开柜时扫描官方物品树，按物品实例识别带入本站的钢印。</summary>
        private static List<Item> CarriedStamps(CharacterMainControl player)
        {
            try
            {
                if (player != null && player.CharacterItem != null)
                {
                    List<Item> items = player.CharacterItem.GetAllChildren(true, true);
                    items.RemoveAll(item => item == null || item.TypeID != BossRushItemIds.LastShiftStamp || item.IsBeingDestroyed);
                    return items;
                }
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 读取随身钢印失败：" + e.Message); }
            return new List<Item>();
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            if (lootSubscribed) { InteractableLootbox.OnStartLoot -= OnStartLoot; lootSubscribed = false; }
            if (phaseSubscribed) { timetable.PhaseChanged -= OnPhase; phaseSubscribed = false; }
            Opened = null;
            foreach (Box box in boxes)
            {
                if (box.Device != null) { UnityEngine.Object.Destroy(box.Device); box.Device = null; }
                if (box.Loot == null) continue;
                // 官方 LootView 仍可能持有引用；先停用再销毁。
                try { box.Loot.gameObject.SetActive(false); UnityEngine.Object.Destroy(box.Loot.gameObject); }
                catch (Exception) { /* 场景已卸载 */ }
                box.Loot = null;
            }
            boxes.Clear();
            stampsBroughtIn.Clear();
            if (cabinetStaging != null) UnityEngine.Object.Destroy(cabinetStaging);
            cabinetStaging = null;
        }
    }
}

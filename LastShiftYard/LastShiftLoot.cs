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
        private int stampsBroughtIn = -1;
        private float nextTick;
        private bool closed, lootSubscribed, phaseSubscribed;

        /// <summary>本趟唯一一次封签货签收已经发生。</summary>
        internal bool CargoClaimed { get; private set; }
        /// <summary>某只箱柜第一次被打开（参数是箱子 id；铅封侧柜是 `stamp`）。</summary>
        internal event Action<string> Opened;
        /// <summary>风险柜（布局 pool == 'risk'）：打开会拉响库房警铃。</summary>
        internal IEnumerable<string> RiskContainerIds { get { return riskIds; } }

        internal LastShiftLoot(LastShiftWorld world, LastShiftTimetable timetable, string manifest, int seed, Func<bool> valid, Action<string, bool> status)
        {
            this.world = world;
            this.timetable = timetable;
            this.manifest = manifest ?? "medical";
            this.valid = valid;
            this.status = status;
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
        private int PickPrize(System.Random r, int[][] theme, int grade, out Cat category)
        {
            int floor = grade >= 5 ? 5 : 4, id = 0;
            category = Cat.General;
            for (int attempt = 0; attempt < DistinctAttempts; attempt++)
            {
                category = (Cat)PickTable(r, theme);
                id = prizes.Pick(category, grade, floor, grade, r, Cat.General);
                if (id > 0 && !prizesUsed.Contains(id)) break;
            }
            if (id > 0) prizesUsed.Add(id);
            return id;
        }

        // ---------------- 每帧（0.5 秒一次）：懒建箱子 ----------------

        internal void Tick(CharacterMainControl player)
        {
            if (closed || player == null || Time.time < nextTick || valid == null || !valid()) return;
            nextTick = Time.time + TickInterval;
            if (stampsBroughtIn < 0) stampsBroughtIn = CountCarried(player, BossRushItemIds.LastShiftStamp);
            Vector3 origin = player.transform.position;
            Box nearest = null;
            float best = ActivationRange * ActivationRange;
            for (int i = 0; i < boxes.Count; i++)
            {
                Box box = boxes[i];
                // 机关在完成回调里不直接停用自己，留到这里收起。
                if (box.Unlocked && box.Device != null && box.Device.activeSelf) box.Device.SetActive(false);
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
            string error;
            InteractableLootbox loot = SkyIslandRewardCrate.Build(world.Root.transform, box.Anchor.position, box.Yaw, "LastShiftLoot_" + box.Id, out error, true);
            if (loot == null)
            {
                box.Failed = true;
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 搜刮点 " + box.Id + " 准备失败：" + error);
                return;
            }
            box.Loot = loot;
            foreach (Renderer renderer in loot.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
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
                        status(L10n.T("押运零担：这一批是押运车上卸下来的好货。", "Escort lot: this batch came off the escort van. Better goods."), false);
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
                var device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(host.transform, "Interact", new Vector3(0f, 1f, 0f), x =>
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
                case "sealed": return CargoClaimed ? L10n.T("封签货（本趟已签收）", "Sealed cargo (already signed for)") : L10n.T("签收封签货", "Sign for the sealed cargo");
                case "drop": return L10n.T("零担箱（空）", "Freight-lot crate (empty)");
                default: return L10n.T("用押运钢印开铅封侧柜", "Open the sealed side cabinet with an escort stamp");
            }
        }

        private void OnDevice(Box box)
        {
            if (closed || box.Unlocked || valid == null || !valid()) return;
            try
            {
                if (box.Kind == "sealed") ClaimCargo(box);
                else if (box.Kind == "drop") Say(L10n.T("零担箱是空的。下一班停靠", "The freight-lot crate is empty. The next train to stop at the ")
                    + LastShiftText.PlatformName(SideOf(box)) + L10n.T("台时会卸新货。", " platform unloads a new lot."), false);
                else OpenSideCabinet(box);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 箱柜机关 " + box.Id + " 处理失败：" + e.Message);
            }
        }

        private static char SideOf(Box box) { return box.Id.EndsWith("E", StringComparison.Ordinal) ? 'E' : 'W'; }

        /// <summary>原型 activate(cargoW / cargoE)：本班停在这一侧才签得了，全趟只签一次；货单物品 Q4 75% / Q5 25%，加价台 50% / 50%。</summary>
        private void ClaimCargo(Box box)
        {
            char side = SideOf(box);
            if (CargoClaimed) { Say(L10n.T("这趟封签货已签收，下一班也不会补货。", "This raid's sealed cargo is already signed for. Later trains won't bring more."), false); return; }
            if (timetable.Phase != LastShiftPhase.Dwell)
            { Say(L10n.T("这里还没有本班货车；只有列车停靠的一侧可以签收。", "No train is standing here. Only the platform with a stopped train can sign for the cargo."), false); return; }
            if (timetable.LineUsed != side)
            {
                Say(L10n.T("本班停在", "This train is standing at the ") + LastShiftText.PlatformName(timetable.LineUsed)
                    + L10n.T("台，去那边签收。", " platform. Sign for the cargo there."), false);
                return;
            }
            bool premium = timetable.Premium == side;
            System.Random r = timetable.Stream("sealed#" + manifest + (premium ? "#premium" : ""));
            int grade = PickTable(r, premium ? SealedPremiumGrades : SealedGrades);
            Cat category;
            int item = PickPrize(r, SealedTheme(manifest), grade, out category);
            if (item <= 0 || !Unlock(box) || !SkyIslandRewardCrate.AddOfficialStack(box.Loot, item, 1, new StringBuilder(), "LastShiftSealed"))
            {
                if (box.Loot != null && box.Unlocked && box.Loot.Inventory != null && box.Loot.Inventory.GetItemCount() == 0)
                { box.Unlocked = false; box.Loot.gameObject.SetActive(false); }
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 封签货没能装箱 id=" + box.Id + " item=" + item);
                Say(L10n.T("铅封货单对不上，这一班签不了。等下一班再来。", "The seal doesn't match the manifest. This train can't be signed for; try the next one."), true);
                return;
            }
            CargoClaimed = true;
            Debug.Log(LastShiftRaidScene.LogTag + " CARGO_CLAIMED id=" + box.Id + " item=" + item + " q=" + grade + " premium=" + premium);
            Say(L10n.T("已签收", "Signed for the ") + LastShiftText.ManifestName(manifest) + L10n.T("封签货：", " sealed cargo: ") + NameOf(item)
                + (premium ? L10n.T("（加价台）", " (premium platform)") : "")
                + L10n.T("。本趟签收完毕，去箱里取货。", ". That's this raid's one signature. Collect it from the crate."), false);
        }

        /// <summary>原型 activate(stamp)：要上一趟带出来的押运钢印（本趟刚拿的不算），消耗一枚开一次。</summary>
        private void OpenSideCabinet(Box box)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            if (stampsBroughtIn < 0) stampsBroughtIn = CountCarried(player, BossRushItemIds.LastShiftStamp);
            Item stamp = TidegateInventory.FindCarried(player, BossRushItemIds.LastShiftStamp);
            if (stamp == null) { Say(L10n.T("需要上一趟带出来的押运钢印。", "You need an escort stamp carried out of an earlier raid."), false); return; }
            if (stampsBroughtIn < 1)
            {
                Say(L10n.T("这枚钢印是本趟刚拿到的：带它撤离，下一趟才能开侧柜。", "This stamp was taken this raid. Carry it out; it opens the cabinet on your next trip."), false);
                return;
            }
            if (!box.Built) Build(box);
            if (box.Loot == null) { Say(L10n.T("侧柜卡住了，钢印还在你身上。", "The cabinet is stuck. You keep the stamp."), true); return; }
            try { TidegateInventory.Consume(stamp); }
            catch (Exception e)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运钢印消耗失败：" + e.Message);
                Say(L10n.T("钢印没压上，侧柜还锁着。", "The stamp didn't take. The cabinet is still sealed."), true);
                return;
            }
            stampsBroughtIn--;
            Unlock(box);
            Debug.Log(LastShiftRaidScene.LogTag + " SIDE_CABINET_OPENED");
            Say(L10n.T("消耗一枚押运钢印，铅封侧柜打开了。", "One escort stamp used. The sealed side cabinet is open."), false);
        }

        /// <summary>原型 trainArrived：首趟第 2 班起，每列停靠的车在它停的那一台卸一批零担（快车不停靠）；约 1/4 是押运零担（航务档）。</summary>
        private void OnPhase(LastShiftPhase previous, LastShiftPhase next)
        {
            if (closed || next != LastShiftPhase.Dwell || timetable.Training || timetable.IsExpress) return;
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

        /// <summary>主角整棵物品树里这件物品一共几枚。整树遍历会分配列表，只在开局一次与开侧柜时用。</summary>
        private static int CountCarried(CharacterMainControl player, int typeId)
        {
            int count = 0;
            try
            {
                if (player == null || player.CharacterItem == null) return 0;
                List<Item> all = player.CharacterItem.GetAllChildren(true, true);
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].TypeID == typeId && !all[i].IsBeingDestroyed) count += Math.Max(1, all[i].StackCount);
            }
            catch (Exception) { /* 数不清就当没带 */ }
            return count;
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
        }
    }
}

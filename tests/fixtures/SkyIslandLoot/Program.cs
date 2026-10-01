using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;

// 生产代码的委托文案走 L10n.T（英文玩家不该看到中文派单）。L10n 依赖官方
// LocalizationManager，隔离进程里用最小替身顶上：返回中文分支，断言只看布尔与枚举，不看文案。
namespace BossRush { internal static class L10n { internal static string T(string zh, string en) { return zh; } } }

/// <summary>
/// 天空岛搜刮点内容表与航务委托的隔离执行回归。
///
/// 这两份都是**无 Unity 依赖的生产代码**，因此直接链接真源文件执行，只替身一个 L10n：
/// 覆盖率断言（每个区域都有产出、深处更值钱）、随机流的确定性，以及委托的基线/升级语义。
/// </summary>
internal static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("FAIL " + description);
    }

    private static void AnchorCoverage()
    {
        SkyIslandLootAnchor[] anchors = SkyIslandLootTables.Anchors;
        Check(anchors.Length == 39, "39 scavenging anchors");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var regions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SkyIslandLootAnchor anchor in anchors)
        {
            Check(ids.Add(anchor.Id), "anchor id unique: " + anchor.Id);
            Check(!string.IsNullOrEmpty(anchor.Marker), "anchor has a marker: " + anchor.Id);
            Check(anchor.Distance >= 4f && anchor.Distance <= 12f, "anchor offset sane: " + anchor.Id);
            Check(anchor.Bearing >= 0f && anchor.Bearing < 360f, "anchor bearing sane: " + anchor.Id);
            int count;
            regions.TryGetValue(anchor.Region, out count);
            regions[anchor.Region] = count + 1;
        }
        // 每个可到达区域都必须有产出：走到哪里都有可搜的东西，才谈得上「完整体验」。
        string[] expected = { "A", "B", "C", "D", "E", "F", "G", "H", "S1", "S2", "S3", "S4" };
        Check(regions.Count == expected.Length, "every region covered");
        foreach (string region in expected)
            Check(regions.ContainsKey(region) && regions[region] >= 1, "region has loot: " + region);
        Check(SkyIslandLootTables.CountOfTier(SkyIslandLootTier.Supply) == 11, "supply tier count");
        Check(SkyIslandLootTables.CountOfTier(SkyIslandLootTier.Voyage) == 18, "voyage tier count");
        Check(SkyIslandLootTables.CountOfTier(SkyIslandLootTier.Starworks) == 10, "starworks tier count");
        Check(SkyIslandLootTables.CountOfRegion("H") == 4, "bell court is a high-value region");
        Check(SkyIslandLootTables.CountOfRegion("Nowhere") == 0, "unknown region has no loot");
    }

    private static void QualityBands()
    {
        // 深处更值钱：三档的上下限必须严格递增，否则「风险-回报」这条设计承诺就是空的。
        SkyIslandLootTier[] order = { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks };
        for (int i = 0; i < order.Length; i++)
        {
            int min = SkyIslandLootTables.MinQuality(order[i]);
            int max = SkyIslandLootTables.MaxQuality(order[i]);
            Check(min >= 1 && max <= 8 && min < max, "band sane: " + order[i]);
            Check(SkyIslandLootTables.MinCount(order[i]) >= 1, "min count sane: " + order[i]);
            Check(SkyIslandLootTables.MaxCount(order[i]) >= SkyIslandLootTables.MinCount(order[i]),
                "count band sane: " + order[i]);
            // owner 2026-10-01：每箱 3–7 格（含保底特产那一格），容量 12 格放得下。
            Check(SkyIslandLootTables.MinCount(order[i]) >= SkyIslandLootTables.CrateMinSlots
                  && SkyIslandLootTables.MaxCount(order[i]) <= SkyIslandLootTables.CrateMaxSlots,
                "count stays within the 3-7 slot budget: " + order[i]);
            if (i == 0) continue;
            Check(SkyIslandLootTables.MinQuality(order[i]) > SkyIslandLootTables.MinQuality(order[i - 1]),
                "min quality escalates at " + order[i]);
            Check(SkyIslandLootTables.MaxQuality(order[i]) > SkyIslandLootTables.MaxQuality(order[i - 1]),
                "max quality escalates at " + order[i]);
            // 件数也必须随档次单调不减。旧表里航务补给是 2–4、星工遗存反而只有 2–3：
            // 中段区域比全图最深处出得还多，与品质带的递增方向相反，玩家的最优解变成「别往深处走」。
            // 上面那两条只钉品质，钉不住这个，得单独钉。
            Check(SkyIslandLootTables.MaxCount(order[i]) >= SkyIslandLootTables.MaxCount(order[i - 1]),
                "max count does not regress at " + order[i]);
            Check(SkyIslandLootTables.MinCount(order[i]) >= SkyIslandLootTables.MinCount(order[i - 1]),
                "min count does not regress at " + order[i]);
        }
        // 保底只给「赚来的」奖励用，且必须落在该档自己的品质带内，否则保底带会是空的。
        Check(SkyIslandLootTables.GuaranteeMinQuality(SkyIslandLootTier.Supply) == 0, "supply tier has no guarantee");
        foreach (SkyIslandLootTier tier in new[] { SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks })
        {
            int floor = SkyIslandLootTables.GuaranteeMinQuality(tier);
            Check(floor > 0, "earned reward tier has a guarantee: " + tier);
            Check(floor >= SkyIslandLootTables.MinQuality(tier) && floor <= SkyIslandLootTables.MaxQuality(tier),
                "guarantee floor sits inside its own band: " + tier);
        }
        Check(SkyIslandLootTables.GuaranteeMinQuality(SkyIslandLootTier.Starworks) >
              SkyIslandLootTables.GuaranteeMinQuality(SkyIslandLootTier.Voyage),
            "deeper tier guarantees a better floor");
        // 游戏一共 8 档品质（既有 Boss 奖池按 1-8 取）。最深处必须够得到顶档，
        // 否则第 8 档物品在天空岛永远刷不出来。
        Check(SkyIslandLootTables.MaxQuality(SkyIslandLootTier.Starworks) == 8,
            "the deepest tier can reach the top quality band");
    }

    private static void SeededStreams()
    {
        // 同一次出击、同一个点永远是同一份内容：离开再回来不能刷出第二份。
        for (int seed = -3; seed < 5; seed++)
        {
            List<int> first = Draw(seed, "G1"), again = Draw(seed, "G1");
            Check(Same(first, again), "same seed and point replays identically");
        }
        Check(!Same(Draw(7, "G1"), Draw(7, "G2")), "different points draw differently");
        Check(!Same(Draw(7, "G1"), Draw(8, "G1")), "different raids draw differently");
        Check(SkyIslandLootTables.StableHash("G1") == SkyIslandLootTables.StableHash("G1"), "hash is stable");
        Check(SkyIslandLootTables.StableHash("G1") != SkyIslandLootTables.StableHash("G2"), "hash separates points");
        Check(SkyIslandLootTables.StableHash("G1") >= 0, "hash stays non-negative for seeding");
        Check(SkyIslandLootTables.StableHash(null) == 0 && SkyIslandLootTables.StableHash("") == 0, "hash tolerates empty");
        // RollCount 必须始终落在「档次区间按岛区上移、再夹进 [3, 7]」之内，且区间两端都抽得到。
        var seen = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (SkyIslandLootAnchor anchor in SkyIslandLootTables.Anchors)
        {
            int bonus = SkyIslandLootTables.IsRichRegion(anchor.Region) ? SkyIslandLootTables.RichRegionBonus : 0;
            int low = Math.Min(Math.Max(SkyIslandLootTables.MinCount(anchor.Tier) + bonus, 3), 7);
            int high = Math.Min(Math.Max(SkyIslandLootTables.MaxCount(anchor.Tier) + bonus, 3), 7);
            var counts = new HashSet<int>();
            for (int seed = 0; seed < 200; seed++)
            {
                int count = SkyIslandLootTables.RollCount(anchor.Tier, anchor.Region,
                    SkyIslandLootTables.CreateStream(seed, "count:" + anchor.Id));
                Check(count >= low && count <= high, "roll within band: " + anchor.Id);
                Check(count >= SkyIslandLootTables.CrateMinSlots && count <= SkyIslandLootTables.CrateMaxSlots, "roll within 3-7: " + anchor.Id);
                counts.Add(count);
            }
            Check(counts.Count == high - low + 1, "every slot count in the band is reachable: " + anchor.Id);
            seen[anchor.Id] = counts;
        }
        // 具体口径（owner 2026-10-01）：码头 3–4，悬根林 4–6，镜水寺 5–7（富裕），工坊 / 钟庭 6–7。
        Check(seen["A1"].Min() == 3 && seen["A1"].Max() == 4, "supply crates hold 3-4 slots");
        Check(seen["D1"].Min() == 4 && seen["D1"].Max() == 6, "voyage crates hold 4-6 slots");
        Check(seen["F1"].Min() == 5 && seen["F1"].Max() == 7 && seen["S3a"].Min() == 5, "rich voyage regions shift up one slot");
        Check(seen["G1"].Min() == 6 && seen["H4"].Max() == 7 && seen["S4b"].Max() == 7, "starworks crates hold 6-7 slots in rich regions");
        string[] rich = { "F", "G", "H", "S3", "S4" };
        foreach (string region in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "S1", "S2", "S3", "S4" })
            Check(SkyIslandLootTables.IsRichRegion(region) == (Array.IndexOf(rich, region) >= 0), "rich region table: " + region);
        Check(!SkyIslandLootTables.IsRichRegion(null), "unknown region is not rich");
    }

    /// <summary>格子拆分：保底特产一格 + 额外特产追加一格（顶到 7 格才顶掉一格官方物品），总格数恒在 [3, 7]。</summary>
    private static void SlotSplit()
    {
        for (int slots = SkyIslandLootTables.CrateMinSlots; slots <= SkyIslandLootTables.CrateMaxSlots; slots++)
        {
            int plain = SkyIslandLootTables.OfficialSlots(slots, false);
            int withExtra = SkyIslandLootTables.OfficialSlots(slots, true);
            Check(plain == slots - 1, "staple takes exactly one slot: " + slots);
            Check(1 + plain >= SkyIslandLootTables.CrateMinSlots && 1 + plain <= SkyIslandLootTables.CrateMaxSlots, "plain crate stays in 3-7: " + slots);
            Check(2 + withExtra >= SkyIslandLootTables.CrateMinSlots && 2 + withExtra <= SkyIslandLootTables.CrateMaxSlots, "extra crate stays in 3-7: " + slots);
            Check(withExtra == (slots >= SkyIslandLootTables.CrateMaxSlots ? plain - 1 : plain), "extra only displaces an official item at the cap: " + slots);
            Check(withExtra >= 1, "every crate keeps at least one official item: " + slots);
        }
    }

    /// <summary>类别牌：同一流结果固定、不放回、超出牌数补通用池；每档牌堆都有武器 / 子弹 / 装备 / 物资。</summary>
    private static void CategoryDeal()
    {
        foreach (SkyIslandLootTier tier in new[] { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks })
        {
            SkyIslandLootCategory[] deck = SkyIslandLootTables.CategoryDeck(tier);
            Check(deck.Length >= SkyIslandLootTables.CrateMaxSlots - 1, "deck covers a full crate: " + tier);
            foreach (SkyIslandLootCategory need in new[] { SkyIslandLootCategory.Weapon, SkyIslandLootCategory.Ammo,
                SkyIslandLootCategory.Gear, SkyIslandLootCategory.Supplies })
                Check(Array.IndexOf(deck, need) >= 0, "deck has " + need + ": " + tier);
            for (int seed = 0; seed < 300; seed++)
            {
                SkyIslandLootCategory[] first = SkyIslandLootTables.DealCategories(tier, 6, SkyIslandLootTables.CreateStream(seed, "G1"));
                SkyIslandLootCategory[] again = SkyIslandLootTables.DealCategories(tier, 6, SkyIslandLootTables.CreateStream(seed, "G1"));
                Check(first.SequenceEqual(again), "same stream deals the same categories");
                // 不放回：一副牌全发出去就是这副牌本身（多重集合相等）。
                Check(first.OrderBy(c => c).SequenceEqual(deck.OrderBy(c => c)), "a full deal uses every card exactly once: " + tier);
                SkyIslandLootCategory[] two = SkyIslandLootTables.DealCategories(tier, 2, SkyIslandLootTables.CreateStream(seed, "A1"));
                foreach (SkyIslandLootCategory card in two.Distinct())
                    Check(two.Count(c => c == card) <= deck.Count(c => c == card), "no category beyond its cards: " + tier);
            }
            SkyIslandLootCategory[] overflow = SkyIslandLootTables.DealCategories(tier, deck.Length + 2, new Random(1));
            Check(overflow[deck.Length] == SkyIslandLootCategory.General && overflow[deck.Length + 1] == SkyIslandLootCategory.General,
                "slots beyond the deck fall back to the general pool");
            Check(SkyIslandLootTables.DealCategories(tier, 0, new Random(1)).Length == 0, "no slots, no cards");
            // 子弹一格一堆，深处给得多。
            Check(SkyIslandLootTables.AmmoStackMin(tier) >= 10 && SkyIslandLootTables.AmmoStackMin(tier) <= SkyIslandLootTables.AmmoStackMax(tier),
                "ammo stack band sane: " + tier);
            for (int seed = 0; seed < 100; seed++)
            {
                int stack = SkyIslandLootTables.RollAmmoStack(tier, new Random(seed));
                Check(stack >= SkyIslandLootTables.AmmoStackMin(tier) && stack <= SkyIslandLootTables.AmmoStackMax(tier), "ammo stack in band: " + tier);
            }
        }
        Check(SkyIslandLootTables.AmmoStackMax(SkyIslandLootTier.Starworks) > SkyIslandLootTables.AmmoStackMax(SkyIslandLootTier.Supply),
            "deeper crates hold bigger ammo stacks");
        // 类别池预热表：每一档常规带 × 四类，正好 12 个，不含通用类（通用带由 PrewarmBands 预热）。
        int[][] bands = SkyIslandLootTables.PrewarmCategoryBands();
        Check(bands.Length == 12, "category prewarm covers 3 tiers x 4 categories");
        foreach (SkyIslandLootTier tier in new[] { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks })
            foreach (SkyIslandLootCategory category in SkyIslandLootTables.PoolCategories)
                Check(bands.Count(b => b[0] == (int)category && b[1] == SkyIslandLootTables.MinQuality(tier)
                    && b[2] == SkyIslandLootTables.MaxQuality(tier)) == 1, "category band prewarmed once: " + tier + "/" + category);
        Check(bands.All(b => b[0] != (int)SkyIslandLootCategory.General), "general pool is not duplicated in the category prewarm");
    }

    private static List<int> Draw(int seed, string id)
    {
        Random random = SkyIslandLootTables.CreateStream(seed, id);
        var result = new List<int>();
        for (int i = 0; i < 8; i++) result.Add(random.Next(1000));
        return result;
    }

    private static bool Same(List<int> left, List<int> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
        return true;
    }

    private static void BountyFlow()
    {
        var bounty = new SkyIslandBounty();
        string message;
        Check(!bounty.HasActive && bounty.Progress == 0, "no contract at raid start");
        Check(!bounty.TryClaim(t => true, out _, out message), "cannot claim without a contract");

        // 接单前已经做过的量不计入本单，也不会倒扣。
        bounty.ReportScavenged();
        bounty.ReportScavenged();
        Check(bounty.TryAccept(SkyIslandBountyKind.Salvage, out message), "salvage contract accepted");
        Check(bounty.Active == SkyIslandBountyKind.Salvage, "accepted kind is recorded");
        Check(bounty.Progress == 0, "accept-time baseline excludes earlier work");
        Check(!bounty.TryAccept(SkyIslandBountyKind.Threats, out message), "only one contract at a time");
        Check(bounty.Target == SkyIslandBounty.BaseSalvageTarget, "first round uses the base target");

        for (int i = 0; i < bounty.Target - 1; i++) bounty.ReportScavenged();
        Check(!bounty.IsComplete, "incomplete contract is not claimable");
        Check(!bounty.TryClaim(t => true, out _, out message), "claiming early fails");
        bounty.ReportScavenged();
        Check(bounty.IsComplete && bounty.Progress == bounty.Target, "contract completes exactly at target");
        bounty.ReportScavenged();
        Check(bounty.Progress == bounty.Target, "progress is clamped at the target");

        SkyIslandLootTier reward;
        // 送达失败时委托必须原样保留：不能出现「单没了、谢礼也没有」。
        int deliveries = 0;
        Check(!bounty.TryClaim(t => { deliveries++; return false; }, out reward, out message),
            "failed delivery does not consume the contract");
        Check(deliveries == 1 && bounty.HasActive && bounty.CompletedRounds == 0,
            "contract survives a failed delivery");
        Check(reward == bounty.PendingReward, "reported reward matches the pending tier");
        Check(bounty.TryClaim(t => true, out reward, out message), "completed contract is claimable");
        Check(reward == SkyIslandLootTier.Voyage, "early rounds pay voyage tier");
        Check(!bounty.HasActive && bounty.CompletedRounds == 1, "claiming clears the contract");
        Check(bounty.Active == SkyIslandBountyKind.None, "claiming clears the recorded kind");

        // 重复可做但不是无痛刷：目标逐单变重，第三单起奖励升档。
        Check(bounty.TryAccept(SkyIslandBountyKind.Threats, out message), "second contract accepted");
        Check(bounty.Target == SkyIslandBounty.BaseThreatTarget + 1, "targets escalate per completed round");
        for (int i = 0; i < bounty.Target; i++) bounty.ReportEncounterCleared();
        Check(bounty.TryClaim(t => true, out reward, out message) && reward == SkyIslandLootTier.Voyage, "second round still voyage");

        Check(bounty.TryAccept(SkyIslandBountyKind.Survey, out message), "third contract accepted");
        for (int i = 0; i < bounty.Target; i++) bounty.ReportRegionVisited();
        Check(bounty.TryClaim(t => true, out reward, out message), "third contract claimed");
        Check(reward == SkyIslandLootTier.Starworks, "third round escalates to starworks");
        Check(bounty.CompletedRounds == SkyIslandBounty.StarworksFromRound, "round counter tracks completions");

        // 单次出击的派单上限：交满就收摊。目标只线性增长而保底顶档恒定，没有上限就是可刷的。
        Check(!bounty.CanAcceptMore, "the raid's contract quota is used up");
        Check(!bounty.TryAccept(SkyIslandBountyKind.Salvage, out message), "no fourth contract in one raid");

        // 退单：接了才发现做不完时的唯一出口。
        // 清场与巡岛的计数源都是持久存档事实（遭遇清一次永久记下、区域走过永久记下），
        // 老档上可能一件都不剩；没有退单就等于把本局委托槽永久卡死。
        var stuck = new SkyIslandBounty();
        Check(!stuck.TryAbandon(out message), "abandoning without a contract fails");
        Check(stuck.TryAccept(SkyIslandBountyKind.Survey, out message), "contract accepted before abandoning");
        stuck.ReportRegionVisited();
        Check(stuck.Progress == 1, "partial progress is recorded before abandoning");
        Check(stuck.TryAbandon(out message), "an accepted contract can be abandoned");
        Check(!stuck.HasActive && stuck.Active == SkyIslandBountyKind.None, "abandoning frees the slot");
        Check(stuck.CompletedRounds == 0, "abandoning does not count as a completed round");
        Check(stuck.Progress == 0, "abandoning clears the reported progress");
        Check(stuck.TryAccept(SkyIslandBountyKind.Salvage, out message), "another kind can be taken after abandoning");
        Check(stuck.Target == SkyIslandBounty.BaseSalvageTarget, "abandoning does not inflate the next target");

        // 退掉再接同一类：基线重取，退单前做过的量不会白送进度。
        var rebase = new SkyIslandBounty();
        rebase.TryAccept(SkyIslandBountyKind.Survey, out message);
        rebase.ReportRegionVisited();
        rebase.TryAbandon(out message);
        rebase.TryAccept(SkyIslandBountyKind.Survey, out message);
        Check(rebase.Progress == 0, "re-accepting the same kind rebaselines progress");

        // 四类委托各自记账，不会互相顶替（驱蚋是内容批次四接进来的第四类）。
        var isolated = new SkyIslandBounty();
        isolated.TryAccept(SkyIslandBountyKind.Threats, out message);
        isolated.ReportScavenged();
        isolated.ReportRegionVisited();
        isolated.ReportGnatCulled();
        Check(isolated.Progress == 0, "unrelated actions do not advance a contract");
        isolated.ReportEncounterCleared();
        Check(isolated.Progress == 1, "matching action advances the contract");
        Check(!isolated.TryAccept(SkyIslandBountyKind.None, out message), "the empty kind is not a contract");
        Check(SkyIslandBounty.AllKinds.Length == 4, "four contract kinds are offered");
        // 每一类都要有正的目标数，否则派单面板会挂出一张 ×0 的单。
        for (int i = 0; i < SkyIslandBounty.AllKinds.Length; i++)
            Check(isolated.TargetFor(SkyIslandBounty.AllKinds[i]) > 0,
                "every contract kind has a positive target: " + SkyIslandBounty.AllKinds[i]);
    }

    /// <summary>
    /// 品质加权抽样（`CR-2026-09-12-019`）。池子是「按品质带筛出来的 TypeID 清单」，旧写法在清单上
    /// **均匀抽**，于是一档被抽中的概率正比于这一档有多少种物品——而实机反解出来的表里
    /// （q1=77 · q2–3=277 · q4–5=174 · q6–8=204）**星工带高半段的种类比低半段还多**，
    /// 均匀抽比原版更肥。这里钉住权重函数本身的性质，不依赖官方物品表。
    /// </summary>
    private static void QualityWeights()
    {
        int scale = SkyIslandLootTables.QualityWeightScale;
        Check(scale > 0, "weight scale is positive");
        Check(SkyIslandLootTables.QualityFalloffPerStep > 0
              && SkyIslandLootTables.QualityFalloffPerStep < 1,
              "falloff must be a real decay: 0 < r < 1");

        foreach (SkyIslandLootTier tier in new[] { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage,
                                                   SkyIslandLootTier.Starworks })
        {
            int min = SkyIslandLootTables.MinQuality(tier);
            int max = SkyIslandLootTables.MaxQuality(tier);
            Check(SkyIslandLootTables.QualityWeight(min, min) == scale, "band floor carries the full weight: " + tier);
            for (int q = min; q < max; q++)
            {
                int here = SkyIslandLootTables.QualityWeight(q, min);
                int next = SkyIslandLootTables.QualityWeight(q + 1, min);
                // 严格递减：这是「加权比均匀更接近原版稀有度」的唯一必要条件，
                // 而且与官方物品表无关——不管每一档有多少种物品，高档的单件机会一定比低档小。
                Check(next < here, "weight strictly decreases with quality in " + tier + ": q" + (q + 1));
                // 但永远不为零：任何一档都不能被彻底抽空，否则顶档物品在天空岛永远刷不出来。
                Check(next > 0, "no quality is ever weighted to zero: " + tier + " q" + (q + 1));
            }
            // 保底带在**自己的**带里重新递减，而不是因为整体档位高就被压成一条平线。
            int guaranteeMin = SkyIslandLootTables.GuaranteeMinQuality(tier);
            if (guaranteeMin > 0)
                Check(SkyIslandLootTables.QualityWeight(guaranteeMin, guaranteeMin) == scale,
                      "guarantee band restarts the falloff at its own floor: " + tier);
        }

        // 低于本带下界的品质不该出现；真出现时按满权重处理，不能返回 0 或负数。
        Check(SkyIslandLootTables.QualityWeight(1, 4) == scale, "below-floor quality clamps to full weight");

        // 逐档复算一次具体数值，防止 0.6 被悄悄改掉而上面的单调断言仍然成立。
        int[] expected = { 10000, 6000, 3600, 2160, 1296 };
        for (int i = 0; i < expected.Length; i++)
            Check(SkyIslandLootTables.QualityWeight(4 + i, 4) == expected[i],
                  "starworks weight table is frozen at q" + (4 + i));
    }

    private static int Main()
    {
        try
        {
            AnchorCoverage();
            QualityBands();
            QualityWeights();
            SeededStreams();
            SlotSplit();
            CategoryDeal();
            BountyFlow();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
        Console.WriteLine("PASS SkyIslandLoot (" + checks + " checks)");
        return 0;
    }
}

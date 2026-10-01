using System;

namespace BossRush
{
    /// <summary>物资档次：按区域危险度递增，决定品质带、件数与牌面文案。</summary>
    internal enum SkyIslandLootTier
    {
        /// <summary>生活物资：码头、集市、梯田这类安全区。</summary>
        Supply = 0,
        /// <summary>航务补给：需要穿过遭遇点才能到的中段区域与支路。</summary>
        Voyage = 1,
        /// <summary>星工遗存：工坊、瞭台、钟庭这类最深处。</summary>
        Starworks = 2
    }

    /// <summary>
    /// 搜刮箱里一格官方物品的类别（owner 2026-10-01：箱里要有物资、武器、子弹、装备）。
    /// 类别只决定按哪组官方 Tag 建池（<c>SkyIslandLootPools.GetCategoryBand</c>），品质带、加权、价值上限与黑名单口径不变；
    /// 类别池为空时退回本档通用池（<see cref="General"/>），绝不因此出空格。
    /// </summary>
    internal enum SkyIslandLootCategory
    {
        /// <summary>本档通用池：全部官方 Tag 的并集，也就是改版前每一格的来源。</summary>
        General = 0,
        /// <summary>武器：Gun + MeleeWeapon。</summary>
        Weapon = 1,
        /// <summary>子弹：Bullet，按堆给。</summary>
        Ammo = 2,
        /// <summary>装备：护甲、头盔、背包、面罩、耳机。</summary>
        Gear = 3,
        /// <summary>物资：医疗、针剂、食物。</summary>
        Supplies = 4
    }

    /// <summary>一个搜刮点的稳定身份：锚点标记 + 极坐标偏移 + 档次。落点由运行时地面检查最终决定。</summary>
    internal sealed class SkyIslandLootAnchor
    {
        internal string Id;
        internal string Marker;
        internal float Bearing;
        internal float Distance;
        internal SkyIslandLootTier Tier;
        internal string Region;
    }

    /// <summary>
    /// COMPAT：天空岛搜刮点内容表。纯逻辑、无 Unity 依赖，可被隔离回归直接执行。
    ///
    /// 冻结口径：
    /// - 搜刮点只用**已有作者标记**做锚点，不需要新的场景资源；偏移由运行时地面/墙体/占位检查裁决。
    /// - 档次只决定品质带与件数，**不引入新 TypeID**；池子来自官方 `ItemAssetsCollection`。
    /// - 每次出击重新 roll，不进存档：这是出击图的常规刷新口径，也避免为纯消耗性内容扩存档 schema。
    /// </summary>
    internal static class SkyIslandLootTables
    {
        /// <summary>候选落点与已有玩法标记的最小间距，避免与搜索点/居民/撤离圈抢同一次交互。</summary>
        internal const float MarkerClearance = 4.5f;
        /// <summary>玩家进入这个距离才真正建箱（4.12 门控：不在整图预生成）。</summary>
        internal const float ActivationRange = 72f;

        /// <summary>
        /// 岛上物资池里单件物品的价值上限（CR-2026-09-11-001）。品质带内按物品**种类**均匀抽（原版按 RandomContainer 权重），
        /// 带里混进官方「皇冠」（21,593,218）这种收藏品时，一趟约 30 次星工抽取就有约 15% 抽到一顶，一件把整张图的经济抹平；
        /// 两把「神秘钥匙」（15–25 万）同理。上限 10 万约为星工遗存均值的十几倍：蓝图、纯金徽章这类稀有大货仍在池里，照样有惊喜。
        /// </summary>
        internal const int MaxPoolItemValue = 100000;

        /// <summary>这件物品的官方价值能不能进岛上的物资池。</summary>
        internal static bool AllowedInPool(int value) { return value <= MaxPoolItemValue; }

        /// <summary>
        /// 品质带内每升一档，被抽中的机会乘上这个系数（`CR-2026-09-12-019`）。
        ///
        /// **为什么需要它**：池子是「按品质带筛出来的 TypeID 清单」，旧写法在清单上**均匀抽**，
        /// 于是一档被抽中的概率正比于**这一档有多少种物品**，而不是它该有多稀有。
        /// 2026-09-12 从实机 `SKY_LOOT_BANDS` 的池子大小反解出这张表（算术自洽、可交叉验证）：
        ///
        /// <code>
        ///   q1 = 77 · q2–3 = 277 · q4–5 = 174 · q6–8 = 204
        /// </code>
        ///
        /// 也就是说**星工遗存带里高半段（q6–8，204 种）的物品种类比低半段（q4–5，174 种）还多**——
        /// 均匀抽不是「像原版那样随机」，而是**明显比原版更肥**：一趟满搜期望约 91 件、
        /// 其中约 16 件落在 q6–8，还没算三个委托谢礼箱与噬风战利品。
        /// 这与设计自己写下的顾虑正好相反（<see cref="GuaranteeMinQuality"/> 的注释：
        /// 「地上捡到的搜刮箱保持全随机——否则十个星工遗存箱每个保底一件高品质，一趟就发烂了」）。
        ///
        /// 0.6 的口径：每升一档机会约降四成，八档跨度合计约 36 倍。按同一份反解估算，
        /// 星工带里 q6–8 的占比从约 54% 降到约 26%，一趟满搜的 q6–8 期望从约 16 件降到约 8 件。
        /// **回退办法**：把 <see cref="QualityWeight"/> 改成恒返回 <see cref="QualityWeightScale"/>
        /// 即退回旧的均匀抽，池子构成与其它一切不变。
        /// </summary>
        internal const double QualityFalloffPerStep = 0.6;

        /// <summary>权重基数。用整数权重是为了让抽样在不同机器上逐位一致（浮点累加不保证）。</summary>
        internal const int QualityWeightScale = 10000;

        /// <summary>
        /// 一件 <paramref name="quality"/> 档物品在**以 <paramref name="minQuality"/> 为下界的品质带里**的抽样权重。
        ///
        /// 相对本带下界算，而不是相对绝对品质 1：保底带（星工 q6–8）也该在自己的带里递减，
        /// 而不是因为整体档位高就被压成一条平线。
        /// 权重恒为正（八档跨度下最小仍有 280），所以**不会有任何一档被彻底抽空**。
        /// 纯算术、无 Unity 依赖，隔离回归直接执行。
        /// </summary>
        internal static int QualityWeight(int quality, int minQuality)
        {
            int step = quality - minQuality;
            if (step <= 0) return QualityWeightScale;
            double weight = QualityWeightScale;
            for (int i = 0; i < step; i++) weight *= QualityFalloffPerStep;
            int rounded = (int)(weight + 0.5);
            return rounded < 1 ? 1 : rounded;
        }
        /// <summary>单次 Tick 最多建一个箱子，避免入区瞬间集中开销。</summary>
        internal const float TickInterval = 0.5f;

        private static readonly SkyIslandLootAnchor[] anchors = Build();

        internal static SkyIslandLootAnchor[] Anchors { get { return anchors; } }

        /// <summary>品质带下限。上限见 <see cref="MaxQuality"/>；两者都不做官方 Search 的静默降级。</summary>
        internal static int MinQuality(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 4;
            if (tier == SkyIslandLootTier.Voyage) return 2;
            return 1;
        }

        /// <summary>
        /// 品质带上限。游戏一共有 8 档（既有 Boss 奖池就按 1-8 取），
        /// 因此最深处的星工遗存必须能够到第 8 档，否则顶档物品在天空岛永远刷不出来。
        /// </summary>
        internal static int MaxQuality(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 8;
            if (tier == SkyIslandLootTier.Voyage) return 5;
            return 3;
        }

        /// <summary>
        /// 搜刮箱的格子数下限/上限（一格 = 一件或一堆；含保底的那一格岛上特产）。
        ///
        /// owner 2026-10-01 实机反馈旧表（生活物资 1–2 件、航务 2–3、星工 2–4）太空：码头开出来常常只有一件。
        /// 改为每箱 3–7 格：生活物资 3–4、航务补给 4–6、星工遗存 5–7；富裕岛区（<see cref="IsRichRegion"/>）再往上偏一格，
        /// 整体夹在 [<see cref="CrateMinSlots"/>, <see cref="CrateMaxSlots"/>]，箱子容量 12 格放得下。
        ///
        /// **必须随档次单调不减**：旧表里航务补给曾是 2–4、星工遗存反而只有 2–3，
        /// 中段区域比全图最深处出得还多，与品质带的递增方向相反。
        /// 只管地上捡到的搜刮箱；Boss 战利品与委托谢礼的件数由各自调用方给，不读这张表。
        /// </summary>
        internal static int MinCount(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 5;
            if (tier == SkyIslandLootTier.Voyage) return 4;
            return 3;
        }
        internal static int MaxCount(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 7;
            if (tier == SkyIslandLootTier.Voyage) return 6;
            return 4;
        }

        /// <summary>任何一只搜刮箱的格子数下限（owner 2026-10-01）。</summary>
        internal const int CrateMinSlots = 3;
        /// <summary>任何一只搜刮箱的格子数上限（owner 2026-10-01），远小于箱子容量 12。</summary>
        internal const int CrateMaxSlots = 7;
        /// <summary>富裕岛区的格子数加成。</summary>
        internal const int RichRegionBonus = 1;

        /// <summary>
        /// 富裕岛区：镜水寺 F、残星工坊 G、归航钟庭 H 与两条深处支路 S3 / S4。
        /// 与巡守按岛区分级（`Assets/Data/SkyIsland/Patrols.json` 的 rank ≥ 6）是同一批，守卫交叉核对。
        /// </summary>
        internal static bool IsRichRegion(string region)
        {
            switch (region)
            {
                case "F":
                case "G":
                case "H":
                case "S3":
                case "S4":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 一只搜刮箱的官方物品格数：总格数里先扣掉保底的一格特产；额外特产（<c>SkyIslandItemRules.IslandExtraFor</c>）
        /// 照旧追加一格、不占原有格数，只有总格数已经顶到 <see cref="CrateMaxSlots"/> 时才顶掉一格官方物品。
        /// 于是总格数 = 1 + 返回值 + (额外特产 ? 1 : 0)，恒在 [<see cref="CrateMinSlots"/>, <see cref="CrateMaxSlots"/>]。
        /// </summary>
        internal static int OfficialSlots(int slots, bool islandExtra)
        {
            int official = slots - 1;
            if (islandExtra && slots >= CrateMaxSlots) official--;
            return official < 0 ? 0 : official;
        }

        /// <summary>
        /// 每档一副 6 张的类别牌：一只箱子的官方物品格从洗过的牌里依次发（不放回），超过 6 格的部分补通用池。
        /// 生活物资偏吃的用的，航务补给偏子弹，星工遗存偏装备；每副都有武器、子弹、装备、物资与一张通用（留给蓝图、金饰这类惊喜）。
        /// 不放回发牌让同一类在一只箱里出现的次数不超过它的张数，满格的箱子五类都见得到。
        /// </summary>
        internal static SkyIslandLootCategory[] CategoryDeck(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks)
                return new[] { SkyIslandLootCategory.Supplies, SkyIslandLootCategory.Ammo, SkyIslandLootCategory.Weapon,
                    SkyIslandLootCategory.Gear, SkyIslandLootCategory.Gear, SkyIslandLootCategory.General };
            if (tier == SkyIslandLootTier.Voyage)
                return new[] { SkyIslandLootCategory.Supplies, SkyIslandLootCategory.Ammo, SkyIslandLootCategory.Ammo,
                    SkyIslandLootCategory.Weapon, SkyIslandLootCategory.Gear, SkyIslandLootCategory.General };
            return new[] { SkyIslandLootCategory.Supplies, SkyIslandLootCategory.Supplies, SkyIslandLootCategory.Ammo,
                SkyIslandLootCategory.Weapon, SkyIslandLootCategory.Gear, SkyIslandLootCategory.General };
        }

        /// <summary>按本档牌堆给 <paramref name="slots"/> 格发类别（部分 Fisher–Yates，不放回）；同一条随机流结果固定。</summary>
        internal static SkyIslandLootCategory[] DealCategories(SkyIslandLootTier tier, int slots, Random random)
        {
            if (slots <= 0) return new SkyIslandLootCategory[0];
            SkyIslandLootCategory[] deck = CategoryDeck(tier);
            var dealt = new SkyIslandLootCategory[slots];
            for (int i = 0; i < slots; i++)
            {
                if (i >= deck.Length) { dealt[i] = SkyIslandLootCategory.General; continue; }
                int j = i + random.Next(deck.Length - i);
                SkyIslandLootCategory card = deck[j];
                deck[j] = deck[i];
                deck[i] = card;
                dealt[i] = card;
            }
            return dealt;
        }

        /// <summary>一格子弹给多少发（一格一堆，实际再被这种子弹的官方堆叠上限截住）。深处给得多。</summary>
        internal static int AmmoStackMin(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 30;
            if (tier == SkyIslandLootTier.Voyage) return 20;
            return 15;
        }
        internal static int AmmoStackMax(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 60;
            if (tier == SkyIslandLootTier.Voyage) return 40;
            return 30;
        }

        internal static int RollAmmoStack(SkyIslandLootTier tier, Random random)
        {
            int min = AmmoStackMin(tier), max = AmmoStackMax(tier);
            return min + random.Next(max - min + 1);
        }

        /// <summary>
        /// 保底品质下限：**只作用于「赚来的」奖励**（Boss 战利品、委托谢礼），
        /// 地上捡到的搜刮箱保持全随机——否则十个星工遗存箱每个保底一件高品质，一趟就发烂了。
        /// 返回 0 表示该档没有保底。
        /// </summary>
        internal static int GuaranteeMinQuality(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 6;
            if (tier == SkyIslandLootTier.Voyage) return 4;
            return 0;
        }

        /// <summary>
        /// 进岛装配时要预热的品质带（CR-2026-09-14-014）：每一档的常规带，加上有保底的档次的保底带——
        /// 正好是 <c>SkyIslandLootPools.Get</c> / <c>Pick</c> / <c>GetGuaranteeBand</c> 会去查的全部带。去重，按档次顺序。
        /// 纯算术，隔离回归逐项核对。
        /// </summary>
        internal static int[][] PrewarmBands()
        {
            var bands = new System.Collections.Generic.List<int[]>(6);
            SkyIslandLootTier[] tiers = { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks };
            for (int i = 0; i < tiers.Length; i++)
            {
                AddBand(bands, MinQuality(tiers[i]), MaxQuality(tiers[i]));
                int guarantee = GuaranteeMinQuality(tiers[i]);
                if (guarantee > 0) AddBand(bands, guarantee, MaxQuality(tiers[i]));
            }
            return bands.ToArray();
        }

        /// <summary>搜刮箱按类别抽的四个类别（通用池就是 <see cref="PrewarmBands"/> 里的常规带，不重复列）。</summary>
        internal static readonly SkyIslandLootCategory[] PoolCategories =
        {
            SkyIslandLootCategory.Weapon, SkyIslandLootCategory.Ammo, SkyIslandLootCategory.Gear, SkyIslandLootCategory.Supplies
        };

        /// <summary>
        /// 进岛装配时要预热的类别池：每一档常规带 × <see cref="PoolCategories"/>，每项是 {类别, 下限, 上限}。
        /// 正好是 <c>SkyIslandLootPools.PickCategory</c> 会去查的全部类别池。纯算术，隔离回归逐项核对。
        /// </summary>
        internal static int[][] PrewarmCategoryBands()
        {
            var bands = new System.Collections.Generic.List<int[]>(12);
            SkyIslandLootTier[] tiers = { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks };
            for (int i = 0; i < tiers.Length; i++)
                for (int c = 0; c < PoolCategories.Length; c++)
                    bands.Add(new[] { (int)PoolCategories[c], MinQuality(tiers[i]), MaxQuality(tiers[i]) });
            return bands.ToArray();
        }

        private static void AddBand(System.Collections.Generic.List<int[]> bands, int min, int max)
        {
            for (int i = 0; i < bands.Count; i++)
                if (bands[i][0] == min && bands[i][1] == max) return;
            bands.Add(new[] { min, max });
        }

        internal static string TierNameCn(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return "星工遗存";
            if (tier == SkyIslandLootTier.Voyage) return "航务补给";
            return "生活物资";
        }

        internal static string TierNameEn(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return "Starworks Cache";
            if (tier == SkyIslandLootTier.Voyage) return "Voyage Supplies";
            return "Household Stores";
        }

        /// <summary>
        /// 按散列方位摆放的灶火与完成纪念物，有几处散列方位正好落在路面上（2026-09-30 场景审计：
        /// 标记本身就在路中间，六个试探方位里第一个站得住的就在路上）。这几处改用离线复算选定的方位与距离；
        /// 键就是原来喂给 <see cref="StableHash"/> 的字符串，不在表里的照旧取散列方位与调用方给的距离。
        /// 复算与守卫：`tools/sky_island_runtime_placements.py`、`tests/SkyIslandSceneCleanlinessPropertyTest.py`。
        /// </summary>
        internal static void PlacementFor(string key, float defaultDistance, out float bearing, out float distance)
        {
            switch (key)
            {
                case "Search_A:hearth": bearing = 25f; distance = 3.9f; return;
                case "Search_D": bearing = 9f; distance = 3.7f; return;
                case "EnemySpawn_F": bearing = 146f; distance = 3.2f; return;
                // 信鸽 Letter_02 的散列方位落在风铃架（Tripo 件比登记占地长）上。
                case "Letter_02": bearing = 37f; distance = 3.2f; return;
            }
            bearing = StableHash(key) % 360;
            distance = defaultDistance;
        }

        /// <summary>Mono 与 .NET Core 的 string.GetHashCode 口径不同，抽样必须用自带的稳定散列。</summary>
        internal static int StableHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
                return (int)(hash & 0x7fffffff);
            }
        }

        /// <summary>同一次出击、同一个搜刮点永远得到同一份内容，避免离开再回来刷出第二份。</summary>
        internal static Random CreateStream(int raidSeed, string anchorId)
        {
            return new Random(unchecked(raidSeed * 486187739 + StableHash(anchorId)));
        }

        /// <summary>一只搜刮箱的总格数：本档区间整体按岛区富裕程度上移，再夹进 [<see cref="CrateMinSlots"/>, <see cref="CrateMaxSlots"/>] 后均匀抽。</summary>
        internal static int RollCount(SkyIslandLootTier tier, string region, Random random)
        {
            int bonus = IsRichRegion(region) ? RichRegionBonus : 0;
            int min = ClampSlots(MinCount(tier) + bonus), max = ClampSlots(MaxCount(tier) + bonus);
            return min + random.Next(max - min + 1);
        }

        private static int ClampSlots(int slots)
        {
            if (slots < CrateMinSlots) return CrateMinSlots;
            return slots > CrateMaxSlots ? CrateMaxSlots : slots;
        }

        /// <summary>档次统计：给验收和守卫用，确认每个区域都有产出且深处更值钱。</summary>
        internal static int CountOfTier(SkyIslandLootTier tier)
        {
            int count = 0;
            for (int i = 0; i < anchors.Length; i++) if (anchors[i].Tier == tier) count++;
            return count;
        }

        internal static int CountOfRegion(string region)
        {
            int count = 0;
            for (int i = 0; i < anchors.Length; i++)
                if (string.Equals(anchors[i].Region, region, StringComparison.Ordinal)) count++;
            return count;
        }

        private static SkyIslandLootAnchor[] Build()
        {
            return new[]
            {
                // A 登云码头：出发与主撤离，最安全，只放生活物资。
                Anchor("A1", "Lamp_A", 35f, 6f, SkyIslandLootTier.Supply, "A"),
                Anchor("A2", "Lamp_A_02", 215f, 6f, SkyIslandLootTier.Supply, "A"),
                Anchor("A3", "EnemySpawn_A", 120f, 7f, SkyIslandLootTier.Supply, "A"),
                // B 风铃集：枢纽，密度最高，出发前能在这里凑齐消耗品。
                // 2026-09-30 场景审计：B1 / B2 / B4 原方位落在路面上，B3 贴着茶水灶；按离线复算挪到路边空地。
                Anchor("B1", "Lamp_B", 145f, 6.5f, SkyIslandLootTier.Supply, "B"),
                Anchor("B2", "Lamp_B_02", 270f, 4f, SkyIslandLootTier.Supply, "B"),
                Anchor("B3", "Search_B", 255f, 7f, SkyIslandLootTier.Supply, "B"),
                Anchor("B4", "Search_B_02", 50f, 8f, SkyIslandLootTier.Supply, "B"),
                // C 青穗梯田：西线第一段，仍是生活物资。
                Anchor("C1", "Lamp_C", 60f, 6f, SkyIslandLootTier.Supply, "C"),
                Anchor("C2", "Lamp_C_02", 240f, 6f, SkyIslandLootTier.Supply, "C"),
                Anchor("C3", "POI_C", 150f, 7f, SkyIslandLootTier.Supply, "C"),
                Anchor("C4", "Search_C", 200f, 7f, SkyIslandLootTier.Supply, "C"),
                // D 悬根林：西航标所在，两组遭遇，升到航务补给。
                Anchor("D1", "Lamp_D", 20f, 6f, SkyIslandLootTier.Voyage, "D"),
                Anchor("D2", "Lamp_D_02", 200f, 6f, SkyIslandLootTier.Voyage, "D"),
                // 布局 v2：D3 / G3 / E3 挪到回程捷径 K1 / K2 / K3 的中继平台，横向偏 7 米仍在平台上；区域与档次不变。
                Anchor("D3", "Relay_K1", 30f, 7f, SkyIslandLootTier.Voyage, "D"),
                Anchor("D4", "Search_D", 250f, 8f, SkyIslandLootTier.Voyage, "D"),
                // E 鸣风栈道：两线汇合，也是噬风的挑战地点。
                Anchor("E1", "Lamp_E", 75f, 6f, SkyIslandLootTier.Voyage, "E"),
                Anchor("E2", "Lamp_E_02", 255f, 6f, SkyIslandLootTier.Voyage, "E"),
                Anchor("E3", "Relay_K3", 0f, 7f, SkyIslandLootTier.Voyage, "E"),
                Anchor("E4", "Search_E", 300f, 7f, SkyIslandLootTier.Voyage, "E"),
                // F 镜水寺：东线第一段。
                Anchor("F1", "Lamp_F", 45f, 6f, SkyIslandLootTier.Voyage, "F"),
                Anchor("F2", "Lamp_F_02", 225f, 6f, SkyIslandLootTier.Voyage, "F"),
                // 2026-09-30 场景审计：F3 / F4 原方位压到路缘。
                Anchor("F3", "POI_F", 140f, 7f, SkyIslandLootTier.Voyage, "F"),
                Anchor("F4", "Search_F", 300f, 7f, SkyIslandLootTier.Voyage, "F"),
                // G 残星工坊：东航标所在，全图最深的常规区域。
                Anchor("G1", "Lamp_G", 30f, 5.5f, SkyIslandLootTier.Starworks, "G"),
                Anchor("G2", "Lamp_G_02", 210f, 6f, SkyIslandLootTier.Starworks, "G"),
                Anchor("G3", "Relay_K2", 301f, 7f, SkyIslandLootTier.Starworks, "G"),
                Anchor("G4", "Search_G", 250f, 7f, SkyIslandLootTier.Starworks, "G"),
                // H 归航钟庭：双航标门之后才进得来，回报最高。
                Anchor("H1", "Lamp_H", 50f, 6f, SkyIslandLootTier.Starworks, "H"),
                Anchor("H2", "Lamp_H_02", 230f, 6f, SkyIslandLootTier.Starworks, "H"),
                Anchor("H3", "Lamp_H", 280f, 9f, SkyIslandLootTier.Starworks, "H"),
                Anchor("H4", "Search_H_02", 200f, 7f, SkyIslandLootTier.Starworks, "H"),
                // 四条支路：绕路的人应当拿到额外回报，S4 与工坊同档。
                // 2026-09-30 场景审计：S1a 原落点贴着岛缘护栏，S3b 原方位在路上。
                Anchor("S1a", "POI_S1", 100f, 7.5f, SkyIslandLootTier.Voyage, "S1"),
                Anchor("S1b", "Search_S1", 280f, 6f, SkyIslandLootTier.Voyage, "S1"),
                Anchor("S2a", "POI_S2", 100f, 7f, SkyIslandLootTier.Voyage, "S2"),
                Anchor("S2b", "Search_S2", 280f, 6f, SkyIslandLootTier.Voyage, "S2"),
                Anchor("S3a", "POI_S3", 100f, 7f, SkyIslandLootTier.Voyage, "S3"),
                Anchor("S3b", "Search_S3", 255f, 6f, SkyIslandLootTier.Voyage, "S3"),
                Anchor("S4a", "POI_S4", 100f, 7f, SkyIslandLootTier.Starworks, "S4"),
                Anchor("S4b", "Search_S4", 280f, 6f, SkyIslandLootTier.Starworks, "S4")
            };
        }

        private static SkyIslandLootAnchor Anchor(string id, string marker, float bearing, float distance,
            SkyIslandLootTier tier, string region)
        {
            return new SkyIslandLootAnchor
            {
                Id = id, Marker = marker, Bearing = bearing, Distance = distance, Tier = tier, Region = region
            };
        }
    }
}

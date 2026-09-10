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
        /// 件数下限/上限。上限压在 4 件，配合 12 敌上限控制单区峰值。
        ///
        /// **必须随档次单调不减**：旧表里航务补给是 2–4、星工遗存反而只有 2–3，
        /// 中段区域比全图最深处出得还多，与品质带的递增方向相反。
        /// 生活物资同时下调到 1–2：码头与集市本来就是安全区，不该在最安全的地方给最多的量。
        /// </summary>
        internal static int MinCount(SkyIslandLootTier tier)
        {
            return tier == SkyIslandLootTier.Supply ? 1 : 2;
        }
        internal static int MaxCount(SkyIslandLootTier tier)
        {
            if (tier == SkyIslandLootTier.Starworks) return 4;
            if (tier == SkyIslandLootTier.Voyage) return 3;
            return 2;
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

        internal static int RollCount(SkyIslandLootTier tier, Random random)
        {
            int min = MinCount(tier), max = MaxCount(tier);
            return min + random.Next(max - min + 1);
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
                Anchor("B1", "Lamp_B", 120f, 6f, SkyIslandLootTier.Supply, "B"),
                Anchor("B2", "Lamp_B_02", 300f, 6f, SkyIslandLootTier.Supply, "B"),
                Anchor("B3", "Search_B", 250f, 7f, SkyIslandLootTier.Supply, "B"),
                Anchor("B4", "Search_B_02", 40f, 7f, SkyIslandLootTier.Supply, "B"),
                // C 青穗梯田：西线第一段，仍是生活物资。
                Anchor("C1", "Lamp_C", 60f, 6f, SkyIslandLootTier.Supply, "C"),
                Anchor("C2", "Lamp_C_02", 240f, 6f, SkyIslandLootTier.Supply, "C"),
                Anchor("C3", "POI_C", 150f, 7f, SkyIslandLootTier.Supply, "C"),
                Anchor("C4", "Search_C", 200f, 7f, SkyIslandLootTier.Supply, "C"),
                // D 悬根林：西航标所在，两组遭遇，升到航务补给。
                Anchor("D1", "Lamp_D", 20f, 6f, SkyIslandLootTier.Voyage, "D"),
                Anchor("D2", "Lamp_D_02", 200f, 6f, SkyIslandLootTier.Voyage, "D"),
                Anchor("D3", "POI_D", 110f, 7f, SkyIslandLootTier.Voyage, "D"),
                Anchor("D4", "Search_D", 250f, 8f, SkyIslandLootTier.Voyage, "D"),
                // E 鸣风栈道：两线汇合，也是噬风的挑战地点。
                Anchor("E1", "Lamp_E", 75f, 6f, SkyIslandLootTier.Voyage, "E"),
                Anchor("E2", "Lamp_E_02", 255f, 6f, SkyIslandLootTier.Voyage, "E"),
                Anchor("E3", "POI_E", 165f, 7f, SkyIslandLootTier.Voyage, "E"),
                Anchor("E4", "Search_E", 300f, 7f, SkyIslandLootTier.Voyage, "E"),
                // F 镜水寺：东线第一段。
                Anchor("F1", "Lamp_F", 45f, 6f, SkyIslandLootTier.Voyage, "F"),
                Anchor("F2", "Lamp_F_02", 225f, 6f, SkyIslandLootTier.Voyage, "F"),
                Anchor("F3", "POI_F", 135f, 7f, SkyIslandLootTier.Voyage, "F"),
                Anchor("F4", "Search_F", 315f, 7f, SkyIslandLootTier.Voyage, "F"),
                // G 残星工坊：东航标所在，全图最深的常规区域。
                Anchor("G1", "Lamp_G", 30f, 6f, SkyIslandLootTier.Starworks, "G"),
                Anchor("G2", "Lamp_G_02", 210f, 6f, SkyIslandLootTier.Starworks, "G"),
                Anchor("G3", "POI_G", 140f, 7f, SkyIslandLootTier.Starworks, "G"),
                Anchor("G4", "Search_G", 250f, 7f, SkyIslandLootTier.Starworks, "G"),
                // H 归航钟庭：双航标门之后才进得来，回报最高。
                Anchor("H1", "Lamp_H", 50f, 6f, SkyIslandLootTier.Starworks, "H"),
                Anchor("H2", "Lamp_H_02", 230f, 6f, SkyIslandLootTier.Starworks, "H"),
                Anchor("H3", "Lamp_H", 280f, 9f, SkyIslandLootTier.Starworks, "H"),
                Anchor("H4", "Search_H_02", 200f, 7f, SkyIslandLootTier.Starworks, "H"),
                // 四条支路：绕路的人应当拿到额外回报，S4 与工坊同档。
                Anchor("S1a", "POI_S1", 100f, 7f, SkyIslandLootTier.Voyage, "S1"),
                Anchor("S1b", "Search_S1", 280f, 6f, SkyIslandLootTier.Voyage, "S1"),
                Anchor("S2a", "POI_S2", 100f, 7f, SkyIslandLootTier.Voyage, "S2"),
                Anchor("S2b", "Search_S2", 280f, 6f, SkyIslandLootTier.Voyage, "S2"),
                Anchor("S3a", "POI_S3", 100f, 7f, SkyIslandLootTier.Voyage, "S3"),
                Anchor("S3b", "Search_S3", 280f, 6f, SkyIslandLootTier.Voyage, "S3"),
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

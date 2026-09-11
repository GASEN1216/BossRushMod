using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>采集点的五种外观与产出。</summary>
    internal enum SkyIslandGatherKind
    {
        /// <summary>青穗草丛：青穗草。</summary>
        Grass = 0,
        /// <summary>搁浅的浮木：浮木。</summary>
        Driftwood = 1,
        /// <summary>云苔：云苔纤维。</summary>
        Moss = 2,
        /// <summary>残铜矿脉：残铜片，深处偶尔带一片风晶碎片。</summary>
        Ore = 3,
        /// <summary>风晶簇：风晶碎片，深处与夜里带星屑。</summary>
        Crystal = 4
    }

    /// <summary>三处合成台：都挂在已有居民与装置上，居民不在时装置兜底。</summary>
    internal enum SkyIslandCraftStation
    {
        /// <summary>浮舟 · 渡口工台（码头装置 Search_A 兜底）。</summary>
        Dock = 0,
        /// <summary>晴禾 · 灶台（菜畦 Search_C 兜底）。</summary>
        Stove = 1,
        /// <summary>眠苔 · 药臼（悬根林见闻点 Search_D_02 兜底）。</summary>
        Mortar = 2
    }

    /// <summary>三种局内耗材的效果。</summary>
    internal enum SkyIslandFieldBuff
    {
        None = -1,
        /// <summary>风灯：照明，并在燃烧期间抵御寒意。</summary>
        Lantern = 0,
        /// <summary>驱风香：不受风寒，耐力恢复加快。</summary>
        Incense = 1,
        /// <summary>晴岚护符：本趟出击噬风的风暴伤害降低，生命上限与耐力恢复小幅提升，不叠加。</summary>
        Charm = 2,
        /// <summary>归航菜便当：菜畦重新开张之后在岛上吃，算作晴禾的归航菜（与她那一顿共用本趟一次）。</summary>
        Meal = 3,
        /// <summary>晴岚航徽：在岛上拉一下缆绳回到登云码头（每趟一次，不消耗）。</summary>
        Recall = 4
    }

    /// <summary>此刻挡着风的东西：决定寒意是积还是退。</summary>
    internal enum SkyIslandWarmth
    {
        /// <summary>什么都没有。</summary>
        None = 0,
        /// <summary>只有风灯：微风里暖和；大风里火苗被压低，寒意按大风的一半速率积。</summary>
        Lantern = 1,
        /// <summary>灶火或风晶灯旁、或焚着驱风香：什么风都挡得住。</summary>
        Shelter = 2
    }

    /// <summary>一个采集点的稳定身份：锚点标记 + 极坐标偏移 + 外观 + 档次。落点由运行时地面与墙体检查最终决定。</summary>
    internal sealed class SkyIslandGatherNode
    {
        internal string Id;
        internal string Marker;
        internal float Bearing;
        internal float Distance;
        internal SkyIslandGatherKind Kind;
        internal SkyIslandLootTier Tier;
        internal string Region;
    }

    /// <summary>一次产出：哪件物品、几件。</summary>
    internal struct SkyIslandYield
    {
        internal int TypeId;
        internal int Count;
        internal SkyIslandYield(int typeId, int count) { TypeId = typeId; Count = count; }
    }

    /// <summary>配方里的一种材料与数量。</summary>
    internal struct SkyIslandIngredient
    {
        internal int TypeId;
        internal int Count;
        internal SkyIslandIngredient(int typeId, int count) { TypeId = typeId; Count = count; }
    }

    /// <summary>一条配方：在哪做、用什么、做出什么。</summary>
    internal sealed class SkyIslandRecipe
    {
        internal string Id;
        internal SkyIslandCraftStation Station;
        internal int OutputTypeId;
        internal int OutputCount;
        internal SkyIslandIngredient[] Inputs;
        /// <summary>要先达成的剧情旗标；None 表示一开始就会做。</summary>
        internal SkyIslandStoryFlag RequiresFlag;
        /// <summary>要先记进本槽手记的条目；null 表示不需要。</summary>
        internal string RequiresNote;

        /// <summary>这条配方要等剧情走到 <paramref name="flag"/> 才会做（配方表里链式写在 Recipe(...) 之后）。</summary>
        internal SkyIslandRecipe After(SkyIslandStoryFlag flag) { RequiresFlag = flag; return this; }

        /// <summary>这条配方要等手记里有 <paramref name="noteId"/> 才会做。</summary>
        internal SkyIslandRecipe AfterNote(string noteId) { RequiresNote = noteId; return this; }
    }

    /// <summary>
    /// COMPAT：天空岛内容批次三「采集 → 攒材料 → 合成 / 烹饪 → 靠产物走得更远」的纯规则。
    ///
    /// 冻结口径：
    /// - **按出击刷新、不进存档**：采集点每趟每处只采一次，产出由本趟种子 + 采集点 id 决定；局内增益走游戏时间、离岛摘除。
    ///   不加存档字段、不加旗标位。
    /// - 采集点只挂在**已有作者标记**旁（不动场景几何），落点复用 `SkyIslandRewardCrate.TryFindCratePosition`；
    ///   与全岛其它静态交互体的净空由 `tests/SkyIslandInteractionCompetitionPropertyTest.py` 按真实几何复算。
    /// - 产出随区域危险度递增（与搜刮点共用 <see cref="SkyIslandLootTier"/>）；配方只读这张表，面板与回归共用。
    /// - 夜风只动耐力恢复与饥饿速度，**不掉血、不减跑速**：噬风的第一圈要求约 5 m/s 净逃离速度，减跑速会把那一关变成数值墙。
    /// - **串联**：每件东西都要有岛上的用处，不做只为卖钱的物品——剧情让群岛长回来（<see cref="StoryBonusCount"/>）、
    ///   配方随剧情解锁（<see cref="Unlocked"/>）、三件耗材各挡一层风（风灯挡微风、驱风香挡大风、护符挡噬风的风暴，
    ///   <see cref="StepExposure"/> / <see cref="StormPulseDamage"/>）、晴岚风晶是七盏风晶灯的灯芯（<see cref="SkyIslandLights"/>），
    ///   灯凑满十盏之后夜里不再起风（<see cref="NightWind"/>），带在身上的噬风之核让大风只算微风（<see cref="CoreEased"/>）。
    /// 纯逻辑、无 Unity 依赖，隔离回归（`tests/fixtures/SkyIslandStory`）直接执行。
    /// </summary>
    internal static class SkyIslandFieldcraftRules
    {
        #region 常量

        /// <summary>玩家进入这个距离才真正建采集交互体（AGENTS 4.12：不在进图时预生成整图）。</summary>
        internal const float ActivationRange = 60f;
        /// <summary>采集、增益与夜风的推进间隔（游戏秒）。一次推进最多建一个采集点。</summary>
        internal const float TickInterval = 0.5f;
        /// <summary>采集点触发盒的水平边长（米）。交互竞争属性测试读这个常量算半宽。</summary>
        internal const float NodeTriggerSize = 1.6f;

        /// <summary>寒意上限；积满即「风寒」。</summary>
        internal const float ExposureMax = 100f;
        /// <summary>寒意越过这一档时提醒一次。</summary>
        internal const float ExposureWarn = 60f;
        /// <summary>风寒退到这一档以下才解除（滞回，避免在阈值上反复开关）。</summary>
        internal const float ExposureClear = 40f;
        /// <summary>微风（夜里、白天的桥上）每游戏秒增加的寒意：约 143 秒积满。</summary>
        internal const float BreezeRate = 0.7f;
        /// <summary>大风（夜里的桥上、噬风将至时的栈道）每游戏秒增加的寒意：约 67 秒积满。</summary>
        internal const float GaleRate = 1.5f;
        /// <summary>无风时每游戏秒退去的寒意。</summary>
        internal const float CalmRecovery = 2f;
        /// <summary>暖和时（营火旁、风灯或驱风香燃着）每游戏秒退去的寒意。</summary>
        internal const float WarmRecovery = 4f;
        /// <summary>营火的取暖半径（米）。</summary>
        internal const float CampfireRadius = 7f;

        /// <summary>风灯燃烧的游戏秒数。</summary>
        internal const float LanternSeconds = 240f;
        /// <summary>驱风香燃烧的游戏秒数。</summary>
        internal const float IncenseSeconds = 300f;
        /// <summary>耗材快燃尽时提醒的剩余游戏秒数。</summary>
        internal const float BuffLowSeconds = 20f;

        /// <summary>风寒：耐力恢复 −25%、饥饿速度 +25%。</summary>
        internal const float ChillStaminaRecover = -0.25f;
        internal const float ChillEnergyCost = 0.25f;
        /// <summary>驱风香：耐力恢复 +15%。</summary>
        internal const float IncenseStaminaRecover = 0.15f;
        /// <summary>晴岚护符：生命上限 +10%、耐力恢复 +10%（本趟出击，不叠加）。</summary>
        internal const float CharmMaxHealth = 0.10f;
        internal const float CharmStaminaRecover = 0.10f;
        /// <summary>晴岚护符：本趟噬风风暴脉冲的伤害降低比例（噬风每一波读一次）。</summary>
        internal const float CharmStormWard = 0.35f;
        /// <summary>风灯在大风里只挡一半：火苗被压低，寒意按大风速率的这个比例积。</summary>
        internal const float LanternGaleFactor = 0.5f;
        /// <summary>多久数一次背包里有没有噬风之核（游戏秒）：放进拿出晚两秒生效无所谓，不必每次推进都数背包。</summary>
        internal const float CarryCheckInterval = 2f;
        /// <summary>观星镜校准之后，残星瞭台的风晶簇多出星屑的概率。</summary>
        internal const double TelescopeStardustBonus = 0.20;

        /// <summary>夜里（21 点到次日 5 点，与光照的「星夜」整档一致）风晶簇多出星屑的概率。</summary>
        internal const double NightStardustBonus = 0.15;

        #endregion

        #region 采集点

        private static readonly SkyIslandGatherNode[] nodes = BuildNodes();

        internal static SkyIslandGatherNode[] Nodes { get { return nodes; } }

        private static SkyIslandGatherNode[] BuildNodes()
        {
            // 落点全部离锚点 8 米、第一次尝试就站得住（真实几何离线复算），离其它交互体与撤离环最近也有 4 米以上。
            return new[]
            {
                // A 登云码头 / B 风铃集 / C 青穗梯田：安全区，产出最少。
                Node("A1", "Lamp_A", 90f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Supply, "A"),
                Node("A2", "Lamp_A_02", 0f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Supply, "A"),
                Node("B1", "Lamp_B", 0f, 8f, SkyIslandGatherKind.Grass, SkyIslandLootTier.Supply, "B"),
                Node("B2", "Lamp_B_02", 0f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Supply, "B"),
                Node("C1", "POI_C", 0f, 8f, SkyIslandGatherKind.Grass, SkyIslandLootTier.Supply, "C"),
                Node("C2", "Lamp_C", 0f, 8f, SkyIslandGatherKind.Grass, SkyIslandLootTier.Supply, "C"),
                Node("C3", "Lamp_C_02", 15f, 8f, SkyIslandGatherKind.Moss, SkyIslandLootTier.Supply, "C"),
                // D 悬根林 / E 鸣风栈道 / F 镜水寺 / 三座秘境：中段。
                Node("D1", "Lamp_D", 75f, 8f, SkyIslandGatherKind.Moss, SkyIslandLootTier.Voyage, "D"),
                Node("D2", "POI_D", 0f, 8f, SkyIslandGatherKind.Moss, SkyIslandLootTier.Voyage, "D"),
                Node("D3", "Lamp_D_02", 0f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Voyage, "D"),
                Node("E1", "Lamp_E", 0f, 8f, SkyIslandGatherKind.Crystal, SkyIslandLootTier.Voyage, "E"),
                Node("E2", "Lamp_E_02", 0f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Voyage, "E"),
                Node("F1", "Lamp_F", 150f, 8f, SkyIslandGatherKind.Moss, SkyIslandLootTier.Voyage, "F"),
                Node("F2", "Lamp_F_02", 0f, 8f, SkyIslandGatherKind.Grass, SkyIslandLootTier.Voyage, "F"),
                Node("S1a", "POI_S1", 0f, 8f, SkyIslandGatherKind.Grass, SkyIslandLootTier.Voyage, "S1"),
                Node("S1b", "Region_S1", 150f, 8f, SkyIslandGatherKind.Moss, SkyIslandLootTier.Voyage, "S1"),
                Node("S2a", "POI_S2", 0f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Voyage, "S2"),
                Node("S2b", "Region_S2", 0f, 8f, SkyIslandGatherKind.Moss, SkyIslandLootTier.Voyage, "S2"),
                Node("S3a", "POI_S3", 0f, 8f, SkyIslandGatherKind.Ore, SkyIslandLootTier.Voyage, "S3"),
                Node("S3b", "Region_S3", 0f, 8f, SkyIslandGatherKind.Crystal, SkyIslandLootTier.Voyage, "S3"),
                // 回程捷径的中继平台：区域与档次跟随搜刮点口径（K1 → D、K3 → E 航务，K2 → G 星工）。
                Node("K1", "Relay_K1", 75f, 8f, SkyIslandGatherKind.Driftwood, SkyIslandLootTier.Voyage, "D"),
                Node("K3", "Relay_K3", 45f, 8f, SkyIslandGatherKind.Crystal, SkyIslandLootTier.Voyage, "E"),
                // G 残星工坊 / H 归航钟庭 / S4 残星瞭台：最深处，矿脉与风晶簇集中在这里。
                Node("G1", "Lamp_G", 75f, 8f, SkyIslandGatherKind.Ore, SkyIslandLootTier.Starworks, "G"),
                Node("G2", "Lamp_G_02", 0f, 8f, SkyIslandGatherKind.Ore, SkyIslandLootTier.Starworks, "G"),
                Node("G3", "POI_G", 60f, 8f, SkyIslandGatherKind.Crystal, SkyIslandLootTier.Starworks, "G"),
                Node("K2", "Relay_K2", 0f, 8f, SkyIslandGatherKind.Ore, SkyIslandLootTier.Starworks, "G"),
                Node("H1", "Lamp_H", 0f, 8f, SkyIslandGatherKind.Crystal, SkyIslandLootTier.Starworks, "H"),
                Node("H2", "Lamp_H_02", 0f, 8f, SkyIslandGatherKind.Ore, SkyIslandLootTier.Starworks, "H"),
                Node("S4a", "POI_S4", 15f, 8f, SkyIslandGatherKind.Crystal, SkyIslandLootTier.Starworks, "S4"),
                Node("S4b", "Region_S4", 75f, 8f, SkyIslandGatherKind.Ore, SkyIslandLootTier.Starworks, "S4")
            };
        }

        internal static SkyIslandGatherNode FindNode(string id)
        {
            for (int i = 0; i < nodes.Length; i++)
                if (string.Equals(nodes[i].Id, id, StringComparison.Ordinal)) return nodes[i];
            return null;
        }

        /// <summary>采集读条的游戏秒数：越硬的东西采得越久。</summary>
        internal static float InteractSeconds(SkyIslandGatherKind kind)
        {
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: return 1.5f;
                case SkyIslandGatherKind.Moss: return 2f;
                case SkyIslandGatherKind.Driftwood: return 2.5f;
                case SkyIslandGatherKind.Crystal: return 3f;
                default: return 3.5f;
            }
        }

        /// <summary>主产出的件数范围：随档次单调不减（隔离回归逐档核对）。</summary>
        internal static void CountRange(SkyIslandGatherKind kind, SkyIslandLootTier tier, out int min, out int max)
        {
            int t = (int)tier;
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: min = t == 0 ? 2 : 3; max = t == 0 ? 3 : t == 1 ? 4 : 5; return;
                case SkyIslandGatherKind.Driftwood: min = t == 0 ? 1 : 2; max = t == 0 ? 2 : 3; return;
                case SkyIslandGatherKind.Moss: min = 1 + t; max = 2 + t; return;
                case SkyIslandGatherKind.Ore: min = t == 0 ? 1 : 2; max = t == 0 ? 2 : t == 1 ? 3 : 4; return;
                default: min = t == 2 ? 2 : 1; max = t == 0 ? 1 : t == 1 ? 2 : 3; return;
            }
        }

        internal static int PrimaryTypeId(SkyIslandGatherKind kind)
        {
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: return BossRushItemIds.SkyIslandGreenearSheaf;
                case SkyIslandGatherKind.Driftwood: return BossRushItemIds.SkyIslandDriftwood;
                case SkyIslandGatherKind.Moss: return BossRushItemIds.SkyIslandCloudmossFiber;
                case SkyIslandGatherKind.Ore: return BossRushItemIds.SkyIslandBrassScrap;
                default: return BossRushItemIds.SkyIslandWindcrystalShard;
            }
        }

        /// <summary>附带产出（每次至多一件）：矿脉带风晶碎片、风晶簇带星屑；其余外观没有。</summary>
        internal static int ExtraTypeId(SkyIslandGatherKind kind)
        {
            if (kind == SkyIslandGatherKind.Ore) return BossRushItemIds.SkyIslandWindcrystalShard;
            if (kind == SkyIslandGatherKind.Crystal) return BossRushItemIds.SkyIslandStardust;
            return 0;
        }

        /// <summary>附带产出的概率。安全区没有；风晶簇在夜里多 <see cref="NightStardustBonus"/>。</summary>
        internal static double ExtraChance(SkyIslandGatherKind kind, SkyIslandLootTier tier, bool night)
        {
            if (kind == SkyIslandGatherKind.Ore)
                return tier == SkyIslandLootTier.Starworks ? 0.30 : tier == SkyIslandLootTier.Voyage ? 0.15 : 0.0;
            if (kind == SkyIslandGatherKind.Crystal)
            {
                double baseChance = tier == SkyIslandLootTier.Starworks ? 0.35 : tier == SkyIslandLootTier.Voyage ? 0.15 : 0.0;
                return baseChance > 0.0 && night ? baseChance + NightStardustBonus : baseChance;
            }
            return 0.0;
        }

        /// <summary>
        /// 一次采集的产出。随机流由调用方按「本趟种子 + 采集点 id」给（<see cref="SkyIslandLootTables.CreateStream"/>）；
        /// 抽样顺序固定为「件数 → 附带」，昼夜只改附带的门槛、不改抽了几次，同一种子白天黑夜的主产出件数一致。
        /// 不带存档的这个重载就是没有任何剧情加成时的产出。
        /// </summary>
        internal static SkyIslandYield[] Roll(SkyIslandGatherNode node, Random random, bool night)
        {
            return Roll(node, random, night, null);
        }

        /// <summary>
        /// 带剧情进度的一次采集：修好的地方长得更旺（<see cref="StoryBonusCount"/> / <see cref="StoryBonusChance"/>）。
        /// 加成只加在已经抽出的件数与附带门槛上，**不多抽一次随机数**：同一种子有没有加成，抽到的底数与附带判定用的同一个数。
        /// </summary>
        internal static SkyIslandYield[] Roll(SkyIslandGatherNode node, Random random, bool night, SkyIslandStoryData data)
        {
            if (node == null || random == null) return new SkyIslandYield[0];
            int min, max;
            CountRange(node.Kind, node.Tier, out min, out max);
            int count = min + random.Next(max - min + 1) + StoryBonusCount(node, data);
            double roll = random.NextDouble();
            int extra = ExtraTypeId(node.Kind);
            if (extra != 0 && roll < ExtraChance(node.Kind, node.Tier, night) + StoryBonusChance(node, data))
                return new[] { new SkyIslandYield(PrimaryTypeId(node.Kind), count), new SkyIslandYield(extra, 1) };
            return new[] { new SkyIslandYield(PrimaryTypeId(node.Kind), count) };
        }

        /// <summary>一个采集点的期望产出（件），没有剧情加成。</summary>
        internal static Dictionary<int, double> ExpectedYield(SkyIslandGatherNode node, bool night)
        {
            return ExpectedYield(node, night, null);
        }

        /// <summary>一个采集点的期望产出（件）：主产出取件数均值、附带取概率，都含剧情加成。经济估算与报告共用。</summary>
        internal static Dictionary<int, double> ExpectedYield(SkyIslandGatherNode node, bool night, SkyIslandStoryData data)
        {
            var result = new Dictionary<int, double>();
            if (node == null) return result;
            int min, max;
            CountRange(node.Kind, node.Tier, out min, out max);
            result[PrimaryTypeId(node.Kind)] = (min + max) / 2.0 + StoryBonusCount(node, data);
            int extra = ExtraTypeId(node.Kind);
            double chance = ExtraChance(node.Kind, node.Tier, night) + StoryBonusChance(node, data);
            if (extra != 0 && chance > 0.0)
            {
                double existing;
                result.TryGetValue(extra, out existing);
                result[extra] = existing + chance;
            }
            return result;
        }

        /// <summary>全岛采集点一趟采完的期望产出（件），没有剧情加成。</summary>
        internal static Dictionary<int, double> ExpectedRaidYield(bool night)
        {
            return ExpectedRaidYield(night, null);
        }

        /// <summary>全岛采集点一趟采完的期望产出（件），含这份存档的剧情加成。</summary>
        internal static Dictionary<int, double> ExpectedRaidYield(bool night, SkyIslandStoryData data)
        {
            var total = new Dictionary<int, double>();
            for (int i = 0; i < nodes.Length; i++)
                foreach (KeyValuePair<int, double> entry in ExpectedYield(nodes[i], night, data))
                {
                    double existing;
                    total.TryGetValue(entry.Key, out existing);
                    total[entry.Key] = existing + entry.Value;
                }
            return total;
        }

        /// <summary>全岛采集点一趟采完的期望价值（按物品 Value 计），没有剧情加成。</summary>
        internal static double ExpectedRaidValue(bool night)
        {
            return ExpectedRaidValue(night, null);
        }

        /// <summary>全岛采集点一趟采完的期望价值（按物品 Value 计），含这份存档的剧情加成。</summary>
        internal static double ExpectedRaidValue(bool night, SkyIslandStoryData data)
        {
            double value = 0.0;
            foreach (KeyValuePair<int, double> entry in ExpectedRaidYield(night, data))
                value += entry.Value * SkyIslandItemRules.ValueOf(entry.Key);
            return value;
        }

        /// <summary>
        /// 剧情进度对采集的回应：修好的地方长得更旺，每处多一件主产出。
        /// 菜畦重新开张 → 青穗梯田的青穗草；风标转回来 → 悬根林的云苔；星灯亮起 → 残星工坊的残铜矿脉；
        /// 噬风散去 → 鸣风栈道的风晶簇（风凝成了晶）。没有存档（<paramref name="data"/> 为 null）时一律为 0。
        /// </summary>
        internal static int StoryBonusCount(SkyIslandGatherNode node, SkyIslandStoryData data)
        {
            if (node == null || data == null) return 0;
            switch (node.Kind)
            {
                case SkyIslandGatherKind.Grass: return node.Region == "C" && data.Has(SkyIslandStoryFlag.PlantingDelivered) ? 1 : 0;
                case SkyIslandGatherKind.Moss: return node.Region == "D" && data.Has(SkyIslandStoryFlag.WindBeacon) ? 1 : 0;
                case SkyIslandGatherKind.Ore: return node.Region == "G" && data.Has(SkyIslandStoryFlag.StarLamp) ? 1 : 0;
                case SkyIslandGatherKind.Crystal: return node.Region == "E" && data.StormResolved ? 1 : 0;
                default: return 0;
            }
        }

        /// <summary>观星镜校准之后，残星瞭台的风晶簇多出星屑的概率加成；其余为 0。</summary>
        internal static double StoryBonusChance(SkyIslandGatherNode node, SkyIslandStoryData data)
        {
            return node != null && data != null && node.Kind == SkyIslandGatherKind.Crystal && node.Region == "S4" &&
                data.Has(SkyIslandStoryFlag.Telescope) ? TelescopeStardustBonus : 0.0;
        }

        /// <summary>这一处为什么长得更旺（采到时字幕末尾附一句）；没有加成返回 null。</summary>
        internal static string StoryBonusReason(SkyIslandGatherNode node, SkyIslandStoryData data)
        {
            if (StoryBonusCount(node, data) == 0 && StoryBonusChance(node, data) <= 0.0) return null;
            switch (node.Kind)
            {
                case SkyIslandGatherKind.Grass:
                    return L10n.T("晴禾的菜畦重新种下去了，青穗长得更旺", "Qinghe's beds are planted again, and the greenear grows thicker");
                case SkyIslandGatherKind.Moss:
                    return L10n.T("风标转回来之后，根环里的云苔长回来了", "Since the wind beacon turned back, the cloudmoss has grown back in the root ring");
                case SkyIslandGatherKind.Ore:
                    return L10n.T("星灯照亮了工坊的铜脉", "The star lamp lights up the workshop's brass veins");
                default:
                    return node.Region == "E"
                        ? L10n.T("噬风散去的地方，风凝成了晶", "Where the Windeater broke apart, the wind has set into crystal")
                        : L10n.T("观星镜对准之后，星屑常落在镜筒边", "Since the telescope was aligned, stardust keeps settling beside it");
            }
        }

        internal static string KindName(SkyIslandGatherKind kind)
        {
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: return L10n.T("青穗草丛", "Greenear tuft");
                case SkyIslandGatherKind.Driftwood: return L10n.T("搁浅的浮木", "Stranded driftwood");
                case SkyIslandGatherKind.Moss: return L10n.T("云苔", "Cloudmoss patch");
                case SkyIslandGatherKind.Ore: return L10n.T("残铜矿脉", "Brass vein");
                default: return L10n.T("风晶簇", "Wind crystal cluster");
            }
        }

        /// <summary>采集点的交互名与浮空字。</summary>
        internal static string GatherLabel(SkyIslandGatherKind kind)
        {
            return KindName(kind) + L10n.T(" · 采集", " · gather");
        }

        /// <summary>采到东西之后的字幕，末尾附上这一处为什么长得更旺；没有原因时与不带原因的那一句相同。</summary>
        internal static string HarvestCaption(SkyIslandYield[] yields, string reason)
        {
            string caption = HarvestCaption(yields);
            if (string.IsNullOrEmpty(reason) || yields == null || yields.Length == 0) return caption;
            return caption + L10n.T("（", " (") + reason + L10n.T("）", ")");
        }

        /// <summary>采到东西之后的字幕：「采到：云苔纤维 ×3、风晶碎片 ×1」。</summary>
        internal static string HarvestCaption(SkyIslandYield[] yields)
        {
            if (yields == null || yields.Length == 0)
                return L10n.T("这里已经采空了。", "There is nothing left to gather here.");
            string list = string.Empty;
            for (int i = 0; i < yields.Length; i++)
            {
                if (i > 0) list += L10n.T("、", ", ");
                list += SkyIslandItemRules.Name(yields[i].TypeId) + " ×" + yields[i].Count;
            }
            return L10n.T("采到：", "Gathered: ") + list;
        }

        #endregion

        #region 配方

        private static readonly SkyIslandRecipe[] recipes =
        {
            // 浮舟 · 渡口工台：工具、护符与「碎片凑整」。
            Recipe("Lantern", SkyIslandCraftStation.Dock, BossRushItemIds.SkyIslandWindLantern, 1,
                In(BossRushItemIds.SkyIslandDriftwood, 2), In(BossRushItemIds.SkyIslandCloudmossFiber, 1)),
            // 晴岚风晶是风晶灯的灯芯；碎晶要送进残星工坊的熔晶炉，星灯不亮炉子烧不起来。
            Recipe("Windcrystal", SkyIslandCraftStation.Dock, BossRushItemIds.SkyIslandQinglanWindcrystal, 1,
                In(BossRushItemIds.SkyIslandWindcrystalShard, 5)).After(SkyIslandStoryFlag.StarLamp),
            Recipe("Charm", SkyIslandCraftStation.Dock, BossRushItemIds.SkyIslandQinglanCharm, 1,
                In(BossRushItemIds.SkyIslandBrassScrap, 3), In(BossRushItemIds.SkyIslandWindcrystalShard, 2),
                In(BossRushItemIds.SkyIslandStardust, 1)),
            // 风标罗盘丢了（死在岛上进了墓碑）可以重做一只：得先收到过浮舟捎来的那一只。材料价值略高于罗盘，不是换钱的路子。
            Recipe("Compass", SkyIslandCraftStation.Dock, BossRushItemIds.SkyIslandWindVaneCompass, 1,
                In(BossRushItemIds.SkyIslandBrassScrap, 4), In(BossRushItemIds.SkyIslandWindcrystalShard, 2))
                .AfterNote(SkyIslandItemRules.CompassKeepsake),
            // 晴禾 · 灶台：浮木当柴。归航菜的做法写在种植记录里，记录交还晴禾之后才会做便当。
            Recipe("Bento", SkyIslandCraftStation.Stove, BossRushItemIds.SkyIslandHomecomingBento, 1,
                In(BossRushItemIds.SkyIslandGreenearSheaf, 4), In(BossRushItemIds.SkyIslandDriftwood, 1))
                .After(SkyIslandStoryFlag.PlantingDelivered),
            Recipe("IncenseStove", SkyIslandCraftStation.Stove, BossRushItemIds.SkyIslandWindwardIncense, 1,
                In(BossRushItemIds.SkyIslandCloudmossFiber, 2), In(BossRushItemIds.SkyIslandGreenearSheaf, 2)),
            // 眠苔 · 药臼：驱风香两处都能做，任一居民不在（婚后离岛、生成失败）另一处照样有。
            Recipe("Salve", SkyIslandCraftStation.Mortar, BossRushItemIds.SkyIslandStarmossSalve, 1,
                In(BossRushItemIds.SkyIslandCloudmossFiber, 4), In(BossRushItemIds.SkyIslandWindcrystalShard, 1)),
            Recipe("IncenseMortar", SkyIslandCraftStation.Mortar, BossRushItemIds.SkyIslandWindwardIncense, 1,
                In(BossRushItemIds.SkyIslandCloudmossFiber, 2), In(BossRushItemIds.SkyIslandGreenearSheaf, 2))
        };

        internal static SkyIslandRecipe[] Recipes { get { return recipes; } }

        internal static List<SkyIslandRecipe> RecipesFor(SkyIslandCraftStation station)
        {
            var result = new List<SkyIslandRecipe>();
            for (int i = 0; i < recipes.Length; i++) if (recipes[i].Station == station) result.Add(recipes[i]);
            return result;
        }

        internal static SkyIslandRecipe FindRecipe(string id)
        {
            for (int i = 0; i < recipes.Length; i++)
                if (string.Equals(recipes[i].Id, id, StringComparison.Ordinal)) return recipes[i];
            return null;
        }

        /// <summary>还差哪些材料（数量不足的那几种，缺多少）。<paramref name="countInPack"/> 给出背包里某件物品的件数。</summary>
        internal static List<SkyIslandIngredient> Missing(SkyIslandRecipe recipe, Func<int, int> countInPack)
        {
            return Missing(recipe == null ? null : recipe.Inputs, countInPack);
        }

        /// <summary>一组材料还差哪些（配方与点灯共用）。</summary>
        internal static List<SkyIslandIngredient> Missing(SkyIslandIngredient[] inputs, Func<int, int> countInPack)
        {
            var missing = new List<SkyIslandIngredient>();
            if (inputs == null) return missing;
            for (int i = 0; i < inputs.Length; i++)
            {
                SkyIslandIngredient input = inputs[i];
                int have = countInPack == null ? 0 : Math.Max(0, countInPack(input.TypeId));
                if (have < input.Count) missing.Add(new SkyIslandIngredient(input.TypeId, input.Count - have));
            }
            return missing;
        }

        /// <summary>这份存档会不会做这条配方（剧情旗标与手记条目都满足）。没有存档时只有不设门槛的配方算会。</summary>
        internal static bool Unlocked(SkyIslandRecipe recipe, SkyIslandStoryData data)
        {
            if (recipe == null) return false;
            if (recipe.RequiresFlag == SkyIslandStoryFlag.None && recipe.RequiresNote == null) return true;
            if (data == null) return false;
            if (recipe.RequiresFlag != SkyIslandStoryFlag.None && !data.Has(recipe.RequiresFlag)) return false;
            return recipe.RequiresNote == null ||
                (data.discoveredNotes != null && Array.IndexOf(data.discoveredNotes, recipe.RequiresNote) >= 0);
        }

        /// <summary>还不会做时，要等到什么时候（按钮与手记共用）。</summary>
        internal static string UnlockHint(SkyIslandRecipe recipe)
        {
            if (recipe == null) return string.Empty;
            if (recipe.RequiresNote != null)
                return L10n.T("收到浮舟托信鸽捎来的罗盘之后", "once Fuzhou's compass has come with a pigeon");
            switch (recipe.RequiresFlag)
            {
                case SkyIslandStoryFlag.PlantingDelivered: return L10n.T("种植记录交还晴禾之后", "once Qinghe has her planting record back");
                case SkyIslandStoryFlag.StarLamp: return L10n.T("残星工坊的星灯亮起之后", "once the Fallen Star Workshop's star lamp is lit");
                default: return L10n.T("旅程再往前走一段之后", "further along the journey");
            }
        }

        /// <summary>还不会做的配方按钮：「还不会做 归航菜便当（种植记录交还晴禾之后）」。</summary>
        internal static string LockedLabel(SkyIslandRecipe recipe)
        {
            if (recipe == null) return string.Empty;
            return L10n.T("还不会做 ", "Not yet: ") + SkyIslandItemRules.Name(recipe.OutputTypeId) +
                L10n.T("（", " (") + UnlockHint(recipe) + L10n.T("）", ")");
        }

        /// <summary>点了还不会做的配方时，居民说为什么。</summary>
        internal static string LockedMessage(SkyIslandRecipe recipe)
        {
            if (recipe == null) return string.Empty;
            switch (recipe.Id)
            {
                case "Bento": return L10n.T("晴禾：归航菜的做法写在种植记录里。记录还泡在蛙鸣池边，我凑不齐那几样。",
                    "Qinghe: The homecoming recipe is written in my planting record, and that is still soaking by Frogsong Pool. I cannot put it together without it.");
                case "Windcrystal": return L10n.T("浮舟：碎晶得送进残星工坊的熔晶炉。星灯不亮，炉子就烧不起来。",
                    "Fuzhou: Shards have to go into the crystal furnace at the Fallen Star Workshop, and that furnace will not burn until the star lamp is lit.");
                case "Compass": return L10n.T("浮舟：罗盘我还没捎给你呢。等第一只信鸽落了、你手里有过一只，我才照着样子重做。",
                    "Fuzhou: I have not even sent you the compass yet. Once the first pigeon has come and you have held one, I can make another to match.");
                default: return L10n.T("还不会做：", "Not yet: ") + UnlockHint(recipe) + L10n.T("。", ".");
            }
        }

        internal static bool CanCraft(SkyIslandRecipe recipe, Func<int, int> countInPack)
        {
            return recipe != null && Missing(recipe, countInPack).Count == 0;
        }

        /// <summary>配方材料的总价值（按物品 Value 计）。</summary>
        internal static int InputValue(SkyIslandRecipe recipe)
        {
            int value = 0;
            if (recipe == null || recipe.Inputs == null) return 0;
            for (int i = 0; i < recipe.Inputs.Length; i++)
                value += recipe.Inputs[i].Count * SkyIslandItemRules.ValueOf(recipe.Inputs[i].TypeId);
            return value;
        }

        internal static int OutputValue(SkyIslandRecipe recipe)
        {
            return recipe == null ? 0 : recipe.OutputCount * SkyIslandItemRules.ValueOf(recipe.OutputTypeId);
        }

        internal static string StationName(SkyIslandCraftStation station)
        {
            switch (station)
            {
                case SkyIslandCraftStation.Dock: return L10n.T("浮舟 · 渡口工台", "Fuzhou · dock workbench");
                case SkyIslandCraftStation.Stove: return L10n.T("晴禾 · 灶台", "Qinghe · garden stove");
                default: return L10n.T("眠苔 · 药臼", "Miantai · moss mortar");
            }
        }

        /// <summary>居民与装置面板上「打开合成台」那一项的文字。</summary>
        internal static string StationChoice(SkyIslandCraftStation station)
        {
            switch (station)
            {
                case SkyIslandCraftStation.Dock: return L10n.T("渡口工台 · 用群岛材料做东西", "Dock workbench · make things from island materials");
                case SkyIslandCraftStation.Stove: return L10n.T("灶台 · 用群岛材料下厨", "Garden stove · cook with island materials");
                default: return L10n.T("药臼 · 用群岛材料配药", "Moss mortar · grind remedies from island materials");
            }
        }

        /// <summary>合成面板正文的开头：谁在这儿、能做什么。</summary>
        internal static string StationIntro(SkyIslandCraftStation station)
        {
            switch (station)
            {
                case SkyIslandCraftStation.Dock: return L10n.T(
                    "浮舟把工台上的刨花扫到一边：『浮木作骨、云苔糊罩，就是一盏夜里用的风灯；铜片打底、嵌上风晶和星屑，就是护符——噬风那阵风碰上它会让开几分。碎晶攒够五片，等工坊的星灯亮了，我拿去熔成一整块：岛上还有七处缺一盏风晶灯。』",
                    "Fuzhou sweeps the shavings off the workbench: 'Driftwood for the frame and a cloudmoss shade make a wind lantern for the nights. A brass backing set with crystal and stardust makes a charm — the Windeater's gusts give way around it. Bring five shards once the workshop's star lamp is lit and I will fuse them whole: seven places on the isles still want a windcrystal lamp.'");
                case SkyIslandCraftStation.Stove: return L10n.T(
                    "晴禾往灶里添了块浮木：『驱风香的烟压得住大风，过桥、上栈道都靠它。等种植记录回来，我照着上面的做法给你装归航菜便当——在岛上吃，就算吃过我这一顿。』",
                    "Qinghe feeds a piece of driftwood into the stove: 'Windward incense smoke holds off even a gale — you want it on the bridges and the boardwalk. Once my planting record is back I can pack homecoming bentos from the recipe in it; eat one on the isles and it counts as my meal.'");
                default: return L10n.T(
                    "眠苔把药臼推过来：『云苔纤维磨得越细，药膏越凉；加一片风晶，伤口好得快——省下来的钱，留着付给真正要命的伤。驱风香也是这么捣出来的。』",
                    "Miantai slides the mortar over: 'The finer the cloudmoss is ground, the cooler the salve; a shard of wind crystal closes wounds faster — save your coin for the wounds that really need me. Windward incense is pounded the same way.'");
            }
        }

        /// <summary>合成面板正文的后半：背包里的群岛材料（只算背包，不算基地仓库）。</summary>
        internal static string PackSummary(Func<int, int> countInPack)
        {
            string list = string.Empty;
            for (int i = 0; i < MaterialTypeIds.Length; i++)
            {
                int have = countInPack == null ? 0 : countInPack(MaterialTypeIds[i]);
                if (have <= 0) continue;
                if (list.Length > 0) list += " · ";
                list += SkyIslandItemRules.Name(MaterialTypeIds[i]) + " " + have;
            }
            if (list.Length == 0)
                return L10n.T("背包里还没有群岛材料——去找采集点：青穗草丛、浮木、云苔、残铜矿脉和风晶簇，走近时会亮起一点光。",
                    "No island materials in your pack yet — look for gathering spots: greenear tufts, driftwood, cloudmoss, brass veins and wind crystal clusters. They glow faintly as you get close.");
            return L10n.T("背包里的群岛材料（只算背包，不算基地仓库）：", "Island materials in your pack (pack only, not base storage): ") + list;
        }

        /// <summary>配方按钮：「制作 风灯（浮木 2/2 · 云苔纤维 1/1）」。</summary>
        internal static string RecipeLabel(SkyIslandRecipe recipe, Func<int, int> countInPack)
        {
            if (recipe == null) return string.Empty;
            return L10n.T("制作 ", "Make ") + SkyIslandItemRules.Name(recipe.OutputTypeId) +
                (recipe.OutputCount > 1 ? " ×" + recipe.OutputCount : string.Empty) +
                L10n.T("（", " (") + HaveNeedList(recipe.Inputs, countInPack) + L10n.T("）", ")");
        }

        /// <summary>「浮木 2/2 · 云苔纤维 1/1」：配方按钮与点灯按钮共用，有几件按需要的封顶。</summary>
        internal static string HaveNeedList(SkyIslandIngredient[] inputs, Func<int, int> countInPack)
        {
            string list = string.Empty;
            if (inputs == null) return list;
            for (int i = 0; i < inputs.Length; i++)
            {
                SkyIslandIngredient input = inputs[i];
                int have = countInPack == null ? 0 : Math.Max(0, countInPack(input.TypeId));
                if (i > 0) list += " · ";
                list += SkyIslandItemRules.Name(input.TypeId) + " " + Math.Min(have, input.Count) + "/" + input.Count;
            }
            return list;
        }

        /// <summary>材料不够时的回话：「材料还差：浮木 ×1 · 云苔纤维 ×1。」</summary>
        internal static string MissingMessage(List<SkyIslandIngredient> missing)
        {
            string list = string.Empty;
            for (int i = 0; i < missing.Count; i++)
            {
                if (i > 0) list += " · ";
                list += SkyIslandItemRules.Name(missing[i].TypeId) + " ×" + missing[i].Count;
            }
            return L10n.T("材料还差：", "Still short: ") + list + L10n.T("。", ".");
        }

        internal static string CraftedMessage(SkyIslandRecipe recipe)
        {
            return L10n.T("做好了：", "Done: ") + SkyIslandItemRules.Name(recipe.OutputTypeId) +
                (recipe.OutputCount > 1 ? " ×" + recipe.OutputCount : string.Empty) +
                L10n.T("（已放进背包，放不下就落在脚边）。", " (in your pack, or at your feet if it was full).");
        }

        #endregion

        #region 耗材与夜风

        /// <summary>全部群岛材料，按 TypeID 递增（背包摘要按这个顺序列）。</summary>
        internal static readonly int[] MaterialTypeIds =
        {
            BossRushItemIds.SkyIslandCloudmossFiber, BossRushItemIds.SkyIslandGreenearSheaf, BossRushItemIds.SkyIslandDriftwood,
            BossRushItemIds.SkyIslandBrassScrap, BossRushItemIds.SkyIslandWindcrystalShard, BossRushItemIds.SkyIslandStardust,
            BossRushItemIds.SkyIslandQinglanWindcrystal
        };

        internal static SkyIslandFieldBuff BuffFor(int typeId)
        {
            switch (typeId)
            {
                case BossRushItemIds.SkyIslandWindLantern: return SkyIslandFieldBuff.Lantern;
                case BossRushItemIds.SkyIslandWindwardIncense: return SkyIslandFieldBuff.Incense;
                case BossRushItemIds.SkyIslandQinglanCharm: return SkyIslandFieldBuff.Charm;
                case BossRushItemIds.SkyIslandHomecomingBento: return SkyIslandFieldBuff.Meal;
                case BossRushItemIds.SkyIslandHomecomingBadge: return SkyIslandFieldBuff.Recall;
                default: return SkyIslandFieldBuff.None;
            }
        }

        /// <summary>物品详情里「使用」那一行。</summary>
        internal static string UsageText(SkyIslandFieldBuff buff)
        {
            switch (buff)
            {
                case SkyIslandFieldBuff.Lantern: return L10n.T("使用：在晴岚群岛上点亮约 4 分钟：微风里不积寒意，大风里只挡一半；照亮身边（离岛无效）",
                    "Use: on the Qinglan isles, burns about 4 minutes: no chill in a breeze, half protection in a gale; lights your way (no effect elsewhere)");
                case SkyIslandFieldBuff.Incense: return L10n.T("使用：在晴岚群岛上约 5 分钟什么风都挡得住（大风也一样），耐力恢复加快（离岛无效）",
                    "Use: on the Qinglan isles, about 5 minutes safe from any wind (gales too) with faster stamina recovery (no effect elsewhere)");
                case SkyIslandFieldBuff.Charm: return L10n.T("使用：本趟出击噬风的风暴伤害 −35%、生命上限 +10%、耐力恢复 +10%（只在晴岚群岛上能用，离岛失效，不叠加）",
                    "Use: this raid, 35% less damage from the Windeater's storm, +10% max health and +10% stamina recovery (Qinglan isles only; ends when you leave; does not stack)");
                case SkyIslandFieldBuff.Meal: return L10n.T("在晴岚群岛上吃：菜畦重新开张之后，算作晴禾的归航菜（本趟一次）",
                    "Eat on the Qinglan isles: once the garden has reopened, it counts as Qinghe's homecoming meal (once per raid)");
                case SkyIslandFieldBuff.Recall: return L10n.T("使用：在晴岚群岛上拉一下缆绳，回到登云码头（每趟一次，附近有敌人时不行；不消耗）",
                    "Use: on the Qinglan isles, pull the line back to Cloudrise Dock (once per raid, not with enemies nearby; not consumed)");
                default: return string.Empty;
            }
        }

        /// <summary>拉缆绳回到码头之后的字幕。</summary>
        internal static string RecallArrived
        {
            get
            {
                return L10n.T("你拉了拉航徽上的缆绳——云海那头有人把它收紧了。回过神来，你已经站在登云码头上。（这一趟的缆绳用掉了）",
                    "You tug the line on the badge — someone across the cloud sea hauls it in. The next moment you are standing on Cloudrise Dock. (That was this trip's line.)");
            }
        }

        internal static string RecallSpent
        {
            get
            {
                return L10n.T("这一趟的缆绳已经拉过了，下次出击码头会再系一条。",
                    "You have already used this trip's line; the dock will tie another for your next raid.");
            }
        }

        internal static string RecallNotReady
        { get { return L10n.T("群岛还没就绪，缆绳拉不动。", "The isles are not ready yet; the line will not pull."); } }

        internal static string RecallFailed
        { get { return L10n.T("缆绳没拉动——码头那头找不到落脚的地方。", "The line will not pull — there is no footing at the dock end."); } }

        /// <summary>耗材生效时的字幕（便当的回话由归航菜服务给，这里没有）。</summary>
        internal static string BuffStarted(SkyIslandFieldBuff buff)
        {
            switch (buff)
            {
                case SkyIslandFieldBuff.Lantern: return L10n.T("风灯点亮了：约 4 分钟内微风吹不透，大风里也能挡掉一半。",
                    "The wind lantern is lit: for about 4 minutes a breeze cannot chill you, and it holds off half of a gale.");
                case SkyIslandFieldBuff.Incense: return L10n.T("驱风香点上了：约 5 分钟内什么风都侵不了身，耐力恢复加快。",
                    "Windward incense is burning: for about 5 minutes no wind can chill you, and stamina recovers faster.");
                case SkyIslandFieldBuff.Charm: return L10n.T("晴岚护符系上了：本趟噬风的风暴伤不到你那么深，生命上限与耐力恢复也小幅提升（离岛失效）。",
                    "The Qinglan charm is tied on: the Windeater's storm will not cut as deep this raid, and max health and stamina recovery rise a little (ends when you leave the isles).");
                default: return string.Empty;
            }
        }

        internal static string BuffLow(SkyIslandFieldBuff buff)
        {
            return buff == SkyIslandFieldBuff.Lantern
                ? L10n.T("风灯快燃尽了。", "The wind lantern is burning low.")
                : L10n.T("驱风香快燃尽了。", "The windward incense is almost gone.");
        }

        internal static string BuffEnded(SkyIslandFieldBuff buff)
        {
            return buff == SkyIslandFieldBuff.Lantern
                ? L10n.T("风灯熄了。", "The wind lantern has gone out.")
                : L10n.T("驱风香燃尽了。", "The windward incense has burned out.");
        }

        internal static string CharmAlreadyWorn
        { get { return L10n.T("身上已经系着一枚晴岚护符了，这一趟不会再叠。", "You are already wearing a Qinglan charm; a second one will not stack this raid."); } }

        internal static string OffIsland
        { get { return L10n.T("它只认得晴岚群岛的风——到了岛上才有用。", "It only answers to the winds of the Qinglan isles — use it there."); } }

        /// <summary>夜里：21 点到次日 5 点（与 <c>SkyIslandLighting.ResolveTimeBlend</c> 的「星夜」整档一致）。</summary>
        internal static bool IsNight(double hours)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours)) return false;
            hours = (hours % 24 + 24) % 24;
            return hours >= 21 || hours < 5;
        }

        /// <summary>
        /// 风力 0 无风 / 1 微风 / 2 大风。夜里 +1、站在桥上（含中继平台）+1、噬风将至（双航标已亮而噬风未散）时栈道与桥上再 +1，封顶 2。
        /// 白天在主岛上永远无风——夜风是「晚上出门要准备」，不是「随时在掉东西」。
        /// </summary>
        internal static int WindLevel(bool night, bool onBridge, bool onBoardwalk, bool stormPending)
        {
            int level = night ? 1 : 0;
            if (onBridge) level++;
            if (stormPending && (onBoardwalk || onBridge)) level++;
            return level > 2 ? 2 : level;
        }

        /// <summary>夜里算不算起风：岛上的灯凑满 <see cref="SkyIslandLights.Target"/> 盏之后，夜里不再起风（桥上本来就有的风照旧）。</summary>
        internal static bool NightWind(bool night, int lightsLit)
        {
            return night && lightsLit < SkyIslandLights.Target;
        }

        /// <summary>带着噬风之核：核里那团风把身边的风吃掉一截，大风对你只算微风。</summary>
        internal static int CoreEased(int windLevel, bool coreCarried)
        {
            return coreCarried && windLevel >= 2 ? 1 : windLevel;
        }

        /// <summary>此刻挡着风的是什么：灶火 / 风晶灯旁或焚着驱风香什么风都挡得住，只有风灯时算半挡。</summary>
        internal static SkyIslandWarmth Warmth(bool nearFire, bool incenseBurning, bool lanternLit)
        {
            if (nearFire || incenseBurning) return SkyIslandWarmth.Shelter;
            return lanternLit ? SkyIslandWarmth.Lantern : SkyIslandWarmth.None;
        }

        /// <summary>
        /// 推进一段游戏时间后的寒意。灶火、风晶灯、驱风香什么风都挡得住，一直在退；
        /// 风灯挡得住微风，大风里火苗被压低，寒意按大风速率的 <see cref="LanternGaleFactor"/> 积。
        /// </summary>
        internal static float StepExposure(float exposure, int windLevel, SkyIslandWarmth warmth, float seconds)
        {
            if (float.IsNaN(exposure)) exposure = 0f;
            if (seconds > 0f && !float.IsNaN(seconds) && !float.IsInfinity(seconds))
            {
                float rate;
                if (warmth == SkyIslandWarmth.Shelter) rate = -WarmRecovery;
                else if (warmth == SkyIslandWarmth.Lantern) rate = windLevel >= 2 ? GaleRate * LanternGaleFactor : -WarmRecovery;
                else rate = windLevel >= 2 ? GaleRate : windLevel == 1 ? BreezeRate : -CalmRecovery;
                exposure += rate * seconds;
            }
            return exposure < 0f ? 0f : exposure > ExposureMax ? ExposureMax : exposure;
        }

        /// <summary>噬风一波风暴脉冲对玩家的伤害：系着晴岚护符时按 <see cref="CharmStormWard"/> 减。</summary>
        internal static float StormPulseDamage(float baseDamage, bool warded)
        {
            return warded ? baseDamage * (1f - CharmStormWard) : baseDamage;
        }

        /// <summary>本趟第一次靠噬风之核把大风压成微风时的字幕。</summary>
        internal static string CoreEasesGale
        {
            get
            {
                return L10n.T("噬风之核在背包里轻轻打转：大风绕开了你，只剩一点微风。",
                    "The Windeater Core turns softly in your pack: the gale parts around you, leaving only a breeze.");
            }
        }

        /// <summary>风寒的滞回：积满才上身，退到 <see cref="ExposureClear"/> 以下才解除。</summary>
        internal static bool NextChilled(bool chilled, float exposure)
        {
            return chilled ? exposure > ExposureClear : exposure >= ExposureMax;
        }

        /// <summary>本趟第一次起风时的说明（风力大于 0 时发一次）。</summary>
        internal static string WindExplain(bool stormPending)
        {
            string text = L10n.T(
                "起风了：夜里和桥上会慢慢积累寒意，积满后耐力恢复变慢、饿得更快（不掉血）。灶火与风晶灯旁、焚着驱风香时什么风都挡得住；风灯挡得住微风，大风里只挡一半。",
                "The wind is picking up: at night and on bridges you slowly get chilled. A full chill slows stamina recovery and makes you hungry faster (no health loss). By a hearth or a windcrystal lamp, or with windward incense burning, no wind gets through; a wind lantern holds off a breeze but only half of a gale.");
            if (stormPending)
                text += L10n.T("两盏航标都亮了、噬风还没散：鸣风栈道和桥上是大风。",
                    " Both beacons are lit and the Windeater is still out there: Windsong Boardwalk and the bridges are in a gale.");
            return text;
        }

        internal static string ExposureWarning
        {
            get
            {
                return L10n.T("寒意渐重——找处灶火或风晶灯，或者焚一炷驱风香、点上风灯。",
                    "The chill is setting in — find a hearth or a windcrystal lamp, or burn windward incense or light a wind lantern.");
            }
        }

        internal static string ChillStarted
        { get { return L10n.T("风寒：耐力恢复变慢、饿得更快。暖和过来就会消失。", "Wind chill: stamina recovers slower and you get hungry faster. It goes away once you warm up."); } }

        internal static string ChillEnded
        { get { return L10n.T("身子暖过来了，风寒退了。", "You have warmed up; the wind chill is gone."); } }

        #endregion

        private static SkyIslandGatherNode Node(string id, string marker, float bearing, float distance,
            SkyIslandGatherKind kind, SkyIslandLootTier tier, string region)
        {
            return new SkyIslandGatherNode
            {
                Id = id, Marker = marker, Bearing = bearing, Distance = distance, Kind = kind, Tier = tier, Region = region
            };
        }

        private static SkyIslandRecipe Recipe(string id, SkyIslandCraftStation station, int output, int count,
            params SkyIslandIngredient[] inputs)
        {
            return new SkyIslandRecipe { Id = id, Station = station, OutputTypeId = output, OutputCount = count, Inputs = inputs };
        }

        private static SkyIslandIngredient In(int typeId, int count) { return new SkyIslandIngredient(typeId, count); }
    }
}

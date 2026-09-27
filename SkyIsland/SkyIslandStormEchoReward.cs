// ============================================================================
// SkyIslandStormEchoReward.cs - 噬风·回响的「回响遗存」：一箱只装岛上的东西（纯规则）
// ============================================================================
// 奖励只进已有的消耗链：不给新 TypeID，也不抽官方物资池——池里有纯金徽章这类只能卖钱的收藏品。
// - 风晶碎片 ×3：护符、药膏、罗盘都要，攒够五片又是一块晴岚风晶。**少于一块风晶折合的五片**，
//   回响自己养不活自己，下一趟还得去采（防刷）；
// - 星屑 ×2：晴岚护符、云苔纱笠、残星瞭台那盏灯都要，而它平时只在深处与夜里的风晶簇偶尔出；
// - 再抽一件「下一趟打回响用得上」的：晴岚护符（挡回响的风暴）/ 归航菜便当（跑得快一点）/ 驱风香 ×2（挡回响带回来的大风）。
// 价值一律取 SkyIslandItemRules.ValueOf（全部远低于岛上物资池的单件上限）；期望价值见 ExpectedValue，报告与回归共用。
// 纯逻辑，隔离回归直接执行。
// ============================================================================

namespace BossRush
{
    internal static class SkyIslandStormEchoReward
    {
        internal const int Shards = 3;
        internal const int Stardust = 2;
        /// <summary>「下一趟用得上」那一件的两道门槛：[0, CharmBelow) 护符，[CharmBelow, BentoBelow) 便当，其余驱风香。</summary>
        internal const double CharmBelow = 0.35;
        internal const double BentoBelow = 0.70;
        internal const int IncenseCount = 2;
        /// <summary>回响遗存箱的随机流 id：本趟种子 + 这个 id，同一趟结果确定。</summary>
        internal const string Stream = "StormEchoTrophy";

        /// <summary>引风烧掉的东西：晴岚风晶，块数取剧情规则里的同一个常量（选项判据读的也是它）。</summary>
        internal static SkyIslandIngredient[] Cost
        {
            get
            {
                return new[] { new SkyIslandIngredient(BossRushItemIds.SkyIslandQinglanWindcrystal, SkyIslandStoryRules.StormEchoWindcrystalCost) };
            }
        }

        /// <summary>一箱回响遗存。<paramref name="kitRoll"/> 取 [0, 1) 的一次抽样。</summary>
        internal static SkyIslandYield[] For(double kitRoll)
        {
            SkyIslandYield kit = kitRoll < CharmBelow
                ? new SkyIslandYield(BossRushItemIds.SkyIslandQinglanCharm, 1)
                : kitRoll < BentoBelow
                    ? new SkyIslandYield(BossRushItemIds.SkyIslandHomecomingBento, 1)
                    : new SkyIslandYield(BossRushItemIds.SkyIslandWindwardIncense, IncenseCount);
            return new[]
            {
                new SkyIslandYield(BossRushItemIds.SkyIslandWindcrystalShard, Shards),
                new SkyIslandYield(BossRushItemIds.SkyIslandStardust, Stardust),
                kit
            };
        }

        /// <summary>一块晴岚风晶折合几片风晶碎片：读合成表那条配方，不另写一个 5。</summary>
        internal static int ShardsPerWindcrystal()
        {
            SkyIslandRecipe recipe = SkyIslandFieldcraftRules.FindRecipe("Windcrystal");
            if (recipe == null || recipe.Inputs == null) return 0;
            for (int i = 0; i < recipe.Inputs.Length; i++)
                if (recipe.Inputs[i].TypeId == BossRushItemIds.SkyIslandWindcrystalShard) return recipe.Inputs[i].Count;
            return 0;
        }

        /// <summary>引一次风烧掉的物品价值（按物品 Value 计）。</summary>
        internal static int CostValue()
        {
            int value = 0;
            SkyIslandIngredient[] cost = Cost;
            for (int i = 0; i < cost.Length; i++) value += cost[i].Count * SkyIslandItemRules.ValueOf(cost[i].TypeId);
            return value;
        }

        /// <summary>一箱回响遗存的期望价值（按物品 Value 计），三道分支按门槛宽度加权。</summary>
        internal static double ExpectedValue()
        {
            return Shards * SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandWindcrystalShard)
                + Stardust * SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandStardust)
                + CharmBelow * SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandQinglanCharm)
                + (BentoBelow - CharmBelow) * SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandHomecomingBento)
                + (1.0 - BentoBelow) * IncenseCount * SkyIslandItemRules.ValueOf(BossRushItemIds.SkyIslandWindwardIncense);
        }
    }
}

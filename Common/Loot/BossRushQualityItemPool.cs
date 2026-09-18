// ============================================================================
// BossRushQualityItemPool.cs - 「官方全表按品质随机」候选池（跨玩法共享）
// ============================================================================
// 口径出处：owner 为《鸭科夫日报》签到奖品拍板的那一套——**全物品表按品质取，
// 只过 LootBlacklistRegistry，不限定标签**。2026-09-18 天灾远征的战利品沿用同一口径，
// 因此把它收敛成一份共享实现，避免第二个玩法再抄一遍全表扫描 + 缓存 + 黑名单。
//
// 冻结要点（改动前先读这四条）：
//   - 用 `ItemAssetsCollection.GetAllTypeIds` 而**不是** `Search`：官方 Search 在结果为空时
//     会自行降低 minQuality/maxQuality 反复重搜（鸭科夫源码/ItemStatsSystem/
//     ItemAssetsCollection.cs 的 DownGradeSearch 循环），会把承诺的 Q4 悄悄降成 Q1。
//     池为空时如实返回空数组，由调用方 fail-closed（留欠账或本次不发）；
//   - 全表扫描不能每次发奖都跑，因此**按品质缓存**；
//   - **空结果不入缓存**：空数组多半是 ItemAssetsCollection 尚未就绪的瞬态故障，
//     缓存它会把一次瞬时失败放大成该品质整会话不可用；
//   - 结果**排序后**返回：官方表的枚举顺序不保证稳定，确定性种子流要靠稳定顺序才成立。
//
// 本文件**不做抽取**：怎么抽是各玩法自己的事（日报要确定性种子流以便跨天补发抽到同一件，
// 远征则是结算时抽一次就写进记录）。这里只负责"有哪些候选"。
//
// 备注（2026-09-18）：`Integration/DailyReport/DailyReportRewards.cs` 目前仍持有一份等价的
// 私有实现（该文件在本轮期间由另一会话在改，按仓库多会话纪律未动它）。两边口径一致；
// 日报那一侧稳定后把它的 GetCandidates/BuildCandidates 换成调用本类即可，届时删私有副本。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>按品质缓存的官方物品候选池。无实例，只有缓存。</summary>
    internal static class BossRushQualityItemPool
    {
        private const string LogPrefix = "[LootPool] ";

        /// <summary>按品质缓存的候选物品 id 表。全表扫描不能每次发奖都跑。</summary>
        private static readonly Dictionary<int, int[]> _candidateCache = new Dictionary<int, int[]>();

        private static readonly object _lock = new object();

        /// <summary>
        /// 取某品质的候选 id 表（已过黑名单、已排序）。**非空结果**才缓存。
        /// 永不返回 null；池不可用时返回空数组，由调用方 fail-closed。
        /// </summary>
        internal static int[] GetCandidates(int quality)
        {
            lock (_lock)
            {
                int[] cached;
                if (_candidateCache.TryGetValue(quality, out cached)) return cached;
            }

            int[] built = BuildCandidates(quality);
            if (built == null || built.Length <= 0) return new int[0];

            lock (_lock)
            {
                _candidateCache[quality] = built;
            }
            return built;
        }

        private static int[] BuildCandidates(int quality)
        {
            try
            {
                if (ItemAssetsCollection.Instance == null) return new int[0];

                LootBlacklistRegistry.EnsureInitialized();

                // 全表按品质取：requireTags 留空，只卡品质区间。
                ItemFilter filter = new ItemFilter();
                filter.requireTags = null;
                filter.minQuality = quality;
                filter.maxQuality = quality;
                filter.caliber = string.Empty;

                // 精确过滤入口，不走 Search 的降品质兜底
                int[] raw = ItemAssetsCollection.GetAllTypeIds(filter);
                if (raw == null || raw.Length <= 0) return new int[0];

                List<int> safe = new List<int>(raw.Length);
                for (int i = 0; i < raw.Length; i++)
                {
                    int id = raw[i];
                    if (id <= 0) continue;
                    if (LootBlacklistRegistry.Contains(id)) continue;
                    safe.Add(id);
                }

                // 官方动态表 / HashSet 的枚举顺序不稳定，排序后同一 seed 才有同一结果
                safe.Sort();
                ModBehaviour.DevLog(LogPrefix + "品质 " + quality
                    + " 候选池：" + safe.Count + " 件（原始 " + raw.Length + " 件）");
                return safe.ToArray();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix
                    + "[WARNING] 构建品质 " + quality + " 候选池失败: " + e.Message);
                return new int[0];
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _candidateCache.Clear();
            }
        }
    }
}

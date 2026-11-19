// ============================================================================
// CollectibleLootRoller.cs - 收集品抽签（纯逻辑，不碰 Unity，离线回归 CollectibleLoot 直接编译它）
// ============================================================================
// 一次开箱只调一次 Roll：先按箱子档次决定出不出，再按档次权重抽品质，最后在
// 「本图的本地系列 + 两个全图系列 + 珍品组」里挑这一档的一件。某档没有候选（被上限截掉、
// 或本图没有本地系列）时权重直接作废，不会抽空。候选表按需建一次，之后只读。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 一趟出击的开箱账本：每个库存只判一次，交付满 <see cref="CollectibleTuning.PerRaidCap"/> 件后不再出。
    /// 纯逻辑，换一趟（关卡 owner 变化）由调用方 Reset。
    /// </summary>
    internal sealed class CollectibleRaidLedger
    {
        private readonly HashSet<int> judged = new HashSet<int>();

        internal int Delivered { get; private set; }

        internal bool CapReached { get { return Delivered >= CollectibleTuning.PerRaidCap; } }

        /// <summary>这只箱子能不能判：官方已经搜过的、本趟判过的、已到上限的都不判。判过就记下，不管中没中。</summary>
        internal bool TryClaim(int inventoryId, bool alreadyInspected)
        {
            if (alreadyInspected) return false;
            if (!judged.Add(inventoryId)) return false;
            return !CapReached;
        }

        internal void MarkDelivered() { Delivered++; }

        internal void Reset()
        {
            judged.Clear();
            Delivered = 0;
        }
    }

    internal static class CollectibleLootRoller
    {
        /// <summary>[series][quality] → TypeID 列表；全图与珍品合并在 GlobalByQuality。</summary>
        private static List<int>[][] _localByQuality;
        private static List<int>[] _globalByQuality;

        private static void EnsureTables()
        {
            if (_globalByQuality != null) return;
            int seriesCount = CollectibleCatalog.SeriesCount;
            var local = new List<int>[seriesCount][];
            for (int s = 0; s < seriesCount; s++)
            {
                local[s] = new List<int>[CollectibleTuning.MaxQuality + 1];
                for (int q = 0; q <= CollectibleTuning.MaxQuality; q++) local[s][q] = new List<int>();
            }
            var global = new List<int>[CollectibleTuning.MaxQuality + 1];
            for (int q = 0; q <= CollectibleTuning.MaxQuality; q++) global[q] = new List<int>();

            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def == null || def.Quality < 1 || def.Quality > CollectibleTuning.MaxQuality) continue;
                CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(def.Series);
                if (info == null) continue;
                if (info.Local) local[(int)def.Series][def.Quality].Add(def.TypeId);
                else global[def.Quality].Add(def.TypeId);
            }
            _localByQuality = local;
            _globalByQuality = global;
        }

        internal static void ResetStaticCaches()
        {
            _localByQuality = null;
            _globalByQuality = null;
        }

        /// <summary>这只箱子出现收集品的概率（已乘倍率、夹到 0..1）。</summary>
        internal static float ChanceFor(CollectibleBoxTier tier, float chanceMultiplier)
        {
            float baseChance = tier == CollectibleBoxTier.High ? CollectibleTuning.HighBoxChance : CollectibleTuning.NormalBoxChance;
            float chance = baseChance * Math.Max(0f, chanceMultiplier);
            return chance < 0f ? 0f : (chance > 1f ? 1f : chance);
        }

        /// <summary>
        /// 抽一次。<paramref name="next"/> 返回 [0,1) 的随机数；<paramref name="localSeries"/> 为空表示本图没有本地系列。
        /// <paramref name="registered"/> 是「这件已登记」的查询，给了就按新件优先挑（见 <see cref="PickInQuality"/>）。
        /// 返回 TypeID，不出返回 0。
        /// </summary>
        internal static int Roll(CollectibleBoxTier tier, CollectibleSeries? localSeries, int qualityCap, float chanceMultiplier, Func<double> next,
            Func<int, bool> registered = null)
        {
            if (next == null) return 0;
            if (next() >= ChanceFor(tier, chanceMultiplier)) return 0;
            int quality = RollQuality(tier, localSeries, qualityCap, next);
            return quality <= 0 ? 0 : PickInQuality(quality, localSeries, next, registered);
        }

        /// <summary>已经决定「出」之后抽品质（F3「下一只箱子必出」与回归共用）。</summary>
        internal static int RollQuality(CollectibleBoxTier tier, CollectibleSeries? localSeries, int qualityCap, Func<double> next)
        {
            EnsureTables();
            float[] weights = tier == CollectibleBoxTier.High ? CollectibleTuning.HighQualityWeights : CollectibleTuning.NormalQualityWeights;
            int cap = Math.Min(qualityCap, CollectibleTuning.MaxQuality);
            double total = 0d;
            for (int q = 1; q <= cap; q++)
            {
                if (HasCandidate(q, localSeries)) total += weights[q];
            }
            if (total <= 0d) return 0;
            double roll = next() * total;
            for (int q = 1; q <= cap; q++)
            {
                if (!HasCandidate(q, localSeries)) continue;
                roll -= weights[q];
                if (roll < 0d) return q;
            }
            // 浮点尾差：落到最后一个有候选的档
            for (int q = cap; q >= 1; q--)
            {
                if (HasCandidate(q, localSeries) && weights[q] > 0f) return q;
            }
            return 0;
        }

        /// <summary>
        /// 在已经抽中的品质里挑一件。新件优先（CR-2026-11-19-007）：给了 <paramref name="registered"/> 时，
        /// 有 <see cref="CollectibleTuning.NewItemPreference"/> 的机会只在没登记过的件里挑，这一档全登记过就照常挑。
        /// 只换「挑哪一件」，不碰出现率和品质分布。
        /// </summary>
        internal static int PickInQuality(int quality, CollectibleSeries? localSeries, Func<double> next, Func<int, bool> registered = null)
        {
            EnsureTables();
            List<int> local = LocalList(quality, localSeries);
            List<int> global = _globalByQuality[quality];
            if (registered != null && next() < CollectibleTuning.NewItemPreference)
            {
                List<int> freshLocal = Unregistered(local, registered);
                List<int> freshGlobal = Unregistered(global, registered);
                if (freshLocal.Count > 0 || freshGlobal.Count > 0)
                {
                    local = freshLocal;
                    global = freshGlobal;
                }
            }
            List<int> chosen;
            if (local.Count > 0 && global.Count > 0)
                chosen = next() < CollectibleTuning.LocalSeriesShare ? local : global;
            else
                chosen = local.Count > 0 ? local : global;
            if (chosen.Count == 0) return 0;
            int index = (int)(next() * chosen.Count);
            if (index >= chosen.Count) index = chosen.Count - 1;
            return chosen[index];
        }

        private static bool HasCandidate(int quality, CollectibleSeries? localSeries)
        {
            return _globalByQuality[quality].Count > 0 || LocalList(quality, localSeries).Count > 0;
        }

        /// <summary>只在真出一件收集品时调，新建一份小列表，不改共享候选表。</summary>
        private static List<int> Unregistered(List<int> source, Func<int, bool> registered)
        {
            var fresh = new List<int>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (!registered(source[i])) fresh.Add(source[i]);
            }
            return fresh;
        }

        private static readonly List<int> Empty = new List<int>();

        private static List<int> LocalList(int quality, CollectibleSeries? localSeries)
        {
            if (!localSeries.HasValue) return Empty;
            int s = (int)localSeries.Value;
            if (s < 0 || s >= _localByQuality.Length) return Empty;
            return _localByQuality[s][quality];
        }
    }
}

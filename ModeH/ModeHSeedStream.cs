using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// Mode H 确定性随机（设计提案 §21.1）。
    ///
    /// 冻结规则：
    /// - 所有抽样通过 ModeHSeedStream(runSeed, domain, sequence) 生成，不同 domain 相互独立；
    /// - 算法为 UTF-8 FNV-1a 64 派生 + SplitMix64 前进，全部 unchecked ulong；
    /// - 禁止调用 Unity 全局随机作为结算来源；
    /// - 保存 planId、sequence、侦察结果和最终列表后，重载只重放已保存结果；
    /// - 概率判定一律整数量化，禁浮点直乘产生平台差异。
    /// </summary>
    public struct ModeHSeedStream
    {
        #region 冻结算法常量

        /// <summary>UTF-8 FNV-1a 64 offset basis。</summary>
        private const ulong FnvOffsetBasis = 14695981039346656037UL;

        /// <summary>UTF-8 FNV-1a 64 prime。</summary>
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>SplitMix64 增量（2^64 / 黄金比例，向下取整）。</summary>
        private const ulong SplitMix64Increment = 0x9E3779B97F4A7C15UL;

        /// <summary>概率量化基数（百万分之一精度）。</summary>
        public const int ProbabilityQuantizationBasis = 1000000;

        #endregion

        #region domain 常量

        /// <summary>相互独立的随机域（§21.1）。</summary>
        public static class Domains
        {
            /// <summary>五席试棚候选与展示顺序。</summary>
            public const string Draft = "draft";
            /// <summary>落选回响三路分流洗牌。</summary>
            public const string Echo = "echo";
            /// <summary>敌军计划生成。</summary>
            public const string EncounterPlan = "encounter_plan";
            /// <summary>侦察结果。</summary>
            public const string Recon = "recon";
            /// <summary>战痕候选。</summary>
            public const string Scar = "scar";
            /// <summary>虚拟奖励候选。</summary>
            public const string Reward = "reward";
            /// <summary>真实资产路径的预冻结损失列表。</summary>
            public const string PlannedLoss = "planned_loss";
            /// <summary>胆怯判定。</summary>
            public const string Coward = "coward";
            /// <summary>ERROR 判定。</summary>
            public const string Error = "error";
            /// <summary>看台表演点位。</summary>
            public const string StandIn = "stand_in";
            /// <summary>转会市场。</summary>
            public const string Transfer = "transfer";
        }

        #endregion

        #region 状态

        private ulong _state;

        #endregion

        #region 构造

        /// <summary>
        /// 由 (runSeed, domain, sequence) 派生一条独立随机流。
        /// 同一组三元组永远得到同一序列，因此重载可以重放已保存结果。
        /// </summary>
        public static ModeHSeedStream Create(long runSeed, string domain, int sequence)
        {
            ModeHSeedStream stream = new ModeHSeedStream();
            stream._state = DeriveSeed(runSeed, domain, sequence);
            return stream;
        }

        /// <summary>派生初始 state（可用于持久化 planSeed 等派生 seed）。</summary>
        public static ulong DeriveSeed(long runSeed, string domain, int sequence)
        {
            unchecked
            {
                ulong hash = Fnv1a64(domain != null ? domain : string.Empty);
                hash ^= (ulong)runSeed;
                hash *= FnvPrime;
                hash ^= (ulong)(uint)sequence;
                hash *= FnvPrime;
                return hash;
            }
        }

        /// <summary>UTF-8 FNV-1a 64 位哈希。</summary>
        public static ulong Fnv1a64(string input)
        {
            if (input == null) input = string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            unchecked
            {
                ulong hash = FnvOffsetBasis;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        #endregion

        #region 基础取值

        /// <summary>SplitMix64 单步前进。</summary>
        public ulong NextUInt64()
        {
            unchecked
            {
                _state += SplitMix64Increment;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>
        /// [0, maxExclusive) 无偏整数。使用 rejection sampling 避免模偏差；
        /// maxExclusive 非正时返回 0。
        /// </summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            unchecked
            {
                ulong bound = (ulong)maxExclusive;
                ulong limit = ulong.MaxValue - (ulong.MaxValue % bound) - 1UL;
                while (true)
                {
                    ulong raw = NextUInt64();
                    if (raw <= limit)
                    {
                        return (int)(raw % bound);
                    }
                }
            }
        }

        /// <summary>[minInclusive, maxInclusive] 无偏整数。</summary>
        public int NextIntInclusive(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            long span = (long)maxInclusive - minInclusive + 1L;
            if (span > int.MaxValue) span = int.MaxValue;
            return minInclusive + NextInt((int)span);
        }

        /// <summary>
        /// 按公开概率做一次判定。概率先量化到百万分之一整数，禁浮点直乘。
        /// probability 小于等于 0 恒 false，大于等于 1 恒 true。
        /// </summary>
        public bool NextChance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            int threshold = (int)Math.Round(probability * ProbabilityQuantizationBasis, MidpointRounding.AwayFromZero);
            if (threshold <= 0) return false;
            if (threshold >= ProbabilityQuantizationBasis) return true;
            return NextInt(ProbabilityQuantizationBasis) < threshold;
        }

        /// <summary>Fisher-Yates 洗牌（原地，确定性）。</summary>
        public void Shuffle<T>(IList<T> items)
        {
            if (items == null || items.Count < 2) return;
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                if (i == j) continue;
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        /// <summary>
        /// 整数权重无偏抽样，返回下标；权重全为非正或列表为空时返回 -1。
        /// 权重必须是已量化的整数，禁止传浮点权重。
        /// </summary>
        public int PickWeightedIndex(IList<int> weights)
        {
            if (weights == null || weights.Count == 0) return -1;
            long total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = weights[i];
                if (w > 0) total += w;
            }
            if (total <= 0) return -1;
            if (total > int.MaxValue) total = int.MaxValue;
            int roll = NextInt((int)total);
            long accumulated = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = weights[i];
                if (w <= 0) continue;
                accumulated += w;
                if (roll < accumulated) return i;
            }
            return weights.Count - 1;
        }

        /// <summary>从列表中确定性取一个元素；列表为空返回 default。</summary>
        public T Pick<T>(IList<T> items)
        {
            if (items == null || items.Count == 0) return default(T);
            return items[NextInt(items.Count)];
        }

        /// <summary>
        /// 从已排序候选中取 count 个不重复元素，保持抽取顺序确定；
        /// count 大于等于候选数时返回全部候选的洗牌结果。
        /// </summary>
        public List<T> TakeDistinct<T>(IList<T> candidates, int count)
        {
            List<T> result = new List<T>();
            if (candidates == null || candidates.Count == 0 || count <= 0) return result;
            List<T> pool = new List<T>(candidates);
            Shuffle(pool);
            int take = count < pool.Count ? count : pool.Count;
            for (int i = 0; i < take; i++)
            {
                result.Add(pool[i]);
            }
            return result;
        }

        #endregion
    }
}

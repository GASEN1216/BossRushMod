using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// Mode G 确定性随机系统（C5 裁决重写版）。方案文档 §3.2（169-187 行）。
    ///
    /// 冻结算法核心：UTF-8 FNV-1a 64 + SplitMix64，全部 unchecked ulong。
    /// 玩法 seed 禁止运行时非确定性随机源和平台相关哈希。
    /// 八个 domain stream 相互独立，各领域独立实例互不消费序列。
    /// 加权抽样一律整数量化后无偏整数累计抽取，禁浮点直乘。
    /// </summary>
    public static class ModeGDeterministicRandom
    {
        #region FNV-1a 64（算法核心保留）

        /// <summary>
        /// UTF-8 FNV-1a 64 位哈希。逐字节 xor 后 unchecked 乘，无大小写/normalization。
        /// </summary>
        public static ulong Fnv1a64(string input)
        {
            if (input == null) input = string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            unchecked
            {
                ulong hash = ModeGAvailability.FnvOffsetBasis;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= ModeGAvailability.FnvPrime;
                }
                return hash;
            }
        }

        #endregion

        #region SplitMix64（算法核心保留）

        /// <summary>
        /// SplitMix64 单步：state += 增量，返回 gamma 变换。全部 unchecked。
        /// </summary>
        public static ulong SplitMix64Next(ref ulong state)
        {
            unchecked
            {
                state += ModeGAvailability.SplitMix64Increment;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>
        /// [0, maxExclusive) 无偏整数：rejection sampling，避免模偏差。
        /// </summary>
        public static int NextInt(ref ulong state, int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;
            unchecked
            {
                ulong umax = (ulong)maxExclusive;
                ulong threshold = ulong.MaxValue - (ulong.MaxValue % umax);
                ulong raw = SplitMix64Next(ref state);
                while (raw >= threshold)
                {
                    raw = SplitMix64Next(ref state);
                }
                return (int)(raw % umax);
            }
        }

        #endregion

        #region Golden Vectors（独立脚本复算固化，禁由被测实现自产 expected）

        // FNV-1a 64 golden vectors（UTF-8 原始字节）
        public const ulong GoldenFnv_Empty = 0xcbf29ce484222325UL;
        public const ulong GoldenFnv_BossRush = 0x2ab0caa511720f06UL;
        public const ulong GoldenFnv_SuMingHuiXiang = 0x5898876a11981479UL; // "宿命回响"

        // SplitMix64 state=0 前五次输出
        public const ulong GoldenSplitMix_0 = 0xe220a8397b1dcdafUL;
        public const ulong GoldenSplitMix_1 = 0x6e789e6aa1b965f4UL;
        public const ulong GoldenSplitMix_2 = 0x06c45d188009454fUL;
        public const ulong GoldenSplitMix_3 = 0xf88bb8a8724c81ecUL;
        public const ulong GoldenSplitMix_4 = 0x1b39896a51a8749bUL;

        /// <summary>
        /// golden vectors 自检：实现与 guard 常量交叉验证。
        /// Starting 预检调用一次；任何失败即 fail-closed。
        /// </summary>
        public static bool ValidateGoldenVectors()
        {
            if (Fnv1a64(string.Empty) != GoldenFnv_Empty) return false;
            if (Fnv1a64("BossRush") != GoldenFnv_BossRush) return false;
            if (Fnv1a64("宿命回响") != GoldenFnv_SuMingHuiXiang) return false;

            ulong s = 0UL;
            if (SplitMix64Next(ref s) != GoldenSplitMix_0) return false;
            if (SplitMix64Next(ref s) != GoldenSplitMix_1) return false;
            if (SplitMix64Next(ref s) != GoldenSplitMix_2) return false;
            if (SplitMix64Next(ref s) != GoldenSplitMix_3) return false;
            if (SplitMix64Next(ref s) != GoldenSplitMix_4) return false;
            return true;
        }

        #endregion

        #region Domain Constants（C5：8 个 ASCII 冻结常量，替代旧 DomainSalts）

        /// <summary>
        /// 八个 domain 的 ASCII 冻结常量（规格第五节）。只追加不修改。
        /// </summary>
        public static class DomainConstants
        {
            /// <summary>"BOSSPLAN"</summary>
            public const ulong BossPlan = 0x424F5353504C414EUL;
            /// <summary>"VARIANT!"</summary>
            public const ulong Variant = 0x56415249414E5421UL;
            /// <summary>"AMMOBAN!"</summary>
            public const ulong AmmoBan = 0x414D4D4F42414E21UL;
            /// <summary>"REWARD!!"</summary>
            public const ulong Reward = 0x5245574152442121UL;
            /// <summary>"CONTRACT"</summary>
            public const ulong Contract = 0x434F4E5452414354UL;
            /// <summary>"TEMPERAM"</summary>
            public const ulong Temperament = 0x54454D504552414DUL;
            /// <summary>"REMATCH!"</summary>
            public const ulong RematchComposition = 0x52454D4154434821UL;
            /// <summary>"REROLL!!"</summary>
            public const ulong Reroll = 0x5245524F4C4C2121UL;
        }

        #endregion

        #region Seed Derivation（规格第五节三步公式）

        /// <summary>processNonce 混合常量 "MODEGNON"</summary>
        private const ulong ProcessNonceMix = 0x4D4F4445474E4F4EUL;
        /// <summary>session seed 混合乘数（规格第五节第 2 步）</summary>
        private const ulong SessionMixMultiplier = 0xD1342543DE82EF95UL;

        private static ulong _processNonce;
        private static bool _processNonceInitialized;
        private static readonly object _processLock = new object();

        /// <summary>
        /// processNonce：主线程一次。
        /// state = UtcNow.Ticks ^ (TickCount &lt;&lt; 32) ^ 0x4D4F4445474E4F4E，调一次 SplitMix64。
        /// </summary>
        public static ulong GetProcessNonce()
        {
            lock (_processLock)
            {
                if (!_processNonceInitialized)
                {
                    unchecked
                    {
                        ulong state = (ulong)DateTime.UtcNow.Ticks
                            ^ ((ulong)(uint)Environment.TickCount << 32)
                            ^ ProcessNonceMix;
                        _processNonce = SplitMix64Next(ref state);
                    }
                    _processNonceInitialized = true;
                }
                return _processNonce;
            }
        }

        /// <summary>
        /// runSeed 派生（规格第五节第 2 步）：
        /// seedState = processNonce ^ unchecked(sessionCounter * 0xD1342543DE82EF95)，调一次 SplitMix64。
        /// sessionCounter 由调用方（Entry preview 创建）递增传入。
        /// </summary>
        public static ulong DeriveRunSeed(long sessionCounter)
        {
            unchecked
            {
                ulong state = GetProcessNonce() ^ ((ulong)sessionCounter * SessionMixMultiplier);
                return SplitMix64Next(ref state);
            }
        }

        /// <summary>
        /// 领域流初始 state（规格第五节第 3 步）：
        /// domainState = runSeed ^ domainConstant ^ FNV1a64(presetKey ?? "") ^ ((ulong)(uint)waveEpoch &lt;&lt; 32)，
        /// 再调一次 SplitMix64。各领域独立实例互不消费序列。
        /// </summary>
        public static ulong SeedDomain(ulong runSeed, ulong domainConstant, string presetKey, int waveEpoch)
        {
            unchecked
            {
                ulong state = runSeed
                    ^ domainConstant
                    ^ Fnv1a64(presetKey ?? string.Empty)
                    ^ ((ulong)(uint)waveEpoch << 32);
                return SplitMix64Next(ref state);
            }
        }

        /// <summary>
        /// 领域流初始 state（无 preset 绑定场景：全局编排/契约/性格等）。
        /// </summary>
        public static ulong SeedDomain(ulong runSeed, ulong domainConstant, int waveEpoch)
        {
            return SeedDomain(runSeed, domainConstant, string.Empty, waveEpoch);
        }

        #endregion

        #region Integer-Quantized Weighted Sampling（禁浮点直乘）

        /// <summary>加权整数量化下界</summary>
        public const int WeightQuantumMin = 1;
        /// <summary>加权整数量化上界（threatShare * 1,000,000）</summary>
        public const int WeightQuantumMax = 1000000;

        /// <summary>
        /// 威胁份额整数量化：clamp(1, 1000000, round(share * 1000000, AwayFromZero))。
        /// share &lt;= 0 视为最小权重 1（不参与零权重跳过语义，由调用方预过滤）。
        /// </summary>
        public static int QuantizeThreatWeight(double share)
        {
            if (double.IsNaN(share) || double.IsInfinity(share) || share <= 0.0) return WeightQuantumMin;
            double scaled = share * WeightQuantumMax;
            if (scaled >= WeightQuantumMax) return WeightQuantumMax;
            long rounded = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
            if (rounded < WeightQuantumMin) return WeightQuantumMin;
            if (rounded > WeightQuantumMax) return WeightQuantumMax;
            return (int)rounded;
        }

        /// <summary>
        /// 加权选择（整数权重版）：无偏整数累计抽取。
        /// weights 为已量化整数权重；total 溢出由 clamp 上界与条数上限约束（调用方保证 count * 10^6 &lt; long 上限）。
        /// </summary>
        public static int WeightedSelect(ref ulong state, IList<int> quantizedWeights)
        {
            if (quantizedWeights == null || quantizedWeights.Count == 0) return 0;
            int count = quantizedWeights.Count;

            long total = 0;
            for (int i = 0; i < count; i++)
            {
                int w = quantizedWeights[i];
                if (w < WeightQuantumMin) w = WeightQuantumMin;
                total += w;
            }
            if (total <= 0) return NextInt(ref state, count);

            // 无偏整数抽取：在 [0, total) 内均匀取一个 long
            long pick = NextLongBounded(ref state, total);
            long cursor = 0;
            for (int i = 0; i < count; i++)
            {
                int w = quantizedWeights[i];
                if (w < WeightQuantumMin) w = WeightQuantumMin;
                cursor += w;
                if (pick < cursor) return i;
            }
            return count - 1;
        }

        /// <summary>
        /// [0, bound) 无偏 long：rejection sampling。
        /// </summary>
        private static long NextLongBounded(ref ulong state, long bound)
        {
            if (bound <= 1) return 0;
            unchecked
            {
                ulong ubound = (ulong)bound;
                ulong threshold = ulong.MaxValue - (ulong.MaxValue % ubound);
                ulong raw = SplitMix64Next(ref state);
                while (raw >= threshold)
                {
                    raw = SplitMix64Next(ref state);
                }
                return (long)(raw % ubound);
            }
        }

        #endregion

        #region Stable Key / Ordering Helpers

        /// <summary>
        /// 对 TypeID 列表升序去重（奖励 DTO 按 TypeID 升序；契约/性格按稳定整数 ID 升序）。
        /// </summary>
        public static int[] SortTypeIdsStable(IList<int> typeIds)
        {
            if (typeIds == null || typeIds.Count == 0) return new int[0];
            int[] result = new int[typeIds.Count];
            for (int i = 0; i < typeIds.Count; i++) result[i] = typeIds[i];
            Array.Sort(result);
            // 去重
            int write = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (write == 0 || result[i] != result[write - 1])
                {
                    result[write++] = result[i];
                }
            }
            if (write == result.Length) return result;
            int[] trimmed = new int[write];
            Array.Copy(result, trimmed, write);
            return trimmed;
        }

        /// <summary>
        /// 对字符串序列做 Ordinal 稳定排序（Boss 池快照 nameKey 排序用）。
        /// </summary>
        public static string[] SortStringsOrdinal(IList<string> values)
        {
            if (values == null || values.Count == 0) return new string[0];
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i];
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        #endregion
    }
}

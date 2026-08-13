using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// Mode G 运行形态（C4 裁决：固定九波）。
    /// </summary>
    public enum ModeGRunFormat
    {
        /// <summary>首胜叙事：三个署名 Boss（波 3/6/9）</summary>
        FirstClearNarrative,
        /// <summary>复战混编：保留 2 署名 + 1 官方 wildcard</summary>
        RematchMix
    }

    /// <summary>
    /// Mode G 波次槽位类别。
    /// </summary>
    public enum ModeGSlotKind
    {
        /// <summary>官方 Boss 池抽取</summary>
        Official,
        /// <summary>署名 Boss（托管三 Boss 之一）</summary>
        Signature,
        /// <summary>宿敌波（宿敌记录指定或署名/wildcard）</summary>
        Nemesis
    }

    /// <summary>
    /// Mode G 九波不可变计划（C4 裁决重写版）。方案文档 §6/§7。
    ///
    /// 固定九波节拍：1/2/1宿敌/1/3/1宿敌/1/3/1宿敌。
    /// 署名槽/宿敌槽/reserve 6 key 全局互斥（同 key 不得跨类别重复出现）。
    /// 休整：普通 8s、第 3/6 波后 20s（owner 冻结值）。
    /// 首胜 3 署名 Boss；复战 RematchMix 保留 2 + 官方 wildcard。
    /// planFingerprint 完整重复时专用 Reroll domain 最多重建一次。
    /// 在 Starting 阶段从 runSeed 确定性生成，Active 阶段只读。
    /// </summary>
    public sealed class ModeGWavePlan
    {
        #region Frozen Tempo Constants

        /// <summary>固定九波</summary>
        public const int WaveCount = 9;

        /// <summary>休整时长：普通波后 8 秒（owner 冻结值，§7）</summary>
        public const float IntermissionNormalSeconds = 8f;
        /// <summary>休整时长：第 3/6 波后 20 秒（owner 冻结值，§7）</summary>
        public const float IntermissionLongSeconds = 20f;

        /// <summary>reserve key 数量（6-key 全局互斥成员之一）</summary>
        public const int ReserveKeyCount = 6;

        /// <summary>每波 Boss 数量节拍：1/2/1宿敌/1/3/1宿敌/1/3/1宿敌</summary>
        private static readonly int[] WaveBossCounts = { 1, 2, 1, 1, 3, 1, 1, 3, 1 };
        /// <summary>宿敌波（第 3/6/9 波，0-based 索引 2/5/8）</summary>
        private static readonly bool[] NemesisWaveFlags = { false, false, true, false, false, true, false, false, true };

        #endregion

        #region Wave Slot

        /// <summary>
        /// 单个波次描述（不可变）。
        /// </summary>
        public sealed class WaveSlot
        {
            public readonly int waveIndex;               // 0-8
            public readonly int actIndex;                // 0/1/2
            public readonly int bossCount;               // 节拍冻结值
            public readonly bool isNemesisWave;          // 第 3/6/9 波
            public readonly ModeGSlotKind slotKind;      // Official/Signature/Nemesis
            public readonly string[] bossPresetKeys;     // 长度 == bossCount，键互斥
            public readonly ModeGPlanVariant variant;    // Split/Pincer/Arc 编排包

            public WaveSlot(int waveIndex, int actIndex, int bossCount, bool isNemesisWave,
                ModeGSlotKind slotKind, string[] bossPresetKeys, ModeGPlanVariant variant)
            {
                this.waveIndex = waveIndex;
                this.actIndex = actIndex;
                this.bossCount = bossCount;
                this.isNemesisWave = isNemesisWave;
                this.slotKind = slotKind;
                this.bossPresetKeys = bossPresetKeys;
                this.variant = variant;
            }
        }

        #endregion

        public readonly ulong runSeed;
        public readonly ModeGRunFormat runFormat;
        public readonly WaveSlot[] waves;                        // 固定 9 个
        public readonly string[] reserveKeys;                  // reserve 6 key（与全部槽位 key 互斥）
        public readonly int rematchCompositionId;              // RematchMix 时被替换署名槽索引（-1 = 首胜）
        public readonly string planFingerprint;                // §3.2 冻结组合

        private ModeGWavePlan(ulong runSeed, ModeGRunFormat runFormat, WaveSlot[] waves,
            string[] reserveKeys, int rematchCompositionId, string planFingerprint)
        {
            this.runSeed = runSeed;
            this.runFormat = runFormat;
            this.waves = waves;
            this.reserveKeys = reserveKeys;
            this.rematchCompositionId = rematchCompositionId;
            this.planFingerprint = planFingerprint;
        }

        #region Queries

        public WaveSlot GetWave(int waveIndex)
        {
            if (waveIndex < 0 || waveIndex >= WaveCount) return null;
            return waves[waveIndex];
        }

        /// <summary>
        /// 波 w（0-based）结束后的休整时长。第 9 波后为 0（进入终局）。
        /// </summary>
        public static float GetIntermissionDuration(int waveIndex)
        {
            if (waveIndex < 0 || waveIndex >= WaveCount - 1) return 0f;
            return (waveIndex == 2 || waveIndex == 5) ? IntermissionLongSeconds : IntermissionNormalSeconds;
        }

        public int TotalBossCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < waves.Length; i++) total += waves[i].bossCount;
                return total;
            }
        }

        #endregion

        #region Build（确定性，Starting 一次）

        /// <summary>
        /// 从 runSeed 确定性生成九波计划。失败（池不足/互斥失败）返回 null，调用方 fail-closed。
        /// </summary>
        /// <param name="runSeed">preview 冻结的 runSeed</param>
        /// <param name="runFormat">首胜叙事 / 复战混编</param>
        /// <param name="signatureKeys">eligible 署名 Boss key（升序快照）；首胜须 >=3</param>
        /// <param name="officialKeys">官方 Boss 池 run-scoped 快照 key（Ordinal 排序）</param>
        /// <param name="selectedFateContractId">入口冻结契约 ID</param>
        /// <param name="nemesisTemperamentId">宿敌性格 ID</param>
        /// <param name="seenFingerprints">同进程已出现 fingerprint（可为 null）</param>
        public static ModeGWavePlan Build(
            ulong runSeed,
            ModeGRunFormat runFormat,
            IList<string> signatureKeys,
            IList<string> officialKeys,
            int selectedFateContractId,
            int nemesisTemperamentId,
            HashSet<string> seenFingerprints)
        {
            ModeGWavePlan plan = BuildCore(runSeed, runFormat, signatureKeys, officialKeys,
                selectedFateContractId, nemesisTemperamentId, 0);
            if (plan == null) return null;

            // 同进程完整 fingerprint 重复：专用 Reroll domain 最多重建一次
            if (seenFingerprints != null && seenFingerprints.Contains(plan.planFingerprint))
            {
                ModeGWavePlan rerolled = BuildCore(runSeed, runFormat, signatureKeys, officialKeys,
                    selectedFateContractId, nemesisTemperamentId, 1);
                if (rerolled != null) return rerolled;
            }
            return plan;
        }

        private static ModeGWavePlan BuildCore(
            ulong runSeed,
            ModeGRunFormat runFormat,
            IList<string> signatureKeys,
            IList<string> officialKeys,
            int selectedFateContractId,
            int nemesisTemperamentId,
            int rerollEpoch)
        {
            int signatureCount = signatureKeys != null ? signatureKeys.Count : 0;
            int officialCount = officialKeys != null ? officialKeys.Count : 0;

            // 首胜须 3 署名；复战须 2 署名。池不足 fail-closed。
            int requiredSignature = (runFormat == ModeGRunFormat.FirstClearNarrative) ? 3 : 2;
            if (signatureCount < requiredSignature) return null;
            // 官方池需足够填充非署名波总量（14 只）+ reserve 6 + wildcard（复战 1）
            int officialWaveTotal = WaveBossCounts[0] + WaveBossCounts[1] + WaveBossCounts[3]
                + WaveBossCounts[4] + WaveBossCounts[6] + WaveBossCounts[7];
            if (runFormat == ModeGRunFormat.RematchMix) officialWaveTotal += 1; // 宿敌波 wildcard
            if (officialCount < officialWaveTotal + ReserveKeyCount) return null;

            // ---- domain streams（rerollEpoch 注入 waveEpoch 位）----
            ulong bossState = ModeGDeterministicRandom.SeedDomain(runSeed,
                ModeGDeterministicRandom.DomainConstants.BossPlan, string.Empty, rerollEpoch);
            ulong variantState = ModeGDeterministicRandom.SeedDomain(runSeed,
                ModeGDeterministicRandom.DomainConstants.Variant, string.Empty, rerollEpoch);
            ulong rematchState = ModeGDeterministicRandom.SeedDomain(runSeed,
                ModeGDeterministicRandom.DomainConstants.RematchComposition, string.Empty, rerollEpoch);
            ulong rerollState = ModeGDeterministicRandom.SeedDomain(runSeed,
                ModeGDeterministicRandom.DomainConstants.Reroll, string.Empty, rerollEpoch);
            if (rerollEpoch > 0)
            {
                // Reroll 流消费一次推进 bossState 起点，避免与首次构建同序
                bossState ^= ModeGDeterministicRandom.SplitMix64Next(ref rerollState);
            }

            // ---- 署名槽分配（波 3/6/9）----
            string[] shuffledSignature = Shuffle(signatureKeys, ref bossState);
            int rematchCompositionId = -1;
            string[] nemesisWaveKeys = new string[3];
            for (int i = 0; i < 3; i++) nemesisWaveKeys[i] = shuffledSignature[i];

            // ---- 官方 key 无重复消费序列 ----
            string[] shuffledOfficial = Shuffle(officialKeys, ref bossState);
            int officialCursor = 0;
            HashSet<string> usedKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 3; i++) usedKeys.Add(nemesisWaveKeys[i]);

            // RematchMix：保留 2 署名 + 官方 wildcard 替换第 3 个署名槽
            if (runFormat == ModeGRunFormat.RematchMix)
            {
                rematchCompositionId = ModeGDeterministicRandom.NextInt(ref rematchState, 3);
                string wildcard = null;
                while (officialCursor < shuffledOfficial.Length)
                {
                    string candidate = shuffledOfficial[officialCursor++];
                    if (!usedKeys.Contains(candidate)) { wildcard = candidate; break; }
                }
                if (wildcard == null) return null;
                usedKeys.Add(wildcard);
                nemesisWaveKeys[rematchCompositionId] = wildcard;
            }

            // ---- 逐波装配 ----
            WaveSlot[] waves = new WaveSlot[WaveCount];
            int nemesisCursor = 0;
            for (int w = 0; w < WaveCount; w++)
            {
                int act = w / 3;
                int count = WaveBossCounts[w];
                bool isNemesis = NemesisWaveFlags[w];
                ModeGPlanVariant variant = SelectVariant(count, ref variantState);

                string[] keys = new string[count];
                ModeGSlotKind kind;
                if (isNemesis)
                {
                    kind = ModeGSlotKind.Nemesis;
                    string mainKey = nemesisWaveKeys[nemesisCursor++];
                    keys[0] = mainKey;
                    // 宿敌波 count 固定为 1（节拍冻结），无副槽
                }
                else
                {
                    kind = ModeGSlotKind.Official;
                    for (int i = 0; i < count; i++)
                    {
                        string picked = null;
                        while (officialCursor < shuffledOfficial.Length)
                        {
                            string candidate = shuffledOfficial[officialCursor++];
                            if (!usedKeys.Contains(candidate)) { picked = candidate; break; }
                        }
                        if (picked == null) return null; // 互斥失败 fail-closed
                        usedKeys.Add(picked);
                        keys[i] = picked;
                    }
                }
                waves[w] = new WaveSlot(w, act, count, isNemesis, kind, keys, variant);
            }

            // ---- reserve 6 key（与全部槽位 key 全局互斥）----
            string[] reserve = new string[ReserveKeyCount];
            int reserved = 0;
            while (reserved < ReserveKeyCount && officialCursor < shuffledOfficial.Length)
            {
                string candidate = shuffledOfficial[officialCursor++];
                if (!usedKeys.Contains(candidate))
                {
                    usedKeys.Add(candidate);
                    reserve[reserved++] = candidate;
                }
            }
            if (reserved < ReserveKeyCount) return null;

            // ---- fingerprint（§3.2 冻结六段）----
            string fingerprint = BuildFingerprint(runFormat, rematchCompositionId,
                selectedFateContractId, nemesisTemperamentId, waves);

            return new ModeGWavePlan(runSeed, runFormat, waves, reserve, rematchCompositionId, fingerprint);
        }

        private static ModeGPlanVariant SelectVariant(int bossCount, ref ulong variantState)
        {
            // 单 Boss 波固定 Split（三角分布退化为中心锚点）
            if (bossCount <= 1) return ModeGPlanVariant.Split;
            int idx = ModeGDeterministicRandom.NextInt(ref variantState, 3);
            switch (idx)
            {
                case 0: return ModeGPlanVariant.Split;
                case 1: return ModeGPlanVariant.Pincer;
                default: return ModeGPlanVariant.Arc;
            }
        }

        /// <summary>
        /// 确定性洗牌（Fisher-Yates，整数无偏抽样）。
        /// </summary>
        private static string[] Shuffle(IList<string> source, ref ulong state)
        {
            string[] result = new string[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            for (int i = result.Length - 1; i > 0; i--)
            {
                int j = ModeGDeterministicRandom.NextInt(ref state, i + 1);
                string tmp = result[i];
                result[i] = result[j];
                result[j] = tmp;
            }
            return result;
        }

        private static string BuildFingerprint(ModeGRunFormat runFormat, int rematchCompositionId,
            int selectedFateContractId, int nemesisTemperamentId, WaveSlot[] waves)
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append((int)runFormat).Append('|');
            sb.Append(rematchCompositionId).Append('|');
            sb.Append(selectedFateContractId).Append('|');
            sb.Append(nemesisTemperamentId).Append('|');
            for (int w = 0; w < waves.Length; w++)
            {
                sb.Append((int)waves[w].variant);
                for (int i = 0; i < waves[w].bossPresetKeys.Length; i++)
                {
                    sb.Append(':').Append(waves[w].bossPresetKeys[i]);
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        #endregion

        #region Formation Descriptors（编排包）

        /// <summary>
        /// 编排包锚点描述：玩家最小距离、Boss 两两最小 XZ 距离。
        /// 实际落点由 MapSupportRegistry 冻结安全三元组校验后采用。
        /// </summary>
        public struct FormationSpec
        {
            /// <summary>玩家与最近 Boss 的最小距离（米）</summary>
            public readonly float playerMinDistance;
            /// <summary>Boss 两两最小 XZ 距离（米）</summary>
            public readonly float bossPairMinDistance;

            public FormationSpec(float playerMinDistance, float bossPairMinDistance)
            {
                this.playerMinDistance = playerMinDistance;
                this.bossPairMinDistance = bossPairMinDistance;
            }
        }

        /// <summary>
        /// 编排包静态描述（owner 冻结值）。
        /// </summary>
        public static FormationSpec GetFormationSpec(ModeGPlanVariant variant)
        {
            switch (variant)
            {
                case ModeGPlanVariant.Pincer:
                    // 钳形包夹：贴近玩家两翼
                    return new FormationSpec(10f, 8f);
                case ModeGPlanVariant.Arc:
                    // 弧形包围：中距扇面
                    return new FormationSpec(14f, 6f);
                default:
                    // Split 标准三角分布
                    return new FormationSpec(13f, 10f);
            }
        }

        #endregion
    }
}

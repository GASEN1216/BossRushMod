using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode G 宿命契约族（§3.2 四族分类）。
    /// </summary>
    public enum ModeGContractFamily
    {
        /// <summary>适应族</summary>
        Adaptation,
        /// <summary>处决族</summary>
        Execution,
        /// <summary>节奏族</summary>
        Tempo,
        /// <summary>风格族</summary>
        Style
    }

    /// <summary>
    /// Mode G 契约进度快照（纯数据，遥测/三轴模块在终局时填充）。
    /// </summary>
    public struct ModeGContractProgress
    {
        /// <summary>距离轴破解次数</summary>
        public int distanceResolves;
        /// <summary>弹药轴破解次数</summary>
        public int ammoResolves;
        /// <summary>属性轴破解次数</summary>
        public int attributeResolves;
        /// <summary>Last Stand 成功处决次数</summary>
        public int lastStandCount;
        /// <summary>是否以直接伤害终结 R3 宿敌</summary>
        public bool nemesisR3FinalBlowDirect;
        /// <summary>连续破解三轴目标的最大连击</summary>
        public int maxConsecutiveAxisBreaks;
        /// <summary>三幕各幕 Resolve 次数（长度 3）</summary>
        public int[] resolvesPerAct;
        /// <summary>距离回声累计次数</summary>
        public int distanceEchoCount;
        /// <summary>弹药禁令累计次数</summary>
        public int ammoBanCount;
        /// <summary>属性封锁累计次数</summary>
        public int attributeLockCount;
        /// <summary>第 3/6/9 波中弹药禁令可用的波数</summary>
        public int ammoBanAvailableOnNemesisWaves;
    }

    /// <summary>
    /// Mode G 宿命契约（C3 裁决重写版：纯目标组合）。方案文档 §3.2（183 行）。
    ///
    /// 硬约束：
    /// - 8 项纯目标组合，不新增任何事件/Stat/条件/奖励，不改数值不给奖励；
    /// - 稳定 ID 为 bit 0-31，只追加不复用（正式首发不得少于 8 项）；
    /// - 入口二选一候选（确定性 Contract domain 派生）；
    /// - 阈值为规格候选值，全部标注 owner tunable。
    /// </summary>
    public static class ModeGFateContract
    {
        #region Stable Bit IDs（bit 0-31 只追加）

        public const int IdTriadBreaker = 0;       // 适应：三轴各破解至少一次
        public const int IdLastExecutioner = 1;    // 处决：>=2 次 Last Stand 成功处决且直接伤害终结 R3 宿敌
        public const int IdCounterflowChain = 2;   // 节奏：连续破解三个三轴目标
        public const int IdUnbrokenActs = 3;       // 节奏：三幕各 >=2 Resolve
        public const int IdEdgeWalker = 4;         // 风格：三次距离回声
        public const int IdArsenalDiscipline = 5;  // 风格：>=2 次弹药禁令 + 1 次属性封锁
        public const int IdFinalMinute = 6;        // 处决：三次 Last Stand 成功处决
        public const int IdNemesisDenied = 7;      // 适应：第 3/6/9 波全部可用弹药禁令

        /// <summary>契约总数（正式首发不得少于 8）</summary>
        public const int ContractCount = 8;
        /// <summary>每幕 Resolve 门槛（owner tunable：UnbrokenActs 阈值，§3.2 候选值 2）</summary>
        public const int UnbrokenActsPerActThreshold = 2;
        /// <summary>距离回声门槛（owner tunable：EdgeWalker 阈值，§3.2 候选值 3）</summary>
        public const int EdgeWalkerEchoThreshold = 3;
        /// <summary>Last Stand 门槛（owner tunable：FinalMinute 阈值，§3.2 候选值 3）</summary>
        public const int FinalMinuteLastStandThreshold = 3;
        /// <summary>LastExecutioner 的 Last Stand 门槛（owner tunable：候选值 2）</summary>
        public const int LastExecutionerLastStandThreshold = 2;
        /// <summary>ArsenalDiscipline 弹药禁令门槛（owner tunable：候选值 2）</summary>
        public const int ArsenalDisciplineAmmoBanThreshold = 2;
        /// <summary>ArsenalDiscipline 属性封锁门槛（owner tunable：候选值 1）</summary>
        public const int ArsenalDisciplineAttributeLockThreshold = 1;
        /// <summary>CounterflowChain 连击门槛（owner tunable：候选值 3）</summary>
        public const int CounterflowChainThreshold = 3;
        /// <summary>NemesisDenied 需覆盖的宿敌波数（owner tunable：候选值 3 = 波 3/6/9）</summary>
        public const int NemesisDeniedWaveThreshold = 3;

        #endregion

        #region Contract Definition

        /// <summary>
        /// 宿命契约定义（纯目标；无 difficultyMultiplier/rewardMultiplier）。
        /// </summary>
        public struct ContractDef
        {
            /// <summary>稳定 bit ID（0-31 只追加）</summary>
            public readonly int id;
            public readonly ModeGContractFamily family;
            public readonly string key;       // 稳定标识（本地化 key 后缀/遥测标签）
            public readonly string nameCn;
            public readonly string nameEn;
            public readonly string descCn;
            public readonly string descEn;

            public ContractDef(int id, ModeGContractFamily family, string key,
                string nameCn, string nameEn, string descCn, string descEn)
            {
                this.id = id;
                this.family = family;
                this.key = key;
                this.nameCn = nameCn;
                this.nameEn = nameEn;
                this.descCn = descCn;
                this.descEn = descEn;
            }

            public string GetDisplayName() { return L10n.T(nameCn, nameEn); }
            public string GetDescription() { return L10n.T(descCn, descEn); }
        }

        #endregion

        /// <summary>
        /// 契约池（8 项，按稳定 ID 升序；只追加不复用）。
        /// </summary>
        private static readonly ContractDef[] _pool =
        {
            new ContractDef(IdTriadBreaker, ModeGContractFamily.Adaptation, "TriadBreaker",
                "三轴破晓", "Triad Breaker",
                "本局内距离、弹药、属性三个反制轴各破解至少一次。",
                "Break each of the three counter axes (distance, ammo, attribute) at least once this run."),

            new ContractDef(IdLastExecutioner, ModeGContractFamily.Execution, "LastExecutioner",
                "终末行刑者", "Last Executioner",
                "完成至少 2 次 Last Stand 处决，并以直接伤害终结 R3 宿敌。",
                "Complete at least two Last Stand executions and finish an R3 nemesis with direct damage."),

            new ContractDef(IdCounterflowChain, ModeGContractFamily.Tempo, "CounterflowChain",
                "逆流连锁", "Counterflow Chain",
                "连续破解三个三轴目标（不间断）。",
                "Break three axis objectives consecutively without interruption."),

            new ContractDef(IdUnbrokenActs, ModeGContractFamily.Tempo, "UnbrokenActs",
                "三幕无缺", "Unbroken Acts",
                "三幕中每一幕都达成至少 2 次 Resolve。",
                "Achieve at least 2 Resolves in each of the three acts."),

            new ContractDef(IdEdgeWalker, ModeGContractFamily.Style, "EdgeWalker",
                "边缘行者", "Edge Walker",
                "累计触发三次距离回声。",
                "Trigger distance echoes three times."),

            new ContractDef(IdArsenalDiscipline, ModeGContractFamily.Style, "ArsenalDiscipline",
                "武库戒律", "Arsenal Discipline",
                "达成至少 2 次弹药禁令与 1 次属性封锁。",
                "Enforce at least 2 ammo bans and 1 attribute lock."),

            new ContractDef(IdFinalMinute, ModeGContractFamily.Execution, "FinalMinute",
                "最终时刻", "Final Minute",
                "本局完成三次 Last Stand 处决。",
                "Complete three Last Stand executions this run."),

            new ContractDef(IdNemesisDenied, ModeGContractFamily.Adaptation, "NemesisDenied",
                "宿敌否定", "Nemesis Denied",
                "第 3/6/9 波全部可用弹药禁令。",
                "Keep the ammo ban available on all of waves 3, 6 and 9."),
        };

        /// <summary>
        /// 获取完整契约池（按稳定 ID 升序）。
        /// </summary>
        public static IReadOnlyList<ContractDef> Pool { get { return _pool; } }

        /// <summary>
        /// 根据稳定 ID 获取契约定义。未找到返回 id=-1 的默认值。
        /// </summary>
        public static ContractDef GetById(int id)
        {
            if (id >= 0 && id < _pool.Length && _pool[id].id == id) return _pool[id];
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i].id == id) return _pool[i];
            }
            return new ContractDef(-1, ModeGContractFamily.Adaptation, "None", "无", "None", string.Empty, string.Empty);
        }

        #region Entry Candidate Pair（入口二选一，确定性）

        /// <summary>
        /// 从 runSeed 确定性派生两个契约候选 ID（升序，恰好 2 个，互不相同）。
        /// 供 ModeGEntryPreview 冻结；取消重开不刷新。
        /// </summary>
        public static int[] SelectEntryCandidatePair(ulong runSeed)
        {
            return SelectEntryCandidatePair(runSeed, -1);
        }

        /// <summary>
        /// 候选对排除上一局实际选择一次；0..7 均可排除，-1 表示无历史。
        /// </summary>
        public static int[] SelectEntryCandidatePair(ulong runSeed, int excludedContractId)
        {
            try
            {
                List<int> eligible = new List<int>(_pool.Length);
                for (int i = 0; i < _pool.Length; i++)
                {
                    if (_pool[i].id != excludedContractId) eligible.Add(_pool[i].id);
                }
                if (eligible.Count < 2) throw new InvalidOperationException("eligible contract pool < 2");

                ulong state = ModeGDeterministicRandom.SeedDomain(runSeed,
                    ModeGDeterministicRandom.DomainConstants.Contract, 0);
                int first = ModeGDeterministicRandom.NextInt(ref state, eligible.Count);
                int second = ModeGDeterministicRandom.NextInt(ref state, eligible.Count - 1);
                if (second >= first) second++;
                int a = Math.Min(eligible[first], eligible[second]);
                int b = Math.Max(eligible[first], eligible[second]);
                return new int[] { a, b };
            }
            catch
            {
                int firstFallback = excludedContractId == IdTriadBreaker
                    ? IdLastExecutioner : IdTriadBreaker;
                int secondFallback = excludedContractId == IdFinalMinute
                    ? IdNemesisDenied : IdFinalMinute;
                return new int[] { Math.Min(firstFallback, secondFallback),
                    Math.Max(firstFallback, secondFallback) };
            }
        }

        #endregion

        #region Objective Evaluation（纯函数，不改数值不给奖励）

        /// <summary>
        /// 评估契约目标是否达成（纯函数；奖励由 ModeGRewardTransaction 独立结算，不读此结果加成）。
        /// </summary>
        public static bool Evaluate(int contractId, ModeGContractProgress p)
        {
            switch (contractId)
            {
                case IdTriadBreaker:
                    return p.distanceResolves >= 1 && p.ammoResolves >= 1 && p.attributeResolves >= 1;

                case IdLastExecutioner:
                    return p.lastStandCount >= LastExecutionerLastStandThreshold && p.nemesisR3FinalBlowDirect;

                case IdCounterflowChain:
                    return p.maxConsecutiveAxisBreaks >= CounterflowChainThreshold;

                case IdUnbrokenActs:
                    if (p.resolvesPerAct == null || p.resolvesPerAct.Length < 3) return false;
                    return p.resolvesPerAct[0] >= UnbrokenActsPerActThreshold
                        && p.resolvesPerAct[1] >= UnbrokenActsPerActThreshold
                        && p.resolvesPerAct[2] >= UnbrokenActsPerActThreshold;

                case IdEdgeWalker:
                    return p.distanceEchoCount >= EdgeWalkerEchoThreshold;

                case IdArsenalDiscipline:
                    return p.ammoBanCount >= ArsenalDisciplineAmmoBanThreshold
                        && p.attributeLockCount >= ArsenalDisciplineAttributeLockThreshold;

                case IdFinalMinute:
                    return p.lastStandCount >= FinalMinuteLastStandThreshold;

                case IdNemesisDenied:
                    return p.ammoBanAvailableOnNemesisWaves >= NemesisDeniedWaveThreshold;

                default:
                    return false;
            }
        }

        #endregion
    }
}

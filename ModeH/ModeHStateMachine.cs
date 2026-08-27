using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 一次状态转换的记录（§18.2）。按钮、场景事件和战斗事件只提交命令，不直接写状态字段。
    /// </summary>
    public sealed class ModeHTransitionRecord
    {
        /// <summary>转换后的状态序号。</summary>
        public int StateSequence;
        /// <summary>当前比赛编号。</summary>
        public int MatchIndex;
        /// <summary>同场技术重试序号。</summary>
        public int TechnicalRetrySequence;
        /// <summary>赛季运行 ID。</summary>
        public string RunId;
        /// <summary>故障源状态。</summary>
        public ModeHLifecycle RecoveryOriginalLifecycle;
        /// <summary>恢复目标状态。</summary>
        public ModeHLifecycle RecoveryResumeTarget;
        /// <summary>转换原因。</summary>
        public string Reason;
        /// <summary>转换前状态。</summary>
        public ModeHLifecycle From;
        /// <summary>转换后状态。</summary>
        public ModeHLifecycle To;
    }

    /// <summary>
    /// Mode H 唯一状态机（设计提案 §18.2）。
    ///
    /// 硬约束：
    /// - 全部状态变化必须经过 TryTransition(expected, next, token) 单点完成；
    /// - 合法转换表逐条冻结，非法跳转一律拒绝并保留原状态；
    /// - 早期恢复子表（EntryIntent/SceneLoading/Drafting/RosterLocked）独立于统一异常出口，
    ///   这些源状态不得由默认恢复出口直接跳到 MatchBrief；
    /// - 已 CAS 的事实必须先进入 ErrorRecoveryPending，恢复动作开始后才转 Recovering，
    ///   屏障完成后回 ErrorRecoveryPending，再由原 token 命令继续；
    /// - TechnicalAbort / ModeHAvailability 都不是 lifecycle 值。
    /// </summary>
    public static class ModeHStateMachine
    {
        #region 转换表

        private static readonly Dictionary<ModeHLifecycle, ModeHLifecycle[]> Transitions =
            BuildTransitions();

        /// <summary>统一异常出口适用的状态（§18.2）。</summary>
        private static readonly ModeHLifecycle[] UnifiedFailureSources = new ModeHLifecycle[]
        {
            ModeHLifecycle.MatchBrief,
            ModeHLifecycle.LoadoutEditing,
            ModeHLifecycle.OddsPreview,
            ModeHLifecycle.LoadoutLocked,
            ModeHLifecycle.StakePrepared,
            ModeHLifecycle.MatchSpawning,
            ModeHLifecycle.MatchFighting,
            ModeHLifecycle.RelayPending,
            ModeHLifecycle.MatchSettling,
            ModeHLifecycle.Intermission,
            ModeHLifecycle.TransferWindow,
            ModeHLifecycle.HallOfFame
        };

        /// <summary>早期恢复子表：源状态 -> 允许的恢复目标（§18.2）。</summary>
        private static readonly Dictionary<ModeHLifecycle, ModeHLifecycle[]> EarlyRecoveryTargets =
            BuildEarlyRecoveryTargets();

        private static Dictionary<ModeHLifecycle, ModeHLifecycle[]> BuildTransitions()
        {
            Dictionary<ModeHLifecycle, ModeHLifecycle[]> map =
                new Dictionary<ModeHLifecycle, ModeHLifecycle[]>();

            map[ModeHLifecycle.None] = new ModeHLifecycle[]
            {
                ModeHLifecycle.EntryIntent
            };
            map[ModeHLifecycle.EntryIntent] = new ModeHLifecycle[]
            {
                ModeHLifecycle.SceneLoading,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.None
            };
            map[ModeHLifecycle.SceneLoading] = new ModeHLifecycle[]
            {
                ModeHLifecycle.ProductionCertifying,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.None
            };
            map[ModeHLifecycle.ProductionCertifying] = new ModeHLifecycle[]
            {
                ModeHLifecycle.Drafting,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.None
            };
            map[ModeHLifecycle.Drafting] = new ModeHLifecycle[]
            {
                ModeHLifecycle.RosterLocked,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.None
            };
            map[ModeHLifecycle.RosterLocked] = new ModeHLifecycle[]
            {
                ModeHLifecycle.MatchBrief,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.None
            };
            map[ModeHLifecycle.MatchBrief] = new ModeHLifecycle[]
            {
                ModeHLifecycle.LoadoutEditing,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.LoadoutEditing] = new ModeHLifecycle[]
            {
                ModeHLifecycle.OddsPreview,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.OddsPreview] = new ModeHLifecycle[]
            {
                ModeHLifecycle.LoadoutEditing,
                ModeHLifecycle.LoadoutLocked,
                ModeHLifecycle.StakePrepared,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.LoadoutLocked] = new ModeHLifecycle[]
            {
                ModeHLifecycle.MatchSpawning,
                ModeHLifecycle.StakePrepared,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.StakePrepared] = new ModeHLifecycle[]
            {
                ModeHLifecycle.MatchSpawning,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.MatchSpawning] = new ModeHLifecycle[]
            {
                ModeHLifecycle.MatchFighting,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.MatchFighting] = new ModeHLifecycle[]
            {
                ModeHLifecycle.RelayPending,
                ModeHLifecycle.MatchSettling,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.ErrorRecoveryPending] = new ModeHLifecycle[]
            {
                ModeHLifecycle.RelayPending,
                ModeHLifecycle.MatchSettling,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.RelayPending] = new ModeHLifecycle[]
            {
                ModeHLifecycle.MatchFighting,
                ModeHLifecycle.MatchSettling,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.MatchSettling] = new ModeHLifecycle[]
            {
                ModeHLifecycle.Intermission,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.Intermission] = new ModeHLifecycle[]
            {
                ModeHLifecycle.SeasonEnded,
                ModeHLifecycle.HallOfFame,
                ModeHLifecycle.TransferWindow,
                ModeHLifecycle.MatchBrief,
                ModeHLifecycle.Suspended,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending
            };
            map[ModeHLifecycle.TransferWindow] = new ModeHLifecycle[]
            {
                ModeHLifecycle.MatchBrief,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.HallOfFame] = new ModeHLifecycle[]
            {
                ModeHLifecycle.SeasonEnded,
                ModeHLifecycle.Recovering,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.Recovering] = new ModeHLifecycle[]
            {
                ModeHLifecycle.EntryIntent,
                ModeHLifecycle.SceneLoading,
                ModeHLifecycle.Drafting,
                ModeHLifecycle.RosterLocked,
                ModeHLifecycle.MatchBrief,
                ModeHLifecycle.ErrorRecoveryPending,
                ModeHLifecycle.Intermission,
                ModeHLifecycle.TransferWindow,
                ModeHLifecycle.HallOfFame,
                ModeHLifecycle.Suspended
            };
            map[ModeHLifecycle.Suspended] = new ModeHLifecycle[]
            {
                ModeHLifecycle.Recovering
            };
            map[ModeHLifecycle.SeasonEnded] = new ModeHLifecycle[]
            {
                ModeHLifecycle.None
            };
            return map;
        }

        private static Dictionary<ModeHLifecycle, ModeHLifecycle[]> BuildEarlyRecoveryTargets()
        {
            Dictionary<ModeHLifecycle, ModeHLifecycle[]> map =
                new Dictionary<ModeHLifecycle, ModeHLifecycle[]>();
            // EntryIntent -> Recovering -> EntryIntent
            map[ModeHLifecycle.EntryIntent] = new ModeHLifecycle[] { ModeHLifecycle.EntryIntent };
            // SceneLoading -> Recovering -> SceneLoading（scene intent/owner 快照有效）否则 EntryIntent
            map[ModeHLifecycle.SceneLoading] = new ModeHLifecycle[]
            {
                ModeHLifecycle.SceneLoading, ModeHLifecycle.EntryIntent
            };
            // Drafting -> Recovering -> Drafting（候选 seed/sequence 快照有效）否则 EntryIntent
            map[ModeHLifecycle.Drafting] = new ModeHLifecycle[]
            {
                ModeHLifecycle.Drafting, ModeHLifecycle.EntryIntent
            };
            // RosterLocked -> Recovering -> RosterLocked（roster snapshot/invariant 有效）否则 EntryIntent
            map[ModeHLifecycle.RosterLocked] = new ModeHLifecycle[]
            {
                ModeHLifecycle.RosterLocked, ModeHLifecycle.EntryIntent
            };
            // ProductionCertifying 认证失败只退款回 None，不属于早期恢复子表
            return map;
        }

        #endregion

        #region 查询

        /// <summary>该转换是否在冻结转换表内。</summary>
        public static bool IsTransitionAllowed(ModeHLifecycle from, ModeHLifecycle to)
        {
            if (from == ModeHLifecycle.Unknown || to == ModeHLifecycle.Unknown) return false;
            ModeHLifecycle[] targets;
            if (!Transitions.TryGetValue(from, out targets)) return false;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == to) return true;
            }
            return false;
        }

        /// <summary>该状态是否适用统一异常出口。</summary>
        public static bool IsUnifiedFailureSource(ModeHLifecycle lifecycle)
        {
            for (int i = 0; i < UnifiedFailureSources.Length; i++)
            {
                if (UnifiedFailureSources[i] == lifecycle) return true;
            }
            return false;
        }

        /// <summary>取早期恢复子表允许的目标；不属于早期状态返回 null。</summary>
        public static ModeHLifecycle[] GetEarlyRecoveryTargets(ModeHLifecycle source)
        {
            ModeHLifecycle[] targets;
            return EarlyRecoveryTargets.TryGetValue(source, out targets) ? targets : null;
        }

        /// <summary>
        /// 早期恢复出口不得直接跳到 MatchBrief（§18.2）。
        /// </summary>
        public static bool IsEarlyRecoverySource(ModeHLifecycle source)
        {
            return EarlyRecoveryTargets.ContainsKey(source);
        }

        /// <summary>该状态是否为终态（只有赛季终局或未创建 run 的入口取消才写 None）。</summary>
        public static bool IsTerminal(ModeHLifecycle lifecycle)
        {
            return lifecycle == ModeHLifecycle.None || lifecycle == ModeHLifecycle.SeasonEnded;
        }

        #endregion

        #region 静态缓存

        /// <summary>
        /// 转换表与早期恢复子表都是只读冻结数据，本方法不清空它们；
        /// 保留统一入口是为了让 Mode H 的静态缓存清理在一个地方全部可见。
        /// </summary>
        public static void ResetStaticCaches()
        {
        }

        #endregion

        #region 唯一转换入口

        /// <summary>
        /// 唯一状态转换入口。expected 必须等于当前状态（CAS 语义），
        /// token 必须与当前 run owner 匹配，转换必须在冻结表内。
        /// </summary>
        public static bool TryTransition(
            ModeHRunState runState,
            ModeHLifecycle expected,
            ModeHLifecycle next,
            long ownerToken,
            string reason,
            out ModeHTransitionRecord record,
            out string failureReasonId)
        {
            record = null;
            failureReasonId = null;

            if (runState == null)
            {
                failureReasonId = "state_run_null";
                return false;
            }
            if (!runState.IsOwnerTokenValid(ownerToken))
            {
                failureReasonId = "state_owner_token_mismatch";
                return false;
            }
            if (runState.Lifecycle != expected)
            {
                failureReasonId = "state_expected_mismatch";
                return false;
            }
            if (!IsTransitionAllowed(expected, next))
            {
                failureReasonId = "state_transition_rejected";
                return false;
            }
            if (expected == ModeHLifecycle.Recovering && IsEarlyRecoverySource(runState.RecoveryOriginalLifecycle))
            {
                ModeHLifecycle[] allowed = GetEarlyRecoveryTargets(runState.RecoveryOriginalLifecycle);
                bool ok = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (allowed[i] == next) { ok = true; break; }
                }
                if (!ok)
                {
                    failureReasonId = "state_early_recovery_target_rejected";
                    return false;
                }
            }

            record = runState.ApplyTransition(expected, next, reason);
            return true;
        }

        #endregion
    }
}

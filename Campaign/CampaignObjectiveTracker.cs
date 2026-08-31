// ============================================================================
// CampaignObjectiveTracker.cs - 单局契约目标追踪
// ============================================================================
// 会话态，**完全不落盘**：局中失败或离场即整体丢弃，契约保持已接取状态可无限重试。
// 这是刻意的设计——跨局累计会让「单局制」目标（无伤、存活时长）失去意义。
//
// 【武装（Arm）的时机】
//   目标不能一接契约就开始记：玩家接完约还要回基地整备、切地图。
//   真正开始记的时刻是「进入该章指定的模式」，由 CampaignModeBridge 在检测到
//   模式激活时调 EnsureArmedFor(mode)。武装会清空上一局残留的计数。
//
// 【为什么无伤目标要单独的 Failed 而不是"计数不够"】
//   无伤是「一旦破防本局就废了」的判定，与「击杀数还没攒够」语义完全不同。
//   合并成一个计数会让 HUD 显示成"0/1 还有机会"，误导玩家继续打完一局白费。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>单局契约目标追踪。全静态：一局只有一份进度。</summary>
    internal static class CampaignObjectiveTracker
    {
        #region 状态

        /// <summary>本局正在追踪的章节；null = 未武装。</summary>
        private static string _armedChapterId;

        /// <summary>本局武装时对应的模式标识。</summary>
        private static string _armedMode;

        /// <summary>各目标的本局进度，与章节定义的 Objectives 一一对应。</summary>
        private static readonly List<CampaignObjectiveProgress> _progress =
            new List<CampaignObjectiveProgress>();

        /// <summary>入场累计秒数（SurviveMinutes 用）。</summary>
        private static float _elapsedSeconds;

        /// <summary>本局已达成并上报过，避免重复通知。</summary>
        private static bool _notified;

        #endregion

        #region 只读

        /// <summary>当前是否已武装（局内 HUD 据此决定要不要显示）。</summary>
        internal static bool IsArmed { get { return !string.IsNullOrEmpty(_armedChapterId); } }

        /// <summary>当前追踪的章节 ID；未武装返回 null。</summary>
        internal static string ArmedChapterId { get { return _armedChapterId; } }

        /// <summary>各目标进度快照（HUD 用）。永不返回 null。</summary>
        internal static IList<CampaignObjectiveProgress> Progress { get { return _progress; } }

        #endregion

        #region 武装与复位

        /// <summary>
        /// 若当前有进行中契约且其指定模式与传入模式一致，则武装本局追踪。
        /// 幂等：同一局重复调用不会清掉已累积的进度。
        /// </summary>
        internal static void EnsureArmedFor(string mode)
        {
            try
            {
                if (string.IsNullOrEmpty(mode)) return;
                if (IsArmed && string.Equals(_armedMode, mode, StringComparison.Ordinal)) return;

                CampaignChapterDef def = CampaignProgressService.GetActiveChapterDef();
                if (def == null) return;
                if (!string.Equals(def.Mode, mode, StringComparison.Ordinal)) return;

                _armedChapterId = def.ChapterId;
                _armedMode = mode;
                _elapsedSeconds = 0f;
                _notified = false;

                _progress.Clear();
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    CampaignObjectiveProgress item = new CampaignObjectiveProgress();
                    item.Def = def.Objectives[i];
                    item.Current = 0;
                    item.Failed = false;
                    _progress.Add(item);
                }

                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "契约追踪已武装: "
                    + _armedChapterId + " (" + mode + ")");
            }
            catch (Exception e)
            {
                LogFailure("arm", e);
            }
        }

        /// <summary>整局复位。离场、放弃契约、换槽都要调。</summary>
        internal static void ResetSession()
        {
            _armedChapterId = null;
            _armedMode = null;
            _elapsedSeconds = 0f;
            _notified = false;
            _progress.Clear();
        }

        #endregion

        #region 上报入口（全部 no-throw，未武装时 O(1) 早返）

        /// <summary>玩家造成的一次击杀。isMelee/isBoss 由采集器判定后传入。</summary>
        internal static void ReportPlayerKill(bool isMelee, bool isBoss, bool hasBountyMark)
        {
            if (!IsArmed) return;
            try
            {
                for (int i = 0; i < _progress.Count; i++)
                {
                    CampaignObjectiveProgress item = _progress[i];
                    if (item == null || item.Def == null) continue;

                    switch (item.Def.Kind)
                    {
                        case CampaignObjectiveKind.MeleeKills:
                            if (isMelee) item.Current++;
                            break;
                        case CampaignObjectiveKind.FactionBossKills:
                            if (isBoss) item.Current++;
                            break;
                        case CampaignObjectiveKind.BountyKills:
                            if (isBoss && hasBountyMark) item.Current++;
                            break;
                    }
                }
                EvaluateAndNotify();
            }
            catch (Exception e)
            {
                LogFailure("report_kill", e);
            }
        }

        /// <summary>玩家受到一次伤害。会让尚未跨过波次门槛的无伤目标直接判失败。</summary>
        internal static void ReportPlayerDamaged(int currentWave)
        {
            if (!IsArmed) return;
            try
            {
                for (int i = 0; i < _progress.Count; i++)
                {
                    CampaignObjectiveProgress item = _progress[i];
                    if (item == null || item.Def == null) continue;
                    if (item.Def.Kind != CampaignObjectiveKind.NoDamageUntilWave) continue;
                    if (item.Failed) continue;

                    // 已经打过了门槛波次就不再判失败——目标已达成
                    if (currentWave > item.Def.Threshold) continue;
                    item.Failed = true;

                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "无伤目标已失败（第 "
                        + currentWave + " 波受伤）");
                }
            }
            catch (Exception e)
            {
                LogFailure("report_damaged", e);
            }
        }

        /// <summary>本局到达的波次（取历史最大值，防止波次回退把进度抹掉）。</summary>
        internal static void ReportWaveReached(int wave)
        {
            if (!IsArmed) return;
            try
            {
                for (int i = 0; i < _progress.Count; i++)
                {
                    CampaignObjectiveProgress item = _progress[i];
                    if (item == null || item.Def == null) continue;

                    if (item.Def.Kind == CampaignObjectiveKind.ReachWave)
                    {
                        if (wave > item.Current) item.Current = wave;
                    }
                    else if (item.Def.Kind == CampaignObjectiveKind.NoDamageUntilWave)
                    {
                        // 熬过门槛波次即达成；已判失败的不会被这里救回来
                        if (!item.Failed && wave > item.Def.Threshold) item.Current = item.Def.Threshold;
                    }
                }
                EvaluateAndNotify();
            }
            catch (Exception e)
            {
                LogFailure("report_wave", e);
            }
        }

        /// <summary>标准竞技场通关。</summary>
        internal static void ReportStandardClear()
        {
            MarkSimpleObjective(CampaignObjectiveKind.StandardClear);
            SatisfyNoDamageOnRunComplete();
        }

        /// <summary>当前模式撤离成功。</summary>
        internal static void ReportExtract()
        {
            MarkSimpleObjective(CampaignObjectiveKind.ModeExtract);
            SatisfyNoDamageOnRunComplete();
        }

        /// <summary>
        /// 整局走完（通关或撤离成功）时结算无伤目标。
        ///
        /// 为什么需要这个兜底：NoDamageUntilWave 平时靠「波次超过门槛」置达成，
        /// 而标准模式的总波次 = BossFilter 过滤后的 Boss 数 / 每波 Boss 数。
        /// 玩家把 Boss 池筛得足够小时，波次可能永远到不了门槛+1，
        /// 目标就卡死在未达成——哪怕全程一滴血没掉。
        /// 走完整局本身已经蕴含「熬过了全部波次」，因此在这里补判。
        /// 已经被破防（Failed）的不会被救回来。
        /// </summary>
        private static void SatisfyNoDamageOnRunComplete()
        {
            if (!IsArmed) return;
            try
            {
                for (int i = 0; i < _progress.Count; i++)
                {
                    CampaignObjectiveProgress item = _progress[i];
                    if (item == null || item.Def == null) continue;
                    if (item.Def.Kind != CampaignObjectiveKind.NoDamageUntilWave) continue;
                    if (item.Failed) continue;
                    if (item.Current < item.Def.Threshold) item.Current = item.Def.Threshold;
                }
                EvaluateAndNotify();
            }
            catch (Exception e)
            {
                LogFailure("satisfy_no_damage", e);
            }
        }

        /// <summary>终章 Boss 被击败。</summary>
        internal static void ReportFinalBossKill()
        {
            MarkSimpleObjective(CampaignObjectiveKind.FinalBossKill);
        }

        private static void MarkSimpleObjective(CampaignObjectiveKind kind)
        {
            if (!IsArmed) return;
            try
            {
                for (int i = 0; i < _progress.Count; i++)
                {
                    CampaignObjectiveProgress item = _progress[i];
                    if (item == null || item.Def == null) continue;
                    if (item.Def.Kind != kind) continue;
                    if (item.Current < item.Def.Threshold) item.Current = item.Def.Threshold;
                }
                EvaluateAndNotify();
            }
            catch (Exception e)
            {
                LogFailure("mark_objective", e);
            }
        }

        /// <summary>
        /// 宿主 tick 驱动的计时（SurviveMinutes）。
        /// 未武装时 O(1) 早返；无计时类目标时也只是一次列表扫描。
        /// </summary>
        internal static void Tick(float deltaTime)
        {
            if (!IsArmed) return;
            if (deltaTime <= 0f) return;

            try
            {
                _elapsedSeconds += deltaTime;
                int minutes = (int)(_elapsedSeconds / 60f);

                bool changed = false;
                for (int i = 0; i < _progress.Count; i++)
                {
                    CampaignObjectiveProgress item = _progress[i];
                    if (item == null || item.Def == null) continue;
                    if (item.Def.Kind != CampaignObjectiveKind.SurviveMinutes) continue;
                    if (minutes > item.Current)
                    {
                        item.Current = minutes;
                        changed = true;
                    }
                }
                if (changed) EvaluateAndNotify();
            }
            catch (Exception e)
            {
                LogFailure("tick", e);
            }
        }

        #endregion

        #region 判定

        /// <summary>全部目标达成时上报一次（幂等）。</summary>
        private static void EvaluateAndNotify()
        {
            if (_notified) return;
            if (_progress.Count == 0) return;

            for (int i = 0; i < _progress.Count; i++)
            {
                CampaignObjectiveProgress item = _progress[i];
                if (item == null || !item.IsSatisfied) return;
            }

            _notified = true;
            CampaignProgressService.NotifyObjectivesSatisfied(_armedChapterId);
        }

        #endregion

        #region 诊断与清理

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 目标追踪 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 日志函数自身：这里再记日志会无限递归
            }
        }

        internal static void ResetStaticCaches()
        {
            ResetSession();
        }

        #endregion
    }
}

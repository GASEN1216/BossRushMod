using System;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 专用测试档中的真实日报事务：签到、跨日、落盘、清缓存回读与 Store 失败回滚。
        /// 独立文件只为保持 F3 主 runner 低于 1200 行结构预算。
        /// </summary>
        internal bool ValidateDailyReportRollback(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            bool signed = false;
            bool rolled = false;
            bool rollback = false;
            bool reloaded = false;
            int expectedDay = -1;
            try
            {
                DailyReportData current = DailyReportPersistence.Current;
                if (current == null)
                {
                    reason = "日报存档不可用";
                    return false;
                }

                DailyReportData seed = current.Clone();
                seed.DayIndex = Math.Max(1, current.DayIndex + 1);
                seed.LastSettledDayIndex = seed.DayIndex - 1;
                seed.LastSignedDayIndex = 0;
                seed.BountyCompleted = false;
                seed.BountyRewardClaimed = false;
                seed.Today = new DailyReportStats();
                seed.Yesterday = new DailyReportStats();
                seed.HasYesterday = false;
                seed.PendingIssueBanner = false;
                seed.CarrySeconds = 0d;
                if (!DailyReportPersistence.Store(seed) || !DailyReportPersistence.FlushPending())
                {
                    reason = "日报测试种子无法持久化";
                    return false;
                }

                DailyReportService.NotifySlotChanged();
                DailyReportSignInResult signIn;
                signed = DailyReportService.TrySignInToday(out signIn)
                    && signIn.Outcome == DailyReportSignInOutcome.Success;

                int signedDay = DailyReportPersistence.Current.DayIndex;
                DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
                DailyReportData afterRollover = DailyReportPersistence.Current;
                expectedDay = signedDay + 1;
                rolled = afterRollover != null
                    && afterRollover.DayIndex == expectedDay
                    && afterRollover.LastSettledDayIndex == signedDay
                    && afterRollover.HasYesterday;

                string beforeFailure = DailyReportCodec.Encode(afterRollover);
                DailyReportPersistence.SetValidationRejectStore(true);
                DailyReportSignInResult rejectedResult;
                bool rejectedAccepted = DailyReportService.TrySignInToday(out rejectedResult);
                string afterFailure = DailyReportCodec.Encode(DailyReportPersistence.Current);
                rollback = !rejectedAccepted
                    && rejectedResult.Outcome == DailyReportSignInOutcome.PersistBlocked
                    && string.Equals(beforeFailure, afterFailure, StringComparison.Ordinal);
                DailyReportPersistence.SetValidationRejectStore(false);

                if (!DailyReportPersistence.FlushPending())
                {
                    reason = "日报跨日批次无法写入存档缓存";
                    return false;
                }
                SavesSystem.SaveFile(false);

                DailyReportService.ResetStaticCaches();
                DailyReportPersistence.ResetStaticCaches();
                DailyReportPersistence.EnsureSubscribed();
                DailyReportData readback = DailyReportPersistence.Current;
                reloaded = readback != null
                    && readback.DayIndex == expectedDay
                    && readback.LastSettledDayIndex == expectedDay - 1
                    && readback.HasYesterday;

                metrics = "signed=" + signed + ",rolled=" + rolled + ",rollback=" + rollback
                    + ",reloaded=" + reloaded + ",day=" + (readback != null ? readback.DayIndex : -1);
                if (!signed || !rolled || !rollback || !reloaded)
                    reason = "真实签到/跨日/失败回滚/重启回读链不完整";
                return reason == null;
            }
            finally
            {
                DailyReportPersistence.SetValidationRejectStore(false);
            }
        }
    }
}

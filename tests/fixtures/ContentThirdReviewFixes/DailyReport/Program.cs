using System;
using System.Collections.Generic;
using BossRush;

namespace UnityEngine { static class Time { public static float realtimeSinceStartup; } }
class GameClock { public static GameClock Instance = new GameClock(); public float clockTimeScale = 60; }
namespace BossRush
{
    static class ModBehaviour { internal static void DevLog(string text) {} }
    // Substitute the host I/O boundary, while keeping the production service and full JSON codec.
    static class DailyReportPersistence
    {
        internal static DailyReportData Current;
        internal static bool IsStoreFaulted, HasWriteBarrier, RejectStore;
        internal static DailyReportData LoadOrInit() { return Current; }
        internal static bool Store(DailyReportData data)
        {
            if (IsStoreFaulted || HasWriteBarrier || RejectStore) return false;
            Current = DailyReportCodec.Decode(DailyReportCodec.Encode(data));
            if (Current == null) throw new Exception("Production codec rejected service candidate");
            return true;
        }
    }
    static class DailyReportSaveCoordinator
    {
        internal static bool FaultOnNextFlush;
        internal static void RequestFlush()
        {
            if (!FaultOnNextFlush) return;
            FaultOnNextFlush = false;
            DailyReportPersistence.IsStoreFaulted = true;
        }
        internal static bool TryPrepareCashReward() { return true; }
    }
    class DailyReportBountyDef { internal string Id; internal int Target; internal long CashReward; }
    static class DailyReportBounty
    {
        internal static DailyReportBountyDef SelectForDay(long seed, int day) { return null; }
        internal static int EvaluateProgress(DailyReportBountyDef def, DailyReportStats stats) { return 0; }
    }
    static class DailyReportRewards
    {
        internal static bool Reject, FaultAfterGrant;
        internal static int Attempts;
        internal static int RejectQuality;
        internal static readonly List<string> Delivered = new List<string>();
        internal static bool TryGrantMilestone(int quality, long seed, int day, int slot, out string reason)
        {
            Attempts++;
            reason = Reject || quality == RejectQuality ? "unavailable" : null;
            if (Reject || quality == RejectQuality) return false;
            Delivered.Add(quality + "/" + seed + "/" + day + "/" + slot);
            if (FaultAfterGrant) DailyReportPersistence.IsStoreFaulted = true;
            return true;
        }
        internal static bool TryGrantBountyCash(long amount, out string reason) { reason = null; return true; }
    }
}

class Program
{
    static int checks;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    // Literal old v1 JSON: no new fields, not synthesized by the new encoder.
    static string Legacy(int period, int signed, int last, int day, int mask = 0, long seed = 99)
    {
        return "{\"schemaVersion\":1,\"periodIndex\":" + period
            + ",\"periodSignedCount\":" + signed + ",\"lastSignedDayIndex\":" + last
            + ",\"dayIndex\":" + day + ",\"periodClaimedMask\":" + mask
            + ",\"streak\":" + signed + ",\"bountySeed\":" + seed + "}";
    }

    static void Reset(int period, int signed, int last, int day, int mask = 0, long seed = 99)
    {
        DailyReportService.ResetStaticCaches();
        DailyReportPersistence.IsStoreFaulted = DailyReportPersistence.HasWriteBarrier = false;
        DailyReportPersistence.RejectStore = false;
        DailyReportSaveCoordinator.FaultOnNextFlush = false;
        DailyReportRewards.Reject = DailyReportRewards.FaultAfterGrant = false;
        DailyReportRewards.Attempts = 0;
        DailyReportRewards.RejectQuality = 0;
        DailyReportRewards.Delivered.Clear();
        DailyReportPersistence.Current = DailyReportCodec.Decode(Legacy(period, signed, last, day, mask, seed));
        Check(DailyReportPersistence.Current != null && DailyReportPersistence.Current.PendingMilestones.Count == 0,
            "real codec accepts legacy v1 without debt extension");
    }

    static void Reload()
    {
        string payload = DailyReportCodec.Encode(DailyReportPersistence.Current);
        DailyReportService.ResetStaticCaches();
        DailyReportPersistence.Current = DailyReportCodec.Decode(payload);
        Check(DailyReportPersistence.Current != null, "new ledger round-trips through production codec");
    }

    static void Main()
    {
        Reset(1, 7, 7, 8);
        DailyReportRewards.Reject = true;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == 1 && DailyReportPersistence.Current.PendingMilestones.Count == 1,
            "legacy unpaid milestone becomes an independent persisted debt");
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        Check(DailyReportPersistence.Current.DayIndex == 9 && DailyReportPersistence.Current.PeriodSignedCount == 0
            && DailyReportPersistence.Current.PendingMilestones.Count == 1, "streak reset preserves the earned reward");
        Reload();
        DailyReportRewards.Reject = false;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 1 && DailyReportRewards.Delivered[0] == "5/99/7/7",
            "post-reset reload delivers original quality seed sign-day and slot");
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 1 && DailyReportPersistence.Current.PendingMilestones.Count == 0,
            "successful recovery clears only the paid debt and does not repeat");

        Reset(1, 15, 15, 16);
        DailyReportRewards.RejectQuality = 5;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == 2 && DailyReportRewards.Delivered.Count == 1
            && DailyReportRewards.Delivered[0] == "6/99/15/15" && DailyReportPersistence.Current.PendingMilestones.Count == 1,
            "unavailable older quality does not block another deliverable debt");
        DailyReportRewards.RejectQuality = 0;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 2 && DailyReportPersistence.Current.PendingMilestones.Count == 0,
            "older quality recovers independently without repeating paid later reward");

        Reset(1, 30, 30, 31, (1 << 6) | (1 << 14) | (1 << 23));
        DailyReportRewards.Reject = true;
        var result = DailyReportService.SignInAndClaim();
        Check(result.Outcome == DailyReportSignInOutcome.Success && DailyReportPersistence.Current.PeriodIndex == 2
            && DailyReportPersistence.Current.PendingMilestones.Count == 1, "period rollover stages old unpaid Q8 before reset");
        Reload();
        DailyReportRewards.Reject = false;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 1 && DailyReportRewards.Delivered[0] == "8/99/30/30"
            && DailyReportPersistence.Current.PeriodClaimedMask == 0, "old-period delivery retains identity without claiming new-period slot");

        Reset(1, 7, 7, 8);
        DailyReportRewards.Reject = true;
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        var data = DailyReportPersistence.Current;
        data.DayIndex = 15; data.LastSignedDayIndex = 14; data.PeriodSignedCount = 6;
        DailyReportService.SignInAndClaim();
        Check(DailyReportPersistence.Current.PendingMilestones.Count == 2, "same-period restarted streak keeps both slot-seven identities");
        Reload();
        DailyReportRewards.Reject = false;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 2 && DailyReportRewards.Delivered[0] == "5/99/7/7"
            && DailyReportRewards.Delivered[1] == "5/99/15/7" && DailyReportService.IsMilestoneClaimed(DailyReportPersistence.Current, 7),
            "old and new streak rewards each arrive once and current mask matches new streak");

        Reset(1, 6, 6, 7);
        DailyReportSaveCoordinator.FaultOnNextFlush = true;
        DailyReportService.SignInAndClaim();
        DailyReportService.TryRedeliverPendingMilestones();
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == 0 && DailyReportPersistence.Current.PendingMilestones.Count == 1,
            "fault during sign-in flush blocks initial grant and every panel retry");
        Reload(); DailyReportPersistence.IsStoreFaulted = false;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 1, "debt can recover after reload and storage repair");

        Reset(1, 7, 7, 8);
        DailyReportSaveCoordinator.FaultOnNextFlush = true;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == 0, "retry preparation flush fault blocks physical reward");

        Reset(1, 6, 6, 7);
        DailyReportRewards.FaultAfterGrant = true;
        DailyReportService.SignInAndClaim();
        DailyReportService.TryRedeliverPendingMilestones();
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 1 && DailyReportPersistence.Current.PendingMilestones.Count == 1,
            "unexpected post-grant fault retains debt but known fault stops further sends");
        Reload(); DailyReportPersistence.IsStoreFaulted = false; DailyReportRewards.FaultAfterGrant = false;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Delivered.Count == 2 && DailyReportPersistence.Current.PendingMilestones.Count == 0,
            "recovery preserves accepted at-least-once behavior after uncommitted grant");

        Reset(1, 7, 7, 8);
        DailyReportPersistence.RejectStore = true;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == 0 && DailyReportPersistence.Current.PendingMilestones.Count == 0,
            "rejected debt candidate grants nothing and does not mutate live state");
        DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
        Check(DailyReportPersistence.Current.DayIndex == 8 && DailyReportPersistence.Current.PeriodSignedCount == 7,
            "rejected rollover preserves original progress and legacy debt carrier");

        Reset(1, 7, 7, 8, 1 << 6);
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == 0, "legacy claimed mask never becomes new debt");
        Reset(1, 7, 7, 8, 0, 0);
        DailyReportRewards.Reject = true;
        DailyReportService.TryRedeliverPendingMilestones();
        long frozenSeed = DailyReportPersistence.Current.PendingMilestones[0].Seed;
        Reload();
        Check(frozenSeed != 0 && DailyReportPersistence.Current.PendingMilestones[0].Seed == frozenSeed,
            "legacy missing seed is derived once and frozen with the debt before delivery");
        DailyReportPersistence.HasWriteBarrier = true;
        int before = DailyReportRewards.Attempts;
        DailyReportService.TryRedeliverPendingMilestones();
        Check(DailyReportRewards.Attempts == before, "write barrier prohibits grants");

        string encoded = DailyReportCodec.Encode(DailyReportPersistence.Current);
        Check(DailyReportCodec.ReadSchemaVersion(encoded) == 1, "optional extension preserves schema version and storage contract");
        Check(DailyReportCodec.Decode(encoded.Replace("\"m0_quality\":5", "\"m0_quality\":0")) == null,
            "corrupt declared debt rejects whole payload instead of swallowing or redrawing reward");
        Check(DailyReportCodec.Decode("{\"schemaVersion\":1,\"pendingMilestoneCount\":2147483647}") == null,
            "unbounded declared debt count fails closed before iteration");
        Console.WriteLine("DailyReport regression checks=" + checks);
    }
}

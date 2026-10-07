using System;
using BossRush;

internal static partial class Program
{
    static partial void CheckNavigation();
    private static int checks;
    private static void Check(bool ok, string id)
    {
        checks++;
        if (!ok) throw new Exception("ASSERT[" + id + "]");
    }
    private static Actor Spawn(Host host, int slot, bool active = true)
    {
        Request request = host.Begin(slot);
        Check(request != null, "spawn_request");
        Actor actor = new Actor();
        Check(host.Complete(request, actor, active), "spawn_complete");
        return actor;
    }
    private static void Capacity()
    {
        Host host = new Host(30, 24);
        for (int i = 0; i < 24; i++) Spawn(host, i);
        Check(host.Schedule.ActiveCount == 24 && host.Schedule.PendingCount == 0, "capacity_counts");
        Check(!host.Schedule.CanSpawn(24) && host.Begin(24) == null, "capacity_reservation");
        Check(host.Suspend(0), "capacity_free_slot");
        Request pending = host.Begin(24);
        Check(pending != null && host.Schedule.ActiveCount + host.Schedule.PendingCount == 24, "capacity_pending_budget");
        Check(!host.Activate(0), "capacity_reactivation");
        Check(host.Complete(pending, new Actor(), false), "capacity_far_completion");
        Check(host.Activate(0), "capacity_resume_after_pending");
        Check(!host.Activate(24), "capacity_reactivation_full");
        Check(host.Suspend(1) && host.Activate(24), "capacity_switch_near_zone");
        Check(host.Schedule.ActiveCount == 24 && host.Actors.Count == 25, "capacity_preserves_far_actor");
        host.Close();
    }
    private static void SinglePending()
    {
        SkyIslandPatrolSchedule schedule = new SkyIslandPatrolSchedule(3, 24);
        Check(schedule.TryReserve(0), "single_pending_first");
        int token = schedule.Generation;
        Check(!schedule.CompleteSpawn(1, token, true) && !schedule.AbortSpawn(1, token, true) &&
            !schedule.CompleteSpawn(0, token + 1, true) && schedule.PendingCount == 1 && schedule.IsPending(0), "wrong_slot_or_token");
        Check(!schedule.CanSpawn(1) && !schedule.TryReserve(1) && !schedule.TryReserve(0), "single_pending");
        Check(schedule.Generation == token && schedule.PendingCount == 1, "failed_reserve_no_token_change");
        Check(schedule.AbortSpawn(0, token, true), "retry_abort");
        Check(!schedule.AbortSpawn(0, token, true) && schedule.PendingCount == 0, "double_abort");
        Check(schedule.TryReserve(1), "released_pending_available");
    }
    private static void PreserveLiveActor()
    {
        Host host = new Host(1, 1);
        Actor original = Spawn(host, 0);
        original.HP = 37;
        object equipment = original.Equipment;
        for (int n = 0; n < 40; n++)
        {
            Check(host.Suspend(0), "near_far_suspend");
            Check(!host.Suspend(0) && host.Schedule.ActiveCount == 0, "double_suspend");
            Check(host.Schedule.IsSpawned(0) && !host.Schedule.IsDefeated(0), "suspend_not_death");
            Check(host.Begin(0) == null, "suspended_not_respawn");
            Check(host.Activate(0) && !host.Activate(0), "near_far_resume");
            Check(object.ReferenceEquals(host.Actors[0], original) && original.HP == 37 &&
                object.ReferenceEquals(original.Equipment, equipment) && !original.Destroyed, "live_identity_hp_equipment");
        }
        Check(host.Defeat(0) && !host.Defeat(0), "double_defeat");
        Check(original.DestroyCalls == 1 && host.Schedule.ActiveCount == 0, "defeat_release_once");
        Check(host.Schedule.IsDefeated(0) && !host.Schedule.IsSpawned(0) && !host.Schedule.CanSpawn(0) &&
            !host.Schedule.TryReserve(0) && !host.Schedule.TryActivate(0), "dead_not_respawn");
        host.Close();
    }
    private static void LateRetryAndDuplicate()
    {
        Host host = new Host(2, 1);
        Request old = host.Begin(0);
        Check(host.Schedule.AbortSpawn(0, old.Generation, true), "old_retry_abort");
        Request current = host.Begin(0);
        Check(current != null && current.Generation != old.Generation, "retry_new_generation");
        Actor late = new Actor();
        Check(!host.Complete(old, late, true) && late.DestroyCalls == 1 && host.Schedule.PendingCount == 1 &&
            host.Schedule.ActiveCount == 0, "stale_token");
        Check(!host.Schedule.AbortSpawn(0, old.Generation, false) && host.Schedule.IsPending(0), "old_abort_preserves_current");
        Actor accepted = new Actor();
        Check(host.Complete(current, accepted, true), "current_completion");
        Actor duplicate = new Actor();
        bool duplicateAccepted = host.Schedule.CompleteSpawn(current.Slot, current.Generation, true);
        if (!duplicateAccepted) duplicate.Destroy();
        Check(!duplicateAccepted && duplicate.DestroyCalls == 1 &&
            host.Schedule.ActiveCount == 1 && host.Schedule.PendingCount == 0, "duplicate_completion");
        Check(!host.Schedule.AbortSpawn(0, current.Generation, true), "completed_abort_rejected");
        Check(host.Actors[0] == accepted && !accepted.Destroyed, "duplicate_keeps_original");
        host.Close();
    }
    private static void FinalFailureAndPendingDefeat()
    {
        SkyIslandPatrolSchedule schedule = new SkyIslandPatrolSchedule(3, 2);
        Check(schedule.TryReserve(0), "terminal_reserve");
        int token = schedule.Generation;
        Check(schedule.AbortSpawn(0, token, false) && schedule.IsDefeated(0) && !schedule.CanSpawn(0) &&
            !schedule.TryReserve(0), "terminal_failure");
        Check(schedule.TryReserve(1), "pending_defeat_reserve");
        token = schedule.Generation;
        Check(schedule.MarkDefeated(1) && schedule.PendingCount == 0, "pending_defeat_release");
        Check(!schedule.CompleteSpawn(1, token, false) && !schedule.AbortSpawn(1, token, true), "pending_defeat_late_rejected");
        Check(schedule.TryReserve(2), "pending_defeat_frees_request");
    }
    private static void CloseAndInvalidInputs()
    {
        Host host = new Host(3, 2);
        Actor active = Spawn(host, 0);
        Actor suspended = Spawn(host, 1, false);
        Request pending = host.Begin(2);
        Check(pending != null, "close_pending_exists");
        int before = host.Schedule.Generation;
        host.Close();
        int closedToken = host.Schedule.Generation;
        Check(host.Schedule.Closed && closedToken != before && host.Schedule.ActiveCount == 0 &&
            host.Schedule.PendingCount == 0, "close_counters_generation");
        Actor late = new Actor();
        Check(!host.Complete(pending, late, true) && late.DestroyCalls == 1, "close_late_completion");
        Check(!host.Schedule.AbortSpawn(2, pending.Generation, true), "close_late_abort");
        host.Close();
        Check(host.Schedule.Generation == closedToken && active.DestroyCalls == 1 && suspended.DestroyCalls == 1, "close_idempotent");
        Check(!host.Schedule.CanSpawn(0) && !host.Schedule.TryReserve(0) && !host.Schedule.TryActivate(1) &&
            !host.Schedule.IsSpawned(0) && !host.Schedule.IsPending(2) && !host.Schedule.MarkDefeated(0), "closed_rejects_work");
        foreach (int i in new[] { -1, 3, int.MinValue, int.MaxValue })
        {
            Check(!host.Schedule.CanSpawn(i) && !host.Schedule.TryReserve(i) && !host.Schedule.TryActivate(i) &&
                !host.Schedule.Suspend(i) && !host.Schedule.MarkDefeated(i) && !host.Schedule.IsSpawned(i) &&
                !host.Schedule.IsActive(i) && !host.Schedule.IsPending(i) && !host.Schedule.IsDefeated(i) &&
                !host.Schedule.CompleteSpawn(i, closedToken, true) && !host.Schedule.AbortSpawn(i, closedToken, true), "invalid_index");
        }
        SkyIslandPatrolSchedule zero = new SkyIslandPatrolSchedule(1, 0);
        Check(!zero.CanSpawn(0) && !zero.TryReserve(0), "zero_budget");
        SkyIslandPatrolSchedule empty = new SkyIslandPatrolSchedule(0, 24);
        Check(!empty.CanSpawn(0) && empty.ActiveCount == 0, "empty_slots");
        bool rejectedCount = false, rejectedLimit = false;
        try { new SkyIslandPatrolSchedule(-1, 1); } catch (ArgumentOutOfRangeException) { rejectedCount = true; }
        try { new SkyIslandPatrolSchedule(1, -1); } catch (ArgumentOutOfRangeException) { rejectedLimit = true; }
        Check(rejectedCount && rejectedLimit, "negative_constructor");
    }
    private static void SuspendedDefeat()
    {
        Host host = new Host(1, 1);
        Actor actor = Spawn(host, 0, false);
        Check(host.Schedule.IsSpawned(0) && !host.Schedule.IsActive(0), "born_far_alive");
        Check(host.Defeat(0) && host.Schedule.ActiveCount == 0 && actor.DestroyCalls == 1, "far_defeat");
        Check(!host.Activate(0) && host.Begin(0) == null, "far_dead_not_respawn");
    }
    private static int Main()
    {
        try
        {
            CheckNavigation();
            Capacity(); LateRetryAndDuplicate(); SinglePending(); PreserveLiveActor();
            FinalFailureAndPendingDefeat(); CloseAndInvalidInputs(); SuspendedDefeat();
            Console.WriteLine("SkyIslandPatrols: PASS (7 behavior cases, " + checks + " assertions; real production schedule)");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

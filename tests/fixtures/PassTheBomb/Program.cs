using System;
using BossRush;

internal static class Program
{
    private static int _checks;

    private struct Snapshot
    {
        private readonly bool _armed;
        private readonly int _carrier;
        private readonly float _detonateAt;
        private readonly float _nextTriggerAt;

        internal Snapshot(PassTheBombRules rules)
        {
            _armed = rules.IsArmed;
            _carrier = rules.CarrierId;
            _detonateAt = rules.DetonateAt;
            _nextTriggerAt = rules.NextTriggerAt;
        }

        internal void CheckUnchanged(PassTheBombRules rules, string message)
        {
            Check(_armed == rules.IsArmed && _carrier == rules.CarrierId
                && _detonateAt == rules.DetonateAt && _nextTriggerAt == rules.NextTriggerAt,
                message);
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void RelayCanReturnToPlayer()
    {
        const int firstEnemy = 101;
        const int player = -7;
        const int secondEnemy = 202;
        var rules = new PassTheBombRules(4f, 20f);
        Check(rules.TryArm(firstEnemy, 10f), "first direct hit arms the victim");
        Check(rules.IsArmed && rules.CarrierId == firstEnemy, "first enemy carries the bomb");
        Check(rules.DetonateAt == 14f && rules.NextTriggerAt == 30f, "initial deadlines use arm time");

        Check(rules.TryTransfer(firstEnemy, player, 11f), "enemy carrier can pass bomb to player");
        Check(rules.CarrierId == player, "negative Unity instance ID is a valid carrier");
        Check(rules.DetonateAt == 14f && rules.NextTriggerAt == 30f, "enemy-to-player relay preserves both deadlines");
        Check(rules.Remaining(11f) == 3f, "relay consumes original fuse time");

        Check(rules.TryTransfer(player, secondEnemy, 12f), "player carrier can pass bomb onward");
        Check(rules.CarrierId == secondEnemy, "second enemy receives the same bomb");
        Check(rules.DetonateAt == 14f && rules.NextTriggerAt == 30f, "second relay still cannot refresh fuse or cooldown");
        Check(rules.Remaining(12f) == 2f, "relay back to combat leaves two seconds");
    }

    private static void OnlyCurrentCarrierMayPass()
    {
        var rules = new PassTheBombRules(4f, 20f);
        Check(!rules.TryTransfer(101, 202, 0f), "inactive bomb cannot transfer");
        Check(rules.TryArm(101, 1f), "arm carrier for ownership test");
        var original = new Snapshot(rules);
        Check(!rules.TryTransfer(202, 303, 2f), "unrelated attacker cannot move someone else's bomb");
        Check(!rules.TryTransfer(-7, 101, 2f), "license owner attacking carrier cannot reclaim bomb");
        Check(!rules.TryTransfer(0, 202, 2f), "missing attacker cannot transfer");
        Check(!rules.TryTransfer(101, 0, 2f), "missing victim cannot receive bomb");
        Check(!rules.TryTransfer(101, 101, 2f), "self hit cannot count as a relay");
        original.CheckUnchanged(rules, "all rejected transfers leave full state untouched");

        Check(rules.TryTransfer(101, 202, 2f), "carrier's next direct hit transfers once");
        var transferred = new Snapshot(rules);
        Check(!rules.TryTransfer(101, 303, 2f), "same attacker hitting another target cannot fan out bomb");
        transferred.CheckUnchanged(rules, "multi-target attack does not rewrite successful first recipient");
        Check(rules.TryTransfer(202, 303, 2f), "new carrier can relay at the same timestamp");
    }

    private static void DeadlineIsNeverRenewed()
    {
        var rules = new PassTheBombRules(4f, 20f);
        Check(rules.TryArm(101, 10f), "arm boundary test");
        Check(!rules.IsDue(13.75f) && rules.Remaining(13.75f) == 0.25f, "fuse remains active before deadline");
        Check(rules.TryTransfer(101, -7, 13.75f), "last-quarter-second relay is allowed");
        var beforeExpiry = new Snapshot(rules);
        Check(!rules.TryTransfer(-7, 202, 14f), "relay at exact deadline cannot save carrier");
        Check(!rules.TryTransfer(-7, 202, 14.25f), "overdue relay cannot extend lifetime");
        beforeExpiry.CheckUnchanged(rules, "deadline rejection retains carrier for detonation");
        Check(rules.IsDue(14f) && rules.IsDue(15f), "deadline query is inclusive");
        Check(rules.Remaining(14f) == 0f && rules.Remaining(15f) == 0f, "expired display never goes negative");
        Check(rules.IsArmed && rules.CarrierId == -7, "expiry is observable until runtime completes detonation");
        Check(!rules.TryArm(303, 40f), "overdue live bomb cannot be overwritten before completion");
    }

    private static void SingleBombAndCooldown()
    {
        var rules = new PassTheBombRules(10f, 2f);
        Check(rules.TryArm(101, 0f), "time zero can arm first bomb");
        var armed = new Snapshot(rules);
        Check(!rules.TryArm(202, 3f), "elapsed trigger cooldown cannot create a second live bomb");
        armed.CheckUnchanged(rules, "second arm leaves original carrier and deadlines intact");
        rules.Complete();
        Check(!rules.IsArmed && rules.CarrierId == 0 && rules.DetonateAt == 0f, "complete clears bomb state");
        Check(rules.NextTriggerAt == 2f, "complete retains trigger cooldown");
        Check(!rules.IsDue(100f) && rules.Remaining(100f) == 0f, "completed bomb has no fuse");
        Check(rules.TryArm(202, 3f), "new bomb may start after cooldown and completion");

        rules = new PassTheBombRules(4f, 20f);
        Check(rules.TryArm(101, 10f), "arm cooldown cancellation test");
        rules.Complete(); // Runtime uses this same operation for unequip/death cancellation.
        rules.Complete();
        Check(rules.NextTriggerAt == 30f, "repeated cancel or unequip does not refund cooldown");
        var cancelled = new Snapshot(rules);
        Check(!rules.TryArm(202, 29.75f), "reequip cannot bypass remaining cooldown");
        cancelled.CheckUnchanged(rules, "cooldown rejection changes no state");
        Check(rules.TryArm(202, 30f), "next trigger is allowed exactly at cooldown deadline");
        Check(rules.DetonateAt == 34f && rules.NextTriggerAt == 50f, "next successful arm starts independent deadlines");
    }

    private static void SessionReset()
    {
        var rules = new PassTheBombRules(4f, 20f);
        Check(rules.TryArm(101, 100f), "arm before scene or session reset");
        rules.ResetSession();
        rules.ResetSession();
        Check(!rules.IsArmed && rules.CarrierId == 0 && rules.DetonateAt == 0f
            && rules.NextTriggerAt == 0f, "session reset is idempotent and clears both bomb and cooldown");
        Check(rules.TryArm(-7, 0f), "fresh session accepts a new zero-based game clock");
        Check(rules.DetonateAt == 4f && rules.NextTriggerAt == 20f, "configuration survives reset");
    }

    private static void InvalidInputCannotCorruptState()
    {
        float[] invalid = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, -float.Epsilon };
        var rules = new PassTheBombRules(4f, 20f);
        var inactive = new Snapshot(rules);
        Check(!rules.TryArm(0, 0f), "zero victim ID cannot arm");
        inactive.CheckUnchanged(rules, "invalid first victim leaves fresh state untouched");
        foreach (float now in invalid)
        {
            Check(!rules.TryArm(101, now), "invalid initial clock cannot arm");
            inactive.CheckUnchanged(rules, "invalid initial clock leaves state untouched");
        }
        Check(rules.TryArm(101, 10f), "valid arm after rejected clocks succeeds");
        var armed = new Snapshot(rules);
        foreach (float now in invalid)
        {
            Check(!rules.TryTransfer(101, 202, now), "invalid clock cannot transfer");
            Check(!rules.TryArm(202, now), "invalid clock cannot overwrite armed bomb");
            Check(!rules.IsDue(now), "invalid clock cannot detonate");
            Check(rules.Remaining(now) == 0f, "invalid clock cannot yield NaN or infinite HUD time");
            armed.CheckUnchanged(rules, "invalid queries and mutations preserve armed state");
        }
        rules.Complete();
        var coolingDown = new Snapshot(rules);
        Check(!rules.TryArm(202, float.MaxValue), "clock beyond useful float precision cannot arm");
        coolingDown.CheckUnchanged(rules, "precision rejection preserves old cooldown");

        var hugeDuration = new PassTheBombRules(float.MaxValue, float.MaxValue);
        Check(!hugeDuration.TryArm(101, float.MaxValue), "finite inputs producing infinite deadlines are rejected");
        Check(!hugeDuration.IsArmed && hugeDuration.NextTriggerAt == 0f, "overflow rejection cannot partially commit");
    }

    private static void InvalidConfiguration()
    {
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            ExpectArgumentError(() => new PassTheBombRules(invalid, 20f), "fuseSeconds");
            ExpectArgumentError(() => new PassTheBombRules(4f, invalid), "cooldownSeconds");
        }
    }

    private static void ExpectArgumentError(Action create, string parameterName)
    {
        try { create(); }
        catch (ArgumentOutOfRangeException error)
        {
            Check(error.ParamName == parameterName, "invalid duration reports its parameter");
            return;
        }
        Check(false, "invalid duration must be rejected: " + parameterName);
    }

    private static int Main()
    {
        RelayCanReturnToPlayer();
        OnlyCurrentCarrierMayPass();
        DeadlineIsNeverRenewed();
        SingleBombAndCooldown();
        SessionReset();
        InvalidInputCannotCorruptState();
        InvalidConfiguration();
        Console.WriteLine("PassTheBomb rules: " + _checks + " checks passed.");
        RuntimeChecks.Run();
        return 0;
    }
}

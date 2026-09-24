using System;
using System.Collections;

sealed class WaitForSeconds
{
    public readonly float Seconds;
    public WaitForSeconds(float seconds) { Seconds = seconds; }
}

sealed class BirthdayCakeGiftProbe
{
    public int GrantCheckCalls;

    private void CheckAndGiveDecemberBirthdayCake()
    {
        GrantCheckCalls++;
    }

    // The regression runner inserts the production DelayedBirthdayCakeGift method here.
}

static class Program
{
    private static int checks;

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        checks++;
    }

    private static void Main()
    {
        var probe = new BirthdayCakeGiftProbe();
        IEnumerator routine = probe.DelayedBirthdayCakeGift();

        Check(routine.MoveNext(), "gift routine yields before checking the player");
        var wait = routine.Current as WaitForSeconds;
        Check(wait != null && wait.Seconds == 2f, "gift routine retains its two-second scene-load delay");
        Check(probe.GrantCheckCalls == 0, "grant check does not run before the delay finishes");

        Check(!routine.MoveNext(), "gift routine completes after the grant check");
        Check(probe.GrantCheckCalls == 1, "gift routine invokes exactly one grant check");
        Check(!routine.MoveNext() && probe.GrantCheckCalls == 1, "completed gift routine does not repeat the grant check");

        Console.WriteLine("BirthdayCakeGift: PASS " + checks);
    }
}
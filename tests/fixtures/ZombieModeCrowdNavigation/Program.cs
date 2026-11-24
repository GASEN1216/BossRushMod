using System;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }

    private static ZombieModeEnemyRuntimeMarker Requested(float time, Vector3 origin, Vector3 target)
    {
        return new ZombieModeEnemyRuntimeMarker { HasPathRequest = true, LastPathRequestTime = time, LastPathRequestOrigin = origin, LastPathRequestTarget = target };
    }

    private static bool Throttle(ZombieModeEnemyRuntimeMarker marker, bool waiting, bool hasPath, bool reachedEnd, Vector3 self, Vector3 target, float now)
    {
        return ZombieModeCrowdNavigation.ShouldThrottleZombieModePathRequest(marker, waiting, hasPath, reachedEnd, self, target, now);
    }

    private static void Decisions()
    {
        Vector3 origin = new Vector3(0, 0, 0);
        Vector3 near = new Vector3(5, 0, 0);
        Vector3 far = new Vector3(60, 0, 0);
        Vector3 mid = new Vector3(24, 0, 0);

        Assert(!Throttle(new ZombieModeEnemyRuntimeMarker(), false, true, false, origin, far, 10f), "The first request is always allowed");
        Assert(!Throttle(null, true, false, false, origin, far, 10f), "Unknown markers never block official pathing");

        var marker = Requested(10f, origin, far);
        Assert(Throttle(marker, true, false, false, origin, far, 11f), "An in-flight request must not be cancelled by the next 0.15 s trace tick");
        Assert(!Throttle(marker, true, false, false, origin, far, 10f + ZombieModeTuning.PathRequestPendingTimeoutSeconds), "A request stuck past the pending timeout is replaced");

        Assert(!Throttle(marker, false, false, false, origin, far, 10.05f), "Without a path (failed or cleared) the zombie may ask again at once");
        Assert(!Throttle(marker, false, true, true, origin, far, 10.05f), "Reaching the end of the current path always re-paths");

        Assert(Throttle(marker, false, true, false, origin, far, 10.1f), "Re-path faster than the official cadence is blocked");
        Assert(!Throttle(marker, false, true, false, new Vector3(0, 0, 20), far, 10.1f), "A teleported zombie (recovery, unstack, gravity well) re-paths immediately");

        marker = Requested(10f, origin, near);
        Assert(!Throttle(marker, false, true, false, origin, near, 10f + ZombieModeTuning.PathRepathMinIntervalSeconds), "Close zombies keep the official 0.15 s cadence");

        marker = Requested(10f, origin, far);
        Assert(Throttle(marker, false, true, false, origin, far, 10.5f), "Far zombies with an unchanged target keep their path");
        Assert(!Throttle(marker, false, true, false, origin, far, 10f + ZombieModeTuning.PathRepathMaxIntervalSeconds), "Far zombies still re-path at the maximum interval");
        Assert(!Throttle(marker, false, true, false, origin, new Vector3(60, 0, 15), 10.5f), "A target shift beyond the tolerance re-paths early");
        Assert(Throttle(marker, false, true, false, origin, new Vector3(60, 0, 5), 10.5f), "Small target drift does not re-path a far zombie");
        Assert(Throttle(marker, false, true, false, origin, new Vector3(60, 9, 0), 10.5f), "Vertical target bob alone is not a target shift");

        marker = Requested(10f, origin, mid);
        float midInterval = ZombieModeTuning.PathRepathMinIntervalSeconds +
            (ZombieModeTuning.PathRepathMaxIntervalSeconds - ZombieModeTuning.PathRepathMinIntervalSeconds) *
            ((24f - ZombieModeTuning.PathRepathNearDistance) / (ZombieModeTuning.PathRepathFarDistance - ZombieModeTuning.PathRepathNearDistance));
        Assert(Throttle(marker, false, true, false, origin, mid, 10f + midInterval - ZombieModeTuning.PathRepathTimingSlackSeconds - 0.02f), "Mid-range interval is interpolated by distance (before)");
        Assert(!Throttle(marker, false, true, false, origin, mid, 10f + midInterval + 0.02f), "Mid-range interval is interpolated by distance (after)");

        Assert(!Throttle(Requested(10f, origin, far), false, true, false, origin, far, 3f), "A clock reset (new run) never blocks");
        Assert(!Throttle(Requested(10f, origin, far), false, true, false, origin, new Vector3(float.NaN, 0, 0), 10.5f), "Non-finite targets fall through to official handling");
        Assert(!Throttle(Requested(float.NaN, origin, far), false, true, false, origin, far, 10.5f), "Corrupt request time never blocks");
    }

    /// <summary>
    /// 复现 A* 队列积压：每次寻路要 0.4 秒才回调，官方 TraceTarget 每 0.15 秒重发一次并取消在途请求。
    /// 不节流时路径永远到不了；节流后每次请求都能算完。
    /// </summary>
    private static int DeliveredPaths(bool throttle, float pathLatency)
    {
        var marker = new ZombieModeEnemyRuntimeMarker();
        Vector3 self = new Vector3(0, 0, 0);
        Vector3 target = new Vector3(30, 0, 0);
        bool waiting = false, hasPath = false;
        float requestStart = 0f;
        int delivered = 0;
        for (int tick = 0; tick <= 40; tick++)
        {
            float now = tick * 0.15f;
            if (waiting && now - requestStart >= pathLatency)
            {
                waiting = false;
                hasPath = true;
                delivered++;
            }

            if (throttle && ZombieModeCrowdNavigation.ShouldThrottleZombieModePathRequest(marker, waiting, hasPath, false, self, target, now))
            {
                continue;
            }

            marker.HasPathRequest = true;
            marker.LastPathRequestTime = now;
            marker.LastPathRequestTarget = target;
            marker.LastPathRequestOrigin = self;
            // 官方 MoveToPos：清空路径、取消在途请求、重新排队。
            hasPath = false;
            waiting = true;
            requestStart = now;
        }
        return delivered;
    }

    private static void QueueStarvation()
    {
        Assert(DeliveredPaths(false, 0.4f) == 0, "Simulation must reproduce the starvation: official cadence cancels every slow request");
        Assert(DeliveredPaths(true, 0.4f) >= 4, "Throttled requests must complete under the same queue latency");
        Assert(DeliveredPaths(true, 0.1f) >= 6, "With a fast queue a far zombie still re-paths at least once per second");
    }

    public static void Main()
    {
        Decisions();
        QueueStarvation();
        Console.WriteLine("PASS ZombieModeCrowdNavigation (" + assertions + " assertions)");
    }
}

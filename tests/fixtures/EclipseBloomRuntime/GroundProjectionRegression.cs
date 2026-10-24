using System;
using BossRush;
using UnityEngine;

internal static class GroundProjectionRegression
{
    internal static void Run(Action<bool, string> check)
    {
        int calls = 0;
        var flat = new EclipseBloomGroundProjection((Vector3 point, out float height) => { calls++; height = 0f; return true; });
        float sampled;
        check(!flat.TryHeight(Vector3.zero, out sampled), "unsampled ground cannot authorize an invisible warning");
        Time.frameCount++; flat.Step(Vector3.zero);
        check(calls == 96 && !flat.TryHeight(Vector3.zero, out sampled), "first frame respects the ninety six sample budget and leaves unvisited ground unavailable");
        flat.Step(Vector3.zero); flat.Step(Vector3.zero);
        check(calls == 96, "repeated visual calls in one frame share a single ground sampling budget");
        Fill(flat, Vector3.zero, check, () => calls);
        check(calls == 1089 && flat.TryHeight(new Vector3(0.25f, 0f, 0.75f), out sampled) && sampled == 0f,
            "completed production grid covers flat ground without inventing extra samples");
        Time.frameCount++; flat.Step(Vector3.zero);
        check(calls == 1089 && !flat.TryHeight(new Vector3(17f, 0f, 0f), out sampled), "finished cache is idle and outside points remain unavailable");
        Time.frameCount++; flat.Step(new Vector3(3f, 0f, 0f));
        check(calls == 1185 && !flat.TryHeight(Vector3.zero, out sampled), "moving beyond two meters invalidates old heights before rebuilding the cache");

        var slope = new EclipseBloomGroundProjection((Vector3 point, out float height) => { height = point.x * 0.03f + point.z * 0.02f; return true; });
        Fill(slope, Vector3.zero, check);
        check(slope.TryHeight(new Vector3(0.3f, 0f, 0.6f), out sampled) && Math.Abs(sampled - 0.021f) < 0.00001f,
            "gentle ground slope interpolates the actual sampled height");
        var hole = new EclipseBloomGroundProjection((Vector3 point, out float height) => { height = 0f; return point.x != 0f || point.z != 0f; });
        Fill(hole, Vector3.zero, check);
        check(!hole.TryHeight(new Vector3(0.2f, 0f, 0.2f), out sampled) && hole.TryHeight(new Vector3(2f, 0f, 2f), out sampled),
            "a missing ground corner cuts its cell while leaving nearby valid ground available");
        var ledge = new EclipseBloomGroundProjection((Vector3 point, out float height) => { height = point.x < 0f ? 0f : 1f; return true; });
        Fill(ledge, Vector3.zero, check);
        check(!ledge.TryHeight(new Vector3(-0.2f, 0f, 0.1f), out sampled), "a height discontinuity cannot bridge two floors with a false warning plane");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 3f })
        {
            var unavailable = new EclipseBloomGroundProjection((Vector3 point, out float height) => { height = invalid; return true; });
            Fill(unavailable, Vector3.zero, check);
            check(!unavailable.TryHeight(Vector3.zero, out sampled), "invalid or distant floor heights remain unavailable to damage geometry");
        }
        var probe = new EclipseBloomGroundProjection(); int rays = Physics.RaycastCalls;
        Time.frameCount++; probe.Step(Vector3.zero);
        check(Physics.RaycastCalls - rays == 96, "default production ground probe also obeys its real raycast budget");

        Vector3 a = new Vector3(0f, 0f, 0f), b = new Vector3(4f, 0.4f, 0f), c = new Vector3(0f, 0.8f, 4f);
        check(EclipseBloomGroundProjection.ContainsTriangle(new Vector3(1f, 0.3f, 1f), a, b, c), "damage recognizes the actual projected sloped triangle");
        check(!EclipseBloomGroundProjection.ContainsTriangle(new Vector3(3f, 0.3f, 3f), a, b, c), "bounding box ground outside the rendered triangle is harmless");
        check(!EclipseBloomGroundProjection.ContainsTriangle(new Vector3(1f, 0.751f, 1f), a, b, c), "other floor above the rendered warning is harmless");
        check(EclipseBloomGroundProjection.ContainsTriangle(new Vector3(1f, 0.749f, 1f), a, b, c), "point just inside the projected warning height tolerance remains damageable");
        check(!EclipseBloomGroundProjection.ContainsTriangle(Vector3.zero, a, a, a), "collapsed projected triangles never authorize damage");
    }

    private static void Fill(EclipseBloomGroundProjection projection, Vector3 center, Action<bool, string> check, Func<int> count = null)
    {
        for (int frame = 0; frame < 12; frame++)
        {
            int before = count != null ? count() : 0; Time.frameCount++; projection.Step(center);
            if (count != null) check(count() - before <= 96, "every resampling frame respects its independent sample budget");
        }
    }
}

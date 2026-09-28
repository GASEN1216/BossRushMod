using System;
using BossRush;

// 只替代宿主时钟、场景身份和分项取数；窗口迭代器、开关录制门与完整性判据均取生产代码。
internal static class Time
{
    internal static double realtimeSinceStartupAsDouble;
    internal static float unscaledDeltaTime;
}
internal static class Mathf
{
    internal static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
    internal static int Clamp(int value, int min, int max) { return Math.Min(max, Math.Max(min, value)); }
    internal static float Max(float a, float b) { return Math.Max(a, b); }
}
internal static class SceneManager
{
    internal struct Scene { internal int handle; }
    internal static int Handle;
    internal static Scene GetActiveScene() { return new Scene { handle = Handle }; }
}
namespace BossRush
{
    internal static class SkyIslandFrameProfile
    {
        internal static bool Recording;
        internal static int Opened, Closed;
        internal static void BeginRecording() { Recording = true; Opened++; }
    }
}

internal static class PerformanceWindows
{
    private static ProductionPerformance Fresh()
    {
        Time.realtimeSinceStartupAsDouble = 0;
        Time.unscaledDeltaTime = 0.02f;
        SceneManager.Handle = 7;
        SkyIslandFrameProfile.Recording = false;
        SkyIslandFrameProfile.Opened = SkyIslandFrameProfile.Closed = 0;
        return new ProductionPerformance();
    }

    internal static void Run(Action<bool, string> check)
    {
        var full = Fresh();
        using (var stack = new ValidationCoroutineStack(full.Run()))
            while (stack.MoveNext()) Time.realtimeSinceStartupAsDouble += 0.25;
        check(full.Results.Count == 1 && full.Results[0] == "PASS" && full._baselineP95Ms == 20f,
            "performance: full same-scene window updates baseline and passes");
        check(!SkyIslandFrameProfile.Recording && SkyIslandFrameProfile.Closed == 1,
            "performance: normal completion closes profile once");

        var changed = Fresh();
        using (var stack = new ValidationCoroutineStack(changed.Run()))
        {
            check(stack.MoveNext(), "performance: scene-change window begins");
            Time.realtimeSinceStartupAsDouble = 0.5;
            SceneManager.Handle = 8;
            while (stack.MoveNext()) Time.realtimeSinceStartupAsDouble += 0.25;
        }
        check(changed.Results.Count == 1 && changed.Results[0] == "FAIL" && changed.Reasons[0] == "scene_changed"
            && changed._baselineP95Ms == 13f && changed._baselineMemory == 11,
            "performance: mixed-scene frames never pass or replace baseline");

        var cancelled = Fresh();
        using (var stack = new ValidationCoroutineStack(cancelled.Run()))
        {
            check(stack.MoveNext(), "performance: cancellation window begins");
            Time.realtimeSinceStartupAsDouble = 0.5;
            cancelled.Cancelled = true;
            while (stack.MoveNext()) { }
        }
        check(cancelled.Results.Count == 1 && cancelled.Results[0] == "FAIL" && cancelled.Reasons[0] == "cancelled",
            "performance: directly observed cancellation cannot pass");

        var disposed = Fresh();
        using (var stack = new ValidationCoroutineStack(disposed.Run()))
        {
            check(stack.MoveNext() && SkyIslandFrameProfile.Recording, "performance: disposing an active window");
            Time.realtimeSinceStartupAsDouble = 0.5;
            disposed._skyIslandMode = false; // CompleteSession 先复位当前模式，再释放迭代器。
            stack.Dispose();
            stack.Dispose();
        }
        check(disposed.Results.Count == 1 && disposed.Results[0] == "FAIL" && disposed.Reasons[0] == "cancelled"
            && !SkyIslandFrameProfile.Recording && SkyIslandFrameProfile.Closed == 1 && disposed._baselineP95Ms == 13f,
            "performance: Dispose closes the original island recording after mode reset");

        var sparse = Fresh();
        using (var stack = new ValidationCoroutineStack(sparse.Run()))
        {
            check(stack.MoveNext(), "performance: sparse window begins");
            Time.realtimeSinceStartupAsDouble = 5;
            while (stack.MoveNext()) { }
        }
        check(sparse.Results[0] == "FAIL" && sparse.Reasons[0] == "insufficient_frames",
            "performance: elapsed duration cannot replace required samples");

        var dense = Fresh();
        Time.unscaledDeltaTime = 0.06f;
        using (var stack = new ValidationCoroutineStack(dense.Run(5f, false)))
            while (stack.MoveNext()) Time.realtimeSinceStartupAsDouble += 0.25;
        check(dense.Results[0] == "FAIL" && dense.Reasons[0] == "超过性能阈值",
            "performance: complete slow final window retains existing frame threshold");

        string reason;
        check(ResourcePerformanceMetrics.IsComplete(5, 5, 20, false, true, false, out reason)
            && !ResourcePerformanceMetrics.IsComplete(5, 20, false, true, false, out reason)
            && reason == "window_incomplete", "performance: explicit 5s window preserves default 10s resource contract");
    }
}

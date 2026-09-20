#if BOSSRUSH_DEV
using System;

namespace BossRush
{
    [Serializable]
    internal sealed class ResourceMetric
    {
        public string status = "unavailable", unit, source;
        public int samples;
        public double p50 = -1, p95 = -1, mean = -1, peak = -1;
    }

    internal static class ResourcePerformanceMetrics
    {
        #region 纯判据
        internal static ResourceMetric Summarize(double[] values, int count, string unit, string source)
        {
            var result = new ResourceMetric { unit = unit, source = source };
            if (values == null || count <= 0 || count > values.Length) return result;
            var sorted = new double[count];
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || values[i] < 0) return result;
                sorted[i] = values[i]; sum += values[i];
            }
            Array.Sort(sorted);
            result.status = "available"; result.samples = count;
            result.p50 = sorted[(int)Math.Ceiling(count * .50) - 1];
            result.p95 = sorted[(int)Math.Ceiling(count * .95) - 1];
            result.mean = sum / count; result.peak = sorted[count - 1];
            return result;
        }

        internal static bool IsComplete(double seconds, int frames, bool cancelled, bool sameScene, bool overflow, out string reason)
        {
            reason = cancelled ? "cancelled" : !sameScene ? "scene_changed" : overflow ? "sample_capacity_exceeded"
                : double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 10 ? "window_incomplete"
                : frames < 2 ? "insufficient_frames" : null;
            return reason == null;
        }
        #endregion
    }
}
#endif

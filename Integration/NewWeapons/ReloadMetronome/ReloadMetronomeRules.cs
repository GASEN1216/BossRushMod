using System;

namespace BossRush
{
    internal enum ReloadMetronomeResult { Waiting, Early, Perfect, Late, Inactive }

    /// <summary>一次成功开始的装填只接受一次尝试；UI 与判定共用同一个实时窗口。</summary>
    internal sealed class ReloadMetronomeRules
    {
        internal bool Active { get; private set; }
        internal ReloadMetronomeResult Result { get; private set; }

        internal static bool IsSupportedDuration(float total)
        {
            return !float.IsNaN(total) && !float.IsInfinity(total)
                && total >= ReloadMetronomeConfig.MinimumReloadSeconds;
        }

        internal static void GetWindow(float total, out float start, out float end)
        {
            float width = Math.Min(ReloadMetronomeConfig.MaximumWindowSeconds,
                Math.Max(ReloadMetronomeConfig.MinimumWindowSeconds, total * ReloadMetronomeConfig.WindowFraction));
            start = total * ReloadMetronomeConfig.WindowCenter - width * 0.5f;
            end = start + width;
        }

        internal void Begin(float total)
        {
            Active = IsSupportedDuration(total);
            Result = Active ? ReloadMetronomeResult.Waiting : ReloadMetronomeResult.Inactive;
        }

        internal ReloadMetronomeResult Press(float current, float total)
        {
            if (!Active || Result != ReloadMetronomeResult.Waiting) return ReloadMetronomeResult.Inactive;
            if (!IsSupportedDuration(total) || float.IsNaN(current) || float.IsInfinity(current)
                || current < 0f || current >= total)
            {
                Reset();
                return ReloadMetronomeResult.Inactive;
            }
            float start, end;
            GetWindow(total, out start, out end);
            Result = current < start ? ReloadMetronomeResult.Early
                : current <= end ? ReloadMetronomeResult.Perfect : ReloadMetronomeResult.Late;
            return Result;
        }

        internal void Reset()
        {
            Active = false;
            Result = ReloadMetronomeResult.Inactive;
        }
    }
}

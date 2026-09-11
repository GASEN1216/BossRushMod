using System;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛「现在是不是夜里」的唯一口径——光照的星夜整档、夜风、云蚋三处共用这一份钟点与这一个判断。
    ///
    /// 为什么要收成一处：
    /// - 以前光照（`SkyIslandLighting.ResolveTimeBlend` 的 21–5 点）与夜风（`SkyIslandFieldcraftRules.IsNight`）各写了一份小时数，
    ///   云蚋再写第三份就会出现「天黑了、风没起、蚊子却来了」这种三处对不上的情形。
    /// - 官方 `GameClock` 没有实例时 `TimeOfDay` 恒为 00:00（反编译源 `GameClock.cs` 的 `SecondsOfDay`），照读会把整趟判成夜里；
    ///   这里约定**读不到时钟就当不是夜里**（光照回退晴昼），由 <see cref="EffectiveHours"/> 把「没有时钟」变成 NaN。
    /// - 不用官方 `TimeOfDayController.AtNight`：它是 19–5 点，与岛上的光和风差两个小时。
    ///
    /// 纯逻辑、无 Unity 依赖：隔离回归（`SkyIslandStory` 与 `SkyIslandLighting` 两个夹具）直接执行。
    /// 运行时读钟只有 `SkyIslandLighting.ClockHours()` 一处。
    /// </summary>
    internal static class SkyIslandNight
    {
        /// <summary>夜里从 21 点开始（与光照的「星夜」整档一致）。</summary>
        internal const double StartHour = 21.0;
        /// <summary>夜里到次日 5 点结束。</summary>
        internal const double EndHour = 5.0;
        /// <summary>开发构建「强制夜里」时读出的钟点：星夜整档的正中。</summary>
        internal const double ForcedHour = 23.0;

        /// <summary>
        /// Dev 构建 F3 天空岛面板的「强制夜里」开关：只改本 Mod 读出来的钟点，**不拨官方时钟**（时钟随存档保存）。
        /// 正式构建里没有入口，恒为 false；模块销毁时由 <see cref="ResetStaticCaches"/> 复位。
        /// </summary>
        internal static bool DevForceNight;

        /// <summary>21 点到次日 5 点算夜里；非有限值（读不到时钟）一律不算。</summary>
        internal static bool IsNight(double hours)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours)) return false;
            hours = (hours % 24 + 24) % 24;
            return hours >= StartHour || hours < EndHour;
        }

        /// <summary>
        /// 本 Mod 该用的钟点：开发开关打开时是 <see cref="ForcedHour"/>；官方时钟没有实例时是 NaN（不是夜里、光照回退晴昼）；
        /// 否则原样返回官方钟点。
        /// </summary>
        internal static double EffectiveHours(bool clockAvailable, double clockHours)
        {
            if (DevForceNight) return ForcedHour;
            return clockAvailable ? clockHours : double.NaN;
        }

        /// <summary>由 <c>SkyIslandRuntimeModule.OnDestroy</c> 调用：开发开关不跨进程残留。</summary>
        internal static void ResetStaticCaches()
        {
            DevForceNight = false;
        }
    }
}

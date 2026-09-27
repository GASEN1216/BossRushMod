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
    /// - 边界与官方 `TimeOfDayController` 同相（`morningStart = 6 / nightStart = 22`，2026-09-25 F3 实机读运行时实例）。
    ///   反编译源里的字段初值 5 / 19 会被官方 `LevelManagerPrefab` 的序列化值盖掉，不能照抄（09-16 照抄初值定成 19–5，
    ///   岛上比官方早 3 小时入夜、早 1 小时天亮）。仍不读官方 `AtNight`：它依赖场景里的 controller 实例、分段函数是私有的，
    ///   纯规则夹具也测不了；官方若改这两个值，这里要跟着改（Dev 只读用例 `SKY_NIGHT_BOUNDARY_OFFICIAL` 实机比对）。
    ///
    /// 纯逻辑、无 Unity 依赖：隔离回归（`SkyIslandStory` 与 `SkyIslandLighting` 两个夹具）直接执行。
    /// 运行时读钟只有 `SkyIslandLighting.ClockHours()` 一处。
    /// </summary>
    internal static class SkyIslandNight
    {
        /// <summary>夜里从 22 点开始（对齐官方 `TimeOfDayController.nightStart` 的 prefab 实机值，与光照的「星夜」整档一致）。</summary>
        internal const double StartHour = 22.0;
        /// <summary>夜里到次日 6 点结束（对齐官方 `morningStart` 的 prefab 实机值）。</summary>
        internal const double EndHour = 6.0;
        /// <summary>开发构建「强制夜里」时读出的钟点：22–6 这一段的中段，给驱蚋演练留足天亮前的余量。</summary>
        internal const double ForcedHour = 2.0;

        /// <summary>
        /// Dev 构建 F3 天空岛面板的「强制夜里」开关：只改本 Mod 读出来的钟点，**不拨官方时钟**（时钟随存档保存）。
        /// 正式构建里没有入口，恒为 false；模块销毁时由 <see cref="ResetStaticCaches"/> 复位。
        /// </summary>
        internal static bool DevForceNight;

        /// <summary>22 点到次日 6 点算夜里；非有限值（读不到时钟）一律不算。</summary>
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

        /// <summary>
        /// 官方时钟的默认倍率（反编译源 <c>GameClock.clockTimeScale = 60f</c>）：一现实秒走 60 游戏秒，
        /// 于是一整夜（22–6 点，8 游戏小时）约合 8 现实分钟。读不到实例时按它折算。
        /// </summary>
        internal const double DefaultClockScale = 60.0;

        /// <summary>
        /// 距天亮还有多少**现实秒**。云蚋、夜风与光照都按现实秒推进（<c>Time.time</c>），而钟点按
        /// <paramref name="clockScale"/> 倍速走，所以「今晚还剩多久」必须折算过来才能和刷新间隔比。
        ///
        /// 不是夜里、读不到时钟（NaN）或倍率非正时返回 0——调用方据此判定「这一趟不派驱蚋委托」。
        /// 纯算术，隔离回归直接执行。
        /// </summary>
        internal static double RealSecondsUntilDawn(double hours, double clockScale)
        {
            if (!IsNight(hours)) return 0.0;
            if (double.IsNaN(clockScale) || double.IsInfinity(clockScale) || clockScale <= 0.0) return 0.0;
            hours = (hours % 24 + 24) % 24;
            // 入夜之后要先走到午夜再走到天亮；午夜之后直接走到天亮。
            double remainingHours = hours >= StartHour ? 24.0 - hours + EndHour : EndHour - hours;
            return remainingHours * 3600.0 / clockScale;
        }

        /// <summary>由 <c>SkyIslandRuntimeModule.OnDestroy</c> 调用：开发开关不跨进程残留。</summary>
        internal static void ResetStaticCaches()
        {
            DevForceNight = false;
        }
    }
}

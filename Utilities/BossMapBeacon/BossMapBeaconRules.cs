// ============================================================================
// BossMapBeaconRules.cs - 烽烟圈的纯规则（玩家许愿第 8 项「Boss 地图标记」）
// ============================================================================
// 这里只放数字与判定，不碰 Unity：执行回归 BossMapBeaconRules 直接编译本文件跑断言。
//
// 一只 Boss 在 M 图上的样子分两段：
//   1. 烽烟：一圈朱砂色的区域，圆心在 Boss 附近随机偏开（最多是半径的 15/35，35 米那档就是 15 米），
//      每 8 秒重新落一次，所以只告诉你「大概在这一片」。
//   2. 显形：变成钉在 Boss 身上的实时点，换烽火图标、放大 1.3 倍。
// 收紧：玩家既没走近、也没打到它，每安静 60 秒收一档：35 → 20 → 10 米。玩家打到它就重新计时。
// 显形条件：玩家走进 40 米内（水平距离），或者已经收到 10 米那档又安静了 60 秒。显形后不再变回烽烟。
// 显形那一刻在 Boss 身上冲起一道暗红墨烟光柱，全场同一时刻最多一道（BossMapBeaconFx）。
// 同屏最多 8 个烽烟圈；空位按离玩家最近的补（BossMapBeaconRuntimeModule）。
// ============================================================================

using System;

namespace BossRush
{
    internal static class BossMapBeaconRules
    {
        internal const int MaxBeacons = 8;
        internal const float RecenterSeconds = 8f;
        internal const float QuietStageSeconds = 60f;
        internal const float RevealDistance = 40f;
        internal const float RevealedScale = 1.3f;
        internal const float PillarSeconds = 1.5f;
        internal const float MaxOffsetMeters = 15f;
        internal const int LastStage = 2;

        private static readonly float[] StageRadii = { 35f, 20f, 10f };

        /// <summary>这一档烽烟圈的半径（米）。越界按最近的一档算。</summary>
        internal static float RadiusOf(int stage)
        {
            if (stage < 0) stage = 0;
            if (stage > LastStage) stage = LastStage;
            return StageRadii[stage];
        }

        /// <summary>圆心最多偏开多远：35 米那档 15 米，其余按同一比例（Boss 始终落在圈里）。</summary>
        internal static float MaxOffset(int stage)
        {
            return RadiusOf(stage) * (MaxOffsetMeters / StageRadii[0]);
        }

        /// <summary>安静够 60 秒就收一档，最多收到 10 米那档。</summary>
        internal static int StageAfterQuiet(int stage, float quietSeconds)
        {
            return quietSeconds >= QuietStageSeconds && stage < LastStage ? stage + 1 : stage;
        }

        /// <summary>该不该显形：玩家走进 40 米内，或者已经收到最后一档又安静了 60 秒。</summary>
        internal static bool ShouldReveal(int stage, float quietSeconds, float playerDistance)
        {
            return playerDistance <= RevealDistance || (stage >= LastStage && quietSeconds >= QuietStageSeconds);
        }

        /// <summary>这一刻能不能放光柱：上一道还没散（busyUntil 之前）就不放。</summary>
        internal static bool CanPlayPillar(float now, float busyUntil)
        {
            return now >= busyUntil;
        }

        /// <summary>
        /// 圆心偏移：在半径为 MaxOffset(stage) 的圆盘里均匀取一点。u1、u2 是 [0,1) 的随机数（调用方给，便于回归钉住）。
        /// </summary>
        internal static void Offset(int stage, double u1, double u2, out float x, out float z)
        {
            double radius = MaxOffset(stage) * Math.Sqrt(Clamp01(u1));
            double angle = Clamp01(u2) * Math.PI * 2.0;
            x = (float)(Math.Cos(angle) * radius);
            z = (float)(Math.Sin(angle) * radius);
        }

        private static double Clamp01(double value)
        {
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }
    }
}

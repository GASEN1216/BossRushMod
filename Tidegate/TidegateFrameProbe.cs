// 调试采样只由 F3 窗口持有；正式构建中 Start / Mark 调用（连同参数）全部省略。
using System;
using System.Diagnostics;
#if BOSSRUSH_DEV
using System.Globalization;
using System.Text;
using UnityEngine;
#endif

namespace BossRush
{
    internal enum TidegateTickPart { Hud, TideWater, Loot, EnemyOwner, DevicesMapSound, Hazards, GroundExitHud, AiUpdate, BehaviourTrees }

    internal static class TidegateFrameProbe
    {
        [Conditional("BOSSRUSH_DEV")]
        internal static void Start()
        {
#if BOSSRUSH_DEV
            if (active != null) active.Start(Time.frameCount, Stopwatch.GetTimestamp());
#endif
        }

        [Conditional("BOSSRUSH_DEV")]
        internal static void Mark(TidegateTickPart part)
        {
#if BOSSRUSH_DEV
            if (active != null) active.Mark(Time.frameCount, part, Stopwatch.GetTimestamp());
#endif
        }

#if BOSSRUSH_DEV
        private static Window active;
        internal static long EnterExternal() { return active == null ? 0 : Stopwatch.GetTimestamp(); }
        internal static void LeaveExternal(TidegateTickPart part, long start)
        {
            if (start != 0 && active != null) active.AddExternal(Time.frameCount, part, Stopwatch.GetTimestamp() - start);
        }

        internal sealed class Window : IDisposable
        {
            private static readonly string[] Names = { "hud", "tide_water_light", "loot", "enemy_owner", "devices_map_sound", "hazards", "ground_exit_hud", "ai_update", "behaviour_trees" };
            private readonly long[,] recent = new long[2, Names.Length];
            private readonly int[] frame = { -1, -1 };
            private readonly int[] sessionFrame = { -1, -1 };
            private readonly int[,] externalCalls = new int[2, 2];
            private readonly int[] observed = new int[2];
            private int lastSample = -1, count;
            private long previous;
            private readonly double[,] samples;
            private readonly double[] wall, scratch;
            internal int MatchedFrames { get { return count; } }

            internal Window(int limit = 8192)
            {
                samples = new double[limit, Names.Length];
                wall = new double[limit]; scratch = new double[limit];
                active = this;
            }
            private int PrepareFrame(int f)
            {
                int slot = f & 1;
                if (frame[slot] != f)
                {
                    frame[slot] = f;
                    for (int i = 0; i < Names.Length; i++) recent[slot, i] = 0;
                    externalCalls[slot, 0] = externalCalls[slot, 1] = 0;
                }
                return slot;
            }
            internal void Start(int f, long stamp)
            {
                sessionFrame[PrepareFrame(f)] = f;
                previous = stamp;
            }
            internal void AddExternal(int f, TidegateTickPart part, long ticks)
            {
                int slot = PrepareFrame(f);
                externalCalls[slot, (int)part - (int)TidegateTickPart.AiUpdate]++;
                if (ticks > 0) recent[slot, (int)part] += ticks;
            }
            internal void Mark(int f, TidegateTickPart part, long stamp)
            {
                int slot = f & 1;
                if (frame[slot] != f || stamp < previous) return;
                recent[slot, (int)part] += stamp - previous;
                previous = stamp;
            }
            // unscaledDeltaTime 描述上一帧；不能把这一帧 Update 的耗时归到上一帧的慢帧。
            internal void CapturePrevious(int currentFrame, double milliseconds)
            {
                int f = currentFrame - 1, slot = f & 1;
                if (f <= lastSample || frame[slot] != f || sessionFrame[slot] != f || count >= wall.Length || !(milliseconds > 0.0)
                    || double.IsInfinity(milliseconds)) return;
                for (int i = 0; i < Names.Length; i++) samples[count, i] = 1000.0 * recent[slot, i] / Stopwatch.Frequency;
                observed[0] += externalCalls[slot, 0]; observed[1] += externalCalls[slot, 1];
                wall[count++] = milliseconds;
                lastSample = f;
            }
            private double P95()
            {
                Array.Sort(scratch, 0, count);
                return scratch[Math.Max(0, (int)Math.Ceiling(count * .95) - 1)];
            }
            private static string F(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }
            internal string Metrics()
            {
                var s = new StringBuilder(",owner_samples=").Append(count);
                if (count == 0) return s.Append(",owner_wall_p95_ms=unavailable").ToString();
                Array.Copy(wall, scratch, count);
                double p95 = P95();
                int slow = 0;
                for (int r = 0; r < count; r++) if (wall[r] >= p95) slow++;
                s.Append(",owner_wall_p95_ms=").Append(F(p95)).Append(",owner_tail_frames=").Append(slow);
                for (int i = 0; i < Names.Length; i++)
                {
                    int ext = i - (int)TidegateTickPart.AiUpdate;
                    if (ext >= 0)
                    {
                        s.Append(",").Append(Names[i]).Append("_calls=").Append(observed[ext]);
                        if (observed[ext] == 0)
                        {
                            s.Append(",owner_").Append(Names[i]).Append("_p95_ms=unavailable,tail_").Append(Names[i]).Append("_mean_ms=unavailable");
                            continue;
                        }
                    }
                    double slowSum = 0;
                    for (int r = 0; r < count; r++)
                    {
                        scratch[r] = samples[r, i];
                        if (wall[r] >= p95) slowSum += samples[r, i];
                    }
                    s.Append(",owner_").Append(Names[i]).Append("_p95_ms=").Append(F(P95()));
                    s.Append(",tail_").Append(Names[i]).Append("_mean_ms=").Append(F(slowSum / slow));
                }
                return s.ToString();
            }
            public void Dispose() { if (ReferenceEquals(active, this)) active = null; }
        }
#endif
    }
}

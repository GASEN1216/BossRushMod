// ============================================================================
// SkyIslandFrameProfile.cs - 天空岛每帧开销的分项计时（只在 Dev 构建里干活）
// ============================================================================
// 2026-09-14 首轮实机：码头零敌人时帧时间 p95 约 50 ms，日志里只有 renderers / 材质 / 灯三个计数，分不清是岛上哪一段在吃帧。
// 这里在会话 Update、剧情 owner 与局内 owner 的每帧入口打分段标记；F3 岛内验收的 SKY_PERF_BASELINE_5S / SKY_PERF_FINAL_5S
// 在 5 秒采样窗口里录下来，按段给 p95 与最大值（判据在 F3GameplayValidationSkyIslandRuntimeCases 的纯判据区）。
//
// 纪律：
// - **正式构建零开销**：Start / Mark / BeginRecording 带 [Conditional("BOSSRUSH_DEV")]，正式构建里调用点整条不存在（参数也不求值）；
//   实现包在 #if BOSSRUSH_DEV 里，正式构建只剩一个恒返回 false 的 TryTakeRecording。
// - **平铺计时，不配对**：Mark(X) 记「这一帧上一次 Start / Mark 到这里」这一段归 X。嵌套的 owner 各打各的标记，
//   提前 return 剩下的那一截记到下一处 Mark（或丢掉），不会串到别的帧；不需要 Begin / End 成对。
// - **不录时什么都不做**：只有 F3 开窗之后才读时钟；没开窗时每处 Mark 只是一个布尔判断。
// - **只诊断**：不据此改场景、美术或玩法。
// ============================================================================

using System.Collections.Generic;
using System.Diagnostics;

namespace BossRush
{
    /// <summary>分段。顺序就是报告里的列顺序，只追加不重排（<see cref="SkyIslandFrameProfile.SegmentNames"/> 一一对应）。</summary>
    internal enum SkyIslandFrameSegment
    {
        /// <summary>会话 Update：HUD 驱动（卡片、大标题、字幕）。</summary>
        Hud = 0,
        /// <summary>剧情 owner：对话、官方图鉴同步、面板推进。</summary>
        StoryUi = 1,
        /// <summary>剧情 owner：信鸽。</summary>
        Pigeon = 2,
        /// <summary>局内 owner：云蚋逐帧飞行、叮咬、灭蚊灯与嗡声。</summary>
        Gnats = 3,
        /// <summary>局内 owner：采集点建点与光斑（0.5 秒节流那一帧才有）。</summary>
        Gathering = 4,
        /// <summary>局内 owner：灯的昼夜光强与耗材计时。</summary>
        FiresAndBuffs = 5,
        /// <summary>局内 owner：夜风、寒意与云蚋刷新采样。</summary>
        WindAndSwarm = 6,
        /// <summary>剧情 owner 的其余部分（纪念物、按旗标重建反馈）。</summary>
        StoryRest = 7,
        /// <summary>会话 Update：遭遇 owner。</summary>
        Encounters = 8,
        /// <summary>会话 Update：搜刮点建箱与牌子。</summary>
        Scavenging = 9,
        /// <summary>会话 Update：居民显隐。</summary>
        Residents = 10,
        /// <summary>会话 Update：剧情落盘门。</summary>
        StorySave = 11,
        /// <summary>会话 Update：门、撤离环与官方地图标记。</summary>
        GatesAndMarkers = 12,
        /// <summary>会话 Update：光照。</summary>
        Lighting = 13,
        /// <summary>会话 Update：氛围音效。</summary>
        Ambience = 14,
        /// <summary>会话 Update：落水救援与脚下地面射线。</summary>
        GroundProbe = 15,
        /// <summary>会话 Update：撤离圈判定与 0.5 秒一次的 HUD 刷新。</summary>
        HudRefresh = 16
    }

    internal static class SkyIslandFrameProfile
    {
        /// <summary>报告里的段名，与 <see cref="SkyIslandFrameSegment"/> 逐项对应。</summary>
        internal static readonly string[] SegmentNames =
        {
            "hud", "story_ui", "pigeon", "gnats", "gathering", "fires_buffs", "wind_swarm", "story_rest",
            "encounters", "scavenging", "residents", "story_save", "gates_markers", "lighting", "ambience", "ground_probe", "hud_refresh"
        };

#if BOSSRUSH_DEV
        /// <summary>一个 5 秒窗口按 60 fps 约 300 帧；上限只防窗口被拖长时无限增长。</summary>
        private const int MaxFrames = 4096;
        private static bool recording, dirty;
        private static int frame = -1;
        private static long last;
        private static readonly long[] current = new long[SegmentNames.Length];
        private static readonly List<float[]> recorded = new List<float[]>(512);
#endif

        /// <summary>一帧里第一处每帧入口（会话 Update 开头）调用：换帧时先把上一帧收进录制，再把时钟拨到这里。</summary>
        [Conditional("BOSSRUSH_DEV")]
        internal static void Start()
        {
#if BOSSRUSH_DEV
            if (!recording) return;
            int now = UnityEngine.Time.frameCount;
            if (now != frame)
            {
                Flush();
                frame = now;
            }
            last = Stopwatch.GetTimestamp();
#endif
        }

        /// <summary>把「这一帧上一处 Start / Mark 到这里」这一段记到 <paramref name="segment"/> 名下。</summary>
        [Conditional("BOSSRUSH_DEV")]
        internal static void Mark(SkyIslandFrameSegment segment)
        {
#if BOSSRUSH_DEV
            if (!recording || UnityEngine.Time.frameCount != frame) return;
            long stamp = Stopwatch.GetTimestamp();
            current[(int)segment] += stamp - last;
            last = stamp;
            dirty = true;
#endif
        }

        /// <summary>F3 开窗：清掉旧录制，从下一次 Start 起记。</summary>
        [Conditional("BOSSRUSH_DEV")]
        internal static void BeginRecording()
        {
#if BOSSRUSH_DEV
            recorded.Clear();
            System.Array.Clear(current, 0, current.Length);
            dirty = false;
            frame = -1;
            last = 0;
            recording = true;
#endif
        }

        /// <summary>F3 关窗：收下最后一帧并交出整段录制（每帧一行、每段毫秒）。正式构建恒返回 false。</summary>
        internal static bool TryTakeRecording(out List<float[]> frames)
        {
#if BOSSRUSH_DEV
            Flush();
            recording = false;
            frames = new List<float[]>(recorded);
            recorded.Clear();
            return true;
#else
            frames = null;
            return false;
#endif
        }

        /// <summary>模块销毁时丢掉未交出的录制（Dev 构建）；正式构建里什么都不做。</summary>
        internal static void ResetStaticCaches()
        {
#if BOSSRUSH_DEV
            recording = false;
            recorded.Clear();
            System.Array.Clear(current, 0, current.Length);
            dirty = false;
            frame = -1;
            last = 0;
#endif
        }

#if BOSSRUSH_DEV
        private static void Flush()
        {
            if (!dirty) return;
            if (recorded.Count < MaxFrames)
            {
                float[] row = new float[current.Length];
                for (int i = 0; i < current.Length; i++) row[i] = (float)(current[i] * 1000.0 / Stopwatch.Frequency);
                recorded.Add(row);
            }
            System.Array.Clear(current, 0, current.Length);
            dirty = false;
        }
#endif
    }
}

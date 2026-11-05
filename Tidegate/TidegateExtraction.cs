using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的撤离点：判定、贴地环与官方读条显示共用同一份「开没开」（口径同天空岛 CR-2026-09-08-002：
    /// 判定只在这里，官方 `CountDownArea` 只借显示）。四处出口：公路检查站常驻且最远；栈桥渡口只在高潮来船；
    /// 排水暗渠只在出口露出水面时能走；灯塔码头要点亮灯塔后等船（灯塔交互接上前恒关）。
    /// </summary>
    internal sealed class TidegateExtraction : IDisposable
    {
        internal const float RingWidth = 0.24f;
        private static readonly Color AlwaysColor = new Color(0.60f, 0.84f, 0.86f, 0.78f);
        private static readonly Color TideColor = new Color(0.62f, 0.88f, 0.74f, 0.78f);

        private sealed class Exit
        {
            internal string Id;
            internal Transform Anchor;
            internal float Radius, Hold;
            internal Func<bool> Open;
            internal LineRenderer Ring;
            internal SkyIslandExtractionCountdown Countdown;
            internal bool RingVisible;
        }

        private readonly List<Exit> exits = new List<Exit>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private Exit current;
        private float held = -1f;
        private bool disposed, lighthouseBoat;
        private float nextRings;
        private int fallbackSeconds = -1;

        /// <summary>
        /// 撤离读秒的文字兜底：官方读条控件不在场（或反射字段对不上、`Show` 返回 false）时给 HUD 卡片的一行，
        /// 官方读条接管或不在撤离圈里时为 null（口径同天空岛 `SkyIslandSession.ShowExtraction`）。
        /// </summary>
        internal string HudFallback { get; private set; }

        internal TidegateExtraction(TidegateWorld world, TidegateTide tide, int groundMask)
        {
            this.world = world;
            this.tide = tide;
            Add("road", 2.6f, 10f, delegate { return true; }, AlwaysColor, groundMask);
            Add("ferry", 2.4f, 5f, delegate { return tide.Phase == TidegateTidePhase.High; }, TideColor, groundMask);
            Add("drain", 2.2f, 6f, DrainOpen, TideColor, groundMask);
            Add("lh", 2.2f, 5f, delegate { return lighthouseBoat; }, TideColor, groundMask);
            ApplyRings();
        }

        /// <summary>灯塔点亮后灯塔码头来船（由灯塔交互调用）。</summary>
        internal void SetLighthouseBoat(bool arrived) { lighthouseBoat = arrived; }

        private bool DrainOpen()
        {
            Transform anchor;
            if (!world.Exits.TryGetValue("drain", out anchor)) return false;
            float ground;
            if (!world.TryGroundAt(anchor.position, out ground)) ground = anchor.position.y;
            return TidegateTideRules.Depth(tide.WaterLevelAt(anchor.position), ground) < TidegateTideRules.DrainExposedDepth;
        }

        private void Add(string id, float radius, float hold, Func<bool> open, Color color, int groundMask)
        {
            Transform anchor;
            if (!world.Exits.TryGetValue(id, out anchor)) return;
            var exit = new Exit { Id = id, Anchor = anchor, Radius = radius, Hold = hold, Open = open };
            try
            {
                Vector3 position = anchor.position;
                float ground;
                if (world.TryGroundAt(position, out ground)) position.y = ground;
                exit.Ring = SkyIslandGroundRing.Create(world.Root.transform, position + Vector3.up * SkyIslandGroundRing.GroundLift);
                exit.Ring.gameObject.name = "TidegateExtractionRing_" + id;
                SkyIslandGroundRing.SetShape(exit.Ring, radius, RingWidth, color);
                SpriteRenderer fill = SkyIslandGroundRing.AddFill(exit.Ring, radius, new Color(color.r, color.g, color.b, 0f));
                SkyIslandGroundRingPulse.Attach(exit.Ring, RingWidth, color, fill, 0.16f);
                exit.Ring.gameObject.SetActive(false);
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 撤离环创建失败 " + id + "：" + e.Message); }
            try { exit.Countdown = new SkyIslandExtractionCountdown(anchor, hold); }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 撤离读条接入失败 " + id + "：" + e.Message); }
            exits.Add(exit);
        }

        private void ApplyRings()
        {
            foreach (Exit exit in exits)
            {
                bool open = SafeOpen(exit);
                if (exit.Ring != null && exit.RingVisible != open) { exit.RingVisible = open; exit.Ring.gameObject.SetActive(open); }
            }
        }

        private static bool SafeOpen(Exit exit)
        {
            try { return exit.Open(); }
            catch (Exception) { return false; }
        }

        /// <param name="advance">本帧可推进的游戏时间秒数；官方界面打开时由会话传 0，读条冻结不清零。</param>
        /// <returns>读满了：<paramref name="exitId"/> 是撤离点 id。</returns>
        internal bool Tick(Vector3 position, float advance, out string exitId)
        {
            exitId = null;
            if (disposed) return false;
            if (Time.unscaledTime >= nextRings) { nextRings = Time.unscaledTime + 0.5f; ApplyRings(); }
            Exit inside = null;
            foreach (Exit exit in exits)
                if (Vector3.Distance(position, exit.Anchor.position) < exit.Radius && SafeOpen(exit)) { inside = exit; break; }
            if (inside == null)
            {
                if (current != null) HideCountdown();
                current = null;
                held = -1f;
                SetFallback(-1);
                return false;
            }
            if (inside != current) { HideCountdown(); current = inside; held = 0f; }
            held += Math.Max(0f, advance);
            bool shown = current.Countdown != null && current.Countdown.Show(held);
            SetFallback(shown ? -1 : Mathf.CeilToInt(Mathf.Max(0f, current.Hold - held)));
            if (held < current.Hold) return false;
            exitId = current.Id;
            return true;
        }

        private void SetFallback(int seconds)
        {
            if (seconds == fallbackSeconds) return;
            fallbackSeconds = seconds;
            HudFallback = seconds < 0 ? null : L10n.T("返回基地 · ", "Returning to base · ") + seconds + L10n.T(" 秒", "s");
        }

        internal void HideCountdown()
        {
            SetFallback(-1);
            if (current != null && current.Countdown != null)
            {
                try { current.Countdown.Hide(); }
                catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 撤离读条收起失败：" + e.Message); }
            }
        }

        /// <summary>HUD 第三行：现在哪些出口开着。</summary>
        internal string Describe()
        {
            var open = new List<string>();
            foreach (Exit exit in exits) if (SafeOpen(exit)) open.Add(TidegateText.ExitName(exit.Id));
            return L10n.T("可撤离：", "Open exits: ") + (open.Count == 0 ? "--" : string.Join(L10n.T("、", ", "), open.ToArray()));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            HideCountdown();
            foreach (Exit exit in exits)
            {
                if (exit.Countdown != null)
                {
                    try { exit.Countdown.Dispose(); }
                    catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 撤离读条释放失败：" + e.Message); }
                }
                if (exit.Ring != null) UnityEngine.Object.Destroy(exit.Ring.gameObject);
            }
            exits.Clear();
            current = null;
        }
    }
}

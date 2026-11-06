using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal enum TidegateExitState { Unknown, Open, LightLoad, LowWater, Submerged, Unlit, Inbound }

    /// <summary>
    /// 潮闸镇的撤离点：判定、贴地环与官方读条显示共用同一份「开没开」（口径同天空岛 CR-2026-09-08-002：
    /// 判定只在这里，官方 `CountDownArea` 只借显示）。四处出口：公路检查站常驻且最远；高潮渡船靠岸，涨退潮轻装可抓缆；
    /// 排水暗渠只在出口露出水面时能走；灯塔码头要点亮灯塔后等船（灯塔交互接上前恒关）。
    /// </summary>
    internal sealed partial class TidegateExtraction : IDisposable
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
            internal bool RingVisible, Available;
        }

        private readonly List<Exit> exits = new List<Exit>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly TidegateWater water;
        private readonly TidegateFerryLine ferryLine = new TidegateFerryLine();
        private const float FerryRadius = 2.4f;
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

        internal TidegateExtraction(TidegateWorld world, TidegateTide tide, TidegateWater water, int groundMask)
        {
            this.world = world;
            this.tide = tide;
            this.water = water;
            Add("road", 2.6f, 10f, delegate { return true; }, AlwaysColor, groundMask);
            Add("ferry", FerryRadius, 5f, delegate { return ferryLine.CanBoard(tide.Phase); }, TideColor, groundMask);
            Add("drain", 2.2f, 6f, DrainOpen, TideColor, groundMask);
            Add("lh", 2.2f, 5f, delegate { return lighthouseBoat; }, TideColor, groundMask);
            ApplyRings();
        }

        /// <summary>灯塔点亮后灯塔码头来船（由灯塔交互调用）。</summary>
        internal void SetLighthouseBoat(bool arrived) { lighthouseBoat = arrived; }

        internal bool FerryLineAvailable { get { return !disposed && water != null && !ferryLine.Held && TidegateFerryLine.InReach(tide.Phase); } }

        // M 图取同一份出口判定；不另算潮高、也不为读地图而反复探地。
        internal TidegateExitState StateForMap(string id, bool beaconLit)
        {
            foreach (Exit exit in exits)
            {
                if (exit.Id != id) continue;
                if (exit.Available) return TidegateExitState.Open;
                if (id == "ferry") return FerryLineAvailable ? TidegateExitState.LightLoad : TidegateExitState.LowWater;
                if (id == "drain") return TidegateExitState.Submerged;
                if (id == "lh") return beaconLit ? TidegateExitState.Inbound : TidegateExitState.Unlit;
                break;
            }
            return TidegateExitState.Unknown;
        }

        private bool AtFerry(Vector3 position)
        {
            Transform anchor;
            return world.Exits.TryGetValue("ferry", out anchor) && anchor != null
                && (position - anchor.position).sqrMagnitude < FerryRadius * FerryRadius;
        }

        internal bool TryCatchFerryLine(Vector3 position, out string message)
        {
            bool atPier = AtFerry(position);
            if (!FerryLineAvailable)
                message = L10n.T("缆绳收起来了。高潮时船会直接靠岸。", "The line is in. At high tide the ferry docks alongside the pier.");
            else if (ferryLine.TryCatch(tide.Phase, atPier && water.CheckBoardingLoad(), atPier))
            {
                ApplyRings();
                message = L10n.T("抓住了！留在圈里登船，离开就会松手。", "You've caught the line! Stay in the ring to board. Step away to let go.");
                return true;
            }
            else if (!atPier)
                message = L10n.T("再往栈桥尽头挪两步，才够得着缆绳。", "Move closer to the end of the pier to reach the line.");
            else
                message = L10n.T("你背得太重，游不到船边。放轻到 ", "Too heavy to reach the ferry. Lighten your load to ")
                    + water.WeightLimit.ToString("0") + L10n.T(" 公斤以内，或等高潮连货一起上船。", " kg or wait for high tide to take your cargo aboard.");
            return false;
        }

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
                exit.Available = open;
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
            // 一直抓牢的人可以跨过换潮边界；离圈或重新背重马上松手。
            if (ferryLine.Held && ferryLine.DropIfNeeded(AtFerry(position), water.CanSwimNow)) ApplyRings();
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
            if (ferryLine.Held && tide.Phase != TidegateTidePhase.High && ferryLine.DropIfNeeded(true, water.CheckBoardingLoad()))
            {
                // 背包刚刚变化、水体的半秒检查尚未运行：最后一帧也不能带超重货物走。
                ApplyRings();
                HideCountdown();
                current = null;
                held = -1f;
                return false;
            }
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

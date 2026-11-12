using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的撤离点：判定、贴地环与官方读条显示共用同一份「开没开」（口径同潮闸镇 `TidegateExtraction`：判定只在这里，
    /// 官方 `CountDownArea` 只借显示）。北公路常开且最远；西线检修便门、东台押运门、轨道车接客亭跟列车与拉铃走，
    /// 由第二期的列车 owner 通过 <see cref="SetOpen"/> 打开——在那之前它们关着，M 图与 HUD 写明开启条件。
    /// </summary>
    internal sealed partial class LastShiftExtraction : IDisposable
    {
        internal const float RingWidth = 0.24f, Hold = 5f;
        private static readonly Color AlwaysColor = new Color(0.60f, 0.84f, 0.86f, 0.78f);
        private static readonly Color ConditionalColor = new Color(0.93f, 0.80f, 0.48f, 0.78f);

        private sealed class Exit
        {
            internal string Id;
            internal Transform Anchor;
            internal float Radius;
            internal bool Open;
            internal LineRenderer Ring;
            internal SkyIslandExtractionCountdown Countdown;
        }

        private readonly List<Exit> exits = new List<Exit>();
        private Exit current;
        private float held = -1f;
        private int fallbackSeconds = -1;
        private bool disposed;

        internal string HudFallback { get; private set; }

        internal LastShiftExtraction(LastShiftWorld world)
        {
            // 半径与读条和原型布局一致（`layout.mjs` 的 exits：北 3 m，其余 2.5 m，都读 5 秒）。
            Add(world, "north", 3f, true, AlwaysColor);
            Add(world, "west", 2.5f, false, ConditionalColor);
            Add(world, "east", 2.5f, false, ConditionalColor);
            Add(world, "bell", 2.5f, false, ConditionalColor);
        }

        private void Add(LastShiftWorld world, string id, float radius, bool open, Color color)
        {
            Transform anchor;
            if (!world.Exits.TryGetValue(id, out anchor)) return;
            var exit = new Exit { Id = id, Anchor = anchor, Radius = radius, Open = open };
            try
            {
                Vector3 position = anchor.position;
                float ground;
                if (world.TryGroundAt(position, out ground)) position.y = ground;
                exit.Ring = SkyIslandGroundRing.Create(world.Root.transform, position + Vector3.up * SkyIslandGroundRing.GroundLift);
                exit.Ring.gameObject.name = "LastShiftExtractionRing_" + id;
                SkyIslandGroundRing.SetShape(exit.Ring, radius, RingWidth, color);
                SpriteRenderer fill = SkyIslandGroundRing.AddFill(exit.Ring, radius, new Color(color.r, color.g, color.b, 0f));
                SkyIslandGroundRingPulse.Attach(exit.Ring, RingWidth, color, fill, 0.16f);
                exit.Ring.gameObject.SetActive(open);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离环创建失败 " + id + "：" + e.Message); }
            try { exit.Countdown = new SkyIslandExtractionCountdown(anchor, Hold); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离读条接入失败 " + id + "：" + e.Message); }
            exits.Add(exit);
        }

        /// <summary>列车 / 拉铃 owner 改变条件出口的开关（北公路恒开，不受影响）。</summary>
        internal void SetOpen(string id, bool open)
        {
            foreach (Exit exit in exits)
            {
                if (exit.Id != id || exit.Id == "north" || exit.Open == open) continue;
                exit.Open = open;
                if (exit.Ring != null) exit.Ring.gameObject.SetActive(open);
            }
        }

        /// <summary>站在一个关着的出口圈里时返回它的 id（会话据此说明怎么才开，只说一次），否则 null。</summary>
        internal string LockedNear(Vector3 position)
        {
            foreach (Exit exit in exits)
                if (!exit.Open && Vector3.Distance(position, exit.Anchor.position) < exit.Radius) return exit.Id;
            return null;
        }

        internal bool IsOpen(string id)
        {
            foreach (Exit exit in exits) if (exit.Id == id) return exit.Open;
            return false;
        }

        /// <param name="advance">本帧可推进的游戏时间秒数；官方界面打开时由会话传 0，读条冻结不清零。</param>
        internal bool Tick(Vector3 position, float advance, out string exitId)
        {
            exitId = null;
            if (disposed) return false;
            Exit inside = null;
            foreach (Exit exit in exits)
                if (exit.Open && Vector3.Distance(position, exit.Anchor.position) < exit.Radius) { inside = exit; break; }
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
            SetFallback(shown ? -1 : Mathf.CeilToInt(Mathf.Max(0f, Hold - held)));
            if (held < Hold) return false;
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
                catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离读条收起失败：" + e.Message); }
            }
        }

        /// <summary>HUD 小标行：开着的出口。</summary>
        internal string Describe()
        {
            var open = new List<string>();
            foreach (Exit exit in exits) if (exit.Open) open.Add(LastShiftText.ExitName(exit.Id));
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
                    catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离读条释放失败：" + e.Message); }
                }
                if (exit.Ring != null) UnityEngine.Object.Destroy(exit.Ring.gameObject);
            }
            exits.Clear();
            current = null;
        }
    }
}

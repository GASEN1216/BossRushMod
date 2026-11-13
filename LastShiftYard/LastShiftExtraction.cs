using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using SodaCraft.Localizations;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 撤离判定、贴地环、官方读条和地图标记共用出口状态。北公路常开，条件出口由列车通过 <see cref="SetOpen"/> 更新。
    /// 官方 CountDownArea 用 Time.time 计算已用时，打开界面期间仍会计入时间；会话用游戏推进量计时，并借官方界面显示读条。
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
            /// <summary>锚点会动（末班车押运车厢的尾梯）：环与地图标记挂在锚点下跟着走，判定每帧读锚点。</summary>
            internal bool Moving;
            internal LineRenderer Ring;
            internal SkyIslandExtractionCountdown Countdown;
            internal SimplePointOfInterest MapPin;
        }

        private readonly List<Exit> exits = new List<Exit>();
        private Exit current;
        private float held = -1f;
        private int fallbackSeconds = -1;
        private SystemLanguage mapLanguage = LocalizationManager.CurrentLanguage;
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
            Vector3 position = anchor.position;
            float ground;
            if (world.TryGroundAt(position, out ground)) position.y = ground;
            Build(new Exit { Id = id, Anchor = anchor, Radius = radius, Open = open }, world.Root.transform, position + Vector3.up * SkyIslandGroundRing.GroundLift, color);
        }

        /// <summary>
        /// 会动的出口（2026-11-13 末班车押运车厢的尾梯）：锚点由列车 owner 每帧贴地摆好；环挂在锚点下跟着走，开始时关着。
        /// 读条与普通出口同一套（离开圈子清零），所以车停着时站上去最稳，车开动后要跟着车跑。
        /// </summary>
        internal void AddMoving(string id, Transform anchor, float radius)
        {
            if (disposed || anchor == null) return;
            foreach (Exit existing in exits) if (existing.Id == id) return;
            Build(new Exit { Id = id, Anchor = anchor, Radius = radius, Open = false, Moving = true }, anchor, Vector3.up * SkyIslandGroundRing.GroundLift, ConditionalColor);
        }

        private void Build(Exit exit, Transform ringParent, Vector3 ringLocal, Color color)
        {
            string id = exit.Id;
            Transform anchor = exit.Anchor;
            float radius = exit.Radius;
            bool open = exit.Open;
            try
            {
                exit.Ring = SkyIslandGroundRing.Create(ringParent, ringLocal);
                exit.Ring.gameObject.name = "LastShiftExtractionRing_" + id;
                SkyIslandGroundRing.SetShape(exit.Ring, radius, RingWidth, color);
                SpriteRenderer fill = SkyIslandGroundRing.AddFill(exit.Ring, radius, new Color(color.r, color.g, color.b, 0f));
                SkyIslandGroundRingPulse.Attach(exit.Ring, RingWidth, color, fill, 0.16f);
                exit.Ring.gameObject.SetActive(open);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离环创建失败 " + id + "：" + e.Message); }
            try { exit.Countdown = new SkyIslandExtractionCountdown(anchor, Hold); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离读条接入失败 " + id + "：" + e.Message); }
            try
            {
                var pin = new GameObject("LastShiftExitMap_" + id);
                pin.transform.SetParent(anchor, false);
                exit.MapPin = pin.AddComponent<SimplePointOfInterest>();
                RefreshMapPin(exit, SkyIslandMapMarkers.ExitIcon());
                // 会动的出口关着时锚点停在站界外：地图上不留一个指向站外的标记。
                if (exit.Moving) pin.SetActive(open);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离地图标记创建失败 " + id + "：" + e.Message); }
            exits.Add(exit);
        }

        private static void RefreshMapPin(Exit exit, Sprite icon = null)
        {
            if (exit.MapPin == null) return;
            try
            {
                string key = "BossRush_LastShift_ExitMap_" + exit.Id;
                string status = exit.Open ? L10n.T("可撤离", "Open for extraction") : LastShiftText.ExitRule(exit.Id);
                LocalizationHelper.InjectLocalization(key, LastShiftText.ExitName(exit.Id) + " · " + status);
                exit.MapPin.Color = exit.Open ? AlwaysColor : ConditionalColor;
                // 官方地图条目在绑定时读取文案和颜色，Setup 会刷新该条目。
                exit.MapPin.Setup(icon, key);
                // 官方 Setup 先注销再登记、不看自己启没启用：停用着的标记（关着的末班车尾梯）登记进去会照样画在地图上，
                // 停用的对象销毁时也不走 OnDisable，残留在静态表里。这里补一次注销。
                if (!exit.MapPin.isActiveAndEnabled) PointsOfInterests.Unregister(exit.MapPin);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 撤离地图标记刷新失败 " + exit.Id + "：" + e.Message); }
        }

        /// <summary>列车 / 拉铃 owner 改变条件出口的开关（北公路恒开，不受影响）。</summary>
        internal void SetOpen(string id, bool open)
        {
            foreach (Exit exit in exits)
            {
                if (exit.Id != id || exit.Id == "north" || exit.Open == open) continue;
                exit.Open = open;
                if (exit.Ring != null) exit.Ring.gameObject.SetActive(open);
                if (exit.Moving && exit.MapPin != null) exit.MapPin.gameObject.SetActive(open);
                RefreshMapPin(exit);
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
            SystemLanguage language = LocalizationManager.CurrentLanguage;
            if (language != mapLanguage)
            {
                mapLanguage = language;
                foreach (Exit exit in exits) RefreshMapPin(exit);
            }
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
                if (exit.MapPin != null)
                {
                    exit.MapPin.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(exit.MapPin.gameObject);
                }
            }
            exits.Clear();
            current = null;
        }
    }
}

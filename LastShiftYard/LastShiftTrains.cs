using System;
using System.Collections.Generic;
using BossRush.Utils;
using Duckov.UI;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的列车与调度运行时（第二期）。规则全在 <see cref="LastShiftTimetable"/>，这里只把它落到场景里：
    /// 车厢从场景包 `Templates/VIS_vehicles` 克隆、逐帧摆位；车体是会动的墙层碰撞（挡人挡枪），擦到人只往外推；
    /// 车头正面撞人走 <see cref="TidegateHazards.ResolveLethalImpact"/>（一次官方 Hurt，保留墓碑与结算）；首班撞飞示范推车；
    /// 线上有车时按车体包络封导航三角形；条件出口按班次开关；司机室道岔、轨道车铃、班次运单牌三个交互。
    /// 第七轮 owner 采纳的反馈都在这里：首班等玩家站到南护栏、看完演示才给道岔、扳岔站场广播 + 8 秒地面箭头、锁着的出口说明条件。
    /// </summary>
    internal sealed class LastShiftTrains : IDisposable
    {
        private static readonly string[] Bodies = { "31 hood shunter", "Sealed sundries", "Twin coil bolster", "Green brake van", "Twin coil bolster.001" };
        private const string CartBody = "Teaching cart";
        private const float BodyHeight = 3.6f, NoticeSeconds = 8f;
        private static readonly Color ArrowColor = new Color(1f, 0.79f, 0.36f, 0.9f);

        private readonly LastShiftWorld world;
        private readonly LastShiftTimetable timetable;
        private readonly ArenaPrototypeNavigation navigation;
        private readonly LastShiftExtraction extraction;
        private readonly Action<string, bool> status;
        private readonly Func<bool> valid;
        private readonly Transform holder;
        private readonly GameObject[] cars = new GameObject[5];
        private readonly Quaternion[] baseRotation = new Quaternion[5];
        private readonly BoxCollider[] bodies = new BoxCollider[5];
        private GameObject cart;
        private Quaternion cartRotation;
        private readonly List<GameObject> arrows = new List<GameObject>();
        private readonly List<GameObject> interactables = new List<GameObject>();
        private float noticeUntil = -1f, nextExits;
        private char noticeLine;
        private string navState = "";
        private bool railReminded, demoForcedAnnounced, dispatchWaitAnnounced, disposed;
        private readonly int wallLayer;

        internal LastShiftTimetable Timetable { get { return timetable; } }
        internal string Manifest { get; private set; }

        internal LastShiftTrains(LastShiftWorld world, LastShiftTimetable timetable, ArenaPrototypeNavigation navigation, LastShiftExtraction extraction,
            string manifest, Action<string, bool> status, Func<bool> valid)
        {
            this.world = world; this.timetable = timetable; this.navigation = navigation; this.extraction = extraction;
            this.status = status; this.valid = valid; Manifest = manifest;
            wallLayer = FirstLayer(GameplayDataSettings.Layers.wallLayerMask.value);
            holder = new GameObject("LastShiftTrains").transform;
            holder.SetParent(world.Root.transform, false);
            Transform templates = world.Root.transform.Find("Templates/VIS_vehicles");
            if (templates == null) throw new InvalidOperationException("场景包缺少车辆模板 Templates/VIS_vehicles");
            for (int i = 0; i < Bodies.Length; i++)
            {
                Transform body = templates.Find(Bodies[i]);
                if (body == null) throw new InvalidOperationException("车辆模板缺少 " + Bodies[i]);
                cars[i] = Clone(body, "Car_" + i, out baseRotation[i]);
                bodies[i] = AddBody(cars[i].name);
            }
            Transform cartTemplate = templates.Find(CartBody);
            if (cartTemplate != null) { cart = Clone(cartTemplate, "TeachingCart", out cartRotation); cart.SetActive(true); }
            timetable.PhaseChanged += OnPhase;
            AddInteract("switch", 0.8f, SwitchLabel, delegate { return valid(); }, ThrowSwitch);
            AddInteract("bell", 0.6f, delegate { return L10n.T("拉轨道车铃", "Ring the railcar bell"); }, delegate { return valid() && timetable.BellAt < 0; }, RingBell);
            AddInteract("manifest", 0.4f, delegate { return L10n.T("看班次运单牌", "Read the shift manifest"); }, delegate { return valid(); }, ReadManifest);
            Place(0f);
            RefreshNavigation();
        }

        private GameObject Clone(Transform template, string name, out Quaternion rotation)
        {
            rotation = template.rotation;
            GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, holder, true);
            copy.name = name;
            copy.SetActive(false);
            return copy;
        }

        private BoxCollider AddBody(string name)
        {
            var go = new GameObject("COL_" + name);
            go.transform.SetParent(holder, false);
            go.layer = wallLayer;
            BoxCollider box = go.AddComponent<BoxCollider>();
            go.SetActive(false);
            return box;
        }

        private void AddInteract(string id, float seconds, Func<string> label, Func<bool> available, Action action)
        {
            Transform anchor;
            if (!world.Interacts.TryGetValue(id, out anchor)) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 缺少交互标记 " + id); return; }
            var host = new GameObject("LastShiftInteract_" + id);
            host.transform.SetParent(world.Root.transform, false);
            host.transform.position = anchor.position;
            interactables.Add(host);
            var device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(host.transform, "Interact", new Vector3(0f, 1f, 0f), x =>
            {
                x.Bind(id, seconds, label, available, action);
                SphereCollider sensor = x.GetComponent<SphereCollider>();
                if (sensor != null) { sensor.radius = 1.2f; sensor.isTrigger = true; }
            });
            if (device == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 交互点创建失败：" + id);
        }

        // ---------------- 交互 ----------------
        private string SwitchLabel()
        {
            char other = timetable.Line == 'W' ? 'E' : 'W';
            return other == 'W' ? L10n.T("扳道岔：改走西台", "Throw the switch: west platform") : L10n.T("扳道岔：改走东台", "Throw the switch: east platform");
        }

        private void ThrowSwitch()
        {
            char target = timetable.Line == 'W' ? 'E' : 'W';
            string reason;
            if (!timetable.TrySwitch(target, out reason))
            {
                status(reason == "demo" ? L10n.T("先在南护栏后看完首班示范，道岔才交给你。", "Watch the first-train demonstration from behind the south rail first; then the switch is yours.")
                    : L10n.T("本班已锁线，等列车出清后再改道。", "This train has locked the route. Wait until the line clears."), true);
                return;
            }
            noticeLine = target; noticeUntil = Time.time + NoticeSeconds;
            ShowArrows(target);
            RefreshNavigation();
            status(target == 'W'
                ? L10n.T("站场广播：货车改走西线，押运跟车去西货台。西台在棚内，守卫离得近。", "Yard tannoy: freight routed to the west line, escorts heading for the west platform. The west shed is close quarters.")
                : L10n.T("站场广播：货车改走东线，押运跟车去东货台。东台要穿过敞坪。", "Yard tannoy: freight routed to the east line, escorts heading for the east platform. The east deck is open ground."), false);
        }

        private void RingBell()
        {
            timetable.RingBell();
            status(L10n.T("已拉铃：轨道车约 14 秒后到接客亭，附近巡逻也听到了。", "Bell rung: the railcar reaches the shelter in about 14 s. Nearby patrols heard it too."), false);
            Ringing = true;
        }

        /// <summary>拉过铃（敌人 owner 读它把附近巡逻引过去，读完清零）。</summary>
        internal bool Ringing { get; set; }

        private void ReadManifest()
        {
            string premium = timetable.Premium == 'W' ? L10n.T("西台", "the west platform") : L10n.T("东台", "the east platform");
            status(L10n.T("本趟班单：", "This run's manifest: ") + LastShiftText.ManifestName(Manifest)
                + L10n.T("。两台是一批货，全趟限签一次；封签货保底 Q4。加价台：", ". One consignment for both platforms, one signature per run; sealed cargo is Q4 or better. Premium: ")
                + premium + L10n.T("，在那边签收更可能是 Q5。", " (more likely Q5 there).")
                + (ManifestExtra == null ? "" : " " + ManifestExtra()), false);
        }

        /// <summary>运单牌末尾追加的一行（会话接站台记录）。</summary>
        internal Func<string> ManifestExtra { get; set; }

        // ---------------- 每帧 ----------------
        /// <summary>推进班次并落到场景；返回 true 表示玩家被车撞死（会话停止本帧其余推进）。</summary>
        internal bool Tick(CharacterMainControl player, float advance)
        {
            if (disposed || player == null) return false;
            Vector3 p = player.transform.position;
            double px = p.x, pz = -p.z;
            float ground = p.y;
            double oldLead = timetable.Tick(advance, px, pz);
            TickTutorialNotes(px, pz);
            if (timetable.Training && !timetable.CartHit && timetable.FrontHit(oldLead, LastShiftTimetable.CartX, LastShiftTimetable.CartZ, 0))
            {
                timetable.MarkCartHit();
                status(L10n.T("首班演示：手推车被撞飞。下一班起，正面撞击致命。", "First-train demonstration: the cart is smashed aside. From the next train on, a head-on hit is fatal."), true);
            }
            Place(advance);
            if (Time.unscaledTime >= nextExits)
            {
                nextExits = Time.unscaledTime + 0.5f;
                foreach (string id in new[] { "west", "east", "bell" }) extraction.SetOpen(id, timetable.ExitOpen(id));
            }
            if (noticeUntil >= 0f && Time.time > noticeUntil) { noticeUntil = -1f; HideArrows(); }
            if (advance <= 0f || !valid()) return false;
            PushOut(player, px, pz, ground);
            if (!timetable.Training && timetable.FrontHit(oldLead, px, pz, ground) && !player.Health.IsDead)
            {
                bool dead = TidegateHazards.ResolveLethalImpact(player.Health, p,
                    delegate { return L10n.T("被进站列车正面撞中", "Hit head-on by an incoming train"); });
                Debug.Log(LastShiftRaidScene.LogTag + " TRAIN_IMPACT dead=" + dead + " phase=" + timetable.Phase);
                if (dead) { status(L10n.T("你被进站列车正面撞中。", "You were hit head-on by an incoming train."), true); return true; }
            }
            return false;
        }

        private void TickTutorialNotes(double px, double pz)
        {
            if (!timetable.Training) return;
            if (timetable.Phase == LastShiftPhase.Approach && !timetable.CartHit && !LastShiftTimetable.AtSouthRail(px, pz) && !railReminded && timetable.AwayFromRail > 0)
            {
                railReminded = true;
                status(L10n.T("首班还没发车：回到南护栏后看演示，列车会等你。", "The first train hasn't left yet: go back behind the south rail to watch; it waits for you."), false);
            }
            if (timetable.DemoForced && !demoForcedAnnounced)
            {
                demoForcedAnnounced = true;
                status(L10n.T("首班不再等了：列车已经动身，演示照常进行。", "The first train won't wait any longer: it's on its way."), false);
            }
            if (timetable.DispatchWait && !dispatchWaitAnnounced)
            {
                dispatchWaitAnnounced = true;
                status(L10n.T("首班示范已完成：列车在岔心前等你。上悬车楼司机室扳道岔，货列才会继续进站。",
                    "Demonstration done: the train is holding before the turnout. Climb to the suspended cab and throw the switch."), false);
            }
        }

        private void OnPhase(LastShiftPhase previous, LastShiftPhase next)
        {
            RefreshNavigation();
            if (!valid()) return;
            string label = LastShiftText.EventLabel(timetable.Event);
            switch (next)
            {
                case LastShiftPhase.Approach:
                    status(L10n.T("第 ", "Train ") + timetable.Trip + L10n.T(" 班：20 秒后进站，", " arrives in 20 s: ") + label + L10n.T("。", ". ")
                        + LastShiftText.EventNote(timetable.Event), timetable.IsExpress);
                    break;
                case LastShiftPhase.Arrival:
                    if (timetable.IsExpress)
                        status(L10n.T("直通快车压过", "The express is coming through on the ") + LastShiftText.LineName(timetable.LineUsed)
                            + L10n.T("，不停靠！离开轨道。", " line without stopping! Get off the track."), true);
                    else if (timetable.Training && !timetable.DispatchChosen)
                        status(L10n.T("首班进站，在岔心前减速：到司机室扳道岔选西台或东台，未选前列车会等你。",
                            "The first train slows before the turnout: throw the switch in the cab to pick a platform. It waits until you do."), false);
                    else
                        status(L10n.T("道岔已锁：", "Route locked: ") + LastShiftText.PlatformName(timetable.LineUsed)
                            + L10n.T("进车。走轨外步道，车身会挡住枪线。", " platform. Use the walkways beside the track; the train blocks lines of fire."), false);
                    break;
                case LastShiftPhase.Dwell:
                    status(L10n.T("列车停妥：", "The train has stopped: ") + LastShiftText.PlatformName(timetable.LineUsed)
                        + L10n.T("可签收", " platform, ") + LastShiftText.ManifestName(Manifest) + L10n.T("封签货。", " sealed cargo can be signed for."), false);
                    break;
                case LastShiftPhase.Clear:
                    status(L10n.T("列车出清，西检修便门开放。北公路始终可走。", "The line is clear and the west service gate is open. The north road is always open."), false);
                    break;
            }
        }

        // ---------------- 场景落位 ----------------
        private void Place(float advance)
        {
            List<LastShiftCar> list = timetable.Cars();
            var used = new bool[cars.Length];
            foreach (LastShiftCar c in list)
            {
                int m = c.Model;
                bool show = c.Z >= -81 && c.Z <= 84;
                used[m] = show;
                if (!show) continue;
                Quaternion yaw = Quaternion.Euler(0f, -(float)(c.Yaw * Mathf.Rad2Deg), 0f);
                Vector3 at = new Vector3((float)c.X, 0.06f, -(float)c.Z);
                cars[m].transform.SetPositionAndRotation(at, yaw * baseRotation[m]);
                if (!cars[m].activeSelf) cars[m].SetActive(true);
                BoxCollider body = bodies[m];
                body.transform.SetPositionAndRotation(at + Vector3.up * (BodyHeight * 0.5f), yaw);
                body.size = new Vector3((float)c.Width, BodyHeight, (float)c.Depth);
                if (!body.gameObject.activeSelf) body.gameObject.SetActive(true);
            }
            for (int i = 0; i < cars.Length; i++)
                if (!used[i] && cars[i].activeSelf) { cars[i].SetActive(false); bodies[i].gameObject.SetActive(false); }
            if (cart != null)
            {
                double x, y, z;
                timetable.CartPose(out x, out y, out z);
                float spin = timetable.CartHit ? (float)Math.Min(1.2, timetable.Clock - timetable.CartHitClock) * 300f : 0f;
                cart.transform.SetPositionAndRotation(new Vector3((float)x, (float)y + 0.06f, -(float)z), Quaternion.Euler(spin, spin * 0.4f, 0f) * cartRotation);
            }
        }

        /// <summary>车体擦到人：只往同一侧推出去，不带着人走、也不让人从车身里穿过去（原型 resolveTrainSides）。司机室楼面与跨线桥上不推。</summary>
        private void PushOut(CharacterMainControl player, double px, double pz, float y)
        {
            if (y > 3.6f) return;
            foreach (LastShiftCar c in timetable.Cars())
            {
                double dx = px - c.X, dz = pz - c.Z, co = Math.Cos(c.Yaw), sn = Math.Sin(c.Yaw);
                double across = co * dx - sn * dz, along = sn * dx + co * dz;
                if (Math.Abs(across) > c.Width / 2 + 0.24 || Math.Abs(along) > c.Depth / 2 + 0.24) continue;
                double side = across < 0 ? -1 : 1, r = c.Width / 2 + 0.25;
                double xx = c.X + co * side * r + sn * along, zz = c.Z - sn * side * r + co * along;
                player.SetPosition(new Vector3((float)xx, y + 0.05f, -(float)zz));
                return;
            }
        }

        /// <summary>线上有车（进站、停靠、出站）时，把车体包络里的导航三角形设成不可走；楼面与跨线桥（y ≥ 4.75）不受影响。状态变了才重刷。</summary>
        private void RefreshNavigation()
        {
            if (navigation == null) return;
            char line = timetable.LineUsed;
            string state = timetable.TrainOnLine ? "on" + line : "clear";
            if (state == navState) return;
            navState = state;
            if (!timetable.TrainOnLine) { navigation.SetWalkable(delegate { return true; }); return; }
            navigation.SetWalkable(delegate (Vector3 a, Vector3 b, Vector3 c)
            {
                if (Math.Min(a.y, Math.Min(b.y, c.y)) >= 4.75f) return true;
                Vector3 m = (a + b + c) / 3f;
                return !LastShiftTimetable.InEnvelope(m.x, -m.z, line);
            });
        }

        // ---------------- 扳岔后的地面箭头（8 秒） ----------------
        private void ShowArrows(char line)
        {
            HideArrows();
            Vector3 from = new Vector3(line == 'W' ? -3f : 3f, 0f, 1f), to = new Vector3(line == 'W' ? -28f : 28f, 0f, 27f);
            Vector3 dir = (to - from).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            for (int i = 0; i < 9; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, (i + 0.5f) / 9f);
                float ground;
                if (world.TryGroundAt(at, out ground)) at.y = ground;
                var pivot = new GameObject("LastShiftDispatchArrow_" + i);
                pivot.transform.SetParent(holder, false);
                pivot.transform.SetPositionAndRotation(at + Vector3.up * SkyIslandGroundRing.GroundLift, Quaternion.Euler(0f, yaw, 0f));
                LineRenderer chevron = SkyIslandGroundRing.Create(pivot.transform, Vector3.zero);
                chevron.loop = false;
                chevron.positionCount = 3;
                chevron.SetPositions(new[] { new Vector3(-0.9f, -0.55f, 0f), new Vector3(0f, 0.55f, 0f), new Vector3(0.9f, -0.55f, 0f) });
                chevron.widthMultiplier = 0.32f;
                chevron.startColor = chevron.endColor = ArrowColor;
                arrows.Add(pivot);
            }
        }

        private void HideArrows()
        {
            foreach (GameObject a in arrows) if (a != null) UnityEngine.Object.Destroy(a);
            arrows.Clear();
        }

        /// <summary>HUD 小标行：哪台进车、到哪一步、还剩几秒、班次事件。</summary>
        internal string Chip()
        {
            string phase;
            switch (timetable.Phase)
            {
                case LastShiftPhase.Approach: phase = L10n.T("鸣笛预告", "Whistle"); break;
                case LastShiftPhase.Arrival: phase = L10n.T("进站", "Arriving"); break;
                case LastShiftPhase.Dwell: phase = L10n.T("停靠卸货", "Unloading"); break;
                case LastShiftPhase.Depart: phase = L10n.T("出站", "Departing"); break;
                default: phase = L10n.T("线路净空", "Line clear"); break;
            }
            string seconds = timetable.DispatchWait ? L10n.T(" · 等你扳岔", " · waiting for the switch") : " " + Mathf.CeilToInt((float)timetable.SecondsLeft) + L10n.T(" 秒", " s");
            string evt = timetable.Event == LastShiftEvent.Normal ? "" : " · " + LastShiftText.EventLabel(timetable.Event);
            return LastShiftText.PlatformName(timetable.Line) + L10n.T("台", "") + " · " + phase + seconds + evt;
        }

        /// <summary>HUD 目标行：首班教学按步骤给；之后给常态目标。只在字变化时才算「目标更新」（SkyIslandHud 自己去重）。</summary>
        internal string Objective(bool cargoClaimed)
        {
            if (timetable.Training && !timetable.CartHit)
                return L10n.T("站到南护栏后看首班示范", "Stand behind the south rail and watch the first train");
            if (timetable.Training && !timetable.DispatchChosen)
                return L10n.T("沿坡道上悬车楼司机室，扳道岔选西台或东台", "Climb the ramp to the suspended cab and throw the switch: west or east platform");
            if (!cargoClaimed)
                return L10n.T("在停靠一侧的铅封柜签收", "Sign for the sealed cargo at the platform where the train stops") + "（" + LastShiftText.ManifestName(Manifest) + "）";
            return L10n.T("带货撤离：北公路常开", "Extract with the cargo: the north road is always open");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timetable.PhaseChanged -= OnPhase;
            HideArrows();
            if (navigation != null) { try { navigation.SetWalkable(delegate { return true; }); } catch (Exception) { } }
            foreach (GameObject g in interactables) if (g != null) UnityEngine.Object.Destroy(g);
            interactables.Clear();
            if (holder != null) UnityEngine.Object.Destroy(holder.gameObject);
        }

        private static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return 0;
        }
    }

    /// <summary>编组站的机关交互（道岔、轨道车铃、运单牌、押运钢印侧柜……）：名字在靠近时按当前语言取，读条时长写进官方私有字段。</summary>
    public sealed class LastShiftDeviceInteractable : BossRushBuildingInteractableBase
    {
        private string key = "BossRush_LastShift_Device";
        private Func<string> label;
        private string labelText;
        private Func<bool> available;
        private Action action;

        protected override string InteractNameKey { get { return key; } }
        protected override string LogPrefix { get { return LastShiftRaidScene.LogTag + " "; } }

        internal void Bind(string id, float seconds, Func<string> title, Func<bool> canUse, Action callback)
        {
            key = "BossRush_LastShift_Device_" + id;
            label = title; available = canUse; action = callback;
            Relabel();
            ModeFItemConfigHelper.SetHiddenMember(this, "interactTime", seconds);
            if (Mathf.Abs(InteractTime - seconds) > 0.01f)
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 机关读条时长没有写进官方字段（可能已改名），将变成立即完成：" + id);
        }

        private void Relabel()
        {
            if (label == null) return;
            string text = label();
            if (string.Equals(text, labelText, StringComparison.Ordinal)) return;
            labelText = text;
            LocalizationHelper.InjectLocalization(key, text);
            ApplyInteractName("bind");
        }

        protected override bool IsBuildingInteractable()
        {
            if (available == null || !available()) return false;
            Relabel();
            return true;
        }

        protected override void OnInteractCompleted() { if (action != null) action(); }
    }
}

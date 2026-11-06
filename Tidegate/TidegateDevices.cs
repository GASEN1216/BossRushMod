using System;
using System.Collections.Generic;
using BossRush.Utils;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的机关：三处阀门（水厂泵房、镇区泵站、潮位站——潮位站的手轮被拆走了，在挖沙船船舱里）全开后大闸提起，
    /// 泻湖开始放水、低潮时沉没的储蓄所金库露出来，同时海堤那边来一队淘沙帮；灯塔点亮后 45 秒灯塔码头来船，附近的敌人被引过来。
    /// 状态只属于这一趟出击，不进存档。
    /// </summary>
    internal sealed partial class TidegateDevices : IDisposable
    {
        private const float ValveSeconds = 3f, LighthouseSeconds = 2f;
        private const float BoatDelay = 45f, LureRadius = 60f, RevealSeconds = 40f;
        /// <summary>光束转速（度 / 秒）：一圈约 12 秒，夜里远处也看得见在转。</summary>
        private const float BeamSpeed = 30f;
        /// <summary>灯头那盏灯进灯数组的半径（米）：灯头离地约 6.65 m，地上照亮约 13 m 一圈，和官方点光照到角色的范围相当。</summary>
        private const float BeaconRange = 16f;
        private static readonly Color BeaconColor = new Color(1f, 0.9f, 0.7f) * 1.8f;
        /// <summary>阀门 2（潮位站）的手轮被拆走了，要带着阀门手轮才拧得开。</summary>
        private const int TideGaugeValve = 2;
        // 海堤增援的落点（three.js 原型坐标 (82,20)(85,21)(79,22)，Unity z 取反）。
        private static readonly Vector3[] Reinforcements = { new Vector3(82f, 1f, -20f), new Vector3(85f, 1f, -21f), new Vector3(79f, 1f, -22f) };

        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly TidegateExtraction extraction;
        private readonly TidegateEnemies enemies;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> announce;
        private readonly TidegateSounds sounds;
        private readonly List<GameObject> owned = new List<GameObject>();
        private readonly bool[] valves;
        private bool lighthouseLit, boatArrived, disposed;
        private float boatAt = -1f, reinforceAt = -1f;
        private Transform beam;
        private Light beamLight;

        internal int ValveCount { get { return valves.Length; } }
        internal int OpenValves { get { int open = 0; foreach (bool v in valves) if (v) open++; return open; } }
        /// <summary>潮位站那一处阀门开了没有（没有这处阀门的旧包按「开了」算，不卡引导）。</summary>
        internal bool TideGaugeOpen { get { return valves.Length <= TideGaugeValve || valves[TideGaugeValve]; } }
        internal bool BeaconLit { get { return lighthouseLit; } }

        internal TidegateDevices(TidegateWorld world, TidegateTide tide, TidegateExtraction extraction, TidegateEnemies enemies,
            TidegateSounds sounds, Func<bool> valid, Action<string, bool> announce)
        {
            this.world = world;
            this.tide = tide;
            this.extraction = extraction;
            this.enemies = enemies;
            this.sounds = sounds;
            this.valid = valid;
            this.announce = announce;
            valves = new bool[world.Valves.Count];
            for (int i = 0; i < world.Valves.Count; i++)
            {
                int index = i;
                Add(world.Valves[i], "Valve" + i, new Vector3(0f, 0.4f, 0f), ValveSeconds,
                    delegate { return ValveLabel(index); },
                    delegate { return !valves[index]; }, delegate { TurnValve(index); },
                    index == TideGaugeValve ? BossRushItemIds.TidegateHandwheel : 0);
            }
            if (world.Lighthouse != null)
                Add(world.Lighthouse, "Lighthouse", new Vector3(0f, 0.4f, 0f), LighthouseSeconds,
                    delegate { return L10n.T("点灯叫船（会惊动守卫）", "Signal the boat (alerts guards)"); },
                    delegate { return !lighthouseLit; }, LightLighthouse);
            Transform ferry;
            if (extraction != null && world.Exits.TryGetValue("ferry", out ferry))
                Add(ferry, "FerryLine", new Vector3(0f, 0.4f, 0f), 0.5f,
                    delegate { return L10n.T("抓渡船缆绳（轻装登船）", "Catch the ferry line (light load)"); },
                    delegate { return extraction.FerryLineAvailable; }, CatchFerryLine);
        }

        private void CatchFerryLine()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (disposed || player == null || !valid()) return;
            string message;
            bool caught = extraction.TryCatchFerryLine(player.transform.position, out message);
            announce(message, !caught);
        }

        /// <summary>
        /// 阀门名只写地点；潮位站的手轮在原版需求行显示，空手不会开始 3 秒读条。
        /// </summary>
        private static string ValveLabel(int index)
        {
            return L10n.T("拧开阀门（", "Open the valve (") + ValveName(index) + L10n.T("）", ")");
        }

        /// <summary>还没拧开的阀门在哪（HUD 目标行用），按语言用顿号或逗号隔开。</summary>
        internal string RemainingValves()
        {
            var names = new List<string>(valves.Length);
            for (int i = 0; i < valves.Length; i++) if (!valves[i]) names.Add(ValveName(i));
            return string.Join(L10n.T("、", ", "), names.ToArray());
        }

        /// <summary>阀门 0 在水厂泵房、1 在镇区泵站、2 在潮位站（要手轮）；顺序与场景包标记 `GP_valve_<i>` 一致。</summary>
        private static string ValveName(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("第三水厂泵房", "Waterworks pump house");
                case 1: return L10n.T("镇区泵站", "Town pump station");
                default: return L10n.T("潮位站", "Tide gauge station");
            }
        }

        private void Add(Transform anchor, string id, Vector3 offset, float seconds, Func<string> label, Func<bool> available, Action action,
            int requiredItemId = 0)
        {
            GameObject host = new GameObject("TidegateDevice_" + id);
            host.transform.SetParent(world.Root.transform, false);
            host.transform.position = anchor.position;
            owned.Add(host);
            TidegateDeviceInteractable device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<TidegateDeviceInteractable>(
                host.transform, "Interact", offset, x =>
                {
                    x.Bind(id, seconds, label, delegate { return valid() && available(); }, action);
                    x.BindRequirement(requiredItemId);
                    SphereCollider sensor = x.GetComponent<SphereCollider>();
                    if (sensor != null) { sensor.radius = 1.1f; sensor.isTrigger = true; }
                });
            if (device == null) Debug.LogWarning(TidegateRaidScene.LogTag + " 机关交互点创建失败：" + id);
        }

        private void TurnValve(int index)
        {
            if (disposed || valves[index]) return;
            if (index == TideGaugeValve)
            {
                // 潮位站的手轮被拆走了：身上得带着阀门手轮（挖沙船船舱储物柜），装上就留在阀门上。
                // Dev 演练的旁路不读、也不消耗背包里的手轮（正式构建 DevBypassHandwheel 恒为 false，照常找手轮）。
                Item wheel = DevBypassHandwheel() ? null : TidegateInventory.FindCarried(CharacterMainControl.Main, BossRushItemIds.TidegateHandwheel);
                if (wheel == null && !DevBypassHandwheel())
                {
                    announce(L10n.T("潮位站的阀门手轮被人拆走了。闸伯的信里说，淘沙帮把它扔在了挖沙船船舱里。",
                        "The tide gauge valve has no handwheel. Old Zha's letter says the sand gang tossed it in the dredger's cabin."), true);
                    return;
                }
                TidegateInventory.Consume(wheel);
            }
            valves[index] = true;
            if (sounds != null) sounds.Play(TidegateCue.Valve, world.Valves[index].position);
            int open = OpenValves;
            if (open < valves.Length)
            {
                announce(ValveName(index) + L10n.T(" 的阀门开了（", " valve opened (") + open + "/" + valves.Length + L10n.T("）", ")"), false);
                return;
            }
            tide.OpenSluice();
            // 大闸在泻湖那头，多半离得远：闷响跟着玩家放，全图都知道闸起来了。
            if (sounds != null) sounds.Play(TidegateCue.Gate, null);
            reinforceAt = Time.time + 4f;
            announce(L10n.T("大闸提起来了！等退潮进储蓄所。泻湖在放水，千万别游到闸口。",
                "The tide gate is up! The bank will surface at low tide. Stay clear of the sluice intake while the lagoon drains."), true);
        }


        /// <summary>Dev 演练的旁路（TidegateDrillHooks.cs）：潮位站那一处不发物品、当作已经装上手轮。正式构建恒为 false。</summary>
        private bool DevBypassHandwheel()
        {
#if BOSSRUSH_DEV
            return devBypassHandwheel;
#else
            return false;
#endif
        }

        private void LightLighthouse()
        {
            if (disposed || lighthouseLit) return;
            lighthouseLit = true;
            boatAt = Time.time + BoatDelay;
            if (enemies != null) enemies.Lure(world.Lighthouse.position, LureRadius);
            if (enemies != null) enemies.RevealOnMap(RevealSeconds);
            if (sounds != null) sounds.Play(TidegateCue.Lighthouse, world.Lighthouse.position);
            LightBeam();
            announce(L10n.T("灯塔亮了，附近的守卫可能听见了。M 地图显示敌情 40 秒，船 45 秒后到灯塔码头。",
                "The lighthouse is lit. Nearby guards may have heard it. M shows contacts for 40s. Boat at the jetty in 45s."), false);
        }

        /// <summary>
        /// 打开场景包里预置的光束（作者工程 `Visual/VIS_Fx/LighthouseBeam`，默认未激活）。灯室挂一盏真实点光源，但它只照得亮
        /// 官方着色器的角色：潮闸镇的地面与建筑是自研着色器（GBuffer 里 albedo 置 0，延迟光源对它们不起作用），
        /// 所以灯头同时进潮汐 owner 的灯数组（<see cref="TidegateTide.SetBeacon"/>），地面和塔身才和角色一起亮（CR-2026-11-05-020）。
        /// 旧包没有这个节点时只少了光，灯塔照常能点。
        /// </summary>
        private void LightBeam()
        {
            beam = world.Root.transform.Find("Visual/VIS_Fx/LighthouseBeam");
            if (beam == null)
            {
                Debug.LogWarning(TidegateRaidScene.LogTag + " 场景包里没有灯塔光束节点，只点亮不出光");
                return;
            }
            beam.gameObject.SetActive(true);
            if (tide != null) tide.SetBeacon(beam.position, BeaconRange, BeaconColor);
            var go = new GameObject("TidegateLighthouseLight");
            go.transform.SetParent(beam, false);
            beamLight = go.AddComponent<Light>();
            beamLight.type = LightType.Point;
            beamLight.color = new Color(1f, 0.9f, 0.7f);
            beamLight.range = 14f;
            beamLight.intensity = 2.2f;
            beamLight.shadows = LightShadows.None;
            owned.Add(go);
        }

        internal void Tick()
        {
            if (disposed) return;
            if (beam != null) beam.Rotate(0f, BeamSpeed * Time.deltaTime, 0f, Space.World);
            if (reinforceAt > 0f && Time.time >= reinforceAt)
            {
                reinforceAt = -1f;
                if (enemies != null) foreach (Vector3 point in Reinforcements) enemies.SpawnExtra(point, "digger");
                announce(L10n.T("海堤有动静：淘沙帮正往金库赶。", "Movement on the seawall: the sand gang is headed for the vault."), true);
            }
            if (!boatArrived && boatAt > 0f && Time.time >= boatAt)
            {
                boatArrived = true;
                if (extraction != null) extraction.SetLighthouseBoat(true);
                Transform jetty;
                if (sounds != null && world.Exits.TryGetValue("lh", out jetty)) sounds.Play(TidegateCue.Horn, jetty.position);
                announce(L10n.T("船到灯塔码头了，可以从那边撤。", "The boat is at the lighthouse jetty. You can leave from there."), false);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (GameObject host in owned) if (host != null) UnityEngine.Object.Destroy(host);
            owned.Clear();
            if (beam != null) beam.gameObject.SetActive(false);
            beam = null;
            beamLight = null;
        }
    }

    /// <summary>潮闸镇机关的官方交互点：交互名随语言重写，读条时长写进官方私有字段。</summary>
    public sealed class TidegateDeviceInteractable : TidegateRequirementInteractable
    {
        private string key = "BossRush_Tidegate_Device";
        private Func<string> label;
        private string labelText;
        private Func<bool> available;
        private Action action;

        protected override string InteractNameKey { get { return key; } }
        protected override string LogPrefix { get { return TidegateRaidScene.LogTag + " "; } }

        internal void Bind(string id, float seconds, Func<string> title, Func<bool> canUse, Action callback)
        {
            key = "BossRush_Tidegate_Device_" + id;
            label = title;
            available = canUse;
            action = callback;
            Relabel();
            // 读条时长是官方 InteractableBase 的私有序列化字段，AddComponent 出来恒为 0；写进去再读回核对。
            ModeFItemConfigHelper.SetHiddenMember(this, "interactTime", seconds);
            if (Mathf.Abs(InteractTime - seconds) > 0.01f)
                Debug.LogWarning(TidegateRaidScene.LogTag + " 机关读条时长没有写进官方字段（可能已改名），将变成立即完成：" + id);
        }

        /// <summary>
        /// 交互名在靠近时按当前语言取，字有变化才重新注入。
        /// </summary>
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

        protected override void OnInteractCompleted()
        {
            if (action != null) action();
        }
    }
}

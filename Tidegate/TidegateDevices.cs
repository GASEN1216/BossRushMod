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
    internal sealed class TidegateDevices : IDisposable
    {
        private const float ValveSeconds = 3f, LighthouseSeconds = 2f;
        private const float BoatDelay = 45f, LureRadius = 60f, RevealSeconds = 40f;
        /// <summary>光束转速（度 / 秒）：一圈约 12 秒，夜里远处也看得见在转。</summary>
        private const float BeamSpeed = 30f;
        // 海堤增援的落点（three.js 原型坐标 (82,20)(85,21)(79,22)，Unity z 取反）。
        private static readonly Vector3[] Reinforcements = { new Vector3(82f, 1f, -20f), new Vector3(85f, 1f, -21f), new Vector3(79f, 1f, -22f) };

        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly TidegateExtraction extraction;
        private readonly TidegateEnemies enemies;
        private readonly Func<bool> valid;
        private readonly Action<string, bool> announce;
        private readonly List<GameObject> owned = new List<GameObject>();
        private readonly bool[] valves;
        private bool lighthouseLit, boatArrived, disposed;
        private float boatAt = -1f, reinforceAt = -1f;
        private Transform beam;
        private Light beamLight;

        internal TidegateDevices(TidegateWorld world, TidegateTide tide, TidegateExtraction extraction, TidegateEnemies enemies,
            Func<bool> valid, Action<string, bool> announce)
        {
            this.world = world;
            this.tide = tide;
            this.extraction = extraction;
            this.enemies = enemies;
            this.valid = valid;
            this.announce = announce;
            valves = new bool[world.Valves.Count];
            for (int i = 0; i < world.Valves.Count; i++)
            {
                int index = i;
                Add(world.Valves[i], "Valve" + i, new Vector3(0f, 0.4f, 0f), ValveSeconds,
                    delegate { return L10n.T("拧开阀门", "Open the valve") + "（" + ValveName(index) + "）"; },
                    delegate { return !valves[index]; }, delegate { TurnValve(index); });
            }
            if (world.Lighthouse != null)
                Add(world.Lighthouse, "Lighthouse", new Vector3(0f, 0.4f, 0f), LighthouseSeconds,
                    delegate { return L10n.T("点亮灯塔", "Light the lighthouse"); },
                    delegate { return !lighthouseLit; }, LightLighthouse);
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

        private void Add(Transform anchor, string id, Vector3 offset, float seconds, Func<string> label, Func<bool> available, Action action)
        {
            GameObject host = new GameObject("TidegateDevice_" + id);
            host.transform.SetParent(world.Root.transform, false);
            host.transform.position = anchor.position;
            owned.Add(host);
            TidegateDeviceInteractable device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<TidegateDeviceInteractable>(
                host.transform, "Interact", offset, x =>
                {
                    x.Bind(id, seconds, label, delegate { return valid() && available(); }, action);
                    SphereCollider sensor = x.GetComponent<SphereCollider>();
                    if (sensor != null) { sensor.radius = 1.1f; sensor.isTrigger = true; }
                });
            if (device == null) Debug.LogWarning(TidegateRaidScene.LogTag + " 机关交互点创建失败：" + id);
        }

        private void TurnValve(int index)
        {
            if (disposed || valves[index]) return;
            if (index == 2)
            {
                // 潮位站的手轮被拆走了：身上得带着阀门手轮（挖沙船船舱储物柜），装上就留在阀门上。
                Item wheel = TidegateInventory.Find(CharacterMainControl.Main, BossRushItemIds.TidegateHandwheel);
                if (wheel == null)
                {
                    announce(L10n.T("潮位站的阀门手轮被人拆走了。闸伯的信里说，淘沙帮把它扔在了挖沙船船舱里。",
                        "The tide gauge valve has no handwheel. Old Zha's letter says the sand gang tossed it in the dredger's cabin."), true);
                    return;
                }
                TidegateInventory.Consume(wheel);
            }
            valves[index] = true;
            int open = 0;
            foreach (bool v in valves) if (v) open++;
            if (open < valves.Length)
            {
                announce(ValveName(index) + L10n.T(" 的阀门开了（", " valve opened (") + open + "/" + valves.Length + L10n.T("）", ")"), false);
                return;
            }
            tide.OpenSluice();
            reinforceAt = Time.time + 4f;
            announce(L10n.T("三处阀门都开了，大闸提起来了！泻湖开始放水，等退潮储蓄所就露出来了。",
                "All three valves are open and the tide gate is up! The lagoon is draining; at low tide the sunken bank surfaces."), false);
        }


        private void LightLighthouse()
        {
            if (disposed || lighthouseLit) return;
            lighthouseLit = true;
            boatAt = Time.time + BoatDelay;
            if (enemies != null) enemies.Lure(world.Lighthouse.position, LureRadius, RevealSeconds);
            if (enemies != null) enemies.RevealOnMap(RevealSeconds);
            LightBeam();
            announce(L10n.T("灯塔亮了。45 秒后灯塔码头来船——附近的人也都看见了。",
                "The lighthouse is lit. A boat reaches the lighthouse jetty in 45 seconds — and everyone nearby saw it."), false);
        }

        /// <summary>
        /// 打开场景包里预置的光束（作者工程 `Visual/VIS_Fx/LighthouseBeam`，默认未激活），灯室再挂一盏真实点光源照亮周围的角色与地面。
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
                announce(L10n.T("海堤那边来人了：淘沙帮奔着金库去了。", "Movement on the seawall: the sand gang is heading for the vault."), true);
            }
            if (!boatArrived && boatAt > 0f && Time.time >= boatAt)
            {
                boatArrived = true;
                if (extraction != null) extraction.SetLighthouseBoat(true);
                announce(L10n.T("船到灯塔码头了，可以从那边撤。", "The boat is at the lighthouse jetty. You can leave from there."), false);
            }
        }

        /// <summary>HUD 小标：阀门与大闸进度。</summary>
        internal string Describe()
        {
            int open = 0;
            foreach (bool v in valves) if (v) open++;
            if (tide != null && tide.SluiceOpen)
                return L10n.T("大闸已提起", "Tide gate up");
            return L10n.T("阀门 ", "Valves ") + open + "/" + valves.Length;
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
    public sealed class TidegateDeviceInteractable : BossRushBuildingInteractableBase
    {
        private string key = "BossRush_Tidegate_Device";
        private Func<string> label;
        private int labelLanguage = -1;
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

        /// <summary>交互名每次靠近时按当前语言重写一次（玩家能在游戏里切语言）。</summary>
        private void Relabel()
        {
            int language = L10n.IsChinese ? 1 : 0;
            if (label == null || language == labelLanguage) return;
            labelLanguage = language;
            LocalizationHelper.InjectLocalization(key, label());
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

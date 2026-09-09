using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>COMPAT：地图会话独占的空间音效与装置反馈；集中距离调度，不控制碰撞或剧情。</summary>
    internal sealed class SkyIslandAmbience : IDisposable
    {
        private sealed class Device
        {
            internal GameObject Root;
            internal Transform Rotor;
            internal Light Light;
            internal int Flag;
            internal bool Near, Looping;
            internal string Ambient;
            internal float NextSound, Phase;
            internal float Speed;
        }

        private readonly List<Device> devices = new List<Device>();
        private readonly string soundDirectory;
        private readonly MethodInfo postSound, stopAll;
        private readonly object stopImmediately;
        private int flags = -1;
        private float nextDistanceCheck;
        private bool disposed, audioWarning;
        private Vector3 playerPosition;

        internal SkyIslandAmbience(GameObject root)
        {
            if (root == null) throw new ArgumentNullException("root");
            soundDirectory = Path.Combine(ModBehaviour.GetModPath(), "Assets", "Sounds", "SkyIsland");
            // 正式构建不引用 FMOD：绑定官方音效入口及其 owner 的停止方法，不持有全局音源。
            postSound = typeof(Duckov.AudioManager).GetMethod("PostCustomSFX", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
            stopAll = typeof(Duckov.AudioObject).GetMethod("StopAll", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stopAll != null) stopImmediately = Enum.ToObject(stopAll.GetParameters()[0].ParameterType, 1);
            Add(root, "POI_B", 0, new Color(1f, .82f, .50f), "wind_chimes.wav", 5f);
            Add(root, "Search_D", (int)SkyIslandStoryFlag.WindBeacon, new Color(.45f, .90f, .78f), "island_wind.wav", 48f);
            Add(root, "Search_G", (int)SkyIslandStoryFlag.StarLamp, new Color(.62f, .90f, 1f), null, 15f);
            Add(root, "Search_S4", (int)SkyIslandStoryFlag.Telescope, new Color(.80f, .68f, 1f), null, 9f);
            Add(root, "Search_C", (int)SkyIslandStoryFlag.PlantingDelivered, new Color(.98f, .70f, .52f), null, 65f);
            Add(root, "Search_H", (int)SkyIslandStoryFlag.Ending, new Color(1f, .88f, .57f), null, 6f);
        }

        private void Add(GameObject map, string marker, int required, Color color, string ambient, float speed)
        {
            Transform anchor = map.transform.Find(marker);
            if (anchor == null) return;
            GameObject owned = new GameObject("SkyIslandAmbience_" + marker);
            owned.transform.SetParent(map.transform, false);
            owned.transform.position = anchor.position + Vector3.up * 3.2f;
            GameObject rotor = new GameObject("MovingLightFilaments");
            rotor.transform.SetParent(owned.transform, false);
            var device = new Device { Root = owned, Rotor = rotor.transform, Flag = required,
                Ambient = ambient, Speed = speed, Phase = devices.Count * 1.37f, NextSound = Time.time + 2 + devices.Count };
            device.Light = owned.AddComponent<Light>();
            device.Light.type = LightType.Point;
            device.Light.shadows = LightShadows.None;
            device.Light.range = required == (int)SkyIslandStoryFlag.Ending ? 20f : 12f;
            device.Light.color = color;
            device.Light.intensity = 0;
            // 独立光丝没有 collider，不会改变合并场景网格、寻路或射线命中。
            if (required == (int)SkyIslandStoryFlag.WindBeacon || required == (int)SkyIslandStoryFlag.PlantingDelivered)
            {
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI * .5f;
                    Vector3 axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 side = new Vector3(-axis.z, 0, axis.x);
                    Line(rotor.transform, color, new[] { Vector3.zero, axis * 1.6f, axis * .7f + side * .5f, Vector3.zero }, false);
                }
            }
            else
            {
                Vector3[] points = new Vector3[24];
                for (int i = 0; i < points.Length; i++)
                {
                    float angle = i * Mathf.PI * 2f / points.Length;
                    float radius = required == (int)SkyIslandStoryFlag.StarLamp ? (i % 3 == 0 ? 1.6f : .85f) : 1.2f;
                    points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle * 2) * .18f, Mathf.Sin(angle) * radius);
                }
                Line(rotor.transform, color, points, true);
            }
            owned.SetActive(false);
            devices.Add(device);
        }

        private static void Line(Transform parent, Color color, Vector3[] points, bool loop)
        {
            GameObject child = new GameObject("LightFilament");
            child.transform.SetParent(parent, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = BossRush.Common.Effects.RingParticleEffect.GetSharedParticleMaterial();
            line.useWorldSpace = false;
            line.startWidth = line.endWidth = .055f;
            line.startColor = line.endColor = color;
            line.loop = loop;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        internal void ApplyStoryFlags(int value)
        {
            if (disposed || flags == value) return;
            int added = flags < 0 ? 0 : value & ~flags;
            flags = value;
            foreach (Device device in devices)
            {
                UpdateDistance(device);
                // 重访只恢复世界状态，不能把旧存档当作一次新的装置完成事件。
                if (device.Near && device.Flag != 0 && (added & device.Flag) != 0)
                    Play(device, device.Flag == (int)SkyIslandStoryFlag.Ending ? "homecoming_bell.wav" : "device_awake.wav", false);
            }
        }

        internal void Tick(Vector3 position)
        {
            if (disposed) return;
            playerPosition = position;
            bool checkDistance = Time.time >= nextDistanceCheck;
            if (checkDistance) nextDistanceCheck = Time.time + .25f;
            foreach (Device device in devices)
            {
                if (device.Root == null) continue;
                if (checkDistance) UpdateDistance(device);
                if (!device.Near) continue;
                device.Rotor.localRotation = Quaternion.Euler(0, Time.time * device.Speed, Mathf.Sin(Time.time * .7f + device.Phase) * 6f);
                device.Light.intensity = 1.2f + Mathf.Sin(Time.time * 1.5f + device.Phase) * .18f;
                if (device.Ambient == null || Time.time < device.NextSound || device.Looping) continue;
                bool loop = device.Ambient == "island_wind.wav";
                device.Looping = Play(device, device.Ambient, loop) && loop;
                device.NextSound = Time.time + (loop ? 10 : 13 + device.Phase);
            }
        }

        private void UpdateDistance(Device device)
        {
            if (device.Root == null) return;
            bool enabled = device.Flag == 0 || (flags >= 0 && (flags & device.Flag) != 0);
            // 进入 38m、离开 44m 的滞回防止区域边沿反复启停；只影响本类装饰对象。
            float radius = device.Near ? 44f : 38f;
            bool near = enabled && (device.Root.transform.position - playerPosition).sqrMagnitude <= radius * radius;
            if (device.Near == near) return;
            if (!near) Stop(device);
            device.Near = near;
            device.Root.SetActive(near);
        }

        private bool Play(Device device, string file, bool loop)
        {
            try
            {
                if (postSound == null || stopAll == null) throw new InvalidOperationException("官方空间音效生命周期入口缺失");
                string path = Path.Combine(soundDirectory, file);
                if (!File.Exists(path)) throw new FileNotFoundException("天空岛音效未部署", file);
                return postSound.Invoke(null, new object[] { path, device.Root, loop }) != null;
            }
            catch (Exception e)
            {
                if (!audioWarning) { audioWarning = true; ModBehaviour.DevLog("[SkyIsland] [WARNING] 局部音效不可用：" + e.Message); }
                return false;
            }
        }

        private void Stop(Device device)
        {
            try
            {
                // AudioObject 不承诺 OnDestroy 停止 programmer sound，因此先显式 StopAll，再销毁 emitter。
                Duckov.AudioObject audio = device.Root == null ? null : device.Root.GetComponent<Duckov.AudioObject>();
                if (audio != null && stopAll != null) stopAll.Invoke(audio, new[] { stopImmediately });
            }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIsland] [WARNING] 局部音效停止失败：" + e.Message); }
            device.Looping = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Device device in devices)
            {
                Stop(device);
                if (device.Root != null) UnityEngine.Object.Destroy(device.Root);
            }
            devices.Clear();
        }
    }
}

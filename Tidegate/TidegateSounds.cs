using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>潮闸镇的一次性提示音（文件见 <see cref="TidegateSounds"/>）。</summary>
    internal enum TidegateCue
    {
        /// <summary>涨潮警报：全图都要听得见，跟着玩家放。</summary>
        Siren = 0,
        /// <summary>来船的汽笛：在来船的码头放。</summary>
        Horn = 1,
        /// <summary>阀门拧开：在阀门处放。</summary>
        Valve = 2,
        /// <summary>大闸提起：远处的闷响，跟着玩家放。</summary>
        Gate = 3,
        /// <summary>灯塔点亮：在灯塔处放。</summary>
        Lighthouse = 4
    }

    /// <summary>
    /// COMPAT：潮闸镇的声音（CR-2026-11-05-021）。文件由 `tools/gen_tidegate_sfx.py` 程序化生成，放 `Assets/Sounds/Tidegate`，
    /// 随正式编译的 `Assets\Sounds` 整树部署。正式构建不引用 FMOD：与 `SkyIslandAmbience` 同一套
    /// `AudioManager.PostCustomSFX` / `AudioObject.StopAll` 反射绑定（官方把事件挂在发声体上做 3D 定位）。
    /// - 海浪：沿岸几处发声体循环，进 38 m 起播、出 44 m 停（滞回，口径同天空岛），只在近处响；
    /// - 浮标钟：栈桥渡口一只，近处每 14–20 秒一声；
    /// - 提示音：警报与大闸跟着玩家放（全图都得知道），汽笛、阀门、灯塔在事发处放，45 m 外不放。
    /// 循环声不随发声体销毁而停（官方 AudioObject 没有 OnDestroy），销毁前一律先 StopAll。文件缺失或绑定失败只记一次警告，玩法照常。
    /// </summary>
    internal sealed class TidegateSounds : IDisposable
    {
        private const float NearRadius = 38f, FarRadius = 44f, AudibleRange = 45f;
        private static readonly string[] CueFiles = { "tide_siren.wav", "boat_horn.wav", "valve_turn.wav", "gate_lift.wav", "lighthouse_ignite.wav" };
        private const string WavesFile = "sea_waves.wav", BellFile = "buoy_bell.wav";
        /// <summary>海浪发声体放在这几处地标（`POI_<中文名>`）：码头、滩涂、栈桥、灯塔岬，都在外海边上。</summary>
        private static readonly string[] ShorePois = { "POI_码头", "POI_滩涂", "POI_栈桥", "POI_灯塔岬" };

        private sealed class Emitter
        {
            internal GameObject Root;
            internal Duckov.AudioObject Audio;
            internal string File;
            internal bool Loop, Near, Playing;
            internal float NextSound;
        }

        private readonly List<Emitter> ambient = new List<Emitter>();
        private readonly List<GameObject> owned = new List<GameObject>();
        private readonly string directory;
        private readonly MethodInfo postSound, stopAll;
        private readonly object stopImmediately;
        private readonly GameObject follow, spot;
        private readonly System.Random random = new System.Random(20261105);
        private float nextDistanceCheck;
        private bool disposed, warned;

        internal TidegateSounds(TidegateWorld world)
        {
            if (world == null || world.Root == null) throw new ArgumentNullException("world");
            try
            {
                directory = Path.Combine(ModBehaviour.GetModPath(), "Assets", "Sounds", "Tidegate");
                postSound = typeof(Duckov.AudioManager).GetMethod("PostCustomSFX", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
                stopAll = typeof(Duckov.AudioObject).GetMethod("StopAll", BindingFlags.NonPublic | BindingFlags.Instance);
                if (stopAll != null) stopImmediately = Enum.ToObject(stopAll.GetParameters()[0].ParameterType, 1);
                follow = Own(world.Root.transform, "TidegateSound_Player");
                spot = Own(world.Root.transform, "TidegateSound_Spot");
                foreach (Transform poi in world.Pois)
                    if (Array.IndexOf(ShorePois, poi.name) >= 0) AddAmbient(world.Root.transform, poi.position, WavesFile, true);
                Transform ferry;
                if (world.Exits.TryGetValue("ferry", out ferry)) AddAmbient(world.Root.transform, ferry.position, BellFile, false);
            }
            catch { Dispose(); throw; }
        }

        private GameObject Own(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            owned.Add(go);
            return go;
        }

        private void AddAmbient(Transform parent, Vector3 position, string file, bool loop)
        {
            GameObject go = Own(parent, "TidegateSound_" + Path.GetFileNameWithoutExtension(file) + "_" + ambient.Count);
            go.transform.position = position + Vector3.up * 1.2f;
            ambient.Add(new Emitter { Root = go, File = file, Loop = loop, NextSound = Time.time + 2f + ambient.Count });
        }

        /// <summary>会话每帧调：跟随发声体贴着玩家；距离 0.25 秒采一次，进出滞回半径时起停海浪。</summary>
        internal void Tick(Vector3 player)
        {
            if (disposed) return;
            if (follow != null) follow.transform.position = player + Vector3.up;
            bool check = Time.time >= nextDistanceCheck;
            if (check) nextDistanceCheck = Time.time + 0.25f;
            foreach (Emitter emitter in ambient)
            {
                if (emitter.Root == null) { if (emitter.Playing) Stop(emitter); continue; }
                if (check)
                {
                    float radius = emitter.Near ? FarRadius : NearRadius;
                    bool near = (emitter.Root.transform.position - player).sqrMagnitude <= radius * radius;
                    if (near != emitter.Near)
                    {
                        emitter.Near = near;
                        if (!near) Stop(emitter);
                    }
                }
                if (!emitter.Near || emitter.Playing || Time.time < emitter.NextSound) continue;
                bool posted = Post(emitter.File, emitter.Root, emitter.Loop, ref emitter.Audio);
                emitter.Playing = posted && emitter.Loop;
                emitter.NextSound = Time.time + (emitter.Loop ? 10f : 14f + (float)random.NextDouble() * 6f);
            }
        }

        /// <summary>放一次提示音。<paramref name="at"/> 为空时跟着玩家放；否则在那一点放，离玩家 45 m 外不放。</summary>
        internal void Play(TidegateCue cue, Vector3? at)
        {
            int index = (int)cue;
            if (disposed || index < 0 || index >= CueFiles.Length) return;
            Duckov.AudioObject unused = null;
            if (at == null)
            {
                Post(CueFiles[index], follow, false, ref unused);
                return;
            }
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || spot == null || (main.transform.position - at.Value).sqrMagnitude > AudibleRange * AudibleRange) return;
            spot.transform.position = at.Value + Vector3.up;
            Post(CueFiles[index], spot, false, ref unused);
        }

        private bool Post(string file, GameObject emitter, bool loop, ref Duckov.AudioObject audio)
        {
            try
            {
                if (emitter == null) return false;
                if (postSound == null || stopAll == null) throw new InvalidOperationException("官方空间音效生命周期入口缺失");
                string path = Path.Combine(directory, file);
                if (!File.Exists(path)) throw new FileNotFoundException("潮闸镇音效未部署", file);
                bool posted = postSound.Invoke(null, new object[] { path, emitter, loop }) != null;
                // 官方 PostCustomSFX 经 AudioObject.GetOrCreate 把事件挂在发声体上；趁它活着记住引用，发声体先没了也停得掉。
                if (posted && (object)audio == null) audio = emitter.GetComponent<Duckov.AudioObject>();
                return posted;
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; Debug.LogWarning(TidegateRaidScene.LogTag + " 音效不可用（玩法照常）：" + e.Message); }
                return false;
            }
        }

        private void Stop(Emitter emitter)
        {
            StopAll(emitter.Audio, emitter.Root);
            emitter.Playing = false;
        }

        private void StopAll(Duckov.AudioObject audio, GameObject root)
        {
            try
            {
                // ReferenceEquals 绕开 Unity 的判空重载：发声体被别处先销毁时仍能停掉手里的 FMOD 事件（口径同 SkyIslandAmbience.Stop）。
                if (ReferenceEquals(audio, null) && root != null) audio = root.GetComponent<Duckov.AudioObject>();
                if (!ReferenceEquals(audio, null) && stopAll != null) stopAll.Invoke(audio, new[] { stopImmediately });
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 音效停止失败：" + e.Message); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Emitter emitter in ambient) Stop(emitter);
            ambient.Clear();
            foreach (GameObject go in owned)
            {
                if (go == null) continue;
                StopAll(null, go);
                UnityEngine.Object.Destroy(go);
            }
            owned.Clear();
        }
    }
}

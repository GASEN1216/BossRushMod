#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BossRush
{
    // 播放模式经过真实 Domain Reload 后重新装配入口；不把编辑模式截图当粒子运行证据。
    [InitializeOnLoad]
    public static class EclipseBloomParticleRegression
    {
        [Serializable]
        public sealed class Settings { public string modRoot; public string reportPath; }
        public static Settings Configuration;
        private const string PendingKey = "BossRush.EclipseBloomParticles.Pending";

        static EclipseBloomParticleRegression()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            Configuration = JsonUtility.FromJson<Settings>(File.ReadAllText(Path.Combine(Application.dataPath, "../particle-regression.json")));
            new GameObject("EclipseParticleRegression").AddComponent<EclipseParticleProbe>();
        }
    }

    public sealed class EclipseParticleProbe : MonoBehaviour
    {
        [Serializable]
        public sealed class Sample
        {
            public string stage, name, xMode, yMode, zMode;
            public int particles;
            public bool velocityEnabled;
        }

        [Serializable]
        public sealed class Result
        {
            public bool passed, playMode, phase2, phase3, petalsBroken, deathBurst, deathCleanup;
            public string unityVersion;
            public int simulatedSystems, velocityErrors;
            public List<string> errors = new List<string>();
            public List<string> failures = new List<string>();
            public List<Sample> samples = new List<Sample>();
        }

        private readonly Result result = new Result();
        private EclipseBloomBody body;
        private CharacterMainControl host;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            result.playMode = Application.isPlaying;
            result.unityVersion = Application.unityVersion;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            // 不能在 catch 内 yield；每一步用带错误记录的命名方法隔离。
            Execute(Setup);
            yield return null;
            Execute(delegate { SampleSystems("birth", true, true); });
            yield return null;
            Execute(delegate { body.ApplyFrame(1, 15, 9f, 1, 0.8f, false); SampleSystems("phase1", true, true); });
            yield return null;
            Execute(delegate
            {
                body.ApplyFrame(2, 15, 27f, 5, 1f, true);
                result.phase2 = Read<int>("_phase") == 2;
                SampleSystems("phase2", true, true);
            });
            yield return null;
            Execute(delegate
            {
                body.PlayPetalBreak(0);
                body.PlayPetalBreak(1);
                Require((Read<int>("_brokenMask") & 3) == 3, "phase2 petal break did not execute");
                SampleSystems("petalBreak", true, true);
            });
            yield return null;
            Execute(delegate
            {
                body.ApplyFrame(3, 12, 81f, 15, 1f, true);
                result.phase3 = Read<int>("_phase") == 3;
                SampleSystems("phase3", true, true);
                body.PlayPetalBreak(2);
                body.PlayPetalBreak(3);
                result.petalsBroken = Read<int>("_brokenMask") == 15;
                SampleSystems("allPetalsBroken", true, true);
            });
            yield return null;
            Execute(delegate
            {
                host.Health.IsDead = true;
                body.Dissipate();
                Require(!Read<ParticleSystem>("_embers").isEmitting && !Read<ParticleSystem>("_ash").isEmitting,
                    "death did not stop persistent emitters");
                SampleSystems("deathInward", false, false);
            });
            // AdvanceVisualFrame、PlayDeathBurst、4 秒后销毁都由真实播放帧驱动。
            float until = Time.realtimeSinceStartup + 15f;
            while (body != null && !Read<bool>("_deathBurst") && Time.realtimeSinceStartup < until) yield return null;
            Execute(delegate
            {
                result.deathBurst = body != null && Read<bool>("_deathBurst");
                Require(result.deathBurst, "production PlayDeathBurst was not reached");
                SampleSystems("deathBurst", false, false);
            });
            until = Time.realtimeSinceStartup + 15f;
            while (body != null && Time.realtimeSinceStartup < until) yield return null;
            result.deathCleanup = body == null;
            Require(result.deathCleanup, "production death cleanup left the body alive");
            if (host != null) Destroy(host.gameObject);
            yield return null;
            Finish();
        }

        private void Setup()
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.15f, 0f);
            ground.transform.localScale = new Vector3(40f, 0.3f, 40f);
            host = new GameObject("ParticleHost").AddComponent<CharacterMainControl>();
            body = EclipseBloomBody.Attach(host);
            if (body == null) throw new InvalidOperationException("real EclipseBloomBody.Attach failed");
            Physics.SyncTransforms();
        }

        private void SampleSystems(string stage, bool restart, bool requirePersistent)
        {
            if (body == null) throw new InvalidOperationException("body missing at " + stage);
            ParticleSystem[] systems = body.GetComponentsInChildren<ParticleSystem>(true);
            Require(systems.Length >= 2, stage + ": particle systems missing");
            int living = 0;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                // 直接调用真实引擎入口；混用曲线模式会在这里（及正常 Play 帧）报错。
                ps.Simulate(restart ? 0.35f : 0.05f, false, restart, false);
                if (restart) ps.Play(false);
                result.simulatedSystems++;
                var velocity = ps.velocityOverLifetime;
                var sample = new Sample { stage = stage, name = ps.name, particles = ps.particleCount,
                    velocityEnabled = velocity.enabled, xMode = velocity.x.mode.ToString(),
                    yMode = velocity.y.mode.ToString(), zMode = velocity.z.mode.ToString() };
                result.samples.Add(sample);
                if (velocity.enabled)
                    Require(velocity.x.mode == velocity.y.mode && velocity.y.mode == velocity.z.mode,
                        stage + "/" + ps.name + ": xyz velocity curve mode mismatch");
                if (requirePersistent && (ps == Read<ParticleSystem>("_embers") || ps == Read<ParticleSystem>("_ash")))
                    Require(ps.particleCount > 0, stage + "/" + ps.name + ": no live persistent particles");
                living += ps.particleCount;
            }
            Require(living > 0, stage + ": no live particles after native Simulate");
            if (requirePersistent)
            {
                ParticleSystem embers = Read<ParticleSystem>("_embers");
                ParticleSystem ash = Read<ParticleSystem>("_ash");
                Require(embers != null && ash != null, stage + ": embers/ash missing");
                if (embers != null)
                {
                    var velocity = embers.velocityOverLifetime;
                    Require(velocity.y.mode == ParticleSystemCurveMode.TwoConstants
                        && Mathf.Approximately(velocity.y.constantMin, -0.08f)
                        && Mathf.Approximately(velocity.y.constantMax, 0.3f), stage + ": ember vertical drift changed");
                }
            }
        }

        private T Read<T>(string field) { return (T)typeof(EclipseBloomBody).GetField(field, PrivateInstance).GetValue(body); }

        private void Execute(Action action)
        {
            try { action(); }
            catch (Exception error) { result.failures.Add(error.ToString()); }
        }

        private void Require(bool condition, string failure) { if (!condition) result.failures.Add(failure); }

        private void OnLog(string message, string stack, LogType type)
        {
            if (message.IndexOf("Particle Velocity curves must all be in the same mode", StringComparison.OrdinalIgnoreCase) >= 0)
                result.velocityErrors++;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                result.errors.Add(message + "\n" + stack);
        }

        private void Finish()
        {
            Require(result.playMode && result.phase2 && result.phase3 && result.petalsBroken && result.deathBurst && result.deathCleanup,
                "production lifecycle coverage incomplete");
            Require(result.simulatedSystems > 0, "native particle simulation was not executed");
            Require(result.velocityErrors == 0, "native particle velocity errors: " + result.velocityErrors);
            result.passed = result.failures.Count == 0 && result.errors.Count == 0;
            File.WriteAllText(EclipseBloomParticleRegression.Configuration.reportPath, JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= OnLog;
            Debug.Log("ECLIPSE_PARTICLE_REGRESSION " + (result.passed ? "PASS" : "FAIL") + " nativeSimulations="
                + result.simulatedSystems + " velocityErrors=" + result.velocityErrors + " failures=" + result.failures.Count);
            EditorApplication.Exit(result.passed ? 0 : 1);
        }
    }
}
#endif

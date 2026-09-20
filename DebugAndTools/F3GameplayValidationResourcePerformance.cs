#if BOSSRUSH_DEV
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace BossRush
{
    [Serializable]
    internal sealed class ResourcePerformanceWindow
    {
        public string caseId, scene, startedUtc, status, reason;
        public string steps = "Remain at the current position and camera; observe for 10 seconds; no state changes.";
        public bool read_only = true, cancelled, sceneChanged, frameTimingEnabled, overflow, hadLoadCancellation, hadLoadFailure;
        public string loadHistoryScope = "Most recent 512 operations; pending operations retain endedSeconds=0 until completion.";
        public double windowSeconds, fps;
        public int frames, rendererCount, meshCount, colliderCount, textureCount;
        public Vector3 position, cameraPosition;
        public Quaternion cameraRotation;
        public ResourceMetric frameInterval, cpuFrameTime, gpuFrameTime, gcAlloc, monoMemory, totalMemory, textureNativeMemory, meshNativeMemory, batches, setPassCalls;
        public ResourceBundleLoader.LoadRecord[] assetBundleLoads;
    }

    [Serializable]
    internal sealed class ResourcePerformanceReport
    {
        public int schema = 1;
        public bool read_only = true;
        public string runId;
        public string evidence = "L3 runtime sampling; unavailable metrics are not zero or PASS";
        public List<ResourcePerformanceWindow> windows = new List<ResourcePerformanceWindow>();
    }

    internal sealed partial class F3GameplayValidationRunner
    {
        private readonly ResourcePerformanceReport resourcePerformance = new ResourcePerformanceReport();

        private sealed class Counter : IDisposable
        {
            internal ProfilerRecorder Recorder;
            internal readonly double[] Values = new double[8192];
            internal int Count;
            internal readonly string Name, Unit;
            internal Counter(ProfilerCategory category, string name, string unit)
            {
                Name = name; Unit = unit;
                try { Recorder = ProfilerRecorder.StartNew(category, name, 1); }
                catch (Exception) { Recorder = default(ProfilerRecorder); }
            }
            internal void Capture()
            {
                if (Recorder.Valid && Recorder.Count > 0 && Count < Values.Length) Values[Count++] = Recorder.LastValue;
            }
            internal ResourceMetric Finish() { return ResourcePerformanceMetrics.Summarize(Values, Count, Unit, Name); }
            public void Dispose() { Recorder.Dispose(); }
        }

        private IEnumerator SampleResourcePerformance(string caseId)
        {
            var window = new ResourcePerformanceWindow { caseId = caseId, scene = SceneManager.GetActiveScene().path,
                startedUtc = DateTime.UtcNow.ToString("O"), status = "incomplete" };
            int sceneHandle = SceneManager.GetActiveScene().handle;
            if (CharacterMainControl.Main != null) window.position = CharacterMainControl.Main.transform.position;
            Camera camera = GameCamera.Instance != null ? GameCamera.Instance.renderCamera : null;
            if (camera != null) { window.cameraPosition = camera.transform.position; window.cameraRotation = camera.transform.rotation; }
            // Snapshot outside the timed window: object enumeration must not contaminate measured frames.
            window.rendererCount = UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length;
            window.colliderCount = UnityEngine.Object.FindObjectsOfType<Collider>(true).Length;
            Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            Texture[] textures = Resources.FindObjectsOfTypeAll<Texture>();
            window.meshCount = meshes.Length; window.textureCount = textures.Length;
            double meshBytes = 0, textureBytes = 0;
            foreach (Mesh mesh in meshes) meshBytes += Profiler.GetRuntimeMemorySizeLong(mesh);
            foreach (Texture texture in textures) textureBytes += Profiler.GetRuntimeMemorySizeLong(texture);
            window.meshNativeMemory = ResourcePerformanceMetrics.Summarize(new[] { meshBytes }, 1, "bytes", "GetRuntimeMemorySizeLong: loaded Mesh snapshot before window");
            window.textureNativeMemory = ResourcePerformanceMetrics.Summarize(new[] { textureBytes }, 1, "bytes", "GetRuntimeMemorySizeLong: loaded Texture snapshot before window");
            var intervals = new double[8192]; var cpu = new double[8192]; var gpu = new double[8192];
            var mono = new double[8192]; var total = new double[8192];
            int cpuCount = 0, gpuCount = 0;
            var timings = new FrameTiming[1];
            ulong lastTimestamp = 0;
            window.frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            using (var gc = new Counter(ProfilerCategory.Memory, "GC Allocated In Frame", "bytes/frame"))
            using (var cpuMain = new Counter(ProfilerCategory.Internal, "Main Thread", "ns"))
            using (var batches = new Counter(ProfilerCategory.Render, "Batches Count", "count/frame"))
            using (var setPass = new Counter(ProfilerCategory.Render, "SetPass Calls Count", "count/frame"))
            {
                // Warm the recorder without including its setup allocations in the first sample.
                double started = Time.realtimeSinceStartupAsDouble, previous = started;
                try
                {
                    yield return null;
                    started = previous = Time.realtimeSinceStartupAsDouble;
                    while (Time.realtimeSinceStartupAsDouble - started < 10)
                    {
                        window.cancelled = ShouldAbort();
                        window.sceneChanged = SceneManager.GetActiveScene().handle != sceneHandle;
                        if (window.cancelled || window.sceneChanged) break;
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        double now = Time.realtimeSinceStartupAsDouble;
                        if (window.frames == intervals.Length) { window.overflow = true; break; }
                        int i = window.frames++;
                        intervals[i] = (now - previous) * 1000; previous = now;
                        mono[i] = Profiler.GetMonoUsedSizeLong(); total[i] = Profiler.GetTotalAllocatedMemoryLong();
                        gc.Capture(); cpuMain.Capture(); batches.Capture(); setPass.Capture();
                        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != lastTimestamp)
                        {
                            lastTimestamp = timings[0].frameStartTimestamp;
                            if (timings[0].cpuFrameTime > 0) cpu[cpuCount++] = timings[0].cpuFrameTime;
                            if (timings[0].gpuFrameTime > 0) gpu[gpuCount++] = timings[0].gpuFrameTime;
                        }
                    }
                }
                finally
                {
                    window.windowSeconds = Time.realtimeSinceStartupAsDouble - started;
                    window.cancelled |= ShouldAbort();
                    window.sceneChanged |= SceneManager.GetActiveScene().handle != sceneHandle;
                    window.fps = window.windowSeconds > 0 ? window.frames / window.windowSeconds : 0;
                    window.frameInterval = ResourcePerformanceMetrics.Summarize(intervals, window.frames, "ms", "realtime frame interval");
                    window.cpuFrameTime = ResourcePerformanceMetrics.Summarize(cpu, cpuCount, "ms", "FrameTimingManager.cpuFrameTime");
                    if (window.cpuFrameTime.status == "unavailable")
                    {
                        for (int sample = 0; sample < cpuMain.Count; sample++) cpuMain.Values[sample] /= 1000000.0;
                        window.cpuFrameTime = ResourcePerformanceMetrics.Summarize(cpuMain.Values, cpuMain.Count, "ms",
                            "ProfilerRecorder.Main Thread duration; FrameTimingManager unavailable");
                    }
                    window.gpuFrameTime = ResourcePerformanceMetrics.Summarize(gpu, gpuCount, "ms", "FrameTimingManager.gpuFrameTime");
                    window.monoMemory = ResourcePerformanceMetrics.Summarize(mono, window.frames, "bytes", "Profiler.GetMonoUsedSizeLong");
                    window.totalMemory = ResourcePerformanceMetrics.Summarize(total, window.frames, "bytes", "Profiler.GetTotalAllocatedMemoryLong");
                    window.gcAlloc = gc.Finish(); window.batches = batches.Finish(); window.setPassCalls = setPass.Finish();
                    window.assetBundleLoads = ResourceBundleLoader.Snapshot();
                    foreach (var load in window.assetBundleLoads)
                    {
                        window.hadLoadCancellation |= load.cancelled;
                        window.hadLoadFailure |= load.failed;
                    }
                    bool complete = ResourcePerformanceMetrics.IsComplete(window.windowSeconds, window.frames, window.cancelled, !window.sceneChanged, window.overflow, out window.reason);
                    window.status = complete ? "complete" : "incomplete";
                    resourcePerformance.windows.Add(window);
                    resourcePerformance.runId = _runId;
                    WriteResourcePerformanceReport();
                    Record(caseId, complete ? "PASS" : "FAIL", (long)(window.windowSeconds * 1000),
                        "fps=" + window.fps.ToString("F2") + ";cpu=" + window.cpuFrameTime.status + ";gpu=" + window.gpuFrameTime.status,
                        window.reason ?? "measurement_complete_not_a_smoothness_claim");
                }
            }
        }

        private string AddResourcePerformanceManifest(string json)
        {
            int end = json.LastIndexOf('}');
            return json.Substring(0, end) + ",\n\"resourcePerformance\":" + JsonUtility.ToJson(resourcePerformance, true) + "\n}";
        }

        private string ResourcePerformanceSummary()
        {
            var text = new System.Text.StringBuilder("\n\n## Resource performance (read only)\n\n");
            foreach (var sample in resourcePerformance.windows)
                text.AppendLine("- " + sample.caseId + ": " + sample.status + ", " + sample.frames + " frames / " + sample.windowSeconds.ToString("F2")
                    + " s, FPS=" + sample.fps.ToString("F2") + ", CPU=" + sample.cpuFrameTime.status + ", GPU=" + sample.gpuFrameTime.status
                    + ". Full counters and load history: resource-performance.json.");
            return text.ToString();
        }

        private void WriteResourcePerformanceReport()
        {
            string directory = Path.Combine(Application.persistentDataPath, "BossRushTestReports", _runId);
            try
            {
                Directory.CreateDirectory(directory);
                string json = JsonUtility.ToJson(resourcePerformance, true);
                File.WriteAllText(Path.Combine(directory, "resource-performance.json"), json);
                if (_autotest != null && !string.IsNullOrEmpty(_autotest.RunDir)) WriteAutotestReport();
                else
                {
                    File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"read_only\":true,\"resourcePerformance\":" + json + "}");
                    File.WriteAllText(Path.Combine(directory, "summary.md"), ResourcePerformanceSummary());
                }
            }
            catch (Exception e) { _reportWriteFailed = true; UnityEngine.Debug.LogError("[BossRushValidation] Resource performance report: " + e.Message); }
        }
    }
}
#endif

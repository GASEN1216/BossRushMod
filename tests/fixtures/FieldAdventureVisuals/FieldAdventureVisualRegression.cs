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
using UnityEngine.SceneManagement;

namespace BossRush
{
    [InitializeOnLoad]
    public static class FieldAdventureVisualRegression
    {
        [Serializable] public sealed class Settings { public string modRoot, reportPath, outputDirectory; }
        public static Settings Configuration;
        private const string PendingKey = "BossRush.FieldVisualRegression.Pending";
        static FieldAdventureVisualRegression() { EditorApplication.playModeStateChanged += Entered; }
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            tags.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "DamageReceiver"; tags.ApplyModifiedPropertiesWithoutUndo();
            const string rendererPath = "Assets/FieldPreviewRenderer.asset", pipelinePath = "Assets/FieldPreviewPipeline.asset";
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, rendererPath); }
            ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
            renderer.renderingMode = RenderingMode.Deferred;
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, pipelinePath); }
            pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
            EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            SessionState.SetBool(PendingKey, true); EditorApplication.EnterPlaymode();
        }
        private static void Entered(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            Configuration = JsonUtility.FromJson<Settings>(File.ReadAllText(Path.Combine(Application.dataPath, "../visual-regression.json")));
            new GameObject("FieldAdventureVisualRegression").AddComponent<FieldAdventureVisualProbe>();
        }
    }

    public sealed class FieldAdventureVisualProbe : MonoBehaviour
    {
        [Serializable] public sealed class Sample
        {
            public string name, screenshot;
            public int renderers, solidRenderers, vertices, activePixels, magentaPixels, leases;
            public Vector3 dimensions;
            public float animationDelta;
            public bool deferred, particlesValid;
        }
        [Serializable] public sealed class Report
        {
            public bool passed, playMode, cleanup, constructorFailureCleanup, inactiveOwnerCleanup, hudGate, leaseReacquire, tongueReach, parcelAudioSettings;
            public string unityVersion, graphicsDevice, scope;
            public List<Sample> samples = new List<Sample>();
            public FieldAdventureMimicCollisionRegression.Result mimicCollision;
            public List<string> errors = new List<string>(), failures = new List<string>();
        }
        private readonly Report report = new Report();
        private readonly List<GameObject> owned = new List<GameObject>();
        private Camera camera;
        private RenderTexture target;
        private GameObject ground;
        private GreedyMimicVisual mimic;
        private OddParcelVisual egg, soda, stationA, stationB;
        private DangerousPrototypeVisuals prototype;
        private GameObject stationBench;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            report.playMode = Application.isPlaying; report.unityVersion = Application.unityVersion; report.graphicsDevice = SystemInfo.graphicsDeviceName;
            report.scope = "Real production Visual classes, material bundle, URP Deferred, native particles and object lifecycle. Host character/UI/fog are stubs; this is not game-process validation.";
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            Execute(Setup);
            yield return null;
            Execute(MimicCreate);
            yield return null;
            Execute(MimicCapture);
            Execute(MimicTongueReach);
            Execute(ClearSubjects);
            yield return null;
            Execute(() => Require(FieldAdventureVisualAssets.ActiveLeaseCount == 0, "mimic lease remained after Dispose"));
            Execute(() => report.mimicCollision = FieldAdventureMimicCollisionRegression.Run(Require));
            yield return null;
            Execute(() => Require(FieldAdventureVisualAssets.ActiveLeaseCount == 0, "native mimic collision encounter leaked its visual lease"));
            Execute(EggCreate);
            yield return null;
            Execute(EggCapture);
            Execute(ParcelAudioSettings);
            Execute(ClearSubjects);
            yield return null;
            Execute(SodaCreate);
            yield return null;
            Execute(SodaCapture);
            Execute(ClearSubjects);
            yield return null;
            Execute(StationCreate);
            yield return null;
            Execute(StationCapture);
            Execute(ClearSubjects);
            yield return null;
            Execute(() => PrototypeCreate(DangerousPrototypeKind.JetCannon));
            yield return null;
            Execute(() => PrototypeCapture("05-jet-cannon", true));
            Execute(ClearSubjects);
            yield return null;
            Execute(() => PrototypeCreate(DangerousPrototypeKind.RicochetSun));
            yield return null;
            Execute(() => PrototypeCapture("06-ricochet-sun", false));
            Execute(ClearSubjects);
            yield return null;
            Execute(FailureAndLeaseCase);
            yield return null;
            Execute(() =>
            {
                report.constructorFailureCleanup = FieldAdventureVisualAssets.ActiveLeaseCount == 0;
                Require(report.constructorFailureCleanup, "failed constructor leaked its material lease");
                GameObject inactive = new GameObject("NeverActivatedParent"); inactive.SetActive(false);
                var unopened = new OddParcelVisual(inactive, OddParcelKind.QuackingEgg, false, false);
                Require(FieldAdventureVisualAssets.ActiveLeaseCount == 1, "inactive parcel did not acquire lease");
                unopened.Dispose(); unopened.Dispose();
                report.inactiveOwnerCleanup = FieldAdventureVisualAssets.ActiveLeaseCount == 0;
                Require(report.inactiveOwnerCleanup, "never-active parcel Dispose leaked material lease");
                Destroy(inactive);
                GameObject first = new GameObject("LeaseReacquire");
                Material one = FieldAdventureVisualAssets.Acquire(first), same = FieldAdventureVisualAssets.Acquire(first);
                Require(one == same && FieldAdventureVisualAssets.ActiveLeaseCount == 1, "repeated owner acquisition duplicated lease");
                GameObject second = new GameObject("SecondLease"); FieldAdventureVisualAssets.Acquire(second);
                report.leaseReacquire = FieldAdventureVisualAssets.ActiveLeaseCount == 2;
                Destroy(first); Destroy(second);
            });
            yield return null;
            report.cleanup = FieldAdventureVisualAssets.ActiveLeaseCount == 0;
            Require(report.cleanup && report.leaseReacquire, "final visual lease lifecycle incomplete");
            Require(report.samples.Count == 6, "six production previews required");
            Finish();
        }

        private void Setup()
        {
            Require(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "prepared URP pipeline missing on play-mode entry");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.42f, .48f, .57f); RenderSettings.ambientEquatorColor = new Color(.26f, .29f, .35f); RenderSettings.ambientGroundColor = new Color(.12f, .10f, .08f);
            var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(new Color(.55f, .58f, .62f)); RenderSettings.ambientProbe = probe;
            var sun = new GameObject("WarmKey").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 2.2f; sun.color = new Color(1f, .86f, .68f); sun.transform.rotation = Quaternion.Euler(44f, -35f, 0f);
            var fill = new GameObject("CoolFill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .6f; fill.color = new Color(.55f, .77f, 1f); fill.transform.rotation = Quaternion.Euler(20f, 145f, 0f);
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "PreviewGround"; ground.transform.position = new Vector3(0, -.17f, 0); ground.transform.localScale = new Vector3(60, .3f, 60);
            var floor = new Material(Shader.Find("Universal Render Pipeline/Lit")); floor.SetColor("_BaseColor", new Color(.12f, .15f, .19f)); floor.SetFloat("_Smoothness", .05f); ground.GetComponent<Renderer>().sharedMaterial = floor;
            camera = new GameObject("PreviewCamera").AddComponent<Camera>(); camera.orthographic = true; camera.backgroundColor = new Color(.035f, .045f, .065f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true; camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var volume = new GameObject("PreviewBloom").AddComponent<Volume>(); volume.isGlobal = true; volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = volume.profile.Add<Bloom>(); bloom.intensity.Override(.3f); bloom.threshold.Override(1.1f);
            var tone = volume.profile.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            target = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGBHalf); target.antiAliasing = 1; camera.targetTexture = target;
            Directory.CreateDirectory(FieldAdventureVisualRegression.Configuration.outputDirectory);
        }
        private GameObject Anchor(string name, Vector3 position)
        { var anchor = new GameObject(name); anchor.transform.position = position; owned.Add(anchor); return anchor; }
        private void MimicCreate()
        {
            mimic = new GreedyMimicVisual(SceneManager.GetActiveScene(), Vector3.zero); owned.Add(mimic.Root);
            var rules = new GreedyMimicRules(); rules.Feed(GreedyMimicFood.Ammunition); rules.Feed(GreedyMimicFood.Explosive); rules.Feed(GreedyMimicFood.Medicine); mimic.Grow(rules);
        }
        private void MimicCapture()
        {
            float before = Fingerprint(owned);
            for (int i = 0; i < 45; i++) mimic.Tick(1f / 60, new Vector3(0, 0, 10), true, .72f, 1.2f, GreedyMimicAttack.Bombs);
            float animation = Mathf.Abs(Fingerprint(owned) - before);
            mimic.ShowCircle(0, new Vector3(3.5f, 0, 1.5f), 2.8f, false); mimic.AnimateBomb(0, Vector3.up * 3f, new Vector3(3.5f, 0, 1.5f), .45f);
            Transform hud = mimic.Body.Find("WorldHealth");
            BossRushUI.HudHidden = true; mimic.SyncHudVisibility(); bool hidden = !hud.gameObject.activeSelf;
            BossRushUI.HudHidden = false; BossRushUI.Paused = true; mimic.SyncHudVisibility(); hidden &= !hud.gameObject.activeSelf;
            BossRushUI.Paused = false; mimic.SyncHudVisibility(); report.hudGate = hidden && hud.gameObject.activeSelf; Require(report.hudGate, "world health bar bypassed HUD/pause gates");
            Capture("01-greedy-mimic-three-meals", new Vector3(0, 2.4f, 0), 6.2f, animation);
        }
        private void EggCreate() { egg = new OddParcelVisual(Anchor("Egg", Vector3.zero), OddParcelKind.QuackingEgg, false, false); }
        private void MimicTongueReach()
        {
            Transform[] segments = (Transform[])typeof(GreedyMimicVisual).GetField("tongue", PrivateInstance).GetValue(mimic);
            Require(segments.Length == 12, "full-range tongue requires twelve independent fog samples");
            foreach (Vector3 targetPosition in new[] { new Vector3(0, 0, 31.99f), new Vector3(0, 0, 32.01f), new Vector3(40, 0, 0), new Vector3(-27, 0, 36) })
            {
                mimic.StrikeTongue(targetPosition);
                Vector3 previous = mimic.Root.transform.position + Vector3.up * 1.6f;
                foreach (Transform segment in segments)
                {
                    Vector3 start = segment.TransformPoint(Vector3.down), end = segment.TransformPoint(Vector3.up);
                    Require(segment.gameObject.activeSelf && Vector3.Distance(start, previous) < .001f, "tongue segments must form a continuous visible strike");
                    Require(Vector3.Distance(start, end) <= 4f, "tongue fog sampling segment exceeds four metres");
                    Transform sample;
                    Require(ModEntityFogVisibility.Samples.TryGetValue(segment.gameObject, out sample) && sample == segment, "each tongue segment needs its own moving fog sample");
                    previous = end;
                }
                Require(Vector3.Distance(previous, targetPosition + Vector3.up * .4f) < .001f, "tongue tip must reach the actual locked attack target beyond 32 metres");
                mimic.ClearAttack();
                foreach (Transform segment in segments) Require(!segment.gameObject.activeSelf, "clearing attack must hide every tongue segment");
            }
            report.tongueReach = true;
        }
        private void EggCapture()
        {
            float before = Fingerprint(owned); egg.Burst(false);
            for (int i = 0; i < 25; i++) egg.Tick(1f / 60, 82f, false);
            Capture("02-quacking-egg", new Vector3(0, 2.1f, 0), 3.5f, Mathf.Abs(Fingerprint(owned) - before));
        }
        private void SodaCreate() { soda = new OddParcelVisual(Anchor("Soda", Vector3.zero), OddParcelKind.AngrySoda, false, false); }
        private void ParcelAudioSettings()
        {
            AudioSource voice = (AudioSource)typeof(OddParcelVisual).GetField("voice", PrivateInstance).GetValue(egg);
            Duckov.AudioManager.Bus master = Duckov.AudioManager.GetBus("Master"), sfx = Duckov.AudioManager.GetBus("Master/SFX");
            try
            {
                master.Volume = .4f; sfx.Volume = .5f; egg.SyncVoiceVolume();
                Require(Mathf.Abs(voice.volume - .14f) < .0001f, "parcel Unity volume must multiply both official bus levels");
                master.Volume = 0f; egg.SyncVoiceVolume(); Require(voice.volume == 0f, "zero master volume must silence parcel");
                master.Volume = 1f; sfx.Volume = 0f; egg.SyncVoiceVolume(); Require(voice.volume == 0f, "zero SFX volume must silence parcel");
                sfx.Volume = 1f; master.Mute = true; egg.SyncVoiceVolume(); Require(voice.volume == 0f, "master mute must silence parcel");
                master.Mute = false; sfx.Mute = true; egg.SyncVoiceVolume(); Require(voice.volume == 0f, "SFX mute must silence parcel");
                sfx.Mute = false; egg.SyncVoiceVolume(); Require(Mathf.Abs(voice.volume - .7f) < .0001f, "unmuting must restore parcel level");
                report.parcelAudioSettings = true;
            }
            finally { master.Volume = sfx.Volume = 1f; master.Mute = sfx.Mute = false; }
        }
        private void SodaCapture()
        {
            float before = Fingerprint(owned); soda.Burst(false);
            for (int i = 0; i < 21; i++) soda.Tick(1f / 60, 92f, false);
            Capture("03-angry-soda", new Vector3(0, 2.2f, 0), 3.7f, Mathf.Abs(Fingerprint(owned) - before));
        }
        private void StationCreate()
        {
            stationA = new OddParcelVisual(Anchor("Dispatch", new Vector3(-3, 0, 0)), OddParcelKind.QuackingEgg, true, false);
            stationB = new OddParcelVisual(Anchor("Receipt", new Vector3(0, 0, 0)), OddParcelKind.QuackingEgg, true, true);
            stationBench = DangerousPrototypeVisuals.CreateStation(new Vector3(3.6f, 0, 0)); owned.Add(stationBench);
        }
        private void StationCapture()
        {
            float before = Fingerprint(owned); stationB.Burst(true);
            for (int i = 0; i < 30; i++) { stationA.Tick(1f / 60, 0, false); stationB.Tick(1f / 60, 0, false); }
            Capture("04-field-stations", new Vector3(.4f, 1.5f, 0), 6.7f, Mathf.Abs(Fingerprint(owned) - before));
        }
        private void PrototypeCreate(DangerousPrototypeKind kind)
        {
            GameObject host = Anchor("PrototypeHost", new Vector3(-1.7f, 1.2f, 0));
            CharacterMainControl character = host.AddComponent<CharacterMainControl>();
            prototype = new DangerousPrototypeVisuals(host.transform, character, kind);
        }
        private void PrototypeCapture(string name, bool jet)
        {
            float before = Fingerprint(owned); prototype.Fire();
            prototype.Beam(new Vector3(-1.7f, 1.2f, 1.2f), new Vector3(1.3f, 1.2f, 3.7f), jet, jet ? 0 : 1);
            prototype.Impact(new Vector3(1.3f, 1.2f, 3.7f), Vector3.back, 1);
            for (int i = 0; i < 8; i++) prototype.Tick(1f / 60, .9f);
            // 世界弹道由生产 Visual 自己持有，不挂枪；作为同一预览主体计入像素与清理检测。
            foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name == "DangerousPrototype_BlastSegment" && !owned.Contains(go)) owned.Add(go);
            Capture(name, new Vector3(-.4f, 1.2f, 1.7f), 3.6f, Mathf.Abs(Fingerprint(owned) - before));
        }
        private void FailureAndLeaseCase()
        {
            GameObject host = Anchor("FailedVisualOwner", Vector3.zero); bool threw = false;
            try { new DangerousPrototypeVisuals(host.transform, null, DangerousPrototypeKind.JetCannon); }
            catch (NullReferenceException) { threw = true; }
            Require(threw, "constructor failure path did not execute"); Destroy(host); owned.Clear();
        }
        private void Capture(string name, Vector3 focus, float extent, float animation)
        {
            camera.orthographicSize = extent; camera.transform.position = focus + new Vector3(8f, 7f, 11f); camera.transform.LookAt(focus);
            Physics.SyncTransforms();
            var sample = new Sample { name = name, leases = FieldAdventureVisualAssets.ActiveLeaseCount, animationDelta = animation, particlesValid = true };
            bool hasBounds = false; Bounds bounds = new Bounds(); var allRenderers = new List<Renderer>();
            foreach (GameObject root in owned)
            {
                if (root == null) continue;
                foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.sharedMesh != null) sample.vertices += mesh.sharedMesh.vertexCount;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (allRenderers.Contains(renderer)) continue; allRenderers.Add(renderer); sample.renderers++;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Require(material != null && material.shader != null && material.shader.isSupported, name + ": missing or unsupported material");
                        if (material != null && material.shader != null && material.shader.name == "BossRush/FieldAdventures/Solid")
                        {
                            sample.solidRenderers++; Require(material.FindPass("GBuffer") >= 0 && material.GetFloat("_ZWrite") > .5f && material.GetFloat("_Surface") < .5f, name + ": opaque GBuffer contract");
                            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
                        }
                    }
                }
                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Simulate(.22f, false, false, false);
                    var velocity = ps.velocityOverLifetime;
                    sample.particlesValid &= !velocity.enabled || (velocity.x.mode == velocity.y.mode && velocity.y.mode == velocity.z.mode);
                }
            }
            sample.dimensions = bounds.size;
            Color32[] rendered = Pixels();
            ScriptableRenderer actualRenderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
            PropertyInfo actualMode = actualRenderer.GetType().GetProperty("renderingModeActual", PrivateInstance);
            sample.deferred = actualMode != null && (RenderingMode)actualMode.GetValue(actualRenderer, null) == RenderingMode.Deferred;
            Require(sample.deferred, name + ": actual renderer fell back from Deferred");
            var originalEnabled = new bool[allRenderers.Count];
            for (int i = 0; i < allRenderers.Count; i++) { originalEnabled[i] = allRenderers[i].enabled; allRenderers[i].enabled = false; }
            Color32[] empty = Pixels();
            for (int i = 0; i < allRenderers.Count; i++) allRenderers[i].enabled = originalEnabled[i];
            for (int i = 0; i < rendered.Length; i++)
            {
                Color32 a = rendered[i], b = empty[i];
                if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 55) sample.activePixels++;
                if (a.r > 185 && a.b > 185 && a.g < 65) sample.magentaPixels++;
            }
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false); texture.SetPixels32(rendered); texture.Apply();
            sample.screenshot = Path.Combine(FieldAdventureVisualRegression.Configuration.outputDirectory, name + ".png"); File.WriteAllBytes(sample.screenshot, texture.EncodeToPNG()); Destroy(texture);
            Require(sample.solidRenderers >= 5 && sample.vertices > 200, name + ": physical 3D geometry missing");
            Require(sample.dimensions.x > .5f && sample.dimensions.y > .35f && sample.dimensions.z > .35f, name + ": model volume is degenerate");
            Require(sample.animationDelta > .01f, name + ": production animation did not change any transforms");
            Require(sample.activePixels > 3000, name + ": model did not contribute visible pixels");
            Require(sample.magentaPixels < 30, name + ": shader-error pink pixels");
            Require(sample.particlesValid, name + ": particle velocity curve modes disagree");
            report.samples.Add(sample);
        }
        private Color32[] Pixels()
        {
            camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); Color32[] pixels = texture.GetPixels32(); Destroy(texture); RenderTexture.active = null; return pixels;
        }
        private static float Fingerprint(List<GameObject> roots)
        {
            float sum = 0f;
            foreach (GameObject root in roots) if (root != null)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    sum += t.localPosition.x * .13f + t.localPosition.y * .37f + t.localPosition.z * .53f + t.localRotation.x * .71f + t.localRotation.y * .79f + t.localRotation.z * .89f + t.localScale.sqrMagnitude;
            return sum;
        }
        private void ClearSubjects()
        {
            if (mimic != null) mimic.Dispose(); mimic = null;
            if (egg != null) egg.Dispose(); egg = null;
            if (soda != null) soda.Dispose(); soda = null;
            if (stationA != null) stationA.Dispose(); stationA = null;
            if (stationB != null) stationB.Dispose(); stationB = null;
            if (prototype != null) prototype.Dispose(); prototype = null;
            foreach (GameObject root in owned) if (root != null) { root.SetActive(false); Destroy(root); }
            owned.Clear(); stationBench = null;
            ModEntityFogVisibility.Samples.Clear();
        }
        private void Execute(Action action) { try { action(); } catch (Exception e) { report.failures.Add(e.ToString()); } }
        private void Require(bool condition, string failure) { if (!condition) report.failures.Add(failure); }
        private void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
        private void Finish()
        {
            report.passed = report.failures.Count == 0 && report.errors.Count == 0 && report.playMode;
            File.WriteAllText(FieldAdventureVisualRegression.Configuration.reportPath, JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= OnLog;
            Debug.Log("FIELD_ADVENTURE_VISUALS " + (report.passed ? "PASS" : "FAIL") + " samples=" + report.samples.Count + " errors=" + report.errors.Count + " leases=" + FieldAdventureVisualAssets.ActiveLeaseCount);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
#endif

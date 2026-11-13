using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Duckov.Weathers;
using SodaCraft;
using Umbra;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BossRush
{
    /// <summary>把原版场景的方向光交给出击地图的官方天气系统，并提供自研材质所需的只读光照量。</summary>
    internal sealed class RaidOfficialLighting : IDisposable
    {
        private static readonly FieldInfo TimePhases =
            typeof(TimeOfDayEntry).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LightControlTarget =
            typeof(LightControl).GetField("light", BindingFlags.Static | BindingFlags.NonPublic);
        private const string IndoorWeather = "BaseVolume";
        private const string ClearDay = "Volume_Level_GroundZero_Morning";
        private const string ClearDawn = "Volume_Level_GroundZero_AfterNoon";
        private const string ClearNight = "Volume_Level_GroundZero_ Night";
        private const string OfficialShadows = "SodaUmbraShadowProfile";

        private GameObject staging;
        private Light sun;
        private UmbraProfile shadowProfile;
        private AmbientMode ambientMode;
        private Scene raidScene;
        private Light previousSun;
        private AmbientMode previousAmbientMode;
        private bool bound, subscribed;

        internal void Prepare(Scene sourceScene)
        {
            if (!sourceScene.IsValid() || !sourceScene.isLoaded)
                throw new InvalidOperationException("官方日光来源场景未加载");

            Light source = FindSource(sourceScene);
            if (source == null)
                throw new InvalidOperationException("找不到唯一可复用的官方方向光：" + sourceScene.name);
            // 仓库子场景使用 Trilight。新地图包的默认环境模式不一定会使用 LightControl 写入的三色天光。
            Prepare(source, SceneManager.GetActiveScene() == sourceScene ? RenderSettings.ambientMode : AmbientMode.Trilight);
        }

        internal void PrepareFromTemplate(RaidOfficialLighting template)
        {
            if (template == null || template.staging == null || template.sun == null)
                throw new InvalidOperationException("官方日光模板尚未准备");
            Prepare(template.sun, template.ambientMode);
        }

        internal void PrepareNative()
        {
            EnsureUnprepared();
            try
            {
                ambientMode = AmbientMode.Trilight;
                CreateStaging();
                // 直接进入下水道时，基地室外子场景的方向光从未加载。先提供官方 URP 可用的主光，
                // 由原版 LightControl 首次计算其光色、强度与方向。
                GameObject lightObject = new GameObject("BossRushOfficialSun");
                lightObject.SetActive(false);
                lightObject.transform.SetParent(staging.transform, false);
                sun = lightObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                lightObject.AddComponent<UniversalAdditionalLightData>();
                UmbraSoftShadows shadows = lightObject.AddComponent<UmbraSoftShadows>();
                foreach (UmbraProfile profile in Resources.FindObjectsOfTypeAll<UmbraProfile>())
                {
                    if (profile == null || profile.name != OfficialShadows) continue;
                    shadowProfile = Object.Instantiate(profile);
                    break;
                }
                if (shadowProfile == null) shadowProfile = ScriptableObject.CreateInstance<UmbraProfile>();
                shadows.profile = shadowProfile;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Prepare(Light source, AmbientMode mode)
        {
            EnsureUnprepared();
            try
            {
                ambientMode = mode;
                CreateStaging();
                sun = Object.Instantiate(source, staging.transform, true);
                sun.gameObject.SetActive(false);
                if (sun.bakingOutput.isBaked)
                {
                    // 基地的官方主光参加了原场景烘焙。副本没有那些贴图与探针，交给实时渲染。
                    sun.bakingOutput = new LightBakingOutput
                    {
                        lightmapBakeType = LightmapBakeType.Realtime,
                        probeOcclusionLightIndex = -1,
                        occlusionMaskChannel = -1
                    };
                }
                UmbraSoftShadows shadows = sun.GetComponent<UmbraSoftShadows>();
                if (shadows != null && shadows.profile != null)
                {
                    // 官方 LightControl 在天气混合时写 contactStrength。只让它写本次出击的副本。
                    shadowProfile = Object.Instantiate(shadows.profile);
                    shadows.profile = shadowProfile;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void EnsureUnprepared()
        {
            if (staging != null || sun != null)
                throw new InvalidOperationException("官方日光已准备");
        }

        private void CreateStaging()
        {
            // 官方加载器先用 Single 加载幕布。用停用的根跨场景持有，避免 Umbra 提前注册。
            staging = new GameObject("BossRushOfficialSunStaging");
            staging.SetActive(false);
            Object.DontDestroyOnLoad(staging);
        }

        internal static void ApplyOutdoorWeather(TimeOfDayConfig clone)
        {
            if (clone == null) throw new ArgumentNullException(nameof(clone));
            if (TimePhases == null) throw new MissingFieldException(nameof(TimeOfDayEntry), "phases");
            TimeOfDayEntry sunny = clone.GetCurrentEntry(Weather.Sunny);
            var phases = sunny == null ? null : TimePhases.GetValue(sunny) as List<TimeOfDayPhase>;
            if (phases == null) throw new InvalidOperationException("官方晴天时段缺失");
            TimeOfDayEntry rainy = clone.GetCurrentEntry(Weather.Rainy);
            if (rainy == null) throw new InvalidOperationException("官方户外雨天时段缺失");

            VolumeProfile day = null, dawn = null, night = null;
            foreach (VolumeProfile profile in Resources.FindObjectsOfTypeAll<VolumeProfile>())
            {
                if (profile == null) continue;
                switch (profile.name)
                {
                    case ClearDay: day = profile; break;
                    case ClearDawn: dawn = profile; break;
                    case ClearNight: night = profile; break;
                }
            }

            for (int i = 0; i < phases.Count; i++)
            {
                TimeOfDayPhase phase = phases[i];
                if (phase.volumeProfile == null || phase.volumeProfile.name != IndoorWeather) continue;
                VolumeProfile outdoor;
                switch (phase.timePhaseTag)
                {
                    case TimePhaseTags.day: outdoor = day; break;
                    case TimePhaseTags.dawn: outdoor = dawn; break;
                    case TimePhaseTags.night: outdoor = night; break;
                    default: continue;
                }
                // 基地依赖晴天白昼与雨天的零号区 Volume。晴天黄昏、夜间的资源可能尚未加载，
                // 此时用基地雨天条目已有的同一户外时段，天气判定仍由原版控制器负责。
                if (outdoor == null)
                {
                    outdoor = rainy.GetPhase(phase.timePhaseTag).volumeProfile;
                    Debug.LogWarning("[Tidegate] 官方户外晴天资源未加载，使用同一时段的零号区雨天 Volume：" + phase.timePhaseTag);
                }
                if (outdoor == null || outdoor.name == IndoorWeather)
                    throw new InvalidOperationException("官方户外天气资源缺失：" + phase.timePhaseTag);
                phase.volumeProfile = outdoor;
                phases[i] = phase;
            }
        }

        private static Light FindSource(Scene sourceScene)
        {
            Light configured = RenderSettings.sun;
            if (IsSource(configured) && configured.gameObject.scene == sourceScene) return configured;

            Light found = null;
            foreach (GameObject root in sourceScene.GetRootGameObjects())
            {
                foreach (Light candidate in root.GetComponentsInChildren<Light>())
                {
                    if (!IsSource(candidate) || candidate.gameObject.name != "Directional Light") continue;
                    if (found != null) return null;
                    found = candidate;
                }
            }
            return found;
        }

        private static bool IsSource(Light light)
        {
            if (!IsActiveDirectional(light) || light.transform.childCount != 0 ||
                light.GetComponent<UniversalAdditionalLightData>() == null)
                return false;
            foreach (Component component in light.GetComponents<Component>())
            {
                if (component is Transform || component is Light || component is UniversalAdditionalLightData ||
                    component is UmbraSoftShadows) continue;
                if (component == null) return false;
                Type type = component.GetType();
                // 官方仓库太阳还带一个 Bakery 烘焙数据组件，游戏版没有运行时启停逻辑。
                if (type.FullName != "BakeryDirectLight" || type.Assembly.GetName().Name != "BakeryRuntimeAssembly")
                    return false;
            }
            return true;
        }

        internal void Apply(GameObject root)
        {
            if (sun == null || staging == null || root == null || !root.scene.isLoaded)
                throw new InvalidOperationException("官方日光尚未准备或出击场景未加载");
            try
            {
                raidScene = root.scene;
                sun.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(sun.gameObject, raidScene);
                // 方向光留在场景顶层，独立于玩法世界根的显示状态，卸载场景时随之销毁。
                Object.Destroy(staging);
                staging = null;

                // sceneLoaded 发生在官方 SceneLoader 的 SetActiveScene 之前。RenderSettings 按活动场景取值。
                SceneManager.activeSceneChanged += OnActiveSceneChanged;
                subscribed = true;
                BindIfActive(SceneManager.GetActiveScene());
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            try { BindIfActive(current); }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void BindIfActive(Scene current)
        {
            if (bound || sun == null || current != raidScene) return;
            previousSun = RenderSettings.sun;
            previousAmbientMode = RenderSettings.ambientMode;
            bound = true;
            if (!IsActiveDirectional(previousSun) || previousSun.gameObject.scene != raidScene)
            {
                RenderSettings.sun = sun;
                sun.gameObject.SetActive(true);
                RenderSettings.ambientMode = ambientMode;
            }
            Unsubscribe();
        }

        internal static bool TryRead(Scene scene, out Vector3 direction, out Color sunlight, out Color ambient)
        {
            direction = default(Vector3);
            sunlight = default(Color);
            ambient = default(Color);
            if (!scene.isLoaded || SceneManager.GetActiveScene() != scene) return false;
            Light current = RenderSettings.sun;
            if (!IsActiveDirectional(current) || current.gameObject.scene != scene) return false;

            direction = -current.transform.forward;
            // URP 的 GetMainLight().color 是线性光色乘以光强。地图着色器的备用分支也用这个量。
            Color lightColor = current.color;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) lightColor = lightColor.linear;
            sunlight = lightColor * current.intensity;
            ambient = RenderSettings.ambientEquatorColor;
            return true;
        }

        /// <summary>只读诊断。记录当前官方天气目标和最近一帧实际写入的渲染值。</summary>
        internal static string Describe(Scene scene)
        {
            if (!scene.isLoaded || SceneManager.GetActiveScene() != scene) return "lighting=inactive_scene";
            try
            {
                TimeOfDayController controller = TimeOfDayController.Instance;
                string weather = "weather_controller=missing";
                if (controller != null)
                {
                    TimeOfDayPhase phase = controller.CurrentPhase;
                    weather = "hour=" + Number(controller.Time) + " weather=" + controller.CurrentWeather +
                        " phase=" + phase.timePhaseTag + " target_volume=" + (phase.volumeProfile == null ? "-" : phase.volumeProfile.name);
                }

                VolumeStack stack = VolumeManager.instance.stack;
                LightControl lightControl = stack == null ? null : stack.GetComponent<LightControl>();
                SunFogTD fog = stack == null ? null : stack.GetComponent<SunFogTD>();
                Light sun = RenderSettings.sun;
                string light = "sun=missing";
                if (sun != null)
                    light = "sun=" + sun.gameObject.name + " sun_scene=" + sun.gameObject.scene.name +
                        " sun_active=" + sun.isActiveAndEnabled + " sun_type=" + sun.type + " sun_intensity=" +
                        Number(sun.intensity) + " sun_rgb=" + Rgb(sun.color) + " sun_forward=" + sun.transform.forward +
                        " sun_shadow=" + sun.shadows;
                return weather + " " + light + " stack_light=" + (lightControl != null && lightControl.IsActive()) +
                    " stack_sun_intensity=" + (lightControl == null ? "-" : Number(lightControl.sunIntensity.value)) +
                    " light_control_target=" + DescribeLightControlTarget(sun) +
                    " color_space=" + QualitySettings.activeColorSpace + " ambient_mode=" + RenderSettings.ambientMode +
                    " ambient_sky=" + Rgb(RenderSettings.ambientSkyColor) + " ambient_equator=" + Rgb(RenderSettings.ambientEquatorColor) +
                    " ambient_ground=" + Rgb(RenderSettings.ambientGroundColor) + " unity_fog=" + RenderSettings.fog +
                    " stack_fog=" + (fog != null && fog.IsActive()) + " fog_overlay=" + DescribeFogOverlay() +
                    " fog_near=" + Number(Shader.GetGlobalFloat("SunFogNearDistance")) +
                    " fog_far=" + Number(Shader.GetGlobalFloat("SunFogFarDistance")) + " fog_rgb=" + Rgb(Shader.GetGlobalColor("SunFogColor"));
            }
            catch (Exception e)
            {
                return "lighting=read_failed error=" + e.GetType().Name;
            }
        }

        private static string DescribeLightControlTarget(Light sun)
        {
            try
            {
                if (LightControlTarget == null) return "unavailable";
                Light target = LightControlTarget.GetValue(null) as Light;
                if (target == null) return "missing";
                return target == sun ? "scene_sun" : "other@" + target.gameObject.scene.name;
            }
            catch { return "unavailable"; }
        }

        private static string DescribeFogOverlay()
        {
            try
            {
                GameCamera gameCamera = GameCamera.Instance;
                Camera camera = gameCamera == null ? null : gameCamera.renderCamera;
                SunFogEntry overlay = camera == null ? null : camera.GetComponentInChildren<SunFogEntry>(true);
                if (overlay == null) return "missing";
                Renderer renderer = overlay.GetComponent<Renderer>();
                return overlay.gameObject.activeInHierarchy && renderer != null && renderer.enabled ? "on" : "off";
            }
            catch { return "unavailable"; }
        }

        private static string Number(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Rgb(Color color)
        {
            return "(" + Number(color.r) + "," + Number(color.g) + "," + Number(color.b) + ")";
        }

        private static bool IsActiveDirectional(Light light)
        {
            return light != null && light.type == LightType.Directional && light.isActiveAndEnabled;
        }

        public void Dispose()
        {
            Unsubscribe();
            if (bound && sun != null && SceneManager.GetActiveScene() == raidScene && RenderSettings.sun == sun)
            {
                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = previousAmbientMode;
            }
            bound = false;
            previousSun = null;
            if (sun != null)
            {
                sun.gameObject.SetActive(false);
                Object.Destroy(sun.gameObject);
                sun = null;
            }
            if (shadowProfile != null)
            {
                Object.Destroy(shadowProfile);
                shadowProfile = null;
            }
            if (staging != null)
            {
                Object.Destroy(staging);
                staging = null;
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            subscribed = false;
        }
    }
}

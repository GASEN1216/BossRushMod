using System;
using SodaCraft;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>独立场景光照 owner；自动跟随官方时钟，导览牌可暂时锁定四档光色。</summary>
    internal sealed class SkyIslandLighting : IDisposable
    {
        private const string Enabled = "_SkyIslandLightingEnabled";
        private const string Direction = "_SkyIslandSunDirection";
        private const string Sun = "_SkyIslandSunColor";
        private const string Ambient = "_SkyIslandAmbientColor";
        private const string Owner = "_SkyIslandLightingOwner";
        private float previousEnabled;
        private float previousOwner, ownerId;
        private Vector4 previousDirection, previousSun, previousAmbient;
        private bool acquired;
        private Light ownSun;
        private Light previousSceneSun;
        private bool previousSunEnabled;
        private Color previousSky, previousEquator, previousGround;
        private AmbientMode previousAmbientMode;
        private Scene scene;
        private GameObject volumeObject;
        private VolumeProfile profile;
        private LightControl lightControl;
        private int presetIndex = -1;
        private int automaticIndex;
        // 上一次真正写下去的四个量，见 Tick() 的变化阈值。dirty 保证首次与切档时必写。
        private bool dirty = true;
        private Color appliedSun, appliedAmbient;
        private float appliedIntensity;
        private Quaternion appliedRotation;

        /// <summary>
        /// 写入阈值。取值都在感知阈以下：0.002 的颜色分量约合 8 位色的 0.5 级，
        /// 0.05° 的太阳角在最快的黄昏过渡里也不到一帧的变化量（实测约 0.0044°/帧 @60fps）。
        /// 目的只是把「锁定预设时结果恒定却每帧照写」和「自动档每帧写入不可见的增量」两种
        /// 无效写入去掉：`RenderSettings.ambient*Color` 在 Trilight 下每次赋值都会让 Unity
        /// 重算环境球，属于 AGENTS 4.12 说的每帧重工作。
        /// </summary>
        private const float ColorEpsilon = 0.002f;
        private const float IntensityEpsilon = 0.002f;
        private const float RotationEpsilonDegrees = 0.05f;

        // 与 Unity 作者预览共用的四档调色；只读取官方 GameClock，不改游戏时间。
        // 底色改为原版鸭科夫的暖琥珀后，奇幻感改由这里承担：环境光压暗并推冷、主光提亮并推暖，
        // 形成原版截图里那种「暖光 + 深冷阴影」的高对比；太阳高度角一并放低，让物体拉出长影出体积。
        // 环境光亮度约为旧值的 0.65 倍，主光强度约 1.2 倍，整体曝光基本持平而对比度显著提高。
        // 夜档把环境光压得最狠，是为了让灯笼、晶簇和月光蘑菇的自发光成为夜里的主角。
        private static readonly Preset[] Presets =
        {
            new Preset("晴昼", "Daylight", new Color(1.00f, 0.91f, 0.72f), new Color(0.24f, 0.30f, 0.40f), new Vector3(48, -35, 0), 2.15f),
            new Preset("暮色", "Dusk", new Color(1f, 0.56f, 0.28f), new Color(0.21f, 0.18f, 0.30f), new Vector3(19, -65, 0), 1.30f),
            new Preset("星夜", "Starlight", new Color(0.26f, 0.37f, 0.58f), new Color(0.09f, 0.13f, 0.24f), new Vector3(38, 135, 0), 0.42f),
            new Preset("晨光", "Morning", new Color(1.00f, 0.82f, 0.62f), new Color(0.26f, 0.33f, 0.45f), new Vector3(17, 60, 0), 1.55f)
        };

        internal string PresetName
        {
            get { return presetIndex < 0 ? L10n.T("自动 · ", "Auto · ") + Presets[automaticIndex].Name : Presets[presetIndex].Name; }
        }

        internal void Apply(GameObject root)
        {
            if (acquired) throw new InvalidOperationException("天空岛光照租约已经启用");
            if (root == null || !root.scene.isLoaded || SceneManager.GetActiveScene().handle != root.scene.handle)
                throw new InvalidOperationException("天空岛光照必须在自己的活动 Scene 创建");
            scene = root.scene;
            previousEnabled = Shader.GetGlobalFloat(Enabled);
            previousOwner = Shader.GetGlobalFloat(Owner);
            previousDirection = Shader.GetGlobalVector(Direction);
            previousSun = Shader.GetGlobalVector(Sun);
            previousAmbient = Shader.GetGlobalVector(Ambient);
            previousSceneSun = RenderSettings.sun;
            previousSunEnabled = previousSceneSun != null && previousSceneSun.enabled;
            previousSky = RenderSettings.ambientSkyColor;
            previousEquator = RenderSettings.ambientEquatorColor;
            previousGround = RenderSettings.ambientGroundColor;
            previousAmbientMode = RenderSettings.ambientMode;
            acquired = true;
            GameObject sunObject = new GameObject("SkyIslandDaylightSun");
            sunObject.transform.SetParent(root.transform, false);
            ownSun = sunObject.AddComponent<Light>();
            ownSun.type = LightType.Directional;
            ownSun.shadows = LightShadows.Soft;
            ownerId = ownSun.GetInstanceID();
            Shader.SetGlobalFloat(Owner, ownerId);
            RenderSettings.sun = ownSun;
            if (previousSceneSun != null && previousSceneSun.gameObject.scene.handle == scene.handle)
                previousSceneSun.enabled = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            volumeObject = new GameObject("SkyIslandDaylightVolume");
            volumeObject.transform.SetParent(root.transform, false);
            Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
            if (camera == null) throw new InvalidOperationException("天空岛相机未初始化");
            UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            int mask = cameraData == null ? 1 : cameraData.volumeLayerMask.value;
            for (int layer = 0; layer < 32; layer++)
                if ((mask & (1 << layer)) != 0) { volumeObject.layer = layer; break; }
            Volume volume = volumeObject.AddComponent<Volume>();
            float priority = 1;
            foreach (Volume existing in UnityEngine.Object.FindObjectsOfType<Volume>())
                if (existing != volume && existing.priority >= priority) priority = existing.priority + 1;
            volume.priority = priority;
            volume.isGlobal = true;
            volume.weight = 1;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            lightControl = profile.Add<LightControl>(false);
            lightControl.enable.Override(true);
            lightControl.SodaLightTint.Override(Color.white);
            Tick();
        }

        internal void CyclePreset()
        {
            if (!acquired || ownSun == null || lightControl == null)
                throw new InvalidOperationException("天空岛光照尚未就绪");
            presetIndex++;
            if (presetIndex >= Presets.Length) presetIndex = -1;
            // 切档必须落地：新档与当前值可能相差不到阈值（例如自动档正好停在该预设上）。
            dirty = true;
            Tick();
        }

        internal void Tick()
        {
            if (!acquired || ownSun == null || lightControl == null || !scene.isLoaded ||
                SceneManager.GetActiveScene().handle != scene.handle || Shader.GetGlobalFloat(Owner) != ownerId) return;
            int from, to; float blend;
            ResolveTimeBlend(GameClock.TimeOfDay.TotalHours, out from, out to, out blend);
            automaticIndex = blend < .5f ? from : to;
            if (presetIndex >= 0) { from = to = presetIndex; blend = 0; }
            Preset first = Presets[from], second = Presets[to];
            Color sun = Color.Lerp(first.SunColor, second.SunColor, blend);
            Color ambient = Color.Lerp(first.AmbientColor, second.AmbientColor, blend);
            float intensity = Mathf.Lerp(first.Intensity, second.Intensity, blend);
            Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(first.Rotation), Quaternion.Euler(second.Rotation), blend);
            // 值没有可见变化就不写：锁定预设时这四个量恒定，自动档每帧的增量也远在感知阈之下。
            if (!dirty && Similar(sun, appliedSun) && Similar(ambient, appliedAmbient) &&
                Mathf.Abs(intensity - appliedIntensity) < IntensityEpsilon &&
                Quaternion.Angle(rotation, appliedRotation) < RotationEpsilonDegrees) return;
            dirty = false;
            appliedSun = sun;
            appliedAmbient = ambient;
            appliedIntensity = intensity;
            appliedRotation = rotation;
            Color sky = ScaleRgb(ambient, 1.25f);
            Color ground = ScaleRgb(ambient, .65f);
            ownSun.color = sun;
            ownSun.intensity = intensity;
            ownSun.transform.rotation = rotation;
            lightControl.sunColor.Override(sun);
            lightControl.sunIntensity.Override(intensity);
            lightControl.sunRotation.Override(rotation.eulerAngles);
            lightControl.skyColor.Override(sky);
            lightControl.equatorColor.Override(ambient);
            lightControl.groundColor.Override(ground);
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = ambient;
            RenderSettings.ambientGroundColor = ground;
            // 专用材质与官方角色/真实阴影共用同一方向和调色，避免夜色只覆盖场景材质。
            Vector3 toSun = -ownSun.transform.forward;
            Shader.SetGlobalFloat(Enabled, 1f);
            Shader.SetGlobalVector(Direction, new Vector4(toSun.x, toSun.y, toSun.z, 0));
            Shader.SetGlobalVector(Sun, sun);
            Shader.SetGlobalVector(Ambient, ambient);
        }

        // 0–5 星夜；5–7 晨光；7–10 晴昼；16–19 暮色；19–21 星夜。
        // 以连续小时和 SmoothStep 插值，午夜仍在同一星夜档，不产生跳变。
        internal static void ResolveTimeBlend(double hours, out int from, out int to, out float blend)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours)) hours = 12;
            hours = (hours % 24 + 24) % 24;
            double start, end;
            if (hours < 5 || hours >= 21) { from = to = 2; start = 0; end = 24; }
            else if (hours < 7) { from = 2; to = 3; start = 5; end = 7; }
            else if (hours < 10) { from = 3; to = 0; start = 7; end = 10; }
            else if (hours < 16) { from = to = 0; start = 10; end = 16; }
            else if (hours < 19) { from = 0; to = 1; start = 16; end = 19; }
            else { from = 1; to = 2; start = 19; end = 21; }
            float t = (float)((hours - start) / (end - start));
            blend = from == to ? 0 : t * t * (3 - 2 * t);
        }

        private static Color ScaleRgb(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        /// <summary>纯逻辑：两种颜色是否已经没有可见差别。隔离回归可直接钉住阈值语义。</summary>
        internal static bool Similar(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < ColorEpsilon && Mathf.Abs(a.g - b.g) < ColorEpsilon &&
                Mathf.Abs(a.b - b.b) < ColorEpsilon;
        }

        private struct Preset
        {
            private readonly string chinese, english;
            internal readonly Color SunColor, AmbientColor;
            internal readonly Vector3 Rotation;
            internal readonly float Intensity;
            internal string Name { get { return L10n.T(chinese, english); } }

            internal Preset(string chinese, string english, Color sun, Color ambient, Vector3 rotation, float intensity)
            {
                this.chinese = chinese;
                this.english = english;
                SunColor = sun;
                AmbientColor = ambient;
                Rotation = rotation;
                Intensity = intensity;
            }
        }

        public void Dispose()
        {
            if (!acquired) return;
            acquired = false;
            bool sameScene = scene.isLoaded && SceneManager.GetActiveScene().handle == scene.handle;
            bool ownsShader = Shader.GetGlobalFloat(Owner) == ownerId;
            if (volumeObject != null) { volumeObject.SetActive(false); UnityEngine.Object.Destroy(volumeObject); }
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components) if (component != null) UnityEngine.Object.Destroy(component);
                UnityEngine.Object.Destroy(profile);
            }
            if (sameScene && ownsShader)
            {
                if (RenderSettings.sun == ownSun) RenderSettings.sun = previousSceneSun;
                if (previousSceneSun != null && previousSceneSun.gameObject.scene.handle == scene.handle)
                    previousSceneSun.enabled = previousSunEnabled;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientSkyColor = previousSky;
                RenderSettings.ambientEquatorColor = previousEquator;
                RenderSettings.ambientGroundColor = previousGround;
            }
            if (ownSun != null) { ownSun.enabled = false; UnityEngine.Object.Destroy(ownSun.gameObject); }
            if (ownsShader)
            {
                Shader.SetGlobalFloat(Owner, previousOwner);
                Shader.SetGlobalFloat(Enabled, previousEnabled);
                Shader.SetGlobalVector(Direction, previousDirection);
                Shader.SetGlobalVector(Sun, previousSun);
                Shader.SetGlobalVector(Ambient, previousAmbient);
            }
            ownSun = null;
            lightControl = null;
            volumeObject = null;
            profile = null;
        }
    }
}

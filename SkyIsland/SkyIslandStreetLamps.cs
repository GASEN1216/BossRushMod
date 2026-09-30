using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 夜灯的纯规则：昼夜系数、灯位标记名、每种灯的照明半径与强度。无 Unity 场景依赖，隔离回归可直接执行。
    /// </summary>
    internal static class SkyIslandStreetLampRules
    {
        /// <summary>生成器写进场景的灯位标记前缀：NightLamp_&lt;种类&gt;_&lt;序号&gt;（不以 Search / POI_ / EnemySpawn / Lamp 开头，不进玩法标记）。</summary>
        internal const string MarkerPrefix = "NightLamp_";
        /// <summary>着色器一次最多照几盏（与 SkyIslandEnvironment.shader 的 SKY_ISLAND_MAX_LAMPS 同值）。</summary>
        internal const int MaxShaderLamps = 16;
        /// <summary>给官方角色补的真实点光源数：只放在离玩家最近的几盏灯上。</summary>
        internal const int MaxRealLights = 6;
        /// <summary>挑灯的半径：一屏约 28×20 m，40 m 覆盖镜头外一圈，走动时灯不会在画面边上突然亮起。</summary>
        internal const float SelectRadius = 40f;
        /// <summary>夜里重新挑灯的间隔（现实秒）。</summary>
        internal const float RefreshSeconds = 0.25f;

        /// <summary>
        /// 昼夜系数：与着色器自发光的昼夜压暗同一口径，都读 SkyIslandLighting 写下的日光色亮度。
        /// 星夜（亮度约 0.36）全亮，暮色（约 0.63）约两成，晨光 / 晴昼（0.84 / 0.92）熄灭；导览牌锁定的天色同样生效。
        /// </summary>
        internal static float NightFactor(float r, float g, float b)
        {
            float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float t = (luminance - 0.40f) / 0.30f;
            if (float.IsNaN(t)) return 0f;
            return t <= 0f ? 1f : t >= 1f ? 0f : 1f - t;
        }

        /// <summary>从标记名取灯的种类；不是夜灯标记返回 false。</summary>
        internal static bool TryParseKind(string markerName, out string kind)
        {
            kind = null;
            if (markerName == null || !markerName.StartsWith(MarkerPrefix, StringComparison.Ordinal)) return false;
            string rest = markerName.Substring(MarkerPrefix.Length);
            int last = rest.LastIndexOf('_');
            if (last <= 0) return false;
            kind = rest.Substring(0, last);
            return true;
        }

        /// <summary>
        /// 每种灯的照明半径（米）与强度。强度是着色器里加到光照上的量：灯下地面约 0.9 倍暖光，和星夜的主光同一个数量级。
        /// 路灯（雕花路灯、铜灯柱、程序化灯笼）照得最远；石灯次之；护栏上的小风灯只照亮一圈护栏与桥面。
        /// </summary>
        internal static void Profile(string kind, out float range, out float intensity)
        {
            switch (kind)
            {
                case "glass_street_lamp": range = 10f; intensity = 1.3f; return;
                case "glass_brass_lamp_post": range = 9f; intensity = 1.2f; return;
                case "lantern": range = 9f; intensity = 1.2f; return;
                case "stone": range = 6.5f; intensity = 1.0f; return;
                case "rail": range = 5f; intensity = 0.8f; return;
                default: range = 7f; intensity = 1.0f; return;
            }
        }
    }

    /// <summary>
    /// COMPAT（2026-10-01）：天空岛路灯夜里照亮周围。
    ///
    /// 岛上的环境着色器是 UniversalMaterialType=Unlit，自己算完光照再进 GBuffer，延迟管线的点光源照不到地面和墙面；
    /// 灯罩原本也只是画出来的不透明玻璃。现在分两层：
    /// - 灯罩玻璃分进带自发光的材质，入夜就亮（生成器 sky_island_surface_materials，着色器按日照压暗）；
    /// - 本类在夜里把玩家附近最多 <see cref="SkyIslandStreetLampRules.MaxShaderLamps"/> 盏灯写进着色器全局数组，
    ///   环境着色器自己把灯光加到岛面上；离玩家最近的几盏另放真实点光源，照亮官方角色、敌人与掉落。
    ///
    /// 门控（AGENTS 4.12）：只在天空岛会话里存在；白天（系数为 0）Tick 是 O(1) 早返；
    /// 真实点光源第一次入夜才创建，之后复用；会话清理时 <see cref="Dispose"/> 把灯数清零并销毁光源。
    /// </summary>
    internal sealed class SkyIslandStreetLamps : IDisposable
    {
        private struct Lamp
        {
            internal Vector3 Position;
            internal float Range;
            internal float Intensity;
        }

        // 暖黄灯色（与灯罩玻璃的自发光色 #ffc27a 同色）。
        private static readonly Color LampColor = new Color(1f, 0.76f, 0.48f, 1f);

        private readonly int countId = Shader.PropertyToID("_SkyIslandLampCount");
        private readonly int positionsId = Shader.PropertyToID("_SkyIslandLampPositions");
        private readonly int colorsId = Shader.PropertyToID("_SkyIslandLampColors");
        private readonly List<Lamp> lamps = new List<Lamp>();
        private readonly Vector4[] positions = new Vector4[SkyIslandStreetLampRules.MaxShaderLamps];
        private readonly Vector4[] colors = new Vector4[SkyIslandStreetLampRules.MaxShaderLamps];
        private readonly int[] chosen = new int[SkyIslandStreetLampRules.MaxShaderLamps];
        private readonly float[] chosenDistance = new float[SkyIslandStreetLampRules.MaxShaderLamps];
        private GameObject lightRoot;
        private Light[] lights;
        private bool published;
        private float appliedFactor = -1f;
        private float nextRefresh;

        internal int LampCount { get { return lamps.Count; } }

        internal void Apply(GameObject root)
        {
            if (root == null) throw new ArgumentNullException("root");
            foreach (Transform child in root.transform)
            {
                string kind;
                if (!SkyIslandStreetLampRules.TryParseKind(child.name, out kind)) continue;
                float range, intensity;
                SkyIslandStreetLampRules.Profile(kind, out range, out intensity);
                lamps.Add(new Lamp { Position = child.position, Range = range, Intensity = intensity });
            }
            lightRoot = new GameObject("SkyIslandStreetLamps");
            lightRoot.transform.SetParent(root.transform, false);
            // 上一趟残留的灯数不能带进这一趟：先清零，入夜后由 Tick 写。
            Shader.SetGlobalFloat(countId, 0f);
            Debug.Log("[SkyIsland] night lamps=" + lamps.Count);
        }

        internal void Tick(Vector3 viewer, Color sun)
        {
            if (lamps.Count == 0) return;
            float factor = SkyIslandStreetLampRules.NightFactor(sun.r, sun.g, sun.b);
            if (factor <= 0f)
            {
                if (published) Clear();
                return;
            }
            float now = Time.time;
            if (published && now < nextRefresh && Mathf.Abs(factor - appliedFactor) < 0.02f) return;
            nextRefresh = now + SkyIslandStreetLampRules.RefreshSeconds;
            appliedFactor = factor;
            int count = SelectNearest(viewer);
            Color linear = LampColor.linear;
            for (int i = 0; i < SkyIslandStreetLampRules.MaxShaderLamps; i++)
            {
                if (i < count)
                {
                    Lamp lamp = lamps[chosen[i]];
                    positions[i] = new Vector4(lamp.Position.x, lamp.Position.y, lamp.Position.z, lamp.Range);
                    float strength = lamp.Intensity * factor;
                    colors[i] = new Vector4(linear.r * strength, linear.g * strength, linear.b * strength, 0f);
                }
                else
                {
                    positions[i] = Vector4.zero;
                    colors[i] = Vector4.zero;
                }
            }
            Shader.SetGlobalVectorArray(positionsId, positions);
            Shader.SetGlobalVectorArray(colorsId, colors);
            Shader.SetGlobalFloat(countId, count);
            EnsureLights();
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                bool on = i < count;
                if (on)
                {
                    Lamp lamp = lamps[chosen[i]];
                    light.transform.position = lamp.Position;
                    light.range = lamp.Range;
                    light.intensity = lamp.Intensity * factor;
                }
                if (light.enabled != on) light.enabled = on;
            }
            published = true;
        }

        /// <summary>挑离 viewer 最近、在 <see cref="SkyIslandStreetLampRules.SelectRadius"/> 以内的灯，按距离从近到远放进 chosen。</summary>
        private int SelectNearest(Vector3 viewer)
        {
            int count = 0;
            float limit = SkyIslandStreetLampRules.SelectRadius * SkyIslandStreetLampRules.SelectRadius;
            for (int index = 0; index < lamps.Count; index++)
            {
                Vector3 delta = lamps[index].Position - viewer;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance > limit) continue;
                if (count == chosen.Length && distance >= chosenDistance[count - 1]) continue;
                int slot = count < chosen.Length ? count++ : count - 1;
                while (slot > 0 && chosenDistance[slot - 1] > distance)
                {
                    chosen[slot] = chosen[slot - 1];
                    chosenDistance[slot] = chosenDistance[slot - 1];
                    slot--;
                }
                chosen[slot] = index;
                chosenDistance[slot] = distance;
            }
            return count;
        }

        private void EnsureLights()
        {
            if (lights != null) return;
            lights = new Light[SkyIslandStreetLampRules.MaxRealLights];
            for (int i = 0; i < lights.Length; i++)
            {
                GameObject holder = new GameObject("NightLampLight_" + i);
                holder.transform.SetParent(lightRoot.transform, false);
                Light light = holder.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LampColor;
                light.shadows = LightShadows.None;
                light.enabled = false;
                lights[i] = light;
            }
        }

        private void Clear()
        {
            Shader.SetGlobalFloat(countId, 0f);
            if (lights != null)
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null) lights[i].enabled = false;
            published = false;
            appliedFactor = -1f;
        }

        public void Dispose()
        {
            Shader.SetGlobalFloat(countId, 0f);
            if (lightRoot != null) UnityEngine.Object.Destroy(lightRoot);
            lightRoot = null;
            lights = null;
            lamps.Clear();
            published = false;
        }
    }
}

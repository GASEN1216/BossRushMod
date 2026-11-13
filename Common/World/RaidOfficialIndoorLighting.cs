using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>为当前房间复用官方 SodaPointLight 网格灯。地图自己的环境灯数组沿用同一组灯位。</summary>
    internal sealed class RaidOfficialIndoorLighting : IDisposable
    {
        internal const int LampCount = 4;
        private const string SodaLightLayer = "SodaLight";
        private const string SodaLightShader = "SodaPointLight";
        private const string SodaLightMesh = "SodaPointLightMesh";
        private const string SodaLightProxyShader = "ZeroPoint";
        // 官方 SodaPointLight 以 One + One 叠加。四个体积在房中央重叠，沿用仓库房灯的衰减和仓库中性灯的色值，保留明暗层次。
        // 官方体积网格是单位半径，着色器在 0.91 半径内加光。
        private const float IndoorFillHardness = 0.5f, IndoorFillFalloff = 1.5f, IndoorFillHeight = 5.5f;
        private const float IndoorFillWidthFactor = 1.24f, SodaShaderRadius = 0.91f;
        private const float IndoorFillNeutral = 191f / 255f;
        private static readonly Color IndoorFillColor = new Color(IndoorFillNeutral, IndoorFillNeutral, IndoorFillNeutral, 0f);
        private static readonly Color FallbackFillColor = new Color(1f, 0.98f, 0.95f);
        private static readonly PropertyInfo RendererFeatures = typeof(ScriptableRenderer).GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SodaLightRenderer = typeof(SodaPointLight).GetField("lightRenderer", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly string logTag, lightName;
        private readonly GameObject[] indoorFill = new GameObject[LampCount];
        private GameObject root, indoorFillSource;
        private SodaPointLight fillTemplate;
        private int fillLayer;
        private bool officialFill, fillFallbackLogged, disposed;

        internal RaidOfficialIndoorLighting(string logTag, string lightName)
        {
            this.logTag = logTag;
            this.lightName = lightName;
        }

        /// <summary>幕布场景会卸载仓库。保留一个只有官方网格灯组件的停用模板，入图后交给本图根节点。</summary>
        internal void Prepare()
        {
            if (disposed || indoorFillSource != null) return;
            try
            {
                if (!TryCaptureIndoorFill()) LogFillFallback("缺少可复用的官方 SodaPointLight 预制节点");
            }
            catch (Exception e)
            {
                if (indoorFillSource != null) UnityEngine.Object.Destroy(indoorFillSource);
                indoorFillSource = null;
                fillTemplate = null;
                LogFillFallback(e.Message);
            }
        }

        private bool TryCaptureIndoorFill()
        {
            if (SodaLightRenderer == null) return false;
            foreach (SodaPointLight source in Resources.FindObjectsOfTypeAll<SodaPointLight>())
            {
                if (source == null || source.transform.childCount != 0) continue;
                // 只复制原版灯自身的四个组件，不把触发器、机关或别的效果带进新图。
                if (source.GetComponents<Component>().Length != 4) continue;
                MeshFilter filter = source.GetComponent<MeshFilter>();
                MeshRenderer renderer = source.GetComponent<MeshRenderer>();
                if (filter == null || renderer == null || filter.sharedMesh == null || filter.sharedMesh.name != SodaLightMesh) continue;
                Material proxy = renderer.sharedMaterial;
                if (proxy == null || proxy.shader == null || proxy.shader.name != SodaLightProxyShader) continue;
                if ((Renderer)SodaLightRenderer.GetValue(source) != renderer) continue;
                indoorFillSource = new GameObject(lightName + "Source");
                indoorFillSource.SetActive(false);
                fillTemplate = UnityEngine.Object.Instantiate(source, indoorFillSource.transform, false);
                fillTemplate.gameObject.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(indoorFillSource);
                return true;
            }
            return false;
        }

        internal void Bind(GameObject root)
        {
            if (disposed || this.root != null) return;
            this.root = root;
            fillLayer = LayerMask.NameToLayer(SodaLightLayer);
            // 持有节点已跨过 Single 幕布，移回出击场景后再挂根。它一直停用，不参与渲染。
            if (indoorFillSource != null)
            {
                try
                {
                    SceneManager.MoveGameObjectToScene(indoorFillSource, root.scene);
                    indoorFillSource.transform.SetParent(root.transform, false);
                }
                catch (Exception e) { Debug.LogWarning(logTag + " 室内灯资源仍由出击会话持有：" + e.Message); }
            }
            try
            {
                Material proxy = fillTemplate == null ? null : fillTemplate.GetComponent<MeshRenderer>().sharedMaterial;
                officialFill = proxy != null && proxy.shader != null && proxy.shader.isSupported && HasOfficialFillPass();
            }
            catch (Exception e) { LogFillFallback(e.Message); }
            if (officialFill) Debug.Log(logTag + " INDOOR_FILL_READY mode=SodaPointLight layer=" + fillLayer);
            else LogFillFallback("官方相机的 SodaLight 渲染通道不可用");
        }

        private bool HasOfficialFillPass()
        {
            Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
            if (camera == null || fillLayer < 0 || (camera.cullingMask & (1 << fillLayer)) == 0 || RendererFeatures == null) return false;
            UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
            ScriptableRenderer renderer = data == null ? null : data.scriptableRenderer;
            // URP 将活动渲染器的 feature 列表设为 protected；只读实际相机的列表，禁止改全局渲染器配置。
            var features = renderer == null ? null : RendererFeatures.GetValue(renderer, null) as List<ScriptableRendererFeature>;
            if (features == null) return false;
            foreach (ScriptableRendererFeature feature in features)
            {
                var pass = feature as RenderObjects;
                if (pass == null || !pass.isActive || pass.name != SodaLightLayer) continue;
                RenderObjects.RenderObjectsSettings settings = pass.settings;
                Material material = settings.overrideMaterial;
                return settings.Event == RenderPassEvent.BeforeRenderingDeferredLights
                    && settings.filterSettings.RenderQueueType == RenderQueueType.Transparent
                    && (settings.filterSettings.LayerMask.value & (1 << fillLayer)) != 0
                    && settings.overrideMode == RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material
                    && settings.overrideMaterialPassIndex == 0
                    && material != null && material.shader != null && material.shader.name == SodaLightShader && material.shader.isSupported;
            }
            return false;
        }

        private void LogFillFallback(string reason)
        {
            if (fillFallbackLogged) return;
            fillFallbackLogged = true;
            Debug.LogWarning(logTag + " 室内官方体积灯不可用，改用实时点光：" + reason);
        }

        internal static Vector3 LampPosition(Bounds bounds, int index, float heightFromBottom)
        {
            Vector3 center = bounds.center, extents = bounds.extents;
            float x = (index & 1) == 0 ? -0.5f : 0.5f, z = (index & 2) == 0 ? -0.5f : 0.5f;
            return new Vector3(center.x + x * extents.x, Mathf.Min(bounds.min.y + heightFromBottom, bounds.max.y - 0.2f), center.z + z * extents.z);
        }

        internal void ShowRoom(Bounds bounds, float heightFromBottom)
        {
            if (disposed || root == null) return;
            for (int i = 0; i < indoorFill.Length; i++) PlaceIndoorFill(i, LampPosition(bounds, i, heightFromBottom), bounds.extents);
        }

        private void PlaceIndoorFill(int index, Vector3 at, Vector3 extents)
        {
            GameObject host = indoorFill[index];
            try
            {
                if (host == null)
                {
                    if (officialFill)
                    {
                        SodaPointLight fill = UnityEngine.Object.Instantiate(fillTemplate, root.transform, false);
                        host = fill.gameObject;
                        host.transform.localRotation = Quaternion.identity;
                        ConfigureSodaFill(fill);
                    }
                    else
                    {
                        host = new GameObject();
                        host.SetActive(false);
                        host.transform.SetParent(root.transform, false);
                        Light light = host.AddComponent<Light>();
                        light.type = LightType.Point;
                        light.shadows = LightShadows.None;
                        light.renderMode = LightRenderMode.ForcePixel;
                        light.color = FallbackFillColor;
                    }
                    host.name = lightName + index;
                    indoorFill[index] = host;
                }
                host.transform.position = at;
                if (officialFill) host.transform.localScale = new Vector3(extents.x * IndoorFillWidthFactor, IndoorFillHeight, extents.z * IndoorFillWidthFactor);
                else
                {
                    Light light = host.GetComponent<Light>();
                    light.range = Mathf.Max(extents.x, extents.z) * 1.2f;
                    // Unity 回退也有四个体积叠加，各灯按房间面积缩放并限制峰值。
                    light.intensity = Mathf.Clamp(extents.x * extents.z * 0.325f, 6f, 24f);
                }
                host.SetActive(true);
            }
            catch (Exception e)
            {
                if (host != null) { host.SetActive(false); UnityEngine.Object.Destroy(host); }
                indoorFill[index] = null;
                Debug.LogWarning(logTag + " 室内补光失败：" + e.Message);
            }
        }

        private void ConfigureSodaFill(SodaPointLight fill)
        {
            fill.gameObject.layer = fillLayer;
            MeshRenderer renderer = fill.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            fill.enviromentTint = false;
            fill.Hardness = IndoorFillHardness;
            fill.FallOff = IndoorFillFalloff;
            fill.LightColor = IndoorFillColor;
        }

        internal void HideRoom()
        {
            for (int i = 0; i < indoorFill.Length; i++)
                if (indoorFill[i] != null) indoorFill[i].SetActive(false);
        }

        internal string DescribeActiveLights()
        {
            int count = 0;
            GameObject first = null;
            foreach (GameObject host in indoorFill)
            {
                if (host == null || !host.activeInHierarchy) continue;
                if (first == null) first = host;
                count++;
            }
            if (first == null) return "mode=none count=0";

            var fill = first.GetComponent<SodaPointLight>();
            if (fill != null)
            {
                Color color = fill.LightColor;
                Vector3 radius = first.transform.lossyScale * SodaShaderRadius;
                return string.Format(CultureInfo.InvariantCulture,
                    "mode=SodaPointLight count={0} rgb={1:F3},{2:F3},{3:F3} hardness={4:F2} falloff={5:F2} environmentTint={6} radius={7:F2},{8:F2},{9:F2}",
                    count, color.r, color.g, color.b, fill.Hardness, fill.FallOff, fill.enviromentTint, radius.x, radius.y, radius.z);
            }

            var point = first.GetComponent<Light>();
            if (point == null) return "mode=unknown count=" + count;
            return string.Format(CultureInfo.InvariantCulture, "mode=Point count={0} intensity={1:F2} range={2:F2}",
                count, point.intensity, point.range);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < indoorFill.Length; i++)
            {
                if (indoorFill[i] == null) continue;
                try { indoorFill[i].SetActive(false); UnityEngine.Object.Destroy(indoorFill[i]); } catch (Exception) { /* 场景已卸载，灯随世界根一起没了 */ }
                indoorFill[i] = null;
            }
            if (indoorFillSource != null) UnityEngine.Object.Destroy(indoorFillSource);
            indoorFillSource = null;
            fillTemplate = null;
            root = null;
        }
    }
}

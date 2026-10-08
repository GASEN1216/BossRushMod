using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    /// <summary>保留作者材质的真实贴图与 UV 变换；运行时材质只归当前天空岛会话。</summary>
    internal sealed class SkyIslandRendering : IDisposable
    {
        private readonly List<Material> owned = new List<Material>();
        private readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        private static readonly string[] DoubleSidedFallbacks = { "SodaCraft/SodaLit_EdgeLight_Mask", "SodaCraft/SodaCharacter" };
        private static readonly string[] ModelFallbacks = { "SodaCraft/SodaCharacter", "SodaCraft/SodaLit_EdgeLight_Mask" };
        private static readonly string[] BaseMaps = { "_BaseMap", "_MainTex", "_BaseTex" };

        internal bool CompatibilityMode { get; private set; }

        internal void Apply(GameObject root, int groundLayer, int wallLayer)
        {
            var verifiedShaders = new Dictionary<Shader, bool>();
            var compatibilityLogged = new HashSet<Shader>();
            int textures = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.gameObject.layer = groundLayer;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null) throw new InvalidOperationException("模型缺少材质：" + renderer.name);
                    string shaderName = source.shader == null ? null : source.shader.name;
                    bool worldShader = shaderName == "BossRush/SkyIsland/Environment" || shaderName == "BossRush/SkyIsland/Water" ||
                        shaderName == "BossRush/SkyIsland/Cloud";
                    if (worldShader && WorldShaderAvailable(source.shader, verifiedShaders))
                    {
                        if (source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null) textures++;
                        continue;
                    }
                    Material material;
                    if (!converted.TryGetValue(source, out material))
                    {
                        // 岛面和水面有双面薄片；本机官方 Mask shader 的 Cull Off 保住两面的可见性。
                        // 兼容材质只在作者 shader 不支持或缺 GBuffer 时使用，不替代完整包的判包流程。
                        bool doubleSided = worldShader && shaderName != "BossRush/SkyIsland/Cloud";
                        Shader fallback = FindCompatibleShader(doubleSided, verifiedShaders);
                        material = new Material(fallback);
                        material.name = worldShader ? source.name : "SkyIsland_" + source.name;
                        owned.Add(material);
                        Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                            source.HasProperty("_Color") ? source.GetColor("_Color") :
                            source.HasProperty("_Tint") ? source.GetColor("_Tint") : Color.white;
                        if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
                        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
                        string sourceMap = null;
                        foreach (string candidate in BaseMaps)
                            if (source.HasProperty(candidate) && source.GetTexture(candidate) != null) { sourceMap = candidate; break; }
                        if (sourceMap != null)
                        {
                            bool copied = false;
                            foreach (string targetMap in BaseMaps)
                            {
                                if (!material.HasProperty(targetMap)) continue;
                                CopyTexture(source, sourceMap, material, targetMap);
                                copied = true;
                            }
                            if (!copied) throw new InvalidOperationException("官方着色器无已识别贴图字段，不能丢弃壁画：" + source.name);
                            textures++;
                        }
                        if (source.HasProperty("_EmissionColor") && material.HasProperty("_EmissionColor"))
                            material.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                        if (source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") != null && material.HasProperty("_EmissionMap"))
                            CopyTexture(source, "_EmissionMap", material, "_EmissionMap");
                        foreach (string property in new[] { "_Smoothness", "_Metallic" })
                            if (source.HasProperty(property) && material.HasProperty(property)) material.SetFloat(property, source.GetFloat(property));
                        // 官方独有的顶点色、遮罩与风噪声不属于作者的手绘贴图输入。
                        if (material.HasProperty("_IgnoreVertexColor")) material.SetFloat("_IgnoreVertexColor", 1f);
                        if (material.HasProperty("_IgnoreMask")) material.SetFloat("_IgnoreMask", 1f);
                        if (material.HasProperty("_WindNoiseStrength")) material.SetFloat("_WindNoiseStrength", 0f);
                        material.renderQueue = source.renderQueue;
                        converted.Add(source, material);
                    }
                    if (worldShader)
                    {
                        CompatibilityMode = true;
                        if (compatibilityLogged.Add(source.shader))
                            Debug.LogWarning("[SkyIsland] RENDER_COMPAT shader=" + shaderName + " fallback=" + material.shader.name
                                + " supported=" + source.shader.isSupported + "（保留作者贴图，使用官方兼容显示）");
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
            int ground = 0, walls = 0;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.name.StartsWith("COL_Ground", StringComparison.Ordinal))
                { collider.gameObject.layer = groundLayer; ground++; }
                else if (collider.name.StartsWith("COL_Wall", StringComparison.Ordinal) || collider.name.StartsWith("COL_Rail", StringComparison.Ordinal))
                { collider.gameObject.layer = wallLayer; walls++; }
            }
            if (ground == 0 || walls == 0) throw new InvalidOperationException("天空岛地面或边界碰撞体缺失");
            Debug.Log("[SkyIsland] RENDER_READY materials=" + owned.Count + " textured=" + textures + " ground=" + ground + " walls=" + walls);
        }

        private static void CopyTexture(Material source, string sourceMap, Material target, string targetMap)
        {
            target.SetTexture(targetMap, source.GetTexture(sourceMap));
            target.SetTextureScale(targetMap, source.GetTextureScale(sourceMap));
            target.SetTextureOffset(targetMap, source.GetTextureOffset(sourceMap));
        }

        private static Shader FindCompatibleShader(bool doubleSided, Dictionary<Shader, bool> verifiedShaders)
        {
            foreach (string name in doubleSided ? DoubleSidedFallbacks : ModelFallbacks)
            {
                Shader shader = Shader.Find(name);
                if (WorldShaderAvailable(shader, verifiedShaders)) return shader;
            }
            throw new InvalidOperationException("天空岛专用与官方兼容着色器均不可用，请更新完整资源包并检查图形支持");
        }

        private static bool WorldShaderAvailable(Shader shader, Dictionary<Shader, bool> verifiedShaders)
        {
            if (shader == null) return false;
            bool available;
            if (verifiedShaders.TryGetValue(shader, out available)) return available;
            available = false;
            if (shader.isSupported)
            {
                var lightMode = new ShaderTagId("LightMode");
                for (int i = 0; i < shader.passCount; i++)
                    if (shader.FindPassTagValue(i, lightMode).name == "UniversalGBuffer") { available = true; break; }
            }
            verifiedShaders.Add(shader, available);
            return available;
        }

        /// <summary>
        /// 一次性渲染诊断（CR-2026-09-10-006）。2026-09-10 实机首次进岛成功，但**整张地形一片漆黑**，
        /// 而同一场景里官方预制体（敌人、战利品箱）照常可见。离线已用 UnityPy 逐项排除：
        /// 变体没被剥（三个着色器都有 d3d11 编译产物）、材质与贴图正常、769 个 MeshRenderer 全部
        /// `m_Enabled=1`、合批网格与子网格数自洽、根节点变换为单位阵、相机 cullingMask 含 Ground/Wall。
        /// 剩下的候选**必须在 GPU 上才能分辨**，所以把它们一次性打出来：
        ///   - `visible=False` → 被剔除（层不在 URP renderer 的 opaqueLayerMask 里 / 包围盒不对）；
        ///   - `visible=True` → 真的画了但是黑的（光照全局量、着色器 pass 没被管线取用）。
        /// 用 `Debug.Log` 而不是 `DevLog`：正式构建里也要能拿到这份证据，且每局只打一次。
        /// </summary>
        internal static void LogDiagnostics(GameObject root, int groundLayer, int wallLayer, string phase)
        {
            try
            {
                Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
                Debug.Log("[SkyIsland] RENDER_DIAG/" + phase
                    + " groundLayer=" + groundLayer + "(" + LayerMask.LayerToName(groundLayer) + ")"
                    + " wallLayer=" + wallLayer + "(" + LayerMask.LayerToName(wallLayer) + ")"
                    + " camera=" + (camera == null ? "null" : camera.name + " mask=0x" + camera.cullingMask.ToString("x")
                        + " clear=" + camera.clearFlags + " bg=" + camera.backgroundColor + " far=" + camera.farClipPlane)
                    + " pipeline=" + (GraphicsSettings.currentRenderPipeline == null ? "BuiltIn"
                        : GraphicsSettings.currentRenderPipeline.GetType().Name)
                    + " lightingEnabled=" + Shader.GetGlobalFloat("_SkyIslandLightingEnabled")
                    + " sun=" + Shader.GetGlobalVector("_SkyIslandSunColor")
                    + " ambient=" + Shader.GetGlobalVector("_SkyIslandAmbientColor")
                    + " ambientMode=" + RenderSettings.ambientMode + " fog=" + RenderSettings.fog);
                int logged = 0;
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (logged >= 3) break;
                    Material material = renderer.sharedMaterial;
                    Shader shader = material == null ? null : material.shader;
                    if (shader == null) continue;
                    logged++;
                    string passes = "";
                    for (int i = 0; i < shader.passCount; i++)
                        passes += (i == 0 ? "" : "|") + shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name;
                    Texture baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    Debug.Log("[SkyIsland] RENDER_DIAG/" + phase + " renderer=" + renderer.name
                        + " active=" + renderer.gameObject.activeInHierarchy + " enabled=" + renderer.enabled
                        + " visible=" + renderer.isVisible + " layer=" + renderer.gameObject.layer
                        + " bounds=" + renderer.bounds
                        + " shader=" + shader.name + " supported=" + shader.isSupported
                        + " passes=" + passes + " queue=" + material.renderQueue
                        + " baseMap=" + (baseMap == null ? "null" : baseMap.name)
                        + " baseColor=" + (material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").ToString() : "n/a"));
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIsland] RENDER_DIAG 失败：" + e.Message); }
        }

        public void Dispose()
        {
            foreach (Material material in owned) if (material != null) UnityEngine.Object.Destroy(material);
            owned.Clear();
            converted.Clear();
            CompatibilityMode = false;
        }
    }
}

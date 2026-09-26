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

        internal void Apply(GameObject root, int groundLayer, int wallLayer)
        {
            Shader shader = Shader.Find("SodaCraft/SodaCharacter");
            if (shader == null) throw new InvalidOperationException("官方模型着色器未就绪");
            var converted = new Dictionary<Material, Material>();
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
                    if (shaderName == "BossRush/SkyIsland/Environment" || shaderName == "BossRush/SkyIsland/Water" ||
                        shaderName == "BossRush/SkyIsland/Cloud")
                    {
                        // 这里报错**九成不是显卡的锅**：URP 的变体剥离会在作者工程没指定 URP 资产时
                        // 把这三个着色器的变体全删掉，包照样构建成功，运行时才发现没有任何编译产物
                        // （CR-2026-09-10-004）。判包用 tools/verify_sky_island_bundle_shaders.py。
                        if (!source.shader.isSupported)
                            throw new InvalidOperationException("天空岛专用着色器不可用（多为场景包变体被剥离，也可能是显卡不支持）：" + shaderName);
                        if (source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null) textures++;
                        continue;
                    }
                    Material material;
                    if (!converted.TryGetValue(source, out material))
                    {
                        material = new Material(shader);
                        material.name = "SkyIsland_" + source.name;
                        owned.Add(material);
                        Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                            source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                        if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
                        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
                        string sourceMap = source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null ? "_BaseMap" :
                            source.HasProperty("_MainTex") && source.GetTexture("_MainTex") != null ? "_MainTex" : null;
                        if (sourceMap != null)
                        {
                            bool copied = false;
                            foreach (string targetMap in new[] { "_BaseMap", "_MainTex", "_BaseTex" })
                            {
                                if (!material.HasProperty(targetMap)) continue;
                                material.SetTexture(targetMap, source.GetTexture(sourceMap));
                                material.SetTextureScale(targetMap, source.GetTextureScale(sourceMap));
                                material.SetTextureOffset(targetMap, source.GetTextureOffset(sourceMap));
                                copied = true;
                            }
                            if (!copied) throw new InvalidOperationException("官方着色器无已识别贴图字段，不能丢弃壁画：" + source.name);
                            textures++;
                        }
                        converted.Add(source, material);
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
        }
    }
}

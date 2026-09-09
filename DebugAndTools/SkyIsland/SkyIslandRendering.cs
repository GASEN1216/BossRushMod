using System;
using System.Collections.Generic;
using UnityEngine;

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
                        if (!source.shader.isSupported) throw new InvalidOperationException("天空岛专用着色器不受当前显卡支持：" + shaderName);
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

        public void Dispose()
        {
            foreach (Material material in owned) if (material != null) UnityEngine.Object.Destroy(material);
            owned.Clear();
        }
    }
}

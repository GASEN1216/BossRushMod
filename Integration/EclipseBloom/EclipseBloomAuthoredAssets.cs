using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>Blender MCP 作者网格的按需租约；最后一只花冠（含死亡尾迹）释放后卸载资源。</summary>
    internal static class EclipseBloomAuthoredAssets
    {
        internal const string BundleRelativePath = "Assets/ui/eclipse_bloom_visuals";
        private static AssetBundle bundle;
        private static int references;
        internal static Mesh Petal { get; private set; }
        internal static Mesh Core { get; private set; }
        internal static Mesh Fragment { get; private set; }
        internal static Material Obsidian { get; private set; }
        internal static Material Gold { get; private set; }

        internal static void Acquire()
        {
            if (bundle == null)
            {
                try
                {
                    string path = Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath);
                    if (!File.Exists(path)) throw new InvalidOperationException("eclipse_visual_bundle_missing");
                    bundle = ResourceBundleLoader.LoadFromFile(path);
                    if (bundle == null) throw new InvalidOperationException("eclipse_visual_bundle_unreadable");
                    Petal = LoadMesh("petal");
                    Core = LoadMesh("core");
                    Fragment = LoadMesh("fragment");
                    Obsidian = LoadMaterial("obsidian");
                    Gold = LoadMaterial("gold");
                }
                catch
                {
                    ResetStaticCaches();
                    throw;
                }
            }
            references++;
        }

        private static Mesh LoadMesh(string name)
        {
            Mesh mesh = bundle.LoadAsset<Mesh>("assets/eclipsebloom/meshes/eclipsebloom_" + name + ".asset");
            if (mesh == null || mesh.vertexCount < 4 || mesh.bounds.size.sqrMagnitude <= 0f)
                throw new InvalidOperationException("eclipse_authored_mesh_missing_" + name);
            return mesh;
        }

        private static Material LoadMaterial(string name)
        {
            Material material = bundle.LoadAsset<Material>("assets/eclipsebloom/materials/eclipsebloom_" + name + ".mat");
            if (material == null || material.shader == null || !material.shader.isSupported)
                throw new InvalidOperationException("eclipse_authored_material_missing_" + name);
            return material;
        }

        internal static void Release()
        {
            if (references > 0) references--;
            if (references == 0) ResetStaticCaches();
        }

        internal static void ResetStaticCaches()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            references = 0;
            Petal = Core = Fragment = null;
            Obsidian = Gold = null;
        }
    }
}

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>仅构建野外趣事实体材质包，不扫描或重打作者工程其它资源。</summary>
public static class FieldAdventureBundleBuilder
{
    private const string Root = "Assets/FieldAdventures";
    private const string BundleName = "field_adventures_visuals";
    public static void BuildAndExit()
    {
        try { WithDeferredPipeline(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void WithDeferredPipeline()
    {
        RenderPipelineAsset graphics = GraphicsSettings.defaultRenderPipeline, quality = QualitySettings.renderPipeline;
        UniversalRendererData renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); renderer.renderingMode = RenderingMode.Deferred;
        UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(renderer);
#pragma warning disable 618
        string shaderPipeline = Shader.globalRenderPipeline;
        try
        {
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            Shader.globalRenderPipeline = "UniversalPipeline";
            Build();
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = graphics; QualitySettings.renderPipeline = quality;
            Shader.globalRenderPipeline = shaderPipeline;
            UnityEngine.Object.DestroyImmediate(pipeline); UnityEngine.Object.DestroyImmediate(renderer);
        }
#pragma warning restore 618
    }
    private static void Build()
    {
        string repo = Environment.GetEnvironmentVariable("BOSSRUSH_RESOURCE_REPO");
        if (string.IsNullOrEmpty(repo)) throw new InvalidOperationException("BOSSRUSH_RESOURCE_REPO required");
        Directory.CreateDirectory(Root + "/Shaders"); Directory.CreateDirectory(Root + "/Materials");
        string output = Path.Combine(repo, "Build/FieldAdventureResources/AssetBundles"); Directory.CreateDirectory(output);
        File.Copy(Path.Combine(repo, "ArtSource/FieldAdventures/FieldAdventuresSolid.shader"), Root + "/Shaders/FieldAdventuresSolid.shader", true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FieldAdventuresSolid.shader");
        if (shader == null) throw new InvalidOperationException("solid shader import failed");
        string materialPath = Root + "/Materials/fieldadventures_solid.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
        material.shader = shader;
        material.SetColor("_BaseColor", Color.white); material.SetColor("_Color", Color.white);
        material.SetFloat("_Metallic", 0.16f); material.SetFloat("_Smoothness", 0.42f);
        material.SetFloat("_Cull", 2); material.SetFloat("_Surface", 0); material.SetFloat("_ZWrite", 1);
        material.SetFloat("_SrcBlend", 1); material.SetFloat("_DstBlend", 0);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 2000;
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssets(); Verify(material);
        var builds = new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { materialPath }, addressableNames = new[] { materialPath.ToLowerInvariant() } } };
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output, builds,
            BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("explicit material bundle build failed");
        string[] dependencies = manifest.GetAllDependencies(BundleName);
        if (dependencies.Length != 0) throw new InvalidOperationException("material bundle must be self-contained");
        AssetBundle bundle = AssetBundle.LoadFromFile(Path.Combine(output, BundleName));
        if (bundle == null) throw new InvalidOperationException("bundle readback failed");
        try { Verify(bundle.LoadAsset<Material>(materialPath.ToLowerInvariant())); }
        finally { bundle.Unload(true); }
        string target = Path.Combine(repo, "Assets/ui/" + BundleName); Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(Path.Combine(output, BundleName), target, true);
        string report = "PASS: explicit single bundle; shader=BossRush/FieldAdventures/Solid; GBuffer=true; opaque=true; ZWrite=1; dependencies=0; bytes=" + new FileInfo(target).Length;
        File.WriteAllText(Path.Combine(output, "verification.txt"), report, new UTF8Encoding(false));
        Debug.Log("FIELD_ADVENTURE_BUNDLE " + report);
    }
    private static void Verify(Material material)
    {
#pragma warning disable 618
        // BuildAssetBundles 会重置 batch 编辑器的当前管线；显式选择 URP SubShader，仍检查实际 pass。
        // 真正 Deferred 渲染在不带作者 SDK 的隔离 Unity 夹具执行。
        Shader.globalRenderPipeline = "UniversalPipeline";
#pragma warning restore 618
        if (material == null || material.shader == null || material.shader.name != "BossRush/FieldAdventures/Solid"
            || material.FindPass("GBuffer") < 0 || material.GetFloat("_ZWrite") != 1f || material.GetFloat("_Surface") != 0f)
            throw new InvalidOperationException("solid deferred material contract failed: material=" + (material == null ? "null" : material.name)
                + "; shader=" + (material == null || material.shader == null ? "null" : material.shader.name)
                + "; pass=" + (material == null ? -99 : material.FindPass("GBuffer"))
                + "; zwrite=" + (material == null ? -99 : material.GetFloat("_ZWrite"))
                + "; surface=" + (material == null ? -99 : material.GetFloat("_Surface"))
                + "; pipeline=" + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
        if (ShaderUtil.GetShaderMessages(material.shader).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
            throw new InvalidOperationException("solid shader compile error");
    }
}
#endif

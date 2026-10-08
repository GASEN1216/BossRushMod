using System;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static class Program
{
    private const string Character = "SodaCraft/SodaCharacter";
    private const string DoubleSided = "SodaCraft/SodaLit_EdgeLight_Mask";
    private static int checks;
    private static void Check(bool pass, string label)
    { checks++; if (!pass) throw new Exception(label); }
    private static bool Compatibility(SkyIslandRendering rendering)
    {
        PropertyInfo property = typeof(SkyIslandRendering).GetProperty("CompatibilityMode", BindingFlags.Instance | BindingFlags.NonPublic);
        return property != null && (bool)property.GetValue(rendering);
    }
    private static Shader ShaderFor(string name, bool supported = true, params string[] tags)
    { return new Shader { name = name, isSupported = supported, Tags = tags }; }
    private static void Officials()
    {
        Shader.Registry.Clear(); Shader.FindReads = 0; Debug.Messages.Clear();
        foreach (string name in new[] { Character, DoubleSided })
        {
            Shader shader = ShaderFor(name, true, "UniversalForward", "UniversalGBuffer");
            shader.Properties.UnionWith(new[] { "_MainTex", "_Tint", "_EmissionMap", "_EmissionColor", "_Smoothness", "_Metallic" });
            if (name == DoubleSided) shader.Properties.UnionWith(new[] { "_IgnoreVertexColor", "_IgnoreMask", "_WindNoiseStrength" });
            Shader.Registry.Add(name, shader);
        }
    }
    private static Material Painted(Shader shader, string name = "Sky_TripoCrateBarrel", string map = "_BaseMap")
    {
        shader.Properties.UnionWith(new[] { map, "_BaseColor", "_EmissionColor", "_EmissionMap", "_Smoothness", "_Metallic" });
        var material = new Material(shader) { name = name, renderQueue = 2020 };
        material.SetTexture(map, new Texture { name = "HandPainted" });
        material.SetTextureScale(map, new Vector2(2.5f, 3.5f));
        material.SetTextureOffset(map, new Vector2(0.2f, 0.3f));
        material.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.8f, 1f));
        material.SetColor("_EmissionColor", new Color(2f, 0.3f, 0.4f, 1f));
        material.SetTexture("_EmissionMap", new Texture { name = "LanternGlow" });
        material.SetTextureScale("_EmissionMap", new Vector2(4f, 5f));
        material.SetTextureOffset("_EmissionMap", new Vector2(0.6f, 0.7f));
        material.SetFloat("_Smoothness", 0.72f);
        material.SetFloat("_Metallic", 0.23f);
        return material;
    }
    private static GameObject World(Material material, int count = 1)
    {
        var renderers = new MeshRenderer[count];
        for (int i = 0; i < count; i++) renderers[i] = new MeshRenderer { sharedMaterials = new[] { material } };
        return new GameObject { Renderers = renderers, Colliders = new[]
        {
            new Collider { name = "COL_Ground_Dock" },
            new Collider { name = "COL_Rail_Dock" }
        } };
    }
    private static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, label);
    }
    private static void Preserved(Material source, Material target, string map = "_BaseMap")
    {
        Check(ReferenceEquals(target.GetTexture("_MainTex"), source.GetTexture(map)), "painted texture remains the exact source object");
        Check(target.GetTextureScale("_MainTex").Equals(source.GetTextureScale(map)), "nontrivial texture scale survives");
        Check(target.GetTextureOffset("_MainTex").Equals(source.GetTextureOffset(map)), "nontrivial texture offset survives");
        Check(target.GetColor("_Tint").Equals(source.GetColor("_BaseColor")), "author color reaches the official tint field");
        Check(target.GetColor("_EmissionColor").Equals(source.GetColor("_EmissionColor")), "HDR emission color survives");
        Check(ReferenceEquals(target.GetTexture("_EmissionMap"), source.GetTexture("_EmissionMap")), "emission texture survives");
        Check(target.GetTextureScale("_EmissionMap").Equals(source.GetTextureScale("_EmissionMap")), "emission UV scale survives");
        Check(target.GetTextureOffset("_EmissionMap").Equals(source.GetTextureOffset("_EmissionMap")), "emission UV offset survives");
        Check(target.GetFloat("_Smoothness") == 0.72f && target.GetFloat("_Metallic") == 0.23f, "author surface parameters survive");
        Check(target.renderQueue == source.renderQueue, "world queue survives conversion");
    }
    private static void SupportedWorlds()
    {
        foreach (string name in new[] { "Environment", "Water", "Cloud" })
        {
            Officials(); Shader.Registry.Clear();
            var shader = ShaderFor("BossRush/SkyIsland/" + name, true, "UniversalForward", "UniversalGBuffer");
            var source = Painted(shader); var world = World(source, 100); var rendering = new SkyIslandRendering();
            rendering.Apply(world, 8, 9);
            Check(shader.TagReads == 2, "shared " + name + " shader is validated once per entry");
            Check(Shader.FindReads == 0, "healthy author shader needs no official fallback");
            Check(ReferenceEquals(world.Renderers[0].sharedMaterials[0], source), "healthy author material is preserved");
            Check(!Compatibility(rendering), "healthy shader does not report compatibility mode");
            Check(world.Colliders[0].gameObject.layer == 8 && world.Colliders[1].gameObject.layer == 9, "collision layers remain valid");
            rendering.Dispose(); Check(source != null, "healthy source is never owned or destroyed");
        }
    }
    private static void CompatibilityWorlds()
    {
        foreach (string name in new[] { "Environment", "Water", "Cloud" })
        foreach (bool supported in new[] { true, false })
        {
            Officials();
            var shader = ShaderFor("BossRush/SkyIsland/" + name, supported, "UniversalForward");
            var source = Painted(shader); var world = World(source, 100); var rendering = new SkyIslandRendering();
            rendering.Apply(world, 8, 9);
            Material converted = world.Renderers[0].sharedMaterials[0];
            Check(converted.shader.name == (name == "Cloud" ? Character : DoubleSided), "official candidate respects world surface kind");
            Check(converted.name == source.name, "world material name remains exact for loot-crate lookup");
            Check(!ReferenceEquals(converted, source) && ReferenceEquals(converted, world.Renderers[99].sharedMaterials[0]), "source material reference is converted once and shared");
            Check(shader.TagReads == (supported ? 1 : 0), "unavailable world shader is checked once");
            Check(Compatibility(rendering), "world fallback exposes compatibility mode to the session");
            Check(Debug.Messages.FindAll(s => s.Contains("RENDER_COMPAT")).Count == 1, "compatibility diagnostic is once per shader");
            Preserved(source, converted);
            if (name != "Cloud") Check(converted.GetFloat("_IgnoreVertexColor") == 1f && converted.GetFloat("_IgnoreMask") == 1f
                && converted.GetFloat("_WindNoiseStrength") == 0f, "official-only color and wind inputs are disabled");
            rendering.Dispose();
            Check(converted == null && converted.DestroyCalls == 1 && source != null, "only owned conversion is destroyed");
            Check(source.GetTexture("_BaseMap") != null && source.GetTexture("_EmissionMap") != null, "source textures survive owner cleanup");
            Check(!Compatibility(rendering), "cleanup clears compatibility state");
            var cache = typeof(SkyIslandRendering).GetField("converted", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(cache != null && ((System.Collections.IDictionary)cache.GetValue(rendering)).Count == 0, "cleanup releases source-reference cache");
            rendering.Dispose(); Check(converted.DestroyCalls == 1, "repeated cleanup is harmless");
        }
    }
    private static void CandidateFailures()
    {
        foreach (string kind in new[] { "Environment", "Cloud" })
        foreach (string reason in new[] { "missing", "unsupported", "forward-only" })
        {
            Officials(); string first = kind == "Cloud" ? Character : DoubleSided;
            string second = kind == "Cloud" ? DoubleSided : Character;
            if (reason == "missing") Shader.Registry.Remove(first);
            else if (reason == "unsupported") Shader.Registry[first].isSupported = false;
            else Shader.Registry[first].Tags = new[] { "UniversalForward" };
            var source = Painted(ShaderFor("BossRush/SkyIsland/" + kind, false));
            var world = World(source); var rendering = new SkyIslandRendering(); rendering.Apply(world, 8, 9);
            Check(world.Renderers[0].sharedMaterials[0].shader.name == second, "unavailable preferred candidate falls back: " + reason);
            rendering.Dispose();
        }
        foreach (string kind in new[] { "Environment", "Water", "Cloud" })
        {
            Officials(); Shader.Registry[Character].isSupported = false; Shader.Registry[DoubleSided].Tags = new[] { "UniversalForward" };
            var rendering = new SkyIslandRendering();
            Reject(() => rendering.Apply(World(Painted(ShaderFor("BossRush/SkyIsland/" + kind, false))), 8, 9), "all unusable candidates keep invisible worlds rejected");
            rendering.Dispose();
        }
        Officials(); Shader.Registry.Clear();
        Reject(() => new SkyIslandRendering().Apply(World(Painted(ShaderFor("BossRush/SkyIsland/Environment", false))), 8, 9), "all missing candidates are rejected");
    }
    private static void OwnershipAndHardGates()
    {
        Officials(); var shader = ShaderFor("BossRush/SkyIsland/Environment", false);
        var source = Painted(shader); var other = Painted(shader, source.name); var world = World(source, 3);
        world.Renderers[2].sharedMaterials[0] = other;
        var owner = new SkyIslandRendering(); owner.Apply(world, 8, 9);
        Check(!ReferenceEquals(world.Renderers[0].sharedMaterials[0], world.Renderers[2].sharedMaterials[0]), "same names with different source references remain distinct");
        var secondWorld = World(source); var secondOwner = new SkyIslandRendering(); secondOwner.Apply(secondWorld, 8, 9);
        owner.Dispose(); Check(secondWorld.Renderers[0].sharedMaterials[0] != null, "one session cannot destroy another session's material");
        secondOwner.Dispose();
        foreach (bool ground in new[] { false, true })
        {
            Officials(); world = World(Painted(ShaderFor("BossRush/SkyIsland/Environment", false)));
            world.Colliders = new[] { new Collider { name = ground ? "COL_Ground_Dock" : "COL_Wall_Dock" } };
            owner = new SkyIslandRendering();
            Reject(() => owner.Apply(world, 8, 9), "compatible rendering never bypasses ground or boundary collision gate");
            owner.Dispose(); Check(world.Renderers[0].sharedMaterials[0] == null, "failed entry still releases partial materials");
        }
        Officials(); source = Painted(ShaderFor("Author/Decoration"), "Decoration", "_MainTex"); world = World(source);
        owner = new SkyIslandRendering(); owner.Apply(world, 8, 9);
        Check(world.Renderers[0].sharedMaterials[0].name == "SkyIsland_Decoration", "existing decoration naming remains compatible");
        Check(!Compatibility(owner), "ordinary decoration conversion does not produce player compatibility notice");
        Preserved(source, world.Renderers[0].sharedMaterials[0], "_MainTex"); owner.Dispose();
        Officials(); source = Painted(ShaderFor("BossRush/SkyIsland/Environment", false)); world = World(source, 2);
        world.Renderers[1].sharedMaterials[0] = null; owner = new SkyIslandRendering();
        Reject(() => owner.Apply(world, 8, 9), "missing materials remain a hard failure");
        Material partial = world.Renderers[0].sharedMaterials[0]; owner.Dispose();
        Check(partial == null && source != null, "partial failure destroys owned material and preserves author input");
        Officials(); source = Painted(ShaderFor("BossRush/SkyIsland/Environment", false)); UnityEngine.Object.Destroy(source);
        Reject(() => new SkyIslandRendering().Apply(World(source), 8, 9), "destroyed source follows Unity null semantics");
        world = World(new Material(ShaderFor("Author/Decoration"))); UnityEngine.Object.Destroy(world);
        Check(world.Renderers[0] == null && world.Colliders[0] == null, "destroying a root destroys its component doubles");
    }
    private static void Main()
    {
        CompatibilityWorlds(); SupportedWorlds(); CandidateFailures(); OwnershipAndHardGates();
        Console.WriteLine("PASS: " + checks + " production rendering-entry checks");
    }
}

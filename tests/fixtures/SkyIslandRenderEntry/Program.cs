using System;
using System.Collections.Generic;
using BossRush;

namespace UnityEngine.Rendering
{
    public struct ShaderTagId
    {
        public string name;
        public ShaderTagId(string name) { this.name = name; }
    }
}
namespace UnityEngine
{
    public struct Color { public static Color white; }
    public struct Vector2 { }
    public sealed class Texture { }
    public static class Debug { public static void Log(string message) { } }
    public sealed class Shader
    {
        public string name;
        public bool isSupported = true;
        public string[] Tags;
        public int TagReads;
        public int passCount { get { return Tags.Length; } }
        public Rendering.ShaderTagId FindPassTagValue(int i, Rendering.ShaderTagId key)
        { TagReads++; return new Rendering.ShaderTagId(Tags[i]); }
        private static readonly Shader Official = new Shader { name = "SodaCraft/SodaCharacter", Tags = new[] { "UniversalGBuffer" } };
        public static Shader Find(string name) { return Official; }
    }
    public sealed class Material
    {
        public Shader shader;
        public string name;
        public Material(Shader shader) { this.shader = shader; }
        public bool HasProperty(string key) { return false; }
        public Color GetColor(string key) { return Color.white; }
        public Texture GetTexture(string key) { return null; }
        public Vector2 GetTextureScale(string key) { return new Vector2(); }
        public Vector2 GetTextureOffset(string key) { return new Vector2(); }
        public void SetColor(string key, Color value) { }
        public void SetTexture(string key, Texture value) { }
        public void SetTextureScale(string key, Vector2 value) { }
        public void SetTextureOffset(string key, Vector2 value) { }
    }
    public sealed class GameObject
    {
        public int layer;
        public MeshRenderer[] Renderers;
        public Collider[] Colliders;
        public T[] GetComponentsInChildren<T>(bool inactive)
        {
            return typeof(T) == typeof(MeshRenderer) ? (T[])(object)Renderers : (T[])(object)Colliders;
        }
    }
    public sealed class MeshRenderer
    {
        public string name = "IslandGround";
        public GameObject gameObject = new GameObject();
        public Material[] sharedMaterials;
    }
    public sealed class Collider
    {
        public string name;
        public GameObject gameObject = new GameObject();
    }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool pass, string label)
    { checks++; if (!pass) throw new Exception(label); }
    private static UnityEngine.GameObject World(UnityEngine.Shader shader, int count = 1)
    {
        var renderers = new UnityEngine.MeshRenderer[count];
        for (int i = 0; i < count; i++) renderers[i] = new UnityEngine.MeshRenderer
        { sharedMaterials = new[] { new UnityEngine.Material(shader) } };
        return new UnityEngine.GameObject { Renderers = renderers, Colliders = new[]
        {
            new UnityEngine.Collider { name = "COL_Ground_Dock" },
            new UnityEngine.Collider { name = "COL_Wall_Dock" }
        } };
    }
    private static void Main()
    {
        foreach (string name in new[] { "Environment", "Water", "Cloud" })
        {
            var shader = new UnityEngine.Shader { name = "BossRush/SkyIsland/" + name,
                Tags = new[] { "UniversalForward", "UniversalGBuffer" } };
            var world = World(shader, 100);
            new SkyIslandRendering().Apply(world, 8, 9);
            Check(shader.TagReads == 2, "shared " + name + " shader is validated once per entry");
            Check(ReferenceEquals(world.Renderers[0].sharedMaterials[0].shader, shader), "author shader is preserved");
            Check(world.Colliders[0].gameObject.layer == 8 && world.Colliders[1].gameObject.layer == 9, "collision layers remain valid");

            shader = new UnityEngine.Shader { name = "BossRush/SkyIsland/" + name, Tags = new[] { "UniversalForward" } };
            bool rejected = false;
            try { new SkyIslandRendering().Apply(World(shader), 8, 9); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "supported Forward-only " + name + " cannot silently admit an invisible Deferred world");

            shader = new UnityEngine.Shader { name = "BossRush/SkyIsland/" + name, isSupported = false, Tags = new[] { "UniversalGBuffer" } };
            rejected = false;
            try { new SkyIslandRendering().Apply(World(shader), 8, 9); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "unsupported shaders still reject setup");
        }
        var fallback = World(new UnityEngine.Shader { name = "Author/Decoration", Tags = new string[0] });
        new SkyIslandRendering().Apply(fallback, 8, 9);
        Check(fallback.Renderers[0].sharedMaterials[0].shader.name == "SodaCraft/SodaCharacter", "existing decoration fallback remains available");
        Console.WriteLine("PASS: " + checks + " production rendering-entry checks");
    }
}

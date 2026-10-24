using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CharacterRandomPreset : UnityEngine.Object
{ internal CharacterRandomPreset Clone() { return new CharacterRandomPreset(); } }
public sealed class CharacterMainControl : MonoBehaviour { }
public class ItemAgent : MonoBehaviour { }
public sealed class DuckovItemAgent : ItemAgent { }
public sealed class ItemGraphicInfo : MonoBehaviour { public Transform groundPoint; }
namespace ItemStatsSystem
{
    public sealed class AgentUtilities
    {
        internal readonly Dictionary<string, ItemAgent> Prefabs = new Dictionary<string, ItemAgent>();
        public ItemAgent GetPrefab(string name) { ItemAgent value; return Prefabs.TryGetValue(name, out value) ? value : null; }
    }
    public sealed class Item : MonoBehaviour
    {
        public int TypeID;
        public bool useSpriteForPickup = true;
        public ItemGraphicInfo ItemGraphic;
        public readonly AgentUtilities AgentUtilities = new AgentUtilities();
    }
}
namespace Duckov.Utilities
{
    internal static class GameplayDataSettings
    { internal static class Layers { internal const int groundLayerMask = 1; } }
}
namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { On, Off }
    public enum LightProbeUsage { Off }
    public enum ReflectionProbeUsage { Off }
}
namespace UnityEngine
{
    public sealed class Shader : Object { public bool isSupported = true; }
    public sealed class Sprite : Object { public Texture2D texture; }
    public sealed class Material : Object { public Shader shader; }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public class Renderer : Behaviour
    {
        public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows, forceRenderingOff;
        public Rendering.LightProbeUsage lightProbeUsage; public Rendering.ReflectionProbeUsage reflectionProbeUsage;
        public void SetPropertyBlock(MaterialPropertyBlock block) { }
    }
    public sealed class MeshRenderer : Renderer { }
    public sealed class LineRenderer : Renderer
    {
        public bool useWorldSpace; public int positionCount, numCornerVertices, numCapVertices;
        public float widthMultiplier; public Color startColor, endColor;
        public void SetPositions(Vector3[] points) { }
    }
    public sealed class MaterialPropertyBlock { public void Clear() { } public void SetVector(string name, Vector4 value) { } }
    public struct Vector4 { public Vector4(float x, float y, float z, float w) { } }
    public sealed class Texture2D : Object { public int width, height; public static readonly Texture2D whiteTexture = new Texture2D(); }
    public sealed class AnimationCurve { public static AnimationCurve EaseInOut(float a, float b, float c, float d) { return new AnimationCurve(); } }
    public sealed class ParticleSystem : Component
    {
        public readonly MainModule main = new MainModule();
        public readonly ColorOverLifetimeModule colorOverLifetime = new ColorOverLifetimeModule();
        public readonly SizeOverLifetimeModule sizeOverLifetime = new SizeOverLifetimeModule();
        public sealed class MainModule { public MinMaxCurve startLifetime, startSize, startRotation; }
        public sealed class ColorOverLifetimeModule { public bool enabled; public object color; }
        public sealed class SizeOverLifetimeModule { public bool enabled; public MinMaxCurve size; }
        public struct MinMaxCurve { public MinMaxCurve(float a, float b) { } public MinMaxCurve(float a, AnimationCurve curve) { } }
    }
    public sealed class AssetBundle : Object
    {
        internal readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        internal readonly List<string> Requests = new List<string>();
        internal int Unloads;
        internal bool UnloadedAll;
        public T LoadAsset<T>(string name) where T : Object
        { Requests.Add(name); Object asset; return Assets.TryGetValue(name, out asset) ? asset as T : null; }
        public void Unload(bool all)
        {
            Unloads++; UnloadedAll = all;
            if (all) foreach (Object asset in Assets.Values) Object.Destroy(asset);
            Destroyed = true;
        }
    }
}
namespace BossRush
{
    internal enum BossRushFxBlend { Additive, Alpha }
    internal static class BossRushFxKit
    {
        internal const float GainBright = 1f, GainSoft = 0.5f;
        internal static Material GetMaterial(BossRushFxBlend blend, Texture2D texture, float gain) { return new Material { shader = new Shader() }; }
        internal static object FadeGradient(Color a, Color b, Color c, float alpha) { return new object(); }
    }
    public static class ModBehaviour
    { internal static string ModPath; public static string GetModPath() { return ModPath; } public static void DevLog(string text) { } }
    public static partial class EquipmentFactory
    {
        internal static readonly Dictionary<int, ItemAgent> loadedModels = new Dictionary<int, ItemAgent>();
        internal static readonly HashSet<AssetBundle> ownedBundles = new HashSet<AssetBundle>();
        internal static ItemAgent Loaded, OnLoad;
        internal static int Queries, Loads, SubVisuals;
        internal static bool ThrowQuery, RejectGraphic;
        internal static string LastBundle, LastModel;
        public static bool TryGetLoadedModel(string name, out ItemAgent agent)
        { Queries++; LastModel = name; if (ThrowQuery) throw new InvalidOperationException("injected factory query failure"); agent = Loaded; return agent != null; }
        public static int LoadBundle(string name) { Loads++; LastBundle = name; Loaded = OnLoad; return Loaded != null ? 1 : 0; }
        private static void EnsureModelSubVisuals(GameObject model) { SubVisuals++; }
        private static void SetAgentUtilityPrefab(ItemStatsSystem.Item item, string name, ItemAgent model) { item.AgentUtilities.Prefabs[name] = model; }
        private static void InjectItemGraphicForEquipment(ItemStatsSystem.Item item, ItemAgent model, bool overwrite)
        { if (!RejectGraphic) item.ItemGraphic = model.GetComponent<ItemGraphicInfo>(); }
        internal static void Reset()
        { Loaded = OnLoad = null; Queries = Loads = SubVisuals = 0; ThrowQuery = RejectGraphic = false; loadedModels.Clear(); ownedBundles.Clear(); }
    }
    internal static class ResourceBundleLoader
    {
        internal static AssetBundle Next;
        internal static int Loads;
        internal static string LastPath;
        internal static AssetBundle LoadFromFile(string path)
        { Loads++; LastPath = path; AssetBundle value = Next; Next = null; return value; }
    }
}

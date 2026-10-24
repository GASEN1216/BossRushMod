using UnityEngine;

// 只替换游戏宿主边界；Unity 粒子、对象生命周期、真实资源加载均由引擎执行。
public sealed class CharacterMainControl : MonoBehaviour
{
    public readonly EclipseParticleHealth Health = new EclipseParticleHealth();
}

public sealed class EclipseParticleHealth { public bool IsDead; }

namespace Duckov.Utilities
{
    public static class GameplayDataSettings { public static readonly EclipseParticleLayers Layers = new EclipseParticleLayers(); }
    public sealed class EclipseParticleLayers { public int groundLayerMask = 1; }
}

namespace BossRush
{
    internal static class ResourceBundleLoader
    {
        internal static AssetBundle LoadFromFile(string path) { return AssetBundle.LoadFromFile(path); }
    }
    internal static class ModBehaviour
    {
        internal static string GetModPath() { return EclipseBloomParticleRegression.Configuration.modRoot; }
        internal static void DevLog(string message) { Debug.Log(message); }
    }
    internal static class BossRushUI
    {
        internal static bool IsGamePaused() { return false; }
        internal static float SmoothStep(float value) { return Mathf.SmoothStep(0f, 1f, value); }
    }
    internal static class ModEntityFogVisibility { internal static void Attach(GameObject root, Transform target) { } }
    internal enum SkyIslandBossCue { Telegraph = 0, Impact = 1, Shatter = 2, Phase = 3, Defeat = 4 }
    internal static class SkyIslandBossSfx { internal static void Play(Transform root, SkyIslandBossCue cue, Vector3 at) { } }
    internal static class SandstormChampionAssetManager
    {
        internal static void ShakeNear(Vector3 point, float strength, float range) { }
    }
}

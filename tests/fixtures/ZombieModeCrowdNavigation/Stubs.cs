using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    /// <summary>与 Unity 同语义：InverseLerp / Lerp 都夹在 0–1。</summary>
    public static class Mathf
    {
        public static float Sqrt(float value) { return (float)Math.Sqrt(value); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Clamp01(float value) { return value < 0f ? 0f : (value > 1f ? 1f : value); }
        public static float InverseLerp(float a, float b, float value) { return a != b ? Clamp01((value - a) / (b - a)) : 0f; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
    }
}

namespace BossRush
{
    /// <summary>只保留节流判定读写的字段。</summary>
    public sealed class ZombieModeEnemyRuntimeMarker
    {
        public bool HasPathRequest;
        public float LastPathRequestTime;
        public UnityEngine.Vector3 LastPathRequestTarget;
        public UnityEngine.Vector3 LastPathRequestOrigin;
    }
}

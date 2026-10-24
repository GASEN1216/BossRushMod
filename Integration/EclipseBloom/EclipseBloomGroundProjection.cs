using UnityEngine;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>单场战斗的地形高度缓存。每帧最多 96 次采样，未采到的地面不画也不判伤。</summary>
    internal sealed class EclipseBloomGroundProjection
    {
        internal delegate bool SampleGround(Vector3 point, out float height);
        internal const int SamplesPerFrame = 96;
        private const int Side = 33;
        private const int Half = 16;
        private readonly float[] heights = new float[Side * Side];
        private readonly bool[] valid = new bool[Side * Side];
        private readonly SampleGround sample;
        private Vector3 center;
        private bool initialized;
        private int cursor;
        private int frame = -1;

        internal EclipseBloomGroundProjection(SampleGround sampler = null)
        {
            sample = sampler ?? Probe;
        }

        internal void Step(Vector3 origin)
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            Vector3 delta = origin - center;
            if (!initialized || delta.x * delta.x + delta.z * delta.z > 4f || Mathf.Abs(delta.y) > 0.5f)
            {
                center = new Vector3(Mathf.Floor(origin.x), origin.y, Mathf.Floor(origin.z));
                initialized = true;
                cursor = 0;
                System.Array.Clear(valid, 0, valid.Length);
            }
            int end = Mathf.Min(cursor + SamplesPerFrame, heights.Length);
            while (cursor < end)
            {
                Vector3 point = center + new Vector3(cursor % Side - Half, 0f, cursor / Side - Half);
                float height;
                valid[cursor] = sample(point, out height) && !float.IsNaN(height) && !float.IsInfinity(height)
                    && Mathf.Abs(height - center.y) <= 2f;
                heights[cursor] = height;
                cursor++;
            }
        }

        internal bool TryHeight(Vector3 world, out float height)
        {
            height = 0f;
            if (!initialized) return false;
            float x = world.x - center.x + Half, z = world.z - center.z + Half;
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            if (ix < 0 || iz < 0 || ix >= Side - 1 || iz >= Side - 1) return false;
            int a = iz * Side + ix, b = a + 1, c = a + Side, d = c + 1;
            if (!valid[a] || !valid[b] || !valid[c] || !valid[d]) return false;
            float min = Mathf.Min(Mathf.Min(heights[a], heights[b]), Mathf.Min(heights[c], heights[d]));
            float max = Mathf.Max(Mathf.Max(heights[a], heights[b]), Mathf.Max(heights[c], heights[d]));
            // 不跨越陡坎、不同楼层或墙顶插值出一块实际上不存在的警示地面。
            if (max - min > 0.65f) return false;
            height = Mathf.Lerp(Mathf.Lerp(heights[a], heights[b], x - ix),
                Mathf.Lerp(heights[c], heights[d], x - ix), z - iz);
            return true;
        }

        private static bool Probe(Vector3 point, out float height)
        {
            height = 0f;
            RaycastHit hit;
            if (!Physics.Raycast(point + Vector3.up * 2.5f, Vector3.down, out hit, 5f,
                GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.6f) return false;
            height = hit.point.y;
            return true;
        }

        /// <summary>判伤读取实际绘制的三角面，高度不符或被裁掉的面不命中。</summary>
        internal static bool ContainsTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < 0.00001f) return false;
            float u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
            float v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
            float w = 1f - u - v;
            return u >= -0.0001f && v >= -0.0001f && w >= -0.0001f
                && Mathf.Abs(point.y - (u * a.y + v * b.y + w * c.y)) <= 0.45f;
        }
    }
}

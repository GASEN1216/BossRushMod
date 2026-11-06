using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一片不与其它片重叠的水域；坐标是世界 XZ，海与泻湖共享同一套分界。</summary>
    internal struct TidegateWaterRegion
    {
        internal float MinX, MinZ, MaxX, MaxZ;
        internal bool Lagoon;

        internal TidegateWaterRegion(float minX, float minZ, float maxX, float maxZ, bool lagoon)
        {
            MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; Lagoon = lagoon;
        }
    }

    /// <summary>
    /// 从真实泻湖遮罩合并出空间水域（CR-2026-11-06-002）。只细分遮罩非零范围，外围仍是四块大海面；
    /// 0.25 m 格内按中心取样，再把同类连续条带合并。轮廓近似误差最多约 0.18 m，盒子没有面积重叠或漏缝。
    /// 官方 0.2 m 探针在轮廓两侧的窄带仍可能同时碰到两片水；不为消掉这条窄带而关闭远处的真实水域。
    /// 纯托管采样，不在细分循环里调用 Texture2D.GetPixelBilinear。
    /// </summary>
    internal static class TidegateWaterRegions
    {
        internal const float CellSize = 0.25f;

        /// <summary>与 TidegateWater.shader 的 GPU Clamp 双线性取样一致：像素中心位于 ((x+0.5)/w,(z+0.5)/h)。</summary>
        internal static float Sample(float[] mask, int width, int height, float u, float v)
        {
            float x = Math.Max(0f, Math.Min(width - 1, u * width - 0.5f));
            float z = Math.Max(0f, Math.Min(height - 1, v * height - 0.5f));
            int ix = (int)x, iz = (int)z;
            int nextX = Math.Min(ix + 1, width - 1), nextZ = Math.Min(iz + 1, height - 1);
            float fx = x - ix, fz = z - iz;
            float low = mask[iz * width + ix] * (1f - fx) + mask[iz * width + nextX] * fx;
            float high = mask[nextZ * width + ix] * (1f - fx) + mask[nextZ * width + nextX] * fx;
            return low * (1f - fz) + high * fz;
        }

        internal static List<TidegateWaterRegion> Build(float[] mask, int width, int height,
            float originX, float originZ, float sizeX, float sizeZ, out int samples)
        {
            if (mask == null || width <= 0 || height <= 0 || mask.Length != width * height || sizeX <= 0f || sizeZ <= 0f)
                throw new ArgumentException("Invalid Tidegate water mask");
            samples = 0;
            var regions = new List<TidegateWaterRegion>();
            int minX = width, minZ = height, maxX = -1, maxZ = -1;
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    if (mask[z * width + x] <= 0f) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                }
            if (maxX < 0)
            {
                regions.Add(new TidegateWaterRegion(originX, originZ, originX + sizeX, originZ + sizeZ, false));
                return regions;
            }
            // GPU 纹素中心在 i+0.5，非零像素 i 的双线性支撑区是 [i-0.5,i+1.5]；覆盖全支撑区后吸到 0.25 m 网格。
            int columns = (int)Math.Ceiling(sizeX / CellSize), rows = (int)Math.Ceiling(sizeZ / CellSize);
            int firstX = Math.Max(0, (int)Math.Floor((minX - 0.5f) / width * sizeX / CellSize));
            int lastX = Math.Min(columns, (int)Math.Ceiling((maxX + 1.5f) / width * sizeX / CellSize));
            int firstZ = Math.Max(0, (int)Math.Floor((minZ - 0.5f) / height * sizeZ / CellSize));
            int lastZ = Math.Min(rows, (int)Math.Ceiling((maxZ + 1.5f) / height * sizeZ / CellSize));
            float left = originX + firstX * CellSize, right = originX + Math.Min(sizeX, lastX * CellSize);
            float bottom = originZ + firstZ * CellSize, top = originZ + Math.Min(sizeZ, lastZ * CellSize);
            Add(regions, originX, originZ, left, originZ + sizeZ);
            Add(regions, right, originZ, originX + sizeX, originZ + sizeZ);
            Add(regions, left, originZ, right, bottom);
            Add(regions, left, top, right, originZ + sizeZ);
            var previous = new Dictionary<long, int>();
            var current = new Dictionary<long, int>();
            for (int row = firstZ; row < lastZ; row++)
            {
                current.Clear();
                float z0 = row * CellSize, z1 = Math.Min(sizeZ, (row + 1) * CellSize);
                int start = firstX;
                bool lagoon = false;
                for (int column = firstX; column <= lastX; column++)
                {
                    bool atEnd = column == lastX, next = false;
                    if (!atEnd)
                    {
                        float x0 = column * CellSize, x1 = Math.Min(sizeX, (column + 1) * CellSize);
                        next = Sample(mask, width, height, (x0 + x1) * 0.5f / sizeX, (z0 + z1) * 0.5f / sizeZ) > 0.5f;
                        samples++;
                    }
                    if (column == firstX) { lagoon = next; continue; }
                    if (!atEnd && next == lagoon) continue;
                    long key = ((long)start << 33) | ((long)column << 1) | (lagoon ? 1L : 0L);
                    int index;
                    if (previous.TryGetValue(key, out index))
                    {
                        TidegateWaterRegion region = regions[index];
                        region.MaxZ = originZ + z1;
                        regions[index] = region;
                    }
                    else
                    {
                        index = regions.Count;
                        regions.Add(new TidegateWaterRegion(originX + start * CellSize, originZ + z0,
                            originX + Math.Min(sizeX, column * CellSize), originZ + z1, lagoon));
                    }
                    current.Add(key, index);
                    start = column;
                    lagoon = next;
                }
                Dictionary<long, int> swap = previous; previous = current; current = swap;
            }
            return regions;
        }

        private static void Add(List<TidegateWaterRegion> regions, float minX, float minZ, float maxX, float maxZ)
        {
            if (maxX > minX && maxZ > minZ) regions.Add(new TidegateWaterRegion(minX, minZ, maxX, maxZ, false));
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>由实际碰撞体足迹生成俯视图。像素原点在左下，X 向右、Z 向上。</summary>
    internal static class StoneOutpostMapRaster
    {
        internal const int Resolution = 512;
        internal const float WorldSize = 100f;

        internal static int Pixel(float coordinate)
        {
            return (int)Math.Floor((coordinate + WorldSize * 0.5f) / WorldSize * Resolution);
        }

        internal static Color32[] Draw(IList<Rect> footprints, Color32 ground, Color32 grid,
            Color32 wall, Color32 outline)
        {
            var pixels = new Color32[Resolution * Resolution];
            for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++) pixels[y * Resolution + x] = ground;
            // 每十米一条细网格，精确比例不依赖屏幕分辨率。
            for (int meter = -40; meter <= 40; meter += 10)
            {
                int p = Pixel(meter);
                for (int i = 0; i < Resolution; i++)
                { pixels[p * Resolution + i] = grid; pixels[i * Resolution + p] = grid; }
            }
            foreach (Rect rect in footprints)
            {
                int left = Math.Max(0, Pixel(rect.xMin));
                int right = Math.Min(Resolution - 1, Pixel(rect.xMax));
                int bottom = Math.Max(0, Pixel(rect.yMin));
                int top = Math.Min(Resolution - 1, Pixel(rect.yMax));
                for (int y = bottom; y <= top; y++)
                    for (int x = left; x <= right; x++)
                        pixels[y * Resolution + x] = x == left || x == right || y == bottom || y == top ? outline : wall;
            }
            return pixels;
        }
    }
}

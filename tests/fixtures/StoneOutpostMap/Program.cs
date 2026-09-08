using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BossRush;
using Duckov.MiniMaps;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static List<MiniMapSettings.MapEntry> Maps()
    { return new List<MiniMapSettings.MapEntry> { new MiniMapSettings.MapEntry() }; }
    private static int At(float x, float z)
    { return StoneOutpostMapRaster.Pixel(z) * StoneOutpostMapRaster.Resolution + StoneOutpostMapRaster.Pixel(x); }
    private static readonly Color32 Ground = new Color32(19, 23, 27, 255);
    private static readonly Color32 Grid = new Color32(23, 29, 33, 255);
    private static readonly Color32 Wall = new Color32(171, 184, 191, 255);
    private static readonly Color32 Edge = new Color32(240, 245, 247, 255);

    private static void Main(string[] args)
    {
        var originalMaps = Maps();
        var originalSprite = new Sprite();
        var oldCenter = new Vector3(1, 2, 3);
        var center = new Vector3(2048, 0, 2048);
        var settings = new MiniMapSettings { maps = originalMaps, combinedSprite = originalSprite,
            combinedCenter = oldCenter, combinedSize = 270 };
        var owned = Maps();
        var lease = new StoneOutpostMapDataLease(settings, owned, center, 100);
        Check(ReferenceEquals(settings.maps, owned), "进入未切换地图引用");
        Check(settings.combinedSprite == null && settings.combinedCenter == center && settings.combinedSize == 100,
            "前哨坐标或组合底图未切换");
        Check(originalMaps.Count == 1, "原地图集合被原地修改");
        lease.Dispose();
        Check(ReferenceEquals(settings.maps, originalMaps) && ReferenceEquals(settings.combinedSprite, originalSprite),
            "离场未返还原始引用");
        Check(settings.combinedCenter == oldCenter && settings.combinedSize == 270, "离场未还原坐标范围");
        lease.Dispose();
        Check(ReferenceEquals(settings.maps, originalMaps), "重复释放改变官方状态");
        lease = new StoneOutpostMapDataLease(settings, Maps(), center, 100);
        var newerMaps = Maps();
        var newerSprite = new Sprite();
        settings.maps = newerMaps;
        settings.combinedSprite = newerSprite;
        settings.combinedCenter = new Vector3(5, 6, 7);
        settings.combinedSize = 900;
        lease.Dispose();
        Check(ReferenceEquals(settings.maps, newerMaps) && ReferenceEquals(settings.combinedSprite, newerSprite) &&
            settings.combinedSize == 900 && settings.combinedCenter == new Vector3(5, 6, 7), "覆盖了后来接管的地图");
        lease = new StoneOutpostMapDataLease(settings, Maps(), center, 100);
        settings.combinedSprite = originalSprite;
        settings.combinedCenter = oldCenter;
        settings.combinedSize = 700;
        lease.Dispose();
        Check(ReferenceEquals(settings.combinedSprite, originalSprite) && settings.combinedCenter == oldCenter &&
            settings.combinedSize == 700, "覆盖了其他模块单独更新的数据字段");
        bool rejected = false;
        try { new StoneOutpostMapDataLease(settings, new List<MiniMapSettings.MapEntry>(), center, 100); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected && ReferenceEquals(settings.maps, newerMaps), "空地图拒绝前污染数据");

        Check(StoneOutpostMapRaster.Pixel(-50) == 0 && StoneOutpostMapRaster.Pixel(0) == 256 &&
            StoneOutpostMapRaster.Pixel(50) == 512, "地图米/像素范围错误");
        var footprints = new List<Rect> { Rect.MinMaxRect(-10, 20, -8, 26),
            Rect.MinMaxRect(-60, -60, -45, -45), Rect.MinMaxRect(90, 90, 100, 100) };
        var pixels = StoneOutpostMapRaster.Draw(footprints, Ground, Grid, Wall, Edge);
        Check(pixels.Length == 512 * 512, "地图分辨率错误");
        Check(pixels[At(-9, 23)].r == Wall.r, "西北障碍没有绘制到对应位置");
        Check(pixels[At(-9, -23)].r == Ground.r, "南北轴颠倒");
        Check(pixels[At(9, 23)].r == Ground.r, "东西轴颠倒");
        Check(pixels[0].r == Edge.r, "越界障碍未正确裁剪");
        Check(pixels[At(-10, 21)].r == Edge.r, "障碍轮廓遗漏");
        Check(pixels[At(0, 1)].r == Grid.r, "十米网格缺失");
        Check(pixels[At(1, 1)].r == Ground.r, "开放区域误填充");
        if (args.Length == 2)
        {
            footprints.Clear();
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(args[0])))
                foreach (JsonElement box in document.RootElement.GetProperty("collision_boxes").EnumerateArray())
                {
                    if (box.GetProperty("name").GetString() == "COL_Ground") continue;
                    JsonElement c = box.GetProperty("center_blender"), s = box.GetProperty("size_blender");
                    float x = c[0].GetSingle(), z = c[1].GetSingle(), w = s[0].GetSingle(), h = s[1].GetSingle();
                    footprints.Add(Rect.MinMaxRect(x - w / 2, z - h / 2, x + w / 2, z + h / 2));
                }
            pixels = StoneOutpostMapRaster.Draw(footprints, Ground, Grid, Wall, Edge);
            // PPM 为俯视北朝上的查看图；Unity 纹理像素本身仍为左下原点。
            using (var stream = new BinaryWriter(File.Create(args[1])))
            {
                stream.Write(System.Text.Encoding.ASCII.GetBytes("P6\n512 512\n255\n"));
                for (int y = 511; y >= 0; y--)
                    for (int x = 0; x < 512; x++)
                    { Color32 p = pixels[y * 512 + x]; stream.Write(p.r); stream.Write(p.g); stream.Write(p.b); }
            }
        }
        Console.WriteLine("StoneOutpostMap: PASS (" + assertions + " assertions)");
    }
}

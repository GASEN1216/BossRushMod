using System;
using System.IO;
using System.Linq;
using BossRush;
using UnityEngine;
using UObject = UnityEngine.Object;

// 生产 ModSprites（按路径取图的唯一入口）与借用它的 MapThumbnailCache。
internal static class ResourceOwnershipTests
{
    private static int assertions;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("ModSprites: " + message);
        assertions++;
    }

    internal static void Run(string directory)
    {
        string root = Path.Combine(directory, "mod-sprites");
        ModBehaviour.ModPath = root;
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Items"));
        ModSprites.ResetStaticCaches();
        ProductionIconCache.ResetStaticCaches();
        CheckDecodeAndCache(root);
        CheckRejectedPaths();
        CheckFailures(root);
        CheckReleaseWithBundle(root);
        CheckMapThumbnails(root);
        Console.WriteLine("ResourceOwnership: " + assertions + " PASS (production ModSprites + MapThumbnailCache, Unity doubles)");
    }

    private static void CheckDecodeAndCache(string root)
    {
        string path = "Assets/Items/test.png";
        File.WriteAllBytes(Path.Combine(root, path), new byte[] { 1 });
        Sprite.CreationMode = 0;
        int before = Texture2D.DecodeCalls;
        Sprite sprite = ModSprites.Get(path);
        Check(sprite != null && !sprite.texture.isReadable, "pixels released after decode");
        Check(sprite.name == "test" && sprite.texture.name == "test", "decoded sprite is named after the file like packed sprites");
        Check(sprite.texture.wrapMode == TextureWrapMode.Clamp && !sprite.texture.mipChain, "decoded texture uses UI settings");
        Check(sprite.hideFlags == HideFlags.DontSave && sprite.texture.hideFlags == HideFlags.DontSave, "decoded objects do not persist");
        Check(ReferenceEquals(sprite, ModSprites.Get(path.Replace('/', '\\'))), "separator aliases share cache");
        Check(ReferenceEquals(sprite, ModSprites.Get("ASSETS/items/TEST.png")), "case aliases share cache");
        for (int i = 0; i < 30; i++) Check(ReferenceEquals(sprite, ModSprites.Get(path)), "repeated requests share one sprite");
        Check(Texture2D.DecodeCalls == before + 1, "one decode for repeated requests");
        Check(ModSprites.Exists(path) && !ModSprites.Exists("Assets/Items/absent.png"), "probe sees cached and missing art");

        // Mod 停用再启用：缓存里的图成了 Unity-null，按路径重新取到新的。
        UObject.Destroy(sprite);
        UObject.Destroy(sprite.texture);
        Sprite reloaded = ModSprites.Get(path);
        Check(reloaded != null && !ReferenceEquals(sprite, reloaded), "Unity-null cache entry reloads");
        ModSprites.ResetStaticCaches();
        ModSprites.ResetStaticCaches();
        Check(sprite.destroyCalls == 1 && sprite.texture.destroyCalls == 1, "already-destroyed objects are not destroyed again");
        Check(reloaded.destroyCalls == 1 && reloaded.texture.destroyCalls == 1, "owned native objects released once");
    }

    private static void CheckRejectedPaths()
    {
        int created = UObject.Created.Count;
        int reads = ModBehaviour.PathReads;
        foreach (string bad in new[] { null, "", "../x.png", "Assets/../x.png", "/abs.png", "C:/abs.png", @"C:\abs.png", @"\\server\x.png" })
            Check(ModSprites.Get(bad) == null && ModSprites.GetUnpacked(bad) == null && !ModSprites.Exists(bad), "rejected path: " + bad);
        Check(UObject.Created.Count == created && ModBehaviour.PathReads == reads, "rejected paths never touch disk or allocate");
    }

    private static void CheckFailures(string root)
    {
        string path = "Assets/Items/broken.png";
        File.WriteAllBytes(Path.Combine(root, path), new byte[0]);
        int objects = UObject.Created.Count;
        Check(ModSprites.Get(path) == null && UObject.Created.Count == objects, "empty file returns null without allocating");
        foreach (int failure in new[] { 0, 1, 2, 3 })
        {
            Sprite.CreationMode = failure == 2 ? 1 : failure == 3 ? 2 : 0;
            File.WriteAllBytes(Path.Combine(root, path), new byte[] { (byte)(failure == 0 ? 0 : failure == 1 ? 2 : 1) });
            objects = UObject.Created.Count;
            Check(ModSprites.Get(path) == null, "decode/create failure returns null");
            Check(UObject.Created.Skip(objects).All(o => o.destroyed && o.destroyCalls == 1), "failure frees every partial allocation");
        }
        Sprite.CreationMode = 0;
        File.WriteAllBytes(Path.Combine(root, path), new byte[] { 1 });
        Check(ModSprites.Get(path) != null, "a fixed file loads after earlier failures (misses are not cached)");
        ModSprites.ResetStaticCaches();
    }

    private static void CheckReleaseWithBundle(string root)
    {
        // 正式构建且生产包在：Get 只借包里的图、散图一律不读；GetUnpacked 照读散图。
        ModSprites.ResetStaticCaches();
        ProductionIconCache.ResetStaticCaches();
        string bundleFile = Path.Combine(root, ProductionIconCache.BundleRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(bundleFile));
        File.WriteAllText(bundleFile, "fixture");
        AssetBundle pack = new AssetBundle { name = "production_icons" };
        Texture2D packedTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Sprite packed = new Sprite { texture = packedTexture, name = "packed" };
        pack.Sprites["assets/items/packed.png"] = packed;
        pack.Sprites["assets/items/probe.png"] = new Sprite { texture = packedTexture, name = "probe" };
        AssetBundle.FileResult = pack;
        try
        {
            string loose = "Assets/Items/test.png";
            int decodes = Texture2D.DecodeCalls;
            Check(ReferenceEquals(packed, ModSprites.Get("Assets/Items/packed.png")), "packed sprite is borrowed from the production bundle");
            Check(ModSprites.Get(loose) == null && Texture2D.DecodeCalls == decodes, "release build with bundle never decodes loose PNG");
            int loads = pack.AssetLoads;
            Check(ModSprites.Exists("Assets/Items/probe.png") && pack.AssetLoads == loads, "probe reads the bundle directory without loading the asset");
            Check(!ModSprites.Exists(loose), "probe follows Get: loose PNG does not count in release with bundle");
            File.WriteAllBytes(Path.Combine(root, "Assets", "Items", "art.jpg"), new byte[] { 1 });
            Sprite art = ModSprites.GetUnpacked("Assets/Items/art.jpg");
            Check(art != null && Texture2D.DecodeCalls == decodes + 1, "unpacked art is decoded even with the bundle present");
            Check(ReferenceEquals(art, ModSprites.GetUnpacked("Assets\\Items\\art.jpg")), "unpacked art is cached");
            ModSprites.ResetStaticCaches();
            Check(packed.destroyCalls == 0 && packedTexture.destroyCalls == 0, "ModSprites never destroys borrowed packed sprites");
            Check(art.destroyCalls == 1 && art.texture.destroyCalls == 1, "ModSprites releases unpacked art");
            ProductionIconCache.ResetStaticCaches();
            Check(packed.destroyCalls == 1 && pack.UnloadCalls == 1, "the production bundle stays the owner of packed sprites");
        }
        finally
        {
            AssetBundle.FileResult = null;
            File.Delete(bundleFile);
            ProductionIconCache.ResetStaticCaches();
        }
    }

    private static void CheckMapThumbnails(string root)
    {
        File.WriteAllBytes(Path.Combine(root, "map.png"), new byte[] { 1 });
        Sprite map = MapThumbnailCache.GetOrLoad("map.png");
        Check(map != null && !map.texture.isReadable, "map thumbnail found in the mod root releases CPU pixels");
        Check(ReferenceEquals(map, MapThumbnailCache.GetOrLoad("map.png")), "map cache shares sprite");
        Directory.CreateDirectory(Path.Combine(root, "Assets", "preview"));
        File.WriteAllBytes(Path.Combine(root, "Assets", "preview", "pre.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "pre.png"), new byte[] { 1 });
        Check(ReferenceEquals(MapThumbnailCache.GetOrLoad("pre.png"), ModSprites.Get("Assets/preview/pre.png")), "Assets/preview is searched first");
        Check(MapThumbnailCache.GetOrLoad("missing.png") == null, "missing thumbnail returns null");
        int reads = ModBehaviour.PathReads;
        Check(MapThumbnailCache.GetOrLoad("missing.png") == null && ModBehaviour.PathReads == reads, "missing thumbnail is remembered, not re-probed");
        UObject.Destroy(map);
        Sprite replacement = MapThumbnailCache.GetOrLoad("map.png");
        Check(replacement != null && !ReferenceEquals(map, replacement), "destroyed map cache reloads");
        MapThumbnailCache.ResetStaticCaches();
        MapThumbnailCache.ResetStaticCaches();
        Check(replacement.destroyCalls == 0, "thumbnail cache only drops references");
        ModSprites.ResetStaticCaches();
        Check(map.destroyCalls == 1 && map.texture.destroyCalls == 1, "ModSprites releases the old thumbnail texture once");
        Check(replacement.destroyCalls == 1 && replacement.texture.destroyCalls == 1, "ModSprites releases the replacement once");
    }
}

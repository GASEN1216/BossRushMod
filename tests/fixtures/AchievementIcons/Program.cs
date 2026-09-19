using System;
using System.IO;
using System.Linq;
using BossRush;
using UnityEngine;
using UnityObject = UnityEngine.Object;

internal static class Program
{
    private static string root;
    private static int passed;

    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        passed++;
    }

    private static void Reset(string name)
    {
        AchievementIconLoader.ResetStaticCaches();
        AssetBundle.Loaded.Clear();
        UnityObject.Created.Clear();
        AssetBundle.FindCalls = AssetBundle.LoadCalls = Texture2D.DecodeCalls = 0;
        Sprite.CreationMode = 0;
        ModBehaviour.PathReads = 0;
        ModBehaviour.ModPath = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(ModBehaviour.ModPath, "Assets", "achievement"));
    }

    private static void Png(string id, byte marker)
    {
        File.WriteAllBytes(Path.Combine(ModBehaviour.ModPath, "Assets", "achievement", id + ".png"), new[] { marker });
    }

    private static AssetBundle Bundle(string id, bool hasSprite)
    {
        AssetBundle bundle = new AssetBundle { name = "achievement_icons" };
        Texture2D texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        string assetPath = "assets/achievementicons/" + id.ToLowerInvariant() + ".png";
        bundle.Textures[assetPath] = texture;
        if (hasSprite) bundle.Sprites[assetPath] = new Sprite { texture = texture };
        AssetBundle.Loaded.Add(bundle);
        return bundle;
    }

    private static void CheckPngPriorityAndCache()
    {
        Reset("png-priority");
        AssetBundle bundle = Bundle("new_icon", true);
        Png("new_icon", 1);
        Sprite sprite = AchievementIconLoader.GetSprite("new_icon");
        Assert(sprite != null && sprite.texture.decoded, "PNG is preferred over an existing bundle sprite");
        Assert(AssetBundle.FindCalls == 0 && AssetBundle.LoadCalls == 0, "valid PNG avoids bundle discovery and load");
        Assert(ReferenceEquals(sprite, AchievementIconLoader.GetSprite("new_icon")), "sprite cache returns the same instance");
        Assert(ReferenceEquals(sprite.texture, AchievementIconLoader.GetTexture("new_icon")), "texture request shares PNG sprite texture");
        Assert(ReferenceEquals(sprite.texture, AchievementIconLoader.GetTexture("new_icon")), "texture cache returns the same instance");
        Assert(Texture2D.DecodeCalls == 1, "cached PNG is decoded once");
        Assert(!sprite.texture.mipChain && sprite.texture.wrapMode == TextureWrapMode.Clamp, "PNG uses UI texture settings");
        Assert(sprite.hideFlags == HideFlags.DontSave && sprite.texture.hideFlags == HideFlags.DontSave, "runtime PNG objects do not persist");
        Texture2D pngTexture = sprite.texture;
        AchievementIconLoader.ClearCache();
        AchievementIconLoader.ClearCache();
        Assert(sprite.destroyCalls == 1 && pngTexture.destroyCalls == 1, "PNG cleanup destroys both owned objects once");
        Assert(bundle.Sprites.Values.All(x => x.destroyCalls == 0) && bundle.Textures.Values.All(x => x.destroyCalls == 0), "PNG cleanup leaves unused bundle assets alone");
        Sprite refreshed = AchievementIconLoader.GetSprite("new_icon");
        Assert(refreshed != null && !ReferenceEquals(sprite, refreshed), "PNG reload succeeds after repeated clear");
        AchievementIconLoader.Unload();
        AchievementIconLoader.Unload();
        Assert(refreshed.destroyCalls == 1 && refreshed.texture.destroyCalls == 1, "repeated unload releases reloaded PNG once");
    }

    private static void CheckTextureFirstAndDestroyedCache()
    {
        Reset("texture-first");
        Png("tex_first", 1);
        Texture2D texture = AchievementIconLoader.GetTexture("tex_first");
        Assert(texture != null && texture.decoded, "texture-first access loads PNG without bundle");
        Sprite sprite = AchievementIconLoader.GetSprite("tex_first");
        Assert(ReferenceEquals(sprite.texture, texture), "texture-first access populates sprite cache");
        UnityObject.Destroy(sprite);
        UnityObject.Destroy(texture);
        Texture2D reloaded = AchievementIconLoader.GetTexture("tex_first");
        Assert(reloaded != null && !ReferenceEquals(reloaded, texture), "destroyed cached Unity objects are reloaded");
        AchievementIconLoader.ClearCache();
        Assert(sprite.destroyCalls == 1 && texture.destroyCalls == 1, "already-destroyed objects are not destroyed again");
        Assert(reloaded.destroyCalls == 1, "replacement PNG is owned");
    }

    private static void CheckBundleOwnership()
    {
        Reset("bundle-sprite");
        AssetBundle bundle = Bundle("legacy_sprite", true);
        Sprite borrowed = bundle.Sprites.Values.Single();
        Assert(ReferenceEquals(borrowed, AchievementIconLoader.GetSprite("legacy_sprite")), "missing PNG falls back to legacy Sprite");
        Assert(ReferenceEquals(borrowed.texture, AchievementIconLoader.GetTexture("legacy_sprite")), "legacy Sprite texture is shared");
        AchievementIconLoader.ClearCache();
        AchievementIconLoader.ClearCache();
        Assert(borrowed.destroyCalls == 0 && borrowed.texture.destroyCalls == 0, "clear does not directly destroy borrowed Sprite or Texture");
        Assert(ReferenceEquals(borrowed, AchievementIconLoader.GetSprite("legacy_sprite")), "legacy Sprite can be read after clear");
        AchievementIconLoader.Unload();
        AchievementIconLoader.Unload();
        Assert(bundle.UnloadCalls == 1, "legacy bundle unload remains idempotent");

        Reset("bundle-texture");
        bundle = Bundle("LEGACY_TEXTURE", false);
        Texture2D borrowedTexture = bundle.Textures.Values.Single();
        Sprite owned = AchievementIconLoader.GetSprite("LEGACY_TEXTURE");
        Assert(owned != null && ReferenceEquals(owned.texture, borrowedTexture), "legacy Texture creates compatible Sprite using invariant lowercase path");
        AchievementIconLoader.ClearCache();
        AchievementIconLoader.ClearCache();
        Assert(owned.destroyCalls == 1, "Sprite created from a bundle Texture is owned and destroyed once");
        Assert(borrowedTexture.destroyCalls == 0, "bundle Texture is not directly destroyed by cache cleanup");
        Sprite second = AchievementIconLoader.GetSprite("LEGACY_TEXTURE");
        Assert(second != null && !ReferenceEquals(second, owned), "legacy Texture can create a fresh Sprite after clear");
    }

    private static void CheckFailedPngCleanup(byte marker, int creationMode, string name)
    {
        Reset(name);
        AssetBundle bundle = Bundle("broken", true);
        Sprite borrowed = bundle.Sprites.Values.Single();
        Png("broken", marker);
        Sprite.CreationMode = creationMode;
        Assert(ReferenceEquals(borrowed, AchievementIconLoader.GetSprite("broken")), name + ": failed PNG falls back to bundle");
        Texture2D failed = UnityObject.Created.OfType<Texture2D>().Single(x => !ReferenceEquals(x, borrowed.texture));
        Assert(failed.destroyCalls == 1, name + ": failed allocation is destroyed immediately");
        Assert(borrowed.destroyCalls == 0 && borrowed.texture.destroyCalls == 0, name + ": fallback remains valid");
        AchievementIconLoader.ClearCache();
        Assert(failed.destroyCalls == 1, name + ": failed allocation was not retained");
    }

    private static void CheckMissingAndInvalidNames()
    {
        Reset("invalid-names");
        string[] invalid = { null, "", "../new_icon", @"..\new_icon", "folder/new_icon", @"folder\new_icon", @"C:\new_icon", "new_icon.png", "new:icon", ".", "..", "new icon" };
        foreach (string id in invalid)
        {
            Assert(AchievementIconLoader.GetSprite(id) == null, "invalid Sprite path rejected: " + id);
            Assert(AchievementIconLoader.GetTexture(id) == null, "invalid Texture path rejected: " + id);
        }
        Assert(ModBehaviour.PathReads == 0 && AssetBundle.FindCalls == 0, "invalid names do not reach filesystem or bundle discovery");
        Assert(AchievementIconLoader.GetSprite("absent") == null, "missing PNG and bundle returns null");
        Assert(AchievementIconLoader.GetTexture("absent") == null, "missing PNG and bundle texture returns null");
        Assert(UnityObject.Created.Count == 0, "missing icons allocate no Unity objects");
        Png("Icon_12-A", 1);
        Assert(AchievementIconLoader.GetSprite("Icon_12-A") != null, "valid safe ID loads even after an earlier bundle miss");
    }

    public static int Main(string[] args)
    {
        root = Path.Combine(args[0], Guid.NewGuid().ToString("N"));
        CheckPngPriorityAndCache();
        CheckTextureFirstAndDestroyedCache();
        CheckBundleOwnership();
        CheckFailedPngCleanup(0, 0, "decode-rejected");
        CheckFailedPngCleanup(2, 0, "decode-exception");
        CheckFailedPngCleanup(1, 1, "sprite-null");
        CheckFailedPngCleanup(1, 2, "sprite-exception");
        CheckMissingAndInvalidNames();
        AchievementIconLoader.ResetStaticCaches();
        Console.WriteLine("AchievementIcons: " + passed + " PASS");
        return 0;
    }
}

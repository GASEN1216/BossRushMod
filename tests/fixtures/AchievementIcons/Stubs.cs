using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BossRush
{
    // This fixture exercises ownership/decoding; native asynchronous lifecycle has its own ResourceProduction fixture.
    internal static class ResourceBundleLoader
    {
        internal static UnityEngine.AssetBundle LoadFromFile(string path) { return UnityEngine.AssetBundle.LoadFromFile(path); }
        internal static System.Collections.IEnumerator Prepare(string path, bool prefabs, Func<bool> cancelled, Action consumer)
        { if (!cancelled()) consumer(); yield break; }
    }
    internal static class ModBehaviour
    {
        internal static string ModPath;
        internal static int PathReads;
        public static string GetModPath() { PathReads++; return ModPath; }
        public static void DevLog(string message) { }
        public static void LogError(string message) { throw new Exception(message); }
    }
}

namespace UnityEngine
{
    public static class Debug { public static void LogWarning(string message) { } }
    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public bool destroyed;
        public int destroyCalls;
        public static readonly List<Object> Created = new List<Object>();
        public Object() { Created.Add(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.destroyCalls++;
            value.destroyed = true;
        }
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
    }
    public enum HideFlags { None, DontSave }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public struct Rect { public Rect(float x, float y, float width, float height) { } }
    public struct Vector2 { public Vector2(float x, float y) { } }
    public class Texture2D : Object
    {
        public int width;
        public int height;
        public bool mipChain;
        public TextureWrapMode wrapMode;
        public bool decoded;
        public bool isReadable = true;
        public static int DecodeCalls;
        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        { this.width = width; this.height = height; this.mipChain = mipChain; }
        // The decoder double accepts marker 1, rejects marker 0, and throws for marker 2.
        public bool LoadImage(byte[] data, bool markNonReadable = false)
        {
            DecodeCalls++;
            if (data.Length > 0 && data[0] == 2) throw new InvalidOperationException("decode failure");
            if (data.Length == 0 || data[0] != 1) return false;
            decoded = true;
            isReadable = !markNonReadable;
            width = height = 16;
            return true;
        }
    }
    public class Sprite : Object
    {
        public Texture2D texture;
        public static int CreationMode;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot)
        {
            if (CreationMode == 1) return null;
            if (CreationMode == 2) throw new InvalidOperationException("sprite creation failure");
            return new Sprite { texture = texture };
        }
    }
    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable = false)
        { return texture.LoadImage(data, markNonReadable); }
    }
    public class AssetBundle : Object
    {
        public static readonly List<AssetBundle> Loaded = new List<AssetBundle>();
        public static int FindCalls;
        public static int LoadCalls;
        public int UnloadCalls;
        public readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        public readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        public static IEnumerable<AssetBundle> GetAllLoadedAssetBundles() { FindCalls++; return Loaded; }
        public static AssetBundle LoadFromFile(string path) { LoadCalls++; return null; }
        public string[] GetAllAssetNames() { return new string[0]; }
        public T LoadAsset<T>(string path) where T : Object
        {
            if (typeof(T) == typeof(Sprite)) return Sprites.TryGetValue(path, out Sprite sprite) ? sprite as T : null;
            return Textures.TryGetValue(path, out Texture2D texture) ? texture as T : null;
        }
        public void Unload(bool unloadAllLoadedObjects)
        {
            UnloadCalls++;
            if (unloadAllLoadedObjects)
            {
                foreach (Sprite sprite in Sprites.Values) Destroy(sprite);
                foreach (Texture2D texture in Textures.Values) Destroy(texture);
            }
            Loaded.Remove(this);
        }
    }
}

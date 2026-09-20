using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>按旧 PNG 相对路径借用压缩 Sprite；Bundle 是唯一原生资源 owner。</summary>
    internal static class ProductionIconCache
    {
        internal const string BundleRelativePath = "Assets/ui/production_icons";
        private static AssetBundle bundle;
        private static bool attempted;
        private static readonly HashSet<UnityEngine.Object> borrowed = new HashSet<UnityEngine.Object>();
        internal static bool IsBorrowed(UnityEngine.Object value) { return value != null && borrowed.Contains(value); }
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        internal static Sprite Get(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            string key = relativePath.Replace('\\', '/').ToLowerInvariant();
            if (key.Contains("..") || !key.StartsWith("assets/", StringComparison.Ordinal)) return null;
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite) && sprite != null) return sprite;
            EnsureLoaded();
            if (bundle == null) return null;
            try { sprite = bundle.LoadAsset<Sprite>(key); }
            catch (Exception e) { ModBehaviour.DevLog("[ProductionIconCache] " + key + ": " + e.Message); return null; }
            if (sprite != null) { sprites[key] = sprite; borrowed.Add(sprite); borrowed.Add(sprite.texture); }
            return sprite;
        }

        internal static bool AllowRawFallback
        {
            get
            {
#if BOSSRUSH_DEV
                return true;
#else
                EnsureLoaded();
                return bundle == null;
#endif
            }
        }

        private static void EnsureLoaded()
        {
            if (bundle != null || attempted) return;
            attempted = true;
            string path = Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath);
            try { if (File.Exists(path)) bundle = ResourceBundleLoader.LoadFromFile(path); }
            catch (Exception e) { Debug.LogWarning("[ProductionIconCache] " + e.Message); }
        }

        internal static IEnumerator Prepare(Func<bool> cancelled)
        {
            if (bundle != null) yield break;
            yield return ResourceBundleLoader.Prepare(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath), false, cancelled, EnsureLoaded);
        }

        internal static void ResetStaticCaches()
        {
            sprites.Clear();
            borrowed.Clear();
            if (bundle != null) bundle.Unload(true);
            bundle = null; attempted = false;
        }
    }
}

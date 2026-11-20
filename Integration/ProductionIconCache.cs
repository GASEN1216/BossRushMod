using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 按旧 PNG 相对路径借用压缩 Sprite；Bundle 是唯一原生资源 owner。
    /// 业务代码不直接调这里，按路径取图统一走 <see cref="ModSprites"/>。
    /// </summary>
    internal static class ProductionIconCache
    {
        internal const string BundleRelativePath = "Assets/ui/production_icons";
        private static AssetBundle bundle;
        private static bool attempted;
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        internal static Sprite Get(string relativePath)
        {
            string key = Key(relativePath);
            if (key == null) return null;
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite) && sprite != null) return sprite;
            EnsureLoaded();
            if (bundle == null) return null;
            try { sprite = bundle.LoadAsset<Sprite>(key); }
            catch (Exception e) { ModBehaviour.DevLog("[ProductionIconCache] " + key + ": " + e.Message); return null; }
            if (sprite != null) sprites[key] = sprite;
            return sprite;
        }

        /// <summary>只读探测：包目录里有没有这张图。只查目录，不加载资源、不写缓存。</summary>
        internal static bool Contains(string relativePath)
        {
            string key = Key(relativePath);
            if (key == null) return false;
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite) && sprite != null) return true;
            EnsureLoaded();
            if (bundle == null) return false;
            try { return bundle.Contains(key); }
            catch (Exception e) { ModBehaviour.DevLog("[ProductionIconCache] " + key + ": " + e.Message); return false; }
        }

        internal static bool AllowRawFallback
        {
            get
            {
#if BOSSRUSH_DEV
                return true;
#else
                EnsureLoaded();
                return bundle == null && attempted;
#endif
            }
        }

        private static string Key(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            string key = relativePath.Replace('\\', '/').ToLowerInvariant();
            if (key.Contains("..") || !key.StartsWith("assets/", StringComparison.Ordinal)) return null;
            return key;
        }

        private static void EnsureLoaded()
        {
            if (bundle != null || attempted) return;
            string path = Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath);
            try
            {
                if (File.Exists(path)) bundle = ResourceBundleLoader.LoadFromFile(path);
                // 旧宿主的取消请求仍可能在排空。这次查询不构成缺包，也不能让散图进正式缓存。
                attempted = bundle != null || !ResourceBundleLoader.IsPreparationDraining(path);
            }
            catch (Exception e)
            {
                attempted = true;
                Debug.LogWarning("[ProductionIconCache] " + e.Message);
            }
        }

        internal static IEnumerator Prepare(Func<bool> cancelled)
        {
            if (bundle != null) yield break;
            yield return ResourceBundleLoader.Prepare(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath), false, cancelled, () =>
            {
                attempted = false;
                EnsureLoaded();
            }, alreadyLoaded: () => bundle != null);
        }

        internal static void ResetStaticCaches()
        {
            sprites.Clear();
            if (bundle != null) bundle.Unload(true);
            bundle = null; attempted = false;
        }
    }
}

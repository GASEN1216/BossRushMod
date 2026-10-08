using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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

        /// <summary>
        /// 复制一份生产包贴图，供跨 Mod 热重载仍要持有图标的调用方自己拥有。
        /// 生产包贴图是压缩且不可读的：Object.Instantiate 只拷出没有像素的空壳（Player.log「No texture data
        /// available」），图标显示为空白（2026-10-08 星阙 / 空仓地雷盒实测）。这里按原格式、尺寸与 mip 新建贴图，
        /// 在 GPU 上整块复制，不读回 CPU。平台不支持或复制失败时返回 null，调用方改用借出的 Sprite 或程序化图。
        /// </summary>
        internal static Texture2D CloneTexture(Texture2D source, string name)
        {
            if (source == null || SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None) return null;
            Texture2D copy = null;
            try
            {
                copy = new Texture2D(source.width, source.height, source.graphicsFormat, source.mipmapCount,
                    TextureCreationFlags.None);
                copy.name = name;
                copy.filterMode = source.filterMode;
                copy.wrapMode = source.wrapMode;
                copy.anisoLevel = source.anisoLevel;
                Graphics.CopyTexture(source, copy);
                return copy;
            }
            catch (Exception e)
            {
                if (copy != null) UnityEngine.Object.Destroy(copy);
                ModBehaviour.DevLog("[ProductionIconCache] 贴图复制失败 " + name + ": " + e.Message);
                return null;
            }
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

// ============================================================================
// MapThumbnailCache.cs - 地图选择缩略图缓存
// ============================================================================
// 模块说明：
//   地图选择菜单的自定义背景图（缩略图）此前每次打开菜单都重新 File.ReadAllBytes
//   + new Texture2D + Sprite.Create，N 张图 = N 次同步读盘 + N 次纹理上传，全压在
//   菜单打开帧，且纹理从不复用 = 内存泄漏。
//   本缓存按 imageName 复用已加载的 Sprite/Texture2D，加载一次永久复用，
//   并在 ResetStaticCaches 时显式销毁纹理，避免泄漏。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 地图选择缩略图缓存（BossRush 与僵尸模式共用）。
    /// </summary>
    public static class MapThumbnailCache
    {
        // imageName -> Sprite（加载一次复用）。null 值也缓存，表示「该图找不到/加载失败」，
        // 避免反复对不存在的文件做磁盘探测。
        private static readonly Dictionary<string, Sprite> spriteCache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        // 同时持有创建出来的纹理，便于 Reset 时显式销毁，杜绝泄漏。
        private static readonly List<Texture2D> ownedTextures = new List<Texture2D>();

        /// <summary>
        /// 按文件名加载缩略图，命中缓存则直接复用。
        /// </summary>
        /// <param name="modPath">Mod 根目录</param>
        /// <param name="imageName">图片文件名</param>
        /// <returns>Sprite，找不到或失败返回 null（结果同样会被缓存）</returns>
        public static Sprite GetOrLoad(string modPath, string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
            {
                return null;
            }

            Sprite cached;
            if (spriteCache.TryGetValue(imageName, out cached))
            {
                // 命中缓存（包含「曾经加载失败」的 null 记录）。
                // 防御：纹理可能在场景卸载时被引擎回收，若 sprite 已失效则重新加载。
                if (cached != null)
                {
                    return cached;
                }

                // null 缓存项：仅在我们确实记录过失败时直接返回 null。
                return null;
            }

            Sprite loaded = LoadFromDisk(modPath, imageName);
            spriteCache[imageName] = loaded; // 同时缓存成功与失败结果
            return loaded;
        }

        private static Sprite LoadFromDisk(string modPath, string imageName)
        {
            try
            {
                if (string.IsNullOrEmpty(modPath))
                {
                    return null;
                }

                // 尝试 Assets/preview 子目录 -> Assets -> 根目录
                string imagePath = Path.Combine(modPath, "Assets", "preview", imageName);
                if (!File.Exists(imagePath))
                {
                    imagePath = Path.Combine(modPath, "Assets", imageName);
                    if (!File.Exists(imagePath))
                    {
                        imagePath = Path.Combine(modPath, imageName);
                        if (!File.Exists(imagePath))
                        {
                            return null;
                        }
                    }
                }

                byte[] imageData = File.ReadAllBytes(imagePath);
                Texture2D texture = new Texture2D(2, 2);
                if (ImageConversion.LoadImage(texture, imageData))
                {
                    ownedTextures.Add(texture);
                    return Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                }

                // 加载失败：销毁刚分配的占位纹理
                UnityEngine.Object.Destroy(texture);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] MapThumbnailCache 加载失败(" + imageName + "): " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 清空缓存并销毁持有的纹理（卸载时调用，杜绝纹理泄漏）。
        /// </summary>
        public static void ResetStaticCaches()
        {
            try
            {
                for (int i = 0; i < ownedTextures.Count; i++)
                {
                    Texture2D tex = ownedTextures[i];
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] MapThumbnailCache 销毁纹理异常: " + e.Message);
            }

            ownedTextures.Clear();
            spriteCache.Clear();
        }
    }
}

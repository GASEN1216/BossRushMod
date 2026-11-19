// ============================================================================
// MapThumbnailCache.cs - 地图选择缩略图缓存
// ============================================================================
// 模块说明：
//   地图选择菜单的自定义背景图（缩略图）此前每次打开菜单都重新 File.ReadAllBytes
//   + new Texture2D + Sprite.Create，N 张图 = N 次同步读盘 + N 次纹理上传，全压在
//   菜单打开帧，且纹理从不复用 = 内存泄漏。
//   现在取图走 ModSprites（生产图标包优先，Dev 或缺包读散图，解码出来的图由它持有并在卸载时销毁），
//   本缓存只按 imageName 记住结果（含「找不到」），菜单再开不再探盘。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 地图选择缩略图缓存（BossRush 与僵尸模式共用）。
    /// </summary>
    public static class MapThumbnailCache
    {
        // imageName -> Sprite（借自 ModSprites，不归这里销毁）。null 值也缓存，表示「该图找不到/加载失败」，
        // 避免反复对不存在的文件做磁盘探测。
        private static readonly Dictionary<string, Sprite> spriteCache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>
        /// 按文件名取缩略图，命中缓存则直接复用。依次找 Assets/preview、Assets、Mod 根目录。
        /// </summary>
        /// <param name="imageName">图片文件名</param>
        /// <returns>Sprite，找不到或失败返回 null（结果同样会被缓存）</returns>
        public static Sprite GetOrLoad(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
            {
                return null;
            }

            Sprite cached;
            if (spriteCache.TryGetValue(imageName, out cached))
            {
                // 命中缓存（包含「曾经加载失败」的 null 记录）。
                // 防御：图可能随 Mod 卸载被销毁，若 sprite 已失效则重新取。
                if (cached != null)
                {
                    return cached;
                }

                // Unity 已销毁对象也 == null，只有真正的 null 才是失败缓存。
                if (ReferenceEquals(cached, null)) return null;
            }

            Sprite loaded = ModSprites.Get("Assets/preview/" + imageName)
                ?? ModSprites.Get("Assets/" + imageName)
                ?? ModSprites.Get(imageName);
            spriteCache[imageName] = loaded; // 同时缓存成功与失败结果
            return loaded;
        }

        /// <summary>
        /// 清空缓存（卸载时调用）。缩略图借自 ModSprites，由它释放。
        /// </summary>
        public static void ResetStaticCaches()
        {
            spriteCache.Clear();
        }
    }
}

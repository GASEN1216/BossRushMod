// ============================================================================
// ModSprites.cs - 按路径取图（Sprite / Texture）的唯一入口
// ============================================================================
// 规则（ModSpritesGuard 守卫）：
//   1. 生产代码按 Mod 根下的相对路径取图一律调这里；只有本文件解码图片字节（LoadImage / ReadAllBytes）。
//      专用 bundle（Mode G / Mode H / 图鉴立绘 / 战役展示包 / 物品包）按资源名 LoadAsset，不算按路径取图。
//   2. Get 的顺序：自身缓存 → production_icons 压缩包（ProductionIconCache 借出）
//      → 不许散图（ProductionIconCache.AllowRawFallback 为 false：正式构建且包在）就返回 null → 解码散图。
//      解码：RGBA32、markNonReadable（不留 CPU 像素副本）、DontSave、Clamp、PPU 100，名字取文件名。
//   3. GetUnpacked 只给不进压缩包、正式构建也必须读散图的美术（目前只有潮闸镇信件插画 jpg）。
//   4. 取到的 Sprite / Texture 对调用方一律是借用的，调用方不 Destroy：
//      压缩包里的归 ProductionIconCache（包卸载时一起销毁），解码出来的归本类。
//      要改采样参数的调用方（云蚋精灵表改点采样）改的是共享贴图，同一路径只给一个用途。
//   5. ResetStaticCaches 挂在 ItemFactory.Shutdown，先于 ProductionIconCache.ResetStaticCaches。
//      缓存里的图变成 Unity-null（Mod 停用后重新启用）时按路径重新取，不留「试过一次」的静态标记。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    internal static class ModSprites
    {
        private const string LogPrefix = "[ModSprites] ";

        // 不缓存「没有」：Dev 下美术边出图边看，补上文件就能取到；需要记住缺图的调用方自己缓存 null。
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Sprite> ownedSprites = new HashSet<Sprite>();
        private static readonly HashSet<Texture2D> ownedTextures = new HashSet<Texture2D>();

        /// <summary>按相对路径取图：压缩包优先，Dev 或缺包时解码散图。取不到返回 null。</summary>
        internal static Sprite Get(string relativePath)
        {
            return Load(relativePath, true);
        }

        /// <summary>不进压缩包的美术：正式构建也直接解码散图。</summary>
        internal static Sprite GetUnpacked(string relativePath)
        {
            return Load(relativePath, false);
        }

        /// <summary>
        /// 只读探测：<see cref="Get"/> 能不能取到这张图。只查缓存、包目录与散图文件是否存在，
        /// 不解码、不加载资源、不写缓存（F3 验收用）。
        /// </summary>
        internal static bool Exists(string relativePath)
        {
            string key = Normalize(relativePath);
            if (key == null) return false;
            Sprite cached;
            if (sprites.TryGetValue(key, out cached) && cached != null) return true;
            try
            {
                if (ProductionIconCache.Contains(key)) return true;
                if (!ProductionIconCache.AllowRawFallback) return false;
                string modPath = ModBehaviour.GetModPath();
                return !string.IsNullOrEmpty(modPath) && File.Exists(Path.Combine(modPath, key));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "探测失败: " + key + " - " + e.Message);
                return false;
            }
        }

        private static Sprite Load(string relativePath, bool packed)
        {
            string key = Normalize(relativePath);
            if (key == null) return null;
            Sprite cached;
            if (sprites.TryGetValue(key, out cached) && cached != null) return cached;
            Sprite sprite = null;
            try
            {
                if (packed)
                {
                    sprite = ProductionIconCache.Get(key);
                    if (sprite == null && !ProductionIconCache.AllowRawFallback) return null;
                }
                if (sprite == null) sprite = Decode(key);
            }
            catch (Exception e)
            {
                // 取图是观感层：任何失败都只返回 null，不把异常抛给面板、Buff 或物品注册。
                ModBehaviour.DevLog(LogPrefix + "取图失败: " + key + " - " + e.Message);
                return null;
            }
            if (sprite != null) sprites[key] = sprite;
            return sprite;
        }

        /// <summary>统一分隔符；只认 Mod 根下的相对路径（绝对路径、盘符与 .. 一律拒绝）。</summary>
        private static string Normalize(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            string key = relativePath.Replace('\\', '/');
            if (key[0] == '/' || key.IndexOf(':') >= 0 || key.Contains("..")) return null;
            return key;
        }

        private static Sprite Decode(string key)
        {
            Texture2D texture = null;
            Sprite sprite = null;
            bool retained = false;
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;
                string fullPath = Path.Combine(modPath, key);
                if (!File.Exists(fullPath)) return null;

                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0) return null;

                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                // 展示用图只由 GPU 采样，不保留解码后的 CPU 像素副本。
                if (!texture.LoadImage(bytes, true)) return null;

                string name = Path.GetFileNameWithoutExtension(key);
                texture.name = name;
                texture.hideFlags = HideFlags.DontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                // 三参数重载的 pixelsPerUnit 就是 100，与压缩包里的 Sprite 一致。
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                if (sprite == null) return null;
                sprite.name = name;
                sprite.hideFlags = HideFlags.DontSave;
                ownedTextures.Add(texture);
                ownedSprites.Add(sprite);
                retained = true;
                return sprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "解码散图失败: " + key + " - " + e.Message);
                return null;
            }
            finally
            {
                // 解码或 Sprite 创建中途失败时，不把未缓存的原生对象留到下一次。
                if (!retained)
                {
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>Mod 卸载：销毁本类解码出来的 Sprite / Texture，清缓存。借来的压缩图归 ProductionIconCache。</summary>
        internal static void ResetStaticCaches()
        {
            sprites.Clear();
            // 已销毁（Unity-null）的跳过，保证每个对象只销毁一次；单个销毁失败不拖住 ItemFactory.Shutdown 的其余清理。
            foreach (Sprite sprite in ownedSprites)
            {
                try { if (sprite != null) UnityEngine.Object.Destroy(sprite); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "销毁 Sprite 失败: " + e.Message); }
            }
            ownedSprites.Clear();
            foreach (Texture2D texture in ownedTextures)
            {
                try { if (texture != null) UnityEngine.Object.Destroy(texture); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "销毁 Texture 失败: " + e.Message); }
            }
            ownedTextures.Clear();
        }
    }
}

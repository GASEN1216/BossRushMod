// ============================================================================
// NPCUIAssetCache.cs - NPC UI 资源缓存
// ============================================================================
// 模块说明：
//   为 NPC UI 资源（爱心序列帧、图标）提供统一缓存，避免重复 I/O 和反复解包。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush.Utils
{
    /// <summary>
    /// NPC UI 资源缓存（Assets/ui/*）
    /// </summary>
    public static class NPCUIAssetCache
    {
        private static readonly Dictionary<string, Sprite[]> spriteSheets =
            new Dictionary<string, Sprite[]>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Sprite> spriteCache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private static readonly object cacheLock = new object();

        /// <summary>
        /// 获取 bundle 中的所有 Sprite（按名称排序，带缓存）
        /// </summary>
        public static bool TryGetAllSprites(string bundleName, out Sprite[] sprites)
        {
            sprites = null;

            if (string.IsNullOrEmpty(bundleName))
            {
                return false;
            }

            lock (cacheLock)
            {
                if (spriteSheets.TryGetValue(bundleName, out sprites) &&
                    sprites != null &&
                    sprites.Length > 0)
                {
                    return true;
                }
            }

            try
            {
                string bundlePath = BuildBundlePath(bundleName);
                if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath))
                {
                    return false;
                }

                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    return false;
                }

                Sprite[] loaded = bundle.LoadAllAssets<Sprite>();
                bundle.Unload(false);

                if (loaded == null || loaded.Length == 0)
                {
                    return false;
                }

                Array.Sort(loaded, (a, b) =>
                {
                    string nameA = a != null ? a.name : string.Empty;
                    string nameB = b != null ? b.name : string.Empty;
                    return string.CompareOrdinal(nameA, nameB);
                });

                lock (cacheLock)
                {
                    spriteSheets[bundleName] = loaded;
                }

                sprites = loaded;
                return true;
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "NPCUIAssetCache.TryGetAllSprites");
                return false;
            }
        }

        /// <summary>
        /// 按候选名称获取单个 Sprite（带缓存，未命中时回退首帧）
        /// </summary>
        public static bool TryGetSprite(string bundleName, out Sprite sprite, params string[] candidateNames)
        {
            sprite = null;

            if (string.IsNullOrEmpty(bundleName))
            {
                return false;
            }

            string key = BuildSpriteCacheKey(bundleName, candidateNames);
            lock (cacheLock)
            {
                if (spriteCache.TryGetValue(key, out sprite) && sprite != null)
                {
                    return true;
                }
            }

            if (!TryGetAllSprites(bundleName, out Sprite[] sprites) || sprites == null || sprites.Length == 0)
            {
                return false;
            }

            sprite = FindBestMatch(sprites, candidateNames);
            if (sprite == null)
            {
                return false;
            }

            lock (cacheLock)
            {
                spriteCache[key] = sprite;
            }

            return true;
        }

        /// <summary>
        /// 清理缓存（场景切换或热重载时可调用）
        /// </summary>
        public static void Clear()
        {
            lock (cacheLock)
            {
                spriteSheets.Clear();
                spriteCache.Clear();
            }
        }

        private static string BuildBundlePath(string bundleName)
        {
            string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
            string modDir = Path.GetDirectoryName(assemblyLocation);
            return Path.Combine(modDir, "Assets", "ui", bundleName);
        }

        private static string BuildSpriteCacheKey(string bundleName, string[] candidateNames)
        {
            if (candidateNames == null || candidateNames.Length == 0)
            {
                return bundleName + "|*";
            }

            return bundleName + "|" + string.Join("|", candidateNames);
        }

        private static Sprite FindBestMatch(Sprite[] sprites, string[] candidateNames)
        {
            if (sprites == null || sprites.Length == 0)
            {
                return null;
            }

            if (candidateNames != null)
            {
                for (int i = 0; i < candidateNames.Length; i++)
                {
                    string candidate = candidateNames[i];
                    if (string.IsNullOrEmpty(candidate))
                    {
                        continue;
                    }

                    for (int j = 0; j < sprites.Length; j++)
                    {
                        Sprite s = sprites[j];
                        if (s != null && string.Equals(s.name, candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            return s;
                        }
                    }
                }
            }

            return sprites[0];
        }
    }
}

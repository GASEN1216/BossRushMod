// ============================================================================
// EquipmentHelperIcon.cs - 占位 Item 图标注入辅助
// ============================================================================
// 模块说明：
//   用户提供的资源只包含贴图（PNG）/ 3D 模型 / 音效，不会提供 Item Prefab、
//   Buff Prefab、Projectile 等"代码层资产"。Item Prefab 一律由占位逻辑克隆生成，
//   外观差异化主要靠"加载用户提供的图标 PNG"完成。
//
//   该辅助类提供多条加载路径：
//   1) 直接从 Assets/Equipment 或 Assets/Items 下读取 PNG 文件（推荐）
//   2) 从已加载或可加载的 Equipment AssetBundle 中读 Sprite/Texture2D
//   3) 从 Items AssetBundle 中读 Sprite/Texture2D（参考 ItemFactory.GetSprite）
//
//   两条路径都失败时，物品继承克隆源的 Sprite，玩家会看到船票/飞行图腾的图标
//   （这是当前已经在跑的 fallback 行为，不算新增 bug）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 占位 Item 的图标注入辅助
    /// </summary>
    public static class EquipmentHelperIcon
    {
        private static string modDirectoryCache = null;
        private static readonly Dictionary<string, AssetBundle> equipmentIconBundles =
            new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> equipmentBundleSprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<AssetBundle> equipmentIconBundlesLoadedByHelper =
            new HashSet<AssetBundle>();

        public static void ResetStaticCaches()
        {
            foreach (AssetBundle bundle in equipmentIconBundlesLoadedByHelper)
            {
                if (bundle == null) continue;
                try { bundle.Unload(false); } catch  { /* best-effort fallback intentionally ignored */ }
            }

            equipmentIconBundlesLoadedByHelper.Clear();
            equipmentIconBundles.Clear();
            equipmentBundleSprites.Clear();
            modDirectoryCache = null;
        }

        private static string GetModDirectory()
        {
            if (modDirectoryCache == null)
            {
                modDirectoryCache = Path.GetDirectoryName(typeof(EquipmentHelperIcon).Assembly.Location);
            }
            return modDirectoryCache;
        }

        /// <summary>
        /// 把用户提供的 PNG 图标加载并设置到 Item 上。
        /// 加载顺序：
        ///   1) Assets/Equipment/{bundle}/{iconAssetName}.png（仅无同名 bundle 文件时适用）
        ///   2) Assets/Equipment/{iconAssetName}.png
        ///   3) Assets/Items/{bundle}/{iconAssetName}.png
        ///   4) Assets/Items/{iconAssetName}.png
        ///   5) Equipment AssetBundle 中的 Sprite/Texture2D
        ///   6) ItemFactory.GetSprite(bundle, iconAssetName)（Items AssetBundle 兼容兜底）
        /// 全部失败时不动 item.Icon，保持克隆源的图标。
        /// </summary>
        public static bool TryInjectIcon(Item item, string bundleName, string iconAssetName)
        {
            if (item == null) return false;
            if (string.IsNullOrEmpty(iconAssetName)) return false;

            try
            {
                Sprite sprite = LoadIconFromPng(bundleName, iconAssetName);

                if (sprite == null && !string.IsNullOrEmpty(bundleName))
                {
                    sprite = LoadIconFromEquipmentBundle(bundleName, iconAssetName);
                }

                if (sprite == null && !string.IsNullOrEmpty(bundleName))
                {
                    // 退路：用户把图标做进 AssetBundle 时，复用 ItemFactory 的现成加载入口
                    try { sprite = ItemFactory.GetSprite(bundleName, iconAssetName); } catch  { /* best-effort fallback intentionally ignored */ }
                }

                if (sprite != null)
                {
                    item.Icon = sprite;
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentHelperIcon] 注入图标失败 " + iconAssetName + ": " + e.Message);
            }
            return false;
        }

        /// <summary>
        /// 从 Assets/Equipment/{bundleName} AssetBundle 中读取 Sprite/Texture2D。
        /// 该路径用于支持模型 bundle 与图标资源同包，避免 PNG 子目录和无扩展名 bundle 文件重名冲突。
        /// </summary>
        private static Sprite LoadIconFromEquipmentBundle(string bundleName, string iconAssetName)
        {
            if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(iconAssetName))
            {
                return null;
            }

            string cacheKey = bundleName + "/" + iconAssetName;
            Sprite cachedSprite;
            if (equipmentBundleSprites.TryGetValue(cacheKey, out cachedSprite))
            {
                return cachedSprite;
            }

            try
            {
                AssetBundle bundle = GetOrLoadEquipmentIconBundle(bundleName);
                if (bundle == null)
                {
                    return null;
                }

                Sprite sprite = bundle.LoadAsset<Sprite>(iconAssetName);
                if (sprite == null)
                {
                    sprite = bundle.LoadAsset<Sprite>(iconAssetName + ".png");
                }

                if (sprite == null)
                {
                    Texture2D texture = bundle.LoadAsset<Texture2D>(iconAssetName);
                    if (texture == null)
                    {
                        texture = bundle.LoadAsset<Texture2D>(iconAssetName + ".png");
                    }

                    if (texture != null)
                    {
                        sprite = Sprite.Create(
                            texture,
                            new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f),
                            100f);
                        if (sprite != null)
                        {
                            sprite.hideFlags = HideFlags.DontSave;
                            sprite.name = iconAssetName;
                        }
                    }
                }

                if (sprite != null)
                {
                    equipmentBundleSprites[cacheKey] = sprite;
                    ModBehaviour.DevLog("[EquipmentHelperIcon] 加载 Equipment Bundle 图标成功: " + bundleName + "/" + iconAssetName);
                }

                return sprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentHelperIcon] 读取 Equipment Bundle 图标失败 " + cacheKey + ": " + e.Message);
                return null;
            }
        }

        private static AssetBundle GetOrLoadEquipmentIconBundle(string bundleName)
        {
            AssetBundle cachedBundle;
            if (equipmentIconBundles.TryGetValue(bundleName, out cachedBundle) && cachedBundle != null)
            {
                return cachedBundle;
            }

            AssetBundle loadedBundle = FindLoadedBundleByName(bundleName);
            if (loadedBundle != null)
            {
                equipmentIconBundles[bundleName] = loadedBundle;
                return loadedBundle;
            }

            string baseDir = GetModDirectory();
            if (string.IsNullOrEmpty(baseDir))
            {
                return null;
            }

            string bundlePath = Path.Combine(baseDir, "Assets", "Equipment", bundleName);
            if (!File.Exists(bundlePath))
            {
                return null;
            }

            loadedBundle = AssetBundle.LoadFromFile(bundlePath);
            if (loadedBundle != null)
            {
                equipmentIconBundles[bundleName] = loadedBundle;
                equipmentIconBundlesLoadedByHelper.Add(loadedBundle);
            }
            return loadedBundle;
        }

        private static AssetBundle FindLoadedBundleByName(string bundleName)
        {
            try
            {
                foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle == null) continue;
                    if (IsBundleNameMatch(bundle.name, bundleName))
                    {
                        return bundle;
                    }
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            return null;
        }

        private static bool IsBundleNameMatch(string loadedName, string expectedName)
        {
            if (string.IsNullOrEmpty(loadedName) || string.IsNullOrEmpty(expectedName))
            {
                return false;
            }

            if (string.Equals(loadedName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalized = loadedName.Replace('\\', '/');
            int slashIndex = normalized.LastIndexOf('/');
            string fileName = slashIndex >= 0 ? normalized.Substring(slashIndex + 1) : normalized;
            return string.Equals(fileName, expectedName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从 Assets/Equipment 子目录读 PNG 并构造 Sprite。
        /// 命中后挂 HideFlags.DontSave，避免被场景序列化引用。
        /// </summary>
        private static Sprite LoadIconFromPng(string bundleName, string iconAssetName)
        {
            string baseDir = GetModDirectory();
            if (string.IsNullOrEmpty(baseDir)) return null;

            string fileName = iconAssetName + ".png";

            // 候选路径列表：优先含 bundle 的子目录
            string[] candidates;
            if (!string.IsNullOrEmpty(bundleName))
            {
                candidates = new string[]
                {
                    Path.Combine(baseDir, "Assets", "Equipment", bundleName, fileName),
                    Path.Combine(baseDir, "Assets", "Equipment", fileName),
                    Path.Combine(baseDir, "Assets", "Items", bundleName, fileName),
                    Path.Combine(baseDir, "Assets", "Items", fileName)
                };
            }
            else
            {
                candidates = new string[]
                {
                    Path.Combine(baseDir, "Assets", "Equipment", fileName),
                    Path.Combine(baseDir, "Assets", "Items", fileName)
                };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (!File.Exists(path)) continue;

                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(data))
                    {
                        UnityEngine.Object.Destroy(tex);
                        continue;
                    }
                    tex.hideFlags = HideFlags.DontSave;
                    tex.name = iconAssetName;

                    Sprite sprite = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    if (sprite != null)
                    {
                        sprite.hideFlags = HideFlags.DontSave;
                        sprite.name = iconAssetName;
                        ModBehaviour.DevLog("[EquipmentHelperIcon] 加载图标 PNG 成功: " + path);
                        return sprite;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[EquipmentHelperIcon] 读取 PNG 失败 " + path + ": " + e.Message);
                }
            }

            return null;
        }
    }
}

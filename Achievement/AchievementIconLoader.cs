// ============================================================================
// AchievementIconLoader.cs - 成就图标加载服务（共享单例）
// ============================================================================
// 模块说明：
//   统一管理成就图标的 PNG 与 AssetBundle 加载、缓存和自有资源
//   供 SteamAchievementPopup 和 AchievementEntryUI 共同使用
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 成就图标加载服务 - 优先加载独立 PNG，兼容旧 AssetBundle
    /// </summary>
    public static class AchievementIconLoader
    {
        #region 常量

        private const string PNG_RELATIVE_PATH = "Assets/achievement";
        private const string BUNDLE_RELATIVE_PATH = "Assets/achievement/achievement_icons";
        private const string BUNDLE_NAME = "achievement_icons";

        #endregion

        #region 私有字段

        private static AssetBundle iconBundle = null;
        private static bool loadAttempted = false;
        private static Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        private static Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<Sprite> ownedSprites = new HashSet<Sprite>();
        private static readonly HashSet<Texture2D> ownedTextures = new HashSet<Texture2D>();

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取成就图标 Sprite（带缓存）
        /// </summary>
        public static Sprite GetSprite(string iconName)
        {
            if (!IsValidIconName(iconName)) return null;

            if (spriteCache.TryGetValue(iconName, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite sprite = LoadSpriteFromPng(iconName);
            if (sprite == null)
            {
                EnsureBundleLoaded();
                sprite = LoadSpriteFromBundle(iconName);
            }

            if (sprite != null)
            {
                spriteCache[iconName] = sprite;
            }

            return sprite;
        }

        /// <summary>
        /// 获取成就图标 Texture2D（带缓存）
        /// </summary>
        public static Texture2D GetTexture(string iconName)
        {
            if (!IsValidIconName(iconName)) return null;

            // 检查缓存
            if (textureCache.TryGetValue(iconName, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            // 尝试从 Sprite 获取
            Sprite sprite = GetSprite(iconName);
            if (sprite != null && sprite.texture != null)
            {
                textureCache[iconName] = sprite.texture;
                return sprite.texture;
            }

            EnsureBundleLoaded();
            if (iconBundle == null) return null;

            // 直接加载 Texture2D
            Texture2D tex = LoadTextureFromBundle(iconName);
            if (tex != null)
            {
                textureCache[iconName] = tex;
            }

            return tex;
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public static void ClearCache()
        {
            spriteCache.Clear();
            textureCache.Clear();

            // Bundle 中借用的资源由 Bundle 管理，只销毁本加载器创建的对象。
            foreach (Sprite sprite in ownedSprites)
            {
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
            }
            ownedSprites.Clear();
            foreach (Texture2D texture in ownedTextures)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            ownedTextures.Clear();
        }

        /// <summary>
        /// 卸载 AssetBundle（通常在 mod 卸载时调用）
        /// </summary>
        public static void Unload()
        {
            ClearCache();
            if (iconBundle != null)
            {
                iconBundle.Unload(true);
                iconBundle = null;
            }
            loadAttempted = false;
        }

        public static void ResetStaticCaches()
        {
            Unload();
        }

        #endregion

        #region 私有方法

        private static bool IsValidIconName(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return false;
            for (int i = 0; i < iconName.Length; i++)
            {
                char c = iconName[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            }
            return true;
        }

        private static Sprite LoadSpriteFromPng(string iconName)
        {
            Texture2D texture = null;
            Sprite sprite = null;
            bool retained = false;
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;
                string path = System.IO.Path.Combine(modPath, PNG_RELATIVE_PATH, iconName + ".png");
                if (!System.IO.File.Exists(path)) return null;

                byte[] bytes = System.IO.File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes)) return null;
                texture.hideFlags = HideFlags.DontSave;
                texture.name = iconName;
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                if (sprite == null) return null;
                sprite.hideFlags = HideFlags.DontSave;
                sprite.name = iconName;
                ownedTextures.Add(texture);
                ownedSprites.Add(sprite);
                retained = true;
                return sprite;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[AchievementIconLoader] 加载 PNG 失败: " + iconName + " - " + e.Message);
                return null;
            }
            finally
            {
                // 解码或 Sprite 创建中途失败时，不能把未缓存的原生对象留到下一次加载。
                if (!retained)
                {
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>
        /// 确保 AssetBundle 已加载
        /// </summary>
        private static void EnsureBundleLoaded()
        {
            if (loadAttempted) return;
            loadAttempted = true;

            // 先检查是否已被其他代码加载
            iconBundle = FindLoadedBundle(BUNDLE_NAME);
            if (iconBundle != null)
            {
                ModBehaviour.DevLog("[AchievementIconLoader] 使用已加载的 AssetBundle");
                LogBundleContents();
                return;
            }

            // 尝试加载
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                ModBehaviour.DevLog("[AchievementIconLoader] ModPath 为空，无法加载 AssetBundle");
                return;
            }

            string bundlePath = System.IO.Path.Combine(modPath, BUNDLE_RELATIVE_PATH);
            if (!System.IO.File.Exists(bundlePath))
            {
                ModBehaviour.DevLog("[AchievementIconLoader] AssetBundle 文件不存在: " + bundlePath);
                return;
            }

            try
            {
                iconBundle = AssetBundle.LoadFromFile(bundlePath);
                if (iconBundle != null)
                {
                    ModBehaviour.DevLog("[AchievementIconLoader] 成功加载 AssetBundle: " + bundlePath);
                    LogBundleContents();
                }
                else
                {
                    // 再次检查是否被并发加载
                    iconBundle = FindLoadedBundle(BUNDLE_NAME);
                    if (iconBundle != null)
                    {
                        ModBehaviour.DevLog("[AchievementIconLoader] 从已加载列表获取 AssetBundle");
                    }
                    else
                    {
                        ModBehaviour.DevLog("[AchievementIconLoader] AssetBundle.LoadFromFile 返回 null");
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.LogError("[AchievementIconLoader] 加载 AssetBundle 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 查找已加载的 AssetBundle
        /// </summary>
        private static AssetBundle FindLoadedBundle(string bundleName)
        {
            try
            {
                foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle != null && bundle.name.Contains(bundleName))
                    {
                        return bundle;
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[AchievementIconLoader] 查找已加载 AssetBundle 失败: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 打印 bundle 内容（调试用）
        /// </summary>
        private static void LogBundleContents()
        {
            if (iconBundle == null) return;
            try
            {
                string[] allAssets = iconBundle.GetAllAssetNames();
                ModBehaviour.DevLog("[AchievementIconLoader] AssetBundle 包含 " + allAssets.Length + " 个资源");
            }
            catch { }
        }

        /// <summary>
        /// 从 bundle 加载 Sprite
        /// </summary>
        private static Sprite LoadSpriteFromBundle(string iconName)
        {
            if (iconBundle == null) return null;

            // 根据 manifest，资源路径格式为 "assets/achievementicons/xxx.png"（小写）
            string assetPath = "assets/achievementicons/" + iconName.ToLowerInvariant() + ".png";

            try
            {
                // 尝试直接加载 Sprite
                Sprite sprite = iconBundle.LoadAsset<Sprite>(assetPath);
                if (sprite != null)
                {
                    return sprite;
                }

                // 尝试从 Texture2D 创建 Sprite
                Texture2D tex = iconBundle.LoadAsset<Texture2D>(assetPath);
                if (tex != null)
                {
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    if (sprite != null) ownedSprites.Add(sprite);
                    return sprite;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[AchievementIconLoader] 加载 Sprite 失败: " + iconName + " - " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 从 bundle 加载 Texture2D
        /// </summary>
        private static Texture2D LoadTextureFromBundle(string iconName)
        {
            if (iconBundle == null) return null;

            string assetPath = "assets/achievementicons/" + iconName.ToLowerInvariant() + ".png";

            try
            {
                return iconBundle.LoadAsset<Texture2D>(assetPath);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[AchievementIconLoader] 加载 Texture2D 失败: " + iconName + " - " + e.Message);
            }

            return null;
        }

        #endregion
    }
}

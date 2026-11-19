// ============================================================================
// AchievementIconLoader.cs - 成就图标加载服务（共享单例）
// ============================================================================
// 模块说明：
//   统一管理成就图标的取图、缓存和自有资源：按路径走 ModSprites，取不到再回退旧 achievement_icons 包
//   供 SteamAchievementPopup 和 AchievementEntryUI 共同使用
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 成就图标加载服务：ModSprites（生产压缩图标，Dev 或缺包读散图）优先，保留旧 AssetBundle 回退。
    /// 借自 ModSprites 与旧包的图不归这里销毁；只销毁用旧包 Texture2D 现造的 Sprite。
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

            Sprite sprite = ModSprites.Get(PNG_RELATIVE_PATH + "/" + iconName + ".png");
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

            // 借自 ModSprites 与旧包的资源由各自 owner 管理，只销毁本加载器创建的对象。
            foreach (Sprite sprite in ownedSprites)
            {
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
            }
            ownedSprites.Clear();
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

        /// <summary>
        /// 确保 AssetBundle 已加载
        /// </summary>
        private static void EnsureBundleLoaded()
        {
            if (loadAttempted) return;
            loadAttempted = true;

            // 先检查是否已被其他代码加载
            iconBundle = ResourceBundleLoader.FindLoaded(BUNDLE_NAME);
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
                iconBundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                if (iconBundle != null)
                {
                    ModBehaviour.DevLog("[AchievementIconLoader] 成功加载 AssetBundle: " + bundlePath);
                    LogBundleContents();
                }
                else
                {
                    // 再次检查是否被并发加载
                    iconBundle = ResourceBundleLoader.FindLoaded(BUNDLE_NAME);
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

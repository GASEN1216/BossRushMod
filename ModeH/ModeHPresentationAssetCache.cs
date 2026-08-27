using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 展示资源缓存（设计提案 §23.3、§25.1）。
    ///
    /// 冻结契约：
    /// - bundle 文件名/运行路径固定为 Assets/ui/modeh_presentation，恰好两张 Sprite；
    /// - 每个 runtime 最多一次 AssetBundle.LoadFromFile；
    /// - 入口预检必须同时取到徽记与横幅，生产缺包 fail-closed；
    /// - AllowDevRawPngFallback 是编译期开发门，发布构建恒 false，不得用 raw PNG 冒充发布包；
    /// - 正常终止 / OnDestroy / Mod 卸载 / 宿主重建时先销毁引用再幂等 Unload(true)。
    /// </summary>
    public static class ModeHPresentationAssetCache
    {
        #region 冻结常量

        /// <summary>展示 bundle 名（label 与文件名一致）。</summary>
        public const string PresentationBundleName = "modeh_presentation";

        /// <summary>Mod 运行目录下的 bundle 相对路径。</summary>
        public const string BundleRelativePath = "Assets/ui/modeh_presentation";

        /// <summary>徽记 Sprite 短名。</summary>
        public const string EmblemAssetName = "ModeH_BlackMarketCup_Emblem";

        /// <summary>横幅 Sprite 短名。</summary>
        public const string BannerAssetName = "ModeH_BlackMarketCup_Banner";

        /// <summary>开发期 raw PNG 目录（仅在编译期开发门开启时使用）。</summary>
        public const string DevRawRelativeDir = "Assets/ui/ModeH";

        #endregion

        #region 状态

        private static readonly object _lock = new object();
        private static AssetBundle _bundle;
        private static Sprite _emblemSprite;
        private static Sprite _bannerSprite;
        private static bool _loadAttempted;
        private static bool _emblemAttempted;
        private static bool _bannerAttempted;
        private static bool _preflightAttempted;
        private static bool _preflightResult;

        #endregion

        #region 预检与访问

        /// <summary>
        /// 入口预检：必须同时取到两张 Sprite 才算通过（fail-closed），结果缓存到本 runtime 结束。
        /// </summary>
        public static bool TryPreflight()
        {
            lock (_lock)
            {
                if (_preflightAttempted) return _preflightResult;
                _preflightAttempted = true;
            }

            try
            {
                Sprite emblem = GetEmblemSprite();
                Sprite banner = GetBannerSprite();
                _preflightResult = emblem != null && banner != null;
                if (!_preflightResult)
                {
                    ModBehaviour.CriticalLog(
                        "modeh-presentation-missing",
                        "[ModeH] [ERROR] 展示资源缺失，Mode H 入口 fail-closed（需要 "
                        + BundleRelativePath + " 内的两张 Sprite）");
                }
            }
            catch (Exception e)
            {
                _preflightResult = false;
                ModBehaviour.DevLog("[ModeH] [WARNING] 展示资源预检异常: " + e.Message);
            }
            return _preflightResult;
        }

        /// <summary>徽记 Sprite（单次尝试后缓存）。</summary>
        public static Sprite GetEmblemSprite()
        {
            if (_emblemSprite != null) return _emblemSprite;
            if (_emblemAttempted) return _emblemSprite;
            _emblemAttempted = true;
            try
            {
                _emblemSprite = LoadBundleSprite(EmblemAssetName) ?? LoadDevRawSprite(EmblemAssetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 徽记加载异常: " + e.Message);
                _emblemSprite = null;
            }
            return _emblemSprite;
        }

        /// <summary>横幅 Sprite（单次尝试后缓存）。</summary>
        public static Sprite GetBannerSprite()
        {
            if (_bannerSprite != null) return _bannerSprite;
            if (_bannerAttempted) return _bannerSprite;
            _bannerAttempted = true;
            try
            {
                _bannerSprite = LoadBundleSprite(BannerAssetName) ?? LoadDevRawSprite(BannerAssetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 横幅加载异常: " + e.Message);
                _bannerSprite = null;
            }
            return _bannerSprite;
        }

        #endregion

        #region Bundle 加载（每 runtime 至多一次）

        private static bool EnsureBundleLoaded()
        {
            if (_bundle != null) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;
            try
            {
                foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (loaded != null && loaded.name != null
                        && loaded.name.IndexOf(PresentationBundleName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _bundle = loaded;
                        return true;
                    }
                }

                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return false;

                string bundlePath = Path.Combine(
                    modPath, BundleRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[ModeH] 展示 bundle 不存在: " + bundlePath);
                    return false;
                }

                _bundle = AssetBundle.LoadFromFile(bundlePath);
                if (_bundle == null)
                {
                    ModBehaviour.DevLog("[ModeH] 展示 bundle 加载失败: " + bundlePath);
                    return false;
                }

                ModBehaviour.DevLog("[ModeH] 展示 bundle 加载成功: " + bundlePath);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [ERROR] 展示 bundle 加载异常: " + e.Message);
                return false;
            }
        }

        private static Sprite LoadBundleSprite(string assetName)
        {
            if (!EnsureBundleLoaded() || _bundle == null) return null;
            try
            {
                return _bundle.LoadAsset<Sprite>(assetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] bundle 资源加载失败 " + assetName + ": " + e.Message);
                return null;
            }
        }

        #endregion

        #region 开发期 raw PNG fallback（发布构建恒关闭）

        private static Sprite LoadDevRawSprite(string assetName)
        {
            try
            {
                if (!ModeHAvailability.AllowDevRawPngFallback) return null;

                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;

                string path = Path.Combine(
                    modPath,
                    DevRawRelativeDir.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar
                        + assetName + ".png");
                if (!File.Exists(path)) return null;

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                ModBehaviour.DevLog("[ModeH] [DEV] 使用 raw PNG fallback: " + assetName);
                return Sprite.Create(
                    texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] raw PNG fallback 异常: " + e.Message);
                return null;
            }
        }

        #endregion

        #region 卸载

        /// <summary>
        /// 幂等卸载：调用方必须先销毁引用这两张 Sprite 的 Image/UI 根，再调用本方法。
        /// </summary>
        public static void Unload()
        {
            lock (_lock)
            {
                try
                {
                    if (_bundle != null)
                    {
                        _bundle.Unload(true);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeH] [WARNING] 展示 bundle 卸载异常: " + e.Message);
                }

                _bundle = null;
                _emblemSprite = null;
                _bannerSprite = null;
                _loadAttempted = false;
                _emblemAttempted = false;
                _bannerAttempted = false;
                _preflightAttempted = false;
                _preflightResult = false;
            }
        }

        /// <summary>清空静态缓存（等价于卸载）。</summary>
        public static void ResetStaticCaches()
        {
            Unload();
        }

        #endregion
    }
}

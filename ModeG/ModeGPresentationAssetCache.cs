using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode G 展示资源缓存（规格 §12 重写版）。
    ///
    /// 硬约束：
    /// - bundle 路径 &lt;modRoot&gt;/Assets/ui/modeg_presentation，每 runtime 至多 LoadFromFile 一次；
    /// - 资源：modeg_echo_emblem（徽记 256x256）、modeg_echo_banner（横幅 1024x576）；
    /// - raw PNG fallback（Assets/ui/ModeG/*.png）仅开发构建（AllowDevTestEntry）可用；
    /// - 生产环境缺包 fail-closed（TryPreflight 返回 false，入口不呈现）；
    /// - Unload 幂等（CleanupController 终局调用）。
    /// </summary>
    public static class ModeGPresentationAssetCache
    {
        /// <summary>展示 AssetBundle 名称（Unity 侧双 label 之一）</summary>
        public const string PresentationBundleName = "modeg_presentation";

        private const string BundleRelativePath = "Assets/ui/modeg_presentation";
        private const string EmblemAssetName = "modeg_echo_emblem";
        private const string BannerAssetName = "modeg_echo_banner";
        private const string DevRawRelativeDir = "Assets/ui/ModeG";

        private static AssetBundle _bundle;
        private static bool _loadAttempted;
        private static bool _preflightAttempted;
        private static bool _preflightResult;
        private static Sprite _emblemSprite;
        private static Sprite _bannerSprite;
        private static bool _emblemAttempted;
        private static bool _bannerAttempted;

        #region Preflight（入口门控；fail-closed）

        /// <summary>
        /// 展示资源可用性预检（no-throw，缓存结果，Unload 后重置）。
        /// 生产：bundle 必须可加载；开发：允许 raw PNG fallback。
        /// </summary>
        public static bool TryPreflight()
        {
            if (_preflightAttempted) return _preflightResult;
            _preflightAttempted = true;
            try
            {
                if (EnsureBundleLoaded())
                {
                    _preflightResult = true;
                    return true;
                }

                // 仅开发构建允许 raw PNG fallback
                if (ModeGAvailability.AllowDevRawPngFallback
                    && DevRawEmblemPath() != null && DevRawBannerPath() != null)
                {
                    _preflightResult = true;
                    return true;
                }

                ModBehaviour.DevLog("[ModeG] 展示资源预检失败（fail-closed）");
                _preflightResult = false;
                return false;
            }
            catch
            {
                _preflightResult = false;
                return false;
            }
        }

        #endregion

        #region Sprite Accessors（带缓存）

        /// <summary>
        /// 宿命回响徽记 Sprite（bundle 优先；开发构建 raw PNG fallback）。
        /// </summary>
        public static Sprite GetEmblemSprite()
        {
            if (_emblemAttempted) return _emblemSprite;
            _emblemAttempted = true;
            try
            {
                _emblemSprite = LoadBundleSprite(EmblemAssetName)
                    ?? LoadDevRawSprite(EmblemAssetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 徽记加载异常: " + e.Message);
                _emblemSprite = null;
            }
            return _emblemSprite;
        }

        /// <summary>
        /// 宿命回响横幅 Sprite（bundle 优先；开发构建 raw PNG fallback）。
        /// </summary>
        public static Sprite GetBannerSprite()
        {
            if (_bannerAttempted) return _bannerSprite;
            _bannerAttempted = true;
            try
            {
                _bannerSprite = LoadBundleSprite(BannerAssetName)
                    ?? LoadDevRawSprite(BannerAssetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 横幅加载异常: " + e.Message);
                _bannerSprite = null;
            }
            return _bannerSprite;
        }

        #endregion

        #region Bundle Loading（每 runtime 至多一次 LoadFromFile）

        private static bool EnsureBundleLoaded()
        {
            if (_bundle != null) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;
            try
            {
                // 复用已加载实例（避免与其他代码并发加载冲突）
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

                string bundlePath = System.IO.Path.Combine(modPath, BundleRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[ModeG] 展示 bundle 不存在: " + bundlePath);
                    return false;
                }

                _bundle = AssetBundle.LoadFromFile(bundlePath);
                if (_bundle == null)
                {
                    ModBehaviour.DevLog("[ModeG] 展示 bundle 加载失败: " + bundlePath);
                    return false;
                }

                ModBehaviour.DevLog("[ModeG] 展示 bundle 加载成功: " + bundlePath);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] 展示 bundle 加载异常: " + e.Message);
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
                ModBehaviour.DevLog("[ModeG] [WARNING] bundle 资源加载失败 " + assetName + ": " + e.Message);
                return null;
            }
        }

        #endregion

        #region Dev Raw PNG Fallback（仅开发构建）

        private static Sprite LoadDevRawSprite(string assetName)
        {
            try
            {
                if (!ModeGAvailability.AllowDevRawPngFallback) return null;

                string path = assetName == EmblemAssetName ? DevRawEmblemPath() : DevRawBannerPath();
                if (path == null || !System.IO.File.Exists(path)) return null;

                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                ModBehaviour.DevLog("[ModeG] [DEV] 使用 raw PNG fallback: " + assetName);
                return Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] raw PNG fallback 失败 " + assetName + ": " + e.Message);
                return null;
            }
        }

        private static string DevRawEmblemPath()
        {
            return DevRawPath(EmblemAssetName + ".png");
        }

        private static string DevRawBannerPath()
        {
            return DevRawPath(BannerAssetName + ".png");
        }

        private static string DevRawPath(string fileName)
        {
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;
                return System.IO.Path.Combine(
                    modPath,
                    DevRawRelativeDir.Replace('/', System.IO.Path.DirectorySeparatorChar),
                    fileName);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Unload（幂等；CleanupController 终局调用）

        /// <summary>
        /// 清空缓存并卸载 bundle（幂等）。Unload 后预检/缓存全部重置，
        /// 下次访问重新走每 runtime 一次的加载路径。
        /// </summary>
        public static void Unload()
        {
            try
            {
                if (_bundle != null)
                {
                    _bundle.Unload(true);
                    _bundle = null;
                }
            }
            catch { /* no-throw */ }

            _emblemSprite = null;
            _bannerSprite = null;
            _loadAttempted = false;
            _preflightAttempted = false;
            _preflightResult = false;
            _emblemAttempted = false;
            _bannerAttempted = false;
        }

        #endregion
    }
}

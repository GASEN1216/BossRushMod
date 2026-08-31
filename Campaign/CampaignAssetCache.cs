// ============================================================================
// CampaignAssetCache.cs - 战役立绘与海报加载
// ============================================================================
// 形态照 ModeG/ModeGPresentationAssetCache.cs：bundle 优先 → 开发期 raw PNG → 无。
//
// 【与 Mode G 的关键差别：这里是 fail-open】
//   Mode G 缺展示资源就拒绝进入模式（fail-closed），因为那是模式的核心呈现。
//   战役立绘缺失只是「对话没有头像」——官方 DialogueUI 会自动隐藏立绘容器，
//   对话照常播完，玩法一点不少。所以美术未就绪绝不能挡住战役上线。
//
// 【raw PNG fallback 在这里是允许的】
//   与 UI 九宫格图集不同，立绘/海报是整图显示，不需要 border 信息，
//   运行时 LoadImage 出来的散图与 bundle 里的效果一致。
//   这让美术可以边出图边看效果，定稿后再打 bundle，调用方零改动。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>战役展示资源缓存。全静态，每 runtime 至多 LoadFromFile 一次。</summary>
    internal static class CampaignAssetCache
    {
        #region 常量

        /// <summary>展示 AssetBundle 名。</summary>
        internal const string PresentationBundleName = "campaign_presentation";

        private const string BundleRelativePath = "Assets/ui/campaign_presentation";
        private const string RawRelativeDir = "Assets/ui/Campaign";

        /// <summary>中间人立绘资源名。</summary>
        internal const string BrokerPortraitAsset = "campaign_portrait_broker";

        /// <summary>前冠军立绘资源名。</summary>
        internal const string ChampionPortraitAsset = "campaign_portrait_champion";

        #endregion

        #region 状态

        private static AssetBundle _bundle;
        private static bool _bundleLoadAttempted;

        /// <summary>资源名 → Sprite。null 值也缓存，避免反复打盘找不存在的文件。</summary>
        private static readonly Dictionary<string, Sprite> _sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>程序化创建的 Sprite/Texture，Unload 时必须显式销毁。</summary>
        private static readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>();

        #endregion

        #region 查询

        /// <summary>中间人立绘。缺失返回 null（对话会自动隐藏立绘位）。</summary>
        internal static Sprite GetBrokerPortrait()
        {
            return GetSprite(BrokerPortraitAsset);
        }

        /// <summary>前冠军立绘。缺失返回 null。</summary>
        internal static Sprite GetChampionPortrait()
        {
            return GetSprite(ChampionPortraitAsset);
        }

        /// <summary>章节海报（同时用作线索插画）。缺失返回 null。</summary>
        internal static Sprite GetChapterPoster(int order)
        {
            return GetSprite("campaign_poster_ch" + order);
        }

        /// <summary>按资源名取图。bundle 优先，其次 raw PNG，都没有返回 null。</summary>
        internal static Sprite GetSprite(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;

            Sprite cached;
            if (_sprites.TryGetValue(assetName, out cached)) return cached;

            Sprite loaded = null;
            try
            {
                loaded = LoadBundleSprite(assetName);
                if (loaded == null) loaded = LoadRawSprite(assetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 展示资源加载异常 "
                    + assetName + ": " + e.Message);
            }

            // null 也缓存：美术没做的资源不该每次对话都去打一次盘
            _sprites[assetName] = loaded;
            return loaded;
        }

        #endregion

        #region 加载

        private static bool EnsureBundleLoaded()
        {
            if (_bundle != null) return true;
            if (_bundleLoadAttempted) return false;
            _bundleLoadAttempted = true;

            try
            {
                // 复用已加载实例：同一个 bundle 被 LoadFromFile 两次会直接失败
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
                if (!File.Exists(bundlePath)) return false;

                _bundle = AssetBundle.LoadFromFile(bundlePath);
                return _bundle != null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 展示 bundle 加载异常: " + e.Message);
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
            catch (Exception)
            {
                return null;
            }
        }

        private static Sprite LoadRawSprite(string assetName)
        {
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;

                string path = Path.Combine(
                    modPath,
                    RawRelativeDir.Replace('/', Path.DirectorySeparatorChar),
                    assetName + ".png");
                if (!File.Exists(path)) return null;

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));

                // 程序化对象带 HideFlags 之外的生命周期，必须记账以便 Unload 时销毁
                _ownedObjects.Add(texture);
                _ownedObjects.Add(sprite);
                return sprite;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 清理

        /// <summary>卸载 bundle 并销毁程序化对象（幂等）。</summary>
        internal static void Unload()
        {
            try
            {
                for (int i = 0; i < _ownedObjects.Count; i++)
                {
                    UnityEngine.Object obj = _ownedObjects[i];
                    if (obj != null) UnityEngine.Object.Destroy(obj);
                }
                _ownedObjects.Clear();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 卸载展示资源失败: " + e.Message);
            }

            try
            {
                if (_bundle != null)
                {
                    _bundle.Unload(true);
                    _bundle = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 卸载展示资源失败: " + e.Message);
            }

            _sprites.Clear();
            _bundleLoadAttempted = false;
        }

        internal static void ResetStaticCaches()
        {
            Unload();
        }

        #endregion
    }
}

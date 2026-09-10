// ============================================================================
// SkyIslandUiArt.cs - 天空岛交互面板的插图资源
// ============================================================================
// 形态照 Campaign/CampaignAssetCache.cs：bundle 优先 → 开发期 raw PNG → 无。
//
// 【fail-open，且这里比战役更该 fail-open】
//   插图缺失只是「面板没有配图」，面板本身、正文与全部选项照常工作。
//   天空岛的装置面板挂着 K1/K2/K3 与敲响归航钟，绝不能因为一张图没出来就打不开。
//
// 【raw PNG 在这里是允许的】
//   与 BossRushUISkin 的九宫格底图不同，立绘与横幅都是整图显示、不需要 border 信息，
//   运行时 LoadImage 出来的散图与 bundle 里效果一致。这让美术可以边出图边看效果，
//   定稿后再打 bundle，调用方零改动（见 tools/gen_sky_island_ui_art.py）。
//
// 【两类图对应面板的两种形态】
//   - `skyisland_portrait_<npcId>.png` 512×512 抠图立绘：居民对话面板的头像位。
//   - `skyisland_scene_<region>.png`  横幅：装置/见闻面板顶部的区域插图。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 天空岛面板插图缓存。全静态，每 runtime 至多 LoadFromFile 一次。
    ///
    /// 【常驻内存代价，以及为什么仍按模块 owner 释放】
    ///   全部读满是 12 张横幅（1024×288×4B ≈ 1.13 MiB）+ 6 张立绘（512×512×4B = 1 MiB）
    ///   ≈ **19.5 MiB，约 20 MB**。这是 GPU 上的那一份；CPU 端拷贝读图时已经丢掉（见 FromRawPng）。
    ///   按需加载，所以只有玩家真的开过那个区域的面板才会占；
    ///   但一旦读进来就活到模块销毁，回基地也不释放。
    ///
    ///   之所以不改成随出击结束释放：子系统静态缓存的唯一清理 owner 是
    ///   `SkyIslandRuntimeModule.OnDestroy`（设计复审 D-4 定的口径，物资池与档次染色块同此），
    ///   在这里另开一套按会话释放的生命周期会让「谁负责清」重新变成两个答案。
    ///   若将来实测这 20 MB 真的要命，正确改法是把 D-4 的口径整体改掉，而不是给插图开特例。
    /// </summary>
    internal static class SkyIslandUiArt
    {
        internal const string BundleName = "skyisland_ui";
        private const string BundleRelativePath = "Assets/ui/skyisland_ui";
        private const string RawRelativeDir = "Assets/ui/SkyIsland";
        private const string LogPrefix = "[SkyIslandUiArt] ";

        private static AssetBundle bundle;
        private static bool bundleLoadAttempted;

        /// <summary>资源名 → Sprite。**null 也缓存**，否则每次开面板都要打一次盘找不存在的文件。</summary>
        private static readonly Dictionary<string, Sprite> sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>运行时 new 出来的 Sprite/Texture，Reset 时必须显式销毁。</summary>
        private static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        /// <summary>居民立绘。没有就返回 null，面板会退成无头像布局。</summary>
        internal static Sprite GetPortrait(string npcId)
        {
            string asset = PortraitAssetName(npcId);
            return asset == null ? null : Get(asset);
        }

        internal static string PortraitAssetName(string npcId)
        {
            return string.IsNullOrEmpty(npcId) ? null : "skyisland_portrait_" + npcId;
        }

        internal static string SceneAssetName(string marker)
        {
            string region = RegionOf(marker);
            return region == null ? null : "skyisland_scene_" + region;
        }

        /// <summary>
        /// 只读探测：这张插图部署了没有。**不解码贴图、不创建 Sprite、不写缓存**，给 F3 验收用。
        ///
        /// 旧用例直接调 <see cref="GetScene"/> / <see cref="GetPortrait"/>：在玩家真实的出击上一帧内同步解码约 20 MB 贴图
        /// （这次卡顿还不进性能采样），而且查不到的结果（null）也被永久缓存——验收时美术若还没拷完，
        /// 面板从此到进程结束都没有插图。这里只问：已缓存的真图？bundle 目录里有没有这个名字（`Contains`
        /// 只读目录不加载资源）？散图文件在不在？首次调用会像打开面板一样加载 bundle 本身（目录与头信息）。
        /// </summary>
        internal static bool HasArt(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return false;
            Sprite cached;
            if (sprites.TryGetValue(assetName, out cached) && cached != null) return true;
            try
            {
                EnsureBundleLoadAttempted();
                if (bundle != null && bundle.Contains(assetName)) return true;
                return File.Exists(Path.Combine(Path.Combine(ModBehaviour.GetModPath(), RawRelativeDir),
                    assetName + ".png"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 区域横幅。<paramref name="marker"/> 是见闻点或地标名（`Search_D` / `POI_S1` / `Search_H_02`），
        /// 这里只取区域字母，让同一区域的多个点共用一张图。
        /// </summary>
        internal static Sprite GetScene(string marker)
        {
            string asset = SceneAssetName(marker);
            return asset == null ? null : Get(asset);
        }

        /// <summary>
        /// 从标记名解析区域：`Search_D` → D，`Search_D_02` → D，`POI_S1` → S1，`Lamp_H_02` → H。
        /// 解析不出来返回 null（调用方退成无插图），**不猜**：猜错会给玩家配一张别的岛的图。
        /// </summary>
        internal static string RegionOf(string marker)
        {
            if (string.IsNullOrEmpty(marker)) return null;
            int underscore = marker.IndexOf('_');
            if (underscore < 0 || underscore + 1 >= marker.Length) return null;
            string rest = marker.Substring(underscore + 1);
            int next = rest.IndexOf('_');
            if (next >= 0) rest = rest.Substring(0, next);
            if (rest.Length == 1 && rest[0] >= 'A' && rest[0] <= 'H') return rest;
            if (rest.Length == 2 && rest[0] == 'S' && rest[1] >= '1' && rest[1] <= '4') return rest;
            return null;
        }

        /// <summary>
        /// 面板插图底部的竖向渐隐条，用来把插图和正文接起来。
        ///
        /// 必须是**真渐变**：早先那版用的是一条 55% 不透明的纯色横条，上下两条硬边一眼看得出来，
        /// 正是「廉价感」的典型来源。这里生成 1×64 的 alpha 渐变纹理，上端全透、下端接近面板底色。
        /// 与插图共用同一套 owned 释放，`ResetStaticCaches` 一并销毁。
        /// </summary>
        internal static Sprite GetBannerFade()
        {
            Sprite cached;
            if (sprites.TryGetValue(FadeAssetName, out cached)) return cached;
            Sprite result = null;
            try
            {
                const int height = 64;
                Texture2D texture = new Texture2D(1, height, TextureFormat.RGBA32, false, false);
                texture.name = FadeAssetName;
                texture.wrapMode = TextureWrapMode.Clamp;
                Color surface = BossRushUIColors.Surface;
                for (int y = 0; y < height; y++)
                {
                    // y=0 是纹理底部：底部最不透明，往上淡出。用平方曲线让过渡更贴近视觉线性。
                    float t = 1f - (y / (float)(height - 1));
                    texture.SetPixel(0, y, new Color(surface.r, surface.g, surface.b, t * t));
                }
                texture.Apply(false, true);
                owned.Add(texture);
                result = Sprite.Create(texture, new Rect(0f, 0f, 1f, height), new Vector2(0.5f, 0.5f),
                    100f, 0u, SpriteMeshType.FullRect);
                result.name = FadeAssetName;
                owned.Add(result);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "渐隐条生成失败，横幅将直接接正文：" + e.Message);
            }
            sprites[FadeAssetName] = result;
            return result;
        }

        private const string FadeAssetName = "__skyisland_banner_fade";
        private const string ScrimAssetName = "__skyisland_title_scrim";

        /// <summary>
        /// 区域大标题背后的压暗底。**必须有**：岛上抬头就是一片高亮的云海，
        /// 浅色文字直接压在云上几乎读不出来（离线预览里一眼可见）。
        /// 主流游戏的区域名底下也都垫一层柔和压暗，这不是装饰而是可读性。
        ///
        /// 形状是**二维**柔边：竖向是 0→1→0 的余弦钟形，横向是两侧各 30% 的 smoothstep 淡出、
        /// 中间留一段平台罩住文字。早先那版只做了竖向渐变、横向拉伸成矩形，
        /// 左右两侧是两条笔直的硬边——正是要消灭的「贴了一块板」。
        /// </summary>
        internal static Sprite GetTitleScrim()
        {
            Sprite cached;
            if (sprites.TryGetValue(ScrimAssetName, out cached)) return cached;
            Sprite result = null;
            try
            {
                const int width = 64;
                const int height = 48;
                const float peak = 0.60f;
                const float edge = 0.30f;
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                texture.name = ScrimAssetName;
                texture.wrapMode = TextureWrapMode.Clamp;
                // 双线性采样会把这张小图平滑拉到 1180×200，所以 64×48 足够，不必出大图。
                texture.filterMode = FilterMode.Bilinear;
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1);
                    float sideIn = Mathf.Clamp01(u / edge);
                    float sideOut = Mathf.Clamp01((1f - u) / edge);
                    // smoothstep：两侧从 0 平滑爬到 1，中间是平台。
                    float horizontal = sideIn * sideIn * (3f - 2f * sideIn)
                        * sideOut * sideOut * (3f - 2f * sideOut);
                    for (int y = 0; y < height; y++)
                    {
                        float v = y / (float)(height - 1);
                        float vertical = 0.5f - 0.5f * Mathf.Cos(v * Mathf.PI * 2f);
                        texture.SetPixel(x, y, new Color(0.02f, 0.03f, 0.04f, horizontal * vertical * peak));
                    }
                }
                texture.Apply(false, true);
                owned.Add(texture);
                result = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                    100f, 0u, SpriteMeshType.FullRect);
                result.name = ScrimAssetName;
                owned.Add(result);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "标题压暗底生成失败：" + e.Message);
            }
            sprites[ScrimAssetName] = result;
            return result;
        }

        private static Sprite Get(string assetName)
        {
            Sprite cached;
            if (sprites.TryGetValue(assetName, out cached)) return cached;
            Sprite result = null;
            try
            {
                result = FromBundle(assetName) ?? FromRawPng(assetName);
            }
            catch (Exception e)
            {
                // 插图是纯观感层：读图失败绝不能拦住面板打开。
                Debug.LogWarning(LogPrefix + assetName + " 读取失败，面板将不带插图：" + e.Message);
            }
            sprites[assetName] = result;
            return result;
        }

        private static Sprite FromBundle(string assetName)
        {
            EnsureBundleLoadAttempted();
            return bundle == null ? null : bundle.LoadAsset<Sprite>(assetName);
        }

        private static void EnsureBundleLoadAttempted()
        {
            if (bundleLoadAttempted) return;
            bundleLoadAttempted = true;
            try
            {
                string path = Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath);
                if (File.Exists(path)) bundle = AssetBundle.LoadFromFile(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "插图 bundle 加载失败，退回散图：" + e.Message);
            }
        }

        private static Sprite FromRawPng(string assetName)
        {
            string path = Path.Combine(Path.Combine(ModBehaviour.GetModPath(), RawRelativeDir),
                assetName + ".png");
            if (!File.Exists(path)) return null;
            // mipmap 关掉：UI 图不缩小采样，开了只是白占显存。
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            // markNonReadable: true —— 上传 GPU 之后丢掉 CPU 端那份拷贝。我们从不读像素，
            // 留着等于把这批图的常驻内存整整翻一倍（满载约 40 MB → 约 20 MB）。
            if (!texture.LoadImage(File.ReadAllBytes(path), true))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            texture.name = assetName;
            texture.wrapMode = TextureWrapMode.Clamp;
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = assetName;
            owned.Add(sprite);
            return sprite;
        }

        /// <summary>
        /// 由 <c>SkyIslandRuntimeModule.OnDestroy</c> 调用，与其它天空岛静态缓存同一个 owner。
        /// 运行时 new 出来的 Texture/Sprite 必须显式 Destroy，只置 null 是丢给
        /// <c>Resources.UnloadUnusedAssets</c> 碰运气（口径同 <see cref="SkyIslandRendering"/>）。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] != null) UnityEngine.Object.Destroy(owned[i]);
            owned.Clear();
            sprites.Clear();
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            bundleLoadAttempted = false;
        }
    }
}

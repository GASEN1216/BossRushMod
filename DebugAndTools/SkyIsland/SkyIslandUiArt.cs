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
    ///   全部读满是 12 张横幅（1024×288×4B ≈ 1.2 MB）+ 6 张立绘（512×512×4B ≈ 1 MB）
    ///   ≈ **20 MB**。按需加载，所以只有玩家真的开过那个区域的面板才会占；
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
            return string.IsNullOrEmpty(npcId) ? null : Get("skyisland_portrait_" + npcId);
        }

        /// <summary>
        /// 区域横幅。<paramref name="marker"/> 是见闻点或地标名（`Search_D` / `POI_S1` / `Search_H_02`），
        /// 这里只取区域字母，让同一区域的多个点共用一张图。
        /// </summary>
        internal static Sprite GetScene(string marker)
        {
            string region = RegionOf(marker);
            return region == null ? null : Get("skyisland_scene_" + region);
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
            if (!bundleLoadAttempted)
            {
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
            return bundle == null ? null : bundle.LoadAsset<Sprite>(assetName);
        }

        private static Sprite FromRawPng(string assetName)
        {
            string path = Path.Combine(Path.Combine(ModBehaviour.GetModPath(), RawRelativeDir),
                assetName + ".png");
            if (!File.Exists(path)) return null;
            // mipmap 关掉：UI 图不缩小采样，开了只是白占显存。
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
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

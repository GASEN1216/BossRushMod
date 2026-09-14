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
    ///   全部读满是 13 张横幅（含手记，1024×288×4B ≈ 1.13 MiB）+ 6 张立绘（512×512×4B = 1 MiB）
    ///   + 13 张面板底图（220×236×4B ≈ 0.2 MiB）≈ **23 MiB**。这是 GPU 上的那一份；CPU 端拷贝读图时已经丢掉（见 FromRawPng）。
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
        /// 群岛手记的横幅资源名。手记不挂在任何一处区域上，所以不走 <see cref="GetScene"/> 的区域解析。
        /// </summary>
        internal const string JournalSceneAsset = "skyisland_scene_journal";

        /// <summary>面板整屏底图的资源名前缀。由 `tools/gen_sky_island_panel_backgrounds.py` 派生。</summary>
        private const string BackgroundPrefix = "skyisland_bg_";

        /// <summary>
        /// 面板整屏底图：那一区的场景横幅经模糊、降饱和派生出来的 220×236 小图。
        ///
        /// 【为什么不直接把横幅铺满面板】面板是竖的（880 × 约 940），横幅是 3.56:1；
        /// cover 上去要放大 **3.26 倍**、只看得见原图中间 26% 的宽度，云海的平滑渐变在这个
        /// 倍率下会出现带状阶梯。底图是**为模糊而生**的，所以小图反而正确。
        ///
        /// 【区域从哪来】<paramref name="banner"/> 的 <c>Sprite.name</c> 就是资源名
        /// （bundle 与散图两条路径都显式赋过），形如 <c>skyisland_scene_B</c>，取后缀即可——
        /// 不必给 <c>Show</c> 加一个新参数、也就不用动任何调用点。
        /// 没有横幅（居民面板只有立绘）时退到群岛总览那张，它是中性的全群岛远景。
        /// 一张都没有时返回 null，面板退回纯色底（fail-open）。
        /// </summary>
        internal static Sprite GetPanelBackground(Sprite banner)
        {
            string region = null;
            if (banner != null && !string.IsNullOrEmpty(banner.name)
                && banner.name.StartsWith("skyisland_scene_", StringComparison.Ordinal))
            {
                region = banner.name.Substring("skyisland_scene_".Length);
            }
            if (string.IsNullOrEmpty(region)) region = "journal";
            Sprite background = Get(BackgroundPrefix + region);
            // 区域底图缺失时退到总览那张，而不是直接没有底图：换皮降级要一档一档退。
            if (background == null && !string.Equals(region, "journal", StringComparison.Ordinal))
                background = Get(BackgroundPrefix + "journal");
            return background;
        }

        /// <summary>
        /// 群岛手记的横幅。手记是全链条里文本最长的一页（总览 + 岛上的灯 + 群岛之物），
        /// 却是唯一一页既没有立绘也没有插图的——十二张区域横幅都在，独它裸着。
        /// 缺图时照旧退成无插图布局（fail-open）。
        /// </summary>
        internal static Sprite GetJournalBanner()
        {
            return Get(JournalSceneAsset);
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
        private const string CaptionScrimAssetName = "__skyisland_caption_scrim";

        /// <summary>
        /// 区域大标题压暗底的最大不透明度：压暗底 Image 的 alpha。贴图本身只存形状（峰值恒为 1），
        /// 颜色与峰值都由调用方施加，见 <see cref="GetScrim"/>。
        ///
        /// 【数值按游戏的线性色彩空间定（2026-09-14 审核 F-01）】鸭科夫是 Linear 色彩空间，UI 半透明在线性光里混合：
        /// 近黑色 α=0.72 只把背后的亮度乘 0.28。旧口径在 sRGB 值上混合、还把相对亮度当 sRGB 灰度代入，
        /// 算出眉题 5.70:1，线性复算实际只有 2.13:1。现值 0.84 配合眉题与提示行改用 TextPrimary，
        /// 云海高光（相对亮度 p99=0.839）下两行小字 5.14:1（守卫按线性混合复算）。
        /// </summary>
        internal const float ScrimPeak = 0.84f;
        /// <summary>
        /// 底部字幕压暗底的最大不透明度。比大标题高一档：字幕里有警示色（WarningText 相对亮度 0.64），
        /// 云海高光下要到 0.90 才过 4.5:1。主流游戏的字幕底框不透明度也在 75%–90% 之间。
        /// </summary>
        internal const float CaptionScrimPeak = 0.90f;
        /// <summary>
        /// 压暗底**竖向**两端各占多少比例做 smoothstep 淡出；中间 <c>1-2×</c> 是平台。
        ///
        /// 平台必须罩住每一行文字（否则小字又掉进淡出区），但淡出区也必须够长——
        /// 0.20 那一版在 230 高上只有 46px 的过渡，整块压暗底在云海上看着就是**一块深色板**，
        /// 上下两条边清清楚楚，正是这个文件一开始就想消灭的东西。0.28 配上加高到 340 的
        /// 压暗底是 95px 的过渡，读作一团柔光晕；三行文字仍整个落在平台里
        /// （守卫 SkyIslandUiContrastGuard 按文字框的上下两端复算，不是只算中心）。
        /// </summary>
        internal const float ScrimEdge = 0.28f;
        /// <summary>
        /// 区域大标题压暗底**横向**两侧各占多少比例做 smoothstep 淡出；中间 <c>1-2×</c> 是平台。
        /// 调用方要保证文字整行落在平台里——<c>SkyIslandHud.FitBannerScrim</c> 按实测文字宽度
        /// 反算压暗底宽度（<c>宽度 ≥ 文字宽 / (1-2×edge)</c>），英文长句不会跑到淡出区里去。
        /// </summary>
        internal const float ScrimHorizontalEdge = 0.30f;
        /// <summary>
        /// 底部字幕压暗底横向两侧的淡出比例。比大标题窄：字幕框宽 1000，平台若只占 40%，
        /// 整行坐进平台要 2500 宽的压暗底、出屏；0.15 时平台占 70%，最宽一行配 1429 宽就够
        /// （<c>SkyIslandHud.FitCaptionScrim</c>）。旧写法宽度写死 1180，平台只剩中间 472 px，
        /// 800 px 宽的一行行尾压暗只剩 0.40（2026-09-14 审核 F-04）。
        /// </summary>
        internal const float CaptionScrimHorizontalEdge = 0.15f;

        /// <summary>
        /// 「两端 smoothstep 淡出 + 中间平台」的一维窗函数。<paramref name="t"/> 取 0..1，
        /// <paramref name="edge"/> 是单侧淡出占的比例（0.18 = 两端各 18%、中间 64% 平台）。
        /// </summary>
        private static float Plateau(float t, float edge)
        {
            if (edge <= 0f) return 1f;
            float head = Mathf.Clamp01(t / edge);
            float tail = Mathf.Clamp01((1f - t) / edge);
            return head * head * (3f - 2f * head) * tail * tail * (3f - 2f * tail);
        }

        /// <summary>
        /// 压暗底在**相对高度** <paramref name="v"/>（0=底边，1=顶边）处的不透明度。
        /// 守卫与离线预览按这个函数复算三行文字各自坐在多厚的底上，**不要在别处再写一份**。
        /// </summary>
        internal static float ScrimAlphaAt(float v)
        {
            return Plateau(Mathf.Clamp01(v), ScrimEdge) * ScrimPeak;
        }

        /// <summary>
        /// 区域大标题背后的压暗底。**必须有**：岛上抬头就是一片高亮的云海，
        /// 浅色文字直接压在云上几乎读不出来（离线预览里一眼可见）。
        /// 主流游戏的区域名底下也都垫一层柔和压暗，这不是装饰而是可读性。
        ///
        /// 形状是**二维**柔边：两个方向都是「两侧 smoothstep 淡出 + 中间一段平台」。
        /// 早先那版只做了竖向渐变、横向拉伸成矩形，左右两侧是两条笔直的硬边——那是第一版的病。
        ///
        /// 【竖向为什么从余弦钟形改成平台（CR-2026-09-13-001）】
        ///   钟形 <c>0.5-0.5·cos(2πv)</c> 的峰值在正中，而正中坐的是 44px 的大地名——
        ///   它按 WCAG 只需要 3:1。真正需要 4.5:1 的两行小字（眉题 y=+44、落地提示 y=-42）
        ///   被甩到钟形的腰上：在 230 高的压暗底里 v=0.691 / 0.317，α 只有 0.405 / 0.423。
        ///   实算（场景亮度取 12 张场景横幅实测 p90=0.679）：眉题 **2.42:1**、提示行 **2.54:1**，
        ///   云海高光（p99=0.839）下更是 1.66 / 1.75——离线预览里一眼可见糊在云里。
        ///   改成上下各 <see cref="ScrimEdge"/> 的 smoothstep、中间平台之后，三行全部坐在平台上。
        ///
        /// 压暗底之外没有第二层保险（文字不带描边），云海高光那一档全靠这里的峰值与文字色撑住，
        /// 数值见 <see cref="ScrimPeak"/>。
        /// </summary>
        internal static Sprite GetTitleScrim()
        {
            return GetScrim(ScrimAssetName, ScrimHorizontalEdge);
        }

        /// <summary>底部字幕的压暗底：竖向形状同大标题，横向淡出更窄（见 <see cref="CaptionScrimHorizontalEdge"/>）。</summary>
        internal static Sprite GetCaptionScrim()
        {
            return GetScrim(CaptionScrimAssetName, CaptionScrimHorizontalEdge);
        }

        /// <summary>
        /// 压暗底贴图：**纯白 + 形状 alpha，峰值恒为 1**。颜色（<see cref="BossRushUIColors.Backdrop"/>）与峰值不透明度
        /// （<see cref="ScrimPeak"/> / <see cref="CaptionScrimPeak"/>）由调用方的 <c>Image.color</c> 施加：
        /// 大标题与字幕共用形状、各用各的峰值，也不再在 SetPixel 里写死一份遮罩色。
        /// </summary>
        private static Sprite GetScrim(string assetName, float horizontalEdge)
        {
            Sprite cached;
            if (sprites.TryGetValue(assetName, out cached)) return cached;
            Sprite result = null;
            try
            {
                const int width = 64;
                const int height = 48;
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                texture.name = assetName;
                texture.wrapMode = TextureWrapMode.Clamp;
                // 双线性采样会把这张小图平滑拉到上千像素宽（大标题最宽 1700×340），所以 64×48 足够，不必出大图。
                texture.filterMode = FilterMode.Bilinear;
                for (int x = 0; x < width; x++)
                {
                    float horizontal = Plateau(x / (float)(width - 1), horizontalEdge);
                    for (int y = 0; y < height; y++)
                    {
                        float vertical = Plateau(y / (float)(height - 1), ScrimEdge);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, horizontal * vertical));
                    }
                }
                texture.Apply(false, true);
                owned.Add(texture);
                result = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                    100f, 0u, SpriteMeshType.FullRect);
                result.name = assetName;
                owned.Add(result);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "压暗底生成失败：" + e.Message);
            }
            sprites[assetName] = result;
            return result;
        }

        private const string GlowAssetName = "__skyisland_radial_glow";

        /// <summary>
        /// 径向柔光：中心实、往外 smoothstep 淡到 0，四角（d>1）直接为 0 所以不出方边。
        /// 纯白 + alpha，颜色由调用方的 <c>Image.color</c> / <c>SpriteRenderer.color</c> 施加。
        ///
        /// 两处在用，是**同一张**图不是两份：
        /// - 剧情面板主视觉里立绘脚下的落影（抠图直接压在插图上，没有影子会「浮」着）；
        /// - 采集点的贴地光斑（<see cref="SkyIslandGathering"/>，按资源种类 tint）。
        /// UI 一侧由 RectTransform 显式给尺寸，<c>pixelsPerUnit</c> 不起作用；**世界空间的 SpriteRenderer 不会拉伸精灵**，
        /// 原生尺寸就是 64 px / PPU 100 = 0.64 m，要多大得按比例缩放（见 <c>SkyIslandGathering.GlowDiscSize</c>）。
        /// </summary>
        internal static Sprite GetRadialGlow()
        {
            Sprite cached;
            if (sprites.TryGetValue(GlowAssetName, out cached)) return cached;
            Sprite result = null;
            try
            {
                const int size = 64;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                texture.name = GlowAssetName;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                Color32[] pixels = new Color32[size * size];
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) / center;
                        float dy = (y - center) / center;
                        float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        pixels[y * size + x] = new Color32(255, 255, 255,
                            (byte)Mathf.RoundToInt(t * t * (3f - 2f * t) * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                owned.Add(texture);
                result = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                    100f, 0u, SpriteMeshType.FullRect);
                result.name = GlowAssetName;
                owned.Add(result);
            }
            catch (Exception e)
            {
                // 纯观感层：落影/光斑出不来不影响任何交互。
                Debug.LogWarning(LogPrefix + "径向柔光生成失败：" + e.Message);
            }
            sprites[GlowAssetName] = result;
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

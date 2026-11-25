// ============================================================================
// BossRushInkSkin.cs - 共享 UI 库的水墨主题：宣纸底、墨笔边、毛笔笔触、朱印、墨晕与落印动效
// ============================================================================
// 模块说明：
//   2026-11-24 玩家愿望批次（出征令、星宿祈向、星灯、心意簿、献魂、钟罩 HUD），owner 定的方向是「水墨东方」，
//   要求「UI、交互逻辑、特效一定要高级高端大气史诗感，无塑料感」。参照《黑神话：悟空》的菜单：
//   近黑墨色底、宣纸米色字、鎏金细笔、朱砂印章；配色守在原版的暖色调里。
//   规范全文在 docs/architecture/UI制作共识.md 第 12 节；这里是它的唯一实现。
//
//   【为什么全部程序化】宣纸纤维、墨笔飞白、印章崩边都是噪声问题，运行时按固定种子生成、缓存一份，
//   和 BossRushUIDepth 的投影 / 斜面同一个做法：不占包体，不依赖生图网关（中文毛笔字 AI 生成常写错笔画），
//   换参数就能整体调质感。所有贴图都是白色 + alpha，颜色由 Image.color 决定（token 在 BossRushInkColors）。
//
//   【和质感层的关系】按钮仍然经过 ZombieModeUIHelper.SetButtonBaseColor，拿到官方 UI/hover、UI/click 音效、
//   按下微缩与外投影；只把圆角矩形的「顶边斜面高光」换成墨笔边——圆角高光压在崩边墨块上正是塑料感的来源。
//   出征令这类专门的水墨面板用 ApplyInkPanel 套墨边与纤维。2026-11-25 起共享库整体走水墨主题（BossRushInkTheme）：
//   ApplyPanelSkin / ApplyPanelStroke / 按钮质感 / 打开动效都换成这里的贴图，其余界面调用方一行不改。
//
//   【动效】只用 BossRushUI.EaseOut（位移、缩放）与 BossRushUI.SmoothStep（原地淡变），unscaled 时间，
//   官方暂停菜单打开时冻结。慢而沉：0.35–0.6 秒，不回弹。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BossRush
{
    /// <summary>
    /// 水墨主题的设计 token。2026-11-25 起全项目统一走水墨主题（BossRushInkTheme）：<see cref="BossRushUIColors"/> 的
    /// 同名语义 token 与这里同值（Surface / SurfaceRaised / Paper→TextPrimary / PaperDim→TextSecondary / Gold→Accent /
    /// CinnabarFill→AccentFill / Cinnabar→Danger / CinnabarText→DangerText），这里另有 Gold / GoldSoft / InkWash / Ember 等专用色。
    /// </summary>
    internal static class BossRushInkColors
    {
        /// <summary>面板墨色底（#14110F 附近，略透出背后场景）。</summary>
        internal static readonly Color Surface = new Color(0.078f, 0.067f, 0.059f, 0.90f);
        /// <summary>卡片、次级按钮：比底色亮一档的墨。</summary>
        internal static readonly Color SurfaceRaised = new Color(0.125f, 0.105f, 0.088f, 0.94f);
        /// <summary>墨晕、笔触底衬：最深的墨。</summary>
        internal static readonly Color InkWash = new Color(0.018f, 0.014f, 0.012f, 1f);
        /// <summary>正文：宣纸色（#F1E9D6），与 BossRushUIColors.TextPrimary 同值。在 Surface 上约 15:1。</summary>
        internal static readonly Color Paper = new Color(0.945f, 0.915f, 0.840f, 1f);
        /// <summary>次要文字：旧纸色。在 Surface 上约 6.5:1。</summary>
        internal static readonly Color PaperDim = new Color(0.690f, 0.640f, 0.560f, 1f);
        /// <summary>鎏金（#C9A24A）：细笔描边、选中、强调字。</summary>
        internal static readonly Color Gold = new Color(0.788f, 0.635f, 0.290f, 1f);
        /// <summary>淡金：常态描边、英文副标题。</summary>
        internal static readonly Color GoldSoft = new Color(0.788f, 0.635f, 0.290f, 0.55f);
        /// <summary>朱砂（#B0302A）：印章、危险描边。</summary>
        internal static readonly Color Cinnabar = new Color(0.690f, 0.188f, 0.165f, 1f);
        /// <summary>主操作填充：压深的朱砂，宣纸字在上面约 8:1。每屏最多一处。</summary>
        internal static readonly Color CinnabarFill = new Color(0.420f, 0.118f, 0.098f, 1f);
        /// <summary>危险操作的字色：浅朱，在 Surface 上约 9:1；危险按钮悬停底上仍过 4.5:1（与 DangerText 同值）。</summary>
        internal static readonly Color CinnabarText = new Color(0.960f, 0.620f, 0.550f, 1f);
        /// <summary>余烬：噩梦卡边缘、魂火、灯火。</summary>
        internal static readonly Color Ember = new Color(1f, 0.620f, 0.280f, 1f);
    }

    /// <summary>水墨按钮的三种角色，对应 UI 共识的主 / 次 / 危险。</summary>
    internal enum BossRushInkButtonRole
    {
        Secondary = 0,
        Primary = 1,
        Danger = 2
    }

    /// <summary>
    /// 水墨主题的贴图、构件与动效。全部幂等、可重复调用。
    /// </summary>
    internal static class BossRushInkSkin
    {
        internal const string FrameName = "InkFrame";
        internal const string FiberName = "InkFiber";
        internal const string BloomName = "InkBloom";
        /// <summary>水墨音效目录：Assets/Sounds/InkUI，由 tools/gen_ink_ui_sfx.py 程序化生成。</summary>
        internal const string SoundFolder = "InkUI";

        internal const string SfxSealStamp = "seal_stamp.wav";
        internal const string SfxPaperTurn = "paper_turn.wav";
        internal const string SfxBrushSweep = "brush_sweep.wav";
        internal const string SfxTempleBell = "temple_bell.wav";
        internal const string SfxChimeStone = "chime_stone.wav";
        internal const string SfxWarDrum = "war_drum.wav";
        internal const string SfxBellHum = "bell_hum.wav";
        internal const string SfxBellShatter = "bell_shatter.wav";
        internal const string SfxBellDing = "bell_ding.wav";
        internal const string SfxEmberFlare = "ember_flare.wav";
        internal const string SfxSoulRise = "soul_rise.wav";

        private const int Seed = 20261124;

        private static Sprite surfaceSprite;
        private static Sprite fiberSprite;
        private static Sprite frameSprite;
        private static Sprite brushSprite;
        private static Sprite sealSprite;
        private static Sprite blotSprite;
        private static Sprite ringSprite;
        private static Sprite glowSprite;
        private static bool soundWarned;

        // ------------------------------------------------------------------
        // 贴图（白色 + alpha，按固定种子程序化生成，缓存一份）
        // ------------------------------------------------------------------

        /// <summary>崩边墨块：边缘是被纸吃掉的不规则毛边，中心平整。九宫格，border 28。</summary>
        internal static Sprite GetSurfaceSprite()
        {
            if (surfaceSprite != null) return surfaceSprite;
            const int size = 96;
            const int border = 28;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int edge;
                    float along;
                    float d = EdgeDistance(x, y, size, size, out edge, out along);
                    // 崩边深度 3–11px，沿边方向的低频噪声决定哪里被纸吃得深。
                    float bite = 3f + 8f * Fbm(along * 0.075f, edge * 13.7f, Seed + 1, 3);
                    float alpha = Smooth01((d - bite + 2.5f) / 5f);
                    // 上下边缘的横向飞白：笔刷擦过时留下的细缝。
                    if (edge == 1 || edge == 3)
                    {
                        float streak = Fbm(x * 0.012f, y * 0.85f, Seed + 2, 2);
                        float near = 1f - Mathf.Clamp01((d - bite) / 14f);
                        if (streak > 0.62f) alpha *= 1f - 0.55f * near;
                    }
                    // 中心区（九宫格拉伸部分）保持完全平整，免得噪声被拉成条纹。
                    float lum = d >= border ? 1f : 1f - 0.05f * Fbm(x * 0.09f, y * 0.09f, Seed + 3, 2);
                    pixels[y * size + x] = ToPixel(lum, alpha);
                }
            }
            surfaceSprite = BuildSprite("BossRushInk_Surface", size, size, pixels, new Vector4(border, border, border, border));
            return surfaceSprite;
        }

        /// <summary>宣纸纤维：可平铺的淡纤维与纸纹，用很低的 alpha 叠在墨底上。</summary>
        internal static Sprite GetFiberSprite()
        {
            if (fiberSprite != null) return fiberSprite;
            const int size = 128;
            float[] acc = new float[size * size];
            System.Random rng = new System.Random(Seed + 4);
            // 纸纹底：周期噪声，四边可无缝平铺。
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    acc[y * size + x] = 0.18f * PeriodicNoise(x, y, size, 16, Seed + 5);
                }
            }
            // 纤维：随机短弧，坐标取模保证平铺。
            for (int f = 0; f < 90; f++)
            {
                float px = (float)rng.NextDouble() * size;
                float py = (float)rng.NextDouble() * size;
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float curve = ((float)rng.NextDouble() - 0.5f) * 0.12f;
                int length = 10 + rng.Next(26);
                float strength = 0.25f + (float)rng.NextDouble() * 0.45f;
                for (int s = 0; s < length; s++)
                {
                    angle += curve;
                    px += Mathf.Cos(angle);
                    py += Mathf.Sin(angle);
                    int ix = Mod(Mathf.RoundToInt(px), size);
                    int iy = Mod(Mathf.RoundToInt(py), size);
                    float fade = Mathf.Sin(Mathf.PI * s / length);
                    int index = iy * size + ix;
                    acc[index] = Mathf.Max(acc[index], strength * fade);
                }
            }
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = ToPixel(1f, Mathf.Clamp01(acc[i]));
            fiberSprite = BuildSprite("BossRushInk_Fiber", size, size, pixels, Vector4.zero);
            return fiberSprite;
        }

        /// <summary>墨笔边框：沿四边的一道干笔，粗细起伏、有飞白断口，四角积墨。九宫格，border 40。</summary>
        internal static Sprite GetFrameSprite()
        {
            if (frameSprite == null) frameSprite = BuildFrameSprite("BossRushInk_Frame");
            return frameSprite;
        }

        private static Sprite BuildFrameSprite(string name)
        {
            const int size = 128;
            const int border = 40;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int edge;
                    float along;
                    float d = EdgeDistance(x, y, size, size, out edge, out along);
                    float center = 10f + 2.4f * (Fbm(along * 0.03f, edge * 5.1f + 11f, Seed + 6, 2) - 0.5f);
                    float width = 1.3f + 1.7f * Fbm(along * 0.05f, edge * 3.3f, Seed + 7, 2);
                    // 四角积墨：两条边都近的地方笔更粗。
                    float cornerX = Mathf.Min(x, size - 1 - x);
                    float cornerY = Mathf.Min(y, size - 1 - y);
                    float corner = 1f - Mathf.Clamp01((Mathf.Max(cornerX, cornerY) - 6f) / 22f);
                    width *= 1f + 0.9f * corner;
                    float t = (d - center) / width;
                    float alpha = Mathf.Exp(-t * t);
                    // 飞白：沿笔方向的断口，越往笔尾（每条边的后半段）越干。
                    float gap = Fbm(along * 0.11f, edge * 7.9f + d * 0.35f, Seed + 8, 2);
                    alpha *= 0.18f + 0.82f * Smooth01((gap - 0.28f) / 0.18f);
                    alpha *= 0.72f + 0.28f * Fbm(along * 0.6f, d * 1.9f, Seed + 9, 1);
                    if (d > border - 2) alpha = 0f;
                    pixels[y * size + x] = ToPixel(1f, Mathf.Clamp01(alpha * 1.15f));
                }
            }
            return BuildSprite(name, size, size, pixels, new Vector4(border, border, border, border));
        }

        /// <summary>
        /// 一笔横画：起笔圆头、行笔有起伏、收笔干裂分叉。九宫格左右各留笔头笔尾（72 / 96），中段可拉长。
        /// 用于标题底衬、选中下划线、分隔笔。
        /// </summary>
        internal static Sprite GetBrushSprite()
        {
            if (brushSprite != null) return brushSprite;
            const int width = 512;
            const int height = 64;
            Color32[] pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float head = Mathf.Clamp01(u / 0.07f);
                head = Mathf.Sqrt(head);
                float tail = Mathf.Clamp01((1f - u) / 0.24f);
                tail = Mathf.Pow(tail, 0.75f);
                float swell = 1f + 0.12f * Mathf.Sin(u * 7.3f) + 0.25f * Mathf.Clamp01(1f - u / 0.15f);
                float half = height * 0.34f * Mathf.Min(head, tail) * swell;
                float wobble = 2.2f * (Fbm(u * 3.1f, 0.5f, Seed + 10, 2) - 0.5f) - 2.5f * (1f - tail);
                float centerY = height * 0.5f + wobble;
                float dry = 0.12f + 0.88f * Smooth01((u - 0.45f) / 0.5f);
                for (int y = 0; y < height; y++)
                {
                    float rag = 1f + 0.18f * (Fbm(u * 18f, y * 0.12f, Seed + 11, 2) - 0.5f);
                    float dy = Mathf.Abs(y - centerY) / Mathf.Max(0.5f, half * rag);
                    float alpha = dy >= 1f ? 0f : 1f - Smooth01((dy - 0.78f) / 0.22f);
                    // 笔毛：沿 x 拉长的条纹，越往笔尾越干，断成丝。
                    float bristle = Fbm(x * 0.012f, y * 0.55f, Seed + 12, 2);
                    if (bristle < dry * 0.62f) alpha *= 0.12f + 0.3f * bristle;
                    alpha *= 0.86f + 0.14f * Fbm(x * 0.08f, y * 0.4f, Seed + 13, 1);
                    pixels[y * width + x] = ToPixel(1f, Mathf.Clamp01(alpha));
                }
            }
            brushSprite = BuildSprite("BossRushInk_Brush", width, height, pixels, new Vector4(72, 0, 96, 0));
            return brushSprite;
        }

        /// <summary>朱印底：方印崩边、内框一道、印泥不匀的斑驳。白文印：字由调用方叠在上面。</summary>
        internal static Sprite GetSealSprite()
        {
            if (sealSprite != null) return sealSprite;
            const int size = 128;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int edge;
                    float along;
                    float d = EdgeDistance(x, y, size, size, out edge, out along);
                    float bite = 5f + 5f * Fbm(along * 0.09f, edge * 17.1f, Seed + 14, 2);
                    float alpha = Smooth01((d - bite + 1.5f) / 3f);
                    // 内框：留白一道细线（印石刻出的边栏）。
                    float frameCenter = 15f;
                    float frame = Mathf.Abs(d - frameCenter);
                    if (frame < 2.2f && Fbm(along * 0.2f, edge * 3f, Seed + 15, 1) > 0.18f) alpha *= 0.08f;
                    // 印泥斑驳：零星浅点。
                    float speck = Fbm(x * 0.21f, y * 0.21f, Seed + 16, 2);
                    if (speck > 0.74f) alpha *= 0.45f;
                    pixels[y * size + x] = ToPixel(1f, alpha);
                }
            }
            sealSprite = BuildSprite("BossRushInk_Seal", size, size, pixels, Vector4.zero);
            return sealSprite;
        }

        /// <summary>墨晕：一滴墨在纸上洇开的形状，边缘不规则、带几点溅墨。</summary>
        internal static Sprite GetBlotSprite()
        {
            if (blotSprite != null) return blotSprite;
            const int size = 256;
            Color32[] pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float theta = Mathf.Atan2(dy, dx);
                    float rim = 0.62f + 0.18f * CircleNoise(theta, 2.4f, Seed + 17) + 0.05f * CircleNoise(theta, 9f, Seed + 18);
                    float alpha = Smooth01((rim - r) / 0.22f);
                    alpha *= 0.78f + 0.22f * Fbm(x * 0.03f, y * 0.03f, Seed + 19, 2);
                    // 溅墨：外圈零星小点。
                    if (r > rim && r < 0.97f)
                    {
                        float splat = Fbm(x * 0.09f, y * 0.09f, Seed + 20, 2);
                        if (splat > 0.78f) alpha = Mathf.Max(alpha, Smooth01((splat - 0.78f) / 0.06f) * 0.6f);
                    }
                    if (r >= 0.99f) alpha = 0f;
                    pixels[y * size + x] = ToPixel(1f, alpha);
                }
            }
            blotSprite = BuildSprite("BossRushInk_Blot", size, size, pixels, Vector4.zero);
            return blotSprite;
        }

        /// <summary>一圈干笔墨环：升级、落印余波、钟罩回满。</summary>
        internal static Sprite GetRingSprite()
        {
            if (ringSprite != null) return ringSprite;
            const int size = 256;
            Color32[] pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float theta = Mathf.Atan2(dy, dx);
                    float center = 0.80f + 0.025f * CircleNoise(theta, 3f, Seed + 21);
                    float width = 0.030f + 0.030f * (0.5f + 0.5f * CircleNoise(theta, 2f, Seed + 22));
                    float t = (r - center) / width;
                    float alpha = Mathf.Exp(-t * t);
                    float gap = 0.5f + 0.5f * CircleNoise(theta, 7f, Seed + 23);
                    alpha *= 0.2f + 0.8f * Smooth01((gap - 0.3f) / 0.2f);
                    pixels[y * size + x] = ToPixel(1f, Mathf.Clamp01(alpha));
                }
            }
            ringSprite = BuildSprite("BossRushInk_Ring", size, size, pixels, Vector4.zero);
            return ringSprite;
        }

        /// <summary>柔光点：灯火、星点、余烬、魂火。</summary>
        internal static Sprite GetGlowSprite()
        {
            if (glowSprite != null) return glowSprite;
            const int size = 64;
            Color32[] pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r2 = dx * dx + dy * dy;
                    float alpha = Mathf.Exp(-r2 * 4.2f) * Mathf.Clamp01(1f - r2);
                    pixels[y * size + x] = ToPixel(1f, alpha);
                }
            }
            glowSprite = BuildSprite("BossRushInk_Glow", size, size, pixels, Vector4.zero);
            return glowSprite;
        }

        // ------------------------------------------------------------------
        // 构件
        // ------------------------------------------------------------------

        /// <summary>
        /// 给一块面板 / 卡片套水墨皮肤：崩边墨底 + 宣纸纤维 + 墨笔边。幂等。
        /// <paramref name="frameColor"/> 传 default 时用淡金。
        /// </summary>
        internal static void ApplyInkPanel(Image surface, bool raised, Color frameColor = default(Color))
        {
            if (surface == null) return;
            surface.sprite = GetSurfaceSprite();
            surface.type = Image.Type.Sliced;
            surface.fillCenter = true;
            surface.pixelsPerUnitMultiplier = raised ? 1.6f : 1f;
            surface.color = raised ? BossRushInkColors.SurfaceRaised : BossRushInkColors.Surface;
            Transform host = surface.transform;
            if (host.Find(FiberName) == null)
            {
                Image fiber = CreateLayer(host, FiberName, GetFiberSprite());
                fiber.type = Image.Type.Tiled;
                fiber.color = new Color(1f, 0.93f, 0.80f, raised ? 0.035f : 0.045f);
                RectTransform rect = fiber.rectTransform;
                rect.offsetMin = new Vector2(10f, 10f);
                rect.offsetMax = new Vector2(-10f, -10f);
                fiber.transform.SetSiblingIndex(0);
            }
            AddInkFrame(surface, frameColor.a > 0f ? frameColor : BossRushInkColors.GoldSoft, raised ? 1.6f : 1f);
        }

        /// <summary>只加墨笔边（卡片选中态换色时调 <see cref="SetFrameColor"/>）。幂等。</summary>
        internal static Image AddInkFrame(Image surface, Color color, float pixelsPerUnitMultiplier = 1f)
        {
            if (surface == null) return null;
            Transform existing = surface.transform.Find(FrameName);
            Image frame = existing != null ? existing.GetComponent<Image>() : null;
            if (frame == null)
            {
                frame = CreateLayer(surface.transform, FrameName, GetFrameSprite());
                frame.type = Image.Type.Sliced;
                frame.fillCenter = false;
                RectTransform rect = frame.rectTransform;
                rect.offsetMin = new Vector2(-4f, -4f);
                rect.offsetMax = new Vector2(4f, 4f);
            }
            frame.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            frame.color = color;
            return frame;
        }

        internal static void SetFrameColor(Component host, Color color)
        {
            if (host == null) return;
            Transform frame = host.transform.Find(FrameName);
            Image image = frame != null ? frame.GetComponent<Image>() : null;
            if (image != null) image.color = color;
        }

        /// <summary>
        /// 水墨按钮。底色仍走 <see cref="ZombieModeUIHelper.SetButtonBaseColor"/>（官方音效、按下微缩、外投影），
        /// 底图换成崩边墨块、加墨笔边，顶边斜面换掉。所有 TMP 标签改成宣纸 / 浅朱字并用游戏字体。
        /// </summary>
        internal static void StyleInkButton(Button button, BossRushInkButtonRole role)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null)
            {
                image.sprite = GetSurfaceSprite();
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 2.4f;
                SuppressBevel(image.transform);
            }
            Color baseColor = role == BossRushInkButtonRole.Primary ? BossRushInkColors.CinnabarFill : BossRushInkColors.SurfaceRaised;
            ZombieModeUIHelper.SetButtonBaseColor(button, baseColor);
            if (image != null)
            {
                Color frame = role == BossRushInkButtonRole.Primary ? BossRushInkColors.Gold
                    : role == BossRushInkButtonRole.Danger ? BossRushInkColors.Cinnabar : BossRushInkColors.GoldSoft;
                AddInkFrame(image, frame, 2.4f);
            }
            Color text = role == BossRushInkButtonRole.Danger ? BossRushInkColors.CinnabarText : BossRushInkColors.Paper;
            TextMeshProUGUI[] labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                BossRushUI.ApplyGameFont(labels[i]);
                labels[i].color = text;
                labels[i].characterSpacing = 6f;
            }
        }

        /// <summary>
        /// 毛笔标题：墨笔底衬 + 宣纸大字（字距拉开）+ 鎏金一笔 + 英文小字。返回根节点，调用方定位。
        /// 金笔会在 <see cref="PlayInkReveal"/> 时从左到右写出来。
        /// </summary>
        internal static RectTransform CreateBrushTitle(Transform parent, string zh, string en, float fontSize)
        {
            GameObject root = ZombieModeUIHelper.CreateRect("InkTitle", parent, new Vector2(0.5f, 0.5f), new Vector2(900f, fontSize * 2.4f));
            RectTransform rootRect = root.GetComponent<RectTransform>();

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText("Title", root.transform, zh, fontSize,
                new Vector2(0f, fontSize * 0.28f), new Vector2(900f, fontSize * 1.45f + 4f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            title.characterSpacing = 28f;
            title.fontStyle = FontStyles.Bold;
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Overflow;
            float textWidth = Mathf.Max(fontSize * 2f, title.GetPreferredValues(zh).x);

            Image underlay = CreateLayer(root.transform, "TitleUnderlay", GetBrushSprite());
            underlay.type = Image.Type.Sliced;
            underlay.pixelsPerUnitMultiplier = 0.7f;
            underlay.color = new Color(BossRushInkColors.InkWash.r, BossRushInkColors.InkWash.g, BossRushInkColors.InkWash.b, 0.82f);
            RectTransform underlayRect = underlay.rectTransform;
            underlayRect.anchorMin = underlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            underlayRect.sizeDelta = new Vector2(textWidth + fontSize * 3.2f, fontSize * 1.9f);
            underlayRect.anchoredPosition = new Vector2(-fontSize * 0.2f, fontSize * 0.22f);
            underlay.transform.SetSiblingIndex(0);

            RectTransform stroke = CreateBrushStroke(root.transform, "TitleStroke", new Vector2(textWidth + fontSize * 1.2f, 12f),
                new Vector2(0f, -fontSize * 0.62f), BossRushInkColors.Gold);
            stroke.gameObject.AddComponent<BossRushInkRevealStroke>();

            if (!string.IsNullOrEmpty(en))
            {
                TextMeshProUGUI sub = ZombieModeUIHelper.CreateText("Subtitle", root.transform, en.ToUpperInvariant(), Mathf.Round(fontSize * 0.36f),
                    new Vector2(0f, -fontSize * 0.98f), new Vector2(900f, Mathf.Round(fontSize * 0.36f) * 1.45f + 4f),
                    TextAlignmentOptions.Center, BossRushInkColors.GoldSoft);
                sub.characterSpacing = 36f;
                sub.enableWordWrapping = false;
            }
            return rootRect;
        }

        /// <summary>
        /// 一道墨笔（RectMask2D 包着整笔）：<see cref="PlayBrushWrite"/> 让它从左到右「写」出来。
        /// 返回外层遮罩节点。
        /// </summary>
        internal static RectTransform CreateBrushStroke(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            GameObject mask = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            RectTransform maskRect = mask.GetComponent<RectTransform>();
            maskRect.anchoredPosition = position;
            mask.AddComponent<RectMask2D>();
            GameObject inner = ZombieModeUIHelper.CreateRect("Brush", mask.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, size, new Vector2(0f, 0.5f));
            Image image = inner.AddComponent<Image>();
            image.sprite = GetBrushSprite();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 64f / Mathf.Max(4f, size.y);
            image.color = color;
            image.raycastTarget = false;
            return maskRect;
        }

        /// <summary>白文朱印：朱砂印底 + 宣纸色字。一字或两字横排，四字两行。</summary>
        internal static RectTransform CreateSeal(Transform parent, string zh, float size)
        {
            GameObject seal = ZombieModeUIHelper.CreateRect("InkSeal", parent, new Vector2(0.5f, 0.5f), new Vector2(size, size));
            Image image = seal.AddComponent<Image>();
            image.sprite = GetSealSprite();
            image.type = Image.Type.Simple;
            image.color = BossRushInkColors.Cinnabar;
            image.raycastTarget = false;
            string text = zh ?? string.Empty;
            if (text.Length == 4) text = text.Substring(0, 2) + "\n" + text.Substring(2, 2);
            float glyph = text.Length <= 1 ? size * 0.56f : (text.Contains("\n") ? size * 0.30f : size * 0.34f);
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("SealText", seal.transform, text, glyph,
                Vector2.zero, new Vector2(size * 0.86f, size * 0.86f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            label.fontStyle = FontStyles.Bold;
            label.enableWordWrapping = false;
            label.lineSpacing = -18f;
            label.characterSpacing = text.Length == 2 ? -4f : 0f;
            return seal.GetComponent<RectTransform>();
        }

        /// <summary>分隔笔：一道淡金横笔，代替直线分隔线。</summary>
        internal static RectTransform CreateInkDivider(Transform parent, float width, Vector2 position)
        {
            return CreateBrushStroke(parent, "InkDivider", new Vector2(width, 8f), position,
                new Color(BossRushInkColors.Gold.r, BossRushInkColors.Gold.g, BossRushInkColors.Gold.b, 0.32f));
        }

        // ------------------------------------------------------------------
        // 动效
        // ------------------------------------------------------------------

        /// <summary>
        /// 面板打开：背后一滴墨洇开（0.6 秒，停在淡淡的墨池），面板从 0.97 缓缓放到 1 并淡入，
        /// 标题金笔随后写出。面板需要有 CanvasGroup（没有就加）。
        /// </summary>
        internal static void PlayInkReveal(RectTransform panel)
        {
            if (panel == null) return;
            BossRushInkMotion motion = BossRushInkMotion.For(panel.gameObject);
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            panel.localScale = Vector3.one * 0.97f;

            Transform parent = panel.parent;
            if (parent != null && parent.Find(BloomName) == null)
            {
                GameObject bloomObject = ZombieModeUIHelper.CreateRect(BloomName, parent, new Vector2(0.5f, 0.5f), Vector2.one);
                RectTransform bloom = bloomObject.GetComponent<RectTransform>();
                bloom.anchorMin = panel.anchorMin;
                bloom.anchorMax = panel.anchorMax;
                bloom.anchoredPosition = panel.anchoredPosition;
                Vector2 size = panel.rect.size;
                float side = Mathf.Max(size.x, size.y) * 1.28f;
                bloom.sizeDelta = new Vector2(side, side * Mathf.Clamp(size.y / Mathf.Max(1f, size.x), 0.55f, 1f));
                bloom.SetSiblingIndex(panel.GetSiblingIndex());
                Image blot = bloomObject.AddComponent<Image>();
                blot.sprite = GetBlotSprite();
                blot.color = new Color(BossRushInkColors.InkWash.r, BossRushInkColors.InkWash.g, BossRushInkColors.InkWash.b, 0f);
                blot.raycastTarget = false;
                motion.Add(0f, 0.6f, true, t =>
                {
                    if (bloom == null) return;
                    bloom.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, t);
                    blot.color = new Color(blot.color.r, blot.color.g, blot.color.b, Mathf.Lerp(0f, 0.55f, Mathf.Min(1f, t * 1.6f)));
                });
            }
            motion.Add(0.06f, 0.45f, false, t => { if (group != null) group.alpha = t; });
            motion.Add(0.06f, 0.5f, true, t => { if (panel != null) panel.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, t); });
            BossRushInkRevealStroke[] strokes = panel.GetComponentsInChildren<BossRushInkRevealStroke>(true);
            for (int i = 0; i < strokes.Length; i++)
            {
                PlayBrushWrite(strokes[i].transform as RectTransform, 0.32f + i * 0.08f, 0.42f, motion);
            }
            PlaySfx(SfxBrushSweep);
        }

        /// <summary>面板关闭：墨池收拢，面板随共享关闭动画淡出销毁。调用方先释放输入租约。</summary>
        internal static void PlayInkDismiss(GameObject root, RectTransform panel)
        {
            if (root == null) return;
            if (panel != null && panel.parent != null)
            {
                Transform bloomTransform = panel.parent.Find(BloomName);
                RectTransform bloom = bloomTransform as RectTransform;
                if (bloom != null)
                {
                    Vector3 start = bloom.localScale;
                    BossRushInkMotion.For(root).Add(0f, 0.2f, true, t => { if (bloom != null) bloom.localScale = start * Mathf.Lerp(1f, 0.6f, t); });
                }
            }
            BossRushUIKit.PlayCloseAndDestroy(root, 0.2f);
        }

        /// <summary>让一道 <see cref="CreateBrushStroke"/> 从左到右写出来。</summary>
        internal static void PlayBrushWrite(RectTransform strokeMask, float delay, float seconds, BossRushInkMotion motion = null)
        {
            if (strokeMask == null) return;
            if (motion == null) motion = BossRushInkMotion.For(strokeMask.gameObject);
            float full = strokeMask.sizeDelta.x;
            // 标题金笔可能被重播（面板复用再开、半途重开）：整笔宽度第一次写之前记下，之后都从它写起，不会越写越短。
            BossRushInkRevealStroke marker = strokeMask.GetComponent<BossRushInkRevealStroke>();
            if (marker != null)
            {
                if (marker.FullWidth < 0f) marker.FullWidth = full;
                else full = marker.FullWidth;
            }
            Vector2 pivot = strokeMask.pivot;
            float left = strokeMask.anchoredPosition.x - full * pivot.x;
            strokeMask.pivot = new Vector2(0f, pivot.y);
            strokeMask.anchoredPosition = new Vector2(left, strokeMask.anchoredPosition.y);
            strokeMask.sizeDelta = new Vector2(0f, strokeMask.sizeDelta.y);
            motion.Add(delay, seconds, true, t =>
            {
                if (strokeMask != null) strokeMask.sizeDelta = new Vector2(full * t, strokeMask.sizeDelta.y);
            });
        }

        /// <summary>
        /// 落印：印从 1.7 倍快速按下到 1（缓出、不回弹），落定时外圈一道朱色墨环散开、
        /// <paramref name="shake"/> 轻震一下，配印泥声。
        /// </summary>
        internal static void StampSeal(RectTransform seal, RectTransform shake, float delay)
        {
            if (seal == null) return;
            BossRushInkMotion motion = BossRushInkMotion.For(seal.gameObject);
            CanvasGroup group = seal.GetComponent<CanvasGroup>();
            if (group == null) group = seal.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            seal.localScale = Vector3.one * 1.7f;
            motion.Add(delay, 0.17f, true, t =>
            {
                if (seal == null) return;
                seal.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, t);
                group.alpha = Mathf.Min(1f, t * 2f);
            }, () =>
            {
                if (seal == null) return;
                PlaySfx(SfxSealStamp);
                SpawnRing(seal.parent, seal.anchoredPosition, seal.sizeDelta.x * 1.1f, BossRushInkColors.Cinnabar, 0.4f, 1.7f, 0.42f);
                if (shake != null)
                {
                    Vector2 origin = shake.anchoredPosition;
                    BossRushInkMotion.For(shake.gameObject).Add(0f, 0.16f, false, s =>
                    {
                        if (shake == null) return;
                        float amp = 3.5f * (1f - s);
                        shake.anchoredPosition = origin + new Vector2(0f, -amp * Mathf.Cos(s * 18f));
                    }, () => { if (shake != null) shake.anchoredPosition = origin; });
                }
            });
        }

        /// <summary>在 <paramref name="parent"/> 下放一圈散开的墨环（只做表现，结束自毁）。</summary>
        internal static void SpawnRing(Transform parent, Vector2 position, float size, Color color, float alpha, float growTo, float seconds)
        {
            if (parent == null) return;
            GameObject ringObject = ZombieModeUIHelper.CreateRect("InkRing", parent, new Vector2(0.5f, 0.5f), new Vector2(size, size));
            RectTransform ring = ringObject.GetComponent<RectTransform>();
            ring.anchoredPosition = position;
            Image image = ringObject.AddComponent<Image>();
            image.sprite = GetRingSprite();
            image.raycastTarget = false;
            image.color = new Color(color.r, color.g, color.b, alpha);
            BossRushInkMotion.For(ringObject).Add(0f, seconds, true, t =>
            {
                if (ring == null) return;
                ring.localScale = Vector3.one * Mathf.Lerp(0.8f, growTo, t);
                image.color = new Color(color.r, color.g, color.b, alpha * (1f - t));
            }, () => { if (ringObject != null) Object.Destroy(ringObject); });
        }

        // ------------------------------------------------------------------
        // 音效
        // ------------------------------------------------------------------

        /// <summary>
        /// 放一声水墨界面音效（Assets/Sounds/InkUI）。经官方 PostCustomSFX 挂在主角身上；
        /// 没有主角（读档中）时挂在主摄像机上。文件缺了只警告一次，界面照常。
        /// </summary>
        internal static void PlaySfx(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            GameObject emitter = null;
            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null) emitter = main.gameObject;
            else if (Camera.main != null) emitter = Camera.main.gameObject;
            PlaySfxAt(file, emitter);
        }

        /// <summary>在指定发声体上放一声（世界里的钟罩、光柱用）。</summary>
        internal static void PlaySfxAt(string file, GameObject emitter)
        {
            if (emitter == null || string.IsNullOrEmpty(file)) return;
            try
            {
                if (!BossRushOfficialAudio.HasPostCustomSfx) throw new InvalidOperationException("官方音效入口缺失");
                string path = Path.Combine(Path.Combine(Path.Combine(ModBehaviour.GetModPath(), "Assets"), "Sounds"), Path.Combine(SoundFolder, file));
                if (!File.Exists(path)) throw new FileNotFoundException("水墨界面音效未部署", file);
                object instance;
                Exception error;
                if (!BossRushOfficialAudio.TryPostCustomSfx(path, emitter, false, out instance, out error)) throw error;
            }
            catch (Exception e)
            {
                if (soundWarned) return;
                soundWarned = true;
                Debug.LogWarning("[BossRush] 水墨界面音效不可用（界面照常）：" + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        /// <summary>
        /// 把圆角斜面高光换掉：放一个不显示的同名占位，<see cref="BossRushUIDepth"/> 按名判重就不会再加。
        /// 已经加上的那层直接销毁。
        /// </summary>
        private static void SuppressBevel(Transform host)
        {
            Transform bevel = host.Find(BossRushUIDepth.BevelName);
            if (bevel != null && bevel.GetComponent<Image>() != null)
            {
                Object.Destroy(bevel.gameObject);
                bevel = null;
            }
            if (bevel == null)
            {
                GameObject placeholder = new GameObject(BossRushUIDepth.BevelName, typeof(RectTransform));
                placeholder.transform.SetParent(host, false);
                placeholder.SetActive(false);
            }
        }

        private static Image CreateLayer(Transform host, string name, Sprite sprite)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform));
            layer.transform.SetParent(host, false);
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = layer.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite BuildSprite(string name, int width, int height, Color32[] pixels, Vector4 border)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.hideFlags = HideFlags.DontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Color32 ToPixel(float lum, float alpha)
        {
            byte l = (byte)Mathf.RoundToInt(Mathf.Clamp01(lum) * 255f);
            return new Color32(l, l, l, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }

        /// <summary>像素到最近一条边的距离；<paramref name="edge"/> 0 左 1 下 2 右 3 上，<paramref name="along"/> 是沿该边的坐标。</summary>
        private static float EdgeDistance(int x, int y, int width, int height, out int edge, out float along)
        {
            int left = x, bottom = y, right = width - 1 - x, top = height - 1 - y;
            int best = left;
            edge = 0;
            along = y;
            if (bottom < best) { best = bottom; edge = 1; along = x; }
            if (right < best) { best = right; edge = 2; along = y; }
            if (top < best) { best = top; edge = 3; along = x; }
            return best;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static int Mod(int value, int m)
        {
            int r = value % m;
            return r < 0 ? r + m : r;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static float ValueNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float fx = x - xi;
            float fy = y - yi;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, seed);
            float b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed);
            float d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        /// <summary>分形噪声，输出约 0–1。</summary>
        private static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f;
            float amp = 0.5f;
            float norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(x, y, seed + o * 31);
                norm += amp;
                x *= 2.03f;
                y *= 2.03f;
                amp *= 0.5f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>按圆周取的噪声，绕一圈首尾相接；输出约 -1–1。</summary>
        private static float CircleNoise(float theta, float frequency, int seed)
        {
            float x = Mathf.Cos(theta) * frequency + 50f;
            float y = Mathf.Sin(theta) * frequency + 50f;
            return Fbm(x, y, seed, 2) * 2f - 1f;
        }

        /// <summary>周期为 <paramref name="size"/> 的格点噪声，供可平铺贴图用。</summary>
        private static float PeriodicNoise(int x, int y, int size, int cells, int seed)
        {
            float scale = cells / (float)size;
            float fx = x * scale;
            float fy = y * scale;
            int xi = Mathf.FloorToInt(fx);
            int yi = Mathf.FloorToInt(fy);
            float tx = fx - xi;
            float ty = fy - yi;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash(Mod(xi, cells), Mod(yi, cells), seed);
            float b = Hash(Mod(xi + 1, cells), Mod(yi, cells), seed);
            float c = Hash(Mod(xi, cells), Mod(yi + 1, cells), seed);
            float d = Hash(Mod(xi + 1, cells), Mod(yi + 1, cells), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static void DestroySprite(ref Sprite sprite)
        {
            if (sprite == null) return;
            Texture2D texture = sprite.texture;
            Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
            sprite = null;
        }

        /// <summary>释放程序化贴图（DontSave，必须显式销毁）。由 <see cref="BossRushUI.ResetStaticCaches"/> 调用。</summary>
        internal static void ResetStaticCaches()
        {
            // 全局主题的 Sprite 壳引用这里的纹理，先清壳。
            BossRushInkTheme.ResetStaticCaches();
            DestroySprite(ref surfaceSprite);
            DestroySprite(ref fiberSprite);
            DestroySprite(ref frameSprite);
            DestroySprite(ref brushSprite);
            DestroySprite(ref sealSprite);
            DestroySprite(ref blotSprite);
            DestroySprite(ref ringSprite);
            DestroySprite(ref glowSprite);
            soundWarned = false;
        }
    }

    /// <summary>
    /// 标记：面板打开时要「写」出来的墨笔（<see cref="BossRushInkSkin.PlayInkReveal"/>，
    /// 以及共享打开动效经 <see cref="BossRushInkTheme.WriteRevealStrokes"/>）。
    /// </summary>
    internal sealed class BossRushInkRevealStroke : MonoBehaviour
    {
        /// <summary>整笔宽度；小于 0 = 还没写过。</summary>
        internal float FullWidth = -1f;
    }

    /// <summary>
    /// 水墨动效的驱动器：挂在动效所属的界面物体上，物体销毁动效就停。
    /// unscaled 时间（模态面板会把 timeScale 压到 0），官方暂停菜单开着时冻结。
    /// 缓动只用 <see cref="BossRushUI.EaseOut"/> 与 <see cref="BossRushUI.SmoothStep"/>。
    /// </summary>
    internal sealed class BossRushInkMotion : MonoBehaviour
    {
        private sealed class Track
        {
            internal float Delay;
            internal float Duration;
            internal bool EaseOut;
            internal Action<float> Apply;
            internal Action Done;
            internal float Elapsed;
            internal bool Started;
        }

        private readonly List<Track> tracks = new List<Track>();

        internal static BossRushInkMotion For(GameObject host)
        {
            BossRushInkMotion motion = host.GetComponent<BossRushInkMotion>();
            if (motion == null) motion = host.AddComponent<BossRushInkMotion>();
            motion.enabled = true;
            return motion;
        }

        /// <summary>加一段动效：<paramref name="delay"/> 秒后开始，<paramref name="duration"/> 秒内把 0→1 交给 <paramref name="apply"/>。</summary>
        internal void Add(float delay, float duration, bool easeOut, Action<float> apply, Action done = null)
        {
            if (apply == null) return;
            Track track = new Track { Delay = Mathf.Max(0f, delay), Duration = Mathf.Max(0.01f, duration), EaseOut = easeOut, Apply = apply, Done = done };
            tracks.Add(track);
            if (track.Delay <= 0f)
            {
                track.Started = true;
                SafeApply(track, 0f);
            }
            enabled = true;
        }

        private void Update()
        {
            if (tracks.Count == 0)
            {
                enabled = false;
                return;
            }
            if (BossRushUI.IsGamePaused()) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                Track track = tracks[i];
                track.Elapsed += dt;
                if (track.Elapsed < track.Delay) continue;
                track.Started = true;
                float t = Mathf.Clamp01((track.Elapsed - track.Delay) / track.Duration);
                SafeApply(track, t);
                if (t >= 1f)
                {
                    tracks.RemoveAt(i);
                    if (track.Done != null)
                    {
                        try { track.Done(); }
                        catch (Exception e) { ModBehaviour.DevLog("[InkMotion] 收尾回调失败: " + e.Message); }
                    }
                }
            }
        }

        private static void SafeApply(Track track, float t)
        {
            float eased = track.EaseOut ? BossRushUI.EaseOut(t) : BossRushUI.SmoothStep(t);
            try { track.Apply(eased); }
            catch (Exception e) { ModBehaviour.DevLog("[InkMotion] 动效回调失败: " + e.Message); }
        }

        private void OnDestroy()
        {
            tracks.Clear();
        }
    }
}

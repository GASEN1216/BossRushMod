// ============================================================================
// CollectibleSlotFrameArt.cs - 收集品格子边框的程序化贴图（零新增美术资源）
// ============================================================================
// 全部在第一次用到时烘焙一次、静态缓存，ResetStaticCaches 时销毁（由 CollectibleSlotFrame.ResetStaticCaches 调用）。
// 造型 2026-11-19 先用 Python 原型对着 6–8 档收集品图标调过（鎏金双层斜面线、四角角花、顶边徽记），这里是逐项移植：
//   - 金框：9 宫格，外沿一圈暗铜、中间金身（深→亮→中）、内侧一道高光、再往里一道压暗的阴线，像真画框的斜面；
//   - 内辉 / 外辉：9 宫格白色柔光，运行时乘品质色；外辉贴图里格子外沿在离贴图边 16 单位处；
//   - 角花、徽记：带暗铜描边的金色小饰件，光从左上来；
//   - 星点、光点、扫光带：白色，运行时上色。
// 金框、角花、徽记、星点按 2 倍像素烘焙（每单位 2 像素），高分屏不糊；柔光类 1 倍就够。
// 贴图按 sRGB 存（Texture2D 默认），游戏是 Linear 色彩空间，混合在线性光里做，原型也按这个模型预览。
// 光点没有复用 BossRushProceduralSprites.GetCircleSprite：那张图归新武器的清理链管，它被销毁时这里的图元会变成白方块。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static class CollectibleSlotFrameArt
    {
        /// <summary>金框外沿的圆角半径（UI 单位）。</summary>
        internal const float CornerRadius = 7f;
        /// <summary>金线中线离外沿多深：流光、星点沿这条线走。</summary>
        internal const float LineDepth = 2.6f;
        /// <summary>外辉贴图里格子外沿离贴图边的距离；外辉图元要比格子每边多出这么多才对得齐。</summary>
        internal const float OuterGlowExtent = 16f;

        private const float HiRes = 2f;
        private const float BasePixelsPerUnit = 100f;

        private static readonly Color Rim = new Color(0.22f, 0.13f, 0.04f, 1f);
        private static readonly Color GoldDeep = new Color(0.62f, 0.42f, 0.13f, 1f);
        private static readonly Color GoldBright = new Color(0.98f, 0.83f, 0.45f, 1f);
        private static readonly Color GoldMid = new Color(0.80f, 0.60f, 0.22f, 1f);
        private static readonly Color Highlight = new Color(1f, 0.96f, 0.80f, 1f);
        private static readonly Color Shade = new Color(0.10f, 0.06f, 0.02f, 1f);

        // 金框剖面：离外沿的深度（单位）→ 颜色，相邻两档之间线性插值。
        private static readonly float[] FrameStopDepth = { -0.5f, 0.3f, 1.0f, 1.5f, 2.6f, 3.5f, 3.9f, 4.4f, 4.9f, 5.8f, 7.5f };
        private static readonly Color[] FrameStopColor =
        {
            new Color(Rim.r, Rim.g, Rim.b, 0f), Rim, Rim, GoldDeep, GoldBright, GoldMid, Highlight, Highlight,
            new Color(Shade.r, Shade.g, Shade.b, 0.75f), new Color(Shade.r, Shade.g, Shade.b, 0.5f), new Color(Shade.r, Shade.g, Shade.b, 0f),
        };

        private static Sprite cachedFrame, cachedInnerGlow, cachedOuterGlow, cachedCorner, cachedCrest, cachedSpark, cachedSheen, cachedDot;
        private static readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();

        internal static Sprite Frame { get { return cachedFrame != null ? cachedFrame : (cachedFrame = BakeFrame()); } }
        internal static Sprite InnerGlow { get { return cachedInnerGlow != null ? cachedInnerGlow : (cachedInnerGlow = BakeInnerGlow()); } }
        internal static Sprite OuterGlow { get { return cachedOuterGlow != null ? cachedOuterGlow : (cachedOuterGlow = BakeOuterGlow()); } }
        internal static Sprite Corner { get { return cachedCorner != null ? cachedCorner : (cachedCorner = BakeOrnament("BossRushCollectibleCorner", 48, 48, CornerSdf, CornerGold, CornerHighlight)); } }
        internal static Sprite Crest { get { return cachedCrest != null ? cachedCrest : (cachedCrest = BakeOrnament("BossRushCollectibleCrest", 40, 24, CrestSdf, CrestGold, CrestHighlight)); } }
        internal static Sprite Spark { get { return cachedSpark != null ? cachedSpark : (cachedSpark = BakeSpark()); } }
        internal static Sprite Sheen { get { return cachedSheen != null ? cachedSheen : (cachedSheen = BakeSheen()); } }
        internal static Sprite Dot { get { return cachedDot != null ? cachedDot : (cachedDot = BakeDot()); } }

        internal static void ResetStaticCaches()
        {
            for (int i = 0; i < ownedObjects.Count; i++)
            {
                try { if (ownedObjects[i] != null) UnityEngine.Object.Destroy(ownedObjects[i]); }
                catch (Exception) { /* 销毁失败不得拖崩宿主清理链 */ }
            }
            ownedObjects.Clear();
            cachedFrame = cachedInnerGlow = cachedOuterGlow = cachedCorner = cachedCrest = cachedSpark = cachedSheen = cachedDot = null;
        }

        // ---------------- 烘焙 ----------------

        private static Sprite BakeFrame()
        {
            const int size = 128;
            float half = size / (2f * HiRes);
            return Bake("BossRushCollectibleFrame", size, size, BasePixelsPerUnit * HiRes, new Vector4(32f, 32f, 32f, 32f), delegate(float x, float y)
            {
                float depth = -RoundedRectSdf(x / HiRes - half, y / HiRes - half, half, half, CornerRadius);
                return EvaluateStops(depth);
            });
        }

        private static Sprite BakeInnerGlow()
        {
            const int size = 64;
            float half = size * 0.5f;
            return Bake("BossRushCollectibleInnerGlow", size, size, BasePixelsPerUnit, new Vector4(28f, 28f, 28f, 28f), delegate(float x, float y)
            {
                float depth = -RoundedRectSdf(x - half, y - half, half, half, CornerRadius);
                float alpha;
                if (depth < 0f) alpha = 0f;
                else if (depth < 1.5f) alpha = depth / 1.5f;
                else alpha = Mathf.Pow(1f - Mathf.Clamp01((depth - 1.5f) / 15f), 2.6f);
                return new Color(1f, 1f, 1f, alpha);
            });
        }

        private static Sprite BakeOuterGlow()
        {
            const int size = 64;
            float half = size * 0.5f;
            float cellHalf = half - OuterGlowExtent;
            return Bake("BossRushCollectibleOuterGlow", size, size, BasePixelsPerUnit, new Vector4(28f, 28f, 28f, 28f), delegate(float x, float y)
            {
                float outside = RoundedRectSdf(x - half, y - half, cellHalf, cellHalf, CornerRadius);
                float alpha = outside >= 0f
                    ? Mathf.Pow(1f - Mathf.Clamp01(outside / 15f), 2.4f)
                    : Mathf.Pow(Mathf.Clamp01(1f + outside / 4f), 2f);
                return new Color(1f, 1f, 1f, alpha);
            });
        }

        private static Sprite BakeSpark()
        {
            const int size = 48;
            float half = size / (2f * HiRes);
            return Bake("BossRushCollectibleSpark", size, size, BasePixelsPerUnit * HiRes, Vector4.zero, delegate(float x, float y)
            {
                float dx = x / HiRes - half, dy = y / HiRes - half;
                float d1 = (dx + dy) * 0.70710678f, d2 = (dx - dy) * 0.70710678f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Max(Mathf.Max(Ray(dx, dy, 12f, 1.2f), Ray(dy, dx, 12f, 1.2f)),
                    Mathf.Max(0.55f * Mathf.Max(Ray(d1, d2, 6f, 0.8f), Ray(d2, d1, 6f, 0.8f)), Mathf.Exp(-(r / 2f) * (r / 2f))));
                return new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            });
        }

        /// <summary>扫光带：横向高斯剖面，纵向拉长后斜着扫过格子。</summary>
        private static Sprite BakeSheen()
        {
            const int width = 64, height = 4;
            return Bake("BossRushCollectibleSheen", width, height, BasePixelsPerUnit, Vector4.zero, delegate(float x, float y)
            {
                float u = x / width * 2f - 1f;
                return new Color(1f, 1f, 1f, Mathf.Exp(-(u / 0.42f) * (u / 0.42f)) * (1f - u * u));
            });
        }

        private static Sprite BakeDot()
        {
            const int size = 32;
            float half = size * 0.5f;
            return Bake("BossRushCollectibleDot", size, size, BasePixelsPerUnit, Vector4.zero, delegate(float x, float y)
            {
                float dx = x - half, dy = y - half;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / half);
                return new Color(1f, 1f, 1f, t * t * (3f - 2f * t));
            });
        }

        /// <summary>带暗铜描边的金色饰件：sdf 与配色函数的坐标都是自左上角起、向右向下的 UI 单位。</summary>
        private static Sprite BakeOrnament(string name, int width, int height, Func<float, float, float> sdf,
            Func<float, float, Color> gold, Func<float, float, float> highlight)
        {
            return Bake(name, width, height, BasePixelsPerUnit * HiRes, Vector4.zero, delegate(float x, float y)
            {
                float ux = x / HiRes, uy = (height - y) / HiRes;
                float d = sdf(ux, uy);
                float shapeAlpha = Mathf.Clamp01(0.5f - d * HiRes);
                float outlineAlpha = Mathf.Clamp01(0.5f - (d - 0.8f) * HiRes) * 0.92f;
                float alpha = shapeAlpha + outlineAlpha * (1f - shapeAlpha);
                if (alpha <= 0.0001f) return new Color(0f, 0f, 0f, 0f);
                Color body = Color.Lerp(gold(ux, uy), Highlight, highlight(ux, uy));
                float outlineWeight = outlineAlpha * (1f - shapeAlpha);
                return new Color((body.r * shapeAlpha + Shade.r * outlineWeight) / alpha,
                    (body.g * shapeAlpha + Shade.g * outlineWeight) / alpha,
                    (body.b * shapeAlpha + Shade.b * outlineWeight) / alpha, alpha);
            });
        }

        private static Sprite Bake(string name, int width, int height, float pixelsPerUnit, Vector4 border, Func<float, float, Color> shade)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.name = name;
                texture.hideFlags = HideFlags.DontSave;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                Color[] pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++) pixels[y * width + x] = shade(x + 0.5f, y + 0.5f);
                }
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                // 先建 Sprite 再登记：Sprite.Create 抛异常时贴图不进缓存，由 catch 销毁。
                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0,
                    SpriteMeshType.FullRect, border);
                sprite.name = name;
                sprite.hideFlags = HideFlags.DontSave;
                ownedObjects.Add(texture);
                ownedObjects.Add(sprite);
                return sprite;
            }
            catch (Exception e)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 边框贴图烘焙失败 " + name + ": " + e.Message);
                return null;
            }
        }

        // ---------------- 形状 ----------------

        private static Color EvaluateStops(float depth)
        {
            if (depth <= FrameStopDepth[0] || depth >= FrameStopDepth[FrameStopDepth.Length - 1]) return new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < FrameStopDepth.Length - 1; i++)
            {
                if (depth < FrameStopDepth[i + 1])
                {
                    float t = (depth - FrameStopDepth[i]) / (FrameStopDepth[i + 1] - FrameStopDepth[i]);
                    return Color.Lerp(FrameStopColor[i], FrameStopColor[i + 1], t);
                }
            }
            return new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>圆角矩形的有符号距离：外为正、内为负（中心在原点，hx/hy 为半宽半高）。</summary>
        private static float RoundedRectSdf(float px, float py, float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(px) - (hx - radius), qy = Mathf.Abs(py) - (hy - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static float Segment(float ux, float uy, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = ux - ax, wy = uy - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
            float dx = wx - t * vx, dy = wy - t * vy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float Ray(float along, float across, float length, float width)
        {
            float falloff = Mathf.Clamp01(1f - Mathf.Abs(along) / length);
            float w = width * Mathf.Pow(falloff, 1.5f) + 0.001f;
            return Mathf.Clamp01(1f - Mathf.Abs(across) / w) * falloff;
        }

        /// <summary>左上角的角花：沿上边、左边各伸一支渐细的金枝，拐角一颗菱形，内侧一道细折线和一颗小圆珠。</summary>
        private static float CornerSdf(float ux, float uy)
        {
            float top = CornerArm(ux, uy), left = CornerArm(uy, ux);
            float diamond = (Mathf.Abs(ux - 3.8f) + Mathf.Abs(uy - 3.8f) - 4.6f) * 0.70710678f;
            float lineA = Segment(ux, uy, 8.5f, 6.2f, 14.5f, 6.2f) - 0.45f;
            float lineB = Segment(ux, uy, 6.2f, 8.5f, 6.2f, 14.5f) - 0.45f;
            float bead = Mathf.Sqrt((ux - 9.8f) * (ux - 9.8f) + (uy - 9.8f) * (uy - 9.8f)) - 1.1f;
            return Mathf.Min(Mathf.Min(Mathf.Min(top, left), Mathf.Min(diamond, bead)), Mathf.Min(lineA, lineB));
        }

        private static float CornerArm(float along, float across)
        {
            float thickness = Mathf.Lerp(1.5f, 0.35f, Mathf.Clamp01((along - 4f) / 16f));
            return Mathf.Max(Mathf.Abs(across - 1.7f) - thickness, along - 21f);
        }

        private static Color CornerGold(float ux, float uy)
        {
            return Color.Lerp(GoldBright, GoldDeep, Mathf.Clamp01((ux + uy) / 28f));
        }

        private static float CornerHighlight(float ux, float uy)
        {
            return Mathf.Clamp01(1f - (Mathf.Abs(ux - 3.8f) + Mathf.Abs(uy - 3.8f)) / 2.2f);
        }

        /// <summary>顶边徽记（8 档）：竖长菱形宝石，两侧各一支渐细的短翼。</summary>
        private static float CrestSdf(float ux, float uy)
        {
            float dx = ux - 10f, dy = uy - 6f;
            float diamond = (Mathf.Abs(dx) + Mathf.Abs(dy) / 1.25f - 4.6f) / 1.6f;
            float thickness = Mathf.Lerp(0.9f, 0.2f, Mathf.Clamp01((Mathf.Abs(dx) - 5f) / 5f));
            float wing = Mathf.Max(Mathf.Abs(dy) - thickness, Mathf.Max(5f - Mathf.Abs(dx), Mathf.Abs(dx) - 10f));
            return Mathf.Min(diamond, wing);
        }

        private static Color CrestGold(float ux, float uy)
        {
            return Color.Lerp(GoldBright, GoldDeep, Mathf.Clamp01(uy / 12f));
        }

        private static float CrestHighlight(float ux, float uy)
        {
            return Mathf.Clamp01(1f - (Mathf.Abs(ux - 10f) + Mathf.Abs(uy - 6f) * 0.8f) / 2.4f);
        }
    }
}

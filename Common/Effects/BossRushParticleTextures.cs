// ============================================================================
// BossRushParticleTextures.cs - 全 Mod 共享的程序化粒子形状贴图（星芒 / 烟缕 / 条带 / 电弧……）
// ============================================================================
// 来历（2026-09-23 特效审美审查 VA-34）：
//   这批形状原本只在 PetNest/PetNestAuraTextures.cs 里，是遗种巢私有的；套装、新武器、随机事件、
//   征程终章想要「星芒闪光」「烟缕」「条带拖尾」只能各自手搓，或者拿一张圆形渐变硬凑——
//   那正是「一团雾」「剪纸边」的来源。画师原样挪到这里，遗种巢改为转调本类（形状与像素逐字不变）。
//
// 口径：
//   - 画师是纯函数：输入种类与边长，输出 Color32[]（RGB 恒白，只有 alpha 描形状）。
//     颜色一律交给粒子的 startColor / 线的 colorGradient，所以一张贴图可以给任意颜色用；
//   - 像素按 Texture2D.SetPixels32 的口径排列（行优先、左下原点），采样点取像素中心；
//   - 所有形状在贴图边缘 alpha 归零，Clamp 采样不会出现硬边；
//   - <see cref="Get"/> 懒加载、带 mip（屏上只有几到几十个像素，没有 mip 细线会闪），全 Mod 共享一份；
//     遗种巢仍按自己的口径逐只建贴图并随崽销毁（它的资源所有权由 PetNestAuraEffectGuard 钉着），
//     只复用这里的画师。
//
// 生命周期：静态缓存经 <see cref="ResetStaticCaches"/> 在 Mod 卸载路径销毁（HideAndDontSave，切场景不回收）。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>共享形状种类。数值与 <c>PetNestAuraTexture</c> 一一对应（遗种巢按数值转调），Count 必须在最后。</summary>
    internal enum BossRushParticleShape
    {
        /// <summary>亮核 + 柔光晕的圆点（火星、光点、余烬）。</summary>
        GlowDot = 0,
        /// <summary>四主芒 + 四副芒的星光（闪光、星点）。</summary>
        Star = 1,
        /// <summary>带高光的空心气泡。</summary>
        Bubble = 2,
        /// <summary>六瓣雪花。</summary>
        Snowflake = 3,
        /// <summary>带叶脉的叶片。</summary>
        Leaf = 4,
        /// <summary>带噪声边缘的烟缕（烟、雾、尘、暗影）。</summary>
        Wisp = 5,
        /// <summary>折线电弧。</summary>
        Bolt = 6,
        /// <summary>细长菱形碎片（冰屑、镜屑、碎片）。</summary>
        Shard = 7,
        /// <summary>干净细环（涟漪、冲击环）。</summary>
        Ring = 8,
        /// <summary>双细环 + 刻度 + 菱形符文（128 px）。</summary>
        RuneOuter = 9,
        /// <summary>细环 + 八颗珠点。</summary>
        RuneInner = 10,
        /// <summary>条带：沿 U 恒亮、沿 V 柔边（拖尾 / 线按 Stretch 贴，头尾渐隐交给颜色梯度）。</summary>
        TrailStrip = 11,
        Count = 12,
    }

    /// <summary>程序化粒子形状画师 + 全 Mod 共享的懒加载贴图缓存。</summary>
    internal static class BossRushParticleTextures
    {
        private static readonly Texture2D[] cachedTextures = new Texture2D[(int)BossRushParticleShape.Count];

        /// <summary>各种类的边长。外圈符文环细节多，用 128；条带只有一个方向有变化，32 足够；其余 64。</summary>
        internal static int SizeOf(BossRushParticleShape kind)
        {
            if (kind == BossRushParticleShape.RuneOuter) return 128;
            if (kind == BossRushParticleShape.TrailStrip) return 32;
            return 64;
        }

        /// <summary>
        /// 取一张共享贴图（懒加载、带 mip、白色，形状只在 alpha 里）。
        /// 返回的贴图是共享的，**不要**改它，也不要销毁它。
        /// </summary>
        internal static Texture2D Get(BossRushParticleShape kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= cachedTextures.Length) return null;
            Texture2D cached = cachedTextures[index];
            if (cached != null) return cached;

            int size = SizeOf(kind);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = "BossRushFx_" + kind;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = kind == BossRushParticleShape.RuneOuter || kind == BossRushParticleShape.RuneInner
                ? FilterMode.Trilinear
                : FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixels32(Paint(kind, size));
            texture.Apply(true, true);
            cachedTextures[index] = texture;
            return texture;
        }

        /// <summary>销毁共享贴图（Mod 卸载路径）。</summary>
        internal static void ResetStaticCaches()
        {
            for (int i = 0; i < cachedTextures.Length; i++)
            {
                if (cachedTextures[i] != null)
                {
                    Object.Destroy(cachedTextures[i]);
                }
                cachedTextures[i] = null;
            }
        }

        /// <summary>画一张贴图的像素。size 必须为正；未知种类返回全透明。</summary>
        internal static Color32[] Paint(BossRushParticleShape kind, int size)
        {
            if (size <= 0) size = 1;
            Color32[] pixels = new Color32[size * size];
            float inv = 2f / size;
            for (int py = 0; py < size; py++)
            {
                float y = (py + 0.5f) * inv - 1f;
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) * inv - 1f;
                    float a = Mathf.Clamp01(Sample(kind, x, y));
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            }
            return pixels;
        }

        /// <summary>单点 alpha。x、y 是以贴图中心为原点、边缘为 ±1 的坐标。</summary>
        internal static float Sample(BossRushParticleShape kind, float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            switch (kind)
            {
                case BossRushParticleShape.GlowDot: return GlowDot(r);
                case BossRushParticleShape.Star: return Star(x, y, r);
                case BossRushParticleShape.Bubble: return Bubble(x, y, r);
                case BossRushParticleShape.Snowflake: return Snowflake(x, y, r);
                case BossRushParticleShape.Leaf: return Leaf(x, y);
                case BossRushParticleShape.Wisp: return Wisp(x, y, r);
                case BossRushParticleShape.Bolt: return Bolt(x, y);
                case BossRushParticleShape.Shard: return Shard(x, y);
                case BossRushParticleShape.Ring: return Ring(r);
                case BossRushParticleShape.RuneOuter: return RuneOuter(x, y, r);
                case BossRushParticleShape.RuneInner: return RuneInner(x, y, r);
                case BossRushParticleShape.TrailStrip: return TrailStrip(y);
                default: return 0f;
            }
        }

        #region 形状

        private static float GlowDot(float r)
        {
            if (r >= 1f) return 0f;
            // 亮核很小、光晕很淡：屏上几个像素的火星要「亮一点」，而不是「大一团」
            float core = Mathf.Exp(-Sq(r / 0.2f));
            float halo = 1f - r;
            halo = halo * halo * halo;
            return core + 0.55f * halo;
        }

        private static float Star(float x, float y, float r)
        {
            float ax = Mathf.Abs(x);
            float ay = Mathf.Abs(y);
            // 主芒：沿两轴的细线，越往外越细越淡
            float beamH = Mathf.Exp(-ay / 0.028f) * Sq(Mathf.Max(0f, 1f - ax));
            float beamV = Mathf.Exp(-ax / 0.028f) * Sq(Mathf.Max(0f, 1f - ay));
            // 副芒：45° 方向、更短更细
            float u = Mathf.Abs((x + y) * 0.70710678f);
            float v = Mathf.Abs((x - y) * 0.70710678f);
            float diagA = Mathf.Exp(-v / 0.02f) * Sq(Mathf.Max(0f, 1f - u * 1.7f));
            float diagB = Mathf.Exp(-u / 0.02f) * Sq(Mathf.Max(0f, 1f - v * 1.7f));
            float core = Mathf.Exp(-Sq(r / 0.12f));
            float halo = r < 1f ? 0.22f * Cube(1f - r) : 0f;
            return beamH + beamV + 0.55f * (diagA + diagB) + core + halo;
        }

        private static float Bubble(float x, float y, float r)
        {
            if (r >= 1f) return 0f;
            float ring = Mathf.Exp(-Sq((r - 0.8f) / 0.07f));
            float fill = r < 0.8f ? 0.08f + 0.1f * (r / 0.8f) : 0f;
            // 左上一点高光，气泡才像气泡而不是圆圈
            float hx = x + 0.3f;
            float hy = y - 0.34f;
            float highlight = 0.95f * Mathf.Exp(-Sq(Mathf.Sqrt(hx * hx + hy * hy) / 0.13f));
            float edge = Mathf.Clamp01((1f - r) / 0.08f);
            return (ring + fill + highlight) * edge;
        }

        private static float Snowflake(float x, float y, float r)
        {
            if (r >= 1f) return 0f;
            // 六重对称：把点折进「一条臂的上半边」再只算这一条臂，
            // 每像素 3 段线距离，而不是 6 臂 × 5 段 = 30 段
            float angle = Mathf.Atan2(y, x) - Mathf.PI * 0.5f;
            const float sector = Mathf.PI / 3f;
            angle -= sector * Mathf.Round(angle / sector);
            angle = Mathf.Abs(angle);
            float px = r * Mathf.Cos(angle);
            float py = r * Mathf.Sin(angle);

            const float branchCos = 0.5736f; // cos 55°
            const float branchSin = 0.8192f; // sin 55°
            float d = SegmentDistance(px, py, 0f, 0f, 0.88f, 0f);
            d = Mathf.Min(d, SegmentDistance(px, py, 0.42f, 0f, 0.42f + 0.26f * branchCos, 0.26f * branchSin));
            d = Mathf.Min(d, SegmentDistance(px, py, 0.64f, 0f, 0.64f + 0.16f * branchCos, 0.16f * branchSin));
            // 臂宽 0.07、核心 0.18（VA-13）：崽身上的雪花屏上只有 12–15 px，
            // 0.045 的臂宽缩到 mip 3 以下不到 1 px，六条臂糊成一个光点
            float arms = Mathf.Exp(-Sq(d / 0.07f));
            float core = 0.9f * Mathf.Exp(-Sq(r / 0.18f));
            return arms + core;
        }

        private static float Leaf(float x, float y)
        {
            // 叶轴斜 40°，叶尖朝右上
            const float c = 0.7660f; // cos 40°
            const float s = 0.6428f; // sin 40°
            float u = x * c + y * s;
            float v = -x * s + y * c;
            const float halfLength = 0.84f;
            if (u < -halfLength)
            {
                // 叶柄
                if (u > -0.97f && Mathf.Abs(v) < 0.035f) return 0.75f;
                return 0f;
            }
            if (u > halfLength) return 0f;
            float t = u / halfLength;
            float halfWidth = 0.36f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) * (1f - 0.2f * t);
            float av = Mathf.Abs(v);
            float body = Mathf.Clamp01((halfWidth - av) / 0.05f);
            if (body <= 0f) return 0f;
            float vein = av < 0.022f ? 0.55f : 1f;
            float shade = 0.72f + 0.28f * (1f - av / Mathf.Max(halfWidth, 0.001f));
            return body * vein * shade;
        }

        private static float Wisp(float x, float y, float r)
        {
            if (r >= 1f) return 0f;
            float n = 0.6f * ValueNoise(x * 2.3f + 5.1f, y * 2.3f + 1.7f)
                + 0.4f * ValueNoise(x * 4.9f + 2.3f, y * 4.9f + 8.9f);
            float body = Mathf.Pow(1f - r, 1.4f);
            return body * (0.4f + 0.8f * n);
        }

        private static float Bolt(float x, float y)
        {
            // 自下而上的折线 + 一条短分叉
            float d = SegmentDistance(x, y, -0.04f, -0.92f, 0.2f, -0.46f);
            d = Mathf.Min(d, SegmentDistance(x, y, 0.2f, -0.46f, -0.14f, -0.1f));
            d = Mathf.Min(d, SegmentDistance(x, y, -0.14f, -0.1f, 0.16f, 0.22f));
            d = Mathf.Min(d, SegmentDistance(x, y, 0.16f, 0.22f, -0.08f, 0.55f));
            d = Mathf.Min(d, SegmentDistance(x, y, -0.08f, 0.55f, 0.05f, 0.92f));
            d = Mathf.Min(d, SegmentDistance(x, y, -0.14f, -0.1f, -0.5f, 0.12f));
            float core = Mathf.Exp(-Sq(d / 0.035f));
            float glow = 0.35f * Mathf.Exp(-Sq(d / 0.13f));
            float edge = Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y))) / 0.06f);
            return (core + glow) * edge;
        }

        private static float Shard(float x, float y)
        {
            float q = Mathf.Abs(x) / 0.3f + Mathf.Abs(y) / 0.86f;
            if (q >= 1.15f) return 0f;
            if (q >= 1f) return 0.3f * Mathf.Exp(-Sq((q - 1f) / 0.06f));
            float body = Mathf.Clamp01((1f - q) / 0.08f);
            float spine = 0.45f * Mathf.Exp(-Sq(x / 0.05f));
            return body * (0.6f + spine);
        }

        private static float Ring(float r)
        {
            if (r >= 1f) return 0f;
            float ring = Mathf.Exp(-Sq((r - 0.84f) / 0.05f));
            return ring * Mathf.Clamp01((1f - r) / 0.06f);
        }

        private static float RuneOuter(float x, float y, float r)
        {
            if (r >= 1f) return 0f;
            float a = Mathf.Exp(-Sq((r - 0.93f) / 0.017f));      // 外细环
            a += 0.85f * Mathf.Exp(-Sq((r - 0.79f) / 0.012f));   // 内细环
            if (r > 0.8f && r < 0.92f) a += 0.08f;               // 两环之间的淡底

            // 刻度每 15° 一格，每第 3 格换成菱形符文
            const float step = Mathf.PI / 12f;
            float angle = Mathf.Atan2(y, x);
            float k = Mathf.Round(angle / step);
            float arc = (angle - k * step) * r;
            int index = ((int)k % 3 + 3) % 3;
            if (index == 0)
            {
                // 菱形符文要在屏上约 48 px 的环里还认得出，所以比刻度粗得多
                float dq = Mathf.Abs(arc) / 0.065f + Mathf.Abs(r - 0.855f) / 0.066f;
                a += Mathf.Clamp01((1f - dq) / 0.15f);
            }
            else if (r > 0.83f && r < 0.885f)
            {
                a += 0.9f * Mathf.Exp(-Sq(arc / 0.014f));
            }
            return a * Mathf.Clamp01((1f - r) / 0.03f);
        }

        private static float RuneInner(float x, float y, float r)
        {
            if (r >= 1f) return 0f;
            float a = 0.9f * Mathf.Exp(-Sq((r - 0.84f) / 0.035f));
            // 八颗珠点压在环上，转起来才看得出在转
            const float step = Mathf.PI / 4f;
            float angle = Mathf.Atan2(y, x);
            float k = Mathf.Round(angle / step);
            float bx = 0.84f * Mathf.Cos(k * step);
            float by = 0.84f * Mathf.Sin(k * step);
            float dx = x - bx;
            float dy = y - by;
            a += Mathf.Exp(-Sq(Mathf.Sqrt(dx * dx + dy * dy) / 0.07f));
            return a * Mathf.Clamp01((1f - r) / 0.05f);
        }

        private static float TrailStrip(float y)
        {
            float ay = Mathf.Abs(y);
            return Mathf.Exp(-Sq(ay / 0.42f)) * Mathf.Clamp01((1f - ay) / 0.12f);
        }

        #endregion

        #region 数学

        private static float Sq(float v) { return v * v; }

        private static float Cube(float v) { return v * v * v; }

        /// <summary>点到线段的距离。</summary>
        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float lengthSq = abx * abx + aby * aby;
            float t = lengthSq > 1e-8f ? ((px - ax) * abx + (py - ay) * aby) / lengthSq : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            float dx = px - (ax + abx * t);
            float dy = py - (ay + aby * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>确定性的平滑值噪声（0..1），只用于烟缕边缘，不追求质量。</summary>
        private static float ValueNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy);
            float b = Hash(ix + 1, iy);
            float c = Hash(ix, iy + 1);
            float d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        #endregion
    }
}

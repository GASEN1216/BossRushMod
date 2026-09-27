// ============================================================================
// BossRushParticleTextures.cs - 全 Mod 共享的程序化粒子形状贴图（星芒 / 烟缕 / 条带 / 电弧……）
// ============================================================================
// 来历（2026-09-23 特效审美审查 VA-34）：
//   这批形状原本只在 PetNest/PetNestAuraTextures.cs 里，是遗种巢私有的；套装、新武器、随机事件、
//   征程终章想要「星芒闪光」「烟缕」「条带拖尾」只能各自手搓，或者拿一张圆形渐变硬凑——
//   那正是「一团雾」「剪纸边」的来源。画师原样挪到这里，遗种巢改为转调本类（形状与像素逐字不变）。
//
// 口径：
//   - 画师是纯函数：输入种类与边长，输出 Color32[]（alpha 描形状；RGB 是明暗，除叶片外恒白）。
//     颜色一律交给粒子的 startColor / 线的 colorGradient，所以一张贴图可以给任意颜色用；
//     叶片的 RGB 带一层灰度明暗（主脉、侧脉、受光 / 背光两半），乘上顶点色后才不是一块平涂色片
//     （2026-09-25 owner「绿色的塑料叶子」）。明暗在整张图上连续定义，形状外的像素也不是白色，
//     生成 mip 时叶缘不会混进一圈白边；
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
        /// <summary>薄壁气泡：中心近乎透明、一圈粗细不匀的细亮边、左上窗形高光 + 小亮点、右下一点反光。</summary>
        Bubble = 2,
        /// <summary>六瓣雪花。</summary>
        Snowflake = 3,
        /// <summary>竖直的叶片（叶尖朝上）：叶柄、微弯主脉、斜向侧脉、半透明叶缘，RGB 带明暗。</summary>
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
        /// <summary>珍珠晶片：竖直柔边透镜形，细亮轮廓 + 中间一道珠光带 + 上部高光 + 四周淡柔光。</summary>
        Pearl = 12,
        /// <summary>水面涟漪：主细环 + 一圈更淡的回波，亮度沿圆周不均匀。</summary>
        Ripple = 13,
        Count = 14,
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
                    // 明暗恒为 1 的形状写出 255，与只描 alpha 时逐字节相同
                    byte l = (byte)(Mathf.Clamp01(SampleLuma(kind, x, y)) * 255f + 0.5f);
                    pixels[py * size + px] = new Color32(l, l, l, (byte)(a * 255f + 0.5f));
                }
            }
            return pixels;
        }

        /// <summary>单点明暗（写进 RGB 的灰度，sRGB）。只有叶片不是 1。</summary>
        internal static float SampleLuma(BossRushParticleShape kind, float x, float y)
        {
            if (kind == BossRushParticleShape.Leaf) return LeafLuma(x, y);
            return 1f;
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
                case BossRushParticleShape.Pearl: return Pearl(x, y, r);
                case BossRushParticleShape.Ripple: return Ripple(x, y, r);
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
            // 2026-09-25 重画（owner：「蓝色的泡泡太塑料了」）：旧图是一圈 0.07 宽的均匀粗环 + 0.08–0.18 的内填充，
            // 缩到十来个像素就是一枚实心感的圆圈。现在按薄膜画：
            const float wall = 0.82f;
            // 膜：中心几乎透明，只在贴近边缘处按 d^6 变厚（菲涅耳）
            float d = r / wall;
            float film = d < 1f ? 0.03f + 0.3f * Mathf.Pow(d, 6f) : 0f;
            // 边：细亮线，沿圆周左上、右下亮，两侧暗——一圈粗细亮度处处相同的环正是塑料感的来源
            float angle = Mathf.Atan2(y, x);
            float rim = Mathf.Exp(-Sq((r - wall) / 0.045f))
                * (0.45f + 0.55f * (0.5f + 0.5f * Mathf.Cos(2f * (angle - 2.3561945f))));
            // 左上一条顺着圆周的窗形高光 + 旁边一粒小亮点（光源方向恒定，粒子不自转）
            float hx = x + 0.33f;
            float hy = y - 0.36f;
            float along = (hx + hy) * 0.70710678f;
            float across = (hx - hy) * 0.70710678f;
            float window = Mathf.Exp(-(Sq(along / 0.15f) + Sq(across / 0.055f)));
            float dx = x + 0.02f;
            float dy = y - 0.56f;
            float dot = 0.75f * Mathf.Exp(-(dx * dx + dy * dy) / Sq(0.05f));
            // 右下内侧一小段反光弧
            float bounce = 0.4f * Mathf.Exp(-Sq((r - 0.7f) / 0.05f))
                * Mathf.Exp(-Sq(WrapAngle(angle + 0.7853982f) / 0.6f));
            float edge = Mathf.Clamp01((1f - r) / 0.06f);
            return Mathf.Clamp01(film + 0.7f * rim + window + dot + bounce) * edge;
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

        // 叶片（2026-09-25 重画，owner：「绿色的塑料叶子」）：旧图叶轴斜 40°、一块等 alpha 的实心叶形，
        // 只有主脉处 alpha 打折——暗地面上主脉是黑线、亮地面上是白线，颜色全靠一个顶点色，就是一片贴纸。
        // 现在叶轴竖直（叶尖朝上，粒子按 X 轴压扁就是在绕主脉翻面）、叶形略不对称（主脉微弯）、
        // 叶缘半透明；明暗写进 RGB（见 LeafLuma）。
        private const float LeafBase = -0.70f;
        private const float LeafTip = 0.92f;
        private const float LeafHalfWidth = 0.40f;

        /// <summary>叶片坐标：t 从叶基 0 到叶尖 1；dv 是到（微弯）主脉的横向距离；w 是该处半宽。</summary>
        private static void LeafFrame(float x, float y, out float t, out float dv, out float w)
        {
            t = (y - LeafBase) / (LeafTip - LeafBase);
            float tc = Mathf.Clamp01(t);
            float midrib = 0.07f * Mathf.Sin(Mathf.PI * tc);
            dv = x - midrib;
            // 最宽处在离叶基约 40% 的地方；0.391 是 t^0.55 (1-t)^0.85 的峰值，归一化后峰值半宽 = LeafHalfWidth
            w = LeafHalfWidth * Mathf.Pow(tc, 0.55f) * Mathf.Pow(1f - tc, 0.85f) / 0.391f;
        }

        private static float Leaf(float x, float y)
        {
            float t, dv, w;
            LeafFrame(x, y, out t, out dv, out w);
            float blade = 0f;
            if (t > 0f && t < 1f)
            {
                float av = Mathf.Abs(dv);
                float body = Mathf.Clamp01((w - av) / 0.045f);
                float q = Mathf.Clamp01(av / Mathf.Max(w, 0.02f));
                // 叶缘比叶心薄：0.95 → 0.70
                blade = body * (0.7f + 0.25f * (1f - q * q));
            }
            float stem = 0f;
            if (y > -0.95f && y < LeafBase + 0.06f)
            {
                stem = 0.85f * Mathf.Clamp01((0.03f - Mathf.Abs(x)) / 0.015f) * Mathf.Clamp01((y + 0.95f) / 0.05f);
            }
            return Mathf.Max(blade, stem);
        }

        /// <summary>
        /// 叶片明暗：左半受光 0.88、右半背光 0.72（叶片沿主脉微折）；主脉更亮、到叶尖变细变淡；
        /// 侧脉斜向叶尖、往叶缘淡掉；叶尖略亮、叶缘略暗。整张图连续定义（形状外取叶缘的值），mip 不混白边。
        /// </summary>
        private static float LeafLuma(float x, float y)
        {
            float t, dv, w;
            LeafFrame(x, y, out t, out dv, out w);
            float tc = Mathf.Clamp01(t);
            float av = Mathf.Abs(dv);
            float q = Mathf.Clamp01(av / Mathf.Max(w, 0.02f));
            float side = 0.72f + 0.16f * SmoothStep(-0.02f, 0.02f, -dv);
            float shade = side * (0.9f + 0.1f * tc) * (1f - 0.14f * q * q * q);
            float midrib = Mathf.Exp(-Sq(dv / 0.016f)) * (1f - 0.45f * tc);
            float phase = (y - 1.1f * av) * 3.8f;
            float veins = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * phase), 10f) * (1f - q)
                * SmoothStep(0.06f, 0.2f, tc) * (1f - SmoothStep(0.82f, 1f, tc));
            return Mathf.Clamp01(shade + 0.3f * midrib + 0.14f * veins);
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

        private static float Pearl(float x, float y, float r)
        {
            // 竖直的柔边透镜：两端收尖；中间一道珠光带、上部一粒高光、轮廓一道细亮边；四周一层很淡的柔光。
            // 给加色用：本体只有 0.14 的底，亮的只是那道珠光带与轮廓——旧的「珍珠晶片」借的是镜屑那张
            // 满实心的硬边菱形，半透明叠上去是一块块发灰的白片（2026-09-25）。
            float t = y / 0.86f;
            float tt = Mathf.Clamp01(1f - t * t);
            float hw = 0.4f * Mathf.Pow(tt, 0.8f);
            float ax = Mathf.Abs(x);
            float body = Mathf.Clamp01((hw - ax) / 0.07f);
            float across = ax / Mathf.Max(hw, 0.001f);
            float sheen = Mathf.Exp(-Sq(across / 0.45f)) * tt;
            float contour = Mathf.Abs(t) < 1f ? Mathf.Exp(-Sq((hw - ax) / 0.05f)) * tt : 0f;
            float spec = 0.55f * Mathf.Exp(-(Sq(x + 0.05f) + Sq(y - 0.3f)) / Sq(0.09f));
            float glow = r < 1f ? 0.2f * Sq(1f - r) : 0f;
            return Mathf.Clamp01(body * (0.14f + 0.36f * sheen + spec) + 0.3f * contour + glow);
        }

        private static float Ripple(float x, float y, float r)
        {
            if (r >= 1f) return 0f;
            // 主细环 + 内侧一圈更淡的回波，亮度沿圆周起伏（水面反光不会处处一样亮）；环内一层极淡的水光
            float angle = Mathf.Atan2(y, x);
            float wobble = 0.8f + 0.2f * Mathf.Sin(3f * angle + 1.3f) * Mathf.Sin(5f * angle + 0.4f);
            float main = Mathf.Exp(-Sq((r - 0.84f) / 0.03f));
            float echo = 0.36f * Mathf.Exp(-Sq((r - 0.68f) / 0.024f));
            float sheen = r < 0.84f ? 0.05f * SmoothStep(0.4f, 0.84f, r) : 0f;
            float edge = Mathf.Clamp01((1f - r) / 0.06f);
            return Mathf.Clamp01(main * wobble + echo * (1.6f - wobble) + sheen) * edge;
        }

        #endregion

        #region 数学

        private static float Sq(float v) { return v * v; }

        private static float Cube(float v) { return v * v * v; }

        private static float SmoothStep(float edge0, float edge1, float v)
        {
            float t = Mathf.Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>角度折回 (-π, π]。</summary>
        private static float WrapAngle(float angle)
        {
            const float twoPi = Mathf.PI * 2f;
            angle -= twoPi * Mathf.Floor((angle + Mathf.PI) / twoPi);
            return angle;
        }

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

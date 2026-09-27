// ============================================================================
// PetNestAuraRecipes.cs - 崽特效配方：炫彩十色各一种元素，异色一整套金色仪式感
// ============================================================================
// 尺寸口径：配方里的米数按「0.45 m 高的崽」调，统一乘 _unit（= 实测身高 / 0.45）；
// 位置以脚底为 0（PetNestAuraEffect.Build 已把特效根对齐到模型包围盒底边）。
// 屏幕换算（默认相机 FOV 20°、臂长 45 m，1080p 约 68 px/m）：
//   崽本体约 30 px 高；火星 / 光点 2–4 cm ≈ 2–3 px 的亮核（靠 HDR + Bloom 出光），
//   星芒 5–9 cm、闪光 15–22 cm、雪花 16–22 cm、异色符文环直径约 0.7 m ≈ 48 px。
//   2026-09-25 起崽 Lv1 就有 0.62 倍官方体型（_unit 约 1.5–2.2）：蓝白绿三色按「相对崽身」重新定尺寸——
//   气泡 4–8 cm、叶片 10–14 cm、珍珠晶片 5–7.5 cm（都还要再乘 _unit），不再是半个崽那么大的贴纸。
//
// 炫彩（两色 A、B）：
//   A 决定主元素（下表），B 是一圈反向环绕崽身的点缀，用 B 自己的元素贴图与颜色——
//   「黑白」= 升腾的暗影烟缕 + 一圈珍珠晶片；「白黑」= 珠光光尘与晶片 + 一圈暗色烟缕。
//     赤 red    → 龙息火焰：克隆官方火 AK-47 的 Smoke（火舌）与 Spark（火星），与龙息武器同源
//     橙 orange → 锻火飞溅：一阵阵向上喷、再被重力拉回的拉伸火花 + 慢慢飘起的余烬
//     黄 yellow → 雷弧：身周一闪即逝的折线电弧 + 四溅的电火花 + 静电星点
//     绿 green  → 落叶孢子：头顶左右荡着落下、来回摆动翻面的自然色叶片 + 脚下升起的柔光孢子
//     青 cyan   → 霜花：缓缓飘落旋转的六瓣雪花 + 冰晶闪烁 + 贴地的一层薄寒雾
//     蓝 blue   → 水泡涟漪：抖动上浮、末尾「啵」地破掉的薄壁气泡 + 脚下的双圈细涟漪 + 脚边溅起的水珠
//     紫 purple → 奥术环绕：带拖尾绕身旋转的星芒（一圈在转的法阵感）+ 升起的符光
//     白 white  → 珠光：细小光尘 + 绕身翻转、带虹彩的珍珠晶片 + 偶尔的星芒
//   蓝白绿三色用全 Mod 共享的贴图与材质（SharedLook），气泡 / 珠光 / 孢子是真加色，其余七色不变。
//     黑 black  → 暗影：卷曲升腾的暗色烟缕（浅色地面上清楚）+ 一点淡紫余烬勾边（深色地面上也看得见）
//     银 silver → 镜屑：身周一闪一闪的细碎亮片 + 旋转环绕的菱形镜屑
// 异色（最豪华的一档）：脚下两圈反向旋转、轻轻呼吸的金色符文环；从环上升起的金色星光；
//   身上不时一亮的大星芒；头顶一圈带拖尾的光冠；身周的细金尘；入场时一圈金星向外绽开；
//   一盏暖金点光。带炫彩的异色再叠一层强度 0.6 的元素特效。
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>炫彩元素。每种调色板颜色对应一种（映射在 PetNestAuraStyles.ResolveElement）。</summary>
    internal enum PetNestAuraElement
    {
        Radiant = 0,
        Flame = 1,
        Forge = 2,
        Thunder = 3,
        Verdant = 4,
        Frost = 5,
        Tide = 6,
        Arcane = 7,
        Umbra = 8,
        Glimmer = 9,
    }

    /// <summary>调色板颜色 → 元素。纯映射、无 Unity 依赖，守卫逐条核对十色都有专属元素。</summary>
    internal static class PetNestAuraStyles
    {
        internal static PetNestAuraElement ResolveElement(string colorId)
        {
            switch (colorId)
            {
                case "red": return PetNestAuraElement.Flame;
                case "orange": return PetNestAuraElement.Forge;
                case "yellow": return PetNestAuraElement.Thunder;
                case "green": return PetNestAuraElement.Verdant;
                case "cyan": return PetNestAuraElement.Frost;
                case "blue": return PetNestAuraElement.Tide;
                case "purple": return PetNestAuraElement.Arcane;
                case "white": return PetNestAuraElement.Radiant;
                case "black": return PetNestAuraElement.Umbra;
                case "silver": return PetNestAuraElement.Glimmer;
                // 调色板只增不改：将来新增的颜色在补专属元素之前，先用本色的珠光显示
                default: return PetNestAuraElement.Radiant;
            }
        }
    }

    public sealed partial class PetNestAuraEffect
    {
        #region 异色常量

        private const float ShinyHaloOuterDegreesPerSecond = 16f;
        private const float ShinyHaloInnerDegreesPerSecond = 30f;
        /// <summary>呼吸角速度（弧度/秒），约 2.6 秒一次。</summary>
        private const float ShinyPulseSpeed = 2.4f;
        private const float ShinyLightIntensityMin = 0.9f;
        private const float ShinyLightIntensityMax = 1.4f;

        /// <summary>赤色火焰的来源：官方火 AK-47（与 DragonBreathWeaponConfig.FIRE_AK47_TYPE_ID 同一件）。</summary>
        private const int OfficialFireAk47TypeId = 862;

        #endregion

        private static PetNestAuraElement ResolveElement(string colorId)
        {
            return PetNestAuraStyles.ResolveElement(colorId);
        }

        #region 颜色

        /// <summary>调色板粒子色 → Color；有色相的颜色略提饱和度，小亮点才不发灰。</summary>
        private static Color ToColor(PetNestChromaColor color)
        {
            if (color == null) return Color.white;
            Color c = new Color(color.ParticleR, color.ParticleG, color.ParticleB, 1f);
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (max <= 0.2f || max - min < 0.15f) return c; // 黑、白、银不动
            Color normalized = new Color(c.r / max, c.g / max, c.b / max, 1f);
            Color squared = new Color(normalized.r * normalized.r, normalized.g * normalized.g,
                normalized.b * normalized.b, 1f);
            return Color.Lerp(normalized, squared, 0.35f);
        }

        private static Color WithAlpha(Color c, float alpha)
        {
            c.a = alpha;
            return c;
        }

        #endregion

        #region 炫彩主元素

        private void BuildChromaPrimary(PetNestAuraElement element, PetNestChromaColor colorA, float f)
        {
            Color c = ToColor(colorA);
            switch (element)
            {
                case PetNestAuraElement.Flame: BuildFlame(f); break;
                case PetNestAuraElement.Forge: BuildForge(c, f); break;
                case PetNestAuraElement.Thunder: BuildThunder(c, f); break;
                case PetNestAuraElement.Verdant: BuildVerdant(c, f); break;
                case PetNestAuraElement.Frost: BuildFrost(c, f); break;
                case PetNestAuraElement.Tide: BuildTide(c, f); break;
                case PetNestAuraElement.Arcane: BuildArcane(c, f); break;
                case PetNestAuraElement.Umbra: BuildUmbra(f); break;
                case PetNestAuraElement.Glimmer: BuildGlimmer(c, f); break;
                default: BuildRadiant(c, f); break;
            }
        }

        /// <summary>赤：龙息火焰。优先克隆官方火 AK-47 的火舌与火星；预制体缺失时用程序化火焰兜底。</summary>
        private void BuildFlame(float f)
        {
            if (TryCloneOfficialFire(f)) return;
            BuildFlameFallback(f);
        }

        private bool TryCloneOfficialFire(float f)
        {
            GameObject source = ResolveOfficialFireGraphic();
            if (source == null) return false;
            Transform smoke = FindChildRecursive(source.transform, "Smoke");
            Transform spark = FindChildRecursive(source.transform, "Spark");
            float h = _height;
            float r = _radius;
            float u = _unit;
            bool any = false;

            if (smoke != null)
            {
                // 火舌：官方 SmokeFireFX 材质 + 8×8 序列帧，原本是贴着枪管 0.3–0.5 m 的大团；
                // 缩到 13–22 cm、向上窜、改世界空间——崽跑起来身后拖一小段火
                ParticleSystem flames = CloneOfficial(smoke.gameObject, "FlameTongues",
                    Scaled(14, f), new Vector3(0f, 0.06f * h, 0f));
                if (flames != null)
                {
                    any = true;
                    Life(flames, 0.45f, 0.8f);
                    Size(flames, 0.13f * u, 0.22f * u);
                    Speed(flames, 0.22f * u, 0.45f * u);
                    Rate(flames, 14f * f);
                    ParticleSystem.MainModule main = flames.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    ParticleSystem.ShapeModule shape = flames.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(1.5f * r, 1.5f * r, 0.02f);
                    shape.randomDirectionAmount = 0.15f;
                    // 官方 Smoke 的尺寸 / 颜色曲线是给枪管 0.3–0.5 m 大团调的，原样继承会在寿命后段
                    // 长到比崽还大：显式写「先窜起再收尖」与「亮黄 → 橙 → 暗红、淡入淡出」（VA-14）
                    ParticleSystem.SizeOverLifetimeModule size = flames.sizeOverLifetime;
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                        new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
                    Fade(flames, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.7f, 0.4f), new Color(0.7f, 0.25f, 0.1f), 0.1f, 0.5f);
                    Prewarm(flames);
                    BuildFlameLight(h);
                }
            }

            if (spark != null)
            {
                // 火星：官方 SodaSoftParticle（HDR 橙）拉伸公告板。原速 1–3 m/s、寿命 1–2 s 会飞出几米，
                // 这里压到 0.3–0.65 m/s、带一点上浮，噪声幅度同比缩小
                ParticleSystem embers = CloneOfficial(spark.gameObject, "Embers",
                    Scaled(12, f), new Vector3(0f, 0.2f * h, 0f));
                if (embers != null)
                {
                    any = true;
                    Life(embers, 0.6f, 1.1f);
                    Size(embers, 0.028f * u, 0.05f * u);
                    Speed(embers, 0.3f * u, 0.65f * u);
                    Gravity(embers, -0.08f);
                    Rate(embers, 8f * f);
                    ParticleSystem.MainModule main = embers.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    ParticleSystem.ShapeModule shape = embers.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(2f * r, 2f * r, 0.4f * h);
                    shape.randomDirectionAmount = 0.5f;
                    ParticleSystem.NoiseModule noise = embers.noise;
                    if (noise.enabled) noise.strengthMultiplier = 0.35f * u;
                    Prewarm(embers);
                }
            }
            return any;
        }

        /// <summary>
        /// 火光：一盏小范围、闪烁的暖橙点光（龙息武器同样带官方火光）。闪烁由 BossRushFxLightFade 自己驱动，
        /// 不启用本组件的 Update；随特效根一起淡出、销毁。
        /// </summary>
        private void BuildFlameLight(float h)
        {
            GameObject lightObject = new GameObject("FlameLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.3f * h, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.22f);
            light.range = Mathf.Clamp(2.2f * h, 0.8f, 1.8f);
            light.shadows = LightShadows.None;
            light.bounceIntensity = 0f;
            BossRushFxLightFade.Attach(light, 0.7f, 0.4f, 0f, 2.9f, 0.3f);
        }

        private void BuildFlameFallback(float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            Color yellow = new Color(1f, 0.78f, 0.3f);
            Color red = new Color(1f, 0.28f, 0.08f);

            ParticleSystem tongues = NewEmitter("FlameWisps", PetNestAuraTexture.Wisp, Glow.Hot,
                Scaled(10, f), new Vector3(0f, 0.05f * h, 0f), true);
            if (tongues != null)
            {
                ShapeCircle(tongues, 0.8f * r, 1f);
                Velocity(tongues, 0.3f * u, 0.5f * u, 0f, 0f);
                Life(tongues, 0.4f, 0.7f);
                Size(tongues, 0.12f * u, 0.2f * u);
                Grow(tongues, 1f, 0.4f);
                RandomSpin(tongues, 90f);
                Tint(tongues, WithAlpha(yellow, 0.85f), WithAlpha(red, 0.85f));
                Fade(tongues, Color.white, new Color(1f, 0.6f, 0.3f), new Color(0.8f, 0.15f, 0.05f), 0.1f, 0.55f);
                Rate(tongues, 14f * f);
                Prewarm(tongues);
            }

            ParticleSystem embers = NewEmitter("FlameEmbers", PetNestAuraTexture.GlowDot, Glow.Hot,
                Scaled(14, f), new Vector3(0f, 0.15f * h, 0f), true);
            if (embers != null)
            {
                ShapeCircle(embers, r, 1f);
                Velocity(embers, 0.25f * u, 0.5f * u, 0f, 0f);
                Noise(embers, 0.12f * u, 1.4f);
                Life(embers, 0.7f, 1.2f);
                Size(embers, 0.032f * u, 0.05f * u);
                Tint(embers, yellow, red);
                Fade(embers, Color.white, new Color(1f, 0.6f, 0.3f), new Color(0.8f, 0.15f, 0.05f), 0.1f, 0.6f);
                Stretch(embers, 0.04f, 1f);
                Rate(embers, 10f * f);
                Prewarm(embers);
            }
        }

        /// <summary>橙：锻火飞溅。</summary>
        private void BuildForge(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;

            ParticleSystem shower = NewEmitter("ForgeSparks", PetNestAuraTexture.GlowDot, Glow.Hot,
                Scaled(22, f), new Vector3(0f, 0.35f * h, 0f), true);
            if (shower != null)
            {
                ShapeCone(shower, 28f, 0.4f * r);
                Speed(shower, 0.9f * u, 1.5f * u);
                Gravity(shower, 0.45f);
                Life(shower, 0.45f, 0.75f);
                Size(shower, 0.03f * u, 0.05f * u);
                Rate(shower, 4f * f);
                Bursts(shower, 0.6f, 3, 5, 0.75f * f + 0.1f);
                Fade(shower, new Color(1f, 0.95f, 0.75f), c, new Color(0.85f, 0.18f, 0.05f), 0.02f, 0.7f);
                Stretch(shower, 0.07f, 1.2f);
            }

            ParticleSystem cinders = NewEmitter("ForgeCinders", PetNestAuraTexture.GlowDot, Glow.Bright,
                Scaled(8, f), new Vector3(0f, 0.1f * h, 0f), true);
            if (cinders != null)
            {
                ShapeCircle(cinders, r, 1f);
                Velocity(cinders, 0.08f * u, 0.18f * u, 0f, 0f);
                Noise(cinders, 0.12f * u, 0.8f);
                Life(cinders, 1f, 1.6f);
                Size(cinders, 0.035f * u, 0.06f * u);
                Tint(cinders, c, Color.Lerp(c, new Color(1f, 0.85f, 0.4f), 0.5f));
                Fade(cinders, Color.white, Color.white, new Color(0.7f, 0.2f, 0.1f), 0.15f, 0.6f);
                Rate(cinders, 4f * f);
                Prewarm(cinders);
            }
        }

        /// <summary>黄：雷弧。</summary>
        private void BuildThunder(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            Color hot = new Color(1f, 0.98f, 0.8f);

            ParticleSystem bolts = NewEmitter("ThunderBolts", PetNestAuraTexture.Bolt, Glow.Hot,
                Scaled(4, f), new Vector3(0f, 0.5f * h, 0f), false);
            if (bolts != null)
            {
                ShapeSphere(bolts, 0.7f * r, 0f);
                Life(bolts, 0.07f, 0.13f);
                Size(bolts, 0.2f * u, 0.3f * u);
                RandomSpin(bolts, 0f);
                Tint(bolts, hot, c);
                Pop(bolts);
                Bursts(bolts, 0.45f, 1, 2, 0.6f * f + 0.1f);
            }

            ParticleSystem crackle = NewEmitter("ThunderCrackle", PetNestAuraTexture.GlowDot, Glow.Hot,
                Scaled(16, f), new Vector3(0f, 0.5f * h, 0f), false);
            if (crackle != null)
            {
                ShapeSphere(crackle, 0.45f * r, 0f);
                Speed(crackle, 1.8f * u, 3f * u);
                Life(crackle, 0.06f, 0.12f);
                Size(crackle, 0.028f * u, 0.045f * u);
                Tint(crackle, hot, c);
                Fade(crackle, 0.02f, 0.5f);
                Stretch(crackle, 0.035f, 1f);
                Rate(crackle, 5f * f);
                Bursts(crackle, 0.45f, 3, 6, 0.6f * f + 0.1f);
            }

            ParticleSystem glints = NewEmitter("ThunderGlints", PetNestAuraTexture.Star, Glow.Hot,
                Scaled(4, f), new Vector3(0f, 0.55f * h, 0f), false);
            if (glints != null)
            {
                ShapeSphere(glints, 0.8f * r, 0f);
                Life(glints, 0.2f, 0.35f);
                Size(glints, 0.05f * u, 0.08f * u);
                Tint(glints, c, hot);
                Pop(glints);
                Rate(glints, 5f * f);
            }
        }

        /// <summary>
        /// 绿：落叶孢子。2026-09-25 第二轮（owner：「绿色的塑料叶子」）。上一轮的问题：叶片按崽身高放大后
        /// 有 30–45 cm（半个崽高）、一色荧光薄荷绿（调色板的绿提过饱和度）、Legacy 半透明下几乎不透明、
        /// 只在屏幕平面里匀速自转——一张张贴纸。现在：
        ///   - 叶片 10–14 cm：专门画的叶子（叶柄、微弯主脉、斜向侧脉、受光 / 背光两半、半透明叶缘，明暗在 RGB 里）；
        ///     颜色在黄绿 / 嫩绿 / 中绿 / 深绿一整段里随机，调色板的绿只混 12–15%；
        ///     下落时左右荡（正弦速度，每片方向幅度不同）、像钟摆一样来回摆而不是一直转，宽度一收一放像在翻面；
        ///     寿命末尾略泛黄变暗再淡出；
        ///   - 孢子：脚下升起的 1.4–2.6 cm 淡黄绿柔光点，加色，慢慢明灭。
        /// </summary>
        private void BuildVerdant(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;

            ParticleSystem leaves = NewEmitter("VerdantLeaves",
                SharedLook(BossRushParticleShape.Leaf, BossRushFxBlend.Alpha, 1.1f),
                Scaled(7, f), new Vector3(0f, h + 0.06f * u, 0f), true);
            if (leaves != null)
            {
                ShapeCircle(leaves, r + 0.04f * u, 0.35f);
                Life(leaves, 2.1f, 2.9f);
                Size(leaves, 0.1f * u, 0.14f * u);
                LeafDrift(leaves, -0.2f * u, -0.13f * u, 0.2f * u, 1.5f, 0.2f);
                Noise(leaves, 0.02f * u, 0.4f);
                LeafTumble(leaves);
                TintFoliage(leaves, c, 0.92f);
                Fade(leaves, Color.white, Color.white, new Color(0.9f, 0.84f, 0.6f), 0.1f, 0.72f);
                Rate(leaves, 2.4f * f);
                Prewarm(leaves);
            }

            ParticleSystem spores = NewEmitter("VerdantSpores",
                SharedLook(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, 1.5f),
                Scaled(12, f), new Vector3(0f, 0.04f * h, 0f), true);
            if (spores != null)
            {
                ShapeCircle(spores, r, 1f);
                Velocity(spores, 0.06f * u, 0.14f * u, 0f, 0f);
                Noise(spores, 0.06f * u, 0.9f);
                Life(spores, 1.6f, 2.4f);
                Size(spores, 0.014f * u, 0.026f * u);
                Tint(spores, WithAlpha(Color.Lerp(new Color(0.86f, 1f, 0.6f), c, 0.15f), 0.85f),
                    WithAlpha(Color.Lerp(new Color(0.7f, 0.95f, 0.45f), c, 0.2f), 0.75f));
                Twinkle(spores);
                Fade(spores, 0.12f, 0.7f);
                Rate(spores, 6f * f);
                Prewarm(spores);
            }
        }

        /// <summary>青：霜花。</summary>
        private void BuildFrost(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            Color ice = Color.Lerp(c, Color.white, 0.55f);

            // 雪花 16–22 cm：六条臂要在屏上认得出（VA-13；贴图臂宽也加粗了）
            ParticleSystem flakes = NewEmitter("FrostFlakes", PetNestAuraTexture.Snowflake, Glow.Bright,
                Scaled(6, f), new Vector3(0f, h + 0.05f * u, 0f), true);
            if (flakes != null)
            {
                ShapeCircle(flakes, r + 0.05f * u, 1f);
                Life(flakes, 1.6f, 2.4f);
                Size(flakes, 0.16f * u, 0.22f * u);
                Gravity(flakes, 0.02f);
                Noise(flakes, 0.1f * u, 0.6f);
                RandomSpin(flakes, 40f);
                Tint(flakes, c, ice);
                Fade(flakes, 0.12f, 0.8f);
                Rate(flakes, 2.2f * f);
                Prewarm(flakes);
            }

            ParticleSystem glints = NewEmitter("FrostGlints", PetNestAuraTexture.Star, Glow.Hot,
                Scaled(4, f), new Vector3(0f, 0.5f * h, 0f), false);
            if (glints != null)
            {
                ShapeSphere(glints, 0.85f * r, 0f);
                Life(glints, 0.25f, 0.4f);
                Size(glints, 0.05f * u, 0.075f * u);
                Tint(glints, Color.white, ice);
                Pop(glints);
                Rate(glints, 5f * f);
            }

            ParticleSystem mist = NewEmitter("FrostMist", PetNestAuraTexture.Wisp, Glow.Soft,
                Scaled(5, f), new Vector3(0f, 0.03f * h, 0f), true);
            if (mist != null)
            {
                ShapeCircle(mist, 0.6f * r, 1f);
                Velocity(mist, 0f, 0f, 0f, 0.08f * u);
                Life(mist, 1.3f, 1.8f);
                Size(mist, 0.16f * u, 0.24f * u);
                Grow(mist, 0.7f, 1.3f);
                RandomSpin(mist, 30f);
                Tint(mist, WithAlpha(ice, 0.22f), WithAlpha(c, 0.18f));
                Fade(mist, 0.2f, 0.6f);
                Rate(mist, 2.5f * f);
                Prewarm(mist);
            }
        }

        /// <summary>
        /// 蓝：水泡涟漪。2026-09-25 第二轮（owner：「蓝色的泡泡太塑料了」）。上一轮的问题：气泡按崽身高放大后
        /// 有 20–33 cm、饱和蓝、Legacy 半透明叠一圈 0.07 宽的均匀粗环 + 内填充——屏上是一枚枚实心感的蓝圈；
        /// 匀速直线上浮，到点线性淡掉；「水珠」用的是四角星。现在：
        ///   - 气泡 4–8 cm、加色：薄壁贴图（中心近乎透明、一圈粗细亮度不匀的细亮边、左上窗形高光 + 小亮点、
        ///     右下一点反光），偏冷的浅水色（调色板的蓝只混 10–20%），出生略带水蓝、升上去发白；
        ///     快频小幅抖着上浮、边升边胀，寿命最后 8% 猛地再胀一圈、亮一下、没了——「啵」；
        ///   - 涟漪：双圈细环（主环 + 淡回波）贴地，先快后慢地扩开，一出生就开始变淡；
        ///   - 水珠：脚边偶尔溅起两三颗极小的亮点，按速度拉成细水线再落回去。
        /// </summary>
        private void BuildTide(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;

            ParticleSystem bubbles = NewEmitter("TideBubbles",
                SharedLook(BossRushParticleShape.Bubble, BossRushFxBlend.Additive, 1.3f),
                Scaled(12, f), new Vector3(0f, 0.08f * h, 0f), true);
            if (bubbles != null)
            {
                ShapeCircle(bubbles, 0.75f * r, 1f);
                Velocity(bubbles, 0.13f * u, 0.24f * u, 0.25f, 0f);
                Noise(bubbles, 0.028f * u, 2.2f);
                Life(bubbles, 1.1f, 1.8f);
                Size(bubbles, 0.04f * u, 0.08f * u);
                TintWater(bubbles, c, 0.9f);
                BubbleLife(bubbles);
                Rate(bubbles, 6.5f * f);
                Prewarm(bubbles);
            }

            ParticleSystem ripples = NewEmitter("TideRipples",
                SharedLook(BossRushParticleShape.Ripple, BossRushFxBlend.Additive, 1.1f),
                Scaled(3, f), new Vector3(0f, 0.05f, 0f), true);
            if (ripples != null)
            {
                FlatOnGround(ripples);
                ShapeCircle(ripples, 0.35f * r, 1f);
                Life(ripples, 1.2f, 1.5f);
                Size(ripples, 0.1f * u, 0.12f * u);
                ParticleSystem.SizeOverLifetimeModule spread = ripples.sizeOverLifetime;
                spread.enabled = true;
                spread.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 0.35f, 2.6f, 1f, 3.8f));
                TintWater(ripples, c, 0.5f);
                AlphaLife(ripples, Color.white, Color.white, Color.white,
                    0f, 0f, 0.06f, 1f, 0.35f, 0.55f, 0.7f, 0.18f, 1f, 0f);
                Rate(ripples, 1.2f * f);
            }

            ParticleSystem drops = NewEmitter("TideDroplets",
                SharedLook(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, 1.6f),
                Scaled(6, f), new Vector3(0f, 0.03f * h, 0f), true);
            if (drops != null)
            {
                ShapeCone(drops, 28f, 0.35f * r);
                Speed(drops, 0.5f * u, 0.8f * u);
                Gravity(drops, 0.85f);
                Life(drops, 0.3f, 0.45f);
                Size(drops, 0.012f * u, 0.02f * u);
                TintWater(drops, c, 0.9f);
                Fade(drops, 0.02f, 0.7f);
                Stretch(drops, 0.05f, 1f);
                Bursts(drops, 0.9f, 2, 3, 0.55f * f + 0.1f);
            }
        }

        /// <summary>紫：奥术环绕。</summary>
        private void BuildArcane(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            Color pale = Color.Lerp(c, new Color(1f, 0.8f, 1f), 0.55f);

            ParticleSystem orbit = NewEmitter("ArcaneOrbit", PetNestAuraTexture.Star, Glow.Hot,
                Scaled(9, f), new Vector3(0f, 0.4f * h, 0f), false);
            if (orbit != null)
            {
                ShapeCircle(orbit, r + 0.08f * u, 0f);
                Velocity(orbit, 0.02f * u, 0.05f * u, 2.4f, 0f);
                Life(orbit, 1.8f, 2.4f);
                Size(orbit, 0.045f * u, 0.065f * u);
                Tint(orbit, c, pale);
                Fade(orbit, 0.1f, 0.8f);
                Rate(orbit, 4f * f);
                Trails(orbit, 0.18f, 0.015f * u);
                Prewarm(orbit);
            }

            ParticleSystem sigils = NewEmitter("ArcaneSigils", PetNestAuraTexture.GlowDot, Glow.Hot,
                Scaled(6, f), new Vector3(0f, 0.05f * h, 0f), true);
            if (sigils != null)
            {
                ShapeCircle(sigils, 0.7f * r, 1f);
                Velocity(sigils, 0.25f * u, 0.4f * u, 0f, 0f);
                Noise(sigils, 0.1f * u, 1.2f);
                Life(sigils, 0.6f, 1f);
                Size(sigils, 0.03f * u, 0.045f * u);
                Tint(sigils, pale, c);
                Fade(sigils, 0.1f, 0.6f);
                Rate(sigils, 5f * f);
                Prewarm(sigils);
            }
        }

        /// <summary>
        /// 白：珠光（也是未知颜色的兜底，用本色显示）。2026-09-25 第二轮（owner：蓝白绿要精致一点）。
        /// 上一轮的问题：「珍珠晶片」借的是银色镜屑那张满实心的硬边菱形，Legacy 半透明下是一块块发灰的白片；
        /// 光点 2–3.5 cm（放大后 4–8 cm）满 alpha 纯白，大星芒 7–10 cm。现在：
        ///   - 光尘：身周空气里 1.2–2.2 cm 的细小加色光点，缓缓上飘、柔和明灭；
        ///   - 珍珠晶片：专门画的柔边透镜形（细亮轮廓、中间一道珠光带、上部一粒高光、四周淡柔光），加色；
        ///     每片在极淡的粉 / 暖白 / 青 / 淡紫里随机，寿命里再从偏青慢慢转到偏粉（虹彩）；
        ///     绕身慢转、像薄片在翻，正对镜头那一下最亮；
        ///   - 星芒：偶尔一颗、比上一轮小，只「闪一下」。
        /// 本色占一半：白色时就是珠光，将来新增的颜色在补专属元素之前是本色的珠光。
        /// </summary>
        private void BuildRadiant(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;

            ParticleSystem dust = NewEmitter("RadiantDust",
                SharedLook(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, 1.7f),
                Scaled(14, f), new Vector3(0f, 0.45f * h, 0f), true);
            if (dust != null)
            {
                ShapeSphere(dust, 1.05f * r, 0.5f);
                Velocity(dust, 0.04f * u, 0.1f * u, 0f, 0f);
                Noise(dust, 0.05f * u, 0.7f);
                Life(dust, 1.8f, 2.6f);
                Size(dust, 0.012f * u, 0.022f * u);
                TintNacre(dust, c, 0.8f);
                Twinkle(dust);
                Fade(dust, 0.1f, 0.75f);
                Rate(dust, 6f * f);
                Prewarm(dust);
            }

            ParticleSystem flakes = NewEmitter("RadiantFlakes",
                SharedLook(BossRushParticleShape.Pearl, BossRushFxBlend.Additive, 1.2f),
                Scaled(5, f), new Vector3(0f, 0.5f * h, 0f), false);
            if (flakes != null)
            {
                ShapeCircle(flakes, r + 0.07f * u, 0f);
                Velocity(flakes, 0.02f * u, 0.05f * u, 0.6f, 0f);
                Life(flakes, 2f, 2.8f);
                Size(flakes, 0.05f * u, 0.075f * u);
                RandomSpin(flakes, 20f);
                TintNacre(flakes, c, 0.85f);
                PearlSheen(flakes);
                Rate(flakes, 2f * f);
                Prewarm(flakes);
            }

            ParticleSystem glints = NewEmitter("RadiantGlints",
                SharedLook(BossRushParticleShape.Star, BossRushFxBlend.Additive, 2f),
                Scaled(3, f), new Vector3(0f, 0.55f * h, 0f), false);
            if (glints != null)
            {
                ShapeSphere(glints, 0.8f * r, 0f);
                Life(glints, 0.25f, 0.4f);
                Size(glints, 0.045f * u, 0.065f * u);
                RandomSpin(glints, 0f);
                TintNacre(glints, c, 1f);
                Pop(glints);
                Bursts(glints, 1.1f, 1, 1, 0.5f * f + 0.1f);
            }
        }

        /// <summary>黑：暗影。颜色固定（调色板的黑本身就是近黑），不随 c 走。</summary>
        private void BuildUmbra(float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;

            ParticleSystem wisps = NewEmitter("UmbraWisps", PetNestAuraTexture.Wisp, Glow.Soft,
                Scaled(11, f), new Vector3(0f, 0.05f * h, 0f), true);
            if (wisps != null)
            {
                ShapeCircle(wisps, 0.8f * r, 1f);
                Velocity(wisps, 0.14f * u, 0.24f * u, 0f, 0f);
                Noise(wisps, 0.2f * u, 1.1f);
                Life(wisps, 0.9f, 1.4f);
                Size(wisps, 0.12f * u, 0.2f * u);
                Grow(wisps, 0.6f, 1.35f);
                RandomSpin(wisps, 60f);
                // 近黑但不是泥色：0.62 的不透明黑烟在浅色地面上像一块污渍，压到 0.45 并带一点紫
                Tint(wisps, new Color(0.09f, 0.07f, 0.13f, 0.45f), new Color(0.16f, 0.12f, 0.22f, 0.38f));
                Fade(wisps, 0.15f, 0.55f);
                Rate(wisps, 7f * f);
                Prewarm(wisps);
            }

            ParticleSystem rim = NewEmitter("UmbraEmbers", PetNestAuraTexture.GlowDot, Glow.Bright,
                Scaled(5, f), new Vector3(0f, 0.4f * h, 0f), true);
            if (rim != null)
            {
                ShapeSphere(rim, r, 0f);
                Velocity(rim, 0.1f * u, 0.2f * u, 0f, 0f);
                Life(rim, 0.6f, 1f);
                Size(rim, 0.032f * u, 0.05f * u);
                Tint(rim, new Color(0.72f, 0.6f, 0.95f), new Color(0.5f, 0.45f, 0.7f));
                Fade(rim, 0.1f, 0.5f);
                Rate(rim, 4f * f);
                Prewarm(rim);
            }
        }

        /// <summary>银：镜屑。</summary>
        private void BuildGlimmer(Color c, float f)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            Color bright = Color.Lerp(c, Color.white, 0.6f);

            ParticleSystem glitter = NewEmitter("GlimmerGlitter", PetNestAuraTexture.Star, Glow.Hot,
                Scaled(7, f), new Vector3(0f, 0.5f * h, 0f), false);
            if (glitter != null)
            {
                ShapeSphere(glitter, 0.9f * r, 0f);
                Life(glitter, 0.25f, 0.5f);
                Size(glitter, 0.035f * u, 0.06f * u);
                RandomSpin(glitter, 0f);
                Tint(glitter, bright, Color.white);
                Pop(glitter);
                Rate(glitter, 12f * f);
            }

            ParticleSystem shards = NewEmitter("GlimmerShards", PetNestAuraTexture.Shard, Glow.Bright,
                Scaled(5, f), new Vector3(0f, 0.45f * h, 0f), false);
            if (shards != null)
            {
                ShapeCircle(shards, r + 0.1f * u, 0f);
                Velocity(shards, -0.03f * u, 0.03f * u, 0.9f, 0f);
                Life(shards, 1.4f, 2f);
                Size(shards, 0.12f * u, 0.16f * u);
                RandomSpin(shards, 360f);
                Tint(shards, c, bright);
                Fade(shards, 0.12f, 0.8f);
                Rate(shards, 3f * f);
                Prewarm(shards);
            }
        }

        #endregion

        #region 炫彩点缀（第二色）

        /// <summary>
        /// 第二色：一圈反向环绕崽身的点缀，用第二色自己的元素贴图。
        /// 主元素自下而上、点缀横向环绕，两色在画面上分得开。
        /// </summary>
        private void BuildChromaAccent(PetNestAuraElement element, PetNestChromaColor colorB, float f)
        {
            Color c = ToColor(colorB);
            Color c2 = Color.Lerp(c, Color.white, 0.4f);
            PetNestAuraTexture kind = PetNestAuraTexture.GlowDot;
            Glow level = Glow.Hot;
            // 蓝白绿三色走共享材质（见 SharedLook），不再按 kind / level 取本实例材质；取不到就不建
            Material shared = null;
            bool sharedLook = false;
            bool nacre = false;
            float sizeMin = 0.035f;
            float sizeMax = 0.05f;
            bool trails = true;
            bool spin = false;

            switch (element)
            {
                case PetNestAuraElement.Flame:
                    c = new Color(1f, 0.42f, 0.12f);
                    c2 = new Color(1f, 0.75f, 0.3f);
                    break;
                case PetNestAuraElement.Forge:
                    c2 = Color.Lerp(c, new Color(1f, 0.9f, 0.4f), 0.5f);
                    break;
                case PetNestAuraElement.Thunder:
                    kind = PetNestAuraTexture.Star;
                    sizeMin = 0.05f;
                    sizeMax = 0.07f;
                    trails = false;
                    c2 = new Color(1f, 0.98f, 0.8f);
                    break;
                case PetNestAuraElement.Verdant:
                    // 绕身的一圈自然色叶片，来回摆、翻面（颜色与翻飞在下面按元素补）
                    shared = SharedLook(BossRushParticleShape.Leaf, BossRushFxBlend.Alpha, 1.1f);
                    sharedLook = true;
                    sizeMin = 0.075f;
                    sizeMax = 0.1f;
                    trails = false;
                    break;
                case PetNestAuraElement.Frost:
                    kind = PetNestAuraTexture.Snowflake;
                    level = Glow.Bright;
                    sizeMin = 0.13f;
                    sizeMax = 0.175f;
                    trails = false;
                    spin = true;
                    break;
                case PetNestAuraElement.Tide:
                    // 绕身的一圈薄壁气泡，加色、浅水色，末尾「啵」地破掉
                    shared = SharedLook(BossRushParticleShape.Bubble, BossRushFxBlend.Additive, 1.25f);
                    sharedLook = true;
                    sizeMin = 0.04f;
                    sizeMax = 0.065f;
                    trails = false;
                    break;
                case PetNestAuraElement.Arcane:
                    kind = PetNestAuraTexture.Star;
                    sizeMin = 0.045f;
                    sizeMax = 0.06f;
                    break;
                case PetNestAuraElement.Umbra:
                    kind = PetNestAuraTexture.Wisp;
                    level = Glow.Soft;
                    sizeMin = 0.09f;
                    sizeMax = 0.13f;
                    trails = false;
                    spin = true;
                    c = new Color(0.09f, 0.07f, 0.13f, 0.48f);
                    c2 = new Color(0.16f, 0.12f, 0.22f, 0.4f);
                    break;
                case PetNestAuraElement.Glimmer:
                    kind = PetNestAuraTexture.Shard;
                    level = Glow.Bright;
                    sizeMin = 0.1f;
                    sizeMax = 0.14f;
                    trails = false;
                    spin = true;
                    break;
                default:
                    // 白（与未知颜色）：绕身的一圈珍珠晶片，加色、珠母色、翻转时一亮一暗
                    shared = SharedLook(BossRushParticleShape.Pearl, BossRushFxBlend.Additive, 1.2f);
                    sharedLook = true;
                    sizeMin = 0.04f;
                    sizeMax = 0.055f;
                    trails = false;
                    nacre = true;
                    break;
            }

            float h = _height;
            float r = _radius;
            float u = _unit;
            if (sharedLook && shared == null) return;
            ParticleSystem ring = sharedLook
                ? NewEmitter("Accent_" + element, shared, Scaled(10, f), new Vector3(0f, 0.55f * h, 0f), false)
                : NewEmitter("Accent_" + element, kind, level, Scaled(10, f), new Vector3(0f, 0.55f * h, 0f), false);
            if (ring == null) return;
            ShapeCircle(ring, r + 0.14f * u, 0f);
            Velocity(ring, -0.02f * u, 0.04f * u, -1.7f, 0f);
            Life(ring, 1.6f, 2.2f);
            Size(ring, sizeMin * u, sizeMax * u);
            Tint(ring, c, c2);
            Fade(ring, 0.15f, 0.8f);
            Rate(ring, 3.6f * f);
            if (spin) RandomSpin(ring, 120f);
            if (element == PetNestAuraElement.Thunder) Twinkle(ring);
            if (trails) Trails(ring, 0.16f, 0.015f * u);
            // 蓝白绿三色的点缀与主元素同一套颜色与生命曲线（覆盖上面的通用 Tint / Fade）
            if (element == PetNestAuraElement.Tide)
            {
                TintWater(ring, c, 0.85f);
                BubbleLife(ring);
            }
            else if (element == PetNestAuraElement.Verdant)
            {
                TintFoliage(ring, c, 0.9f);
                LeafTumble(ring);
            }
            else if (nacre)
            {
                TintNacre(ring, c, 0.85f);
                PearlSheen(ring);
            }
            Prewarm(ring);
        }

        #endregion

        #region 异色

        private void BuildShiny()
        {
            float r = _radius;
            float u = _unit;
            Color gold = new Color(PetNestChroma.ShinyParticleR, PetNestChroma.ShinyParticleG,
                PetNestChroma.ShinyParticleB);
            Color whiteGold = new Color(1f, 0.97f, 0.85f);
            Color amber = new Color(1f, 0.6f, 0.15f);
            float haloRadius = r + 0.16f * u;

            BuildShinyHalo(haloRadius);
            BuildShinyStars(haloRadius, gold, whiteGold, amber);
            BuildShinyGlints(gold, whiteGold);
            BuildShinyCrown(gold, whiteGold);
            BuildShinyDust(gold, whiteGold);
            BuildShinyFlourish(gold, whiteGold);
            BuildShinyLight();
        }

        /// <summary>脚下两圈反向旋转的金色符文环（平铺网格，亮度与呼吸走 MaterialPropertyBlock）。</summary>
        private void BuildShinyHalo(float radius)
        {
            Mesh quad = Own(new Mesh());
            FillFlatQuad(quad);
            _haloBlock = new MaterialPropertyBlock();
            // 网格顶点色为白，颜色全在 tint 里：tint 是原始线性值，先把 sRGB 金色转线性
            _haloOuterTint = TintVector(new Color(1f, 0.84f, 0.42f).linear, 1.35f);
            _haloInnerTint = TintVector(new Color(1f, 0.93f, 0.66f).linear, 1.6f);
            // 离脚底抬 5 cm：贴得太低会被地面起伏吃掉（天空岛撤离环就被 +0.1 m 的铺面盖住过），
            // 55° 俯视下这点悬空看不出来
            _haloOuter = CreateHaloQuad("ShinyHaloOuter", quad, PetNestAuraTexture.RuneOuter,
                radius * 2f, 0.05f, _haloOuterTint, out _haloOuterRenderer);
            _haloInner = CreateHaloQuad("ShinyHaloInner", quad, PetNestAuraTexture.RuneInner,
                radius * 1.24f, 0.055f, _haloInnerTint, out _haloInnerRenderer);
        }

        private Transform CreateHaloQuad(string name, Mesh mesh, PetNestAuraTexture kind, float diameter,
            float height, Vector4 tint, out MeshRenderer renderer)
        {
            renderer = null;
            Material material = GetMaterial(kind, Glow.Soft);
            if (material == null || mesh == null) return null;

            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(diameter, 1f, diameter);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _haloBlock.SetVector(_tintPropertyId, tint);
            renderer.SetPropertyBlock(_haloBlock);
            return go.transform;
        }

        /// <summary>XZ 平面上的单位方片，法线朝上，白色顶点色（legacy 粒子着色器要读顶点色）。</summary>
        private static void FillFlatQuad(Mesh mesh)
        {
            mesh.name = "PetNestAuraFlatQuad";
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            Color32 white = new Color32(255, 255, 255, 255);
            mesh.colors32 = new Color32[] { white, white, white, white };
            // 俯视顺时针 = 正面朝上（着色器本身也不剔除背面）
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
        }

        /// <summary>从符文环上升起的金色星光（世界空间：崽跑起来身后留一串金星）。</summary>
        private void BuildShinyStars(float haloRadius, Color gold, Color whiteGold, Color amber)
        {
            float u = _unit;
            ParticleSystem stars = NewEmitter("ShinyStars", PetNestAuraTexture.Star, Glow.Hot,
                16, new Vector3(0f, 0.05f, 0f), true);
            if (stars == null) return;
            ShapeCircle(stars, haloRadius * 0.85f, 0.3f);
            Velocity(stars, 0.28f * u, 0.45f * u, 0f, 0f);
            Noise(stars, 0.05f * u, 0.8f);
            Life(stars, 1f, 1.5f);
            Size(stars, 0.05f * u, 0.085f * u);
            RandomSpin(stars, 60f);
            Tint(stars, whiteGold, gold);
            Fade(stars, Color.white, Color.white, amber, 0.08f, 0.7f);
            Twinkle(stars);
            Rate(stars, 10f);
            Prewarm(stars);
        }

        /// <summary>身上不时一亮的大星芒。</summary>
        private void BuildShinyGlints(Color gold, Color whiteGold)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            ParticleSystem glints = NewEmitter("ShinyGlints", PetNestAuraTexture.Star, Glow.Hot,
                3, new Vector3(0f, 0.55f * h, 0f), false);
            if (glints == null) return;
            ShapeSphere(glints, 0.85f * r, 0f);
            Life(glints, 0.2f, 0.3f);
            Size(glints, 0.15f * u, 0.22f * u);
            RandomSpin(glints, 0f);
            Tint(glints, Color.white, whiteGold);
            Pop(glints);
            Bursts(glints, 0.7f, 1, 1, 0.45f);
        }

        /// <summary>头顶一圈带拖尾的光冠。</summary>
        private void BuildShinyCrown(Color gold, Color whiteGold)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            ParticleSystem crown = NewEmitter("ShinyCrown", PetNestAuraTexture.GlowDot, Glow.Hot,
                10, new Vector3(0f, h + 0.1f * u, 0f), false);
            if (crown == null) return;
            ShapeCircle(crown, Mathf.Max(0.1f * u, 0.6f * r), 0f);
            Velocity(crown, 0f, 0f, 3f, 0f);
            Life(crown, 1.1f, 1.5f);
            Size(crown, 0.03f * u, 0.045f * u);
            Tint(crown, whiteGold, gold);
            Fade(crown, 0.1f, 0.8f);
            Rate(crown, 8f);
            Trails(crown, 0.18f, 0.01f * u);
            Prewarm(crown);
        }

        /// <summary>身周缓缓浮动的细金尘（「整只崽在发光」的空气感）。</summary>
        private void BuildShinyDust(Color gold, Color whiteGold)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            ParticleSystem dust = NewEmitter("ShinyDust", PetNestAuraTexture.GlowDot, Glow.Bright,
                18, new Vector3(0f, 0.5f * h, 0f), true);
            if (dust == null) return;
            ShapeSphere(dust, 1.1f * r, 1f);
            Noise(dust, 0.08f * u, 0.9f);
            Life(dust, 1.5f, 2.5f);
            Size(dust, 0.026f * u, 0.04f * u);
            Tint(dust, gold, whiteGold);
            Twinkle(dust);
            Rate(dust, 9f);
            Prewarm(dust);
        }

        /// <summary>入场时一圈金星向外绽开（只放一次，之后该发射器静默）。</summary>
        private void BuildShinyFlourish(Color gold, Color whiteGold)
        {
            float h = _height;
            float r = _radius;
            float u = _unit;
            ParticleSystem flourish = NewEmitter("ShinyFlourish", PetNestAuraTexture.Star, Glow.Hot,
                14, new Vector3(0f, 0.3f * h, 0f), false);
            if (flourish == null) return;
            ParticleSystem.MainModule main = flourish.main;
            main.loop = false;
            main.duration = 1f;
            ShapeCircle(flourish, 0.2f * r, 1f);
            Speed(flourish, 1.2f * u, 1.8f * u);
            Life(flourish, 0.5f, 0.7f);
            Size(flourish, 0.06f * u, 0.1f * u);
            RandomSpin(flourish, 0f);
            Tint(flourish, whiteGold, gold);
            Fade(flourish, 0.02f, 0.5f);
            ParticleSystem.EmissionModule emission = flourish.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)14) });
        }

        /// <summary>一盏暖金点光，在 Update 里轻轻呼吸（不投影）。</summary>
        private void BuildShinyLight()
        {
            GameObject lightObject = new GameObject("ShinyLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.55f * _height, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.82f, 0.48f);
            light.range = Mathf.Clamp(2.6f * _height, 0.9f, 2.4f);
            light.intensity = ShinyLightIntensityMin;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            _light = light;
        }

        #endregion

        #region 官方火焰克隆

        /// <summary>官方火 AK-47 的枪械图形预制体（Smoke / Spark 在它的 Model/GunModel 下）。</summary>
        private static GameObject ResolveOfficialFireGraphic()
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(OfficialFireAk47TypeId);
                if (prefab == null || prefab.ItemGraphic == null) return null;
                return prefab.ItemGraphic.gameObject;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 读取官方火 AK-47 预制体失败，赤色改用程序化火焰: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 克隆一个官方粒子系统挂到特效根下（Z 朝上），沿用它自己的官方材质，只改上限与尺度。
        /// 源对象没有粒子系统时不扣预算、返回 null。
        /// </summary>
        private ParticleSystem CloneOfficial(GameObject source, string name, int maxParticles, Vector3 localPosition)
        {
            if (source == null || source.GetComponent<ParticleSystem>() == null) return null;
            int cap = TakeBudget(maxParticles);
            if (cap <= 0) return null;

            GameObject copy = Instantiate(source, transform);
            copy.name = name;
            copy.transform.localPosition = localPosition;
            copy.transform.localRotation = ZUp;
            copy.transform.localScale = Vector3.one;

            // 只保留根上的粒子系统：官方预制体的子节点（子发射器、灯、别的粒子）不计预算、不随崽缩放
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(copy.transform.GetChild(i).gameObject);
            }

            ParticleSystem ps = copy.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.maxParticles = cap;
            main.playOnAwake = true;
            main.loop = true;
            main.scalingMode = ParticleSystemScalingMode.Local;
            // 三轴尺寸会让 Size() 写的 startSize 不生效；子发射器 / 灯光 / 碰撞模块一律关掉
            main.startSize3D = false;
            ParticleSystem.SubEmittersModule subEmitters = ps.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.LightsModule lights = ps.lights;
            lights.enabled = false;
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = false;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverDistance = 0f;

            ParticleSystemRenderer renderer = copy.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            _systems.Add(ps);
            return ps;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform found = parent.Find(name);
            if (found != null) return found;
            for (int i = 0; i < parent.childCount; i++)
            {
                found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        #endregion
    }
}

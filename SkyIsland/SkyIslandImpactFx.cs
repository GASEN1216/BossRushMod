// ============================================================================
// SkyIslandImpactFx.cs - 天空岛头目 / 岛主招式的预警填充与结算表现（纯表现层，不碰判定）
// ============================================================================
// 2026-09-23 特效审美审查 VB-21 / VB-22 / VB-24 / VB-25 查实：
//   - 结算那一帧预警圈直接没了，圆心冒一颗固定大小的官方手雷火球（1.8 m 的换位和 10 m 的噬风第三波是同一颗），
//     `shakeStrength = 0` 还把官方震屏关了——看起来像预警被取消；
//   - 预警只有「描边变粗变亮」，圈内是空的，玩家自己估还剩多久；
//   - 供能桩 / 根桩被打碎、断风冲步 / 镜首换位都是「东西没了 / 人瞬移了」，没有任何回执。
//
// 这里只加表现，判定半径、延迟与伤害一字不动：
//   1. SkyIslandBossRingFx：挂在 Boss 贴地圈上的附件——圈内一块随蓄力从 0 长到满圈的软边填充（计时读数），
//      出现 0.18 s 淡入、收起 0.16 s 淡出；边界圈本身的半径永远是判定半径（SkyIslandBossForge.SetRing）。
//   2. Play：结算时在原地放一圈「先闪一下、再扩到 1.08 R 淡出」的余波圈（判定此刻已经结算完）+ 圈沿扬尘。
//   3. Streak / Puff / Shatter / Splash / Sparks：风痕、扬尘团、桩碎、泥点、电火花。
//
// 粒子全部走三个世界空间的共享发射器（扬尘 / 碎屑 / 火花），挪到位置再 Emit 一次：已发出的粒子留在世界里，
// 不随发射器移动。只在真的有招式结算时懒建（AGENTS §4.12），没有 Boss 战时零成本；数量有上限。
// 材质走全 Mod 共享工厂 BossRushFxMaterials（软圆 / 本文件的软边带 / 软边圆盘）。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static class SkyIslandImpactFx
    {
        /// <summary>头目 / 岛主结算的震屏强度（官方手雷是 1）。只给一轮齐落的第一发，避免三圈叠成 1.5。</summary>
        internal const float BossShake = 0.5f;
        /// <summary>噬风每一波的震屏强度。</summary>
        internal const float StormShake = 0.8f;
        /// <summary>桩被打碎的震屏强度。</summary>
        internal const float ShatterShake = 0.3f;

        private const int DustCap = 192;
        private const int ChipCap = 96;
        private const int SparkCap = 96;
        private const float ImpactLift = 0.1f;
        private static readonly Color DustColor = new Color(0.78f, 0.70f, 0.58f, 1f);

        private static ParticleSystem dust, chips, sparks;
        private static Texture2D bandTexture, discTexture;

        /// <summary>圈沿扬尘颗数：随半径长、有上下限（半径 2 m 16 颗，6 m 起 48 颗封顶）。</summary>
        internal static int DustFor(float radius)
        {
            return Mathf.Clamp(Mathf.RoundToInt(8f * radius), 16, 48);
        }

        /// <summary>
        /// 结算表现：原地一圈余波（先闪、再扩到 1.08 R 淡出）+ 圈沿扬尘。伤害已由调用方结算完，这里只画。
        /// <paramref name="root"/> 是地图根（可为 null）；<paramref name="dustCount"/> ≤ 0 时按 <see cref="DustFor"/>。
        /// </summary>
        internal static void Play(Transform root, Vector3 origin, float radius, Color tint, int dustCount)
        {
            if (radius <= 0f) return;
            try
            {
                SkyIslandImpactRingFade.Spawn(root, origin, radius, tint);
                EmitDust(root, origin, radius, dustCount > 0 ? dustCount : DustFor(radius), 1f);
            }
            catch (System.Exception e)
            {
                // 纯表现层：画不出来不影响这一击已经结算。
                Debug.LogWarning("[SkyIslandBoss] 结算表现失败：" + e.Message);
            }
        }

        /// <summary>一小团扬尘（换位起落点、钻进根洞）：<paramref name="radius"/> 米的圈上 <paramref name="count"/> 颗、个头小一号。</summary>
        internal static void Puff(Transform root, Vector3 at, float radius, int count)
        {
            try { EmitDust(root, at, Mathf.Max(0.1f, radius), count, 0.6f); }
            catch (System.Exception e) { Debug.LogWarning("[SkyIslandBoss] 扬尘失败：" + e.Message); }
        }

        /// <summary>风痕：从起点到落点一道贴地的带子，0.22 s 内收窄淡出（冲步 / 换位的「它冲过来了」）。</summary>
        internal static void Streak(Transform root, Vector3 from, Vector3 to, Color tint)
        {
            try { SkyIslandStreakFade.Spawn(root, from, to, tint); }
            catch (System.Exception e) { Debug.LogWarning("[SkyIslandBoss] 风痕失败：" + e.Message); }
        }

        /// <summary>桩被打碎：一把飞散的碎片火花 + 一圈 0.9 → 1.6 m 的地面余波 + 轻微震屏。</summary>
        internal static void Shatter(Transform root, Vector3 at, Vector3 ground, Color tint)
        {
            try
            {
                Sparks(root, at, tint, 12);
                SkyIslandImpactRingFade.SpawnBurst(root, ground, 0.9f, 1.6f, tint);
                EmitDust(root, ground, 0.6f, 8, 0.6f);
                Shake(at, ShatterShake);
            }
            catch (System.Exception e) { Debug.LogWarning("[SkyIslandBoss] 碎裂表现失败：" + e.Message); }
        }

        /// <summary>泥点：脚下溅起几粒（穗镰的泥地）。</summary>
        internal static void Splash(Transform root, Vector3 at, Color color, int count)
        {
            try
            {
                ParticleSystem ps = Chips(root);
                if (ps == null) return;
                ParticleSystem.MainModule main = ps.main;
                main.startColor = color;
                ps.transform.position = at + Vector3.up * 0.05f;
                EmitNow(ps, count);
            }
            catch (System.Exception e) { Debug.LogWarning("[SkyIslandBoss] 泥点失败：" + e.Message); }
        }

        /// <summary>火花：加色、拉伸，≤ 0.12 m（电弧击中、桩碎）。</summary>
        internal static void Sparks(Transform root, Vector3 at, Color color, int count)
        {
            try
            {
                ParticleSystem ps = SparkEmitter(root);
                if (ps == null) return;
                ParticleSystem.MainModule main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.6f), color);
                ps.transform.position = at;
                EmitNow(ps, count);
            }
            catch (System.Exception e) { Debug.LogWarning("[SkyIslandBoss] 火花失败：" + e.Message); }
        }

        /// <summary>
        /// 官方口径的震屏（照 ExplosionManager.CreateExplosion：30 m 内、朝向从主角指向震源、×0.4）。
        /// 给不走官方爆炸的表现用（桩碎）；走官方爆炸的直接传 shakeStrength。
        /// </summary>
        internal static void Shake(Vector3 at, float strength)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || strength <= 0f) return;
            Vector3 offset = at - main.transform.position;
            if (offset.sqrMagnitude > 30f * 30f) return;
            Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.forward;
            CameraShaker.Shake(direction * 0.4f * strength, CameraShaker.CameraShakeTypes.explosion);
        }

        // ====================================================================
        // 材质与贴图
        // ====================================================================

        /// <summary>跨带宽方向羽化、沿线方向不变的软边带（线、风痕、光丝用）。颜色走顶点色。</summary>
        internal static Material SoftLineMaterial(bool additive)
        {
            return BossRushFxMaterials.Get(additive ? BossRushFxBlend.Additive : BossRushFxBlend.Alpha, BandTexture());
        }

        /// <summary>给一条线挂上加色软边带（电弧、光丝）；共享工厂不可用时用 <paramref name="fallback"/>。</summary>
        internal static void UseGlowLine(LineRenderer line, Material fallback)
        {
            if (line == null) return;
            Material glow = SoftLineMaterial(true);
            line.sharedMaterial = glow != null ? glow : fallback;
            line.textureMode = LineTextureMode.Stretch;
        }

        /// <summary>收尖：线宽从起点的 <paramref name="width"/> 收到终点的 <paramref name="width"/> × <paramref name="tipScale"/>。</summary>
        internal static void Taper(LineRenderer line, float width, float tipScale)
        {
            if (line == null) return;
            // 先写曲线再写倍率：startWidth / endWidth 的 setter 只改曲线端点，倍率仍是 1。
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, tipScale));
            line.widthMultiplier = width;
        }

        /// <summary>实心、外缘 10% 羽化的圆盘（预警填充、泥地面）。</summary>
        internal static Material DiscMaterial()
        {
            return BossRushFxMaterials.Get(BossRushFxBlend.Alpha, DiscTexture());
        }

        private static Texture2D BandTexture()
        {
            if (bandTexture != null) return bandTexture;
            const int width = 2;
            const int height = 32;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.name = "SkyIslandFx_SoftBand";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                // 中间一条亮芯、两侧平滑收到 0：细线也读得出「芯 + 晕」。
                float edge = 1f - Mathf.Abs(v * 2f - 1f);
                float alpha = BossRushUI.SmoothStep(Mathf.Clamp01(edge / 0.7f));
                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                for (int x = 0; x < width; x++) pixels[y * width + x] = new Color32(255, 255, 255, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            bandTexture = texture;
            return bandTexture;
        }

        private static Texture2D DiscTexture()
        {
            if (discTexture != null) return discTexture;
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            texture.name = "SkyIslandFx_SoftDisc";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // 0.9 以内实心，外缘 10% 羽化到 0：填满时边缘正好落在判定圈的带子下面。
                    float alpha = 1f - BossRushUI.SmoothStep((r - 0.9f) / 0.1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            discTexture = texture;
            return discTexture;
        }

        // ====================================================================
        // 共享发射器（世界空间，挪过去再 Emit；已发出的粒子不跟着动）
        // ====================================================================

        private static void EmitDust(Transform root, Vector3 at, float radius, int count, float sizeScale)
        {
            ParticleSystem ps = Dust(root);
            if (ps == null || count <= 0) return;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.radius = Mathf.Max(0.05f, radius);
            ParticleSystem.MainModule main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * sizeScale, 1.1f * sizeScale);
            ps.transform.position = at + Vector3.up * ImpactLift;
            EmitNow(ps, Mathf.Min(count, 48));
        }

        private static ParticleSystem Dust(Transform root)
        {
            if (dust != null) return dust;
            Material material = BossRushFxMaterials.Get(BossRushFxBlend.Alpha);
            if (material == null) return null;
            dust = NewEmitter(root, "SkyIslandImpactDust", material, DustCap);
            ParticleSystem.MainModule main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = DustColor;
            ParticleSystem.ShapeModule shape = dust.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            // Circle 默认在发射器局部 XY 平面（竖着）：转 90° 放平到地面上，出生方向随之变成水平向外。
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radiusThickness = 0f;
            ParticleSystem.VelocityOverLifetimeModule rise = dust.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.y = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SetFade(dust, DustColor, DustColor, 0.35f, 0.15f);
            SetGrow(dust, 0.6f, 1.4f);
            return dust;
        }

        private static ParticleSystem Chips(Transform root)
        {
            if (chips != null) return chips;
            Material material = BossRushFxMaterials.Get(BossRushFxBlend.Alpha);
            if (material == null) return null;
            chips = NewEmitter(root, "SkyIslandImpactChips", material, ChipCap);
            ParticleSystem.MainModule main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.gravityModifier = 1.5f;
            ParticleSystem.ShapeModule shape = chips.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.15f;
            // 锥形默认朝 +Z：转到朝上。
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SetFade(chips, Color.white, Color.white, 1f, 0.05f);
            SetGrow(chips, 1f, 0.5f);
            return chips;
        }

        private static ParticleSystem SparkEmitter(Transform root)
        {
            if (sparks != null) return sparks;
            Material material = BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            if (material == null) return null;
            sparks = NewEmitter(root, "SkyIslandImpactSparks", material, SparkCap);
            ParticleSystem.MainModule main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.gravityModifier = 1f;
            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            ParticleSystemRenderer renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.5f;
            renderer.velocityScale = 0.05f;
            SetFade(sparks, Color.white, Color.white, 1f, 0.02f);
            SetGrow(sparks, 1f, 0.3f);
            return sparks;
        }

        /// <summary>
        /// 发一把：发射器保持「在播、但不自己发」（发射模块关着、循环），Emit 出来的粒子一定被模拟。
        /// 地图根被停用再启用后发射器会停在非播放态，这里补一次 Play。
        /// </summary>
        private static void EmitNow(ParticleSystem ps, int count)
        {
            if (!ps.isPlaying) ps.Play();
            ps.Emit(count);
        }

        private static ParticleSystem NewEmitter(Transform root, string name, Material material, int cap)
        {
            GameObject go = new GameObject(name);
            if (root != null) go.transform.SetParent(root, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = cap;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>寿命内的颜色与透明度：<paramref name="rampIn"/> 之前从 0 淡入到峰值，之后淡出到 0。</summary>
        private static void SetFade(ParticleSystem ps, Color start, Color end, float peak, float rampIn)
        {
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, rampIn), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
        }

        private static void SetGrow(ParticleSystem ps, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        /// <summary>由 SkyIslandBossForge.ResetStaticCaches（SkyIslandRuntimeModule.OnDestroy）调用：收掉共享发射器与程序化贴图。</summary>
        internal static void ResetStaticCaches()
        {
            if (dust != null) Object.Destroy(dust.gameObject);
            if (chips != null) Object.Destroy(chips.gameObject);
            if (sparks != null) Object.Destroy(sparks.gameObject);
            dust = chips = sparks = null;
            // 贴图带 HideAndDontSave，切场景不会自动回收。材质由 BossRushFxMaterials 持有，这里不碰。
            if (bandTexture != null) Object.Destroy(bandTexture);
            if (discTexture != null) Object.Destroy(discTexture);
            bandTexture = null;
            discTexture = null;
        }

        /// <summary>地图根下的局部坐标，抬到贴地圈的统一高度（口径同 SkyIslandBossForge.LocalOf）。</summary>
        internal static Vector3 GroundLocal(Transform root, Vector3 world)
        {
            Vector3 local = root != null ? root.InverseTransformPoint(world) : world;
            return local + Vector3.up * SkyIslandGroundRing.GroundLift;
        }

        /// <summary>给一个贴地圈加一块软边圆盘子物体（与圈同平面、排在圈下面一层）。</summary>
        internal static LineRenderer CreateDisc(Transform ring)
        {
            Material material = DiscMaterial();
            if (ring == null || material == null) return null;
            GameObject go = new GameObject("WarningFill");
            go.transform.SetParent(ring, false);
            LineRenderer disc = go.AddComponent<LineRenderer>();
            // 两点直线 + 带宽 = 直径，TransformZ 让带子躺在父圈同一平面（父圈已绕 X 转 90°），软边圆盘贴图拉满就是一块圆。
            disc.useWorldSpace = false;
            disc.loop = false;
            disc.positionCount = 2;
            disc.numCapVertices = 0;
            disc.numCornerVertices = 0;
            disc.alignment = LineAlignment.TransformZ;
            disc.textureMode = LineTextureMode.Stretch;
            disc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            disc.receiveShadows = false;
            disc.sortingOrder = SkyIslandGroundRing.SortingOrder - 1;
            disc.sharedMaterial = material;
            SetDisc(disc, 0f, Color.clear);
            return disc;
        }

        internal static void SetDisc(LineRenderer disc, float radius, Color color)
        {
            if (disc == null) return;
            bool visible = radius > 0.01f && color.a > 0.002f;
            if (disc.enabled != visible) disc.enabled = visible;
            if (!visible) return;
            if (disc.GetPosition(1).x != radius)
            {
                disc.SetPosition(0, new Vector3(-radius, 0f, 0f));
                disc.SetPosition(1, new Vector3(radius, 0f, 0f));
                disc.widthMultiplier = radius * 2f;
            }
            if (!disc.startColor.Equals(color)) disc.startColor = color;
            if (!disc.endColor.Equals(color)) disc.endColor = color;
        }
    }

    /// <summary>
    /// Boss 贴地圈的附件（<see cref="SkyIslandBossForge.CreateGroundRing"/> 挂）：
    /// - 圈内一块软边填充，半径随蓄力从 0 长到判定半径，不透明度 0.10 → 0.25，最后一成蓄力闪到 0.4（「现在要落下」）；
    /// - 刚画出来 0.18 s 淡入，收起时 0.16 s 淡出再自毁（<see cref="Release"/>）。
    /// 边界圈半径由 SetRing 决定、永远等于判定半径，这里只动颜色的不透明度。只在淡入 / 淡出窗口里跑 LateUpdate，其余时间关着。
    /// </summary>
    internal sealed class SkyIslandBossRingFx : MonoBehaviour
    {
        private const float AppearSeconds = 0.18f;
        private const float ReleaseSeconds = 0.16f;

        private LineRenderer ring, fill;
        private Color ringColor, fillColor;
        private float fillRadius, age, releaseAge = -1f;

        internal static SkyIslandBossRingFx Attach(LineRenderer ring)
        {
            if (ring == null) return null;
            SkyIslandBossRingFx fx = ring.gameObject.AddComponent<SkyIslandBossRingFx>();
            fx.ring = ring;
            fx.ringColor = ring.startColor;
            fx.fill = SkyIslandImpactFx.CreateDisc(ring.transform);
            fx.enabled = true;
            return fx;
        }

        /// <summary>蓄力读数：<paramref name="solid"/> 是 SetRing 刚写给边界圈的颜色。</summary>
        internal void Charge(float radius, float charge, Color tint, Color solid)
        {
            charge = Mathf.Clamp01(charge);
            float alpha = charge < 0.9f
                ? Mathf.Lerp(0.10f, 0.25f, charge / 0.9f)
                : Mathf.Lerp(0.25f, 0.40f, (charge - 0.9f) / 0.1f);
            Apply(radius * charge, new Color(tint.r, tint.g, tint.b, alpha), solid);
        }

        /// <summary>常驻的一块地（穗镰漫开之后的泥）：填充铺满、用给定的地面色。</summary>
        internal void Surface(float radius, Color surface, Color solid)
        {
            Apply(radius, surface, solid);
        }

        private void Apply(float radius, Color fillTarget, Color solid)
        {
            ringColor = solid;
            fillColor = fillTarget;
            fillRadius = radius;
            float scale = Scale();
            SkyIslandImpactFx.SetDisc(fill, fillRadius, Scaled(fillColor, scale));
            if (scale < 1f && ring != null)
            {
                Color c = Scaled(ringColor, scale);
                ring.startColor = c;
                ring.endColor = c;
            }
        }

        /// <summary>收起：0.16 s 淡出后自毁。调用方随即丢掉引用、不再 SetRing。</summary>
        internal void Release()
        {
            if (releaseAge >= 0f) return;
            releaseAge = 0f;
            enabled = true;
        }

        private float Scale()
        {
            if (releaseAge >= 0f) return 1f - BossRushUI.SmoothStep(releaseAge / ReleaseSeconds);
            return age < AppearSeconds ? BossRushUI.SmoothStep(age / AppearSeconds) : 1f;
        }

        private static Color Scaled(Color color, float scale)
        {
            color.a *= scale;
            return color;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (releaseAge >= 0f) releaseAge += dt;
            else age += dt;
            float scale = Scale();
            if (ring != null)
            {
                Color c = Scaled(ringColor, scale);
                ring.startColor = c;
                ring.endColor = c;
            }
            SkyIslandImpactFx.SetDisc(fill, fillRadius, Scaled(fillColor, scale));
            if (releaseAge >= ReleaseSeconds)
            {
                Destroy(gameObject);
                return;
            }
            // 淡入完成且没有在收起：之后颜色全由 SetRing / Charge 写，这里关掉，不进每帧路径。
            if (releaseAge < 0f && age >= AppearSeconds) enabled = false;
        }
    }

    /// <summary>
    /// 结算余波圈：先闪 0.06 s（带宽 0.55 → 0.9、提亮），再 0.25 s 扩到 1.08 倍半径并淡出，然后自毁。
    /// 判定在它出生那一帧已经结算完，这圈只是「刚才那一下」的回执；原预警圈同帧开始淡出。
    /// </summary>
    internal sealed class SkyIslandImpactRingFade : MonoBehaviour
    {
        private const float FlashSeconds = 0.06f;
        private const float SpreadSeconds = 0.25f;

        private LineRenderer ring, fill;
        private float radius, endRadius, age, flashWidth, spreadWidth;
        private Color tint;
        private bool burst;

        internal static void Spawn(Transform root, Vector3 origin, float radius, Color tint)
        {
            SkyIslandImpactRingFade fade = Build(root, origin, radius, tint);
            if (fade == null) return;
            fade.endRadius = radius * 1.08f;
            fade.flashWidth = 0.9f;
            fade.spreadWidth = 0.35f;
            fade.fill = SkyIslandImpactFx.CreateDisc(fade.transform);
            fade.Step(0f);
        }

        /// <summary>小一号的地面余波：从 <paramref name="from"/> 扩到 <paramref name="to"/> 米、0.2 s 淡出（桩碎）。</summary>
        internal static void SpawnBurst(Transform root, Vector3 origin, float from, float to, Color tint)
        {
            SkyIslandImpactRingFade fade = Build(root, origin, from, tint);
            if (fade == null) return;
            fade.endRadius = to;
            fade.flashWidth = 0.35f;
            fade.spreadWidth = 0.12f;
            fade.burst = true;
            fade.Step(0f);
        }

        private static SkyIslandImpactRingFade Build(Transform root, Vector3 origin, float radius, Color tint)
        {
            LineRenderer line = SkyIslandGroundRing.Create(root, SkyIslandImpactFx.GroundLocal(root, origin));
            line.gameObject.name = "SkyIslandImpactRing";
            SkyIslandImpactRingFade fade = line.gameObject.AddComponent<SkyIslandImpactRingFade>();
            fade.ring = line;
            fade.radius = radius;
            fade.tint = tint;
            return fade;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (Step(age)) Destroy(gameObject);
        }

        /// <returns>播完了没有。</returns>
        private bool Step(float t)
        {
            float flash = burst ? 0f : FlashSeconds;
            float spread = burst ? 0.2f : SpreadSeconds;
            float r, width, alpha;
            Color hot = Color.Lerp(tint, Color.white, 0.35f);
            Color color;
            if (t < flash)
            {
                float k = BossRushUI.SmoothStep(t / flash);
                r = radius;
                width = Mathf.Lerp(0.55f, flashWidth, k);
                alpha = 1f;
                color = Color.Lerp(tint, hot, k);
            }
            else
            {
                float k = Mathf.Clamp01((t - flash) / spread);
                r = Mathf.Lerp(radius, endRadius, BossRushUI.EaseOut(k));
                width = Mathf.Lerp(flashWidth, spreadWidth, k);
                alpha = 1f - BossRushUI.SmoothStep(k);
                color = Color.Lerp(hot, tint, k);
            }
            color.a = alpha;
            SkyIslandGroundRing.SetShape(ring, r, width, color);
            if (fill != null)
            {
                // 圈内那块地亮一下再退：最亮 0.3，随余波一起淡掉。
                float fillAlpha = 0.3f * (t < flash ? 1f : 1f - BossRushUI.SmoothStep(Mathf.Clamp01((t - flash) / spread)));
                SkyIslandImpactFx.SetDisc(fill, radius, new Color(tint.r, tint.g, tint.b, fillAlpha));
            }
            return t >= flash + spread;
        }
    }

    /// <summary>冲步 / 换位留下的风痕：贴地一道从起点到落点的带子，0.22 s 内从 0.7 m 收窄到 0、淡出后自毁。</summary>
    internal sealed class SkyIslandStreakFade : MonoBehaviour
    {
        private const float Seconds = 0.22f;
        private const float StartWidth = 0.7f;

        private LineRenderer line;
        private Color tint;
        private float age;

        internal static void Spawn(Transform root, Vector3 from, Vector3 to, Color tint)
        {
            Material material = SkyIslandImpactFx.SoftLineMaterial(false);
            if (material == null) return;
            Vector3 flat = to - from;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.25f) return;
            GameObject go = new GameObject("SkyIslandStreak");
            if (root != null) go.transform.SetParent(root, false);
            // 世界坐标两点 + TransformZ：物体绕 X 转 90° 后 Z 轴朝下，带子摊平在地面上。
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = SkyIslandGroundRing.SortingOrder;
            line.sharedMaterial = material;
            Vector3 lift = Vector3.up * SkyIslandGroundRing.GroundLift;
            line.SetPosition(0, from + lift);
            line.SetPosition(1, to + lift);
            // 起点细、落点粗：看得出往哪边冲。
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f));
            SkyIslandStreakFade fade = go.AddComponent<SkyIslandStreakFade>();
            fade.line = line;
            fade.tint = tint;
            fade.Apply(0f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= Seconds)
            {
                Destroy(gameObject);
                return;
            }
            Apply(age / Seconds);
        }

        private void Apply(float k)
        {
            float eased = BossRushUI.EaseOut(k);
            line.widthMultiplier = Mathf.Lerp(StartWidth, 0f, eased);
            Color head = new Color(tint.r, tint.g, tint.b, 0.8f * (1f - eased));
            Color tail = new Color(tint.r, tint.g, tint.b, 0.25f * (1f - eased));
            line.startColor = tail;
            line.endColor = head;
        }
    }

    /// <summary>
    /// 任意贴地圈的收尾淡出（噬风的预警圈这类不挂 <see cref="SkyIslandBossRingFx"/> 的圈）：
    /// 从当前颜色开始 SmoothStep 淡到透明，然后自毁。调用方随即丢掉引用、不再写它。
    /// </summary>
    internal sealed class SkyIslandRingFadeOut : MonoBehaviour
    {
        private LineRenderer line;
        private Color start, end;
        private float seconds, age;

        internal static void Begin(LineRenderer ring, float duration)
        {
            if (ring == null) return;
            if (ring.GetComponent<SkyIslandRingFadeOut>() != null) return;
            SkyIslandRingFadeOut fade = ring.gameObject.AddComponent<SkyIslandRingFadeOut>();
            fade.line = ring;
            fade.start = ring.startColor;
            fade.end = ring.endColor;
            fade.seconds = Mathf.Max(0.01f, duration);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = 1f - BossRushUI.SmoothStep(age / seconds);
            if (line != null)
            {
                Color a = start, b = end;
                a.a *= k;
                b.a *= k;
                line.startColor = a;
                line.endColor = b;
            }
            if (age >= seconds) Destroy(gameObject);
        }
    }

    /// <summary>灯的淡入 / 淡出（0.2 s 左右，SmoothStep）。淡出到 0 后按需自毁整个物体或只关灯。</summary>
    internal sealed class SkyIslandLightFade : MonoBehaviour
    {
        private Light target;
        private float from, to, seconds, age;
        private bool destroyWhenDark;

        internal static void FadeTo(Light light, float intensity, float duration, bool destroyWhenDark)
        {
            if (light == null) return;
            SkyIslandLightFade fade = light.GetComponent<SkyIslandLightFade>();
            if (fade == null) fade = light.gameObject.AddComponent<SkyIslandLightFade>();
            fade.target = light;
            fade.from = light.enabled ? light.intensity : 0f;
            fade.to = intensity;
            fade.seconds = Mathf.Max(0.01f, duration);
            fade.age = 0f;
            fade.destroyWhenDark = destroyWhenDark;
            light.enabled = true;
            fade.enabled = true;
        }

        private void Update()
        {
            if (target == null) { enabled = false; return; }
            age += Time.deltaTime;
            float k = BossRushUI.SmoothStep(age / seconds);
            target.intensity = Mathf.Lerp(from, to, k);
            if (k < 1f) return;
            enabled = false;
            if (to > 0.001f) return;
            if (destroyWhenDark) Destroy(gameObject);
            else target.enabled = false;
        }
    }
}

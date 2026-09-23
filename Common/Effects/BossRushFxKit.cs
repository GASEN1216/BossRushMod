// ============================================================================
// BossRushFxKit.cs - 程序化特效的共享工具：亮度档材质、一次性粒子爆发、灯光淡入淡出、自然退场
// ============================================================================
// 为什么有这一层（2026-09-23 特效审美审查 VA-01…VA-34）：
//   套装、新武器、随机事件、奖励箱、征程终章各自手搓特效，反复出现同一批廉价感的根因：
//     - 粒子只有 startColor，没有 colorOverLifetime / sizeOverLifetime，出生即满、消失瞬断；
//     - 火花 / 碎片用 SpriteRenderer 平移一块实心椭圆，不朝运动方向，也不减速；
//     - 灯光开关硬切，退场直接 Destroy，粒子与光在同一帧消失；
//     - 想发光却只有 LDR 顶点色，推不到泛光。
//   这里把「做对了的写法」收成几个入口，各系统只填参数：
//     GetMaterial / GetShapeMaterial —— 在 BossRushFxMaterials（主会话的材质工厂）之上加亮度档：
//       gain > 1 时复制一份写 HDR 倍数（URP 写 _BaseColor，Legacy 写 _TintColor = 0.5×gain），吃官方 Bloom；
//     PlayBurst —— 一次性粒子爆发（火花、碎冰、烟花、尘土），World 空间、带寿命渐变与尺寸渐变，
//       发射只走 burst，死完由 stopAction 自毁；
//     CreateEmitter —— 常驻发射器的安全默认值（先失活再加组件，不会按默认参数先喷一团白色大颗粒）；
//     BossRushFxLightFade —— 灯光淡入、呼吸 / 闪烁、淡出，全部按 deltaTime（暂停时一起停）；
//     Release —— 停发射、灯淡出、等最后一颗粒子死完再销毁（替代「到点 Destroy 一帧消失」）。
//
// 材质口径：颜色一律走粒子 / 顶点色，材质本身只带贴图与亮度倍数。返回的材质是共享的，不要改它。
//
// 生命周期（AGENTS 4.6 / 4.12）：
//   静态缓存只有材质与两张 UI 精灵，懒加载；经 ResetStaticCaches 在 Mod 卸载路径销毁。
//   本类不自己启动任何逐帧工作：一次性爆发自毁，常驻发射器归调用方所有。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>一次性粒子爆发的参数。用 <see cref="BossRushFxKit.Sparks"/> 等预设起步再改字段。</summary>
    internal struct BossRushFxBurst
    {
        /// <summary>贴图形状。</summary>
        public BossRushParticleShape Shape;
        /// <summary>混合方式（加色只有 URP 粒子着色器可用时才真的是加色）。</summary>
        public BossRushFxBlend Blend;
        /// <summary>材质亮度倍数：1 = 原色，2 = 亮，3 = 热（火星、星点核心）。</summary>
        public float Gain;
        public int Count;
        public float SpeedMin, SpeedMax;
        public float SizeMin, SizeMax;
        public float LifeMin, LifeMax;
        public float Gravity;
        /// <summary>阻力：&gt;0 时先快后慢（与帧率无关的物理阻力）。</summary>
        public float Drag;
        /// <summary>&gt;0 时用拉伸公告板，值为 velocityScale（火花、碎片沿运动方向拉长）。</summary>
        public float Stretch;
        /// <summary>水平公告板（贴地的尘、霜）。</summary>
        public bool FlatOnGround;
        /// <summary>生命期尺寸终点倍数（起点 1）。</summary>
        public float GrowTo;
        /// <summary>发射体半径。</summary>
        public float ShapeRadius;
        /// <summary>只向上半球发射（落地尘、上窜余烬）。</summary>
        public bool Upward;
        /// <summary>在水平圆面上向外发射（贴地扩散的尘）。与 Upward 互斥，优先。</summary>
        public bool Radial;
        /// <summary>出生色（通常偏白，做「亮芯」）。</summary>
        public Color Core;
        /// <summary>主色（寿命 30% 处）。</summary>
        public Color Main;
        /// <summary>消散色（alpha 通常为 0）。</summary>
        public Color End;
        /// <summary>淡入占寿命的比例；0 = 出生即亮（火花）。</summary>
        public float FadeIn;
        /// <summary>&gt;0 时带拖尾，值为拖尾寿命占粒子寿命的比例。</summary>
        public float Trail;
        /// <summary>&gt;0 时随机初始角并按 ±Spin 度/秒自转。</summary>
        public float Spin;
    }

    /// <summary>程序化特效共享工具。见文件头。</summary>
    internal static class BossRushFxKit
    {
        /// <summary>亮度档：原色（烟、雾、尘）。</summary>
        internal const float GainSoft = 1f;
        /// <summary>亮度档：亮（能量线、光环主体）。</summary>
        internal const float GainBright = 2f;
        /// <summary>亮度档：热（火星、星点、电弧芯）。</summary>
        internal const float GainHot = 3f;

        private static readonly Dictionary<long, Material> gainMaterialCache = new Dictionary<long, Material>();
        private static Sprite softCircleSprite;
        private static readonly Sprite[] shapeSprites = new Sprite[(int)BossRushParticleShape.Count];
        private static Sprite vignetteSprite;
        private static Texture2D vignetteTexture;

        #region 材质

        /// <summary>
        /// 取一份共享材质：贴图 + 混合 + 亮度倍数。gain≈1 时直接返回工厂材质；
        /// gain&gt;1 时复制一份写 HDR 倍数（按 0.05 分档缓存）。找不到可用着色器时返回 null，调用方不画。
        /// </summary>
        internal static Material GetMaterial(BossRushFxBlend blend, Texture texture, float gain)
        {
            Material baseMaterial = BossRushFxMaterials.Get(blend, texture);
            if (baseMaterial == null) return null;
            int step = Mathf.Clamp(Mathf.RoundToInt(gain * 20f), 1, 400);
            if (step == 20) return baseMaterial;

            long key = ((long)baseMaterial.GetInstanceID() << 16) ^ step;
            Material cached;
            if (gainMaterialCache.TryGetValue(key, out cached) && cached != null && cached.mainTexture != null)
            {
                return cached;
            }
            if (cached != null) Object.Destroy(cached);

            float g = step / 20f;
            Material material = new Material(baseMaterial);
            material.name = baseMaterial.name + "_x" + g.ToString("0.##");
            material.hideFlags = HideFlags.HideAndDontSave;
            // SetVector 写原始值：SetColor 在线性空间会先做 sRGB→线性，把 >1 的倍数再放大一截
            if (material.HasProperty("_BaseColor"))
            {
                material.SetVector("_BaseColor", new Vector4(g, g, g, 1f));
            }
            else if (material.HasProperty("_TintColor"))
            {
                // Legacy 粒子着色器自带 ×2：0.5 灰是 1×
                float half = g * 0.5f;
                material.SetVector("_TintColor", new Vector4(half, half, half, 0.5f));
            }
            gainMaterialCache[key] = material;
            return material;
        }

        /// <summary>按共享形状取材质。</summary>
        internal static Material GetShapeMaterial(BossRushParticleShape shape, BossRushFxBlend blend, float gain)
        {
            Texture2D texture = BossRushParticleTextures.Get(shape);
            if (texture == null) return null;
            return GetMaterial(blend, texture, gain);
        }

        /// <summary>
        /// 给 SpriteRenderer 用的材质：贴图取精灵自己的那张（URP 粒子着色器读 _BaseMap、Legacy 读 _MainTex，
        /// 两条路都落到同一张图上）。精灵应占满整张贴图。
        /// </summary>
        internal static Material GetSpriteMaterial(Sprite sprite, BossRushFxBlend blend, float gain)
        {
            if (sprite == null || sprite.texture == null) return null;
            return GetMaterial(blend, sprite.texture, gain);
        }

        /// <summary>全 Mod 共用软圆（BossRushFxMaterials.GetSoftCircleTexture）的精灵版，给 SpriteRenderer 用。</summary>
        internal static Sprite GetSoftCircleSprite()
        {
            if (softCircleSprite != null && softCircleSprite.texture != null) return softCircleSprite;
            Texture2D texture = BossRushFxMaterials.GetSoftCircleTexture();
            if (texture == null) return null;
            if (softCircleSprite != null) Object.Destroy(softCircleSprite);
            softCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), texture.width);
            softCircleSprite.name = "BossRushFx_SoftCircleSprite";
            softCircleSprite.hideFlags = HideFlags.HideAndDontSave;
            return softCircleSprite;
        }

        /// <summary>共享形状贴图的精灵版（给 SpriteRenderer 用，例如面朝镜头的眼光亮点）。</summary>
        internal static Sprite GetShapeSprite(BossRushParticleShape shape)
        {
            int index = (int)shape;
            if (index < 0 || index >= shapeSprites.Length) return null;
            Sprite cached = shapeSprites[index];
            if (cached != null && cached.texture != null) return cached;
            Texture2D texture = BossRushParticleTextures.Get(shape);
            if (texture == null) return null;
            if (cached != null) Object.Destroy(cached);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), texture.width);
            sprite.name = "BossRushFx_" + shape + "Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            shapeSprites[index] = sprite;
            return sprite;
        }

        /// <summary>
        /// 全屏暗角精灵（白色，形状只在 alpha 里）：中心透明，向边缘按
        /// pow(smoothstep(0.45, 1.05, r), 1.5) 加深，四角为 1。贴到全屏 Image 上拉伸成 16:9 就是椭圆暗角。
        /// 给血月一类「全屏氛围」用，不盖画面中心。
        /// </summary>
        internal static Sprite GetVignetteSprite()
        {
            if (vignetteSprite != null && vignetteTexture != null) return vignetteSprite;
            const int size = 256;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "BossRushFx_Vignette";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            float inv = 2f / size;
            for (int py = 0; py < size; py++)
            {
                float y = (py + 0.5f) * inv - 1f;
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) * inv - 1f;
                    float r = Mathf.Sqrt(x * x + y * y);
                    float t = Mathf.Clamp01((r - 0.45f) / 0.6f);
                    float a = Mathf.Pow(t * t * (3f - 2f * t), 1.5f);
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f + 0.5f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            if (vignetteSprite != null) Object.Destroy(vignetteSprite);
            vignetteTexture = texture;
            vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            vignetteSprite.name = "BossRushFx_VignetteSprite";
            vignetteSprite.hideFlags = HideFlags.HideAndDontSave;
            return vignetteSprite;
        }

        #endregion

        #region 一次性爆发

        /// <summary>火花 / 碎屑预设：亮芯 → 主色 → 透明，拉伸公告板，先快后慢。</summary>
        internal static BossRushFxBurst Sparks(Color main, int count)
        {
            BossRushFxBurst spec = new BossRushFxBurst();
            spec.Shape = BossRushParticleShape.GlowDot;
            spec.Blend = BossRushFxBlend.Additive;
            spec.Gain = GainHot;
            spec.Count = count;
            spec.SpeedMin = 4f;
            spec.SpeedMax = 7f;
            spec.SizeMin = 0.05f;
            spec.SizeMax = 0.08f;
            spec.LifeMin = 0.18f;
            spec.LifeMax = 0.3f;
            spec.Drag = 6f;
            spec.Stretch = 0.04f;
            spec.GrowTo = 0.4f;
            spec.ShapeRadius = 0.1f;
            spec.Core = new Color(1f, 1f, 1f, 1f);
            spec.Main = new Color(main.r, main.g, main.b, 0.9f);
            spec.End = new Color(main.r * 0.6f, main.g * 0.6f, main.b * 0.6f, 0f);
            return spec;
        }

        /// <summary>尘土 / 烟预设：贴地扩散的烟缕，淡入后长大淡出。</summary>
        internal static BossRushFxBurst Dust(Color color, int count)
        {
            BossRushFxBurst spec = new BossRushFxBurst();
            spec.Shape = BossRushParticleShape.Wisp;
            spec.Blend = BossRushFxBlend.Alpha;
            spec.Gain = GainSoft;
            spec.Count = count;
            spec.SpeedMin = 2f;
            spec.SpeedMax = 3f;
            spec.SizeMin = 0.5f;
            spec.SizeMax = 0.9f;
            spec.LifeMin = 0.8f;
            spec.LifeMax = 1.2f;
            spec.Drag = 3.5f;
            spec.GrowTo = 1.8f;
            spec.ShapeRadius = 0.3f;
            spec.Radial = true;
            spec.FlatOnGround = true;
            spec.FadeIn = 0.12f;
            spec.Spin = 40f;
            spec.Core = color;
            spec.Main = color;
            spec.End = new Color(color.r, color.g, color.b, 0f);
            return spec;
        }

        /// <summary>
        /// 在 position 放一次性粒子爆发，粒子死完自毁。材质不可用或 Count≤0 时什么都不建，返回 null。
        /// </summary>
        internal static ParticleSystem PlayBurst(Vector3 position, BossRushFxBurst spec)
        {
            if (spec.Count <= 0) return null;
            Material material = GetShapeMaterial(spec.Shape, spec.Blend, spec.Gain);
            if (material == null) return null;

            GameObject go = new GameObject("BossRushFxBurst");
            go.SetActive(false);
            go.transform.position = position;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.maxParticles = spec.Count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Max(0.02f, spec.LifeMin), Mathf.Max(spec.LifeMin, spec.LifeMax));
            main.startSpeed = new ParticleSystem.MinMaxCurve(spec.SpeedMin, Mathf.Max(spec.SpeedMin, spec.SpeedMax));
            main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Max(0.001f, spec.SizeMin), Mathf.Max(spec.SizeMin, spec.SizeMax));
            main.startColor = Color.white;
            main.gravityModifier = spec.Gravity;
            if (spec.Spin > 0f)
            {
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                float radians = spec.Spin * Mathf.Deg2Rad;
                rotation.z = new ParticleSystem.MinMaxCurve(-radians, radians);
            }

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)Mathf.Min(spec.Count, short.MaxValue)) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.radius = Mathf.Max(0.001f, spec.ShapeRadius);
            shape.radiusThickness = 1f;
            if (spec.Radial)
            {
                // Circle 在形状本地 XY 平面内向外发射；绕 X 转 90° 放平到世界水平面
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.arc = 360f;
                shape.rotation = new Vector3(90f, 0f, 0f);
            }
            else if (spec.Upward)
            {
                // Hemisphere 朝 +Z；绕 X 转 -90° 朝上
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
            }

            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(spec.Core, spec.Main, spec.End, spec.FadeIn);

            if (spec.GrowTo > 0f && !Mathf.Approximately(spec.GrowTo, 1f))
            {
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, spec.GrowTo));
            }

            if (spec.Drag > 0f)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(1000f);
                limit.dampen = 0f;
                limit.drag = new ParticleSystem.MinMaxCurve(spec.Drag);
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                if (spec.Stretch > 0f)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = spec.Stretch;
                    renderer.lengthScale = 1f;
                    renderer.cameraVelocityScale = 0f;
                }
                else
                {
                    renderer.renderMode = spec.FlatOnGround
                        ? ParticleSystemRenderMode.HorizontalBillboard
                        : ParticleSystemRenderMode.Billboard;
                }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                if (spec.Trail > 0f)
                {
                    Material trailMaterial = GetShapeMaterial(BossRushParticleShape.TrailStrip, spec.Blend, spec.Gain);
                    if (trailMaterial != null)
                    {
                        renderer.trailMaterial = trailMaterial;
                        ParticleSystem.TrailModule trails = ps.trails;
                        trails.enabled = true;
                        trails.mode = ParticleSystemTrailMode.PerParticle;
                        trails.ratio = 1f;
                        trails.lifetime = new ParticleSystem.MinMaxCurve(Mathf.Clamp01(spec.Trail));
                        trails.minVertexDistance = 0.05f;
                        trails.dieWithParticles = true;
                        trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                        trails.sizeAffectsWidth = true;
                        trails.inheritParticleColor = true;
                        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 0f));
                    }
                }
            }

            go.SetActive(true);
            ps.Play(true);
            return ps;
        }

        /// <summary>
        /// 生命期颜色：出生 → 主色（30% 处）→ 消散。fadeIn&gt;0 时 alpha 从 0 淡入，否则出生即是 core.a。
        /// </summary>
        internal static ParticleSystem.MinMaxGradient FadeGradient(Color core, Color main, Color end, float fadeIn)
        {
            Gradient gradient = new Gradient();
            float mid = Mathf.Clamp(Mathf.Max(0.3f, fadeIn), 0.05f, 0.9f);
            GradientAlphaKey[] alpha;
            if (fadeIn > 0f)
            {
                alpha = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(core.a, Mathf.Clamp(fadeIn, 0.01f, 0.5f)),
                    new GradientAlphaKey(main.a, Mathf.Clamp(mid + 0.1f, 0.1f, 0.95f)),
                    new GradientAlphaKey(end.a, 1f),
                };
            }
            else
            {
                alpha = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(core.a, 0f),
                    new GradientAlphaKey(main.a, mid),
                    new GradientAlphaKey(end.a, 1f),
                };
            }
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(core, 0f),
                    new GradientColorKey(main, mid),
                    new GradientColorKey(end, 1f),
                },
                alpha);
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        #endregion

        #region 常驻发射器与退场

        /// <summary>
        /// 新建一个挂在 parent 下的常驻发射器，所有默认参数改成安全值（循环、不自动播放、零速率、零速度、白色）。
        /// 先失活再加组件，所以不会按 Unity 默认参数先喷一团白色大颗粒。调用方配完模块后自己 Play()。
        /// 材质不可用时返回 null。
        /// </summary>
        internal static ParticleSystem CreateEmitter(string name, Transform parent, Vector3 localPosition,
            Material material, int maxParticles, bool worldSpace)
        {
            if (material == null || parent == null) return null;
            GameObject go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 2f;
            main.maxParticles = Mathf.Max(1, maxParticles);
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            go.SetActive(true);
            return ps;
        }

        /// <summary>
        /// 让一棵特效自然退场：所有粒子停发射（已发出的照常走完寿命），灯在 lightFadeSeconds 内淡到 0，
        /// 最后一颗粒子死完（或 maxWaitSeconds 到）再销毁根。detach=true 时先脱离父节点留在原地，
        /// 父对象（崽、Boss、箱子）当帧被销毁也不会把尾巴一起带走。
        /// </summary>
        internal static void Release(GameObject root, float lightFadeSeconds, float maxWaitSeconds, bool detach)
        {
            if (root == null) return;
            try
            {
                if (detach && root.transform.parent != null)
                {
                    root.transform.SetParent(null, true);
                }
                ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(false);
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] != null) systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
                Light[] lights = root.GetComponentsInChildren<Light>(false);
                for (int i = 0; i < lights.Length; i++)
                {
                    Light light = lights[i];
                    if (light == null) continue;
                    BossRushFxLightFade fade = light.GetComponent<BossRushFxLightFade>();
                    if (fade == null) fade = BossRushFxLightFade.Attach(light, light.intensity, 0f);
                    fade.FadeOut(lightFadeSeconds, false);
                }
                BossRushFxReleaser releaser = root.GetComponent<BossRushFxReleaser>();
                if (releaser == null) releaser = root.AddComponent<BossRushFxReleaser>();
                releaser.Begin(systems, maxWaitSeconds);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRushFxKit] Release 失败，直接销毁: " + e.Message);
                Object.Destroy(root);
            }
        }

        #endregion

        /// <summary>销毁亮度档材质与精灵（Mod 卸载路径）。形状贴图由 BossRushParticleTextures 自己清。</summary>
        internal static void ResetStaticCaches()
        {
            foreach (KeyValuePair<long, Material> pair in gainMaterialCache)
            {
                if (pair.Value != null) Object.Destroy(pair.Value);
            }
            gainMaterialCache.Clear();
            if (softCircleSprite != null) Object.Destroy(softCircleSprite);
            softCircleSprite = null;
            for (int i = 0; i < shapeSprites.Length; i++)
            {
                if (shapeSprites[i] != null) Object.Destroy(shapeSprites[i]);
                shapeSprites[i] = null;
            }
            if (vignetteSprite != null) Object.Destroy(vignetteSprite);
            vignetteSprite = null;
            if (vignetteTexture != null) Object.Destroy(vignetteTexture);
            vignetteTexture = null;
            BossRushParticleTextures.ResetStaticCaches();
        }
    }

    /// <summary>
    /// 灯光淡入 / 呼吸 / 闪烁 / 淡出。只在挂着它的灯存在期间运行（O(1)），按 deltaTime（游戏暂停时一起停）。
    /// </summary>
    internal sealed class BossRushFxLightFade : MonoBehaviour
    {
        private Light _light;
        private float _target;
        private float _fadeIn;
        private float _breatheAmplitude;
        private float _breatheSpeed;
        private float _flickerAmplitude;
        private float _level;
        private float _time;
        private float _seed;
        private float _fadeOut = -1f;
        private float _fadeOutFrom = 1f;
        private bool _destroyOnDone;

        /// <summary>
        /// 给一盏灯挂淡入：fadeInSeconds 内按 SmoothStep 从 0 亮到 targetIntensity。
        /// breatheAmplitude 是相对幅度（0.15 = ±15%），breathePeriod 是一次呼吸的秒数；
        /// flickerAmplitude&gt;0 时叠一层火焰式 Perlin 闪烁（相对幅度）。
        /// </summary>
        internal static BossRushFxLightFade Attach(Light light, float targetIntensity, float fadeInSeconds,
            float breatheAmplitude = 0f, float breathePeriod = 2.9f, float flickerAmplitude = 0f)
        {
            if (light == null) return null;
            BossRushFxLightFade fade = light.GetComponent<BossRushFxLightFade>();
            if (fade == null) fade = light.gameObject.AddComponent<BossRushFxLightFade>();
            fade._light = light;
            fade._target = Mathf.Max(0f, targetIntensity);
            fade._fadeIn = Mathf.Max(0f, fadeInSeconds);
            fade._breatheAmplitude = Mathf.Clamp01(breatheAmplitude);
            fade._breatheSpeed = breathePeriod > 0.05f ? (Mathf.PI * 2f) / breathePeriod : 0f;
            fade._flickerAmplitude = Mathf.Clamp01(flickerAmplitude);
            fade._level = fade._fadeIn > 0f ? 0f : 1f;
            fade._seed = Random.value * 100f;
            fade._fadeOut = -1f;
            fade.enabled = true;
            fade.Apply();
            return fade;
        }

        /// <summary>改目标亮度（不重新淡入）。</summary>
        internal void SetTarget(float intensity)
        {
            _target = Mathf.Max(0f, intensity);
        }

        /// <summary>seconds 内淡到 0。destroyGameObject=true 时淡完销毁灯所在的物体，否则只关灯。</summary>
        internal void FadeOut(float seconds, bool destroyGameObject)
        {
            _fadeOut = Mathf.Max(0.01f, seconds);
            _fadeOutFrom = _level;
            _destroyOnDone = destroyGameObject;
            enabled = true;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _light == null) return;
            _time += dt;
            if (_fadeOut > 0f)
            {
                _level = Mathf.MoveTowards(_level, 0f, _fadeOutFrom * dt / _fadeOut);
                if (_level <= 0f)
                {
                    _light.intensity = 0f;
                    _light.enabled = false;
                    enabled = false;
                    if (_destroyOnDone) Destroy(gameObject);
                    return;
                }
            }
            else if (_level < 1f)
            {
                _level = _fadeIn > 0f ? Mathf.MoveTowards(_level, 1f, dt / _fadeIn) : 1f;
            }
            Apply();
        }

        private void Apply()
        {
            if (_light == null) return;
            float shaped = BossRushUI.SmoothStep(_level);
            float factor = 1f;
            if (_breatheSpeed > 0f && _breatheAmplitude > 0f)
            {
                factor += _breatheAmplitude * Mathf.Sin(_time * _breatheSpeed + _seed);
            }
            if (_flickerAmplitude > 0f)
            {
                factor += _flickerAmplitude * (Mathf.PerlinNoise(_time * 7.5f, _seed) - 0.5f) * 2f;
            }
            _light.intensity = _target * shaped * Mathf.Max(0f, factor);
            if (!_light.enabled && shaped > 0f) _light.enabled = true;
        }
    }

    /// <summary>退场中的特效根：等所有粒子死完（或超时）再销毁自己。只在退场的那一两秒里存在。</summary>
    internal sealed class BossRushFxReleaser : MonoBehaviour
    {
        private ParticleSystem[] _systems;
        private float _remaining;

        internal void Begin(ParticleSystem[] systems, float maxWaitSeconds)
        {
            _systems = systems;
            _remaining = Mathf.Clamp(maxWaitSeconds, 0.05f, 10f);
            enabled = true;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f || !AnyAlive())
            {
                Destroy(gameObject);
            }
        }

        private bool AnyAlive()
        {
            if (_systems == null) return false;
            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps != null && ps.IsAlive(false)) return true;
            }
            return false;
        }
    }
}

// ============================================================================
// PetNestAuraEffect.cs - 崽身上的炫彩 / 异色特效（owner 需求 14；2026-09-22 实测第 9、12 条重做）
// ============================================================================
// owner 口径：
//   「崽们也要带上自己的炫彩和异色的特效，在他们自己身上……比如龙息的火焰粒子，就可以复刻到
//    崽的身上，不同炫彩弄不同的粒子特效，异色则是最豪华的最好看的。」
//   「异色弄的特效要帅不要廉价，你现在这个异色太廉价了」（截图：崽周围一大团扁平的半透明黄雾）
//
// 旧实现为什么廉价（已整段替换，不再继承 RingParticleEffect）：
//   - 霜雾 / 腾云驾雾那套配方：0.85 m 的圆形渐变粒子、六个发射器、LateUpdate 每帧再手撒一颗，
//     密度随帧率走；材质 _TintColor 写成白色，legacy 粒子着色器里等于 alpha 再翻一倍；
//   - 挂在角色根上、尺寸按成年角色调，而旧崽的模型只有 0.25–0.4 倍——雾比崽大三倍；
//   - 炫彩两层只差颜色，十种颜色看起来是同一团雾。
//
// 现在的做法（配方在 PetNestAuraRecipes.cs）：
//   - 每种炫彩颜色对应一个**元素**（赤=龙息火焰、橙=锻火飞溅、黄=雷弧、绿=落叶孢子、
//     青=霜花、蓝=水泡涟漪、紫=奥术环绕、白=圣光、黑=暗影、银=镜屑），搭配的第一色是主元素、
//     第二色是一圈反向环绕的点缀，两色身份一眼能分开；
//   - 异色：脚下两圈反向旋转的金色符文环 + 上升金星 + 身上的星芒闪光 + 头顶环绕的光冠 +
//     一盏轻轻呼吸的暖金点光；带炫彩的异色再叠一层降强度的元素特效；
//   - 尺寸按崽**实际渲染包围盒**量出来（身高 / 体宽），粒子是 2–30 cm 的小亮点，不是雾；
//   - 发射全部走粒子系统自带的固定速率与 burst，没有每帧手撒。
//
// 材质口径（为什么不怕 URP Deferred 看不见）：
//   - 程序化粒子一律 `new Material(RingParticleEffect.GetSharedParticleMaterial())` 作模板，
//     着色器解析链与全 Mod 共享的那一份完全相同。2026-09-23 用 UnityPy 读正式包：
//     `Legacy Shaders/Particles/Alpha Blended` 在 resources.assets（path_id 4610）里，单 pass、
//     无 LightMode 标签 → URP 当 SRPDefaultUnlit 在透明前向阶段画，旧光环、霜雾、灶火都走它，
//     owner 截图里那团黄雾就是它画出来的（能渲染，只是配方廉价）。
//     包里**没有** `Legacy Shaders/Particles/Additive`，所以不走 PhantomWitch 的「加色」链。
//   - 亮度靠 HDR：着色器是 `2 × 顶点色 × _TintColor × 贴图`，用 SetVector 写原始值
//     （SetColor 在线性空间会先做 sRGB→线性），把 _TintColor 推到 >0.5 就是超白高光，
//     吃官方 Bloom；官方火星材质 BulletHitSpark 的 _BaseColor 也是 (24, 6, 0.4) 这种 HDR 值。
//   - 赤色的火焰直接克隆官方火 AK-47（TypeID 862）的 Smoke / Spark 粒子系统，用它们自己的
//     官方材质（SmokeFireFX / SodaSoftParticle），与龙息武器同一来源，只改尺寸、速度与数量。
//
// 生命周期（AGENTS 4.6 / 4.12）：
//   - 只有真的带炫彩或异色、且已激活入场的崽才会调 Attach（门控在 PetNestCompanionSpawner）；
//     普通崽一个对象都不建；
//   - 特效根挂在崽的角色根下：召回、换崽、远征、放生、倒地退场、切图、宿主清理都经
//     CleanupOnce 先 Dispose，再销毁角色；就算角色被别的路径直接销毁，特效也作为子节点一起走；
//   - 本组件自建的材质、贴图、网格全部登记在 _ownedAssets，OnDestroy 逐个销毁；
//     没有任何静态缓存。每只崽入场时按需画 3–7 张 32–128 px 贴图（离线 .NET Framework 实测
//     每张 0.3–2.7 ms，游戏内 Mono 未实测），只发生在入场那一帧，之后零分配；
//   - 只有异色需要 Update（符文环旋转、点光呼吸，O(1)）；纯炫彩崽组件 enabled=false，零脚本帧成本。
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Common.Effects;
using UnityEngine;

namespace BossRush
{
    /// <summary>崽身上的炫彩 / 异色特效。一只崽一个实例，挂在角色根下。</summary>
    public sealed partial class PetNestAuraEffect : MonoBehaviour
    {
        #region 预算与尺寸常量

        /// <summary>纯炫彩崽的活粒子上限（全部发射器 maxParticles 之和，不含拖尾顶点）。</summary>
        internal const int ChromaParticleBudget = 60;

        /// <summary>异色崽的活粒子上限（异色本体 + 降强度叠加的炫彩层）。</summary>
        internal const int ShinyParticleBudget = 120;

        /// <summary>异色带炫彩时，炫彩层的强度倍率（速率与上限一起乘）。</summary>
        internal const float ChromaUnderShinyIntensity = 0.6f;

        /// <summary>配方的历史标尺；所有距离乘实际模型身高 / 此标尺，随成长同步放大。</summary>
        private const float NominalPetHeight = 0.45f;
        private const float MinPetHeight = 0.25f;
        private const float MaxPetHeight = 1.2f;
        private const float MinPetRadius = 0.1f;
        private const float MaxPetRadius = 0.5f;
        private const float FallbackPetRadius = 0.18f;

        /// <summary>发射器统一「Z 朝上」：锥形 / 圆形发射默认沿 +Z，转到世界上方（同 SkyIslandHearthFx）。</summary>
        private static readonly Quaternion ZUp = Quaternion.Euler(-90f, 0f, 0f);

        #endregion

        #region 亮度档

        /// <summary>材质亮度档：Soft = 原色（暗影、寒雾、叶片），Bright = 2 倍，Hot = 3.4 倍（火星、星芒）。</summary>
        internal enum Glow
        {
            Soft = 0,
            Bright = 1,
            Hot = 2,
        }

        private const int GlowLevelCount = 3;

        private static float GlowGain(Glow level)
        {
            switch (level)
            {
                case Glow.Hot: return 3.4f;
                case Glow.Bright: return 2f;
                default: return 1f;
            }
        }

        #endregion

        #region 状态

        private readonly List<UnityEngine.Object> _ownedAssets = new List<UnityEngine.Object>(16);
        private readonly List<ParticleSystem> _systems = new List<ParticleSystem>(8);
        private readonly Texture2D[] _textures = new Texture2D[(int)PetNestAuraTexture.Count];
        private readonly Material[] _materials = new Material[(int)PetNestAuraTexture.Count * GlowLevelCount];

        private Material _template;
        private int _tintPropertyId;
        private bool _legacyTint;
        private int _particleBudgetLeft;

        /// <summary>崽的可见身高（米，脚底到头顶）。</summary>
        private float _height = NominalPetHeight;
        /// <summary>崽的水平半宽（米）。</summary>
        private float _radius = FallbackPetRadius;
        /// <summary>尺寸单位：身高 / 标称身高。所有配方里的米数都乘它。</summary>
        private float _unit = 1f;

        // 异色专属的逐帧表现（只有异色会非空）
        private Transform _haloOuter;
        private Transform _haloInner;
        private MeshRenderer _haloOuterRenderer;
        private MeshRenderer _haloInnerRenderer;
        private MaterialPropertyBlock _haloBlock;
        private Vector4 _haloOuterTint;
        private Vector4 _haloInnerTint;
        private Light _light;
        private float _phase;
        private bool _disposed;
        /// <summary>退场淡出进度（1 → 0），只在 Dispose 之后由 Update 推进。</summary>
        private float _releaseFade;

        /// <summary>本实例实际分配出去的粒子上限之和（诊断 / 验收用）。</summary>
        internal int AllocatedParticles { get; private set; }

        /// <summary>本实例创建的粒子系统数（诊断 / 验收用）。</summary>
        internal int SystemCount { get { return _systems.Count; } }

        #endregion

        #region 入口

        /// <summary>
        /// 给一只已激活的崽挂特效。shiny=false 且两色任一缺失时返回 null、不建任何对象。
        /// 失败一律返回 null 并清掉半成品——特效是锦上添花，绝不能让崽入场失败。
        /// </summary>
        internal static PetNestAuraEffect Attach(CharacterMainControl character, bool shiny,
            PetNestChromaColor colorA, PetNestChromaColor colorB)
        {
            if (character == null) return null;
            bool chroma = colorA != null && colorB != null;
            if (!shiny && !chroma) return null;

            Material template = RingParticleEffect.GetSharedParticleMaterial();
            if (template == null)
            {
                ModBehaviour.DevLog("[PetNest] 崽特效跳过：共享粒子材质不可用");
                return null;
            }

            GameObject root = null;
            PetNestAuraEffect fx = null;
            try
            {
                root = new GameObject("PetNestAura");
                // 先失活再搭：新加的 ParticleSystem 默认 playOnAwake，挂在活跃对象上会先按默认参数
                // （白色大颗粒）喷一下；全部配好再一次性激活（同 NewWeaponSwingFx 的做法）。
                root.SetActive(false);
                root.transform.SetParent(character.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                fx = root.AddComponent<PetNestAuraEffect>();
                fx.Build(character, template, shiny, chroma ? colorA : null, chroma ? colorB : null);
                root.SetActive(true);
                fx.PlayAll();
                return fx;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 崽特效创建失败: " + e.Message);
                // 根还没激活过时 Unity 不会调 OnDestroy：已画好的贴图、材质、网格要手动回收（幂等）
                if (fx != null) fx.OnDestroy();
                if (root != null) UnityEngine.Object.Destroy(root);
                return null;
            }
        }

        /// <summary>
        /// 回收。幂等。召回、倒地、换崽时不再一帧消失：先脱离角色留在原地，停发射、灯淡出、
        /// 符文环 0.3 秒淡掉，最后一颗粒子走完（至多 1.5 秒）再销毁。看不见的时候直接销毁。
        /// </summary>
        internal void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!gameObject.activeInHierarchy)
            {
                enabled = false;
                Destroy(gameObject);
                return;
            }
            _releaseFade = 1f;
            enabled = _haloOuterRenderer != null || _haloInnerRenderer != null;
            BossRushFxKit.Release(gameObject, 0.3f, 1.5f, true);
        }

        #endregion

        #region 搭建

        private void Build(CharacterMainControl character, Material template, bool shiny,
            PetNestChromaColor colorA, PetNestChromaColor colorB)
        {
            _template = template;
            _legacyTint = template.HasProperty("_TintColor");
            _tintPropertyId = Shader.PropertyToID(_legacyTint ? "_TintColor" : "_Color");
            _particleBudgetLeft = shiny ? ShinyParticleBudget : ChromaParticleBudget;

            float groundOffset;
            MeasurePet(character, out _height, out _radius, out groundOffset);
            _unit = Mathf.Clamp(_height / NominalPetHeight, 0.6f, 2.2f);
            // 特效根落在模型脚底：角色根不一定在脚底，按包围盒底边对齐
            transform.localPosition = new Vector3(0f, groundOffset, 0f);

            if (shiny)
            {
                BuildShiny();
            }
            if (colorA != null && colorB != null)
            {
                float intensity = shiny ? ChromaUnderShinyIntensity : 1f;
                BuildChromaPrimary(ResolveElement(colorA.Id), colorA, intensity);
                BuildChromaAccent(ResolveElement(colorB.Id), colorB, intensity);
            }

            // 只有异色的符文环与点光需要逐帧驱动；纯炫彩崽不跑 Update
            enabled = _haloOuter != null || _haloInner != null || _light != null;
        }

        private void PlayAll()
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps != null && !ps.isPlaying) ps.Play(false);
            }
        }

        /// <summary>
        /// 量崽的可见身高、半宽和脚底偏移。只在入场这一帧量一次。
        /// 包围盒异常（皮肤网格尚未更新、全是空渲染器）时回落到标称尺寸。
        /// </summary>
        private static void MeasurePet(CharacterMainControl character, out float height,
            out float radius, out float groundOffset)
        {
            height = NominalPetHeight;
            radius = FallbackPetRadius;
            groundOffset = 0f;
            try
            {
                Transform model = character.modelRoot;
                if (model == null) return;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(false);
                bool any = false;
                Bounds bounds = new Bounds();
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || !r.enabled) continue;
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!any)
                    {
                        bounds = r.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }
                if (!any || bounds.size.y < 0.05f) return;

                Vector3 rootPosition = character.transform.position;
                height = Mathf.Clamp(bounds.size.y, MinPetHeight, MaxPetHeight);
                // 半宽再按身高封顶：手里的枪、贴地的影子片这类宽渲染器会把包围盒撑宽，
                // 不封顶的话特效会离崽一大圈
                float halfWidth = Mathf.Min(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f, 0.55f * height);
                radius = Mathf.Clamp(halfWidth, MinPetRadius, MaxPetRadius);

                // 脚底偏移只信「包围盒确实在角色身边」的那种：崽是先在 -240 m 的 staging 点建好、
                // 再挪到落点激活的，皮肤网格的包围盒若还停在 staging 点，钳制会把 -240 m 静默
                // 变成 -0.6 m，特效整套埋进地里。离得不对就按角色根即脚底处理。
                float rawOffset = bounds.min.y - rootPosition.y;
                Vector2 horizontal = new Vector2(bounds.center.x - rootPosition.x, bounds.center.z - rootPosition.z);
                bool plausible = rawOffset > -0.6f && rawOffset < 0.3f && horizontal.sqrMagnitude < 4f;
                groundOffset = plausible ? Mathf.Clamp(rawOffset, -0.6f, 0.2f) : 0f;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 崽特效量尺寸失败，按标称尺寸: " + e.Message);
            }
        }

        #endregion

        #region 资源（全部登记，OnDestroy 销毁）

        private T Own<T>(T asset) where T : UnityEngine.Object
        {
            if (asset != null) _ownedAssets.Add(asset);
            return asset;
        }

        private Texture2D GetTexture(PetNestAuraTexture kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= _textures.Length) return null;
            if (_textures[index] != null) return _textures[index];

            int size = PetNestAuraTextures.SizeOf(kind);
            Texture2D texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, true));
            texture.name = "PetNestAura_" + kind;
            texture.wrapMode = TextureWrapMode.Clamp;
            // 屏上只有几到几十个像素：必须有 mip，否则细线在缩小时闪烁
            texture.filterMode = kind == PetNestAuraTexture.RuneOuter || kind == PetNestAuraTexture.RuneInner
                ? FilterMode.Trilinear
                : FilterMode.Bilinear;
            texture.SetPixels32(PetNestAuraTextures.Paint(kind, size));
            texture.Apply(true, true);
            _textures[index] = texture;
            return texture;
        }

        /// <summary>按「贴图 × 亮度档」取材质，同一实例内复用。</summary>
        private Material GetMaterial(PetNestAuraTexture kind, Glow level)
        {
            int index = (int)kind * GlowLevelCount + (int)level;
            if (index < 0 || index >= _materials.Length) return null;
            if (_materials[index] != null) return _materials[index];

            Texture2D texture = GetTexture(kind);
            if (texture == null || _template == null) return null;
            Material material = Own(new Material(_template));
            material.name = "PetNestAura_" + kind + "_" + level;
            material.mainTexture = texture;
            material.SetVector(_tintPropertyId, TintVector(Color.white, GlowGain(level)));
            _materials[index] = material;
            return material;
        }

        /// <summary>
        /// 把「颜色 × 亮度倍数」换成着色器吃的原始 tint。legacy 粒子着色器自带 ×2 且 alpha 也 ×2，
        /// 所以写 (c·g/2, 0.5)；回落到 UI/Default、Sprites/Default 时没有 ×2，写 (c·g, 1)。
        /// </summary>
        private Vector4 TintVector(Color linearColor, float gain)
        {
            if (_legacyTint)
            {
                float half = gain * 0.5f;
                return new Vector4(linearColor.r * half, linearColor.g * half, linearColor.b * half, 0.5f);
            }
            return new Vector4(linearColor.r * gain, linearColor.g * gain, linearColor.b * gain, 1f);
        }

        /// <summary>从剩余预算里扣一个发射器的上限。预算用完返回 0，调用方不建该发射器。</summary>
        private int TakeBudget(int requested)
        {
            int granted = Mathf.Min(Mathf.Max(0, requested), _particleBudgetLeft);
            _particleBudgetLeft -= granted;
            AllocatedParticles += granted;
            return granted;
        }

        #endregion

        #region 发射器工具

        /// <summary>
        /// 新建一个程序化发射器（Z 朝上），把所有默认参数改成安全值：
        /// 循环、不重力、零速度、单色、固定速率 0，由配方逐项覆盖。
        /// 上限经 TakeBudget 扣预算；预算不足返回 null。
        /// </summary>
        private ParticleSystem NewEmitter(string name, PetNestAuraTexture kind, Glow level,
            int maxParticles, Vector3 localPosition, bool worldSpace)
        {
            Material material = GetMaterial(kind, level);
            if (material == null) return null;
            int cap = TakeBudget(maxParticles);
            if (cap <= 0) return null;

            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = ZUp;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 3f;
            main.prewarm = false;
            main.maxParticles = cap;
            main.simulationSpace = worldSpace
                ? ParticleSystemSimulationSpace.World
                : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startDelay = 0f;
            main.startSpeed = 0f;
            main.startRotation = 0f;
            main.gravityModifier = 0f;
            main.startColor = Color.white;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;
            shape.radiusThickness = 1f;
            shape.randomDirectionAmount = 0f;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            _systems.Add(ps);
            return ps;
        }

        private static void Life(ParticleSystem ps, float min, float max)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void Size(ParticleSystem ps, float min, float max)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void Speed(ParticleSystem ps, float min, float max)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void Gravity(ParticleSystem ps, float modifier)
        {
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = modifier;
        }

        /// <summary>出生颜色在两色之间随机（alpha 取两色各自的 alpha）。</summary>
        private static void Tint(ParticleSystem ps, Color a, Color b)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        }

        /// <summary>出生角度随机（公告板绕视线轴）。</summary>
        private static void RandomSpin(ParticleSystem ps, float maxDegreesPerSecond)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            if (maxDegreesPerSecond <= 0f) return;
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            float radians = maxDegreesPerSecond * Mathf.Deg2Rad;
            rotation.z = new ParticleSystem.MinMaxCurve(-radians, radians);
        }

        private static void Rate(ParticleSystem ps, float perSecond)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, perSecond);
        }

        /// <summary>周期性 burst：每 interval 秒以 probability 的概率喷 min–max 颗（循环内无限次）。</summary>
        private static void Bursts(ParticleSystem ps, float interval, short min, short max, float probability)
        {
            ParticleSystem.Burst burst = new ParticleSystem.Burst(0.05f, min, max, 0, interval);
            burst.probability = Mathf.Clamp01(probability);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { burst });
        }

        /// <summary>预热：一出场就是「已经烧了一阵」的样子，而不是从零长出来。</summary>
        private static void Prewarm(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.prewarm = true;
        }

        private static void ShapeSphere(ParticleSystem ps, float radius, float thickness)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.001f, radius);
            shape.radiusThickness = Mathf.Clamp01(thickness);
        }

        /// <summary>水平圆（Z 朝上的发射器里，圆面落在世界水平面上）。</summary>
        private static void ShapeCircle(ParticleSystem ps, float radius, float thickness)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.001f, radius);
            shape.radiusThickness = Mathf.Clamp01(thickness);
            shape.arc = 360f;
        }

        /// <summary>朝上的锥。</summary>
        private static void ShapeCone(ParticleSystem ps, float angle, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = Mathf.Max(0.001f, radius);
            shape.radiusThickness = 1f;
            shape.arc = 360f;
        }

        /// <summary>
        /// 生命期速度（发射器本地空间，Z = 世界上方）。rise 是竖直速度区间；
        /// orbit 是绕竖轴的角速度（弧度/秒，正值逆时针俯视）。三轴线速度与三轴轨道各自保持同一曲线模式，
        /// 否则 Unity 会报「curves must all be in the same mode」。
        /// </summary>
        private static void Velocity(ParticleSystem ps, float riseMin, float riseMax, float orbit, float radial)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(orbit);
            velocity.radial = new ParticleSystem.MinMaxCurve(radial);
        }

        private static void Noise(ParticleSystem ps, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(strength);
            noise.frequency = frequency;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.25f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        /// <summary>生命期颜色：三段颜色（出生 / 中段 / 消散）+ 淡入淡出。</summary>
        private static void Fade(ParticleSystem ps, Color start, Color middle, Color end,
            float fadeIn, float holdUntil)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(start, 0f),
                    new GradientColorKey(middle, 0.45f),
                    new GradientColorKey(end, 1f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp(fadeIn, 0.01f, 0.5f)),
                    new GradientAlphaKey(1f, Mathf.Clamp(holdUntil, 0.5f, 0.98f)),
                    new GradientAlphaKey(0f, 1f),
                });
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>只淡入淡出、不改色。</summary>
        private static void Fade(ParticleSystem ps, float fadeIn, float holdUntil)
        {
            Fade(ps, Color.white, Color.white, Color.white, fadeIn, holdUntil);
        }

        /// <summary>生命期尺寸：线性从 from 到 to。</summary>
        private static void Grow(ParticleSystem ps, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        /// <summary>闪烁：尺寸在生命期里亮—暗—亮—灭两次，星光才有「一闪一闪」的节奏。</summary>
        private static void Twinkle(ParticleSystem ps)
        {
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.15f, 1f),
                new Keyframe(0.4f, 0.45f),
                new Keyframe(0.62f, 0.95f),
                new Keyframe(1f, 0f));
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>一闪即逝：快速放大到峰值再收掉（星芒闪光、电弧）。</summary>
        private static void Pop(ParticleSystem ps)
        {
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0f));
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>拉伸公告板：速度越快拖得越长（火星、雨滴、电火花）。</summary>
        private static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = lengthScale;
            renderer.cameraVelocityScale = 0f;
        }

        /// <summary>水平公告板（贴地的涟漪）。</summary>
        private static void FlatOnGround(ParticleSystem ps)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        /// <summary>
        /// 粒子拖尾（环绕类用：几颗点拖出一段弧，读起来就是「一圈在转」）。
        /// lifetimeFraction 是粒子寿命的比例（Unity 的口径）。拖尾用专门的条带贴图：
        /// 圆点贴图按 Stretch 贴到拖尾上，靠近粒子的那一端 alpha 是 0，看起来像一截脱节的光棍。
        /// </summary>
        private void Trails(ParticleSystem ps, float lifetimeFraction, float minVertexDistance)
        {
            Material trailMaterial = GetMaterial(PetNestAuraTexture.TrailStrip, Glow.Bright);
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (trailMaterial == null || renderer == null) return;
            renderer.trailMaterial = trailMaterial;

            ParticleSystem.TrailModule trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(Mathf.Clamp01(lifetimeFraction));
            trails.minVertexDistance = Mathf.Max(0.005f, minVertexDistance);
            trails.worldSpace = false;
            trails.dieWithParticles = true;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 0f));
            Gradient fade = new Gradient();
            fade.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(fade);
        }

        /// <summary>按强度缩放速率与上限（异色叠加炫彩时 intensity &lt; 1）。</summary>
        private static int Scaled(int count, float intensity)
        {
            return Mathf.Max(1, Mathf.CeilToInt(count * intensity));
        }

        #endregion

        #region 逐帧（仅异色）

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // 暂停时整套表现一起停
            if (_disposed)
            {
                // 退场：符文环 0.3 秒淡掉（灯由 BossRushFxKit.Release 挂的淡出组件负责）
                _releaseFade = Mathf.MoveTowards(_releaseFade, 0f, dt / 0.3f);
                if (_haloBlock != null)
                {
                    if (_haloOuterRenderer != null)
                    {
                        _haloBlock.SetVector(_tintPropertyId, Scale(_haloOuterTint, _releaseFade));
                        _haloOuterRenderer.SetPropertyBlock(_haloBlock);
                    }
                    if (_haloInnerRenderer != null)
                    {
                        _haloBlock.SetVector(_tintPropertyId, Scale(_haloInnerTint, _releaseFade));
                        _haloInnerRenderer.SetPropertyBlock(_haloBlock);
                    }
                }
                if (_releaseFade <= 0f) enabled = false;
                return;
            }
            _phase += dt;

            if (_haloOuter != null) _haloOuter.Rotate(0f, ShinyHaloOuterDegreesPerSecond * dt, 0f, Space.Self);
            if (_haloInner != null) _haloInner.Rotate(0f, -ShinyHaloInnerDegreesPerSecond * dt, 0f, Space.Self);

            // 呼吸：约 2.6 秒一次，幅度很小——是「在发光」，不是「在闪」
            float pulse = 0.5f + 0.5f * Mathf.Sin(_phase * ShinyPulseSpeed);
            if (_light != null)
            {
                _light.intensity = Mathf.Lerp(ShinyLightIntensityMin, ShinyLightIntensityMax, pulse);
            }
            if (_haloBlock != null)
            {
                float gain = Mathf.Lerp(0.82f, 1.12f, pulse);
                if (_haloOuterRenderer != null)
                {
                    _haloBlock.SetVector(_tintPropertyId, Scale(_haloOuterTint, gain));
                    _haloOuterRenderer.SetPropertyBlock(_haloBlock);
                }
                if (_haloInnerRenderer != null)
                {
                    _haloBlock.SetVector(_tintPropertyId, Scale(_haloInnerTint, 2.06f - gain));
                    _haloInnerRenderer.SetPropertyBlock(_haloBlock);
                }
            }
        }

        private static Vector4 Scale(Vector4 tint, float gain)
        {
            return new Vector4(tint.x * gain, tint.y * gain, tint.z * gain, tint.w);
        }

        #endregion

        #region 销毁

        private void OnDestroy()
        {
            _disposed = true;
            for (int i = 0; i < _ownedAssets.Count; i++)
            {
                UnityEngine.Object asset = _ownedAssets[i];
                if (asset == null) continue;
                try
                {
                    Destroy(asset);
                }
                catch (Exception)
                {
                    // 单个资源销毁失败不影响其余资源回收
                }
            }
            _ownedAssets.Clear();
            _systems.Clear();
            Array.Clear(_textures, 0, _textures.Length);
            Array.Clear(_materials, 0, _materials.Length);
            _template = null;
            _haloOuter = null;
            _haloInner = null;
            _haloOuterRenderer = null;
            _haloInnerRenderer = null;
            _haloBlock = null;
            _light = null;
        }

        #endregion
    }
}

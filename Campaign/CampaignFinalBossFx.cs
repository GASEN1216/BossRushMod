// ============================================================================
// CampaignFinalBossFx.cs - 终章「冠军之影」的表现层：召唤石、召唤爆发、影的染色与烟缕 / 符文环
// ============================================================================
// 为什么有这个文件（2026-09-23 特效审美审查 VA-25 / VA-26）：
//   - 召唤石原来是一个方块、一根方柱、一个固定在柱顶的红球，URP/Lit 哑光打光、不浮不发光，
//     开战那一帧直接 Destroy；全剧最后一战的入口长得像白模，Boss 出场也没有任何召唤表现。
//   - 冠军之影的「绯红染色」对官方 SodaCharacter 写的是 _BaseColor，多半无效，终章 Boss 和普通幽灵女巫
//     几乎长得一样；反过来直接把 (0.85,0.15,0.2) 写进 _Tint 又会把整张贴图压成一片暗红。
//   CampaignFinalBoss.cs 是 ModBehaviour 的 partial（宿主行数预算已顶格），表现逻辑收在这里，宿主各留一行调用。
//
// 口径：
//   - 召唤石的几何仍是程序化方块（没有找到可靠的官方祭坛道具预制体可以离线确认），材质换成官方画风的
//     SodaCraft/SodaCharacter（CampaignAssetCache.GetAltarMaterial），颜色走 _Tint 属性块；
//     核心是一颗立在顶点上的晶体，浮动自转、自发光（_EmissionColor，官方着色器实机枚举有这个字段）
//     + 加色光晕 + 一盏呼吸点光 + 上升余烬。
//   - 开战（或召唤石因任何原因不该在场）时 Dismiss：0.5 秒 EaseOut 缩没、灯淡出、粒子停发自然死完，
//     不阻塞也不改 Boss 生成时序；召唤爆发与 Boss 生成并行。
//   - 影的染色只乘在 SodaCharacter 的 _Tint 上，跳过捏脸部件（官方 CustomFacePart 的 customColorKey 也是 _Tint）
//     与女巫自己的特效渲染器；烟缕与符文环跟随 Boss，真隐身（官方关掉全部渲染器）时一起藏起来，
//     不替玩家暴露隐身中的 Boss；Boss 死亡或被销毁后用 BossRushFxKit.Release 自然退场。
//   - 只改表现，不改伤害、判定范围与时序。逐帧成本只在召唤石在场 / 终章 Boss 在场期间存在（AGENTS 4.12）。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>终章表现入口。全部 no-throw：表现失败只记 DevLog，不影响开战与战斗。</summary>
    internal static class CampaignFinalBossFx
    {
        #region 召唤石

        private static readonly Color AltarBaseColor = new Color(0.10f, 0.10f, 0.13f, 1f);
        private static readonly Color AltarPillarColor = new Color(0.16f, 0.15f, 0.19f, 1f);
        private static readonly Color AltarCapColor = new Color(0.24f, 0.12f, 0.14f, 1f);
        private static readonly Color EmberColor = new Color(1f, 0.45f, 0.32f, 0.9f);
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>核心悬浮的中心高度：晶体最低点（含浮动幅度）也在柱顶盖板之上。</summary>
        internal const float CoreHeight = 1.45f;
        private const float CoreSize = 0.26f;
        private const float CoreEmissionGain = 1.6f;
        private const float CoreLightRange = 2.5f;
        private const float CoreLightIntensity = 1.2f;
        private const float CoreLightBreathe = 0.15f;
        private const int EmberMax = 12;
        private const int HaloMax = 4;

        /// <summary>
        /// 在 altar 根物体下搭召唤石：基座、方柱、盖板、悬浮晶体核心、光晕、余烬、点光，并挂浮动组件。
        /// 碰撞体与交互组件由宿主照旧挂在根上。
        /// </summary>
        internal static void Build(GameObject altar)
        {
            if (altar == null) return;
            Material material = CampaignAssetCache.GetAltarMaterial();
            Transform root = altar.transform;
            CreatePart(root, PrimitiveType.Cube, "Base", new Vector3(1.0f, 0.25f, 1.0f),
                new Vector3(0f, 0.12f, 0f), Quaternion.identity, AltarBaseColor, material);
            CreatePart(root, PrimitiveType.Cube, "Pillar", new Vector3(0.34f, 0.85f, 0.34f),
                new Vector3(0f, 0.60f, 0f), Quaternion.identity, AltarPillarColor, material);
            CreatePart(root, PrimitiveType.Cube, "Cap", new Vector3(0.5f, 0.08f, 0.5f),
                new Vector3(0f, 1.065f, 0f), Quaternion.identity, AltarCapColor, material);

            GameObject pivot = new GameObject("CorePivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, CoreHeight, 0f);
            // 立在顶点上的晶体：转起来看得出在转（球体自转是看不见的）
            Renderer core = CreatePart(pivot.transform, PrimitiveType.Cube, "Core", Vector3.one * CoreSize,
                Vector3.zero, Quaternion.Euler(45f, 0f, 35.26f), CampaignTuning.FinalBossTint, material);
            SetEmission(core, CampaignTuning.FinalBossTint, CoreEmissionGain);

            try
            {
                GameObject lightObject = new GameObject("CoreLight");
                lightObject.transform.SetParent(pivot.transform, false);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = CoreLightRange;
                light.color = CampaignTuning.FinalBossTint;
                light.shadows = LightShadows.None;
                BossRushFxLightFade.Attach(light, CoreLightIntensity, 0.6f, CoreLightBreathe, 2.4f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 召唤石灯光失败: " + e.Message);
            }

            ConfigureHalo(BossRushFxKit.CreateEmitter("CoreHalo", root, new Vector3(0f, CoreHeight, 0f),
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright),
                HaloMax, true));
            ConfigureEmbers(BossRushFxKit.CreateEmitter("CoreEmbers", root, new Vector3(0f, CoreHeight - 0.15f, 0f),
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot),
                EmberMax, true));

            CampaignFinalBossAltarMotion motion = altar.AddComponent<CampaignFinalBossAltarMotion>();
            motion.Init(pivot.transform);
        }

        /// <summary>
        /// 召唤石退场：关掉碰撞（交互提示随之消失），0.5 秒 EaseOut 缩没，灯淡出，粒子停发后自然死完再销毁。
        /// 调用方随即放弃引用；没有浮动组件（构建失败的半成品）时直接销毁。
        /// </summary>
        internal static void Dismiss(GameObject altar)
        {
            if (altar == null) return;
            try
            {
                Collider[] colliders = altar.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null) colliders[i].enabled = false;
                }
                CampaignFinalBossAltarMotion motion = altar.GetComponent<CampaignFinalBossAltarMotion>();
                if (motion == null)
                {
                    UnityEngine.Object.Destroy(altar);
                    return;
                }
                motion.BeginDismiss();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 召唤石退场失败，直接销毁: " + e.Message);
                UnityEngine.Object.Destroy(altar);
            }
        }

        private static Renderer CreatePart(Transform parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 localPos, Quaternion localRotation, Color color, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localRotation = localRotation;
            part.transform.localScale = scale;

            // 装饰件的碰撞体会跟交互 trigger 打架，必须删掉
            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null) UnityEngine.Object.Destroy(partCollider);

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
                CampaignAssetCache.SetRendererColor(renderer, color);
            }
            return renderer;
        }

        /// <summary>核心自发光：属性块写 HDR 原始值（SetVector，免得线性空间把倍数再转一次）。</summary>
        private static void SetEmission(Renderer renderer, Color color, float gain)
        {
            if (renderer == null || renderer.sharedMaterial == null
                || !renderer.sharedMaterial.HasProperty(EmissionColorId)) return;
            try
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Color linear = color.linear;
                block.SetVector(EmissionColorId, new Vector4(linear.r * gain, linear.g * gain, linear.b * gain, 1f));
                renderer.SetPropertyBlock(block);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 召唤石核心自发光失败: " + e.Message);
            }
        }

        /// <summary>核心光晕：两三颗大号加色光点交替淡入淡出，像一颗在呼吸的红宝石。</summary>
        private static void ConfigureHalo(ParticleSystem ps)
        {
            if (ps == null) return;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 1.8f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 0.85f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 1.2f;
            Color tint = CampaignTuning.FinalBossTint;
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(
                new Color(1f, 0.55f, 0.55f, 0.45f),
                new Color(tint.r, tint.g, tint.b, 0.45f),
                new Color(tint.r * 0.6f, tint.g * 0.6f, tint.b * 0.6f, 0f),
                0.35f);
            ps.Play();
        }

        /// <summary>上升余烬：约 9 颗 3–5 cm 的亮点从核心附近缓缓升起、变暗消失（World 空间，不跟着核心晃）。</summary>
        private static void ConfigureEmbers(ParticleSystem ps)
        {
            if (ps == null) return;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.gravityModifier = -0.06f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 6f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.radius = 0.3f;
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(
                new Color(1f, 0.85f, 0.75f, 1f),
                EmberColor,
                new Color(0.45f, 0.05f, 0.06f, 0f),
                0.15f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));
            ps.Play();
        }

        #endregion

        #region 召唤爆发

        private static readonly Color SummonRingColor = new Color(0.85f, 0.18f, 0.22f, 0.9f);
        private static readonly Color SummonSmokeColor = new Color(0.30f, 0.05f, 0.08f, 0.5f);

        /// <summary>
        /// Boss 出生点的召唤表现：一道半径 3 m 的绯红环带灯爆开 + 贴地卷开的暗红烟。
        /// 与 Boss 生成并行，不等待、不改时序。
        /// </summary>
        internal static void PlaySummonBurst(Vector3 position)
        {
            try
            {
                NewWeaponFx.PlayBurst(position + Vector3.up * 0.1f, SummonRingColor, 3f, 0.8f, 12, true);
                BossRushFxBurst smoke = BossRushFxKit.Dust(SummonSmokeColor, 14);
                smoke.ShapeRadius = 1.2f;
                smoke.SpeedMin = 1.5f;
                smoke.SpeedMax = 2.5f;
                smoke.LifeMin = 1.0f;
                smoke.LifeMax = 1.4f;
                BossRushFxKit.PlayBurst(position + Vector3.up * 0.15f, smoke);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 召唤爆发失败: " + e.Message);
            }
        }

        #endregion

        #region 冠军之影

        private static readonly int SodaTintId = Shader.PropertyToID("_Tint");
        private static readonly MaterialPropertyBlock ShadowTintBlock = new MaterialPropertyBlock();

        /// <summary>给终章 Boss 套上「影」的外观：角色染色 + 跟随的暗红烟缕与脚下符文环。各自失败互不影响。</summary>
        internal static void ApplyShadowLook(CharacterMainControl boss)
        {
            if (boss == null) return;
            Renderer probe = null;
            try
            {
                probe = TintShadow(boss, CampaignTuning.FinalBossShadowTint);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战染色失败: " + e.Message);
            }
            try
            {
                CampaignFinalBossShadowAura.Attach(boss, probe);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战影环失败: " + e.Message);
            }
        }

        /// <summary>
        /// 只染角色网格：SodaCharacter 的 _Tint 在原值上乘一层冷红（保住原本的色相与明暗），
        /// 其它着色器照旧写颜色属性块。跳过粒子 / 拖尾 / 线 / 精灵（女巫自己的特效）与捏脸部件。
        /// 返回一个在场的角色网格，给影环判断「官方真隐身关掉了全部渲染器」用。
        /// </summary>
        private static Renderer TintShadow(CharacterMainControl boss, Color tint)
        {
            Renderer probe = FindBodyRenderer(boss);
            Renderer anyMesh = null;
            Color linearTint = tint.linear;
            Renderer[] renderers = boss.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer
                    || renderer is LineRenderer || renderer is SpriteRenderer) continue;
                if (renderer.GetComponentInParent<CustomFacePart>(true) != null) continue;
                Material material = renderer.sharedMaterial;
                if (material == null) continue;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    if (probe == null && renderer is SkinnedMeshRenderer) probe = renderer;
                    if (anyMesh == null) anyMesh = renderer;
                }

                if (!material.HasProperty(SodaTintId))
                {
                    CampaignAssetCache.SetRendererColor(renderer, tint);
                    continue;
                }
                // 捏脸主体（CustomFaceInstance.mainRenderers）的 _Tint 已写在属性块里：在它上面乘，不覆盖
                renderer.GetPropertyBlock(ShadowTintBlock);
                Vector4 current = ShadowTintBlock.HasVector(SodaTintId)
                    ? ShadowTintBlock.GetVector(SodaTintId)
                    : material.GetVector(SodaTintId);
                ShadowTintBlock.SetVector(SodaTintId, new Vector4(
                    current.x * linearTint.r, current.y * linearTint.g, current.z * linearTint.b, current.w));
                renderer.SetPropertyBlock(ShadowTintBlock);
            }
            return probe != null ? probe : anyMesh;
        }

        /// <summary>
        /// 找一块「身体」渲染器当隐身探针：优先捏脸主体（CustomFaceInstance.mainRenderers），
        /// 找不到时 TintShadow 退到第一块在场的蒙皮网格，再退到任意在场网格（武器会随换手开关，尽量不用它判断隐身）。
        /// </summary>
        private static Renderer FindBodyRenderer(CharacterMainControl boss)
        {
            CustomFaceInstance face = boss.GetComponentInChildren<CustomFaceInstance>(true);
            if (face == null || face.mainRenderers == null) return null;
            for (int i = 0; i < face.mainRenderers.Length; i++)
            {
                Renderer renderer = face.mainRenderers[i];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) return renderer;
            }
            return null;
        }

        #endregion
    }

    /// <summary>召唤石的浮动 / 自转与退场缩没。只在召唤石在场期间存在（按 deltaTime，暂停时一起停）。</summary>
    internal sealed class CampaignFinalBossAltarMotion : MonoBehaviour
    {
        private const float FloatAmplitude = 0.08f;
        private const float FloatSpeed = 1.6f;
        private const float SpinDegreesPerSecond = 40f;
        private const float DismissSeconds = 0.5f;
        private const float ReleaseWaitSeconds = 2.5f;

        private Transform _core;
        private Vector3 _baseScale = Vector3.one;
        private float _time;
        private float _dismiss = -1f;
        private bool _released;

        internal void Init(Transform core)
        {
            _core = core;
            _baseScale = transform.localScale;
        }

        internal void BeginDismiss()
        {
            if (_dismiss >= 0f) return;
            _dismiss = 0f;
            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(false);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null) systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            Light[] lights = GetComponentsInChildren<Light>(false);
            for (int i = 0; i < lights.Length; i++)
            {
                BossRushFxLightFade fade = lights[i] != null ? lights[i].GetComponent<BossRushFxLightFade>() : null;
                if (fade != null) fade.FadeOut(DismissSeconds, false);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _time += dt;
            if (_core != null)
            {
                _core.localPosition = new Vector3(0f,
                    CampaignFinalBossFx.CoreHeight + FloatAmplitude * Mathf.Sin(_time * FloatSpeed), 0f);
                _core.localRotation = Quaternion.Euler(0f, _time * SpinDegreesPerSecond, 0f);
            }
            if (_dismiss < 0f || _released) return;

            _dismiss += dt;
            float shrink = 1f - BossRushUI.EaseOut(_dismiss / DismissSeconds);
            transform.localScale = _baseScale * Mathf.Max(0.001f, shrink);
            if (_dismiss >= DismissSeconds)
            {
                // 缩没之后交给共享退场：等最后一颗余烬死完再销毁整棵
                _released = true;
                BossRushFxKit.Release(gameObject, 0.05f, ReleaseWaitSeconds, false);
            }
        }
    }

    /// <summary>
    /// 冠军之影的跟随特效：身上升起的暗红烟缕（World 空间，移动时拖在身后）+ 脚下缓慢旋转的红色符文环。
    /// 不挂在 Boss 下（Boss 被销毁时不会被一帧带走），每帧只跟随位置；Boss 死亡 / 被销毁就停发、自然退场。
    /// 官方真隐身（关掉全部渲染器）时一起藏起来，不替玩家暴露隐身中的 Boss。
    /// </summary>
    internal sealed class CampaignFinalBossShadowAura : MonoBehaviour
    {
        private const int WispMax = 16;
        private const float WispRate = 6f;
        private const int RuneMax = 3;
        private const float RuneLifetime = 4f;
        private const float RuneInterval = 3.2f;
        private const float RuneSize = 2.3f;
        private const float RuneSpinDegreesPerSecond = 20f;
        private const float ReleaseWaitSeconds = 4.5f;

        private CharacterMainControl _boss;
        private Health _health;
        private Renderer _probe;
        private ParticleSystem _wisps;
        private ParticleSystem _rune;
        private bool _hidden;
        private bool _released;

        internal static void Attach(CharacterMainControl boss, Renderer probe)
        {
            if (boss == null) return;
            Material wispMaterial = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            Material runeMaterial = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (wispMaterial == null && runeMaterial == null) return;

            GameObject root = new GameObject("BossRushCampaignShadowAura");
            root.transform.position = boss.transform.position;
            CampaignFinalBossShadowAura aura = root.AddComponent<CampaignFinalBossShadowAura>();
            aura._boss = boss;
            aura._health = boss.Health;
            aura._probe = probe;
            aura._wisps = ConfigureWisps(BossRushFxKit.CreateEmitter("Wisps", root.transform, new Vector3(0f, 0.7f, 0f),
                wispMaterial, WispMax, true));
            aura._rune = ConfigureRune(BossRushFxKit.CreateEmitter("RuneRing", root.transform, new Vector3(0f, 0.06f, 0f),
                runeMaterial, RuneMax, false));
        }

        private static ParticleSystem ConfigureWisps(ParticleSystem ps)
        {
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.03f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = WispRate;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.radius = 0.35f;
            Color tint = CampaignTuning.FinalBossTint;
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(
                new Color(tint.r * 0.55f, tint.g * 0.4f, tint.b * 0.45f, 0.35f),
                new Color(tint.r * 0.38f, tint.g * 0.25f, tint.b * 0.35f, 0.35f),
                new Color(tint.r * 0.2f, tint.g * 0.1f, tint.b * 0.2f, 0f),
                0.25f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 1.6f));
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-30f * Mathf.Deg2Rad, 30f * Mathf.Deg2Rad);
            ps.Play();
            return ps;
        }

        /// <summary>
        /// 符文环：每 3.2 秒发一片 4 秒寿命的水平符文，前后两片交叉淡入淡出，看起来是一枚持续、缓慢自转的环。
        /// Local 空间，跟着 Boss 走。
        /// </summary>
        private static ParticleSystem ConfigureRune(ParticleSystem ps)
        {
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.duration = RuneInterval;
            main.startLifetime = RuneLifetime;
            main.startSize = RuneSize;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = RuneSpinDegreesPerSecond * Mathf.Deg2Rad;
            Color tint = CampaignTuning.FinalBossTint;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f),
                    new GradientAlphaKey(0.55f, 0.8f), new GradientAlphaKey(0f, 1f),
                });
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps.Play();
            return ps;
        }

        private void LateUpdate()
        {
            if (_released) return;
            if (_boss == null || _health == null || _health.IsDead)
            {
                _released = true;
                SetHidden(false);
                BossRushFxKit.Release(gameObject, 0.3f, ReleaseWaitSeconds, false);
                return;
            }
            transform.position = _boss.transform.position;
            // 官方真隐身会关掉 Boss 的全部渲染器：影环跟着藏，出隐身再亮（与官方同为瞬切）
            SetHidden(_probe != null && !_probe.enabled);
        }

        private void SetHidden(bool hidden)
        {
            if (_hidden == hidden) return;
            _hidden = hidden;
            SetEmitterVisible(_wisps, !hidden);
            SetEmitterVisible(_rune, !hidden);
        }

        private static void SetEmitterVisible(ParticleSystem ps, bool visible)
        {
            if (ps == null) return;
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = visible;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = visible;
        }
    }
}

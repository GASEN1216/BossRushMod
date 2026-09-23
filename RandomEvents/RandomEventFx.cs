// ============================================================================
// RandomEventFx.cs - 随机事件「鸭生无常」的表现层：血月暗角、程序化烟花、空投落点圈与落地扬尘
// ============================================================================
// 为什么单独成文件（2026-09-23 特效审美审查 VA-21 / VA-22 / VA-23）：
//   - 血月原来是 HudOverlay 上一整屏均匀的红色平涂，画面中心的角色、敌人，连同血条和快捷栏一起染红；
//   - 鸭王的烟花原来是半空 15 次官方爆炸火球 + 黑烟 + 震屏 + 一个「★」飘字，读成的是空袭而不是庆祝；
//   - 空投像电梯一样 28 m 匀速降下，落地炸一团火球当「尘土」，下落期间看不到它会落在哪。
//   事件目录（RandomEventCatalog*.cs）贴着 1200 行的单文件预算，桥（RandomEventEffectsBridge*.cs）是
//   ModBehaviour 的 partial、受宿主行数预算约束，所以表现逻辑收在这个独立类型里，调用点各只留一行。
//
// 口径：
//   - 颜色走共享 token（BossRushUIColors.Rarity* / WarningText）或下方的表现常量；材质、形状贴图、
//     一次性爆发、灯光淡出全部复用 Common/Effects 的 BossRushFxKit，不另起材质或贴图。
//   - 只做表现：零伤害、零碰撞查询（不调官方 CreateExplosion，那条路对范围内接收体仍会派发 Hurt），
//     不改事件时长、落地时间点与箱子生成逻辑。
//   - 生命周期：一次性爆发与闪光灯各自到点自毁；落点圈是自管理的小组件，箱子被销毁（事件结束 /
//     切图 / 关开关）或下落时长走完就淡出自毁；血月暗角的 Image 挂在事件 Scope 托管的画布下。
//     本类没有静态缓存，只有一张只读调色表。
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>随机事件的程序化表现。全部 no-throw：表现失败只记 DevLog，不影响事件本身。</summary>
    internal static class RandomEventFx
    {
        #region 血月暗角

        /// <summary>血月暗角颜色：压暗的血红，只在屏幕边缘出现（中心由暗角贴图保持透明）。</summary>
        internal static readonly Color BloodMoonTint = new Color(
            RandomEventsTuning.BloodMoonVignetteRed,
            RandomEventsTuning.BloodMoonVignetteGreen,
            RandomEventsTuning.BloodMoonVignetteBlue,
            0f);

        /// <summary>
        /// 在 parent（全屏画布）下铺一张暗角 Image：边缘着色、中心透明，初始 alpha 为 0（由调用方逐帧淡入）。
        /// 暗角精灵拿不到时返回 null——宁可没有氛围层，也不退回一整屏平涂。
        /// </summary>
        internal static Image CreateScreenVignette(Transform parent, Color tint)
        {
            if (parent == null) return null;
            Sprite sprite = BossRushFxKit.GetVignetteSprite();
            if (sprite == null) return null;

            GameObject go = new GameObject("Vignette");
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;      // 拉伸到 16:9，暗角成椭圆，四角最深
            image.raycastTarget = false;
            image.color = new Color(tint.r, tint.g, tint.b, 0f);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        /// <summary>
        /// 血月暗角的逐帧 alpha：在 Min–Max 间按 SmoothStep 呼吸，再乘出现 / 消失包络
        /// （开场 FadeIn 秒淡入、到期前 FadeOut 秒淡出）。纯 float 运算，零分配。
        /// </summary>
        internal static float BloodMoonVignetteAlpha(float elapsedSeconds, float remainingSeconds)
        {
            float period = Mathf.Max(0.1f, RandomEventsTuning.BloodMoonVignetteBreathSeconds);
            float wave = (Mathf.Sin(elapsedSeconds * (Mathf.PI * 2f) / period) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(
                RandomEventsTuning.BloodMoonVignetteAlphaMin,
                RandomEventsTuning.BloodMoonVignetteAlphaMax,
                BossRushUI.SmoothStep(wave));
            float appear = BossRushUI.SmoothStep(
                elapsedSeconds / Mathf.Max(0.01f, RandomEventsTuning.BloodMoonVignetteFadeInSeconds));
            float vanish = BossRushUI.SmoothStep(
                remainingSeconds / Mathf.Max(0.01f, RandomEventsTuning.BloodMoonVignetteFadeOutSeconds));
            return alpha * appear * vanish;
        }

        #endregion

        #region 鸭王的烟花

        /// <summary>烟花调色：稀有度 token，逐发轮换。</summary>
        private static readonly Color[] FireworkPalette =
        {
            BossRushUIColors.RarityLegendary,
            BossRushUIColors.RarityRare,
            BossRushUIColors.RarityEpic,
            BossRushUIColors.RarityUncommon,
        };

        private const int FireworkSparkCount = 56;
        private const int FireworkGlitterCount = 8;
        private const int FireworkFlashEvery = 3;
        private const float FireworkFlashRange = 6f;
        private const float FireworkFlashIntensity = 2f;
        private const float FireworkFlashSeconds = 0.25f;

        /// <summary>
        /// 第 index 发烟花：白芯星芒一闪 → 带拖尾的火花球（白 → 稀有度色 → 暗且透明，受重力与阻力，先快后慢）
        /// → 一小撮慢落的闪点；每 3 发配一盏 0.25 秒淡出的闪光。零伤害、零碰撞查询、不震屏。
        /// </summary>
        internal static void PlayFirework(Vector3 position, int index)
        {
            try
            {
                Color tint = FireworkPalette[((index % FireworkPalette.Length) + FireworkPalette.Length) % FireworkPalette.Length];
                Color main = new Color(tint.r, tint.g, tint.b, 0.95f);
                Color end = new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0f);

                // 芯：一颗星芒闪一下，给爆点一个「砰」
                BossRushFxBurst flash = new BossRushFxBurst();
                flash.Shape = BossRushParticleShape.Star;
                flash.Blend = BossRushFxBlend.Additive;
                flash.Gain = BossRushFxKit.GainHot;
                flash.Count = 1;
                flash.SizeMin = 1.0f;
                flash.SizeMax = 1.2f;
                flash.LifeMin = 0.18f;
                flash.LifeMax = 0.24f;
                flash.GrowTo = 1.5f;
                flash.ShapeRadius = 0.01f;
                flash.Spin = 25f;
                flash.Core = Color.white;
                flash.Main = new Color(tint.r, tint.g, tint.b, 0.7f);
                flash.End = end;
                BossRushFxKit.PlayBurst(position, flash);

                // 主体：球形散开的拖尾火花
                BossRushFxBurst sparks = BossRushFxKit.Sparks(tint, FireworkSparkCount);
                sparks.SpeedMin = 5f;
                sparks.SpeedMax = 7f;
                sparks.SizeMin = 0.08f;
                sparks.SizeMax = 0.12f;
                sparks.LifeMin = 0.9f;
                sparks.LifeMax = 1.3f;
                sparks.Gravity = 0.35f;
                sparks.Drag = 1.6f;
                sparks.Stretch = 0.03f;
                sparks.GrowTo = 0.5f;
                sparks.ShapeRadius = 0.15f;
                sparks.Trail = 0.3f;
                sparks.Core = Color.white;
                sparks.Main = main;
                sparks.End = end;
                BossRushFxKit.PlayBurst(position, sparks);

                // 余韵：几颗慢落、闪烁的星点
                BossRushFxBurst glitter = new BossRushFxBurst();
                glitter.Shape = BossRushParticleShape.Star;
                glitter.Blend = BossRushFxBlend.Additive;
                glitter.Gain = BossRushFxKit.GainHot;
                glitter.Count = FireworkGlitterCount;
                glitter.SpeedMin = 1.5f;
                glitter.SpeedMax = 3f;
                glitter.SizeMin = 0.06f;
                glitter.SizeMax = 0.09f;
                glitter.LifeMin = 1.2f;
                glitter.LifeMax = 1.6f;
                glitter.Gravity = 0.25f;
                glitter.Drag = 2.5f;
                glitter.GrowTo = 0.3f;
                glitter.ShapeRadius = 0.3f;
                glitter.Spin = 90f;
                glitter.Core = Color.white;
                glitter.Main = main;
                glitter.End = end;
                BossRushFxKit.PlayBurst(position, glitter);

                if (index % FireworkFlashEvery == 0)
                {
                    GameObject lightObject = new GameObject("RandomEventFireworkFlash");
                    lightObject.transform.position = position;
                    Light light = lightObject.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = FireworkFlashRange;
                    light.color = tint;
                    light.shadows = LightShadows.None;
                    BossRushFxLightFade fade = BossRushFxLightFade.Attach(light, FireworkFlashIntensity, 0f);
                    if (fade != null) fade.FadeOut(FireworkFlashSeconds, true);
                    else UnityEngine.Object.Destroy(lightObject, FireworkFlashSeconds);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 烟花表现失败: " + e.Message);
            }
        }

        #endregion

        #region 空投

        /// <summary>落点圈颜色（WarningText 琥珀）的不透明度：低 alpha，只做提示，不盖地面。</summary>
        private const float AirdropMarkerAlpha = 0.45f;
        private static readonly Color AirdropDustColor = new Color(0.52f, 0.47f, 0.40f, 0.45f);
        private static readonly Color AirdropDebrisColor = new Color(0.30f, 0.26f, 0.22f, 0.9f);

        /// <summary>
        /// 下落期间在落点放一个呼吸的琥珀色圈：半径随下落从 2.2 m 收到 1.2 m，箱子被销毁或下落时长走完就淡出自毁。
        /// </summary>
        internal static void SpawnAirdropMarker(Vector3 groundPosition, GameObject crate, float fallSeconds)
        {
            try
            {
                RandomEventAirdropMarker.Create(groundPosition, crate, fallSeconds, AirdropMarkerAlpha);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投落点圈失败: " + e.Message);
            }
        }

        /// <summary>空投落地：贴地扩散的扬尘 + 几块溅起又落下的碎土，不是火球。</summary>
        internal static void PlayAirdropLanding(Vector3 groundPosition)
        {
            try
            {
                BossRushFxBurst dust = BossRushFxKit.Dust(AirdropDustColor, 14);
                dust.ShapeRadius = 0.6f;
                BossRushFxKit.PlayBurst(groundPosition + Vector3.up * 0.15f, dust);

                BossRushFxBurst debris = new BossRushFxBurst();
                debris.Shape = BossRushParticleShape.Shard;
                debris.Blend = BossRushFxBlend.Alpha;
                debris.Gain = BossRushFxKit.GainSoft;
                debris.Count = 8;
                debris.SpeedMin = 2.5f;
                debris.SpeedMax = 4f;
                debris.SizeMin = 0.05f;
                debris.SizeMax = 0.09f;
                debris.LifeMin = 0.5f;
                debris.LifeMax = 0.8f;
                debris.Gravity = 1.2f;
                debris.Drag = 1f;
                debris.GrowTo = 0.8f;
                debris.ShapeRadius = 0.5f;
                debris.Upward = true;
                debris.Spin = 360f;
                debris.Core = AirdropDebrisColor;
                debris.Main = AirdropDebrisColor;
                debris.End = new Color(AirdropDebrisColor.r, AirdropDebrisColor.g, AirdropDebrisColor.b, 0f);
                BossRushFxKit.PlayBurst(groundPosition + Vector3.up * 0.2f, debris);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投落地表现失败: " + e.Message);
            }
        }

        #endregion
    }

    /// <summary>
    /// 空投落点圈：一个贴地的琥珀色细环 + 中心一点淡光，半径随下落收拢、alpha 呼吸。
    /// 只在下落的两三秒里存在；箱子没了或时长走完就 0.3 秒淡出并销毁自己（按 deltaTime，暂停时一起停）。
    /// </summary>
    internal sealed class RandomEventAirdropMarker : MonoBehaviour
    {
        private const float StartRadius = 2.2f;
        private const float EndRadius = 1.2f;
        private const float CenterRadius = 0.45f;
        private const float CenterAlphaScale = 0.35f;
        /// <summary>共享环形精灵的环带中心在 0.41 个单位处（BossRushProceduralSprites.GetRingSprite）。</summary>
        private const float RingSpriteRadius = 0.41f;
        /// <summary>共享软圆精灵的可见半径（整张图 1 个单位宽）。</summary>
        private const float SoftCircleSpriteRadius = 0.5f;
        private const float GroundLift = 0.08f;
        private const float FadeInSeconds = 0.25f;
        private const float FadeOutSeconds = 0.3f;
        private const float BreathePeriod = 0.9f;

        private GameObject _crate;
        private SpriteRenderer _ring;
        private SpriteRenderer _center;
        private float _fallSeconds;
        private float _alpha;
        private float _age;
        private float _fadeOut = -1f;

        internal static void Create(Vector3 groundPosition, GameObject crate, float fallSeconds, float alpha)
        {
            Sprite ringSprite = BossRushProceduralSprites.GetRingSprite();
            Material ringMaterial = BossRushFxKit.GetSpriteMaterial(ringSprite, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            if (ringSprite == null || ringMaterial == null) return;

            GameObject root = new GameObject("RandomEventAirdropMarker");
            root.transform.position = groundPosition + Vector3.up * GroundLift;
            RandomEventAirdropMarker marker = root.AddComponent<RandomEventAirdropMarker>();
            marker._crate = crate;
            marker._fallSeconds = Mathf.Max(0.05f, fallSeconds);
            marker._alpha = Mathf.Clamp01(alpha);
            marker._ring = CreateFlatSprite(root.transform, "Ring", ringSprite, ringMaterial);

            Sprite centerSprite = BossRushFxKit.GetSoftCircleSprite();
            Material centerMaterial = BossRushFxKit.GetSpriteMaterial(centerSprite, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            if (centerSprite != null && centerMaterial != null)
            {
                marker._center = CreateFlatSprite(root.transform, "Center", centerSprite, centerMaterial);
                float scale = CenterRadius / SoftCircleSpriteRadius;
                marker._center.transform.localScale = new Vector3(scale, scale, 1f);
            }
            marker.Apply(0f, StartRadius);
        }

        private static SpriteRenderer CreateFlatSprite(Transform parent, string name, Sprite sprite, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 平躺，朝上
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            if (_fadeOut < 0f && (_crate == null || _age >= _fallSeconds)) _fadeOut = 0f;

            float envelope = BossRushUI.SmoothStep(_age / FadeInSeconds);
            if (_fadeOut >= 0f)
            {
                _fadeOut += dt;
                if (_fadeOut >= FadeOutSeconds)
                {
                    Destroy(gameObject);
                    return;
                }
                envelope *= 1f - BossRushUI.SmoothStep(_fadeOut / FadeOutSeconds);
            }

            float wave = (Mathf.Sin(_age * (Mathf.PI * 2f) / BreathePeriod) + 1f) * 0.5f;
            float breathe = Mathf.Lerp(0.6f, 1f, BossRushUI.SmoothStep(wave));
            float radius = Mathf.Lerp(StartRadius, EndRadius, BossRushUI.EaseOut(_age / _fallSeconds));
            Apply(_alpha * envelope * breathe, radius);
        }

        private void Apply(float alpha, float radius)
        {
            Color tint = BossRushUIColors.WarningText;
            if (_ring != null)
            {
                float scale = radius / RingSpriteRadius;
                _ring.transform.localScale = new Vector3(scale, scale, 1f);
                _ring.color = new Color(tint.r, tint.g, tint.b, alpha);
            }
            if (_center != null)
            {
                _center.color = new Color(tint.r, tint.g, tint.b, alpha * CenterAlphaScale);
            }
        }
    }
}

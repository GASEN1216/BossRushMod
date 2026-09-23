// ============================================================================
// PhantomWitchScytheAction.cs - 幽灵女巫大镰右键技能动作：诅咒领域
// ============================================================================
// 模块说明：
//   右键技能「诅咒领域」：
//   - 前摇 0.25s（锁定移动），随后在玩家脚下生成 4m 半径紫色符文阵
//   - 领域独立存活 4s（不跟随玩家），每 0.5s 对范围内敌人造成 22 点幽能伤害并施加一层诅咒
//   - 技能冷却 12s，启动体力 20
//
//   设计说明：
//   - 动作本身仅持续 RealmActionDuration（0.6s）负责前摇/收招的动画窗口
//   - 领域由独立 MonoBehaviour（PhantomWitchCurseRealmRuntime）运行，互不阻塞
//   - 诅咒 Buff 复用 PhantomWitchAssetManager.GetCurseBuff()，与 Boss 共享（最多 3 层）
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Common.Equipment;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫大镰右键技能动作 — 诅咒领域
    /// </summary>
    public class PhantomWitchScytheAction : EquipmentAbilityAction
    {
        private static PhantomWitchScytheConfig _config;

        private bool realmSpawned;
        private Vector3 realmOrigin;

        public static void SetConfig(PhantomWitchScytheConfig config)
        {
            _config = config;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (_config == null)
            {
                _config = new PhantomWitchScytheConfig();
            }
            return _config;
        }

        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        protected override bool CanUseHandWhileActive()
        {
            // 前摇后允许玩家继续挥镰，不锁手
            return true;
        }

        protected override bool OnAbilityStart()
        {
            realmSpawned = false;

            if (characterController == null)
            {
                return false;
            }

            realmOrigin = SnapToGround(characterController.transform.position);
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            // 前摇结束后生成领域（仅生成一次）
            if (!realmSpawned && actionElapsedTime >= PhantomWitchScytheConfig.RealmCastTime)
            {
                realmSpawned = true;
                SpawnCurseRealm(realmOrigin, characterController);
            }

            // 动作本体仅持续 RealmActionDuration（0.6s），让玩家迅速恢复行动自由
            if (actionElapsedTime >= PhantomWitchScytheConfig.RealmActionDuration)
            {
                // 兜底：若前摇意外未触发，也强制生成一次
                if (!realmSpawned)
                {
                    realmSpawned = true;
                    SpawnCurseRealm(realmOrigin, characterController);
                }

                StopAction();
            }
        }

        protected override void OnAbilityStop()
        {
            realmSpawned = false;
        }

        private static void SpawnCurseRealm(Vector3 origin, CharacterMainControl caster)
        {
            try
            {
                GameObject host = new GameObject("PhantomWitch_CurseRealm");
                host.transform.position = origin;

                PhantomWitchCurseRealmRuntime runtime =
                    host.AddComponent<PhantomWitchCurseRealmRuntime>();
                runtime.Initialize(origin, caster);

                ModBehaviour.DevLog("[PhantomWitchScythe] 诅咒领域已在玩家脚下生成 @ " + origin);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 生成诅咒领域失败: " + e.Message);
            }
        }

        private static Vector3 SnapToGround(Vector3 position)
        {
            try
            {
                int groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                Vector3 samplePoint = position + Vector3.up * 3f;

                RaycastHit hit;
                if (Physics.Raycast(samplePoint, Vector3.down, out hit, 10f, groundMask))
                {
                    return hit.point;
                }
            }
            catch
            {
            }

            return position;
        }
    }

    /// <summary>
    /// 诅咒领域运行时（独立 MonoBehaviour，独立存活 4s）
    /// </summary>
    internal sealed class PhantomWitchCurseRealmRuntime : MonoBehaviour
    {
        private static readonly Collider[] damageHitBuffer = new Collider[32];
        private static readonly HashSet<int> processedReceiverIds = new HashSet<int>();

        private Vector3 realmOrigin;
        private CharacterMainControl caster;
        private float elapsedTime;
        private float nextDamageTime;
        private GameObject visualMarker;

        private float overrideRadius;
        private float overrideDuration;
        private float overrideDamagePerTick;
        private float overrideDamageInterval;

        internal void Initialize(Vector3 origin, CharacterMainControl ownerCaster)
        {
            Initialize(origin, ownerCaster,
                PhantomWitchScytheConfig.RealmRadius,
                PhantomWitchScytheConfig.RealmDuration,
                PhantomWitchScytheConfig.RealmDamagePerTick,
                PhantomWitchScytheConfig.RealmDamageInterval);
        }

        internal void Initialize(Vector3 origin, CharacterMainControl ownerCaster,
            float radius, float duration, float damagePerTick, float damageInterval)
        {
            realmOrigin = origin;
            caster = ownerCaster;
            overrideRadius = radius;
            overrideDuration = duration;
            overrideDamagePerTick = damagePerTick;
            overrideDamageInterval = damageInterval;
            elapsedTime = 0f;
            nextDamageTime = 0f;

            CreateVisualMarker();
        }

        private void Update()
        {
            elapsedTime += Time.deltaTime;

            // 周期伤害判定
            if (elapsedTime >= nextDamageTime && elapsedTime < overrideDuration)
            {
                DealRealmDamage();
                nextDamageTime = elapsedTime + overrideDamageInterval;
            }

            // 持续时间结束 → 自毁
            if (elapsedTime >= overrideDuration)
            {
                DestroySelf();
            }
        }

        private void OnDestroy()
        {
            if (visualMarker != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(visualMarker);
                }
                catch
                {
                }
                visualMarker = null;
            }
        }

        private void CreateVisualMarker()
        {
            try
            {
                visualMarker = PhantomWitchCurseRealmVisual.Create(
                    realmOrigin + Vector3.up * PhantomWitchScytheConfig.RealmVisualHeight,
                    overrideRadius,
                    overrideDuration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 领域视觉创建失败: " + e.Message);
            }
        }

        private void DealRealmDamage()
        {
            if (caster == null)
            {
                return;
            }

            int layerMask;
            try
            {
                layerMask = FenHuangHalberdRuntime.DamageReceiverLayerMask;
            }
            catch
            {
                layerMask = ~0;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                realmOrigin,
                overrideRadius,
                damageHitBuffer,
                layerMask);

            if (hitCount <= 0)
            {
                return;
            }

            Buff curseBuff = PhantomWitchAssetManager.GetCurseBuff();
            processedReceiverIds.Clear();

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = damageHitBuffer[i];
                if (col == null)
                {
                    continue;
                }

                DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(col);
                if (receiver == null || receiver.health == null || receiver.health.IsDead)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (!processedReceiverIds.Add(receiverId))
                {
                    continue;
                }

                if (!IsEnemyReceiver(receiver))
                {
                    continue;
                }

                CharacterMainControl realmTarget = receiver.health.TryGetCharacter();
                if (realmTarget == null)
                {
                    continue;
                }

                // 2D 距离二次过滤
                Vector3 delta = receiver.transform.position - realmOrigin;
                delta.y = 0f;
                if (delta.sqrMagnitude > overrideRadius * overrideRadius)
                {
                    continue;
                }

                // 施加幽能伤害
                try
                {
                    DamageInfo damageInfo = new DamageInfo(caster);
                    damageInfo.damageType = DamageTypes.normal;
                    damageInfo.damageValue = overrideDamagePerTick;
                    damageInfo.damagePoint = receiver.transform.position;
                    damageInfo.damageNormal = (receiver.transform.position - realmOrigin).normalized;
                    damageInfo.fromWeaponItemID = PhantomWitchScytheIds.WeaponTypeId;
                    damageInfo.isFromBuffOrEffect = true;
                    damageInfo.crit = -1;
                    damageInfo.AddElementFactor(ElementTypes.ghost, 1f);

                    using (ModeGTelemetrySuppressionScope.Enter(receiver.health))
                    {
                        receiver.Hurt(damageInfo);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PhantomWitchScythe] 领域伤害应用失败: " + e.Message);
                }

                // 叠加一层诅咒 Buff
                if (curseBuff != null)
                {
                    try
                    {
                        realmTarget.AddBuff(curseBuff, caster, PhantomWitchScytheIds.WeaponTypeId);
                        PhantomWitchCurseSweatVfx.TryAttach(realmTarget.gameObject);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PhantomWitchScythe] 领域施加诅咒失败: " + e.Message);
                    }
                }
            }
        }

        private bool IsEnemyReceiver(DamageReceiver receiver)
        {
            if (receiver == null || caster == null)
            {
                return false;
            }

            try
            {
                return Team.IsEnemy(caster.Team, receiver.Team);
            }
            catch
            {
                return false;
            }
        }

        private void DestroySelf()
        {
            try
            {
                UnityEngine.Object.Destroy(gameObject);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 诅咒领域视觉：多层叠加（地面染色 / 开场冲击波 / 双层旋转符文环 / 外缘星轨 /
    /// 上升亡魂粒子 / 中心脉冲光晕 / 尾声淡出），避免"一个紫球"的廉价观感。
    /// 2026-09-23 审查 VB-10：此前是直径约 9.7 m、alpha 0.55 的饱和紫圆盘 + 0.22 m 纯紫硬边带 + 2.9 m 亮紫呼吸面片，
    /// 在暖琥珀地图上像一块塑料贴纸。现在向 Redesign 那套银灰 + 暗紫色板靠拢：环走柔边带贴图、地面暗染、
    /// 大面积颜色压饱和压 alpha，亮的只留细环与火星。玩家右键领域与 Boss 领域共用这一套。
    /// </summary>
    internal static class PhantomWitchCurseRealmVisual
    {
        // 配色 —— 银灰高光 + 低饱和灰紫，阴影偏深靛；大面积（地面、中心光晕）只用暗色低 alpha
        private static readonly Color RingColorOuter = new Color(PhantomWitchConfig.VioletVoidVeil.r, PhantomWitchConfig.VioletVoidVeil.g, PhantomWitchConfig.VioletVoidVeil.b, 0.85f);
        private static readonly Color RingColorInner = new Color(PhantomWitchConfig.SilverAshCore.r, PhantomWitchConfig.SilverAshCore.g, PhantomWitchConfig.SilverAshCore.b, 0.60f);
        private static readonly Color RuneMarkColor = new Color(0.90f, 0.89f, 0.85f, 1.00f);
        private static readonly Color GroundStainColor = new Color(0.18f, 0.10f, 0.24f, 0.28f);
        private static readonly Color CoreGlowColor = new Color(0.62f, 0.50f, 0.78f, 0.25f);
        private static readonly Color ShockwaveColor = new Color(0.90f, 0.86f, 0.95f, 0.80f);
        private static readonly Color WispColor = new Color(0.72f, 0.62f, 0.90f, 1.00f);
        private static readonly Color SparkColor = new Color(1.00f, 0.94f, 0.98f, 1.00f);
        private static readonly Color BlackSmokeCoreColor = new Color(0.10f, 0.08f, 0.12f, 0.90f);
        private static readonly Color BlackSmokeMidColor = new Color(0.20f, 0.14f, 0.26f, 0.68f);
        private static readonly Color BlackSmokeEdgeColor = new Color(0.42f, 0.28f, 0.56f, 0.12f);
        /// <summary>领域点光：与女巫其它特效同一套低饱和灰紫，强度 ≤3.5、半径 ≤5 m（审查 VB-09）。</summary>
        private static readonly Color RealmLightColor = new Color(0.65f, 0.58f, 0.72f, 1f);

        private static Mesh cachedQuadMesh;

        private const int OuterRingSegments = 32;
        private const int InnerRingSegments = 20;
        private const int RuneMarkCount = 5;
        private const float RotationSpeedOuter = 14f;
        private const float RotationSpeedInner = -22f;

        private static int ResolveAdaptiveCount(PhantomWitchFxDetailLevel detailLevel, int full, int reduced, int minimal)
        {
            switch (detailLevel)
            {
                case PhantomWitchFxDetailLevel.Minimal:
                    return Mathf.Max(0, minimal);
                case PhantomWitchFxDetailLevel.Reduced:
                    return Mathf.Max(0, reduced);
                default:
                    return Mathf.Max(0, full);
            }
        }

        private static float ResolveAdaptiveFloat(PhantomWitchFxDetailLevel detailLevel, float full, float reduced, float minimal)
        {
            switch (detailLevel)
            {
                case PhantomWitchFxDetailLevel.Minimal:
                    return Mathf.Max(0f, minimal);
                case PhantomWitchFxDetailLevel.Reduced:
                    return Mathf.Max(0f, reduced);
                default:
                    return Mathf.Max(0f, full);
            }
        }

        internal static GameObject Create(Vector3 origin, float radius, float duration)
        {
            PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
            int outerSegments = ResolveAdaptiveCount(detailLevel, OuterRingSegments, PhantomWitchConfig.FxReducedRingSegments, PhantomWitchConfig.FxMinimalRingSegments);
            int innerSegments = ResolveAdaptiveCount(detailLevel, InnerRingSegments, PhantomWitchConfig.FxReducedSmallRingSegments, PhantomWitchConfig.FxMinimalSmallRingSegments);
            int runeCount = ResolveAdaptiveCount(detailLevel, RuneMarkCount, 4, 0);
            GameObject root = new GameObject("PhantomWitch_CurseRealm_Visual");
            root.transform.position = origin;
            PhantomWitchFxRuntime.RegisterEffectRoot(root);

            // 材质统一走 BossRushFxMaterials（审查 VB-02）：此前这里的 Shader.Find 回退链首选项在游戏里都不存在。
            if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
            {
                CreatePointLight(root.transform, new Vector3(0f, 1f, 0f), RealmLightColor, Mathf.Min(5f, radius + 1f), 3.2f, duration);
            }

            CreateGroundStain(root.transform, radius);
            CreateAreaBlackSmoke(root.transform, radius, duration, detailLevel);
            CreateShockwave(root.transform, radius, outerSegments);
            CreateRingChild(root.transform, "OuterRing", radius, outerSegments, 0.28f, RingColorOuter, RotationSpeedOuter, pulse: detailLevel == PhantomWitchFxDetailLevel.Full);
            CreateRingChild(root.transform, "InnerRing", radius * 0.72f, innerSegments, 0.10f, RingColorInner, RotationSpeedInner, pulse: detailLevel != PhantomWitchFxDetailLevel.Minimal);

            if (runeCount > 0)
            {
                CreateRuneMarks(root.transform, radius * 0.55f, runeCount);
            }
            if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
            {
                CreatePentagram(root.transform, radius * 0.55f);
            }
            CreateCoreGlow(root.transform, radius * 0.32f, detailLevel != PhantomWitchFxDetailLevel.Minimal);
            CreateRisingWisps(root.transform, radius, duration, detailLevel);
            CreateOrbitSparks(root.transform, radius, duration, detailLevel);
            if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
            {
                PhantomWitchVfxRedesign.CreateStardustEmitter(root.transform, radius * 0.95f, 10f, duration);
            }

            // 淡入淡出 + 总时长自毁
            PhantomWitchCurseRealmFader fader = root.AddComponent<PhantomWitchCurseRealmFader>();
            fader.Initialize(duration);

            if (duration > 0f)
            {
                UnityEngine.Object.Destroy(root, duration);
            }

            ModBehaviour.DevLog("[PhantomWitchScythe] 诅咒领域已在玩家脚下生成 @ " + origin);
            return root;
        }

        private static void CreatePointLight(Transform parent, Vector3 localPos, Color color, float range, float intensity, float duration)
        {
            GameObject lightGo = new GameObject("PW_PointLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPos;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = Mathf.Min(5f, range);
            light.intensity = Mathf.Min(3.5f, intensity);
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;

            PhantomWitchLightPulse pulser = lightGo.AddComponent<PhantomWitchLightPulse>();
            pulser.Configure(light.intensity, light.range, duration);
        }

        // ---------- 地面染色：最底层的暗色半透明圆盘，读作"被污染的土地"而不是一层紫光 ----------
        private static void CreateGroundStain(Transform parent, float radius)
        {
            CreateFlatQuad(parent, "GroundStain", radius * 2.15f, 0.02f, GroundStainColor, PhantomWitchAssetManager.GetQuadMaterial());
        }

        // ---------- 整区黑烟覆盖：以 billbord 粒子铺满法阵区域，让整个领域都带有左键那种黑紫雾感 ----------
        private static void CreateAreaBlackSmoke(Transform parent, float radius, float duration, PhantomWitchFxDetailLevel detailLevel)
        {
            int innerMaxParticles = ResolveAdaptiveCount(detailLevel, 72, 40, 0);
            float innerEmissionRate = ResolveAdaptiveFloat(detailLevel, 20f, 12f, 0f);
            int outerMaxParticles = ResolveAdaptiveCount(detailLevel, 56, 32, 0);
            float outerEmissionRate = ResolveAdaptiveFloat(detailLevel, 14f, 8f, 0f);

            if ((innerMaxParticles <= 0 || innerEmissionRate <= 0f) &&
                (outerMaxParticles <= 0 || outerEmissionRate <= 0f))
            {
                return;
            }

            if (innerMaxParticles > 0 && innerEmissionRate > 0f)
            {
                CreateBlackSmokeEmitter(
                    parent,
                    "RealmBlackSmoke_Inner",
                    duration,
                    0.08f,
                    radius * 0.82f,
                    1f,
                    innerMaxParticles,
                    innerEmissionRate,
                    Mathf.Max(0.18f, radius * 0.14f),
                    Mathf.Max(0.32f, radius * 0.32f),
                    0.07f,
                    0.12f,
                    detailLevel);
            }

            if (outerMaxParticles > 0 && outerEmissionRate > 0f)
            {
                CreateBlackSmokeEmitter(
                    parent,
                    "RealmBlackSmoke_Outer",
                    duration,
                    0.12f,
                    radius * 1.08f,
                    0.92f,
                    outerMaxParticles,
                    outerEmissionRate,
                    Mathf.Max(0.24f, radius * 0.18f),
                    Mathf.Max(0.40f, radius * 0.38f),
                    0.09f,
                    0.16f,
                    detailLevel);
            }
        }

        private static void CreateBlackSmokeEmitter(
            Transform parent,
            string name,
            float duration,
            float yOffset,
            float emitterRadius,
            float radiusThickness,
            int maxParticles,
            float emissionRate,
            float minSize,
            float maxSize,
            float horizontalDrift,
            float verticalDrift,
            PhantomWitchFxDetailLevel detailLevel)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            // 黑烟必须半透明混合：加色的暗色粒子等于看不见。
            ConfigureDefaultParticleRenderer(ps, BossRushFxBlend.Alpha);

            var main = ps.main;
            main.duration = Mathf.Max(0.5f, duration);
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = Color.white;
            main.gravityModifier = -0.02f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = emissionRate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = emitterRadius;
            shape.radiusThickness = radiusThickness;

            // 审查 VB-29-4：发射器 GO 转了 90° 放平，Local 空间的 y 实际指向 +Z（远离镜头）；
            // 速度改用 World 空间，y 才是真正的向上漂。
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-horizontalDrift, horizontalDrift);
            velocity.y = new ParticleSystem.MinMaxCurve(0.03f, verticalDrift);
            velocity.z = new ParticleSystem.MinMaxCurve(-horizontalDrift, horizontalDrift);

            // 大面积烟压 alpha（审美口径第 6 条）：峰值 0.82 → 0.62，读得出黑紫雾感但不糊成一整块。
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(BlackSmokeCoreColor, 0f),
                    new GradientColorKey(BlackSmokeMidColor, 0.45f),
                    new GradientColorKey(BlackSmokeEdgeColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.62f, 0.18f),
                    new GradientAlphaKey(0.40f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(0.28f, 1f),
                new Keyframe(1f, 0.55f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = detailLevel == PhantomWitchFxDetailLevel.Full;
            if (noise.enabled)
            {
                noise.strength = 0.14f;
                noise.frequency = 0.42f;
            }

            ps.Play();
        }

        // ---------- 开场冲击波：0→1 快速扩张再立即消失，给施法瞬间的"咚"一下反馈 ----------
        private static void CreateShockwave(Transform parent, float radius, int segments)
        {
            GameObject shock = new GameObject("Shockwave");
            shock.transform.SetParent(parent, false);
            shock.transform.localPosition = new Vector3(0f, 0.01f, 0f);

            PhantomWitchFlatRingMesh ringMesh = shock.AddComponent<PhantomWitchFlatRingMesh>();
            ringMesh.Configure(Mathf.Max(12, segments), 0.01f, 0.28f, GetSharedLineMaterial(), ShockwaveColor);

            PhantomWitchShockwaveAnimation shockAnim = shock.AddComponent<PhantomWitchShockwaveAnimation>();
            shockAnim.Configure(radius * 1.15f, 0.35f, ShockwaveColor);
        }

        // ---------- 符文环：柔边带绕轴旋转，宽度可轻微呼吸 ----------
        private static void CreateRingChild(Transform parent, string name, float radius, int segments, float width, Color color, float rotationSpeed, bool pulse)
        {
            GameObject ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            PhantomWitchFlatRingMesh ringMesh = ring.AddComponent<PhantomWitchFlatRingMesh>();
            ringMesh.Configure(Mathf.Max(12, segments), radius, width, GetSharedLineMaterial(), color);

            PhantomWitchRingSpin spin = ring.AddComponent<PhantomWitchRingSpin>();
            spin.rotationSpeed = rotationSpeed;

            if (pulse)
            {
                PhantomWitchRingPulse pulseAnim = ring.AddComponent<PhantomWitchRingPulse>();
                pulseAnim.Configure(1f, 0.03f, 1.6f, color);
            }
        }

        // ---------- 符文横条：内环上切出几段亮边，随内环一起反向旋转 ----------
        private static void CreateRuneMarks(Transform parent, float radius, int count)
        {
            if (count <= 0)
            {
                return;
            }

            GameObject runeRoot = new GameObject("RuneMarks");
            runeRoot.transform.SetParent(parent, false);
            runeRoot.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            Material sharedLine = GetSharedLineMaterial();
            Color runeColor = new Color(RuneMarkColor.r, RuneMarkColor.g, RuneMarkColor.b, 0.78f);

            for (int i = 0; i < count; i++)
            {
                float baseAngle = (float)i / count * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(baseAngle) * radius, 0f, Mathf.Sin(baseAngle) * radius);
                Vector3 tangent = new Vector3(-Mathf.Sin(baseAngle), 0f, Mathf.Cos(baseAngle)) * 0.55f;
                CreateFlatSegment(runeRoot.transform, "Rune_" + i, center - tangent, center + tangent, 0.11f, runeColor, sharedLine, 0f);
            }

            PhantomWitchRingSpin spin = runeRoot.AddComponent<PhantomWitchRingSpin>();
            spin.rotationSpeed = RotationSpeedInner * 0.6f;
        }

        // ---------- 五芒星：连接内环上的 5 个等距点，经典的"诅咒阵法"元素 ----------
        private static void CreatePentagram(Transform parent, float radius)
        {
            GameObject pent = new GameObject("Pentagram");
            pent.transform.SetParent(parent, false);
            pent.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            Material sharedLine = GetSharedLineMaterial();
            Color pentagramColor = new Color(RingColorInner.r, RingColorInner.g, RingColorInner.b, 0.45f);

            // 5 点五芒星：按 i * 2 跳点连接形成星形
            Vector3[] points = new Vector3[5];
            for (int i = 0; i < 5; i++)
            {
                float angle = -Mathf.PI / 2f + i * (2f * Mathf.PI / 5f);
                points[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            Vector3[] order = new Vector3[5];
            for (int i = 0; i < 5; i++)
            {
                order[i] = points[(i * 2) % 5];
            }

            for (int i = 0; i < order.Length; i++)
            {
                CreateFlatSegment(pent.transform, "Edge_" + i, order[i], order[(i + 1) % order.Length], 0.08f, pentagramColor, sharedLine, i * 0.002f);
            }

            PhantomWitchRingSpin spin = pent.AddComponent<PhantomWitchRingSpin>();
            spin.rotationSpeed = RotationSpeedOuter * 0.4f;
        }

        // ---------- 中心脉冲光晕：贴地的加色软圆，周期缩放 + alpha 呼吸（alpha 上限 0.25，只做核心不铺大面） ----------
        private static void CreateCoreGlow(Transform parent, float radius, bool enablePulse)
        {
            GameObject core = CreateFlatQuad(parent, "CoreGlow", radius * 2f, 0.12f, CoreGlowColor, PhantomWitchAssetManager.GetGlowQuadMaterial());
            Renderer renderer = core.GetComponent<Renderer>();
            if (renderer != null && enablePulse)
            {
                PhantomWitchCorePulse pulse = core.AddComponent<PhantomWitchCorePulse>();
                pulse.Configure(renderer, CoreGlowColor, radius * 2f, 1.2f);
            }
        }

        // ---------- 上升亡魂粒子：从地面缓缓升起，偏冷蓝紫的光点 ----------
        private static void CreateRisingWisps(Transform parent, float radius, float duration, PhantomWitchFxDetailLevel detailLevel)
        {
            int maxParticles = ResolveAdaptiveCount(detailLevel, 28, 14, 0);
            float emissionRate = ResolveAdaptiveFloat(detailLevel, 8f, 4f, 0f);
            if (maxParticles <= 0 || emissionRate <= 0f)
            {
                return;
            }

            GameObject go = new GameObject("RisingWisps");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ConfigureDefaultParticleRenderer(ps);

            var main = ps.main;
            main.duration = Mathf.Max(0.5f, duration);
            main.loop = false;
            main.startLifetime = 1.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startColor = WispColor;
            main.gravityModifier = -0.15f; // 轻微向上
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = emissionRate;

            // 审查 VB-04：Circle 形状默认在局部 XY 平面（竖着），此前亡魂从一面竖直的 4 m 大圆盘上升起；放平到地面。
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.9f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(WispColor, 0f),
                    new GradientColorKey(new Color(0.92f, 0.86f, 0.96f), 0.5f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidMid, 1f)
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.3f),
                    new GradientAlphaKey(0.5f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 0.6f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = detailLevel == PhantomWitchFxDetailLevel.Full;
            if (noise.enabled)
            {
                noise.strength = 0.2f;
                noise.frequency = 0.5f;
            }

            ps.Play();
        }

        // ---------- 外缘星火：沿领域边缘绕圈的小亮点，加强"阵法运转"感 ----------
        private static void CreateOrbitSparks(Transform parent, float radius, float duration, PhantomWitchFxDetailLevel detailLevel)
        {
            int maxParticles = ResolveAdaptiveCount(detailLevel, 32, 16, 0);
            float emissionRate = ResolveAdaptiveFloat(detailLevel, 12f, 6f, 0f);
            if (maxParticles <= 0 || emissionRate <= 0f)
            {
                return;
            }

            GameObject go = new GameObject("OrbitSparks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.15f, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ConfigureDefaultParticleRenderer(ps);

            var main = ps.main;
            main.duration = Mathf.Max(0.5f, duration);
            main.loop = false;
            main.startLifetime = 0.8f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
            main.startColor = SparkColor;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = emissionRate;

            // 审查 VB-04：「只在边缘」的火星此前在一面竖着的圆上，立成一道 4.5 m 高的拱门；放平后沿边缘绕圈（阵法运转）。
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.02f; // 只在边缘
            shape.rotation = new Vector3(90f, 0f, 0f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            // 轨道速度三轴与线速度三轴同为「两常数」模式，避免 Unity 的 "Particle Velocity curves must all be in the same mode" 刷屏。
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0.5f, 0.7f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(SparkColor, 0f),
                    new GradientColorKey(PhantomWitchConfig.VioletVoidVeil, 1f)
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0.3f)));

            ps.Play();
        }

        private static void ConfigureDefaultParticleRenderer(ParticleSystem ps)
        {
            // 亡魂、火星：共享软圆加色材质（BossRushFxMaterials）
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);
        }

        private static void ConfigureDefaultParticleRenderer(ParticleSystem ps, BossRushFxBlend blend)
        {
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps, blend);
        }

        /// <summary>环、符文、五芒星：柔边带贴图的半透明材质（此前是 whiteTexture 硬边带）。</summary>
        private static Material GetSharedLineMaterial()
        {
            return PhantomWitchVfxRedesign.GetSharedGroundLineMaterial();
        }


        private static Mesh GetSharedQuadMesh()
        {
            if (cachedQuadMesh != null)
            {
                return cachedQuadMesh;
            }

            cachedQuadMesh = new Mesh();
            cachedQuadMesh.name = "PW_CurseRealm_QuadMesh";
            cachedQuadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            cachedQuadMesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            cachedQuadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            cachedQuadMesh.RecalculateNormals();
            return cachedQuadMesh;
        }

        private static GameObject CreateFlatSegment(Transform parent, string name, Vector3 start, Vector3 end, float width, Color color, Material material, float yOffset)
        {
            GameObject segment = new GameObject(name);
            segment.transform.SetParent(parent, false);
            segment.transform.localPosition = new Vector3(0f, yOffset, 0f);

            MeshFilter meshFilter = segment.AddComponent<MeshFilter>();
            Mesh mesh = BuildSegmentMesh(start, end, width);
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = segment.AddComponent<MeshRenderer>();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
                PhantomWitchFxRenderUtil.SetRendererColor(meshRenderer, color);
            }

            PhantomWitchRuntimeMesh runtimeMesh = segment.AddComponent<PhantomWitchRuntimeMesh>();
            runtimeMesh.SetMesh(mesh);
            return segment;
        }

        private static Mesh BuildSegmentMesh(Vector3 start, Vector3 end, float width)
        {
            Vector3 direction = end - start;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.right * 0.001f;
            }

            Vector3 side = Vector3.Cross(Vector3.up, direction.normalized) * (Mathf.Max(0.001f, width) * 0.5f);
            Mesh mesh = new Mesh();
            mesh.name = "PW_CurseRealm_SegmentMesh";
            mesh.vertices = new Vector3[]
            {
                start - side,
                start + side,
                end + side,
                end - side
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static void BuildRingMesh(Mesh mesh, float radius, float width, int segments)
        {
            if (mesh == null)
            {
                return;
            }

            segments = Mathf.Max(3, segments);
            radius = Mathf.Max(0.001f, radius);
            width = Mathf.Max(0.001f, width);

            float outerRadius = radius + width * 0.5f;
            float innerRadius = Mathf.Max(0.001f, radius - width * 0.5f);
            Vector3[] vertices = new Vector3[segments * 2];
            Vector2[] uv = new Vector2[segments * 2];
            int[] triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int vertexIndex = i * 2;
                vertices[vertexIndex] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                vertices[vertexIndex + 1] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
                float u = (float)i / segments;
                uv[vertexIndex] = new Vector2(u, 1f);
                uv[vertexIndex + 1] = new Vector2(u, 0f);

                int nextVertexIndex = ((i + 1) % segments) * 2;
                int triangleIndex = i * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 1;
                triangles[triangleIndex + 2] = nextVertexIndex;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = nextVertexIndex + 1;
                triangles[triangleIndex + 5] = nextVertexIndex;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        internal static void ClearCache()
        {
            // 线 / 面片 / 粒子材质归 BossRushFxMaterials（全 Mod 共享），这里只清自己建的网格。
            if (cachedQuadMesh != null)
            {
                UnityEngine.Object.Destroy(cachedQuadMesh);
                cachedQuadMesh = null;
            }
        }

        private static GameObject CreateFlatQuad(Transform parent, string name, float scale, float yOffset, Color color, Material material)
        {
            GameObject quad = new GameObject(name);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, yOffset, 0f);
            quad.transform.localScale = new Vector3(scale, 1f, scale);

            MeshFilter meshFilter = quad.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetSharedQuadMesh();

            MeshRenderer meshRenderer = quad.AddComponent<MeshRenderer>();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
                PhantomWitchFxRenderUtil.SetRendererColor(meshRenderer, color);
            }

            return quad;
        }
    }
}

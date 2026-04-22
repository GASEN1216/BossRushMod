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
                    damageInfo.crit = -1;
                    damageInfo.AddElementFactor(ElementTypes.ghost, 1f);

                    receiver.Hurt(damageInfo);
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
    /// </summary>
    internal static class PhantomWitchCurseRealmVisual
    {
        // 配色 —— 主色调为紫罗兰（亡灵/诅咒），高光偏冷白，阴影偏深靛
        private static readonly Color RingColorOuter = new Color(0.62f, 0.25f, 1.00f, 0.95f);
        private static readonly Color RingColorInner = new Color(0.95f, 0.70f, 1.00f, 0.90f);
        private static readonly Color RuneMarkColor = new Color(1.00f, 0.85f, 1.00f, 1.00f);
        private static readonly Color GroundStainColor = new Color(0.34f, 0.05f, 0.58f, 0.55f);
        private static readonly Color CoreGlowColor = new Color(0.80f, 0.35f, 1.00f, 0.65f);
        private static readonly Color ShockwaveColor = new Color(1.00f, 0.75f, 1.00f, 0.85f);
        private static readonly Color WispColor = new Color(0.75f, 0.55f, 1.00f, 1.00f);
        private static readonly Color SparkColor = new Color(1.00f, 0.90f, 1.00f, 1.00f);

        private static Material cachedLineMaterial;
        private static Material cachedQuadMaterial;
        private static Material cachedParticleMaterial;
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

            Shader unlit = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Color");

            if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
            {
                CreatePointLight(root.transform, new Vector3(0f, 1f, 0f), CoreGlowColor, radius * 1.5f, 6.5f, duration);
            }

            CreateGroundStain(root.transform, radius, unlit);
            CreateShockwave(root.transform, radius, outerSegments, unlit);
            CreateRingChild(root.transform, "OuterRing", radius, outerSegments, 0.22f, RingColorOuter, RotationSpeedOuter, unlit, pulse: detailLevel == PhantomWitchFxDetailLevel.Full);
            CreateRingChild(root.transform, "InnerRing", radius * 0.72f, innerSegments, 0.14f, RingColorInner, RotationSpeedInner, unlit, pulse: detailLevel != PhantomWitchFxDetailLevel.Minimal);

            if (runeCount > 0)
            {
                CreateRuneMarks(root.transform, radius * 0.55f, runeCount, unlit);
            }
            if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
            {
                CreatePentagram(root.transform, radius * 0.55f, unlit);
            }
            CreateCoreGlow(root.transform, radius * 0.32f, unlit, detailLevel != PhantomWitchFxDetailLevel.Minimal);
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
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;

            PhantomWitchLightPulse pulser = lightGo.AddComponent<PhantomWitchLightPulse>();
            pulser.Configure(intensity, range, duration);
        }

        // ---------- 地面染色：放在最底层的柔化紫色圆盘，给一种"被污染的土地"感 ----------
        private static void CreateGroundStain(Transform parent, float radius, Shader unlit)
        {
            CreateFlatQuad(parent, "GroundStain", radius * 2.15f, 0.02f, GroundStainColor, unlit);
        }

        // ---------- 开场冲击波：0→1 快速扩张再立即消失，给施法瞬间的"咚"一下反馈 ----------
        private static void CreateShockwave(Transform parent, float radius, int segments, Shader unlit)
        {
            GameObject shock = new GameObject("Shockwave");
            shock.transform.SetParent(parent, false);
            shock.transform.localPosition = new Vector3(0f, 0.01f, 0f);

            PhantomWitchFlatRingMesh ringMesh = shock.AddComponent<PhantomWitchFlatRingMesh>();
            ringMesh.Configure(Mathf.Max(12, segments), 0.01f, 0.28f, GetSharedLineMaterial(unlit), ShockwaveColor);

            PhantomWitchShockwaveAnimation shockAnim = shock.AddComponent<PhantomWitchShockwaveAnimation>();
            shockAnim.Configure(radius * 1.15f, 0.35f, ShockwaveColor);
        }

        // ---------- 发光符文环：LineRenderer 绕轴旋转，宽度可轻微呼吸 ----------
        private static void CreateRingChild(Transform parent, string name, float radius, int segments, float width, Color color, float rotationSpeed, Shader unlit, bool pulse)
        {
            GameObject ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            PhantomWitchFlatRingMesh ringMesh = ring.AddComponent<PhantomWitchFlatRingMesh>();
            ringMesh.Configure(Mathf.Max(12, segments), radius, width, GetSharedLineMaterial(unlit), color);

            PhantomWitchRingSpin spin = ring.AddComponent<PhantomWitchRingSpin>();
            spin.rotationSpeed = rotationSpeed;

            if (pulse)
            {
                PhantomWitchRingPulse pulseAnim = ring.AddComponent<PhantomWitchRingPulse>();
                pulseAnim.Configure(1f, 0.03f, 1.6f, color);
            }
        }

        // ---------- 符文横条：内环上切出几段亮边，随内环一起反向旋转 ----------
        private static void CreateRuneMarks(Transform parent, float radius, int count, Shader unlit)
        {
            if (count <= 0)
            {
                return;
            }

            GameObject runeRoot = new GameObject("RuneMarks");
            runeRoot.transform.SetParent(parent, false);
            runeRoot.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            Material sharedLine = GetSharedLineMaterial(unlit);
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
        private static void CreatePentagram(Transform parent, float radius, Shader unlit)
        {
            GameObject pent = new GameObject("Pentagram");
            pent.transform.SetParent(parent, false);
            pent.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            Material sharedLine = GetSharedLineMaterial(unlit);
            Color pentagramColor = new Color(RingColorInner.r, RingColorInner.g, RingColorInner.b, 0.55f);

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
                CreateFlatSegment(pent.transform, "Edge_" + i, order[i], order[(i + 1) % order.Length], 0.06f, pentagramColor, sharedLine, i * 0.002f);
            }

            PhantomWitchRingSpin spin = pent.AddComponent<PhantomWitchRingSpin>();
            spin.rotationSpeed = RotationSpeedOuter * 0.4f;
        }

        // ---------- 中心脉冲光晕：贴地的四边形，周期缩放 + alpha 呼吸 ----------
        private static void CreateCoreGlow(Transform parent, float radius, Shader unlit, bool enablePulse)
        {
            GameObject core = CreateFlatQuad(parent, "CoreGlow", radius * 2f, 0.12f, CoreGlowColor, unlit);
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

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.9f;
            shape.radiusThickness = 1f;

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
                    new GradientColorKey(new Color(1f, 0.85f, 1f), 0.5f),
                    new GradientColorKey(new Color(0.5f, 0.2f, 0.8f), 1f)
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

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.02f; // 只在边缘

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(SparkColor, 0f),
                    new GradientColorKey(new Color(0.85f, 0.55f, 1f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // 用 texture sheet 简单模拟闪烁（没有贴图就保留静态亮点）
            ps.Play();
        }

        private static void ConfigureDefaultParticleRenderer(ParticleSystem ps)
        {
            // 复用 AssetManager 的共享软圆粒子材质（Additive 混合 + 柔和渐变纹理）
            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);
        }

        private static Material GetSharedLineMaterial(Shader shader)
        {
            if (cachedLineMaterial != null)
            {
                return cachedLineMaterial;
            }

            shader = ResolveShader(shader);
            if (shader == null)
            {
                return null;
            }

            cachedLineMaterial = new Material(shader);
            cachedLineMaterial.name = "PW_CurseRealm_FlatLine";
            cachedLineMaterial.enableInstancing = true;
            if (cachedLineMaterial.HasProperty("_MainTex"))
            {
                cachedLineMaterial.mainTexture = Texture2D.whiteTexture;
            }
            cachedLineMaterial.renderQueue = 3000;
            return cachedLineMaterial;
        }

        private static Material GetSharedQuadMaterial(Shader shader)
        {
            return PhantomWitchAssetManager.GetQuadMaterial();
        }

        private static Material GetSharedParticleMaterial(Shader shader)
        {
            return PhantomWitchAssetManager.GetParticleMaterial();
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
            if (cachedLineMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedLineMaterial);
                cachedLineMaterial = null;
            }

            if (cachedQuadMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedQuadMaterial);
                cachedQuadMaterial = null;
            }

            if (cachedParticleMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedParticleMaterial);
                cachedParticleMaterial = null;
            }

            if (cachedQuadMesh != null)
            {
                UnityEngine.Object.Destroy(cachedQuadMesh);
                cachedQuadMesh = null;
            }
        }

        private static GameObject CreateFlatQuad(Transform parent, string name, float scale, float yOffset, Color color, Shader unlit)
        {
            GameObject quad = new GameObject(name);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, yOffset, 0f);
            quad.transform.localScale = new Vector3(scale, 1f, scale);

            MeshFilter meshFilter = quad.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetSharedQuadMesh();

            MeshRenderer meshRenderer = quad.AddComponent<MeshRenderer>();
            Material sharedQuad = GetSharedQuadMaterial(unlit);
            if (sharedQuad != null)
            {
                meshRenderer.sharedMaterial = sharedQuad;
                PhantomWitchFxRenderUtil.SetRendererColor(meshRenderer, color);
            }

            return quad;
        }

        private static Shader ResolveShader(Shader shader)
        {
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Color");
            return shader;
        }
    }

    /// <summary>符文环绕 Y 轴匀速旋转。</summary>
    internal sealed class PhantomWitchRingSpin : MonoBehaviour
    {
        public float rotationSpeed = 20f;

        private void Update()
        {
            transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }

    internal sealed class PhantomWitchRuntimeMesh : MonoBehaviour
    {
        private Mesh mesh;

        internal void SetMesh(Mesh mesh)
        {
            this.mesh = mesh;
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
        }
    }

    internal sealed class PhantomWitchFlatRingMesh : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Mesh mesh;
        private int segments;

        internal void Configure(int segments, float radius, float width, Material material, Color color)
        {
            this.segments = Mathf.Max(3, segments);
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "PW_CurseRealm_RingMesh";
            }

            meshFilter.sharedMesh = mesh;
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            SetShape(radius, width);
            SetColor(color);
        }

        internal void SetShape(float radius, float width)
        {
            if (mesh == null)
            {
                return;
            }

            PhantomWitchCurseRealmVisual.BuildRingMesh(mesh, radius, width, segments);
        }

        internal void SetColor(Color color)
        {
            if (meshRenderer == null)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            PhantomWitchFxRenderUtil.SetRendererColor(meshRenderer, propertyBlock, color);
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
        }
    }

    /// <summary>LineRenderer 宽度/颜色呼吸，给环增加"能量流动"感。</summary>
    internal sealed class PhantomWitchRingPulse : MonoBehaviour
    {
        private Renderer targetRenderer;
        private MaterialPropertyBlock propertyBlock;
        private float baseScale;
        private float amplitude;
        private float frequency;
        private Color baseColor;
        private Vector3 initialScale;
        private float lastPulseAlpha;

        public void Configure(float baseScale, float amplitude, float frequency, Color baseColor)
        {
            this.targetRenderer = GetComponent<Renderer>();
            this.propertyBlock = new MaterialPropertyBlock();
            this.baseScale = baseScale;
            this.amplitude = amplitude;
            this.frequency = frequency;
            this.baseColor = baseColor;
            this.initialScale = transform.localScale;
            this.lastPulseAlpha = Mathf.Max(0.0001f, baseColor.a);
        }

        private void Update()
        {
            if (targetRenderer == null)
            {
                return;
            }

            float s = 0.5f + 0.5f * Mathf.Sin(Time.time * frequency * Mathf.PI * 2f);
            float factor = baseScale + amplitude * s;
            transform.localScale = initialScale * factor;

            Color currentColor = PhantomWitchFxRenderUtil.GetRendererColor(targetRenderer, propertyBlock);
            float fadeFactor = Mathf.Clamp01(currentColor.a / Mathf.Max(0.0001f, lastPulseAlpha));
            float pulseAlpha = Mathf.Lerp(baseColor.a * 0.7f, baseColor.a, s);
            Color c = baseColor;
            c.a = pulseAlpha * fadeFactor;
            lastPulseAlpha = Mathf.Max(0.0001f, pulseAlpha);
            PhantomWitchFxRenderUtil.SetRendererColor(targetRenderer, propertyBlock, c);
        }
    }

    /// <summary>开场冲击波：半径 0→target，持续很短，自毁。</summary>
    internal sealed class PhantomWitchShockwaveAnimation : MonoBehaviour
    {
        private PhantomWitchFlatRingMesh ringMesh;
        private float targetRadius;
        private float duration;
        private Color baseColor;
        private float elapsed;

        public void Configure(float targetRadius, float duration, Color color)
        {
            this.ringMesh = GetComponent<PhantomWitchFlatRingMesh>();
            this.targetRadius = targetRadius;
            this.duration = duration;
            this.baseColor = color;
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (ringMesh == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // 开场快、尾端收（easeOutQuad）
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0f, targetRadius, eased);
            float width = Mathf.Lerp(0.28f, 0.06f, t);
            ringMesh.SetShape(radius, width);

            Color c = baseColor;
            c.a = baseColor.a * (1f - t);
            ringMesh.SetColor(c);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>中心光晕：正弦缩放 + 透明度呼吸。</summary>
    internal sealed class PhantomWitchCorePulse : MonoBehaviour
    {
        private Material material;
        private Renderer targetRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Color baseColor;
        private float baseScale;
        private float frequency;
        private Vector3 initialScale;

        public void Configure(Material material, Color color, float baseScale, float frequency)
        {
            this.material = material;
            this.baseColor = color;
            this.baseScale = baseScale;
            this.frequency = frequency;
            this.initialScale = transform.localScale;
        }

        public void Configure(Renderer renderer, Color color, float baseScale, float frequency)
        {
            this.material = null;
            this.targetRenderer = renderer;
            this.propertyBlock = new MaterialPropertyBlock();
            this.baseColor = color;
            this.baseScale = baseScale;
            this.frequency = frequency;
            this.initialScale = transform.localScale;
        }

        private void Update()
        {
            float s = 0.5f + 0.5f * Mathf.Sin(Time.time * frequency * Mathf.PI * 2f);
            float factor = Mathf.Lerp(0.8f, 1.15f, s);
            transform.localScale = initialScale * factor;

            if (material != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(baseColor.a * 0.55f, baseColor.a, s);
                material.color = c;
            }
            else if (targetRenderer != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(baseColor.a * 0.55f, baseColor.a, s);
                PhantomWitchFxRenderUtil.SetRendererColor(targetRenderer, propertyBlock, c);
            }
        }
    }

    /// <summary>领域结束前 0.5s 让所有层淡出，避免视觉骤然消失。</summary>
    internal sealed class PhantomWitchCurseRealmFader : MonoBehaviour
    {
        private const float FadeOutDuration = 0.7f;

        private float totalDuration;
        private float elapsed;
        private LineRenderer[] lines;
        private Renderer[] otherRenderers;
        private MaterialPropertyBlock[] otherPropertyBlocks;
        private Color[] lineStartColors;
        private Color[] lineEndColors;
        private Color[] otherBaseColors;

        internal void Initialize(float duration)
        {
            totalDuration = Mathf.Max(0.05f, duration);
            elapsed = 0f;

            lines = GetComponentsInChildren<LineRenderer>(true);
            lineStartColors = new Color[lines.Length];
            lineEndColors = new Color[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                {
                    lineStartColors[i] = lines[i].startColor;
                    lineEndColors[i] = lines[i].endColor;
                }
            }

            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            List<Renderer> rendererList = new List<Renderer>(8);
            List<MaterialPropertyBlock> blockList = new List<MaterialPropertyBlock>(8);
            List<Color> colorList = new List<Color>(8);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer r = allRenderers[i];
                if (r == null || r is LineRenderer || r is ParticleSystemRenderer)
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rendererList.Add(r);
                blockList.Add(block);
                colorList.Add(PhantomWitchFxRenderUtil.GetRendererColor(r, block));
            }
            otherRenderers = rendererList.ToArray();
            otherPropertyBlocks = blockList.ToArray();
            otherBaseColors = colorList.ToArray();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float remaining = totalDuration - elapsed;
            if (remaining >= FadeOutDuration)
            {
                return;
            }

            float alpha = Mathf.Clamp01(remaining / FadeOutDuration);

            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    LineRenderer lr = lines[i];
                    if (lr == null)
                    {
                        continue;
                    }
                    Color start = lineStartColors[i];
                    Color end = lineEndColors[i];
                    start.a *= alpha;
                    end.a *= alpha;
                    lr.startColor = start;
                    lr.endColor = end;
                }
            }

            if (otherRenderers != null)
            {
                for (int i = 0; i < otherRenderers.Length; i++)
                {
                    Renderer renderer = otherRenderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }
                    Color c = otherBaseColors[i];
                    c.a *= alpha;
                    PhantomWitchFxRenderUtil.SetRendererColor(renderer, otherPropertyBlocks[i], c);
                }
            }
        }

        private void OnDestroy()
        {
            otherRenderers = null;
            otherPropertyBlocks = null;
            lines = null;
        }
    }
}

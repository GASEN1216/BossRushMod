using System;
using System.Collections.Generic;
using Duckov.Buffs;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed class DragonKingBossGunGroundZone : MonoBehaviour
    {
        private const float MaxLifetime = 15f;
        private const int MaxActivePoisonZones = 6;
        private const int PoisonZoneMaxParticles = 36;
        private const float PoisonZoneEmissionMin = 14f;
        private const float PoisonZoneEmissionMax = 24f;
        private const float PoisonTickLockDuration = 0.32f;
        private const float PoisonTickKeepTime = 1.2f;
        private const float PoisonTickCleanupInterval = 1f;
        private const float DamageNormalFallbackSqr = 0.0000000001f;
        // VB-17：16 段在 2 m 半径时每段 0.8 m，看得出棱角；40 段约 0.3 m。
        private const int RingSegments = 40;
        private const float RingUpdateInterval = 0.1f;
        /// <summary>到时之后环宽与灯强淡到 0 的时长（VB-17：旧版满亮挂 2 s 再一帧消失）。伤害在到时那一刻就停，与原来一致。</summary>
        private const float FadeOutSeconds = 0.4f;

        private static readonly Dictionary<int, float> poisonTickTimes = new Dictionary<int, float>();
        private static readonly List<int> poisonTickKeysToRemove = new List<int>();
        private static readonly List<DragonKingBossGunGroundZone> activePoisonZones = new List<DragonKingBossGunGroundZone>();
        private static readonly Vector3 RingHeightOffset = Vector3.up * 0.04f;
        private static readonly Vector3[] RingUnitOffsets = BuildRingUnitOffsets();
        private static float lastPoisonTickCleanup;

        private ProjectileContext sourceContext;
        private DragonKingBossGunShotProfile profile;
        private float radius;
        private float duration;
        private float tickDamageFactor;
        private float tickTimer;
        private float elapsed;
        private float pulseTime;
        private float ringUpdateTimer;
        private float lastPulse;
        private float ringBaseWidth;
        private LineRenderer zoneRing;
        private LineRenderer zoneStain;
        private Light zoneLight;
        private Color ringColor;
        private Color stainColor;
        private float lightBaseIntensity;
        private bool fadingOut;
        private float fadeElapsed;

        internal static void ClearStaticCaches()
        {
            // 材质已改由全 Mod 共享工厂持有（BossRushFxMaterials），这里不再建、不再销毁。
            poisonTickTimes.Clear();
            poisonTickKeysToRemove.Clear();
            activePoisonZones.Clear();
        }

        public void Initialize(ProjectileContext projectileContext, DragonKingBossGunShotProfile profile)
        {
            sourceContext = projectileContext;
            this.profile = profile;
            radius = Mathf.Max(0.25f, profile.GroundZoneRadius);
            duration = Mathf.Max(0.2f, profile.GroundZoneDuration);
            tickDamageFactor = Mathf.Max(0.05f, profile.GroundZoneTickDamageFactor);
            tickTimer = 0f;
            elapsed = 0f;
            pulseTime = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            transform.position = FenHuangHalberdRuntime.SnapToGround(transform.position, transform.position.y);

            RegisterZonePerformanceBudget();
            CreateZoneVisual();
        }

        private void RegisterZonePerformanceBudget()
        {
            if (profile == null || profile.GroundZoneElement != ElementTypes.poison)
            {
                return;
            }

            CompactActivePoisonZones();
            while (activePoisonZones.Count >= MaxActivePoisonZones)
            {
                DragonKingBossGunGroundZone oldestZone = activePoisonZones[0];
                activePoisonZones.RemoveAt(0);
                if (oldestZone == null)
                {
                    continue;
                }

                UnityEngine.Object.Destroy(oldestZone.gameObject);
            }

            activePoisonZones.Add(this);
        }

        private static void CompactActivePoisonZones()
        {
            for (int i = activePoisonZones.Count - 1; i >= 0; i--)
            {
                DragonKingBossGunGroundZone zone = activePoisonZones[i];
                if (zone == null)
                {
                    activePoisonZones.RemoveAt(i);
                }
            }
        }

        private void CreateZoneVisual()
        {
            // VB-17：颜色只走顶点色 / 粒子色（旧版材质色 × 顶点色二次相乘，毒区实际是 (0.006,0.18,0.006) 的近黑绿方块）。
            Color zoneColor;
            switch (profile.GroundZoneElement)
            {
                case ElementTypes.fire:
                    zoneColor = new Color(1f, 0.5f, 0.18f, 0.7f);
                    stainColor = new Color(0.35f, 0.12f, 0.04f, 0.18f);
                    break;
                case ElementTypes.poison:
                    zoneColor = new Color(0.35f, 0.62f, 0.22f, 0.6f);
                    stainColor = new Color(0.16f, 0.30f, 0.10f, 0.22f);
                    break;
                case ElementTypes.ice:
                    zoneColor = new Color(0.42f, 0.86f, 1f, 0.75f);
                    stainColor = new Color(0.55f, 0.80f, 0.95f, 0.15f);
                    break;
                default:
                    return;
            }

            ringColor = zoneColor;
            CreateZoneStain();
            CreateZoneRing(zoneColor);
            if (profile.GroundZoneElement != ElementTypes.poison)
            {
                CreateZoneLight(zoneColor);
            }

            GameObject fxObj = new GameObject("ZoneRingFx");
            fxObj.transform.SetParent(transform);
            fxObj.transform.localPosition = Vector3.zero;

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.duration = duration;
            main.startLifetime = Mathf.Min(0.6f, duration * 0.5f);
            main.startSpeed = 0.18f;
            float baseSize = Mathf.Clamp(radius * 0.14f, 0.08f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(baseSize * 0.7f, baseSize * 1.2f);
            main.startColor = zoneColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = profile.GroundZoneElement == ElementTypes.poison ? PoisonZoneMaxParticles : 72;

            var emission = ps.emission;
            float emissionMin = profile.GroundZoneElement == ElementTypes.poison ? PoisonZoneEmissionMin : 22f;
            float emissionMax = profile.GroundZoneElement == ElementTypes.poison ? PoisonZoneEmissionMax : 42f;
            emission.rateOverTime = Mathf.Lerp(emissionMin, emissionMax, Mathf.InverseLerp(0.5f, 2f, radius));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.2f;
            // Circle 默认竖在局部 XY 平面（VB-04 / VB-17）：转 90° 放平到地面上。
            shape.rotation = new Vector3(90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            Material particleMaterial = GetZoneParticleMaterial(profile.GroundZoneElement);
            if (particleMaterial != null)
            {
                renderer.sharedMaterial = particleMaterial;
            }
            else
            {
                renderer.enabled = false;
            }

            ps.Play();
        }

        /// <summary>火区的火星发光（加色），毒雾与冰雾是普通半透明。材质归共享工厂所有。</summary>
        private static Material GetZoneParticleMaterial(ElementTypes element)
        {
            return DragonKingFxShared.Soft(element == ElementTypes.fire ? BossRushFxBlend.Additive : BossRushFxBlend.Alpha);
        }

        /// <summary>
        /// 贴地的一块软色面（VB-17）：两点直线 + 带宽 = 直径，软圆贴图拉满就是一块中心浓、边缘散的圆，
        /// TransformZ + 绕 X 转 90° 让它躺在地面上。毒区读作「一滩毒」，火区是焦痕，冰区是一层霜。
        /// </summary>
        private void CreateZoneStain()
        {
            Material material = DragonKingFxShared.Soft(BossRushFxBlend.Alpha);
            if (material == null)
            {
                return;
            }

            GameObject stainObj = new GameObject("ZoneStain");
            stainObj.transform.SetParent(transform, false);
            stainObj.transform.localPosition = RingHeightOffset * 0.5f;
            stainObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            zoneStain = stainObj.AddComponent<LineRenderer>();
            zoneStain.useWorldSpace = false;
            zoneStain.loop = false;
            zoneStain.positionCount = 2;
            zoneStain.numCapVertices = 0;
            zoneStain.numCornerVertices = 0;
            zoneStain.alignment = LineAlignment.TransformZ;
            zoneStain.textureMode = LineTextureMode.Stretch;
            zoneStain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            zoneStain.receiveShadows = false;
            zoneStain.sharedMaterial = material;
            float stainRadius = radius * 1.1f;
            zoneStain.SetPosition(0, new Vector3(-stainRadius, 0f, 0f));
            zoneStain.SetPosition(1, new Vector3(stainRadius, 0f, 0f));
            zoneStain.widthMultiplier = stainRadius * 2f;
            zoneStain.startColor = stainColor;
            zoneStain.endColor = stainColor;
        }

        private void CreateZoneRing(Color zoneColor)
        {
            GameObject ringObj = new GameObject("ZoneRingLine");
            ringObj.transform.SetParent(transform, false);

            zoneRing = ringObj.AddComponent<LineRenderer>();
            zoneRing.useWorldSpace = false;
            zoneRing.loop = true;
            zoneRing.positionCount = RingSegments;
            zoneRing.numCapVertices = 2;
            zoneRing.numCornerVertices = 2;
            zoneRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            zoneRing.receiveShadows = false;
            zoneRing.textureMode = LineTextureMode.Stretch;
            zoneRing.startColor = zoneColor;
            zoneRing.endColor = zoneColor;

            // 软边带材质（共享工厂），颜色只走顶点色；用 sharedMaterial，不给每个区实例化一份材质。
            Material ringMaterial = DragonKingFxShared.Band(BossRushFxBlend.Alpha);
            if (ringMaterial != null)
            {
                zoneRing.sharedMaterial = ringMaterial;
            }
            else
            {
                zoneRing.enabled = false;
            }
            ringBaseWidth = Mathf.Clamp(radius * 0.08f, 0.08f, 0.18f);
            zoneRing.widthMultiplier = ringBaseWidth;
            UpdateZoneRing(radius);
        }

        private void CreateZoneLight(Color zoneColor)
        {
            zoneLight = gameObject.AddComponent<Light>();
            zoneLight.type = LightType.Point;
            zoneLight.color = zoneColor;
            // VB-17：照亮范围 = 判定半径 + 1 m（旧值 3.25 倍半径，地上的亮区比判定区大一圈）。
            zoneLight.range = radius + 1f;
            zoneLight.intensity = Mathf.Lerp(1.1f, 2f, Mathf.InverseLerp(0.5f, 2f, radius));
            zoneLight.shadows = LightShadows.None;
        }

        private void UpdateZoneRing(float ringRadius)
        {
            if (zoneRing == null)
            {
                return;
            }

            for (int i = 0; i < RingUnitOffsets.Length; i++)
            {
                Vector3 offset = RingUnitOffsets[i] * ringRadius;
                zoneRing.SetPosition(i, offset + RingHeightOffset);
            }
        }

        private static Vector3[] BuildRingUnitOffsets()
        {
            Vector3[] offsets = new Vector3[RingSegments];
            for (int i = 0; i < offsets.Length; i++)
            {
                float angle = 360f * i / RingSegments;
                offsets[i] = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            }

            return offsets;
        }

        private void Update()
        {
            if (fadingOut)
            {
                UpdateFadeOut();
                return;
            }

            elapsed += Time.deltaTime;
            tickTimer += Time.deltaTime;
            pulseTime += Time.deltaTime * 3.5f;
            ringUpdateTimer += Time.deltaTime;

            if (ringUpdateTimer >= RingUpdateInterval)
            {
                ringUpdateTimer = 0f;
                float pulseSin = Mathf.Sin(pulseTime);
                float pulse = 1f + pulseSin * 0.08f;
                if (zoneRing != null && Mathf.Abs(pulse - lastPulse) > 0.005f)
                {
                    lastPulse = pulse;
                    zoneRing.widthMultiplier = ringBaseWidth * pulse;
                    UpdateZoneRing(radius * pulse);
                }

                if (zoneLight != null)
                {
                    zoneLight.intensity = Mathf.Lerp(1f, 2.2f, Mathf.InverseLerp(-1f, 1f, pulseSin));
                }
            }

            if (tickTimer >= 0.35f)
            {
                tickTimer = 0f;
                TickZone();
            }

            if (elapsed >= duration || elapsed >= MaxLifetime)
            {
                // 到时：伤害就此停止（不再 TickZone），粒子停发射、2 s 后销毁照旧；环宽、环色与灯在 0.4 s 内淡到 0。
                DragonKingBossGunProjectileAgent.FadeAndDestroy(gameObject, 2f);
                fadingOut = true;
                fadeElapsed = 0f;
                lightBaseIntensity = zoneLight != null ? zoneLight.intensity : 0f;
            }
        }

        private void UpdateFadeOut()
        {
            fadeElapsed += Time.deltaTime;
            float k = 1f - BossRushUI.SmoothStep(fadeElapsed / FadeOutSeconds);
            if (zoneRing != null)
            {
                zoneRing.widthMultiplier = ringBaseWidth * k;
                Color c = ringColor;
                c.a *= k;
                zoneRing.startColor = c;
                zoneRing.endColor = c;
            }
            if (zoneStain != null)
            {
                Color c = stainColor;
                c.a *= k;
                zoneStain.startColor = c;
                zoneStain.endColor = c;
            }
            if (zoneLight != null)
            {
                zoneLight.intensity = lightBaseIntensity * k;
            }
            if (fadeElapsed >= FadeOutSeconds)
            {
                if (zoneRing != null) zoneRing.enabled = false;
                if (zoneStain != null) zoneStain.enabled = false;
                if (zoneLight != null) zoneLight.enabled = false;
                enabled = false;
            }
        }

        private void TickZone()
        {
            Buff buff = GetZoneBuff();

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, DragonKingBossGunRuntime.SharedColliderBuffer, GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Ignore);
            DragonKingBossGunRuntime.SharedReceiverIdSet.Clear();
            for (int i = 0; i < count; i++)
            {
                DamageReceiver receiver = DragonKingBossGunRuntime.SharedColliderBuffer[i] != null ? DragonKingBossGunRuntime.SharedColliderBuffer[i].GetComponent<DamageReceiver>() : null;
                if (receiver == null)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (DragonKingBossGunRuntime.SharedReceiverIdSet.Contains(receiverId))
                {
                    continue;
                }

                if (sourceContext.team == receiver.Team && receiver.Team != Teams.all)
                {
                    continue;
                }

                CharacterMainControl receiverCharacter = receiver.health != null ? receiver.health.TryGetCharacter() : null;
                if (receiverCharacter != null && receiverCharacter == sourceContext.realFromCharacter)
                {
                    continue;
                }

                if (profile.GroundZoneElement == ElementTypes.poison && !TryClaimPoisonTick(receiverId))
                {
                    continue;
                }

                DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiverId);
                Vector3 damagePoint = receiver.transform.position + Vector3.up * 0.35f;
                Vector3 damageNormal = receiver.transform.position - transform.position;
                if (damageNormal.sqrMagnitude <= DamageNormalFallbackSqr)
                {
                    damageNormal = Vector3.up;
                }

                DamageInfo damageInfo = DragonKingBossGunRuntime.CreateDamageInfo(sourceContext, tickDamageFactor, damagePoint, damageNormal, true, true);
                if (profile.GroundZoneElement == ElementTypes.poison)
                {
                    damageInfo.damageValue = Mathf.Min(damageInfo.damageValue, 1f);
                }

                receiver.Hurt(damageInfo);
                receiver.AddBuff(GameplayDataSettings.Buffs.Pain, sourceContext.fromCharacter);

                if (buff != null)
                {
                    receiver.AddBuff(buff, sourceContext.fromCharacter);
                }
            }
        }

        private Buff GetZoneBuff()
        {
            if (profile == null)
            {
                return null;
            }

            switch (profile.GroundZoneElement)
            {
                case ElementTypes.fire:
                    return GameplayDataSettings.Buffs.Burn;
                case ElementTypes.ice:
                    return GameplayDataSettings.Buffs.Cold;
                case ElementTypes.poison:
                    return GameplayDataSettings.Buffs.Poison;
                default:
                    return null;
            }
        }

        private static bool TryClaimPoisonTick(int receiverId)
        {
            if (receiverId == 0)
            {
                return true;
            }

            float now = Time.time;
            float lastTickTime;
            if (poisonTickTimes.TryGetValue(receiverId, out lastTickTime) && now - lastTickTime < PoisonTickLockDuration)
            {
                CleanupPoisonTickClaims(now);
                return false;
            }

            poisonTickTimes[receiverId] = now;
            CleanupPoisonTickClaims(now);
            return true;
        }

        private static void CleanupPoisonTickClaims(float now)
        {
            if (poisonTickTimes.Count == 0 || now - lastPoisonTickCleanup < PoisonTickCleanupInterval)
            {
                return;
            }

            lastPoisonTickCleanup = now;
            poisonTickKeysToRemove.Clear();
            foreach (var kvp in poisonTickTimes)
            {
                if (now - kvp.Value >= PoisonTickKeepTime)
                {
                    poisonTickKeysToRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < poisonTickKeysToRemove.Count; i++)
            {
                poisonTickTimes.Remove(poisonTickKeysToRemove[i]);
            }
        }

        private void OnDestroy()
        {
            activePoisonZones.Remove(this);
            // 材质归共享工厂所有，不在单个 zone 销毁时 Destroy。
        }
    }

    internal sealed class DragonKingBossGunStickyCharge : MonoBehaviour
    {
        private const float MaxLifetime = 10f;

        private ProjectileContext sourceContext;
        private DragonKingBossGunShotProfile profile;
        private int shotId;
        private Transform followTarget;
        private Transform cachedTransform;
        private Vector3 localOffset;
        private float elapsed;

        private float CleanupDelay
        {
            get
            {
                return profile != null ? Mathf.Clamp(profile.ExplosionFxDuration, 0.1f, 2f) : 0.35f;
            }
        }

        private Transform CachedTransform
        {
            get
            {
                if (cachedTransform == null)
                {
                    cachedTransform = transform;
                }

                return cachedTransform;
            }
        }

        public void Initialize(ProjectileContext projectileContext, DragonKingBossGunShotProfile shotProfile, int currentShotId, Transform target, Vector3 worldPoint)
        {
            sourceContext = projectileContext;
            profile = shotProfile;
            shotId = currentShotId;
            followTarget = target;
            localOffset = target != null ? target.InverseTransformPoint(worldPoint) : Vector3.zero;
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            Transform selfTransform = CachedTransform;
            Vector3 chargePosition = selfTransform.position;
            if (followTarget != null)
            {
                chargePosition = followTarget.TransformPoint(localOffset);
                selfTransform.position = chargePosition;
            }

            if (profile == null || elapsed < Mathf.Max(0.05f, profile.StickyDelay))
            {
                if (elapsed >= MaxLifetime)
                {
                    DragonKingBossGunProjectileAgent.FadeAndDestroy(gameObject, CleanupDelay);
                    enabled = false;
                }

                return;
            }

            if (profile.PlayObstacleHitFx)
            {
                DragonKingBossGunRuntime.TrySpawnExplosionFx(chargePosition, profile);
            }
            float marker = DragonKingBossGunRuntime.EncodeShotMarker(
                shotId,
                profile.Id,
                DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary);
            DragonKingBossGunRuntime.ApplyRadiusDamage(
                chargePosition,
                Mathf.Max(0.4f, profile.StickyExplosionRange),
                sourceContext,
                Mathf.Max(0.2f, profile.StickyExplosionDamageFactor),
                false,
                true,
                marker);
            DragonKingBossGunProjectileAgent.FadeAndDestroy(gameObject, CleanupDelay);
            enabled = false;
        }
    }
}

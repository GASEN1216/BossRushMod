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
        private const int RingSegments = 16;
        private const float RingUpdateInterval = 0.1f;

        private static readonly Dictionary<int, float> poisonTickTimes = new Dictionary<int, float>();
        private static readonly List<int> poisonTickKeysToRemove = new List<int>();
        private static readonly List<DragonKingBossGunGroundZone> activePoisonZones = new List<DragonKingBossGunGroundZone>();
        private static readonly Vector3 RingHeightOffset = Vector3.up * 0.04f;
        private static readonly Vector3[] RingUnitOffsets = BuildRingUnitOffsets();
        private static float lastPoisonTickCleanup;
        private static Shader cachedZoneShader;
        private static readonly Dictionary<ElementTypes, Material> cachedZoneMaterials = new Dictionary<ElementTypes, Material>();

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
        private Material zoneRingMaterial;
        private Light zoneLight;

        internal static void ClearStaticCaches()
        {
            foreach (var kvp in cachedZoneMaterials)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            cachedZoneMaterials.Clear();
            cachedZoneShader = null;
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
            Color zoneColor;
            switch (profile.GroundZoneElement)
            {
                case ElementTypes.fire:
                    zoneColor = new Color(1f, 0.4f, 0.1f, 0.7f);
                    break;
                case ElementTypes.poison:
                    zoneColor = new Color(0.08f, 0.42f, 0.08f, 0.82f);
                    break;
                case ElementTypes.ice:
                    zoneColor = new Color(0.42f, 0.86f, 1f, 0.82f);
                    break;
                default:
                    return;
            }

            EnsureZoneShaderCached();
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
            main.startSize = Mathf.Clamp(radius * 0.14f, 0.08f, 0.22f);
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

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(zoneColor, 0f), new GradientColorKey(zoneColor, 0.7f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetOrCreateZoneMaterial(profile.GroundZoneElement, zoneColor);

            ps.Play();
        }

        private static void EnsureZoneShaderCached()
        {
            if (cachedZoneShader != null)
            {
                return;
            }

            cachedZoneShader = Shader.Find("Sprites/Default");
            if (cachedZoneShader == null) cachedZoneShader = Shader.Find("Unlit/Color");
            if (cachedZoneShader == null) cachedZoneShader = Shader.Find("Standard");
        }

        private static Material GetOrCreateZoneMaterial(ElementTypes element, Color color)
        {
            Material mat;
            if (cachedZoneMaterials.TryGetValue(element, out mat) && mat != null)
            {
                return mat;
            }

            EnsureZoneShaderCached();
            mat = new Material(cachedZoneShader);
            mat.color = color;
            cachedZoneMaterials[element] = mat;
            return mat;
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

            zoneRingMaterial = GetOrCreateZoneMaterial(profile.GroundZoneElement, zoneColor);
            zoneRing.material = zoneRingMaterial;
            ringBaseWidth = Mathf.Clamp(radius * 0.08f, 0.08f, 0.18f);
            zoneRing.widthMultiplier = ringBaseWidth;
            UpdateZoneRing(radius);
        }

        private void CreateZoneLight(Color zoneColor)
        {
            zoneLight = gameObject.AddComponent<Light>();
            zoneLight.type = LightType.Point;
            zoneLight.color = zoneColor;
            zoneLight.range = Mathf.Max(2.2f, radius * 3.25f);
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
                DragonKingBossGunProjectileAgent.FadeAndDestroy(gameObject, 2f);
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

            // Material 已改为静态共享缓存，不在单个 zone 销毁时 Destroy
            zoneRingMaterial = null;
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

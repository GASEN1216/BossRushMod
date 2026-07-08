using System;
using System.Collections.Generic;
using Duckov.Buffs;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed class DragonKingBossGunProjectileAgent : MonoBehaviour
    {
        private enum DragonKingBossGunProjectileDeathReason
        {
            None = 0,
            DamageReceiver = 1,
            Obstacle = 2,
            MaxDistance = 3,
            Airburst = 4,
            StickyAttach = 5
        }

        private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); }
        }

        private struct TraceTargetCandidate
        {
            public CharacterMainControl Character;
            public Transform Transform;

            public bool IsValid
            {
                get { return Character != null || Transform != null; }
            }
        }

        private const int RaycastBufferSize = 32;
        private static readonly RaycastHit[] raycastBuffer = new RaycastHit[RaycastBufferSize];

        private static readonly AccessTools.FieldRef<Projectile, bool> deadRef = AccessTools.FieldRefAccess<Projectile, bool>("dead");
        private static readonly AccessTools.FieldRef<Projectile, bool> overMaxDistanceRef = AccessTools.FieldRefAccess<Projectile, bool>("overMaxDistance");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> directionRef = AccessTools.FieldRefAccess<Projectile, Vector3>("direction");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> velocityRef = AccessTools.FieldRefAccess<Projectile, Vector3>("velocity");
        private static readonly AccessTools.FieldRef<Projectile, float> traveledDistanceRef = AccessTools.FieldRefAccess<Projectile, float>("traveledDistance");
        private static readonly AccessTools.FieldRef<Projectile, float> distanceThisFrameRef = AccessTools.FieldRefAccess<Projectile, float>("_distanceThisFrame");
        private static readonly AccessTools.FieldRef<Projectile, bool> firstFrameRef = AccessTools.FieldRefAccess<Projectile, bool>("firstFrame");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> startPointRef = AccessTools.FieldRefAccess<Projectile, Vector3>("startPoint");
        private static readonly AccessTools.FieldRef<Projectile, LayerMask> hitLayersRef = AccessTools.FieldRefAccess<Projectile, LayerMask>("hitLayers");

        internal static void ClearStaticCaches()
        {
            cachedIceMaterial = null;
            cachedFireworkMaterial = null;
            DragonKingBossGunGroundZone.ClearStaticCaches();
        }

        private static Material cachedIceMaterial;
        private static Material cachedFireworkMaterial;
        private static readonly Color[] FireworkPalette =
        {
            new Color(1f, 0.28f, 0.18f),
            new Color(1f, 0.82f, 0.22f),
            new Color(0.24f, 0.95f, 0.72f),
            new Color(0.32f, 0.72f, 1f),
            new Color(0.82f, 0.36f, 1f),
            new Color(1f, 0.36f, 0.78f)
        };

        private Projectile projectile;
        private ItemAgent_Gun sourceGun;
        private DragonKingBossGunShotProfile profile;
        private int shotId;
        private int projectileIndex;
        private bool secondaryProjectile;
        private bool returning;
        private bool splitTriggered;
        private bool deathHandled;
        private float customTraceLerp;
        private float airburstDistance;
        private int remainingBounce;
        private int remainingTargetHits;
        private int successfulHits;
        private Vector3 lastHelixOffset;
        private GameObject customTrailInstance;
        private TrailRenderer iceBladeTrailRenderer;
        private GameObject savedExplosionFx;
        private bool stopMovementThisFrame;
        private float splitActivationTimer;
        private bool splitActivated = true;
        private int splitSourceReceiverId = -1;
        private float traceRefreshTimer;
        private bool mandatorySplitTraceRefresh;
        private Transform explicitTraceTargetTransform;
        private Vector3 splitOrbitCenter;
        private Vector3 splitOrbitAxis;
        private Vector3 splitOrbitBaseOffset;
        private int lastHitReceiverId = -1;
        private Vector3 returnTargetPoint;
        private bool stickyAttached;
        private Transform stickyFollowTarget;
        private Vector3 stickyLocalOffset;
        private Vector3 stickyWorldPoint;
        private Vector3 stickyNormal;
        private float stickyElapsed;
        private GameObject stickyFireFxInstance;
        private DragonKingBossGunProjectileDeathReason deathReason;
        private Vector3 deathPoint;
        private Vector3 deathNormal;
        private bool hasDeathContext;
        private bool deathGroundImpact;
        private float rollingElapsed;
        private Vector3 rollingBaseScale = Vector3.one;
        private float rollingBaseRadius;
        private float rollingCurrentScaleFactor = 1f;
        private readonly HashSet<int> damagedReceiverIds = new HashSet<int>();

        public bool IsActiveForRuntime
        {
            get { return projectile != null && profile != null; }
        }

        public bool UsesCustomMovement
        {
            get
            {
                return IsActiveForRuntime &&
                       (profile.RequiresCustomMovement ||
                        profile.UseSplit ||
                        profile.UseGroundZone ||
                        profile.UseSticky ||
                        profile.UseReturn ||
                        profile.Bounce > 0 ||
                        profile.PierceDamageDecay != null ||
                        profile.UseHelix ||
                        profile.Arc != DragonKingBossGunArcMode.None ||
                        profile.TraceAbility > 0.01f ||
                        secondaryProjectile);
            }
        }

        public bool IsDead
        {
            get { return projectile != null && deadRef(projectile); }
        }

        private void BeginStickyAttachment(DamageReceiver target, GameObject hitObject, Vector3 point, Vector3 normal)
        {
            stickyAttached = true;
            stickyFollowTarget = target != null ? target.transform : (hitObject != null ? hitObject.transform : null);
            stickyLocalOffset = stickyFollowTarget != null ? stickyFollowTarget.InverseTransformPoint(point) : Vector3.zero;
            stickyWorldPoint = point;
            stickyNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : -directionRef(projectile);
            stickyElapsed = 0f;

            transform.position = point;
            transform.rotation = ResolveStickyRotation(normal);
            velocityRef(projectile) = Vector3.zero;
            distanceThisFrameRef(projectile) = 0f;
            stopMovementThisFrame = true;

            EnsureStickyFireFx();
        }

        private Quaternion ResolveStickyRotation(Vector3 normal)
        {
            Vector3 forward = directionRef(projectile);
            if (forward.sqrMagnitude <= 0.001f)
            {
                forward = normal.sqrMagnitude > 0.001f ? -normal.normalized : transform.forward;
            }

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void EnsureStickyFireFx()
        {
            if (stickyFireFxInstance != null || profile == null || profile.Element != ElementTypes.fire)
            {
                return;
            }

            stickyFireFxInstance = new GameObject("DragonGun_StickyFireFx");
            stickyFireFxInstance.transform.SetParent(transform);
            stickyFireFxInstance.transform.localPosition = Vector3.zero;
            stickyFireFxInstance.transform.localRotation = Quaternion.identity;
            DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(stickyFireFxInstance);
            StripPhysicsComponents(stickyFireFxInstance);
        }

        private void UpdateStickyAttachment(float deltaTime)
        {
            stickyElapsed += deltaTime;
            Vector3 attachPoint = stickyWorldPoint;
            if (stickyFollowTarget != null)
            {
                attachPoint = stickyFollowTarget.TransformPoint(stickyLocalOffset);
                stickyWorldPoint = attachPoint;
            }

            transform.position = attachPoint;
            velocityRef(projectile) = Vector3.zero;
            distanceThisFrameRef(projectile) = 0f;

            if (stickyElapsed < Mathf.Max(0.05f, profile.StickyDelay))
            {
                return;
            }

            DetonateStickyAttachment(attachPoint);
        }

        private void DetonateStickyAttachment(Vector3 attachPoint)
        {
            stickyAttached = false;
            SetDeathContext(DragonKingBossGunProjectileDeathReason.StickyAttach, attachPoint, stickyNormal);
            if (profile.PlayObstacleHitFx)
            {
                DragonKingBossGunRuntime.TrySpawnExplosionFx(attachPoint, profile);
            }
            float marker = DragonKingBossGunRuntime.EncodeShotMarker(
                shotId,
                profile.Id,
                DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary);
            DragonKingBossGunRuntime.ApplyRadiusDamage(
                attachPoint,
                Mathf.Max(0.4f, profile.StickyExplosionRange),
                projectile.context,
                Mathf.Max(0.2f, profile.StickyExplosionDamageFactor),
                false,
                true,
                marker);

            if (stickyFireFxInstance != null)
            {
                UnityEngine.Object.Destroy(stickyFireFxInstance);
                stickyFireFxInstance = null;
            }

            ProjectileContext context = projectile.context;
            context.explosionRange = 0f;
            context.explosionDamage = 0f;
            projectile.context = context;
            deathHandled = true;
            deadRef(projectile) = true;
        }


        private void SpawnFireHitEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject fireFx = new GameObject("DragonGun_FireHitFx");
            fireFx.transform.position = hitPoint;
            fireFx.transform.rotation = Quaternion.LookRotation(hitNormal);
            DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(fireFx);

            ParticleSystem[] particles = fireFx.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particles)
            {
                var main = ps.main;
                main.loop = false;
                main.duration = 0.2f;

                var em = ps.emission;
                em.rateOverDistance = 0;
                em.rateOverTime = 0;
                em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 15) });

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }

            UnityEngine.Object.Destroy(fireFx, 1.5f);
        }

        public void Initialize(Projectile projectileInstance, ItemAgent_Gun gunAgent, DragonKingBossGunShotProfile shotProfile, int currentShotId, int currentProjectileIndex, bool isSecondary, int sourceReceiverId = -1)
        {
            projectile = projectileInstance;
            sourceGun = gunAgent;
            profile = shotProfile;
            shotId = currentShotId;
            projectileIndex = currentProjectileIndex;
            secondaryProjectile = isSecondary;
            returning = false;
            splitTriggered = false;
            deathHandled = false;
            customTraceLerp = 0f;
            airburstDistance = ResolveAirburstDistance();
            remainingBounce = profile != null ? Mathf.Max(0, profile.Bounce) : 0;
            remainingTargetHits = projectile != null ? Mathf.Max(1, projectile.context.penetrate + 1) : 1;
            successfulHits = 0;
            if (profile != null && profile.UseReturn)
            {
                remainingTargetHits = Mathf.Max(2, remainingTargetHits);
            }

            lastHelixOffset = Vector3.zero;
            stopMovementThisFrame = false;
            splitActivationTimer = 0f;
            splitActivated = !isSecondary || profile == null || (profile.SplitActivationDelay <= 0f && profile.SplitInvulnerableDuration <= 0f && profile.SplitGravity <= 0f);
            splitSourceReceiverId = sourceReceiverId;
            traceRefreshTimer = 0f;
            mandatorySplitTraceRefresh = isSecondary &&
                                         profile != null &&
                                         profile.Id == DragonKingBossGunProfileId.Energy &&
                                         profile.SplitTraceAbility > 0.01f;
            explicitTraceTargetTransform = null;
            InitializeSplitOrbit(projectileInstance);
            lastHitReceiverId = -1;
            returnTargetPoint = projectileInstance != null ? projectileInstance.transform.position : Vector3.zero;
            deathReason = DragonKingBossGunProjectileDeathReason.None;
            deathPoint = Vector3.zero;
            deathNormal = Vector3.up;
            hasDeathContext = false;
            deathGroundImpact = false;
            rollingElapsed = 0f;
            rollingBaseScale = projectileInstance != null ? projectileInstance.transform.localScale : Vector3.one;
            rollingBaseRadius = projectileInstance != null ? projectileInstance.radius : 0f;
            rollingCurrentScaleFactor = 1f;
            damagedReceiverIds.Clear();

            savedExplosionFx = projectileInstance != null ? projectileInstance.explosionFx : null;
            bool usesNativeVisual = profile != null && profile.UseNativeProjectile && secondaryProjectile;
            if (projectileInstance != null && !usesNativeVisual)
            {
                projectileInstance.explosionFx = null;
            }

            if (customTrailInstance == null && profile != null && !usesNativeVisual && !string.IsNullOrEmpty(profile.TrailFxPrefab))
            {
                customTrailInstance = DragonKingAssetManager.InstantiateEffect(profile.TrailFxPrefab, transform.position, transform.rotation, transform);
            }
            else if (customTrailInstance == null && profile != null && !usesNativeVisual && profile.Id == DragonKingBossGunProfileId.Firework)
            {
                customTrailInstance = CreateFireworkTrail();
            }
            else if (customTrailInstance == null && profile != null && !usesNativeVisual && profile.Element == ElementTypes.fire)
            {
                customTrailInstance = new GameObject("DragonGun_FireTrailFx");
                customTrailInstance.transform.SetParent(transform);
                customTrailInstance.transform.localPosition = Vector3.zero;
                customTrailInstance.transform.localRotation = Quaternion.identity;
                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(customTrailInstance);
            }

            if (customTrailInstance != null)
            {
                customTrailInstance.transform.localPosition = Vector3.zero;
                customTrailInstance.transform.localRotation = Quaternion.identity;
                StripPhysicsComponents(customTrailInstance);
            }

            if (profile != null && profile.Id == DragonKingBossGunProfileId.IceBlade)
            {
                CreateIceBladeTrail();
            }
            else
            {
                DisableIceBladeTrail();
            }

            // 弹体视觉缩放已在 SpawnDragonProjectile 中通过 transform.localScale 处理。
            ApplyRollingSnowballVisual(0f);
            enabled = true;
        }

        private float ResolveAirburstDistance()
        {
            if (projectile == null || profile == null || !profile.SplitOnAirburst)
            {
                return float.MaxValue;
            }

            float fallbackDistance = projectile.context.distance * Mathf.Clamp01(profile.AirburstDistanceFactor);
            if (secondaryProjectile || sourceGun == null || sourceGun.Holder == null || !sourceGun.Holder.IsMainCharacter)
            {
                return fallbackDistance;
            }

            try
            {
                Vector3 aimPoint = sourceGun.Holder.GetCurrentAimPoint();
                float aimedDistance = Vector3.Distance(projectile.context.firstFrameCheckStartPoint, aimPoint);
                if (float.IsNaN(aimedDistance) || float.IsInfinity(aimedDistance) || aimedDistance <= 0f)
                {
                    return fallbackDistance;
                }

                return Mathf.Clamp(aimedDistance, 0.5f, Mathf.Max(0.5f, projectile.context.distance));
            }
            catch
            {
                return fallbackDistance;
            }
        }

        private void CreateIceBladeTrail()
        {
            TrailRenderer trail = iceBladeTrailRenderer != null ? iceBladeTrailRenderer : gameObject.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = gameObject.AddComponent<TrailRenderer>();
            }

            iceBladeTrailRenderer = trail;
            trail.enabled = true;
            trail.Clear();
            trail.time = 0.34f;
            trail.startWidth = 0.34f;
            trail.endWidth = 0.035f;
            trail.startColor = new Color(0.55f, 0.92f, 1f, 0.92f);
            trail.endColor = new Color(0.2f, 0.55f, 1f, 0f);
            trail.sharedMaterial = GetOrCreateIceMaterial();
            trail.numCornerVertices = 4;
            trail.numCapVertices = 4;
            trail.minVertexDistance = 0.025f;
        }

        private void DisableIceBladeTrail()
        {
            if (iceBladeTrailRenderer == null)
            {
                return;
            }

            iceBladeTrailRenderer.Clear();
            iceBladeTrailRenderer.enabled = false;
        }

        private GameObject CreateFireworkTrail()
        {
            bool spark = secondaryProjectile;
            Color color = ResolveFireworkColor(spark ? projectileIndex : shotId, spark);
            Color hotColor = Color.Lerp(Color.white, color, spark ? 0.35f : 0.2f);

            GameObject trailObject = new GameObject(spark ? "DragonGun_FireworkSparkTrailFx" : "DragonGun_FireworkShellTrailFx");
            trailObject.transform.SetParent(transform);
            trailObject.transform.localPosition = Vector3.zero;
            trailObject.transform.localRotation = Quaternion.identity;

            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = spark ? 0.58f : 0.46f;
            trail.startWidth = spark ? 0.11f : 0.22f;
            trail.endWidth = 0.018f;
            trail.startColor = WithAlpha(hotColor, spark ? 0.92f : 0.98f);
            trail.endColor = WithAlpha(color, 0f);
            trail.sharedMaterial = GetOrCreateFireworkMaterial();
            trail.numCornerVertices = 4;
            trail.numCapVertices = 4;
            trail.minVertexDistance = 0.025f;

            ParticleSystem ps = trailObject.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.duration = 0.35f;
            main.startLifetime = spark ? new ParticleSystem.MinMaxCurve(0.18f, 0.36f) : new ParticleSystem.MinMaxCurve(0.26f, 0.5f);
            main.startSpeed = spark ? new ParticleSystem.MinMaxCurve(0.25f, 0.9f) : new ParticleSystem.MinMaxCurve(0.08f, 0.45f);
            main.startSize = spark ? new ParticleSystem.MinMaxCurve(0.025f, 0.07f) : new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(hotColor, 0.88f), WithAlpha(color, 0.72f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = spark ? 36 : 28;

            var emission = ps.emission;
            emission.rateOverTime = spark ? 24f : 18f;
            emission.rateOverDistance = spark ? 8f : 5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = spark ? 0.035f : 0.055f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(hotColor, 0f), new GradientColorKey(color, 0.45f), new GradientColorKey(color, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.86f, 0f), new GradientAlphaKey(0.52f, 0.45f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.18f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = spark ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = spark ? 0.8f : 0.45f;
            renderer.velocityScale = spark ? 0.18f : 0.08f;
            renderer.sharedMaterial = GetOrCreateFireworkMaterial();

            ps.Play(true);
            return trailObject;
        }

        private void SpawnFireworkBloomEffect(Vector3 position)
        {
            GameObject fx = new GameObject("DragonGun_FireworkBloomFx");
            fx.transform.position = position;

            Color colorA = ResolveFireworkColor(shotId, true);
            Color colorB = ResolveFireworkColor(shotId + 3, true);
            Color flashColor = new Color(1f, 0.92f, 0.55f);

            ParticleSystem burst = fx.AddComponent<ParticleSystem>();
            var main = burst.main;
            main.loop = false;
            main.duration = 0.08f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.86f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, 7.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.105f);
            main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(colorA, 0.95f), WithAlpha(colorB, 0.95f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.22f;
            main.maxParticles = 96;

            var emission = burst.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 72) });

            var shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var colorOverLifetime = burst.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient burstGradient = new Gradient();
            burstGradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(colorA, 0.28f), new GradientColorKey(colorB, 0.72f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.98f, 0f), new GradientAlphaKey(0.78f, 0.28f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = burstGradient;

            var sizeOverLifetime = burst.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.05f));

            var burstRenderer = burst.GetComponent<ParticleSystemRenderer>();
            burstRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            burstRenderer.lengthScale = 1.1f;
            burstRenderer.velocityScale = 0.22f;
            burstRenderer.sharedMaterial = GetOrCreateFireworkMaterial();

            GameObject flashObject = new GameObject("BloomFlash");
            flashObject.transform.SetParent(fx.transform);
            flashObject.transform.localPosition = Vector3.zero;
            flashObject.transform.localRotation = Quaternion.identity;

            ParticleSystem flash = flashObject.AddComponent<ParticleSystem>();
            var flashMain = flash.main;
            flashMain.loop = false;
            flashMain.duration = 0.05f;
            flashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
            flashMain.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.1f);
            flashMain.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.48f);
            flashMain.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(Color.white, 0.95f), WithAlpha(flashColor, 0.9f));
            flashMain.simulationSpace = ParticleSystemSimulationSpace.World;
            flashMain.maxParticles = 12;

            var flashEmission = flash.emission;
            flashEmission.rateOverTime = 0;
            flashEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10) });

            var flashShape = flash.shape;
            flashShape.shapeType = ParticleSystemShapeType.Sphere;
            flashShape.radius = 0.03f;

            var flashRenderer = flash.GetComponent<ParticleSystemRenderer>();
            flashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            flashRenderer.sharedMaterial = GetOrCreateFireworkMaterial();

            burst.Play(true);
            flash.Play(true);
            UnityEngine.Object.Destroy(fx, 1.35f);
        }

        private void SpawnFireworkSparkEffect(Vector3 position)
        {
            GameObject fx = new GameObject("DragonGun_FireworkSparkEndFx");
            fx.transform.position = position;

            Color color = ResolveFireworkColor(projectileIndex + shotId, true);
            ParticleSystem ps = fx.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.duration = 0.06f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(Color.white, 0.9f), WithAlpha(color, 0.82f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.2f;
            main.maxParticles = 16;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.035f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.45f), new GradientColorKey(color, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.86f, 0f), new GradientAlphaKey(0.45f, 0.45f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 0.8f;
            renderer.velocityScale = 0.16f;
            renderer.sharedMaterial = GetOrCreateFireworkMaterial();

            ps.Play(true);
            UnityEngine.Object.Destroy(fx, 0.65f);
        }

        private void SpawnIcePierceEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject iceFx = new GameObject("DragonGun_IcePierceFx");
            iceFx.transform.position = hitPoint;
            iceFx.transform.rotation = Quaternion.LookRotation(hitNormal);

            ParticleSystem ps = iceFx.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.duration = 0.16f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.095f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.9f, 1f, 1f, 0.88f),
                new Color(0.42f, 0.78f, 1f, 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 18;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.045f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(new Color(0.9f, 1f, 1f), 0f), new GradientColorKey(new Color(0.45f, 0.75f, 1f), 0.55f), new GradientColorKey(new Color(0.2f, 0.45f, 1f), 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.08f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.35f;
            renderer.velocityScale = 0.22f;
            renderer.sharedMaterial = GetOrCreateIceMaterial();

            ps.Play();
            UnityEngine.Object.Destroy(iceFx, 0.65f);
        }

        private void SpawnIceBladeShatterEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject iceFx = new GameObject("DragonGun_IceBladeShatterFx");
            iceFx.transform.position = hitPoint;
            iceFx.transform.rotation = hitNormal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(hitNormal.normalized, Vector3.up)
                : Quaternion.identity;

            ParticleSystem shards = iceFx.AddComponent<ParticleSystem>();
            var main = shards.main;
            main.loop = false;
            main.duration = 0.16f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 9.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 1f, 1f, 0.86f),
                new Color(0.38f, 0.75f, 1f, 0.68f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 18;

            var emission = shards.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 14) });

            var shape = shards.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.07f;

            var colorOverLifetime = shards.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(new Color(0.95f, 1f, 1f), 0f), new GradientColorKey(new Color(0.42f, 0.74f, 1f), 0.6f), new GradientColorKey(new Color(0.25f, 0.42f, 1f), 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.65f, 0.35f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = shards.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.06f));

            var renderer = shards.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.75f;
            renderer.velocityScale = 0.28f;
            renderer.sharedMaterial = GetOrCreateIceMaterial();

            shards.Play();
            UnityEngine.Object.Destroy(iceFx, 0.7f);
        }

        private static Material GetOrCreateIceMaterial()
        {
            if (cachedIceMaterial == null)
            {
                cachedIceMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            return cachedIceMaterial;
        }

        private static Material GetOrCreateFireworkMaterial()
        {
            if (cachedFireworkMaterial == null)
            {
                cachedFireworkMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            return cachedFireworkMaterial;
        }

        private static Color ResolveFireworkColor(int seed, bool secondary)
        {
            int index = Mathf.Abs(seed) % FireworkPalette.Length;
            Color color = FireworkPalette[index];
            return secondary ? color : Color.Lerp(color, new Color(1f, 0.82f, 0.28f), 0.45f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static void StripPhysicsComponents(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            foreach (var col in obj.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.Destroy(col);
            }

            foreach (var rb in obj.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.Destroy(rb);
            }
        }


        internal static void FadeAndDestroy(GameObject obj, float delay)
        {
            if (obj == null) return;
            var particles = obj.GetComponentsInChildren<ParticleSystem>(true);
            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    var em = particles[i].emission;
                    em.enabled = false;
                }
            }
            UnityEngine.Object.Destroy(obj, delay);
        }

        private void InitializeSplitOrbit(Projectile projectileInstance)
        {
            splitOrbitCenter = projectileInstance != null ? projectileInstance.transform.position : Vector3.zero;
            splitOrbitAxis = Vector3.up;
            splitOrbitBaseOffset = Vector3.zero;

            if (!secondaryProjectile || profile == null || profile.SplitOrbitRadius <= 0f || projectileInstance == null)
            {
                return;
            }

            Vector3 radial = Vector3.ProjectOnPlane(directionRef(projectileInstance), splitOrbitAxis);
            if (radial.sqrMagnitude <= 0.001f)
            {
                float angle = profile.SplitCount > 0 ? 360f * projectileIndex / profile.SplitCount : 0f;
                radial = Quaternion.AngleAxis(angle, splitOrbitAxis) * Vector3.forward;
            }

            splitOrbitBaseOffset = radial.normalized * Mathf.Max(0.05f, profile.SplitOrbitRadius);
        }

        private bool IsSplitWarmupActive()
        {
            if (!secondaryProjectile || splitActivated || profile == null)
            {
                return false;
            }

            float warmupDuration = Mathf.Max(profile.SplitActivationDelay, profile.SplitInvulnerableDuration);
            return warmupDuration > 0f && splitActivationTimer < warmupDuration;
        }

        private bool UpdateSplitWarmup(float deltaTime)
        {
            if (!IsSplitWarmupActive())
            {
                return false;
            }

            if (profile.UseRollingSnowball)
            {
                return false;
            }

            if (profile.SplitOrbitRadius > 0f && splitOrbitBaseOffset.sqrMagnitude > 0.001f)
            {
                float angle = profile.SplitOrbitAngularSpeed * splitActivationTimer;
                Vector3 offset = Quaternion.AngleAxis(angle, splitOrbitAxis) * splitOrbitBaseOffset;
                float growDuration = Mathf.Min(0.08f, Mathf.Max(0.01f, profile.SplitActivationDelay));
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(splitActivationTimer / growDuration));
                transform.position = splitOrbitCenter + offset * grow;

                Vector3 tangent = Vector3.Cross(splitOrbitAxis, offset);
                if (profile.SplitOrbitAngularSpeed < 0f)
                {
                    tangent = -tangent;
                }

                if (tangent.sqrMagnitude > 0.001f)
                {
                    Vector3 direction = tangent.normalized;
                    float speed = Mathf.Max(6f, velocityRef(projectile).magnitude);
                    directionRef(projectile) = direction;
                    velocityRef(projectile) = direction * speed;
                    transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
            }

            distanceThisFrameRef(projectile) = 0f;
            stopMovementThisFrame = true;
            return true;
        }

        private void UpdateSplitActivation(float deltaTime)
        {
            if (splitActivated || profile == null)
            {
                return;
            }

            if (profile.SplitActivationDelay <= 0f)
            {
                splitActivationTimer += deltaTime;
                if (splitActivationTimer >= Mathf.Max(0f, profile.SplitInvulnerableDuration))
                {
                    splitActivated = true;
                }

                return;
            }

            splitActivationTimer += deltaTime;
            if (splitActivationTimer >= profile.SplitActivationDelay)
            {
                splitActivated = true;
                Vector3 direction = directionRef(projectile);
                Vector3 velocity = velocityRef(projectile);
                float splitInitialSpeedMult = Mathf.Max(0.01f, profile.SplitInitialSpeedMult);
                velocityRef(projectile) = direction * (velocity.magnitude / splitInitialSpeedMult);
                customTraceLerp = 0f;
                traceRefreshTimer = 0f;
                return;
            }

            if (splitActivationTimer <= deltaTime)
            {
                Vector3 direction = directionRef(projectile);
                Vector3 velocity = velocityRef(projectile);
                float splitInitialSpeedMult = Mathf.Max(0.01f, profile.SplitInitialSpeedMult);
                velocityRef(projectile) = direction * (velocity.magnitude * splitInitialSpeedMult);
            }
        }

        private void TryRefreshTraceTarget(float deltaTime)
        {
            if (!splitActivated ||
                projectile == null ||
                profile == null ||
                profile.Id != DragonKingBossGunProfileId.Energy ||
                projectile.context.traceAbility <= 0.01f)
            {
                return;
            }

            bool currentTraceTargetUsable = HasUsableTraceTarget();
            if (currentTraceTargetUsable && !mandatorySplitTraceRefresh)
            {
                return;
            }

            if (!mandatorySplitTraceRefresh)
            {
                traceRefreshTimer -= deltaTime;
                if (traceRefreshTimer > 0f)
                {
                    return;
                }
            }

            traceRefreshTimer = 0.08f;
            TraceTargetCandidate target = mandatorySplitTraceRefresh ? FindNearestTraceTarget() : GetGunTraceTargetCandidate();
            if (!target.IsValid)
            {
                target = mandatorySplitTraceRefresh ? GetGunTraceTargetCandidate() : FindNearestTraceTarget();
            }

            if (!target.IsValid)
            {
                if (currentTraceTargetUsable)
                {
                    mandatorySplitTraceRefresh = false;
                }

                return;
            }

            SetTraceTarget(target);
            mandatorySplitTraceRefresh = false;
        }

        private bool HasUsableTraceTarget()
        {
            return IsTraceTargetUsable(projectile != null ? projectile.context.traceTarget : null) ||
                   IsTraceTransformUsable(explicitTraceTargetTransform);
        }

        private TraceTargetCandidate GetGunTraceTargetCandidate()
        {
            CharacterMainControl target = DragonKingBossGunRuntime.GetTraceTarget(sourceGun);
            if (IsTraceTargetUsable(target))
            {
                TraceTargetCandidate candidate = default(TraceTargetCandidate);
                candidate.Character = target;
                return candidate;
            }

            return default(TraceTargetCandidate);
        }

        private void SetTraceTarget(TraceTargetCandidate target)
        {
            if (target.Character != null)
            {
                SetTraceTarget(target.Character);
                return;
            }

            SetTraceTarget(target.Transform);
        }

        private void SetTraceTarget(CharacterMainControl target)
        {
            if (target == null || projectile == null)
            {
                return;
            }

            ApplyTraceTargetContext(target);
            explicitTraceTargetTransform = null;
        }

        private void SetTraceTarget(Transform targetTransform)
        {
            if (targetTransform == null || projectile == null)
            {
                return;
            }

            ApplyTraceTargetContext(null);
            explicitTraceTargetTransform = targetTransform;
        }

        private void ApplyTraceTargetContext(CharacterMainControl traceTarget)
        {
            ProjectileContext context = projectile.context;
            context.traceTarget = traceTarget;
            context.ignoreHalfObsticle = true;
            context.critRate = 1f;
            projectile.context = context;
        }

        private bool IsTraceTargetUsable(CharacterMainControl target)
        {
            if (target == null || projectile == null)
            {
                return false;
            }

            if (target == projectile.context.realFromCharacter)
            {
                return false;
            }

            if (target.Hidden)
            {
                return false;
            }

            if (target.Team == Teams.all || projectile.context.team == Teams.all)
            {
                return true;
            }

            return Team.IsEnemy(projectile.context.team, target.Team);
        }

        private bool IsTraceTransformUsable(Transform targetTransform)
        {
            return targetTransform != null && targetTransform.gameObject.activeInHierarchy;
        }

        private bool IsTraceReceiverUsable(DamageReceiver receiver, CharacterMainControl candidateCharacter, bool allowBaseSceneReceivers)
        {
            if (receiver == null || projectile == null)
            {
                return false;
            }

            if (candidateCharacter != null)
            {
                return IsTraceTargetUsable(candidateCharacter);
            }

            if (!receiver.gameObject.activeInHierarchy || receiver.health == null)
            {
                return false;
            }

            if (receiver.Team == Teams.all || projectile.context.team == Teams.all)
            {
                return true;
            }

            if (allowBaseSceneReceivers)
            {
                return true;
            }

            return Team.IsEnemy(projectile.context.team, receiver.Team);
        }

        private TraceTargetCandidate FindNearestTraceTarget()
        {
            Vector3 traceCenter;
            if (TryGetOfficialTraceCenter(out traceCenter))
            {
                TraceTargetCandidate target = FindNearestTraceTargetAround(traceCenter, 8f);
                if (target.IsValid)
                {
                    return target;
                }
            }

            float remainingDistance = Mathf.Max(4f, projectile.context.distance - traveledDistanceRef(projectile));
            return FindNearestTraceTargetAround(transform.position, remainingDistance);
        }

        private bool TryGetOfficialTraceCenter(out Vector3 traceCenter)
        {
            CharacterMainControl holder = sourceGun != null ? sourceGun.Holder : null;
            if (holder == null)
            {
                traceCenter = Vector3.zero;
                return false;
            }

            if (holder.IsMainCharacter && LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
            {
                traceCenter = LevelManager.Instance.InputManager.InputAimPoint;
                return true;
            }

            traceCenter = holder.GetCurrentAimPoint();
            return true;
        }

        private TraceTargetCandidate FindNearestTraceTargetAround(Vector3 center, float radius)
        {
            if (radius <= 0f)
            {
                return default(TraceTargetCandidate);
            }

            int count = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                DragonKingBossGunRuntime.SharedColliderBuffer,
                GameplayDataSettings.Layers.damageReceiverLayerMask,
                QueryTriggerInteraction.Ignore);

            TraceTargetCandidate bestTarget = default(TraceTargetCandidate);
            float bestDistanceSqr = float.MaxValue;
            bool allowBaseSceneReceivers = IsBaseScene();
            DragonKingBossGunRuntime.SharedReceiverIdSet.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider collider = DragonKingBossGunRuntime.SharedColliderBuffer[i];
                DamageReceiver receiver = collider != null ? collider.GetComponent<DamageReceiver>() : null;
                if (receiver == null)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (DragonKingBossGunRuntime.SharedReceiverIdSet.Contains(receiverId))
                {
                    continue;
                }

                if (splitSourceReceiverId >= 0 && receiverId == splitSourceReceiverId)
                {
                    DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiverId);
                    continue;
                }

                DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiverId);
                CharacterMainControl candidateCharacter = receiver.health != null ? receiver.health.TryGetCharacter() : null;
                if (!IsTraceReceiverUsable(receiver, candidateCharacter, allowBaseSceneReceivers))
                {
                    continue;
                }

                Transform candidateTransform = candidateCharacter != null ? candidateCharacter.transform : receiver.transform;
                if (candidateTransform == null)
                {
                    continue;
                }

                float distanceSqr = (candidateTransform.position - center).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                bestTarget.Character = candidateCharacter;
                bestTarget.Transform = candidateCharacter == null ? candidateTransform : null;
            }

            return bestTarget;
        }

        private bool IsBaseScene()
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return string.Equals(sceneName, "Base", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sceneName, "Base_SceneV2", StringComparison.OrdinalIgnoreCase);
        }


        private void SetDeathContext(DragonKingBossGunProjectileDeathReason reason, Vector3 point, Vector3 normal, GameObject hitObject = null)
        {
            deathReason = reason;
            deathPoint = point;
            deathNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            deathGroundImpact = hitObject != null && DragonKingBossGunRuntime.IsGroundSurface(hitObject, deathNormal);
            hasDeathContext = true;
        }

        private void SuppressNativeExplosionForAirburst()
        {
            if (projectile == null || profile == null || !profile.UseNativeProjectile || secondaryProjectile)
            {
                return;
            }

            ProjectileContext context = projectile.context;
            context.explosionRange = 0f;
            context.explosionDamage = 0f;
            projectile.context = context;
        }

        private Vector3 GetDeathPoint()
        {
            return hasDeathContext ? deathPoint : transform.position;
        }

        private bool ShouldPlayObstacleHitFx()
        {
            return profile != null && profile.PlayObstacleHitFx;
        }

        private bool ShouldPlaySplitTriggerFx()
        {
            return profile != null && profile.PlaySplitTriggerFx;
        }

        private bool ShouldPlayDeathExplosionFx()
        {
            if (profile == null)
            {
                return false;
            }

            if (profile.UseRollingSnowball &&
                secondaryProjectile &&
                (deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver ||
                 deathReason == DragonKingBossGunProjectileDeathReason.Obstacle))
            {
                return profile.PlayObstacleHitFx;
            }

            if (deathReason == DragonKingBossGunProjectileDeathReason.Obstacle ||
                deathReason == DragonKingBossGunProjectileDeathReason.MaxDistance)
            {
                return profile.PlayObstacleHitFx;
            }

            return true;
        }

        private bool ShouldSpawnGroundZoneOnDeath()
        {
            if (profile == null || !profile.UseGroundZone)
            {
                return false;
            }

            if (profile.UseRollingSnowball)
            {
                return !secondaryProjectile &&
                       (deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver ||
                        deathReason == DragonKingBossGunProjectileDeathReason.Obstacle ||
                        deathReason == DragonKingBossGunProjectileDeathReason.MaxDistance);
            }

            if (secondaryProjectile && !profile.GroundZoneAllowSecondary)
            {
                return false;
            }

            if (!profile.GroundZoneRequireGroundImpact)
            {
                return true;
            }

            return deathReason == DragonKingBossGunProjectileDeathReason.Obstacle && deathGroundImpact;
        }

        private void OnDisable()
        {
            if (projectile != null)
            {
                projectile.explosionFx = savedExplosionFx;
            }

            projectile = null;
            sourceGun = null;
            profile = null;
            secondaryProjectile = false;
            returning = false;
            splitTriggered = false;
            deathHandled = false;
            customTraceLerp = 0f;
            airburstDistance = float.MaxValue;
            remainingBounce = 0;
            remainingTargetHits = 1;
            successfulHits = 0;
            lastHelixOffset = Vector3.zero;
            stopMovementThisFrame = false;
            splitActivationTimer = 0f;
            splitActivated = true;
            splitSourceReceiverId = -1;
            traceRefreshTimer = 0f;
            mandatorySplitTraceRefresh = false;
            explicitTraceTargetTransform = null;
            splitOrbitCenter = Vector3.zero;
            splitOrbitAxis = Vector3.up;
            splitOrbitBaseOffset = Vector3.zero;
            lastHitReceiverId = -1;
            returnTargetPoint = Vector3.zero;
            stickyAttached = false;
            stickyFollowTarget = null;
            stickyLocalOffset = Vector3.zero;
            stickyWorldPoint = Vector3.zero;
            stickyNormal = Vector3.up;
            stickyElapsed = 0f;
            deathReason = DragonKingBossGunProjectileDeathReason.None;
            deathPoint = Vector3.zero;
            deathNormal = Vector3.up;
            hasDeathContext = false;
            deathGroundImpact = false;
            rollingElapsed = 0f;
            rollingBaseScale = Vector3.one;
            rollingBaseRadius = 0f;
            rollingCurrentScaleFactor = 1f;
            damagedReceiverIds.Clear();
            savedExplosionFx = null;

            if (customTrailInstance != null)
            {
                UnityEngine.Object.Destroy(customTrailInstance, 2f);
                customTrailInstance = null;
            }

            DisableIceBladeTrail();

            if (stickyFireFxInstance != null)
            {
                UnityEngine.Object.Destroy(stickyFireFxInstance);
                stickyFireFxInstance = null;
            }
        }

        public void OnBeforeBaseMove()
        {
            if (!IsActiveForRuntime)
            {
                return;
            }

            UpdateSplitActivation(Mathf.Min(Time.deltaTime, 0.04f));

            if (profile.UseHelix)
            {
                ApplyHelixOffset();
            }

            if (!secondaryProjectile &&
                profile.UseSplit &&
                profile.SplitOnAirburst &&
                !splitTriggered &&
                traveledDistanceRef(projectile) >= airburstDistance)
            {
                SetDeathContext(DragonKingBossGunProjectileDeathReason.Airburst, transform.position, -directionRef(projectile));
                TriggerSplit(ShouldPlaySplitTriggerFx());
                SuppressNativeExplosionForAirburst();
                deadRef(projectile) = true;
            }
        }

        public void OnAfterBaseMove()
        {
            if (!IsActiveForRuntime)
            {
                return;
            }

            if (!secondaryProjectile &&
                profile.UseSplit &&
                profile.SplitOnAirburst &&
                !splitTriggered &&
                !deadRef(projectile) &&
                traveledDistanceRef(projectile) >= airburstDistance)
            {
                SetDeathContext(DragonKingBossGunProjectileDeathReason.Airburst, transform.position, -directionRef(projectile));
                TriggerSplit(ShouldPlaySplitTriggerFx());
                SuppressNativeExplosionForAirburst();
                deadRef(projectile) = true;
            }

            if (deadRef(projectile))
            {
                if (!hasDeathContext && overMaxDistanceRef(projectile))
                {
                    SetDeathContext(DragonKingBossGunProjectileDeathReason.MaxDistance, transform.position, -directionRef(projectile));
                }

                HandleDeath();
            }
        }

        public void ExecuteCustomMoveAndCheck()
        {
            if (!IsActiveForRuntime)
            {
                return;
            }

            ManualMoveAndCheck();
            if (deadRef(projectile))
            {
                HandleDeath();
            }
        }

        private void ManualMoveAndCheck()
        {
            bool isFirstFrame = firstFrameRef(projectile);
            stopMovementThisFrame = false;

            if (isFirstFrame)
            {
                startPointRef(projectile) = transform.position;
            }

            float deltaTime = Mathf.Min(Time.deltaTime, 0.04f);
            ApplyRollingSnowballVisual(deltaTime);

            if (stickyAttached)
            {
                UpdateStickyAttachment(deltaTime);
                return;
            }

            UpdateSplitActivation(deltaTime);
            if (UpdateSplitWarmup(deltaTime))
            {
                if (isFirstFrame)
                {
                    startPointRef(projectile) = splitOrbitCenter;
                    firstFrameRef(projectile) = false;
                }

                return;
            }

            TryRefreshTraceTarget(deltaTime);

            float currentSpeed = UpdateDirectionAndVelocity(deltaTime);

            if (profile.UseHelix)
            {
                ApplyHelixOffset();
            }

            if (!splitActivated && secondaryProjectile && profile.SplitGravity > 0f && velocityRef(projectile).y <= 0f)
            {
                splitActivated = true;
            }

            float distanceThisFrame = currentSpeed * deltaTime;
            if (distanceThisFrame + traveledDistanceRef(projectile) > projectile.context.distance)
            {
                distanceThisFrame = projectile.context.distance - traveledDistanceRef(projectile);
                overMaxDistanceRef(projectile) = true;
            }

            distanceThisFrameRef(projectile) = distanceThisFrame;

            Vector3 castStart = transform.position - transform.forward * 0.1f;
            if (isFirstFrame && projectile.context.firstFrameCheck)
            {
                castStart = projectile.context.firstFrameCheckStartPoint;
            }

            if (isFirstFrame)
            {
                firstFrameRef(projectile) = false;
            }

            int hitCount = Physics.SphereCastNonAlloc(
                castStart,
                projectile.radius,
                directionRef(projectile),
                raycastBuffer,
                distanceThisFrame + 0.3f,
                hitLayersRef(projectile),
                QueryTriggerInteraction.Ignore);
            if (hitCount > 1)
            {
                Array.Sort(raycastBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
            }

            for (int i = 0; i < hitCount; i++)
            {
                if (HandleHit(raycastBuffer[i]))
                {
                    break;
                }
            }

            if (overMaxDistanceRef(projectile))
            {
                if (profile.UseReturn && !returning)
                {
                    BeginReturn();
                }
                else
                {
                    SetDeathContext(DragonKingBossGunProjectileDeathReason.MaxDistance, transform.position, -directionRef(projectile));
                    deadRef(projectile) = true;
                }
            }

            if (!deadRef(projectile) && !stopMovementThisFrame)
            {
                transform.position += directionRef(projectile) * distanceThisFrame;
                traveledDistanceRef(projectile) = traveledDistanceRef(projectile) + distanceThisFrame;
            }

            if (!deadRef(projectile) &&
                !secondaryProjectile &&
                profile.UseSplit &&
                profile.SplitOnAirburst &&
                !splitTriggered &&
                traveledDistanceRef(projectile) >= airburstDistance)
            {
                SetDeathContext(DragonKingBossGunProjectileDeathReason.Airburst, transform.position, -directionRef(projectile));
                TriggerSplit(ShouldPlaySplitTriggerFx());
                SuppressNativeExplosionForAirburst();
                deadRef(projectile) = true;
            }
        }

        private float UpdateDirectionAndVelocity(float deltaTime)
        {
            Vector3 direction = directionRef(projectile);
            Vector3 velocity = velocityRef(projectile);
            float currentSpeed;

            if (returning)
            {
                Vector3 returnDirection = GetReturnDirection();
                customTraceLerp = Mathf.MoveTowards(customTraceLerp, 1f, 4f * deltaTime);
                direction = Vector3.Slerp(direction, returnDirection, customTraceLerp).normalized;
                currentSpeed = Mathf.Max(6f, velocity.magnitude);
                if (direction.sqrMagnitude <= 0.0000000001f)
                {
                    currentSpeed = 0f;
                }
                velocity = direction * currentSpeed;
            }
            else if (splitActivated && projectile.context.traceAbility > 0.01f && HasUsableTraceTarget())
            {
                Vector3 targetDirection = GetTraceDirection();
                customTraceLerp = Mathf.MoveTowards(customTraceLerp, 1f, projectile.context.traceAbility * deltaTime);
                direction = Vector3.Lerp(projectile.context.direction, targetDirection, customTraceLerp).normalized;
                currentSpeed = Mathf.Max(6f, velocity.magnitude);
                if (direction.sqrMagnitude <= 0.0000000001f)
                {
                    currentSpeed = 0f;
                }
                velocity = direction * currentSpeed;
            }
            else
            {
                velocity.y -= deltaTime * Mathf.Abs(projectile.context.gravity);
                currentSpeed = velocity.magnitude;
                direction = currentSpeed > 0.00001f ? velocity / currentSpeed : Vector3.zero;
            }

            directionRef(projectile) = direction;
            velocityRef(projectile) = velocity;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            return currentSpeed;
        }

        private void ApplyRollingSnowballVisual(float deltaTime)
        {
            if (projectile == null || profile == null || !profile.UseRollingSnowball)
            {
                return;
            }

            rollingElapsed += Mathf.Max(0f, deltaTime);

            float scaleFactor = ResolveRollingScaleFactor();

            rollingCurrentScaleFactor = scaleFactor;
            transform.localScale = rollingBaseScale * scaleFactor;
            if (rollingBaseRadius > 0f)
            {
                projectile.radius = Mathf.Max(0.02f, rollingBaseRadius * scaleFactor);
            }
        }

        private float ResolveRollingScaleFactor()
        {
            float growDuration = secondaryProjectile
                ? (profile.RollingSecondaryGrowthDuration > 0f ? profile.RollingSecondaryGrowthDuration : profile.SplitMaxLifetimeSeconds)
                : (profile.RollingGrowthDuration > 0f ? profile.RollingGrowthDuration : profile.MaxLifetimeSeconds);
            float startScale = secondaryProjectile ? profile.RollingSecondaryStartScaleFactor : profile.RollingStartScaleFactor;
            float endScale = secondaryProjectile ? profile.RollingSecondaryEndScaleFactor : profile.RollingEndScaleFactor;
            float progress = Mathf.Clamp01(rollingElapsed / Mathf.Max(0.01f, growDuration));
            return Mathf.Lerp(
                Mathf.Max(0.1f, startScale),
                Mathf.Max(0.1f, endScale),
                Mathf.SmoothStep(0f, 1f, progress));
        }

        private float ResolveRollingDamageFactor()
        {
            if (profile == null || !profile.UseRollingSnowball)
            {
                return 1f;
            }

            return Mathf.Max(0.1f, rollingCurrentScaleFactor);
        }

        private Vector3 GetTraceDirection()
        {
            if (IsTraceTargetUsable(projectile != null ? projectile.context.traceTarget : null))
            {
                return GetTraceDirection(projectile.context.traceTarget);
            }

            return GetTraceDirection(explicitTraceTargetTransform);
        }

        private Vector3 GetTraceDirection(CharacterMainControl target)
        {
            if (target == null)
            {
                return directionRef(projectile);
            }

            Transform targetTransform = target.characterModel != null ? target.characterModel.HelmatSocket : target.transform;
            Vector3 direction = targetTransform.position - transform.position;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : directionRef(projectile);
        }

        private Vector3 GetTraceDirection(Transform targetTransform)
        {
            if (targetTransform == null)
            {
                return directionRef(projectile);
            }

            Vector3 direction = targetTransform.position - transform.position;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : directionRef(projectile);
        }

        private Vector3 GetReturnDirection()
        {
            Vector3 directionToOrigin = returnTargetPoint - transform.position;
            if (directionToOrigin.sqrMagnitude > 0.001f)
            {
                return directionToOrigin.normalized;
            }

            CharacterMainControl holder = sourceGun != null ? sourceGun.Holder : null;
            if (holder == null)
            {
                return -directionRef(projectile);
            }

            Vector3 direction = holder.transform.position + Vector3.up * 1.1f - transform.position;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : -directionRef(projectile);
        }

        private bool HandleHit(RaycastHit hit)
        {
            if (hit.collider == null)
            {
                return false;
            }

            if (projectile.context.ignoreHalfObsticle &&
                GameplayDataSettings.LayersData.IsLayerInLayerMask(hit.collider.gameObject.layer, GameplayDataSettings.Layers.halfObsticleLayer))
            {
                return false;
            }

            if (projectile.damagedObjects.Contains(hit.collider.gameObject))
            {
                return false;
            }

            Vector3 hitPoint = hit.distance > 0f ? hit.point : hit.collider.transform.position;
            Vector3 hitNormal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : Vector3.up;

            DamageReceiver receiver = (GameplayDataSettings.Layers.damageReceiverLayerMask & 1 << hit.collider.gameObject.layer) != 0
                ? hit.collider.GetComponent<DamageReceiver>()
                : null;

            if (receiver != null)
            {
                return HandleDamageReceiverHit(receiver, hit.collider.gameObject, hitPoint, hitNormal);
            }

            return HandleObstacleHit(hit.collider.gameObject, hitPoint, hitNormal);
        }

        private bool HandleDamageReceiverHit(DamageReceiver receiver, GameObject hitObject, Vector3 hitPoint, Vector3 hitNormal)
        {
            int receiverId = receiver.GetInstanceID();

            if (!splitActivated && profile.UseRollingSnowball)
            {
                return false;
            }

            if (!splitActivated)
            {
                // 未激活的分裂弹命中敌人时立即激活，而非忽略
                splitActivated = true;
            }

            if (splitSourceReceiverId >= 0 && receiverId == splitSourceReceiverId)
            {
                return false;
            }

            if (damagedReceiverIds.Contains(receiverId))
            {
                return false;
            }

            if (projectile.context.team == receiver.Team && receiver.Team != Teams.all)
            {
                return false;
            }

            if (receiver.isHalfObsticle && projectile.context.ignoreHalfObsticle)
            {
                return false;
            }

            CharacterMainControl receiverCharacter = receiver.health != null ? receiver.health.TryGetCharacter() : null;
            if (receiverCharacter != null && receiverCharacter == projectile.context.realFromCharacter)
            {
                return false;
            }

            if (receiverCharacter != null && receiverCharacter.Dashing)
            {
                return false;
            }

            damagedReceiverIds.Add(receiverId);
            if (hitObject != null)
            {
                projectile.damagedObjects.Add(hitObject);
            }

            DragonKingBossGunRuntime.DragonKingBossGunHitStage hitStage = ResolveHitStage();
            float marker = DragonKingBossGunRuntime.EncodeShotMarker(shotId, profile.Id, hitStage);
            DamageInfo damageInfo = DragonKingBossGunRuntime.CreateDamageInfo(projectile.context, 1f, hitPoint, hitNormal, false, false, marker);
            damageInfo.damageValue *= ResolveRollingDamageFactor();
            float halfDamageDistance = projectile.context.halfDamageDistance;
            if (halfDamageDistance > 0f)
            {
                float halfDamageDistanceSqr = halfDamageDistance * halfDamageDistance;
                Vector3 damageDistanceDelta = hitPoint - startPointRef(projectile);
                if (damageDistanceDelta.sqrMagnitude > halfDamageDistanceSqr)
                {
                    float rangeFactor = projectile.context.dmgOverDistance > 0f ? projectile.context.dmgOverDistance : 0.5f;
                    damageInfo.damageValue *= rangeFactor;
                }
            }

            if (profile.PierceDamageDecay != null && successfulHits > 0 && successfulHits <= profile.PierceDamageDecay.Length)
            {
                damageInfo.damageValue *= profile.PierceDamageDecay[successfulHits - 1];
            }
            else if (profile.PierceDamageDecay != null && successfulHits > profile.PierceDamageDecay.Length)
            {
                damageInfo.damageValue *= profile.PierceDamageDecay[profile.PierceDamageDecay.Length - 1];
            }

            receiver.Hurt(damageInfo);
            receiver.AddBuff(GameplayDataSettings.Buffs.Pain, projectile.context.fromCharacter);
            if (projectile.context.fromGunItemSetting != null)
            {
                projectile.context.fromGunItemSetting.TriggerOnHurtEnemyEvent(receiver, damageInfo);
            }

            successfulHits++;
            remainingTargetHits--;
            lastHitReceiverId = receiverId;

            if (profile.Id == DragonKingBossGunProfileId.IceBlade && remainingTargetHits > 0)
            {
                SpawnIcePierceEffect(hitPoint, hitNormal);
            }

            if (profile.UseSticky)
            {
                BeginStickyAttachment(receiver, hitObject, hitPoint, hitNormal);
                return true;
            }

            if (remainingTargetHits <= 0)
            {
                SetDeathContext(DragonKingBossGunProjectileDeathReason.DamageReceiver, hitPoint, hitNormal, hitObject);
                transform.position = hitPoint;
                deadRef(projectile) = true;
                return true;
            }

            return false;
        }

        private bool HandleObstacleHit(GameObject hitObject, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (hitObject != null)
            {
                projectile.damagedObjects.Add(hitObject);
            }

            if (profile.UseSticky)
            {
                BeginStickyAttachment(null, hitObject, hitPoint, hitNormal);
                return true;
            }

            if (profile.UseReturn && !returning)
            {
                BeginReturn();
                return true;
            }

            if (remainingBounce > 0)
            {
                Bounce(hitObject, hitPoint, hitNormal);
                return true;
            }

            transform.position = hitPoint;
            SetDeathContext(DragonKingBossGunProjectileDeathReason.Obstacle, hitPoint, hitNormal, hitObject);
            deadRef(projectile) = true;
            if (ShouldPlayObstacleHitFx())
            {
                SpawnObstacleHitFx(hitPoint, hitNormal);
            }
            return true;
        }

        private void Bounce(GameObject hitObject, Vector3 hitPoint, Vector3 hitNormal)
        {
            Vector3 reflectedDirection = Vector3.Reflect(directionRef(projectile), hitNormal).normalized;
            float speed = Mathf.Max(6f, velocityRef(projectile).magnitude);
            directionRef(projectile) = reflectedDirection;
            velocityRef(projectile) = reflectedDirection * speed;
            transform.position = hitPoint + reflectedDirection * Mathf.Max(projectile.radius + 0.04f, 0.12f);
            transform.rotation = Quaternion.LookRotation(reflectedDirection, Vector3.up);
            projectile.damagedObjects.Clear();
            damagedReceiverIds.Clear();
            if (hitObject != null)
            {
                projectile.damagedObjects.Add(hitObject);
            }

            remainingBounce--;
            customTraceLerp = 0f;
            stopMovementThisFrame = true;
            if (ShouldPlayObstacleHitFx())
            {
                SpawnObstacleHitFx(hitPoint, hitNormal);
            }
        }

        private void SpawnObstacleHitFx(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (profile != null && !string.IsNullOrEmpty(profile.HitFxPrefab))
            {
                GameObject fx = DragonKingAssetManager.InstantiateEffect(profile.HitFxPrefab, hitPoint, Quaternion.LookRotation(hitNormal, Vector3.up));
                if (fx != null)
                {
                    UnityEngine.Object.Destroy(fx, 2f);
                }
                else if (profile.Element == ElementTypes.fire)
                {
                    SpawnFireHitEffect(hitPoint, hitNormal);
                }
            }
            else if (profile != null && profile.Element == ElementTypes.fire)
            {
                SpawnFireHitEffect(hitPoint, hitNormal);
            }
        }

        private void BeginReturn()
        {
            returning = true;
            overMaxDistanceRef(projectile) = false;
            traveledDistanceRef(projectile) = 0f;
            projectile.damagedObjects.Clear();
            damagedReceiverIds.Clear();
            customTraceLerp = 0f;
            remainingTargetHits = Mathf.Max(1, projectile.context.penetrate + 1);

            Vector3 returnDirection = GetReturnDirection();
            directionRef(projectile) = returnDirection;
            velocityRef(projectile) = returnDirection * Mathf.Max(6f, velocityRef(projectile).magnitude);
            transform.rotation = Quaternion.LookRotation(returnDirection, Vector3.up);
        }

        private void HandleDeath()
        {
            if (deathHandled || !IsActiveForRuntime)
            {
                return;
            }

            deathHandled = true;
            Vector3 resolvedDeathPoint = GetDeathPoint();
            bool playDeathExplosionFx = ShouldPlayDeathExplosionFx();
            bool isNativeExplosion = profile.UseNativeProjectile && secondaryProjectile;
            bool suppressPrimaryAirburstExplosion = !secondaryProjectile &&
                                                   deathReason == DragonKingBossGunProjectileDeathReason.Airburst;
            bool secondaryCollisionExplosion =
                deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver ||
                deathReason == DragonKingBossGunProjectileDeathReason.Obstacle ||
                deathReason == DragonKingBossGunProjectileDeathReason.StickyAttach;

            if (!isNativeExplosion && !secondaryProjectile && !suppressPrimaryAirburstExplosion && profile.ExplosionRange > 0f)
            {
                if (playDeathExplosionFx)
                {
                    if (profile.Id == DragonKingBossGunProfileId.IceBlade)
                    {
                        SpawnIceBladeShatterEffect(resolvedDeathPoint, deathNormal);
                    }
                    else
                    {
                        DragonKingBossGunRuntime.TrySpawnExplosionFx(resolvedDeathPoint, profile);
                    }
                }

                DragonKingBossGunRuntime.ApplyRadiusDamage(
                    resolvedDeathPoint,
                    Mathf.Max(0.5f, profile.ExplosionRange),
                    projectile.context,
                    Mathf.Max(0.15f, profile.ExplosionDamageFactor),
                    true,
                    true,
                    0f);

                if (profile.Element == ElementTypes.ice)
                {
                    ApplyDeathBuff(GameplayDataSettings.Buffs.Cold, Mathf.Max(0.5f, profile.ExplosionRange));
                }
            }

            if (!secondaryProjectile && profile.UseSplit && !splitTriggered)
            {
                TriggerSplit(ShouldPlaySplitTriggerFx());
            }

            if (ShouldSpawnGroundZoneOnDeath())
            {
                DragonKingBossGunRuntime.SpawnGroundZone(resolvedDeathPoint, sourceGun, projectile.context, profile);
            }

            if (!isNativeExplosion && secondaryProjectile && secondaryCollisionExplosion && profile.SplitExplosionRange > 0f)
            {
                if (playDeathExplosionFx)
                {
                    DragonKingBossGunRuntime.TrySpawnExplosionFx(resolvedDeathPoint, profile);
                }

                DragonKingBossGunRuntime.ApplyRadiusDamage(
                    resolvedDeathPoint,
                    Mathf.Max(0.3f, profile.SplitExplosionRange),
                    projectile.context,
                    Mathf.Max(0.1f, profile.SplitExplosionDamageFactor) * ResolveRollingDamageFactor(),
                    true,
                    true,
                    0f);
            }

            if (profile.Id == DragonKingBossGunProfileId.Firework &&
                secondaryProjectile &&
                (deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver ||
                 deathReason == DragonKingBossGunProjectileDeathReason.Obstacle ||
                 deathReason == DragonKingBossGunProjectileDeathReason.MaxDistance))
            {
                SpawnFireworkSparkEffect(resolvedDeathPoint);
            }
        }

        private void TriggerSplit(bool playFx = true)
        {
            if (splitTriggered)
            {
                return;
            }

            splitTriggered = true;
            int sourceId = profile.SplitIgnoreSourceOnSplit ? lastHitReceiverId : -1;
            Vector3 splitNormal = hasDeathContext ? deathNormal : Vector3.up;
            DragonKingBossGunRuntime.SpawnSplitProjectiles(sourceGun, profile, shotId, transform.position, directionRef(projectile), splitNormal, sourceId);
            if (profile.Id == DragonKingBossGunProfileId.Firework)
            {
                SpawnFireworkBloomEffect(transform.position);
            }
            else if (playFx)
            {
                DragonKingBossGunRuntime.TrySpawnExplosionFx(transform.position, profile);
            }
        }

        private void ApplyHelixOffset()
        {
            float phase = traveledDistanceRef(projectile) * profile.HelixFrequency + ((projectileIndex & 1) == 0 ? 0f : Mathf.PI);
            Vector3 forward = directionRef(projectile);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = transform.forward;
            }

            forward.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            if (lateral.sqrMagnitude < 0.001f)
            {
                lateral = Vector3.Cross(Vector3.forward, forward);
            }

            lateral.Normalize();

            Vector3 offset = lateral * (Mathf.Sin(phase) * profile.HelixAmplitude);
            if (!profile.LockHelixToHorizontalPlane)
            {
                Vector3 vertical = Vector3.Cross(forward, lateral);
                offset += vertical * (Mathf.Cos(phase) * profile.HelixAmplitude * 0.55f);
            }

            if (profile.HelixVerticalLift > 0f)
            {
                offset += Vector3.up * (traveledDistanceRef(projectile) * profile.HelixVerticalLift * 0.02f);
            }

            transform.position += offset - lastHelixOffset;
            lastHelixOffset = offset;
        }

        private void ApplyDeathBuff(Buff buff, float radius)
        {
            if (buff == null || radius <= 0f)
            {
                return;
            }

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

                DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiverId);
                receiver.AddBuff(buff, projectile.context.fromCharacter);
            }
        }

        private DragonKingBossGunRuntime.DragonKingBossGunHitStage ResolveHitStage()
        {
            if (secondaryProjectile)
            {
                return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary;
            }

            if (returning)
            {
                return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Return;
            }

            if (successfulHits > 0)
            {
                return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Followup;
            }

            return DragonKingBossGunRuntime.DragonKingBossGunHitStage.Primary;
        }
    }

    [HarmonyPatch(typeof(Projectile), "UpdateMoveAndCheck")]
    internal static class DragonKingBossGunProjectileMovePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Projectile __instance, out DragonKingBossGunProjectileAgent __state)
        {
            __state = __instance != null ? __instance.GetComponent<DragonKingBossGunProjectileAgent>() : null;
            if (__state == null || !__state.IsActiveForRuntime)
            {
                __state = null;
                return true;
            }

            if (__state.UsesCustomMovement)
            {
                __state.ExecuteCustomMoveAndCheck();
                return false;
            }

            __state.OnBeforeBaseMove();
            return !__state.IsDead;
        }

        [HarmonyPostfix]
        private static void Postfix(DragonKingBossGunProjectileAgent __state)
        {
            if (__state == null || !__state.IsActiveForRuntime)
            {
                return;
            }

            __state.OnAfterBaseMove();
        }
    }
}

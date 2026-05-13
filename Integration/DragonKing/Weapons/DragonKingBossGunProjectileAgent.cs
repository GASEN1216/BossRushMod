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
            DragonKingBossGunGroundZone.ClearStaticCaches();
        }

        private static Material cachedIceMaterial;

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
        private GameObject savedExplosionFx;
        private bool stopMovementThisFrame;
        private float splitActivationTimer;
        private bool splitActivated = true;
        private int splitSourceReceiverId = -1;
        private int lastHitReceiverId = -1;
        private Vector3 returnTargetPoint;
        private DragonKingBossGunProjectileDeathReason deathReason;
        private Vector3 deathPoint;
        private Vector3 deathNormal;
        private bool hasDeathContext;
        private bool deathGroundImpact;
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

        private void CreateStickyCharge(DamageReceiver target, Vector3 point, Vector3 normal)
        {
            GameObject chargeObj = new GameObject("DragonGun_StickyCharge");
            chargeObj.transform.position = point;

            if (profile.Element == ElementTypes.fire)
            {
                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(chargeObj);
            }

            DragonKingBossGunStickyCharge charge = chargeObj.AddComponent<DragonKingBossGunStickyCharge>();
            charge.Initialize(projectile.context, profile, shotId, target != null ? target.transform : null, point);
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
            airburstDistance = profile != null && profile.SplitOnAirburst ? projectile.context.distance * Mathf.Clamp01(profile.AirburstDistanceFactor) : float.MaxValue;
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
            splitActivated = !isSecondary || profile == null || (profile.SplitActivationDelay <= 0f && profile.SplitGravity <= 0f);
            splitSourceReceiverId = sourceReceiverId;
            lastHitReceiverId = -1;
            returnTargetPoint = projectileInstance != null ? projectileInstance.transform.position : Vector3.zero;
            deathReason = DragonKingBossGunProjectileDeathReason.None;
            deathPoint = Vector3.zero;
            deathNormal = Vector3.up;
            hasDeathContext = false;
            deathGroundImpact = false;
            damagedReceiverIds.Clear();

            savedExplosionFx = projectileInstance != null ? projectileInstance.explosionFx : null;
            bool keepNativeExplosionFx = profile != null && profile.UseNativeProjectile && !isSecondary;
            if (projectileInstance != null && !keepNativeExplosionFx)
            {
                projectileInstance.explosionFx = null;
            }

            if (customTrailInstance == null && profile != null && !profile.UseNativeProjectile && !string.IsNullOrEmpty(profile.TrailFxPrefab))
            {
                customTrailInstance = DragonKingAssetManager.InstantiateEffect(profile.TrailFxPrefab, transform.position, transform.rotation, transform);
            }
            else if (customTrailInstance == null && profile != null && !profile.UseNativeProjectile && profile.Element == ElementTypes.fire)
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

            // 保留原版弹幕视觉，缩放已在 SpawnDragonProjectile 中通过 transform.localScale 处理
            enabled = true;
        }

        private void CreateIceBladeTrail()
        {
            TrailRenderer trail = gameObject.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = gameObject.AddComponent<TrailRenderer>();
            }

            trail.time = 0.25f;
            trail.startWidth = 0.25f;
            trail.endWidth = 0f;
            trail.startColor = new Color(0.5f, 0.8f, 1f, 0.8f);
            trail.endColor = new Color(0.5f, 0.8f, 1f, 0f);
            trail.material = GetOrCreateIceMaterial();
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.minVertexDistance = 0.05f;
        }

        private void SpawnIcePierceEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject iceFx = new GameObject("DragonGun_IcePierceFx");
            iceFx.transform.position = hitPoint;
            iceFx.transform.rotation = Quaternion.LookRotation(hitNormal);

            ParticleSystem ps = iceFx.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.duration = 0.15f;
            main.startLifetime = 0.35f;
            main.startSpeed = 4f;
            main.startSize = 0.12f;
            main.startColor = new Color(0.5f, 0.8f, 1f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 12;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(new Color(0.5f, 0.8f, 1f), 0f), new GradientColorKey(new Color(0.7f, 0.9f, 1f), 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetOrCreateIceMaterial();

            ps.Play();
            UnityEngine.Object.Destroy(iceFx, 1f);
        }

        private static Material GetOrCreateIceMaterial()
        {
            if (cachedIceMaterial == null)
            {
                cachedIceMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            return cachedIceMaterial;
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

        private void UpdateSplitActivation(float deltaTime)
        {
            if (splitActivated || profile == null || profile.SplitActivationDelay <= 0f)
            {
                return;
            }

            splitActivationTimer += deltaTime;
            if (splitActivationTimer >= profile.SplitActivationDelay)
            {
                splitActivated = true;
                velocityRef(projectile) = directionRef(projectile) * (velocityRef(projectile).magnitude / Mathf.Max(0.01f, profile.SplitInitialSpeedMult));
                customTraceLerp = 0f;
                return;
            }

            if (splitActivationTimer <= deltaTime)
            {
                velocityRef(projectile) = directionRef(projectile) * (velocityRef(projectile).magnitude * Mathf.Max(0.01f, profile.SplitInitialSpeedMult));
            }
        }

        private void SetDeathContext(DragonKingBossGunProjectileDeathReason reason, Vector3 point, Vector3 normal, GameObject hitObject = null)
        {
            deathReason = reason;
            deathPoint = point;
            deathNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            deathGroundImpact = hitObject != null && DragonKingBossGunRuntime.IsGroundSurface(hitObject, deathNormal);
            hasDeathContext = true;
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
            lastHitReceiverId = -1;
            returnTargetPoint = Vector3.zero;
            deathReason = DragonKingBossGunProjectileDeathReason.None;
            deathPoint = Vector3.zero;
            deathNormal = Vector3.up;
            hasDeathContext = false;
            deathGroundImpact = false;
            damagedReceiverIds.Clear();
            savedExplosionFx = null;

            if (customTrailInstance != null)
            {
                UnityEngine.Object.Destroy(customTrailInstance, 2f);
                customTrailInstance = null;
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

            UpdateSplitActivation(deltaTime);

            UpdateDirectionAndVelocity(deltaTime);

            if (profile.UseHelix)
            {
                ApplyHelixOffset();
            }

            if (!splitActivated && secondaryProjectile && profile.SplitGravity > 0f && velocityRef(projectile).y <= 0f)
            {
                splitActivated = true;
            }

            float distanceThisFrame = velocityRef(projectile).magnitude * deltaTime;
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
            Array.Sort(raycastBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);

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
                deadRef(projectile) = true;
            }
        }

        private void UpdateDirectionAndVelocity(float deltaTime)
        {
            Vector3 direction = directionRef(projectile);
            Vector3 velocity = velocityRef(projectile);

            if (returning)
            {
                Vector3 returnDirection = GetReturnDirection();
                customTraceLerp = Mathf.MoveTowards(customTraceLerp, 1f, 4f * deltaTime);
                direction = Vector3.Slerp(direction, returnDirection, customTraceLerp).normalized;
                velocity = direction * Mathf.Max(6f, velocity.magnitude);
            }
            else if (splitActivated && projectile.context.traceTarget != null && projectile.context.traceAbility > 0.01f)
            {
                Vector3 targetDirection = GetTraceDirection(projectile.context.traceTarget);
                customTraceLerp = Mathf.MoveTowards(customTraceLerp, 1f, projectile.context.traceAbility * deltaTime);
                direction = Vector3.Lerp(direction, targetDirection, customTraceLerp).normalized;
                velocity = direction * Mathf.Max(6f, velocity.magnitude);
            }
            else
            {
                velocity.y -= deltaTime * Mathf.Abs(projectile.context.gravity);
                direction = velocity.normalized;
            }

            directionRef(projectile) = direction;
            velocityRef(projectile) = velocity;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
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
            if (projectile.context.halfDamageDistance > 0f && Vector3.Distance(startPointRef(projectile), hitPoint) > projectile.context.halfDamageDistance)
            {
                float rangeFactor = projectile.context.dmgOverDistance > 0f ? projectile.context.dmgOverDistance : 0.5f;
                damageInfo.damageValue *= rangeFactor;
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
            successfulHits++;
            remainingTargetHits--;
            lastHitReceiverId = receiverId;

            if (profile.Id == DragonKingBossGunProfileId.IceBlade && remainingTargetHits > 0)
            {
                SpawnIcePierceEffect(hitPoint, hitNormal);
            }

            if (profile.UseSticky)
            {
                CreateStickyCharge(receiver, hitPoint, hitNormal);
                SetDeathContext(DragonKingBossGunProjectileDeathReason.StickyAttach, hitPoint, hitNormal, hitObject);
                transform.position = hitPoint;
                deadRef(projectile) = true;
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
                CreateStickyCharge(null, hitPoint, hitNormal);
                SetDeathContext(DragonKingBossGunProjectileDeathReason.StickyAttach, hitPoint, hitNormal, hitObject);
                transform.position = hitPoint;
                deadRef(projectile) = true;
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
            bool isNativeExplosion = profile.UseNativeProjectile && !secondaryProjectile;

            if (!isNativeExplosion && !secondaryProjectile && profile.ExplosionRange > 0f)
            {
                if (playDeathExplosionFx)
                {
                    DragonKingBossGunRuntime.TrySpawnExplosionFx(resolvedDeathPoint, profile);
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

            if (secondaryProjectile && profile.SplitExplosionRange > 0f)
            {
                if (playDeathExplosionFx)
                {
                    DragonKingBossGunRuntime.TrySpawnExplosionFx(resolvedDeathPoint, profile);
                }

                DragonKingBossGunRuntime.ApplyRadiusDamage(
                    resolvedDeathPoint,
                    Mathf.Max(0.3f, profile.SplitExplosionRange),
                    projectile.context,
                    Mathf.Max(0.1f, profile.SplitExplosionDamageFactor),
                    true,
                    true,
                    0f);
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
            if (playFx)
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
            Vector3 vertical = Vector3.Cross(forward, lateral).normalized;

            Vector3 offset = lateral * (Mathf.Sin(phase) * profile.HelixAmplitude);
            offset += vertical * (Mathf.Cos(phase) * profile.HelixAmplitude * 0.55f);
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
                if (receiver == null || DragonKingBossGunRuntime.SharedReceiverIdSet.Contains(receiver.GetInstanceID()))
                {
                    continue;
                }

                DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiver.GetInstanceID());
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

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
        }

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
        private readonly HashSet<int> damagedReceiverIds = new HashSet<int>();

        public bool IsActiveForRuntime
        {
            get { return projectile != null && profile != null; }
        }

        public bool UsesCustomMovement
        {
            get { return IsActiveForRuntime && profile.RequiresCustomMovement; }
        }

        public bool IsDead
        {
            get { return projectile != null && deadRef(projectile); }
        }

        private void CreateStickyCharge(DamageReceiver target, Vector3 point, Vector3 normal)
        {
            GameObject chargeObj = new GameObject("DragonGun_StickyCharge_" + profile.Id);
            chargeObj.transform.position = point;
            
            if (profile.Element == ElementTypes.fire)
            {
                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(chargeObj);
            }

            DragonKingBossGunStickyCharge charge = chargeObj.AddComponent<DragonKingBossGunStickyCharge>();
            charge.Initialize(projectile.context, profile, shotId, target != null ? target.transform : null, point);
        }

        private void CreateGroundZone(Vector3 position)
        {
            GameObject zoneObj = new GameObject("DragonGun_GroundZone_" + profile.Id);
            zoneObj.transform.position = FenHuangHalberdRuntime.SnapToGround(position, position.y);
            
            if (profile.Element == ElementTypes.fire)
            {
                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(zoneObj);
            }

            DragonKingBossGunGroundZone zone = zoneObj.AddComponent<DragonKingBossGunGroundZone>();
            zone.Initialize(projectile.context, profile);
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

        public void Initialize(Projectile projectileInstance, ItemAgent_Gun gunAgent, DragonKingBossGunShotProfile shotProfile, int currentShotId, int currentProjectileIndex, bool isSecondary)
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
            damagedReceiverIds.Clear();

            savedExplosionFx = projectileInstance != null ? projectileInstance.explosionFx : null;
            if (projectileInstance != null)
            {
                projectileInstance.explosionFx = null;
            }

            if (customTrailInstance == null && profile != null && !string.IsNullOrEmpty(profile.TrailFxPrefab))
            {
                customTrailInstance = DragonKingAssetManager.InstantiateEffect(profile.TrailFxPrefab, transform.position, transform.rotation, transform);
            }
            else if (customTrailInstance == null && profile != null && profile.Element == ElementTypes.fire)
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

            // 保留 Boss_Red 原版弹幕视觉，缩放已在 SpawnDragonProjectile 中通过 transform.localScale 处理
            enabled = true;
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
            damagedReceiverIds.Clear();
            savedExplosionFx = null;

            if (customTrailInstance != null)
            {
                customTrailInstance.transform.SetParent(null);
                FadeAndDestroy(customTrailInstance, 2f);
                customTrailInstance = null;
            }
        }

        public void OnBeforeBaseMove()
        {
            if (!IsActiveForRuntime)
            {
                return;
            }

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
                TriggerSplit();
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
                TriggerSplit();
                deadRef(projectile) = true;
            }

            if (deadRef(projectile))
            {
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
            UpdateDirectionAndVelocity(deltaTime);

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
                    deadRef(projectile) = true;
                }
            }

            if (!deadRef(projectile) && !stopMovementThisFrame)
            {
                transform.position += directionRef(projectile) * distanceThisFrame;
                traveledDistanceRef(projectile) = traveledDistanceRef(projectile) + distanceThisFrame;
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
            else if (projectile.context.traceTarget != null && projectile.context.traceAbility > 0.01f)
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

            receiver.Hurt(damageInfo);
            receiver.AddBuff(GameplayDataSettings.Buffs.Pain, projectile.context.fromCharacter);
            successfulHits++;
            remainingTargetHits--;

            if (profile.UseSticky)
            {
                CreateStickyCharge(receiver, hitPoint, hitNormal);
                transform.position = hitPoint;
                deadRef(projectile) = true;
                return true;
            }

            if (remainingTargetHits <= 0)
            {
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
                transform.position = hitPoint;
                deadRef(projectile) = true;
                return true;
            }

            if (remainingBounce > 0)
            {
                Bounce(hitObject, hitPoint, hitNormal);
                return true;
            }

            transform.position = hitPoint;
            deadRef(projectile) = true;
            SpawnObstacleHitFx(hitPoint, hitNormal);
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
            SpawnObstacleHitFx(hitPoint, hitNormal);
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

            GameObject hitFx = GameplayDataSettings.Prefabs != null ? GameplayDataSettings.Prefabs.BulletHitObsticleFx : null;
            if (hitFx != null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(hitFx, hitPoint, Quaternion.LookRotation(hitNormal, Vector3.up));
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance, 2f);
                }
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
            remainingTargetHits = 1;

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

            if (!secondaryProjectile && profile.ExplosionRange > 0f)
            {
                DragonKingBossGunRuntime.TrySpawnExplosionFx(transform.position, profile);
                DragonKingBossGunRuntime.ApplyRadiusDamage(
                    transform.position,
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

            if (!secondaryProjectile && profile.UseSplit && !splitTriggered && !profile.SplitOnAirburst)
            {
                TriggerSplit();
            }

            if (!secondaryProjectile && profile.UseGroundZone)
            {
                DragonKingBossGunRuntime.SpawnGroundZone(transform.position, sourceGun, projectile.context, profile);
            }
        }

        private void TriggerSplit()
        {
            if (splitTriggered)
            {
                return;
            }

            splitTriggered = true;
            DragonKingBossGunRuntime.SpawnSplitProjectiles(sourceGun, profile, shotId, transform.position, directionRef(projectile), Vector3.up);
            DragonKingBossGunRuntime.TrySpawnExplosionFx(transform.position, profile);
        }

        private void ApplyHelixOffset()
        {
            float phase = traveledDistanceRef(projectile) * profile.HelixFrequency + ((projectileIndex & 1) == 0 ? 0f : Mathf.PI);
            Vector3 offset = transform.right * (Mathf.Sin(phase) * profile.HelixAmplitude);
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

    internal sealed class DragonKingBossGunGroundZone : MonoBehaviour
    {
        private const float MaxLifetime = 15f;

        private ProjectileContext sourceContext;
        private DragonKingBossGunShotProfile profile;
        private float radius;
        private float duration;
        private float tickDamageFactor;
        private float tickTimer;
        private float elapsed;

        public void Initialize(ProjectileContext projectileContext, DragonKingBossGunShotProfile profile)
        {
            sourceContext = projectileContext;
            this.profile = profile;
            radius = Mathf.Max(0.25f, profile.GroundZoneRadius);
            duration = Mathf.Max(0.2f, profile.GroundZoneDuration);
            tickDamageFactor = Mathf.Max(0.05f, profile.GroundZoneTickDamageFactor);
            tickTimer = 0f;
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            tickTimer += Time.deltaTime;
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
                if (receiver == null || DragonKingBossGunRuntime.SharedReceiverIdSet.Contains(receiver.GetInstanceID()))
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

                DragonKingBossGunRuntime.SharedReceiverIdSet.Add(receiver.GetInstanceID());
                Vector3 damagePoint = receiver.transform.position + Vector3.up * 0.35f;
                Vector3 damageNormal = (receiver.transform.position - transform.position).normalized;
                if (damageNormal.sqrMagnitude < 0.001f)
                {
                    damageNormal = Vector3.up;
                }

                DamageInfo damageInfo = DragonKingBossGunRuntime.CreateDamageInfo(sourceContext, tickDamageFactor, damagePoint, damageNormal, true, true);
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
    }

    internal sealed class DragonKingBossGunStickyCharge : MonoBehaviour
    {
        private const float MaxLifetime = 10f;

        private ProjectileContext sourceContext;
        private DragonKingBossGunShotProfile profile;
        private int shotId;
        private Transform followTarget;
        private Vector3 localOffset;
        private float elapsed;

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
            if (followTarget != null)
            {
                transform.position = followTarget.TransformPoint(localOffset);
            }

            if (profile == null || elapsed < Mathf.Max(0.05f, profile.StickyDelay))
            {
                if (elapsed >= MaxLifetime)
                {
                    DragonKingBossGunProjectileAgent.FadeAndDestroy(gameObject, 2f);
                    enabled = false;
                }

                return;
            }

            DragonKingBossGunRuntime.TrySpawnExplosionFx(transform.position, profile);
            float marker = DragonKingBossGunRuntime.EncodeShotMarker(
                shotId,
                profile.Id,
                DragonKingBossGunRuntime.DragonKingBossGunHitStage.Secondary);
            DragonKingBossGunRuntime.ApplyRadiusDamage(
                transform.position,
                Mathf.Max(0.4f, profile.StickyExplosionRange),
                sourceContext,
                Mathf.Max(0.2f, profile.StickyExplosionDamageFactor),
                false,
                true,
                marker);
            DragonKingBossGunProjectileAgent.FadeAndDestroy(gameObject, 2f);
            enabled = false;
        }
    }
}

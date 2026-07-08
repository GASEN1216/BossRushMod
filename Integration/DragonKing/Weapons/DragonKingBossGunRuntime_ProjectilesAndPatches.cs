using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using TMPro;
using UnityEngine;

namespace BossRush
{
    public static partial class DragonKingBossGunRuntime
    {
        private const float DamageNormalFallbackSqr = 0.0000000001f;

        private static Vector3 ApplyWeaponScatter(ItemAgent_Gun gun, Vector3 shootDirection)
        {
            bool isMainCharacterShot = gun.Holder != null && gun.Holder.IsMainCharacter;
            float extraScatter = 0f;
            if (isMainCharacterShot)
            {
                extraScatter = Mathf.Max(1f, gun.CurrentScatter) * Mathf.Lerp(1.5f, 0f, Mathf.InverseLerp(0f, 0.5f, gun.durabilityPercent));
            }

            float randomYaw = UnityEngine.Random.Range(-0.5f, 0.5f) * (gun.CurrentScatter + extraScatter);
            Vector3 adjustedDirection = Quaternion.Euler(0f, randomYaw, 0f) * shootDirection;
            adjustedDirection.Normalize();
            return adjustedDirection;
        }

        private static void SpawnPrimaryProjectiles(ItemAgent_Gun gun, DragonKingBossGunShotProfile profile, Vector3 muzzlePoint, Vector3 shootDirection, Vector3 firstFrameCheckStartPoint)
        {
            int shotId = NextShotId();
            int dirCount = BuildFanDirections(shootDirection, profile.ShotCount, profile.SpreadAngle, fanDirectionBuffer, profile.RandomSpread);

            for (int i = 0; i < dirCount; i++)
            {
                SpawnDragonProjectile(
                    gun,
                    profile,
                    shotId,
                    muzzlePoint,
                    fanDirectionBuffer[i],
                    firstFrameCheckStartPoint,
                    dirCount,
                    i,
                    false,
                    1f,
                    1f,
                    1f,
                    1f,
                    -1f,
                    DragonKingBossGunHitStage.Primary);
            }
        }

        internal static void SpawnSplitProjectiles(ItemAgent_Gun gun, DragonKingBossGunShotProfile profile, int shotId, Vector3 origin, Vector3 forward, Vector3 normal, int sourceReceiverId = -1)
        {
            if (gun == null || profile == null || profile.SplitCount <= 0)
            {
                return;
            }

            int dirCount = BuildSplitDirections(profile, forward, normal, splitDirectionBuffer);
            for (int i = 0; i < dirCount; i++)
            {
                SpawnDragonProjectile(
                    gun,
                    profile,
                    shotId,
                    origin,
                    splitDirectionBuffer[i],
                    origin,
                    dirCount,
                    i,
                    true,
                    profile.SplitScale,
                    profile.SplitSpeedFactor,
                    profile.SplitDistanceFactor,
                    profile.SplitDamageFactor,
                    profile.SplitTraceAbility > 0f ? profile.SplitTraceAbility : -1f,
                    DragonKingBossGunHitStage.Secondary,
                    sourceReceiverId);
            }
        }

        internal static void SpawnGroundZone(Vector3 position, ItemAgent_Gun gun, ProjectileContext sourceContext, DragonKingBossGunShotProfile profile)
        {
            if (gun == null || profile == null || !profile.UseGroundZone)
            {
                return;
            }

            if (!TryClaimGroundZoneSpawn(profile, sourceContext.buffChance))
            {
                return;
            }

            GameObject zoneObject = new GameObject("DragonKingBossGunGroundZone");
            zoneObject.transform.position = FenHuangHalberdRuntime.SnapToGround(position, position.y);
            DragonKingBossGunGroundZone zone = zoneObject.AddComponent<DragonKingBossGunGroundZone>();
            zone.Initialize(sourceContext, profile);
        }

        private static bool TryClaimGroundZoneSpawn(DragonKingBossGunShotProfile profile, float marker)
        {
            if (profile == null || profile.MaxGroundZonesPerShot <= 0)
            {
                return true;
            }

            int shotId;
            DragonKingBossGunProfileId profileId;
            DragonKingBossGunHitStage hitStage;
            if (!TryDecodeShotMarker(marker, out shotId, out profileId, out hitStage))
            {
                return true;
            }

            long key = ComposeProcessedHitKey(shotId, (int)profileId, DragonKingBossGunHitStage.Primary);
            ProcessedGroundZoneState state;
            if (!processedGroundZoneShots.TryGetValue(key, out state) || Time.time - state.time >= ProcessedHitKeepTime)
            {
                state = default(ProcessedGroundZoneState);
            }

            if (state.spawnCount >= profile.MaxGroundZonesPerShot)
            {
                return false;
            }

            state.time = Time.time;
            state.spawnCount++;
            processedGroundZoneShots[key] = state;
            CleanupProcessedHitPairs();
            return true;
        }

        internal static void SpawnStickyCharge(Vector3 position, Vector3 normal, Transform followTarget, ItemAgent_Gun gun, ProjectileContext sourceContext, DragonKingBossGunShotProfile profile, int shotId)
        {
            if (gun == null || profile == null || !profile.UseSticky)
            {
                return;
            }

            GameObject stickyObject = new GameObject("DragonKingBossGunStickyCharge");
            stickyObject.transform.position = position;
            if (normal.sqrMagnitude > 0.001f)
            {
                stickyObject.transform.rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
            }

            DragonKingBossGunStickyCharge sticky = stickyObject.AddComponent<DragonKingBossGunStickyCharge>();
            sticky.Initialize(sourceContext, profile, shotId, followTarget, position);
        }

        internal static void ApplyRadiusDamage(Vector3 position, float radius, ProjectileContext sourceContext, float damageFactor, bool treatAsEffect, bool ignoreOwner, float markerOverride = -1f)
        {
            if (radius <= 0f)
            {
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(position, radius, SharedColliderBuffer, GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Ignore);
            SharedReceiverIdSet.Clear();

            for (int i = 0; i < count; i++)
            {
                DamageReceiver receiver = SharedColliderBuffer[i] != null ? SharedColliderBuffer[i].GetComponent<DamageReceiver>() : null;
                if (receiver == null)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (SharedReceiverIdSet.Contains(receiverId))
                {
                    continue;
                }

                if (sourceContext.team == receiver.Team && receiver.Team != Teams.all)
                {
                    continue;
                }

                CharacterMainControl receiverCharacter = receiver.health != null ? receiver.health.TryGetCharacter() : null;
                if (ignoreOwner && receiverCharacter != null && receiverCharacter == sourceContext.realFromCharacter)
                {
                    continue;
                }

                SharedReceiverIdSet.Add(receiverId);

                Vector3 damagePoint = receiver.transform.position + Vector3.up * 0.35f;
                Vector3 damageNormal = receiver.transform.position - position;
                if (damageNormal.sqrMagnitude <= DamageNormalFallbackSqr)
                {
                    damageNormal = Vector3.up;
                }

                DamageInfo damageInfo = CreateDamageInfo(sourceContext, damageFactor, damagePoint, damageNormal, treatAsEffect, treatAsEffect, markerOverride);
                receiver.Hurt(damageInfo);
                receiver.AddBuff(GameplayDataSettings.Buffs.Pain, sourceContext.fromCharacter);
            }
        }

        internal static void TrySpawnExplosionFx(Vector3 position, DragonKingBossGunShotProfile profile = null)
        {
            float fxDuration = profile != null ? profile.ExplosionFxDuration : 3f;

            if (profile != null && !string.IsNullOrEmpty(profile.ExplosionFxPrefab))
            {
                GameObject fx = DragonKingAssetManager.InstantiateEffect(profile.ExplosionFxPrefab, position, Quaternion.identity);
                if (fx != null)
                {
                    UnityEngine.Object.Destroy(fx, fxDuration);
                    return;
                }
            }

            if (profile != null && profile.Element == ElementTypes.fire)
            {
                GameObject fireFx = new GameObject("DragonGun_FireExplosionFx");
                fireFx.transform.position = position;
                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(fireFx);
                float fireFxLifetime = Mathf.Clamp(fxDuration, 0.2f, 2f);

                ParticleSystem[] particles = fireFx.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particles)
                {
                    var main = ps.main;
                    main.loop = false;
                    main.duration = Mathf.Min(0.5f, fireFxLifetime);

                    var em = ps.emission;
                    em.rateOverDistance = 0;
                    em.rateOverTime = 0;
                    em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 30) });

                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 1f;

                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }

                UnityEngine.Object.Destroy(fireFx, fireFxLifetime);
                return;
            }

            if (cachedExplosionFx != null)
            {
                GameObject fx = UnityEngine.Object.Instantiate(cachedExplosionFx, position, Quaternion.identity);
                if (fx != null)
                {
                    UnityEngine.Object.Destroy(fx, fxDuration);
                }
            }
        }

        internal static bool IsGroundSurface(GameObject hitObject, Vector3 hitNormal)
        {
            int layer = hitObject != null ? hitObject.layer : -1;
            if (layer >= 0 && ((FenHuangHalberdRuntime.GroundLayerMask & (1 << layer)) != 0))
            {
                return true;
            }

            return hitNormal.sqrMagnitude > 0.001f && Vector3.Dot(hitNormal.normalized, Vector3.up) >= GroundImpactDotThreshold;
        }

        internal static DamageInfo CreateDamageInfo(ProjectileContext sourceContext, float damageFactor, Vector3 point, Vector3 normal, bool isFromEffect, bool suppressMarks, float markerOverride = -1f)
        {
            DamageInfo damageInfo = new DamageInfo(sourceContext.fromCharacter);
            damageInfo.damageValue = sourceContext.damage * damageFactor;
            damageInfo.critDamageFactor = sourceContext.critDamageFactor;
            damageInfo.critRate = sourceContext.critRate;
            damageInfo.armorPiercing = sourceContext.armorPiercing;
            damageInfo.armorBreak = sourceContext.armorBreak;
            damageInfo.damagePoint = point;
            damageInfo.damageNormal = normal.normalized;
            damageInfo.damageType = DamageTypes.normal;
            damageInfo.fromWeaponItemID = sourceContext.fromWeaponItemID;
            damageInfo.bleedChance = suppressMarks ? 0f : sourceContext.bleedChance;
            damageInfo.buffChance = suppressMarks ? 0f : (markerOverride >= 0f ? markerOverride : sourceContext.buffChance);
            damageInfo.buff = suppressMarks ? null : sourceContext.buff;
            damageInfo.isFromBuffOrEffect = isFromEffect;

            ApplyContextElements(ref damageInfo, sourceContext);
            return damageInfo;
        }

        internal static void ApplyContextElements(ref DamageInfo damageInfo, ProjectileContext sourceContext)
        {
            damageInfo.AddElementFactor(ElementTypes.physics, sourceContext.element_Physics);
            damageInfo.AddElementFactor(ElementTypes.fire, sourceContext.element_Fire);
            damageInfo.AddElementFactor(ElementTypes.poison, sourceContext.element_Poison);
            damageInfo.AddElementFactor(ElementTypes.electricity, sourceContext.element_Electricity);
            damageInfo.AddElementFactor(ElementTypes.space, sourceContext.element_Space);
            damageInfo.AddElementFactor(ElementTypes.ghost, sourceContext.element_Ghost);
            damageInfo.AddElementFactor(ElementTypes.ice, sourceContext.element_Ice);
        }

        private static Projectile SpawnDragonProjectile(
            ItemAgent_Gun gun,
            DragonKingBossGunShotProfile profile,
            int shotId,
            Vector3 muzzlePoint,
            Vector3 direction,
            Vector3 firstFrameCheckStartPoint,
            int projectileCount,
            int projectileIndex,
            bool isSecondary,
            float scaleFactor,
            float speedFactor,
            float distanceFactor,
            float damageFactor,
            float traceAbilityOverride,
            DragonKingBossGunHitStage hitStage,
            int sourceReceiverId = -1)
        {
            bool useNative = profile != null && profile.UseNativeProjectile && isSecondary;
            Projectile nativeProjectile = useNative ? GetNativeRocketProjectile() : null;
            Projectile baseProjectile = nativeProjectile != null ? nativeProjectile : GetDragonProjectile();
            if (baseProjectile == null || LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
            {
                return null;
            }

            if (!useNative && gun != null && gun.GunItemSetting != null && gun.GunItemSetting.bulletPfb != baseProjectile)
            {
                gun.GunItemSetting.bulletPfb = baseProjectile;
            }

            Projectile projectile = LevelManager.Instance.BulletPool.GetABullet(baseProjectile);
            if (projectile == null)
            {
                return null;
            }

            projectile.transform.position = muzzlePoint;

            if (useNative && nativeProjectile != null)
            {
                float nativeScale = Mathf.Max(0.2f, scaleFactor);
                projectile.transform.localScale = nativeProjectile.transform.localScale * nativeScale;
                projectile.radius = nativeProjectile.radius * Mathf.Max(0.25f, nativeScale);
            }
            else
            {
                float finalScale = Mathf.Max(0.2f, profile.Scale * scaleFactor);
                projectile.transform.localScale = cachedProjectileScale * finalScale;
                projectile.radius = cachedProjectileRadius * Mathf.Max(0.25f, finalScale);
            }

            ProjectileContext context = BuildProjectileContext(
                gun,
                profile,
                direction,
                firstFrameCheckStartPoint,
                projectileCount,
                shotId,
                speedFactor,
                distanceFactor,
                damageFactor,
                traceAbilityOverride,
                isSecondary,
                hitStage);

            projectile.transform.rotation = Quaternion.LookRotation(context.direction, Vector3.up);
            projectile.Init(context);

            DragonKingBossGunProjectileAgent agent = projectile.GetComponent<DragonKingBossGunProjectileAgent>();
            if (agent == null)
            {
                agent = projectile.gameObject.AddComponent<DragonKingBossGunProjectileAgent>();
            }

            agent.Initialize(projectile, gun, profile, shotId, projectileIndex, isSecondary, sourceReceiverId);
            return projectile;
        }

        private static ProjectileContext BuildProjectileContext(
            ItemAgent_Gun gun,
            DragonKingBossGunShotProfile profile,
            Vector3 direction,
            Vector3 firstFrameCheckStartPoint,
            int projectileCount,
            int shotId,
            float speedFactor,
            float distanceFactor,
            float damageFactor,
            float traceAbilityOverride,
            bool isSecondary,
            DragonKingBossGunHitStage hitStage)
        {
            bool isMainCharacterShot = gun != null && gun.Holder != null && gun.Holder.IsMainCharacter;
            float characterDamageMultiplier = gun != null ? gun.CharacterDamageMultiplier : 1f;

            ProjectileContext context = default(ProjectileContext);
            context.firstFrameCheck = true;
            context.firstFrameCheckStartPoint = firstFrameCheckStartPoint;

            // 瞄准投掷模式：根据鼠标位置计算抛物线
            if (!isSecondary && profile.UseAimedArc && profile.Gravity > 0f && gun != null && gun.Holder != null && gun.Holder.IsMainCharacter)
            {
                Vector3 aimPoint = gun.Holder.GetCurrentAimPoint();
                Vector3 toTarget = aimPoint - firstFrameCheckStartPoint;
                toTarget.y = 0f;
                float toTargetDistanceSqr = toTarget.sqrMagnitude;
                float toTargetDistance = Mathf.Sqrt(toTargetDistanceSqr);
                float horizontalDist = Mathf.Max(1f, toTargetDistance);
                Vector3 horizontalDir = toTargetDistanceSqr > 0.0000000001f ? toTarget / toTargetDistance : Vector3.zero;

                // 55° 固定仰角: cos≈0.5736, sin≈0.8192, sin(110°)≈0.9397
                float speed = Mathf.Clamp(Mathf.Sqrt(Mathf.Abs(profile.Gravity * horizontalDist * 1.0642f)), 8f, 80f);

                context.direction = (horizontalDir * 0.5736f + Vector3.up * 0.8192f).normalized;
                context.speed = speed;
            }
            else
            {
                context.direction = ResolveLaunchDirection(direction.normalized, profile, isSecondary);
                context.speed = (gun != null ? gun.BulletSpeed : 90f) * profile.SpeedFactor * speedFactor;
            }
            context.traceTarget = null;
            context.traceAbility = traceAbilityOverride >= 0f ? traceAbilityOverride : profile.TraceAbility;
            if (isSecondary && profile.SplitGravity > 0f)
            {
                context.traceAbility = 0f;
            }

            if (context.traceAbility > 0.01f && gun != null)
            {
                context.traceTarget = GetTraceTarget(gun);
            }

            context.controlMindType = gun != null ? gun.ControlMindType : ControlMindTypes.none;
            if (gun != null && gun.GunItemSetting != null && !gun.GunItemSetting.CanControlMind)
            {
                context.controlMindType = ControlMindTypes.none;
            }

            context.controlMindTime = gun != null ? gun.ControlMindTime : 0f;
            if (gun != null && gun.Holder != null)
            {
                context.team = gun.Holder.Team;
                context.speed *= gun.Holder.GunBulletSpeedMultiplier;
            }
            else
            {
                context.team = Teams.all;
            }

            context.distance = (gun != null ? gun.BulletDistance : 24f) *
                               profile.DistanceFactor *
                               Mathf.Max(0.05f, profile.LifetimeFactor) *
                               distanceFactor +
                               0.4f;
            context.halfDamageDistance = context.distance * 0.5f;
            if (!isMainCharacterShot)
            {
                context.distance *= 1.05f;
            }
            if (isSecondary && profile.SplitMaxLifetimeSeconds > 0f)
            {
                context.distance = Mathf.Max(0.4f, context.speed * profile.SplitMaxLifetimeSeconds);
                context.halfDamageDistance = context.distance * 0.5f;
            }

            context.penetrate = Mathf.Max(gun != null ? gun.Penetrate : 0, profile.Pierce);
            int damageProjectileCount = profile.DivideDamageByProjectileCount ? projectileCount : 1;
            context.damage = (gun != null ? gun.Damage : 20f) *
                             (gun != null ? gun.BulletDamageMultiplier : 1f) *
                             profile.DamageFactor *
                             damageFactor *
                             characterDamageMultiplier /
                             Mathf.Max(1, damageProjectileCount);
            if (gun != null && gun.Damage > 1f && context.damage < 1f)
            {
                context.damage = 1f;
            }

            context.dmgOverDistance = gun != null ? gun.DmgOverDistance : 0.5f;
            context.critDamageFactor = gun != null
                ? (gun.CritDamageFactor + gun.BulletCritDamageFactorGain) * (1f + gun.CharacterGunCritDamageGain)
                : 1.5f;
            context.critRate = gun != null
                ? gun.CritRate * (1f + gun.CharacterGunCritRateGain)
                : 0f;
            if (isMainCharacterShot && LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
            {
                context.critRate = LevelManager.Instance.InputManager.AimingEnemyHead ? 1f : context.critRate;
            }

            context.armorPiercing = gun != null ? gun.ArmorPiercing + gun.BulletArmorPiercingGain : 0f;
            context.armorBreak = gun != null ? gun.ArmorBreak + gun.BulletArmorBreakGain : 0f;
            context.fromCharacter = gun != null ? gun.Holder : null;
            context.realFromCharacter = gun != null ? gun.Holder : null;
            if (gun != null && gun.Holder != null && LevelManager.Instance != null && gun.Holder == LevelManager.Instance.ControllingCharacter)
            {
                context.fromCharacter = CharacterMainControl.Main;
            }

            context.gravity = ResolveGravity(profile);
            if (isSecondary && profile.SplitGravity > 0f)
            {
                context.gravity = profile.SplitGravity;
            }

            ApplyElement(ref context, profile.Element);

            if (isSecondary && profile.UseNativeProjectile && profile.SplitExplosionRange > 0f)
            {
                context.explosionRange = Mathf.Max(0.3f, profile.SplitExplosionRange);
                context.explosionDamage = context.damage * Mathf.Max(0.1f, profile.SplitExplosionDamageFactor);
            }

            context.fromGunItemSetting = gun != null ? gun.GunItemSetting : null;
            context.fromWeaponItemID = gun != null && gun.Item != null ? gun.Item.TypeID : 0;
            context.buff = null;
            context.buffChance = EncodeShotMarker(shotId, profile.Id, hitStage);
            context.bleedChance = gun != null ? gun.BulletBleedChance : 0f;

            if (gun != null && gun.Holder != null && isMainCharacterShot && gun.Holder.HasNearByHalfObsticle())
            {
                context.ignoreHalfObsticle = true;
            }

            if (context.critRate > 0.99f)
            {
                context.ignoreHalfObsticle = true;
            }

            return context;
        }

        private static float ResolveGravity(DragonKingBossGunShotProfile profile)
        {
            if (profile == null || profile.Arc == DragonKingBossGunArcMode.None)
            {
                return 0f;
            }

            return Mathf.Max(0f, profile.Gravity);
        }

        private static Vector3 ResolveLaunchDirection(Vector3 direction, DragonKingBossGunShotProfile profile, bool isSecondary)
        {
            if (profile == null)
            {
                return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            }

            if (isSecondary && profile.SplitGravity > 0f)
            {
                return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            }

            if (profile.Arc == DragonKingBossGunArcMode.None)
            {
                return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            }

            float lift = profile.ArcLift;
            if (lift <= 0f)
            {
                lift = profile.Arc == DragonKingBossGunArcMode.High ? 0.8f : 0.28f;
            }

            Vector3 adjusted = direction + Vector3.up * lift;
            return adjusted.sqrMagnitude > 0.001f ? adjusted.normalized : Vector3.up;
        }

        private static void ApplyElement(ref ProjectileContext context, ElementTypes element)
        {
            switch (element)
            {
                case ElementTypes.physics:
                    context.element_Physics = 1f;
                    break;
                case ElementTypes.poison:
                    context.element_Poison = 1f;
                    break;
                case ElementTypes.electricity:
                    context.element_Electricity = 1f;
                    break;
                case ElementTypes.space:
                    context.element_Space = 1f;
                    break;
                case ElementTypes.ghost:
                    context.element_Ghost = 1f;
                    break;
                case ElementTypes.ice:
                    context.element_Ice = 1f;
                    break;
                case ElementTypes.fire:
                default:
                    context.element_Fire = 1f;
                    break;
            }
        }

        private static int BuildFanDirections(Vector3 forward, int shotCount, float spreadAngle, Vector3[] buffer, bool randomSpread = false)
        {
            shotCount = Mathf.Clamp(shotCount, 1, buffer.Length);
            if (shotCount == 1)
            {
                buffer[0] = forward.normalized;
                return 1;
            }

            float halfSpread = spreadAngle * 0.5f;
            for (int i = 0; i < shotCount; i++)
            {
                float angle;
                if (randomSpread)
                {
                    angle = UnityEngine.Random.Range(-halfSpread, halfSpread);
                }
                else
                {
                    float t = (float)i / (shotCount - 1);
                    angle = Mathf.Lerp(-halfSpread, halfSpread, t);
                }

                float pitch = randomSpread ? UnityEngine.Random.Range(-halfSpread * 0.2f, halfSpread * 0.2f) : 0f;
                buffer[i] = (Quaternion.Euler(pitch, angle, 0f) * forward).normalized;
            }

            return shotCount;
        }

        private static int BuildSplitDirections(DragonKingBossGunShotProfile profile, Vector3 forward, Vector3 normal, Vector3[] buffer)
        {
            int count = Mathf.Clamp(profile.SplitCount, 1, buffer.Length);
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            switch (profile.SplitPattern)
            {
                case DragonKingBossGunSplitPattern.DownBurst:
                {
                    Vector3 downForward = (Vector3.down + forward * 0.2f).normalized;
                    for (int i = 0; i < count; i++)
                    {
                        float angle = ((360f / count) * i) + UnityEngine.Random.Range(-8f, 8f);
                        Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * downForward;
                        dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, profile.SplitSpreadAngle * 0.35f), Vector3.Cross(dir, Vector3.up)) * dir;
                        buffer[i] = dir.normalized;
                    }

                    return count;
                }

                case DragonKingBossGunSplitPattern.Radial:
                {
                    bool useParabolicScatter = profile.SplitGravity > 0f;
                    bool forceHorizontalScatter = profile.Id == DragonKingBossGunProfileId.Energy;
                    Vector3 axis = (useParabolicScatter || forceHorizontalScatter)
                        ? Vector3.up
                        : (normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up);
                    Vector3 radialBase;
                    if (useParabolicScatter || forceHorizontalScatter)
                    {
                        radialBase = Vector3.ProjectOnPlane(forward, Vector3.up);
                        if (radialBase.sqrMagnitude < 0.001f)
                        {
                            radialBase = Vector3.forward;
                        }
                    }
                    else
                    {
                        radialBase = Vector3.Cross(axis, Vector3.right);
                        if (radialBase.sqrMagnitude < 0.001f)
                        {
                            radialBase = Vector3.Cross(axis, Vector3.forward);
                        }
                    }

                    radialBase.Normalize();
                    for (int i = 0; i < count; i++)
                    {
                        float angle = 360f * i / count + UnityEngine.Random.Range(-12f, 12f);
                        Vector3 dir = Quaternion.AngleAxis(angle, axis) * radialBase;
                        if (useParabolicScatter)
                        {
                            float downwardBias = UnityEngine.Random.Range(0.03f, 0.12f);
                            dir = (dir + Vector3.down * downwardBias).normalized;
                        }
                        else if (forceHorizontalScatter)
                        {
                            Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
                            if (horizontalForward.sqrMagnitude < 0.001f)
                            {
                                horizontalForward = radialBase;
                            }

                            dir = Vector3.ProjectOnPlane(dir + horizontalForward.normalized * 0.25f, Vector3.up).normalized;
                        }
                        else
                        {
                            dir = (dir + forward * 0.25f).normalized;
                        }

                        buffer[i] = dir;
                    }

                    return count;
                }

                case DragonKingBossGunSplitPattern.ForwardFan:
                default:
                    return BuildFanDirections(forward, count, profile.SplitSpreadAngle, buffer, false);
            }
        }

        [HarmonyPatch(typeof(ItemSetting_Gun), "IsValidBullet")]
        private static class MultiCaliberIsValidBulletPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(ItemSetting_Gun __instance, Item newBulletItem, ref bool __result)
            {
                if (__instance == null || !IsDragonKingBossGun(__instance.Item))
                {
                    return true;
                }

                if (newBulletItem == null || !DragonKingBossGunProfiles.IsBulletLike(newBulletItem))
                {
                    __result = false;
                    return false;
                }

                Item currentLoadedBullet = __instance.GetCurrentLoadedBullet();
                if (currentLoadedBullet != null &&
                    currentLoadedBullet.TypeID == newBulletItem.TypeID &&
                    __instance.BulletCount >= __instance.Capacity)
                {
                    __result = false;
                    return false;
                }

                __result = IsCompatibleBullet(__instance.Item, newBulletItem);
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemSetting_Gun), "AutoSetTypeInInventory")]
        private static class MultiCaliberAutoSetTypePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(ItemSetting_Gun __instance, Inventory inventory, ref bool __result)
            {
                if (__instance == null || !IsDragonKingBossGun(__instance.Item))
                {
                    return true;
                }

                Item currentLoadedBullet = __instance.GetCurrentLoadedBullet();
                if (currentLoadedBullet != null && IsCompatibleBullet(__instance.Item, currentLoadedBullet))
                {
                    __instance.SetTargetBulletType(currentLoadedBullet);
                    ApplyAmmoAttributeFromBullet(__instance, currentLoadedBullet);
                    __result = true;
                    return false;
                }

                __instance.SetTargetBulletType(-1);
                if (inventory != null)
                {
                    foreach (Item item in inventory)
                    {
                        if (!IsCompatibleBullet(__instance.Item, item))
                        {
                            continue;
                        }

                        __instance.SetTargetBulletType(item);
                        ApplyAmmoAttributeFromBullet(__instance, item);
                        __result = true;
                        return false;
                    }
                }

                __result = false;
                return false;
            }

            private static void ApplyAmmoAttributeFromBullet(ItemSetting_Gun gunSetting, Item bulletItem)
            {
                DragonKingBossGunShotProfile profile;
                if (DragonKingBossGunProfiles.TryResolve(bulletItem, out profile))
                {
                    TryApplyAmmoProfile(gunSetting != null ? gunSetting.Item : null, profile, "AutoSetTypeInInventory");
                }
            }
        }

        [HarmonyPatch(typeof(ItemSetting_Gun), "SetTargetBulletType", new Type[] { typeof(int) })]
        private static class DragonKingBossGunSetTargetBulletTypePatch
        {
            [HarmonyPostfix]
            private static void Postfix(ItemSetting_Gun __instance, int typeID)
            {
                TryApplyAmmoProfileFromTargetType(__instance, typeID, "SetTargetBulletType");
            }
        }

        [HarmonyPatch(typeof(ItemSetting_Gun), "GetBulletTypesInInventory")]
        private static class MultiCaliberGetBulletTypesPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(ItemSetting_Gun __instance, Inventory inventory, ref Dictionary<int, BulletTypeInfo> __result)
            {
                if (__instance == null || !IsDragonKingBossGun(__instance.Item))
                {
                    return true;
                }

                reusableBulletTypeDict.Clear();
                if (inventory != null)
                {
                    foreach (Item item in inventory)
                    {
                        if (!IsCompatibleBullet(__instance.Item, item))
                        {
                            continue;
                        }

                        BulletTypeInfo info;
                        if (!reusableBulletTypeDict.TryGetValue(item.TypeID, out info))
                        {
                            info = new BulletTypeInfo();
                            info.bulletTypeID = item.TypeID;
                            info.count = item.StackCount;
                            reusableBulletTypeDict.Add(item.TypeID, info);
                        }
                        else
                        {
                            info.count += item.StackCount;
                        }
                    }
                }

                __result = reusableBulletTypeDict.Count > 0
                    ? new Dictionary<int, BulletTypeInfo>(reusableBulletTypeDict)
                    : emptyBulletTypeDict;
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemAgent_Gun), "UpdateStates")]
        private static class DragonKingBossGunReloadCompletePatch
        {
            [HarmonyPrefix]
            private static void Prefix(ItemAgent_Gun __instance, out bool __state)
            {
                __state = __instance != null &&
                          IsDragonKingBossGun(__instance.Item) &&
                          __instance.IsReloading();
            }

            [HarmonyPostfix]
            private static void Postfix(ItemAgent_Gun __instance, bool __state)
            {
                if (!__state || __instance == null || __instance.IsReloading())
                {
                    return;
                }

                TryApplyAmmoProfileFromLoadedBullet(__instance.GunItemSetting, "ReloadComplete");
            }
        }

        [HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
        private static class DragonKingBossGunShootPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(ItemAgent_Gun __instance, Vector3 _muzzlePoint, Vector3 _shootDirection, Vector3 firstFrameCheckStartPoint)
            {
                if (__instance == null || !IsDragonKingBossGun(__instance.Item))
                {
                    return true;
                }

                if (__instance.GunItemSetting == null || __instance.GunItemSetting.LoadingBullets)
                {
                    return false;
                }

                if (__instance.Holder != null && __instance.BulletItem == null)
                {
                    return false;
                }

                if (__instance.Holder != null &&
                    LevelManager.Instance != null &&
                    LevelManager.Instance.ControllingCharacter == __instance.Holder &&
                    Team.IsEnemy(__instance.Holder.Team, Teams.player))
                {
                    __instance.Holder.SetTeam(Teams.all);
                }

                DragonKingBossGunShotProfile profile;
                if (!DragonKingBossGunProfiles.TryResolve(__instance.BulletItem, out profile))
                {
                    profile = DragonKingBossGunProfiles.DefaultProfile;
                }

                // 射击时检测弹种是否变化，确保属性覆盖已应用
                TryApplyAmmoProfile(__instance.Item, profile, "ShootOneBullet");

                Vector3 adjustedDirection = ApplyWeaponScatter(__instance, _shootDirection);
                SpawnPrimaryProjectiles(__instance, profile, _muzzlePoint, adjustedDirection, firstFrameCheckStartPoint);
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemVariableEntry), "Refresh")]
        private static class DragonKingBossGunCaliberDisplayPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ItemVariableEntry __instance)
            {
                try
                {
                    if (__instance == null || itemVariableEntryTargetField == null || itemVariableEntryValueField == null)
                    {
                        return;
                    }

                    CustomData targetData = itemVariableEntryTargetField.GetValue(__instance) as CustomData;
                    if (targetData == null || !string.Equals(targetData.Key, "Caliber", StringComparison.Ordinal))
                    {
                        return;
                    }

                    ItemDetailsDisplay detailsDisplay = __instance.GetComponentInParent<ItemDetailsDisplay>();
                    if (detailsDisplay == null || !IsDragonKingBossGun(detailsDisplay.Target))
                    {
                        return;
                    }

                    TextMeshProUGUI valueText = itemVariableEntryValueField.GetValue(__instance) as TextMeshProUGUI;
                    if (valueText == null)
                    {
                        return;
                    }

                    // 只覆写详情页展示，保留后台真实口径，避免影响多口径配弹逻辑。
                    valueText.text = DragonKingBossGunConfig.GetUnknownCaliberDisplayText();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonKingBossGun] 覆写详情页口径显示失败: " + e.Message);
                }
            }
        }
    }
}

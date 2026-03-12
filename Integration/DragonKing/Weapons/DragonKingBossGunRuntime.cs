using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal enum DragonKingBossGunAmmoMode
    {
        Shotgun = 1,
        Assault = 2,
        Heavy = 3
    }

    public static class DragonKingBossGunRuntime
    {
        private const int ShotMarkerBase = 1000000;
        private const int ShotMarkerModeScale = 10;
        private const int MaxEncodedShotId = 99999;
        private const float ProcessedHitKeepTime = 1.25f;

        private static readonly HashSet<string> SupportedCalibers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SMG",
            "AR_S",
            "BR"
        };

        private static readonly Dictionary<long, float> processedHitPairs = new Dictionary<long, float>();
        private static readonly List<long> processedHitKeysToRemove = new List<long>();
        private static readonly FieldInfo traceTargetField = typeof(ItemAgent_Gun).GetField("traceTarget", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool hurtEventSubscribed;
        private static int shotSequence;
        private static Projectile cachedDragonProjectile;
        private static float cachedProjectileRadius = 0.12f;
        private static Vector3 cachedProjectileScale = Vector3.one;
        private static GameObject cachedExplosionFx;

        public static void InitializeRuntime()
        {
            if (hurtEventSubscribed)
            {
                return;
            }

            Health.OnHurt += OnDragonKingBossGunHurt;
            hurtEventSubscribed = true;
        }

        public static bool IsDragonKingBossGun(Item item)
        {
            return item != null && item.TypeID == DragonKingBossGunConfig.WeaponTypeId;
        }

        public static bool TryAddFireEffectsToAgent(ItemAgent_Gun gunAgent)
        {
            if (gunAgent == null || !IsDragonKingBossGun(gunAgent.Item))
            {
                return false;
            }

            DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(gunAgent.gameObject);
            return true;
        }

        private static void OnDragonKingBossGunHurt(Health health, DamageInfo damageInfo)
        {
            if (health == null)
            {
                return;
            }

            if (damageInfo.fromWeaponItemID != DragonKingBossGunConfig.WeaponTypeId ||
                damageInfo.isFromBuffOrEffect ||
                damageInfo.isExplosion)
            {
                return;
            }

            DragonKingBossGunAmmoMode mode;
            int shotId;
            if (!TryDecodeShotMarker(damageInfo.buffChance, out shotId, out mode))
            {
                return;
            }

            DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(health);
            if (receiver == null)
            {
                return;
            }

            long processedKey = ComposeProcessedHitKey(shotId, receiver.GetInstanceID());
            float lastHitTime;
            if (processedHitPairs.TryGetValue(processedKey, out lastHitTime) && Time.time - lastHitTime < ProcessedHitKeepTime)
            {
                return;
            }

            processedHitPairs[processedKey] = Time.time;
            CleanupProcessedHitPairs();

            DragonFlameMarkTracker.AddMark(
                receiver,
                GetModeMarkGain(mode),
                DragonKingBossGunConfig.MaxLinkedMarkStacks,
                FenHuangHalberdConfig.MarkDuration);
        }

        private static void CleanupProcessedHitPairs()
        {
            if (processedHitPairs.Count == 0)
            {
                return;
            }

            float threshold = Time.time - ProcessedHitKeepTime;
            processedHitKeysToRemove.Clear();

            foreach (var kvp in processedHitPairs)
            {
                if (kvp.Value <= threshold)
                {
                    processedHitKeysToRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < processedHitKeysToRemove.Count; i++)
            {
                processedHitPairs.Remove(processedHitKeysToRemove[i]);
            }
        }

        private static long ComposeProcessedHitKey(int shotId, int receiverId)
        {
            return ((long)shotId << 32) ^ (uint)receiverId;
        }

        private static int GetModeMarkGain(DragonKingBossGunAmmoMode mode)
        {
            switch (mode)
            {
                case DragonKingBossGunAmmoMode.Heavy:
                    return 3;
                case DragonKingBossGunAmmoMode.Shotgun:
                case DragonKingBossGunAmmoMode.Assault:
                default:
                    return 1;
            }
        }

        private static int NextShotId()
        {
            shotSequence++;
            if (shotSequence > MaxEncodedShotId)
            {
                shotSequence = 1;
            }

            return shotSequence;
        }

        private static float EncodeShotMarker(int shotId, DragonKingBossGunAmmoMode mode)
        {
            return ShotMarkerBase + shotId * ShotMarkerModeScale + (int)mode;
        }

        private static bool TryDecodeShotMarker(float marker, out int shotId, out DragonKingBossGunAmmoMode mode)
        {
            shotId = 0;
            mode = DragonKingBossGunAmmoMode.Assault;

            int encoded = Mathf.RoundToInt(marker);
            if (encoded < ShotMarkerBase + 10)
            {
                return false;
            }

            int payload = encoded - ShotMarkerBase;
            int modeValue = payload % ShotMarkerModeScale;
            shotId = payload / ShotMarkerModeScale;

            if (shotId <= 0)
            {
                return false;
            }

            if (!Enum.IsDefined(typeof(DragonKingBossGunAmmoMode), modeValue))
            {
                return false;
            }

            mode = (DragonKingBossGunAmmoMode)modeValue;
            return true;
        }

        private static bool TryGetAmmoMode(Item bulletItem, out DragonKingBossGunAmmoMode mode)
        {
            mode = DragonKingBossGunAmmoMode.Assault;
            if (bulletItem == null || bulletItem.Constants == null)
            {
                return false;
            }

            string caliber = bulletItem.Constants.GetString("Caliber", null);
            if (string.IsNullOrEmpty(caliber))
            {
                return false;
            }

            if (string.Equals(caliber, "SMG", StringComparison.OrdinalIgnoreCase))
            {
                mode = DragonKingBossGunAmmoMode.Shotgun;
                return true;
            }

            if (string.Equals(caliber, "BR", StringComparison.OrdinalIgnoreCase))
            {
                mode = DragonKingBossGunAmmoMode.Heavy;
                return true;
            }

            if (string.Equals(caliber, "AR_S", StringComparison.OrdinalIgnoreCase))
            {
                mode = DragonKingBossGunAmmoMode.Assault;
                return true;
            }

            return false;
        }

        private static bool IsCompatibleBullet(Item weaponItem, Item bulletItem)
        {
            if (!IsDragonKingBossGun(weaponItem) || bulletItem == null)
            {
                return false;
            }

            if (!bulletItem.GetBool("IsBullet", false))
            {
                return false;
            }

            string caliber = bulletItem.Constants != null ? bulletItem.Constants.GetString("Caliber", null) : null;
            return !string.IsNullOrEmpty(caliber) && SupportedCalibers.Contains(caliber);
        }

        private static CharacterMainControl GetTraceTarget(ItemAgent_Gun gun)
        {
            if (traceTargetField == null || gun == null)
            {
                return null;
            }

            return traceTargetField.GetValue(gun) as CharacterMainControl;
        }

        private static Projectile GetDragonProjectile(Projectile fallback)
        {
            if (cachedDragonProjectile != null)
            {
                return cachedDragonProjectile;
            }

            Projectile projectile = EquipmentFactory.GetLoadedBullet("dragon");
            if (projectile == null)
            {
                projectile = EquipmentFactory.GetLoadedBullet("Dragon");
            }
            if (projectile == null)
            {
                Item sourceGun = EquipmentFactory.GetLoadedGun(DragonKingBossGunConfig.SourceWeaponTypeId);
                ItemSetting_Gun sourceSetting = sourceGun != null ? sourceGun.GetComponent<ItemSetting_Gun>() : null;
                if (sourceSetting != null)
                {
                    projectile = sourceSetting.bulletPfb;
                }
            }

            if (projectile == null)
            {
                projectile = fallback != null ? fallback : GameplayDataSettings.Prefabs.DefaultBullet;
            }

            cachedDragonProjectile = projectile;
            if (projectile != null)
            {
                cachedProjectileRadius = projectile.radius;
                cachedProjectileScale = projectile.transform.localScale;
                cachedExplosionFx = projectile.explosionFx;
            }

            return cachedDragonProjectile;
        }

        private static void SpawnDragonProjectile(ItemAgent_Gun gun, Vector3 muzzlePoint, Vector3 direction, Vector3 firstFrameCheckStartPoint, DragonKingBossGunAmmoMode mode, int shotId, int projectileCount, float scaleFactor, float radiusFactor, bool enableExplosion)
        {
            Projectile baseProjectile = GetDragonProjectile(gun.GunItemSetting != null ? gun.GunItemSetting.bulletPfb : null);
            if (baseProjectile == null || LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
            {
                return;
            }

            Projectile projectile = LevelManager.Instance.BulletPool.GetABullet(baseProjectile);
            if (projectile == null)
            {
                return;
            }

            projectile.transform.position = muzzlePoint;
            projectile.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            projectile.transform.localScale = cachedProjectileScale * scaleFactor;
            projectile.radius = cachedProjectileRadius * radiusFactor;
            projectile.explosionFx = enableExplosion ? cachedExplosionFx : null;

            ProjectileContext context = BuildProjectileContext(gun, direction, firstFrameCheckStartPoint, mode, shotId, projectileCount, enableExplosion);
            projectile.Init(context);
        }

        private static ProjectileContext BuildProjectileContext(ItemAgent_Gun gun, Vector3 direction, Vector3 firstFrameCheckStartPoint, DragonKingBossGunAmmoMode mode, int shotId, int projectileCount, bool enableExplosion)
        {
            bool isMainCharacterShot = gun.Holder != null && gun.Holder.IsMainCharacter;
            float characterDamageMultiplier = gun.CharacterDamageMultiplier;

            ProjectileContext context = default(ProjectileContext);
            context.firstFrameCheck = true;
            context.firstFrameCheckStartPoint = firstFrameCheckStartPoint;
            context.direction = direction.normalized;
            context.speed = gun.BulletSpeed * GetModeSpeedFactor(mode);
            context.traceTarget = mode == DragonKingBossGunAmmoMode.Heavy ? GetTraceTarget(gun) : null;
            context.traceAbility = mode == DragonKingBossGunAmmoMode.Heavy ? Mathf.Max(gun.TraceAbility, 0.55f) : 0f;
            context.controlMindType = gun.ControlMindType;
            if (gun.GunItemSetting != null && !gun.GunItemSetting.CanControlMind)
            {
                context.controlMindType = ControlMindTypes.none;
            }

            context.controlMindTime = gun.ControlMindTime;
            if (gun.Holder != null)
            {
                context.team = gun.Holder.Team;
                context.speed *= gun.Holder.GunBulletSpeedMultiplier;
            }
            else
            {
                context.team = Teams.all;
            }

            context.distance = gun.BulletDistance * GetModeDistanceFactor(mode) + 0.4f;
            context.halfDamageDistance = context.distance * 0.5f;
            if (!isMainCharacterShot)
            {
                context.distance *= 1.05f;
            }

            context.penetrate = Mathf.Max(gun.Penetrate, GetModePenetrate(mode));
            context.damage = gun.Damage * gun.BulletDamageMultiplier * GetModeDamageFactor(mode) * characterDamageMultiplier / Mathf.Max(1, projectileCount);
            if (gun.Damage > 1f && context.damage < 1f)
            {
                context.damage = 1f;
            }

            context.dmgOverDistance = gun.DmgOverDistance;
            context.critDamageFactor = (gun.CritDamageFactor + gun.BulletCritDamageFactorGain) * (1f + gun.CharacterGunCritDamageGain);
            context.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain);
            if (isMainCharacterShot && LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
            {
                context.critRate = LevelManager.Instance.InputManager.AimingEnemyHead ? 1f : context.critRate;
            }

            context.armorPiercing = gun.ArmorPiercing + gun.BulletArmorPiercingGain;
            context.armorBreak = gun.ArmorBreak + gun.BulletArmorBreakGain;
            context.fromCharacter = gun.Holder;
            context.realFromCharacter = gun.Holder;
            if (gun.Holder != null && LevelManager.Instance != null && gun.Holder == LevelManager.Instance.ControllingCharacter)
            {
                context.fromCharacter = CharacterMainControl.Main;
            }

            if (enableExplosion)
            {
                context.explosionRange = Mathf.Max(gun.BulletExplosionRange, 1.35f);
                context.explosionDamage = Mathf.Max(
                    gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier * characterDamageMultiplier,
                    gun.Damage * 0.7f * characterDamageMultiplier);
            }

            ApplyElement(ref context, gun.GunItemSetting != null ? gun.GunItemSetting.element : ElementTypes.fire);

            context.fromWeaponItemID = gun.Item != null ? gun.Item.TypeID : 0;
            context.buff = null;
            context.buffChance = EncodeShotMarker(shotId, mode);
            context.bleedChance = gun.BulletBleedChance;

            if (gun.Holder != null && isMainCharacterShot && gun.Holder.HasNearByHalfObsticle())
            {
                context.ignoreHalfObsticle = true;
            }

            if (context.critRate > 0.99f)
            {
                context.ignoreHalfObsticle = true;
            }

            return context;
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

        private static float GetModeDamageFactor(DragonKingBossGunAmmoMode mode)
        {
            switch (mode)
            {
                case DragonKingBossGunAmmoMode.Shotgun:
                    return 1.12f;
                case DragonKingBossGunAmmoMode.Heavy:
                    return 2.05f;
                case DragonKingBossGunAmmoMode.Assault:
                default:
                    return 1.15f;
            }
        }

        private static float GetModeSpeedFactor(DragonKingBossGunAmmoMode mode)
        {
            switch (mode)
            {
                case DragonKingBossGunAmmoMode.Shotgun:
                    return 0.8f;
                case DragonKingBossGunAmmoMode.Heavy:
                    return 0.72f;
                case DragonKingBossGunAmmoMode.Assault:
                default:
                    return 1.08f;
            }
        }

        private static float GetModeDistanceFactor(DragonKingBossGunAmmoMode mode)
        {
            switch (mode)
            {
                case DragonKingBossGunAmmoMode.Shotgun:
                    return 0.52f;
                case DragonKingBossGunAmmoMode.Heavy:
                    return 1.15f;
                case DragonKingBossGunAmmoMode.Assault:
                default:
                    return 0.9f;
            }
        }

        private static int GetModePenetrate(DragonKingBossGunAmmoMode mode)
        {
            switch (mode)
            {
                case DragonKingBossGunAmmoMode.Heavy:
                    return 2;
                case DragonKingBossGunAmmoMode.Assault:
                    return 1;
                default:
                    return 0;
            }
        }

        private static void SpawnModeProjectiles(ItemAgent_Gun gun, Vector3 muzzlePoint, Vector3 shootDirection, Vector3 firstFrameCheckStartPoint, DragonKingBossGunAmmoMode mode)
        {
            int shotId = NextShotId();

            switch (mode)
            {
                case DragonKingBossGunAmmoMode.Shotgun:
                    SpawnShotgunPattern(gun, muzzlePoint, shootDirection, firstFrameCheckStartPoint, shotId);
                    break;
                case DragonKingBossGunAmmoMode.Heavy:
                    SpawnDragonProjectile(gun, muzzlePoint, shootDirection, firstFrameCheckStartPoint, mode, shotId, 1, 1.8f, 1.9f, true);
                    break;
                case DragonKingBossGunAmmoMode.Assault:
                default:
                    SpawnAssaultPattern(gun, muzzlePoint, shootDirection, firstFrameCheckStartPoint, shotId);
                    break;
            }
        }

        private static void SpawnShotgunPattern(ItemAgent_Gun gun, Vector3 muzzlePoint, Vector3 shootDirection, Vector3 firstFrameCheckStartPoint, int shotId)
        {
            const int pelletCount = 7;
            const float spread = 22f;

            for (int i = 0; i < pelletCount; i++)
            {
                float t = pelletCount <= 1 ? 0f : (float)i / (pelletCount - 1);
                float angle = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * shootDirection;
                SpawnDragonProjectile(gun, muzzlePoint, direction, firstFrameCheckStartPoint, DragonKingBossGunAmmoMode.Shotgun, shotId, pelletCount, 0.85f, 0.85f, false);
            }
        }

        private static void SpawnAssaultPattern(ItemAgent_Gun gun, Vector3 muzzlePoint, Vector3 shootDirection, Vector3 firstFrameCheckStartPoint, int shotId)
        {
            float[] angles = new float[] { -4.5f, 0f, 4.5f };
            for (int i = 0; i < angles.Length; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, angles[i], 0f) * shootDirection;
                SpawnDragonProjectile(gun, muzzlePoint, direction, firstFrameCheckStartPoint, DragonKingBossGunAmmoMode.Assault, shotId, angles.Length, 0.92f, 0.92f, false);
            }
        }

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

                if (newBulletItem == null || !newBulletItem.Tags.Contains(GameplayDataSettings.Tags.Bullet))
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
                if (currentLoadedBullet != null)
                {
                    __instance.SetTargetBulletType(currentLoadedBullet);
                    __result = false;
                    return false;
                }

                if (inventory == null)
                {
                    __instance.SetTargetBulletType(-1);
                    __result = false;
                    return false;
                }

                __instance.SetTargetBulletType(-1);
                foreach (Item item in inventory)
                {
                    if (IsCompatibleBullet(__instance.Item, item))
                    {
                        __instance.SetTargetBulletType(item);
                        break;
                    }
                }

                __result = __instance.TargetBulletID != -1;
                return false;
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

                Dictionary<int, BulletTypeInfo> result = new Dictionary<int, BulletTypeInfo>();
                if (inventory != null)
                {
                    foreach (Item item in inventory)
                    {
                        if (!IsCompatibleBullet(__instance.Item, item))
                        {
                            continue;
                        }

                        BulletTypeInfo info;
                        if (!result.TryGetValue(item.TypeID, out info))
                        {
                            info = new BulletTypeInfo();
                            info.bulletTypeID = item.TypeID;
                            info.count = item.StackCount;
                            result.Add(item.TypeID, info);
                        }
                        else
                        {
                            info.count += item.StackCount;
                        }
                    }
                }

                __result = result;
                return false;
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

                DragonKingBossGunAmmoMode mode;
                if (!TryGetAmmoMode(__instance.BulletItem, out mode))
                {
                    mode = DragonKingBossGunAmmoMode.Assault;
                }

                Vector3 adjustedDirection = ApplyWeaponScatter(__instance, _shootDirection);
                SpawnModeProjectiles(__instance, _muzzlePoint, adjustedDirection, firstFrameCheckStartPoint, mode);
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal enum DragonKingBossGunProfileId
    {
        Rocket = 1,
        Smg = 2,
        Assault = 3,
        Heavy = 4,
        Sniper = 5,
        Shotgun = 6,
        Magnum = 7,
        Arrow = 8,
        Energy = 9,
        Poop = 10,
        Candy = 11,
        IceBlade = 12,
        Snow = 13,
        Nano = 14,
        Firework = 15
    }

    internal enum DragonKingBossGunArcMode
    {
        None = 0,
        Low = 1,
        High = 2
    }

    internal enum DragonKingBossGunSplitPattern
    {
        None = 0,
        ForwardFan = 1,
        DownBurst = 2,
        Radial = 3
    }

    internal sealed class DragonKingBossGunShotProfile
    {
        public DragonKingBossGunProfileId Id;
        public int[] TypeIds;
        public string[] Calibers;
        public int ShotCount = 1;
        public int Burst = 1;
        public float Scale = 1f;
        public float SpeedFactor = 1f;
        public float DistanceFactor = 1f;
        public float DamageFactor = 1f;
        public float SpreadAngle;
        public bool RandomSpread;
        public DragonKingBossGunArcMode Arc = DragonKingBossGunArcMode.None;
        public float ArcLift;
        public float Gravity;
        public bool UseAimedArc;
        public float TraceAbility;
        public int Pierce;
        public int Bounce;
        public bool UseHelix;
        public float HelixAmplitude;
        public float HelixFrequency = 6f;
        public float HelixVerticalLift;
        public bool UseReturn;
        public bool UseSticky;
        public float StickyDelay = 0.3f;
        public float StickyExplosionRange = 1.35f;
        public float StickyExplosionDamageFactor = 1f;
        public bool UseGroundZone;
        public int MaxGroundZonesPerShot;
        public float GroundZoneRadius = 1f;
        public float GroundZoneDuration = 1.2f;
        public float GroundZoneTickDamageFactor = 0.18f;
        public ElementTypes GroundZoneElement = ElementTypes.fire;
        public bool GroundZoneRequireGroundImpact = true;
        public bool GroundZoneAllowSecondary;
        public bool UseSplit;
        public bool SplitOnAirburst;
        public float AirburstDistanceFactor = 0.55f;
        public int SplitCount;
        public float SplitScale = 0.55f;
        public float SplitSpeedFactor = 0.92f;
        public float SplitDistanceFactor = 0.55f;
        public float SplitDamageFactor = 0.45f;
        public float SplitSpreadAngle = 24f;
        public float SplitTraceAbility;
        public DragonKingBossGunSplitPattern SplitPattern = DragonKingBossGunSplitPattern.None;
        public float[] PierceDamageDecay;
        public float SplitActivationDelay;
        public float SplitInitialSpeedMult = 0.3f;
        public bool SplitIgnoreSourceOnSplit;
        public float SplitGravity;
        public float ExplosionFxDuration = 3f;
        public float ExplosionRange;
        public float ExplosionDamageFactor = 1f;
        public float SplitExplosionRange;
        public float SplitExplosionDamageFactor = 0.3f;
        public bool PlayObstacleHitFx = true;
        public bool PlaySplitTriggerFx = true;
        public bool RequiresCustomMovement;
        public ElementTypes Element = ElementTypes.fire;
        public bool UseNativeProjectile;
        public int MarkPerHit = 1;
        public int MaxMarksPerTargetPerShot = 1;
        public int SecondaryMarkPerHit = -1;
        public int MaxSecondaryMarksPerTargetPerShot = -1;
        public int FollowupMarkPerHit = -1;
        public int MaxFollowupMarksPerTargetPerShot = -1;
        public int ReturnMarkPerHit = -1;
        public int MaxReturnMarksPerTargetPerShot = -1;
        // 枪械属性覆盖（换弹/场景加载时应用到龙枪 StatCollection）
        public float FireRateMult = 1f;       // ShootSpeed 乘数（<1 变慢，>1 变快）
        public float GunDamageMult = 1f;      // 基础伤害乘数
        public int OverrideCapacity = -1;     // 弹匣容量覆盖（-1 = 不覆盖）
        public float OverrideReloadTime = -1f;// 换弹时间覆盖（-1 = 不覆盖）
        public float OverrideBulletDistance = -1f; // 射程覆盖（-1 = 不覆盖）

        public string TrailFxPrefab = string.Empty;
        public string HitFxPrefab = string.Empty;
        public string ExplosionFxPrefab = string.Empty;

        public bool Matches(Item bulletItem)
        {
            if (bulletItem == null)
            {
                return false;
            }

            if (TypeIds != null)
            {
                for (int i = 0; i < TypeIds.Length; i++)
                {
                    if (bulletItem.TypeID == TypeIds[i])
                    {
                        return true;
                    }
                }
            }

            string caliber = bulletItem.Constants != null ? bulletItem.Constants.GetString("Caliber", null) : null;
            if (string.IsNullOrEmpty(caliber) || Calibers == null)
            {
                return false;
            }

            for (int i = 0; i < Calibers.Length; i++)
            {
                if (string.Equals(caliber, Calibers[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static class DragonKingBossGunProfiles
    {
        private static readonly DragonKingBossGunShotProfile[] orderedProfiles =
        {
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.IceBlade,
                TypeIds = new[] { 1303 },
                Calibers = null,
                Scale = 1.1f,
                PlayObstacleHitFx = false,
                SpeedFactor = 1.1f,
                DamageFactor = 1.22f,
                Pierce = 3,
                Element = ElementTypes.ice,
                ExplosionRange = 1.25f,
                ExplosionDamageFactor = 0.55f,
                MarkPerHit = 2,
                MaxMarksPerTargetPerShot = 2,
                FireRateMult = 0.5f,
                GunDamageMult = 2f,
                OverrideCapacity = 8,
                OverrideReloadTime = 3.5f,
                OverrideBulletDistance = 26f,
                TrailFxPrefab = "Fx_DragonGun_IceBlade_Trail",
                HitFxPrefab = "Fx_DragonGun_IceBlade_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_IceBlade_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Rocket,
                TypeIds = new[] { 326 },
                Calibers = new[] { "Rocket" },
                Scale = 1f,
                SpeedFactor = 0.65f,
                DistanceFactor = 0.7f,
                DamageFactor = 1.75f,
                Arc = DragonKingBossGunArcMode.Low,
                ArcLift = 0.28f,
                Gravity = 5f,
                TraceAbility = 0.2f,
                UseNativeProjectile = true,
                UseSplit = true,
                SplitOnAirburst = true,
                AirburstDistanceFactor = 0.52f,
                SplitCount = 1,
                SplitScale = 0.72f,
                SplitSpeedFactor = 0.78f,
                SplitDistanceFactor = 0.92f,
                SplitDamageFactor = 0.55f,
                SplitSpreadAngle = 0f,
                SplitPattern = DragonKingBossGunSplitPattern.DownBurst,
                SplitIgnoreSourceOnSplit = true,
                SplitGravity = 7.5f,
                ExplosionRange = 1.5f,
                ExplosionDamageFactor = 0.6f,
                SplitExplosionRange = 0.8f,
                SplitExplosionDamageFactor = 0.35f,
                ExplosionFxDuration = 0.35f,
                MarkPerHit = 3,
                MaxMarksPerTargetPerShot = 3,
                SecondaryMarkPerHit = 1,
                MaxSecondaryMarksPerTargetPerShot = 1,
                FireRateMult = 0.25f,
                GunDamageMult = 2.2f,
                OverrideCapacity = 5,
                OverrideReloadTime = 4.5f,
                OverrideBulletDistance = 18f,
                TrailFxPrefab = "",
                HitFxPrefab = "",
                ExplosionFxPrefab = ""
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Smg,
                TypeIds = new[] { 594 },
                Calibers = new[] { "SMG" },
                ShotCount = 2,
                Scale = 0.45f,
                SpeedFactor = 1.35f,
                DistanceFactor = 0.82f,
                DamageFactor = 0.9f,
                SpreadAngle = 6f,
                UseHelix = true,
                HelixAmplitude = 0.18f,
                HelixFrequency = 10f,
                HelixVerticalLift = 0.04f,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 2,
                FireRateMult = 1.6f,
                GunDamageMult = 0.45f,
                OverrideCapacity = 30,
                OverrideReloadTime = 2.5f,
                OverrideBulletDistance = 18f,
                TrailFxPrefab = "Fx_DragonGun_SMG_Trail",
                HitFxPrefab = "Fx_DragonGun_SMG_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_SMG_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Assault,
                TypeIds = new[] { 603 },
                Calibers = new[] { "AR" },
                ShotCount = 3,
                Scale = 0.55f,
                SpeedFactor = 1.15f,
                DistanceFactor = 1.28f,
                DamageFactor = 0.88f,
                SpreadAngle = 8f,
                Bounce = 1,
                PlayObstacleHitFx = false,
                RequiresCustomMovement = true,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 1,
                FireRateMult = 1f,
                GunDamageMult = 0.75f,
                OverrideCapacity = 15,
                OverrideReloadTime = 3.35f,
                OverrideBulletDistance = 24f,
                TrailFxPrefab = "Fx_DragonGun_Assault_Trail",
                HitFxPrefab = "Fx_DragonGun_Assault_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Assault_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Heavy,
                TypeIds = new[] { 612 },
                Calibers = new[] { "BR" },
                Scale = 1.2f,
                SpeedFactor = 1f,
                DistanceFactor = 1.08f,
                DamageFactor = 1.52f,
                Pierce = 2,
                PierceDamageDecay = new[] { 0.5f, 0.25f },
                PlayObstacleHitFx = false,
                RequiresCustomMovement = true,
                MarkPerHit = 3,
                MaxMarksPerTargetPerShot = 3,
                FollowupMarkPerHit = 1,
                MaxFollowupMarksPerTargetPerShot = 1,
                FireRateMult = 0.4f,
                GunDamageMult = 1.4f,
                OverrideCapacity = 8,
                OverrideReloadTime = 3.8f,
                OverrideBulletDistance = 28f,
                TrailFxPrefab = "Fx_DragonGun_Heavy_Trail",
                HitFxPrefab = "Fx_DragonGun_Heavy_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Heavy_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Sniper,
                TypeIds = new[] { 621 },
                Calibers = new[] { "SNP" },
                Scale = 0.7f,
                SpeedFactor = 1.8f,
                DistanceFactor = 1.45f,
                DamageFactor = 1.4f,
                TraceAbility = 0.2f,
                Pierce = 4,
                PierceDamageDecay = new[] { 0.8f, 0.65f, 0.5f, 0.25f },
                MarkPerHit = 4,
                MaxMarksPerTargetPerShot = 4,
                FireRateMult = 0.12f,
                GunDamageMult = 4f,
                OverrideCapacity = 5,
                OverrideReloadTime = 4f,
                OverrideBulletDistance = 36f,
                TrailFxPrefab = "Fx_DragonGun_Sniper_Trail",
                HitFxPrefab = "Fx_DragonGun_Sniper_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Sniper_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Shotgun,
                TypeIds = new[] { 630 },
                Calibers = new[] { "SHT" },
                ShotCount = 10,
                Scale = 0.35f,
                SpeedFactor = 0.95f,
                DistanceFactor = 0.5f,
                DamageFactor = 0.56f,
                SpreadAngle = 26f,
                UseGroundZone = true,
                MaxGroundZonesPerShot = 1,
                GroundZoneRadius = 1.05f,
                GroundZoneDuration = 1.25f,
                GroundZoneTickDamageFactor = 0.12f,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 2,
                FireRateMult = 0.35f,
                GunDamageMult = 1.5f,
                OverrideCapacity = 8,
                OverrideReloadTime = 3.5f,
                OverrideBulletDistance = 12f,
                TrailFxPrefab = "Fx_DragonGun_Shotgun_Trail",
                HitFxPrefab = "Fx_DragonGun_Shotgun_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Shotgun_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Magnum,
                TypeIds = new[] { 640 },
                Calibers = new[] { "MAG" },
                Scale = 1f,
                SpeedFactor = 0.95f,
                DistanceFactor = 1.12f,
                DamageFactor = 1.28f,
                Pierce = 3,
                UseReturn = true,
                PlayObstacleHitFx = false,
                RequiresCustomMovement = true,
                MarkPerHit = 2,
                MaxMarksPerTargetPerShot = 2,
                ReturnMarkPerHit = 1,
                MaxReturnMarksPerTargetPerShot = 1,
                FireRateMult = 0.33f,
                GunDamageMult = 1.9f,
                OverrideCapacity = 6,
                OverrideReloadTime = 3.5f,
                OverrideBulletDistance = 26f,
                TrailFxPrefab = "Fx_DragonGun_Magnum_Trail",
                HitFxPrefab = "Fx_DragonGun_Magnum_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Magnum_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Arrow,
                TypeIds = new[] { 648 },
                Calibers = new[] { "ARR" },
                Scale = 0.6f,
                SpeedFactor = 0.9f,
                DistanceFactor = 0.88f,
                DamageFactor = 0.96f,
                Arc = DragonKingBossGunArcMode.Low,
                ArcLift = 0.32f,
                Gravity = 6.5f,
                UseSticky = true,
                StickyDelay = 1f,
                PlayObstacleHitFx = false,
                RequiresCustomMovement = true,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 2,
                FireRateMult = 0.5f,
                GunDamageMult = 1.8f,
                OverrideCapacity = 10,
                OverrideReloadTime = 3.2f,
                OverrideBulletDistance = 20f,
                TrailFxPrefab = "Fx_DragonGun_Arrow_Trail",
                HitFxPrefab = "Fx_DragonGun_Arrow_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Arrow_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Energy,
                TypeIds = new[] { 650 },
                Calibers = new[] { "PWS" },
                Scale = 0.75f,
                SpeedFactor = 1.05f,
                DistanceFactor = 0.92f,
                DamageFactor = 1f,
                TraceAbility = 0.55f,
                Element = ElementTypes.electricity,
                PlayObstacleHitFx = false,
                PlaySplitTriggerFx = false,
                UseSplit = true,
                SplitCount = 3,
                SplitScale = 0.45f,
                SplitSpeedFactor = 0.72f,
                SplitDistanceFactor = 1.35f,
                SplitDamageFactor = 0.34f,
                SplitSpreadAngle = 24f,
                SplitTraceAbility = 0.65f,
                SplitPattern = DragonKingBossGunSplitPattern.Radial,
                SplitActivationDelay = 0.5f,
                SplitInitialSpeedMult = 0.35f,
                SplitIgnoreSourceOnSplit = true,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 1,
                SecondaryMarkPerHit = 1,
                MaxSecondaryMarksPerTargetPerShot = 1,
                FireRateMult = 0.65f,
                GunDamageMult = 1.2f,
                OverrideCapacity = 12,
                OverrideReloadTime = 3f,
                OverrideBulletDistance = 22f,
                TrailFxPrefab = "Fx_DragonGun_Energy_Trail",
                HitFxPrefab = "Fx_DragonGun_Energy_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Energy_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Poop,
                TypeIds = new[] { 944 },
                Calibers = new[] { "Poop" },
                Scale = 0.9f,
                SpeedFactor = 0.55f,
                DistanceFactor = 2.5f,
                DamageFactor = 0.7f,
                Arc = DragonKingBossGunArcMode.High,
                ArcLift = 0.95f,
                Gravity = 9.5f,
                UseAimedArc = true,
                Element = ElementTypes.poison,
                UseGroundZone = true,
                MaxGroundZonesPerShot = 1,
                GroundZoneRadius = 1.6f,
                GroundZoneDuration = 4f,
                GroundZoneTickDamageFactor = 0.12f,
                GroundZoneElement = ElementTypes.poison,
                PlayObstacleHitFx = false,
                RequiresCustomMovement = true,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 1,
                FireRateMult = 0.28f,
                GunDamageMult = 3.2f,
                OverrideCapacity = 6,
                OverrideReloadTime = 3.5f,
                OverrideBulletDistance = 60f,
                TrailFxPrefab = "Fx_DragonGun_Poop_Trail",
                HitFxPrefab = "Fx_DragonGun_Poop_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Poop_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Candy,
                TypeIds = new[] { 1262 },
                Calibers = new[] { "Candy" },
                ShotCount = 6,
                Scale = 0.4f,
                SpeedFactor = 1f,
                DistanceFactor = 1.1f,
                DamageFactor = 0.52f,
                SpreadAngle = 180f,
                RandomSpread = true,
                Bounce = 1,
                PlayObstacleHitFx = false,
                RequiresCustomMovement = true,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 2,
                SecondaryMarkPerHit = 1,
                MaxSecondaryMarksPerTargetPerShot = 1,
                FireRateMult = 1.2f,
                GunDamageMult = 0.5f,
                OverrideCapacity = 20,
                OverrideReloadTime = 2.8f,
                OverrideBulletDistance = 18f,
                TrailFxPrefab = "Fx_DragonGun_Candy_Trail",
                HitFxPrefab = "Fx_DragonGun_Candy_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Candy_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Snow,
                TypeIds = new[] { 1351 },
                Calibers = new[] { "Snow" },
                Scale = 1.6f,
                SpeedFactor = 0.6f,
                DistanceFactor = 0.75f,
                DamageFactor = 0.8f,
                Arc = DragonKingBossGunArcMode.High,
                ArcLift = 0.78f,
                Gravity = 10f,
                Element = ElementTypes.ice,
                PlayObstacleHitFx = false,
                PlaySplitTriggerFx = false,
                UseSplit = true,
                SplitCount = 4,
                SplitScale = 0.42f,
                SplitSpeedFactor = 0.74f,
                SplitDistanceFactor = 1.1f,
                SplitDamageFactor = 0.3f,
                SplitSpreadAngle = 42f,
                SplitPattern = DragonKingBossGunSplitPattern.Radial,
                SplitActivationDelay = 0.35f,
                SplitInitialSpeedMult = 0.45f,
                SplitIgnoreSourceOnSplit = true,
                UseGroundZone = true,
                MaxGroundZonesPerShot = 2,
                GroundZoneRadius = 1.85f,
                GroundZoneDuration = 2.4f,
                GroundZoneTickDamageFactor = 0.12f,
                GroundZoneElement = ElementTypes.ice,
                GroundZoneAllowSecondary = true,
                MarkPerHit = 1,
                MaxMarksPerTargetPerShot = 1,
                SecondaryMarkPerHit = 0,
                MaxSecondaryMarksPerTargetPerShot = 0,
                FireRateMult = 0.33f,
                GunDamageMult = 2.2f,
                OverrideCapacity = 6,
                OverrideReloadTime = 3.8f,
                OverrideBulletDistance = 18f,
                TrailFxPrefab = "Fx_DragonGun_Snow_Trail",
                HitFxPrefab = "Fx_DragonGun_Snow_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Snow_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Nano,
                TypeIds = new[] { 1434 },
                Calibers = new[] { "NM" },
                Scale = 0.65f,
                SpeedFactor = 1.1f,
                DistanceFactor = 0.96f,
                DamageFactor = 0.95f,
                TraceAbility = 0.7f,
                PlayObstacleHitFx = false,
                PlaySplitTriggerFx = false,
                UseSplit = true,
                SplitCount = 5,
                SplitScale = 0.28f,
                SplitSpeedFactor = 0.7f,
                SplitDistanceFactor = 1.45f,
                SplitDamageFactor = 0.28f,
                SplitSpreadAngle = 28f,
                SplitTraceAbility = 0.95f,
                SplitPattern = DragonKingBossGunSplitPattern.Radial,
                SplitActivationDelay = 0.5f,
                SplitInitialSpeedMult = 0.32f,
                SplitIgnoreSourceOnSplit = true,
                MarkPerHit = 2,
                MaxMarksPerTargetPerShot = 2,
                SecondaryMarkPerHit = 1,
                MaxSecondaryMarksPerTargetPerShot = 1,
                FireRateMult = 0.55f,
                GunDamageMult = 1.3f,
                OverrideCapacity = 10,
                OverrideReloadTime = 3.2f,
                OverrideBulletDistance = 24f,
                TrailFxPrefab = "Fx_DragonGun_Nano_Trail",
                HitFxPrefab = "Fx_DragonGun_Nano_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Nano_Explosion"
            },
            new DragonKingBossGunShotProfile
            {
                Id = DragonKingBossGunProfileId.Firework,
                TypeIds = new[] { 1523 },
                Calibers = new[] { "FireWork" },
                Scale = 0.85f,
                SpeedFactor = 0.8f,
                DistanceFactor = 0.78f,
                DamageFactor = 0.88f,
                Arc = DragonKingBossGunArcMode.Low,
                ArcLift = 0.3f,
                Gravity = 3f,
                UseHelix = true,
                HelixAmplitude = 0.22f,
                HelixFrequency = 9f,
                HelixVerticalLift = 0.26f,
                PlayObstacleHitFx = false,
                PlaySplitTriggerFx = false,
                UseSplit = true,
                SplitOnAirburst = true,
                AirburstDistanceFactor = 0.62f,
                SplitCount = 8,
                SplitScale = 0.34f,
                SplitSpeedFactor = 0.75f,
                SplitDistanceFactor = 0.7f,
                SplitDamageFactor = 0.26f,
                SplitSpreadAngle = 360f,
                SplitPattern = DragonKingBossGunSplitPattern.Radial,
                SplitActivationDelay = 0.35f,
                SplitInitialSpeedMult = 0.45f,
                SplitIgnoreSourceOnSplit = true,
                MarkPerHit = 2,
                MaxMarksPerTargetPerShot = 2,
                SecondaryMarkPerHit = 1,
                MaxSecondaryMarksPerTargetPerShot = 1,
                FireRateMult = 0.28f,
                GunDamageMult = 2.2f,
                OverrideCapacity = 6,
                OverrideReloadTime = 3.5f,
                OverrideBulletDistance = 20f,
                TrailFxPrefab = "Fx_DragonGun_Firework_Trail",
                HitFxPrefab = "Fx_DragonGun_Firework_Hit",
                ExplosionFxPrefab = "Fx_DragonGun_Firework_Explosion"
            }
        };

        private static readonly Dictionary<int, DragonKingBossGunShotProfile> typeProfiles = new Dictionary<int, DragonKingBossGunShotProfile>();
        private static readonly Dictionary<string, DragonKingBossGunShotProfile> caliberProfiles = new Dictionary<string, DragonKingBossGunShotProfile>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<DragonKingBossGunProfileId, DragonKingBossGunShotProfile> idProfiles = new Dictionary<DragonKingBossGunProfileId, DragonKingBossGunShotProfile>();
        private static readonly HashSet<string> supportedCalibers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> supportedTypeIds = new HashSet<int>();

        static DragonKingBossGunProfiles()
        {
            for (int i = 0; i < orderedProfiles.Length; i++)
            {
                DragonKingBossGunShotProfile profile = orderedProfiles[i];
                idProfiles[profile.Id] = profile;

                if (profile.TypeIds != null)
                {
                    for (int j = 0; j < profile.TypeIds.Length; j++)
                    {
                        typeProfiles[profile.TypeIds[j]] = profile;
                        supportedTypeIds.Add(profile.TypeIds[j]);
                    }
                }

                if (profile.Calibers != null)
                {
                    for (int j = 0; j < profile.Calibers.Length; j++)
                    {
                        string caliber = profile.Calibers[j];
                        if (string.IsNullOrEmpty(caliber))
                        {
                            continue;
                        }

                        if (!caliberProfiles.ContainsKey(caliber))
                        {
                            caliberProfiles.Add(caliber, profile);
                        }

                        supportedCalibers.Add(caliber);
                    }
                }
            }
        }

        public static IReadOnlyCollection<string> SupportedCalibers
        {
            get { return supportedCalibers; }
        }

        public static IReadOnlyCollection<int> SupportedTypeIds
        {
            get { return supportedTypeIds; }
        }

        public static DragonKingBossGunShotProfile DefaultProfile
        {
            get { return caliberProfiles["AR"]; }
        }

        public static bool IsBulletLike(Item item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.GetBool("IsBullet", false))
            {
                return true;
            }

            return item.Tags != null && item.Tags.Contains(GameplayDataSettings.Tags.Bullet);
        }

        public static DragonKingBossGunShotProfile GetProfile(DragonKingBossGunProfileId id)
        {
            DragonKingBossGunShotProfile profile;
            if (idProfiles.TryGetValue(id, out profile))
            {
                return profile;
            }

            return DefaultProfile;
        }

        public static bool TryResolve(Item bulletItem, out DragonKingBossGunShotProfile profile)
        {
            profile = null;
            if (!IsBulletLike(bulletItem))
            {
                return false;
            }

            if (typeProfiles.TryGetValue(bulletItem.TypeID, out profile))
            {
                return true;
            }

            string caliber = bulletItem.Constants != null ? bulletItem.Constants.GetString("Caliber", null) : null;
            if (!string.IsNullOrEmpty(caliber) && caliberProfiles.TryGetValue(caliber, out profile))
            {
                return true;
            }

            return false;
        }
    }
}

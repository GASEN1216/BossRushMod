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
        private const int ShotMarkerBase = 1000000;
        private const int ShotMarkerScale = 100;
        private const int ShotMarkerStageScale = 20;
        private const int MaxEncodedShotId = 99999;
        private const float ProcessedHitKeepTime = 1.25f;
        private const float ProcessedHitCleanupInterval = 2f;
        private const int PhysicsBufferSize = 64;
        private const float GroundImpactDotThreshold = 0.45f;
        private const string BossRedPresetNameKey = "Cname_Boss_Red";

        internal static readonly Collider[] SharedColliderBuffer = new Collider[PhysicsBufferSize];
        internal static readonly HashSet<int> SharedReceiverIdSet = new HashSet<int>();

        internal enum DragonKingBossGunHitStage
        {
            Primary = 0,
            Secondary = 1,
            Return = 2,
            Followup = 3
        }

        private struct ProcessedHitState
        {
            public float time;
            public int marksApplied;
        }

        private struct ProcessedGroundZoneState
        {
            public float time;
            public int spawnCount;
        }

        private static readonly Dictionary<long, ProcessedHitState> processedHitPairs = new Dictionary<long, ProcessedHitState>();
        private static readonly Dictionary<long, ProcessedGroundZoneState> processedGroundZoneShots = new Dictionary<long, ProcessedGroundZoneState>();
        private static readonly List<long> processedHitKeysToRemove = new List<long>();
        private static readonly FieldInfo traceTargetField = typeof(ItemAgent_Gun).GetField("traceTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo itemVariableEntryTargetField = typeof(ItemVariableEntry).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo itemVariableEntryValueField = typeof(ItemVariableEntry).GetField("value", BindingFlags.Instance | BindingFlags.NonPublic);

        private const int MaxProfileId = 15;
        private const int MaxHitStage = 3;

        private static bool hurtEventSubscribed;
        private static int shotSequence;
        private static float lastCleanupTime;
        private static Projectile cachedDragonProjectile;
        private static float cachedProjectileRadius = 0.12f;
        private static Vector3 cachedProjectileScale = Vector3.one;
        private static GameObject cachedExplosionFx;
        private static Projectile cachedNativeRocketProjectile;

        // 弹种属性覆盖：记录当前已应用的 profile，避免重复覆写
        private static DragonKingBossGunProfileId lastAppliedProfileId;
        private static bool hasAppliedProfile;

        private static readonly Vector3[] fanDirectionBuffer = new Vector3[16];
        private static readonly Vector3[] splitDirectionBuffer = new Vector3[16];
        private static readonly Dictionary<int, BulletTypeInfo> reusableBulletTypeDict = new Dictionary<int, BulletTypeInfo>();
        private static readonly Dictionary<int, BulletTypeInfo> emptyBulletTypeDict = new Dictionary<int, BulletTypeInfo>();
        private static readonly MethodInfo presetGenerateItemsMethod = typeof(CharacterRandomPreset).GetMethod("GenerateItems", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool bossRedProjectileWarmupStarted;

        public static void InitializeRuntime()
        {
            if (hurtEventSubscribed)
            {
                return;
            }

            Health.OnHurt += OnDragonKingBossGunHurt;
            hurtEventSubscribed = true;
        }

        public static void WarmupProjectileCache()
        {
            if (cachedDragonProjectile != null || bossRedProjectileWarmupStarted)
            {
                return;
            }

            bossRedProjectileWarmupStarted = true;
            PreloadBossRedProjectileAsync().Forget();
        }

        public static void CleanupRuntime()
        {
            if (!hurtEventSubscribed)
            {
                return;
            }

            Health.OnHurt -= OnDragonKingBossGunHurt;
            hurtEventSubscribed = false;
            ClearSceneCaches();
        }

        /// <summary>
        /// 场景切换时清理临时缓存，但保留 Health.OnHurt 订阅（龙枪可能仍在玩家手中）。
        /// </summary>
        public static void ClearSceneCaches()
        {
            processedHitPairs.Clear();
            processedGroundZoneShots.Clear();
            processedHitKeysToRemove.Clear();
            cachedDragonProjectile = null;
            cachedExplosionFx = null;
            cachedNativeRocketProjectile = null;
            shotSequence = 0;
            lastCleanupTime = 0f;
            bossRedProjectileWarmupStarted = false;
            lastAppliedProfileId = default(DragonKingBossGunProfileId);
            hasAppliedProfile = false;
            DragonKingBossGunProjectileAgent.ClearStaticCaches();
        }

        private static async UniTaskVoid PreloadBossRedProjectileAsync()
        {
            try
            {
                for (int i = 0; i < 60; i++)
                {
                    if (LevelManager.Instance != null)
                    {
                        break;
                    }

                    await UniTask.DelayFrame(1);
                }

                CharacterRandomPreset preset = FindBossRedBasePreset();
                if (preset == null)
                {
                    bossRedProjectileWarmupStarted = false;
                    ModBehaviour.DevLog("[DragonKingBossGun] 未找到 Cname_Boss_Red 预设，无法绑定 Boss_Red 弹幕");
                    return;
                }

                Projectile projectile = await ExtractBossRedProjectileAsync(preset);
                if (projectile == null)
                {
                    bossRedProjectileWarmupStarted = false;
                    ModBehaviour.DevLog("[DragonKingBossGun] 解析 Boss_Red 弹幕失败：未找到原始枪械子弹");
                    return;
                }

                CacheBaseProjectile(projectile, "[DragonKingBossGun] 预缓存 Boss_Red 弹幕基底: ");
                ApplyBossRedProjectileToLoadedGun(projectile);
            }
            catch (Exception e)
            {
                bossRedProjectileWarmupStarted = false;
                ModBehaviour.DevLog("[DragonKingBossGun] 预缓存 Boss_Red 弹幕异常: " + e.Message);
            }
        }

        private static CharacterRandomPreset FindBossRedBasePreset()
        {
            CharacterRandomPreset[] presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
            for (int i = 0; i < presets.Length; i++)
            {
                CharacterRandomPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                if (preset.nameKey == BossRedPresetNameKey)
                {
                    return preset;
                }
            }

            for (int i = 0; i < presets.Length; i++)
            {
                CharacterRandomPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                if ((!string.IsNullOrEmpty(preset.name) && preset.name.IndexOf("Boss_Red", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(preset.nameKey) && preset.nameKey.IndexOf("Boss_Red", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return preset;
                }
            }

            return null;
        }

        private static async UniTask<Projectile> ExtractBossRedProjectileAsync(CharacterRandomPreset preset)
        {
            if (preset == null || presetGenerateItemsMethod == null)
            {
                return null;
            }

            Exception lastError = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<Item> generatedItems = null;
                try
                {
                    generatedItems = await (UniTask<List<Item>>)presetGenerateItemsMethod.Invoke(preset, null);
                    if (generatedItems == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < generatedItems.Count; i++)
                    {
                        Item item = generatedItems[i];
                        if (item == null)
                        {
                            continue;
                        }

                        ItemSetting_Gun gunSetting = item.GetComponent<ItemSetting_Gun>();
                        if (gunSetting != null && gunSetting.bulletPfb != null)
                        {
                            ModBehaviour.DevLog("[DragonKingBossGun] 从 Boss_Red 预设解析到原始枪械: " + item.name + ", 子弹=" + gunSetting.bulletPfb.name);
                            return gunSetting.bulletPfb;
                        }
                    }
                }
                catch (Exception e)
                {
                    lastError = e;
                }
                finally
                {
                    DestroyGeneratedItems(generatedItems);
                }
            }

            if (lastError != null)
            {
                ModBehaviour.DevLog("[DragonKingBossGun] 解析 Boss_Red 预设武器失败: " + lastError.Message);
            }

            return null;
        }

        private static void DestroyGeneratedItems(List<Item> generatedItems)
        {
            if (generatedItems == null)
            {
                return;
            }

            for (int i = 0; i < generatedItems.Count; i++)
            {
                Item item = generatedItems[i];
                if (item == null)
                {
                    continue;
                }

                try
                {
                    item.DestroyTree();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonKingBossGun] DestroyTree 失败: " + e.Message);
                    if (item.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(item.gameObject);
                    }
                }
            }
        }

        private static void ApplyBossRedProjectileToLoadedGun(Projectile projectile)
        {
            if (projectile == null)
            {
                return;
            }

            Item loadedGun = EquipmentFactory.GetLoadedGun(DragonKingBossGunConfig.WeaponTypeId);
            ItemSetting_Gun gunSetting = loadedGun != null ? loadedGun.GetComponent<ItemSetting_Gun>() : null;
            if (gunSetting != null)
            {
                gunSetting.bulletPfb = projectile;
            }
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

            int shotId;
            DragonKingBossGunProfileId profileId;
            DragonKingBossGunHitStage hitStage;
            if (!TryDecodeShotMarker(damageInfo.buffChance, out shotId, out profileId, out hitStage))
            {
                return;
            }

            DragonKingBossGunShotProfile profile = DragonKingBossGunProfiles.GetProfile(profileId);
            DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(health);
            if (receiver == null || profile == null)
            {
                return;
            }

            int markPerHit;
            int maxMarksPerStage;
            ResolveMarkRule(profile, hitStage, out markPerHit, out maxMarksPerStage);
            if (maxMarksPerStage <= 0 || markPerHit <= 0)
            {
                return;
            }

            long processedKey = ComposeProcessedHitKey(shotId, receiver.GetInstanceID(), hitStage);
            ProcessedHitState state;
            if (!processedHitPairs.TryGetValue(processedKey, out state) || Time.time - state.time >= ProcessedHitKeepTime)
            {
                state = default(ProcessedHitState);
            }

            int remainingMarks = Mathf.Max(0, maxMarksPerStage - state.marksApplied);
            int markGain = Mathf.Min(markPerHit, remainingMarks);
            if (markGain <= 0)
            {
                return;
            }

            state.time = Time.time;
            state.marksApplied += markGain;
            processedHitPairs[processedKey] = state;
            CleanupProcessedHitPairs();

            DragonFlameMarkTracker.AddMark(
                receiver,
                markGain,
                DragonKingBossGunConfig.MaxLinkedMarkStacks,
                FenHuangHalberdConfig.MarkDuration);
        }

        private static void CleanupProcessedHitPairs()
        {
            if (processedHitPairs.Count == 0 && processedGroundZoneShots.Count == 0)
            {
                return;
            }

            float now = Time.time;
            if (now - lastCleanupTime < ProcessedHitCleanupInterval)
            {
                return;
            }

            lastCleanupTime = now;
            float threshold = now - ProcessedHitKeepTime;
            processedHitKeysToRemove.Clear();

            foreach (var kvp in processedHitPairs)
            {
                if (kvp.Value.time <= threshold)
                {
                    processedHitKeysToRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < processedHitKeysToRemove.Count; i++)
            {
                processedHitPairs.Remove(processedHitKeysToRemove[i]);
            }

            processedHitKeysToRemove.Clear();

            foreach (var kvp in processedGroundZoneShots)
            {
                if (kvp.Value.time <= threshold)
                {
                    processedHitKeysToRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < processedHitKeysToRemove.Count; i++)
            {
                processedGroundZoneShots.Remove(processedHitKeysToRemove[i]);
            }
        }

        private static void ResolveMarkRule(DragonKingBossGunShotProfile profile, DragonKingBossGunHitStage hitStage, out int markPerHit, out int maxMarksPerStage)
        {
            markPerHit = profile != null ? profile.MarkPerHit : 0;
            maxMarksPerStage = profile != null ? profile.MaxMarksPerTargetPerShot : 0;
            if (profile == null)
            {
                return;
            }

            switch (hitStage)
            {
                case DragonKingBossGunHitStage.Secondary:
                    if (profile.SecondaryMarkPerHit >= 0)
                    {
                        markPerHit = profile.SecondaryMarkPerHit;
                    }

                    if (profile.MaxSecondaryMarksPerTargetPerShot >= 0)
                    {
                        maxMarksPerStage = profile.MaxSecondaryMarksPerTargetPerShot;
                    }
                    break;

                case DragonKingBossGunHitStage.Return:
                    if (profile.ReturnMarkPerHit >= 0)
                    {
                        markPerHit = profile.ReturnMarkPerHit;
                    }

                    if (profile.MaxReturnMarksPerTargetPerShot >= 0)
                    {
                        maxMarksPerStage = profile.MaxReturnMarksPerTargetPerShot;
                    }
                    break;

                case DragonKingBossGunHitStage.Followup:
                    if (profile.FollowupMarkPerHit >= 0)
                    {
                        markPerHit = profile.FollowupMarkPerHit;
                    }

                    if (profile.MaxFollowupMarksPerTargetPerShot >= 0)
                    {
                        maxMarksPerStage = profile.MaxFollowupMarksPerTargetPerShot;
                    }
                    break;
            }
        }

        private static long ComposeProcessedHitKey(int shotId, int receiverId, DragonKingBossGunHitStage hitStage)
        {
            // bit 布局：[shotId:bit34-53 / 20bit][hitStage:bit32-33 / 2bit][receiverId:bit0-31 / 32bit]
            // NextShotId 保证 shotId ∈ [1, MaxEncodedShotId=99999] < 2^17；& 0xFFFFF 为防御性截断
            // 避免 XOR 方案在 (shotId, hitStage, receiverId) 不同组合时产生碰撞。
            return ((long)(shotId & 0xFFFFF) << 34) | ((long)hitStage << 32) | (uint)receiverId;
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

        internal static float EncodeShotMarker(int shotId, DragonKingBossGunProfileId profileId, DragonKingBossGunHitStage hitStage)
        {
            return ShotMarkerBase + shotId * ShotMarkerScale + ((int)hitStage * ShotMarkerStageScale) + (int)profileId;
        }

        private static bool TryDecodeShotMarker(float marker, out int shotId, out DragonKingBossGunProfileId profileId, out DragonKingBossGunHitStage hitStage)
        {
            shotId = 0;
            profileId = DragonKingBossGunProfileId.Assault;
            hitStage = DragonKingBossGunHitStage.Primary;

            int encoded = Mathf.RoundToInt(marker);
            if (encoded < ShotMarkerBase + 1)
            {
                return false;
            }

            int payload = encoded - ShotMarkerBase;
            int localValue = payload % ShotMarkerScale;
            int profileValue = localValue % ShotMarkerStageScale;
            int stageValue = localValue / ShotMarkerStageScale;
            shotId = payload / ShotMarkerScale;

            if (shotId <= 0 ||
                profileValue < 1 || profileValue > MaxProfileId ||
                stageValue < 0 || stageValue > MaxHitStage)
            {
                return false;
            }

            profileId = (DragonKingBossGunProfileId)profileValue;
            hitStage = (DragonKingBossGunHitStage)stageValue;
            return true;
        }

        private static bool IsCompatibleBullet(Item weaponItem, Item bulletItem)
        {
            return IsDragonKingBossGun(weaponItem) && DragonKingBossGunProfiles.TryResolve(bulletItem, out _);
        }

        /// <summary>
        /// 根据弹种 profile 覆写龙枪运行时属性（射速、伤害、弹匣、换弹时间、射程、后坐力）。
        /// 仅修改运行时 StatCollection.BaseValue，不修改 Prefab/SO。
        /// </summary>
        internal static void ApplyAmmoAttributeOverride(Item gunItem, DragonKingBossGunShotProfile profile)
        {
            if (gunItem == null || profile == null || !IsDragonKingBossGun(gunItem))
            {
                return;
            }

            StatCollection stats = gunItem.Stats;
            if (stats == null)
            {
                return;
            }

            // 射速覆盖
            SetStatValue(stats, "ShootSpeed", 9.2f * profile.FireRateMult);

            // 伤害覆盖
            SetStatValue(stats, "Damage", 26f * profile.GunDamageMult);

            // 弹匣容量覆盖
            if (profile.OverrideCapacity > 0)
            {
                SetStatValue(stats, "Capacity", profile.OverrideCapacity);
            }

            // 换弹时间覆盖
            if (profile.OverrideReloadTime > 0f)
            {
                SetStatValue(stats, "ReloadTime", profile.OverrideReloadTime);
            }

            // 射程覆盖
            if (profile.OverrideBulletDistance > 0f)
            {
                SetStatValue(stats, "BulletDistance", profile.OverrideBulletDistance);
            }

            // 统一去震屏
            SetStatValue(stats, "RecoilScaleV", 0f);
            SetStatValue(stats, "RecoilScaleH", 0f);

            lastAppliedProfileId = profile.Id;
            hasAppliedProfile = true;
            ModBehaviour.DevLog("[DragonKingBossGun] 应用弹种属性覆盖: " + profile.Id +
                " ShootSpeed=" + (9.2f * profile.FireRateMult).ToString("F2") +
                " Damage=" + (26f * profile.GunDamageMult).ToString("F2") +
                " Capacity=" + profile.OverrideCapacity);
        }

        /// <summary>
        /// 场景加载时调用：找到玩家手中的龙枪并重新应用弹种属性。
        /// </summary>
        public static void ReapplyAmmoAttributeOverrideForScene()
        {
            hasAppliedProfile = false;

            if (LevelManager.Instance == null)
            {
                return;
            }

            CharacterMainControl mainChar = CharacterMainControl.Main;
            if (mainChar == null)
            {
                return;
            }

            ItemAgent_Gun gunAgent = mainChar.GetComponentInChildren<ItemAgent_Gun>();
            if (gunAgent == null || !IsDragonKingBossGun(gunAgent.Item))
            {
                return;
            }

            Item bulletItem = gunAgent.GunItemSetting != null ? gunAgent.GunItemSetting.GetCurrentLoadedBullet() : null;
            if (bulletItem == null)
            {
                return;
            }

            DragonKingBossGunShotProfile profile;
            if (DragonKingBossGunProfiles.TryResolve(bulletItem, out profile))
            {
                ApplyAmmoAttributeOverride(gunAgent.Item, profile);
            }
        }

        private static void SetStatValue(StatCollection stats, string key, float value)
        {
            Stat stat = stats.GetStat(key);
            if (stat != null)
            {
                stat.BaseValue = value;
            }
        }

        private static CharacterMainControl GetTraceTarget(ItemAgent_Gun gun)
        {
            if (traceTargetField == null || gun == null)
            {
                return null;
            }

            return traceTargetField.GetValue(gun) as CharacterMainControl;
        }

        private static Projectile ResolveBaseProjectile()
        {
            if (cachedDragonProjectile == null)
            {
                WarmupProjectileCache();

                Projectile fallbackProjectile = TryResolveFallbackProjectile();
                if (fallbackProjectile != null)
                {
                    CacheBaseProjectile(fallbackProjectile, "[DragonKingBossGun] 使用同步兜底弹幕基底: ");
                    ApplyBossRedProjectileToLoadedGun(fallbackProjectile);
                }
            }
            else
            {
                ApplyBossRedProjectileToLoadedGun(cachedDragonProjectile);
            }

            return cachedDragonProjectile;
        }

        internal static Projectile GetDragonProjectile()
        {
            return ResolveBaseProjectile();
        }

        internal static Projectile GetNativeRocketProjectile()
        {
            if (cachedNativeRocketProjectile != null)
            {
                return cachedNativeRocketProjectile;
            }

            try
            {
                // 从游戏中所有已加载的枪械中查找使用 Rocket 口径的原版枪
                ItemSetting_Gun[] allGunSettings = Resources.FindObjectsOfTypeAll<ItemSetting_Gun>();
                for (int i = 0; i < allGunSettings.Length; i++)
                {
                    ItemSetting_Gun gs = allGunSettings[i];
                    if (gs == null || gs.bulletPfb == null || gs.Item == null)
                    {
                        continue;
                    }

                    if (IsDragonKingBossGun(gs.Item))
                    {
                        continue;
                    }

                    string caliber = gs.Item.Constants != null ? gs.Item.Constants.GetString("Caliber", null) : null;
                    if (string.Equals(caliber, "Rocket", StringComparison.OrdinalIgnoreCase))
                    {
                        cachedNativeRocketProjectile = gs.bulletPfb;
                        ModBehaviour.DevLog("[DragonKingBossGun] 缓存原版火箭弹预制体: " + gs.bulletPfb.name + " (来自 " + gs.Item.name + ")");
                        return cachedNativeRocketProjectile;
                    }
                }

                // fallback: 从 Projectile 池中查找名称含 Rocket 的
                Projectile[] allProjectiles = Resources.FindObjectsOfTypeAll<Projectile>();
                for (int i = 0; i < allProjectiles.Length; i++)
                {
                    if (allProjectiles[i] != null &&
                        !string.IsNullOrEmpty(allProjectiles[i].name) &&
                        allProjectiles[i].name.IndexOf("Rocket", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cachedNativeRocketProjectile = allProjectiles[i];
                        ModBehaviour.DevLog("[DragonKingBossGun] 缓存原版火箭弹预制体(fallback): " + allProjectiles[i].name);
                        return cachedNativeRocketProjectile;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKingBossGun] 查找原版火箭弹预制体失败: " + e.Message);
            }

            return null;
        }

        private static Projectile TryResolveFallbackProjectile()
        {
            Item loadedGun = EquipmentFactory.GetLoadedGun(DragonKingBossGunConfig.WeaponTypeId);
            ItemSetting_Gun gunSetting = loadedGun != null ? loadedGun.GetComponent<ItemSetting_Gun>() : null;
            if (gunSetting != null && gunSetting.bulletPfb != null)
            {
                return gunSetting.bulletPfb;
            }

            Projectile projectile = ModBehaviour.GetCachedDragonDescendantPhase2BulletPrefab();
            if (projectile != null)
            {
                return projectile;
            }

            projectile = EquipmentFactory.GetLoadedBullet("dragon");
            if (projectile != null)
            {
                return projectile;
            }

            projectile = EquipmentFactory.GetLoadedBullet("Dragon");
            if (projectile != null)
            {
                return projectile;
            }

            return GameplayDataSettings.Prefabs.DefaultBullet;
        }

        private static void CacheBaseProjectile(Projectile projectile, string logPrefix)
        {
            cachedDragonProjectile = projectile;
            if (projectile == null)
            {
                return;
            }

            cachedProjectileRadius = projectile.radius;
            cachedProjectileScale = projectile.transform.localScale;
            cachedExplosionFx = null;

            if (!string.IsNullOrEmpty(logPrefix))
            {
                ModBehaviour.DevLog(logPrefix + projectile.name);
            }
        }

    }
}

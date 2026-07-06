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
        private const int NativeRocketBulletTypeId = 326;
        private const float DefaultProfileDamage = 26f;
        private const float DefaultProfileShootSpeed = 9.2f;
        private const float DefaultProfileCapacity = 15f;
        private const float DefaultProfileReloadTime = 3.35f;
        private const float DefaultProfileBulletDistance = 24f;

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

        private struct DragonKingBossGunStatBaseline
        {
            public float Damage;
            public float ShootSpeed;
            public float Capacity;
            public float ReloadTime;
            public float BulletDistance;
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
        private static bool nativeRocketProjectileLookupAttempted;

        // 弹种属性覆盖：按枪实例记录已应用 profile 和重铸后的属性基准。
        private static readonly Dictionary<int, DragonKingBossGunProfileId> appliedProfileByItemInstance = new Dictionary<int, DragonKingBossGunProfileId>();
        private static readonly Dictionary<int, DragonKingBossGunStatBaseline> statBaselineByItemInstance = new Dictionary<int, DragonKingBossGunStatBaseline>();

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
            if (hurtEventSubscribed)
            {
                Health.OnHurt -= OnDragonKingBossGunHurt;
                hurtEventSubscribed = false;
            }

            ClearSceneCaches();
            ClearAmmoProfileStateCaches();
        }

        /// <summary>
        /// 场景切换时清理弹幕/命中临时缓存，但保留 Health.OnHurt 订阅和枪实例弹种基准。
        /// </summary>
        public static void ClearSceneCaches()
        {
            processedHitPairs.Clear();
            processedGroundZoneShots.Clear();
            processedHitKeysToRemove.Clear();
            cachedDragonProjectile = null;
            cachedExplosionFx = null;
            cachedNativeRocketProjectile = null;
            nativeRocketProjectileLookupAttempted = false;
            shotSequence = 0;
            lastCleanupTime = 0f;
            bossRedProjectileWarmupStarted = false;
            DragonKingBossGunProjectileAgent.ClearStaticCaches();
        }

        private static void ClearAmmoProfileStateCaches()
        {
            appliedProfileByItemInstance.Clear();
            statBaselineByItemInstance.Clear();
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
            CharacterRandomPreset[] presets = ObjectCache.GetCharacterPresets();
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
            ApplyAmmoProfileInternal(gunItem, profile, "Legacy");
        }

        private static bool ApplyAmmoProfileInternal(Item gunItem, DragonKingBossGunShotProfile profile, string reason)
        {
            if (gunItem == null || profile == null || !IsDragonKingBossGun(gunItem))
            {
                return false;
            }

            StatCollection stats = gunItem.Stats;
            if (stats == null)
            {
                return false;
            }

            int itemKey = gunItem.GetInstanceID();
            DragonKingBossGunStatBaseline baseline = CaptureAmmoStatBaseline(stats, itemKey);
            float shootSpeed = Mathf.Max(0.01f, baseline.ShootSpeed * Mathf.Max(0.01f, profile.FireRateMult));
            float damage = Mathf.Max(0.01f, baseline.Damage * Mathf.Max(0.01f, profile.GunDamageMult));
            float capacityRatio = profile.OverrideCapacity > 0 ? profile.OverrideCapacity / DefaultProfileCapacity : 1f;
            int capacity = Mathf.Max(1, Mathf.RoundToInt(baseline.Capacity * capacityRatio));
            float reloadRatio = profile.OverrideReloadTime > 0f ? profile.OverrideReloadTime / DefaultProfileReloadTime : 1f;
            float reloadTime = Mathf.Max(0.05f, baseline.ReloadTime * reloadRatio);
            float distanceRatio = profile.OverrideBulletDistance > 0f ? profile.OverrideBulletDistance / DefaultProfileBulletDistance : 1f;
            float bulletDistance = Mathf.Max(0.5f, baseline.BulletDistance * distanceRatio);

            // 射速覆盖
            SetStatValue(stats, "ShootSpeed", shootSpeed);

            // 伤害覆盖
            SetStatValue(stats, "Damage", damage);

            // 弹匣容量覆盖
            SetStatValue(stats, "Capacity", capacity);

            // 换弹时间覆盖
            SetStatValue(stats, "ReloadTime", reloadTime);

            // 射程覆盖
            SetStatValue(stats, "BulletDistance", bulletDistance);

            // 统一去震屏
            SetStatValue(stats, "RecoilScaleV", 0f);
            SetStatValue(stats, "RecoilScaleH", 0f);

            statBaselineByItemInstance[itemKey] = baseline;
            appliedProfileByItemInstance[itemKey] = profile.Id;
            ModBehaviour.DevLog("[DragonKingBossGun] 应用弹种属性覆盖: " + profile.Id +
                " Reason=" + (reason ?? string.Empty) +
                " ShootSpeed=" + shootSpeed.ToString("F2") +
                " Damage=" + damage.ToString("F2") +
                " Capacity=" + capacity);
            return true;
        }

        internal static bool TryApplyAmmoProfile(Item gunItem, DragonKingBossGunShotProfile profile, string reason)
        {
            if (gunItem == null || profile == null || !IsDragonKingBossGun(gunItem))
            {
                return false;
            }

            int itemKey = gunItem.GetInstanceID();
            DragonKingBossGunProfileId appliedProfileId;
            if (!ShouldForceAmmoProfileApply(reason) &&
                appliedProfileByItemInstance.TryGetValue(itemKey, out appliedProfileId) &&
                appliedProfileId == profile.Id &&
                statBaselineByItemInstance.ContainsKey(itemKey))
            {
                return false;
            }

            return ApplyAmmoProfileInternal(gunItem, profile, reason);
        }

        private static bool ShouldForceAmmoProfileApply(string reason)
        {
            return string.Equals(reason, "Legacy", StringComparison.Ordinal) ||
                   string.Equals(reason, "RuntimeRestore", StringComparison.Ordinal) ||
                   string.Equals(reason, "SceneReapply", StringComparison.Ordinal);
        }

        internal static bool TryApplyAmmoProfileFromTargetType(ItemSetting_Gun gunSetting, int targetTypeId, string reason)
        {
            if (gunSetting == null || !IsDragonKingBossGun(gunSetting.Item))
            {
                return false;
            }

            DragonKingBossGunShotProfile profile;
            if (!TryResolveTargetBulletProfile(gunSetting, targetTypeId, out profile))
            {
                return false;
            }

            return TryApplyAmmoProfile(gunSetting.Item, profile, reason);
        }

        private static bool TryResolveTargetBulletProfile(ItemSetting_Gun gunSetting, int targetTypeId, out DragonKingBossGunShotProfile profile)
        {
            profile = null;
            if (targetTypeId <= 0)
            {
                return false;
            }

            if (DragonKingBossGunProfiles.TryResolveTypeId(targetTypeId, out profile))
            {
                return true;
            }

            Item bulletItem = FindTargetBulletItem(gunSetting, targetTypeId);
            return bulletItem != null && DragonKingBossGunProfiles.TryResolve(bulletItem, out profile);
        }

        private static Item FindTargetBulletItem(ItemSetting_Gun gunSetting, int targetTypeId)
        {
            if (gunSetting == null || targetTypeId <= 0)
            {
                return null;
            }

            Item preferredBullet = gunSetting.PreferdBulletsToLoad;
            if (IsResolvableBulletOfType(preferredBullet, targetTypeId))
            {
                return preferredBullet;
            }

            Item currentLoadedBullet = gunSetting.GetCurrentLoadedBullet();
            if (IsResolvableBulletOfType(currentLoadedBullet, targetTypeId))
            {
                return currentLoadedBullet;
            }

            Item gunItem = gunSetting.Item;
            Item found = FindTargetBulletItem(gunItem != null ? gunItem.Inventory : null, targetTypeId);
            if (found != null)
            {
                return found;
            }

            ItemAgent_Gun gunAgent = gunItem != null ? gunItem.ActiveAgent as ItemAgent_Gun : null;
            if (gunAgent != null && gunAgent.Holder != null && gunAgent.Holder.CharacterItem != null)
            {
                found = FindTargetBulletItem(gunAgent.Holder.CharacterItem.Inventory, targetTypeId);
                if (found != null)
                {
                    return found;
                }
            }

            found = FindTargetBulletItem(gunItem != null ? gunItem.InInventory : null, targetTypeId);
            if (found != null)
            {
                return found;
            }

            if (LevelManager.Instance != null &&
                LevelManager.Instance.MainCharacter != null &&
                LevelManager.Instance.MainCharacter.CharacterItem != null)
            {
                found = FindTargetBulletItem(LevelManager.Instance.MainCharacter.CharacterItem.Inventory, targetTypeId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Item FindTargetBulletItem(Inventory inventory, int targetTypeId)
        {
            if (inventory == null || targetTypeId <= 0)
            {
                return null;
            }

            try
            {
                foreach (Item item in inventory)
                {
                    if (IsResolvableBulletOfType(item, targetTypeId))
                    {
                        return item;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool IsResolvableBulletOfType(Item bulletItem, int targetTypeId)
        {
            if (bulletItem == null || bulletItem.TypeID != targetTypeId)
            {
                return false;
            }

            if (bulletItem.Stackable && bulletItem.StackCount <= 0)
            {
                return false;
            }

            DragonKingBossGunShotProfile ignoredProfile;
            return DragonKingBossGunProfiles.TryResolve(bulletItem, out ignoredProfile);
        }

        internal static bool TryApplyAmmoProfileFromLoadedBullet(ItemSetting_Gun gunSetting, string reason)
        {
            if (gunSetting == null || !IsDragonKingBossGun(gunSetting.Item))
            {
                return false;
            }

            DragonKingBossGunShotProfile profile;
            Item bulletItem = gunSetting.GetCurrentLoadedBullet();
            if (bulletItem != null && DragonKingBossGunProfiles.TryResolve(bulletItem, out profile))
            {
                return TryApplyAmmoProfile(gunSetting.Item, profile, reason);
            }

            if (gunSetting.TargetBulletID > 0 &&
                TryResolveTargetBulletProfile(gunSetting, gunSetting.TargetBulletID, out profile))
            {
                return TryApplyAmmoProfile(gunSetting.Item, profile, reason);
            }

            return false;
        }

        public static void RefreshAmmoProfileAfterRuntimeRestore(Item gunItem)
        {
            InvalidateAmmoProfileState(gunItem);
            ItemSetting_Gun gunSetting = gunItem != null ? gunItem.GetComponent<ItemSetting_Gun>() : null;
            TryApplyAmmoProfileFromLoadedBullet(gunSetting, "RuntimeRestore");
        }

        private static void InvalidateAmmoProfileState(Item gunItem)
        {
            if (gunItem == null)
            {
                return;
            }

            int itemKey = gunItem.GetInstanceID();
            appliedProfileByItemInstance.Remove(itemKey);
            statBaselineByItemInstance.Remove(itemKey);
        }

        private static DragonKingBossGunStatBaseline CaptureAmmoStatBaseline(StatCollection stats, int itemKey)
        {
            DragonKingBossGunStatBaseline cachedBaseline;
            if (statBaselineByItemInstance.TryGetValue(itemKey, out cachedBaseline))
            {
                return cachedBaseline;
            }

            DragonKingBossGunProfileId previousProfileId;
            if (appliedProfileByItemInstance.TryGetValue(itemKey, out previousProfileId))
            {
                DragonKingBossGunShotProfile previousProfile = DragonKingBossGunProfiles.GetProfile(previousProfileId);
                return new DragonKingBossGunStatBaseline
                {
                    Damage = ReverseStatValue(stats, "Damage", DefaultProfileDamage, previousProfile.GunDamageMult),
                    ShootSpeed = ReverseStatValue(stats, "ShootSpeed", DefaultProfileShootSpeed, previousProfile.FireRateMult),
                    Capacity = ReverseStatValue(stats, "Capacity", DefaultProfileCapacity, previousProfile.OverrideCapacity > 0 ? previousProfile.OverrideCapacity / DefaultProfileCapacity : 1f),
                    ReloadTime = ReverseStatValue(stats, "ReloadTime", DefaultProfileReloadTime, previousProfile.OverrideReloadTime > 0f ? previousProfile.OverrideReloadTime / DefaultProfileReloadTime : 1f),
                    BulletDistance = ReverseStatValue(stats, "BulletDistance", DefaultProfileBulletDistance, previousProfile.OverrideBulletDistance > 0f ? previousProfile.OverrideBulletDistance / DefaultProfileBulletDistance : 1f)
                };
            }

            return new DragonKingBossGunStatBaseline
            {
                Damage = ReadPositiveStatValue(stats, "Damage", DefaultProfileDamage),
                ShootSpeed = ReadPositiveStatValue(stats, "ShootSpeed", DefaultProfileShootSpeed),
                Capacity = ReadPositiveStatValue(stats, "Capacity", DefaultProfileCapacity),
                ReloadTime = ReadPositiveStatValue(stats, "ReloadTime", DefaultProfileReloadTime),
                BulletDistance = ReadPositiveStatValue(stats, "BulletDistance", DefaultProfileBulletDistance)
            };
        }

        private static float ReverseStatValue(StatCollection stats, string key, float defaultBaseValue, float profileRatio)
        {
            if (profileRatio <= 0.0001f)
            {
                return defaultBaseValue;
            }

            float currentValue = ReadPositiveStatValue(stats, key, defaultBaseValue * profileRatio);
            float baselineValue = currentValue / profileRatio;
            if (float.IsNaN(baselineValue) || float.IsInfinity(baselineValue) || baselineValue <= 0f)
            {
                return defaultBaseValue;
            }

            return baselineValue;
        }

        private static float ReadPositiveStatValue(StatCollection stats, string key, float fallback)
        {
            if (stats == null)
            {
                return fallback;
            }

            Stat stat = stats.GetStat(key);
            if (stat == null || float.IsNaN(stat.BaseValue) || float.IsInfinity(stat.BaseValue) || stat.BaseValue <= 0f)
            {
                return fallback;
            }

            return stat.BaseValue;
        }

        /// <summary>
        /// 场景加载时调用：找到玩家手中的龙枪并重新应用弹种属性。
        /// </summary>
        public static void ReapplyAmmoAttributeOverrideForScene()
        {
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

            TryApplyAmmoProfileFromLoadedBullet(gunAgent.GunItemSetting, "SceneReapply");
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

            if (nativeRocketProjectileLookupAttempted)
            {
                return null;
            }

            nativeRocketProjectileLookupAttempted = true;
            try
            {
                if (TryCacheNativeRocketProjectileFromItemCollection())
                {
                    return cachedNativeRocketProjectile;
                }

                // 按原版火箭弹 TypeID 精确关联原版枪械，避免通过预制体名称猜测。
                ItemSetting_Gun[] allGunSettings = Resources.FindObjectsOfTypeAll<ItemSetting_Gun>();
                for (int i = 0; i < allGunSettings.Length; i++)
                {
                    if (TryCacheNativeRocketProjectileFromGunSetting(allGunSettings[i]))
                    {
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

        private static bool TryCacheNativeRocketProjectileFromItemCollection()
        {
            try
            {
                var instance = ItemAssetsCollection.Instance;
                if (instance == null || instance.entries == null)
                {
                    return false;
                }

                foreach (var entry in instance.entries)
                {
                    if (entry == null || entry.prefab == null)
                    {
                        continue;
                    }

                    if (TryCacheNativeRocketProjectileFromGunSetting(entry.prefab.GetComponent<ItemSetting_Gun>()))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool TryCacheNativeRocketProjectileFromGunSetting(ItemSetting_Gun gunSetting)
        {
            if (gunSetting == null || gunSetting.bulletPfb == null || gunSetting.Item == null)
            {
                return false;
            }

            if (IsDragonKingBossGun(gunSetting.Item) || !IsNativeRocketBulletGun(gunSetting))
            {
                return false;
            }

            cachedNativeRocketProjectile = gunSetting.bulletPfb;
            nativeRocketProjectileLookupAttempted = true;
            ModBehaviour.DevLog("[DragonKingBossGun] 按火箭弹 TypeID=" + NativeRocketBulletTypeId + " 缓存原版火箭弹预制体: " + gunSetting.bulletPfb.name + " (来自 " + gunSetting.Item.name + ")");
            return true;
        }

        private static bool IsNativeRocketBulletGun(ItemSetting_Gun gunSetting)
        {
            if (gunSetting == null)
            {
                return false;
            }

            if (gunSetting.TargetBulletID == NativeRocketBulletTypeId)
            {
                return true;
            }

            Item preferredBullet = gunSetting.PreferdBulletsToLoad;
            if (preferredBullet != null && preferredBullet.TypeID == NativeRocketBulletTypeId)
            {
                return true;
            }

            try
            {
                Item loadedBullet = gunSetting.GetCurrentLoadedBullet();
                if (loadedBullet != null && loadedBullet.TypeID == NativeRocketBulletTypeId)
                {
                    return true;
                }
            }
            catch { }

            return false;
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

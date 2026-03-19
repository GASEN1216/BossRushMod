using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public static class DragonKingBossGunConfig
    {
        public const int WeaponTypeId = 500035;
        public const int MaxLinkedMarkStacks = 10;

        public const string WeaponNameKey = "DragonKingBossGun_Name";
        public const string WeaponDescKey = "DragonKingBossGun_Desc";
        public const string WeaponNameCN = "焚天龙铳";
        public const string WeaponNameEN = "Skyburn Dragon Cannon";
        public const string WeaponDescCN = "龙皇以龙息枪为骨，熔炼断界余焰而成的王兵。可兼容霰弹、AR_S 与 AR_B 三种口径，但射出的永远都是龙焰本体。命中会为断界戟铺设最多 10 层龙焰印记。";
        public const string WeaponDescEN = "A royal arm forged by the Dragon King from the Dragon's Breath and the halberd's lingering fire. It accepts S, AR, and L ammo, yet every shot manifests as pure dragon flame. Hits can prepare up to 10 Dragon Flame marks for the halberd.";

        private const string BaseName = "dragonking_Gun";
        private const string PrefabName = "dragonking_Gun_Item";
        private const string DefaultDisplayCaliber = "SMG";
        private const float DefaultMaxDurability = 100f;
        private const float DefaultRepairLossRatio = 0.2f;
        private const int MinimumValue = 4800;

        private static readonly Dictionary<string, float> WeaponStats = new Dictionary<string, float>
        {
            { "Damage", 26f },
            { "ShootSpeed", 9.2f },
            { "ShootSpeedGainEachShoot", 0f },
            { "ShootSpeedGainByShootMax", 0f },
            { "Capacity", 15f },
            { "ReloadTime", 3.35f },
            { "BurstCount", 1f },
            { "BulletSpeed", 108f },
            { "BulletDistance", 24f },
            { "Penetrate", 0f },
            { "TraceAbility", 0.15f },
            { "CritRate", 0.28f },
            { "CritDamageFactor", 1.6f },
            { "ArmorPiercing", 0f },
            { "ArmorBreak", 0f },
            { "ShotCount", 1f },
            { "ShotAngle", 0f },
            { "SoundRange", 30f },
            { "ADSAimDistanceFactor", 1f },
            { "ADSTime", 0.55f },
            { "MoveSpeedMultiplier", 0.85f },
            { "AdsWalkSpeedMultiplier", 0.5f },
            { "ExplosionDamageMultiplier", 1f },
            { "BuffChance", 0f },
            { "ScatterFactor", 18f },
            { "ScatterFactorADS", 6f },
            { "DefaultScatter", 0.24f },
            { "MaxScatter", 0.78f },
            { "ScatterGrow", 0.18f },
            { "ScatterRecover", 0.3f },
            { "DefaultScatterADS", 0.14f },
            { "MaxScatterADS", 0.55f },
            { "ScatterGrowADS", 0.16f },
            { "ScatterRecoverADS", 0.34f },
            { "RecoilVMin", 0.85f },
            { "RecoilVMax", 1.15f },
            { "RecoilHMin", -0.4f },
            { "RecoilHMax", 0.6f },
            { "RecoilTime", 0.075f },
            { "RecoilScaleV", 28f },
            { "RecoilScaleH", 30f },
            { "RecoilRecoverTime", 0.12f },
            { "RecoilRecover", 500f },
            { "FlashLight", 0f },
            { "OverrideTriggerMode", 0f }
        };

        private static readonly Dictionary<string, FieldInfo> fieldInfoCache = new Dictionary<string, FieldInfo>();

        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null)
            {
                return false;
            }

            bool isDragonKingBossGun =
                item.TypeID == WeaponTypeId ||
                string.Equals(baseName, BaseName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseName, PrefabName, StringComparison.OrdinalIgnoreCase);

            if (!isDragonKingBossGun)
            {
                return false;
            }

            ConfigureWeapon(item);
            return true;
        }

        public static void ConfigureWeapon(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                InjectLocalization();

                if (item.TypeID != WeaponTypeId)
                {
                    ModBehaviour.DevLog("[DragonKingBossGun] 修正 TypeID: " + item.TypeID + " -> " + WeaponTypeId);
                    item.SetTypeID(WeaponTypeId);
                }

                item.name = PrefabName;
                item.gameObject.name = PrefabName;
                item.DisplayNameRaw = WeaponNameKey;
                item.Value = Mathf.Max(item.Value, MinimumValue);

                EnsureGunSettingComponent(item);

                DragonBreathWeaponConfig.ConfigureSharedGunFoundation(
                    item,
                    DefaultDisplayCaliber,
                    DefaultMaxDurability,
                    DefaultRepairLossRatio);

                ApplyWeaponStats(item);
                DragonBreathWeaponConfig.ConfigureSharedFireGunSettings(item, false);
                ApplyGunSettings(item);
                ApplyWeaponTags(item);

                ModBehaviour.DevLog("[DragonKingBossGun] 已按 bundle 预制体完成配置，TypeID=" + item.TypeID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKingBossGun] 配置失败: " + e.Message);
            }
        }

        private static void InjectLocalization()
        {
            string displayName = L10n.T(WeaponNameCN, WeaponNameEN);
            string description = L10n.T(WeaponDescCN, WeaponDescEN);

            LocalizationHelper.InjectLocalization(WeaponNameKey, displayName);
            LocalizationHelper.InjectLocalization(WeaponDescKey, description);
            LocalizationHelper.InjectLocalization("Item_" + WeaponTypeId, displayName);
            LocalizationHelper.InjectLocalization("Item_" + WeaponTypeId + "_Desc", description);
        }

        private static ItemSetting_Gun EnsureGunSettingComponent(Item item)
        {
            if (item == null)
            {
                return null;
            }

            ItemSetting_Gun gunSetting = item.GetComponent<ItemSetting_Gun>();
            if (gunSetting != null)
            {
                return gunSetting;
            }

            gunSetting = item.gameObject.AddComponent<ItemSetting_Gun>();
            gunSetting.shootKey = DragonBreathWeaponConfig.SHOOT_KEY;
            gunSetting.reloadKey = DragonBreathWeaponConfig.RELOAD_KEY;
            gunSetting.triggerMode = ItemSetting_Gun.TriggerModes.auto;
            gunSetting.reloadMode = ItemSetting_Gun.ReloadModes.fullMag;
            gunSetting.autoReload = true;
            gunSetting.element = ElementTypes.fire;
            gunSetting.buff = null;

            ModBehaviour.DevLog("[DragonKingBossGun] 已为 bundle 物品补齐 ItemSetting_Gun: " + item.name);
            return gunSetting;
        }

        private static void ApplyGunSettings(Item item)
        {
            ItemSetting_Gun gunSetting = item != null ? item.GetComponent<ItemSetting_Gun>() : null;
            if (gunSetting == null)
            {
                return;
            }

            gunSetting.element = ElementTypes.fire;
            gunSetting.buff = null;
            gunSetting.shootKey = DragonBreathWeaponConfig.SHOOT_KEY;
            gunSetting.reloadKey = DragonBreathWeaponConfig.RELOAD_KEY;

            EnsureGunSettingReferences(gunSetting, item);
        }

        private static void ApplyWeaponTags(Item item)
        {
            if (item == null)
            {
                return;
            }

            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "Gun");
            EquipmentHelper.AddTagToItem(item, "Special");
            EquipmentHelper.AddTagToItem(item, "DragonKing");
            EquipmentHelper.AddRepairableTag(item);
            item.soundKey = "default";
        }

        private static void ApplyWeaponStats(Item item)
        {
            if (item == null)
            {
                return;
            }

            StatCollection stats = item.Stats;
            if (stats == null)
            {
                stats = item.gameObject.AddComponent<StatCollection>();
                SetPrivateField(item, "stats", stats);
            }

            foreach (var kvp in WeaponStats)
            {
                Stat stat = stats.GetStat(kvp.Key);
                if (stat == null)
                {
                    stats.Add(new Stat(kvp.Key, kvp.Value, true));
                }
                else
                {
                    stat.BaseValue = kvp.Value;
                }
            }
        }

        private static void EnsureGunSettingReferences(ItemSetting_Gun gunSetting, Item item)
        {
            if (gunSetting == null || item == null)
            {
                return;
            }

            SetPrivateField(gunSetting, "item", item);
            if (item.Stats != null)
            {
                SetPrivateField(gunSetting, "stats", item.Stats);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                return;
            }

            Type type = target.GetType();
            string key = type.FullName + "." + fieldName;

            FieldInfo field;
            if (!fieldInfoCache.TryGetValue(key, out field))
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                fieldInfoCache[key] = field;
            }

            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}

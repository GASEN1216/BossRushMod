using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    public static class DragonKingBossGunConfig
    {
        public const int WeaponTypeId = 500035;
        public const int SourceWeaponTypeId = DragonBreathConfig.WEAPON_TYPE_ID;
        public const int MaxLinkedMarkStacks = 10;

        public const string WeaponNameKey = "DragonKingBossGun_Name";
        public const string WeaponDescKey = "DragonKingBossGun_Desc";
        public const string WeaponNameCN = "焚天龙铳";
        public const string WeaponNameEN = "Skyburn Dragon Cannon";
        public const string WeaponDescCN = "龙皇以龙息枪为骨，熔炼断界余焰而成的王兵。可兼容霰弹、AR_S 与 AR_B 三种口径，但射出的永远都是龙焰本体。命中会为断界戟铺设最多 10 层龙焰印记。";
        public const string WeaponDescEN = "A royal arm forged by the Dragon King from the Dragon's Breath and the halberd's lingering fire. It accepts S, AR, and L ammo, yet every shot manifests as pure dragon flame. Hits can prepare up to 10 Dragon Flame marks for the halberd.";

        private const string PrefabName = "DragonKingBossGun_Item";
        private const string DefaultDisplayCaliber = "SMG";

        private static readonly Dictionary<string, float> WeaponStats = new Dictionary<string, float>
        {
            { "Damage", 26f },
            { "ShootSpeed", 9.2f },
            { "Capacity", 15f },
            { "ReloadTime", 3.35f },
            { "BulletSpeed", 108f },
            { "BulletDistance", 24f },
            { "TraceAbility", 0.15f },
            { "CritRate", 0.28f },
            { "CritDamageFactor", 1.6f },
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
            { "RecoilScaleV", 28f },
            { "RecoilScaleH", 30f },
            { "SoundRange", 30f },
            { "BuffChance", 0f }
        };

        private static bool initialized;
        private static Item runtimePrefab;

        public static Item RuntimePrefab
        {
            get { return runtimePrefab; }
        }

        public static bool InitializeRuntimePrefab()
        {
            if (initialized && runtimePrefab != null)
            {
                return true;
            }

            try
            {
                Item source = EquipmentFactory.GetLoadedGun(SourceWeaponTypeId);
                if (source == null)
                {
                    source = ItemAssetsCollection.GetPrefab(SourceWeaponTypeId);
                }

                if (source == null)
                {
                    ModBehaviour.DevLog("[DragonKingBossGun] 未找到龙息枪源预制体，无法初始化新武器");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                clone.name = PrefabName;
                clone.gameObject.name = PrefabName;
                clone.SetTypeID(WeaponTypeId);
                clone.DisplayNameRaw = WeaponNameKey;
                clone.Order = source.Order;
                clone.Value = Mathf.Max(source.Value, 4800);

                DragonBreathWeaponConfig.ConfigureWeapon(clone);
                EnsureInventoryComponent(clone);
                ApplyWeaponStats(clone);
                ApplyWeaponConstants(clone);
                ApplyGunSettings(clone);
                ApplyWeaponTags(clone);
                ResetDynamicPrefabState(clone);
                InjectLocalization();

                clone.transform.position = new Vector3(0f, -9999f, 0f);
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);

                ItemAssetsCollection.AddDynamicEntry(clone);

                runtimePrefab = clone;
                initialized = true;
                ModBehaviour.DevLog("[DragonKingBossGun] 新武器预制体初始化完成，TypeID=" + WeaponTypeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKingBossGun] 初始化失败: " + e.Message);
                return false;
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

        private static void ApplyWeaponConstants(Item item)
        {
            if (item == null || item.Constants == null)
            {
                return;
            }

            item.Constants.SetString("Caliber", DefaultDisplayCaliber, true);
            item.Constants.SetFloat("MaxDurability", 100f, true);
            item.Constants.SetFloat("RepairLossRatio", 0.2f, true);
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
            gunSetting.shootKey = "rifle_heavy";
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

        private static void ResetDynamicPrefabState(Item item)
        {
            if (item == null)
            {
                return;
            }

            item.hideFlags = HideFlags.None;
            item.gameObject.hideFlags = HideFlags.None;
            item.gameObject.SetActive(true);

            SetPrivateField(item, "initialized", false);

            if (item.AgentUtilities != null)
            {
                SetPrivateField(item.AgentUtilities, "master", null);
                SetPrivateField(item.AgentUtilities, "activeAgent", null);
            }
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

        private static void EnsureInventoryComponent(Item item)
        {
            if (item == null || item.Inventory != null)
            {
                return;
            }

            try
            {
                item.CreateInventoryComponent();
            }
            catch
            {
            }

            if (item.Inventory != null)
            {
                return;
            }

            Inventory inventory = item.gameObject.AddComponent<Inventory>();
            SetPrivateField(item, "inventory", inventory);
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

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}

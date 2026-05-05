using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 尸潮信标配置。
    /// 末日丧尸模式专用信标：在准备/撤离窗口内可反复使用以立即开启下一波。
    /// </summary>
    public static class ZombieTideBeaconConfig
    {
        public const int TYPE_ID = BossRushItemIds.ZombieTideBeacon;
        public const string BUNDLE_NAME = "zombie_tide_beacon";
        public const string PREFAB_NAME = "BossRush_ZombieTideBeacon";
        public const string LOC_KEY_DISPLAY = "BossRush_ZombieTideBeacon";
        public const string DISPLAY_NAME_CN = "尸潮信标";
        public const string DISPLAY_NAME_EN = "Zombie Tide Beacon";
        public const string DESCRIPTION_CN = "末日丧尸模式专用信标，可反复使用。";
        public const string DESCRIPTION_EN = "A reusable beacon for Zombie Mode.";
        public const string USE_DESC_CN = "使用：准备期快速开始下一波。";
        public const string USE_DESC_EN = "Use: start the next wave during preparation.";
        public const int VALUE = 0;
        public const int MAX_STACK = 1;
        public const float INFINITE_DURABILITY = 999f;
        public const float USE_TIME_SECONDS = 0.5f;

        private static bool runtimeFallbackRegistered = false;

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        public static string GetUseDescription()
        {
            return L10n.T(USE_DESC_CN, USE_DESC_EN);
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[ZombieTideBeaconConfig] Registered item configurator");
        }

        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); } catch { }
                if (existing != null)
                {
                    return true;
                }

                existing = ItemFactory.GetLoadedItem(TYPE_ID);
                if (existing != null)
                {
                    ConfigureItem(existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); } catch { }
                    return true;
                }

                if (runtimeFallbackRegistered)
                {
                    try { return ItemAssetsCollection.GetPrefab(TYPE_ID) != null; } catch { }
                    return false;
                }

                Item source = FindRuntimeFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog("[ZombieTideBeaconConfig] No runtime fallback source item was found");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null)
                {
                    ModBehaviour.DevLog("[ZombieTideBeaconConfig] Failed to clone runtime fallback item");
                    return false;
                }

                clone.gameObject.name = PREFAB_NAME;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);

                clone.SetTypeID(TYPE_ID);
                ConfigureItem(clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                runtimeFallbackRegistered = true;

                ModBehaviour.DevLog("[ZombieTideBeaconConfig] Runtime fallback item registered");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideBeaconConfig] EnsureRuntimeRegistration failed: " + e.Message);
                return false;
            }
        }

        public static bool EnsureRuntimeFallbackRegistrationShell()
        {
            return EnsureRuntimeRegistration();
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = DISPLAY_NAME_EN;
                item.MaxStackCount = MAX_STACK;
                if (item.StackCount <= 0)
                {
                    item.StackCount = 1;
                }
                item.Value = VALUE;
                item.Quality = 1;
                EnsureReusableInstance(item);
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelper.AddTagToItem(item, "RunOnly");
                ConfigureUsage(item);
                ModBehaviour.DevLog("[ZombieTideBeaconConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideBeaconConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void EnsureReusableInstance(Item item)
        {
            if (item == null)
            {
                return;
            }

            item.MaxDurability = INFINITE_DURABILITY;
            item.Durability = INFINITE_DURABILITY;
        }

        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;
                string displayName = isChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN;
                string description = isChinese ? DESCRIPTION_CN : DESCRIPTION_EN;

                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", description);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN + "_Desc", description);
                ModBehaviour.DevLog("[ZombieTideBeaconConfig] Localization injected");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideBeaconConfig] InjectLocalization failed: " + e.Message);
            }
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds = new int[]
            {
                AwenLootSweepTokenConfig.TYPE_ID,
                RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID,
                RespawnItemConfig.TAUNT_SMOKE_TYPE_ID,
                RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID
            };

            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null)
                    {
                        return prefab;
                    }
                }
                catch { }

                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(fallbackIds[i]);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
                catch { }
            }

            return null;
        }

        private static void ConfigureUsage(Item item)
        {
            UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
            if (usageUtils == null)
            {
                usageUtils = item.gameObject.AddComponent<UsageUtilities>();
            }

            if (usageUtils.behaviors == null)
            {
                usageUtils.behaviors = new List<UsageBehavior>();
            }
            else
            {
                usageUtils.behaviors.Clear();
            }
            usageUtils.useDurability = false;
            usageUtils.durabilityUsage = 0;

            ZombieTideBeaconUsage usage = item.GetComponent<ZombieTideBeaconUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<ZombieTideBeaconUsage>();
            }

            usageUtils.behaviors.Add(usage);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, USE_TIME_SECONDS);
        }
    }
}

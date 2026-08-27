using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 便携安全区装置配置。
    /// 丧尸模式奖励物品；在局内消耗一次，在玩家当前位置重新部署安全区。
    /// </summary>
    public static class PortableSafeZoneDeviceConfig
    {
        public const int TYPE_ID = BossRushItemIds.PortableSafeZoneDevice;
        public const string BUNDLE_NAME = "portable_safe_zone_device";
        public const string PREFAB_NAME = "BossRush_PortableSafeZoneDevice";
        public const string LOC_KEY_DISPLAY = "BossRush_PortableSafeZoneDevice";
        public const string DISPLAY_NAME_CN = "便携安全区装置";
        public const string DISPLAY_NAME_EN = "Portable Safe-Zone Device";
        public const string DESCRIPTION_CN = "丧尸模式专用装置。战斗中使用会把安全区移到当前位置，波次结束后恢复为带商人的正常安全区；准备阶段使用则额外部署一个不带商人的安全区，与正常安全区并存到下一波开始。使用一次即消耗。";
        public const string DESCRIPTION_EN = "A Zombie Mode device. Used in combat it moves the safe zone to your position, and the normal merchant zone returns after the wave; used during preparation it deploys an extra zone without a merchant that lasts until the next wave. Consumed on use.";
        public const string USE_DESC_CN = "使用：在当前位置部署安全区";
        public const string USE_DESC_EN = "Use: deploy a safe zone at your position";
        public const int VALUE = 2400;
        public const int MAX_STACK = 1;
        public const float MAX_DURABILITY = 1f;
        public const float USE_TIME_SECONDS = 0.75f;

        private static bool runtimeFallbackRegistered;

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
            ModBehaviour.DevLog("[PortableSafeZoneDeviceConfig] Registered item configurator");
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
                item.StackCount = 1;
                item.Value = VALUE;
                item.Quality = 4;
                item.MaxDurability = MAX_DURABILITY;
                item.Durability = MAX_DURABILITY;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelper.AddTagToItem(item, "RunOnly");
                ConfigureUsage(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PortableSafeZoneDeviceConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); } catch { }
                if (existing != null)
                {
                    ConfigureItem(existing);
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
                    try { return ItemAssetsCollection.GetPrefab(TYPE_ID) != null; } catch { return false; }
                }

                Item source = FindRuntimeFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog("[PortableSafeZoneDeviceConfig] No runtime fallback source item was found");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null)
                {
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
                ModBehaviour.DevLog("[PortableSafeZoneDeviceConfig] Runtime fallback item registered");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PortableSafeZoneDeviceConfig] EnsureRuntimeRegistration failed: " + e.Message);
                return false;
            }
        }

        public static bool EnsureRuntimeFallbackRegistrationShell()
        {
            return EnsureRuntimeRegistration();
        }

        public static void InjectLocalization()
        {
            try
            {
                string displayName = L10n.IsChinese ? DISPLAY_NAME_CN : DISPLAY_NAME_EN;
                string description = L10n.IsChinese ? DESCRIPTION_CN : DESCRIPTION_EN;
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY, displayName);
                LocalizationHelper.InjectLocalization(LOC_KEY_DISPLAY + "_Desc", description);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID, displayName);
                LocalizationHelper.InjectLocalization("Item_" + TYPE_ID + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN, displayName);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_CN + "_Desc", description);
                LocalizationHelper.InjectLocalization(DISPLAY_NAME_EN + "_Desc", description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PortableSafeZoneDeviceConfig] InjectLocalization failed: " + e.Message);
            }
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds =
            {
                ZombieTideBeaconConfig.TYPE_ID,
                ZombieTideInvitationConfig.TYPE_ID,
                AwenLootSweepTokenConfig.TYPE_ID,
                RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID
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

            // 成功部署后由 UsageBehavior 将耐久设为 0；CA_UseItem 随即销毁物品。
            // 这样运行时状态在使用动画期间失效时不会白白消耗装置。
            usageUtils.useDurability = false;
            usageUtils.durabilityUsage = 0;
            PortableSafeZoneDeviceUsage usage = item.GetComponent<PortableSafeZoneDeviceUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<PortableSafeZoneDeviceUsage>();
            }
            usageUtils.behaviors.Add(usage);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, USE_TIME_SECONDS);
        }
    }
}

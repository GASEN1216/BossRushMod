using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 阿稳扫箱令配置。
    /// 使用后会让阿稳按由近到远的顺序清理当前局快照中的掉落箱。
    /// </summary>
    public static class AwenLootSweepTokenConfig
    {
        public const int TYPE_ID = 500042;
        public const string BUNDLE_NAME = "awen_loot_sweep_token";
        public const string PREFAB_NAME = "BossRush_AwenLootSweepToken";
        public const string ICON_NAME = "BossRush_AwenLootSweepToken_Icon";
        public const string LOC_KEY_DISPLAY = "BossRush_AwenLootSweepToken";
        public const string DISPLAY_NAME_CN = "阿稳扫箱令";
        public const string DISPLAY_NAME_EN = "Awen Loot Sweep Token";
        public const string DESCRIPTION_CN = "一枚刻着鸭邮回收章的铜令。可在普通BossRush、模式E、模式F中使用，使用后阿稳会接手当前已存在的掉落箱，按由近到远的顺序逐个清理。";
        public const string DESCRIPTION_EN = "A brass token stamped with Awen's cleanup seal. Usable in standard BossRush, Mode E, and Mode F. Awen will take over the already-existing lootboxes and clear them from nearest to farthest.";
        public const string USE_DESC_CN = "使用：命令阿稳清理当前已存在的掉落箱";
        public const string USE_DESC_EN = "Use: Command Awen to clear the currently existing lootboxes";
        public const float USE_TIME_SECONDS = 1f;

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
            ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Registered item configurator");
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
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = 3200;
                item.Quality = 4;
                SetHiddenMember(item, "description", GetDescription());
                SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Special");
                ConfigureUsage(item);
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] ConfigureItem failed: " + e.Message);
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
                    ModBehaviour.DevLog("[AwenLootSweepTokenConfig] No runtime fallback source item was found");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null)
                {
                    ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Failed to clone runtime fallback item");
                    return false;
                }

                clone.gameObject.name = PREFAB_NAME;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);

                SetItemTypeId(clone, TYPE_ID);
                ConfigureItem(clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                runtimeFallbackRegistered = true;

                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Runtime fallback item registered");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] EnsureRuntimeRegistration failed: " + e.Message);
                return false;
            }
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
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] Localization injected");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] InjectLocalization failed: " + e.Message);
            }
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds = new int[]
            {
                AwenCourierTokenConfig.TYPE_ID,
                RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID,
                RespawnItemConfig.TAUNT_SMOKE_TYPE_ID
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

            AwenLootSweepTokenUsage usage = item.GetComponent<AwenLootSweepTokenUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<AwenLootSweepTokenUsage>();
            }

            usageUtils.behaviors.Add(usage);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, USE_TIME_SECONDS);
        }

        private static void SetItemTypeId(Item item, int typeId)
        {
            if (item == null)
            {
                return;
            }

            // Item 类提供公开方法 SetTypeID 来安全设置 TypeID，无需反射。
            try
            {
                item.SetTypeID(typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepTokenConfig] SetTypeID failed: " + e.Message);
            }
        }

        private static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.SetMethod != null)
            {
                property.SetValue(target, value, null);
                return;
            }

            FieldInfo field = target.GetType().GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Duckov.Economy;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 尸潮邀请函配置。
    /// 使用后打开末日丧尸模式地图选择，确认前不会被消耗。
    /// </summary>
    public static class ZombieTideInvitationConfig
    {
        public const int TYPE_ID = BossRushItemIds.ZombieTideInvitation;
        public const string BUNDLE_NAME = "zombie_tide_invitation";
        public const string PREFAB_NAME = "BossRush_ZombieTideInvitation";
        public const string LOC_KEY_DISPLAY = "BossRush_ZombieTideInvitation";
        public const string DISPLAY_NAME_CN = "尸潮邀请函";
        public const string DISPLAY_NAME_EN = "Zombie Tide Invitation";
        public const string DESCRIPTION_CN = "进入末日丧尸模式的邀请函。确认选择地图前不会消耗。";
        public const string DESCRIPTION_EN = "An invitation to enter Zombie Mode. It is not consumed until a map is confirmed.";
        public const string USE_DESC_CN = "使用：选择末日丧尸模式地图";
        public const string USE_DESC_EN = "Use: choose a Zombie Mode map";
        public const int DEFAULT_PRICE = 20000;
        public const int BASE_SHOP_STOCK = 5;
        public const int MAX_STACK = 1;
        public const float USE_TIME_SECONDS = 0.25f;

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
            ModBehaviour.DevLog("[ZombieTideInvitationConfig] Registered item configurator");
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
                    ModBehaviour.DevLog("[ZombieTideInvitationConfig] No runtime fallback source item was found");
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null)
                {
                    ModBehaviour.DevLog("[ZombieTideInvitationConfig] Failed to clone runtime fallback item");
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

                ModBehaviour.DevLog("[ZombieTideInvitationConfig] Runtime fallback item registered");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideInvitationConfig] EnsureRuntimeRegistration failed: " + e.Message);
                return false;
            }
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
                item.Value = DEFAULT_PRICE;
                item.Quality = 3;
                item.MaxDurability = 999f;
                item.Durability = 999f;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());
                EquipmentHelper.AddTagToItem(item, "Key");
                EquipmentHelper.AddTagToItem(item, "SpecialKey");
                EquipmentHelper.AddTagToItem(item, "Special");
                ConfigureUsage(item);
                ModBehaviour.DevLog("[ZombieTideInvitationConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideInvitationConfig] ConfigureItem failed: " + e.Message);
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
                ModBehaviour.DevLog("[ZombieTideInvitationConfig] Localization injected");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideInvitationConfig] InjectLocalization failed: " + e.Message);
            }
        }

        /// <summary>
        /// 将尸潮邀请函注入到基地售货机
        /// </summary>
        public static void InjectIntoShops(string targetSceneName = null)
        {
            try
            {
                string currentScene = targetSceneName;
                if (string.IsNullOrEmpty(currentScene))
                {
                    try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                }
                if (currentScene != "Base_SceneV2")
                {
                    return;
                }

                Duckov.Economy.StockShop[] shops = ObjectCache.GetStockShops();
                if (shops == null || shops.Length == 0)
                {
                    return;
                }

                ModBehaviour inst = ModBehaviour.Instance;
                int addedCount = 0;
                foreach (Duckov.Economy.StockShop shop in shops)
                {
                    if (TryInjectIntoShop(shop, inst))
                    {
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    ModBehaviour.DevLog("[ZombieTideInvitationConfig] 商店注入完成，新增 " + addedCount + " 个条目");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieTideInvitationConfig] 商店注入失败: " + e.Message);
            }
        }

        public static bool TryInjectIntoShop(Duckov.Economy.StockShop shop, ModBehaviour inst = null)
        {
            if (shop == null || shop.entries == null)
            {
                return false;
            }

            inst = inst ?? ModBehaviour.Instance;
            if (inst == null || !inst.IsBaseHubNormalMerchantShop(shop))
            {
                return false;
            }

            foreach (Duckov.Economy.StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == TYPE_ID)
                {
                    return false;
                }
            }

            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
            itemEntry.typeID = TYPE_ID;
            itemEntry.maxStock = BASE_SHOP_STOCK;
            itemEntry.forceUnlock = true;
            itemEntry.priceFactor = 1f;
            itemEntry.possibility = 1f;
            itemEntry.lockInDemo = false;

            Duckov.Economy.StockShop.Entry wrapped = new Duckov.Economy.StockShop.Entry(itemEntry);
            wrapped.CurrentStock = BASE_SHOP_STOCK;
            wrapped.Show = true;

            shop.entries.Add(wrapped);
            return true;
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds = new int[]
            {
                BossRushItemIds.BossRushTicket,
                BloodhuntTransponderConfig.TYPE_ID,
                AwenCourierTokenConfig.TYPE_ID
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

            ZombieTideInvitationUsage usage = item.GetComponent<ZombieTideInvitationUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<ZombieTideInvitationUsage>();
            }

            usageUtils.behaviors.Add(usage);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, USE_TIME_SECONDS);
        }
    }

}

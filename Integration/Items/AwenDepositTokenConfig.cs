using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 阿稳快递牌配置。
    /// 使用后会把玩家当前穿戴和背包中的物品一键快递回家。
    /// </summary>
    public static class AwenCourierTokenConfig
    {
        public const int TYPE_ID = 500008;
        public const string BUNDLE_NAME = "awen_courier_token";
        public const string PREFAB_NAME = "AwenCourierToken";
        public const string ICON_NAME = "AwenCourierToken";
        public const int DEFAULT_MAX_STOCK = 5;
        public const string LOC_KEY_DISPLAY = "BossRush_AwenCourierToken";
        public const string TAG_NAME = "Courier";
        public const string TAG_DISPLAY_CN = "快递";
        public const string TAG_DISPLAY_EN = "Courier";
        public const string TAG_DESC_CN = "可一键快递回家当前携带物品";
        public const string TAG_DESC_EN = "Used to ship your currently carried items back home";
        public const string DISPLAY_NAME_CN = "阿稳快递牌";
        public const string DISPLAY_NAME_EN = "Awen Courier Token";
        public const string DESCRIPTION_CN = "刻着小鸭邮记的铜制快递牌。使用后会把你当前穿戴和背包中的物品一键快递回家。在基地使用免费，其他地方会按阿稳快递服务的同价收费。";
        public const string DESCRIPTION_EN = "A brass courier token stamped with Awen's duck mark. Use it to ship your equipped gear and backpack items back home in one go. It is free in base, and costs the same delivery fee as Awen's courier service elsewhere.";
        public const string USE_DESC_CN = "使用：基地免费，其他地方按阿稳快递费送回当前携带物品";
        public const string USE_DESC_EN = "Use: Free in base, otherwise costs Awen's normal courier fee";
        public const float USE_TIME_SECONDS = 1.5f;

        private static Tag cachedCourierTag;

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

        public static string GetSuccessBannerText(int shippedCount)
        {
            return GetSuccessBannerText(shippedCount, 0);
        }

        public static string GetSuccessBannerText(int shippedCount, int fee)
        {
            if (fee <= 0)
            {
                return L10n.T(
                    "<color=#00FF00>已快递回家，共 " + shippedCount + " 件，基地免费</color>",
                    "<color=#00FF00>Shipped home: " + shippedCount + " items, free in base</color>");
            }

            return L10n.T(
                "<color=#00FF00>已快递回家，共 " + shippedCount + " 件，花费 " + fee + "</color>",
                "<color=#00FF00>Shipped home: " + shippedCount + " items, cost " + fee + "</color>");
        }

        public static string GetSuccessMessageText(int shippedCount)
        {
            return L10n.T(
                "已一键快递回家，共 " + shippedCount + " 件",
                "Sent home in one tap: " + shippedCount + " items");
        }

        public static string GetSuccessMessageText(int shippedCount, int fee)
        {
            if (fee <= 0)
            {
                return L10n.T(
                    "基地免费快递回家，共 " + shippedCount + " 件",
                    "Shipped home for free in base: " + shippedCount + " items");
            }

            return L10n.T(
                "已支付快递费 " + fee + "，送回 " + shippedCount + " 件",
                "Paid " + fee + " for delivery and shipped " + shippedCount + " items");
        }

        public static string GetNoItemsMessageText()
        {
            return L10n.T(
                "身上没有可快递回家的物品",
                "You have no items to ship home");
        }

        public static string GetInsufficientFundsMessageText(int fee)
        {
            return L10n.T(
                "余额不足，当前快递需要 " + fee,
                "Not enough money. Delivery costs " + fee);
        }

        private static Tag GetCourierTag()
        {
            if (cachedCourierTag != null)
            {
                return cachedCourierTag;
            }

            try
            {
                cachedCourierTag = ScriptableObject.CreateInstance<Tag>();

                Type tagType = typeof(Tag);
                cachedCourierTag.name = TAG_NAME;

                FieldInfo showField = tagType.GetField("show", BindingFlags.NonPublic | BindingFlags.Instance);
                showField?.SetValue(cachedCourierTag, true);

                FieldInfo showDescField = tagType.GetField("showDescription", BindingFlags.NonPublic | BindingFlags.Instance);
                showDescField?.SetValue(cachedCourierTag, true);

                FieldInfo colorField = tagType.GetField("color", BindingFlags.NonPublic | BindingFlags.Instance);
                colorField?.SetValue(cachedCourierTag, new Color(0f, 0.78f, 0f, 1f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] Create courier tag failed: " + e.Message);
            }

            return cachedCourierTag;
        }

        private static void AddCourierTag(Item item)
        {
            try
            {
                Tag courierTag = GetCourierTag();
                if (courierTag != null && !item.Tags.Contains(courierTag))
                {
                    item.Tags.Add(courierTag);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] AddCourierTag failed: " + e.Message);
            }
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = 18000;
                item.Quality = 4;
                item.name = DISPLAY_NAME_EN;
                SetHiddenMember(item, "description", GetDescription());
                SetHiddenMember(item, "DescriptionRaw", GetDescription());

                ConfigureUsage(item);
                AddCourierTag(item);
                EquipmentHelper.AddTagToItem(item, "Special");

                ModBehaviour.DevLog("[AwenCourierTokenConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[AwenCourierTokenConfig] Registered item configurator");
        }

        /// <summary>
        /// 将阿稳快递牌注入到基地普通商人。
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

                Duckov.Economy.StockShop[] shops = UnityEngine.Object.FindObjectsOfType<Duckov.Economy.StockShop>();
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
                    ModBehaviour.DevLog("[AwenCourierTokenConfig] 商店注入完成，新增 " + addedCount + " 个条目");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] 商店注入失败: " + e.Message);
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
            itemEntry.maxStock = DEFAULT_MAX_STOCK;
            itemEntry.forceUnlock = true;
            itemEntry.priceFactor = 1f;
            itemEntry.possibility = 1f;
            itemEntry.lockInDemo = false;

            Duckov.Economy.StockShop.Entry wrapped = new Duckov.Economy.StockShop.Entry(itemEntry);
            wrapped.CurrentStock = DEFAULT_MAX_STOCK;
            wrapped.Show = true;

            shop.entries.Add(wrapped);
            return true;
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

                string tagDisplayName = L10n.T(TAG_DISPLAY_CN, TAG_DISPLAY_EN);
                string tagDescription = L10n.T(TAG_DESC_CN, TAG_DESC_EN);
                LocalizationHelper.InjectLocalization("Tag_" + TAG_NAME, tagDisplayName);
                LocalizationHelper.InjectLocalization("Tag_" + TAG_NAME + "_Desc", tagDescription);
                LocalizationHelper.InjectLocalization(TAG_NAME, tagDisplayName);

                ModBehaviour.DevLog("[AwenCourierTokenConfig] Localization injected");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] InjectLocalization failed: " + e.Message);
            }
        }

        private static void ConfigureUsage(Item item)
        {
            UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
            if (usageUtils == null)
            {
                usageUtils = item.gameObject.AddComponent<UsageUtilities>();
            }

            SetUsageUtilitiesMaster(usageUtils, item);
            SetUsageTime(usageUtils, USE_TIME_SECONDS);

            AwenCourierTokenUsage usage = item.GetComponent<AwenCourierTokenUsage>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<AwenCourierTokenUsage>();
            }

            if (usageUtils.behaviors == null)
            {
                usageUtils.behaviors = new List<UsageBehavior>();
            }

            if (!usageUtils.behaviors.Contains(usage))
            {
                usageUtils.behaviors.Add(usage);
            }

            SetItemUsageUtilities(item, usageUtils);
        }

        private static void SetUsageUtilitiesMaster(UsageUtilities usageUtils, Item item)
        {
            try
            {
                FieldInfo masterField = typeof(UsageUtilities).BaseType.GetField(
                    "master",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                masterField?.SetValue(usageUtils, item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] SetUsageUtilitiesMaster failed: " + e.Message);
            }
        }

        private static void SetItemUsageUtilities(Item item, UsageUtilities usageUtils)
        {
            try
            {
                FieldInfo field = typeof(Item).GetField(
                    "usageUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(item, usageUtils);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] SetItemUsageUtilities failed: " + e.Message);
            }
        }

        private static void SetUsageTime(UsageUtilities usageUtils, float useTime)
        {
            try
            {
                FieldInfo field = typeof(UsageUtilities).GetField(
                    "useTime",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(usageUtils, useTime);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenCourierTokenConfig] SetUsageTime failed: " + e.Message);
            }
        }

        private static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.SetMethod != null)
            {
                property.SetValue(target, value);
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

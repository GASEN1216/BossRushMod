using System;
using ItemStatsSystem;

namespace BossRush
{
    public static class FateEchoRelicConfig
    {
        public const int TYPE_ID = 500057;
        public const string BUNDLE_NAME = "fate_echo_relic";
        public const string PREFAB_NAME = "BossRush_ModeG_FateEchoRelic";
        public const string LOC_KEY_DISPLAY = "BossRush_FateEchoRelic";
        public const string DISPLAY_NAME_CN = "宿命回响信物";
        public const string DISPLAY_NAME_EN = "Fate Echo Relic";
        public const string DESCRIPTION_CN = "一枚铭刻着宿命回路的古老信物。裸装携带它和船票进入bossrush，将启动宿命回响模式——你的每一次选择都将成为下一局的题目。";
        public const string DESCRIPTION_EN = "An ancient relic engraved with fate circuits. Enter bossrush naked with this and a ticket to start Fate Echo mode — your every choice becomes the next run's challenge.";
        public const int DEFAULT_PRICE = 20000;
        public const int BASE_SHOP_STOCK = 5;

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = DEFAULT_PRICE;
                item.Quality = 5;
                item.name = DISPLAY_NAME_EN;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());

                EquipmentHelper.AddTagToItem(item, "Key");
                EquipmentHelper.AddTagToItem(item, "SpecialKey");
                EquipmentHelper.AddTagToItem(item, "Special");

                ModBehaviour.DevLog("[FateEchoRelicConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FateEchoRelicConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[FateEchoRelicConfig] Registered item configurator");
        }

        /// <summary>
        /// 将宿命回响信物注入到基地售货机
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
                if (currentScene != "Base_SceneV2") return;

                Duckov.Economy.StockShop[] shops = ObjectCache.GetStockShops();
                if (shops == null || shops.Length == 0) return;

                ModBehaviour inst = ModBehaviour.Instance;
                int addedCount = 0;
                foreach (Duckov.Economy.StockShop shop in shops)
                {
                    if (TryInjectIntoShop(shop, inst)) addedCount++;
                }

                if (addedCount > 0)
                {
                    ModBehaviour.DevLog("[FateEchoRelicConfig] 商店注入完成，新增 " + addedCount + " 个条目");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FateEchoRelicConfig] 商店注入失败: " + e.Message);
            }
        }

        public static bool TryInjectIntoShop(Duckov.Economy.StockShop shop, ModBehaviour inst = null)
        {
            if (shop == null || shop.entries == null) return false;

            inst = inst ?? ModBehaviour.Instance;
            if (inst == null || !inst.IsBaseHubNormalMerchantShop(shop)) return false;

            foreach (Duckov.Economy.StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == TYPE_ID) return false;
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
    }
}

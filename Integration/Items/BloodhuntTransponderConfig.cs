using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    public static class BloodhuntTransponderConfig
    {
        public const int TYPE_ID = 500036;
        public const string BUNDLE_NAME = "bloodhunt_transponder";
        public const string PREFAB_NAME = "BossRush_ModeF_BloodhuntTransponder";
        public const string LOC_KEY_DISPLAY = "BossRush_BloodhuntTransponder";
        public const string DISPLAY_NAME_CN = "血猎收发器";
        public const string DISPLAY_NAME_EN = "Bloodhunt Transponder";
        public const string DESCRIPTION_CN = "一台被鲜血浸透的军用收发器。裸装携带它和船票进入bossrush，将启动血猎追击模式——持续掉血，击杀Boss回血续命。";
        public const string DESCRIPTION_EN = "A military transponder soaked in blood. Enter bossrush naked with this and a ticket to start Bloodhunt mode — you bleed constantly, kill bosses to survive.";

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
                item.Value = 15000;
                item.Quality = 5;
                item.name = DISPLAY_NAME_EN;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", GetDescription());
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", GetDescription());

                EquipmentHelper.AddTagToItem(item, "Key");
                EquipmentHelper.AddTagToItem(item, "SpecialKey");
                EquipmentHelper.AddTagToItem(item, "Special");

                ModBehaviour.DevLog("[BloodhuntTransponderConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BloodhuntTransponderConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog("[BloodhuntTransponderConfig] Registered item configurator");
        }

        /// <summary>
        /// 将血猎收发器注入到基地售货机
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

                Duckov.Economy.StockShop[] shops = UnityEngine.Object.FindObjectsOfType<Duckov.Economy.StockShop>();
                if (shops == null || shops.Length == 0) return;

                ModBehaviour inst = ModBehaviour.Instance;
                int addedCount = 0;
                foreach (Duckov.Economy.StockShop shop in shops)
                {
                    if (shop == null || shop.entries == null) continue;
                    if (inst == null || !inst.IsBaseHubNormalMerchantShop(shop)) continue;

                    // 检查是否已存在
                    bool exists = false;
                    foreach (Duckov.Economy.StockShop.Entry entry in shop.entries)
                    {
                        if (entry != null && entry.ItemTypeID == TYPE_ID)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists) continue;

                    StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                    itemEntry.typeID = TYPE_ID;
                    itemEntry.maxStock = 5;
                    itemEntry.forceUnlock = true;
                    itemEntry.priceFactor = 1f;
                    itemEntry.possibility = 1f;
                    itemEntry.lockInDemo = false;

                    Duckov.Economy.StockShop.Entry wrapped = new Duckov.Economy.StockShop.Entry(itemEntry);
                    wrapped.CurrentStock = 5;
                    wrapped.Show = true;

                    shop.entries.Add(wrapped);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    ModBehaviour.DevLog("[BloodhuntTransponderConfig] 商店注入完成，新增 " + addedCount + " 个条目");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BloodhuntTransponderConfig] 商店注入失败: " + e.Message);
            }
        }

    }
}

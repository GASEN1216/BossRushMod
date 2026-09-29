// ============================================================================
// FactionFlagConfig.cs - 营旗物品配置
// ============================================================================
// 模块说明：
//   定义 Mode E（划地为营）营旗物品的配置常量、本地化和商店注入逻辑。
//   营旗是 Mode E 的入场凭证，分为随机营旗和 5 种指定阵营营旗。
//   玩家在基地售货机购买营旗后，裸装携带进入竞技场即可启动 Mode E。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;
using Duckov.Economy;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 营旗物品配置（Mode E 入场凭证）
    /// </summary>
    public static class FactionFlagConfig
    {
        // ============================================================================
        // 物品 TypeID
        // ============================================================================

        /// <summary>随机营旗 TypeID</summary>
        public const int RANDOM_FLAG_TYPE_ID = 500020;

        /// <summary>拾荒者营旗 TypeID</summary>
        public const int SCAV_FLAG_TYPE_ID = 500021;

        /// <summary>USEC营旗 TypeID</summary>
        public const int USEC_FLAG_TYPE_ID = 500022;

        /// <summary>BEAR营旗 TypeID</summary>
        public const int BEAR_FLAG_TYPE_ID = 500023;

        /// <summary>实验室营旗 TypeID</summary>
        public const int LAB_FLAG_TYPE_ID = 500024;

        /// <summary>狼群营旗 TypeID</summary>
        public const int WOLF_FLAG_TYPE_ID = 500025;

        /// <summary>爷的营旗 TypeID（玩家独立阵营，敌对所有Boss）</summary>
        public const int PLAYER_FLAG_TYPE_ID = 500026;

        /// <summary>所有营旗 TypeID 列表</summary>
        public static readonly int[] ALL_FLAG_TYPE_IDS = new int[]
        {
            RANDOM_FLAG_TYPE_ID,
            SCAV_FLAG_TYPE_ID,
            USEC_FLAG_TYPE_ID,
            BEAR_FLAG_TYPE_ID,
            LAB_FLAG_TYPE_ID,
            WOLF_FLAG_TYPE_ID,
            PLAYER_FLAG_TYPE_ID
        };

        /// <summary>商店默认库存</summary>
        public const int DEFAULT_MAX_STOCK = 5;

        // ============================================================================
        // 本地化常量
        // ============================================================================

        // --- 随机营旗 ---
        public const string RANDOM_FLAG_LOC_KEY = "BossRush_FactionFlagRandom";
        public const string RANDOM_FLAG_NAME_CN = "随机营旗";
        public const string RANDOM_FLAG_NAME_EN = "Random Faction Flag";
        public const string RANDOM_FLAG_DESC_CN = "旗面是空白的，扛上它，才知道自己跟哪一伙。裸装带着它进入 BossRush，会被随机分到一个阵营，参加划地为营。";
        public const string RANDOM_FLAG_DESC_EN = "A blank flag; you only learn whose side you're on once you pick it up. Enter BossRush naked with it and you're dropped into a random faction for Faction War.";

        // --- 拾荒者营旗 ---
        public const string SCAV_FLAG_LOC_KEY = "BossRush_FactionFlagScav";
        public const string SCAV_FLAG_NAME_CN = "拾荒者营旗";
        public const string SCAV_FLAG_NAME_EN = "Scav Faction Flag";
        public const string SCAV_FLAG_DESC_CN = "拾荒者的旗，补丁摞补丁，旗杆是根撬棍。裸装带着它进入 BossRush，加入拾荒者阵营，参加划地为营。";
        public const string SCAV_FLAG_DESC_EN = "The scavs' flag, patch on patch, hung on a pry bar. Enter BossRush naked with it to join the Scav faction in Faction War.";

        // --- USEC营旗 ---
        public const string USEC_FLAG_LOC_KEY = "BossRush_FactionFlagUsec";
        public const string USEC_FLAG_NAME_CN = "USEC营旗";
        public const string USEC_FLAG_NAME_EN = "USEC Faction Flag";
        public const string USEC_FLAG_DESC_CN = "USEC 的旗，边角压得平整，一看就是有人管着的队伍。裸装带着它进入 BossRush，加入 USEC 阵营，参加划地为营。";
        public const string USEC_FLAG_DESC_EN = "The USEC flag, edges pressed flat; somebody runs a tight ship. Enter BossRush naked with it to join the USEC faction in Faction War.";

        // --- BEAR营旗 ---
        public const string BEAR_FLAG_LOC_KEY = "BossRush_FactionFlagBear";
        public const string BEAR_FLAG_NAME_CN = "BEAR营旗";
        public const string BEAR_FLAG_NAME_EN = "BEAR Faction Flag";
        public const string BEAR_FLAG_DESC_CN = "BEAR 的旗，布厚得像帆布，风大了也不见飘。裸装带着它进入 BossRush，加入 BEAR 阵营，参加划地为营。";
        public const string BEAR_FLAG_DESC_EN = "The BEAR flag, cloth as thick as canvas; it barely stirs in a gale. Enter BossRush naked with it to join the BEAR faction in Faction War.";

        // --- 实验室营旗 ---
        public const string LAB_FLAG_LOC_KEY = "BossRush_FactionFlagLab";
        public const string LAB_FLAG_NAME_CN = "实验室营旗";
        public const string LAB_FLAG_NAME_EN = "Lab Faction Flag";
        public const string LAB_FLAG_DESC_CN = "实验室的旗，白底，边上沾着洗不掉的药水渍。裸装带着它进入 BossRush，加入实验室阵营，参加划地为营。";
        public const string LAB_FLAG_DESC_EN = "The Lab flag, white cloth with chemical stains that never washed out. Enter BossRush naked with it to join the Lab faction in Faction War.";

        // --- 狼群营旗 ---
        public const string WOLF_FLAG_LOC_KEY = "BossRush_FactionFlagWolf";
        public const string WOLF_FLAG_NAME_CN = "狼群营旗";
        public const string WOLF_FLAG_NAME_EN = "Wolf Faction Flag";
        public const string WOLF_FLAG_DESC_CN = "狼群的旗，灰旗面上几道爪痕，摸上去发硬。裸装带着它进入 BossRush，加入狼群阵营，参加划地为营。";
        public const string WOLF_FLAG_DESC_EN = "The wolf pack's flag: gray cloth with claw marks, stiff to the touch. Enter BossRush naked with it to join the Wolf faction in Faction War.";

        // --- 爷的营旗 ---
        public const string PLAYER_FLAG_LOC_KEY = "BossRush_FactionFlagPlayer";
        public const string PLAYER_FLAG_NAME_CN = "爷的营旗";
        public const string PLAYER_FLAG_NAME_EN = "Lone Wolf Flag";
        public const string PLAYER_FLAG_DESC_CN = "只属于你自己的旗，杆子上就挂了这么一面。扛着它，你就是独狼一只：裸装带着它进入 BossRush，所有阵营的 Boss 都冲你来，没人跟你一伙。";
        public const string PLAYER_FLAG_DESC_EN = "A flag that is yours alone, the only one on its pole. Carry it and you're a Lone Wolf: enter BossRush naked with it and every faction's Bosses come for you. Nobody is on your side.";

        // ============================================================================
        // 营旗信息结构（内部使用）
        // ============================================================================

        private struct FlagInfo
        {
            public int typeId;
            public string locKey;
            public string nameCN;
            public string nameEN;
            public string descCN;
            public string descEN;
        }

        private static readonly FlagInfo[] AllFlags = new FlagInfo[]
        {
            new FlagInfo { typeId = RANDOM_FLAG_TYPE_ID, locKey = RANDOM_FLAG_LOC_KEY, nameCN = RANDOM_FLAG_NAME_CN, nameEN = RANDOM_FLAG_NAME_EN, descCN = RANDOM_FLAG_DESC_CN, descEN = RANDOM_FLAG_DESC_EN },
            new FlagInfo { typeId = SCAV_FLAG_TYPE_ID, locKey = SCAV_FLAG_LOC_KEY, nameCN = SCAV_FLAG_NAME_CN, nameEN = SCAV_FLAG_NAME_EN, descCN = SCAV_FLAG_DESC_CN, descEN = SCAV_FLAG_DESC_EN },
            new FlagInfo { typeId = USEC_FLAG_TYPE_ID, locKey = USEC_FLAG_LOC_KEY, nameCN = USEC_FLAG_NAME_CN, nameEN = USEC_FLAG_NAME_EN, descCN = USEC_FLAG_DESC_CN, descEN = USEC_FLAG_DESC_EN },
            new FlagInfo { typeId = BEAR_FLAG_TYPE_ID, locKey = BEAR_FLAG_LOC_KEY, nameCN = BEAR_FLAG_NAME_CN, nameEN = BEAR_FLAG_NAME_EN, descCN = BEAR_FLAG_DESC_CN, descEN = BEAR_FLAG_DESC_EN },
            new FlagInfo { typeId = LAB_FLAG_TYPE_ID, locKey = LAB_FLAG_LOC_KEY, nameCN = LAB_FLAG_NAME_CN, nameEN = LAB_FLAG_NAME_EN, descCN = LAB_FLAG_DESC_CN, descEN = LAB_FLAG_DESC_EN },
            new FlagInfo { typeId = WOLF_FLAG_TYPE_ID, locKey = WOLF_FLAG_LOC_KEY, nameCN = WOLF_FLAG_NAME_CN, nameEN = WOLF_FLAG_NAME_EN, descCN = WOLF_FLAG_DESC_CN, descEN = WOLF_FLAG_DESC_EN },
            new FlagInfo { typeId = PLAYER_FLAG_TYPE_ID, locKey = PLAYER_FLAG_LOC_KEY, nameCN = PLAYER_FLAG_NAME_CN, nameEN = PLAYER_FLAG_NAME_EN, descCN = PLAYER_FLAG_DESC_CN, descEN = PLAYER_FLAG_DESC_EN },
        };

        // ============================================================================
        // 物品配置
        // ============================================================================

        /// <summary>
        /// 配置营旗物品（由 ItemFactory 调用）
        /// </summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                int typeId = item.TypeID;

                // 查找对应的营旗信息，设置本地化键
                for (int i = 0; i < AllFlags.Length; i++)
                {
                    if (AllFlags[i].typeId == typeId)
                    {
                        item.DisplayNameRaw = AllFlags[i].locKey;
                        break;
                    }
                }

                ModBehaviour.DevLog("[FactionFlagConfig] 营旗物品配置完成: TypeID=" + typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FactionFlagConfig] 配置物品失败: " + e.Message);
            }
        }

        // ============================================================================
        // 注册与本地化
        // ============================================================================

        /// <summary>
        /// 注册所有营旗的配置器到 ItemFactory
        /// </summary>
        public static void RegisterConfigurators()
        {
            for (int i = 0; i < ALL_FLAG_TYPE_IDS.Length; i++)
            {
                ItemFactory.RegisterConfigurator(ALL_FLAG_TYPE_IDS[i], ConfigureItem);
            }
            ModBehaviour.DevLog("[FactionFlagConfig] 已注册 " + ALL_FLAG_TYPE_IDS.Length + " 个营旗配置器");
        }

        /// <summary>
        /// 注入所有营旗的本地化文本
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                bool isChinese = L10n.IsChinese;

                for (int i = 0; i < AllFlags.Length; i++)
                {
                    FlagInfo flag = AllFlags[i];
                    string displayName = isChinese ? flag.nameCN : flag.nameEN;
                    string description = isChinese ? flag.descCN : flag.descEN;

                    // 注入本地化键
                    LocalizationHelper.InjectLocalization(flag.locKey, displayName);
                    LocalizationHelper.InjectLocalization(flag.locKey + "_Desc", description);

                    // 注入物品 ID 键（游戏系统使用 Item_{TypeID} 格式）
                    string itemKey = "Item_" + flag.typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

                    // 注入中英文键（兼容性）
                    LocalizationHelper.InjectLocalization(flag.nameCN, displayName);
                    LocalizationHelper.InjectLocalization(flag.nameEN, displayName);
                    LocalizationHelper.InjectLocalization(flag.nameCN + "_Desc", description);
                    LocalizationHelper.InjectLocalization(flag.nameEN + "_Desc", description);
                }

                ModBehaviour.DevLog("[FactionFlagConfig] 本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FactionFlagConfig] 本地化注入失败: " + e.Message);
            }
        }

        // ============================================================================
        // 商店注入
        // ============================================================================

        /// <summary>缓存已注入的商店条目引用</summary>
        private static readonly Dictionary<int, StockShop.Entry> injectedFlagEntries = new Dictionary<int, StockShop.Entry>();

        public static void ResetStaticCaches()
        {
            injectedFlagEntries.Clear();
        }

        internal static int TryInjectIntoShop(StockShop shop)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || shop == null || shop.entries == null || !inst.IsBaseHubNormalMerchantShop(shop))
            {
                return 0;
            }

            HashSet<int> existingFlagIds = new HashSet<int>();
            foreach (StockShop.Entry entry in shop.entries)
            {
                if (entry != null)
                {
                    for (int i = 0; i < ALL_FLAG_TYPE_IDS.Length; i++)
                    {
                        if (entry.ItemTypeID == ALL_FLAG_TYPE_IDS[i])
                        {
                            existingFlagIds.Add(ALL_FLAG_TYPE_IDS[i]);
                            injectedFlagEntries[ALL_FLAG_TYPE_IDS[i]] = entry;
                        }
                    }
                }
            }

            int addedCount = 0;
            for (int i = 0; i < ALL_FLAG_TYPE_IDS.Length; i++)
            {
                int typeId = ALL_FLAG_TYPE_IDS[i];
                if (existingFlagIds.Contains(typeId))
                {
                    continue;
                }

                StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                itemEntry.typeID = typeId;
                itemEntry.maxStock = DEFAULT_MAX_STOCK;
                itemEntry.forceUnlock = true;
                itemEntry.priceFactor = 1f;
                itemEntry.possibility = 1f;
                itemEntry.lockInDemo = false;

                StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                wrapped.CurrentStock = DEFAULT_MAX_STOCK;
                wrapped.Show = true;

                injectedFlagEntries[typeId] = wrapped;
                shop.entries.Add(wrapped);
                addedCount++;
            }

            return addedCount;
        }

        /// <summary>
        /// 将所有营旗注入到基地售货机
        /// </summary>
        public static void InjectIntoShops(string targetSceneName = null)
        {
            try
            {
                // 只在基地主场景注入
                string currentScene = targetSceneName;
                if (string.IsNullOrEmpty(currentScene))
                {
                    try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                }
                if (currentScene != "Base_SceneV2") return;

                StockShop[] shops = ObjectCache.GetStockShops();
                if (shops == null || shops.Length == 0) return;

                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null) continue;
                    addedCount += TryInjectIntoShop(shop);
                }

                if (addedCount > 0)
                {
                    ModBehaviour.DevLog("[FactionFlagConfig] 营旗商店注入完成，新增 " + addedCount + " 个条目");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FactionFlagConfig] 商店注入失败: " + e.Message);
            }
        }
    }
}

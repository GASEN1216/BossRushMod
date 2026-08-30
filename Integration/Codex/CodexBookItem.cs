// ============================================================================
// CodexBookItem.cs - 鸭皇图鉴（可用物品）+ 基地商店注入
// ============================================================================
// TypeID 500061（台账见 docs/Bossrush使用物品ID表.md，AGENTS.md 4.3 严格递增不复用）。
//
// 设计要点：
//   - **零新增 bundle**：没有专属模型，走 BossRushDynamicItemRegistry 的
//     FallbackLoader 从既有物品克隆一份 prefab 顶上（形态照 RelicEggConfig），
//     图标由 EquipmentHelperIcon 从 Assets/Items/codex_book.png 注入。
//   - **使用后不消耗**：耐久模式 MaxDurability/Durability = 999f，
//     UsageBehavior 只打开面板，不 Consume（形态照 AchievementMedalConfig）。
//   - **DisplayNameRaw 必须配本地化注入**（AGENTS.md 4.4）：本物品的全部 key
//     由 Localization/CodexLocalization.cs 单点注入，本文件的 InjectLocalization()
//     只是转发门面，不再各写一份，避免两处文案漂移。
//   - 商店注入与库存持久化形态照 Achievement/AchievementMedalItem.cs：
//     只进基地普通商人（IsBaseHubNormalMerchantShop），库存跟存档走。
//   - **dormant 纪律**：codexEnabled 关闭时不注入商店条目——开关关掉之后连
//     买入口都不该出现。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>鸭皇图鉴物品配置。TypeID 500061，零新增 bundle，克隆兜底。</summary>
    public static class CodexBookConfig
    {
        #region 常量

        public const int TYPE_ID = BossRushItemIds.CodexBook;
        public const string PREFAB_NAME = "BossRush_CodexBook";

        /// <summary>DisplayNameRaw 与本地化 key（唯一出处：CodexLocalization）。</summary>
        public const string LOC_KEY_DISPLAY = "BossRush_CodexBook";

        public const string DISPLAY_NAME_CN = "鸭皇图鉴";
        public const string DISPLAY_NAME_EN = "Duckov Codex";

        public const string DESCRIPTION_CN = "一本会自己写字的厚册子。你每亲手放倒一个鸭皇，"
            + "它就悄悄补上一页：立绘、击杀次数、最快用时、初次相遇的日子和地方。"
            + "右键打开图鉴面板。";
        public const string DESCRIPTION_EN = "A heavy tome that writes itself. Every boss you bring down "
            + "in person earns a new page: portrait, kill count, fastest clear, and where and when you "
            + "first met. Right-click to open the codex.";

        /// <summary>图标资源名。EquipmentHelperIcon 按 Assets/Items/{ICON_NAME}.png 找 PNG。</summary>
        public const string ICON_NAME = "codex_book";

        public const int VALUE = 4000;
        public const int MAX_STACK = 1;
        public const int QUALITY = 5;

        /// <summary>使用耗时（秒）。翻书是即时动作，给 0 表示不做读条。</summary>
        private const float USE_TIME = 0f;

        /// <summary>不消耗物品的耐久值。与成就勋章同款手法。</summary>
        private const float NON_CONSUMABLE_DURABILITY = 999f;

        public const string STOCK_SAVE_KEY = "BossRush_CodexBookStock";
        public const int DEFAULT_MAX_STOCK = 1;

        #endregion

        private static bool runtimeFallbackRegistered;

        #region 显示文本

        public static string GetDisplayName()
        {
            return L10n.T(DISPLAY_NAME_CN, DISPLAY_NAME_EN);
        }

        public static string GetDescription()
        {
            return L10n.T(DESCRIPTION_CN, DESCRIPTION_EN);
        }

        #endregion

        #region 注册与配置

        /// <summary>注册配置器到 ItemFactory（主控在 ItemContentRegistry 里调）。</summary>
        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
            ModBehaviour.DevLog(CodexTuning.LogPrefix + "已注册图鉴物品配置器");
        }

        /// <summary>配置图鉴物品（由 ItemFactory 调用）。</summary>
        public static void ConfigureItem(Item item)
        {
            if (item == null) return;

            try
            {
                // 耐久模式：使用后不消耗。没有这两行右键一次书就没了
                item.MaxDurability = NON_CONSUMABLE_DURABILITY;
                item.Durability = NON_CONSUMABLE_DURABILITY;

                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.name = DISPLAY_NAME_EN;
                item.MaxStackCount = MAX_STACK;
                item.StackCount = 1;
                item.Value = VALUE;
                item.Quality = QUALITY;

                string description = GetDescription();
                ModeFItemConfigHelper.SetHiddenMember(item, "description", description);
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", description);

                AttachUsageBehavior(item);

                EquipmentHelper.AddTagToItem(item, "Special");
                // 专属图标：没有它就会顶着克隆源物品的脸出现在背包里
                EquipmentHelperIcon.TryInjectIcon(item, null, ICON_NAME);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "ConfigureItem failed: " + e.Message);
            }
        }

        /// <summary>
        /// 挂使用行为。master / usageUtilities 的私有成员绑定复用既有的
        /// ModeFItemConfigHelper.BindUsageUtilitiesToItem，不新增反射绑定策略。
        /// </summary>
        private static void AttachUsageBehavior(Item item)
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

            CodexBookUsageBehavior usage = item.GetComponent<CodexBookUsageBehavior>();
            if (usage == null)
            {
                usage = item.gameObject.AddComponent<CodexBookUsageBehavior>();
            }

            if (!usageUtils.behaviors.Contains(usage))
            {
                usageUtils.behaviors.Add(usage);
            }

            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, USE_TIME);
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        /// <summary>
        /// 没有专属 bundle，因此从既有物品克隆一个 prefab 顶上。
        /// 形态照 RelicEggConfig.EnsureRuntimeRegistration。
        /// </summary>
        public static bool EnsureRuntimeRegistration()
        {
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(TYPE_ID); }
                catch (Exception)
                {
                    // prefab 查询失败按"尚未注册"处理，继续走克隆路径
                }
                if (existing != null)
                {
                    ConfigureItem(existing);
                    return true;
                }

                existing = ItemFactory.GetLoadedItem(TYPE_ID);
                if (existing != null)
                {
                    ConfigureItem(existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); }
                    catch (Exception)
                    {
                        // 已在表内时重复登记会抛，忽略即可
                    }
                    return true;
                }

                if (runtimeFallbackRegistered)
                {
                    try { return ItemAssetsCollection.GetPrefab(TYPE_ID) != null; }
                    catch (Exception) { return false; }
                }

                Item source = FindRuntimeFallbackSource();
                if (source == null)
                {
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "没有可用的运行时兜底克隆源");
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
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "运行时兜底物品已注册");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "EnsureRuntimeRegistration failed: " + e.Message);
                return false;
            }
        }

        /// <summary>供 BossRushDynamicItemRegistry 的 FallbackLoader 调用。</summary>
        public static bool EnsureRuntimeFallbackRegistrationShell()
        {
            return EnsureRuntimeRegistration();
        }

        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds =
            {
                AchievementMedalConfig.TYPE_ID,
                BossRushItemIds.RelicEgg,
                BossRushItemIds.PortableSafeZoneDevice,
                AwenLootSweepTokenConfig.TYPE_ID,
            };

            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null) return prefab;
                }
                catch (Exception)
                {
                    // 该候选不可用，继续下一个
                }

                try
                {
                    Item loaded = ItemFactory.GetLoadedItem(fallbackIds[i]);
                    if (loaded != null) return loaded;
                }
                catch (Exception)
                {
                    // 同上
                }
            }

            return null;
        }

        #endregion

        #region 本地化

        /// <summary>
        /// 本地化注入门面。真正的键值表在 Localization/CodexLocalization.cs，
        /// 这里只转发，保证物品名与面板文案永远来自同一处。
        /// </summary>
        public static void InjectLocalization()
        {
            CodexLocalization.InjectBookKeys();
        }

        #endregion
    }

    /// <summary>图鉴使用行为：打开图鉴面板，不消耗物品。</summary>
    public class CodexBookUsageBehavior : UsageBehavior
    {
        /// <summary>图鉴随时可用。</summary>
        public override bool CanBeUsed(Item item, object user)
        {
            return true;
        }

        /// <summary>使用即切换面板。入口门面唯一走 CodexRuntime，禁止直接 new 面板。</summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner != null && owner.CodexRuntime != null)
                {
                    owner.CodexRuntime.ToggleCodexPanel();
                    return;
                }

                ModBehaviour.DevLog(CodexTuning.LogPrefix + "CodexRuntime 不可用，无法打开图鉴");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "打开图鉴面板失败: " + e.Message);
            }
        }
    }

    /// <summary>图鉴商店注入 + 库存持久化。形态照 Achievement/AchievementMedalItem.cs。</summary>
    public partial class ModBehaviour
    {
        /// <summary>已注入的商店条目引用（存档时读它的当前库存）。</summary>
        private static StockShop.Entry injectedCodexBookEntry = null;

        /// <summary>库存缓存。-1 = 尚未从存档读过。</summary>
        private static int cachedCodexBookStock = -1;

        /// <summary>把图鉴注入到基地普通商人的货架最前面。</summary>
        internal bool TryInjectCodexBookIntoShop(StockShop shop)
        {
            if (!IsBaseHubNormalMerchantShop(shop))
            {
                return false;
            }

            bool alreadyExists = false;
            foreach (StockShop.Entry entry in shop.entries)
            {
                if (entry != null && entry.ItemTypeID == CodexBookConfig.TYPE_ID)
                {
                    alreadyExists = true;
                    injectedCodexBookEntry = entry;
                    break;
                }
            }

            if (alreadyExists)
            {
                return false;
            }

            float priceFactor = 1f;
            try
            {
                Item itemPrefab = ItemAssetsCollection.GetPrefab(CodexBookConfig.TYPE_ID);
                if (itemPrefab != null)
                {
                    int rawValue = itemPrefab.GetTotalRawValue();
                    if (rawValue > 0)
                    {
                        priceFactor = 1f / rawValue;
                    }
                }
            }
            catch (Exception)
            {
                // 定价系数取不到就用 1，不阻断上架
            }

            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
            itemEntry.typeID = CodexBookConfig.TYPE_ID;
            itemEntry.maxStock = CodexBookConfig.DEFAULT_MAX_STOCK;
            itemEntry.forceUnlock = true;
            itemEntry.priceFactor = priceFactor;
            itemEntry.possibility = 1f;
            itemEntry.lockInDemo = false;

            StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
            int stockToSet = LoadCodexBookStockFromSave();
            wrapped.CurrentStock = stockToSet;
            wrapped.Show = true;

            injectedCodexBookEntry = wrapped;
            shop.entries.Insert(0, wrapped);
            DevLog(CodexTuning.LogPrefix + "图鉴上架成功，库存: " + stockToSet + ", priceFactor=" + priceFactor);
            return true;
        }

        /// <summary>扫基地商店并注入。非基地场景与开关关闭时零成本早返。</summary>
        internal void InjectCodexBookIntoShops(string targetSceneName = null)
        {
            // dormant 纪律：开关关掉之后连买入口都不该出现
            if (!IsCodexConfiguredEnabled())
            {
                return;
            }

            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
                catch (Exception) { }
            }
            if (currentScene != BaseSceneName)
            {
                return;
            }

            try
            {
                StockShop[] shops = ObjectCache.GetStockShops();
                if (shops == null || shops.Length == 0)
                {
                    return;
                }

                int addedCount = 0;
                for (int i = 0; i < shops.Length; i++)
                {
                    StockShop shop = shops[i];
                    if (shop == null) continue;

                    if (TryInjectCodexBookIntoShop(shop))
                    {
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    DevLog(CodexTuning.LogPrefix + "图鉴商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                DevLog(CodexTuning.LogPrefix + "InjectCodexBookIntoShops 出错: " + e.Message);
            }
        }

        /// <summary>从存档读库存。</summary>
        private int LoadCodexBookStockFromSave()
        {
            try
            {
                if (cachedCodexBookStock >= 0)
                {
                    return cachedCodexBookStock;
                }

                // 官方拼写是 KeyExisits（少一个 t），不要"修正"成 KeyExists
                if (SavesSystem.KeyExisits(CodexBookConfig.STOCK_SAVE_KEY))
                {
                    cachedCodexBookStock = SavesSystem.Load<int>(CodexBookConfig.STOCK_SAVE_KEY);
                    DevLog(CodexTuning.LogPrefix + "从存档读取图鉴库存: " + cachedCodexBookStock);
                    return cachedCodexBookStock;
                }

                cachedCodexBookStock = CodexBookConfig.DEFAULT_MAX_STOCK;
                return cachedCodexBookStock;
            }
            catch (Exception e)
            {
                DevLog(CodexTuning.LogPrefix + "读取图鉴库存失败: " + e.Message);
                cachedCodexBookStock = CodexBookConfig.DEFAULT_MAX_STOCK;
                return cachedCodexBookStock;
            }
        }

        /// <summary>存档时保存库存。</summary>
        private void OnCollectSaveData_CodexBookStock()
        {
            try
            {
                int stockToSave = CodexBookConfig.DEFAULT_MAX_STOCK;
                if (injectedCodexBookEntry != null)
                {
                    stockToSave = injectedCodexBookEntry.CurrentStock;
                }

                SavesSystem.Save<int>(CodexBookConfig.STOCK_SAVE_KEY, stockToSave);
                cachedCodexBookStock = stockToSave;
            }
            catch (Exception e)
            {
                DevLog(CodexTuning.LogPrefix + "保存图鉴库存失败: " + e.Message);
            }
        }

        /// <summary>读档 / 切档时重置库存缓存。</summary>
        private void OnSetFile_CodexBookStock()
        {
            cachedCodexBookStock = -1;
            injectedCodexBookEntry = null;
            DevLog(CodexTuning.LogPrefix + "检测到读档，重置图鉴库存缓存");
        }
    
        /// <summary>
        /// 鸭皇图鉴的宿主销毁清理。顺序是硬约束：先落盘、再退订、最后清静态缓存，
        /// 颠倒会把本次会话新解锁的条目写丢。
        /// </summary>
        internal void CleanupCodexRuntimeOnDestroy()
        {
            SafeRuntime.Run("CodexSaveCoordinator.TryFlushOnHostDestroy", () => CodexSaveCoordinator.TryFlushOnHostDestroy());
            SafeRuntime.Run("CodexSaveCoordinator.ShutdownSubscription", () => CodexSaveCoordinator.ShutdownSubscription());
            SafeRuntime.Run("CodexPersistence.ShutdownSubscription", () => CodexPersistence.ShutdownSubscription());
            SafeRuntime.Run("CodexKillCollector.ResetStaticCaches", () => CodexKillCollector.ResetStaticCaches());
            SafeRuntime.Run("CodexMilestones.ResetStaticCaches", () => CodexMilestones.ResetStaticCaches());
            SafeRuntime.Run("CodexView.ResetStaticCaches", () => CodexView.ResetStaticCaches());
            SafeRuntime.Run("CodexPortraitCache.ResetStaticCaches", () => CodexPortraitCache.ResetStaticCaches());
            SafeRuntime.Run("CodexBossCatalog.ResetStaticCaches", () => CodexBossCatalog.ResetStaticCaches());
            SafeRuntime.Run("CodexPersistence.ResetStaticCaches", () => CodexPersistence.ResetStaticCaches());
            SafeRuntime.Run("CodexSaveCoordinator.ResetStaticCaches", () => CodexSaveCoordinator.ResetStaticCaches());
        }
}
}

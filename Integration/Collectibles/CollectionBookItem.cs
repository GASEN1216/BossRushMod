// ============================================================================
// CollectionBookItem.cs - 收藏册（TypeID 500219）：可用书 + 基地售货机上架
// ============================================================================
// 形态照 Integration/Codex/CodexBookItem.cs：零新增 bundle、克隆兜底，使用不消耗（耐久 999），
// 使用时打开收藏册界面；不可出售，免得买了倒卖。
// 上架门槛（owner 2026-11-19）：先由杰夫的引导 590217 介绍并送一本，接取之后基地售货机才开卖。
// 上架由 CollectiblesRuntimeModule 在基地场景里做一次（库存不进存档，每次回基地补满 1 本）。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;
using ItemStatsSystem;

namespace BossRush
{
    internal static class CollectionBookItem
    {
        internal const int TypeId = BossRushItemIds.CollectionBook;
        internal const string IconName = "collection_book";
        private const int Value = 2000;
        private const int Quality = 3;
        private const float Weight = 0.8f;
        private const float NonConsumableDurability = 999f;

        private static bool _configuratorRegistered;

        internal static void RegisterConfigurator()
        {
            if (_configuratorRegistered) return;
            _configuratorRegistered = true;
            ItemFactory.RegisterConfigurator(TypeId, ConfigureItem);
        }

        internal static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                if (item.Tags != null) item.Tags.Clear();
                item.MaxDurability = NonConsumableDurability;
                item.Durability = NonConsumableDurability;
                item.DisplayNameRaw = CollectiblesLocalization.BookLocKey;
                item.name = CollectiblesLocalization.BookLocKey;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = Value;
                item.Quality = Quality;
                // 光晕跟着品质；不写会沿用克隆源的红光晕（CR-2026-11-19-001）。
                item.DisplayQuality = (DisplayQuality)CollectibleTuning.DisplayQualityOf(Quality);
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", Weight);
                AttachUsageBehavior(item);
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelper.AddTagToItem(item, "NotSellable");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "配置收藏册失败: " + e.Message);
            }
        }

        private static void AttachUsageBehavior(Item item)
        {
            UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
            if (usageUtils == null) usageUtils = item.gameObject.AddComponent<UsageUtilities>();
            if (usageUtils.behaviors == null) usageUtils.behaviors = new List<UsageBehavior>();
            // 克隆源的使用行为会一起带过来（遗种蛋 / 安全区装置），不清掉右键会连带触发它们。
            else usageUtils.behaviors.Clear();
            CollectionBookUsageBehavior usage = item.GetComponent<CollectionBookUsageBehavior>();
            if (usage == null) usage = item.gameObject.AddComponent<CollectionBookUsageBehavior>();
            if (!usageUtils.behaviors.Contains(usage)) usageUtils.behaviors.Add(usage);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usageUtils, 0f);
            usageUtils.useDurability = true;
            usageUtils.durabilityUsage = 0;
        }

        internal static bool EnsureRuntimeRegistration()
        {
            return CollectibleItems.EnsureClone(TypeId, CollectiblesLocalization.BookLocKey, ConfigureItem);
        }

        /// <summary>
        /// 把收藏册挂到基地普通商人货架上。返回「这台售货机现在卖收藏册」：刚挂上或本来就在都算；
        /// 不是基地普通商人、克隆注册失败、注入出错都返回 false，由调用方过一会儿再试（CR-2026-11-19-004）。
        /// </summary>
        internal static bool TryInjectIntoShop(StockShop shop, ModBehaviour host)
        {
            if (shop == null || shop.entries == null || host == null) return false;
            try
            {
                if (!host.IsBaseHubNormalMerchantShop(shop)) return false;
                foreach (StockShop.Entry entry in shop.entries)
                {
                    if (entry != null && entry.ItemTypeID == TypeId) return true;
                }
                // 售货机按 TypeID 取 prefab 算价、实例化：没注册的号挂上去是空条目。
                if (!EnsureRuntimeRegistration()) return false;
                StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                itemEntry.typeID = TypeId;
                itemEntry.maxStock = CollectibleTuning.BookShopMaxStock;
                itemEntry.forceUnlock = true;
                itemEntry.priceFactor = 1f;
                itemEntry.possibility = 1f;
                itemEntry.lockInDemo = false;
                StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                wrapped.CurrentStock = CollectibleTuning.BookShopMaxStock;
                wrapped.Show = true;
                // 和成就勋章、冒险家日志、鸭皇图鉴一样排在货架最前面（owner 2026-11-19；旧版 Add 落在末尾）。
                shop.entries.Insert(0, wrapped);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收藏册上架失败: " + e.Message);
                return false;
            }
        }

        internal static void ResetStaticCaches()
        {
            _configuratorRegistered = false;
        }
    }

    /// <summary>收藏册使用行为：打开收藏册界面，不消耗。</summary>
    public class CollectionBookUsageBehavior : UsageBehavior
    {
        public override bool CanBeUsed(Item item, object user)
        {
            return true;
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                CollectiblesRuntimeModule module = CollectiblesRuntimeModule.Current;
                if (module != null) module.ToggleBook();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "打开收藏册失败: " + e.Message);
            }
        }
    }
}

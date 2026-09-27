using System;
using Duckov.Economy;
using Duckov.Scenes;
using Saves;

namespace BossRush
{
    /// <summary>图鉴书基地商店库存状态的唯一运行时 owner。</summary>
    internal sealed partial class IntegrationRuntimeModule
    {
        private bool _codexBookStockEventsSubscribed;

        /// <summary>已注入的商店条目引用（存档时读取当前库存）。</summary>
        private static StockShop.Entry injectedCodexBookEntry = null;

        /// <summary>库存缓存。-1 表示尚未从存档读取。</summary>
        private static int cachedCodexBookStock = -1;

        internal void SubscribeCodexBookStockEvents()
        {
            if (_codexBookStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData += OnCollectSaveData_CodexBookStock;
            SavesSystem.OnSetFile += OnSetFile_CodexBookStock;
            _codexBookStockEventsSubscribed = true;
        }

        internal void UnsubscribeCodexBookStockEvents()
        {
            if (!_codexBookStockEventsSubscribed) return;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData_CodexBookStock;
            SavesSystem.OnSetFile -= OnSetFile_CodexBookStock;
            _codexBookStockEventsSubscribed = false;
        }

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

            // StockShop 的 priceFactor 会乘物品原始价值。固定 1 表示按 4000 金出售；
            // 旧实现用 1/rawValue 把售价压成了 1 金。
            float priceFactor = 1f;

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
            ModBehaviour.DevLog(CodexTuning.LogPrefix + "图鉴上架成功，库存: " + stockToSet + ", priceFactor=" + priceFactor);
            return true;
        }

        /// <summary>扫基地商店并注入。非基地场景与开关关闭时零成本早返。</summary>
        internal void InjectCodexBookIntoShops(string targetSceneName = null)
        {
            // dormant 纪律：开关关掉之后连买入口都不该出现
            if (!_owner.IsCodexConfiguredEnabled())
            {
                return;
            }

            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "读取当前场景失败: " + e.Message);
                }
            }
            if (currentScene != _owner.IntegrationBaseSceneName)
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
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "图鉴商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "InjectCodexBookIntoShops 出错: " + e.Message);
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

                // 官方拼写是 KeyExisits（少一个 t），不要“修正”成 KeyExists
                if (SavesSystem.KeyExisits(CodexBookConfig.STOCK_SAVE_KEY))
                {
                    cachedCodexBookStock = SavesSystem.Load<int>(CodexBookConfig.STOCK_SAVE_KEY);
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "从存档读取图鉴库存: " + cachedCodexBookStock);
                    return cachedCodexBookStock;
                }

                cachedCodexBookStock = CodexBookConfig.DEFAULT_MAX_STOCK;
                return cachedCodexBookStock;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "读取图鉴库存失败: " + e.Message);
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
                else if (cachedCodexBookStock >= 0)
                {
                    // 商店尚未注入时保留已读缓存，不能把售罄库存重置成默认值。
                    stockToSave = cachedCodexBookStock;
                }

                SavesSystem.Save<int>(CodexBookConfig.STOCK_SAVE_KEY, stockToSave);
                cachedCodexBookStock = stockToSave;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "保存图鉴库存失败: " + e.Message);
            }
        }

        /// <summary>读档 / 切档时重置库存缓存。</summary>
        private void OnSetFile_CodexBookStock()
        {
            cachedCodexBookStock = -1;
            injectedCodexBookEntry = null;
            ModBehaviour.DevLog(CodexTuning.LogPrefix + "检测到读档，重置图鉴库存缓存");
        }
    }
}

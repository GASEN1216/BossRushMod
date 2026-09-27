using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov.Economy;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class IntegrationRuntimeModule
    {
        private StockShop ammoShop;

        internal bool IsIntegrationAmmoShop(StockShop shop) { return ammoShop != null && shop == ammoShop; }

        internal void CleanupAmmoShopOnPlayerDeath(Action<string, string, Exception> LogLootWarningLimited)
        {
            try
            {
                if (ammoShop != null)
                {
                    try
                    {
                        if (ammoShop.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(ammoShop.gameObject);
                        }
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("OnPlayerDeathInBossRush_ammoShopDestroy", "玩家死亡时销毁加油站商店对象失败", e);
                    }
                    ammoShop = null;
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("OnPlayerDeathInBossRush_ammoShopCleanup", "玩家死亡时清理加油站商店失败", e);
            }
        }


        internal void EnsureAmmoShop_Utilities()
        {
            if (ammoShop != null)
            {
                return;
            }

            try
            {
                GameObject go = new GameObject("BossRush_AmmoShop");
                try
                {
                    UnityEngine.Object.DontDestroyOnLoad(go);
                }
                catch {}

                ammoShop = go.AddComponent<StockShop>();

                try
                {
                    // 使用缓存的 FieldInfo
                    var fMerchant = BossRushEagerReflectionCache.StockShop_MerchantID;
                    if (fMerchant != null)
                    {
                        fMerchant.SetValue(ammoShop, "BossRushAmmo");
                    }
                }
                catch {}

                try
                {
                    // 设置 accountAvaliable = true，允许直接扣银行余额而非消耗现金物品
                    var fAccount = BossRushEagerReflectionCache.StockShop_AccountAvaliable;
                    if (fAccount != null)
                    {
                        fAccount.SetValue(ammoShop, true);
                    }
                }
                catch {}

                try
                {
                    if (ammoShop.entries == null)
                    {
                        ammoShop.entries = new List<StockShop.Entry>();
                    }
                    else
                    {
                        ammoShop.entries.Clear();
                    }
                }
                catch {}

                List<int> ammoIds = new List<int>
                {
                    105, 648, 870, 871, 649,
                    612, 613, 615, 616, 698,
                    603, 604, 606, 607, 694,
                    594, 595, 597, 598, 691,
                    640, 708, 709, 710,
                    630, 631, 633, 634, 707,
                    621, 622, 700, 701, 702,
                    650, 1162, 918, 944, 1262,
                    326,
                    23, 24, 67, 66, 942, 660, 933, 941, 1366, 10, 17, 16, 15
                };

                try
                {
                    foreach (int id in ammoIds)
                    {
                        StockShopDatabase.ItemEntry raw = new StockShopDatabase.ItemEntry();
                        raw.typeID = id;
                        raw.maxStock = 9999;
                        raw.forceUnlock = true;
                        raw.priceFactor = 1.1f;
                        raw.possibility = 1f;
                        raw.lockInDemo = false;

                        StockShop.Entry entry = new StockShop.Entry(raw);
                        entry.CurrentStock = entry.MaxStock;
                        ammoShop.entries.Add(entry);
                    }
                }
                catch {}

                try
                {
                    // 使用缓存的 FieldInfo
                    var fItems = BossRushEagerReflectionCache.StockShop_ItemInstances;
                    if (fItems != null)
                    {
                        var dict = fItems.GetValue(ammoShop) as Dictionary<int, Item>;
                        if (dict == null)
                        {
                            dict = new Dictionary<int, Item>();
                            fItems.SetValue(ammoShop, dict);
                        }

                        foreach (int id in ammoIds)
                        {
                            if (dict.ContainsKey(id))
                            {
                                continue;
                            }
                            Item item = null;
                            try
                            {
                                item = ItemAssetsCollection.InstantiateSync(id);
                            }
                            catch {}
                            if (item == null)
                            {
                                continue;
                            }
                            try
                            {
                                dict[id] = item;
                            }
                            catch {}
                        }
                    }
                }
                catch {}
            }
            catch {}
        }

        internal void ShowAmmoShop()
        {
            try
            {
                // 每次打开加油站时重置 ID 105 购买计数
                Item105PurchaseCount = 0;

                EnsureAmmoShop_Utilities();
                if (ammoShop != null)
                {
                    ammoShop.ShowUI();
                }
            }
            catch {}
        }

        internal void CleanupAmmoShop()
        {
            try
            {
                if (ammoShop != null)
                {
                    try
                    {
                        if (ammoShop.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(ammoShop.gameObject);
                        }
                    }
                    catch { }
                    ammoShop = null;
                }
            }
            catch { }
        }
    }
}

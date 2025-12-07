// ============================================================================
// Utilities.cs - 工具方法
// ============================================================================
// 模块说明：
//   提供 BossRush 模组的通用工具方法，包括：
//   - 弹药商店创建和管理
//   - 其他辅助功能
//   
// 弹药商店：
//   在无间炼狱模式下提供弹药购买功能，包含常见弹药类型。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov.Economy;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 工具方法模块
    /// </summary>
    public partial class ModBehaviour
    {
        /// <summary>
        /// 确保弹药商店已创建
        /// <para>在无间炼狱模式下提供弹药购买功能</para>
        /// </summary>
        private void EnsureAmmoShop_Utilities()
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
                    var fMerchant = typeof(StockShop).GetField("merchantID", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (fMerchant != null)
                    {
                        fMerchant.SetValue(ammoShop, "BossRushAmmo");
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
                    612, 613, 615, 616, 698,
                    603, 604, 606, 607, 694,
                    594, 595, 597, 598, 691,
                    640, 708, 709, 710,
                    630, 631, 633, 634, 707,
                    621, 622, 700, 701, 702,
                    650, 1162, 918, 944, 1262,
                    326,
                    23, 24, 67, 66, 942, 660, 933, 941, 10, 17, 16, 15
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
                    var fItems = typeof(StockShop).GetField("itemInstances", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
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
    }
}

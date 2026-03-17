using System;
using UnityEngine;
using Duckov.Economy;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 商人

        /// <summary>Mode F 商人 Other 商店的固定商品列表</summary>
        private static readonly int[] modeFMerchantOtherItemIds = new int[]
        {
            FoldableCoverPackConfig.TYPE_ID,         // 500037 折叠掩体包
            ReinforcedRoadblockPackConfig.TYPE_ID,   // 500038 加固路障包
            BarbedWirePackConfig.TYPE_ID,             // 500039 阻滞铁丝网包
            EmergencyRepairSprayConfig.TYPE_ID        // 500040 应急维修喷剂
        };

        /// <summary>
        /// 将 Mode F 物品注入到 Mode E 商人的 Other 商店
        /// 在 Mode F 激活时，商人 Other 商店额外包含工事包和喷剂
        /// </summary>
        internal static int TryInjectModeFItemsIntoMerchantShop(StockShop shop)
        {
            try
            {
                ModBehaviour inst = ModBehaviour.Instance;
                if (inst == null || !inst.modeFActive || shop == null || shop.entries == null)
                {
                    return 0;
                }

                int addedCount = 0;
                for (int i = 0; i < modeFMerchantOtherItemIds.Length; i++)
                {
                    int typeId = modeFMerchantOtherItemIds[i];

                    // 检查是否已存在
                    bool exists = false;
                    foreach (StockShop.Entry entry in shop.entries)
                    {
                        if (entry != null && entry.ItemTypeID == typeId)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists) continue;

                    StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                    itemEntry.typeID = typeId;
                    itemEntry.maxStock = 10;
                    itemEntry.forceUnlock = true;
                    itemEntry.priceFactor = 1f;
                    itemEntry.possibility = 1f;
                    itemEntry.lockInDemo = false;

                    StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                    wrapped.CurrentStock = 10;
                    wrapped.Show = true;

                    shop.entries.Add(wrapped);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    ModBehaviour.DevLog("[ModeF] 商人 Other 商店注入 " + addedCount + " 个 Mode F 物品");
                }

                return addedCount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [ERROR] TryInjectModeFItemsIntoMerchantShop 失败: " + e.Message);
                return 0;
            }
        }

        #endregion
    }
}

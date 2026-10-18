// ============================================================================
// NPCShopSystemOfficialShopHooks.cs - NPC 商店对官方 StockShop 的身份、存档回调与补货计时处理
// ============================================================================
// 2026-10-18 从 NPCShopSystem.cs 原样提取（大文件行数预算，AGENTS §4.15），行为逐字不变。
// 契约见 docs/contracts.md §7.1「NPC 商店」。
// ============================================================================

using System;
using System.Reflection;
using Duckov.Economy;

namespace BossRush
{
    public static partial class NPCShopSystem
    {
        /// <summary>
        /// 摘掉官方 StockShop.Awake 挂的存档回调（OnCollectSaveData += Save、OnSetFile += Load）。
        /// 库存只记在好感存档里，官方 Save 只会往存档写一条 Mod 不读的 StockShop_BossRushNpc_* 键（审查 CR-2026-10-18-012）。
        /// 官方 OnDestroy 再退订一次是空操作。反射找不到方法时只记日志，退回原行为（merchantID 已换成 Mod 命名空间）。
        /// </summary>
        private static void DetachOfficialShopSaveHooks()
        {
            if (currentShop == null) return;
            try
            {
                if (officialShopSaveMethod == null)
                    officialShopSaveMethod = typeof(StockShop).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (officialShopLoadMethod == null)
                    officialShopLoadMethod = typeof(StockShop).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (officialShopSaveMethod == null || officialShopLoadMethod == null)
                {
                    ModBehaviour.DevLog("[NPCShop] [WARNING] StockShop.Save / Load 反射缺失，官方存档回调未摘除");
                    return;
                }
                Saves.SavesSystem.OnCollectSaveData -= (Action)Delegate.CreateDelegate(typeof(Action), currentShop, officialShopSaveMethod);
                Saves.SavesSystem.OnSetFile -= (Action)Delegate.CreateDelegate(typeof(Action), currentShop, officialShopLoadMethod);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 摘除官方商店存档回调失败: " + e.Message);
            }
        }

        private static MethodInfo officialShopSaveMethod;
        private static MethodInfo officialShopLoadMethod;

        /// <summary>把官方 merchantID 换成本 NPC 的命名空间并回读核对；失败只记日志（库存照常由本系统管理）。</summary>
        private static void ConfigureShopIdentity(string npcId)
        {
            try
            {
                var fMerchant = BossRushEagerReflectionCache.StockShop_MerchantID;
                if (fMerchant == null || currentShop == null)
                {
                    ModBehaviour.DevLog("[NPCShop] [WARNING] StockShop.merchantID 反射字段缺失");
                    return;
                }

                string merchantId = MerchantIdPrefix + npcId;
                fMerchant.SetValue(currentShop, merchantId);
                if (!string.Equals(currentShop.MerchantID, merchantId, StringComparison.Ordinal))
                {
                    ModBehaviour.DevLog("[NPCShop] [WARNING] merchantID 设置回读失败: " + merchantId);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 设置 merchantID 失败: " + e.Message);
            }
        }

        /// <summary>把官方补货计时推到很远的将来，ShowUI 里的 RefreshIfNeeded 就不会把存档库存冲回满。</summary>
        private static void SuppressOfficialRestock()
        {
            try
            {
                var fRefreshAfter = BossRushEagerReflectionCache.StockShop_RefreshAfterTimeSpan;
                var fLastRefreshed = BossRushEagerReflectionCache.StockShop_LastTimeRefreshedStock;
                if (fRefreshAfter == null || fLastRefreshed == null || currentShop == null)
                {
                    ModBehaviour.DevLog("[NPCShop] [WARNING] 官方补货计时反射字段缺失，开店会补满库存");
                    return;
                }

                fRefreshAfter.SetValue(currentShop, OfficialRefreshSuppressedTicks);
                fLastRefreshed.SetValue(currentShop, DateTime.UtcNow.ToBinary());
                // 官方 Start 在 refreshStockOnStart 为真时也会补满；默认就是 false，这里明确钉住。
                var fRefreshOnStart = BossRushEagerReflectionCache.StockShop_RefreshStockOnStart;
                if (fRefreshOnStart != null) fRefreshOnStart.SetValue(currentShop, false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCShop] [WARNING] 压住官方补货失败: " + e.Message);
            }
        }
    }
}

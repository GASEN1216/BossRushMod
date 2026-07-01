// ============================================================================
// StockShopGetItemInstanceDirectPatch.cs
// ============================================================================
// 功能：
//   修复延迟注入商店条目后打开 UI 崩溃的问题（NullReferenceException）。
//
// 问题根源：
//   游戏原版 StockShop.Start() 调用 CacheItemInstances() 异步缓存所有 entries 的物品实例到 itemInstances 字典。
//   BossRush 模组在性能优化后将商店注入改为延迟执行（IntegrationDeferredBootstrap），
//   导致注入的条目在 Start() 之后才添加，永远不会被 CacheItemInstances() 缓存。
//   当玩家打开商店 UI 时，StockShopItemEntry.Setup() 调用 GetItemInstanceDirect(typeID) 返回 null，
//   接下来访问 item.StackCount 时触发 NullReferenceException，导致 UI 无法显示。
//
// 解决方案：
//   Harmony Prefix 拦截 GetItemInstanceDirect(typeID)，检查 itemInstances 字典：
//   - 若 typeID 已缓存，放行让原版方法返回
//   - 若 typeID 未缓存（延迟注入的条目），立即同步实例化并缓存，然后放行
//   这样无论注入时机如何，GetItemInstanceDirect 永远不会返回 null。
//
// 性能影响：
//   - 原生条目（Start时已缓存）：零开销（字典命中，直接放行）
//   - 延迟注入条目：首次访问时同步实例化（一次性开销，后续访问命中缓存）
//   - 实例化使用 InstantiateSync，与游戏原版异步缓存逻辑一致
//
// 兼容性：
//   - 与 ModeEBulletShopUIDisplayPatch (Postfix) 兼容：Prefix 先缓存，Postfix 后修改 StackCount
//   - 覆盖所有延迟注入场景：船票、日志、勋章、砖石、邀请函、营旗、收发器等
// ============================================================================

using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemStatsSystem;
using Duckov.Economy;

namespace BossRush.Patches.Economy
{
    [HarmonyPatch(typeof(StockShop), "GetItemInstanceDirect")]
    public static class StockShopGetItemInstanceDirectPatch
    {
        [HarmonyPrefix]
        public static void Prefix(StockShop __instance, int typeID)
        {
            try
            {
                // 使用反射获取 itemInstances 字典
                var fItems = ReflectionCache.StockShop_ItemInstances;
                if (fItems == null)
                {
                    return;
                }

                var dict = fItems.GetValue(__instance) as Dictionary<int, Item>;
                if (dict == null)
                {
                    // 字典不存在，创建并设置（极端边界情况）
                    dict = new Dictionary<int, Item>();
                    fItems.SetValue(__instance, dict);
                }

                // 若 typeID 已缓存，无需操作（常见路径：原生商品）
                if (dict.ContainsKey(typeID))
                {
                    return;
                }

                // typeID 未缓存（延迟注入的条目），立即同步实例化并缓存
                Item item = ItemAssetsCollection.InstantiateSync(typeID);
                if (item != null)
                {
                    dict[typeID] = item;
                }
            }
            catch (Exception e)
            {
                // 防御性：即使缓存失败，也不影响原版方法执行（返回 null 由调用方处理）
                ModBehaviour.DevLog("[BossRush] [WARNING] StockShopGetItemInstanceDirectPatch 执行失败: TypeID=" + typeID + ", " + e.Message);
            }
        }
    }
}

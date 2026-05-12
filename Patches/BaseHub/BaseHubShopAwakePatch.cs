// ============================================================================
// BaseHubShopAwakePatch.cs - 基地商店 Awake 注入 Patch
// ============================================================================
// 说明：Patch StockShop.Awake，在目标售货机实例完成原版初始化后，
//       直接向该实例注入 BossRush 商品
// ============================================================================

using HarmonyLib;
using Duckov.Economy;

namespace BossRush
{
    [HarmonyPatch(typeof(StockShop), "Awake")]
    public static class BossRushBaseHubShopAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix(StockShop __instance)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || __instance == null || !inst.IsBaseHubNormalMerchantShop(__instance))
            {
                return;
            }

            int injectedCount = inst.TryInjectAllBossRushItemsIntoShop(__instance);
            if (injectedCount > 0)
            {
                ModBehaviour.DevLog("[BossRush] HarmonyPatch: 商店实例注入完成，新增条目数=" + injectedCount + ", merchantID=" + __instance.MerchantID);
            }
        }
    }
}

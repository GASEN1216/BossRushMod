// ============================================================================
// BossRushHarmonyPatch.cs - Base hub injection patches
// ============================================================================
// 说明：
//   1. Patch StockShop.Awake：在目标售货机实例完成原版初始化后，直接向该实例注入 BossRush 商品
//   2. Patch InteractableBase.Start：在目标船点实例完成原版初始化后，直接向该实例注入 BossRush 交互
//   3. 保留现有场景扫描逻辑作为兜底，处理热加载和已存在对象
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

    [HarmonyPatch(typeof(InteractableBase), "Start")]
    public static class BossRushBaseHubInteractableStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InteractableBase __instance)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || __instance == null)
            {
                return;
            }

            if (inst.TryInjectBaseHubBoatInteractable(__instance))
            {
                string sceneName = string.Empty;
                try { sceneName = __instance.gameObject.scene.name; } catch { }
                ModBehaviour.DevLog("[BossRush] HarmonyPatch: 已向船点交互实例注入 BossRush 选项, scene=" + sceneName + ", name=" + __instance.gameObject.name);
            }
        }
    }
}

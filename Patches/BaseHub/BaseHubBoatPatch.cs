// ============================================================================
// BaseHubBoatPatch.cs - 基地船点交互注入 Patch
// ============================================================================
// 说明：Patch InteractableBase.Start，在目标船点实例完成原版初始化后，
//       直接向该实例注入 BossRush 交互选项
// ============================================================================

using HarmonyLib;
using Duckov;

namespace BossRush
{
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

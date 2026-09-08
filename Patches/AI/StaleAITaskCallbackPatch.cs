using System;
using HarmonyLib;
using NodeCanvas.Tasks.Actions;

namespace BossRush
{
    // AIMainBrain 延迟交付搜索结果时，任务所属角色可能已销毁。
    // 官方回调先解引用 agent.gameObject；在这之前检查 Unity 对象生命周期。
    [HarmonyPatch(typeof(SearchEnemyAround), "OnSearchFinished",
        new Type[] { typeof(DamageReceiver), typeof(InteractablePickup) })]
    internal static class StaleAISearchCallbackPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(SearchEnemyAround __instance)
        {
            return __instance != null && __instance.agent != null && __instance.agent.gameObject != null;
        }
    }

    [HarmonyPatch(typeof(CheckObsticle), "OnCheckFinished", new Type[] { typeof(bool) })]
    internal static class StaleAIObstacleCallbackPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(CheckObsticle __instance)
        {
            return __instance != null && __instance.agent != null && __instance.agent.gameObject != null;
        }
    }
}

// ============================================================================
// TombLootboxPatch.cs - 死神系统墓地战利品通知 Patch
// ============================================================================
// 说明：Patch InteractableLootbox.CreateFromItem，在墓地战利品创建时通知死神系统
// ============================================================================

using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using Duckov;

namespace BossRush
{
    [HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
    public static class BossRushDeathWraithTombLootboxPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            Item item,
            Vector3 position,
            Quaternion rotation,
            bool moveToMainScene,
            InteractableLootbox prefab,
            bool filterDontDropOnDead,
            InteractableLootbox __result)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || __result == null)
            {
                return;
            }

            inst.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(
                __result,
                item,
                position,
                prefab);
        }
    }
}

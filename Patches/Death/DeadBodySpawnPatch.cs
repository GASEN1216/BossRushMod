// ============================================================================
// DeadBodySpawnPatch.cs - 死神系统尸体生成通知 Patch
// ============================================================================
// 说明：Patch DeadBodyManager.SpawnDeadBody，在尸体生成时通知死神系统
// ============================================================================

using HarmonyLib;
using Duckov;

namespace BossRush
{
    [HarmonyPatch(typeof(DeadBodyManager), "SpawnDeadBody")]
    public static class BossRushDeathWraithDeadBodySpawnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(DeadBodyManager.DeathInfo info)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || info == null)
            {
                return;
            }

            inst.NotifyOriginalDeadBodySpawnRequested_DeathWraith(info);
        }
    }
}

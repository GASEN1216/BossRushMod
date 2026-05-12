// ============================================================================
// DeadBodyTouchedPatch.cs - 死神系统尸体触碰通知 Patch
// ============================================================================
// 说明：Patch DeadBodyManager.NotifyDeadbodyTouched，在尸体被触碰时通知死神系统
// ============================================================================

using HarmonyLib;
using Duckov;

namespace BossRush
{
    [HarmonyPatch(typeof(DeadBodyManager), "NotifyDeadbodyTouched")]
    public static class BossRushDeathWraithDeadBodyTouchedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DeadBodyManager.DeathInfo info)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || info == null)
            {
                return;
            }

            inst.NotifyOriginalDeadBodyTouched_DeathWraith(info);
        }
    }
}

using HarmonyLib;
using Duckov;

namespace BossRush
{
    [HarmonyPatch(typeof(DeadBodyManager), "AppendDeathInfo")]
    public static class BossRushDeathWraithDeadBodyAppendPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DeadBodyManager.DeathInfo deathInfo)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || deathInfo == null)
            {
                return;
            }

            inst.NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith(deathInfo);
        }
    }
}

using HarmonyLib;

namespace BossRush
{
    [HarmonyPatch(typeof(Projectile), "Init", new System.Type[] { typeof(ProjectileContext) })]
    internal static class ZombieModeRewardProjectileInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Projectile __instance)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst != null)
            {
                inst.ApplyZombieModeProjectileRewardEffects(__instance);
            }
        }
    }
}

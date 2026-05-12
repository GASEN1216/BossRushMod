// ============================================================================
// ProjectileHalfObstaclePatch.cs - 枪械半掩体修复 Patch
// ============================================================================
// 说明：Projectile.Init 后缀，修复主角射击时半掩体穿透判定
// ============================================================================

using HarmonyLib;

namespace BossRush
{
    // Projectile 有两个 Init 重载；必须显式绑定带 ProjectileContext 的版本，
    // 否则 PatchAll() 会因为重载歧义直接失败，枪械半掩体修复也不会生效。
    [HarmonyPatch(typeof(Projectile), "Init", new System.Type[] { typeof(ProjectileContext) })]
    public static class BossRushProjectileInitPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Projectile __instance)
        {
            if (__instance == null || __instance.context.fromCharacter == null || !__instance.context.fromCharacter.IsMainCharacter)
            {
                return;
            }

            if (!__instance.context.ignoreHalfObsticle || __instance.damagedObjects == null)
            {
                return;
            }

            UnityEngine.GameObject[] nearByHalfObstacles;
            try
            {
                nearByHalfObstacles = __instance.context.fromCharacter.GetNearByHalfObsticles();
            }
            catch
            {
                return;
            }

            if (nearByHalfObstacles == null || nearByHalfObstacles.Length == 0)
            {
                return;
            }

            for (int i = 0; i < nearByHalfObstacles.Length; i++)
            {
                UnityEngine.GameObject obstacle = nearByHalfObstacles[i];
                if (obstacle != null && !__instance.damagedObjects.Contains(obstacle))
                {
                    __instance.damagedObjects.Add(obstacle);
                }
            }
        }
    }
}

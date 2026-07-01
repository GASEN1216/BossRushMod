// ============================================================================
// BossLethalHealthProtectionPatch.cs - custom Boss lethal-health guard patch
// ============================================================================
// 作用：
//   - 修复龙裔遗族“复活”与焚天龙皇“孩儿护我”都挂在 Health.OnHurtEvent 上，
//     但原版 Health.Hurt() 会先进入死亡分支、最后才触发 OnHurtEvent 的时序问题。
//   - 在 Health.Hurt() 内部写入致死 CurrentHealth 之前，针对仍有保命机制的自定义 Boss
//     先把血量钳回到触发阈值，让后续 OnHurtEvent 还能正常启动二命/护驾逻辑。
// ============================================================================

using System;
using HarmonyLib;

namespace BossRush
{
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class BossRushHealthHurtContextPatch
    {
        [ThreadStatic]
        private static int hurtDepth;

        internal static bool IsInsideHurt
        {
            get { return hurtDepth > 0; }
        }

        [HarmonyPrefix]
        private static void Prefix()
        {
            hurtDepth++;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            if (hurtDepth > 0)
            {
                hurtDepth--;
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Health), "CurrentHealth", MethodType.Setter)]
    internal static class BossRushBossLethalHealthProtectionPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Health __instance, ref float value)
        {
            if (!BossRushHealthHurtContextPatch.IsInsideHurt ||
                __instance == null ||
                value > 0f)
            {
                return;
            }

            if (TryClampDragonKing(__instance, ref value))
            {
                return;
            }

            TryClampDragonDescendant(__instance, ref value);
        }

        private static bool TryClampDragonKing(Health health, ref float value)
        {
            DragonKingAbilityController controller = health.GetComponent<DragonKingAbilityController>();
            if (controller == null)
            {
                controller = health.GetComponentInParent<DragonKingAbilityController>();
            }

            if (controller == null || !controller.ShouldClampLethalHealthDuringHurt())
            {
                return false;
            }

            value = DragonKingConfig.ChildProtectionHealthThreshold;
            ModBehaviour.DevLog("[DragonKing] 拦截致死伤害，保留孩儿护我触发窗口");
            return true;
        }

        private static bool TryClampDragonDescendant(Health health, ref float value)
        {
            DragonDescendantAbilityController controller = health.GetComponent<DragonDescendantAbilityController>();
            if (controller == null)
            {
                controller = health.GetComponentInParent<DragonDescendantAbilityController>();
            }

            if (controller == null || !controller.ShouldClampLethalHealthDuringHurt())
            {
                return false;
            }

            value = 1f;
            ModBehaviour.DevLog("[DragonDescendant] 拦截致死伤害，保留复活触发窗口");
            return true;
        }
    }
}

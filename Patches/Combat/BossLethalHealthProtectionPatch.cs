// ============================================================================
// BossLethalHealthProtectionPatch.cs - custom Boss lethal-health guard patch
// ============================================================================
// 作用：
//   - 修复龙裔遗族“复活”、焚天龙皇“孩儿护我”和逆鳞图腾都依赖 OnHurt
//     触发窗口，但原版 Health.Hurt() 会先进入死亡分支、最后才触发 OnHurt 的时序问题。
//   - 在 Health.Hurt() 内部写入致死 CurrentHealth 之前，针对仍有保命机制的目标
//     先把血量钳回到触发阈值，让后续 OnHurt / OnHurtEvent 还能正常启动保命逻辑。
// ============================================================================

using System;
using HarmonyLib;

namespace BossRush
{
    [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
    internal static class BossRushHealthHurtContextPatch
    {
        // hurtDepth 所有权改为逐调用 Harmony __state：
        // 只有本次 Prefix 实际执行了 hurtDepth++ 时，Finalizer 才递减。
        private static int hurtDepth;

        // 限频告警：Mode G staging 查询异常时最多每 5 秒打一条，OnHurt 热路径零分配
        private static float _lastStagingQueryFaultLogTime = -1000f;
        private const float StagingQueryFaultLogIntervalSeconds = 5f;

        internal static bool IsInsideHurt
        {
            get { return hurtDepth > 0; }
        }

        [HarmonyPrefix]
        private static bool Prefix(Health __instance, ref bool __result, ref bool __state)
        {
            __state = false;

            // Mode G staging 屏障（加法分支）：先读静态 active bool 快速早返，
            // 未激活时零分配；命中已登记 clone/exact 身份时返回 false 并由 Felix 侧记 blocked-hit。
            // 静态 active false/查询异常/非 staging 时继续现有 ReverseScale/深度逻辑。
            if (IsModeGStagingBarrierArmed())
            {
                bool stagingBlocked = false;
                try
                {
                    stagingBlocked = ModeGRuntimeGates.IsModeGStagingHealthBlocked(__instance);
                }
                catch (Exception stagingQueryEx)
                {
                    stagingBlocked = false;
                    LogStagingQueryFaultLimited(stagingQueryEx);
                }

                if (stagingBlocked)
                {
                    __result = false;
                    return false;
                }
            }

            if (ReverseScaleAbilityManager.IsPostTriggerInvincible(__instance))
            {
                __result = false;
                return false;
            }

            hurtDepth++;
            __state = true;
            return true;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __state)
        {
            // 只有本次调用实际 hurtDepth++ 过（Prefix 走到 __state = true）才递减
            if (__state && hurtDepth > 0)
            {
                hurtDepth--;
            }

            return __exception;
        }

        /// <summary>
        /// Mode G staging 屏障静态开关快速早返（no-throw；异常视为未激活）。
        /// </summary>
        private static bool IsModeGStagingBarrierArmed()
        {
            try
            {
                return ModeGRuntimeGates.IsModeGStagingBarrierActive;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 限频打印 staging 查询异常（5 秒一条，避免 OnHurt 热路径日志风暴）。
        /// </summary>
        private static void LogStagingQueryFaultLimited(Exception ex)
        {
            try
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now - _lastStagingQueryFaultLogTime >= StagingQueryFaultLogIntervalSeconds)
                {
                    _lastStagingQueryFaultLogTime = now;
                    ModBehaviour.DevLog("[ModeG] [WARNING] Hurt staging 查询异常，继续现有保护逻辑: " + ex.Message);
                }
            }
            catch { }
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

            if (TryClampReverseScale(__instance, ref value))
            {
                return;
            }

            if (TryClampDragonKing(__instance, ref value))
            {
                return;
            }

            TryClampDragonDescendant(__instance, ref value);
        }

        private static bool TryClampReverseScale(Health health, ref float value)
        {
            if (!ReverseScaleAbilityManager.TryPrepareLethalProtectionDuringHurt(health))
            {
                return false;
            }

            value = ReverseScaleConfig.Instance.TriggerHealthThreshold;
            ModBehaviour.DevLog("[ReverseScale] 拦截致死伤害，保留逆鳞触发窗口");
            return true;
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

// ============================================================================
// ModeHHarmonyPatches.cs - ERROR 完整互换的部件二：解冻玩家身体
// ============================================================================
// 设计提案 §17.6.5 冻结：Mode H 首发**只允许**这两个 Harmony postfix，
// 分别打在 CA_ControlOtherCharacter.CanMove 与 CanRun 上。
//
// 原版把玩家身体在受控期间完全冻结（CanMove/CanRun/CanUseHand/CanControlAim
// 全部恒 false），这是“完整互换”必须跨过的唯一障碍。
//
// **明确不打补丁的两个方法**：CanUseHand 与 CanControlAim 保持原版 false。
// 这不是妥协——它让看台身体在引擎层面就无法使用武器、无法瞄准、无法触发手部交互，
// 因此不需要 Mode H 再写一层“禁止使用玩家装备”的自定义拦截。
//
// 补丁约束（逐条对应 §17.6.5）：
// - 都是 [HarmonyPostfix]，只读改 ref bool __result，不改变原版执行流，不返回 false；
// - 快路径先读静态 ModeHRuntimeGates.IsModeHStandInActive（零分配 bool），
//   未激活直接返回，热路径不建集合、不分配；
// - 激活后再做 O(1) 身份查询：__instance.gameObject 必须是本场已登记的玩家身体；
// - 全程 no-throw，任何异常 fail-closed 到原版 false（身体保持冻结），
//   并按 5 秒限频告警，绝不打断宿主动作流程。
//
// 禁止扩大到全局 team、输入、索敌或死亡逻辑；额外死亡掉落抑制仍只走
// Patches/Combat/CharacterOnDeadPatch.cs 的既有扩展。
// ============================================================================

using System;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>ERROR 互换期间解冻看台身体的移动能力（§17.6.5 部件二）。</summary>
    [HarmonyPatch(typeof(CA_ControlOtherCharacter), "CanMove")]
    public static class ModeHStandInCanMovePatch
    {
        /// <summary>只在本场已登记的玩家身体上把 false 改为 true。</summary>
        [HarmonyPostfix]
        public static void Postfix(CA_ControlOtherCharacter __instance, ref bool __result)
        {
            if (!ModeHStandInPatchGate.ShouldUnfreeze(__instance)) return;
            __result = true;
        }
    }

    /// <summary>同上，允许“莽攻”类底色在看台表现出快步（§17.6.5 部件二）。</summary>
    [HarmonyPatch(typeof(CA_ControlOtherCharacter), "CanRun")]
    public static class ModeHStandInCanRunPatch
    {
        /// <summary>只在本场已登记的玩家身体上把 false 改为 true。</summary>
        [HarmonyPostfix]
        public static void Postfix(CA_ControlOtherCharacter __instance, ref bool __result)
        {
            if (!ModeHStandInPatchGate.ShouldUnfreeze(__instance)) return;
            __result = true;
        }
    }

    /// <summary>
    /// 两个 postfix 共用的零分配门。放在这里而不是内联，是为了让
    /// “静态快门 -> O(1) 身份查询 -> no-throw fail-closed”只有一份实现。
    /// </summary>
    internal static class ModeHStandInPatchGate
    {
        private static float _lastWarnTime;

        /// <summary>
        /// 是否应当解冻。fail-closed：任何不确定情形都返回 false，
        /// 让原版的 false 结果原样通过（身体保持冻结）。
        /// </summary>
        internal static bool ShouldUnfreeze(CA_ControlOtherCharacter instance)
        {
            // 快路径：静态 bool，未激活直接返回，不触碰任何引用
            if (!ModeHRuntimeGates.IsModeHStandInActive) return false;
            if (instance == null) return false;

            try
            {
                GameObject go = instance.gameObject;
                if (go == null) return false;
                return ModeHRuntimeGates.IsModeHStandInBody(go.GetInstanceID());
            }
            catch (Exception e)
            {
                WarnThrottled(e.GetType().Name);
                return false;
            }
        }

        private static void WarnThrottled(string reason)
        {
            float now;
            try { now = Time.realtimeSinceStartup; }
            catch (Exception)
            {
                // 非主线程或引擎未就绪：放弃告警，绝不影响补丁结论
                return;
            }
            if (now - _lastWarnTime < ModeHConfig.DiagnosticLogIntervalSeconds) return;
            _lastWarnTime = now;
            ModBehaviour.CriticalLog("[ModeH] 看台解冻补丁异常，已 fail-closed: " + reason);
        }
    }
}

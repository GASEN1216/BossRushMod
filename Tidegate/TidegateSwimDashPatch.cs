using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方游泳仍允许 Dash；只在潮闸镇主角游泳时拒绝开始动作，先于耗体力和停止当前动作。</summary>
    [HarmonyPatch(typeof(CharacterMainControl), "StartAction", new[] { typeof(CharacterActionBase) })]
    internal static class TidegateSwimDashPatch
    {
        internal static bool BlocksDash(CharacterMainControl character)
        {
            return character != null && character == CharacterMainControl.Main
                && character.Swimming && TidegateRaidScene.IsActiveScene;
        }

        private static bool Prefix(CharacterMainControl __instance, CharacterActionBase newAction, ref bool __result)
        {
            if (!(newAction is CA_Dash) || !BlocksDash(__instance)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>岸上翻滚入水时，在官方更新游泳状态的同一帧结束翻滚；OnStop 会留下冲量，需同步清掉。</summary>
    [HarmonyPatch(typeof(CharacterMainControl), "UpdateSwim")]
    internal static class TidegateSwimDashStopPatch
    {
        private static void Postfix(CharacterMainControl __instance)
        {
            if (!TidegateSwimDashPatch.BlocksDash(__instance) || !__instance.Dashing || __instance.dashAction == null) return;
            if (__instance.dashAction.StopAction()) __instance.SetForceMoveVelocity(Vector3.zero);
        }
    }
}

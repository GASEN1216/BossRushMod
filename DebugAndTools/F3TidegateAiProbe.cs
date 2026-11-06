#if BOSSRUSH_DEV
using HarmonyLib;
using NodeCanvas.BehaviourTrees;

namespace BossRush
{
    // 仅在 F3 潮闸镇采样窗读时钟。两项是各自入口的含子调用时间，不可与脚本线程时间相加。
    [HarmonyPatch(typeof(AICharacterController), "Update")]
    internal static class F3TidegateControllerProbe
    {
        [HarmonyPrefix] private static void Prefix(out long __state) { __state = TidegateFrameProbe.EnterExternal(); }
        [HarmonyPostfix] private static void Postfix(long __state) { TidegateFrameProbe.LeaveExternal(TidegateTickPart.AiUpdate, __state); }
    }

    [HarmonyPatch(typeof(BehaviourTree), "OnGraphUpdate")]
    internal static class F3TidegateBehaviourProbe
    {
        [HarmonyPrefix] private static void Prefix(out long __state) { __state = TidegateFrameProbe.EnterExternal(); }
        [HarmonyPostfix] private static void Postfix(long __state) { TidegateFrameProbe.LeaveExternal(TidegateTickPart.BehaviourTrees, __state); }
    }
}
#endif

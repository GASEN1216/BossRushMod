// ============================================================================
// CharacterOnDeadPatch.cs - 角色死亡前缀 Patch
// ============================================================================
// 说明：在角色死亡时处理额外追加掉落（霜之哀伤蓝色 Boss、噬魂挽歌）
// Mode G 门控（加法分支）：Mode G run-scoped 额外死亡掉落抑制注册表命中时
// 跳过两个 handler；查询默认 false、no-throw、异常 fail-open=false。
// ============================================================================

using System;
using HarmonyLib;

namespace BossRush
{
    [HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
    public static class BossRushCharacterOnDeadPatch
    {
        // 限频告警：抑制查询异常时最多每 5 秒打一条，热路径零分配
        private static float _lastSuppressionQueryFaultLogTime = -1000f;
        private const float SuppressionQueryFaultLogIntervalSeconds = 5f;

        [HarmonyPrefix]
        public static void Prefix(CharacterMainControl __instance)
        {
            // Mode G 额外死亡掉落抑制（加法分支）：
            // 先读静态 bool 快速早返，未激活时不建集合、不分配；
            // 激活后再做 staging preset/已登记 Character 引用身份 O(1) 查询。
            // 任何异常 fail-open=false（让原两个 handler 继续），绝不能打断宿主 OnDead。
            // Mode H 使用同形的引用身份查询（设计提案 §19.5）：clone preset 在创建调用之前登记，
            // 角色引用在创建返回后补登记；命中后同样只跳过本 Mod 的两个额外掉落 handler。
            if (IsModeGSuppressionArmed() || ModeHDeathSuppressionRegistry.IsSuppressionArmed)
            {
                bool suppressed = false;
                try
                {
                    Health deadHealth = null;
                    try
                    {
                        deadHealth = __instance != null ? __instance.Health : null;
                    }
                    catch
                    {
                        deadHealth = null;
                    }

                    suppressed = ModeGRuntimeGates.IsModeGOnDeadSuppressionActive(deadHealth)
                        || ModeHDeathSuppressionRegistry.IsModeHOnDeadSuppressionActive(deadHealth);
                }
                catch (Exception suppressionQueryEx)
                {
                    suppressed = false;
                    LogSuppressionQueryFaultLimited(suppressionQueryEx);
                }

                if (suppressed)
                {
                    // 命中 Mode G 或 Mode H 的 staging preset/已登记 Character 身份：
                    // 整段跳过霜之哀伤与女巫镰刀两个额外掉落 handler；
                    // 原版 CharacterMainControl.OnDead 与 Health.OnDead 继续执行
                    return;
                }
            }

            FrostmourneBlueBossDropHandler.TryHandleBlueBossDeath(__instance);
            PhantomWitchScytheBossDropHandler.TryHandlePhantomWitchDeath(__instance);
        }

        /// <summary>
        /// 静态抑制开关快速早返（no-throw；异常视为未激活）。
        /// </summary>
        private static bool IsModeGSuppressionArmed()
        {
            try
            {
                return ModeGRuntimeGates.IsModeGSuppressionActive;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 限频打印抑制查询异常（5 秒一条，避免热路径日志风暴）。
        /// </summary>
        private static void LogSuppressionQueryFaultLimited(Exception ex)
        {
            try
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now - _lastSuppressionQueryFaultLogTime >= SuppressionQueryFaultLogIntervalSeconds)
                {
                    _lastSuppressionQueryFaultLogTime = now;
                    ModBehaviour.DevLog("[ModeG] [WARNING] OnDead 抑制查询异常，fail-open=false 继续原 handler: " + ex.Message);
                }
            }
            catch { }
        }
    }
}

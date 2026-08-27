// ============================================================================
// SafeRuntime.cs - guarded runtime operation helpers
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static class SafeRuntime
    {
        private static readonly HashSet<string> loggedWarningLabels = new HashSet<string>();

        public static void Run(string label, Action action)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception e)
            {
                LogWarningOnce(label, e);
            }
        }

        public static bool Try(string label, Func<bool> action, bool fallback = false)
        {
            if (action == null)
            {
                return fallback;
            }

            try
            {
                return action();
            }
            catch (Exception e)
            {
                LogWarningOnce(label, e);
                return fallback;
            }
        }

        public static void ResetStaticCaches()
        {
            loggedWarningLabels.Clear();
        }

        private static void LogWarningOnce(string label, Exception e)
        {
            string safeLabel = string.IsNullOrEmpty(label) ? "runtime operation" : label;
            if (!loggedWarningLabels.Add(safeLabel))
            {
                return;
            }

            // 被 SafeRuntime 吞掉的异常在正式构建里原本完全不可见（DevLog 被编译删除），
            // 这里改走 CriticalLog；本方法自身已按 label 去重，调用点均为清理/奖励/引导路径，不在热路径。
            ModBehaviour.CriticalLog(
                "safe-runtime-" + safeLabel,
                "[SafeRuntime] [WARNING] " + safeLabel + " failed: " + (e != null ? e.Message : "unknown"));
        }
    }
}

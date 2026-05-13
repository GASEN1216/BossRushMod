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

            ModBehaviour.DevLog("[SafeRuntime] [WARNING] " + safeLabel + " failed: " + (e != null ? e.Message : "unknown"));
        }
    }
}

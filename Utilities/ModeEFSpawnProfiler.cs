using UnityEngine;

namespace BossRush
{
    internal sealed class ModeEFSpawnProfiler
    {
        private readonly bool enabled;
        private readonly string scope;
        private readonly float startTime;
        private float lastCheckpointTime;
        private bool completed;

        public ModeEFSpawnProfiler(string scope, string detail = null)
        {
            enabled = ModBehaviour.DevModeEnabled && ModBehaviour.ModeEFSpawnProfilingEnabled;
            if (!enabled)
            {
                return;
            }

            this.scope = string.IsNullOrEmpty(detail) ? scope : scope + " [" + detail + "]";
            startTime = Time.realtimeSinceStartup;
            lastCheckpointTime = startTime;
            ModBehaviour.DevLog("[ModeE/F] [Profile] " + this.scope + " begin");
        }

        public void Mark(string stageName)
        {
            if (!enabled || completed)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            ModBehaviour.DevLog("[ModeE/F] [Profile] " + scope + " | " + stageName + ": +" +
                ((now - lastCheckpointTime) * 1000f).ToString("F1") + " ms");
            lastCheckpointTime = now;
        }

        public void Complete(string status = "completed")
        {
            if (!enabled || completed)
            {
                return;
            }

            completed = true;
            float now = Time.realtimeSinceStartup;
            ModBehaviour.DevLog("[ModeE/F] [Profile] " + scope + " | " + status + " | total=" +
                ((now - startTime) * 1000f).ToString("F1") + " ms");
        }
    }

}

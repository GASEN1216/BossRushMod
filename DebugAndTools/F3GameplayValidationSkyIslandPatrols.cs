using System;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private bool ValidateSkyIslandPatrols(out string metrics, out string reason)
        {
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { metrics = "session=null"; reason = "session_missing"; return false; }
            return session.ValidationPatrolRuntime(out metrics, out reason);
        }
    }
}

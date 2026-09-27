using UnityEngine;

namespace BossRush
{
    internal static class BossRushWaitCache
    {
        internal static readonly WaitForSeconds sharedWait01s = new WaitForSeconds(0.1f);

        internal static readonly WaitForSeconds sharedWait05s = new WaitForSeconds(0.5f);

        internal static readonly WaitForSeconds sharedWait1s = new WaitForSeconds(1f);
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>保留原 Integration 生命周期入口。</summary>
    public partial class ModBehaviour
    {
        private void InitializeFrostmourneSystem()
        {
            frostmourneRuntime.InitializeFrostmourneSystem();
        }

        private void SetupFrostmourneForScene(Scene scene)
        {
            frostmourneRuntime.SetupFrostmourneForScene(scene);
        }

        private void CleanupFrostmourneSystem()
        {
            frostmourneRuntime.CleanupFrostmourneSystem();
        }

        internal static WaitForSeconds FrostmourneSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }
    }
}

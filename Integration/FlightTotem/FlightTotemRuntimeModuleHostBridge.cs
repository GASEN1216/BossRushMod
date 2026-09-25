using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>保留原 Integration 生命周期入口，转发到飞行图腾运行时 owner。</summary>
    public partial class ModBehaviour
    {
        private void InitializeFlightTotemSystem()
        {
            flightTotemRuntime.InitializeFlightTotemSystem();
        }

        private void SetupFlightTotemForScene(Scene scene)
        {
            flightTotemRuntime.SetupFlightTotemForScene(scene);
        }

        private void CleanupFlightTotemSystem()
        {
            flightTotemRuntime.CleanupFlightTotemSystem();
        }

        internal static WaitForSeconds FlightTotemSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }
    }
}

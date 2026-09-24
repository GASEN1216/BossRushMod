using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>保留原 Integration 生命周期入口，并把配置器留在原工厂宿主。</summary>
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

        internal void InitializeFlightTotemItemFromRuntimeModule()
        {
            InitializeFlightTotemItem();
        }

        internal void InjectFlightTotemLocalizationFromRuntimeModule()
        {
            InjectFlightTotemLocalization();
        }

        internal static WaitForSeconds FlightTotemSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }
    }
}

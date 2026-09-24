using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>保留原 Integration 生命周期入口，并把配置器留在原工厂宿主。</summary>
    public partial class ModBehaviour
    {
        private void InitializeReverseScaleSystem()
        {
            reverseScaleRuntime.InitializeReverseScaleSystem();
        }

        private void SetupReverseScaleForScene(Scene scene)
        {
            reverseScaleRuntime.SetupReverseScaleForScene(scene);
        }

        private void CleanupReverseScaleSystem()
        {
            reverseScaleRuntime.CleanupReverseScaleSystem();
        }

        internal void InitializeReverseScaleItemFromRuntimeModule()
        {
            InitializeReverseScaleItem();
        }

        internal void InjectReverseScaleLocalizationFromRuntimeModule()
        {
            InjectReverseScaleLocalization();
        }

        internal static WaitForSeconds ReverseScaleSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }
    }
}
